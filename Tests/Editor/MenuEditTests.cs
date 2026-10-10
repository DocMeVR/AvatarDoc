using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using nadena.dev.ndmf;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Menu edits (hide, rename, re-icon, move) applied in the merged menu model and written by MenuWriter.</summary>
    public class MenuEditTests
    {
        TestAvatar avatar;
        VRCExpressionsMenu function;
        VRCExpressionsMenu existingRoot;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            function = Menu(Toggle("Jelly_Eye", "Jelly"));
            function.name = "Function";
            existingRoot = Menu(Submenu("Function", function), Toggle("Ear_Type", "Ear"), Toggle("Breast", "Breast"));
            existingRoot.name = "Root";
            avatar.Descriptor.expressionsMenu = existingRoot;
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            Object.DestroyImmediate(function);
            Object.DestroyImmediate(existingRoot);
        }

        static VRCExpressionsMenu Menu(params VRCExpressionsMenu.Control[] controls)
        {
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.controls = controls.ToList();
            return menu;
        }

        static VRCExpressionsMenu.Control Submenu(string name, VRCExpressionsMenu menu)
        {
            return new VRCExpressionsMenu.Control { name = name, type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = menu, parameter = new VRCExpressionsMenu.Control.Parameter { name = "" } };
        }

        static VRCExpressionsMenu.Control Toggle(string name, string parameter)
        {
            return new VRCExpressionsMenu.Control { name = name, type = VRCExpressionsMenu.Control.ControlType.Toggle, parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter }, value = 1 };
        }

        MenuTree Tree(params AvatarDoc[] menus) => MenuModel.Build(avatar.Root.transform, avatar.Descriptor.expressionsMenu, menus);

        /// <summary>The model as the inspector builds it, where a move into a submenu nobody made yet creates it so the tree can show the result.</summary>
        MenuTree InspectorTree(params AvatarDoc[] menus) => MenuModel.Build(avatar.Root.transform, avatar.Descriptor.expressionsMenu, menus, null, MenuEditMode.Inspector);

        VRCExpressionsMenu Write(MenuTree tree)
        {
            var plan = ParameterPlanner.Plan(tree, new Dictionary<string, PlannedType>());
            var context = new BuildContext(avatar.Root, null);
            MenuWriter.Write(context, avatar.Descriptor, tree, plan);
            return avatar.Descriptor.expressionsMenu;
        }

        [Test]
        public void HideRemovesTheControlFromTheWrittenMenu()
        {
            var menu = avatar.AddMenu();
            menu.menuEdits.Add(new MenuEdit { source = new List<string> { "Ear_Type" }, hide = true });

            var root = Write(Tree(menu));

            Assert.That(root.controls.Any(c => c.name == "Ear_Type"), Is.False);
            Assert.That(root.controls.Select(c => c.name), Does.Contain("Breast"));
        }

        [Test]
        public void RenameAndIconReachTheWrittenControl()
        {
            var icon = new Texture2D(4, 4);
            try
            {
                var menu = avatar.AddMenu();
                menu.menuEdits.Add(new MenuEdit { source = new List<string> { "Ear_Type" }, name = "<b>Ears</b>", icon = icon });

                var root = Write(Tree(menu));

                var control = root.controls.Single(c => RichName.Strip(c.name) == "Ears");
                Assert.That(control.name, Is.EqualTo("<b>Ears</b>"));
                Assert.That(control.icon, Is.SameAs(icon));
            }
            finally
            {
                Object.DestroyImmediate(icon);
            }
        }

        [Test]
        public void MoveIntoAnAvatarDoctorSubmenu()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Submenu, "Outfits");
            menu.menuEdits.Add(new MenuEdit { source = new List<string> { "Ear_Type" }, move = true, targetPath = "Outfits" });

            var root = Write(Tree(menu));

            var outfits = root.controls.Single(c => c.name == "Outfits");
            Assert.That(outfits.subMenu.controls.Select(c => c.name), Does.Contain("Ear_Type"));
            Assert.That(root.controls.Any(c => c.name == "Ear_Type"), Is.False);
            Assert.That(existingRoot.controls.Select(c => c.name), Is.EqualTo(new[] { "Function", "Ear_Type", "Breast" }), "source asset unchanged");
        }

        [Test]
        public void MoveCreatesMissingSubmenus()
        {
            var menu = avatar.AddMenu();
            menu.menuEdits.Add(new MenuEdit { source = new List<string> { "Ear_Type" }, move = true, targetPath = "New/Deep" });

            var root = Write(InspectorTree(menu));

            var newControl = root.controls.Single(c => c.name == "New");
            var deepControl = newControl.subMenu.controls.Single(c => c.name == "Deep");
            Assert.That(deepControl.subMenu.controls.Select(c => c.name), Does.Contain("Ear_Type"));
        }

        [Test]
        public void BuildModelDefersMovesIntoMissingSubmenus()
        {
            var menu = avatar.AddMenu();
            var edit = new MenuEdit { source = new List<string> { "Ear_Type" }, move = true, targetPath = "New/Deep" };
            menu.menuEdits.Add(edit);

            var tree = Tree(menu);
            var root = Write(tree);

            Assert.That(tree.PendingEdits.Select(p => p.Edit), Does.Contain(edit));
            Assert.That(root.controls.Any(c => c.name == "New"), Is.False, "nothing is created beside what another tool may still install");
            Assert.That(root.controls.Select(c => c.name), Does.Contain("Ear_Type"));
        }

        [Test]
        public void StyledExistingSubmenuIsAMoveTarget()
        {
            var styled = Menu(Toggle("Inner", "Inner"));
            styled.name = "Styled";
            existingRoot.controls.Add(Submenu("<color=red>Clothes</color>", styled));
            try
            {
                var menu = avatar.AddMenu();
                menu.menuEdits.Add(new MenuEdit { source = new List<string> { "Ear_Type" }, move = true, targetPath = "Clothes" });

                var tree = Tree(menu);
                var node = tree.AllNodes().Single(n => n.Path == "Clothes");
                Assert.That(node.Name, Is.EqualTo("Clothes"));
                Assert.That(node.RichName, Is.EqualTo("<color=red>Clothes</color>"));

                var root = Write(tree);

                var control = root.controls.Single(c => RichName.Strip(c.name) == "Clothes");
                Assert.That(control.name, Is.EqualTo("<color=red>Clothes</color>"), "the styled name is kept");
                Assert.That(control.subMenu.controls.Select(c => c.name), Is.EquivalentTo(new[] { "Inner", "Ear_Type" }));
                Assert.That(root.controls.Any(c => c.name == "Ear_Type"), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(styled);
            }
        }

        [Test]
        public void EntrySubmenuMergesIntoAnExistingSubmenuOfTheSameName()
        {
            var menu = avatar.AddMenu();
            var sub = menu.AddEntry(EntryKind.Submenu, "Function");
            menu.AddEntry(EntryKind.Toggle, "Extra", sub.id).objects.Add(new ObjectTarget { target = avatar.Hat });

            var tree = Tree(menu);
            var root = Write(tree);

            var controls = root.controls.Where(c => RichName.Strip(c.name) == "Function").ToList();
            Assert.That(controls, Has.Count.EqualTo(1), "one Function, not two");
            Assert.That(controls[0].subMenu.controls.Select(c => c.name), Is.EqualTo(new[] { "Jelly_Eye", "Extra" }));
            Assert.That(tree.Messages.Any(m => m.Key == "ad.info.same_submenu_merged" && m.EntryIds.Contains(sub.id)), Is.True);
        }

        [Test]
        public void MovingASubmenuKeepsItsChildren()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Submenu, "Outfits");
            menu.menuEdits.Add(new MenuEdit { source = new List<string> { "Function" }, move = true, targetPath = "Outfits" });

            var root = Write(Tree(menu));

            var outfits = root.controls.Single(c => c.name == "Outfits");
            var writtenFunction = outfits.subMenu.controls.Single(c => c.name == "Function");
            Assert.That(writtenFunction.subMenu.controls.Select(c => c.name), Is.EqualTo(new[] { "Jelly_Eye" }));
            Assert.That(root.controls.Any(c => c.name == "Function"), Is.False);
        }

        [Test]
        public void CycleIsAnError()
        {
            var menu = avatar.AddMenu();
            menu.menuEdits.Add(new MenuEdit { source = new List<string> { "Function" }, move = true, targetPath = "Function/Deep" });

            var tree = Tree(menu);

            Assert.That(tree.Messages.Any(m => m.Key == "ad.error.edit_cycle" && m.Severity == MessageSeverity.Error), Is.True);
            var root = Write(tree);
            var writtenFunction = root.controls.Single(c => c.name == "Function");
            Assert.That(writtenFunction.subMenu.controls.Select(c => c.name), Is.EqualTo(new[] { "Jelly_Eye" }), "the item was left alone");
        }

        [Test]
        public void OccurrencePicksTheSecondDuplicate()
        {
            var dup = Menu(Toggle("Dup", "A"), Toggle("Dup", "B"));
            avatar.Descriptor.expressionsMenu = dup;
            try
            {
                var menu = avatar.AddMenu();
                menu.menuEdits.Add(new MenuEdit { source = new List<string> { "Dup" }, occurrence = 1, hide = true });

                var root = Write(Tree(menu));

                Assert.That(root.controls.Count(c => c.name == "Dup"), Is.EqualTo(1));
                Assert.That(root.controls.Single(c => c.name == "Dup").parameter.name, Is.EqualTo("A"), "only the second match, bound to B, was hidden");
            }
            finally
            {
                Object.DestroyImmediate(dup);
            }
        }

        [Test]
        public void UnresolvedEditsArePending()
        {
            var menu = avatar.AddMenu();
            var edit = new MenuEdit { source = new List<string> { "Nonexistent" }, hide = true };
            menu.menuEdits.Add(edit);

            var tree = Tree(menu);

            var pending = tree.PendingEdits.Single();
            Assert.That(pending.Edit, Is.SameAs(edit));
            Assert.That(pending.Component, Is.SameAs(menu));
            Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.edit_source_missing"), Is.False, "the late passes own that warning");
        }

        [Test]
        public void EditsOnOwnEntriesAreIgnoredWithAWarning()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat");
            menu.menuEdits.Add(new MenuEdit { source = new List<string> { "Hat" }, hide = true });

            var tree = Tree(menu);

            Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.edit_own_entry" && m.Severity == MessageSeverity.Warning), Is.True);
            var control = tree.Controls.Single(c => c.Entry.name == "Hat");
            Assert.That(tree.Root.Items.Single(i => i.Control == control).Hidden, Is.False, "the edit was ignored, not applied");
        }

        [Test]
        public void EditedExistingSubmenusAreRewrittenNotReused()
        {
            var menu = avatar.AddMenu();
            menu.menuEdits.Add(new MenuEdit { source = new List<string> { "Function", "Jelly_Eye" }, hide = true });

            var tree = Tree(menu);
            var functionNode = tree.Root.Items.Single(i => i.Submenu != null && i.Submenu.Name == "Function").Submenu;
            Assert.That(functionNode.ContainsGenerated(), Is.True);

            var root = Write(tree);

            var writtenFunction = root.controls.Single(c => c.name == "Function").subMenu;
            Assert.That(writtenFunction, Is.Not.SameAs(function));
            Assert.That(writtenFunction.controls.Any(c => c.name == "Jelly_Eye"), Is.False);
        }

        [Test]
        public void PositionReordersInsideTheTarget()
        {
            var menu = avatar.AddMenu();
            menu.menuEdits.Add(new MenuEdit { source = new List<string> { "Breast" }, position = 0 });

            var root = Write(Tree(menu));

            Assert.That(root.controls.Select(c => c.name), Is.EqualTo(new[] { "Breast", "Function", "Ear_Type" }));
        }
    }
}
