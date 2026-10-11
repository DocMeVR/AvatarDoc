#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>The pass after Modular Avatar: every clip that animates an overridden shape gets a constant curve at the pinned value.</summary>
    public static class OverrideBuilder
    {
        public static void RunAfterModularAvatar(BuildContext context) => Run(context, final: false);

        public static void RunFinal(BuildContext context) => Run(context, final: true);

        /// <summary>
        /// Pins every rule against the clips that exist right now. A rule is pinned in both runs, since VRCFury's
        /// own clips only exist in the second one, so whether it found anything is only known at the end: the
        /// "applied" and "unused" lines are held back until the final run and speak for both runs together. When
        /// the final run never happens (the phase was skipped) those two lines are simply not said.
        /// </summary>
        public static void Run(BuildContext context, bool final)
        {
            var state = context.GetState<OverrideState>();
            if (!state.Ready || state.Rules.Count == 0) return;
            var asc = context.Extension<AnimatorServicesContext>();
            var reported = new List<ModelMessage>();
            foreach (var rule in state.Rules)
            {
                int applied = 0;
                bool gone = false;
                foreach (var message in Pin(asc, new List<OverrideRule> { rule }))
                {
                    if (message.Says("ad.info.override_applied"))
                    {
                        applied += int.Parse((string)message.Args[2]);
                        continue;
                    }
                    if (message.Key == "ad.warn.override_unused") continue;
                    if (message.Key == "ad.warn.override_target_gone")
                    {
                        gone = true;
                        continue;
                    }
                    if (message.Key == "ad.warn.marker_clip_skipped") state.Markers.Add(rule);
                    if (message.Severity == MessageSeverity.Warning && !state.Reported.Add(rule.EntryId + "|" + message.Key + "|" + rule.Shape + "|" + string.Join("|", (message.Args ?? new object[0]).Where(x => x is string))))
                        continue;
                    reported.Add(message);
                }
                // The final run pins every clip that carries the binding, the earlier run's included, so the last
                // run that pinned anything holds the whole count; nothing is added up across the runs.
                if (applied > 0) state.Pinned[rule] = applied;

                // A renderer that is gone now but was pinned in an earlier run was there when it mattered.
                if (gone && state.PinnedCount(rule) == 0 && state.Reported.Add(rule.EntryId + "|gone|" + rule.Shape))
                    reported.Add(ModelMessage.Warning("ad.warn.override_target_gone", rule.EntryName, rule.Shape).For(rule.EntryId));
                if (!final) continue;

                int total = state.PinnedCount(rule);
                if (total > 0)
                    reported.Add(ModelMessage.Info(UiText.CountKey(total, "ad.info.override_applied"), rule.EntryName, rule.Shape, total.ToString(), Context(rule)).For(rule.EntryId));
                else if (!gone && !state.Markers.Contains(rule))
                    reported.Add(ModelMessage.Warning("ad.warn.override_unused", rule.EntryName, rule.Shape, Context(rule)).For(rule.EntryId));
            }
            L.Report(reported);
        }

        /// <summary>What a message names the rule's renderer by. Never the renderer itself once it is gone: a destroyed object has no reference to report.</summary>
        static object Context(OverrideRule rule) => rule.Renderer != null ? (object)rule.Renderer : rule.EntryName;

        public static List<ModelMessage> Pin(AnimatorServicesContext asc, IReadOnlyList<OverrideRule> rules)
        {
            var messages = new List<ModelMessage>();
            foreach (var rule in rules)
            {
                if (rule.Renderer == null)
                {
                    messages.Add(ModelMessage.Warning("ad.warn.override_target_gone", rule.EntryName, rule.Shape).For(rule.EntryId));
                    continue;
                }
                var path = asc.ObjectPathRemapper.GetVirtualPathForObject(rule.Renderer.gameObject);
                var binding = EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + rule.Shape);
                int count = 0;
                int markers = 0;
                foreach (var clip in asc.AnimationIndex.GetClipsForBinding(binding).ToList())
                {
                    if (clip.IsMarkerClip)
                    {
                        markers++;
                        messages.Add(ModelMessage.Warning("ad.warn.marker_clip_skipped", clip.Name, rule.EntryName).For(rule.EntryId));
                        continue;
                    }
                    var existing = clip.GetFloatCurve(binding);
                    if (existing == null) continue;
                    clip.SetFloatCurve(binding, Constant(existing, rule.Value));
                    count++;
                }
                if (count > 0)
                    messages.Add(ModelMessage.Info(UiText.CountKey(count, "ad.info.override_applied"), rule.EntryName, rule.Shape, count.ToString(), rule.Renderer).For(rule.EntryId));
                else if (markers == 0)
                    messages.Add(ModelMessage.Warning("ad.warn.override_unused", rule.EntryName, rule.Shape, rule.Renderer).For(rule.EntryId));
            }
            return messages;
        }

        /// <summary>Two keys at the pinned value spanning the curve it replaces, so the clip keeps the length exit times and blend durations are measured against.</summary>
        static AnimationCurve Constant(AnimationCurve replaced, float value)
        {
            float last = replaced.length > 0 ? replaced.keys[replaced.length - 1].time : 0f;
            var curve = new AnimationCurve(
                new Keyframe(0f, value) { inTangent = 0f, outTangent = 0f },
                new Keyframe(Mathf.Max(last, 1f / 60f), value) { inTangent = 0f, outTangent = 0f });
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
            }
            return curve;
        }
    }
}
#endif
