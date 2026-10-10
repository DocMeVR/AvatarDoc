#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
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

            public static Item Line() => new Item { Separator = true };
            public static Item Heading(string text) => new Item { Text = text, Header = true };
        }

        /// <summary>How a menu closed: an item was chosen, the user closed it (Escape, Tab, a click beside it, another menu opening, code), it closed by itself, or its view left the panel.</summary>
        enum CloseKind { Chosen, User, ByItself, ViewGone }

        public const float RowHeight = 22f;
        public const float MaxWidth = 320f;
        public const int MaxRows = 12;
        /// <summary>How many times within one second the menu takes the focus back from something inside its own window before it gives up and closes.</summary>
        public const int MaxRefocusesPerSecond = 3;
        const float SeparatorHeight = 9f;
        const float HeaderHeight = 20f;
        const float DescribedRowHeight = 34f;
        const float CardFrame = 10f;
        const float EdgeGap = 4f;
        const double TypeResetSeconds = 1.0;

        /// <summary>The clock type to search and the focus check read, replaceable by a test so it need not wait a second.</summary>
        public static Func<double> Clock = () => EditorApplication.timeSinceStartup;

        /// <summary>Whether the editor is the application in front. Replaceable by a test, which cannot choose that.</summary>
        public static Func<bool> EditorActive = () => InternalEditorUtility.isApplicationActive;

        /// <summary>Whether the editor is in front and the window hosting a menu has the keyboard focus. Replaceable by a test, since the window a test opens rarely has it.</summary>
        public static Func<EditorWindow, bool> HostHasFocus = host => host != null && EditorActive() && EditorWindow.focusedWindow == host;

        /// <summary>The window under the mouse pointer, replaceable by a test.</summary>
        public static Func<EditorWindow> PointerWindow = () => EditorWindow.mouseOverWindow;

        /// <summary>Where the line about a menu that closed by itself goes. Replaceable by a test that reads it.</summary>
        public static Action<string> Log = message => Debug.Log(message);

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
        readonly Rect anchor;
        readonly bool keepOpenOnTick;
        readonly Func<IEnumerable<Item>> refresh;
        Item[] items;
        int selected = -1;
        bool closed, holding, focusCheckPending;
        string typed = "";
        double typedAt;

        public bool IsClosed => closed;
        public int SelectedIndex => selected;
        public IReadOnlyList<VisualElement> Rows => rows;
        public IReadOnlyList<Item> Items => items;
        public VisualElement Card => card;
        public int Depth => parents.Count;
        /// <summary>The AvatarDoc view this menu belongs to, or null for a menu opened outside one.</summary>
        public AvatarDoctorView Owner => owner;
        /// <summary>Why the menu closed by itself. Null while it is open, and after a close the user chose.</summary>
        public string CloseReason { get; private set; }

        /// <summary>
        /// Opens the menu under anchorWorld (flipping above when it would leave the window, shifting left at the
        /// right edge), inside the window whose panel hosts target. With keepOpenOnTick, choosing an item runs
        /// its action and keeps the menu open, rebuilding the rows from refresh (or flipping the item's tick when
        /// there is none) unless the item sets Closes. Returns null when target is not on a panel.
        /// </summary>
        public static AdMenu Open(Rect anchorWorld, VisualElement target, IEnumerable<Item> items, bool keepOpenOnTick = false, Func<IEnumerable<Item>> refresh = null)
        {
            if (target == null || target.panel == null || items == null) return null;
            var window = Resources.FindObjectsOfTypeAll<EditorWindow>()
                .FirstOrDefault(w => w != null && w.rootVisualElement.panel == target.panel && w.rootVisualElement.Contains(target));
            var panelRoot = window != null ? window.rootVisualElement : target.panel.visualTree;
            var owner = target as AvatarDoctorView ?? target.GetFirstAncestorOfType<AvatarDoctorView>();
            // Held before the old menus close, so swapping one menu for another never lets a refresh through.
            owner?.HoldForMenu();
            foreach (var old in panelRoot.Query<AdMenu>().ToList()) old.Close(false);
            var menu = new AdMenu(anchorWorld, target, owner, window, items.ToArray(), panelRoot, keepOpenOnTick, refresh);
            menu.holding = owner != null;
            panelRoot.Add(menu);
            menu.watched.RegisterCallback<DetachFromPanelEvent>(menu.OnWatchedDetached);
            Undo.undoRedoPerformed += menu.OnUndoRedo;
            menu.Populate(false);
            menu.Focus();
            return menu;
        }

        AdMenu(Rect anchor, VisualElement target, AvatarDoctorView owner, EditorWindow host, Item[] items, VisualElement panelRoot, bool keepOpenOnTick, Func<IEnumerable<Item>> refresh)
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
            focusable = true;
            tabIndex = -1;

            card = new ScrollView { name = "ad-menu-card" };
            card.AddToClassList("ad-menu-card");
            card.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            card.verticalScrollerVisibility = ScrollerVisibility.Auto;
            card.focusable = false;
            card.contentContainer.focusable = false;
            card.style.minWidth = Mathf.Min(Mathf.Max(anchor.width, 0f), MaxWidth);
            card.style.maxHeight = CardLimit(panelRoot.layout.height);
            Add(card);

            RegisterCallback<PointerDownEvent>(e => { if (e.target == this) Close(); e.StopPropagation(); });
            RegisterCallback<WheelEvent>(e => e.StopPropagation());
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<FocusOutEvent>(OnFocusOut);
            card.RegisterCallback<GeometryChangedEvent>(e => { if (e.oldRect.size != e.newRect.size) Place(); });
            panelRoot.RegisterCallback<GeometryChangedEvent>(OnPanelGeometry);
            RegisterCallback<DetachFromPanelEvent>(OnOwnDetached);
        }

        static bool Enabled(Item item) => !item.Separator && !item.Header && !item.Disabled && (item.Action != null || item.Children != null);

        static float HeightOf(Item item) => item.Separator ? SeparatorHeight : item.Header ? HeaderHeight : string.IsNullOrEmpty(item.Description) ? RowHeight : DescribedRowHeight;

        void Populate(bool keepSelection)
        {
            int keep = keepSelection ? selected : -1;
            var scroll = card.scrollOffset;
            card.Clear();
            rows.Clear();
            selected = -1;
            typed = "";
            if (parents.Count > 0)
            {
                var back = new VisualElement { name = "ad-menu-back" };
                back.AddToClassList("ad-menu-item");
                back.AddToClassList("ad-menu-back");
                back.Add(Cell("ad-menu-check", ""));
                back.Add(Cell("ad-menu-label", "‹ " + L.Text("ad.ui.menu.back")));
                back.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0) { Back(); e.StopPropagation(); } });
                card.Add(back);
            }
            for (int i = 0; i < items.Length; i++)
            {
                var row = BuildRow(items[i], i);
                rows.Add(row);
                card.Add(row);
            }
            if (keep >= 0 && keep < items.Length && Enabled(items[keep])) Select(keep, false);
            card.scrollOffset = scroll;
            Place();
            Focus();
        }

        VisualElement BuildRow(Item item, int index)
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
            row.Add(Cell("ad-menu-check", item.Checked ? "✓" : ""));
            var column = new VisualElement();
            column.AddToClassList("ad-menu-column");
            column.Add(Cell("ad-menu-label", item.Text));
            if (!string.IsNullOrEmpty(item.Description)) column.Add(Cell("ad-menu-description", item.Description));
            row.Add(column);
            if (!string.IsNullOrEmpty(item.Shortcut)) row.Add(Cell("ad-menu-shortcut", item.Shortcut));
            if (item.Children != null) row.Add(Cell("ad-menu-arrow", "›"));
            row.RegisterCallback<PointerMoveEvent>(e => Select(index, false));
            row.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button != 0 || !Enabled(item)) return;
                e.StopPropagation();
                Commit(index);
            });
            return row;
        }

        static Label Cell(string cls, string text)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(cls);
            return label;
        }

        /// <summary>The tallest the card grows before it scrolls: the first MaxRows rows, never more than the window.</summary>
        float CardLimit(float rootHeight)
        {
            float rows = items.Take(MaxRows).Sum(HeightOf) + CardFrame + (parents.Count > 0 ? RowHeight : 0f);
            return Mathf.Max(RowHeight, rootHeight > 0f ? Mathf.Min(rows, rootHeight - 2f * EdgeGap) : rows);
        }

        void Place()
        {
            if (closed) return;
            float rootWidth = panelRoot.layout.width;
            float rootHeight = panelRoot.layout.height;
            float estimate = items.Sum(HeightOf) + CardFrame + (parents.Count > 0 ? RowHeight : 0f);
            float width = card.layout.width > 0f ? card.layout.width : Mathf.Max(card.resolvedStyle.minWidth.value, 0f);
            float height = card.layout.height > 0f ? card.layout.height : Mathf.Min(estimate, CardLimit(rootHeight));
            var local = panelRoot.WorldToLocal(anchor.position);
            if (rootWidth > 0f && rootHeight > 0f)
            {
                card.style.maxHeight = CardLimit(rootHeight);
                float below = rootHeight - (local.y + anchor.height) - EdgeGap;
                float above = local.y - EdgeGap;
                bool putBelow = below >= height || below >= above;
                float top = putBelow ? local.y + anchor.height : local.y - height;
                card.style.top = Mathf.Clamp(top, EdgeGap, Mathf.Max(EdgeGap, rootHeight - height - EdgeGap));
                card.style.left = Mathf.Clamp(local.x, EdgeGap, Mathf.Max(EdgeGap, rootWidth - width - EdgeGap));
            }
            else
            {
                card.style.top = local.y + anchor.height;
                card.style.left = local.x;
            }
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
            Focus();
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
                case KeyCode.KeypadEnter: Commit(selected); break;
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
                if (parents.Count == 0) items = refresh().ToArray();
            }
            else item.Checked = !item.Checked;
            Populate(true);
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
