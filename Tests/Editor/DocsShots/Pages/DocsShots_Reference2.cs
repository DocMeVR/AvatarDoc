#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The pictures of the last seventeen Reference pages: overrides, merged animators, menu assets and declared
    /// parameters, presets, outfit sets, AvatarDoc Shapes, the Convert window, the rows Convert reads, the icon
    /// capture window, the Add to AvatarDoc window, the Copy to another avatar window, the Gesture faces window, the
    /// AvatarDoc window and the wheel, the scene preview, the summary and the lock, the language list and the limits. A window is shot at its own width; the inspector at DocsShot.InspectorWidth.
    /// DocsShotSetupTests runs every setup without the screen read.
    /// </summary>
    [Explicit, Category("DocsShots")]
    public class DocsShots_Reference2
    {
        /// <summary>The names the shots give unnamed elements, so the markers can find them.</summary>
        internal const string ImportLateNoteName = "docs-import-late-note";
        internal const string LimitsRowName = "docs-limits-row";
        internal const string MoreRowName = "docs-more-row";
        internal const string LimitsSlotsName = "docs-limits-slots";

        /// <summary>The AvatarDoc window has no size of its own; it is shown wide enough for the tree, the wheel and the panel side by side.</summary>
        internal const float WindowWidth = 900f;

        [Test] public void OverridesOverview() { using (var avatar = DocsAvatar.Create()) OverridesOverview(avatar).Take(); }
        [Test] public void MergedAnimatorsOverview() { using (var avatar = DocsAvatar.Create()) MergedAnimatorsOverview(avatar).Take(); }
        [Test] public void MenuAssetOverview() { using (var avatar = DocsAvatar.Create()) MenuAssetOverview(avatar).Take(); }
        [Test] public void ParametersOverview() { using (var avatar = DocsAvatar.Create()) ParametersOverview(avatar).Take(); }
        [Test] public void PresetsOverview() { using (var avatar = DocsAvatar.Create()) PresetsOverview(avatar).Take(); }
        [Test] public void OutfitSetsOverview() { using (var avatar = DocsAvatar.Create()) OutfitSetsOverview(avatar).Take(); }
        [Test] public void AvatarDocShapesOverview() { using (var avatar = DocsAvatar.Create()) AvatarDocShapesOverview(avatar).Take(); }
        [Test] public void ConvertWindowOverview() { using (var avatar = DocsAvatar.Create()) ConvertWindowOverview(avatar).Take(); }
        [Test] public void ImportRows() { using (var avatar = DocsAvatar.Create()) ImportRows(avatar).Take(); }
        [Test] public void IconCaptureOverview() { using (var avatar = DocsAvatar.Create()) IconCaptureOverview(avatar).Take(); }
        [Test] public void AddWindowOverview() { using (var avatar = DocsAvatar.Create()) AddWindowOverview(avatar).Take(); }
        [Test] public void CopyWindowOverview() { using (var avatar = DocsAvatar.Create()) CopyWindowOverview(avatar).Take(); }
        [Test] public void GestureFacesWindowOverview() { using (var avatar = DocsAvatar.Create()) GestureFacesWindowOverview(avatar).Take(); }
        [Test] public void WindowOverview() { using (var avatar = DocsAvatar.Create()) WindowOverview(avatar).Take(); }
        [Test] public void WheelTestMode() { using (var avatar = DocsAvatar.Create()) WheelTestMode(avatar).Take(); }
        [Test] public void ScenePreviewOverview() { using (var avatar = DocsAvatar.Create()) ScenePreviewOverview(avatar).Take(); }
        [Test] public void SummaryOverview() { using (var avatar = DocsAvatar.Create()) SummaryOverview(avatar).Take(); }
        [Test] public void LockedOverview() { using (var avatar = DocsAvatar.Create()) LockedOverview(avatar).Take(); }
        [Test] public void LanguagesPicker() { using (var avatar = DocsAvatar.Create()) LanguagesPicker(avatar).Take(); }
        [Test] public void LimitsMore() { using (var avatar = DocsAvatar.Create()) LimitsMore(avatar).Take(); }

        /// <summary>
        /// A No tongue override that holds the face's Tongue_Out at 0, with two clips of the avatar's FX controller that
        /// move it: 1 Name, 2 the Builds line, 3 Capture, 4 the shape, 5 Pinned value, 6 the list of clips that animate
        /// it, 7 the greyed out preview row, 8 the Advanced fold.
        /// </summary>
        internal static DocsShotSpec OverridesOverview(DocsAvatar avatar)
        {
            var added = new List<Object>();
            try
            {
                DocsShots_Guides2.AddFxShapeClip(avatar, "FT Tongue Out", "Tongue_Out", added);
                DocsShots_Guides2.AddFxShapeClip(avatar, "Gesture Silly Face", "Tongue_Out", added);
                var menu = avatar.AddMenu();
                var rule = menu.AddEntry(EntryKind.Override, "No tongue");
                rule.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Tongue_Out", weight = 0f });
                var spec = DocsShots_Guides1.PanelShot(menu, rule, "overview",
                    ("ad-name", 1), ("ad-builds-line", 2), ("ad-shape-set-capture", 3), ("shape", 4), ("weight", 5), ("ad-override-usage", 6), ("ad-preview-choice-disabled", 7), ("ad-advanced-fold", 8));
                spec.Page = "reference-overrides";
                spec.Release = () => DocsShots_Guides2.DestroyAll(added);
                return spec;
            }
            catch
            {
                DocsShots_Guides2.DestroyAll(added);
                throw;
            }
        }

        /// <summary>
        /// A merged Sparkles FX controller of two layers and two parameters, where a Glow toggle already gives the first
        /// parameter a menu control: 1 Controller, 2 Path root, 3 Match write defaults, 4 Platform, 5 the layer list, 6 the
        /// parameter list, 7 who declares the first parameter, 8 the Add control button of the other.
        /// </summary>
        internal static DocsShotSpec MergedAnimatorsOverview(DocsAvatar avatar)
        {
            var controller = new AnimatorController { name = "Sparkles FX" };
            try
            {
                controller.AddParameter("Sparkles", AnimatorControllerParameterType.Bool);
                controller.AddParameter("Sparkles Hue", AnimatorControllerParameterType.Float);
                controller.AddLayer("Sparkles");
                controller.AddLayer("Sparkles Colour");
                var menu = avatar.AddMenu();
                var glow = menu.AddEntry(EntryKind.Toggle, "Glow");
                glow.objects.Add(new ObjectTarget { target = avatar.Glasses });
                glow.parameterName = "Sparkles";
                var merge = AnimatorMerge.Create(controller);
                menu.animators.Add(merge);
                var spec = DocsShots_Guides2.KeyPanelShot(menu, merge.id, "overview",
                    ("ad-animator-controller", 1), ("ad-animator-path-root", 2), ("ad-animator-match-write-defaults", 3), ("ad-animator-platform", 4),
                    ("ad-animator-layers", 5), ("ad-animator-parameters", 6), ("ad-animator-param-declared", 7), ("ad-animator-add-control", 8));
                spec.Page = "reference-merged-animators";
                spec.Release = () => DocsShots_Guides2.DestroyController(controller);
                return spec;
            }
            catch
            {
                DocsShots_Guides2.DestroyController(controller);
                throw;
            }
        }

        /// <summary>
        /// A finished Sparkles menu of a toggle on Sparkles and a radial on Sparkles Hue, installed as a submenu, where the
        /// component declares Sparkles Hue and nothing declares Sparkles: 1 Menu asset, 2 Install path, 3 Place, 4 Name,
        /// 5 Platform, 6 the parameter list, 7 who declares the first parameter, 8 its Add declaration button.
        /// </summary>
        internal static DocsShotSpec MenuAssetOverview(DocsAvatar avatar)
        {
            var asset = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            try
            {
                asset.name = "Sparkles menu";
                asset.controls = new List<VRCExpressionsMenu.Control>
                {
                    TestAvatar.ToggleControl("Sparkles", "Sparkles"),
                    new VRCExpressionsMenu.Control
                    {
                        name = "Hue",
                        type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = "" },
                        subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = "Sparkles Hue" } },
                        labels = Array.Empty<VRCExpressionsMenu.Control.Label>(),
                    },
                };
                var menu = avatar.AddMenu();
                var install = MenuAssetInstall.Create(asset);
                menu.menuAssets.Add(install);
                var hue = ParameterDeclaration.Create("Sparkles Hue", PlannedType.Float);
                hue.defaultValue = 0.5f;
                menu.parameters.Add(hue);
                var spec = DocsShots_Guides2.KeyPanelShot(menu, install.id, "menu-asset",
                    ("ad-menu-asset-menu", 1), ("ad-menu-asset-path", 2), ("ad-menu-asset-place", 3), ("ad-menu-asset-name", 4),
                    ("ad-menu-asset-platform", 5), ("ad-menu-asset-parameters", 6), ("ad-menu-asset-param-declared", 7), ("ad-menu-asset-declare-Sparkles", 8));
                spec.Page = "reference-menu-assets-and-parameters";
                spec.Release = () => Object.DestroyImmediate(asset);
                return spec;
            }
            catch
            {
                Object.DestroyImmediate(asset);
                throw;
            }
        }

        /// <summary>
        /// The Parameters table with a Bool Sparkles and a Float Sparkles Hue that starts at 0.5, both saved and synced:
        /// 1 the first name, 2 its type, 3 its default value, 4 Saved, 5 Synced, 6 its remove button, 7 the total, 8 the
        /// + Parameter button.
        /// </summary>
        internal static DocsShotSpec ParametersOverview(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            menu.parameters.Add(ParameterDeclaration.Create("Sparkles", PlannedType.Bool));
            var hue = ParameterDeclaration.Create("Sparkles Hue", PlannedType.Float);
            hue.defaultValue = 0.5f;
            menu.parameters.Add(hue);
            var spec = DocsShots_Guides2.KeyPanelShot(menu, TreeRow.ParametersRowKey, "parameters",
                ("ad-parameter-name-0", 1), ("ad-parameter-type-0", 2), ("ad-parameter-default-0", 3), ("ad-parameter-saved-0", 4),
                ("ad-parameter-synced-0", 5), ("ad-parameter-remove-0", 6), ("ad-parameters-total", 7), ("ad-parameters-add", 8));
            spec.Page = "reference-menu-assets-and-parameters";
            return spec;
        }

        /// <summary>
        /// A Hoodie look preset that takes the hat off, puts the hoodie of the Outfits group on, sets the Smile radial
        /// halfway, has one row with no control yet, and sets the declared parameter Sparkles: 1 the Sets + Add, 2 the
        /// first row's control, 3 its On and Off, 4 the radial's slider, 5 From preview, 6 the Other parameters + Add,
        /// 7 the parameter's name, 8 the preview row.
        /// </summary>
        internal static DocsShotSpec PresetsOverview(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var jacket = menu.AddEntry(EntryKind.Toggle, "Jacket", outfits.id);
            jacket.objects.Add(new ObjectTarget { target = avatar.Jacket });
            jacket.exclusiveGroup = "Outfits";
            jacket.defaultOn = true;
            var hoodie = menu.AddEntry(EntryKind.Toggle, "Hoodie", outfits.id);
            hoodie.objects.Add(new ObjectTarget { target = avatar.Hoodie });
            hoodie.exclusiveGroup = "Outfits";
            var smile = menu.AddEntry(EntryKind.Radial, "Smile");
            smile.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Smile", minWeight = 0f, maxWeight = 100f });
            menu.parameters.Add(ParameterDeclaration.Create("Sparkles", PlannedType.Bool));
            var look = menu.AddEntry(EntryKind.Preset, "Hoodie look");
            look.presetStates.Add(new PresetState { entryId = hat.id, on = false });
            look.presetStates.Add(new PresetState { entryId = hoodie.id, on = true });
            look.presetStates.Add(new PresetState { entryId = smile.id, value = 0.5f });
            look.presetStates.Add(new PresetState { entryId = "", on = true });
            look.presetParameters.Add(new PresetParameter { name = "Sparkles", value = 1f });
            var spec = DocsShots_Guides1.PanelShot(menu, look, "overview",
                ("ad-preset-add-state", 1), ("ad-preset-control", 2), ("ad-preset-on", 3), ("ad-preset-value", 4),
                ("ad-preset-from-preview", 5), ("ad-preset-add-parameter", 6), ("ad-preset-param-name", 7), ("ad-preview-choice", 8));
            spec.Page = "reference-presets";
            return spec;
        }

        /// <summary>
        /// The Outfit set window at the width it opens at, named Outfits, in One at a time with a None option, Jacket,
        /// Hoodie and Shoes as one outfit each and the Hat hidden while any outfit is on: 1 Set name, 2 Mode, 3 Add a
        /// None option, 4 the outfit cards, 5 + Outfit, 6 Hidden while any outfit is on, 7 Capture icons, 8 Create outfit
        /// set. Nothing is built.
        /// </summary>
        internal static DocsShotSpec OutfitSetsOverview(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var window = ScriptableObject.CreateInstance<OutfitSetWindow>();
            try
            {
                window.Init(menu);
                window.SetName("Outfits");
                foreach (var piece in new[] { avatar.Jacket, avatar.Hoodie, avatar.Shoes })
                {
                    window.AddOutfit(piece.name);
                    if (!window.AddPiece(window.OutfitCount - 1, piece))
                        throw new InvalidOperationException("The outfit set window refused " + piece.name + ": " + window.LastStatus);
                }
                window.SetAddNoneOption(true);
                window.AddHiddenPiece(avatar.Hat);
            }
            catch
            {
                Object.DestroyImmediate(window);
                throw;
            }
            return new DocsShotSpec
            {
                Page = "reference-outfit-sets",
                Name = "overview",
                View = window.rootVisualElement,
                Width = OutfitSetWindow.MinWidth,
                Markers = new[]
                {
                    ("ad-outfit-name", 1), ("ad-outfit-mode", 2), ("ad-outfit-none", 3), ("ad-outfit-list", 4),
                    ("ad-outfit-add", 5), ("ad-outfit-hidden", 6), ("ad-outfit-capture", 7), ("ad-outfit-create", 8),
                },
                Release = () => Object.DestroyImmediate(window),
            };
        }

        /// <summary>
        /// The AvatarDoc Shapes inspector on a Sweater piece that has its own mesh, with a Chest_Big follow row of the
        /// body and a Shrink_Feet while-on row held at 100: 1 the follow row's source mesh, 2 its source shape, 3 On this
        /// piece, 4 Add, 5 Add matching shapes, 6 the while-on row's mesh, 7 Value, 8 Platform. Unity's own component
        /// header is not part of the picture.
        /// </summary>
        internal static DocsShotSpec AvatarDocShapesOverview(DocsAvatar avatar)
        {
            var mesh = TestAvatar.QuadWithShapes("Chest_Big");
            mesh.name = "Sweater";
            UnityEditor.Editor editor = null;
            try
            {
                var sweater = DocsShots_Guides2.Child(avatar, "Sweater");
                var renderer = sweater.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.sharedMaterials = new[] { avatar.Cloth };
                avatar.AddMenu().AddEntry(EntryKind.Toggle, "Sweater").objects.Add(new ObjectTarget { target = sweater });
                var shapes = sweater.AddComponent<AvatarDocShapes>();
                shapes.follow.Add(new ShapeFollow { sourceMesh = avatar.Body, sourceShape = "Chest_Big" });
                shapes.whileOn.Add(new ShapeWhileOn { mesh = avatar.Body, shape = "Shrink_Feet", value = 100f });
                AvatarPlanCache.Invalidate();
                editor = UnityEditor.Editor.CreateEditor(shapes);
                var made = editor;
                return new DocsShotSpec
                {
                    Page = "reference-avatardoc-shapes",
                    Name = "overview",
                    View = new InspectorElement(editor),
                    Markers = new[]
                    {
                        ("ad-shapes-follow-mesh-0", 1), ("ad-shapes-follow-shape-0", 2), ("ad-shapes-follow-local-0", 3), ("ad-shapes-add-follow", 4),
                        ("ad-shapes-add-matching", 5), ("ad-shapes-while-on-mesh-0", 6), ("ad-shapes-while-on-value-0", 7), ("ad-shapes-platform", 8),
                    },
                    Release = () =>
                    {
                        Object.DestroyImmediate(made);
                        Object.DestroyImmediate(mesh);
                    },
                };
            }
            catch
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(mesh);
                throw;
            }
        }

        /// <summary>
        /// The Convert window the guide shows, at the width it opens at, for two VRCFury toggles, a Modular Avatar toggle
        /// where DOCME_MA is defined, and the avatar's own Sit and Wave: 1 All, 2 None, 3 the first row, 4 the first row
        /// that cannot be converted, 5 Remove originals, 6 the Convert button. The markers do not depend on DOCME_MA.
        /// </summary>
        internal static DocsShotSpec ConvertWindowOverview(DocsAvatar avatar)
        {
            var spec = DocsShots_Guides2.ConvertWindow(avatar);
            spec.Page = "reference-convert-window";
            spec.Name = "overview";
            spec.Markers = new[] { ("ad-import-all", 1), ("ad-import-none", 2), ("ad-import-row", 3), ("ad-unsupported", 4), ("ad-import-remove", 5), ("ad-import-run", 6) };
            return spec;
        }

        /// <summary>
        /// The tree beside a Hat toggle of its own with the rows other tools add, the VRCFury Glasses row selected: 1 a
        /// VRCFury chip, 2 a Modular Avatar chip, then the note that the item is applied at build and In menu. Without
        /// DOCME_MA the Modular Avatar chip is dropped and the later numbers shift down by one, so capture with it defined
        /// to match the page. The chips and the note have no names, so the arrange step names them.
        /// </summary>
        internal static DocsShotSpec ImportRows(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            DocsShots_Guides2.AddOtherToolItems(avatar);
            var markers = new List<(string, int)> { (DocsShots_Guides2.VrcFuryChipName, 1) };
#if DOCME_MA
            markers.Add((DocsShots_Guides2.ModularAvatarChipName, 2));
#endif
            markers.Add((ImportLateNoteName, markers.Count + 1));
            markers.Add(("ad-existing-in-menu", markers.Count + 1));
            var spec = DocsShots_Guides2.TreeAndPanelShot(menu, view =>
            {
                var glasses = view.Tree.RowsOfKind(RowKind.Foreign).FirstOrDefault(r => r.Name == "Glasses")
                    ?? throw new InvalidOperationException("The tree shows no VRCFury row named Glasses.");
                view.Tree.RevealKey(glasses.Key);
                LayoutProbe.SettleLists(view);
                DocsShots_Guides2.NameChip(view.Tree, L.Text("ad.ui.chip.vrcfury"), DocsShots_Guides2.VrcFuryChipName);
#if DOCME_MA
                DocsShots_Guides2.NameChip(view.Tree, L.Text("ad.ui.chip.modular_avatar"), DocsShots_Guides2.ModularAvatarChipName);
#endif
                DocsShots_Reference1.NameNote(view.Detail, string.Format(L.Text("ad.ui.existing.late_note"), view.Tree.Selected.SourceChip), ImportLateNoteName);
            }, "rows", markers.ToArray());
            spec.Page = "reference-import-window";
            return spec;
        }

        /// <summary>
        /// The Add to AvatarDoc window at the width it opens at, as a right click on the Face's Smile slider opens it with
        /// the slider at 100, the Face submenu chosen: 1 Shape, 2 New toggle, New radial and Add to, 3 Name, 4 Submenu,
        /// 5 On value, 6 Off value, 7 putting the value back, 8 Add.
        /// </summary>
        internal static DocsShotSpec AddWindowOverview(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var face = menu.AddEntry(EntryKind.Submenu, "Face");
            avatar.Face.SetBlendShapeWeight(avatar.Face.sharedMesh.GetBlendShapeIndex("Smile"), 100f);
            var draft = AddToAvatarDoc.ForShape(avatar.Face, "Smile");
            draft.parentId = face.id;
            var window = ScriptableObject.CreateInstance<AddToAvatarDocWindow>();
            try
            {
                window.Init(draft);
            }
            catch
            {
                Object.DestroyImmediate(window);
                throw;
            }
            return new DocsShotSpec
            {
                Page = "reference-add-window",
                Name = "overview",
                View = window.rootVisualElement,
                Width = AddToAvatarDocWindow.OpeningWidth,
                Markers = new[]
                {
                    ("ad-add-shape", 1), ("ad-add-make", 2), ("ad-add-name", 3), ("ad-add-submenu", 4),
                    ("ad-add-on", 5), ("ad-add-off", 6), ("ad-add-put-back", 7), ("ad-add-run", 8),
                },
                Release = () => Object.DestroyImmediate(window),
            };
        }

        /// <summary>
        /// The Gesture faces window at the width it opens at, for an avatar with lip tracking: a smile on Fist for
        /// both hands, paused by face tracking, and a wide grin on Victory for the right hand, which wins while held:
        /// 1 Face mesh, 2 Face tracking, 3 Both hands, 4 Fist strength, 5 Menu switch, 6 the Fist row's hand choice,
        /// 7 its Capture, 8 its While tracked choice. Nothing is made: the picture shows the faces before Make.
        /// </summary>
        internal static DocsShotSpec GestureFacesWindowOverview(DocsAvatar avatar)
        {
            var parameters = avatar.Test.ExpressionParameters.parameters.ToList();
            parameters.Add(new VRCExpressionParameters.Parameter { name = "LipTrackingActive", valueType = VRCExpressionParameters.ValueType.Bool, saved = false, networkSynced = true });
            avatar.Test.ExpressionParameters.parameters = parameters.ToArray();
            AvatarPlanCache.Invalidate();
            var menu = avatar.AddMenu();
            var draft = GestureFaces.Read(menu);
            var fist = draft.rows.First(r => r.gesture == GestureFaces.Fist);
            fist.shapes.Add(new GestureShape { renderer = avatar.Face, shape = "Smile", weight = 100f });
            fist.shapes.Add(new GestureShape { renderer = avatar.Face, shape = "Smile_Eyes", weight = 80f });
            var victory = draft.rows.First(r => r.gesture == 4);
            victory.hand = GestureHand.Right;
            victory.tracking = GestureTracking.Wins;
            victory.shapes.Add(new GestureShape { renderer = avatar.Face, shape = "Mouth_Smile_Wide", weight = 100f });
            var window = ScriptableObject.CreateInstance<GestureFacesWindow>();
            try
            {
                window.Init(draft);
            }
            catch
            {
                Object.DestroyImmediate(window);
                throw;
            }
            return new DocsShotSpec
            {
                Page = "reference-gesture-faces-window",
                Name = "overview",
                View = window.rootVisualElement,
                Width = GestureFacesWindow.OpeningWidth,
                Height = 812f,
                Markers = new[]
                {
                    ("ad-gestures-mesh", 1), ("ad-gestures-tracking", 2), ("ad-gestures-order", 3), ("ad-gestures-fist", 4),
                    ("ad-gestures-switch", 5), ("ad-gestures-hand", 6), ("ad-gestures-capture", 7), ("ad-gestures-row-tracking", 8),
                },
                Release = () => Object.DestroyImmediate(window),
            };
        }

        /// <summary>
        /// The Copy to another avatar window at the width it opens at, copying the menu of Avatar (3) to its next version,
        /// Avatar (4), which has an AvatarDoc of its own, no Glasses and a face without Tongue_Out: 1 To avatar, 2 Goes
        /// on, 3 Replace it or Add another, 4 the object it did not find, 5 Use instead, 6 the shape that avatar lacks,
        /// 7 Copy. The window lists only these two avatars, never the ones of the open scene.
        /// </summary>
        internal static DocsShotSpec CopyWindowOverview(DocsAvatar avatar)
        {
            avatar.Root.name = "Avatar (3)";
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            menu.AddEntry(EntryKind.Toggle, "Glasses").objects.Add(new ObjectTarget { target = avatar.Glasses });
            menu.AddEntry(EntryKind.Toggle, "Smile").blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Smile", weight = 100f });
            menu.AddEntry(EntryKind.Toggle, "Tongue").blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Tongue_Out", weight = 100f });
            var next = Object.Instantiate(avatar.Root);
            next.name = "Avatar (4)";
            Object.DestroyImmediate(next.transform.Find("Glasses").gameObject);
            var face = TestAvatar.QuadWithShapes(DocsAvatar.FaceShapes.Where(s => s != "Tongue_Out").ToArray());
            next.transform.Find("Face").GetComponent<SkinnedMeshRenderer>().sharedMesh = face;
            var avatars = MenuCopy.SceneAvatars;
            MenuCopy.SceneAvatars = () => new[] { avatar.Root.transform, next.transform };
            CopyMenuWindow window = null;
            void Release()
            {
                if (window != null) Object.DestroyImmediate(window);
                MenuCopy.SceneAvatars = avatars;
                Object.DestroyImmediate(next);
                Object.DestroyImmediate(face);
            }
            try
            {
                window = ScriptableObject.CreateInstance<CopyMenuWindow>();
                window.Init(new CopyDraft { source = menu, target = next.transform });
            }
            catch
            {
                Release();
                throw;
            }
            return new DocsShotSpec
            {
                Page = "reference-copy-window",
                Name = "overview",
                View = window.rootVisualElement,
                Width = CopyMenuWindow.OpeningWidth,
                Markers = new[]
                {
                    ("ad-copy-target", 1), ("ad-copy-goes-on", 2), ("ad-copy-where", 3), ("ad-copy-missing-text-0", 4),
                    ("ad-copy-pick-0", 5), ("ad-copy-check-0", 6), ("ad-copy-run", 7),
                },
                Release = Release,
            };
        }

        /// <summary>
        /// The Capture icon window at its smallest width, framed on the Hat of a Hat toggle with a transparent background. The
        /// Hat wears a green sphere for the shot, because the test avatar's own hat is a flat quad that draws as grey:
        /// 1 the preview, 2 Background, 3 Fit, 4 Fill, 5 Save icon. The window gets a frame of its own, because the Scene
        /// view of the machine that takes the picture is not part of it.
        /// </summary>
        internal static DocsShotSpec IconCaptureOverview(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var hatRenderer = avatar.Hat.GetComponent<MeshRenderer>();
            var hatFilter = avatar.Hat.GetComponent<MeshFilter>();
            var previousMesh = hatFilter.sharedMesh;
            var previousMaterials = hatRenderer.sharedMaterials;
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hatFilter.sharedMesh = sphere.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(sphere);
            var hatMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.25f, 0.62f, 0.38f) };
            hatRenderer.sharedMaterials = new[] { hatMaterial };
            var window = ScriptableObject.CreateInstance<IconCaptureWindow>();
            try
            {
                var frame = IconCapture.Fit(
                    new IconCapture.Frame { Position = avatar.Hat.transform.position + new Vector3(0f, 0f, -3f), Rotation = Quaternion.identity, FieldOfView = 40f },
                    IconCapture.BoundsOf(new Renderer[] { avatar.Hat.GetComponent<MeshRenderer>() }));
                window.FrameSource = () => frame;
                window.Init(menu, hat.id);
            }
            catch
            {
                Object.DestroyImmediate(window);
                RestoreHat(hatFilter, hatRenderer, previousMesh, previousMaterials, hatMaterial);
                throw;
            }
            return new DocsShotSpec
            {
                Page = "reference-icon-capture",
                Name = "overview",
                View = window.rootVisualElement,
                Width = IconCaptureWindow.MinWidth,
                Markers = new[] { ("ad-capture-preview", 1), ("ad-capture-background", 2), ("ad-capture-fit", 3), ("ad-capture-fill", 4), ("ad-capture-save", 5) },
                Release = () =>
                {
                    Object.DestroyImmediate(window);
                    RestoreHat(hatFilter, hatRenderer, previousMesh, previousMaterials, hatMaterial);
                },
            };
        }

        static void RestoreHat(MeshFilter filter, MeshRenderer renderer, Mesh mesh, Material[] materials, Material own)
        {
            if (filter != null) filter.sharedMesh = mesh;
            if (renderer != null) renderer.sharedMaterials = materials;
            Object.DestroyImmediate(own);
        }

        /// <summary>
        /// The view the AvatarDoc window shows, built the way the window builds it for the selected avatar, with the Jacket
        /// of an Outfits submenu selected: 1 the mode row, 2 the toolbar, 3 the tree, 4 the wheel, 5 the panel.
        /// </summary>
        internal static DocsShotSpec WindowOverview(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var jacket = menu.AddEntry(EntryKind.Toggle, "Jacket", outfits.id);
            jacket.objects.Add(new ObjectTarget { target = avatar.Jacket });
            jacket.exclusiveGroup = "Outfits";
            jacket.defaultOn = true;
            var hoodie = menu.AddEntry(EntryKind.Toggle, "Hoodie", outfits.id);
            hoodie.objects.Add(new ObjectTarget { target = avatar.Hoodie });
            hoodie.exclusiveGroup = "Outfits";
            var shoes = menu.AddEntry(EntryKind.Toggle, "Shoes", outfits.id);
            shoes.objects.Add(new ObjectTarget { target = avatar.Shoes });
            var smile = menu.AddEntry(EntryKind.Radial, "Smile");
            smile.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Smile", minWeight = 0f, maxWeight = 100f });

            var serialized = new SerializedObject(menu);
            try
            {
                var tips = new MemoryTipStore();
                foreach (var kind in TipKinds.All) tips.Dismiss(kind);
                var view = new AvatarDoctorView(serialized, menu, wide: true, viewPrefs: new MemoryViewPrefs()) { TipStore = tips };
                view.Refresh();
                return new DocsShotSpec
                {
                    Page = "reference-window-and-wheel",
                    Name = "window",
                    View = view,
                    Width = WindowWidth,
                    Arrange = root =>
                    {
                        var shown = (AvatarDoctorView)root;
                        shown.Tree.RevealEntry(jacket.id);
                        LayoutProbe.SettleLists(shown);
                    },
                    Markers = new[] { ("ad-mode-row", 1), ("ad-toolbar-actions", 2), ("ad-tree", 3), ("ad-wheel", 4), ("ad-detail", 5) },
                    Release = () => serialized.Dispose(),
                };
            }
            catch
            {
                serialized.Dispose();
                throw;
            }
        }

        /// <summary>
        /// The toolbar, the tree and the wheel in Test mode inside an Outfits submenu, with the Hoodie previewed on and a
        /// Glasses toggle hidden from the menu: 1 the Wheel button, 2 the crumb back to the top, 3 Select and Test, 4 the
        /// ring, 5 Reset, 6 the Not in the menu strip. The previews are cleared again in Release.
        /// </summary>
        internal static DocsShotSpec WheelTestMode(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var jacket = menu.AddEntry(EntryKind.Toggle, "Jacket", outfits.id);
            jacket.objects.Add(new ObjectTarget { target = avatar.Jacket });
            jacket.exclusiveGroup = "Outfits";
            jacket.defaultOn = true;
            var hoodie = menu.AddEntry(EntryKind.Toggle, "Hoodie", outfits.id);
            hoodie.objects.Add(new ObjectTarget { target = avatar.Hoodie });
            hoodie.exclusiveGroup = "Outfits";
            var shoes = menu.AddEntry(EntryKind.Toggle, "Shoes", outfits.id);
            shoes.objects.Add(new ObjectTarget { target = avatar.Shoes });
            var glasses = menu.AddEntry(EntryKind.Toggle, "Glasses", outfits.id);
            glasses.objects.Add(new ObjectTarget { target = avatar.Glasses });
            glasses.defaultOn = true;
            glasses.hiddenFromMenu = true;
            return new DocsShotSpec
            {
                Page = "reference-window-and-wheel",
                Name = "wheel",
                View = DocsShot.Inspector(menu),
                Arrange = root =>
                {
                    var view = root.Q<AvatarDoctorView>();
                    view.Q("ad-mode-row").style.display = DisplayStyle.None;
                    view.Header.style.display = DisplayStyle.None;
                    view.Detail.style.display = DisplayStyle.None;
                    view.Tree.RevealEntry(jacket.id);
                    PreviewState.SetOn(hoodie, view.Plan.Tree);
                    DocsShots_Guides2.ShowWheel(view);
                    view.TestMode = true;
                    view.Wheel.RefreshWedgeStates();
                },
                Markers = new[] { ("ad-wheel-toggle", 1), ("ad-crumb", 2), ("ad-wheel-mode", 3), ("ad-ring", 4), ("ad-wheel-reset", 5), ("ad-wheel-not-in-menu-label", 6) },
                Release = PreviewState.ClearAll,
            };
        }

        /// <summary>
        /// The header and the panel of a Hat toggle with its scene preview set to On: 1 the Default, On and Off choice,
        /// 2 Reset preview. The header's note that NDMF's previews are off is not marked, because it depends on the
        /// machine that takes the picture. The Hat's preview is cleared again in Release.
        /// </summary>
        internal static DocsShotSpec ScenePreviewOverview(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            return new DocsShotSpec
            {
                Page = "reference-scene-preview",
                Name = "overview",
                View = DocsShot.Inspector(menu),
                Arrange = root =>
                {
                    var view = root.Q<AvatarDoctorView>();
                    view.Tree.RevealEntry(hat.id);
                    view.Q("ad-mode-row").style.display = DisplayStyle.None;
                    view.Left.style.display = DisplayStyle.None;
                    PreviewState.Set(hat.id, 1f);
                    view.Header.RefreshPreviewIndicator();
                    view.Detail.RefreshPreview();
                },
                Markers = new[] { ("ad-preview-choice", 1), ("ad-preview-reset", 2) },
                Release = () => PreviewState.Clear(hat.id),
            };
        }

        /// <summary>
        /// The header and the open Summary of a menu of a Hat toggle and a Smile radial, in Plain: 1 the Summary button, 2
        /// Plain and Markdown, 3 the text, 4 Copy, 5 Lock, 6 Note for users.
        /// </summary>
        internal static DocsShotSpec SummaryOverview(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            var smile = menu.AddEntry(EntryKind.Radial, "Smile");
            smile.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Smile", minWeight = 0f, maxWeight = 100f });
            return new DocsShotSpec
            {
                Page = "reference-summary-and-lock",
                Name = "summary",
                View = DocsShot.Inspector(menu),
                Arrange = root =>
                {
                    var view = root.Q<AvatarDoctorView>();
                    view.Q("ad-mode-row").style.display = DisplayStyle.None;
                    view.Left.style.display = DisplayStyle.None;
                    view.OpenSummary();
                },
                Markers = new[] { ("ad-summary-toggle", 1), ("ad-summary-format", 2), ("ad-summary-text", 3), ("ad-summary-copy", 4), ("ad-lock-toggle", 5), ("ad-lock-note", 6) },
            };
        }

        /// <summary>
        /// A locked component with a note for its users and its Component settings open: 1 the banner, 2 Unlock, 3 the
        /// settings that stay editable, 4 the toolbar with its add buttons greyed out, 5 the tree.
        /// </summary>
        internal static DocsShotSpec LockedOverview(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            var smile = menu.AddEntry(EntryKind.Radial, "Smile");
            smile.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Smile", minWeight = 0f, maxWeight = 100f });
            menu.locked = true;
            menu.lockNote = "Change only the settings under Component settings.";
            return new DocsShotSpec
            {
                Page = "reference-summary-and-lock",
                Name = "locked",
                View = DocsShot.Inspector(menu),
                Arrange = root =>
                {
                    var view = root.Q<AvatarDoctorView>();
                    view.Detail.style.display = DisplayStyle.None;
                    if (!view.Header.SettingsFoldOpen) view.Header.ToggleSettingsFold();
                },
                Markers = new[] { ("ad-lock-banner", 1), ("ad-unlock", 2), ("ad-settings-fold-content", 3), ("ad-toolbar-actions", 4), ("ad-tree", 5) },
            };
        }

        /// <summary>
        /// The inspector with the language list open under the picker: 1 the Simple and Advanced switch, 2 the language
        /// picker. The list sits in the window's root outside the view, so it carries no marker of its own, and the
        /// languages it offers depend on what NDMF knows on the machine that takes the picture.
        /// </summary>
        internal static DocsShotSpec LanguagesPicker(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            return new DocsShotSpec
            {
                Page = "reference-languages",
                Name = "picker",
                View = DocsShot.Inspector(menu),
                Arrange = root =>
                {
                    DocsShots_Guides1.CloseMenus(root);
                    var view = root.Q<AvatarDoctorView>();
                    LayoutProbe.SettleLists(root);
                    AdMenu.Open(view.LanguagePicker.worldBound, view.LanguagePicker, LanguagePicker.Items());
                },
                Markers = new[] { ("ad-mode", 1), ("ad-language", 2) },
            };
        }

        /// <summary>
        /// An Outfits submenu of nine toggles, selected and open in the tree under the header: 1 the memory bar, 2 its
        /// legend, 3 the submenu's row, 4 the More page the ninth toggle moved to, 5 the line that says how many places
        /// the submenu uses.
        /// </summary>
        internal static DocsShotSpec LimitsMore(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var pieces = new[]
            {
                avatar.Jacket, avatar.Hoodie, avatar.Shoes, avatar.Hat, avatar.Glasses,
                DocsShots_Guides2.Child(avatar, "Gloves"), DocsShots_Guides2.Child(avatar, "Scarf"),
                DocsShots_Guides2.Child(avatar, "Socks"), DocsShots_Guides2.Child(avatar, "Belt"),
            };
            foreach (var piece in pieces)
                menu.AddEntry(EntryKind.Toggle, piece.name, outfits.id).objects.Add(new ObjectTarget { target = piece });
            return new DocsShotSpec
            {
                Page = "reference-limits",
                Name = "more-page",
                View = DocsShot.Inspector(menu),
                Arrange = root =>
                {
                    var view = root.Q<AvatarDoctorView>();
                    view.Q("ad-mode-row").style.display = DisplayStyle.None;
                    view.Tree.SelectEntry(outfits.id);
                    view.Tree.SetExpanded(view.Tree.RowFor(outfits.id).Key, true);
                    LayoutProbe.SettleLists(view);
                    DocsShots_Reference1.NameTreeRow(view.Tree, "Outfits", LimitsRowName);
                    DocsShots_Reference1.NameTreeRow(view.Tree, string.Format(L.Text("ad.ui.more_page"), 2), MoreRowName);
                    DocsShots_Reference1.NameNote(view.Detail, string.Format(L.Text("ad.ui.slots"), view.Tree.Selected.Slots, VRCExpressionsMenu.MAX_CONTROLS), LimitsSlotsName);
                },
                Markers = new[] { ("ad-membar", 1), ("ad-membar-legend", 2), (LimitsRowName, 3), (MoreRowName, 4), (LimitsSlotsName, 5) },
            };
        }
    }
}
#endif
