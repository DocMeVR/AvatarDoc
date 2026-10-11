#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// One of the target lists of an entry, bound to its serialized list. The list keeps the id of its entry beside
    /// its path and finds the entry again by it whenever it writes: while a typed value waits to be saved the panel
    /// is not rebuilt, and an Undo or a removal in front of the entry moves it to another place in the component's
    /// list. A write whose entry is gone is refused.
    /// </summary>
    public sealed class TargetListView : VisualElement
    {
        public enum Kind { Objects, BlendShapes, Materials, Properties, Animations, Follows }

        static readonly Regex EntryListPath = new Regex(@"^entries\.Array\.data\[(\d+)\]\.(.+)$");
        static readonly Regex EntryPrefix = new Regex(@"^entries\.Array\.data\[\d+\]");

        readonly AvatarDoctorView owner;
        SerializedProperty list;
        /// <summary>The id of the entry this list belongs to, or null for a list that is not under an entry of the component.</summary>
        readonly string entryId;
        /// <summary>The list's name inside its entry, such as "objects".</summary>
        readonly string listName;
        /// <summary>Where the entry was found in the component's list the last time.</summary>
        int entryIndex;
        /// <summary>Set while the entry is gone: the rows are unbound and switched off until it is back.</summary>
        bool released;
        readonly Kind kind;
        readonly bool radial;
        readonly bool link;
        readonly bool pinned;
        /// <summary>Non-null for every blendshape list that offers Capture: the entry its captured rows belong to. Only a toggle's or a trigger's list also offers Hold the rest and shows the rule lines, see <see cref="ShowsRule"/>.</summary>
        readonly MenuEntry captureEntry;
        readonly ListView listView = new ListView();
        VisualElement rulesBox;
        VisualElement shapeSetStatusBox;
        string lastShapeSetStatus = "";

        /// <summary>Set when a row changes its height (the off value line appears or goes), so the next reload rebuilds the rows instead of only rebinding them.</summary>
        bool rebuildRows;
        readonly Label count = new Label();

        public int Count => list.arraySize;

        /// <summary>Whether the section holds anything worth showing: a row, or a rule that holds the rest of a mesh, which has no row of its own.</summary>
        public bool HasContent
        {
            get
            {
                if (list.arraySize > 0) return true;
                if (!ShowsRule) return false;
                var entry = CapturedEntry() ?? captureEntry;
                return entry.shapeSets.Any(r => r != null);
            }
        }
        /// <summary>True for the blendshape list of a blendshape-source trigger: rows show a fixed/follow mode instead of a plain weight.</summary>
        public bool IsLinkMode => link;
        /// <summary>True for an override's blendshape list: rows show a slider labelled "Pinned value" and no mode dropdown.</summary>
        public bool IsPinnedMode => pinned;
        /// <summary>Only a toggle's or a trigger's blendshape list carries the rules that hold the rest of a mesh: a radial has two ends, and an override already pins by kind (MenuEntry.OffersShapeSets).</summary>
        bool ShowsRule => captureEntry != null && captureEntry.OffersShapeSets;

        public TargetListView(AvatarDoctorView owner, SerializedProperty list, Kind kind, bool radial, bool link = false, bool pinned = false, MenuEntry captureEntry = null)
        {
            this.owner = owner;
            this.list = list;
            this.kind = kind;
            this.radial = radial;
            this.link = link;
            this.pinned = pinned;
            this.captureEntry = captureEntry;
            var inEntry = EntryListPath.Match(list.propertyPath);
            if (inEntry.Success)
            {
                entryIndex = int.Parse(inEntry.Groups[1].Value);
                listName = inEntry.Groups[2].Value;
                var id = EntryIdAt(owner.SerializedObject.FindProperty(nameof(AvatarDoc.entries)), entryIndex);
                entryId = string.IsNullOrEmpty(id) ? null : id;
            }
            name = NameOf(kind);
            style.marginBottom = 8;

            var header = new VisualElement();
            header.AddToClassList("ad-row");
            var title = new Label(L.Text(TitleKeyOf(kind))) { tooltip = L.Text(TitleKeyOf(kind) + ".tip") };
            title.AddToClassList("ad-label");
            title.AddToClassList("ad-section");
            header.Add(title);
            count.AddToClassList("ad-chip");
            // The label column before it already reserves exactly 112px (every .ad-row .ad-label does), so the
            // chip's own default left margin would push it a few pixels past that column instead of sitting right
            // at its start.
            count.style.marginLeft = 0;
            header.Add(count);
            var spacer = new VisualElement();
            spacer.AddToClassList("ad-spacer");
            header.Add(spacer);
            var add = new Button { text = L.Text("ad.ui.add"), tooltip = L.Text(AddTooltipKeyOf(kind)) };
            add.clicked += () => OnAdd(add);
            header.Add(add);
            if (captureEntry != null)
            {
                var capture = new Button { text = L.Text("ad.ui.shape_set.capture"), name = "ad-shape-set-capture", tooltip = L.Text("ad.ui.shape_set.capture.tip") };
                capture.clicked += () => AvatarObjectPicker.Show(capture, owner.Plan.AvatarRoot, PickKind.SkinnedMeshWithShapes,
                    picked => CaptureFrom(picked as SkinnedMeshRenderer));
                header.Add(capture);
                if (ShowsRule)
                {
                    var hold = new Button { text = L.Text("ad.ui.shape_set.hold"), name = "ad-shape-set-hold", tooltip = L.Text("ad.ui.shape_set.hold.tip") };
                    hold.clicked += () => AvatarObjectPicker.Show(hold, owner.Plan.AvatarRoot, PickKind.SkinnedMeshWithShapes,
                        picked => HoldTheRest(picked as SkinnedMeshRenderer));
                    header.Add(hold);
                    // Three buttons do not fit beside the title at the narrow end of the inspector: the last one
                    // moves to a line of its own, still at the right.
                    header.AddToClassList("ad-header-wrap");
                }
            }
            Add(header);

            listView.reorderable = !owner.Locked;
            listView.reorderMode = ListViewReorderMode.Animated;
            listView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            listView.showBoundCollectionSize = false;
            listView.showBorder = false;
            listView.selectionType = SelectionType.None;
            // Bound here rather than through the panel around it: the detail panel writes its own fields directly,
            // so nothing else binds this subtree any more.
            listView.BindProperty(list);
            listView.makeItem = MakeRow;
            listView.bindItem = BindRow;
            listView.itemIndexChanged += (_, __) => owner.ScheduleRefresh();
            Add(listView);

            if (kind == Kind.Objects)
            {
                var hint = new Label(L.Text("ad.ui.drop.objects"));
                hint.AddToClassList("ad-drop-hint");
                Add(hint);
                this.AddManipulator(new HierarchyDropManipulator(this, Accepts, objects => AddObjects(objects)));
            }
            if (captureEntry != null)
            {
                if (ShowsRule)
                {
                    rulesBox = new VisualElement { name = "ad-shape-sets" };
                    Add(rulesBox);
                }
                shapeSetStatusBox = new VisualElement { name = "ad-shape-set-status-box" };
                Add(shapeSetStatusBox);
                RefreshRules();
            }
            UpdateCount();
        }

        static string NameOf(Kind kind)
        {
            switch (kind)
            {
                case Kind.Objects: return "ad-objects";
                case Kind.BlendShapes: return "ad-blendshapes";
                case Kind.Materials: return "ad-materials";
                case Kind.Properties: return "ad-properties";
                case Kind.Animations: return "ad-animations";
                default: return "ad-follows";
            }
        }

        static string TitleKeyOf(Kind kind)
        {
            switch (kind)
            {
                case Kind.Objects: return "ad.ui.objects";
                case Kind.BlendShapes: return "ad.ui.blendshapes";
                case Kind.Materials: return "ad.ui.materials";
                case Kind.Properties: return "ad.ui.targets.properties";
                case Kind.Animations: return "ad.ui.targets.animations";
                default: return "ad.ui.targets.follows";
            }
        }

        static string AddTooltipKeyOf(Kind kind)
        {
            switch (kind)
            {
                case Kind.Objects: return "ad.ui.objects.add.tip";
                case Kind.BlendShapes: return "ad.ui.blendshapes.add.tip";
                case Kind.Materials: return "ad.ui.materials.add.tip";
                case Kind.Properties: return "ad.ui.targets.properties.add.tip";
                case Kind.Animations: return "ad.ui.targets.animations.add.tip";
                default: return "ad.ui.targets.follows.add.tip";
            }
        }

        bool Accepts(GameObject go) => go != null && !MenuModel.IsOutsideAvatar(owner.Plan.AvatarRoot, go.transform);

        /// <summary>Whether this entry's component is locked. Every write below is guarded by this, since the list writes its entry's SerializedProperty directly rather than going through EntryEditing or MenuEditing.</summary>
        bool Locked => owner.Locked;

        void UpdateCount() => count.text = list.arraySize.ToString();

        /// <summary>
        /// Whether the serialized array this list was built on still belongs to the given entry: the same
        /// component, a path under the entry's own element, and an array that still resolves. False means the
        /// entry moved or went away under the panel, so a bind would read a different entry's list, or past the
        /// end of the array. Read by a test after a delete.
        /// </summary>
        /// <summary>The serialized path this list was built on. Read by a test's failure message.</summary>
        public string BoundPath => list.propertyPath;

        public bool DescribesEntry(SerializedProperty entryProperty)
        {
            if (entryProperty == null) return false;
            try
            {
                if (list.serializedObject.targetObject != entryProperty.serializedObject.targetObject) return false;
                if (!list.propertyPath.StartsWith(entryProperty.propertyPath + ".", System.StringComparison.Ordinal)) return false;
                var fresh = entryProperty.serializedObject.FindProperty(list.propertyPath);
                return fresh != null && fresh.isArray && fresh.arraySize == list.arraySize;
            }
            catch (System.Exception) { return false; }
        }

        static string EntryIdAt(SerializedProperty entries, int index)
        {
            if (entries == null || index < 0 || index >= entries.arraySize) return null;
            return entries.GetArrayElementAtIndex(index).FindPropertyRelative(nameof(MenuEntry.id))?.stringValue;
        }

        /// <summary>
        /// Points the list at the entry it was built for, wherever the component keeps that entry now, and binds the
        /// rows to it. False when no entry has the id any more: nothing may be written then, since the place the list
        /// was built for holds some other entry.
        /// </summary>
        bool Follow()
        {
            if (entryId == null) return true;
            var entries = owner.SerializedObject.FindProperty(nameof(AvatarDoc.entries));
            if (entries == null) return false;
            int index = -1;
            if (EntryIdAt(entries, entryIndex) == entryId) index = entryIndex;
            else
                for (int i = 0; i < entries.arraySize && index < 0; i++)
                    if (EntryIdAt(entries, i) == entryId) index = i;
            if (index < 0)
            {
                Release();
                return false;
            }
            if (index == entryIndex && !released) return true;
            entryIndex = index;
            list = entries.GetArrayElementAtIndex(index).FindPropertyRelative(listName);
            listView.Unbind();
            listView.BindProperty(list);
            if (released)
            {
                released = false;
                listView.SetEnabled(true);
            }
            return true;
        }

        /// <summary>The entry is gone. The rows stop following whatever entry took its place, and cannot be used.</summary>
        void Release()
        {
            if (released) return;
            released = true;
            listView.Unbind();
            listView.SetEnabled(false);
        }

        /// <summary>
        /// The panel waits to rebuild and this list stays on screen. A row dragged in a bound list is moved at the place
        /// the binding points at, which is another entry's once the entry has moved, so a list that no longer sits where
        /// its entry does is pointed at the entry now, or switched off when the entry is gone. A list that still sits
        /// where its entry does is left alone and keeps taking drags.
        /// </summary>
        public void Hold() => Begin();

        /// <summary>Starts a write: the serialized data is brought up to date and the list is pointed at its entry. False means the write is refused.</summary>
        bool Begin()
        {
            owner.SerializedObject.Update();
            return Follow();
        }

        /// <summary>A property by a path taken while this list was built or bound, which names the entry by the place it had then; null when the entry is gone.</summary>
        SerializedProperty Find(string path)
        {
            if (!Follow()) return null;
            if (entryId != null) path = EntryPrefix.Replace(path, nameof(AvatarDoc.entries) + ".Array.data[" + entryIndex + "]", 1);
            return owner.SerializedObject.FindProperty(path);
        }

        /// <summary>The entry the capture buttons and rule lines write to, as the component holds it now. The entry object a panel was built with is a stale copy once an Undo restored the list.</summary>
        MenuEntry CapturedEntry() => captureEntry == null ? null : owner.Menu.FindEntry(captureEntry.id);

        /// <summary>
        /// Rewrites every visible row from the data. The rows hold their values rather than binding them, so a
        /// change made elsewhere (an undo, the other view of this component) reaches them here. Held back entirely
        /// while the focus sits in this list: a property row rebuilds its value controls on every bind, so a
        /// refresh would throw away the very control the author is typing in.
        /// </summary>
        public void Reload()
        {
            Follow();
            listView.reorderable = !Locked;
            if (!DirectFields.Editing(this))
            {
                if (rebuildRows) listView.Rebuild();
                else listView.RefreshItems();
                rebuildRows = false;
            }
            UpdateCount();
            if (captureEntry != null) RefreshRules();
        }

        /// <summary>Rewrites one row from the data, exactly as a reload does. Public because an unattached ListView never materialises its rows, so that path can only be taken directly.</summary>
        public void ReloadRow(VisualElement row)
        {
            if (row?.userData is int index) BindRow(row, index);
        }

        /// <summary>Writes a row control from the data, unless the author is working in that very control.</summary>
        static void Load<T>(BaseField<T> field, T value)
        {
            if (!DirectFields.Editing(field)) field.SetValueWithoutNotify(value);
        }

        /// <summary>The property behind one field of the target at index, or null when that row is gone.</summary>
        SerializedProperty FieldAt(int index, string relative)
        {
            if (index < 0 || index >= list.arraySize) return null;
            return list.GetArrayElementAtIndex(index).FindPropertyRelative(relative);
        }

        /// <summary>
        /// Writes one field of a recycled row. While the component is locked the write is refused and the control
        /// is put back to what the data holds, so the lock does not depend on the panel's disabled state. The row
        /// stands for whichever index it is bound to at the moment of the write, so the index is read out of
        /// userData here rather than captured when the control was made. live is for a value dragged or typed
        /// continuously: it commits without rebuilding the panel under the pointer.
        /// </summary>
        void WriteRow(VisualElement row, string relative, System.Action<SerializedProperty> write, System.Action revert, bool live)
        {
            if (Locked) { revert(); return; }
            WriteField((int)row.userData, relative, write, live);
        }

        /// <summary>Writes one field of the target at index through the same guard a row's own control uses. Public so that guard can be taken on its own, without a panel to dispatch a field's change event through.</summary>
        public void WriteField(int index, string relative, System.Action<SerializedProperty> write, bool live = false)
        {
            if (Locked || !Begin()) return;
            var property = FieldAt(index, relative);
            if (property == null) return;
            write(property);
            if (live) owner.ApplyLive(); else owner.ApplyAndRefresh();
        }

        T ReadRow<T>(VisualElement row, string relative, System.Func<SerializedProperty, T> read)
        {
            var property = FieldAt((int)row.userData, relative);
            return property != null ? read(property) : default(T);
        }

        /// <summary>The same guarded write against one element's own path, for the property row's value controls: those are built fresh on every bind, so they can hold the path they write.</summary>
        void WritePath(string path, System.Action<SerializedProperty> write, System.Action revert, bool live)
        {
            if (Locked) { revert(); return; }
            WriteValueAt(path, write, live);
        }

        /// <summary>Writes one property value by its own path, through the same guard a property row's value control uses. Public so that guard can be taken on its own, without a panel to dispatch a field's change event through.</summary>
        public void WriteValueAt(string path, System.Action<SerializedProperty> write, bool live = false)
        {
            if (Locked || !Begin()) return;
            var property = Find(path);
            if (property == null) return;
            write(property);
            if (live) owner.ApplyLive(); else owner.ApplyAndRefresh();
        }

        T ReadPath<T>(string path, System.Func<SerializedProperty, T> read)
        {
            var property = Find(path);
            return property != null ? read(property) : default(T);
        }

        ObjectField RowObject(VisualElement row, string relative, System.Type type, bool allowSceneObjects, PickKind? pick = null)
        {
            var field = new ObjectField { objectType = type, allowSceneObjects = allowSceneObjects };
            field.RegisterValueChangedCallback(e => WriteRow(row, relative,
                p => p.objectReferenceValue = e.newValue,
                () => field.SetValueWithoutNotify(ReadRow(row, relative, p => p.objectReferenceValue)), false));
            if (pick.HasValue) AvatarObjectPicker.TakeOverSelector(field, () => owner.Plan?.AvatarRoot, pick.Value, picked => field.value = picked);
            return field;
        }

        IntegerField RowInt(VisualElement row, string relative, string label)
        {
            var field = new IntegerField(label);
            field.RegisterValueChangedCallback(e => WriteRow(row, relative,
                p => p.intValue = e.newValue,
                () => field.SetValueWithoutNotify(ReadRow(row, relative, p => p.intValue)), false));
            return field;
        }

        FloatField RowFloat(VisualElement row, string relative, string label)
        {
            var field = new FloatField(label);
            field.RegisterValueChangedCallback(e => WriteRow(row, relative,
                p => p.floatValue = e.newValue,
                () => field.SetValueWithoutNotify(ReadRow(row, relative, p => p.floatValue)), true));
            return field;
        }

        Slider RowSlider(VisualElement row, string relative, string label, float min, float max)
        {
            var field = new Slider(label, min, max) { showInputField = true };
            field.RegisterValueChangedCallback(e => WriteRow(row, relative,
                p => p.floatValue = e.newValue,
                () => field.SetValueWithoutNotify(ReadRow(row, relative, p => p.floatValue)), true));
            return field;
        }

        VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("ad-target");
            // The controls wrap onto another line inside this box when the inspector is narrow; the remove button
            // sits beside it, so it stays at the row's top right however many lines the controls take.
            var main = new VisualElement { name = "ad-target-main" };
            main.AddToClassList("ad-target-main");
            row.Add(main);
            VisualElement offLine = null;
            switch (kind)
            {
                case Kind.Objects:
                {
                    var target = RowObject(row, nameof(ObjectTarget.target), typeof(GameObject), true, PickKind.GameObject);
                    target.name = "target";
                    target.tooltip = L.Text("ad.ui.target.object.tip");
                    target.AddToClassList("ad-target-object");
                    main.Add(target);
                    var mode = new SegmentedChoice(new List<string> { L.Text("ad.ui.seg.shown"), L.Text("ad.ui.seg.hidden") }) { name = "mode", tooltip = L.Text("ad.ui.target.mode.tip") };
                    mode.AddToClassList("ad-w-132");
                    // Registered once here, not in BindRow: the ListView recycles this row across indices, so a
                    // handler added on every bind would pile up one closure per bind, each writing a stale index.
                    mode.Changed += newIndex =>
                    {
                        if (Locked || !Begin()) return;
                        int index = (int)row.userData;
                        var active = list.GetArrayElementAtIndex(index).FindPropertyRelative(nameof(ObjectTarget.activeWhenOn));
                        active.boolValue = newIndex == 0;
                        owner.ApplyAndRefresh();
                    };
                    main.Add(mode);
                    break;
                }
                case Kind.BlendShapes:
                {
                    // A long renderer and shape name ends in an ellipsis; the tooltip BindRow writes names it in full,
                    // so Unity's own tooltip for elided text is switched off rather than left to replace it.
                    var shape = new Button { name = "shape", tooltip = L.Text("ad.ui.target.shape.tip"), displayTooltipWhenElided = false };
                    shape.AddToClassList("ad-shape-button");
                    // The row element is recycled across indices, so the element this click belongs to is pinned
                    // down by its property path here; by the time the picker closes the row may stand for another one.
                    shape.clicked += () =>
                    {
                        if (!Follow()) return;
                        var path = list.GetArrayElementAtIndex((int)row.userData).propertyPath;
                        ShapePicker.Show(shape, owner.Plan.AvatarRoot, (r, s) => SetShape(path, r, s));
                    };
                    main.Add(shape);
                    var usage = new Label();
                    usage.AddToClassList("ad-chip");
                    usage.AddToClassList("ad-chip-usage");
                    usage.pickingMode = PickingMode.Position;
                    main.Add(usage);
                    if (radial)
                    {
                        // The two ends share a line of their own under the shape, one under the other only when the row is too narrow for both.
                        var range = Group("range");
                        range.AddToClassList("ad-target-range");
                        var min = RowFloat(row, nameof(BlendShapeTarget.minWeight), L.Text("ad.ui.min"));
                        min.name = "min";
                        min.tooltip = L.Text("ad.ui.min.tip");
                        min.AddToClassList("ad-target-number");
                        range.Add(min);
                        var max = RowFloat(row, nameof(BlendShapeTarget.maxWeight), L.Text("ad.ui.max"));
                        max.name = "max";
                        max.tooltip = L.Text("ad.ui.max.tip");
                        max.AddToClassList("ad-target-number");
                        range.Add(max);
                        main.Add(range);
                    }
                    else if (link)
                    {
                        var modeChoices = new List<string> { L.Text("ad.ui.effect.fixed"), L.Text("ad.ui.effect.follow") };
                        var mode = new DropdownField(modeChoices, 0) { name = "effect-mode", tooltip = L.Text("ad.ui.effect.link_mode.tip") };
                        mode.AddToClassList("ad-w-132");
                        // Registered once here, not in BindRow: the ListView recycles this row across indices, so a
                        // handler added on every bind would pile up one closure per bind, each writing a stale index.
                        mode.RegisterValueChangedCallback(e =>
                        {
                            if (Locked || !Begin()) return;
                            int index = (int)row.userData;
                            var modeProperty = list.GetArrayElementAtIndex(index).FindPropertyRelative(nameof(BlendShapeTarget.mode));
                            modeProperty.enumValueIndex = modeChoices.IndexOf(e.newValue);
                            owner.ApplyAndRefresh();
                        });
                        main.Add(mode);
                        var weight = RowSlider(row, nameof(BlendShapeTarget.weight), L.Text("ad.ui.weight"), 0f, 100f);
                        weight.name = "weight";
                        weight.tooltip = L.Text("ad.ui.weight.tip");
                        weight.AddToClassList("ad-target-slider");
                        weight.AddToClassList("ad-weight-slider");
                        main.Add(weight);
                        var multiplier = RowFloat(row, nameof(BlendShapeTarget.multiplier), L.Text("ad.ui.effect.multiplier"));
                        multiplier.name = "multiplier";
                        multiplier.tooltip = L.Text("ad.ui.effect.multiplier.tip");
                        multiplier.AddToClassList("ad-target-number");
                        main.Add(multiplier);
                    }
                    else if (pinned)
                    {
                        var weight = RowSlider(row, nameof(BlendShapeTarget.weight), L.Text("ad.ui.override.value"), 0f, 100f);
                        weight.name = "weight";
                        weight.tooltip = L.Text("ad.ui.override.value.tip");
                        weight.AddToClassList("ad-target-slider");
                        weight.AddToClassList("ad-weight-slider");
                        main.Add(weight);
                    }
                    else
                    {
                        var mode = new SegmentedChoice(new List<string> { L.Text("ad.ui.seg.fixed"), L.Text("ad.ui.seg.pin") }) { name = "effect-mode", tooltip = L.Text("ad.ui.seg.fixed_pin.tip") };
                        mode.AddToClassList("ad-w-132");
                        // Follow distance blends every row from rest to its value the same way, whatever mode is
                        // stored, so the choice is hidden rather than offering a pick that no longer changes anything.
                        mode.EnableInClassList("ad-hidden", captureEntry != null && captureEntry.IsFollowDistanceTrigger);
                        // Registered once here, not in BindRow: the ListView recycles this row across indices, so a
                        // handler added on every bind would pile up one closure per bind, each writing a stale index.
                        mode.Changed += newIndex =>
                        {
                            int index = (int)row.userData;
                            SetShapeMode(index, newIndex == 1 ? ShapeEffectMode.Pin : ShapeEffectMode.Fixed);
                        };
                        main.Add(mode);
                        var weight = RowSlider(row, nameof(BlendShapeTarget.weight), L.Text("ad.ui.weight"), 0f, 100f);
                        weight.name = "weight";
                        weight.tooltip = L.Text("ad.ui.weight.tip");
                        weight.AddToClassList("ad-target-slider");
                        weight.AddToClassList("ad-weight-slider");
                        main.Add(weight);
                        var explicitOff = new Toggle(L.Text("ad.ui.target.explicit_off")) { name = "explicit-off", tooltip = L.Text("ad.ui.target.explicit_off.tip") };
                        explicitOff.AddToClassList("ad-compact");
                        // Registered once here, not in BindRow, for the same reason the mode choice above is.
                        explicitOff.RegisterValueChangedCallback(e =>
                        {
                            explicitOff.Blur();
                            SetShapeExplicitOff((int)row.userData, e.newValue);
                        });
                        offLine = OffLine();
                        offLine.Add(explicitOff);
                        var offValue = RowSlider(row, nameof(BlendShapeTarget.minWeight), L.Text("ad.ui.target.off_value"), 0f, 100f);
                        offValue.name = "off-value";
                        offValue.tooltip = L.Text("ad.ui.target.off_value.tip");
                        offValue.AddToClassList("ad-target-slider");
                        offValue.AddToClassList("ad-weight-slider");
                        offLine.Add(offValue);
                    }
                    break;
                }
                case Kind.Materials:
                {
                    var renderer = RowObject(row, nameof(MaterialTarget.renderer), typeof(Renderer), true, PickKind.Renderer);
                    renderer.name = "renderer";
                    renderer.tooltip = L.Text("ad.ui.target.renderer.tip");
                    renderer.AddToClassList("ad-target-object");
                    main.Add(renderer);
                    var slot = RowInt(row, nameof(MaterialTarget.slot), L.Text("ad.ui.slot"));
                    slot.name = "slot";
                    slot.tooltip = L.Text("ad.ui.slot.tip");
                    slot.AddToClassList("ad-target-number");
                    main.Add(slot);
                    var material = RowObject(row, nameof(MaterialTarget.material), typeof(Material), false);
                    material.name = "material";
                    material.tooltip = L.Text("ad.ui.target.material.tip");
                    material.AddToClassList("ad-target-object");
                    main.Add(material);
                    break;
                }
                case Kind.Properties:
                {
                    var renderer = RowObject(row, nameof(MaterialPropertyTarget.renderer), typeof(Renderer), true, PickKind.Renderer);
                    renderer.name = "renderer";
                    renderer.tooltip = L.Text("ad.ui.property.renderer.tip");
                    renderer.AddToClassList("ad-target-object");
                    main.Add(renderer);
                    var pick = new Button { name = "property", tooltip = L.Text("ad.ui.property.pick_button.tip"), displayTooltipWhenElided = false };
                    pick.AddToClassList("ad-target-pick");
                    // Registered once here, not in BindRow: the ListView recycles this row across indices, so a
                    // handler added on every bind would pile up one closure per bind, each reading a stale index.
                    pick.clicked += () =>
                    {
                        if (!Follow()) return;
                        int index = (int)row.userData;
                        var element = list.GetArrayElementAtIndex(index);
                        var currentRenderer = element.FindPropertyRelative(nameof(MaterialPropertyTarget.renderer)).objectReferenceValue as Renderer;
                        ShowPropertyMenu(pick, currentRenderer, element.propertyPath);
                    };
                    main.Add(pick);
                    var values = new VisualElement { name = "values" };
                    values.AddToClassList("ad-property-values");
                    main.Add(values);
                    if (!radial) offLine = OffLine();
                    break;
                }
                case Kind.Animations:
                {
                    var clip = RowObject(row, nameof(AnimationTarget.clip), typeof(AnimationClip), false);
                    clip.name = "clip";
                    clip.tooltip = L.Text("ad.ui.target.clip.tip");
                    clip.AddToClassList("ad-target-object");
                    main.Add(clip);
                    var pathRoot = RowObject(row, nameof(AnimationTarget.pathRoot), typeof(Transform), true, PickKind.Transform);
                    pathRoot.name = "path-root";
                    pathRoot.label = L.Text("ad.ui.animation.path_root");
                    pathRoot.tooltip = L.Text("ad.ui.animation.path_root.tip");
                    pathRoot.AddToClassList("ad-target-object");
                    main.Add(pathRoot);
                    break;
                }
                case Kind.Follows:
                {
                    var target = RowObject(row, nameof(FollowTarget.target), typeof(GameObject), true, PickKind.GameObject);
                    target.name = "target";
                    target.tooltip = L.Text("ad.ui.target.follow_object.tip");
                    target.AddToClassList("ad-target-object");
                    main.Add(target);
                    var follows = RowObject(row, nameof(FollowTarget.follows), typeof(Transform), true, PickKind.Transform);
                    follows.name = "follows";
                    follows.tooltip = L.Text("ad.ui.target.follow_target.tip");
                    follows.AddToClassList("ad-target-object");
                    main.Add(follows);
                    // Keep offset used to sit as a third control crammed onto the same line as the two grow object
                    // fields: with both already claiming the row's width, its own 112px label column left the
                    // checkbox pushed past the row's right edge, cut off. It now gets a real labelled row of its
                    // own underneath, indented under the target/follows row the way a multi-line target's other
                    // continuation lines are: a caption in the label column, the checkbox at the field column.
                    var keepOffsetRow = new VisualElement { name = "keep-offset-row" };
                    keepOffsetRow.AddToClassList("ad-row");
                    keepOffsetRow.AddToClassList("ad-target-off");
                    keepOffsetRow.AddToClassList("ad-target-sub-row");
                    var keepOffsetLabel = new Label(L.Text("ad.ui.target.keep_offset"));
                    keepOffsetLabel.AddToClassList("ad-label");
                    keepOffsetRow.Add(keepOffsetLabel);
                    var keepOffset = new Toggle { name = "keep-offset", tooltip = L.Text("ad.ui.target.keep_offset.tip") };
                    keepOffset.RegisterValueChangedCallback(e => WriteRow(row, nameof(FollowTarget.keepOffset),
                        p => p.boolValue = e.newValue,
                        () => keepOffset.SetValueWithoutNotify(ReadRow(row, nameof(FollowTarget.keepOffset), p => p.boolValue)), false));
                    keepOffsetRow.Add(keepOffset);
                    offLine = keepOffsetRow;
                    break;
                }
            }
            var remove = new Button { text = "×", name = "remove" };
            remove.AddToClassList("ad-target-value");
            remove.AddToClassList("ad-target-remove");
            remove.AddToClassList("ad-w-24");
            remove.clicked += () => RemoveAt((int)row.userData);
            row.Add(remove);
            if (offLine != null)
            {
                // A line of its own under the row: beside the target and its on value the switch and the off value
                // do not fit the default inspector width.
                row.AddToClassList("ad-target-wrap");
                row.Add(offLine);
            }
            return row;
        }

        static VisualElement OffLine()
        {
            var line = new VisualElement { name = "off-line" };
            line.AddToClassList("ad-target-off");
            return line;
        }

        /// <summary>Controls that belong together, such as two of a vector's four numbers: a narrow row moves them to the next line as one.</summary>
        static VisualElement Group(string name)
        {
            var group = new VisualElement { name = name };
            group.AddToClassList("ad-target-group");
            return group;
        }

        void BindRow(VisualElement row, int index)
        {
            // The remove/shape click handlers and the mode dropdown's change handler are registered once in
            // MakeRow, against this recycled row element; they read the current index back out of userData.
            row.userData = index;
            // A virtualized list can ask for a row the array no longer has, in the same frame a delete shrank the
            // component under it; that row is left blank and disabled until the refresh that follows rebinds it.
            int size;
            try { size = list.arraySize; }
            catch (System.Exception) { size = 0; }
            if (index < 0 || index >= size) { row.SetEnabled(false); return; }
            row.SetEnabled(!Locked);
            var element = list.GetArrayElementAtIndex(index);
            if (element == null) { row.SetEnabled(false); return; }
            switch (kind)
            {
                case Kind.Objects:
                {
                    Load(row.Q<ObjectField>("target"), element.FindPropertyRelative(nameof(ObjectTarget.target)).objectReferenceValue);
                    var mode = row.Q<SegmentedChoice>("mode");
                    var active = element.FindPropertyRelative(nameof(ObjectTarget.activeWhenOn));
                    if (!DirectFields.Editing(mode)) mode.SetIndexWithoutNotify(active.boolValue ? 0 : 1);
                    break;
                }
                case Kind.BlendShapes:
                {
                    var renderer = element.FindPropertyRelative(nameof(BlendShapeTarget.renderer)).objectReferenceValue as SkinnedMeshRenderer;
                    var shape = element.FindPropertyRelative(nameof(BlendShapeTarget.shape)).stringValue;
                    var shapeButton = row.Q<Button>("shape");
                    shapeButton.text = (renderer != null ? renderer.name : "?") + " › " + shape;
                    shapeButton.tooltip = shapeButton.text + "\n" + L.Text("ad.ui.target.shape.tip");
                    var usage = row.Q<Label>(className: "ad-chip-usage");
                    var clips = owner.Plan.Usage.ClipsFor(renderer, shape);
                    usage.EnableInClassList("ad-hidden", clips.Count == 0);
                    usage.text = string.Format(L.Text("ad.ui.usage.chip"), clips.Count);
                    usage.tooltip = clips.Count > 0 ? L.Text("ad.ui.usage.chip.tip") + "\n" + string.Join("\n", clips.Select(c => c.name)) : L.Text("ad.ui.usage.chip.tip");
                    if (radial)
                    {
                        Load(row.Q<FloatField>("min"), element.FindPropertyRelative(nameof(BlendShapeTarget.minWeight)).floatValue);
                        Load(row.Q<FloatField>("max"), element.FindPropertyRelative(nameof(BlendShapeTarget.maxWeight)).floatValue);
                    }
                    else if (link)
                    {
                        var modeProperty = element.FindPropertyRelative(nameof(BlendShapeTarget.mode));
                        bool follow = (ShapeEffectMode)modeProperty.enumValueIndex == ShapeEffectMode.Follow;
                        var modeField = row.Q<DropdownField>("effect-mode");
                        Load(modeField, modeField.choices[follow ? 1 : 0]);
                        var weight = row.Q<Slider>("weight");
                        Load(weight, element.FindPropertyRelative(nameof(BlendShapeTarget.weight)).floatValue);
                        weight.EnableInClassList("ad-hidden", follow);
                        var multiplier = row.Q<FloatField>("multiplier");
                        Load(multiplier, element.FindPropertyRelative(nameof(BlendShapeTarget.multiplier)).floatValue);
                        multiplier.EnableInClassList("ad-hidden", !follow);
                    }
                    else if (pinned)
                    {
                        Load(row.Q<Slider>("weight"), element.FindPropertyRelative(nameof(BlendShapeTarget.weight)).floatValue);
                    }
                    else
                    {
                        var modeProperty = element.FindPropertyRelative(nameof(BlendShapeTarget.mode));
                        bool pin = (ShapeEffectMode)modeProperty.enumValueIndex == ShapeEffectMode.Pin;
                        var modeField = row.Q<SegmentedChoice>("effect-mode");
                        if (!DirectFields.Editing(modeField)) modeField.SetIndexWithoutNotify(pin ? 1 : 0);
                        Load(row.Q<Slider>("weight"), element.FindPropertyRelative(nameof(BlendShapeTarget.weight)).floatValue);

                        var explicitOffProperty = element.FindPropertyRelative(nameof(BlendShapeTarget.explicitOff));
                        var explicitOffField = row.Q<Toggle>("explicit-off");
                        row.Q<VisualElement>("off-line").EnableInClassList("ad-hidden", pin);
                        if (!DirectFields.Editing(explicitOffField)) explicitOffField.SetValueWithoutNotify(explicitOffProperty.boolValue);
                        var offValueField = row.Q<Slider>("off-value");
                        offValueField.EnableInClassList("ad-hidden", pin || !explicitOffProperty.boolValue);
                        Load(offValueField, element.FindPropertyRelative(nameof(BlendShapeTarget.minWeight)).floatValue);
                    }
                    break;
                }
                case Kind.Materials:
                    Load(row.Q<ObjectField>("renderer"), element.FindPropertyRelative(nameof(MaterialTarget.renderer)).objectReferenceValue);
                    Load(row.Q<IntegerField>("slot"), element.FindPropertyRelative(nameof(MaterialTarget.slot)).intValue);
                    Load(row.Q<ObjectField>("material"), element.FindPropertyRelative(nameof(MaterialTarget.material)).objectReferenceValue);
                    break;
                case Kind.Properties:
                    BindPropertyRow(row, element);
                    break;
                case Kind.Animations:
                    Load(row.Q<ObjectField>("clip"), element.FindPropertyRelative(nameof(AnimationTarget.clip)).objectReferenceValue);
                    Load(row.Q<ObjectField>("path-root"), element.FindPropertyRelative(nameof(AnimationTarget.pathRoot)).objectReferenceValue);
                    break;
                case Kind.Follows:
                    Load(row.Q<ObjectField>("target"), element.FindPropertyRelative(nameof(FollowTarget.target)).objectReferenceValue);
                    Load(row.Q<ObjectField>("follows"), element.FindPropertyRelative(nameof(FollowTarget.follows)).objectReferenceValue);
                    Load(row.Q<Toggle>("keep-offset"), element.FindPropertyRelative(nameof(FollowTarget.keepOffset)).boolValue);
                    break;
            }
        }

        /// <summary>
        /// Rebuilds the value controls of a property row from scratch on every bind: unlike the other kinds, a
        /// property row's control set depends on the target's own type, which can differ between the indices a
        /// recycled row is bound to, so it cannot be decided once in MakeRow the way the other kinds' rows are.
        /// </summary>
        void BindPropertyRow(VisualElement row, SerializedProperty element)
        {
            var rendererProperty = element.FindPropertyRelative(nameof(MaterialPropertyTarget.renderer));
            Load(row.Q<ObjectField>("renderer"), rendererProperty.objectReferenceValue);
            var currentRenderer = rendererProperty.objectReferenceValue as Renderer;
            var propertyProperty = element.FindPropertyRelative(nameof(MaterialPropertyTarget.property));
            string propertyName = propertyProperty.stringValue;

            var choices = PropertyPicker.Choices(currentRenderer);
            bool hasMatch = false;
            PropertyPicker.Choice match = default;
            if (!string.IsNullOrEmpty(propertyName))
                foreach (var choice in choices)
                    if (choice.PropertyName == propertyName) { match = choice; hasMatch = true; break; }

            var pick = row.Q<Button>("property");
            pick.text = string.IsNullOrEmpty(propertyName) ? L.Text("ad.ui.property.pick") : (hasMatch ? match.DisplayName : propertyName);
            // A long display name ends in an ellipsis, so the tooltip names it in full, with the shader's own name when it differs.
            pick.tooltip = string.IsNullOrEmpty(propertyName) ? L.Text("ad.ui.property.pick_button.tip")
                : hasMatch && match.DisplayName != propertyName ? match.DisplayName + "\n" + propertyName : propertyName;

            var typeProperty = element.FindPropertyRelative(nameof(MaterialPropertyTarget.type));
            var values = row.Q<VisualElement>("values");
            // These controls are made fresh on every bind, so rebuilding them would destroy the one the author is
            // typing in rather than merely overwrite it.
            var offLine = row.Q<VisualElement>("off-line");
            if (DirectFields.Editing(values) || (offLine != null && DirectFields.Editing(offLine))) return;
            values.Clear();
            offLine?.Clear();

            PropertyValueType type;
            bool isRangeFloat = false;
            float rangeMin = 0f, rangeMax = 1f;
            if (hasMatch)
            {
                type = match.Type;
                isRangeFloat = match.IsRange;
                rangeMin = match.RangeMin;
                rangeMax = match.RangeMax;
            }
            else
            {
                type = (PropertyValueType)typeProperty.enumValueIndex;
                var typeField = TypeChoiceField(typeProperty);
                typeField.tooltip = L.Text("ad.ui.property.value_type.tip");
                values.Add(typeField);
            }

            var valueProperty = element.FindPropertyRelative(nameof(MaterialPropertyTarget.value));
            var colorProperty = element.FindPropertyRelative(nameof(MaterialPropertyTarget.color));
            var vectorProperty = element.FindPropertyRelative(nameof(MaterialPropertyTarget.vector));
            var minValueProperty = element.FindPropertyRelative(nameof(MaterialPropertyTarget.minValue));
            var minColorProperty = element.FindPropertyRelative(nameof(MaterialPropertyTarget.minColor));
            var minVectorProperty = element.FindPropertyRelative(nameof(MaterialPropertyTarget.minVector));
            var explicitOffProperty = element.FindPropertyRelative(nameof(MaterialPropertyTarget.explicitOff));
            var elementPath = element.propertyPath;

            // Each value carries its caption as its own label, so a narrow row moves a caption to the next line
            // together with its value instead of leaving it behind at the end of a line.
            string minCaption = L.Text("ad.ui.min");
            string valueCaption = L.Text(radial ? "ad.ui.max" : "ad.ui.target.on_value");
            string offCaption = L.Text("ad.ui.target.off_value");
            string valueTip = L.Text(radial ? "ad.ui.property.max.tip" : "ad.ui.property.value.tip");
            string minTip = L.Text("ad.ui.property.min.tip");
            string offTip = L.Text("ad.ui.target.off_value.tip");
            switch (type)
            {
                case PropertyValueType.Color:
                    if (radial) values.Add(ColorControl(minColorProperty, "min", minTip, minCaption));
                    values.Add(ColorControl(colorProperty, "value", valueTip, valueCaption));
                    if (!radial) AddExplicitOffControls(offLine, explicitOffProperty, elementPath, () => ColorControl(minColorProperty, "off-value", offTip, offCaption));
                    break;
                case PropertyValueType.Vector:
                    if (radial) values.Add(VectorControl(minVectorProperty, "min", minTip, minCaption));
                    values.Add(VectorControl(vectorProperty, "value", valueTip, valueCaption));
                    if (!radial) AddExplicitOffControls(offLine, explicitOffProperty, elementPath, () => VectorControl(minVectorProperty, "off-value", offTip, offCaption));
                    break;
                default:
                    if (radial) values.Add(FloatControl(minValueProperty, "min", isRangeFloat, rangeMin, rangeMax, minTip, minCaption));
                    values.Add(FloatControl(valueProperty, "value", isRangeFloat, rangeMin, rangeMax, valueTip, valueCaption));
                    if (!radial) AddExplicitOffControls(offLine, explicitOffProperty, elementPath, () => FloatControl(minValueProperty, "off-value", isRangeFloat, rangeMin, rangeMax, offTip, offCaption));
                    break;
            }
        }

        /// <summary>The "set a value when off" switch and, when it is on, the off value control of the same type as the on value. Property targets have no pin mode, so a toggle target always offers this, unlike a radial's fixed two ends.</summary>
        void AddExplicitOffControls(VisualElement values, SerializedProperty explicitOffProperty, string elementPath, System.Func<VisualElement> makeOffValueControl)
        {
            if (values == null) return;
            var toggle = new Toggle(L.Text("ad.ui.target.explicit_off")) { name = "explicit-off", tooltip = L.Text("ad.ui.target.explicit_off.tip") };
            toggle.AddToClassList("ad-compact");
            toggle.SetValueWithoutNotify(explicitOffProperty.boolValue);
            toggle.RegisterValueChangedCallback(e =>
            {
                toggle.Blur();
                SetPropertyExplicitOff(elementPath, e.newValue);
            });
            values.Add(toggle);
            if (explicitOffProperty.boolValue) values.Add(makeOffValueControl());
        }

        /// <summary>Applies a pick from a row's explicit-off switch by index, exactly as clicking it would.</summary>
        public void SetPropertyExplicitOff(int index, bool value)
        {
            if (!Follow()) return;
            SetPropertyExplicitOff(list.GetArrayElementAtIndex(index).propertyPath, value);
        }

        /// <summary>
        /// Sets whether a toggle's property writes an off value of its own instead of returning to what the
        /// materials already hold. Turning it on seeds the lower end field from the current material value, so
        /// nothing changes visibly until the value is edited.
        /// </summary>
        void SetPropertyExplicitOff(string elementPath, bool value)
        {
            if (Locked || !Begin()) return;
            var element = Find(elementPath);
            if (element == null) return;
            element.FindPropertyRelative(nameof(MaterialPropertyTarget.explicitOff)).boolValue = value;
            rebuildRows = true;
            if (value)
            {
                var renderer = element.FindPropertyRelative(nameof(MaterialPropertyTarget.renderer)).objectReferenceValue as Renderer;
                var propertyName = element.FindPropertyRelative(nameof(MaterialPropertyTarget.property)).stringValue;
                var type = (PropertyValueType)element.FindPropertyRelative(nameof(MaterialPropertyTarget.type)).enumValueIndex;
                var material = MenuModel.MaterialWithProperty(renderer, propertyName);
                if (material != null)
                {
                    var packed = MenuModel.ReadProperty(material, propertyName, type);
                    switch (type)
                    {
                        case PropertyValueType.Color:
                            element.FindPropertyRelative(nameof(MaterialPropertyTarget.minColor)).colorValue = packed;
                            break;
                        case PropertyValueType.Vector:
                            element.FindPropertyRelative(nameof(MaterialPropertyTarget.minVector)).vector4Value = packed;
                            break;
                        default:
                            element.FindPropertyRelative(nameof(MaterialPropertyTarget.minValue)).floatValue = packed.x;
                            break;
                    }
                }
            }
            owner.ApplyAndRefresh();
        }

        SegmentedChoice TypeChoiceField(SerializedProperty typeProperty)
        {
            var choices = new List<string> { L.Text("ad.ui.property.type.float"), L.Text("ad.ui.property.type.color"), L.Text("ad.ui.property.type.vector") };
            var field = new SegmentedChoice(choices, Mathf.Clamp(typeProperty.enumValueIndex, 0, choices.Count - 1)) { name = "type" };
            field.AddToClassList("ad-target-choice");
            field.Changed += index =>
            {
                if (Locked || !Begin()) return;
                var property = Find(typeProperty.propertyPath);
                if (property == null) return;
                property.enumValueIndex = index;
                owner.ApplyAndRefresh();
            };
            return field;
        }

        /// <summary>A caption in the look of a field's own label, for a control that has none of its own.</summary>
        static VisualElement Caption(string text, string tooltip = null)
        {
            var label = new Label(text) { tooltip = tooltip };
            label.AddToClassList("ad-label");
            label.AddToClassList("ad-caption");
            return label;
        }

        VisualElement FloatControl(SerializedProperty property, string name, bool isRange, float min, float max, string tip, string caption)
        {
            var path = property.propertyPath;
            if (isRange)
            {
                var slider = new Slider(caption, min, max) { showInputField = true, name = name, tooltip = tip };
                slider.AddToClassList("ad-target-slider");
                slider.SetValueWithoutNotify(property.floatValue);
                slider.RegisterValueChangedCallback(e => WritePath(path, p => p.floatValue = e.newValue,
                    () => slider.SetValueWithoutNotify(ReadPath(path, p => p.floatValue)), true));
                return slider;
            }
            var field = new FloatField(caption) { name = name, tooltip = tip };
            field.AddToClassList("ad-target-number");
            field.SetValueWithoutNotify(property.floatValue);
            field.RegisterValueChangedCallback(e => WritePath(path, p => p.floatValue = e.newValue,
                () => field.SetValueWithoutNotify(ReadPath(path, p => p.floatValue)), true));
            return field;
        }

        VisualElement ColorControl(SerializedProperty property, string name, string tip, string caption)
        {
            var path = property.propertyPath;
            var field = new ColorField(caption) { hdr = true, showAlpha = true, name = name, tooltip = tip };
            field.AddToClassList("ad-target-color");
            field.SetValueWithoutNotify(property.colorValue);
            field.RegisterValueChangedCallback(e => WritePath(path, p => p.colorValue = e.newValue,
                () => field.SetValueWithoutNotify(ReadPath(path, p => p.colorValue)), true));
            return field;
        }

        static readonly string[] VectorAxes = { "x", "y", "z", "w" };

        /// <summary>Four number fields after an optional caption, in two pairs: a narrow row puts Z and W under X and Y, never one field alone on a line.</summary>
        VisualElement VectorControl(SerializedProperty vectorProperty, string name, string tip, string caption)
        {
            var container = new VisualElement { name = name, tooltip = tip };
            container.AddToClassList("ad-property-vector");
            if (caption != null) container.Add(Caption(caption, tip));
            VisualElement pair = null;
            foreach (var axis in VectorAxes)
            {
                if (pair == null || pair.childCount == 2)
                {
                    pair = Group(name + "-pair");
                    container.Add(pair);
                }
                var axisProperty = vectorProperty.FindPropertyRelative(axis);
                var axisPath = axisProperty.propertyPath;
                var field = new FloatField { name = name + "-" + axis, tooltip = tip };
                field.AddToClassList("ad-target-number");
                field.SetValueWithoutNotify(axisProperty.floatValue);
                field.RegisterValueChangedCallback(e => WritePath(axisPath, p => p.floatValue = e.newValue,
                    () => field.SetValueWithoutNotify(ReadPath(axisPath, p => p.floatValue)), true));
                pair.Add(field);
            }
            return container;
        }

        /// <summary>The property dropdown: every choice the renderer's shaders list, grouped into submenus by the first letter, or a fixed chunk of the alphabet when there are more than 40; "Other..." always comes first, for a property typed by hand. Its search field finds a property by its display name or its shader name, which shows beside the display name when the two differ.</summary>
        void ShowPropertyMenu(VisualElement anchorElement, Renderer renderer, string elementPath)
        {
            var anchor = anchorElement.worldBound;
            var items = new List<AdMenu.Item>();
            items.Add(new AdMenu.Item { Text = L.Text("ad.ui.property.other"), Action = () =>
            {
                var property = Find(elementPath)?.FindPropertyRelative(nameof(MaterialPropertyTarget.property));
                string starting = property != null ? property.stringValue : "";
                PropertyNamePopup.Show(anchor, starting, name => SetPropertyOther(elementPath, name));
            } });
            var choices = PropertyPicker.Choices(renderer);
            bool manyChoices = choices.Count > 40;
            var groups = new List<AdMenu.Item>();
            foreach (var choice in choices)
            {
                string group = AlphabetGroup(choice.DisplayName, manyChoices);
                var branch = groups.Find(g => g.Text == group);
                if (branch == null) groups.Add(branch = new AdMenu.Item { Text = group, Children = new List<AdMenu.Item>() });
                var picked = choice;
                branch.Children.Add(new AdMenu.Item
                {
                    Text = choice.DisplayName,
                    SearchText = choice.PropertyName,
                    Note = choice.DisplayName == choice.PropertyName ? "" : choice.PropertyName,
                    Action = () => SetProperty(elementPath, picked),
                });
            }
            items.AddRange(groups);
            AdMenu.Open(anchor, anchorElement, items, search: true);
        }

        static string AlphabetGroup(string displayName, bool wideChunks)
        {
            char first = string.IsNullOrEmpty(displayName) ? '#' : char.ToUpperInvariant(displayName[0]);
            if (first < 'A' || first > 'Z') return "#";
            if (!wideChunks) return first.ToString();
            int index = first - 'A';
            char start = (char)('A' + index / 5 * 5);
            char end = (char)Mathf.Min('Z', start + 4);
            return start + "-" + end;
        }

        /// <summary>Applies a pick from the dropdown to the row at index, exactly as choosing it from the menu would. Public for tests and for driving the picker without opening the menu.</summary>
        public void PickProperty(int index, PropertyPicker.Choice choice)
        {
            if (Follow()) SetProperty(list.GetArrayElementAtIndex(index).propertyPath, choice);
        }

        /// <summary>Applies a hand typed property name to the row at index, exactly as committing the "Other..." popup would.</summary>
        public void PickPropertyOther(int index, string name)
        {
            if (Follow()) SetPropertyOther(list.GetArrayElementAtIndex(index).propertyPath, name);
        }

        /// <summary>Only a property or type change counts as picking something new: repicking the same property a row already has must not overwrite what the creator has edited since.</summary>
        void SetProperty(string elementPath, PropertyPicker.Choice choice)
        {
            if (Locked || !Begin()) return;
            var element = Find(elementPath);
            if (element == null) return;
            var renderer = element.FindPropertyRelative(nameof(MaterialPropertyTarget.renderer)).objectReferenceValue as Renderer;
            var propertyProperty = element.FindPropertyRelative(nameof(MaterialPropertyTarget.property));
            var typeProperty = element.FindPropertyRelative(nameof(MaterialPropertyTarget.type));
            bool changed = propertyProperty.stringValue != choice.PropertyName || (PropertyValueType)typeProperty.enumValueIndex != choice.Type;
            propertyProperty.stringValue = choice.PropertyName;
            typeProperty.enumValueIndex = (int)choice.Type;
            if (changed) CopyCurrentValue(element, renderer, choice.PropertyName, choice.Type);
            owner.ApplyAndRefresh();
        }

        /// <summary>
        /// A hand typed name resolves its type against the renderer's materials: from the shader when it lists the
        /// property, else from Material.HasFloat/HasColor/HasVector when the shader hides it from the inspector but
        /// a material still has it. When no material has the property at all, the row's own type and value are left
        /// alone; the model reports ad.warn.property_missing for that. The current value is copied under the same
        /// changed only rule SetProperty uses.
        /// </summary>
        void SetPropertyOther(string elementPath, string name)
        {
            if (Locked || !Begin()) return;
            var element = Find(elementPath);
            if (element == null) return;
            string trimmed = name ?? "";
            var renderer = element.FindPropertyRelative(nameof(MaterialPropertyTarget.renderer)).objectReferenceValue as Renderer;
            var propertyProperty = element.FindPropertyRelative(nameof(MaterialPropertyTarget.property));
            var typeProperty = element.FindPropertyRelative(nameof(MaterialPropertyTarget.type));
            bool nameChanged = propertyProperty.stringValue != trimmed;
            propertyProperty.stringValue = trimmed;

            var material = MenuModel.MaterialWithProperty(renderer, trimmed);
            if (material != null)
            {
                var currentType = (PropertyValueType)typeProperty.enumValueIndex;
                var resolvedType = ResolveType(renderer, material, trimmed, currentType);
                bool typeChanged = resolvedType != currentType;
                typeProperty.enumValueIndex = (int)resolvedType;
                if (nameChanged || typeChanged) CopyCurrentValue(element, renderer, trimmed, resolvedType);
            }
            owner.ApplyAndRefresh();
        }

        /// <summary>The property's type from the shader's own reflection, or from what the material itself reports when the shader hides the property from the inspector; falls back to the row's current type when neither knows it.</summary>
        static PropertyValueType ResolveType(Renderer renderer, Material material, string property, PropertyValueType fallback)
        {
            foreach (var choice in PropertyPicker.Choices(renderer))
                if (choice.PropertyName == property) return choice.Type;
            if (material.HasColor(property)) return PropertyValueType.Color;
            if (material.HasVector(property)) return PropertyValueType.Vector;
            if (material.HasFloat(property)) return PropertyValueType.Float;
            return fallback;
        }

        /// <summary>Copies the material's current value into the target's own value, so the first edit starts from what is on screen.</summary>
        static void CopyCurrentValue(SerializedProperty element, Renderer renderer, string property, PropertyValueType type)
        {
            var material = MenuModel.MaterialWithProperty(renderer, property);
            if (material == null) return;
            var packed = MenuModel.ReadProperty(material, property, type);
            switch (type)
            {
                case PropertyValueType.Color:
                    element.FindPropertyRelative(nameof(MaterialPropertyTarget.color)).colorValue = packed;
                    break;
                case PropertyValueType.Vector:
                    element.FindPropertyRelative(nameof(MaterialPropertyTarget.vector)).vector4Value = packed;
                    break;
                default:
                    element.FindPropertyRelative(nameof(MaterialPropertyTarget.value)).floatValue = packed.x;
                    break;
            }
        }

        /// <summary>
        /// Builds and binds a row through MakeRow/BindRow on demand, the same way the ListView would once it
        /// materialises it: an unattached ListView never lays out and virtualises its rows, so tests that need to
        /// read or drive a row's fields ask for it here instead of querying the (empty) ListView hierarchy.
        /// </summary>
        public VisualElement RowAt(int index)
        {
            var row = MakeRow();
            BindRow(row, index);
            return row;
        }

        /// <summary>Adds one default row of this section's own kind, exactly as its own "+ Add" button does.</summary>
        public void OnAdd(VisualElement anchor) => OnAdd(anchor.worldBound);

        /// <summary>The same, opening any picker at a rectangle read earlier. Public so the panel's shared "+ Add effect" button can add to a section that is not showing yet, from a menu item that runs after the button may be gone.</summary>
        public void OnAdd(Rect anchor)
        {
            switch (kind)
            {
                case Kind.Objects: AddObjects(new GameObject[] { null }); break;
                case Kind.BlendShapes: ShapePicker.Show(anchor, owner, owner.Plan.AvatarRoot, AddShape); break;
                case Kind.Materials: AddMaterialRow(); break;
                case Kind.Properties: AddProperty(null, ""); break;
                case Kind.Animations: AddAnimation(null); break;
                case Kind.Follows: AddFollow(); break;
            }
        }

        public void AddObjects(IEnumerable<GameObject> objects)
        {
            if (Locked || !Begin()) return;
            var present = new HashSet<Object>();
            for (int i = 0; i < list.arraySize; i++) present.Add(list.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(ObjectTarget.target)).objectReferenceValue);
            foreach (var go in objects)
            {
                if (go != null && present.Contains(go)) continue;
                if (go != null) present.Add(go);
                list.arraySize++;
                var element = list.GetArrayElementAtIndex(list.arraySize - 1);
                element.FindPropertyRelative(nameof(ObjectTarget.target)).objectReferenceValue = go;
                element.FindPropertyRelative(nameof(ObjectTarget.activeWhenOn)).boolValue = true;
            }
            owner.ApplyAndRefresh();
        }

        /// <summary>Adds a blend shape row. An override's row starts at 0, the value that switches the shape off; every other row starts at the full weight. Growing a serialized list copies the last row, so every field is set here and none is left from it.</summary>
        public void AddShape(SkinnedMeshRenderer renderer, string shape)
        {
            if (Locked || !Begin()) return;
            list.arraySize++;
            var element = list.GetArrayElementAtIndex(list.arraySize - 1);
            var fresh = new BlendShapeTarget();
            element.FindPropertyRelative(nameof(BlendShapeTarget.renderer)).objectReferenceValue = renderer;
            element.FindPropertyRelative(nameof(BlendShapeTarget.shape)).stringValue = shape;
            element.FindPropertyRelative(nameof(BlendShapeTarget.weight)).floatValue = pinned ? 0f : fresh.weight;
            element.FindPropertyRelative(nameof(BlendShapeTarget.minWeight)).floatValue = fresh.minWeight;
            element.FindPropertyRelative(nameof(BlendShapeTarget.maxWeight)).floatValue = fresh.maxWeight;
            element.FindPropertyRelative(nameof(BlendShapeTarget.multiplier)).floatValue = fresh.multiplier;
            element.FindPropertyRelative(nameof(BlendShapeTarget.mode)).enumValueIndex = (int)fresh.mode;
            element.FindPropertyRelative(nameof(BlendShapeTarget.explicitOff)).boolValue = fresh.explicitOff;
            owner.ApplyAndRefresh();
        }

        void SetShape(string path, SkinnedMeshRenderer renderer, string shape)
        {
            if (Locked || !Begin()) return;
            var element = Find(path);
            if (element == null) return;
            element.FindPropertyRelative(nameof(BlendShapeTarget.renderer)).objectReferenceValue = renderer;
            element.FindPropertyRelative(nameof(BlendShapeTarget.shape)).stringValue = shape;
            owner.ApplyAndRefresh();
        }

        /// <summary>Writes the fixed/pin mode of a plain toggle or condition-trigger blend shape row.</summary>
        public void SetShapeMode(int index, ShapeEffectMode mode)
        {
            if (Locked || !Begin()) return;
            var modeProperty = list.GetArrayElementAtIndex(index).FindPropertyRelative(nameof(BlendShapeTarget.mode));
            modeProperty.enumValueIndex = (int)mode;
            owner.ApplyAndRefresh();
        }

        /// <summary>
        /// Sets whether a toggle's blend shape writes an off value of its own instead of returning to the scene.
        /// Turning it on seeds the lower end field from the scene's current weight, so nothing changes visibly
        /// until the value is edited.
        /// </summary>
        public void SetShapeExplicitOff(int index, bool value)
        {
            if (Locked || !Begin()) return;
            var element = list.GetArrayElementAtIndex(index);
            element.FindPropertyRelative(nameof(BlendShapeTarget.explicitOff)).boolValue = value;
            rebuildRows = true;
            if (value)
            {
                var renderer = element.FindPropertyRelative(nameof(BlendShapeTarget.renderer)).objectReferenceValue as SkinnedMeshRenderer;
                var shape = element.FindPropertyRelative(nameof(BlendShapeTarget.shape)).stringValue;
                element.FindPropertyRelative(nameof(BlendShapeTarget.minWeight)).floatValue = SceneShapeWeight(renderer, shape);
            }
            owner.ApplyAndRefresh();
        }

        static float SceneShapeWeight(SkinnedMeshRenderer renderer, string shape)
        {
            if (renderer == null || renderer.sharedMesh == null) return 0f;
            int index = renderer.sharedMesh.GetBlendShapeIndex(shape);
            return index >= 0 ? renderer.GetBlendShapeWeight(index) : 0f;
        }

        /// <summary>Rebuilds the rule lines under a toggle's or a trigger's blendshape list from the entry's own data, and the capture status line every section that offers Capture carries.</summary>
        void RefreshRules()
        {
            var current = CapturedEntry() ?? captureEntry;
            if (rulesBox != null)
            {
                rulesBox.Clear();
                var context = owner.Plan?.Tree?.HeldContext ?? ShapeSetModel.ContextFor(owner.Plan?.AvatarRoot);
                for (int i = 0; i < current.shapeSets.Count; i++)
                {
                    var rule = current.shapeSets[i];
                    if (rule != null) rulesBox.Add(RuleLine(current, rule, i, context));
                }
            }
            shapeSetStatusBox.Clear();
            if (lastShapeSetStatus.Length > 0)
            {
                var status = UiText.MessageLabel(lastShapeSetStatus);
                status.name = "ad-shape-set-status";
                shapeSetStatusBox.Add(status);
            }
        }

        /// <summary>
        /// One rule's lines: its switch, named for the mesh, and the remove button, on a line that never wraps so a
        /// long name ends in an ellipsis instead of pushing the button down; under them Hold at with the Default or
        /// 0 choice, Hold blink for a mesh VRChat blinks with and Hold visemes for one it speaks with; then the count
        /// note.
        /// </summary>
        VisualElement RuleLine(MenuEntry entry, ShapeSetRule rule, int index, ShapeSetModel.HeldContext context)
        {
            var meshName = rule.mesh != null ? rule.mesh.name : "?";
            var container = new VisualElement { name = "ad-shape-set-row" };
            var head = new VisualElement { name = "ad-shape-set-head" };
            head.AddToClassList("ad-row");
            var on = new Toggle(string.Format(L.Text("ad.ui.shape_set.zero_others"), meshName)) { name = "zero-others", tooltip = string.Format(L.Text("ad.ui.shape_set.zero_others.tip"), meshName) };
            on.AddToClassList("ad-compact");
            on.AddToClassList("ad-shape-set-switch");
            on.SetValueWithoutNotify(rule.On);
            on.RegisterValueChangedCallback(e => SetZeroOthers(index, e.newValue));
            head.Add(on);
            var spacer = new VisualElement();
            spacer.AddToClassList("ad-spacer");
            head.Add(spacer);
            var remove = new Button { text = "×", name = "remove", tooltip = L.Text("ad.ui.shape_set.remove.tip") };
            remove.AddToClassList("ad-w-24");
            remove.clicked += () => RemoveRule(index);
            head.Add(remove);
            container.Add(head);

            var settings = new VisualElement { name = "ad-shape-set-settings" };
            settings.AddToClassList("ad-row");
            settings.AddToClassList("ad-target-wrap");
            var holdAt = new Label(L.Text("ad.ui.shape_set.hold_at"));
            holdAt.AddToClassList("ad-label");
            settings.Add(holdAt);
            var value = new SegmentedChoice(new[] { L.Text("ad.ui.shape_set.value.default"), L.Text("ad.ui.shape_set.value.zero") },
                rule.value == ShapeSetValue.Default ? 0 : 1,
                new[] { L.Text("ad.ui.shape_set.value.default.tip"), L.Text("ad.ui.shape_set.value.zero.tip") })
            {
                name = "ad-shape-set-value",
                tooltip = L.Text("ad.ui.shape_set.value.tip"),
            };
            value.AddToClassList("ad-shape-set-value");
            value.Changed += chosen => SetRuleValue(index, chosen == 0 ? ShapeSetValue.Default : ShapeSetValue.Zero);
            settings.Add(value);
            if (ShapeSetModel.MeshHasEyelidShapes(rule.mesh, context))
            {
                var blink = new Toggle(L.Text("ad.ui.shape_set.hold_blink")) { name = "hold-blink", tooltip = L.Text("ad.ui.shape_set.hold_blink.tip") };
                blink.AddToClassList("ad-compact");
                blink.SetValueWithoutNotify(rule.holdBlink);
                blink.RegisterValueChangedCallback(e => SetHoldBlink(index, e.newValue));
                settings.Add(blink);
            }
            if (ShapeSetModel.MeshHasVisemeShapes(rule.mesh, context))
            {
                var visemes = new Toggle(L.Text("ad.ui.shape_set.hold_visemes")) { name = "hold-visemes", tooltip = L.Text("ad.ui.shape_set.hold_visemes.tip") };
                visemes.AddToClassList("ad-compact");
                visemes.SetValueWithoutNotify(rule.holdVisemes);
                visemes.RegisterValueChangedCallback(e => SetHoldVisemes(index, e.newValue));
                settings.Add(visemes);
            }
            container.Add(settings);

            // The count the switch would hold if it were on, not what HeldShapes reports right now: an off rule still
            // has to say what it holds, or its own note would tell the author it does nothing. Up to that many: the
            // build writes only the ones something else moves.
            int count = ShapeSetModel.CoveredShapeCount(entry, rule, context);
            var note = new Label(rule.value == ShapeSetValue.Default
                ? UiText.Count(count, "ad.info.shape_set_count.default.one", "ad.info.shape_set_count.default")
                : UiText.Count(count, "ad.info.shape_set_count.one", "ad.info.shape_set_count")) { name = "ad-shape-set-note" };
            note.AddToClassList("ad-note");
            container.Add(note);
            return container;
        }

        /// <summary>
        /// Captures mesh's shapes above 0 into this section's rows: a radial takes the upper end, a toggle or a
        /// trigger condition takes Pin mode, a blend shape link takes Fixed mode, an override just takes the
        /// weight. A toggle's rule for the mesh is added at 0, or switched on when it has one; a trigger gets only the
        /// rows. Public for the Capture button and for tests.
        /// </summary>
        public void CaptureFrom(SkinnedMeshRenderer mesh)
        {
            var entry = CapturedEntry();
            if (mesh == null || entry == null) return;
            ShapeEffectMode? mode = radial || pinned ? (ShapeEffectMode?)null : (link ? ShapeEffectMode.Fixed : ShapeEffectMode.Pin);
            int count = EntryEditing.CaptureShapeSet(owner.Menu, entry, mesh, intoUpperEnd: radial, mode: mode, withRule: entry.kind == EntryKind.Toggle);
            lastShapeSetStatus = count == 0 ? string.Format(L.Text("ad.info.capture_nothing_above_zero"), mesh.name) : "";
            if (count == 0) { RefreshRules(); return; }
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
        }

        /// <summary>Adds a rule that holds every other shape of mesh at its default while the entry is on, unless the entry already has one for that mesh. Public for the Hold the rest button and for tests.</summary>
        public void HoldTheRest(SkinnedMeshRenderer mesh)
        {
            if (!EntryEditing.AddHoldRule(owner.Menu, CapturedEntry(), mesh)) return;
            AfterRuleWrite();
        }

        /// <summary>Switches a rule on or off. Public for the rule row's switch and for tests.</summary>
        public void SetZeroOthers(int ruleIndex, bool value)
        {
            if (!EntryEditing.SetShapeSetZeroOthers(owner.Menu, CapturedEntry(), ruleIndex, value)) return;
            AfterRuleWrite();
        }

        /// <summary>Chooses what a rule holds the other shapes at. Public for the rule row's Default or 0 choice and for tests.</summary>
        public void SetRuleValue(int ruleIndex, ShapeSetValue value)
        {
            if (!EntryEditing.SetShapeSetValue(owner.Menu, CapturedEntry(), ruleIndex, value)) return;
            AfterRuleWrite();
        }

        /// <summary>Chooses whether a rule holds the shapes VRChat blinks with too. Public for the rule row's Hold blink switch and for tests.</summary>
        public void SetHoldBlink(int ruleIndex, bool value)
        {
            if (!EntryEditing.SetShapeSetHoldBlink(owner.Menu, CapturedEntry(), ruleIndex, value)) return;
            AfterRuleWrite();
        }

        /// <summary>Chooses whether a rule holds the shapes VRChat speaks with too. Public for the rule row's Hold visemes switch and for tests.</summary>
        public void SetHoldVisemes(int ruleIndex, bool value)
        {
            if (!EntryEditing.SetShapeSetHoldVisemes(owner.Menu, CapturedEntry(), ruleIndex, value)) return;
            AfterRuleWrite();
        }

        void AfterRuleWrite()
        {
            AvatarPlanCache.NoteSelfEdit(owner.Menu);
            AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
            owner.Refresh();
        }

        /// <summary>Drops a rule; its captured rows stay. Public for the rule row's remove button and for tests.</summary>
        public void RemoveRule(int ruleIndex)
        {
            if (!EntryEditing.RemoveShapeSet(owner.Menu, CapturedEntry(), ruleIndex)) return;
            AfterRuleWrite();
        }

        public void AddMaterialRow()
        {
            if (Locked || !Begin()) return;
            list.arraySize++;
            var element = list.GetArrayElementAtIndex(list.arraySize - 1);
            element.FindPropertyRelative(nameof(MaterialTarget.renderer)).objectReferenceValue = null;
            element.FindPropertyRelative(nameof(MaterialTarget.slot)).intValue = 0;
            element.FindPropertyRelative(nameof(MaterialTarget.material)).objectReferenceValue = null;
            owner.ApplyAndRefresh();
        }

        /// <summary>Adds a property row. Picking a property this way (from the "+ Add" button, with no renderer chosen yet) leaves the value at its declared default; the dropdown itself copies the current value once a renderer and a property are chosen. Growing a serialized list copies the last row, so every field is set here and none is left from it.</summary>
        public void AddProperty(Renderer renderer, string property)
        {
            if (Locked || !Begin()) return;
            list.arraySize++;
            var element = list.GetArrayElementAtIndex(list.arraySize - 1);
            var fresh = new MaterialPropertyTarget();
            element.FindPropertyRelative(nameof(MaterialPropertyTarget.renderer)).objectReferenceValue = renderer;
            element.FindPropertyRelative(nameof(MaterialPropertyTarget.property)).stringValue = property ?? "";
            element.FindPropertyRelative(nameof(MaterialPropertyTarget.value)).floatValue = fresh.value;
            element.FindPropertyRelative(nameof(MaterialPropertyTarget.color)).colorValue = fresh.color;
            element.FindPropertyRelative(nameof(MaterialPropertyTarget.vector)).vector4Value = fresh.vector;
            element.FindPropertyRelative(nameof(MaterialPropertyTarget.minValue)).floatValue = fresh.minValue;
            element.FindPropertyRelative(nameof(MaterialPropertyTarget.minColor)).colorValue = fresh.minColor;
            element.FindPropertyRelative(nameof(MaterialPropertyTarget.minVector)).vector4Value = fresh.minVector;
            element.FindPropertyRelative(nameof(MaterialPropertyTarget.explicitOff)).boolValue = fresh.explicitOff;
            var match = PropertyPicker.Choices(renderer).FirstOrDefault(c => c.PropertyName == property);
            var type = !string.IsNullOrEmpty(property) && match.PropertyName == property ? match.Type : PropertyValueType.Float;
            element.FindPropertyRelative(nameof(MaterialPropertyTarget.type)).enumValueIndex = (int)type;
            CopyCurrentValue(element, renderer, property, type);
            owner.ApplyAndRefresh();
        }

        public void AddAnimation(AnimationClip clip)
        {
            if (Locked || !Begin()) return;
            list.arraySize++;
            var element = list.GetArrayElementAtIndex(list.arraySize - 1);
            element.FindPropertyRelative(nameof(AnimationTarget.clip)).objectReferenceValue = clip;
            element.FindPropertyRelative(nameof(AnimationTarget.pathRoot)).objectReferenceValue = null;
            owner.ApplyAndRefresh();
        }

        public void AddFollow()
        {
            if (Locked || !Begin()) return;
            list.arraySize++;
            var element = list.GetArrayElementAtIndex(list.arraySize - 1);
            element.FindPropertyRelative(nameof(FollowTarget.target)).objectReferenceValue = null;
            element.FindPropertyRelative(nameof(FollowTarget.follows)).objectReferenceValue = null;
            // Off by default: a new row snaps onto the target's own origin, which is what the grab case wants
            // out of the box.
            element.FindPropertyRelative(nameof(FollowTarget.keepOffset)).boolValue = false;
            owner.ApplyAndRefresh();
        }

        public void RemoveAt(int index)
        {
            if (Locked || !Begin()) return;
            if (index < 0 || index >= list.arraySize) return;
            var element = list.GetArrayElementAtIndex(index);
            // DeleteArrayElementAtIndex on an element that still holds an object reference only clears the
            // first one on the first call, so every reference field of the element must be nulled beforehand
            // or a row with more than one reference set takes several clicks to actually disappear.
            foreach (var fieldName in ReferenceFields())
            {
                var reference = element.FindPropertyRelative(fieldName);
                if (reference.objectReferenceValue != null) reference.objectReferenceValue = null;
            }
            list.DeleteArrayElementAtIndex(index);
            owner.ApplyAndRefresh();
        }

        string[] ReferenceFields()
        {
            switch (kind)
            {
                case Kind.Objects: return new[] { nameof(ObjectTarget.target) };
                case Kind.BlendShapes: return new[] { nameof(BlendShapeTarget.renderer) };
                case Kind.Materials: return new[] { nameof(MaterialTarget.renderer), nameof(MaterialTarget.material) };
                case Kind.Properties: return new[] { nameof(MaterialPropertyTarget.renderer) };
                case Kind.Follows: return new[] { nameof(FollowTarget.target), nameof(FollowTarget.follows) };
                default: return new[] { nameof(AnimationTarget.clip), nameof(AnimationTarget.pathRoot) };
            }
        }
    }
}
#endif
