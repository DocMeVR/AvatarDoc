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
    /// Connects every blend shape trigger to its shape, in the final pass: each FX clip that moves the shape gets a
    /// curve on the trigger's hidden parameter that copies the shape's own curve key for key, and with Replace the
    /// shape's own curve goes. An animated parameter only reaches layers of the controller its clip plays in, and
    /// the trigger's layer is in FX, so a clip of any other playable layer is left alone and named in a warning.
    /// </summary>
    public static class ClipRewriter
    {
        public static List<ModelMessage> Rewrite(AnimatorServicesContext asc, PinLayerState pins)
        {
            var messages = new List<ModelMessage>();
            if (pins == null || pins.ShapeMirrors.Count == 0) return messages;
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return messages;
            var fxClips = new HashSet<VirtualClip>(fx.AllReachableNodes().OfType<VirtualClip>());

            var removals = new HashSet<(VirtualClip clip, EditorCurveBinding binding)>();
            var keptCandidates = new List<(VirtualClip clip, EditorCurveBinding binding, MenuEntry entry)>();
            foreach (var job in pins.ShapeMirrors)
            {
                var entry = job.Entry;
                if (job.Renderer == null || entry == null) continue;
                var sourceBinding = EditorCurveBinding.FloatCurve(asc.ObjectPathRemapper.GetVirtualPathForObject(job.Renderer.gameObject), typeof(SkinnedMeshRenderer), "blendShape." + job.Shape);
                var effects = EffectBindings(asc, entry);
                var outside = new List<string>();
                int copied = 0;
                foreach (var clip in asc.AnimationIndex.GetClipsForBinding(sourceBinding).ToList())
                {
                    if (!fxClips.Contains(clip))
                    {
                        if (!outside.Contains(clip.Name)) outside.Add(clip.Name);
                        continue;
                    }
                    if (clip.IsMarkerClip)
                    {
                        messages.Add(ModelMessage.Warning("ad.warn.marker_clip_skipped", clip.Name, entry.PlainName));
                        continue;
                    }
                    var source = clip.GetFloatCurve(sourceBinding);
                    if (source == null || source.length == 0) continue;
                    if (job.Parameter == null)
                    {
                        keptCandidates.Add((clip, sourceBinding, entry));
                        continue;
                    }
                    clip.SetFloatCurve(EditorCurveBinding.FloatCurve("", typeof(Animator), job.Parameter), new AnimationCurve(source.keys) { preWrapMode = source.preWrapMode, postWrapMode = source.postWrapMode });
                    copied++;
                    if (effects.Any(b => clip.GetFloatCurve(b) != null || clip.GetObjectCurve(b) != null))
                        messages.Add(ModelMessage.Info("ad.info.curve_overwritten", clip.Name, entry.PlainName).For(entry.id));
                    if (job.Replace) removals.Add((clip, sourceBinding));
                }
                if (outside.Count > 0)
                    messages.Add(ModelMessage.Warning("ad.warn.link_clips_outside_fx", entry.PlainName, job.Renderer.name + "/" + job.Shape, L.JoinList(outside)).For(entry.id));
                if (job.Parameter == null) continue;
                if (copied == 0)
                    messages.Add(ModelMessage.Warning("ad.warn.link_no_fx_clip", entry.PlainName, job.Renderer.name + "/" + job.Shape).For(entry.id));
                // Set last, once Shapes on the outfit pieces and every other tool have run: where a Write Defaults
                // on layer or a partly weighted blend tree falls back to defaults, the copy falls back to the same
                // value the shape does.
                int shapeIndex = job.Renderer.sharedMesh != null ? job.Renderer.sharedMesh.GetBlendShapeIndex(job.Shape) : -1;
                if (shapeIndex >= 0 && fx.Parameters.TryGetValue(job.Parameter, out var declared))
                {
                    declared.defaultFloat = job.Renderer.GetBlendShapeWeight(shapeIndex);
                    fx.SetParameter(job.Parameter, declared);
                }
            }
            // Removed only once every trigger has copied the shape, so two triggers on one shape both see it.
            foreach (var removal in removals) removal.clip.SetFloatCurve(removal.binding, null);

            // A Replace trigger with nothing to build keeps the shape's curve, unless another Replace trigger on the
            // same shape took it out anyway. Reported once per trigger and clip.
            var warned = new HashSet<(VirtualClip clip, MenuEntry entry)>();
            foreach (var candidate in keptCandidates)
            {
                if (removals.Contains((candidate.clip, candidate.binding))) continue;
                if (!warned.Add((candidate.clip, candidate.entry))) continue;
                messages.Add(ModelMessage.Warning("ad.warn.replace_kept_source", candidate.entry.PlainName, candidate.clip.Name).For(candidate.entry.id));
            }
            return messages;
        }

        /// <summary>Every curve the trigger's own layer writes, so a clip that also moves one of them can be named: while the trigger is on, its layer, last in FX, wins there.</summary>
        static List<EditorCurveBinding> EffectBindings(AnimatorServicesContext asc, MenuEntry entry)
        {
            var bindings = new List<EditorCurveBinding>();
            string PathOf(GameObject go) => asc.ObjectPathRemapper.GetVirtualPathForObject(go);
            foreach (var target in entry.blendShapes)
                if (MenuModel.ShapeExists(target.renderer, target.shape) && !(target.renderer == entry.sourceRenderer && target.shape == entry.sourceShape))
                    bindings.Add(EditorCurveBinding.FloatCurve(PathOf(target.renderer.gameObject), typeof(SkinnedMeshRenderer), "blendShape." + target.shape));
            foreach (var target in entry.objects)
                if (target.target != null) bindings.Add(EditorCurveBinding.FloatCurve(PathOf(target.target), typeof(GameObject), "m_IsActive"));
            foreach (var target in entry.materials)
                if (MenuModel.SlotExists(target)) bindings.Add(EditorCurveBinding.PPtrCurve(PathOf(target.renderer.gameObject), target.renderer.GetType(), "m_Materials.Array.data[" + target.slot + "]"));
            foreach (var target in entry.properties)
                if (target.renderer != null && !string.IsNullOrEmpty(target.property))
                    bindings.Add(EditorCurveBinding.FloatCurve(PathOf(target.renderer.gameObject), target.renderer.GetType(), "material." + target.property));
            return bindings;
        }
    }
}
#endif
