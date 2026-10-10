#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Plan 15 task R: a toggle or a trigger holds every other shape of a mesh at its default or at 0 while it is on,
    /// with the descriptor's blink and viseme shapes left free unless the rule holds them too.
    /// </summary>
    public class HoldTheRestTests
    {
        TestAvatar avatar;
        AvatarDoc menu;

        [SetUp]
        public void SetUp()
        {
            GeneratedAssets.Note();
            avatar = TestAvatar.Create();
            avatar.AddBodyShapes("Smile", "Blink", "vrc.v_aa");
            menu = avatar.AddMenu();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            AvatarObjectPicker.ShowOverride = null;
            avatar.Dispose();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
            GeneratedAssets.CleanUp();
        }

        static AnimatorController FxOf(TestAvatar a)
            => (AnimatorController)a.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;

        static AnimatorControllerLayer Layer(AnimatorController fx, string name) => fx.layers.Single(l => l.name == name);
        static AnimatorState State(AnimatorControllerLayer layer, string name) => layer.stateMachine.states.Single(s => s.state.name == name).state;

        static AnimationCurve Shape(Motion motion, string shape)
            => AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape." + shape));

        static float Weight(Motion motion, string shape)
        {
            var curve = Shape(motion, shape);
            Assert.That(curve, Is.Not.Null, "a curve for " + shape + " in " + motion.name);
            return curve.Evaluate(0f);
        }

        static AnimationCurve Parameter(Motion motion, string parameter)
            => AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve("", typeof(Animator), parameter));

        void SetWeight(string shape, float weight) => avatar.Body.SetBlendShapeWeight(avatar.Mesh.GetBlendShapeIndex(shape), weight);

        int Index(string shape) => avatar.Mesh.GetBlendShapeIndex(shape);

        /// <summary>The descriptor blinks with Blink and speaks with vrc.v_aa, both on Body.</summary>
        void TrackBlinkAndVisemes()
        {
            avatar.Descriptor.enableEyeLook = true;
            avatar.Descriptor.customEyeLookSettings.eyelidType = VRCAvatarDescriptor.EyelidType.Blendshapes;
            avatar.Descriptor.customEyeLookSettings.eyelidsSkinnedMesh = avatar.Body;
            avatar.Descriptor.customEyeLookSettings.eyelidsBlendshapes = new[] { Index("Blink"), -1, -1 };
            avatar.Descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape;
            avatar.Descriptor.VisemeSkinnedMesh = avatar.Body;
            avatar.Descriptor.VisemeBlendShapes = new string[15];
            avatar.Descriptor.VisemeBlendShapes[1] = "vrc.v_aa";
        }

        ShapeSetRule Rule(ShapeSetValue value, bool holdTracked = false) => new ShapeSetRule { mesh = avatar.Body, zeroOthers = true, value = value, holdTracked = holdTracked };

        MenuEntry HatToggle()
        {
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            return hat;
        }

        /// <summary>Model, defaults and the toggle layers inside an NDMF context, without the final pass; returns the committed FX controller.</summary>
        AnimatorController BuildToggles(out List<ErrorContext> errors)
        {
            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
            var plan = ParameterPlanner.Plan(tree, new Dictionary<string, PlannedType> { { "Existing", PlannedType.Bool } });
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            errors = ErrorReport.CaptureErrors(() => L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState)));
            context.DeactivateAllExtensionContexts();
            return FxOf(avatar);
        }

        /// <summary>The whole build the way NDMF runs it: AvatarDoc's own pass, then, when asked, the final pass the Optimizing phase runs after every other tool.</summary>
        AnimatorController BuildAll(out List<ErrorContext> errors, bool finalPass = true)
        {
            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            if (finalPass) errors.AddRange(ErrorReport.CaptureErrors(() => AvatarDoctorPlugin.FinishAfterOtherTools(context)));
            return FxOf(avatar);
        }

        static void NoErrors(List<ErrorContext> errors)
            => Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error).Select(e => (e.TheError as SimpleError)?.TitleKey), Is.Empty);

        AvatarDoctorView View(AvatarDoc doc, bool simple = false)
        {
            var prefs = new MemoryViewPrefs();
            if (simple) ViewModes.Save(prefs, ViewMode.Simple);
            return new AvatarDoctorView(new SerializedObject(doc), doc, false, prefs);
        }

        // ---- data ----------------------------------------------------------------------------------------------

        [Test]
        public void AStoredRuleLoadsAsZeroWithBlinkAndVisemesFree()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            // Switched off, so a loaded "off" can only have come from the stored data, never from the field's own
            // initialiser.
            face.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = false, value = ShapeSetValue.Default, holdTracked = true });
            var json = EditorJsonUtility.ToJson(menu);
            // The rule exactly as a scene saved it before the value and the blink and viseme switch existed.
            var stored = json.Replace(",\"value\":1,\"holdTracked\":true", "");
            Assert.That(stored, Is.Not.EqualTo(json), "sanity: the stored form really lacks both fields");

            var loaded = avatar.Glasses.AddComponent<AvatarDoc>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(stored, loaded);

                var rule = loaded.entries.Single(e => e.name == "Face").shapeSets.Single();
                Assert.That(rule.On, Is.False, "the switch keeps its stored name and value");
                Assert.That(rule.value, Is.EqualTo(ShapeSetValue.Zero), "every stored rule keeps holding at 0");
                Assert.That(rule.holdTracked, Is.False, "and leaves the blink and viseme shapes free");
            }
            finally
            {
                Object.DestroyImmediate(loaded);
            }
        }

        [Test]
        public void AStoredRuleLeavesBlinkAndVisemesFree()
        {
            TrackBlinkAndVisemes();
            SetWeight("Blink", 30f);
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true });

            var fx = BuildToggles(out var errors);

            Assert.That(errors, Is.Empty);
            var on = State(Layer(fx, "AD: Face (pin)"), "On");
            Assert.That(Weight(on.motion, "Smile"), Is.EqualTo(0f), "a stored rule still holds at 0");
            Assert.That(Shape(on.motion, "Blink"), Is.Null, "the shape VRChat blinks with stays free");
            Assert.That(Shape(on.motion, "vrc.v_aa"), Is.Null, "and so does the one it speaks with");
        }

        // ---- toggles -------------------------------------------------------------------------------------------

        [Test]
        public void ADefaultRuleHoldsEveryOtherShapeAtItsSceneWeightWhileOn()
        {
            SetWeight("Eye_Big", 40f);
            SetWeight("Smile", 15f);
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            face.shapeSets.Add(Rule(ShapeSetValue.Default));

            var fx = BuildToggles(out var errors);

            Assert.That(errors, Is.Empty);
            var on = State(Layer(fx, "AD: Face (pin)"), "On");
            Assert.That(Weight(on.motion, "Eye_Big"), Is.EqualTo(40f));
            Assert.That(Weight(on.motion, "Smile"), Is.EqualTo(15f));
            Assert.That(Weight(on.motion, "Blush"), Is.EqualTo(100f), "the entry's own row wins over the rule");
        }

        /// <summary>Guard: pin semantics, Off writes nothing for a held shape; true before and after this task.</summary>
        [Test]
        public void ADefaultRuleWritesNothingWhileOff()
        {
            SetWeight("Eye_Big", 40f);
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(Rule(ShapeSetValue.Default));

            var fx = BuildToggles(out var errors);

            Assert.That(errors, Is.Empty);
            var off = State(Layer(fx, "AD: Face (pin)"), "Off");
            foreach (var shape in new[] { "Blush", "Eye_Big", "Smile" })
                Assert.That(Shape(off.motion, shape), Is.Null, shape + ": Off writes nothing for a held shape");
        }

        [Test]
        public void ADefaultIsTheWeightOtherTogglesRestAt()
        {
            SetWeight("Eye_Big", 40f);
            // On by default with a 0 rule and no row: the build writes 0 onto the clone before any layer is made, so
            // only the base state's own read of the rule's mesh still knows the 40.
            var clear = menu.AddEntry(EntryKind.Toggle, "Clear");
            clear.defaultOn = true;
            clear.shapeSets.Add(Rule(ShapeSetValue.Zero));
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(Rule(ShapeSetValue.Default));

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            Assert.That(avatar.Body.GetBlendShapeWeight(Index("Eye_Big")), Is.EqualTo(0f), "sanity: the clone starts at the other toggle's 0");
            float clearOff = Weight(State(Layer(fx, "AD: Clear (pin)"), "Off").motion, "Eye_Big");
            Assert.That(clearOff, Is.EqualTo(40f), "the other toggle hands the shape back to 40 when it turns off");
            Assert.That(Weight(State(Layer(fx, "AD: Face (pin)"), "On").motion, "Eye_Big"), Is.EqualTo(clearOff), "a held shape and an Off state never disagree");
        }

        /// <summary>Guard: a 0 rule still holds at 0; true before and after this task.</summary>
        [Test]
        public void AZeroRuleStillHoldsAtZero()
        {
            SetWeight("Eye_Big", 40f);
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(Rule(ShapeSetValue.Zero));

            var fx = BuildToggles(out var errors);

            Assert.That(errors, Is.Empty);
            var on = State(Layer(fx, "AD: Face (pin)"), "On");
            Assert.That(Weight(on.motion, "Eye_Big"), Is.EqualTo(0f));
            Assert.That(Weight(on.motion, "Smile"), Is.EqualTo(0f));
        }

        [Test]
        public void BlinkAndVisemeShapesAreLeftFreeByDefault()
        {
            TrackBlinkAndVisemes();
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(Rule(ShapeSetValue.Default));

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Face (pin)");
            var on = State(layer, "On");
            Assert.That(Shape(on.motion, "Smile"), Is.Not.Null, "sanity: the rule holds the other shapes");
            Assert.That(Shape(on.motion, "Blink"), Is.Null);
            Assert.That(Shape(on.motion, "vrc.v_aa"), Is.Null);
            Assert.That(Shape(State(layer, "Off").motion, "Blink"), Is.Null, "nothing hands the blink shape back to rest either");
            Assert.That(fx.layers.Any(l => l.name == "AD: Blink pause" || l.name == "AD: Lip sync pause"), Is.False, "nothing to pause");
        }

        [Test]
        public void HoldingBlinkAndVisemesHoldsThemAndPausesTrackingWhileOn()
        {
            TrackBlinkAndVisemes();
            SetWeight("Blink", 10f);
            SetWeight("Smile", 20f);
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(Rule(ShapeSetValue.Default, holdTracked: true));

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Face (pin)");
            var on = State(layer, "On");
            var off = State(layer, "Off");
            Assert.That(Weight(on.motion, "Blink"), Is.EqualTo(10f), "held at its default");
            Assert.That(Weight(on.motion, "vrc.v_aa"), Is.EqualTo(0f));

            var blink = Layer(fx, "AD: Blink pause");
            var lipSync = Layer(fx, "AD: Lip sync pause");
            var flag = State(blink, "Idle").transitions.Single().conditions.Single().parameter;
            Assert.That(State(lipSync, "Idle").transitions.Single().conditions.Single().parameter, Is.EqualTo(flag), "one flag for the entry, read by both pauses");
            Assert.That(Parameter(on.motion, flag).Evaluate(0f), Is.EqualTo(1f), "the flag is 1 while the hold is active");
            Assert.That(Parameter(off.motion, flag).Evaluate(0f), Is.EqualTo(0f), "and 0 at rest");
            Assert.That(fx.parameters.Single(p => p.name == flag).type, Is.EqualTo(AnimatorControllerParameterType.Float));

            Assert.That(Shape(off.motion, "Blink"), Is.Null, "the blink shape is never handed back to rest: VRChat's blinking owns it while off");
            Assert.That(Shape(off.motion, "vrc.v_aa"), Is.Null);
            Assert.That(Weight(off.motion, "Smile"), Is.EqualTo(20f), "an ordinary held shape is still handed back to rest when nothing else owns it");
        }

        [Test]
        public void AnExclusiveGroupMemberHoldingBlinkRaisesItsFlagOnlyInItsOwnState()
        {
            TrackBlinkAndVisemes();
            var sleepy = menu.AddEntry(EntryKind.Toggle, "Sleepy");
            sleepy.exclusiveGroup = "Face";
            sleepy.shapeSets.Add(Rule(ShapeSetValue.Zero, holdTracked: true));
            var smug = menu.AddEntry(EntryKind.Toggle, "Smug");
            smug.exclusiveGroup = "Face";
            smug.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Smile", weight = 100f, mode = ShapeEffectMode.Pin });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Face (pin)");
            var flag = State(Layer(fx, "AD: Blink pause"), "Idle").transitions.Single().conditions.Single().parameter;
            Assert.That(Parameter(State(layer, "Sleepy").motion, flag).Evaluate(0f), Is.EqualTo(1f), "the member whose rule holds the blink shape");
            Assert.That(Parameter(State(layer, "Smug").motion, flag).Evaluate(0f), Is.EqualTo(0f), "another member: the layer moves straight between members, never through Off");
            Assert.That(Parameter(State(layer, "Off").motion, flag).Evaluate(0f), Is.EqualTo(0f));
        }

        [Test]
        public void TheFlagIsAnimatorOnlyAndCostsNoBit()
        {
            TrackBlinkAndVisemes();
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(Rule(ShapeSetValue.Default, holdTracked: true));
            AvatarPlanCache.Invalidate();

            var provided = ParameterInfo.ForUI.GetParametersForObject(avatar.Root).ToList();
            var flag = provided.SingleOrDefault(p => p.EffectiveName == "AD/Pause/Face");
            Assert.That(flag, Is.Not.Null, "NDMF's parameter list names the toggle's flag");
            Assert.That(flag.IsAnimatorOnly, Is.True);
            Assert.That(flag.WantSynced, Is.False);
            Assert.That(flag.BitUsage, Is.EqualTo(0));
            Assert.That(flag.ParameterType, Is.EqualTo(AnimatorControllerParameterType.Float));
            Assert.That(flag.Source, Is.SameAs(menu));

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            Assert.That(fx.parameters.Single(p => p.name == "AD/Pause/Face").type, Is.EqualTo(AnimatorControllerParameterType.Float), "declared in FX");
            Assert.That(avatar.Descriptor.expressionParameters.parameters.Any(p => p.name.Contains("/Pause/")), Is.False, "never an expression parameter");
        }

        [Test]
        public void ACapturedSleepyFaceLeavesBlinkingToVRChatWhileOff()
        {
            TrackBlinkAndVisemes();
            SetWeight("Blink", 60f);
            SetWeight("Smile", 20f);
            var sleepy = menu.AddEntry(EntryKind.Toggle, "Sleepy");
            EntryEditing.CaptureShapeSet(menu, sleepy, avatar.Body);
            Assert.That(sleepy.blendShapes.Single(t => t.shape == "Blink").mode, Is.EqualTo(ShapeEffectMode.Pin), "sanity: Capture made a Pin row for the blink shape");

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Sleepy (pin)");
            var on = State(layer, "On");
            var off = State(layer, "Off");
            Assert.That(Weight(on.motion, "Blink"), Is.EqualTo(60f), "the captured eyes stay half closed while on");
            Assert.That(Shape(off.motion, "Blink"), Is.Null, "while off the blink shape is VRChat's again: no rest curve");
            Assert.That(Weight(off.motion, "Smile"), Is.EqualTo(20f), "an ordinary captured shape is still handed back to rest");
            var flag = State(Layer(fx, "AD: Blink pause"), "Idle").transitions.Single().conditions.Single().parameter;
            Assert.That(Parameter(on.motion, flag).Evaluate(0f), Is.EqualTo(1f), "blinking pauses while the toggle is on");
            Assert.That(Parameter(off.motion, flag).Evaluate(0f), Is.EqualTo(0f), "and comes back when it is off");
        }

        [Test]
        public void AHoldTriggersPinRowOnTheBlinkShapePausesBlinkingWhileFiring()
        {
            TrackBlinkAndVisemes();
            var hat = HatToggle();
            var wink = menu.AddEntry(EntryKind.Trigger, "Wink");
            wink.source = TriggerSource.Toggle;
            wink.sourceToggleId = hat.id;
            wink.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blink", weight = 100f, mode = ShapeEffectMode.Pin });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Wink (pin)");
            var flag = State(Layer(fx, "AD: Blink pause"), "Idle").transitions.Single().conditions.Single().parameter;
            Assert.That(Parameter(State(layer, "On").motion, flag).Evaluate(0f), Is.EqualTo(1f));
            Assert.That(Parameter(State(layer, "Off").motion, flag).Evaluate(0f), Is.EqualTo(0f));
            Assert.That(Shape(State(layer, "Off").motion, "Blink"), Is.Null, "never handed back to rest");
        }

        [Test]
        public void APauseAskedForAtLoadWaitsOutTheLoadWindow()
        {
            TrackBlinkAndVisemes();
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.defaultOn = true;
            face.shapeSets.Add(Rule(ShapeSetValue.Default, holdTracked: true));

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            Assert.That(Layer(fx, "AD: Face (pin)").stateMachine.defaultState.name, Is.EqualTo("On"), "sanity: the flag is 1 from the first frame");
            var pause = Layer(fx, "AD: Blink pause");
            var load = State(pause, "Load");
            Assert.That(pause.stateMachine.defaultState, Is.SameAs(load), "the layer starts in Load, not listening yet");
            Assert.That(load.behaviours, Is.Empty, "Load asks VRChat for nothing");
            var toIdle = load.transitions.Single();
            Assert.That(toIdle.destinationState, Is.SameAs(State(pause, "Idle")));
            Assert.That(toIdle.conditions, Is.Empty);
            Assert.That(toIdle.hasExitTime, Is.True);
            Assert.That(toIdle.exitTime, Is.EqualTo(5f), "about 5 seconds: an empty state lasts one second");
            Assert.That(toIdle.duration, Is.EqualTo(0f));
        }

        [Test]
        public void ARuleLeavesTheShapesTheTogglesOwnClipMoves()
        {
            // The clip names its shape relative to its path root, the way a clip made inside a prefab does.
            var clip = TestAvatar.ShapeClip("Smile clip", "", "Smile", 100f, 100f);
            try
            {
                var face = menu.AddEntry(EntryKind.Toggle, "Face");
                face.animations.Add(new AnimationTarget { clip = clip, pathRoot = avatar.Body.transform });
                face.shapeSets.Add(Rule(ShapeSetValue.Default));

                var fx = BuildAll(out var errors, finalPass: false);

                NoErrors(errors);
                Assert.That(Weight(State(Layer(fx, "AD: Face"), "On").motion, "Smile"), Is.EqualTo(100f), "sanity: the toggle's own clip moves Smile");
                var on = State(Layer(fx, "AD: Face (pin)"), "On");
                Assert.That(Shape(on.motion, "Smile"), Is.Null, "the rule leaves the shape the entry's own clip moves");
                Assert.That(Shape(on.motion, "Eye_Big"), Is.Not.Null, "and still holds the others");
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void ARuleLeavesTheShapesAnOutfitPieceDrives()
        {
            var piece = avatar.Hat.AddComponent<AvatarDocShapes>();
            piece.whileOn.Add(new ShapeWhileOn { mesh = avatar.Body, shape = "Smile", value = 100f });
            var shirtGo = new GameObject("Shirt");
            shirtGo.transform.SetParent(avatar.Root.transform);
            var shirt = shirtGo.AddComponent<SkinnedMeshRenderer>();
            shirt.sharedMesh = TestAvatar.QuadWithShapes("Shrink", "Hide");
            var follower = shirtGo.AddComponent<AvatarDocShapes>();
            follower.follow.Add(new ShapeFollow { sourceMesh = avatar.Body, sourceShape = "Blush", localShape = "Shrink" });
            try
            {
                var face = menu.AddEntry(EntryKind.Toggle, "Face");
                face.shapeSets.Add(Rule(ShapeSetValue.Default));
                var coat = menu.AddEntry(EntryKind.Toggle, "Coat");
                coat.shapeSets.Add(new ShapeSetRule { mesh = shirt, zeroOthers = true, value = ShapeSetValue.Zero });

                var fx = BuildAll(out var errors, finalPass: false);

                NoErrors(errors);
                var faceOn = State(Layer(fx, "AD: Face (pin)"), "On");
                Assert.That(Shape(faceOn.motion, "Smile"), Is.Null, "a shape a piece holds while worn is left to the piece");
                Assert.That(Shape(faceOn.motion, "Eye_Big"), Is.Not.Null, "the others are held");
                var coatOn = (AnimationClip)State(Layer(fx, "AD: Coat (pin)"), "On").motion;
                Assert.That(AnimationUtility.GetEditorCurve(coatOn, EditorCurveBinding.FloatCurve("Shirt", typeof(SkinnedMeshRenderer), "blendShape.Shrink")), Is.Null, "a shape a piece's follow row drives is left to it too");
                Assert.That(AnimationUtility.GetEditorCurve(coatOn, EditorCurveBinding.FloatCurve("Shirt", typeof(SkinnedMeshRenderer), "blendShape.Hide")), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(shirt.sharedMesh);
            }
        }

        // ---- triggers ------------------------------------------------------------------------------------------

        [Test]
        public void AHoldTriggerHoldsTheRestWhileFiring()
        {
            SetWeight("Eye_Big", 40f);
            var hat = HatToggle();
            var pout = menu.AddEntry(EntryKind.Trigger, "Pout");
            pout.source = TriggerSource.Toggle;
            pout.sourceToggleId = hat.id;
            pout.shapeSets.Add(Rule(ShapeSetValue.Default));

            var fx = BuildAll(out var errors, finalPass: false);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Pout (pin)");
            var on = State(layer, "On");
            Assert.That(Weight(on.motion, "Eye_Big"), Is.EqualTo(40f));
            Assert.That(Weight(on.motion, "Blush"), Is.EqualTo(0f));
            foreach (var shape in new[] { "Blush", "Eye_Big", "Smile" })
                Assert.That(Shape(State(layer, "Off").motion, shape), Is.Null, shape + ": nothing while the trigger does not fire");
        }

        [Test]
        public void AFadeContactFadesTheRestToZero()
        {
            SetWeight("Eye_Big", 40f);
            var pat = menu.AddEntry(EntryKind.Trigger, "Pat");
            pat.source = TriggerSource.Contact;
            pat.contactAnchor = avatar.Hat.transform;
            pat.contactTags = new List<string> { "Hand" };
            pat.touchMode = TouchMode.FollowDistance;
            pat.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            pat.shapeSets.Add(Rule(ShapeSetValue.Zero));

            var fx = BuildAll(out var errors, finalPass: false);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Pat (distance)");
            var near = (BlendTree)State(layer, "Near").motion;
            var start = near.children.Single(c => c.threshold == 0f).motion;
            var full = near.children.Single(c => c.threshold > 0f).motion;
            Assert.That(Weight(start, "Eye_Big"), Is.EqualTo(40f), "at rest at the start of the blend");
            Assert.That(Weight(full, "Eye_Big"), Is.EqualTo(0f), "at 0 when fully on");
            Assert.That(Weight(full, "Smile"), Is.EqualTo(0f));
            Assert.That(Shape(State(layer, "Far").motion, "Eye_Big"), Is.Null, "Far writes nothing for a held shape");
        }

        [Test]
        public void ASmoothBlendShapeTriggerHoldsTheRestAtItsDefault()
        {
            SetWeight("Eye_Big", 40f);
            var link = menu.AddEntry(EntryKind.Trigger, "Blush link");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            link.shapeSets.Add(Rule(ShapeSetValue.Default));

            var fx = BuildAll(out var errors, finalPass: false);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Blush link (shape)");
            var near = (BlendTree)State(layer, "Near").motion;
            Assert.That(near.children.Length, Is.GreaterThanOrEqualTo(2));
            foreach (var child in near.children)
            {
                Assert.That(Weight(child.motion, "Eye_Big"), Is.EqualTo(40f), "both ends of the blend are the default");
                Assert.That(Shape(child.motion, "Blush"), Is.Null, "the trigger's own shape is never held");
            }
            Assert.That(Shape(State(layer, "Far").motion, "Eye_Big"), Is.Null, "Far writes nothing for a held shape");
        }

        [Test]
        public void ASwitchBlendShapeTriggerHoldsTheRestInItsOnState()
        {
            SetWeight("Eye_Big", 40f);
            var link = menu.AddEntry(EntryKind.Trigger, "Blush link");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            link.linkStyle = BlendShapeLinkStyle.Switch;
            link.shapeSets.Add(Rule(ShapeSetValue.Zero));

            var fx = BuildAll(out var errors, finalPass: false);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Blush link (shape)");
            Assert.That(Weight(State(layer, "On").motion, "Eye_Big"), Is.EqualTo(0f));
            Assert.That(Shape(State(layer, "Off").motion, "Eye_Big"), Is.Null);
        }

        [Test]
        public void AHoldTriggerHoldingBlinkPausesItOnlyWhileFiring()
        {
            TrackBlinkAndVisemes();
            var hat = HatToggle();
            var pout = menu.AddEntry(EntryKind.Trigger, "Pout");
            pout.source = TriggerSource.Toggle;
            pout.sourceToggleId = hat.id;
            pout.shapeSets.Add(Rule(ShapeSetValue.Zero, holdTracked: true));

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Pout (pin)");
            var on = State(layer, "On");
            Assert.That(Weight(on.motion, "Blink"), Is.EqualTo(0f), "held while the trigger fires");
            var flag = State(Layer(fx, "AD: Blink pause"), "Idle").transitions.Single().conditions.Single().parameter;
            Assert.That(Parameter(on.motion, flag).Evaluate(0f), Is.EqualTo(1f));
            Assert.That(Parameter(State(layer, "Off").motion, flag).Evaluate(0f), Is.EqualTo(0f));
            Assert.That(Shape(State(layer, "Off").motion, "Blink"), Is.Null, "never handed back to rest");
        }

        [Test]
        public void ASmoothBlendShapeTriggerHoldingBlinkPausesItWhileItsEffectsMove()
        {
            TrackBlinkAndVisemes();
            var link = menu.AddEntry(EntryKind.Trigger, "Blush link");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            link.shapeSets.Add(Rule(ShapeSetValue.Zero, holdTracked: true));

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Blush link (shape)");
            var flag = State(Layer(fx, "AD: Blink pause"), "Idle").transitions.Single().conditions.Single().parameter;
            foreach (var child in ((BlendTree)State(layer, "Near").motion).children)
            {
                Assert.That(Shape(child.motion, "Blink"), Is.Not.Null, "the blink shape is in the blend");
                Assert.That(Parameter(child.motion, flag).Evaluate(0f), Is.EqualTo(1f), "and blinking pauses while it moves");
            }
            Assert.That(Parameter(State(layer, "Far").motion, flag).Evaluate(0f), Is.EqualTo(0f));
            Assert.That(Shape(State(layer, "Far").motion, "Blink"), Is.Null);
        }

        [Test]
        public void ARuleOnATriggerIsAllowedButNotOnAFlipTrigger()
        {
            var hat = HatToggle();
            var hold = menu.AddEntry(EntryKind.Trigger, "Pout");
            hold.source = TriggerSource.Toggle;
            hold.sourceToggleId = hat.id;
            hold.shapeSets.Add(Rule(ShapeSetValue.Default));
            var flip = menu.AddEntry(EntryKind.Trigger, "Boop");
            flip.source = TriggerSource.Contact;
            flip.contactAnchor = avatar.Hat.transform;
            flip.touchMode = TouchMode.FlipToggle;
            flip.flipToggleId = hat.id;
            flip.shapeSets.Add(Rule(ShapeSetValue.Default));

            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });

            Assert.That(tree.Messages.Any(m => m.Key == "ad.error.shape_set_not_a_toggle" && m.EntryIds.Contains(hold.id)), Is.False, "a trigger with effects can hold one");
            Assert.That(tree.Messages.Any(m => m.Key == "ad.error.shape_set_not_a_toggle" && m.EntryIds.Contains(flip.id)), Is.True, "a flip trigger has no effects to hold it with");
        }

        // ---- inspector -----------------------------------------------------------------------------------------

        [Test]
        public void HoldTheRestAddsADefaultRuleForThePickedMeshOnce()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            System.Action<Object> pick = null;
            PickKind? kind = null;
            AvatarObjectPicker.ShowOverride = (_, root, k, onPick) => { kind = k; pick = onPick; };
            var view = View(menu);
            view.Tree.SelectEntry(face.id);
            try
            {
                var hold = view.Detail.Q<TargetListView>("ad-blendshapes").Q<Button>("ad-shape-set-hold");
                Assert.That(hold, Is.Not.Null, "Hold the rest sits beside Capture");
                Assert.That(hold.tooltip, Is.Not.Empty);

                Undo.IncrementCurrentGroup();
                UiTestEvents.SimulateClick(hold);
                Assert.That(kind, Is.EqualTo(PickKind.SkinnedMeshWithShapes), "it opens the avatar's picker for meshes with shapes");
                pick(avatar.Body);

                var rule = menu.FindEntry(face.id).shapeSets.Single();
                Assert.That(rule.mesh, Is.SameAs(avatar.Body));
                Assert.That(rule.On, Is.True);
                Assert.That(rule.value, Is.EqualTo(ShapeSetValue.Default));
                Assert.That(rule.holdTracked, Is.False, "blink and visemes start free");
                Assert.That(view.Detail.Q<Toggle>("zero-others"), Is.Not.Null, "the rule line shows under the list");

                rule.value = ShapeSetValue.Zero;
                pick = null;
                UiTestEvents.SimulateClick(view.Detail.Q<TargetListView>("ad-blendshapes").Q<Button>("ad-shape-set-hold"));
                pick(avatar.Body);
                Assert.That(menu.FindEntry(face.id).shapeSets.Count, Is.EqualTo(1), "a mesh the entry already holds is not added twice");
                Assert.That(menu.FindEntry(face.id).shapeSets[0].value, Is.EqualTo(ShapeSetValue.Zero), "and the rule it has is kept as it is");

                Undo.PerformUndo();
                Assert.That(menu.FindEntry(face.id).shapeSets, Is.Empty, "one Undo step takes the added rule away");
            }
            finally
            {
                Undo.ClearUndo(menu);
            }
        }

        [Test]
        public void HoldTheRestShowsOnTogglesAndTriggersOnly()
        {
            var hat = HatToggle();
            var trigger = menu.AddEntry(EntryKind.Trigger, "Pout");
            trigger.source = TriggerSource.Toggle;
            trigger.sourceToggleId = hat.id;
            trigger.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            var link = menu.AddEntry(EntryKind.Trigger, "Blush link");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 100f });
            var radial = menu.AddEntry(EntryKind.Radial, "Eyes");
            var over = menu.AddEntry(EntryKind.Override, "Silence");
            var view = View(menu);

            foreach (var (entry, shows) in new[] { (hat, true), (trigger, true), (link, true), (radial, false), (over, false) })
            {
                view.Tree.SelectEntry(entry.id);
                var list = view.Detail.Q<TargetListView>("ad-blendshapes");
                Assert.That(list.Q<Button>("ad-shape-set-hold") != null, Is.EqualTo(shows), entry.name + ": Hold the rest");
                Assert.That(list.Q<VisualElement>("ad-shape-sets") != null, Is.EqualTo(shows), entry.name + ": the rule lines");
            }
        }

        [Test]
        public void TheRuleLineControlsEachWriteTheRuleInOneUndoStep()
        {
            TrackBlinkAndVisemes();
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            face.shapeSets.Add(Rule(ShapeSetValue.Zero));
            var view = View(menu);
            ShapeSetRule Stored() => menu.FindEntry(face.id).shapeSets.Single();
            try
            {
                LayoutProbe.Measure(view, () =>
                {
                    view.Tree.SelectEntry(face.id);
                    LayoutProbe.Validate(view);

                    var value = view.Detail.Q<SegmentedChoice>("ad-shape-set-value");
                    Assert.That(value, Is.Not.Null, "the Default or 0 choice");
                    Assert.That(value.Index, Is.EqualTo(1), "a 0 rule shows 0");
                    Undo.IncrementCurrentGroup();
                    value.Pick(0);
                    Assert.That(Stored().value, Is.EqualTo(ShapeSetValue.Default));
                    Assert.That(view.Detail.Q<Label>("ad-shape-set-note").text, Is.EqualTo(string.Format(L.Text("ad.info.shape_set_count.default"), 2)), "the note follows the value: Eye_Big and Smile, with the blink and viseme shapes free");
                    Undo.PerformUndo();
                    Assert.That(Stored().value, Is.EqualTo(ShapeSetValue.Zero), "one Undo step");

                    view.Refresh();
                    LayoutProbe.Validate(view);
                    var tracked = view.Detail.Q<Toggle>("hold-tracked");
                    Assert.That(tracked, Is.Not.Null, "the blink and viseme switch shows for a mesh the descriptor blinks with");
                    Assert.That(tracked.value, Is.False);
                    Undo.IncrementCurrentGroup();
                    tracked.value = true;
                    Assert.That(Stored().holdTracked, Is.True);
                    Assert.That(view.Detail.Q<Label>("ad-shape-set-note").text, Is.EqualTo(string.Format(L.Text("ad.info.shape_set_count"), 4)), "the count takes the blink and viseme shapes in");
                    Undo.PerformUndo();
                    Assert.That(Stored().holdTracked, Is.False, "one Undo step");

                    view.Refresh();
                    LayoutProbe.Validate(view);
                    var on = view.Detail.Q<Toggle>("zero-others");
                    Assert.That(on.value, Is.True);
                    Undo.IncrementCurrentGroup();
                    on.value = false;
                    Assert.That(Stored().On, Is.False);
                    Undo.PerformUndo();
                    Assert.That(Stored().On, Is.True, "one Undo step");
                });
            }
            finally
            {
                Undo.ClearUndo(menu);
            }
        }

        [Test]
        public void TheBlinkSwitchShowsOnlyForAMeshTheDescriptorBlinksOrSpeaksWith()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            face.shapeSets.Add(Rule(ShapeSetValue.Default));
            var view = View(menu);
            view.Tree.SelectEntry(face.id);

            Assert.That(view.Detail.Q<Toggle>("hold-tracked"), Is.Null, "nothing to hold or free on a mesh VRChat neither blinks nor speaks with");
            Assert.That(view.Detail.Q<SegmentedChoice>("ad-shape-set-value"), Is.Not.Null);
        }

        /// <summary>Guard: a toggle's Capture still adds its rule at 0; true before and after this task.</summary>
        [Test]
        public void CaptureStillAddsAZeroRule()
        {
            SetWeight("Blush", 40f);
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            var view = View(menu);
            view.Tree.SelectEntry(face.id);

            view.Detail.Q<TargetListView>("ad-blendshapes").CaptureFrom(avatar.Body);

            var rule = menu.FindEntry(face.id).shapeSets.Single();
            Assert.That(rule.value, Is.EqualTo(ShapeSetValue.Zero), "the rest is set to 0, as the creator first asked");
            Assert.That(rule.holdTracked, Is.False);
            Assert.That(rule.On, Is.True);
        }

        [Test]
        public void CaptureOnATriggerAddsOnlyTheRows()
        {
            SetWeight("Blush", 40f);
            var hat = HatToggle();
            var pout = menu.AddEntry(EntryKind.Trigger, "Pout");
            pout.source = TriggerSource.Toggle;
            pout.sourceToggleId = hat.id;
            pout.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var view = View(menu);
            view.Tree.SelectEntry(pout.id);

            view.Detail.Q<TargetListView>("ad-blendshapes").CaptureFrom(avatar.Body);

            var stored = menu.FindEntry(pout.id);
            Assert.That(stored.blendShapes.Single().mode, Is.EqualTo(ShapeEffectMode.Pin));
            Assert.That(stored.shapeSets, Is.Empty, "only a toggle's Capture holds the rest at 0; on a trigger it adds the captured rows alone");
        }

        [Test]
        public void SimpleShowsHoldTheRestWithCaptureAndTheRuleLine()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.objects.Add(new ObjectTarget { target = avatar.Hat });
            face.shapeSets.Add(Rule(ShapeSetValue.Default));
            var view = View(menu, simple: true);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(face.id);
                LayoutProbe.Validate(view);
                var list = view.Detail.Q<TargetListView>("ad-blendshapes");
                Assert.That(list.Q<Button>("ad-shape-set-capture").worldBound.width, Is.GreaterThan(0f), "Capture shows in Simple");
                Assert.That(list.Q<Button>("ad-shape-set-hold").worldBound.width, Is.GreaterThan(0f), "and Hold the rest beside it");
                Assert.That(list.Q<VisualElement>("ad-shape-set-row").worldBound.height, Is.GreaterThan(0f), "the rule line shows while the toggle has a rule");
            });
        }

        [Test]
        public void ASectionHoldingOnlyARuleStaysShown()
        {
            var hat = HatToggle();
            var pout = menu.AddEntry(EntryKind.Trigger, "Pout");
            pout.source = TriggerSource.Toggle;
            pout.sourceToggleId = hat.id;
            pout.shapeSets.Add(Rule(ShapeSetValue.Default));
            var view = View(menu);

            view.Tree.SelectEntry(pout.id);

            var list = view.Detail.Q<TargetListView>("ad-blendshapes");
            Assert.That(list.Count, Is.EqualTo(0), "sanity: no rows");
            Assert.That(list.ClassListContains("ad-hidden"), Is.False, "a rule is something the section holds, so it shows");
        }

        // ---- preview -------------------------------------------------------------------------------------------

        [Test]
        public void ADefaultRuleCancelsAnotherPreviewedEffectOnAHeldShape()
        {
            SetWeight("Eye_Big", 40f);
            var big = menu.AddEntry(EntryKind.Toggle, "Big eyes");
            big.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 100f });
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(Rule(ShapeSetValue.Default));

            var bigOnly = PreviewModel.Compute(avatar.Root.transform, new[] { menu }, new Dictionary<string, float> { [big.id] = 1f }, SceneReader.Direct);
            Assert.That(bigOnly[avatar.Body].Shapes[Index("Eye_Big")], Is.EqualTo(100f), "sanity: the other toggle moves the shape");

            var both = PreviewModel.Compute(avatar.Root.transform, new[] { menu }, new Dictionary<string, float> { [big.id] = 1f, [face.id] = 1f }, SceneReader.Direct);
            bool moved = both.TryGetValue(avatar.Body, out var state) && state.Shapes.ContainsKey(Index("Eye_Big"));
            Assert.That(moved, Is.False, "the held shape shows its default, 40, the scene's own weight");
        }

        [Test]
        public void ADefaultRuleOnAPreviewedTriggerCancelsAToggle()
        {
            SetWeight("Eye_Big", 40f);
            var big = menu.AddEntry(EntryKind.Toggle, "Big eyes");
            big.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 100f });
            var hat = HatToggle();
            var pout = menu.AddEntry(EntryKind.Trigger, "Pout");
            pout.source = TriggerSource.Toggle;
            pout.sourceToggleId = hat.id;
            pout.shapeSets.Add(Rule(ShapeSetValue.Default));

            var states = PreviewModel.Compute(avatar.Root.transform, new[] { menu }, new Dictionary<string, float> { [big.id] = 1f, [pout.id] = 1f }, SceneReader.Direct);

            bool moved = states.TryGetValue(avatar.Body, out var state) && state.Shapes.ContainsKey(Index("Eye_Big"));
            Assert.That(moved, Is.False, "the trigger shown firing holds the shape at its default, 40, over the toggle's 100");
        }

        [Test]
        public void ADefaultRuleCancelsALaterEntrysEffectToo()
        {
            SetWeight("Eye_Big", 40f);
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(Rule(ShapeSetValue.Default));
            var big = menu.AddEntry(EntryKind.Toggle, "Big eyes");
            big.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 100f });

            var states = PreviewModel.Compute(avatar.Root.transform, new[] { menu }, new Dictionary<string, float> { [face.id] = 1f, [big.id] = 1f }, SceneReader.Direct);

            bool moved = states.TryGetValue(avatar.Body, out var state) && state.Shapes.ContainsKey(Index("Eye_Big"));
            Assert.That(moved, Is.False, "the pin layer sits above every ordinary layer, whatever the tree order: the default wins");
        }

        [Test]
        public void AZeroRuleWinsOverALaterFixedRowThatIsOff()
        {
            SetWeight("Eye_Big", 40f);
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(Rule(ShapeSetValue.Zero));
            var big = menu.AddEntry(EntryKind.Toggle, "Big eyes");
            big.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 100f });

            var states = PreviewModel.Compute(avatar.Root.transform, new[] { menu }, new Dictionary<string, float> { [face.id] = 1f, [big.id] = 0f }, SceneReader.Direct);

            Assert.That(states[avatar.Body].Shapes[Index("Eye_Big")], Is.EqualTo(0f), "the off toggle writes its rest, 40, below the pin layer that holds 0");
        }

        [Test]
        public void ThePreviewLeavesBlinkFreeUnlessTheRuleHoldsIt()
        {
            TrackBlinkAndVisemes();
            SetWeight("Blink", 30f);
            SetWeight("Smile", 30f);
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(Rule(ShapeSetValue.Zero));

            var free = PreviewModel.Compute(avatar.Root.transform, new[] { menu }, new Dictionary<string, float> { [face.id] = 1f }, SceneReader.Direct);
            Assert.That(free[avatar.Body].Shapes[Index("Smile")], Is.EqualTo(0f), "sanity: the rule holds the other shapes");
            Assert.That(free[avatar.Body].Shapes.ContainsKey(Index("Blink")), Is.False, "blinking stays free");

            face.shapeSets[0].holdTracked = true;
            AvatarPlanCache.Invalidate();
            var held = PreviewModel.Compute(avatar.Root.transform, new[] { menu }, new Dictionary<string, float> { [face.id] = 1f }, SceneReader.Direct);
            Assert.That(held[avatar.Body].Shapes[Index("Blink")], Is.EqualTo(0f), "held with the switch on");
        }

        // ---- words ---------------------------------------------------------------------------------------------

        [Test]
        public void TheSummaryLineNamesTheValue()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(Rule(ShapeSetValue.Default));
            var hat = HatToggle();
            var pout = menu.AddEntry(EntryKind.Trigger, "Pout");
            pout.source = TriggerSource.Toggle;
            pout.sourceToggleId = hat.id;
            pout.shapeSets.Add(Rule(ShapeSetValue.Zero));
            var plan = AvatarPlanCache.GetFor(menu);

            var summary = SummaryBuilder.Build(plan, menu, SummaryFormat.Plain);

            Assert.That(summary, Does.Contain(string.Format(L.Text("ad.summary.shape_sets.line.default"), "Face", avatar.Body.name, 5)));
            Assert.That(summary, Does.Contain(string.Format(L.Text("ad.summary.shape_sets.line"), "Pout", avatar.Body.name, 5)), "a trigger's rule is listed too");
        }

        [Test]
        public void TheBuildsLineSaysBlinkingPausesWhenTheRuleHoldsIt()
        {
            TrackBlinkAndVisemes();
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(Rule(ShapeSetValue.Default));
            var view = View(menu);
            view.Tree.SelectEntry(face.id);
            Assert.That(view.Detail.Q<Label>("ad-builds-line").text, Does.Not.Contain(L.Text("ad.ui.builds.pauses_blink_shape")), "blinking stays free");

            menu.FindEntry(face.id).shapeSets[0].holdTracked = true;
            AvatarPlanCache.Invalidate();
            view = View(menu);
            view.Tree.SelectEntry(face.id);
            Assert.That(view.Detail.Q<Label>("ad-builds-line").text, Does.Contain(L.Text("ad.ui.builds.pauses_blink_shape")));
            Assert.That(view.Detail.Q<Label>("ad-builds-line").text, Does.Contain(L.Text("ad.ui.builds.pauses_lipsync_shape")));
        }

        /// <summary>Guard on the words only: the large rule warning and the error speak of holding, for triggers too.</summary>
        [Test]
        public void TheLargeRuleWarningSpeaksOfHoldingForBothValues()
        {
            Assert.That(L.Text("ad.warn.shape_set_large"), Does.Contain("holds"));
            Assert.That(L.Text("ad.warn.shape_set_large"), Does.Not.Contain("zeroes"));
            Assert.That(L.Text("ad.error.shape_set_not_a_toggle"), Does.Contain("trigger"), "the error says a trigger can hold one too");
        }
    }
}
#endif
