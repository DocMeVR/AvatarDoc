#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Refresh work that grew with the avatar.</summary>
    public class RefreshCostTests
    {
        TestAvatar avatar;
        readonly List<Object> extras = new List<Object>();

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
            foreach (var extra in extras)
                if (extra != null && !AssetDatabase.Contains(extra)) Object.DestroyImmediate(extra);
            extras.Clear();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        AvatarDoctorView View(AvatarDoc menu) => new AvatarDoctorView(new SerializedObject(menu), menu);

        [Test]
        public void AnAnimatorPanelReadsTheAvatarsParametersOncePerRebuild()
        {
            var menu = avatar.AddMenu();
            var controller = new AnimatorController { name = "Merged" };
            extras.Add(controller);
            for (int i = 0; i < 5; i++) controller.AddParameter("Loose" + i, AnimatorControllerParameterType.Float);
            menu.animators.Add(AnimatorMerge.Create(controller));
            var view = View(menu);
            view.Tree.SelectKey(view.Tree.RowsOfKind(RowKind.Animator).Single().Key);
            var panel = view.Detail.Q<AnimatorPanel>();
            Assert.That(panel, Is.Not.Null);
            int before = AnimatorPanel.ExistingReads;

            panel.Rebuild();

            Assert.That(AnimatorPanel.ExistingReads - before, Is.EqualTo(1), "five parameter rows ask one map of the avatar's parameters");
            var rows = panel.Q<VisualElement>("ad-animator-parameters").Children().ToList();
            Assert.That(rows.Count, Is.EqualTo(5));
            Assert.That(rows.All(r => r.Q<Label>("ad-animator-param-declared").text == L.Text("ad.ui.animator.not_synced")), Is.True, "and each row still says who declares it");
        }

        [Test]
        public void AMenuAssetPanelReadsTheAvatarsParametersOncePerRebuild()
        {
            var asset = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            asset.name = "Pack";
            asset.controls = Enumerable.Range(0, 5).Select(i => TestAvatar.ToggleControl("Item" + i, "Pack/Item" + i)).ToList();
            extras.Add(asset);
            var menu = avatar.AddMenu();
            var install = MenuAssetInstall.Create(asset);
            install.asSubmenu = true;
            install.name = "Extra";
            menu.menuAssets.Add(install);
            var view = View(menu);
            view.Tree.SelectKey(install.id);
            var panel = view.Detail.Q<MenuAssetPanel>();
            Assert.That(panel, Is.Not.Null);
            int before = MenuAssetPanel.ExistingReads;

            panel.Rebuild();

            Assert.That(MenuAssetPanel.ExistingReads - before, Is.EqualTo(1), "five undeclared parameters ask one map of the avatar's parameters");
            Assert.That(panel.Q<Button>("ad-menu-asset-declare-Pack/Item4"), Is.Not.Null, "and each still offers its declaration");
        }

        static void Pointer<T>(VisualElement target, EventType type, int button) where T : PointerEventBase<T>, new()
        {
            using (var e = PointerEventBase<T>.GetPooled(new Event { type = type, button = button, mousePosition = target.worldBound.center }))
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        [Test]
        public void TheShapesValueSliderSavesEveryMoveAndPlansOnRelease()
        {
            var piece = new GameObject("Piece");
            piece.transform.SetParent(avatar.Root.transform);
            var pieceMesh = piece.AddComponent<SkinnedMeshRenderer>();
            var meshAsset = TestAvatar.QuadWithShapes("Blush");
            extras.Add(meshAsset);
            pieceMesh.sharedMesh = meshAsset;
            var shapes = piece.AddComponent<AvatarDocShapes>();
            shapes.whileOn.Add(new ShapeWhileOn { mesh = avatar.Body, shape = "Eye_Big", value = 50f });
            UnityEditor.Editor editor = null;
            try
            {
                editor = UnityEditor.Editor.CreateEditor(shapes);
                var shapesEditor = (AvatarDocShapesEditor)editor;
                var root = shapesEditor.CreateInspectorGUI();
                LayoutProbe.Measure(root, () =>
                {
                    var slider = root.Q<Slider>("ad-shapes-while-on-value-0");
                    Assert.That(slider, Is.Not.Null);
                    int refreshes = shapesEditor.MessageRefreshes;

                    // What a drag does per pointer move: the slider takes a new value and raises its change.
                    slider.value = 60f;
                    slider.value = 70f;
                    slider.value = 80f;

                    Assert.That(shapes.whileOn[0].value, Is.EqualTo(80f), "every move is saved");
                    Assert.That(shapesEditor.MessageRefreshes, Is.EqualTo(refreshes), "no move plans the avatar again");

                    var dragArea = slider.Q(className: "unity-base-slider__drag-container") ?? slider;
                    Pointer<PointerUpEvent>(dragArea, EventType.MouseUp, 0);

                    Assert.That(shapesEditor.MessageRefreshes, Is.EqualTo(refreshes + 1), "letting go shows what the new value means");
                });
            }
            finally
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Undo.ClearUndo(shapes);
            }
        }

        [Test]
        public void TheWelcomeScanWaitsForItsOwnAvatarsPlanToChange()
        {
            using (var other = TestAvatar.Create())
            {
                var menu = avatar.AddMenu();
                other.AddMenu();
                var view = View(menu);
                Assert.That(view.Tree.Q<VisualElement>("ad-welcome-card").ClassListContains("ad-hidden"), Is.False, "an empty component shows the welcome card");
                int scans = view.Tree.WelcomeScans;
                Assert.That(scans, Is.GreaterThan(0), "the card scanned once for something to convert");

                AvatarPlanCache.InvalidateMenu(other.Root.transform);
                view.Refresh();
                AvatarPlanCache.NoteChangedObjects(new Object[] { avatar.Hat.transform });
                view.Refresh();

                Assert.That(view.Tree.WelcomeScans, Is.EqualTo(scans), "an edit on another avatar and a drag under this one change nothing it could convert");

                AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
                view.Refresh();

                Assert.That(view.Tree.WelcomeScans, Is.EqualTo(scans + 1), "a change to its own avatar's plan scans again");
            }
        }
    }
}
#endif
