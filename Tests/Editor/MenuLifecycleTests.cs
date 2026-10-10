#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The menu that went away by itself, the way the creator meets it: the view in a shown window, a field focused
    /// for real, a real right click on a row the tree itself made, and the focus moving the way Unity moves it. Also
    /// the Summary panel a refresh used to close.
    /// </summary>
    public class MenuLifecycleTests
    {
        TestAvatar avatar;
        Func<EditorWindow, bool> oldHostHasFocus;
        Func<bool> oldEditorActive;
        Func<EditorWindow> oldPointerWindow;
        Action<string> oldLog;
        Func<double> oldClock;
        readonly List<string> logged = new List<string>();

        [SetUp]
        public void SetUp()
        {
            // First, before anything that can throw: the runner calls TearDown even when SetUp fails, and TearDown
            // puts these back.
            oldHostHasFocus = AdMenu.HostHasFocus;
            oldEditorActive = AdMenu.EditorActive;
            oldPointerWindow = AdMenu.PointerWindow;
            oldLog = AdMenu.Log;
            oldClock = AdMenu.Clock;
            logged.Clear();
            AdMenu.Log = logged.Add;
            avatar = TestAvatar.Create();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
            TagFilterMemory.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            AdMenu.HostHasFocus = oldHostHasFocus;
            AdMenu.EditorActive = oldEditorActive;
            AdMenu.PointerWindow = oldPointerWindow;
            AdMenu.Log = oldLog;
            AdMenu.Clock = oldClock;
            avatar.Dispose();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
            TagFilterMemory.ClearAll();
        }

        static AvatarDoctorView NewView(AvatarDoc menu) => new AvatarDoctorView(new SerializedObject(menu), menu);

        static EditorWindow WindowOf(VisualElement element) =>
            Resources.FindObjectsOfTypeAll<EditorWindow>().First(w => w.rootVisualElement.panel == element.panel && w.rootVisualElement.Contains(element));

        /// <summary>Holds the host window at one size, so no resize closes a menu halfway through a test.</summary>
        static void Pin(VisualElement element, float width, float height)
        {
            var window = WindowOf(element);
            window.minSize = new Vector2(width, height);
            window.maxSize = new Vector2(width, height);
            window.position = new Rect(window.position.x, window.position.y, width, height);
            LayoutProbe.Validate(element);
        }

        static void InWindow(VisualElement view, Action<VisualElement> body)
        {
            LayoutProbe.Measure(view, () =>
            {
                Pin(view, 400f, 900f);
                var root = WindowOf(view).rootVisualElement;
                try { body(root); }
                finally { foreach (var left in root.Query<AdMenu>().ToList()) left.Close(false); }
            }, 400f, 900f);
        }

        static void Pointer<T>(VisualElement target, EventType type, int button) where T : PointerEventBase<T>, new()
        {
            using (var e = PointerEventBase<T>.GetPooled(new Event { type = type, button = button, mousePosition = target.worldBound.center }))
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        /// <summary>
        /// A right press on target, then the release where the panel routes it: to the topmost element under the
        /// pointer, which is the menu once the press has opened one. A release sent to a tree row would reach the
        /// TreeView, which takes the focus on every release inside it.
        /// </summary>
        static void RightClick(VisualElement target)
        {
            var at = target.worldBound.center;
            Pointer<PointerDownEvent>(target, EventType.MouseDown, 1);
            var top = target.panel?.Pick(at) ?? target;
            using (var e = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 1, mousePosition = at }))
            {
                e.target = top;
                top.SendEvent(e);
            }
        }

        static void Key(VisualElement target, KeyCode code, char character = '\0')
        {
            using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = code, character = character }))
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        static Focusable Focused(VisualElement anyInPanel) => anyInPanel.panel?.focusController?.focusedElement;

        /// <summary>
        /// Puts text into a delayed field the way Unity's keyboard editor does while typing: the text element shows it
        /// without raising a change, and the field keeps it unsaved until it loses the focus or Enter is pressed.
        /// </summary>
        internal static void TypeWithoutSaving(TextField field, string text)
        {
            var shown = field.Q(className: "unity-text-field__input")?.Q<TextElement>();
            Assert.That(shown, Is.Not.Null, "the field has Unity's own text element inside its input");
            ((INotifyValueChanged<string>)shown).SetValueWithoutNotify(text);
            Assert.That(field.text, Is.EqualTo(text), "the field shows the typed text");
            Assert.That(field.value, Is.Not.EqualTo(text), "typed text is not saved until the field loses the focus");
        }

        string Closes() => logged.Count == 0 ? "no close was logged" : "closes logged: " + string.Join(" | ", logged);

        /// <summary>The row element the tree's own virtualization made and bound for an entry. A MakeRow element never leaves the panel, so only one of these can show a menu losing its row.</summary>
        static VisualElement RealisedRow(AvatarDoctorView view, string entryId)
        {
            LayoutProbe.Validate(view);
            var rows = view.Tree.Tree.Query(className: "ad-tree-row").ToList();
            Assert.That(rows, Is.Not.Empty, "the tree made its own rows in the shown window");
            var row = rows.FirstOrDefault(r => (r.userData as TreeRow)?.EntryId == entryId);
            Assert.That(row, Is.Not.Null, "the tree shows a row for the entry");
            return row;
        }

        static MenuEntry Entry(AvatarDoc menu, string id) => menu.entries.First(e => e.id == id);

        [Test]
        public void RightClickRightAfterTypingKeepsTheMenuOpen()
        {
            var menu = avatar.AddMenu();
            var clothes = menu.AddEntry(EntryKind.Submenu, "Clothes");
            var accessories = menu.AddEntry(EntryKind.Submenu, "Accessories");
            var view = NewView(menu);
            InWindow(view, root =>
            {
                view.Tree.SelectEntry(clothes.id);
                var name = view.Detail.Q<TextField>("ad-name");
                name.Focus();
                var focused = Focused(name) as VisualElement;
                Assert.That(focused == name || name.Contains(focused), Is.True, "the name field has the focus");
                TypeWithoutSaving(name, "Outfits");
                var row = RealisedRow(view, accessories.id);

                RightClick(row);

                var open = root.Q<AdMenu>();
                Assert.That(open, Is.Not.Null, "the row menu is still open after the typed name saved on the way into it; " + Closes());
                Assert.That(Focused(open), Is.SameAs(open), "the menu has the focus");
                Assert.That(Entry(menu, clothes.id).name, Is.EqualTo("Outfits"), "the typed name saved onto the submenu it was typed for");
                Assert.That(Entry(menu, accessories.id).name, Is.EqualTo("Accessories"), "the right clicked row keeps its own name");
                Assert.That(view.RefreshHeldByMenu, Is.True, "the save's refresh waits for the menu");

                Key(open, KeyCode.Escape, (char)27);
                Assert.That(open.IsClosed, Is.True);
                Assert.That(open.CloseReason, Is.Null, "Escape is a close the user asked for");
                view.CheckVersion();
                Assert.That(view.RefreshHeldByMenu, Is.False, "the held refresh ran on the next tick");
                Assert.That(view.Tree.RowFor(clothes.id).Name, Is.EqualTo("Outfits"), "and the tree shows the new name");
                Assert.That(view.Detail.Row?.EntryId, Is.EqualTo(accessories.id), "the panel shows the row the right click selected");
                Assert.That(logged, Is.Empty);
            });
        }

        [Test]
        public void GroupPickerStaysOpenAfterTypingAGroup()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = NewView(menu);
            InWindow(view, root =>
            {
                view.Tree.SelectEntry(hat.id);
                LayoutProbe.Validate(view);
                var group = view.Detail.Query<TextField>().ToList().First(f => f.label == L.Text("ad.ui.group"));
                var pick = group.parent.Q<Button>();
                group.Focus();
                TypeWithoutSaving(group, "Outfit");

                Pointer<PointerDownEvent>(pick, EventType.MouseDown, 0);
                Assert.That(Entry(menu, hat.id).exclusiveGroup, Is.EqualTo("Outfit"), "pressing the picker took the focus and saved the typed group");
                Assert.That(Focused(pick), Is.SameAs(pick), "the picker has the focus");
                Pointer<PointerUpEvent>(pick, EventType.MouseUp, 0);
                // The panel never sees a synthetic pointer as over the button, so Unity's Clickable does not run the
                // click on the release; the click is run here. The focus already moved for real on the press.
                if (root.Q<AdMenu>() == null) UiTestEvents.SimulateClick(pick);
                var open = root.Q<AdMenu>();
                Assert.That(open, Is.Not.Null, "the picker opened its menu");
                Assert.That(Focused(open), Is.SameAs(open), "the menu took the focus from the picker");

                view.Detail.RunPendingRebuild();

                Assert.That(open.IsClosed, Is.False, "the rebuild the saved group asked for waits while the menu is open; " + Closes());
                Assert.That(pick.panel, Is.Not.Null, "the picker the menu hangs on is still on screen");

                Key(open, KeyCode.Escape, (char)27);
                Assert.That(open.IsClosed, Is.True);
                Assert.That(pick.panel, Is.Null, "the waiting rebuild ran as soon as the menu closed");
                Assert.That(logged, Is.Empty);
            });
        }

        [Test]
        public void RowMenuStaysOpenWhileTheViewIsAskedToRefresh()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var boot = menu.AddEntry(EntryKind.Toggle, "Boot");
            boot.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var view = NewView(menu);
            InWindow(view, root =>
            {
                RightClick(RealisedRow(view, hat.id));
                var open = root.Q<AdMenu>();
                Assert.That(open, Is.Not.Null, "a right click on a row opens its menu; " + Closes());

                Entry(menu, boot.id).name = "Shoe";
                AvatarPlanCache.Invalidate();
                view.CheckVersion();
                view.Refresh();

                Assert.That(open.IsClosed, Is.False, "a refresh asked for from outside waits while the row menu is open; " + Closes());
                Assert.That(view.MenuOpen, Is.True);
                Assert.That(view.RefreshHeldByMenu, Is.True, "and is remembered");
                Assert.That(view.Tree.RowFor(boot.id).Name, Is.EqualTo("Boot"), "the tree under the menu is not rebuilt");

                Key(open, KeyCode.Escape, (char)27);
                Assert.That(view.MenuOpen, Is.False);
                view.CheckVersion();
                Assert.That(view.RefreshHeldByMenu, Is.False, "the held refresh ran once the menu closed");
                Assert.That(view.Tree.RowFor(boot.id).Name, Is.EqualTo("Shoe"));
                Assert.That(logged, Is.Empty);
            });
        }

        [Test]
        public void ChoosingAnItemRefreshesFirstThenRunsIt()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var boot = menu.AddEntry(EntryKind.Toggle, "Boot");
            boot.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var view = NewView(menu);
            InWindow(view, root =>
            {
                var row = RealisedRow(view, hat.id);
                string seen = null;
                var open = AdMenu.Open(new Rect(row.worldBound.center, Vector2.zero), row, new[]
                {
                    new AdMenu.Item { Text = "Read the tree", Action = () => seen = view.Tree.RowFor(boot.id).Name },
                });
                Entry(menu, boot.id).name = "Shoe";
                AvatarPlanCache.Invalidate();
                view.CheckVersion();
                Assert.That(view.RefreshHeldByMenu, Is.True, "the refresh waits while the menu is open");
                Assert.That(view.Tree.RowFor(boot.id).Name, Is.EqualTo("Boot"));

                open.Commit(0);

                Assert.That(open.IsClosed, Is.True);
                Assert.That(seen, Is.EqualTo("Shoe"), "the held refresh ran before the chosen item, so the item read the current tree");
                Assert.That(view.RefreshHeldByMenu, Is.False);
                Assert.That(logged, Is.Empty, "a chosen item is no close to report");
            });
        }

        [Test]
        public void WindowClearingItsRootWithAMenuOpenDoesNotThrow()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = NewView(menu);
            InWindow(view, root =>
            {
                RightClick(RealisedRow(view, hat.id));
                var open = root.Q<AdMenu>();
                Assert.That(open, Is.Not.Null, "a right click on a row opens its menu; " + Closes());

                // The AvatarDoc window clears its root this way when it switches to another avatar: the view and
                // the menu leave in one call, the view first.
                Assert.DoesNotThrow(() => root.Clear(), "a window clearing its root with a menu open");

                Assert.That(open.IsClosed, Is.True);
                Assert.That(view.MenuOpen, Is.False, "and the view holds nothing for the next time it is shown");
            });
        }

        [Test]
        public void AddEffectPickerOpensAtItsButtonAfterATypedGroup()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = NewView(menu);
            var oldShapePicker = ShapePicker.ShowOverride;
            Rect? shownAt = null;
            ShapePicker.ShowOverride = (anchor, avatarRoot, onPick) => shownAt = anchor;
            try
            {
                InWindow(view, root =>
                {
                    view.Tree.SelectEntry(hat.id);
                    LayoutProbe.Validate(view);
                    var group = view.Detail.Query<TextField>().ToList().First(f => f.label == L.Text("ad.ui.group"));
                    var add = view.Detail.Q<Button>("ad-add-effect");
                    group.Focus();
                    TypeWithoutSaving(group, "Outfit");

                    Pointer<PointerDownEvent>(add, EventType.MouseDown, 0);
                    Assert.That(Entry(menu, hat.id).exclusiveGroup, Is.EqualTo("Outfit"), "pressing + Add effect took the focus and saved the typed group");
                    var bound = add.worldBound;
                    Pointer<PointerUpEvent>(add, EventType.MouseUp, 0);
                    // As in GroupPickerStaysOpenAfterTypingAGroup: the click itself is run here, the focus moved for real.
                    if (root.Q<AdMenu>() == null) UiTestEvents.SimulateClick(add);
                    var open = root.Q<AdMenu>();
                    Assert.That(open, Is.Not.Null, "+ Add effect opened its menu");
                    int blendShapes = open.Items.ToList().FindIndex(i => i.Text == L.Text("ad.ui.blendshapes"));
                    Assert.That(blendShapes, Is.GreaterThanOrEqualTo(0));
                    // The button moves while the menu is open, so a rectangle read when the item runs differs from
                    // the one read when the menu opened.
                    add.transform.position = new Vector3(0f, 40f, 0f);
                    LayoutProbe.Validate(view);
                    Assert.That(add.worldBound.y, Is.EqualTo(bound.y + 40f).Within(1f), "the button moved down after the menu opened");

                    // Chosen with a press and a release on its row, as the pointer does, so the close runs inside an event.
                    Pointer<PointerDownEvent>(open.Rows[blendShapes], EventType.MouseDown, 0);
                    Pointer<PointerUpEvent>(open.Rows[blendShapes], EventType.MouseUp, 0);

                    Assert.That(open.IsClosed, Is.True);
                    Assert.That(add.panel, Is.Null, "choosing ran the rebuild the saved group asked for, which took the button away");
                    Assert.That(shownAt.HasValue, Is.True, "Blend shapes opened the shape picker");
                    Assert.That(shownAt.Value.x, Is.EqualTo(bound.x).Within(1f), "the picker opens at the button, not at the window's left edge; it opened at " + shownAt.Value + ", the button was at " + bound);
                    Assert.That(shownAt.Value.y, Is.EqualTo(bound.y).Within(1f), "the picker opens at the button, not at the window's top; it opened at " + shownAt.Value + ", the button was at " + bound);
                });
            }
            finally
            {
                ShapePicker.ShowOverride = oldShapePicker;
            }
        }

        [Test]
        public void UndoClosesAnOpenMenu()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = NewView(menu);
            InWindow(view, root =>
            {
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(menu, "Rename Hat");
                Entry(menu, hat.id).name = "Cap";
                Undo.IncrementCurrentGroup();
                RightClick(RealisedRow(view, hat.id));
                var open = root.Q<AdMenu>();
                Assert.That(open, Is.Not.Null, "a right click on a row opens its menu; " + Closes());

                Undo.PerformUndo();

                Assert.That(open.IsClosed, Is.True, "an undo changes the data under the menu, so the menu closes");
                Assert.That(open.CloseReason, Is.Not.Null.And.Not.Empty, "a close nobody chose says why");
                Assert.That(logged, Has.Count.EqualTo(1), "in one console line");
                Assert.That(logged[0], Does.Contain(open.CloseReason));
                Assert.That(view.MenuOpen, Is.False, "the view's refreshes are let go");
                Assert.That(Entry(menu, hat.id).name, Is.EqualTo("Hat"));
            });
        }

        [Test]
        public void FocusTakenInsideTheWindowIsTakenBackThreeTimesASecond()
        {
            AdMenu.HostHasFocus = _ => true;
            double now = 500.0;
            AdMenu.Clock = () => now;
            var view = new VisualElement();
            var opener = new Button { text = "Open", name = "opener" };
            var thief = new Button { text = "Thief", name = "focus-thief" };
            view.Add(opener);
            view.Add(thief);
            InWindow(view, root =>
            {
                var menu = AdMenu.Open(opener.worldBound, opener, new[] { new AdMenu.Item { Text = "Alpha", Action = () => { } } });
                for (int steal = 1; steal <= AdMenu.MaxRefocusesPerSecond; steal++)
                {
                    thief.Focus();
                    Assert.That(menu.IsClosed, Is.False, "steal " + steal + ": the menu does not close the moment the focus leaves it");
                    menu.CheckFocus();
                    Assert.That(Focused(menu), Is.SameAs(menu), "steal " + steal + ": the next tick takes the focus back");
                    now += 0.1;
                }
                thief.Focus();
                menu.CheckFocus();
                Assert.That(menu.IsClosed, Is.True, "a fourth steal inside one second closes the menu");
                Assert.That(menu.CloseReason, Does.Contain("focus-thief"), "naming the element that kept taking the focus");
                Assert.That(logged, Has.Count.EqualTo(1));

                var again = AdMenu.Open(opener.worldBound, opener, new[] { new AdMenu.Item { Text = "Alpha", Action = () => { } } });
                for (int steal = 1; steal <= 2 * AdMenu.MaxRefocusesPerSecond; steal++)
                {
                    thief.Focus();
                    again.CheckFocus();
                    now += 0.5;
                }
                Assert.That(again.IsClosed, Is.False, "steals spread out over time are taken back every time");
                Assert.That(Focused(again), Is.SameAs(again));
            });
        }

        [Test]
        public void MenuClosesWhenItsWindowLosesTheFocus()
        {
            AdMenu.HostHasFocus = _ => false;
            var view = new VisualElement();
            var opener = new Button { text = "Open" };
            view.Add(opener);
            InWindow(view, root =>
            {
                var host = WindowOf(opener);
                AdMenu.PointerWindow = () => null;
                var menu = AdMenu.Open(opener.worldBound, opener, new[] { new AdMenu.Item { Text = "Alpha", Action = () => { } } });
                menu.Blur();
                Assert.That(menu.IsClosed, Is.False, "the focus leaving is checked on the next tick");
                menu.CheckFocus();
                Assert.That(menu.IsClosed, Is.True, "another window has the focus, so the menu closes");
                Assert.That(menu.CloseReason, Is.Not.Null.And.Not.Empty);
                Assert.That(logged, Is.Empty, "the pointer is over the other window: the user clicked there, nothing to report");

                AdMenu.PointerWindow = () => host;
                AdMenu.EditorActive = () => false;
                var altTab = AdMenu.Open(opener.worldBound, opener, new[] { new AdMenu.Item { Text = "Alpha", Action = () => { } } });
                altTab.Blur();
                altTab.CheckFocus();
                Assert.That(altTab.IsClosed, Is.True);
                Assert.That(logged, Is.Empty, "another application is in front (Alt+Tab) with the pointer still over the window: the user did that, nothing to report");

                AdMenu.EditorActive = () => true;
                var stolen = AdMenu.Open(opener.worldBound, opener, new[] { new AdMenu.Item { Text = "Alpha", Action = () => { } } });
                stolen.Blur();
                stolen.CheckFocus();
                Assert.That(stolen.IsClosed, Is.True);
                Assert.That(logged, Has.Count.EqualTo(1), "the editor stayed in front and the pointer never left the menu's window, so something took the focus: that is reported");
            });
        }

        [Test]
        public void MenuTakenOutOfItsWindowClosesAndLetsTheViewRefresh()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = NewView(menu);
            InWindow(view, root =>
            {
                RightClick(RealisedRow(view, hat.id));
                var open = root.Q<AdMenu>();
                Assert.That(open, Is.Not.Null, "a right click on a row opens its menu; " + Closes());
                Assert.That(view.MenuOpen, Is.True, "an open row menu holds the view's refreshes");

                open.RemoveFromHierarchy();

                Assert.That(open.IsClosed, Is.True, "a menu its window took away counts as closed");
                Assert.That(view.MenuOpen, Is.False, "and holds nothing any more");
                Assert.That(open.CloseReason, Is.EqualTo(L.Text("ad.log.menu_closed.removed")));
                Assert.That(logged, Is.Empty, "a window takes its menus away on a tab switch, a close or a maximize, the creator's own doing: no console line");
            });
        }

        [Test]
        public void MenuClosesWhenItsViewLeavesThePanel()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = NewView(menu);
            InWindow(view, root =>
            {
                AdMenu.HostHasFocus = _ => true;
                RightClick(RealisedRow(view, hat.id));
                var open = root.Q<AdMenu>();
                Assert.That(open, Is.Not.Null, "a right click on a row opens its menu; " + Closes());

                view.RemoveFromHierarchy();

                Assert.That(open.IsClosed, Is.True, "the Inspector rebuilding its editor takes the menu's view away, and the menu with it");
                Assert.That(open.CloseReason, Is.EqualTo(L.Text("ad.log.menu_closed.view_gone")));
                Assert.That(logged, Has.Count.EqualTo(1), "the creator was still in the window, so the close is reported");
                Assert.That(view.MenuOpen, Is.False, "and the view holds nothing for the next time it is shown");

                root.Add(view);
                RightClick(RealisedRow(view, hat.id));
                var again = root.Q<AdMenu>();
                Assert.That(again, Is.Not.Null, "the view shown again opens its menu; " + Closes());
                AdMenu.HostHasFocus = _ => false;
                AdMenu.PointerWindow = () => null;

                view.RemoveFromHierarchy();

                Assert.That(again.IsClosed, Is.True);
                Assert.That(logged, Has.Count.EqualTo(1), "a click on another window that rebuilt the Inspector is the creator's own close: nothing more is reported");
                Assert.That(view.MenuOpen, Is.False);
            });
        }

        [Test]
        public void ResizingTheWindowClosesAMenuWithoutALine()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = NewView(menu);
            InWindow(view, root =>
            {
                RightClick(RealisedRow(view, hat.id));
                var open = root.Q<AdMenu>();
                Assert.That(open, Is.Not.Null, "a right click on a row opens its menu; " + Closes());

                Pin(view, 420f, 900f);

                Assert.That(open.IsClosed, Is.True, "the window changed size under the menu, so the menu closes");
                Assert.That(open.CloseReason, Is.EqualTo(L.Text("ad.log.menu_closed.resized")));
                Assert.That(logged, Is.Empty, "only a dragged splitter or window frame resizes the window while the menu covers it: the creator's own doing, no line");
                Assert.That(view.MenuOpen, Is.False);
            });
        }

        [Test]
        public void SummaryStaysOpenThroughItsOwnLockAndARefresh()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var boot = menu.AddEntry(EntryKind.Toggle, "Boot");
            boot.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var view = NewView(menu);
            view.Tree.SelectEntry(hat.id);
            view.OpenSummary();

            view.SetLocked(true);
            Assert.That(view.SummaryOpen, Is.True, "locking from the Summary panel keeps the panel open");
            Assert.That(view.Tree.Selected?.EntryId, Is.EqualTo(hat.id), "with the same row still selected");

            view.Refresh();
            Assert.That(view.SummaryOpen, Is.True, "a refresh keeps the Summary panel open");

            view.Tree.SelectEntry(boot.id);
            Assert.That(view.SummaryOpen, Is.False, "picking another row still shows that row's panel");

            var fresh = NewView(menu);
            fresh.OpenSummary();
            fresh.Refresh();
            Assert.That(fresh.SummaryOpen, Is.True, "a refresh with no row selected keeps it open too");
        }

        /// <summary>
        /// A plain check of the right click Delete on a merged animator's row: when chosen, it finds its row again by
        /// key and deletes that animator, even with another row selected since the menu was built.
        /// </summary>
        [Test]
        public void RowMenuDeleteRemovesTheAnimatorItWasOpenedFor()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var controller = new AnimatorController { name = "Extra" };
            controller.AddLayer("Base");
            try
            {
                menu.animators.Add(AnimatorMerge.Create(controller));
                var view = NewView(menu);
                var row = view.Tree.RowsOfKind(RowKind.Animator).Single();
                var delete = view.Tree.RowMenu(row).Last();
                Assert.That(delete.Text, Is.EqualTo(L.Text("ad.ui.menu.delete")));
                view.Tree.SelectEntry(hat.id);

                delete.Action();

                Assert.That(menu.animators, Is.Empty, "Delete removed the animator its row stands for, found again by its key");
                Assert.That(menu.entries.Any(e => e.id == hat.id), Is.True, "and not the row selected since");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(controller);
            }
        }
    }
}
#endif
