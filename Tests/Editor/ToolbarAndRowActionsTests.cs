#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The slim toolbar's four add buttons and its "+ More" list. An earlier version rebuilt the same
    /// top bar with the right Unity control for each job (a real Toolbar, ToolbarButton, ToolbarMenu, ToolbarToggle
    /// and ToolbarSearchField) and gave Move to, Duplicate and Delete built in icons on the row's own visible
    /// buttons; those tests lived here too. A later version replaced every one of those mixed control types with a
    /// single ToolbarButton and moved every row action to the right click menu, so the tests below now check the
    /// one control type and RowActions as the menu's own single source instead.
    /// </summary>
    public class ToolbarAndRowActionsTests
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

        AvatarDoctorView View(AvatarDoc menu, IViewPrefs prefs) => new AvatarDoctorView(new SerializedObject(menu), menu, viewPrefs: prefs);

        Component AddFuryToggle(string name)
        {
            var go = new GameObject("Fury");
            go.transform.SetParent(avatar.Root.transform);
            return VrcFuryTestHelper.AddToggle(go, name);
        }

        [Test]
        public void TheToolbarShowsFourAddButtonsAndMore()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);

            var toolbar = view.Tree.Q<VisualElement>(className: "ad-toolbar");
            Assert.That(toolbar, Is.Not.Null);
            var childNames = toolbar.Children().Select(c => c.name).ToList();

            Assert.That(childNames, Is.EqualTo(new[]
            {
                "ad-add-toggle", "ad-add-radial", "ad-add-submenu", "ad-add-trigger", "ad-add-override", "ad-add-preset", "ad-add-grabbable",
                "ad-add-animator", "ad-add-menu-asset", "ad-add-parameter", "ad-outfit-open", "ad-add-contact", "ad-more", "", "ad-capture-missing", "ad-import-open", "ad-wheel-toggle",
            }), "every add in priority order, More, the flexible spacer, then Capture icons, Convert and the Wheel");
            Assert.That(view.Tree.Q<TextField>("ad-search"), Is.Not.Null, "search stays");
            Assert.That(view.Tree.Q<Button>("ad-tag-filter"), Is.Not.Null, "the tag filter stays, in the finding bar below the actions bar");
            Assert.That(toolbar.Contains(view.Tree.Q<TextField>("ad-search")), Is.False, "search moved to its own bar");
            Assert.That(view.Q<Button>("ad-import-open").text, Is.EqualTo(L.Text("ad.ui.toolbar.convert")));

            LayoutProbe.Measure(view, () =>
            {
                var shown = view.Tree.AddButtons.Where(b => b.resolvedStyle.display != DisplayStyle.None).Select(b => b.name).ToList();
                var all = view.Tree.AddButtons.Select(b => b.name).ToList();
                Assert.That(shown, Is.EqualTo(new[] { "ad-add-toggle", "ad-add-radial", "ad-add-submenu" }), "at 360 px exactly the first three adds show");
                Assert.That(view.Tree.Q<Button>("ad-more").resolvedStyle.display, Is.Not.EqualTo(DisplayStyle.None));
                Assert.That(view.Tree.MoreMenu.Count, Is.EqualTo(all.Count - shown.Count), "+ More holds exactly the adds that did not fit");
            }, 360f);
        }

        [Test]
        public void TopBarUsesUnityToolbarControls()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);

            var actionsBar = view.Tree.Q<VisualElement>("ad-toolbar-actions");
            Assert.That(actionsBar, Is.TypeOf<Toolbar>(), "the actions bar is a real Unity Toolbar");
            // Every action, including the two dropdowns and the wheel, is the same ToolbarButton: the one control
            // type is what keeps every item's text on one shared baseline, which mixing ToolbarMenu, ToolbarToggle
            // and ToolbarSearchField in with ToolbarButton could not do on the creator's own screen.
            Assert.That(view.Tree.Q<Button>("ad-add-toggle"), Is.TypeOf<ToolbarButton>());
            Assert.That(view.Tree.Q<Button>("ad-add-radial"), Is.TypeOf<ToolbarButton>());
            Assert.That(view.Tree.Q<Button>("ad-add-submenu"), Is.TypeOf<ToolbarButton>());
            Assert.That(view.Tree.Q<Button>("ad-import-open"), Is.TypeOf<ToolbarButton>());
            Assert.That(view.Tree.Q<Button>("ad-more"), Is.TypeOf<ToolbarButton>(), "+ More is a ToolbarButton that opens an AdMenu, not a control of its own");
            Assert.That(view.Tree.Q<Button>("ad-wheel-toggle"), Is.TypeOf<ToolbarButton>(), "Wheel is a ToolbarButton with its own checked class, not a toggle control");
            Assert.That(view.Tree.MoreMenu, Is.Not.Null, "+ More keeps its items behind it");
            var searchIcon = view.Tree.Q<Image>(className: "ad-search-icon");
            Assert.That(searchIcon, Is.Not.Null);
            Assert.That(searchIcon.image, Is.Not.Null, "the magnifier inside the search field has a texture under one of the names this Unity version knows");

            var findBar = view.Tree.Q<VisualElement>("ad-toolbar-find");
            Assert.That(findBar, Is.TypeOf<Toolbar>(), "the finding bar is a real Unity Toolbar too");
            Assert.That(findBar.Q<TextField>("ad-search"), Is.Not.Null, "search is a plain TextField, not a ToolbarSearchField");
            Assert.That(findBar.Q<Button>("ad-tag-filter"), Is.TypeOf<ToolbarButton>(), "the tag filter is a ToolbarButton too, the same shape as + More");
            Assert.That(view.Tree.TagFilterMenu, Is.Not.Null, "the tag filter keeps its items behind it");

            // Both bars carry the class the stylesheet sizes them by (height: auto, min-height: 24px) and never
            // pin their own height in code, which would fight a wrapped second line growing the bar instead of
            // overlapping it.
            foreach (var bar in new[] { actionsBar, findBar })
            {
                Assert.That(bar.ClassListContains("ad-toolbar"), Is.True, bar.name + " carries the ad-toolbar class");
                Assert.That(bar.style.height.keyword, Is.EqualTo(StyleKeyword.Null), bar.name + " has no inline fixed height of its own");
            }
        }

        [Test]
        public void RowActionsAreIconButtonsWithTooltips()
        {
            // The row's own visible icon buttons are gone (Part B): Rename, Duplicate, Move to and Delete now
            // live in the right click menu only, reading this same list. What used to be an icon-or-text check on
            // a rendered button is now a check that every one of the menu's own actions still carries a real
            // label, a real tooltip and a real action to run.
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var row = view.Tree.RowFor(hat.id);

            var actions = view.Tree.RowActions(row).ToList();
            Assert.That(actions.Select(a => a.LabelKey), Is.EqualTo(new[] { "ad.ui.menu.rename", "ad.ui.menu.duplicate", "ad.ui.menu.move", "ad.ui.menu.delete" }),
                "the right click menu's own actions, in the order they show");
            foreach (var (labelKey, tooltipKey, run) in actions)
            {
                Assert.That(L.Text(labelKey), Is.Not.Null.And.Not.Empty, labelKey);
                Assert.That(L.Text(tooltipKey), Is.Not.Null.And.Not.Empty, tooltipKey + " (tooltip for " + labelKey + ")");
                Assert.That(run, Is.Not.Null, labelKey + " must carry a real action to run");
            }
        }

        [Test]
        public void FoldsAreUnityFoldouts()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            menu.installPath = "Outfits";
            hat.blendSeconds = 1.5f;
            // A memory store shared by both views below, so "remembered" is checked without ever touching the
            // real EditorPrefs: a fresh store per test needs no cleanup either, unlike the real one.
            var prefs = new MemoryViewPrefs();
            var view = View(menu, prefs);

            var settingsFoldout = view.Header.Q<Foldout>("ad-settings-fold-header");
            Assert.That(settingsFoldout, Is.Not.Null, "the Component settings fold is a real Foldout");
            Assert.That(settingsFoldout.value, Is.False, "closed by default");
            Assert.That(view.Header.SettingsFoldDotVisible, Is.True, "the dot still shows a hidden, changed value");

            view.Tree.SelectEntry(hat.id);
            var advancedFold = view.Detail.Q<AdvancedFold>();
            var advancedFoldout = advancedFold.Q<Foldout>();
            Assert.That(advancedFoldout, Is.Not.Null, "the entry panel's Advanced fold is a real Foldout too");
            Assert.That(advancedFoldout.value, Is.False, "closed by default");
            Assert.That(advancedFold.DotVisible, Is.True);

            view.Header.ToggleSettingsFold();
            Assert.That(settingsFoldout.value, Is.True, "the Foldout itself follows the remembered state");
            Assert.That(view.Header.SettingsFoldDotVisible, Is.False, "no need for the dot once the fold is open");

            advancedFold.ToggleOpen();
            Assert.That(advancedFoldout.value, Is.True);
            Assert.That(advancedFold.DotVisible, Is.False);

            // The remembered open state survives a second, separate view of the same component and entry,
            // sharing the same store.
            var reopened = View(menu, prefs);
            Assert.That(reopened.Header.SettingsFoldOpen, Is.True);
            reopened.Tree.SelectEntry(hat.id);
            Assert.That(reopened.Detail.Q<AdvancedFold>().IsOpen, Is.True);
        }

        [Test]
        public void MoreListsEveryOtherKindWithADescription()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);

            var items = view.Tree.MoreItems();

            var expectedNames = new[]
            {
                "ad.ui.add.trigger", "ad.ui.add.override", "ad.ui.add.preset", "ad.ui.add.grabbable",
                "ad.ui.add.animator", "ad.ui.add.menu_asset", "ad.ui.parameters.add", "ad.ui.add.outfit",
            };
            Assert.That(items.Select(i => i.NameKey), Is.EqualTo(expectedNames));
            foreach (var item in items)
            {
                Assert.That(L.Text(item.NameKey), Is.Not.Null.And.Not.Empty, item.NameKey);
                Assert.That(L.Text(item.DescriptionKey), Is.Not.Null.And.Not.Empty, item.DescriptionKey + " (description for " + item.NameKey + ")");
                Assert.That(item.NameKey, Is.Not.EqualTo(item.DescriptionKey));
            }

            foreach (var hidden in view.Tree.MoreMenu)
                Assert.That(hidden.Description, Is.Not.Null.And.Not.Empty, hidden.Text + " carries its description in + More");

            int before = menu.animators.Count;
            items.First(i => i.NameKey == "ad.ui.add.animator").Run();
            // Run() is exactly the same delegate the (now hidden) "+ Animator" button already called: it adds an
            // empty merge to fill in afterward, the same as before this task moved the button into this menu.
            Assert.That(menu.animators.Count, Is.EqualTo(before + 1));
        }

        [Test]
        public void TheSelectedRowShowsItsActions()
        {
            // The right click menu reads RowActions regardless of selection, unlike the removed row buttons that
            // only ever showed on the selected row: right-clicking an unselected row must still offer its actions.
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var row = view.Tree.RowFor(hat.id);

            var labels = view.Tree.RowActions(row).Select(a => a.LabelKey).ToList();
            Assert.That(labels, Is.EqualTo(new[] { "ad.ui.menu.rename", "ad.ui.menu.duplicate", "ad.ui.menu.move", "ad.ui.menu.delete" }));

            view.Tree.SelectEntry(hat.id);
            Assert.That(view.Tree.RowActions(row).Select(a => a.LabelKey), Is.EqualTo(labels), "selecting the row first changes nothing about what it offers");
        }

        [Test]
        public void AForeignRowShowsConvert()
        {
            Assume.That(VrcFuryTestHelper.VrcFuryType, Is.Not.Null);
            var menu = avatar.AddMenu();
            var toggle = AddFuryToggle("Hat");
            VrcFuryTestHelper.AddAction(toggle, VrcFuryReader.ObjectToggleActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "obj", avatar.Hat);
                VrcFuryTestHelper.SetField(a, "mode", 0);
            });
            var view = View(menu);
            var row = view.Tree.RowsOfKind(RowKind.Foreign).Single(r => r.Name == "Hat");
            view.Tree.SelectKey(row.Key);

            var actions = view.Tree.RowActions(row).ToList();

            Assert.That(actions.Select(a => a.LabelKey), Is.EqualTo(new[] { "ad.ui.convert" }));
        }

        [Test]
        public void AnExistingRowOffersNoRowActions()
        {
            // The default test avatar already ships an existing root menu control ("RootToggle"), which this
            // component neither made nor may duplicate, delete or convert: only its right-click actions apply.
            var menu = avatar.AddMenu();
            var view = View(menu);
            var row = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Node == null && r.Name == "RootToggle");

            Assert.That(view.Tree.RowActions(row), Is.Empty, "existing rows keep their own actions right click only");
        }

        [Test]
        public void TheSelectedRowCollapsesToOneMoreButtonWhenNarrow()
        {
            // Kept under its old name, repurposed: the row's own trailing action buttons this test used to
            // collapse are gone at every width now, Move to, Duplicate and Delete having all moved to the right
            // click menu, so this confirms the removal itself instead of a "more" button that no longer exists.
            // IsNarrow itself still matters (the row's own chips still drop at this width) so it is left standing.
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.Tree.SelectEntry(hat.id);
            var row = view.Tree.RowFor(hat.id);

            Assert.That(view.Tree.IsNarrow(200f), Is.True);
            Assert.That(view.Tree.IsNarrow(300f), Is.True, "the default inspector width still counts as narrow for the row's own least essential chips");
            Assert.That(view.Tree.IsNarrow(0f), Is.False, "an unmeasured (detached) width is never treated as narrow");
            Assert.That(view.Tree.IsNarrow(400f), Is.False);

            view.Tree.RowWidthOverride = 200f;
            var narrowElement = view.Tree.MakeRow();
            view.Tree.BindRowContent(narrowElement, row);
            Assert.That(narrowElement.Query<Button>().Build().ToList(), Is.Empty, "no row action button at the narrowest width");

            view.Tree.RowWidthOverride = null;
            var wideElement = view.Tree.MakeRow();
            view.Tree.BindRowContent(wideElement, row);
            Assert.That(wideElement.Query<Button>().Build().ToList(), Is.Empty, "and none at the default width either: every action moved to the right click menu");
        }

        [Test]
        public void NarrowRowsDropLowPriorityChipsButKeepTheName()
        {
            Assume.That(VrcFuryTestHelper.VrcFuryType, Is.Not.Null);
            var menu = avatar.AddMenu();
            var toggle = AddFuryToggle("Hat");
            VrcFuryTestHelper.AddAction(toggle, VrcFuryReader.ObjectToggleActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "obj", avatar.Hat);
                VrcFuryTestHelper.SetField(a, "mode", 0);
            });
            var view = View(menu);
            var row = view.Tree.RowsOfKind(RowKind.Foreign).Single(r => r.Name == "Hat");
            Assert.That(row.SourceChip, Is.Not.Null.And.Not.Empty, "a foreign row always carries a source chip");

            view.Tree.RowWidthOverride = 200f;
            var narrowElement = view.Tree.MakeRow();
            view.Tree.BindRowContent(narrowElement, row);
            Assert.That(narrowElement.Q<Label>(className: "ad-chip-source"), Is.Null, "the source chip is one of the row's least essential chips at the narrowest width");
            Assert.That(narrowElement.Q<Label>(className: "ad-name").text, Is.EqualTo(row.Name), "the row's own name is never dropped");

            view.Tree.RowWidthOverride = null;
            var wideElement = view.Tree.MakeRow();
            view.Tree.BindRowContent(wideElement, row);
            Assert.That(wideElement.Q<Label>(className: "ad-chip-source"), Is.Not.Null);
        }

        /// <summary>
        /// Mimics the wrapper Unity's own TreeView draws around a bound row's content at a given depth (from the
        /// outside in: "unity-tree-view__item" holding "unity-tree-view__item-indent", width indentPerDepth times
        /// depth, then "unity-tree-view__item-content" holding the row itself), the same classes MeasureIndent
        /// reads off a real row. A real, virtualized TreeView never actually pools a row inside this suite's
        /// synchronous tests (confirmed empirically here: even an explicit style.height, RefreshItems, Rebuild and
        /// the panel's own Update leave its content container holding zero children), so this gives MeasureIndent a
        /// real, LayoutProbe-measured stand-in to read from instead of skipping the real arithmetic entirely.
        /// </summary>
        static VisualElement WrappedRow(VisualElement content, int depth, float indentPerDepth)
        {
            var item = new VisualElement();
            // No stylesheet backs these class names here (Unity's own USS supplies the real row layout for
            // "unity-tree-view__item" and its indent), so the row direction MeasureIndent actually relies on is
            // set explicitly instead of assumed from a class name alone.
            item.style.flexDirection = FlexDirection.Row;
            var indent = new VisualElement();
            indent.AddToClassList("unity-tree-view__item-indent");
            indent.style.width = depth * indentPerDepth;
            item.Add(indent);
            var itemContent = new VisualElement();
            itemContent.AddToClassList("unity-tree-view__item-content");
            itemContent.Add(content);
            item.Add(itemContent);
            return item;
        }

        [Test]
        public void TheDragGripDoesNotMoveTheIndent()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var casual = menu.AddEntry(EntryKind.Toggle, "Casual", outfits.id);
            casual.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);

            var depth0Row = view.Tree.MakeRow();
            view.Tree.BindRowContent(depth0Row, view.Tree.RowFor(outfits.id));
            var depth1Row = view.Tree.MakeRow();
            view.Tree.BindRowContent(depth1Row, view.Tree.RowFor(casual.id));

            Assert.That(depth0Row.Q<VisualElement>(className: "ad-drag-grip"), Is.Null, "the creator asked for the hover grip to be removed; the row itself is still the drag handle");
            Assert.That(depth1Row.Q<VisualElement>(className: "ad-drag-grip"), Is.Null);
            Assert.That(depth0Row.Q<VisualElement>("row-grip"), Is.Null);
            Assert.That(depth1Row.Q<VisualElement>("row-grip"), Is.Null);

            // The measured cache is a static, so a stale value another test left behind must not let this one pass
            // without MeasureIndent actually running again against these two real, laid out wrappers.
            var measuredField = typeof(MenuTreeView).GetField("measuredIndentWidth", BindingFlags.NonPublic | BindingFlags.Static);
            measuredField.SetValue(null, null);
            var measureIndent = typeof(MenuTreeView).GetMethod("MeasureIndent", BindingFlags.NonPublic | BindingFlags.Static);

            const float realIndentPerDepth = 15f;
            var host = new VisualElement();
            var depth0Wrapped = WrappedRow(depth0Row, 0, realIndentPerDepth);
            var depth1Wrapped = WrappedRow(depth1Row, 1, realIndentPerDepth);
            host.Add(depth0Wrapped);
            host.Add(depth1Wrapped);

            LayoutProbe.Measure(host, () =>
            {
                measureIndent.Invoke(null, new object[] { view.Tree.Tree, depth0Row, 0 });
                measureIndent.Invoke(null, new object[] { view.Tree.Tree, depth1Row, 1 });

                var measuredValue = (float?)measuredField.GetValue(null);
                Assert.That(measuredValue, Is.Not.Null, "MeasureIndent must actually read a per-depth width off the depth 1 wrapper's own indent element");
                Assert.That(measuredValue.Value, Is.GreaterThan(0f).And.LessThan(300f), "the measured indent width must be a plausible real reading");
                Assert.That(measuredValue.Value, Is.EqualTo(realIndentPerDepth).Within(0.5f), "it must read the real width this wrapper actually laid out, not some other number");

                Assert.That(depth1Row.worldBound.x, Is.EqualTo(depth0Row.worldBound.x + measuredValue.Value).Within(1f),
                    "the depth 1 row's own content must start exactly one indent further right than the depth 0 row's, with the grip gone");
            });
        }
    }
}
#endif
