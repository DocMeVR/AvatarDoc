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

namespace DocMe.AvatarDoctor.Editor
{
    public class ShapeSetTests
    {
        TestAvatar avatar;
        AvatarDoc menu;

        [SetUp]
        public void SetUp()
        {
            GeneratedAssets.Note();
            avatar = TestAvatar.Create();
            menu = avatar.AddMenu();
            AvatarPlanCache.Invalidate();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            AvatarPlanCache.Invalidate();
            GeneratedAssets.CleanUp();
        }

        (MenuTree tree, ParameterPlan plan) Model(params AvatarDoc[] menus)
        {
            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, menus);
            return (tree, ParameterPlanner.Plan(tree, new Dictionary<string, PlannedType> { { "Existing", PlannedType.Bool } }));
        }

        /// <summary>Runs model, defaults and the animator builder inside an NDMF context and returns the committed FX controller.</summary>
        AnimatorController BuildAnimator(out List<ErrorContext> errors, params AvatarDoc[] menus)
        {
            var (tree, plan) = Model(menus);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var captured = ErrorReport.CaptureErrors(() => L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState)));
            context.DeactivateAllExtensionContexts();
            errors = captured;
            return FxOf(avatar);
        }

        static AnimatorController FxOf(TestAvatar a)
            => (AnimatorController)a.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;

        static AnimatorControllerLayer Layer(AnimatorController fx, string name) => fx.layers.Single(l => l.name == name);
        static AnimatorState State(AnimatorControllerLayer layer, string name) => layer.stateMachine.states.Single(s => s.state.name == name).state;

        static AnimationCurve CurveOf(Motion motion, string path, string property)
            => AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), property));

        [Test]
        public void CaptureTakesEveryShapeAboveZero()
        {
            avatar.Body.SetBlendShapeWeight(0, 40f);
            avatar.Body.SetBlendShapeWeight(1, 0f);

            var rows = ShapeSetModel.Capture(avatar.Body, new List<BlendShapeTarget>());

            Assert.That(rows.Count, Is.EqualTo(1));
            Assert.That(rows[0].Shape, Is.EqualTo("Blush"));
            Assert.That(rows[0].Weight, Is.EqualTo(40f));
        }

        [Test]
        public void CaptureUpdatesAnExistingRowInPlace()
        {
            avatar.Body.SetBlendShapeWeight(0, 40f);
            var existingRow = new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 5f, mode = ShapeEffectMode.Fixed };

            var rows = ShapeSetModel.Capture(avatar.Body, new List<BlendShapeTarget> { existingRow });

            Assert.That(rows.Count, Is.EqualTo(1));
            Assert.That(rows[0].Existing, Is.SameAs(existingRow), "identifies the row to update instead of adding a duplicate");
            Assert.That(existingRow.weight, Is.EqualTo(5f), "Capture is pure: it never writes to the row it found");
        }

        [Test]
        public void CaptureMakesRowsPinned()
        {
            avatar.Body.SetBlendShapeWeight(0, 40f);
            var face = menu.AddEntry(EntryKind.Toggle, "Face");

            EntryEditing.CaptureShapeSet(menu, face, avatar.Body);

            Assert.That(face.blendShapes.Single().mode, Is.EqualTo(ShapeEffectMode.Pin), "the caller applies Pin mode after Capture reports the weight");
        }

        [Test]
        public void CaptureFillsARadialsUpperEnd()
        {
            avatar.Body.SetBlendShapeWeight(0, 40f);
            var eyes = menu.AddEntry(EntryKind.Radial, "Eyes");

            int count = EntryEditing.CaptureShapeSet(menu, eyes, avatar.Body, intoUpperEnd: true, mode: null, withRule: false);

            Assert.That(count, Is.EqualTo(1));
            var row = eyes.blendShapes.Single();
            Assert.That(row.maxWeight, Is.EqualTo(40f), "the scene weight becomes the upper end");
            Assert.That(row.minWeight, Is.EqualTo(0f), "the lower end is 0, so turning the radial up reaches the captured pose");
            Assert.That(eyes.shapeSets, Is.Empty, "a radial offers no shape set rule");
        }

        [Test]
        public void CaptureOnATriggerAddsPinnedRows()
        {
            avatar.Body.SetBlendShapeWeight(0, 40f);
            var blink = menu.AddEntry(EntryKind.Trigger, "Blink");

            int count = EntryEditing.CaptureShapeSet(menu, blink, avatar.Body, mode: ShapeEffectMode.Pin, withRule: false);

            Assert.That(count, Is.EqualTo(1));
            var row = blink.blendShapes.Single();
            Assert.That(row.weight, Is.EqualTo(40f));
            Assert.That(row.mode, Is.EqualTo(ShapeEffectMode.Pin), "a trigger holds its effects while it is met, exactly like a toggle");
            Assert.That(blink.shapeSets, Is.Empty, "a trigger's condition effects carry no rule");
        }

        [Test]
        public void CaptureOnALinkedTriggerAddsFixedRows()
        {
            avatar.Body.SetBlendShapeWeight(0, 40f);
            var link = menu.AddEntry(EntryKind.Trigger, "Link");
            link.source = TriggerSource.BlendShape;

            int count = EntryEditing.CaptureShapeSet(menu, link, avatar.Body, mode: ShapeEffectMode.Fixed, withRule: false);

            Assert.That(count, Is.EqualTo(1));
            var row = link.blendShapes.Single();
            Assert.That(row.weight, Is.EqualTo(40f));
            Assert.That(row.mode, Is.EqualTo(ShapeEffectMode.Fixed), "a link has no pin, so a capture takes the section's own mode");
        }

        [Test]
        public void CaptureOnAnOverrideAddsItsPins()
        {
            avatar.Body.SetBlendShapeWeight(0, 40f);
            var rule = menu.AddEntry(EntryKind.Override, "Silence blush");

            int count = EntryEditing.CaptureShapeSet(menu, rule, avatar.Body, mode: null, withRule: false);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(rule.blendShapes.Single().weight, Is.EqualTo(40f), "an override is a pin by nature, so a capture only needs the weight");
        }

        [Test]
        public void UndoOfACaptureRestoresTheRowsItChanged()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 5f, mode = ShapeEffectMode.Fixed });
            avatar.Body.SetBlendShapeWeight(0, 40f);

            Undo.IncrementCurrentGroup();
            EntryEditing.CaptureShapeSet(menu, face, avatar.Body);
            Assert.That(face.blendShapes[0].weight, Is.EqualTo(40f), "sanity: the capture applied");

            Undo.PerformUndo();

            Assert.That(face.blendShapes[0].weight, Is.EqualTo(5f), "undo restores the row Capture changed");
            Assert.That(face.blendShapes[0].mode, Is.EqualTo(ShapeEffectMode.Fixed));
            Assert.That(face.shapeSets, Is.Empty, "undo also removes the rule the capture added");
        }

        [Test]
        public void CaptureOfAnAllZeroMeshSaysSo()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            view.Tree.SelectEntry(face.id);
            var blendShapes = view.Detail.Q<TargetListView>("ad-blendshapes");

            blendShapes.CaptureFrom(avatar.Body);

            Assert.That(face.blendShapes, Is.Empty);
            Assert.That(face.shapeSets, Is.Empty);
            Assert.That(view.Detail.Q<Label>("ad-shape-set-status").text,
                Is.EqualTo(string.Format(L.Text("ad.info.capture_nothing_above_zero"), avatar.Body.name)));
        }

        [Test]
        public void ZeroedShapesLeavesOutWhatTheEntryNames()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            var rule = new ShapeSetRule { mesh = avatar.Body, zeroOthers = true };

            var zeroed = ShapeSetModel.HeldShapes(face, rule, null);

            Assert.That(zeroed, Is.EqualTo(new List<string> { "Eye_Big" }));
        }

        [Test]
        public void RuleZeroesTheOtherShapesWhileOn()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            face.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true });

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            var on = State(Layer(fx, "AD: Face (pin)"), "On");
            Assert.That(CurveOf(on.motion, "Body", "blendShape.Eye_Big").Evaluate(0f), Is.EqualTo(0f), "the rule zeroes the shape the entry does not name");
            Assert.That(CurveOf(on.motion, "Body", "blendShape.Blush").Evaluate(0f), Is.EqualTo(100f), "the entry's own row wins over the rule");
        }

        [Test]
        public void RuleWritesNothingWhileOff()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true });

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            var off = State(Layer(fx, "AD: Face (pin)"), "Off");
            Assert.That(CurveOf(off.motion, "Body", "blendShape.Blush"), Is.Null);
            Assert.That(CurveOf(off.motion, "Body", "blendShape.Eye_Big"), Is.Null);
        }

        [Test]
        public void ARuleZeroedShapeIsReleasedToItsSceneWeight()
        {
            // An outfit that is on by default and zeroes every other shape of the body: Eye_Big, which the entry
            // does not name, sits at 40 in the scene.
            var outfit = menu.AddEntry(EntryKind.Toggle, "Outfit");
            outfit.defaultOn = true;
            outfit.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true });
            avatar.Body.SetBlendShapeWeight(1, 40f);

            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error), Is.Empty);
            Assert.That(avatar.Body.GetBlendShapeWeight(1), Is.EqualTo(0f), "on by default, so the rule's 0 is what the avatar starts with");

            AvatarDoctorPlugin.FinishAfterOtherTools(context);

            var off = State(Layer(FxOf(avatar), "AD: Outfit (pin)"), "Off");
            var curve = CurveOf(off.motion, "Body", "blendShape.Eye_Big");
            Assert.That(curve, Is.Not.Null, "nothing else animates Eye_Big, so turning the outfit off has to hand it back");
            Assert.That(curve.Evaluate(0f), Is.EqualTo(40f), "back to the scene weight from before the build, not to the 0 the rule wrote into the scene");
        }

        [Test]
        public void EntryWithOnlyARuleStillGetsItsPinLayer()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true });

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            Assert.That(fx.layers.Any(l => l.name == "AD: Face"), Is.False, "no rows outside the rule means no main layer");
            Assert.That(Layer(fx, "AD: Face (pin)").stateMachine.states.Select(s => s.state.name), Is.EquivalentTo(new[] { "Off", "On" }));
        }

        [Test]
        public void PinLayerFromATriggerIsAlsoMovedLast()
        {
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            var blink = menu.AddEntry(EntryKind.Trigger, "Blink");
            blink.source = TriggerSource.Toggle;
            blink.sourceToggleId = hat.id;
            blink.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();

            AvatarDoctorBuilder.Run(context);
            context.DeactivateAllExtensionContexts();

            var pins = context.GetState<PinLayerState>();
            Assert.That(pins.Ready, Is.True);
            Assert.That(pins.HasAny, Is.True, "a trigger's own pin layer (built after AnimatorBuilder.Build) counts too, not only a toggle's");
        }

        [Test]
        public void PinLayersSitAfterALaterMergedLayer()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            var (tree, plan) = Model(menu);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState));
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx);
            // Simulates a layer Modular Avatar merges into the same virtual controller after AvatarDoc has built.
            fx.AddLayer(LayerPriority.Default, "MA Face Tracking");
            context.DeactivateAllExtensionContexts();

            var names = FxOf(avatar).layers.Select(l => l.name).ToList();
            Assert.That(names.IndexOf("MA Face Tracking"), Is.LessThan(names.IndexOf("AD: Face (pin)")));
        }

        [Test]
        public void PinLayersAreMovedLastByTheFinalPass()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            var (tree, plan) = Model(menu);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            var pinNames = new List<string>();
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState, pinNames));
            context.DeactivateAllExtensionContexts();

            // A tool that merges straight into the real controller, outside the animator services (VRCFury's own
            // build step), after AvatarDoc's pin layer has already been committed once.
            FxOf(avatar).AddLayer("Other Tool");

            var context2 = new BuildContext(avatar.Root, null);
            var asc2 = context2.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            AnimatorBuilder.MovePinLayersLast(asc2, pinNames);
            context2.DeactivateAllExtensionContexts();

            var names = FxOf(avatar).layers.Select(l => l.name).ToList();
            Assert.That(names.Last(), Is.EqualTo("AD: Face (pin)"));
            Assert.That(names.IndexOf("Other Tool"), Is.LessThan(names.IndexOf("AD: Face (pin)")));
        }

        [Test]
        public void AForeignLayerNamedLikeAPinIsNotMoved()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            var (tree, plan) = Model(menu);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            var pinNames = new List<string>();
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState, pinNames));
            context.DeactivateAllExtensionContexts();

            // A creator's own layer that happens to end in " (pin)" but carries no AvatarDoc prefix, and was never
            // recorded by BuildPinLayer, so its exact name is not in pinNames.
            FxOf(avatar).AddLayer("Custom (pin)");

            var context2 = new BuildContext(avatar.Root, null);
            var asc2 = context2.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            AnimatorBuilder.MovePinLayersLast(asc2, pinNames);
            context2.DeactivateAllExtensionContexts();

            var names = FxOf(avatar).layers.Select(l => l.name).ToList();
            Assert.That(names.Last(), Is.EqualTo("AD: Face (pin)"), "only the real pin layer moves");
            Assert.That(names.IndexOf("Custom (pin)"), Is.LessThan(names.IndexOf("AD: Face (pin)")), "the foreign layer was left where it was appended");
        }

        [Test]
        public void AnEntryNamedLikeAPinKeepsItsPlace()
        {
            // A main (non-pin) layer whose display name itself ends in " (pin)": nothing pinned here, Fixed only.
            var face = menu.AddEntry(EntryKind.Toggle, "Face (pin)");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Fixed });
            var (tree, plan) = Model(menu);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            var pinNames = new List<string>();
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState, pinNames));
            context.DeactivateAllExtensionContexts();
            Assert.That(pinNames, Is.Empty, "no pin layer was created, so nothing was recorded");

            FxOf(avatar).AddLayer("Other Tool");

            var context2 = new BuildContext(avatar.Root, null);
            var asc2 = context2.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            AnimatorBuilder.MovePinLayersLast(asc2, pinNames);
            context2.DeactivateAllExtensionContexts();

            var names = FxOf(avatar).layers.Select(l => l.name).ToList();
            Assert.That(names.IndexOf("AD: Face (pin)"), Is.LessThan(names.IndexOf("Other Tool")), "the main layer was never a pin layer and stays where it was built");
        }

        [Test]
        public void AMergedLayerNamedLikeAPinIsNotMoved()
        {
            // A merge source controller that ships a layer literally called like a pin layer, in memory like
            // avatar.Fx itself: MergeBuilder.Build clones it, and the clone joins the real FX controller.
            var controller = new AnimatorController { name = "Merged" };
            controller.AddLayer("AD: Something (pin)");
            var decoyLayers = controller.layers;
            decoyLayers[decoyLayers.Length - 1].stateMachine.AddState("Idle").motion = new AnimationClip { name = "Idle" };

            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            menu.animators.Add(AnimatorMerge.Create(controller));

            var (tree, plan) = Model(menu);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            var pinNames = new List<string>();
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState, pinNames));
            L.Report(MergeBuilder.Build(asc, tree, plan, true));
            context.DeactivateAllExtensionContexts();

            Assert.That(pinNames, Is.EquivalentTo(new[] { "AD: Face (pin)" }), "only the layer BuildPinLayer made was recorded");

            var context2 = new BuildContext(avatar.Root, null);
            var asc2 = context2.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            AnimatorBuilder.MovePinLayersLast(asc2, pinNames);
            context2.DeactivateAllExtensionContexts();

            var names = FxOf(avatar).layers.Select(l => l.name).ToList();
            Assert.That(names.Last(), Is.EqualTo("AD: Face (pin)"), "only the real pin layer moves");
            Assert.That(names.IndexOf("AD: Something (pin)"), Is.LessThan(names.IndexOf("AD: Face (pin)")), "the merged layer's name alone does not make it one of ours");
        }

        [Test]
        public void RuleOnARadialIsAnError()
        {
            var radial = menu.AddEntry(EntryKind.Radial, "Eyes");
            radial.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big" });
            radial.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true });

            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });

            Assert.That(tree.Messages.Any(m => m.Key == "ad.error.shape_set_not_a_toggle" && m.EntryIds.Contains(radial.id)), Is.True);
        }

        [Test]
        public void RuleWithAMeshOutsideTheAvatarIsAnError()
        {
            var stray = new GameObject("Stray");
            try
            {
                var strayMesh = stray.AddComponent<SkinnedMeshRenderer>();
                strayMesh.sharedMesh = TestAvatar.QuadWithShapes("Other");
                var face = menu.AddEntry(EntryKind.Toggle, "Face");
                face.shapeSets.Add(new ShapeSetRule { mesh = strayMesh, zeroOthers = true });

                var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });

                Assert.That(tree.Messages.Any(m => m.Key == "ad.error.shape_set_outside_avatar" && m.EntryIds.Contains(face.id)), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(stray);
            }
        }

        [Test]
        public void TwoRulesForOneMeshAreMerged()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = false });
            face.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true });

            var effective = ShapeSetModel.EffectiveRules(face).ToList();

            Assert.That(effective.Count, Is.EqualTo(1));
            Assert.That(effective[0].zeroOthers, Is.True, "on if either raw row is on");
            Assert.That(ShapeSetModel.HeldShapes(face, effective[0], null).Count, Is.EqualTo(2));
        }

        [Test]
        public void LargeRuleWarns()
        {
            var shapeNames = Enumerable.Range(0, 151).Select(i => "S" + i).ToArray();
            var big = new GameObject("Big");
            big.transform.SetParent(avatar.Root.transform);
            var bigMesh = big.AddComponent<SkinnedMeshRenderer>();
            bigMesh.sharedMesh = TestAvatar.QuadWithShapes(shapeNames);
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(new ShapeSetRule { mesh = bigMesh, zeroOthers = true });

            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });

            Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.shape_set_large" && m.EntryIds.Contains(face.id)), Is.True);
        }

        [Test]
        public void PreviewShowsTheOtherShapesAtZero()
        {
            avatar.Body.SetBlendShapeWeight(1, 50f);
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true });

            var states = PreviewModel.Compute(avatar.Root.transform, new[] { menu },
                new Dictionary<string, float> { [face.id] = 1f }, SceneReader.Direct);

            Assert.That(states[avatar.Body].Shapes[1], Is.EqualTo(0f));
        }

        [Test]
        public void SummaryNamesTheRuleAndItsCount()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true });
            var plan = AvatarPlanCache.GetFor(menu);

            var summary = SummaryBuilder.Build(plan, menu, SummaryFormat.Plain);

            Assert.That(summary, Does.Contain(string.Format(L.Text("ad.summary.shape_sets.line"), "Face", avatar.Body.name, 2)));
        }

        [Test]
        public void LockedComponentRefusesCaptureAndRuleWrites()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true });
            avatar.Body.SetBlendShapeWeight(0, 40f);
            menu.locked = true;

            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            view.Tree.SelectEntry(face.id);
            var blendShapes = view.Detail.Q<TargetListView>("ad-blendshapes");

            blendShapes.CaptureFrom(avatar.Body);
            blendShapes.SetZeroOthers(0, false);
            blendShapes.RemoveRule(0);

            Assert.That(face.blendShapes, Is.Empty, "capture refused while locked");
            Assert.That(face.shapeSets.Count, Is.EqualTo(1), "the rule was not removed");
            Assert.That(face.shapeSets[0].zeroOthers, Is.True, "zeroOthers was not changed");
        }

        [Test]
        public void RuleAddsNoParameterAndNoBit()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true });

            var (_, plan) = Model(menu);

            Assert.That(plan.Parameters.Count, Is.EqualTo(1), "only the toggle's own parameter");
            Assert.That(plan.Parameters[0].Bits, Is.EqualTo(1));
        }
    }
}
#endif
