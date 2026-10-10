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
    /// The actions bar's overflow: the adds in priority order, as many shown as the width allows, the rest in
    /// "+ More", and Capture icons, Convert and the Wheel fixed at the right end.
    /// </summary>
    public class ToolbarOverflowTests
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

        AvatarDoctorView View()
        {
            var menu = avatar.AddMenu();
            return new AvatarDoctorView(new SerializedObject(menu), menu);
        }

        static bool Shown(VisualElement e) => e.resolvedStyle.display != DisplayStyle.None;

        static List<string> VisibleAdds(AvatarDoctorView view) => view.Tree.AddButtons.Where(Shown).Select(b => b.name).ToList();

        static Toolbar Bar(AvatarDoctorView view) => view.Tree.Q<Toolbar>("ad-toolbar-actions");

        static void AssertSplit(AvatarDoctorView view, float width)
        {
            var all = view.Tree.AddButtons.Select(b => b.name).ToList();
            var shown = VisibleAdds(view);
            var more = view.Tree.Q<Button>("ad-more");
            Assert.That(shown, Is.Not.Empty, width + " px shows at least the first add");
            Assert.That(shown, Is.EqualTo(all.Take(shown.Count).ToList()), width + " px shows the top of the priority list");
            var hidden = view.Tree.AddButtons.Skip(shown.Count).Select(b => string.IsNullOrEmpty(b.text) ? b.Q<Label>().text : b.text).ToList();
            Assert.That(view.Tree.MoreMenu.Select(i => i.Text), Is.EqualTo(hidden), width + " px: + More holds exactly the hidden adds, in order");
            Assert.That(Shown(more), Is.EqualTo(hidden.Count > 0), "+ More shows only while something is hidden");

            var bar = Bar(view);
            var items = bar.Children().Where(c => !c.ClassListContains("ad-spacer") && Shown(c)).OrderBy(c => c.worldBound.x).ToList();
            for (int i = 0; i + 1 < items.Count; i++)
                Assert.That(items[i].worldBound.xMax, Is.LessThanOrEqualTo(items[i + 1].worldBound.x + 0.5f), items[i].name + " overlaps " + items[i + 1].name);
            foreach (var item in items)
            {
                Assert.That(item.worldBound.x, Is.GreaterThanOrEqualTo(bar.worldBound.x - 1.5f), item.name + " clips on the left");
                Assert.That(item.worldBound.xMax, Is.LessThanOrEqualTo(bar.worldBound.xMax + 1.5f), item.name + " clips on the right");
            }
            Assert.That(bar.Q<Button>("ad-wheel-toggle").worldBound.xMax, Is.EqualTo(bar.worldBound.xMax).Within(1.5f), "the Wheel stays at the right edge");
        }

        [Test]
        public void CaptureIconsSitsBesideConvert()
        {
            var view = View();
            LayoutProbe.Measure(view, () =>
            {
                var bar = Bar(view);
                var capture = bar.Q<Button>("ad-capture-missing");
                var convert = bar.Q<Button>("ad-import-open");
                var wheel = bar.Q<Button>("ad-wheel-toggle");
                Assert.That(capture, Is.TypeOf<ToolbarButton>());
                Assert.That(capture.text, Is.EqualTo(L.Text("ad.ui.toolbar.capture")));
                Assert.That(capture.tooltip, Is.EqualTo(L.Text("ad.ui.capture_missing.tip")));
                var children = bar.Children().ToList();
                Assert.That(children.IndexOf(convert), Is.EqualTo(children.IndexOf(capture) + 1), "Convert follows Capture icons");
                Assert.That(children.IndexOf(wheel), Is.EqualTo(children.Count - 1), "the Wheel is last");
                Assert.That(capture.worldBound.xMax, Is.LessThanOrEqualTo(convert.worldBound.x + 0.5f));
                Assert.That(convert.worldBound.xMax, Is.LessThanOrEqualTo(wheel.worldBound.x + 0.5f));
                Assert.That(wheel.worldBound.xMax, Is.EqualTo(bar.worldBound.xMax).Within(1.5f), "the group ends at the bar's right edge");
                Assert.That(view.Tree.AddButtons.Any(b => b.name == "ad-capture-missing"), Is.False, "it is no longer an add");
            }, 800f);
        }

        [Test]
        public void OverflowShowsWhatFitsAtEachWidth()
        {
            int previous = 0;
            foreach (var width in new[] { 360f, 800f, 1800f })
            {
                var view = View();
                LayoutProbe.Measure(view, () =>
                {
                    AssertSplit(view, width);
                    int shown = VisibleAdds(view).Count;
                    Assert.That(shown, Is.GreaterThanOrEqualTo(previous), "a wider bar never shows fewer adds");
                    previous = shown;
                    if (width == 360f) Assert.That(shown, Is.EqualTo(3), "360 px shows exactly three adds and + More");
                    if (width == 800f) Assert.That(shown, Is.InRange(6, 8), "800 px shows six to eight adds and folds the rest");
                    if (width == 1800f) Assert.That(shown, Is.EqualTo(view.Tree.AddButtons.Count), "1800 px shows every add");
                }, width);
            }
        }

        [Test]
        public void NarrowBarShowsTheRightGroupAsIcons()
        {
            var view = View();
            LayoutProbe.Measure(view, () =>
            {
                var bar = Bar(view);
                foreach (var name in new[] { "ad-capture-missing", "ad-wheel-toggle" })
                {
                    var button = bar.Q<Button>(name);
                    Assert.That(button.text, Is.Empty, name + " is icon only below 520 px");
                    var icon = button.Q<Image>(className: "ad-toolbar-icon");
                    Assert.That(icon, Is.Not.Null);
                    Assert.That(icon.image, Is.Not.Null, name + " found a built in icon in this Unity version");
                    Assert.That(button.tooltip, Is.Not.Empty, name + " keeps its tooltip");
                }
                Assert.That(bar.Q<Button>("ad-import-open").text, Is.EqualTo(L.Text("ad.ui.toolbar.convert")), "Convert keeps its text");
            }, 360f);
            LayoutProbe.Measure(view, () =>
            {
                var bar = Bar(view);
                Assert.That(bar.Q<Button>("ad-capture-missing").text, Is.EqualTo(L.Text("ad.ui.toolbar.capture")));
                Assert.That(bar.Q<Button>("ad-wheel-toggle").text, Is.EqualTo(L.Text("ad.ui.wheel")));
                Assert.That(bar.Q<Image>(className: "ad-toolbar-icon"), Is.Null, "wide again: text, no icons");
            }, 800f);
        }

        [Test]
        public void MoreDisappearsWhenEverythingFits()
        {
            var view = View();
            LayoutProbe.Measure(view, () =>
            {
                Assert.That(Shown(view.Tree.Q<Button>("ad-more")), Is.False);
                Assert.That(view.Tree.MoreMenu, Is.Empty);
                Assert.That(view.Tree.AddButtons.All(Shown), Is.True);
            }, 1800f);
        }

        [Test]
        public void OverflowRecomputesOnResize()
        {
            var view = View();
            int narrow = -1;
            LayoutProbe.Measure(view, () => { narrow = VisibleAdds(view).Count; AssertSplit(view, 360f); }, 360f);
            LayoutProbe.Measure(view, () =>
            {
                Assert.That(VisibleAdds(view).Count, Is.EqualTo(view.Tree.AddButtons.Count), "widening shows everything");
                AssertSplit(view, 1800f);
            }, 1800f);
            LayoutProbe.Measure(view, () =>
            {
                Assert.That(VisibleAdds(view).Count, Is.EqualTo(narrow), "narrowing again folds the same adds back");
                AssertSplit(view, 360f);
            }, 360f);
        }

        [Test]
        public void ContactStartsButtonOpensTheThreeStarts()
        {
            var view = View();
            LayoutProbe.Measure(view, () =>
            {
                var contact = view.Tree.Q<Button>("ad-add-contact");
                Assert.That(contact, Is.TypeOf<ToolbarButton>());
                Assert.That(Shown(contact), Is.True);
                Assert.That(contact.Q<Image>(className: "ad-dropdown-arrow"), Is.Not.Null, "the dropdown arrow is a flow child");
                UiTestEvents.SimulateClick(contact);
                var root = Resources.FindObjectsOfTypeAll<EditorWindow>().First(w => w.rootVisualElement.panel == view.panel && w.rootVisualElement.Contains(view)).rootVisualElement;
                var open = root.Q<AdMenu>();
                try
                {
                    Assert.That(open, Is.Not.Null);
                    Assert.That(open.Items.Select(i => i.Text), Is.EqualTo(new[]
                    {
                        L.Text("ad.ui.add.contact_starts.headpat"), L.Text("ad.ui.add.contact_starts.eye_touch"), L.Text("ad.ui.add.contact_starts.nose_boop"),
                    }));
                    Assert.That(open.Items.All(i => !string.IsNullOrEmpty(i.Description)), Is.True);
                }
                finally { open?.Close(false); }
            }, 1800f);
        }
    }
}
#endif
