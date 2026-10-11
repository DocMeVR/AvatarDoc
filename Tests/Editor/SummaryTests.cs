using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    public class SummaryTests
    {
        TestAvatar avatar;

        [SetUp]
        public void SetUp()
        {
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
        }

        [Test]
        public void SummaryListsParametersAndBits()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            var plan = AvatarPlanCache.GetFor(menu);

            var text = SummaryBuilder.Build(plan, menu, SummaryFormat.Plain);

            Assert.That(text, Does.Contain("AD/Hat"));
            Assert.That(text, Does.Contain("Bool"));
            Assert.That(plan.AvatarDoctorBits, Is.EqualTo(1));
            Assert.That(text, Does.Contain("Adds 1 synced bit. AvatarDoc uses 1 synced bit on this avatar in total."));
        }

        [Test]
        public void SummaryShowsTheMenuTreeWithSlots()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            menu.AddEntry(EntryKind.Toggle, "Casual", outfits.id).objects.Add(new ObjectTarget { target = avatar.Hat });
            menu.AddEntry(EntryKind.Toggle, "Fancy", outfits.id).objects.Add(new ObjectTarget { target = avatar.Glasses });
            var plan = AvatarPlanCache.GetFor(menu);

            var text = SummaryBuilder.Build(plan, menu, SummaryFormat.Plain);

            Assert.That(text, Does.Contain("Outfits (2/" + VRCExpressionsMenu.MAX_CONTROLS + " slots on " + L.Text("ad.ui.platform.name.pc") + ")"));
            Assert.That(text, Does.Contain("  Casual"));
            Assert.That(text, Does.Contain("  Fancy"));
        }

        [Test]
        public void SummaryCountsEveryTouchSourceAndMarksThePlatformLimitedOnes()
        {
            var menu = avatar.AddMenu();
            var poke = menu.AddEntry(EntryKind.Trigger, "Poke");
            poke.source = TriggerSource.Contact;
            poke.contactAnchor = avatar.Hat.transform;
            poke.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var questOnly = menu.AddEntry(EntryKind.Trigger, "Quest poke");
            questOnly.source = TriggerSource.Contact;
            questOnly.contactAnchor = avatar.Body.transform;
            questOnly.platform = EntryPlatform.MobileOnly;
            questOnly.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var plan = AvatarPlanCache.GetFor(menu);

            var text = SummaryBuilder.Build(plan, menu, SummaryFormat.Plain);

            Assert.That(text, Does.Contain(string.Format(L.Text("ad.summary.touch.contacts"), 2)), "the Quest only source counts on a PC page too");
            Assert.That(text, Does.Contain("Quest poke (" + L.Text("ad.ui.chip.quest_only") + ")"));
        }

#if DOCME_MA
        [Test]
        public void SummaryNamesModularAvatarWhenAParameterComesFromIt()
        {
            var holder = new GameObject("MA parameters");
            holder.transform.SetParent(avatar.Root.transform);
            var parameters = holder.AddComponent<nadena.dev.modular_avatar.core.ModularAvatarParameters>();
            parameters.parameters.Add(new nadena.dev.modular_avatar.core.ParameterConfig
            {
                nameOrPrefix = "MA_Wings", syncType = nadena.dev.modular_avatar.core.ParameterSyncType.Bool,
            });
            var menu = avatar.AddMenu();
            var trigger = menu.AddEntry(EntryKind.Trigger, "Wings");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "MA_Wings";
            trigger.objects.Add(new ObjectTarget { target = avatar.Hat });
            AvatarPlanCache.Invalidate();

            var text = SummaryBuilder.Build(AvatarPlanCache.GetFor(menu), menu, SummaryFormat.Plain);

            Assert.That(text, Does.Contain(L.Text("ad.summary.needs.modular_avatar")));
        }
#endif

        [Test]
        public void PlainFormatHasNoMarkup()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            menu.AddEntry(EntryKind.Toggle, "Hat", outfits.id).objects.Add(new ObjectTarget { target = avatar.Hat });
            var plan = AvatarPlanCache.GetFor(menu);

            var text = SummaryBuilder.Build(plan, menu, SummaryFormat.Plain);

            Assert.That(text, Does.Not.Contain("#"));
            Assert.That(text, Does.Not.Contain("*"));
            Assert.That(text, Does.Not.Contain("`"));
            Assert.That(text, Does.Not.Contain("|"));
        }

        [Test]
        public void MarkdownFormatHasAParameterTable()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            var plan = AvatarPlanCache.GetFor(menu);

            var text = SummaryBuilder.Build(plan, menu, SummaryFormat.Markdown);

            Assert.That(text, Does.Contain("## " + L.Text("ad.summary.parameters")));
            Assert.That(text, Does.Contain("|"));
            Assert.That(text, Does.Contain("AD/Hat"));
        }

        [Test]
        public void SummaryCoversOnlyItsOwnComponent()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            var other = new GameObject("Other");
            other.transform.SetParent(avatar.Root.transform);
            var otherMenu = other.AddComponent<AvatarDoc>();
            otherMenu.AddEntry(EntryKind.Toggle, "Glasses").objects.Add(new ObjectTarget { target = avatar.Glasses });
            var plan = AvatarPlanCache.GetFor(menu);

            var text = SummaryBuilder.Build(plan, menu, SummaryFormat.Plain);

            Assert.That(text, Does.Contain("Hat"));
            Assert.That(text, Does.Not.Contain("Glasses"));
        }

        [Test]
        public void SummarySkipsHiddenItemsAndMarksPlatformLimitedOnes()
        {
            var menu = avatar.AddMenu();
            menu.installPath = "Old";
            var hidden = menu.AddEntry(EntryKind.Toggle, "HiddenHat");
            hidden.objects.Add(new ObjectTarget { target = avatar.Hat });
            var plan = AvatarPlanCache.GetFor(menu);
            var (_, byId) = TreeRow.Build(plan, menu);
            var oldRow = byId.Values.First(r => r.Kind == RowKind.Existing && r.Name == "Old");
            MenuEditing.SetHidden(menu, oldRow, true);
            AvatarPlanCache.Invalidate();
            plan = AvatarPlanCache.GetFor(menu);

            var hiddenText = SummaryBuilder.Build(plan, menu, SummaryFormat.Plain);
            Assert.That(hiddenText, Does.Contain(L.Text("ad.summary.menu.none")), "the whole \"Old\" submenu is hidden, so nothing of this component's shows in the menu tree");

            var otherGo = new GameObject("Limited");
            otherGo.transform.SetParent(avatar.Root.transform);
            var limitedMenu = otherGo.AddComponent<AvatarDoc>();
            var extras = limitedMenu.AddEntry(EntryKind.Submenu, "Extras");
            var glasses = limitedMenu.AddEntry(EntryKind.Toggle, "Glasses", extras.id);
            glasses.objects.Add(new ObjectTarget { target = avatar.Glasses });
            glasses.platform = EntryPlatform.PCOnly;
            AvatarPlanCache.Invalidate();
            var limitedPlan = AvatarPlanCache.GetFor(limitedMenu);

            var limitedText = SummaryBuilder.Build(limitedPlan, limitedMenu, SummaryFormat.Plain);
            Assert.That(limitedText, Does.Contain("Glasses (" + L.Text("ad.ui.chip.pc_only") + ")"));
            Assert.That(limitedText, Does.Contain("slots on " + L.Text("ad.ui.platform.name.pc")));
        }

        [Test]
        public void MarkdownEscapesPipesInNames()
        {
            var menu = avatar.AddMenu();
            var entry = menu.AddEntry(EntryKind.Toggle, "Hat");
            entry.objects.Add(new ObjectTarget { target = avatar.Hat });
            entry.parameterName = "Weird|Name";
            var plan = AvatarPlanCache.GetFor(menu);

            var text = SummaryBuilder.Build(plan, menu, SummaryFormat.Markdown);

            Assert.That(text, Does.Contain("Weird\\|Name"));
        }

        [Test]
        public void SummaryMarksUnsyncedParametersAsLocal()
        {
            var menu = avatar.AddMenu();
            var entry = menu.AddEntry(EntryKind.Toggle, "Hat");
            entry.objects.Add(new ObjectTarget { target = avatar.Hat });
            entry.synced = false;
            var plan = AvatarPlanCache.GetFor(menu);

            var plainText = SummaryBuilder.Build(plan, menu, SummaryFormat.Plain);
            var markdownText = SummaryBuilder.Build(plan, menu, SummaryFormat.Markdown);

            Assert.That(plainText, Does.Contain(L.Text("ad.summary.local")));
            Assert.That(markdownText, Does.Contain("| " + L.Text("ad.summary.local") + " |"));
        }

        [Test]
        public void SummaryNamesVrcFuryWhenAPresetUsesItsGlobalParameter()
        {
            Assume.That(VrcFuryTestHelper.VrcFuryType, Is.Not.Null);
            var furyGo = new GameObject("Fury");
            furyGo.transform.SetParent(avatar.Root.transform);
            var toggle = VrcFuryTestHelper.AddToggle(furyGo, "Fury/Toggle");
            VrcFuryTestHelper.ConfigureToggle(toggle, t =>
            {
                VrcFuryTestHelper.SetField(t, "useGlobalParam", true);
                VrcFuryTestHelper.SetField(t, "globalParam", "MyGlobalParam");
            });

            var menu = avatar.AddMenu();
            var preset = menu.AddEntry(EntryKind.Preset, "Preset");
            preset.presetParameters.Add(new PresetParameter { name = "MyGlobalParam", value = 1f });
            var plan = AvatarPlanCache.GetFor(menu);

            var text = SummaryBuilder.Build(plan, menu, SummaryFormat.Plain);

            Assert.That(text, Does.Contain(L.Text("ad.summary.needs.vrcfury")));
        }

        [Test]
        public void SummaryCountsDeclarationsAndListsRemovals()
        {
            var list = avatar.ExpressionParameters.parameters.ToList();
            list.Add(new VRCExpressionParameters.Parameter { name = "HatParam", valueType = VRCExpressionParameters.ValueType.Bool, saved = true, networkSynced = true });
            avatar.ExpressionParameters.parameters = list.ToArray();
            avatar.ExpressionsMenu.controls.Add(TestAvatar.ToggleControl("Hat", "HatParam"));

            var menu = avatar.AddMenu();
            menu.parameters.Add(ParameterDeclaration.Create("Prefab/Shine", PlannedType.Float));
            menu.menuEdits.Add(new MenuEdit { id = "hat-edit", source = new System.Collections.Generic.List<string> { "Hat" }, remove = true });
            AvatarPlanCache.Invalidate();
            var plan = AvatarPlanCache.GetFor(menu);

            var text = SummaryBuilder.Build(plan, menu, SummaryFormat.Plain);

            Assert.That(text, Does.Contain("Adds 8 synced bits. AvatarDoc uses " + plan.AvatarDoctorBits + " synced bits on this avatar in total."),
                "a declaration costs its bits like any other parameter");
            Assert.That(text, Does.Contain(L.Text("ad.summary.removed")));
            Assert.That(text, Does.Contain("Hat"));
            Assert.That(text, Does.Contain("HatParam is freed (1 synced bit)"));
        }
    }
}
