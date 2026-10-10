#if DOCME_VRCSDK3A
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Gathers an entry's custom animation clips into rewritten curve bindings, ready to merge with the generated values.</summary>
    public static class CustomClips
    {
        /// <summary>Every binding of the entry's clips with its path rewritten for pathRoot. A float binding carries curve; an object reference binding carries keys. Proxy clips are refused (ad.warn.animation_proxy) and skipped; a target with no clip, or a pathRoot outside the avatar, is skipped without a message (the model already reports both as ad.warn.target_missing / ad.warn.target_outside_avatar).</summary>
        public static List<(EditorCurveBinding binding, AnimationCurve curve, ObjectReferenceKeyframe[] keys)> Collect(MenuEntry entry, Transform avatarRoot, List<ModelMessage> messages)
        {
            var result = new List<(EditorCurveBinding, AnimationCurve, ObjectReferenceKeyframe[])>();
            foreach (var target in entry.animations)
            {
                var clip = target.clip;
                if (clip == null) continue;
                if (MenuModel.IsOutsideAvatar(avatarRoot, target.pathRoot)) continue;
                if (IsProxy(clip))
                {
                    messages.Add(ModelMessage.Warning("ad.warn.animation_proxy", entry.PlainName, clip.name).For(entry.id));
                    continue;
                }
                string prefix = PrefixFor(avatarRoot, target.pathRoot);
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    var rewritten = binding;
                    rewritten.path = CombinePath(prefix, binding.path);
                    result.Add((rewritten, AnimationUtility.GetEditorCurve(clip, binding), null));
                }
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    var rewritten = binding;
                    rewritten.path = CombinePath(prefix, binding.path);
                    result.Add((rewritten, null, AnimationUtility.GetObjectReferenceCurve(clip, binding)));
                }
            }
            return result;
        }

        /// <summary>VRChat proxy animations must be preserved as-is and never merged into another clip.</summary>
        public static bool IsProxy(AnimationClip clip) => clip != null && clip.name.StartsWith("proxy_");

        /// <summary>The longest usable custom clip of the entry, or one second when every one of them is shorter than a frame.</summary>
        public static float Length(MenuEntry entry)
        {
            float length = 0f;
            foreach (var target in entry.animations)
            {
                if (target.clip == null || IsProxy(target.clip)) continue;
                length = Mathf.Max(length, target.clip.length);
            }
            return length >= 1f / 60f ? length : 1f;
        }

        /// <summary>The path from the avatar root to pathRoot, or empty when pathRoot is null or the avatar root itself.</summary>
        public static string PrefixFor(Transform avatarRoot, Transform pathRoot)
        {
            if (pathRoot == null || pathRoot == avatarRoot) return "";
            return AnimationUtility.CalculateTransformPath(pathRoot, avatarRoot);
        }

        static string CombinePath(string prefix, string path)
        {
            if (string.IsNullOrEmpty(prefix)) return path;
            return string.IsNullOrEmpty(path) ? prefix : prefix + "/" + path;
        }
    }
}
#endif
