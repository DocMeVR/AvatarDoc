#if DOCME_NDMF && DOCME_VRCSDK3A
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The whole grabbable arrangement in one short panel: the object, which hand or hands, keep offset, a one
    /// line summary of what the build makes, and a fold for the rest (anyone may grab, return time, the menu
    /// toggle). Every field writes straight through EntryDetailView's DirectFields, the same as every other panel.
    /// </summary>
    public sealed class GrabPanel : VisualElement
    {
        public GrabPanel(EntryDetailView detail, AvatarDoctorView owner, SerializedProperty property)
        {
            var fields = detail.Fields;

            var objectField = fields.ObjectOnAvatar<GameObject>(property.FindPropertyRelative(nameof(MenuEntry.grabObject)), L.Text("ad.ui.grab.object"), () => owner.Plan?.AvatarRoot, PickKind.GameObject, owner.ApplyAndRefresh);
            objectField.name = "ad-grab-object";
            objectField.tooltip = L.Text("ad.ui.grab.object.tip");
            Add(Row(objectField));

            var handsLabels = new[] { L.Text("ad.ui.grab.hands.both"), L.Text("ad.ui.grab.hands.left"), L.Text("ad.ui.grab.hands.right") };
            var hands = fields.EnumSegments(property.FindPropertyRelative(nameof(MenuEntry.grabHands)), handsLabels, owner.ApplyAndRefresh);
            hands.name = "ad-grab-hands";
            hands.tooltip = L.Text("ad.ui.grab.hands.tip");
            Add(Labelled("ad.ui.grab.hands", hands));

            // A real labelled row, not a self-captioned Toggle: every other field in this panel keeps its own
            // caption in the label column and its own control at the field column, and a standalone boolean now
            // has to line up in that same column too.
            var keepOffsetRow = new VisualElement();
            keepOffsetRow.AddToClassList("ad-row");
            var keepOffsetLabel = new Label(L.Text("ad.ui.target.keep_offset"));
            keepOffsetLabel.AddToClassList("ad-label");
            keepOffsetRow.Add(keepOffsetLabel);
            var keepOffset = fields.Toggle(property.FindPropertyRelative(nameof(MenuEntry.grabKeepOffset)), "", owner.ApplyAndRefresh);
            keepOffset.name = "ad-grab-keep-offset";
            keepOffset.tooltip = L.Text("ad.ui.target.keep_offset.tip");
            keepOffsetRow.Add(keepOffset);
            Add(keepOffsetRow);

            var summary = new Label(L.Text("ad.ui.grab.summary"));
            summary.AddToClassList("ad-note");
            Add(summary);

            var fold = new Foldout { name = "ad-grab-fold-toggle", text = L.Text("ad.ui.grab.fold"), tooltip = L.Text("ad.ui.grab.fold.tip") };
            fold.AddToClassList("ad-fold");
            var foldToggle = fold.Q<Toggle>();
            if (foldToggle != null) foldToggle.tooltip = L.Text("ad.ui.grab.fold.tip");
            fold.contentContainer.name = "ad-grab-fold";
            fold.SetValueWithoutNotify(false);

            var anyone = fields.Toggle(property.FindPropertyRelative(nameof(MenuEntry.grabAnyone)), L.Text("ad.ui.grab.anyone"), owner.ApplyAndRefresh);
            anyone.name = "ad-grab-anyone";
            anyone.tooltip = L.Text("ad.ui.grab.anyone.tip");
            fold.Add(Row(anyone));

            var returnSeconds = fields.FloatClamped(property.FindPropertyRelative(nameof(MenuEntry.grabReturnSeconds)), L.Text("ad.ui.grab.return_seconds"), 0f, 10f, owner.ApplyLive);
            returnSeconds.name = "ad-grab-return-seconds";
            returnSeconds.tooltip = L.Text("ad.ui.grab.return_seconds.tip");
            fold.Add(Row(returnSeconds));

            var reach = fields.FloatClamped(property.FindPropertyRelative(nameof(MenuEntry.grabReach)), L.Text("ad.ui.grab.reach"), 0.1f, 5f, owner.ApplyLive);
            reach.name = "ad-grab-reach";
            reach.tooltip = L.Text("ad.ui.grab.reach.tip");
            fold.Add(Row(reach));

            var menuToggle = fields.Toggle(property.FindPropertyRelative(nameof(MenuEntry.grabMenuToggle)), L.Text("ad.ui.grab.menu_toggle"), owner.ApplyAndRefresh);
            menuToggle.name = "ad-grab-menu-toggle";
            menuToggle.tooltip = L.Text("ad.ui.grab.menu_toggle.tip");
            fold.Add(Row(menuToggle));

            Add(fold);
        }

        static VisualElement Row(VisualElement field)
        {
            var row = new VisualElement();
            row.AddToClassList("ad-row");
            row.Add(field);
            return row;
        }

        static VisualElement Labelled(string labelKey, VisualElement field)
        {
            var row = new VisualElement();
            row.AddToClassList("ad-row");
            var label = new Label(L.Text(labelKey));
            label.AddToClassList("ad-label");
            row.Add(label);
            row.Add(field);
            return row;
        }
    }
}
#endif
