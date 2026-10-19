#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Keeps only the shape set rule curves that matter. A rule writes every shape it holds while the layers are built
    /// (Record); in the final pass, once Modular Avatar and VRCFury have added their layers, a recorded curve stays when
    /// something else moves its shape, when the rule holds at 0 a shape whose default is not 0, or when it is a shape
    /// VRChat blinks or speaks with that the rule's switch holds. Every other recorded curve goes: nothing moves that
    /// shape, so it already shows the value the rule would hold it at, and leaving it unanimated keeps the controller
    /// small. What moves a shape is any curve of any clip in a playable layer's controller except the recorded rule
    /// curves themselves, a kept rule curve that holds a shape at 0 away from its default, which really moves it, and an
    /// override's target, which the override pass pins in every clip that has it. Only what plays from the avatar's own
    /// controllers counts: a shape something outside them moves, such as an MMD world's dance, is not held any more.
    /// </summary>
    public static class HeldShapePruner
    {
        const float Epsilon = 0.0001f;

        /// <summary>Records every shape a rule wrote into content, once its state is in the layer: the state's motion when child is -1, or that child of the state's blend tree.</summary>
        public static void Record(PinLayerState pins, ClipContent content, MenuEntry entry, string layerName, VirtualStateMachine stateMachine, VirtualState state, int child, BaseState baseState, ShapeSetModel.HeldContext context)
        {
            if (pins == null || content == null || entry == null || state == null) return;
            int occurrence = stateMachine.AllStates().TakeWhile(s => s != state).Count(s => s.Name == state.Name);
            foreach (var (renderer, shape, rule) in content.HeldWrites())
            {
                pins.HeldCurves.Add(new PinLayerState.HeldCurve
                {
                    Entry = entry,
                    Renderer = renderer,
                    Shape = shape,
                    LayerName = layerName,
                    StateName = state.Name,
                    StateOccurrence = occurrence,
                    Child = child,
                    Value = rule.value,
                    DefaultWeight = baseState.WeightOf(renderer, shape),
                    Tracked = context != null && (context.IsEyelid(renderer, shape) || context.IsViseme(renderer, shape)),
                });
            }
        }

        /// <summary>
        /// Runs before ReleasePinsToRest, so a dropped shape is never handed back to rest by its layer: a layer that no
        /// longer writes the shape in any state loses its binding here, and its Reset state, still empty now, never gets
        /// a curve for it. Runs before ClipRewriter too, so a blend shape trigger copies its shape from the clips as they
        /// will play. Says once per rule how many of its shapes it still holds, when that is fewer than all of them.
        /// overrides may be null.
        /// </summary>
        public static List<ModelMessage> Prune(AnimatorServicesContext asc, PinLayerState pins, OverrideState overrides)
        {
            var messages = new List<ModelMessage>();
            if (pins == null || pins.HeldCurves.Count == 0) return messages;
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return messages;

            var byName = new Dictionary<string, VirtualLayer>();
            foreach (var layer in fx.Layers)
                if (layer.Name != null && !byName.ContainsKey(layer.Name)) byName[layer.Name] = layer;

            // A clip another state or tree child also plays (some other tool reused it) is left whole: its curves move
            // the shape from there too.
            var uses = ClipUses(asc);
            var resolved = new List<(PinLayerState.HeldCurve curve, VirtualClip clip, EditorCurveBinding binding)>();
            var ruleCurves = new Dictionary<VirtualClip, HashSet<EditorCurveBinding>>();
            foreach (var curve in pins.HeldCurves)
            {
                if (!TryResolve(asc, byName, curve, out var clip, out var binding)) continue;
                if (!uses.TryGetValue(clip, out int count) || count != 1) continue;
                resolved.Add((curve, clip, binding));
                if (!ruleCurves.TryGetValue(clip, out var own)) ruleCurves[clip] = own = new HashSet<EditorCurveBinding>();
                own.Add(binding);
            }

            var moved = MovedShapes(asc, ruleCurves);
            foreach (var (curve, _, binding) in resolved)
                if (HoldsAwayFromDefault(curve)) moved.Add((binding.path, binding.propertyName));
            // The override pass flattens every curve of its target to the override's value, so a rule curve on that
            // shape is an animation the override really holds it in.
            if (overrides != null && overrides.Ready)
                foreach (var rule in overrides.Rules)
                    if (rule.Renderer != null && !string.IsNullOrEmpty(rule.Shape))
                        moved.Add((asc.ObjectPathRemapper.GetVirtualPathForObject(rule.Renderer.gameObject), "blendShape." + rule.Shape));

            var dropped = new HashSet<PinLayerState.HeldCurve>();
            var droppedBindings = new HashSet<(string layerName, SkinnedMeshRenderer renderer, string shape, EditorCurveBinding binding)>();
            foreach (var (curve, clip, binding) in resolved)
            {
                if (curve.Tracked || HoldsAwayFromDefault(curve) || moved.Contains((binding.path, binding.propertyName))) continue;
                clip.SetFloatCurve(binding, null);
                dropped.Add(curve);
                droppedBindings.Add((curve.LayerName, curve.Renderer, curve.Shape, binding));
            }

            // A layer that no longer writes a shape anywhere has nothing to hand back to rest.
            foreach (var (layerName, renderer, shape, binding) in droppedBindings)
                if (!LayerWrites(byName[layerName], binding))
                    pins.Bindings.RemoveAll(b => b.LayerName == layerName && b.Renderer == renderer && b.Shape == shape);

            foreach (var rule in pins.HeldCurves.GroupBy(c => (c.Entry, c.Renderer)))
            {
                var (entry, renderer) = rule.Key;
                if (entry == null || renderer == null) continue;
                int total = rule.Select(c => c.Shape).Distinct().Count();
                int held = rule.Where(c => !dropped.Contains(c)).Select(c => c.Shape).Distinct().Count();
                if (held >= total) continue;
                messages.Add(ModelMessage.Info(total == 1 ? "ad.info.shape_set_pruned.one" : "ad.info.shape_set_pruned", entry.PlainName, held, total, renderer.name).For(entry.id));
            }
            return messages;
        }

        /// <summary>A Zero rule on a shape whose default is not 0: it changes what the shape shows, so it is kept and it moves the shape for every other rule.</summary>
        static bool HoldsAwayFromDefault(PinLayerState.HeldCurve curve) => curve.Value == ShapeSetValue.Zero && Mathf.Abs(curve.DefaultWeight) > Epsilon;

        /// <summary>Every (path, blendShape property) some curve moves in any clip of the playable layers' controllers, except the recorded rule curves.</summary>
        static HashSet<(string path, string property)> MovedShapes(AnimatorServicesContext asc, Dictionary<VirtualClip, HashSet<EditorCurveBinding>> ruleCurves)
        {
            var moved = new HashSet<(string, string)>();
            var seen = new HashSet<VirtualClip>();
            foreach (VRCAvatarDescriptor.AnimLayerType type in Enum.GetValues(typeof(VRCAvatarDescriptor.AnimLayerType)))
            {
                if (!asc.ControllerContext.Controllers.TryGetValue(type, out var controller) || controller == null) continue;
                foreach (var clip in controller.AllReachableNodes().OfType<VirtualClip>())
                {
                    if (!seen.Add(clip)) continue;
                    ruleCurves.TryGetValue(clip, out var own);
                    foreach (var binding in clip.GetFloatCurveBindings())
                    {
                        if (binding.type != typeof(SkinnedMeshRenderer) || binding.propertyName == null || !binding.propertyName.StartsWith("blendShape.")) continue;
                        if (own != null && own.Contains(binding)) continue;
                        moved.Add((binding.path, binding.propertyName));
                    }
                }
            }
            return moved;
        }

        /// <summary>How many places each clip plays from in the playable layers' controllers: every state, every child of its blend tree at any depth, and every motion a synced layer puts in a state.</summary>
        static Dictionary<VirtualClip, int> ClipUses(AnimatorServicesContext asc)
        {
            var uses = new Dictionary<VirtualClip, int>();
            void Count(VirtualMotion motion)
            {
                if (motion == null) return;
                foreach (var clip in AnimatorBuilder.LeafClips(motion))
                    uses[clip] = uses.TryGetValue(clip, out int count) ? count + 1 : 1;
            }
            var seen = new HashSet<VirtualAnimatorController>();
            foreach (VRCAvatarDescriptor.AnimLayerType type in Enum.GetValues(typeof(VRCAvatarDescriptor.AnimLayerType)))
            {
                if (!asc.ControllerContext.Controllers.TryGetValue(type, out var controller) || controller == null || !seen.Add(controller)) continue;
                foreach (var layer in controller.Layers)
                {
                    if (layer.StateMachine != null)
                        foreach (var state in layer.StateMachine.AllStates()) Count(state.Motion);
                    if (layer.SyncedLayerMotionOverrides != null)
                        foreach (var motion in layer.SyncedLayerMotionOverrides.Values) Count(motion);
                }
            }
            return uses;
        }

        /// <summary>The clip and binding a record names, in the controller as it is now. False when the layer, the state, the child or the curve is gone (another tool rebuilt it): such a curve is left alone.</summary>
        static bool TryResolve(AnimatorServicesContext asc, Dictionary<string, VirtualLayer> byName, PinLayerState.HeldCurve curve, out VirtualClip clip, out EditorCurveBinding binding)
        {
            clip = null;
            binding = default;
            if (curve.Renderer == null || curve.LayerName == null || !byName.TryGetValue(curve.LayerName, out var layer) || layer.StateMachine == null) return false;
            var state = layer.StateMachine.AllStates().Where(s => s.Name == curve.StateName).Skip(curve.StateOccurrence).FirstOrDefault();
            if (state == null) return false;
            var motion = state.Motion;
            if (curve.Child >= 0)
            {
                if (!(motion is VirtualBlendTree tree) || curve.Child >= tree.Children.Count) return false;
                motion = tree.Children[curve.Child].Motion;
            }
            if (!(motion is VirtualClip found)) return false;
            binding = EditorCurveBinding.FloatCurve(asc.ObjectPathRemapper.GetVirtualPathForObject(curve.Renderer.gameObject), typeof(SkinnedMeshRenderer), "blendShape." + curve.Shape);
            if (found.GetFloatCurve(binding) == null) return false;
            clip = found;
            return true;
        }

        static bool LayerWrites(VirtualLayer layer, EditorCurveBinding binding)
        {
            if (layer?.StateMachine == null) return false;
            foreach (var state in layer.StateMachine.AllStates())
                if (state.Motion != null && AnimatorBuilder.LeafClips(state.Motion).Any(c => c.GetFloatCurve(binding) != null)) return true;
            return false;
        }
    }
}
#endif
