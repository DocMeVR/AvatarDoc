using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>MenuEditing reads and writes MenuEdit entries for existing and foreign rows, with Undo.</summary>
    public class MenuEditingTests
    {
        TestAvatar avatar;
        VRCExpressionsMenu existingRoot;
        VRCExpressionsMenu groupSubmenu;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            groupSubmenu = Menu(Toggle("Inner", "Inner"));
            groupSubmenu.name = "Group";
            existingRoot = Menu(Submenu("Group", groupSubmenu), Toggle("Ear_Type", "Ear"), Toggle("Breast", "Breast"));
            existingRoot.name = "Root";
            avatar.Descriptor.expressionsMenu = existingRoot;
            AvatarPlanCache.Invalidate();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            Object.DestroyImmediate(existingRoot);
            Object.DestroyImmediate(groupSubmenu);
            AvatarPlanCache.Invalidate();
        }

        static VRCExpressionsMenu Menu(params VRCExpressionsMenu.Control[] controls)
        {
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.controls = controls.ToList();
            return menu;
        }

        static VRCExpressionsMenu.Control Toggle(string name, string parameter)
        {
            return new VRCExpressionsMenu.Control { name = name, type = VRCExpressionsMenu.Control.ControlType.Toggle, parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter }, value = 1 };
        }

        static VRCExpressionsMenu.Control Submenu(string name, VRCExpressionsMenu menu)
        {
            return new VRCExpressionsMenu.Control { name = name, type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = menu, parameter = new VRCExpressionsMenu.Control.Parameter { name = "" } };
        }

        AvatarDoctorView View(AvatarDoc menu) => new AvatarDoctorView(new SerializedObject(menu), menu);

        static TreeRow RowFor(AvatarDoctorView view, string name) => view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Name == name);

        [Test]
        public void AnEditOfADisabledOtherComponentIsNeitherFoundNorChanged()
        {
            // An old AvatarDoc kept switched off had renamed "Ear_Type". The tree does not apply that edit, because
            // the build does not, so a rename made from the newer component must not land in the old one.
            var older = avatar.AddMenu();
            older.menuEdits.Add(new MenuEdit { id = "old", source = new List<string> { "Ear_Type" }, name = "Old name" });
            older.enabled = false;
            var newerObject = new GameObject("Newer");
            newerObject.transform.SetParent(avatar.Root.transform);
            var newer = avatar.AddMenu(newerObject);
            AvatarPlanCache.Invalidate();
            var row = RowFor(View(newer), "Ear_Type");
            Assert.That(row.Edited, Is.False, "the row shows no edit: the old component's is not applied");

            MenuEditing.SetName(newer, row, "New name");
            AvatarPlanCache.Invalidate();

            Assert.That(older.menuEdits.Single().name, Is.EqualTo("Old name"), "the switched off component's edit is left as it was");
            Assert.That(newer.menuEdits.Single().name, Is.EqualTo("New name"), "the edit is made on the component being edited");
            var shown = AvatarPlanCache.GetFor(newer).Tree.Root.Items.Select(i => i.Name).ToList();
            Assert.That(shown, Does.Contain("New name"), "and the row shows it at once");
            Assert.That(shown, Does.Not.Contain("Ear_Type"));
        }

        [Test]
        public void CreateUpdateAndRemoveWhenEmpty()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);
            var row = RowFor(view, "Ear_Type");

            MenuEditing.SetHidden(menu, row, true);
            Assert.That(menu.menuEdits.Count, Is.EqualTo(1));
            Assert.That(menu.menuEdits[0].hide, Is.True);
            Assert.That(menu.menuEdits[0].source, Is.EqualTo(new[] { "Ear_Type" }));

            MenuEditing.SetHidden(menu, row, false);

            Assert.That(menu.menuEdits, Is.Empty, "an edit that becomes empty is removed");
        }

        [Test]
        public void ARowFromAnOlderPlanStillFindsItsEdit()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);
            var row = RowFor(view, "Ear_Type");

            MenuEditing.SetName(menu, row, "<color=#FF0000>Ear_Type</color>");
            AvatarPlanCache.Invalidate();
            AvatarPlanCache.GetFor(menu);
            MenuEditing.SetName(menu, row, "<color=#00FF00>Ear_Type</color>");

            Assert.That(menu.menuEdits.Count, Is.EqualTo(1), "a colour drag keeps writing through the row it started with; that must update the one edit, not add a second");
            Assert.That(menu.menuEdits[0].occurrence, Is.EqualTo(0));
            Assert.That(menu.menuEdits[0].name, Is.EqualTo("<color=#00FF00>Ear_Type</color>"));
            AvatarPlanCache.Invalidate();
            Assert.That(AvatarPlanCache.GetFor(menu).Tree.PendingEdits, Is.Empty, "no orphan edit, so no 'no such menu item' warning");
        }

        [Test]
        public void EditsAreFoundOnOtherComponents()
        {
            var menu = avatar.AddMenu();
            var other = new GameObject("Other"); other.transform.SetParent(avatar.Root.transform);
            var otherMenu = other.AddComponent<AvatarDoc>();
            var view = View(menu);
            var row = RowFor(view, "Ear_Type");

            MenuEditing.SetHidden(otherMenu, row, true);
            var edit = MenuEditing.EditFor(menu, row, create: false);

            Assert.That(edit, Is.Not.Null);
            Assert.That(otherMenu.menuEdits, Does.Contain(edit));
            Assert.That(menu.menuEdits, Is.Empty, "found on the other component, not duplicated onto menu");
        }

        [Test]
        public void UndoRestoresTheList()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);
            var row = RowFor(view, "Ear_Type");

            Undo.IncrementCurrentGroup();
            MenuEditing.SetHidden(menu, row, true);
            Assert.That(menu.menuEdits.Count, Is.EqualTo(1));

            Undo.PerformUndo();

            Assert.That(menu.menuEdits, Is.Empty);
        }

        [Test]
        public void MoveToRootUsesAnEmptyPath()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);
            var row = RowFor(view, "Ear_Type");

            MenuEditing.MoveTo(menu, row, "");

            var edit = menu.menuEdits.Single();
            Assert.That(edit.move, Is.True);
            Assert.That(edit.targetPath, Is.EqualTo(""));
        }

        [Test]
        public void MoveIntoItselfIsRefused()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);
            var groupRow = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Node != null && r.Node.Path == "Group");

            bool moved = MenuEditing.MoveTo(menu, groupRow, "Group");

            Assert.That(moved, Is.False);
            Assert.That(menu.menuEdits, Is.Empty);
        }

        [Test]
        public void MoveIntoOwnDescendantIsRefused()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);
            var groupRow = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Node != null && r.Node.Path == "Group");

            bool moved = MenuEditing.MoveTo(menu, groupRow, "Group/Deeper");

            Assert.That(moved, Is.False);
            Assert.That(menu.menuEdits, Is.Empty);
        }
    }
}
