#if DOCME_NDMF && DOCME_VRCSDK3A
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Plan 17 task 10 part B: the product bugs the reference pages found.</summary>
    public class DocsFixesTests
    {
        TestAvatar avatar;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            AvatarPlanCache.Invalidate();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            AvatarPlanCache.Invalidate();
        }

        AvatarDoctorView View(AvatarDoc menu) => new AvatarDoctorView(new SerializedObject(menu), menu, false, new MemoryViewPrefs());

        MenuEntry Member(AvatarDoc menu, string name, string group, int value = 0)
        {
            var entry = menu.AddEntry(EntryKind.Toggle, name);
            entry.exclusiveGroup = group;
            entry.groupValue = value;
            return entry;
        }

        public static System.Collections.Generic.Dictionary<string, string> Real(string language, params string[] keys)
        {
            var text = File.ReadAllText(Path.GetFullPath(L.TablePath(language)));
            var texts = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var key in keys)
            {
                var match = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                Assert.That(match.Success, Is.True, key + " is in the " + language + " table");
                texts[key] = Regex.Unescape(match.Groups[1].Value);
            }
            return texts;
        }

        static string English(string key)
        {
            var text = File.ReadAllText(Path.GetFullPath(L.TablePath("en-US")));
            var match = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            Assert.That(match.Success, Is.True, key + " is in the English table");
            return Regex.Unescape(match.Groups[1].Value);
        }

        [Test]
        public void AGroupMemberHasNoCustomNameAndSaysItUsesTheGroupsParameter()
        {
            var menu = avatar.AddMenu();
            var first = Member(menu, "Red", "Colours");
            Member(menu, "Blue", "Colours");
            first.parameterName = "Leftover";
            var view = View(menu);

            view.Tree.SelectEntry(first.id);

            Assert.That(view.Detail.Q<Toggle>("ad-parameter-custom"), Is.Null, "the build ignores a member's own parameter name");
            Assert.That(view.Detail.Q<TextField>("ad-parameter-name-field"), Is.Null);
            Assert.That(view.Detail.Q<FloatField>("ad-parameter-value"), Is.Null, "and its parameter value");
            var note = view.Detail.Q<Label>("ad-parameter-group-note");
            Assert.That(note, Is.Not.Null);
            Assert.That(note.text, Is.EqualTo(L.Text("ad.ui.parameter.group_note")));
            Assert.That(note.text, Is.EqualTo("Members use the group's parameter."));
        }

        [Test]
        public void AToggleOutsideAGroupKeepsItsCustomName()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            var view = View(menu);

            view.Tree.SelectEntry(hat.id);

            Assert.That(view.Detail.Q<Toggle>("ad-parameter-custom"), Is.Not.Null);
            Assert.That(view.Detail.Q<Label>("ad-parameter-group-note"), Is.Null);
        }

        [Test]
        public void AGroupMembersValueIsAFieldThatTheBuildReads()
        {
            var menu = avatar.AddMenu();
            var first = Member(menu, "Red", "Colours");
            var second = Member(menu, "Blue", "Colours");
            var view = View(menu);
            view.Tree.SelectEntry(second.id);

            var field = view.Detail.Q<IntegerField>("ad-group-value");
            Assert.That(field, Is.Not.Null, "a member shows the number its group's parameter takes");
            Assert.That(field.label, Is.EqualTo(L.Text("ad.ui.group.value")));
            Assert.That(field.tooltip, Is.EqualTo(L.Text("ad.ui.group.value.tip")));
            LayoutProbe.Measure(view, () => field.value = 5);

            Assert.That(second.groupValue, Is.EqualTo(5), "the entry holds the typed value");
            var binding = view.Plan.Plan.Bindings.Single(b => b.Key.Entry.id == second.id).Value;
            Assert.That(binding.Value, Is.EqualTo(5f), "the plan uses it");
            Assert.That(first.groupValue, Is.EqualTo(0));
        }


        [Test]
        public void TheValueConflictMessagePointsAtTheButton()
        {
            Assert.That(English("ad.error.group_value_conflict"), Does.Contain(English("ad.ui.group.number")));
        }

        [Test]
        public void TheValueConflictBoxOffersToGiveEachMemberItsOwnValueInOneUndoStep()
        {
            var menu = avatar.AddMenu();
            var first = Member(menu, "Red", "Colours", 1);
            var second = Member(menu, "Blue", "Colours", 1);
            var third = Member(menu, "Green", "Colours", 2);
            var view = View(menu);
            var conflict = view.Plan.Messages.Single(m => m.Key == "ad.error.group_value_conflict");
            view.Tree.SelectEntry(conflict.EntryIds.Single());

            var button = view.Detail.Q<Button>("ad-group-number");
            Assert.That(button, Is.Not.Null, "the error box carries the way out");
            Assert.That(button.text, Is.EqualTo(L.Text("ad.ui.group.number")));
            Assert.That(button.tooltip, Is.EqualTo(L.Text("ad.ui.group.number.tip")));
            Assert.That(button.parent.ClassListContains("ad-message-actions"), Is.True, "in the row right under the error box");

            Undo.IncrementCurrentGroup();
            UiTestEvents.SimulateClick(button);

            var values = new[] { first, second, third }.Select(e => e.groupValue).ToList();
            Assert.That(values.Distinct().Count(), Is.EqualTo(3), "three members, three values");
            Assert.That(values.All(v => v > 0), Is.True);
            Assert.That(view.Plan.Messages.Any(m => m.Key == "ad.error.group_value_conflict"), Is.False, "the error is gone");
            Assert.That(view.Detail.Q<Button>("ad-group-number"), Is.Null, "and so is its button");

            Undo.PerformUndo();

            Assert.That(new[] { first, second, third }.Select(e => e.groupValue), Is.EqualTo(new[] { 1, 1, 2 }), "one undo step puts all three back");
        }

        [Test]
        public void ALockedComponentRefusesToNumberTheMembers()
        {
            var menu = avatar.AddMenu();
            var first = Member(menu, "Red", "Colours", 1);
            var second = Member(menu, "Blue", "Colours", 1);
            var view = View(menu);
            view.Tree.SelectEntry(view.Plan.Messages.Single(m => m.Key == "ad.error.group_value_conflict").EntryIds.Single());
            var button = view.Detail.Q<Button>("ad-group-number");
            Assert.That(button, Is.Not.Null);
            menu.locked = true;

            UiTestEvents.SimulateClick(button);

            Assert.That((first.groupValue, second.groupValue), Is.EqualTo((1, 1)));
        }

        [Test]
        public void AGroupMemberWithTheConflictBoxFitsAt300And360InEveryLanguage()
        {
            var keys = new[] { "ad.ui.group.number", "ad.ui.group.value", "ad.ui.parameter.group_note", "ad.ui.group.settings", "ad.ui.message.learn_more", "ad.error.group_value_conflict", "ad.error.trigger_too_complex", "ad.error.bits_exceeded" };
            var ja = Real("ja-JP", keys);
            var zh = Real("zh-Hans", keys);
            foreach (var language in new[] { ("en-US", (System.Collections.Generic.Dictionary<string, string>)null), ("ja-JP", ja), ("zh-Hans", zh) })
            foreach (float width in new[] { 300f, 360f })
            {
                using (L.UseTestLanguage(language.Item1, language.Item2))
                {
                    var menu = avatar.AddMenu();
                    Member(menu, "Red", "Colours", 1);
                    Member(menu, "Blue", "Colours", 1);
                    var view = View(menu);
                    view.Tree.SelectEntry(view.Plan.Messages.Single(m => m.Key == "ad.error.group_value_conflict").EntryIds.Single());
                    LayoutProbe.Measure(view, () =>
                    {
                        LayoutProbe.Validate(view);
                        var fold = view.Detail.Q<AdvancedFold>();
                        if (!fold.IsOpen) fold.ToggleOpen();
                        LayoutProbe.Validate(view);
                        Assert.That(view.Detail.Q<Button>("ad-group-number"), Is.Not.Null);
                        PanelAudit.Audit(view.Detail, view.Detail.worldBound.xMax, PanelAuditTests.StandardAllowList());
                    }, width);
                    Object.DestroyImmediate(menu);
                    AvatarPlanCache.Invalidate();
                }
            }
        }


        string BuildsLineOfOverride(params string[] clipNames)
        {
            foreach (var name in clipNames) avatar.AddFxClip(name, TestAvatar.ShapeClip(name, "Body", "Blush", 0f, 100f));
            var menu = avatar.AddMenu();
            var rule = menu.AddEntry(EntryKind.Override, "No blush");
            rule.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 0f });
            var view = View(menu);
            view.Tree.SelectEntry(rule.id);
            return view.Detail.Q<Label>("ad-builds-line").text;
        }

        [Test]
        public void AnOverrideSaysHowManyClipsItRewrites()
        {
            Assert.That(BuildsLineOfOverride("Smile", "Frown"), Is.EqualTo(string.Format(L.Text("ad.ui.builds.line"), "rewrites 2 clips")));
        }

        [Test]
        public void AnOverrideOfOneClipSaysClipInTheSingular()
        {
            Assert.That(BuildsLineOfOverride("Smile"), Is.EqualTo(string.Format(L.Text("ad.ui.builds.line"), "rewrites 1 clip")));
        }

        [Test]
        public void AnOverrideNothingAnimatesSaysSoInsteadOfAskingForATarget()
        {
            var line = BuildsLineOfOverride();
            Assert.That(line, Is.EqualTo(L.Text("ad.ui.builds.nothing.no_clips")));
            Assert.That(line, Is.Not.EqualTo(L.Text("ad.ui.builds.nothing.empty")));
        }

        [Test]
        public void AnOverrideWithoutATargetStillAsksForOne()
        {
            var menu = avatar.AddMenu();
            var rule = menu.AddEntry(EntryKind.Override, "Empty");
            var view = View(menu);
            view.Tree.SelectEntry(rule.id);
            Assert.That(view.Detail.Q<Label>("ad-builds-line").text, Is.EqualTo(L.Text("ad.ui.builds.nothing.empty")));
        }


        string[] DeclaredByEach(bool synced)
        {
            var controller = new AnimatorController { name = "FX" };
            try
            {
                controller.AddParameter("Declared", AnimatorControllerParameterType.Bool);
                controller.AddParameter("Loose", AnimatorControllerParameterType.Float);
                var menu = avatar.AddMenu();
                var declaration = ParameterDeclaration.Create("Declared", PlannedType.Bool);
                declaration.synced = synced;
                menu.parameters.Add(declaration);
                menu.animators.Add(AnimatorMerge.Create(controller));
                var view = View(menu);
                view.Tree.SelectKey(view.Tree.RowsOfKind(RowKind.Animator).Single().Key);
                var rows = view.Detail.Q<AnimatorPanel>().Q<VisualElement>("ad-animator-parameters").Children().ToList();
                return new[] { "Declared", "Loose" }.Select(n => rows.Single(r => r.Q<Label>("ad-animator-param-name").text == n).Q<Label>("ad-animator-param-declared").text).ToArray();
            }
            finally
            {
                Object.DestroyImmediate(controller);
            }
        }

        [Test]
        public void ADeclaredSyncedParameterIsNotCalledNotSynced()
        {
            var texts = DeclaredByEach(true);
            Assert.That(texts[0], Is.EqualTo(L.Text("ad.ui.animator.declared_synced")));
            Assert.That(texts[1], Is.EqualTo(L.Text("ad.ui.animator.not_synced")), "a parameter nothing declares still is");
        }

        [Test]
        public void ADeclaredLocalParameterSaysItIsDeclaredAndNotSynced()
        {
            Assert.That(DeclaredByEach(false)[0], Is.EqualTo(L.Text("ad.ui.animator.declared_local")));
        }


        [Test]
        public void TheNoneOptionTooltipSaysItShowsTheHiddenPieces()
        {
            var tip = English("ad.ui.outfit.none_option.tip");
            Assert.That(tip, Does.Contain("shows"));
            Assert.That(tip, Does.Not.Contain("hides"));
            Assert.That(L.Text("ad.ui.outfit.none_option.tip"), Is.EqualTo(tip));
        }


        [Test]
        public void TheShapesConflictNamesDifferentLayersInEveryLanguage()
        {
            foreach (var (code, wanted, unwanted) in new[] { ("en-US", "different layers", "different toggles"), ("ja-JP", "別々のレイヤー", "別々のトグル"), ("zh-Hans", "不同的层", "不同的开关") })
            {
                var text = File.ReadAllText(Path.GetFullPath(L.TablePath(code)));
                var match = Regex.Match(text, "\"ad.warn.shapes_conflict\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                Assert.That(match.Success, Is.True, code);
                Assert.That(match.Groups[1].Value, Does.Contain(wanted), code);
                Assert.That(match.Groups[1].Value, Does.Not.Contain(unwanted), code);
            }
        }
    }
}
#endif
