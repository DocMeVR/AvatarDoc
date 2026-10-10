#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// A TreeView owns a drag handler of its own: in an editor panel it answers every drag update and every drop
    /// itself, rejects what does not come from the tree, and stops the event. The tree's own drop handlers (an object
    /// from the Hierarchy, a controller or an expressions menu from the Project window) therefore have to see the
    /// event first, and stop it only when they take the drop. These send the same drag events a real drag does to a
    /// row the tree itself made, in a shown window, with the real handler in the way.
    /// </summary>
    public class TreeDropHandlerTests
    {
        TestAvatar avatar;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
            DragAndDrop.PrepareStartDrag();
        }

        [TearDown]
        public void TearDown()
        {
            DragAndDrop.PrepareStartDrag();
            foreach (var doc in avatar.Root.GetComponentsInChildren<AvatarDoc>(true)) Undo.ClearUndo(doc);
            avatar.Dispose();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        static AvatarDoctorView View(AvatarDoc menu) => new AvatarDoctorView(new SerializedObject(menu), menu);

        static void Send<T>(VisualElement target) where T : EventBase<T>, new()
        {
            using (var e = EventBase<T>.GetPooled())
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        static VisualElement RealisedRow(AvatarDoctorView view, string entryId)
        {
            LayoutProbe.Validate(view);
            var row = view.Tree.Tree.Query(className: "ad-tree-row").ToList().FirstOrDefault(r => (r.userData as TreeRow)?.EntryId == entryId);
            Assert.That(row, Is.Not.Null, "the tree made a row for the entry in the shown window");
            return row;
        }

        sealed class Reached
        {
            public bool Updated;
            public bool Performed;
        }

        /// <summary>Listens on the TreeView itself, in the ordinary phase, after its own drag handler: an event the tree's handler stopped higher up the path never gets here, while one it merely answered still does.</summary>
        static Reached ListenOnTheTree(AvatarDoctorView view)
        {
            var reached = new Reached();
            view.Tree.Tree.RegisterCallback<DragUpdatedEvent>(_ => reached.Updated = true);
            view.Tree.Tree.RegisterCallback<DragPerformEvent>(_ => reached.Performed = true);
            return reached;
        }

        static void Drag(params Object[] dragged)
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = dragged;
        }

        [Test]
        public void AHierarchyObjectDroppedOnARowAddsAToggle()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                var row = RealisedRow(view, hat.id);
                var reached = ListenOnTheTree(view);
                Drag(avatar.Glasses);
                try
                {
                    Send<DragUpdatedEvent>(row);
                    Assert.That(DragAndDrop.visualMode, Is.EqualTo(DragAndDropVisualMode.Generic), "the cursor says the drop is welcome");
                    Assert.That(reached.Updated, Is.False, "and the tree's own handler never answers it with a refusal");

                    Send<DragPerformEvent>(row);
                    Assert.That(reached.Performed, Is.False, "the tree's own handler never sees the drop");
                }
                finally
                {
                    Send<DragExitedEvent>(row);
                    DragAndDrop.PrepareStartDrag();
                }

                var added = menu.entries.SingleOrDefault(e => e.name == "Glasses");
                Assert.That(added, Is.Not.Null, "a toggle named after the object was added");
                Assert.That(added.kind, Is.EqualTo(EntryKind.Toggle));
                Assert.That(added.objects.Select(o => o.target), Is.EqualTo(new Object[] { avatar.Glasses }));
            });
        }

        [Test]
        public void TheDropHighlightGoesWhenADragEndsOverARow()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                var row = RealisedRow(view, hat.id);
                Drag(avatar.Glasses);
                try
                {
                    Send<DragUpdatedEvent>(row);
                    Assert.That(view.Tree.ClassListContains("ad-drop-active"), Is.True, "the tree shows it takes the drop");

                    Send<DragExitedEvent>(row);

                    Assert.That(view.Tree.ClassListContains("ad-drop-active"), Is.False, "the tree's own handler stops the end of a drag, and the highlight still goes");
                }
                finally
                {
                    DragAndDrop.PrepareStartDrag();
                }
            });
        }

        [Test]
        public void ADraggedAnimatorControllerDroppedOnARowIsMerged()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var controller = new AnimatorController { name = "Dropped" };
            var view = View(menu);
            try
            {
                LayoutProbe.Measure(view, () =>
                {
                    var row = RealisedRow(view, hat.id);
                    var reached = ListenOnTheTree(view);
                    Drag(controller);
                    try
                    {
                        Send<DragUpdatedEvent>(row);
                        Assert.That(DragAndDrop.visualMode, Is.EqualTo(DragAndDropVisualMode.Generic));
                        Assert.That(reached.Updated, Is.False);
                        Send<DragPerformEvent>(row);
                        Assert.That(reached.Performed, Is.False);
                    }
                    finally
                    {
                        Send<DragExitedEvent>(row);
                        DragAndDrop.PrepareStartDrag();
                    }

                    Assert.That(menu.animators.Select(a => a.controller), Is.EqualTo(new Object[] { controller }));
                });
            }
            finally
            {
                Undo.ClearUndo(menu);
                Object.DestroyImmediate(controller);
            }
        }

        [Test]
        public void ADraggedExpressionsMenuDroppedOnARowIsInstalled()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var asset = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            asset.name = "Pack";
            asset.controls = new System.Collections.Generic.List<VRCExpressionsMenu.Control> { TestAvatar.ToggleControl("Alpha", "Pack/Alpha") };
            var view = View(menu);
            try
            {
                LayoutProbe.Measure(view, () =>
                {
                    var row = RealisedRow(view, hat.id);
                    var reached = ListenOnTheTree(view);
                    Drag(asset);
                    try
                    {
                        Send<DragUpdatedEvent>(row);
                        Assert.That(DragAndDrop.visualMode, Is.EqualTo(DragAndDropVisualMode.Generic));
                        Assert.That(reached.Updated, Is.False);
                        Send<DragPerformEvent>(row);
                        Assert.That(reached.Performed, Is.False);
                    }
                    finally
                    {
                        Send<DragExitedEvent>(row);
                        DragAndDrop.PrepareStartDrag();
                    }

                    Assert.That(menu.menuAssets.Select(a => a.menu), Is.EqualTo(new Object[] { asset }));
                });
            }
            finally
            {
                Undo.ClearUndo(menu);
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ADragWithNothingFromOutsideStillReachesTheTreesOwnHandler()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                var row = RealisedRow(view, hat.id);
                var reached = ListenOnTheTree(view);
                Drag();
                try
                {
                    Send<DragUpdatedEvent>(row);
                    Send<DragPerformEvent>(row);
                }
                finally
                {
                    Send<DragExitedEvent>(row);
                    DragAndDrop.PrepareStartDrag();
                }

                Assert.That(reached.Updated, Is.True, "a reorder of the tree's own rows carries on to the tree, as before");
                Assert.That(reached.Performed, Is.True);
                Assert.That(menu.entries.Count, Is.EqualTo(1), "and the tree's handlers added nothing");
            });
        }

        [Test]
        public void ALockedTreeTakesNoDropAndLeavesTheEventToTheTree()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            menu.locked = true;
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                var row = RealisedRow(view, hat.id);
                var reached = ListenOnTheTree(view);
                Drag(avatar.Glasses);
                try
                {
                    Send<DragUpdatedEvent>(row);
                    Send<DragPerformEvent>(row);
                }
                finally
                {
                    Send<DragExitedEvent>(row);
                    DragAndDrop.PrepareStartDrag();
                }

                Assert.That(menu.entries.Count, Is.EqualTo(1), "a locked component takes no toggle from the Hierarchy");
                Assert.That(reached.Updated, Is.True, "and the tree answers the drag as it did before");
            });
        }
    }
}
#endif
