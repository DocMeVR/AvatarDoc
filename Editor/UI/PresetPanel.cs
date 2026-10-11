#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The states and named parameters a preset sets, plus its apply-once preview row. Every list mutation goes
    /// through the entry's SerializedProperty and AvatarDoctorView.ApplyAndRefresh, then rebuilds this panel's own
    /// rows: EntryDetailView only rebuilds the whole detail body when the row count changes (see EntryDetailView.Shape),
    /// so a value-only edit (a different control picked, on/off, a slider) has to refresh itself here.
    /// </summary>
    public sealed class PresetPanel : VisualElement
    {
        readonly AvatarDoctorView owner;
        readonly TreeRow row;
        readonly string entryId;
        SerializedProperty statesProperty;
        SerializedProperty parametersProperty;

        public PresetPanel(EntryDetailView detail, AvatarDoctorView owner, TreeRow row, SerializedProperty property)
        {
            this.owner = owner;
            this.row = row;
            entryId = row.Entry.id;
            statesProperty = property.FindPropertyRelative(nameof(MenuEntry.presetStates));
            parametersProperty = property.FindPropertyRelative(nameof(MenuEntry.presetParameters));
            Rebuild();
        }

        /// <summary>
        /// Brings the serialized data up to date and takes both lists from the preset this panel was built for, wherever
        /// the component holds it now: the panel waits to rebuild while a field has the focus, and an Undo or a removal in
        /// front of the preset moves it to another place. False when the preset is gone, and then nothing may be written.
        /// </summary>
        bool Retake()
        {
            owner.SerializedObject.Update();
            var entry = owner.Menu.FindEntry(entryId);
            var property = entry == null ? null : owner.EntryProperty(entry);
            if (property == null) return false;
            statesProperty = property.FindPropertyRelative(nameof(MenuEntry.presetStates));
            parametersProperty = property.FindPropertyRelative(nameof(MenuEntry.presetParameters));
            return true;
        }

        void Rebuild()
        {
            Clear();

            Add(SectionHeader("ad.ui.preset.sets", "ad.ui.preset.sets.tip", "ad.ui.preset.add", "ad-preset-add-state", "ad.ui.preset.add.tip", () => AddPresetState("", true, 1f)));

            for (int i = 0; i < statesProperty.arraySize; i++) Add(StateRow(i));

            var fromPreview = new Button(FillPresetFromPreview) { text = L.Text("ad.ui.preset.from_preview"), tooltip = L.Text("ad.ui.preset.from_preview.tip"), name = "ad-preset-from-preview" };
            Add(fromPreview);

            Add(SectionHeader("ad.ui.preset.parameters", "ad.ui.preset.parameters.tip", "ad.ui.add", "ad-preset-add-parameter", "ad.ui.preset.parameters.add.tip", () => AddPresetParameter("", 1f)));

            for (int i = 0; i < parametersProperty.arraySize; i++) Add(ParameterRow(i));

            if (statesProperty.arraySize == 0 && parametersProperty.arraySize == 0)
            {
                var none = new Label(L.Text("ad.ui.preset.none"));
                none.AddToClassList("ad-note");
                Add(none);
            }

            var preview = new VisualElement();
            preview.AddToClassList("ad-preview");
            var buttons = new VisualElement();
            buttons.AddToClassList("ad-row");
            var label = new Label(L.Text("ad.ui.preview")) { tooltip = L.Text("ad.ui.preview.tip") };
            label.AddToClassList("ad-label");
            buttons.Add(label);
            // The same Default, On, Off control every other kind's preview uses: On applies the preset (what
            // "Apply" used to do), Default clears the overrides it names (what "Reset" used to do), Off forces
            // every one of them to its own off value instead.
            var choice = new SegmentedChoice(new List<string> { L.Text("ad.ui.preview.default"), L.Text("ad.ui.preview.on"), L.Text("ad.ui.preview.off") }) { name = "ad-preview-choice", tooltip = L.Text("ad.ui.preview.tip") };
            choice.Changed += index =>
            {
                if (index == 0) DefaultPreview();
                else if (index == 1) ApplyPreview();
                else OffPreview();
            };
            buttons.Add(choice);
            preview.Add(buttons);
            Add(preview);
        }

        /// <summary>One "+ Add" section header, shared by the sets list and the named-parameters list.</summary>
        static VisualElement SectionHeader(string titleKey, string titleTooltipKey, string addKey, string addName, string addTooltipKey, System.Action onAdd)
        {
            var header = new VisualElement();
            header.AddToClassList("ad-row");
            var title = new Label(L.Text(titleKey));
            if (titleTooltipKey != null) title.tooltip = L.Text(titleTooltipKey);
            title.AddToClassList("ad-label");
            title.AddToClassList("ad-section");
            header.Add(title);
            var spacer = new VisualElement();
            spacer.AddToClassList("ad-spacer");
            header.Add(spacer);
            var add = new Button(onAdd) { text = L.Text(addKey), name = addName, tooltip = addTooltipKey != null ? L.Text(addTooltipKey) : null };
            header.Add(add);
            return header;
        }

        /// <summary>
        /// Writes a structural change (control repicked, on or off, a row added or removed) already applied to the
        /// SerializedObject back to the entries array, then rebuilds this panel's own rows in place. While locked,
        /// the pending edit is discarded instead: every write here goes through the same SerializedObject as the
        /// rest of the panel, and nothing was committed to the entry until this ran.
        /// </summary>
        void Apply()
        {
            if (owner.Locked)
            {
                if (Retake()) Rebuild();
                return;
            }
            owner.ApplyAndRefresh();
            Rebuild();
        }

        /// <summary>
        /// Writes a continuous edit (a slider drag, a value field) without rebuilding this panel: rebuilding would
        /// replace the element under the pointer mid-drag, which drops pointer capture and kills the drag after its
        /// first move. EntryDetailView.Shape does not change from a value-only edit, so the deferred refresh this
        /// schedules will not rebuild the panel either. While locked, the pending edit is discarded instead of committed.
        /// </summary>
        void ApplyLive()
        {
            if (owner.Locked) { owner.SerializedObject.Update(); return; }
            owner.SerializedObject.ApplyModifiedProperties();
            owner.ScheduleRefresh();
        }

        VisualElement StateRow(int index)
        {
            var element = statesProperty.GetArrayElementAtIndex(index);
            var entryIdProperty = element.FindPropertyRelative(nameof(PresetState.entryId));
            var onProperty = element.FindPropertyRelative(nameof(PresetState.on));
            var valueProperty = element.FindPropertyRelative(nameof(PresetState.value));
            var control = owner.Plan.Tree.FindControl(entryIdProperty.stringValue);

            var stateRow = new VisualElement();
            stateRow.AddToClassList("ad-target");
            stateRow.AddToClassList("ad-preset-state");

            bool neverPicked = string.IsNullOrEmpty(entryIdProperty.stringValue);
            var pick = new Button
            {
                text = control != null ? control.Path : L.Text(neverPicked ? "ad.ui.preset.pick" : "ad.ui.preset.missing"),
                name = "ad-preset-control", tooltip = L.Text("ad.ui.preset.control.tip"),
            };
            pick.AddToClassList("ad-grow");
            pick.EnableInClassList("ad-text-warn", control == null && !neverPicked);
            pick.clicked += () => ShowControlPicker(pick, index, entryIdProperty.stringValue);
            stateRow.Add(pick);

            if (control != null && control.Entry.kind == EntryKind.Radial)
            {
                var slider = new Slider(0f, 1f) { showInputField = true, name = "ad-preset-value", tooltip = L.Text("ad.ui.preset.value.tip") };
                slider.SetValueWithoutNotify(valueProperty.floatValue);
                slider.RegisterValueChangedCallback(e => SetPresetStateValue(index, e.newValue));
                slider.AddToClassList("ad-grow");
                stateRow.Add(slider);
            }
            else
            {
                var choices = new List<string> { L.Text("ad.ui.preset.on"), L.Text("ad.ui.preset.off") };
                var onOff = new SegmentedChoice(choices, onProperty.boolValue ? 0 : 1) { name = "ad-preset-on", tooltip = L.Text("ad.ui.preset.on.tip") };
                onOff.AddToClassList("ad-w-70");
                onOff.Changed += newIndex => SetPresetStateOn(index, newIndex == 0);
                stateRow.Add(onOff);
            }

            var remove = new Button(() => RemovePresetState(index)) { text = "×", name = "remove" };
            remove.AddToClassList("ad-target-value");
            remove.AddToClassList("ad-w-24");
            stateRow.Add(remove);
            return stateRow;
        }

        /// <summary>Every toggle, radial and exclusive group member on the avatar, across every AvatarDoc, labelled by its menu path. Presets (including this one) never appear: a preset cannot target another preset.</summary>
        void ShowControlPicker(VisualElement anchor, int index, string current)
        {
            var items = new List<AdMenu.Item>();
            var choices = owner.Plan.Tree.Controls
                .Where(c => c.Entry.kind != EntryKind.Preset && c.Entry.id != row.Entry.id)
                .OrderBy(c => c.Path);
            foreach (var control in choices)
            {
                var id = control.Entry.id;
                items.Add(new AdMenu.Item { Text = control.Path, Checked = current == id, Action = () => SetPresetStateControl(index, id) });
            }
            if (items.Count == 0) items.Add(new AdMenu.Item { Text = L.Text("ad.ui.pick.none"), Disabled = true });
            AdMenu.Open(anchor.worldBound, anchor, items);
        }

        void SetPresetStateControl(int index, string controlId)
        {
            if (!Retake() || index < 0 || index >= statesProperty.arraySize) return;
            statesProperty.GetArrayElementAtIndex(index).FindPropertyRelative(nameof(PresetState.entryId)).stringValue = controlId ?? "";
            Apply();
        }

        void SetPresetStateOn(int index, bool on)
        {
            if (!Retake() || index < 0 || index >= statesProperty.arraySize) return;
            statesProperty.GetArrayElementAtIndex(index).FindPropertyRelative(nameof(PresetState.on)).boolValue = on;
            Apply();
        }

        void SetPresetStateValue(int index, float value)
        {
            if (!Retake() || index < 0 || index >= statesProperty.arraySize) return;
            statesProperty.GetArrayElementAtIndex(index).FindPropertyRelative(nameof(PresetState.value)).floatValue = Mathf.Clamp01(value);
            ApplyLive();
        }

        /// <summary>Adds one state at the end of the list. The panel's own "+ Add" button calls this with no control chosen yet.</summary>
        public void AddPresetState(string controlId, bool on, float value)
        {
            if (owner.Locked || !Retake()) return;
            statesProperty.arraySize++;
            var element = statesProperty.GetArrayElementAtIndex(statesProperty.arraySize - 1);
            element.FindPropertyRelative(nameof(PresetState.entryId)).stringValue = controlId ?? "";
            element.FindPropertyRelative(nameof(PresetState.on)).boolValue = on;
            element.FindPropertyRelative(nameof(PresetState.value)).floatValue = value;
            Apply();
        }

        public void RemovePresetState(int index)
        {
            if (owner.Locked || !Retake()) return;
            if (index < 0 || index >= statesProperty.arraySize) return;
            statesProperty.DeleteArrayElementAtIndex(index);
            Apply();
        }

        /// <summary>Adds or updates a state for every control that currently has a scene preview override, at the value it is previewing.</summary>
        public void FillPresetFromPreview()
        {
            if (owner.Locked || !Retake()) return;
            var tree = owner.Plan.Tree;
            foreach (var pair in PreviewState.Overrides.Value)
            {
                var control = tree.FindControl(pair.Key);
                if (control == null || control.Entry.kind == EntryKind.Preset || control.Entry.id == row.Entry.id) continue;
                int index = IndexOfState(pair.Key);
                if (index < 0)
                {
                    statesProperty.arraySize++;
                    index = statesProperty.arraySize - 1;
                    statesProperty.GetArrayElementAtIndex(index).FindPropertyRelative(nameof(PresetState.entryId)).stringValue = pair.Key;
                }
                var element = statesProperty.GetArrayElementAtIndex(index);
                if (control.Entry.kind == EntryKind.Radial) element.FindPropertyRelative(nameof(PresetState.value)).floatValue = Mathf.Clamp01(pair.Value);
                else element.FindPropertyRelative(nameof(PresetState.on)).boolValue = pair.Value >= 0.5f;
            }
            Apply();
        }

        int IndexOfState(string controlId)
        {
            for (int i = 0; i < statesProperty.arraySize; i++)
                if (statesProperty.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(PresetState.entryId)).stringValue == controlId) return i;
            return -1;
        }

        VisualElement ParameterRow(int index)
        {
            var element = parametersProperty.GetArrayElementAtIndex(index);
            var nameProperty = element.FindPropertyRelative(nameof(PresetParameter.name));
            var valueProperty = element.FindPropertyRelative(nameof(PresetParameter.value));

            var parameterRow = new VisualElement();
            parameterRow.AddToClassList("ad-target");
            parameterRow.AddToClassList("ad-preset-parameter");

            var nameField = new TextField { isDelayed = true, name = "ad-preset-param-name", tooltip = L.Text("ad.ui.preset.param_name.tip") };
            nameField.SetValueWithoutNotify(nameProperty.stringValue);
            nameField.RegisterValueChangedCallback(e => SetParameterName(index, e.newValue));
            nameField.AddToClassList("ad-grow");
            parameterRow.Add(nameField);

            var pick = new Button { name = "ad-preset-param-pick" };
            EntryDetailView.AddDropdownIcon(pick);
            pick.clicked += () => ParameterPicker.Show(pick, owner.Plan, name => SetParameterName(index, name));
            parameterRow.Add(pick);

            var valueField = new FloatField { isDelayed = true, name = "ad-preset-param-value", tooltip = L.Text("ad.ui.preset.param_value.tip") };
            valueField.SetValueWithoutNotify(valueProperty.floatValue);
            valueField.RegisterValueChangedCallback(e => SetParameterValue(index, e.newValue));
            valueField.AddToClassList("ad-w-70");
            parameterRow.Add(valueField);

            var remove = new Button(() => RemovePresetParameter(index)) { text = "×", name = "remove" };
            remove.AddToClassList("ad-target-value");
            remove.AddToClassList("ad-w-24");
            parameterRow.Add(remove);
            return parameterRow;
        }

        void SetParameterName(int index, string name)
        {
            if (!Retake() || index < 0 || index >= parametersProperty.arraySize) return;
            parametersProperty.GetArrayElementAtIndex(index).FindPropertyRelative(nameof(PresetParameter.name)).stringValue = name ?? "";
            Apply();
        }

        void SetParameterValue(int index, float value)
        {
            if (!Retake() || index < 0 || index >= parametersProperty.arraySize) return;
            parametersProperty.GetArrayElementAtIndex(index).FindPropertyRelative(nameof(PresetParameter.value)).floatValue = value;
            ApplyLive();
        }

        public void AddPresetParameter(string name, float value)
        {
            if (owner.Locked || !Retake()) return;
            parametersProperty.arraySize++;
            var element = parametersProperty.GetArrayElementAtIndex(parametersProperty.arraySize - 1);
            element.FindPropertyRelative(nameof(PresetParameter.name)).stringValue = name ?? "";
            element.FindPropertyRelative(nameof(PresetParameter.value)).floatValue = value;
            Apply();
        }

        public void RemovePresetParameter(int index)
        {
            if (owner.Locked || !Retake()) return;
            if (index < 0 || index >= parametersProperty.arraySize) return;
            parametersProperty.DeleteArrayElementAtIndex(index);
            Apply();
        }

        /// <summary>Applies every state and named parameter to the scene preview in one click, not a held state.</summary>
        public void ApplyPreview()
        {
            if (row.Entry == null) return;
            PreviewState.ApplyPreset(row.Entry, owner.Plan.Tree);
            owner.Header.RefreshPreviewIndicator();
        }

        /// <summary>Clears the preview overrides of the controls this preset names, leaving every other override untouched.</summary>
        public void DefaultPreview()
        {
            if (row.Entry == null) return;
            foreach (var state in row.Entry.presetStates) PreviewState.Clear(state.entryId);
            owner.Header.RefreshPreviewIndicator();
        }

        /// <summary>Forces every control this preset names to its own off value, the opposite of applying it.</summary>
        public void OffPreview()
        {
            if (row.Entry == null) return;
            foreach (var state in row.Entry.presetStates) PreviewState.Set(state.entryId, 0f);
            owner.Header.RefreshPreviewIndicator();
        }
    }
}
#endif
