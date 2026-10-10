#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>One parameter of a removed control: freed, or kept with the reason it had to stay.</summary>
    public sealed class RemovedParameter
    {
        public string Name = "";
        /// <summary>The build leaves this parameter out of the expression parameters.</summary>
        public bool Freed;
        /// <summary>Synced bits the avatar gets back when this parameter is freed.</summary>
        public int Bits;
        /// <summary>Localisation key saying what keeps the parameter, empty when it is freed.</summary>
        public string KeptBecause = "";
        /// <summary>FX layers that go with this parameter. Empty when nothing can be removed.</summary>
        public List<string> Layers = new List<string>();
        /// <summary>The edits that asked for the removal, so every row that shares the parameter shows the result.</summary>
        public List<MenuEdit> Edits = new List<MenuEdit>();
    }

    /// <summary>What "Remove from avatar" takes out of the built avatar: parameters, FX layers, and what had to stay.</summary>
    public sealed class RemovalPlan
    {
        public List<RemovedParameter> Parameters = new List<RemovedParameter>();
        /// <summary>FX layer names the build leaves out, each driven only by freed parameters.</summary>
        public List<string> Layers = new List<string>();
        /// <summary>The avatar uses an animator layer control, so no layer can be removed without moving what it points at.</summary>
        public bool LayerControlBlocks;
        /// <summary>Layers that would have gone but for the animator layer control, for the warning that says so.</summary>
        public List<string> BlockedLayers = new List<string>();

        public int FreedBits => Parameters.Where(p => p.Freed).Sum(p => p.Bits);

        /// <summary>The parameters one edit asked to remove. A parameter two removed controls share answers to both.</summary>
        public IEnumerable<RemovedParameter> For(MenuEdit edit)
            => edit == null ? Enumerable.Empty<RemovedParameter>() : Parameters.Where(p => p.Edits.Any(e => e == edit || e.id == edit.id));
    }

    /// <summary>
    /// Decides what a removed control takes with it. Pure: it reads the avatar's assets and components and writes
    /// nothing, so the inspector and the build reach the same answer. Every check is separate and named, and a
    /// parameter whose readers cannot all be accounted for stays: freeing one that something still needs would
    /// break the avatar without a word.
    /// </summary>
    public static class RemovalPlanner
    {
        /// <summary>How often the full removal walk actually ran, for a test to prove a reorder does not repeat it.</summary>
        public static int PlanCalls { get; private set; }

        /// <summary>
        /// fresh skips every shared, inspector-facing cache (ParameterUsage's and LayerAnalysis's), reading the
        /// avatar's controllers directly instead: the build's own removal plan passes it, since its clone is
        /// thrown away as soon as the build ends and must never be answered from, or leave anything behind in,
        /// whatever the inspector has already cached for the live scene's own controllers. This costs one extra,
        /// uncached deserialisation pass over the FX controller and every merged one per build; negligible next to
        /// the rest of what a build already does, and paid only once, not once per avatar in the project.
        /// </summary>
        public static RemovalPlan Plan(MenuTree tree, VRCAvatarDescriptor descriptor, bool fresh = false)
        {
            PlanCalls++;
            var result = new RemovalPlan();
            // Without an avatar there is nothing to read the rest of the avatar from, and a check that sees
            // nothing would free everything.
            if (tree == null || tree.Root == null || descriptor == null || tree.AvatarRoot == null) return result;

            var removed = RemovedItems(tree.Root).ToList();
            if (removed.Count == 0) return result;

            var declared = DeclaredParameters(descriptor);
            // Every name the removed controls carry, whether or not it can be freed: a layer is only a candidate
            // when everything it reads is going away with the controls. A name the expression parameters do not
            // declare costs no synced bits, so removing the control frees nothing and the planner says nothing.
            var candidates = new List<(string name, List<MenuEdit> edits)>();
            var candidateNames = new HashSet<string>();
            foreach (var item in removed)
                foreach (var name in NamesOf(item.Existing))
                {
                    candidateNames.Add(name);
                    if (!declared.ContainsKey(name)) continue;
                    int found = candidates.FindIndex(c => c.name == name);
                    if (found < 0) candidates.Add((name, new List<MenuEdit> { item.Edit }));
                    else if (!candidates[found].edits.Contains(item.Edit)) candidates[found].edits.Add(item.Edit);
                }
            if (candidates.Count == 0) return result;

            var fx = ParametersWriter.FxOf(descriptor);
            var usage = ParameterUsage.For(tree.AvatarRoot, tree, fresh);

            result.LayerControlBlocks = FxLayerImport.AnyLayerControlTargetsFx(descriptor, fresh);
            var layerReads = new Dictionary<string, List<string>>();
            var removableLayers = RemovableLayers(fx, candidateNames, layerReads, fresh);
            if (result.LayerControlBlocks)
            {
                result.BlockedLayers.AddRange(removableLayers);
                removableLayers.Clear();
            }

            // A layer only goes when every parameter it reads is really freed, and a parameter is only freed when
            // no layer that stays reads it. Deciding both at once would be circular, so the layer set shrinks until
            // it stops changing: each pass can only take layers away, never add them back. A parameter that already
            // has a reason keeps it, so the inspector names what really holds the parameter rather than the layer
            // that only stayed behind because of it.
            var decided = candidates.Select(c => Decide(c.name, c.edits, usage, declared, removed, removableLayers, fx)).ToList();
            while (true)
            {
                var freed = new HashSet<string>(decided.Where(p => p.Freed).Select(p => p.Name));
                var kept = removableLayers.Where(layer => layerReads[layer].All(freed.Contains)).ToList();
                if (kept.Count == removableLayers.Count) break;
                removableLayers = kept;
                for (int i = 0; i < decided.Count; i++)
                    if (decided[i].Freed)
                        decided[i] = Decide(candidates[i].name, candidates[i].edits, usage, declared, removed, removableLayers, fx);
            }

            foreach (var parameter in decided)
            {
                if (parameter.Freed)
                    parameter.Layers.AddRange(removableLayers.Where(layer => layerReads[layer].Contains(parameter.Name)));
                result.Parameters.Add(parameter);
            }
            result.Layers.AddRange(removableLayers);
            return result;
        }

        /// <summary>One parameter's verdict. The checks run in the order the spec lists them, so the reason the inspector shows is always the same one.</summary>
        static RemovedParameter Decide(string name, List<MenuEdit> edits, ParameterUsage usage, IReadOnlyDictionary<string, VRCExpressionParameters.Parameter> declared,
            IReadOnlyList<MenuItem> removed, IReadOnlyList<string> removableLayers, AnimatorController fx)
        {
            var result = new RemovedParameter { Name = name };
            result.Edits.AddRange(edits);
            result.Bits = BitsOf(declared[name]);

            if (TriggerPlanner.BuiltInType(name).HasValue)
            {
                result.KeptBecause = "ad.removal.kept.builtin";
                return result;
            }

            foreach (var kind in new[] { UsageKind.MenuControl, UsageKind.Entry, UsageKind.OtherTool, UsageKind.Layer, UsageKind.OtherController, UsageKind.Driver, UsageKind.Contact })
            {
                foreach (var reader in usage.Readers(name))
                {
                    if (reader.Kind != kind) continue;
                    if (kind == UsageKind.MenuControl && removed.Any(item => ReferenceEquals(item, reader.Owner))) continue;
                    // Only a layer of the avatar's own FX controller goes away with the removal. Layer names repeat
                    // across controllers, so a reader anywhere else keeps the parameter whatever its layer is called.
                    if ((kind == UsageKind.Layer || kind == UsageKind.Driver)
                        && fx != null && ReferenceEquals(reader.Owner, fx) && removableLayers.Contains(reader.Layer)) continue;
                    result.KeptBecause = KeyOf(kind);
                    return result;
                }
            }

            result.Freed = true;
            return result;
        }

        static string KeyOf(UsageKind kind)
        {
            switch (kind)
            {
                case UsageKind.MenuControl: return "ad.removal.kept.control";
                case UsageKind.Entry: return "ad.removal.kept.entry";
                case UsageKind.Layer: return "ad.removal.kept.layer";
                case UsageKind.Driver: return "ad.removal.kept.driver";
                case UsageKind.Contact: return "ad.removal.kept.contact";
                case UsageKind.OtherTool: return "ad.removal.kept.other_tool";
                default: return "ad.removal.kept.other_controller";
            }
        }

        /// <summary>
        /// FX layers that read nothing but the candidate names and are safe to leave out whatever their states
        /// look like. layerReads collects, per layer, which candidate names it reads, so the caller can hold a
        /// layer back once one of those parameters turns out to stay. What each layer reads is asked from
        /// LayerAnalysis's own per-controller cache, already keyed by name there, rather than walked or re-keyed
        /// here: the candidate set is the only thing that changes call to call and the layer's own shape and reads
        /// never do until the asset reimports. Matched back to fx.layers by the layer's own name rather than by
        /// position: that cache is kept per controller instance until a reimport, so a layer inserted or removed by
        /// the Animator window in between would otherwise pair a stale entry with whatever layer now happens to sit
        /// at that index. Name is already the identity the build itself takes a layer out by (LayerShapeProblem
        /// refuses a repeated one for the same reason), so keying on it costs nothing this system does not already
        /// rely on.
        /// </summary>
        static List<string> RemovableLayers(AnimatorController fx, HashSet<string> candidates, Dictionary<string, List<string>> layerReads, bool fresh = false)
        {
            var result = new List<string>();
            if (fx == null) return result;
            var infosByName = LayerAnalysis.LayerReadsByNameOf(fx, fresh);
            // Each read of AnimatorController.layers builds a new array of every layer.
            var layers = fx.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                if (!infosByName.TryGetValue(layer.name, out var info)) continue;
                if (layer.stateMachine == null || info.ShapeProblem != null) continue;
                if (info.Parameters.Count == 0 || !info.Parameters.All(candidates.Contains)) continue;
                layerReads[layer.name] = info.Parameters;
                result.Add(layer.name);
            }
            return result;
        }

        /// <summary>Existing, non foreign items a menu edit removes. A foreign item belongs to another tool, which writes it after AvatarDoctor has run.</summary>
        static IEnumerable<MenuItem> RemovedItems(MenuNode node)
        {
            foreach (var item in node.Items)
            {
                if (item.Existing != null && item.Foreign == ForeignSource.None && item.Edit != null && item.Edit.remove)
                    yield return item;
                if (item.Submenu != null)
                    foreach (var found in RemovedItems(item.Submenu))
                        yield return found;
            }
        }

        static IEnumerable<string> NamesOf(VRCExpressionsMenu.Control control)
        {
            if (control.parameter != null && !string.IsNullOrWhiteSpace(control.parameter.name)) yield return control.parameter.name.Trim();
            if (control.subParameters == null) yield break;
            foreach (var sub in control.subParameters)
                if (sub != null && !string.IsNullOrWhiteSpace(sub.name)) yield return sub.name.Trim();
        }

        static Dictionary<string, VRCExpressionParameters.Parameter> DeclaredParameters(VRCAvatarDescriptor descriptor)
        {
            var result = new Dictionary<string, VRCExpressionParameters.Parameter>();
            var asset = descriptor.expressionParameters;
            if (asset == null || asset.parameters == null) return result;
            foreach (var parameter in asset.parameters)
                if (parameter != null && !string.IsNullOrEmpty(parameter.name)) result[parameter.name] = parameter;
            return result;
        }

        static int BitsOf(VRCExpressionParameters.Parameter parameter)
        {
            if (!parameter.networkSynced) return 0;
            return parameter.valueType == VRCExpressionParameters.ValueType.Bool ? 1 : 8;
        }
    }
}
#endif
