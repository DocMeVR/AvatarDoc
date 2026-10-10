#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The "When" section of a trigger's panel: the rule in words, then one card per group, captioned "Any of
    /// these" between them and "All of" inside, with a compact row per condition. A trigger that still works from
    /// its single source shows no cards, only the sentence and the two buttons that give it its first group.
    /// A blend shape trigger always shows one card, its shape first and its requirements after it; it and a Fade
    /// contact offer "+ And" for requirements and no "+ Or", since one driver moves their effects.
    /// </summary>
    public sealed class ConditionGroupsPanel : VisualElement
    {
        readonly EntryDetailView detail;
        readonly AvatarDoctorView owner;

        /// <param name="driverRow">A blend shape trigger's shape row, shown first in its card. Null for every other trigger, whose driver, if any, is a condition row of its own.</param>
        public ConditionGroupsPanel(EntryDetailView detail, AvatarDoctorView owner, TreeRow row, SerializedProperty property, VisualElement driverRow = null)
        {
            this.detail = detail;
            this.owner = owner;
            var entry = row.Entry;
            bool driven = entry.HasDriver;
            name = "ad-when-groups";

            var heading = new VisualElement();
            heading.AddToClassList("ad-row");
            var title = new Label(L.Text("ad.ui.fires")) { name = "ad-when-title", tooltip = L.Text("ad.ui.fires.tip") };
            title.AddToClassList("ad-label");
            heading.Add(title);
            var sentence = new Label(ConditionLogic.Sentence(entry, owner.Plan.Tree)) { name = "ad-when-sentence" };
            sentence.AddToClassList("ad-note");
            sentence.AddToClassList("ad-grow");
            heading.Add(sentence);
            Add(heading);

            if (driverRow != null)
            {
                // The shape is the card's first row; its requirement group, when it has one, follows. Any other
                // card left over from another time is shown too, so it can be seen and removed.
                var groups = property.FindPropertyRelative(nameof(MenuEntry.when));
                int requirementGroup = entry.RequirementGroupIndex;
                Add(DriverCard(row, entry, driverRow, requirementGroup >= 0 && requirementGroup < groups.arraySize ? groups.GetArrayElementAtIndex(requirementGroup) : null, requirementGroup));
                for (int g = 0; g < entry.when.Count && g < groups.arraySize; g++)
                {
                    if (g == requirementGroup || entry.when[g] == null || entry.when[g].all == null || entry.when[g].all.All(c => c == null)) continue;
                    Add(Caption("ad.ui.when.ignored", "ad-when-ignored"));
                    Add(Card(row, entry, groups.GetArrayElementAtIndex(g), g));
                }
            }
            else if (entry.UsesWhenGroups)
            {
                var groups = property.FindPropertyRelative(nameof(MenuEntry.when));
                for (int g = 0; g < entry.when.Count && g < groups.arraySize; g++)
                {
                    if (g > 0) Add(Caption("ad.ui.when.any_of", "ad-when-any"));
                    Add(Card(row, entry, groups.GetArrayElementAtIndex(g), g));
                }
                if (!driven && ConditionLogic.TooComplex(entry.EffectiveWhen, out int groupCount, out int leaveCount))
                    Add(UiText.MessageLabel(string.Format(L.Text("ad.error.trigger_too_complex"), entry.PlainName, groupCount, leaveCount)));
            }

            // One driver moves a blend shape trigger's or a Fade contact's effects, so "+ And" adds a requirement
            // to the card the driver is in, and there is no "+ Or": a second way to be met would need a second driver.
            if (driven)
            {
                // Inside the card the driver sits in, once there is one; a Fade contact that still works from its
                // flat fields has no card yet, so its "+ And" sits here and gives it one.
                if (driverRow == null && entry.RequirementGroupIndex < 0)
                {
                    var buttons = new VisualElement();
                    buttons.AddToClassList("ad-row");
                    buttons.Add(RequirementButton(entry));
                    Add(buttons);
                }

                var note = new Label(L.Text("ad.ui.when.requirements_note"));
                note.AddToClassList("ad-note");
                Add(note);
            }
            else
            {
                var buttons = new VisualElement();
                buttons.AddToClassList("ad-row");
                var addAnd = new Button(() => detail.AddCondition(entry.UsesWhenGroups ? entry.when.Count - 1 : 0)) { text = L.Text("ad.ui.when.add_and"), name = "ad-when-add-and", tooltip = L.Text("ad.ui.when.add_and.tip") };
                if (entry.UsesWhenGroups) LimitAddAnd(addAnd, entry.when[entry.when.Count - 1]);
                var addOr = new Button(detail.AddConditionGroup) { text = L.Text("ad.ui.when.add_or"), name = "ad-when-add-or", tooltip = L.Text("ad.ui.when.add_or.tip") };
                buttons.Add(addAnd);
                buttons.Add(addOr);
                Add(buttons);

                var note = new Label(L.Text("ad.ui.when.note"));
                note.AddToClassList("ad-note");
                Add(note);
            }
        }

        VisualElement Card(TreeRow row, MenuEntry entry, SerializedProperty groupProperty, int groupIndex)
        {
            var card = new VisualElement { name = "ad-when-card" };
            card.AddToClassList("ad-when-card");
            var header = new VisualElement();
            header.AddToClassList("ad-row");
            var caption = new Label(L.Text("ad.ui.when.all_of")) { tooltip = L.Text("ad.ui.when.all_of.tip") };
            caption.AddToClassList("ad-label");
            header.Add(caption);
            var spacer = new VisualElement();
            spacer.AddToClassList("ad-spacer");
            header.Add(spacer);
            var removeGroup = new Button(() => detail.RemoveConditionGroup(groupIndex)) { text = "x", name = "ad-when-remove-group", tooltip = L.Text("ad.ui.when.remove_group") };
            header.Add(removeGroup);
            card.Add(header);

            var group = entry.when[groupIndex];
            var all = groupProperty.FindPropertyRelative(nameof(ConditionGroup.all));
            for (int i = 0; i < group.all.Count && i < all.arraySize; i++)
            {
                var condition = group.all[i];
                if (condition == null) continue;
                card.Add(ConditionRow(row, entry, condition, all.GetArrayElementAtIndex(i), groupIndex, i));
            }

            if (!entry.HasDriver)
            {
                var add = new Button(() => detail.AddCondition(groupIndex)) { text = L.Text("ad.ui.when.add_and"), name = "ad-when-add-and-" + groupIndex, tooltip = L.Text("ad.ui.when.add_and.tip") };
                LimitAddAnd(add, group);
                card.Add(add);
            }
            else if (groupIndex == entry.RequirementGroupIndex) card.Add(RequirementButton(entry));
            return card;
        }

        /// <summary>
        /// "+ And" on a blend shape trigger or a Fade contact: adds a requirement to the card its driver is in. The
        /// card holds the driver too (the shape row, or the first contact), so it is full one requirement sooner
        /// than a plain group, and the switched off button's tooltip says so.
        /// </summary>
        Button RequirementButton(MenuEntry entry)
        {
            int requirementGroup = entry.RequirementGroupIndex;
            var add = new Button(() => detail.AddCondition(requirementGroup >= 0 ? requirementGroup : 0)) { text = L.Text("ad.ui.when.add_and"), name = "ad-when-add-and", tooltip = L.Text("ad.ui.when.add_requirement.tip") };
            if (entry.Requirements().Count() + 1 >= ConditionLogic.MaxPerGroup)
            {
                add.SetEnabled(false);
                add.tooltip = string.Format(L.Text("ad.ui.when.add_and.limit_driver.tip"), ConditionLogic.MaxPerGroup);
            }
            return add;
        }

        /// <summary>A blend shape trigger's one card: its shape row first, then a row per requirement. No remove button of its own, since the shape is not a condition that can go.</summary>
        VisualElement DriverCard(TreeRow row, MenuEntry entry, VisualElement driverRow, SerializedProperty groupProperty, int groupIndex)
        {
            var card = new VisualElement { name = "ad-when-card" };
            card.AddToClassList("ad-when-card");
            var header = new VisualElement();
            header.AddToClassList("ad-row");
            var caption = new Label(L.Text("ad.ui.when.all_of")) { tooltip = L.Text("ad.ui.when.all_of.tip") };
            caption.AddToClassList("ad-label");
            header.Add(caption);
            card.Add(header);
            card.Add(driverRow);
            if (groupProperty != null && groupIndex >= 0)
            {
                var group = entry.when[groupIndex];
                var all = groupProperty.FindPropertyRelative(nameof(ConditionGroup.all));
                for (int i = 0; i < group.all.Count && i < all.arraySize; i++)
                {
                    var condition = group.all[i];
                    if (condition == null) continue;
                    card.Add(ConditionRow(row, entry, condition, all.GetArrayElementAtIndex(i), groupIndex, i));
                }
            }
            card.Add(RequirementButton(entry));
            return card;
        }

        /// <summary>A group holds at most ConditionLogic.MaxPerGroup conditions: past that the rule cannot be built, so the button that would add another is switched off and its tooltip says why.</summary>
        static void LimitAddAnd(Button button, ConditionGroup group)
        {
            if (group == null || group.all == null || group.all.Count < ConditionLogic.MaxPerGroup) return;
            button.SetEnabled(false);
            button.tooltip = string.Format(L.Text("ad.ui.when.add_and.limit.tip"), ConditionLogic.MaxPerGroup);
        }

        VisualElement ConditionRow(TreeRow row, MenuEntry entry, WhenCondition condition, SerializedProperty conditionProperty, int groupIndex, int index)
        {
            var container = new VisualElement { name = "ad-when-row" };
            container.AddToClassList("ad-when-row");

            var head = new VisualElement();
            head.AddToClassList("ad-row");
            var kinds = new List<string>
            {
                L.Text("ad.ui.when.kind.toggle"), L.Text("ad.ui.when.kind.parameter"),
                L.Text("ad.ui.when.kind.contact"), L.Text("ad.ui.when.kind.physbone"),
            };
            var kindField = new DropdownField(kinds, (int)condition.kind) { name = "ad-when-kind", tooltip = L.Text("ad.ui.when.kind.tip") };
            kindField.AddToClassList("ad-grow");
            kindField.RegisterValueChangedCallback(e =>
            {
                int i = kinds.IndexOf(e.newValue);
                if (i >= 0) detail.SetConditionKind(groupIndex, index, (WhenConditionKind)i);
            });
            head.Add(kindField);
            var remove = new Button(() => detail.RemoveCondition(groupIndex, index)) { text = "x", name = "ad-when-remove", tooltip = L.Text("ad.ui.when.remove") };
            head.Add(remove);
            container.Add(head);

            switch (condition.kind)
            {
                case WhenConditionKind.Toggle:
                    container.Add(ToggleRow(entry, condition, conditionProperty, groupIndex, index));
                    break;
                case WhenConditionKind.Parameter:
                    container.Add(ParameterRows(condition, conditionProperty));
                    break;
                default:
                    container.Add(TouchRows(row, condition, conditionProperty, groupIndex, index));
                    break;
            }
            return container;
        }

        VisualElement ToggleRow(MenuEntry entry, WhenCondition condition, SerializedProperty conditionProperty, int groupIndex, int index)
        {
            var container = new VisualElement();
            var toggles = owner.Plan.Tree.Controls.Where(c => c.Entry.kind == EntryKind.Toggle && c.Entry.id != entry.id).ToList();
            var labels = new List<string> { L.Text("ad.ui.when.pick_toggle") };
            labels.AddRange(toggles.Select(c => c.Component == owner.Menu ? c.Entry.PlainName : L.Aside(c.Entry.PlainName, c.Component.gameObject.name)));
            int current = toggles.FindIndex(c => c.Entry.id == condition.sourceToggleId);
            var field = new DropdownField(L.Text("ad.ui.trigger.toggle"), labels, current + 1) { name = "ad-when-toggle", tooltip = L.Text("ad.ui.trigger.toggle.tip") };
            field.RegisterValueChangedCallback(e =>
            {
                int i = labels.IndexOf(e.newValue) - 1;
                detail.SetConditionToggle(conditionProperty, i >= 0 ? toggles[i].Entry.id : "");
            });
            container.Add(Row(field));

            var states = new List<string> { L.Text("ad.ui.trigger.toggle.on"), L.Text("ad.ui.trigger.toggle.off") };
            var state = new SegmentedChoice(states, condition.sourceToggleOn ? 0 : 1) { name = "ad-when-toggle-state", tooltip = L.Text("ad.ui.trigger.toggle_state.tip") };
            var stateProperty = conditionProperty.FindPropertyRelative(nameof(WhenCondition.sourceToggleOn));
            state.Changed += i => detail.SetConditionBool(stateProperty, i == 0);
            container.Add(Row(state));
            return container;
        }

        VisualElement ParameterRows(WhenCondition condition, SerializedProperty conditionProperty)
        {
            var container = new VisualElement();
            var line = new VisualElement();
            line.AddToClassList("ad-row");
            var nameField = detail.Fields.Text(conditionProperty.FindPropertyRelative(nameof(WhenCondition.sourceParameter)), L.Text("ad.ui.trigger.parameter"));
            nameField.name = "ad-when-parameter";
            nameField.tooltip = L.Text("ad.ui.trigger.parameter.tip");
            line.Add(nameField);
            var pick = new Button();
            EntryDetailView.AddDropdownIcon(pick);
            pick.clicked += () => ParameterPicker.Show(pick.worldBound, owner.Plan, picked => detail.SetConditionParameter(conditionProperty, picked));
            line.Add(pick);
            container.Add(line);

            var comparison = detail.Fields.EnumChoice(conditionProperty.FindPropertyRelative(nameof(WhenCondition.condition)), L.Text("ad.ui.trigger.condition"));
            comparison.name = "ad-when-comparison";
            comparison.tooltip = L.Text("ad.ui.trigger.condition.tip");
            container.Add(Row(comparison));

            bool needsValue = condition.condition != ParameterCondition.IsTrue && condition.condition != ParameterCondition.IsFalse;
            var value = detail.Fields.Float(conditionProperty.FindPropertyRelative(nameof(WhenCondition.conditionValue)), L.Text("ad.ui.trigger.value"));
            value.name = "ad-when-value";
            value.tooltip = L.Text("ad.ui.trigger.value.tip");
            var valueRow = Row(value);
            valueRow.EnableInClassList("ad-hidden", !needsValue);
            container.Add(valueRow);
            return container;
        }

        VisualElement TouchRows(TreeRow row, WhenCondition condition, SerializedProperty conditionProperty, int groupIndex, int index)
        {
            var container = new VisualElement();
            if (detail.IsSelectedCondition(groupIndex, index))
            {
                container.Add(new TouchPanel(detail, owner, row, conditionProperty, condition, withMode: false));
                return container;
            }
            var line = new VisualElement();
            line.AddToClassList("ad-row");
            var label = new Label(ConditionLogic.Words(condition, owner.Plan.Tree));
            label.AddToClassList("ad-label");
            label.AddToClassList("ad-grow");
            line.Add(label);
            var open = new Button(() => detail.SelectCondition(groupIndex, index)) { text = L.Text("ad.ui.when.settings"), name = "ad-when-settings", tooltip = L.Text("ad.ui.when.settings.tip") };
            line.Add(open);
            container.Add(line);
            return container;
        }

        static VisualElement Caption(string key, string elementName)
        {
            var label = new Label(L.Text(key)) { name = elementName, tooltip = L.Text(key + ".tip") };
            label.AddToClassList("ad-when-any");
            return label;
        }

        static VisualElement Row(VisualElement field)
        {
            var row = new VisualElement();
            row.AddToClassList("ad-row");
            row.Add(field);
            return row;
        }
    }
}
#endif
