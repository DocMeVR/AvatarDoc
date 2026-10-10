#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Text the code puts together from pieces (a list, a note in brackets, a quoted name, a table header) takes its
    /// punctuation and words from the language tables, so a Japanese or Chinese view never shows an English ", " or
    /// "(" between its own words. Every language here is a test table.
    /// </summary>
    public class CodeBuiltWordsTests
    {
        static readonly Dictionary<string, string> Japanese = new Dictionary<string, string>
        {
            { "ad.ui.list.separator", "、" },
            { "ad.ui.aside", "{0}（{1}）" },
            { "ad.ui.brackets", "（{0}）" },
            { "ad.ui.quoted", "「{0}」" },
            { "ad.ui.labelled", "{0}：{1}" },
            { "ad.summary.parameters.name", "名前" },
            { "ad.summary.parameters.type", "型" },
            { "ad.summary.parameters.notes", "備考" },
            { "ad.summary.parameters.synced", "同期" },
            { "ad.ui.blend.unit", "秒" },
        };

        TestAvatar avatar;
        AvatarDoc menu;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
            menu = avatar.AddMenu();
        }

        [TearDown]
        public void TearDown()
        {
            try { avatar.Dispose(); }
            finally
            {
                AvatarPlanCache.Invalidate();
                PreviewState.ClearAll();
            }
        }

        MenuEntry Toggle(string name, GameObject target)
        {
            var entry = menu.AddEntry(EntryKind.Toggle, name);
            entry.objects.Add(new ObjectTarget { target = target });
            return entry;
        }

        [Test]
        public void TheHelpersUseTheLanguagesOwnPunctuation()
        {
            Assert.That(L.JoinList(new[] { "a", "b" }), Is.EqualTo("a, b"));
            Assert.That(L.Aside("Hat", "Body"), Is.EqualTo("Hat (Body)"));
            Assert.That(L.Aside("Hat", ""), Is.EqualTo("Hat"), "no note, no brackets");
            Assert.That(L.Brackets("x"), Is.EqualTo("(x)"));
            Assert.That(L.Quoted("Hat"), Is.EqualTo("\"Hat\""));
            Assert.That(L.Labelled("Error", "x"), Is.EqualTo("Error: x"));
            using (L.UseTestLanguage("ja-JP", Japanese))
            {
                Assert.That(L.JoinList(new[] { "a", "b" }), Is.EqualTo("a、b"));
                Assert.That(L.Aside("Hat", "Body"), Is.EqualTo("Hat（Body）"));
                Assert.That(L.Brackets("x"), Is.EqualTo("（x）"));
                Assert.That(L.Quoted("Hat"), Is.EqualTo("「Hat」"));
                Assert.That(L.Labelled("Error", "x"), Is.EqualTo("Error：x"));
            }
        }

        [Test]
        public void TheBuildsLineListsWithTheLanguagesSeparator()
        {
            var hat = Toggle("Hat", avatar.Hat);
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            var english = WhatThisBuildsModel.Describe(view.Plan, view.Tree.RowFor(hat.id));
            Assert.That(english, Does.Contain(", "), "a toggle builds a control, a bit and a layer");
            using (L.UseTestLanguage("ja-JP", Japanese))
            {
                var plan = AvatarPlanCache.GetFor(menu);
                var line = WhatThisBuildsModel.Describe(plan, view.Tree.RowFor(hat.id));
                Assert.That(line, Does.Contain("、").And.Not.Contain(", "));
            }
        }

        [Test]
        public void ConditionGroupsUseTheLanguagesBrackets()
        {
            var hat = Toggle("Hat", avatar.Hat);
            var trigger = menu.AddEntry(EntryKind.Trigger, "Ears");
            trigger.objects.Add(new ObjectTarget { target = avatar.Glasses });
            trigger.when = new List<ConditionGroup>
            {
                new ConditionGroup { all = new List<WhenCondition>
                {
                    new WhenCondition { kind = WhenConditionKind.Toggle, sourceToggleId = hat.id, sourceToggleOn = true },
                    new WhenCondition { kind = WhenConditionKind.Contact, contactAnchor = avatar.Glasses.transform },
                } },
                new ConditionGroup { all = new List<WhenCondition>
                {
                    new WhenCondition { kind = WhenConditionKind.Parameter, sourceParameter = "GestureLeft", condition = ParameterCondition.Equals, conditionValue = 3f },
                } },
            };
            using (L.UseTestLanguage("ja-JP", Japanese))
            {
                var sentence = ConditionLogic.Sentence(trigger, AvatarPlanCache.GetFor(menu).Tree);
                Assert.That(sentence, Does.Contain("（").And.Contain("）").And.Not.Contain("(").And.Not.Contain(")"), sentence);
            }
        }

        [Test]
        public void TheSummaryTakesItsHeadersAndBracketsFromTheLanguage()
        {
            var extras = menu.AddEntry(EntryKind.Submenu, "Extras");
            var glasses = menu.AddEntry(EntryKind.Toggle, "Glasses", extras.id);
            glasses.objects.Add(new ObjectTarget { target = avatar.Glasses });
            glasses.platform = EntryPlatform.PCOnly;
            var english = SummaryBuilder.Build(AvatarPlanCache.GetFor(menu), menu, SummaryFormat.Markdown);
            Assert.That(english, Does.Contain("| Name | Type | ").And.Contain(" | Notes |"));
            using (L.UseTestLanguage("ja-JP", Japanese))
            {
                var plan = AvatarPlanCache.GetFor(menu);
                var markdown = SummaryBuilder.Build(plan, menu, SummaryFormat.Markdown);
                Assert.That(markdown, Does.Contain("| 名前 | 型 | ").And.Contain(" | 備考 |"), "the table's headers");
                Assert.That(markdown, Does.Not.Contain("| Name |"));
                var plain = SummaryBuilder.Build(plan, menu, SummaryFormat.Plain);
                Assert.That(plain, Does.Contain("Glasses（" + L.Text("ad.ui.chip.pc_only") + "）"), "a platform note");
                Assert.That(plain, Does.Contain("Extras（"), "the slot count of a submenu");
            }
        }

        [Test]
        public void TheParityListUsesTheLanguagesWords()
        {
            Toggle("Hat", avatar.Hat);
            var english = PlatformParity.ListOf(avatar.Root.transform);
            Assert.That(english.Last(), Does.EndWith(" (Bool, synced)"));
            using (L.UseTestLanguage("ja-JP", Japanese))
                Assert.That(PlatformParity.ListOf(avatar.Root.transform).Last(), Does.EndWith("（Bool、同期）"));
        }

        [Test]
        public void NamesInAMessageAreQuotedAndListedTheLanguagesWay()
        {
            for (int i = 0; i < FollowPlan.MaxClaims + 2; i++)
                menu.AddEntry(EntryKind.Toggle, "Claim " + i).follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform });
            using (L.UseTestLanguage("ja-JP", Japanese))
            {
                var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
                var message = tree.Messages.Single(m => m.Key == "ad.error.follow_too_many_sources");
                Assert.That(message.Args[3], Is.EqualTo("「Claim " + FollowPlan.MaxClaims + "」、「Claim " + (FollowPlan.MaxClaims + 1) + "」"));
            }
        }

        [Test]
        public void TheBlendTimeUnitFollowsTheLanguage()
        {
            var hat = Toggle("Hat", avatar.Hat);
            using (L.UseTestLanguage("ja-JP", Japanese))
            {
                var view = new AvatarDoctorView(new SerializedObject(menu), menu);
                view.Tree.SelectEntry(hat.id);
                var unit = view.Detail.Q<FloatField>("ad-blend").parent.Q<Label>(className: "ad-suffix");
                Assert.That(unit.text, Is.EqualTo("秒"));
            }
        }
    }
}
#endif
