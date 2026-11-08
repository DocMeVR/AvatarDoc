#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using Object = UnityEngine.Object;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// A world's dance (an MMD world, a station's animation) plays under the avatar's FX. Any FX layer that is Write
    /// Defaults off and weighs more than 0 makes FX hold every shape the dance moves, so AvatarDoc's own Write
    /// Defaults off layers weigh 0 while they rest and 1 while they act.
    /// </summary>
    public class WorldDanceTests
    {
        TestAvatar avatar;
        AvatarDoc menu;
        readonly List<Object> made = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            GeneratedAssets.Note();
            // Write Defaults on, the way VRCFury's MMD support leaves the avatar's own layers: only AvatarDoc's own
            // Write Defaults off layers are left to hold the dance.
            avatar = TestAvatar.Create(writeDefaultsOff: false);
            var mesh = TestAvatar.QuadWithShapes("Blush", "Eye_Big", "Blink", "Dance");
            made.Add(mesh);
            avatar.Body.sharedMesh = mesh;
            menu = avatar.AddMenu();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in made)
                if (obj != null) Undo.ClearUndo(obj);
            avatar.Dispose();
            foreach (var obj in made)
                if (obj != null && !AssetDatabase.Contains(obj)) Object.DestroyImmediate(obj);
            made.Clear();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
            GeneratedAssets.CleanUp();
        }

        static AnimatorController FxOf(TestAvatar a)
            => (AnimatorController)a.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;

        static AnimatorControllerLayer Layer(AnimatorController fx, string name) => fx.layers.Single(l => l.name == name);
        static AnimatorState State(AnimatorControllerLayer layer, string name) => layer.stateMachine.states.Single(s => s.state.name == name).state;

        static void NoErrors(List<ErrorContext> errors)
            => Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error).Select(e => (e.TheError as SimpleError)?.TitleKey), Is.Empty);

        static string Shown(IEnumerable<float> weights) => "(" + string.Join(", ", weights) + ")";

        /// <summary>The whole build the way NDMF runs it: AvatarDoc's own pass, then the final pass the Optimizing phase runs after every other tool.</summary>
        AnimatorController BuildAll(out List<ErrorContext> errors, System.Action<VirtualAnimatorController> betweenPasses = null)
        {
            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            if (betweenPasses != null)
            {
                var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
                asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx);
                betweenPasses(fx);
                context.DeactivateAllExtensionContexts();
            }
            errors.AddRange(ErrorReport.CaptureErrors(() => AvatarDoctorPlugin.FinishAfterOtherTools(context)));
            return FxOf(avatar);
        }

        /// <summary>A dance that holds the Dance shape at 80, a shape no FX layer ever animates.</summary>
        AnimationClip Dance()
        {
            var clip = TestAvatar.ShapeClip("Dance", "Body", "Dance", 80f, 80f);
            made.Add(clip);
            return clip;
        }

        MenuEntry Headpat(string shape)
        {
            var pat = menu.AddEntry(EntryKind.Trigger, "Headpat");
            pat.source = TriggerSource.Contact;
            pat.contactAnchor = avatar.Hat.transform;
            pat.contactTags = new List<string> { "Hand" };
            pat.touchMode = TouchMode.FollowDistance;
            pat.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = shape, weight = 100f, mode = ShapeEffectMode.Pin });
            return pat;
        }

        /// <summary>The parameter that leaves this state, read from its single way out.</summary>
        static string Leaving(AnimatorControllerLayer layer, string state) => State(layer, state).transitions.First().conditions.Single().parameter;

        // ---- played back ----------------------------------------------------------------------------------------

        [TestCase(20f, 0)]
        [TestCase(60f, 0)]
        [TestCase(144f, 0)]
        [TestCase(60f, 1)]
        [TestCase(144f, 1)]
        public void AHeadpatAtRestLetsTheDanceMoveTheFace(float framesPerSecond, int controlDelay)
        {
            var pat = Headpat("Blush");

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var contact = TouchBuilder.ParameterFor(pat, "AD");
            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond, controlDelay, Dance()))
            {
                var resting = player.Settled("Dance");
                Assert.That(resting.Last(), Is.EqualTo(80f).Within(0.01f), "no hand near: the dance reaches the face " + Shown(resting));

                player.Set(contact, 1f);
                var patted = player.Settled("Blush");
                Assert.That(patted.Last(), Is.EqualTo(100f).Within(0.01f), "a hand near: the headpat's own shape " + Shown(patted));

                player.Set(contact, 0f);
                var after = player.Settled("Dance");
                Assert.That(after.Last(), Is.EqualTo(80f).Within(0.01f), "after the pat the dance has the face again " + Shown(after));
                Assert.That(player.Weight("Blush"), Is.EqualTo(0f).Within(0.01f), "and the headpat's shape is back at rest");
            }
        }

        [TestCase(60f, 0)]
        [TestCase(144f, 1)]
        public void AHeadpatAtRestLetsTheDanceMoveTheShapeItPins(float framesPerSecond, int controlDelay)
        {
            var pat = Headpat("Blush");

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var contact = TouchBuilder.ParameterFor(pat, "AD");
            var dance = TestAvatar.ShapeClip("Dance Blush", "Body", "Blush", 60f, 60f);
            made.Add(dance);
            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond, controlDelay, dance))
            {
                var resting = player.Settled("Blush");
                Assert.That(resting.Last(), Is.EqualTo(60f).Within(0.01f), "no hand near: the dance moves the shape " + Shown(resting));

                player.Set(contact, 1f);
                var patted = player.Settled("Blush");
                Assert.That(patted.Last(), Is.EqualTo(100f).Within(0.01f), "a hand near: the pin wins over the dance " + Shown(patted));

                player.Set(contact, 0f);
                var after = player.Settled("Blush");
                Assert.That(after.Last(), Is.EqualTo(60f).Within(0.01f), "and after the pat the dance has it again " + Shown(after));
            }
        }

        [TestCase(20f, 0)]
        [TestCase(60f, 0)]
        [TestCase(144f, 1)]
        public void APinToggleAtRestLetsTheDanceMoveTheFace(float framesPerSecond, int controlDelay)
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            avatar.Body.SetBlendShapeWeight(0, 22f);

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var on = Leaving(Layer(fx, "AD: Face (pin)"), "On");
            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond, controlDelay, Dance()))
            {
                var resting = player.Settled("Dance");
                Assert.That(resting.Last(), Is.EqualTo(80f).Within(0.01f), "toggle off: the dance reaches the face " + Shown(resting));

                player.Set(on, 1f);
                var held = player.Settled("Blush");
                Assert.That(held.Last(), Is.EqualTo(100f).Within(0.01f), "toggle on: the pin " + Shown(held));

                player.Set(on, 0f);
                var after = player.Settled("Dance");
                Assert.That(after.Last(), Is.EqualTo(80f).Within(0.01f), "off again: the dance " + Shown(after));
                Assert.That(player.Weight("Blush"), Is.EqualTo(22f).Within(0.01f), "and the pinned shape at rest");
            }
        }

        [TestCase(60f, 0)]
        [TestCase(144f, 1)]
        public void ABlendShapeTriggerAtRestLetsTheDanceMoveTheFace(float framesPerSecond, int controlDelay)
        {
            var smile = menu.AddEntry(EntryKind.Toggle, "Smile");
            smile.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            var link = menu.AddEntry(EntryKind.Trigger, "Blush link");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Fixed, weight = 80f });
            avatar.Body.SetBlendShapeWeight(1, 7f);

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var smileOn = State(Layer(fx, "AD: Smile"), "On").transitions.Single().conditions.Single().parameter;
            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond, controlDelay, Dance()))
            {
                var resting = player.Settled("Dance");
                Assert.That(resting.Last(), Is.EqualTo(80f).Within(0.01f), "the trigger at rest: the dance " + Shown(resting));

                player.Set(smileOn, 1f);
                var driven = player.Settled("Eye_Big");
                Assert.That(driven.Last(), Is.EqualTo(80f).Within(0.01f), "Smile on drives the trigger " + Shown(driven));

                player.Set(smileOn, 0f);
                var after = player.Settled("Dance");
                Assert.That(after.Last(), Is.EqualTo(80f).Within(0.01f), "back at rest: the dance " + Shown(after));
                Assert.That(player.Weight("Eye_Big"), Is.EqualTo(7f).Within(0.01f), "and the trigger's shape at rest");
            }
        }

        [TestCase(20f, 0)]
        [TestCase(60f, 0)]
        [TestCase(60f, 1)]
        [TestCase(144f, 1)]
        public void AHeadpatsObjectStaysOffAtRestWhileTheLayerWeighsNothing(float framesPerSecond, int controlDelay)
        {
            var pat = Headpat("Blush");
            pat.objects.Add(new ObjectTarget { target = avatar.Glasses, activeWhenOn = true });
            avatar.Glasses.SetActive(false);

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var contact = TouchBuilder.ParameterFor(pat, "AD");
            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond, controlDelay))
            {
                player.Settled("Blush");
                Assert.That(avatar.Glasses.activeSelf, Is.False, "at load the object is off");
                Assert.That(player.LayerWeight("AD: Headpat (distance)"), Is.EqualTo(0f), "and the resting layer weighs nothing");

                player.Set(contact, 1f);
                player.Settled("Blush");
                Assert.That(avatar.Glasses.activeSelf, Is.True, "a hand near shows it");

                player.Set(contact, 0f);
                var after = player.Settled("Blush");
                Assert.That(avatar.Glasses.activeSelf, Is.False, "and after the pat it is off again " + Shown(after));
                player.Play(30, "Blush");
                Assert.That(avatar.Glasses.activeSelf, Is.False, "and stays off");
            }
        }

        [TestCase(60f, 0)]
        [TestCase(144f, 1)]
        public void AHeadpatWhoseObjectIsOnInTheSceneKeepsItsWeightAndTheObjectOff(float framesPerSecond, int controlDelay)
        {
            var pat = Headpat("Blush");
            pat.objects.Add(new ObjectTarget { target = avatar.Glasses, activeWhenOn = true });
            avatar.Glasses.SetActive(true);

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            Assert.That(ControlOn(State(Layer(fx, "AD: Headpat (distance)"), "Far")), Is.Null,
                "at weight 0 Unity would show the object as uploaded, on, so the layer keeps its weight");
            var contact = TouchBuilder.ParameterFor(pat, "AD");
            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond, controlDelay))
            {
                player.Settled("Blush");
                Assert.That(avatar.Glasses.activeSelf, Is.False, "at load the object is off");
                player.Set(contact, 1f);
                player.Settled("Blush");
                player.Set(contact, 0f);
                player.Settled("Blush");
                player.Play(30, "Blush");
                Assert.That(avatar.Glasses.activeSelf, Is.False, "and off again after a pat");
            }
        }

        [TestCase(60f, 0)]
        [TestCase(144f, 1)]
        public void ADefaultOnPinToggleTurnedOffLetsTheDanceMoveTheFace(float framesPerSecond, int controlDelay)
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.defaultOn = true;
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            Assert.That(fx.layers.Any(l => l.name == PinLayerState.RestLayerName), Is.True, "sanity: the rest layer rests Blush, which the uploaded avatar shows at 100");
            var on = Leaving(Layer(fx, "AD: Face (pin)"), "On");
            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond, controlDelay, Dance()))
            {
                player.Set(on, 1f);
                Assert.That(player.Settled("Blush").Last(), Is.EqualTo(100f).Within(0.01f), "loaded on: the pin holds Blush");

                player.Set(on, 0f);
                var off = player.Settled("Dance");
                Assert.That(off.Last(), Is.EqualTo(80f).Within(0.01f), "turned off: the dance reaches the face past the rest layer, which is Write Defaults on " + Shown(off));
                Assert.That(player.Weight("Blush"), Is.EqualTo(0f).Within(0.01f), "and Blush is at rest");
            }
        }

        [Test]
        public void ADanceCannotMoveAShapeTheRestLayerRests()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.defaultOn = true;
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            var dance = TestAvatar.ShapeClip("Dance Blush", "Body", "Blush", 80f, 80f);
            made.Add(dance);

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var on = Leaving(Layer(fx, "AD: Face (pin)"), "On");
            Assert.That(fx.layers.Any(l => l.name == PinLayerState.RestLayerName), Is.True, "sanity: the rest layer rests Blush");
            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / 60f, 0, dance))
            {
                player.Set(on, 0f);
                var off = player.Settled("Blush");
                Assert.That(player.LayerWeight("AD: Face (pin)"), Is.EqualTo(0f), "sanity: the pin layer rests at weight 0, so only the rest layer holds Blush");
                Assert.That(off.Last(), Is.EqualTo(0f).Within(0.01f), "the known cost: while the toggle is off, its held shape stays at rest under a dance " + Shown(off));
            }
        }

        [Test]
        public void AHeadpatsOwnOffValueKeepsItsWeightBesideAToggleThatStartsOnWithTheSameShape()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.defaultOn = true;
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            var pat = Headpat("Blush");
            pat.blendShapes[0] = new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, explicitOff = true, minWeight = 30f };

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            Assert.That(fx.layers.Any(l => l.name == PinLayerState.RestLayerName), Is.True, "sanity: the rest layer rests Blush");
            Assert.That(ControlOn(State(Layer(fx, "AD: Headpat (distance)"), "Far")), Is.Null, "the headpat rests at its own 30, not at the rest layer's value, so its layer keeps its weight");
            AssertFadesWhileIdle(fx, "AD: Face (pin)", "Off", "On");
        }

        [Test]
        public void ALayerThatRestsAwayFromTheAvatarKeepsItsWeight()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.defaultOn = true;
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            var pat = Headpat("Eye_Big");
            pat.blendShapes[0] = new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 100f, explicitOff = true, minWeight = 30f };
            var plain = menu.AddEntry(EntryKind.Toggle, "Plain");
            plain.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Dance", weight = 100f, mode = ShapeEffectMode.Pin });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            Assert.That(ControlOn(State(Layer(fx, "AD: Headpat (distance)"), "Far")), Is.Null, "a row's own off value is not the avatar's");
            AssertFadesWhileIdle(fx, "AD: Plain (pin)", "Off", "On");
            AssertFadesWhileIdle(fx, "AD: Face (pin)", "Off", "On");
        }

        /// <summary>Fangs, on by default, writes Eye_Big at 100 into the avatar from its own layer, which writes the shape on every frame.</summary>
        void FangsOnByDefault()
        {
            var fangs = menu.AddEntry(EntryKind.Toggle, "Fangs");
            fangs.defaultOn = true;
            fangs.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 100f });
        }

        [TestCase(false, TestName = "ATriggerRowOnAShapeAToggleStartsOnStillLetsTheDanceThrough")]
        [TestCase(true, TestName = "ATriggerRuleOnAMeshAToggleStartsOnStillLetsTheDanceThrough")]
        public void ATriggerBesideAToggleThatStartsOnStillLetsTheDanceThrough(bool rule)
        {
            FangsOnByDefault();
            var smile = menu.AddEntry(EntryKind.Toggle, "Smile");
            smile.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            var link = menu.AddEntry(EntryKind.Trigger, "Blush link");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            if (rule) link.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true });
            else link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Fixed, weight = 30f });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            AssertFadesWhileIdle(fx, "AD: Blush link (shape)", "Far", "Near");
            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / 60f, 0, Dance()))
            {
                var resting = player.Settled("Dance");
                Assert.That(resting.Last(), Is.EqualTo(80f).Within(0.01f), "the trigger at rest: the dance reaches the face " + Shown(resting));
                Assert.That(player.Weight("Eye_Big"), Is.EqualTo(100f).Within(0.01f), "and Fangs, on, keeps its shape");
            }
        }

        [Test]
        public void APinToggleOnAShapeAToggleStartsOnStillFades()
        {
            FangsOnByDefault();
            var clear = menu.AddEntry(EntryKind.Toggle, "Clear");
            clear.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            AssertFadesWhileIdle(fx, "AD: Clear (pin)", "Off", "On");
        }

        [Test]
        public void AHeadpatsOwnOffValueStaysBesideFaceTrackingThatIsOff()
        {
            // Face tracking the way a template builds it: Disabled writes nothing, Tracking holds a Direct tree. It
            // counts as moving Blush, but while tracking is off it writes nothing, so the off value has to hold.
            const string parameter = "FT/Blush";
            avatar.Fx.AddParameter(new AnimatorControllerParameter { name = parameter, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            var clip = TestAvatar.ShapeClip("FT Blush", "Body", "Blush", 70f, 70f);
            made.Add(clip);
            var tree = new BlendTree { name = "FT Blush", blendType = BlendTreeType.Direct };
            made.Add(tree);
            tree.children = new[] { new ChildMotion { motion = clip, directBlendParameter = parameter, timeScale = 1f } };
            avatar.Fx.AddLayer("FT Blush");
            var machine = avatar.Fx.layers[avatar.Fx.layers.Length - 1].stateMachine;
            var disabled = machine.AddState("Disabled");
            disabled.writeDefaultValues = false;
            var tracking = machine.AddState("Tracking");
            tracking.motion = tree;
            tracking.writeDefaultValues = true;
            machine.defaultState = disabled;
            made.Add(machine);
            var layers = avatar.Fx.layers;
            foreach (var layer in layers) layer.defaultWeight = 1f;
            avatar.Fx.layers = layers;
            var pat = Headpat("Blush");
            pat.blendShapes[0] = new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, explicitOff = true, minWeight = 30f };

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            Assert.That(AnimatorBuilder.LastOwnedByAnotherLayer.Contains(EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blush")), Is.True,
                "sanity: the face tracking layer counts as moving Blush");
            Assert.That(ControlOn(State(Layer(fx, "AD: Headpat (distance)"), "Far")), Is.Null, "it may write nothing at all, so the headpat's own off value keeps the layer's weight");
            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body))
            {
                var resting = player.Settled("Blush");
                Assert.That(player.Play(30, "Blush").Last(), Is.EqualTo(30f).Within(0.01f), "tracking off, no hand near: the off value stays " + Shown(resting));
            }
        }

        void TrackBlink()
        {
            avatar.Descriptor.enableEyeLook = true;
            avatar.Descriptor.customEyeLookSettings.eyelidType = VRCAvatarDescriptor.EyelidType.Blendshapes;
            avatar.Descriptor.customEyeLookSettings.eyelidsSkinnedMesh = avatar.Body;
            avatar.Descriptor.customEyeLookSettings.eyelidsBlendshapes = new[] { avatar.Body.sharedMesh.GetBlendShapeIndex("Blink"), -1, -1 };
        }

        [TestCase(60f, 0)]
        [TestCase(144f, 1)]
        public void AToggleThatHoldsTheBlinkReleasesItsPauseFlagWhileTheLayerWeighsNothing(float framesPerSecond, int controlDelay)
        {
            TrackBlink();
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true, value = ShapeSetValue.Default, holdBlink = true });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var pause = Layer(fx, "AD: Blink pause");
            var flag = State(pause, "Idle").transitions.Single().conditions.Single().parameter;
            var on = Leaving(Layer(fx, "AD: Face (pin)"), "On");
            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond, controlDelay, Dance()))
            {
                var resting = player.Settled("Dance");
                Assert.That(player.Float(flag), Is.EqualTo(0f), "the flag is down at rest");
                Assert.That(resting.Last(), Is.EqualTo(80f).Within(0.01f), "and the dance reaches the face " + Shown(resting));

                player.Set(on, 1f);
                player.Settled("Blink");
                Assert.That(player.Float(flag), Is.EqualTo(1f), "the hold raises the flag");

                player.Set(on, 0f);
                var after = player.Settled("Dance");
                Assert.That(player.Float(flag), Is.EqualTo(0f), "leaving the hold lowers it again, though the layer weighs nothing by then");
                Assert.That(after.Last(), Is.EqualTo(80f).Within(0.01f), "and the dance has the face " + Shown(after));
            }
        }

        // ---- the layers as built --------------------------------------------------------------------------------

        static VRCAnimatorLayerControl ControlOn(AnimatorState state)
            => state.behaviours.OfType<VRCAnimatorLayerControl>().SingleOrDefault();

        /// <summary>
        /// The layer weighs 1 when the avatar loads, its idle state fades it to 0 and every active state back to 1,
        /// each aimed at the layer itself in FX with no blend time. Reset and Load set nothing, so they play at the 1
        /// the way in left behind.
        /// </summary>
        static void AssertFadesWhileIdle(AnimatorController fx, string layerName, string idleName, params string[] activeNames)
        {
            int index = System.Array.FindIndex(fx.layers, l => l.name == layerName);
            var layer = fx.layers[index];
            Assert.That(layer.defaultWeight, Is.EqualTo(1f), layerName + ": loads at full weight, so Load and a toggle that starts on play at once");
            void AssertControl(AnimatorState state, float goal)
            {
                var control = ControlOn(state);
                Assert.That(control, Is.Not.Null, layerName + " " + state.name + ": sets the layer's weight");
                Assert.That(control.playable, Is.EqualTo(VRC_AnimatorLayerControl.BlendableLayer.FX));
                Assert.That(control.layer, Is.EqualTo(index), layerName + " " + state.name + ": aimed at the layer itself where it ends up");
                Assert.That(control.goalWeight, Is.EqualTo(goal), layerName + " " + state.name);
                Assert.That(control.blendDuration, Is.EqualTo(0f));
            }
            var idle = State(layer, idleName);
            AssertControl(idle, 0f);
            var refresh = idle.transitions.SingleOrDefault(t => t.destinationState == idle);
            Assert.That(refresh, Is.Not.Null, layerName + ": idle enters itself again, so its 0 is set again after VRChat ignored it at load");
            Assert.That(refresh.conditions, Is.Empty);
            Assert.That(refresh.hasExitTime, Is.True);
            Assert.That(refresh.exitTime * ((AnimationClip)idle.motion).length, Is.EqualTo(AnimatorBuilder.IdleRefreshSeconds).Within(0.01f), layerName + ": every few seconds");
            Assert.That(idle.transitions.Last(), Is.SameAs(refresh), "after every way into the active side");
            foreach (var active in activeNames) AssertControl(State(layer, active), 1f);
            foreach (var child in layer.stateMachine.states)
                if (child.state.name == "Reset" || child.state.name == "Load")
                    Assert.That(ControlOn(child.state), Is.Null, layerName + " " + child.state.name + ": plays at the weight it was entered with");
        }

        [Test]
        public void EveryWriteDefaultsOffLayerAvatarDocBuildsFadesWhileIdle()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            Headpat("Eye_Big");
            var link = menu.AddEntry(EntryKind.Trigger, "Blush link");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Dance", mode = ShapeEffectMode.Fixed, weight = 80f });

            // A layer another tool adds after AvatarDoc's own pass, which the final pass then moves the pin layers past.
            var fx = BuildAll(out var errors, between => between.AddLayer(LayerPriority.Default, "Late layer"));

            NoErrors(errors);
            Assert.That(fx.layers.Last().name, Is.Not.EqualTo("Late layer"), "sanity: the pin layers were moved after it");
            AssertFadesWhileIdle(fx, "AD: Face (pin)", "Off", "On");
            AssertFadesWhileIdle(fx, "AD: Headpat (distance)", "Far", "Near");
            AssertFadesWhileIdle(fx, "AD: Blush link (shape)", "Far", "Near");
        }

        [Test]
        public void ThePausePartsOnlyLayerWeighsNothingAndStillPauses()
        {
            TrackBlink();
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true, value = ShapeSetValue.Default, holdBlink = true });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var pause = Layer(fx, "AD: Blink pause");
            Assert.That(pause.defaultWeight, Is.EqualTo(0f), "it animates nothing, so it never needs weight: its tracking controls run at 0 too");
            Assert.That(pause.stateMachine.states.SelectMany(s => s.state.behaviours).OfType<VRCAnimatorLayerControl>(), Is.Empty);
            Assert.That(State(pause, "Paused").behaviours.OfType<VRCAnimatorTrackingControl>().Single().trackingEyes,
                Is.EqualTo(VRC_AnimatorTrackingControl.TrackingType.Animation), "Paused still hands the eyes to animation");
        }

        [Test]
        public void AnAvatarsOwnLayerControlStillFindsItsLayerAfterTheFade()
        {
            Headpat("Blush");
            // A layer of the avatar's own that a control in Action fades, the way an emote turns face tracking off.
            avatar.Fx.AddLayer("Tracking");
            int tracking = avatar.Fx.layers.Length - 1;
            var action = new AnimatorController { name = "Action" };
            made.Add(action);
            action.AddLayer("Emote");
            var wave = action.layers[0].stateMachine.AddState("Wave");
            var control = wave.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
            control.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
            control.layer = tracking;
            var layers = avatar.Descriptor.baseAnimationLayers;
            for (int i = 0; i < layers.Length; i++)
                if (layers[i].type == VRCAvatarDescriptor.AnimLayerType.Action) { layers[i].isDefault = false; layers[i].animatorController = action; }
            avatar.Descriptor.baseAnimationLayers = layers;

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var committed = (AnimatorController)avatar.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.Action).animatorController;
            var aimed = committed.layers[0].stateMachine.states.Single().state.behaviours.OfType<VRCAnimatorLayerControl>().Single();
            Assert.That(fx.layers[aimed.layer].name, Is.EqualTo("Tracking"));
            AssertFadesWhileIdle(fx, "AD: Headpat (distance)", "Far", "Near");
        }
    }
}
#endif
