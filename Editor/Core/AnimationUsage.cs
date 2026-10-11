#if DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Which clips in the descriptor's controllers animate which blend shape. Read from assets only, so it cannot see animators other tools merge at build time.</summary>
    public sealed class AnimationUsage
    {
        static readonly Dictionary<int, AnimationUsage> memo = new Dictionary<int, AnimationUsage>();
        static readonly IReadOnlyList<AnimationClip> none = new AnimationClip[0];

        readonly Dictionary<(string path, string shape), List<AnimationClip>> clips = new Dictionary<(string, string), List<AnimationClip>>();
        /// <summary>The same index for the clips that play only in a controller other than FX: an animated animator parameter in them never reaches an FX layer.</summary>
        readonly Dictionary<(string path, string shape), List<AnimationClip>> outsideFx = new Dictionary<(string, string), List<AnimationClip>>();
        readonly Transform avatarRoot;
        readonly string controllerKey;
        readonly List<FxSwitch> fxSwitches = new List<FxSwitch>();

        /// <summary>One constant on or off curve of an object in a clip of the avatar's own FX controller.</summary>
        public readonly struct FxSwitch
        {
            /// <summary>The object's path from the avatar root.</summary>
            public readonly string Path;
            /// <summary>The clip's layer, by its place in the FX controller.</summary>
            public readonly int Layer;
            public readonly string LayerName;
            public readonly AnimationClip Clip;
            public readonly bool On;

            public FxSwitch(string path, int layer, string layerName, AnimationClip clip, bool on)
            {
                Path = path;
                Layer = layer;
                LayerName = layerName;
                Clip = clip;
                On = on;
            }
        }

        /// <summary>Every clip of the avatar's own FX controller that turns an object on or off with a constant curve, the way the build finds the clips that switch an outfit piece.</summary>
        public IReadOnlyList<FxSwitch> FxSwitches => fxSwitches;

        /// <summary>How many layers the avatar's own FX controller has. The build adds AvatarDoc's layers after them.</summary>
        public int FxLayerCount { get; private set; }

        AnimationUsage(Transform avatarRoot, string controllerKey)
        {
            this.avatarRoot = avatarRoot;
            this.controllerKey = controllerKey;
        }

        public static AnimationUsage Empty => new AnimationUsage(null, "");

        /// <summary>The memoized index for a descriptor; rebuilt when its controller set changes or after an animation asset import.</summary>
        public static AnimationUsage ForDescriptor(VRCAvatarDescriptor descriptor, Transform avatarRoot)
        {
            if (descriptor == null || avatarRoot == null) return Empty;
            string key = ControllerKey(descriptor);
            int id = descriptor.GetInstanceID();
            if (memo.TryGetValue(id, out var cached) && cached.controllerKey == key && cached.avatarRoot == avatarRoot) return cached;
            var usage = Scan(descriptor, avatarRoot);
            memo[id] = usage;
            return usage;
        }

        public static void Invalidate() => memo.Clear();

        public static AnimationUsage Scan(VRCAvatarDescriptor descriptor, Transform avatarRoot)
        {
            var usage = new AnimationUsage(avatarRoot, descriptor != null ? ControllerKey(descriptor) : "");
            if (descriptor == null || avatarRoot == null) return usage;
            var seen = new HashSet<AnimationClip>();
            var fxClips = new HashSet<AnimationClip>(Layers(descriptor).Where(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX)
                .SelectMany(l => l.controller.animationClips).Where(c => c != null));
            foreach (var controller in Controllers(descriptor))
            {
                foreach (var clip in controller.animationClips)
                {
                    if (clip == null || !seen.Add(clip)) continue;
                    bool outside = !fxClips.Contains(clip);
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.type != typeof(SkinnedMeshRenderer) || !binding.propertyName.StartsWith("blendShape.")) continue;
                        var key = (binding.path ?? "", binding.propertyName.Substring("blendShape.".Length));
                        if (!usage.clips.TryGetValue(key, out var list)) usage.clips[key] = list = new List<AnimationClip>();
                        list.Add(clip);
                        if (!outside) continue;
                        if (!usage.outsideFx.TryGetValue(key, out var outsideList)) usage.outsideFx[key] = outsideList = new List<AnimationClip>();
                        outsideList.Add(clip);
                    }
                }
            }
            var fx = Layers(descriptor).Where(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).Select(l => l.controller).OfType<AnimatorController>().FirstOrDefault();
            if (fx != null)
            {
                usage.FxLayerCount = fx.layers.Length;
                usage.fxSwitches.AddRange(SwitchesIn(fx));
            }
            return usage;
        }

        /// <summary>Every clip of a controller that turns an object on or off with a constant curve, with the clip's layer by its place in that controller.</summary>
        public static List<FxSwitch> SwitchesIn(AnimatorController controller)
        {
            var found = new List<FxSwitch>();
            if (controller == null) return found;
            var layers = controller.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                var layerClips = new HashSet<AnimationClip>();
                CollectClips(layers[i].stateMachine, layerClips);
                foreach (var clip in layerClips)
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.type != typeof(GameObject) || binding.propertyName != "m_IsActive") continue;
                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        if (curve == null || curve.length == 0 || curve.keys.Any(k => !Mathf.Approximately(k.value, curve.keys[0].value))) continue;
                        found.Add(new FxSwitch(binding.path ?? "", i, layers[i].name, clip, curve.keys[0].value >= 0.5f));
                    }
            }
            return found;
        }

        static void CollectClips(AnimatorStateMachine machine, HashSet<AnimationClip> found)
        {
            if (machine == null) return;
            foreach (var child in machine.states) CollectMotion(child.state != null ? child.state.motion : null, found);
            foreach (var sub in machine.stateMachines) CollectClips(sub.stateMachine, found);
        }

        static void CollectMotion(Motion motion, HashSet<AnimationClip> found)
        {
            if (motion is AnimationClip clip) found.Add(clip);
            else if (motion is BlendTree tree)
                foreach (var child in tree.children) CollectMotion(child.motion, found);
        }

        public IReadOnlyList<AnimationClip> ClipsFor(SkinnedMeshRenderer renderer, string shape) => Lookup(clips, renderer, shape);

        /// <summary>The clips that move this shape and play only in a controller other than FX (Base, Additive, Gesture, Action, or a special layer), where a blend shape trigger cannot follow it.</summary>
        public IReadOnlyList<AnimationClip> ClipsOutsideFx(SkinnedMeshRenderer renderer, string shape) => Lookup(outsideFx, renderer, shape);

        IReadOnlyList<AnimationClip> Lookup(Dictionary<(string path, string shape), List<AnimationClip>> index, SkinnedMeshRenderer renderer, string shape)
        {
            if (renderer == null || avatarRoot == null || string.IsNullOrEmpty(shape)) return none;
            string path = AnimationUtility.CalculateTransformPath(renderer.transform, avatarRoot);
            return index.TryGetValue((path, shape), out var list) ? list : none;
        }

        /// <summary>The descriptor's own playable layer controllers. None while the layers are not customized: VRChat animates the avatar with its defaults then.</summary>
        static IEnumerable<RuntimeAnimatorController> Controllers(VRCAvatarDescriptor descriptor) => Layers(descriptor).Select(l => l.controller);

        static IEnumerable<(RuntimeAnimatorController controller, VRCAvatarDescriptor.AnimLayerType type)> Layers(VRCAvatarDescriptor descriptor)
        {
            if (!descriptor.customizeAnimationLayers) yield break;
            foreach (var layers in new[] { descriptor.baseAnimationLayers, descriptor.specialAnimationLayers })
            {
                if (layers == null) continue;
                foreach (var layer in layers)
                    if (!layer.isDefault && layer.animatorController != null) yield return (layer.animatorController, layer.type);
            }
        }

        static string ControllerKey(VRCAvatarDescriptor descriptor)
            => string.Join(",", Controllers(descriptor).Select(c => c.GetInstanceID() + ":" + c.animationClips.Length));
    }

    /// <summary>
    /// Clears the usage memo, and the plans that hold one, when an animation asset changes on disk, and the plans
    /// when an expressions menu or parameters asset does: a file put back by a version control tool is an import,
    /// which no ObjectChangeEvents change reports.
    /// </summary>
    sealed class AnimationUsagePostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var animation = imported.Concat(deleted).Concat(moved).FirstOrDefault(p => p.EndsWith(".anim") || p.EndsWith(".controller") || p.EndsWith(".overrideController"));
            if (animation != null)
            {
                AnimationUsage.Invalidate();
                // A layer or a parameter added in the Animator window has to show in the merge's panel right away.
                MergeBuilder.DropCaches();
#if DOCME_NDMF
                AvatarPlanCache.InvalidateImported(animation, controllers: true);
#endif
                return;
            }
#if DOCME_NDMF
            var expressions = imported.FirstOrDefault(IsExpressionsAsset);
            if (expressions != null) AvatarPlanCache.InvalidateImported(expressions, controllers: false);
#endif
        }

        static bool IsExpressionsAsset(string path)
        {
            if (!path.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase)) return false;
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            return type != null && (typeof(VRCExpressionsMenu).IsAssignableFrom(type) || typeof(VRCExpressionParameters).IsAssignableFrom(type));
        }
    }
}
#endif
