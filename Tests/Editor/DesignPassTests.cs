#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Phase 12 task 11: the design pass from the creator's real screenshots. One test per finding that can be
    /// measured, against a real, laid out panel (LayoutProbe) wherever position or clipping is what changed.
    /// </summary>
    public class DesignPassTests
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

        AvatarDoctorView View(AvatarDoc menu) => new AvatarDoctorView(new SerializedObject(menu), menu);

        // ---- finding 1: plurals -------------------------------------------------------------------------------

        [Test]
        public void CountHelperPicksTheRealSingularOrPlural()
        {
            Assert.That(UiText.Count(1, "ad.ui.ready.problems.one", "ad.ui.ready.problems"), Is.EqualTo("1 problem"));
            Assert.That(UiText.Count(2, "ad.ui.ready.problems.one", "ad.ui.ready.problems"), Is.EqualTo("2 problems"));
            Assert.That(UiText.Count(0, "ad.ui.ready.problems.one", "ad.ui.ready.problems"), Is.EqualTo("0 problems"));
        }

        /// <summary>Every pair this task's plural fix touched: neither string reads "(s)", and the two texts actually differ.</summary>
        [Test]
        public void EveryTouchedCountStringHasARealPairAndNoParenS()
        {
            var pairs = new[]
            {
                ("ad.ui.ready.problems.one", "ad.ui.ready.problems"),
                ("ad.ui.ready.warnings.one", "ad.ui.ready.warnings"),
                ("ad.ui.memory.avatar.one", "ad.ui.memory.avatar"),
                ("ad.ui.memory.ad.one", "ad.ui.memory.ad"),
                ("ad.ui.other_components.one", "ad.ui.other_components"),
                ("ad.ui.triggers.note.one", "ad.ui.triggers.note"),
                ("ad.ui.overrides.note.one", "ad.ui.overrides.note"),
                ("ad.ui.builds.controls.one", "ad.ui.builds.controls"),
                ("ad.ui.builds.bits.one", "ad.ui.builds.bits"),
                ("ad.ui.builds.layers.one", "ad.ui.builds.layers"),
                ("ad.ui.builds.contacts.one", "ad.ui.builds.contacts"),
                ("ad.ui.builds.physbones.one", "ad.ui.builds.physbones"),
                ("ad.ui.builds.constraints.one", "ad.ui.builds.constraints"),
                ("ad.summary.touch.contacts.one", "ad.summary.touch.contacts"),
                ("ad.summary.touch.physbones.one", "ad.summary.touch.physbones"),
                ("ad.info.shape_set_count.one", "ad.info.shape_set_count"),
            };
            foreach (var (singularKey, pluralKey) in pairs)
            {
                Assert.That(L.Localizer.TryGetLocalizedString(singularKey, out var singularText), Is.True, singularKey + " must exist");
                Assert.That(L.Localizer.TryGetLocalizedString(pluralKey, out var pluralText), Is.True, pluralKey + " must exist");
                Assert.That(singularText, Does.Not.Contain("(s)"), singularKey);
                Assert.That(pluralText, Does.Not.Contain("(s)"), pluralKey);
                Assert.That(singularText, Is.Not.EqualTo(pluralText), singularKey + " and " + pluralKey + " must actually read differently");
            }
        }

        [Test]
        public void BuildsLineUsesARealSingularAtOne()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.Tree.SelectEntry(hat.id);
            var line = view.Detail.Q<Label>("ad-builds-line");
            Assert.That(line.text, Does.Contain("1 menu control"));
            Assert.That(line.text, Does.Not.Contain("control(s)"));
            Assert.That(line.text, Does.Not.Contain("1 menu controls"));
        }

        // ---- finding 4: the finding bar never clips ------------------------------------------------------------

        [Test]
        public void FilterMenuNeverClipsAtTheNarrowestWidths()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            foreach (var width in new[] { 360f, 400f })
            {
                var view = View(menu);
                LayoutProbe.Measure(view, () =>
                {
                    var findBar = view.Tree.Q<VisualElement>("ad-toolbar-find");
                    var filter = view.Tree.Q<ToolbarButton>("ad-tag-filter");
                    Assert.That(filter, Is.Not.Null);
                    Assert.That(filter.worldBound.xMax, Is.LessThanOrEqualTo(findBar.worldBound.xMax + 0.5f),
                        "the filter menu must never clip at " + width + "px");
                    Assert.That(filter.worldBound.width, Is.GreaterThan(30f), "the filter menu keeps its own full width at " + width + "px");
                }, width, 900f);
            }
        }

        // ---- finding 5: one chip rule -------------------------------------------------------------------------

        [Test]
        public void EveryRowChipSharesOneHeight()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            hat.hiddenFromMenu = true;
            var broken = menu.AddEntry(EntryKind.Toggle, "Broken");
            broken.objects.Add(new ObjectTarget { target = null });
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                var chips = view.Tree.Query<VisualElement>(className: "ad-chip").Build().ToList();
                // The default fixture avatar already ships an existing "Old" submenu and "RootToggle", so this
                // also covers the plain neutral chip shared with "no menu" and the merged warning chip.
                Assert.That(chips.Count, Is.GreaterThan(2), "the tree must actually show more than one kind of chip for this to be a real check");
                float height = chips[0].resolvedStyle.height;
                foreach (var chip in chips)
                    Assert.That(chip.resolvedStyle.height, Is.EqualTo(height).Within(0.5f), "every chip must share one height");
            }, 400f, 900f);
        }

        [Test]
        public void TheWarningTriangleIsMergedIntoAWarnChip()
        {
            var menu = avatar.AddMenu();
            var broken = menu.AddEntry(EntryKind.Toggle, "Broken");
            broken.objects.Add(new ObjectTarget { target = null });
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                var chip = view.Tree.Query<VisualElement>("ad-row-problem-chip").Build().FirstOrDefault();
                Assert.That(chip, Is.Not.Null, "the row's own warning shows as a chip, not a bare triangle");
                Assert.That(chip.ClassListContains("ad-chip"), Is.True);
                Assert.That(chip.ClassListContains("ad-chip-warn"), Is.True);
                Assert.That(chip.Q<KindGlyph>(), Is.Not.Null, "the triangle glyph still draws inside the chip");
                Assert.That(chip.Q<Label>().text, Is.EqualTo(L.Text("ad.ui.chip.problem")));
            }, 400f, 900f);
        }

        // ---- finding 6: row actions stay on the selected row only -----------------------------------------------

        [Test]
        public void OnlyTheSelectedRowKeepsVisibleActionsAfterARefresh()
        {
            // Kept under its old name: the row's own visible action buttons this test used to guard against a
            // pooled row wrongly keeping (Part B of phase 12 task 12) are gone at every selection now, Move to,
            // Duplicate and Delete having all moved to the right click menu, so a refresh has nothing of the kind
            // left to leak onto the wrong row. This confirms the removal itself instead.
            var menu = avatar.AddMenu();
            var a = menu.AddEntry(EntryKind.Toggle, "Eyes");
            a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var b = menu.AddEntry(EntryKind.Toggle, "Fade");
            b.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(a.id);
                LayoutProbe.Validate(view);
                view.Tree.SelectEntry(b.id);
                LayoutProbe.Validate(view);
                view.Refresh();
                LayoutProbe.Validate(view);

                Assert.That(view.Tree.Query<VisualElement>("row-actions").Build().ToList(), Is.Empty,
                    "no row draws a row-actions container any more, selected or not, before or after a refresh");
            }, 400f, 900f);
        }

        // ---- finding 7: Tags gets its own row -------------------------------------------------------------------

        [Test]
        public void TagsIsItsOwnLabelledRowUnderStyle()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(hat.id);
                LayoutProbe.Validate(view);
                var rawToggle = view.Detail.Q<Toggle>("ad-style-raw");
                Assert.That(rawToggle, Is.Not.Null);
                var rawRow = rawToggle.parent;
                Assert.That(rawRow.ClassListContains("ad-row"), Is.True, "Tags sits in its own row");
                Assert.That(rawRow.Q<Button>("ad-style-bold"), Is.Null, "the Style row's own B and I are not on Tags' row");
                var caption = rawRow.Q<Label>(className: "ad-label");
                Assert.That(caption, Is.Not.Null);
                Assert.That(caption.text, Is.EqualTo(L.Text("ad.ui.style.raw_label")));
                Assert.That(rawToggle.worldBound.x, Is.EqualTo(rawRow.worldBound.x + 112f).Within(1f), "the checkbox sits at the field column");
                Assert.That(rawToggle.worldBound.xMax, Is.LessThanOrEqualTo(view.Detail.worldBound.xMax + 0.5f), "Tags must not clip");
            }, 360f, 900f);
        }

        // ---- finding 9: 8px between sections -------------------------------------------------------------------

        [Test]
        public void SectionsAreEightPixelsApart()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            hat.materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 0, material = avatar.MaterialA });
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(hat.id);
                LayoutProbe.Validate(view);
                var objectsSection = view.Detail.Q<TargetListView>("ad-objects");
                var materialsSection = view.Detail.Q<TargetListView>("ad-materials");
                Assert.That(materialsSection.worldBound.y - objectsSection.worldBound.yMax, Is.EqualTo(8f).Within(0.5f),
                    "the gap between one section's last element and the next section's header is 8px");
            });
        }

        // ---- finding 10: Mode sits right under the When source row ----------------------------------------------

        [Test]
        public void ModeSitsRightUnderTheWhenSourceRowThenFullyOnAt()
        {
            var menu = avatar.AddMenu();
            var trigger = menu.AddEntry(EntryKind.Trigger, "Fade");
            trigger.source = TriggerSource.Contact;
            trigger.contactAnchor = avatar.Root.transform;
            trigger.touchMode = TouchMode.FollowDistance;
            trigger.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(trigger.id);
                LayoutProbe.Validate(view);
                var source = view.Detail.Q<DropdownField>("ad-trigger-source");
                var mode = view.Detail.Q<SegmentedChoice>("ad-touch-mode");
                var fullAt = view.Detail.Q<Slider>("ad-touch-fullat");
                var anchor = view.Detail.Q<ObjectField>("ad-touch-anchor");
                var fires = view.Detail.Q<Label>("ad-when-sentence");
                Assert.That(source.worldBound.y, Is.LessThan(mode.worldBound.y), "Mode sits under the When source row");
                Assert.That(mode.worldBound.y, Is.LessThan(fullAt.worldBound.y), "Fully on at sits right after Mode once Fade is picked");
                Assert.That(fullAt.worldBound.y, Is.LessThan(anchor.worldBound.y), "the contact fields come after Mode and Fully on at");
                Assert.That(anchor.worldBound.y, Is.LessThan(fires.worldBound.y), "the Fires summary comes after the contact fields");
            });
        }

        // ---- finding 11: Touched by and Who wrap at the field column --------------------------------------------

        [Test]
        public void TouchedByAndWhoWrapAtTheFieldColumn()
        {
            var menu = avatar.AddMenu();
            var trigger = menu.AddEntry(EntryKind.Trigger, "Poke");
            trigger.source = TriggerSource.Contact;
            trigger.contactAnchor = avatar.Root.transform;
            trigger.touchMode = TouchMode.Hold;
            trigger.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(trigger.id);
                LayoutProbe.Validate(view);
                var finger = view.Detail.Q<Toggle>("ad-touch-tag-Finger");
                var self = view.Detail.Q<Toggle>("ad-touch-self");
                var others = view.Detail.Q<Toggle>("ad-touch-others");
                Assert.That(finger, Is.Not.Null);
                var flagsRow = finger.parent;
                var tagsRow = flagsRow.parent;
                Assert.That(flagsRow.ClassListContains("ad-flags"), Is.True);
                Assert.That(flagsRow.resolvedStyle.flexWrap, Is.EqualTo(Wrap.Wrap), "the tags flow wraps instead of overflowing");
                Assert.That(flagsRow.worldBound.x, Is.EqualTo(tagsRow.worldBound.x + 112f).Within(1f), "the flags wrap starts at the field column");
                var toggles = flagsRow.Query<Toggle>().Build().ToList();
                Assert.That(toggles.Count, Is.GreaterThanOrEqualTo(2));
                Assert.That(flagsRow.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Row), "the flags flow runs left to right, not down a column");
                Assert.That(toggles[1].worldBound.y, Is.EqualTo(toggles[0].worldBound.y).Within(1f), "the first two tags share one line");
                Assert.That(toggles[1].worldBound.x, Is.GreaterThan(toggles[0].worldBound.xMax), "the second tag sits to the right of the first");

                var whoRow = self.parent;
                Assert.That(whoRow.ClassListContains("ad-flags"), Is.True, "Who uses the same compact flags row");
                Assert.That(self.worldBound.x, Is.EqualTo(whoRow.worldBound.x + 112f).Within(1f), "Who's own flags sit at the field column");
                Assert.That(others.worldBound.y, Is.EqualTo(self.worldBound.y).Within(1f), "Self and Others share one line");
            }, 360f, 900f);
        }

        // ---- finding 12: Tips gets its own caption --------------------------------------------------------------

        [Test]
        public void TipsButtonHasItsOwnCaptionAtTheFieldColumn()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                if (!view.Header.SettingsFoldOpen) view.Header.ToggleSettingsFold();
                LayoutProbe.Validate(view);
                var button = view.Header.Q<Button>("ad-tips-switch");
                Assert.That(button, Is.Not.Null);
                var row = button.parent;
                Assert.That(row.ClassListContains("ad-row"), Is.True);
                var caption = row.Q<Label>(className: "ad-label");
                Assert.That(caption, Is.Not.Null, "First-use tips gets a labelled row");
                Assert.That(caption.text, Is.EqualTo(L.Text("ad.ui.tips_switch_label")));
                Assert.That(button.text, Is.EqualTo(L.Text("ad.ui.tips_switch")));
                Assert.That(button.worldBound.x, Is.EqualTo(row.worldBound.x + 112f).Within(1f), "Show again sits at the field column");
            });
        }

        // ---- finding 13: no glyph on the empty state --------------------------------------------------------

        [Test]
        public void EmptyDetailPanelTitleCarriesNoGlyph()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);
            var glyph = view.Detail.Q<KindGlyph>();
            Assert.That(glyph, Is.Not.Null);
            Assert.That(glyph.ClassListContains("ad-hidden"), Is.True, "no entry is selected, so no kind glyph shows");

            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
            view.Refresh();
            view.Tree.SelectEntry(hat.id);
            Assert.That(glyph.ClassListContains("ad-hidden"), Is.False, "a real selection shows its own kind glyph again");
        }

        // ---- finding 14: Quick start reads as a real control ----------------------------------------------------

        [Test]
        public void QuickStartIsAFlatButtonNotAnItalicLink()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);
            var button = view.Tree.Q<Button>("ad-welcome-quickstart");
            Assert.That(button, Is.Not.Null);
            Assert.That(button.ClassListContains("ad-link"), Is.False, "Quick start no longer uses the italic link style");
            Assert.That(button.ClassListContains("ad-welcome-button"), Is.True, "Quick start uses the same flat button style as the two starts above it");
        }
    }
}
#endif
