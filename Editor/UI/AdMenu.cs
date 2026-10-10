#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The one popup menu every AvatarDoc dropdown and right click opens, drawn in UI Toolkit inside the window
    /// that hosts the control instead of as Unity's grey system list. Modelled on ThryEditor's RetainedMenu.
    /// While it is open it holds its AvatarDoc view's refreshes, so nothing rebuilds the rows it was opened on.
    /// A menu opened with search has a text field at the top that filters every group at once (the pickers).
    /// </summary>
    public sealed class AdMenu : VisualElement
    {
        public sealed class Item
        {
            public string Text;
            public string Description;
            public string Shortcut;
            public bool Checked;
            public bool Disabled;
            public bool Header;
            public bool Separator;
            /// <summary>Under keepOpenOnTick, an item that still closes the menu when chosen (a Clear item).</summary>
            public bool Closes;
            public Action Action;
            public List<Item> Children;
            /// <summary>Text a search also finds the item by, besides its own: a shader property's name, an object's path.</summary>
            public string SearchText;
            /// <summary>The name a search matches in place of Text, when Text carries more than the name: an object's name without its "(inactive)" mark.</summary>
            public string SearchName;
            /// <summary>Dim text after the item's own on the same row. A search result without one shows its group there instead; an empty one shows nothing.</summary>
            public string Note;
            /// <summary>A line that says something about the results (nothing matches, how many more): never chosen, and wrapped rather than cut off.</summary>
            internal bool Info;

            public static Item Line() => new Item { Separator = true };
            public static Item Heading(string text) => new Item { Text = text, Header = true };
        }

        /// <summary>How a menu closed: an item was chosen, the user closed it (Escape, Tab, a click beside it, another menu opening, code), it closed by itself, or its view left the panel.</summary>
        enum CloseKind { Chosen, User, ByItself, ViewGone }

        /// <summary>One item a search can find, folded once when the menu opens.</summary>
        readonly struct Entry
        {
            public readonly Item Item;
            public readonly string Name;
            public readonly string Extra;
            public readonly string Group;

            public Entry(Item item, string group)
            {
                Item = item;
                Name = Fold(item.SearchName ?? item.Text);
                Extra = string.IsNullOrEmpty(item.SearchText) ? null : Fold(item.SearchText);
                Group = group;
            }
        }

        /// <summary>
        /// The form a search compares: full width letters and digits as plain ones, half width katakana as full width
        /// (NFKC), lower case, and hiragana as katakana, so ういんく finds ウインク and ｂｌｉｎｋ finds blink. Han
        /// characters stay as they are.
        /// </summary>
        static string Fold(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string normal;
            try { normal = text.Normalize(NormalizationForm.FormKC); }
            catch (ArgumentException) { normal = text; }
            var chars = normal.ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (chars[i] >= (char)0x3041 && chars[i] <= (char)0x3096) chars[i] = (char)(chars[i] + 0x60);
            return new string(chars);
        }

        public const float RowHeight = 22f;
        public const float MaxWidth = 320f;
        public const int MaxRows = 12;
        /// <summary>The most result rows a search builds; one more row says how many others match.</summary>
        public const int MaxResults = 100;
        /// <summary>The width of a menu with search unless its control is wider: fixed, so the card never moves while the results change.</summary>
        public const float SearchWidth = 260f;
        /// <summary>How many times within one second the menu takes the focus back from something inside its own window before it gives up and closes.</summary>
        public const int MaxRefocusesPerSecond = 3;
        const float SeparatorHeight = 9f;
        const float HeaderHeight = 20f;
        const float DescribedRowHeight = 34f;
        const float CardFrame = 10f;
        /// <summary>The search field and the gap below it.</summary>
        const float SearchHeight = 26f;
        const float EdgeGap = 4f;
        const double TypeResetSeconds = 1.0;
        const string GroupJoin = " › ";
        /// <summary>How many rows a long list of a search menu fills at once; the rest get their text as they scroll near.</summary>
        const int FilledAtOnce = 2 * MaxRows;
        /// <summary>How far above and below the visible rows a long list fills rows ahead of the scroll.</summary>
        const float FillAhead = MaxRows * RowHeight;

        /// <summary>The clock type to search and the focus check read, replaceable by a test so it need not wait a second.</summary>
        public static Func<double> Clock = () => EditorApplication.timeSinceStartup;

        /// <summary>Whether the editor is the application in front. Replaceable by a test, which cannot choose that.</summary>
        public static Func<bool> EditorActive = () => InternalEditorUtility.isApplicationActive;

        /// <summary>Whether the editor is in front and the window hosting a menu has the keyboard focus. Replaceable by a test, since the window a test opens rarely has it.</summary>
        public static Func<EditorWindow, bool> HostHasFocus = host => host != null && EditorActive() && EditorWindow.focusedWindow == host;

        /// <summary>The window under the mouse pointer, replaceable by a test.</summary>
        public static Func<EditorWindow> PointerWindow = () => EditorWindow.mouseOverWindow;

        /// <summary>The window with the keyboard focus, where FindTarget looks first. Replaceable by a test.</summary>
        public static Func<EditorWindow> FocusedWindow = () => EditorWindow.focusedWindow;

        /// <summary>Where the line about a menu that closed by itself goes. Replaceable by a test that reads it.</summary>
        public static Action<string> Log = message => Debug.Log(message);

        /// <summary>The text an input method is composing (Japanese, Chinese), not yet in the field. Replaceable by a test, which cannot drive an input method.</summary>
        public static Func<string> CompositionText = () => Input.compositionString;

        readonly List<VisualElement> rows = new List<VisualElement>();
        readonly Stack<Item[]> parents = new Stack<Item[]>();
        readonly List<double> refocuses = new List<double>();
        readonly VisualElement target;
        readonly AvatarDoctorView owner;
        /// <summary>The element whose removal closes the menu: the owning view, or the target when it has none. Never a tree row, since the tree recycles its rows on every rebuild.</summary>
        readonly VisualElement watched;
        readonly EditorWindow host;
        readonly VisualElement panelRoot;
        readonly ScrollView card;
        /// <summary>The box placed under the control: the card itself, or with search the box holding the field above the card.</summary>
        readonly VisualElement frame;
        readonly TextField searchField;
        readonly float searchWidth;
        readonly float listHeight;
        readonly Rect anchor;
        readonly bool keepOpenOnTick;
        readonly Func<IEnumerable<Item>> refresh;
        Item[] items;
        /// <summary>While the search field has text, the level the menu was browsing, shown again once the field is empty.</summary>
        Item[] browseItems;
        /// <summary>While the search field has text, the dim text of each result row.</summary>
        string[] notes;
        Entry[] index = Array.Empty<Entry>();
        /// <summary>For a long list of a search menu, which rows have their text yet, and the note each one shows; null otherwise.</summary>
        bool[] rowFilled;
        string[] rowNotes;
        int selected = -1;
        /// <summary>Whether the pointer went down inside the card: a release on a row picks it only then, never the release of the press that opened the menu.</summary>
        bool pressedInside;
        bool closed, holding, focusCheckPending;
        string typed = "";
        double typedAt;

        public bool IsClosed => closed;
        public int SelectedIndex => selected;
        public IReadOnlyList<VisualElement> Rows => rows;
        public IReadOnlyList<Item> Items => items;
        public VisualElement Card => card;
        /// <summary>The control the menu was opened on: it gets the focus back when the menu closes, and its removal closes a menu outside an AvatarDoc view.</summary>
        public VisualElement Target => target;
        /// <summary>The box placed under the control: the card, or with search the box holding the field and the card.</summary>
        public VisualElement Frame => frame;
        /// <summary>The search field, or null for a menu opened without search.</summary>
        public TextField SearchField => searchField;
        /// <summary>How many items the text in the search field matches, shown or not.</summary>
        public int MatchCount { get; private set; }
        public int Depth => parents.Count;
        /// <summary>The AvatarDoc view this menu belongs to, or null for a menu opened outside one.</summary>
        public AvatarDoctorView Owner => owner;
        /// <summary>Why the menu closed by itself. Null while it is open, and after a close the user chose.</summary>
        public string CloseReason { get; private set; }

        /// <summary>
        /// Opens the menu under anchorWorld (flipping above when it would leave the window, shifting left at the
        /// right edge), inside the window whose panel hosts target. With keepOpenOnTick, choosing an item runs
        /// its action and keeps the menu open, rebuilding the rows from refresh (or flipping the item's tick when
        /// there is none) unless the item sets Closes. With search, a field at the top takes the keyboard focus and
        /// filters every group at once. Returns null when target is not on a panel.
        /// </summary>
        public static AdMenu Open(Rect anchorWorld, VisualElement target, IEnumerable<Item> items, bool keepOpenOnTick = false, Func<IEnumerable<Item>> refresh = null, bool search = false)
        {
            if (target == null || target.panel == null || items == null) return null;
            var window = Resources.FindObjectsOfTypeAll<EditorWindow>()
                .FirstOrDefault(w => w != null && w.rootVisualElement.panel == target.panel && w.rootVisualElement.Contains(target));
            var panelRoot = window != null ? window.rootVisualElement : target.panel.visualTree;
            var owner = target as AvatarDoctorView ?? target.GetFirstAncestorOfType<AvatarDoctorView>();
            // Held before the old menus close, so swapping one menu for another never lets a refresh through.
            owner?.HoldForMenu();
            foreach (var old in panelRoot.Query<AdMenu>().ToList()) old.Close(false);
            var menu = new AdMenu(anchorWorld, target, owner, window, items.ToArray(), panelRoot, keepOpenOnTick, refresh, search);
            menu.holding = owner != null;
            panelRoot.Add(menu);
            menu.watched.RegisterCallback<DetachFromPanelEvent>(menu.OnWatchedDetached);
            Undo.undoRedoPerformed += menu.OnUndoRedo;
            menu.Populate(false);
            menu.FocusInput();
            return menu;
        }

        /// <summary>
        /// The control a picker was opened from when an outside caller hands over only its world rectangle: the
        /// element with exactly that rectangle under its centre in the window with the keyboard focus, else in the
        /// window under the pointer; failing that, whatever the focused window, else the pointed at one, shows there.
        /// No other window is looked in. Null for an empty or unmeasured rectangle, or when neither window has
        /// anything there. AvatarDoc's own controls hand over themselves instead.
        /// </summary>
        public static VisualElement FindTarget(Rect anchorWorld)
        {
            if (!(anchorWorld.width > 0f) || !(anchorWorld.height > 0f) || float.IsNaN(anchorWorld.x) || float.IsNaN(anchorWorld.y)
                || float.IsInfinity(anchorWorld.x) || float.IsInfinity(anchorWorld.y) || float.IsInfinity(anchorWorld.width) || float.IsInfinity(anchorWorld.height))
                return null;
            var focused = FocusedWindow();
            var pointed = PointerWindow();
            var point = anchorWorld.center;
            VisualElement fallback = null;
            foreach (var window in new[] { focused, pointed })
            {
                var root = window != null ? window.rootVisualElement : null;
                if (root?.panel == null || !root.worldBound.Contains(point)) continue;
                var picked = root.panel.Pick(point);
                if (picked == null || !root.Contains(picked)) continue;
                // An open menu covers its window; the control under it is not what the creator just pressed.
                if (picked.GetFirstOfType<AdMenu>() != null) picked = root;
                for (var element = picked; element != null; element = element.parent)
                {
                    if (SameRect(element.worldBound, anchorWorld)) return element;
                    if (element == root) break;
                }
                if (fallback == null) fallback = picked;
            }
            return fallback;
        }

        static bool SameRect(Rect a, Rect b) =>
            Mathf.Abs(a.x - b.x) < 0.5f && Mathf.Abs(a.y - b.y) < 0.5f && Mathf.Abs(a.width - b.width) < 0.5f && Mathf.Abs(a.height - b.height) < 0.5f;

        AdMenu(Rect anchor, VisualElement target, AvatarDoctorView owner, EditorWindow host, Item[] items, VisualElement panelRoot, bool keepOpenOnTick, Func<IEnumerable<Item>> refresh, bool search)
        {
            this.anchor = anchor;
            this.target = target;
            this.owner = owner;
            watched = (VisualElement)owner ?? target;
            this.host = host;
            this.items = items;
            this.panelRoot = panelRoot;
            this.keepOpenOnTick = keepOpenOnTick;
            this.refresh = refresh;
            name = "ad-menu";
            AddToClassList("ad-menu");
            AddToClassList(EditorGUIUtility.isProSkin ? "ad-dark" : "ad-light");
            var uss = AvatarDoctorView.LoadStyleSheet();
            if (uss != null) styleSheets.Add(uss);
            // With search the field holds the focus, so the menu itself never takes it.
            focusable = !search;
            tabIndex = -1;

            card = new ScrollView { name = "ad-menu-card" };
            card.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            card.verticalScrollerVisibility = ScrollerVisibility.Auto;
            card.focusable = false;
            card.contentContainer.focusable = false;
            if (search)
            {
                AddToClassList("ad-menu-searchable");
                BuildIndex(items);
                frame = new VisualElement { name = "ad-menu-frame" };
                frame.AddToClassList("ad-menu-card");
                searchField = BuildSearchField();
                frame.Add(searchField);
                card.AddToClassList("ad-menu-list");
                // Tall enough for every result the search can show, so the card keeps one size while typing.
                listHeight = Mathf.Clamp(Mathf.Max(items.Length, index.Length + 1), 1, MaxRows) * RowHeight;
                card.style.height = listHeight;
                frame.Add(card);
                float room = panelRoot.layout.width > 0f ? panelRoot.layout.width - 2f * EdgeGap : MaxWidth;
                searchWidth = Mathf.Max(Mathf.Min(Mathf.Clamp(anchor.width, SearchWidth, MaxWidth), room), RowHeight);
                frame.style.width = searchWidth;
            }
            else
            {
                frame = card;
                card.AddToClassList("ad-menu-card");
                card.style.minWidth = Mathf.Min(Mathf.Max(anchor.width, 0f), MaxWidth);
            }
            frame.style.maxHeight = CardLimit(panelRoot.layout.height);
            Add(frame);

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<WheelEvent>(e => e.StopPropagation());
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            if (search) RegisterCallback<KeyDownEvent>(OnSearchKeyDown, TrickleDown.TrickleDown);
            RegisterCallback<FocusOutEvent>(OnFocusOut);
            frame.RegisterCallback<GeometryChangedEvent>(e => { if (e.oldRect.size != e.newRect.size) Place(); });
            card.verticalScroller.valueChanged += _ => FillNearby();
            card.contentContainer.RegisterCallback<GeometryChangedEvent>(_ => FillNearby());
            panelRoot.RegisterCallback<GeometryChangedEvent>(OnPanelGeometry);
            RegisterCallback<DetachFromPanelEvent>(OnOwnDetached);
            LanguageFont.Apply(this);
        }

        /// <summary>A plain TextField with the tree search's look: the magnifier and the placeholder inside its one input box.</summary>
        TextField BuildSearchField()
        {
            var field = new TextField { name = "ad-menu-search", tooltip = L.Text("ad.ui.pick.search.tip") };
            field.AddToClassList("ad-menu-search");
            // Every key goes to the text, so a focus that comes back never selects what was typed.
            field.selectAllOnFocus = false;
            field.selectAllOnMouseUp = false;
            var input = field.Q(className: "unity-text-field__input") ?? field;
            // The same 20px inset the tree's search field sets from code: room for the magnifier, outside the stylesheet's spacing scale.
            input.style.paddingLeft = 20;
            var icon = new Image { image = MenuTreeView.FirstIcon("Search Icon", "d_Search Icon", "SearchWindow", "d_SearchWindow", "ViewToolZoom", "d_ViewToolZoom"), pickingMode = PickingMode.Ignore };
            icon.AddToClassList("ad-search-icon");
            input.Add(icon);
            var placeholder = new Label(L.Text("ad.ui.search.placeholder")) { pickingMode = PickingMode.Ignore };
            placeholder.AddToClassList("ad-search-placeholder");
            input.Add(placeholder);
            // The placeholder hides while the field holds text or an input method is composing, whichever of the two
            // carries the composed text. Every key pressed or let go while it composes reaches the field, so each one looks again.
            void ShowPlaceholder() => placeholder.EnableInClassList("ad-hidden", field.value.Length > 0 || !string.IsNullOrEmpty(CompositionText()));
            field.RegisterCallback<KeyDownEvent>(_ => ShowPlaceholder());
            field.RegisterCallback<KeyUpEvent>(_ => ShowPlaceholder());
            field.RegisterValueChangedCallback(e =>
            {
                // The text is the menu's own business: nothing in the window it sits in hears it change.
                e.StopPropagation();
                ShowPlaceholder();
                Filter(e.newValue, false);
            });
            return field;
        }

        static bool Enabled(Item item) => !item.Separator && !item.Header && !item.Disabled && (item.Action != null || item.Children != null);

        static float HeightOf(Item item) => item.Separator ? SeparatorHeight : item.Header ? HeaderHeight : string.IsNullOrEmpty(item.Description) ? RowHeight : DescribedRowHeight;

        /// <summary>Every item the search can pick, through every group, with the group it sits in: the submenus above it and the header over it.</summary>
        void BuildIndex(Item[] root)
        {
            var entries = new List<Entry>();
            AddEntries(entries, root, null);
            index = entries.ToArray();
        }

        static void AddEntries(List<Entry> entries, IEnumerable<Item> level, string group)
        {
            string heading = null;
            foreach (var item in level)
            {
                if (item == null) continue;
                if (item.Separator) { heading = null; continue; }
                if (item.Header) { heading = item.Text; continue; }
                var own = JoinGroup(group, heading);
                if (item.Children != null) AddEntries(entries, item.Children, JoinGroup(own, item.Text));
                else if (Enabled(item)) entries.Add(new Entry(item, own));
            }
        }

        static string JoinGroup(string outer, string inner) =>
            string.IsNullOrEmpty(outer) ? inner : string.IsNullOrEmpty(inner) ? outer : outer + GroupJoin + inner;

        /// <summary>
        /// Shows every item whose name or search text holds the typed text, whatever the case, across every group in
        /// the groups' order, up to MaxResults rows and one row saying how many more. An empty field shows the level
        /// the menu was browsing.
        /// </summary>
        void Filter(string text, bool keepSelection)
        {
            if (closed) return;
            var query = (text ?? "").Trim();
            if (query.Length == 0)
            {
                if (browseItems == null) return;
                items = browseItems;
                browseItems = null;
                notes = null;
                MatchCount = 0;
                Populate(keepSelection, !keepSelection);
                return;
            }
            if (browseItems == null) browseItems = items;
            var needle = Fold(query);
            var shown = new List<Item>();
            var shownNotes = new List<string>();
            int count = 0;
            foreach (var entry in index)
            {
                if (entry.Name.IndexOf(needle, StringComparison.Ordinal) < 0 && (entry.Extra == null || entry.Extra.IndexOf(needle, StringComparison.Ordinal) < 0)) continue;
                if (++count > MaxResults) continue;
                shown.Add(entry.Item);
                shownNotes.Add(entry.Item.Note ?? entry.Group);
            }
            MatchCount = count;
            if (count == 0) shown.Add(new Item { Text = string.Format(L.Text("ad.ui.pick.no_match"), query), Disabled = true, Info = true });
            else if (count > MaxResults) shown.Add(new Item { Text = UiText.Count(count - MaxResults, "ad.ui.pick.more.one", "ad.ui.pick.more"), Disabled = true, Info = true });
            if (shownNotes.Count < shown.Count) shownNotes.Add(null);
            items = shown.ToArray();
            notes = shownNotes.ToArray();
            Populate(keepSelection, !keepSelection);
        }

        void Populate(bool keepSelection, bool scrollToTop = false)
        {
            int keep = keepSelection ? selected : -1;
            var scroll = scrollToTop ? Vector2.zero : card.scrollOffset;
            card.Clear();
            rows.Clear();
            selected = -1;
            typed = "";
            if (parents.Count > 0 && browseItems == null)
            {
                var back = new VisualElement { name = "ad-menu-back" };
                back.AddToClassList("ad-menu-item");
                back.AddToClassList("ad-menu-back");
                back.Add(Cell("ad-menu-check", ""));
                back.Add(Cell("ad-menu-label", "‹ " + L.Text("ad.ui.menu.back")));
                back.RegisterCallback<PointerUpEvent>(e =>
                {
                    if (e.button != 0 || !TakePress()) return;
                    e.StopPropagation();
                    Back();
                });
                card.Add(back);
            }
            // A picker can browse a list of hundreds (every object of an avatar, every shape of one mesh): each row is
            // built at once with its size, but gets its text only when it scrolls near.
            bool lazy = searchField != null && browseItems == null && items.Length > FilledAtOnce;
            rowFilled = lazy ? new bool[items.Length] : null;
            rowNotes = lazy ? new string[items.Length] : null;
            for (int i = 0; i < items.Length; i++)
            {
                var note = notes != null ? notes[i] : items[i].Note;
                bool fill = !lazy || i < FilledAtOnce;
                var row = BuildRow(items[i], i, note, fill);
                if (lazy)
                {
                    rowFilled[i] = fill;
                    rowNotes[i] = note;
                }
                rows.Add(row);
                card.Add(row);
            }
            if (keep >= 0 && keep < items.Length && Enabled(items[keep])) Select(keep, false);
            card.scrollOffset = scroll;
            Place();
            FocusInput();
        }

        /// <summary>One row. Without fill it has its size and its actions but no text yet, until FillRow.</summary>
        VisualElement BuildRow(Item item, int index, string note, bool fill)
        {
            var row = new VisualElement();
            if (item.Separator) { row.AddToClassList("ad-menu-separator"); return row; }
            if (item.Header)
            {
                row.AddToClassList("ad-menu-header");
                row.Add(Cell("ad-menu-header-text", (item.Text ?? "").ToUpperInvariant()));
                return row;
            }
            row.AddToClassList("ad-menu-item");
            row.EnableInClassList("ad-menu-disabled", !Enabled(item));
            row.EnableInClassList("ad-menu-ticked", item.Checked);
            row.EnableInClassList("ad-menu-info", item.Info);
            if (fill) AddCells(row, item, note);
            else row.style.height = HeightOf(item);
            // A row of a search menu keeps to one line, so a long name ends in an ellipsis and shows whole here.
            if (searchField != null) row.tooltip = string.IsNullOrEmpty(note) ? item.Text : L.Aside(item.Text, note);
            row.RegisterCallback<PointerMoveEvent>(e => Select(index, false));
            row.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button != 0 || !Enabled(item) || !TakePress()) return;
                e.StopPropagation();
                Commit(index);
            });
            return row;
        }

        static void AddCells(VisualElement row, Item item, string note)
        {
            // A row with a note keeps the item's own name whole up to the stylesheet's share of the row, and the note takes the rest.
            row.EnableInClassList("ad-menu-noted", !string.IsNullOrEmpty(note));
            row.Add(Cell("ad-menu-check", item.Checked ? "✓" : ""));
            var column = new VisualElement();
            column.AddToClassList("ad-menu-column");
            column.Add(Cell("ad-menu-label", item.Text));
            if (!string.IsNullOrEmpty(item.Description)) column.Add(Cell("ad-menu-description", item.Description));
            row.Add(column);
            if (!string.IsNullOrEmpty(note)) row.Add(Cell("ad-menu-note", note));
            if (!string.IsNullOrEmpty(item.Shortcut)) row.Add(Cell("ad-menu-shortcut", item.Shortcut));
            if (item.Children != null) row.Add(Cell("ad-menu-arrow", "›"));
        }

        /// <summary>Gives the row at index its text, if it has none yet.</summary>
        void FillRow(int index)
        {
            if (rowFilled == null || index < 0 || index >= rowFilled.Length || rowFilled[index]) return;
            rowFilled[index] = true;
            var row = rows[index];
            row.style.height = StyleKeyword.Null;
            AddCells(row, items[index], rowNotes[index]);
        }

        /// <summary>Fills the rows in view and a screen of them above and below, as the list scrolls.</summary>
        void FillNearby()
        {
            if (rowFilled == null || closed) return;
            float top = card.scrollOffset.y - FillAhead;
            float bottom = card.scrollOffset.y + Mathf.Max(card.contentViewport.layout.height, listHeight) + FillAhead;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rowFilled[i]) continue;
                var box = rows[i].layout;
                if (float.IsNaN(box.y)) continue;
                if (box.yMax >= top && box.y <= bottom) FillRow(i);
            }
        }

        /// <summary>Whether the pointer being released went down inside the card; one press counts for one release.</summary>
        bool TakePress()
        {
            bool pressed = pressedInside;
            pressedInside = false;
            return pressed;
        }

        static Label Cell(string cls, string text)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(cls);
            return label;
        }

        /// <summary>The tallest the card grows before it scrolls: the first MaxRows rows (with search, the field and the list), never more than the window.</summary>
        float CardLimit(float rootHeight)
        {
            float wanted = searchField != null
                ? CardFrame + SearchHeight + listHeight
                : items.Take(MaxRows).Sum(HeightOf) + CardFrame + (parents.Count > 0 ? RowHeight : 0f);
            return Mathf.Max(RowHeight, rootHeight > 0f ? Mathf.Min(wanted, rootHeight - 2f * EdgeGap) : wanted);
        }

        void Place()
        {
            if (closed) return;
            float rootWidth = panelRoot.layout.width;
            float rootHeight = panelRoot.layout.height;
            float estimate = searchField != null
                ? CardFrame + SearchHeight + listHeight
                : items.Sum(HeightOf) + CardFrame + (parents.Count > 0 ? RowHeight : 0f);
            float width = frame.layout.width > 0f ? frame.layout.width : searchField != null ? searchWidth : Mathf.Max(card.resolvedStyle.minWidth.value, 0f);
            float height = frame.layout.height > 0f ? frame.layout.height : Mathf.Min(estimate, CardLimit(rootHeight));
            var local = panelRoot.WorldToLocal(anchor.position);
            if (rootWidth > 0f && rootHeight > 0f)
            {
                frame.style.maxHeight = CardLimit(rootHeight);
                float below = rootHeight - (local.y + anchor.height) - EdgeGap;
                float above = local.y - EdgeGap;
                bool putBelow = below >= height || below >= above;
                float top = putBelow ? local.y + anchor.height : local.y - height;
                frame.style.top = Mathf.Clamp(top, EdgeGap, Mathf.Max(EdgeGap, rootHeight - height - EdgeGap));
                frame.style.left = Mathf.Clamp(local.x, EdgeGap, Mathf.Max(EdgeGap, rootWidth - width - EdgeGap));
            }
            else
            {
                frame.style.top = local.y + anchor.height;
                frame.style.left = local.x;
            }
        }

        /// <summary>Gives the keyboard focus to the search field, or without search to the menu itself; a field that has it already keeps its caret.</summary>
        void FocusInput()
        {
            if (searchField == null)
            {
                Focus();
                return;
            }
            // The focus controller names the field itself while its inner text element has the focus.
            var focused = focusController?.focusedElement as VisualElement;
            if (focused != searchField && (focused == null || !searchField.Contains(focused))) searchField.Focus();
        }

        /// <summary>A press beside the card closes the menu. With search, a press on a row leaves the focus in the field, so typing goes on.</summary>
        void OnPointerDown(PointerDownEvent e)
        {
            e.StopPropagation();
            if (e.target == this)
            {
                Close();
                return;
            }
            pressedInside = true;
            if (searchField != null && !(e.target is VisualElement pressed && searchField.Contains(pressed))) e.PreventDefault();
        }

        void OnWatchedDetached(DetachFromPanelEvent e)
        {
            if (e.target != watched) return;
            // A click on another window can rebuild the Inspector (a new selection): that close is the creator's own.
            bool creatorStayed = HostHasFocus(host) || (host != null && PointerWindow() == host);
            Close(false, CloseKind.ViewGone, L.Text("ad.log.menu_closed.view_gone"), creatorStayed);
        }

        /// <summary>A window takes its menus away when its tab is switched, or when it closes or maximizes: the creator's own doing, so no console line.</summary>
        void OnOwnDetached(DetachFromPanelEvent e)
        {
            if (e.target != this) return;
            panelRoot.UnregisterCallback<GeometryChangedEvent>(OnPanelGeometry);
            if (!closed) CloseByItself(L.Text("ad.log.menu_closed.removed"), false);
        }

        /// <summary>While the menu's shade covers the window, only a dragged splitter or window frame changes its size: the creator's own doing, so no console line.</summary>
        void OnPanelGeometry(GeometryChangedEvent e)
        {
            if (e.oldRect.width > 0f && e.oldRect.height > 0f && e.oldRect.size != e.newRect.size) CloseByItself(L.Text("ad.log.menu_closed.resized"), false);
        }

        /// <summary>Some items keep a position in a list, which an undo can shift under them, so an undo or redo closes the menu.</summary>
        void OnUndoRedo() => CloseByItself(L.Text("ad.log.menu_closed.undo"));

        /// <summary>
        /// A focus move out of the menu is not acted on at once: Unity moves the focus for reasons of its own (an
        /// IMGUI editor in the same inspector syncing its keyboard focus, a window shown by something else), so the
        /// next tick looks at where the focus went.
        /// </summary>
        void OnFocusOut(FocusOutEvent e)
        {
            if (closed) return;
            var next = e.relatedTarget as VisualElement;
            if (next != null && Contains(next)) return;
            if (focusCheckPending) return;
            focusCheckPending = true;
            schedule.Execute(CheckFocus);
        }

        /// <summary>
        /// What the tick after the focus left the menu does. Nothing when the focus is back. When the editor and the
        /// host window still have the focus, something inside the window took it, so the menu takes it back, at
        /// most MaxRefocusesPerSecond times in one second, then closes naming what keeps taking it. When another
        /// window or application has the focus, the menu closes. Public so a test can run the tick.
        /// </summary>
        public void CheckFocus()
        {
            focusCheckPending = false;
            if (closed || panel == null) return;
            var focused = focusController?.focusedElement as VisualElement;
            if (focused == this || (focused != null && Contains(focused))) return;
            if (!HostHasFocus(host))
            {
                // A click on another window moves the pointer there, and Alt+Tab puts another application in
                // front; a window that takes the focus while the editor is in front and the pointer still rests on
                // this one is something the creator did not do, and is worth a line.
                bool pointerStayed = host != null && EditorActive() && PointerWindow() == host;
                CloseByItself(string.Format(L.Text("ad.log.menu_closed.window_focus"), FocusedWindowName()), pointerStayed);
                return;
            }
            double now = Clock();
            refocuses.RemoveAll(t => now - t >= 1.0);
            if (refocuses.Count >= MaxRefocusesPerSecond)
            {
                CloseByItself(string.Format(L.Text("ad.log.menu_closed.focus_taken"), Describe(focused)));
                return;
            }
            refocuses.Add(now);
            FocusInput();
        }

        static string FocusedWindowName()
        {
            if (!EditorActive()) return L.Text("ad.log.menu_closed.other_app");
            var window = EditorWindow.focusedWindow;
            return window != null ? "'" + window.titleContent.text + "'" : L.Text("ad.log.menu_closed.nothing");
        }

        static string Describe(VisualElement element)
        {
            if (element == null) return L.Text("ad.log.menu_closed.nothing");
            return string.IsNullOrEmpty(element.name) ? element.GetType().Name : element.GetType().Name + " '" + element.name + "'";
        }

        void OnKeyDown(KeyDownEvent e)
        {
            // A search menu takes its keys before the field sees them, in OnSearchKeyDown.
            if (searchField != null) return;
            switch (e.keyCode)
            {
                case KeyCode.DownArrow: Move(1); break;
                case KeyCode.UpArrow: Move(-1); break;
                case KeyCode.Home: SelectEdge(true); break;
                case KeyCode.End: SelectEdge(false); break;
                case KeyCode.RightArrow:
                    if (selected >= 0 && items[selected].Children != null) Commit(selected);
                    break;
                case KeyCode.LeftArrow:
                case KeyCode.Backspace: Back(); break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    Commit(selected);
                    if (closed) DropEnterCharacter();
                    break;
                case KeyCode.Escape: Close(); break;
                case KeyCode.Tab: Close(false); break;
                default:
                    if (e.ctrlKey || e.commandKey || e.altKey || char.IsControl(e.character) || e.character == '\0') return;
                    FindTyped(e.character);
                    break;
            }
            e.PreventDefault();
            e.StopPropagation();
        }

        /// <summary>
        /// The keys a search menu answers before its field sees them: Down and Up move through the rows, Enter picks
        /// the highlighted row or the first result, Escape clears the field or closes an empty one, Tab closes. While
        /// the field is empty, Right opens a highlighted group and Left or Backspace go back up, as without search.
        /// Every other key types into the field, and so does every key while an input method is composing.
        /// </summary>
        void OnSearchKeyDown(KeyDownEvent e)
        {
            if (closed || !string.IsNullOrEmpty(CompositionText())) return;
            bool empty = searchField.value.Length == 0;
            switch (e.keyCode)
            {
                case KeyCode.DownArrow: Move(1); break;
                case KeyCode.UpArrow: Move(-1); break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    Commit(selected < 0 && browseItems != null ? Array.FindIndex(items, Enabled) : selected);
                    if (closed) DropEnterCharacter();
                    break;
                case KeyCode.Escape:
                    if (empty) Close();
                    else searchField.value = "";
                    break;
                case KeyCode.Tab: Close(false); break;
                case KeyCode.RightArrow when empty && selected >= 0 && items[selected].Children != null:
                    Commit(selected);
                    break;
                case KeyCode.LeftArrow when empty && parents.Count > 0:
                case KeyCode.Backspace when empty && parents.Count > 0:
                    Back();
                    break;
                case KeyCode.None when e.character == '\n' || e.character == '\r' || e.character == '\t' || e.character == (char)27:
                    // Windows follows Enter, Tab and Escape with their character: the key itself has been answered.
                    break;
                case KeyCode.None when e.character == ' ' && empty:
                    // The space Windows sends after Space opened the picker on an object field; a leading space finds nothing anyway.
                    break;
                default:
                    return;
            }
            e.PreventDefault();
            e.StopPropagation();
        }

        /// <summary>
        /// Windows follows Enter with its character as a key press of its own, which would press the control the focus
        /// went back to and open its menu again. The next key press in the window is dropped when it is that character.
        /// </summary>
        void DropEnterCharacter()
        {
            var root = panelRoot;
            EventCallback<KeyDownEvent> drop = null;
            drop = e =>
            {
                root.UnregisterCallback(drop, TrickleDown.TrickleDown);
                if (e.keyCode != KeyCode.None || (e.character != '\n' && e.character != '\r')) return;
                // A used key press is one Unity sends no submit for, and the submit is what presses a focused button.
                e.imguiEvent?.Use();
                e.StopImmediatePropagation();
                e.PreventDefault();
            };
            root.RegisterCallback(drop, TrickleDown.TrickleDown);
        }

        void Move(int direction)
        {
            int index = selected;
            if (index < 0 && direction < 0) index = items.Length;
            do index += direction; while (index >= 0 && index < items.Length && !Enabled(items[index]));
            if (index >= 0 && index < items.Length) Select(index, true);
        }

        void SelectEdge(bool first)
        {
            int index = first ? Array.FindIndex(items, Enabled) : Array.FindLastIndex(items, Enabled);
            if (index >= 0) Select(index, true);
        }

        void FindTyped(char character)
        {
            double now = Clock();
            typed = now - typedAt > TypeResetSeconds ? character.ToString() : typed + character;
            typedAt = now;
            int index = Array.FindIndex(items, item => Enabled(item) && (item.Text ?? "").StartsWith(typed, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) Select(index, true);
        }

        void Select(int index, bool reveal)
        {
            if (index < 0 || index >= items.Length || !Enabled(items[index]) || selected == index) return;
            FillRow(index);
            if (selected >= 0 && selected < rows.Count) rows[selected].RemoveFromClassList("ad-menu-selected");
            selected = index;
            rows[index].AddToClassList("ad-menu-selected");
            if (reveal) card.ScrollTo(rows[index]);
        }

        /// <summary>Runs or opens the row at index as a click on it would. Public so a test can choose a row without a pointer.</summary>
        public void Commit(int index)
        {
            if (closed || index < 0 || index >= items.Length || !Enabled(items[index])) return;
            var item = items[index];
            if (item.Children != null)
            {
                parents.Push(items);
                items = item.Children.ToArray();
                Populate(false);
                return;
            }
            if (keepOpenOnTick && !item.Closes)
            {
                RunAndKeepOpen(item);
                return;
            }
            // Closing lets a refresh held while the menu was open run first, so the item acts on current rows.
            Close(true, CloseKind.Chosen, null, false);
            item.Action?.Invoke();
        }

        /// <summary>The Filter's own ticks refresh the view on purpose, so the hold is let go around the item's action alone.</summary>
        void RunAndKeepOpen(Item item)
        {
            owner?.SuspendMenuHold();
            try { item.Action?.Invoke(); }
            finally { owner?.ResumeMenuHold(); }
            if (closed) return;
            if (refresh != null)
            {
                if (parents.Count == 0)
                {
                    var root = refresh().ToArray();
                    if (browseItems != null) browseItems = root;
                    else items = root;
                    if (searchField != null) BuildIndex(root);
                }
            }
            else item.Checked = !item.Checked;
            if (browseItems != null) Filter(searchField.value, true);
            else Populate(true);
        }

        void Back()
        {
            if (parents.Count == 0) return;
            items = parents.Pop();
            Populate(false);
        }

        /// <summary>Closes the menu as a deliberate close: nothing is logged.</summary>
        public void Close(bool focusTarget = true) => Close(focusTarget, CloseKind.User, null, false);

        void CloseByItself(string reason, bool log = true) => Close(false, CloseKind.ByItself, reason, log);

        void Close(bool focusTarget, CloseKind kind, string reason, bool log)
        {
            if (closed) return;
            closed = true;
            CloseReason = reason;
            watched.UnregisterCallback<DetachFromPanelEvent>(OnWatchedDetached);
            panelRoot.UnregisterCallback<GeometryChangedEvent>(OnPanelGeometry);
            Undo.undoRedoPerformed -= OnUndoRedo;
            // A window clearing its root lets go of its child list before it detaches the children one by one, so
            // a menu closed while its view detaches is no longer in that list; the same Clear detaches it next.
            if (hierarchy.parent != null && hierarchy.parent.hierarchy.IndexOf(this) >= 0) RemoveFromHierarchy();
            if (focusTarget && target.panel != null) target.Focus();
            if (log && reason != null) Log(string.Format(L.Text("ad.log.menu_closed"), reason, AvatarPlanCache.LastInvalidationReason));
            if (!holding) return;
            holding = false;
            if (kind == CloseKind.ViewGone) owner.DropMenuHold();
            else owner.ReleaseMenuHold(kind == CloseKind.Chosen, kind == CloseKind.ByItself);
        }
    }
}
#endif
