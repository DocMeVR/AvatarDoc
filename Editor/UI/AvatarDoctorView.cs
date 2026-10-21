#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>The whole AvatarDoctor editor for one component: header, merged menu tree, detail panel.</summary>
    public sealed class AvatarDoctorView : VisualElement
    {
        /// <summary>Entry id the tree selects when the view is first shown (used by tooling and tests).</summary>
        public static string InitialSelectionId;

        /// <summary>Entries a view of this component reveals on its next refresh, even one already open elsewhere (a window, another inspector). Set by an external change such as an import.</summary>
        static readonly Dictionary<AvatarDoc, string> pendingReveals = new Dictionary<AvatarDoc, string>();

        public static int PendingRevealCount => pendingReveals.Count;

        /// <summary>
        /// Raised when a view's mode switch is pressed, as a signal and nothing more: every other open AvatarDoc view
        /// reads its own store again and follows when the choice there differs. A view listens only while it is on a panel.
        /// </summary>
        public static event System.Action ModeChanged;

        /// <summary>How many views are listening to ModeChanged right now. Read by the tests that check a view lets go when it leaves its panel.</summary>
        public static int ModeListenerCount => ModeChanged?.GetInvocationList().Length ?? 0;

        /// <summary>Asks any open view of this component to select and reveal this entry the next time it refreshes.</summary>
        public static void RequestReveal(AvatarDoc menu, string entryId)
        {
            if (menu == null || string.IsNullOrEmpty(entryId)) return;
            DropGoneReveals();
            pendingReveals[menu] = entryId;
        }

        /// <summary>A component destroyed since it asked will never refresh a view, so its request is let go of instead of keeping the dead component in this list for the rest of the session.</summary>
        static void DropGoneReveals()
        {
            foreach (var gone in pendingReveals.Keys.Where(m => m == null).ToList()) pendingReveals.Remove(gone);
        }

        public readonly SerializedObject SerializedObject;
        public readonly AvatarDoc Menu;
        public AvatarPlan Plan { get; private set; }
        public HeaderView Header { get; private set; }
        /// <summary>The row of the tree and the detail panel, built again with every other part on a language change.</summary>
        public VisualElement Content { get; private set; }
        public VisualElement Left { get; private set; }
        public readonly List<VisualElement> Refreshables = new List<VisualElement>();
        public MenuTreeView Tree { get; private set; }
        public EntryDetailView Detail { get; private set; }
        public SummaryPanel Summary { get; private set; }
        /// <summary>
        /// Where first-use tips read whether they were dismissed and turn them all back on. Defaults to an
        /// in-memory store that never touches the real EditorPrefs, the way a detached view keeps its own test
        /// mode in a field instead of the real one: the two real hosts (AvatarDocEditor, AvatarDoctorWindow) hand
        /// this a real, EditorPrefs backed store right after construction; nothing a test constructs ever does.
        /// </summary>
        public ITipStore TipStore { get; set; } = new MemoryTipStore();
        /// <summary>
        /// Where this view's own remembered preferences (fold state, tree expansion) are read and written. Unlike
        /// TipStore this cannot be a settable property: the tree loads its saved expansion and the header its fold
        /// state while THIS constructor is still running (building Header and Tree below), so it has to be known
        /// before either of them exists. Defaults to an in-memory store nothing a test constructs ever escapes;
        /// the two real hosts (AvatarDocEditor, AvatarDoctorWindow) pass a real, EditorPrefs backed one instead.
        /// </summary>
        public IViewPrefs ViewPrefs { get; }

        /// <summary>Simple or Advanced, read from the person's one preference when the view is built and changed by SetMode.</summary>
        public ViewMode Mode { get; private set; }

        /// <summary>Whether the view shows only what most avatars need. Tools are left out of Simple; nothing the creator made ever is.</summary>
        public bool Simple => Mode == ViewMode.Simple;

        /// <summary>The Simple and Advanced switch at the very top of the view.</summary>
        public SegmentedChoice ModeChoice { get; private set; }

        /// <summary>The language picker at the right end of the switch's row, shown in both modes.</summary>
        public LanguagePicker LanguagePicker { get; private set; }

        /// <summary>The L.LanguageVersion the parts were last built in. A view that was off its panel during a change builds again on its next poll.</summary>
        int builtLanguage;

        /// <summary>How many times the parts were built again for a language change. Read by the tests.</summary>
        public int Rerenders { get; private set; }

        VisualElement lockBanner;
        Label lockBannerText;

        readonly bool wide;
        readonly PublishedValueWatch<ImmutableDictionary<string, float>> overridesWatch;
        /// <summary>The shortest gap between two scheduled refreshes while changes keep arriving.</summary>
        const int RefreshFloorMs = 100;

        /// <summary>How long a drag can hold Dragging with the tree's pointer capture already gone before the poller treats it as a leak rather than a genuine hold.</summary>
        const double StuckDragSeconds = 2.0;
        int shownVersion = -1;
        WheelView wheel;
        bool refreshPending;
        /// <summary>Set when a refresh was asked for while a row drag held the tree's pointer, so the poller knows to run it once the drag ends instead of waiting for another change to ask again.</summary>
        bool refreshHeldByDrag;
        /// <summary>Set when a refresh was asked for while a menu of this view was open.</summary>
        bool refreshHeldByMenu;
        int menuHolds;
        int menuHoldSuspended;
        double lastRefresh = -1000.0;
        IVisualElementScheduledItem pending;
        IVisualElementScheduledItem poller;
        string pendingInitialSelection;

        public AvatarDoctorView(SerializedObject serializedObject, AvatarDoc menu, bool wide = false, IViewPrefs viewPrefs = null)
        {
            SerializedObject = serializedObject;
            Menu = menu;
            this.wide = wide;
            ViewPrefs = viewPrefs ?? new MemoryViewPrefs();
            Mode = ViewModes.Load(ViewPrefs);
            pendingInitialSelection = InitialSelectionId;
            AddToClassList("ad-root");
            AddToClassList(EditorGUIUtility.isProSkin ? "ad-dark" : "ad-light");
            if (wide)
            {
                AddToClassList("ad-wide");
                AddToClassList("ad-window");
            }
            var uss = LoadStyleSheet();
            if (uss != null) styleSheets.Add(uss);

            if (EntryEditing.EnsureUniqueIds(menu, OthersOf(menu)) > 0) AvatarPlanCache.InvalidateMenu(AvatarPlanCache.RootOf(menu));

            BuildParts();

            // The poller only has to run while the inspector is on screen, so it is paused with the panel.
            overridesWatch = new PublishedValueWatch<ImmutableDictionary<string, float>>(PreviewState.Overrides, OnOverridesChanged);
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (poller == null) poller = schedule.Execute(CheckVersion).Every(250);
                else poller.Resume();
                overridesWatch.Start();
                AvatarPlanCache.Changed += OnCacheChanged;
                ModeChanged -= OnModeChangedElsewhere;
                ModeChanged += OnModeChangedElsewhere;
                // A language chosen while this view was off screen is caught by the poller's first run instead of
                // here: building the parts again inside their own attach would change the tree being attached.
                L.LanguageChanged -= OnLanguageChanged;
                L.LanguageChanged += OnLanguageChanged;
                // A switch pressed while this view was off screen never reached it, so it reads the person's
                // choice again before it shows.
                var stored = ViewModes.Load(ViewPrefs);
                if (stored != Mode) ApplyMode(stored);
                // A refresh asked for while there was no panel to schedule it on never ran, so the flag that
                // holds the next one back has to be let go of here.
                refreshPending = false;
                UpdateWheel();
                TouchTest.NoteView(this, true);
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                poller?.Pause();
                overridesWatch.Stop();
                AvatarPlanCache.Changed -= OnCacheChanged;
                ModeChanged -= OnModeChangedElsewhere;
                L.LanguageChanged -= OnLanguageChanged;
                // Nothing off screen has to redraw, and a refresh left waiting here would never run to clear itself.
                pending?.Pause();
                refreshPending = false;
                TouchTest.NoteView(this, false);
            });
            Refresh();
        }

        /// <summary>Builds every part of the view, each reading its texts in the current language and, except in English, in NDMF's font for it.</summary>
        void BuildParts()
        {
            builtLanguage = L.LanguageVersion;
            Content = new VisualElement();
            Left = new VisualElement();
            var modeRow = new VisualElement { name = "ad-mode-row" };
            modeRow.AddToClassList("ad-mode-row");
            ModeChoice = new SegmentedChoice(new List<string> { L.Text("ad.ui.mode.simple"), L.Text("ad.ui.mode.advanced") }, Simple ? 0 : 1)
                { name = "ad-mode", tooltip = L.Text("ad.ui.mode.tip") };
            ModeChoice.Changed += index => SetMode(index == 0 ? ViewMode.Simple : ViewMode.Advanced);
            modeRow.Add(ModeChoice);
            LanguagePicker = new LanguagePicker();
            modeRow.Add(LanguagePicker);
            Add(modeRow);

            lockBanner = new VisualElement { name = "ad-lock-banner" };
            lockBannerText = new Label { name = "ad-lock-banner-text" };
            lockBanner.AddToClassList("ad-lock-banner");
            lockBanner.AddToClassList("ad-hidden");
            lockBannerText.AddToClassList("ad-note");
            lockBanner.Add(lockBannerText);
            lockBanner.Add(new Button(() => SetLocked(false)) { text = L.Text("ad.ui.unlock"), name = "ad-unlock", tooltip = L.Text("ad.ui.unlock.tip") });
            Add(lockBanner);

            Header = new HeaderView(this) { name = "ad-header" };
            Add(Header);
            Content.AddToClassList("ad-content");
            Add(Content);
            Tree = new MenuTreeView(this);
            Left.AddToClassList("ad-left");
            Left.Add(Tree);
            Content.Add(Left);
            Refreshables.Add(Tree);
            Detail = new EntryDetailView(this) { name = "ad-detail" };
            Content.Add(Detail);
            Refreshables.Add(Detail);
            Summary = new SummaryPanel(this);
            Summary.AddToClassList("ad-hidden");
            Content.Add(Summary);
            Refreshables.Add(Summary);
            Tree.SelectionChanged += Detail.Show;
            Tree.SelectionChanged += _ => UpdateWheel();
            Tree.SelectionChanged += _ => CloseSummary();
            LanguageFont.ApplyToChildren(this);
        }

        void OnLanguageChanged()
        {
            if (Menu == null) return;
            // A rebuild would drop a drag the tree holds; the poller builds again once the drag ends.
            if (Tree.Dragging) return;
            Rerender();
        }

        /// <summary>
        /// Builds every part again, so each label, tooltip and note reads in the current language. What the creator was
        /// looking at stays: the selected row, the search, the Summary and its format, the mode, and the folds and open rows
        /// the view's store keeps. A menu of this view is closed first, since its rows were made in the old language. A
        /// language chosen with the keyboard leaves the focus on the new picker, so the keyboard can go on from there.
        /// </summary>
        public void Rerender()
        {
            if (Menu == null) return;
            Tree.PollExpansion();
            var selectedKey = Tree.Selected?.Key;
            var search = Tree.Q<TextField>("ad-search")?.value ?? "";
            bool summaryOpen = SummaryOpen;
            var summaryFormat = Summary.Format;
            // The focus a closing menu hands back is still on its way while that menu's key press is being handled, so a
            // picker whose own list is choosing the language counts as focused.
            bool pickerFocused = LanguagePicker != null && LanguagePicker.IsChoosing;
            if (panel != null)
            {
                foreach (var menu in panel.visualTree.Query<AdMenu>().ToList())
                    if (menu.Owner == this) menu.Close(false);
                var focused = panel.focusController?.focusedElement as VisualElement;
                pickerFocused |= focused != null && LanguagePicker != null && (focused == LanguagePicker || LanguagePicker.Contains(focused));
                if (focused != null && Contains(focused)) focused.Blur();
            }
            Clear();
            Refreshables.Clear();
            wheel = null;
            SummaryOpen = false;
            BuildParts();
            Rerenders++;
            Refresh();
            if (search.Length > 0)
            {
                var field = Tree.Q<TextField>("ad-search");
                if (field != null) field.value = search;
            }
            if (selectedKey != null && Tree.RowForKey(selectedKey) != null) Tree.SelectKey(selectedKey);
            if (summaryFormat != Summary.Format) Summary.SetFormat(summaryFormat);
            if (summaryOpen) OpenSummary();
            if (pickerFocused) LanguagePicker.Focus();
        }

        /// <summary>
        /// Switches this view to a mode, remembers it for the person in this view's own store, and tells every other
        /// open AvatarDoc view to read its store again. Only what the views show changes: the plan is not built again,
        /// and nothing in the component is written. Only the view that was pressed saves.
        /// </summary>
        public void SetMode(ViewMode mode)
        {
            if (mode == Mode) return;
            ViewModes.Save(ViewPrefs, mode);
            ApplyMode(mode);
            ModeChanged?.Invoke();
        }

        void OnModeChangedElsewhere()
        {
            if (Menu == null) return;
            var stored = ViewModes.Load(ViewPrefs);
            if (stored != Mode) ApplyMode(stored);
        }

        void ApplyMode(ViewMode mode)
        {
            Mode = mode;
            ModeChoice.SetIndexWithoutNotify(mode == ViewMode.Simple ? 0 : 1);
            Header.ApplyMode();
            if (Tree.ApplyMode()) Refresh();
            Detail.ApplyMode();
            UpdateWheel();
        }

        /// <summary>An override set somewhere else, by the other host of this view or by a keyboard action, reaches the header, the detail's preview row and the wheel's wedge fills.</summary>
        void OnOverridesChanged(ImmutableDictionary<string, float> _)
        {
            if (Menu == null || Plan == null) return;
            Header.RefreshPreviewIndicator();
            Detail.RefreshPreview();
            wheel?.RefreshWedgeStates();
        }

        public SerializedProperty EntryProperty(MenuEntry entry)
        {
            int index = Menu.entries.IndexOf(entry);
            return index < 0 ? null : SerializedObject.FindProperty(nameof(AvatarDoc.entries)).GetArrayElementAtIndex(index);
        }

        public SerializedProperty MergeProperty(AnimatorMerge merge)
        {
            int index = Menu.animators.IndexOf(merge);
            return index < 0 ? null : SerializedObject.FindProperty(nameof(AvatarDoc.animators)).GetArrayElementAtIndex(index);
        }

        public SerializedProperty MenuAssetProperty(MenuAssetInstall install)
        {
            int index = Menu.menuAssets.IndexOf(install);
            return index < 0 ? null : SerializedObject.FindProperty(nameof(AvatarDoc.menuAssets)).GetArrayElementAtIndex(index);
        }

        public SerializedProperty DeclarationProperty(ParameterDeclaration declaration)
        {
            int index = Menu.parameters.IndexOf(declaration);
            return index < 0 ? null : SerializedObject.FindProperty(nameof(AvatarDoc.parameters)).GetArrayElementAtIndex(index);
        }

        public void ApplyAndRefresh()
        {
            SerializedObject.ApplyModifiedProperties();
            AvatarPlanCache.NoteSelfEdit(Menu);
            AvatarPlanCache.InvalidateMenu(Plan != null ? Plan.AvatarRoot : AvatarPlanCache.RootOf(Menu));
            Refresh();
        }

        /// <summary>
        /// Writes a change that only alters how something reads: a name that no parameter is made from, an icon.
        /// The plan still stands, so the tree row, the wheel and the header are redrawn from it without a rebuild.
        /// </summary>
        public void ApplyCosmetic()
        {
            SerializedObject.ApplyModifiedProperties();
            AvatarPlanCache.NoteSelfEdit(Menu);
            AvatarPlanCache.NoteCosmetic(Plan != null ? Plan.AvatarRoot : null);
            Refresh();
        }

        /// <summary>Whether this component is locked by its maker. Read by the tree and the detail panel so they know to disable themselves.</summary>
        public bool Locked => Menu != null && Menu.locked;

        /// <summary>Sets AvatarDoc.locked through the SerializedObject, like every other component level field, and refreshes the whole view: locking or unlocking changes what the tree and the detail panel let the author touch.</summary>
        public void SetLocked(bool value)
        {
            SerializedObject.Update();
            var property = SerializedObject.FindProperty(nameof(AvatarDoc.locked));
            if (property.boolValue == value) return;
            property.boolValue = value;
            ApplyAndRefresh();
        }

        /// <summary>Whether the summary panel is open in place of the detail panel.</summary>
        public bool SummaryOpen { get; private set; }

        /// <summary>The last built summary text, in the format currently chosen. Read by tests.</summary>
        public string SummaryText => Summary.Text;

        public void OpenSummary()
        {
            if (SummaryOpen) return;
            SummaryOpen = true;
            // A row that a search hides is selected again when the search ends; with the Summary open that would close it.
            Tree.ForgetHiddenSelection();
            Detail.AddToClassList("ad-hidden");
            Summary.RemoveFromClassList("ad-hidden");
            Header.RefreshSummaryToggle(SummaryOpen);
            Summary.Refresh(Plan);
        }

        public void CloseSummary()
        {
            if (!SummaryOpen) return;
            SummaryOpen = false;
            Summary.AddToClassList("ad-hidden");
            Detail.RemoveFromClassList("ad-hidden");
            Header.RefreshSummaryToggle(SummaryOpen);
        }

        public void ToggleSummary()
        {
            if (SummaryOpen) CloseSummary(); else OpenSummary();
        }

        public void SetSummaryFormat(SummaryFormat format) => Summary.SetFormat(format);

        /// <summary>
        /// Writes an edit that arrives per pointer move (a slider drag). Nothing is rebuilt under the pointer: the
        /// change event Unity publishes at the end of the frame drops the plan, and the refresh follows a frame later.
        /// </summary>
        public void ApplyLive()
        {
            SerializedObject.ApplyModifiedProperties();
            ScheduleRefresh();
        }

        /// <summary>
        /// Whether a rename of this entry leaves the plan as it is. An automatic parameter name is made from the
        /// entry's name, a submenu's name is part of every path below it, a group member's name reaches the group's
        /// own parameter, a preset panel labels its states by the control's path, and a message about the entry has
        /// its plain name baked into its arguments. Only a plainly labelled toggle or radial that nothing complains
        /// about and no preset names renames for free.
        /// </summary>
        public bool RenameIsCosmetic(MenuEntry entry)
        {
            if (entry == null) return false;
            if (entry.kind != EntryKind.Toggle && entry.kind != EntryKind.Radial) return false;
            if (string.IsNullOrWhiteSpace(entry.parameterName)) return false;
            if (!string.IsNullOrWhiteSpace(entry.exclusiveGroup)) return false;
            if (Plan != null && Plan.Tree != null && Plan.Tree.Components.Any(c => c != null && c.entries.Any(e => e.presetStates.Any(s => s.entryId == entry.id)))) return false;
            return Plan == null || !Plan.MessagesFor(Menu, entry).Any();
        }

        void OnCacheChanged(Transform avatarRoot)
        {
            if (Menu == null) return;
            if (avatarRoot != null && Plan != null && Plan.AvatarRoot != null && Plan.AvatarRoot != avatarRoot) return;
            ScheduleRefresh();
        }

        public static IEnumerable<AvatarDoc> OthersOf(AvatarDoc menu)
        {
            var root = AvatarPlanCache.RootOf(menu);
            if (root == null) return Enumerable.Empty<AvatarDoc>();
            return root.GetComponentsInChildren<AvatarDoc>(true).Where(m => m != menu);
        }

        /// <summary>Whether a refresh was asked for while a row drag held the tree's pointer and is still waiting for the drag to end. Public so a test can see the hold take effect without a real mouse or a real timer.</summary>
        public bool RefreshHeldByDrag => refreshHeldByDrag;

        /// <summary>
        /// Whether one of this view's menus is open and holding its refreshes. A refresh rebuilds the tree's rows
        /// and the entry panel, and so would pull out the row or button the menu was opened on; it waits instead.
        /// </summary>
        public bool MenuOpen => menuHolds > 0 && menuHoldSuspended == 0;

        /// <summary>Whether a refresh was asked for while a menu was open and still waits to run.</summary>
        public bool RefreshHeldByMenu => refreshHeldByMenu;

        internal void HoldForMenu() => menuHolds++;

        /// <summary>The Filter menu's own ticks refresh on purpose: the hold steps aside around that one action.</summary>
        internal void SuspendMenuHold() => menuHoldSuspended++;

        internal void ResumeMenuHold()
        {
            if (menuHoldSuspended > 0) menuHoldSuspended--;
        }

        /// <summary>
        /// Lets go of one menu's hold. When it was the last: a chosen item gets the held refresh right away, so it
        /// acts on current rows; any other close leaves the refresh to the next tick, since it may be closing from
        /// inside the very event (a detach, a resize) that a rebuild would disturb.
        /// </summary>
        internal void ReleaseMenuHold(bool chosen, bool byItself)
        {
            if (menuHolds == 0) return;
            if (--menuHolds > 0) return;
            menuHoldSuspended = 0;
            if (panel == null) return;
            if (chosen)
            {
                if (refreshHeldByMenu) Refresh();
                Detail.RunPendingRebuild();
                return;
            }
            if (!byItself) Detail.RunPendingRebuild();
            // The held refresh rebuilds the entry panel itself when its row changed; a panel rebuild of its own
            // would show the old plan for one tick.
            if (refreshHeldByMenu) ScheduleRefresh();
            else if (byItself) schedule.Execute(Detail.RunPendingRebuild);
        }

        /// <summary>Lets go of one menu's hold while this view leaves its panel: nothing is refreshed or rebuilt now, and a held refresh runs from the poller once the view is shown again.</summary>
        internal void DropMenuHold()
        {
            if (menuHolds > 0) menuHolds--;
            if (menuHolds == 0) menuHoldSuspended = 0;
        }

        /// <summary>Whether a menu of this view is still open in its panel. A window that cleared its root without closing the menu would otherwise leave the hold stuck.</summary>
        bool AnyMenuOfMineOpen()
        {
            var root = panel?.visualTree;
            return root != null && root.Query<AdMenu>().ToList().Any(m => !m.IsClosed && m.Owner == this);
        }

        /// <summary>Saves the expansion state and refreshes if what the view shows is older than the cache. The poller calls it; a second view can be brought up to date with it directly. Held back, same as Refresh itself, while a row drag has the tree's pointer captured, so a rebuild landing mid-drag cannot drop it, and while one of the view's menus is open. It also lets go of a menu hold whose menu is no longer in the panel.</summary>
        public void CheckVersion()
        {
            // A row opened or closed by hand is only noticed here, so it is saved before a plan change rebuilds
            // the tree: the other way round the refresh would wipe a click made in the moment before it.
            Tree.PollExpansion();
            if (Tree.Dragging)
            {
                // An unpaired pointer capture (a modal opening mid-drag, for instance) would otherwise leave
                // Dragging stuck true for the rest of the session and hold every refresh back with it. A drag
                // that still genuinely holds the tree's pointer is left alone no matter how long it runs.
                Tree.ClearIfStuck(StuckDragSeconds);
            }
            if (Tree.Dragging)
            {
                if (AvatarPlanCache.Version != shownVersion) refreshHeldByDrag = true;
                return;
            }
            if (menuHolds > 0 && !AnyMenuOfMineOpen())
            {
                menuHolds = 0;
                menuHoldSuspended = 0;
                Detail.RunPendingRebuild();
            }
            if (builtLanguage != L.LanguageVersion && Menu != null)
            {
                Rerender();
                return;
            }
            if (MenuOpen)
            {
                if (AvatarPlanCache.Version != shownVersion) refreshHeldByMenu = true;
                return;
            }
            if (refreshHeldByDrag || refreshHeldByMenu || AvatarPlanCache.Version != shownVersion) Refresh();
        }

        /// <summary>
        /// Refreshes on the next frame, once, however many times it is asked for in between. While changes keep
        /// arriving (a drag publishes one per frame) the refreshes are held to one per RefreshFloorMs. Held back
        /// entirely, without even scheduling, while a row drag has the tree's pointer captured or a menu is open.
        /// </summary>
        public void ScheduleRefresh()
        {
            if (Tree.Dragging) { refreshHeldByDrag = true; return; }
            if (MenuOpen) { refreshHeldByMenu = true; return; }
            if (refreshPending) return;
            refreshPending = true;
            long wait = (long)(RefreshFloorMs - (EditorApplication.timeSinceStartup - lastRefresh) * 1000.0);
            pending = wait > 0 ? schedule.Execute(RunScheduledRefresh).StartingIn(wait) : schedule.Execute(RunScheduledRefresh);
        }

        void RunScheduledRefresh()
        {
            refreshPending = false;
            Refresh();
        }

        /// <summary>Hands out the pending initial selection once, then forgets it. The static field itself is left alone for callers that set and clear it.</summary>
        public string TakeInitialSelection()
        {
            var value = pendingInitialSelection;
            pendingInitialSelection = null;
            return value;
        }

        public void Refresh()
        {
            if (Menu == null) return;
            // A rebuild landing while a row drag has the tree's pointer captured drops the drag, so it is held
            // back until the drag ends: HandleReorder, the drop's own refresh, clears Dragging before calling
            // here, so it always runs regardless.
            if (Tree.Dragging) { refreshHeldByDrag = true; return; }
            // An edit saved while a menu is open (a typed name committing as the menu takes the focus) asks for a
            // refresh here directly, so the hold has to be in Refresh itself, not only in the scheduled paths.
            if (MenuOpen) { refreshHeldByMenu = true; return; }
            refreshHeldByDrag = false;
            refreshHeldByMenu = false;
            // This Unity TreeView version has no itemExpandedChanged event, so a click on a row's disclosure
            // arrow is only noticed by the poller. An edit can rebuild the tree well before that poll runs, and
            // rebuilding from the saved expansion set at that point would throw the just-opened row away, so
            // every refresh, not only the poller's own, saves the tree's live expansion state first.
            Tree.PollExpansion();
            lastRefresh = EditorApplication.timeSinceStartup;
            SerializedObject.Update();
            // Timed stage by stage: a refresh that takes longer than a person notices writes one console line
            // saying where the time went, since the creator's own avatar is the only place a slow one shows up.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int planBuilds = AvatarPlanCache.PlanBuilds;
            Plan = AvatarPlanCache.GetFor(Menu);
            bool planBuilt = AvatarPlanCache.PlanBuilds != planBuilds;
            shownVersion = AvatarPlanCache.Version;
            double planMs = clock.Elapsed.TotalMilliseconds;
            Header.Refresh(Plan);
            RefreshLockBanner();
            double headerMs = clock.Elapsed.TotalMilliseconds - planMs;
            double treeMs = 0, detailMs = 0, otherMs = 0;
            foreach (var element in Refreshables)
            {
                if (!(element is IPlanRefreshable refreshable)) continue;
                double before = clock.Elapsed.TotalMilliseconds;
                refreshable.Refresh(Plan);
                double took = clock.Elapsed.TotalMilliseconds - before;
                if (element == Tree) treeMs += took;
                else if (element == Detail) detailMs += took;
                else otherMs += took;
            }
            if (pendingReveals.Count > 0) DropGoneReveals();
            if (pendingReveals.TryGetValue(Menu, out var revealId))
            {
                pendingReveals.Remove(Menu);
                // The entry may be gone by now, undone while no view of this component was open. Asking the tree
                // for a row it does not have would clear the creator's selection and empty the detail panel.
                if (Tree.RowFor(revealId) != null) Tree.RevealEntry(revealId);
            }
            UpdateWheel();
            double totalMs = clock.Elapsed.TotalMilliseconds;
            if (totalMs >= SlowRefreshThreshold)
                SlowRefreshLog(string.Format(L.Text("ad.log.slow_refresh"), Menu.gameObject.name, totalMs.ToString("0"), planMs.ToString("0"), headerMs.ToString("0"), treeMs.ToString("0"), detailMs.ToString("0"), otherMs.ToString("0"), planBuilt ? Plan.BuildReason : L.Text("ad.log.plan_reused")));
        }

        /// <summary>A refresh at or above this many milliseconds is reported in the console with its stage times.</summary>
        public const double SlowRefreshMs = 250;

        /// <summary>The threshold the slow refresh line uses, SlowRefreshMs unless a test lowers it to see the line.</summary>
        public static double SlowRefreshThreshold = SlowRefreshMs;

        /// <summary>Where the slow refresh line goes: the console. A test hands it a list of its own.</summary>
        public static System.Action<string> SlowRefreshLog = message => Debug.Log(message);

        /// <summary>Shows or hides the lock banner at the top of the view and fills in the maker's note, if any.</summary>
        void RefreshLockBanner()
        {
            lockBanner.EnableInClassList("ad-hidden", !Locked);
            if (!Locked) return;
            var note = (Menu.lockNote ?? "").Trim();
            lockBannerText.text = note.Length == 0 ? L.Text("ad.ui.locked") : L.Text("ad.ui.locked") + " " + note;
        }

        /// <summary>Whether the wheel is shown, from the view's preferences, which the toolbar toggle flips (EditorPrefs in the real hosts). While the view is not attached to a panel, reports true so headless tests see the wheel.</summary>
        internal bool WheelShown
        {
            get => panel == null || ViewPrefs.GetBool(WheelPrefKey, wide);
            set => ViewPrefs.SetBool(WheelPrefKey, value);
        }

        string WheelPrefKey => wide ? "DocMe.AvatarDoctor.Wheel.Window" : "DocMe.AvatarDoctor.Wheel.Inspector";

        /// <summary>Whether a wedge click drives the scene preview instead of selecting the row. Off by default, remembered in the view's preferences with the other wheel preferences (EditorPrefs in the real hosts). A detached view (a test) never reads or writes that
        /// key: it keeps its own choice in a field instead, off unless a test sets it.</summary>
        public bool TestMode
        {
            get => panel == null ? detachedTestMode : ViewPrefs.GetBool(TestModePrefKey, false);
            set
            {
                if (panel == null) detachedTestMode = value;
                else ViewPrefs.SetBool(TestModePrefKey, value);
                if (wheel != null) UpdateWheel();
            }
        }

        bool detachedTestMode;

        string TestModePrefKey => wide ? "DocMe.AvatarDoctor.Wheel.Test.Window" : "DocMe.AvatarDoctor.Wheel.Test.Inspector";

        /// <summary>The wheel, made the first time anything asks for it: a view whose wheel is switched off never pays for one.</summary>
        public WheelView Wheel
        {
            get
            {
                if (wheel != null) return wheel;
                wheel = new WheelView();
                Left.Add(wheel);
                wheel.WedgeClicked += w => { if (w.EntryId != null) Tree.RevealEntry(w.EntryId); else Tree.SelectKey(w.Key); };
                wheel.CrumbClicked += key => { if (key == null) { Tree.ClearSelection(); UpdateWheel(); } else Tree.SelectKey(key); };
                wheel.TestModeChanged += mode => TestMode = mode;
                wheel.PreviewChanged += () => { Header.RefreshPreviewIndicator(); Detail.RefreshPreview(); UpdateWheel(); };
                // A radial drag ticks PreviewState on every pointer move; rebuilding the whole wheel for each tick
                // would be wasteful, so the wheel already updated its own wedge in place and only the header's
                // preview indicator needs to follow.
                wheel.RadialDragged += () => Header.RefreshPreviewIndicator();
                if (Plan != null) UpdateWheel();
                return wheel;
            }
        }

        internal void UpdateWheel()
        {
            Tree.RefreshWheelToggle();
            var row = Tree.Selected;
            bool hidden = Simple || !WheelShown || (row != null && (row.Kind == RowKind.Trigger || row.Kind == RowKind.TriggersGroup || row.Kind == RowKind.Override || row.Kind == RowKind.OverridesGroup));
            if (hidden && wheel == null) return;
            Wheel.EnableInClassList("ad-hidden", hidden);
            if (hidden) return;
            MenuNode shown = row == null ? Plan.Tree.Root
                : row.Node != null ? row.Node
                : row.ParentRow?.Node ?? Plan.Tree.Root;
            Wheel.TestMode = TestMode;
            Wheel.Show(WheelModel.For(shown, row?.Key, L.Text("ad.ui.root_menu"), Plan), Plan);
        }

        static StyleSheet styleSheet;

        /// <summary>The sheet's asset id. It travels with the file, so the direct load works wherever the package sits (Assets or Packages) and costs no project wide search.</summary>
        const string StyleSheetGuid = "02f62c99d11e3c842b4ce925471e4b22";

        /// <summary>
        /// The shared stylesheet, kept for the session once found so the search behind it is never paid twice in
        /// one domain reload. The asset id is tried first; a reimport that changed the id falls through to the
        /// asset database search by name.
        /// </summary>
        public static StyleSheet LoadStyleSheet()
        {
            if (styleSheet != null) return styleSheet;
            var known = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(StyleSheetGuid));
            if (known != null) return styleSheet = known;
            foreach (var guid in AssetDatabase.FindAssets("AvatarDoctor t:StyleSheet"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/AvatarDoctor.uss")) return styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            }
            return null;
        }
    }

    public interface IPlanRefreshable
    {
        void Refresh(AvatarPlan plan);
    }
}
#endif
