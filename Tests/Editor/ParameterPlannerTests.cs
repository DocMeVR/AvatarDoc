using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    public class ParameterPlannerTests
    {
        GameObject root;
        AvatarDoc menu;
        MenuTree tree;
        static readonly Dictionary<string, PlannedType> NoExisting = new Dictionary<string, PlannedType>();

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Avatar");
            menu = root.AddComponent<AvatarDoc>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
        }

        MenuEntry Toggle(string name, string parentId = "")
        {
            var entry = menu.AddEntry(EntryKind.Toggle, name, parentId);
            entry.objects.Add(new ObjectTarget { target = root });
            return entry;
        }

        MenuEntry Radial(string name)
        {
            var entry = menu.AddEntry(EntryKind.Radial, name);
            entry.blendShapes.Add(new BlendShapeTarget { shape = "x" });
            return entry;
        }

        ParameterPlan Plan(Dictionary<string, PlannedType> existing = null)
        {
            tree = MenuModel.Build(root.transform, null, new[] { menu });
            return ParameterPlanner.Plan(tree, existing ?? NoExisting);
        }

        ParameterPlan Plan(Dictionary<string, ExistingParameter> existing)
        {
            tree = MenuModel.Build(root.transform, null, new[] { menu });
            return ParameterPlanner.Plan(tree, existing);
        }

        static ControlBinding BindingOf(ParameterPlan plan, MenuTree tree, MenuEntry entry) => plan.Bindings[tree.Controls.Single(c => c.Entry == entry)];

        [Test]
        public void ToggleGetsPrefixedBoolParameter()
        {
            var casual = Toggle("Casual");
            casual.defaultOn = true;
            var plan = Plan();

            var p = plan.Parameters.Single();
            Assert.That(p.Name, Is.EqualTo("AD/Casual"));
            Assert.That(p.Type, Is.EqualTo(PlannedType.Bool));
            Assert.That(p.Default, Is.EqualTo(1f));
            Assert.That(p.Saved, Is.True);
            Assert.That(p.Synced, Is.True);
            Assert.That(p.Bits, Is.EqualTo(1));
            Assert.That(BindingOf(plan, tree, casual).Value, Is.EqualTo(1f));
            Assert.That(BindingOf(plan, tree, casual).OnByDefault, Is.True);
            Assert.That(plan.NewSyncedBits, Is.EqualTo(1));
        }

        [Test]
        public void AutoNamesAreDeduplicatedInTreeOrder()
        {
            Toggle("Hat");
            var sub = menu.AddEntry(EntryKind.Submenu, "Sub");
            Toggle("Hat", sub.id);
            Toggle("Hat");
            var plan = Plan(new Dictionary<string, PlannedType> { { "AD/Hat 3", PlannedType.Bool } });

            Assert.That(plan.Parameters.Select(p => p.Name), Is.EqualTo(new[] { "AD/Hat", "AD/Hat 2", "AD/Hat 4" }));
        }

        [Test]
        public void UnsyncedToggleCostsNoBits()
        {
            Toggle("Local").synced = false;
            var plan = Plan();
            Assert.That(plan.Parameters.Single().Bits, Is.EqualTo(0));
            Assert.That(plan.NewSyncedBits, Is.EqualTo(0));
        }

        [Test]
        public void RadialGetsFloatWithDefault()
        {
            var eyes = Radial("Eye Size");
            eyes.defaultValue = 0.25f;
            var plan = Plan();
            var p = plan.Parameters.Single();
            Assert.That(p.Name, Is.EqualTo("AD/Eye Size"));
            Assert.That(p.Type, Is.EqualTo(PlannedType.Float));
            Assert.That(p.Default, Is.EqualTo(0.25f));
            Assert.That(p.Bits, Is.EqualTo(8));
        }

        [Test]
        public void GroupSharesOneIntParameter()
        {
            menu.groups.Add(new ExclusiveGroup { name = "Outfits", saved = false, synced = true });
            var a = Toggle("Casual"); a.exclusiveGroup = "Outfits";
            var b = Toggle("Suit"); b.exclusiveGroup = "Outfits"; b.defaultOn = true;
            var c = Toggle("Swim"); c.exclusiveGroup = "Outfits";
            var plan = Plan();

            var p = plan.Parameters.Single();
            Assert.That(p.Name, Is.EqualTo("AD/Outfits"));
            Assert.That(p.Type, Is.EqualTo(PlannedType.Int));
            Assert.That(p.GroupName, Is.EqualTo("Outfits"));
            Assert.That(p.Saved, Is.False);
            Assert.That(p.Controls.Select(x => x.Entry.name), Is.EqualTo(new[] { "Casual", "Suit", "Swim" }));
            Assert.That(BindingOf(plan, tree, a).Value, Is.EqualTo(1f));
            Assert.That(BindingOf(plan, tree, b).Value, Is.EqualTo(2f));
            Assert.That(BindingOf(plan, tree, c).Value, Is.EqualTo(3f));
            Assert.That(p.Default, Is.EqualTo(2f));
            Assert.That(BindingOf(plan, tree, b).OnByDefault, Is.True);
            Assert.That(BindingOf(plan, tree, a).OnByDefault, Is.False);
            Assert.That(p.Bits, Is.EqualTo(8));
        }

        [Test]
        public void ExplicitGroupValuesAreKeptAndDuplicatesAreErrors()
        {
            var a = Toggle("A"); a.exclusiveGroup = "G"; a.groupValue = 2;
            var b = Toggle("B"); b.exclusiveGroup = "G";
            var c = Toggle("C"); c.exclusiveGroup = "G"; c.groupValue = 2;
            var plan = Plan();

            Assert.That(BindingOf(plan, tree, a).Value, Is.EqualTo(2f));
            Assert.That(BindingOf(plan, tree, b).Value, Is.EqualTo(1f));
            Assert.That(BindingOf(plan, tree, c).Value, Is.EqualTo(3f));
            Assert.That(plan.Messages.Any(m => m.Key == "ad.error.group_value_conflict"), Is.True);
            Assert.That(plan.HasErrors, Is.True);
        }

        [Test]
        public void MultipleGroupDefaultsWarnAndFirstWins()
        {
            var a = Toggle("A"); a.exclusiveGroup = "G"; a.defaultOn = true;
            var b = Toggle("B"); b.exclusiveGroup = "G"; b.defaultOn = true;
            var plan = Plan();

            Assert.That(plan.Parameters.Single().Default, Is.EqualTo(1f));
            Assert.That(BindingOf(plan, tree, b).OnByDefault, Is.False);
            Assert.That(plan.Messages.Any(m => m.Key == "ad.warn.group_multiple_defaults"), Is.True);
            Assert.That(plan.HasErrors, Is.False);
        }

        [Test]
        public void CustomNameReusesExistingParameterOfCompatibleType()
        {
            var t = Toggle("Mode"); t.parameterName = "ExistingInt"; t.parameterValue = 3;
            var plan = Plan(new Dictionary<string, PlannedType> { { "ExistingInt", PlannedType.Int } });

            var p = plan.Parameters.Single();
            Assert.That(p.Name, Is.EqualTo("ExistingInt"));
            Assert.That(p.Type, Is.EqualTo(PlannedType.Int));
            Assert.That(p.ReusesExisting, Is.True);
            Assert.That(BindingOf(plan, tree, t).Value, Is.EqualTo(3f));
            Assert.That(plan.NewSyncedBits, Is.EqualTo(0));
        }

        [Test]
        public void ReusedParameterDeclarationWins()
        {
            var t = Toggle("Mode");
            t.parameterName = "Mode";
            t.parameterValue = 2;
            t.saved = true;
            t.synced = true;
            t.defaultOn = false;
            var plan = Plan(new Dictionary<string, ExistingParameter>
            {
                { "Mode", new ExistingParameter { Type = PlannedType.Int, IsExpressionParameter = true, Saved = false, Synced = false, Default = 2f } },
            });

            var p = plan.Parameters.Single();
            Assert.That(p.Saved, Is.False);
            Assert.That(p.Synced, Is.False);
            Assert.That(p.Default, Is.EqualTo(2f));
            Assert.That(p.Bits, Is.EqualTo(0));
            Assert.That(BindingOf(plan, tree, t).OnByDefault, Is.True);
            Assert.That(plan.Messages.Any(m => m.Key == "ad.info.parameter_reused"), Is.True);
        }

        [Test]
        public void NewCustomNameTypeFollowsValue()
        {
            Toggle("A").parameterName = "CustomBool";
            var i = Toggle("B"); i.parameterName = "CustomInt"; i.parameterValue = 4;
            var f = Toggle("C"); f.parameterName = "CustomFloat"; f.parameterValue = 0.5f;
            var plan = Plan();

            Assert.That(plan.Parameters.Select(p => p.Type), Is.EqualTo(new[] { PlannedType.Bool, PlannedType.Int, PlannedType.Float }));
        }

        [Test]
        public void SharedCustomNameWidensToInt()
        {
            var a = Toggle("A"); a.parameterName = "Shared";
            var b = Toggle("B"); b.parameterName = "Shared"; b.parameterValue = 2;
            var plan = Plan();

            var p = plan.Parameters.Single();
            Assert.That(p.Type, Is.EqualTo(PlannedType.Int));
            Assert.That(BindingOf(plan, tree, a).Value, Is.EqualTo(1f));
            Assert.That(BindingOf(plan, tree, b).Value, Is.EqualTo(2f));
            Assert.That(plan.HasErrors, Is.False);
        }

        [Test]
        public void AnimatorOnlyParameterGetsAnInfoMessage()
        {
            var t = Toggle("Mode");
            t.parameterName = "FaceTracking";
            var plan = Plan(new Dictionary<string, ExistingParameter>
            {
                { "FaceTracking", new ExistingParameter { Type = PlannedType.Float, IsExpressionParameter = false } },
            });

            var p = plan.Parameters.Single();
            Assert.That(p.IsAnimatorOnly, Is.True);
            Assert.That(plan.NewSyncedBits, Is.EqualTo(0));
            var message = plan.Messages.Single(m => m.Key == "ad.info.parameter_animator_only");
            Assert.That(message.EntryIds, Does.Contain(t.id));
        }

        [Test]
        public void ValueOutsideReusedTypeIsError()
        {
            var t = Toggle("Flag"); t.parameterName = "Flag"; t.parameterValue = 3;
            var plan = Plan(new Dictionary<string, ExistingParameter>
            {
                { "Flag", new ExistingParameter { Type = PlannedType.Bool, IsExpressionParameter = true, Saved = true, Synced = true } },
            });

            Assert.That(plan.Messages.Any(m => m.Key == "ad.error.parameter_type_conflict"), Is.True);
            Assert.That(plan.HasErrors, Is.True);
        }

        [Test]
        public void RadialOnNonFloatParameterIsError()
        {
            Radial("R").parameterName = "Existing";
            var plan = Plan(new Dictionary<string, PlannedType> { { "Existing", PlannedType.Bool } });
            Assert.That(plan.Messages.Any(m => m.Key == "ad.error.parameter_type_conflict"), Is.True);
            Assert.That(plan.HasErrors, Is.True);
        }

        [Test]
        public void TwoControlsSharingACustomNameShareTheParameter()
        {
            var a = Toggle("A"); a.parameterName = "Shared"; a.saved = false;
            var b = Toggle("B"); b.parameterName = "Shared"; b.synced = false;
            var plan = Plan();

            var p = plan.Parameters.Single();
            Assert.That(p.Controls.Count, Is.EqualTo(2));
            Assert.That(p.Saved, Is.True);
            Assert.That(p.Synced, Is.True);
        }

        [Test]
        public void SanitizeTrimsAndReplacesSlashes()
        {
            Assert.That(ParameterPlanner.Sanitize("  Cat / Ears "), Is.EqualTo("Cat _ Ears"));
            Assert.That(ParameterPlanner.Sanitize(""), Is.EqualTo("Unnamed"));
        }

        [Test]
        public void ToggleSharingAParameterWithARadialIsError()
        {
            Radial("R").parameterName = "Shared";
            Toggle("T").parameterName = "Shared";
            var plan = Plan();

            Assert.That(plan.Messages.Any(m => m.Key == "ad.error.parameter_type_conflict"), Is.True);
            Assert.That(plan.HasErrors, Is.True);
            Assert.That(plan.Parameters.Single().Controls.Count, Is.EqualTo(1));
        }
    }
}
