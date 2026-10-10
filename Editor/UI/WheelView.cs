#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>The in-game radial menu as a picture: eight slots, the shown submenu in the centre, crumbs above.</summary>
    public sealed class WheelView : VisualElement
    {
        // Must match the width and height of .ad-ring in AvatarDoctor.uss.
        const float Size = 224f;
        const float Outer = 104f;
        const float Inner = 46f;

        public WheelModel Model { get; private set; } = new WheelModel();
        public event Action<Wedge> WedgeClicked;
        public event Action<string> CrumbClicked;
        /// <summary>Raised when the Test/Select switch is flipped by hand, so the owner can persist the choice.</summary>
        public event Action<bool> TestModeChanged;
        /// <summary>Raised after a test-mode action writes or clears a preview override, so the owner can refresh the header and the detail panel.</summary>
        public event Action PreviewChanged;
        /// <summary>Raised after a radial drag ticks the override. The wheel has already updated its own wedge and repainted; the owner only needs to keep the header's preview indicator in step.</summary>
        public event Action RadialDragged;

        /// <summary>How many times Show has rebuilt the crumbs, icons and labels. Tests use it to prove a cheap update never triggers a full rebuild.</summary>
        public int RebuildCount { get; private set; }

        readonly VisualElement crumbs = new VisualElement();
        readonly VisualElement ring = new VisualElement();
        readonly Label title = new Label();
        readonly Label count = new Label();
        readonly List<VisualElement> decorations = new List<VisualElement>();
        readonly Color wedgeColor, submenuColor, emptyColor, selectedColor, foreignColor;
        readonly SegmentedChoice modeChoice;
        readonly Button resetButton;
        readonly VisualElement radialRow = new VisualElement();
        readonly Slider radialSlider;
        readonly VisualElement triggerStrip = new VisualElement();
        readonly VisualElement triggerButtons = new VisualElement();
        readonly VisualElement notInMenuStrip = new VisualElement();
        readonly VisualElement notInMenuButtons = new VisualElement();

        AvatarPlan plan;
        string radialEntryId;
        bool testMode;

        public WheelView()
        {
            AddToClassList("ad-wheel");
            bool dark = EditorGUIUtility.isProSkin;
            wedgeColor = dark ? new Color32(0x3A, 0x3F, 0x46, 0xFF) : new Color32(0xD9, 0xDD, 0xE2, 0xFF);
            submenuColor = dark ? new Color32(0x33, 0x48, 0x4A, 0xFF) : new Color32(0xC6, 0xE4, 0xDD, 0xFF);
            emptyColor = dark ? new Color32(0x2B, 0x2F, 0x34, 0xFF) : new Color32(0xEE, 0xF0, 0xF2, 0xFF);
            selectedColor = dark ? new Color32(0x3F, 0xC9, 0xA8, 0xFF) : new Color32(0x12, 0x8A, 0x6E, 0xFF);
            // Neutral, a shade darker than wedgeColor so a foreign wedge (another tool's item, or the avatar's own
            // existing menu) reads as dimmed rather than accented. No longer mirrors --ad-source: that variable is gone.
            foreignColor = dark ? new Color32(0x2E, 0x32, 0x38, 0xFF) : new Color32(0xC8, 0xCB, 0xD0, 0xFF);

            var header = new VisualElement();
            header.AddToClassList("ad-wheel-header");
            var controls = new VisualElement();
            controls.AddToClassList("ad-wheel-controls");
            modeChoice = new SegmentedChoice(new List<string> { L.Text("ad.ui.wheel.select"), L.Text("ad.ui.wheel.test") }) { name = "ad-wheel-mode", tooltip = L.Text("ad.ui.wheel.mode.tip") };
            modeChoice.Changed += index =>
            {
                TestMode = index == 1;
                TestModeChanged?.Invoke(TestMode);
            };
            controls.Add(modeChoice);
            resetButton = new Button(ResetPreview) { text = L.Text("ad.ui.wheel.reset"), tooltip = L.Text("ad.ui.preview.reset.tip"), name = "ad-wheel-reset" };
            resetButton.AddToClassList("ad-wheel-reset");
            resetButton.AddToClassList("ad-hidden");
            controls.Add(resetButton);
            header.Add(controls);
            crumbs.AddToClassList("ad-crumbs");
            header.Add(crumbs);
            Add(header);

            ring.AddToClassList("ad-ring");
            ring.tooltip = L.Text("ad.ui.wheel.ring.tip");
            ring.generateVisualContent += Draw;
            ring.RegisterCallback<ClickEvent>(OnClick);
            var centre = new VisualElement();
            centre.AddToClassList("ad-wheel-centre");
            centre.pickingMode = PickingMode.Ignore;
            title.AddToClassList("ad-wheel-title");
            title.enableRichText = true;
            count.AddToClassList("ad-wheel-count");
            centre.Add(title);
            centre.Add(count);
            ring.Add(centre);
            Add(ring);

            radialRow.AddToClassList("ad-wheel-radial");
            radialRow.AddToClassList("ad-hidden");
            radialSlider = new Slider(0f, 1f) { name = "ad-wheel-radial-slider", showInputField = true, tooltip = L.Text("ad.ui.wheel.radial_slider.tip") };
            radialSlider.AddToClassList("ad-grow");
            radialSlider.RegisterValueChangedCallback(e => SetRadialValue(e.newValue));
            radialRow.Add(radialSlider);
            Add(radialRow);

            triggerStrip.AddToClassList("ad-wheel-triggers");
            triggerStrip.AddToClassList("ad-hidden");
            var triggersLabel = new Label(L.Text("ad.ui.triggers")) { name = "ad-wheel-triggers-label", tooltip = L.Text("ad.ui.wheel.triggers_strip.tip") };
            triggersLabel.AddToClassList("ad-label");
            triggerStrip.Add(triggersLabel);
            triggerButtons.AddToClassList("ad-wheel-trigger-row");
            triggerStrip.Add(triggerButtons);
            Add(triggerStrip);

            notInMenuStrip.AddToClassList("ad-wheel-triggers");
            notInMenuStrip.AddToClassList("ad-hidden");
            var notInMenuLabel = new Label(L.Text("ad.ui.wheel.not_in_menu")) { name = "ad-wheel-not-in-menu-label", tooltip = L.Text("ad.ui.wheel.not_in_menu.tip") };
            notInMenuLabel.AddToClassList("ad-label");
            notInMenuStrip.Add(notInMenuLabel);
            notInMenuButtons.AddToClassList("ad-wheel-trigger-row");
            notInMenuStrip.Add(notInMenuButtons);
            Add(notInMenuStrip);
        }

        /// <summary>Whether a wedge click drives the preview instead of selecting the row. Pushed in by the owner on every refresh; the switch and the Reset button follow it.</summary>
        internal bool TestMode
        {
            get => testMode;
            set
            {
                testMode = value;
                modeChoice.SetIndexWithoutNotify(value ? 1 : 0);
                resetButton.EnableInClassList("ad-hidden", !value);
                if (!value) radialEntryId = null;
                RefreshRadialRow();
                RefreshTriggerStrip();
                RefreshNotInMenuStrip();
            }
        }

        public void Show(WheelModel model, AvatarPlan plan = null)
        {
            RebuildCount++;
            this.plan = plan;
            Model = model ?? new WheelModel();
            title.text = Model.Title;
            count.text = string.Format(L.Text("ad.ui.wheel.count"), Model.Count, VRCExpressionsMenu.MAX_CONTROLS);
            crumbs.Clear();
            foreach (var crumb in Model.Crumbs)
            {
                var key = crumb.key;
                var button = new Button(() => CrumbClicked?.Invoke(key)) { text = crumb.name, tooltip = L.Text("ad.ui.wheel.crumb.tip") };
                button.AddToClassList("ad-crumb");
                crumbs.Add(button);
            }
            foreach (var decoration in decorations) decoration.RemoveFromHierarchy();
            decorations.Clear();
            for (int i = 0; i < Model.Wedges.Count; i++)
            {
                var wedge = Model.Wedges[i];
                var at = SlotCentre(i, (Outer + Inner) / 2f);
                VisualElement icon = wedge.Icon != null ? new Image { image = wedge.Icon, scaleMode = ScaleMode.ScaleToFit } : new KindGlyph(wedge.Kind);
                icon.AddToClassList("ad-wedge-icon");
                icon.pickingMode = PickingMode.Ignore;
                icon.style.position = Position.Absolute;
                icon.style.left = at.x - 10f;
                icon.style.top = at.y - 18f;
                ring.Add(icon);
                decorations.Add(icon);
                var label = new Label(wedge.Name) { pickingMode = PickingMode.Ignore, enableRichText = true };
                label.AddToClassList("ad-wedge-label");
                label.EnableInClassList("ad-selected", wedge.Selected);
                label.style.position = Position.Absolute;
                label.style.left = at.x - 34f;
                label.style.top = at.y + 2f;
                ring.Add(label);
                decorations.Add(label);
            }
            if (radialEntryId != null && !Model.Wedges.Any(w => w.EntryId == radialEntryId)) radialEntryId = null;
            RefreshRadialRow();
            RefreshTriggerStrip();
            RefreshNotInMenuStrip();
            ring.MarkDirtyRepaint();
        }

        /// <summary>Selects wedge i as a click would. Tests and keyboard handling use it. Routes to the preview instead of the tree while test mode is on, exactly as the panel's own preview buttons do.</summary>
        public void Click(int index)
        {
            if (index < 0 || index >= Model.Wedges.Count) return;
            var wedge = Model.Wedges[index];
            if (!testMode || wedge.IsSubmenu) { WedgeClicked?.Invoke(wedge); return; }
            if (wedge.EntryId == null || wedge.IsForeign) return;
            var control = plan?.Tree?.FindControl(wedge.EntryId);
            if (control == null) return;
            var entry = control.Entry;
            switch (entry.kind)
            {
                case EntryKind.Toggle:
                    PreviewState.Flip(entry, plan.Tree, plan.Plan);
                    CloseRadial();
                    PreviewChanged?.Invoke();
                    break;
                case EntryKind.Preset:
                    PreviewState.ApplyPreset(entry, plan.Tree);
                    CloseRadial();
                    PreviewChanged?.Invoke();
                    break;
                case EntryKind.Radial:
                    radialEntryId = radialEntryId == entry.id ? null : entry.id;
                    RefreshRadialRow();
                    break;
            }
        }

        /// <summary>Follows crumb i as a click would. Tests and keyboard handling use it.</summary>
        public void ClickCrumb(int index)
        {
            if (index < 0 || index >= Model.Crumbs.Count) return;
            CrumbClicked?.Invoke(Model.Crumbs[index].key);
        }

        /// <summary>
        /// Writes the open radial's override, as dragging its slider does. Never rebuilds: a drag ticks this once per
        /// pointer move, so it updates the matching wedge of the current model in place and repaints only the ring.
        /// Public so a test, which cannot dispatch a drag on an unattached slider, can drive the same path.
        /// </summary>
        public void SetRadialValue(float value)
        {
            if (radialEntryId == null) return;
            float clamped = Mathf.Clamp01(value);
            PreviewState.Set(radialEntryId, clamped);
            var wedge = Model.Wedges.FirstOrDefault(w => w.EntryId == radialEntryId);
            if (wedge != null) { wedge.Value = clamped; wedge.On = clamped > 0f; }
            radialSlider.SetValueWithoutNotify(clamped);
            ring.MarkDirtyRepaint();
            RadialDragged?.Invoke();
        }

        /// <summary>
        /// Recomputes every wedge's On and Value from the current PreviewState, in place, and repaints. No crumb,
        /// icon or label is touched. Used when an override changes elsewhere: the panel's own preview row, another
        /// view of the same avatar, an Undo.
        /// </summary>
        public void RefreshWedgeStates()
        {
            if (plan == null) return;
            foreach (var wedge in Model.Wedges)
            {
                if (wedge.EntryId == null) continue;
                var entry = plan.Tree?.FindControl(wedge.EntryId)?.Entry;
                if (entry == null) continue;
                if (entry.kind == EntryKind.Toggle)
                {
                    wedge.On = PreviewState.Effective(entry, plan.Tree, plan.Plan) >= 0.5f;
                    wedge.Value = wedge.On ? 1f : 0f;
                }
                else if (entry.kind == EntryKind.Radial)
                {
                    wedge.Value = PreviewState.Effective(entry, plan.Tree, plan.Plan);
                    wedge.On = wedge.Value > 0f;
                }
            }
            RefreshRadialRow();
            UpdateTriggerStates();
            UpdateNotInMenuStates();
            ring.MarkDirtyRepaint();
        }

        /// <summary>Presses a simulated touch on a trigger: on, then off on the next press. A trigger in Flip a toggle mode flips its target toggle's preview state instead, and never touches its own override. Does nothing while the trigger's gate is not met, the same as a real touch would. Public so a test can drive it without a real button click.</summary>
        public void PressTrigger(string triggerId)
        {
            if (string.IsNullOrEmpty(triggerId)) return;
            var entry = plan?.Tree?.Triggers?.FirstOrDefault(t => t.Entry.id == triggerId)?.Entry;
            if (entry != null && !PreviewState.GateMet(entry, plan.Tree, plan.Plan)) return;
            if (entry != null && entry.IsFlipTrigger)
            {
                var target = plan.Tree.FindControl(entry.flipToggleId);
                if (target != null) PreviewState.Flip(target.Entry, plan.Tree, plan.Plan);
                UpdateTriggerStates();
                PreviewChanged?.Invoke();
                return;
            }
            if (PreviewState.TryGet(triggerId, out var value) && value >= 0.5f) PreviewState.Clear(triggerId);
            else PreviewState.Set(triggerId, 1f);
            UpdateTriggerStates();
            PreviewChanged?.Invoke();
        }

        /// <summary>Clears every preview override. The wheel's own Reset button behind it, and Header.ResetPreview does the same for the panel.</summary>
        public void ResetPreview()
        {
            PreviewState.ClearAll();
            RefreshRadialRow();
            RefreshTriggerStrip();
            RefreshNotInMenuStrip();
            PreviewChanged?.Invoke();
        }

        /// <summary>Flips a toggle or radial that is hidden from the menu, as the "Not in the menu" strip's own press does. Public so a test can drive it without a real button click.</summary>
        public void PressHiddenToggle(string entryId)
        {
            if (string.IsNullOrEmpty(entryId)) return;
            var control = plan?.Tree?.FindControl(entryId);
            if (control == null || control.Entry.kind != EntryKind.Toggle) return;
            PreviewState.Flip(control.Entry, plan.Tree, plan.Plan);
            UpdateNotInMenuStates();
            PreviewChanged?.Invoke();
        }

        void CloseRadial()
        {
            if (radialEntryId == null) return;
            radialEntryId = null;
            RefreshRadialRow();
        }

        void RefreshRadialRow()
        {
            bool show = testMode && radialEntryId != null;
            radialRow.EnableInClassList("ad-hidden", !show);
            if (!show) return;
            var entry = plan?.Tree?.FindControl(radialEntryId)?.Entry;
            if (entry == null) { radialEntryId = null; radialRow.AddToClassList("ad-hidden"); return; }
            radialSlider.SetValueWithoutNotify(Mathf.Clamp01(PreviewState.Effective(entry, plan.Tree, plan.Plan)));
        }

        void RefreshTriggerStrip()
        {
            triggerButtons.Clear();
            var triggers = plan?.Tree?.Triggers?.Where(t => !t.Excluded && t.Entry.source >= TriggerSource.Parameter && t.Entry.source != TriggerSource.BlendShape).ToList()
                ?? new List<TriggerNode>();
            bool show = testMode && triggers.Count > 0;
            triggerStrip.EnableInClassList("ad-hidden", !show);
            if (!show) return;
            foreach (var node in triggers)
            {
                var id = node.Entry.id;
                var button = new Button(() => PressTrigger(id)) { text = node.Entry.PlainName, userData = id, tooltip = L.Text("ad.ui.wheel.trigger_button.tip") };
                button.AddToClassList("ad-wheel-trigger");
                triggerButtons.Add(button);
            }
            UpdateTriggerStates();
        }

        /// <summary>Repaints the strip from the overrides as they stand. A press moves one class; the buttons themselves stay, so nothing under the pointer is replaced. A trigger whose gate is not met is disabled: dimmed, and a press does nothing.</summary>
        void UpdateTriggerStates()
        {
            foreach (var child in triggerButtons.Children())
            {
                if (!(child.userData is string id)) continue;
                child.EnableInClassList("ad-primary", PreviewState.TryGet(id, out var value) && value >= 0.5f);
                var entry = plan?.Tree?.Triggers?.FirstOrDefault(t => t.Entry.id == id)?.Entry;
                child.SetEnabled(entry == null || PreviewState.GateMet(entry, plan.Tree, plan.Plan));
            }
        }

        void RefreshNotInMenuStrip()
        {
            notInMenuButtons.Clear();
            var hidden = Model.HiddenControls;
            bool show = testMode && hidden.Count > 0;
            notInMenuStrip.EnableInClassList("ad-hidden", !show);
            if (!show) return;
            foreach (var (name, id) in hidden)
            {
                var button = new Button(() => PressHiddenToggle(id)) { text = name, userData = id, tooltip = L.Text("ad.ui.wheel.hidden_button.tip") };
                button.AddToClassList("ad-wheel-trigger");
                notInMenuButtons.Add(button);
            }
            UpdateNotInMenuStates();
        }

        /// <summary>Repaints the "Not in the menu" strip from the current preview state, the same way UpdateTriggerStates repaints the trigger strip.</summary>
        void UpdateNotInMenuStates()
        {
            foreach (var child in notInMenuButtons.Children())
            {
                if (!(child.userData is string id)) continue;
                var entry = plan?.Tree?.FindControl(id)?.Entry;
                child.EnableInClassList("ad-primary", entry != null && PreviewState.Effective(entry, plan.Tree, plan.Plan) >= 0.5f);
            }
        }

        static Vector2 SlotCentre(int index, float radius)
        {
            float step = 360f / VRCExpressionsMenu.MAX_CONTROLS;
            float angle = (-90f + step * (index + 0.5f)) * Mathf.Deg2Rad;
            return new Vector2(Size / 2f + Mathf.Cos(angle) * radius, Size / 2f + Mathf.Sin(angle) * radius);
        }

        void OnClick(ClickEvent e)
        {
            var local = ring.WorldToLocal(e.position);
            Click(WheelModel.WedgeAt(local, new Vector2(Size / 2f, Size / 2f), Outer, Inner, Model.Wedges.Count));
        }

        void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            var centre = new Vector2(Size / 2f, Size / 2f);
            float step = 360f / VRCExpressionsMenu.MAX_CONTROLS;
            var palette = new WedgePalette(wedgeColor, submenuColor, emptyColor, selectedColor, foreignColor);
            for (int i = 0; i < VRCExpressionsMenu.MAX_CONTROLS; i++)
            {
                float start = -90f + i * step + 1.5f;
                float end = start + step - 3f;
                bool inRange = i < Model.Wedges.Count;
                painter.BeginPath();
                painter.Arc(centre, Outer, start, end, ArcDirection.Clockwise);
                painter.Arc(centre, Inner, end, start, ArcDirection.CounterClockwise);
                painter.ClosePath();
                painter.fillColor = FillFor(inRange ? Model.Wedges[i] : null, palette);
                painter.Fill();

                if (inRange && Model.Wedges[i].Kind == GlyphKind.Radial && Model.Wedges[i].Value > 0f)
                {
                    float sweep = (end - start) * Mathf.Clamp01(Model.Wedges[i].Value);
                    painter.BeginPath();
                    painter.Arc(centre, Outer - 4f, start, start + sweep, ArcDirection.Clockwise);
                    painter.strokeColor = selectedColor;
                    painter.lineWidth = 2f;
                    painter.Stroke();
                }
            }
        }

        /// <summary>One wedge slot's palette: Own and Submenu are the caller's own neutral tones, Foreign the
        /// neutral tone for another tool's or the avatar's existing items, Accent the browsed or active fill, Empty
        /// an unused slot.</summary>
        public readonly struct WedgePalette
        {
            public readonly Color Own;
            public readonly Color Submenu;
            public readonly Color Empty;
            public readonly Color Accent;
            public readonly Color Foreign;

            public WedgePalette(Color own, Color submenu, Color empty, Color accent, Color foreign)
            {
                Own = own;
                Submenu = submenu;
                Empty = empty;
                Accent = accent;
                Foreign = foreign;
            }
        }

        /// <summary>The fill for one wedge slot. A pure function so a test can prove a foreign wedge always gets
        /// the neutral fill, never the accent or a source colour, without building a live wheel.</summary>
        public static Color FillFor(Wedge wedge, WedgePalette palette)
        {
            if (wedge == null) return palette.Empty;
            if (wedge.Selected || wedge.On) return palette.Accent;
            if (wedge.IsForeign) return palette.Foreign;
            return wedge.IsSubmenu ? palette.Submenu : palette.Own;
        }
    }
}
#endif
