#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.ScriptableObjects;
#if DOCME_MA
using nadena.dev.modular_avatar.core;
#endif

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// An entry with Only if used builds only when something besides AvatarDoc uses its parameter: a tool that makes
    /// a part only when the creator turns it on reports the part's parameter only then. A Modular Avatar Parameters
    /// component stands in for such a tool, as in OtherToolParametersTests.
    /// </summary>
    public class OnlyIfUsedTests
    {
        const string Parameter = "DocMe/SkinTouch/Heads";

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

        static MenuEntry OnlyIfUsed(AvatarDoc menu, string name = "Heads press")
        {
            var entry = menu.AddEntry(EntryKind.Toggle, name);
            entry.parameterName = Parameter;
            entry.onlyIfUsed = true;
            return entry;
        }

        GameObject Child(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(avatar.Root.transform, false);
            return go;
        }

        void Declare()
        {
            avatar.ExpressionParameters.parameters = avatar.ExpressionParameters.parameters
                .Concat(new[] { new VRCExpressionParameters.Parameter { name = Parameter, valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true } })
                .ToArray();
        }

        static ControlNode Control(AvatarPlan plan, MenuEntry entry) => plan.Tree.Controls.Single(c => c.Entry == entry);

        static bool Says(AvatarPlan plan, string key, MenuEntry entry) => plan.Messages.Any(m => m.Key == key && m.EntryIds.Contains(entry.id));

        /// <summary>Runs AvatarDoc's own build step only, not Modular Avatar's passes after it.</summary>
        static List<ErrorContext> RunAvatarDocBuild(TestAvatar a)
        {
            var context = new BuildContext(a.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            return errors;
        }

        [Test]
        public void AnEntryNothingElseUsesIsLeftOut()
        {
            var menu = avatar.AddMenu();
            var heads = OnlyIfUsed(menu);

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(Control(plan, heads).Unused, Is.True);
            Assert.That(Control(plan, heads).Excluded, Is.True, "the writers and the slot count skip it");
            Assert.That(plan.Plan.Parameters.Any(p => p.Name == Parameter), Is.False, "no parameter, so no synced bit");
            Assert.That(plan.AvatarDoctorBits, Is.Zero);
            Assert.That(Says(plan, "ad.info.left_out_unused", heads), Is.True);
            Assert.That(Says(plan, "ad.warn.parameter_control_unused", heads), Is.False, "the note says it instead of the warning");
        }

        [Test]
        public void WithoutTheSwitchTheEntryIsBuiltAsBefore()
        {
            var menu = avatar.AddMenu();
            var heads = OnlyIfUsed(menu);
            heads.onlyIfUsed = false;

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(Control(plan, heads).Unused, Is.False);
            Assert.That(plan.Plan.Parameters.Any(p => p.Name == Parameter), Is.True);
            Assert.That(Says(plan, "ad.warn.parameter_control_unused", heads), Is.True);
        }

        [Test]
        public void TheUploadLeavesItOut()
        {
            var menu = avatar.AddMenu();
            OnlyIfUsed(menu);
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });

            var errors = RunAvatarDocBuild(avatar);

            Assert.That(errors.Any(e => e.TheError is SimpleError s && s.Severity == ErrorSeverity.Error), Is.False);
            var controls = avatar.Descriptor.expressionsMenu.controls;
            Assert.That(controls.Any(c => c.name == "Heads press"), Is.False, "no menu control");
            Assert.That(controls.Any(c => c.name == "Hat"), Is.True, "the rest builds");
            Assert.That(avatar.Descriptor.expressionParameters.parameters.Any(p => p.name == Parameter), Is.False, "no declaration");
        }

        [Test]
        public void AParameterTheAvatarDeclaresCountsAsUsed()
        {
            Declare();
            var menu = avatar.AddMenu();
            var heads = OnlyIfUsed(menu);

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(Control(plan, heads).Unused, Is.False);
            Assert.That(plan.Plan.Parameters.Single(p => p.Name == Parameter).ReusesExisting, Is.True);
        }

        [Test]
        public void AnotherAvatarDocEntryOfTheSameNameIsNotAUse()
        {
            var menu = avatar.AddMenu();
            var heads = OnlyIfUsed(menu);
            var plain = menu.AddEntry(EntryKind.Toggle, "Heads too");
            plain.parameterName = Parameter;

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(Control(plan, heads).Unused, Is.True, "only names from outside AvatarDoc count");
            Assert.That(Control(plan, plain).Unused, Is.False);
            Assert.That(plan.Plan.Parameters.Single(p => p.Name == Parameter).Controls, Is.EqualTo(new[] { Control(plan, plain) }));
        }

        [Test]
        public void AGroupMemberIgnoresTheSwitch()
        {
            var menu = avatar.AddMenu();
            var member = OnlyIfUsed(menu);
            member.exclusiveGroup = "Kinds";
            menu.AddEntry(EntryKind.Toggle, "Other").exclusiveGroup = "Kinds";

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(Control(plan, member).Unused, Is.False, "a member uses the group's parameter, never its own name");
        }

        [Test]
        public void ARadialCanBeLeftOutToo()
        {
            var menu = avatar.AddMenu();
            var radial = menu.AddEntry(EntryKind.Radial, "Strength");
            radial.parameterName = Parameter;
            radial.onlyIfUsed = true;

            Assert.That(Control(AvatarPlanCache.GetFor(menu), radial).Unused, Is.True);
        }

        [Test]
        public void AvatarDocDoesNotReportALeftOutParameter()
        {
            var menu = avatar.AddMenu();
            OnlyIfUsed(menu);
            AvatarPlanCache.GetFor(menu);

            var reported = ParameterInfo.ForUI.GetParametersForObject(avatar.Root).Where(p => p.Source is AvatarDoc).Select(p => p.EffectiveName);
            Assert.That(reported, Does.Not.Contain(Parameter));
        }

        [Test]
        public void TheRowIsDimmedWithAChipAndTheSwitchShows()
        {
            var menu = avatar.AddMenu();
            var heads = OnlyIfUsed(menu);
            var view = new AvatarDoctorView(new SerializedObject(menu), menu, false, new MemoryViewPrefs());

            var row = view.Tree.RowFor(heads.id);
            Assert.That(row.Excluded, Is.True);
            Assert.That(row.PlatformChip, Is.EqualTo(L.Text("ad.ui.chip.unused")));

            view.Tree.SelectEntry(heads.id);
            var toggle = view.Detail.Q<Toggle>("ad-parameter-only-if-used");
            Assert.That(toggle, Is.Not.Null);
            Assert.That(toggle.value, Is.True);
            Assert.That(toggle.tooltip, Is.EqualTo(L.Text("ad.ui.parameter.only_if_used.tip")));
        }

        [Test]
        public void TheSwitchShowsOnlyWithACustomName()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = new AvatarDoctorView(new SerializedObject(menu), menu, false, new MemoryViewPrefs());

            view.Tree.SelectEntry(hat.id);

            Assert.That(view.Detail.Q<Toggle>("ad-parameter-only-if-used"), Is.Null, "an automatic name is AvatarDoc's own, so nothing else could use it");
        }

#if DOCME_MA
        static void AddOtherTool(GameObject on)
        {
            var parameters = on.AddComponent<ModularAvatarParameters>();
            parameters.parameters.Add(new ParameterConfig
            {
                nameOrPrefix = Parameter,
                syncType = ParameterSyncType.Bool,
                saved = true,
                defaultValue = 1f,
            });
        }

        [Test]
        public void AParameterAnotherToolReportsBuildsTheEntry()
        {
            AddOtherTool(Child("Skin Touch"));
            var menu = avatar.AddMenu();
            var heads = OnlyIfUsed(menu);

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(Control(plan, heads).Unused, Is.False);
            Assert.That(Says(plan, "ad.info.left_out_unused", heads), Is.False);
            Assert.That(plan.Plan.Parameters.Single(p => p.Name == Parameter).Synced, Is.True);
        }

        [Test]
        public void TheUploadBuildsTheEntryAnotherToolReports()
        {
            AddOtherTool(Child("Skin Touch"));
            var menu = avatar.AddMenu();
            OnlyIfUsed(menu);

            RunAvatarDocBuild(avatar);

            Assert.That(avatar.Descriptor.expressionsMenu.controls.Single(c => c.name == "Heads press").parameter.name, Is.EqualTo(Parameter));
            Assert.That(avatar.Descriptor.expressionParameters.parameters.Any(p => p.name == Parameter), Is.True);
        }
#endif
    }
}
#endif
