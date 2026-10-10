#if DOCME_VRCSDK3A
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Scene values of every target before AvatarDoctor changes anything. These are the Off values.</summary>
    public sealed class BaseState
    {
        readonly Dictionary<GameObject, bool> active = new Dictionary<GameObject, bool>();
        readonly Dictionary<(SkinnedMeshRenderer, string), float> weights = new Dictionary<(SkinnedMeshRenderer, string), float>();
        readonly Dictionary<(Renderer, int), Material> materials = new Dictionary<(Renderer, int), Material>();
        readonly Dictionary<(Renderer, string), Vector4> properties = new Dictionary<(Renderer, string), Vector4>();
        readonly Dictionary<EditorCurveBinding, float> customFloats = new Dictionary<EditorCurveBinding, float>();
        readonly Dictionary<EditorCurveBinding, Object> customObjects = new Dictionary<EditorCurveBinding, Object>();

        public static BaseState Capture(MenuTree tree)
        {
            var state = new BaseState();
            // An entry left out on this platform writes nothing here, so its targets are not part of the Off state.
            foreach (var control in tree.Controls)
                if (!control.Excluded) state.CaptureEntry(control.Entry, tree.AvatarRoot);
            foreach (var trigger in tree.Triggers)
                if (!trigger.Excluded) state.CaptureEntry(trigger.Entry, tree.AvatarRoot);
            return state;
        }

        void CaptureEntry(MenuEntry entry, Transform avatarRoot)
        {
            foreach (var target in entry.objects)
            {
                if (target.target == null || MenuModel.IsOutsideAvatar(avatarRoot, target.target.transform)) continue;
                if (!active.ContainsKey(target.target)) active[target.target] = target.target.activeSelf;
            }
            foreach (var target in entry.blendShapes)
            {
                if (!MenuModel.ShapeExists(target.renderer, target.shape)) continue;
                if (MenuModel.IsOutsideAvatar(avatarRoot, target.renderer.transform)) continue;
                var key = (target.renderer, target.shape);
                if (!weights.ContainsKey(key)) weights[key] = target.renderer.GetBlendShapeWeight(target.renderer.sharedMesh.GetBlendShapeIndex(target.shape));
            }
            // The shapes a "zero others" rule forces to 0 are pinned too, and their pin hands them back to this
            // weight. Read here, before DefaultsApplier writes a default on toggle's zeros into the scene.
            foreach (var rule in ShapeSetModel.EffectiveRules(entry))
            {
                if (MenuModel.IsOutsideAvatar(avatarRoot, rule.mesh.transform)) continue;
                foreach (var shape in ShapeSetModel.ZeroedShapes(entry, rule))
                {
                    var key = (rule.mesh, shape);
                    if (!weights.ContainsKey(key)) weights[key] = rule.mesh.GetBlendShapeWeight(rule.mesh.sharedMesh.GetBlendShapeIndex(shape));
                }
            }
            foreach (var target in entry.materials)
            {
                if (!MenuModel.SlotExists(target) || MenuModel.IsOutsideAvatar(avatarRoot, target.renderer.transform)) continue;
                var key = (target.renderer, target.slot);
                if (!materials.ContainsKey(key)) materials[key] = target.renderer.sharedMaterials[target.slot];
            }
            foreach (var target in entry.properties)
            {
                if (!MenuModel.PropertyExists(target, entry) || MenuModel.IsOutsideAvatar(avatarRoot, target.renderer.transform)) continue;
                var key = (target.renderer, target.property);
                if (!properties.ContainsKey(key)) properties[key] = ReadProperty(target.renderer, target.property, target.type, entry);
            }
            if (entry.animations.Count > 0 && avatarRoot != null)
                foreach (var (binding, curve, keys) in CustomClips.Collect(entry, avatarRoot, new List<ModelMessage>()))
                {
                    if (customFloats.ContainsKey(binding) || customObjects.ContainsKey(binding)) continue;
                    if (curve != null)
                    {
                        if (AnimationUtility.GetFloatValue(avatarRoot.gameObject, binding, out float value)) customFloats[binding] = value;
                    }
                    else
                    {
                        if (AnimationUtility.GetObjectReferenceValue(avatarRoot.gameObject, binding, out var value)) customObjects[binding] = value;
                    }
                }
        }

        public bool ActiveOf(GameObject go) => active.TryGetValue(go, out var value) ? value : go.activeSelf;

        public float WeightOf(SkinnedMeshRenderer renderer, string shape)
        {
            if (weights.TryGetValue((renderer, shape), out var value)) return value;
            int index = renderer.sharedMesh.GetBlendShapeIndex(shape);
            return index >= 0 ? renderer.GetBlendShapeWeight(index) : 0f;
        }

        public Material MaterialOf(Renderer renderer, int slot)
        {
            if (materials.TryGetValue((renderer, slot), out var value)) return value;
            var shared = renderer.sharedMaterials;
            return slot >= 0 && slot < shared.Length ? shared[slot] : null;
        }

        /// <summary>The scene value of a property target, packed into a Vector4. Read from the captured value, or from the first material that has the property when nothing was captured for it.</summary>
        public Vector4 PropertyOf(Renderer renderer, string property, PropertyValueType type)
        {
            if (properties.TryGetValue((renderer, property), out var value)) return value;
            return ReadProperty(renderer, property, type, null);
        }

        /// <summary>The scene value of a custom animation binding captured before the build changed anything, or false when it never resolved.</summary>
        public bool TryGetCustomFloat(EditorCurveBinding binding, out float value) => customFloats.TryGetValue(binding, out value);

        /// <summary>The scene object reference of a custom animation binding captured before the build changed anything, or false when it never resolved.</summary>
        public bool TryGetCustomObject(EditorCurveBinding binding, out Object value) => customObjects.TryGetValue(binding, out value);

        static Vector4 ReadProperty(Renderer renderer, string property, PropertyValueType type, MenuEntry entry)
            => MenuModel.ReadProperty(MenuModel.MaterialWithProperty(renderer, property, entry), property, type);
    }
}
#endif
