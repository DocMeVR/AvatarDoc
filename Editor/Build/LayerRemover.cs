#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Removes FX layers an import marked for removal, and the ones a removal plan leaves out, from the virtual FX controller. The FX controller asset is never touched.</summary>
    public static class LayerRemover
    {
        public static void Run(AnimatorServicesContext asc, IReadOnlyList<AvatarDoc> components, List<ModelMessage> messages, RemovalPlan removal = null)
        {
            if (asc == null || components == null) return;
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return;

            // A VRCAnimatorLayerControl behaviour refers to a layer by its index, wherever it lives on the avatar.
            // Removing a layer would shift every index after it, so nothing is removed once one exists.
            bool unsafeToRemove = AnyFxLayerControl(asc);

            foreach (var component in components)
            {
                if (component == null) continue;
                foreach (var removed in component.removedLayers)
                {
                    var layerName = removed?.layer;
                    if (string.IsNullOrEmpty(layerName)) continue;
                    if (unsafeToRemove)
                    {
                        messages.Add(ModelMessage.Warning("ad.warn.converted_layer_unsafe", layerName, component));
                        continue;
                    }
                    // Removing by name would take every layer that carries it, so a repeated name is left alone.
                    int matches = fx.Layers.Count(l => l.Name == layerName);
                    if (matches == 1)
                        fx.RemoveLayers(l => l.Name == layerName);
                    else if (matches > 1)
                        messages.Add(ModelMessage.Warning("ad.warn.removed_layer_ambiguous", layerName, matches.ToString(), component));
                    else
                        messages.Add(ModelMessage.Warning("ad.warn.removed_layer_missing", layerName, component));
                }
            }

            if (removal == null) return;
            var owner = components.FirstOrDefault(c => c != null);
            // A layer control the planner could not see, in a controller another tool merged, blocks the removal
            // only here. The layer then stays, so its parameter has to stay declared with it.
            if (unsafeToRemove) KeepBlocked(removal);
            foreach (var layerName in removal.BlockedLayers)
                messages.Add(ModelMessage.Warning("ad.warn.removed_layer_unsafe", layerName, owner));
            // The builder already ran this before it counted the budget, so here it only serves a direct caller.
            messages.AddRange(KeepAmbiguous(asc, removal, owner));
            foreach (var layerName in removal.Layers)
            {
                if (string.IsNullOrEmpty(layerName)) continue;
                // Zero matches means an import also named the layer and it is already gone, which is the result
                // the plan asked for. More than one was taken back by KeepAmbiguous and is no longer in the list.
                if (fx.Layers.Count(l => l.Name == layerName) == 1) fx.RemoveLayers(l => l.Name == layerName);
            }
        }

        /// <summary>
        /// Takes back every planned layer the FX controller now carries more than once. The planner only ever names
        /// a layer whose name is unique in the controller asset, but the clone another tool handed on may hold a
        /// second one by now, and removing by name would take that one too. The layer stays and the parameters
        /// freed for it are declared again, so this has to run before the budget counts the freed bits.
        /// </summary>
        public static List<ModelMessage> KeepAmbiguous(AnimatorServicesContext asc, RemovalPlan removal, AvatarDoc owner)
        {
            var messages = new List<ModelMessage>();
            if (asc == null || removal == null) return messages;
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return messages;
            foreach (var layerName in removal.Layers.ToList())
            {
                int matches = fx.Layers.Count(l => l.Name == layerName);
                if (string.IsNullOrEmpty(layerName) || matches <= 1) continue;
                messages.Add(ModelMessage.Warning("ad.warn.removed_layer_ambiguous", layerName, matches.ToString(), owner));
                KeepLayer(removal, layerName);
            }
            return messages;
        }

        /// <summary>
        /// Takes back the removal of one layer the build cannot carry out: the layer leaves the plan and every
        /// parameter freed only for it is declared again, the way KeepBlocked does for all of them. It is not
        /// added to BlockedLayers, which only feeds the layer control warning.
        /// </summary>
        static void KeepLayer(RemovalPlan removal, string layerName)
        {
            removal.Layers.Remove(layerName);
            foreach (var parameter in removal.Parameters)
            {
                if (!parameter.Freed || !parameter.Layers.Contains(layerName)) continue;
                parameter.Freed = false;
                parameter.KeptBecause = "ad.removal.kept.layer";
            }
        }

        /// <summary>True when any controller of the build switches FX layers by their position, so leaving one out would move what it points at.</summary>
        internal static bool AnyFxLayerControl(AnimatorServicesContext asc)
            => asc != null && asc.ControllerContext.GetAllControllers().Any(HasFxLayerControl);

        /// <summary>
        /// Takes back a removal the build cannot carry out: the planned layers move to BlockedLayers and every
        /// parameter that was freed for them is declared again. A kept layer reading a name the expression
        /// parameters no longer declare is exactly the silent breakage the planner exists to avoid.
        /// </summary>
        internal static void KeepBlocked(RemovalPlan removal)
        {
            if (removal == null || removal.Layers.Count == 0) return;
            var blocked = new HashSet<string>(removal.Layers);
            foreach (var parameter in removal.Parameters)
            {
                if (!parameter.Freed || !parameter.Layers.Any(blocked.Contains)) continue;
                parameter.Freed = false;
                parameter.KeptBecause = "ad.removal.kept.layer";
            }
            removal.BlockedLayers.AddRange(removal.Layers);
            removal.Layers.Clear();
        }

        static bool HasFxLayerControl(VirtualAnimatorController controller) => FxLayerControls(controller).Any();

        /// <summary>Every VRCAnimatorLayerControl in this controller that fades an FX layer: on its state machines, its states, and a synced layer's own state behaviours.</summary>
        internal static IEnumerable<VRCAnimatorLayerControl> FxLayerControls(VirtualAnimatorController controller)
        {
            foreach (var layer in controller.Layers)
            {
                if (layer.StateMachine != null)
                {
                    foreach (var sm in AllMachines(layer.StateMachine))
                        foreach (var control in FxLayerControls(sm.Behaviours))
                            yield return control;
                    foreach (var state in layer.StateMachine.AllStates())
                        foreach (var control in FxLayerControls(state.Behaviours))
                            yield return control;
                }
                foreach (var behaviours in layer.SyncedLayerBehaviourOverrides.Values)
                    foreach (var control in FxLayerControls(behaviours))
                        yield return control;
            }
        }

        static IEnumerable<VRCAnimatorLayerControl> FxLayerControls(IEnumerable<StateMachineBehaviour> behaviours)
        {
            if (behaviours == null) yield break;
            foreach (var behaviour in behaviours)
                if (behaviour is VRCAnimatorLayerControl layerControl && layerControl.playable == VRC_AnimatorLayerControl.BlendableLayer.FX)
                    yield return layerControl;
        }

        static IEnumerable<VirtualStateMachine> AllMachines(VirtualStateMachine root)
        {
            yield return root;
            foreach (var child in root.StateMachines)
                foreach (var sm in AllMachines(child.StateMachine))
                    yield return sm;
        }
    }
}
#endif
