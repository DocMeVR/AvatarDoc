#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.preview;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Install target, install mode, parameter prefix and the synced-bit budget.</summary>
    public sealed class HeaderView : VisualElement
    {
        readonly AvatarDoctorView owner;
        readonly TextField installPath;
        readonly VisualElement submenuRows = new VisualElement();
        readonly Button summaryToggle;
        readonly Label readyLabel = new Label { name = "ad-ready-label" };
        readonly Clickable readyLabelClickable;
        readonly Button readyShow;
        readonly KindGlyph statusGlyph = new KindGlyph(GlyphKind.Warning) { name = "ad-status-glyph" };
        readonly Toggle problemsArrow = new Toggle { name = "ad-problems-arrow" };
        readonly VisualElement problemsList = new VisualElement { name = "ad-problems-list" };
        readonly Label writeDefaultsLabel = new Label { name = "ad-write-defaults" };
        List<ModelMessage> problems = new List<ModelMessage>();
        int problemIndex;
        readonly Dictionary<string, VisualElement> lineElementsByMessageKey = new Dictionary<string, VisualElement>();
        readonly string problemsFoldKey;
        bool problemsOpen;
        /// <summary>The error count RefreshReadyLine last saw, so a 0 to more than 0 transition can be told apart from a component that already had errors the moment it was opened. -1 means nothing has been observed yet.</summary>
        int lastErrorCount = -1;
        bool autoOpenedOnce;
        /// <summary>Whether "Show N more" was clicked; expands the list past its 6 line cap for the rest of this view's own life, not remembered between sessions.</summary>
        bool showAllProblems;
        readonly MemoryBar bar = new MemoryBar();
        readonly Label legendAvatar = new Label { name = "ad-legend-avatar" };
        readonly VisualElement legendDoctorSwatch = new VisualElement { name = "ad-legend-swatch-doctor" };
        readonly Label legendDoctor = new Label { name = "ad-legend-doctor" };
        readonly Label legendFree = new Label { name = "ad-legend-free" };
        readonly Label disabledNote = new Label();
        readonly Label noAvatarNote = new Label();
        readonly Label platformNote = new Label();
        readonly SegmentedChoice platformChoice;
        readonly Label previewNote = new Label();
        readonly Button previewReset;
        readonly PublishedValueWatch<bool> previewWatch;
        readonly DirectFields fields;
        readonly Foldout settingsFold;
        readonly VisualElement settingsFoldContent = new VisualElement { name = "ad-settings-fold-content" };
        readonly VisualElement settingsFoldDot = new VisualElement { name = "ad-settings-fold-dot" };
        readonly string settingsFoldKey;
        bool settingsFoldOpen;
        /// <summary>Whether Simple has shown the Component settings fold since the mode last changed. Once shown it stays, even when its last changed setting is put back, so the fold the creator has open does not vanish under the pointer.</summary>
        bool settingsFoldKept;
        readonly VisualElement headerTip = new VisualElement { name = "ad-header-tip" };
        readonly Button copyButton;
        readonly VisualElement byHandList = new VisualElement { name = "ad-by-hand-list" };

        public HeaderView(AvatarDoctorView owner)
        {
            this.owner = owner;
            fields = new DirectFields(owner.SerializedObject, owner.ApplyAndRefresh);
            AddToClassList("ad-panel");
            var body = new VisualElement();
            body.AddToClassList("ad-panel-body");
            Add(body);

            // One status row instead of a title bar and a stack of warnings: a disclosure arrow shaped exactly
            // like Unity's own Foldout arrow, the severity glyph, the same ready text the line always had, then
            // Show and Summary at the row's own right end. Built from a plain Toggle carrying Unity's own foldout
            // classes rather than a real Foldout, since a Foldout draws its own label and content indent and this
            // row draws its own icon, text and trailing buttons in their place.
            var statusRow = new VisualElement { name = "ad-status-row" };
            statusRow.AddToClassList("ad-row");
            problemsArrow.tooltip = L.Text("ad.ui.problems.arrow.tip");
            problemsArrow.AddToClassList(Foldout.toggleUssClassName);
            var problemsArrowInput = problemsArrow.Q(className: Toggle.inputUssClassName);
            problemsArrowInput?.AddToClassList(Foldout.inputUssClassName);
            problemsArrowInput?.Q(className: Toggle.checkmarkUssClassName)?.AddToClassList(Foldout.checkmarkUssClassName);
            problemsArrow.RegisterValueChangedCallback(_ => ToggleProblemsOpen());
            statusRow.Add(problemsArrow);
            statusGlyph.AddToClassList("ad-warn-glyph");
            statusRow.Add(statusGlyph);
            readyLabel.tooltip = L.Text("ad.ui.ready.tip");
            readyLabel.pickingMode = PickingMode.Position;
            readyLabelClickable = new Clickable(ToggleProblemsOpen);
            readyLabel.AddManipulator(readyLabelClickable);
            statusRow.Add(readyLabel);
            var readySpacer = new VisualElement();
            readySpacer.AddToClassList("ad-spacer");
            statusRow.Add(readySpacer);
            readyShow = new Button(ShowNextProblem) { text = L.Text("ad.ui.ready.show"), name = "ad-ready-show", tooltip = L.Text("ad.ui.ready.show.tip") };
            statusRow.Add(readyShow);
            summaryToggle = new Button(owner.ToggleSummary) { text = L.Text("ad.ui.summary"), name = "ad-summary-toggle", tooltip = L.Text("ad.ui.summary.toggle.tip") };
            statusRow.Add(summaryToggle);
            body.Add(statusRow);

            problemsList.AddToClassList("ad-hidden");
            body.Add(problemsList);

            body.Add(headerTip);
            RefreshHeaderTip();

            // The status row, the problem list and the memory bar all speak about the whole avatar or the whole
            // build: none of them may fold away with Install into / Install as / Parameter prefix / Platform, so
            // both sit above the Component settings fold, never inside it. Write Defaults moved inside the fold
            // (still its very first line), since it is one more install setting, not a build wide problem.
            bar.tooltip = L.Text("ad.ui.memory.bar.tip");
            body.Add(bar);
            var legend = new VisualElement { name = "ad-membar-legend" };
            legend.AddToClassList("ad-membar-legend");
            var legendAvatarSwatch = new VisualElement { name = "ad-legend-swatch-avatar" };
            legendAvatarSwatch.AddToClassList("ad-legend-swatch");
            legendAvatarSwatch.AddToClassList("ad-legend-swatch-avatar");
            legend.Add(legendAvatarSwatch);
            legend.Add(legendAvatar);
            legendDoctorSwatch.AddToClassList("ad-legend-swatch");
            legendDoctorSwatch.AddToClassList("ad-legend-swatch-doctor");
            legend.Add(legendDoctorSwatch);
            legend.Add(legendDoctor);
            var legendSpacer = new VisualElement();
            legendSpacer.AddToClassList("ad-spacer");
            legend.Add(legendSpacer);
            legend.Add(legendFree);
            body.Add(legend);

            settingsFold = new Foldout { name = "ad-settings-fold-header", text = L.Text("ad.ui.settings_fold"), tooltip = L.Text("ad.ui.settings_fold.tip") };
            settingsFold.AddToClassList("ad-fold");
            var settingsToggle = settingsFold.Q<Toggle>();
            if (settingsToggle != null) settingsToggle.tooltip = L.Text("ad.ui.settings_fold.tip");
            settingsFoldDot.AddToClassList("ad-dot");
            settingsFoldDot.tooltip = L.Text("ad.ui.settings_fold.dot.tip");
            var settingsInputRow = settingsToggle?.Q(className: "unity-foldout__input");
            (settingsInputRow ?? (VisualElement)settingsToggle ?? settingsFold).Add(settingsFoldDot);
            settingsFold.RegisterValueChangedCallback(e =>
            {
                settingsFoldOpen = e.newValue;
                ComponentFold.Save(owner.ViewPrefs, settingsFoldKey, settingsFoldOpen);
                ApplySettingsFoldState();
            });
            body.Add(settingsFold);
            settingsFoldContent.name = "ad-settings-fold-content";
            body.Add(settingsFoldContent);

            writeDefaultsLabel.AddToClassList("ad-note");
            writeDefaultsLabel.tooltip = L.Text("ad.ui.write_defaults.tip");
            settingsFoldContent.Add(writeDefaultsLabel);

            var installRow = new VisualElement();
            installRow.AddToClassList("ad-row");
            installPath = fields.Text(owner.SerializedObject.FindProperty(nameof(AvatarDoc.installPath)), L.Text("ad.ui.install"));
            installPath.name = "ad-install-path";
            installPath.tooltip = L.Text("ad.ui.install.tip");
            Button pick = null;
            pick = new Button(() => PickPath(pick)) { tooltip = L.Text("ad.ui.install.pick") };
            EntryDetailView.AddDropdownIcon(pick);
            installRow.Add(installPath);
            installRow.Add(pick);
            settingsFoldContent.Add(installRow);

            var mode = fields.EnumChoice(owner.SerializedObject.FindProperty(nameof(AvatarDoc.installMode)), L.Text("ad.ui.install.mode"),
                new List<string> { L.Text("ad.ui.install.mode.append"), L.Text("ad.ui.install.mode.submenu") });
            mode.name = "ad-install-mode";
            mode.tooltip = L.Text("ad.ui.install.mode.tip");
            settingsFoldContent.Add(Row(mode));
            var menuNameField = fields.Text(owner.SerializedObject.FindProperty(nameof(AvatarDoc.menuName)), L.Text("ad.ui.menu_name"));
            menuNameField.name = "ad-menu-name";
            menuNameField.tooltip = L.Text("ad.ui.menu_name.tip");
            submenuRows.Add(Row(menuNameField));
            // The component's own submenu icon is copied into the merged menu node, so it is a plan change like its name.
            var menuIconField = fields.Object<Texture2D>(owner.SerializedObject.FindProperty(nameof(AvatarDoc.menuIcon)), L.Text("ad.ui.menu_icon"));
            menuIconField.name = "ad-menu-icon";
            menuIconField.tooltip = L.Text("ad.ui.menu_icon.tip");
            submenuRows.Add(Row(menuIconField));
            settingsFoldContent.Add(submenuRows);
            var prefixField = fields.Text(owner.SerializedObject.FindProperty(nameof(AvatarDoc.parameterPrefix)), L.Text("ad.ui.prefix"));
            prefixField.name = "ad-parameter-prefix";
            prefixField.tooltip = L.Text("ad.ui.prefix.tip");
            settingsFoldContent.Add(Row(prefixField));

            var platformRow = new VisualElement();
            platformRow.AddToClassList("ad-row");
            var platformLabel = new Label(L.Text("ad.ui.platform")) { tooltip = L.Text("ad.ui.platform.component.tip") };
            platformLabel.AddToClassList("ad-label");
            platformRow.Add(platformLabel);
            platformChoice = fields.EnumSegments(owner.SerializedObject.FindProperty(nameof(AvatarDoc.platform)), PlatformLabels);
            platformChoice.name = "ad-platform";
            platformChoice.tooltip = L.Text("ad.ui.platform.component.tip");
            platformRow.Add(platformChoice);
            settingsFoldContent.Add(platformRow);
            platformNote.name = "ad-platform-note";
            platformNote.AddToClassList("ad-note");
            platformNote.AddToClassList("ad-note-indent");
            settingsFoldContent.Add(platformNote);

            var tipsRow = new VisualElement();
            tipsRow.AddToClassList("ad-row");
            var tipsLabel = new Label(L.Text("ad.ui.tips_switch_label")) { tooltip = L.Text("ad.ui.tips_switch.tip") };
            tipsLabel.AddToClassList("ad-label");
            tipsRow.Add(tipsLabel);
            var tipsButton = new Button(TurnTipsBackOn) { text = L.Text("ad.ui.tips_switch"), name = "ad-tips-switch", tooltip = L.Text("ad.ui.tips_switch.tip") };
            tipsButton.AddToClassList("ad-field-button");
            tipsRow.Add(tipsButton);
            settingsFoldContent.Add(tipsRow);

            var copyRow = new VisualElement { name = "ad-copy-row" };
            copyRow.AddToClassList("ad-row");
            var copyLabel = new Label(L.Text("ad.ui.copy_to.row")) { tooltip = L.Text("ad.ui.copy_to.open.tip") };
            copyLabel.AddToClassList("ad-label");
            copyRow.Add(copyLabel);
            copyButton = new Button(OpenCopy) { text = L.Text("ad.ui.copy_to.open"), name = "ad-copy-open", tooltip = L.Text("ad.ui.copy_to.open.tip") };
            copyButton.AddToClassList("ad-field-button");
            copyButton.AddToClassList("ad-copy-open");
            copyRow.Add(copyButton);
            settingsFoldContent.Add(copyRow);
            settingsFoldContent.Add(byHandList);

            settingsFoldKey = ComponentFold.KeyFor(owner.Menu, "component_settings");
            settingsFoldOpen = ComponentFold.Load(owner.ViewPrefs, settingsFoldKey);
            ApplySettingsFoldState();

            problemsFoldKey = ComponentFold.KeyFor(owner.Menu, "problems");
            problemsOpen = ComponentFold.Load(owner.ViewPrefs, problemsFoldKey);
            ApplyProblemsFoldState();

            disabledNote.text = L.Text("ad.ui.disabled");
            disabledNote.AddToClassList("ad-note");
            noAvatarNote.text = L.Text("ad.ui.no_avatar");
            noAvatarNote.AddToClassList("ad-note");
            body.Add(disabledNote);
            body.Add(noAvatarNote);

            previewNote.name = "ad-preview-note";
            previewNote.AddToClassList("ad-note");
            previewNote.text = L.Text("ad.ui.preview.off_note");
            body.Add(previewNote);
            previewReset = new Button(ResetPreview) { name = "ad-preview-reset", text = L.Text("ad.ui.preview.reset"), tooltip = L.Text("ad.ui.preview.reset.tip") };
            previewReset.AddToClassList("ad-preview-reset");
            body.Add(previewReset);

            UpdateModeRows();

            previewWatch = new PublishedValueWatch<bool>(AvatarDoctorPreview.Toggle.IsEnabled, OnPreviewToggled);
            RegisterCallback<AttachToPanelEvent>(_ => previewWatch.Start());
            RegisterCallback<DetachFromPanelEvent>(_ => previewWatch.Stop());
        }

        /// <summary>NDMF's preview switch can flip while a problem line's Copy menu is open; the header refresh clears those lines, so it waits for the view's menu hold like every other refresh.</summary>
        void OnPreviewToggled(bool _)
        {
            if (owner.MenuOpen) owner.ScheduleRefresh();
            else Refresh(owner.Plan);
        }

        /// <summary>Rebuilds the header's own first-use tip from the current tip store, so a store swapped in after construction (a real host handing a detached view a real one) is still picked up. Simple words it without the fold it hides.</summary>
        void RefreshHeaderTip()
        {
            headerTip.Clear();
            var tip = TipBanner.Build(owner, TipKinds.Header, owner.Simple ? "ad.tip.header.simple" : "ad.tip.header");
            if (tip != null) headerTip.Add(tip);
        }

        /// <summary>Follows the owning view's mode: the tip's wording and whether the Component settings fold shows, which is decided afresh here.</summary>
        internal void ApplyMode()
        {
            settingsFoldKept = false;
            RefreshHeaderTip();
            ApplySettingsFoldState();
        }

        /// <summary>"Tips" in the Component settings fold: forgets every dismissed first-use tip so they all show again. Public so its button and a test call the same thing.</summary>
        public void TurnTipsBackOn()
        {
            owner.TipStore.ResetAll();
            RefreshHeaderTip();
            // A refresh alone would leave the selected entry's own panel as it is: it only rebuilds a panel whose
            // shape changed, and dismissing or restoring a tip is not part of that shape.
            owner.Detail.ForceRebuild();
            owner.Refresh();
        }

        /// <summary>Highlights the Summary button while its panel is open.</summary>
        public void RefreshSummaryToggle(bool open) => summaryToggle.EnableInClassList("ad-primary", open);

        /// <summary>The status text's own click manipulator, so a test can drive a real click on it the same way UiTestEvents drives a Button, rather than calling ToggleProblemsOpen directly.</summary>
        public Clickable ReadyLabelClickable => readyLabelClickable;

        /// <summary>Clears every preview override; the button behind it in the header.</summary>
        public void ResetPreview()
        {
            PreviewState.ClearAll();
            owner.Refresh();
        }

        /// <summary>The component's three platform choices, in PlatformChoice order.</summary>
        static List<string> PlatformLabels => new List<string> { L.Text("ad.ui.platform.both"), L.Text("ad.ui.platform.pc"), L.Text("ad.ui.platform.quest") };

        /// <summary>The platform the editor builds for right now, named the way the entry panels name it.</summary>
        internal static string CurrentPlatformName => L.Text(Platform.IsMobileTarget ? "ad.ui.platform.name.quest" : "ad.ui.platform.name.pc");

        static VisualElement Row(VisualElement field)
        {
            var row = new VisualElement();
            row.AddToClassList("ad-row");
            row.Add(field);
            return row;
        }

        void UpdateModeRows()
        {
            bool asSubmenu = owner.Menu != null && owner.Menu.installMode == InstallMode.AsSubmenu;
            submenuRows.EnableInClassList("ad-hidden", !asSubmenu);
        }

        /// <summary>Whether the Component settings fold is open right now. Closed by default, remembered per component. Read by tests.</summary>
        public bool SettingsFoldOpen => settingsFoldOpen;

        /// <summary>Whether the fold's own dot is showing, meaning it is closed while one of the values it hides is not at its default. Read by tests.</summary>
        public bool SettingsFoldDotVisible => !settingsFoldDot.ClassListContains("ad-hidden");

        /// <summary>Opens or closes the Component settings fold and remembers the choice for this component. The button the fold's own header calls, so a test can call the same thing.</summary>
        public void ToggleSettingsFold()
        {
            settingsFoldOpen = !settingsFoldOpen;
            ComponentFold.Save(owner.ViewPrefs, settingsFoldKey, settingsFoldOpen);
            ApplySettingsFoldState();
        }

        void ApplySettingsFoldState()
        {
            settingsFold.SetValueWithoutNotify(settingsFoldOpen);
            // Simple leaves the fold out, unless a setting in it is not at its default: a value the creator set is
            // never hidden. Once shown the fold stays until the mode changes.
            if (SettingsNotAtDefault) settingsFoldKept = true;
            bool hidden = owner.Simple && !settingsFoldKept;
            settingsFold.EnableInClassList("ad-hidden", hidden);
            settingsFoldContent.EnableInClassList("ad-hidden", hidden || !settingsFoldOpen);
            RefreshSettingsFoldDot();
        }

        /// <summary>Whether one of the values the fold holds (Install into, Install as, Parameter prefix, Platform, the objects to pick by hand) is not at its default.</summary>
        bool SettingsNotAtDefault => owner.Menu != null
            && (owner.Menu.installPath.Length > 0 || owner.Menu.installMode != InstallMode.AppendItems
                || owner.Menu.parameterPrefix != "AD" || owner.Menu.platform != PlatformChoice.Both
                || (owner.Menu.pickByHand != null && owner.Menu.pickByHand.Count > 0));

        /// <summary>"Copy" in the Component settings fold: opens Copy to another avatar for this component. Public so its button and a test call the same thing.</summary>
        public void OpenCopy()
        {
            if (MenuCopy.CanCopy(owner.Menu)) MenuCopy.Open(MenuCopy.For(owner.Menu));
        }

        /// <summary>
        /// The objects marked Pick by hand when copying, one row each with the button that takes the mark off. A locked
        /// component lists them without the buttons. Hidden while the list is empty.
        /// </summary>
        void RefreshByHandList()
        {
            byHandList.Clear();
            var marked = owner.Menu.pickByHand;
            byHandList.EnableInClassList("ad-hidden", marked == null || marked.Count == 0);
            if (marked == null) return;
            for (int i = 0; i < marked.Count; i++)
            {
                int index = i;
                var row = new VisualElement { name = "ad-by-hand-row-" + i };
                row.AddToClassList("ad-row");
                var caption = new Label(i == 0 ? L.Text("ad.ui.copy_to.by_hand_list") : "") { tooltip = L.Text("ad.ui.copy_to.by_hand_list.tip") };
                caption.AddToClassList("ad-label");
                row.Add(caption);
                var target = marked[i];
                var path = target != null ? MenuCopy.PathOf(AvatarPlanCache.RootOf(owner.Menu), target.transform) : "";
                var name = new Label(target != null ? target.name : L.Text("ad.ui.copy_to.by_hand_gone"))
                {
                    tooltip = (path.Length > 0 ? path + "\n" : "") + L.Text("ad.ui.copy_to.by_hand_list.tip"),
                    displayTooltipWhenElided = false,
                };
                name.AddToClassList("ad-add-value");
                name.AddToClassList("ad-by-hand-name");
                row.Add(name);
                if (!owner.Locked)
                {
                    var remove = new Button(() => RemoveByHand(index)) { text = "×", name = "ad-by-hand-remove-" + i, tooltip = L.Text("ad.ui.copy_to.by_hand.remove.tip") };
                    remove.AddToClassList("ad-w-24");
                    row.Add(remove);
                }
                byHandList.Add(row);
            }
        }

        /// <summary>Takes the mark off the object at index of the list, with Undo, and redraws.</summary>
        public void RemoveByHand(int index)
        {
            if (!MenuCopy.RemoveByHandAt(owner.Menu, index)) return;
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            owner.Refresh();
        }

        /// <summary>The dot shows only while the fold is closed and one of the values it hides is not at its default, so nothing about this component's install settings is ever silently different without a hint.</summary>
        void RefreshSettingsFoldDot()
        {
            settingsFoldDot.EnableInClassList("ad-hidden", settingsFoldOpen || !SettingsNotAtDefault);
        }

        /// <summary>Whether the problem list is open right now. Closed by default, remembered per component. Read by tests.</summary>
        public bool ProblemsOpen => problemsOpen;

        /// <summary>Opens or closes the problem list and remembers the choice for this component. Public so a test can drive the same thing a click on the arrow does.</summary>
        public void ToggleProblemsOpen()
        {
            problemsOpen = !problemsOpen;
            ComponentFold.Save(owner.ViewPrefs, problemsFoldKey, problemsOpen);
            ApplyProblemsFoldState();
        }

        void ApplyProblemsFoldState()
        {
            problemsArrow.SetValueWithoutNotify(problemsOpen);
            problemsList.EnableInClassList("ad-hidden", !problemsOpen);
        }

        public void Refresh(AvatarPlan plan)
        {
            RefreshHeaderTip();
            fields.Reload();
            int limit = VRCExpressionParameters.MAX_PARAMETER_COST;
            bar.Set(plan.AvatarBits, plan.AvatarDoctorBits, limit);
            legendAvatar.text = UiText.Count(plan.AvatarBits, "ad.ui.memory.avatar.one", "ad.ui.memory.avatar");
            legendDoctor.text = UiText.Count(plan.AvatarDoctorBits, "ad.ui.memory.ad.one", "ad.ui.memory.ad");
            int free = limit - plan.AvatarBits - plan.AvatarDoctorBits;
            bool overBudget = free < 0;
            legendFree.text = overBudget ? UiText.Count(-free, "ad.ui.memory.over.one", "ad.ui.memory.over") : string.Format(L.Text("ad.ui.memory.free_bits"), free, limit);
            legendDoctorSwatch.EnableInClassList("ad-legend-swatch-error", overBudget);
            disabledNote.EnableInClassList("ad-hidden", owner.Menu.enabled);
            noAvatarNote.EnableInClassList("ad-hidden", plan.AvatarRoot != null);
            RefreshPreviewIndicator();
            platformNote.text = string.Format(L.Text("ad.ui.platform.current"), CurrentPlatformName);
            var orphanEdits = OrphanEdits(plan).ToList();
            RefreshReadyLine(plan, orphanEdits);
            RefreshProblemsList(plan, orphanEdits);
            writeDefaultsLabel.text = string.Format(L.Text("ad.ui.write_defaults"), L.Text(plan.WriteDefaultsOn ? "ad.ui.write_defaults.on" : "ad.ui.write_defaults.off"));
            copyButton.SetEnabled(MenuCopy.CanCopy(owner.Menu));
            RefreshByHandList();
            UpdateModeRows();
            ApplySettingsFoldState();
        }

        /// <summary>A synthetic warning for an orphaned menu edit, carrying its EditId the same way a real plan message would, so it sorts and cycles exactly like one.</summary>
        static ModelMessage OrphanEditMessage(PendingEdit pending)
            => ModelMessage.Warning("ad.warn.edit_source_missing", string.Join("/", pending.Edit.source ?? new List<string>())).ForEdit(pending.Edit.id);

        /// <summary>
        /// The messages the ready line counts and "Show" cycles through: every error, or, when there is none, every
        /// warning plus one per orphaned menu edit this component holds (an edit whose source is gone is exactly as
        /// much the creator's business as any other warning). Within that set, a message that names an entry or a
        /// menu edit comes before one that names nothing, since only the first kind has a row "Show" can reveal.
        /// </summary>
        static List<ModelMessage> OrderedProblems(AvatarPlan plan, List<PendingEdit> orphanEdits, out int errorCount, out int warningCount)
        {
            errorCount = plan.Messages.Count(m => m.Severity == MessageSeverity.Error);
            warningCount = plan.Messages.Count(m => m.Severity == MessageSeverity.Warning) + orphanEdits.Count;
            IEnumerable<ModelMessage> chosen;
            if (errorCount > 0) chosen = plan.Messages.Where(m => m.Severity == MessageSeverity.Error);
            else chosen = plan.Messages.Where(m => m.Severity == MessageSeverity.Warning).Concat(orphanEdits.Select(OrphanEditMessage));
            var named = new List<ModelMessage>();
            var unnamed = new List<ModelMessage>();
            foreach (var message in chosen)
                (message.EntryIds.Count > 0 || !string.IsNullOrEmpty(message.EditId) ? named : unnamed).Add(message);
            named.AddRange(unnamed);
            return named;
        }

        void RefreshReadyLine(AvatarPlan plan, List<PendingEdit> orphanEdits)
        {
            problems = OrderedProblems(plan, orphanEdits, out int errorCount, out int warningCount);
            problemIndex = 0;
            bool ready = errorCount == 0 && warningCount == 0;
            readyLabel.text = ready ? L.Text("ad.ui.ready")
                : errorCount > 0 ? UiText.Count(errorCount, "ad.ui.ready.problems.one", "ad.ui.ready.problems")
                : UiText.Count(warningCount, "ad.ui.ready.warnings.one", "ad.ui.ready.warnings");
            readyLabel.EnableInClassList("ad-text-ok", ready);
            readyLabel.EnableInClassList("ad-text-error", !ready && errorCount > 0);
            readyLabel.EnableInClassList("ad-text-warn", !ready && errorCount == 0);
            readyShow.EnableInClassList("ad-hidden", ready);
            problemsArrow.EnableInClassList("ad-hidden", ready);
            statusGlyph.EnableInClassList("ad-hidden", ready);
            statusGlyph.EnableInClassList("ad-text-error", errorCount > 0);

            // A component that already had errors the moment it was opened stays closed, matching "closed by
            // default"; only a live 0 to more than 0 transition during this session pops the list open, once.
            if (lastErrorCount == 0 && errorCount > 0 && !autoOpenedOnce)
            {
                autoOpenedOnce = true;
                problemsOpen = true;
                ComponentFold.Save(owner.ViewPrefs, problemsFoldKey, true);
                ApplyProblemsFoldState();
            }
            lastErrorCount = errorCount;
        }

        /// <summary>
        /// Selects the row of the first problem, reveals it and, when the message names a known field, focuses it.
        /// A second call moves to the next problem; the count wraps once every one has been shown.
        /// </summary>
        public void ShowNextProblem()
        {
            if (problems.Count == 0) return;
            var message = problems[problemIndex % problems.Count];
            problemIndex++;
            RevealProblem(message);
        }

        void RevealProblem(ModelMessage message)
        {
            var entryId = message.EntryIds.FirstOrDefault();
            if (entryId != null)
            {
                owner.Tree.RevealEntry(entryId);
                if (!string.IsNullOrEmpty(message.FieldName)) owner.Detail.FocusField(message.FieldName);
                return;
            }
            if (!string.IsNullOrEmpty(message.EditId))
            {
                var row = owner.Tree.RowForEdit(message.EditId);
                if (row != null) { owner.Tree.RevealKey(row.Key); return; }
                // An orphaned edit resolves no row (that is exactly what makes it orphaned): the only place left
                // for "Show" to point at is the edit's own line in the list.
                FlashPlanNote("edit:" + message.EditId);
                return;
            }
            // A message about the whole avatar (the synced bit budget, mixed Write Defaults) names no row to
            // select, so "Show" has to point at the one place it already appears: its own line in the list.
            FlashPlanNote(message.Key);
        }

        /// <summary>The key of the plan note "Show" last pointed at. Read by tests; the real effect is the list's own open, scroll and flash.</summary>
        public string LastFlashedPlanNoteKey { get; private set; }

        /// <summary>Opens the problem list (expanding it past its 6 line cap if that is where the line sits), scrolls to the named line and flashes it briefly, since a message with no row of its own has nothing else for "Show" to select.</summary>
        void FlashPlanNote(string key)
        {
            LastFlashedPlanNoteKey = key;
            problemsOpen = true;
            ComponentFold.Save(owner.ViewPrefs, problemsFoldKey, true);
            showAllProblems = true;
            ApplyProblemsFoldState();
            RefreshProblemsList(owner.Plan, OrphanEdits(owner.Plan).ToList());
            if (!lineElementsByMessageKey.TryGetValue(key, out var box)) return;
            var scrollView = GetFirstAncestorOfType<ScrollView>();
            scrollView?.ScrollTo(box);
            box.AddToClassList("ad-flash");
            box.schedule.Execute(() => box.RemoveFromClassList("ad-flash")).StartingIn(900);
        }

        /// <summary>
        /// The only parts of the header a preview override changes: the note that says the preview is off and the
        /// Reset button. Run on its own while a radial is dragged, where a full Refresh (a SerializedObject
        /// update, the problem list and the write defaults line) would be paid for on every pointer move.
        /// </summary>
        public void RefreshPreviewIndicator()
        {
            var plan = owner.Plan;
            previewNote.EnableInClassList("ad-hidden", PreviewRuns || plan == null || plan.AvatarRoot == null);
            previewReset.EnableInClassList("ad-hidden", PreviewState.Overrides.Value.IsEmpty);
        }

        /// <summary>One compact line per problem in the plan, plus one per orphaned menu edit this component owns, at most 6 unless "Show N more" was clicked.</summary>
        sealed class ProblemLine
        {
            public MessageSeverity Severity;
            public string Key;
            public string Text;
            public int Count = 1;
            /// <summary>The message this line represents, for Show; null for an orphaned edit line, which removes instead.</summary>
            public ModelMessage Message;
            public string EditIdToRemove;
        }

        /// <summary>
        /// Every error, then every warning, from the plan's own messages (nothing new walks the avatar here),
        /// merging a message with the same key and the same formatted text into one line ending in "x N", then one
        /// line per menu edit this component owns whose source no longer resolves anywhere in the merged menu.
        /// </summary>
        List<ProblemLine> BuildProblemLines(AvatarPlan plan, List<PendingEdit> orphanEdits)
        {
            var lines = new List<ProblemLine>();
            var byKeyAndText = new Dictionary<(string, string), ProblemLine>();
            void AddOrMerge(ModelMessage message)
            {
                string text = UiText.Format(message);
                var mergeKey = (message.Key, text);
                if (byKeyAndText.TryGetValue(mergeKey, out var existing)) { existing.Count++; return; }
                var line = new ProblemLine { Severity = message.Severity, Key = message.Key, Text = text, Message = message };
                byKeyAndText[mergeKey] = line;
                lines.Add(line);
            }
            foreach (var message in plan.Messages.Where(m => m.Severity == MessageSeverity.Error)) AddOrMerge(message);
            foreach (var message in plan.Messages.Where(m => m.Severity == MessageSeverity.Warning)) AddOrMerge(message);
            foreach (var pending in orphanEdits)
            {
                var edit = pending.Edit;
                lines.Add(new ProblemLine
                {
                    Severity = MessageSeverity.Warning,
                    Key = "edit:" + edit.id,
                    Text = UiText.Format(OrphanEditMessage(pending)),
                    EditIdToRemove = edit.id,
                });
            }
            return lines;
        }

        const int ProblemListCap = 6;

        void RefreshProblemsList(AvatarPlan plan, List<PendingEdit> orphanEdits)
        {
            problemsList.Clear();
            lineElementsByMessageKey.Clear();
            var lines = BuildProblemLines(plan, orphanEdits);
            int shown = showAllProblems ? lines.Count : Mathf.Min(ProblemListCap, lines.Count);
            for (int i = 0; i < shown; i++)
            {
                var line = lines[i];
                var box = BuildProblemLine(line);
                problemsList.Add(box);
                if (line.Message != null || line.EditIdToRemove != null) lineElementsByMessageKey[line.Key] = box;
            }
            if (!showAllProblems && lines.Count > ProblemListCap)
            {
                var more = new Button(ShowAllProblems)
                    { text = string.Format(L.Text("ad.ui.problems.more"), lines.Count - ProblemListCap), name = "ad-problems-more" };
                more.AddToClassList("ad-link");
                problemsList.Add(more);
            }
        }

        /// <summary>Expands the problem list past its 6 line cap for the rest of this view's own life. The list's own "Show N more" link calls this; public so a test can call the same thing.</summary>
        public void ShowAllProblems()
        {
            showAllProblems = true;
            RefreshProblemsList(owner.Plan, OrphanEdits(owner.Plan).ToList());
        }

        VisualElement BuildProblemLine(ProblemLine line)
        {
            var box = new VisualElement { name = "ad-problem-line-" + line.Key };
            box.AddToClassList("ad-problem-line");
            var icon = new KindGlyph(GlyphKind.Warning);
            icon.AddToClassList("ad-warn-glyph");
            icon.EnableInClassList("ad-text-error", line.Severity == MessageSeverity.Error);
            box.Add(icon);
            string text = line.Count > 1 ? string.Format(L.Text("ad.ui.problems.line_count"), line.Text, line.Count) : line.Text;
            box.Add(UiText.MessageLabel(text));
            if (line.Message != null)
            {
                var learnMore = DocsLinks.LearnMoreButton(line.Message.Key);
                if (learnMore != null) box.Add(learnMore);
            }
            if (line.EditIdToRemove != null)
            {
                // Removing the edit is an entry data write, so a locked component shows the warning without the way out.
                if (!owner.Locked)
                {
                    var editId = line.EditIdToRemove;
                    box.Add(new Button(() => RemoveOrphanEdit(editId)) { text = L.Text("ad.ui.edit.remove"), tooltip = L.Text("ad.ui.edit.remove.tip") });
                }
            }
            else if (line.Message != null && (line.Message.EntryIds.Count > 0 || !string.IsNullOrEmpty(line.Message.EditId)))
            {
                var message = line.Message;
                box.Add(new Button(() => RevealProblem(message)) { text = L.Text("ad.ui.ready.show"), tooltip = L.Text("ad.ui.ready.show.tip") });
            }
            return box;
        }

        /// <summary>Drops a menu edit whose source no longer exists, with Undo, and redraws.</summary>
        public void RemoveOrphanEdit(string editId)
        {
            if (!MenuEditing.RemoveById(owner.Menu, editId)) return;
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
        }

        /// <summary>
        /// Menu edits whose source resolves on nothing and that THIS component holds. Another AvatarDoc's leftover
        /// edit is its own inspector's business: showing it here would put the same warning, with the same button,
        /// in front of every component on the avatar.
        /// </summary>
        IEnumerable<PendingEdit> OrphanEdits(AvatarPlan plan)
        {
            if (plan == null || plan.Tree == null) return Enumerable.Empty<PendingEdit>();
            return plan.Tree.PendingEdits.Where(p => p.Edit != null && p.Component == owner.Menu);
        }

        /// <summary>Ids of the edits the list shows, in the order it shows them. Read by the tests.</summary>
        public List<string> OrphanEditIds => OrphanEdits(owner.Plan).Select(p => p.Edit.id).ToList();

        /// <summary>Whether anything the preview draws can reach the scene: our own node, NDMF's global switch, and the transient depth counter NDMF raises while previews are suspended.</summary>
        static bool PreviewRuns => AvatarDoctorPreview.Toggle.IsEnabled.Value && NDMFPreviewPrefs.instance.EnablePreview && NDMFPreview.DisablePreviewDepth == 0;

        /// <summary>Every submenu path of the merged menu that this component can install into: existing menus and other components' submenus, not its own.</summary>
        public static List<string> PathChoices(AvatarPlan plan, AvatarDoc menu)
        {
            return plan.Tree.AllNodes()
                .Where(n => n.Path.Length > 0 && !n.IsMorePage)
                .Where(n => !(n.Component == menu && n.EntryId != null))
                .Select(n => n.Path)
                .Distinct()
                .OrderBy(p => p)
                .ToList();
        }

        void PickPath(VisualElement anchor)
        {
            var property = owner.SerializedObject.FindProperty(nameof(AvatarDoc.installPath));
            var items = new List<AdMenu.Item> { new AdMenu.Item { Text = L.Text("ad.ui.install.root"), Checked = property.stringValue.Length == 0, Action = () => SetPath("") } };
            foreach (var path in PathChoices(owner.Plan, owner.Menu))
            {
                var choice = path;
                items.Add(new AdMenu.Item { Text = choice.Replace("/", " › "), Checked = property.stringValue == choice, Action = () => SetPath(choice) });
            }
            AdMenu.Open(anchor.worldBound, anchor, items);
        }

        void SetPath(string path)
        {
            owner.SerializedObject.Update();
            owner.SerializedObject.FindProperty(nameof(AvatarDoc.installPath)).stringValue = path;
            owner.ApplyAndRefresh();
        }
    }
}
#endif
