#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// A tag filter beside the search that combines with it.
    /// The button that opens it is a plain ToolbarButton showing an AdMenu, whose ticks and count this file also
    /// checks through the items kept behind MenuTreeView.TagFilterMenu.
    /// </summary>
    public class TagFilterTests
    {
        TestAvatar avatar;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
            TagFilterMemory.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
            TagFilterMemory.ClearAll();
        }

        AvatarDoctorView View(AvatarDoc menu) => new AvatarDoctorView(new SerializedObject(menu), menu);

        [Test]
        public void TagFilterOffersOnlyTagsPresent()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);

            var available = view.Tree.AvailableTags();

            Assert.That(available, Does.Contain(TagFilter.TagId(TagFilter.GroupKind, "toggle")));
            Assert.That(available, Does.Contain(TagFilter.TagId(TagFilter.GroupFrom, "avatardoc")));
            Assert.That(available, Does.Contain(TagFilter.TagId(TagFilter.GroupFrom, "avatar_menu")), "the default avatar's own existing menu items are present");
            Assert.That(available, Does.Not.Contain(TagFilter.TagId(TagFilter.GroupKind, "radial")), "nothing Radial exists on this avatar");
            Assert.That(available, Does.Not.Contain(TagFilter.TagId(TagFilter.GroupKind, "grabbable")));
        }

        [Test]
        public void TagFilterCombinesOrWithinAndAcrossGroups()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            var radial = menu.AddEntry(EntryKind.Radial, "Eyes");
            radial.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", minWeight = 0f, maxWeight = 100f });
            var view = View(menu);

            view.Tree.ToggleTag(TagFilter.TagId(TagFilter.GroupKind, "toggle"));
            view.Tree.ToggleTag(TagFilter.TagId(TagFilter.GroupKind, "radial"));

            Assert.That(view.Tree.RowFor(toggle.id), Is.Not.Null, "OR within the Kind group keeps a toggle row");
            Assert.That(view.Tree.RowFor(radial.id), Is.Not.Null, "OR within the Kind group keeps a radial row too");

            // No own entry is ever From "Avatar menu"; ticking it too must exclude both rows the Kind group alone kept.
            view.Tree.ToggleTag(TagFilter.TagId(TagFilter.GroupFrom, "avatar_menu"));

            Assert.That(view.Tree.RowFor(toggle.id), Is.Null, "AND across groups: the From group now rules it out");
            Assert.That(view.Tree.RowFor(radial.id), Is.Null);
        }

        [Test]
        public void TagFilterKeepsAncestorsVisible()
        {
            var menu = avatar.AddMenu();
            var sub = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat", sub.id);
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);

            view.Tree.ToggleTag(TagFilter.TagId(TagFilter.GroupKind, "toggle"));

            var subRow = view.Tree.RowFor(sub.id);
            var toggleRow = view.Tree.RowFor(toggle.id);
            Assert.That(subRow, Is.Not.Null, "the submenu stays visible so the tree's own structure still reads");
            Assert.That(subRow.FilterDimmed, Is.True, "kept only because its child matches, so it shows dimmed");
            Assert.That(toggleRow, Is.Not.Null);
            Assert.That(toggleRow.FilterDimmed, Is.False, "the row that actually matches the tag is never dimmed");
        }

        [Test]
        public void EveryRowTheFilteredTreeShowsHasItsData()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Submenu, "Extras");
            var sub = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat", sub.id);
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            menu.AddEntry(EntryKind.Radial, "Size");
            var view = View(menu);

            view.Tree.ToggleTag(TagFilter.TagId(TagFilter.GroupKind, "toggle"));

            int count = view.Tree.Tree.viewController.GetItemsCount();
            Assert.That(count, Is.GreaterThan(0), "the filtered tree still shows the match and its submenu");
            for (int i = 0; i < count; i++)
            {
                var row = view.Tree.RowAtIndex(i);
                Assert.That(row, Is.Not.Null, "index " + i + " of the filtered tree has no row, so it would draw blank");
                Assert.That(row.Id, Is.EqualTo(view.Tree.Tree.GetIdForIndex(i)));
            }
        }

        [Test]
        public void TagFilterAndSearchCombine()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var boot = menu.AddEntry(EntryKind.Toggle, "Boot");
            boot.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var view = View(menu);

            view.Tree.ToggleTag(TagFilter.TagId(TagFilter.GroupKind, "toggle"));
            Assert.That(view.Tree.RowFor(hat.id), Is.Not.Null);
            Assert.That(view.Tree.RowFor(boot.id), Is.Not.Null);

            view.Tree.Filter = "Hat";

            Assert.That(view.Tree.RowFor(hat.id), Is.Not.Null);
            Assert.That(view.Tree.RowFor(boot.id), Is.Null, "the tag filter and the text search combine with AND");
        }

        (AvatarDoc menu, MenuEntry hat, MenuEntry glasses, AvatarDoctorView view) HatAndGlasses()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var glasses = menu.AddEntry(EntryKind.Radial, "Glasses");
            glasses.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", minWeight = 0f, maxWeight = 100f });
            return (menu, hat, glasses, View(menu));
        }

        [Test]
        public void ASearchThatHidesTheSelectedRowGivesItBackWhenItStopsHidingIt()
        {
            var (_, hat, _, view) = HatAndGlasses();
            view.Tree.SelectEntry(hat.id);

            view.Tree.Filter = "glasses";
            Assert.That(view.Tree.RowFor(hat.id), Is.Null, "the search hides Hat");
            Assert.That(view.Detail.Row, Is.Null, "so the panel has nothing to show meanwhile");

            view.Tree.Filter = "";

            Assert.That(view.Tree.Selected?.EntryId, Is.EqualTo(hat.id), "Hat is selected again as soon as the search lets it back");
            Assert.That(view.Detail.Row?.EntryId, Is.EqualTo(hat.id), "and the panel shows it");
        }

        [Test]
        public void ASearchThatStillHidesItKeepsWaitingAcrossRefreshes()
        {
            var (_, hat, _, view) = HatAndGlasses();
            view.Tree.SelectEntry(hat.id);

            view.Tree.Filter = "glass";
            view.Refresh();
            view.Tree.Filter = "glasses";
            view.Refresh();
            Assert.That(view.Tree.Selected, Is.Null);
            view.Tree.Filter = "ha";

            Assert.That(view.Tree.Selected?.EntryId, Is.EqualTo(hat.id), "a later search that shows Hat again brings the selection back, however many refreshes passed");
        }

        [Test]
        public void ATagFilterThatHidesTheSelectedRowGivesItBackWhenItIsCleared()
        {
            var (_, hat, glasses, view) = HatAndGlasses();
            view.Tree.SelectEntry(hat.id);

            view.Tree.ToggleTag(TagFilter.TagId(TagFilter.GroupKind, "radial"));
            Assert.That(view.Tree.RowFor(hat.id), Is.Null, "the Radial filter hides the toggle");
            Assert.That(view.Tree.RowFor(glasses.id), Is.Not.Null);

            view.Tree.ClearTags();

            Assert.That(view.Tree.Selected?.EntryId, Is.EqualTo(hat.id));
            Assert.That(view.Detail.Row?.EntryId, Is.EqualTo(hat.id));
        }

        [Test]
        public void ARowPickedWhileTheFilterHidesTheOldOneStaysSelected()
        {
            var (_, hat, glasses, view) = HatAndGlasses();
            view.Tree.SelectEntry(hat.id);
            view.Tree.Filter = "glasses";

            view.Tree.SelectEntry(glasses.id);
            view.Tree.Filter = "";

            Assert.That(view.Tree.Selected?.EntryId, Is.EqualTo(glasses.id), "the choice made while filtering is the one that stays");
        }

        [Test]
        public void ARowDeletedWhileTheFilterHidesItDoesNotComeBack()
        {
            var (menu, hat, _, view) = HatAndGlasses();
            try
            {
                view.Tree.SelectEntry(hat.id);
                view.Tree.Filter = "glasses";

                Assert.That(EntryEditing.Delete(menu, menu.FindEntry(hat.id)), Is.True);
                AvatarPlanCache.Invalidate();
                view.Refresh();
                view.Tree.Filter = "";

                Assert.That(view.Tree.RowFor(hat.id), Is.Null);
                Assert.That(view.Tree.Selected, Is.Null, "nothing is selected, and no error comes of it");
            }
            finally
            {
                Undo.ClearUndo(menu);
            }
        }

        [Test]
        public void OpeningTheSummaryWhileASearchHidesTheSelectionKeepsItOpenWhenTheSearchEnds()
        {
            var (_, hat, _, view) = HatAndGlasses();
            view.Tree.SelectEntry(hat.id);
            view.Tree.Filter = "glasses";

            view.OpenSummary();
            view.Tree.Filter = "";

            Assert.That(view.SummaryOpen, Is.True, "the row that comes back does not close the Summary the creator opened meanwhile");
            Assert.That(view.Tree.Selected, Is.Null, "and nothing is announced behind it");
        }

        [Test]
        public void ClearingTheSelectionWhileTheFilterHidesTheOldOneForgetsIt()
        {
            var (_, hat, _, view) = HatAndGlasses();
            view.Tree.SelectEntry(hat.id);
            view.Tree.Filter = "glasses";

            view.Tree.ClearSelection();
            view.Tree.Filter = "";

            Assert.That(view.Tree.Selected, Is.Null, "an explicit choice of nothing is not undone by the search ending");
        }

        [Test]
        public void ClearRemovesEveryFilter()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            var radial = menu.AddEntry(EntryKind.Radial, "Eyes");
            radial.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", minWeight = 0f, maxWeight = 100f });
            var view = View(menu);
            view.Tree.ToggleTag(TagFilter.TagId(TagFilter.GroupKind, "toggle"));
            Assert.That(view.Tree.RowFor(radial.id), Is.Null);
            Assert.That(view.Tree.ActiveTags, Is.Not.Empty);

            view.Tree.ClearTags();

            Assert.That(view.Tree.ActiveTags, Is.Empty);
            Assert.That(view.Tree.RowFor(radial.id), Is.Not.Null, "clearing brings every row back");
        }

        [Test]
        public void TagFilterMenuShowsTicksAndTheCount()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var tagFilterButton = view.Tree.Q<ToolbarButton>("ad-tag-filter");
            Assert.That(tagFilterButton, Is.Not.Null);
            // The caption is its own Label child, not the button's own text: a ToolbarButton draws its own text
            // across the whole content box regardless of any appended child, which drew the dropdown arrow over
            // the last letter until the caption moved into the same row of children as the icon and the arrow.
            var tagFilterLabel = tagFilterButton.Q<Label>();
            Assert.That(tagFilterLabel.text, Is.EqualTo(L.Text("ad.ui.tagfilter")), "no count while nothing is ticked");
            var clear = view.Tree.TagFilterMenu.First(a => a.Text == L.Text("ad.ui.tagfilter.clear"));
            Assert.That(clear.Disabled, Is.True, "Clear is disabled while nothing is ticked");

            var kindTag = TagFilter.TagId(TagFilter.GroupKind, "toggle");
            view.Tree.ToggleTag(kindTag);

            Assert.That(tagFilterLabel.text, Is.EqualTo(string.Format(L.Text("ad.ui.tagfilter.count"), 1)), "the count shows once a tag is ticked");
            var items = view.Tree.TagFilterMenu;
            var heading = items.ToList().FindIndex(a => a.Header && a.Text == TagFilter.GroupLabel(TagFilter.GroupKind));
            Assert.That(heading, Is.GreaterThanOrEqualTo(0), "each group has its own header");
            var ticked = items.Skip(heading + 1).First(a => a.Text == TagFilter.TagLabel(kindTag));
            Assert.That(ticked.Checked, Is.True, "the ticked tag shows Checked");
            clear = items.First(a => a.Text == L.Text("ad.ui.tagfilter.clear"));
            Assert.That(clear.Disabled, Is.False, "Clear enables once something is ticked");
        }

        [Test]
        public void TagsAreRememberedPerComponentForTheSessionOnly()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.Tree.ToggleTag(TagFilter.TagId(TagFilter.GroupKind, "toggle"));

            // A second view of the same component, as reopening the inspector would build, keeps the ticked tags.
            var reopened = View(menu);
            Assert.That(reopened.Tree.ActiveTags, Is.EquivalentTo(new[] { TagFilter.TagId(TagFilter.GroupKind, "toggle") }));
        }

        [Test]
        public void TagFilterClearSitsAtTheRight()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            menu.AddEntry(EntryKind.Radial, "Size");
            menu.AddEntry(EntryKind.Submenu, "Outfits");
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                CheckClear(view, false, TagFilter.TagId(TagFilter.GroupKind, "toggle"), TagFilter.TagId(TagFilter.GroupKind, "radial"));
                view.Tree.ClearTags();
                CheckClear(view, true,
                    TagFilter.TagId(TagFilter.GroupKind, "toggle"), TagFilter.TagId(TagFilter.GroupKind, "radial"), TagFilter.TagId(TagFilter.GroupKind, "submenu"),
                    TagFilter.TagId(TagFilter.GroupFrom, "avatardoc"), TagFilter.TagId(TagFilter.GroupFrom, "avatar_menu"));
            }, 360f);
        }

        static void CheckClear(AvatarDoctorView view, bool wraps, params string[] tags)
        {
            foreach (var tag in tags) view.Tree.ToggleTag(tag);
            LayoutProbe.Validate(view);
            string what = tags.Length + " tags: ";
            var row = view.Tree.Q("ad-filter-chips");
            var clear = row.Q<Button>("ad-tagfilter-clear");
            var chips = row.Query(className: "ad-filter-chip").ToList();
            Assert.That(clear, Is.Not.Null, what + "Clear is shown");
            Assert.That(chips, Has.Count.EqualTo(tags.Length));
            bool wrapped = chips.Any(c => c.worldBound.y > chips[0].worldBound.y + 1f);
            Assert.That(wrapped, Is.EqualTo(wraps), what + (wraps ? "the chips wrap onto a second line" : "the chips fit on one line"));

            var box = clear.worldBound;
            string layout = " Row " + row.worldBound + ", Clear " + box + " min-height " + clear.resolvedStyle.minHeight + " margins " + clear.resolvedStyle.marginTop + "/" + clear.resolvedStyle.marginBottom
                + ", chips " + string.Join(" ", chips.Select(c => c.worldBound.ToString()));
            float contentRight = row.worldBound.xMax - row.resolvedStyle.paddingRight - row.resolvedStyle.borderRightWidth;
            Assert.That(box.xMax, Is.EqualTo(contentRight).Within(1f), what + "Clear sits at the row's right end." + layout);
            Assert.That(box.center.y, Is.EqualTo(chips[0].worldBound.center.y).Within(1f), what + "Clear is centred on the first line of chips." + layout);
            foreach (var chip in chips)
                Assert.That(box.Overlaps(chip.worldBound), Is.False, what + "Clear does not overlap a chip");
            Assert.That(clear.resolvedStyle.paddingLeft, Is.GreaterThanOrEqualTo(4f), what + "Clear's text has room on the left");
            Assert.That(clear.resolvedStyle.paddingRight, Is.GreaterThanOrEqualTo(4f), what + "Clear's text has room on the right");
            var text = clear.MeasureTextSize(clear.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
            Assert.That(clear.contentRect.width, Is.GreaterThanOrEqualTo(text.x - 0.5f), what + "Clear's text fits between its paddings");
        }
    }
}
#endif
