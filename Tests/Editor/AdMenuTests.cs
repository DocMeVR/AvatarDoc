#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// AdMenu, the one UI Toolkit popup every AvatarDoc dropdown and right click opens, driven through real
    /// events on elements attached to a shown host window.
    /// </summary>
    public class AdMenuTests
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

        static VisualElement RootOf(VisualElement element) =>
            Resources.FindObjectsOfTypeAll<EditorWindow>().First(w => w.rootVisualElement.panel == element.panel && w.rootVisualElement.Contains(element)).rootVisualElement;

        static void Key(VisualElement target, KeyCode code, char character = '\0')
        {
            using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = code, character = character }))
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        static void Pointer<T>(VisualElement target, EventType type, int button) where T : PointerEventBase<T>, new()
        {
            using (var e = PointerEventBase<T>.GetPooled(new Event { type = type, button = button, mousePosition = target.worldBound.center }))
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        static void Press(VisualElement target, int button = 0) => Pointer<PointerDownEvent>(target, EventType.MouseDown, button);

        static void Release(VisualElement target, int button = 0) => Pointer<PointerUpEvent>(target, EventType.MouseUp, button);

        static void Hover(VisualElement target) => Pointer<PointerMoveEvent>(target, EventType.MouseMove, 0);

        static void ClickRow(AdMenu menu, int index)
        {
            Release(menu.Rows[index]);
        }

        static List<AdMenu.Item> Sample(List<string> log)
        {
            return new List<AdMenu.Item>
            {
                AdMenu.Item.Heading("Group"),
                new AdMenu.Item { Text = "Alpha", Action = () => log.Add("Alpha") },
                new AdMenu.Item { Text = "Beta", Checked = true, Action = () => log.Add("Beta") },
                new AdMenu.Item { Text = "Gamma", Disabled = true, Action = () => log.Add("Gamma") },
                AdMenu.Item.Line(),
                new AdMenu.Item { Text = "Delta", Shortcut = "Del", Action = () => log.Add("Delta") },
            };
        }

        static void Resize(VisualElement element, float width, float height)
        {
            var window = Resources.FindObjectsOfTypeAll<EditorWindow>().First(w => w.rootVisualElement.panel == element.panel && w.rootVisualElement.Contains(element));
            window.minSize = new Vector2(width, height);
            window.maxSize = new Vector2(width, height);
            window.position = new Rect(window.position.x, window.position.y, width, height);
            LayoutProbe.Validate(element);
        }

        static void InWindow(Action<Button> body, float width = 360f, float height = 600f)
        {
            var view = new VisualElement();
            var opener = new Button { text = "Open" };
            opener.style.position = Position.Absolute;
            opener.style.left = 40f;
            opener.style.top = 30f;
            opener.style.width = 100f;
            opener.style.height = 22f;
            view.Add(opener);
            LayoutProbe.Measure(view, () =>
            {
                Resize(opener, width, height);
                body(opener);
            }, width, height);
        }

        [Test]
        public void AdMenuOpensAtTheAnchorInsideTheWindowRoot()
        {
            InWindow(opener =>
            {
                var root = RootOf(opener);
                var anchor = opener.worldBound;
                var menu = AdMenu.Open(anchor, opener, Sample(new List<string>()));
                try
                {
                    Assert.That(menu, Is.Not.Null);
                    Assert.That(menu.parent, Is.SameAs(root), "added to the root of the window that hosts the control");
                    LayoutProbe.Validate(menu);
                    var card = menu.Card.worldBound;
                    Assert.That(card.x, Is.EqualTo(anchor.x).Within(1f), "left edge on the anchor");
                    Assert.That(card.y, Is.EqualTo(anchor.yMax).Within(1f), "directly below the anchor");
                    Assert.That(card.width, Is.GreaterThanOrEqualTo(anchor.width - 1f), "at least as wide as the anchor");
                    Assert.That(card.width, Is.LessThanOrEqualTo(AdMenu.MaxWidth + 1f));
                    Assert.That(root.worldBound.Contains(card.min) && root.worldBound.Contains(card.max - Vector2.one), Is.True, "inside the window");
                    Assert.That(menu.focusController.focusedElement, Is.SameAs(menu), "the menu takes the focus");
                }
                finally { menu?.Close(false); }
                Assert.That(root.Query<AdMenu>().ToList(), Is.Empty);
            });
        }

        [Test]
        public void AdMenuFlipsAboveWhenItWouldLeaveTheWindow()
        {
            InWindow(opener =>
            {
                var root = RootOf(opener);
                opener.style.top = root.layout.height - 40f;
                LayoutProbe.Validate(opener);
                var anchor = opener.worldBound;
                var menu = AdMenu.Open(anchor, opener, Sample(new List<string>()));
                try
                {
                    LayoutProbe.Validate(menu);
                    var card = menu.Card.worldBound;
                    Assert.That(card.yMax, Is.LessThanOrEqualTo(anchor.y + 1f), "above the anchor, not over it");
                    Assert.That(card.y, Is.GreaterThanOrEqualTo(0f), "still inside the window");
                }
                finally { menu?.Close(false); }
            });
        }

        [Test]
        public void AdMenuShiftsLeftWhenItWouldLeaveTheRightEdge()
        {
            InWindow(opener =>
            {
                var root = RootOf(opener);
                opener.style.left = root.layout.width - 30f;
                LayoutProbe.Validate(opener);
                var menu = AdMenu.Open(opener.worldBound, opener, Sample(new List<string>()));
                try
                {
                    LayoutProbe.Validate(menu);
                    Assert.That(menu.Card.worldBound.xMax, Is.LessThanOrEqualTo(root.worldBound.xMax + 0.5f));
                    Assert.That(menu.Card.worldBound.x, Is.GreaterThanOrEqualTo(0f));
                }
                finally { menu?.Close(false); }
            });
        }

        [Test]
        public void AdMenuTicksAndDisabledItems()
        {
            InWindow(opener =>
            {
                var log = new List<string>();
                var menu = AdMenu.Open(opener.worldBound, opener, Sample(log));
                try
                {
                    LayoutProbe.Validate(menu);
                    var beta = menu.Rows[2];
                    Assert.That(beta.ClassListContains("ad-menu-ticked"), Is.True);
                    Assert.That(beta.Q<Label>(className: "ad-menu-check").text, Is.Not.Empty, "a ticked row shows its tick");
                    Assert.That(menu.Rows[1].Q<Label>(className: "ad-menu-check").text, Is.Empty, "an unticked row shows none");
                    Assert.That(menu.Rows[3].ClassListContains("ad-menu-disabled"), Is.True);
                    Assert.That(menu.Rows[5].Q<Label>(className: "ad-menu-shortcut").text, Is.EqualTo("Del"));
                    Assert.That(menu.Rows[0].ClassListContains("ad-menu-header"), Is.True);

                    ClickRow(menu, 3);
                    ClickRow(menu, 0);
                    Assert.That(log, Is.Empty, "a disabled item and a header never run");
                    Assert.That(menu.IsClosed, Is.False, "and never close the menu");

                    Hover(menu.Rows[1]);
                    Assert.That(menu.SelectedIndex, Is.EqualTo(1), "hovering highlights an enabled row");
                    Hover(menu.Rows[3]);
                    Assert.That(menu.SelectedIndex, Is.EqualTo(1), "and never a disabled one");

                    ClickRow(menu, 2);
                    Assert.That(log, Is.EqualTo(new[] { "Beta" }));
                    Assert.That(menu.IsClosed, Is.True, "choosing an item closes the menu");
                }
                finally { menu?.Close(false); }
                Assert.That(RootOf(opener).Query<AdMenu>().ToList(), Is.Empty);
            });
        }

        [Test]
        public void AdMenuSubmenuOpensAndGoesBack()
        {
            InWindow(opener =>
            {
                var log = new List<string>();
                var items = new List<AdMenu.Item>
                {
                    new AdMenu.Item { Text = "Move to", Children = new List<AdMenu.Item>
                    {
                        new AdMenu.Item { Text = "Root", Action = () => log.Add("Root") },
                        new AdMenu.Item { Text = "Outfits", Action = () => log.Add("Outfits") },
                    } },
                    new AdMenu.Item { Text = "Delete", Action = () => log.Add("Delete") },
                };
                var menu = AdMenu.Open(opener.worldBound, opener, items);
                try
                {
                    ClickRow(menu, 0);
                    Assert.That(menu.Depth, Is.EqualTo(1));
                    Assert.That(menu.Items.Select(i => i.Text), Is.EqualTo(new[] { "Root", "Outfits" }));
                    Assert.That(menu.Card.Q(name: "ad-menu-back"), Is.Not.Null, "a Back row sits above a submenu");

                    Key(menu, KeyCode.LeftArrow);
                    Assert.That(menu.Depth, Is.EqualTo(0));
                    Assert.That(menu.Items.Select(i => i.Text), Is.EqualTo(new[] { "Move to", "Delete" }));
                    Assert.That(menu.Card.Q(name: "ad-menu-back"), Is.Null);

                    Key(menu, KeyCode.DownArrow);
                    Key(menu, KeyCode.RightArrow);
                    Assert.That(menu.Depth, Is.EqualTo(1), "Right opens the highlighted submenu");
                    Key(menu, KeyCode.Backspace);
                    Assert.That(menu.Depth, Is.EqualTo(0), "Backspace goes up one level");

                    ClickRow(menu, 0);
                    Release(menu.Card.Q(name: "ad-menu-back"));
                    Assert.That(menu.Depth, Is.EqualTo(0), "the Back row goes up too");

                    ClickRow(menu, 0);
                    ClickRow(menu, 1);
                    Assert.That(log, Is.EqualTo(new[] { "Outfits" }));
                    Assert.That(menu.IsClosed, Is.True);
                }
                finally { menu?.Close(false); }
            });
        }

        [Test]
        public void AdMenuKeyboardMovesRunsAndCloses()
        {
            InWindow(opener =>
            {
                var log = new List<string>();
                var menu = AdMenu.Open(opener.worldBound, opener, Sample(log));
                try
                {
                    Key(menu, KeyCode.DownArrow);
                    Assert.That(menu.SelectedIndex, Is.EqualTo(1), "Down from nothing picks the first enabled row, past the header");
                    Key(menu, KeyCode.DownArrow);
                    Assert.That(menu.SelectedIndex, Is.EqualTo(2));
                    Key(menu, KeyCode.DownArrow);
                    Assert.That(menu.SelectedIndex, Is.EqualTo(5), "Down skips the disabled row and the separator");
                    Key(menu, KeyCode.DownArrow);
                    Assert.That(menu.SelectedIndex, Is.EqualTo(5), "and stops at the last row");
                    Key(menu, KeyCode.UpArrow);
                    Assert.That(menu.SelectedIndex, Is.EqualTo(2));
                    Key(menu, KeyCode.Home);
                    Assert.That(menu.SelectedIndex, Is.EqualTo(1));
                    Key(menu, KeyCode.End);
                    Assert.That(menu.SelectedIndex, Is.EqualTo(5));
                    Assert.That(menu.Rows[5].ClassListContains("ad-menu-selected"), Is.True);
                    Assert.That(menu.Rows.Count(r => r.ClassListContains("ad-menu-selected")), Is.EqualTo(1));

                    Key(menu, KeyCode.Return, '\n');
                    Assert.That(log, Is.EqualTo(new[] { "Delta" }), "Enter runs the highlighted item");
                    Assert.That(menu.IsClosed, Is.True);
                    Assert.That(opener.focusController.focusedElement, Is.SameAs(opener), "closing gives the focus back to the control");
                }
                finally { menu?.Close(false); }

                var again = AdMenu.Open(opener.worldBound, opener, Sample(log));
                try
                {
                    Key(again, KeyCode.Escape, (char)27);
                    Assert.That(again.IsClosed, Is.True, "Escape closes without running anything");
                    Assert.That(log, Is.EqualTo(new[] { "Delta" }));
                }
                finally { again?.Close(false); }
            });
        }

        [Test]
        public void AdMenuTypeToSearchSelectsTheFirstMatch()
        {
            InWindow(opener =>
            {
                double now = 100.0;
                var oldClock = AdMenu.Clock;
                AdMenu.Clock = () => now;
                var items = new List<AdMenu.Item>
                {
                    new AdMenu.Item { Text = "Rename", Action = () => { } },
                    new AdMenu.Item { Text = "Duplicate", Action = () => { } },
                    new AdMenu.Item { Text = "Delete", Action = () => { } },
                };
                var menu = AdMenu.Open(opener.worldBound, opener, items);
                try
                {
                    Key(menu, KeyCode.D, 'd');
                    Assert.That(menu.SelectedIndex, Is.EqualTo(1), "the first item that starts with D");
                    now += 0.3;
                    Key(menu, KeyCode.E, 'e');
                    Assert.That(menu.SelectedIndex, Is.EqualTo(2), "typed letters narrow the match: De is Delete, not Duplicate");
                    now += 2.0;
                    Key(menu, KeyCode.R, 'r');
                    Assert.That(menu.SelectedIndex, Is.EqualTo(0), "the typed text starts over after a second");
                }
                finally
                {
                    AdMenu.Clock = oldClock;
                    menu?.Close(false);
                }
            });
        }

        [Test]
        public void AdMenuClosesOnOutsideClickAndLeavesNothing()
        {
            InWindow(opener =>
            {
                var root = RootOf(opener);
                var log = new List<string>();

                var menu = AdMenu.Open(opener.worldBound, opener, Sample(log));
                Assert.That(root.Query<AdMenu>().ToList(), Has.Count.EqualTo(1));
                Press(menu);
                Assert.That(menu.IsClosed, Is.True, "a click on the shade outside the card closes it");
                Assert.That(root.Query<AdMenu>().ToList(), Is.Empty);

                menu = AdMenu.Open(opener.worldBound, opener, Sample(log));
                var second = AdMenu.Open(opener.worldBound, opener, Sample(log));
                Assert.That(menu.IsClosed, Is.True, "opening another menu closes the first");
                Assert.That(root.Query<AdMenu>().ToList(), Has.Count.EqualTo(1));
                var oldHostHasFocus = AdMenu.HostHasFocus;
                var oldPointerWindow = AdMenu.PointerWindow;
                AdMenu.HostHasFocus = _ => false;
                AdMenu.PointerWindow = () => null;
                try
                {
                    opener.Focus();
                    Assert.That(second.IsClosed, Is.False, "the focus leaving is checked on the next tick, not acted on at once");
                    second.CheckFocus();
                }
                finally
                {
                    AdMenu.HostHasFocus = oldHostHasFocus;
                    AdMenu.PointerWindow = oldPointerWindow;
                }
                Assert.That(second.IsClosed, Is.True, "the focus moving out of the menu's window closes it");
                Assert.That(root.Query<AdMenu>().ToList(), Is.Empty);

                Press(AdMenu.Open(opener.worldBound, opener, Sample(log)).Card);
                Assert.That(root.Query<AdMenu>().ToList(), Has.Count.EqualTo(1), "a click on the card itself keeps it open");
                root.Q<AdMenu>().Close(false);
                Assert.That(root.Query<AdMenu>().ToList(), Is.Empty);
                Assert.That(log, Is.Empty);
            });
        }

        [Test]
        public void FilterMenuStaysOpenWhileTicking()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            LayoutProbe.Measure(view, () =>
            {
                Resize(view, 360f, 700f);
                var root = RootOf(view);
                var button = view.Tree.Q<Button>("ad-tag-filter");
                UiTestEvents.SimulateClick(button);
                var open = root.Q<AdMenu>();
                try
                {
                    Assert.That(open, Is.Not.Null, "the Filter button opens an AdMenu");
                    Assert.That(open.Items.Any(i => i.Header), Is.True, "with a header for each group");
                    int first = open.Items.ToList().FindIndex(i => !i.Header && !i.Separator && !i.Disabled && i.Text != L.Text("ad.ui.tagfilter.clear"));
                    Assert.That(first, Is.GreaterThanOrEqualTo(0));
                    var text = open.Items[first].Text;

                    ClickRow(open, first);
                    Assert.That(open.IsClosed, Is.False, "ticking a tag keeps the menu open");
                    Assert.That(view.Tree.ActiveTags, Has.Count.EqualTo(1));
                    Assert.That(open.Rows[first].ClassListContains("ad-menu-ticked"), Is.True, "and the row shows its tick at once");
                    var clearIndex = open.Items.ToList().FindIndex(i => i.Text == L.Text("ad.ui.tagfilter.clear"));
                    Assert.That(open.Items[clearIndex].Disabled, Is.False, "Clear enables once something is ticked");
                    Assert.That(open.Items[first].Text, Is.EqualTo(text));

                    ClickRow(open, first);
                    Assert.That(open.IsClosed, Is.False);
                    Assert.That(view.Tree.ActiveTags, Is.Empty, "choosing a ticked tag unticks it");
                    Assert.That(open.Rows[first].ClassListContains("ad-menu-ticked"), Is.False);

                    ClickRow(open, first);
                    ClickRow(open, clearIndex);
                    Assert.That(open.IsClosed, Is.True, "Clear closes the menu");
                    Assert.That(view.Tree.ActiveTags, Is.Empty);
                }
                finally { root.Q<AdMenu>()?.Close(false); }
                Assert.That(root.Query<AdMenu>().ToList(), Is.Empty);
            });
        }

        [Test]
        public void RowRightClickOpensAdMenuWithRowActions()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            LayoutProbe.Measure(view, () =>
            {
                Resize(view, 360f, 700f);
                var root = RootOf(view);
                var row = view.Tree.RowFor(hat.id);
                var element = view.Tree.MakeRow();
                element.userData = row;
                view.Add(element);
                LayoutProbe.Validate(view);
                try
                {
                    Press(element, 0);
                    Assert.That(root.Query<AdMenu>().ToList(), Is.Empty, "a left click opens nothing");

                    Press(element, 1);
                    Release(element, 1);
                    var open = root.Q<AdMenu>();
                    Assert.That(open, Is.Not.Null, "a right click on a row opens an AdMenu");
                    var shown = open.Items.Where(i => !i.Separator).Select(i => i.Text).ToList();
                    Assert.That(shown, Is.EqualTo(view.Tree.RowActions(row).Select(a => L.Text(a.LabelKey)).ToList()), "the same actions, in the same order, as RowActions");
                    var move = open.Items.First(i => i.Text == L.Text("ad.ui.menu.move"));
                    Assert.That(move.Children, Is.Not.Null.And.Not.Empty, "Move to is a submenu");
                    Assert.That(move.Children[0].Text, Is.EqualTo(L.Text("ad.ui.menu.move.root")));
                    var delete = open.Items.Last();
                    Assert.That(delete.Text, Is.EqualTo(L.Text("ad.ui.menu.delete")));
                    Assert.That(delete.Shortcut, Is.EqualTo(L.Text("ad.ui.shortcut.delete")));
                    Assert.That(open.Items[open.Items.Count - 2].Separator, Is.True, "a separator sits right before Delete");

                    open.Close(false);
                    menu.locked = true;
                    Press(element, 1);
                    Assert.That(root.Query<AdMenu>().ToList(), Is.Empty, "a locked component offers no edits");
                }
                finally
                {
                    foreach (var left in root.Query<AdMenu>().ToList()) left.Close(false);
                    menu.locked = false;
                }
            });
        }

        [Test]
        public void NoImguiMenusRemainInEditorUi()
        {
            string uss = AssetDatabase.FindAssets("AvatarDoctor t:StyleSheet")
                .Select(AssetDatabase.GUIDToAssetPath)
                .First(p => p.EndsWith("/AvatarDoctor.uss"));
            string editorDir = Path.GetDirectoryName(Path.GetDirectoryName(uss));
            var banned = new[] { "GenericMenu", "ContextualMenuManipulator", "DropdownMenu", "ShowAsContext", "DisplayPopupMenu" };
            var offenders = new List<string>();
            foreach (var file in Directory.GetFiles(editorDir, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                foreach (var name in banned)
                    if (text.Contains(name)) offenders.Add(Path.GetFileName(file) + ": " + name);
            }
            Assert.That(offenders, Is.Empty, "every popup goes through AdMenu");
        }

        static void Pointer<T>(VisualElement target, EventType type, int button, EventModifiers modifiers) where T : PointerEventBase<T>, new()
        {
            using (var e = PointerEventBase<T>.GetPooled(new Event { type = type, button = button, modifiers = modifiers, mousePosition = target.worldBound.center }))
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        AvatarDoctorView NewView(AvatarDoc menu) => new AvatarDoctorView(new SerializedObject(menu), menu);

        static void InView(AvatarDoctorView view, Action<VisualElement> body)
        {
            LayoutProbe.Measure(view, () =>
            {
                Resize(view, 400f, 900f);
                var root = RootOf(view);
                try { body(root); }
                finally { foreach (var left in root.Query<AdMenu>().ToList()) left.Close(false); }
            }, 400f, 900f);
        }

        static AdMenu Opened(VisualElement root, string what)
        {
            var open = root.Q<AdMenu>();
            Assert.That(open, Is.Not.Null, what + " opens an AdMenu");
            return open;
        }

        [Test]
        public void AdMenuClosesWhenItsTargetLeavesThePanel()
        {
            InWindow(opener =>
            {
                var root = RootOf(opener);
                var lines = new List<string>();
                var oldLog = AdMenu.Log;
                AdMenu.Log = lines.Add;
                try
                {
                    var menu = AdMenu.Open(opener.worldBound, opener, Sample(new List<string>()));
                    Assert.That(root.Query<AdMenu>().ToList(), Has.Count.EqualTo(1));
                    Assert.DoesNotThrow(() => opener.RemoveFromHierarchy());
                    Assert.That(menu.IsClosed, Is.True);
                    Assert.That(root.Query<AdMenu>().ToList(), Is.Empty, "a menu never outlives the control that opened it");
                }
                finally { AdMenu.Log = oldLog; }
            });
        }

        [Test]
        public void ControlClickOpensTheRowMenuToo()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = NewView(menu);
            InView(view, root =>
            {
                var element = view.Tree.MakeRow();
                element.userData = view.Tree.RowFor(hat.id);
                view.Add(element);
                LayoutProbe.Validate(view);
                Pointer<PointerDownEvent>(element, EventType.MouseDown, 0, EventModifiers.None);
                Pointer<PointerUpEvent>(element, EventType.MouseUp, 0, EventModifiers.None);
                Assert.That(root.Query<AdMenu>().ToList(), Is.Empty);
                Pointer<PointerDownEvent>(element, EventType.MouseDown, 0, EventModifiers.Control);
                Pointer<PointerUpEvent>(element, EventType.MouseUp, 0, EventModifiers.Control);
                Opened(root, "Control and left click on a row");
            });
        }

        [Test]
        public void DeleteKeyStillDeletesAfterAMenuClosed()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = NewView(menu);
            InView(view, root =>
            {
                var element = view.Tree.MakeRow();
                element.userData = view.Tree.RowFor(hat.id);
                view.Add(element);
                LayoutProbe.Validate(view);
                view.Tree.Tree.Focus();
                Press(element, 1);
                Release(element, 1);
                var open = Opened(root, "a right click");
                Key(open, KeyCode.Escape, (char)27);
                Assert.That(root.Query<AdMenu>().ToList(), Is.Empty);
                view.Tree.Tree.Focus();
                Assert.That(view.Tree.Selected, Is.Not.Null, "the menu selected its row");

                Key(view.Tree.Tree, KeyCode.Delete);
                Assert.That(menu.entries.Any(e => e.id == hat.id), Is.False, "the Delete key still deletes the selected entry; selected is " + (view.Tree.Selected == null ? "gone" : view.Tree.Selected.Kind.ToString()));
            });
        }

        [Test]
        public void MoveToSubmenuListsTheTargetsAndMovesOnChoice()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var view = NewView(menu);
            InView(view, root =>
            {
                var element = view.Tree.MakeRow();
                element.userData = view.Tree.RowFor(hat.id);
                view.Add(element);
                LayoutProbe.Validate(view);
                Press(element, 1);
                Release(element, 1);
                var open = Opened(root, "a right click");
                int move = open.Items.ToList().FindIndex(i => i.Children != null);
                Assert.That(move, Is.GreaterThanOrEqualTo(0));
                ClickRow(open, move);
                Assert.That(open.Items.Select(i => i.Text), Is.EqualTo(new[] { L.Text("ad.ui.menu.move.root"), "Outfits" }));
                ClickRow(open, 1);
                Assert.That(open.IsClosed, Is.True);
                Assert.That(hat.parentId, Is.EqualTo(outfits.id), "choosing a target moves the entry into it");
            });
        }

        [Test]
        public void AdMenuScrollsAtTwelveRowsAndStaysNarrow()
        {
            InWindow(opener =>
            {
                var items = Enumerable.Range(0, 20).Select(i => new AdMenu.Item
                {
                    Text = "Item " + i,
                    Description = "A long description that goes on and on so that the row has to wrap onto a second line inside the card " + i,
                    Action = () => { },
                }).ToList();
                var menu = AdMenu.Open(opener.worldBound, opener, items);
                try
                {
                    LayoutProbe.Validate(menu);
                    var card = menu.Card;
                    Assert.That(card.worldBound.width, Is.LessThanOrEqualTo(AdMenu.MaxWidth + 0.5f));
                    Assert.That(card.layout.height, Is.LessThanOrEqualTo(AdMenu.MaxRows * 34f + 10f + 0.5f), "no taller than twelve described rows");
                    Assert.That(card.contentContainer.layout.height, Is.GreaterThan(card.layout.height), "the rest scrolls");
                }
                finally { menu?.Close(false); }
            }, 360f, 900f);
        }

        [Test]
        public void InstallPathPickerOpensAnAdMenu()
        {
            var menu = avatar.AddMenu();
            var view = NewView(menu);
            InView(view, root =>
            {
                var pick = view.Header.Q("ad-install-path").parent.Q<Button>();
                UiTestEvents.SimulateClick(pick);
                var open = Opened(root, "the install path picker");
                Assert.That(open.Items[0].Text, Is.EqualTo(L.Text("ad.ui.install.root")));
                Assert.That(open.Items[0].Checked, Is.True);
                Assert.That(open.Items.Count, Is.GreaterThan(1), "and every submenu path it can install into");
            });
        }

        [Test]
        public void EntryPickersOpenAnAdMenu()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            hat.exclusiveGroup = "Outfit";
            var view = NewView(menu);
            InView(view, root =>
            {
                view.Tree.SelectEntry(hat.id);
                UiTestEvents.SimulateClick(view.Detail.Q<Button>("ad-add-effect"));
                var effects = Opened(root, "Add effect");
                Assert.That(effects.Items, Is.Not.Empty);
                Assert.That(effects.Items.All(i => !string.IsNullOrEmpty(i.Description)), Is.True, "each effect carries its description");
                effects.Close(false);

                var group = view.Detail.Query<TextField>().ToList().First(f => f.label == L.Text("ad.ui.group"));
                UiTestEvents.SimulateClick(group.parent.Q<Button>());
                var groups = Opened(root, "the exclusive group picker");
                Assert.That(groups.Items[0].Text, Is.EqualTo(L.Text("ad.ui.group.none")));
                Assert.That(groups.Items.Any(i => i.Text == "Outfit" && i.Checked), Is.True, "the current group is ticked");
            });
        }

        [Test]
        public void ExistingItemMovePickerOpensAnAdMenu()
        {
            var menu = avatar.AddMenu();
            var view = NewView(menu);
            InView(view, root =>
            {
                view.Tree.SelectKey(view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Name == "RootToggle").Key);
                UiTestEvents.SimulateClick(view.Detail.Q<TextField>("ad-existing-move").parent.Q<Button>());
                var open = Opened(root, "the existing item move picker");
                Assert.That(open.Items[0].Text, Is.EqualTo(L.Text("ad.ui.existing.stay")));
                Assert.That(open.Items[0].Checked, Is.True);
                Assert.That(open.Items.Count, Is.GreaterThan(1));
            });
        }

        [Test]
        public void PresetControlPickerOpensAnAdMenu()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            var preset = menu.AddEntry(EntryKind.Preset, "Casual");
            preset.presetStates.Add(new PresetState { entryId = hat.id, on = true });
            var view = NewView(menu);
            InView(view, root =>
            {
                view.Tree.SelectEntry(preset.id);
                UiTestEvents.SimulateClick(view.Detail.Q<Button>("ad-preset-control"));
                var open = Opened(root, "the preset control picker");
                var item = open.Items.Single(i => i.Text.EndsWith("Hat"));
                Assert.That(item.Checked, Is.True, "the control the state uses is ticked");
            });
        }

        [Test]
        public void PropertyPickerOpensAnAdMenu()
        {
            var menu = avatar.AddMenu();
            var shine = menu.AddEntry(EntryKind.Toggle, "Shine");
            shine.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 0.9f });
            var view = NewView(menu);
            InView(view, root =>
            {
                view.Tree.SelectEntry(shine.id);
                var list = view.Detail.Query<TargetListView>().ToList().First(l => l.name == "ad-properties");
                var row = list.RowAt(0);
                view.Add(row);
                LayoutProbe.Validate(view);
                var button = row.Q<Button>("property");
                Assert.That(button, Is.Not.Null, "the property row is built");
                UiTestEvents.SimulateClick(button);
                var open = Opened(root, "the property picker");
                Assert.That(open.Items[0].Text, Is.EqualTo(L.Text("ad.ui.property.other")));
                Assert.That(open.Items.Skip(1).All(i => i.Children != null && i.Children.Count > 0), Is.True, "the choices sit in letter submenus");
            });
        }

        [Test]
        public void TreeEmptySpaceAndMoveFallbackOpenAnAdMenu()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            menu.AddEntry(EntryKind.Submenu, "Outfits");
            var view = NewView(menu);
            InView(view, root =>
            {
                Press(view.Tree, 1);
                var open = Opened(root, "a right click on the tree");
                Assert.That(open.Items.Select(i => i.Text), Is.EqualTo(new[] { L.Text("ad.ui.capture_missing") }));
                open.Close(false);

                var row = view.Tree.RowFor(hat.id);
                view.Tree.SelectEntry(hat.id);
                view.Tree.RowActions(row).First(a => a.LabelKey == "ad.ui.menu.move").Run();
                var move = Opened(root, "the Move to action run directly");
                Assert.That(move.Items.Select(i => i.Text), Is.EqualTo(new[] { L.Text("ad.ui.menu.move.root"), "Outfits" }));
                move.Close(false);

                menu.locked = true;
                try
                {
                    Press(view.Tree, 1);
                    Assert.That(root.Query<AdMenu>().ToList(), Is.Empty, "a locked component offers no capture");
                }
                finally { menu.locked = false; }
            });
        }
    }
}
#endif
