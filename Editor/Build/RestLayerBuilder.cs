#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The rest layer, and the default look in the uploaded avatar. Each shape of PinLayerState.UploadRests goes into
    /// the avatar at its look, here at the end of the build, after Modular Avatar and VRCFury have read the scene, and
    /// the rest layer gives the animator its rest. One Write Defaults on state at weight 1 writes each rest on every frame,
    /// the way Modular Avatar rests what its reactive components change, so a gesture, face tracking or a pin layer
    /// later in FX decides the shape while it plays and lets it go back to that rest, not to the uploaded value, when
    /// it stops. Write Defaults on, because a Write Defaults off layer that weighs more than 0 holds every shape a
    /// world's dance moves; a dance still cannot move the shapes this layer writes.
    /// The layer goes right after the first three FX layers, which MMD worlds switch off and where Modular Avatar's MMD
    /// relay and VRCFury's MMD Compatibility keep their own layers, so nothing in those slots moves. A shape some layer
    /// in those slots moves stays out of it and keeps its rest in the avatar, since the rest layer would sit after that
    /// layer and hide it. Pin layers do not count: they move to the end (MovePinLayersLast).
    /// The source shape of a Replace blend shape trigger gets its rest curve, so the trigger's hidden copy reads the
    /// rest, but keeps its rest in the avatar too: the trigger takes every curve of the shape out of FX (ClipRewriter),
    /// and the shape shows the uploaded value from then on. A shape another tool copies from a held one (Modular
    /// Avatar's Blendshape Sync, VRCFury's Blendshape Link, AvatarDoc Shapes' Follow rows) gets its look and its rest the
    /// same way as its source, read from the pin layer clips it was copied into (Copies).
    /// Runs in the final pass after the release to rest, so no ownership guess counts this layer as an owner.
    /// </summary>
    public static class RestLayerBuilder
    {
        /// <summary>How many FX layers at the start keep their places.</summary>
        public const int KeptSlots = 3;

        const float Tolerance = 0.001f;

        public static void Build(AnimatorServicesContext asc, PinLayerState pins)
        {
            if (pins == null || pins.UploadRests.Count == 0) return;
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return;

            var layers = fx.Layers.ToList();
            var ordinary = layers.Where(l => !AnimatorBuilder.IsKnownPinLayer(l, pins.Names)).ToList();
            int slot = Mathf.Min(KeptSlots, ordinary.Count);
            var movedInFront = new HashSet<EditorCurveBinding>(ordinary.Take(slot).SelectMany(FloatBindings));
            var replaced = new HashSet<EditorCurveBinding>(pins.ShapeMirrors
                .Where(m => m.Replace && m.Parameter != null && m.Renderer != null)
                .Select(m => ShapeBinding(asc, m.Renderer, m.Shape)));
            var pinClips = PinStateClips(layers, pins);
            var ours = OwnBindings(asc, pins);

            var clip = VirtualClip.Create(PinLayerState.RestLayerName);
            var copied = new HashSet<EditorCurveBinding>();
            void Rest(EditorCurveBinding binding, SkinnedMeshRenderer renderer, int index, float rest, float look, bool showLook)
            {
                clip.SetFloatCurve(binding, AnimationCurve.Constant(0f, 1f / 60f, rest));
                pins.RestLayerValues[binding] = rest;
                if (showLook) renderer.SetBlendShapeWeight(index, look);
            }
            foreach (var rest in pins.UploadRests)
            {
                if (rest.Renderer == null) continue;
                var binding = ShapeBinding(asc, rest.Renderer, rest.Shape);
                if (movedInFront.Contains(binding)) continue;
                int index = rest.Renderer.sharedMesh != null ? rest.Renderer.sharedMesh.GetBlendShapeIndex(rest.Shape) : -1;
                if (index < 0) continue;
                bool replace = replaced.Contains(binding);
                Rest(binding, rest.Renderer, index, rest.Rest, rest.Look, !replace);
                if (replace) continue;
                foreach (var copy in Copies(asc, binding, rest.Look, pinClips, ours))
                {
                    if (movedInFront.Contains(copy.binding) || replaced.Contains(copy.binding) || !copied.Add(copy.binding)) continue;
                    Rest(copy.binding, copy.renderer, copy.index, copy.rest, copy.look, true);
                }
            }
            if (!clip.GetFloatCurveBindings().Any()) return;

            var layer = fx.AddLayer(LayerPriority.Default, AnimatorBuilder.UniqueLayerName(fx, PinLayerState.RestLayerName));
            layer.DefaultWeight = 1f;
            var state = layer.StateMachine.AddState("Rest");
            state.Motion = clip;
            state.WriteDefaultValues = true;
            layer.StateMachine.DefaultState = state;

            var order = new List<VirtualLayer>();
            if (slot == 0) order.Add(layer);
            int seen = 0;
            foreach (var existing in layers)
            {
                order.Add(existing);
                if (slot > 0 && !AnimatorBuilder.IsKnownPinLayer(existing, pins.Names) && ++seen == slot) order.Add(layer);
            }
            fx.Layers = order;
        }

        static EditorCurveBinding ShapeBinding(AnimatorServicesContext asc, SkinnedMeshRenderer renderer, string shape)
            => EditorCurveBinding.FloatCurve(asc.ObjectPathRemapper.GetVirtualPathForObject(renderer.gameObject), typeof(SkinnedMeshRenderer), "blendShape." + shape);

        /// <summary>
        /// The shapes another tool copied from source into AvatarDoc's own pin layer clips, each with the look and rest
        /// it should get: a blend shape curve in every clip of a pin state that moves source, none of AvatarDoc's own
        /// (ours), whose value is the same one wherever source has the same value. Its look is its value where source
        /// is at sourceLook; its rest is the weight the tool gave it in the scene, which it copied from the source's rest.
        /// A copy with no clip at the source's look is left as it is.
        /// </summary>
        static IEnumerable<(EditorCurveBinding binding, SkinnedMeshRenderer renderer, int index, float rest, float look)> Copies(
            AnimatorServicesContext asc, EditorCurveBinding source, float sourceLook, List<VirtualClip> pinClips, HashSet<EditorCurveBinding> ours)
        {
            var clips = pinClips.Where(c => c.GetFloatCurve(source) != null).ToList();
            if (clips.Count == 0) yield break;
            var candidates = clips
                .Select(c => new HashSet<EditorCurveBinding>(c.GetFloatCurveBindings().Where(b => b.propertyName.StartsWith("blendShape.") && b != source && !ours.Contains(b))))
                .Aggregate((a, b) => { a.IntersectWith(b); return a; });
            foreach (var binding in candidates)
            {
                bool consistent = true;
                float? look = null;
                var seen = new List<(float source, float copy)>();
                foreach (var c in clips)
                {
                    float s = c.GetFloatCurve(source).Evaluate(0f), v = c.GetFloatCurve(binding).Evaluate(0f);
                    if (seen.Any(p => Mathf.Abs(p.source - s) <= Tolerance && Mathf.Abs(p.copy - v) > Tolerance)) { consistent = false; break; }
                    seen.Add((s, v));
                    if (Mathf.Abs(s - sourceLook) <= Tolerance) look = v;
                }
                if (!consistent || look == null) continue;
                var target = asc.ObjectPathRemapper.GetObjectForPath(binding.path);
                var renderer = target != null ? target.GetComponent<SkinnedMeshRenderer>() : null;
                if (renderer == null || renderer.sharedMesh == null) continue;
                int index = renderer.sharedMesh.GetBlendShapeIndex(binding.propertyName.Substring("blendShape.".Length));
                if (index < 0) continue;
                float rest = renderer.GetBlendShapeWeight(index);
                if (Mathf.Abs(rest - look.Value) <= Tolerance) continue;
                yield return (binding, renderer, index, rest, look.Value);
            }
        }

        /// <summary>Every leaf clip of AvatarDoc's own pin layers, but the Reset and Load states', which the final pass filled after every other tool ran.</summary>
        static List<VirtualClip> PinStateClips(List<VirtualLayer> layers, PinLayerState pins)
        {
            var result = new List<VirtualClip>();
            foreach (var layer in layers)
            {
                if (layer.StateMachine == null || !AnimatorBuilder.IsKnownPinLayer(layer, pins.Names)) continue;
                foreach (var state in layer.StateMachine.AllStates())
                {
                    if (state.Motion == null || state.Name == PinLayerState.ResetStateName || state.Name == PinLayerState.LoadStateName) continue;
                    result.AddRange(AnimatorBuilder.LeafClips(state.Motion));
                }
            }
            return result;
        }

        /// <summary>The shapes AvatarDoc itself writes from a pin layer: its pinned rows, its rules' held shapes and the shapes it found away from their rest.</summary>
        static HashSet<EditorCurveBinding> OwnBindings(AnimatorServicesContext asc, PinLayerState pins)
        {
            var result = new HashSet<EditorCurveBinding>();
            foreach (var binding in pins.Bindings)
                if (binding.Renderer != null) result.Add(ShapeBinding(asc, binding.Renderer, binding.Shape));
            foreach (var curve in pins.HeldCurves)
                if (curve.Renderer != null) result.Add(ShapeBinding(asc, curve.Renderer, curve.Shape));
            foreach (var rest in pins.UploadRests)
                if (rest.Renderer != null) result.Add(ShapeBinding(asc, rest.Renderer, rest.Shape));
            return result;
        }

        static IEnumerable<EditorCurveBinding> FloatBindings(VirtualLayer layer)
        {
            var motions = new List<VirtualMotion>();
            if (layer.StateMachine != null)
                foreach (var state in layer.StateMachine.AllStates())
                    if (state.Motion != null) motions.Add(state.Motion);
            if (layer.SyncedLayerMotionOverrides != null)
                foreach (var motion in layer.SyncedLayerMotionOverrides.Values)
                    if (motion != null) motions.Add(motion);
            foreach (var motion in motions)
                foreach (var clip in AnimatorBuilder.LeafClips(motion))
                    foreach (var binding in clip.GetFloatCurveBindings())
                        yield return binding;
        }
    }
}
#endif
