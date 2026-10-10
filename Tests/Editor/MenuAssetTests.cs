using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Installing a finished expressions menu asset, and the parameters a component declares on its own.</summary>
    public class MenuAssetTests
    {
        TestAvatar avatar;
        readonly List<Object> extras = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            Platform.OverrideMobile = false;
            AvatarPlanCache.Invalidate();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            foreach (var extra in extras)
                if (extra != null && !AssetDatabase.Contains(extra)) Object.DestroyImmediate(extra);
            extras.Clear();
            Platform.OverrideMobile = null;
            AvatarPlanCache.Invalidate();
        }

        /// <summary>A menu asset of the shape a bought prefab ships: a finished VRCExpressionsMenu nobody on the avatar owns.</summary>
        VRCExpressionsMenu Asset(string name, params VRCExpressionsMenu.Control[] controls)
        {
            var asset = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            asset.name = name;
            asset.controls = controls.ToList();
            extras.Add(asset);
            return asset;
        }

        static VRCExpressionsMenu.Control SubmenuControl(string name, VRCExpressionsMenu target)
        {
            return new VRCExpressionsMenu.Control
            {
                name = name,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = target,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "" },
            };
        }

        MenuAssetInstall Install(AvatarDoc menu, VRCExpressionsMenu asset, bool asSubmenu = true, string name = "")
        {
            var install = MenuAssetInstall.Create(asset);
            install.asSubmenu = asSubmenu;
            install.name = name;
            menu.menuAssets.Add(install);
            return install;
        }

        MenuTree Model(params AvatarDoc[] menus)
        {
            return MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, menus);
        }

        static List<ErrorContext> RunFullBuild(TestAvatar a)
        {
            var context = new BuildContext(a.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            return errors;
        }

        static MenuNode SubmenuNamed(MenuNode node, string name)
        {
            return node.Items.Where(i => i.Submenu != null).Select(i => i.Submenu).FirstOrDefault(s => s.Name == name);
        }

        [Test]
        public void MenuAssetInstallsAsASubmenu()
        {
            var asset = Asset("Pack", TestAvatar.ToggleControl("Alpha", "Pack/Alpha"), TestAvatar.ToggleControl("Beta", "Pack/Beta"));
            var menu = avatar.AddMenu();
            Install(menu, asset, asSubmenu: true, name: "Extra");

            var tree = Model(menu);
            var node = SubmenuNamed(tree.Root, "Extra");

            Assert.That(node, Is.Not.Null, "one submenu holds the whole asset");
            Assert.That(node.Items.Select(i => i.Name), Is.EqualTo(new[] { "Alpha", "Beta" }));
            Assert.That(node.Items.All(i => i.Asset != null), Is.True, "every item knows the install it came from");
            Assert.That(node.Items[0].OriginalPath, Is.EqualTo("Extra/Alpha"), "asset items carry a path like the avatar's own");
        }

        [Test]
        public void MenuAssetAppendsItsControlsInPlace()
        {
            var asset = Asset("Pack", TestAvatar.ToggleControl("Alpha", "Pack/Alpha"));
            var menu = avatar.AddMenu();
            Install(menu, asset, asSubmenu: false);

            var tree = Model(menu);

            Assert.That(SubmenuNamed(tree.Root, "Pack"), Is.Null, "no submenu is made for it");
            var item = tree.Root.Items.Single(i => i.Name == "Alpha");
            Assert.That(item.Asset, Is.Not.Null);
            Assert.That(item.OriginalPath, Is.EqualTo("Alpha"));
        }

        [Test]
        public void NestedSubmenusOfAnAssetAreInstalled()
        {
            var inner = Asset("Inner", TestAvatar.ToggleControl("Deep", "Pack/Deep"));
            var asset = Asset("Pack", SubmenuControl("Inner", inner), TestAvatar.ToggleControl("Alpha", "Pack/Alpha"));
            var menu = avatar.AddMenu();
            Install(menu, asset, asSubmenu: true, name: "Extra");

            var tree = Model(menu);
            var node = SubmenuNamed(tree.Root, "Extra");
            var innerNode = SubmenuNamed(node, "Inner");

            Assert.That(innerNode, Is.Not.Null);
            Assert.That(innerNode.Items.Single().Name, Is.EqualTo("Deep"));
            Assert.That(innerNode.Items.Single().OriginalPath, Is.EqualTo("Extra/Inner/Deep"));
        }

        [Test]
        public void TheAssetIsNeverModified()
        {
            var inner = Asset("Inner", TestAvatar.ToggleControl("Deep", "Pack/Deep"));
            var asset = Asset("Pack", SubmenuControl("Inner", inner), TestAvatar.ToggleControl("Alpha", "Pack/Alpha"));
            var menu = avatar.AddMenu();
            Install(menu, asset, asSubmenu: true, name: "Extra");
            menu.parameters.Add(ParameterDeclaration.Create("Pack/Alpha", PlannedType.Bool));
            menu.parameters.Add(ParameterDeclaration.Create("Pack/Deep", PlannedType.Bool));

            var before = EditorJsonUtility.ToJson(asset);
            var innerBefore = EditorJsonUtility.ToJson(inner);

            RunFullBuild(avatar);

            Assert.That(EditorJsonUtility.ToJson(asset), Is.EqualTo(before), "the installed asset is untouched");
            Assert.That(EditorJsonUtility.ToJson(inner), Is.EqualTo(innerBefore), "so is every submenu asset under it");
            Assert.That(avatar.Descriptor.expressionsMenu, Is.Not.SameAs(avatar.ExpressionsMenu), "the build writes its own menu");
            var built = avatar.Descriptor.expressionsMenu;
            var extra = built.controls.Single(c => c.name == "Extra");
            Assert.That(extra.subMenu, Is.Not.SameAs(asset), "the asset's controls are written as clones");
            Assert.That(extra.subMenu.controls.Select(c => c.name), Is.EqualTo(new[] { "Inner", "Alpha" }));
        }

        [Test]
        public void AssetItemsCanBeRenamedAndHiddenByMenuEdits()
        {
            var asset = Asset("Pack", TestAvatar.ToggleControl("Alpha", "Pack/Alpha"), TestAvatar.ToggleControl("Beta", "Pack/Beta"));
            var menu = avatar.AddMenu();
            Install(menu, asset, asSubmenu: true, name: "Extra");
            var rename = MenuEdit.Create(new[] { "Extra", "Alpha" });
            rename.name = "Renamed";
            menu.menuEdits.Add(rename);
            var hide = MenuEdit.Create(new[] { "Extra", "Beta" });
            hide.hide = true;
            menu.menuEdits.Add(hide);

            var tree = Model(menu);
            var node = SubmenuNamed(tree.Root, "Extra");

            Assert.That(node.Items[0].Name, Is.EqualTo("Renamed"), "the edit resolved in the first pass");
            Assert.That(node.Items[0].Edit, Is.SameAs(rename));
            Assert.That(node.Items[1].Hidden, Is.True);
            Assert.That(tree.PendingEdits, Is.Empty, "no edit had to wait for a later pass");
        }

        [Test]
        public void AssetControlsCountTowardThePageSplit()
        {
            var controls = Enumerable.Range(1, 10).Select(i => TestAvatar.ToggleControl("C" + i, "Pack/C" + i)).ToArray();
            var asset = Asset("Pack", controls);
            var menu = avatar.AddMenu();
            Install(menu, asset, asSubmenu: false);

            var tree = Model(menu);
            MenuModel.SplitOverflow(tree, null);

            Assert.That(tree.Root.Items.Count(i => !i.Hidden && !i.Excluded), Is.EqualTo(VRCExpressionsMenu.MAX_CONTROLS));
            var more = SubmenuNamed(tree.Root, "More");
            Assert.That(more, Is.Not.Null, "the asset's controls overflow like anything else");
            Assert.That(more.Items.Any(i => i.Name == "C10"), Is.True);
        }

        [Test]
        public void ExcludedMenuAssetInstallsNothingAndChangesNoParameter()
        {
            var asset = Asset("Pack", TestAvatar.ToggleControl("Alpha", "Pack/Alpha"));
            var menu = avatar.AddMenu();
            var install = Install(menu, asset, asSubmenu: true, name: "Extra");
            install.platform = EntryPlatform.MobileOnly;
            menu.parameters.Add(ParameterDeclaration.Create("Pack/Alpha", PlannedType.Bool));

            Platform.OverrideMobile = false;
            var pc = Model(menu);
            var pcPlan = ParameterPlanner.Plan(pc, new Dictionary<string, PlannedType>());
            Platform.OverrideMobile = true;
            var quest = Model(menu);
            var questPlan = ParameterPlanner.Plan(quest, new Dictionary<string, PlannedType>());

            var pcItem = pc.Root.Items.SingleOrDefault(i => i.Asset == install);
            Assert.That(pcItem, Is.Not.Null, "the asset is read on both platforms, so both trees have the same shape");
            Assert.That(pcItem.Excluded, Is.True, "nothing of it is written on the platform it is left out of");
            Assert.That(pcItem.Submenu.Items.All(i => i.Excluded), Is.True, "and that goes for every control it brought");
            var questItem = quest.Root.Items.Single(i => i.Asset == install);
            Assert.That(questItem.Excluded, Is.False);
            Assert.That(SubmenuNamed(quest.Root, "Extra"), Is.Not.Null);
            Assert.That(pcPlan.Parameters.Select(p => p.Name).ToList(), Is.EqualTo(questPlan.Parameters.Select(p => p.Name).ToList()),
                "the declared parameter is planned on both platforms alike");

            Platform.OverrideMobile = false;
            RunFullBuild(avatar);
            var written = avatar.Descriptor.expressionsMenu.controls;
            Assert.That(written.Any(c => c.name == "Extra" || c.name == "Alpha"), Is.False,
                "the menu written on the platform it is left out of holds nothing of it");
        }

        [Test]
        public void AnotherComponentsItemsSurviveInsideAnExcludedAssetSubmenu()
        {
            var asset = Asset("Extras", TestAvatar.ToggleControl("Pack toggle", "Pack/Toggle"));
            var packHolder = new GameObject("Pack holder");
            packHolder.transform.SetParent(avatar.Root.transform);
            var pack = avatar.AddMenu(packHolder);
            var install = Install(pack, asset, asSubmenu: true, name: "Extras");
            install.platform = EntryPlatform.PCOnly;

            var otherHolder = new GameObject("Other holder");
            otherHolder.transform.SetParent(avatar.Root.transform);
            var other = avatar.AddMenu(otherHolder);
            other.installPath = "Extras";
            other.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            other.AddEntry(EntryKind.Toggle, "Glasses").objects.Add(new ObjectTarget { target = avatar.Glasses });

            Platform.OverrideMobile = false;
            var onPc = PlatformParity.ListOf(avatar.Root.transform);
            Platform.OverrideMobile = true;
            var onQuest = PlatformParity.ListOf(avatar.Root.transform);
            Assert.That(onQuest, Is.EqualTo(onPc), "both platforms declare the same list");

            RunFullBuild(avatar);

            var extras = avatar.Descriptor.expressionsMenu.controls.Where(c => c.name == "Extras").ToList();
            Assert.That(extras.Count, Is.EqualTo(1), "the submenu is written, since the other component's toggles live in it");
            var inside = extras[0].subMenu.controls.Select(c => c.name).ToList();
            Assert.That(inside, Is.EquivalentTo(new[] { "Hat", "Glasses" }), "the two toggles are there and the asset's own control is not");
        }

        [Test]
        public void DeclaredParametersArePlannedAfterEntriesInOrder()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            menu.parameters.Add(ParameterDeclaration.Create("Pack/One", PlannedType.Int));
            var second = ParameterDeclaration.Create("Pack/Two", PlannedType.Float);
            second.defaultValue = 0.25f;
            second.saved = false;
            second.synced = false;
            menu.parameters.Add(second);

            var plan = ParameterPlanner.Plan(Model(menu), new Dictionary<string, PlannedType>());

            Assert.That(plan.Parameters.Select(p => p.Name), Is.EqualTo(new[] { "AD/Hat", "Pack/One", "Pack/Two" }));
            var one = plan.Parameters[1];
            Assert.That(one.Type, Is.EqualTo(PlannedType.Int));
            Assert.That(one.Bits, Is.EqualTo(8));
            var two = plan.Parameters[2];
            Assert.That(two.Synced, Is.False);
            Assert.That(two.Saved, Is.False);
            Assert.That(two.Default, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(two.Bits, Is.EqualTo(0), "an unsynced declaration costs nothing");
        }

        [Test]
        public void DeclarationThatAnEntryAlreadyPlansWarns()
        {
            var menu = avatar.AddMenu();
            var entry = menu.AddEntry(EntryKind.Toggle, "Hat");
            entry.parameterName = "Pack/Shared";
            entry.objects.Add(new ObjectTarget { target = avatar.Hat });
            menu.parameters.Add(ParameterDeclaration.Create("Pack/Shared", PlannedType.Float));

            var plan = ParameterPlanner.Plan(Model(menu), new Dictionary<string, PlannedType>());

            Assert.That(plan.Messages.Any(m => m.Key == "ad.warn.parameter_declared_twice"), Is.True);
            var shared = plan.Parameters.Single(p => p.Name == "Pack/Shared");
            Assert.That(shared.Type, Is.EqualTo(PlannedType.Bool), "the entry wins");
            Assert.That(plan.Parameters.Count(p => p.Name == "Pack/Shared"), Is.EqualTo(1));
        }

        [Test]
        public void DeclarationTypeClashIsAnError()
        {
            var menu = avatar.AddMenu();
            menu.parameters.Add(ParameterDeclaration.Create("Existing", PlannedType.Float));

            var existing = ParametersWriter.ExistingParameters(avatar.ExpressionParameters, (UnityEditor.Animations.AnimatorController)null);
            var plan = ParameterPlanner.Plan(Model(menu), existing);

            Assert.That(plan.Messages.Any(m => m.Key == "ad.error.parameter_declared_type" && m.Severity == MessageSeverity.Error), Is.True);
            Assert.That(plan.HasErrors, Is.True);

            menu.parameters[0].type = PlannedType.Bool;
            var matching = ParameterPlanner.Plan(Model(menu), existing);

            Assert.That(matching.HasErrors, Is.False);
            Assert.That(matching.Parameters.Single(p => p.Name == "Existing").ReusesExisting, Is.True, "a matching type reuses the declaration the avatar already has");
            Assert.That(matching.NewSyncedBits, Is.EqualTo(0));
        }

        [Test]
        public void UndeclaredParameterOfAnAssetControlWarns()
        {
            var asset = Asset("Pack", TestAvatar.ToggleControl("Alpha", "Pack/Alpha"));
            var menu = avatar.AddMenu();
            Install(menu, asset, asSubmenu: true, name: "Extra");

            var plan = ParameterPlanner.Plan(Model(menu), new Dictionary<string, PlannedType>());
            Assert.That(plan.Messages.Any(m => m.Key == "ad.warn.parameter_undeclared"), Is.True);

            menu.parameters.Add(ParameterDeclaration.Create("Pack/Alpha", PlannedType.Bool));
            var declared = ParameterPlanner.Plan(Model(menu), new Dictionary<string, PlannedType>());

            Assert.That(declared.Messages.Any(m => m.Key == "ad.warn.parameter_undeclared"), Is.False, "a declaration answers it");
        }

        [Test]
        public void AddDeclarationActionDeclaresIt()
        {
            var asset = Asset("Pack", TestAvatar.ToggleControl("Alpha", "Pack/Alpha"));
            var menu = avatar.AddMenu();
            var install = Install(menu, asset, asSubmenu: true, name: "Extra");

            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            var row = view.Tree.RowsOfKind(RowKind.MenuAsset).Single();
            Assert.That(row.Key, Is.EqualTo(install.id));
            view.Tree.SelectKey(row.Key);

            var button = view.Detail.Q<MenuAssetPanel>().Q<Button>("ad-menu-asset-declare-Pack/Alpha");
            Assert.That(button, Is.Not.Null, "the panel offers Add declaration for the undeclared parameter");

            var made = view.Detail.AddDeclarationForParameter("Pack/Alpha");

            Assert.That(made, Is.Not.Null);
            var declaration = menu.parameters.Single();
            Assert.That(declaration.name, Is.EqualTo("Pack/Alpha"));
            Assert.That(declaration.type, Is.EqualTo(PlannedType.Bool));
        }

        [Test]
        public void ParityHoldsWithDeclarations()
        {
            using (var twin = TestAvatar.Create())
            {
                var blueprint = "avtr_menu_asset_parity";
                avatar.Root.AddComponent<VRC.Core.PipelineManager>().blueprintId = blueprint;
                twin.Root.AddComponent<VRC.Core.PipelineManager>().blueprintId = blueprint;

                foreach (var a in new[] { avatar, twin })
                {
                    var doc = a.AddMenu();
                    doc.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = a.Hat });
                    doc.parameters.Add(ParameterDeclaration.Create("Pack/One", PlannedType.Int));
                    doc.parameters.Add(ParameterDeclaration.Create("Pack/Two", PlannedType.Bool));
                }

                Platform.OverrideMobile = false;
                AvatarPlanCache.Invalidate();
                var pc = PlatformParity.ListOf(avatar.Root.transform);
                Platform.OverrideMobile = true;
                AvatarPlanCache.Invalidate();
                var quest = PlatformParity.ListOf(avatar.Root.transform);

                Assert.That(pc, Does.Contain("Pack/One (Int, synced)"));
                Assert.That(pc, Is.EqualTo(quest), "declarations are planned identically on both platforms");

                Platform.OverrideMobile = false;
                AvatarPlanCache.Invalidate();
                var plan = AvatarPlanCache.GetFor(avatar.Root.GetComponentsInChildren<AvatarDoc>(true)[0]);
                Assert.That(plan.Messages.Any(m => m.Key == "ad.warn.platform_parity"), Is.False);
            }
        }

        [Test]
        public void LockedComponentRefusesMenuAssetAndParameterWrites()
        {
            var asset = Asset("Pack", TestAvatar.ToggleControl("Alpha", "Pack/Alpha"));
            var other = Asset("Other");
            var menu = avatar.AddMenu();
            var install = Install(menu, asset, asSubmenu: true, name: "Extra");
            menu.parameters.Add(ParameterDeclaration.Create("Pack/Alpha", PlannedType.Bool));
            menu.locked = true;

            Assert.That(EntryEditing.AddMenuAsset(menu, other), Is.Null, "EntryEditing.AddMenuAsset");
            Assert.That(EntryEditing.RemoveMenuAsset(menu, install), Is.False, "EntryEditing.RemoveMenuAsset");
            Assert.That(EntryEditing.AddDeclaration(menu, "Pack/New", PlannedType.Int), Is.Null, "EntryEditing.AddDeclaration");
            Assert.That(EntryEditing.RemoveDeclaration(menu, menu.parameters[0]), Is.False, "EntryEditing.RemoveDeclaration");
            Assert.That(menu.menuAssets.Count, Is.EqualTo(1));
            Assert.That(menu.parameters.Count, Is.EqualTo(1));

            var serialized = new SerializedObject(menu);
            var fields = new DirectFields(serialized, () => serialized.ApplyModifiedProperties());
            fields.Write(serialized.FindProperty(nameof(AvatarDoc.menuAssets)).GetArrayElementAtIndex(0)
                .FindPropertyRelative(nameof(MenuAssetInstall.name)).propertyPath, p => p.stringValue = "Changed", null);
            fields.Write(serialized.FindProperty(nameof(AvatarDoc.parameters)).GetArrayElementAtIndex(0)
                .FindPropertyRelative(nameof(ParameterDeclaration.name)).propertyPath, p => p.stringValue = "Changed", null);

            Assert.That(install.name, Is.EqualTo("Extra"), "DirectFields.Write refuses a path under menuAssets");
            Assert.That(menu.parameters[0].name, Is.EqualTo("Pack/Alpha"), "DirectFields.Write refuses a path under parameters");
        }

        [Test]
        public void RootInstallPathLandsInTheRootMenu()
        {
            var asset = Asset("Pack", TestAvatar.ToggleControl("Alpha", "Pack/Alpha"));
            var menu = avatar.AddMenu();
            menu.installMode = InstallMode.AsSubmenu;
            menu.menuName = "Pack Menu";
            var install = Install(menu, asset, asSubmenu: false);
            install.installPath = "/";

            var tree = Model(menu);

            Assert.That(tree.Root.Items.Any(i => i.Name == "Alpha" && i.Asset != null), Is.True, "a slash installs into the root menu");
            Assert.That(SubmenuNamed(tree.Root, "Pack Menu").Items, Is.Empty);

            install.installPath = "";
            var withEmpty = Model(menu);

            Assert.That(withEmpty.Root.Items.Any(i => i.Name == "Alpha"), Is.False, "empty still means where the component installs");
            Assert.That(SubmenuNamed(withEmpty.Root, "Pack Menu").Items.Single().Name, Is.EqualTo("Alpha"));
        }

        [Test]
        public void DeclarationAndAssetControlKeepTheirParameter()
        {
            avatar.ExpressionParameters.parameters = avatar.ExpressionParameters.parameters.Concat(new[]
            {
                new VRCExpressionParameters.Parameter { name = "DeclaredParam", valueType = VRCExpressionParameters.ValueType.Bool, saved = true, networkSynced = true },
                new VRCExpressionParameters.Parameter { name = "AssetParam", valueType = VRCExpressionParameters.ValueType.Bool, saved = true, networkSynced = true },
            }).ToArray();
            avatar.ExpressionsMenu.controls.Add(TestAvatar.ToggleControl("DeclaredCtl", "DeclaredParam"));
            avatar.ExpressionsMenu.controls.Add(TestAvatar.ToggleControl("AssetCtl", "AssetParam"));

            var asset = Asset("Pack", TestAvatar.ToggleControl("Shine", "AssetParam"));
            var menu = avatar.AddMenu();
            Install(menu, asset, asSubmenu: true, name: "Extra");
            menu.parameters.Add(ParameterDeclaration.Create("DeclaredParam", PlannedType.Bool));
            foreach (var name in new[] { "DeclaredCtl", "AssetCtl" })
            {
                var edit = MenuEdit.Create(new[] { name });
                edit.remove = true;
                menu.menuEdits.Add(edit);
            }

            var tree = Model(menu);
            var removal = RemovalPlanner.Plan(tree, avatar.Descriptor);

            var declared = removal.Parameters.Single(p => p.Name == "DeclaredParam");
            Assert.That(declared.Freed, Is.False, "a declaration keeps its parameter");
            Assert.That(declared.KeptBecause, Is.EqualTo("ad.removal.kept.entry"));
            var fromAsset = removal.Parameters.Single(p => p.Name == "AssetParam");
            Assert.That(fromAsset.Freed, Is.False, "a control of an installed menu asset keeps its parameter");
            Assert.That(fromAsset.KeptBecause, Is.EqualTo("ad.removal.kept.control"));
            Assert.That(removal.FreedBits, Is.EqualTo(0));
        }
    }
}
