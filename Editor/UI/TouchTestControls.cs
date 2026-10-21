#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Test with a hand, or Stop while the hand runs on this view's avatar. It follows TouchTest while it is on a panel,
    /// so every button of every open view shows the same state.
    /// </summary>
    public sealed class TouchTestButton : Button
    {
        readonly AvatarDoctorView owner;
        readonly Func<MenuEntry> focus;
        readonly Func<WhenCondition> at;

        /// <param name="focus">The trigger whose contact the hand starts beside, or null to start it in front of the avatar's head.</param>
        /// <param name="at">The condition of that trigger the panel has selected, which wins when it is a contact.</param>
        public TouchTestButton(AvatarDoctorView owner, Func<MenuEntry> focus = null, Func<WhenCondition> at = null)
        {
            this.owner = owner;
            this.focus = focus;
            this.at = at;
            AddToClassList("ad-field-button");
            AddToClassList("ad-touch-test-button");
            clicked += Press;
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                TouchTest.Changed -= Sync;
                TouchTest.Changed += Sync;
                Sync();
            });
            RegisterCallback<DetachFromPanelEvent>(_ => TouchTest.Changed -= Sync);
            Sync();
        }

        bool RunsHere => TouchTest.RunsOn(owner.Plan?.AvatarRoot);

        void Press()
        {
            if (RunsHere) TouchTest.Stop();
            else TouchTest.Start(owner.Menu, focus?.Invoke(), at?.Invoke());
            Sync();
        }

        public void Sync()
        {
            bool running = RunsHere;
            text = L.Text(running ? "ad.ui.touch_test.stop" : "ad.ui.touch_test.start");
            tooltip = L.Text(running ? "ad.ui.touch_test.stop.tip" : "ad.ui.touch_test.start.tip");
            EnableInClassList("ad-primary", running);
        }
    }

    /// <summary>The rows that start the test hand and, in the header, set what it stands for while it runs.</summary>
    public static class TouchTestControls
    {
        /// <summary>The caption and the button, as one row of the field column.</summary>
        public static VisualElement Row(AvatarDoctorView owner, string rowName, string buttonName, Func<MenuEntry> focus = null, Func<WhenCondition> at = null)
        {
            var row = new VisualElement { name = rowName };
            row.AddToClassList("ad-row");
            var label = new Label(L.Text("ad.ui.touch_test.row")) { tooltip = L.Text("ad.ui.touch_test.start.tip") };
            label.AddToClassList("ad-label");
            row.Add(label);
            row.Add(new TouchTestButton(owner, focus, at) { name = buttonName });
            return row;
        }

        /// <summary>The body parts the hand can stand for, in TouchTestPart order.</summary>
        public static List<string> PartLabels => new List<string>
        {
            L.Text("ad.ui.touch_test.part.hand"), L.Text("ad.ui.touch_test.part.head"), L.Text("ad.ui.touch_test.part.torso"),
            L.Text("ad.ui.touch_test.part.foot"), L.Text("ad.ui.touch_test.part.custom"),
        };
    }

    /// <summary>
    /// The header's part of the test hand: its row, shown while the avatar has a contact touch trigger or the hand runs
    /// on it, and while it runs, what it stands for (Counts as, a custom tag) and whose it is (Belongs to).
    /// </summary>
    public sealed class TouchTestBlock : VisualElement
    {
        readonly AvatarDoctorView owner;
        readonly VisualElement settings = new VisualElement { name = "ad-touch-test-settings" };
        readonly Button part;
        readonly Label partText = new Label { name = "ad-touch-test-part-label", pickingMode = PickingMode.Ignore };
        readonly VisualElement tagRow;
        readonly TextField tag;
        readonly SegmentedChoice who;
        bool hasTriggers;

        public TouchTestBlock(AvatarDoctorView owner)
        {
            this.owner = owner;
            name = "ad-touch-test-block";
            AddToClassList("ad-touch-test-block");
            Add(TouchTestControls.Row(owner, "ad-touch-test-row", "ad-touch-test"));

            // A button that opens a small menu, not a DropdownField: Unity's popup field grows to 24 px with the
            // font NDMF gives Chinese, and the rows here keep the 22 px of the rows around them.
            var partRow = new VisualElement { name = "ad-touch-test-part-row" };
            partRow.AddToClassList("ad-row");
            var partLabel = new Label(L.Text("ad.ui.touch_test.part")) { tooltip = L.Text("ad.ui.touch_test.part.tip") };
            partLabel.AddToClassList("ad-label");
            partRow.Add(partLabel);
            part = new Button(OpenParts) { name = "ad-touch-test-part", tooltip = L.Text("ad.ui.touch_test.part.tip") };
            part.AddToClassList("ad-field-button");
            part.AddToClassList("ad-touch-test-button");
            part.AddToClassList("ad-touch-test-part");
            partText.text = TouchTestControls.PartLabels[0];
            part.Add(partText);
            MenuTreeView.AddDropdownArrow(part);
            partRow.Add(part);
            settings.Add(partRow);

            tag = new TextField(L.Text("ad.ui.touch_test.tag")) { name = "ad-touch-test-tag", isDelayed = true, tooltip = L.Text("ad.ui.touch_test.tag.tip") };
            tag.RegisterValueChangedCallback(e => TouchTest.SetCustomTag(e.newValue));
            tagRow = InRow(tag);
            settings.Add(tagRow);

            var whoRow = new VisualElement { name = "ad-touch-test-who-row" };
            whoRow.AddToClassList("ad-row");
            var whoLabel = new Label(L.Text("ad.ui.touch_test.who")) { tooltip = L.Text("ad.ui.touch_test.who.tip") };
            whoLabel.AddToClassList("ad-label");
            whoRow.Add(whoLabel);
            who = new SegmentedChoice(new List<string> { L.Text("ad.ui.touch_test.who.others"), L.Text("ad.ui.touch_test.who.self") }, 0,
                new List<string> { L.Text("ad.ui.touch_test.who.others.tip"), L.Text("ad.ui.touch_test.who.self.tip") })
                { name = "ad-touch-test-who", tooltip = L.Text("ad.ui.touch_test.who.tip") };
            who.AddToClassList("ad-touch-test-who");
            who.Changed += index => TouchTest.SetSelf(index == 1);
            whoRow.Add(who);
            settings.Add(whoRow);

            var note = new Label(L.Text("ad.ui.touch_test.note")) { name = "ad-touch-test-note" };
            note.AddToClassList("ad-note");
            settings.Add(note);
            Add(settings);

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                TouchTest.Changed -= Sync;
                TouchTest.Changed += Sync;
                Sync();
            });
            RegisterCallback<DetachFromPanelEvent>(_ => TouchTest.Changed -= Sync);
            Sync();
        }

        static VisualElement InRow(VisualElement field)
        {
            var row = new VisualElement();
            row.AddToClassList("ad-row");
            row.Add(field);
            return row;
        }

        /// <summary>What Counts as offers: every body part the hand can stand for, the current one ticked.</summary>
        List<AdMenu.Item> PartItems()
        {
            var hand = TouchTest.Hand;
            var items = new List<AdMenu.Item>();
            var labels = TouchTestControls.PartLabels;
            for (int i = 0; i < labels.Count; i++)
            {
                var chosen = (TouchTestPart)i;
                items.Add(new AdMenu.Item { Text = labels[i], Checked = hand != null && hand.Part == chosen, Action = () => TouchTest.SetPart(chosen) });
            }
            return items;
        }

        void OpenParts() => AdMenu.Open(part.worldBound, part, PartItems());

        /// <summary>Reads again whether the avatar has a trigger the hand sets off. Called with every header refresh, from the plan the header already holds.</summary>
        public void Refresh(AvatarPlan plan)
        {
            hasTriggers = plan != null && plan.AvatarRoot != null && TouchProbe.HasTouchTriggers(plan.Tree);
            Sync();
        }

        void Sync()
        {
            bool running = TouchTest.RunsOn(owner.Plan?.AvatarRoot);
            EnableInClassList("ad-hidden", !running && !hasTriggers);
            settings.EnableInClassList("ad-hidden", !running);
            var hand = TouchTest.Hand;
            if (!running || hand == null) return;
            partText.text = TouchTestControls.PartLabels[(int)hand.Part];
            tagRow.EnableInClassList("ad-hidden", hand.Part != TouchTestPart.Custom);
            tag.SetValueWithoutNotify(hand.CustomTag);
            who.SetIndexWithoutNotify(hand.Self ? 1 : 0);
        }
    }
}
#endif
