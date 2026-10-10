#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The Contact or PhysBone rows of one condition of a trigger, plus the Mode row and the flip toggle picker
    /// both kinds share. The condition is the trigger's own single source while it has no groups, and the
    /// selected card row once it has.
    /// </summary>
    public sealed class TouchPanel : VisualElement
    {
        static readonly string[] CommonTags = { "Finger", "Hand", "Head", "Torso", "Foot" };
        static readonly string[] MoreTags = { "HandL", "HandR", "FootL", "FootR", "FingerL", "FingerR", "FingerIndex", "FingerMiddle", "FingerRing", "FingerLittle" };
        static readonly HashSet<string> AllStandardTags = new HashSet<string>(CommonTags.Concat(MoreTags));

        readonly EntryDetailView detail;
        readonly AvatarDoctorView owner;
        readonly TreeRow row;

        /// <param name="condition">The condition whose settings are shown. Its fields carry the same names as the entry's own, so conditionProperty may be the entry itself.</param>
        /// <param name="withMode">Whether the trigger's Hold, Flip or Follow distance row belongs under it. It sits on the trigger, not on the condition, so a panel shown inside a condition card leaves it out.</param>
        public TouchPanel(EntryDetailView detail, AvatarDoctorView owner, TreeRow row, SerializedProperty conditionProperty, WhenCondition condition = null, bool withMode = true)
        {
            this.detail = detail;
            this.owner = owner;
            this.row = row;
            var entry = row.Entry;
            condition = condition ?? WhenCondition.FromEntry(entry);

            // Mode decides which fields follow it (Fully on at only applies once Fade is picked), so for a
            // trigger that shows its own single source directly it sits right under the When row, with Fully on
            // at right after it; a per-condition card leaves both out here and gets them from ModeOnly instead,
            // shown once below every card.
            if (withMode)
            {
                BuildMode(entry);
                if (condition.kind == WhenConditionKind.Contact && entry.IsFollowDistanceTrigger) Add(BuildFullAtRow());
            }

            if (condition.kind == WhenConditionKind.Contact) BuildContact(condition, conditionProperty, includeFullAt: !withMode, fadeDriver: IsFadeDriver(entry, condition));
            else BuildPhysBone(condition, conditionProperty);
        }

        /// <summary>Whether this contact drives a Fade trigger, rather than being one of its requirements: the flat source of a trigger with no groups, or the first condition of its card. Only the driver fades, so only it gets Fully on at and loses the Touch or Closer than choice.</summary>
        static bool IsFadeDriver(MenuEntry entry, WhenCondition condition)
        {
            if (!entry.IsFollowDistanceTrigger) return false;
            if (!entry.UsesWhenGroups) return true;
            return ReferenceEquals(TouchBuilder.FollowDistancePrimary(entry).condition, condition);
        }

        /// <summary>Only the Hold, Flip or Follow distance row and the flip target picker, for a trigger whose conditions are shown as cards instead of as one source.</summary>
        public static VisualElement ModeOnly(EntryDetailView detail, AvatarDoctorView owner, TreeRow row)
        {
            return new TouchPanel(detail, owner, row);
        }

        TouchPanel(EntryDetailView detail, AvatarDoctorView owner, TreeRow row)
        {
            this.detail = detail;
            this.owner = owner;
            this.row = row;
            BuildMode(row.Entry);
        }

        /// <summary>Whether a PhysBone event reads a float parameter (and so needs a threshold), rather than a bool one. Shared with EntryDetailView's default-on-switch fix and its layout shape token.</summary>
        public static bool IsFloatEvent(PhysBoneEvent evt)
            => evt == PhysBoneEvent.AngleAbove || evt == PhysBoneEvent.StretchAbove || evt == PhysBoneEvent.SquishAbove;

        void BuildContact(WhenCondition entry, SerializedProperty property, bool includeFullAt, bool fadeDriver)
        {
            var anchorRow = new VisualElement();
            anchorRow.AddToClassList("ad-row");
            var anchorField = detail.Fields.ObjectOnAvatar<Transform>(property.FindPropertyRelative(nameof(WhenCondition.contactAnchor)), L.Text("ad.ui.touch.anchor"), () => owner.Plan?.AvatarRoot, PickKind.Transform, owner.ApplyAndRefresh);
            anchorField.name = "ad-touch-anchor";
            anchorField.tooltip = L.Text("ad.ui.touch.anchor.tip");
            anchorField.AddToClassList("ad-grow");
            anchorRow.Add(anchorField);
            Add(anchorRow);

            bool followDistance = fadeDriver;
            var radius = detail.Fields.Slider(property.FindPropertyRelative(nameof(WhenCondition.contactRadius)), L.Text("ad.ui.touch.radius"), 0.005f, 1f, owner.ApplyLive);
            radius.name = "ad-touch-radius";
            radius.tooltip = L.Text(followDistance ? "ad.ui.touch.radius_follow.tip" : "ad.ui.touch.radius.tip");
            Add(Row(radius));

            var offset = detail.Fields.Vector3(property.FindPropertyRelative(nameof(WhenCondition.contactOffset)), L.Text("ad.ui.touch.offset"));
            offset.name = "ad-touch-offset";
            offset.tooltip = L.Text("ad.ui.touch.offset.tip");
            Add(Row(offset));

            Add(BuildTagsSection(entry, property));

            var whoRow = new VisualElement();
            whoRow.AddToClassList("ad-row");
            whoRow.AddToClassList("ad-flags");
            var whoLabel = new Label(L.Text("ad.ui.touch.who")) { tooltip = L.Text("ad.ui.touch.who.tip") };
            whoLabel.AddToClassList("ad-label");
            whoRow.Add(whoLabel);
            var self = detail.Fields.Toggle(property.FindPropertyRelative(nameof(WhenCondition.contactAllowSelf)), L.Text("ad.ui.touch.self"));
            self.name = "ad-touch-self";
            self.tooltip = L.Text("ad.ui.touch.self.tip");
            self.AddToClassList("ad-compact");
            whoRow.Add(self);
            var others = detail.Fields.Toggle(property.FindPropertyRelative(nameof(WhenCondition.contactAllowOthers)), L.Text("ad.ui.touch.others"));
            others.name = "ad-touch-others";
            others.tooltip = L.Text("ad.ui.touch.others.tip");
            others.AddToClassList("ad-compact");
            whoRow.Add(others);
            Add(whoRow);

            if (followDistance) { if (includeFullAt) Add(BuildFullAtRow()); }
            else Add(BuildDistanceRow(entry));
            var hint = new Label(L.Text("ad.ui.touch.scene_hint"));
            hint.AddToClassList("ad-note");
            Add(hint);
        }

        /// <summary>Follow distance's own row in place of the Touch or Closer than choice: how close the hand must get for the effects to be fully on.</summary>
        VisualElement BuildFullAtRow()
        {
            var container = new VisualElement();
            var entryProperty = owner.EntryProperty(row.Entry);
            var slider = detail.Fields.Slider(entryProperty.FindPropertyRelative(nameof(MenuEntry.contactFullAt)), L.Text("ad.ui.touch.fullat"), 0.05f, 1f, owner.ApplyLive);
            slider.name = "ad-touch-fullat";
            slider.tooltip = L.Text("ad.ui.touch.fullat.tip");
            container.Add(Row(slider));
            var note = new Label(L.Text("ad.ui.touch.fullat_note"));
            note.AddToClassList("ad-note");
            container.Add(note);
            return container;
        }

        /// <summary>The tag checkboxes, the custom tags and the Custom tag field. Each writes to the condition it was built for: the Custom tag field saves when the focus leaves it, which can be the click that selects another row.</summary>
        VisualElement BuildTagsSection(WhenCondition entry, SerializedProperty property)
        {
            var container = new VisualElement();
            var tags = entry.contactTags ?? new List<string>();
            var conditionPath = property.propertyPath;

            var tagsRow = new VisualElement();
            tagsRow.AddToClassList("ad-row");
            var tagsLabel = new Label(L.Text("ad.ui.touch.tags")) { tooltip = L.Text("ad.ui.touch.tags.tip") };
            tagsLabel.AddToClassList("ad-label");
            tagsRow.Add(tagsLabel);
            // One wrapping flags row at the field column, the same compact toggle style "In menu" and "Saved"
            // already use, rather than a fixed two column split: at the default inspector width that split ran a
            // checkbox and its own label a few pixels past the panel's own right edge, and it read as a floating
            // vertical list instead of a row of short flags.
            var flags = new VisualElement { name = "ad-touch-tag-flags" };
            flags.AddToClassList("ad-row");
            flags.AddToClassList("ad-flags");
            flags.AddToClassList("ad-grow");
            tagsRow.Add(flags);
            container.Add(tagsRow);
            foreach (var tag in CommonTags)
            {
                var toggle = TagToggle(tag, tags, conditionPath);
                toggle.AddToClassList("ad-compact");
                flags.Add(toggle);
            }

            var moreToggles = new List<Toggle>();
            foreach (var tag in MoreTags)
            {
                var toggle = TagToggle(tag, tags, conditionPath);
                toggle.AddToClassList("ad-compact");
                toggle.EnableInClassList("ad-hidden", !detail.TouchMoreTagsOpen);
                moreToggles.Add(toggle);
                flags.Add(toggle);
            }

            // Sits last in the same wrap whether the extra tags show or not, since hiding them never reorders
            // the flow: a small button at the end of the row, not a separate section under it.
            var moreButton = new Button(() =>
            {
                detail.TouchMoreTagsOpen = !detail.TouchMoreTagsOpen;
                foreach (var toggle in moreToggles) toggle.EnableInClassList("ad-hidden", !detail.TouchMoreTagsOpen);
            })
            { name = "ad-touch-more-tags-toggle", text = L.Text("ad.ui.touch.more_tags"), tooltip = L.Text("ad.ui.touch.more_tags.tip") };
            moreButton.AddToClassList("ad-chip-gap");
            flags.Add(moreButton);

            var customTags = tags.Where(t => !string.IsNullOrWhiteSpace(t) && !AllStandardTags.Contains(t)).ToList();
            if (customTags.Count > 0)
            {
                var customList = new VisualElement();
                customList.AddToClassList("ad-tag-chips");
                foreach (var tag in customTags)
                {
                    var chip = new Button(() => detail.ToggleContactTag(conditionPath, tag, false)) { text = tag + "  x", tooltip = L.Text("ad.ui.touch.custom_tag_chip.tip") };
                    chip.AddToClassList("ad-chip");
                    customList.Add(chip);
                }
                container.Add(customList);
            }

            var customRow = new VisualElement();
            customRow.AddToClassList("ad-row");
            var customField = new TextField(L.Text("ad.ui.touch.custom_tag")) { isDelayed = true, name = "ad-touch-custom-tag", tooltip = L.Text("ad.ui.touch.custom_tag.tip") };
            customField.RegisterValueChangedCallback(e =>
            {
                var value = (e.newValue ?? "").Trim();
                customField.SetValueWithoutNotify("");
                if (value.Length == 0) return;
                detail.ToggleContactTag(conditionPath, value, true);
            });
            customRow.Add(customField);
            container.Add(customRow);

            return container;
        }

        Toggle TagToggle(string tag, List<string> current, string conditionPath)
        {
            var toggle = new Toggle(tag) { name = "ad-touch-tag-" + tag, value = current.Contains(tag), tooltip = L.Text("ad.ui.touch.tags.tip") };
            toggle.RegisterValueChangedCallback(e => detail.ToggleContactTag(conditionPath, tag, e.newValue));
            return toggle;
        }

        VisualElement BuildDistanceRow(WhenCondition entry)
        {
            var row = new VisualElement();
            row.AddToClassList("ad-row");
            var label = new Label(L.Text("ad.ui.touch.distance")) { tooltip = L.Text("ad.ui.touch.distance.tip") };
            label.AddToClassList("ad-label");
            row.Add(label);
            bool closer = entry.contactProximity > 0f;
            var choice = new SegmentedChoice(new List<string> { L.Text("ad.ui.touch.touch"), L.Text("ad.ui.touch.closer") }, closer ? 1 : 0) { name = "ad-touch-distance", tooltip = L.Text("ad.ui.touch.distance.tip") };
            choice.Changed += index => detail.SetContactProximity(index == 1 ? (entry.contactProximity > 0f ? entry.contactProximity : 0.5f) : 0f, false);
            row.Add(choice);
            if (closer)
            {
                var slider = new Slider(0f, 1f) { showInputField = true, name = "ad-touch-proximity", tooltip = L.Text("ad.ui.touch.proximity.tip") };
                slider.AddToClassList("ad-grow");
                slider.SetValueWithoutNotify(entry.contactProximity);
                slider.RegisterValueChangedCallback(e => detail.SetContactProximity(e.newValue, true));
                row.Add(slider);
            }
            return row;
        }

        void BuildPhysBone(WhenCondition entry, SerializedProperty property)
        {
            var physBoneRow = new VisualElement();
            physBoneRow.AddToClassList("ad-row");
            var physBoneField = detail.Fields.ObjectOnAvatar<VRCPhysBone>(property.FindPropertyRelative(nameof(WhenCondition.physBone)), L.Text("ad.ui.touch.physbone"), () => owner.Plan?.AvatarRoot, PickKind.PhysBone, owner.ApplyAndRefresh);
            physBoneField.name = "ad-touch-physbone";
            physBoneField.tooltip = L.Text("ad.ui.touch.physbone.tip");
            physBoneField.AddToClassList("ad-grow");
            physBoneRow.Add(physBoneField);
            Add(physBoneRow);

            var eventLabels = new List<string>
            {
                L.Text("ad.ui.touch.event.grabbed"), L.Text("ad.ui.touch.event.posed"),
                L.Text("ad.ui.touch.event.angle"), L.Text("ad.ui.touch.event.stretch"), L.Text("ad.ui.touch.event.squish"),
            };
            var eventField = new DropdownField(L.Text("ad.ui.touch.event"), eventLabels, (int)entry.physBoneEvent) { name = "ad-touch-physbone-event", tooltip = L.Text("ad.ui.touch.event.tip") };
            eventField.RegisterValueChangedCallback(e =>
            {
                int i = eventLabels.IndexOf(e.newValue);
                if (i >= 0) detail.SetPhysBoneEvent((PhysBoneEvent)i);
            });
            Add(Row(eventField));

            if (IsFloatEvent(entry.physBoneEvent))
            {
                var threshold = detail.Fields.Slider(property.FindPropertyRelative(nameof(WhenCondition.physBoneThreshold)), L.Text("ad.ui.touch.threshold"), 0f, 1f, owner.ApplyLive);
                threshold.name = "ad-touch-threshold";
                threshold.tooltip = L.Text("ad.ui.touch.threshold.tip");
                Add(Row(threshold));
            }
        }

        void BuildMode(MenuEntry entry)
        {
            var row = new VisualElement();
            row.AddToClassList("ad-row");
            var label = new Label(L.Text("ad.ui.touch.mode")) { tooltip = L.Text("ad.ui.touch.mode.tip") };
            label.AddToClassList("ad-label");
            row.Add(label);
            // Follow distance only ever does anything from a Contact source, so a PhysBone trigger is not offered
            // the option at all; a stale FollowDistance left over from a source switch shows and behaves as Hold.
            bool contactSource = entry.source == TriggerSource.Contact;
            var modeLabels = new List<string> { L.Text("ad.ui.touch.hold"), L.Text("ad.ui.touch.flip") };
            var modeTips = new List<string> { L.Text("ad.ui.touch.hold.tip"), L.Text("ad.ui.touch.flip.tip") };
            if (contactSource)
            {
                modeLabels.Add(L.Text("ad.ui.touch.follow"));
                modeTips.Add(L.Text("ad.ui.touch.follow.tip"));
            }
            // The selected segment follows the same predicate as everything else on the panel: a trigger whose
            // data says Follow distance but fails it (a stale source switch, or a first condition that is not a
            // contact) shows Hold selected, not Distance.
            int selected = entry.IsFollowDistanceTrigger ? 2 : entry.touchMode == TouchMode.FlipToggle ? 1 : 0;
            var choice = new SegmentedChoice(modeLabels, selected, modeTips) { name = "ad-touch-mode", tooltip = L.Text("ad.ui.touch.mode.tip") };
            choice.Changed += index => detail.SetTouchMode((TouchMode)index);
            row.Add(choice);
            Add(row);

            if (entry.touchMode != TouchMode.FlipToggle) return;

            var toggles = owner.Plan.Tree.Controls.Where(c => c.Entry.kind == EntryKind.Toggle).ToList();
            DropdownField toggleField;
            if (toggles.Count == 0)
            {
                toggleField = new DropdownField(L.Text("ad.ui.touch.flip_target"), new List<string> { L.Text("ad.ui.trigger.no_toggles") }, 0) { name = "ad-touch-flip-toggle", tooltip = L.Text("ad.ui.touch.flip_target.tip") };
                toggleField.SetEnabled(false);
            }
            else
            {
                var labels = toggles.Select(c => c.Component == owner.Menu ? c.Entry.PlainName : c.Entry.PlainName + " (" + c.Component.gameObject.name + ")").ToList();
                int current = toggles.FindIndex(c => c.Entry.id == entry.flipToggleId);
                toggleField = new DropdownField(L.Text("ad.ui.touch.flip_target"), labels, Mathf.Max(0, current)) { name = "ad-touch-flip-toggle", tooltip = L.Text("ad.ui.touch.flip_target.tip") };
                if (current < 0) toggleField.SetValueWithoutNotify("");
                toggleField.RegisterValueChangedCallback(e => { int i = labels.IndexOf(e.newValue); if (i >= 0) detail.SetFlipToggle(toggles[i].Entry.id); });
            }
            Add(Row(toggleField));
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
