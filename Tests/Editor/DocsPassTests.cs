#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Counts with a real singular, translated choices, names for the controls the docs point at, Reset preview,
    /// + Parameter, the Add Component paths and the Convert window's rows: each on the real view, the real window
    /// or the real summary, wherever the creator sees it.
    /// </summary>
    public class DocsPassTests
    {
        TestAvatar avatar;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        AvatarDoctorView View(AvatarDoc menu, IViewPrefs prefs = null) => new AvatarDoctorView(new SerializedObject(menu), menu, false, prefs ?? new MemoryViewPrefs());

        MenuEntry Toggle(AvatarDoc menu, string name, GameObject target)
        {
            var entry = menu.AddEntry(EntryKind.Toggle, name);
            entry.objects.Add(new ObjectTarget { target = target });
            return entry;
        }

        // ---- every count reads right at one -----------------------------------------------------------------

        [Test]
        public void AToggleSaysItCostsOneBitInTheSingular()
        {
            var menu = avatar.AddMenu();
            var hat = Toggle(menu, "Hat", avatar.Hat);
            var smile = menu.AddEntry(EntryKind.Radial, "Smile");
            smile.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush" });
            var view = View(menu);

            view.Tree.SelectEntry(hat.id);
            Assert.That(view.Detail.Q<Label>("ad-parameter-bits").text, Is.EqualTo("Bool, 1 bit"));

            view.Tree.SelectEntry(smile.id);
            Assert.That(view.Detail.Q<Label>("ad-parameter-bits").text, Is.EqualTo("Float, 8 bits"));
        }

        [Test]
        public void ADeclaredBoolCostsOneBitInTheTableAndTheTree()
        {
            var menu = avatar.AddMenu();
            menu.parameters.Add(ParameterDeclaration.Create("Sparkles", PlannedType.Bool));
            var view = View(menu);

            var row = view.Tree.RowsOfKind(RowKind.Parameter).Single();
            var element = view.Tree.MakeRow();
            view.Tree.BindRowContent(element, row);
            Assert.That(element.Query<Label>().ToList().Select(l => l.text), Does.Contain("Bool, 1 bit"), "the tree row's note");

            view.Tree.RevealKey(row.Key);
            Assert.That(view.Detail.Q<Label>("ad-parameters-total").text, Is.EqualTo("Total 1 bit"));
            Assert.That(view.Detail.Q("ad-parameter-row-0").Query<Label>().ToList().Select(l => l.text), Does.Contain("1 bit"), "the table row's cost");

            menu.parameters.Add(ParameterDeclaration.Create("Hue", PlannedType.Float));
            AvatarPlanCache.Invalidate();
            view.Refresh();
            Assert.That(view.Detail.Q<Label>("ad-parameters-total").text, Is.EqualTo("Total 9 bits"));
        }

        [Test]
        public void AnOverrideOneClipMovesSaysClipInTheSingular()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var menu = avatar.AddMenu();
            var rule = menu.AddEntry(EntryKind.Override, "No blush");
            rule.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 0f });
            var view = View(menu);

            view.Tree.SelectEntry(rule.id);

            Assert.That(view.Detail.Q("ad-override-usage").Q<Label>(className: "ad-section").text, Is.EqualTo("Animated in 1 clip"));
            Assert.That(view.Detail.Q<TargetListView>("ad-blendshapes").RowAt(0).Q<Label>(className: "ad-chip-usage").text, Is.EqualTo("1 clip"), "the row's own chip");
        }

        [Test]
        public void OneBitOverTheLimitIsSaidInTheSingular()
        {
            var menu = avatar.AddMenu();
            for (int i = 0; i < 31; i++)
                menu.AddEntry(EntryKind.Radial, "R" + i).blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big" });
            var plan = AvatarPlanCache.GetFor(menu);
            int toggles = VRCExpressionParameters.MAX_PARAMETER_COST + 1 - plan.AvatarBits - plan.AvatarDoctorBits;
            Assert.That(toggles, Is.GreaterThan(0));
            for (int i = 0; i < toggles; i++) Toggle(menu, "T" + i, avatar.Hat);
            AvatarPlanCache.Invalidate();
            plan = AvatarPlanCache.GetFor(menu);
            Assert.That(plan.AvatarBits + plan.AvatarDoctorBits, Is.EqualTo(VRCExpressionParameters.MAX_PARAMETER_COST + 1));

            var view = View(menu);

            Assert.That(view.Header.Q<Label>("ad-legend-free").text, Is.EqualTo("1 bit over the limit"));
        }

        [Test]
        public void TheWelcomeCardOffersToConvertOneItem()
        {
            Assume.That(VrcFuryTestHelper.VrcFuryType, Is.Not.Null);
            var menu = avatar.AddMenu();
            var go = new GameObject("Hat Toggle");
            go.transform.SetParent(avatar.Root.transform);
            var toggle = VrcFuryTestHelper.AddToggle(go, "Hat");
            VrcFuryTestHelper.AddAction(toggle, VrcFuryReader.ObjectToggleActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "obj", avatar.Hat);
                VrcFuryTestHelper.SetField(a, "mode", 0);
            });

            var view = View(menu);

            Assert.That(view.Tree.Q<Button>("ad-welcome-convert").text, Is.EqualTo("Convert 1 item from VRCFury"));
        }

        [Test]
        public void TheConvertWindowSummariesUseARealSingular()
        {
            var controller = new AnimatorController { name = "Sparkles FX" };
            controller.AddLayer("Sparkles");
            var asset = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            asset.controls = new List<VRCExpressionsMenu.Control> { TestAvatar.ToggleControl("Sparkles", "Sparkles") };
            try
            {
                var merge = new ImportCandidate { MergeDraft = AnimatorMerge.Create(controller) };
                Assert.That(ImportWindow.Summary(merge), Is.EqualTo("1 layer stays merged"));

                var takeover = new ImportCandidate { MenuAssetDraft = MenuAssetInstall.Create(asset) };
                takeover.ParameterDrafts.Add(ParameterDeclaration.Create("Sparkles", PlannedType.Bool));
                Assert.That(ImportWindow.Summary(takeover), Is.EqualTo("1 parameter, 1 declaration"));

                var shapes = new ImportCandidate { ShapesDraft = new ShapesDraft { Piece = avatar.Hat } };
                shapes.ShapesDraft.Follow.Add(new ShapeFollow { sourceMesh = avatar.Body, sourceShape = "Blush" });
                shapes.ShapesDraft.WhileOn.Add(new ShapeWhileOn { mesh = avatar.Body, shape = "Eye_Big" });
                Assert.That(ImportWindow.Summary(shapes), Is.EqualTo("1 follow, 1 while on"));
                shapes.ShapesDraft.Follow.Add(new ShapeFollow { sourceMesh = avatar.Body, sourceShape = "Eye_Big" });
                Assert.That(ImportWindow.Summary(shapes), Is.EqualTo("2 follows, 1 while on"));

                var draft = MenuEntry.Create(EntryKind.Toggle, "Hat");
                draft.objects.Add(new ObjectTarget { target = avatar.Hat });
                draft.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush" });
                draft.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big" });
                Assert.That(ImportWindow.Summary(new ImportCandidate { Draft = draft }), Is.EqualTo("1 object, 2 blend shapes"));
            }
            finally
            {
                Object.DestroyImmediate(controller);
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void TheSummaryCountsInTheSingular()
        {
            var controller = new AnimatorController { name = "Sparkles FX" };
            controller.AddLayer("Sparkles");
            var asset = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            asset.name = "Extras";
            asset.controls = new List<VRCExpressionsMenu.Control> { TestAvatar.ToggleControl("Sparkles", "Sparkles") };
            try
            {
                var menu = avatar.AddMenu();
                Toggle(menu, "Hat", avatar.Hat);
                menu.animators.Add(AnimatorMerge.Create(controller));
                menu.menuAssets.Add(MenuAssetInstall.Create(asset));
                menu.parameters.Add(ParameterDeclaration.Create("Sparkles", PlannedType.Bool));
                var shapes = avatar.Glasses.AddComponent<AvatarDocShapes>();
                shapes.follow.Add(new ShapeFollow { sourceMesh = avatar.Body, sourceShape = "Blush" });
                shapes.whileOn.Add(new ShapeWhileOn { mesh = avatar.Body, shape = "Eye_Big" });
                AvatarPlanCache.Invalidate();
                var plan = AvatarPlanCache.GetFor(menu);

                var text = SummaryBuilder.Build(plan, menu, SummaryFormat.Plain);

                Assert.That(plan.AvatarDoctorBits, Is.EqualTo(2), "the Hat toggle and the declared Sparkles");
                Assert.That(text, Does.Contain("Adds 2 synced bits. AvatarDoc uses 2 synced bits on this avatar in total."));
                Assert.That(text, Does.Contain("Sparkles FX: 1 layer"));
                Assert.That(text, Does.Contain("Extras: 1 control"));
                Assert.That(text, Does.Contain("Sparkles (Bool, 1 bit)"));
                Assert.That(text, Does.Contain("Glasses: 1 follow, 1 while on"));
            }
            finally
            {
                Object.DestroyImmediate(controller);
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void TheSummarySaysOneSyncedBitInTheSingular()
        {
            var menu = avatar.AddMenu();
            Toggle(menu, "Hat", avatar.Hat);
            var plan = AvatarPlanCache.GetFor(menu);

            var text = SummaryBuilder.Build(plan, menu, SummaryFormat.Plain);

            Assert.That(text, Does.Contain("Adds 1 synced bit. AvatarDoc uses 1 synced bit on this avatar in total."));
            using (L.UseTestLanguage("ja-JP", null))
                Assert.That(SummaryBuilder.Build(plan, menu, SummaryFormat.Plain), Does.Contain(L.Text("ad.summary.bits.adds.one").Replace("{0}", "1") + L.Text("ad.summary.bits.total.one").Replace("{0}", "1")),
                    "Japanese joins the two sentences without a space");
        }

        // ---- dropdown choices come from the language table ------------------------------------------------

        static readonly string[] ConditionKeys =
        {
            "ad.ui.trigger.condition.is_true", "ad.ui.trigger.condition.is_false", "ad.ui.trigger.condition.equals",
            "ad.ui.trigger.condition.not_equals", "ad.ui.trigger.condition.greater", "ad.ui.trigger.condition.less",
        };

        [Test]
        public void TheConditionChoicesComeFromTheTable()
        {
            var menu = avatar.AddMenu();
            var flat = menu.AddEntry(EntryKind.Trigger, "Thumbs up blush");
            flat.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush" });
            flat.source = TriggerSource.Parameter;
            flat.sourceParameter = "GestureRight";
            flat.condition = ParameterCondition.Equals;
            flat.conditionValue = 7f;
            var grouped = menu.AddEntry(EntryKind.Trigger, "Away");
            grouped.objects.Add(new ObjectTarget { target = avatar.Glasses });
            grouped.when = new List<ConditionGroup>
            {
                new ConditionGroup { all = new List<WhenCondition> { new WhenCondition { kind = WhenConditionKind.Parameter, sourceParameter = "AFK", condition = ParameterCondition.Less } } },
            };

            foreach (var language in new[] { "en-US", "ja-JP" })
                using (L.UseTestLanguage(language, null))
                {
                    var expected = ConditionKeys.Select(L.Text).ToList();
                    var view = View(menu);

                    view.Tree.SelectEntry(flat.id);
                    var fields = view.Detail.Query<DropdownField>().Where(f => f.name == "ad-trigger-condition" || f.name == "ad-when-comparison").ToList();
                    Assert.That(fields, Is.Not.Empty, "the parameter trigger shows its Condition");
                    foreach (var field in fields)
                    {
                        Assert.That(field.choices, Is.EqualTo(expected), language + ": " + field.name);
                        Assert.That(field.value, Is.EqualTo(L.Text("ad.ui.trigger.condition.equals")), language + ": " + field.name);
                    }

                    view.Tree.SelectEntry(grouped.id);
                    var comparison = view.Detail.Q<DropdownField>("ad-when-comparison");
                    Assert.That(comparison.choices, Is.EqualTo(expected), language);
                    Assert.That(comparison.value, Is.EqualTo(L.Text("ad.ui.trigger.condition.less")), language);

                    if (language != "ja-JP") continue;
                    Assert.That(expected[4], Is.EqualTo("値より上"), "the same words as the sentence above the cards (ad.ui.when.greater)");
                    Assert.That(expected[5], Is.EqualTo("値より下"), "and ad.ui.when.less");
                    Assert.That(L.Text("ad.ui.when.greater"), Does.EndWith("より上"));
                    Assert.That(L.Text("ad.ui.when.less"), Does.EndWith("より下"));
                }

            Assert.That(ConditionKeys.Select(L.Text).Intersect(new[] { "Is True", "Is False", "Not Equals", "Greater", "Less" }), Is.Empty, "no enum name reaches the creator");
        }

        [Test]
        public void TheInstallAsChoicesComeFromTheTable()
        {
            var menu = avatar.AddMenu();
            menu.installMode = InstallMode.AsSubmenu;

            foreach (var language in new[] { "en-US", "zh-Hans" })
                using (L.UseTestLanguage(language, null))
                {
                    var view = View(menu);
                    var mode = view.Header.Q<DropdownField>("ad-install-mode");
                    Assert.That(mode.choices, Is.EqualTo(new[] { L.Text("ad.ui.install.mode.append"), L.Text("ad.ui.install.mode.submenu") }), language);
                    Assert.That(mode.value, Is.EqualTo(L.Text("ad.ui.install.mode.submenu")), language);
                }

            Assert.That(L.Text("ad.ui.install.mode.append"), Is.EqualTo(L.Text("ad.ui.menu_asset.place.append")), "the menu asset's own Place says the same");
            Assert.That(L.Text("ad.ui.install.mode.submenu"), Is.EqualTo(L.Text("ad.ui.menu_asset.place.submenu")));
        }

        [Test]
        public void EveryEnumDropdownIsGivenItsLabels()
        {
            var overloads = typeof(DirectFields).GetMethods().Where(m => m.Name == nameof(DirectFields.EnumChoice)).ToList();
            Assert.That(overloads, Is.Not.Empty);
            foreach (var method in overloads)
                Assert.That(method.GetParameters().Any(p => p.ParameterType == typeof(IReadOnlyList<string>)), Is.True,
                    "an EnumChoice without labels would show Unity's own enum names: " + method);
        }

        // ---- names for the controls the docs point at ----------------------------------------------------

        [Test]
        public void TheEntryPlatformAndTheComponentPlatformHaveTheirOwnNames()
        {
            var menu = avatar.AddMenu();
            var hat = Toggle(menu, "Hat", avatar.Hat);
            var view = View(menu);

            view.Tree.SelectEntry(hat.id);

            Assert.That(view.Detail.Q<DropdownField>("ad-entry-platform"), Is.Not.Null, "the entry's Platform in its Advanced fold");
            Assert.That(view.Header.Q<SegmentedChoice>("ad-platform"), Is.Not.Null, "the component's Platform in Component settings");
            Assert.That(view.Query("ad-platform").ToList().Count, Is.EqualTo(1), "one element per name");
        }

        [Test]
        public void TheExclusiveGroupControlsHaveNames()
        {
            var menu = avatar.AddMenu();
            var jacket = Toggle(menu, "Jacket", avatar.Hat);
            jacket.exclusiveGroup = "Outfits";
            jacket.defaultOn = true;
            var view = View(menu);

            view.Tree.SelectEntry(jacket.id);

            Assert.That(view.Detail.Q<TextField>("ad-group").value, Is.EqualTo("Outfits"));
            Assert.That(view.Detail.Q<TextField>("ad-group").label, Is.EqualTo(L.Text("ad.ui.group")));
            Assert.That(view.Detail.Q<Button>("ad-group-pick"), Is.Not.Null);
            Assert.That(view.Detail.Q<Button>("ad-group-pick").parent, Is.EqualTo(view.Detail.Q<TextField>("ad-group").parent), "the arrow sits beside the field");
            Assert.That(view.Detail.Q<Toggle>("ad-default-on").label, Is.EqualTo(L.Text("ad.ui.default_on")));
            Assert.That(view.Detail.Q<Toggle>("ad-default-on").value, Is.True);
        }

        // ---- Reset preview ---------------------------------------------------------------------------------

        [Test]
        public void ResetPreviewClearsEveryAvatarsPreviewAndItsTooltipSaysSo()
        {
            var other = TestAvatar.Create();
            try
            {
                var menu = avatar.AddMenu();
                var hat = Toggle(menu, "Hat", avatar.Hat);
                var otherMenu = other.AddMenu();
                var cap = Toggle(otherMenu, "Cap", other.Hat);
                PreviewState.Set(hat.id, 1f);
                PreviewState.Set(cap.id, 1f);
                var view = View(menu);
                var reset = view.Header.Q<Button>("ad-preview-reset");

                UiTestEvents.SimulateClick(reset);

                Assert.That(PreviewState.TryGet(hat.id, out _), Is.False);
                Assert.That(PreviewState.TryGet(cap.id, out _), Is.False, "the other avatar's preview goes too");
                Assert.That(reset.tooltip, Does.Contain("every avatar"));
                Assert.That(reset.tooltip, Does.Not.Contain("this component"));
            }
            finally
            {
                other.Dispose();
            }
        }

        // ---- + Parameter in the toolbar -----------------------------------------------------------------

        [Test]
        public void PlusParameterDeclaresTheFirstParameterFromTheToolbar()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);
            Assert.That(view.Tree.RowsOfKind(RowKind.ParametersGroup), Is.Empty, "nothing declared yet, so no Parameters row");

            var add = view.Tree.Q<ToolbarButton>("ad-add-parameter");
            Assert.That(add, Is.Not.Null);
            Assert.That(add.text, Is.EqualTo(L.Text("ad.ui.parameters.add")));
            Assert.That(add.tooltip, Is.EqualTo(L.Text("ad.ui.parameters.add.tip")));

            UiTestEvents.SimulateClick(add);

            Assert.That(menu.parameters.Select(p => p.name), Is.EqualTo(new[] { L.Text("ad.ui.parameters.new") }));
            Assert.That(view.Tree.RowsOfKind(RowKind.ParametersGroup).Count, Is.EqualTo(1), "the Parameters row appears");
            Assert.That(view.Tree.Selected?.Kind, Is.EqualTo(RowKind.ParametersGroup), "and is selected");
            Assert.That(view.Detail.Q("ad-parameters-table"), Is.Not.Null, "with the table open beside it");

            UiTestEvents.SimulateClick(add);
            Assert.That(menu.parameters.Count, Is.EqualTo(2), "each press declares one more");
            Assert.That(menu.parameters.Select(p => p.name).Distinct().Count(), Is.EqualTo(2), "under its own name");
        }

        [Test]
        public void PlusParameterIsOneUndoStep()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);
            Undo.IncrementCurrentGroup();

            UiTestEvents.SimulateClick(view.Tree.Q<ToolbarButton>("ad-add-parameter"));
            Assert.That(menu.parameters.Count, Is.EqualTo(1));

            Undo.PerformUndo();
            AvatarPlanCache.Invalidate();
            view.Refresh();

            Assert.That(menu.parameters, Is.Empty, "the declaration is undone");
            Assert.That(view.Tree.RowsOfKind(RowKind.ParametersGroup), Is.Empty, "and the Parameters row goes with it");
        }

        [Test]
        public void PlusParameterIsRefusedWhileLocked()
        {
            var menu = avatar.AddMenu();
            menu.locked = true;
            var view = View(menu);

            Assert.That(view.Tree.Q<ToolbarButton>("ad-add-parameter").enabledSelf, Is.False, "the button is greyed out while the component is locked");
            Assert.That(view.Tree.AddDeclaration(), Is.Null);
            Assert.That(menu.parameters, Is.Empty);

            menu.locked = false;
            view.Refresh();
            Assert.That(view.Tree.Q<ToolbarButton>("ad-add-parameter").enabledSelf, Is.True, "and comes back once it is unlocked");
        }

        [Test]
        public void PlusParameterFollowsPlusMenuAssetAndLeavesSimple()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);

            var names = view.Tree.MoreItems().Select(i => i.NameKey).ToList();
            Assert.That(names.IndexOf("ad.ui.parameters.add"), Is.EqualTo(names.IndexOf("ad.ui.add.menu_asset") + 1), "right after + Menu asset in the priority list");
            var buttons = view.Tree.AddButtons.Select(b => b.name).ToList();
            Assert.That(buttons.IndexOf("ad-add-parameter"), Is.EqualTo(buttons.IndexOf("ad-add-menu-asset") + 1));

            var prefs = new MemoryViewPrefs();
            ViewModes.Save(prefs, ViewMode.Simple);
            var simple = View(menu, prefs);
            Assert.That(simple.Tree.AddButtons.Any(b => b.name == "ad-add-parameter"), Is.False, "an advanced add, so Simple leaves it out");
            Assert.That(simple.Tree.Q<ToolbarButton>("ad-add-parameter").ClassListContains("ad-hidden"), Is.True);
        }

        // ---- Add Component ---------------------------------------------------------------------------------

        [Test]
        public void BothComponentsSitUnderDocMeInAddComponent()
        {
            string PathOf(Type type) => ((AddComponentMenu)type.GetCustomAttributes(typeof(AddComponentMenu), false).Single()).componentMenu;

            Assert.That(PathOf(typeof(AvatarDoc)), Is.EqualTo("DocMe/AvatarDoc"));
            Assert.That(PathOf(typeof(AvatarDocShapes)), Is.EqualTo("DocMe/AvatarDoc Shapes"));
        }

        // ---- the Convert window's rows ----------------------------------------------------------------------

        [Test]
        public void TheConvertWindowStartsEveryNameAtTheLeftInEveryLanguage()
        {
            Assume.That(VrcFuryTestHelper.VrcFuryType, Is.Not.Null);
            foreach (var language in new[] { "en-US", "ja-JP", "zh-Hans" })
                using (L.UseTestLanguage(language, null))
                using (var docs = DocsAvatar.Create())
                {
                    var spec = DocsShots_Guides2.ConvertWindow(docs);
                    try
                    {
                        Assert.That(spec.Width, Is.EqualTo(ImportWindow.OpeningWidth), "measured at the width the window opens at");
                        LayoutProbe.Measure(spec.View, () =>
                        {
                            LayoutProbe.SettleLists(spec.View);
                            var rows = spec.View.Query(className: "ad-import-row").ToList();
                            Assert.That(rows.Count, Is.GreaterThanOrEqualTo(4), language + ": the tools' toggles and the avatar's own items");
                            float? column = null;
                            foreach (var row in rows)
                            {
                                var header = row.Children().First();
                                var box = header.Children().First();
                                var path = header.Q(className: "ad-import-path");
                                var texts = path.Query<Label>().ToList();
                                var name = texts.Last();
                                var chips = header.Query<Label>(className: "ad-import-meta").ToList();
                                string where = language + ", " + name.text + ": ";

                                Assert.That(box.worldBound.width, Is.EqualTo(24f).Within(0.5f), where + "the check box column keeps 24 points");
                                Assert.That(path.worldBound.xMin - box.worldBound.xMax, Is.LessThanOrEqualTo(10.5f), where + "the text starts right after the check box, at " + path.worldBound.xMin);
                                Assert.That(texts[0].worldBound.xMin, Is.EqualTo(path.worldBound.xMin).Within(0.5f), where + "and at the left of its own box");
                                if (texts.Count > 1)
                                    Assert.That(name.worldBound.xMin, Is.EqualTo(texts[0].worldBound.xMax).Within(0.5f), where + "the name follows its dim path");
                                column = column ?? path.worldBound.xMin;
                                Assert.That(path.worldBound.xMin, Is.EqualTo(column.Value).Within(0.5f), where + "every row starts in the same column");
                                Assert.That(chips.Last().worldBound.xMax, Is.EqualTo(header.worldBound.xMax).Within(0.5f), where + "the chips end at the right edge");
                                Assert.That(name.worldBound.xMax, Is.LessThanOrEqualTo(chips.First().worldBound.xMin + 0.5f), where + "the name never runs under the chips");
                            }
                        }, spec.Width);
                    }
                    finally
                    {
                        spec.Release?.Invoke();
                    }
                }
        }
    }
}
#endif
