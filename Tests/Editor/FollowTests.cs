#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace DocMe.AvatarDoctor.Editor
{
    public class FollowTests
    {
        TestAvatar avatar;

        [SetUp]
        public void SetUp()
        {
            GeneratedAssets.Note();
            avatar = TestAvatar.Create();
            Platform.OverrideMobile = false;
            AvatarPlanCache.Invalidate();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            Platform.OverrideMobile = null;
            AvatarPlanCache.Invalidate();
            GeneratedAssets.CleanUp();
        }

        (MenuTree tree, ParameterPlan plan) Model(params AvatarDoc[] menus)
        {
            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, menus);
            return (tree, ParameterPlanner.Plan(tree, new Dictionary<string, PlannedType> { { "Existing", PlannedType.Bool } }));
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

        static bool HasCurve(Motion motion, string path, System.Type type, string property)
            => AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve(path, type, property)) != null;

        /// <summary>
        /// Model, FollowBuilder, defaults, the animator builder, the trigger builder and FollowLayerBuilder, in the
        /// same order AvatarDoctorBuilder.Run uses them, without a real avatar clone: enough for anything that only
        /// needs the clips and the constraint values, not clone/scene separation.
        /// </summary>
        (AnimatorController fx, MenuTree tree, ParameterPlan plan, FollowPlan followPlan, List<ErrorContext> errors) BuildFull(params AvatarDoc[] menus)
        {
            var (tree, plan) = Model(menus);
            var context = new BuildContext(avatar.Root, null);
            var (followPlan, followMessages) = FollowBuilder.Build(context, tree);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var virtualFx);
            var existing = ParametersWriter.ExistingParameters(avatar.Descriptor.expressionParameters, virtualFx);
            var touch = TouchBuilder.Describe(tree, existing, plan);
            var (triggerPlans, triggerMessages) = TriggerPlanner.Plan(tree, plan, existing, touch);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState, triggerPlans);
            var errors = ErrorReport.CaptureErrors(() =>
            {
                L.Report(followMessages);
                L.Report(triggerMessages);
                L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState));
                L.Report(TriggerBuilder.Build(asc, tree, triggerPlans, baseState));
                L.Report(FollowLayerBuilder.Build(asc, tree, plan, followPlan, triggerPlans));
            });
            context.DeactivateAllExtensionContexts();
            return (FxOf(avatar), tree, plan, followPlan, errors);
        }

        MenuEntry ParameterTrigger(AvatarDoc menu, string name, string parameter, float value)
        {
            var trigger = menu.AddEntry(EntryKind.Trigger, name);
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = parameter;
            trigger.condition = ParameterCondition.Equals;
            trigger.conditionValue = value;
            return trigger;
        }

        [Test]
        public void FollowMakesTheObjectFollowWhileOn()
        {
            var menu = avatar.AddMenu();
            var hold = menu.AddEntry(EntryKind.Toggle, "Hold");
            hold.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform });

            var (fx, _, _, followPlan, errors) = BuildFull(menu);

            Assert.That(errors, Is.Empty);
            Assert.That(followPlan.TryGetSource(hold, avatar.Glasses, out var constraint, out var index), Is.True);
            Assert.That(index, Is.EqualTo(1));
            Assert.That(constraint.Sources[0].SourceTransform, Is.SameAs(avatar.Glasses.transform.parent));
            Assert.That(constraint.Sources[1].SourceTransform, Is.SameAs(avatar.Hat.transform));

            // Entries never write their own follow's weight any more: the one layer FollowLayerBuilder made for
            // the object owns the source, so the claim lives there, not on "AD: Hold".
            var holdLayer = Layer(fx, "AD: Hold");
            foreach (var s in holdLayer.stateMachine.states)
                Assert.That(HasCurve(s.state.motion, "Glasses", typeof(VRCParentConstraint), "Sources.source1.Weight"), Is.False, s.state.name);

            var followLayer = Layer(fx, "AD: Follow Glasses");
            var claim = State(followLayer, "Hold");
            Assert.That(FloatValue(claim.motion, "Glasses", typeof(VRCParentConstraint), "Sources.source1.Weight"), Is.EqualTo(1f));
            Assert.That(FloatValue(claim.motion, "Glasses", typeof(VRCParentConstraint), "Sources.source0.Weight"), Is.EqualTo(0f));
        }

        [Test]
        public void OffClipPutsItBack()
        {
            var menu = avatar.AddMenu();
            var hold = menu.AddEntry(EntryKind.Toggle, "Hold");
            hold.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform });

            var (fx, _, _, followPlan, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);
            Assert.That(followPlan.TryGetSource(hold, avatar.Glasses, out var constraint, out var index), Is.True);

            // Off by default: the layer's own default state (Rest) already put it back, and FollowLayerBuilder
            // baked the same values onto the real constraint.
            Assert.That(constraint.Sources[0].Weight, Is.EqualTo(1f));
            Assert.That(constraint.Sources[index].Weight, Is.EqualTo(0f));

            var rest = State(Layer(fx, "AD: Follow Glasses"), "Rest");
            Assert.That(FloatValue(rest.motion, "Glasses", typeof(VRCParentConstraint), "Sources.source0.Weight"), Is.EqualTo(1f));
            Assert.That(FloatValue(rest.motion, "Glasses", typeof(VRCParentConstraint), "Sources.source" + index + ".Weight"), Is.EqualTo(0f));
        }

        [Test]
        public void KeepOffsetKeepsTheSceneOffset()
        {
            avatar.Hat.transform.position = new Vector3(1f, 2f, 3f);
            avatar.Hat.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            avatar.Glasses.transform.position = new Vector3(1.1f, 2.05f, 2.9f);
            avatar.Glasses.transform.rotation = Quaternion.Euler(10f, 100f, 5f);
            var extra = new GameObject("Extra");
            extra.transform.SetParent(avatar.Root.transform);
            extra.transform.position = new Vector3(50f, 50f, 50f);
            extra.transform.rotation = Quaternion.Euler(0f, 0f, 45f);

            var menu = avatar.AddMenu();
            var keep = menu.AddEntry(EntryKind.Toggle, "Keep");
            keep.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform, keepOffset = true });
            var snap = menu.AddEntry(EntryKind.Toggle, "Snap");
            snap.follows.Add(new FollowTarget { target = extra, follows = avatar.Hat.transform, keepOffset = false });

            var (_, _, _, followPlan, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);

            // keepOffset means the pose the object has relative to the target as posed in the scene, not the
            // pose it has under its own rest parent.
            var expectedPos = avatar.Hat.transform.InverseTransformPoint(avatar.Glasses.transform.position);
            var expectedRot = Quaternion.Inverse(avatar.Hat.transform.rotation) * avatar.Glasses.transform.rotation;
            followPlan.TryGetSource(keep, avatar.Glasses, out var keepConstraint, out var keepIndex);
            var keepSource = keepConstraint.Sources[keepIndex];
            AssertVector3(keepSource.ParentPositionOffset, expectedPos);
            AssertVector3(keepSource.ParentRotationOffset, expectedRot.eulerAngles);

            followPlan.TryGetSource(snap, extra, out var snapConstraint, out var snapIndex);
            var snapSource = snapConstraint.Sources[snapIndex];
            AssertVector3(snapSource.ParentPositionOffset, Vector3.zero, "off means the object snaps with no offset");
            AssertVector3(snapSource.ParentRotationOffset, Vector3.zero);
        }

        /// <summary>A Euler angle round trip can be off by a hair past the plain Vector3 float epsilon, so offset comparisons use a small tolerance instead of exact equality.</summary>
        static void AssertVector3(Vector3 actual, Vector3 expected, string message = null)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f), message);
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f), message);
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.001f), message);
        }

        [Test]
        public void TwoEntriesShareOneConstraint()
        {
            var menu = avatar.AddMenu();
            var a = menu.AddEntry(EntryKind.Toggle, "A");
            a.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform });
            var b = menu.AddEntry(EntryKind.Toggle, "B");
            b.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Root.transform });

            var (_, _, _, followPlan, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);

            Assert.That(followPlan.TryGetSource(a, avatar.Glasses, out var constraintA, out var indexA), Is.True);
            Assert.That(followPlan.TryGetSource(b, avatar.Glasses, out var constraintB, out var indexB), Is.True);
            Assert.That(constraintB, Is.SameAs(constraintA));
            Assert.That(indexA, Is.Not.EqualTo(indexB));
            Assert.That(avatar.Glasses.GetComponents<VRCParentConstraint>().Length, Is.EqualTo(1));
        }

        [Test]
        public void TwoFollowsOfOneObjectNeverBlendTogether()
        {
            var menu = avatar.AddMenu();
            var grabL = ParameterTrigger(menu, "Grab L", "GestureLeft", 1f);
            grabL.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform });
            var grabR = ParameterTrigger(menu, "Grab R", "GestureRight", 1f);
            grabR.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Root.transform });

            var (fx, _, _, followPlan, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);
            followPlan.TryGetSource(grabL, avatar.Glasses, out _, out var indexL);
            followPlan.TryGetSource(grabR, avatar.Glasses, out _, out var indexR);

            // With the left hand's parameter on and the right one off, the built layer has exactly one state
            // where the left source is 1 and both the rest source and the right one are 0.
            var layer = Layer(fx, "AD: Follow Glasses");
            var claimL = State(layer, "Grab L");
            Assert.That(FloatValue(claimL.motion, "Glasses", typeof(VRCParentConstraint), "Sources.source" + indexL + ".Weight"), Is.EqualTo(1f));
            Assert.That(FloatValue(claimL.motion, "Glasses", typeof(VRCParentConstraint), "Sources.source" + indexR + ".Weight"), Is.EqualTo(0f));
            Assert.That(FloatValue(claimL.motion, "Glasses", typeof(VRCParentConstraint), "Sources.source0.Weight"), Is.EqualTo(0f));
        }

        [Test]
        public void LaterClaimWinsWhileBothAreMet()
        {
            var menu = avatar.AddMenu();
            var grabL = ParameterTrigger(menu, "Grab L", "GestureLeft", 1f);
            grabL.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform });
            var grabR = ParameterTrigger(menu, "Grab R", "GestureRight", 1f);
            grabR.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Root.transform });

            var (fx, _, _, _, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);

            // Starting from Rest with both conditions met, Grab R (the later entry) must be checked first so
            // it wins outright instead of the two fighting over which one plays.
            var rest = State(Layer(fx, "AD: Follow Glasses"), "Rest");
            int indexOfR = System.Array.FindIndex(rest.transitions, t => t.destinationState.name == "Grab R");
            int indexOfL = System.Array.FindIndex(rest.transitions, t => t.destinationState.name == "Grab L");
            Assert.That(indexOfR, Is.GreaterThanOrEqualTo(0));
            Assert.That(indexOfL, Is.GreaterThanOrEqualTo(0));
            Assert.That(indexOfR, Is.LessThan(indexOfL));
        }

        /// <summary>
        /// The list-order check above is not enough on its own: it would still pass if the layer thrashed between
        /// the two claims once both were entered, since AnyState-style order only decides where Rest goes first,
        /// not what happens once a claim state is reached. This asserts the actual shape that prevents the
        /// thrash: no transition anywhere leads back to an earlier claim.
        /// </summary>
        [Test]
        public void TheLaterClaimHoldsWhileBothAreMet()
        {
            var menu = avatar.AddMenu();
            var grabL = ParameterTrigger(menu, "Grab L", "GestureLeft", 1f);
            grabL.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform });
            var grabR = ParameterTrigger(menu, "Grab R", "GestureRight", 1f);
            grabR.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Root.transform });

            var (fx, _, _, _, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);

            var layer = Layer(fx, "AD: Follow Glasses");
            var stateL = State(layer, "Grab L");
            var stateR = State(layer, "Grab R");

            // From the later claim's own state, nothing leads back to the earlier one: holding both conditions
            // can never slide the layer back to Grab L once it reached Grab R.
            Assert.That(stateR.transitions.Any(t => t.destinationState.name == "Grab L"), Is.False);
            // From the earlier claim's state, there is a transition to the later one, so the later claim
            // becoming met while the earlier one is still held moves straight there.
            Assert.That(stateL.transitions.Any(t => t.destinationState.name == "Grab R"), Is.True);
        }

        [Test]
        public void FollowLayerRestsWhenNothingIsMet()
        {
            var menu = avatar.AddMenu();
            var grab = ParameterTrigger(menu, "Grab", "GestureLeft", 1f);
            grab.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform });

            var (fx, _, _, _, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);
            Assert.That(Layer(fx, "AD: Follow Glasses").stateMachine.defaultState.name, Is.EqualTo("Rest"));
        }

        /// <summary>
        /// Repurposed from testing per-entry clip writes (finding 1 moved those into FollowLayerBuilder's own
        /// layer, so that scenario belongs to TwoFollowsOfOneObjectNeverBlendTogether and LaterClaimWinsWhileBothAreMet
        /// now): this still tests something real and distinct, the default-state side of "later wins", between two
        /// toggles that are each on by default rather than two conditions met at once.
        /// </summary>
        [Test]
        public void LaterEntryWinsWhileBothAreOn()
        {
            var menu = avatar.AddMenu();
            var a = menu.AddEntry(EntryKind.Toggle, "A");
            a.defaultOn = true;
            a.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform });
            var b = menu.AddEntry(EntryKind.Toggle, "B");
            b.defaultOn = true;
            b.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Root.transform });

            var (fx, _, _, followPlan, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);
            followPlan.TryGetSource(b, avatar.Glasses, out _, out var indexB);

            var layer = Layer(fx, "AD: Follow Glasses");
            Assert.That(layer.stateMachine.defaultState.name, Is.EqualTo("B"), "both start on; the later entry's claim wins the default state");

            // The real constraint was baked to match, so the avatar shows the same thing before any animator plays.
            followPlan.TryGetSource(a, avatar.Glasses, out var constraint, out _);
            Assert.That(constraint.Sources[indexB].Weight, Is.EqualTo(1f));
        }

        [Test]
        public void FollowOnATriggerFollowsWhileMet()
        {
            var menu = avatar.AddMenu();
            var grab = ParameterTrigger(menu, "Grab", "GestureLeft", 3f);
            grab.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform });

            var (fx, _, _, followPlan, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);
            Assert.That(followPlan.TryGetSource(grab, avatar.Glasses, out _, out var index), Is.True);

            var layer = Layer(fx, "AD: Follow Glasses");
            var claim = State(layer, "Grab");
            var rest = State(layer, "Rest");
            Assert.That(FloatValue(claim.motion, "Glasses", typeof(VRCParentConstraint), "Sources.source" + index + ".Weight"), Is.EqualTo(1f));
            Assert.That(FloatValue(rest.motion, "Glasses", typeof(VRCParentConstraint), "Sources.source" + index + ".Weight"), Is.EqualTo(0f));
        }

        [Test]
        public void BlendTimeFadesTheSources()
        {
            var menu = avatar.AddMenu();
            var hold = menu.AddEntry(EntryKind.Toggle, "Hold");
            hold.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform });
            hold.blendSeconds = 2f;

            var (fx, _, _, followPlan, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);
            followPlan.TryGetSource(hold, avatar.Glasses, out _, out var index);

            var layer = Layer(fx, "AD: Follow Glasses");
            var claim = State(layer, "Hold");
            var rest = State(layer, "Rest");
            var enter = rest.transitions.Single(t => t.destinationState.name == "Hold");
            Assert.That(enter.duration, Is.EqualTo(2f));
            Assert.That(claim.transitions.Single().duration, Is.EqualTo(2f));

            // The fade only means something if both ends of it actually carry the source weight curves.
            Assert.That(HasCurve(claim.motion, "Glasses", typeof(VRCParentConstraint), "Sources.source" + index + ".Weight"), Is.True);
            Assert.That(HasCurve(claim.motion, "Glasses", typeof(VRCParentConstraint), "Sources.source0.Weight"), Is.True);
            Assert.That(HasCurve(rest.motion, "Glasses", typeof(VRCParentConstraint), "Sources.source" + index + ".Weight"), Is.True);
            Assert.That(HasCurve(rest.motion, "Glasses", typeof(VRCParentConstraint), "Sources.source0.Weight"), Is.True);
        }

        [Test]
        public void MoreThanFifteenClaimsOnOneObjectIsAnError()
        {
            var menu = avatar.AddMenu();
            var entries = new List<MenuEntry>();
            for (int i = 0; i < FollowPlan.MaxClaims + 1; i++)
            {
                var toggle = menu.AddEntry(EntryKind.Toggle, "Claim " + i);
                toggle.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform });
                entries.Add(toggle);
            }

            var (tree, _) = Model(menu);

            var message = tree.Messages.Single(m => m.Key == "ad.error.follow_too_many_sources");
            Assert.That(message.Args[0], Is.EqualTo("Glasses"));
            Assert.That(message.Args[1], Is.EqualTo((FollowPlan.MaxClaims + 1).ToString()));
            Assert.That(tree.HasErrors, Is.True);

            // A build reached with this data regardless (stale or hand-edited) still never writes a 16th source.
            var context = new BuildContext(avatar.Root, null);
            var (followPlan, _) = FollowBuilder.Build(context, tree);
            Assert.That(followPlan.ClaimCount(avatar.Glasses), Is.GreaterThan(0).And.LessThanOrEqualTo(FollowPlan.MaxClaims));
        }

        /// <summary>FollowBuilder adds one source per follow ROW, not per entry, so the count the model checks against the limit has to count rows too, or one entry with two rows on the same object slips the count past what the builder actually builds.</summary>
        [Test]
        public void TwoRowsOfOneEntryCountTowardTheLimit()
        {
            var menu = avatar.AddMenu();
            for (int i = 0; i < FollowPlan.MaxClaims - 1; i++)
            {
                var toggle = menu.AddEntry(EntryKind.Toggle, "Claim " + i);
                toggle.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform });
            }
            // 14 single-row entries above, plus this one entry with two rows on the same object: 16 rows in
            // total across 15 entries, one row past the limit, which a count of distinct entries would miss.
            var twoRows = menu.AddEntry(EntryKind.Toggle, "TwoRows");
            twoRows.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform });
            twoRows.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Root.transform });

            var (tree, _) = Model(menu);

            var message = tree.Messages.Single(m => m.Key == "ad.error.follow_too_many_sources");
            Assert.That(message.Args[0], Is.EqualTo("Glasses"));
            Assert.That(message.Args[1], Is.EqualTo((FollowPlan.MaxClaims + 1).ToString()), "16 rows, not 15 entries");
            Assert.That(tree.HasErrors, Is.True);
        }

        [Test]
        public void ExistingConstraintIsAnError()
        {
            avatar.Glasses.AddComponent<VRCParentConstraint>();
            var menu = avatar.AddMenu();
            var hold = menu.AddEntry(EntryKind.Toggle, "Hold");
            hold.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform });

            var (tree, _) = Model(menu);

            Assert.That(tree.Messages.Any(m => m.Key == "ad.error.follow_existing_constraint" && m.EntryIds.Contains(hold.id)), Is.True);
            Assert.That(tree.HasErrors, Is.True);

            var context = new BuildContext(avatar.Root, null);
            var (followPlan, _) = FollowBuilder.Build(context, tree);
            Assert.That(followPlan.TryGetSource(hold, avatar.Glasses, out _, out _), Is.False);
            Assert.That(avatar.Glasses.GetComponents<VRCParentConstraint>().Length, Is.EqualTo(1), "the existing constraint was left alone and no second one was added");
        }

        [Test]
        public void FollowBuilderReportsAConstraintFoundOnlyAtBuildTime()
        {
            var menu = avatar.AddMenu();
            var hold = menu.AddEntry(EntryKind.Toggle, "Hold");
            hold.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform });

            var (tree, _) = Model(menu);
            Assert.That(tree.HasErrors, Is.False, "nothing on the object yet, so the model itself sees no problem");

            // Simulates another NDMF plugin adding its own constraint to the clone between the model's own
            // validation and FollowBuilder running: the model could never have seen this.
            avatar.Glasses.AddComponent<VRCParentConstraint>();
            var context = new BuildContext(avatar.Root, null);
            var (followPlan, messages) = FollowBuilder.Build(context, tree);

            Assert.That(messages.Any(m => m.Key == "ad.error.follow_existing_constraint" && m.EntryIds.Contains(hold.id)), Is.True);
            Assert.That(followPlan.TryGetSource(hold, avatar.Glasses, out _, out _), Is.False);
            Assert.That(avatar.Glasses.GetComponents<VRCParentConstraint>().Length, Is.EqualTo(1));
        }

        /// <summary>A blend shape linked trigger rewrites clips from a continuous source value; it has no on/off state a follow layer can ride on. ValidateFollows lets it through (it is a trigger, which does have an on/off state in general), so FollowBuilder gives it a source, and only FollowLayerBuilder discovers it cannot be driven. That has to be said, not silently dropped.</summary>
        [Test]
        public void AFollowOnABlendShapeLinkedTriggerIsReported()
        {
            var menu = avatar.AddMenu();
            var link = menu.AddEntry(EntryKind.Trigger, "Link");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            link.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform });

            var (tree, plan) = Model(menu);
            Assert.That(tree.HasErrors, Is.False, "the model itself sees nothing wrong with a follow on a trigger");

            var context = new BuildContext(avatar.Root, null);
            var (followPlan, _) = FollowBuilder.Build(context, tree);
            Assert.That(followPlan.TryGetSource(link, avatar.Glasses, out _, out _), Is.True, "the builder still gives it a source; only the layer can tell it never resolves");

            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var messages = FollowLayerBuilder.Build(asc, tree, plan, followPlan, new List<TriggerPlan>());
            context.DeactivateAllExtensionContexts();

            var warning = messages.Single(m => m.Key == "ad.warn.follow_claim_unresolved");
            Assert.That(warning.Args[0], Is.EqualTo("Link"));
            Assert.That(warning.Args[1], Is.EqualTo("Glasses"));

            var layer = Layer(FxOf(avatar), "AD: Follow Glasses");
            Assert.That(layer.stateMachine.states.Any(s => s.state.name == "Link"), Is.False, "an unresolved claim leaves no unreachable state behind");
        }

        [Test]
        public void TargetOutsideTheAvatarIsAnError()
        {
            var outside = new GameObject("Outside");
            try
            {
                var menu = avatar.AddMenu();
                var hold = menu.AddEntry(EntryKind.Toggle, "Hold");
                hold.follows.Add(new FollowTarget { target = avatar.Glasses, follows = outside.transform });

                var root = menu.AddEntry(EntryKind.Toggle, "Root");
                root.follows.Add(new FollowTarget { target = avatar.Root, follows = avatar.Hat.transform });

                var child = new GameObject("Child");
                child.transform.SetParent(avatar.Glasses.transform);
                var descendant = menu.AddEntry(EntryKind.Toggle, "Descendant");
                descendant.follows.Add(new FollowTarget { target = avatar.Glasses, follows = child.transform });

                var (tree, _) = Model(menu);
                Assert.That(tree.Messages.Any(m => m.Key == "ad.error.follow_outside_avatar" && m.EntryIds.Contains(hold.id)), Is.True);
                Assert.That(tree.Messages.Any(m => m.Key == "ad.error.follow_no_rest_parent" && m.EntryIds.Contains(root.id)), Is.True, "the avatar root has no rest parent of its own");
                Assert.That(tree.Messages.Any(m => m.Key == "ad.error.follow_self" && m.EntryIds.Contains(descendant.id)), Is.True, "an object cannot follow its own descendant");

                var context = new BuildContext(avatar.Root, null);
                var (followPlan, _) = FollowBuilder.Build(context, tree);
                Assert.That(followPlan.TryGetSource(hold, avatar.Glasses, out _, out _), Is.False);
                Assert.That(followPlan.TryGetSource(root, avatar.Root, out _, out _), Is.False);
                Assert.That(followPlan.TryGetSource(descendant, avatar.Glasses, out _, out _), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(outside);
            }
        }

        [Test]
        public void FollowAddsNoParameterAndNoBit()
        {
            var menu = avatar.AddMenu();
            var hold = menu.AddEntry(EntryKind.Toggle, "Hold");
            var (_, planBefore) = Model(menu);

            hold.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform });
            var (_, planAfter) = Model(menu);

            Assert.That(planAfter.Parameters.Count, Is.EqualTo(planBefore.Parameters.Count));
            Assert.That(planAfter.NewSyncedBits, Is.EqualTo(planBefore.NewSyncedBits));
        }

        [Test]
        public void ExcludedPlatformBuildsNoConstraint()
        {
            try
            {
                Platform.OverrideMobile = true;
                var menu = avatar.AddMenu();
                var hold = menu.AddEntry(EntryKind.Toggle, "Hold");
                hold.platform = EntryPlatform.PCOnly;
                hold.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform });

                var context = new BuildContext(avatar.Root, null);
                context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
                var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
                context.DeactivateAllExtensionContexts();

                Assert.That(errors, Is.Empty);
                Assert.That(avatar.Glasses.GetComponent<VRCParentConstraint>(), Is.Null);
            }
            finally
            {
                Platform.OverrideMobile = false;
            }
        }

        [Test]
        public void TheSceneObjectIsNeverTouched()
        {
            var menu = avatar.AddMenu();
            var hold = menu.AddEntry(EntryKind.Toggle, "Hold");
            hold.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform });

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                Assert.That(avatar.Glasses.GetComponent<VRCParentConstraint>(), Is.Null, "the scene object was never touched");
                var processedGlasses = processed.transform.Find("Glasses");
                Assert.That(processedGlasses, Is.Not.Null);
                Assert.That(processedGlasses.GetComponent<VRCParentConstraint>(), Is.Not.Null, "the build clone got the constraint");
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                GeneratedAssets.CleanUp();
            }
        }

        [Test]
        public void PreviewShowsTheObjectAtTheTarget()
        {
            avatar.Glasses.AddComponent<MeshFilter>();
            var renderer = avatar.Glasses.AddComponent<MeshRenderer>();
            avatar.Hat.transform.position = new Vector3(1f, 2f, 3f);
            avatar.Hat.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            var menu = avatar.AddMenu();
            var hold = menu.AddEntry(EntryKind.Toggle, "Hold");
            hold.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform, keepOffset = false });

            var overrides = new Dictionary<string, float> { { hold.id, 1f } };
            var states = PreviewModel.Compute(avatar.Root.transform, new[] { menu }, overrides, SceneReader.Direct);

            Assert.That(states.TryGetValue(renderer, out var state), Is.True);
            Assert.That(state.HasFollow, Is.True);
            Assert.That(state.FollowPosition, Is.EqualTo(avatar.Hat.transform.position));
            Assert.That(state.FollowRotation, Is.EqualTo(avatar.Hat.transform.rotation));
        }

        /// <summary>The child renderer fix has to have a test with a renderer that is NOT the followed object itself, or a return to the old "only the object's own renderer moves" code regresses silently.</summary>
        [Test]
        public void PreviewMovesChildRenderersToo()
        {
            avatar.Glasses.AddComponent<MeshFilter>();
            avatar.Glasses.AddComponent<MeshRenderer>();
            var blade = new GameObject("Blade");
            blade.transform.SetParent(avatar.Glasses.transform);
            blade.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            blade.transform.localRotation = Quaternion.Euler(0f, 0f, 20f);
            blade.AddComponent<MeshFilter>();
            var bladeRenderer = blade.AddComponent<MeshRenderer>();

            avatar.Hat.transform.position = new Vector3(1f, 2f, 3f);
            avatar.Hat.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            var menu = avatar.AddMenu();
            var hold = menu.AddEntry(EntryKind.Toggle, "Hold");
            hold.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform, keepOffset = false });

            var overrides = new Dictionary<string, float> { { hold.id, 1f } };
            var states = PreviewModel.Compute(avatar.Root.transform, new[] { menu }, overrides, SceneReader.Direct);

            Assert.That(states.TryGetValue(bladeRenderer, out var state), Is.True, "a renderer on a child of the followed object is in the preview too");
            Assert.That(state.HasFollow, Is.True);

            var originalTargetPosition = avatar.Glasses.transform.position;
            var originalTargetRotation = avatar.Glasses.transform.rotation;
            var newTargetPosition = avatar.Hat.transform.position;
            var newTargetRotation = avatar.Hat.transform.rotation;
            var delta = newTargetRotation * Quaternion.Inverse(originalTargetRotation);
            var expectedPosition = newTargetPosition + delta * (bladeRenderer.transform.position - originalTargetPosition);
            var expectedRotation = delta * bladeRenderer.transform.rotation;

            Assert.That(state.FollowPosition, Is.Not.EqualTo(newTargetPosition), "not simply snapped onto the target, its own offset moves with it");
            AssertVector3(state.FollowPosition, expectedPosition);
            Assert.That(Quaternion.Angle(state.FollowRotation, expectedRotation), Is.LessThan(0.01f));
        }

        [Test]
        public void NewFollowRowSnapsToTheTarget()
        {
            var menu = avatar.AddMenu();
            var hold = menu.AddEntry(EntryKind.Toggle, "Hold");
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            view.Tree.SelectEntry(hold.id);

            var follows = view.Detail.Q<TargetListView>("ad-follows");
            Assert.That(follows, Is.Not.Null);
            follows.AddFollow();

            Assert.That(hold.follows.Count, Is.EqualTo(1));
            Assert.That(hold.follows[0].keepOffset, Is.False, "a new row snaps into the target out of the box");
        }

        [Test]
        public void SummaryNamesTheFollow()
        {
            var menu = avatar.AddMenu();
            var hold = menu.AddEntry(EntryKind.Toggle, "Hold");
            hold.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform });
            var plan = AvatarPlanCache.GetFor(menu);

            var text = SummaryBuilder.Build(plan, menu, SummaryFormat.Plain);

            Assert.That(text, Does.Contain("Hold"));
            Assert.That(text, Does.Contain("Glasses"));
            Assert.That(text, Does.Contain("Hat"));
        }
    }
}
#endif
