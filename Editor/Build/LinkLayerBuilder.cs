#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// One FX layer per blend shape trigger, driven by the hidden copy of its shape's weight (TriggerPlan.MirrorParameter),
    /// which ClipRewriter writes into every FX clip that moves the shape in the final pass. Built like Follow
    /// distance's own layer: every state Write Defaults off, at the highest priority and named into pinLayerNames so
    /// it is moved last, its resting state leaving shapes and properties to the final pass's release rule and never
    /// holding a shape VRChat blinks or speaks with.
    ///
    /// Up to three states and Reset: the rest state (Far, or Off in Switch); a driven state below the switch point
    /// (Near, or Following in Switch when a Follow row moves there); and a driven state at or above it (Near on, or
    /// On). Both driven states hold the same blend for shapes and properties and differ only in objects and material
    /// slots, which cannot blend, so those swap exactly at the switch point instead of wherever a blend tree would
    /// pick. The layer starts in Load, which passes to Reset and then the rest state, and every way back from a
    /// driven state to rest passes Reset.
    /// </summary>
    public static class LinkLayerBuilder
    {
        /// <summary>How far above the lowest weight anything moves at the copy has to be before the rest state is left, in shape weight, so float noise on a resting shape never wakes the layer.</summary>
        const float RestMargin = 0.01f;

        /// <summary>How far below the threshold the switch sits, so a shape exactly at the threshold counts as above it, as EffectMath.LinkAbove has it.</summary>
        const float SwitchMargin = 0.001f;

        /// <summary>The rows of one trigger the layer can build: those whose target is on the avatar, without a row that names the trigger's own shape, and every shape its rules hold.</summary>
        sealed class Effects
        {
            public readonly List<BlendShapeTarget> Fixed = new List<BlendShapeTarget>();
            public readonly List<BlendShapeTarget> Follow = new List<BlendShapeTarget>();
            public readonly List<ObjectTarget> Objects = new List<ObjectTarget>();
            public readonly List<MaterialTarget> Materials = new List<MaterialTarget>();
            public readonly List<MaterialPropertyTarget> Properties = new List<MaterialPropertyTarget>();
            /// <summary>Each shape a rule holds, with its rule: it goes from its default to the rule's value the way a Fixed row goes to its weight.</summary>
            public readonly List<(SkinnedMeshRenderer renderer, string shape, ShapeSetRule rule)> Held = new List<(SkinnedMeshRenderer, string, ShapeSetRule)>();

            public Effects(MenuEntry entry, Transform avatarRoot, ShapeSetModel.HeldContext context)
            {
                foreach (var rule in ShapeSetModel.EffectiveRules(entry))
                    if (!MenuModel.IsOutsideAvatar(avatarRoot, rule.mesh.transform))
                        foreach (var shape in ShapeSetModel.HeldShapes(entry, rule, context))
                            Held.Add((rule.mesh, shape, rule));
                foreach (var target in entry.objects)
                    if (target.target != null && !MenuModel.IsOutsideAvatar(avatarRoot, target.target.transform)) Objects.Add(target);
                foreach (var target in entry.blendShapes)
                {
                    if (target.renderer == entry.sourceRenderer && target.shape == entry.sourceShape) continue;
                    if (!MenuModel.ShapeExists(target.renderer, target.shape) || MenuModel.IsOutsideAvatar(avatarRoot, target.renderer.transform)) continue;
                    if (target.mode == ShapeEffectMode.Follow) Follow.Add(target);
                    else Fixed.Add(target);
                }
                foreach (var target in entry.materials)
                    if (MenuModel.SlotExists(target) && !MenuModel.IsOutsideAvatar(avatarRoot, target.renderer.transform)) Materials.Add(target);
                foreach (var target in entry.properties)
                    if (MenuModel.PropertyExists(target, entry) && !MenuModel.IsOutsideAvatar(avatarRoot, target.renderer.transform)) Properties.Add(target);
            }

            public bool IsEmpty => Fixed.Count + Follow.Count + Objects.Count + Materials.Count + Properties.Count + Held.Count == 0;
            /// <summary>Objects and material slots: they cannot blend, so they switch.</summary>
            public bool Swapped => Objects.Count > 0 || Materials.Count > 0;
            /// <summary>Shapes and properties that go from rest to their value: blended in Smooth, switched in Switch.</summary>
            public bool Ranged => Fixed.Count > 0 || Properties.Count > 0 || Held.Count > 0;
        }

        public static List<ModelMessage> Build(AnimatorServicesContext asc, MenuTree tree, IReadOnlyList<TriggerPlan> plans, BaseState baseState, ICollection<string> pinLayerNames, PinLayerState pinState)
        {
            var messages = new List<ModelMessage>();
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return messages;
            foreach (var plan in plans)
            {
                if (plan.Kind != TriggerKind.Link) continue;
                var effects = new Effects(plan.Entry, tree.AvatarRoot, baseState.HeldContext ?? ShapeSetModel.ContextFor(tree.AvatarRoot));
                if (effects.IsEmpty)
                {
                    // Nothing to build, so nothing replaces the shape: the final pass keeps its curves and says so.
                    if (plan.Replace && pinState != null)
                        pinState.ShapeMirrors.Add(new PinLayerState.ShapeMirror { Entry = plan.Entry, Renderer = plan.SourceRenderer, Shape = plan.SourceShape, Replace = true });
                    continue;
                }
                BuildLayer(asc, fx, tree.AvatarRoot, plan, effects, baseState, pinLayerNames, pinState);
            }
            return messages;
        }

        static void BuildLayer(AnimatorServicesContext asc, VirtualAnimatorController fx, Transform avatarRoot, TriggerPlan plan, Effects effects, BaseState baseState,
            ICollection<string> pinLayerNames, PinLayerState pinState)
        {
            var entry = plan.Entry;
            bool smooth = plan.Style == BlendShapeLinkStyle.Smooth;
            float start = plan.SmoothStart, full = plan.SmoothFull, threshold = plan.Threshold;
            var mirror = plan.MirrorParameter;

            int sourceIndex = plan.SourceRenderer.sharedMesh.GetBlendShapeIndex(plan.SourceShape);
            AnimatorBuilder.EnsureParameter(fx, mirror, PlannedType.Float, plan.SourceRenderer.GetBlendShapeWeight(sourceIndex));
            foreach (var required in plan.Conditions.Concat(plan.Gate))
                AnimatorBuilder.EnsureParameter(fx, required.Parameter, required.Type, required.Default);

            // The lowest weight at which anything leaves its rest: a Follow row moves from 0, a ranged row from
            // Start (Smooth) or the threshold (Switch), a swapped object or material at the threshold.
            float low = float.PositiveInfinity;
            if (effects.Follow.Count > 0) low = 0f;
            if (effects.Ranged) low = Mathf.Min(low, smooth ? start : threshold);
            if (effects.Swapped) low = Mathf.Min(low, threshold);
            float lo = low + RestMargin;
            float hi = threshold - SwitchMargin;
            bool hasAbove = effects.Swapped || (!smooth && effects.Ranged);
            bool hasBelow = !hasAbove || lo < hi;

            // Every weight where some row bends: between two neighbours every row is a straight line, so a
            // Simple1D tree with a child at each of them gives every row its exact value at every weight.
            var points = new List<float>();
            if (smooth && effects.Ranged) { points.Add(start); points.Add(full); }
            foreach (var row in effects.Follow)
            {
                points.Add(0f);
                points.Add(100f);
                if (row.multiplier > 1f) points.Add(100f / row.multiplier);
            }
            points = points.OrderBy(p => p).Aggregate(new List<float>(), (kept, p) => { if (kept.Count == 0 || p - kept[kept.Count - 1] > 0.0001f) kept.Add(p); return kept; });

            // The shared blink and lip sync pause reads this flag: 0 at rest, 1 in every state that moves the
            // effects, which is only reached while every requirement holds.
            var pauseFlag = plan.PauseFlag?.Parameter;
            if (pauseFlag != null) AnimatorBuilder.EnsureParameter(fx, pauseFlag, PlannedType.Float, 0f);

            ClipContent At(float x, bool swappedOn)
            {
                var content = new ClipContent(avatarRoot);
                if (pauseFlag != null) content.SetAnimatorParameter(pauseFlag, 1f);
                float fraction = smooth ? EffectMath.SmoothFraction(x, start, full) : (swappedOn ? 1f : 0f);
                foreach (var row in effects.Objects) content.SetActive(row.target, EffectMath.ObjectActive(row, swappedOn));
                foreach (var row in effects.Fixed) content.SetShape(row.renderer, row.shape, Mathf.Lerp(baseState.WeightOf(row.renderer, row.shape), row.weight, fraction));
                foreach (var row in effects.Follow) content.SetShape(row.renderer, row.shape, EffectMath.FollowWeight(x, row.multiplier));
                foreach (var (renderer, shape, rule) in effects.Held)
                {
                    float rest = baseState.WeightOf(renderer, shape);
                    content.SetHeldShape(renderer, shape, Mathf.Lerp(rest, ShapeSetModel.HeldWeight(rule, rest), fraction), rule);
                }
                foreach (var row in effects.Materials) content.SetMaterial(row.renderer, row.slot, EffectMath.ToggleMaterial(row, swappedOn, baseState.MaterialOf(row.renderer, row.slot)));
                foreach (var row in effects.Properties)
                {
                    var baseValue = baseState.PropertyOf(row.renderer, row.property, row.type);
                    content.SetProperty(row.renderer, row.property, row.type, Vector4.Lerp(EffectMath.PropertyValue(row, false, baseValue), EffectMath.PropertyValue(row, true, baseValue), fraction), entry, baseState,
                        own => Vector4.Lerp(EffectMath.PropertyValue(row, false, own), EffectMath.PropertyValue(row, true, own), fraction));
                }
                return content;
            }

            var name = AnimatorBuilder.UniqueLayerName(fx, "AD: " + entry.PlainName + " (shape)");
            // The contents of the motion Motion made last, each with its blend tree child (-1 for a lone clip), so the
            // rule's curves in them can be recorded once their state is in the layer.
            var made = new List<(ClipContent content, int child)>();
            VirtualMotion Motion(string stateName, bool swappedOn, float alone)
            {
                made.Clear();
                if (points.Count < 2)
                {
                    var single = At(points.Count == 1 ? points[0] : alone, swappedOn);
                    made.Add((single, -1));
                    return single.ToClip(asc, name + " " + stateName);
                }
                var blend = VirtualBlendTree.Create(name + " " + stateName);
                blend.BlendType = BlendTreeType.Simple1D;
                blend.BlendParameter = mirror;
                blend.UseAutomaticThresholds = false;
                var children = new List<VirtualBlendTree.VirtualChildMotion>();
                for (int i = 0; i < points.Count; i++)
                {
                    var content = At(points[i], swappedOn);
                    made.Add((content, i));
                    children.Add(new VirtualBlendTree.VirtualChildMotion { Motion = content.ToClip(asc, name + " " + stateName + " " + i), Threshold = points[i] });
                }
                blend.Children = ImmutableList.CreateRange(children);
                return blend;
            }
            var heldContext = baseState.HeldContext ?? ShapeSetModel.ContextFor(avatarRoot);
            void RecordHeld(VirtualStateMachine stateMachine, VirtualState state)
            {
                foreach (var (content, child) in made)
                    HeldShapePruner.Record(pinState, content, entry, name, stateMachine, state, child, baseState, heldContext);
            }

            pinLayerNames?.Add(name);
            var layer = fx.AddLayer(new LayerPriority(int.MaxValue), name);
            var sm = layer.StateMachine;

            // Rest: objects and material slots at their off values; shapes and properties never carry a curve here.
            // The final pass writes their rest values into Reset, once on the way back: a shape even where another
            // layer owns it or VRChat blinks or speaks with it, a property only where no other layer owns it.
            var restContent = new ClipContent(avatarRoot);
            if (pauseFlag != null)
            {
                restContent.SetAnimatorParameter(pauseFlag, 0f);
                pinState?.PauseFlags.Add(plan.PauseFlag);
            }
            foreach (var row in effects.Objects) restContent.SetActive(row.target, EffectMath.ObjectActive(row, false));
            foreach (var row in effects.Materials) restContent.SetMaterial(row.renderer, row.slot, EffectMath.ToggleMaterial(row, false, baseState.MaterialOf(row.renderer, row.slot)));
            foreach (var row in effects.Properties)
            {
                var restValue = EffectMath.PropertyValue(row, false, baseState.PropertyOf(row.renderer, row.property, row.type));
                restContent.SetProperty(row.renderer, row.property, row.type, restValue, entry, baseState, own => EffectMath.PropertyValue(row, false, own));
            }
            if (pinState != null)
            {
                foreach (var (renderer, property, value) in restContent.TakePropertyWrites())
                    pinState.PropertyRests.Add(new PinLayerState.PropertyRest { LayerName = name, Renderer = renderer, Property = property, RestValue = value });
                foreach (var (renderer, shape) in effects.Fixed.Concat(effects.Follow).Select(row => (row.renderer, row.shape)).Concat(effects.Held.Select(h => (h.renderer, h.shape))))
                    pinState.Bindings.Add(new PinLayerState.PinBinding { LayerName = name, Renderer = renderer, Shape = shape, RestValue = baseState.WeightOf(renderer, shape) });
                pinState.ShapeMirrors.Add(new PinLayerState.ShapeMirror { Entry = entry, Renderer = plan.SourceRenderer, Shape = plan.SourceShape, Parameter = mirror, Replace = plan.Replace });
            }

            var rest = sm.AddState(smooth ? "Far" : "Off", restContent.ToClip(asc, name + (smooth ? " Far" : " Off")), new Vector3(0f, 0f, 0f));
            rest.WriteDefaultValues = false;
            sm.DefaultState = rest;
            VirtualState below = null, above = null;
            if (hasBelow)
            {
                string belowName = smooth ? "Near" : hasAbove ? "Following" : "On";
                below = sm.AddState(belowName, Motion(belowName, false, low), new Vector3(300f, 0f, 0f));
                below.WriteDefaultValues = false;
                RecordHeld(sm, below);
            }
            if (hasAbove)
            {
                string aboveName = smooth ? (hasBelow ? "Near on" : "Near") : "On";
                above = sm.AddState(aboveName, Motion(aboveName, true, Mathf.Max(threshold, 0f)), new Vector3(300f, 150f, 0f));
                above.WriteDefaultValues = false;
                RecordHeld(sm, above);
            }

            // Requirements join every way in (all must hold) and add one way out each (any failing leaves). The
            // shape's own way out comes first, so a shape that drops below everything goes straight back. Every
            // way back goes through Reset, which writes the rest values once, to the rest state.
            var back = pinState != null ? AnimatorBuilder.AddResetState(sm, name, rest, new Vector3(150f, 300f, 0f), false, pinState) : rest;
            AnimatorCondition Mirror(AnimatorConditionMode mode, float value) => new AnimatorCondition { parameter = mirror, mode = mode, threshold = value };
            ImmutableList<AnimatorCondition> With(IEnumerable<AnimatorCondition> requirement, params AnimatorCondition[] own) => ImmutableList.CreateRange(own.Concat(requirement));

            var fromRest = new List<VirtualStateTransition>();
            if (above != null)
                fromRest.AddRange(plan.Enter.Select(e => AnimatorBuilder.Transition(above, With(e, Mirror(AnimatorConditionMode.Greater, hi)))));
            if (below != null)
                fromRest.AddRange(plan.Enter.Select(e => AnimatorBuilder.Transition(below, above != null
                    ? With(e, Mirror(AnimatorConditionMode.Greater, lo), Mirror(AnimatorConditionMode.Less, hi))
                    : With(e, Mirror(AnimatorConditionMode.Greater, lo)))));
            rest.Transitions = ImmutableList.CreateRange(fromRest);

            if (below != null)
            {
                var fromBelow = new List<VirtualStateTransition> { AnimatorBuilder.Transition(back, ImmutableList.Create(Mirror(AnimatorConditionMode.Less, lo))) };
                fromBelow.AddRange(plan.Leave.Select(l => AnimatorBuilder.Transition(back, ImmutableList.CreateRange(l))));
                if (above != null) fromBelow.Add(AnimatorBuilder.Transition(above, ImmutableList.Create(Mirror(AnimatorConditionMode.Greater, hi))));
                below.Transitions = ImmutableList.CreateRange(fromBelow);
            }
            if (above != null)
            {
                var fromAbove = new List<VirtualStateTransition> { AnimatorBuilder.Transition(back, ImmutableList.Create(Mirror(AnimatorConditionMode.Less, below != null ? lo : hi))) };
                fromAbove.AddRange(plan.Leave.Select(l => AnimatorBuilder.Transition(back, ImmutableList.CreateRange(l))));
                if (below != null) fromAbove.Add(AnimatorBuilder.Transition(below, ImmutableList.Create(Mirror(AnimatorConditionMode.Less, hi))));
                above.Transitions = ImmutableList.CreateRange(fromAbove);
            }
        }
    }
}
#endif
