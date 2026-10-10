#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    /// The search field at the top of AdMenu, and the blend shape, material property, parameter and avatar object
    /// pickers that open with it. Keys and clicks go through real events on a menu attached to a shown host window,
    /// sent to the element the panel's own focus controller says has the focus.
    /// </summary>
    public class PickerSearchTests
    {
        TestAvatar avatar;
        readonly List<UnityEngine.Object> made = new List<UnityEngine.Object>();
        UnityEditor.Editor editor;

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
            if (editor != null) UnityEngine.Object.DestroyImmediate(editor);
            editor = null;
            avatar.Dispose();
            foreach (var thing in made) if (thing != null) UnityEngine.Object.DestroyImmediate(thing);
            made.Clear();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        static VisualElement RootOf(VisualElement element) =>
            Resources.FindObjectsOfTypeAll<EditorWindow>().First(w => w.rootVisualElement.panel == element.panel && w.rootVisualElement.Contains(element)).rootVisualElement;

        static VisualElement Focused(AdMenu menu) => menu.focusController?.focusedElement as VisualElement;

        static bool FieldHasFocus(AdMenu menu)
        {
            var focused = Focused(menu);
            // The focus controller names the field itself while its inner text element has the focus.
            return menu.SearchField != null && focused != null && (focused == menu.SearchField || menu.SearchField.Contains(focused));
        }

        static readonly MethodInfo RepaintImmediately = typeof(EditorWindow).GetMethod("RepaintImmediately", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        /// <summary>
        /// What happens between two real key presses: the window lays out and draws again, which is where the field
        /// measures the text its caret moves through.
        /// </summary>
        static void Frame(VisualElement element)
        {
            if (element.panel == null) return;
            LayoutProbe.Validate(element);
            var window = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.rootVisualElement.panel == element.panel);
            if (window != null && RepaintImmediately != null) RepaintImmediately.Invoke(window, null);
        }

        /// <summary>One layout pass of the panel, the part of a frame that places new rows: what the timed steps include.</summary>
        static void LayoutOnce(VisualElement element)
        {
            var panel = element.panel;
            panel.GetType().GetMethod("ValidateLayout", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(panel, null);
        }

        /// <summary>One key down with no target of its own, so the panel routes it to the element that has the focus, as it does a real key press, a frame after the last one.</summary>
        static void Key(AdMenu menu, KeyCode code, char character = '\0')
        {
            Frame(menu);
            SendKey(menu, code, character);
        }

        static void SendKey(VisualElement inPanel, KeyCode code, char character = '\0')
        {
            using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = code, character = character }))
                inPanel.SendEvent(e);
        }

        /// <summary>Types text into the focused search field as Windows sends a key press: the key first, then its character.</summary>
        static void Type(AdMenu menu, string text)
        {
            foreach (var c in text)
            {
                Assert.That(FieldHasFocus(menu), Is.True, "the search field has the focus while typing '" + c + "'");
                TypeKey(menu, c);
            }
        }

        static void TypeKey(AdMenu menu, char c)
        {
            var code = KeyOf(c);
            if (code != KeyCode.None) Key(menu, code);
            Key(menu, KeyCode.None, c);
        }

        static KeyCode KeyOf(char c)
        {
            var lower = char.ToLowerInvariant(c);
            return lower >= 'a' && lower <= 'z' ? KeyCode.A + (lower - 'a') : c == ' ' ? KeyCode.Space : KeyCode.None;
        }

        /// <summary>The key and then its character, with no frame between them or before: for the timed steps.</summary>
        static void TypeKeyNow(AdMenu menu, char c)
        {
            var code = KeyOf(c);
            if (code != KeyCode.None) SendKey(menu, code);
            SendKey(menu, KeyCode.None, c);
        }

        static void Pointer<T>(VisualElement target, EventType type, int button) where T : PointerEventBase<T>, new()
        {
            using (var e = PointerEventBase<T>.GetPooled(new Event { type = type, button = button, mousePosition = target.worldBound.center }))
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        /// <summary>A pointer event with no target of its own: the panel sends it to whatever lies under the pointer at that point, as it does a real one.</summary>
        static void PointerAt<T>(VisualElement inPanel, Vector2 position, EventType type) where T : PointerEventBase<T>, new()
        {
            using (var e = PointerEventBase<T>.GetPooled(new Event { type = type, button = 0, mousePosition = position }))
                inPanel.SendEvent(e);
        }

        static void Press(VisualElement target) => Pointer<PointerDownEvent>(target, EventType.MouseDown, 0);

        static void Release(VisualElement target) => Pointer<PointerUpEvent>(target, EventType.MouseUp, 0);

        static void Click(VisualElement target)
        {
            Press(target);
            Release(target);
        }

        /// <summary>No window has the keyboard focus or the pointer, so a picker handed only a rectangle finds nothing: what is left is the control the caller passed.</summary>
        static void WithNoWindowInFront(Action body)
        {
            var focused = AdMenu.FocusedWindow;
            var pointed = AdMenu.PointerWindow;
            AdMenu.FocusedWindow = () => null;
            AdMenu.PointerWindow = () => null;
            try { body(); }
            finally
            {
                AdMenu.FocusedWindow = focused;
                AdMenu.PointerWindow = pointed;
            }
        }

        static void Hover(VisualElement target) => Pointer<PointerMoveEvent>(target, EventType.MouseMove, 0);

        static List<string> Texts(AdMenu menu) => menu.Items.Select(i => i.Text).ToList();

        static List<string> Notes(AdMenu menu) => menu.Rows.Select(r => r.Q<Label>(className: "ad-menu-note")?.text ?? "").ToList();

        /// <summary>Pins the size of the window hosting element: Unity reopens a window type at the size it last had, and a control past its bottom edge is not under any pointer.</summary>
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
                var root = RootOf(opener);
                try { body(opener); }
                finally { foreach (var left in root.Query<AdMenu>().ToList()) left.Close(false); }
            }, width, height);
        }

        static AdMenu.Item Leaf(string text, List<string> log, string group) => new AdMenu.Item { Text = text, Action = () => log.Add(group + "/" + text) };

        /// <summary>Two submenus, the second with a separator, and one item of its own.</summary>
        static List<AdMenu.Item> Grouped(List<string> log) => new List<AdMenu.Item>
        {
            new AdMenu.Item { Text = "Body", Children = new List<AdMenu.Item> { Leaf("Smile", log, "Body"), Leaf("Blink", log, "Body"), Leaf("MouthSmileWide", log, "Body") } },
            new AdMenu.Item { Text = "Face", Children = new List<AdMenu.Item> { Leaf("Smile", log, "Face"), AdMenu.Item.Line(), Leaf("Frown", log, "Face") } },
            new AdMenu.Item { Text = "Wave", Action = () => log.Add("Wave") },
        };

        Mesh Track(Mesh mesh)
        {
            made.Add(mesh);
            return mesh;
        }

        SkinnedMeshRenderer AddMesh(string name, params string[] shapes)
        {
            var go = new GameObject(name);
            go.transform.SetParent(avatar.Root.transform);
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = Track(TestAvatar.QuadWithShapes(shapes));
            return renderer;
        }

        AvatarDoctorView NewView(AvatarDoc menu) => new AvatarDoctorView(new SerializedObject(menu), menu);

        [Test]
        public void SearchFieldHasTheFocusWhenTheMenuOpens()
        {
            InWindow(opener =>
            {
                var menu = AdMenu.Open(opener.worldBound, opener, Grouped(new List<string>()), search: true);
                Assert.That(menu.SearchField, Is.Not.Null, "a search menu has a field");
                Assert.That(FieldHasFocus(menu), Is.True, "and the field has the keyboard focus as soon as the menu opens");
                Assert.That(menu.SearchField.tooltip, Is.EqualTo(L.Text("ad.ui.pick.search.tip")));
                var placeholder = menu.SearchField.Q<Label>(className: "ad-search-placeholder");
                Assert.That(placeholder, Is.Not.Null);
                Assert.That(placeholder.text, Is.EqualTo(L.Text("ad.ui.search.placeholder")));
                Assert.That(placeholder.ClassListContains("ad-hidden"), Is.False, "the placeholder shows while the field is empty, focused or not");

                LayoutProbe.Validate(menu);
                var frame = menu.Frame.worldBound;
                var field = menu.SearchField.worldBound;
                Assert.That(menu.Frame.Contains(menu.SearchField) && menu.Frame.Contains(menu.Card), Is.True, "the field and the list share one card");
                Assert.That(field.yMax, Is.LessThanOrEqualTo(menu.Card.worldBound.y + 0.5f), "the field sits above the rows");
                float leftGap = field.x - frame.x;
                float rightGap = frame.xMax - field.xMax;
                Assert.That(leftGap, Is.EqualTo(rightGap).Within(0.5f), "the field spans the card with the same gap on both sides");
                Assert.That(leftGap, Is.LessThanOrEqualTo(9f), "a gap from the spacing scale, plus the card's border");
                Assert.That(menu.SearchField.Q(className: "unity-text-field__input").worldBound.height, Is.EqualTo(AdMenu.RowHeight).Within(0.5f), "the field is one row high");

                Type(menu, "x");
                Assert.That(placeholder.ClassListContains("ad-hidden"), Is.True, "and hides once something is typed");
            });
        }

        [Test]
        public void TheCardKeepsOneSizeAndPlaceWhileTyping()
        {
            InWindow(opener =>
            {
                var root = RootOf(opener);
                int heard = 0;
                EventCallback<ChangeEvent<string>> listen = e => heard++;
                root.RegisterCallback(listen);
                try
                {
                    var menu = AdMenu.Open(opener.worldBound, opener, Grouped(new List<string>()), search: true);
                    LayoutProbe.Validate(menu);
                    var before = menu.Frame.worldBound;
                    Assert.That(menu.Card.worldBound.height, Is.GreaterThanOrEqualTo(6 * AdMenu.RowHeight - 0.5f), "room for every item and one more row");
                    Type(menu, "smi");
                    LayoutProbe.Validate(menu);
                    Assert.That(menu.Frame.worldBound, Is.EqualTo(before), "the same box with results");
                    Key(menu, KeyCode.Escape);
                    Type(menu, "zzz");
                    LayoutProbe.Validate(menu);
                    Assert.That(menu.Frame.worldBound, Is.EqualTo(before), "and with nothing matching");
                    Assert.That(heard, Is.Zero, "the window never hears the search text change");
                }
                finally { root.UnregisterCallback(listen); }
            });
        }

        [Test]
        public void AMenuWithoutSearchHasNoField()
        {
            InWindow(opener =>
            {
                var menu = AdMenu.Open(opener.worldBound, opener, Grouped(new List<string>()));
                Assert.That(menu.SearchField, Is.Null);
                Assert.That(menu.focusController.focusedElement, Is.SameAs(menu), "the menu itself takes the focus, as before");
            });
        }

        [Test]
        public void ANameWithABrokenCharacterStillOpensAndSearches()
        {
            InWindow(opener =>
            {
                var log = new List<string>();
                var items = new List<AdMenu.Item> { Leaf("Bad\uD800Name", log, "Body"), Leaf("Smile", log, "Body") };
                var menu = AdMenu.Open(opener.worldBound, opener, items, search: true);
                Assert.That(menu, Is.Not.Null, "a lone surrogate in a name must not stop the menu opening");
                Type(menu, "bad");
                Assert.That(Texts(menu), Is.EqualTo(new[] { "Bad\uD800Name" }), "the name is still found by its other letters");
            });
        }

        [Test]
        public void TypingFiltersAcrossGroupsWhateverTheCase()
        {
            InWindow(opener =>
            {
                var menu = AdMenu.Open(opener.worldBound, opener, Grouped(new List<string>()), search: true);
                Type(menu, "SMI");
                Assert.That(menu.SearchField.value, Is.EqualTo("SMI"), "the typed keys reach the field");
                Assert.That(Texts(menu), Is.EqualTo(new[] { "Smile", "MouthSmileWide", "Smile" }), "every item whose name holds the text anywhere, in the groups' order");
                Assert.That(Notes(menu), Is.EqualTo(new[] { "Body", "Body", "Face" }), "each with its group as the dim text beside it");
                Assert.That(menu.MatchCount, Is.EqualTo(3));
                Assert.That(menu.SelectedIndex, Is.EqualTo(-1), "type-ahead is off: typing highlights nothing");
                Assert.That(menu.Depth, Is.EqualTo(0));
                Assert.That(menu.Card.Q(name: "ad-menu-back"), Is.Null, "results have no Back row");
                Assert.That(FieldHasFocus(menu), Is.True);
            });
        }

        [Test]
        public void SearchMatchesTheExtraTextToo()
        {
            InWindow(opener =>
            {
                var log = new List<string>();
                var items = new List<AdMenu.Item>
                {
                    new AdMenu.Item { Text = "Main Color", SearchText = "_Color", Note = "_Color", Action = () => log.Add("_Color") },
                    new AdMenu.Item { Text = "Smoothness", SearchText = "_Glossiness", Note = "_Glossiness", Action = () => log.Add("_Glossiness") },
                };
                var menu = AdMenu.Open(opener.worldBound, opener, items, search: true);
                Type(menu, "_gloss");
                Assert.That(Texts(menu), Is.EqualTo(new[] { "Smoothness" }), "the shader name finds the property its display name does not");
                Assert.That(Notes(menu), Is.EqualTo(new[] { "_Glossiness" }), "an item's own note wins over its group");
            });
        }

        [Test]
        public void HeadersAndSeparatorsStayOutOfTheResults()
        {
            InWindow(opener =>
            {
                var log = new List<string>();
                var items = new List<AdMenu.Item>
                {
                    AdMenu.Item.Heading("Kind"),
                    Leaf("Toggle", log, "Kind"),
                    Leaf("Trigger", log, "Kind"),
                    AdMenu.Item.Line(),
                    AdMenu.Item.Heading("From"),
                    Leaf("Avatar menu", log, "From"),
                    new AdMenu.Item { Text = "Toggle off", Disabled = true, Action = () => log.Add("off") },
                };
                var menu = AdMenu.Open(opener.worldBound, opener, items, search: true);
                Type(menu, "t");
                Assert.That(Texts(menu), Is.EqualTo(new[] { "Toggle", "Trigger", "Avatar menu" }), "only items that can be picked, never a header, a separator or a disabled item");
                Assert.That(Notes(menu), Is.EqualTo(new[] { "Kind", "Kind", "From" }), "a header counts as the group of the items under it");
                Assert.That(menu.Rows.Any(r => r.ClassListContains("ad-menu-header") || r.ClassListContains("ad-menu-separator")), Is.False);
            });
        }

        [Test]
        public void AnEmptyFieldBrowsesTheMenuAsBefore()
        {
            InWindow(opener =>
            {
                var log = new List<string>();
                var plain = AdMenu.Open(opener.worldBound, opener, Grouped(log));
                var plainTexts = Texts(plain);
                int plainRows = plain.Rows.Count;
                int plainArrows = plain.Card.Query<Label>(className: "ad-menu-arrow").ToList().Count;
                plain.Close(false);

                var menu = AdMenu.Open(opener.worldBound, opener, Grouped(log), search: true);
                Assert.That(Texts(menu), Is.EqualTo(plainTexts), "the same items");
                Assert.That(menu.Rows.Count, Is.EqualTo(plainRows));
                Assert.That(menu.Card.Query<Label>(className: "ad-menu-arrow").ToList().Count, Is.EqualTo(plainArrows), "the groups are submenus, as before");

                Type(menu, "smi");
                Key(menu, KeyCode.Escape);
                Assert.That(menu.SearchField.value, Is.Empty);
                Assert.That(Texts(menu), Is.EqualTo(plainTexts), "emptying the field shows the menu again");
                Assert.That(menu.IsClosed, Is.False);

                Key(menu, KeyCode.DownArrow);
                Assert.That(menu.SelectedIndex, Is.EqualTo(0));
                Key(menu, KeyCode.Return);
                Assert.That(menu.Depth, Is.EqualTo(1), "Enter opens a highlighted group");
                Assert.That(Texts(menu), Is.EqualTo(new[] { "Smile", "Blink", "MouthSmileWide" }));
                Assert.That(FieldHasFocus(menu), Is.True, "with the focus still in the field");
                Key(menu, KeyCode.Backspace);
                Assert.That(menu.Depth, Is.EqualTo(0), "Backspace in an empty field goes up one level");
                Key(menu, KeyCode.DownArrow);
                Key(menu, KeyCode.RightArrow);
                Assert.That(menu.Depth, Is.EqualTo(1), "Right opens it too while the field is empty");
                Key(menu, KeyCode.LeftArrow);
                Assert.That(menu.Depth, Is.EqualTo(0), "and Left goes back");

                Click(menu.Rows[1]);
                Assert.That(menu.Depth, Is.EqualTo(1), "a click opens a group");
                Type(menu, "smile");
                Assert.That(Texts(menu), Is.EqualTo(new[] { "Smile", "MouthSmileWide", "Smile" }), "typing inside a group still searches every group");
                Key(menu, KeyCode.Backspace);
                Key(menu, KeyCode.LeftArrow);
                Assert.That(menu.Depth, Is.EqualTo(1), "Backspace and Left belong to the text while the field has some");
                Assert.That(menu.IsClosed, Is.False);
                Key(menu, KeyCode.Escape);
                Assert.That(menu.Depth, Is.EqualTo(1), "clearing the field goes back to the group it was typed in");
                Assert.That(Texts(menu), Is.EqualTo(new[] { "Smile", null, "Frown" }), "with its separator");
                Assert.That(log, Is.Empty);
            });
        }

        [Test]
        public void NothingMatchingSaysSoInOneDisabledRow()
        {
            InWindow(opener =>
            {
                var log = new List<string>();
                var menu = AdMenu.Open(opener.worldBound, opener, Grouped(log), search: true);
                Type(menu, "zzz");
                Assert.That(menu.Rows.Count, Is.EqualTo(1));
                Assert.That(menu.Rows[0].ClassListContains("ad-menu-disabled"), Is.True);
                Assert.That(menu.Rows[0].Q<Label>(className: "ad-menu-label").text, Is.EqualTo(string.Format(L.Text("ad.ui.pick.no_match"), "zzz")));
                Assert.That(menu.MatchCount, Is.EqualTo(0));
                Key(menu, KeyCode.Return);
                Click(menu.Rows[0]);
                Assert.That(log, Is.Empty, "the row picks nothing");
                Assert.That(menu.IsClosed, Is.False);
            });
        }

        [Test]
        public void ResultsStopAtOneHundredRowsAndCountTheRest()
        {
            InWindow(opener =>
            {
                var items = Enumerable.Range(0, 10).Select(g => new AdMenu.Item
                {
                    Text = "Group " + g,
                    Children = Enumerable.Range(0, 100).Select(i => new AdMenu.Item { Text = "Item " + (g * 100 + i), Action = () => { } }).ToList(),
                }).ToList();
                var menu = AdMenu.Open(opener.worldBound, opener, items, search: true);
                Type(menu, "item");
                Assert.That(menu.MatchCount, Is.EqualTo(1000));
                Assert.That(menu.Rows.Count, Is.EqualTo(AdMenu.MaxResults + 1), "one hundred results and one row saying how many more");
                Assert.That(menu.Card.contentContainer.childCount, Is.EqualTo(AdMenu.MaxResults + 1), "no row is built for the results past the cap");
                var more = menu.Rows[AdMenu.MaxResults];
                Assert.That(more.ClassListContains("ad-menu-disabled"), Is.True);
                Assert.That(more.Q<Label>(className: "ad-menu-label").text, Is.EqualTo(UiText.Count(900, "ad.ui.pick.more.one", "ad.ui.pick.more")));
                Assert.That(Texts(menu).Take(3), Is.EqualTo(new[] { "Item 0", "Item 1", "Item 2" }), "the first hundred in order");

                Type(menu, " 99");
                Assert.That(menu.MatchCount, Is.EqualTo(11), "Item 99 and Item 990 to 999");
                Assert.That(menu.Rows.Count, Is.EqualTo(11), "no count row while everything fits");
            });
        }

        [Test]
        public void TheCountRowUsesTheSingularForOne()
        {
            var texts = new Dictionary<string, string> { { "ad.ui.pick.more", "MANY {0}" }, { "ad.ui.pick.more.one", "ONE {0}" } };
            using (L.UseTestLanguage("ja-JP", texts))
            {
                foreach (var (count, expected) in new[] { (101, "ONE 1"), (102, "MANY 2") })
                {
                    InWindow(opener =>
                    {
                        var items = Enumerable.Range(0, count).Select(i => new AdMenu.Item { Text = "Item " + i, Action = () => { } }).ToList();
                        var menu = AdMenu.Open(opener.worldBound, opener, items, search: true);
                        Type(menu, "item");
                        Assert.That(menu.Rows.Last().Q<Label>(className: "ad-menu-label").text, Is.EqualTo(expected));
                    });
                }
            }
        }

        [Test]
        public void DownUpAndEnterPickWhileTheFieldKeepsTheFocus()
        {
            InWindow(opener =>
            {
                var log = new List<string>();
                var menu = AdMenu.Open(opener.worldBound, opener, Grouped(log), search: true);
                Type(menu, "smi");
                Key(menu, KeyCode.DownArrow);
                Assert.That(menu.SelectedIndex, Is.EqualTo(0), "Down highlights the first result");
                Assert.That(FieldHasFocus(menu), Is.True, "and the field keeps the focus");
                Key(menu, KeyCode.DownArrow);
                Key(menu, KeyCode.DownArrow);
                Assert.That(menu.SelectedIndex, Is.EqualTo(2));
                Key(menu, KeyCode.DownArrow);
                Assert.That(menu.SelectedIndex, Is.EqualTo(2), "and stops at the last");
                Key(menu, KeyCode.UpArrow);
                Assert.That(menu.SelectedIndex, Is.EqualTo(1));
                Assert.That(menu.Rows[1].ClassListContains("ad-menu-selected"), Is.True);
                Assert.That(menu.SearchField.value, Is.EqualTo("smi"), "arrow keys never edit the text");
                Type(menu, "l");
                Assert.That(menu.SearchField.value, Is.EqualTo("smil"), "typing goes on after moving");
                Key(menu, KeyCode.DownArrow);
                Key(menu, KeyCode.DownArrow);
                Key(menu, KeyCode.Return);
                Assert.That(log, Is.EqualTo(new[] { "Body/MouthSmileWide" }), "Enter picks the highlighted result");
                Assert.That(menu.IsClosed, Is.True);
            });
        }

        [Test]
        public void EnterPicksTheFirstResultWhenNoneIsHighlighted()
        {
            InWindow(opener =>
            {
                var log = new List<string>();
                var menu = AdMenu.Open(opener.worldBound, opener, Grouped(log), search: true);
                Type(menu, "fro");
                Key(menu, KeyCode.Return);
                Assert.That(log, Is.EqualTo(new[] { "Face/Frown" }));
                Assert.That(menu.IsClosed, Is.True);
            });
        }

        [Test]
        public void EscapeClearsTheSearchThenCloses()
        {
            InWindow(opener =>
            {
                var log = new List<string>();
                var menu = AdMenu.Open(opener.worldBound, opener, Grouped(log), search: true);
                Type(menu, "sm");
                Key(menu, KeyCode.Escape);
                Assert.That(menu.IsClosed, Is.False, "Escape with text clears it");
                Assert.That(menu.SearchField.value, Is.Empty);
                Assert.That(Texts(menu), Is.EqualTo(new[] { "Body", "Face", "Wave" }), "and shows the menu again");
                Assert.That(FieldHasFocus(menu), Is.True);
                Key(menu, KeyCode.Escape);
                Assert.That(menu.IsClosed, Is.True, "Escape in an empty field closes the menu");
                Assert.That(log, Is.Empty);
            });
        }

        [Test]
        public void AClickPicksAResultAndHoveringHighlightsIt()
        {
            InWindow(opener =>
            {
                var log = new List<string>();
                var menu = AdMenu.Open(opener.worldBound, opener, Grouped(log), search: true);
                Type(menu, "smile");
                LayoutProbe.Validate(menu);
                Hover(menu.Rows[2]);
                Assert.That(menu.SelectedIndex, Is.EqualTo(2), "hovering highlights a result");
                Press(menu.Rows[2]);
                Assert.That(FieldHasFocus(menu), Is.True, "pressing a row leaves the focus in the field");
                Release(menu.Rows[2]);
                Assert.That(log, Is.EqualTo(new[] { "Face/Smile" }));
                Assert.That(menu.IsClosed, Is.True);
            });
        }

        [Test]
        public void TabAndAClickBesideTheMenuCloseIt()
        {
            InWindow(opener =>
            {
                var menu = AdMenu.Open(opener.worldBound, opener, Grouped(new List<string>()), search: true);
                Type(menu, "s");
                Key(menu, KeyCode.Tab);
                Assert.That(menu.IsClosed, Is.True, "Tab closes");

                var again = AdMenu.Open(opener.worldBound, opener, Grouped(new List<string>()), search: true);
                Press(again.SearchField);
                Assert.That(again.IsClosed, Is.False, "a click in the field keeps it open");
                Press(again);
                Assert.That(again.IsClosed, Is.True, "a click beside the card closes it");
                Assert.That(RootOf(opener).Query<AdMenu>().ToList(), Is.Empty);
            });
        }

        [Test]
        public void ALongNameTruncatesAndShowsItselfAsTheTooltip()
        {
            InWindow(opener =>
            {
                var longName = string.Concat(Enumerable.Repeat("VeryLongBlendShapeName", 8));
                var items = new List<AdMenu.Item>
                {
                    new AdMenu.Item { Text = "Body", Children = new List<AdMenu.Item> { Leaf(longName, new List<string>(), "Body"), Leaf("Short", new List<string>(), "Body") } },
                    new AdMenu.Item { Text = "Face", Children = new List<AdMenu.Item> { Leaf("Other", new List<string>(), "Face") } },
                };
                var menu = AdMenu.Open(opener.worldBound, opener, items, search: true);
                Type(menu, "o");
                LayoutProbe.Validate(menu);
                Assert.That(menu.Frame.worldBound.width, Is.LessThanOrEqualTo(AdMenu.MaxWidth + 0.5f), "the menu keeps its width limit");
                var row = menu.Rows[0];
                var label = row.Q<Label>(className: "ad-menu-label");
                Assert.That(row.layout.height, Is.EqualTo(AdMenu.RowHeight).Within(0.5f), "one line, the row height of every row");
                Assert.That(menu.Rows[1].layout.height, Is.EqualTo(row.layout.height).Within(0.5f));
                Assert.That(label.resolvedStyle.textOverflow, Is.EqualTo(TextOverflow.Ellipsis), "the name ends in an ellipsis");
                Assert.That(label.worldBound.xMax, Is.LessThanOrEqualTo(row.worldBound.xMax + 0.5f), "inside its row");
                Assert.That(row.Q<Label>(className: "ad-menu-note").worldBound.xMax, Is.LessThanOrEqualTo(row.worldBound.xMax + 0.5f), "the group too");
                Assert.That(row.tooltip, Does.Contain(longName), "the full name is the row's tooltip");
            });
        }

        [Test]
        public void ShapePickerGroupsByMeshAndShowsTheMeshBesideAResult()
        {
            avatar.AddBodyShapes("Smile");
            var face = AddMesh("Face", "Smile", "Frown");
            InWindow(opener =>
            {
                (SkinnedMeshRenderer renderer, string shape) picked = (null, null);
                var menu = ShapePicker.Open(opener.worldBound, opener, avatar.Root.transform, (r, s) => picked = (r, s));
                Assert.That(menu, Is.Not.Null);
                Assert.That(menu.SearchField, Is.Not.Null, "the shape picker has a search field");
                Assert.That(Texts(menu), Is.EqualTo(new[] { "Body", "Face" }), "one group per mesh, in hierarchy order");
                Assert.That(menu.Items[0].Children.Select(i => i.Text), Is.EqualTo(new[] { "Blush", "Eye_Big", "Smile" }), "each mesh's shapes in mesh order");
                Assert.That(menu.Items[1].Children.Select(i => i.Text), Is.EqualTo(new[] { "Smile", "Frown" }));

                Type(menu, "SMILE");
                Assert.That(Texts(menu), Is.EqualTo(new[] { "Smile", "Smile" }));
                Assert.That(Notes(menu), Is.EqualTo(new[] { "Body", "Face" }), "the mesh tells two shapes of one name apart");
                Key(menu, KeyCode.DownArrow);
                Key(menu, KeyCode.DownArrow);
                Key(menu, KeyCode.Return);
                Assert.That(picked.renderer, Is.SameAs(face), "the pick hands over the mesh");
                Assert.That(picked.shape, Is.EqualTo("Smile"), "and the shape, as before");
            });
        }

        [Test]
        public void ShapePickerOnOneMeshListsItsShapesDirectly()
        {
            InWindow(opener =>
            {
                (SkinnedMeshRenderer renderer, string shape) picked = (null, null);
                var menu = ShapePicker.Open(opener.worldBound, opener, avatar.Root.transform, (r, s) => picked = (r, s));
                Assert.That(Texts(menu), Is.EqualTo(new[] { "Blush", "Eye_Big" }), "no group level for a single mesh");
                Assert.That(menu.Items.All(i => i.Children == null), Is.True);
                Type(menu, "eye");
                Assert.That(Texts(menu), Is.EqualTo(new[] { "Eye_Big" }));
                LayoutProbe.Validate(menu);
                Click(menu.Rows[0]);
                Assert.That(picked.renderer, Is.SameAs(avatar.Body));
                Assert.That(picked.shape, Is.EqualTo("Eye_Big"));
            });
        }

        [Test]
        public void ShapePickerWithoutMeshesAsksForARendererFirst()
        {
            InWindow(opener =>
            {
                var menu = ShapePicker.Open(opener.worldBound, opener, avatar.Hat.transform, (r, s) => Assert.Fail("nothing to pick"));
                Assert.That(Texts(menu), Is.EqualTo(new[] { L.Text("ad.ui.pick.renderer") }));
                Assert.That(menu.Items[0].Disabled, Is.True);
            });
        }

        [Test]
        public void PropertySearchMatchesTheShaderPropertyName()
        {
            var menu = avatar.AddMenu();
            var shine = menu.AddEntry(EntryKind.Toggle, "Shine");
            shine.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 0.9f });
            var view = NewView(menu);
            LayoutProbe.Measure(view, () =>
            {
                var root = RootOf(view);
                try
                {
                    view.Tree.SelectEntry(shine.id);
                    var list = view.Detail.Query<TargetListView>().ToList().First(l => l.name == "ad-properties");
                    var row = list.RowAt(0);
                    view.Add(row);
                    LayoutProbe.Validate(view);
                    UiTestEvents.SimulateClick(row.Q<Button>("property"));
                    var open = root.Q<AdMenu>();
                    Assert.That(open, Is.Not.Null, "the property button opens an AdMenu");
                    Assert.That(open.SearchField, Is.Not.Null, "with a search field");
                    Assert.That(FieldHasFocus(open), Is.True);
                    Assert.That(open.Items[0].Text, Is.EqualTo(L.Text("ad.ui.property.other")), "Other... stays first");
                    Assert.That(open.Items.Skip(1).All(i => i.Children != null && i.Children.Count > 0), Is.True, "browsing keeps the letter groups");

                    Type(open, "_metal");
                    Assert.That(Texts(open), Is.EqualTo(new[] { "Metallic" }), "the shader's own name finds the property");
                    Assert.That(Notes(open), Is.EqualTo(new[] { "_Metallic" }), "and shows beside it");
                    Key(open, KeyCode.Return);
                    Assert.That(open.IsClosed, Is.True);
                    Assert.That(shine.properties[0].property, Is.EqualTo("_Metallic"), "the pick lands on the row as before");
                }
                finally { foreach (var left in root.Query<AdMenu>().ToList()) left.Close(false); }
            }, 400f, 900f);
        }

        [Test]
        public void ObjectSearchMatchesThePathUnderTheAvatar()
        {
            var armature = new GameObject("Armature");
            armature.transform.SetParent(avatar.Root.transform);
            var hips = new GameObject("Hips");
            hips.transform.SetParent(armature.transform);
            var spine = new GameObject("Spine");
            spine.transform.SetParent(hips.transform);
            InWindow(opener =>
            {
                UnityEngine.Object picked = null;
                var menu = AvatarObjectPicker.Open(opener.worldBound, opener, avatar.Root.transform, PickKind.GameObject, o => picked = o);
                Assert.That(menu.SearchField, Is.Not.Null);
                int spineIndex = Texts(menu).IndexOf("Spine");
                Assert.That(spineIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(Notes(menu)[spineIndex], Is.EqualTo("Armature/Hips"), "the path shows beside the name");

                Type(menu, "armature");
                Assert.That(Texts(menu), Is.EqualTo(new[] { "Armature", "Hips", "Spine" }), "the path finds everything under it");
                Key(menu, KeyCode.Escape);
                Type(menu, "hips/sp");
                Assert.That(Texts(menu), Is.EqualTo(new[] { "Spine" }));
                Assert.That(Notes(menu), Is.EqualTo(new[] { "Armature/Hips" }));
                Key(menu, KeyCode.Return);
                Assert.That(picked, Is.SameAs(spine), "the pick hands over the object, as before");
            });
        }

        [Test]
        public void ParameterPickerKeepsItsGroupsAndShowsTheType()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = NewView(menu);
            InWindow(opener =>
            {
                string picked = null;
                var open = ParameterPicker.Open(opener.worldBound, opener, view.Plan, name => picked = name);
                Assert.That(open.SearchField, Is.Not.Null);
                Assert.That(Texts(open), Is.EqualTo(new[] { L.Text("ad.ui.pick.parameter.avatar"), L.Text("ad.ui.pick.parameter.doctor"), L.Text("ad.ui.pick.parameter.vrchat") }),
                    "the avatar's own, AvatarDoc's and VRChat's, in that order; VRCFury's only when the avatar has some");
                var existing = open.Items[0].Children.Single(i => i.Text == "Existing");
                Assert.That(existing.Note, Is.EqualTo("Bool"), "the type is the dim text beside the name, not part of it");
                Assert.That(open.Items[2].Children.All(i => !i.Text.Contains("(")), Is.True, "no type in brackets in the names");
                var names = open.Items.SelectMany(g => g.Children).Select(i => i.Text).ToList();
                Assert.That(names, Is.Unique, "a name shows in the first group it turns up in only");

                Type(open, "gestureleft");
                Assert.That(Texts(open), Is.EqualTo(new[] { "GestureLeft", "GestureLeftWeight" }));
                Assert.That(Notes(open), Is.EqualTo(new[] { "Int", "Float" }));
                Key(open, KeyCode.Return);
                Assert.That(picked, Is.EqualTo("GestureLeft"), "the pick hands over the name, as before");
            });
        }

        [Test]
        public void ObjectPickerKeepsTheBonesInOneGroup()
        {
            avatar.AddHumanoidRig();
            InWindow(opener =>
            {
                UnityEngine.Object picked = null;
                var menu = AvatarObjectPicker.Open(opener.worldBound, opener, avatar.Root.transform, PickKind.Transform, o => picked = o);
                Assert.That(menu.Items[0].Text, Is.EqualTo(L.Text("ad.ui.pick.bones")), "the humanoid bones come first, in one group");
                var head = menu.Items[0].Children.SingleOrDefault(i => i.Text == "Head");
                Assert.That(head, Is.Not.Null);
                Assert.That(head.Note, Is.EqualTo("Hips/Spine/Chest/Neck"));
                Assert.That(menu.Items.Skip(1).Any(i => i.Text == "Head"), Is.False, "and nowhere else");

                Type(menu, "neck/he");
                Assert.That(Texts(menu), Is.EqualTo(new[] { "Head" }));
                Assert.That(Notes(menu), Is.EqualTo(new[] { "Hips/Spine/Chest/Neck" }));
                Key(menu, KeyCode.Return);
                Assert.That(picked, Is.SameAs(avatar.HeadBone));
            });
        }

        [Test]
        public void TheSearchIndexIsBuiltOnceWhenTheMenuOpens()
        {
            InWindow(opener =>
            {
                var items = Grouped(new List<string>());
                var menu = AdMenu.Open(opener.worldBound, opener, items, search: true);
                items[0].Children[1].Text = "Renamed";
                Type(menu, "blink");
                Assert.That(menu.MatchCount, Is.EqualTo(1), "the search reads what the items said when the menu opened");
                Key(menu, KeyCode.Escape);
                Type(menu, "renamed");
                Assert.That(menu.MatchCount, Is.EqualTo(0), "not what they say now");
            });
        }

        [Test]
        public void APickerOpenedInAViewBelongsToIt()
        {
            var menu = avatar.AddMenu();
            var link = menu.AddEntry(EntryKind.Trigger, "Link");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            var view = NewView(menu);
            LayoutProbe.Measure(view, () =>
            {
                var root = RootOf(view);
                try
                {
                    Resize(view, 400f, 900f);
                    view.Tree.SelectEntry(link.id);
                    LayoutProbe.Validate(view);
                    var button = view.Detail.Q<Button>("ad-trigger-shape");
                    Assert.That(button, Is.Not.Null);
                    Press(button);
                    Assert.That(button.focusController.focusedElement, Is.SameAs(button), "the press gives the button the focus");
                    Assert.That(root.Q<AdMenu>(), Is.Null, "and opens nothing yet");
                    Release(button);
                    var open = root.Q<AdMenu>();
                    Assert.That(open, Is.Not.Null, "the click on the trigger's shape button opens the shape picker");
                    Assert.That(open.Target, Is.SameAs(button), "on the button itself");
                    Assert.That(open.Owner, Is.SameAs(view), "in its own view, so the view holds its refreshes while the picker is open");
                    Assert.That(FieldHasFocus(open), Is.True, "and the search field has the focus after the click");
                    Type(open, "eye");
                    Key(open, KeyCode.Return);
                    Assert.That(link.sourceShape, Is.EqualTo("Eye_Big"), "the pick lands on the trigger as before");
                    Assert.That(link.sourceRenderer, Is.SameAs(avatar.Body));
                }
                finally { foreach (var left in root.Query<AdMenu>().ToList()) left.Close(false); }
            }, 400f, 900f);
        }

        [Test]
        public void AddEffectBlendShapesOpensTheShapePickerWhereTheButtonWas()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = NewView(menu);
            WithNoWindowInFront(() => LayoutProbe.Measure(view, () =>
            {
                var root = RootOf(view);
                try
                {
                    Resize(view, 400f, 900f);
                    view.Tree.SelectEntry(hat.id);
                    LayoutProbe.Validate(view);
                    UiTestEvents.SimulateClick(view.Detail.Q<Button>("ad-add-effect"));
                    var effects = root.Q<AdMenu>();
                    Assert.That(effects, Is.Not.Null);
                    int shapes = Texts(effects).IndexOf(L.Text("ad.ui.blendshapes"));
                    Assert.That(shapes, Is.GreaterThanOrEqualTo(0));
                    effects.Commit(shapes);
                    var picker = root.Q<AdMenu>();
                    Assert.That(picker, Is.Not.Null, "choosing Blend shapes opens the shape picker at the rectangle the button had");
                    Assert.That(picker, Is.Not.SameAs(effects));
                    Assert.That(picker.Target, Is.SameAs(view), "on the view, which outlives a rebuild of the button");
                    Assert.That(picker.SearchField, Is.Not.Null);
                    Type(picker, "blush");
                    Key(picker, KeyCode.Return);
                    Assert.That(hat.blendShapes.Select(b => b.shape), Is.EqualTo(new[] { "Blush" }), "and its pick adds the row");
                }
                finally { foreach (var left in root.Query<AdMenu>().ToList()) left.Close(false); }
            }, 400f, 900f));
        }

        AvatarDocShapes ShapesPiece()
        {
            var piece = new GameObject("Piece");
            piece.transform.SetParent(avatar.Root.transform);
            piece.AddComponent<SkinnedMeshRenderer>().sharedMesh = Track(TestAvatar.QuadWithShapes("Blush"));
            var shapes = piece.AddComponent<AvatarDocShapes>();
            shapes.follow.Add(new ShapeFollow { sourceMesh = avatar.Body, sourceShape = "Blush" });
            shapes.whileOn.Add(new ShapeWhileOn { mesh = avatar.Body, shape = "Blush", value = 100f });
            return shapes;
        }

        /// <summary>Clicks the control and checks the menu it opened was opened on it, then closes the menu.</summary>
        static void OpensOnItself(VisualElement root, Button button, string what, VisualElement expected = null)
        {
            Assert.That(button, Is.Not.Null, what + " is there");
            UiTestEvents.SimulateClick(button);
            var open = root.Q<AdMenu>();
            Assert.That(open, Is.Not.Null, what + " opens its picker");
            Assert.That(open.Target, Is.SameAs(expected ?? button), what + " opens it on " + (expected == null ? "itself" : "the view"));
            Assert.That(open.SearchField, Is.Not.Null, what + " opens it with search");
            open.Close(false);
        }

        [Test]
        public void TheShapesInspectorOpensEachPickerOnItsButton()
        {
            var shapes = ShapesPiece();
            editor = UnityEditor.Editor.CreateEditor(shapes);
            var inspector = ((AvatarDocShapesEditor)editor).CreateInspectorGUI();
            WithNoWindowInFront(() => LayoutProbe.Measure(inspector, () =>
            {
                var root = RootOf(inspector);
                try
                {
                    OpensOnItself(root, inspector.Q<Button>("ad-shapes-add-matching"), "Add matching shapes");
                    OpensOnItself(root, inspector.Q<Button>("ad-shapes-follow-shape-0"), "a follow row's source shape");
                    OpensOnItself(root, inspector.Q<Button>("ad-shapes-follow-local-0"), "a follow row's own shape");
                    OpensOnItself(root, inspector.Q<Button>("ad-shapes-while-on-shape-0"), "a while-on row's shape");
                }
                finally { foreach (var left in root.Query<AdMenu>().ToList()) left.Close(false); }
            }));
        }

        [Test]
        public void TheViewOpensEachPickerOnItsButton()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            hat.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            var wave = menu.AddEntry(EntryKind.Trigger, "Wave");
            wave.objects.Add(new ObjectTarget { target = avatar.Glasses });
            wave.source = TriggerSource.Parameter;
            wave.sourceParameter = "GestureLeft";
            var link = menu.AddEntry(EntryKind.Trigger, "Link");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            var ears = menu.AddEntry(EntryKind.Trigger, "Ears");
            ears.objects.Add(new ObjectTarget { target = avatar.Glasses });
            ears.when = new List<ConditionGroup>
            {
                new ConditionGroup { all = new List<WhenCondition>
                {
                    new WhenCondition { kind = WhenConditionKind.Parameter, sourceParameter = "GestureRight", condition = ParameterCondition.Equals, conditionValue = 1f },
                    new WhenCondition { kind = WhenConditionKind.Toggle, sourceToggleId = hat.id, sourceToggleOn = true },
                } },
            };
            var casual = menu.AddEntry(EntryKind.Preset, "Casual");
            casual.presetParameters.Add(new PresetParameter { name = "VRCEmote", value = 1f });
            var view = NewView(menu);
            WithNoWindowInFront(() => LayoutProbe.Measure(view, () =>
            {
                var root = RootOf(view);
                try
                {
                    Resize(view, 400f, 900f);
                    VisualElement Select(MenuEntry entry)
                    {
                        view.Tree.SelectEntry(entry.id);
                        LayoutProbe.Validate(view);
                        return view.Detail;
                    }
                    OpensOnItself(root, Select(wave).Q<TextField>("ad-trigger-parameter").parent.Q<Button>(), "a parameter trigger's picker button");
                    OpensOnItself(root, Select(link).Q<Button>("ad-trigger-shape"), "a blend shape trigger's shape button");
                    OpensOnItself(root, Select(ears).Q<TextField>("ad-when-parameter").parent.Q<Button>(), "a requirement's parameter picker button");
                    OpensOnItself(root, Select(casual).Q<Button>("ad-preset-param-pick"), "a preset's parameter picker button");
                    OpensOnItself(root, Select(hat).Q<Button>("ad-shape-set-capture"), "Capture");
                    OpensOnItself(root, Select(hat).Q<Button>("ad-shape-set-hold"), "Hold the rest");

                    var list = Select(hat).Query<TargetListView>().ToList().First(l => l.name == "ad-blendshapes");
                    OpensOnItself(root, list.Query<Button>().ToList().First(b => b.text == L.Text("ad.ui.add")), "the blend shape section's + Add", view);
                    var row = list.RowAt(0);
                    view.Add(row);
                    LayoutProbe.Validate(view);
                    OpensOnItself(root, row.Q<Button>("shape"), "a blend shape row's shape button");
                    row.RemoveFromHierarchy();
                }
                finally { foreach (var left in root.Query<AdMenu>().ToList()) left.Close(false); }
            }, 400f, 900f));
        }

        [Test]
        public void TheWelcomeCardOpensItsPickerOnItsButton()
        {
            var menu = avatar.AddMenu();
            var view = NewView(menu);
            WithNoWindowInFront(() => LayoutProbe.Measure(view, () =>
            {
                var root = RootOf(view);
                try { OpensOnItself(root, view.Tree.Q<Button>("ad-welcome-toggle"), "Make a toggle"); }
                finally { foreach (var left in root.Query<AdMenu>().ToList()) left.Close(false); }
            }));
        }

        [Test]
        public void AButtonScrolledHalfOutOfViewStillOpensItsPicker()
        {
            var shapes = ShapesPiece();
            editor = UnityEditor.Editor.CreateEditor(shapes);
            var inspector = ((AvatarDocShapesEditor)editor).CreateInspectorGUI();
            var scroll = new ScrollView();
            scroll.Add(inspector);
            LayoutProbe.Measure(scroll, () =>
            {
                var root = RootOf(scroll);
                try
                {
                    var button = inspector.Q<Button>("ad-shapes-follow-shape-0");
                    scroll.style.height = button.worldBound.center.y - scroll.worldBound.y - 2f;
                    LayoutProbe.Validate(scroll);
                    var viewport = scroll.contentViewport.worldBound;
                    Assert.That(button.worldBound.y, Is.LessThan(viewport.yMax), "the button's top is in view");
                    Assert.That(button.worldBound.center.y, Is.GreaterThan(viewport.yMax), "and its middle is scrolled out");
                    OpensOnItself(root, button, "a half hidden shape button");
                }
                finally { foreach (var left in root.Query<AdMenu>().ToList()) left.Close(false); }
            });
        }

        [Test]
        public void ThePickersNoLongerOpenUnitysDropdown()
        {
            string uss = AssetDatabase.FindAssets("AvatarDoctor t:StyleSheet").Select(AssetDatabase.GUIDToAssetPath).First(p => p.EndsWith("/AvatarDoctor.uss"));
            string folder = Path.GetDirectoryName(uss);
            foreach (var file in new[] { "ShapePicker.cs", "ParameterPicker.cs", "AvatarObjectPicker.cs" })
            {
                var text = File.ReadAllText(Path.Combine(folder, file));
                Assert.That(text, Does.Not.Contain("AdvancedDropdown"), file);
                Assert.That(text, Does.Not.Contain("UnityEditor.IMGUI.Controls"), file);
            }
        }

        [Test]
        public void ShowFindsTheButtonFromItsRectangleAlone()
        {
            InWindow(opener =>
            {
                opener.style.left = 137f;
                opener.style.top = 211f;
                opener.style.width = 93f;
                LayoutProbe.Validate(opener);
                var root = RootOf(opener);
                var window = Resources.FindObjectsOfTypeAll<EditorWindow>().First(w => w.rootVisualElement == root);
                WithNoWindowInFront(() =>
                    Assert.That(AdMenu.FindTarget(opener.worldBound), Is.Null, "a window that has neither the keyboard focus nor the pointer is never looked in"));

                var pointed = AdMenu.PointerWindow;
                AdMenu.PointerWindow = () => window;
                try
                {
                    Assert.That(AdMenu.FindTarget(opener.worldBound), Is.SameAs(opener), "the element with that rectangle under its centre, in the window under the pointer");
                    Assert.That(AdMenu.FindTarget(Rect.zero), Is.Null, "a rectangle of nothing finds nothing");
                    Assert.That(AdMenu.FindTarget(new Rect(float.NaN, float.NaN, float.NaN, float.NaN)), Is.Null, "nor does one never laid out");

                    ShapePicker.Show(opener.worldBound, avatar.Root.transform, (r, s) => { });
                    var shapes = root.Q<AdMenu>();
                    Assert.That(shapes, Is.Not.Null, "the shape picker opens in the button's own window");
                    Assert.That(shapes.Target, Is.SameAs(opener));
                    shapes.Close(false);

                    var menu = avatar.AddMenu();
                    ParameterPicker.Show(opener.worldBound, NewView(menu).Plan, _ => { });
                    var parameters = root.Q<AdMenu>();
                    Assert.That(parameters, Is.Not.Null, "so does the parameter picker");
                    Assert.That(parameters.Target, Is.SameAs(opener));
                    parameters.Close(false);

                    AvatarObjectPicker.Show(opener.worldBound, avatar.Root.transform, PickKind.GameObject, _ => { });
                    var objects = root.Q<AdMenu>();
                    Assert.That(objects, Is.Not.Null, "and the avatar object picker");
                    Assert.That(objects.Target, Is.SameAs(opener));
                }
                finally { AdMenu.PointerWindow = pointed; }
            });
        }

        [Test]
        public void TheSelectorOpensTheObjectPickerOnItself()
        {
            InWindow(opener =>
            {
                var field = new ObjectField { objectType = typeof(GameObject) };
                field.style.position = Position.Absolute;
                field.style.left = 20f;
                field.style.top = 80f;
                field.style.width = 200f;
                opener.parent.Add(field);
                LayoutProbe.Validate(field);
                var selector = field.Q<VisualElement>(className: ObjectField.selectorUssClassName);
                UnityEngine.Object picked = null;
                WithNoWindowInFront(() =>
                    Assert.That(AvatarObjectPicker.TryOpen(selector, () => avatar.Root.transform, PickKind.GameObject, o => picked = o), Is.True));
                var open = RootOf(field).Q<AdMenu>();
                Assert.That(open, Is.Not.Null);
                Assert.That(open.Target, Is.SameAs(selector));
                Type(open, "glasses");
                Key(open, KeyCode.Return);
                Assert.That(picked, Is.SameAs(avatar.Glasses));
            });
        }

        [Test]
        public void AShapesInspectorMenuClosesWhenTheInspectorGoesAway()
        {
            var shapes = ShapesPiece();
            editor = UnityEditor.Editor.CreateEditor(shapes);
            var inspector = ((AvatarDocShapesEditor)editor).CreateInspectorGUI();
            WithNoWindowInFront(() => LayoutProbe.Measure(inspector, () =>
            {
                var root = RootOf(inspector);
                try
                {
                    var button = inspector.Q<Button>("ad-shapes-follow-shape-0");
                    UiTestEvents.SimulateClick(button);
                    var open = root.Q<AdMenu>();
                    Assert.That(open, Is.Not.Null, "the Shapes inspector's shape button opens the picker");
                    Assert.That(open.Target, Is.SameAs(button), "on the button");
                    Assert.That(open.Owner, Is.Null, "an inspector without an AvatarDoc view");
                    inspector.RemoveFromHierarchy();
                    Assert.That(open.IsClosed, Is.True, "the menu closes when the inspector goes away");
                    Assert.That(root.Query<AdMenu>().ToList(), Is.Empty);
                }
                finally { foreach (var left in root.Query<AdMenu>().ToList()) left.Close(false); }
            }));
        }

        /// <summary>
        /// An object field at mid height of a window too short for the search card above or below it, so the card
        /// lies over the selector, opened by a real press and release on the selector, each sent to whatever lies
        /// under the pointer.
        /// </summary>
        void WithSelectorClicked(Action<AdMenu, VisualElement, Func<UnityEngine.Object>> body)
        {
            for (int i = 0; i < 20; i++) new GameObject("Piece" + i).transform.SetParent(avatar.Root.transform);
            InWindow(opener =>
            {
                var root = RootOf(opener);
                var field = new ObjectField { objectType = typeof(GameObject) };
                field.style.position = Position.Absolute;
                field.style.left = 20f;
                field.style.width = 200f;
                field.style.top = root.layout.height / 2f - 9f;
                opener.parent.Add(field);
                UnityEngine.Object picked = null;
                AvatarObjectPicker.TakeOverSelector(field, () => avatar.Root.transform, PickKind.GameObject, o => picked = o);
                LayoutProbe.Validate(field);
                var selector = field.Q<VisualElement>(className: ObjectField.selectorUssClassName);
                var at = selector.worldBound.center;
                PointerAt<PointerDownEvent>(selector, at, EventType.MouseDown);
                var early = root.Q<AdMenu>();
                if (early != null) Frame(early);
                PointerAt<PointerUpEvent>(selector, at, EventType.MouseUp);
                var menu = root.Q<AdMenu>();
                Assert.That(menu, Is.Not.Null, "the click on the selector opens the picker");
                Frame(menu);
                body(menu, selector, () => picked);
            }, 360f, 400f);
        }

        [Test]
        public void TheClickThatOpensAnObjectPickerPicksNothing()
        {
            WithSelectorClicked((menu, selector, picked) =>
            {
                Assert.That(picked(), Is.Null, "the release that opened the picker chose nothing");
                Assert.That(menu.IsClosed, Is.False);
                Assert.That(menu.Frame.worldBound.Overlaps(selector.worldBound), Is.True, "with no room above or below, the card lies over the selector");
            });
        }

        [Test]
        public void ARealClickOnARowStillPicksAfterThat()
        {
            WithSelectorClicked((menu, selector, picked) =>
            {
                var row = menu.Rows[0];
                PointerAt<PointerDownEvent>(menu, row.worldBound.center, EventType.MouseDown);
                PointerAt<PointerUpEvent>(menu, row.worldBound.center, EventType.MouseUp);
                Assert.That(picked(), Is.SameAs(avatar.Root), "a press and a release on a row pick it");
                Assert.That(menu.IsClosed, Is.True);
            });
        }

        [Test]
        public void TheSpaceThatOpensAnObjectPickerTypesNothing()
        {
            InWindow(opener =>
            {
                var field = new ObjectField { objectType = typeof(GameObject) };
                field.style.position = Position.Absolute;
                field.style.left = 20f;
                field.style.top = 80f;
                field.style.width = 200f;
                opener.parent.Add(field);
                AvatarObjectPicker.TakeOverSelector(field, () => avatar.Root.transform, PickKind.GameObject, _ => { });
                LayoutProbe.Validate(field);
                field.Focus();
                SendKey(field, KeyCode.Space);
                var menu = RootOf(field).Q<AdMenu>();
                Assert.That(menu, Is.Not.Null, "Space on the focused field opens the picker");
                Frame(menu);
                SendKey(menu, KeyCode.None, ' ');
                Assert.That(menu.SearchField.value, Is.Empty, "the space Windows sends after the key types nothing");
                Assert.That(menu.SearchField.Q<Label>(className: "ad-search-placeholder").ClassListContains("ad-hidden"), Is.False, "and the placeholder still shows");
                Type(menu, "a b");
                Assert.That(menu.SearchField.value, Is.EqualTo("a b"), "a space between letters still types");
            });
        }

        [Test]
        public void SearchingFiveMeshesOfFourHundredShapesStaysQuick()
        {
            for (int m = 0; m < 5; m++)
                AddMesh("Mesh" + m, Enumerable.Range(0, 400).Select(i => "Shape" + m + "_" + i.ToString("000")).ToArray());
            InWindow(opener =>
            {
                // A first pass compiles the code the timed pass runs, so the times measure the work, not the JIT.
                var warm = ShapePicker.Open(opener.worldBound, opener, avatar.Root.transform, (r, s) => { });
                Type(warm, "sha");
                warm.Close(false);

                var clock = Stopwatch.StartNew();
                var menu = ShapePicker.Open(opener.worldBound, opener, avatar.Root.transform, (r, s) => { });
                LayoutOnce(menu);
                var steps = new List<double> { clock.Elapsed.TotalMilliseconds };
                foreach (var c in "sha")
                {
                    Frame(menu);
                    clock.Restart();
                    TypeKeyNow(menu, c);
                    LayoutOnce(menu);
                    steps.Add(clock.Elapsed.TotalMilliseconds);
                }
                var times = string.Join(", ", steps.Select(t => t.ToString("0.0") + " ms"));
                Assert.That(menu.MatchCount, Is.EqualTo(2000), "every shape of the five meshes matches \"sha\" (" + times + ")");
                Assert.That(menu.Rows.Count, Is.EqualTo(AdMenu.MaxResults + 1), "and only the first hundred are built");
                Assert.That(steps.Max(), Is.LessThan(150.0), "opening and each typed letter, with the layout: " + times);
            });
        }

        [Test]
        public void ALongListFillsItsRowsAsTheyComeIntoView()
        {
            InWindow(opener =>
            {
                var log = new List<string>();
                var items = Enumerable.Range(0, 800).Select(i =>
                {
                    var text = "Object" + i.ToString("000");
                    return new AdMenu.Item { Text = text, Action = () => log.Add(text) };
                }).ToList();
                var menu = AdMenu.Open(opener.worldBound, opener, items, search: true);
                Frame(menu);
                Assert.That(menu.Rows.Count, Is.EqualTo(800), "every row is there, with its size");
                Assert.That(menu.Rows[0].Q<Label>(className: "ad-menu-label").text, Is.EqualTo("Object000"));
                Assert.That(menu.Rows[700].Q<Label>(className: "ad-menu-label"), Is.Null, "a row far below gets its text only when it comes near");

                Assert.That(menu.Rows[59].Q<Label>(className: "ad-menu-label"), Is.Null, "row 59 lies past the rows filled at once and past the screen below the view");
                for (int i = 0; i < 60; i++) Key(menu, KeyCode.DownArrow);
                Assert.That(menu.SelectedIndex, Is.EqualTo(59), "Down still walks the rows");
                Assert.That(menu.Rows[59].Q<Label>(className: "ad-menu-label")?.text, Is.EqualTo("Object059"), "and the row it reaches has its text");
                Assert.That(FieldHasFocus(menu), Is.True);

                Assert.That(menu.Rows[400].Q<Label>(className: "ad-menu-label"), Is.Null);
                Hover(menu.Rows[400]);
                Assert.That(menu.SelectedIndex, Is.EqualTo(400));
                Assert.That(menu.Rows[400].Q<Label>(className: "ad-menu-label")?.text, Is.EqualTo("Object400"), "a row highlighted without scrolling gets its text too");

                ((ScrollView)menu.Card).scrollOffset = new Vector2(0f, menu.Rows[700].layout.y);
                Frame(menu);
                var row = menu.Rows[700];
                Assert.That(row.Q<Label>(className: "ad-menu-label")?.text, Is.EqualTo("Object700"), "scrolling fills the rows it brings into view");
                Hover(row);
                Assert.That(menu.SelectedIndex, Is.EqualTo(700), "hovering highlights it");
                Click(row);
                Assert.That(log, Is.EqualTo(new[] { "Object700" }), "and a click picks it");
            });
        }

        [Test]
        public void AFlatListOfEightHundredOpensAndSearchesQuickly()
        {
            List<AdMenu.Item> Items() => Enumerable.Range(0, 800).Select(i => new AdMenu.Item
            {
                Text = "Object" + i.ToString("000"),
                Note = "Armature/Hips/Spine/Chest",
                Action = () => { },
            }).ToList();
            InWindow(opener =>
            {
                var warm = AdMenu.Open(opener.worldBound, opener, Items(), search: true);
                LayoutOnce(warm);
                Type(warm, "obj");
                warm.Close(false);

                var clock = Stopwatch.StartNew();
                var menu = AdMenu.Open(opener.worldBound, opener, Items(), search: true);
                LayoutOnce(menu);
                var steps = new List<double> { clock.Elapsed.TotalMilliseconds };
                foreach (var c in "obj")
                {
                    Frame(menu);
                    clock.Restart();
                    TypeKeyNow(menu, c);
                    LayoutOnce(menu);
                    steps.Add(clock.Elapsed.TotalMilliseconds);
                }
                var times = string.Join(", ", steps.Select(t => t.ToString("0.0") + " ms"));
                Assert.That(menu.MatchCount, Is.EqualTo(800), times);
                Assert.That(steps.Max(), Is.LessThan(150.0), "opening 800 rows and each typed letter, with the layout: " + times);
            });
        }

        [Test]
        public void TheSearchMenuFitsAtTheInspectorWidthsInEveryLanguage()
        {
            foreach (var language in new[] { "en-US", "ja-JP", "zh-Hans" })
            {
                using (L.UseTestLanguage(language, null))
                {
                    foreach (var width in new[] { 300f, 360f })
                    {
                        InWindow(opener =>
                        {
                            var items = Enumerable.Range(0, 3).Select(g => new AdMenu.Item
                            {
                                Text = "Body_Mesh_" + g,
                                Children = Enumerable.Range(0, 60).Select(i => new AdMenu.Item { Text = "Item_" + g + "_" + i + "_MouthSmileWideOpen", Note = "Armature/Hips/Spine/Chest/Neck/Head", Action = () => { } }).ToList(),
                            }).ToList();
                            var menu = AdMenu.Open(opener.worldBound, opener, items, search: true);
                            string where = language + " at " + width + " px";
                            LayoutProbe.Validate(menu);
                            AssertFits(menu, where + ", browsing");
                            var placeholder = menu.SearchField.Q<Label>(className: "ad-search-placeholder");
                            AssertTextFits(placeholder, where + ", the placeholder");

                            Type(menu, "item");
                            LayoutProbe.Validate(menu);
                            AssertFits(menu, where + ", with results and the count row");

                            Key(menu, KeyCode.Escape);
                            Type(menu, "zzzz");
                            LayoutProbe.Validate(menu);
                            AssertFits(menu, where + ", with nothing matching");
                        }, width, 600f);
                    }
                }
            }
        }

        [Test]
        public void TheInactiveMarkShowsButIsNotSearched()
        {
            var ring = new GameObject("Ring");
            ring.transform.SetParent(avatar.Root.transform);
            ring.SetActive(false);
            foreach (var (language, word) in new[] { ("en-US", "inact"), ("ja-JP", "非アクティブ") })
            {
                using (L.UseTestLanguage(language, null))
                {
                    InWindow(opener =>
                    {
                        UnityEngine.Object picked = null;
                        var menu = AvatarObjectPicker.Open(opener.worldBound, opener, avatar.Root.transform, PickKind.GameObject, o => picked = o);
                        var marked = "Ring " + L.Text("ad.ui.pick.inactive");
                        Assert.That(Texts(menu), Does.Contain(marked), language + ": the switched off object still shows its mark");
                        Type(menu, word);
                        Assert.That(menu.MatchCount, Is.Zero, language + ": typing the mark's own word \"" + word + "\" finds no object by it");
                        Key(menu, KeyCode.Escape);
                        Type(menu, "rin");
                        Assert.That(Texts(menu), Is.EqualTo(new[] { marked }), language + ": the object's own name still finds it, mark and all");
                        Key(menu, KeyCode.Return);
                        Assert.That(picked, Is.SameAs(ring), language + ": and picks it");
                    });
                }
            }
        }

        [Test]
        public void ThePlaceholderHidesWhileAnInputMethodComposes()
        {
            var real = AdMenu.CompositionText;
            string composition = "";
            AdMenu.CompositionText = () => composition;
            try
            {
                InWindow(opener =>
                {
                    var log = new List<string>();
                    var menu = AdMenu.Open(opener.worldBound, opener, Grouped(log), search: true);
                    var placeholder = menu.SearchField.Q<Label>(className: "ad-search-placeholder");
                    Assert.That(placeholder.ClassListContains("ad-hidden"), Is.False, "the placeholder shows in an empty field");

                    // A key pressed while an input method composes reaches the field with no character, and the field keeps its value.
                    composition = "う";
                    Key(menu, KeyCode.None);
                    Assert.That(menu.SearchField.value, Is.Empty, "the field holds nothing yet");
                    Assert.That(placeholder.ClassListContains("ad-hidden"), Is.True, "but the placeholder steps aside for the text being composed");
                    Key(menu, KeyCode.Return);
                    Assert.That(menu.IsClosed, Is.False, "Enter while composing belongs to the input method");
                    Assert.That(log, Is.Empty);

                    composition = "";
                    using (var up = KeyUpEvent.GetPooled(new Event { type = EventType.KeyUp, keyCode = KeyCode.Escape }))
                        menu.SendEvent(up);
                    Assert.That(placeholder.ClassListContains("ad-hidden"), Is.False, "once the composition is gone, the empty field shows the placeholder again");

                    Type(menu, "s");
                    Assert.That(placeholder.ClassListContains("ad-hidden"), Is.True, "and typed text still hides it");
                });
            }
            finally { AdMenu.CompositionText = real; }
        }

        [Test]
        public void SearchFoldsKanaAndWidth()
        {
            InWindow(opener =>
            {
                var log = new List<string>();
                var items = new List<AdMenu.Item>
                {
                    Leaf("ウインク", log, "Face"),
                    Leaf("blink", log, "Face"),
                    Leaf("ﾏﾊﾞﾀｷ", log, "Face"),
                    Leaf("眨眼", log, "Face"),
                    Leaf("Smile", log, "Face"),
                };
                var menu = AdMenu.Open(opener.worldBound, opener, items, search: true);
                Type(menu, "ういんく");
                Assert.That(Texts(menu), Is.EqualTo(new[] { "ウインク" }), "hiragana finds a name in katakana");
                Key(menu, KeyCode.Escape);
                Type(menu, "ｂｌｉｎｋ");
                Assert.That(Texts(menu), Is.EqualTo(new[] { "blink" }), "full width letters find a name in plain letters");
                Key(menu, KeyCode.Escape);
                Type(menu, "ＢＬＩＮＫ");
                Assert.That(Texts(menu), Is.EqualTo(new[] { "blink" }), "whatever their case");
                Key(menu, KeyCode.Escape);
                Type(menu, "ｳｲﾝｸ");
                Assert.That(Texts(menu), Is.EqualTo(new[] { "ウインク" }), "half width katakana finds full width katakana");
                Key(menu, KeyCode.Escape);
                Type(menu, "まばたき");
                Assert.That(Texts(menu), Is.EqualTo(new[] { "ﾏﾊﾞﾀｷ" }), "and a name in half width katakana is found too");
                Key(menu, KeyCode.Escape);
                Type(menu, "眨眼");
                Assert.That(Texts(menu), Is.EqualTo(new[] { "眨眼" }), "a Chinese name is still found by its own characters");
                Key(menu, KeyCode.Return);
                Assert.That(log, Is.EqualTo(new[] { "Face/眨眼" }));
            });
        }

        [Test]
        public void AnObjectsNameShowsWholeBesideALongPath()
        {
            var parent = avatar.Root.transform;
            foreach (var name in new[] { "Armature", "J_Bip_C_Hips", "J_Bip_C_Spine", "J_Bip_C_Chest", "J_Bip_C_UpperChest" })
            {
                var bone = new GameObject(name);
                bone.transform.SetParent(parent);
                parent = bone.transform;
            }
            new GameObject("LeftShoulder").transform.SetParent(parent);
            foreach (var width in new[] { 300f, 360f })
            {
                InWindow(opener =>
                {
                    var menu = AvatarObjectPicker.Open(opener.worldBound, opener, avatar.Root.transform, PickKind.GameObject, _ => { });
                    LayoutProbe.Validate(menu);
                    int index = Texts(menu).IndexOf("LeftShoulder");
                    Assert.That(index, Is.GreaterThanOrEqualTo(0));
                    AssertNameWins(menu.Rows[index], "browsing at " + width + " px");
                    Type(menu, "leftshoulder");
                    LayoutProbe.Validate(menu);
                    AssertNameWins(menu.Rows[0], "a result at " + width + " px");
                }, width);
            }
        }

        [Test]
        public void AShortNameLeavesALongPropertyNameWhole()
        {
            foreach (var width in new[] { 300f, 360f })
            {
                InWindow(opener =>
                {
                    var items = new List<AdMenu.Item> { new AdMenu.Item { Text = "Metallic", SearchText = "_MetallicGlossMap", Note = "_MetallicGlossMap", Action = () => { } } };
                    var menu = AdMenu.Open(opener.worldBound, opener, items, search: true);
                    Type(menu, "metal");
                    LayoutProbe.Validate(menu);
                    var row = menu.Rows[0];
                    var note = row.Q<Label>(className: "ad-menu-note");
                    string where = "at " + width + " px";
                    Assert.That(TextWidth(note), Is.InRange(85f, 125f), where + ": the note measures about 100 px, the case the earlier fix was for");
                    Assert.That(ShownWidth(note), Is.GreaterThanOrEqualTo(TextWidth(note) - 0.5f), where + ": the property name shows whole beside a short display name");
                    Assert.That(ShownWidth(row.Q<Label>(className: "ad-menu-label")), Is.GreaterThanOrEqualTo(TextWidth(row.Q<Label>(className: "ad-menu-label")) - 0.5f), where + ": and so does the display name");
                }, width);
            }
        }

        [Test]
        public void ALongNameTakesAboutSixtyPercentAndThePathTheRest()
        {
            foreach (var width in new[] { 300f, 360f })
            {
                InWindow(opener =>
                {
                    var items = new List<AdMenu.Item>
                    {
                        new AdMenu.Item { Text = "VeryLongBlendShapeName_ForTheLeftEyeWideOpen", Note = "Armature/J_Bip_C_Hips/J_Bip_C_Spine/J_Bip_C_Chest", Action = () => { } },
                    };
                    var menu = AdMenu.Open(opener.worldBound, opener, items, search: true);
                    Type(menu, "very");
                    LayoutProbe.Validate(menu);
                    var row = menu.Rows[0];
                    var label = row.Q<Label>(className: "ad-menu-label");
                    var note = row.Q<Label>(className: "ad-menu-note");
                    float content = row.contentRect.width;
                    string where = "at " + width + " px, a row of " + content.ToString("0.0") + " px";
                    Assert.That(ShownWidth(label), Is.LessThan(TextWidth(label)), where + ": a name too long for the row ends in an ellipsis");
                    Assert.That(label.worldBound.width / content, Is.InRange(0.5f, 0.62f), where + ": at about 60% of the row (" + label.worldBound.width.ToString("0.0") + " px)");
                    Assert.That(note.worldBound.width / content, Is.GreaterThan(0.25f), where + ": the path keeps the rest (" + note.worldBound.width.ToString("0.0") + " px)");
                    Assert.That(note.worldBound.x, Is.GreaterThanOrEqualTo(label.worldBound.xMax - 0.5f), where + ": after the name");
                    Assert.That(note.worldBound.xMax, Is.LessThanOrEqualTo(row.LocalToWorld(row.contentRect).xMax + 0.5f), where + ": inside the row");
                }, width);
            }
        }

        /// <summary>The name beside a long path shows whole, and the path fills what is left of the row after it.</summary>
        static void AssertNameWins(VisualElement row, string where)
        {
            var label = row.Q<Label>(className: "ad-menu-label");
            var note = row.Q<Label>(className: "ad-menu-note");
            Assert.That(note, Is.Not.Null, where + ": the row shows the path");
            Assert.That(ShownWidth(label), Is.GreaterThanOrEqualTo(TextWidth(label) - 0.5f),
                where + ": the name '" + label.text + "' shows whole (" + ShownWidth(label).ToString("0.0") + " px for text that measures " + TextWidth(label).ToString("0.0") + ")");
            Assert.That(note.worldBound.x, Is.GreaterThanOrEqualTo(label.worldBound.xMax - 0.5f), where + ": the path starts after the name");
            float rowEnd = row.LocalToWorld(row.contentRect).xMax;
            Assert.That(note.worldBound.xMax, Is.LessThanOrEqualTo(rowEnd + 0.5f), where + ": and ends inside the row");
            Assert.That(note.worldBound.xMax, Is.GreaterThanOrEqualTo(rowEnd - 0.5f), where + ": at the row's end, taking what is left");
            Assert.That(ShownWidth(note), Is.LessThan(TextWidth(note)), where + ": a path longer than that is cut");
        }

        static float ShownWidth(Label label) => label.contentRect.width;

        static float TextWidth(Label label) =>
            label.MeasureTextSize(label.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).x;

        static void AssertFits(AdMenu menu, string where)
        {
            var root = menu.parent.worldBound;
            var frame = menu.Frame.worldBound;
            Assert.That(frame.x, Is.GreaterThanOrEqualTo(root.x - 0.5f), where + ": the card starts inside the window");
            Assert.That(frame.xMax, Is.LessThanOrEqualTo(root.xMax + 0.5f), where + ": the card ends inside the window");
            var field = menu.SearchField.worldBound;
            Assert.That(field.x >= frame.x - 0.5f && field.xMax <= frame.xMax + 0.5f, Is.True, where + ": the field is inside the card");
            Assert.That(field.width, Is.GreaterThan(100f), where + ": the field keeps a usable width");
            var list = menu.Card.worldBound;
            foreach (var row in menu.Rows)
            {
                var box = row.worldBound;
                Assert.That(box.x >= list.x - 0.5f && box.xMax <= list.xMax + 0.5f, Is.True, where + ": a row stays inside the card");
                foreach (var label in row.Query<Label>().ToList())
                {
                    if (string.IsNullOrEmpty(label.text) || label.resolvedStyle.display == DisplayStyle.None) continue;
                    Assert.That(label.worldBound.xMax, Is.LessThanOrEqualTo(box.xMax + 0.5f), where + ": '" + label.text + "' stays inside its row");
                }
                if (row.ClassListContains("ad-menu-info")) AssertTextFits(row.Q<Label>(className: "ad-menu-label"), where);
            }
        }

        /// <summary>A label whose whole text is shown: it wraps or fits, never cut off.</summary>
        static void AssertTextFits(Label label, string where)
        {
            var content = label.contentRect;
            var needed = label.MeasureTextSize(label.text, content.width, VisualElement.MeasureMode.Exactly, 0f, VisualElement.MeasureMode.Undefined);
            Assert.That(needed.y, Is.LessThanOrEqualTo(content.height + 1f), where + ": '" + label.text + "' is shown whole");
        }
    }
}
#endif
