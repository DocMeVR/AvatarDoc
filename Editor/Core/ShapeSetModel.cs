#if DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Pure reads of an entry's shape set rules: what they zero, and what a mesh's scene weights capture into rows. No scene write, no asset write.</summary>
    public static class ShapeSetModel
    {
        /// <summary>Above this many shapes forced to 0 by one rule, the cost is called out with ad.warn.shape_set_large.</summary>
        public const int LargeThreshold = 150;

        /// <summary>
        /// One rule per mesh this entry names, several rows for the same mesh acting as one: zeroOthers is on if
        /// any of them is. A row with no mesh is left out.
        /// </summary>
        public static IEnumerable<ShapeSetRule> EffectiveRules(MenuEntry entry)
        {
            var byMesh = new Dictionary<SkinnedMeshRenderer, ShapeSetRule>();
            if (entry?.shapeSets == null) return byMesh.Values;
            foreach (var rule in entry.shapeSets)
            {
                if (rule == null || rule.mesh == null) continue;
                if (byMesh.TryGetValue(rule.mesh, out var merged))
                {
                    if (rule.zeroOthers) merged.zeroOthers = true;
                    continue;
                }
                byMesh[rule.mesh] = new ShapeSetRule { mesh = rule.mesh, zeroOthers = rule.zeroOthers };
            }
            return byMesh.Values;
        }

        /// <summary>The shapes a rule forces to 0 on this entry, in mesh order, excluding every shape the entry names itself. Empty when the rule is off, or its mesh is gone: this is what the build and the preview actually write.</summary>
        public static List<string> ZeroedShapes(MenuEntry entry, ShapeSetRule rule)
        {
            var result = new List<string>();
            if (entry == null || rule?.mesh == null || rule.mesh.sharedMesh == null || !rule.zeroOthers) return result;
            var named = new HashSet<string>(entry.blendShapes.Where(t => t.renderer == rule.mesh).Select(t => t.shape));
            var mesh = rule.mesh.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                var name = mesh.GetBlendShapeName(i);
                if (!named.Contains(name)) result.Add(name);
            }
            return result;
        }

        /// <summary>
        /// How many shapes of the rule's mesh it would zero if it were on, whatever zeroOthers currently says: the
        /// rule line's note and the summary read this instead of ZeroedShapes, so switching the rule off does not
        /// make it claim to cost nothing.
        /// </summary>
        public static int CoveredShapeCount(MenuEntry entry, ShapeSetRule rule)
        {
            if (entry == null || rule?.mesh == null || rule.mesh.sharedMesh == null) return 0;
            var named = new HashSet<string>(entry.blendShapes.Where(t => t.renderer == rule.mesh).Select(t => t.shape));
            var mesh = rule.mesh.sharedMesh;
            int count = 0;
            for (int i = 0; i < mesh.blendShapeCount; i++)
                if (!named.Contains(mesh.GetBlendShapeName(i))) count++;
            return count;
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
