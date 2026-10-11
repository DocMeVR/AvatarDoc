#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.ScriptableObjects;
#if DOCME_MA
using nadena.dev.modular_avatar.core;
#endif
using Object = UnityEngine.Object;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The pictures of the second eleven guides, one per page: overrides, presets, AvatarDoc Shapes, merged
    /// animators, the existing menu, Convert, VRCFury and Modular Avatar rows, platforms, parameter memory, the
    /// wheel's Test mode and Remove from avatar. Each setup builds the state the guide's steps leave. VRCFury items
    /// are made through VrcFuryTestHelper, by type name only; Modular Avatar items only where DOCME_MA is defined.
    /// DocsShotSetupTests runs every setup without the screen read.
    /// </summary>
    [Explicit, Category("DocsShots")]
    public class DocsShots_Guides2
    {
        const string Page = "guides";

        /// <summary>The width the Convert window opens at: the minimum size ImportWindow.Open gives it.</summary>
        internal const float ConvertWindowWidth = 360f;

        /// <summary>The names the shots give unnamed chips and a field whose name the header repeats, so the markers can find them.</summary>
        internal const string VrcFuryChipName = "docs-chip-vrcfury";
        internal const string ModularAvatarChipName = "docs-chip-modular-avatar";
        internal const string PlatformChipName = "docs-platform-chip";
        internal const string EntryPlatformName = "docs-entry-platform";

        [Test] public void OverridesPinned() { using (var avatar = DocsAvatar.Create()) OverridesPinned(avatar).Take(); }
        [Test] public void PresetsSets() { using (var avatar = DocsAvatar.Create()) PresetsSets(avatar).Take(); }
        [Test] public void ShrinkUnderClothesShapes() { using (var avatar = DocsAvatar.Create()) ShrinkUnderClothesShapes(avatar).Take(); }
        [Test] public void OwnAnimatorOrMenuAnimator() { using (var avatar = DocsAvatar.Create()) OwnAnimatorOrMenuAnimator(avatar).Take(); }
        [Test] public void ChangeTheExistingMenuItem() { using (var avatar = DocsAvatar.Create()) ChangeTheExistingMenuItem(avatar).Take(); }
        [Test] public void ConvertWindow() { using (var avatar = DocsAvatar.Create()) ConvertWindow(avatar).Take(); }
        [Test] public void OtherToolRows() { using (var avatar = DocsAvatar.Create()) OtherToolRows(avatar).Take(); }
        [Test] public void PcAndQuestPlatform() { using (var avatar = DocsAvatar.Create()) PcAndQuestPlatform(avatar).Take(); }
        [Test] public void ParameterMemoryBits() { using (var avatar = DocsAvatar.Create()) ParameterMemoryBits(avatar).Take(); }
        [Test] public void TestAndPreviewWheel() { using (var avatar = DocsAvatar.Create()) TestAndPreviewWheel(avatar).Take(); }
        [Test] public void RemoveThingsResult() { using (var avatar = DocsAvatar.Create()) RemoveThingsResult(avatar).Take(); }

        /// <summary>
        /// A No tongue override that holds the face's Tongue_Out at 0, with two clips of the avatar's FX controller
        /// that move it: 1 the shape, 2 Pinned value, 3 the list of clips that animate it.
        /// </summary>
        internal static DocsShotSpec OverridesPinned(DocsAvatar avatar)
        {
            var added = new List<Object>();
            try
            {
                AddFxShapeClip(avatar, "FT Tongue Out", "Tongue_Out", added);
                AddFxShapeClip(avatar, "Gesture Silly Face", "Tongue_Out", added);
                var menu = avatar.AddMenu();
                var rule = menu.AddEntry(EntryKind.Override, "No tongue");
                rule.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Tongue_Out", weight = 0f });
                var spec = DocsShots_Guides1.PanelShot(menu, rule, "overrides-pinned", ("shape", 1), ("weight", 2), ("ad-override-usage", 3));
                spec.Release = () => DestroyAll(added);
                return spec;
            }
            catch
            {
                DestroyAll(added);
                throw;
            }
        }

        /// <summary>
        /// A Hoodie look preset that takes the hat off, puts the hoodie of the Outfits group on and sets the Smile
        /// radial halfway: 1 the first row's control, 2 its On and Off, 3 the radial's slider, 4 From preview, 5 the
        /// + Add of Other parameters.
        /// </summary>
        internal static DocsShotSpec PresetsSets(DocsAvatar avatar)
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
            var look = menu.AddEntry(EntryKind.Preset, "Hoodie look");
            look.presetStates.Add(new PresetState { entryId = hat.id, on = false });
            look.presetStates.Add(new PresetState { entryId = hoodie.id, on = true });
            look.presetStates.Add(new PresetState { entryId = smile.id, value = 0.5f });
            return DocsShots_Guides1.PanelShot(menu, look, "presets-sets",
                ("ad-preset-control", 1), ("ad-preset-on", 2), ("ad-preset-value", 3), ("ad-preset-from-preview", 4), ("ad-preset-add-parameter", 5));
        }

        /// <summary>
        /// The AvatarDoc Shapes inspector on the shoes, holding the body's Shrink_Feet at 100 while they are on, with a
        /// Shoes toggle that switches them: 1 Mesh, 2 Shape, 3 Value, 4 Add matching shapes. Unity's own component
        /// header is not part of the picture.
        /// </summary>
        internal static DocsShotSpec ShrinkUnderClothesShapes(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Shoes").objects.Add(new ObjectTarget { target = avatar.Shoes });
            var shapes = avatar.Shoes.AddComponent<AvatarDocShapes>();
            shapes.whileOn.Add(new ShapeWhileOn { mesh = avatar.Body, shape = "Shrink_Feet", value = 100f });
            AvatarPlanCache.Invalidate();
            var editor = UnityEditor.Editor.CreateEditor(shapes);
            try
            {
                return new DocsShotSpec
                {
                    Page = Page,
                    Name = "shrink-under-clothes-shapes",
                    View = new InspectorElement(editor),
                    Markers = new[] { ("ad-shapes-while-on-mesh-0", 1), ("ad-shapes-while-on-shape-0", 2), ("ad-shapes-while-on-value-0", 3), ("ad-shapes-add-matching", 4) },
                    Release = () => Object.DestroyImmediate(editor),
                };
            }
            catch
            {
                Object.DestroyImmediate(editor);
                throw;
            }
        }

        /// <summary>
        /// A merged animator with a Sparkles FX controller of two layers and two parameters nothing declares: 1
        /// Controller, 2 Path root, 3 the layer list, 4 the first Add control.
        /// </summary>
        internal static DocsShotSpec OwnAnimatorOrMenuAnimator(DocsAvatar avatar)
        {
            var controller = new AnimatorController { name = "Sparkles FX" };
            try
            {
                controller.AddParameter("Sparkles", AnimatorControllerParameterType.Bool);
                controller.AddParameter("Sparkles Hue", AnimatorControllerParameterType.Float);
                controller.AddLayer("Sparkles");
                controller.AddLayer("Sparkles Colour");
                var menu = avatar.AddMenu();
                var merge = AnimatorMerge.Create(controller);
                menu.animators.Add(merge);
                var spec = KeyPanelShot(menu, merge.id, "own-animator-or-menu-animator",
                    ("ad-animator-controller", 1), ("ad-animator-path-root", 2), ("ad-animator-layers", 3), ("ad-animator-add-control", 4));
                spec.Release = () => DestroyController(controller);
                return spec;
            }
            catch
            {
                DestroyController(controller);
                throw;
            }
        }

        /// <summary>
        /// The avatar's own Sit toggle renamed to Sit down and moved into its Emotes submenu, selected in the tree:
        /// 1 Name, 2 Move to, 3 In menu, 4 Remove from avatar, 5 Reset. The header and the mode row are left out.
        /// </summary>
        internal static DocsShotSpec ChangeTheExistingMenuItem(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            var edit = MenuEdit.Create(new[] { "Sit" });
            edit.name = "Sit down";
            edit.move = true;
            edit.targetPath = "Emotes";
            menu.menuEdits.Add(edit);
            return TreeAndPanelShot(menu, view => RevealEdit(view, edit.id), "change-the-existing-menu-item",
                ("ad-name", 1), ("ad-existing-move", 2), ("ad-existing-in-menu", 3), ("ad-existing-remove", 4), ("ad-existing-reset", 5));
        }

        /// <summary>
        /// The Convert window at the width it opens at, for an avatar with two VRCFury toggles, a Modular Avatar
        /// toggle where DOCME_MA is defined, and the avatar's own Sit and Wave, which no FX layer drives: 1 All, 2 the
        /// first row, 3 the first row that cannot be converted, 4 Remove originals, 5 the Convert button.
        /// </summary>
        internal static DocsShotSpec ConvertWindow(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            AddOtherToolItems(avatar);
            var window = ScriptableObject.CreateInstance<ImportWindow>();
            try
            {
                window.Init(menu);
                window.CreateGUI();
            }
            catch
            {
                Object.DestroyImmediate(window);
                throw;
            }
            return new DocsShotSpec
            {
                Page = Page,
                Name = "convert-window",
                View = window.rootVisualElement,
                Width = ConvertWindowWidth,
                Markers = new[] { ("ad-import-all", 1), ("ad-import-row", 2), ("ad-unsupported", 3), ("ad-import-remove", 4), ("ad-import-run", 5) },
                Release = () => Object.DestroyImmediate(window),
            };
        }

        /// <summary>
        /// The same VRCFury and Modular Avatar items as rows of the tree, next to an own Hat toggle, with the VRCFury
        /// Glasses row selected: 1 a VRCFury chip, 2 a Modular Avatar chip (only where DOCME_MA is defined), 3 Name,
        /// 4 In menu. The chips have no names, so the arrange step names them after the rows are drawn.
        /// </summary>
        internal static DocsShotSpec OtherToolRows(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            AddOtherToolItems(avatar);
            var markers = new List<(string, int)> { (VrcFuryChipName, 1) };
#if DOCME_MA
            markers.Add((ModularAvatarChipName, 2));
#endif
            markers.Add(("ad-name", 3));
            markers.Add(("ad-existing-in-menu", 4));
            return TreeAndPanelShot(menu, view =>
            {
                var glasses = view.Tree.RowsOfKind(RowKind.Foreign).FirstOrDefault(r => r.Name == "Glasses")
                    ?? throw new InvalidOperationException("The tree shows no VRCFury row named Glasses.");
                view.Tree.RevealKey(glasses.Key);
                LayoutProbe.SettleLists(view);
                NameChip(view.Tree, L.Text("ad.ui.chip.vrcfury"), VrcFuryChipName);
#if DOCME_MA
                NameChip(view.Tree, L.Text("ad.ui.chip.modular_avatar"), ModularAvatarChipName);
#endif
            }, "with-vrcfury-and-modular-avatar-rows", markers.ToArray());
        }

        /// <summary>
        /// A Glow radial built on PC only, selected, with its Advanced fold open, while the editor builds for PC: 1
        /// the PC only chip of its row, 2 the Advanced fold, 3 its Platform. The build target is pinned to PC for the
        /// shot and given back in Release.
        /// </summary>
        internal static DocsShotSpec PcAndQuestPlatform(DocsAvatar avatar)
        {
            var before = Platform.OverrideMobile;
            Platform.OverrideMobile = false;
            try
            {
                var menu = avatar.AddMenu();
                menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
                var glow = menu.AddEntry(EntryKind.Radial, "Glow");
                glow.properties.Add(GlassesGlow(avatar));
                glow.platform = EntryPlatform.PCOnly;
                menu.AddEntry(EntryKind.Toggle, "Jacket").objects.Add(new ObjectTarget { target = avatar.Jacket });
                var spec = TreeAndPanelShot(menu, view =>
                {
                    view.Tree.RevealEntry(glow.id);
                    OpenAdvancedFold(view.Detail);
                    Rename(view.Detail, "ad-platform", EntryPlatformName);
                    LayoutProbe.SettleLists(view);
                    NameChip(view.Tree, L.Text("ad.ui.chip.pc_only"), PlatformChipName);
                }, "pc-and-quest-platform", (PlatformChipName, 1), ("ad-advanced-fold-toggle", 2), (EntryPlatformName, 3));
                spec.Release = () => Platform.OverrideMobile = before;
                return spec;
            }
            catch
            {
                Platform.OverrideMobile = before;
                throw;
            }
        }

        /// <summary>
        /// The header and the Smile radial's panel with its Advanced fold open, on a menu of a toggle, a radial and an
        /// exclusive group: 1 the memory bar, 2 its legend, 3 the Builds line, 4 the cost beside Synced. A radial is
        /// shown because its parameter costs 8 bits.
        /// </summary>
        internal static DocsShotSpec ParameterMemoryBits(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            var smile = menu.AddEntry(EntryKind.Radial, "Smile");
            smile.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Smile", minWeight = 0f, maxWeight = 100f });
            AddOutfitGroup(menu, avatar);
            return new DocsShotSpec
            {
                Page = Page,
                Name = "parameter-memory-bits",
                View = DocsShot.Inspector(menu),
                Arrange = root =>
                {
                    var view = root.Q<AvatarDoctorView>();
                    view.Tree.RevealEntry(smile.id);
                    view.Q("ad-mode-row").style.display = DisplayStyle.None;
                    view.Left.style.display = DisplayStyle.None;
                    OpenAdvancedFold(view.Detail);
                },
                Markers = new[] { ("ad-membar", 1), ("ad-membar-legend", 2), ("ad-builds-line", 3), ("ad-synced-cost", 4) },
            };
        }

        /// <summary>
        /// The toolbar, the tree and the wheel in Test mode, with nothing selected, the Hat toggle previewed on and a
        /// Headpat contact trigger on the strip: 1 the Wheel button, 2 Select and Test, 3 the ring, 4 the Triggers
        /// strip, 5 Reset. The Hat's preview is cleared again in Release.
        /// </summary>
        internal static DocsShotSpec TestAndPreviewWheel(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var smile = menu.AddEntry(EntryKind.Radial, "Smile");
            smile.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Smile", minWeight = 0f, maxWeight = 100f });
            AddOutfitGroup(menu, avatar);
            var created = ContactStarts.Apply(menu, ContactStarts.StartKind.Headpat, out var errorKey);
            if (errorKey != null || created.Count != 1)
                throw new InvalidOperationException("The Headpat start made nothing on the docs avatar: " + (errorKey != null ? L.Text(errorKey) : "no trigger came back."));
            created[0].blendShapes[0].shape = "Blush";
            return new DocsShotSpec
            {
                Page = Page,
                Name = "test-and-preview-wheel",
                View = DocsShot.Inspector(menu),
                Arrange = root =>
                {
                    var view = root.Q<AvatarDoctorView>();
                    view.Tree.ClearSelection();
                    view.Q("ad-mode-row").style.display = DisplayStyle.None;
                    view.Header.style.display = DisplayStyle.None;
                    view.Detail.style.display = DisplayStyle.None;
                    PreviewState.Set(hat.id, 1f);
                    ShowWheel(view);
                    view.TestMode = true;
                },
                Markers = new[] { ("ad-wheel-toggle", 1), ("ad-wheel-mode", 2), ("ad-ring", 3), ("ad-wheel-triggers-label", 4), ("ad-wheel-reset", 5) },
                Release = () => PreviewState.Clear(hat.id),
            };
        }

        /// <summary>
        /// The avatar's own Dance toggle, on an Int parameter that two FX layers read, with Remove from avatar on and
        /// selected in the tree: 1 In menu, 2 Remove from avatar, 3 the line that says what the removal frees. The
        /// header and the mode row are left out.
        /// </summary>
        internal static DocsShotSpec RemoveThingsResult(DocsAvatar avatar)
        {
            var machines = new List<AnimatorStateMachine>();
            try
            {
                AddDance(avatar, machines);
                var menu = avatar.AddMenu();
                menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
                var edit = MenuEdit.Create(new[] { "Dance" });
                edit.remove = true;
                menu.menuEdits.Add(edit);
                var spec = TreeAndPanelShot(menu, view => RevealEdit(view, edit.id), "remove-things-result",
                    ("ad-existing-in-menu", 1), ("ad-existing-remove", 2), ("ad-existing-remove-note", 3));
                spec.Release = () => DestroyMachines(machines);
                return spec;
            }
            catch
            {
                DestroyMachines(machines);
                throw;
            }
        }

        /// <summary>
        /// What the Convert and the rows shots share: a VRCFury toggle at Accessories/Glasses that turns the glasses
        /// on, a VRCFury Blush toggle that sets the face's Blush to 100, and, where DOCME_MA is defined, a Modular
        /// Avatar Hoodie toggle that turns the hoodie on. Every object is made under the avatar root, so the
        /// avatar's Dispose takes it away.
        /// </summary>
        internal static void AddOtherToolItems(DocsAvatar avatar)
        {
            var glasses = VrcFuryTestHelper.AddToggle(Child(avatar, "Glasses Toggle"), "Accessories/Glasses");
            VrcFuryTestHelper.AddAction(glasses, VrcFuryReader.ObjectToggleActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "obj", avatar.Glasses);
                VrcFuryTestHelper.SetField(a, "mode", 0);
            });
            var blush = VrcFuryTestHelper.AddToggle(Child(avatar, "Blush Toggle"), "Blush");
            VrcFuryTestHelper.AddAction(blush, VrcFuryReader.BlendShapeActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "blendShape", "Blush");
                VrcFuryTestHelper.SetField(a, "blendShapeValue", 100f);
                VrcFuryTestHelper.SetField(a, "allRenderers", false);
                VrcFuryTestHelper.SetField(a, "renderer", avatar.Face);
            });
#if DOCME_MA
            var hoodie = Child(avatar, "Hoodie Toggle");
            hoodie.AddComponent<ModularAvatarMenuInstaller>();
            var item = hoodie.AddComponent<ModularAvatarMenuItem>();
            if (item.Control == null) item.Control = new VRCExpressionsMenu.Control();
            item.Control.type = VRCExpressionsMenu.Control.ControlType.Toggle;
            item.label = "Hoodie";
            var toggle = hoodie.AddComponent<ModularAvatarObjectToggle>();
            var reference = new AvatarObjectReference();
            reference.Set(avatar.Hoodie);
            toggle.Objects = new List<ToggledObject> { new ToggledObject { Object = reference, Active = true } };
#endif
            AvatarPlanCache.Invalidate();
        }

        /// <summary>The Jacket and Hoodie toggles of an Outfits submenu, in the Outfits exclusive group, the jacket on by default.</summary>
        static void AddOutfitGroup(AvatarDoc menu, DocsAvatar avatar)
        {
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var jacket = menu.AddEntry(EntryKind.Toggle, "Jacket", outfits.id);
            jacket.objects.Add(new ObjectTarget { target = avatar.Jacket });
            jacket.exclusiveGroup = "Outfits";
            jacket.defaultOn = true;
            var hoodie = menu.AddEntry(EntryKind.Toggle, "Hoodie", outfits.id);
            hoodie.objects.Add(new ObjectTarget { target = avatar.Hoodie });
            hoodie.exclusiveGroup = "Outfits";
        }

        /// <summary>The glasses' emission colour from black to a soft green, the same glow the home picture uses.</summary>
        static MaterialPropertyTarget GlassesGlow(DocsAvatar avatar) => new MaterialPropertyTarget
        {
            renderer = avatar.Glasses.GetComponent<MeshRenderer>(),
            property = "_EmissionColor",
            type = PropertyValueType.Color,
            minColor = Color.black,
            color = new Color(0.25f, 1f, 0.8f),
        };

        /// <summary>
        /// The avatar's own Dance toggle at the top of its menu, on a synced Int parameter, and two FX layers, Dance
        /// and Dance Music, that read only that parameter: nothing else keeps either, so a removal frees both and
        /// the parameter's 8 bits. The made state machines go into machines for Release.
        /// </summary>
        static void AddDance(DocsAvatar avatar, List<AnimatorStateMachine> machines)
        {
            var test = avatar.Test;
            test.ExpressionParameters.parameters = test.ExpressionParameters.parameters.Concat(new[]
            {
                new VRCExpressionParameters.Parameter { name = "Dance", valueType = VRCExpressionParameters.ValueType.Int, saved = false, networkSynced = true },
            }).ToArray();
            test.ExpressionsMenu.controls.Add(TestAvatar.ToggleControl("Dance", "Dance"));
            test.Fx.AddParameter("Dance", AnimatorControllerParameterType.Int);
            machines.Add(AddIntLayer(test.Fx, "Dance", "Dance"));
            machines.Add(AddIntLayer(test.Fx, "Dance Music", "Dance"));
            AvatarPlanCache.Invalidate();
        }

        /// <summary>A full weight layer with Idle and Playing, switched by the parameter being 1 and back, Write Defaults off.</summary>
        static AnimatorStateMachine AddIntLayer(AnimatorController controller, string layerName, string parameter)
        {
            controller.AddLayer(layerName);
            var layers = controller.layers;
            layers[layers.Length - 1].defaultWeight = 1f;
            controller.layers = layers;
            var machine = controller.layers[controller.layers.Length - 1].stateMachine;
            var idle = machine.AddState("Idle");
            var playing = machine.AddState("Playing");
            // The avatar's own Idle state has Write Defaults off: a mixed avatar would show a warning in the picture.
            idle.writeDefaultValues = false;
            playing.writeDefaultValues = false;
            machine.defaultState = idle;
            var start = idle.AddTransition(playing);
            start.hasExitTime = false;
            start.AddCondition(AnimatorConditionMode.Equals, 1f, parameter);
            var stop = playing.AddTransition(idle);
            stop.hasExitTime = false;
            stop.AddCondition(AnimatorConditionMode.NotEqual, 1f, parameter);
            return machine;
        }

        /// <summary>A state on the FX controller's first layer playing a clip that moves one face shape from 0 to 100. The clip and the state go into added.</summary>
        static void AddFxShapeClip(DocsAvatar avatar, string name, string shape, List<Object> added)
        {
            var clip = TestAvatar.ShapeClip(name, avatar.Face.name, shape, 0f, 100f);
            added.Add(clip);
            added.Add(avatar.Test.AddFxClip(name, clip));
            AvatarPlanCache.Invalidate();
        }

        static GameObject Child(DocsAvatar avatar, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(avatar.Root.transform, false);
            return go;
        }

        /// <summary>A picture of one tree row's panel alone, for a row that is not an entry: the row revealed by its key and selected, the mode row, the header and the tree left out.</summary>
        static DocsShotSpec KeyPanelShot(AvatarDoc menu, string rowKey, string name, params (string, int)[] markers) => new DocsShotSpec
        {
            Page = Page,
            Name = name,
            View = DocsShot.Inspector(menu),
            Arrange = root =>
            {
                var view = root.Q<AvatarDoctorView>();
                view.Tree.RevealKey(rowKey);
                view.Q("ad-mode-row").style.display = DisplayStyle.None;
                view.Header.style.display = DisplayStyle.None;
                view.Left.style.display = DisplayStyle.None;
            },
            Markers = markers,
        };

        /// <summary>A picture of the toolbar, the tree and the selected row's panel: select runs first, then the mode row and the header are left out.</summary>
        static DocsShotSpec TreeAndPanelShot(AvatarDoc menu, Action<AvatarDoctorView> select, string name, params (string, int)[] markers) => new DocsShotSpec
        {
            Page = Page,
            Name = name,
            View = DocsShot.Inspector(menu),
            Arrange = root =>
            {
                var view = root.Q<AvatarDoctorView>();
                view.Q("ad-mode-row").style.display = DisplayStyle.None;
                view.Header.style.display = DisplayStyle.None;
                select(view);
            },
            Markers = markers,
        };

        /// <summary>Reveals and selects the existing or foreign row a menu edit belongs to.</summary>
        static void RevealEdit(AvatarDoctorView view, string editId)
        {
            var row = view.Tree.RowForEdit(editId) ?? throw new InvalidOperationException("No tree row carries the menu edit " + editId + ".");
            view.Tree.RevealKey(row.Key);
        }

        /// <summary>Opens the panel's Advanced fold, leaving it open when it already is: the arrange step runs again before every read.</summary>
        internal static void OpenAdvancedFold(VisualElement detail)
        {
            var fold = detail.Q<AdvancedFold>() ?? throw new InvalidOperationException("The selected panel has no Advanced fold.");
            if (!fold.IsOpen) fold.ToggleOpen();
        }

        /// <summary>Shows the wheel through the toolbar's own Wheel button, unless it already shows.</summary>
        internal static void ShowWheel(AvatarDoctorView view)
        {
            if (!view.Wheel.ClassListContains("ad-hidden")) return;
            UiTestEvents.SimulateClick(view.Tree.Q<Button>("ad-wheel-toggle"));
        }

        /// <summary>Gives the element named from, or already named to, the name to. A second run finds it under the new name.</summary>
        static void Rename(VisualElement within, string from, string to)
        {
            var element = within.Q(to) ?? within.Q(from) ?? throw new InvalidOperationException("Nothing named \"" + from + "\" is in the panel.");
            element.name = to;
        }

        /// <summary>Names the first chip in the tree that reads text. The rows must be drawn first.</summary>
        static void NameChip(VisualElement tree, string text, string name)
        {
            var chip = tree.Q<Label>(name) ?? tree.Query<Label>(className: "ad-chip").Where(l => l.text == text).First();
            if (chip == null) throw new InvalidOperationException("No row of the tree shows the chip \"" + text + "\".");
            chip.name = name;
        }

        static void DestroyAll(List<Object> objects)
        {
            foreach (var obj in objects)
                if (obj != null) Object.DestroyImmediate(obj);
            objects.Clear();
        }

        /// <summary>Destroys a controller made in memory, with the state machines its layers made.</summary>
        static void DestroyController(AnimatorController controller)
        {
            if (controller == null) return;
            DestroyMachines(controller.layers.Select(l => l.stateMachine).ToList());
            Object.DestroyImmediate(controller);
        }

        /// <summary>Destroys state machines made in memory, with their states and the states' transitions.</summary>
        static void DestroyMachines(List<AnimatorStateMachine> machines)
        {
            foreach (var machine in machines)
            {
                if (machine == null) continue;
                foreach (var child in machine.states)
                {
                    if (child.state == null) continue;
                    foreach (var transition in child.state.transitions)
                        if (transition != null) Object.DestroyImmediate(transition);
                    Object.DestroyImmediate(child.state);
                }
                Object.DestroyImmediate(machine);
            }
            machines.Clear();
        }
    }
}
#endif
