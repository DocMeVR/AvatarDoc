using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    public class TriggerBuildTests
    {
        TestAvatar avatar;
        AvatarDoc menu;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            menu = avatar.AddMenu();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
        }

        MenuEntry Toggle(string name, GameObject target)
        {
            var entry = menu.AddEntry(EntryKind.Toggle, name);
            entry.objects.Add(new ObjectTarget { target = target });
            return entry;
        }

        MenuEntry Trigger(string name)
        {
            var entry = menu.AddEntry(EntryKind.Trigger, name);
            entry.objects.Add(new ObjectTarget { target = avatar.Glasses });
            return entry;
        }

        MenuEntry Link(string name, string sourceShape)
        {
            var entry = menu.AddEntry(EntryKind.Trigger, name);
            entry.source = TriggerSource.BlendShape;
            entry.sourceRenderer = avatar.Body;
            entry.sourceShape = sourceShape;
            return entry;
        }

        static AnimatorController FxOf(TestAvatar a) => (AnimatorController)a.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
        static AnimatorControllerLayer Layer(AnimatorController fx, string name) => fx.layers.Single(l => l.name == name);
        static AnimatorState State(AnimatorControllerLayer layer, string name) => layer.stateMachine.states.Single(s => s.state.name == name).state;

        static float FloatValue(Motion motion, string path, System.Type type, string property)
        {
            var curve = AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve(path, type, property));
            Assert.That(curve, Is.Not.Null, path + " " + property);
            return curve.Evaluate(0f);
        }

        static AnimationCurve CurveOf(Motion motion, string path, System.Type type, string property)
        {
            return AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve(path, type, property));
        }

        /// <summary>A blend shape trigger's hidden copy of its shape in one clip.</summary>
        static AnimationCurve MirrorOf(Motion motion, string trigger) => CurveOf(motion, "", typeof(Animator), "AD/Shape/" + trigger);

        /// <summary>The blend tree a Smooth blend shape trigger's Near state holds, or the clip a Switch trigger's On state holds.</summary>
        static Motion Driven(AnimatorController fx, string trigger, string state = "Near") => State(Layer(fx, "AD: " + trigger + " (shape)"), state).motion;

        /// <summary>The value one child of a driven tree writes, by the weight of the shape it sits at.</summary>
        static float At(Motion tree, float threshold, string path, System.Type type, string property)
            => CurveOf(((BlendTree)tree).children.Single(c => Mathf.Approximately(c.threshold, threshold)).motion, path, type, property).Evaluate(0f);

        /// <summary>Model, plan, defaults, toggle layers, then trigger layers, inside an NDMF context; returns the committed FX and captured errors.</summary>
        AnimatorController BuildWithTriggers(out List<ErrorContext> errors, out List<TriggerPlan> triggerPlans)
        {
            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var virtualFx);
            var existing = ParametersWriter.ExistingParameters(avatar.Descriptor.expressionParameters, virtualFx);
            ParametersWriter.AddProvided(existing, ParameterInfo.ForContext(context).GetParametersForObject(avatar.Root));
            var plan = ParameterPlanner.Plan(tree, existing);
            var (plans, planMessages) = TriggerPlanner.Plan(tree, plan, existing);
            triggerPlans = plans;
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            var pins = new PinLayerState();
            var captured = ErrorReport.CaptureErrors(() =>
            {
                L.Report(planMessages);
                L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState));
                L.Report(TriggerBuilder.Build(asc, tree, plans, baseState));
                L.Report(LinkLayerBuilder.Build(asc, tree, plans, baseState, avatar.Descriptor, pins.Names, pins));
                L.Report(ClipRewriter.Rewrite(asc, pins));
            });
            context.DeactivateAllExtensionContexts();
            errors = captured;
            return FxOf(avatar);
        }

        [Test]
        public void ToggleSourceTriggerFollowsTheTogglesParameter()
        {
            var hat = Toggle("Hat", avatar.Hat);
            var ears = Trigger("Ears");
            ears.source = TriggerSource.Toggle;
            ears.sourceToggleId = hat.id;

            var fx = BuildWithTriggers(out var errors, out _);

            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: Ears");
            var off = State(layer, "Off");
            var on = State(layer, "On");
            Assert.That(layer.stateMachine.defaultState, Is.SameAs(off));
            Assert.That(FloatValue(on.motion, "Glasses", typeof(GameObject), "m_IsActive"), Is.EqualTo(1f));
            Assert.That(FloatValue(off.motion, "Glasses", typeof(GameObject), "m_IsActive"), Is.EqualTo(0f));
            var toOn = off.transitions.Single();
            Assert.That(toOn.conditions.Single().parameter, Is.EqualTo("AD/Hat"));
            Assert.That(toOn.conditions.Single().mode, Is.EqualTo(AnimatorConditionMode.If));
            Assert.That(on.transitions.Single().conditions.Single().mode, Is.EqualTo(AnimatorConditionMode.IfNot));
            Assert.That(fx.parameters.Count(p => p.name == "AD/Hat"), Is.EqualTo(1));
        }

        [Test]
        public void ParameterSourceTriggerAddsTheAnimatorParameter()
        {
            var tongue = Trigger("Tongue");
            tongue.source = TriggerSource.Parameter;
            tongue.sourceParameter = "GestureRight";
            tongue.condition = ParameterCondition.Equals;
            tongue.conditionValue = 5;

            var fx = BuildWithTriggers(out var errors, out _);

            Assert.That(errors, Is.Empty);
            var parameter = fx.parameters.Single(p => p.name == "GestureRight");
            Assert.That(parameter.type, Is.EqualTo(AnimatorControllerParameterType.Int));
            var layer = Layer(fx, "AD: Tongue");
            var toOn = State(layer, "Off").transitions.Single();
            Assert.That(toOn.conditions.Single().mode, Is.EqualTo(AnimatorConditionMode.Equals));
            Assert.That(toOn.conditions.Single().threshold, Is.EqualTo(5f));
            Assert.That(avatar.Descriptor.expressionParameters.parameters.Any(p => p.name == "GestureRight"), Is.False, "triggers never add expression parameters");
        }

        [Test]
        public void ExpressionOnlyParameterIsAddedToTheFx()
        {
            avatar.ExpressionParameters.parameters = avatar.ExpressionParameters.parameters
                .Concat(new[] { new VRCExpressionParameters.Parameter { name = "Mood", valueType = VRCExpressionParameters.ValueType.Int } })
                .ToArray();
            var mood = Trigger("Mood two");
            mood.source = TriggerSource.Parameter;
            mood.sourceParameter = "Mood";
            mood.condition = ParameterCondition.Equals;
            mood.conditionValue = 2;

            var fx = BuildWithTriggers(out var errors, out _);

            Assert.That(errors, Is.Empty);
            var parameter = fx.parameters.Single(p => p.name == "Mood");
            Assert.That(parameter.type, Is.EqualTo(AnimatorControllerParameterType.Int));
            var toOn = State(Layer(fx, "AD: Mood two"), "Off").transitions.Single();
            Assert.That(toOn.conditions.Single().parameter, Is.EqualTo("Mood"));
            Assert.That(toOn.conditions.Single().threshold, Is.EqualTo(2f));
        }

        [Test]
        public void FloatNotEqualsTriggerHasTwoOnTransitions()
        {
            var t = Trigger("T");
            t.source = TriggerSource.Parameter;
            t.sourceParameter = "Voice";
            t.condition = ParameterCondition.NotEquals;
            t.conditionValue = 0.5f;

            var fx = BuildWithTriggers(out var errors, out _);

            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: T");
            Assert.That(State(layer, "Off").transitions.Length, Is.EqualTo(2));
            var toOff = State(layer, "On").transitions.Single();
            Assert.That(toOff.conditions.Length, Is.EqualTo(2));
        }

        [Test]
        public void ExistingAnimatorParameterIsNotRetyped()
        {
            avatar.Fx.AddParameter("GestureRight", AnimatorControllerParameterType.Float);
            var t = Trigger("T");
            t.source = TriggerSource.Parameter;
            t.sourceParameter = "GestureRight";
            t.condition = ParameterCondition.Greater;
            t.conditionValue = 2;

            var fx = BuildWithTriggers(out var errors, out var plans);

            Assert.That(errors, Is.Empty);
            Assert.That(plans.Single().Condition.Type, Is.EqualTo(PlannedType.Float), "planned from the animator, not from the built-in table");
            Assert.That(fx.parameters.Single(p => p.name == "GestureRight").type, Is.EqualTo(AnimatorControllerParameterType.Float), "the animator's own declaration stays");
        }

        [Test]
        public void UnknownParameterWarningIsReported()
        {
            var t = Trigger("T");
            t.source = TriggerSource.Parameter;
            t.sourceParameter = "Custom/Thing";
            t.condition = ParameterCondition.IsTrue;

            BuildWithTriggers(out var errors, out _);

            Assert.That(errors.Any(e => e.TheError is SimpleError s && s.TitleKey == "ad.warn.parameter_type_unknown"), Is.True);
        }

        [Test]
        public void FollowEffectScalesTheSourceCurve()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var link = Link("Blush link", "Blush");
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Follow, multiplier = 0.5f });

            var fx = BuildWithTriggers(out var errors, out _);

            Assert.That(errors, Is.Empty);
            var smile = State(Layer(fx, "Base"), "Smile");
            var near = Driven(fx, "Blush link");
            Assert.That(At(near, 0f, "Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"), Is.EqualTo(0f));
            Assert.That(At(near, 100f, "Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"), Is.EqualTo(50f), "half the source weight, now in the trigger's own layer");
            Assert.That(MirrorOf(smile.motion, "Blush link").Evaluate(1f), Is.EqualTo(100f), "the clip carries the hidden copy of the source");
            Assert.That(CurveOf(smile.motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), Is.Not.Null, "Add keeps the source");
        }

        [Test]
        public void FixedAndObjectEffectsStepAtTheThreshold()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var link = Link("Blush link", "Blush");
            link.linkStyle = BlendShapeLinkStyle.Switch;
            link.sourceThreshold = 50f;
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Fixed, weight = 80f });
            link.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = true });
            link.materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 1, material = avatar.MaterialB });

            var fx = BuildWithTriggers(out var errors, out _);

            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: Blush link (shape)");
            var off = State(layer, "Off");
            var on = State(layer, "On");
            Assert.That(CurveOf(on.motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big").Evaluate(0f), Is.EqualTo(80f));
            Assert.That(CurveOf(on.motion, "Hat", typeof(GameObject), "m_IsActive").Evaluate(0f), Is.EqualTo(1f));
            Assert.That(CurveOf(off.motion, "Hat", typeof(GameObject), "m_IsActive").Evaluate(0f), Is.EqualTo(0f));
            Assert.That(AnimationUtility.GetObjectReferenceCurve((AnimationClip)off.motion, EditorCurveBinding.PPtrCurve("Body", typeof(SkinnedMeshRenderer), "m_Materials.Array.data[1]"))[0].value, Is.SameAs(avatar.MaterialA));
            Assert.That(AnimationUtility.GetObjectReferenceCurve((AnimationClip)on.motion, EditorCurveBinding.PPtrCurve("Body", typeof(SkinnedMeshRenderer), "m_Materials.Array.data[1]"))[0].value, Is.SameAs(avatar.MaterialB));
            var enter = off.transitions.Single().conditions.Single();
            Assert.That(enter.mode, Is.EqualTo(AnimatorConditionMode.Greater), "stepped, not interpolated: the whole trigger switches at the threshold");
            Assert.That(enter.threshold, Is.EqualTo(50f).Within(0.01f));
        }

        [Test]
        public void ReplaceRemovesTheSourceCurve()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var link = Link("Blush link", "Blush");
            link.linkMode = BlendShapeLinkMode.Replace;
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Follow, multiplier = 1f });

            var fx = BuildWithTriggers(out _, out _);

            var smile = State(Layer(fx, "Base"), "Smile");
            Assert.That(CurveOf(smile.motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), Is.Null);
            Assert.That(MirrorOf(smile.motion, "Blush link").Evaluate(1f), Is.EqualTo(100f), "the hidden copy stays where the source was");
            Assert.That(At(Driven(fx, "Blush link"), 100f, "Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"), Is.EqualTo(100f));
        }

        [Test]
        public void ReplaceKeepsTheSourceWhenNothingWasWritten()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var link = Link("Blush link", "Blush");
            link.linkMode = BlendShapeLinkMode.Replace;
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Nope", mode = ShapeEffectMode.Follow, multiplier = 1f });

            var fx = BuildWithTriggers(out var errors, out _);

            var smile = State(Layer(fx, "Base"), "Smile");
            Assert.That(CurveOf(smile.motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), Is.Not.Null, "nothing was written, so the source curve stays");
            Assert.That(errors.Any(e => e.TheError is SimpleError s && s.TitleKey == "ad.warn.replace_kept_source"), Is.True);
        }

        [Test]
        public void ReplaceWarningAgreesWithTheRemoval()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var linkA = Link("Blush follow", "Blush");
            linkA.linkMode = BlendShapeLinkMode.Replace;
            linkA.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Follow, multiplier = 1f });
            var linkB = Link("Blush missing", "Blush");
            linkB.linkMode = BlendShapeLinkMode.Replace;
            linkB.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Missing", mode = ShapeEffectMode.Follow, multiplier = 1f });

            var fx = BuildWithTriggers(out var errors, out _);

            var smile = State(Layer(fx, "Base"), "Smile");
            Assert.That(CurveOf(smile.motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), Is.Null, "link A's removal deletes the source");
            Assert.That(At(Driven(fx, "Blush follow"), 100f, "Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"), Is.EqualTo(100f));
            Assert.That(errors.Any(e => e.TheError is SimpleError s && s.TitleKey == "ad.warn.replace_kept_source"), Is.False);
        }

        [Test]
        public void ReplaceOnOneLinkKeepsTheSourceForLaterLinks()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var first = Link("Blush replace", "Blush");
            first.linkMode = BlendShapeLinkMode.Replace;
            first.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Follow, multiplier = 1f });
            var second = Link("Blush hat", "Blush");
            second.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = true });

            var fx = BuildWithTriggers(out var errors, out _);

            var smile = State(Layer(fx, "Base"), "Smile");
            Assert.That(MirrorOf(smile.motion, "Blush replace"), Is.Not.Null, "the first link still ran");
            Assert.That(At(Driven(fx, "Blush replace"), 100f, "Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"), Is.EqualTo(100f));
            var hat = MirrorOf(smile.motion, "Blush hat");
            Assert.That(hat, Is.Not.Null, "the second link found the source too");
            Assert.That(hat.Evaluate(0f), Is.EqualTo(0f));
            Assert.That(hat.Evaluate(1f), Is.EqualTo(100f));
            Assert.That(CurveOf(Driven(fx, "Blush hat"), "Hat", typeof(GameObject), "m_IsActive").Evaluate(0f), Is.EqualTo(1f));
            Assert.That(CurveOf(smile.motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), Is.Null, "Replace still drops the source");
            Assert.That(errors.Any(e => e.TheError is SimpleError s && s.TitleKey == "ad.warn.trigger_source_missing"), Is.False);
        }

        [Test]
        public void BlendTreeChildrenAreRewritten()
        {
            avatar.AddFxBlendTree("Tracking", TestAvatar.ShapeClip("Track", "Body", "Blush", 0f, 100f));
            var link = Link("Blush link", "Blush");
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Follow, multiplier = 1f });

            var fx = BuildWithTriggers(out var errors, out _);

            Assert.That(errors, Is.Empty);
            var tree = (BlendTree)State(Layer(fx, "Base"), "Tracking").motion;
            Assert.That(MirrorOf(tree.children[0].motion, "Blush link").Evaluate(1f), Is.EqualTo(100f), "a blend tree's own clips carry the hidden copy too");
        }

        [Test]
        public void OwnToggleClipsAreRewrittenToo()
        {
            var blush = menu.AddEntry(EntryKind.Toggle, "Blush");
            blush.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            var link = Link("Blush link", "Blush");
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Follow, multiplier = 1f });

            var fx = BuildWithTriggers(out var errors, out _);

            Assert.That(errors, Is.Empty);
            var on = State(Layer(fx, "AD: Blush"), "On");
            Assert.That(MirrorOf(on.motion, "Blush link").Evaluate(0f), Is.EqualTo(100f), "AvatarDoc's own toggle clip carries the hidden copy like any other");
        }

        [Test]
        public void ExistingEffectCurveIsOverwrittenWithAMessage()
        {
            var clip = TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"), AnimationCurve.Constant(0f, 1f, 7f));
            avatar.AddFxClip("Smile", clip);
            var link = Link("Blush link", "Blush");
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Follow, multiplier = 1f });

            var fx = BuildWithTriggers(out var errors, out _);

            Assert.That(errors.Any(e => e.TheError is SimpleError s && s.TitleKey == "ad.info.curve_overwritten"), Is.True, "the clip also moves Eye_Big, which the trigger's layer wins over while it is on");
            Assert.That(CurveOf(State(Layer(fx, "Base"), "Smile").motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big").Evaluate(1f), Is.EqualTo(7f), "the clip's own curve is left as it was");
            Assert.That(At(Driven(fx, "Blush link"), 100f, "Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"), Is.EqualTo(100f));
        }

        [Test]
        public void ScaleAndSteppedHelpers()
        {
            Assert.That(new[] { 0f, 100f, 40f }.Select(w => EffectMath.FollowWeight(w, 2f)), Is.EqualTo(new[] { 0f, 100f, 80f }), "a Follow row scales the source and is clamped to 100");
            Assert.That(new[] { 0f, 49.9f, 50f, 100f }.Select(w => EffectMath.LinkAbove(w, 50f)), Is.EqualTo(new[] { false, false, true, true }), "a switch steps at the threshold");
            Assert.That(new[] { 0f, 20f, 50f, 80f, 100f }.Select(w => EffectMath.SmoothFraction(w, 20f, 80f)), Is.EqualTo(new[] { 0f, 0f, 0.5f, 1f, 1f }), "a blend is a straight line from Start to Full");
        }

        [Test]
        public void SelfReferencingEffectIsIgnoredInAddMode()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var link = Link("Blush link", "Blush");
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", mode = ShapeEffectMode.Follow, multiplier = 0.5f });
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Follow, multiplier = 1f });

            var fx = BuildWithTriggers(out var errors, out _);

            var smile = State(Layer(fx, "Base"), "Smile");
            var blush = CurveOf(smile.motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush");
            Assert.That(blush.Evaluate(0f), Is.EqualTo(0f));
            Assert.That(blush.Evaluate(1f), Is.EqualTo(100f));
            var near = (BlendTree)Driven(fx, "Blush link");
            Assert.That(near.children.All(c => CurveOf(c.motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush") == null), Is.True, "the trigger's own layer never writes its own shape");
            Assert.That(At(near, 100f, "Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"), Is.EqualTo(100f));
            Assert.That(errors.Any(e => e.TheError is SimpleError s && s.TitleKey == "ad.info.curve_overwritten"), Is.False);
            Assert.That(errors.Count(e => e.TheError is SimpleError s && s.TitleKey == "ad.warn.link_effect_is_source"), Is.EqualTo(1));
        }

        [Test]
        public void SelfReferencingEffectIsIgnoredInReplaceMode()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var link = Link("Blush link", "Blush");
            link.linkMode = BlendShapeLinkMode.Replace;
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", mode = ShapeEffectMode.Follow, multiplier = 0.5f });
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Follow, multiplier = 1f });

            var fx = BuildWithTriggers(out var errors, out _);

            var smile = State(Layer(fx, "Base"), "Smile");
            Assert.That(CurveOf(smile.motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), Is.Null);
            Assert.That(MirrorOf(smile.motion, "Blush link"), Is.Not.Null);
            Assert.That(At(Driven(fx, "Blush link"), 100f, "Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"), Is.EqualTo(100f));
            Assert.That(errors.Any(e => e.TheError is SimpleError s && s.TitleKey == "ad.info.curve_overwritten"), Is.False);
            Assert.That(errors.Count(e => e.TheError is SimpleError s && s.TitleKey == "ad.warn.link_effect_is_source"), Is.EqualTo(1));
        }

        [Test]
        public void LinkDefaultFollowsTheSourceWeight()
        {
            avatar.Body.SetBlendShapeWeight(0, 60f);
            var link = Link("Blush link", "Blush");
            link.sourceThreshold = 50f;
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Follow, multiplier = 0.5f });
            link.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = true });
            link.materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 1, material = avatar.MaterialB });
            avatar.Hat.SetActive(false);
            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });
            var plan = ParameterPlanner.Plan(tree, new Dictionary<string, ExistingParameter>());
            var (plans, _) = TriggerPlanner.Plan(tree, plan, new Dictionary<string, ExistingParameter>());
            var baseState = BaseState.Capture(tree);

            DefaultsApplier.Apply(tree, plan, baseState, plans);

            Assert.That(avatar.Body.GetBlendShapeWeight(1), Is.EqualTo(30f));
            Assert.That(avatar.Hat.activeSelf, Is.True);
            Assert.That(avatar.Body.sharedMaterials[1], Is.SameAs(avatar.MaterialB));
        }

        [Test]
        public void LinkDefaultUsesThePendingToggleValue()
        {
            var toggle = menu.AddEntry(EntryKind.Toggle, "Blush");
            toggle.defaultOn = true;
            toggle.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 80f });
            var link = Link("Blush link", "Blush");
            link.sourceThreshold = 50f;
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Fixed, weight = 40f });
            avatar.Body.SetBlendShapeWeight(0, 0f);
            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });
            var plan = ParameterPlanner.Plan(tree, new Dictionary<string, ExistingParameter>());
            var (plans, _) = TriggerPlanner.Plan(tree, plan, new Dictionary<string, ExistingParameter>());
            var baseState = BaseState.Capture(tree);

            DefaultsApplier.Apply(tree, plan, baseState, plans);

            Assert.That(avatar.Body.GetBlendShapeWeight(1), Is.EqualTo(32f).Within(0.001f), "the pending toggle value (80) is 80% of the way from Start (0) to Full (100), not the stale scene weight (0)");
        }

        [Test]
        public void ConditionTriggersLeaveTheSceneAtBase()
        {
            var t = Trigger("T");
            t.source = TriggerSource.Parameter;
            t.sourceParameter = "AFK";
            avatar.Glasses.SetActive(false);
            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });
            var plan = ParameterPlanner.Plan(tree, new Dictionary<string, ExistingParameter>());
            var (plans, _) = TriggerPlanner.Plan(tree, plan, new Dictionary<string, ExistingParameter>());

            DefaultsApplier.Apply(tree, plan, BaseState.Capture(tree), plans);

            Assert.That(avatar.Glasses.activeSelf, Is.False);
        }

        [Test]
        public void FullBuildRunsTriggersAndLinks()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var hat = Toggle("Hat", avatar.Hat);
            var ears = Trigger("Ears");
            ears.source = TriggerSource.Toggle;
            ears.sourceToggleId = hat.id;
            var link = Link("Blush link", "Blush");
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Follow, multiplier = 1f });
            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();

            var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            errors.AddRange(ErrorReport.CaptureErrors(() => AvatarDoctorPlugin.FinishAfterOtherTools(context)));

            Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error), Is.Empty);
            var fx = FxOf(avatar);
            Assert.That(fx.layers.Select(l => l.name), Is.EquivalentTo(new[] { "Base", "AD: Hat", "AD: Ears", "AD: Blush link (shape)" }), "a blend shape trigger has a layer of its own now");
            Assert.That(MirrorOf(State(Layer(fx, "Base"), "Smile").motion, "Blush link").Evaluate(1f), Is.EqualTo(100f), "the final pass copied the shape into the clip");
            Assert.That(At(Driven(fx, "Blush link"), 100f, "Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"), Is.EqualTo(100f));
            Assert.That(avatar.Descriptor.expressionParameters.parameters.Select(p => p.name), Is.EqualTo(new[] { "Existing", "AD/Hat" }), "triggers add no expression parameters");
            Assert.That(avatar.Descriptor.expressionsMenu.controls.Any(c => c.name == "Ears" || c.name == "Blush link"), Is.False, "triggers have no menu buttons");
        }

        [Test]
        public void PinTargetWritesNothingWhenOff()
        {
            var toggle = MenuEntry.Create(EntryKind.Toggle, "Mute cringe");
            toggle.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 0f, mode = ShapeEffectMode.Pin });
            toggle.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 30f });
            menu.entries.Add(toggle);
            avatar.Body.SetBlendShapeWeight(0, 60f);

            var fx = BuildWithTriggers(out var errors, out _);

            Assert.That(errors, Is.Empty);
            var main = Layer(fx, "AD: Mute cringe");
            var off = CurveOf(State(main, "Off").motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush");
            var offEye = CurveOf(State(main, "Off").motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big");
            var on = CurveOf(State(main, "On").motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush");
            Assert.That(off, Is.Null, "a Pin target is released when the toggle is off");
            Assert.That(offEye, Is.Not.Null, "a Fixed target still restores the scene value");
            Assert.That(on, Is.Null, "the main layer leaves Pin targets to the pin layer");

            var pin = Layer(fx, "AD: Mute cringe (pin)");
            Assert.That(CurveOf(State(pin, "Off").motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), Is.Null);
            Assert.That(CurveOf(State(pin, "On").motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush").Evaluate(0f), Is.EqualTo(0f));
            Assert.That(State(pin, "Off").writeDefaultValues, Is.False);
            Assert.That(State(pin, "On").writeDefaultValues, Is.False);
        }

        MenuEntry PinToggle(string name, string shape = "Blush")
        {
            var toggle = menu.AddEntry(EntryKind.Toggle, name);
            toggle.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = shape, weight = 0f, mode = ShapeEffectMode.Pin });
            return toggle;
        }

        static string[] ConditionsOf(AnimatorStateTransition[] transitions)
        {
            return transitions
                .Select(t => string.Join(" & ", t.conditions.Select(c => c.parameter + " " + c.mode + " " + c.threshold)))
                .ToArray();
        }

        static string[] DestinationsOf(AnimatorStateTransition[] transitions)
        {
            return transitions.Select(t => t.destinationState.name).ToArray();
        }

        [Test]
        public void GroupMembersPinsAreReleasedInOtherStates()
        {
            var a = PinToggle("A");
            a.exclusiveGroup = "Outfits";
            var b = PinToggle("B", "Eye_Big");
            b.exclusiveGroup = "Outfits";
            b.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 30f });

            var fx = BuildWithTriggers(out var errors, out _);

            Assert.That(errors, Is.Empty);
            var pin = Layer(fx, "AD: Outfits (pin)");
            Assert.That(CurveOf(State(pin, "A").motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), Is.Not.Null);
            Assert.That(CurveOf(State(pin, "A").motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"), Is.Null);
            Assert.That(CurveOf(State(pin, "B").motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"), Is.Not.Null);
            Assert.That(CurveOf(State(pin, "B").motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), Is.Null, "a member's pin state writes only its own pins");
            Assert.That(CurveOf(State(pin, "Off").motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), Is.Null);
            Assert.That(CurveOf(State(pin, "Off").motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"), Is.Null);
            foreach (var name in new[] { "Off", "A", "B" })
                Assert.That(State(pin, name).writeDefaultValues, Is.False, name);

            var main = Layer(fx, "AD: Outfits");
            foreach (var name in new[] { "Off", "A", "B" })
                Assert.That(CurveOf(State(main, name).motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"), Is.Null, name);
        }

        [Test]
        public void ConditionTriggerPinsUseAPinLayer()
        {
            var quiet = menu.AddEntry(EntryKind.Trigger, "Quiet");
            quiet.source = TriggerSource.Parameter;
            quiet.sourceParameter = "AFK";
            quiet.condition = ParameterCondition.IsTrue;
            quiet.objects.Add(new ObjectTarget { target = avatar.Glasses });
            quiet.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 0f, mode = ShapeEffectMode.Pin });

            var fx = BuildWithTriggers(out var errors, out _);

            Assert.That(errors, Is.Empty);
            var main = Layer(fx, "AD: Quiet");
            var pin = Layer(fx, "AD: Quiet (pin)");
            Assert.That(CurveOf(State(pin, "On").motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush").Evaluate(0f), Is.EqualTo(0f));
            Assert.That(CurveOf(State(pin, "Off").motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), Is.Null);
            Assert.That(State(pin, "Off").writeDefaultValues, Is.False);
            Assert.That(State(pin, "On").writeDefaultValues, Is.False);
            Assert.That(ConditionsOf(State(pin, "Off").transitions), Is.EqualTo(ConditionsOf(State(main, "Off").transitions)));
            Assert.That(ConditionsOf(State(pin, "On").transitions), Is.EqualTo(ConditionsOf(State(main, "On").transitions)));
        }

        [Test]
        public void GroupPinLayerSwitchesMembersDirectly()
        {
            var a = PinToggle("A");
            a.exclusiveGroup = "Outfits";
            var b = PinToggle("B", "Eye_Big");
            b.exclusiveGroup = "Outfits";

            var fx = BuildWithTriggers(out var errors, out _);

            Assert.That(errors, Is.Empty);
            var main = Layer(fx, "AD: Outfits").stateMachine.anyStateTransitions;
            var pin = Layer(fx, "AD: Outfits (pin)").stateMachine.anyStateTransitions;
            Assert.That(DestinationsOf(pin), Is.EqualTo(new[] { "Off", "A", "B" }));
            Assert.That(DestinationsOf(main), Is.EqualTo(new[] { "Off", "A", "B" }));
            Assert.That(ConditionsOf(pin), Is.EqualTo(ConditionsOf(main)));
            foreach (var name in new[] { "Off", "A", "B" })
                Assert.That(State(Layer(fx, "AD: Outfits (pin)"), name).transitions, Is.Empty, name);
        }

        [Test]
        public void GroupWithOnePinnedMemberReleasesOnUnpinnedMembers()
        {
            var a = PinToggle("A");
            a.exclusiveGroup = "Outfits";
            var b = menu.AddEntry(EntryKind.Toggle, "B");
            b.exclusiveGroup = "Outfits";
            b.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 30f });

            var fx = BuildWithTriggers(out var errors, out _);

            Assert.That(errors, Is.Empty);
            var main = Layer(fx, "AD: Outfits").stateMachine.anyStateTransitions;
            var pin = Layer(fx, "AD: Outfits (pin)").stateMachine.anyStateTransitions;
            Assert.That(DestinationsOf(pin), Is.EqualTo(new[] { "Off", "A" }), "the pin layer has no state for an unpinned member");
            var toA = main.Single(t => t.destinationState.name == "A").conditions.Single();
            var pinOff = pin.Single(t => t.destinationState.name == "Off").conditions;
            Assert.That(pinOff.Length, Is.EqualTo(1), "the pin layer releases as soon as A is not selected");
            Assert.That(pinOff[0].mode, Is.EqualTo(AnimatorConditionMode.NotEqual));
            Assert.That(pinOff[0].parameter, Is.EqualTo(toA.parameter));
            Assert.That(pinOff[0].threshold, Is.EqualTo(toA.threshold));
            Assert.That(main.Single(t => t.destinationState.name == "Off").conditions.Length, Is.EqualTo(2), "the main layer still checks every member");
        }

        [Test]
        public void MainLayersKeepTheAvatarWriteDefaults()
        {
            PinToggle("Mute").objects.Add(new ObjectTarget { target = avatar.Hat });

            var fx = BuildWithTriggers(out var errors, out _);

            Assert.That(errors, Is.Empty);
            Assert.That(State(Layer(fx, "AD: Mute"), "On").writeDefaultValues, Is.False, "the avatar has Write Defaults off");
            Assert.That(State(Layer(fx, "AD: Mute (pin)"), "On").writeDefaultValues, Is.False);

            avatar.Dispose();
            avatar = TestAvatar.Create(writeDefaultsOff: false);
            menu = avatar.AddMenu();
            PinToggle("Mute").objects.Add(new ObjectTarget { target = avatar.Hat });

            fx = BuildWithTriggers(out errors, out _);

            Assert.That(errors, Is.Empty);
            Assert.That(State(Layer(fx, "AD: Mute"), "On").writeDefaultValues, Is.True, "the main layer follows the avatar");
            Assert.That(State(Layer(fx, "AD: Mute (pin)"), "On").writeDefaultValues, Is.False, "a pin layer is always Write Defaults off");
            Assert.That(State(Layer(fx, "AD: Mute (pin)"), "Off").writeDefaultValues, Is.False);
        }

        [Test]
        public void PinOnlyControlsGetNoMainLayer()
        {
            PinToggle("Mute");

            var fx = BuildWithTriggers(out var errors, out _);

            Assert.That(errors, Is.Empty);
            Assert.That(fx.layers.Any(l => l.name == "AD: Mute"), Is.False, "the main layer would write nothing");
            Assert.That(fx.layers.Count(l => l.name == "AD: Mute (pin)"), Is.EqualTo(1));
        }

        [Test]
        public void WriteDefaultsDecisionIgnoresPinLayers()
        {
            PinToggle("Mute").objects.Add(new ObjectTarget { target = avatar.Hat });
            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });
            var plan = ParameterPlanner.Plan(tree, new Dictionary<string, ExistingParameter>());
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            try
            {
                asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var virtualFx);
                AnimatorBuilder.Build(asc, tree, plan, BaseState.Capture(tree));

                Assert.That(virtualFx.Layers.Any(l => l.Name.EndsWith(" (pin)")), Is.True);
                Assert.That(AnimatorBuilder.DecideWriteDefaults(virtualFx), Is.False, "the avatar has Write Defaults off");
            }
            finally
            {
                context.DeactivateAllExtensionContexts();
            }
        }

        [Test]
        public void PinDefaultsFollowTheControlDefault()
        {
            avatar.Body.SetBlendShapeWeight(0, 60f);
            var mute = PinToggle("Mute");
            mute.defaultOn = true;
            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });
            var plan = ParameterPlanner.Plan(tree, new Dictionary<string, ExistingParameter>());

            DefaultsApplier.Apply(tree, plan, BaseState.Capture(tree));

            Assert.That(avatar.Body.GetBlendShapeWeight(0), Is.EqualTo(0f), "a pin writes its weight while the control starts on");

            mute.defaultOn = false;
            avatar.Body.SetBlendShapeWeight(0, 60f);
            var offTree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });
            var offPlan = ParameterPlanner.Plan(offTree, new Dictionary<string, ExistingParameter>());

            DefaultsApplier.Apply(offTree, offPlan, BaseState.Capture(offTree));

            Assert.That(avatar.Body.GetBlendShapeWeight(0), Is.EqualTo(60f), "a released pin leaves the scene weight alone");
        }

        [Test]
        public void OverrideDefaultPinsTheScene()
        {
            avatar.Body.SetBlendShapeWeight(0, 60f);
            var rule = MenuEntry.Create(EntryKind.Override, "Silence blush");
            rule.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 0f });
            menu.entries.Add(rule);
            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });
            var plan = ParameterPlanner.Plan(tree, new Dictionary<string, ExistingParameter>());

            DefaultsApplier.Apply(tree, plan, BaseState.Capture(tree));

            Assert.That(avatar.Body.GetBlendShapeWeight(0), Is.EqualTo(0f));
        }
    }
}
