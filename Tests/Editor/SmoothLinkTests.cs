#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Blend shape triggers follow their shape through a hidden copy of its weight (an animated
    /// animator parameter) into a layer of their own, smoothly or as a switch, and they and Fade contacts take
    /// "+ And" requirements.
    /// </summary>
    public class SmoothLinkTests
    {
        TestAvatar avatar;
        AvatarDoc menu;
        AnimatorController gesture;

        [SetUp]
        public void SetUp()
        {
            GeneratedAssets.Note();
            avatar = TestAvatar.Create();
            menu = avatar.AddMenu();
            avatar.MaterialA.SetFloat("_Glossiness", 0.2f);
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            if (gesture != null) Object.DestroyImmediate(gesture);
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
            GeneratedAssets.CleanUp();
        }

        const string Mirror = "AD/Shape/Blush link";

        static AnimatorController FxOf(TestAvatar a) => (AnimatorController)a.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
        static AnimatorControllerLayer Layer(AnimatorController fx, string name) => fx.layers.Single(l => l.name == name);
        static AnimatorState State(AnimatorControllerLayer layer, string name) => layer.stateMachine.states.Single(s => s.state.name == name).state;
        static string[] StateNames(AnimatorControllerLayer layer) => layer.stateMachine.states.Select(s => s.state.name).ToArray();

        static EditorCurveBinding MirrorBinding(string parameter) => EditorCurveBinding.FloatCurve("", typeof(Animator), parameter);
        static AnimationCurve Shape(Motion motion, string shape) => AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape." + shape));
        static AnimationCurve Active(Motion motion, string path) => AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive"));
        static AnimationCurve Glossiness(Motion motion) => AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "material._Glossiness"));
        static Object Slot(Motion motion, int slot) => AnimationUtility.GetObjectReferenceCurve((AnimationClip)motion, EditorCurveBinding.PPtrCurve("Body", typeof(SkinnedMeshRenderer), "m_Materials.Array.data[" + slot + "]"))[0].value;

        MenuEntry Link(string name = "Blush link")
        {
            var entry = menu.AddEntry(EntryKind.Trigger, name);
            entry.source = TriggerSource.BlendShape;
            entry.sourceRenderer = avatar.Body;
            entry.sourceShape = "Blush";
            return entry;
        }

        static BlendShapeTarget EyeBig(float weight) => new BlendShapeTarget { renderer = null, shape = "Eye_Big", mode = ShapeEffectMode.Fixed, weight = weight };

        BlendShapeTarget Eye(float weight)
        {
            var target = EyeBig(weight);
            target.renderer = avatar.Body;
            return target;
        }

        MenuEntry HatToggle()
        {
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            return hat;
        }

        static WhenCondition OnToggle(MenuEntry toggle) => new WhenCondition { kind = WhenConditionKind.Toggle, sourceToggleId = toggle.id, sourceToggleOn = true };

        MenuEntry FadeTrigger(string name)
        {
            var entry = menu.AddEntry(EntryKind.Trigger, name);
            entry.source = TriggerSource.Contact;
            entry.contactAnchor = avatar.Hat.transform;
            entry.contactTags = new List<string> { "Hand" };
            entry.touchMode = TouchMode.FollowDistance;
            entry.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            return entry;
        }

        static WhenCondition ContactOn(Transform anchor, string tag = "Hand") => new WhenCondition { kind = WhenConditionKind.Contact, contactAnchor = anchor, contactTags = new List<string> { tag } };

        /// <summary>The whole build the way NDMF runs it: AvatarDoc's own pass, then the final pass the Optimizing phase runs after every other tool.</summary>
        AnimatorController BuildAll(out List<ErrorContext> errors, System.Action<AnimatorServicesContext, VirtualAnimatorController> betweenPasses = null)
        {
            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            if (betweenPasses != null)
            {
                var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
                asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx);
                betweenPasses(asc, fx);
                context.DeactivateAllExtensionContexts();
            }
            errors.AddRange(ErrorReport.CaptureErrors(() => AvatarDoctorPlugin.FinishAfterOtherTools(context)));
            return FxOf(avatar);
        }

        static void NoErrors(List<ErrorContext> errors)
            => Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error).Select(e => (e.TheError as SimpleError)?.TitleKey), Is.Empty);

        static bool Reported(List<ErrorContext> errors, string key) => errors.Any(e => e.TheError is SimpleError s && s.TitleKey == key);

        /// <summary>The single condition on the mirror parameter in a transition, with its mode and threshold.</summary>
        static AnimatorCondition On(AnimatorStateTransition transition, string parameter) => transition.conditions.Single(c => c.parameter == parameter);

        static string Words(AnimatorCondition c) => c.parameter + " " + c.mode + " " + c.threshold.ToString(System.Globalization.CultureInfo.InvariantCulture);

        static AnimatorStateTransition[] To(AnimatorState from, string destination) => from.transitions.Where(t => t.destinationState.name == destination).ToArray();

        // ---- Build: the mirror ---------------------------------------------------------------------------------

        [Test]
        public void TheMirrorCurveCopiesTheShapeKeyForKeyInEveryFxClip()
        {
            avatar.AddFxClip("Smile", new AnimationClip { name = "Smile" });
            var smileClip = (AnimationClip)avatar.Fx.layers[0].stateMachine.states.Single(s => s.state.name == "Smile").state.motion;
            AnimationUtility.SetEditorCurve(smileClip, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"),
                new AnimationCurve(new Keyframe(0f, 0f, 0f, 30f), new Keyframe(0.5f, 70f, 10f, -5f), new Keyframe(1f, 100f)));
            avatar.AddFxBlendTree("Tracking", TestAvatar.ShapeClip("Track", "Body", "Blush", 10f, 60f));
            Link().blendShapes.Add(Eye(80f));

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var smile = State(Layer(fx, "Base"), "Smile").motion;
            var source = Shape(smile, "Blush");
            var mirror = AnimationUtility.GetEditorCurve((AnimationClip)smile, MirrorBinding(Mirror));
            Assert.That(source, Is.Not.Null, "Add keeps the shape's own curve");
            Assert.That(mirror, Is.Not.Null, "every FX clip that moves the shape carries the hidden copy");
            Assert.That(mirror.keys.Select(k => (k.time, k.value, k.inTangent, k.outTangent)), Is.EqualTo(source.keys.Select(k => (k.time, k.value, k.inTangent, k.outTangent))), "the copy follows the shape key for key, tangents included");

            var tree = (BlendTree)State(Layer(fx, "Base"), "Tracking").motion;
            var child = tree.children[0].motion;
            Assert.That(AnimationUtility.GetEditorCurve((AnimationClip)child, MirrorBinding(Mirror)).keys.Select(k => k.value), Is.EqualTo(new[] { 10f, 60f }), "a blend tree's own clips are copied too");
        }

        [Test]
        public void TheMirrorIsAHiddenFloatWithNoSyncedBits()
        {
            avatar.Body.SetBlendShapeWeight(0, 20f);
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            Link().blendShapes.Add(Eye(80f));

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var parameter = fx.parameters.Single(p => p.name == Mirror);
            Assert.That(parameter.type, Is.EqualTo(AnimatorControllerParameterType.Float));
            Assert.That(parameter.defaultFloat, Is.EqualTo(20f), "it starts where the shape starts, so it rests with the shape before any clip moves it");
            Assert.That(avatar.Descriptor.expressionParameters.parameters.Select(p => p.name), Is.EqualTo(new[] { "Existing" }), "never an expression parameter, so it costs no synced bits");
        }

        [Test]
        public void SmoothBuildsFarAndANearTreeFromStartToFull()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var link = Link();
            link.smoothStart = 20f;
            link.smoothFull = 80f;
            link.blendShapes.Add(Eye(80f));

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Blush link (shape)");
            Assert.That(StateNames(layer), Is.EquivalentTo(new[] { "Far", "Near", "Reset", "Load" }));
            var far = State(layer, "Far");
            var near = State(layer, "Near");
            var reset = State(layer, "Reset");
            Assert.That(layer.stateMachine.defaultState, Is.SameAs(State(layer, "Load")), "the layer starts in Load, which passes to Reset and then Far");
            Assert.That(far.writeDefaultValues || near.writeDefaultValues || reset.writeDefaultValues, Is.False, "Write Defaults off, like every layer that is moved last");

            var tree = (BlendTree)near.motion;
            Assert.That(tree.blendType, Is.EqualTo(BlendTreeType.Simple1D));
            Assert.That(tree.blendParameter, Is.EqualTo(Mirror));
            Assert.That(tree.children.Select(c => c.threshold), Is.EqualTo(new[] { 20f, 80f }), "from rest at Start to the effect at Full");
            Assert.That(Shape(tree.children[0].motion, "Eye_Big").Evaluate(0f), Is.EqualTo(0f));
            Assert.That(Shape(tree.children[1].motion, "Eye_Big").Evaluate(0f), Is.EqualTo(80f));
            Assert.That(Shape(far.motion, "Eye_Big"), Is.Null, "Far writes no shape");
            Assert.That(Shape(reset.motion, "Eye_Big").Evaluate(0f), Is.EqualTo(0f), "nothing else moves Eye_Big, so the final pass rests it once, in Reset");

            var enter = far.transitions.Single();
            Assert.That(enter.destinationState, Is.SameAs(near));
            Assert.That(On(enter, Mirror).mode, Is.EqualTo(AnimatorConditionMode.Greater));
            Assert.That(On(enter, Mirror).threshold, Is.EqualTo(20f).Within(0.05f));
            var leave = near.transitions.Single();
            Assert.That(leave.destinationState, Is.SameAs(reset), "the way back passes Reset");
            Assert.That(reset.transitions.Single().destinationState, Is.SameAs(far), "which falls through to Far");
            Assert.That(On(leave, Mirror).mode, Is.EqualTo(AnimatorConditionMode.Less));
            Assert.That(On(leave, Mirror).threshold, Is.EqualTo(On(enter, Mirror).threshold), "in and out at the same point, so the two never fire together");

            var names = fx.layers.Select(l => l.name).ToList();
            Assert.That(names.Last(), Is.EqualTo("AD: Blush link (shape)"), "moved last, after every other layer");
        }

        [Test]
        public void SmoothSwapsObjectsAndMaterialsAtSwitchAt()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            avatar.Hat.SetActive(false);
            var link = Link();
            link.sourceThreshold = 50f;
            link.blendShapes.Add(Eye(80f));
            link.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = true });
            link.materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 1, material = avatar.MaterialB });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Blush link (shape)");
            Assert.That(StateNames(layer), Is.EquivalentTo(new[] { "Far", "Near", "Near on", "Reset", "Load" }));
            var far = State(layer, "Far");
            var near = State(layer, "Near");
            var nearOn = State(layer, "Near on");
            foreach (var child in ((BlendTree)near.motion).children)
            {
                Assert.That(Active(child.motion, "Hat").Evaluate(0f), Is.EqualTo(0f), "below Switch at the object stays off");
                Assert.That(Slot(child.motion, 1), Is.SameAs(avatar.MaterialA));
            }
            foreach (var child in ((BlendTree)nearOn.motion).children)
            {
                Assert.That(Active(child.motion, "Hat").Evaluate(0f), Is.EqualTo(1f), "from Switch at on the object is on");
                Assert.That(Slot(child.motion, 1), Is.SameAs(avatar.MaterialB));
            }
            Assert.That(((BlendTree)nearOn.motion).children.Select(c => Shape(c.motion, "Eye_Big").Evaluate(0f)), Is.EqualTo(((BlendTree)near.motion).children.Select(c => Shape(c.motion, "Eye_Big").Evaluate(0f))), "the shapes blend the same in both");
            Assert.That(Active(far.motion, "Hat").Evaluate(0f), Is.EqualTo(0f));

            Assert.That(On(To(near, "Near on").Single(), Mirror).threshold, Is.EqualTo(50f).Within(0.05f));
            Assert.That(On(To(near, "Near on").Single(), Mirror).mode, Is.EqualTo(AnimatorConditionMode.Greater));
            Assert.That(On(To(nearOn, "Near").Single(), Mirror).threshold, Is.EqualTo(50f).Within(0.05f));
            Assert.That(On(To(nearOn, "Near").Single(), Mirror).mode, Is.EqualTo(AnimatorConditionMode.Less));
            Assert.That(On(To(far, "Near on").Single(), Mirror).threshold, Is.EqualTo(50f).Within(0.05f), "a jump straight past Switch at goes straight to Near on");
            Assert.That(far.transitions.First().destinationState, Is.SameAs(nearOn), "checked first, so it wins when both would fire");
        }

        [Test]
        public void SwitchStyleSwitchesAtTheThreshold()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var link = Link();
            link.linkStyle = BlendShapeLinkStyle.Switch;
            link.sourceThreshold = 30f;
            link.blendShapes.Add(Eye(80f));
            link.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = true });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Blush link (shape)");
            Assert.That(StateNames(layer), Is.EquivalentTo(new[] { "Off", "On", "Reset", "Load" }));
            var off = State(layer, "Off");
            var on = State(layer, "On");
            Assert.That(on.motion, Is.InstanceOf<AnimationClip>(), "a switch needs no blend");
            Assert.That(Shape(on.motion, "Eye_Big").Evaluate(0f), Is.EqualTo(80f));
            Assert.That(Active(on.motion, "Hat").Evaluate(0f), Is.EqualTo(1f));
            var enter = off.transitions.Single();
            Assert.That(On(enter, Mirror).mode, Is.EqualTo(AnimatorConditionMode.Greater));
            Assert.That(On(enter, Mirror).threshold, Is.EqualTo(30f).Within(0.05f));
            Assert.That(On(enter, Mirror).threshold, Is.LessThan(30f), "at the threshold itself counts as on, like before");
            var leave = on.transitions.Single();
            Assert.That(On(leave, Mirror).mode, Is.EqualTo(AnimatorConditionMode.Less));
            Assert.That(On(leave, Mirror).threshold, Is.EqualTo(On(enter, Mirror).threshold));
        }

        [Test]
        public void OneRequirementJoinsTheWayIntoNearAndAddsOneWayOut()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var hat = HatToggle();
            var link = Link();
            link.blendShapes.Add(Eye(80f));
            link.when.Add(new ConditionGroup { all = { OnToggle(hat) } });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Blush link (shape)");
            var far = State(layer, "Far");
            var near = State(layer, "Near");
            var enter = far.transitions.Single();
            Assert.That(enter.conditions.Select(c => c.parameter), Is.EquivalentTo(new[] { "AD/Hat", Mirror }), "all requirements and the shape together");
            Assert.That(enter.conditions.Single(c => c.parameter == "AD/Hat").mode, Is.EqualTo(AnimatorConditionMode.If));
            var leave = near.transitions.Where(t => t.destinationState == State(layer, "Reset")).ToList();
            Assert.That(leave.Count, Is.EqualTo(2), "one way out for the requirement, one for the shape, both through Reset");
            Assert.That(leave.Any(t => t.conditions.Length == 1 && t.conditions[0].parameter == "AD/Hat" && t.conditions[0].mode == AnimatorConditionMode.IfNot), Is.True);
            Assert.That(leave.Any(t => t.conditions.Length == 1 && t.conditions[0].parameter == Mirror && t.conditions[0].mode == AnimatorConditionMode.Less), Is.True);
        }

        [Test]
        public void OnlyWhileWorksAsOneMoreRequirementOnABlendShapeTrigger()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var hat = HatToggle();
            var link = Link();
            link.blendShapes.Add(Eye(80f));
            link.gateToggleId = hat.id;

            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
            Assert.That(tree.Messages.Where(m => m.Severity == MessageSeverity.Error).Select(m => m.Key), Is.Empty, "a blend shape trigger may have Only while now");

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Blush link (shape)");
            var enter = State(layer, "Far").transitions.Single();
            Assert.That(enter.conditions.Select(c => c.parameter), Is.EquivalentTo(new[] { "AD/Hat", Mirror }));
            Assert.That(State(layer, "Near").transitions.Count(t => t.conditions.Any(c => c.parameter == "AD/Hat" && c.mode == AnimatorConditionMode.IfNot)), Is.EqualTo(1));
        }

        [Test]
        public void APropertyRowBlendsInTheNearTree()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var link = Link();
            link.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 0.9f });
            Assert.That(link.HasEffects, Is.True, "a property row alone is an effect of a blend shape trigger now");

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Blush link (shape)");
            var tree = (BlendTree)State(layer, "Near").motion;
            Assert.That(Glossiness(tree.children[0].motion).Evaluate(0f), Is.EqualTo(0.2f).Within(0.0001f), "rest at Start is the material's own value");
            Assert.That(Glossiness(tree.children[1].motion).Evaluate(0f), Is.EqualTo(0.9f).Within(0.0001f), "the row's value at Full");
            Assert.That(Glossiness(State(layer, "Far").motion), Is.Null, "Far writes nothing for the property");
            Assert.That(Glossiness(State(layer, "Reset").motion).Evaluate(0f), Is.EqualTo(0.2f).Within(0.0001f), "nothing else moves it, so Reset rests it once");
        }

        [Test]
        public void AFollowRowStillFollowsTheShapeTimesItsMultiplier()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var link = Link();
            link.smoothStart = 20f;
            link.smoothFull = 80f;
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Follow, multiplier = 2f });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var tree = (BlendTree)State(Layer(fx, "AD: Blush link (shape)"), "Near").motion;
            var thresholds = tree.children.Select(c => c.threshold).ToArray();
            var weights = tree.children.Select(c => Shape(c.motion, "Eye_Big").Evaluate(0f)).ToArray();
            for (int i = 0; i < thresholds.Length; i++)
                Assert.That(weights[i], Is.EqualTo(Mathf.Clamp(thresholds[i] * 2f, 0f, 100f)).Within(0.001f), "at " + thresholds[i] + " the row is twice the shape, up to 100");
            Assert.That(thresholds, Does.Contain(50f), "the point where twice the shape reaches 100 is a child of its own, so the blend between children stays exact");
        }

        [Test]
        public void ReplaceTakesTheShapeOutAndKeepsTheCopy()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var link = Link();
            link.linkMode = BlendShapeLinkMode.Replace;
            link.blendShapes.Add(Eye(80f));

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var smile = State(Layer(fx, "Base"), "Smile").motion;
            Assert.That(Shape(smile, "Blush"), Is.Null, "Replace takes the shape's own curve out");
            Assert.That(AnimationUtility.GetEditorCurve((AnimationClip)smile, MirrorBinding(Mirror)).keys.Select(k => k.value), Is.EqualTo(new[] { 0f, 100f }), "the trigger still follows where the shape would have been");
        }

        [Test]
        public void TheCopyReachesAClipMergedAfterAvatarDocsOwnPass()
        {
            Link().blendShapes.Add(Eye(80f));

            var fx = BuildAll(out var errors, (asc, virtualFx) =>
            {
                var clip = VirtualClip.Create("Merged smile");
                clip.SetFloatCurve(EditorCurveBinding.FloatCurve(asc.ObjectPathRemapper.GetVirtualPathForObject(avatar.Body.gameObject), typeof(SkinnedMeshRenderer), "blendShape.Blush"), AnimationCurve.Linear(0f, 0f, 1f, 100f));
                var layer = virtualFx.AddLayer(LayerPriority.Default, "Merged later");
                var state = layer.StateMachine.AddState("Smile", clip);
                layer.StateMachine.DefaultState = state;
            });

            NoErrors(errors);
            var merged = State(Layer(fx, "Merged later"), "Smile").motion;
            Assert.That(AnimationUtility.GetEditorCurve((AnimationClip)merged, MirrorBinding(Mirror)), Is.Not.Null, "the copy is made after Modular Avatar and VRCFury have merged their clips");
            var names = fx.layers.Select(l => l.name).ToList();
            Assert.That(names.IndexOf("AD: Blush link (shape)"), Is.GreaterThan(names.IndexOf("Merged later")));
        }

        /// <summary>A Gesture controller with one clip, "Gesture smile", that moves Blush. Destroyed in TearDown.</summary>
        void UseGestureSmile()
        {
            gesture = new AnimatorController { name = "Gesture" };
            gesture.AddLayer("Hands");
            var state = gesture.layers[0].stateMachine.AddState("Smile");
            state.motion = TestAvatar.ShapeClip("Gesture smile", "Body", "Blush", 0f, 100f);
            var layers = avatar.Descriptor.baseAnimationLayers;
            for (int i = 0; i < layers.Length; i++)
                if (layers[i].type == VRCAvatarDescriptor.AnimLayerType.Gesture) { layers[i].isDefault = false; layers[i].animatorController = gesture; }
            avatar.Descriptor.baseAnimationLayers = layers;
        }

        [Test]
        public void AClipOutsideFxIsNotFollowedAndTheBuildSaysSo()
        {
            UseGestureSmile();
            Link().blendShapes.Add(Eye(80f));

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            Assert.That(Reported(errors, "ad.warn.link_clips_outside_fx"), Is.True, "an animated value only reaches layers of its own controller, so a Gesture clip cannot drive the trigger");
            var built = (AnimatorController)avatar.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.Gesture).animatorController;
            var clip = (AnimationClip)built.layers[0].stateMachine.states.Single().state.motion;
            Assert.That(AnimationUtility.GetEditorCurve(clip, MirrorBinding(Mirror)), Is.Null);
            Assert.That(Shape(clip, "Blush"), Is.Not.Null, "and its own curve stays where it is");
            Assert.That(fx.layers.Any(l => l.name == "AD: Blush link (shape)"), Is.True);
        }

        [Test]
        public void FarNeverHoldsAShapeTheDescriptorBlinksWith()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            avatar.Descriptor.enableEyeLook = true;
            avatar.Descriptor.customEyeLookSettings.eyelidType = VRCAvatarDescriptor.EyelidType.Blendshapes;
            avatar.Descriptor.customEyeLookSettings.eyelidsSkinnedMesh = avatar.Body;
            avatar.Descriptor.customEyeLookSettings.eyelidsBlendshapes = new[] { avatar.Mesh.GetBlendShapeIndex("Eye_Big"), -1, -1 };
            Link().blendShapes.Add(Eye(80f));

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            Assert.That(Shape(State(Layer(fx, "AD: Blush link (shape)"), "Far").motion, "Eye_Big"), Is.Null, "VRChat's own blinking writes that shape while the trigger rests");
            Assert.That(StateNames(Layer(fx, "AD: Blush link (shape)")), Does.Not.Contain("Reset"), "and with nothing else to rest, not even a frame of reset writes it");
        }

        /// <summary>Written after the first failing run, so it never ran red; its body fails without the requirement receivers in TouchBuilder.Describe.</summary>
        [Test]
        public void AContactRequirementOnABlendShapeTriggerGetsItsReceiver()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var link = Link();
            link.blendShapes.Add(Eye(80f));
            link.when.Add(new ConditionGroup { all = { ContactOn(avatar.Glasses.transform, "Finger") } });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var receiver = avatar.Root.GetComponentsInChildren<VRCContactReceiver>(true).Single();
            Assert.That(receiver.transform.parent, Is.SameAs(avatar.Glasses.transform));
            var enter = State(Layer(fx, "AD: Blush link (shape)"), "Far").transitions.Single();
            Assert.That(enter.conditions.Select(c => c.parameter), Is.EquivalentTo(new[] { receiver.parameter, Mirror }));
        }

        /// <summary>Written after the first failing run, so it never ran red; its body fails without the smooth_range check and EffectiveSmoothFull.</summary>
        [Test]
        public void CrossedStartAndFullAreReportedAndKeptApart()
        {
            var link = Link();
            link.blendShapes.Add(Eye(80f));
            link.smoothStart = 70f;
            link.smoothFull = 40f;

            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });

            var warning = tree.Messages.Single(m => m.Key == "ad.warn.smooth_range" && m.EntryIds.Contains(link.id));
            Assert.That(warning.Severity, Is.EqualTo(MessageSeverity.Warning));
            Assert.That(link.EffectiveSmoothStart, Is.EqualTo(70f));
            Assert.That(link.EffectiveSmoothFull, Is.EqualTo(71f), "fully on just above Start");
            link.linkStyle = BlendShapeLinkStyle.Switch;
            tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
            Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.smooth_range"), Is.False, "Switch never reads the range");
        }

        // ---- Build: Fade contacts with requirements -------------------------------------------------------------

        [Test]
        public void AFadeContactWithARequirementNeedsItToComeNear()
        {
            var hat = HatToggle();
            var pat = FadeTrigger("Pat");
            pat.when.Add(new ConditionGroup { all = { ContactOn(avatar.Hat.transform), OnToggle(hat) } });
            Assert.That(pat.IsFollowDistanceTrigger, Is.True);

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Pat (distance)");
            var far = State(layer, "Far");
            var near = State(layer, "Near");
            var enter = far.transitions.Single();
            Assert.That(enter.conditions.Select(c => c.parameter + " " + c.mode), Is.EquivalentTo(new[] { "AD/Contact/Pat Greater", "AD/Hat If" }));
            var leave = near.transitions.Select(t => string.Join(" & ", t.conditions.Select(c => c.parameter + " " + c.mode))).ToArray();
            Assert.That(leave, Is.EquivalentTo(new[] { "AD/Contact/Pat Less", "AD/Hat IfNot" }), "the hand leaving, or the requirement failing, each leaves on its own");
        }

        /// <summary>Regression guard: passes before and after the change it guards by design. Its one red run came from a locale bug in Words, since fixed.</summary>
        [Test]
        public void AFadeContactWithoutRequirementsKeepsItsTwoTransitions()
        {
            FadeTrigger("Pat");

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Pat (distance)");
            var enter = State(layer, "Far").transitions.Single();
            Assert.That(enter.conditions.Select(Words), Is.EqualTo(new[] { "AD/Contact/Pat Greater 0.001" }));
            var leave = State(layer, "Near").transitions.Single();
            Assert.That(leave.conditions.Select(Words), Is.EqualTo(new[] { "AD/Contact/Pat Less 0.001" }));
        }

        [Test]
        public void AFadeRequirementContactGetsAReceiverOfItsOwn()
        {
            var pat = FadeTrigger("Pat");
            pat.when.Add(new ConditionGroup { all = { ContactOn(avatar.Hat.transform), ContactOn(avatar.Glasses.transform, "Finger") } });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var receivers = avatar.Root.GetComponentsInChildren<VRCContactReceiver>(true);
            Assert.That(receivers.Length, Is.EqualTo(2), "the driving contact and the required one");
            var required = receivers.Single(r => r.transform.parent == avatar.Glasses.transform);
            Assert.That(required.receiverType, Is.EqualTo(VRC.Dynamics.ContactReceiver.ReceiverType.Constant), "a requirement keeps its own touch setting");
            var enter = State(Layer(fx, "AD: Pat (distance)"), "Far").transitions.Single();
            Assert.That(enter.conditions.Any(c => c.parameter == required.parameter && c.mode == AnimatorConditionMode.If), Is.True);
        }

        [Test]
        public void OnlyWhileWorksOnAFadeContact()
        {
            var hat = HatToggle();
            var pat = FadeTrigger("Pat");
            pat.gateToggleId = hat.id;

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var enter = State(Layer(fx, "AD: Pat (distance)"), "Far").transitions.Single();
            Assert.That(enter.conditions.Select(c => c.parameter), Is.EquivalentTo(new[] { "AD/Contact/Pat", "AD/Hat" }));
        }

        // ---- Load ----------------------------------------------------------------------------------------------

        /// <summary>
        /// A trigger saved before the style existed (its data has no linkStyle, smoothStart or smoothFull) loads and
        /// builds as Smooth with its own threshold as the swap point, and its Replace still takes the shape's curve out.
        /// Partly a guard: Smooth is 0, so the load half passes as long as that stays so; the build half fails if the
        /// threshold or the mode were lost or if the trigger came back as a switch.
        /// </summary>
        [Test]
        public void AStoredLinkLoadsAsSmoothWithItsThresholdAndModeKept()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var stored = Link("Old link");
            stored.sourceThreshold = 35f;
            stored.linkMode = BlendShapeLinkMode.Replace;
            stored.blendShapes.Add(Eye(60f));
            stored.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = true });
            var json = EditorJsonUtility.ToJson(menu);
            // Saved before the style existed: the three new fields are simply not in the data.
            var old = Regex.Replace(json, "\"(linkStyle|smoothStart|smoothFull)\":[^,}]*,", "");
            Assert.That(old, Does.Not.Contain("smoothFull"));
            menu.entries.Clear();

            EditorJsonUtility.FromJsonOverwrite(old, menu);

            var entry = menu.entries.Single(e => e.name == "Old link");
            Assert.That(entry.linkStyle, Is.EqualTo(BlendShapeLinkStyle.Smooth), "every stored trigger becomes smooth");
            Assert.That(entry.sourceThreshold, Is.EqualTo(35f), "its threshold is kept, now the point objects and materials swap at");
            Assert.That(entry.linkMode, Is.EqualTo(BlendShapeLinkMode.Replace));
            Assert.That(entry.sourceShape, Is.EqualTo("Blush"));
            Assert.That(entry.blendShapes.Single().weight, Is.EqualTo(60f));
            Assert.That(entry.EffectiveSmoothStart, Is.EqualTo(0f));
            Assert.That(entry.EffectiveSmoothFull, Is.EqualTo(100f), "a Full that never was stored reads as fully on at 100");
            Assert.That(entry.when, Is.Empty, "no requirements appear out of nowhere");
            // The scene references are what a saved scene carries anyway; the round trip above is about the plain fields.
            entry.sourceRenderer = avatar.Body;
            entry.blendShapes.Single().renderer = avatar.Body;
            entry.objects.Single().target = avatar.Hat;

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Old link (shape)");
            Assert.That(StateNames(layer), Is.EquivalentTo(new[] { "Far", "Near", "Near on", "Reset", "Load" }), "it builds as Smooth");
            var swap = On(To(State(layer, "Near"), "Near on").Single(), "AD/Shape/Old link");
            Assert.That(swap.mode, Is.EqualTo(AnimatorConditionMode.Greater));
            Assert.That(swap.threshold, Is.EqualTo(35f).Within(0.01f), "the hat swaps at the stored threshold");
            Assert.That(((BlendTree)State(layer, "Near").motion).children.Select(c => c.threshold), Is.EqualTo(new[] { 0f, 100f }));
            var smile = State(Layer(fx, "Base"), "Smile").motion;
            Assert.That(Shape(smile, "Blush"), Is.Null, "Replace still takes the shape's own curve out");
            Assert.That(AnimationUtility.GetEditorCurve((AnimationClip)smile, MirrorBinding("AD/Shape/Old link")), Is.Not.Null);
        }

        // ---- The pause, clips outside FX, the source switch, the copy's default, the provider ------

        void BlinkWith(string shape)
        {
            avatar.Descriptor.enableEyeLook = true;
            avatar.Descriptor.customEyeLookSettings.eyelidType = VRCAvatarDescriptor.EyelidType.Blendshapes;
            avatar.Descriptor.customEyeLookSettings.eyelidsSkinnedMesh = avatar.Body;
            avatar.Descriptor.customEyeLookSettings.eyelidsBlendshapes = new[] { avatar.Mesh.GetBlendShapeIndex(shape), -1, -1 };
        }

        static float FlagIn(Motion motion, string flag) => AnimationUtility.GetEditorCurve((AnimationClip)motion, MirrorBinding(flag)).Evaluate(0f);

        [Test]
        public void AFadeContactPausesBlinkingOnlyWhileItsRequirementsHold()
        {
            BlinkWith("Blush");
            var hat = HatToggle();
            var blink = FadeTrigger("Blink");
            blink.when.Add(new ConditionGroup { all = { ContactOn(avatar.Hat.transform), OnToggle(hat) } });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var toPaused = State(Layer(fx, "AD: Blink pause"), "Idle").transitions.Single().conditions.Single();
            Assert.That(toPaused.parameter, Is.EqualTo("AD/Pause/Blink"), "the pause reads the layer's own flag, not the hand alone");
            var layer = Layer(fx, "AD: Blink (distance)");
            Assert.That(FlagIn(State(layer, "Far").motion, "AD/Pause/Blink"), Is.EqualTo(0f), "resting, which includes a near hand while Hat is off");
            foreach (var child in ((BlendTree)State(layer, "Near").motion).children)
                Assert.That(FlagIn(child.motion, "AD/Pause/Blink"), Is.EqualTo(1f), "Near is only reached while every requirement holds");
            Assert.That(fx.parameters.Single(p => p.name == "AD/Pause/Blink").type, Is.EqualTo(AnimatorControllerParameterType.Float));
        }

        [Test]
        public void AFadeContactWhoseRequirementDoesNotResolveBuildsNoPause()
        {
            BlinkWith("Blush");
            var blink = FadeTrigger("Blink");
            blink.when.Add(new ConditionGroup { all = { ContactOn(avatar.Hat.transform), new WhenCondition { kind = WhenConditionKind.Toggle, sourceToggleId = "gone" } } });

            var fx = BuildAll(out var errors);

            Assert.That(Reported(errors, "ad.warn.trigger_source_missing"), Is.True);
            Assert.That(fx.layers.Any(l => l.name == "AD: Blink (distance)"), Is.False, "a rule missing a part builds nothing");
            Assert.That(fx.layers.Any(l => l.name == "AD: Blink pause"), Is.False, "so nothing pauses blinking for it either");
        }

        [Test]
        public void ABlendShapeTriggerWithABlinkEffectPausesBlinking()
        {
            BlinkWith("Eye_Big");
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            Link().blendShapes.Add(Eye(80f));

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var toPaused = State(Layer(fx, "AD: Blink pause"), "Idle").transitions.Single().conditions.Single();
            Assert.That(toPaused.parameter, Is.EqualTo("AD/Pause/Blush link"));
            var layer = Layer(fx, "AD: Blush link (shape)");
            Assert.That(FlagIn(State(layer, "Far").motion, "AD/Pause/Blush link"), Is.EqualTo(0f));
            foreach (var child in ((BlendTree)State(layer, "Near").motion).children)
                Assert.That(FlagIn(child.motion, "AD/Pause/Blush link"), Is.EqualTo(1f), "blinking pauses while the trigger moves the eyelid");
        }

        [Test]
        public void TheInspectorNamesClipsOutsideFxBeforeTheBuild()
        {
            UseGestureSmile();
            var link = Link();
            link.blendShapes.Add(Eye(80f));
            AnimationUsage.Invalidate();
            AvatarPlanCache.Invalidate();

            var plan = AvatarPlanCache.GetFor(menu);

            var message = plan.Messages.Single(m => m.Key == "ad.warn.link_clips_outside_fx" && m.EntryIds.Contains(link.id));
            Assert.That(message.Args[2], Is.EqualTo("Gesture smile"), "read from the descriptor's own controllers, so the creator sees it before uploading");
        }

        [Test]
        public void AShapeNoFxClipMovesIsReportedAtBuild()
        {
            Link().blendShapes.Add(Eye(80f));

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            Assert.That(Reported(errors, "ad.warn.link_no_fx_clip"), Is.True, "no FX animation moves Blush, so the trigger has nothing to follow");
            Assert.That(fx.layers.Any(l => l.name == "AD: Blush link (shape)"), Is.True);
        }

        /// <summary>Guard: the PhysBone requirement path was already built in the task, so this passed on its first run.</summary>
        [Test]
        public void APhysBoneRequirementOnABlendShapeTriggerReadsItsParameter()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var bone = avatar.Hat.AddComponent<VRCPhysBone>();
            var link = Link();
            link.blendShapes.Add(Eye(80f));
            link.when.Add(new ConditionGroup { all = { new WhenCondition { kind = WhenConditionKind.PhysBone, physBone = bone, physBoneEvent = PhysBoneEvent.Grabbed } } });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Blush link (shape)");
            var grabbed = State(layer, "Far").transitions.Single().conditions.Single(c => c.parameter != Mirror);
            Assert.That(grabbed.mode, Is.EqualTo(AnimatorConditionMode.If));
            Assert.That(grabbed.parameter, Is.EqualTo(bone.parameter + "_IsGrabbed"), "the PhysBone was given the parameter the layer reads");
            Assert.That(State(layer, "Near").transitions.Any(t => t.conditions.Length == 1 && t.conditions[0].parameter == grabbed.parameter && t.conditions[0].mode == AnimatorConditionMode.IfNot), Is.True, "letting go leaves on its own");
        }

        [Test]
        public void TheCopyStartsWhereTheShapeIsOnceEveryToolHasRun()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            Link().blendShapes.Add(Eye(80f));

            // Shapes on the outfit pieces and other tools run after AvatarDoc's own pass and can still move a weight.
            var fx = BuildAll(out var errors, (asc, virtualFx) => avatar.Body.SetBlendShapeWeight(0, 35f));

            NoErrors(errors);
            Assert.That(fx.parameters.Single(p => p.name == Mirror).defaultFloat, Is.EqualTo(35f));
        }

        [Test]
        public void TheParameterProviderListsTheCopyAndThePauseFlagAsAnimatorOnly()
        {
            BlinkWith("Eye_Big");
            Link().blendShapes.Add(Eye(80f));
            AvatarPlanCache.Invalidate();

            var provided = ParameterInfo.ForUI.GetParametersForObject(avatar.Root).ToList();

            foreach (var name in new[] { Mirror, "AD/Pause/Blush link" })
            {
                var parameter = provided.Single(p => p.EffectiveName == name);
                Assert.That(parameter.IsAnimatorOnly, Is.True, name);
                Assert.That(parameter.WantSynced, Is.False, name);
                Assert.That(parameter.ParameterType, Is.EqualTo(AnimatorControllerParameterType.Float), name);
                Assert.That(parameter.BitUsage, Is.EqualTo(0), name);
                Assert.That(parameter.Source, Is.SameAs(menu), name);
            }
        }

        [Test]
        public void SwitchingABlendShapeTriggerWithRequirementsAwayKeepsThePickedSourceFirst()
        {
            var hat = HatToggle();
            var link = Link();
            link.blendShapes.Add(Eye(80f));
            link.when.Add(new ConditionGroup { all = { OnToggle(hat) } });
            var view = View();
            view.Tree.SelectEntry(link.id);
            Undo.IncrementCurrentGroup();

            view.Detail.SetTriggerSource(TriggerSource.Parameter);

            var entry = menu.entries.Single(e => e.id == link.id);
            Assert.That(entry.source, Is.EqualTo(TriggerSource.Parameter));
            Assert.That(entry.when[0].all.Count, Is.EqualTo(2));
            Assert.That(entry.when[0].all[0].kind, Is.EqualTo(WhenConditionKind.Parameter), "the source just picked is the rule's first condition");
            Assert.That(entry.when[0].all[1].kind, Is.EqualTo(WhenConditionKind.Toggle), "the requirement stays, now beside it");
            Assert.That(view.Detail.Query<VisualElement>("ad-when-row").ToList().Count, Is.EqualTo(2));

            Undo.PerformUndo();

            entry = menu.entries.Single(e => e.id == link.id);
            Assert.That(entry.source, Is.EqualTo(TriggerSource.BlendShape), "one Undo step takes both back");
            Assert.That(entry.when[0].all.Count, Is.EqualTo(1));
        }

        [Test]
        public void ABlendShapeTriggersLeftoverCardIsCaptionedAsNotUsed()
        {
            var hat = HatToggle();
            var link = Link();
            link.when.Add(new ConditionGroup { all = { OnToggle(hat) } });
            link.when.Add(new ConditionGroup { all = { OnToggle(hat) } });
            var view = View();
            view.Tree.SelectEntry(link.id);

            var caption = view.Detail.Q<Label>("ad-when-ignored");
            Assert.That(caption, Is.Not.Null);
            Assert.That(caption.text, Is.EqualTo(L.Text("ad.ui.when.ignored")));
            Assert.That(view.Detail.Q<Label>("ad-when-any"), Is.Null, "never Any of these: the build does not read that card");
        }

        // ---- Inspector -----------------------------------------------------------------------------------------

        AvatarDoctorView View() => new AvatarDoctorView(new SerializedObject(menu), menu);

        [Test]
        public void TheBlendShapePanelOffersTheStyleAndItsSliders()
        {
            var link = Link();
            link.blendShapes.Add(Eye(80f));
            var view = View();
            view.Tree.SelectEntry(link.id);

            var style = view.Detail.Q<SegmentedChoice>("ad-trigger-style");
            Assert.That(style, Is.Not.Null);
            Assert.That(style.Items.Select(i => i.text), Is.EqualTo(new[] { L.Text("ad.ui.trigger.style.smooth"), L.Text("ad.ui.trigger.style.switch") }));
            Assert.That(style.Index, Is.EqualTo(0), "Smooth by default");
            Assert.That(view.Detail.Q<Slider>("ad-trigger-smooth-start"), Is.Not.Null);
            Assert.That(view.Detail.Q<Slider>("ad-trigger-smooth-full"), Is.Not.Null);
            Assert.That(view.Detail.Q<Slider>("ad-trigger-threshold").label, Is.EqualTo(L.Text("ad.ui.trigger.swap_at")), "in Smooth the one switch point is for objects and materials");
            Assert.That(view.Detail.Q<DropdownField>("ad-trigger-mode"), Is.Not.Null, "Add or Replace stays");

            style.Pick(1);

            Assert.That(link.linkStyle, Is.EqualTo(BlendShapeLinkStyle.Switch));
            Assert.That(view.Detail.Q<Slider>("ad-trigger-smooth-start"), Is.Null);
            Assert.That(view.Detail.Q<Slider>("ad-trigger-smooth-full"), Is.Null);
            Assert.That(view.Detail.Q<Slider>("ad-trigger-threshold").label, Is.EqualTo(L.Text("ad.ui.trigger.switch_at")), "in Switch it moves the whole trigger");
            Assert.That(view.Detail.Q<SegmentedChoice>("ad-trigger-style").Index, Is.EqualTo(1));
        }

        [Test]
        public void TheBlendShapeCardHasTheShapeFirstAndOffersAndButNotOr()
        {
            var link = Link();
            link.blendShapes.Add(Eye(80f));
            var view = View();
            view.Tree.SelectEntry(link.id);

            var card = view.Detail.Q<VisualElement>("ad-when-card");
            Assert.That(card, Is.Not.Null, "the shape sits in a card of its own");
            Assert.That(card.Q<Button>("ad-trigger-shape"), Is.Not.Null, "its first row is the shape");
            Assert.That(card.Q<Button>("ad-when-add-and"), Is.Not.Null, "+ And sits in the card");
            Assert.That(view.Detail.Q<Button>("ad-when-add-or"), Is.Null, "one driver, so no other way to be met");
            Assert.That(view.Detail.Q<Button>("ad-trigger-gate-add"), Is.Not.Null, "Only while is offered too");

            view.Detail.AddCondition(0);

            Assert.That(link.source, Is.EqualTo(TriggerSource.BlendShape), "a requirement never turns the shape into something else");
            Assert.That(link.sourceShape, Is.EqualTo("Blush"));
            Assert.That(link.when.Count, Is.EqualTo(1));
            Assert.That(link.when[0].all.Count, Is.EqualTo(1), "the shape stays in the flat fields; the group holds the requirements");
            card = view.Detail.Q<VisualElement>("ad-when-card");
            Assert.That(card.Q<Button>("ad-trigger-shape"), Is.Not.Null);
            Assert.That(card.Query<VisualElement>("ad-when-row").ToList().Count, Is.EqualTo(1));
            Assert.That(view.Detail.Q<SegmentedChoice>("ad-trigger-style"), Is.Not.Null, "the rest of the panel stays the blend shape panel");

            view.Detail.RemoveCondition(0, 0);

            Assert.That(link.when, Is.Empty);
            Assert.That(link.source, Is.EqualTo(TriggerSource.BlendShape), "removing the last requirement leaves the shape alone");
            Assert.That(link.sourceShape, Is.EqualTo("Blush"));
        }

        [Test]
        public void PlusAndSwitchesOffOnceTheBlendShapeCardIsFull()
        {
            var hat = HatToggle();
            var link = Link();
            link.blendShapes.Add(Eye(80f));
            link.when.Add(new ConditionGroup { all = Enumerable.Range(0, ConditionLogic.MaxPerGroup - 1).Select(_ => OnToggle(hat)).ToList() });
            var view = View();
            view.Tree.SelectEntry(link.id);

            var add = view.Detail.Q<Button>("ad-when-add-and");
            Assert.That(add.enabledSelf, Is.False, "the shape and " + (ConditionLogic.MaxPerGroup - 1) + " requirements fill the card");
            Assert.That(add.tooltip, Is.EqualTo(string.Format(L.Text("ad.ui.when.add_and.limit_driver.tip"), ConditionLogic.MaxPerGroup)));
        }

        [Test]
        public void AFadeContactOffersAndAndOnlyWhileButNotOr()
        {
            var pat = FadeTrigger("Pat");
            var view = View();
            view.Tree.SelectEntry(pat.id);

            Assert.That(view.Detail.Q<Button>("ad-when-add-and"), Is.Not.Null);
            Assert.That(view.Detail.Q<Button>("ad-when-add-or"), Is.Null);
            Assert.That(view.Detail.Q<Button>("ad-trigger-gate-add"), Is.Not.Null);

            view.Detail.AddCondition(0);

            Assert.That(pat.when.Count, Is.EqualTo(1));
            Assert.That(pat.when[0].all.Count, Is.EqualTo(2), "the contact moved into the card first, and the requirement joined it");
            Assert.That(pat.when[0].all[0].kind, Is.EqualTo(WhenConditionKind.Contact));
            Assert.That(pat.IsFollowDistanceTrigger, Is.True, "the contact still drives the fade");
            Assert.That(pat.Requirements().Count(), Is.EqualTo(1));
            Assert.That(view.Detail.Q<VisualElement>("ad-when-card").Q<Button>("ad-when-add-and"), Is.Not.Null, "once the contact has a card, + And sits in it");
            Assert.That(view.Detail.Q<Button>("ad-when-add-or"), Is.Null);
            Assert.That(view.Detail.Query<Button>().ToList().Count(b => b.name != null && b.name.StartsWith("ad-when-add-and")), Is.EqualTo(1), "one + And, under the card");
        }

        [Test]
        public void AddEffectOnABlendShapeTriggerOffersProperties()
        {
            var link = Link();
            var view = View();
            view.Tree.SelectEntry(link.id);

            Assert.That(view.Detail.EffectSections.Select(s => s.NameKey), Is.EqualTo(new[] { "ad.ui.objects", "ad.ui.blendshapes", "ad.ui.materials", "ad.ui.targets.properties" }), "no Follows: there is no on and off for one to ride on");
        }

        [Test]
        public void BlendShapePanelsFitAndAlign()
        {
            var hat = HatToggle();
            var smooth = Link("Smooth");
            smooth.blendShapes.Add(Eye(80f));
            smooth.when.Add(new ConditionGroup { all = { OnToggle(hat) } });
            var toggled = Link("Switched");
            toggled.linkStyle = BlendShapeLinkStyle.Switch;
            toggled.blendShapes.Add(Eye(80f));
            var pat = FadeTrigger("Pat");
            pat.when.Add(new ConditionGroup { all = { ContactOn(avatar.Hat.transform), OnToggle(hat) } });
            var view = View();
            var allow = new PanelAudit.AllowList()
                .Allow(e => e.name == "remove" && e.ClassListContains("ad-w-24"), "a target list row's own trailing x remove button")
                .Allow(e => e.ClassListContains("ad-chip-remove"), "a chip's own remove x")
                .AllowForRule(3, e => e is Toggle && e.parent != null && e.parent.ClassListContains("ad-flags"), "a compact flags row's own toggle keeps its inline caption");
            LayoutProbe.Measure(view, () =>
            {
                foreach (var entry in new[] { smooth, toggled, pat })
                {
                    view.Tree.SelectEntry(entry.id);
                    LayoutProbe.Validate(view);
                    PanelAudit.Audit(view.Detail, view.Detail.worldBound.xMax, allow);
                }
            });
        }

        // ---- Preview -------------------------------------------------------------------------------------------

        Dictionary<Renderer, RendererState> Preview(Dictionary<string, float> overrides = null)
        {
            AvatarPlanCache.Invalidate();
            return PreviewModel.Compute(avatar.Root.transform, new[] { menu }, overrides, SceneReader.Direct);
        }

        float PreviewedEye(Dictionary<Renderer, RendererState> states)
        {
            int eye = avatar.Mesh.GetBlendShapeIndex("Eye_Big");
            return states.TryGetValue(avatar.Body, out var state) && state.Shapes.TryGetValue(eye, out var weight) ? weight : avatar.Body.GetBlendShapeWeight(eye);
        }

        [Test]
        public void HalfwayBetweenStartAndFullGivesHalfTheEffectWhileSwitchGivesAllOrNothing()
        {
            var link = Link();
            link.smoothStart = 20f;
            link.smoothFull = 80f;
            link.sourceThreshold = 50f;
            link.blendShapes.Add(Eye(80f));
            avatar.Body.SetBlendShapeWeight(0, 50f);

            Assert.That(PreviewedEye(Preview()), Is.EqualTo(40f).Within(0.001f), "50 is halfway from 20 to 80, so half of 80");

            link.linkStyle = BlendShapeLinkStyle.Switch;
            Assert.That(PreviewedEye(Preview()), Is.EqualTo(80f), "at the threshold Switch is fully on");
            avatar.Body.SetBlendShapeWeight(0, 49f);
            Assert.That(PreviewedEye(Preview()), Is.EqualTo(0f), "just below it, nothing");
        }

        [Test]
        public void ThePreviewChecksToggleRequirementsAndCountsTheRestAsMet()
        {
            var hat = HatToggle();
            var link = Link();
            link.blendShapes.Add(Eye(80f));
            link.when.Add(new ConditionGroup { all = { OnToggle(hat) } });
            avatar.Body.SetBlendShapeWeight(0, 100f);

            Assert.That(PreviewedEye(Preview()), Is.EqualTo(0f), "Hat is off, so the effects rest");
            Assert.That(PreviewedEye(Preview(new Dictionary<string, float> { [hat.id] = 1f })), Is.EqualTo(80f), "with Hat on the shape drives them");

            link.when[0].all.Add(new WhenCondition { kind = WhenConditionKind.Parameter, sourceParameter = "AFK", condition = ParameterCondition.IsTrue });
            Assert.That(PreviewedEye(Preview(new Dictionary<string, float> { [hat.id] = 1f })), Is.EqualTo(80f), "a parameter the scene cannot know counts as met");

            var view = View();
            view.Tree.SelectEntry(link.id);
            Assert.That(view.Detail.Q<Label>("ad-preview-unchecked"), Is.Not.Null, "and the panel says so under the preview control");

            link.when[0].all.RemoveAt(1);
            view.Refresh();
            view.Tree.SelectEntry(link.id);
            Assert.That(view.Detail.Q<Label>("ad-preview-unchecked"), Is.Null, "toggles alone are all checked");
        }

        [Test]
        public void AForcedFadeContactRestsWhileARequirementToggleIsOff()
        {
            var hat = HatToggle();
            var pat = FadeTrigger("Pat");
            pat.when.Add(new ConditionGroup { all = { ContactOn(avatar.Hat.transform), OnToggle(hat) } });
            int blush = avatar.Mesh.GetBlendShapeIndex("Blush");

            var off = Preview(new Dictionary<string, float> { [pat.id] = 1f });
            Assert.That(off.TryGetValue(avatar.Body, out var state) && state.Shapes.ContainsKey(blush), Is.False);
            var on = Preview(new Dictionary<string, float> { [pat.id] = 1f, [hat.id] = 1f });
            Assert.That(on[avatar.Body].Shapes[blush], Is.EqualTo(100f));
        }

        // ---- Words ---------------------------------------------------------------------------------------------

        [Test]
        public void WhatThisBuildsCountsTheLayerAndNoBits()
        {
            var link = Link();
            link.blendShapes.Add(Eye(80f));
            var view = View();
            var plan = view.Plan;
            var row = view.Tree.RowFor(link.id);

            var summary = WhatThisBuildsModel.Compute(plan, row);
            Assert.That(summary.FxLayers, Is.EqualTo(1), "one layer of its own now");
            Assert.That(summary.Bits, Is.EqualTo(0));
            var line = WhatThisBuildsModel.Describe(plan, row);
            Assert.That(line, Does.Contain(string.Format(L.Text("ad.ui.builds.smooth"), 0, 100)));
            Assert.That(line, Does.Contain(L.Text("ad.ui.builds.mirror")));
        }

        [Test]
        public void TheSummaryListsTheCopyAsAnAnimatorOnlyParameter()
        {
            Link().blendShapes.Add(Eye(80f));
            var plan = AvatarPlanCache.GetFor(menu);

            var text = SummaryBuilder.Build(plan, menu, SummaryFormat.Plain);

            Assert.That(text, Does.Contain(L.Text("ad.summary.parameters.animator_only")));
            Assert.That(text, Does.Contain(Mirror));
        }
    }
}
#endif
