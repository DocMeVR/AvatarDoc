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
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDKBase;

namespace DocMe.AvatarDoctor.Editor
{
    public class TouchTriggerTests
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

        static AnimatorController FxOf(TestAvatar a) => (AnimatorController)a.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
        static AnimatorControllerLayer Layer(AnimatorController fx, string name) => fx.layers.Single(l => l.name == name);
        static AnimatorState State(AnimatorControllerLayer layer, string name) => layer.stateMachine.states.Single(s => s.state.name == name).state;

        static float FloatValue(Motion motion, string path, System.Type type, string property)
        {
            var curve = AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve(path, type, property));
            Assert.That(curve, Is.Not.Null, path + " " + property);
            return curve.Evaluate(0f);
        }

        static AnimationCurve CurveOf(Motion motion, string path, string property)
            => AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), property));

        MenuEntry ContactTrigger(string name, Transform anchor)
        {
            var entry = menu.AddEntry(EntryKind.Trigger, name);
            entry.source = TriggerSource.Contact;
            entry.contactAnchor = anchor;
            entry.objects.Add(new ObjectTarget { target = avatar.Glasses });
            return entry;
        }

        MenuEntry PhysBoneTrigger(string name, VRCPhysBone physBone)
        {
            var entry = menu.AddEntry(EntryKind.Trigger, name);
            entry.source = TriggerSource.PhysBone;
            entry.physBone = physBone;
            entry.objects.Add(new ObjectTarget { target = avatar.Glasses });
            return entry;
        }

        /// <summary>Model, plan, TouchBuilder.Describe and TouchBuilder.Create: what a successful build's planner and clone would see, without building any layer.</summary>
        (MenuTree tree, ParameterPlan plan, TouchBuilder.TouchParameters touch, List<TriggerPlan> plans) PlanTouch()
        {
            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var virtualFx);
            var existing = ParametersWriter.ExistingParameters(avatar.Descriptor.expressionParameters, virtualFx);
            var plan = ParameterPlanner.Plan(tree, existing);
            var touch = TouchBuilder.Describe(tree, existing, plan);
            TouchBuilder.Create(context, tree, touch);
            var (plans, _) = TriggerPlanner.Plan(tree, plan, existing, touch);
            context.DeactivateAllExtensionContexts();
            return (tree, plan, touch, plans);
        }

        /// <summary>Model, plan, defaults, toggle layers, trigger layers, touch receivers and flip layers, inside an NDMF context; returns the committed FX.</summary>
        AnimatorController BuildFull(out List<ErrorContext> errors, out ParameterPlan plan, out TouchBuilder.TouchParameters touch, out MenuTree tree)
            => BuildFull(avatar, menu, out errors, out plan, out touch, out tree, out _);

        /// <summary>Model, plan, defaults, toggle layers, trigger layers, touch receivers, flip layers and Follow distance's own layers plus its shared pause layers, inside an NDMF context; returns the committed FX. Does not run the final pass (ReleasePinsToRest, MovePinLayersLast, RemoveForeignOwnedPauseLayers): tests that need those run them themselves, on their own second context, the way the real build's Optimizing phase does.</summary>
        static AnimatorController BuildFull(TestAvatar avatar, AvatarDoc menu, out List<ErrorContext> errors, out ParameterPlan plan, out TouchBuilder.TouchParameters touch, out MenuTree tree, out PinLayerState pins)
        {
            var builtTree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var virtualFx);
            var existing = ParametersWriter.ExistingParameters(avatar.Descriptor.expressionParameters, virtualFx);
            var builtPlan = ParameterPlanner.Plan(builtTree, existing);
            var builtTouch = TouchBuilder.Describe(builtTree, existing, builtPlan);
            var (plans, planMessages) = TriggerPlanner.Plan(builtTree, builtPlan, existing, builtTouch);
            var baseState = BaseState.Capture(builtTree);
            DefaultsApplier.Apply(builtTree, builtPlan, baseState, plans);
            var builtPins = new PinLayerState();
            var captured = ErrorReport.CaptureErrors(() =>
            {
                L.Report(planMessages);
                L.Report(AnimatorBuilder.Build(asc, builtTree, builtPlan, baseState));
                L.Report(TriggerBuilder.Build(asc, builtTree, plans, baseState));
                L.Report(TouchBuilder.Create(context, builtTree, builtTouch));
                L.Report(TouchBuilder.BuildFlipLayers(asc, builtTree, builtPlan, builtTouch));
                L.Report(TouchBuilder.BuildFollowDistanceLayers(asc, builtTree, builtTouch, baseState, avatar.Descriptor, builtPins.Names, builtPins));
                L.Report(TouchBuilder.BuildTrackingPauseLayers(asc, builtTree.AvatarRoot, builtPins.Names, builtPins));
            });
            context.DeactivateAllExtensionContexts();
            errors = captured;
            plan = builtPlan;
            touch = builtTouch;
            tree = builtTree;
            pins = builtPins;
            return FxOf(avatar);
        }

        MenuEntry FollowDistanceTrigger(string name, Transform anchor)
        {
            var entry = ContactTrigger(name, anchor);
            entry.touchMode = TouchMode.FollowDistance;
            entry.objects.Clear();
            return entry;
        }

        [Test]
        public void ContactReceiverIsCreatedOnTheCloneOnly()
        {
            var trigger = ContactTrigger("Poke", avatar.Hat.transform);
            trigger.contactOffset = new Vector3(0f, 0.1f, 0f);
            trigger.contactRadius = 0.03f;
            trigger.contactTags = new List<string> { "Hand" };

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                var processedHat = processed.transform.Find("Hat");
                var receiver = processedHat.GetComponentInChildren<VRCContactReceiver>();
                Assert.That(receiver, Is.Not.Null);
                Assert.That(receiver.transform.IsChildOf(processed.transform), Is.True, "the receiver sits under the processed avatar");
                Assert.That(receiver.transform.localPosition, Is.EqualTo(new Vector3(0f, 0.1f, 0f)));
                Assert.That(receiver.radius, Is.EqualTo(0.03f));
                Assert.That(receiver.collisionTags, Is.EqualTo(new List<string> { "Hand" }));
                Assert.That(receiver.allowSelf, Is.True);
                Assert.That(receiver.allowOthers, Is.True);
                Assert.That(receiver.parameter, Is.EqualTo(TouchBuilder.ParameterFor(trigger, "AD")));

                Assert.That(avatar.Hat.GetComponentInChildren<VRCContactReceiver>(true), Is.Null, "the scene object was never touched");
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                GeneratedAssets.CleanUp();
            }
        }

        [Test]
        public void OutsideAnchorCreatesNothingAnywhere()
        {
            var outside = new GameObject("OtherAvatar");
            GameObject processed = null;
            try
            {
                var trigger = ContactTrigger("Poke", outside.transform);

                var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
                var touch = TouchBuilder.Describe(tree, new Dictionary<string, ExistingParameter>());

                Assert.That(tree.Messages.Any(m => m.Key == "ad.error.touch_outside_avatar" && m.EntryIds.Contains(trigger.id)), Is.True);
                Assert.That(touch.ByTriggerId.ContainsKey(trigger.id), Is.False, "an anchor on another avatar is never described");

                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                Assert.That(outside.GetComponentsInChildren<VRCContactReceiver>(true), Is.Empty, "nothing was parented under the scene anchor");
                Assert.That(outside.transform.childCount, Is.EqualTo(0), "no AD Contact child was created in the scene");
                Assert.That(processed.GetComponentsInChildren<VRCContactReceiver>(true), Is.Empty, "and none on the processed avatar either");
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                Object.DestroyImmediate(outside);
                GeneratedAssets.CleanUp();
            }
        }

        [Test]
        public void OutsidePhysBoneKeepsItsEmptyParameter()
        {
            var outside = new GameObject("OtherAvatar");
            try
            {
                var pb = outside.AddComponent<VRCPhysBone>();
                pb.parameter = "";
                var trigger = PhysBoneTrigger("Ear", pb);

                var (tree, _, touch, _) = PlanTouch();

                Assert.That(string.IsNullOrEmpty(pb.parameter), Is.True, "the scene PhysBone of another avatar was never written");
                Assert.That(touch.ByTriggerId.ContainsKey(trigger.id), Is.False);
                Assert.That(tree.Messages.Any(m => m.Key == "ad.error.touch_outside_avatar" && m.EntryIds.Contains(trigger.id)), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(outside);
            }
        }

        [Test]
        public void ContactHoldTriggerBuildsAnOrdinaryTriggerLayer()
        {
            var trigger = ContactTrigger("Poke", avatar.Hat.transform);

            var fx = BuildFull(out var errors, out _, out _, out _);

            Assert.That(errors, Is.Empty);
            var name = TouchBuilder.ParameterFor(trigger, "AD");
            var layer = Layer(fx, "AD: Poke");
            var off = State(layer, "Off");
            State(layer, "On");
            var toOn = off.transitions.Single();
            Assert.That(toOn.conditions.Single().parameter, Is.EqualTo(name));
            Assert.That(toOn.conditions.Single().mode, Is.EqualTo(AnimatorConditionMode.If));
            Assert.That(fx.parameters.Single(p => p.name == name).type, Is.EqualTo(AnimatorControllerParameterType.Bool));
        }

        [Test]
        public void ProximityContactUsesAFloatAndGreater()
        {
            var trigger = ContactTrigger("Near", avatar.Hat.transform);
            trigger.contactProximity = 0.4f;

            var fx = BuildFull(out var errors, out _, out _, out _);

            Assert.That(errors, Is.Empty);
            var name = TouchBuilder.ParameterFor(trigger, "AD");
            Assert.That(fx.parameters.Single(p => p.name == name).type, Is.EqualTo(AnimatorControllerParameterType.Float));
            var toOn = State(Layer(fx, "AD: Near"), "Off").transitions.Single();
            Assert.That(toOn.conditions.Single().mode, Is.EqualTo(AnimatorConditionMode.Greater));
            Assert.That(toOn.conditions.Single().threshold, Is.EqualTo(0.4f));
        }

        [Test]
        public void ContactParameterStaysOutOfTheExpressionParameters()
        {
            ContactTrigger("Poke", avatar.Hat.transform);

            BuildFull(out var errors, out _, out _, out _);

            Assert.That(errors, Is.Empty);
            Assert.That(avatar.Descriptor.expressionParameters.parameters.Any(p => p.name.Contains("Contact")), Is.False);
        }

        [Test]
        public void PhysBoneWithoutAParameterGetsOneOnTheClone()
        {
            var pb = avatar.Hat.AddComponent<VRCPhysBone>();
            pb.parameter = "";
            var trigger = PhysBoneTrigger("Ear", pb);

            var (_, _, touch, _) = PlanTouch();

            var expected = TouchBuilder.ParameterFor(trigger, "AD");
            Assert.That(pb.parameter, Is.EqualTo(expected));
            Assert.That(touch.ByTriggerId[trigger.id].Parameter, Is.EqualTo(expected + "_IsGrabbed"));
        }

        [Test]
        public void PhysBoneWithAParameterKeepsIt()
        {
            var pb = avatar.Hat.AddComponent<VRCPhysBone>();
            pb.parameter = "MyEar";
            PhysBoneTrigger("Ear", pb);

            PlanTouch();

            Assert.That(pb.parameter, Is.EqualTo("MyEar"));
        }

        [Test]
        public void PhysBoneEventsMapToSuffixesAndConditions()
        {
            var cases = new (PhysBoneEvent evt, string suffix, PlannedType type, ParameterCondition mode, float value)[]
            {
                (PhysBoneEvent.Grabbed, "_IsGrabbed", PlannedType.Bool, ParameterCondition.IsTrue, 0f),
                (PhysBoneEvent.Posed, "_IsPosed", PlannedType.Bool, ParameterCondition.IsTrue, 0f),
                (PhysBoneEvent.AngleAbove, "_Angle", PlannedType.Float, ParameterCondition.Greater, 0.5f),
                (PhysBoneEvent.StretchAbove, "_Stretch", PlannedType.Float, ParameterCondition.Greater, 0.5f),
                (PhysBoneEvent.SquishAbove, "_Squish", PlannedType.Float, ParameterCondition.Greater, 0.5f),
            };
            var entries = new List<MenuEntry>();
            foreach (var c in cases)
            {
                var pb = new GameObject("PB_" + c.evt).AddComponent<VRCPhysBone>();
                pb.transform.SetParent(avatar.Root.transform);
                var trigger = PhysBoneTrigger("Trig_" + c.evt, pb);
                trigger.physBoneEvent = c.evt;
                entries.Add(trigger);
            }

            var (_, _, touch, _) = PlanTouch();

            for (int i = 0; i < cases.Length; i++)
            {
                var c = cases[i];
                var info = touch.ByTriggerId[entries[i].id];
                Assert.That(info.Type, Is.EqualTo(c.type), c.evt.ToString());
                Assert.That(info.Mode, Is.EqualTo(c.mode), c.evt.ToString());
                Assert.That(info.Value, Is.EqualTo(c.value), c.evt.ToString());
                Assert.That(info.Parameter.EndsWith(c.suffix), Is.True, c.evt.ToString());
            }
        }

        [Test]
        public void TwoTriggersShareOnePhysBoneParameter()
        {
            var pb = avatar.Hat.AddComponent<VRCPhysBone>();
            var t1 = PhysBoneTrigger("Grab", pb);
            t1.physBoneEvent = PhysBoneEvent.Grabbed;
            var t2 = PhysBoneTrigger("Pose", pb);
            t2.physBoneEvent = PhysBoneEvent.Posed;

            var (_, _, touch, _) = PlanTouch();

            var basePrefix = pb.parameter;
            Assert.That(basePrefix, Is.Not.Empty);
            Assert.That(touch.ByTriggerId[t1.id].Parameter, Is.EqualTo(basePrefix + "_IsGrabbed"));
            Assert.That(touch.ByTriggerId[t2.id].Parameter, Is.EqualTo(basePrefix + "_IsPosed"));
        }

        [Test]
        public void FlipLayerSetsTheToggleOnAndOff()
        {
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var trigger = menu.AddEntry(EntryKind.Trigger, "Flip");
            trigger.source = TriggerSource.Contact;
            trigger.contactAnchor = avatar.Glasses.transform;
            trigger.touchMode = TouchMode.FlipToggle;
            trigger.flipToggleId = hat.id;

            var fx = BuildFull(out var errors, out _, out _, out _);

            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: Flip (flip)");
            var idle = State(layer, "Idle");
            var setOn = State(layer, "Set on");
            var setOff = State(layer, "Set off");
            Assert.That(layer.stateMachine.defaultState, Is.SameAs(idle));

            var toOn = idle.transitions.Single(t => t.destinationState == setOn);
            var toOff = idle.transitions.Single(t => t.destinationState == setOff);
            Assert.That(toOn.conditions.Length, Is.EqualTo(2), "the touch condition and the toggle's off condition");
            Assert.That(toOff.conditions.Length, Is.EqualTo(2), "the touch condition and the toggle's on condition");

            var driverOn = setOn.behaviours.OfType<VRCAvatarParameterDriver>().Single();
            Assert.That(driverOn.localOnly, Is.True);
            Assert.That(driverOn.parameters.Single().name, Is.EqualTo("AD/Hat"));
            Assert.That(driverOn.parameters.Single().value, Is.EqualTo(1f));

            var driverOff = setOff.behaviours.OfType<VRCAvatarParameterDriver>().Single();
            Assert.That(driverOff.parameters.Single().name, Is.EqualTo("AD/Hat"));
            Assert.That(driverOff.parameters.Single().value, Is.EqualTo(0f));

            Assert.That(setOn.transitions.Single().destinationState, Is.SameAs(idle));
            Assert.That(setOff.transitions.Single().destinationState, Is.SameAs(idle));
        }

        [Test]
        public void FlipOnAGroupMemberWritesTheGroupValue()
        {
            var a = menu.AddEntry(EntryKind.Toggle, "A");
            a.exclusiveGroup = "Outfits";
            a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var b = menu.AddEntry(EntryKind.Toggle, "B");
            b.exclusiveGroup = "Outfits";
            b.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var trigger = menu.AddEntry(EntryKind.Trigger, "Flip");
            trigger.source = TriggerSource.Contact;
            trigger.contactAnchor = avatar.Body.transform;
            trigger.touchMode = TouchMode.FlipToggle;
            trigger.flipToggleId = a.id;

            var fx = BuildFull(out var errors, out var plan, out _, out var tree);

            Assert.That(errors, Is.Empty);
            var binding = plan.Bindings[tree.FindControl(a.id)];
            var layer = Layer(fx, "AD: Flip (flip)");
            var driverOn = State(layer, "Set on").behaviours.OfType<VRCAvatarParameterDriver>().Single();
            Assert.That(driverOn.parameters.Single().name, Is.EqualTo(binding.Parameter.Name));
            Assert.That(driverOn.parameters.Single().value, Is.EqualTo(binding.Value));
            var driverOff = State(layer, "Set off").behaviours.OfType<VRCAvatarParameterDriver>().Single();
            Assert.That(driverOff.parameters.Single().name, Is.EqualTo(binding.Parameter.Name));
            Assert.That(driverOff.parameters.Single().value, Is.EqualTo(0f));
        }

        [Test]
        public void FlipTargetMustBeAToggle()
        {
            var other = menu.AddEntry(EntryKind.Trigger, "Other");
            other.source = TriggerSource.Parameter;
            other.sourceParameter = "AFK";
            other.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var trigger = menu.AddEntry(EntryKind.Trigger, "Flip");
            trigger.source = TriggerSource.Contact;
            trigger.contactAnchor = avatar.Hat.transform;
            trigger.touchMode = TouchMode.FlipToggle;
            trigger.flipToggleId = other.id;

            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            Assert.That(tree.HasErrors, Is.True);
            Assert.That(tree.Messages.Any(m => m.Key == "ad.error.flip_target" && m.EntryIds.Contains(trigger.id)), Is.True);
        }

        [Test]
        public void ContactValidationErrors()
        {
            var noAnchor = menu.AddEntry(EntryKind.Trigger, "NoAnchor");
            noAnchor.source = TriggerSource.Contact;
            noAnchor.objects.Add(new ObjectTarget { target = avatar.Glasses });

            var noTags = menu.AddEntry(EntryKind.Trigger, "NoTags");
            noTags.source = TriggerSource.Contact;
            noTags.contactAnchor = avatar.Hat.transform;
            noTags.contactTags = new List<string>();
            noTags.objects.Add(new ObjectTarget { target = avatar.Glasses });

            var nobody = menu.AddEntry(EntryKind.Trigger, "Nobody");
            nobody.source = TriggerSource.Contact;
            nobody.contactAnchor = avatar.Hat.transform;
            nobody.contactAllowSelf = false;
            nobody.contactAllowOthers = false;
            nobody.objects.Add(new ObjectTarget { target = avatar.Glasses });

            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            Assert.That(tree.Messages.Any(m => m.Key == "ad.error.contact_no_anchor" && m.EntryIds.Contains(noAnchor.id)), Is.True);
            Assert.That(tree.Messages.Any(m => m.Key == "ad.error.contact_no_tags" && m.EntryIds.Contains(noTags.id)), Is.True);
            Assert.That(tree.Messages.Any(m => m.Key == "ad.error.contact_nobody" && m.EntryIds.Contains(nobody.id)), Is.True);
        }

        [Test]
        public void FailedBuildCreatesNoReceiver()
        {
            var a = menu.AddEntry(EntryKind.Toggle, "A");
            a.exclusiveGroup = "Outfits";
            a.groupValue = 1;
            a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var b = menu.AddEntry(EntryKind.Toggle, "B");
            b.exclusiveGroup = "Outfits";
            b.groupValue = 1;
            b.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var pb = avatar.Body.gameObject.AddComponent<VRCPhysBone>();
            PhysBoneTrigger("Ear", pb);
            ContactTrigger("Poke", avatar.Hat.transform);

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                Assert.That(processed, Is.Not.Null, "the pipeline reports the model error without throwing");
                Assert.That(processed.GetComponentsInChildren<VRCContactReceiver>(true), Is.Empty, "the gate never opened, so Create never ran");
                var processedPb = processed.GetComponentInChildren<VRCPhysBone>(true);
                Assert.That(string.IsNullOrEmpty(processedPb.parameter), Is.True);
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                GeneratedAssets.CleanUp();
            }
        }

        [Test]
        public void InspectorPlanDescribesTouchTriggersWithoutTouchingTheScene()
        {
            var pb = avatar.Hat.AddComponent<VRCPhysBone>();
            PhysBoneTrigger("Ear", pb);
            ContactTrigger("Poke", avatar.Glasses.transform);
            AvatarPlanCache.Invalidate();

            try
            {
                var plan = AvatarPlanCache.GetFor(menu);

                Assert.That(avatar.Root.GetComponentsInChildren<VRCContactReceiver>(true), Is.Empty, "the inspector never creates a receiver");
                Assert.That(string.IsNullOrEmpty(pb.parameter), Is.True, "the inspector never writes the PhysBone's parameter");
                Assert.That(plan.Triggers.Any(t => t.Entry.PlainName == "Poke"), Is.True, "the contact trigger still gets a hold mode trigger plan");
            }
            finally
            {
                AvatarPlanCache.Invalidate();
            }
        }

        [Test]
        public void TwoContactsWithTheSameNameGetDistinctParameters()
        {
            var first = ContactTrigger("Poke", avatar.Hat.transform);
            var second = ContactTrigger("Poke", avatar.Glasses.transform);

            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
            var touch = TouchBuilder.Describe(tree, new Dictionary<string, ExistingParameter>());

            var firstName = touch.ByTriggerId[first.id].Parameter;
            var secondName = touch.ByTriggerId[second.id].Parameter;
            Assert.That(firstName, Is.EqualTo("AD/Contact/Poke"));
            Assert.That(secondName, Is.EqualTo("AD/Contact/Poke 2"));
        }

#if DOCME_MA
        [Test]
        public void PhysBoneRenamedByModularAvatarWarns()
        {
            var holder = new GameObject("MA prefab");
            holder.transform.SetParent(avatar.Root.transform);
            var parameters = holder.AddComponent<nadena.dev.modular_avatar.core.ModularAvatarParameters>();
            parameters.parameters.Add(new nadena.dev.modular_avatar.core.ParameterConfig
            {
                nameOrPrefix = "Ear", isPrefix = true, internalParameter = true,
            });
            var pb = holder.AddComponent<VRCPhysBone>();
            pb.parameter = "Ear";
            var trigger = PhysBoneTrigger("Ear", pb);

            var (tree, _, _, _) = PlanTouch();

            Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.physbone_renamed" && m.EntryIds.Contains(trigger.id)), Is.True);
        }
#endif

        [Test]
        public void OldTouchDataIsRepairedAtBuild()
        {
            var poke = ContactTrigger("Poke", avatar.Hat.transform);
            // Unity skips field initialisers for data an undo or an old prefab override deserialises, so a saved
            // trigger can come back with a radius of 0 and a threshold of 0.
            poke.contactRadius = 0f;
            var pb = avatar.Glasses.AddComponent<VRCPhysBone>();
            var bend = PhysBoneTrigger("Bend", pb);
            bend.physBoneEvent = PhysBoneEvent.AngleAbove;
            bend.physBoneThreshold = 0f;

            var (_, _, touch, _) = PlanTouch();

            Assert.That(touch.ByTriggerId[poke.id].Radius, Is.EqualTo(0.005f).Within(0.0001f), "a dead contact is clamped to the smallest radius the inspector allows");
            Assert.That(touch.ByTriggerId[bend.id].Value, Is.EqualTo(0.5f).Within(0.0001f), "a float event with no threshold takes the spec's default");
        }

        [Test]
        public void ContactNameMatchingAPlannedParameterIsMadeUnique()
        {
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            toggle.parameterName = "AD/Contact/Poke";
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            var trigger = ContactTrigger("Poke", avatar.Glasses.transform);

            var (_, _, touch, _) = PlanTouch();

            Assert.That(touch.ByTriggerId[trigger.id].Parameter, Is.EqualTo("AD/Contact/Poke 2"));
        }

        [Test]
        public void ContactNameMatchingAnExistingParameterIsMadeUnique()
        {
            var trigger = ContactTrigger("Poke", avatar.Hat.transform);
            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
            var existing = new Dictionary<string, ExistingParameter> { ["AD/Contact/Poke"] = new ExistingParameter { Type = PlannedType.Bool } };

            var touch = TouchBuilder.Describe(tree, existing);

            Assert.That(touch.ByTriggerId[trigger.id].Parameter, Is.EqualTo("AD/Contact/Poke 2"));
        }

        // ---- Follow distance -----------------------------------------------------------

        [Test]
        public void FollowDistanceBuildsAProximityReceiverWithAnUnsyncedFloat()
        {
            var trigger = FollowDistanceTrigger("Pat", avatar.Hat.transform);
            trigger.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            var name = TouchBuilder.ParameterFor(trigger, "AD");

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                var receiver = processed.GetComponentInChildren<VRCContactReceiver>();
                Assert.That(receiver, Is.Not.Null);
                Assert.That(receiver.receiverType, Is.EqualTo(ContactReceiver.ReceiverType.Proximity));
                Assert.That(receiver.parameter, Is.EqualTo(name));

                var descriptor = processed.GetComponent<VRCAvatarDescriptor>();
                var fx = (AnimatorController)descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
                var parameter = fx.parameters.Single(p => p.name == name);
                Assert.That(parameter.type, Is.EqualTo(AnimatorControllerParameterType.Float));
                Assert.That(descriptor.expressionParameters.parameters.Any(p => p.name == name), Is.False, "costs 0 bits: animator only");
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                GeneratedAssets.CleanUp();
            }
        }

        [Test]
        public void FollowDistanceBlendsFromOffToOnAtFullAt()
        {
            var trigger = FollowDistanceTrigger("Pat", avatar.Hat.transform);
            trigger.contactFullAt = 0.6f;
            trigger.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });

            var fx = BuildFull(out var errors, out _, out _, out _);

            Assert.That(errors, Is.Empty);
            var name = TouchBuilder.ParameterFor(trigger, "AD");
            var layer = Layer(fx, "AD: Pat (distance)");
            var near = State(layer, "Near");
            var tree = (BlendTree)near.motion;
            Assert.That(tree.blendType, Is.EqualTo(BlendTreeType.Simple1D));
            Assert.That(tree.blendParameter, Is.EqualTo(name));
            Assert.That(tree.children.Select(c => c.threshold), Is.EqualTo(new[] { 0f, 0.6f }));
            Assert.That(FloatValue(tree.children[0].motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), Is.EqualTo(0f), "the off pose, so the blend is defined");
            Assert.That(FloatValue(tree.children[1].motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), Is.EqualTo(100f));
        }

        [Test]
        public void FollowDistanceTransitionsAtZero()
        {
            var trigger = FollowDistanceTrigger("Pat", avatar.Hat.transform);
            trigger.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });

            var fx = BuildFull(out var errors, out _, out _, out _);

            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: Pat (distance)");
            var far = State(layer, "Far");
            var near = State(layer, "Near");
            Assert.That(layer.stateMachine.defaultState, Is.SameAs(far));

            var toNear = far.transitions.Single();
            Assert.That(toNear.destinationState, Is.SameAs(near));
            Assert.That(toNear.hasExitTime, Is.False);
            Assert.That(toNear.duration, Is.EqualTo(0f));
            Assert.That(toNear.conditions.Single().mode, Is.EqualTo(AnimatorConditionMode.Greater));
            Assert.That(toNear.conditions.Single().threshold, Is.EqualTo(0.001f));

            var toFar = near.transitions.Single();
            Assert.That(toFar.destinationState, Is.SameAs(far));
            Assert.That(toFar.hasExitTime, Is.False);
            Assert.That(toFar.duration, Is.EqualTo(0f));
            Assert.That(toFar.conditions.Single().mode, Is.EqualTo(AnimatorConditionMode.Less));
            Assert.That(toFar.conditions.Single().threshold, Is.EqualTo(0.001f));
        }

        /// <summary>Builds only Follow distance's own layer, without the rest of AnimatorBuilder, so a fake face tracking layer can be added to fx before the deferred release pass runs, exactly the two-context shape PinReleaseTests uses.</summary>
        static (MenuTree tree, TouchBuilder.TouchParameters touch, PinLayerState pins) BuildDistanceLayerOnly(TestAvatar avatar, AvatarDoc menu, MenuEntry trigger)
        {
            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var virtualFx);
            var existing = ParametersWriter.ExistingParameters(avatar.Descriptor.expressionParameters, virtualFx);
            var plan = ParameterPlanner.Plan(tree, existing);
            var touch = TouchBuilder.Describe(tree, existing, plan);
            var baseState = BaseState.Capture(tree);
            var pins = new PinLayerState();
            L.Report(TouchBuilder.BuildFollowDistanceLayers(asc, tree, touch, baseState, avatar.Descriptor, pins.Names, pins));
            context.DeactivateAllExtensionContexts();
            return (tree, touch, pins);
        }

        /// <summary>Adds a plain (non pin, non AvatarDoc) layer with a Direct tree driving one shape, simulating a face tracking controller Modular Avatar or VRCFury would merge in after AvatarDoc's own build.</summary>
        static void AddFakeFaceTracking(AnimatorServicesContext asc, string shape, float value)
        {
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx);
            var faceTracking = fx.AddLayer(LayerPriority.Default, "Face Tracking");
            var directClip = VirtualClip.Create(shape + " Track");
            directClip.SetFloatCurve(EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape." + shape), AnimationCurve.Constant(0f, 1f / 60f, value));
            var directTree = VirtualBlendTree.Create("Direct");
            directTree.BlendType = BlendTreeType.Direct;
            directTree.Children = ImmutableList.Create(new VirtualBlendTree.VirtualChildMotion { Motion = directClip });
            var trackState = faceTracking.StateMachine.AddState("Tracking", directTree);
            trackState.WriteDefaultValues = true;
            var disabled = faceTracking.StateMachine.AddState("Disabled", null);
            disabled.WriteDefaultValues = false;
        }

        /// <summary>Builds the distance layer on a fresh avatar, merges a fake face tracking layer after it, then runs the same final pass the Optimizing phase does (release, move). Returns the owned TestAvatar (dispose it) and its committed FX; writeDefaultsOff picks TestAvatar.Create's own starting Write Defaults, which DecideWriteDefaults would otherwise have used had this layer not always forced Write Defaults off.</summary>
        static TestAvatar BuildDistanceLayerPastFaceTracking(bool writeDefaultsOff, out AnimatorController fx)
        {
            var owned = TestAvatar.Create(writeDefaultsOff);
            var ownedMenu = owned.AddMenu();
            var trigger = ownedMenu.AddEntry(EntryKind.Trigger, "Pat");
            trigger.source = TriggerSource.Contact;
            trigger.touchMode = TouchMode.FollowDistance;
            trigger.contactAnchor = owned.Hat.transform;
            trigger.blendShapes.Add(new BlendShapeTarget { renderer = owned.Body, shape = "Blush", weight = 100f });
            owned.Body.SetBlendShapeWeight(0, 22f);

            var (_, _, pins) = BuildDistanceLayerOnly(owned, ownedMenu, trigger);

            var context2 = new BuildContext(owned.Root, null);
            var asc2 = context2.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            AddFakeFaceTracking(asc2, "Blush", 40f);
            AnimatorBuilder.ReleasePinsToRest(asc2, pins);
            AnimatorBuilder.MovePinLayersLast(asc2, pins.Names);
            context2.DeactivateAllExtensionContexts();

            fx = FxOf(owned);
            return owned;
        }

        [Test]
        public void FollowDistanceFarStateReleasesAShapeFaceTrackingOwns()
        {
            var trigger = FollowDistanceTrigger("Pat", avatar.Hat.transform);
            trigger.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            avatar.Body.SetBlendShapeWeight(0, 22f);

            var (_, _, pins) = BuildDistanceLayerOnly(avatar, menu, trigger);

            // Nobody else animates Blush yet: the release pass has to hand it back to rest itself.
            var context1 = new BuildContext(avatar.Root, null);
            var asc1 = context1.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            AnimatorBuilder.ReleasePinsToRest(asc1, pins);
            context1.DeactivateAllExtensionContexts();
            var farReleased = State(Layer(FxOf(avatar), "AD: Pat (distance)"), "Far");
            var releasedCurve = CurveOf(farReleased.motion, "Body", "blendShape.Blush");
            Assert.That(releasedCurve, Is.Not.Null, "without a tracking layer, Far carries the rest value itself");
            Assert.That(releasedCurve.Evaluate(0f), Is.EqualTo(22f));
        }

        [Test]
        public void FarNeverHoldsTheBlinkShape()
        {
            avatar.Descriptor.enableEyeLook = true;
            avatar.Descriptor.customEyeLookSettings.eyelidType = VRCAvatarDescriptor.EyelidType.Blendshapes;
            avatar.Descriptor.customEyeLookSettings.eyelidsSkinnedMesh = avatar.Body;
            avatar.Descriptor.customEyeLookSettings.eyelidsBlendshapes = new[] { avatar.Body.sharedMesh.GetBlendShapeIndex("Eye_Big"), -1, -1 };
            var trigger = FollowDistanceTrigger("Blink", avatar.Hat.transform);
            trigger.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 100f });
            trigger.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            avatar.Body.SetBlendShapeWeight(0, 22f);

            var (_, _, pins) = BuildDistanceLayerOnly(avatar, menu, trigger);
            var context1 = new BuildContext(avatar.Root, null);
            var asc1 = context1.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            AnimatorBuilder.ReleasePinsToRest(asc1, pins);
            context1.DeactivateAllExtensionContexts();

            var far = State(Layer(FxOf(avatar), "AD: Blink (distance)"), "Far");
            Assert.That(CurveOf(far.motion, "Body", "blendShape.Eye_Big"), Is.Null,
                "the descriptor's own blink shape is never registered for release; VRChat's own tracking owns it while no hand is near");
            var blushCurve = CurveOf(far.motion, "Body", "blendShape.Blush");
            Assert.That(blushCurve, Is.Not.Null, "an ordinary shape on the same trigger still gets its rest curve");
            Assert.That(blushCurve.Evaluate(0f), Is.EqualTo(22f));
        }

        [Test]
        public void FarNeverHoldsAViseme()
        {
            avatar.Descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape;
            avatar.Descriptor.VisemeSkinnedMesh = avatar.Body;
            avatar.Descriptor.VisemeBlendShapes = new string[15];
            avatar.Descriptor.VisemeBlendShapes[0] = "Eye_Big";
            var trigger = FollowDistanceTrigger("Boop", avatar.Hat.transform);
            trigger.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 100f });
            trigger.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            avatar.Body.SetBlendShapeWeight(0, 22f);

            var (_, _, pins) = BuildDistanceLayerOnly(avatar, menu, trigger);
            var context1 = new BuildContext(avatar.Root, null);
            var asc1 = context1.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            AnimatorBuilder.ReleasePinsToRest(asc1, pins);
            context1.DeactivateAllExtensionContexts();

            var far = State(Layer(FxOf(avatar), "AD: Boop (distance)"), "Far");
            Assert.That(CurveOf(far.motion, "Body", "blendShape.Eye_Big"), Is.Null,
                "the descriptor's own viseme shape is never registered for release; VRChat's own lip sync owns it while no hand is near");
            var blushCurve = CurveOf(far.motion, "Body", "blendShape.Blush");
            Assert.That(blushCurve, Is.Not.Null, "an ordinary shape on the same trigger still gets its rest curve");
            Assert.That(blushCurve.Evaluate(0f), Is.EqualTo(22f));
        }

        [Test]
        public void FollowDistanceFarGetsRestCurveOnWriteDefaultsOnAvatarDespiteAnUnrelatedLayer()
        {
            using (var owned = TestAvatar.Create(false))
            {
                var ownedMenu = owned.AddMenu();
                var trigger = ownedMenu.AddEntry(EntryKind.Trigger, "Pat");
                trigger.source = TriggerSource.Contact;
                trigger.touchMode = TouchMode.FollowDistance;
                trigger.contactAnchor = owned.Hat.transform;
                trigger.blendShapes.Add(new BlendShapeTarget { renderer = owned.Body, shape = "Blush", weight = 100f });
                owned.Body.SetBlendShapeWeight(0, 22f);

                var (_, _, pins) = BuildDistanceLayerOnly(owned, ownedMenu, trigger);

                // A Write Defaults on layer that never touches Blush at all, and no face tracking layer: owning
                // means animating (finding 14), so this must not be mistaken for an owner just because it is WD on.
                var context2 = new BuildContext(owned.Root, null);
                var asc2 = context2.ActivateExtensionContextRecursive<AnimatorServicesContext>();
                asc2.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx2);
                var unrelated = fx2.AddLayer(LayerPriority.Default, "Unrelated");
                var idle = unrelated.StateMachine.AddState("Idle", VirtualClip.Create("Idle"));
                idle.WriteDefaultValues = true;
                AnimatorBuilder.ReleasePinsToRest(asc2, pins);
                context2.DeactivateAllExtensionContexts();

                var far = State(Layer(FxOf(owned), "AD: Pat (distance)"), "Far");
                var curve = CurveOf(far.motion, "Body", "blendShape.Blush");
                Assert.That(curve, Is.Not.Null, "an unrelated Write Defaults on layer that never animates Blush does not own it");
                Assert.That(curve.Evaluate(0f), Is.EqualTo(22f));
            }
        }

        [TestCase(false, TestName = "FollowDistanceLayerMovesAfterFaceTrackingOnWriteDefaultsOff")]
        [TestCase(true, TestName = "FollowDistanceLayerMovesAfterFaceTrackingOnWriteDefaultsOn")]
        public void FollowDistanceLayerMovesAfterFaceTracking(bool writeDefaultsOn)
        {
            using (var owned = BuildDistanceLayerPastFaceTracking(writeDefaultsOff: !writeDefaultsOn, out var fx))
            {
                var far = State(Layer(fx, "AD: Pat (distance)"), "Far");
                Assert.That(CurveOf(far.motion, "Body", "blendShape.Blush"), Is.Null,
                    "face tracking's Direct tree owns Blush, so Far leaves it alone, whatever Write Defaults the avatar decided");

                var names = fx.layers.Select(l => l.name).ToList();
                Assert.That(names.IndexOf("AD: Pat (distance)"), Is.GreaterThan(names.IndexOf("Face Tracking")),
                    "the distance layer is a pin layer for ordering, so it always ends up after a layer merged in later");
            }
        }

        [Test]
        public void FollowDistanceUsesOnlyTheFirstConditionAndSaysSo()
        {
            var trigger = menu.AddEntry(EntryKind.Trigger, "Pat");
            trigger.source = TriggerSource.Contact;
            trigger.touchMode = TouchMode.FollowDistance;
            trigger.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            var g0 = new ConditionGroup();
            g0.all.Add(new WhenCondition { kind = WhenConditionKind.Contact, contactAnchor = avatar.Hat.transform, contactTags = new List<string> { "Hand" } });
            var g1 = new ConditionGroup();
            g1.all.Add(new WhenCondition { kind = WhenConditionKind.Contact, contactAnchor = avatar.Glasses.transform, contactTags = new List<string> { "Hand" } });
            trigger.when.Add(g0);
            trigger.when.Add(g1);
            // Only while is a requirement Fade reads, so one pointing at a toggle that no longer
            // exists is an error on it like on any trigger; the second card is still ignored.
            trigger.gateToggleId = "missing-toggle";

            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
            var touch = TouchBuilder.Describe(tree, new Dictionary<string, ExistingParameter>());

            var message = tree.Messages.Single(m => m.Key == "ad.warn.follow_distance_one_contact" && m.EntryIds.Contains(trigger.id));
            Assert.That(message.Severity, Is.EqualTo(MessageSeverity.Warning), "reported, but must never block the build");
            Assert.That(tree.Messages.Any(m => m.Key == "ad.error.gate_target" && m.EntryIds.Contains(trigger.id)), Is.True, "Only while is read now, so a broken one says so");
            trigger.gateToggleId = "";
            tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
            touch = TouchBuilder.Describe(tree, new Dictionary<string, ExistingParameter>());
            Assert.That(tree.HasErrors, Is.False, "the ignored second card never blocks the build");
            var key = TouchBuilder.TouchKey(trigger, 0, 0);
            Assert.That(touch.ByTriggerId.ContainsKey(key), Is.True);
            Assert.That(touch.ByTriggerId[key].Source.contactAnchor, Is.SameAs(avatar.Hat.transform), "the first condition, not the second");
            Assert.That(touch.Created.Count, Is.EqualTo(1), "one receiver only, whatever else the stale data carries");
        }

        [Test]
        public void PhysBoneFollowDistanceModeBehavesAsHold()
        {
            // A stale FollowDistance left over from switching a trigger's source to PhysBone: the mode does
            // nothing here, so the trigger builds exactly as a Hold trigger would.
            var pb = avatar.Hat.AddComponent<VRCPhysBone>();
            var trigger = PhysBoneTrigger("Ear", pb);
            trigger.touchMode = TouchMode.FollowDistance;
            Assert.That(trigger.IsFollowDistanceTrigger, Is.False);

            var fx = BuildFull(out var errors, out _, out _, out _);

            Assert.That(errors, Is.Empty);
            Assert.That(Layer(fx, "AD: Ear").stateMachine.states.Select(s => s.state.name), Is.EquivalentTo(new[] { "Off", "On" }),
                "an ordinary Hold layer, not a distance one");
        }

        // ---- Follow distance: the shared blinking and lip sync pause layers --------------------

        [Test]
        public void PauseLayersNeverTouchTrackingAtLoad()
        {
            avatar.Descriptor.enableEyeLook = true;
            avatar.Descriptor.customEyeLookSettings.eyelidType = VRCAvatarDescriptor.EyelidType.Blendshapes;
            avatar.Descriptor.customEyeLookSettings.eyelidsSkinnedMesh = avatar.Body;
            avatar.Descriptor.customEyeLookSettings.eyelidsBlendshapes = new[] { avatar.Body.sharedMesh.GetBlendShapeIndex("Blush"), -1, -1 };
            var blink = FollowDistanceTrigger("Blink", avatar.Hat.transform);
            blink.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });

            var fx = BuildFull(out var errors, out _, out _, out _);

            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: Blink pause");
            var idle = State(layer, "Idle");
            var load = State(layer, "Load");
            Assert.That(layer.stateMachine.defaultState, Is.SameAs(load));
            Assert.That(load.behaviours, Is.Empty, "the default state changes nothing at load");
            Assert.That(idle.behaviours, Is.Empty, "and neither does Idle, where it goes next");
        }

        [Test]
        public void TwoTriggersOnOnePartShareOnePause()
        {
            avatar.Descriptor.enableEyeLook = true;
            avatar.Descriptor.customEyeLookSettings.eyelidType = VRCAvatarDescriptor.EyelidType.Blendshapes;
            avatar.Descriptor.customEyeLookSettings.eyelidsSkinnedMesh = avatar.Body;
            avatar.Descriptor.customEyeLookSettings.eyelidsBlendshapes = new[] { avatar.Body.sharedMesh.GetBlendShapeIndex("Blush"), avatar.Body.sharedMesh.GetBlendShapeIndex("Eye_Big"), -1 };
            var first = FollowDistanceTrigger("Blink1", avatar.Hat.transform);
            first.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            var second = FollowDistanceTrigger("Blink2", avatar.Glasses.transform);
            second.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 100f });

            var fx = BuildFull(out var errors, out _, out var touch, out _);

            Assert.That(errors, Is.Empty);
            // The pause reads each layer's own pause flag, which only its effect states set, not the receivers.
            var firstParam = touch.PauseFlags[first.id].Parameter;
            var secondParam = touch.PauseFlags[second.id].Parameter;
            var layer = Layer(fx, "AD: Blink pause");
            var idle = State(layer, "Idle");
            var paused = State(layer, "Paused");
            var release = State(layer, "Release");

            var toPaused = idle.transitions;
            Assert.That(toPaused.Length, Is.EqualTo(2), "one Idle to Paused transition per contributing trigger, an OR");
            Assert.That(toPaused.Select(t => t.destinationState), Is.All.SameAs(paused));
            Assert.That(toPaused.Select(t => t.conditions.Single().parameter), Is.EquivalentTo(new[] { firstParam, secondParam }));
            Assert.That(toPaused.All(t => t.conditions.Single().mode == AnimatorConditionMode.Greater), Is.True);

            var toRelease = paused.transitions.Single();
            Assert.That(toRelease.destinationState, Is.SameAs(release));
            Assert.That(toRelease.conditions.Select(c => c.parameter), Is.EquivalentTo(new[] { firstParam, secondParam }), "one transition whose conditions are ANDed, both floats");
            Assert.That(toRelease.conditions.All(c => c.mode == AnimatorConditionMode.Less), Is.True);

            var backToIdle = release.transitions.Single();
            Assert.That(backToIdle.destinationState, Is.SameAs(idle));
            Assert.That(backToIdle.hasExitTime, Is.True);
            Assert.That(backToIdle.exitTime, Is.EqualTo(0f));
            Assert.That(backToIdle.duration, Is.EqualTo(0f));
        }

        [Test]
        public void AnotherSystemsTrackingControlWins()
        {
            avatar.Descriptor.enableEyeLook = true;
            avatar.Descriptor.customEyeLookSettings.eyelidType = VRCAvatarDescriptor.EyelidType.Blendshapes;
            avatar.Descriptor.customEyeLookSettings.eyelidsSkinnedMesh = avatar.Body;
            avatar.Descriptor.customEyeLookSettings.eyelidsBlendshapes = new[] { avatar.Body.sharedMesh.GetBlendShapeIndex("Blush"), -1, -1 };
            var blink = FollowDistanceTrigger("Blink", avatar.Hat.transform);
            blink.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });

            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var virtualFx);
            var existing = ParametersWriter.ExistingParameters(avatar.Descriptor.expressionParameters, virtualFx);
            var plan = ParameterPlanner.Plan(tree, existing);
            var touch = TouchBuilder.Describe(tree, existing, plan);
            var baseState = BaseState.Capture(tree);
            var pins = new PinLayerState();
            L.Report(TouchBuilder.BuildFollowDistanceLayers(asc, tree, touch, baseState, avatar.Descriptor, pins.Names, pins));
            L.Report(TouchBuilder.BuildTrackingPauseLayers(asc, tree.AvatarRoot, pins.Names, pins));
            Assert.That(pins.BlinkPauseLayerName, Is.EqualTo("AD: Blink pause"));

            // A VRCFT style template layer, merged in after AvatarDoc's own build, already sets Eyes to Animation.
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx);
            var vrcft = fx.AddLayer(LayerPriority.Default, "FT_Eye");
            var vrcftState = vrcft.StateMachine.AddState("Tracking", VirtualClip.Create("FT Idle"));
            var control = ScriptableObject.CreateInstance<VRCAnimatorTrackingControl>();
            control.trackingEyes = VRC_AnimatorTrackingControl.TrackingType.Animation;
            vrcftState.Behaviours = ImmutableList.Create<StateMachineBehaviour>(control);

            var messages = TouchBuilder.RemoveForeignOwnedPauseLayers(asc, pins);
            context.DeactivateAllExtensionContexts();

            Assert.That(messages.Any(m => m.Key == "ad.warn.follow_distance_tracking_left_to" && m.Args.Contains("FT_Eye")), Is.True);
            Assert.That(FxOf(avatar).layers.Any(l => l.name == "AD: Blink pause"), Is.False, "AvatarDoc's own pause layer steps aside");
            Assert.That(pins.Names.Contains("AD: Blink pause"), Is.False);
        }

        [Test]
        public void VrcfTrackingArbiterIsNotAnotherSystem()
        {
            avatar.Descriptor.enableEyeLook = true;
            avatar.Descriptor.customEyeLookSettings.eyelidType = VRCAvatarDescriptor.EyelidType.Blendshapes;
            avatar.Descriptor.customEyeLookSettings.eyelidsSkinnedMesh = avatar.Body;
            avatar.Descriptor.customEyeLookSettings.eyelidsBlendshapes = new[] { avatar.Body.sharedMesh.GetBlendShapeIndex("Blush"), -1, -1 };
            var blink = FollowDistanceTrigger("Blink", avatar.Hat.transform);
            blink.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });

            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var virtualFx);
            var existing = ParametersWriter.ExistingParameters(avatar.Descriptor.expressionParameters, virtualFx);
            var plan = ParameterPlanner.Plan(tree, existing);
            var touch = TouchBuilder.Describe(tree, existing, plan);
            var baseState = BaseState.Capture(tree);
            var pins = new PinLayerState();
            L.Report(TouchBuilder.BuildFollowDistanceLayers(asc, tree, touch, baseState, avatar.Descriptor, pins.Names, pins));
            L.Report(TouchBuilder.BuildTrackingPauseLayers(asc, tree.AvatarRoot, pins.Names, pins));

            // VRCFury rewrote AvatarDoc's own tracking control into a parameter driver (not simulated here, since
            // that needs no C# reference to any VRCFury type) and applies the merged result from its own layer,
            // named this by TrackingConflictResolverService, behind the "[VF<n>] " prefix VRCFury gives every layer it creates.
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx);
            var actor = fx.AddLayer(LayerPriority.Default, "[VF12] VRCF Tracking Control Actor");
            var actorState = actor.StateMachine.AddState("Applying", VirtualClip.Create("Applying"));
            var control = ScriptableObject.CreateInstance<VRCAnimatorTrackingControl>();
            control.trackingEyes = VRC_AnimatorTrackingControl.TrackingType.Animation;
            actorState.Behaviours = ImmutableList.Create<StateMachineBehaviour>(control);

            var messages = TouchBuilder.RemoveForeignOwnedPauseLayers(asc, pins);
            context.DeactivateAllExtensionContexts();

            Assert.That(messages, Is.Empty, "VRCFury's own arbitration layer already carries AvatarDoc's request; it is not another system to step aside for");
            Assert.That(FxOf(avatar).layers.Any(l => l.name == "AD: Blink pause"), Is.True, "AvatarDoc's own pause layer stays, feeding VRCFury's merge");
            Assert.That(pins.Names.Contains("AD: Blink pause"), Is.True);
        }

        [Test]
        public void TheTrackingControlOwnerIsReadAgainAfterAnInvalidate()
        {
            avatar.Fx.AddLayer("Sleep");
            var sleep = avatar.Fx.layers[avatar.Fx.layers.Length - 1].stateMachine;
            var asleep = sleep.AddState("Asleep");
            var control = asleep.AddStateMachineBehaviour<VRCAnimatorTrackingControl>();
            try
            {
                control.trackingEyes = VRC_AnimatorTrackingControl.TrackingType.Tracking;
                Assert.That(TouchBuilder.ExistingTrackingControlOwners(avatar.Fx).eyesLayer, Is.Null, "nothing sets Eyes to Animation yet");

                // The creator sets the control to Animation for Eyes: that edits the behaviour, not the controller, so
                // the controller's own dirty count, which keys the cache, does not move.
                int dirty = EditorUtility.GetDirtyCount(avatar.Fx);
                control.trackingEyes = VRC_AnimatorTrackingControl.TrackingType.Animation;
                Assert.That(EditorUtility.GetDirtyCount(avatar.Fx), Is.EqualTo(dirty), "only a dropped cache can show this edit");
                AvatarPlanCache.Invalidate();

                Assert.That(TouchBuilder.ExistingTrackingControlOwners(avatar.Fx).eyesLayer, Is.EqualTo("Sleep"),
                    "a coarse invalidation drops the tracking control cache with the other controller caches, so \"What this builds\" names the layer now holding Eyes");
            }
            finally
            {
                // The Undo records Unity's animator calls left for what this test made, before TearDown destroys them.
                foreach (var made in new Object[] { control, asleep, sleep, avatar.Fx })
                    if (made != null) Undo.ClearUndo(made);
            }
        }

        [Test]
        public void EyelidDetectionRequiresEnableEyeLook()
        {
            avatar.Descriptor.enableEyeLook = false;
            avatar.Descriptor.customEyeLookSettings.eyelidType = VRCAvatarDescriptor.EyelidType.Blendshapes;
            avatar.Descriptor.customEyeLookSettings.eyelidsSkinnedMesh = avatar.Body;
            avatar.Descriptor.customEyeLookSettings.eyelidsBlendshapes = new[] { avatar.Body.sharedMesh.GetBlendShapeIndex("Blush"), -1, -1 };
            var entry = FollowDistanceTrigger("Blink", avatar.Hat.transform);
            entry.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });

            TouchBuilder.FollowDistanceTracking(entry, avatar.Descriptor, out bool eyes, out bool mouth);

            Assert.That(eyes, Is.False, "eye look is off, so the shape does not count as an eyelid here");
            Assert.That(mouth, Is.False);
        }

        [Test]
        public void VisemeDetectionRequiresVisemeBlendShapeLipSync()
        {
            avatar.Descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.Default;
            avatar.Descriptor.VisemeSkinnedMesh = avatar.Body;
            avatar.Descriptor.VisemeBlendShapes = new string[15];
            avatar.Descriptor.VisemeBlendShapes[0] = "Blush";
            var entry = FollowDistanceTrigger("Boop", avatar.Hat.transform);
            entry.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });

            TouchBuilder.FollowDistanceTracking(entry, avatar.Descriptor, out bool eyes, out bool mouth);

            Assert.That(mouth, Is.False, "lip sync is not set to read blend shapes, so this is not counted as a viseme");
        }

        [Test]
        public void JawFlapBlendShapeCountsAsLipSync()
        {
            avatar.Descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.JawFlapBlendShape;
            avatar.Descriptor.VisemeSkinnedMesh = avatar.Body;
            avatar.Descriptor.MouthOpenBlendShapeName = "Blush";
            var entry = FollowDistanceTrigger("Boop", avatar.Hat.transform);
            entry.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });

            TouchBuilder.FollowDistanceTracking(entry, avatar.Descriptor, out bool eyes, out bool mouth);

            Assert.That(mouth, Is.True, "the jaw flap shape is the one lip sync itself reads");
            Assert.That(eyes, Is.False);
        }

        [Test]
        public void PinModeShapeCountsForTracking()
        {
            avatar.Descriptor.enableEyeLook = true;
            avatar.Descriptor.customEyeLookSettings.eyelidType = VRCAvatarDescriptor.EyelidType.Blendshapes;
            avatar.Descriptor.customEyeLookSettings.eyelidsSkinnedMesh = avatar.Body;
            avatar.Descriptor.customEyeLookSettings.eyelidsBlendshapes = new[] { avatar.Body.sharedMesh.GetBlendShapeIndex("Blush"), -1, -1 };
            var entry = FollowDistanceTrigger("Blink", avatar.Hat.transform);
            entry.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });

            TouchBuilder.FollowDistanceTracking(entry, avatar.Descriptor, out bool eyes, out bool mouth);

            Assert.That(eyes, Is.True, "Follow distance has no separate pin layer, so a Pin mode shape blends and pauses tracking the same as a Fixed one");
        }

        [Test]
        public void FollowDistanceNeverClaimsAFollowsConstraint()
        {
            var trigger = FollowDistanceTrigger("Pat", avatar.Hat.transform);
            trigger.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            trigger.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform });

            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
            var (followPlan, _) = FollowBuilder.Build(new BuildContext(avatar.Root, null), tree);

            Assert.That(followPlan.TryGetSource(trigger, avatar.Glasses, out _, out _), Is.False, "Follow distance never claims a constraint for a Follows target");
            Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.follow_distance_no_follows" && m.EntryIds.Contains(trigger.id) && m.Severity == MessageSeverity.Warning), Is.True);
        }

        [Test]
        public void FollowDistanceIgnoredGroupsAreNeverTooComplex()
        {
            var trigger = menu.AddEntry(EntryKind.Trigger, "Pat");
            trigger.source = TriggerSource.Contact;
            trigger.touchMode = TouchMode.FollowDistance;
            trigger.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            var g0 = new ConditionGroup();
            g0.all.Add(new WhenCondition { kind = WhenConditionKind.Contact, contactAnchor = avatar.Hat.transform, contactTags = new List<string> { "Hand" } });
            trigger.when.Add(g0);
            // Four more groups of four Parameter conditions: 256 ways to leave, well past the 64 a layer may
            // hold (ConditionGroupTests.TooManyCombinationsIsAnError uses the same shape for an ordinary trigger).
            // Follow distance never expands these into transitions at all, so they must never be too complex.
            for (int g = 0; g < 4; g++)
            {
                var group = new ConditionGroup();
                for (int i = 0; i < 4; i++)
                    group.all.Add(new WhenCondition { kind = WhenConditionKind.Parameter, sourceParameter = "AFK", condition = ParameterCondition.IsTrue });
                trigger.when.Add(group);
            }

            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });

            Assert.That(tree.Messages.Any(m => m.Says("ad.error.trigger_too_complex") && m.EntryIds.Contains(trigger.id)), Is.False);
            Assert.That(tree.HasErrors, Is.False);
        }

    }
}
#endif
