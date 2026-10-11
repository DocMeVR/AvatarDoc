using System.Collections.Generic;
using System.Linq;
#if DOCME_MA
using nadena.dev.modular_avatar.core;
#endif
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>MenuEditApplier: menu edits resolved against the real menu assets after Modular Avatar and again in BuildPhase.Optimizing.</summary>
    public class LateEditTests
    {
        readonly List<Object> spawned = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            GeneratedAssets.Note();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in spawned) if (obj != null) Object.DestroyImmediate(obj);
            spawned.Clear();
            GeneratedAssets.CleanUp();
        }

        VRCExpressionsMenu Menu(params VRCExpressionsMenu.Control[] controls)
        {
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.controls = controls.ToList();
            spawned.Add(menu);
            return menu;
        }

        static VRCExpressionsMenu.Control Submenu(string name, VRCExpressionsMenu menu)
        {
            return new VRCExpressionsMenu.Control { name = name, type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = menu, parameter = new VRCExpressionsMenu.Control.Parameter { name = "" } };
        }

        static VRCExpressionsMenu.Control Toggle(string name)
        {
            return new VRCExpressionsMenu.Control { name = name, type = VRCExpressionsMenu.Control.ControlType.Toggle, parameter = new VRCExpressionsMenu.Control.Parameter { name = "P" }, value = 1 };
        }

        static PendingEdit Pending(MenuEdit edit) => new PendingEdit { Edit = edit };

        List<VRCExpressionsMenu> Save() => new List<VRCExpressionsMenu>();

        [Test]
        public void ApplyClonesBeforeChanging()
        {
            var root = Menu(Toggle("Foo"), Toggle("Bar"));
            var saved = Save();
            var pending = new List<PendingEdit> { Pending(new MenuEdit { source = new List<string> { "Foo" }, hide = true }) };
            var messages = new List<ModelMessage>();

            var result = MenuEditApplier.Apply(root, pending, m => { saved.Add(m); spawned.Add(m); }, messages, false);

            Assert.That(result, Is.Not.SameAs(root));
            Assert.That(root.controls.Select(c => c.name), Does.Contain("Foo"), "the source asset is unchanged");
            Assert.That(result.controls.Any(c => c.name == "Foo"), Is.False);
            Assert.That(result.controls.Select(c => c.name), Does.Contain("Bar"));
            Assert.That(pending, Is.Empty);
            Assert.That(saved, Does.Contain(result));
        }

        [Test]
        public void ApplyReturnsTheSameRootWhenNothingResolves()
        {
            var root = Menu(Toggle("Foo"));
            var saved = Save();
            var pending = new List<PendingEdit> { Pending(new MenuEdit { source = new List<string> { "Missing" }, hide = true }) };
            var messages = new List<ModelMessage>();

            var result = MenuEditApplier.Apply(root, pending, m => { saved.Add(m); spawned.Add(m); }, messages, false);

            Assert.That(result, Is.SameAs(root));
            Assert.That(saved, Is.Empty);
            Assert.That(pending, Has.Count.EqualTo(1));
            Assert.That(messages, Is.Empty);
        }

        [Test]
        public void LateHideRenameIconMove()
        {
            var icon = new Texture2D(4, 4);
            spawned.Add(icon);
            var root = Menu(Toggle("A"), Toggle("B"), Toggle("C"), Toggle("D"));
            var pending = new List<PendingEdit>
            {
                Pending(new MenuEdit { source = new List<string> { "A" }, hide = true }),
                Pending(new MenuEdit { source = new List<string> { "B" }, name = "<b>Beta</b>" }),
                Pending(new MenuEdit { source = new List<string> { "C" }, icon = icon }),
                Pending(new MenuEdit { source = new List<string> { "D" }, move = true, targetPath = "New" }),
            };
            var messages = new List<ModelMessage>();

            var result = MenuEditApplier.Apply(root, pending, m => spawned.Add(m), messages, true);

            Assert.That(result.controls.Any(c => c.name == "A"), Is.False);
            Assert.That(result.controls.Single(c => RichName.Strip(c.name) == "Beta").name, Is.EqualTo("<b>Beta</b>"));
            Assert.That(result.controls.Single(c => c.name == "C").icon, Is.SameAs(icon));
            var newControl = result.controls.Single(c => c.name == "New");
            Assert.That(newControl.subMenu.controls.Select(c => c.name), Does.Contain("D"));
            Assert.That(result.controls.Any(c => c.name == "D"), Is.False);
            Assert.That(pending, Is.Empty);
        }

        [Test]
        public void RenameOfASubmenuAndHideInsideItBothApplyLate()
        {
            var clothes = Menu(Toggle("Hat"), Toggle("Coat"));
            var root = Menu(Submenu("Clothes", clothes));
            var pending = new List<PendingEdit>
            {
                Pending(new MenuEdit { source = new List<string> { "Clothes" }, name = "Outfits" }),
                Pending(new MenuEdit { source = new List<string> { "Clothes", "Hat" }, hide = true }),
            };
            var messages = new List<ModelMessage>();

            var result = MenuEditApplier.Apply(root, pending, m => spawned.Add(m), messages, true);

            var renamed = result.controls.Single();
            Assert.That(renamed.name, Is.EqualTo("Outfits"));
            Assert.That(renamed.subMenu.controls.Select(c => c.name), Is.EquivalentTo(new[] { "Coat" }));
            Assert.That(pending, Is.Empty);
            Assert.That(messages.Any(m => m.Key == "ad.warn.edit_source_missing"), Is.False);
        }

        [Test]
        public void HideOfOccurrenceZeroDoesNotShiftOccurrenceOne()
        {
            var root = Menu(Toggle("Item"), Toggle("Item"));
            var pending = new List<PendingEdit>
            {
                Pending(new MenuEdit { source = new List<string> { "Item" }, occurrence = 0, hide = true }),
                Pending(new MenuEdit { source = new List<string> { "Item" }, occurrence = 1, name = "Kept" }),
            };
            var messages = new List<ModelMessage>();

            var result = MenuEditApplier.Apply(root, pending, m => spawned.Add(m), messages, true);

            Assert.That(result.controls.Select(c => c.name), Is.EquivalentTo(new[] { "Kept" }));
            Assert.That(pending, Is.Empty);
            Assert.That(messages.Any(m => m.Key == "ad.warn.edit_source_missing"), Is.False);
        }

        [Test]
        public void LateEditFindsAnItemInsideAMorePage()
        {
            var deepMore = Menu(Toggle("Boots"));
            var clothes = Menu(Toggle("C1"), Toggle("C2"), Toggle("C3"), Toggle("C4"), Toggle("C5"), Toggle("C6"), Toggle("C7"), Submenu("More", deepMore));
            var rootMore = Menu(Toggle("Hat"), Toggle("Scarf"));
            var root = Menu(Toggle("A"), Toggle("B"), Toggle("C"), Toggle("D"), Toggle("E"), Toggle("F"), Submenu("Clothes", clothes), Submenu("More", rootMore));
            var pending = new List<PendingEdit>
            {
                Pending(new MenuEdit { source = new List<string> { "Hat" }, name = "Cap" }),
                Pending(new MenuEdit { source = new List<string> { "Clothes", "Boots" }, hide = true }),
            };
            var messages = new List<ModelMessage>();

            var result = MenuEditApplier.Apply(root, pending, m => spawned.Add(m), messages, true);

            var morePage = result.controls.Single(c => c.name == "More").subMenu;
            Assert.That(morePage.controls.Select(c => c.name), Does.Contain("Cap"));
            var deepPage = result.controls.Single(c => c.name == "Clothes").subMenu.controls.Single(c => c.name == "More").subMenu;
            Assert.That(deepPage.controls, Is.Empty);
            Assert.That(pending, Is.Empty);
            Assert.That(messages.Any(m => m.Key == "ad.warn.edit_source_missing"), Is.False);
        }

        [Test]
        public void LateMoveSplitsAnOverfullTarget()
        {
            var target = Menu(Toggle("T1"), Toggle("T2"), Toggle("T3"), Toggle("T4"), Toggle("T5"), Toggle("T6"), Toggle("T7"), Toggle("T8"));
            var root = Menu(Submenu("Target", target), Toggle("X"));
            var pending = new List<PendingEdit> { Pending(new MenuEdit { source = new List<string> { "X" }, move = true, targetPath = "Target" }) };
            var messages = new List<ModelMessage>();

            var result = MenuEditApplier.Apply(root, pending, m => spawned.Add(m), messages, false);

            var targetClone = result.controls.Single(c => c.name == "Target").subMenu;
            Assert.That(targetClone.controls.Count, Is.EqualTo(VRCExpressionsMenu.MAX_CONTROLS));
            var more = targetClone.controls.Last();
            Assert.That(more.type, Is.EqualTo(VRCExpressionsMenu.Control.ControlType.SubMenu));
            Assert.That(more.name, Is.EqualTo("More"));
            Assert.That(more.subMenu.controls.Count, Is.EqualTo(2));
            var allNames = targetClone.controls.Take(targetClone.controls.Count - 1).Select(c => c.name)
                .Concat(more.subMenu.controls.Select(c => c.name)).ToList();
            Assert.That(allNames, Does.Contain("X"));
            Assert.That(pending, Is.Empty);
        }

        [Test]
        public void RealSubmenuNamedMoreIsNotTransparent()
        {
            var more = Menu(Toggle("Hat"));
            var root = Menu(Toggle("Hat"), Submenu("More", more));
            var pending = new List<PendingEdit> { Pending(new MenuEdit { source = new List<string> { "Hat" }, hide = true }) };
            var messages = new List<ModelMessage>();

            var result = MenuEditApplier.Apply(root, pending, m => spawned.Add(m), messages, true);

            Assert.That(result.controls.Any(c => c.name == "Hat"), Is.False, "the root item is the one named");
            var morePage = result.controls.Single(c => c.name == "More").subMenu;
            Assert.That(morePage.controls.Select(c => c.name), Is.EqualTo(new[] { "Hat" }), "the one inside is left alone");
        }

        [Test]
        public void EditSourcedInsideARealMoreSubmenuResolves()
        {
            var more = Menu(Toggle("Hat"));
            var root = Menu(Toggle("Hat"), Submenu("More", more));
            var pending = new List<PendingEdit> { Pending(new MenuEdit { source = new List<string> { "More", "Hat" }, name = "Cap" }) };
            var messages = new List<ModelMessage>();

            var result = MenuEditApplier.Apply(root, pending, m => spawned.Add(m), messages, true);

            var morePage = result.controls.Single(c => c.name == "More").subMenu;
            Assert.That(morePage.controls.Select(c => c.name), Is.EqualTo(new[] { "Cap" }));
            Assert.That(result.controls.Select(c => c.name), Does.Contain("Hat"), "the root item keeps its name");
            Assert.That(pending, Is.Empty);
        }

        [Test]
        public void MoveTargetOnAnOverflowPageIsFoundNotDuplicated()
        {
            var clothes = Menu(Toggle("Coat"));
            var page = Menu(Toggle("P1"), Submenu("Clothes", clothes));
            var root = Menu(Toggle("A"), Toggle("B"), Toggle("C"), Toggle("D"), Toggle("E"), Toggle("F"), Toggle("X"), Submenu("More", page));
            var pending = new List<PendingEdit> { Pending(new MenuEdit { source = new List<string> { "X" }, move = true, targetPath = "Clothes" }) };
            var messages = new List<ModelMessage>();

            var result = MenuEditApplier.Apply(root, pending, m => spawned.Add(m), messages, false);

            Assert.That(result.controls.Any(c => c.name == "Clothes"), Is.False, "no second Clothes one level up");
            var movedInto = result.controls.Single(c => c.name == "More").subMenu.controls.Single(c => c.name == "Clothes").subMenu;
            Assert.That(movedInto.controls.Select(c => c.name), Is.EquivalentTo(new[] { "Coat", "X" }));
            Assert.That(pending, Is.Empty, "the target was there, so the move did not wait for the final pass");
        }

        [Test]
        public void LateMoveIntoAMissingSubmenuWaitsForTheFinalPass()
        {
            var root = Menu(Toggle("X"));
            var edit = new MenuEdit { source = new List<string> { "X" }, move = true, targetPath = "Clothes" };
            var pending = new List<PendingEdit> { Pending(edit) };
            var messages = new List<ModelMessage>();

            var afterMa = MenuEditApplier.Apply(root, pending, m => spawned.Add(m), messages, false);

            Assert.That(afterMa, Is.SameAs(root), "the pass after Modular Avatar changes nothing");
            Assert.That(pending, Has.Count.EqualTo(1));
            Assert.That(messages, Is.Empty);

            var result = MenuEditApplier.Apply(root, pending, m => spawned.Add(m), messages, true);

            var clothes = result.controls.Single(c => c.name == "Clothes");
            Assert.That(clothes.subMenu.controls.Select(c => c.name), Does.Contain("X"));
            Assert.That(pending, Is.Empty);
        }

        [Test]
        public void UntouchedOverfullMenusAreLeftAlone()
        {
            var overfull = Menu(Toggle("O1"), Toggle("O2"), Toggle("O3"), Toggle("O4"), Toggle("O5"), Toggle("O6"), Toggle("O7"), Toggle("O8"), Toggle("O9"), Toggle("O10"));
            var renamed = Menu(Toggle("R"));
            var root = Menu(Submenu("Overfull", overfull), Submenu("Other", renamed));
            var pending = new List<PendingEdit> { Pending(new MenuEdit { source = new List<string> { "Other", "R" }, name = "Renamed" }) };
            var messages = new List<ModelMessage>();

            var result = MenuEditApplier.Apply(root, pending, m => spawned.Add(m), messages, false);

            var overfullClone = result.controls.Single(c => c.name == "Overfull").subMenu;
            Assert.That(overfullClone.controls.Count, Is.EqualTo(10));
            Assert.That(overfullClone.controls.Any(c => c.name == "More"), Is.False);
            var otherClone = result.controls.Single(c => c.name == "Other").subMenu;
            Assert.That(otherClone.controls.Single().name, Is.EqualTo("Renamed"));
            Assert.That(pending, Is.Empty);
        }

        [Test]
        public void ModelAndApplierAgreeOnOccurrenceInASharedSubmenu()
        {
            var avatar = TestAvatar.Create();
            var shared = Menu(Toggle("Item"));
            var existingRoot = Menu(Submenu("A", shared), Submenu("B", shared));
            avatar.Descriptor.expressionsMenu = existingRoot;
            var menu = avatar.AddMenu();
            menu.menuEdits.Add(new MenuEdit { source = new List<string> { "B", "Item" }, occurrence = 0, hide = true });

            var tree = MenuModel.Build(avatar.Root.transform, existingRoot, new[] { menu });
            var plan = ParameterPlanner.Plan(tree, new Dictionary<string, PlannedType>());
            var context = new BuildContext(avatar.Root, null);
            MenuWriter.Write(context, avatar.Descriptor, tree, plan);
            var writtenRoot = avatar.Descriptor.expressionsMenu;

            var writtenA = writtenRoot.controls.Single(c => c.name == "A").subMenu;
            var writtenB = writtenRoot.controls.Single(c => c.name == "B").subMenu;
            Assert.That(writtenA.controls.Select(c => c.name), Does.Contain("Item"), "A keeps it through the model path");
            Assert.That(writtenB.controls.Any(c => c.name == "Item"), Is.False, "B loses it through the model path");

            var applierShared = Menu(Toggle("Item"));
            var applierRoot = Menu(Submenu("A", applierShared), Submenu("B", applierShared));
            var pending = new List<PendingEdit> { Pending(new MenuEdit { source = new List<string> { "B", "Item" }, occurrence = 0, hide = true }) };
            var messages = new List<ModelMessage>();

            var result = MenuEditApplier.Apply(applierRoot, pending, m => spawned.Add(m), messages, false);

            var clonedA = result.controls.Single(c => c.name == "A").subMenu;
            var clonedB = result.controls.Single(c => c.name == "B").subMenu;
            Assert.That(clonedA, Is.Not.SameAs(clonedB), "each reference path got its own clone");
            Assert.That(clonedA.controls.Select(c => c.name), Does.Contain("Item"), "A keeps it through the applier path");
            Assert.That(clonedB.controls.Any(c => c.name == "Item"), Is.False, "B loses it through the applier path");
            Assert.That(pending, Is.Empty);

            avatar.Dispose();
        }

        [Test]
        public void FinalPassWarnsForWhatIsLeft()
        {
            var root = Menu(Toggle("A"));
            var pending = new List<PendingEdit> { Pending(new MenuEdit { source = new List<string> { "Missing" }, hide = true }) };
            var messages = new List<ModelMessage>();

            MenuEditApplier.Apply(root, pending, m => spawned.Add(m), messages, true);

            Assert.That(messages.Any(m => m.Key == "ad.warn.edit_source_missing"), Is.True);
        }

        [Test]
        public void NonFinalPassStaysQuiet()
        {
            var root = Menu(Toggle("A"));
            var pending = new List<PendingEdit> { Pending(new MenuEdit { source = new List<string> { "Missing" }, hide = true }) };
            var messages = new List<ModelMessage>();

            MenuEditApplier.Apply(root, pending, m => spawned.Add(m), messages, false);

            Assert.That(messages, Is.Empty);
            Assert.That(pending, Has.Count.EqualTo(1));
        }

        [Test]
        [Timeout(5000)]
        public void CyclesInMenuAssetsDoNotHang()
        {
            var loop = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            spawned.Add(loop);
            var root = Menu(Toggle("A"), Submenu("Loop", loop));
            loop.controls = new List<VRCExpressionsMenu.Control> { Submenu("Back", root) };
            var pending = new List<PendingEdit> { Pending(new MenuEdit { source = new List<string> { "A" }, hide = true }) };
            var messages = new List<ModelMessage>();

            VRCExpressionsMenu result = null;
            Assert.That(() => result = MenuEditApplier.Apply(root, pending, m => spawned.Add(m), messages, false), Throws.Nothing);

            Assert.That(result, Is.Not.SameAs(root));
            Assert.That(result.controls.Any(c => c.name == "A"), Is.False);
            Assert.That(pending, Is.Empty);
        }

#if DOCME_MA
        [Test]
        public void EditsOnModularAvatarItemsApplyInTheFullPipeline()
        {
            var avatar = TestAvatar.Create();
            var menu = avatar.AddMenu();
            menu.menuEdits.Add(new MenuEdit { source = new List<string> { "Gift" }, name = "Present" });

            var child = new GameObject("Gift");
            child.transform.SetParent(avatar.Root.transform);
            child.AddComponent<ModularAvatarMenuInstaller>();
            var item = child.AddComponent<ModularAvatarMenuItem>();
            item.Control = new VRCExpressionsMenu.Control
            {
                name = "Gift",
                type = VRCExpressionsMenu.Control.ControlType.Button,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "" },
            };

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                var descriptor = processed.GetComponent<VRCAvatarDescriptor>();
                var names = descriptor.expressionsMenu.controls.Select(c => c.name).ToList();
                Assert.That(names, Does.Contain("Present"));
                Assert.That(names, Does.Not.Contain("Gift"));
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                avatar.Dispose();
                GeneratedAssets.CleanUp();
            }
        }

        [Test]
        public void LateEditReachesAModularAvatarItemPushedIntoAMorePage()
        {
            var avatar = TestAvatar.Create();
            var full = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            full.name = "Root";
            full.controls = Enumerable.Range(1, VRCExpressionsMenu.MAX_CONTROLS).Select(i => TestAvatar.ToggleControl("Fill" + i, "Existing")).ToList();
            spawned.Add(full);
            avatar.Descriptor.expressionsMenu = full;

            var menu = avatar.AddMenu();
            menu.menuEdits.Add(new MenuEdit { source = new List<string> { "Hat" }, name = "Cap" });

            var child = new GameObject("Hat");
            child.transform.SetParent(avatar.Root.transform);
            child.AddComponent<ModularAvatarMenuInstaller>();
            var item = child.AddComponent<ModularAvatarMenuItem>();
            item.Control = new VRCExpressionsMenu.Control
            {
                name = "Hat",
                type = VRCExpressionsMenu.Control.ControlType.Button,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "" },
            };

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                var root = processed.GetComponent<VRCAvatarDescriptor>().expressionsMenu;
                var more = root.controls.Single(c => c.name == "More").subMenu;
                var names = root.controls.Select(c => c.name).Concat(more.controls.Select(c => c.name)).ToList();
                Assert.That(names, Does.Contain("Cap"));
                Assert.That(names, Does.Not.Contain("Hat"));
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                avatar.Dispose();
                GeneratedAssets.CleanUp();
            }
        }
#endif

        [Test]
        public void LeftoverEditWarnsWithoutFailingTheBuild()
        {
            var avatar = TestAvatar.Create();
            var menu = avatar.AddMenu();
            menu.menuEdits.Add(new MenuEdit { id = "e1", source = new List<string> { "NeverThere" }, hide = true });

            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var errors = ErrorReport.CaptureErrors(() =>
            {
                AvatarDoctorBuilder.Run(context);
                MenuEditApplier.RunFinal(context);
            });
            context.DeactivateAllExtensionContexts();

            var missing = errors.Where(e => e.TheError is SimpleError s && s.TitleKey == "ad.warn.edit_source_missing").ToList();
            Assert.That(missing, Has.Count.EqualTo(1), "the leftover edit is reported once");
            Assert.That(errors.Any(e => e.TheError.Severity == ErrorSeverity.InternalError), Is.False, "and nothing threw");

            avatar.Dispose();
        }

        [Test]
        public void SecondOverridePassIsQuiet()
        {
            var avatar = TestAvatar.Create();
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var state = context.GetState<OverrideState>();
            state.Ready = true;
            state.Rules.Add(new OverrideRule { Renderer = avatar.Body, Shape = "Blush", Value = 0f, EntryName = "Silence blush", EntryId = "r1" });
            state.Rules.Add(new OverrideRule { Renderer = avatar.Body, Shape = "Eye_Big", Value = 50f, EntryName = "Big eyes", EntryId = "r2" });
            state.Rules.Add(new OverrideRule { Renderer = avatar.Body, Shape = "Nothing", Value = 10f, EntryName = "Never used", EntryId = "r3" });

            var errors = ErrorReport.CaptureErrors(() =>
            {
                OverrideBuilder.RunAfterModularAvatar(context);
                // A clip that only exists once the other tools have run, as VRCFury's do.
                var fxController = asc.ControllerContext.Controllers[VRCAvatarDescriptor.AnimLayerType.FX];
                var late = VirtualClip.Create("Wink");
                late.SetFloatCurve(EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"),
                    new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 100f)));
                fxController.AddLayer(LayerPriority.Default, "Late").StateMachine.AddState("Wink", late, Vector3.zero);
                OverrideBuilder.RunFinal(context);
            });
            context.DeactivateAllExtensionContexts();

            var warnings = errors.Where(e => e.TheError is SimpleError s && s.TitleKey == "ad.warn.override_unused").ToList();
            Assert.That(warnings, Has.Count.EqualTo(1), "only the rule that never found anything warns");
            var applied = errors.Where(e => e.TheError is SimpleError s && (s.TitleKey == "ad.info.override_applied" || s.TitleKey == "ad.info.override_applied.one")).ToList();
            Assert.That(applied, Has.Count.EqualTo(2), "each rule that pinned something says so once");

            var fx = avatar.Descriptor.baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
            var motion = fx.layers.Single(l => l.name == "Base").stateMachine.states.Single(s => s.state.name == "Smile").state.motion;
            var curve = AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"));
            Assert.That(curve.Evaluate(0f), Is.EqualTo(0f));
            Assert.That(curve.Evaluate(1f), Is.EqualTo(0f));

            avatar.Dispose();
        }

        [Test]
        public void FinalPassClosesTheAnimatorContextItOpened()
        {
            var avatar = TestAvatar.Create();
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var context = new BuildContext(avatar.Root, null);
            var state = context.GetState<OverrideState>();
            state.Ready = true;
            state.Rules.Add(new OverrideRule { Renderer = avatar.Body, Shape = "Blush", Value = 0f, EntryName = "Silence blush", EntryId = "r1" });

            ErrorReport.CaptureErrors(() => AvatarDoctorPlugin.FinishAfterOtherTools(context));

            Assert.That(() => context.Extension<AnimatorServicesContext>(), Throws.Exception, "the pass leaves nothing open behind it");
            var fx = avatar.Descriptor.baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
            var motion = fx.layers.Single(l => l.name == "Base").stateMachine.states.Single(s => s.state.name == "Smile").state.motion;
            var curve = AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"));
            Assert.That(curve.Evaluate(1f), Is.EqualTo(0f), "and the pinned controller was committed back to the descriptor");

            context.DeactivateAllExtensionContexts();
            avatar.Dispose();
        }

        [Test]
        public void OverrideStillReportsWhatItPinnedWhenTheRendererGoesAway()
        {
            var avatar = TestAvatar.Create();
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var state = context.GetState<OverrideState>();
            state.Ready = true;
            var rule = new OverrideRule { Renderer = avatar.Body, Shape = "Blush", Value = 0f, EntryName = "Silence blush", EntryId = "r1" };
            state.Rules.Add(rule);

            var errors = ErrorReport.CaptureErrors(() =>
            {
                OverrideBuilder.RunAfterModularAvatar(context);
                // An optimizer took the renderer away between the two runs.
                rule.Renderer = null;
                OverrideBuilder.RunFinal(context);
            });
            context.DeactivateAllExtensionContexts();

            var applied = errors.Where(e => e.TheError is SimpleError s && (s.TitleKey == "ad.info.override_applied" || s.TitleKey == "ad.info.override_applied.one")).ToList();
            Assert.That(applied, Has.Count.EqualTo(1), "what the rule did pin is still reported");
            Assert.That(errors.Any(e => e.TheError is SimpleError s && s.TitleKey == "ad.warn.override_target_gone"), Is.False,
                "and the renderer was there when it mattered, so nothing complains about it");

            avatar.Dispose();
        }

        [Test]
        public void SecondOverridePassDoesNotRepeatTargetGone()
        {
            var avatar = TestAvatar.Create();
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var state = context.GetState<OverrideState>();
            state.Ready = true;
            state.Rules.Add(new OverrideRule { Renderer = null, Shape = "Blush", Value = 0f, EntryName = "Silence blush", EntryId = "r6" });

            var errors = ErrorReport.CaptureErrors(() =>
            {
                OverrideBuilder.RunAfterModularAvatar(context);
                OverrideBuilder.RunFinal(context);
            });
            context.DeactivateAllExtensionContexts();

            var warnings = errors.Where(e => e.TheError is SimpleError s && s.TitleKey == "ad.warn.override_target_gone").ToList();
            Assert.That(warnings, Has.Count.EqualTo(1));

            avatar.Dispose();
        }
    }
}
