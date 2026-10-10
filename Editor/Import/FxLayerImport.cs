#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Converts a Toggle control of the avatar's own menu, driven by an FX layer, into an import candidate. Conservative by design: a build removes the origin layer, so anything that cannot be read with confidence is unsupported rather than guessed at.</summary>
    public static class FxLayerImport
    {
        public static List<ImportCandidate> Scan(Transform avatarRoot, MenuTree tree, ParameterUsage usage = null)
        {
            var result = new List<ImportCandidate>();
            if (avatarRoot == null || tree == null || tree.Root == null) return result;

            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            var fx = ParametersWriter.FxOf(descriptor);
            if (fx == null) return result;

            var existing = ParametersWriter.ExistingParameters(descriptor.expressionParameters, fx);
            bool layerControlUnsafe = AnyLayerControlTargetsFx(descriptor);
            var converted = ConvertedLayers(avatarRoot);
            var reading = usage ?? ParameterUsage.For(avatarRoot, tree);
            foreach (var (path, item) in ExistingToggles(tree.Root, new List<string>()))
            {
                var candidate = Build(avatarRoot, tree, fx, existing, path, item, layerControlUnsafe, converted, reading);
                if (candidate != null) result.Add(candidate);
            }
            return result;
        }

        /// <summary>Layer names an earlier import already left out of the build, from every AvatarDoc on the avatar. Offering one of those again would make a second entry on the same parameter.</summary>
        internal static HashSet<string> ConvertedLayers(Transform avatarRoot)
        {
            var result = new HashSet<string>();
            if (avatarRoot == null) return result;
            foreach (var component in avatarRoot.GetComponentsInChildren<AvatarDoc>(true))
                foreach (var removed in component.removedLayers)
                    if (removed != null && !string.IsNullOrEmpty(removed.layer)) result.Add(removed.layer);
            return result;
        }

        static IEnumerable<(List<string> path, MenuItem item)> ExistingToggles(MenuNode node, List<string> path)
        {
            foreach (var item in node.Items)
            {
                if (item.Submenu != null)
                {
                    var childPath = new List<string>(path) { item.Submenu.Name };
                    foreach (var pair in ExistingToggles(item.Submenu, childPath)) yield return pair;
                }
                else if (item.Existing != null && item.Foreign == ForeignSource.None &&
                         item.Existing.type == VRCExpressionsMenu.Control.ControlType.Toggle)
                {
                    yield return (path, item);
                }
            }
        }

        static ImportCandidate Build(Transform avatarRoot, MenuTree tree, AnimatorController fx,
            IReadOnlyDictionary<string, ExistingParameter> existing, List<string> path, MenuItem item, bool layerControlUnsafe,
            HashSet<string> converted, ParameterUsage usage)
        {
            var control = item.Existing;
            var paramName = control.parameter != null ? control.parameter.name : "";
            if (string.IsNullOrEmpty(paramName)) return null;
            if (TriggerPlanner.BuiltInType(paramName).HasValue) return null;

            var candidate = new ImportCandidate
            {
                Source = ImportSource.FxLayer,
                Path = new List<string>(path),
                OriginControlPath = MenuEdits.SourceOf(item),
                OriginOccurrence = MenuEditing.OccurrenceOf(tree, item),
            };
            var richName = string.IsNullOrEmpty(item.RichName) ? item.Name : item.RichName;
            var draft = MenuEntry.Create(EntryKind.Toggle, richName);
            candidate.Draft = draft;
            draft.parameterName = paramName;
            draft.parameterValue = control.value;
            draft.icon = control.icon;
            bool? declaredDefaultOn = null;
            if (existing.TryGetValue(paramName, out var declared) && declared.IsExpressionParameter)
            {
                draft.defaultOn = Mathf.Approximately(declared.Default, control.value);
                declaredDefaultOn = draft.defaultOn;
            }

            // A control an earlier import hid is that import's leftover, not something still waiting to be converted.
            if (item.Hidden)
                return Fail(candidate, L.Text("ad.import.reason.already_converted"));

            if (layerControlUnsafe)
                return Fail(candidate, L.Text("ad.import.reason.layer_control"));

            if (OtherControlsShareParameter(tree.Root, paramName, item))
                return Fail(candidate, L.Text("ad.import.reason.shared_parameter_controls"));

            // The clips of the avatar's own FX controller already start at the avatar root.
            var verdict = LayerAnalysis.Toggle(fx, paramName, control.value, p => p, avatarRoot, declaredDefaultOn);
            candidate.OriginLayer = verdict.LayerName;

            if (converted != null && !string.IsNullOrEmpty(verdict.LayerName) && converted.Contains(verdict.LayerName))
                return Fail(candidate, L.Text("ad.import.reason.already_converted"));
            // Nothing the analysis read is carried over until it says yes: a row that cannot be ticked must not
            // advertise effects it will never create.
            if (!verdict.Convertible)
                return Fail(candidate, verdict.Reason);

            // The entry keeps the old name, but the layer that answered it is left out of the build and replaced
            // by one of AvatarDoc's own. Anything else still reading the name, on this menu or in a controller
            // another tool merges, would be driving something that no longer does what it did. The FX layer itself
            // and this very control are the two readers that go away with the conversion, so they do not count.
            if (usage != null && usage.UsedBesides(paramName, item, fx))
                return Fail(candidate, string.Format(L.Text("ad.import.reason.fx_parameter_shared"), paramName));

            candidate.Notes.AddRange(verdict.Notes);
            LayerAnalysis.CopyTargets(verdict.Draft, draft);
            if (verdict.Inverted) draft.defaultOn = true;
            candidate.Notes.Add(L.Text("ad.import.note.osc"));
            candidate.Supported = true;
            return candidate;
        }

        static ImportCandidate Fail(ImportCandidate candidate, string reason)
        {
            candidate.Supported = false;
            candidate.Reason = reason;
            return candidate;
        }

        /// <summary>True when another control anywhere in the avatar's own menu (any control type) names the same parameter, either as its own parameter or as one of a puppet's sub parameters (radial, two axis, four axis). A parameter more than one control drives is unsafe to convert one control at a time.</summary>
        internal static bool OtherControlsShareParameter(MenuNode root, string paramName, MenuItem self)
        {
            bool Walk(MenuNode node)
            {
                foreach (var item in node.Items)
                {
                    if (item != self && item.Existing != null && ControlNamesParameter(item.Existing, paramName))
                        return true;
                    if (item.Submenu != null && Walk(item.Submenu)) return true;
                }
                return false;
            }
            return Walk(root);
        }

        static bool ControlNamesParameter(VRCExpressionsMenu.Control control, string paramName)
        {
            if (control.parameter != null && control.parameter.name == paramName) return true;
            if (control.subParameters == null) return false;
            foreach (var sub in control.subParameters)
                if (sub != null && sub.name == paramName) return true;
            return false;
        }

        /// <summary>
        /// True when a VRCAnimatorLayerControl behaviour anywhere on the descriptor's playable layers targets the FX
        /// layer. Removing an FX layer would then shift the index it points at. Each controller's answer is kept by
        /// its instance id and GetDirtyCount (LayerAnalysis.UsesLayerControl), since every removal plan build asks;
        /// fresh reads every controller again and keeps nothing, for the build's own throwaway clone.
        /// </summary>
        internal static bool AnyLayerControlTargetsFx(VRCAvatarDescriptor descriptor, bool fresh = false)
        {
            if (descriptor == null) return false;
            foreach (var controller in AllControllersOf(descriptor))
                if (LayerAnalysis.UsesLayerControl(controller, fresh)) return true;
            return false;
        }

        internal static IEnumerable<AnimatorController> AllControllersOf(VRCAvatarDescriptor descriptor)
        {
            if (descriptor.baseAnimationLayers != null)
                foreach (var layer in descriptor.baseAnimationLayers)
                    if (layer.animatorController is AnimatorController ac) yield return ac;
            if (descriptor.specialAnimationLayers != null)
                foreach (var layer in descriptor.specialAnimationLayers)
                    if (layer.animatorController is AnimatorController ac) yield return ac;
        }
    }
}
#endif
