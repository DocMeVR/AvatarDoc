#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// With Write Defaults off, a shape nobody else animated kept whatever value the pin last wrote, forever, once
    /// the pin was left; an additive layer then added on top of that stale value instead of the rest pose. These
    /// tests cover the final pass that hands a pinned shape back to rest, once, in the pin layer's Reset state,
    /// unless some other, non-additive layer can really take it. Off itself never writes the shape.
    /// </summary>
    public class PinReleaseTests
    {
        TestAvatar avatar;
        AvatarDoc menu;

        [SetUp]
        public void SetUp()
        {
            GeneratedAssets.Note();
            avatar = TestAvatar.Create();
            menu = avatar.AddMenu();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            GeneratedAssets.CleanUp();
        }

        (MenuTree tree, ParameterPlan plan) Model(params AvatarDoc[] menus)
        {
            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, menus);
            return (tree, ParameterPlanner.Plan(tree, new Dictionary<string, PlannedType>()));
        }

        static AnimatorController FxOf(TestAvatar a)
            => (AnimatorController)a.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;

        static AnimatorControllerLayer Layer(AnimatorController fx, string name) => fx.layers.Single(l => l.name == name);
        static AnimatorState State(AnimatorControllerLayer layer, string name) => layer.stateMachine.states.Single(s => s.state.name == name).state;

        static AnimationCurve CurveOf(Motion motion, string path, string property)
            => AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), property));

        /// <summary>The curve the layer's Reset state writes for the property, or null when the layer has no Reset state left or it writes nothing for the property.</summary>
        static AnimationCurve ResetCurveOf(AnimatorControllerLayer layer, string path, string property)
        {
            var reset = layer.stateMachine.states.Select(s => s.state).SingleOrDefault(s => s.name == "Reset");
            return reset == null ? null : CurveOf(reset.motion, path, property);
        }

        /// <summary>Whether the final pass found another layer owning the shape: a shape rests once either way, so only this tells.</summary>
        static bool Owned(PinLayerState pins, string shape) => pins.OwnedByAnotherLayer.Contains(EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape." + shape));

        /// <summary>The rest curve the pin layer hands its shape back with, after checking its Off state never writes the shape itself.</summary>
        static AnimationCurve RestCurveOf(AnimatorControllerLayer layer, string path, string property)
        {
            Assert.That(CurveOf(State(layer, "Off").motion, path, property), Is.Null, layer.name + ": Off never writes " + property + " itself");
            return ResetCurveOf(layer, path, property);
        }

        MenuEntry PinnedFace(float sceneWeight)
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            avatar.Body.SetBlendShapeWeight(0, sceneWeight);
            return face;
        }

        [Test]
        public void TheRestValueIsTheSceneValueBeforeAnyEffect()
        {
            var face = PinnedFace(37f);
            // On by default, so DefaultsApplier writes the pinned weight into the scene before the animator is
            // even built: a rest value read from the scene afterward, instead of from the capture taken before it,
            // would come back 100, not 37, and this test would not be able to tell the two apart.
            face.defaultOn = true;
            var (tree, plan) = Model(menu);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            Assert.That(avatar.Body.GetBlendShapeWeight(0), Is.EqualTo(100f), "defaults really did write the pinned value into the scene");
            var pins = new PinLayerState();
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState, pins.Names, pinState: pins));
            context.DeactivateAllExtensionContexts();

            var binding = pins.Bindings.Single(b => b.Shape == "Blush");
            Assert.That(binding.RestValue, Is.EqualTo(37f), "the rest value is the scene weight before AvatarDoc wrote anything, not the pinned weight defaults just wrote");
        }

        [Test]
        public void APinNobodyElseAnimatesReturnsToRestWhenOff()
        {
            PinnedFace(22f);

            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error), Is.Empty);

            AvatarDoctorPlugin.FinishAfterOtherTools(context);

            var curve = RestCurveOf(Layer(FxOf(avatar), "AD: Face (pin)"), "Body", "blendShape.Blush");
            Assert.That(curve, Is.Not.Null, "nobody else animates Blush, so the pin has to hand it back itself");
            Assert.That(curve.Evaluate(0f), Is.EqualTo(22f), "handed back to the scene value from before the build, not 0");
        }

        [Test]
        public void ATriggerPinNobodyElseAnimatesReturnsToRestWhenOff()
        {
            // The user's exact shape: a condition-driven trigger, not a toggle, pinning a shape nothing else
            // animates and nothing else pins.
            var source = menu.AddEntry(EntryKind.Toggle, "PetSource");
            var headpat = menu.AddEntry(EntryKind.Trigger, "Headpat");
            headpat.source = TriggerSource.Toggle;
            headpat.sourceToggleId = source.id;
            headpat.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            avatar.Body.SetBlendShapeWeight(0, 22f);

            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error), Is.Empty);

            AvatarDoctorPlugin.FinishAfterOtherTools(context);

            var curve = RestCurveOf(Layer(FxOf(avatar), "AD: Headpat (pin)"), "Body", "blendShape.Blush");
            Assert.That(curve, Is.Not.Null, "nobody else animates Blush, so the trigger's own pin has to hand it back itself");
            Assert.That(curve.Evaluate(0f), Is.EqualTo(22f));
        }

        [Test]
        public void ATriggerPinFaceTrackingOwnsStillRestsOnce()
        {
            // A Hold headpat on a face tracking avatar: the pinned shape is one face tracking's Direct tree drives.
            // On other players' copies face tracking did not always write it again, so the pin rests it itself.
            avatar.Fx.AddParameter("FT/Blush", AnimatorControllerParameterType.Float);
            var trackClip = TestAvatar.ShapeClip("FT Blush", "Body", "Blush", 40f, 40f);
            var trackTree = new BlendTree { name = "FT", blendType = BlendTreeType.Direct };
            trackTree.children = new[] { new ChildMotion { motion = trackClip, directBlendParameter = "FT/Blush", timeScale = 1f } };
            avatar.Fx.AddLayer("Face Tracking");
            var faceTracking = avatar.Fx.layers[avatar.Fx.layers.Length - 1];
            var tracking = faceTracking.stateMachine.AddState("Tracking");
            tracking.motion = trackTree;
            tracking.writeDefaultValues = true;
            var disabled = faceTracking.stateMachine.AddState("Disabled");
            disabled.writeDefaultValues = false;
            var layers = avatar.Fx.layers;
            foreach (var layer in layers) layer.defaultWeight = 1f;
            avatar.Fx.layers = layers;
            var source = menu.AddEntry(EntryKind.Toggle, "PetSource");
            var headpat = menu.AddEntry(EntryKind.Trigger, "Headpat");
            headpat.source = TriggerSource.Toggle;
            headpat.sourceToggleId = source.id;
            headpat.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            avatar.Body.SetBlendShapeWeight(0, 22f);

            try
            {
                var context = new BuildContext(avatar.Root, null);
                context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
                var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
                context.DeactivateAllExtensionContexts();
                Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error), Is.Empty);

                AvatarDoctorPlugin.FinishAfterOtherTools(context);

                Assert.That(AnimatorBuilder.LastOwnedByAnotherLayer.Contains(EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blush")), Is.True, "sanity: face tracking's Direct tree owns Blush");
                var curve = RestCurveOf(Layer(FxOf(avatar), "AD: Headpat (pin)"), "Body", "blendShape.Blush");
                Assert.That(curve, Is.Not.Null, "the trigger's pin rests Blush once on its way out all the same");
                Assert.That(curve.Evaluate(0f), Is.EqualTo(22f));
            }
            finally
            {
                ClearUndoOf(faceTracking.stateMachine, tracking, disabled);
                Object.DestroyImmediate(trackTree);
                Object.DestroyImmediate(trackClip);
            }
        }

        [Test]
        public void ATriggerPinOnTheBlinkShapeRestsOnce()
        {
            // A gesture face that closes the eyes: a Hold trigger pinning the shape VRChat blinks with.
            avatar.Descriptor.enableEyeLook = true;
            avatar.Descriptor.customEyeLookSettings.eyelidType = VRCAvatarDescriptor.EyelidType.Blendshapes;
            avatar.Descriptor.customEyeLookSettings.eyelidsSkinnedMesh = avatar.Body;
            avatar.Descriptor.customEyeLookSettings.eyelidsBlendshapes = new[] { avatar.Mesh.GetBlendShapeIndex("Eye_Big"), -1, -1 };
            var source = menu.AddEntry(EntryKind.Toggle, "WinkSource");
            var wink = menu.AddEntry(EntryKind.Trigger, "Wink");
            wink.source = TriggerSource.Toggle;
            wink.sourceToggleId = source.id;
            wink.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 100f, mode = ShapeEffectMode.Pin });
            avatar.Body.SetBlendShapeWeight(avatar.Mesh.GetBlendShapeIndex("Eye_Big"), 7f);

            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error), Is.Empty);

            AvatarDoctorPlugin.FinishAfterOtherTools(context);

            var curve = RestCurveOf(Layer(FxOf(avatar), "AD: Wink (pin)"), "Body", "blendShape.Eye_Big");
            Assert.That(curve, Is.Not.Null, "the wink rests the eyes once on its way out, so no copy of the avatar keeps them closed");
            Assert.That(curve.Evaluate(0f), Is.EqualTo(0f), "at 0, where VRChat's blinking starts from, not at the scene's 7");
        }

        [Test]
        public void APinAnOverrideLayerAnimatesIsHandedBack()
        {
            PinnedFace(22f);
            var (tree, plan) = Model(menu);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            var pins = new PinLayerState();
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState, pins.Names, pinState: pins));
            // Simulates a layer another tool merges after AvatarDoc, such as face tracking, that can really hold
            // Blush on its own while the pin layer's own off state stays empty.
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx);
            var otherLayer = fx.AddLayer(LayerPriority.Default, "Face Tracking");
            var clip = VirtualClip.Create("FT Idle");
            clip.SetFloatCurve(EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), AnimationCurve.Constant(0f, 1f / 60f, 40f));
            otherLayer.StateMachine.AddState("Idle", clip);
            context.DeactivateAllExtensionContexts();

            var context2 = new BuildContext(avatar.Root, null);
            var asc2 = context2.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            AnimatorBuilder.ReleasePinsToRest(asc2, pins);
            context2.DeactivateAllExtensionContexts();

            Assert.That(Owned(pins, "Blush"), Is.True, "an override layer elsewhere owns Blush");
            Assert.That(RestCurveOf(Layer(FxOf(avatar), "AD: Face (pin)"), "Body", "blendShape.Blush"), Is.Not.Null, "and the pin still rests Blush once on its way out, so a copy of the avatar where the owner stands still never keeps the pinned value");
        }

        [Test]
        public void APinOnlyAnAdditiveLayerAnimatesReturnsToRest()
        {
            PinnedFace(22f);
            var (tree, plan) = Model(menu);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            var pins = new PinLayerState();
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState, pins.Names, pinState: pins));
            // An additive face tracking layer adds to whatever the property already is; it cannot stand in for the
            // rest pose on its own, so the pin still has to hand the shape back.
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx);
            var otherLayer = fx.AddLayer(LayerPriority.Default, "Face Tracking Additive");
            otherLayer.BlendingMode = AnimatorLayerBlendingMode.Additive;
            var clip = VirtualClip.Create("FT Additive");
            clip.SetFloatCurve(EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), AnimationCurve.Constant(0f, 1f / 60f, 40f));
            otherLayer.StateMachine.AddState("Idle", clip);
            context.DeactivateAllExtensionContexts();

            var context2 = new BuildContext(avatar.Root, null);
            var asc2 = context2.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            AnimatorBuilder.ReleasePinsToRest(asc2, pins);
            context2.DeactivateAllExtensionContexts();

            var curve = RestCurveOf(Layer(FxOf(avatar), "AD: Face (pin)"), "Body", "blendShape.Blush");
            Assert.That(Owned(pins, "Blush"), Is.False, "an additive layer cannot replace the rest pose, so the pin hands it back itself");
            Assert.That(curve, Is.Not.Null, "an additive layer cannot replace the rest pose, so the pin hands it back itself");
            Assert.That(curve.Evaluate(0f), Is.EqualTo(22f));
        }

        [Test]
        public void TwoPinsOnOneShapeDoNotMaskEachOther()
        {
            // The user's exact shape: a toggle pinned with Capture (Sleepy), and a trigger pinned the same way
            // (Headpat), both on Blush. AnimatorBuilder.Build makes every toggle's pin layer before
            // TriggerBuilder.Build makes any trigger's, so Sleepy's pin is the lower one and Headpat's is the higher.
            // ResetOnceTests.PlayedBackTwoPinsOnOneShapeComeBackToRestWhicheverIsLeftLast plays the same pair back.
            var sleepy = menu.AddEntry(EntryKind.Toggle, "Sleepy");
            sleepy.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 90f, mode = ShapeEffectMode.Pin });
            var petSource = menu.AddEntry(EntryKind.Toggle, "PetSource");
            var headpat = menu.AddEntry(EntryKind.Trigger, "Headpat");
            headpat.source = TriggerSource.Toggle;
            headpat.sourceToggleId = petSource.id;
            headpat.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 10f, mode = ShapeEffectMode.Pin });
            avatar.Body.SetBlendShapeWeight(0, 5f);

            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error), Is.Empty);

            AvatarDoctorPlugin.FinishAfterOtherTools(context);

            var fx = FxOf(avatar);
            var sleepyRest = RestCurveOf(Layer(fx, "AD: Sleepy (pin)"), "Body", "blendShape.Blush");
            var headpatRest = RestCurveOf(Layer(fx, "AD: Headpat (pin)"), "Body", "blendShape.Blush");
            Assert.That(sleepyRest, Is.Not.Null, "Sleepy hands Blush back to rest once on its own way out");
            Assert.That(headpatRest, Is.Not.Null,
                "so does Headpat, so Blush never stays at Headpat's value when Headpat is left last; its Off writes nothing, so it cannot mask Sleepy's pin while Sleepy is on, which was the user's bug");
            Assert.That(sleepyRest.Evaluate(0f), Is.EqualTo(5f));
            Assert.That(headpatRest.Evaluate(0f), Is.EqualTo(5f));
        }

        [Test]
        public void TwoPinsOnOneShapeDoNotMaskEachOtherInReverseBuildOrder()
        {
            // The same two pins, added the other way round: whichever entry is built first is the lower pin,
            // whatever its name, and neither order changes who resets.
            var headpat = menu.AddEntry(EntryKind.Toggle, "Headpat");
            headpat.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 10f, mode = ShapeEffectMode.Pin });
            var sleepy = menu.AddEntry(EntryKind.Toggle, "Sleepy");
            sleepy.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 90f, mode = ShapeEffectMode.Pin });
            avatar.Body.SetBlendShapeWeight(0, 5f);

            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error), Is.Empty);

            AvatarDoctorPlugin.FinishAfterOtherTools(context);

            var fx = FxOf(avatar);
            Assert.That(RestCurveOf(Layer(fx, "AD: Headpat (pin)"), "Body", "blendShape.Blush"), Is.Not.Null, "built first this time, Headpat still resets Blush once on its way out");
            Assert.That(RestCurveOf(Layer(fx, "AD: Sleepy (pin)"), "Body", "blendShape.Blush"), Is.Not.Null, "and so does Sleepy, built second");
        }

        [Test]
        public void AnOverrideAlsoFlattensAPinsRestCurve()
        {
            var over = MenuEntry.Create(EntryKind.Override, "Silence");
            over.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 0f });
            menu.entries.Add(over);
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            avatar.Body.SetBlendShapeWeight(0, 45f);

            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error), Is.Empty);

            AvatarDoctorPlugin.FinishAfterOtherTools(context);

            var curve = RestCurveOf(Layer(FxOf(avatar), "AD: Face (pin)"), "Body", "blendShape.Blush");
            Assert.That(curve, Is.Not.Null, "nothing but the override animates Blush, so the pin still has to hand it back itself");
            Assert.That(curve.Evaluate(0f), Is.EqualTo(0f),
                "the override flattens the rest curve to its own pinned value, the same as it flattens every other clip carrying this shape; the pin's last-writer position must not let it escape");
        }

        [Test]
        public void AWriteDefaultsOffLayerThatOnlySometimesWritesDoesNotOwn()
        {
            PinnedFace(22f);
            var (tree, plan) = Model(menu);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            var pins = new PinLayerState();
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState, pins.Names, pinState: pins));
            // A gesture layer whose Fist state writes Blush and whose Idle state does not: after the hand leaves
            // Fist, Blush would keep whatever the pin last wrote, the user's bug again on a different avatar.
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx);
            var gesture = fx.AddLayer(LayerPriority.Default, "Left Hand");
            var idle = gesture.StateMachine.AddState("Idle", VirtualClip.Create("Idle"));
            idle.WriteDefaultValues = false;
            var fistClip = VirtualClip.Create("Fist");
            fistClip.SetFloatCurve(EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), AnimationCurve.Constant(0f, 1f / 60f, 40f));
            var fist = gesture.StateMachine.AddState("Fist", fistClip);
            fist.WriteDefaultValues = false;
            context.DeactivateAllExtensionContexts();

            var context2 = new BuildContext(avatar.Root, null);
            var asc2 = context2.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            AnimatorBuilder.ReleasePinsToRest(asc2, pins);
            context2.DeactivateAllExtensionContexts();

            var curve = RestCurveOf(Layer(FxOf(avatar), "AD: Face (pin)"), "Body", "blendShape.Blush");
            Assert.That(Owned(pins, "Blush"), Is.False, "Idle's own clip does not carry Blush, so this layer cannot be trusted to hold it whenever Idle is the state actually playing");
            Assert.That(curve, Is.Not.Null, "Idle's own clip does not carry Blush, so this layer cannot be trusted to hold it whenever Idle is the state actually playing");
            Assert.That(curve.Evaluate(0f), Is.EqualTo(22f));
        }

        [Test]
        public void AZeroWeightLayerDoesNotOwn()
        {
            PinnedFace(22f);
            var (tree, plan) = Model(menu);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            var pins = new PinLayerState();
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState, pins.Names, pinState: pins));
            // Write Defaults on and a curve on every state would otherwise look like ownership, but a layer at
            // zero weight never actually contributes anything at runtime.
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx);
            var faded = fx.AddLayer(LayerPriority.Default, "Faded Face Tracking");
            faded.DefaultWeight = 0f;
            var clip = VirtualClip.Create("Faded Idle");
            clip.SetFloatCurve(EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), AnimationCurve.Constant(0f, 1f / 60f, 40f));
            var state = faded.StateMachine.AddState("Idle", clip);
            state.WriteDefaultValues = true;
            context.DeactivateAllExtensionContexts();

            var context2 = new BuildContext(avatar.Root, null);
            var asc2 = context2.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            AnimatorBuilder.ReleasePinsToRest(asc2, pins);
            context2.DeactivateAllExtensionContexts();

            var curve = RestCurveOf(Layer(FxOf(avatar), "AD: Face (pin)"), "Body", "blendShape.Blush");
            Assert.That(Owned(pins, "Blush"), Is.False, "a layer at zero weight contributes nothing at runtime, so it cannot be trusted to hold the shape either");
            Assert.That(curve, Is.Not.Null, "a layer at zero weight contributes nothing at runtime, so it cannot be trusted to hold the shape either");
            Assert.That(curve.Evaluate(0f), Is.EqualTo(22f));
        }

        [Test]
        public void AWriteDefaultsOnLayerOwns()
        {
            PinnedFace(22f);
            var (tree, plan) = Model(menu);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            var pins = new PinLayerState();
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState, pins.Names, pinState: pins));
            // An AFK layer, Write Defaults on throughout, that only writes Blush from one of its two states: every
            // state being Write Defaults on is enough on its own, the same as the real avatar's own AFK layer.
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx);
            var afk = fx.AddLayer(LayerPriority.Default, "AFK");
            var idle = afk.StateMachine.AddState("Idle", VirtualClip.Create("Idle"));
            idle.WriteDefaultValues = true;
            var poseClip = VirtualClip.Create("AFK Pose");
            poseClip.SetFloatCurve(EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), AnimationCurve.Constant(0f, 1f / 60f, 40f));
            var pose = afk.StateMachine.AddState("Pose", poseClip);
            pose.WriteDefaultValues = true;
            context.DeactivateAllExtensionContexts();

            var context2 = new BuildContext(avatar.Root, null);
            var asc2 = context2.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            AnimatorBuilder.ReleasePinsToRest(asc2, pins);
            context2.DeactivateAllExtensionContexts();

            Assert.That(Owned(pins, "Blush"), Is.True, "every state of AFK is Write Defaults on, so it owns Blush");
            Assert.That(RestCurveOf(Layer(FxOf(avatar), "AD: Face (pin)"), "Body", "blendShape.Blush"), Is.Not.Null, "and the pin still rests Blush once on its way out, so a copy of the avatar where the owner stands still never keeps the pinned value");
        }

        [Test]
        public void WriteDefaultsOnLayerOwnsOnlyWhatItAnimates()
        {
            PinnedFace(22f);
            var (tree, plan) = Model(menu);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            var pins = new PinLayerState();
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState, pins.Names, pinState: pins));
            // Every state of this layer is Write Defaults on, but none of them ever animates Blush anywhere: Unity
            // only writes a Write Defaults value for a property a layer itself touches, so being Write Defaults on
            // throughout is not enough on its own.
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx);
            var unrelated = fx.AddLayer(LayerPriority.Default, "Unrelated");
            var idle = unrelated.StateMachine.AddState("Idle", VirtualClip.Create("Idle"));
            idle.WriteDefaultValues = true;
            var otherClip = VirtualClip.Create("Other");
            otherClip.SetFloatCurve(EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"), AnimationCurve.Constant(0f, 1f / 60f, 40f));
            var other = unrelated.StateMachine.AddState("Other", otherClip);
            other.WriteDefaultValues = true;
            context.DeactivateAllExtensionContexts();

            var context2 = new BuildContext(avatar.Root, null);
            var asc2 = context2.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            AnimatorBuilder.ReleasePinsToRest(asc2, pins);
            context2.DeactivateAllExtensionContexts();

            var curve = RestCurveOf(Layer(FxOf(avatar), "AD: Face (pin)"), "Body", "blendShape.Blush");
            Assert.That(Owned(pins, "Blush"), Is.False, "Unrelated is Write Defaults on throughout but never animates Blush anywhere, so it does not own it");
            Assert.That(curve, Is.Not.Null, "Unrelated is Write Defaults on throughout but never animates Blush anywhere, so it does not own it");
            Assert.That(curve.Evaluate(0f), Is.EqualTo(22f));
        }

        [Test]
        public void ALayerControlledLayerDoesNotOwn()
        {
            PinnedFace(22f);
            var (tree, plan) = Model(menu);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            var pins = new PinLayerState();
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState, pins.Names, pinState: pins));
            // Write Defaults on throughout would otherwise be ownership on its own (AWriteDefaultsOnLayerOwns), but
            // an emote elsewhere in FX fades this very layer's weight, so it may be at nothing whenever the pin is
            // off and cannot be trusted to hold the shape.
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx);
            var faceTracking = fx.AddLayer(LayerPriority.Default, "Face Tracking");
            var clip = VirtualClip.Create("FT Idle");
            clip.SetFloatCurve(EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), AnimationCurve.Constant(0f, 1f / 60f, 40f));
            var idle = faceTracking.StateMachine.AddState("Idle", clip);
            idle.WriteDefaultValues = true;
            var emoteLayer = fx.AddLayer(LayerPriority.Default, "Emote");
            var emoteState = emoteLayer.StateMachine.AddState("Wave", VirtualClip.Create("Wave"));
            var control = ScriptableObject.CreateInstance<VRCAnimatorLayerControl>();
            control.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
            // Inside FX a control names its layer the way NDMF stores it while the animator services are open: by
            // the layer's VirtualLayerIndex, not by its position.
            control.layer = faceTracking.VirtualLayerIndex;
            emoteState.Behaviours = ImmutableList.Create<StateMachineBehaviour>(control);
            // Read in the same activation, before anything commits. ALayerControlOnAnUnrelatedLayerKeepsFaceTrackingOwnership
            // takes the same kind of control through a commit and a fresh activation.
            AnimatorBuilder.ReleasePinsToRest(asc, pins);
            context.DeactivateAllExtensionContexts();

            var curve = RestCurveOf(Layer(FxOf(avatar), "AD: Face (pin)"), "Body", "blendShape.Blush");
            Assert.That(Owned(pins, "Blush"), Is.False, "a layer control fades the face tracking layer itself, so it cannot be trusted and the pin hands the shape back itself");
            Assert.That(curve, Is.Not.Null, "a layer control fades the face tracking layer itself, so it cannot be trusted and the pin hands the shape back itself");
            Assert.That(curve.Evaluate(0f), Is.EqualTo(22f));
        }

        /// <summary>Face tracking's usual layer: one Direct tree state, Write Defaults on, with a child moving Blush, beside a disabled state with Write Defaults off and no motion.</summary>
        static VirtualLayer AddDirectFaceTracking(VirtualAnimatorController fx)
        {
            var faceTracking = fx.AddLayer(LayerPriority.Default, "Face Tracking");
            var directClip = VirtualClip.Create("Blush Track");
            directClip.SetFloatCurve(EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), AnimationCurve.Constant(0f, 1f / 60f, 40f));
            var directTree = VirtualBlendTree.Create("Direct");
            directTree.BlendType = BlendTreeType.Direct;
            directTree.Children = ImmutableList.Create(new VirtualBlendTree.VirtualChildMotion { Motion = directClip });
            var trackState = faceTracking.StateMachine.AddState("Tracking", directTree);
            trackState.WriteDefaultValues = true;
            var disabled = faceTracking.StateMachine.AddState("Disabled", null);
            disabled.WriteDefaultValues = false;
            return faceTracking;
        }

        [Test]
        public void ALayerControlOnAnUnrelatedLayerKeepsFaceTrackingOwnership()
        {
            PinnedFace(22f);
            var (tree, plan) = Model(menu);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            var pins = new PinLayerState();
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState, pins.Names, pinState: pins));
            // Modular Avatar's MMD relay, VRCFury's MMD support and full body emotes all add controls like this one:
            // it fades a third layer, never the face tracking layer, so face tracking still owns what it drives.
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx);
            AddDirectFaceTracking(fx);
            var emoteLayer = fx.AddLayer(LayerPriority.Default, "Emote");
            var emoteState = emoteLayer.StateMachine.AddState("Wave", VirtualClip.Create("Wave"));
            var target = fx.AddLayer(LayerPriority.Default, "Wave Target");
            target.StateMachine.AddState("Idle", VirtualClip.Create("Wave Target Idle"));
            var control = ScriptableObject.CreateInstance<VRCAnimatorLayerControl>();
            control.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
            control.layer = target.VirtualLayerIndex;
            emoteState.Behaviours = ImmutableList.Create<StateMachineBehaviour>(control);
            context.DeactivateAllExtensionContexts();

            // Committed, the control holds the target's position; the final pass opens a fresh activation, where
            // NDMF turns that position back into a virtual layer reference.
            var committed = FxOf(avatar);
            var committedControl = State(Layer(committed, "Emote"), "Wave").behaviours.OfType<VRCAnimatorLayerControl>().Single();
            Assert.That(committed.layers[committedControl.layer].name, Is.EqualTo("Wave Target"), "the control really aims at the third layer");

            var context2 = new BuildContext(avatar.Root, null);
            var asc2 = context2.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            AnimatorBuilder.ReleasePinsToRest(asc2, pins);
            context2.DeactivateAllExtensionContexts();

            Assert.That(Owned(pins, "Blush"), Is.True, "the control fades another layer, so face tracking still owns Blush");
            Assert.That(RestCurveOf(Layer(FxOf(avatar), "AD: Face (pin)"), "Body", "blendShape.Blush"), Is.Not.Null, "and the pin still rests Blush once on its way out, so a copy of the avatar where the owner stands still never keeps the pinned value");
        }

        [TestCase(true, TestName = "ALayerControlInActionOnTheFaceTrackingLayerTakesItsOwnership")]
        [TestCase(false, TestName = "ALayerControlInActionOnAnotherLayerLeavesFaceTrackingItsOwnership")]
        public void ALayerControlInAnotherControllerNamesItsLayerByPosition(bool aimAtFaceTracking)
        {
            PinnedFace(22f);
            var (tree, plan) = Model(menu);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            var pins = new PinLayerState();
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState, pins.Names, pinState: pins));
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx);
            AddDirectFaceTracking(fx);
            var other = fx.AddLayer(LayerPriority.Default, "Other");
            other.StateMachine.AddState("Idle", VirtualClip.Create("Other Idle"));
            context.DeactivateAllExtensionContexts();

            // A control in the Action controller that fades an FX layer: NDMF only turns a control into a virtual
            // layer reference inside the controller its playable names, so this one keeps the FX position.
            var layers = FxOf(avatar).layers;
            var action = new AnimatorController { name = "Action" };
            AnimatorStateMachine emote = null;
            AnimatorState wave = null;
            VRCAnimatorLayerControl control = null;
            try
            {
                action.AddLayer("Emote");
                emote = action.layers[0].stateMachine;
                wave = emote.AddState("Wave");
                control = wave.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
                control.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
                control.layer = System.Array.FindIndex(layers, l => l.name == (aimAtFaceTracking ? "Face Tracking" : "Other"));
                SetController(avatar, VRCAvatarDescriptor.AnimLayerType.Action, action);

                var context2 = new BuildContext(avatar.Root, null);
                var asc2 = context2.ActivateExtensionContextRecursive<AnimatorServicesContext>();
                AnimatorBuilder.ReleasePinsToRest(asc2, pins);
                context2.DeactivateAllExtensionContexts();

                var curve = RestCurveOf(Layer(FxOf(avatar), "AD: Face (pin)"), "Body", "blendShape.Blush");
                if (aimAtFaceTracking)
                {
                    Assert.That(Owned(pins, "Blush"), Is.False, "the Action control fades the face tracking layer itself, so the pin hands Blush back itself");
                    Assert.That(curve, Is.Not.Null, "the Action control fades the face tracking layer itself, so the pin hands Blush back itself");
                    Assert.That(curve.Evaluate(0f), Is.EqualTo(22f));
                }
                else
                {
                    Assert.That(Owned(pins, "Blush"), Is.True, "the Action control fades another FX layer, so face tracking still owns Blush");
                    Assert.That(curve, Is.Not.Null, "and the pin still rests Blush once on its way out, so a copy of the avatar where the owner stands still never keeps the pinned value");
                }
            }
            finally
            {
                ClearUndoOf(control, wave, emote, action);
                Object.DestroyImmediate(action);
            }
        }

        /// <summary>Takes the Undo records Unity's animator calls left for these objects off the shared Undo stack, before the objects are destroyed. A destroyed one is skipped.</summary>
        static void ClearUndoOf(params Object[] made)
        {
            foreach (var obj in made)
                if (obj != null) Undo.ClearUndo(obj);
        }

        static void SetController(TestAvatar a, VRCAvatarDescriptor.AnimLayerType type, RuntimeAnimatorController controller)
        {
            var layers = a.Descriptor.baseAnimationLayers;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].type != type) continue;
                layers[i].isDefault = false;
                layers[i].animatorController = controller;
            }
            a.Descriptor.baseAnimationLayers = layers;
        }

        static AnimatorController ControllerOf(TestAvatar a, VRCAvatarDescriptor.AnimLayerType type)
            => (AnimatorController)a.Descriptor.baseAnimationLayers.First(l => l.type == type).animatorController;

        static IEnumerable<AnimatorState> AllStates(AnimatorStateMachine machine)
        {
            foreach (var child in machine.states) yield return child.state;
            foreach (var child in machine.stateMachines)
                foreach (var state in AllStates(child.stateMachine))
                    yield return state;
        }

        [Test]
        public void APinLayerVrcFuryTurnedToWriteDefaultsOnIsTurnedBackOff()
        {
            using (var owned = TestAvatar.Create(writeDefaultsOff: false))
            {
                var ownedMenu = owned.AddMenu();
                var face = ownedMenu.AddEntry(EntryKind.Toggle, "Face");
                face.blendShapes.Add(new BlendShapeTarget { renderer = owned.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });

                var context = new BuildContext(owned.Root, null);
                context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
                var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
                context.DeactivateAllExtensionContexts();
                Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error), Is.Empty);

                // What VRCFury's Write Defaults fixer hands on between the two phases: a controller of its own in
                // which every state of every layer is Write Defaults on, AvatarDoc's pin layer included.
                var committed = ControllerOf(owned, VRCAvatarDescriptor.AnimLayerType.FX);
                var forced = new AnimatorController { name = committed.name, parameters = committed.parameters, layers = committed.layers };
                try
                {
                    foreach (var layer in forced.layers)
                        foreach (var state in AllStates(layer.stateMachine))
                            state.writeDefaultValues = true;
                    SetController(owned, VRCAvatarDescriptor.AnimLayerType.FX, forced);

                    AvatarDoctorPlugin.FinishAfterOtherTools(context);

                    var fx = ControllerOf(owned, VRCAvatarDescriptor.AnimLayerType.FX);
                    var pinStates = AllStates(Layer(fx, "AD: Face (pin)").stateMachine).ToList();
                    Assert.That(pinStates, Is.Not.Empty);
                    foreach (var state in pinStates)
                        Assert.That(state.writeDefaultValues, Is.False,
                            state.name + ": a pin state with Write Defaults on writes the shape's default every frame from the last FX layer, which freezes face tracking whenever the toggle is off");
                    Assert.That(AllStates(Layer(fx, "Base").stateMachine).All(s => s.writeDefaultValues), Is.True,
                        "the avatar's own layers are VRCFury's decision and stay as it left them");
                }
                finally
                {
                    Object.DestroyImmediate(forced);
                }
            }
        }

        [Test]
        public void AnActionLayerControlAimedAtFxStillHitsItsLayerAfterTheMove()
        {
            PinnedFace(22f);
            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error), Is.Empty);

            // VRCFury appends its own FX layer after the committed pin layer, in a controller of its own, and aims a
            // control in the Action controller at it by position.
            var committed = ControllerOf(avatar, VRCAvatarDescriptor.AnimLayerType.FX);
            var vrcFuryFx = new AnimatorController { name = committed.name, parameters = committed.parameters, layers = committed.layers };
            var action = new AnimatorController { name = "Action" };
            var made = new List<Object>();
            try
            {
                vrcFuryFx.AddLayer("[VF1] Thing");
                var thing = vrcFuryFx.layers[vrcFuryFx.layers.Length - 1].stateMachine;
                made.Add(thing);
                made.Add(thing.AddState("Idle"));
                SetController(avatar, VRCAvatarDescriptor.AnimLayerType.FX, vrcFuryFx);
                Assert.That(vrcFuryFx.layers.Select(l => l.name), Is.EqualTo(new[] { "Base", "AD: Face (pin)", "[VF1] Thing" }), "the VRCFury layer starts after the pin layer");

                action.AddLayer("Emote");
                var emote = action.layers[0].stateMachine;
                made.Add(emote);
                var wave = emote.AddState("Wave");
                made.Add(wave);
                var control = wave.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
                made.Add(control);
                control.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
                control.layer = 2;
                SetController(avatar, VRCAvatarDescriptor.AnimLayerType.Action, action);

                AvatarDoctorPlugin.FinishAfterOtherTools(context);

                var fx = ControllerOf(avatar, VRCAvatarDescriptor.AnimLayerType.FX);
                Assert.That(fx.layers.Last().name, Is.EqualTo("AD: Face (pin)"), "the final pass really moved the pin layer past the VRCFury layer");
                var committedAction = ControllerOf(avatar, VRCAvatarDescriptor.AnimLayerType.Action);
                var aimed = AllStates(committedAction.layers[0].stateMachine).SelectMany(s => s.behaviours).OfType<VRCAnimatorLayerControl>().Single();
                Assert.That(fx.layers[aimed.layer].name, Is.EqualTo("[VF1] Thing"), "the control still fades the layer it was written for, not whichever layer now sits at its old position");
            }
            finally
            {
                made.Add(vrcFuryFx);
                made.Add(action);
                ClearUndoOf(made.ToArray());
                Object.DestroyImmediate(vrcFuryFx);
                Object.DestroyImmediate(action);
            }
        }

#if DOCME_MA
        [Test]
        public void ModularAvatarsMmdRelayDoesNotFreezeFaceTracking()
        {
            PinnedFace(22f);
            var made = new List<Object> { avatar.Fx };
            // Two layers after Base, so the first three FX layers MA's MMD relay watches are the avatar's own, and
            // face tracking sits after them, where a VRCFT controller merged in by Modular Avatar lands.
            foreach (var name in new[] { "Filler A", "Filler B" })
            {
                avatar.Fx.AddLayer(name);
                var filler = avatar.Fx.layers[avatar.Fx.layers.Length - 1];
                made.Add(filler.stateMachine);
                made.Add(filler.stateMachine.AddState("Idle"));
            }
            avatar.Fx.AddParameter("FT/Blush", AnimatorControllerParameterType.Float);
            var trackClip = TestAvatar.ShapeClip("FT Blush", "Body", "Blush", 40f, 40f);
            var trackTree = new BlendTree { name = "FT", blendType = BlendTreeType.Direct };
            trackTree.children = new[] { new ChildMotion { motion = trackClip, directBlendParameter = "FT/Blush", timeScale = 1f } };
            avatar.Fx.AddLayer("Face Tracking");
            var faceTracking = avatar.Fx.layers[avatar.Fx.layers.Length - 1];
            var tracking = faceTracking.stateMachine.AddState("Tracking");
            tracking.motion = trackTree;
            tracking.writeDefaultValues = true;
            var disabled = faceTracking.stateMachine.AddState("Disabled");
            disabled.writeDefaultValues = false;
            made.Add(faceTracking.stateMachine);
            made.Add(tracking);
            made.Add(disabled);
            faceTracking.stateMachine.defaultState = tracking;
            var layers = avatar.Fx.layers;
            foreach (var layer in layers) layer.defaultWeight = 1f;
            avatar.Fx.layers = layers;

            // Any Merge Blend Tree gimmick: its layer goes in at the very top, which pushes the avatar's first three
            // FX layers down, so the MMD relay adds a layer full of FX layer controls for them.
            var gimmick = new GameObject("Gimmick");
            gimmick.transform.SetParent(avatar.Root.transform);
            var gimmickClip = TestAvatar.ShapeClip("Gimmick", "Body", "Eye_Big", 0f, 0f);
            var merge = gimmick.AddComponent<nadena.dev.modular_avatar.core.ModularAvatarMergeBlendTree>();
            merge.Motion = gimmickClip;
            merge.PathMode = nadena.dev.modular_avatar.core.MergeAnimatorPathMode.Absolute;

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                var descriptor = processed.GetComponent<VRCAvatarDescriptor>();
                var fx = (AnimatorController)descriptor.baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
                var relay = fx.layers.Where(l => l.name == "Modular Avatar: MMD Control").ToList();
                Assert.That(relay.Count, Is.EqualTo(1), "Modular Avatar added its MMD relay layer");
                Assert.That(AllStates(relay[0].stateMachine).SelectMany(s => s.behaviours).OfType<VRCAnimatorLayerControl>()
                    .Any(c => c.playable == VRC_AnimatorLayerControl.BlendableLayer.FX), Is.True, "and the relay fades FX layers");

                Assert.That(AnimatorBuilder.LastOwnedByAnotherLayer.Contains(EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blush")), Is.True, "the relay fades the avatar's first layers, never face tracking, so face tracking still owns Blush");
                Assert.That(RestCurveOf(Layer(fx, "AD: Face (pin)"), "Body", "blendShape.Blush"), Is.Not.Null, "and the pin still rests Blush once on its way out, so a copy of the avatar where the owner stands still never keeps the pinned value");
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                ClearUndoOf(made.ToArray());
                Object.DestroyImmediate(trackTree);
                Object.DestroyImmediate(trackClip);
                Object.DestroyImmediate(gimmickClip);
            }
        }
#endif

        [Test]
        public void ADirectBlendTreeLayerOwnsTheShapesItTracks()
        {
            PinnedFace(22f);
            var (tree, plan) = Model(menu);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            var pins = new PinLayerState();
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState, pins.Names, pinState: pins));
            // Face tracking's usual shape: one Direct tree state, Write Defaults on, with a child moving Blush,
            // beside a disabled state with Write Defaults off and no motion at all. Neither of the other two
            // clauses would ever call this an owner: not every state is Write Defaults on, and the disabled
            // state's null motion fails the leaf-clip clause outright.
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx);
            var faceTracking = fx.AddLayer(LayerPriority.Default, "Face Tracking");
            var directClip = VirtualClip.Create("Blush Track");
            directClip.SetFloatCurve(EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), AnimationCurve.Constant(0f, 1f / 60f, 40f));
            var directTree = VirtualBlendTree.Create("Direct");
            directTree.BlendType = BlendTreeType.Direct;
            directTree.Children = ImmutableList.Create(new VirtualBlendTree.VirtualChildMotion { Motion = directClip });
            var trackState = faceTracking.StateMachine.AddState("Tracking", directTree);
            trackState.WriteDefaultValues = true;
            var disabled = faceTracking.StateMachine.AddState("Disabled", null);
            disabled.WriteDefaultValues = false;
            context.DeactivateAllExtensionContexts();

            var context2 = new BuildContext(avatar.Root, null);
            var asc2 = context2.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            AnimatorBuilder.ReleasePinsToRest(asc2, pins);
            context2.DeactivateAllExtensionContexts();

            Assert.That(Owned(pins, "Blush"), Is.True, "the Direct tree's child drives Blush, so face tracking owns it");
            Assert.That(RestCurveOf(Layer(FxOf(avatar), "AD: Face (pin)"), "Body", "blendShape.Blush"), Is.Not.Null, "and the pin still rests Blush once on its way out, so a copy of the avatar where the owner stands still never keeps the pinned value");
        }

        [Test]
        public void ADirectBlendTreeThatDoesNotDriveTheShapeDoesNotOwnIt()
        {
            PinnedFace(22f);
            var (tree, plan) = Model(menu);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            var pins = new PinLayerState();
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState, pins.Names, pinState: pins));
            // Same shape as above, but the Direct tree's only child moves a different shape: the third clause asks
            // for a child that carries THIS binding, not merely a Direct tree existing somewhere in the layer.
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx);
            var faceTracking = fx.AddLayer(LayerPriority.Default, "Face Tracking");
            var otherClip = VirtualClip.Create("EyeBig Track");
            otherClip.SetFloatCurve(EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"), AnimationCurve.Constant(0f, 1f / 60f, 40f));
            var directTree = VirtualBlendTree.Create("Direct");
            directTree.BlendType = BlendTreeType.Direct;
            directTree.Children = ImmutableList.Create(new VirtualBlendTree.VirtualChildMotion { Motion = otherClip });
            var trackState = faceTracking.StateMachine.AddState("Tracking", directTree);
            trackState.WriteDefaultValues = true;
            var disabled = faceTracking.StateMachine.AddState("Disabled", null);
            disabled.WriteDefaultValues = false;
            context.DeactivateAllExtensionContexts();

            var context2 = new BuildContext(avatar.Root, null);
            var asc2 = context2.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            AnimatorBuilder.ReleasePinsToRest(asc2, pins);
            context2.DeactivateAllExtensionContexts();

            var curve = RestCurveOf(Layer(FxOf(avatar), "AD: Face (pin)"), "Body", "blendShape.Blush");
            Assert.That(Owned(pins, "Blush"), Is.False, "the Direct tree drives a different shape, so it does not own Blush and the pin still has to hand it back itself");
            Assert.That(curve, Is.Not.Null, "the Direct tree drives a different shape, so it does not own Blush and the pin still has to hand it back itself");
            Assert.That(curve.Evaluate(0f), Is.EqualTo(22f));
        }

        [Test]
        public void ANestedDirectBlendTreeOwnsTheShapeItTracks()
        {
            PinnedFace(22f);
            var (tree, plan) = Model(menu);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            var pins = new PinLayerState();
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState, pins.Names, pinState: pins));
            // A Direct tree does not have to sit directly on the state: here it is one child of an ordinary 1D
            // tree, one level deeper. The search has to keep looking past a non-Direct root to find it.
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx);
            var faceTracking = fx.AddLayer(LayerPriority.Default, "Face Tracking");
            var directClip = VirtualClip.Create("Blush Track");
            directClip.SetFloatCurve(EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), AnimationCurve.Constant(0f, 1f / 60f, 40f));
            var innerDirect = VirtualBlendTree.Create("Inner Direct");
            innerDirect.BlendType = BlendTreeType.Direct;
            innerDirect.Children = ImmutableList.Create(new VirtualBlendTree.VirtualChildMotion { Motion = directClip });
            var outer1D = VirtualBlendTree.Create("Outer");
            outer1D.BlendType = BlendTreeType.Simple1D;
            outer1D.BlendParameter = "Existing";
            outer1D.Children = ImmutableList.Create(new VirtualBlendTree.VirtualChildMotion { Motion = innerDirect, Threshold = 0f });
            var trackState = faceTracking.StateMachine.AddState("Tracking", outer1D);
            trackState.WriteDefaultValues = true;
            var disabled = faceTracking.StateMachine.AddState("Disabled", null);
            disabled.WriteDefaultValues = false;
            context.DeactivateAllExtensionContexts();

            var context2 = new BuildContext(avatar.Root, null);
            var asc2 = context2.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            AnimatorBuilder.ReleasePinsToRest(asc2, pins);
            context2.DeactivateAllExtensionContexts();

            Assert.That(Owned(pins, "Blush"), Is.True, "a Direct tree nested one level inside an ordinary blend tree still owns the shapes its own children drive");
            Assert.That(RestCurveOf(Layer(FxOf(avatar), "AD: Face (pin)"), "Body", "blendShape.Blush"), Is.Not.Null, "and the pin still rests Blush once on its way out, so a copy of the avatar where the owner stands still never keeps the pinned value");
        }

        [Test]
        public void SwitchingGroupMembersReleasesTheOldMembersPin()
        {
            var a = menu.AddEntry(EntryKind.Toggle, "A");
            a.exclusiveGroup = "G";
            a.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            var b = menu.AddEntry(EntryKind.Toggle, "B");
            b.exclusiveGroup = "G";
            b.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 100f, mode = ShapeEffectMode.Pin });
            avatar.Body.SetBlendShapeWeight(0, 5f);

            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error), Is.Empty);

            AvatarDoctorPlugin.FinishAfterOtherTools(context);

            var layer = Layer(FxOf(avatar), "AD: G (pin)");
            var aState = State(layer, "A");
            var bState = State(layer, "B");
            var rest = RestCurveOf(layer, "Body", "blendShape.Blush");
            Assert.That(rest, Is.Not.Null, "Reset releases A's pin");
            Assert.That(rest.Evaluate(0f), Is.EqualTo(5f));
            Assert.That(CurveOf(bState.motion, "Body", "blendShape.Blush"), Is.Null,
                "switching from A to B passes Reset and Off, so B's own state writes only B's pins");
            Assert.That(aState.transitions.Select(t => t.destinationState.name), Is.EqualTo(new[] { "Reset" }), "A is left through Reset, so Blush never stays pinned once A is left");
            var aCurve = CurveOf(aState.motion, "Body", "blendShape.Blush");
            Assert.That(aCurve, Is.Not.Null, "A's own state still pins Blush itself");
            Assert.That(aCurve.Evaluate(0f), Is.EqualTo(100f), "A's own pinned value must not be overwritten by the rest curve");
        }
    }
}
#endif
