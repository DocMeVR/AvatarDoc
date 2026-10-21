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

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The Simple and Advanced switch at the very top of the view: remembered for the person, applied to every open
    /// view without building the plan again, and never hiding anything the creator made.
    /// </summary>
    public class SimpleModeTests
    {
        static readonly string[] SimpleAdds = { "ad-add-toggle", "ad-add-radial", "ad-add-submenu", "ad-outfit-open" };

        static readonly string[] AdvancedOnlyButtons =
        {
            "ad-add-trigger", "ad-add-override", "ad-add-preset", "ad-add-grabbable", "ad-add-animator", "ad-add-menu-asset", "ad-add-parameter",
            "ad-add-gesture-faces", "ad-add-contact", "ad-capture-missing", "ad-import-open", "ad-wheel-toggle", "ad-tag-filter",
        };

        TestAvatar avatar;
        MemoryViewPrefs prefs;
        bool realHadKey;
        string realValue;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
            TagFilterMemory.ClearAll();
            prefs = new MemoryViewPrefs();
            realHadKey = EditorPrefs.HasKey(ViewModes.PrefKey);
            realValue = EditorPrefs.GetString(ViewModes.PrefKey, "");
        }

        [TearDown]
        public void TearDown()
        {
            try { avatar.Dispose(); }
            finally
            {
                AvatarPlanCache.Invalidate();
                PreviewState.ClearAll();
                TagFilterMemory.ClearAll();
            }
        }

        /// <summary>
        /// Read only: the person's real mode key must be exactly as the test found it. Called at the end of every test
        /// rather than from TearDown, because the runner logs a failing TearDown without counting it, so a leak there
        /// would pass unseen.
        /// </summary>
        void AssertRealModeUntouched()
        {
            Assert.That(EditorPrefs.HasKey(ViewModes.PrefKey), Is.EqualTo(realHadKey), "the real EditorPrefs key for the mode was touched");
            Assert.That(EditorPrefs.GetString(ViewModes.PrefKey, ""), Is.EqualTo(realValue), "the real EditorPrefs value for the mode was changed");
        }

        AvatarDoctorView View(AvatarDoc menu, bool simple = false, bool wide = false, IViewPrefs store = null)
        {
            var use = store ?? prefs;
            if (simple) ViewModes.Save(use, ViewMode.Simple);
            return new AvatarDoctorView(new SerializedObject(menu), menu, wide, use);
        }

        MenuEntry Toggle(AvatarDoc menu, string name)
        {
            var entry = menu.AddEntry(EntryKind.Toggle, name);
            entry.objects.Add(new ObjectTarget { target = avatar.Hat });
            return entry;
        }

        static bool ClassHidden(VisualElement e, VisualElement root = null)
        {
            if (e == null) return true;
            for (var p = e; p != null && p != root; p = p.parent)
                if (p.ClassListContains("ad-hidden")) return true;
            return false;
        }

        static bool Shown(VisualElement e, VisualElement root = null) => !ClassHidden(e, root);

        static bool Painted(VisualElement e)
        {
            if (e == null) return false;
            for (var p = e; p != null; p = p.parent)
                if (p.resolvedStyle.display == DisplayStyle.None) return false;
            return e.worldBound.width > 0f && e.worldBound.height > 0f;
        }

        static void AssertHidden(VisualElement root, string name)
        {
            var element = root.Q<VisualElement>(name);
            Assert.That(element, Is.Not.Null, name + " exists, hidden rather than removed");
            Assert.That(element.ClassListContains("ad-hidden"), Is.True, name + " carries the hidden class in Simple");
        }

        static void AssertShown(VisualElement root, string name)
        {
            var element = root.Q<VisualElement>(name);
            Assert.That(element, Is.Not.Null, name + " exists");
            Assert.That(Shown(element, root), Is.True, name + " stays in Simple");
        }

        static Toggle ToggleLabelled(VisualElement root, string label) => root.Query<Toggle>().Build().ToList().FirstOrDefault(t => t.label == label);

        static TextField FieldTipped(VisualElement root, string tooltipKey) => root.Query<TextField>().Build().ToList().FirstOrDefault(t => t.tooltip == L.Text(tooltipKey));

        static IEnumerable<VisualElement> Descendants(VisualElement root)
        {
            foreach (var child in root.Children())
            {
                yield return child;
                foreach (var d in Descendants(child)) yield return d;
            }
        }

        static void Pointer<T>(VisualElement target, EventType type) where T : PointerEventBase<T>, new()
        {
            using (var e = PointerEventBase<T>.GetPooled(new Event { type = type, button = 0, mousePosition = target.worldBound.center }))
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        static void Click(VisualElement target)
        {
            Pointer<PointerDownEvent>(target, EventType.MouseDown);
            Pointer<PointerUpEvent>(target, EventType.MouseUp);
        }

        [Test]
        public void TheSwitchSitsAtTheVeryTopAboveTheStatusLine()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "A").objects.Add(new ObjectTarget { target = null });
            foreach (var wide in new[] { false, true })
            {
                var view = View(menu, wide: wide);
                var row = view.ElementAt(0);
                Assert.That(row.name, Is.EqualTo("ad-mode-row"), "the very first child of the view (wide " + wide + ")");
                Assert.That(row.Contains(view.ModeChoice), Is.True);
                Assert.That(view.ModeChoice.name, Is.EqualTo("ad-mode"));
                Assert.That(view.ModeChoice.Items.Select(b => b.text), Is.EqualTo(new[] { L.Text("ad.ui.mode.simple"), L.Text("ad.ui.mode.advanced") }));
                Assert.That(L.Text("ad.ui.mode.simple"), Is.EqualTo("Simple"));
                Assert.That(L.Text("ad.ui.mode.advanced"), Is.EqualTo("Advanced"));
                Assert.That(view.ModeChoice.tooltip, Is.EqualTo("Simple shows only what most avatars need; Advanced shows every tool. Nothing you made is hidden."));
                Assert.That(view.ModeChoice.Index, Is.EqualTo(1), "Advanced is the default");
                var all = view.Children().ToList();
                Assert.That(all.IndexOf(row), Is.LessThan(all.IndexOf(view.Header)), "above the header, which holds the status line");
                LayoutProbe.Measure(view, () =>
                {
                    var status = view.Header.Q<VisualElement>("ad-status-row");
                    Assert.That(view.ModeChoice.worldBound.yMax, Is.LessThanOrEqualTo(status.worldBound.y), "the switch sits above the status line");
                    Assert.That(view.ModeChoice.worldBound.y, Is.LessThan(view.Header.worldBound.y), "and above the header's own box");
                    Assert.That(view.ModeChoice.worldBound.xMax, Is.LessThanOrEqualTo(view.worldBound.xMax + 0.5f));
                    Assert.That(view.ModeChoice.worldBound.height, Is.EqualTo(20f).Within(1f));
                }, wide ? 800f : 360f);
            }
            AssertRealModeUntouched();
        }

        [Test]
        public void TheSwitchWorksWhileTheComponentIsLocked()
        {
            var menu = avatar.AddMenu();
            menu.locked = true;
            var view = View(menu);
            Assert.That(view.ModeChoice.enabledInHierarchy, Is.True, "the mode is a view preference, not an edit, so the lock never touches it");
            view.ModeChoice.Pick(0);
            Assert.That(view.Mode, Is.EqualTo(ViewMode.Simple));
            Assert.That(menu.locked, Is.True);
            AssertRealModeUntouched();
        }

        [Test]
        public void ModeIsRememberedPerPersonNotPerComponent()
        {
            var first = avatar.AddMenu();
            var otherObject = new GameObject("Second");
            otherObject.transform.SetParent(avatar.Root.transform);
            var second = avatar.AddMenu(otherObject);

            var firstView = View(first);
            Assert.That(firstView.Mode, Is.EqualTo(ViewMode.Advanced), "nothing changes for anyone who never touches the switch");
            Assert.That(prefs.GetString(ViewModes.PrefKey, "unset"), Is.EqualTo("unset"), "looking at the view stores nothing");

            firstView.ModeChoice.Pick(0);

            Assert.That(prefs.GetString(ViewModes.PrefKey, "unset"), Is.EqualTo("Simple"), "one key for every AvatarDoc");
            Assert.That(ViewModes.PrefKey, Is.EqualTo("DocMe.AvatarDoctor.Mode"));
            var secondView = View(second);
            Assert.That(secondView.Mode, Is.EqualTo(ViewMode.Simple), "another component follows the person's choice");
            Assert.That(secondView.ModeChoice.Index, Is.EqualTo(0));
            Assert.That(View(first).Mode, Is.EqualTo(ViewMode.Simple), "the same component, opened again");

            Assert.That(View(first, store: new MemoryViewPrefs()).Mode, Is.EqualTo(ViewMode.Advanced), "another person's store is their own");

            secondView.ModeChoice.Pick(1);
            Assert.That(prefs.GetString(ViewModes.PrefKey, "unset"), Is.EqualTo("Advanced"));
            Assert.That(View(first).Mode, Is.EqualTo(ViewMode.Advanced));
            AssertRealModeUntouched();
        }

        [Test]
        public void TheInspectorEditorReadsTheModeFromItsOwnStore()
        {
            var first = avatar.AddMenu();
            var otherObject = new GameObject("Second");
            otherObject.transform.SetParent(avatar.Root.transform);
            var second = avatar.AddMenu(otherObject);
            ViewModes.Save(prefs, ViewMode.Simple);

            var oldViewPrefs = AvatarDocEditor.MakeViewPrefs;
            var oldTipStore = AvatarDocEditor.MakeTipStore;
            UnityEditor.Editor editor = null;
            try
            {
                AvatarDocEditor.MakeViewPrefs = () => prefs;
                AvatarDocEditor.MakeTipStore = () => new MemoryTipStore();
                editor = UnityEditor.Editor.CreateEditor(second);
                var view = ((AvatarDocEditor)editor).CreateInspectorGUI() as AvatarDoctorView;
                Assert.That(view, Is.Not.Null);
                Assert.That(view.Mode, Is.EqualTo(ViewMode.Simple), "the inspector of any component starts in the person's mode");
                Assert.That(view.Tree.AddButtons.Select(b => b.name), Is.EqualTo(SimpleAdds));
                view.ModeChoice.Pick(1);
                Assert.That(ViewModes.Load(prefs), Is.EqualTo(ViewMode.Advanced), "the inspector writes the store it was given");
                Assert.That(View(first).Mode, Is.EqualTo(ViewMode.Advanced));
            }
            finally
            {
                AvatarDocEditor.MakeViewPrefs = oldViewPrefs;
                AvatarDocEditor.MakeTipStore = oldTipStore;
                if (editor != null) UnityEngine.Object.DestroyImmediate(editor);
            }
            AssertRealModeUntouched();
        }

        [Test]
        public void SwitchingModeAppliesToEveryOpenView()
        {
            var first = avatar.AddMenu();
            var otherObject = new GameObject("Second");
            otherObject.transform.SetParent(avatar.Root.transform);
            var second = avatar.AddMenu(otherObject);
            var thirdObject = new GameObject("Third");
            thirdObject.transform.SetParent(avatar.Root.transform);
            var third = avatar.AddMenu(thirdObject);
            Toggle(first, "Hat");
            Toggle(second, "Glasses");

            int listeners = AvatarDoctorView.ModeListenerCount;
            var a = View(first);
            var b = View(second);
            var c = View(third);
            Assert.That(AvatarDoctorView.ModeListenerCount, Is.EqualTo(listeners), "a view that is on no panel listens to nothing");
            LayoutProbe.Measure(a, () => LayoutProbe.Measure(b, () =>
            {
                Assert.That(AvatarDoctorView.ModeListenerCount, Is.EqualTo(listeners + 2), "two views are on a panel and listen");
                Assert.That(a.Mode, Is.EqualTo(ViewMode.Advanced));
                Assert.That(b.Mode, Is.EqualTo(ViewMode.Advanced));

                Click(a.ModeChoice.Items[0]);

                Assert.That(a.Mode, Is.EqualTo(ViewMode.Simple), "the pressed view switches");
                Assert.That(b.Mode, Is.EqualTo(ViewMode.Simple), "the other open view follows at once");
                Assert.That(b.ModeChoice.Index, Is.EqualTo(0), "and its switch shows it");
                Assert.That(b.Tree.AddButtons.Select(x => x.name), Is.EqualTo(SimpleAdds), "the other view's toolbar changed");
                Assert.That(Shown(b.Header.Q<VisualElement>("ad-settings-fold-header")), Is.False, "and its header");
                Assert.That(ViewModes.Load(prefs), Is.EqualTo(ViewMode.Simple));
                Assert.That(c.Mode, Is.EqualTo(ViewMode.Advanced), "a view on no panel is not reached");

                Click(b.ModeChoice.Items[1]);
                Assert.That(a.Mode, Is.EqualTo(ViewMode.Advanced), "and back, from the other view");
                Assert.That(a.Tree.AddButtons.Count, Is.EqualTo(13));
                Assert.That(a.ModeChoice.Index, Is.EqualTo(1));

                Click(a.ModeChoice.Items[0]);
            }));

            Assert.That(AvatarDoctorView.ModeListenerCount, Is.EqualTo(listeners), "both views let go when they left their panels");
            Assert.That(a.Mode, Is.EqualTo(ViewMode.Simple));
            Assert.That(b.Mode, Is.EqualTo(ViewMode.Simple));
            LayoutProbe.Measure(c, () =>
            {
                Assert.That(AvatarDoctorView.ModeListenerCount, Is.EqualTo(listeners + 1));
                Assert.That(c.Mode, Is.EqualTo(ViewMode.Simple), "a view that comes onto a panel reads the person's choice again");
                Assert.That(c.ModeChoice.Index, Is.EqualTo(0));

                Click(c.ModeChoice.Items[1]);

                Assert.That(c.Mode, Is.EqualTo(ViewMode.Advanced));
                Assert.That(a.Mode, Is.EqualTo(ViewMode.Simple), "a view that left its panel is not reached any more");
                Assert.That(b.Mode, Is.EqualTo(ViewMode.Simple));
            });
            Assert.That(AvatarDoctorView.ModeListenerCount, Is.EqualTo(listeners));
            AssertRealModeUntouched();
        }

        [Test]
        public void TwoViewsOnSeparateStoresDoNotReachEachOther()
        {
            var first = avatar.AddMenu();
            var otherObject = new GameObject("Second");
            otherObject.transform.SetParent(avatar.Root.transform);
            var second = avatar.AddMenu(otherObject);
            Toggle(first, "Hat");
            Toggle(second, "Glasses");
            var storeA = new MemoryViewPrefs();
            var storeB = new MemoryViewPrefs();
            var a = View(first, store: storeA);
            var b = View(second, store: storeB);

            LayoutProbe.Measure(a, () => LayoutProbe.Measure(b, () =>
            {
                Assert.That(AvatarDoctorView.ModeListenerCount, Is.GreaterThanOrEqualTo(2), "both views are on a panel and listen");

                Click(a.ModeChoice.Items[0]);

                Assert.That(a.Mode, Is.EqualTo(ViewMode.Simple));
                Assert.That(ViewModes.Load(storeA), Is.EqualTo(ViewMode.Simple));
                Assert.That(b.Mode, Is.EqualTo(ViewMode.Advanced), "another person's view, with a store of its own, is not switched by a press elsewhere");
                Assert.That(b.ModeChoice.Index, Is.EqualTo(1));
                Assert.That(b.Tree.AddButtons.Count, Is.EqualTo(13), "and keeps its whole toolbar");
                Assert.That(Shown(b.Header.Q<VisualElement>("ad-settings-fold-header")), Is.True, "and its header");
                Assert.That(storeB.GetString(ViewModes.PrefKey, "unset"), Is.EqualTo("unset"), "and its store is never written by a press elsewhere");

                Click(b.ModeChoice.Items[0]);
                Click(a.ModeChoice.Items[1]);

                Assert.That(a.Mode, Is.EqualTo(ViewMode.Advanced));
                Assert.That(ViewModes.Load(storeA), Is.EqualTo(ViewMode.Advanced));
                Assert.That(b.Mode, Is.EqualTo(ViewMode.Simple), "each view follows its own store only");
                Assert.That(ViewModes.Load(storeB), Is.EqualTo(ViewMode.Simple));
            }));
            AssertRealModeUntouched();
        }

        [Test]
        public void SwitchingModeDoesNotRebuildThePlan()
        {
            var menu = avatar.AddMenu();
            var hat = Toggle(menu, "Hat");
            var trigger = menu.AddEntry(EntryKind.Trigger, "Poke");
            var view = View(menu);
            view.Tree.SelectEntry(hat.id);
            int builds = AvatarPlanCache.PlanBuilds;
            int previewBuilds = AvatarPlanCache.PreviewPlanBuilds;
            var plan = view.Plan;
            int dirty = EditorUtility.GetDirtyCount(menu);
            string data = EditorJsonUtility.ToJson(menu);

            LayoutProbe.Measure(view, () =>
            {
                int undoGroup = Undo.GetCurrentGroup();
                view.ModeChoice.Pick(0);
                view.Tree.SelectEntry(trigger.id);
                view.ModeChoice.Pick(1);
                view.Tree.SelectEntry(hat.id);
                view.ModeChoice.Pick(0);
                view.CheckVersion();
                Assert.That(Undo.GetCurrentGroup(), Is.EqualTo(undoGroup), "nothing was recorded for Undo");
            });

            Assert.That(AvatarPlanCache.PlanBuilds, Is.EqualTo(builds), "no plan was built");
            Assert.That(AvatarPlanCache.PreviewPlanBuilds, Is.EqualTo(previewBuilds), "and no preview plan");
            Assert.That(view.Plan, Is.SameAs(plan), "the view still holds the very same plan");
            Assert.That(EditorUtility.GetDirtyCount(menu), Is.EqualTo(dirty), "the component was not touched");
            Assert.That(EditorJsonUtility.ToJson(menu), Is.EqualTo(data), "the entries are exactly as they were");
            AssertRealModeUntouched();
        }

        [Test]
        public void SwitchingBackRestoresTheAdvancedView()
        {
            var menu = avatar.AddMenu();
            var hat = Toggle(menu, "Hat");
            hat.exclusiveGroup = "Outfits";
            Toggle(menu, "Glasses");
            menu.AddEntry(EntryKind.Trigger, "Poke");
            var view = View(menu);
            view.Tree.SelectEntry(hat.id);

            string Signature() => string.Join(";", Descendants(view).Where(e => Painted(e) && !string.IsNullOrEmpty(e.name)).Select(e => e.name));

            LayoutProbe.Measure(view, () =>
            {
                string advanced = Signature();
                Assert.That(advanced, Does.Contain("ad-wheel-toggle").And.Contain("ad-tag-filter").And.Contain("ad-import-open").And.Contain("ad-capture-missing"));
                view.ModeChoice.Pick(0);
                LayoutProbe.Validate(view);
                string simple = Signature();
                Assert.That(simple, Is.Not.EqualTo(advanced));
                view.ModeChoice.Pick(1);
                LayoutProbe.Validate(view);
                Assert.That(Signature(), Is.EqualTo(advanced), "everything that was shown before shows again, and nothing else");
            }, 800f);
            AssertRealModeUntouched();
        }

        [Test]
        public void SimpleHidesTheAdvancedToolbarAndSettings()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Problem").objects.Add(new ObjectTarget { target = null });
            var view = View(menu, simple: true);

            foreach (var name in AdvancedOnlyButtons) AssertHidden(view.Tree, name);
            AssertHidden(view.Tree, "ad-filter-chips");
            AssertHidden(view.Header, "ad-settings-fold-header");
            AssertHidden(view.Header, "ad-settings-fold-content");
            foreach (var name in SimpleAdds) AssertShown(view.Tree, name);
            AssertShown(view.Tree, "ad-search");
            AssertShown(view.Header, "ad-status-row");
            AssertShown(view.Header, "ad-summary-toggle");
            AssertShown(view.Header, "ad-ready-show");
            Assert.That(Shown(view.Header.Q<MemoryBar>(), view.Header), Is.True, "the memory bar stays");
            Assert.That(Shown(view.Header.Q<VisualElement>("ad-membar-legend"), view.Header), Is.True);

            LayoutProbe.Measure(view, () =>
            {
                foreach (var name in AdvancedOnlyButtons.Concat(new[] { "ad-more", "ad-settings-fold-header", "ad-settings-fold-content" }))
                {
                    var element = view.Q<VisualElement>(name);
                    Assert.That(element, Is.Not.Null, name);
                    Assert.That(Painted(element), Is.False, name + " takes no room in Simple at 800 px");
                }
                foreach (var name in SimpleAdds.Concat(new[] { "ad-search", "ad-status-row", "ad-summary-toggle", "ad-ready-show" }))
                    Assert.That(Painted(view.Q<VisualElement>(name)), Is.True, name + " is drawn in Simple at 800 px");
                Assert.That(Painted(view.Header.Q<MemoryBar>()), Is.True);
                Assert.That(view.Tree.Q<Button>("ad-more").ClassListContains("ad-hidden"), Is.True, "+ More goes when nothing is left in it");
                Assert.That(view.Tree.MoreMenu, Is.Empty);
            }, 800f);

            view.ModeChoice.Pick(1);
            foreach (var name in AdvancedOnlyButtons.Concat(new[] { "ad-more" }))
                Assert.That(view.Q<VisualElement>(name), Is.Not.Null, name);
            Assert.That(Shown(view.Header.Q<VisualElement>("ad-settings-fold-header")), Is.True, "Advanced shows the Component settings fold");
            AssertRealModeUntouched();
        }

        [Test]
        public void SimpleKeepsOutfitSetAndHidesContact()
        {
            var menu = avatar.AddMenu();
            var view = View(menu, simple: true);
            Assert.That(view.Tree.AddButtons.Select(b => b.name), Is.EqualTo(SimpleAdds), "the four adds, in the toolbar's own order");
            Assert.That(view.Tree.AddButtons.Last().text, Is.EqualTo(L.Text("ad.ui.add.outfit")), "+ Outfit set stays");
            Assert.That(view.Tree.AddButtons.Any(b => b.name == "ad-add-contact"), Is.False, "+ Contact is not in the Simple list");
            Assert.That(view.Tree.Q<Button>("ad-add-contact").ClassListContains("ad-hidden"), Is.True);
            Assert.That(view.Tree.Q<Button>("ad-outfit-open").ClassListContains("ad-hidden"), Is.False);

            LayoutProbe.Measure(view, () =>
            {
                Assert.That(Painted(view.Tree.Q<Button>("ad-outfit-open")), Is.True, "+ Outfit set is drawn");
                Assert.That(Painted(view.Tree.Q<Button>("ad-add-contact")), Is.False, "+ Contact is not");
                AssertSimpleSplit(view, 800f);
            }, 800f);
            LayoutProbe.Measure(view, () => AssertSimpleSplit(view, 360f), 360f);
            LayoutProbe.Measure(view, () =>
            {
                AssertSimpleSplit(view, 200f);
                Assert.That(Shown(view.Tree.Q<Button>("ad-more")), Is.True, "200 px is too narrow for four adds, so + More appears");
                var offered = view.Tree.MoreMenu.Select(i => i.Text).ToList();
                var simpleNames = SimpleAdds.Select(n => view.Tree.Q<Button>(n)).Select(b => string.IsNullOrEmpty(b.text) ? b.Q<Label>().text : b.text).ToList();
                Assert.That(offered, Is.Not.Empty);
                Assert.That(offered, Is.SubsetOf(simpleNames), "+ More holds the rest of the Simple list only");
                Assert.That(offered.Any(t => t == L.Text("ad.ui.add.contact") || t == L.Text("ad.ui.add.trigger") || t == L.Text("ad.ui.add.override")), Is.False);
            }, 200f);
            LayoutProbe.Measure(view, () => AssertSimpleSplit(view, 360f), 360f);
            AssertRealModeUntouched();
        }

        static void AssertSimpleSplit(AvatarDoctorView view, float width)
        {
            var adds = view.Tree.AddButtons;
            Assert.That(adds.Select(b => b.name), Is.EqualTo(SimpleAdds));
            var shown = adds.Where(b => Painted(b)).ToList();
            var more = view.Tree.Q<Button>("ad-more");
            Assert.That(shown, Is.Not.Empty, width + " px shows at least the first add");
            Assert.That(shown.Select(b => b.name), Is.EqualTo(adds.Take(shown.Count).Select(b => b.name)), width + " px shows the top of the Simple list");
            var hidden = adds.Skip(shown.Count).Select(b => string.IsNullOrEmpty(b.text) ? b.Q<Label>().text : b.text).ToList();
            Assert.That(view.Tree.MoreMenu.Select(i => i.Text), Is.EqualTo(hidden), width + " px: + More holds exactly the Simple adds that are hidden");
            Assert.That(Painted(more), Is.EqualTo(hidden.Count > 0), "+ More shows only while something is hidden");
            var bar = view.Tree.Q<Toolbar>("ad-toolbar-actions");
            var items = bar.Children().Where(c => !c.ClassListContains("ad-spacer") && Painted(c)).OrderBy(c => c.worldBound.x).ToList();
            for (int i = 0; i + 1 < items.Count; i++)
                Assert.That(items[i].worldBound.xMax, Is.LessThanOrEqualTo(items[i + 1].worldBound.x + 0.5f), items[i].name + " overlaps " + items[i + 1].name);
            foreach (var item in items)
            {
                Assert.That(item.worldBound.x, Is.GreaterThanOrEqualTo(bar.worldBound.x - 1.5f), item.name + " clips on the left");
                Assert.That(item.worldBound.xMax, Is.LessThanOrEqualTo(bar.worldBound.xMax + 1.5f), item.name + " clips on the right");
            }
        }

        [Test]
        public void ChangingTheWidthKeepsTheSimpleListInOrder()
        {
            var menu = avatar.AddMenu();
            var view = View(menu, simple: true);
            int previous = 0;
            foreach (var width in new[] { 200f, 360f, 800f })
            {
                LayoutProbe.Measure(view, () =>
                {
                    AssertSimpleSplit(view, width);
                    int shown = view.Tree.AddButtons.Count(b => Painted(b));
                    Assert.That(shown, Is.GreaterThanOrEqualTo(previous), "a wider bar never shows fewer adds");
                    Assert.That(view.Tree.VisibleAddCount, Is.EqualTo(shown));
                    previous = shown;
                    if (width == 800f) Assert.That(shown, Is.EqualTo(4));
                }, width);
            }
            AssertRealModeUntouched();
        }

        [Test]
        public void SwitchingModeWhileLaidOutRefitsTheToolbar()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                int advanced = view.Tree.AddButtons.Count(b => Painted(b));
                Assert.That(advanced, Is.InRange(6, 8), "Advanced at 800 px folds the rest into + More");
                view.ModeChoice.Pick(0);
                LayoutProbe.Validate(view);
                AssertSimpleSplit(view, 800f);
                Assert.That(view.Tree.AddButtons.Count(b => Painted(b)), Is.EqualTo(4));
                Assert.That(Painted(view.Tree.Q<Button>("ad-wheel-toggle")), Is.False);
                view.ModeChoice.Pick(1);
                LayoutProbe.Validate(view);
                Assert.That(view.Tree.AddButtons.Count(b => Painted(b)), Is.EqualTo(advanced), "the same split as before");
                Assert.That(Painted(view.Tree.Q<Button>("ad-wheel-toggle")), Is.True);
                Assert.That(Painted(view.Tree.Q<Button>("ad-import-open")), Is.True);
                Assert.That(Painted(view.Tree.Q<Button>("ad-capture-missing")), Is.True);
            }, 800f);
            AssertRealModeUntouched();
        }

        [Test]
        public void RightClickMenusStayCompleteInSimple()
        {
            var menu = avatar.AddMenu();
            var hat = Toggle(menu, "Hat");
            var advancedView = View(menu);
            advancedView.Tree.SelectEntry(hat.id);
            var simpleView = View(menu, simple: true);
            simpleView.Tree.SelectEntry(hat.id);
            var advancedRow = advancedView.Tree.RowFor(hat.id);
            var simpleRow = simpleView.Tree.RowFor(hat.id);
            Assert.That(simpleView.Tree.RowMenu(simpleRow).Select(i => i.Text), Is.EqualTo(advancedView.Tree.RowMenu(advancedRow).Select(i => i.Text)));
            Assert.That(simpleView.Tree.RowActions(simpleRow).Select(a => a.LabelKey), Is.EqualTo(advancedView.Tree.RowActions(advancedRow).Select(a => a.LabelKey)));
            var existing = simpleView.Tree.RowsOfKind(RowKind.Existing).First(r => r.Node == null);
            var existingAdvanced = advancedView.Tree.RowsOfKind(RowKind.Existing).First(r => r.Node == null);
            Assert.That(simpleView.Tree.RowMenu(existing).Select(i => i.Text), Is.EqualTo(advancedView.Tree.RowMenu(existingAdvanced).Select(i => i.Text)));
            AssertRealModeUntouched();
        }

        [Test]
        public void AnActiveTagFilterPausesInSimpleAndComesBack()
        {
            var menu = avatar.AddMenu();
            var hat = Toggle(menu, "Hat");
            var poke = menu.AddEntry(EntryKind.Trigger, "Poke");
            var view = View(menu);
            string tag = TagFilter.TagId(TagFilter.GroupKind, "trigger");
            view.Tree.ToggleTag(tag);
            Assert.That(view.Tree.RowFor(hat.id), Is.Null, "the filter narrows the tree in Advanced");
            Assert.That(view.Tree.RowFor(poke.id), Is.Not.Null);
            Assert.That(Shown(view.Tree.Q<VisualElement>("ad-filter-chips")), Is.True);
            int builds = AvatarPlanCache.PlanBuilds;

            view.ModeChoice.Pick(0);

            Assert.That(view.Tree.RowFor(hat.id), Is.Not.Null, "Simple has no filter button, so a filter nobody can see or clear does not hide rows");
            Assert.That(view.Tree.RowFor(poke.id), Is.Not.Null);
            Assert.That(Shown(view.Tree.Q<VisualElement>("ad-filter-chips")), Is.False);
            Assert.That(Shown(view.Tree.Q<Button>("ad-tag-filter")), Is.False);
            Assert.That(view.Tree.ActiveTags, Is.EquivalentTo(new[] { tag }), "the choice itself is kept");

            view.ModeChoice.Pick(1);

            Assert.That(view.Tree.RowFor(hat.id), Is.Null, "and applies again when Advanced comes back");
            Assert.That(Shown(view.Tree.Q<VisualElement>("ad-filter-chips")), Is.True);
            Assert.That(AvatarPlanCache.PlanBuilds, Is.EqualTo(builds), "drawing the rows again builds no plan");
            AssertRealModeUntouched();
        }

        [Test]
        public void TheWheelStaysHiddenInSimple()
        {
            var menu = avatar.AddMenu();
            Toggle(menu, "Hat");
            var view = View(menu);
            Assert.That(view.Wheel.ClassListContains("ad-hidden"), Is.False, "Advanced shows the wheel");
            view.ModeChoice.Pick(0);
            Assert.That(view.Wheel.ClassListContains("ad-hidden"), Is.True, "Simple has no Wheel button, so it does not draw the wheel either");
            view.ModeChoice.Pick(1);
            Assert.That(view.Wheel.ClassListContains("ad-hidden"), Is.False);
            AssertRealModeUntouched();
        }

        [Test]
        public void TheSettingsFoldShowsInSimpleWhileASettingIsNotAtItsDefault()
        {
            var menu = avatar.AddMenu();
            var view = View(menu, simple: true);
            Assert.That(Shown(view.Header.Q<VisualElement>("ad-settings-fold-header")), Is.False, "everything at its default: hidden");

            menu.installPath = "Outfits";
            AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
            view.Refresh();

            Assert.That(Shown(view.Header.Q<VisualElement>("ad-settings-fold-header")), Is.True, "a changed setting is never hidden");
            Assert.That(view.Header.SettingsFoldDotVisible, Is.True, "closed, with the dot that says a value is not at its default");
            Assert.That(Shown(view.Header.Q<VisualElement>("ad-settings-fold-content")), Is.False, "still closed until it is opened");

            view.Header.ToggleSettingsFold();
            Assert.That(Shown(view.Header.Q<VisualElement>("ad-settings-fold-content")), Is.True);

            menu.installPath = "";
            AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
            view.Refresh();
            Assert.That(Shown(view.Header.Q<VisualElement>("ad-settings-fold-header")), Is.True, "clearing the last changed setting does not take the fold out from under the pointer");
            Assert.That(Shown(view.Header.Q<VisualElement>("ad-settings-fold-content")), Is.True, "nor the settings the creator has open");

            view.ModeChoice.Pick(1);
            view.ModeChoice.Pick(0);
            Assert.That(Shown(view.Header.Q<VisualElement>("ad-settings-fold-header")), Is.False, "the next time the mode changes, the fold holds nothing and Simple hides it again");
            Assert.That(Shown(view.Header.Q<VisualElement>("ad-settings-fold-content")), Is.False);

            menu.parameterPrefix = "Custom";
            AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
            view.Refresh();
            Assert.That(Shown(view.Header.Q<VisualElement>("ad-settings-fold-header")), Is.True, "a setting that changes while Simple is on shows the fold at once");
            AssertRealModeUntouched();
        }

        [Test]
        public void AShownStyleRowStaysUntilAnotherRowIsSelected()
        {
            var menu = avatar.AddMenu();
            var bold = Toggle(menu, "<b>Bold</b>");
            var other = Toggle(menu, "Other");
            var view = View(menu, simple: true);
            var panel = view.Detail;

            view.Tree.SelectEntry(bold.id);
            Assert.That(Shown(panel.Q<VisualElement>("ad-style-color"), panel), Is.True, "a bold name shows its Style row");

            view.Detail.SetNameStyle(null, false, false);

            Assert.That(menu.FindEntry(bold.id).name, Is.EqualTo("Bold"), "the bold really went");
            Assert.That(Shown(panel.Q<VisualElement>("ad-style-color"), panel), Is.True, "the Style row the creator is using does not vanish under the pointer");
            Assert.That(Shown(panel.Q<Toggle>("ad-style-raw"), panel), Is.True, "nor does Rich text tags");

            view.Tree.SelectEntry(other.id);
            Assert.That(Shown(panel.Q<VisualElement>("ad-style-color"), panel), Is.False, "another row is a new decision, and its plain name has no Style row");
            view.Tree.SelectEntry(bold.id);
            Assert.That(Shown(panel.Q<VisualElement>("ad-style-color"), panel), Is.False, "back on the first row its name is plain now, so it has none either");

            view.Tree.SelectEntry(other.id);
            menu.FindEntry(other.id).name = "<i>Other</i>";
            AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
            view.Refresh();
            Assert.That(Shown(panel.Q<VisualElement>("ad-style-color"), panel), Is.True, "a style that appears while the panel is shown shows the row at once");
            AssertRealModeUntouched();
        }

        [Test]
        public void AShownAdvancedFoldStaysUntilAnotherRowIsSelected()
        {
            var menu = avatar.AddMenu();
            var quiet = Toggle(menu, "Quiet");
            quiet.saved = false;
            var other = Toggle(menu, "Other");
            var view = View(menu, simple: true);

            LayoutProbe.Measure(view, () =>
            {
                var panel = view.Detail;
                view.Tree.SelectEntry(quiet.id);
                Assert.That(Shown(panel.Q<AdvancedFold>(), panel), Is.True, "Saved is off, which is not its default");
                Assert.That(panel.Q<AdvancedFold>().DotVisible, Is.True);
                panel.Q<AdvancedFold>().ToggleOpen();
                var saved = ToggleLabelled(panel, L.Text("ad.ui.saved"));
                Assert.That(saved.value, Is.False);

                saved.value = true;

                Assert.That(menu.FindEntry(quiet.id).saved, Is.True, "the creator ticked Saved back on");
                Assert.That(Shown(panel.Q<AdvancedFold>(), panel), Is.True, "the open fold does not vanish under the pointer");
                Assert.That(panel.Q<AdvancedFold>().IsOpen, Is.True);

                view.Tree.SelectEntry(other.id);
                Assert.That(Shown(panel.Q<AdvancedFold>(), panel), Is.False, "another row is a new decision, and nothing differs in its fold");
                view.Tree.SelectEntry(quiet.id);
                Assert.That(Shown(panel.Q<AdvancedFold>(), panel), Is.False, "Saved is on again, so nothing differs here either");

                menu.FindEntry(quiet.id).blendSeconds = 2f;
                AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
                view.Refresh();
                Assert.That(Shown(panel.Q<AdvancedFold>(), panel), Is.True, "a value that changes while the panel is shown brings the fold at once");
            });
            AssertRealModeUntouched();
        }

        [Test]
        public void AShownGroupRowStaysUntilAnotherRowIsSelected()
        {
            var menu = avatar.AddMenu();
            var grouped = Toggle(menu, "Tops");
            grouped.exclusiveGroup = "Tops";
            var other = Toggle(menu, "Other");
            var view = View(menu, simple: true);

            LayoutProbe.Measure(view, () =>
            {
                var panel = view.Detail;
                view.Tree.SelectEntry(grouped.id);
                Assert.That(Shown(FieldTipped(panel, "ad.ui.group.tip"), panel), Is.True, "a toggle in a group shows the group");

                FieldTipped(panel, "ad.ui.group.tip").value = "";

                Assert.That(menu.FindEntry(grouped.id).exclusiveGroup, Is.Empty, "the creator took it out of the group");
                Assert.That(Shown(FieldTipped(panel, "ad.ui.group.tip"), panel), Is.True, "the row stays under the pointer, though the panel was built again for the new group");

                view.Tree.SelectEntry(other.id);
                Assert.That(Shown(FieldTipped(panel, "ad.ui.group.tip"), panel), Is.False, "another row is a new decision");
                view.Tree.SelectEntry(grouped.id);
                Assert.That(Shown(FieldTipped(panel, "ad.ui.group.tip"), panel), Is.False, "and the first one is in no group now");

                menu.FindEntry(grouped.id).exclusiveGroup = "Bottoms";
                AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
                view.Refresh();
                Assert.That(Shown(FieldTipped(panel, "ad.ui.group.tip"), panel), Is.True, "a group set while the panel is shown brings the row at once");
            });
            AssertRealModeUntouched();
        }

        [Test]
        public void FirstUseTipsFollowTheMode()
        {
            var menu = avatar.AddMenu();
            var hat = Toggle(menu, "Hat");
            var view = View(menu);
            string TipText() => view.Header.Q<VisualElement>("ad-tip-header")?.Q<Label>("ad-tip-text")?.text;
            Assert.That(TipText(), Is.EqualTo(L.Text("ad.tip.header")));
            Assert.That(L.Text("ad.tip.header"), Does.Contain("install settings"));

            view.ModeChoice.Pick(0);
            Assert.That(TipText(), Is.EqualTo(L.Text("ad.tip.header.simple")), "Simple does not talk about the fold it hides");
            Assert.That(L.Text("ad.tip.header.simple"), Does.Not.Contain("install settings"));

            view.TipStore.Dismiss(TipKinds.Header);
            view.ModeChoice.Pick(1);
            Assert.That(view.Header.Q<VisualElement>("ad-tip-header"), Is.Null, "a tip dismissed in one mode stays dismissed in the other");

            view.TipStore.ResetAll();
            view.Tree.SelectEntry(hat.id);
            view.ModeChoice.Pick(0);
            Assert.That(view.Detail.Q<VisualElement>("ad-tip-toggle"), Is.Not.Null, "the toggle's own tip still shows in Simple");
            AssertRealModeUntouched();
        }

        [Test]
        public void TheWelcomeCardHidesItsConvertStartInSimple()
        {
            var menu = avatar.AddMenu();
            var go = new GameObject("Fury");
            go.transform.SetParent(avatar.Root.transform);
            var furyToggle = VrcFuryTestHelper.AddToggle(go, "Hat");
            VrcFuryTestHelper.AddAction(furyToggle, VrcFuryReader.ObjectToggleActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "obj", avatar.Hat);
                VrcFuryTestHelper.SetField(a, "mode", 0);
            });
            AvatarPlanCache.Invalidate();
            var advanced = View(menu);
            var convert = advanced.Tree.Q<Button>("ad-welcome-convert");
            Assert.That(convert.parent.ClassListContains("ad-hidden"), Is.False, "Advanced offers to convert what the avatar already has");

            var simple = View(menu, simple: true);
            var simpleConvert = simple.Tree.Q<Button>("ad-welcome-convert");
            Assert.That(simpleConvert.parent.ClassListContains("ad-hidden"), Is.True, "Simple does not");
            Assert.That(simple.Tree.WelcomeScans, Is.EqualTo(0), "and does not scan the avatar to find out");
            Assert.That(simple.Tree.Q<Button>("ad-welcome-toggle"), Is.Not.Null);
            Assert.That(Shown(simple.Tree.Q<Button>("ad-welcome-toggle"), simple.Tree), Is.True, "Make a toggle stays");
            Assert.That(Shown(simple.Tree.Q<Button>("ad-welcome-outfit"), simple.Tree), Is.True, "Start an outfit set stays");

            simple.ModeChoice.Pick(1);
            Assert.That(simpleConvert.parent.ClassListContains("ad-hidden"), Is.False, "back in Advanced the start is offered again");
            Assert.That(simple.Tree.WelcomeScans, Is.EqualTo(1));
            AssertRealModeUntouched();
        }

        [Test]
        public void SimplePanelsShowOnlyTheBasics()
        {
            var menu = avatar.AddMenu();
            var hat = Toggle(menu, "Hat");
            var eyes = menu.AddEntry(EntryKind.Radial, "Eyes");
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var view = View(menu, simple: true);

            view.Tree.SelectEntry(hat.id);
            var panel = view.Detail;
            AssertShown(panel, "ad-name");
            AssertShown(panel, "ad-icon");
            AssertShown(panel, "ad-capture-icon");
            Assert.That(Shown(ToggleLabelled(panel, L.Text("ad.ui.default_on")), panel), Is.True, "Default on");
            Assert.That(Shown(ToggleLabelled(panel, L.Text("ad.ui.in_menu")), panel), Is.True, "In menu");
            AssertShown(panel, "ad-objects");
            AssertShown(panel, "ad-blendshapes");
            AssertShown(panel, "ad-shape-set-capture");
            Assert.That(panel.Q<TargetListView>("ad-blendshapes").Count, Is.EqualTo(0), "Blend shapes shows with Capture before the toggle has a single shape");
            foreach (var name in new[] { "ad-materials", "ad-properties", "ad-animations", "ad-follows" })
            {
                var section = panel.Q<TargetListView>(name);
                Assert.That(section, Is.Not.Null, name + " is built, so a row added elsewhere shows at once");
                Assert.That(Shown(section, panel), Is.False, name + " is empty, so Simple hides it");
            }
            Assert.That(Shown(panel.Q<VisualElement>("ad-style-color"), panel), Is.False, "the Style row");
            Assert.That(Shown(panel.Q<Toggle>("ad-style-raw"), panel), Is.False, "Rich text tags");
            Assert.That(FieldTipped(panel, "ad.ui.group.tip"), Is.Null.Or.Matches<TextField>(f => !Shown(f, panel)), "Exclusive group");
            Assert.That(Shown(panel.Q<VisualElement>("ad-builds-line"), panel), Is.False, "the Builds line");
            Assert.That(Shown(panel.Q<VisualElement>("ad-preview-choice"), panel), Is.False, "the scene preview control");
            Assert.That(panel.Q<VisualElement>(className: "ad-preview"), Is.Null.Or.Matches<VisualElement>(e => !Shown(e, panel)));
            Assert.That(Shown(panel.Q<AdvancedFold>(), panel), Is.False, "the Advanced fold");
            Assert.That(Shown(panel.Q<Button>("ad-add-effect"), panel), Is.True, "+ Add effect stays");
            Assert.That(panel.EffectSections.Select(s => s.NameKey), Is.EqualTo(new[] { "ad.ui.objects", "ad.ui.blendshapes", "ad.ui.targets.properties" }), "and offers only Objects, Blend shapes and Properties");
            Assert.That(panel.Q<Label>("ad-advanced-item-note"), Is.Null, "a toggle is no advanced item");

            view.Tree.SelectEntry(eyes.id);
            AssertShown(panel, "ad-name");
            AssertShown(panel, "ad-icon");
            AssertShown(panel, "ad-default-value");
            Assert.That(Shown(ToggleLabelled(panel, L.Text("ad.ui.in_menu")), panel), Is.True);
            AssertShown(panel, "ad-blendshapes");
            AssertShown(panel, "ad-shape-set-capture");
            foreach (var name in new[] { "ad-properties", "ad-animations" })
                Assert.That(Shown(panel.Q<TargetListView>(name), panel), Is.False, name);
            Assert.That(Shown(panel.Q<VisualElement>("ad-style-color"), panel), Is.False);
            Assert.That(Shown(panel.Q<VisualElement>("ad-builds-line"), panel), Is.False);
            Assert.That(Shown(panel.Q<Button>("ad-preview-default"), panel), Is.False);
            Assert.That(Shown(panel.Q<Slider>("ad-preview-slider"), panel), Is.False);
            Assert.That(Shown(panel.Q<AdvancedFold>(), panel), Is.False);
            Assert.That(panel.EffectSections.Select(s => s.NameKey), Is.EqualTo(new[] { "ad.ui.blendshapes", "ad.ui.targets.properties" }), "a radial offers the two of those it can have");

            view.Tree.SelectEntry(outfits.id);
            AssertShown(panel, "ad-name");
            AssertShown(panel, "ad-icon");
            Assert.That(Shown(panel.Q<VisualElement>("ad-style-color"), panel), Is.False);
            Assert.That(Shown(panel.Q<VisualElement>("ad-builds-line"), panel), Is.False);
            Assert.That(Shown(panel.Q<AdvancedFold>(), panel), Is.False);
            Assert.That(panel.Q<Button>("ad-add-effect"), Is.Null, "a submenu has no effects");

            view.ModeChoice.Pick(1);
            view.Tree.SelectEntry(hat.id);
            AssertShown(panel, "ad-builds-line");
            AssertShown(panel, "ad-preview-choice");
            Assert.That(Shown(panel.Q<VisualElement>("ad-style-color"), panel), Is.True);
            Assert.That(Shown(panel.Q<Toggle>("ad-style-raw"), panel), Is.True);
            Assert.That(Shown(FieldTipped(panel, "ad.ui.group.tip"), panel), Is.True);
            Assert.That(Shown(panel.Q<AdvancedFold>(), panel), Is.True);
            Assert.That(panel.EffectSections.Count, Is.EqualTo(6), "Advanced offers every kind");
            Assert.That(Shown(panel.Q<TargetListView>("ad-blendshapes"), panel), Is.False, "Advanced keeps its own rule: an empty Blend shapes section waits for + Add effect");
            AssertRealModeUntouched();
        }

        [Test]
        public void SimpleAddEffectOffersOnlyObjectsAndBlendShapes()
        {
            var menu = avatar.AddMenu();
            var hat = Toggle(menu, "Hat");
            var view = View(menu, simple: true);
            view.Tree.SelectEntry(hat.id);
            LayoutProbe.Measure(view, () =>
            {
                UiTestEvents.SimulateClick(view.Detail.Q<Button>("ad-add-effect"));
                var root = Resources.FindObjectsOfTypeAll<EditorWindow>().First(w => w.rootVisualElement.panel == view.panel && w.rootVisualElement.Contains(view)).rootVisualElement;
                var open = root.Q<AdMenu>();
                try
                {
                    Assert.That(open, Is.Not.Null);
                    Assert.That(open.Items.Select(i => i.Text), Is.EqualTo(new[] { L.Text("ad.ui.objects"), L.Text("ad.ui.blendshapes"), L.Text("ad.ui.targets.properties") }));
                    Assert.That(open.Items.All(i => !string.IsNullOrEmpty(i.Description)), Is.True);
                }
                finally { open?.Close(false); }
            }, 400f);
            AssertRealModeUntouched();
        }

        [Test]
        public void SimpleRadialOffersBlendShapesAndProperties()
        {
            var menu = avatar.AddMenu();
            var eyes = menu.AddEntry(EntryKind.Radial, "Eyes");
            var view = View(menu, simple: true);
            view.Tree.SelectEntry(eyes.id);
            LayoutProbe.Measure(view, () =>
            {
                UiTestEvents.SimulateClick(view.Detail.Q<Button>("ad-add-effect"));
                var root = Resources.FindObjectsOfTypeAll<EditorWindow>().First(w => w.rootVisualElement.panel == view.panel && w.rootVisualElement.Contains(view)).rootVisualElement;
                var open = root.Q<AdMenu>();
                try
                {
                    Assert.That(open, Is.Not.Null);
                    Assert.That(open.Items.Select(i => i.Text), Is.EqualTo(new[] { L.Text("ad.ui.blendshapes"), L.Text("ad.ui.targets.properties") }));
                    var properties = open.Items.Single(i => i.Text == L.Text("ad.ui.targets.properties"));
                    Assert.That(properties.Description, Is.EqualTo(L.Text("ad.ui.add_effect.properties")));
                    open.Close(false);
                    properties.Action();
                }
                finally { open?.Close(false); }
            }, 400f);
            Assert.That(menu.entries.Single(e => e.id == eyes.id).properties.Count, Is.EqualTo(1), "picking Properties adds a row to the radial");
            Assert.That(Shown(view.Detail.Q<TargetListView>("ad-properties"), view.Detail), Is.True, "and its section shows in Simple");
            AssertRealModeUntouched();
        }

        [Test]
        public void SimpleNeverHidesExistingData()
        {
            var menu = avatar.AddMenu();
            menu.installPath = "Outfits";
            var shirt = Toggle(menu, "<b>Shirt</b>");
            shirt.exclusiveGroup = "Tops";
            shirt.platform = EntryPlatform.PCOnly;
            shirt.materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 0, material = avatar.MaterialB });
            shirt.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 0.5f });
            var plain = Toggle(menu, "Hat");
            var poke = menu.AddEntry(EntryKind.Trigger, "Poke");
            poke.source = TriggerSource.Parameter;
            poke.sourceParameter = "GestureRight";
            poke.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var over = menu.AddEntry(EntryKind.Override, "Pin");
            over.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 50f });
            string before = EditorJsonUtility.ToJson(menu);

            var view = View(menu, simple: true);

            foreach (var entry in new[] { shirt, plain, poke, over })
                Assert.That(view.Tree.RowFor(entry.id), Is.Not.Null, entry.name + " is in the tree");
            Assert.That(view.Tree.RowsOfKind(RowKind.TriggersGroup).Any(), Is.True, "the Triggers group stays");
            Assert.That(view.Tree.RowsOfKind(RowKind.OverridesGroup).Any(), Is.True, "and the Overrides group");
            Assert.That(view.Tree.RowsOfKind(RowKind.Existing).Any(), Is.True, "and the avatar's own menu");

            view.Tree.SelectEntry(shirt.id);
            var panel = view.Detail;
            Assert.That(Shown(panel.Q<TargetListView>("ad-materials"), panel), Is.True, "a toggle that uses Materials shows them");
            Assert.That(Shown(panel.Q<TargetListView>("ad-properties"), panel), Is.True, "and Properties");
            Assert.That(Shown(panel.Q<TargetListView>("ad-animations"), panel), Is.False, "while an empty section stays out of the way");
            Assert.That(panel.EffectSections.Select(s => s.NameKey), Is.EqualTo(new[] { "ad.ui.objects", "ad.ui.blendshapes", "ad.ui.targets.properties" }), "new Materials are not offered; Properties are");
            Assert.That(Shown(FieldTipped(panel, "ad.ui.group.tip"), panel), Is.True, "the Exclusive group it belongs to shows");
            Assert.That(FieldTipped(panel, "ad.ui.group.tip").value, Is.EqualTo("Tops"));
            Assert.That(Shown(panel.Q<VisualElement>("ad-style-color"), panel), Is.True, "a styled name keeps its Style row");
            Assert.That(Shown(panel.Q<AdvancedFold>(), panel), Is.True, "the Advanced fold shows while something in it is not at its default");
            Assert.That(panel.Q<AdvancedFold>().DotVisible, Is.True);
            Assert.That(Shown(view.Header.Q<VisualElement>("ad-settings-fold-header"), view.Header), Is.True, "so does Component settings, for the install path");
            Assert.That(view.Header.SettingsFoldDotVisible, Is.True);

            view.Tree.SelectEntry(plain.id);
            Assert.That(Shown(panel.Q<VisualElement>("ad-style-color"), panel), Is.False, "a plain name has no Style row");
            Assert.That(Shown(panel.Q<AdvancedFold>(), panel), Is.False, "and a panel at its defaults has no fold");

            view.Tree.SelectEntry(poke.id);
            Assert.That(panel.Q<DropdownField>("ad-trigger-source"), Is.Not.Null, "a trigger keeps its whole panel");
            Assert.That(panel.Q<TargetListView>("ad-objects"), Is.Not.Null);
            view.Tree.SelectEntry(over.id);
            Assert.That(panel.Q<TargetListView>("ad-blendshapes"), Is.Not.Null, "and so does an override");

            view.ModeChoice.Pick(1);
            view.ModeChoice.Pick(0);
            Assert.That(EditorJsonUtility.ToJson(menu), Is.EqualTo(before), "switching back and forth wrote nothing");
            AssertRealModeUntouched();
        }

        [Test]
        public void AStyledNameKeepsItsStyleRowInSimpleAndAPlainOneDoesNot()
        {
            var menu = avatar.AddMenu();
            var plain = Toggle(menu, "Plain");
            var colored = Toggle(menu, "<color=#FF0000>Red</color>");
            var custom = Toggle(menu, "<size=20>Big</size>");
            var view = View(menu, simple: true);
            var panel = view.Detail;

            view.Tree.SelectEntry(plain.id);
            Assert.That(Shown(panel.Q<TextField>("ad-name"), panel), Is.True);
            Assert.That(Shown(panel.Q<VisualElement>("ad-style-color"), panel), Is.False);
            Assert.That(Shown(panel.Q<Toggle>("ad-style-raw"), panel), Is.False);

            view.Tree.SelectEntry(colored.id);
            Assert.That(Shown(panel.Q<VisualElement>("ad-style-color"), panel), Is.True, "a coloured name shows how it is styled");
            Assert.That(Shown(panel.Q<Label>("ad-name-preview"), panel), Is.True, "and how it reads in game");

            view.Tree.SelectEntry(custom.id);
            Assert.That(Shown(panel.Q<VisualElement>("ad-style-color"), panel), Is.True);
            Assert.That(Shown(panel.Q<Toggle>("ad-style-raw"), panel), Is.True, "hand written tags show their raw text and the switch that explains it");
            Assert.That(Shown(panel.Q<TextField>("ad-name-raw"), panel), Is.True);
            AssertRealModeUntouched();
        }

        [Test]
        public void AdvancedItemsShowTheirFullPanelWithANote()
        {
            var menu = avatar.AddMenu();
            var hat = Toggle(menu, "Hat");
            var trigger = menu.AddEntry(EntryKind.Trigger, "Poke");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "GestureRight";
            trigger.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var over = menu.AddEntry(EntryKind.Override, "Pin");
            over.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 50f });
            var preset = menu.AddEntry(EntryKind.Preset, "Casual");
            preset.presetStates.Add(new PresetState { entryId = hat.id, on = true });
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Hat;
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            for (int i = 1; i <= 9; i++) menu.AddEntry(EntryKind.Toggle, "T" + i, outfits.id).objects.Add(new ObjectTarget { target = avatar.Hat });
            var controller = new AnimatorController { name = "FX2" };
            var asset = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            asset.name = "Bought";
            var extra = new GameObject("Extra");
            try
            {
                menu.animators.Add(AnimatorMerge.Create(controller));
                var install = MenuAssetInstall.Create(asset);
                install.asSubmenu = true;
                menu.menuAssets.Add(install);
                extra.transform.SetParent(avatar.Root.transform);
                VrcFuryTestHelper.AddToggle(extra, "Foreign");

                var view = View(menu, simple: true);
                var panel = view.Detail;
                var withNote = new List<(string Name, string Key)>
                {
                    ("trigger", view.Tree.RowFor(trigger.id).Key), ("override", view.Tree.RowFor(over.id).Key),
                    ("grabbable", view.Tree.RowFor(grab.id).Key),
                    ("animator", view.Tree.RowsOfKind(RowKind.Animator).First().Key), ("menu asset", view.Tree.RowsOfKind(RowKind.MenuAsset).First().Key),
                    ("triggers group", view.Tree.RowsOfKind(RowKind.TriggersGroup).First().Key),
                };
                var more = view.Tree.RowsOfKind(RowKind.More).First();
                // The avatar's own menu items and another tool's items are not AvatarDoc's to add more of, so the
                // note's "switch to Advanced to add more like it" has nothing to offer on them.
                var withoutNote = new List<(string Name, string Key)>
                {
                    ("preset", view.Tree.RowFor(preset.id).Key),
                    ("More page", more.Key),
                    ("avatar menu row", view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Node == null).Key),
                    ("foreign row", view.Tree.RowsOfKind(RowKind.Foreign).First().Key),
                };
                string Signature(string skipName) => string.Join(";", Descendants(panel).Where(e => e.name != skipName && !(e.parent != null && e.parent.name == skipName))
                    .Select(e => e.GetType().Name + "#" + e.name));

                foreach (var (name, key) in withNote)
                {
                    view.SetMode(ViewMode.Simple);
                    view.Tree.SelectKey(key);
                    var note = panel.Q<Label>("ad-advanced-item-note");
                    Assert.That(note, Is.Not.Null, name + " shows the note in Simple");
                    Assert.That(note.text, Is.EqualTo("This is an advanced item; switch to Advanced to add more like it."));
                    Assert.That(Shown(note, panel), Is.True);
                    var body = panel.Q<VisualElement>(className: "ad-panel-body");
                    Assert.That(body.ElementAt(0), Is.SameAs(note), name + ": the note is the first line of the panel");
                    string simpleShape = Signature("ad-advanced-item-note");
                    Assert.That(simpleShape.Length, Is.GreaterThan(0));

                    view.SetMode(ViewMode.Advanced);
                    Assert.That(panel.Q<Label>("ad-advanced-item-note"), Is.Null, name + ": Advanced has nothing to switch to, so no note");
                    Assert.That(Signature("ad-advanced-item-note"), Is.EqualTo(simpleShape), name + ": the rest of the panel is the same in both modes");
                }

                foreach (var (name, key) in withoutNote)
                {
                    view.SetMode(ViewMode.Simple);
                    view.Tree.SelectKey(key);
                    Assert.That(panel.Q<Label>("ad-advanced-item-note"), Is.Null,
                        name + " is no advanced item of AvatarDoc's: + Outfit set builds presets, a menu past eight items makes the More pages itself, and the avatar's or another tool's items are not AvatarDoc's to add");
                    string simpleShape = Signature("ad-advanced-item-note");
                    Assert.That(simpleShape.Length, Is.GreaterThan(0));

                    view.SetMode(ViewMode.Advanced);
                    Assert.That(Signature("ad-advanced-item-note"), Is.EqualTo(simpleShape), name + ": the same whole panel in both modes");
                }
                view.SetMode(ViewMode.Simple);
                view.Tree.SelectKey(more.Key);
                Assert.That(panel.Query<Label>(className: "ad-note").ToList().Select(l => l.text), Has.Some.EqualTo(string.Format(L.Text("ad.ui.existing_note"), more.Name)),
                    "a More page still explains itself in Simple");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(controller);
                UnityEngine.Object.DestroyImmediate(asset);
            }
            AssertRealModeUntouched();
        }

        static PanelAudit.AllowList StandardAllowList() => new PanelAudit.AllowList()
            .Allow(e => e.name == "remove" && e.ClassListContains("ad-w-24"), "a target list row's own trailing x remove button: deliberately at the row's far end")
            .Allow(e => e.ClassListContains("ad-chip-remove"), "a chip's own remove x: trailing inside its own chip")
            .Allow(e => e.name == "ad-unlock", "the lock banner's own trailing action button")
            .Allow(e => e.name == "ad-preview-simulate" || e.name == "ad-select-component", "a panel's own single primary action button")
            .Allow(e => e.name == "ad-problems-arrow", "the status row's own leading disclosure arrow")
            .Allow(e => e.name == "ad-status-glyph", "the status row's own leading icon")
            .Allow(e => e.name == "ad-ready-show" || e.name == "ad-summary-toggle", "the status row's own trailing actions")
            .Allow(e => e.name == "ad-settings-fold-dot" || e.name == "ad-advanced-fold-dot", "a fold's own non default dot")
            .AllowForRule(3, e => e is Toggle && e.parent != null && e.parent.ClassListContains("ad-flags"), "a compact flags row's own toggle");

        [Test]
        public void SimpleFitsAt360()
        {
            var menu = avatar.AddMenu();
            avatar.AddBodyShapes("Blush");
            var hat = Toggle(menu, "Hat");
            hat.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", minWeight = 0f, maxWeight = 100f });
            hat.materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 0, material = avatar.MaterialB });
            var styled = Toggle(menu, "<b>Bold</b>");
            styled.exclusiveGroup = "Group";
            styled.platform = EntryPlatform.PCOnly;
            var eyes = menu.AddEntry(EntryKind.Radial, "Eyes");
            eyes.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", minWeight = 0f, maxWeight = 100f });
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var poke = menu.AddEntry(EntryKind.Trigger, "Poke");
            poke.objects.Add(new ObjectTarget { target = avatar.Glasses });
            menu.AddEntry(EntryKind.Toggle, "Problem").objects.Add(new ObjectTarget { target = null });
            var view = View(menu, simple: true);
            var allow = StandardAllowList();

            LayoutProbe.Measure(view, () =>
            {
                Assert.That(view.Mode, Is.EqualTo(ViewMode.Simple));
                var mode = view.ModeChoice;
                Assert.That(mode.worldBound.x, Is.GreaterThanOrEqualTo(view.worldBound.x - 0.5f));
                Assert.That(mode.worldBound.xMax, Is.LessThanOrEqualTo(view.worldBound.xMax + 0.5f), "the switch fits");
                Assert.That(mode.Items.All(i => i.worldBound.width > 0f), Is.True);

                LayoutProbe.Validate(view);
                PanelAudit.Audit(view.Q<VisualElement>("ad-mode-row"), view.worldBound.xMax, allow);
                PanelAudit.Audit(view.Header, view.Header.worldBound.xMax, allow);
                PanelAudit.Audit(view.Tree, view.Tree.worldBound.xMax, allow);
                AssertSimpleSplit(view, 360f);
                var search = view.Tree.Q<TextField>("ad-search");
                Assert.That(Painted(search), Is.True);
                Assert.That(search.worldBound.xMax, Is.LessThanOrEqualTo(view.Tree.Q<VisualElement>("ad-toolbar-find").worldBound.xMax + 0.5f));

                foreach (var id in new[] { hat.id, styled.id, eyes.id, outfits.id, poke.id })
                {
                    view.Tree.SelectEntry(id);
                    LayoutProbe.Validate(view);
                    PanelAudit.Audit(view.Detail, view.Detail.worldBound.xMax, allow);
                    if (id == styled.id)
                    {
                        Assert.That(Painted(view.Detail.Q<TargetListView>("ad-blendshapes")), Is.True, "the audit covers the empty Blend shapes section Simple always shows");
                        Assert.That(Painted(view.Detail.Q<Button>("ad-shape-set-capture")), Is.True, "and its Capture");
                    }
                }
                foreach (var row in new[] { view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Node == null), view.Tree.RowsOfKind(RowKind.TriggersGroup).First() })
                {
                    view.Tree.SelectKey(row.Key);
                    LayoutProbe.Validate(view);
                    PanelAudit.Audit(view.Detail, view.Detail.worldBound.xMax, allow);
                }
            }, 360f);

            var empty = new GameObject("Empty");
            empty.transform.SetParent(avatar.Root.transform);
            var emptyView = View(avatar.AddMenu(empty), simple: true);
            LayoutProbe.Measure(emptyView, () =>
            {
                Assert.That(Painted(emptyView.Tree.Q<VisualElement>("ad-welcome-card")), Is.True, "an empty component shows the welcome card");
                PanelAudit.Audit(emptyView.Tree, emptyView.Tree.worldBound.xMax, allow);
                PanelAudit.Audit(emptyView.Header, emptyView.Header.worldBound.xMax, allow);
            }, 360f);
            AssertRealModeUntouched();
        }

        [Test]
        public void TheDefaultIsAdvancedAndShowsEverything()
        {
            var menu = avatar.AddMenu();
            var hat = Toggle(menu, "Hat");
            var view = View(menu);
            Assert.That(view.Mode, Is.EqualTo(ViewMode.Advanced));
            Assert.That(view.Simple, Is.False);
            Assert.That(view.Tree.AddButtons.Count, Is.EqualTo(13));
            foreach (var name in AdvancedOnlyButtons) Assert.That(view.Tree.Q<VisualElement>(name).ClassListContains("ad-hidden"), Is.False, name);
            Assert.That(Shown(view.Header.Q<VisualElement>("ad-settings-fold-header")), Is.True);
            view.Tree.SelectEntry(hat.id);
            Assert.That(Shown(view.Detail.Q<VisualElement>("ad-builds-line"), view.Detail), Is.True);
            Assert.That(Shown(view.Detail.Q<AdvancedFold>(), view.Detail), Is.True);
            Assert.That(view.Detail.Q<Label>("ad-advanced-item-note"), Is.Null);
            AssertRealModeUntouched();
        }
    }
}
#endif
