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
using Object = UnityEngine.Object;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Plan 17 task H: at upload a shape set rule writes only the shapes something else moves, a Zero rule also the
    /// shapes whose default is not 0, and the blink or viseme shapes its switch holds. Everything else stays unanimated,
    /// which shows the same value the rule would hold it at.
    /// </summary>
    public class HoldOnlyWhatMovesTests
    {
        TestAvatar avatar;
        AvatarDoc menu;
        readonly List<Object> made = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            GeneratedAssets.Note();
            avatar = TestAvatar.Create();
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

        static AnimatorController ControllerOf(TestAvatar a, VRCAvatarDescriptor.AnimLayerType type)
            => (AnimatorController)a.Descriptor.baseAnimationLayers.First(l => l.type == type).animatorController;

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

        static AnimatorControllerLayer Layer(AnimatorController fx, string name) => fx.layers.Single(l => l.name == name);
        static AnimatorState State(AnimatorControllerLayer layer, string name) => layer.stateMachine.states.Single(s => s.state.name == name).state;

        /// <summary>Every shape of the renderer at this path that the motion writes.</summary>
        static List<string> Shapes(Motion motion, string path)
            => AnimationUtility.GetCurveBindings((AnimationClip)motion)
                .Where(b => b.path == path && b.type == typeof(SkinnedMeshRenderer) && b.propertyName.StartsWith("blendShape."))
                .Select(b => b.propertyName.Substring("blendShape.".Length)).ToList();

        static AnimationCurve Curve(Motion motion, string path, string shape)
            => AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + shape));

        static AnimationCurve Parameter(Motion motion, string parameter)
            => AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve("", typeof(Animator), parameter));

        static void NoErrors(List<ErrorContext> errors)
            => Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error).Select(e => (e.TheError as SimpleError)?.TitleKey), Is.Empty);

        static List<string> Pruned(List<ErrorContext> errors)
            => errors.Where(e => e.TheError is SimpleError s && (s.TitleKey == "ad.info.shape_set_pruned" || s.TitleKey == "ad.info.shape_set_pruned.one"))
                .Select(e => ((SimpleError)e.TheError).FormatTitle()).ToList();

        /// <summary>The whole build the way NDMF runs it: AvatarDoc's own pass, then the final pass the Optimizing phase runs after every other tool.</summary>
        AnimatorController BuildAll(out List<ErrorContext> errors)
        {
            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            errors.AddRange(ErrorReport.CaptureErrors(() => AvatarDoctorPlugin.FinishAfterOtherTools(context)));
            return ControllerOf(avatar, VRCAvatarDescriptor.AnimLayerType.FX);
        }

        /// <summary>AvatarDoc's own pass alone, its controllers committed, as they are when the other tools run.</summary>
        BuildContext FirstPass(out List<ErrorContext> errors)
        {
            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            return context;
        }

        /// <summary>Adds a layer after every layer of the committed FX controller, outside the animator services, the way VRCFury merges its own between AvatarDoc's two passes.</summary>
        void AppendForeignLayer(string layerName, Motion motion)
        {
            var committed = ControllerOf(avatar, VRCAvatarDescriptor.AnimLayerType.FX);
            var foreign = new AnimatorController { name = committed.name, parameters = committed.parameters, layers = committed.layers };
            made.Add(foreign);
            foreign.AddLayer(layerName);
            var machine = foreign.layers[foreign.layers.Length - 1].stateMachine;
            made.Add(machine);
            var state = machine.AddState(layerName + " state");
            made.Add(state);
            state.motion = motion;
            machine.defaultState = state;
            SetController(avatar, VRCAvatarDescriptor.AnimLayerType.FX, foreign);
        }

        static bool Reported(List<ErrorContext> errors, string key) => errors.Any(e => e.TheError is SimpleError s && s.TitleKey == key);

        AnimationClip Clip(string name, string path, string shape)
        {
            var clip = TestAvatar.ShapeClip(name, path, shape, 0f, 100f);
            made.Add(clip);
            return clip;
        }

        AnimatorController ControllerWith(string name, string layerName, Motion motion)
        {
            var controller = new AnimatorController { name = name };
            made.Add(controller);
            controller.AddLayer(layerName);
            var machine = controller.layers[controller.layers.Length - 1].stateMachine;
            made.Add(machine);
            var state = machine.AddState(layerName + " state");
            made.Add(state);
            state.motion = motion;
            machine.defaultState = state;
            return controller;
        }

        /// <summary>
        /// A mesh "Face" with shapes A to F, the descriptor blinking with F. Another toggle moves A, a merged animator's
        /// layer moves B, the Gesture layer moves C; nothing moves D (default 0) or E (default 50).
        /// </summary>
        SkinnedMeshRenderer FaceScene()
        {
            var go = new GameObject("Face");
            go.transform.SetParent(avatar.Root.transform);
            var face = go.AddComponent<SkinnedMeshRenderer>();
            face.sharedMesh = TestAvatar.QuadWithShapes("A", "B", "C", "D", "E", "F");
            made.Add(face.sharedMesh);
            face.SetBlendShapeWeight(4, 50f);
            avatar.Descriptor.enableEyeLook = true;
            avatar.Descriptor.customEyeLookSettings.eyelidType = VRCAvatarDescriptor.EyelidType.Blendshapes;
            avatar.Descriptor.customEyeLookSettings.eyelidsSkinnedMesh = face;
            avatar.Descriptor.customEyeLookSettings.eyelidsBlendshapes = new[] { 5, -1, -1 };

            var other = menu.AddEntry(EntryKind.Toggle, "Other");
            other.blendShapes.Add(new BlendShapeTarget { renderer = face, shape = "A", weight = 100f });
            menu.animators.Add(AnimatorMerge.Create(ControllerWith("Merged", "Mouth", Clip("B moves", "Face", "B"))));
            SetController(avatar, VRCAvatarDescriptor.AnimLayerType.Gesture, ControllerWith("Gesture", "Hands", Clip("C moves", "Face", "C")));
            return face;
        }

        static ShapeSetRule FaceRule(SkinnedMeshRenderer face, ShapeSetValue value) => new ShapeSetRule { mesh = face, zeroOthers = true, value = value, holdBlink = true };

        static string[] Expected(ShapeSetValue value) => value == ShapeSetValue.Default ? new[] { "A", "B", "C", "F" } : new[] { "A", "B", "C", "E", "F" };

        // ---- the four builders ---------------------------------------------------------------------------------

        [TestCase(ShapeSetValue.Default)]
        [TestCase(ShapeSetValue.Zero)]
        public void AToggleHoldsOnlyTheShapesThatMove(ShapeSetValue value)
        {
            var face = FaceScene();
            var holder = menu.AddEntry(EntryKind.Toggle, "Holder");
            holder.shapeSets.Add(FaceRule(face, value));

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Holder (pin)");
            Assert.That(Shapes(State(layer, "On").motion, "Face"), Is.EquivalentTo(Expected(value)),
                "another toggle (A), a merged animator (B), the Gesture layer (C) and the blink shape Hold blink holds (F); E only for a Zero rule");
            Assert.That(Shapes(State(layer, "Off").motion, "Face"), Does.Not.Contain("D"), "a dropped shape gets no rest curve either");
            Assert.That(Shapes(State(layer, "Off").motion, "Face"), Does.Not.Contain("F"), "the blink shape is still never handed back to rest");
        }

        [TestCase(ShapeSetValue.Default)]
        [TestCase(ShapeSetValue.Zero)]
        public void AHoldTriggerHoldsOnlyTheShapesThatMove(ShapeSetValue value)
        {
            var face = FaceScene();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var pout = menu.AddEntry(EntryKind.Trigger, "Pout");
            pout.source = TriggerSource.Toggle;
            pout.sourceToggleId = hat.id;
            pout.shapeSets.Add(FaceRule(face, value));

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Pout (pin)");
            Assert.That(Shapes(State(layer, "On").motion, "Face"), Is.EquivalentTo(Expected(value)));
            Assert.That(Shapes(State(layer, "Off").motion, "Face"), Does.Not.Contain("D"));
        }

        [TestCase(ShapeSetValue.Default)]
        [TestCase(ShapeSetValue.Zero)]
        public void AFadeContactHoldsOnlyTheShapesThatMoveInEveryChild(ShapeSetValue value)
        {
            var face = FaceScene();
            var pat = menu.AddEntry(EntryKind.Trigger, "Pat");
            pat.source = TriggerSource.Contact;
            pat.contactAnchor = avatar.Hat.transform;
            pat.contactTags = new List<string> { "Hand" };
            pat.touchMode = TouchMode.FollowDistance;
            pat.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            pat.shapeSets.Add(FaceRule(face, value));

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Pat (distance)");
            var near = (BlendTree)State(layer, "Near").motion;
            Assert.That(near.children.Length, Is.EqualTo(2));
            foreach (var child in near.children)
                Assert.That(Shapes(child.motion, "Face"), Is.EquivalentTo(Expected(value)), "every child of the Near tree drops the same shapes, at " + child.threshold);
            Assert.That(Shapes(State(layer, "Far").motion, "Face"), Does.Not.Contain("D"), "Far gets no rest curve for a dropped shape");
            Assert.That(Shapes(State(layer, "Far").motion, "Body"), Does.Contain("Blush"), "sanity: the row's own shape is still handed back to rest");
        }

        [TestCase(ShapeSetValue.Default)]
        [TestCase(ShapeSetValue.Zero)]
        public void ASmoothBlendShapeTriggerHoldsOnlyTheShapesThatMoveInEveryChild(ShapeSetValue value)
        {
            var face = FaceScene();
            var link = menu.AddEntry(EntryKind.Trigger, "Blush link");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            link.shapeSets.Add(FaceRule(face, value));

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Blush link (shape)");
            var near = (BlendTree)State(layer, "Near").motion;
            Assert.That(near.children.Length, Is.GreaterThanOrEqualTo(2));
            foreach (var child in near.children)
                Assert.That(Shapes(child.motion, "Face"), Is.EquivalentTo(Expected(value)), "every child of the link layer's tree drops the same shapes, at " + child.threshold);
            Assert.That(Shapes(State(layer, "Far").motion, "Face"), Does.Not.Contain("D"));
        }

        // ---- what counts as moving -----------------------------------------------------------------------------

        [Test]
        public void TwoDefaultRulesDoNotKeepEachOthersShapes()
        {
            var left = menu.AddEntry(EntryKind.Toggle, "Left");
            left.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true, value = ShapeSetValue.Default });
            var right = menu.AddEntry(EntryKind.Toggle, "Right");
            right.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true, value = ShapeSetValue.Default });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            foreach (var name in new[] { "AD: Left (pin)", "AD: Right (pin)" })
                foreach (var state in new[] { "On", "Off" })
                    Assert.That(Curve(State(Layer(fx, name), state).motion, "Body", "Eye_Big"), Is.Null, name + " " + state + ": a rule's own curve never counts as something that moves the shape");
        }

        [Test]
        public void AZeroRuleThatMovesAShapeKeepsAnotherRulesDefaultForIt()
        {
            avatar.Body.SetBlendShapeWeight(avatar.Mesh.GetBlendShapeIndex("Eye_Big"), 40f);
            var clear = menu.AddEntry(EntryKind.Toggle, "Clear");
            clear.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true, value = ShapeSetValue.Zero });
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true, value = ShapeSetValue.Default });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            Assert.That(Curve(State(Layer(fx, "AD: Clear (pin)"), "On").motion, "Body", "Eye_Big").Evaluate(0f), Is.EqualTo(0f), "the 0 rule takes the shape away from its 40");
            Assert.That(Curve(State(Layer(fx, "AD: Face (pin)"), "On").motion, "Body", "Eye_Big")?.Evaluate(0f), Is.EqualTo(40f),
                "so the later Default rule still holds it at 40 while both are on");
            Assert.That(Curve(State(Layer(fx, "AD: Face (pin)"), "On").motion, "Body", "Blush"), Is.Null, "a shape at 0 that nothing moves is dropped from both");
            Assert.That(Curve(State(Layer(fx, "AD: Clear (pin)"), "On").motion, "Body", "Blush"), Is.Null);
        }

        [Test]
        public void APinRowIsNeverDroppedAndOtherPinRowsCountAsMoving()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            face.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true, value = ShapeSetValue.Default });
            var wide = menu.AddEntry(EntryKind.Toggle, "Wide");
            wide.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 100f, mode = ShapeEffectMode.Pin });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var on = State(Layer(fx, "AD: Face (pin)"), "On").motion;
            Assert.That(Curve(on, "Body", "Blush")?.Evaluate(0f), Is.EqualTo(100f), "the entry's own Pin row stays, though nothing else moves Blush");
            Assert.That(Curve(on, "Body", "Eye_Big"), Is.Not.Null, "another entry's Pin row moves Eye_Big, so the rule keeps holding it");
            Assert.That(Curve(State(Layer(fx, "AD: Wide (pin)"), "On").motion, "Body", "Eye_Big")?.Evaluate(0f), Is.EqualTo(100f), "and that Pin row stays too");
        }

        [Test]
        public void AVrcFuryLayerThatMovesAShapeKeepsItHeld()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true, value = ShapeSetValue.Default });
            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            NoErrors(errors);

            // VRCFury merges its own layers straight into the real controller between AvatarDoc's two passes.
            var committed = ControllerOf(avatar, VRCAvatarDescriptor.AnimLayerType.FX);
            var vrcFuryFx = new AnimatorController { name = committed.name, parameters = committed.parameters, layers = committed.layers };
            made.Add(vrcFuryFx);
            vrcFuryFx.AddLayer("[VF1] Something");
            var machine = vrcFuryFx.layers[vrcFuryFx.layers.Length - 1].stateMachine;
            made.Add(machine);
            var state = machine.AddState("Big");
            made.Add(state);
            state.motion = Clip("Eye_Big moves", "Body", "Eye_Big");
            SetController(avatar, VRCAvatarDescriptor.AnimLayerType.FX, vrcFuryFx);

            errors.AddRange(ErrorReport.CaptureErrors(() => AvatarDoctorPlugin.FinishAfterOtherTools(context)));

            NoErrors(errors);
            var fx = ControllerOf(avatar, VRCAvatarDescriptor.AnimLayerType.FX);
            var on = State(Layer(fx, "AD: Face (pin)"), "On").motion;
            Assert.That(Curve(on, "Body", "Eye_Big"), Is.Not.Null, "the VRCFury layer moves Eye_Big, so the rule holds it");
            Assert.That(Curve(on, "Body", "Blush"), Is.Null, "and nothing moves Blush");
        }

        [Test]
        public void AClipAnotherLayerAlsoPlaysIsLeftWhole()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true, value = ShapeSetValue.Default });
            var context = FirstPass(out var errors);
            NoErrors(errors);
            var pinOn = State(Layer(ControllerOf(avatar, VRCAvatarDescriptor.AnimLayerType.FX), "AD: Face (pin)"), "On").motion;
            Assert.That(Curve(pinOn, "Body", "Blush"), Is.Not.Null, "sanity: the rule wrote Blush");
            AppendForeignLayer("[VF1] Reuse", pinOn);

            errors.AddRange(ErrorReport.CaptureErrors(() => AvatarDoctorPlugin.FinishAfterOtherTools(context)));

            NoErrors(errors);
            var fx = ControllerOf(avatar, VRCAvatarDescriptor.AnimLayerType.FX);
            var on = State(Layer(fx, "AD: Face (pin)"), "On").motion;
            Assert.That(State(Layer(fx, "[VF1] Reuse"), "[VF1] Reuse state").motion, Is.SameAs(on), "sanity: the other layer still plays the very same clip");
            Assert.That(Curve(on, "Body", "Blush"), Is.Not.Null, "a clip another layer also plays keeps the rule's curves: that layer moves them too");
            Assert.That(Curve(on, "Body", "Eye_Big"), Is.Not.Null);
            Assert.That(Pruned(errors), Is.Empty, "and the rule says nothing was left out");
        }

        [Test]
        public void AnOverrideTargetCountsAsMoving()
        {
            var big = menu.AddEntry(EntryKind.Override, "Big eyes");
            big.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 70f });
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true, value = ShapeSetValue.Default });
            var context = FirstPass(out var errors);
            NoErrors(errors);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            errors.AddRange(ErrorReport.CaptureErrors(() => OverrideBuilder.RunAfterModularAvatar(context)));
            context.DeactivateAllExtensionContexts();

            errors.AddRange(ErrorReport.CaptureErrors(() => AvatarDoctorPlugin.FinishAfterOtherTools(context)));

            NoErrors(errors);
            var on = State(Layer(ControllerOf(avatar, VRCAvatarDescriptor.AnimLayerType.FX), "AD: Face (pin)"), "On").motion;
            Assert.That(Curve(on, "Body", "Eye_Big")?.Evaluate(0f), Is.EqualTo(70f), "the overridden shape keeps its curve, at the override's value");
            Assert.That(Curve(on, "Body", "Blush"), Is.Null, "a shape nothing moves is still dropped");
            Assert.That(Reported(errors, "ad.info.override_applied"), Is.True, "the override says it holds the shape in an animation that really has it");
            Assert.That(Reported(errors, "ad.warn.override_unused"), Is.False);
        }

        // ---- the final pass's order ----------------------------------------------------------------------------

        [TestCase(ShapeSetValue.Default)]
        [TestCase(ShapeSetValue.Zero)]
        public void ABlendShapeTriggerReadsTheRightValueFromAShapeOnlyARuleHolds(ShapeSetValue value)
        {
            int blush = avatar.Mesh.GetBlendShapeIndex("Blush");
            avatar.Body.SetBlendShapeWeight(blush, 30f);
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true, value = value });
            var link = menu.AddEntry(EntryKind.Trigger, "Blush link");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            link.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var context = FirstPass(out var errors);
            NoErrors(errors);
            // A layer another tool adds after AvatarDoc's, outside the animator services: only the move in the final
            // pass puts the pin layers after it.
            AppendForeignLayer("[VF1] Thing", null);

            errors.AddRange(ErrorReport.CaptureErrors(() => AvatarDoctorPlugin.FinishAfterOtherTools(context)));
            var fx = ControllerOf(avatar, VRCAvatarDescriptor.AnimLayerType.FX);

            NoErrors(errors);
            const string mirror = "AD/Shape/Blush link";
            Assert.That(fx.parameters.Single(p => p.name == mirror).defaultFloat, Is.EqualTo(30f), "the trigger starts from the shape's own weight");
            var faceOn = State(Layer(fx, "AD: Face (pin)"), "On").motion;
            if (value == ShapeSetValue.Default)
            {
                Assert.That(Curve(faceOn, "Body", "Blush"), Is.Null, "nothing else moves Blush, so the Default rule drops it");
                Assert.That(Parameter(faceOn, mirror), Is.Null, "and the trigger keeps reading 30, the value the shape shows while the toggle is on");
                Assert.That(Reported(errors, "ad.warn.link_no_fx_clip"), Is.True, "the build says nothing in FX moves Blush any more, which is true");
            }
            else
            {
                Assert.That(Curve(faceOn, "Body", "Blush").Evaluate(0f), Is.EqualTo(0f), "the 0 rule keeps Blush, whose default is 30");
                Assert.That(Parameter(faceOn, mirror)?.Evaluate(0f), Is.EqualTo(0f), "and the trigger reads the 0 the toggle holds it at");
                Assert.That(Reported(errors, "ad.warn.link_no_fx_clip"), Is.False, "the toggle's 0 rule moves Blush, so the trigger has something to follow");
            }
            var names = fx.layers.Select(l => l.name).ToList();
            Assert.That(names.IndexOf("[VF1] Thing"), Is.GreaterThan(names.IndexOf("Base")), "sanity: the other tool's layer is in FX");
            Assert.That(names.Skip(names.Count - 2), Is.EquivalentTo(new[] { "AD: Face (pin)", "AD: Blush link (shape)" }), "the pin layers still go last, after the other tool's layer");
        }

        // ---- preview and words ---------------------------------------------------------------------------------

        [TestCase(ShapeSetValue.Default, 50f)]
        [TestCase(ShapeSetValue.Zero, 0f)]
        public void ThePreviewShowsWhatTheBuildShowsForADroppedShape(ShapeSetValue value, float weight)
        {
            int eyeBig = avatar.Mesh.GetBlendShapeIndex("Eye_Big");
            avatar.Body.SetBlendShapeWeight(eyeBig, weight);
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true, value = value });
            var states = PreviewModel.Compute(avatar.Root.transform, new[] { menu }, new Dictionary<string, float> { [face.id] = 1f }, SceneReader.Direct);
            float previewed = states.TryGetValue(avatar.Body, out var state) && state.Shapes.TryGetValue(eyeBig, out var shown) ? shown : avatar.Body.GetBlendShapeWeight(eyeBig);

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            Assert.That(Curve(State(Layer(fx, "AD: Face (pin)"), "On").motion, "Body", "Eye_Big"), Is.Null, "sanity: the build dropped the shape, so it shows its scene weight while the toggle is on");
            Assert.That(previewed, Is.EqualTo(avatar.Body.GetBlendShapeWeight(eyeBig)), "the preview shows the same value");
            Assert.That(previewed, Is.EqualTo(weight));
        }

        [Test]
        public void TheBuildSaysHowManyShapesEachRuleStillHolds()
        {
            var face = FaceScene();
            var holder = menu.AddEntry(EntryKind.Toggle, "Holder");
            holder.shapeSets.Add(FaceRule(face, ShapeSetValue.Default));
            // Every shape of this one moves, so it holds them all and says nothing.
            var all = menu.AddEntry(EntryKind.Toggle, "All");
            all.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            all.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true, value = ShapeSetValue.Default });
            var wide = menu.AddEntry(EntryKind.Toggle, "Wide");
            wide.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 100f });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            Assert.That(Pruned(errors), Is.EqualTo(new[] { string.Format(L.Text("ad.info.shape_set_pruned"), "Holder", 4, 6, "Face") }),
                "one line for the rule that holds fewer than all of its shapes: A, B, C and F of six");
            Assert.That(errors.Single(e => e.TheError is SimpleError s && s.TitleKey == "ad.info.shape_set_pruned").TheError.Severity, Is.EqualTo(ErrorSeverity.Information));
            Assert.That(L.Text("ad.info.shape_set_pruned"), Is.EqualTo("\"{0}\" holds {1} of {2} shapes on \"{3}\"; the others never move, so they stay as they are."));
        }

        [Test]
        public void TheLineHasARealSingularForAOneShapeRule()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            face.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true, value = ShapeSetValue.Default });

            BuildAll(out var errors);

            NoErrors(errors);
            Assert.That(Pruned(errors), Is.EqualTo(new[] { string.Format(L.Text("ad.info.shape_set_pruned.one"), "Face", 0, 1, avatar.Body.name) }), "Eye_Big is the one shape the rule could hold, and nothing moves it");
        }
    }
}
#endif
