using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;

namespace DocMe.AvatarDoctor.Editor
{
    public class PlatformTests
    {
        TestAvatar avatar;
        readonly List<Object> extras = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            Platform.OverrideMobile = false;
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            foreach (var extra in extras) Object.DestroyImmediate(extra);
            extras.Clear();
            Platform.OverrideMobile = null;
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        /// <summary>Adds an empty submenu of this name to the avatar's own expressions menu, as a menu the author already had.</summary>
        VRCExpressionsMenu AddExistingSubmenu(TestAvatar a, string name)
        {
            var submenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            submenu.name = name;
            submenu.controls = new List<VRCExpressionsMenu.Control>();
            extras.Add(submenu);
            a.ExpressionsMenu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = name,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = submenu,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "" },
            });
            return submenu;
        }

        static MenuTree Model(TestAvatar a, params AvatarDoc[] menus)
        {
            return MenuModel.Build(a.Root.transform, a.ExpressionsMenu, menus);
        }

        static ParameterPlan Plan(MenuTree tree)
        {
            return ParameterPlanner.Plan(tree, new Dictionary<string, PlannedType> { { "Existing", PlannedType.Bool } });
        }

        static AnimatorController FxOf(TestAvatar a)
        {
            return (AnimatorController)a.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
        }

        static List<ErrorContext> RunFullBuild(TestAvatar a)
        {
            var context = new BuildContext(a.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            return errors;
        }

        static List<string> PlanSignature(ParameterPlan plan)
        {
            return plan.Parameters.Select(p => string.Join("|", p.Name, p.Type.ToString(),
                p.Default.ToString(CultureInfo.InvariantCulture), p.Saved.ToString(), p.Synced.ToString(),
                p.ReusesExisting.ToString(), p.IsAnimatorOnly.ToString())).ToList();
        }

        static List<string> BindingSignature(MenuTree tree, ParameterPlan plan)
        {
            return tree.Controls.Select(control =>
            {
                if (!plan.Bindings.TryGetValue(control, out var binding)) return control.Entry.PlainName + "|unbound";
                return string.Join("|", control.Entry.PlainName, control.Path, binding.Parameter.Name,
                    binding.Value.ToString(CultureInfo.InvariantCulture), binding.OnByDefault.ToString());
            }).ToList();
        }

        static List<string> AssetSignature(VRCExpressionParameters parameters)
        {
            return parameters.parameters.Select(p => string.Join("|", p.name, p.valueType.ToString(),
                p.defaultValue.ToString(CultureInfo.InvariantCulture), p.saved.ToString(), p.networkSynced.ToString())).ToList();
        }

        /// <summary>One component holding every kind the platform choice touches, so both platforms plan the same list from the same source.</summary>
        static AvatarDoc BuildSample(TestAvatar a)
        {
            var menu = a.AddMenu();

            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = a.Hat });

            var eyes = menu.AddEntry(EntryKind.Radial, "Eyes");
            eyes.blendShapes.Add(new BlendShapeTarget { renderer = a.Body, shape = "Eye_Big" });

            var first = menu.AddEntry(EntryKind.Toggle, "First");
            first.exclusiveGroup = "Outfits";
            first.objects.Add(new ObjectTarget { target = a.Hat });
            var middle = menu.AddEntry(EntryKind.Toggle, "Middle");
            middle.exclusiveGroup = "Outfits";
            middle.platform = EntryPlatform.PCOnly;
            middle.objects.Add(new ObjectTarget { target = a.Glasses });
            var last = menu.AddEntry(EntryKind.Toggle, "Last");
            last.exclusiveGroup = "Outfits";
            last.objects.Add(new ObjectTarget { target = a.Glasses });

            var preset = menu.AddEntry(EntryKind.Preset, "Preset");
            preset.presetStates.Add(new PresetState { entryId = hat.id, on = true });
            preset.presetStates.Add(new PresetState { entryId = middle.id, on = true });

            var touch = menu.AddEntry(EntryKind.Trigger, "Touch");
            touch.source = TriggerSource.Contact;
            touch.contactAnchor = a.Body.transform;
            touch.objects.Add(new ObjectTarget { target = a.Glasses });

            var questHat = menu.AddEntry(EntryKind.Toggle, "Quest hat");
            questHat.platform = EntryPlatform.MobileOnly;
            questHat.objects.Add(new ObjectTarget { target = a.Hat });

            var deskOnly = menu.AddEntry(EntryKind.Submenu, "Desk only");
            deskOnly.platform = EntryPlatform.PCOnly;
            var child = menu.AddEntry(EntryKind.Toggle, "Shiny", deskOnly.id);
            child.blendShapes.Add(new BlendShapeTarget { renderer = a.Body, shape = "Blush", weight = 100f });

            return menu;
        }

        [Test]
        public void ParameterListIsIdenticalOnBothPlatforms()
        {
            var menu = BuildSample(avatar);

            Platform.OverrideMobile = false;
            var pcTree = Model(avatar, menu);
            var pcPlan = Plan(pcTree);
            Platform.OverrideMobile = true;
            var questTree = Model(avatar, menu);
            var questPlan = Plan(questTree);

            Assert.That(PlanSignature(questPlan), Is.EqualTo(PlanSignature(pcPlan)), "planned parameters");
            Assert.That(BindingSignature(questTree, questPlan), Is.EqualTo(BindingSignature(pcTree, pcPlan)), "control bindings");
            Assert.That(pcPlan.Parameters.Select(p => p.Name),
                Is.EqualTo(new[] { "AD/Hat", "AD/Eyes", "AD/Outfits", "AD/Preset", "AD/Quest hat", "AD/Shiny" }));

            // The same avatar built twice through the whole pipeline: what VRChat reads has to match too.
            Platform.OverrideMobile = false;
            RunFullBuild(avatar);
            var pcAsset = AssetSignature(avatar.Descriptor.expressionParameters);

            using (var quest = TestAvatar.Create())
            {
                BuildSample(quest);
                Platform.OverrideMobile = true;
                RunFullBuild(quest);
                Assert.That(AssetSignature(quest.Descriptor.expressionParameters), Is.EqualTo(pcAsset), "written expression parameters");
                Assert.That(quest.Descriptor.expressionParameters.parameters.Select(p => p.name),
                    Is.EqualTo(new[] { "Existing", "AD/Hat", "AD/Eyes", "AD/Outfits", "AD/Preset", "AD/Quest hat", "AD/Shiny" }));
            }
        }

        [Test]
        public void ExcludedToggleHasNoControlAndNoLayer()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var questHat = menu.AddEntry(EntryKind.Toggle, "Quest hat");
            questHat.platform = EntryPlatform.MobileOnly;
            questHat.objects.Add(new ObjectTarget { target = avatar.Glasses });
            avatar.Glasses.SetActive(false);

            RunFullBuild(avatar);

            Assert.That(avatar.Descriptor.expressionParameters.parameters.Select(p => p.name),
                Is.EqualTo(new[] { "Existing", "AD/Hat", "AD/Quest hat" }), "its parameter is still declared");
            Assert.That(FxOf(avatar).layers.Select(l => l.name), Does.Not.Contain("AD: Quest hat"));
            var names = avatar.Descriptor.expressionsMenu.controls.Select(c => c.name).ToList();
            Assert.That(names, Does.Contain("Hat"));
            Assert.That(names, Does.Not.Contain("Quest hat"));
        }

        [Test]
        public void ExcludedSubmenuExcludesItsChildrenUnlessTheyOverride()
        {
            var menu = avatar.AddMenu();
            var deskOnly = menu.AddEntry(EntryKind.Submenu, "Desk only");
            deskOnly.platform = EntryPlatform.PCOnly;
            var inherits = menu.AddEntry(EntryKind.Toggle, "Inherits", deskOnly.id);
            inherits.objects.Add(new ObjectTarget { target = avatar.Hat });
            var overrides = menu.AddEntry(EntryKind.Toggle, "Overrides", deskOnly.id);
            overrides.platform = EntryPlatform.Both;
            overrides.objects.Add(new ObjectTarget { target = avatar.Glasses });

            Platform.OverrideMobile = true;
            var tree = Model(avatar, menu);

            Assert.That(tree.Controls.Select(c => c.Entry.PlainName), Is.EqualTo(new[] { "Inherits", "Overrides" }), "both still plan their parameters");
            Assert.That(tree.Controls.Single(c => c.Entry == inherits).Excluded, Is.True);
            Assert.That(tree.Controls.Single(c => c.Entry == overrides).Excluded, Is.False);
            var item = tree.Root.Items.Single(i => i.Submenu != null && i.Submenu.EntryId == deskOnly.id);
            Assert.That(item.Excluded, Is.False, "the submenu stays, or the child that overrode it would have nowhere to live");
            Assert.That(item.Submenu.Items.Count(i => !i.Excluded), Is.EqualTo(1));
        }

        [Test]
        public void GroupValuesDoNotRenumberWhenAMemberIsExcluded()
        {
            var menu = avatar.AddMenu();
            var names = new[] { "First", "Middle", "Last" };
            var members = names.Select(name =>
            {
                var entry = menu.AddEntry(EntryKind.Toggle, name);
                entry.exclusiveGroup = "Outfits";
                entry.objects.Add(new ObjectTarget { target = avatar.Hat });
                return entry;
            }).ToList();
            members[1].platform = EntryPlatform.PCOnly;

            Platform.OverrideMobile = true;
            var tree = Model(avatar, menu);
            var plan = Plan(tree);

            var values = members.Select(entry => plan.Bindings[tree.Controls.Single(c => c.Entry == entry)].Value).ToList();
            Assert.That(values, Is.EqualTo(new[] { 1f, 2f, 3f }));

            RunFullBuild(avatar);
            var layer = FxOf(avatar).layers.Single(l => l.name == "AD: Outfits");
            Assert.That(layer.stateMachine.states.Select(s => s.state.name), Is.EquivalentTo(new[] { "Off", "First", "Last" }));
            var control = avatar.Descriptor.expressionsMenu.controls.Single(c => c.name == "Last");
            Assert.That(control.value, Is.EqualTo(3f));
        }

        [Test]
        public void ExcludedEntriesSkipTargetValidation()
        {
            var menu = avatar.AddMenu();
            var broken = menu.AddEntry(EntryKind.Toggle, "Broken");
            broken.objects.Add(new ObjectTarget { target = null });
            broken.platform = EntryPlatform.MobileOnly;

            var pc = Model(avatar, menu);
            Assert.That(pc.Messages.Any(m => m.Key == "ad.warn.target_missing"), Is.False);

            Platform.OverrideMobile = true;
            var quest = Model(avatar, menu);
            Assert.That(quest.Messages.Any(m => m.Key == "ad.warn.target_missing"), Is.True, "the platform it is built on still checks it");
        }

        [Test]
        public void ExcludedContactTriggerCreatesNoReceiver()
        {
            var menu = avatar.AddMenu();
            var touch = menu.AddEntry(EntryKind.Trigger, "Touch");
            touch.source = TriggerSource.Contact;
            touch.contactAnchor = avatar.Body.transform;
            touch.platform = EntryPlatform.MobileOnly;
            touch.objects.Add(new ObjectTarget { target = avatar.Hat });

            RunFullBuild(avatar);

            Assert.That(avatar.Root.GetComponentsInChildren<VRCContactReceiver>(true), Is.Empty);
            Assert.That(FxOf(avatar).layers.Select(l => l.name), Does.Not.Contain("AD: Touch"));
        }

        [Test]
        public void ExcludedPresetKeepsItsParameterOnly()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var preset = menu.AddEntry(EntryKind.Preset, "Preset");
            preset.platform = EntryPlatform.MobileOnly;
            preset.presetStates.Add(new PresetState { entryId = hat.id, on = true });

            RunFullBuild(avatar);

            Assert.That(avatar.Descriptor.expressionParameters.parameters.Select(p => p.name),
                Is.EqualTo(new[] { "Existing", "AD/Hat", "AD/Preset" }));
            Assert.That(FxOf(avatar).layers.Select(l => l.name), Does.Not.Contain("AD: Preset (preset)"));
            Assert.That(avatar.Descriptor.expressionsMenu.controls.Select(c => c.name), Does.Not.Contain("Preset"));
        }

        [Test]
        public void IncludedPresetStillDrivesAnExcludedControl()
        {
            var menu = avatar.AddMenu();
            var questHat = menu.AddEntry(EntryKind.Toggle, "Quest hat");
            questHat.platform = EntryPlatform.MobileOnly;
            questHat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var preset = menu.AddEntry(EntryKind.Preset, "Preset");
            preset.presetStates.Add(new PresetState { entryId = questHat.id, on = true });

            var tree = Model(avatar, menu);
            var plan = Plan(tree);
            var messages = new List<ModelMessage>();
            var operations = PresetBuilder.Operations(preset, tree, plan, messages);

            Assert.That(operations.Select(o => o.parameter), Is.EqualTo(new[] { "AD/Quest hat" }));
            Assert.That(messages, Is.Empty, "the parameter exists on both platforms, so there is nothing to warn about");
        }

        [Test]
        public void ComponentDefaultAppliesToInheritingEntries()
        {
            var menu = avatar.AddMenu();
            menu.platform = PlatformChoice.MobileOnly;
            var inherits = menu.AddEntry(EntryKind.Toggle, "Inherits");
            inherits.objects.Add(new ObjectTarget { target = avatar.Hat });
            var both = menu.AddEntry(EntryKind.Toggle, "Both");
            both.platform = EntryPlatform.Both;
            both.objects.Add(new ObjectTarget { target = avatar.Glasses });

            var tree = Model(avatar, menu);

            Assert.That(Platform.Effective(menu, inherits), Is.EqualTo(PlatformChoice.MobileOnly));
            Assert.That(Platform.Effective(menu, both), Is.EqualTo(PlatformChoice.Both));
            Assert.That(tree.Controls.Single(c => c.Entry == inherits).Excluded, Is.True);
            Assert.That(tree.Controls.Single(c => c.Entry == both).Excluded, Is.False);
        }

        [Test]
        public void ExcludedRowsAreDimmedWithAChip()
        {
            var menu = avatar.AddMenu();
            var questHat = menu.AddEntry(EntryKind.Toggle, "Quest hat");
            questHat.platform = EntryPlatform.MobileOnly;
            questHat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Glasses });

            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            var excludedRow = view.Tree.RowFor(questHat.id);
            var plainRow = view.Tree.RowFor(hat.id);

            Assert.That(excludedRow.Excluded, Is.True);
            Assert.That(excludedRow.PlatformChip, Is.EqualTo(L.Text("ad.ui.chip.quest_only")));
            Assert.That(plainRow.Excluded, Is.False);
            Assert.That(plainRow.PlatformChip, Is.Null);

            view.Tree.SelectEntry(questHat.id);
            var chips = view.Detail.Query<Label>(className: "ad-chip").ToList();
            Assert.That(chips.Select(c => c.text), Does.Contain(L.Text("ad.ui.chip.quest_only")));
            Assert.That(view.Detail.Q<DropdownField>("ad-entry-platform"), Is.Not.Null);
            Assert.That(view.Header.Q<SegmentedChoice>("ad-platform"), Is.Not.Null);
            Assert.That(view.Header.Q<Label>("ad-platform-note").text,
                Is.EqualTo(string.Format(L.Text("ad.ui.platform.current"), L.Text("ad.ui.platform.name.pc"))));
        }

        [Test]
        public void ExcludedControlsAreNotInTheWheel()
        {
            var menu = avatar.AddMenu();
            var questHat = menu.AddEntry(EntryKind.Toggle, "Quest hat");
            questHat.platform = EntryPlatform.MobileOnly;
            questHat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Glasses });

            var tree = Model(avatar, menu);
            var wheel = WheelModel.For(tree.Root, null, "Avatar menu");

            Assert.That(wheel.Wedges.Select(w => w.Name), Does.Not.Contain("Quest hat"));
            Assert.That(wheel.Wedges.Select(w => w.Name), Does.Contain("Hat"));
        }

        [Test]
        public void FlipTriggerWhoseToggleIsExcludedSaysSoAndBuildsNoLayer()
        {
            var menu = avatar.AddMenu();
            var questHat = menu.AddEntry(EntryKind.Toggle, "Quest hat");
            questHat.platform = EntryPlatform.MobileOnly;
            questHat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var touch = menu.AddEntry(EntryKind.Trigger, "Touch");
            touch.source = TriggerSource.Contact;
            touch.contactAnchor = avatar.Body.transform;
            touch.touchMode = TouchMode.FlipToggle;
            touch.flipToggleId = questHat.id;

            var tree = Model(avatar, menu);
            Assert.That(tree.Messages.Any(m => m.Key == "ad.info.flip_target_excluded"), Is.True);
            Assert.That(tree.Messages.Any(m => m.Key == "ad.error.flip_target"), Is.False);

            RunFullBuild(avatar);
            Assert.That(FxOf(avatar).layers.Select(l => l.name), Does.Not.Contain("AD: Touch (flip)"));
        }

        [Test]
        public void PlanCacheIsDroppedWhenTheTargetPlatformChanges()
        {
            var menu = avatar.AddMenu();
            var questHat = menu.AddEntry(EntryKind.Toggle, "Quest hat");
            questHat.platform = EntryPlatform.MobileOnly;
            questHat.objects.Add(new ObjectTarget { target = avatar.Hat });

            var onPc = AvatarPlanCache.GetFor(menu);
            Assert.That(onPc.Tree.Controls.Single().Excluded, Is.True);

            Platform.OverrideMobile = true;
            var onQuest = AvatarPlanCache.GetFor(menu);

            Assert.That(onQuest, Is.Not.SameAs(onPc), "the cached plan was built for the other platform");
            Assert.That(onQuest.Tree.Controls.Single().Excluded, Is.False);
        }

        [Test]
        public void ExcludedFlipTargetGetsNoReceiver()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.platform = EntryPlatform.MobileOnly;
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var trigger = menu.AddEntry(EntryKind.Trigger, "Boop");
            trigger.source = TriggerSource.Contact;
            trigger.contactAnchor = avatar.Body.transform;
            trigger.touchMode = TouchMode.FlipToggle;
            trigger.flipToggleId = hat.id;

            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });
            var touch = TouchBuilder.Describe(tree, new Dictionary<string, ExistingParameter>());

            Assert.That(touch.ByTriggerId.ContainsKey(trigger.id), Is.False, "nothing to flip here, so no contact is spent on it");
            Assert.That(tree.Messages.Any(m => m.Key == "ad.info.flip_target_excluded"), Is.True);
        }

        [Test]
        public void ExcludedGroupDefaultIsReported()
        {
            var menu = avatar.AddMenu();
            var casual = menu.AddEntry(EntryKind.Toggle, "Casual");
            casual.exclusiveGroup = "Outfits";
            casual.defaultOn = true;
            casual.platform = EntryPlatform.MobileOnly;
            casual.objects.Add(new ObjectTarget { target = avatar.Hat });
            var formal = menu.AddEntry(EntryKind.Toggle, "Formal");
            formal.exclusiveGroup = "Outfits";
            formal.objects.Add(new ObjectTarget { target = avatar.Glasses });

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(plan.Messages.Any(m => m.Key == "ad.info.group_default_excluded" && m.EntryIds.Contains(casual.id)), Is.True);

            Platform.OverrideMobile = true;
            AvatarPlanCache.Invalidate();

            Assert.That(AvatarPlanCache.GetFor(menu).Messages.Any(m => m.Key == "ad.info.group_default_excluded"), Is.False, "on the platform that builds it there is nothing to say");
        }

        [Test]
        public void ParityWarningNamesTheFirstDifference()
        {
            using (var twin = TestAvatar.Create())
            {
                var blueprint = "avtr_platform_parity_test";
                avatar.Root.AddComponent<VRC.Core.PipelineManager>().blueprintId = blueprint;
                twin.Root.AddComponent<VRC.Core.PipelineManager>().blueprintId = blueprint;

                var menu = avatar.AddMenu();
                menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
                var twinMenu = twin.AddMenu();
                twinMenu.AddEntry(EntryKind.Toggle, "Boots").objects.Add(new ObjectTarget { target = twin.Hat });

                AvatarPlanCache.Invalidate();
                var plan = AvatarPlanCache.GetFor(menu);
                var parity = plan.Messages.SingleOrDefault(m => m.Key == "ad.warn.platform_parity");

                Assert.That(parity, Is.Not.Null);
                Assert.That(parity.Args[0], Is.EqualTo(twin.Root.name));
                Assert.That(parity.Args[1], Is.EqualTo("2"), "the first position after the shared Existing parameter");
                Assert.That(parity.Args[2], Is.EqualTo("AD/Hat (Bool, synced)"));
                Assert.That(parity.Args[3], Is.EqualTo("AD/Boots (Bool, synced)"));
            }
        }

        [Test]
        public void FixingTheTwinClearsTheWarningOnThisAvatar()
        {
            using (var twin = TestAvatar.Create())
            {
                var blueprint = "avtr_platform_parity_stale";
                avatar.Root.AddComponent<VRC.Core.PipelineManager>().blueprintId = blueprint;
                twin.Root.AddComponent<VRC.Core.PipelineManager>().blueprintId = blueprint;

                var menu = avatar.AddMenu();
                menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
                var twinMenu = twin.AddMenu();
                var boots = twinMenu.AddEntry(EntryKind.Toggle, "Boots");
                boots.objects.Add(new ObjectTarget { target = twin.Hat });

                AvatarPlanCache.Invalidate();
                Assert.That(AvatarPlanCache.GetFor(menu).Messages.Any(m => m.Key == "ad.warn.platform_parity"), Is.True);

                boots.name = "Hat";
                AvatarPlanCache.NoteChangedObjects(new Object[] { twinMenu });

                Assert.That(AvatarPlanCache.GetFor(menu).Messages.Any(m => m.Key == "ad.warn.platform_parity"), Is.False,
                    "the copy that raised the warning replans once its twin is fixed");
            }
        }

        [Test]
        public void AReorderWithALiveTwinRecomputesTheParityComparison()
        {
            using (var twin = TestAvatar.Create())
            {
                var blueprint = "avtr_platform_parity_reorder";
                avatar.Root.AddComponent<VRC.Core.PipelineManager>().blueprintId = blueprint;
                twin.Root.AddComponent<VRC.Core.PipelineManager>().blueprintId = blueprint;

                var menu = avatar.AddMenu();
                var a = menu.AddEntry(EntryKind.Toggle, "A"); a.parameterName = "Alpha"; a.objects.Add(new ObjectTarget { target = avatar.Hat });
                var b = menu.AddEntry(EntryKind.Toggle, "B"); b.parameterName = "Beta"; b.objects.Add(new ObjectTarget { target = avatar.Glasses });
                var twinMenu = twin.AddMenu();
                var ta = twinMenu.AddEntry(EntryKind.Toggle, "A"); ta.parameterName = "Alpha"; ta.objects.Add(new ObjectTarget { target = twin.Hat });
                var tb = twinMenu.AddEntry(EntryKind.Toggle, "B"); tb.parameterName = "Beta"; tb.objects.Add(new ObjectTarget { target = twin.Glasses });

                AvatarPlanCache.Invalidate();
                Assert.That(AvatarPlanCache.GetFor(menu).Messages.Any(m => m.Key == "ad.warn.platform_parity"), Is.False, "both sides start in the same generated order");

                // The same shape of change EntryEditing.Reorder makes: A and B swap places, so the generated
                // parameters land in the opposite order to the twin's.
                menu.entries.Remove(a);
                menu.entries.Add(a);
                AvatarPlanCache.InvalidateOrder(avatar.Root.transform);

                var parity = AvatarPlanCache.GetFor(menu).Messages.SingleOrDefault(m => m.Key == "ad.warn.platform_parity");
                Assert.That(parity, Is.Not.Null, "the reorder changed which generated parameter lands at this position, so the comparison must not lag one move behind");
            }
        }

        [Test]
        public void EditingThisAvatarDoesNotRebuildTheTwinsOwnList()
        {
            using (var twin = TestAvatar.Create())
            {
                var blueprint = "avtr_platform_parity_twin_cache";
                avatar.Root.AddComponent<VRC.Core.PipelineManager>().blueprintId = blueprint;
                twin.Root.AddComponent<VRC.Core.PipelineManager>().blueprintId = blueprint;

                var menu = avatar.AddMenu();
                menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
                var twinMenu = twin.AddMenu();
                twinMenu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = twin.Hat });

                AvatarPlanCache.Invalidate();
                AvatarPlanCache.GetFor(menu);
                int builds = PlatformParity.ListBuilds;

                // A menu-only edit to this avatar: adding an entry here cannot change what the twin will upload.
                menu.AddEntry(EntryKind.Toggle, "Extra").objects.Add(new ObjectTarget { target = avatar.Glasses });
                AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
                AvatarPlanCache.GetFor(menu);

                Assert.That(PlatformParity.ListBuilds, Is.EqualTo(builds + 1), "only this avatar's own list needed rebuilding; the twin's untouched list was reused");
            }
        }

        [Test]
        public void DisabledComponentDoesNotRaiseAParityWarning()
        {
            using (var twin = TestAvatar.Create())
            {
                var blueprint = "avtr_platform_parity_disabled";
                avatar.Root.AddComponent<VRC.Core.PipelineManager>().blueprintId = blueprint;
                twin.Root.AddComponent<VRC.Core.PipelineManager>().blueprintId = blueprint;

                var menu = avatar.AddMenu();
                menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
                // A disabled component is skipped at build, so it is no part of what either copy will upload.
                var spare = new GameObject("Spare");
                spare.transform.SetParent(avatar.Root.transform);
                var disabled = avatar.AddMenu(spare);
                disabled.enabled = false;
                disabled.AddEntry(EntryKind.Toggle, "Extra").objects.Add(new ObjectTarget { target = avatar.Glasses });

                var twinMenu = twin.AddMenu();
                twinMenu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = twin.Hat });

                AvatarPlanCache.Invalidate();
                var plan = AvatarPlanCache.GetFor(menu);

                Assert.That(plan.Tree.Components, Does.Contain(disabled), "the inspector still shows it");
                Assert.That(plan.Messages.Any(m => m.Key == "ad.warn.platform_parity"), Is.False);
            }
        }

        [Test]
        public void ParityUsesTheBuildOrderOnBothSides()
        {
            using (var twin = TestAvatar.Create())
            {
                var blueprint = "avtr_platform_parity_order";
                avatar.Root.AddComponent<VRC.Core.PipelineManager>().blueprintId = blueprint;
                twin.Root.AddComponent<VRC.Core.PipelineManager>().blueprintId = blueprint;

                // "Old" comes before "Second" in the avatar's own menu, so installing Alpha into Second and Beta
                // into Old puts the controls in the opposite order to the components that hold them.
                foreach (var a in new[] { avatar, twin })
                {
                    AddExistingSubmenu(a, "Second");
                    var alphaObject = new GameObject("Alpha holder");
                    alphaObject.transform.SetParent(a.Root.transform);
                    var alpha = a.AddMenu(alphaObject);
                    alpha.installPath = "Second";
                    alpha.AddEntry(EntryKind.Toggle, "Alpha").objects.Add(new ObjectTarget { target = a.Hat });
                    var betaObject = new GameObject("Beta holder");
                    betaObject.transform.SetParent(a.Root.transform);
                    var beta = a.AddMenu(betaObject);
                    beta.installPath = "Old";
                    beta.AddEntry(EntryKind.Toggle, "Beta").objects.Add(new ObjectTarget { target = a.Glasses });
                }

                Assert.That(PlatformParity.ListOf(avatar.Root.transform),
                    Is.EqualTo(new[] { "Existing (Bool, synced)", "AD/Beta (Bool, synced)", "AD/Alpha (Bool, synced)" }),
                    "menu order, not component order");

                AvatarPlanCache.Invalidate();
                var plan = AvatarPlanCache.GetFor(avatar.Root.GetComponentsInChildren<AvatarDoc>(true)[0]);

                Assert.That(plan.Messages.Any(m => m.Key == "ad.warn.platform_parity"), Is.False, "identical twins agree");
            }
        }

        [Test]
        public void ExistingSubmenuSurvivesWhenItsAvatarDoctorContentIsExcluded()
        {
            AddExistingSubmenu(avatar, "Empty");
            var menu = avatar.AddMenu();
            var merged = menu.AddEntry(EntryKind.Submenu, "Empty");
            merged.platform = EntryPlatform.PCOnly;
            var child = menu.AddEntry(EntryKind.Toggle, "Inner", merged.id);
            child.objects.Add(new ObjectTarget { target = avatar.Hat });

            Platform.OverrideMobile = true;
            RunFullBuild(avatar);

            Assert.That(avatar.Descriptor.expressionsMenu.controls.Select(c => c.name), Does.Contain("Empty"),
                "the avatar's own submenu is not AvatarDoctor's to remove");
            var written = avatar.Descriptor.expressionsMenu.controls.Single(c => c.name == "Empty").subMenu;
            Assert.That(written.controls, Is.Empty);
        }

        [Test]
        public void SubmenuWithOnlyHiddenSurvivorsFallsAway()
        {
            var menu = avatar.AddMenu();
            var box = menu.AddEntry(EntryKind.Submenu, "Box");
            var inner = menu.AddEntry(EntryKind.Toggle, "Inner", box.id);
            inner.platform = EntryPlatform.PCOnly;
            inner.objects.Add(new ObjectTarget { target = avatar.Hat });
            var edit = MenuEdit.Create(new[] { "RootToggle" });
            edit.move = true;
            edit.targetPath = "Box";
            edit.hide = true;
            menu.menuEdits.Add(edit);

            var onPc = Model(avatar, menu);
            var pcItem = onPc.Root.Items.Single(i => i.Submenu != null && i.Submenu.EntryId == box.id);
            Assert.That(pcItem.Excluded, Is.False, "Inner is built here, so the submenu has something to hold");

            Platform.OverrideMobile = true;
            var onQuest = Model(avatar, menu);
            var questItem = onQuest.Root.Items.Single(i => i.Submenu != null && i.Submenu.EntryId == box.id);
            Assert.That(questItem.Submenu.Items.Count, Is.EqualTo(2));
            Assert.That(questItem.Excluded, Is.True, "a hidden item is not written either, so it cannot keep the submenu alive");
        }

        [Test]
        public void ExcludedOverrideRuleIsNotPinned()
        {
            var menu = avatar.AddMenu();
            var rule = menu.AddEntry(EntryKind.Override, "Hold blush");
            rule.platform = EntryPlatform.MobileOnly;
            rule.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            avatar.Body.SetBlendShapeWeight(0, 0f);

            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            var overrides = context.GetState<OverrideState>();
            context.DeactivateAllExtensionContexts();

            Assert.That(overrides.Rules, Is.Empty, "nothing for the later pass to pin");
            Assert.That(avatar.Body.GetBlendShapeWeight(0), Is.EqualTo(0f), "and no scene default written");
        }

        [Test]
        public void EffectivePlatformSurvivesCyclesAndMissingParents()
        {
            var menu = avatar.AddMenu();
            menu.platform = PlatformChoice.PCOnly;

            var orphan = menu.AddEntry(EntryKind.Toggle, "Orphan");
            orphan.parentId = "no-such-entry";
            Assert.That(Platform.Effective(menu, orphan), Is.EqualTo(PlatformChoice.PCOnly));

            var a = menu.AddEntry(EntryKind.Submenu, "A");
            var b = menu.AddEntry(EntryKind.Submenu, "B");
            a.parentId = b.id;
            b.parentId = a.id;
            var child = menu.AddEntry(EntryKind.Toggle, "Child", a.id);

            Assert.That(Platform.Effective(menu, child), Is.EqualTo(PlatformChoice.PCOnly), "the walk stops on the cycle");

            b.platform = EntryPlatform.MobileOnly;
            Assert.That(Platform.Effective(menu, child), Is.EqualTo(PlatformChoice.MobileOnly), "and still reads what it passes");
        }

        [Test]
        public void MatchingTwinRaisesNoParityWarning()
        {
            using (var twin = TestAvatar.Create())
            {
                var blueprint = "avtr_platform_parity_match";
                avatar.Root.AddComponent<VRC.Core.PipelineManager>().blueprintId = blueprint;
                twin.Root.AddComponent<VRC.Core.PipelineManager>().blueprintId = blueprint;

                var menu = avatar.AddMenu();
                var mine = menu.AddEntry(EntryKind.Toggle, "Hat");
                mine.platform = EntryPlatform.PCOnly;
                mine.objects.Add(new ObjectTarget { target = avatar.Hat });
                var twinMenu = twin.AddMenu();
                var theirs = twinMenu.AddEntry(EntryKind.Toggle, "Hat");
                theirs.platform = EntryPlatform.PCOnly;
                theirs.objects.Add(new ObjectTarget { target = twin.Hat });

                AvatarPlanCache.Invalidate();
                Platform.OverrideMobile = true;
                var plan = AvatarPlanCache.GetFor(menu);

                Assert.That(plan.Messages.Any(m => m.Key == "ad.warn.platform_parity"), Is.False,
                    "an entry left out on this platform still declares its parameter, so the lists match");
            }
        }

        /// <summary>Adds a bool expression parameter and a control of the avatar's own menu that drives it.</summary>
        static void AddOwnControl(TestAvatar a, string control, string parameter)
        {
            var list = a.ExpressionParameters.parameters.ToList();
            list.Add(new VRCExpressionParameters.Parameter
            {
                name = parameter,
                valueType = VRCExpressionParameters.ValueType.Bool,
                saved = true,
                networkSynced = true,
            });
            a.ExpressionParameters.parameters = list.ToArray();
            a.ExpressionsMenu.controls.Add(TestAvatar.ToggleControl(control, parameter));
        }

        [Test]
        public void ParitySeesAFreedParameter()
        {
            using (var twin = TestAvatar.Create())
            {
                var blueprint = "avtr_platform_parity_freed";
                avatar.Root.AddComponent<VRC.Core.PipelineManager>().blueprintId = blueprint;
                twin.Root.AddComponent<VRC.Core.PipelineManager>().blueprintId = blueprint;
                AddOwnControl(avatar, "Hat", "HatParam");
                AddOwnControl(twin, "Hat", "HatParam");

                var menu = avatar.AddMenu();
                menu.menuEdits.Add(new MenuEdit { id = "hat-edit", source = new List<string> { "Hat" }, remove = true });
                twin.AddMenu();

                Assert.That(PlatformParity.ListOf(avatar.Root.transform), Does.Not.Contain("HatParam (Bool, synced)"),
                    "this copy frees it");
                Assert.That(PlatformParity.ListOf(twin.Root.transform), Does.Contain("HatParam (Bool, synced)"));

                AvatarPlanCache.Invalidate();
                var parity = AvatarPlanCache.GetFor(menu).Messages.SingleOrDefault(m => m.Key == "ad.warn.platform_parity");

                Assert.That(parity, Is.Not.Null, "the shorter list is a difference like any other");
                Assert.That(parity.Args[2], Is.EqualTo("-"));
                Assert.That(parity.Args[3], Is.EqualTo("HatParam (Bool, synced)"));
            }
        }
    }
}
