#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>A follow row resolved against the avatar: both meshes exist and both shapes are really on them.</summary>
    public sealed class ShapesFollowRow
    {
        public ShapeFollow Row;
        public SkinnedMeshRenderer Source;
        public string SourceShape;
        public string LocalShape;
    }

    /// <summary>A while-on row resolved against the avatar: the mesh is part of it and the shape is really on it.</summary>
    public sealed class ShapesWhileOnRow
    {
        public ShapeWhileOn Row;
        public SkinnedMeshRenderer Mesh;
        public string Shape;
        public float Value;
    }

    /// <summary>One AvatarDoc Shapes component with its rows resolved.</summary>
    public sealed class ShapesPiece
    {
        public AvatarDocShapes Component;
        public GameObject Object;
        /// <summary>The piece's own skinned mesh, the one follow rows write to. Null when the object carries none.</summary>
        public SkinnedMeshRenderer LocalMesh;
        /// <summary>Left out on the platform the editor is building for.</summary>
        public bool Excluded;
        public List<ShapesFollowRow> Follows = new List<ShapesFollowRow>();
        public List<ShapesWhileOnRow> WhileOn = new List<ShapesWhileOnRow>();

        public string Name => Component != null ? Component.gameObject.name : "";
    }

    /// <summary>One animation that turns a piece on or off: the clip's FX layer by its place, so a larger one plays after a smaller one, the clip itself, which tells the states of one layer apart, and whether it turns the piece on.</summary>
    public readonly struct ShapesSwitch
    {
        public readonly int Layer;
        public readonly object Clip;
        public readonly bool On;

        public ShapesSwitch(int layer, object clip, bool on)
        {
            Layer = layer;
            Clip = clip;
            On = on;
        }
    }

    /// <summary>The animations the build will find switching the objects of one avatar, read ahead of a build by ShapesModel.PredictSwitches.</summary>
    public sealed class PieceSwitches
    {
        readonly Transform avatarRoot;
        readonly Dictionary<GameObject, List<ShapesSwitch>> byObject;

        public PieceSwitches(Transform avatarRoot, Dictionary<GameObject, List<ShapesSwitch>> byObject)
        {
            this.avatarRoot = avatarRoot;
            this.byObject = byObject ?? new Dictionary<GameObject, List<ShapesSwitch>>();
        }

        /// <summary>
        /// The switches of one piece, found the way the build finds them: its own, or, when nothing animates it, its
        /// nearest animated parent's, as long as every object between is on. activeSelf reads the scene unless the
        /// caller reads it its own way, as the preview does.
        /// </summary>
        public IReadOnlyList<ShapesSwitch> For(GameObject piece, Func<GameObject, bool> activeSelf = null)
        {
            activeSelf = activeSelf ?? (go => go.activeSelf);
            for (var cursor = piece != null ? piece.transform : null; cursor != null; cursor = cursor.parent)
            {
                if (byObject.TryGetValue(cursor.gameObject, out var list)) return list;
                if (cursor == avatarRoot || !activeSelf(cursor.gameObject)) break;
            }
            return Array.Empty<ShapesSwitch>();
        }
    }

    /// <summary>Every AvatarDoc Shapes component of one avatar, resolved, with what is wrong with the rows.</summary>
    public sealed class ShapesPlan
    {
        public Transform AvatarRoot;
        public List<ShapesPiece> Pieces = new List<ShapesPiece>();
        public List<ModelMessage> Messages = new List<ModelMessage>();

        public IEnumerable<ShapesPiece> Included => Pieces.Where(p => !p.Excluded);
    }

    /// <summary>
    /// Reads the AvatarDoc Shapes components of an avatar the same way for the inspector and for the build: which
    /// rows really resolve, what is wrong with the rest, and which pieces fight over one shape. Pure: nothing is
    /// written, no clip and no scene value is touched. Which pieces fight depends on what switches them: the build
    /// reads its real FX layers, the inspector and the preview read them ahead from the avatar's FX controller and
    /// AvatarDoc's own controls and merged animators.
    /// </summary>
    public static class ShapesModel
    {
        /// <summary>How many rows a piece has, the way the Convert window and the summary say it: "2 follows, 1 while on".</summary>
        public static string RowCounts(int follows, int whileOn)
            => L.JoinList(new[] { UiText.Count(follows, "ad.ui.shapes.count.follows.one", "ad.ui.shapes.count.follows"), string.Format(L.Text("ad.ui.shapes.count.while_on"), whileOn) });

        /// <summary>Every enabled AvatarDoc Shapes component under this root, in hierarchy order.</summary>
        public static List<AvatarDocShapes> Components(Transform avatarRoot)
        {
            if (avatarRoot == null) return new List<AvatarDocShapes>();
            return avatarRoot.GetComponentsInChildren<AvatarDocShapes>(true).Where(c => c != null && c.enabled).ToList();
        }

        public static ShapesPlan Plan(Transform avatarRoot)
        {
            var plan = new ShapesPlan { AvatarRoot = avatarRoot };
            if (avatarRoot == null) return plan;
            foreach (var component in Components(avatarRoot))
                plan.Pieces.Add(Read(plan, avatarRoot, component));
            return plan;
        }

        /// <summary>
        /// The plan an inspector shows for one component: the avatar's whole plan, with the pieces that fight over a
        /// shape read from the switches the build will find. A piece outside an avatar gets its own rows only.
        /// </summary>
        public static ShapesPlan PlanFor(AvatarDocShapes component)
        {
            var root = component != null ? AvatarPlanCache.RootOf(component) : null;
            if (root != null)
            {
                var avatarPlan = Plan(root);
                var switches = AvatarPlanCache.Get(root).PieceSwitches;
                avatarPlan.Messages.AddRange(Conflicts(avatarPlan.Included, piece => switches.For(piece.Object), piece => ActiveInAvatar(piece.Object, root)));
                return avatarPlan;
            }
            var plan = new ShapesPlan();
            if (component != null) plan.Pieces.Add(Read(plan, null, component));
            return plan;
        }

        /// <summary>
        /// The animations the build will find switching objects, read before a build, numbered in the order the build
        /// puts their FX layers in: the avatar's own FX layers, then AvatarDoc's toggles and exclusive groups in menu
        /// order, its condition triggers, its merged animators, and last its Fade and blend shape triggers, whose
        /// layers go after every other one. Animators that Modular Avatar or VRCFury add at build time are not known
        /// here.
        /// </summary>
        public static PieceSwitches PredictSwitches(Transform avatarRoot, MenuTree tree, AnimationUsage usage)
        {
            var byObject = new Dictionary<GameObject, List<ShapesSwitch>>();
            void Add(GameObject target, int layer, object clip, bool on)
            {
                if (target == null) return;
                if (!byObject.TryGetValue(target, out var list)) byObject[target] = list = new List<ShapesSwitch>();
                list.Add(new ShapesSwitch(layer, clip, on));
            }
            void AddClip(Dictionary<GameObject, bool> clip, int layer)
            {
                var id = new object();
                foreach (var pair in clip) Add(pair.Key, layer, id, pair.Value);
            }
            // One controller's switches, its layers numbered from first on, without the layers left out of the build.
            int AddController(IEnumerable<AnimationUsage.FxSwitch> switches, int layerCount, Func<string, bool> leftOut, string prefix, int first)
            {
                foreach (var fxSwitch in switches)
                {
                    if (leftOut(fxSwitch.LayerName)) continue;
                    var path = prefix.Length == 0 ? fxSwitch.Path : fxSwitch.Path.Length == 0 ? prefix : prefix + "/" + fxSwitch.Path;
                    var target = path.Length == 0 ? avatarRoot : avatarRoot.Find(path);
                    if (target != null) Add(target.gameObject, first + fxSwitch.Layer, fxSwitch.Clip, fxSwitch.On);
                }
                return first + layerCount;
            }

            int layerIndex = 0;
            if (avatarRoot != null && usage != null)
            {
                var removed = new HashSet<string>(tree != null ? tree.Components.Where(c => c != null).SelectMany(c => c.removedLayers.Select(r => r.layer)) : Enumerable.Empty<string>());
                layerIndex = AddController(usage.FxSwitches, usage.FxLayerCount, removed.Contains, "", 0);
            }
            if (tree == null || avatarRoot == null) return new PieceSwitches(avatarRoot, byObject);

            var groups = new HashSet<GroupKey>();
            foreach (var control in tree.Controls)
            {
                var entry = control.Entry;
                if (control.Excluded || control.Disabled || entry.kind != EntryKind.Toggle || entry.IsParameterControl) continue;
                var group = tree.GroupOf(control);
                if (group.IsNone)
                {
                    if (entry.objects.Count == 0) continue;
                    AddClip(ObjectStates(entry, true), layerIndex);
                    AddClip(ObjectStates(entry, false), layerIndex);
                    layerIndex++;
                    continue;
                }
                if (!groups.Add(group)) continue;
                var members = tree.Controls
                    .Where(c => !c.Excluded && !c.Disabled && tree.GroupOf(c) == group)
                    .Select(c => c.Entry).ToList();
                var off = new Dictionary<GameObject, bool>();
                foreach (var member in members) WriteObjects(off, member, false);
                AddClip(off, layerIndex);
                foreach (var member in members)
                {
                    var state = new Dictionary<GameObject, bool>();
                    foreach (var other in members)
                        if (other != member) WriteObjects(state, other, false);
                    WriteObjects(state, member, true);
                    AddClip(state, layerIndex);
                }
                layerIndex++;
            }

            void AddTriggers(Func<MenuEntry, bool> kind)
            {
                foreach (var trigger in tree.Triggers)
                {
                    var entry = trigger.Entry;
                    if (trigger.Excluded || entry.objects.Count == 0 || !kind(entry)) continue;
                    AddClip(ObjectStates(entry, true), layerIndex);
                    AddClip(ObjectStates(entry, false), layerIndex);
                    layerIndex++;
                }
            }
            AddTriggers(entry => !entry.IsFlipTrigger && !entry.HasDriver);

            foreach (var node in tree.Merges)
            {
                if (node.Excluded || node.Merge == null || MenuModel.IsOutsideAvatar(avatarRoot, node.Merge.pathRoot)) continue;
                var controller = MergeBuilder.Resolve(node.Merge.controller);
                if (controller == null) continue;
                var skipped = new HashSet<string>(node.Merge.skipLayers.Where(name => !string.IsNullOrEmpty(name)));
                layerIndex = AddController(AnimationUsage.SwitchesIn(controller), controller.layers.Length, skipped.Contains,
                    CustomClips.PrefixFor(avatarRoot, node.Merge.pathRoot), layerIndex);
            }

            AddTriggers(entry => entry.IsFollowDistanceTrigger);
            AddTriggers(entry => entry.kind == EntryKind.Trigger && entry.source == TriggerSource.BlendShape);
            return new PieceSwitches(avatarRoot, byObject);
        }

        static Dictionary<GameObject, bool> ObjectStates(MenuEntry entry, bool on)
        {
            var states = new Dictionary<GameObject, bool>();
            WriteObjects(states, entry, on);
            return states;
        }

        static void WriteObjects(Dictionary<GameObject, bool> states, MenuEntry entry, bool on)
        {
            foreach (var target in entry.objects)
                if (target != null && target.target != null) states[target.target] = EffectMath.ObjectActive(target, on);
        }

        static ShapesPiece Read(ShapesPlan plan, Transform avatarRoot, AvatarDocShapes component)
        {
            var piece = new ShapesPiece
            {
                Component = component,
                Object = component.gameObject,
                LocalMesh = component.GetComponent<SkinnedMeshRenderer>(),
                Excluded = !Platform.Included(component.platform),
            };
            string name = piece.Name;

            if (piece.LocalMesh == null && component.follow.Any(r => r != null))
                plan.Messages.Add(ModelMessage.Error("ad.error.shapes_no_mesh", name, component));

            foreach (var row in component.follow)
            {
                if (row == null) continue;
                if (piece.LocalMesh == null) continue;
                string source = (row.sourceShape ?? "").Trim();
                string local = row.EffectiveLocalShape;
                if (row.sourceMesh == null || MenuModel.IsOutsideAvatar(avatarRoot, row.sourceMesh.transform))
                {
                    plan.Messages.Add(ModelMessage.Error("ad.error.shapes_outside_avatar", name, source, component));
                    continue;
                }
                if (!MenuModel.ShapeExists(row.sourceMesh, source))
                {
                    plan.Messages.Add(ModelMessage.Warning("ad.warn.shapes_missing_shape", name, row.sourceMesh.name, source, component));
                    continue;
                }
                if (!MenuModel.ShapeExists(piece.LocalMesh, local))
                {
                    plan.Messages.Add(ModelMessage.Warning("ad.warn.shapes_missing_shape", name, piece.LocalMesh.name, local, component));
                    continue;
                }
                piece.Follows.Add(new ShapesFollowRow { Row = row, Source = row.sourceMesh, SourceShape = source, LocalShape = local });
            }

            foreach (var row in component.whileOn)
            {
                if (row == null) continue;
                string shape = (row.shape ?? "").Trim();
                if (row.mesh == null || MenuModel.IsOutsideAvatar(avatarRoot, row.mesh.transform))
                {
                    plan.Messages.Add(ModelMessage.Error("ad.error.shapes_outside_avatar", name, shape, component));
                    continue;
                }
                if (!MenuModel.ShapeExists(row.mesh, shape))
                {
                    plan.Messages.Add(ModelMessage.Warning("ad.warn.shapes_missing_shape", name, row.mesh.name, shape, component));
                    continue;
                }
                piece.WhileOn.Add(new ShapesWhileOnRow { Row = row, Mesh = row.mesh, Shape = shape, Value = Mathf.Clamp(row.value, 0f, 100f) });
            }
            return piece;
        }

        /// <summary>
        /// What the build does with pieces that hold one shape. Pieces switched in different FX layers fight: with
        /// Write Defaults off the later layer decides the shape, and its off state puts the shape back while the other
        /// piece is worn, whatever the two values are, so the pair gets a warning that names the piece that wins.
        /// Pieces that one layer switches share it: where one of its animations turns both on at different values,
        /// the larger value holds, and a note says so. A piece nothing switches stays as the scene has it, under
        /// every layer. Both pieces' components ride along, so each piece's inspector shows the message.
        /// </summary>
        public static List<ModelMessage> Conflicts(IEnumerable<ShapesPiece> pieces, Func<ShapesPiece, IReadOnlyList<ShapesSwitch>> switchesOf, Func<ShapesPiece, bool> onInScene)
        {
            var messages = new List<ModelMessage>();
            var holders = new Dictionary<(SkinnedMeshRenderer, string), List<(ShapesPiece piece, float value)>>();
            var order = new List<(SkinnedMeshRenderer, string)>();
            foreach (var piece in pieces)
                foreach (var row in piece.WhileOn)
                {
                    var key = (row.Mesh, row.Shape);
                    if (!holders.TryGetValue(key, out var list))
                    {
                        holders[key] = list = new List<(ShapesPiece, float)>();
                        order.Add(key);
                    }
                    int at = list.FindIndex(h => h.piece == piece);
                    if (at >= 0) list[at] = (piece, Mathf.Max(list[at].value, row.Value));
                    else list.Add((piece, row.Value));
                }

            var known = new Dictionary<ShapesPiece, IReadOnlyList<ShapesSwitch>>();
            IReadOnlyList<ShapesSwitch> SwitchesOf(ShapesPiece piece)
            {
                if (!known.TryGetValue(piece, out var found)) known[piece] = found = switchesOf(piece) ?? Array.Empty<ShapesSwitch>();
                return found;
            }
            List<int> LayersOf(ShapesPiece piece)
            {
                var switches = SwitchesOf(piece);
                return switches.Count > 0 ? switches.Select(s => s.Layer).Distinct().ToList() : new List<int> { -1 };
            }
            bool EverOn(ShapesPiece piece) => SwitchesOf(piece).Count > 0 || onInScene(piece);

            foreach (var key in order)
            {
                var list = holders[key];
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        var (a, valueA) = list[i];
                        var (b, valueB) = list[j];
                        if (!EverOn(a) || !EverOn(b)) continue;
                        var layersA = LayersOf(a);
                        var layersB = LayersOf(b);
                        if (layersA.Union(layersB).Count() > 1)
                        {
                            bool laterB = layersB.Max() >= layersA.Max();
                            var loser = laterB ? a : b;
                            var winner = laterB ? b : a;
                            messages.Add(ModelMessage.Warning("ad.warn.shapes_conflict", loser.Name, winner.Name, key.Item2, loser.Component, winner.Component));
                            continue;
                        }
                        if (Mathf.Approximately(valueA, valueB)) continue;
                        bool together = layersA[0] == -1 || SwitchesOf(a).Any(x => x.On && SwitchesOf(b).Any(y => y.On && Equals(x.Clip, y.Clip)));
                        if (together)
                            messages.Add(ModelMessage.Info("ad.info.shapes_larger_wins", a.Name, b.Name, key.Item2,
                                Mathf.Max(valueA, valueB).ToString("0.##", CultureInfo.InvariantCulture), a.Component, b.Component));
                    }
            }
            return messages;
        }

        /// <summary>Whether this object is on in the scene: itself and everything above it up to the avatar root.</summary>
        public static bool ActiveInAvatar(GameObject target, Transform avatarRoot)
        {
            if (target == null) return false;
            for (var cursor = target.transform; cursor != null; cursor = cursor.parent)
            {
                if (!cursor.gameObject.activeSelf) return false;
                if (cursor == avatarRoot) break;
            }
            return true;
        }
    }
}
#endif
