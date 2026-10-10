#if DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.Constraint.Components;
#if DOCME_NDMF
using nadena.dev.ndmf.animator;
#endif

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Which blend shape targets a toggle writes: everything, everything but the pins, or the pins alone.</summary>
    public enum ShapeSet { All, MainOnly, PinsOnly }

    /// <summary>The values one animator state writes: GameObject active flags, blendshape weights and material slots.</summary>
    public sealed class ClipContent
    {
        struct Key : IEquatable<Key>
        {
            public UnityEngine.Object Target;
            public string Property;
            public bool Equals(Key other) => Target == other.Target && Property == other.Property;
            public override bool Equals(object obj) => obj is Key other && Equals(other);
            public override int GetHashCode() => (Target != null ? Target.GetInstanceID() : 0) * 397 ^ Property.GetHashCode();
        }

        readonly Dictionary<Key, object> values = new Dictionary<Key, object>();
        readonly List<Key> order = new List<Key>();
        readonly Transform avatarRoot;

        /// <summary>Custom clip curves merged in on top of the generated values, in avatar-root-relative paths. A float binding carries curve; an object reference binding carries keys.</summary>
        readonly List<(EditorCurveBinding binding, AnimationCurve curve, ObjectReferenceKeyframe[] keys)> customCurves = new List<(EditorCurveBinding, AnimationCurve, ObjectReferenceKeyframe[])>();

        /// <summary>Generated blend shape and property values written as a linear ramp instead of a constant, for a radial whose custom clips replace its blend tree with one motion-time clip.</summary>
        readonly List<(Key key, float from, float to)> ramps = new List<(Key, float, float)>();
        float rampLength = 1f / 60f;

        /// <summary>Animator parameters this content animates, such as a pause flag: the binding is the Animator at the avatar root.</summary>
        readonly Dictionary<string, float> animatorParameters = new Dictionary<string, float>();

        /// <summary>The loop setting the merged clip takes from the entry's first usable custom clip.</summary>
        bool? customLoop;

        /// <summary>Set for the clip a radial plays with its parameter as motion time: a looping clip wraps at 1.0, which would snap the radial back to its start at 100 percent.</summary>
        bool noLoop;

        /// <summary>Targets outside avatarRoot are skipped. Passing null keeps every target.</summary>
        public ClipContent(Transform avatarRoot = null)
        {
            this.avatarRoot = avatarRoot;
        }

        public int Count => values.Count + customCurves.Count + ramps.Count;

        public void SetActive(GameObject go, bool active) => Set(go, "m_IsActive", active ? 1f : 0f);

        public void SetShape(SkinnedMeshRenderer renderer, string shape, float weight) => Set(renderer, "blendShape." + shape, weight);

        /// <summary>A shape weight already written into this content, for a later default that must not read the stale scene value.</summary>
        public bool TryGetShape(SkinnedMeshRenderer renderer, string shape, out float weight)
        {
            weight = 0f;
            if (renderer == null || !values.TryGetValue(new Key { Target = renderer, Property = "blendShape." + shape }, out var value)) return false;
            if (!(value is float f)) return false;
            weight = f;
            return true;
        }

        public void SetMaterial(Renderer renderer, int slot, Material material) => Set(renderer, "m_Materials.Array.data[" + slot + "]", material);

        /// <summary>Animates a Float animator parameter of the controller this clip plays in to a constant value.</summary>
        public void SetAnimatorParameter(string parameter, float value) => animatorParameters[parameter] = value;

        /// <summary>Every blend shape binding this content writes, renderer, shape name and weight. Used to find
        /// out what a pin layer's on state actually pinned, without duplicating WriteToggle's own filtering.</summary>
        public IEnumerable<(SkinnedMeshRenderer renderer, string shape, float weight)> ShapeWrites()
        {
            foreach (var key in order)
            {
                if (!(key.Target is SkinnedMeshRenderer renderer) || !key.Property.StartsWith("blendShape.")) continue;
                if (values[key] is float weight) yield return (renderer, key.Property.Substring("blendShape.".Length), weight);
            }
        }

        /// <summary>
        /// Removes every material property value from this content and returns each one with its renderer and its
        /// animated property name ("material._Glow", "material._Color.r"). A trigger's inactive state hands these
        /// to the final pass, which writes them back only where no other layer owns the binding.
        /// </summary>
        public List<(Renderer renderer, string property, float value)> TakePropertyWrites()
        {
            var taken = new List<(Renderer, string, float)>();
            foreach (var key in order)
            {
                if (!(key.Target is Renderer renderer) || !key.Property.StartsWith("material.")) continue;
                if (values[key] is float value) taken.Add((renderer, key.Property, value));
            }
            foreach (var (renderer, property, _) in taken)
            {
                var key = new Key { Target = renderer, Property = property };
                values.Remove(key);
                order.Remove(key);
            }
            return taken;
        }

        /// <summary>Writes one source's weight on a follow's constraint. The exact property VRChat exposes for animation: "Sources.source&lt;N&gt;.Weight".</summary>
        public void SetFollowWeight(VRCParentConstraint constraint, int sourceIndex, float weight) => Set(constraint, "Sources.source" + sourceIndex + ".Weight", weight);

        /// <summary>Enables or disables a component, such as a grabbable's PhysBone under its own menu toggle. The same "m_Enabled" property every Behaviour exposes for animation, the component-level counterpart of SetActive.</summary>
        public void SetEnabled(Component component, bool enabled) => Set(component, "m_Enabled", enabled ? 1f : 0f);

        /// <summary>Writes a shader property for the whole renderer. A float uses value.x; a colour writes its r/g/b/a; a vector writes its x/y/z/w.</summary>
        public void SetProperty(Renderer renderer, string property, PropertyValueType type, Vector4 value)
        {
            switch (type)
            {
                case PropertyValueType.Color:
                    Set(renderer, "material." + property + ".r", value.x);
                    Set(renderer, "material." + property + ".g", value.y);
                    Set(renderer, "material." + property + ".b", value.z);
                    Set(renderer, "material." + property + ".a", value.w);
                    break;
                case PropertyValueType.Vector:
                    Set(renderer, "material." + property + ".x", value.x);
                    Set(renderer, "material." + property + ".y", value.y);
                    Set(renderer, "material." + property + ".z", value.z);
                    Set(renderer, "material." + property + ".w", value.w);
                    break;
                default:
                    Set(renderer, "material." + property, value.x);
                    break;
            }
        }

        void Set(UnityEngine.Object target, string property, object value)
        {
            var key = new Key { Target = target, Property = property };
            if (!values.ContainsKey(key)) order.Add(key);
            values[key] = value;
        }

        /// <summary>
        /// Writes a toggle's targets in the On or Off state. Off values come from the base state. The shape set
        /// splits a toggle between its main layer and its pin layer; objects and materials belong to the main one.
        /// followPlan is read only for a grabbable's own menu toggle, to enable or disable the PhysBone it built:
        /// the entry's own layer is the only one that ever claims that switch, so it is safe to write here, unlike
        /// a follow's source weights.
        /// </summary>
        public void WriteToggle(MenuEntry entry, bool on, BaseState baseState, ShapeSet shapes = ShapeSet.All, FollowPlan followPlan = null)
        {
            if (entry.kind == EntryKind.Grabbable && followPlan != null && followPlan.GrabPhysBones.TryGetValue(entry, out var grabPhysBone) && grabPhysBone != null)
                SetEnabled(grabPhysBone, on);
            if (shapes != ShapeSet.PinsOnly)
                foreach (var target in entry.objects)
                    if (target.target != null && !Outside(target.target.transform))
                        SetActive(target.target, EffectMath.ObjectActive(target, on));
            // A follow's source weights are never written here: FollowLayerBuilder's own layer is the only writer
            // of a constraint's sources, so two entries claiming the same object never fight from two layers.
            foreach (var target in entry.blendShapes)
            {
                bool pin = target.mode == ShapeEffectMode.Pin;
                if (shapes == ShapeSet.MainOnly && pin) continue;
                if (shapes == ShapeSet.PinsOnly && !pin) continue;
                if (!on && !EffectMath.WritesWhenOff(target)) continue;
                if (MenuModel.ShapeExists(target.renderer, target.shape) && !Outside(target.renderer.transform))
                    SetShape(target.renderer, target.shape, EffectMath.ToggleShapeWeight(target, on, baseState.WeightOf(target.renderer, target.shape)));
            }
            // A shape set rule lives in the same pin state the pinned rows use: forced to 0 while on, nothing at
            // all while off, so it holds over whatever else animates that mesh only while the control is on.
            if (on && shapes != ShapeSet.MainOnly)
                foreach (var rule in ShapeSetModel.EffectiveRules(entry))
                    if (!Outside(rule.mesh.transform))
                        foreach (var shape in ShapeSetModel.ZeroedShapes(entry, rule))
                            SetShape(rule.mesh, shape, 0f);
            if (shapes != ShapeSet.PinsOnly)
                foreach (var target in entry.materials)
                    if (MenuModel.SlotExists(target) && !Outside(target.renderer.transform))
                        SetMaterial(target.renderer, target.slot, EffectMath.ToggleMaterial(target, on, baseState.MaterialOf(target.renderer, target.slot)));
            if (shapes != ShapeSet.PinsOnly)
                foreach (var target in entry.properties)
                    if (MenuModel.PropertyExists(target, entry) && !Outside(target.renderer.transform))
                        SetProperty(target.renderer, target.property, target.type,
                            EffectMath.PropertyValue(target, on, baseState.PropertyOf(target.renderer, target.property, target.type)));
        }

        /// <summary>
        /// Follow distance's Far state: everything a normal off state writes except the blend shapes, which a
        /// later pass releases to rest only once nothing else already holds them. Objects and materials keep
        /// writing their off value directly. Properties are written here too; the layer builder takes them back
        /// out (TakePropertyWrites) and hands them to the same later pass when it has one to hand them to.
        /// </summary>
        public void WriteFarPose(MenuEntry entry, BaseState baseState)
        {
            foreach (var target in entry.objects)
                if (target.target != null && !Outside(target.target.transform))
                    SetActive(target.target, EffectMath.ObjectActive(target, false));
            foreach (var target in entry.materials)
                if (MenuModel.SlotExists(target) && !Outside(target.renderer.transform))
                    SetMaterial(target.renderer, target.slot, EffectMath.ToggleMaterial(target, false, baseState.MaterialOf(target.renderer, target.slot)));
            foreach (var target in entry.properties)
                if (MenuModel.PropertyExists(target, entry) && !Outside(target.renderer.transform))
                    SetProperty(target.renderer, target.property, target.type, EffectMath.PropertyValue(target, false, baseState.PropertyOf(target.renderer, target.property, target.type)));
        }

        /// <summary>Writes an override's pinned values.</summary>
        public void WriteOverride(MenuEntry entry)
        {
            foreach (var target in entry.blendShapes)
                if (MenuModel.ShapeExists(target.renderer, target.shape) && !Outside(target.renderer.transform))
                    SetShape(target.renderer, target.shape, target.weight);
        }

        /// <summary>Writes a radial's shapes at position t (0..1) between their min and max weights.</summary>
        public void WriteRadial(MenuEntry entry, float t)
        {
            foreach (var target in entry.blendShapes)
                if (MenuModel.ShapeExists(target.renderer, target.shape) && !Outside(target.renderer.transform))
                    SetShape(target.renderer, target.shape, EffectMath.RadialShapeWeight(target, t));
            foreach (var target in entry.properties)
                if (MenuModel.PropertyExists(target, entry) && !Outside(target.renderer.transform))
                    SetProperty(target.renderer, target.property, target.type, EffectMath.RadialPropertyValue(target, t));
        }

        /// <summary>Writes a radial's blend shape and property values as linear ramps from their min to max across [0, length], for a radial whose custom clips replace the blend tree with one motion-time clip.</summary>
        public void WriteRadialRamp(MenuEntry entry, float length)
        {
            rampLength = length;
            noLoop = true;
            foreach (var target in entry.blendShapes)
                if (MenuModel.ShapeExists(target.renderer, target.shape) && !Outside(target.renderer.transform))
                    SetRamp(target.renderer, "blendShape." + target.shape, EffectMath.RadialShapeWeight(target, 0f), EffectMath.RadialShapeWeight(target, 1f));
            foreach (var target in entry.properties)
                if (MenuModel.PropertyExists(target, entry) && !Outside(target.renderer.transform))
                    AddPropertyRamp(target);
        }

        void SetRamp(UnityEngine.Object target, string property, float from, float to) => ramps.Add((new Key { Target = target, Property = property }, from, to));

        void AddPropertyRamp(MaterialPropertyTarget target)
        {
            var from = EffectMath.RadialPropertyValue(target, 0f);
            var to = EffectMath.RadialPropertyValue(target, 1f);
            switch (target.type)
            {
                case PropertyValueType.Color:
                    SetRamp(target.renderer, "material." + target.property + ".r", from.x, to.x);
                    SetRamp(target.renderer, "material." + target.property + ".g", from.y, to.y);
                    SetRamp(target.renderer, "material." + target.property + ".b", from.z, to.z);
                    SetRamp(target.renderer, "material." + target.property + ".a", from.w, to.w);
                    break;
                case PropertyValueType.Vector:
                    SetRamp(target.renderer, "material." + target.property + ".x", from.x, to.x);
                    SetRamp(target.renderer, "material." + target.property + ".y", from.y, to.y);
                    SetRamp(target.renderer, "material." + target.property + ".z", from.z, to.z);
                    SetRamp(target.renderer, "material." + target.property + ".w", from.w, to.w);
                    break;
                default:
                    SetRamp(target.renderer, "material." + target.property, from.x, to.x);
                    break;
            }
        }

        /// <summary>Merges an entry's collected custom clip bindings on top of the values already written here. A binding this content already writes (compared by avatar-root-relative path, type and property) keeps the generated value; the entry gets one ad.info.animation_overridden with the count of bindings that lost.</summary>
        public void MergeCustom(IReadOnlyList<(EditorCurveBinding binding, AnimationCurve curve, ObjectReferenceKeyframe[] keys)> custom, MenuEntry entry, List<ModelMessage> messages)
        {
            if (custom.Count == 0) return;
            ApplyLoopFromFirstClip(entry);
            var overridden = new HashSet<EditorCurveBinding>();
            foreach (var (binding, curve, keys) in custom)
            {
                if (Writes(binding))
                {
                    overridden.Add(binding);
                    continue;
                }
                customCurves.Add((binding, curve, keys));
            }
            if (overridden.Count > 0) messages.Add(ModelMessage.Info("ad.info.animation_overridden", entry.PlainName, overridden.Count).For(entry.id));
        }

        /// <summary>Writes the scene value of every custom binding that resolves on the avatar (skipping one this content already writes), so a Write-Defaults-off avatar returns to rest after a custom clip plays. A binding that never resolved is left out and reported once with the count.</summary>
        public void WriteCustomOff(IReadOnlyList<(EditorCurveBinding binding, AnimationCurve curve, ObjectReferenceKeyframe[] keys)> custom, BaseState baseState, MenuEntry entry, List<ModelMessage> messages)
        {
            if (custom.Count == 0) return;
            var seen = new HashSet<EditorCurveBinding>();
            int missing = 0;
            foreach (var (binding, curve, keys) in custom)
            {
                if (!seen.Add(binding) || Writes(binding) || HasCustomCurve(binding)) continue;
                if (curve != null)
                {
                    if (baseState.TryGetCustomFloat(binding, out float value)) customCurves.Add((binding, AnimationCurve.Constant(0f, 1f / 60f, value), null));
                    else missing++;
                }
                else
                {
                    if (baseState.TryGetCustomObject(binding, out var obj))
                        customCurves.Add((binding, null, new[] { new ObjectReferenceKeyframe { time = 0f, value = obj }, new ObjectReferenceKeyframe { time = 1f / 60f, value = obj } }));
                    else missing++;
                }
            }
            if (missing > 0) messages.Add(ModelMessage.Warning("ad.warn.animation_binding_missing", entry.PlainName, missing).For(entry.id));
        }

        void ApplyLoopFromFirstClip(MenuEntry entry)
        {
            if (customLoop.HasValue) return;
            foreach (var target in entry.animations)
            {
                if (target.clip == null || CustomClips.IsProxy(target.clip)) continue;
                customLoop = AnimationUtility.GetAnimationClipSettings(target.clip).loopTime;
                return;
            }
        }

        /// <summary>True when this content already writes the generated value for this binding, compared by avatar-root-relative path, type and property name.</summary>
        bool Writes(EditorCurveBinding binding)
        {
            foreach (var key in order)
                if (KeyMatches(key, binding)) return true;
            foreach (var (key, _, _) in ramps)
                if (KeyMatches(key, binding)) return true;
            return false;
        }

        /// <summary>True when a custom curve for this binding is already merged or restored, so a binding two of a group's members share is not written twice.</summary>
        bool HasCustomCurve(EditorCurveBinding binding)
        {
            foreach (var (existing, _, _) in customCurves)
                if (existing.Equals(binding)) return true;
            return false;
        }

        bool KeyMatches(Key key, EditorCurveBinding binding)
        {
            if (key.Property != binding.propertyName) return false;
            var gameObject = key.Target is GameObject go ? go : ((Component)key.Target).gameObject;
            var type = key.Target is GameObject ? typeof(GameObject) : key.Target.GetType();
            if (type != binding.type) return false;
            return AnimationUtility.CalculateTransformPath(gameObject.transform, avatarRoot) == binding.path;
        }

        bool Outside(Transform target) => MenuModel.IsOutsideAvatar(avatarRoot, target);

        /// <summary>Writes a blend shape trigger's effects for one value of its source shape, in its own style: what the scene should show at that weight.</summary>
        public void WriteLinkDefault(MenuEntry entry, float sourceWeight, BaseState baseState)
        {
            bool above = EffectMath.LinkAbove(sourceWeight, entry.sourceThreshold);
            foreach (var target in entry.objects)
                if (target.target != null && !Outside(target.target.transform)) SetActive(target.target, EffectMath.ObjectActive(target, above));
            foreach (var target in entry.blendShapes)
            {
                if (target.renderer == entry.sourceRenderer && target.shape == entry.sourceShape) continue;
                if (!MenuModel.ShapeExists(target.renderer, target.shape) || Outside(target.renderer.transform)) continue;
                float value = EffectMath.LinkShapeWeight(entry, target, sourceWeight, baseState.WeightOf(target.renderer, target.shape));
                SetShape(target.renderer, target.shape, value);
            }
            foreach (var target in entry.materials)
                if (MenuModel.SlotExists(target) && !Outside(target.renderer.transform))
                    SetMaterial(target.renderer, target.slot, EffectMath.ToggleMaterial(target, above, baseState.MaterialOf(target.renderer, target.slot)));
        }

        public void ApplyToScene()
        {
            foreach (var key in order)
            {
                var value = values[key];
                if (key.Target is GameObject go) go.SetActive((float)value > 0.5f);
                else if (key.Target is SkinnedMeshRenderer smr && key.Property.StartsWith("blendShape."))
                    smr.SetBlendShapeWeight(smr.sharedMesh.GetBlendShapeIndex(key.Property.Substring("blendShape.".Length)), (float)value);
                else if (key.Target is Renderer renderer)
                {
                    // A property target is never written into the scene: doing so would mutate the material asset
                    // itself. The animator's first frame carries the default value instead, as it does for every
                    // other target.
                    if (key.Property.StartsWith("material.")) continue;
                    int slot = SlotOf(key.Property);
                    var shared = renderer.sharedMaterials;
                    shared[slot] = value as Material;
                    renderer.sharedMaterials = shared;
                }
                else if (key.Target is VRCParentConstraint constraint && key.Property.StartsWith("Sources.source"))
                {
                    int index = FollowSourceIndex(key.Property);
                    var source = constraint.Sources[index];
                    source.Weight = (float)value;
                    constraint.Sources[index] = source;
                }
                else if (key.Target is Behaviour behaviour && key.Property == "m_Enabled")
                {
                    behaviour.enabled = (float)value > 0.5f;
                }
            }
        }

        static int FollowSourceIndex(string property)
        {
            int start = "Sources.source".Length;
            int end = property.IndexOf('.', start);
            return int.Parse(property.Substring(start, end - start));
        }

        static int SlotOf(string property)
        {
            int start = property.IndexOf('[') + 1;
            return int.Parse(property.Substring(start, property.IndexOf(']') - start));
        }

#if DOCME_NDMF
        public VirtualClip ToClip(AnimatorServicesContext asc, string name)
        {
            var clip = VirtualClip.Create(name);
            foreach (var key in order)
            {
                var value = values[key];
                var gameObject = key.Target is GameObject go ? go : ((Component)key.Target).gameObject;
                var path = asc.ObjectPathRemapper.GetVirtualPathForObject(gameObject);
                var type = key.Target is GameObject ? typeof(GameObject) : key.Target.GetType();
                if (value is float f)
                {
                    clip.SetFloatCurve(EditorCurveBinding.FloatCurve(path, type, key.Property), AnimationCurve.Constant(0f, 1f / 60f, f));
                }
                else
                {
                    var material = value as Material;
                    clip.SetObjectCurve(EditorCurveBinding.PPtrCurve(path, type, key.Property), new[]
                    {
                        new ObjectReferenceKeyframe { time = 0f, value = material },
                        new ObjectReferenceKeyframe { time = 1f / 60f, value = material },
                    });
                }
            }
            foreach (var (key, from, to) in ramps)
            {
                var gameObject = key.Target is GameObject go ? go : ((Component)key.Target).gameObject;
                var path = asc.ObjectPathRemapper.GetVirtualPathForObject(gameObject);
                var type = key.Target is GameObject ? typeof(GameObject) : key.Target.GetType();
                // Linear, not the eased default of a two key curve: the blend tree radial is linear, and the
                // motion time one has to match it.
                clip.SetFloatCurve(EditorCurveBinding.FloatCurve(path, type, key.Property), AnimationCurve.Linear(0f, from, rampLength, to));
            }
            foreach (var pair in animatorParameters)
                clip.SetFloatCurve(EditorCurveBinding.FloatCurve("", typeof(Animator), pair.Key), AnimationCurve.Constant(0f, 1f / 60f, pair.Value));
            foreach (var (binding, curve, keys) in customCurves)
            {
                var final = RemapCustomBinding(asc, binding);
                if (curve != null) clip.SetFloatCurve(final, curve);
                else clip.SetObjectCurve(final, keys);
            }
            var loop = noLoop ? false : customLoop;
            if (loop.HasValue)
            {
                var settings = clip.Settings;
                settings.loopTime = loop.Value;
                clip.Settings = settings;
            }
            return clip;
        }

        /// <summary>A custom binding's path is authored relative to the avatar root; resolved to an actual object it uses the same virtual path the generated content does, so overrides find it too. An unresolved path is kept as-is.</summary>
        EditorCurveBinding RemapCustomBinding(AnimatorServicesContext asc, EditorCurveBinding binding)
        {
            if (avatarRoot == null) return binding;
            var resolved = binding.path == "" ? avatarRoot : avatarRoot.Find(binding.path);
            if (resolved == null) return binding;
            var remapped = binding;
            remapped.path = asc.ObjectPathRemapper.GetVirtualPathForObject(resolved.gameObject);
            return remapped;
        }
#endif
    }
}
#endif
