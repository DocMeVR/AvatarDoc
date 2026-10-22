#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Edits the selected row: bound fields for this component's entries, notes for everything else.</summary>
    public sealed class EntryDetailView : VisualElement, IPlanRefreshable
    {
        readonly AvatarDoctorView owner;
        readonly Label kindLabel = new Label();
        readonly KindGlyph glyph = new KindGlyph(GlyphKind.Toggle);
        readonly VisualElement titleChips = new VisualElement();
        readonly VisualElement body = new VisualElement();
        readonly VisualElement messageBoxes = new VisualElement();
        readonly EventCallback<FocusOutEvent> focusOutCallback;
        readonly DirectFields fields;

        string lastKey;
        string lastShape;
        bool pendingRebuild;
        bool focusOutRegistered;
        NameStyleRow nameRow;
        /// <summary>
        /// The entry and mode the three flags below were decided for. In Simple a part shows only while it holds something.
        /// That is decided when the panel is built for another entry or another mode, and a part already shown stays until
        /// then, so a part the creator is using does not vanish under the pointer when they put its last value back.
        /// </summary>
        string keptFor;
        bool keptStyleRows;
        bool keptGroupRow;
        bool keptAdvancedFold;
        bool groupRowBuilt;
        TextField submenuName;
        Label slotNote;
        Label parameterName;
        Label parameterChip;
        Toggle parameterCustom;
        Label parameterNote;
        Label parameterShared;
        Label groupOwnerNote;
        Button previewDefault;
        SegmentedChoice previewChoice;
        Slider previewSlider;
        Label touchTestNote;
        Label syncedCost;
        Label buildsLine;
        AdvancedFold advancedFold;
        /// <summary>The target sections this panel built: each one's "+ Add effect" name and description keys, its view, and whether it always shows regardless of its row count (the panel's common starting place).</summary>
        readonly List<(string NameKey, string DescKey, TargetListView View, bool AlwaysVisible)> effectSections = new List<(string, string, TargetListView, bool)>();
        /// <summary>The sections among effectSections that "+ Add effect" offers. All of them, except on a toggle or radial in Simple, which offers Objects, Blend shapes and Properties only; the others stay built, so one that already has rows still shows.</summary>
        readonly List<(string NameKey, string DescKey, TargetListView View, bool AlwaysVisible)> offeredSections = new List<(string, string, TargetListView, bool)>();

        /// <summary>What the currently built panel's "+ Add effect" button offers, in order. Public so a test can check the list and add through a section without opening a real menu, the same way MenuTreeView.MoreItems works.</summary>
        public IReadOnlyList<(string NameKey, string DescKey, TargetListView View)> EffectSections
            => offeredSections.Select(s => (s.NameKey, s.DescKey, s.View)).ToList();
        readonly ContactSceneHandle sceneHandle;

        int selectedGroup;
        int selectedIndex;

        public TreeRow Row { get; private set; }
        public string BoundPropertyPath { get; private set; }
        /// <summary>The id of the entry BoundPropertyPath was taken for. Paths this panel handed out while it was built name the entry by the place it had then.</summary>
        string boundEntryId;
        /// <summary>How often the panel was built from scratch. A refresh that only touches derived parts does not count.</summary>
        public int RebuildCount { get; private set; }
        /// <summary>The direct-bound fields of this panel, shared with TouchPanel so its rows commit through the same path as everything else here.</summary>
        internal DirectFields Fields => fields;
        /// <summary>Whether the Contact panel's "More tags" section is open. Kept here, not on TouchPanel, because a tag check box changes the panel's Shape and rebuilds it, which would otherwise close the section on every click.</summary>
        internal bool TouchMoreTagsOpen { get; set; }
        /// <summary>The Scene view handle backing a selected contact trigger row. Public so tests, which live in a separate assembly, can drive its registry directly instead of dispatching real panel or Scene view events.</summary>
        public ContactSceneHandle SceneHandle => sceneHandle;
        /// <summary>Which condition of the selected trigger the contact and PhysBone settings and the Scene view handle work on.</summary>
        public string SelectedConditionKey => selectedGroup + "." + selectedIndex;

        public bool IsSelectedCondition(int groupIndex, int index) => groupIndex == selectedGroup && index == selectedIndex;

        /// <summary>Picks the condition whose contact or PhysBone settings the panel opens and the Scene view handle draws.</summary>
        public void SelectCondition(int groupIndex, int index)
        {
            if (Row == null || Row.Entry == null) return;
            ReleaseFocus();
            selectedGroup = groupIndex;
            selectedIndex = index;
            Refresh(owner.Plan);
        }

        /// <summary>The condition stored in the entry that the touch settings write to, or null while the trigger still works from its flat source fields and the entry itself is the condition.</summary>
        public WhenCondition StoredTouchCondition => ConditionAt(Row != null ? Row.Entry : null, selectedGroup, selectedIndex);

        /// <summary>The condition a row's touch settings read: the selected one of a trigger with groups, or a reading of the entry's own flat fields.</summary>
        public WhenCondition TouchConditionOf(TreeRow row)
        {
            if (row == null || !row.Mine || row.Entry == null || row.Entry.kind != EntryKind.Trigger) return null;
            if (!row.Entry.UsesWhenGroups) return WhenCondition.FromEntry(row.Entry);
            return ConditionAt(row.Entry, selectedGroup, selectedIndex);
        }

        static WhenCondition ConditionAt(MenuEntry entry, int groupIndex, int index)
        {
            if (entry == null || !entry.UsesWhenGroups) return null;
            if (groupIndex < 0 || groupIndex >= entry.when.Count) return null;
            var group = entry.when[groupIndex];
            if (group == null || index < 0 || index >= group.all.Count) return null;
            return group.all[index];
        }

        /// <summary>The property the contact and PhysBone fields write through: the selected condition, or the entry itself while the trigger has a single source. Their field names match, so the same relative paths work on both.</summary>
        internal SerializedProperty ConditionProperty()
        {
            if (Row == null || Row.Entry == null) return null;
            var property = owner.EntryProperty(Row.Entry);
            if (property == null || !Row.Entry.UsesWhenGroups) return property;
            var groups = property.FindPropertyRelative(nameof(MenuEntry.when));
            // A selection that is not there any more writes nothing. The entry's own flat fields are not a
            // fallback: a trigger with groups never reads them, so a write there would vanish without a word.
            if (selectedGroup < 0 || selectedGroup >= groups.arraySize) return null;
            var all = groups.GetArrayElementAtIndex(selectedGroup).FindPropertyRelative(nameof(ConditionGroup.all));
            if (selectedIndex < 0 || selectedIndex >= all.arraySize) return null;
            return all.GetArrayElementAtIndex(selectedIndex);
        }

        public EntryDetailView(AvatarDoctorView owner)
        {
            this.owner = owner;
            fields = new DirectFields(owner.SerializedObject, owner.ApplyAndRefresh);
            focusOutCallback = evt =>
            {
                // A FocusOutEvent fires whenever focus leaves any field, including a move to another field still
                // inside this panel; only a move that actually leaves the panel means the author is done with it.
                // A menu takes the focus while it is open without the author leaving the panel: the menu's own
                // close runs the rebuild.
                var related = evt.relatedTarget as VisualElement;
                if (related != null && (body.Contains(related) || related is AdMenu)) return;
                body.UnregisterCallback(focusOutCallback, TrickleDown.NoTrickleDown);
                focusOutRegistered = false;
                // The focus change is still in flight while this event is dispatched, so DeferForFocus would see
                // the old element or none; running the pending rebuild a tick later lets it finish first.
                schedule.Execute(RunPendingRebuild);
            };
            AddToClassList("ad-panel");
            var title = new VisualElement();
            title.AddToClassList("ad-panel-title");
            title.Add(glyph);
            title.Add(kindLabel);
            var spacer = new VisualElement();
            spacer.AddToClassList("ad-spacer");
            title.Add(spacer);
            titleChips.style.flexDirection = FlexDirection.Row;
            title.Add(titleChips);
            Add(title);
            body.AddToClassList("ad-panel-body");
            Add(body);
            sceneHandle = new ContactSceneHandle(this);
            RegisterCallback<AttachToPanelEvent>(_ => sceneHandle.Attach(Row));
            RegisterCallback<DetachFromPanelEvent>(_ => sceneHandle.Detach());
            Rebuild();
        }

        /// <summary>Rebuilds the panel from scratch even though its shape has not changed, for a change Shape does not track: turning first-use tips back on. Public for HeaderView's "Tips" button.</summary>
        public void ForceRebuild()
        {
            lastKey = null;
            Rebuild();
        }

        public void Show(TreeRow row)
        {
            // Another row means another rule: the condition this panel had open says nothing about that one, and
            // its place may not even exist there.
            if (row == null || row.Key != lastKey) { selectedGroup = 0; selectedIndex = 0; }
            Row = row;
            if (ShowsSamePanel(row)) RefreshDerived();
            else Rebuild();
            sceneHandle.Sync(row);
        }

        public void Refresh(AvatarPlan plan)
        {
            if (Row != null && Row.Key != null) Row = owner.Tree.RowForKey(Row.Key) ?? Row;
            if (ShowsSamePanel(Row)) RefreshDerived();
            else Rebuild();
            sceneHandle.Sync(Row);
        }

        /// <summary>Follows the owning view's mode. The mode is part of the panel's shape, so the panel is built again through the path every refresh takes: a rebuild held back by a focused field or an open menu still waits for it.</summary>
        internal void ApplyMode() => Refresh(owner.Plan);

        /// <summary>Rewrites the contact anchor's offset and radius fields from the entry, without a rebuild. Used after a Scene view handle drag writes them directly on the component.</summary>
        public void RefreshTouchFields() => fields.Reload();

        /// <summary>True when the panel already stands for this row and only its derived parts can have moved.</summary>
        bool ShowsSamePanel(TreeRow row)
        {
            return row != null && row.Key != null && row.Key == lastKey && Shape(row) == lastShape;
        }

        /// <summary>What the panel's layout is made of: the row, the bound entry, merged animator or installed menu asset (by the place it has in its list, which an Undo or a delete in front of it moves) and the choices that decide which fields exist.</summary>
        string Shape(TreeRow row)
        {
            if (row == null) return null;
            var entry = row.Mine ? row.Entry : null;
            var property = entry != null ? owner.EntryProperty(entry) : null;
            bool trigger = entry != null && entry.kind == EntryKind.Trigger;
            return row.Kind + "|" + row.Mine + "|" + (property != null ? property.propertyPath : "")
                + "|" + (entry != null ? (int)entry.kind : -1)
                + "|" + (entry != null && !string.IsNullOrWhiteSpace(entry.parameterName))
                + "|" + (entry != null && !string.IsNullOrWhiteSpace(entry.exclusiveGroup))
                + "|" + (trigger ? (int)entry.source : -1)
                + "|" + (trigger ? (int)entry.touchMode : -1)
                + "|" + (trigger && OffersValueFade(entry))
                + "|" + (trigger ? (int)entry.linkStyle : -1)
                + "|" + (trigger && entry.source == TriggerSource.PhysBone && TouchPanel.IsFloatEvent(entry.physBoneEvent))
                + "|" + (trigger && entry.source == TriggerSource.Contact && entry.contactProximity > 0f)
                + "|" + (trigger && entry.source == TriggerSource.Contact ? (entry.contactTags != null ? entry.contactTags.Count : 0) : -1)
                + "|" + (trigger && entry.condition != ParameterCondition.IsTrue && entry.condition != ParameterCondition.IsFalse)
                + "|" + (trigger && entry.sourceRenderer != null ? entry.sourceRenderer.GetInstanceID() : 0)
                + "|" + (trigger ? entry.sourceShape : "")
                + "|" + (trigger ? entry.sourceToggleId : "")
                + "|" + (trigger && entry.sourceToggleOn)
                + "|" + (trigger ? entry.gateToggleId : "")
                + "|" + (trigger && entry.gateToggleOn)
                + "|" + (trigger ? WhenShape(entry) : "")
                + "|" + (trigger ? SelectedConditionKey : "")
                + "|" + (trigger ? ConditionLogic.Sentence(entry, owner.Plan.Tree) : "")
                + "|" + (entry != null ? ShapeUsage(entry).Count : 0)
                + "|" + (entry != null ? entry.properties.Count : -1)
                + "|" + (entry != null ? string.Join(",", entry.properties.Select(PropertyShapeToken)) : "")
                + "|" + (entry != null ? entry.animations.Count : -1)
                + "|" + (entry != null && entry.kind == EntryKind.Preset ? entry.presetStates.Count : -1)
                + "|" + (entry != null && entry.kind == EntryKind.Preset ? entry.presetParameters.Count : -1)
                + "|" + PresetStatesShape(entry)
                + "|" + (owner.Menu != null && (row.Kind == RowKind.ParametersGroup || row.Kind == RowKind.Parameter) ? owner.Menu.parameters.Count : -1)
                + "|" + (row.Asset != null ? row.Asset.id : "")
                + "|" + (row.Merge != null ? owner.MergeProperty(row.Merge)?.propertyPath : "")
                + "|" + (row.Asset != null ? owner.MenuAssetProperty(row.Asset)?.propertyPath : "")
                + "|" + EditShape(row.Item)
                + "|" + owner.Simple;
        }

        /// <summary>What the cards of a trigger's "when" are made of: the kind of every condition and the choices inside it that decide which fields its row shows.</summary>
        static string WhenShape(MenuEntry entry)
        {
            if (!entry.UsesWhenGroups) return "";
            return string.Join(";", entry.when.Select(g => g == null || g.all == null ? "" : string.Join(",", g.all.Select(ConditionShapeToken))));
        }

        static string ConditionShapeToken(WhenCondition condition)
        {
            if (condition == null) return "-";
            return (int)condition.kind
                + ":" + (condition.condition != ParameterCondition.IsTrue && condition.condition != ParameterCondition.IsFalse)
                + ":" + (condition.contactProximity > 0f)
                + ":" + (condition.contactTags != null ? condition.contactTags.Count : 0)
                + ":" + TouchPanel.IsFloatEvent(condition.physBoneEvent);
        }

        /// <summary>Each preset state's resolved control (its id, kind and current menu path) or "missing", so a rename, a delete, or a kind change made from another AvatarDoc's inspector forces this panel to rebuild instead of showing a stale row.</summary>
        string PresetStatesShape(MenuEntry entry)
        {
            if (entry == null || entry.kind != EntryKind.Preset) return "";
            return string.Join(";", entry.presetStates.Select(state =>
            {
                var control = owner.Plan.Tree.FindControl(state.entryId);
                return control == null ? "missing" : control.Entry.id + ":" + (int)control.Entry.kind + ":" + control.Path;
            }));
        }

        /// <summary>The parts of an existing or foreign row's edit that change what the panel shows: whether it has one, is hidden, is moved and where to, its name and its icon. Keeps the panel's Move to field and Name field current across a second move or rename that does not otherwise change the row's Kind.</summary>
        static string EditShape(MenuItem item)
        {
            if (item == null) return "";
            var edit = item.Edit;
            return (edit != null) + ":" + item.Hidden + ":" + (edit != null && edit.remove) + ":" + (edit != null && edit.move)
                + ":" + (edit != null ? edit.targetPath : "")
                + ":" + (edit != null ? edit.name : "")
                + ":" + (edit != null && edit.icon != null ? edit.icon.GetInstanceID() : 0);
        }

        /// <summary>The clips, across the entry's blend shapes, that Shape(row) counts so an animation import refreshes the panel and its chips.</summary>
        List<AnimationClip> ShapeUsage(MenuEntry entry)
            => entry.blendShapes.SelectMany(t => owner.Plan.Usage.ClipsFor(t.renderer, t.shape)).Distinct().ToList();

        /// <summary>What a property target's row is built from: its name and type (a rename swaps which value control shows), whether its renderer is set, and whether it is a range and, if so, its limits, so switching between two properties of the same type still rebuilds the row when their ranges differ.</summary>
        static string PropertyShapeToken(MaterialPropertyTarget target)
        {
            var match = PropertyPicker.Choices(target.renderer).FirstOrDefault(c => c.PropertyName == target.property);
            bool found = !string.IsNullOrEmpty(target.property) && match.PropertyName == target.property;
            return target.property + ":" + (int)target.type + ":" + (target.renderer != null)
                + ":" + (found && match.IsRange) + ":" + (found ? match.RangeMin : 0f) + ":" + (found ? match.RangeMax : 0f);
        }

        /// <summary>
        /// Holds a rebuild back while the focus sits in the panel, so a refresh cannot pull the field the author is
        /// typing in out from under them, and while one of the view's menus is open, which may hang on a button
        /// of this panel. The held rebuild runs as soon as the focus leaves or the menu closes.
        /// </summary>
        bool DeferForFocus()
        {
            if (panel == null) return false;
            if (owner.MenuOpen)
            {
                pendingRebuild = true;
                return true;
            }
            var focused = panel.focusController?.focusedElement as VisualElement;
            if (focused == null || !body.Contains(focused)) return false;
            pendingRebuild = true;
            if (!focusOutRegistered)
            {
                focusOutRegistered = true;
                body.RegisterCallback(focusOutCallback, TrickleDown.NoTrickleDown);
                // A rebuild deferred while a delayed field saves on its Blur is registered after that focus move's
                // FocusOut was already sent, so the callback above would wait for a second one; the next tick
                // looks again.
                schedule.Execute(RunPendingRebuild);
            }
            return true;
        }

        /// <summary>Runs a rebuild that was held back, if one is still owed. The owning view calls it when its last menu closes.</summary>
        public void RunPendingRebuild()
        {
            if (pendingRebuild) Rebuild();
        }

        /// <summary>
        /// Rebuilds the panel and applies the lock state. Applied before the focus deferral as well as after the
        /// content: a rebuild held back while a field has focus would otherwise leave the body of a component
        /// that was locked in the meantime enabled until the focus moves.
        /// </summary>
        void Rebuild()
        {
            body.SetEnabled(!owner.Locked);
            if (DeferForFocus())
            {
                HoldLists();
                return;
            }
            RebuildContent();
            body.SetEnabled(!owner.Locked);
        }

        /// <summary>The panel stays as it is until the focus leaves, so its target lists stay live and a dragged row would be moved at the place the list was bound to, which holds another entry once an Undo or a removal moved this one.</summary>
        void HoldLists()
        {
            foreach (var targets in body.Query<TargetListView>().ToList()) targets.Hold();
        }

        /// <summary>Whether a write to this component's entry data is refused. Every setter below writes the entry array directly, so each one asks here rather than leaning on the body's disabled state.</summary>
        bool Refuses => EntryEditing.Refuses(owner.Menu);

        void RebuildContent()
        {
            // The ids the fields and lists note below come from this copy of the data, which has to be the component's
            // as it stands: another view or a script may have moved an entry since this view last refreshed.
            owner.SerializedObject.Update();
            pendingRebuild = false;
            RebuildCount++;
            bool hadStyleRows = nameRow != null && nameRow.StyleRowsShown;
            bool hadGroupRow = groupRowBuilt;
            bool hadAdvancedFold = advancedFold != null && advancedFold.ShownSinceBuilt;
            groupRowBuilt = false;
            fields.Clear();
            body.Clear();
            titleChips.Clear();
            messageBoxes.Clear();
            nameRow = null;
            submenuName = null;
            slotNote = null;
            parameterName = null;
            parameterChip = null;
            parameterCustom = null;
            parameterNote = null;
            parameterShared = null;
            groupOwnerNote = null;
            previewDefault = null;
            previewChoice = null;
            previewSlider = null;
            touchTestNote = null;
            syncedCost = null;
            buildsLine = null;
            advancedFold = null;
            effectSections.Clear();
            offeredSections.Clear();
            BoundPropertyPath = null;
            boundEntryId = null;
            var row = Row;
            if (row == null || row.Key != lastKey) TouchMoreTagsOpen = false;
            lastKey = row?.Key;
            lastShape = Shape(row);
            string decidedFor = row == null ? null : row.Key + "|" + owner.Simple;
            bool sameDecision = decidedFor != null && decidedFor == keptFor;
            keptFor = decidedFor;
            keptStyleRows = sameDecision && hadStyleRows;
            keptGroupRow = sameDecision && hadGroupRow;
            keptAdvancedFold = sameDecision && hadAdvancedFold;
            if (row == null) { TitleNoGlyph("ad.ui.detail.none"); Note(L.Text("ad.ui.detail.select_hint")); return; }

            if (owner.Simple && IsAdvancedItem(row)) Note(L.Text("ad.ui.advanced_item.note")).name = "ad-advanced-item-note";

            switch (row.Kind)
            {
                case RowKind.More:
                    Title(GlyphKind.Existing, "ad.ui.existing");
                    Note(string.Format(L.Text("ad.ui.existing_note"), row.Name));
                    return;
                case RowKind.Existing:
                case RowKind.Foreign:
                    Title(GlyphKind.Existing, "ad.ui.existing.title");
                    RefreshChips(row);
                    var existingPanel = new ExistingItemPanel(this, owner, row);
                    nameRow = existingPanel.NameRow;
                    body.Add(existingPanel);
                    return;
                case RowKind.TriggersGroup:
                    Title(GlyphKind.Group, "ad.ui.triggers");
                    Note(L.Text("ad.ui.trigger_note"));
                    return;
                case RowKind.OverridesGroup:
                    Title(GlyphKind.Group, "ad.ui.overrides");
                    Note(L.Text("ad.ui.override_note"));
                    return;
                case RowKind.AnimatorsGroup:
                    Title(GlyphKind.Group, "ad.ui.animators");
                    Note(L.Text("ad.ui.animator_note"));
                    return;
                case RowKind.Animator:
                    BuildAnimatorPanel(row);
                    return;
                case RowKind.MenuAssetsGroup:
                    Title(GlyphKind.Group, "ad.ui.menu_assets");
                    Note(L.Text("ad.ui.menu_assets.tip"));
                    return;
                case RowKind.MenuAsset:
                    BuildMenuAssetPanel(row);
                    return;
                case RowKind.ParametersGroup:
                case RowKind.Parameter:
                    BuildParametersPanel(row);
                    return;
            }

            if (!row.Mine)
            {
                Title(GlyphFor(row), KindKey(row));
                RefreshChips(row);
                Note(string.Format(L.Text("ad.ui.other_component"), row.Owner != null ? row.Owner.gameObject.name : ""));
                var select = new Button(() => { if (row.Owner != null) Selection.activeObject = row.Owner; }) { text = L.Text("ad.ui.select_component"), name = "ad-select-component", tooltip = L.Text("ad.ui.select_component.tip") };
                select.AddToClassList("ad-primary");
                body.Add(select);
                return;
            }

            var property = owner.EntryProperty(row.Entry);
            if (property == null) { Note(L.Text("ad.ui.detail.none")); return; }
            BoundPropertyPath = property.propertyPath;
            boundEntryId = row.Entry.id;
            Title(GlyphFor(row), KindKey(row));
            var tip = TipBanner.Build(owner, TipKindFor(row.Entry.kind), TipTextKeyFor(row.Entry.kind));
            if (tip != null) body.Add(tip);
            body.Add(NameRow(row, property));
            bool trimmed = Trimmed(row.Entry);
            if (!trimmed)
            {
                buildsLine = Note("");
                buildsLine.name = "ad-builds-line";
                buildsLine.tooltip = L.Text("ad.ui.builds.tip");
                RefreshBuildsLine(row);
            }
            advancedFold = new AdvancedFold(ComponentFold.KeyForEntry(row.Entry.id, "advanced"), owner.ViewPrefs);
            advancedFold.HiddenWhileDefault = trimmed;
            if (keptAdvancedFold) advancedFold.KeepShown();
            advancedFold.Content.Add(PlatformRow(property));

            switch (row.Entry.kind)
            {
                case EntryKind.Trigger:
                    BuildTriggerBody(row, property);
                    body.Add(PreviewRow(row.Entry));
                    break;
                case EntryKind.Submenu:
                    body.Add(IconRow(row, property));
                    slotNote = Note(string.Format(L.Text("ad.ui.slots"), row.Slots, VRCExpressionsMenu.MAX_CONTROLS));
                    break;
                case EntryKind.Radial:
                    body.Add(IconRow(row, property));
                    var defaultValueField = fields.Slider(property.FindPropertyRelative(nameof(MenuEntry.defaultValue)), L.Text("ad.ui.default_value"), 0f, 1f, owner.ApplyLive);
                    defaultValueField.name = "ad-default-value";
                    defaultValueField.tooltip = L.Text("ad.ui.default_value.tip");
                    body.Add(Bound(defaultValueField));
                    body.Add(VisibleFlagsRow(property, includeDefault: false));
                    advancedFold.Content.Add(SavedSyncedRow(row, property));
                    advancedFold.Content.Add(ParameterRow(row, property));
                    if (row.Entry.IsParameterControl) Note(L.Text("ad.ui.parameter_control_note")).name = "ad-parameter-control-note";
                    AddEffectSections(new List<(string NameKey, string DescKey, TargetListView View, bool AlwaysVisible)>
                    {
                        ("ad.ui.blendshapes", "ad.ui.add_effect.blendshapes", new TargetListView(owner, property.FindPropertyRelative(nameof(MenuEntry.blendShapes)), TargetListView.Kind.BlendShapes, radial: true, captureEntry: row.Entry), true),
                        ("ad.ui.targets.properties", "ad.ui.add_effect.properties", new TargetListView(owner, property.FindPropertyRelative(nameof(MenuEntry.properties)), TargetListView.Kind.Properties, radial: true), false),
                        ("ad.ui.targets.animations", "ad.ui.add_effect.animations", new TargetListView(owner, property.FindPropertyRelative(nameof(MenuEntry.animations)), TargetListView.Kind.Animations, radial: false), false),
                    }, offerOnlyTheBasics: trimmed);
                    if (!trimmed)
                    {
                        Note(L.Text("ad.ui.animations.no_preview"));
                        body.Add(PreviewRow(row.Entry));
                    }
                    break;
                case EntryKind.Override:
                    body.Add(new TargetListView(owner, property.FindPropertyRelative(nameof(MenuEntry.blendShapes)), TargetListView.Kind.BlendShapes, radial: false, link: false, pinned: true, captureEntry: row.Entry));
                    body.Add(UsageList(row.Entry));
                    body.Add(DisabledPreviewRow("ad.ui.preview.always"));
                    break;
                case EntryKind.Preset:
                    body.Add(IconRow(row, property));
                    body.Add(new PresetPanel(this, owner, row, property));
                    break;
                case EntryKind.Grabbable:
                    body.Add(new GrabPanel(this, owner, property));
                    break;
                default:
                    body.Add(IconRow(row, property));
                    if (!trimmed || keptGroupRow || !string.IsNullOrWhiteSpace(row.Entry.exclusiveGroup))
                    {
                        body.Add(GroupRow(row, property));
                        groupOwnerNote = new Label { name = "ad-group-owner" };
                        groupOwnerNote.AddToClassList("ad-note");
                        groupOwnerNote.AddToClassList("ad-note-indent");
                        body.Add(groupOwnerNote);
                        groupRowBuilt = true;
                    }
                    body.Add(VisibleFlagsRow(property, includeDefault: true));
                    advancedFold.Content.Add(SavedSyncedRow(row, property));
                    advancedFold.Content.Add(BlendTimeRow(property, !string.IsNullOrWhiteSpace(row.Entry.exclusiveGroup)));
                    advancedFold.Content.Add(ParameterRow(row, property));
                    if (row.Entry.IsParameterControl) Note(L.Text("ad.ui.parameter_control_note")).name = "ad-parameter-control-note";
                    AddEffectSections(new List<(string NameKey, string DescKey, TargetListView View, bool AlwaysVisible)>
                    {
                        ("ad.ui.objects", "ad.ui.add_effect.objects", new TargetListView(owner, property.FindPropertyRelative(nameof(MenuEntry.objects)), TargetListView.Kind.Objects, false), true),
                        // Only the rule is toggle-only; Capture itself now shows on every blendshape section (TargetListView.ShowsRule gates the rule line).
                        // Advanced shows this section once it has a row or is added; Simple always shows it, with its Capture.
                        ("ad.ui.blendshapes", "ad.ui.add_effect.blendshapes", new TargetListView(owner, property.FindPropertyRelative(nameof(MenuEntry.blendShapes)), TargetListView.Kind.BlendShapes, radial: false, captureEntry: row.Entry), trimmed),
                        ("ad.ui.materials", "ad.ui.add_effect.materials", new TargetListView(owner, property.FindPropertyRelative(nameof(MenuEntry.materials)), TargetListView.Kind.Materials, false), false),
                        ("ad.ui.targets.properties", "ad.ui.add_effect.properties", new TargetListView(owner, property.FindPropertyRelative(nameof(MenuEntry.properties)), TargetListView.Kind.Properties, radial: false), false),
                        ("ad.ui.targets.animations", "ad.ui.add_effect.animations", new TargetListView(owner, property.FindPropertyRelative(nameof(MenuEntry.animations)), TargetListView.Kind.Animations, radial: false), false),
                        ("ad.ui.targets.follows", "ad.ui.add_effect.follows", new TargetListView(owner, property.FindPropertyRelative(nameof(MenuEntry.follows)), TargetListView.Kind.Follows, radial: false), false),
                    }, offerOnlyTheBasics: trimmed);
                    if (!trimmed)
                    {
                        Note(L.Text("ad.ui.animations.no_preview"));
                        body.Add(PreviewRow(row.Entry));
                    }
                    break;
            }

            body.Add(advancedFold);
            body.Add(messageBoxes);
            RefreshDerived();
        }

        /// <summary>The Controller, Path root, Platform and layer/parameter lists of a merged animator row.</summary>
        void BuildAnimatorPanel(TreeRow row)
        {
            Title(GlyphKind.Animator, "ad.ui.detail.animator");
            RefreshChips(row);
            if (!row.Mine)
            {
                Note(string.Format(L.Text("ad.ui.other_component"), row.Owner != null ? row.Owner.gameObject.name : ""));
                var select = new Button(() => { if (row.Owner != null) Selection.activeObject = row.Owner; }) { text = L.Text("ad.ui.select_component"), name = "ad-select-component", tooltip = L.Text("ad.ui.select_component.tip") };
                select.AddToClassList("ad-primary");
                body.Add(select);
                return;
            }
            var mergeProperty = owner.MergeProperty(row.Merge);
            if (mergeProperty == null) { Note(L.Text("ad.ui.detail.none")); return; }
            BoundPropertyPath = mergeProperty.propertyPath;
            var animatorTip = TipBanner.Build(owner, TipKinds.Animator, "ad.tip.animator");
            if (animatorTip != null) body.Add(animatorTip);
            body.Add(new AnimatorPanel(this, owner, row, mergeProperty));
            body.Add(messageBoxes);
            RefreshDerived();
        }

        /// <summary>The asset, the place it installs into, its name, icon and platform, and the parameters its controls drive.</summary>
        void BuildMenuAssetPanel(TreeRow row)
        {
            Title(GlyphKind.Existing, "ad.ui.detail.menu_asset");
            RefreshChips(row);
            if (!row.Mine)
            {
                Note(string.Format(L.Text("ad.ui.other_component"), row.Owner != null ? row.Owner.gameObject.name : ""));
                var select = new Button(() => { if (row.Owner != null) Selection.activeObject = row.Owner; }) { text = L.Text("ad.ui.select_component"), name = "ad-select-component", tooltip = L.Text("ad.ui.select_component.tip") };
                select.AddToClassList("ad-primary");
                body.Add(select);
                return;
            }
            var property = owner.MenuAssetProperty(row.Asset);
            if (property == null) { Note(L.Text("ad.ui.detail.none")); return; }
            BoundPropertyPath = property.propertyPath;
            var menuAssetTip = TipBanner.Build(owner, TipKinds.MenuAsset, "ad.tip.menu_asset");
            if (menuAssetTip != null) body.Add(menuAssetTip);
            body.Add(new MenuAssetPanel(this, owner, row, property));
            body.Add(messageBoxes);
            RefreshDerived();
        }

        /// <summary>The compact table of declared parameters. Both the group row and one declaration's row show it.</summary>
        void BuildParametersPanel(TreeRow row)
        {
            Title(GlyphKind.Group, "ad.ui.parameters");
            RefreshChips(row);
            if (!row.Mine)
            {
                Note(string.Format(L.Text("ad.ui.other_component"), row.Owner != null ? row.Owner.gameObject.name : ""));
                var select = new Button(() => { if (row.Owner != null) Selection.activeObject = row.Owner; }) { text = L.Text("ad.ui.select_component"), name = "ad-select-component", tooltip = L.Text("ad.ui.select_component.tip") };
                select.AddToClassList("ad-primary");
                body.Add(select);
                return;
            }
            Note(L.Text("ad.ui.parameters.tip"));
            body.Add(new ParametersPanel(this, owner));
            body.Add(messageBoxes);
            RefreshDerived();
        }

        /// <summary>Declares a parameter an installed menu asset drives but nobody provides, with the type its controls imply. Public so the panel's button and a test call the same thing.</summary>
        public ParameterDeclaration AddDeclarationForParameter(string parameterName)
        {
            if (Refuses || Row == null || Row.Kind != RowKind.MenuAsset || Row.Asset == null || string.IsNullOrWhiteSpace(parameterName)) return null;
            var declaration = EntryEditing.AddDeclaration(owner.Menu, parameterName, MenuAssetPanel.TypeFor(Row.Asset.menu, parameterName.Trim()));
            if (declaration == null) return null;
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
            return declaration;
        }

        /// <summary>Adds an empty declaration for the creator to name. Public so the table's button and a test call the same thing.</summary>
        public ParameterDeclaration AddDeclarationRow()
        {
            if (Refuses) return null;
            var declaration = EntryEditing.AddDeclaration(owner.Menu, EntryEditing.NewDeclarationName(owner.Menu), PlannedType.Bool);
            if (declaration == null) return null;
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
            return declaration;
        }

        /// <summary>Removes one declaration from the table.</summary>
        public void RemoveDeclarationRow(ParameterDeclaration declaration)
        {
            if (Refuses || !EntryEditing.RemoveDeclaration(owner.Menu, declaration)) return;
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
        }

        /// <summary>Checks or unchecks one layer of the selected merged animator, writing skipLayers through the guarded, Undo-recorded writer.</summary>
        public void SetAnimatorLayerSkipped(string layerName, bool skipped)
        {
            if (Row == null || Row.Kind != RowKind.Animator || Row.Merge == null) return;
            if (EntryEditing.SetLayerSkipped(owner.Menu, Row.Merge, layerName, skipped)) AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
        }

        /// <summary>
        /// Creates a menu entry that drives this parameter and nothing else: a Bool becomes a toggle, a Float a
        /// radial, an Int a toggle whose custom value is 1. Public so the "Add control" button of the animator
        /// panel and tests both call the same thing.
        /// </summary>
        public MenuEntry AddControlForParameter(string parameterName)
        {
            if (Refuses || Row == null || Row.Kind != RowKind.Animator || Row.Merge == null || string.IsNullOrWhiteSpace(parameterName)) return null;
            var declared = MergeBuilder.ParametersOf(Row.Merge.controller);
            var type = declared.FirstOrDefault(p => p.name == parameterName).type;
            var kind = type == AnimatorControllerParameterType.Float ? EntryKind.Radial : EntryKind.Toggle;
            var entry = EntryEditing.Add(owner.Menu, kind, parameterName, "");
            if (entry == null) return null;
            entry.parameterName = parameterName;
            EntryEditing.MarkChanged(owner.Menu);
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
            return entry;
        }

        /// <summary>Rewrites the parts of a built panel that come from the plan and the tree, leaving the bound fields and the focus where they are.</summary>
        void RefreshDerived()
        {
            body.SetEnabled(!owner.Locked);
            var row = Row;
            if (row == null) return;
            fields.Reload();
            foreach (var targets in body.Query<TargetListView>().ToList()) targets.Reload();
            // A section that just gained or lost its last row or rule shows or hides at once: the always visible
            // one (the kind's common starting place) never does, whatever it holds.
            foreach (var section in effectSections)
                if (!section.AlwaysVisible) section.View.EnableInClassList("ad-hidden", !section.View.HasContent);
            RefreshAdvancedFoldDot(row);
            foreach (var animatorPanel in body.Query<AnimatorPanel>().ToList()) animatorPanel.Rebuild();
            foreach (var assetPanel in body.Query<MenuAssetPanel>().ToList()) assetPanel.Rebuild();
            RefreshChips(row);
            var focused = panel?.focusController?.focusedElement as VisualElement;
            // The whole name row is protected: the plain field, the raw tags field and the style controls all hold
            // state the author may be in the middle of changing.
            bool editingName = focused != null && nameRow != null && (focused == nameRow || nameRow.Contains(focused));
            if (!editingName && nameRow != null)
            {
                if (row.Entry != null) nameRow.Load(row.Entry.name);
                else if (row.Item != null) nameRow.Load(ExistingItemPanel.CurrentName(row.Item));
            }
            if (slotNote != null) slotNote.text = string.Format(L.Text("ad.ui.slots"), row.Slots, VRCExpressionsMenu.MAX_CONTROLS);
            RefreshParameter(row);
            RefreshGroupOwner(row);
            RefreshSyncedCost(row);
            RefreshBuildsLine(row);
            RefreshMessages(row);
            if (previewDefault != null || previewChoice != null) RefreshPreviewRow(row.Entry);
            RefreshTouchTestNote(row);
        }

        /// <summary>What the Synced switch costs right now, from the same parameter plan the row's binding already carries: never a second calculation of what a Bool or an Int/Float costs.</summary>
        void RefreshSyncedCost(TreeRow row)
        {
            if (syncedCost == null) return;
            int bits = 0;
            if (row.Control != null && owner.Plan.Plan.Bindings.TryGetValue(row.Control, out var binding) && !binding.Parameter.ReusesExisting)
                bits = binding.Parameter.Bits;
            syncedCost.text = bits == 0 ? L.Text("ad.ui.synced.cost.none") : bits == 1 ? L.Text("ad.ui.synced.cost.bit") : L.Text("ad.ui.synced.cost.bits");
        }

        /// <summary>The "What this builds" line: menu controls, synced bits, FX layers and components, read from the plan the row's own control or trigger already has, not guessed.</summary>
        void RefreshBuildsLine(TreeRow row)
        {
            if (buildsLine == null) return;
            buildsLine.text = WhatThisBuildsModel.Describe(owner.Plan, row);
        }

        /// <summary>Puts the focus on a field of the currently built panel by its element name, for "Show" to land on a message's known field. Does nothing when the panel does not currently show that field.</summary>
        public bool FocusField(string elementName)
        {
            var element = body.Q<VisualElement>(elementName);
            if (element == null) return false;
            element.Focus();
            return true;
        }

        void RefreshChips(TreeRow row)
        {
            titleChips.Clear();
            if (row.Kind == RowKind.Existing || row.Kind == RowKind.Foreign)
            {
                if (!string.IsNullOrEmpty(row.SourceChip)) titleChips.Add(Chip(row.SourceChip, "ad-chip-source"));
                if (row.Edited) titleChips.Add(Chip(L.Text("ad.ui.chip.edited"), "ad-chip-on"));
                if (row.Removed) titleChips.Add(Chip(L.Text("ad.ui.chip.removed"), "ad-chip-warn"));
                else if (row.Hidden) titleChips.Add(Chip(L.Text("ad.ui.chip.hidden"), "ad-chip-warn"));
                return;
            }
            if (!row.Mine && row.Kind != RowKind.More)
                titleChips.Add(Chip(row.SourceChip ?? "", "ad-chip-source"));
            if (row.PlatformChip != null)
                titleChips.Add(Chip(row.PlatformChip, row.Excluded ? "ad-chip-warn" : "ad-chip-existing"));
            if (row.NoMenu) titleChips.Add(Chip(L.Text("ad.ui.chip.no_menu"), "ad-chip-warn", L.Text("ad.ui.chip.no_menu.tip")));
        }

        void RefreshParameter(TreeRow row)
        {
            if (parameterName == null) return;
            string text = "";
            if (row.Control != null && owner.Plan.Plan.Bindings.TryGetValue(row.Control, out var binding))
            {
                parameterName.text = binding.Parameter.Name;
                int bits = binding.Parameter.Bits;
                bool none = binding.Parameter.ReusesExisting || binding.Parameter.IsAnimatorOnly || bits == 0;
                text = none ? string.Format(L.Text("ad.ui.parameter.no_bits"), binding.Parameter.Type) : string.Format(L.Text(UiText.CountKey(bits, "ad.ui.parameter.bits")), binding.Parameter.Type, bits);
            }
            else parameterName.text = "";
            parameterNote.text = text;
            parameterNote.EnableInClassList("ad-hidden", text.Length == 0);
            if (parameterShared == null) return;
            // Sharing a custom name stays allowed: the row only says who else uses it, a toggle or radial of another
            // component, or another component's own Parameters list. A group member uses its group's parameter, which
            // a custom name left on it does not name.
            string shared = "";
            if (row.Entry != null && !string.IsNullOrWhiteSpace(row.Entry.parameterName) && row.Control != null
                && owner.Plan.Plan.Bindings.TryGetValue(row.Control, out var own) && own.Parameter.Group.IsNone)
            {
                var custom = row.Entry.parameterName.Trim();
                var mine = row.Control.Component;
                var others = own.Parameter.Controls.Where(c => c.Component != mine).Select(c => L.Aside(c.Entry.PlainName, c.Component.gameObject.name))
                    .Concat(owner.Plan.Tree.Components.Where(c => c != null && c != mine && !MenuModel.IsDisabled(c) && c.parameters.Any(d => d != null && (d.name ?? "").Trim() == custom))
                        .Select(c => L.Aside(L.Text("ad.ui.parameters"), c.gameObject.name)))
                    .ToList();
                if (others.Count > 0) shared = L.Labelled(L.Text("ad.ui.parameter.also_used_by"), L.JoinList(others));
            }
            parameterShared.text = shared;
            parameterShared.EnableInClassList("ad-hidden", shared.Length == 0);
        }

        /// <summary>Shows what the name field currently reads, while it is being typed in. The entry itself is written only when the field commits.</summary>
        public void SetParameterNameLive(string text)
        {
            if (parameterChip == null || parameterCustom == null) return;
            bool custom = !string.IsNullOrWhiteSpace(text);
            parameterChip.text = L.Text(custom ? "ad.ui.parameter.custom" : "ad.ui.parameter.auto");
            parameterChip.EnableInClassList("ad-chip-on", custom);
            parameterChip.EnableInClassList("ad-chip-existing", !custom);
            parameterCustom.SetValueWithoutNotify(custom);
        }

        void RefreshMessages(TreeRow row)
        {
            messageBoxes.Clear();
            AddMessageBoxes(messageBoxes, row.Messages, MessageAction);
        }

        /// <summary>The way out of a message that has one, shown in a row under its box.</summary>
        VisualElement MessageAction(ModelMessage message)
        {
            if (message.Says("ad.warn.poiyomi_not_animated")) return Row != null && Row.Mine ? MarkAnimatedAction(message) : null;
            if (message.Says("ad.info.group_same_name")) return JoinAction(message);
            if (!message.Says("ad.error.group_value_conflict")) return null;
            var row = new VisualElement();
            row.AddToClassList("ad-message-actions");
            row.Add(new Button(NumberGroupMembers) { text = L.Text("ad.ui.group.number"), name = "ad-group-number", tooltip = L.Text("ad.ui.group.number.tip") });
            return row;
        }

        /// <summary>
        /// The Join them button under the note about two groups of one name. The note names the component whose group
        /// the toggles join (Args[0]), the one whose toggles move (Args[1]), already turned round when only that one
        /// is locked, and the name (Args[2]); the button is greyed out only while both sides are locked.
        /// </summary>
        VisualElement JoinAction(ModelMessage message)
        {
            if (message.Args == null || message.Args.Length < 3 || !(message.Args[0] is AvatarDoc into) || !(message.Args[1] is AvatarDoc from) || !(message.Args[2] is string name)) return null;
            bool refused = !EntryEditing.CanJoinGroup(owner.Plan?.Tree, from, name, into);
            var row = new VisualElement();
            row.AddToClassList("ad-message-actions");
            var tooltip = refused ? L.Text("ad.ui.group.join.locked.tip") : string.Format(L.Text("ad.ui.group.join.tip"), from.gameObject.name, into.gameObject.name);
            var button = new Button(() => JoinGroup(from, name, into)) { text = L.Text("ad.ui.group.join"), name = "ad-group-join", tooltip = tooltip };
            button.SetEnabled(!refused);
            row.Add(button);
            return row;
        }

        void JoinGroup(AvatarDoc from, string name, AvatarDoc into)
        {
            if (owner.Plan?.Tree == null || !EntryEditing.JoinGroup(owner.Plan.Tree, from, name, into, out var changed)) return;
            foreach (var component in changed) AvatarPlanCache.NoteSelfEdit(component);
            AvatarPlanCache.InvalidateMenu(owner.Plan.AvatarRoot);
            owner.Refresh();
        }

        /// <summary>The Mark as animated button under a Poiyomi warning. The message names the property (Args[1]) and the renderer (Args[2]).</summary>
        VisualElement MarkAnimatedAction(ModelMessage message)
        {
            if (message.Args == null || message.Args.Length < 3 || !(message.Args[1] is string property) || !(message.Args[2] is Renderer renderer)) return null;
            var row = new VisualElement();
            row.AddToClassList("ad-message-actions");
            row.Add(new Button(() => MarkAnimated(renderer, property)) { text = L.Text("ad.ui.poiyomi.mark_animated"), name = "ad-poiyomi-mark", tooltip = L.Text("ad.ui.poiyomi.mark_animated.tip") });
            return row;
        }

        void MarkAnimated(Renderer renderer, string property)
        {
            if (owner.Plan?.Tree == null || Row?.Entry == null || !Row.Mine) return;
            var holder = Row.Owner != null ? Row.Owner : owner.Menu;
            if (!EntryEditing.MarkAnimatedForPoiyomi(holder, Row.Entry, renderer, property, owner.Plan.Tree.SwappedOnto(renderer))) return;
            AvatarPlanCache.InvalidateMenu(owner.Plan.AvatarRoot);
            owner.Refresh();
        }

        /// <summary>Numbers the members of the group the shown toggle is in.</summary>
        void NumberGroupMembers()
        {
            if (Refuses || owner.Plan?.Plan == null || Row?.Control == null) return;
            if (!owner.Plan.Plan.Bindings.TryGetValue(Row.Control, out var binding) || binding.Parameter.Group.IsNone) return;
            var parameter = binding.Parameter;
            if (!EntryEditing.NumberGroupMembers(parameter.Controls)) return;
            foreach (var component in parameter.Controls.Select(c => c.Component).Distinct()) AvatarPlanCache.NoteSelfEdit(component);
            AvatarPlanCache.InvalidateMenu(owner.Plan.AvatarRoot);
            owner.Refresh();
        }

        /// <summary>One warning, error or info box per message, styled the way every entry panel already shows them. Shared with ExistingItemPanel.</summary>
        public static void AddMessageBoxes(VisualElement container, IEnumerable<ModelMessage> messages, System.Func<ModelMessage, VisualElement> actionFor = null)
        {
            foreach (var message in messages)
            {
                var box = new VisualElement();
                box.AddToClassList("ad-warnbox");
                box.EnableInClassList("ad-error", message.Severity == MessageSeverity.Error);
                box.EnableInClassList("ad-info", message.Severity == MessageSeverity.Info);
                box.Add(UiText.MessageLabel(UiText.Format(message)));
                var learnMore = DocsLinks.LearnMoreButton(message.Key);
                if (learnMore != null) box.Add(learnMore);
                container.Add(box);
                var action = actionFor?.Invoke(message);
                if (action != null) container.Add(action);
            }
        }

        /// <summary>Default, On and Off (or a slider for radials) writing PreviewState for this entry; links only follow their source.</summary>
        VisualElement PreviewRow(MenuEntry entry)
        {
            var container = new VisualElement();
            container.AddToClassList("ad-preview");
            var buttons = new VisualElement();
            buttons.AddToClassList("ad-row");
            var label = new Label(L.Text("ad.ui.preview"));
            label.AddToClassList("ad-label");
            label.tooltip = L.Text("ad.ui.preview.tip");
            buttons.Add(label);
            container.Add(buttons);
            // The preview reads toggles itself; a parameter, a contact or a PhysBone it cannot know counts as met.
            if (entry.HasDriver && entry.Requirements().Any(r => r.condition.kind != WhenConditionKind.Toggle))
            {
                var uncheckedNote = new Label(L.Text("ad.ui.preview.unchecked")) { name = "ad-preview-unchecked" };
                uncheckedNote.AddToClassList("ad-note");
                container.Add(uncheckedNote);
            }
            if (entry.kind == EntryKind.Trigger && entry.source == TriggerSource.BlendShape)
            {
                var note = new Label(string.Format(L.Text("ad.ui.preview.follows"), entry.sourceRenderer != null ? entry.sourceRenderer.name : "?", entry.sourceShape ?? ""));
                note.AddToClassList("ad-note");
                buttons.Add(note);
                return container;
            }
            // A contact trigger can also be tried with the test hand, under its own preview controls.
            if (TouchProbe.Simulates(entry))
            {
                container.Add(TouchTestControls.Row(owner, "ad-touch-test-trigger-row", "ad-touch-test-trigger", () => Row?.Entry, () => TouchConditionOf(Row)));
                touchTestNote = new Label(L.Text("ad.ui.touch_test.passed_by")) { name = "ad-touch-test-trigger-note" };
                touchTestNote.AddToClassList("ad-note");
                container.Add(touchTestNote);
            }
            if (entry.IsFlipTrigger)
            {
                var simulate = new Button(SimulateTouch) { text = L.Text("ad.ui.touch.simulate"), name = "ad-preview-simulate", tooltip = L.Text("ad.ui.touch.simulate.tip") };
                buttons.Add(simulate);
                return container;
            }
            if (entry.kind == EntryKind.Radial)
            {
                previewDefault = new Button(PreviewDefault) { text = L.Text("ad.ui.preview.default"), name = "ad-preview-default", tooltip = L.Text("ad.ui.preview.default.tip") };
                previewDefault.AddToClassList("ad-field-button");
                buttons.Add(previewDefault);
                previewSlider = new Slider(0f, 1f) { name = "ad-preview-slider", showInputField = true, tooltip = L.Text("ad.ui.preview.tip") };
                previewSlider.AddToClassList("ad-grow");
                previewSlider.RegisterValueChangedCallback(e => PreviewValue(e.newValue));
                buttons.Add(previewSlider);
            }
            else
            {
                previewChoice = new SegmentedChoice(new List<string> { L.Text("ad.ui.preview.default"), L.Text("ad.ui.preview.on"), L.Text("ad.ui.preview.off") }) { name = "ad-preview-choice", tooltip = L.Text("ad.ui.preview.tip") };
                previewChoice.Changed += index =>
                {
                    if (index == 0) PreviewDefault();
                    else if (index == 1) PreviewOn();
                    else PreviewOff();
                };
                buttons.Add(previewChoice);
            }
            RefreshPreviewRow(entry);
            return container;
        }

        /// <summary>The preview actions behind the buttons and the slider. Public so tests and keyboard handling can drive them without a panel.</summary>
        public void PreviewDefault() { if (Row?.Entry == null) return; PreviewState.Clear(Row.Entry.id); RefreshPreviewRow(Row.Entry); owner.Header.RefreshPreviewIndicator(); }
        public void PreviewOn() { if (Row?.Entry == null) return; PreviewState.SetOn(Row.Entry, owner.Plan?.Tree); RefreshPreviewRow(Row.Entry); owner.Header.RefreshPreviewIndicator(); }
        public void PreviewOff() { if (Row?.Entry == null) return; PreviewState.Set(Row.Entry.id, 0f); RefreshPreviewRow(Row.Entry); owner.Header.RefreshPreviewIndicator(); }
        public void PreviewValue(float value) { if (Row?.Entry == null) return; PreviewState.Set(Row.Entry.id, Mathf.Clamp01(value)); RefreshPreviewRow(Row.Entry); owner.Header.RefreshPreviewIndicator(); }

        /// <summary>A flip trigger's own "preview" is one press: it flips the target toggle's override, never sets one on the trigger itself.</summary>
        public void SimulateTouch()
        {
            if (Row?.Entry == null) return;
            var plan = owner.Plan;
            var target = plan?.Tree?.FindControl(Row.Entry.flipToggleId)?.Entry;
            if (target == null) return;
            PreviewState.Flip(target, plan.Tree, plan.Plan);
            owner.Header.RefreshPreviewIndicator();
        }

        /// <summary>The clips that animate the override's shapes, from the descriptor's controllers; click a name to ping the asset.</summary>
        VisualElement UsageList(MenuEntry entry)
        {
            var container = new VisualElement { name = "ad-override-usage" };
            container.AddToClassList("ad-usage");
            var clips = ShapeUsage(entry);
            if (clips.Count == 0)
            {
                var none = new Label(L.Text("ad.ui.override.unused"));
                none.AddToClassList("ad-note");
                container.Add(none);
                return container;
            }
            var title = new Label(UiText.Count(clips.Count, "ad.ui.override.usage.one", "ad.ui.override.usage"));
            title.AddToClassList("ad-section");
            container.Add(title);
            foreach (var clip in clips)
            {
                var item = new Label(clip.name);
                item.AddToClassList("ad-usage-clip");
                item.RegisterCallback<ClickEvent>(_ => EditorGUIUtility.PingObject(clip));
                container.Add(item);
            }
            return container;
        }

        /// <summary>Shows the note under the test hand's row while the hand would pass this trigger by: the plan builds nothing for it, or the component is on no avatar.</summary>
        void RefreshTouchTestNote(TreeRow row)
        {
            if (touchTestNote == null) return;
            var plan = owner.Plan;
            bool measured = plan != null && plan.AvatarRoot != null && row?.Entry != null && TouchProbe.TouchTriggers(plan.Tree).Any(n => n.Entry == row.Entry);
            touchTestNote.EnableInClassList("ad-hidden", measured);
        }

        /// <summary>Rewrites the preview row from the overrides as they stand, for a change that came from outside this panel.</summary>
        public void RefreshPreview()
        {
            if ((previewDefault != null || previewChoice != null) && Row?.Entry != null) RefreshPreviewRow(Row.Entry);
        }

        void RefreshPreviewRow(MenuEntry entry)
        {
            bool has = PreviewState.TryGet(entry.id, out var value);
            previewDefault?.EnableInClassList("ad-primary", !has);
            // A Fade the test hand holds part way is neither On nor Off.
            bool partWay = has && entry.IsFollowDistanceTrigger && value > 0f && value < 1f;
            previewChoice?.SetIndexWithoutNotify(!has ? 0 : partWay ? -1 : value >= 0.5f ? 1 : 2);
            previewSlider?.SetValueWithoutNotify(has ? value : Mathf.Clamp01(entry.defaultValue));
        }

        void Title(GlyphKind kind, string key)
        {
            glyph.EnableInClassList("ad-hidden", false);
            glyph.Kind = kind;
            kindLabel.text = L.Text(key);
        }

        /// <summary>The empty state's own title: no entry is selected, so no kind glyph applies to it.</summary>
        void TitleNoGlyph(string key)
        {
            glyph.EnableInClassList("ad-hidden", true);
            kindLabel.text = L.Text(key);
        }

        static GlyphKind GlyphFor(TreeRow row)
        {
            switch (row.Kind)
            {
                case RowKind.Submenu: return GlyphKind.Submenu;
                case RowKind.Radial: return GlyphKind.Radial;
                case RowKind.Trigger: return GlyphKind.Trigger;
                case RowKind.Override: return GlyphKind.Override;
                case RowKind.Preset: return GlyphKind.Preset;
                case RowKind.Grabbable: return GlyphKind.Grabbable;
                default: return GlyphKind.Toggle;
            }
        }

        static string KindKey(TreeRow row)
        {
            switch (row.Kind)
            {
                case RowKind.Submenu: return "ad.ui.detail.submenu";
                case RowKind.Radial: return "ad.ui.detail.radial";
                case RowKind.Trigger: return "ad.ui.detail.trigger";
                case RowKind.Override: return "ad.ui.detail.override";
                case RowKind.Preset: return "ad.ui.detail.preset";
                case RowKind.Grabbable: return "ad.ui.detail.grabbable";
                default: return "ad.ui.detail.toggle";
            }
        }

        Label Note(string text)
        {
            var note = new Label(text);
            note.AddToClassList("ad-note");
            body.Add(note);
            return note;
        }

        static Label Chip(string text, string cls, string tooltip = null)
        {
            var chip = new Label(text) { tooltip = tooltip };
            chip.AddToClassList("ad-chip");
            chip.AddToClassList(cls);
            return chip;
        }

        /// <summary>Turns a text button into an icon-only dropdown button, falling back to a plain "v" when the built-in icon is missing.</summary>
        internal static void AddDropdownIcon(Button button)
        {
            button.text = "";
            var icon = EditorGUIUtility.IconContent("d_icon dropdown").image;
            if (icon != null)
            {
                var image = new Image { image = icon };
                image.style.width = 12;
                image.style.height = 12;
                button.Add(image);
            }
            else button.text = "v";
        }

        /// <summary>Opens the capture window for this entry, which renders its pieces alone and saves the result as the icon.</summary>
        VisualElement CaptureButton(TreeRow row)
        {
            return new Button(() => IconCaptureWindow.Open(owner.Menu, row.Entry.id)) { text = L.Text("ad.ui.capture"), name = "ad-capture-icon", tooltip = L.Text("ad.ui.capture.tip") };
        }

        /// <summary>
        /// The icon field with the capture button at its right end. A control's icon is read straight off the entry
        /// wherever it is drawn, so it costs no rebuild; a submenu's is copied into the merged menu node the wheel
        /// and the built menu read, so that one does.
        /// </summary>
        VisualElement IconRow(TreeRow row, SerializedProperty property)
        {
            System.Action commit = row.Entry.kind == EntryKind.Submenu ? (System.Action)owner.ApplyAndRefresh : owner.ApplyCosmetic;
            var field = fields.Object<Texture2D>(property.FindPropertyRelative(nameof(MenuEntry.icon)), L.Text("ad.ui.icon"), commit);
            field.name = "ad-icon";
            field.tooltip = L.Text("ad.ui.icon.tip");
            field.AddToClassList("ad-grow");
            var line = Bound(field);
            line.Add(CaptureButton(row));
            return line;
        }

        /// <summary>
        /// Which platforms this entry is built on. A popup rather than a segmented choice: four items of this
        /// length do not fit beside the label column at the inspector's default width. A change is structural, so
        /// it goes through the panel's usual commit and the plan is built again.
        /// </summary>
        VisualElement PlatformRow(SerializedProperty property)
        {
            var labels = new List<string>
            {
                L.Text("ad.ui.platform.inherit"), L.Text("ad.ui.platform.both"),
                L.Text("ad.ui.platform.pc"), L.Text("ad.ui.platform.quest"),
            };
            var field = fields.EnumChoice(property.FindPropertyRelative(nameof(MenuEntry.platform)), L.Text("ad.ui.platform"), labels);
            field.name = "ad-entry-platform";
            field.tooltip = L.Text("ad.ui.platform.tip");
            return Bound(field);
        }

        static VisualElement Bound(VisualElement field)
        {
            var row = new VisualElement();
            row.AddToClassList("ad-row");
            row.Add(field);
            return row;
        }

        /// <summary>
        /// The name field and its style line for the selected entry. Submenu renames go through RenameSubmenu so
        /// install paths follow; every other kind writes the name property directly. Both write to the entry the
        /// field was built for, found again by its id when the name is saved: a typed name saves when the focus
        /// leaves the field, which can be the click that selects another row, or after an Undo moved the entry.
        /// </summary>
        VisualElement NameRow(TreeRow row, SerializedProperty property)
        {
            nameRow = new NameStyleRow(L.Text("ad.ui.name"), styled: !row.Entry.IsRule, compact: Trimmed(row.Entry), keepStyleRows: keptStyleRows);
            submenuName = nameRow.Q<TextField>("ad-name");
            nameRow.Load(row.Entry.name);
            if (row.Entry.kind == EntryKind.Submenu)
            {
                var submenuId = row.Entry.id;
                nameRow.Changed += newName => RenameSubmenu(owner.Menu.FindEntry(submenuId), newName);
            }
            else
            {
                var entryId = row.Entry.id;
                nameRow.Changed += richName => Rename(entryId, richName);
            }
            return Bound(nameRow);
        }

        /// <summary>Applies a whole-name style to the selected entry. Tests and keyboard shortcuts drive it.</summary>
        public void SetNameStyle(Color? color, bool bold, bool italic)
        {
            if (nameRow == null) return;
            nameRow.SetColor(color);
            nameRow.SetBold(bold);
            nameRow.SetItalic(italic);
        }

        /// <summary>Renames the selected submenu and moves the install paths of other components with it.</summary>
        public void RenameSubmenu(string newName) => RenameSubmenu(Row?.Entry, newName);

        /// <summary>Renames one submenu of this component and moves the install paths of other components with it.</summary>
        public void RenameSubmenu(MenuEntry submenu, string newName)
        {
            if (submenu == null || submenu.kind != EntryKind.Submenu) return;
            if (EntryEditing.RenameSubmenu(owner.Menu, submenu, newName, owner.Plan.Tree)) AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
        }

        /// <summary>Hides or shows the selected existing or foreign row.</summary>
        public void SetExistingHidden(bool hidden)
        {
            if (Row == null) return;
            ReleaseFocus();
            if (MenuEditing.SetHidden(owner.Menu, Row, hidden)) AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
        }

        /// <summary>Removes the selected existing row from the avatar, or puts it back. Foreign rows refuse it.</summary>
        public void SetExistingRemoved(bool removed)
        {
            if (Row == null) return;
            ReleaseFocus();
            if (!MenuEditing.SetRemoved(owner.Menu, Row, removed)) return;
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
        }

        /// <summary>Renames the selected existing or foreign row.</summary>
        public void SetExistingName(string richName) => SetExistingName(Row, richName);

        /// <summary>Renames one existing or foreign row: the one a name field was built for, which need not be the row selected now.</summary>
        public void SetExistingName(TreeRow row, string richName)
        {
            if (row == null) return;
            if (MenuEditing.SetName(owner.Menu, row, richName)) AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
        }

        /// <summary>Moves the selected existing or foreign row to path. Null (an empty field) leaves it where it is; an empty string moves it to the avatar root. Returns false, without changing anything, when the move is refused.</summary>
        public bool MoveExistingTo(string path) => MoveExistingTo(Row, path);

        /// <summary>Moves one existing or foreign row, the one a Move to field was built for, the same way MoveExistingTo(path) moves the selected one.</summary>
        public bool MoveExistingTo(TreeRow row, string path)
        {
            if (row == null) return false;
            ReleaseFocus();
            if (!MenuEditing.MoveTo(owner.Menu, row, string.IsNullOrEmpty(path) ? null : path)) return false;
            // A move never adds, removes, renames or changes what an entry reads or writes, the same as the
            // tree's own drag and drop, so the removal plan and the avatar-wide parameter walk stand.
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateOrder(owner.Plan?.AvatarRoot);
            owner.Refresh();
            return true;
        }

        /// <summary>Removes the selected existing or foreign row's edit, wherever it lives.</summary>
        public void ResetExisting()
        {
            if (Row == null) return;
            ReleaseFocus();
            if (MenuEditing.Reset(owner.Menu, Row)) AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
        }

        /// <summary>Default on (when it applies) and In menu: the two flags the creator flips often enough to stay visible outside the Advanced fold.</summary>
        VisualElement VisibleFlagsRow(SerializedProperty property, bool includeDefault)
        {
            var container = new VisualElement();
            container.AddToClassList("ad-row");
            container.AddToClassList("ad-flags");
            var toggles = new List<Toggle>();
            if (includeDefault)
            {
                var defaultOn = BoundToggle(L.Text("ad.ui.default_on"), property.FindPropertyRelative(nameof(MenuEntry.defaultOn)));
                defaultOn.name = "ad-default-on";
                defaultOn.tooltip = L.Text("ad.ui.default_on.tip");
                toggles.Add(defaultOn);
            }
            var inMenu = fields.ToggleInverted(property.FindPropertyRelative(nameof(MenuEntry.hiddenFromMenu)), L.Text("ad.ui.in_menu"));
            inMenu.tooltip = L.Text("ad.ui.in_menu.tip");
            toggles.Add(inMenu);
            for (int i = 0; i < toggles.Count; i++)
            {
                if (i > 0) toggles[i].AddToClassList("ad-compact");
                container.Add(toggles[i]);
            }
            return container;
        }

        /// <summary>Saved and Synced, with the bit cost note: moved into the Advanced fold, since the creator sets these once and rarely revisits them. A group member's own row shows the note that its group owns them instead.</summary>
        VisualElement SavedSyncedRow(TreeRow entryRow, SerializedProperty property)
        {
            var container = new VisualElement();
            container.AddToClassList("ad-row");
            container.AddToClassList("ad-flags");
            bool grouped = !string.IsNullOrWhiteSpace(property.FindPropertyRelative(nameof(MenuEntry.exclusiveGroup)).stringValue);
            if (grouped)
            {
                var note = new Label(L.Text("ad.ui.group.settings"));
                note.AddToClassList("ad-note");
                note.AddToClassList("ad-flags-note");
                container.Add(note);
                return container;
            }
            var saved = BoundToggle(L.Text("ad.ui.saved"), property.FindPropertyRelative(nameof(MenuEntry.saved)));
            saved.tooltip = L.Text("ad.ui.saved.tip");
            container.Add(saved);
            var synced = BoundToggle(L.Text("ad.ui.synced"), property.FindPropertyRelative(nameof(MenuEntry.synced)));
            synced.tooltip = L.Text("ad.ui.synced.tip");
            synced.AddToClassList("ad-compact");
            container.Add(synced);
            syncedCost = new Label { name = "ad-synced-cost" };
            syncedCost.AddToClassList("ad-note");
            RefreshSyncedCost(entryRow);
            container.Add(syncedCost);
            return container;
        }

        /// <summary>The tip kind key one entry kind's panel shows its first-use tip under.</summary>
        static string TipKindFor(EntryKind kind)
        {
            switch (kind)
            {
                case EntryKind.Submenu: return TipKinds.Submenu;
                case EntryKind.Radial: return TipKinds.Radial;
                case EntryKind.Trigger: return TipKinds.Trigger;
                case EntryKind.Override: return TipKinds.Override;
                case EntryKind.Preset: return TipKinds.Preset;
                case EntryKind.Grabbable: return TipKinds.Grabbable;
                default: return TipKinds.Toggle;
            }
        }

        /// <summary>The localization key of one entry kind's first-use tip text.</summary>
        static string TipTextKeyFor(EntryKind kind)
        {
            switch (kind)
            {
                case EntryKind.Submenu: return "ad.tip.submenu";
                case EntryKind.Radial: return "ad.tip.radial";
                case EntryKind.Trigger: return "ad.tip.trigger";
                case EntryKind.Override: return "ad.tip.override";
                case EntryKind.Preset: return "ad.tip.preset";
                case EntryKind.Grabbable: return "ad.tip.grabbable";
                default: return "ad.tip.toggle";
            }
        }

        /// <summary>
        /// Adds a panel's target sections, each shown only while it has a row, save for the one marked always
        /// visible (the common starting place for that kind, so a new entry is never a wall of empty sections),
        /// then one "+ Add effect" button offering every kind this panel supports with a one line description.
        /// </summary>
        void AddEffectSections(List<(string NameKey, string DescKey, TargetListView View, bool AlwaysVisible)> sections, bool offerOnlyTheBasics = false)
        {
            foreach (var section in sections)
            {
                effectSections.Add(section);
                if (!section.AlwaysVisible) section.View.EnableInClassList("ad-hidden", !section.View.HasContent);
                body.Add(section.View);
            }
            var offered = offerOnlyTheBasics ? sections.Where(s => SimpleOffers.Contains(s.NameKey)).ToList() : sections;
            offeredSections.AddRange(offered);
            var addRow = new VisualElement();
            addRow.AddToClassList("ad-row");
            var addButton = new Button { text = L.Text("ad.ui.add_effect"), name = "ad-add-effect", tooltip = L.Text("ad.ui.add_effect.tip") };
            addButton.clicked += () => OpenAddEffectMenu(offered, addButton);
            addRow.Add(addButton);
            body.Add(addRow);
        }

        /// <summary>The effect kinds "+ Add effect" offers on a toggle or a radial in Simple, each only where the panel has that section: a radial has no Objects.</summary>
        static readonly string[] SimpleOffers = { "ad.ui.objects", "ad.ui.blendshapes", "ad.ui.targets.properties" };

        /// <summary>Whether Simple trims this entry's panel: a toggle, a radial or a submenu. Every other kind shows its whole panel in both modes.</summary>
        bool Trimmed(MenuEntry entry) => owner.Simple && (entry.kind == EntryKind.Toggle || entry.kind == EntryKind.Radial || entry.kind == EntryKind.Submenu);

        /// <summary>
        /// Whether a row is one of AvatarDoc's own items that only Advanced adds: a trigger, an override, a grabbable, a
        /// merged animator, an installed menu asset, a declared parameter, or one of their group rows. Its panel opens
        /// with a note that says so. A toggle, a radial, a submenu, a preset (+ Outfit set builds those) and a More page
        /// are what Simple makes too, and an item of the avatar's own menu or of another tool is not AvatarDoc's to add,
        /// so none of those gets the note.
        /// </summary>
        static bool IsAdvancedItem(TreeRow row)
        {
            switch (row.Kind)
            {
                case RowKind.Trigger:
                case RowKind.TriggersGroup:
                case RowKind.Override:
                case RowKind.OverridesGroup:
                case RowKind.Grabbable:
                case RowKind.Animator:
                case RowKind.AnimatorsGroup:
                case RowKind.MenuAsset:
                case RowKind.MenuAssetsGroup:
                case RowKind.Parameter:
                case RowKind.ParametersGroup:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The button's rectangle is read when the menu opens: choosing an item can first run a panel rebuild a typed edit asked for, which takes the button away before the picker opens.</summary>
        static void OpenAddEffectMenu(List<(string NameKey, string DescKey, TargetListView View, bool AlwaysVisible)> sections, VisualElement anchor)
        {
            var bound = anchor.worldBound;
            var items = new List<AdMenu.Item>();
            foreach (var section in sections)
            {
                var view = section.View;
                items.Add(new AdMenu.Item { Text = L.Text(section.NameKey), Description = L.Text(section.DescKey), Action = () => view.OnAdd(bound) });
            }
            AdMenu.Open(bound, anchor, items);
        }

        /// <summary>The same Default, On, Off preview widget, disabled, with a one line reason instead of a different note-only control: for a kind whose preview truly cannot switch between states.</summary>
        VisualElement DisabledPreviewRow(string reasonKey)
        {
            var container = new VisualElement();
            container.AddToClassList("ad-preview");
            var buttons = new VisualElement();
            buttons.AddToClassList("ad-row");
            var label = new Label(L.Text("ad.ui.preview")) { tooltip = L.Text("ad.ui.preview.tip") };
            label.AddToClassList("ad-label");
            buttons.Add(label);
            var choice = new SegmentedChoice(new List<string> { L.Text("ad.ui.preview.default"), L.Text("ad.ui.preview.on"), L.Text("ad.ui.preview.off") })
                { name = "ad-preview-choice-disabled", tooltip = L.Text(reasonKey) };
            choice.SetEnabled(false);
            buttons.Add(choice);
            container.Add(buttons);
            var note = new Label(L.Text(reasonKey));
            note.AddToClassList("ad-note");
            container.Add(note);
            return container;
        }

        /// <summary>The Advanced fold's own dot: on while it is closed and something it hides is not at its default. Kind aware, since a submenu's fold hides only Platform while a toggle's also hides Parameter, Saved, Synced and Blend time.</summary>
        void RefreshAdvancedFoldDot(TreeRow row)
        {
            if (advancedFold == null || row?.Entry == null) return;
            var entry = row.Entry;
            bool grouped = !string.IsNullOrWhiteSpace(entry.exclusiveGroup);
            bool nonDefault = entry.platform != EntryPlatform.Inherit;
            if (entry.kind == EntryKind.Toggle || entry.kind == EntryKind.Radial)
            {
                nonDefault |= !string.IsNullOrWhiteSpace(entry.parameterName);
                if (!grouped) nonDefault |= !entry.saved || !entry.synced;
            }
            if (entry.kind == EntryKind.Toggle && !grouped) nonDefault |= entry.blendSeconds > 0f;
            advancedFold.SetNonDefault(nonDefault);
        }

        Toggle BoundToggle(string label, SerializedProperty property) => fields.Toggle(property, label);

        /// <summary>The blend time field, clamped to 10 seconds. Ignored on a group member, so the field is replaced with a note instead.</summary>
        VisualElement BlendTimeRow(SerializedProperty property, bool grouped)
        {
            if (grouped)
            {
                var note = new Label(L.Text("ad.ui.blend.group_note")) { name = "ad-blend-note" };
                note.AddToClassList("ad-note");
                return note;
            }
            var row = new VisualElement();
            row.AddToClassList("ad-row");
            var field = fields.FloatClamped(property.FindPropertyRelative(nameof(MenuEntry.blendSeconds)), L.Text("ad.ui.blend"), 0f, 10f);
            field.name = "ad-blend";
            field.tooltip = L.Text("ad.ui.blend.tip");
            row.Add(field);
            var suffix = new Label(L.Text("ad.ui.blend.unit"));
            suffix.AddToClassList("ad-note");
            suffix.AddToClassList("ad-suffix");
            row.Add(suffix);
            return row;
        }

        VisualElement GroupRow(TreeRow row, SerializedProperty property)
        {
            var groupProperty = property.FindPropertyRelative(nameof(MenuEntry.exclusiveGroup));
            var entryId = row.Entry.id;
            var container = new VisualElement();
            container.AddToClassList("ad-row");
            // A typed name is a group of the toggle's own component, so it lets go of a link to another one's.
            var field = fields.Text(groupProperty, L.Text("ad.ui.group"), alongside: p => Sibling(p, nameof(MenuEntry.groupComponent)).objectReferenceValue = null);
            field.name = "ad-group";
            field.tooltip = L.Text("ad.ui.group.tip");
            container.Add(field);
            Button pick = null;
            pick = new Button(() =>
            {
                var tree = owner.Plan.Tree;
                var current = tree.GroupOf(owner.Menu, owner.Menu.FindEntry(entryId));
                var popup = new List<AdMenu.Item>
                {
                    new AdMenu.Item { Text = L.Text("ad.ui.group.none"), Checked = current.IsNone, Action = () => SetEntryGroup(entryId, "", null) },
                };
                // This component's own groups first, a group it keeps only settings for included, then each other
                // component's, named with it.
                var keys = tree.GroupKeys();
                foreach (var settings in owner.Menu.groups)
                {
                    var settingsKey = new GroupKey(owner.Menu, (settings?.name ?? "").Trim());
                    if (!settingsKey.IsNone && !keys.Contains(settingsKey)) keys.Add(settingsKey);
                }
                foreach (var component in new[] { owner.Menu }.Concat(tree.Components.Where(c => c != owner.Menu)))
                    foreach (var key in keys.Where(k => k.Component == component).OrderBy(k => k.Name))
                    {
                        var chosen = key;
                        bool own = component == owner.Menu;
                        popup.Add(new AdMenu.Item
                        {
                            Text = own ? chosen.Name : L.Aside(chosen.Name, component.gameObject.name),
                            Checked = current == chosen,
                            Action = () => SetEntryGroup(entryId, chosen.Name, own ? null : chosen.Component),
                        });
                    }
                AdMenu.Open(pick.worldBound, pick, popup);
            });
            pick.name = "ad-group-pick";
            AddDropdownIcon(pick);
            container.Add(pick);
            return container;
        }

        void SetString(SerializedProperty property, string value)
        {
            if (Refuses) return;
            owner.SerializedObject.Update();
            property.stringValue = value;
            owner.ApplyAndRefresh();
        }

        /// <summary>Another field of the same item as this property.</summary>
        static SerializedProperty Sibling(SerializedProperty property, string name)
        {
            var path = property.propertyPath;
            return property.serializedObject.FindProperty(path.Substring(0, path.LastIndexOf('.') + 1) + name);
        }

        /// <summary>Puts the entry a control was built for into a group, in one Undo step: the group's name, and the component the group belongs to, null for the entry's own.</summary>
        void SetEntryGroup(string entryId, string name, AvatarDoc component)
        {
            if (Refuses) return;
            var property = EntryField(entryId, nameof(MenuEntry.exclusiveGroup));
            if (property == null) return;
            property.stringValue = name;
            Sibling(property, nameof(MenuEntry.groupComponent)).objectReferenceValue = component;
            owner.ApplyAndRefresh();
        }

        /// <summary>The note under Exclusive group of a toggle that joins another component's group: whose group it is.</summary>
        void RefreshGroupOwner(TreeRow row)
        {
            if (groupOwnerNote == null) return;
            var group = row.Control != null ? owner.Plan.Tree.GroupOf(row.Control) : default;
            bool linked = !group.IsNone && group.Component != null && group.Component != row.Control.Component;
            groupOwnerNote.text = linked ? string.Format(L.Text("ad.ui.group.linked"), group.Component.gameObject.name) : "";
            groupOwnerNote.EnableInClassList("ad-hidden", !linked);
        }

        /// <summary>One field of the entry with this id, where the component's list holds it now. Null when the entry is gone: a control built for it must not write to whichever entry took its place.</summary>
        SerializedProperty EntryField(string entryId, string relative)
        {
            owner.SerializedObject.Update();
            var entry = owner.Menu.FindEntry(entryId);
            return entry == null ? null : owner.EntryProperty(entry)?.FindPropertyRelative(relative);
        }

        /// <summary>Writes a text field of the entry a control was built for.</summary>
        void SetEntryString(string entryId, string relative, string value)
        {
            var property = EntryField(entryId, relative);
            if (property != null) SetString(property, value);
        }

        /// <summary>Writes a switch of the entry a control was built for.</summary>
        void SetEntryBool(string entryId, string relative, bool value)
        {
            if (Refuses) return;
            var property = EntryField(entryId, relative);
            if (property == null) return;
            property.boolValue = value;
            owner.ApplyAndRefresh();
        }

        /// <summary>Writes a new name to the entry a name field was built for. A name an automatic parameter is made from, or one a group reads, changes the plan; a plain label does not.</summary>
        void Rename(string entryId, string richName)
        {
            if (Refuses) return;
            var property = EntryField(entryId, nameof(MenuEntry.name));
            if (property == null) return;
            property.stringValue = richName;
            if (owner.RenameIsCosmetic(owner.Menu.FindEntry(entryId))) owner.ApplyCosmetic();
            else owner.ApplyAndRefresh();
        }

        VisualElement ParameterRow(TreeRow row, SerializedProperty property)
        {
            var container = new VisualElement();
            var line = new VisualElement();
            line.AddToClassList("ad-row");
            var label = new Label(L.Text("ad.ui.parameter"));
            label.AddToClassList("ad-label");
            label.tooltip = L.Text("ad.ui.parameter.tip");
            line.Add(label);
            parameterName = new Label { name = "ad-parameter-name" };
            line.Add(parameterName);
            var nameProperty = property.FindPropertyRelative(nameof(MenuEntry.parameterName));
            bool grouped = row.Entry.kind == EntryKind.Toggle && !string.IsNullOrWhiteSpace(row.Entry.exclusiveGroup);
            bool custom = !grouped && !string.IsNullOrWhiteSpace(nameProperty.stringValue);
            parameterChip = Chip(L.Text(custom ? "ad.ui.parameter.custom" : "ad.ui.parameter.auto"), custom ? "ad-chip-on" : "ad-chip-existing");
            parameterChip.name = "ad-parameter-chip";
            parameterChip.AddToClassList("ad-chip-gap");
            line.Add(parameterChip);
            if (grouped)
            {
                parameterCustom = null;
                container.Add(line);
                parameterNote = new Label { name = "ad-parameter-bits" };
                parameterNote.AddToClassList("ad-note");
                parameterNote.AddToClassList("ad-note-indent");
                container.Add(parameterNote);
                var groupNote = new Label(L.Text("ad.ui.parameter.group_note")) { name = "ad-parameter-group-note" };
                groupNote.AddToClassList("ad-note");
                container.Add(groupNote);
                var valueInGroup = fields.Int(property.FindPropertyRelative(nameof(MenuEntry.groupValue)), L.Text("ad.ui.group.value"), 0);
                valueInGroup.name = "ad-group-value";
                valueInGroup.tooltip = L.Text("ad.ui.group.value.tip");
                container.Add(Bound(valueInGroup));
                return container;
            }
            var spacer = new VisualElement();
            spacer.AddToClassList("ad-spacer");
            line.Add(spacer);
            parameterCustom = new Toggle(L.Text("ad.ui.parameter.use_custom")) { value = custom, name = "ad-parameter-custom", tooltip = L.Text("ad.ui.parameter.use_custom.tip") };
            parameterCustom.AddToClassList("ad-compact");
            var entryId = row.Entry.id;
            parameterCustom.RegisterValueChangedCallback(e =>
            {
                if (Refuses)
                {
                    parameterCustom.SetValueWithoutNotify(!e.newValue);
                    return;
                }
                ReleaseFocus();
                if (!e.newValue)
                {
                    SetEntryString(entryId, nameof(MenuEntry.parameterName), "");
                    return;
                }
                var customName = EntryField(entryId, nameof(MenuEntry.parameterName));
                if (customName == null) return;
                customName.stringValue = owner.Plan.Plan.Bindings.TryGetValue(row.Control, out var b) ? b.Parameter.Name : row.Entry.PlainName;
                owner.ApplyAndRefresh();
            });
            line.Add(parameterCustom);
            container.Add(line);

            parameterNote = new Label { name = "ad-parameter-bits" };
            parameterNote.AddToClassList("ad-note");
            parameterNote.AddToClassList("ad-note-indent");
            container.Add(parameterNote);
            parameterShared = new Label { name = "ad-parameter-shared" };
            parameterShared.AddToClassList("ad-note");
            parameterShared.AddToClassList("ad-note-indent");
            container.Add(parameterShared);

            if (custom)
            {
                var nameField = fields.Text(nameProperty, L.Text("ad.ui.parameter"));
                nameField.name = "ad-parameter-name-field";
                nameField.tooltip = L.Text("ad.ui.parameter.tip");
                // The field commits on Enter or focus loss; these two follow the text itself so the chip is never a keystroke behind.
                nameField.RegisterCallback<InputEvent>(e => SetParameterNameLive(e.newData));
                nameField.RegisterCallback<KeyUpEvent>(_ => SetParameterNameLive(nameField.text));
                container.Add(Bound(nameField));
                if (row.Entry.kind == EntryKind.Toggle)
                {
                    var valueField = fields.Float(property.FindPropertyRelative(nameof(MenuEntry.parameterValue)), L.Text("ad.ui.parameter.value"));
                    valueField.name = "ad-parameter-value";
                    valueField.tooltip = L.Text("ad.ui.parameter.value.tip");
                    container.Add(Bound(valueField));
                }
            }
            return container;
        }

        void BuildTriggerBody(TreeRow row, SerializedProperty property)
        {
            var entry = row.Entry;
            // A blend shape trigger keeps its shape in its flat fields whatever requirements it has, so it always
            // shows its own source rows below.
            if (entry.UsesWhenGroups && entry.source != TriggerSource.BlendShape)
            {
                body.Add(new ConditionGroupsPanel(this, owner, row, property));
                // Hold or Flip belongs to the trigger, not to any one condition, so it stays below the cards.
                if (entry.source == TriggerSource.Contact || entry.source == TriggerSource.PhysBone)
                    body.Add(TouchPanel.ModeOnly(this, owner, row));
                else if (OffersValueFade(entry)) AddValueFadeRows(entry, property);
                AddGateRow(row, property);
                if (entry.IsFlipTrigger) return;
                AddTriggerTargets(property, false, entry);
                return;
            }
            var sourceChoices = new List<string>
            {
                L.Text("ad.ui.trigger.source.toggle"), L.Text("ad.ui.trigger.source.parameter"), L.Text("ad.ui.trigger.source.blendshape"),
                L.Text("ad.ui.trigger.source.contact"), L.Text("ad.ui.trigger.source.physbone"),
            };
            var source = new DropdownField(L.Text("ad.ui.trigger.when"), sourceChoices, (int)entry.source) { name = "ad-trigger-source", tooltip = L.Text("ad.ui.trigger.when.tip") };
            source.RegisterValueChangedCallback(e => SetTriggerSource((TriggerSource)sourceChoices.IndexOf(e.newValue)));
            body.Add(Bound(source));

            switch (entry.source)
            {
                case TriggerSource.Toggle:
                {
                    var toggles = owner.Plan.Tree.Controls.Where(c => c.Entry.kind == EntryKind.Toggle).ToList();
                    DropdownField toggleField;
                    if (toggles.Count == 0)
                    {
                        toggleField = new DropdownField(L.Text("ad.ui.trigger.toggle"), new List<string> { L.Text("ad.ui.trigger.no_toggles") }, 0) { name = "ad-trigger-toggle", tooltip = L.Text("ad.ui.trigger.toggle.tip") };
                        toggleField.SetEnabled(false);
                    }
                    else
                    {
                        var labels = toggles.Select(c => c.Component == owner.Menu ? c.Entry.PlainName : L.Aside(c.Entry.PlainName, c.Component.gameObject.name)).ToList();
                        int current = toggles.FindIndex(c => c.Entry.id == entry.sourceToggleId);
                        toggleField = new DropdownField(L.Text("ad.ui.trigger.toggle"), labels, Mathf.Max(0, current)) { name = "ad-trigger-toggle", tooltip = L.Text("ad.ui.trigger.toggle.tip") };
                        if (current < 0) toggleField.SetValueWithoutNotify("");
                        toggleField.RegisterValueChangedCallback(e => { int i = labels.IndexOf(e.newValue); if (i >= 0) SetSourceToggle(toggles[i].Entry.id); });
                    }
                    body.Add(Bound(toggleField));
                    var sourceControl = owner.Plan.Tree.FindControl(entry.sourceToggleId);
                    if (sourceControl != null && sourceControl.Component != null && !sourceControl.Component.enabled)
                        Note(L.Text("ad.ui.trigger.source_disabled"));
                    var states = new List<string> { L.Text("ad.ui.trigger.toggle.on"), L.Text("ad.ui.trigger.toggle.off") };
                    var state = new DropdownField(states, entry.sourceToggleOn ? 0 : 1) { tooltip = L.Text("ad.ui.trigger.toggle_state.tip") };
                    state.RegisterValueChangedCallback(e => SetEntryBool(entry.id, nameof(MenuEntry.sourceToggleOn), states.IndexOf(e.newValue) == 0));
                    body.Add(Bound(state));
                    break;
                }
                case TriggerSource.Parameter:
                {
                    var row2 = new VisualElement();
                    row2.AddToClassList("ad-row");
                    var nameField = fields.Text(property.FindPropertyRelative(nameof(MenuEntry.sourceParameter)), L.Text("ad.ui.trigger.parameter"));
                    nameField.name = "ad-trigger-parameter";
                    nameField.tooltip = L.Text("ad.ui.trigger.parameter.tip");
                    row2.Add(nameField);
                    var pick = new Button();
                    AddDropdownIcon(pick);
                    pick.clicked += () => ParameterPicker.Show(pick, owner.Plan, SetSourceParameter);
                    row2.Add(pick);
                    body.Add(row2);
                    // A Fade on the parameter blends with its value, so it has no comparison to show.
                    if (!entry.IsFollowValueTrigger)
                    {
                        var conditionField = fields.EnumChoice(property.FindPropertyRelative(nameof(MenuEntry.condition)), L.Text("ad.ui.trigger.condition"), ConditionGroupsPanel.ComparisonLabels);
                        conditionField.name = "ad-trigger-condition";
                        conditionField.tooltip = L.Text("ad.ui.trigger.condition.tip");
                        body.Add(Bound(conditionField));
                        bool needsValue = entry.condition != ParameterCondition.IsTrue && entry.condition != ParameterCondition.IsFalse;
                        var valueField = fields.Float(property.FindPropertyRelative(nameof(MenuEntry.conditionValue)), L.Text("ad.ui.trigger.value"));
                        valueField.name = "ad-trigger-value";
                        valueField.tooltip = L.Text("ad.ui.trigger.value.tip");
                        var valueRow = Bound(valueField);
                        valueRow.EnableInClassList("ad-hidden", !needsValue);
                        body.Add(valueRow);
                    }
                    Note(L.Text("ad.ui.trigger.parameter_note"));
                    if (OffersValueFade(entry)) AddValueFadeRows(entry, property);
                    break;
                }
                case TriggerSource.Contact:
                case TriggerSource.PhysBone:
                    body.Add(new TouchPanel(this, owner, row, property));
                    break;
                default:
                    BuildBlendShapeSource(row, property, entry);
                    break;
            }

            if (entry.source != TriggerSource.BlendShape)
            {
                body.Add(new ConditionGroupsPanel(this, owner, row, property));
                AddGateRow(row, property);
            }

            if (entry.IsFlipTrigger) return;
            AddTriggerTargets(property, entry.source == TriggerSource.BlendShape, entry);
        }

        /// <summary>
        /// A blend shape trigger's own rows: its card (the shape first, then its requirements and "+ And"), the
        /// style, Smooth's Start and Fully on at, the one switch point (labelled for what it moves in each style),
        /// Add or Replace, a note on the hidden copy, and Only while.
        /// </summary>
        void BuildBlendShapeSource(TreeRow row, SerializedProperty property, MenuEntry entry)
        {
            // A long renderer and shape name ends in an ellipsis at the narrow end of the inspector; the tooltip names it in full.
            var shapeText = (entry.sourceRenderer != null ? entry.sourceRenderer.name : "?") + " › " + entry.sourceShape;
            var shapeButton = new Button { text = shapeText, name = "ad-trigger-shape", tooltip = shapeText + "\n" + L.Text("ad.ui.trigger.shape.tip"), displayTooltipWhenElided = false };
            shapeButton.AddToClassList("ad-field-button");
            shapeButton.clicked += () => ShapePicker.Show(shapeButton, owner.Plan.AvatarRoot, SetSourceShape);
            var shapeRow = new VisualElement { name = "ad-when-driver" };
            shapeRow.AddToClassList("ad-row");
            var label = new Label(L.Text("ad.ui.trigger.shape")) { tooltip = L.Text("ad.ui.trigger.shape.tip") };
            label.AddToClassList("ad-label");
            shapeRow.Add(label);
            shapeRow.Add(shapeButton);
            body.Add(new ConditionGroupsPanel(this, owner, row, property, shapeRow));

            var styleRow = new VisualElement();
            styleRow.AddToClassList("ad-row");
            var styleLabel = new Label(L.Text("ad.ui.trigger.style")) { tooltip = L.Text("ad.ui.trigger.style.tip") };
            styleLabel.AddToClassList("ad-label");
            styleRow.Add(styleLabel);
            var style = fields.EnumSegments(property.FindPropertyRelative(nameof(MenuEntry.linkStyle)),
                new List<string> { L.Text("ad.ui.trigger.style.smooth"), L.Text("ad.ui.trigger.style.switch") }, owner.ApplyAndRefresh);
            style.name = "ad-trigger-style";
            style.tooltip = L.Text("ad.ui.trigger.style.tip");
            style.Items[0].tooltip = L.Text("ad.ui.trigger.style.smooth.tip");
            style.Items[1].tooltip = L.Text("ad.ui.trigger.style.switch.tip");
            styleRow.Add(style);
            body.Add(styleRow);

            bool smooth = entry.linkStyle == BlendShapeLinkStyle.Smooth;
            if (smooth)
            {
                var start = fields.Slider(property.FindPropertyRelative(nameof(MenuEntry.smoothStart)), L.Text("ad.ui.trigger.smooth_start"), 0f, 100f, owner.ApplyLive);
                start.name = "ad-trigger-smooth-start";
                start.tooltip = L.Text("ad.ui.trigger.smooth_start.tip");
                body.Add(Bound(start));
                var full = fields.Slider(property.FindPropertyRelative(nameof(MenuEntry.smoothFull)), L.Text("ad.ui.trigger.smooth_full"), 0f, 100f, owner.ApplyLive);
                full.name = "ad-trigger-smooth-full";
                full.tooltip = L.Text("ad.ui.trigger.smooth_full.tip");
                body.Add(Bound(full));
            }
            var threshold = fields.Slider(property.FindPropertyRelative(nameof(MenuEntry.sourceThreshold)), L.Text(smooth ? "ad.ui.trigger.swap_at" : "ad.ui.trigger.switch_at"), 0f, 100f, owner.ApplyLive);
            threshold.name = "ad-trigger-threshold";
            threshold.tooltip = L.Text(smooth ? "ad.ui.trigger.swap_at.tip" : "ad.ui.trigger.switch_at.tip");
            body.Add(Bound(threshold));
            var modeField = fields.EnumChoice(property.FindPropertyRelative(nameof(MenuEntry.linkMode)), L.Text("ad.ui.trigger.mode"),
                new List<string> { L.Text("ad.ui.trigger.mode.add"), L.Text("ad.ui.trigger.mode.replace") });
            modeField.name = "ad-trigger-mode";
            modeField.tooltip = L.Text("ad.ui.trigger.mode.tip");
            body.Add(Bound(modeField));
            Note(L.Text("ad.ui.trigger.link_note"));
            AddGateRow(row, property);
        }

        /// <summary>
        /// Whether a parameter trigger offers Fade, which blends its effects with its first parameter's value: it is
        /// a Fade already, or that parameter is a Float the avatar or VRChat declares. A Bool or an Int has no value
        /// in between to follow, so its panel is the one it has always been.
        /// </summary>
        bool OffersValueFade(MenuEntry entry)
        {
            if (entry == null || entry.kind != EntryKind.Trigger || entry.source != TriggerSource.Parameter) return false;
            if (entry.touchMode == TouchMode.FollowValue) return true;
            var first = entry.EffectiveGroups().SelectMany(g => g.group.all).FirstOrDefault(c => c != null);
            if (first == null || first.kind != WhenConditionKind.Parameter) return false;
            var name = (first.sourceParameter ?? "").Trim();
            if (name.Length == 0) return false;
            var existing = owner.Plan?.Existing;
            if (existing != null && existing.TryGetValue(name, out var known)) return known.Type == PlannedType.Float;
            return TriggerPlanner.BuiltInType(name) == PlannedType.Float;
        }

        /// <summary>A parameter trigger's Mode, Hold or Fade, and for Fade the value at which the effects are fully on.</summary>
        void AddValueFadeRows(MenuEntry entry, SerializedProperty property)
        {
            var row = new VisualElement();
            row.AddToClassList("ad-row");
            var label = new Label(L.Text("ad.ui.touch.mode")) { tooltip = L.Text("ad.ui.trigger.value_mode.tip") };
            label.AddToClassList("ad-label");
            row.Add(label);
            bool fade = entry.IsFollowValueTrigger;
            var choice = new SegmentedChoice(new List<string> { L.Text("ad.ui.touch.hold"), L.Text("ad.ui.touch.follow") }, fade ? 1 : 0,
                new List<string> { L.Text("ad.ui.trigger.value_hold.tip"), L.Text("ad.ui.trigger.value_fade.tip") })
            {
                name = "ad-trigger-value-mode",
                tooltip = L.Text("ad.ui.trigger.value_mode.tip"),
            };
            choice.Changed += index => SetTouchMode(index == 1 ? TouchMode.FollowValue : TouchMode.Hold);
            row.Add(choice);
            body.Add(row);
            if (!fade) return;
            var fullAt = fields.Slider(property.FindPropertyRelative(nameof(MenuEntry.contactFullAt)), L.Text("ad.ui.touch.fullat"), 0.05f, 1f, owner.ApplyLive);
            fullAt.name = "ad-trigger-value-fullat";
            fullAt.tooltip = L.Text("ad.ui.trigger.value_fullat.tip");
            body.Add(Bound(fullAt));
            Note(L.Text("ad.ui.trigger.value_note"));
        }

        /// <summary>Only while: visible outside the fold when a gate is actually set (it is then something the creator needs to see), tucked into the Advanced fold, like everything else there, while it is not used.</summary>
        void AddGateRow(TreeRow row, SerializedProperty property)
        {
            var gateRow = GateRow(row, property);
            if (string.IsNullOrEmpty(row.Entry.gateToggleId)) advancedFold.Content.Add(gateRow);
            else body.Add(gateRow);
        }

        void AddTriggerTargets(SerializedProperty property, bool link, MenuEntry entry)
        {
            var sections = new List<(string NameKey, string DescKey, TargetListView View, bool AlwaysVisible)>
            {
                ("ad.ui.objects", "ad.ui.add_effect.objects", new TargetListView(owner, property.FindPropertyRelative(nameof(MenuEntry.objects)), TargetListView.Kind.Objects, false), true),
                ("ad.ui.blendshapes", "ad.ui.add_effect.blendshapes", new TargetListView(owner, property.FindPropertyRelative(nameof(MenuEntry.blendShapes)), TargetListView.Kind.BlendShapes, false, link, captureEntry: entry), false),
                ("ad.ui.materials", "ad.ui.add_effect.materials", new TargetListView(owner, property.FindPropertyRelative(nameof(MenuEntry.materials)), TargetListView.Kind.Materials, false), false),
            };
            if (entry.BuildsProperties)
                sections.Add(("ad.ui.targets.properties", "ad.ui.add_effect.properties", new TargetListView(owner, property.FindPropertyRelative(nameof(MenuEntry.properties)), TargetListView.Kind.Properties, radial: false), false));
            // A blend shape link rewrites clips from a continuous source value; it has no on/off state for a follow
            // to ride on, and neither does Follow distance's blend, so "+ Add effect" does not offer Follows there.
            bool followDistance = entry.IsFollowDistanceTrigger;
            if (!link && !followDistance)
                sections.Add(("ad.ui.targets.follows", "ad.ui.add_effect.follows", new TargetListView(owner, property.FindPropertyRelative(nameof(MenuEntry.follows)), TargetListView.Kind.Follows, false), false));
            AddEffectSections(sections);
            if (!link && followDistance && entry.follows.Count > 0)
            {
                // Data left over from before the trigger was switched to Follow distance: shown, not offered for
                // new effects, and the build skips it (FollowBuilder.Handle). MenuModel.ValidateFollows already
                // raises ad.warn.follow_distance_no_follows for the row's own message box above.
                body.Add(new TargetListView(owner, property.FindPropertyRelative(nameof(MenuEntry.follows)), TargetListView.Kind.Follows, false));
            }
        }

        /// <summary>Adds a condition to one group of the selected trigger, moving its single source into the first group when that is what it still has.</summary>
        public void AddCondition(int groupIndex)
        {
            if (Refuses || Row == null || Row.Entry == null) return;
            ReleaseFocus();
            var condition = EntryEditing.AddCondition(owner.Menu, Row.Entry, groupIndex);
            if (condition == null) return;
            SelectAdded(condition);
            AfterStructureChange();
        }

        /// <summary>Adds another group to the selected trigger: one more way its rule can be met.</summary>
        public void AddConditionGroup()
        {
            if (Refuses || Row == null || Row.Entry == null) return;
            ReleaseFocus();
            var condition = EntryEditing.AddConditionGroup(owner.Menu, Row.Entry);
            if (condition == null) return;
            SelectAdded(condition);
            AfterStructureChange();
        }

        public void RemoveCondition(int groupIndex, int index)
        {
            if (Refuses || Row == null || Row.Entry == null) return;
            ReleaseFocus();
            if (!EntryEditing.RemoveCondition(owner.Menu, Row.Entry, groupIndex, index)) return;
            selectedGroup = 0;
            selectedIndex = 0;
            AfterStructureChange();
        }

        public void RemoveConditionGroup(int groupIndex)
        {
            if (Refuses || Row == null || Row.Entry == null) return;
            ReleaseFocus();
            if (!EntryEditing.RemoveConditionGroup(owner.Menu, Row.Entry, groupIndex)) return;
            selectedGroup = 0;
            selectedIndex = 0;
            AfterStructureChange();
        }

        public void SetConditionKind(int groupIndex, int index, WhenConditionKind kind)
        {
            if (Refuses || Row == null || Row.Entry == null) return;
            ReleaseFocus();
            if (!EntryEditing.SetConditionKind(owner.Menu, Row.Entry, groupIndex, index, kind)) return;
            selectedGroup = groupIndex;
            selectedIndex = index;
            AfterStructureChange();
        }

        /// <summary>Writes the toggle one condition reads. The property is the one the panel was built with, which names its entry by the place it had then.</summary>
        public void SetConditionToggle(SerializedProperty conditionProperty, string toggleId)
        {
            if (Refuses || conditionProperty == null) return;
            ReleaseFocus();
            var path = Rebased(conditionProperty.propertyPath + "." + nameof(WhenCondition.sourceToggleId));
            if (path == null) return;
            fields.Write(path, p => p.stringValue = toggleId ?? "", null);
        }

        /// <summary>Writes the parameter one condition watches, from the picker.</summary>
        public void SetConditionParameter(SerializedProperty conditionProperty, string parameter)
        {
            if (Refuses || conditionProperty == null) return;
            ReleaseFocus();
            var path = Rebased(conditionProperty.propertyPath + "." + nameof(WhenCondition.sourceParameter));
            if (path == null) return;
            fields.Write(path, p => p.stringValue = parameter ?? "", null);
        }

        /// <summary>Writes one switch of a condition, such as whether its toggle has to be on or off.</summary>
        public void SetConditionBool(SerializedProperty property, bool value)
        {
            if (Refuses || property == null) return;
            ReleaseFocus();
            var path = Rebased(property.propertyPath);
            if (path == null) return;
            fields.Write(path, p => p.boolValue = value, null);
        }

        void SelectAdded(WhenCondition condition)
        {
            foreach (var (_, stored, groupIndex, index) in Row.Entry.StoredConditions())
                if (ReferenceEquals(stored, condition)) { selectedGroup = groupIndex; selectedIndex = index; return; }
        }

        /// <summary>Redraws everything after a condition was added, removed or changed kind: the entry list itself moved, so the plan and the tree follow.</summary>
        void AfterStructureChange()
        {
            owner.SerializedObject.Update();
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
        }

        /// <summary>"Only while": the trigger's menu switch. First choice is always (no gate); the state follows only when a gate is picked. On a blend shape trigger or a Fade contact it is one more requirement.</summary>
        VisualElement GateRow(TreeRow row, SerializedProperty property)
        {
            var entry = row.Entry;
            var container = new VisualElement();
            var toggles = owner.Plan.Tree.Controls.Where(c => c.Entry.kind == EntryKind.Toggle && c.Entry.id != entry.id).ToList();
            var labels = new List<string> { L.Text("ad.ui.trigger.gate.always") };
            labels.AddRange(toggles.Select(c => c.Component == owner.Menu ? c.Entry.PlainName : L.Aside(c.Entry.PlainName, c.Component.gameObject.name)));
            int current = string.IsNullOrEmpty(entry.gateToggleId) ? -1 : toggles.FindIndex(c => c.Entry.id == entry.gateToggleId);
            var gateField = new DropdownField(L.Text("ad.ui.trigger.gate"), labels, current + 1) { name = "ad-trigger-gate", tooltip = L.Text("ad.ui.trigger.gate.tip") };
            gateField.RegisterValueChangedCallback(e =>
            {
                int i = labels.IndexOf(e.newValue) - 1;
                SetGateToggle(i >= 0 ? toggles[i].Entry.id : "");
            });
            container.Add(Bound(gateField));

            var states = new List<string> { L.Text("ad.ui.trigger.toggle.on"), L.Text("ad.ui.trigger.toggle.off") };
            var stateChoice = new SegmentedChoice(states, entry.gateToggleOn ? 0 : 1) { name = "ad-trigger-gate-state", tooltip = L.Text("ad.ui.trigger.gate_state.tip") };
            stateChoice.Changed += index => SetEntryBool(entry.id, nameof(MenuEntry.gateToggleOn), index == 0);
            container.Add(Bound(stateChoice));

            var add = new Button(AddGateSwitch) { text = L.Text("ad.ui.trigger.gate.add"), name = "ad-trigger-gate-add", tooltip = L.Text("ad.ui.trigger.gate.add.tip") };
            container.Add(add);
            return container;
        }

        /// <summary>Creates a plain menu switch and sets it as the selected trigger's gate: "Add a menu switch" in the trigger panel.</summary>
        public void AddGateSwitch()
        {
            if (Refuses || Row == null || Row.Entry == null) return;
            ReleaseFocus();
            var toggle = EntryEditing.AddGateSwitch(owner.Menu, Row.Entry);
            if (toggle == null) return;
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            AvatarDoctorView.RequestReveal(owner.Menu, toggle.id);
            owner.Refresh();
        }

        public void SetGateToggle(string toggleId)
        {
            if (Refuses || Row == null || Row.Entry == null) return;
            var property = owner.EntryProperty(Row.Entry);
            if (property == null) return;
            ReleaseFocus();
            SetString(property.FindPropertyRelative(nameof(MenuEntry.gateToggleId)), toggleId ?? "");
        }

        public void SetTriggerSource(TriggerSource source)
        {
            if (Refuses || Row == null || Row.Entry == null) return;
            var property = owner.EntryProperty(Row.Entry);
            if (property == null) return;
            // A blend shape trigger's group holds requirements only, beside the shape in its flat fields. Leaving the
            // shape, that group becomes the whole rule, which no longer reads the flat fields: the source just picked
            // goes in as its first condition, in the same Undo step, so the pick is not silently lost.
            var entry = Row.Entry;
            int requirementGroup = entry.source == TriggerSource.BlendShape && source != TriggerSource.BlendShape ? entry.RequirementGroupIndex : -1;
            int undoGroup = Undo.GetCurrentGroup();
            owner.SerializedObject.Update();
            property.FindPropertyRelative(nameof(MenuEntry.source)).enumValueIndex = (int)source;
            if (source == TriggerSource.Contact) ApplyContactDefaults(property);
            else if (source == TriggerSource.PhysBone) ApplyPhysBoneThresholdDefault(property, Row.Entry.physBoneEvent);
            ReleaseFocus();
            if (requirementGroup < 0)
            {
                owner.ApplyAndRefresh();
                return;
            }
            owner.SerializedObject.ApplyModifiedProperties();
            EntryEditing.PutFlatSourceFirst(owner.Menu, entry, requirementGroup);
            Undo.CollapseUndoOperations(undoGroup);
            AfterStructureChange();
        }

        /// <summary>An old trigger switched to Contact has contactRadius 0 (Unity skips field initialisers for data an undo or a prefab override deserialises): write the spec's defaults once, in the same Undo step as the source switch.</summary>
        static void ApplyContactDefaults(SerializedProperty entryProperty)
        {
            var radiusProperty = entryProperty.FindPropertyRelative(nameof(MenuEntry.contactRadius));
            if (radiusProperty.floatValue != 0f) return;
            radiusProperty.floatValue = 0.05f;
            var tagsProperty = entryProperty.FindPropertyRelative(nameof(MenuEntry.contactTags));
            if (tagsProperty.arraySize == 0)
            {
                tagsProperty.InsertArrayElementAtIndex(0);
                tagsProperty.GetArrayElementAtIndex(0).stringValue = "Finger";
                tagsProperty.InsertArrayElementAtIndex(1);
                tagsProperty.GetArrayElementAtIndex(1).stringValue = "Hand";
            }
            entryProperty.FindPropertyRelative(nameof(MenuEntry.contactAllowSelf)).boolValue = true;
            entryProperty.FindPropertyRelative(nameof(MenuEntry.contactAllowOthers)).boolValue = true;
        }

        /// <summary>The same fix for a PhysBone trigger: a threshold left at 0 becomes 0.5 once a float event is in play, whether that happens on the source switch or on picking the event afterwards.</summary>
        static void ApplyPhysBoneThresholdDefault(SerializedProperty entryProperty, PhysBoneEvent evt)
        {
            if (!TouchPanel.IsFloatEvent(evt)) return;
            var thresholdProperty = entryProperty.FindPropertyRelative(nameof(WhenCondition.physBoneThreshold));
            if (thresholdProperty.floatValue == 0f) thresholdProperty.floatValue = 0.5f;
        }

        public void SetTouchMode(TouchMode mode)
        {
            if (Refuses || Row == null || Row.Entry == null) return;
            var property = owner.EntryProperty(Row.Entry);
            if (property == null) return;
            owner.SerializedObject.Update();
            property.FindPropertyRelative(nameof(MenuEntry.touchMode)).enumValueIndex = (int)mode;
            ReleaseFocus();
            owner.ApplyAndRefresh();
        }

        public void SetContactAnchor(Transform anchor)
        {
            if (Refuses || Row == null || Row.Entry == null) return;
            var property = ConditionProperty();
            if (property == null) return;
            owner.SerializedObject.Update();
            property.FindPropertyRelative(nameof(WhenCondition.contactAnchor)).objectReferenceValue = anchor;
            ReleaseFocus();
            owner.ApplyAndRefresh();
        }

        /// <summary>Adds or removes a tag from the contact's collision tag list, keeping the rest of the list as it is.</summary>
        public void ToggleContactTag(string tag, bool on)
        {
            if (Row == null || Row.Entry == null) return;
            WriteContactTag(ConditionProperty()?.propertyPath, tag, on);
        }

        /// <summary>The same for the condition at conditionPath: the one a tag control was built for, which need not belong to the row selected now, and which sits at another place in the component's list once an Undo or a removal moved its entry.</summary>
        internal void ToggleContactTag(string conditionPath, string tag, bool on) => WriteContactTag(Rebased(conditionPath), tag, on);

        /// <summary>A path this panel handed out while it was built, moved to where its entry sits now. Null when the entry is gone.</summary>
        string Rebased(string path)
        {
            if (path == null || boundEntryId == null || BoundPropertyPath == null || !path.StartsWith(BoundPropertyPath, System.StringComparison.Ordinal)) return path;
            if (path.Length > BoundPropertyPath.Length && path[BoundPropertyPath.Length] != '.') return path;
            owner.SerializedObject.Update();
            var entry = owner.Menu.FindEntry(boundEntryId);
            var current = entry == null ? null : owner.EntryProperty(entry)?.propertyPath;
            return current == null ? null : current + path.Substring(BoundPropertyPath.Length);
        }

        void WriteContactTag(string conditionPath, string tag, bool on)
        {
            if (Refuses || string.IsNullOrEmpty(conditionPath) || string.IsNullOrWhiteSpace(tag)) return;
            owner.SerializedObject.Update();
            var property = owner.SerializedObject.FindProperty(conditionPath);
            var tagsProperty = property?.FindPropertyRelative(nameof(WhenCondition.contactTags));
            if (tagsProperty == null) return;
            var current = new List<string>();
            for (int i = 0; i < tagsProperty.arraySize; i++) current.Add(tagsProperty.GetArrayElementAtIndex(i).stringValue);
            bool has = current.Contains(tag);
            if (on && !has) current.Add(tag);
            else if (!on && has) current.RemoveAll(t => t == tag);
            else return;
            tagsProperty.arraySize = current.Count;
            for (int i = 0; i < current.Count; i++) tagsProperty.GetArrayElementAtIndex(i).stringValue = current[i];
            ReleaseFocus();
            owner.ApplyAndRefresh();
        }

        /// <summary>Writes the contact's "closer than" fraction, 0 being an ordinary touch. live is for the slider, which writes once per pointer move and must not rebuild the panel under it.</summary>
        public void SetContactProximity(float value, bool live)
        {
            if (Refuses || Row == null || Row.Entry == null) return;
            var property = ConditionProperty();
            if (property == null) return;
            owner.SerializedObject.Update();
            property.FindPropertyRelative(nameof(WhenCondition.contactProximity)).floatValue = value;
            if (live) { owner.ApplyLive(); return; }
            ReleaseFocus();
            owner.ApplyAndRefresh();
        }

        public void SetPhysBone(VRCPhysBone physBone)
        {
            if (Refuses || Row == null || Row.Entry == null) return;
            var property = ConditionProperty();
            if (property == null) return;
            owner.SerializedObject.Update();
            property.FindPropertyRelative(nameof(WhenCondition.physBone)).objectReferenceValue = physBone;
            ReleaseFocus();
            owner.ApplyAndRefresh();
        }

        public void SetPhysBoneEvent(PhysBoneEvent evt)
        {
            if (Refuses || Row == null || Row.Entry == null) return;
            var property = ConditionProperty();
            if (property == null) return;
            var previous = (PhysBoneEvent)property.FindPropertyRelative(nameof(WhenCondition.physBoneEvent)).enumValueIndex;
            owner.SerializedObject.Update();
            property.FindPropertyRelative(nameof(WhenCondition.physBoneEvent)).enumValueIndex = (int)evt;
            // The threshold default only fills in the gap the first time a float event comes into play; a value
            // left at 0 while moving between two float events (Bent past to Stretched past, say) is deliberate.
            if (!TouchPanel.IsFloatEvent(previous) && TouchPanel.IsFloatEvent(evt)) ApplyPhysBoneThresholdDefault(property, evt);
            ReleaseFocus();
            owner.ApplyAndRefresh();
        }

        public void SetFlipToggle(string toggleId)
        {
            if (Refuses || Row == null || Row.Entry == null) return;
            var property = owner.EntryProperty(Row.Entry);
            if (property == null) return;
            ReleaseFocus();
            SetString(property.FindPropertyRelative(nameof(MenuEntry.flipToggleId)), toggleId ?? "");
        }

        public void SetSourceToggle(string id)
        {
            if (Refuses || Row == null || Row.Entry == null) return;
            var property = owner.EntryProperty(Row.Entry);
            if (property == null) return;
            ReleaseFocus();
            SetString(property.FindPropertyRelative(nameof(MenuEntry.sourceToggleId)), id ?? "");
        }

        public void SetSourceParameter(string name)
        {
            if (Refuses || Row == null || Row.Entry == null) return;
            var property = owner.EntryProperty(Row.Entry);
            if (property == null) return;
            ReleaseFocus();
            SetString(property.FindPropertyRelative(nameof(MenuEntry.sourceParameter)), name ?? "");
        }

        public void SetSourceShape(SkinnedMeshRenderer renderer, string shape)
        {
            if (Refuses || Row == null || Row.Entry == null) return;
            var property = owner.EntryProperty(Row.Entry);
            if (property == null) return;
            owner.SerializedObject.Update();
            property.FindPropertyRelative(nameof(MenuEntry.sourceRenderer)).objectReferenceValue = renderer;
            property.FindPropertyRelative(nameof(MenuEntry.sourceShape)).stringValue = shape;
            ReleaseFocus();
            owner.ApplyAndRefresh();
        }

        /// <summary>Drops the focus a picker button or dropdown took on click, so the refresh that follows a pick is not held back by the focus guard. Internal so TouchPanel's own click handlers can call it too.</summary>
        internal void ReleaseFocus()
        {
            var focused = panel?.focusController?.focusedElement as VisualElement;
            if (focused != null && body.Contains(focused)) focused.Blur();
        }
    }
}
#endif
