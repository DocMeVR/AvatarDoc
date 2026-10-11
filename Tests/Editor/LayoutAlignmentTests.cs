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
    /// The toolbar alignment the creator's screenshot flagged ("the buttons are not aligned
    /// properly", "the + tags and everything are not properly aligned"), measured for real through LayoutProbe
    /// instead of guessed at. A real host window is the only way to get a real worldBound: a plain, unattached
    /// VisualElement tree never resolves a layout pass inside a synchronous test method.
    /// </summary>
    public class LayoutAlignmentTests
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

        /// <summary>Every VisualElement whose own class list holds every one of these classes, walking the whole subtree.</summary>
        static List<VisualElement> AllWith(VisualElement root, params string[] classes)
            => root.Query<VisualElement>().Build().ToList().Where(e => classes.All(e.ClassListContains)).ToList();

        [Test]
        public void ToolbarItemsShareOneTopAndHeight()
        {
            // A row may wrap at the default inspector width (the brief's own global constraint): fixing the
            // dropdown arrow drawing over "+ More"'s own last letter (a real overlap bug, not spacing) made that
            // button genuinely wider, and the actions bar now wraps Wheel to its own line at 360 px. Grouped by
            // the row an item actually landed on, every item still shares that row's own top and height; this is
            // what the alignment the creator flagged actually asks for, not a single line regardless of width.
            var menu = avatar.AddMenu();
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                var toolbar = view.Tree.Q<VisualElement>(className: "ad-toolbar");
                var items = toolbar.Children().Where(c => !c.ClassListContains("ad-spacer") && c.resolvedStyle.display != DisplayStyle.None).ToList();
                Assert.That(items.Count, Is.GreaterThan(1));
                foreach (var row in items.GroupBy(i => Mathf.Round(i.worldBound.y)))
                {
                    float top = row.First().worldBound.y;
                    float height = row.First().worldBound.height;
                    foreach (var item in row)
                    {
                        Assert.That(item.worldBound.y, Is.EqualTo(top).Within(0.5f), item.name + " does not share its own row's top");
                        Assert.That(item.worldBound.height, Is.EqualTo(height).Within(0.5f), item.name + " does not share its own row's height");
                    }
                }
            });
        }

        [Test]
        public void ToolbarTextIsVerticallyCentred()
        {
            // Every item is the same ToolbarButton now. A plain one (Toggle, Wheel) still draws its caption as its
            // own text, its own text element; a dropdown button (+ More, Filter) carries its caption as a plain
            // Label child instead, since a ToolbarButton draws its own text across the whole content box
            // regardless of any appended child, which used to draw the dropdown arrow over the last letter.
            var menu = avatar.AddMenu();
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                var toolbar = view.Tree.Q<VisualElement>(className: "ad-toolbar");
                var more = toolbar.Q<ToolbarButton>("ad-more");
                var moreLabel = more.Q<Label>();
                Assert.That(moreLabel, Is.Not.Null, "+ More's own caption must exist to be centred");
                AssertCentredWithin(more, moreLabel, 1f, "+ More's own caption");
                var moreArrow = more.Q(className: "ad-dropdown-arrow");
                Assert.That(moreArrow, Is.Not.Null, "+ More's own dropdown arrow must exist to be centred");
                AssertCentredWithin(more, moreArrow, 1f, "+ More's own dropdown arrow");
                Assert.That(moreArrow.worldBound.xMin, Is.GreaterThanOrEqualTo(moreLabel.worldBound.xMax - 0.5f), "the dropdown arrow must sit clear of the caption, never over its last letter");

                var wheel = toolbar.Q<ToolbarButton>("ad-wheel-toggle");
                AssertCentredWithin(wheel, wheel.Q(className: "ad-toolbar-icon"), 1f, "Wheel icon (icon only below 520 px)");

                var addToggle = toolbar.Q<ToolbarButton>("ad-add-toggle");
                AssertCentredWithin(addToggle, addToggle, 1f, "+ Toggle (a ToolbarButton is its own text element)");

                var findBar = view.Tree.Q<VisualElement>("ad-toolbar-find");
                var filter = findBar.Q<ToolbarButton>("ad-tag-filter");
                var filterLabel = filter.Q<Label>();
                Assert.That(filterLabel, Is.Not.Null, "Filter's own caption must exist to be centred");
                AssertCentredWithin(filter, filterLabel, 1f, "Filter's own caption");
                var filterIcon = filter.Q(className: "ad-toolbarmenu-icon");
                Assert.That(filterIcon, Is.Not.Null, "the filter funnel icon must exist to be centred");
                AssertCentredWithin(filter, filterIcon, 1f, "the filter funnel icon");
                var filterArrow = filter.Q(className: "ad-dropdown-arrow");
                Assert.That(filterArrow, Is.Not.Null, "the filter's own dropdown arrow must exist to be centred");
                AssertCentredWithin(filter, filterArrow, 1f, "the filter's own dropdown arrow");
                Assert.That(filterArrow.worldBound.xMin, Is.GreaterThanOrEqualTo(filterLabel.worldBound.xMax - 0.5f), "the dropdown arrow must sit clear of the caption, never over its last letter");
            });
        }

        /// <summary>Fails unless child's own vertical centre sits within tolerance px of container's, naming what was checked.</summary>
        static void AssertCentredWithin(VisualElement container, VisualElement child, float tolerance, string what)
        {
            Assert.That(child, Is.Not.Null, what + " must exist to be centred");
            float containerCentre = container.worldBound.y + container.worldBound.height / 2f;
            float childCentre = child.worldBound.y + child.worldBound.height / 2f;
            Assert.That(childCentre, Is.EqualTo(containerCentre).Within(tolerance), what + " is not centred on its own item's vertical centre");
        }

        [Test]
        public void PlusButtonsMatchTheirRow()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            var trigger = menu.AddEntry(EntryKind.Trigger, "Poke");
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                // "+ Add effect": alone in its own row, so it is checked against that row's own bounds.
                view.Tree.SelectEntry(toggle.id);
                LayoutProbe.Validate(view);
                var addEffect = view.Detail.Q<Button>("ad-add-effect");
                AssertCentredWithin(addEffect.parent, addEffect, 1f, "+ Add effect");

                // "+ And" and "+ Or": two buttons sharing one row, so they must match each other's height too.
                view.Tree.SelectEntry(trigger.id);
                LayoutProbe.Validate(view);
                var addAnd = view.Detail.Q<Button>("ad-when-add-and");
                var addOr = view.Detail.Q<Button>("ad-when-add-or");
                Assert.That(addAnd.worldBound.height, Is.EqualTo(addOr.worldBound.height).Within(0.5f), "+ And and + Or must share one height");
                AssertCentredWithin(addAnd.parent, addAnd, 1f, "+ And");
                AssertCentredWithin(addOr.parent, addOr, 1f, "+ Or");

                // The generic "+ Add" section header button (TargetListView.Objects section), against its own row.
                var objectsAdd = view.Detail.Q<TargetListView>("ad-objects").Query<Button>().Build().First(b => b.text == L.Text("ad.ui.add"));
                AssertCentredWithin(objectsAdd.parent, objectsAdd, 1f, "+ Add (the objects section header)");
            });
        }

        [Test]
        public void ChipTextIsCentred()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(toggle.id);
                LayoutProbe.Validate(view);
                var parameterChip = view.Detail.Q<Label>("ad-parameter-chip");
                AssertCentredWithin(parameterChip, parameterChip, 1f, "the parameter chip's own text (the chip is its own text element)");

                // Toggling a tag filters the tree, so it runs after everything above has already been measured.
                view.Tree.ToggleTag(view.Tree.AvailableTags().First());
                LayoutProbe.Validate(view);
                var filterChip = view.Tree.Q<VisualElement>(className: "ad-filter-chip");
                var filterChipLabel = filterChip.Q<Label>();
                AssertCentredWithin(filterChip, filterChipLabel, 1f, "a tag filter chip's own label");
            });
        }
    }
}
#endif
