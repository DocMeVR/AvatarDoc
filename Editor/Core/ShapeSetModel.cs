#if DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Pure reads of an entry's shape set rules: what they hold and at what, and what a mesh's scene weights capture into rows. No scene write, no asset write.</summary>
    public static class ShapeSetModel
    {
        /// <summary>Above this many shapes held by one rule, the cost is called out with ad.warn.shape_set_large.</summary>
        public const int LargeThreshold = 150;

        /// <summary>
        /// One rule per mesh this entry names, several rows for the same mesh acting as one: the merged rule is on if
        /// any of them is, and takes its value and its blink and viseme switch from the first one that is on (the
        /// first one at all when none is). A row with no mesh is left out.
        /// </summary>
        public static IEnumerable<ShapeSetRule> EffectiveRules(MenuEntry entry)
        {
            var byMesh = new Dictionary<SkinnedMeshRenderer, ShapeSetRule>();
            var order = new List<ShapeSetRule>();
            if (entry?.shapeSets == null) return order;
            foreach (var rule in entry.shapeSets)
            {
                if (rule == null || rule.mesh == null) continue;
                if (byMesh.TryGetValue(rule.mesh, out var merged))
                {
                    if (rule.On && !merged.On)
                    {
                        merged.zeroOthers = true;
                        merged.value = rule.value;
                        merged.holdTracked = rule.holdTracked;
                    }
                    continue;
                }
                merged = new ShapeSetRule { mesh = rule.mesh, zeroOthers = rule.On, value = rule.value, holdTracked = rule.holdTracked };
                byMesh[rule.mesh] = merged;
                order.Add(merged);
            }
            return order;
        }

        public static VRCAvatarDescriptor DescriptorOf(Transform avatarRoot) => avatarRoot != null ? avatarRoot.GetComponent<VRCAvatarDescriptor>() : null;

        /// <summary>
        /// Every (mesh, shape) the avatar descriptor itself reads for blinking or for lip sync. Eyelids count only
        /// while enableEyeLook is on and the eyelid type is Blendshapes; a mouth shape counts for
        /// LipSyncStyle.VisemeBlendShape (against VisemeBlendShapes) or LipSyncStyle.JawFlapBlendShape (against
        /// MouthOpenBlendShapeName), both on VisemeSkinnedMesh.
        /// </summary>
        public static (HashSet<(SkinnedMeshRenderer renderer, string shape)> eyelids, HashSet<(SkinnedMeshRenderer renderer, string shape)> mouth) TrackedShapes(VRCAvatarDescriptor descriptor)
        {
            var eyelids = new HashSet<(SkinnedMeshRenderer, string)>();
            var mouth = new HashSet<(SkinnedMeshRenderer, string)>();
            if (descriptor == null) return (eyelids, mouth);

            var eye = descriptor.customEyeLookSettings;
            if (descriptor.enableEyeLook && eye.eyelidType == VRCAvatarDescriptor.EyelidType.Blendshapes
                && eye.eyelidsSkinnedMesh != null && eye.eyelidsSkinnedMesh.sharedMesh != null && eye.eyelidsBlendshapes != null)
                foreach (var shapeIndex in eye.eyelidsBlendshapes)
                    if (shapeIndex >= 0 && shapeIndex < eye.eyelidsSkinnedMesh.sharedMesh.blendShapeCount)
                        eyelids.Add((eye.eyelidsSkinnedMesh, eye.eyelidsSkinnedMesh.sharedMesh.GetBlendShapeName(shapeIndex)));

            if (descriptor.lipSync == VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape && descriptor.VisemeSkinnedMesh != null && descriptor.VisemeBlendShapes != null)
                foreach (var shapeName in descriptor.VisemeBlendShapes)
                    if (!string.IsNullOrEmpty(shapeName)) mouth.Add((descriptor.VisemeSkinnedMesh, shapeName));
            if (descriptor.lipSync == VRC_AvatarDescriptor.LipSyncStyle.JawFlapBlendShape && descriptor.VisemeSkinnedMesh != null && !string.IsNullOrEmpty(descriptor.MouthOpenBlendShapeName))
                mouth.Add((descriptor.VisemeSkinnedMesh, descriptor.MouthOpenBlendShapeName));

            return (eyelids, mouth);
        }

        /// <summary>
        /// What a rule reads from its avatar besides the entry: the shapes the descriptor blinks and speaks with, and
        /// the shapes an included AvatarDoc Shapes piece drives. Kept on the tree (MenuTree.HeldContext) and the build's
        /// base state, so nothing walks the avatar on every refresh; the pieces are read only once a rule asks.
        /// </summary>
        public sealed class HeldContext
        {
            public static readonly HeldContext None = new HeldContext(null);

            public readonly Transform AvatarRoot;
            public readonly HashSet<(SkinnedMeshRenderer renderer, string shape)> Eyelids;
            public readonly HashSet<(SkinnedMeshRenderer renderer, string shape)> Mouth;
            HashSet<(SkinnedMeshRenderer, string)> pieceShapes;

            public HeldContext(Transform avatarRoot)
            {
                AvatarRoot = avatarRoot;
                (Eyelids, Mouth) = TrackedShapes(DescriptorOf(avatarRoot));
            }

            public bool IsTracked(SkinnedMeshRenderer renderer, string shape) => Eyelids.Contains((renderer, shape)) || Mouth.Contains((renderer, shape));

            /// <summary>Every (mesh, shape) an included AvatarDoc Shapes piece writes: its while-on rows and the local shapes its follow rows drive.</summary>
            public HashSet<(SkinnedMeshRenderer renderer, string shape)> PieceShapes => pieceShapes ?? (pieceShapes = ReadPieceShapes(AvatarRoot));

            static HashSet<(SkinnedMeshRenderer, string)> ReadPieceShapes(Transform avatarRoot)
            {
                var result = new HashSet<(SkinnedMeshRenderer, string)>();
#if DOCME_NDMF
                if (avatarRoot == null) return result;
                foreach (var piece in ShapesModel.Plan(avatarRoot).Included)
                {
                    foreach (var row in piece.WhileOn) result.Add((row.Mesh, row.Shape));
                    foreach (var row in piece.Follows) result.Add((piece.LocalMesh, row.LocalShape));
                }
#endif
                return result;
            }
        }

        public static HeldContext ContextFor(Transform avatarRoot) => avatarRoot != null ? new HeldContext(avatarRoot) : HeldContext.None;

        /// <summary>Whether the descriptor blinks or speaks with any shape of this mesh, so a rule on it has a blink and viseme switch worth showing.</summary>
        public static bool MeshHasTrackedShapes(SkinnedMeshRenderer mesh, HeldContext context)
        {
            if (mesh == null || context == null) return false;
            return context.Eyelids.Any(k => k.renderer == mesh) || context.Mouth.Any(k => k.renderer == mesh);
        }

        /// <summary>
        /// The shapes a rule holds on this entry while the entry is on, in mesh order: every shape of the mesh except
        /// those the entry names itself (its rows, and on a toggle the shapes its own animation clips move), a blend
        /// shape trigger's own shape, the shapes an AvatarDoc Shapes piece drives, and (unless the rule's holdTracked
        /// is on) the shapes the descriptor blinks and speaks with. Empty when the rule is off or its mesh is gone:
        /// this is what the build and the preview actually write.
        /// </summary>
        public static List<string> HeldShapes(MenuEntry entry, ShapeSetRule rule, HeldContext context)
        {
            if (rule == null || !rule.On) return new List<string>();
            return Candidates(entry, rule, context);
        }

        /// <summary>
        /// How many shapes of the rule's mesh it would hold if it were on, whatever its switch currently says: the
        /// rule line's note, the summary and the large rule warning read this instead of HeldShapes, so switching the
        /// rule off does not make it claim to cost nothing.
        /// </summary>
        public static int CoveredShapeCount(MenuEntry entry, ShapeSetRule rule, HeldContext context) => Candidates(entry, rule, context).Count;

        static List<string> Candidates(MenuEntry entry, ShapeSetRule rule, HeldContext context)
        {
            var result = new List<string>();
            if (entry == null || rule?.mesh == null || rule.mesh.sharedMesh == null) return result;
            context = context ?? HeldContext.None;
            var named = new HashSet<string>(entry.blendShapes.Where(t => t.renderer == rule.mesh).Select(t => t.shape));
            if (entry.kind == EntryKind.Trigger && entry.source == TriggerSource.BlendShape && entry.sourceRenderer == rule.mesh && !string.IsNullOrEmpty(entry.sourceShape))
                named.Add(entry.sourceShape);
            // A toggle's own clips play in its main layer, below the pin layer the rule writes in: holding those
            // shapes would cancel the entry's own animation.
            if (entry.kind == EntryKind.Toggle && entry.animations.Count > 0)
                named.UnionWith(CustomClips.ShapesMovedOn(entry, context.AvatarRoot, rule.mesh));
            var mesh = rule.mesh.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                var name = mesh.GetBlendShapeName(i);
                if (named.Contains(name)) continue;
                if (!rule.holdTracked && context.IsTracked(rule.mesh, name)) continue;
                if (context.PieceShapes.Contains((rule.mesh, name))) continue;
                result.Add(name);
            }
            return result;
        }

        /// <summary>The weight a rule holds one of its shapes at: that shape's default (its weight in the scene at upload) or 0.</summary>
        public static float HeldWeight(ShapeSetRule rule, float defaultWeight) => rule != null && rule.value == ShapeSetValue.Default ? defaultWeight : 0f;

        /// <summary>Whether this entry's rules, as they are switched now, hold a shape the descriptor blinks with (eyes) or speaks with (mouth), so the entry needs the matching tracking pause while it is on.</summary>
        public static void HeldTracking(MenuEntry entry, HeldContext context, out bool eyes, out bool mouth)
        {
            eyes = false;
            mouth = false;
            if (entry == null || context == null || !entry.OffersShapeSets) return;
            if (context.Eyelids.Count == 0 && context.Mouth.Count == 0) return;
            foreach (var rule in EffectiveRules(entry))
            {
                if (!rule.holdTracked) continue;
                foreach (var shape in HeldShapes(entry, rule, context))
                {
                    if (context.Eyelids.Contains((rule.mesh, shape))) eyes = true;
                    if (context.Mouth.Contains((rule.mesh, shape))) mouth = true;
                }
            }
        }

        /// <summary>
        /// Whether what a toggle's or a Hold trigger's pin layer holds while the entry is on, its Pin rows and its
        /// rules' held shapes, includes a shape the descriptor blinks with (eyes) or speaks with (mouth): the entry
        /// then pauses that part of tracking while it is on.
        /// </summary>
        public static void PinTracking(MenuEntry entry, HeldContext context, out bool eyes, out bool mouth)
        {
            HeldTracking(entry, context, out eyes, out mouth);
            if (entry == null || context == null) return;
            foreach (var target in entry.blendShapes)
            {
                if (target.mode != ShapeEffectMode.Pin || !MenuModel.ShapeExists(target.renderer, target.shape)) continue;
                if (context.Eyelids.Contains((target.renderer, target.shape))) eyes = true;
                if (context.Mouth.Contains((target.renderer, target.shape))) mouth = true;
            }
        }

        /// <summary>One shape Capture found above 0: Existing is the row of the entry it matches (null for a shape the entry has no row for yet), so the caller can update that row in place instead of adding a duplicate.</summary>
        public readonly struct CapturedShape
        {
            public readonly BlendShapeTarget Existing;
            public readonly string Shape;
            public readonly float Weight;

            public CapturedShape(BlendShapeTarget existing, string shape, float weight)
            {
                Existing = existing;
                Shape = shape;
                Weight = weight;
            }
        }

        /// <summary>
        /// Every shape of mesh whose scene weight is above 0.0001, at that weight. Pure: never writes to existing
        /// or to the mesh; the caller decides what a matched Existing row means (update it in place, in Pin mode)
        /// and what a null one means (add a new Pin row), after its own Undo.RecordObject. Empty when the mesh has
        /// none above 0.
        /// </summary>
        public static List<CapturedShape> Capture(SkinnedMeshRenderer mesh, IReadOnlyList<BlendShapeTarget> existing)
        {
            var result = new List<CapturedShape>();
            if (mesh == null || mesh.sharedMesh == null) return result;
            var sharedMesh = mesh.sharedMesh;
            for (int i = 0; i < sharedMesh.blendShapeCount; i++)
            {
                float weight = mesh.GetBlendShapeWeight(i);
                if (weight <= 0.0001f) continue;
                var name = sharedMesh.GetBlendShapeName(i);
                var match = existing?.FirstOrDefault(t => t.renderer == mesh && t.shape == name);
                result.Add(new CapturedShape(match, name, weight));
            }
            return result;
        }
    }
}
#endif
