#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The creator deleted an empty toggle on their real avatar and the entry panel then read past the end of a
    /// serialized list on every scheduled refresh. These drive the same delete and refresh paths and check that
    /// every target list the panel shows still matches its serialized array afterwards.
    /// </summary>
    public class DeleteRefreshTests
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

        static AvatarDoctorView View(AvatarDoc menu) => new AvatarDoctorView(new SerializedObject(menu), menu);

        static void AssertListsConsistent(AvatarDoctorView view, string when)
        {
            var shown = view.Detail.Row;
            if (shown == null || !shown.IsOwnEntry) return;
            var entryProperty = view.EntryProperty(shown.Entry);
            Assert.That(entryProperty, Is.Not.Null, when + ": the shown entry is no longer on the component");
            var lists = view.Detail.Query<TargetListView>().Build().ToList();
            foreach (var list in lists)
                Assert.That(list.DescribesEntry(entryProperty), Is.True, when + ": a target list of the shown panel is bound to " + list.BoundPath + ", not to the shown entry at " + entryProperty.propertyPath);
        }

        static (MenuEntry empty, MenuEntry rich, MenuEntry last) ThreeEntries(TestAvatar avatar, AvatarDoc menu)
        {
            var empty = menu.AddEntry(EntryKind.Toggle, "Empty");
            var rich = menu.AddEntry(EntryKind.Toggle, "Rich");
            rich.objects.Add(new ObjectTarget { target = avatar.Hat });
            rich.follows.Add(new FollowTarget { target = avatar.Hat, follows = avatar.Glasses.transform });
            rich.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", maxWeight = 100f });
            var last = menu.AddEntry(EntryKind.Toggle, "Last");
            last.objects.Add(new ObjectTarget { target = avatar.Glasses });
            return (empty, rich, last);
        }

        [Test]
        public void DeletingTheSelectedEmptyToggleLeavesThePanelConsistent()
        {
            var menu = avatar.AddMenu();
            var (empty, rich, last) = ThreeEntries(avatar, menu);
            var view = View(menu);
            view.Tree.SelectEntry(empty.id);

            view.Tree.DeleteSelected();
            AssertListsConsistent(view, "right after the delete");
            view.Refresh();
            AssertListsConsistent(view, "after the first scheduled refresh");
            view.Refresh();
            AssertListsConsistent(view, "after a second refresh");
            Assert.That(menu.entries.Contains(empty), Is.False);
        }

        [Test]
        public void DeletingAnEarlierRowWhileALaterOneIsShownLeavesThePanelConsistent()
        {
            var menu = avatar.AddMenu();
            var (empty, rich, last) = ThreeEntries(avatar, menu);
            var view = View(menu);
            view.Tree.SelectEntry(rich.id);

            var rowEmpty = view.Tree.RowFor(empty.id);
            var delete = view.Tree.RowActions(rowEmpty).First(x => x.LabelKey == "ad.ui.menu.delete");
            delete.Run();
            AssertListsConsistent(view, "right after the delete");
            view.Refresh();
            AssertListsConsistent(view, "after the first scheduled refresh");
            view.Refresh();
            AssertListsConsistent(view, "after a second refresh");
        }

        [Test]
        public void DeletingTheLastRowWhileAnEarlierOneIsShownLeavesThePanelConsistent()
        {
            var menu = avatar.AddMenu();
            var (empty, rich, last) = ThreeEntries(avatar, menu);
            var view = View(menu);
            view.Tree.SelectEntry(rich.id);

            var rowLast = view.Tree.RowFor(last.id);
            var delete = view.Tree.RowActions(rowLast).First(x => x.LabelKey == "ad.ui.menu.delete");
            delete.Run();
            AssertListsConsistent(view, "right after the delete");
            view.Refresh();
            AssertListsConsistent(view, "after the first scheduled refresh");
        }

        [Test]
        public void DeletingTheShownRichToggleLeavesThePanelConsistent()
        {
            var menu = avatar.AddMenu();
            var (empty, rich, last) = ThreeEntries(avatar, menu);
            var view = View(menu);
            view.Tree.SelectEntry(rich.id);

            view.Tree.DeleteSelected();
            AssertListsConsistent(view, "right after the delete");
            view.Refresh();
            AssertListsConsistent(view, "after the first scheduled refresh");
        }

        [Test]
        public void AnAnimatorPanelFollowsItsMergeAfterADeleteAndUndo()
        {
            var menu = avatar.AddMenu();
            var controllerA = new AnimatorController { name = "A" };
            var controllerB = new AnimatorController { name = "B" };
            try
            {
                var view = View(menu);
                var first = view.Tree.AddAnimator(controllerA);
                var second = view.Tree.AddAnimator(controllerB);
                view.Tree.SelectKey(first.id);
                Undo.IncrementCurrentGroup();
                view.Tree.DeleteSelected();
                view.Tree.SelectKey(second.id);
                Assert.That(view.Detail.BoundPropertyPath, Is.EqualTo(view.MergeProperty(view.Detail.Row.Merge).propertyPath), "alone in the list, B is the first element");

                Undo.PerformUndo();
                view.Refresh();

                Assert.That(menu.animators.Count, Is.EqualTo(2), "the undo brought A back in front of B");
                Assert.That(view.Detail.Row.Key, Is.EqualTo(second.id), "B is still the row shown");
                Assert.That(view.Detail.BoundPropertyPath, Is.EqualTo(view.MergeProperty(view.Detail.Row.Merge).propertyPath),
                    "B's panel is bound to B's place in the list, not to the first element that is A now");
                Assert.That(view.Detail.Q<AnimatorPanel>().Q<ObjectField>("ad-animator-controller").value, Is.SameAs(controllerB),
                    "and shows B's controller");
            }
            finally
            {
                Undo.ClearUndo(menu);
                Object.DestroyImmediate(controllerA);
                Object.DestroyImmediate(controllerB);
            }
        }

        [Test]
        public void AMenuAssetPanelFollowsItsInstallAfterADeleteAndUndo()
        {
            var menu = avatar.AddMenu();
            var assetA = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            var assetB = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            assetA.name = "A";
            assetB.name = "B";
            assetA.controls = new List<VRCExpressionsMenu.Control> { TestAvatar.ToggleControl("Alpha", "Pack/Alpha") };
            assetB.controls = new List<VRCExpressionsMenu.Control> { TestAvatar.ToggleControl("Beta", "Pack/Beta") };
            try
            {
                var view = View(menu);
                var first = view.Tree.AddMenuAsset(assetA);
                var second = view.Tree.AddMenuAsset(assetB);
                view.Tree.SelectKey(first.id);
                Undo.IncrementCurrentGroup();
                view.Tree.DeleteSelected();
                view.Tree.SelectKey(second.id);
                Assert.That(view.Detail.BoundPropertyPath, Is.EqualTo(view.MenuAssetProperty(view.Detail.Row.Asset).propertyPath), "alone in the list, B is the first element");

                Undo.PerformUndo();
                view.Refresh();

                Assert.That(menu.menuAssets.Count, Is.EqualTo(2), "the undo brought A back in front of B");
                Assert.That(view.Detail.Row.Key, Is.EqualTo(second.id), "B is still the row shown");
                Assert.That(view.Detail.BoundPropertyPath, Is.EqualTo(view.MenuAssetProperty(view.Detail.Row.Asset).propertyPath),
                    "B's panel is bound to B's place in the list, not to the first element that is A now");
                Assert.That(view.Detail.Q<MenuAssetPanel>().Q<ObjectField>("ad-menu-asset-menu").value, Is.SameAs(assetB),
                    "and shows B's asset");
            }
            finally
            {
                Undo.ClearUndo(menu);
                Object.DestroyImmediate(assetA);
                Object.DestroyImmediate(assetB);
            }
        }

        [Test]
        public void AToggleThatDrivesATriggerDoesNotWarnAboutNoEffects()
        {
            var menu = avatar.AddMenu();
            var gate = menu.AddEntry(EntryKind.Toggle, "Headpat");
            var trigger = menu.AddEntry(EntryKind.Trigger, "Headpat trigger");
            trigger.source = TriggerSource.Toggle;
            trigger.sourceToggleId = gate.id;
            trigger.objects.Add(new ObjectTarget { target = avatar.Hat });
            AvatarPlanCache.Invalidate();
            var view = View(menu);

            Assert.That(view.Plan.Messages.Any(m => m.Key == "ad.warn.no_effects" && m.EntryIds.Contains(gate.id)), Is.False,
                "a toggle a trigger reads as its source is not an empty toggle");
        }
    }
}
#endif
