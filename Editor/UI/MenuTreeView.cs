#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>The merged menu as a tree with a toolbar. Selection drives the detail panel.</summary>
    public sealed class MenuTreeView : VisualElement, IPlanRefreshable
    {
        readonly AvatarDoctorView owner;
        public readonly TreeView Tree = new TreeView();
        readonly Label othersNote = new Label();
        readonly Label captureNote = new Label();
        readonly Label searchNote = new Label { name = "ad-search-note" };
        readonly Label dropHint = new Label();
        readonly Label dragTarget = new Label { name = "ad-drag-target" };
        readonly VisualElement dropRefusal = new VisualElement { name = "ad-drop-refusal" };
        /// <summary>
        /// Every toolbar dropdown ("+ More", Filter) is a plain ToolbarButton that opens an AdMenu under itself, so
        /// its box is pixel identical to every other ToolbarButton beside it.
        /// </summary>
        readonly Button wheelToggle;
        readonly Button tagFilterButton;
        /// <summary>The tag filter button's own visible caption, a plain child rather than the button's own text: a ToolbarButton draws its text across the whole content box regardless of any appended child, which drew the dropdown arrow over the last letter until this moved the caption into the same row of children the icon and the arrow already are.</summary>
        readonly Label tagFilterLabel = new Label { pickingMode = PickingMode.Ignore };
        List<AdMenu.Item> tagFilterItems = new List<AdMenu.Item>();
        readonly List<AdMenu.Item> moreItems = new List<AdMenu.Item>();
        readonly VisualElement filterChips = new VisualElement { name = "ad-filter-chips" };
        readonly VisualElement welcomeCard = new VisualElement { name = "ad-welcome-card" };
        Toolbar actionsBar;
        Button moreButton, captureButton, convertButton;
        /// <summary>The adds Simple keeps, by button name. Every other add and the right hand group of buttons are tools, and are left out of Simple.</summary>
        static readonly HashSet<string> SimpleAddNames = new HashSet<string> { "ad-add-toggle", "ad-add-radial", "ad-add-submenu", "ad-outfit-open" };
        readonly List<ToolbarButton> addButtons = new List<ToolbarButton>();
        readonly List<AdMenu.Item> addItems = new List<AdMenu.Item>();
        readonly Dictionary<VisualElement, float> footprints = new Dictionary<VisualElement, float>();
        float overflowWidth = float.NaN;
        float overflowFontSize = float.NaN;
        bool compactRight;
        const float CompactBarWidth = 520f;
        const float CompactIconSize = 12f;
        const float CompactIconPadding = 2f;
        const float TightPadding = 4f;
        const float TightArrowMargin = 2f;
        int overflowAddCount = 3;
        readonly List<VisualElement> lockDisabled = new List<VisualElement>();
        Dictionary<int, TreeRow> rows = new Dictionary<int, TreeRow>();
        /// <summary>Every row of the last Build call, before any search or tag pruning: what the tag filter menu offers is read from this, not from the possibly-pruned rows dictionary, so the menu's own choices never shift while a search or tag narrows what is visible.</summary>
        List<TreeRow> allRows = new List<TreeRow>();
        /// <summary>Tag ids currently ticked in the filter menu. Remembered per component for the session only (TagFilterMemory), never in the scene.</summary>
        readonly HashSet<string> activeTags;
        readonly string tagFilterKey;
        /// <summary>The import scan behind the welcome card's Convert start, run once and kept until the avatar itself changes, so an empty tree does not rescan on every refresh.</summary>
        Transform importScanRoot;
        List<ImportCandidate> importScanCache;
        /// <summary>The avatar's own plan version (AvatarPlanCache.VersionFor) at the last scan: a VRCFury or Modular Avatar component added, removed or changed under the avatar drops that avatar's plan and moves it, while an edit on another avatar or a transform dragged under this one does not.</summary>
        int importScanPlanVersion = -1;

        /// <summary>How often the welcome card scanned the avatar for something to convert. Read by the tests.</summary>
        public int WelcomeScans { get; private set; }
        /// <summary>
        /// Below this row width, the row's own least essential chips (source, group, platform when it is not a
        /// warning, the trigger chip) drop before the row's name ever has to shrink past a readable minimum.
        /// Chosen from the brief's about 300 px inspector floor, with margin for the tree's own border and the
        /// row's left padding.
        /// </summary>
        public const float NarrowRowWidth = 360f;
        /// <summary>Overrides the row width IsNarrow reads, so a test can drive the narrow layout without a real window ever laying the tree out. Null uses the tree's own measured width; an unmeasured (zero) width is never treated as narrow, so a detached test with no override sees the normal, wide layout.</summary>
        public float? RowWidthOverride { get; set; }
        bool narrowLast;
        bool suppressSelection;
        /// <summary>Set while Refresh is changing expansion state, so a poll that reaches the tree in the middle of a rebuild is ignored.</summary>
        bool restoring;
        string filter = "";
        /// <summary>Entry the next Refresh selects, set by an edit that just created it.</summary>
        string pendingSelectionId;
        /// <summary>The key of the row that was selected when a search or tag filter hid it, kept to select that row again when the filter lets it back. Null when no row is waiting.</summary>
        string hiddenSelectionKey;
        /// <summary>The row this component's root entries sit under, worked out when the rows are built: a drop on it lands at the component's root. Null when they sit at the avatar's root menu.</summary>
        TreeRow componentRoot;
        /// <summary>The (row, parent, index) structure read back right after the last rebuild, used to tell which rows a drag actually moved.</summary>
        List<(TreeRow row, TreeRow parent, int index)> lastStructure = new List<(TreeRow, TreeRow, int)>();
        /// <summary>Keys of the rows the creator has expanded for this component, loaded from EditorPrefs and kept current as rows expand or collapse.</summary>
        readonly HashSet<string> expanded;
        /// <summary>The EditorPrefs key the expansion set lives under, worked out once for this component.</summary>
        readonly string expansionKey;

        public TreeRow Selected { get; private set; }
        public event Action<TreeRow> SelectionChanged;
        public IReadOnlyCollection<string> ExpandedKeys => expanded;

        /// <summary>
        /// True from the moment a row drag genuinely captures the tree's pointer until it releases, whichever way
        /// that ends (a drop, a cancel, or focus moving elsewhere). The owning view holds refreshes back while this
        /// is true, since a rebuild landing mid-drag drops the drag. This Unity version raises DragUpdated,
        /// DragPerform and DragExited for an in-window reorder too (an editor-hosted TreeView treats even its own
        /// reorder as an editor drag), but PointerCaptureEvent and PointerCaptureOutEvent are the pair Unity's own
        /// ListViewDragger always fires around a genuine drag regardless of that, so those are the ones this tracks.
        /// Publicly settable so a test can hold a refresh back without a real mouse, since a detached tree never
        /// receives pointer capture events.
        /// </summary>
        public bool Dragging
        {
            get => dragging;
            set
            {
                if (value == dragging) return;
                dragging = value;
                if (value) DraggingSince = EditorApplication.timeSinceStartup;
            }
        }
        bool dragging;

        /// <summary>When the current drag started, by EditorApplication.timeSinceStartup. Publicly settable so a test can back-date it to simulate a stuck drag without actually waiting for one.</summary>
        public double DraggingSince { get; set; }

        /// <summary>
        /// True, and clears Dragging itself, when a drag has held it for at least secondsStuck while the tree no
        /// longer actually has the pointer's capture: PointerCaptureEvent and PointerCaptureOutEvent normally pair
        /// up around every drag, and the only way they would not is something swallowing the pointer-up mid-drag,
        /// a modal dialog opening for instance. A drag that still holds real capture is left alone however long
        /// it runs, since the creator might simply be holding the mouse still.
        /// </summary>
        public bool ClearIfStuck(double secondsStuck)
        {
            if (!Dragging || Tree.HasMouseCapture()) return false;
            if (EditorApplication.timeSinceStartup - DraggingSince < secondsStuck) return false;
            Dragging = false;
            ClearDragPreview();
            return true;
        }

        /// <summary>
        /// The tree row nearest the pointer during a drag, tracked from the tree's own pointer move events beside
        /// Dragging: used with DropDepth both to preview the landing target and, from Unity Hierarchy's own gesture,
        /// to bound how deep the pointer's horizontal position can ask for. Null while not dragging, or while the
        /// pointer is over empty space below the last row, which means the root menu. Publicly settable so a test
        /// can drive the preview and the walk to a new depth without a real mouse.
        /// </summary>
        public TreeRow DragHoverRow
        {
            get => dragHoverRow;
            set { dragHoverRow = value; UpdateDragLabel(); }
        }
        TreeRow dragHoverRow;

        /// <summary>
        /// The tree depth the current drag has asked for, from the pointer's horizontal position: 0 is the root
        /// menu, and each indent step further right asks for one level deeper, Unity Hierarchy style. Null when no
        /// drag is tracking a depth. Kept current from the tree's own pointer move events while Dragging is true,
        /// and read once, then cleared, by the drop that follows. Publicly settable so a test can drive a drop to a
        /// chosen depth without a real mouse.
        /// </summary>
        public int? DropDepth
        {
            get => dropDepth;
            set { dropDepth = value; UpdateDragLabel(); }
        }
        int? dropDepth;

        /// <summary>
        /// The tree depth a pointer's horizontal position asks for: at rowContentLeft or further left, depth 0 (the
        /// root menu); each indentWidth further right asks for one level deeper, clamped to maxDepth, the deepest
        /// the current drop position allows. A pure function of its four inputs so it is testable without a real
        /// mouse; the view itself only ever calls this one method to turn a pointer position into a depth.
        /// </summary>
        public static int DepthFromPointer(float pointerX, float rowContentLeft, float indentWidth, int maxDepth)
        {
            if (indentWidth <= 0f) return maxDepth;
            int depth = Mathf.RoundToInt((pointerX - rowContentLeft) / indentWidth);
            return Mathf.Clamp(depth, 0, maxDepth);
        }

        /// <summary>
        /// Walks from start up toward the root until placing the dragged row under the result would put it exactly
        /// at dropDepth, or reaches the root (null) for a dropDepth of 0. Only ever climbs toward the root: a
        /// dropDepth deeper than start already allows leaves start unchanged, since dragging corrects a drop
        /// shallower than Unity's own reorder decided, never invents nesting that decision did not already offer.
        /// </summary>
        static TreeRow WalkUpToDepth(TreeRow start, int dropDepth)
        {
            var candidate = start;
            while (candidate != null && candidate.Depth + 1 > dropDepth)
                candidate = candidate.ParentRow;
            return candidate;
        }

        /// <summary>
        /// Per-depth width of a row's indent guide, in pixels, read once from a real bound row's own indent element
        /// (Unity's "unity-tree-view__item-indent", whose resolved width is the row's depth times this) instead of
        /// guessed, so a skin or Unity version change is picked up on its own rather than silently pointing the drag
        /// label at the wrong level. 15 is only the value used before any row has been measured, and happens to be
        /// exactly what this Unity version (2022.3.22f1) measures too.
        /// </summary>
        public static float IndentWidth => measuredIndentWidth ?? 15f;
        static float? measuredIndentWidth;

        /// <summary>The pixel x, in the tree's own local space, where a depth-0 row's own content begins, read the same way as IndentWidth and for the same reason.</summary>
        public static float RowContentLeft => measuredRowContentLeft ?? 18f;
        static float? measuredRowContentLeft;

        /// <summary>
        /// A measured pixel value is only trusted when it is finite and inside the range a real row layout could
        /// plausibly produce. Guards a read caught mid-layout (a pooled row still carrying its previous binding's
        /// geometry, or a frame between two layout passes) from overwriting a value that already looks right, while
        /// still letting a value that turns out wrong be replaced the next time a row's geometry actually settles.
        /// </summary>
        static bool Plausible(float value) => value >= 0f && value < 300f;

        /// <summary>
        /// Reads IndentWidth and RowContentLeft off a row's own indent element once its geometry has actually
        /// settled for its CURRENT binding (called from a GeometryChangedEvent registered once per pooled row in
        /// MakeRow, never from bindItem itself: bindItem runs before Unity has re-laid the row out for whatever it
        /// was just bound to, so a value read there can still belong to the row's previous binding). Unity wraps
        /// every row's own content (what MakeRow returns) in, from the outside in, an "unity-tree-view__item"
        /// holding an "unity-tree-view__item-indent" (width zero at depth 0, then one indentWidth per depth after),
        /// a foldout arrow, then an "unity-tree-view__item-content" holding this row's own content directly. Never
        /// locks onto its first sample: every plausible reading updates the cached value, so a wrong early read (or
        /// a skin or font scale change mid-session) self-heals instead of sticking for the rest of the session.
        /// </summary>
        static void MeasureIndent(TreeView tree, VisualElement rowContent, int depth)
        {
            var content = rowContent.parent;
            if (content == null || content.resolvedStyle.width <= 0f) return;
            if (depth == 0)
            {
                // Read in tree-local space, the same conversion HitTestRow already uses for y: rowContent's own
                // resolvedStyle.left is relative to its own parent, not the tree, so a horizontal scroll or a focus
                // border between the two would otherwise bias every depth this measurement feeds.
                float left = tree.WorldToLocal(rowContent.worldBound.position).x;
                if (Plausible(left)) measuredRowContentLeft = left;
            }
            else
            {
                var indent = content.parent?.Q<VisualElement>(className: "unity-tree-view__item-indent");
                if (indent != null && indent.resolvedStyle.width > 0f)
                {
                    float width = indent.resolvedStyle.width / depth;
                    if (Plausible(width)) measuredIndentWidth = width;
                }
            }
        }

        /// <summary>Registered once per pooled row in MakeRow: fires once that row's layout actually reflects whatever it is currently bound to, which is the only moment its geometry is safe to measure from.</summary>
        void OnRowGeometryChanged(GeometryChangedEvent evt)
        {
            if (!((evt.target as VisualElement)?.userData is TreeRow row)) return;
            MeasureIndent(Tree, (VisualElement)evt.target, row.Depth);
        }

        /// <summary>Finds the row at localY (tree-local space) by arithmetic on the fixed row height and the tree's own scroll offset, then looks it up through the tree's own index to id mapping, rather than querying every currently bound row's world bounds. Null over empty space (below the last row, past the last flattened item, or the tree's own scrollbar), which the drag preview and depth clamp both read as the root menu; also null for an index the tree's own mapping refuses, which reads the same way rather than throwing mid-drag.</summary>
        TreeRow HitTestRow(float localY)
        {
            if (Tree.fixedItemHeight <= 0) return null;
            float scrollY = Tree.Q<ScrollView>()?.scrollOffset.y ?? 0f;
            int index = Mathf.FloorToInt((localY + scrollY) / Tree.fixedItemHeight);
            if (index < 0) return null;
            int id;
            try { id = Tree.GetIdForIndex(index); }
            catch (ArgumentOutOfRangeException) { return null; }
            return id >= 0 && rows.TryGetValue(id, out var row) ? row : null;
        }

        void OnTreePointerMove(PointerMoveEvent evt)
        {
            if (!Dragging) return;
            var hovered = HitTestRow(evt.localPosition.y);
            int maxDepth = hovered != null ? hovered.Depth + 1 : 0;
            // Set both tracked fields directly (bypassing the DragHoverRow/DropDepth property setters, which each
            // update the label on their own) so a single pointer move updates the label once, not twice.
            dragHoverRow = hovered;
            dropDepth = DepthFromPointer(evt.localPosition.x, RowContentLeft, IndentWidth, maxDepth);
            UpdateDragLabel();
        }

        /// <summary>
        /// What dropping onto row, walked toward the root by depth levels first when depth is given, actually
        /// resolves to: the id of the submenu that will hold the dropped row as a child, the id of the parent it
        /// will land beside (an own toggle, radial, trigger, override or preset cannot hold children, so it lands
        /// where that row already sits), or a refusal naming why, for a row this tree has no menu position to put
        /// anything beside at all (an existing or foreign row, or a synthetic group). A More page stands for the menu
        /// it continues, so a drop on one lands in that menu. The one place
        /// this decision is made: both the drag label and ApplyStructure call it for the same landing row, so they
        /// cannot answer it two different ways again. Depth null skips the walk and classifies row itself, the way
        /// ApplyStructure's own per-row loop uses it once Unity has already picked a final parent for a row this
        /// drag did not itself correct. The row that holds this component's root entries (an avatar submenu its
        /// install path points at, or the submenu As submenu makes) stands for the component's root, the same as
        /// the avatar's root menu does.
        /// </summary>
        static DropResolution ResolveDropTarget(TreeRow row, int? depth, TreeRow root)
        {
            var target = ContainerOf(depth.HasValue ? WalkUpToDepth(row, depth.Value) : row);
            if (target == null || target == root) return DropResolution.Into("");
            if (target.IsMenuEntry && target.Entry.kind == EntryKind.Submenu) return DropResolution.Into(target.Entry.id);
            if (target.IsOwnEntry) return DropResolution.Into(target.Entry.parentId ?? "");
            return DropResolution.Refuse("ad.info.drop_refused.not_a_container");
        }

        /// <summary>The row this component's root entries sit under: the parent of any entry of its own that has no parent entry. Null when they sit at the avatar's root menu, or when there is none.</summary>
        static TreeRow FindComponentRoot(IEnumerable<TreeRow> candidates)
        {
            foreach (var row in candidates)
                if (row.IsMenuEntry && string.IsNullOrEmpty(row.Entry.parentId)) return ContainerOf(row.ParentRow);
            return null;
        }

        /// <summary>The parent id a drop resolves to, or the reason it refuses instead. See ResolveDropTarget.</summary>
        readonly struct DropResolution
        {
            public readonly bool Refused;
            public readonly string ReasonKey;
            public readonly string ParentId;
            DropResolution(bool refused, string reasonKey, string parentId) { Refused = refused; ReasonKey = reasonKey; ParentId = parentId; }
            public static DropResolution Into(string parentId) => new DropResolution(false, null, parentId);
            public static DropResolution Refuse(string reasonKey) => new DropResolution(true, reasonKey, null);
        }

        /// <summary>Sets the one-line preview of where the current drag will land, using the names the creator sees, or that it will be refused and why; cleared (both fields null) when the drag ends, whether by a drop or a cancel. Never rebuilds the plan or the tree: only this label's own text changes.</summary>
        void UpdateDragLabel()
        {
            if (!dropDepth.HasValue) { dragTarget.text = ""; return; }
            var resolved = ResolveDropTarget(dragHoverRow, dropDepth, componentRoot);
            if (resolved.Refused)
            {
                dragTarget.text = string.Format(L.Text("ad.info.drop_refused"), L.Text(resolved.ReasonKey));
                return;
            }
            dragTarget.text = resolved.ParentId.Length == 0
                ? RootLabel()
                : string.Format(L.Text("ad.ui.drag_target.into"), RichName.Strip(RowFor(resolved.ParentId)?.Name ?? ""));
        }

        /// <summary>Where a drop that lands at the component's root goes: into the row the component installs into, or the avatar's root menu when it installs there.</summary>
        string RootLabel()
        {
            return componentRoot != null
                ? string.Format(L.Text("ad.ui.drag_target.into"), RichName.Strip(componentRoot.Name ?? ""))
                : L.Text("ad.ui.drag_target.root");
        }

        /// <summary>Clears the tracked hover row and depth, and with them the preview label, without touching anything else. Called wherever a drag genuinely ends, and defensively at the start of every drop entry point so a cancelled drag never leaves a stale depth for a later, unrelated drop to pick up.</summary>
        void ClearDragPreview()
        {
            DropDepth = null;
            DragHoverRow = null;
        }

        /// <summary>Case-insensitive name filter. A row stays when it or any descendant matches; the tree is fully expanded while filtering and drag reorder is off.</summary>
        public string Filter
        {
            get => filter;
            set
            {
                filter = value ?? "";
                Refresh(owner.Plan);
            }
        }

        public MenuTreeView(AvatarDoctorView owner)
        {
            this.owner = owner;
            expansionKey = TreeExpansion.KeyFor(owner.Menu);
            expanded = TreeExpansion.Load(owner.ViewPrefs, expansionKey);
            tagFilterKey = expansionKey + ":tags";
            activeTags = TagFilterMemory.Load(tagFilterKey);
            AddToClassList("ad-panel");

            var title = new VisualElement();
            title.AddToClassList("ad-panel-title");
            title.Add(new Label(L.Text("ad.ui.menu")));
            var spacer = new VisualElement();
            spacer.AddToClassList("ad-spacer");
            title.Add(spacer);
            othersNote.AddToClassList("ad-note");
            title.Add(othersNote);
            captureNote.name = "ad-capture-note";
            captureNote.AddToClassList("ad-note");
            title.Add(captureNote);
            Add(title);

            // Two Unity toolbars, not one row of plain buttons: the actions bar (the adds, "+ More", then Capture
            // icons, Convert and the Wheel toggle) above the finding bar (search and the tag filter), each the right
            // built in control for its own job rather than a button standing in for a menu, a toggle or a search field.
            // The adds sit in one priority list: as many as the bar's width allows are shown as buttons, the rest
            // are folded into "+ More" by UpdateOverflow, and "+ More" hides itself when everything fits.
            var actionsBar = new Toolbar { name = "ad-toolbar-actions" };
            actionsBar.AddToClassList("ad-toolbar");
            actionsBar.AddToClassList("ad-toolbar-actions");
            this.actionsBar = actionsBar;
            AddAction(actionsBar, "ad-add-toggle", "ad.ui.add.toggle", "ad.ui.add.toggle.tip", () => AddEntry(EntryKind.Toggle));
            AddAction(actionsBar, "ad-add-radial", "ad.ui.add.radial", "ad.ui.add.radial.tip", () => AddEntry(EntryKind.Radial));
            AddAction(actionsBar, "ad-add-submenu", "ad.ui.add.submenu", "ad.ui.add.submenu.tip", () => AddEntry(EntryKind.Submenu));
            foreach (var item in MoreItems())
            {
                var run = item.Run;
                AddAction(actionsBar, ButtonNameOf(item.NameKey), item.NameKey, item.DescriptionKey, run);
                if (item.NameKey == "ad.ui.add.outfit") break;
            }
            var contact = AddAction(actionsBar, "ad-add-contact", "ad.ui.add.contact", "ad.ui.add.contact.tip", null);
            contact.clickable = new Clickable(() => AdMenu.Open(contact.worldBound, contact, ContactStartMenuItems()));
            contact.text = "";
            contact.Add(new Label(L.Text("ad.ui.add.contact")) { pickingMode = PickingMode.Ignore });
            AddDropdownArrow(contact);
            addItems[addItems.Count - 1].Children = ContactStartMenuItems();
            addItems[addItems.Count - 1].Action = null;

            // A plain ToolbarButton that opens the hidden adds as an AdMenu on click. Its own text stays empty: a
            // ToolbarButton draws its text across the whole content box regardless of any appended child, so the
            // label and the arrow that sits after it are both plain children of a row instead, the only way to keep
            // the arrow from drawing over the last letter.
            Button more = null;
            more = new ToolbarButton(() => AdMenu.Open(more.worldBound, more, moreItems)) { name = "ad-more", tooltip = L.Text("ad.ui.more.tip") };
            more.Add(new Label(L.Text("ad.ui.more")) { pickingMode = PickingMode.Ignore });
            AddDropdownArrow(more);
            lockDisabled.Add(more);
            moreButton = more;
            actionsBar.Add(more);
            RebuildMoreItems(overflowAddCount);

            var actionsSpacer = new VisualElement();
            actionsSpacer.AddToClassList("ad-spacer");
            actionsBar.Add(actionsSpacer);

            captureButton = new ToolbarButton(() => CaptureMissingIcons()) { text = L.Text("ad.ui.toolbar.capture"), tooltip = L.Text("ad.ui.capture_missing.tip"), name = "ad-capture-missing" };
            lockDisabled.Add(captureButton);
            actionsBar.Add(captureButton);

            convertButton = new ToolbarButton(OpenImportWindow) { text = L.Text("ad.ui.toolbar.convert"), tooltip = L.Text("ad.ui.import.tooltip"), name = "ad-import-open" };
            lockDisabled.Add(convertButton);
            actionsBar.Add(convertButton);

            // A ToolbarButton, not a ToolbarToggle: the same control type as every other toolbar item, carrying a
            // checked class of its own while the wheel shows instead of a built in checkmark state.
            wheelToggle = new ToolbarButton(ToggleWheel) { name = "ad-wheel-toggle", text = L.Text("ad.ui.wheel"), tooltip = L.Text("ad.ui.wheel.toggle.tip") };
            wheelToggle.AddToClassList("ad-wheel-toggle");
            actionsBar.Add(wheelToggle);
            RefreshWheelToggle();
            actionsBar.RegisterCallback<GeometryChangedEvent>(OnActionsGeometry);
            actionsSpacer.RegisterCallback<GeometryChangedEvent>(OnActionsGeometry);
            // A button that a mode switch shows or hides changes its own box, which is the one thing guaranteed to
            // follow every such switch, so each one reports to the same fitting pass as the bar and the spacer.
            foreach (var item in addButtons.Cast<VisualElement>().Concat(new VisualElement[] { more, captureButton, convertButton, wheelToggle }))
                item.RegisterCallback<GeometryChangedEvent>(OnActionsGeometry);
            actionsBar.RegisterCallback<AttachToPanelEvent>(e => ResetOverflow());
            Add(actionsBar);

            var findBar = new Toolbar { name = "ad-toolbar-find" };
            findBar.AddToClassList("ad-toolbar");
            findBar.AddToClassList("ad-toolbar-find");
            // A plain TextField, not a ToolbarSearchField: the search icon and the empty placeholder are drawn
            // inside its own single bordered input box instead of a second, nested box of the control's own, which
            // is what put a box inside a box and pressed the caret against the magnifier on the creator's screen.
            var search = new TextField { name = "ad-search", tooltip = L.Text("ad.ui.search.tip") };
            search.AddToClassList("ad-search");
            var searchInput = search.Q(className: "unity-text-field__input") ?? search;
            // 20px of left padding, not a USS class: the panel's own spacing scale (SpacingUsesTheScale) only
            // allows 0, 2, 4, 6 or 8, but that is not room enough for a legible magnifier and the 4px gap the
            // creator asked for before the caret, so this one inset is set here instead of in the stylesheet.
            searchInput.style.paddingLeft = 20;
            var searchIcon = new Image { image = FirstIcon("Search Icon", "d_Search Icon", "SearchWindow", "d_SearchWindow", "ViewToolZoom", "d_ViewToolZoom"), pickingMode = PickingMode.Ignore };
            searchIcon.AddToClassList("ad-search-icon");
            searchInput.Add(searchIcon);
            var searchPlaceholder = new Label(L.Text("ad.ui.search.placeholder")) { pickingMode = PickingMode.Ignore };
            searchPlaceholder.AddToClassList("ad-search-placeholder");
            searchInput.Add(searchPlaceholder);
            bool searchFocused = false;
            void RefreshSearchPlaceholder() => searchPlaceholder.EnableInClassList("ad-hidden", searchFocused || search.value.Length > 0);
            search.RegisterValueChangedCallback(e => { Filter = e.newValue; RefreshSearchPlaceholder(); });
            search.RegisterCallback<FocusInEvent>(_ => { searchFocused = true; RefreshSearchPlaceholder(); });
            search.RegisterCallback<FocusOutEvent>(_ => { searchFocused = false; RefreshSearchPlaceholder(); });
            RefreshSearchPlaceholder();
            findBar.Add(search);
            // Rebuilt fresh every time the tree refreshes (RefreshTagFilterMenu, called from Refresh below), so
            // its ticks and its count always match whatever is actually active right now.
            tagFilterButton = new ToolbarButton(OpenTagFilter) { name = "ad-tag-filter", tooltip = L.Text("ad.ui.tagfilter.tip") };
            tagFilterButton.AddToClassList("ad-tag-filter");
            SetToolbarMenuIcon(tagFilterButton, "d_FilterByType", "FilterByType");
            tagFilterButton.Add(tagFilterLabel);
            AddDropdownArrow(tagFilterButton);
            findBar.Add(tagFilterButton);
            Add(findBar);
            ApplyToolbarMode();

            filterChips.AddToClassList("ad-filter-chips");
            Add(filterChips);

            searchNote.AddToClassList("ad-note");
            searchNote.AddToClassList("ad-search-note");
            Add(searchNote);

            Tree.fixedItemHeight = 22;
            Tree.selectionType = SelectionType.Single;
            Tree.makeItem = MakeRow;
            Tree.bindItem = BindRow;
            Tree.selectionChanged += OnSelectionChanged;
            Tree.reorderable = true;
            Tree.itemIndexChanged += HandleReorder;
            Tree.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Delete) { DeleteSelected(); e.StopPropagation(); } });
            Tree.RegisterCallback<PointerCaptureEvent>(e => { if (e.target == Tree) Dragging = true; });
            Tree.RegisterCallback<PointerCaptureOutEvent>(e => { if (e.target == Tree) { Dragging = false; ClearDragPreview(); } });
            Tree.RegisterCallback<PointerMoveEvent>(OnTreePointerMove);
            Tree.RegisterCallback<GeometryChangedEvent>(OnTreeGeometryChanged);
            Tree.AddToClassList("ad-tree");
            Add(Tree);

            dragTarget.AddToClassList("ad-drag-target");
            Add(dragTarget);
            Add(dropRefusal);

            welcomeCard.AddToClassList("ad-welcome-card");
            BuildWelcomeCard();
            Add(welcomeCard);

            // The TreeView inside answers every drag over it itself and stops the event, rejecting anything that did
            // not come from its own rows, so the drops this view takes (objects, controllers, menus) are looked at
            // before it sees them, and the event stops only for a drop that was taken.
            this.AddManipulator(new HierarchyDropManipulator(this, go => !Locked && go != null && !MenuModel.IsOutsideAvatar(owner.Plan.AvatarRoot, go.transform), AddTogglesFor, TrickleDown.TrickleDown));
            RegisterCallback<PointerDownEvent>(OnTreePointerDown);
            RegisterCallback<DragUpdatedEvent>(OnControllerDragUpdated, TrickleDown.TrickleDown);
            RegisterCallback<DragPerformEvent>(OnControllerDragPerform, TrickleDown.TrickleDown);
        }

        /// <summary>Whether this component is locked by its maker. While locked, every edit here is refused; the tree still lets rows be selected and the search field still filters.</summary>
        bool Locked => owner.Locked;

        /// <summary>Whether a row's own action buttons collapse to one "more" button and its least essential chips drop. Public so a test can read what a row would do at a given width without a real layout pass.</summary>
        public bool IsNarrow(float width) => width > 0f && width < NarrowRowWidth;

        /// <summary>The width IsNarrow reads right now: the override a test set, or the tree's own resolved width once it has actually been laid out (zero, and so never narrow, otherwise).</summary>
        bool NarrowNow => IsNarrow(RowWidthOverride ?? Tree.resolvedStyle.width);

        void OnTreeGeometryChanged(GeometryChangedEvent evt)
        {
            bool narrow = NarrowNow;
            if (narrow == narrowLast) return;
            narrowLast = narrow;
            Tree.RefreshItems();
        }

        /// <summary>Builds one pooled row's own content, exactly as the tree's makeItem does. Public so a test can inspect it (the drag grip's overlay placement) without a real TreeView layout, which a headless test's panel never actually resolves.</summary>
        public VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("ad-tree-row");
            row.Add(new KindGlyph(GlyphKind.Toggle));
            var name = new Label();
            name.AddToClassList("ad-name");
            name.enableRichText = true;
            row.Add(name);
            var meta = new VisualElement { name = "meta" };
            meta.style.flexDirection = FlexDirection.Row;
            meta.style.alignItems = Align.Center;
            row.Add(meta);
            row.RegisterCallback<PointerDownEvent>(e => OnRowPointerDown(e, row));
            row.RegisterCallback<GeometryChangedEvent>(OnRowGeometryChanged);
            return row;
        }

        void BindRow(VisualElement element, int index)
        {
            var row = RowAtIndex(index);
            if (row == null)
            {
                // A row the tree asks for that no longer has data (the tree was rebuilt while this pooled element
                // was still queued for binding) is left empty rather than throwing, which would leave it blank
                // and keep every later bind from running.
                element.userData = null;
                element.Q<Label>(className: "ad-name").text = "";
                element.Q("meta")?.Clear();
                return;
            }
            BindRowContent(element, row);
        }

        /// <summary>The row the tree shows at this index: the data carried by the tree's own item first, so the
        /// tree and the lookup can never disagree, then the id lookup; null when neither has it.</summary>
        public TreeRow RowAtIndex(int index)
        {
            TreeRow fromItem = null;
            try { fromItem = Tree.GetItemDataForIndex<TreeRow>(index); } catch (System.Exception) { }
            if (fromItem != null) return fromItem;
            int id;
            try { id = Tree.GetIdForIndex(index); } catch (System.Exception) { return null; }
            return rows.TryGetValue(id, out var row) ? row : null;
        }

        /// <summary>Everything a row's own content shows for its row, exactly as the tree's bindItem uses it. Public so a test can drive it directly on a row TestMakeRow() built, since the TreeView's own virtualization never actually realizes a row inside a headless test.</summary>
        public void BindRowContent(VisualElement element, TreeRow row)
        {
            element.userData = row;
            element.EnableInClassList("ad-dim", row.Excluded || row.NoMenu || row.Kind == RowKind.Existing || row.Kind == RowKind.Foreign || (!row.Mine && row.Kind != RowKind.TriggersGroup && row.Kind != RowKind.OverridesGroup && row.Kind != RowKind.More));
            element.EnableInClassList("ad-group", row.Kind == RowKind.TriggersGroup || row.Kind == RowKind.OverridesGroup);
            element.EnableInClassList("ad-filter-dim", row.FilterDimmed);
            element.Q<KindGlyph>().Kind = GlyphFor(row.Kind);
            element.Q<Label>(className: "ad-name").text = row.Name;
            var meta = element.Q("meta");
            meta.Clear();
            var slotsChipText = SlotsChipText(row);
            if (slotsChipText != null)
                meta.Add(Chip(slotsChipText, row.Slots >= VRCExpressionsMenu.MAX_CONTROLS ? "ad-chip-warn" : null));
            if (row.Kind == RowKind.TriggersGroup)
            {
                var note = new Label(UiText.Count(owner.Plan.Tree.Triggers.Count, "ad.ui.triggers.note.one", "ad.ui.triggers.note"));
                note.AddToClassList("ad-note");
                meta.Add(note);
            }
            if (row.Kind == RowKind.OverridesGroup)
            {
                var note = new Label(UiText.Count(owner.Plan.Tree.Overrides.Count, "ad.ui.overrides.note.one", "ad.ui.overrides.note"));
                note.AddToClassList("ad-note");
                meta.Add(note);
            }
            if (row.Kind == RowKind.AnimatorsGroup)
            {
                var note = new Label(string.Format(L.Text("ad.ui.animators.note"), owner.Plan.Tree.Merges.Count));
                note.AddToClassList("ad-note");
                meta.Add(note);
            }
            if (row.Kind == RowKind.MenuAssetsGroup)
            {
                var note = new Label(string.Format(L.Text("ad.ui.menu_assets.note"), owner.Plan.Tree.MenuAssets.Count));
                note.AddToClassList("ad-note");
                meta.Add(note);
            }
            if (row.Kind == RowKind.ParametersGroup)
            {
                var note = new Label(string.Format(L.Text("ad.ui.parameters.note"), owner.Plan.Tree.Components.Where(c => c != null).Sum(c => c.parameters.Count)));
                note.AddToClassList("ad-note");
                meta.Add(note);
            }
            if (row.Kind == RowKind.Parameter && row.Declaration != null)
            {
                var note = new Label(string.Format(L.Text(UiText.CountKey(row.Declaration.Bits, "ad.ui.parameter.bits")), row.Declaration.type, row.Declaration.Bits));
                note.AddToClassList("ad-note");
                meta.Add(note);
            }
            bool narrow = NarrowNow;
            if (row.DefaultOn) { var dot = new VisualElement(); dot.AddToClassList("ad-dot"); dot.tooltip = L.Text("ad.ui.default_on"); meta.Add(dot); }
            // At the narrowest inspector width these four are the row's least essential chips: dropping them
            // first is what lets the name stay readable and the warning glyph and the state chips below stand,
            // without the row ever having to wrap.
            if (!narrow && row.GroupChip != null) meta.Add(Chip(row.GroupChip, "ad-chip-on"));
            if (!narrow && row.SourceChip != null) meta.Add(Chip(row.SourceChip, "ad-chip-source"));
            if (!narrow && !string.IsNullOrEmpty(row.TriggerChip)) meta.Add(Chip(row.TriggerChip, "ad-chip-trigger"));
            if (row.PlatformChip != null && (!narrow || row.Excluded)) meta.Add(Chip(row.PlatformChip, row.Excluded ? "ad-chip-warn" : "ad-chip-existing"));
            if (row.OwnerDisabled) meta.Add(Chip(L.Text("ad.ui.disabled_chip"), "ad-chip-warn"));
            if (row.NoMenu) meta.Add(Chip(L.Text("ad.ui.chip.no_menu"), "ad-chip-warn", L.Text("ad.ui.chip.no_menu.tip")));
            if (!narrow && row.FromAsset) meta.Add(Chip(L.Text("ad.ui.chip.asset"), "ad-chip-existing", L.Text("ad.ui.chip.asset.tip")));
            if (!narrow && row.Kind == RowKind.Existing) meta.Add(Chip(L.Text("ad.ui.existing"), "ad-chip-existing"));
            if (row.Edited) meta.Add(Chip(L.Text("ad.ui.chip.edited"), "ad-chip-on"));
            if (row.Removed) meta.Add(Chip(L.Text("ad.ui.chip.removed"), "ad-chip-warn"));
            else if (row.Hidden) meta.Add(Chip(L.Text("ad.ui.chip.hidden"), "ad-chip-warn"));
            if (row.Messages.Count > 0)
            {
                var chip = new VisualElement { name = "ad-row-problem-chip" };
                chip.AddToClassList("ad-chip");
                chip.AddToClassList("ad-chip-warn");
                chip.tooltip = string.Join("\n", row.Messages.Select(UiText.Format));
                var warnGlyph = new KindGlyph(GlyphKind.Warning);
                warnGlyph.RemoveFromClassList("ad-glyph");
                warnGlyph.AddToClassList("ad-chip-warn-icon");
                chip.Add(warnGlyph);
                chip.Add(new Label(L.Text("ad.ui.chip.problem")));
                meta.Add(chip);
            }
        }

        /// <summary>
        /// Every built in icon this view has already looked up, by the exact name asked for: EditorGUIUtility's
        /// own icon database is a per-session, project wide resource, so a name already resolved once answers
        /// every later toolbar built from the cached texture instead of asking Unity again. A new AvatarDoctorView
        /// is built every time the inspector selects a different component, so without this every one of them paid
        /// for the same handful of lookups again.
        /// </summary>
        static readonly Dictionary<string, Texture> iconCache = new Dictionary<string, Texture>();

        static Texture LookedUpIcon(string name)
        {
            if (iconCache.TryGetValue(name, out var cached)) return cached;
            Texture texture;
            try { texture = EditorGUIUtility.IconContent(name)?.image; }
            catch { texture = null; }
            iconCache[name] = texture;
            return texture;
        }

        /// <summary>Loads a built in editor icon by the dark or light skin's own name, or null when this Unity version or skin has no such icon; never throws. The one place either kind of icon lookup happens.</summary>
        static Texture TryLoadIcon(string proIconName, string lightIconName)
        {
            return LookedUpIcon(EditorGUIUtility.isProSkin ? proIconName : lightIconName);
        }

        /// <summary>The first of several built in icon names that this Unity version actually has a texture for, since an icon's name has moved between versions and skins.</summary>
        internal static Texture FirstIcon(params string[] names)
        {
            foreach (var name in names)
            {
                var texture = LookedUpIcon(name);
                if (texture != null) return texture;
            }
            return null;
        }

        /// <summary>Shows a built in editor icon in front of a ToolbarMenu's own text; does nothing when this Unity version or skin has no such icon.</summary>
        static void SetToolbarMenuIcon(TextElement element, string proIconName, string lightIconName)
        {
            var icon = TryLoadIcon(proIconName, lightIconName);
            if (icon == null) return;
            var image = new Image { image = icon, pickingMode = PickingMode.Ignore };
            image.AddToClassList("ad-toolbarmenu-icon");
            element.Insert(0, image);
        }

        /// <summary>Appends the same small dropdown arrow after a toolbar button's own text, so a button that opens a popup menu looks exactly like the other one, whichever built in icon name this Unity version and skin has.</summary>
        internal static void AddDropdownArrow(TextElement element)
        {
            var icon = TryLoadIcon("d_icon dropdown", "icon dropdown");
            if (icon == null) return;
            var image = new Image { image = icon, pickingMode = PickingMode.Ignore };
            image.AddToClassList("ad-dropdown-arrow");
            element.Add(image);
        }

        void OpenTagFilter() => AdMenu.Open(tagFilterButton.worldBound, tagFilterButton, tagFilterItems, true, () => tagFilterItems);

        void OnTreePointerDown(PointerDownEvent e)
        {
            if (!IsMenuClick(e) || Locked) return;
            var items = new List<AdMenu.Item> { new AdMenu.Item { Text = L.Text("ad.ui.capture_missing"), Action = () => CaptureMissingIcons() } };
            AdMenu.Open(new Rect(e.position, Vector2.zero), this, items);
            e.PreventDefault();
            e.StopPropagation();
        }

        /// <summary>A right click, or Control and left click as on a Mac.</summary>
        internal static bool IsMenuClick(PointerDownEvent e) => e.button == 1 || (e.button == 0 && e.ctrlKey);

        void OnRowPointerDown(PointerDownEvent e, VisualElement element)
        {
            if (!IsMenuClick(e)) return;
            var items = RowMenu(element.userData as TreeRow);
            if (items == null || items.Count == 0) return;
            AdMenu.Open(new Rect(e.position, Vector2.zero), element, items);
            e.PreventDefault();
            e.StopPropagation();
        }

        /// <summary>
        /// The right click menu's own actions for a row, in the order the menu shows them: Rename, Duplicate and
        /// Move to for an entry of this component, then Delete last; Convert alone for a foreign row; Delete alone
        /// for a merged animator, an installed menu asset or a declared parameter that is this component's own.
        /// BuildContextMenu reads this list in order and puts its own separator right before Delete, the same
        /// place for every row kind, so the menu can never drift from what a test already checked here.
        /// An existing row, a group row and anything not this component's own offer none here: an existing or
        /// foreign row's menu is built by BuildExistingContextMenu instead, which adds its own Convert.
        /// </summary>
        public IEnumerable<(string LabelKey, string TooltipKey, Action Run)> RowActions(TreeRow row)
        {
            if (row == null || Locked) yield break;
            if (row.Kind == RowKind.Foreign)
            {
                yield return ("ad.ui.convert", "ad.ui.row.convert.tip", () => ConvertForeign(row));
                yield break;
            }
            if (row.IsOwnEntry)
            {
                yield return ("ad.ui.menu.rename", "ad.ui.row.rename.tip", () => { SelectEntry(row.EntryId); RenameSelected(); });
                yield return ("ad.ui.menu.duplicate", "ad.ui.row.duplicate.tip", () => { SelectEntry(row.EntryId); DuplicateSelected(); });
                if (row.IsMenuEntry) yield return ("ad.ui.menu.move", "ad.ui.row.move.tip", () => OpenMoveMenu(row));
                yield return ("ad.ui.menu.delete", "ad.ui.row.delete.tip", () => { SelectEntry(row.EntryId); DeleteSelected(); });
                yield break;
            }
            if (row.Mine && (row.Kind == RowKind.Animator || row.Kind == RowKind.MenuAsset || row.Kind == RowKind.Parameter))
                yield return ("ad.ui.menu.delete", "ad.ui.row.delete.tip", () => { SelectKey(row.Key); DeleteSelected(); });
        }

        /// <summary>Puts the focus on the selected entry's own name field, for Rename. Does nothing while nothing is selected.</summary>
        public void RenameSelected()
        {
            if (Selected == null) return;
            owner.Detail.FocusField("ad-name");
        }

        /// <summary>Opens the same submenu list the right click menu offers under Move to, as a popup: RowActions' own fallback for a caller that runs the action directly instead of expanding it into a submenu the way BuildContextMenu does.</summary>
        void OpenMoveMenu(TreeRow row)
        {
            if (Locked || row == null || !row.IsMenuEntry) return;
            SelectEntry(row.EntryId);
            AdMenu.Open(Tree.worldBound, Tree, MoveItems(row));
        }

        /// <summary>
        /// The tree row's own slot chip text for a submenu nearly or already full, or null when it has room to
        /// spare: showing it only in the last two slots is what lets a creator see the split coming instead of
        /// having every submenu carry a chip regardless of how empty it is.
        /// </summary>
        public static string SlotsChipText(TreeRow row)
        {
            if (row.Slots < VRCExpressionsMenu.MAX_CONTROLS - 1) return null;
            return string.Format(L.Text("ad.ui.slots.chip"), row.Slots, VRCExpressionsMenu.MAX_CONTROLS);
        }

        static Label Chip(string text, string cls, string tooltip = null)
        {
            var chip = new Label(text) { tooltip = tooltip };
            chip.AddToClassList("ad-chip");
            if (!string.IsNullOrEmpty(cls)) chip.AddToClassList(cls);
            return chip;
        }

        static GlyphKind GlyphFor(RowKind kind)
        {
            switch (kind)
            {
                case RowKind.Submenu: return GlyphKind.Submenu;
                case RowKind.Radial: return GlyphKind.Radial;
                case RowKind.Trigger: return GlyphKind.Trigger;
                case RowKind.TriggersGroup: return GlyphKind.Group;
                case RowKind.Existing: return GlyphKind.Existing;
                case RowKind.More: return GlyphKind.More;
                case RowKind.Override: return GlyphKind.Override;
                case RowKind.OverridesGroup: return GlyphKind.Group;
                case RowKind.Preset: return GlyphKind.Preset;
                case RowKind.Grabbable: return GlyphKind.Grabbable;
                case RowKind.Foreign: return GlyphKind.Existing;
                case RowKind.AnimatorsGroup: return GlyphKind.Group;
                case RowKind.Animator: return GlyphKind.Animator;
                case RowKind.MenuAssetsGroup: return GlyphKind.Group;
                case RowKind.MenuAsset: return GlyphKind.Existing;
                case RowKind.ParametersGroup: return GlyphKind.Group;
                case RowKind.Parameter: return GlyphKind.Toggle;
                default: return GlyphKind.Toggle;
            }
        }

        void OnSelectionChanged(IEnumerable<object> selection)
        {
            if (suppressSelection) return;
            var row = selection.OfType<TreeRow>().FirstOrDefault();
            SetSelected(row, notify: true);
        }

        void SetSelected(TreeRow row, bool notify)
        {
            hiddenSelectionKey = null;
            Selected = row;
            RefreshRowActionsBinding(row);
            if (notify) SelectionChanged?.Invoke(row);
        }

        /// <summary>
        /// Rebinds every currently realized row so whichever one just gained or lost the visible row-action
        /// buttons is redrawn immediately, without waiting for an unrelated rebuild. A row's own <see cref="TreeRow.Id"/>
        /// is reassigned by every full <see cref="Refresh"/> (TreeRow.Build hands out a fresh counter each time), so
        /// targeting yesterday's id after such a refresh could silently rebind whatever row happens to hold that
        /// number now instead of the one that actually changed selection: a pooled row could then keep showing its
        /// actions after the row that owned them was no longer selected. Refreshing every realized row costs only a
        /// rebind of what is already on screen, not a walk of the avatar, so it stays cheap.
        /// </summary>
        void RefreshRowActionsBinding(TreeRow row) => Tree.RefreshItems();

        /// <summary>Lets go of a selection that a search or tag filter is hiding, so the row is not selected again when the filter ends.</summary>
        public void ForgetHiddenSelection() => hiddenSelectionKey = null;

        public TreeRow RowFor(string entryId)
        {
            return entryId == null ? null : rows.Values.FirstOrDefault(r => r.EntryId == entryId);
        }

        public TreeRow RowForKey(string key)
        {
            return key == null ? null : rows.Values.FirstOrDefault(r => r.Key == key);
        }

        /// <summary>The row of an existing or foreign item a menu edit changed, by that edit's own id. Used to reveal a message that names an edit rather than an entry.</summary>
        public TreeRow RowForEdit(string editId)
        {
            return string.IsNullOrEmpty(editId) ? null : rows.Values.FirstOrDefault(r => r.Item?.Edit?.id == editId);
        }

        public void SelectEntry(string entryId) => Select(RowFor(entryId));

        /// <summary>Selects a row by its stable Key, which survives a rebuild even for rows without an entry id.</summary>
        public void SelectKey(string key) => Select(RowForKey(key));

        /// <summary>Clears the selection, as the wheel's root crumb does.</summary>
        public void ClearSelection() => Select(null);

        void Select(TreeRow row)
        {
            suppressSelection = true;
            try
            {
                if (row != null) Tree.SetSelectionById(row.Id);
                else Tree.ClearSelection();
            }
            finally
            {
                suppressSelection = false;
            }
            SetSelected(row, notify: true);
        }

        /// <summary>Adds an entry under the selected submenu of this component, else at the component's root, and selects it. Refused while locked.</summary>
        public MenuEntry AddEntry(EntryKind kind)
        {
            if (Locked) return null;
            string parentId = kind == EntryKind.Trigger || kind == EntryKind.Override ? ""
                : Selected != null && Selected.IsMenuEntry && Selected.Entry.kind == EntryKind.Submenu ? Selected.Entry.id
                : Selected != null && Selected.IsMenuEntry ? Selected.Entry.parentId : "";
            var entry = EntryEditing.Add(owner.Menu, kind, "New " + kind, parentId);
            pendingSelectionId = entry.id;
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
            return entry;
        }

        /// <summary>Adds a grabbable entry under the selected submenu of this component, else at the component's root, and selects it. Refused while locked.</summary>
        public MenuEntry AddGrabbable()
        {
            if (Locked) return null;
            string parentId = Selected != null && Selected.IsMenuEntry && Selected.Entry.kind == EntryKind.Submenu ? Selected.Entry.id
                : Selected != null && Selected.IsMenuEntry ? Selected.Entry.parentId : "";
            var entry = EntryEditing.AddGrabbable(owner.Menu, parentId);
            if (entry == null) return null;
            pendingSelectionId = entry.id;
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
            return entry;
        }

        /// <summary>Creates one toggle per object under the selected submenu of this component (else at the root) and selects the last one. Refused while locked.</summary>
        public void AddTogglesFor(IEnumerable<GameObject> objects)
        {
            if (Locked) return;
            string parentId = Selected != null && Selected.IsMenuEntry && Selected.Entry.kind == EntryKind.Submenu ? Selected.Entry.id
                : Selected != null && Selected.IsMenuEntry ? Selected.Entry.parentId : "";
            MenuEntry last = null;
            foreach (var go in objects)
            {
                if (go == null || MenuModel.IsOutsideAvatar(owner.Plan.AvatarRoot, go.transform)) continue;
                last = EntryEditing.Add(owner.Menu, EntryKind.Toggle, go.name, parentId);
                last.objects.Add(new ObjectTarget { target = go, activeWhenOn = true });
            }
            if (last == null) return;
            EntryEditing.MarkChanged(owner.Menu);
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
            RevealEntry(last.id);
        }

        /// <summary>Adds a merged animator, selects it and opens the Animators group to show it. Refused while locked.</summary>
        public AnimatorMerge AddAnimator(RuntimeAnimatorController controller)
        {
            if (Locked) return null;
            var merge = EntryEditing.AddAnimator(owner.Menu, controller);
            if (merge == null) return null;
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
            RevealMerge(merge.id);
            return merge;
        }

        /// <summary>Removes a merged animator. Refused while locked.</summary>
        public bool RemoveAnimator(AnimatorMerge merge)
        {
            if (Locked || !EntryEditing.RemoveAnimator(owner.Menu, merge)) return false;
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
            return true;
        }

        /// <summary>Installs a menu asset, selects it and opens the Menu assets group to show it. Refused while locked.</summary>
        public MenuAssetInstall AddMenuAsset(VRCExpressionsMenu asset)
        {
            if (Locked) return null;
            var install = EntryEditing.AddMenuAsset(owner.Menu, asset);
            if (install == null) return null;
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
            RevealKey(install.id);
            return install;
        }

        /// <summary>Declares one more parameter of this component, a Bool at first, and selects the Parameters row that shows the table. The first declaration starts that row. Refused while locked.</summary>
        public ParameterDeclaration AddDeclaration()
        {
            if (Locked) return null;
            var declaration = EntryEditing.AddDeclaration(owner.Menu, EntryEditing.NewDeclarationName(owner.Menu), PlannedType.Bool);
            if (declaration == null) return null;
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
            RevealKey(TreeRow.ParametersRowKey);
            return declaration;
        }

        /// <summary>Removes an installed menu asset. Refused while locked.</summary>
        public bool RemoveMenuAsset(MenuAssetInstall install)
        {
            if (Locked || !EntryEditing.RemoveMenuAsset(owner.Menu, install)) return false;
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
            return true;
        }

        /// <summary>Removes a declared parameter. Refused while locked.</summary>
        public bool RemoveDeclaration(ParameterDeclaration declaration)
        {
            if (Locked || !EntryEditing.RemoveDeclaration(owner.Menu, declaration)) return false;
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
            return true;
        }

        /// <summary>Accepts a RuntimeAnimatorController dragged from the Project window and adds a merged animator with it, the same as the toolbar's "+ Animator" followed by picking the controller.</summary>
        void OnControllerDragUpdated(DragUpdatedEvent e)
        {
            if (Locked) return;
            if (!DragAndDrop.objectReferences.OfType<RuntimeAnimatorController>().Any()
                && !DragAndDrop.objectReferences.OfType<VRCExpressionsMenu>().Any()) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
            e.StopPropagation();
        }

        void OnControllerDragPerform(DragPerformEvent e)
        {
            if (Locked) return;
            var asset = DragAndDrop.objectReferences.OfType<VRCExpressionsMenu>().FirstOrDefault();
            if (asset != null)
            {
                DragAndDrop.AcceptDrag();
                AddMenuAsset(asset);
                e.StopPropagation();
                return;
            }
            var controller = DragAndDrop.objectReferences.OfType<RuntimeAnimatorController>().FirstOrDefault();
            if (controller == null) return;
            DragAndDrop.AcceptDrag();
            AddAnimator(controller);
            e.StopPropagation();
        }

        /// <summary>Opens the convert window for this component. A field of its own so the toolbar's own button and a test call the same thing. Refused while locked.</summary>
        public void OpenImportWindow() { if (!Locked) ImportWindow.Open(owner.Menu); }

        /// <summary>Opens the outfit set wizard for this component. The method the toolbar's own button calls, so a test can call the same thing. Refused while locked.</summary>
        public void OpenOutfitWindow() { if (!Locked) OutfitSetWindow.Open(owner.Menu); }

        /// <summary>Opens the Gesture faces window for this component, as its toolbar button does. Refused while locked.</summary>
        public void OpenGestureFacesWindow() { if (!Locked) GestureFacesWindow.Open(owner.Menu); }

        // ---- the welcome card (first screen) --------------------------------------------------------------

        Button welcomeConvertButton;

        void BuildWelcomeCard()
        {
            var title = new Label(L.Text("ad.ui.welcome.title"));
            title.AddToClassList("ad-note");
            welcomeCard.Add(title);

            welcomeCard.Add(WelcomeOption("ad.ui.welcome.toggle", "ad.ui.welcome.toggle.tip", "ad.ui.welcome.toggle.desc", "ad-welcome-toggle", OpenWelcomeTogglePicker, out _));
            welcomeCard.Add(WelcomeOption("ad.ui.welcome.convert.title", "ad.ui.welcome.convert.tip", "ad.ui.welcome.convert.desc", "ad-welcome-convert", OpenImportWindow, out welcomeConvertButton));
            welcomeCard.Add(WelcomeOption("ad.ui.welcome.outfit", "ad.ui.welcome.outfit.tip", "ad.ui.welcome.outfit.desc", "ad-welcome-outfit", OpenOutfitWindow, out _));

            var quickStart = new Button(QuickStart.Open) { text = L.Text("ad.ui.welcome.quickstart"), name = "ad-welcome-quickstart", tooltip = L.Text("ad.ui.welcome.quickstart.tip") };
            quickStart.AddToClassList("ad-welcome-button");
            welcomeCard.Add(quickStart);

            dropHint.text = L.Text("ad.ui.drop.tree");
            dropHint.AddToClassList("ad-drop-hint");
            welcomeCard.Add(dropHint);
        }

        VisualElement WelcomeOption(string labelKey, string tipKey, string descKey, string name, Action action, out Button button)
        {
            var box = new VisualElement();
            box.AddToClassList("ad-welcome-option");
            button = new Button(action) { text = L.Text(labelKey), name = name, tooltip = L.Text(tipKey) };
            button.AddToClassList("ad-welcome-button");
            lockDisabled.Add(button);
            box.Add(button);
            var desc = new Label(L.Text(descKey));
            desc.AddToClassList("ad-note");
            box.Add(desc);
            return box;
        }

        /// <summary>The welcome card's own "Make a toggle" start: picks one object on the avatar and makes a toggle from it, the same as dropping it.</summary>
        void OpenWelcomeTogglePicker()
        {
            if (Locked) return;
            var root = owner.Plan?.AvatarRoot;
            var button = welcomeCard.Q<Button>("ad-welcome-toggle");
            if (root == null || button == null) return;
            AvatarObjectPicker.Show(button, root, PickKind.GameObject, picked =>
            {
                if (picked is GameObject go) AddTogglesFor(new[] { go });
            });
        }

        /// <summary>Shows or hides the welcome card for whether this component has any entry of its own yet, and, while shown, whether the Convert start has anything to offer.</summary>
        void RefreshWelcomeCard(AvatarPlan plan)
        {
            bool empty = owner.Menu != null && owner.Menu.entries.Count == 0;
            welcomeCard.EnableInClassList("ad-hidden", !empty);
            if (!empty) return;
            // Simple has no Convert, so it does not scan the avatar to find something to offer it for.
            if (owner.Simple)
            {
                welcomeConvertButton.parent.EnableInClassList("ad-hidden", true);
                return;
            }
            var label = ConvertStartLabel(ImportCandidatesForWelcomeCard(plan));
            welcomeConvertButton.parent.EnableInClassList("ad-hidden", label == null);
            if (label != null) welcomeConvertButton.text = label;
        }

        /// <summary>
        /// What VRCFury or Modular Avatar would install on this avatar, scanned once and kept until the avatar
        /// itself changes: the welcome card only ever needs the count, so refreshing it (which happens on every
        /// plan change while the tree is empty) must never re-walk the avatar on its own.
        /// </summary>
        List<ImportCandidate> ImportCandidatesForWelcomeCard(AvatarPlan plan)
        {
            if (plan?.AvatarRoot == null)
            {
                importScanRoot = null;
                importScanCache = null;
                return new List<ImportCandidate>();
            }
            int planVersion = AvatarPlanCache.VersionFor(plan.AvatarRoot);
            if (importScanCache == null || importScanRoot != plan.AvatarRoot || importScanPlanVersion != planVersion)
            {
                WelcomeScans++;
                importScanRoot = plan.AvatarRoot;
                importScanPlanVersion = planVersion;
                importScanCache = ImportScanner.Scan(plan.AvatarRoot, plan.Tree);
            }
            return importScanCache;
        }

        /// <summary>The welcome card's Convert start text, or null when nothing on the avatar can be converted at all.</summary>
        static string ConvertStartLabel(List<ImportCandidate> candidates)
        {
            var supported = candidates.Where(c => c.Supported).ToList();
            if (supported.Count == 0) return null;
            bool allVrcFury = supported.All(c => c.Source == ImportSource.VRCFury);
            bool allModular = supported.All(c => c.Source == ImportSource.ModularAvatar);
            string source = allVrcFury ? L.Text("ad.ui.chip.vrcfury")
                : allModular ? L.Text("ad.ui.chip.modular_avatar")
                : L.Text("ad.ui.welcome.convert.mixed_source");
            return string.Format(L.Text(UiText.CountKey(supported.Count, "ad.ui.welcome.convert")), supported.Count, source);
        }

        // ---- the "+ More" menu ------------------------------------------------------------------------------

        /// <summary>Every action the "+ More" button offers, each with its own one line description, in the order the menu lists them. Public so a test can read the list without opening a real menu.</summary>
        public List<(string NameKey, string DescriptionKey, Action Run)> MoreItems()
        {
            return new List<(string, string, Action)>
            {
                ("ad.ui.add.trigger", "ad.ui.add.trigger.tip", () => AddEntry(EntryKind.Trigger)),
                ("ad.ui.add.override", "ad.ui.add.override.tip", () => AddEntry(EntryKind.Override)),
                ("ad.ui.add.preset", "ad.ui.add.preset.tip", () => AddEntry(EntryKind.Preset)),
                ("ad.ui.add.grabbable", "ad.ui.add.grabbable.tip", () => AddGrabbable()),
                ("ad.ui.add.animator", "ad.ui.add.animator.tip", () => AddAnimator(null)),
                ("ad.ui.add.menu_asset", "ad.ui.add.menu_asset.tip", () => AddMenuAsset(null)),
                ("ad.ui.parameters.add", "ad.ui.parameters.add.tip", () => AddDeclaration()),
                ("ad.ui.add.gesture_faces", "ad.ui.add.gesture_faces.tip", OpenGestureFacesWindow),
                ("ad.ui.add.outfit", "ad.ui.add.outfit.tip", OpenOutfitWindow),
            };
        }

        static string ButtonNameOf(string nameKey)
        {
            switch (nameKey)
            {
                case "ad.ui.add.trigger": return "ad-add-trigger";
                case "ad.ui.add.override": return "ad-add-override";
                case "ad.ui.add.preset": return "ad-add-preset";
                case "ad.ui.add.grabbable": return "ad-add-grabbable";
                case "ad.ui.add.animator": return "ad-add-animator";
                case "ad.ui.add.menu_asset": return "ad-add-menu-asset";
                case "ad.ui.parameters.add": return "ad-add-parameter";
                case "ad.ui.add.gesture_faces": return "ad-add-gesture-faces";
                case "ad.ui.add.outfit": return "ad-outfit-open";
                default: return nameKey;
            }
        }

        ToolbarButton AddAction(VisualElement bar, string name, string nameKey, string descriptionKey, Action run)
        {
            var button = new ToolbarButton(run) { text = L.Text(nameKey), name = name, tooltip = L.Text(descriptionKey) };
            lockDisabled.Add(button);
            bar.Add(button);
            addButtons.Add(button);
            addItems.Add(new AdMenu.Item { Text = L.Text(nameKey), Description = L.Text(descriptionKey), Action = run });
            return button;
        }

        List<AdMenu.Item> ContactStartMenuItems()
        {
            return ContactStartItems().Select(start => new AdMenu.Item { Text = L.Text(start.NameKey), Description = L.Text(start.DescriptionKey), Action = start.Run }).ToList();
        }

        /// <summary>How many of the adds the bar shows as buttons right now, in priority order; the rest are in "+ More". Public so a test can read the split.</summary>
        public int VisibleAddCount => overflowAddCount;

        /// <summary>The add buttons in priority order, shown or not: every add in Advanced, the four Simple keeps in Simple.</summary>
        public IReadOnlyList<ToolbarButton> AddButtons => ModeAddIndexes().Select(i => addButtons[i]).ToList();

        /// <summary>Which adds, by place in the priority list, the current mode offers. The fitting and "+ More" work on this list alone.</summary>
        List<int> ModeAddIndexes()
        {
            var indexes = new List<int>();
            for (int i = 0; i < addButtons.Count; i++)
                if (!owner.Simple || SimpleAddNames.Contains(addButtons[i].name)) indexes.Add(i);
            return indexes;
        }

        void RebuildMoreItems(int visibleCount)
        {
            moreItems.Clear();
            var indexes = ModeAddIndexes();
            for (int i = visibleCount; i < indexes.Count; i++) moreItems.Add(addItems[indexes[i]]);
        }

        bool IsIconCapable(VisualElement element) => element == captureButton || element == wheelToggle;

        float Footprint(VisualElement element)
        {
            float width = element.layout.width;
            bool measurable = width > 0f && element.resolvedStyle.display != DisplayStyle.None && !compactRight;
            if (measurable) footprints[element] = width + element.resolvedStyle.marginLeft + element.resolvedStyle.marginRight;
            if (!footprints.TryGetValue(element, out var known)) return 0f;
            if (!compactRight || IsIconCapable(element)) return known;
            float saved = 2f * (6f - TightPadding) + (element.Q(className: "ad-dropdown-arrow") != null ? 4f - TightArrowMargin : 0f);
            return known - saved;
        }

        /// <summary>What an icon only button takes: the icon plus the button's own padding, border and margin, read from its resolved style.</summary>
        static float CompactFootprint(VisualElement element)
        {
            var style = element.resolvedStyle;
            return CompactIconSize + 2f * CompactIconPadding + style.borderLeftWidth + style.borderRightWidth + style.marginLeft + style.marginRight;
        }

        void ApplyCompact(bool compact)
        {
            if (compactRight == compact) return;
            compactRight = compact;
            actionsBar.EnableInClassList("ad-toolbar-tight", compact);
            SetIconOnly(captureButton, compact, "ad.ui.toolbar.capture", "SceneViewCamera", "d_SceneViewCamera", "Image Icon", "d_Image Icon");
            SetIconOnly(wheelToggle, compact, "ad.ui.wheel", "PreMatCylinder", "d_PreMatCylinder", "Transform Icon", "d_Transform Icon");
        }

        void SetIconOnly(Button button, bool iconOnly, string textKey, params string[] iconNames)
        {
            var existing = button.Q<Image>(className: "ad-toolbar-icon");
            existing?.RemoveFromHierarchy();
            button.EnableInClassList("ad-toolbar-compact", iconOnly);
            if (!iconOnly)
            {
                button.text = L.Text(textKey);
                return;
            }
            button.text = "";
            var icon = new Image { image = FirstIcon(iconNames), pickingMode = PickingMode.Ignore };
            icon.AddToClassList("ad-toolbar-icon");
            button.Add(icon);
        }

        /// <summary>
        /// Forgets every measured width and shows everything the mode offers again, so the next layout pass measures
        /// the real buttons afresh: a button that is hidden has no width to read. What the mode leaves out stays hidden.
        /// </summary>
        void ResetOverflow()
        {
            footprints.Clear();
            overflowWidth = float.NaN;
            ApplyCompact(false);
            bool simple = owner.Simple;
            foreach (var button in addButtons) button.EnableInClassList("ad-hidden", simple && !SimpleAddNames.Contains(button.name));
            moreButton.RemoveFromClassList("ad-hidden");
            captureButton.EnableInClassList("ad-hidden", simple);
            convertButton.EnableInClassList("ad-hidden", simple);
            wheelToggle.EnableInClassList("ad-hidden", simple);
        }

        /// <summary>Shows or hides the tools of the toolbar for the current mode and fits the bar again.</summary>
        void ApplyToolbarMode()
        {
            tagFilterButton.EnableInClassList("ad-hidden", owner.Simple);
            ResetOverflow();
            RebuildMoreItems(overflowAddCount);
        }

        /// <summary>
        /// Follows the owning view's mode: the toolbar and the finding bar keep only what Simple offers, and the welcome
        /// card drops its Convert start. A tag filter ticked in Advanced pauses in Simple and applies again in Advanced,
        /// without being forgotten; the result says whether the rows have to be drawn again for that, which the owning
        /// view then does through its own refresh, so a menu that is open or a drag that is running still holds it back.
        /// </summary>
        internal bool ApplyMode()
        {
            ApplyToolbarMode();
            if (owner.Plan == null) return false;
            RefreshTagChips();
            RefreshWelcomeCard(owner.Plan);
            return activeTags.Count > 0;
        }

        void OnActionsGeometry(GeometryChangedEvent e)
        {
            float width = actionsBar.contentRect.width;
            if (float.IsNaN(width) || width <= 0f) return;
            float font = actionsBar.resolvedStyle.fontSize;
            if (!float.IsNaN(overflowFontSize) && !Mathf.Approximately(font, overflowFontSize)) ResetOverflow();
            overflowFontSize = font;
            if (!float.IsNaN(overflowWidth) && Mathf.Abs(width - overflowWidth) <= 1f) return;
            if (UpdateOverflow(width)) overflowWidth = width;
        }

        /// <summary>Shows as many adds as fit beside the right group and folds the rest into "+ More". False while a button has not been laid out once yet, so the next layout pass tries again.</summary>
        bool UpdateOverflow(float width)
        {
            bool simple = owner.Simple;
            var offered = ModeAddIndexes();
            float captureFull = simple ? 0f : Footprint(captureButton);
            float wheelFull = simple ? 0f : Footprint(wheelToggle);
            float convertWidth = simple ? 0f : Footprint(convertButton);
            float moreWidth = Footprint(moreButton);
            var widths = offered.Select(i => Footprint(addButtons[i])).ToList();
            if (!simple && (captureFull <= 0f || wheelFull <= 0f || convertWidth <= 0f)) return false;
            ApplyCompact(width < CompactBarWidth);
            if (!simple) convertWidth = Footprint(convertButton);
            moreWidth = Footprint(moreButton);
            float right = simple ? 0f : convertWidth + (compactRight ? CompactFootprint(captureButton) + CompactFootprint(wheelToggle) : captureFull + wheelFull);
            widths = offered.Select(i => Footprint(addButtons[i])).ToList();
            if (moreWidth <= 0f || widths.Any(w => w <= 0f)) return false;
            float available = width - right - 1f;
            int fit = 0;
            if (widths.Sum() <= available) fit = offered.Count;
            else
            {
                float remaining = available - moreWidth;
                float used = 0f;
                while (fit < widths.Count && used + widths[fit] <= remaining) used += widths[fit++];
            }
            overflowAddCount = fit;
            for (int i = 0; i < addButtons.Count; i++)
            {
                int place = offered.IndexOf(i);
                addButtons[i].EnableInClassList("ad-hidden", place < 0 || place >= fit);
            }
            moreButton.EnableInClassList("ad-hidden", fit == offered.Count);
            RebuildMoreItems(fit);
            return true;
        }

        /// <summary>The three ready made contact starts shown under "+ More", grouped in a "Contact starts" submenu right after Trigger. Public so a test can read the list without opening a real menu.</summary>
        public List<(string NameKey, string DescriptionKey, Action Run)> ContactStartItems()
        {
            return new List<(string, string, Action)>
            {
                ("ad.ui.add.contact_starts.headpat", "ad.ui.add.contact_starts.headpat.tip", () => AddContactStart(ContactStarts.StartKind.Headpat)),
                ("ad.ui.add.contact_starts.eye_touch", "ad.ui.add.contact_starts.eye_touch.tip", () => AddContactStart(ContactStarts.StartKind.EyeTouch)),
                ("ad.ui.add.contact_starts.nose_boop", "ad.ui.add.contact_starts.nose_boop.tip", () => AddContactStart(ContactStarts.StartKind.NoseBoop)),
            };
        }

        /// <summary>
        /// Runs one of the ready made contact starts, selects the first entry it creates and opens its panel. One
        /// Undo step covers a start that makes more than one trigger. Refused (silently) while locked, the same as
        /// every other add; refused with a dialog naming what is missing when the avatar has no Head bone.
        /// </summary>
        public List<MenuEntry> AddContactStart(ContactStarts.StartKind kind)
        {
            if (Locked) return new List<MenuEntry>();
            var created = ContactStarts.Apply(owner.Menu, kind, out var errorKey);
            if (errorKey != null)
            {
                ShowContactStartError(L.Text(errorKey));
                return created;
            }
            if (created.Count == 0) return created;
            pendingSelectionId = created[0].id;
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
            return created;
        }

        /// <summary>Shown when a contact start is refused for missing a Head bone. A test swaps this out to avoid a real modal.</summary>
        public static Action<string> ShowContactStartError = reason => EditorUtility.DisplayDialog(L.Text("ad.ui.add.contact_starts"), reason ?? "", L.Text("ad.ui.ok"));

        // ---- the tag filter ---------------------------------------------------------------------------------

        /// <summary>Tag ids currently ticked in the filter, across every group. Read only; use ToggleTag and ClearTags to change it.</summary>
        public IReadOnlyCollection<string> ActiveTags => activeTags;

        /// <summary>
        /// Whether the ticked tags narrow the tree right now. Simple has no filter button to see or clear them with,
        /// so a filter ticked in Advanced pauses there instead of hiding rows nobody can bring back; it is kept, and
        /// narrows the tree again when Advanced does.
        /// </summary>
        bool TagsApply => !owner.Simple && activeTags.Count > 0;

        /// <summary>Every tag id present on this avatar's tree right now, from the last Build, independent of whatever the search box or the filter itself already narrowed away.</summary>
        public List<string> AvailableTags() => TagFilter.Available(allRows);

        /// <summary>Ticks or unticks one tag id and refreshes. Several ids can be ticked at once, across separate calls.</summary>
        public void ToggleTag(string tagId)
        {
            if (string.IsNullOrEmpty(tagId)) return;
            if (!activeTags.Remove(tagId)) activeTags.Add(tagId);
            TagFilterMemory.Save(tagFilterKey, activeTags);
            owner.Refresh();
        }

        /// <summary>Removes every ticked tag at once.</summary>
        public void ClearTags()
        {
            if (activeTags.Count == 0) return;
            activeTags.Clear();
            TagFilterMemory.Save(tagFilterKey, activeTags);
            owner.Refresh();
        }

        /// <summary>
        /// Rebuilds the tag filter menu items and its button text from the current tags, so the ticks and
        /// the count always match whatever is actually active: called every time the tree refreshes, since that is
        /// every moment the ticked tags or the tags this avatar's rows offer can have changed. Each group is a header with its tags under it,
        /// ticked ones shown Checked; Clear sits at the end, disabled while nothing is ticked.
        /// </summary>
        void RefreshTagFilterMenu()
        {
            tagFilterLabel.text = activeTags.Count > 0
                ? string.Format(L.Text("ad.ui.tagfilter.count"), activeTags.Count)
                : L.Text("ad.ui.tagfilter");
            var items = new List<AdMenu.Item>();
            foreach (var group in TagFilter.Groups)
            {
                var tags = AvailableTags().Where(t => TagFilter.GroupOf(t) == group).ToList();
                if (tags.Count == 0) continue;
                items.Add(AdMenu.Item.Heading(TagFilter.GroupLabel(group)));
                foreach (var tag in tags)
                {
                    var id = tag;
                    items.Add(new AdMenu.Item { Text = TagFilter.TagLabel(tag), Checked = activeTags.Contains(tag), Action = () => ToggleTag(id) });
                }
            }
            if (items.Count == 0) items.Add(new AdMenu.Item { Text = L.Text("ad.ui.pick.none"), Disabled = true });
            items.Add(AdMenu.Item.Line());
            items.Add(new AdMenu.Item { Text = L.Text("ad.ui.tagfilter.clear"), Disabled = activeTags.Count == 0, Closes = true, Action = ClearTags });
            tagFilterItems = items;
        }

        /// <summary>The items the "+ More" button shows, for a test to inspect without opening the menu.</summary>
        public IReadOnlyList<AdMenu.Item> MoreMenu => moreItems;

        /// <summary>The items the tag filter button shows, for a test to inspect without opening the menu.</summary>
        public IReadOnlyList<AdMenu.Item> TagFilterMenu => tagFilterItems;

        /// <summary>
        /// Rebuilds the row of small removable chips under the search field: one per ticked tag, wrapping in a list
        /// of their own, and Clear outside that list at the row's right end, on the first line. Hidden entirely
        /// while nothing is ticked.
        /// </summary>
        void RefreshTagChips()
        {
            filterChips.Clear();
            filterChips.EnableInClassList("ad-hidden", !TagsApply);
            if (!TagsApply) return;
            var list = new VisualElement { name = "ad-filter-chip-list" };
            list.AddToClassList("ad-filter-chip-list");
            foreach (var tag in activeTags.ToList())
            {
                var chip = new VisualElement();
                chip.AddToClassList("ad-chip");
                chip.AddToClassList("ad-filter-chip");
                chip.Add(new Label(TagFilter.TagLabel(tag)));
                var remove = new Button(() => ToggleTag(tag)) { text = "×", tooltip = L.Text("ad.ui.tagfilter.chip.remove.tip") };
                remove.AddToClassList("ad-chip-remove");
                chip.Add(remove);
                list.Add(chip);
            }
            filterChips.Add(list);
            var clear = new Button(ClearTags) { text = L.Text("ad.ui.tagfilter.clear"), name = "ad-tagfilter-clear", tooltip = L.Text("ad.ui.tagfilter.clear.tip") };
            clear.AddToClassList("ad-filter-clear");
            filterChips.Add(clear);
        }

        /// <summary>Captures an icon for every entry of this component that has none yet. Public so both the background context menu action and a test call the same thing; also used as the whole-component "Capture missing icons" action.</summary>
        public int CaptureMissingIcons()
        {
            if (owner.Menu == null || Locked) return 0;
            var plan = owner.Plan ?? AvatarPlanCache.GetFor(owner.Menu);
            int made = IconBatch.CaptureMissing(owner.Menu, plan.Tree, plan.AvatarRoot);
            captureNote.text = made > 0 ? UiText.Count(made, "ad.ui.capture_missing.done.one", "ad.ui.capture_missing.done") : "";
            if (made > 0)
            {
                AvatarPlanCache.NoteSelfEdit(owner.Menu);
                AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
                owner.Refresh();
            }
            return made;
        }

        /// <summary>Shown to explain why a candidate a "Convert to AvatarDoc" click resolved to cannot be converted. A test swaps this out to avoid a real modal.</summary>
        public static Action<string> ShowUnsupportedReason = reason => EditorUtility.DisplayDialog(L.Text("ad.ui.convert"), reason ?? "", L.Text("ad.ui.ok"));

        /// <summary>The reason the last ConvertForeign call refused to convert, or null when it created something or found nothing to convert.</summary>
        public string LastConvertReason { get; private set; }

        /// <summary>
        /// Converts a Modular Avatar or VRCFury row into an entry of this component, removing the originals: a single
        /// item by its own origin component, or, for a foreign submenu row, every supported candidate found under it.
        /// Shows the reason and changes nothing when a single item's own candidate is unsupported.
        /// </summary>
        public MenuEntry ConvertForeign(TreeRow row)
        {
            LastConvertReason = null;
            if (Locked || row == null || row.Kind != RowKind.Foreign) return null;
            var plan = AvatarPlanCache.GetFor(owner.Menu);
            var candidates = ImportScanner.Scan(plan.AvatarRoot, plan.Tree);

            List<ImportCandidate> chosen;
            if (row.Node != null)
            {
                var origins = new HashSet<Component>(OriginsUnder(row.Node));
                var under = candidates.Where(c => c.OriginComponents != null && c.OriginComponents.Any(o => o != null && origins.Contains(o))).ToList();
                chosen = under.Where(c => c.Supported).ToList();
                if (chosen.Count == 0)
                {
                    var reasons = under.Where(c => !string.IsNullOrEmpty(c.Reason)).Select(c => c.Reason).Distinct().Take(3).ToList();
                    LastConvertReason = reasons.Count > 0 ? string.Join("\n\n", reasons) : L.Text("ad.import.reason.nothing_under");
                    ShowUnsupportedReason(LastConvertReason);
                    return null;
                }
            }
            else
            {
                var origin = row.Item?.Origin;
                var candidate = origin == null ? null : candidates.FirstOrDefault(c => c.OriginComponents != null && c.OriginComponents.Contains(origin));
                if (candidate == null)
                {
                    LastConvertReason = L.Text("ad.import.reason.nothing_here");
                    ShowUnsupportedReason(LastConvertReason);
                    return null;
                }
                if (!candidate.Supported)
                {
                    LastConvertReason = candidate.Reason;
                    ShowUnsupportedReason(candidate.Reason);
                    return null;
                }
                chosen = new List<ImportCandidate> { candidate };
            }

            var created = ImportApply.Apply(owner.Menu, chosen, true);
            if (created.Count == 0) return null;
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
            RevealEntry(created[0].id);
            return created[0];
        }

        /// <summary>Every origin component under a foreign submenu, recursively, for converting the whole submenu at once.</summary>
        static IEnumerable<Component> OriginsUnder(MenuNode node)
        {
            foreach (var item in node.Items)
            {
                if (item.Origin != null) yield return item.Origin;
                if (item.Submenu != null) foreach (var origin in OriginsUnder(item.Submenu)) yield return origin;
            }
        }

        public void Refresh(AvatarPlan plan)
        {
            foreach (var button in lockDisabled) button.SetEnabled(!Locked);
            var keep = Selected?.Key ?? hiddenSelectionKey;
            var (items, byId) = TreeRow.Build(plan, owner.Menu);
            rows = byId;
            allRows = byId.Values.ToList();
            componentRoot = FindComponentRoot(allRows);
            bool filtering = filter.Length > 0 || TagsApply;
            if (filtering)
            {
                items = Prune(items, Matches);
                var kept = new HashSet<int>();
                void Collect(IEnumerable<TreeViewItemData<TreeRow>> level)
                {
                    foreach (var item in level)
                    {
                        kept.Add(item.id);
                        Collect(item.children ?? Enumerable.Empty<TreeViewItemData<TreeRow>>());
                    }
                }
                Collect(items);
                foreach (var id in rows.Keys.Where(id => !kept.Contains(id)).ToList()) rows.Remove(id);
            }
            Tree.reorderable = !filtering && !Locked;
            suppressSelection = true;
            restoring = true;
            try
            {
                Tree.SetRootItems(items);
                Tree.Rebuild();
                if (filtering)
                {
                    // While filtering every matching row must be visible, so the tree is fully expanded
                    // and the saved expansion set is left untouched.
                    Tree.ExpandAll();
                }
                else
                {
                    // Every submenu and group row starts collapsed; only the rows the creator has
                    // expanded before (saved per component) are opened back up.
                    Tree.CollapseAll();
                    foreach (var row in rows.Values)
                        if (row.Key != null && expanded.Contains(row.Key)) Tree.ExpandItem(row.Id, expandAllChildren: false);
                }
            }
            finally
            {
                suppressSelection = false;
                restoring = false;
            }
            // Only after the tree holds the new rows: anything that runs between pruning the rows and handing
            // the tree its new items would leave the tree drawing rows the lookup no longer has.
            searchNote.text = !filtering ? ""
                : filter.Length > 0 && TagsApply ? L.Text("ad.info.drag_needs_no_search_or_tags")
                : TagsApply ? L.Text("ad.info.drag_needs_no_tags")
                : L.Text("ad.info.drag_needs_no_search");
            RefreshTagChips();
            RefreshTagFilterMenu();
            RefreshWelcomeCard(plan);
            lastStructure = ReadStructure();
            RefreshWheelToggle();
            int others = plan.Tree.Components.Count(c => c != owner.Menu);
            othersNote.text = others > 0 ? UiText.Count(others, "ad.ui.other_components.one", "ad.ui.other_components") : "";
            if (pendingSelectionId != null)
            {
                var wanted = pendingSelectionId;
                pendingSelectionId = null;
                RevealEntry(wanted);
            }
            else if (keep != null) RestoreSelection(keep, filtering);
            else
            {
                var initial = owner.TakeInitialSelection();
                if (initial != null) SelectEntry(initial);
                else RestoreSelection(null, filtering);
            }
        }

        /// <summary>
        /// Puts the selection a rebuild cleared back on the row with the same key, without telling anyone: nothing
        /// changed for the creator, and SelectionChanged closes the Summary panel. Only a row that is gone is a
        /// real change, and is announced. A row that a search or tag filter merely hides is gone for now: the
        /// selection is dropped, the key is kept, and the row is selected again, and announced, once the filter lets
        /// it back.
        /// </summary>
        void RestoreSelection(string key, bool filtering)
        {
            var row = RowForKey(key);
            if (row == null && key != null)
            {
                bool hiddenOnly = filtering && allRows.Any(r => r.Key == key);
                if (Selected != null) Select(null);
                hiddenSelectionKey = hiddenOnly ? key : null;
                return;
            }
            bool cameBack = row != null && Selected == null;
            suppressSelection = true;
            try
            {
                if (row != null) Tree.SetSelectionById(row.Id);
                else Tree.ClearSelection();
            }
            finally
            {
                suppressSelection = false;
            }
            SetSelected(row, notify: cameBack);
        }

        /// <summary>
        /// Diffs every row's live expansion state against the saved set and saves once if anything changed.
        /// This Unity version has no itemExpandedChanged event to react to a manual click on the tree's own
        /// arrow, so the owning view calls this from the same timer it already polls the plan version with.
        /// Silent while Refresh itself is driving expansion, or while the search filter has forced everything open.
        /// </summary>
        public void PollExpansion()
        {
            if (restoring || filter.Length > 0 || TagsApply) return;
            bool changed = false;
            foreach (var row in rows.Values)
            {
                if (row.Key == null) continue;
                bool isExpanded = Tree.IsExpanded(row.Id);
                if (isExpanded == expanded.Contains(row.Key)) continue;
                if (isExpanded) expanded.Add(row.Key);
                else expanded.Remove(row.Key);
                changed = true;
            }
            if (changed) TreeExpansion.Save(owner.ViewPrefs, expansionKey, expanded);
        }

        /// <summary>Expands or collapses a row by its stable key and saves the change at once.</summary>
        public void SetExpanded(string rowKey, bool value)
        {
            if (rowKey == null) return;
            bool changed = value ? expanded.Add(rowKey) : expanded.Remove(rowKey);
            if (changed) TreeExpansion.Save(owner.ViewPrefs, expansionKey, expanded);
            var row = RowForKey(rowKey);
            if (row == null) return;
            if (value) Tree.ExpandItem(row.Id, expandAllChildren: false);
            else Tree.CollapseItem(row.Id);
        }

        /// <summary>Opens every ancestor of an entry selected programmatically (after an add, a duplicate, a move, or a wheel click) so the selection is visible, then selects it.</summary>
        public void RevealEntry(string entryId) => RevealAncestorsOf(RowFor(entryId), () => SelectEntry(entryId));

        /// <summary>Opens the Animators group so a merge added or dropped just now is visible, then selects its row.</summary>
        public void RevealMerge(string mergeId) => RevealKey(mergeId);

        /// <summary>Opens every group above a row named by its stable key, then selects it.</summary>
        public void RevealKey(string rowKey) => RevealAncestorsOf(RowForKey(rowKey), () => SelectKey(rowKey));

        /// <summary>Shared by RevealEntry and RevealMerge: opens every ancestor of a row selected programmatically, then runs the caller's own selection.</summary>
        void RevealAncestorsOf(TreeRow row, System.Action select)
        {
            bool changed = false;
            for (var ancestor = row?.ParentRow; ancestor != null; ancestor = ancestor.ParentRow)
            {
                if (ancestor.Key == null) continue;
                changed |= expanded.Add(ancestor.Key);
                Tree.ExpandItem(ancestor.Id, expandAllChildren: false);
            }
            if (changed) TreeExpansion.Save(owner.ViewPrefs, expansionKey, expanded);
            select();
        }

        /// <summary>Whether a row satisfies both the text search and the tag filter, the search and the tags always combining with AND.</summary>
        bool Matches(TreeRow row)
        {
            if (filter.Length > 0 && RichName.Strip(row.Name ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return !TagsApply || TagFilter.Matches(row, activeTags);
        }

        /// <summary>Keeps only rows that satisfy matches, or that have a matching descendant: a row kept only for a matching descendant is marked FilterDimmed so the bound row shows dimmed, keeping the structure readable without pretending it matches itself.</summary>
        static List<TreeViewItemData<TreeRow>> Prune(List<TreeViewItemData<TreeRow>> items, Func<TreeRow, bool> matches)
        {
            var kept = new List<TreeViewItemData<TreeRow>>();
            foreach (var item in items)
            {
                var children = Prune(item.children?.ToList() ?? new List<TreeViewItemData<TreeRow>>(), matches);
                bool isMatch = matches(item.data);
                item.data.FilterDimmed = !isMatch && children.Count > 0;
                if (isMatch || children.Count > 0) kept.Add(new TreeViewItemData<TreeRow>(item.id, item.data, children));
            }
            return kept;
        }

        /// <summary>The wheel button's own click handler: flips the wheel on or off and shows the new state at once.</summary>
        void ToggleWheel()
        {
            owner.WheelShown = !owner.WheelShown;
            RefreshWheelToggle();
            owner.UpdateWheel();
        }

        /// <summary>Keeps the wheel button showing the wheel's real current state, live: the accent "on" class while it shows, the plain box otherwise.</summary>
        internal void RefreshWheelToggle() => wheelToggle.EnableInClassList("ad-toolbar-checked", owner.WheelShown);

        public IEnumerable<TreeRow> RowsOfKind(RowKind kind) => rows.Values.Where(r => r.Kind == kind);

        /// <summary>
        /// Applies a tree structure (row, parent row, index among the parent's children) to this component's
        /// entries. Where each row actually lands is decided by ResolveDropTarget, the same resolver the drag
        /// label reads: a row dropped onto one that cannot hold children (anything but an own submenu) lands
        /// beside it instead, at that row's own position under that row's own parent; dropped onto an own submenu
        /// it still goes inside. Returns false, changing nothing and naming the reason once in the tree, for a
        /// move that is genuinely impossible (a submenu into its own descendant, a target this tree cannot
        /// resolve at all) or refused outright (a locked component). This is itself a drop finishing, so it ends
        /// the drag hold on refreshes whether it is reached from the tree's own reorder event or called directly,
        /// as a test does.
        ///
        /// Before any of that, if a DropDepth was tracked for the currently selected row, that row's own entry in
        /// structure is corrected from the parent Unity's own reorder chose to the ancestor DropDepth asked for
        /// (see WalkUpToDepth): this is how a row leaves a submenu by dragging, without fighting Unity's own
        /// TreeView over which parent it decided on for everything else in the drop.
        /// </summary>
        public bool ApplyStructure(IEnumerable<(TreeRow row, TreeRow parent, int index)> structure)
        {
            Dragging = false;
            int? droppedAtDepth = dropDepth;
            var draggedRow = Selected;
            var hoverRow = dragHoverRow;
            ClearDragPreview();
            dropRefusal.Clear();
            if (Locked)
            {
                ShowDropRefused("ad.info.drop_refused.locked");
                return false;
            }
            var structureList = structure as IList<(TreeRow row, TreeRow parent, int index)> ?? structure.ToList();
            if (droppedAtDepth.HasValue && draggedRow != null && draggedRow.IsMenuEntry)
            {
                for (int i = 0; i < structureList.Count; i++)
                {
                    if (structureList[i].row != draggedRow) continue;
                    var corrected = WalkUpToDepth(structureList[i].parent, droppedAtDepth.Value);
                    if (corrected != structureList[i].parent)
                    {
                        // Unity's own index was picked under the parent it chose, not the shallower one just
                        // corrected to, so it means nothing there. The hovered row is not always a direct child of
                        // the corrected parent (dragging out of a nested submenu usually hovers something still
                        // inside it), so walk it up to whichever of its own ancestors sits directly under the
                        // corrected parent, and land right after that ancestor: that is the row the creator was
                        // actually holding the drop over, at whatever depth it was nested at. Falls to the end of
                        // the corrected parent's children when there is no hovered row (empty space) or none of its
                        // ancestors sit under the corrected parent at all. Left alone, a row corrected to a
                        // shallower parent keeps whatever index it had under the old one, which can land it first
                        // instead of wherever it was actually dropped. A drop that resolves to the same parent
                        // Unity already chose needs none of this: its own index is still exactly what Unity computed.
                        int correctedIndex = structureList.Count;
                        var ancestor = hoverRow;
                        while (ancestor != null && ancestor.ParentRow != corrected) ancestor = ancestor.ParentRow;
                        if (ancestor != null)
                        {
                            var afterAncestor = structureList.FirstOrDefault(x => x.row == ancestor);
                            if (afterAncestor.row != null) correctedIndex = afterAncestor.index + 1;
                        }
                        structureList[i] = (structureList[i].row, corrected, correctedIndex);
                    }
                    break;
                }
            }
            var moves = new List<(MenuEntry entry, string parentId, int index)>();
            var byParent = new Dictionary<string, List<(TreeRow row, int index)>>();
            foreach (var (row, parent, index) in structureList)
            {
                if (!row.IsMenuEntry) continue;
                // A More page is the rest of the menu it hangs under, not a place of its own, so a row on one
                // belongs to that menu, counted after everything on the pages before it.
                var container = ContainerOf(parent);
                // Exactly what the label itself asks for the same landing row, so the two can never name a
                // different place again: an own submenu holds the row inside it, an own toggle/radial/trigger/
                // override/preset lands the row beside it (its own parent, at its own position), and anything
                // else this tree has no menu position to put the dropped row beside at all (an existing or
                // foreign row, or a synthetic group) refuses the drop, naming why. Only the row the drop moved
                // can refuse it: any other row is under the parent it was built under, and keeps it.
                var resolved = ResolveDropTarget(container, null, componentRoot);
                string parentId;
                if (!resolved.Refused) parentId = resolved.ParentId;
                else if (row.ParentRow != parent)
                {
                    ShowDropRefused(resolved.ReasonKey);
                    return false;
                }
                else parentId = row.Entry.parentId ?? "";
                int effectiveIndex = FlatIndex(structureList, parent, index);
                if (container != null && container.IsOwnEntry && !(container.IsMenuEntry && container.Entry.kind == EntryKind.Submenu))
                {
                    var beside = structureList.FirstOrDefault(x => x.row == container);
                    if (beside.row != null) effectiveIndex = FlatIndex(structureList, beside.parent, beside.index);
                }
                if (!byParent.TryGetValue(parentId, out var list)) byParent[parentId] = list = new List<(TreeRow, int)>();
                list.Add((row, effectiveIndex));
            }
            foreach (var pair in byParent)
            {
                int siblingIndex = 0;
                foreach (var (row, _) in pair.Value.OrderBy(x => x.index))
                    moves.Add((row.Entry, pair.Key, siblingIndex++));
            }
            foreach (var (entry, parentId, index) in moves)
                if (parentId.Length > 0 && (entry.id == parentId || EntryEditing.IsDescendantOf(owner.Menu, owner.Menu.FindEntry(parentId), entry.id)))
                {
                    ShowDropRefused("ad.info.drop_refused.own_descendant");
                    return false;
                }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            foreach (var (entry, parentId, index) in moves)
            {
                if (!EntryEditing.Reorder(owner.Menu, entry, parentId, index))
                {
                    Undo.RevertAllDownToGroup(group);
                    ShowDropRefused("ad.info.drop_refused.not_a_container");
                    return false;
                }
            }
            // A move never adds, removes, renames or changes what an entry reads or writes, so the removal plan
            // and the avatar-wide parameter walk answer exactly what they did before it: only the tree itself,
            // built fresh from the new order, has anything to show for the drop.
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateOrder(owner.Plan?.AvatarRoot);
            owner.Refresh();
            return true;
        }

        /// <summary>The menu row a drop under parent really lands in: parent itself, or, for a More page, the menu the page continues (null for the root).</summary>
        static TreeRow ContainerOf(TreeRow parent)
        {
            while (parent != null && parent.Kind == RowKind.More) parent = parent.ParentRow;
            return parent;
        }

        /// <summary>
        /// A row's position within its real menu. Under a More page, index counts from that page, which starts at the
        /// slot the More row itself holds on the page before it, so the page's own position is added, page by page.
        /// </summary>
        static int FlatIndex(IList<(TreeRow row, TreeRow parent, int index)> structure, TreeRow parent, int index)
        {
            while (parent != null && parent.Kind == RowKind.More)
            {
                var page = structure.FirstOrDefault(x => x.row == parent);
                if (page.row == null) break;
                index += page.index;
                parent = page.parent;
            }
            return index;
        }

        /// <summary>Shows why the last drop, move or reorder was refused, once, in the tree. Cleared the next time one is attempted. It is an info line with a Learn more button to its entry on the Messages page.</summary>
        void ShowDropRefused(string reasonKey)
        {
            dropRefusal.Clear();
            dropRefusal.Add(DocsLinks.WithLearnMore(UiText.MessageLabel(string.Format(L.Text("ad.info.drop_refused"), L.Text(reasonKey))), "ad.info.drop_refused"));
        }

        /// <summary>Reads the tree's current (row, parent, index) shape straight from the TreeView's controller.</summary>
        List<(TreeRow row, TreeRow parent, int index)> ReadStructure()
        {
            var controller = Tree.viewController;
            var structure = new List<(TreeRow, TreeRow, int)>();
            void Walk(IEnumerable<int> ids, TreeRow parent)
            {
                int index = 0;
                foreach (var id in ids)
                {
                    if (!rows.TryGetValue(id, out var row)) continue;
                    structure.Add((row, parent, index++));
                    Walk(controller.GetChildrenIds(id), row);
                }
            }
            Walk(controller.GetRootItemIds(), null);
            return structure;
        }

        /// <summary>
        /// Fires after a drag reshapes the tree, with the id of the moved item and of its new parent. Only that item
        /// moved: every row after it, in the menu it left and the menu it joined, shifts one place as a consequence,
        /// and those neighbours can be anything (the avatar's own items, another component's rows, a More page), so
        /// they must never decide whether the drop counts. The moved row is taken from the event's own id; the diff
        /// against the structure captured at the last rebuild is only the fallback for an id this tree does not know.
        /// </summary>
        public void HandleReorder(int from, int to)
        {
            // This fires from inside the drop itself, before the pointer's own capture-out event reaches the
            // tree, so the hold a drag keeps refreshes under has to lift here rather than wait for that event.
            Dragging = false;
            var structure = ReadStructure();
            var moved = MovedRow(from, structure);
            if (moved != null && (moved.Kind == RowKind.Existing || moved.Kind == RowKind.Foreign))
            {
                var newParent = structure.First(x => x.row == moved).parent;
                if (!DropExistingOn(moved, ContainerOf(newParent))) owner.Refresh();
                return;
            }
            if (moved == null || !moved.IsMenuEntry || !ApplyStructure(structure)) owner.Refresh();
        }

        /// <summary>The row a reorder moved: the one the event names when this tree knows it and it really changed place, otherwise the first row whose parent changed, otherwise the first whose position did.</summary>
        TreeRow MovedRow(int id, List<(TreeRow row, TreeRow parent, int index)> structure)
        {
            var previous = lastStructure.ToDictionary(x => x.row, x => x.index);
            bool ParentChanged((TreeRow row, TreeRow parent, int index) x) => x.row.ParentRow != x.parent;
            bool Changed((TreeRow row, TreeRow parent, int index) x)
                => ParentChanged(x) || (previous.TryGetValue(x.row, out var before) && before != x.index);
            if (rows.TryGetValue(id, out var named))
            {
                var now = structure.FirstOrDefault(x => x.row == named);
                if (now.row != null && Changed(now)) return named;
            }
            var reparented = structure.FirstOrDefault(ParentChanged);
            if (reparented.row != null) return reparented.row;
            return structure.FirstOrDefault(Changed).row;
        }

        /// <summary>
        /// Moves an existing or foreign row into the submenu target names (own, existing or foreign), or, with target
        /// null, the avatar root. Drag events do not fire on an unattached tree, so this is exposed directly for
        /// tests and reused by the tree's own drag handling above.
        /// </summary>
        public bool DropExistingOn(TreeRow source, TreeRow target)
        {
            // This is itself a drop finishing, so it ends the drag hold on refreshes whether it is reached from
            // the tree's own reorder event or called directly, as a test does. An existing or foreign row moves by
            // path, not by depth, so DropDepth plays no part here; it is still cleared so a stale value tracked
            // during this drag cannot leak into a later, unrelated drop.
            Dragging = false;
            ClearDragPreview();
            dropRefusal.Clear();
            if (Locked)
            {
                ShowDropRefused("ad.info.drop_refused.locked");
                return false;
            }
            if (source == null || (source.Kind != RowKind.Existing && source.Kind != RowKind.Foreign)) return false;
            if (target != null && (target.Node == null || target.Kind == RowKind.More)) return false;
            var targetPath = target?.Node?.Path ?? "";
            if (!MenuEditing.MoveTo(owner.Menu, source, targetPath)) return false;
            // A move never changes what anything reads or writes, so the removal plan and the avatar-wide
            // parameter walk answer exactly what they did before it.
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateOrder(owner.Plan?.AvatarRoot);
            owner.Refresh();
            return true;
        }

        public List<MenuEntry> MoveTargets()
        {
            if (Selected == null || !Selected.IsMenuEntry) return new List<MenuEntry>();
            var blocked = new HashSet<string> { Selected.Entry.id };
            foreach (var d in EntryEditing.Descendants(owner.Menu, Selected.Entry)) blocked.Add(d.id);
            return owner.Menu.entries.Where(e => e.kind == EntryKind.Submenu && !blocked.Contains(e.id)).ToList();
        }

        public void MoveSelectedTo(string parentId)
        {
            if (Locked || Selected == null || !Selected.IsMenuEntry) return;
            var entry = Selected.Entry;
            if (!EntryEditing.MoveTo(owner.Menu, entry, parentId)) return;
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateOrder(owner.Plan?.AvatarRoot);
            owner.Refresh();
            RevealEntry(entry.id);
        }

        public void DuplicateSelected()
        {
            if (Locked || Selected == null || !Selected.IsOwnEntry) return;
            var copy = EntryEditing.Duplicate(owner.Menu, Selected.Entry);
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
            RevealEntry(copy.id);
        }

        public void DeleteSelected()
        {
            if (Locked || Selected == null) return;
            if (Selected.Kind == RowKind.Animator) { RemoveAnimator(Selected.Merge); return; }
            if (Selected.Kind == RowKind.MenuAsset) { RemoveMenuAsset(Selected.Asset); return; }
            if (Selected.Kind == RowKind.Parameter) { RemoveDeclaration(Selected.Declaration); return; }
            if (!Selected.IsOwnEntry) return;
            var entry = Selected.Entry;
            string fallback;
            if (entry.IsRule)
            {
                // Rules are not counted as siblings by EntryEditing.Siblings, so their own neighbours
                // of the same kind stand in for the sibling-based fallback below.
                var rules = owner.Menu.entries.Where(e => e.kind == entry.kind).ToList();
                int at = rules.IndexOf(entry);
                var next = at + 1 < rules.Count ? rules[at + 1] : at > 0 ? rules[at - 1] : null;
                fallback = next != null ? next.id : null;
            }
            else
            {
                var siblings = EntryEditing.Siblings(owner.Menu, entry.parentId);
                int at = siblings.IndexOf(entry);
                var next = at + 1 < siblings.Count ? siblings[at + 1] : at > 0 ? siblings[at - 1] : null;
                fallback = next != null ? next.id : entry.parentId;
            }
            if (EntryEditing.Delete(owner.Menu, entry)) AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
            SelectEntry(string.IsNullOrEmpty(fallback) ? null : fallback);
        }

        /// <summary>The items the right click menu shows for a row, or null when it offers none (a locked component, a group row, a row that is not this component's own).</summary>
        public List<AdMenu.Item> RowMenu(TreeRow row)
        {
            // Every action this menu offers is an edit, so a locked component hides the whole menu rather than
            // disabling it item by item.
            if (row == null || Locked) return null;
            if (row.Kind == RowKind.Existing || row.Kind == RowKind.Foreign) return ExistingRowMenu(row);
            // Delete finds its row again by key when chosen, the way RowActions does: the merge, asset or
            // declaration object read now can be a stale copy by then.
            if (row.Kind == RowKind.Animator || row.Kind == RowKind.MenuAsset || row.Kind == RowKind.Parameter)
            {
                if (!row.Mine) return null;
                SelectKey(row.Key);
                return DeleteOnly(() => { SelectKey(row.Key); DeleteSelected(); });
            }
            if (!row.IsOwnEntry) return null;
            SelectEntry(row.EntryId);
            // RowActions is the single source: Rename, Duplicate and Move to in the order it yields them, Move to
            // expanded into a real submenu, then Delete last behind its own separator, the same place for every row kind.
            var items = new List<AdMenu.Item>();
            foreach (var (labelKey, _, run) in RowActions(row))
            {
                if (labelKey == "ad.ui.menu.move") { items.Add(new AdMenu.Item { Text = L.Text("ad.ui.menu.move"), Children = MoveItems(row) }); continue; }
                if (labelKey == "ad.ui.menu.delete") { AddDelete(items, run); continue; }
                items.Add(new AdMenu.Item { Text = L.Text(labelKey), Action = run });
            }
            return items;
        }

        List<AdMenu.Item> MoveItems(TreeRow row)
        {
            var items = new List<AdMenu.Item>
            {
                new AdMenu.Item { Text = L.Text("ad.ui.menu.move.root"), Checked = row.Entry.parentId.Length == 0, Disabled = row.Entry.parentId.Length == 0, Action = () => MoveSelectedTo("") },
            };
            foreach (var target in MoveTargets())
            {
                var id = target.id;
                bool here = row.Entry.parentId == id;
                items.Add(new AdMenu.Item { Text = target.PlainName, Checked = here, Disabled = here, Action = () => MoveSelectedTo(id) });
            }
            return items;
        }

        static List<AdMenu.Item> DeleteOnly(Action run)
        {
            var items = new List<AdMenu.Item>();
            AddDelete(items, run);
            return items;
        }

        /// <summary>Appends Delete behind its own separator, with the Delete key shown as its shortcut: the one place Delete is added to any row's right click menu, so every row kind shows the same hint for the same key.</summary>
        static void AddDelete(List<AdMenu.Item> items, Action run)
        {
            if (items.Count > 0) items.Add(AdMenu.Item.Line());
            items.Add(new AdMenu.Item { Text = L.Text("ad.ui.menu.delete"), Shortcut = L.Text("ad.ui.shortcut.delete"), Action = run });
        }

        /// <summary>Move, hide or show, and reset for an existing or foreign row.</summary>
        List<AdMenu.Item> ExistingRowMenu(TreeRow row)
        {
            SelectKey(row.Key);
            var items = new List<AdMenu.Item>();
            if (row.Kind == RowKind.Foreign)
            {
                items.Add(new AdMenu.Item { Text = L.Text("ad.ui.convert"), Action = () => ConvertForeign(row) });
                items.Add(AdMenu.Item.Line());
            }
            var moves = new List<AdMenu.Item>();
            if (!MenuEditing.RefusesMove(row, ""))
                moves.Add(new AdMenu.Item { Text = L.Text("ad.ui.menu.move.root"), Action = () => MoveExisting(row, "") });
            foreach (var node in owner.Plan.Tree.AllNodes())
            {
                if (node.Path.Length == 0 || node.IsMorePage) continue;
                if (MenuEditing.RefusesMove(row, node.Path)) continue;
                var path = node.Path;
                moves.Add(new AdMenu.Item { Text = path.Replace("/", " \u203a "), Action = () => MoveExisting(row, path) });
            }
            if (moves.Count > 0)
            {
                items.Add(new AdMenu.Item { Text = L.Text("ad.ui.menu.move"), Children = moves });
                items.Add(AdMenu.Item.Line());
            }
            items.Add(new AdMenu.Item { Text = L.Text(row.Hidden ? "ad.ui.existing.show" : "ad.ui.existing.hide"), Action = () => SetExistingHidden(row, !row.Hidden) });
            items.Add(new AdMenu.Item { Text = L.Text("ad.ui.existing.reset"), Disabled = !row.Edited, Action = () => ResetExisting(row) });
            return items;
        }

        void MoveExisting(TreeRow row, string path)
        {
            if (Locked) return;
            if (MenuEditing.MoveTo(owner.Menu, row, path)) AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateOrder(owner.Plan?.AvatarRoot);
            owner.Refresh();
        }

        void SetExistingHidden(TreeRow row, bool hidden)
        {
            if (Locked) return;
            if (MenuEditing.SetHidden(owner.Menu, row, hidden)) AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
        }

        void ResetExisting(TreeRow row)
        {
            if (Locked) return;
            if (MenuEditing.Reset(owner.Menu, row)) AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
        }
    }

    /// <summary>Formats a model message for the inspector: the English text with objects shown by name.</summary>
    public static class UiText
    {
        /// <summary>A label for a warning, error or info text that the creator can select with the mouse and copy, and that offers Copy on right click.</summary>
        public static Label MessageLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("ad-message-text");
            label.selection.isSelectable = true;
            label.RegisterCallback<PointerDownEvent>(e =>
            {
                if (!MenuTreeView.IsMenuClick(e)) return;
                AdMenu.Open(new Rect(e.position, Vector2.zero), label, new[] { new AdMenu.Item { Text = L.Text("ad.ui.copy"), Action = () => EditorGUIUtility.systemCopyBuffer = label.text } });
                e.PreventDefault();
                e.StopPropagation();
            });
            return label;
        }

        public static string Format(ModelMessage message)
        {
            var args = (message.Args ?? new object[0]).Select(a => a is UnityEngine.Object o ? (o != null ? o.name : "") : a?.ToString() ?? "").ToArray();
            try { return string.Format(L.Text(message.Key), args); }
            catch (FormatException) { return L.Text(message.Key); }
        }

        /// <summary>Formats a count with its real singular or plural text: never a bare "(s)" suffix. Both keys take the count as {0}.</summary>
        public static string Count(int n, string singularKey, string pluralKey) => string.Format(L.Text(n == 1 ? singularKey : pluralKey), n);

        /// <summary>The key to say a count of n with: the ".one" twin of key for one, key itself otherwise. For a text whose count is not {0}, and for a message, which names its key.</summary>
        public static string CountKey(int n, string key) => n == 1 ? key + ".one" : key;
    }
}
#endif
