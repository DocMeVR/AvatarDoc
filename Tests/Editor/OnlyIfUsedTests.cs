#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
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
        public void AnAnimatorTheComponentMergesCountsAsAUse()
        {
            var controller = new UnityEditor.Animations.AnimatorController();
            controller.AddParameter(Parameter, AnimatorControllerParameterType.Bool);
            try
            {
                var menu = avatar.AddMenu();
                menu.animators.Add(AnimatorMerge.Create(controller));
                var heads = OnlyIfUsed(menu);

                Assert.That(Control(AvatarPlanCache.GetFor(menu), heads).Unused, Is.False, "the creator's own animator reads it");
            }
            finally
            {
                Object.DestroyImmediate(controller);
            }
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

        MenuEntry Trigger(AvatarDoc menu, params WhenCondition[][] groups)
        {
            var trigger = menu.AddEntry(EntryKind.Trigger, "Ears");
            trigger.objects.Add(new ObjectTarget { target = avatar.Glasses });
            trigger.when = groups.Select(g => new ConditionGroup { all = g.ToList() }).ToList();
            return trigger;
        }

        static WhenCondition OnToggle(MenuEntry toggle, bool on) => new WhenCondition { kind = WhenConditionKind.Toggle, sourceToggleId = toggle.id, sourceToggleOn = on };

        static WhenCondition Afk() => new WhenCondition { kind = WhenConditionKind.Parameter, sourceParameter = "AFK", condition = ParameterCondition.IsTrue };

        static TriggerPlan Planned(AvatarPlan plan, MenuEntry trigger) => plan.Triggers.SingleOrDefault(t => t.Node.Entry == trigger);

        [Test]
        public void AGateThatNeedsALeftOutToggleOnLeavesTheTriggerOut()
        {
            var menu = avatar.AddMenu();
            var heads = OnlyIfUsed(menu);
            var trigger = Trigger(menu, new[] { Afk() });
            trigger.gateToggleId = heads.id;
            trigger.gateToggleOn = true;

            var plan = AvatarPlanCache.GetFor(menu);

            var node = plan.Tree.Triggers.Single(t => t.Entry == trigger);
            Assert.That(node.LeftOutBy, Is.SameAs(Control(plan, heads)));
            Assert.That(node.Excluded, Is.True);
            Assert.That(Planned(plan, trigger), Is.Null, "a gate that never opens would never let it on");
            Assert.That(Says(plan, "ad.info.trigger_left_out_unused", trigger), Is.True);
            var view = new AvatarDoctorView(new SerializedObject(menu), menu, false, new MemoryViewPrefs());
            var row = view.Tree.RowFor(trigger.id);
            Assert.That(row.PlatformChip, Is.EqualTo(L.Text("ad.ui.chip.unused")));
            Assert.That(WhatThisBuildsModel.Describe(view.Plan, row), Is.EqualTo(string.Format(L.Text("ad.ui.builds.nothing.unused_toggle"), "Heads press")));
        }

        [Test]
        public void AGateThatNeedsItOffIsAlwaysOpen()
        {
            var menu = avatar.AddMenu();
            var heads = OnlyIfUsed(menu);
            var trigger = Trigger(menu, new[] { Afk() });
            trigger.gateToggleId = heads.id;
            trigger.gateToggleOn = false;

            var planned = Planned(AvatarPlanCache.GetFor(menu), trigger);

            Assert.That(planned, Is.Not.Null);
            Assert.That(planned.Gate, Is.Empty);
            Assert.That(planned.Conditions.Select(c => c.Parameter), Is.EqualTo(new[] { "AFK" }));
        }

        [Test]
        public void AGroupThatNeedsALeftOutToggleOnIsRuledOut()
        {
            var menu = avatar.AddMenu();
            var heads = OnlyIfUsed(menu);
            var trigger = Trigger(menu, new[] { OnToggle(heads, true) }, new[] { Afk() });

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(Planned(plan, trigger).Conditions.Select(c => c.Parameter), Is.EqualTo(new[] { "AFK" }), "the other group still works");
            Assert.That(Says(plan, "ad.warn.trigger_source_missing", trigger), Is.False);
        }

        [Test]
        public void AConditionThatNeedsALeftOutToggleOffIsAlwaysMet()
        {
            var menu = avatar.AddMenu();
            var heads = OnlyIfUsed(menu);
            var trigger = Trigger(menu, new[] { OnToggle(heads, false), Afk() });

            var planned = Planned(AvatarPlanCache.GetFor(menu), trigger);

            Assert.That(planned.Conditions.Select(c => c.Parameter), Is.EqualTo(new[] { "AFK" }));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ATriggerThatOnlyWaitsForALeftOutToggleIsLeftOut(bool on)
        {
            // On: it never fires. Off: it would always be on, which no layer can build.
            var menu = avatar.AddMenu();
            var heads = OnlyIfUsed(menu);
            var trigger = Trigger(menu, new[] { OnToggle(heads, on) });

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(plan.Tree.Triggers.Single(t => t.Entry == trigger).LeftOutBy, Is.Not.Null);
            Assert.That(Planned(plan, trigger), Is.Null);
        }

        [Test]
        public void AnAlwaysMetGroupLeavesTheTriggerOutWhateverTheOtherGroups()
        {
            // "Heads press off" or "AFK": with Heads press left out, the first group is always met.
            var menu = avatar.AddMenu();
            var heads = OnlyIfUsed(menu);
            var trigger = Trigger(menu, new[] { OnToggle(heads, false) }, new[] { Afk() });

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(plan.Tree.Triggers.Single(t => t.Entry == trigger).LeftOutBy, Is.SameAs(Control(plan, heads)));
            Assert.That(Planned(plan, trigger), Is.Null);
        }

        [Test]
        public void AFlipTriggerWithALeftOutTargetIsLeftOut()
        {
            var menu = avatar.AddMenu();
            var heads = OnlyIfUsed(menu);
            var boop = menu.AddEntry(EntryKind.Trigger, "Boop");
            boop.source = TriggerSource.Contact;
            boop.contactAnchor = avatar.Body.transform;
            boop.touchMode = TouchMode.FlipToggle;
            boop.flipToggleId = heads.id;

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(plan.Tree.Triggers.Single(t => t.Entry == boop).LeftOutBy, Is.SameAs(Control(plan, heads)));
            Assert.That(plan.Messages.Any(m => m.Key == "ad.info.flip_target_excluded"), Is.False, "the platform is not the reason");
            Assert.That(Says(plan, "ad.info.trigger_left_out_unused", boop), Is.True);
        }

        [Test]
        public void AMergeThatIsNotBuiltIsNotAUse()
        {
            var controller = new UnityEditor.Animations.AnimatorController();
            controller.AddParameter(Parameter, AnimatorControllerParameterType.Bool);
            try
            {
                var menu = avatar.AddMenu();
                var merge = AnimatorMerge.Create(controller);
                merge.platform = EntryPlatform.MobileOnly;
                menu.animators.Add(merge);
                var heads = OnlyIfUsed(menu);

                Assert.That(Control(AvatarPlanCache.GetFor(menu), heads).Unused, Is.True, "a Quest only merge reads nothing on PC");
            }
            finally
            {
                Object.DestroyImmediate(controller);
            }
        }

        [Test]
        public void ALeftOutEntryTakesNoMenuSlot()
        {
            var menu = avatar.AddMenu();
            menu.installMode = InstallMode.AsSubmenu;
            for (int i = 0; i < 8; i++)
            {
                var entry = OnlyIfUsed(menu, "Kind " + i);
                entry.parameterName = Parameter + i;
            }
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });

            var plan = AvatarPlanCache.GetFor(menu);
            Assert.That(plan.Tree.AllNodes().Any(n => n.Name == "More"), Is.False, "eight left out and one built fit one page");

            var menuName = menu.EffectiveMenuName;
            RunAvatarDocBuild(avatar);
            var submenu = avatar.Descriptor.expressionsMenu.controls.Single(c => c.subMenu != null && c.name == menuName).subMenu;
            Assert.That(submenu.controls.Select(c => c.name), Is.EqualTo(new[] { "Hat" }));
        }

        [Test]
        public void ChecksSkipALeftOutEntry()
        {
            var menu = avatar.AddMenu();
            var heads = OnlyIfUsed(menu);
            heads.objects.Add(new ObjectTarget { target = avatar.Hat });
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(plan.Messages.Any(m => m.Key == "ad.info.target_shared"), Is.False, "a left out entry shares nothing");
        }

        [Test]
        public void TheBuildsLineSaysWhy()
        {
            var menu = avatar.AddMenu();
            var heads = OnlyIfUsed(menu);
            var view = new AvatarDoctorView(new SerializedObject(menu), menu, false, new MemoryViewPrefs());

            Assert.That(WhatThisBuildsModel.Describe(view.Plan, view.Tree.RowFor(heads.id)), Is.EqualTo(L.Text("ad.ui.builds.nothing.unused")));
        }

        [Test]
        public void ThePreviewLeavesNothingOut()
        {
            // The preview cannot ask other tools, so it shows the toggle as if it were built.
            var menu = avatar.AddMenu();
            var heads = OnlyIfUsed(menu);

            var preview = AvatarPlanCache.PreviewPlanFor(avatar.Root.transform, new[] { menu });

            Assert.That(preview.Tree.Controls.Single(c => c.Entry == heads).Unused, Is.False);
        }

#if DOCME_MA
        [Test]
        public void ThePlatformParityListHasTheParameterOnlyWhenTheEntryBuilds()
        {
            // A PC copy with the tool's part on and a Quest copy with it off declare different lists.
            var menu = avatar.AddMenu();
            OnlyIfUsed(menu);
            Assert.That(PlatformParity.ListOf(avatar.Root.transform).Any(p => p.StartsWith(Parameter)), Is.False);

            AddOtherTool(Child("Skin Touch"));
            AvatarPlanCache.Invalidate();
            Assert.That(PlatformParity.ListOf(avatar.Root.transform).Any(p => p.StartsWith(Parameter)), Is.True);
        }

        [Test]
        public void TheWholePipelineKeepsAParameterAnotherToolReports()
        {
            AddOtherTool(Child("Skin Touch"));
            var menu = avatar.AddMenu();
            OnlyIfUsed(menu);
            OnlyIfUsed(menu, "Feet press").parameterName = "DocMe/SkinTouch/Feet";
            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                var descriptor = processed.GetComponent<VRCAvatarDescriptor>();
                var names = descriptor.expressionsMenu.controls.Select(c => c.name).ToList();
                Assert.That(names, Does.Contain("Heads press"));
                Assert.That(names, Does.Not.Contain("Feet press"), "nothing reports the feet parameter");
                Assert.That(descriptor.expressionParameters.parameters.Any(p => p.name == Parameter && p.networkSynced), Is.True, "Modular Avatar kept it");
                Assert.That(descriptor.expressionParameters.parameters.Any(p => p.name == "DocMe/SkinTouch/Feet"), Is.False);
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
            }
        }

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
