#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>The copyable summary of one component: format choice, the read only text, Copy, and the Lock switch with its note.</summary>
    public sealed class SummaryPanel : VisualElement, IPlanRefreshable
    {
        readonly AvatarDoctorView owner;
        readonly DirectFields fields;
        readonly SegmentedChoice formatChoice;
        readonly TextField textField;
        readonly Label copyNote;
        SummaryFormat format = SummaryFormat.Plain;
        IVisualElementScheduledItem copyNoteClear;

        /// <summary>The built summary text, in the format currently chosen. Read by tests instead of the clipboard.</summary>
        public string Text => textField.value;

        /// <summary>Plain or Markdown, as the switch shows it.</summary>
        public SummaryFormat Format => format;

        public SummaryPanel(AvatarDoctorView owner)
        {
            this.owner = owner;
            fields = new DirectFields(owner.SerializedObject, owner.ApplyAndRefresh);
            AddToClassList("ad-panel");

            var title = new VisualElement();
            title.AddToClassList("ad-panel-title");
            title.Add(new Label(L.Text("ad.ui.summary")));
            Add(title);

            var body = new VisualElement();
            body.AddToClassList("ad-panel-body");
            Add(body);

            var formatRow = new VisualElement();
            formatRow.AddToClassList("ad-row");
            formatChoice = new SegmentedChoice(new List<string> { L.Text("ad.ui.summary.plain"), L.Text("ad.ui.summary.markdown") }) { name = "ad-summary-format", tooltip = L.Text("ad.ui.summary.format.tip") };
            formatChoice.Changed += index => { format = (SummaryFormat)index; RefreshText(); };
            formatRow.Add(formatChoice);
            body.Add(formatRow);

            textField = new TextField { multiline = true, isReadOnly = true, name = "ad-summary-text", tooltip = L.Text("ad.ui.summary.text.tip") };
            textField.AddToClassList("ad-summary-text");
            textField.AddToClassList("ad-mono");
            body.Add(textField);

            var copyRow = new VisualElement();
            copyRow.AddToClassList("ad-row");
            var copyButton = new Button(Copy) { text = L.Text("ad.ui.summary.copy"), name = "ad-summary-copy", tooltip = L.Text("ad.ui.summary.copy.tip") };
            copyButton.AddToClassList("ad-primary");
            copyRow.Add(copyButton);
            copyNote = new Label { name = "ad-summary-copy-note" };
            copyNote.AddToClassList("ad-note");
            copyRow.Add(copyNote);
            body.Add(copyRow);

            var lockRow = new VisualElement();
            lockRow.AddToClassList("ad-row");
            var lockToggle = fields.Toggle(owner.SerializedObject.FindProperty(nameof(AvatarDoc.locked)), L.Text("ad.ui.lock"));
            lockToggle.name = "ad-lock-toggle";
            lockToggle.tooltip = L.Text("ad.ui.lock.tip");
            lockRow.Add(lockToggle);
            body.Add(lockRow);

            var noteField = fields.Text(owner.SerializedObject.FindProperty(nameof(AvatarDoc.lockNote)), L.Text("ad.ui.lock.note"), owner.ApplyCosmetic);
            noteField.name = "ad-lock-note";
            noteField.tooltip = L.Text("ad.ui.lock.note.tip");
            var noteRow = new VisualElement();
            noteRow.AddToClassList("ad-row");
            noteRow.Add(noteField);
            body.Add(noteRow);
        }

        /// <summary>Sets the shown format, as the header's SegmentedChoice would, for tests and for a saved choice.</summary>
        public void SetFormat(SummaryFormat value)
        {
            format = value;
            formatChoice.SetIndexWithoutNotify((int)value);
            RefreshText();
        }

        void Copy()
        {
            EditorGUIUtility.systemCopyBuffer = textField.value;
            copyNote.text = L.Text("ad.ui.summary.copied");
            copyNoteClear?.Pause();
            copyNoteClear = schedule.Execute(() => copyNote.text = "").StartingIn(1500);
        }

        /// <summary>
        /// Reloads the lock fields always: they show on this panel too. The summary text itself, a full read of
        /// the plan's parameters, menu, shape sets, animators and messages, is only ever built while this panel is
        /// the one showing: rebuilding it on every refresh of a hidden panel (the common case, since the detail
        /// panel is the default view) paid for a summary nobody was reading. OpenSummary and SetFormat both build
        /// it themselves the moment it actually becomes visible, so nothing here goes stale.
        /// </summary>
        public void Refresh(AvatarPlan plan)
        {
            fields.Reload();
            if (owner.SummaryOpen) RefreshText();
        }

        void RefreshText()
        {
            if (owner.Plan == null || owner.Menu == null) return;
            textField.SetValueWithoutNotify(SummaryBuilder.Build(owner.Plan, owner.Menu, format));
        }
    }
}
#endif
