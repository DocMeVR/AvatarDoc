#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace DocMe.AvatarDoctor.Editor
{
    public class GrabTests
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

        /// <summary>
        /// Model, FollowBuilder, GrabBuilder, defaults, the animator builder, the trigger builder and
        /// FollowLayerBuilder, in the same order AvatarDoctorBuilder.Run uses them, without a real avatar clone:
        /// enough for anything that only needs the built components and the clips, not clone/scene separation.
        /// </summary>
        (AnimatorController fx, MenuTree tree, ParameterPlan plan, FollowPlan followPlan, List<ErrorContext> errors) BuildFull(params AvatarDoc[] menus)
        {
            var (tree, plan) = Model(menus);
            var context = new BuildContext(avatar.Root, null);
            var (followPlan, followMessages) = FollowBuilder.Build(context, tree);
            var errors = ErrorReport.CaptureErrors(() =>
            {
                L.Report(followMessages);
                GrabBuilder.Build(context, tree, followPlan);
                L.Report(tree.Messages);
            });
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var virtualFx);
            var existing = ParametersWriter.ExistingParameters(avatar.Descriptor.expressionParameters, virtualFx);
            var touch = TouchBuilder.Describe(tree, existing, plan);
            var (triggerPlans, triggerMessages) = TriggerPlanner.Plan(tree, plan, existing, touch);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState, triggerPlans, followPlan);
            var buildErrors = ErrorReport.CaptureErrors(() =>
            {
                L.Report(triggerMessages);
                L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState, null, followPlan));
                L.Report(TriggerBuilder.Build(asc, tree, triggerPlans, baseState));
                L.Report(FollowLayerBuilder.Build(asc, tree, plan, followPlan, triggerPlans));
            });
            context.DeactivateAllExtensionContexts();
            errors.AddRange(buildErrors);
            return (FxOf(avatar), tree, plan, followPlan, errors);
        }

        [Test]
        public void GrabbableBuildsPhysBoneContactsAndConstraint()
        {
            avatar.AddHumanoidRig();
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;

            var (_, tree, _, followPlan, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);
            Assert.That(tree.HasErrors, Is.False);

            var pb = avatar.Glasses.GetComponent<VRCPhysBone>();
            Assert.That(pb, Is.Not.Null);
            Assert.That(pb.allowGrabbing, Is.EqualTo(VRCPhysBoneBase.AdvancedBool.True), "grabAnyone defaults on");
            Assert.That(pb.immobile, Is.EqualTo(1f), "no movement of its own");

            var receivers = avatar.Glasses.GetComponentsInChildren<VRCContactReceiver>();
            Assert.That(receivers.Length, Is.EqualTo(2));
            Assert.That(receivers.Any(r => r.collisionTags.Contains("HandL")), Is.True);
            Assert.That(receivers.Any(r => r.collisionTags.Contains("HandR")), Is.True);

            Assert.That(followPlan.TryGetGrabSource(grab, avatar.Glasses, GrabHands.Left, out var constraintL, out var indexL), Is.True);
            Assert.That(followPlan.TryGetGrabSource(grab, avatar.Glasses, GrabHands.Right, out var constraintR, out var indexR), Is.True);
            Assert.That(constraintR, Is.SameAs(constraintL));
            Assert.That(indexL, Is.Not.EqualTo(indexR));
            Assert.That(constraintL.Sources[0].SourceTransform, Is.SameAs(avatar.Glasses.transform.parent), "the rest source is the object's own parent");
            Assert.That(constraintL.Sources[indexL].SourceTransform, Is.SameAs(avatar.LeftHandBone));
            Assert.That(constraintR.Sources[indexR].SourceTransform, Is.SameAs(avatar.RightHandBone));
        }

        [Test]
        public void LeftOnlyBuildsOneHand()
        {
            avatar.AddHumanoidRig();
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;
            grab.grabHands = GrabHands.Left;

            var (_, _, _, followPlan, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);

            var receivers = avatar.Glasses.GetComponentsInChildren<VRCContactReceiver>();
            Assert.That(receivers.Length, Is.EqualTo(1));
            Assert.That(receivers[0].collisionTags, Does.Contain("HandL"));

            Assert.That(followPlan.TryGetGrabSource(grab, avatar.Glasses, GrabHands.Left, out _, out _), Is.True);
            Assert.That(followPlan.TryGetGrabSource(grab, avatar.Glasses, GrabHands.Right, out _, out _), Is.False);
        }

        [Test]
        public void GrabbedByTheLeftHandFollowsTheLeftHand()
        {
            avatar.AddHumanoidRig();
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;

            var (fx, _, _, followPlan, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);

            var pb = avatar.Glasses.GetComponent<VRCPhysBone>();
            var leftReceiver = avatar.Glasses.GetComponentsInChildren<VRCContactReceiver>().Single(r => r.collisionTags.Contains("HandL"));

            var layer = Layer(fx, "AD: Follow Glasses");
            var leftState = State(layer, "Sword (Left)");
            var rest = State(layer, "Rest");

            var enter = rest.transitions.Single(t => t.destinationState == leftState);
            Assert.That(enter.conditions.Any(c => c.parameter == pb.parameter + "_IsGrabbed"), Is.True, "entering needs the PhysBone held");
            Assert.That(enter.conditions.Any(c => c.parameter == leftReceiver.parameter), Is.True, "entering needs the left hand's contact");

            Assert.That(followPlan.TryGetGrabSource(grab, avatar.Glasses, GrabHands.Left, out _, out var indexL), Is.True);
            Assert.That(FloatValue(leftState.motion, "Glasses", typeof(VRCParentConstraint), "Sources.source" + indexL + ".Weight"), Is.EqualTo(1f));
            Assert.That(FloatValue(leftState.motion, "Glasses", typeof(VRCParentConstraint), "Sources.source0.Weight"), Is.EqualTo(0f));
        }

        [Test]
        public void BothHandsHoldTheLaterOne()
        {
            avatar.AddHumanoidRig();
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;

            var (fx, _, _, _, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);

            // The right hand's claim is added after the left one, so it is the "later" claim: checked first from
            // Rest, and the one that wins outright when both hands' conditions become met at the very same time,
            // starting from Rest.
            var layer = Layer(fx, "AD: Follow Glasses");
            var rest = State(layer, "Rest");
            int indexOfRight = System.Array.FindIndex(rest.transitions, t => t.destinationState.name == "Sword (Right)");
            int indexOfLeft = System.Array.FindIndex(rest.transitions, t => t.destinationState.name == "Sword (Left)");
            Assert.That(indexOfRight, Is.GreaterThanOrEqualTo(0));
            Assert.That(indexOfLeft, Is.GreaterThanOrEqualTo(0));
            Assert.That(indexOfRight, Is.LessThan(indexOfLeft));

            // Once one hand actually holds the object, bringing the other hand's contact near does not steal it:
            // neither hand state of the SAME grabbable transitions to the other.
            var leftState = State(layer, "Sword (Left)");
            var rightState = State(layer, "Sword (Right)");
            Assert.That(leftState.transitions.Any(t => t.destinationState == rightState), Is.False);
            Assert.That(rightState.transitions.Any(t => t.destinationState == leftState), Is.False);
        }

        [Test]
        public void TheSecondHandDoesNotStealAHeldObject()
        {
            avatar.AddHumanoidRig();
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;

            var (fx, _, _, _, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);

            var layer = Layer(fx, "AD: Follow Glasses");
            var leftState = State(layer, "Sword (Left)");
            var rightState = State(layer, "Sword (Right)");

            // The hand that grabbed it keeps it until release: only a transition back to Rest exists from either
            // hand's own state, never one straight to the other hand.
            Assert.That(leftState.transitions.All(t => t.destinationState.name == "Rest"), Is.True);
            Assert.That(rightState.transitions.All(t => t.destinationState.name == "Rest"), Is.True);
        }

        [Test]
        public void KeepOffsetOffSnapsIntoTheHand()
        {
            avatar.AddHumanoidRig();
            avatar.Glasses.transform.position = new Vector3(1f, 2f, 3f);
            avatar.Glasses.transform.rotation = Quaternion.Euler(10f, 20f, 30f);
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;
            grab.grabHands = GrabHands.Left;
            grab.grabKeepOffset = false;

            var (_, _, _, followPlan, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);
            followPlan.TryGetGrabSource(grab, avatar.Glasses, GrabHands.Left, out var constraint, out var index);
            var source = constraint.Sources[index];
            Assert.That(source.ParentPositionOffset, Is.EqualTo(Vector3.zero));
            Assert.That(source.ParentRotationOffset, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void ReturnSecondsIsTheTransitionBack()
        {
            avatar.AddHumanoidRig();
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;
            grab.grabHands = GrabHands.Left;
            grab.grabReturnSeconds = 1.5f;

            var (fx, _, _, _, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);

            var layer = Layer(fx, "AD: Follow Glasses");
            var leftState = State(layer, "Sword (Left)");
            var rest = State(layer, "Rest");
            var enter = rest.transitions.Single(t => t.destinationState == leftState);
            Assert.That(enter.duration, Is.EqualTo(1.5f));
            // Leaving needs one transition per negated condition of the AND (PhysBone held, hand near), so there
            // are two of them, not one; both use the same return time.
            Assert.That(leftState.transitions.Length, Is.EqualTo(2));
            Assert.That(leftState.transitions.All(t => t.destinationState == rest && t.duration == 1.5f), Is.True);
        }

        [Test]
        public void NoHumanoidRigIsAnError()
        {
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;

            var (tree, _) = Model(menu);
            Assert.That(tree.Messages.Any(m => m.Key == "ad.error.grab_no_hand" && m.EntryIds.Contains(grab.id)), Is.True);
            Assert.That(tree.HasErrors, Is.True);
        }

        [Test]
        public void MissingHandBoneIsAnError()
        {
            // The rig is built without a right hand on purpose, and Unity's avatar builder logs an error about it.
            // Batch runs fail a test on any unexpected error log, so this one is expected here. The editor's own
            // runner has no test scope for LogAssert and does not fail on logs, so there the setting is skipped.
            try { LogAssert.ignoreFailingMessages = true; } catch (System.InvalidOperationException) { }
            avatar.AddHumanoidRig(includeRightHand: false);
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;

            var (tree, _) = Model(menu);
            Assert.That(tree.Messages.Any(m => m.Key == "ad.error.grab_no_hand" && m.EntryIds.Contains(grab.id)), Is.True);
            Assert.That(tree.HasErrors, Is.True);
        }

        [Test]
        public void ObjectOutsideTheAvatarIsAnError()
        {
            var outside = new GameObject("Outside");
            try
            {
                avatar.AddHumanoidRig();
                var menu = avatar.AddMenu();
                var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
                grab.grabObject = outside;

                var (tree, _) = Model(menu);
                Assert.That(tree.Messages.Any(m => m.Key == "ad.error.grab_outside_avatar" && m.EntryIds.Contains(grab.id)), Is.True);
                Assert.That(tree.HasErrors, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(outside);
            }
        }

        [Test]
        public void ExistingPhysBoneIsReused()
        {
            avatar.AddHumanoidRig();
            var existingPb = avatar.Glasses.AddComponent<VRCPhysBone>();
            existingPb.parameter = "MyBone";
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;

            var (_, tree, _, _, errors) = BuildFull(menu);
            // Reusing the PhysBone reports an info message (asserted below), which CaptureErrors collects too;
            // only a real error would mean something went wrong.
            Assert.That(errors.Any(e => e.TheError.Severity == ErrorSeverity.Error), Is.False);
            Assert.That(avatar.Glasses.GetComponents<VRCPhysBone>().Length, Is.EqualTo(1), "no second PhysBone was added");
            Assert.That(existingPb.parameter, Is.EqualTo("MyBone"), "the existing parameter name was kept");
            Assert.That(tree.Messages.Any(m => m.Key == "ad.info.grab_uses_existing_physbone" && m.EntryIds.Contains(grab.id)), Is.True);
        }

        [Test]
        public void GrabbableWithoutTheMenuToggleTakesNoMenuSlot()
        {
            avatar.AddHumanoidRig();
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;
            grab.grabMenuToggle = false;

            var (tree, plan) = Model(menu);
            Assert.That(grab.NoMenuControl, Is.True);
            var control = tree.Controls.Single(c => c.Entry == grab);
            Assert.That(plan.Bindings.ContainsKey(control), Is.False, "no parameter is planned without a menu toggle");
            Assert.That(tree.Root.Items.Any(i => i.Control == control), Is.True, "the row still exists in the tree");
        }

        [Test]
        public void MenuToggleAllowsGrabbing()
        {
            avatar.AddHumanoidRig();
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;
            grab.grabMenuToggle = true;

            var (fx, _, _, _, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);

            var pb = avatar.Glasses.GetComponent<VRCPhysBone>();
            var layer = Layer(fx, "AD: Sword");
            var off = State(layer, "Off");
            var on = State(layer, "On");
            Assert.That(FloatValue(off.motion, "Glasses", pb.GetType(), "m_Enabled"), Is.EqualTo(0f), "off in game, the PhysBone does not allow grabbing");
            Assert.That(FloatValue(on.motion, "Glasses", pb.GetType(), "m_Enabled"), Is.EqualTo(1f));
        }

        [Test]
        public void GrabbableCostsNoSyncedBit()
        {
            avatar.AddHumanoidRig();
            var menu = avatar.AddMenu();
            var (_, planBefore) = Model(menu);

            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;
            var (_, planAfter) = Model(menu);

            Assert.That(planAfter.Parameters.Count, Is.EqualTo(planBefore.Parameters.Count));
            Assert.That(planAfter.NewSyncedBits, Is.EqualTo(planBefore.NewSyncedBits));
        }

        [Test]
        public void TheSceneObjectIsNeverTouched()
        {
            avatar.AddHumanoidRig();
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                Assert.That(avatar.Glasses.GetComponent<VRCPhysBone>(), Is.Null, "the scene object was never touched");
                Assert.That(avatar.Glasses.GetComponentsInChildren<VRCContactReceiver>().Length, Is.EqualTo(0));
                var processedGlasses = processed.transform.Find("Glasses");
                Assert.That(processedGlasses, Is.Not.Null);
                Assert.That(processedGlasses.GetComponent<VRCPhysBone>(), Is.Not.Null, "the build clone got the PhysBone");
                Assert.That(processedGlasses.GetComponentsInChildren<VRCContactReceiver>().Length, Is.EqualTo(2), "the build clone got the contacts");
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                GeneratedAssets.CleanUp();
            }
        }

        [Test]
        public void PreviewShowsTheObjectInTheHand()
        {
            avatar.AddHumanoidRig();
            avatar.Glasses.AddComponent<MeshFilter>();
            var renderer = avatar.Glasses.AddComponent<MeshRenderer>();
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;
            grab.grabHands = GrabHands.Left;

            var overrides = new Dictionary<string, float> { { grab.id, 1f } };
            var states = PreviewModel.Compute(avatar.Root.transform, new[] { menu }, overrides, SceneReader.Direct);

            Assert.That(states.TryGetValue(renderer, out var state), Is.True);
            Assert.That(state.HasFollow, Is.True);
            Assert.That(state.FollowPosition, Is.EqualTo(avatar.LeftHandBone.position));
        }

        [Test]
        public void AGrabbableWithoutChildrenStillHasABoneToGrab()
        {
            avatar.AddHumanoidRig();
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Ring");
            grab.grabObject = avatar.Glasses;

            var (_, _, _, _, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);
            var pb = avatar.Glasses.GetComponent<VRCPhysBone>();
            Assert.That(pb.endpointPosition, Is.Not.EqualTo(Vector3.zero), "a childless object needs an endpoint to have a bone at all");
            Assert.That(pb.endpointPosition.magnitude, Is.GreaterThanOrEqualTo(0.05f));
        }

        [Test]
        public void GrabContactsAreNotBonesOfTheGrabChain()
        {
            avatar.AddHumanoidRig();
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;

            var (_, _, _, _, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);

            var pb = avatar.Glasses.GetComponent<VRCPhysBone>();
            var receivers = avatar.Glasses.GetComponentsInChildren<VRCContactReceiver>();
            Assert.That(receivers.Length, Is.EqualTo(2));
            foreach (var receiver in receivers)
                Assert.That(pb.ignoreTransforms, Does.Contain(receiver.transform),
                    "a receiver must not become a bone of the chain it reports on: it would move with the chain's own physics and could drop the very contact it exists to hold");
        }

        [Test]
        public void GrabEndpointFollowsTheObjectsScale()
        {
            avatar.AddHumanoidRig();
            // Y is not the largest component: a fix that divides by the largest lossy scale component (right for
            // the receiver's uniform sphere) instead of the Y component alone (right for this local Y offset)
            // would divide by 4 here and this would fail.
            avatar.Glasses.transform.localScale = new Vector3(4f, 2f, 4f);
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Ring");
            grab.grabObject = avatar.Glasses;

            var (_, _, _, _, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);
            var pb = avatar.Glasses.GetComponent<VRCPhysBone>();
            // With no renderer the world length floors at 0.05; VRChat multiplies the local endpoint by the
            // transform's own lossy Y scale alone, so the assigned value has to be divided by that one axis.
            Assert.That(pb.endpointPosition.y, Is.EqualTo(0.05f / 2f).Within(0.0001f));
        }

        [Test]
        public void TheGrabbedBoneDoesNotMoveTheProp()
        {
            avatar.AddHumanoidRig();
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;

            var (_, _, _, _, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);
            var pb = avatar.Glasses.GetComponent<VRCPhysBone>();
            Assert.That(pb.grabMovement, Is.EqualTo(0f));
        }

        [Test]
        public void TurningTheMenuToggleOffReleasesAHeldObject()
        {
            avatar.AddHumanoidRig();
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;
            grab.grabHands = GrabHands.Left;
            grab.grabMenuToggle = true;

            var (fx, tree, plan, _, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);

            var control = tree.Controls.Single(c => c.Entry == grab);
            var binding = plan.Bindings[control];
            var leftState = State(Layer(fx, "AD: Follow Glasses"), "Sword (Left)");

            // Among the leave transitions, one is the toggle parameter alone: turning it off drops the claim
            // even while a disabled PhysBone stops updating its own "held" parameter instead of forcing it false.
            Assert.That(leftState.transitions.Any(t => t.destinationState.name == "Rest" && t.conditions.Length == 1 && t.conditions[0].parameter == binding.Parameter.Name), Is.True);
        }

        [Test]
        public void ReceiverRadiusFollowsTheObjectsScale()
        {
            avatar.AddHumanoidRig();
            avatar.Glasses.transform.localScale = new Vector3(2f, 2f, 2f);
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;
            grab.grabHands = GrabHands.Left;

            var (_, _, _, _, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);
            var receiver = avatar.Glasses.GetComponentsInChildren<VRCContactReceiver>().Single();
            // With no renderer the world radius floors at 0.03; VRChat multiplies whatever local radius is
            // assigned by the transform's own lossy scale, so the assigned value has to be divided by it first.
            Assert.That(receiver.radius, Is.EqualTo(0.03f / 2f).Within(0.0001f));
        }

        [Test]
        public void ReachMultipliesTheRadius()
        {
            avatar.AddHumanoidRig();
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;
            grab.grabHands = GrabHands.Left;
            grab.grabReach = 2f;

            var (_, _, _, _, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);
            var receiver = avatar.Glasses.GetComponentsInChildren<VRCContactReceiver>().Single();
            Assert.That(receiver.radius, Is.EqualTo(0.03f * 2f).Within(0.0001f));
        }

        [Test]
        public void TwoGrabbablesWithTheSameNameDoNotShareParameters()
        {
            avatar.AddHumanoidRig();
            var second = new GameObject("Second");
            second.transform.SetParent(avatar.Root.transform);
            try
            {
                var menu = avatar.AddMenu();
                var grabA = menu.AddEntry(EntryKind.Grabbable, "New Grabbable");
                grabA.grabObject = avatar.Glasses;
                grabA.grabHands = GrabHands.Left;
                var grabB = menu.AddEntry(EntryKind.Grabbable, "New Grabbable");
                grabB.grabObject = second;
                grabB.grabHands = GrabHands.Left;

                var (_, _, _, _, errors) = BuildFull(menu);
                Assert.That(errors, Is.Empty);

                var pbA = avatar.Glasses.GetComponent<VRCPhysBone>();
                var pbB = second.GetComponent<VRCPhysBone>();
                Assert.That(pbA.parameter, Is.Not.EqualTo(pbB.parameter));

                var receiverA = avatar.Glasses.GetComponentsInChildren<VRCContactReceiver>().Single();
                var receiverB = second.GetComponentsInChildren<VRCContactReceiver>().Single();
                Assert.That(receiverA.parameter, Is.Not.EqualTo(receiverB.parameter));
            }
            finally
            {
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void AnotherPlayersHandDoesNotCount()
        {
            avatar.AddHumanoidRig();
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;

            var (_, _, _, _, errors) = BuildFull(menu);
            Assert.That(errors, Is.Empty);
            var receivers = avatar.Glasses.GetComponentsInChildren<VRCContactReceiver>();
            Assert.That(receivers.Length, Is.EqualTo(2));
            Assert.That(receivers.All(r => r.allowSelf && !r.allowOthers), Is.True);
        }

        [Test]
        public void AnObjectInsideAnotherPhysBoneChainIsAnError()
        {
            avatar.AddHumanoidRig();
            avatar.Hat.AddComponent<VRCPhysBone>();
            var sword = new GameObject("Sword");
            sword.transform.SetParent(avatar.Hat.transform);
            try
            {
                var menu = avatar.AddMenu();
                var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
                grab.grabObject = sword;

                var (tree, _) = Model(menu);
                Assert.That(tree.Messages.Any(m => m.Key == "ad.error.grab_inside_physbone" && m.EntryIds.Contains(grab.id)), Is.True);
                Assert.That(tree.HasErrors, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(sword);
            }
        }

        [Test]
        public void SummaryNamesTheGrabbable()
        {
            avatar.AddHumanoidRig();
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;
            var plan = AvatarPlanCache.GetFor(menu);

            var text = SummaryBuilder.Build(plan, menu, SummaryFormat.Plain);

            Assert.That(text, Does.Contain("Sword"));
            Assert.That(text, Does.Contain("Glasses"));
        }

        [Test]
        public void APhysBoneTriggerOnTheGrabbablesOwnPhysBoneReadsItsParameter()
        {
            avatar.AddHumanoidRig();
            var pb = avatar.Glasses.AddComponent<VRCPhysBone>();
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;
            var glow = menu.AddEntry(EntryKind.Trigger, "Glow");
            glow.source = TriggerSource.PhysBone;
            glow.physBone = pb;
            glow.physBoneEvent = PhysBoneEvent.Grabbed;
            glow.objects.Add(new ObjectTarget { target = avatar.Hat });

            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error), Is.Empty);

            Assert.That(pb.parameter, Is.Not.Empty, "the sword's PhysBone was given a parameter");
            var off = State(Layer(FxOf(avatar), "AD: Glow"), "Off");
            var read = off.transitions.SelectMany(t => t.conditions).Select(c => c.parameter).Distinct().ToList();
            Assert.That(read, Is.EqualTo(new[] { pb.parameter + "_IsGrabbed" }),
                "the glow fires on the very parameter VRChat sets for the PhysBone the grab and the trigger share");
        }

        /// <summary>A mesh whose bounds are a cube of this size around this centre, in the mesh's own space.</summary>
        static Mesh Box(Vector3 center, float size)
        {
            float h = size / 2f;
            var mesh = new Mesh { name = "Box" };
            mesh.vertices = new[] { center + new Vector3(-h, -h, -h), center + new Vector3(h, h, h), center + new Vector3(h, -h, h) };
            mesh.triangles = new[] { 0, 1, 2 };
            return mesh;
        }

        /// <summary>
        /// A left hand grab of the avatar's Glasses, holding mesh, with an optional child holding childMesh, built
        /// through FollowBuilder and GrabBuilder only, which is everything that places the receiver. Returns it.
        /// Each object is switched off before its renderer is added: a renderer on an object that is already
        /// inactive has never been awake, the way every hidden renderer of NDMF's freshly made build clone has not.
        /// </summary>
        static VRCContactReceiver GrabReceiver(TestAvatar target, Mesh mesh, bool objectActive, Mesh childMesh = null, bool childActive = true, bool skinned = false)
        {
            target.AddHumanoidRig();
            target.Glasses.transform.localPosition = new Vector3(0f, 1.2f, 0.3f);
            target.Glasses.SetActive(objectActive);
            if (skinned)
            {
                var skin = target.Glasses.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = mesh;
                skin.localBounds = mesh.bounds;
            }
            else
            {
                target.Glasses.AddComponent<MeshFilter>().sharedMesh = mesh;
                target.Glasses.AddComponent<MeshRenderer>();
            }
            if (childMesh != null)
            {
                var gem = new GameObject("Gem");
                gem.SetActive(childActive);
                gem.transform.SetParent(target.Glasses.transform, false);
                gem.transform.localPosition = new Vector3(0.2f, 0f, 0f);
                gem.AddComponent<MeshFilter>().sharedMesh = childMesh;
                gem.AddComponent<MeshRenderer>();
            }
            var doc = target.AddMenu();
            var grab = doc.AddEntry(EntryKind.Grabbable, "Prop");
            grab.grabObject = target.Glasses;
            grab.grabHands = GrabHands.Left;

            var tree = MenuModel.Build(target.Root.transform, target.ExpressionsMenu, new[] { doc });
            Assert.That(tree.HasErrors, Is.False);
            var context = new BuildContext(target.Root, null);
            var (followPlan, _) = FollowBuilder.Build(context, tree);
            GrabBuilder.Build(context, tree, followPlan);
            return target.Glasses.GetComponentsInChildren<VRCContactReceiver>(true).Single();
        }

        [Test]
        public void AnInactiveGrabObjectPlacesItsReceiverFromItsMesh()
        {
            // The same prop on two avatars: shown on one, hidden on the other the way a prop another toggle shows
            // is. The hidden one's receiver has to sit and reach exactly where the shown one's does.
            var shownMesh = Box(new Vector3(0f, 0.2f, 0f), 0.1f);
            var hiddenMesh = Box(new Vector3(0f, 0.2f, 0f), 0.1f);
            var shown = TestAvatar.Create();
            try
            {
                var shownReceiver = GrabReceiver(shown, shownMesh, objectActive: true);
                var hiddenReceiver = GrabReceiver(avatar, hiddenMesh, objectActive: false);

                Assert.That((hiddenReceiver.transform.localPosition - new Vector3(0f, 0.2f, 0f)).magnitude, Is.LessThan(0.0001f),
                    "the receiver sits at the mesh's own centre, not between the prop and the avatar's feet");
                Assert.That((hiddenReceiver.transform.localPosition - shownReceiver.transform.localPosition).magnitude, Is.LessThan(0.0001f));
                Assert.That(hiddenReceiver.radius, Is.EqualTo(shownReceiver.radius).Within(0.0001f), "it reaches as far as it would if the prop were shown");
            }
            finally
            {
                shown.Dispose();
                Object.DestroyImmediate(shownMesh);
                Object.DestroyImmediate(hiddenMesh);
            }
        }

        [Test]
        public void AnInactiveSkinnedGrabObjectPlacesItsReceiverFromItsBounds()
        {
            var shownMesh = Box(new Vector3(0f, 0.2f, 0f), 0.1f);
            var hiddenMesh = Box(new Vector3(0f, 0.2f, 0f), 0.1f);
            var shown = TestAvatar.Create();
            try
            {
                var shownReceiver = GrabReceiver(shown, shownMesh, objectActive: true, skinned: true);
                var hiddenReceiver = GrabReceiver(avatar, hiddenMesh, objectActive: false, skinned: true);

                Assert.That((hiddenReceiver.transform.localPosition - new Vector3(0f, 0.2f, 0f)).magnitude, Is.LessThan(0.0001f),
                    "a skinned prop's bounds are read in its own space when it has no root bone");
                Assert.That((hiddenReceiver.transform.localPosition - shownReceiver.transform.localPosition).magnitude, Is.LessThan(0.0001f));
                Assert.That(hiddenReceiver.radius, Is.EqualTo(shownReceiver.radius).Within(0.0001f));
            }
            finally
            {
                shown.Dispose();
                Object.DestroyImmediate(shownMesh);
                Object.DestroyImmediate(hiddenMesh);
            }
        }

        [Test]
        public void AHiddenChildMeshDoesNotStretchTheReceiver()
        {
            // A shown prop with a hidden child mesh: its receiver has to match the same prop with the child shown.
            var meshes = new[] { Box(new Vector3(0f, 0.2f, 0f), 0.1f), Box(Vector3.zero, 0.1f), Box(new Vector3(0f, 0.2f, 0f), 0.1f), Box(Vector3.zero, 0.1f) };
            var shown = TestAvatar.Create();
            try
            {
                var shownReceiver = GrabReceiver(shown, meshes[0], objectActive: true, meshes[1], childActive: true);
                var hiddenReceiver = GrabReceiver(avatar, meshes[2], objectActive: true, meshes[3], childActive: false);

                Assert.That((hiddenReceiver.transform.localPosition - shownReceiver.transform.localPosition).magnitude, Is.LessThan(0.0001f),
                    "a hidden child counts where its mesh really is, not at the avatar's origin");
                Assert.That(hiddenReceiver.radius, Is.EqualTo(shownReceiver.radius).Within(0.0001f));
                Assert.That(hiddenReceiver.radius, Is.LessThan(0.3f), "the reach stays the size of the prop");
            }
            finally
            {
                shown.Dispose();
                foreach (var mesh in meshes) Object.DestroyImmediate(mesh);
            }
        }
    }
}
#endif
