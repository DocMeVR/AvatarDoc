#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Unity does not rebuild a UI Toolkit inspector for an ordinary Undo, and the Shapes inspector's cards and
    /// buttons hold the array positions they were built for. The inspector has to follow Undo and Redo itself, or
    /// its x buttons and pickers act on the wrong row.
    /// </summary>
    public class ShapesInspectorUndoTests
    {
        TestAvatar avatar;
        Mesh pieceMesh;
        AvatarDocShapes shapesUnderTest;
        UnityEditor.Editor editor;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            AvatarPlanCache.Invalidate();
        }

        [TearDown]
        public void TearDown()
        {
            if (editor != null) Object.DestroyImmediate(editor);
            editor = null;
            if (shapesUnderTest != null) Undo.ClearUndo(shapesUnderTest);
            shapesUnderTest = null;
            avatar.Dispose();
            if (pieceMesh != null) Object.DestroyImmediate(pieceMesh);
            AvatarPlanCache.Invalidate();
        }

        AvatarDocShapes ThreeFollowRows()
        {
            avatar.AddBodyShapes("Chest");
            var piece = new GameObject("Piece");
            piece.transform.SetParent(avatar.Root.transform);
            var renderer = piece.AddComponent<SkinnedMeshRenderer>();
            pieceMesh = TestAvatar.QuadWithShapes("Blush", "Eye_Big", "Chest");
            renderer.sharedMesh = pieceMesh;
            var shapes = piece.AddComponent<AvatarDocShapes>();
            foreach (var shape in new[] { "Blush", "Eye_Big", "Chest" })
                shapes.follow.Add(new ShapeFollow { sourceMesh = avatar.Body, sourceShape = shape });
            shapesUnderTest = shapes;
            return shapes;
        }

        static List<string> Cards(VisualElement root)
        {
            var shown = new List<string>();
            for (int i = 0; root.Q<VisualElement>("ad-shapes-follow-" + i) != null; i++)
                shown.Add(root.Q<Button>("ad-shapes-follow-shape-" + i).text);
            return shown;
        }

        static void PressRemoveOnCardShowing(VisualElement root, string shape)
        {
            var index = Cards(root).IndexOf(shape);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "a card shows " + shape);
            UiTestEvents.SimulateClick(root.Q<Button>("ad-shapes-follow-remove-" + index));
        }

        [Test]
        public void TheInspectorShowsEveryRowAgainAfterAnUndo()
        {
            var shapes = ThreeFollowRows();
            editor = UnityEditor.Editor.CreateEditor(shapes);
            var root = ((AvatarDocShapesEditor)editor).CreateInspectorGUI();
            Assert.That(Cards(root), Is.EqualTo(new[] { "Blush", "Eye_Big", "Chest" }), "one card per follow row");

            Undo.IncrementCurrentGroup();
            PressRemoveOnCardShowing(root, "Blush");
            Assert.That(Cards(root), Is.EqualTo(new[] { "Eye_Big", "Chest" }), "removing the first leaves the other two");

            Undo.PerformUndo();

            Assert.That(shapes.follow.Count, Is.EqualTo(3), "the undo brought the row back");
            Assert.That(Cards(root), Is.EqualTo(new[] { "Blush", "Eye_Big", "Chest" }), "and the inspector shows it again, in its place");
        }

        [Test]
        public void AfterAnUndoTheRemoveButtonsActOnTheirOwnCards()
        {
            var shapes = ThreeFollowRows();
            editor = UnityEditor.Editor.CreateEditor(shapes);
            var root = ((AvatarDocShapesEditor)editor).CreateInspectorGUI();
            Undo.IncrementCurrentGroup();
            PressRemoveOnCardShowing(root, "Blush");
            Undo.PerformUndo();

            PressRemoveOnCardShowing(root, "Chest");

            Assert.That(shapes.follow.Select(r => r.sourceShape), Is.EqualTo(new[] { "Blush", "Eye_Big" }), "the card that said Chest removed the Chest row, not the one beside it");
        }

        [Test]
        public void TheInspectorFollowsARedoToo()
        {
            var shapes = ThreeFollowRows();
            editor = UnityEditor.Editor.CreateEditor(shapes);
            var root = ((AvatarDocShapesEditor)editor).CreateInspectorGUI();
            Undo.IncrementCurrentGroup();
            PressRemoveOnCardShowing(root, "Blush");
            Undo.PerformUndo();
            Assert.That(Cards(root).Count, Is.EqualTo(3));

            Undo.PerformRedo();

            Assert.That(shapes.follow.Count, Is.EqualTo(2), "the redo removed the row again");
            Assert.That(Cards(root), Is.EqualTo(new[] { "Eye_Big", "Chest" }), "and the inspector shows two cards again");
        }

        [Test]
        public void ADestroyedInspectorStopsListeningToUndo()
        {
            var shapes = ThreeFollowRows();
            int before = Undo.undoRedoPerformed == null ? 0 : Undo.undoRedoPerformed.GetInvocationList().Length;
            editor = UnityEditor.Editor.CreateEditor(shapes);
            ((AvatarDocShapesEditor)editor).CreateInspectorGUI();
            Assert.That(Undo.undoRedoPerformed.GetInvocationList().Length, Is.EqualTo(before + 1), "a live inspector listens for undo");

            Object.DestroyImmediate(editor);
            editor = null;

            int after = Undo.undoRedoPerformed == null ? 0 : Undo.undoRedoPerformed.GetInvocationList().Length;
            Assert.That(after, Is.EqualTo(before), "and lets go of it when it is destroyed");
        }
    }
}
#endif
