using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    public class TriggerPlannerTests
    {
        TestAvatar avatar;
        AvatarDoc menu;
        static readonly Dictionary<string, ExistingParameter> NoExisting = new Dictionary<string, ExistingParameter>();

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

        MenuEntry Toggle(string name)
        {
            var entry = menu.AddEntry(EntryKind.Toggle, name);
            entry.objects.Add(new ObjectTarget { target = avatar.Hat });
            return entry;
        }

        MenuEntry Trigger(string name)
        {
            var entry = menu.AddEntry(EntryKind.Trigger, name);
            entry.objects.Add(new ObjectTarget { target = avatar.Glasses });
            return entry;
        }

        (List<TriggerPlan> plans, List<ModelMessage> messages) Plan(Dictionary<string, ExistingParameter> existing = null)
        {
            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });
            var plan = ParameterPlanner.Plan(tree, existing ?? NoExisting);
            return TriggerPlanner.Plan(tree, plan, existing ?? NoExisting);
        }

        static AnimatorCondition C(List<List<AnimatorCondition>> transitions, int t, int i) => transitions[t][i];

        [Test]
        public void ToggleSourceUsesTheTogglesParameter()
        {
            var hat = Toggle("Hat");
            var trigger = Trigger("Ears");
            trigger.source = TriggerSource.Toggle;
            trigger.sourceToggleId = hat.id;

            var (plans, messages) = Plan();

            var p = plans.Single();
            Assert.That(p.Kind, Is.EqualTo(TriggerKind.Condition));
            Assert.That(p.Condition.Parameter, Is.EqualTo("AD/Hat"));
            Assert.That(p.Condition.Type, Is.EqualTo(PlannedType.Bool), "the toggle's own parameter carries the toggle's type");
            Assert.That(C(p.Condition.OnTransitions, 0, 0).mode, Is.EqualTo(AnimatorConditionMode.If));
            Assert.That(C(p.Condition.OffTransitions, 0, 0).mode, Is.EqualTo(AnimatorConditionMode.IfNot));
            Assert.That(messages, Is.Empty);
        }

        [Test]
        public void SourceToggleInAnotherComponentResolves()
        {
            var hat = Toggle("Hat");
            var pack = new GameObject("Pack");
            pack.transform.SetParent(avatar.Root.transform);
            var packMenu = pack.AddComponent<AvatarDoc>();
            var trigger = packMenu.AddEntry(EntryKind.Trigger, "Ears");
            trigger.objects.Add(new ObjectTarget { target = avatar.Glasses });
            trigger.source = TriggerSource.Toggle;
            trigger.sourceToggleId = hat.id;

            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu, packMenu });
            var plan = ParameterPlanner.Plan(tree, NoExisting);
            var (plans, messages) = TriggerPlanner.Plan(tree, plan, NoExisting);

            var p = plans.Single();
            Assert.That(p.Kind, Is.EqualTo(TriggerKind.Condition));
            Assert.That(p.Condition.Parameter, Is.EqualTo("AD/Hat"));
            Assert.That(p.Component, Is.SameAs(packMenu));
            Assert.That(messages.Any(m => m.Key == "ad.warn.trigger_source_missing"), Is.False);
        }

        [Test]
        public void ToggleSourceOffSwapsTheConditions()
        {
            var hat = Toggle("Hat");
            var trigger = Trigger("Ears");
            trigger.source = TriggerSource.Toggle;
            trigger.sourceToggleId = hat.id;
            trigger.sourceToggleOn = false;

            var (plans, _) = Plan();

            Assert.That(C(plans.Single().Condition.OnTransitions, 0, 0).mode, Is.EqualTo(AnimatorConditionMode.IfNot));
        }

        [Test]
        public void GroupMemberSourceUsesEqualsOnTheGroupValue()
        {
            var a = Toggle("A"); a.exclusiveGroup = "G";
            var b = Toggle("B"); b.exclusiveGroup = "G";
            var trigger = Trigger("T");
            trigger.source = TriggerSource.Toggle;
            trigger.sourceToggleId = b.id;

            var (plans, _) = Plan();

            var c = plans.Single().Condition;
            Assert.That(c.Parameter, Is.EqualTo("AD/G"));
            Assert.That(C(c.OnTransitions, 0, 0).mode, Is.EqualTo(AnimatorConditionMode.Equals));
            Assert.That(C(c.OnTransitions, 0, 0).threshold, Is.EqualTo(2f));
            Assert.That(C(c.OffTransitions, 0, 0).mode, Is.EqualTo(AnimatorConditionMode.NotEqual));
        }

        [Test]
        public void MissingToggleSourceIsReported()
        {
            var trigger = Trigger("T");
            trigger.source = TriggerSource.Toggle;
            trigger.sourceToggleId = "nope";

            var (plans, messages) = Plan();

            Assert.That(plans, Is.Empty);
            Assert.That(messages.Single().Key, Is.EqualTo("ad.warn.trigger_source_missing"));
        }

        [Test]
        public void ParameterSourceTakesTheExistingType()
        {
            var trigger = Trigger("T");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "Mode";
            trigger.condition = ParameterCondition.Equals;
            trigger.conditionValue = 3;
            var existing = new Dictionary<string, ExistingParameter> { { "Mode", new ExistingParameter { Type = PlannedType.Int, IsExpressionParameter = true } } };

            var (plans, messages) = Plan(existing);

            var c = plans.Single().Condition;
            Assert.That(c.Type, Is.EqualTo(PlannedType.Int), "taken from the existing declaration");
            Assert.That(C(c.OnTransitions, 0, 0).mode, Is.EqualTo(AnimatorConditionMode.Equals));
            Assert.That(C(c.OnTransitions, 0, 0).threshold, Is.EqualTo(3f));
            Assert.That(C(c.OffTransitions, 0, 0).mode, Is.EqualTo(AnimatorConditionMode.NotEqual));
            Assert.That(messages, Is.Empty);
        }

        [Test]
        public void BuiltInParameterIsTypedFromTheTable()
        {
            var trigger = Trigger("T");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "GestureRight";
            trigger.condition = ParameterCondition.Equals;
            trigger.conditionValue = 5;

            var (plans, messages) = Plan();

            var c = plans.Single().Condition;
            Assert.That(c.Type, Is.EqualTo(PlannedType.Int), "taken from the built-in table");
            Assert.That(messages, Is.Empty);
            Assert.That(TriggerPlanner.BuiltInType("Voice"), Is.EqualTo(PlannedType.Float));
            Assert.That(TriggerPlanner.BuiltInType("AFK"), Is.EqualTo(PlannedType.Bool));
            Assert.That(TriggerPlanner.BuiltInType("nothing"), Is.Null);
        }

        [Test]
        public void UnknownParameterFallsBackToFloatWithAWarning()
        {
            var trigger = Trigger("T");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "Custom/Thing";
            trigger.condition = ParameterCondition.Greater;
            trigger.conditionValue = 0.5f;

            var (plans, messages) = Plan();

            var c = plans.Single().Condition;
            Assert.That(c.Type, Is.EqualTo(PlannedType.Float), "the fallback type");
            Assert.That(C(c.OnTransitions, 0, 0).mode, Is.EqualTo(AnimatorConditionMode.Greater));
            Assert.That(C(c.OffTransitions, 0, 0).mode, Is.EqualTo(AnimatorConditionMode.Less));
            Assert.That(messages.Single().Key, Is.EqualTo("ad.warn.parameter_type_unknown"));
            Assert.That(messages.Single().EntryIds, Does.Contain(trigger.id));
        }

        [Test]
        public void ContactReceiverParameterIsTypedFromNdmf()
        {
            var contact = new GameObject("Contact");
            contact.transform.SetParent(avatar.Root.transform);
            var receiver = contact.AddComponent<VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver>();
            receiver.parameter = "Touch";
            receiver.receiverType = VRC.Dynamics.ContactReceiver.ReceiverType.Proximity;
            var trigger = Trigger("T");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "Touch";
            trigger.condition = ParameterCondition.Greater;
            trigger.conditionValue = 0.5f;
            var existing = ParametersWriter.ExistingParameters(avatar.Descriptor.expressionParameters, (nadena.dev.ndmf.animator.VirtualAnimatorController)null);
            ParametersWriter.AddProvided(existing, ParameterInfo.ForUI.GetParametersForObject(avatar.Root));

            var (plans, messages) = Plan(existing);

            Assert.That(existing.ContainsKey("Touch"), Is.True, "NDMF reported the contact parameter");
            Assert.That(plans.Single().Condition.Type, Is.EqualTo(PlannedType.Float));
            Assert.That(messages.Any(m => m.Key == "ad.warn.parameter_type_unknown"), Is.False);
        }

        [Test]
        public void PhysBonePrefixExpandsToTypedParameters()
        {
            var bone = new GameObject("Tail").AddComponent<VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone>();
            bone.transform.SetParent(avatar.Root.transform);
            bone.parameter = "Tail";
            var existing = ParametersWriter.ExistingParameters(avatar.ExpressionParameters, (nadena.dev.ndmf.animator.VirtualAnimatorController)null);
            ParametersWriter.AddProvided(existing, ParameterInfo.ForUI.GetParametersForObject(avatar.Root));

            Assert.That(existing["Tail_IsGrabbed"].Type, Is.EqualTo(PlannedType.Bool));
            Assert.That(existing["Tail_Angle"].Type, Is.EqualTo(PlannedType.Float));
            Assert.That(existing["Tail_Stretch"].Type, Is.EqualTo(PlannedType.Float));
            Assert.That(existing.ContainsKey("Tail"), Is.False);
        }

        [Test]
        public void FloatEqualsUsesAWindowAndTwoOffTransitions()
        {
            var trigger = Trigger("T");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "Voice";
            trigger.condition = ParameterCondition.Equals;
            trigger.conditionValue = 0.5f;

            var (plans, _) = Plan();

            var c = plans.Single().Condition;
            Assert.That(c.OnTransitions.Count, Is.EqualTo(1));
            Assert.That(c.OnTransitions[0].Select(x => x.mode), Is.EqualTo(new[] { AnimatorConditionMode.Greater, AnimatorConditionMode.Less }));
            Assert.That(c.OnTransitions[0][0].threshold, Is.EqualTo(0.495f).Within(1e-4f));
            Assert.That(c.OffTransitions.Count, Is.EqualTo(2));
        }

        [Test]
        public void IntGreaterOffUsesLessOfValuePlusOne()
        {
            var trigger = Trigger("T");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "GestureLeft";
            trigger.condition = ParameterCondition.Greater;
            trigger.conditionValue = 2;

            var (plans, _) = Plan();

            var c = plans.Single().Condition;
            Assert.That(C(c.OnTransitions, 0, 0).mode, Is.EqualTo(AnimatorConditionMode.Greater));
            Assert.That(C(c.OffTransitions, 0, 0).mode, Is.EqualTo(AnimatorConditionMode.Less));
            Assert.That(C(c.OffTransitions, 0, 0).threshold, Is.EqualTo(3f));
        }

        [Test]
        public void BoolIsTrueOnAnIntUsesNotEqualZero()
        {
            var trigger = Trigger("T");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "Viseme";
            trigger.condition = ParameterCondition.IsTrue;

            var (plans, _) = Plan();

            var c = plans.Single().Condition;
            Assert.That(C(c.OnTransitions, 0, 0).mode, Is.EqualTo(AnimatorConditionMode.NotEqual));
            Assert.That(C(c.OffTransitions, 0, 0).mode, Is.EqualTo(AnimatorConditionMode.Equals));
        }

        [Test]
        public void BlendShapeSourceBecomesALink()
        {
            var trigger = Trigger("Blink fix");
            trigger.source = TriggerSource.BlendShape;
            trigger.sourceRenderer = avatar.Body;
            trigger.sourceShape = "Blush";
            trigger.sourceThreshold = 30f;
            trigger.linkMode = BlendShapeLinkMode.Replace;

            var (plans, messages) = Plan();

            var p = plans.Single();
            Assert.That(p.Kind, Is.EqualTo(TriggerKind.Link));
            Assert.That(p.SourceRenderer, Is.SameAs(avatar.Body));
            Assert.That(p.SourceShape, Is.EqualTo("Blush"));
            Assert.That(p.Threshold, Is.EqualTo(30f));
            Assert.That(p.Replace, Is.True);
            Assert.That(messages, Is.Empty);
        }

        [Test]
        public void MissingBlendShapeSourceIsReported()
        {
            var trigger = Trigger("T");
            trigger.source = TriggerSource.BlendShape;
            trigger.sourceRenderer = avatar.Body;
            trigger.sourceShape = "Nope";

            var (plans, messages) = Plan();

            Assert.That(plans, Is.Empty);
            Assert.That(messages.Single().Key, Is.EqualTo("ad.warn.trigger_source_missing"));
        }

        [Test]
        public void SelfReferencingEffectWarnsOnce()
        {
            var trigger = Trigger("T");
            trigger.source = TriggerSource.BlendShape;
            trigger.sourceRenderer = avatar.Body;
            trigger.sourceShape = "Blush";
            trigger.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", mode = ShapeEffectMode.Follow, multiplier = 1f });
            trigger.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", mode = ShapeEffectMode.Fixed, weight = 50f });

            var (plans, messages) = Plan();

            Assert.That(plans.Single().Kind, Is.EqualTo(TriggerKind.Link));
            var message = messages.Single();
            Assert.That(message.Key, Is.EqualTo("ad.warn.link_effect_is_source"));
            Assert.That(message.EntryIds, Does.Contain(trigger.id));
        }

        [Test]
        public void TriggersWithoutEffectsAreSkipped()
        {
            var trigger = menu.AddEntry(EntryKind.Trigger, "Empty");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "AFK";

            var (plans, messages) = Plan();

            Assert.That(plans, Is.Empty);
            Assert.That(messages, Is.Empty, "the model already reports no_effects");
        }
    }
}
