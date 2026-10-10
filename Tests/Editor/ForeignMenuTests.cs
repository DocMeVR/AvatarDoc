using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using nadena.dev.ndmf;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
#if DOCME_MA
using nadena.dev.modular_avatar.core;
#endif

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Foreign menu items (Modular Avatar and VRCFury) read into the merged menu model as read only rows.</summary>
    public class ForeignMenuTests
    {
        TestAvatar avatar;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            AvatarPlanCache.Invalidate();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            AvatarPlanCache.Invalidate();
        }

        [Test]
        public void SplitMenuPathHonoursEscapedSlashes()
        {
            Assert.That(VrcFuryReader.SplitMenuPath("Clothes/Hat"), Is.EqualTo(new List<string> { "Clothes", "Hat" }));
            Assert.That(VrcFuryReader.SplitMenuPath("A\\/B/C"), Is.EqualTo(new List<string> { "A/B", "C" }));
            Assert.That(VrcFuryReader.SplitMenuPath(""), Is.Empty);
            Assert.That(VrcFuryReader.SplitMenuPath(null), Is.Empty);
        }

        [Test]
        public void VrcFuryTogglesBecomeForeignRows()
        {
            var go = new GameObject("Fury");
            go.transform.SetParent(avatar.Root.transform);
            VrcFuryTestHelper.AddToggle(go, "Clothes/Hat");

            var foreign = ForeignMenu.Read(avatar.Root.transform, MenuModel.ReadExistingRoot(avatar.Descriptor.expressionsMenu));

            var read = foreign.Single();
            Assert.That(read.Source, Is.EqualTo(ForeignSource.VRCFury));
            Assert.That(read.Path, Is.EqualTo(new List<string> { "Clothes", "Hat" }));

            var tree = MenuModel.Build(avatar.Root.transform, avatar.Descriptor.expressionsMenu, new AvatarDoc[0], foreign);

            var clothes = tree.Root.Items.Single(i => i.Submenu != null && i.Submenu.Name == "Clothes");
            Assert.That(clothes.Foreign, Is.EqualTo(ForeignSource.VRCFury));
            var hat = clothes.Submenu.Items.Single();
            Assert.That(hat.Foreign, Is.EqualTo(ForeignSource.VRCFury));
            Assert.That(hat.Name, Is.EqualTo("Hat"));
        }

#if DOCME_MA
        [Test]
        public void AssetSourcedSubmenuShowsItsControls()
        {
            var inner = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            inner.name = "Inner";
            inner.controls = new List<VRCExpressionsMenu.Control> { new VRCExpressionsMenu.Control { name = "Loud", type = VRCExpressionsMenu.Control.ControlType.Toggle } };
            var asset = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            asset.name = "Bells_Menu";
            asset.controls = new List<VRCExpressionsMenu.Control>
            {
                new VRCExpressionsMenu.Control { name = "Bell_R", type = VRCExpressionsMenu.Control.ControlType.Toggle },
                new VRCExpressionsMenu.Control { name = "Sounds", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = inner },
                // A menu that points back at itself must not hang the reader.
                new VRCExpressionsMenu.Control { name = "Loop", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = null },
            };
            asset.controls[2].subMenu = asset;
            var go = new GameObject("Bells");
            go.transform.SetParent(avatar.Root.transform);
            go.AddComponent<ModularAvatarMenuInstaller>();
            var item = go.AddComponent<ModularAvatarMenuItem>();
            item.Control = new VRCExpressionsMenu.Control { type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = asset };
            item.MenuSource = SubmenuSource.MenuAsset;
            item.label = "Bells";
            try
            {
                var foreign = ForeignMenu.Read(avatar.Root.transform, MenuModel.ReadExistingRoot(avatar.Descriptor.expressionsMenu));

                Assert.That(foreign.Select(f => string.Join("/", f.Path)), Is.EquivalentTo(new[] { "Bells", "Bells/Bell_R", "Bells/Sounds", "Bells/Sounds/Loud", "Bells/Loop" }));
                Assert.That(foreign.Single(f => f.Path.Last() == "Bell_R").Origin, Is.Null, "a control inside a menu asset has no scene component of its own");
            }
            finally
            {
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(inner);
            }
        }

        [Test]
        public void ModularAvatarItemsBecomeForeignRows()
        {
            var itemGo = new GameObject("MAItem");
            itemGo.transform.SetParent(avatar.Root.transform);
            itemGo.AddComponent<ModularAvatarMenuInstaller>();
            var item = itemGo.AddComponent<ModularAvatarMenuItem>();
            if (item.Control == null) item.Control = new VRCExpressionsMenu.Control();
            item.Control.type = VRCExpressionsMenu.Control.ControlType.Toggle;
            item.label = "Ears";

            var subGo = new GameObject("MASub");
            subGo.transform.SetParent(avatar.Root.transform);
            subGo.AddComponent<ModularAvatarMenuInstaller>();
            var sub = subGo.AddComponent<ModularAvatarMenuItem>();
            if (sub.Control == null) sub.Control = new VRCExpressionsMenu.Control();
            sub.Control.type = VRCExpressionsMenu.Control.ControlType.SubMenu;
            sub.MenuSource = SubmenuSource.Children;
            sub.label = "Hats";
            var childGo = new GameObject("MAChild");
            childGo.transform.SetParent(subGo.transform);
            var child = childGo.AddComponent<ModularAvatarMenuItem>();
            if (child.Control == null) child.Control = new VRCExpressionsMenu.Control();
            child.Control.type = VRCExpressionsMenu.Control.ControlType.Toggle;
            child.label = "Fedora";

            var foreign = ForeignMenu.Read(avatar.Root.transform, MenuModel.ReadExistingRoot(avatar.Descriptor.expressionsMenu));

            Assert.That(foreign.Select(f => string.Join("/", f.Path)), Is.EquivalentTo(new[] { "Ears", "Hats", "Hats/Fedora" }));

            var tree = MenuModel.Build(avatar.Root.transform, avatar.Descriptor.expressionsMenu, new AvatarDoc[0], foreign);

            var earsItem = tree.Root.Items.Single(i => i.Name == "Ears");
            Assert.That(earsItem.Foreign, Is.EqualTo(ForeignSource.ModularAvatar));
            var hatsItem = tree.Root.Items.Single(i => i.Submenu != null && i.Submenu.Name == "Hats");
            Assert.That(hatsItem.Foreign, Is.EqualTo(ForeignSource.ModularAvatar));
            Assert.That(hatsItem.Submenu.Items.Single().Name, Is.EqualTo("Fedora"));
        }

        GameObject ChildOf(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            return go;
        }

        static ModularAvatarMenuItem MenuItemOn(GameObject go, string label, VRCExpressionsMenu.Control.ControlType type, bool withInstaller)
        {
            if (withInstaller) go.AddComponent<ModularAvatarMenuInstaller>();
            var item = go.AddComponent<ModularAvatarMenuItem>();
            if (item.Control == null) item.Control = new VRCExpressionsMenu.Control();
            item.Control.type = type;
            item.label = label;
            return item;
        }

        List<string> ForeignPaths()
        {
            var foreign = ForeignMenu.Read(avatar.Root.transform, MenuModel.ReadExistingRoot(avatar.Descriptor.expressionsMenu));
            return foreign.Select(f => string.Join("/", f.Path)).ToList();
        }

        [Test]
        public void AChildrenSubmenuThatReachesItsOwnParentDoesNotLoop()
        {
            var hats = ChildOf(avatar.Root.transform, "Hats");
            var submenu = MenuItemOn(ChildOf(hats.transform, "Submenu"), "Submenu", VRCExpressionsMenu.Control.ControlType.SubMenu, withInstaller: true);
            submenu.MenuSource = SubmenuSource.Children;
            submenu.menuSource_otherObjectChildren = hats;
            MenuItemOn(ChildOf(hats.transform, "Sibling"), "Sibling", VRCExpressionsMenu.Control.ControlType.Toggle, withInstaller: false);

            List<string> paths = null;
            Assert.DoesNotThrow(() => paths = ForeignPaths(), "the submenu takes its children from the object that holds it");
            Assert.That(paths, Is.EqualTo(new[] { "Submenu", "Submenu/Sibling" }), "the submenu is listed once, and so is its sibling");
            Assert.DoesNotThrow(() => AvatarPlanCache.GetFor(avatar.AddMenu()), "the inspector reads the same menu on every refresh");
        }

        [Test]
        public void TwoSubmenusThatPointAtEachOthersParentsDoNotLoop()
        {
            var first = ChildOf(avatar.Root.transform, "First");
            var second = ChildOf(avatar.Root.transform, "Second");
            var a = MenuItemOn(ChildOf(first.transform, "A"), "A", VRCExpressionsMenu.Control.ControlType.SubMenu, withInstaller: true);
            a.MenuSource = SubmenuSource.Children;
            a.menuSource_otherObjectChildren = second;
            var b = MenuItemOn(ChildOf(second.transform, "B"), "B", VRCExpressionsMenu.Control.ControlType.SubMenu, withInstaller: true);
            b.MenuSource = SubmenuSource.Children;
            b.menuSource_otherObjectChildren = first;

            List<string> paths = null;
            Assert.DoesNotThrow(() => paths = ForeignPaths());
            Assert.That(paths, Is.EquivalentTo(new[] { "A", "A/B", "B", "B/A" }), "each is read once from its own installer and once under the other, never again");
        }

        [Test]
        public void AMenuGroupWhoseChildReachesTheGroupObjectDoesNotLoop()
        {
            var holder = ChildOf(avatar.Root.transform, "Group");
            holder.AddComponent<ModularAvatarMenuInstaller>();
            holder.AddComponent<ModularAvatarMenuGroup>();
            var inside = MenuItemOn(ChildOf(holder.transform, "Inside"), "Inside", VRCExpressionsMenu.Control.ControlType.SubMenu, withInstaller: false);
            inside.MenuSource = SubmenuSource.Children;
            inside.menuSource_otherObjectChildren = holder;

            List<string> paths = null;
            Assert.DoesNotThrow(() => paths = ForeignPaths());
            Assert.That(paths, Is.EqualTo(new[] { "Inside" }));
        }
#endif

        [Test]
        public void ForeignRowsNeverReachTheWrittenMenu()
        {
            var go = new GameObject("Fury");
            go.transform.SetParent(avatar.Root.transform);
            VrcFuryTestHelper.AddToggle(go, "Clothes/Hat");
            var foreign = ForeignMenu.Read(avatar.Root.transform, MenuModel.ReadExistingRoot(avatar.Descriptor.expressionsMenu));
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Real").objects.Add(new ObjectTarget { target = avatar.Hat });
            var tree = MenuModel.Build(avatar.Root.transform, avatar.Descriptor.expressionsMenu, new[] { menu }, foreign);
            var plan = ParameterPlanner.Plan(tree, new Dictionary<string, PlannedType>());
            var context = new BuildContext(avatar.Root, null);

            MenuWriter.Write(context, avatar.Descriptor, tree, plan);

            var root = avatar.Descriptor.expressionsMenu;
            Assert.That(root.controls.Any(c => c.name == "Clothes"), Is.False);
            Assert.That(root.controls.Any(c => c.name == "Real"), Is.True);
        }

        [Test]
        public void ForeignRowsDoNotCountTowardTheSplit()
        {
            var menu = avatar.AddMenu();
            // The fixture's own menu already holds two existing items (Old, RootToggle); fill the rest of the page.
            for (int i = 0; i < VRCExpressionsMenu.MAX_CONTROLS - 2; i++)
                menu.AddEntry(EntryKind.Toggle, "T" + i).objects.Add(new ObjectTarget { target = avatar.Hat });
            var go = new GameObject("Fury");
            go.transform.SetParent(avatar.Root.transform);
            VrcFuryTestHelper.AddToggle(go, "Extra");
            var foreign = ForeignMenu.Read(avatar.Root.transform, MenuModel.ReadExistingRoot(avatar.Descriptor.expressionsMenu));

            var tree = MenuModel.Build(avatar.Root.transform, avatar.Descriptor.expressionsMenu, new[] { menu }, foreign);
            MenuModel.SplitOverflow(tree, null);

            Assert.That(tree.Root.Items.Count(i => i.Foreign == ForeignSource.None), Is.EqualTo(VRCExpressionsMenu.MAX_CONTROLS));
            Assert.That(tree.Root.Items.Count(i => i.Foreign != ForeignSource.None), Is.EqualTo(1), "the foreign row stays, uncounted");
            Assert.That(tree.Messages.Any(m => m.Key == "ad.info.menu_split"), Is.False);
        }

        [Test]
        public void ForeignRowsStayOutOfMorePages()
        {
            var menu = avatar.AddMenu();
            for (int i = 0; i < VRCExpressionsMenu.MAX_CONTROLS + 2; i++)
                menu.AddEntry(EntryKind.Toggle, "T" + i).objects.Add(new ObjectTarget { target = avatar.Hat });
            var foreign = new List<ForeignItem> { new ForeignItem { Source = ForeignSource.VRCFury, Path = new List<string> { "Extra" } } };

            var tree = MenuModel.Build(avatar.Root.transform, avatar.Descriptor.expressionsMenu, new[] { menu }, foreign);
            MenuModel.SplitOverflow(tree, null);

            var foreignItem = tree.Root.Items.Single(i => i.Foreign != ForeignSource.None);
            Assert.That(foreignItem.Name, Is.EqualTo("Extra"), "still a direct child of the root, not swept into More");
            var more = tree.Root.Items.Single(i => i.Submenu != null && i.Submenu.IsMorePage).Submenu;
            Assert.That(more.Items.Any(i => i.Foreign != ForeignSource.None), Is.False, "the other tool pages its own items; More is AvatarDoctor's own overflow only");
        }

        [Test]
        public void BuildModelHasNoForeignRows()
        {
            var go = new GameObject("Fury");
            go.transform.SetParent(avatar.Root.transform);
            VrcFuryTestHelper.AddToggle(go, "Clothes/Hat");
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Real").objects.Add(new ObjectTarget { target = avatar.Hat });

            // Mirrors AvatarDoctorBuilder, which never passes a foreign items list.
            var tree = MenuModel.Build(avatar.Root.transform, avatar.Descriptor.expressionsMenu, new[] { menu });

            Assert.That(tree.AllNodes().SelectMany(n => n.Items).Any(i => i.Foreign != ForeignSource.None), Is.False);
        }

        [Test]
        public void EditsResolveOnForeignRowsInTheInspectorModel()
        {
            var go = new GameObject("Fury");
            go.transform.SetParent(avatar.Root.transform);
            VrcFuryTestHelper.AddToggle(go, "Clothes/Hat");
            var foreign = ForeignMenu.Read(avatar.Root.transform, MenuModel.ReadExistingRoot(avatar.Descriptor.expressionsMenu));
            var menu = avatar.AddMenu();
            menu.menuEdits.Add(new MenuEdit { source = new List<string> { "Clothes", "Hat" }, name = "<b>New Hat</b>" });

            var tree = MenuModel.Build(avatar.Root.transform, avatar.Descriptor.expressionsMenu, new[] { menu }, foreign);

            var clothes = tree.Root.Items.Single(i => i.Submenu != null && i.Submenu.Name == "Clothes");
            var hat = clothes.Submenu.Items.Single();
            Assert.That(hat.RichName, Is.EqualTo("<b>New Hat</b>"));
            Assert.That(tree.PendingEdits, Is.Empty, "the edit resolved against the foreign row, nothing pending");
        }

        [Test]
        public void TreeShowsTheSourceChip()
        {
            var go = new GameObject("Fury");
            go.transform.SetParent(avatar.Root.transform);
            VrcFuryTestHelper.AddToggle(go, "Clothes/Hat");
            var menu = avatar.AddMenu();

            var plan = AvatarPlanCache.GetFor(menu);
            var (_, byId) = TreeRow.Build(plan, menu);

            var clothesRow = byId.Values.Single(r => r.Kind == RowKind.Foreign && r.Name == "Clothes");
            Assert.That(clothesRow.SourceChip, Is.EqualTo("VRCFury"));
            var hatRow = byId.Values.Single(r => r.Kind == RowKind.Foreign && r.Name == "Hat");
            Assert.That(hatRow.SourceChip, Is.EqualTo("VRCFury"));
            Assert.That(hatRow.Mine, Is.False);
        }
    }
}
