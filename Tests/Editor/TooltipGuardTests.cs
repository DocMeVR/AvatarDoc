#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Phase 10 task 2's permanent guard: every labelled control the panels, the windows and the target rows build
    /// carries a tooltip, and every ".tip" string in the localization file is one sentence of plain, non-repeating
    /// text with a matching key on both sides of L.cs.
    /// </summary>
    public class TooltipGuardTests
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

        // ---- the generic sweep -------------------------------------------------------------------------------

        /// <summary>
        /// The short, explicit, reviewable set of controls that legitimately carry no tooltip: an icon-only picker
        /// button (its text is empty), and a remove or clear button whose whole meaning is its "x"/"×" glyph. A
        /// SegmentedChoice's own item buttons are covered by the choice's own tooltip instead of their own.
        /// </summary>
        static bool IsExemptButton(Button button)
        {
            if (button.parent is SegmentedChoice) return true;
            var text = (button.text ?? "").Trim();
            return text.Length == 0 || text == "x" || text == "X" || text == "×";
        }

        static IEnumerable<VisualElement> Descendants(VisualElement root)
        {
            foreach (var child in root.Children())
            {
                yield return child;
                foreach (var d in Descendants(child)) yield return d;
            }
        }

        /// <summary>
        /// A compound control (a Slider or FloatField with its own internal numeric text editor, a
        /// ToolbarSearchField with its own internal TextField) carries one tooltip for the whole thing; its
        /// internal implementation children are not controls of their own and are never checked separately.
        /// </summary>
        static readonly Type[] CompoundFieldTypes =
        {
            typeof(Button), typeof(SegmentedChoice), typeof(TextField), typeof(FloatField), typeof(IntegerField),
            typeof(ObjectField), typeof(Toggle), typeof(Slider), typeof(DropdownField), typeof(ColorField),
            typeof(Vector3Field),
        };

        static bool HasCompoundAncestor(VisualElement element, VisualElement stopAt)
        {
            for (var ancestor = element.parent; ancestor != null && ancestor != stopAt; ancestor = ancestor.parent)
                if (CompoundFieldTypes.Any(t => t.IsInstanceOfType(ancestor))) return true;
            return false;
        }

        /// <summary>
        /// Walks every descendant of root and fails, naming the offending control, when a switch, slider, text or
        /// number field, object field, dropdown or SegmentedChoice has no tooltip. Buttons are checked the same way
        /// unless IsExemptButton says their whole meaning is a glyph.
        /// </summary>
        static void AssertAllTooltipped(VisualElement root, string context)
        {
            Assert.That(root, Is.Not.Null, context + ": nothing was built to scan.");
            foreach (var element in Descendants(root))
            {
                if (HasCompoundAncestor(element, root)) continue;
                switch (element)
                {
                    case Button button:
                        if (IsExemptButton(button)) continue;
                        Assert.That(button.tooltip, Is.Not.Null.And.Not.Empty,
                            $"{context}: Button '{button.name}' (\"{button.text}\") has no tooltip.");
                        break;
                    case SegmentedChoice choice:
                        Assert.That(choice.tooltip, Is.Not.Null.And.Not.Empty,
                            $"{context}: SegmentedChoice '{choice.name}' has no tooltip.");
                        break;
                    case TextField _:
                    case FloatField _:
                    case IntegerField _:
                    case ObjectField _:
                    case Toggle _:
                    case Slider _:
                    case DropdownField _:
                    case ColorField _:
                    case Vector3Field _:
                        Assert.That(element.tooltip, Is.Not.Null.And.Not.Empty,
                            $"{context}: {element.GetType().Name} '{element.name}' has no tooltip.");
                        break;
                }
            }
        }

        // ---- test 1: every entry kind's panel, the shell, and the detached windows --------------------------

        [Test]
        public void EveryLabelledControlInThePanelsHasATooltip()
        {
            var menu = avatar.AddMenu();
            AnimatorController controller = null;
            VRCExpressionsMenu assetMenu = null;
            try
            {
                var ids = BuildOneOfEveryKind(menu, out controller, out assetMenu);
                var view = View(menu);

                ScanShell(view);

                foreach (var id in ids.Values)
                {
                    view.Tree.SelectEntry(id);
                    AssertAllTooltipped(view.Detail, "detail panel for entry " + id);
                }

                foreach (var kind in new[]
                         {
                             RowKind.TriggersGroup, RowKind.OverridesGroup, RowKind.AnimatorsGroup,
                             RowKind.MenuAssetsGroup, RowKind.ParametersGroup, RowKind.Animator, RowKind.MenuAsset,
                         })
                {
                    var row = view.Tree.RowsOfKind(kind).FirstOrDefault();
                    if (row == null) continue;
                    view.Tree.SelectKey(row.Key);
                    AssertAllTooltipped(view.Detail, "detail panel for group row " + kind);
                }

                var existingRow = view.Tree.RowsOfKind(RowKind.Existing).FirstOrDefault(r => r.Node != null);
                if (existingRow != null)
                {
                    view.Tree.SelectKey(existingRow.Key);
                    AssertAllTooltipped(view.Detail, "detail panel for an existing row");
                }

                // The trigger built from When groups: open its contact and its PhysBone condition so the panel
                // embedded there (TouchPanel, shown only for the selected card) gets scanned too.
                view.Tree.SelectEntry(ids["TrigGroups"]);
                view.Detail.SelectCondition(0, 0);
                AssertAllTooltipped(view.Detail, "detail panel for a selected contact condition");
                view.Detail.SelectCondition(1, 1);
                AssertAllTooltipped(view.Detail, "detail panel for a selected PhysBone condition");

                ScanShell(view);
            }
            finally
            {
                if (controller != null) UnityEngine.Object.DestroyImmediate(controller);
                if (assetMenu != null) UnityEngine.Object.DestroyImmediate(assetMenu);
            }

            ScanImportWindow();
            ScanOutfitSetWindow();
            ScanIconCaptureWindow();
            ScanAvatarDocShapesEditor();
        }

        /// <summary>The header, the tree's own toolbar, the summary panel, the lock banner and the wheel: the parts of the shell that are not any one entry's detail panel.</summary>
        void ScanShell(AvatarDoctorView view)
        {
            AssertAllTooltipped(view.Header, "header");
            var toolbar = view.Tree.Q<VisualElement>(className: "ad-toolbar");
            Assert.That(toolbar, Is.Not.Null, "the tree's toolbar must exist to be scanned");
            AssertAllTooltipped(toolbar, "tree toolbar");
            var findBar = view.Tree.Q<VisualElement>("ad-toolbar-find");
            Assert.That(findBar, Is.Not.Null, "the tree's finding bar (search and the tag filter) must exist to be scanned");
            AssertAllTooltipped(findBar, "tree finding bar");
            AssertAllTooltipped(view.Summary, "summary panel");
            var lockBanner = view.Q<VisualElement>("ad-lock-banner");
            if (lockBanner != null) AssertAllTooltipped(lockBanner, "lock banner");

            view.Tree.ClearSelection();
            view.TestMode = true;
            var wheel = view.Wheel;
            AssertAllTooltipped(wheel, "wheel");
        }

        /// <summary>One entry of every kind, every trigger source (including a When-groups trigger covering all four condition kinds), a merged animator, an installed menu asset and a declared parameter, all on one component.</summary>
        Dictionary<string, string> BuildOneOfEveryKind(AvatarDoc menu, out AnimatorController controller, out VRCExpressionsMenu assetMenu)
        {
            var ids = new Dictionary<string, string>();

            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = true });
            hat.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Fixed, explicitOff = true, minWeight = 10f });
            hat.materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 0, material = avatar.MaterialB });
            hat.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Color", type = PropertyValueType.Color, explicitOff = true });
            hat.animations.Add(new AnimationTarget());
            hat.exclusiveGroup = "Outfits";
            hat.parameterName = "CustomHat";
            hat.blendSeconds = 0.5f;
            ids["Hat"] = hat.id;

            var ghost = menu.AddEntry(EntryKind.Toggle, "Ghost");
            ghost.objects.Add(new ObjectTarget { target = avatar.Glasses });
            ghost.hiddenFromMenu = true;
            ids["Ghost"] = ghost.id;

            var eyes = menu.AddEntry(EntryKind.Radial, "Eyes");
            eyes.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", minWeight = 0f, maxWeight = 100f });
            eyes.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Color", type = PropertyValueType.Color });
            eyes.animations.Add(new AnimationTarget());
            ids["Eyes"] = eyes.id;

            var sub = menu.AddEntry(EntryKind.Submenu, "Outfits2");
            ids["Outfits2"] = sub.id;

            var over = menu.AddEntry(EntryKind.Override, "PinBlush");
            over.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 0f });
            ids["PinBlush"] = over.id;

            var preset = menu.AddEntry(EntryKind.Preset, "Combo");
            preset.presetStates.Add(new PresetState { entryId = hat.id, on = true });
            preset.presetParameters.Add(new PresetParameter { name = "ForeignParam", value = 1f });
            ids["Combo"] = preset.id;

            var trigToggle = menu.AddEntry(EntryKind.Trigger, "TrigToggle");
            trigToggle.source = TriggerSource.Toggle;
            trigToggle.sourceToggleId = hat.id;
            trigToggle.objects.Add(new ObjectTarget { target = avatar.Glasses });
            ids["TrigToggle"] = trigToggle.id;

            var trigParam = menu.AddEntry(EntryKind.Trigger, "TrigParam");
            trigParam.source = TriggerSource.Parameter;
            trigParam.sourceParameter = "GestureRight";
            trigParam.condition = ParameterCondition.Greater;
            trigParam.conditionValue = 2f;
            trigParam.objects.Add(new ObjectTarget { target = avatar.Glasses });
            ids["TrigParam"] = trigParam.id;

            var trigLink = menu.AddEntry(EntryKind.Trigger, "TrigLink");
            trigLink.source = TriggerSource.BlendShape;
            trigLink.sourceRenderer = avatar.Body;
            trigLink.sourceShape = "Blush";
            trigLink.sourceThreshold = 50f;
            trigLink.linkMode = BlendShapeLinkMode.Replace;
            trigLink.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Follow, multiplier = 1f });
            ids["TrigLink"] = trigLink.id;

            var pb = avatar.Hat.AddComponent<VRCPhysBone>();
            var trigGroups = menu.AddEntry(EntryKind.Trigger, "TrigGroups");
            trigGroups.touchMode = TouchMode.Hold;
            trigGroups.gateToggleId = hat.id;
            trigGroups.gateToggleOn = true;
            var g0 = new ConditionGroup();
            g0.all.Add(new WhenCondition { kind = WhenConditionKind.Contact, contactAnchor = avatar.Hat.transform, contactTags = new List<string> { "Hand" } });
            g0.all.Add(new WhenCondition { kind = WhenConditionKind.Toggle, sourceToggleId = hat.id, sourceToggleOn = true });
            var g1 = new ConditionGroup();
            g1.all.Add(new WhenCondition { kind = WhenConditionKind.Parameter, sourceParameter = "GestureLeft", condition = ParameterCondition.Equals, conditionValue = 1f });
            g1.all.Add(new WhenCondition { kind = WhenConditionKind.PhysBone, physBone = pb, physBoneEvent = PhysBoneEvent.AngleAbove, physBoneThreshold = 0.5f });
            trigGroups.when.Add(g0);
            trigGroups.when.Add(g1);
            trigGroups.objects.Add(new ObjectTarget { target = avatar.Glasses });
            ids["TrigGroups"] = trigGroups.id;

            var grab = menu.AddEntry(EntryKind.Grabbable, "Grab");
            grab.grabObject = avatar.Glasses;
            grab.grabMenuToggle = true;
            ids["Grab"] = grab.id;

            var trigFlip = menu.AddEntry(EntryKind.Trigger, "TrigFlip");
            trigFlip.source = TriggerSource.PhysBone;
            trigFlip.physBone = pb;
            trigFlip.physBoneEvent = PhysBoneEvent.Grabbed;
            trigFlip.touchMode = TouchMode.FlipToggle;
            trigFlip.flipToggleId = ghost.id;
            ids["TrigFlip"] = trigFlip.id;

            var trigDistance = menu.AddEntry(EntryKind.Trigger, "TrigDistance");
            trigDistance.source = TriggerSource.Contact;
            trigDistance.contactAnchor = avatar.Hat.transform;
            trigDistance.touchMode = TouchMode.FollowDistance;
            trigDistance.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Fixed });
            ids["TrigDistance"] = trigDistance.id;

            controller = new AnimatorController { name = "Merged" };
            controller.AddLayer("Layer1");
            controller.AddParameter("NotSynced", AnimatorControllerParameterType.Bool);
            var merge = AnimatorMerge.Create(controller);
            menu.animators.Add(merge);

            assetMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            assetMenu.name = "AssetMenu";
            assetMenu.controls = new List<VRCExpressionsMenu.Control> { TestAvatar.ToggleControl("AssetToggle", "AssetParam") };
            var install = MenuAssetInstall.Create(assetMenu);
            install.asSubmenu = true;
            menu.menuAssets.Add(install);

            menu.parameters.Add(ParameterDeclaration.Create("Declared1", PlannedType.Bool));

            return ids;
        }

        void ScanImportWindow()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            var window = ScriptableObject.CreateInstance<ImportWindow>();
            try
            {
                window.Init(menu);
                window.CreateGUI();
                AssertAllTooltipped(window.rootVisualElement, "Import window");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        void ScanOutfitSetWindow()
        {
            var menu = avatar.AddMenu();
            var window = ScriptableObject.CreateInstance<OutfitSetWindow>();
            try
            {
                window.Init(menu);
                window.SetName("Outfits");
                window.AddOutfit("Casual");
                window.AddPiece(0, avatar.Hat);
                window.SetAddNoneOption(true);
                window.AddHiddenPiece(avatar.Glasses);
                AssertAllTooltipped(window.rootVisualElement, "Outfit set window");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        void ScanIconCaptureWindow()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var window = ScriptableObject.CreateInstance<IconCaptureWindow>();
            try
            {
                window.Init(menu, hat.id);
                AssertAllTooltipped(window.rootVisualElement, "Icon capture window");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        void ScanAvatarDocShapesEditor()
        {
            var piece = new GameObject("Piece");
            piece.transform.SetParent(avatar.Root.transform);
            var pieceMesh = piece.AddComponent<SkinnedMeshRenderer>();
            var pieceMeshAsset = TestAvatar.QuadWithShapes("Blush");
            pieceMesh.sharedMesh = pieceMeshAsset;
            var shapes = piece.AddComponent<AvatarDocShapes>();
            shapes.follow.Add(new ShapeFollow { sourceMesh = avatar.Body, sourceShape = "Blush", localShape = "Blush" });
            shapes.whileOn.Add(new ShapeWhileOn { mesh = avatar.Body, shape = "Eye_Big", value = 50f });
            UnityEditor.Editor editor = null;
            try
            {
                editor = UnityEditor.Editor.CreateEditor(shapes);
                var shapesEditor = editor as AvatarDocShapesEditor;
                Assert.That(shapesEditor, Is.Not.Null, "AvatarDocShapesEditor must build detached, without a real inspector window");
                var root = shapesEditor.CreateInspectorGUI();
                AssertAllTooltipped(root, "AvatarDoc Shapes inspector");
            }
            finally
            {
                if (editor != null) UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(pieceMeshAsset);
            }
        }

        // ---- test 2: the target rows, which an unattached ListView never materialises on its own ------------

        [Test]
        public void EveryLabelledControlInTheTargetRowsHasATooltip()
        {
            var menu = avatar.AddMenu();

            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            hat.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Fixed, explicitOff = true, minWeight = 10f });
            hat.materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 0, material = avatar.MaterialB });
            hat.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_MadeUpProp", type = PropertyValueType.Float, explicitOff = true });
            hat.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_MadeUpProp", type = PropertyValueType.Color, explicitOff = true });
            hat.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_MadeUpProp", type = PropertyValueType.Vector, explicitOff = true });
            hat.animations.Add(new AnimationTarget());

            var eyes = menu.AddEntry(EntryKind.Radial, "Eyes");
            eyes.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", minWeight = 0f, maxWeight = 100f });
            eyes.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_MadeUpProp", type = PropertyValueType.Float });
            eyes.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_MadeUpProp", type = PropertyValueType.Color });
            eyes.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_MadeUpProp", type = PropertyValueType.Vector });

            var link = menu.AddEntry(EntryKind.Trigger, "Link");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Follow, multiplier = 1f });

            var over = menu.AddEntry(EntryKind.Override, "Pinned");
            over.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 0f });

            var view = View(menu);

            view.Tree.SelectEntry(hat.id);
            AssertAllTooltipped(view.Detail.Q<TargetListView>("ad-objects").RowAt(0), "object target row");
            AssertAllTooltipped(view.Detail.Q<TargetListView>("ad-blendshapes").RowAt(0), "plain blend shape row (Fixed, off value shown)");
            AssertAllTooltipped(view.Detail.Q<TargetListView>("ad-materials").RowAt(0), "material row");
            var properties = view.Detail.Q<TargetListView>("ad-properties");
            for (int i = 0; i < 3; i++) AssertAllTooltipped(properties.RowAt(i), "toggle property row " + i);
            AssertAllTooltipped(view.Detail.Q<TargetListView>("ad-animations").RowAt(0), "animation row");

            view.Tree.SelectEntry(eyes.id);
            AssertAllTooltipped(view.Detail.Q<TargetListView>("ad-blendshapes").RowAt(0), "radial blend shape row");
            var radialProperties = view.Detail.Q<TargetListView>("ad-properties");
            for (int i = 0; i < 3; i++) AssertAllTooltipped(radialProperties.RowAt(i), "radial property row " + i);

            view.Tree.SelectEntry(link.id);
            AssertAllTooltipped(view.Detail.Q<TargetListView>("ad-blendshapes").RowAt(0), "blend shape link row");

            view.Tree.SelectEntry(over.id);
            AssertAllTooltipped(view.Detail.Q<TargetListView>("ad-blendshapes").RowAt(0), "pinned (override) blend shape row");
        }

        // ---- tests 3 and 4: the JSON's own ".tip" text and its bookkeeping ------------------------------------

        static Dictionary<string, string> ReadLocalizationEntries()
        {
            var guids = AssetDatabase.FindAssets("AvatarDoctor.en-US t:TextAsset");
            Assert.That(guids.Length, Is.GreaterThan(0), "the English localization file must exist");
            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var text = File.ReadAllText(path);
            var result = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(text, "\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\""))
            {
                var key = Regex.Unescape(m.Groups[1].Value);
                var value = Regex.Unescape(m.Groups[2].Value);
                result[key] = value;
            }
            return result;
        }

        /// <summary>Sentence-ending periods only: one inside a number such as "0.005" is not a second sentence.</summary>
        static int SentenceCount(string text) => Regex.Matches(text, @"\.(?=\s+[A-Z]|$)").Count;

        [Test]
        public void TooltipsAreOneSentenceAndPlain()
        {
            var entries = ReadLocalizationEntries();
            var tips = entries.Where(e => e.Key.EndsWith(".tip", StringComparison.Ordinal)).ToList();
            Assert.That(tips.Count, Is.GreaterThan(0), "there should be many .tip keys by now");

            foreach (var (key, value) in tips.Select(e => (e.Key, e.Value)))
            {
                Assert.That(value, Is.Not.Null.And.Not.Empty, $"{key} has no text.");
                Assert.That(value, Does.Not.Contain("—"), $"{key} uses an em dash: \"{value}\"");
                Assert.That(value, Does.Not.Contain("–"), $"{key} uses an en dash: \"{value}\"");
                Assert.That(value, Does.Not.Contain(" - "), $"{key} uses a dash as a sentence connector: \"{value}\"");
                Assert.That(value.TrimEnd(), Does.EndWith("."), $"{key} does not end with a full stop: \"{value}\"");
                Assert.That(SentenceCount(value), Is.LessThanOrEqualTo(2), $"{key} is more than two sentences: \"{value}\"");

                var baseKey = key.Substring(0, key.Length - ".tip".Length);
                if (entries.TryGetValue(baseKey, out var label))
                {
                    var normalizedTip = value.Trim().TrimEnd('.').ToLowerInvariant();
                    var normalizedLabel = label.Trim().ToLowerInvariant();
                    Assert.That(normalizedTip, Is.Not.EqualTo(normalizedLabel), $"{key} only repeats its label \"{label}\".");
                }
            }
        }

        [Test]
        public void EveryTipKeyHasItsControlKey()
        {
            var entries = ReadLocalizationEntries();
            var jsonTipKeys = new HashSet<string>(entries.Keys.Where(k => k.EndsWith(".tip", StringComparison.Ordinal)));
            var codeKeys = new HashSet<string>(L.KnownKeys.Concat(L.UiKeys));
            var codeTipKeys = new HashSet<string>(codeKeys.Where(k => k.EndsWith(".tip", StringComparison.Ordinal)));

            var orphanInJson = jsonTipKeys.Except(codeTipKeys).ToList();
            Assert.That(orphanInJson, Is.Empty, "these .tip keys are in the JSON but missing from L.cs's KnownKeys or UiKeys: " + string.Join(", ", orphanInJson));

            var orphanInCode = codeTipKeys.Except(jsonTipKeys).ToList();
            Assert.That(orphanInCode, Is.Empty, "these .tip keys are listed in L.cs but missing from the JSON: " + string.Join(", ", orphanInCode));
        }
    }
}
#endif
