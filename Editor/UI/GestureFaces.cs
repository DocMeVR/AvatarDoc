#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Which hand a gesture face answers to. Both makes one trigger per hand with the same face.</summary>
    public enum GestureHand { Left = 0, Right = 1, Both = 2 }

    /// <summary>What a gesture face does while face tracking is on: nothing (Paused), or hold its shapes over face tracking until the gesture is let go (Wins).</summary>
    public enum GestureTracking { Paused = 0, Wins = 1 }

    /// <summary>Whose face shows while both hands show a gesture that has one.</summary>
    public enum GestureOrder { RightWins = 0, LeftWins = 1 }

    /// <summary>One blend shape of a gesture face and the weight it has while the face shows.</summary>
    [Serializable]
    public sealed class GestureShape
    {
        public SkinnedMeshRenderer renderer;
        public string shape = "";
        public float weight = 100f;
    }

    /// <summary>One row of the window: a gesture, the hand or hands it answers to, its face and what it does while face tracking is on.</summary>
    [Serializable]
    public sealed class GestureFaceRow
    {
        /// <summary>VRChat's value of GestureLeft and GestureRight for the gesture, 1 (Fist) to 7 (Thumbs up).</summary>
        public int gesture = 1;
        public GestureHand hand = GestureHand.Both;
        public GestureTracking tracking = GestureTracking.Paused;
        public List<GestureShape> shapes = new List<GestureShape>();
    }

    /// <summary>Everything the Gesture faces window holds. Serializable so the window survives a script reload; nothing reaches the component before Apply.</summary>
    [Serializable]
    public sealed class GestureFacesDraft
    {
        public AvatarDoc component;
        public SkinnedMeshRenderer faceMesh;
        /// <summary>The Bool parameter that is true while face tracking is on. Empty: no face tracking, so no face is paused.</summary>
        public string trackingParameter = "";
        public GestureOrder order = GestureOrder.RightWins;
        public bool fistStrength;
        public bool menuSwitch = true;
        public List<GestureFaceRow> rows = new List<GestureFaceRow>();
        /// <summary>Every shape of the face mesh, and of each other mesh a face uses, at the weight it had when the window opened: where the face rests while no gesture is held.</summary>
        public List<GestureShape> rest = new List<GestureShape>();
    }

    /// <summary>
    /// Gesture faces: a face for each VRChat hand gesture, made of ordinary parameter triggers tagged with their row
    /// (MenuEntry.gestureFace), so running the window again edits the same triggers. Each gesture and hand gets one
    /// trigger that pins the face's own shapes while GestureLeft or GestureRight shows the gesture, and nothing else,
    /// so face tracking and toggles keep every other shape. The losing hand's face also gets a trigger under it
    /// that holds its shapes at their rest weight while the other hand shows a face too, so of two faces only the
    /// winning hand's shows. A Fist that follows its strength gets one more, showing it whole on desktop, where
    /// there is no squeeze to follow. The triggers sit together in the component, the losing hand's first.
    /// </summary>
    public static class GestureFaces
    {
        public const int Fist = 1;
        public const int GestureCount = 7;
        public const string SwitchTag = "switch";
        const string WholeRole = "whole";
        const string UnderRole = "under";
        const float Epsilon = 0.0001f;

        /// <summary>The parameters VRChat's face tracking templates set while tracking is on, the one to prefer first.</summary>
        public static readonly string[] TrackingNames = { "LipTrackingActive", "EyeTrackingActive" };

        static readonly string[] GestureKeys =
        {
            "ad.ui.gestures.gesture.1", "ad.ui.gestures.gesture.2", "ad.ui.gestures.gesture.3", "ad.ui.gestures.gesture.4",
            "ad.ui.gestures.gesture.5", "ad.ui.gestures.gesture.6", "ad.ui.gestures.gesture.7",
        };

        static readonly HashSet<string> GestureParameters = new HashSet<string> { "GestureLeft", "GestureRight", "GestureLeftWeight", "GestureRightWeight" };

        public static string GestureParameter(GestureHand hand) => hand == GestureHand.Right ? "GestureRight" : "GestureLeft";

        public static string WeightParameter(GestureHand hand) => hand == GestureHand.Right ? "GestureRightWeight" : "GestureLeftWeight";

        public static string Tag(GestureHand hand, int gesture) => (hand == GestureHand.Right ? "right:" : "left:") + gesture.ToString(CultureInfo.InvariantCulture);

        static string Tag(GestureHand hand, int gesture, string role) => string.IsNullOrEmpty(role) ? Tag(hand, gesture) : Tag(hand, gesture) + ":" + role;

        /// <summary>Reads a face trigger's own tag, such as "left:1". A tag with a role after it belongs to a trigger made to go with a face, and is not a face.</summary>
        public static bool TryParseTag(string tag, out GestureHand hand, out int gesture)
            => TryParseTag(tag, out hand, out gesture, out var role) && role.Length == 0;

        /// <summary>Reads any tag the window writes on a trigger: "left:1", or "left:1:whole", "left:1:under", "left:1:under2" for the triggers that go with that face.</summary>
        static bool TryParseTag(string tag, out GestureHand hand, out int gesture, out string role)
        {
            hand = GestureHand.Left;
            gesture = 0;
            role = "";
            if (string.IsNullOrEmpty(tag)) return false;
            var parts = tag.Split(':');
            if (parts.Length < 2 || parts.Length > 3) return false;
            if (parts[0] == "right") hand = GestureHand.Right;
            else if (parts[0] != "left") return false;
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out gesture) || gesture < 1 || gesture > GestureCount) return false;
            if (parts.Length == 2) return true;
            role = parts[2];
            if (role == WholeRole || role == UnderRole) return true;
            return role.StartsWith(UnderRole, StringComparison.Ordinal) && int.TryParse(role.Substring(UnderRole.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 2;
        }

        public static string GestureName(int gesture) => gesture >= 1 && gesture <= GestureCount ? L.Text(GestureKeys[gesture - 1]) : gesture.ToString(CultureInfo.InvariantCulture);

        static string HandWord(GestureHand hand) => L.Text(hand == GestureHand.Right ? "ad.ui.gestures.name.right" : "ad.ui.gestures.name.left");

        /// <summary>The name a new gesture face trigger gets, such as "Gesture: Fist (left)". A trigger the window made keeps whatever name it has since.</summary>
        public static string TriggerName(GestureHand hand, int gesture) => string.Format(L.Text("ad.ui.gestures.name"), GestureName(gesture), HandWord(hand));

        /// <summary>The triggers the window made in this component, faces and the triggers that go with them, by their tag. A copy of one keeps no tag (EntryEditing.Duplicate); of two that share one, the first counts.</summary>
        static Dictionary<string, MenuEntry> TaggedTriggers(AvatarDoc component)
        {
            var result = new Dictionary<string, MenuEntry>();
            if (component == null) return result;
            foreach (var entry in component.entries)
                if (entry != null && entry.kind == EntryKind.Trigger && TryParseTag(entry.gestureFace, out _, out _, out _) && !result.ContainsKey(entry.gestureFace))
                    result[entry.gestureFace] = entry;
            return result;
        }

        static MenuEntry TaggedSwitch(AvatarDoc component)
            => component == null ? null : component.entries.FirstOrDefault(e => e != null && e.kind == EntryKind.Toggle && e.gestureFace == SwitchTag);

        /// <summary>The face tracking parameter the component's faces were made with: the window's own choice, kept on the component. Empty for None, or when no face was made yet.</summary>
        static string MadeWith(AvatarDoc component)
            => component != null && component.gestureFaces != null && component.gestureFaces.trackingChosen ? (component.gestureFaces.trackingParameter ?? "").Trim() : "";

        /// <summary>
        /// What the window starts from: the faces the component's tagged triggers show, read from the triggers
        /// themselves, so a face edited in the inspector shows as it is now, and a row for every gesture that has none
        /// yet. A face is every blend shape row of its trigger, one at 0 too. Both is one face that the left and the
        /// right trigger share. Where the faces rest is what Make kept, and the scene's weights for every shape it
        /// never kept, so a scene left posed is not taken for the rest.
        /// </summary>
        public static GestureFacesDraft Read(AvatarDoc component)
        {
            var draft = new GestureFacesDraft { component = component };
            if (component == null) return draft;
            var root = AvatarPlanCache.RootOf(component);
            var triggers = TaggedTriggers(component).Where(p => TryParseTag(p.Key, out _, out _)).ToDictionary(p => p.Key, p => p.Value);

            bool chosen = component.gestureFaces != null && component.gestureFaces.trackingChosen;
            var madeWith = MadeWith(component);
            draft.trackingParameter = chosen ? madeWith : DetectTracking(root);
            var faces = new Dictionary<(GestureHand hand, int gesture), (List<GestureShape> shapes, bool paused)>();
            foreach (var pair in triggers)
            {
                TryParseTag(pair.Key, out var hand, out var gesture);
                var entry = pair.Value;
                var shapes = entry.blendShapes.Where(t => t != null && t.renderer != null && !string.IsNullOrEmpty(t.shape))
                    .Select(t => new GestureShape { renderer = t.renderer, shape = t.shape, weight = t.weight }).ToList();
                faces[(hand, gesture)] = (shapes, madeWith.Length > 0 && PausesFor(entry, madeWith));
            }
            draft.fistStrength = triggers.Values.Any(e => TryParseTag(e.gestureFace, out _, out var g) && g == Fist && e.IsFollowValueTrigger);
            draft.menuSwitch = triggers.Count == 0 || TaggedSwitch(component) != null;
            int firstLeft = component.entries.FindIndex(e => triggers.ContainsValue(e) && e.gestureFace.StartsWith("left:", StringComparison.Ordinal));
            int firstRight = component.entries.FindIndex(e => triggers.ContainsValue(e) && e.gestureFace.StartsWith("right:", StringComparison.Ordinal));
            draft.order = firstLeft >= 0 && firstRight >= 0 && firstRight < firstLeft ? GestureOrder.LeftWins : GestureOrder.RightWins;

            // Faces made without a tracking parameter pause for nothing, so Paused and Wins built alike: they read
            // as the choice a new row starts with.
            GestureTracking TrackingOf(bool paused) => madeWith.Length == 0 || paused ? GestureTracking.Paused : GestureTracking.Wins;
            for (int gesture = 1; gesture <= GestureCount; gesture++)
            {
                bool hasLeft = faces.TryGetValue((GestureHand.Left, gesture), out var left);
                bool hasRight = faces.TryGetValue((GestureHand.Right, gesture), out var right);
                if (hasLeft && hasRight && left.paused == right.paused && SameFace(left.shapes, right.shapes))
                {
                    draft.rows.Add(new GestureFaceRow { gesture = gesture, hand = GestureHand.Both, tracking = TrackingOf(left.paused), shapes = left.shapes });
                    continue;
                }
                if (hasLeft) draft.rows.Add(new GestureFaceRow { gesture = gesture, hand = GestureHand.Left, tracking = TrackingOf(left.paused), shapes = left.shapes });
                if (hasRight) draft.rows.Add(new GestureFaceRow { gesture = gesture, hand = GestureHand.Right, tracking = TrackingOf(right.paused), shapes = right.shapes });
                if (!hasLeft && !hasRight) draft.rows.Add(new GestureFaceRow { gesture = gesture });
            }

            draft.faceMesh = draft.rows.SelectMany(r => r.shapes).GroupBy(s => s.renderer).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault()
                ?? GuessFaceMesh(root);
            if (component.gestureFaces?.rest != null)
                foreach (var kept in component.gestureFaces.rest)
                    if (kept != null && MenuModel.ShapeExists(kept.renderer, kept.shape))
                        draft.rest.Add(new GestureShape { renderer = kept.renderer, shape = kept.shape, weight = kept.weight });
            SnapshotRest(draft);
            return draft;
        }

        static bool SameFace(List<GestureShape> a, List<GestureShape> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].renderer != b[i].renderer || a[i].shape != b[i].shape || !Mathf.Approximately(a[i].weight, b[i].weight)) return false;
            return true;
        }

        /// <summary>Whether a trigger waits for this tracking parameter to be off, as a Paused face does.</summary>
        static bool PausesFor(MenuEntry entry, string tracking)
            => entry.EffectiveGroups().Any(g => g.group.all.Any(c => c != null && c.kind == WhenConditionKind.Parameter
                && c.condition == ParameterCondition.IsFalse && (c.sourceParameter ?? "").Trim() == tracking));

        /// <summary>
        /// The parameter face tracking sets while it is on, looked for among the avatar's parameters: its expression
        /// parameters, its FX controller's, the ones its components declare (Modular Avatar's among them) and the
        /// ones in a parameters asset another component merges (a VRCFury face tracking prefab's). A Bool named
        /// LipTrackingActive first, then EyeTrackingActive, then one of the two after a prefix such as "FT/".
        /// Empty when there is none.
        /// </summary>
        public static string DetectTracking(Transform avatarRoot)
        {
            if (avatarRoot == null) return "";
            var bools = TrackingChoices(avatarRoot, "");
            foreach (var name in TrackingNames)
                if (bools.Contains(name)) return name;
            foreach (var name in TrackingNames)
            {
                var prefixed = bools.FirstOrDefault(b => b.EndsWith("/" + name, StringComparison.Ordinal));
                if (prefixed != null) return prefixed;
            }
            return "";
        }

        /// <summary>The Bool parameters of the avatar a face tracking setup can be read from, in name order with the one in use first: the avatar's own, never AvatarDoc's and never VRChat's.</summary>
        public static List<string> TrackingChoices(Transform avatarRoot, string current)
        {
            var result = new List<string>();
            if (avatarRoot == null) return result;
            var plan = AvatarPlanCache.Get(avatarRoot);
            var own = new HashSet<string>(plan.Plan != null ? plan.Plan.Parameters.Select(p => p.Name) : Enumerable.Empty<string>());
            foreach (var pair in plan.Existing.OrderBy(p => p.Key, StringComparer.Ordinal))
                if (pair.Value.Type == PlannedType.Bool && !own.Contains(pair.Key) && TriggerPlanner.BuiltInType(pair.Key) == null)
                    result.Add(pair.Key);
            if (!string.IsNullOrEmpty(current) && result.Remove(current)) result.Insert(0, current);
            return result;
        }

        /// <summary>Whether the avatar has this parameter where AvatarDoc can see it: among its own, its components' or the ones a VRCFury Full Controller keeps global.</summary>
        public static bool TrackingKnown(Transform avatarRoot, string parameter)
        {
            parameter = (parameter ?? "").Trim();
            return avatarRoot != null && parameter.Length > 0 && AvatarPlanCache.Get(avatarRoot).Existing.ContainsKey(parameter);
        }

        /// <summary>Whether other players see this parameter's value: a synced expression parameter of the avatar, of a parameters asset another component merges, or of a component that declares it synced.</summary>
        public static bool TrackingSynced(Transform avatarRoot, string parameter)
        {
            parameter = (parameter ?? "").Trim();
            if (avatarRoot == null || parameter.Length == 0) return false;
            var plan = AvatarPlanCache.Get(avatarRoot);
            if (plan.Existing.TryGetValue(parameter, out var known) && known.IsExpressionParameter && known.Synced) return true;
            if (AvatarPlanCache.ReferencedFor(avatarRoot).Any(p => p.name == parameter && p.networkSynced)) return true;
            return AvatarPlanCache.ProvidedFor(avatarRoot).Any(p => p != null && !(p.Source is AvatarDoc) && p.EffectiveName == parameter && p.WantSynced);
        }

        /// <summary>The mesh a face is most likely on: the one the descriptor speaks with, then the one it blinks with, then a mesh named Face or Body, then the one with the most blend shapes.</summary>
        public static SkinnedMeshRenderer GuessFaceMesh(Transform avatarRoot)
        {
            if (avatarRoot == null) return null;
            bool HasShapes(SkinnedMeshRenderer r) => r != null && r.sharedMesh != null && r.sharedMesh.blendShapeCount > 0 && !MenuModel.IsOutsideAvatar(avatarRoot, r.transform);
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor != null)
            {
                bool speaks = descriptor.lipSync == VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape || descriptor.lipSync == VRC_AvatarDescriptor.LipSyncStyle.JawFlapBlendShape;
                if (speaks && HasShapes(descriptor.VisemeSkinnedMesh)) return descriptor.VisemeSkinnedMesh;
                var eyelids = descriptor.customEyeLookSettings.eyelidsSkinnedMesh;
                if (descriptor.enableEyeLook && HasShapes(eyelids)) return eyelids;
            }
            var meshes = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(HasShapes).ToList();
            return meshes.FirstOrDefault(r => string.Equals(r.name, "Face", StringComparison.OrdinalIgnoreCase))
                ?? meshes.FirstOrDefault(r => string.Equals(r.name, "Body", StringComparison.OrdinalIgnoreCase))
                ?? meshes.OrderByDescending(r => r.sharedMesh.blendShapeCount).FirstOrDefault();
        }

        /// <summary>Keeps the scene's weights of the face mesh and of every other mesh a face uses as where the faces rest, for every shape that has no rest yet: a rest kept before stays, so a face set up since is not taken for it.</summary>
        public static void SnapshotRest(GestureFacesDraft draft)
        {
            if (draft == null) return;
            if (draft.rest == null) draft.rest = new List<GestureShape>();
            draft.rest.RemoveAll(s => s == null || s.renderer == null);
            var known = new HashSet<(SkinnedMeshRenderer, string)>(draft.rest.Select(s => (s.renderer, s.shape)));
            var meshes = new List<SkinnedMeshRenderer> { draft.faceMesh };
            meshes.AddRange(draft.rows.SelectMany(r => r.shapes).Where(s => s != null).Select(s => s.renderer));
            foreach (var mesh in meshes.Distinct())
            {
                if (mesh == null || mesh.sharedMesh == null) continue;
                for (int i = 0; i < mesh.sharedMesh.blendShapeCount; i++)
                {
                    var name = mesh.sharedMesh.GetBlendShapeName(i);
                    if (known.Add((mesh, name))) draft.rest.Add(new GestureShape { renderer = mesh, shape = name, weight = mesh.GetBlendShapeWeight(i) });
                }
            }
        }

        /// <summary>Where each shape rests: the weight the window kept for it, else the weight its prefab gives it, else 0.</summary>
        static Func<SkinnedMeshRenderer, string, float> RestOf(GestureFacesDraft draft)
        {
            var kept = new Dictionary<(SkinnedMeshRenderer, string), float>();
            if (draft?.rest != null)
                foreach (var shape in draft.rest)
                    if (shape != null && shape.renderer != null && !kept.ContainsKey((shape.renderer, shape.shape))) kept[(shape.renderer, shape.shape)] = shape.weight;
            return (renderer, shape) =>
            {
                if (renderer == null) return 0f;
                if (kept.TryGetValue((renderer, shape), out var weight)) return weight;
                var source = PrefabUtility.GetCorrespondingObjectFromSource(renderer);
                if (source != null && source.sharedMesh != null)
                {
                    int index = source.sharedMesh.GetBlendShapeIndex(shape);
                    if (index >= 0) return source.GetBlendShapeWeight(index);
                }
                return 0f;
            };
        }

        /// <summary>
        /// Captures the face mesh's shapes that are away from where they rest as this row's face, at their scene
        /// weights: the row's shapes on that mesh are replaced, its shapes on other meshes stay. A Shrink key or a
        /// body shape that sits where it rests is left out. Only the scene is read. Returns how many shapes were
        /// captured; 0 leaves the row as it was.
        /// </summary>
        public static int Capture(GestureFacesDraft draft, GestureFaceRow row)
        {
            var mesh = draft?.faceMesh;
            if (mesh == null || mesh.sharedMesh == null || row == null) return 0;
            var rest = RestOf(draft);
            var captured = new List<GestureShape>();
            for (int i = 0; i < mesh.sharedMesh.blendShapeCount; i++)
            {
                var name = mesh.sharedMesh.GetBlendShapeName(i);
                float weight = mesh.GetBlendShapeWeight(i);
                if (Mathf.Abs(weight - rest(mesh, name)) > Epsilon) captured.Add(new GestureShape { renderer = mesh, shape = name, weight = weight });
            }
            if (captured.Count == 0) return 0;
            row.shapes.RemoveAll(s => s == null || s.renderer == mesh);
            row.shapes.AddRange(captured);
            return captured.Count;
        }

        /// <summary>Whether a row has a face to build: at least one shape that exists on its mesh.</summary>
        public static bool HasFace(GestureFaceRow row) => row != null && row.shapes.Any(Valid);

        static bool Valid(GestureShape shape) => shape != null && MenuModel.ShapeExists(shape.renderer, shape.shape);

        static IEnumerable<GestureHand> HandsOf(GestureHand hand) => hand == GestureHand.Both ? new[] { GestureHand.Left, GestureHand.Right } : new[] { hand };

        /// <summary>The gestures that have two faces for one hand among the rows that have a face, in gesture order. An empty row overlaps nothing.</summary>
        public static List<int> Overlaps(GestureFacesDraft draft)
        {
            var result = new List<int>();
            if (draft == null) return result;
            var taken = new HashSet<(GestureHand, int)>();
            foreach (var row in draft.rows.Where(HasFace))
                foreach (var hand in HandsOf(row.hand))
                    if (!taken.Add((hand, row.gesture)) && !result.Contains(row.gesture)) result.Add(row.gesture);
            result.Sort();
            return result;
        }

        /// <summary>The shapes the faces use that the scene shows away from where they rest: while no gesture is held, the avatar shows them like that.</summary>
        public static List<GestureShape> PosedShapes(GestureFacesDraft draft)
        {
            var result = new List<GestureShape>();
            if (draft == null) return result;
            var rest = RestOf(draft);
            var seen = new HashSet<(SkinnedMeshRenderer, string)>();
            foreach (var shape in draft.rows.SelectMany(r => r.shapes).Where(Valid))
            {
                if (!seen.Add((shape.renderer, shape.shape))) continue;
                int index = shape.renderer.sharedMesh.GetBlendShapeIndex(shape.shape);
                if (index >= 0 && Mathf.Abs(shape.renderer.GetBlendShapeWeight(index) - rest(shape.renderer, shape.shape)) > Epsilon) result.Add(shape);
            }
            return result;
        }

        /// <summary>Puts every shape PosedShapes names back where it rests in the scene, in one Undo step. Returns how many it put back.</summary>
        public static int ResetPosed(GestureFacesDraft draft)
        {
            var posed = PosedShapes(draft);
            if (posed.Count == 0) return 0;
            var rest = RestOf(draft);
            var renderers = posed.Select(s => s.renderer).Distinct().ToArray();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.RecordObjects(renderers, "Put gesture face shapes back");
            foreach (var shape in posed) shape.renderer.SetBlendShapeWeight(shape.renderer.sharedMesh.GetBlendShapeIndex(shape.shape), rest(shape.renderer, shape.shape));
            foreach (var renderer in renderers) EntryEditing.MarkChanged(renderer);
            Undo.CollapseUndoOperations(group);
            return posed.Count;
        }

        sealed class Wanted
        {
            public GestureHand Hand;
            public int Gesture;
            public GestureTracking Tracking;
            public List<GestureShape> Shapes;
        }

        /// <summary>A face's trigger as Make wrote it, with the conditions of the creator's own it kept.</summary>
        sealed class Written
        {
            public Wanted Face;
            public MenuEntry Trigger;
            public List<WhenCondition> Own;
            public bool Strength;
        }

        /// <summary>
        /// Makes the draft's faces in its component, in one Undo step: a trigger per gesture and hand that has a face,
        /// the triggers that go with it, the ones made before edited in place, and every tagged trigger whose face is
        /// gone removed, with the menu switch when it is asked for and there is a face to switch. What the creator
        /// added to a face trigger since, its objects, materials, properties, follows, rules, icon, its own Only
        /// while and its own extra conditions in its first card, stays; its blend shape rows, its gesture and
        /// tracking conditions and its mode are the window's, and the triggers that go with a face are the window's
        /// alone. Where each shape of the faces rests is kept on the component. Removing the switch takes every link to
        /// it away too. Refused, writing nothing, while the component is locked, while two rows give one hand the same
        /// gesture, or while the switch would go but a locked component still uses it. Returns the switch and the
        /// triggers.
        /// </summary>
        public static List<MenuEntry> Apply(GestureFacesDraft draft, out string status)
        {
            status = "";
            var made = new List<MenuEntry>();
            var component = draft?.component;
            var root = component != null ? AvatarPlanCache.RootOf(component) : null;
            if (root == null)
            {
                status = L.Text("ad.ui.gestures.gone");
                return made;
            }
            if (EntryEditing.Refuses(component))
            {
                status = L.Text("ad.ui.locked.refused");
                return made;
            }
            var overlaps = Overlaps(draft);
            if (overlaps.Count > 0)
            {
                status = string.Format(L.Text("ad.ui.gestures.overlap"), GestureName(overlaps[0]));
                return made;
            }

            var rows = draft.rows.Where(HasFace).ToList();
            var losing = draft.order == GestureOrder.RightWins ? GestureHand.Left : GestureHand.Right;
            var wanted = rows.SelectMany(row => HandsOf(row.hand).Select(hand => new Wanted
                {
                    Hand = hand, Gesture = row.gesture, Tracking = row.tracking,
                    Shapes = row.shapes.Where(Valid).Where(s => !MenuModel.IsOutsideAvatar(root, s.renderer.transform)).ToList(),
                }))
                .Where(w => w.Shapes.Count > 0)
                .OrderBy(w => w.Hand == losing ? 0 : 1).ThenBy(w => w.Gesture).ToList();
            var tagged = TaggedTriggers(component);
            var oldSwitch = TaggedSwitch(component);
            if (wanted.Count == 0 && tagged.Count == 0 && oldSwitch == null) return made;
            if (oldSwitch != null && !(draft.menuSwitch && wanted.Count > 0))
            {
                var locked = LockedUserOf(root, component, oldSwitch.id);
                if (locked != null)
                {
                    status = string.Format(L.Text("ad.ui.gestures.switch_locked"), locked.gameObject.name);
                    return made;
                }
            }
            var tracking = (draft.trackingParameter ?? "").Trim();
            var oldTracking = MadeWith(component);
            bool remember = tracking.Length > 0 || (component.gestureFaces != null && component.gestureFaces.trackingChosen) || DetectTracking(root).Length > 0;
            var rest = RestOf(draft);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Gesture faces");
            Undo.RecordObject(component, "Gesture faces");
            try
            {
                if (component.gestureFaces == null) component.gestureFaces = new GestureFaceSettings();
                if (remember)
                {
                    component.gestureFaces.trackingChosen = true;
                    component.gestureFaces.trackingParameter = tracking;
                }
                component.gestureFaces.rest = wanted.SelectMany(w => w.Shapes).Select(s => (s.renderer, s.shape)).Distinct()
                    .Select(s => new GestureRestWeight { renderer = s.renderer, shape = s.shape, weight = rest(s.renderer, s.shape) }).ToList();

                MenuEntry toggle = oldSwitch;
                string removedSwitch = null;
                if (draft.menuSwitch && wanted.Count > 0)
                {
                    if (toggle == null)
                    {
                        toggle = MenuEntry.Create(EntryKind.Toggle, L.Text("ad.ui.gestures.switch_name"));
                        toggle.defaultOn = true;
                        toggle.saved = true;
                        toggle.synced = true;
                        toggle.gestureFace = SwitchTag;
                        component.entries.Add(toggle);
                    }
                    made.Add(toggle);
                }
                else if (toggle != null)
                {
                    removedSwitch = toggle.id;
                    component.entries.Remove(toggle);
                    toggle = null;
                }

                MenuEntry Reuse(string tag, Func<string> name)
                    => tagged.TryGetValue(tag, out var entry) ? entry : MenuEntry.Create(EntryKind.Trigger, name());

                var written = new List<Written>();
                foreach (var face in wanted)
                {
                    bool strength = draft.fistStrength && face.Gesture == Fist;
                    var trigger = Reuse(Tag(face.Hand, face.Gesture), () => TriggerName(face.Hand, face.Gesture));
                    var own = WriteFace(trigger, face, tracking, oldTracking, strength, toggle, oldSwitch);
                    written.Add(new Written { Face = face, Trigger = trigger, Own = own, Strength = strength });
                }

                var winners = written.Where(w => w.Face.Hand != losing).ToList();
                var triggers = new List<MenuEntry>();
                foreach (var face in written)
                {
                    triggers.Add(face.Trigger);
                    // After the face that follows the squeeze, so on desktop the whole face wins whatever weight
                    // VRChat reports.
                    if (face.Strength)
                    {
                        var whole = Reuse(Tag(face.Face.Hand, face.Face.Gesture, WholeRole), () => string.Format(L.Text("ad.ui.gestures.name.whole"), TriggerName(face.Face.Hand, face.Face.Gesture)));
                        WriteWhole(whole, face.Trigger, face.Face, tracking, face.Own);
                        triggers.Add(whole);
                    }
                    if (face.Face.Hand != losing || winners.Count == 0) continue;
                    var chunks = UnderGroups(face, winners, tracking);
                    for (int i = 0; i < chunks.Count; i++)
                    {
                        int number = i + 1;
                        var tag = Tag(face.Face.Hand, face.Face.Gesture, number == 1 ? UnderRole : UnderRole + number.ToString(CultureInfo.InvariantCulture));
                        var under = Reuse(tag, () => string.Format(L.Text("ad.ui.gestures.name.under"), TriggerName(face.Face.Hand, face.Face.Gesture), HandWord(winners[0].Face.Hand))
                            + (number == 1 ? "" : " " + number.ToString(CultureInfo.InvariantCulture)));
                        WriteUnder(under, tag, face.Trigger, face.Face, chunks[i], rest);
                        triggers.Add(under);
                    }
                }

                // The faces sit together where the first of them was, the losing hand's first, so the winning hand's
                // layers come later in FX.
                int place = component.entries.FindIndex(e => tagged.ContainsValue(e));
                if (place < 0) place = component.entries.Count;
                component.entries.RemoveAll(e => tagged.ContainsValue(e));
                component.entries.InsertRange(Mathf.Clamp(place, 0, component.entries.Count), triggers);
                if (removedSwitch != null) ForgetToggle(root, component, removedSwitch);
                made.AddRange(triggers);
                EntryEditing.MarkChanged(component);
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
            }

            AvatarPlanCache.NoteSelfEdit(component);
            AvatarPlanCache.InvalidateMenu(root);
            status = wanted.Count == 0 ? L.Text("ad.ui.gestures.done.none") : UiText.Count(rows.Count, "ad.ui.gestures.done.one", "ad.ui.gestures.done");
            return made;
        }

        /// <summary>
        /// Gives one trigger its face: Parameter source, the gesture as its condition (after the hand's weight on a
        /// Fist that follows its strength, and before the tracking parameter being off on a face that is paused), the
        /// creator's own other conditions after those, the face's shapes as Pin rows, and the switch as its gate
        /// unless it has an Only while of the creator's own. Returns the creator's own conditions.
        /// </summary>
        static List<WhenCondition> WriteFace(MenuEntry entry, Wanted face, string tracking, string oldTracking, bool strength, MenuEntry toggle, MenuEntry oldSwitch)
        {
            var conditions = new List<WhenCondition>();
            if (strength) conditions.Add(Parameter(WeightParameter(face.Hand), ParameterCondition.Greater, 0f));
            conditions.Add(Parameter(GestureParameter(face.Hand), ParameterCondition.Equals, face.Gesture));
            if (face.Tracking == GestureTracking.Paused && tracking.Length > 0) conditions.Add(Parameter(tracking, ParameterCondition.IsFalse, 0f));
            var own = (entry.EffectiveGroups().Select(g => g.group).FirstOrDefault()?.all
                .Where(c => c != null && !Ours(c, tracking, oldTracking)) ?? Enumerable.Empty<WhenCondition>())
                .Where(c => entry.UsesWhenGroups || c.kind != WhenConditionKind.Toggle || !string.IsNullOrEmpty(c.sourceToggleId)).ToList();
            conditions.AddRange(own);

            entry.kind = EntryKind.Trigger;
            entry.parentId = "";
            entry.gestureFace = Tag(face.Hand, face.Gesture);
            conditions[0].CopyToEntry(entry);
            entry.when = conditions.Count > 1 ? new List<ConditionGroup> { new ConditionGroup { all = conditions } } : new List<ConditionGroup>();
            entry.touchMode = strength ? TouchMode.FollowValue : TouchMode.Hold;
            if (strength) entry.contactFullAt = 1f;
            bool switchGate = string.IsNullOrEmpty(entry.gateToggleId) || (toggle != null && entry.gateToggleId == toggle.id) || (oldSwitch != null && entry.gateToggleId == oldSwitch.id);
            if (toggle != null && switchGate)
            {
                entry.gateToggleId = toggle.id;
                entry.gateToggleOn = true;
            }
            entry.blendShapes = face.Shapes.Select(s => new BlendShapeTarget { renderer = s.renderer, shape = s.shape, weight = Mathf.Clamp(s.weight, 0f, 100f), mode = ShapeEffectMode.Pin }).ToList();
            return own;
        }

        /// <summary>
        /// The Fist face whole on desktop, where VRChat has no squeeze to follow: the gesture, VRMode being 0, the
        /// face's own requirements and gate, and everything the face does, its shapes and what the creator added to
        /// it. In VR the face follows the squeeze from 0, the Index trigger untouched included.
        /// </summary>
        static void WriteWhole(MenuEntry whole, MenuEntry face, Wanted wanted, string tracking, List<WhenCondition> own)
        {
            var conditions = new List<WhenCondition>
            {
                Parameter(GestureParameter(wanted.Hand), ParameterCondition.Equals, wanted.Gesture),
                Parameter("VRMode", ParameterCondition.Equals, 0f),
            };
            if (wanted.Tracking == GestureTracking.Paused && tracking.Length > 0) conditions.Add(Parameter(tracking, ParameterCondition.IsFalse, 0f));
            conditions.AddRange(own.Select(Clone));
            Companion(whole, face, Tag(wanted.Hand, wanted.Gesture, WholeRole), new List<ConditionGroup> { new ConditionGroup { all = conditions } });
            whole.blendShapes = face.blendShapes.Select(t => new BlendShapeTarget { renderer = t.renderer, shape = t.shape, weight = t.weight, mode = ShapeEffectMode.Pin }).ToList();
            whole.objects = face.objects.Select(Clone).ToList();
            whole.materials = face.materials.Select(Clone).ToList();
            whole.properties = face.properties.Select(Clone).ToList();
            whole.follows = face.follows.Select(Clone).ToList();
            whole.shapeSets = face.shapeSets.Select(Clone).ToList();
        }

        /// <summary>The losing hand's face held at its rest weights while the other hand shows a face of its own, so only the winning face shows, and nothing else: the window owns what it does.</summary>
        static void WriteUnder(MenuEntry under, string tag, MenuEntry face, Wanted wanted, List<ConditionGroup> groups, Func<SkinnedMeshRenderer, string, float> rest)
        {
            Companion(under, face, tag, groups);
            under.blendShapes = wanted.Shapes.Select(s => new BlendShapeTarget { renderer = s.renderer, shape = s.shape, weight = Mathf.Clamp(rest(s.renderer, s.shape), 0f, 100f), mode = ShapeEffectMode.Pin }).ToList();
            under.objects = new List<ObjectTarget>();
            under.materials = new List<MaterialTarget>();
            under.properties = new List<MaterialPropertyTarget>();
            under.follows = new List<FollowTarget>();
            under.shapeSets = new List<ShapeSetRule>();
        }

        static T Clone<T>(T value) => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));

        /// <summary>What a trigger that goes with a face shares with it: a Hold trigger at the root, its rule, and the face's own gate.</summary>
        static void Companion(MenuEntry entry, MenuEntry face, string tag, List<ConditionGroup> groups)
        {
            entry.kind = EntryKind.Trigger;
            entry.parentId = "";
            entry.gestureFace = tag;
            groups[0].all[0].CopyToEntry(entry);
            entry.when = groups;
            entry.touchMode = TouchMode.Hold;
            entry.gateToggleId = face.gateToggleId;
            entry.gateToggleOn = face.gateToggleOn;
        }

        /// <summary>
        /// The cards of the triggers that hold a losing face at rest: each one is the face's gesture and a run of the
        /// other hand's gestures that have a face, Greater and Less around a run of two or more. While face tracking
        /// is on a Paused face on the other hand shows nothing, so its run also asks for the tracking parameter to be
        /// off (as every card does when the losing face is Paused itself). A winning face with conditions or an Only
        /// while of the creator's own gets a card of its own that asks for them too, and every card asks for the
        /// losing face's own conditions, so the face is held only while both faces would show. A card past what one
        /// may hold is left out. Split into as many triggers as it takes to keep each within what one layer may hold.
        /// </summary>
        static List<List<ConditionGroup>> UnderGroups(Written losing, List<Written> winning, string tracking)
        {
            var face = losing.Face;
            var other = GestureParameter(winning[0].Face.Hand);
            bool tracked = tracking.Length > 0;
            bool selfPaused = tracked && face.Tracking == GestureTracking.Paused;
            var groups = new List<ConditionGroup>();
            void AddCard(List<WhenCondition> otherHand, bool whileUntracked, IEnumerable<WhenCondition> extra)
            {
                var all = new List<WhenCondition> { Parameter(GestureParameter(face.Hand), ParameterCondition.Equals, face.Gesture) };
                all.AddRange(otherHand);
                if (whileUntracked) all.Add(Parameter(tracking, ParameterCondition.IsFalse, 0f));
                all.AddRange(extra);
                all.AddRange(losing.Own.Select(Clone));
                if (all.Count <= ConditionLogic.MaxPerGroup) groups.Add(new ConditionGroup { all = all });
            }
            List<WhenCondition> OtherHand(int low, int high)
            {
                if (low == high) return new List<WhenCondition> { Parameter(other, ParameterCondition.Equals, low) };
                var range = new List<WhenCondition> { Parameter(other, ParameterCondition.Greater, low - 1) };
                if (high < GestureCount) range.Add(Parameter(other, ParameterCondition.Less, high + 1));
                return range;
            }
            bool Untracked(Written w) => selfPaused || (tracked && w.Face.Tracking == GestureTracking.Paused);
            // A gate other than the one the losing face's trigger has, the menu switch aside when both share it.
            WhenCondition OwnGate(Written w)
            {
                var gate = w.Trigger.gateToggleId;
                if (string.IsNullOrEmpty(gate) || (gate == losing.Trigger.gateToggleId && w.Trigger.gateToggleOn == losing.Trigger.gateToggleOn)) return null;
                return new WhenCondition { kind = WhenConditionKind.Toggle, sourceToggleId = gate, sourceToggleOn = w.Trigger.gateToggleOn };
            }
            var plain = winning.Where(w => w.Own.Count == 0 && OwnGate(w) == null).ToList();
            foreach (var untracked in new[] { false, true })
                foreach (var (low, high) in Runs(plain.Where(w => Untracked(w) == untracked).Select(w => w.Face.Gesture)))
                    AddCard(OtherHand(low, high), untracked, Enumerable.Empty<WhenCondition>());
            foreach (var w in winning.Except(plain))
            {
                var extra = w.Own.Select(Clone).ToList();
                var gate = OwnGate(w);
                if (gate != null) extra.Add(gate);
                AddCard(OtherHand(w.Face.Gesture, w.Face.Gesture), Untracked(w), extra);
            }

            // The gate adds one way out of its own, so the product of the cards stays one under the cap.
            var chunks = new List<List<ConditionGroup>>();
            var chunk = new List<ConditionGroup>();
            long leave = 1;
            foreach (var card in groups)
            {
                if (chunk.Count > 0 && (chunk.Count == ConditionLogic.MaxGroups || leave * card.all.Count > ConditionLogic.MaxLeaveTransitions - 1))
                {
                    chunks.Add(chunk);
                    chunk = new List<ConditionGroup>();
                    leave = 1;
                }
                chunk.Add(card);
                leave *= card.all.Count;
            }
            if (chunk.Count > 0) chunks.Add(chunk);
            return chunks;
        }

        static IEnumerable<(int low, int high)> Runs(IEnumerable<int> values)
        {
            var sorted = values.Distinct().OrderBy(v => v).ToList();
            for (int i = 0; i < sorted.Count;)
            {
                int j = i;
                while (j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1) j++;
                yield return (sorted[i], sorted[j]);
                i = j + 1;
            }
        }

        /// <summary>The first locked component of the avatar, other than this one, with an entry that names the toggle: a link the lock keeps Make from taking away.</summary>
        static AvatarDoc LockedUserOf(Transform root, AvatarDoc component, string id)
        {
            if (root == null) return null;
            foreach (var doc in root.GetComponentsInChildren<AvatarDoc>(true))
            {
                if (doc == null || doc == component || !EntryEditing.Refuses(doc)) continue;
                foreach (var entry in doc.entries)
                    if (entry != null && (entry.gateToggleId == id || entry.flipToggleId == id || entry.sourceToggleId == id
                        || entry.StoredConditions().Any(c => c.condition.kind == WhenConditionKind.Toggle && c.condition.sourceToggleId == id)
                        || (entry.presetStates != null && entry.presetStates.Any(s => s != null && s.entryId == id))))
                        return doc;
            }
            return null;
        }

        /// <summary>Takes every link to a toggle that is gone away from the avatar's entries, in the current Undo step: a gate, a flip target, a source, a condition card's row and a preset's state.</summary>
        static void ForgetToggle(Transform root, AvatarDoc component, string id)
        {
            var components = root != null ? root.GetComponentsInChildren<AvatarDoc>(true) : new[] { component };
            foreach (var doc in components)
            {
                if (doc == null || (doc != component && EntryEditing.Refuses(doc))) continue;
                bool touched = false;
                void Touch()
                {
                    if (!touched && doc != component) Undo.RecordObject(doc, "Gesture faces");
                    touched = true;
                }
                foreach (var entry in doc.entries)
                {
                    if (entry == null) continue;
                    var cards = entry.StoredConditions().Where(c => c.condition.kind == WhenConditionKind.Toggle && c.condition.sourceToggleId == id)
                        .Select(c => (c.groupIndex, c.index)).Reverse().ToList();
                    foreach (var (groupIndex, index) in cards)
                        if (EntryEditing.RemoveCondition(doc, entry, groupIndex, index)) touched = true;
                    if (entry.gateToggleId == id)
                    {
                        Touch();
                        entry.gateToggleId = "";
                        entry.gateToggleOn = true;
                    }
                    if (entry.flipToggleId == id)
                    {
                        Touch();
                        entry.flipToggleId = "";
                    }
                    if (entry.sourceToggleId == id)
                    {
                        Touch();
                        entry.sourceToggleId = "";
                    }
                    if (entry.presetStates != null && entry.presetStates.Any(s => s != null && s.entryId == id))
                    {
                        Touch();
                        entry.presetStates.RemoveAll(s => s != null && s.entryId == id);
                    }
                }
                if (touched) EntryEditing.MarkChanged(doc);
            }
        }

        /// <summary>Whether a condition is one the window writes itself: on a gesture parameter, or the tracking parameter (the one asked for now, or the one the faces were made with until now) being off.</summary>
        static bool Ours(WhenCondition condition, string tracking, string oldTracking)
        {
            if (condition.kind != WhenConditionKind.Parameter) return false;
            var name = (condition.sourceParameter ?? "").Trim();
            if (GestureParameters.Contains(name)) return true;
            return condition.condition == ParameterCondition.IsFalse && name.Length > 0 && (name == tracking || name == oldTracking);
        }

        static WhenCondition Parameter(string name, ParameterCondition comparison, float value)
            => new WhenCondition { kind = WhenConditionKind.Parameter, sourceParameter = name, condition = comparison, conditionValue = value };
    }
}
#endif
