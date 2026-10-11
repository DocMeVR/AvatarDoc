#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using nadena.dev.ndmf.localization;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Phase 12 task 9 Part D: the creator's "look over all UI elements" ask, measured through PanelAudit against a
    /// real, laid out 360px wide panel (LayoutProbe) rather than eyeballed. Each test builds one group of panels the
    /// brief named and audits every one of them; a failure names the offending element and the rule it broke.
    /// </summary>
    public class PanelAuditTests
    {
        TestAvatar avatar;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        AvatarDoctorView View(AvatarDoc menu) => new AvatarDoctorView(new SerializedObject(menu), menu);

        MenuEntry TriggerOn(AvatarDoc menu, string name)
        {
            var entry = menu.AddEntry(EntryKind.Trigger, name);
            entry.objects.Add(new ObjectTarget { target = avatar.Glasses });
            return entry;
        }

        MenuEntry ContactTriggerOn(AvatarDoc menu, string name, Transform anchor)
        {
            var entry = TriggerOn(menu, name);
            entry.source = TriggerSource.Contact;
            entry.contactAnchor = anchor;
            return entry;
        }

        /// <summary>
        /// The target list rows' own trailing "x" remove buttons are the audit's one deliberate exception, named
        /// once here with the reason the brief asks for: an intentional trailing action control, the same shape
        /// as the header's own Show and Remove, not a data field the 112px column or the "never after a growing
        /// field" rule was built to measure. The tree row's own Move to, Duplicate and Delete buttons that used to
        /// need the same exception are gone: those actions live in the right click menu only now.
        /// </summary>
        static PanelAudit.AllowList StandardAllowList() => new PanelAudit.AllowList()
            .Allow(e => e.name == "remove" && e.ClassListContains("ad-w-24"), "a target list row's own trailing x remove button: deliberately at the row's far end, removing the whole row rather than a field on it")
            .Allow(e => e.ClassListContains("ad-chip-remove"), "a chip's own remove x: trailing inside its own chip, not a labelled row field")
            .Allow(e => e.name == "ad-unlock", "the lock banner's own trailing action button")
            .Allow(e => e.name == "ad-preview-simulate" || e.name == "ad-select-component", "a panel's own single primary action button, not part of a labelled field row")
            .AllowForRule(3, e => e is Toggle && e.parent != null && e.parent.ClassListContains("ad-flags"),
                "a compact flags row's own toggle (In menu, Default on, Saved, Synced): keeps its own inline caption and sits in sequence with its row mates, not at the field column, whether the row holds one flag or several");

        [Test]
        public void ToggleAndRadialPanelsFitAndAlign()
        {
            var menu = avatar.AddMenu();
            avatar.AddBodyShapes("Blush");
            var toggle = menu.AddEntry(EntryKind.Toggle, "Face");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            toggle.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", minWeight = 0f, maxWeight = 100f });
            toggle.materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 0, material = avatar.MaterialA });
            toggle.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 0.5f });
            var clip = new AnimationClip();
            toggle.animations.Add(new AnimationTarget { clip = clip });
            toggle.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform, keepOffset = true });

            var radial = menu.AddEntry(EntryKind.Radial, "Eyes");
            radial.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", minWeight = 0f, maxWeight = 100f });

            var submenu = menu.AddEntry(EntryKind.Submenu, "Outfits");

            var view = View(menu);
            var allow = StandardAllowList();
            try
            {
                LayoutProbe.Measure(view, () =>
                {
                    view.Tree.SelectEntry(toggle.id);
                    LayoutProbe.Validate(view);
                    PanelAudit.Audit(view.Detail, view.Detail.worldBound.xMax, allow);

                    view.Tree.SelectEntry(radial.id);
                    LayoutProbe.Validate(view);
                    PanelAudit.Audit(view.Detail, view.Detail.worldBound.xMax, allow);

                    view.Tree.SelectEntry(submenu.id);
                    LayoutProbe.Validate(view);
                    PanelAudit.Audit(view.Detail, view.Detail.worldBound.xMax, allow);
                });
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void TriggerPanelsFitAndAlign()
        {
            var menu = avatar.AddMenu();
            var hold = ContactTriggerOn(menu, "Poke", avatar.Root.transform);
            hold.touchMode = TouchMode.Hold;
            // The touch panel's own "Closer than" shape: Hold, with the contact's own distance choice set past
            // plain touch, which shows the extra proximity slider row beneath the touch/closer choice itself.
            var closer = ContactTriggerOn(menu, "Near", avatar.Root.transform);
            closer.touchMode = TouchMode.Hold;
            closer.contactProximity = 0.5f;
            // The real "Fade" shape: a Contact trigger whose mode is Follow distance, which shows the three
            // segment mode choice with Fade selected, and BuildFullAtRow's own "Fully on at" slider and note in
            // place of the plain touch/closer row. IsFollowDistanceTrigger also requires the trigger's own first
            // condition to be a Contact one; ContactTriggerOn's single, group-less condition already is.
            var fade = ContactTriggerOn(menu, "Fade", avatar.Root.transform);
            fade.touchMode = TouchMode.FollowDistance;
            Assert.That(fade.IsFollowDistanceTrigger, Is.True, "the fixture must actually reach the Fade shape TouchPanel.BuildFullAtRow builds");
            var pb = avatar.Hat.AddComponent<VRCPhysBone>();
            var physBone = TriggerOn(menu, "Squeeze");
            physBone.source = TriggerSource.PhysBone;
            physBone.physBone = pb;

            var view = View(menu);
            var allow = StandardAllowList();
            LayoutProbe.Measure(view, () =>
            {
                foreach (var entry in new[] { hold, closer, fade, physBone })
                {
                    view.Tree.SelectEntry(entry.id);
                    LayoutProbe.Validate(view);
                    PanelAudit.Audit(view.Detail, view.Detail.worldBound.xMax, allow);
                }
            });
        }

        [Test]
        public void OtherPanelsFitAndAlign()
        {
            var menu = avatar.AddMenu();
            var over = menu.AddEntry(EntryKind.Override, "Silence");
            over.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 50f });

            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var preset = menu.AddEntry(EntryKind.Preset, "Casual");
            preset.presetStates.Add(new PresetState { entryId = hat.id, on = true });

            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Hat;

            var controller = new AnimatorController { name = "FX" };
            var asset = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            asset.name = "Bought";
            var extraGo = new GameObject("Extra");
            AvatarDoc foreignHolder = null;
            Component foreignToggle = null;
            try
            {
                extraGo.transform.SetParent(avatar.Root.transform);
                foreignHolder = extraGo.AddComponent<AvatarDoc>();
                foreignHolder.installPath = "Old";
                foreignHolder.AddEntry(EntryKind.Toggle, "Existing").objects.Add(new ObjectTarget { target = avatar.Body.gameObject });
                foreignToggle = VrcFuryTestHelper.AddToggle(extraGo, "Foreign");

                var view = View(menu);
                var allow = StandardAllowList();
                avatar.AddBodyShapes("Blush");

                LayoutProbe.Measure(view, () =>
                {
                    view.Tree.SelectEntry(over.id);
                    LayoutProbe.Validate(view);
                    PanelAudit.Audit(view.Detail, view.Detail.worldBound.xMax, allow);

                    view.Tree.SelectEntry(preset.id);
                    LayoutProbe.Validate(view);
                    PanelAudit.Audit(view.Detail, view.Detail.worldBound.xMax, allow);

                    view.Tree.SelectEntry(grab.id);
                    LayoutProbe.Validate(view);
                    PanelAudit.Audit(view.Detail, view.Detail.worldBound.xMax, allow);

                    var animatorRow = view.Tree.AddAnimator(controller);
                    LayoutProbe.Validate(view);
                    PanelAudit.Audit(view.Detail, view.Detail.worldBound.xMax, allow);

                    var assetRow = view.Tree.AddMenuAsset(asset);
                    LayoutProbe.Validate(view);
                    PanelAudit.Audit(view.Detail, view.Detail.worldBound.xMax, allow);

                    var existingRow = view.Tree.RowsOfKind(RowKind.Existing).First();
                    view.Tree.SelectKey(existingRow.Key);
                    LayoutProbe.Validate(view);
                    PanelAudit.Audit(view.Detail, view.Detail.worldBound.xMax, allow);

                    var foreignRow = view.Tree.RowsOfKind(RowKind.Foreign).First();
                    view.Tree.SelectKey(foreignRow.Key);
                    LayoutProbe.Validate(view);
                    PanelAudit.Audit(view.Detail, view.Detail.worldBound.xMax, allow);
                });
            }
            finally
            {
                Object.DestroyImmediate(controller);
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void HeaderFitsAndAligns()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            var allow = StandardAllowList()
                .Allow(e => e.name == "ad-problems-arrow", "the status row's own leading disclosure arrow, before the status row's own spacer")
                .Allow(e => e.name == "ad-status-glyph", "the status row's own leading icon, before the status row's own spacer")
                .Allow(e => e.name == "ad-ready-show" || e.name == "ad-summary-toggle", "the status row's own trailing actions, after the status row's own spacer")
                .Allow(e => e.name == "ad-settings-fold-dot" || e.name == "ad-advanced-fold-dot", "a fold's own non default dot, drawn inside the foldout's own native toggle row");
            // A fresh view per width: reusing one view's tree across two LayoutProbe.Measure calls left its Yoga
            // nodes holding the first window's resolved sizes, so the second call silently re-measured the same
            // 360px geometry under an 800px window instead of a real, wider layout.
            foreach (var width in new[] { 360f, 800f })
            {
                var view = View(menu);
                LayoutProbe.Measure(view, () =>
                {
                    if (!view.Header.SettingsFoldOpen) view.Header.ToggleSettingsFold();
                    LayoutProbe.Validate(view);
                    PanelAudit.Audit(view.Header, view.Header.worldBound.xMax, allow);
                }, width, 900f);
            }
        }

        /// <summary>Phase 12 task 10: the merged, compact header drops the title row, the count badge and the title's own free count entirely.</summary>
        [Test]
        public void HeaderHasNoTitleRowAndNothingTwice()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);

            Assert.That(view.Header.Q<VisualElement>("ad-title-bar"), Is.Null);
            Assert.That(view.Header.Q<Label>("ad-title"), Is.Null);
            Assert.That(view.Header.Q<Label>("ad-summary-badge"), Is.Null);
            Assert.That(view.Header.Q<Label>("ad-title-bits"), Is.Null);

            var freeTexts = view.Header.Query<Label>().Build().ToList()
                .Where(l => !string.IsNullOrEmpty(l.text) && System.Text.RegularExpressions.Regex.IsMatch(l.text, @"\bfree\b"))
                .ToList();
            Assert.That(freeTexts.Count, Is.EqualTo(1), "the free count text must appear exactly once in the header");
        }

        /// <summary>Phase 12 task 10: clicking the arrow or the status text opens and closes the same list, the same way the key remembers it.</summary>
        [Test]
        public void StatusArrowAndTextBothToggleTheList()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "A").objects.Add(new ObjectTarget { target = null });
            var view = View(menu);
            Assert.That(view.Header.ProblemsOpen, Is.False);

            LayoutProbe.Measure(view, () =>
            {
                var arrow = view.Header.Q<Toggle>("ad-problems-arrow");
                Assert.That(arrow, Is.Not.Null);
                arrow.value = true;
                Assert.That(view.Header.ProblemsOpen, Is.True, "the arrow drives the same state ToggleProblemsOpen does");
                arrow.value = false;
                Assert.That(view.Header.ProblemsOpen, Is.False);

                UiTestEvents.SimulateClick(view.Header.ReadyLabelClickable);
                Assert.That(view.Header.ProblemsOpen, Is.True, "the status text opens the same list the arrow does");
                UiTestEvents.SimulateClick(view.Header.ReadyLabelClickable);
                Assert.That(view.Header.ProblemsOpen, Is.False);
            });
        }

        /// <summary>Phase 12 task 10: the arrow is a real Toggle carrying Unity's own Foldout toggle class, so Unity's own stylesheet draws its triangle rather than a drawn on glyph.</summary>
        [Test]
        public void StatusArrowIsUnitysFoldoutArrow()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "A").objects.Add(new ObjectTarget { target = null });
            var view = View(menu);

            var arrow = view.Header.Q<Toggle>("ad-problems-arrow");
            Assert.That(arrow, Is.Not.Null);
            Assert.That(arrow.ClassListContains(Foldout.toggleUssClassName), Is.True, "the arrow must carry Unity's own foldout toggle class so its checkmark draws the native triangle");
            // The header wires these gently so a changed Unity never breaks the inspector; this is where a change
            // shows up instead, before an arrow with no triangle ships.
            var input = arrow.Q(className: Toggle.inputUssClassName);
            Assert.That(input, Is.Not.Null, "the toggle's own input element is missing in this Unity version");
            Assert.That(input.ClassListContains(Foldout.inputUssClassName), Is.True, "the input carries the foldout input class, as a real Foldout's does");
            var checkmark = input.Q(className: Toggle.checkmarkUssClassName);
            Assert.That(checkmark, Is.Not.Null, "the toggle's own checkmark element is missing in this Unity version");
            Assert.That(checkmark.ClassListContains(Foldout.checkmarkUssClassName), Is.True, "the checkmark carries the foldout checkmark class, which is what draws the triangle");
        }

        /// <summary>Phase 12 task 10: Summary sits at the status row's own right end, with Show directly before it, both on the row's own vertical centre, at both the default and a wider width.</summary>
        [Test]
        public void SummarySitsAtTheStatusRowsRightEnd()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "A").objects.Add(new ObjectTarget { target = null });

            // A fresh view per width, for the same reason HeaderFitsAndAligns needs one: a reused view's tree
            // keeps the first window's resolved Yoga sizes on its second, differently sized pass.
            foreach (var width in new[] { 360f, 800f })
            {
                var view = View(menu);
                LayoutProbe.Measure(view, () =>
                {
                    var statusRow = view.Header.Q<VisualElement>("ad-status-row");
                    var arrow = view.Header.Q<Toggle>("ad-problems-arrow");
                    var glyph = view.Header.Q<KindGlyph>("ad-status-glyph");
                    var label = view.Header.Q<Label>("ad-ready-label");
                    var show = view.Header.Q<Button>("ad-ready-show");
                    var summary = view.Header.Q<Button>("ad-summary-toggle");

                    Assert.That(summary.worldBound.xMax, Is.EqualTo(statusRow.worldBound.xMax).Within(1f), "Summary must sit at the row's own right end at " + width + "px");
                    Assert.That(show.worldBound.xMax, Is.LessThanOrEqualTo(summary.worldBound.x + 0.5f), "Show must sit directly before Summary at " + width + "px");
                    Assert.That(summary.worldBound.x - show.worldBound.xMax, Is.LessThanOrEqualTo(8.5f), "nothing but the row's own button gap sits between Show and Summary at " + width + "px");

                    float rowCentre = statusRow.worldBound.y + statusRow.worldBound.height / 2f;
                    foreach (var item in new VisualElement[] { arrow, glyph, label, show, summary })
                    {
                        float itemCentre = item.worldBound.y + item.worldBound.height / 2f;
                        Assert.That(itemCentre, Is.EqualTo(rowCentre).Within(1f), item.name + " must sit on the status row's own vertical centre at " + width + "px");
                    }
                }, width, 900f);
            }
        }

        /// <summary>Phase 12 task 10: the first-use tip spans the header's own content width, with its close cross at the tip's own right edge, inside its own padding.</summary>
        [Test]
        public void TipSpansTheWidthWithItsCloseAtTheRight()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);

            LayoutProbe.Measure(view, () =>
            {
                var tip = view.Header.Q<VisualElement>("ad-tip-header");
                Assert.That(tip, Is.Not.Null, "a fresh view's tip has not been dismissed");
                var statusRow = view.Header.Q<VisualElement>("ad-status-row");
                Assert.That(tip.worldBound.xMax, Is.EqualTo(statusRow.worldBound.xMax).Within(1f), "the tip must span the same content width the status row does");

                var close = tip.Q<Button>("ad-tip-close");
                Assert.That(close, Is.Not.Null);
                Assert.That(close.worldBound.xMax, Is.LessThanOrEqualTo(tip.worldBound.xMax + 0.5f));
                Assert.That(close.worldBound.xMax, Is.GreaterThan(tip.worldBound.xMax - 14f), "the close cross sits inside the tip's own right padding, not flush past it");
            });
        }

        /// <summary>Phase 12 task 10: at 800px the memory bar legend's two swatch items and the free text share one left aligned line, ending at the right edge.</summary>
        [Test]
        public void LegendIsOneLeftAlignedLine()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);

            LayoutProbe.Measure(view, () =>
            {
                var legend = view.Header.Q<VisualElement>("ad-membar-legend");
                var avatarSwatch = view.Header.Q<VisualElement>("ad-legend-swatch-avatar");
                var avatarLabel = view.Header.Q<Label>("ad-legend-avatar");
                var doctorSwatch = view.Header.Q<VisualElement>("ad-legend-swatch-doctor");
                var doctorLabel = view.Header.Q<Label>("ad-legend-doctor");
                var freeLabel = view.Header.Q<Label>("ad-legend-free");

                float Centre(VisualElement e) => e.worldBound.y + e.worldBound.height / 2f;
                float lineCentre = Centre(freeLabel);
                foreach (var item in new[] { avatarSwatch, avatarLabel, doctorSwatch, doctorLabel })
                    Assert.That(Centre(item), Is.EqualTo(lineCentre).Within(1f), item.name + " must share the legend's one line at 800px");
                Assert.That(avatarSwatch.worldBound.x, Is.LessThan(avatarLabel.worldBound.x));
                Assert.That(avatarLabel.worldBound.x, Is.LessThan(doctorSwatch.worldBound.x));
                Assert.That(doctorSwatch.worldBound.x, Is.LessThan(doctorLabel.worldBound.x));
                Assert.That(doctorLabel.worldBound.x, Is.LessThan(freeLabel.worldBound.x));
                Assert.That(freeLabel.worldBound.xMax, Is.EqualTo(legend.worldBound.xMax).Within(1f), "the free text must end at the legend's own right edge");
            }, 800f, 900f);
        }

        /// <summary>Whether a text element shows its whole text: its content box is at least as wide as the text measures.</summary>
        static void AssertTextFits(TextElement element, string what)
        {
            var size = element.MeasureTextSize(element.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
            Assert.That(element.contentRect.width, Is.GreaterThanOrEqualTo(size.x - 0.5f),
                what + " \"" + element.text + "\" is cut short: " + element.contentRect.width.ToString("0.0") + " wide for text that measures " + size.x.ToString("0.0"));
        }

        /// <summary>
        /// Plan 15 task R: the Hold the rest button beside Capture and the rule lines under a toggle's and a trigger's
        /// blend shapes, at both ends of the default inspector width (360 and 300 px): the switch named for its mesh,
        /// the Default or 0 choice, the Hold blink and Hold visemes switches (plan 17 task H) and the count note, with
        /// nothing clipped or cut short. A long mesh name may end in an ellipsis; its switch's tooltip then names it in
        /// full.
        /// </summary>
        [Test]
        public void ShapeSetRuleLinesFitAndAlign()
        {
            var menu = avatar.AddMenu();
            avatar.AddBodyShapes("Smile", "Blink", "vrc.v_aa");
            avatar.Descriptor.enableEyeLook = true;
            avatar.Descriptor.customEyeLookSettings.eyelidType = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.EyelidType.Blendshapes;
            avatar.Descriptor.customEyeLookSettings.eyelidsSkinnedMesh = avatar.Body;
            avatar.Descriptor.customEyeLookSettings.eyelidsBlendshapes = new[] { avatar.Mesh.GetBlendShapeIndex("Blink"), -1, -1 };
            avatar.Descriptor.lipSync = VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape;
            avatar.Descriptor.VisemeSkinnedMesh = avatar.Body;
            avatar.Descriptor.VisemeBlendShapes = new string[15];
            avatar.Descriptor.VisemeBlendShapes[1] = "vrc.v_aa";
            const string longName = "Body_Outfit_Winter_Coat_With_A_Very_Long_Mesh_Name";
            var longGo = new GameObject(longName);
            longGo.transform.SetParent(avatar.Root.transform);
            var longMesh = longGo.AddComponent<SkinnedMeshRenderer>();
            longMesh.sharedMesh = TestAvatar.QuadWithShapes("Shrink", "Hide");

            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            face.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true, value = ShapeSetValue.Default });
            face.shapeSets.Add(new ShapeSetRule { mesh = longMesh, zeroOthers = true, value = ShapeSetValue.Zero });
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var pout = TriggerOn(menu, "Pout");
            pout.source = TriggerSource.Toggle;
            pout.sourceToggleId = hat.id;
            pout.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            pout.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true, value = ShapeSetValue.Zero, holdBlink = true, holdVisemes = true });

            var allow = StandardAllowList();
            try
            {
                foreach (var width in new[] { 360f, 300f })
                {
                    var view = View(menu);
                    LayoutProbe.Measure(view, () =>
                    {
                        foreach (var entry in new[] { face, pout })
                        {
                            view.Tree.SelectEntry(entry.id);
                            LayoutProbe.Validate(view);
                            PanelAudit.Audit(view.Detail, view.Detail.worldBound.xMax, allow);

                            var list = view.Detail.Q<TargetListView>("ad-blendshapes");
                            foreach (var name in new[] { "ad-shape-set-capture", "ad-shape-set-hold" })
                            {
                                var button = list.Q<Button>(name);
                                Assert.That(button.worldBound.width, Is.GreaterThan(0f), entry.name + ": " + name + " shows");
                                AssertTextFits(button, entry.name + ": " + name);
                            }

                            var rows = list.Query<VisualElement>("ad-shape-set-row").ToList();
                            Assert.That(rows.Count, Is.EqualTo(entry.shapeSets.Count), entry.name + ": one line per rule");
                            for (int i = 0; i < rows.Count; i++)
                            {
                                var row = rows[i];
                                var rule = entry.shapeSets[i];
                                var on = row.Q<Toggle>("zero-others");
                                Assert.That(on.tooltip, Does.Contain(rule.mesh.name), "the switch's tooltip names the mesh in full");
                                if (rule.mesh != longMesh) AssertTextFits(on.labelElement, entry.name + ": the switch");
                                else Assert.That(on.labelElement.worldBound.xMax, Is.LessThanOrEqualTo(row.Q<Button>("remove").worldBound.xMin + 0.5f), "a long mesh name stops short of the remove button");
                                var value = row.Q<SegmentedChoice>("ad-shape-set-value");
                                foreach (var item in value.Items) AssertTextFits(item, entry.name + ": the Default or 0 choice");
                                foreach (var name in new[] { "hold-blink", "hold-visemes" })
                                {
                                    var tracked = row.Q<Toggle>(name);
                                    Assert.That(tracked != null, Is.EqualTo(rule.mesh == avatar.Body), entry.name + ": " + name + " shows for the mesh the descriptor blinks and speaks with");
                                    if (tracked == null) continue;
                                    AssertTextFits(tracked.labelElement, entry.name + ": " + name);
                                    Assert.That(tracked.worldBound.width, Is.GreaterThan(0f), entry.name + ": " + name + " is laid out");
                                    Assert.That(tracked.worldBound.xMax, Is.LessThanOrEqualTo(view.Detail.worldBound.xMax + 0.5f));
                                }
                                var note = row.Q<Label>("ad-shape-set-note");
                                Assert.That(note.worldBound.height, Is.GreaterThan(0f), entry.name + ": the count note shows");
                                Assert.That(note.resolvedStyle.whiteSpace, Is.EqualTo(WhiteSpace.Normal), "the note wraps rather than being cut");
                            }
                        }
                    }, width);
                }
            }
            finally
            {
                Object.DestroyImmediate(longMesh.sharedMesh);
            }
        }

        [Test]
        public void AdvancedFoldFitsAndAligns()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            toggle.parameterName = "CustomParam";
            toggle.exclusiveGroup = "";
            toggle.blendSeconds = 0.5f;
            toggle.platform = EntryPlatform.PCOnly;

            var view = View(menu);
            var allow = StandardAllowList();
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(toggle.id);
                LayoutProbe.Validate(view);
                var fold = view.Detail.Q<AdvancedFold>();
                Assert.That(fold, Is.Not.Null);
                if (!fold.IsOpen) fold.ToggleOpen();
                LayoutProbe.Validate(view);
                PanelAudit.Audit(fold, view.Detail.worldBound.xMax, allow);
            });
        }

        /// <summary>
        /// Plan 16: in Japanese and Chinese every panel takes NDMF's font for the language (LanguageFont), which draws
        /// Latin letters wider than Unity's own font. Each panel is measured in the real language files at both ends
        /// of the default inspector width, in NDMF's font: the audit passes and every text shows whole. An object
        /// field's caption is left out: it shows an object's name, which Unity ends in an ellipsis whenever the field
        /// is narrower, in any language and font.
        /// </summary>
        [Test]
        public void NdmfsFontKeepsEveryPanelFittingInJapaneseAndChinese()
        {
            var menu = avatar.AddMenu();
            avatar.AddBodyShapes("Blush");
            var toggle = menu.AddEntry(EntryKind.Toggle, "Face");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            toggle.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", minWeight = 0f, maxWeight = 100f });
            toggle.materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 0, material = avatar.MaterialA });
            toggle.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 0.5f });
            toggle.follows.Add(new FollowTarget { target = avatar.Glasses, follows = avatar.Hat.transform, keepOffset = true });
            toggle.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true, value = ShapeSetValue.Default });
            var radial = menu.AddEntry(EntryKind.Radial, "Eyes");
            radial.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", minWeight = 0f, maxWeight = 100f });
            var submenu = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var hold = ContactTriggerOn(menu, "Poke", avatar.Root.transform);
            hold.touchMode = TouchMode.Hold;
            var fade = ContactTriggerOn(menu, "Fade", avatar.Root.transform);
            fade.touchMode = TouchMode.FollowDistance;
            var physBone = TriggerOn(menu, "Squeeze");
            physBone.source = TriggerSource.PhysBone;
            physBone.physBone = avatar.Hat.AddComponent<VRCPhysBone>();
            var linked = TriggerOn(menu, "Pout");
            linked.source = TriggerSource.Toggle;
            linked.sourceToggleId = toggle.id;
            var over = menu.AddEntry(EntryKind.Override, "Silence");
            over.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 50f });
            var preset = menu.AddEntry(EntryKind.Preset, "Casual");
            preset.presetStates.Add(new PresetState { entryId = toggle.id, on = true });
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Hat;
            var entries = new[] { toggle, radial, submenu, hold, fade, physBone, linked, over, preset, grab };

            var real = LanguageFont.Applier;
            var report = new List<string>();
            try
            {
                foreach (var language in new[] { "ja-JP", "zh-Hans" })
                {
                    var font = NdmfFont(language);
                    LanguageFont.Applier = element => element.style.unityFontDefinition = font;
                    using (L.UseTestLanguage(language, null))
                    {
                        foreach (var width in new[] { 300f, 360f })
                        {
                            var measured = MeasurePanels(menu, entries, width);
                            string where = language + " at " + width + " px";
                            Assert.That(measured.FontsSet, Is.True, where + ": the panels were measured in NDMF's font");
                            foreach (var failure in measured.Failures) report.Add(where + ", " + failure.Key + ": " + failure.Value);
                            foreach (var cut in measured.Cut) report.Add(where + ": " + cut + " is cut short");
                        }
                    }
                }
            }
            finally { LanguageFont.Applier = real; }
            Assert.That(report, Is.Empty, "does not fit in NDMF's font:\n" + string.Join("\n", report));
        }

        sealed class Measured
        {
            public readonly Dictionary<string, string> Failures = new Dictionary<string, string>();
            public readonly HashSet<string> Cut = new HashSet<string>();
            public bool FontsSet;
        }

        /// <summary>A fresh view at this width, every entry's panel, the header with its settings open and the mode row: what the audit finds, and every text cut short.</summary>
        Measured MeasurePanels(AvatarDoc menu, MenuEntry[] entries, float width)
        {
            var result = new Measured();
            var allow = StandardAllowList()
                .Allow(e => e.name == "ad-problems-arrow" || e.name == "ad-status-glyph", "the status row's own leading arrow and icon")
                .Allow(e => e.name == "ad-ready-show" || e.name == "ad-summary-toggle", "the status row's own trailing actions")
                .Allow(e => e.name == "ad-settings-fold-dot" || e.name == "ad-advanced-fold-dot", "a fold's own non default dot");
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                result.FontsSet = view.Children().All(part => part.style.unityFontDefinition.keyword != StyleKeyword.Null);
                if (!view.Header.SettingsFoldOpen) view.Header.ToggleSettingsFold();
                LayoutProbe.Validate(view);
                Audit(result, "the header", () => PanelAudit.Audit(view.Header, view.Header.worldBound.xMax, allow));
                Audit(result, "the mode row", () => PanelAudit.Audit(view.ElementAt(0), view.worldBound.xMax, new PanelAudit.AllowList()));
                CollectCut(result, view);
                foreach (var entry in entries)
                {
                    view.Tree.SelectEntry(entry.id);
                    LayoutProbe.Validate(view);
                    Audit(result, entry.name + "'s panel", () => PanelAudit.Audit(view.Detail, view.Detail.worldBound.xMax, allow));
                    CollectCut(result, view.Detail, entry.name + "'s panel");
                }
            }, width);
            return result;
        }

        static void Audit(Measured result, string panel, System.Action audit)
        {
            try { audit(); }
            catch (AssertionException e) { result.Failures[panel] = e.Message.Split('\n')[0]; }
        }

        /// <summary>
        /// Every visible text that does not show whole: a single line wider than its box, or wrapped text taller than it.
        /// Text inside an input field scrolls instead, and an object field's caption is an object's name: both are left out.
        /// </summary>
        static void CollectCut(Measured result, VisualElement root, string panel = "the view")
        {
            foreach (var text in root.Query<TextElement>().ToList())
            {
                if (string.IsNullOrEmpty(text.text) || !(text.worldBound.width > 0f) || text.resolvedStyle.display == DisplayStyle.None) continue;
                if (text.parent != null && (text.parent.ClassListContains("unity-text-field__input") || text.parent.ClassListContains("unity-base-text-field__input"))) continue;
                if (text.ClassListContains("unity-object-field-display__label")) continue;
                var box = text.contentRect;
                var natural = text.MeasureTextSize(text.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
                bool cut = text.resolvedStyle.whiteSpace == WhiteSpace.NoWrap
                    ? natural.x > box.width + 0.5f
                    : text.MeasureTextSize(text.text, box.width, VisualElement.MeasureMode.Exactly, 0f, VisualElement.MeasureMode.Undefined).y > box.height + 1f;
                if (cut) result.Cut.Add(panel + ": \"" + text.text + "\" (" + (string.IsNullOrEmpty(text.name) ? text.GetType().Name : "#" + text.name) + " in " + PathOf(text)
                    + ", " + box.width.ToString("0.0") + " px wide for text that measures " + natural.x.ToString("0.0") + ")");
            }
        }

        static string PathOf(VisualElement element)
        {
            var names = new List<string>();
            for (var p = element.parent; p != null && names.Count < 4; p = p.parent)
                names.Add(!string.IsNullOrEmpty(p.name) ? "#" + p.name : (p.GetClasses().FirstOrDefault() ?? p.GetType().Name));
            return string.Join(" < ", names);
        }

        /// <summary>NDMF's font for a language through NDMF's own loader, which is private: its public call hands out only the font of the language NDMF shows, and a test cannot change that without saving it.</summary>
        static StyleFontDefinition NdmfFont(string language)
        {
            var load = typeof(LanguagePrefs).GetMethod("TryLoadFontForLanguage", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(load, Is.Not.Null, "NDMF's font loader (LanguagePrefs.TryLoadFontForLanguage) was not found: this test needs updating for the NDMF version in use");
            var font = (StyleFontDefinition)load.Invoke(null, new object[] { language.ToLowerInvariant() });
            if (font.value.fontAsset == null) Assert.Ignore("NDMF finds no font for " + language + " on this system, so the fit in that font cannot be checked here");
            return font;
        }
    }
}
#endif
