using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using nadena.dev.ndmf;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
#if DOCME_MA
using nadena.dev.modular_avatar.core;
#endif

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Converting VRCFury toggles and Modular Avatar menu items into AvatarDoc entries.</summary>
    public class ImportTests
    {
        TestAvatar avatar;

        [SetUp]
        public void SetUp()
        {
            GeneratedAssets.Note();
            avatar = TestAvatar.Create();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            GeneratedAssets.CleanUp();
        }

        Component AddFuryToggle(string name)
        {
            var go = new GameObject("Fury");
            go.transform.SetParent(avatar.Root.transform);
            return VrcFuryTestHelper.AddToggle(go, name);
        }

        [Test]
        public void VrcFuryObjectToggleBecomesAnObjectTarget()
        {
            var toggle = AddFuryToggle("Hat");

            var onTarget = new GameObject("OnTarget");
            onTarget.transform.SetParent(avatar.Root.transform);
            var offTarget = new GameObject("OffTarget");
            offTarget.transform.SetParent(avatar.Root.transform);
            var toggleTarget = new GameObject("ToggleTarget");
            toggleTarget.transform.SetParent(avatar.Root.transform);
            toggleTarget.SetActive(true);

            VrcFuryTestHelper.AddAction(toggle, VrcFuryReader.ObjectToggleActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "obj", onTarget);
                VrcFuryTestHelper.SetField(a, "mode", 0); // TurnOn
            });
            VrcFuryTestHelper.AddAction(toggle, VrcFuryReader.ObjectToggleActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "obj", offTarget);
                VrcFuryTestHelper.SetField(a, "mode", 1); // TurnOff
            });
            VrcFuryTestHelper.AddAction(toggle, VrcFuryReader.ObjectToggleActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "obj", toggleTarget);
                VrcFuryTestHelper.SetField(a, "mode", 2); // Toggle
            });

            var candidate = VrcFuryImport.Build(toggle, avatar.Root.transform);

            Assert.That(candidate.Supported, Is.True);
            Assert.That(candidate.Draft.objects.Single(o => o.target == onTarget).activeWhenOn, Is.True);
            Assert.That(candidate.Draft.objects.Single(o => o.target == offTarget).activeWhenOn, Is.False);
            Assert.That(candidate.Draft.objects.Single(o => o.target == toggleTarget).activeWhenOn, Is.False,
                "Toggle mode bakes in the opposite of the object's current state");
        }

        [Test]
        public void VrcFuryBlendShapeAllRenderersExpands()
        {
            var secondGo = new GameObject("Second");
            secondGo.transform.SetParent(avatar.Root.transform);
            var second = secondGo.AddComponent<SkinnedMeshRenderer>();
            second.sharedMesh = TestAvatar.QuadWithShapes("Eye_Big");

            var toggle = AddFuryToggle("Eyes");
            VrcFuryTestHelper.AddAction(toggle, VrcFuryReader.BlendShapeActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "blendShape", "Eye_Big");
                VrcFuryTestHelper.SetField(a, "blendShapeValue", 80f);
                VrcFuryTestHelper.SetField(a, "allRenderers", true);
            });

            var candidate = VrcFuryImport.Build(toggle, avatar.Root.transform);

            Assert.That(candidate.Supported, Is.True);
            Assert.That(candidate.Draft.blendShapes.Count, Is.EqualTo(2));
            Assert.That(candidate.Draft.blendShapes.All(b => b.shape == "Eye_Big" && b.weight == 80f), Is.True);
            Assert.That(candidate.Draft.blendShapes.Select(b => b.renderer), Is.EquivalentTo(new Renderer[] { avatar.Body, second }));
        }

        [Test]
        public void VrcFuryMaterialActionMaps()
        {
            var toggle = AddFuryToggle("Skin");
            VrcFuryTestHelper.AddAction(toggle, VrcFuryReader.MaterialActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "renderer", avatar.Body);
                VrcFuryTestHelper.SetField(a, "materialIndex", 1);
                VrcFuryTestHelper.SetField(a, "mat", VrcFuryTestHelper.BuildGuidWrapper("VF.Model.GuidMaterial", avatar.MaterialB));
            });

            var candidate = VrcFuryImport.Build(toggle, avatar.Root.transform);

            Assert.That(candidate.Supported, Is.True);
            var target = candidate.Draft.materials.Single();
            Assert.That(target.renderer, Is.EqualTo(avatar.Body));
            Assert.That(target.slot, Is.EqualTo(1));
            Assert.That(target.material, Is.EqualTo(avatar.MaterialB));
        }

        [Test]
        public void VrcFurySliderBecomesARadial()
        {
            var toggle = AddFuryToggle("Ears");
            VrcFuryTestHelper.ConfigureToggle(toggle, t =>
            {
                VrcFuryTestHelper.SetField(t, "slider", true);
                VrcFuryTestHelper.SetField(t, "defaultSliderValue", 0.5f);
            });
            VrcFuryTestHelper.AddAction(toggle, VrcFuryReader.BlendShapeActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "blendShape", "Eye_Big");
                VrcFuryTestHelper.SetField(a, "blendShapeValue", 100f);
                VrcFuryTestHelper.SetField(a, "renderer", avatar.Body);
                VrcFuryTestHelper.SetField(a, "allRenderers", false);
            });

            var candidate = VrcFuryImport.Build(toggle, avatar.Root.transform);

            Assert.That(candidate.Draft.kind, Is.EqualTo(EntryKind.Radial));
            Assert.That(candidate.Draft.defaultValue, Is.EqualTo(0.5f).Within(0.001f));
            var shape = candidate.Draft.blendShapes.Single();
            Assert.That(shape.minWeight, Is.EqualTo(0f));
            Assert.That(shape.maxWeight, Is.EqualTo(100f));
        }

        [Test]
        public void VrcFuryFlagsIconTagAndGlobalParameterCarryOver()
        {
            var toggle = AddFuryToggle("Face/Blush");
            var icon = new Texture2D(4, 4);
            try
            {
                VrcFuryTestHelper.ConfigureToggle(toggle, t =>
                {
                    VrcFuryTestHelper.SetField(t, "enableExclusiveTag", true);
                    VrcFuryTestHelper.SetField(t, "exclusiveTag", "face, eyes");
                    VrcFuryTestHelper.SetField(t, "enableIcon", true);
                    VrcFuryTestHelper.SetField(t, "icon", VrcFuryTestHelper.BuildGuidWrapper("VF.Model.GuidTexture2d", icon));
                    VrcFuryTestHelper.SetField(t, "useGlobalParam", true);
                    VrcFuryTestHelper.SetField(t, "globalParam", "MyGlobalParam");
                });
                VrcFuryTestHelper.AddAction(toggle, VrcFuryReader.ObjectToggleActionTypeName, a =>
                {
                    VrcFuryTestHelper.SetField(a, "obj", avatar.Hat);
                    VrcFuryTestHelper.SetField(a, "mode", 0);
                });

                var candidate = VrcFuryImport.Build(toggle, avatar.Root.transform);

                Assert.That(candidate.Path, Is.EqualTo(new List<string> { "Face" }));
                Assert.That(candidate.Draft.name, Is.EqualTo("Blush"));
                Assert.That(candidate.Draft.exclusiveGroup, Is.EqualTo("face"));
                Assert.That(candidate.Draft.icon, Is.EqualTo(icon));
                Assert.That(candidate.Draft.parameterName, Is.EqualTo("MyGlobalParam"));
                Assert.That(candidate.Notes.Any(n => n.Contains("eyes")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(icon);
            }
        }

        [Test]
        public void VrcFuryUnsupportedActionsAreNoted()
        {
            var toggle = AddFuryToggle("Extra");
            VrcFuryTestHelper.AddAction(toggle, "VF.Model.StateAction.ScaleAction", a => { });

            var candidate = VrcFuryImport.Build(toggle, avatar.Root.transform);

            Assert.That(candidate.Supported, Is.False);
            Assert.That(candidate.Reason, Is.Not.Null.And.Not.Empty);
            Assert.That(candidate.Notes.Any(n => n.Contains("Scale")), Is.True);
        }

        [Test]
        public void VrcFuryMaterialPropertyActionMaps()
        {
            var toggle = AddFuryToggle("Glow");
            VrcFuryTestHelper.AddAction(toggle, VrcFuryReader.MaterialPropertyActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "renderer2", avatar.Body.gameObject);
                VrcFuryTestHelper.SetField(a, "affectAllMeshes", false);
                VrcFuryTestHelper.SetField(a, "propertyName", "_Color");
                VrcFuryTestHelper.SetField(a, "propertyType", 1); // Color
                VrcFuryTestHelper.SetField(a, "valueColor", Color.red);
            });

            var candidate = VrcFuryImport.Build(toggle, avatar.Root.transform);

            Assert.That(candidate.Supported, Is.True);
            var target = candidate.Draft.properties.Single();
            Assert.That(target.renderer, Is.EqualTo(avatar.Body));
            Assert.That(target.property, Is.EqualTo("_Color"));
            Assert.That(target.type, Is.EqualTo(PropertyValueType.Color));
            Assert.That(target.color, Is.EqualTo(Color.red));
        }

        [Test]
        public void VrcFuryAnimationClipActionMaps()
        {
            var toggle = AddFuryToggle("Dance");
            var clip = new AnimationClip { name = "Wave" };
            try
            {
                VrcFuryTestHelper.AddAction(toggle, VrcFuryReader.AnimationClipActionTypeName, a =>
                {
                    VrcFuryTestHelper.SetField(a, "clip", VrcFuryTestHelper.BuildGuidWrapper("VF.Model.GuidAnimationClip", clip));
                });

                var candidate = VrcFuryImport.Build(toggle, avatar.Root.transform);

                Assert.That(candidate.Supported, Is.True);
                var target = candidate.Draft.animations.Single();
                Assert.That(target.clip, Is.EqualTo(clip));
                Assert.That(target.pathRoot, Is.Null);
                Assert.That(candidate.Notes.Any(n => n.Contains("avatar root")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        MenuTree Tree() => MenuModel.Build(avatar.Root.transform, avatar.Descriptor.expressionsMenu, new AvatarDoc[0]);

#if DOCME_MA
        ModularAvatarMenuItem AddMenuItem(GameObject go, string label, VRCExpressionsMenu.Control.ControlType type)
        {
            go.AddComponent<ModularAvatarMenuInstaller>();
            var item = go.AddComponent<ModularAvatarMenuItem>();
            if (item.Control == null) item.Control = new VRCExpressionsMenu.Control();
            item.Control.type = type;
            item.label = label;
            return item;
        }

        [Test]
        public void ModularAvatarToggleWithObjectToggleMaps()
        {
            var itemGo = new GameObject("MAItem");
            itemGo.transform.SetParent(avatar.Root.transform);
            var item = AddMenuItem(itemGo, "Hat", VRCExpressionsMenu.Control.ControlType.Toggle);
            item.isSaved = false;
            item.isSynced = false;
            item.isDefault = true;

            var toggle = itemGo.AddComponent<ModularAvatarObjectToggle>();
            var reference = new AvatarObjectReference();
            reference.Set(avatar.Hat);
            toggle.Objects = new List<ToggledObject> { new ToggledObject { Object = reference, Active = true } };

            var candidates = ModularAvatarImport.Scan(avatar.Root.transform, Tree());
            var candidate = candidates.Single();

            Assert.That(candidate.Supported, Is.True);
            Assert.That(candidate.Draft.name, Is.EqualTo("Hat"));
            Assert.That(candidate.Draft.saved, Is.False);
            Assert.That(candidate.Draft.synced, Is.False);
            Assert.That(candidate.Draft.defaultOn, Is.True);
            var target = candidate.Draft.objects.Single();
            Assert.That(target.target, Is.EqualTo(avatar.Hat));
            Assert.That(target.activeWhenOn, Is.True);
        }

        [Test]
        public void ModularAvatarShapeChangerSetMapsAndDeleteIsNoted()
        {
            var itemGo = new GameObject("MAItem");
            itemGo.transform.SetParent(avatar.Root.transform);
            AddMenuItem(itemGo, "Blush", VRCExpressionsMenu.Control.ControlType.Toggle);

            var changer = itemGo.AddComponent<ModularAvatarShapeChanger>();
            var setRef = new AvatarObjectReference();
            setRef.Set(avatar.Body.gameObject);
            var deleteRef = new AvatarObjectReference();
            deleteRef.Set(avatar.Body.gameObject);
            changer.Shapes = new List<ChangedShape>
            {
                new ChangedShape { Object = setRef, ShapeName = "Blush", ChangeType = ShapeChangeType.Set, Value = 60f },
                new ChangedShape { Object = deleteRef, ShapeName = "Eye_Big", ChangeType = ShapeChangeType.Delete },
            };

            var candidate = ModularAvatarImport.Scan(avatar.Root.transform, Tree()).Single();

            Assert.That(candidate.Supported, Is.True);
            var shape = candidate.Draft.blendShapes.Single();
            Assert.That(shape.renderer, Is.EqualTo(avatar.Body));
            Assert.That(shape.shape, Is.EqualTo("Blush"));
            Assert.That(shape.weight, Is.EqualTo(60f));
            Assert.That(candidate.Notes.Any(n => n.Contains("Eye_Big")), Is.True);
        }

        [Test]
        public void ModularAvatarMaterialSetterMaps()
        {
            var itemGo = new GameObject("MAItem");
            itemGo.transform.SetParent(avatar.Root.transform);
            AddMenuItem(itemGo, "Outfit", VRCExpressionsMenu.Control.ControlType.Toggle);

            var setter = itemGo.AddComponent<ModularAvatarMaterialSetter>();
            var reference = new AvatarObjectReference();
            reference.Set(avatar.Body.gameObject);
            setter.Objects = new List<MaterialSwitchObject> { new MaterialSwitchObject { Object = reference, Material = avatar.MaterialB, MaterialIndex = 1 } };

            var candidate = ModularAvatarImport.Scan(avatar.Root.transform, Tree()).Single();

            Assert.That(candidate.Supported, Is.True);
            var target = candidate.Draft.materials.Single();
            Assert.That(target.renderer, Is.EqualTo(avatar.Body));
            Assert.That(target.slot, Is.EqualTo(1));
            Assert.That(target.material, Is.EqualTo(avatar.MaterialB));
        }

        [Test]
        public void ModularAvatarMaterialSetterSkipsMissingSlots()
        {
            var itemGo = new GameObject("MAItem");
            itemGo.transform.SetParent(avatar.Root.transform);
            AddMenuItem(itemGo, "Outfit", VRCExpressionsMenu.Control.ControlType.Toggle);

            var setter = itemGo.AddComponent<ModularAvatarMaterialSetter>();
            var reference = new AvatarObjectReference();
            reference.Set(avatar.Body.gameObject);
            setter.Objects = new List<MaterialSwitchObject> { new MaterialSwitchObject { Object = reference, Material = avatar.MaterialB, MaterialIndex = 5 } };

            var candidate = ModularAvatarImport.Scan(avatar.Root.transform, Tree()).Single();

            Assert.That(candidate.Supported, Is.False);
            Assert.That(candidate.Draft.materials, Is.Empty);
            Assert.That(candidate.Notes.Any(n => n.Contains("Body")), Is.True);
        }

        [Test]
        public void RemovingAModularAvatarToggleAlsoRemovesItsEmptyInstaller()
        {
            var itemGo = new GameObject("MAItem");
            itemGo.transform.SetParent(avatar.Root.transform);
            var item = AddMenuItem(itemGo, "Hat", VRCExpressionsMenu.Control.ControlType.Toggle);
            var toggle = itemGo.AddComponent<ModularAvatarObjectToggle>();
            var reference = new AvatarObjectReference();
            reference.Set(avatar.Hat);
            toggle.Objects = new List<ToggledObject> { new ToggledObject { Object = reference, Active = true } };
            var installer = itemGo.GetComponent<ModularAvatarMenuInstaller>();
            Assert.That(installer, Is.Not.Null);
            Assert.That(installer.menuToAppend, Is.Null);

            var menu = avatar.AddMenu();
            var candidate = ModularAvatarImport.Scan(avatar.Root.transform, Tree()).Single();
            ImportApply.Apply(menu, new List<ImportCandidate> { candidate }, true);

            Assert.That(item == null, Is.True, "the imported item is removed");
            Assert.That(installer == null, Is.True, "its now-empty installer is removed with it");
        }

        [Test]
        public void InstallerWithAMenuAssetStays()
        {
            var itemGo = new GameObject("MAItem");
            itemGo.transform.SetParent(avatar.Root.transform);
            var item = AddMenuItem(itemGo, "Hat", VRCExpressionsMenu.Control.ControlType.Toggle);
            var toggle = itemGo.AddComponent<ModularAvatarObjectToggle>();
            var reference = new AvatarObjectReference();
            reference.Set(avatar.Hat);
            toggle.Objects = new List<ToggledObject> { new ToggledObject { Object = reference, Active = true } };
            var installer = itemGo.GetComponent<ModularAvatarMenuInstaller>();
            var extraMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            installer.menuToAppend = extraMenu;

            try
            {
                var menu = avatar.AddMenu();
                var candidate = ModularAvatarImport.Scan(avatar.Root.transform, Tree()).Single();
                ImportApply.Apply(menu, new List<ImportCandidate> { candidate }, true);

                Assert.That(item == null, Is.True, "the imported item is removed");
                Assert.That(installer != null, Is.True, "an installer that carries its own menu asset stays");
            }
            finally
            {
                Object.DestroyImmediate(extraMenu);
            }
        }

        [Test]
        public void ModularAvatarPathComesFromParentItems()
        {
            var subGo = new GameObject("MASub");
            subGo.transform.SetParent(avatar.Root.transform);
            var sub = AddMenuItem(subGo, "Hats", VRCExpressionsMenu.Control.ControlType.SubMenu);
            sub.MenuSource = SubmenuSource.Children;

            // The child is reached through the submenu's own Children traversal, so it gets no installer of its own
            // (mirrors ForeignMenuTests.ModularAvatarItemsBecomeForeignRows); giving it one would double-count it.
            var childGo = new GameObject("MAChild");
            childGo.transform.SetParent(subGo.transform);
            var child = childGo.AddComponent<ModularAvatarMenuItem>();
            if (child.Control == null) child.Control = new VRCExpressionsMenu.Control();
            child.Control.type = VRCExpressionsMenu.Control.ControlType.Toggle;
            child.label = "Fedora";
            var toggle = childGo.AddComponent<ModularAvatarObjectToggle>();
            var reference = new AvatarObjectReference();
            reference.Set(avatar.Hat);
            toggle.Objects = new List<ToggledObject> { new ToggledObject { Object = reference, Active = true } };

            var candidates = ModularAvatarImport.Scan(avatar.Root.transform, Tree());

            Assert.That(candidates.Count, Is.EqualTo(1), "the submenu item itself is not a candidate");
            var candidate = candidates.Single(c => c.Draft.name == "Fedora");
            Assert.That(candidate.Path, Is.EqualTo(new List<string> { "Hats" }));
        }

        /// <summary>A Menu Item toggle with one Object Toggle, on the given parameter (none when null). Made the way the component's own Reset makes one: an automatic value of 1.</summary>
        ModularAvatarMenuItem AddReactiveToggle(string label, string parameter, GameObject target)
        {
            var go = new GameObject("MA " + label);
            go.transform.SetParent(avatar.Root.transform);
            var item = AddMenuItem(go, label, VRCExpressionsMenu.Control.ControlType.Toggle);
            item.automaticValue = true;
            item.Control.value = 1f;
            if (parameter != null) item.Control.parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter };
            var toggle = go.AddComponent<ModularAvatarObjectToggle>();
            var reference = new AvatarObjectReference();
            reference.Set(target);
            toggle.Objects = new List<ToggledObject> { new ToggledObject { Object = reference, Active = true } };
            return item;
        }

        [Test]
        public void AModularAvatarRadioSetConvertsAsOneExclusiveGroup()
        {
            var swimsuit = new GameObject("Swimsuit");
            swimsuit.transform.SetParent(avatar.Root.transform);
            var dress = AddReactiveToggle("Dress", "Outfit", avatar.Hat);
            dress.isDefault = true;
            AddReactiveToggle("Hoodie", "Outfit", avatar.Glasses);
            AddReactiveToggle("Swimsuit", "Outfit", swimsuit);

            var candidates = ModularAvatarImport.Scan(avatar.Root.transform, Tree());

            Assert.That(candidates.Count, Is.EqualTo(3));
            Assert.That(candidates.All(c => c.Supported), Is.True, "three toggles the importer reads on their own are one set that converts");
            Assert.That(candidates.All(c => c.Draft.exclusiveGroup == "Outfit"), Is.True, "the group is named after the shared parameter");
            Assert.That(candidates.All(c => c.Draft.parameterName == ""), Is.True, "the group owns the parameter, so no member carries the old name");
            Assert.That(candidates.Single(c => c.Draft.defaultOn).Draft.name, Is.EqualTo("Dress"), "the default comes from isDefault");

            var menu = avatar.AddMenu();
            ImportApply.Apply(menu, candidates, true);
            AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
            var plan = AvatarPlanCache.GetFor(menu);

            var bindings = plan.Tree.Controls.Select(c => plan.Plan.Bindings[c]).ToList();
            Assert.That(bindings.Count, Is.EqualTo(3));
            Assert.That(bindings.Select(b => b.Parameter).Distinct().Count(), Is.EqualTo(1), "one parameter carries the whole set");
            Assert.That(bindings[0].Parameter.Type, Is.EqualTo(PlannedType.Int));
            Assert.That(bindings.Select(b => b.Value).Distinct().Count(), Is.EqualTo(3), "each outfit has a value of its own, so turning one on turns the others off");
            Assert.That(bindings.Single(b => b.OnByDefault).Control.Entry.PlainName, Is.EqualTo("Dress"));
        }

        [Test]
        public void ConvertingOneMemberOfARadioSetConvertsTheWholeSet()
        {
            AddReactiveToggle("Dress", "Outfit", avatar.Hat);
            AddReactiveToggle("Hoodie", "Outfit", avatar.Glasses);
            var candidates = ModularAvatarImport.Scan(avatar.Root.transform, Tree());
            var menu = avatar.AddMenu();

            var created = ImportApply.Apply(menu, new List<ImportCandidate> { candidates[0] }, true);

            Assert.That(created.Select(e => e.PlainName), Is.EquivalentTo(new[] { "Dress", "Hoodie" }), "a part of the set would not be exclusive with the rest");
            Assert.That(avatar.Root.GetComponentsInChildren<ModularAvatarMenuItem>(true), Is.Empty);
        }

        [Test]
        public void AParameterSharedWithAnItemThatCannotConvertRefusesTheWholeSet()
        {
            AddReactiveToggle("Dress", "Outfit", avatar.Hat);
            AddReactiveToggle("Hoodie", "Outfit", avatar.Glasses);
            var resetObject = new GameObject("MA Reset");
            resetObject.transform.SetParent(avatar.Root.transform);
            var reset = AddMenuItem(resetObject, "Reset", VRCExpressionsMenu.Control.ControlType.Toggle);
            reset.Control.parameter = new VRCExpressionsMenu.Control.Parameter { name = "Outfit" };
            reset.automaticValue = true;
            reset.Control.value = 1f;

            var candidates = ModularAvatarImport.Scan(avatar.Root.transform, Tree());

            Assert.That(candidates.Count, Is.EqualTo(3));
            Assert.That(candidates.Any(c => c.Supported), Is.False, "one member with no action to carry over leaves the others without a group to join");
            foreach (var blocked in candidates.Where(c => c.Draft.name != "Reset"))
                Assert.That(blocked.Reason, Is.EqualTo(L.Text("ad.import.reason.shared_parameter_controls")), blocked.Draft.name);
            Assert.That(candidates.Single(c => c.Draft.name == "Reset").Reason, Is.EqualTo(L.Text("ad.import.reason.no_supported_action")),
                "the item that blocks the set says why itself, so the creator can find it");
        }

        [Test]
        public void AButtonOnTheSameParameterRefusesTheWholeSet()
        {
            AddReactiveToggle("Dress", "Outfit", avatar.Hat);
            AddReactiveToggle("Hoodie", "Outfit", avatar.Glasses);
            var buttonObject = new GameObject("MA Reset");
            buttonObject.transform.SetParent(avatar.Root.transform);
            var button = AddMenuItem(buttonObject, "Reset", VRCExpressionsMenu.Control.ControlType.Button);
            button.Control.parameter = new VRCExpressionsMenu.Control.Parameter { name = "Outfit" };
            button.automaticValue = true;
            button.Control.value = 1f;

            var candidates = ModularAvatarImport.Scan(avatar.Root.transform, Tree());

            Assert.That(candidates.Any(c => c.Supported), Is.False);
            foreach (var blocked in candidates.Where(c => c.Draft.name != "Reset"))
                Assert.That(blocked.Reason, Is.EqualTo(L.Text("ad.import.reason.shared_parameter_controls")), blocked.Draft.name);
            Assert.That(candidates.Single(c => c.Draft.name == "Reset").Reason, Is.EqualTo(L.Text("ad.import.reason.not_a_toggle")),
                "the Button keeps its own reason: it is the item that blocks the set");
        }

        [Test]
        public void TwoItemsWithTheSameExplicitValueRefuseTheSet()
        {
            var jacket = AddReactiveToggle("Jacket", "Jacket", avatar.Hat);
            jacket.automaticValue = false;
            jacket.Control.value = 1f;
            var coat = AddReactiveToggle("Coat", "Jacket", avatar.Glasses);
            coat.automaticValue = false;
            coat.Control.value = 1f;

            var candidates = ModularAvatarImport.Scan(avatar.Root.transform, Tree());

            Assert.That(candidates.Count, Is.EqualTo(2));
            Assert.That(candidates.Any(c => c.Supported), Is.False, "two items on one value are one switch shown in two places, which a group cannot give back");
            Assert.That(candidates.All(c => c.Reason == L.Text("ad.import.reason.shared_parameter_controls")), Is.True);
        }

        [Test]
        public void AnExplicitZeroRefusesTheSet()
        {
            var jacket = AddReactiveToggle("Jacket", "Jacket", avatar.Hat);
            jacket.automaticValue = false;
            jacket.Control.value = 1f;
            var none = AddReactiveToggle("None", "Jacket", avatar.Glasses);
            none.automaticValue = false;
            none.Control.value = 0f;

            var candidates = ModularAvatarImport.Scan(avatar.Root.transform, Tree());

            Assert.That(candidates.Any(c => c.Supported), Is.False, "an item on 0 comes on whenever another one is switched off, which a group does not do");
            Assert.That(candidates.All(c => c.Reason == L.Text("ad.import.reason.shared_parameter_controls")), Is.True);
        }

        [Test]
        public void ExplicitValuesThatDifferAndAreNotZeroStillConvertAsAGroup()
        {
            var values = new[] { 1f, 2f, 3f };
            var targets = new[] { avatar.Hat, avatar.Glasses, avatar.Body.gameObject };
            for (int i = 0; i < values.Length; i++)
            {
                var item = AddReactiveToggle("Piece " + i, "Jacket", targets[i]);
                item.automaticValue = false;
                item.Control.value = values[i];
            }

            var candidates = ModularAvatarImport.Scan(avatar.Root.transform, Tree());

            Assert.That(candidates.Count, Is.EqualTo(3));
            Assert.That(candidates.All(c => c.Supported && c.Draft.exclusiveGroup == "Jacket"), Is.True);
        }

        [Test]
        public void AToggleOnItsOwnParameterKeepsThatParameterAndItsExplicitValue()
        {
            var item = AddReactiveToggle("Slot", "Slot", avatar.Hat);
            item.automaticValue = false;
            item.Control.value = 3f;

            var candidate = ModularAvatarImport.Scan(avatar.Root.transform, Tree()).Single();

            Assert.That(candidate.Supported, Is.True);
            Assert.That(candidate.Draft.parameterName, Is.EqualTo("Slot"));
            Assert.That(candidate.Draft.parameterValue, Is.EqualTo(3f), "the value the menu writes, or the entry would write 1");
            Assert.That(candidate.Draft.exclusiveGroup, Is.Empty);
        }

        [Test]
        public void AnAutomaticValueIsLeftAtOne()
        {
            var item = AddReactiveToggle("Slot", "Slot", avatar.Hat);
            item.automaticValue = true;
            item.Control.value = 7f;

            var candidate = ModularAvatarImport.Scan(avatar.Root.transform, Tree()).Single();

            Assert.That(candidate.Draft.parameterValue, Is.EqualTo(1f), "Modular Avatar picks the value itself, and a lone toggle gets 1");
        }

        // Converting a Modular Avatar menu whose effects live in a Merge Animator. The fixtures reproduce the
        // shapes of the user's Shading_FX: bool layers of two states with one condition each, and float layers of
        // one state holding a 1D blend tree of two constant clips at 0 and 1, all animating material properties.
        sealed class MergeFixture
        {
            public GameObject Root;
            public ModularAvatarMenuInstaller Installer;
            public ModularAvatarMenuItem Submenu;
            public ModularAvatarParameters Parameters;
            public ModularAvatarMergeAnimator Merge;
            public AnimatorController Controller;
        }

        static AnimationClip PropertyClip(string name, string path, string property, float value)
        {
            var clip = new AnimationClip { name = name };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "material." + property), AnimationCurve.Constant(0f, 1f / 60f, value));
            return clip;
        }

        static AnimatorStateMachine AddLayerTo(AnimatorController controller, string layerName)
        {
            controller.AddLayer(layerName);
            var layers = controller.layers;
            layers[layers.Length - 1].defaultWeight = 1f;
            controller.layers = layers;
            return controller.layers[controller.layers.Length - 1].stateMachine;
        }

        /// <summary>restsOn reproduces the shape of the real Shading LTCGI layer: the same two states and conditions, but the layer starts in its On state because the parameter defaults to true.</summary>
        static AnimatorState AddBoolLayer(AnimatorController controller, string layerName, string parameter, AnimationClip off, AnimationClip on, bool restsOn = false)
        {
            controller.AddParameter(parameter, AnimatorControllerParameterType.Bool);
            var sm = AddLayerTo(controller, layerName);
            var offState = sm.AddState("Off");
            var onState = sm.AddState("On");
            sm.defaultState = restsOn ? onState : offState;
            offState.motion = off;
            onState.motion = on;
            var toOn = offState.AddTransition(onState);
            toOn.hasExitTime = false;
            toOn.duration = 0f;
            toOn.AddCondition(AnimatorConditionMode.If, 0f, parameter);
            var toOff = onState.AddTransition(offState);
            toOff.hasExitTime = false;
            toOff.duration = 0f;
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0f, parameter);
            return onState;
        }

        /// <summary>A float layer of one state with a 1D blend tree, thresholds evenly spread from 0 to 1. blendParameterY is set the way Unity leaves it on a tree made in the Animator window, which a 1D tree never reads.</summary>
        static BlendTree AddBlendLayer(AnimatorController controller, string layerName, string parameter, params AnimationClip[] clips)
        {
            controller.AddParameter(parameter, AnimatorControllerParameterType.Float);
            var sm = AddLayerTo(controller, layerName);
            var state = sm.AddState(layerName);
            sm.defaultState = state;
            var tree = new BlendTree
            {
                name = layerName,
                blendType = BlendTreeType.Simple1D,
                blendParameter = parameter,
                blendParameterY = controller.parameters[0].name,
                useAutomaticThresholds = false,
            };
            foreach (var clip in clips) tree.AddChild(clip);
            var children = tree.children;
            for (int i = 0; i < children.Length; i++) children[i].threshold = children.Length == 1 ? 0f : i / (float)(children.Length - 1);
            tree.children = children;
            tree.minThreshold = 0f;
            tree.maxThreshold = 1f;
            state.motion = tree;
            return tree;
        }

        MergeFixture AddMergeMenu(AnimatorController controller, string label = "Shading", MergeAnimatorPathMode pathMode = MergeAnimatorPathMode.Absolute)
        {
            var go = new GameObject(label + " Menu");
            go.transform.SetParent(avatar.Root.transform);
            var fixture = new MergeFixture { Root = go, Controller = controller };
            fixture.Installer = go.AddComponent<ModularAvatarMenuInstaller>();
            fixture.Submenu = go.AddComponent<ModularAvatarMenuItem>();
            fixture.Submenu.Control = new VRCExpressionsMenu.Control { name = label, type = VRCExpressionsMenu.Control.ControlType.SubMenu };
            fixture.Submenu.MenuSource = SubmenuSource.Children;
            fixture.Submenu.label = label;
            fixture.Parameters = go.AddComponent<ModularAvatarParameters>();
            fixture.Merge = go.AddComponent<ModularAvatarMergeAnimator>();
            fixture.Merge.animator = controller;
            fixture.Merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            fixture.Merge.pathMode = pathMode;
            fixture.Merge.matchAvatarWriteDefaults = true;
            return fixture;
        }

        static ModularAvatarMenuItem AddMergeToggle(MergeFixture fixture, string name, string parameter, float value = 1f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(fixture.Root.transform);
            var item = go.AddComponent<ModularAvatarMenuItem>();
            item.Control = new VRCExpressionsMenu.Control
            {
                name = name,
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter },
                value = value,
            };
            item.label = name;
            return item;
        }

        static ModularAvatarMenuItem AddMergeRadial(MergeFixture fixture, string name, string parameter, float value = 1f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(fixture.Root.transform);
            var item = go.AddComponent<ModularAvatarMenuItem>();
            item.Control = new VRCExpressionsMenu.Control
            {
                name = name,
                type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "" },
                subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = parameter } },
                value = value,
            };
            item.label = name;
            return item;
        }

        static void Declare(MergeFixture fixture, string name, ParameterSyncType syncType, float defaultValue, bool saved = true, bool localOnly = false)
        {
            fixture.Parameters.parameters.Add(new ParameterConfig
            {
                nameOrPrefix = name,
                syncType = syncType,
                defaultValue = defaultValue,
                saved = saved,
                localOnly = localOnly,
                hasExplicitDefaultValue = true,
            });
        }

        [Test]
        public void TwoStateLayerUnderAMergeAnimatorBecomesAToggle()
        {
            var controller = new AnimatorController { name = "Shading_FX" };
            AddBoolLayer(controller, "Shading SSAO", "Shading/SSAO",
                PropertyClip("SSAO_Off", "Body", "_SSAOAnimationToggle", 0f),
                PropertyClip("SSAO_On", "Body", "_SSAOAnimationToggle", 1f));
            var fixture = AddMergeMenu(controller);
            AddMergeToggle(fixture, "SSAO", "Shading/SSAO");

            var candidates = MergeAnimatorImport.Scan(avatar.Root.transform, Tree());
            var candidate = candidates.Single(c => c.Draft != null);

            Assert.That(candidate.Supported, Is.True, candidate.Reason);
            Assert.That(candidate.Draft.kind, Is.EqualTo(EntryKind.Toggle));
            Assert.That(candidate.Draft.name, Is.EqualTo("SSAO"));
            Assert.That(candidate.Path, Is.EqualTo(new List<string> { "Shading" }));
            Assert.That(candidate.Draft.parameterName, Is.Empty, "a converted entry gets a parameter of its own");
            var property = candidate.Draft.properties.Single();
            Assert.That(property.renderer, Is.EqualTo(avatar.Body));
            Assert.That(property.property, Is.EqualTo("_SSAOAnimationToggle"));
            Assert.That(property.value, Is.EqualTo(1f));
        }

        [Test]
        public void BlendTreeOfTwoConstantClipsBecomesARadial()
        {
            var controller = new AnimatorController { name = "Shading_FX" };
            AddBlendLayer(controller, "Shading Min Brightness", "Shading/MinBrightness",
                PropertyClip("MinBrightness_0", "Body", "_LightingMinLightBrightness", 0f),
                PropertyClip("MinBrightness_0.3", "Body", "_LightingMinLightBrightness", 0.3f));
            var fixture = AddMergeMenu(controller);
            AddMergeRadial(fixture, "Min Brightness", "Shading/MinBrightness");

            var candidates = MergeAnimatorImport.Scan(avatar.Root.transform, Tree());
            var candidate = candidates.Single(c => c.Draft != null);

            Assert.That(candidate.Supported, Is.True, candidate.Reason);
            Assert.That(candidate.Draft.kind, Is.EqualTo(EntryKind.Radial));
            Assert.That(candidate.Draft.parameterName, Is.Empty);
            var property = candidate.Draft.properties.Single();
            Assert.That(property.property, Is.EqualTo("_LightingMinLightBrightness"));
            Assert.That(property.minValue, Is.EqualTo(0f).Within(0.0001f), "the clip at threshold 0 is the lower end");
            Assert.That(property.value, Is.EqualTo(0.3f).Within(0.0001f), "the clip at threshold 1 is the upper end");
        }

        [Test]
        public void ThreeChildBlendTreeIsNotConvertible()
        {
            var controller = new AnimatorController { name = "Shading_FX" };
            AddBlendLayer(controller, "Shading Min Brightness", "Shading/MinBrightness",
                PropertyClip("Low", "Body", "_LightingMinLightBrightness", 0f),
                PropertyClip("Mid", "Body", "_LightingMinLightBrightness", 0.15f),
                PropertyClip("High", "Body", "_LightingMinLightBrightness", 0.3f));
            var fixture = AddMergeMenu(controller);
            AddMergeRadial(fixture, "Min Brightness", "Shading/MinBrightness");

            var candidates = MergeAnimatorImport.Scan(avatar.Root.transform, Tree());
            var item = candidates.Single(c => c.Draft != null);
            var merge = candidates.Single(c => c.MergeDraft != null);

            Assert.That(item.Supported, Is.True, "it is still importable as a parameter control");
            Assert.That(item.Draft.IsParameterControl, Is.True);
            Assert.That(item.Draft.parameterName, Is.EqualTo("Shading/MinBrightness"));
            Assert.That(item.Draft.properties, Is.Empty);
            Assert.That(merge.MergeDraft.skipLayers, Is.Empty, "the layer stays in the merge");
        }

        [Test]
        public void RelativePathModeResolvesFromTheMergeAnimatorObject()
        {
            var controller = new AnimatorController { name = "Shading_FX" };
            var off = new AnimationClip { name = "CapeOff" };
            var on = new AnimationClip { name = "CapeOn" };
            SetIsActiveCurve(off, "Cape", 0f);
            SetIsActiveCurve(on, "Cape", 1f);
            AddBoolLayer(controller, "Cape", "Outfit/Cape", off, on);
            var fixture = AddMergeMenu(controller, "Outfit", MergeAnimatorPathMode.Relative);
            var cape = new GameObject("Cape");
            cape.transform.SetParent(fixture.Root.transform);
            cape.SetActive(false);
            AddMergeToggle(fixture, "Cape", "Outfit/Cape");

            var candidates = MergeAnimatorImport.Scan(avatar.Root.transform, Tree());
            var candidate = candidates.Single(c => c.Draft != null);

            Assert.That(candidate.Supported, Is.True, candidate.Reason);
            var target = candidate.Draft.objects.Single();
            Assert.That(target.target, Is.EqualTo(cape), "the clip path starts at the Merge Animator's object");
            Assert.That(target.activeWhenOn, Is.True);
        }

        [Test]
        public void AComplexLayerBecomesAParameterControlAndStaysMerged()
        {
            var controller = new AnimatorController { name = "Shading_FX" };
            AddBoolLayer(controller, "Shading SSAO", "Shading/SSAO",
                PropertyClip("SSAO_Off", "Body", "_SSAOAnimationToggle", 0f),
                PropertyClip("SSAO_On", "Body", "_SSAOAnimationToggle", 1f));
            var oily = AddBoolLayer(controller, "Shading Oily Skin", "Shading/OilySkin",
                PropertyClip("Oily_Off", "Body", "_MochieSpecularStrength", 0f),
                PropertyClip("Oily_On", "Body", "_MochieSpecularStrength", 1f));
            oily.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            var fixture = AddMergeMenu(controller);
            AddMergeToggle(fixture, "SSAO", "Shading/SSAO");
            AddMergeToggle(fixture, "Oily Skin", "Shading/OilySkin");

            var candidates = MergeAnimatorImport.Scan(avatar.Root.transform, Tree());
            var ssao = candidates.Single(c => c.Draft != null && c.Draft.name == "SSAO");
            var oilySkin = candidates.Single(c => c.Draft != null && c.Draft.name == "Oily Skin");
            var merge = candidates.Single(c => c.MergeDraft != null);

            Assert.That(ssao.Draft.parameterName, Is.Empty);
            Assert.That(ssao.Draft.properties.Count, Is.EqualTo(1));
            Assert.That(oilySkin.Supported, Is.True, oilySkin.Reason);
            Assert.That(oilySkin.Draft.IsParameterControl, Is.True);
            Assert.That(oilySkin.Draft.parameterName, Is.EqualTo("Shading/OilySkin"));
            Assert.That(merge.MergeDraft.controller, Is.EqualTo(controller));
            Assert.That(merge.MergeDraft.skipLayers, Is.EqualTo(new List<string> { "Shading SSAO" }), "only the converted layer is skipped");
            Assert.That(merge.MergeDraft.pathRoot, Is.Null, "absolute path mode starts at the avatar root");
            Assert.That(merge.Group, Is.EqualTo(ssao.Group).And.Not.Empty);
        }

        [Test]
        public void AllLayersConvertedLeavesNoMerge()
        {
            var controller = new AnimatorController { name = "Shading_FX" };
            AddBoolLayer(controller, "Shading SSAO", "Shading/SSAO",
                PropertyClip("SSAO_Off", "Body", "_SSAOAnimationToggle", 0f),
                PropertyClip("SSAO_On", "Body", "_SSAOAnimationToggle", 1f));
            AddBoolLayer(controller, "Shading LTCGI", "Shading/LTCGI",
                PropertyClip("LTCGI_Off", "Body", "_LTCGI_AnimToggle", 0f),
                PropertyClip("LTCGI_On", "Body", "_LTCGI_AnimToggle", 1f));
            var fixture = AddMergeMenu(controller);
            AddMergeToggle(fixture, "SSAO", "Shading/SSAO");
            AddMergeToggle(fixture, "LTCGI", "Shading/LTCGI");

            var candidates = MergeAnimatorImport.Scan(avatar.Root.transform, Tree());

            Assert.That(candidates.Count, Is.EqualTo(2));
            Assert.That(candidates.Any(c => c.MergeDraft != null), Is.False, "nothing is left to merge");
            Assert.That(candidates.Any(c => c.OriginComponents.Contains(fixture.Merge)), Is.True, "the Merge Animator still goes with the set");
        }

        [Test]
        public void DefaultsSavedAndSyncedComeFromMaParameters()
        {
            var controller = new AnimatorController { name = "Shading_FX" };
            AddBoolLayer(controller, "Shading LTCGI", "Shading/LTCGI",
                PropertyClip("LTCGI_Off", "Body", "_LTCGI_AnimToggle", 0f),
                PropertyClip("LTCGI_On", "Body", "_LTCGI_AnimToggle", 1f));
            AddBlendLayer(controller, "Shading Hair Shine", "Shading/HairShine",
                PropertyClip("HairShine_0", "Body", "_Aniso0Strength", 0f),
                PropertyClip("HairShine_2x", "Body", "_Aniso0Strength", 2f));
            var fixture = AddMergeMenu(controller);
            AddMergeToggle(fixture, "LTCGI", "Shading/LTCGI");
            AddMergeRadial(fixture, "Hair Shine", "Shading/HairShine");
            Declare(fixture, "Shading/LTCGI", ParameterSyncType.Bool, 1f);
            Declare(fixture, "Shading/HairShine", ParameterSyncType.Float, 0.5f, saved: false, localOnly: true);

            var candidates = MergeAnimatorImport.Scan(avatar.Root.transform, Tree());
            var ltcgi = candidates.Single(c => c.Draft != null && c.Draft.name == "LTCGI").Draft;
            var hairShine = candidates.Single(c => c.Draft != null && c.Draft.name == "Hair Shine").Draft;

            Assert.That(ltcgi.defaultOn, Is.True, "a default above 0.5 is a toggle that starts on");
            Assert.That(ltcgi.saved, Is.True);
            Assert.That(ltcgi.synced, Is.True);
            Assert.That(hairShine.defaultValue, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(hairShine.saved, Is.False);
            Assert.That(hairShine.synced, Is.False, "a local only declaration is not synced");
        }

        [Test]
        public void RenamedMaParametersMakeTheGroupUnsupported()
        {
            var controller = new AnimatorController { name = "Shading_FX" };
            AddBoolLayer(controller, "Shading SSAO", "Shading/SSAO",
                PropertyClip("SSAO_Off", "Body", "_SSAOAnimationToggle", 0f),
                PropertyClip("SSAO_On", "Body", "_SSAOAnimationToggle", 1f));
            var fixture = AddMergeMenu(controller);
            AddMergeToggle(fixture, "SSAO", "Shading/SSAO");
            fixture.Parameters.parameters.Add(new ParameterConfig
            {
                nameOrPrefix = "Shading/SSAO",
                remapTo = "Renamed/SSAO",
                syncType = ParameterSyncType.Bool,
                saved = true,
            });

            var candidates = MergeAnimatorImport.Scan(avatar.Root.transform, Tree());

            Assert.That(candidates.Any(c => c.MergeDraft != null), Is.False);
            var candidate = candidates.Single(c => c.Draft != null);
            Assert.That(candidate.Supported, Is.False);
            Assert.That(candidate.Reason, Does.Contain("Shading/SSAO"));
        }

        [Test]
        public void GroupIsAppliedTogetherAndOriginalsAreRemoved()
        {
            var menu = avatar.AddMenu();
            var controller = new AnimatorController { name = "Shading_FX" };
            AddBoolLayer(controller, "Shading SSAO", "Shading/SSAO",
                PropertyClip("SSAO_Off", "Body", "_SSAOAnimationToggle", 0f),
                PropertyClip("SSAO_On", "Body", "_SSAOAnimationToggle", 1f));
            var oily = AddBoolLayer(controller, "Shading Oily Skin", "Shading/OilySkin",
                PropertyClip("Oily_Off", "Body", "_MochieSpecularStrength", 0f),
                PropertyClip("Oily_On", "Body", "_MochieSpecularStrength", 1f));
            oily.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            var fixture = AddMergeMenu(controller);
            var ssaoItem = AddMergeToggle(fixture, "SSAO", "Shading/SSAO");
            var oilyItem = AddMergeToggle(fixture, "Oily Skin", "Shading/OilySkin");
            Declare(fixture, "Shading/OilySkin", ParameterSyncType.Bool, 0f);
            AvatarPlanCache.Invalidate();

            var candidates = ImportScanner.Scan(avatar.Root.transform, AvatarPlanCache.GetFor(menu).Tree);
            var one = candidates.Single(c => c.Draft != null && c.Draft.name == "SSAO");
            var created = ImportApply.Apply(menu, new List<ImportCandidate> { one }, true);

            Assert.That(created.Count, Is.EqualTo(2), "choosing one item of the set converts the whole set");
            Assert.That(menu.animators.Count, Is.EqualTo(1));
            Assert.That(menu.animators[0].controller, Is.EqualTo(controller));
            Assert.That(menu.animators[0].skipLayers, Is.EqualTo(new List<string> { "Shading SSAO" }));
            Assert.That(ssaoItem == null && oilyItem == null, Is.True, "the menu items are gone");
            Assert.That(fixture.Submenu == null && fixture.Installer == null, Is.True, "the submenu item and its installer are gone");
            Assert.That(fixture.Merge == null && fixture.Parameters == null, Is.True, "the Merge Animator and the parameters are gone");
            Assert.That(controller.layers.Length, Is.EqualTo(2), "the controller asset is untouched");
            Assert.That(controller.parameters.Length, Is.EqualTo(2));
        }

        /// <summary>A skinned renderer at a path under the avatar, sharing the avatar's material so its shader properties can be read back.</summary>
        SkinnedMeshRenderer AddSkin(string path)
        {
            var parent = avatar.Root.transform;
            foreach (var segment in path.Split('/'))
            {
                var child = parent.Find(segment);
                if (child == null)
                {
                    var made = new GameObject(segment);
                    made.transform.SetParent(parent);
                    child = made.transform;
                }
                parent = child;
            }
            var renderer = parent.GetComponent<SkinnedMeshRenderer>();
            if (renderer == null) renderer = parent.gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMaterials = new[] { avatar.MaterialA };
            return renderer;
        }

        /// <summary>One clip writing the same material properties on several renderers, the way the real Shading_FX clips do.</summary>
        static AnimationClip PropertiesClip(string name, string[] paths, params (string property, float value)[] values)
        {
            var clip = new AnimationClip { name = name };
            foreach (var path in paths)
                foreach (var pair in values)
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "material." + pair.property), AnimationCurve.Constant(0f, 1f / 60f, pair.value));
            return clip;
        }

        static void SetAnimatorDefault(AnimatorController controller, string name, float value)
        {
            var parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
                if (parameters[i].name == name) { parameters[i].defaultFloat = value; parameters[i].defaultBool = value > 0.5f; }
            controller.parameters = parameters;
        }

        static float FloatOf(Motion motion, string path, string property)
        {
            var curve = AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "material." + property));
            Assert.That(curve, Is.Not.Null, path + " " + property);
            return curve.Evaluate(0f);
        }

        [Test]
        public void InvertedTwoStateLayerBecomesAToggleThatStartsOn()
        {
            var controller = new AnimatorController { name = "Shading_FX" };
            AddBoolLayer(controller, "Shading LTCGI", "Shading/LTCGI",
                PropertyClip("LTCGI_Off", "Body", "_SpecularHighlights", 0f),
                PropertyClip("LTCGI_On", "Body", "_SpecularHighlights", 1f), restsOn: true);
            var fixture = AddMergeMenu(controller);
            AddMergeToggle(fixture, "LTCGI", "Shading/LTCGI");
            Declare(fixture, "Shading/LTCGI", ParameterSyncType.Bool, 1f);

            var candidate = MergeAnimatorImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft != null);

            Assert.That(candidate.Supported, Is.True, candidate.Reason);
            Assert.That(candidate.Draft.parameterName, Is.Empty, "it is a real entry, not a parameter control");
            Assert.That(candidate.Draft.defaultOn, Is.True);
            var property = candidate.Draft.properties.Single();
            Assert.That(property.value, Is.EqualTo(1f), "the state the layer rests in is the on state");
            Assert.That(property.explicitOff, Is.True);
            Assert.That(property.minValue, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void InvertedLayerWithAFalseDefaultIsRefused()
        {
            var controller = new AnimatorController { name = "Shading_FX" };
            AddBoolLayer(controller, "Shading LTCGI", "Shading/LTCGI",
                PropertyClip("LTCGI_Off", "Body", "_SpecularHighlights", 0f),
                PropertyClip("LTCGI_On", "Body", "_SpecularHighlights", 1f), restsOn: true);
            var fixture = AddMergeMenu(controller);
            AddMergeToggle(fixture, "LTCGI", "Shading/LTCGI");
            Declare(fixture, "Shading/LTCGI", ParameterSyncType.Bool, 0f);

            var candidates = MergeAnimatorImport.Scan(avatar.Root.transform, Tree());
            var candidate = candidates.Single(c => c.Draft != null);

            Assert.That(candidate.Draft.IsParameterControl, Is.True, "the layer and the declared default disagree, so it is not read either way round");
            Assert.That(candidates.Single(c => c.MergeDraft != null).MergeDraft.skipLayers, Is.Empty);
        }

        [Test]
        public void OffClipWritingAnExtraBindingIsRefused()
        {
            var controller = new AnimatorController { name = "Shading_FX" };
            var on = PropertyClip("SSAO_On", "Body", "_Glossiness", 1f);
            var off = PropertyClip("SSAO_Off", "Body", "_Glossiness", 0f);
            AnimationUtility.SetEditorCurve(off, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "material._Metallic"), AnimationCurve.Constant(0f, 1f / 60f, 1f));
            AddBoolLayer(controller, "Shading SSAO", "Shading/SSAO", off, on);
            var fixture = AddMergeMenu(controller);
            AddMergeToggle(fixture, "SSAO", "Shading/SSAO");

            var candidates = MergeAnimatorImport.Scan(avatar.Root.transform, Tree());
            var candidate = candidates.Single(c => c.Draft != null);

            Assert.That(candidate.Draft.IsParameterControl, Is.True, "the off state does something the on state does not");
            Assert.That(candidates.Single(c => c.MergeDraft != null).MergeDraft.skipLayers, Is.Empty);
        }

        [Test]
        public void ParameterReadByAnotherMergeAnimatorStaysAParameterControl()
        {
            var controller = new AnimatorController { name = "Shading_FX" };
            AddBoolLayer(controller, "Shading SSAO", "Shading/SSAO",
                PropertyClip("SSAO_Off", "Body", "_Glossiness", 0f),
                PropertyClip("SSAO_On", "Body", "_Glossiness", 1f));
            var fixture = AddMergeMenu(controller);
            AddMergeToggle(fixture, "SSAO", "Shading/SSAO");

            var other = new AnimatorController { name = "Other_FX" };
            other.AddParameter("Shading/SSAO", AnimatorControllerParameterType.Bool);
            var otherGo = new GameObject("Other merge");
            otherGo.transform.SetParent(avatar.Root.transform);
            var otherMerge = otherGo.AddComponent<ModularAvatarMergeAnimator>();
            otherMerge.animator = other;
            otherMerge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;

            var candidates = MergeAnimatorImport.Scan(avatar.Root.transform, Tree());
            var candidate = candidates.Single(c => c.Draft != null);

            Assert.That(candidate.Draft.IsParameterControl, Is.True, "another merged controller still reads the old name");
            Assert.That(candidate.Draft.parameterName, Is.EqualTo("Shading/SSAO"));
            Assert.That(candidates.Single(c => c.MergeDraft != null).MergeDraft.skipLayers, Is.Empty);
        }

        [Test]
        public void UndeclaredParameterFollowsTheMenuItemFlags()
        {
            var controller = new AnimatorController { name = "Shading_FX" };
            AddBlendLayer(controller, "Shading Hair Shine", "Shading/HairShine",
                PropertyClip("HairShine_0", "Body", "_BumpScale", 0f),
                PropertyClip("HairShine_2x", "Body", "_BumpScale", 2f));
            // Modular Avatar creates the parameter from the items, so the controller's own default never reaches
            // the player: the expression parameter's default overwrites it.
            SetAnimatorDefault(controller, "Shading/HairShine", 0.25f);
            var fixture = AddMergeMenu(controller);
            var item = AddMergeRadial(fixture, "Hair Shine", "Shading/HairShine", 0.75f);
            item.isSaved = false;
            item.isSynced = true;
            item.isDefault = true;

            var candidate = MergeAnimatorImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft != null);

            Assert.That(candidate.Supported, Is.True, candidate.Reason);
            Assert.That(candidate.Draft.synced, Is.True, "Modular Avatar creates the parameter and syncs it when the item says so");
            Assert.That(candidate.Draft.saved, Is.False);
            Assert.That(candidate.Draft.defaultValue, Is.EqualTo(0.75f).Within(0.0001f), "the default is the item marked as the default one");
        }

        [Test]
        public void InvertedLayerWithAZeroValueControlIsRefused()
        {
            var controller = new AnimatorController { name = "Shading_FX" };
            AddBoolLayer(controller, "Shading LTCGI", "Shading/LTCGI",
                PropertyClip("LTCGI_Off", "Body", "_SpecularHighlights", 0f),
                PropertyClip("LTCGI_On", "Body", "_SpecularHighlights", 1f), restsOn: true);
            var fixture = AddMergeMenu(controller);
            // A control that sets the parameter to 0 is on while the parameter is false, so the state the layer
            // rests in is not its on state after all.
            AddMergeToggle(fixture, "LTCGI", "Shading/LTCGI", 0f);
            Declare(fixture, "Shading/LTCGI", ParameterSyncType.Bool, 1f);

            var candidates = MergeAnimatorImport.Scan(avatar.Root.transform, Tree());
            var candidate = candidates.Single(c => c.Draft != null);

            Assert.That(candidate.Draft.IsParameterControl, Is.True, "reading it inverted would swap the two clips");
            Assert.That(candidates.Single(c => c.MergeDraft != null).MergeDraft.skipLayers, Is.Empty);
        }

        [Test]
        public void ForwardLayerWithAZeroValueBoolControlIsRefused()
        {
            var controller = new AnimatorController { name = "Shading_FX" };
            AddBoolLayer(controller, "Shading SSAO", "Shading/SSAO",
                PropertyClip("SSAO_Off", "Body", "_SpecularHighlights", 0f),
                PropertyClip("SSAO_On", "Body", "_SpecularHighlights", 1f));
            var fixture = AddMergeMenu(controller);
            AddMergeToggle(fixture, "SSAO", "Shading/SSAO", 0f);
            Declare(fixture, "Shading/SSAO", ParameterSyncType.Bool, 0f);

            var candidates = MergeAnimatorImport.Scan(avatar.Root.transform, Tree());
            var candidate = candidates.Single(c => c.Draft != null);

            Assert.That(candidate.Draft.IsParameterControl, Is.True, "a control that sets false never reaches the on state");
            Assert.That(candidates.Single(c => c.MergeDraft != null).MergeDraft.skipLayers, Is.Empty);
        }

        [Test]
        public void InvertedLayerReadsItsDefaultFromTheExpressionParameters()
        {
            var controller = new AnimatorController { name = "Shading_FX" };
            AddBoolLayer(controller, "Shading LTCGI", "Shading/LTCGI",
                PropertyClip("LTCGI_Off", "Body", "_SpecularHighlights", 0f),
                PropertyClip("LTCGI_On", "Body", "_SpecularHighlights", 1f), restsOn: true);
            var fixture = AddMergeMenu(controller);
            AddMergeToggle(fixture, "LTCGI", "Shading/LTCGI");
            // Nothing declares it for Modular Avatar, but the avatar already carries it, so that is what it starts at.
            avatar.ExpressionParameters.parameters = avatar.ExpressionParameters.parameters
                .Concat(new[] { new VRCExpressionParameters.Parameter { name = "Shading/LTCGI", valueType = VRCExpressionParameters.ValueType.Bool, saved = true, networkSynced = true, defaultValue = 1f } })
                .ToArray();

            var candidate = MergeAnimatorImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft != null);

            Assert.That(candidate.Supported, Is.True, candidate.Reason);
            Assert.That(candidate.Draft.parameterName, Is.Empty, "it is a real entry, not a parameter control");
            Assert.That(candidate.Draft.defaultOn, Is.True);
            Assert.That(candidate.Draft.properties.Single().value, Is.EqualTo(1f), "the state the layer rests in is the on state");
        }

        [Test]
        public void FxLayerWhoseParameterAMergeAnimatorReadsIsNotConverted()
        {
            var on = AddFxToggleLayer("HatLayer", "Hat", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
            var clip = new AnimationClip { name = "HatOn" };
            SetIsActiveCurve(clip, "Hat", 1f);
            on.motion = clip;
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Hat", "Hat"));

            var other = new AnimatorController { name = "Other_FX" };
            other.AddParameter("Hat", AnimatorControllerParameterType.Bool);
            var otherGo = new GameObject("Other merge");
            otherGo.transform.SetParent(avatar.Root.transform);
            var merge = otherGo.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = other;
            merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;

            var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.parameterName == "Hat");

            Assert.That(candidate.Supported, Is.False, "a merged controller still reads the old name");
            Assert.That(candidate.Reason, Does.Contain("Hat"));
        }

        [Test]
        public void ReactiveItemUnderAMergeAnimatorStaysWithTheReactiveImporter()
        {
            var controller = new AnimatorController { name = "Shading_FX" };
            AddBoolLayer(controller, "Shading SSAO", "Shading/SSAO",
                PropertyClip("SSAO_Off", "Body", "_Glossiness", 0f),
                PropertyClip("SSAO_On", "Body", "_Glossiness", 1f));
            controller.AddParameter("Shading/Hat", AnimatorControllerParameterType.Bool);
            var fixture = AddMergeMenu(controller);
            AddMergeToggle(fixture, "SSAO", "Shading/SSAO");

            var hatItem = AddMergeToggle(fixture, "Hat", "Shading/Hat");
            var toggle = hatItem.gameObject.AddComponent<ModularAvatarObjectToggle>();
            var reference = new AvatarObjectReference();
            reference.Set(avatar.Hat);
            toggle.Objects = new List<ToggledObject> { new ToggledObject { Object = reference, Active = true } };

            Assert.That(MergeAnimatorImport.Claims(hatItem), Is.False, "its own reactive component drives it, not the merged animator");
            Assert.That(MergeAnimatorImport.Scan(avatar.Root.transform, Tree()).Any(c => c.Draft != null && c.Draft.name == "Hat"), Is.False);
            var reactive = ModularAvatarImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.name == "Hat");
            Assert.That(reactive.Supported, Is.True, reactive.Reason);
            Assert.That(reactive.Draft.objects.Single().target, Is.EqualTo(avatar.Hat));
        }

        [Test]
        public void TwoItemsOnOneLayerRefuseTheGroup()
        {
            var controller = new AnimatorController { name = "Shading_FX" };
            controller.AddParameter("Shading/A", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Shading/B", AnimatorControllerParameterType.Bool);
            var sm = AddLayerTo(controller, "Shading Shared");
            var off = sm.AddState("Off");
            var on = sm.AddState("On");
            sm.defaultState = off;
            off.motion = PropertyClip("Shared_Off", "Body", "_Glossiness", 0f);
            on.motion = PropertyClip("Shared_On", "Body", "_Glossiness", 1f);
            var byA = off.AddTransition(on);
            byA.hasExitTime = false;
            byA.AddCondition(AnimatorConditionMode.If, 0f, "Shading/A");
            var byB = off.AddTransition(on);
            byB.hasExitTime = false;
            byB.AddCondition(AnimatorConditionMode.If, 0f, "Shading/B");
            var fixture = AddMergeMenu(controller);
            AddMergeToggle(fixture, "A", "Shading/A");
            AddMergeToggle(fixture, "B", "Shading/B");

            var candidates = MergeAnimatorImport.Scan(avatar.Root.transform, Tree());

            Assert.That(candidates.Where(c => c.Draft != null).All(c => !c.Supported), Is.True, "one layer cannot become two entries");
            Assert.That(candidates.Any(c => c.MergeDraft != null), Is.False);
        }

        [Test]
        public void GroupWithARefusedMemberConvertsNothing()
        {
            var menu = avatar.AddMenu();
            var controller = new AnimatorController { name = "Shading_FX" };
            AddBoolLayer(controller, "Shading SSAO", "Shading/SSAO",
                PropertyClip("SSAO_Off", "Body", "_Glossiness", 0f),
                PropertyClip("SSAO_On", "Body", "_Glossiness", 1f));
            AddBoolLayer(controller, "Shading LTCGI", "Shading/LTCGI",
                PropertyClip("LTCGI_Off", "Body", "_Metallic", 0f),
                PropertyClip("LTCGI_On", "Body", "_Metallic", 1f));
            controller.AddParameter("Shading/AxisX", AnimatorControllerParameterType.Float);
            var fixture = AddMergeMenu(controller);
            var ssao = AddMergeToggle(fixture, "SSAO", "Shading/SSAO");

            var puppetGo = new GameObject("Wander");
            puppetGo.transform.SetParent(fixture.Root.transform);
            var puppet = puppetGo.AddComponent<ModularAvatarMenuItem>();
            puppet.Control = new VRCExpressionsMenu.Control
            {
                name = "Wander",
                type = VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "" },
                subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = "Shading/AxisX" } },
            };
            puppet.label = "Wander";
            AvatarPlanCache.Invalidate();

            var candidates = ImportScanner.Scan(avatar.Root.transform, AvatarPlanCache.GetFor(menu).Tree);
            var chosen = candidates.Where(c => c.Group != null && c.Supported).ToList();
            Assert.That(chosen, Is.Not.Empty, "the convertible items are offered");
            var created = ImportApply.Apply(menu, chosen, true);

            Assert.That(created, Is.Empty, "one member that cannot be converted stops the whole set");
            Assert.That(menu.entries, Is.Empty);
            Assert.That(menu.animators, Is.Empty);
            Assert.That(ssao != null && puppet != null, Is.True, "no original is removed");
            Assert.That(fixture.Merge != null && fixture.Submenu != null, Is.True);
        }

        [Test]
        public void MergeGroupRefusesToConvertWithoutRemovingOriginals()
        {
            var menu = avatar.AddMenu();
            var controller = new AnimatorController { name = "Shading_FX" };
            AddBoolLayer(controller, "Shading SSAO", "Shading/SSAO",
                PropertyClip("SSAO_Off", "Body", "_Glossiness", 0f),
                PropertyClip("SSAO_On", "Body", "_Glossiness", 1f));
            var fixture = AddMergeMenu(controller);
            AddMergeToggle(fixture, "SSAO", "Shading/SSAO");
            AvatarPlanCache.Invalidate();

            var candidates = ImportScanner.Scan(avatar.Root.transform, AvatarPlanCache.GetFor(menu).Tree)
                .Where(c => c.Group != null && c.Supported).ToList();
            var created = ImportApply.Apply(menu, candidates, false);

            Assert.That(created, Is.Empty, "leaving the Merge Animator behind would merge the controller twice");
            Assert.That(menu.entries, Is.Empty);
            Assert.That(candidates[0].Reason, Is.EqualTo(L.Text("ad.import.reason.group_needs_removal")));
        }

        [Test]
        public void RemovingOriginalsTrimsASharedParametersComponent()
        {
            var menu = avatar.AddMenu();
            var controller = new AnimatorController { name = "Shading_FX" };
            AddBoolLayer(controller, "Shading SSAO", "Shading/SSAO",
                PropertyClip("SSAO_Off", "Body", "_Glossiness", 0f),
                PropertyClip("SSAO_On", "Body", "_Glossiness", 1f));
            var fixture = AddMergeMenu(controller);
            AddMergeToggle(fixture, "SSAO", "Shading/SSAO");
            Declare(fixture, "Shading/SSAO", ParameterSyncType.Bool, 0f);
            Declare(fixture, "Other/Thing", ParameterSyncType.Bool, 0f);
            AvatarPlanCache.Invalidate();

            var candidates = ImportScanner.Scan(avatar.Root.transform, AvatarPlanCache.GetFor(menu).Tree)
                .Where(c => c.Group != null && c.Supported).ToList();
            ImportApply.Apply(menu, candidates, true);

            Assert.That(fixture.Parameters != null, Is.True, "a declaration something else still needs keeps its component");
            Assert.That(fixture.Parameters.parameters.Select(c => c.nameOrPrefix), Is.EqualTo(new[] { "Other/Thing" }));
        }

        [Test]
        public void ConvertedShadingStyleMenuBuilds()
        {
            var menu = avatar.AddMenu();
            const string neko = "Armature/Hips/Spine/Chest/_AFK_Neko_Chest/Neko";
            AddSkin("Body_2");
            AddSkin("Hair");
            AddSkin(neko);
            var everywhere = new[] { "Body", "Body_2", "Hair", neko };

            var controller = new AnimatorController { name = "Shading_FX" };
            AddBlendLayer(controller, "Shading Min Brightness", "Shading/MinBrightness",
                PropertiesClip("MinBrightness_0", everywhere, ("_OcclusionStrength", 0f)),
                PropertiesClip("MinBrightness_0.3", everywhere, ("_OcclusionStrength", 0.3f)));
            AddBlendLayer(controller, "Shading Grayscale", "Shading/Grayscale",
                PropertiesClip("Grayscale_0", everywhere, ("_GlossMapScale", 0f)),
                PropertiesClip("Grayscale_1", everywhere, ("_GlossMapScale", 1f)));
            AddBlendLayer(controller, "Shading Hair Shine", "Shading/HairShine",
                PropertiesClip("HairShine_0", new[] { "Hair" }, ("_BumpScale", 0f), ("_DetailNormalMapScale", 0f)),
                PropertiesClip("HairShine_2x", new[] { "Hair" }, ("_BumpScale", 2f), ("_DetailNormalMapScale", 2f)));
            AddBoolLayer(controller, "Shading LTCGI", "Shading/LTCGI",
                PropertiesClip("LTCGI_Off", everywhere, ("_SpecularHighlights", 0f)),
                PropertiesClip("LTCGI_On", everywhere, ("_SpecularHighlights", 1f)), restsOn: true);
            AddBoolLayer(controller, "Shading Oily Skin", "Shading/OilySkin",
                PropertiesClip("OilySkin_Off", new[] { "Body_2" }, ("_Glossiness", 0.6f), ("_Metallic", 0.8f), ("_Cutoff", 0.2f), ("_GlossyReflections", 0f), ("_UVSec", 1f)),
                PropertiesClip("OilySkin_On", new[] { "Body_2" }, ("_Glossiness", 1f), ("_Metallic", 1f), ("_Cutoff", 1f), ("_GlossyReflections", 1f), ("_UVSec", 0f)));
            AddBoolLayer(controller, "Shading SSAO", "Shading/SSAO",
                PropertiesClip("SSAO_Off", everywhere, ("_Parallax", 0f)),
                PropertiesClip("SSAO_On", everywhere, ("_Parallax", 1f)));

            var fixture = AddMergeMenu(controller);
            AddMergeRadial(fixture, "Min Brightness", "Shading/MinBrightness");
            AddMergeRadial(fixture, "Grayscale Lighting", "Shading/Grayscale");
            AddMergeRadial(fixture, "Hair Shine", "Shading/HairShine");
            AddMergeToggle(fixture, "LTCGI", "Shading/LTCGI");
            AddMergeToggle(fixture, "Oily Skin", "Shading/OilySkin");
            AddMergeToggle(fixture, "SSAO", "Shading/SSAO");
            Declare(fixture, "Shading/MinBrightness", ParameterSyncType.Float, 0.16666666f);
            Declare(fixture, "Shading/Grayscale", ParameterSyncType.Float, 0f);
            Declare(fixture, "Shading/HairShine", ParameterSyncType.Float, 0.5f);
            Declare(fixture, "Shading/LTCGI", ParameterSyncType.Bool, 1f);
            Declare(fixture, "Shading/OilySkin", ParameterSyncType.Bool, 0f);
            Declare(fixture, "Shading/SSAO", ParameterSyncType.Bool, 0f);
            AvatarPlanCache.Invalidate();

            var candidates = ImportScanner.Scan(avatar.Root.transform, AvatarPlanCache.GetFor(menu).Tree)
                .Where(c => c.Group != null && c.Supported).ToList();
            Assert.That(candidates.Count, Is.EqualTo(6), "all six items convert, so nothing is left to merge");
            Assert.That(candidates.Any(c => c.MergeDraft != null), Is.False);
            var created = ImportApply.Apply(menu, candidates, true);
            Assert.That(created.Count, Is.EqualTo(6));
            Assert.That(menu.animators, Is.Empty);
            var hairShine = created.Single(e => e.name == "Hair Shine");
            Assert.That(hairShine.properties.Select(t => t.property), Is.EquivalentTo(new[] { "_BumpScale", "_DetailNormalMapScale" }), "both properties of one renderer come over");
            AvatarPlanCache.Invalidate();

            var names = new[] { "Min Brightness", "Grayscale Lighting", "Hair Shine", "LTCGI", "Oily Skin", "SSAO" };
            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                var descriptor = processed.GetComponent<VRCAvatarDescriptor>();
                var fx = descriptor.baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
                foreach (var name in names)
                    Assert.That(fx.layers.Any(l => l.name == "AD: " + name), Is.True, name);
                Assert.That(fx.layers.Any(l => l.name.StartsWith("Shading ")), Is.False, "no layer of the merged controller is left over");

                var ltcgi = fx.layers.Single(l => l.name == "AD: LTCGI");
                var ltcgiOn = ltcgi.stateMachine.states.Single(s => s.state.name == "On").state;
                Assert.That(FloatOf(ltcgiOn.motion, neko, "_SpecularHighlights"), Is.EqualTo(1f), "a deep armature path comes over with the rest");

                var oily = fx.layers.Single(l => l.name == "AD: Oily Skin");
                var oilyOff = oily.stateMachine.states.Single(s => s.state.name == "Off").state;
                Assert.That(FloatOf(oilyOff.motion, "Body_2", "_Glossiness"), Is.EqualTo(0.6f).Within(0.0001f), "the old rest values are kept as the toggle's off values");
                Assert.That(FloatOf(oilyOff.motion, "Body_2", "_Metallic"), Is.EqualTo(0.8f).Within(0.0001f));
                Assert.That(FloatOf(oilyOff.motion, "Body_2", "_Cutoff"), Is.EqualTo(0.2f).Within(0.0001f));
                Assert.That(FloatOf(oilyOff.motion, "Body_2", "_UVSec"), Is.EqualTo(1f).Within(0.0001f));

                var parameters = descriptor.expressionParameters.parameters;
                foreach (var name in names)
                {
                    var parameter = parameters.SingleOrDefault(p => p.name == "AD/" + name);
                    Assert.That(parameter, Is.Not.Null, name);
                    Assert.That(parameter.networkSynced, Is.True, name);
                    Assert.That(parameter.saved, Is.True, name);
                }
                Assert.That(parameters.Single(p => p.name == "AD/Min Brightness").valueType, Is.EqualTo(VRCExpressionParameters.ValueType.Float));
                Assert.That(parameters.Single(p => p.name == "AD/Min Brightness").defaultValue, Is.EqualTo(0.16666666f).Within(0.0001f));
                Assert.That(parameters.Single(p => p.name == "AD/Hair Shine").defaultValue, Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(parameters.Single(p => p.name == "AD/LTCGI").valueType, Is.EqualTo(VRCExpressionParameters.ValueType.Bool));
                Assert.That(parameters.Single(p => p.name == "AD/LTCGI").defaultValue, Is.EqualTo(1f).Within(0.0001f), "the layer rested on, so the entry starts on");
                Assert.That(parameters.Single(p => p.name == "AD/SSAO").defaultValue, Is.EqualTo(0f).Within(0.0001f));

                var shading = descriptor.expressionsMenu.controls.Single(c => c.name == "Shading");
                Assert.That(shading.type, Is.EqualTo(VRCExpressionsMenu.Control.ControlType.SubMenu));
                Assert.That(shading.subMenu.controls.Select(c => c.name), Is.EquivalentTo(names));

                Assert.That(controller.layers.Length, Is.EqualTo(6), "the fixture controller is untouched");
                Assert.That(controller.parameters.Select(p => p.name), Is.EquivalentTo(new[]
                {
                    "Shading/MinBrightness", "Shading/Grayscale", "Shading/HairShine",
                    "Shading/LTCGI", "Shading/OilySkin", "Shading/SSAO",
                }));
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                GeneratedAssets.CleanUp();
            }
        }

        // Taking a whole Modular Avatar prefab over unchanged: a Menu Installer with a finished menu asset, a
        // Parameters component and a Merge Animator on one object, the shape Sera_Yura ships.
        sealed class TakeoverFixture
        {
            public GameObject Root;
            public ModularAvatarMenuInstaller Installer;
            public ModularAvatarParameters Parameters;
            public ModularAvatarMergeAnimator Merge;
            public VRCExpressionsMenu Asset;
            public AnimatorController Controller;
        }

        TakeoverFixture AddTakeoverPrefab(string parameter, bool internalParameter = false, string remapTo = "")
        {
            var controller = new AnimatorController { name = "Yura_FX" };
            AddBoolLayer(controller, "Yura Hat", parameter,
                PropertyClip("Hat_Off", "Body", "_Glossiness", 0f),
                PropertyClip("Hat_On", "Body", "_Glossiness", 1f));

            var asset = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            asset.name = "Yura Menu";
            asset.controls = new List<VRCExpressionsMenu.Control> { TestAvatar.ToggleControl("Hat", parameter) };

            var go = new GameObject("Yura Menu");
            go.transform.SetParent(avatar.Root.transform);
            var fixture = new TakeoverFixture { Root = go, Asset = asset, Controller = controller };
            fixture.Installer = go.AddComponent<ModularAvatarMenuInstaller>();
            fixture.Installer.menuToAppend = asset;
            fixture.Parameters = go.AddComponent<ModularAvatarParameters>();
            fixture.Parameters.parameters.Add(new ParameterConfig
            {
                nameOrPrefix = parameter,
                syncType = ParameterSyncType.Bool,
                defaultValue = 1f,
                saved = true,
                hasExplicitDefaultValue = true,
                internalParameter = internalParameter,
                remapTo = remapTo,
            });
            fixture.Merge = go.AddComponent<ModularAvatarMergeAnimator>();
            fixture.Merge.animator = controller;
            fixture.Merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            fixture.Merge.pathMode = MergeAnimatorPathMode.Absolute;
            fixture.Merge.matchAvatarWriteDefaults = true;
            return fixture;
        }

        [Test]
        public void TakeoverOfAModularAvatarPrefabKeepsItAsItIs()
        {
            var menu = avatar.AddMenu();
            var fixture = AddTakeoverPrefab("Yura/Hat");
            var assetBefore = EditorJsonUtility.ToJson(fixture.Asset);
            AvatarPlanCache.Invalidate();

            GameObject processed = null;
            try
            {
                var chosen = ImportScanner.Scan(avatar.Root.transform, AvatarPlanCache.GetFor(menu).Tree)
                    .Where(c => c.MenuAssetDraft != null || c.MergeDraft != null).ToList();
                Assert.That(chosen, Is.Not.Empty, "the prefab is offered as one group");
                Assert.That(chosen.All(c => c.Supported), Is.True, chosen.Select(c => c.Reason).FirstOrDefault());
                foreach (var candidate in chosen) candidate.KeepAsItIs = true;

                ImportApply.Apply(menu, chosen, true);

                var install = menu.menuAssets.Single();
                Assert.That(install.menu, Is.SameAs(fixture.Asset));
                Assert.That(install.asSubmenu, Is.False, "a Menu Installer appends its asset, so the takeover appends it too");
                var declaration = menu.parameters.Single();
                Assert.That(declaration.name, Is.EqualTo("Yura/Hat"));
                Assert.That(declaration.type, Is.EqualTo(PlannedType.Bool));
                Assert.That(declaration.synced, Is.True);
                Assert.That(declaration.saved, Is.True);
                Assert.That(declaration.defaultValue, Is.EqualTo(1f).Within(0.0001f));
                var merge = menu.animators.Single();
                Assert.That(merge.controller, Is.SameAs(fixture.Controller));
                Assert.That(merge.skipLayers, Is.Empty, "Keep as it is converts no layer");
                Assert.That(menu.entries, Is.Empty);

                Assert.That(fixture.Installer == null && fixture.Parameters == null && fixture.Merge == null, Is.True, "the originals go");
                Assert.That(fixture.Root != null, Is.True, "their object stays");
                Assert.That(EditorJsonUtility.ToJson(fixture.Asset), Is.EqualTo(assetBefore), "the menu asset is untouched");
                Assert.That(fixture.Controller.layers.Length, Is.EqualTo(1), "so is the controller");

                AvatarPlanCache.Invalidate();
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                var descriptor = processed.GetComponent<VRCAvatarDescriptor>();
                var installed = descriptor.expressionsMenu.controls.Single(c => c.name == "Hat");
                Assert.That(installed.parameter.name, Is.EqualTo("Yura/Hat"), "the avatar has the menu, appended the way the installer appended it");
                Assert.That(descriptor.expressionParameters.parameters.Any(p => p.name == "Yura/Hat" && p.networkSynced), Is.True,
                    "and the parameter it declares");
                var fx = descriptor.baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
                Assert.That(fx.layers.Any(l => l.name == "Yura Hat"), Is.True, "and the merged layers");
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                GeneratedAssets.CleanUp();
                Object.DestroyImmediate(fixture.Asset);
                Object.DestroyImmediate(fixture.Controller);
            }
        }

        [Test]
        public void TakeoverRefusesRenamedParameters()
        {
            var menu = avatar.AddMenu();
            var fixture = AddTakeoverPrefab("Yura/Hat", internalParameter: true);
            AvatarPlanCache.Invalidate();

            try
            {
                var candidates = ImportScanner.Scan(avatar.Root.transform, AvatarPlanCache.GetFor(menu).Tree)
                    .Where(c => c.MenuAssetDraft != null || c.MergeDraft != null).ToList();

                Assert.That(candidates, Is.Not.Empty);
                Assert.That(candidates.All(c => !c.Supported), Is.True, "an internal parameter renames itself at build time");
                Assert.That(candidates[0].Reason, Is.EqualTo(string.Format(L.Text("ad.import.reason.ma_renamed_parameters"), "Yura/Hat")));

                ImportApply.Apply(menu, candidates, true);

                Assert.That(menu.menuAssets, Is.Empty);
                Assert.That(menu.parameters, Is.Empty);
                Assert.That(menu.animators, Is.Empty);
                Assert.That(fixture.Installer != null && fixture.Parameters != null && fixture.Merge != null, Is.True, "nothing is removed");
            }
            finally
            {
                Object.DestroyImmediate(fixture.Asset);
                Object.DestroyImmediate(fixture.Controller);
            }
        }

        /// <summary>
        /// A prefab that ships both: a finished menu asset appended at the root and one Modular Avatar menu item
        /// whose layer the conversion can really take over, on the same Merge Animator.
        /// </summary>
        sealed class MixedTakeoverFixture
        {
            public GameObject Root;
            public ModularAvatarMenuInstaller Installer;
            public ModularAvatarParameters Parameters;
            public ModularAvatarMergeAnimator Merge;
            public ModularAvatarMenuItem Item;
            public VRCExpressionsMenu Asset;
            public AnimatorController Controller;
        }

        MixedTakeoverFixture AddMixedTakeoverPrefab()
        {
            var controller = new AnimatorController { name = "Yura_FX" };
            AddBoolLayer(controller, "Yura Hat", "Yura/Hat",
                PropertyClip("Hat_Off", "Body", "_Glossiness", 0f),
                PropertyClip("Hat_On", "Body", "_Glossiness", 1f));
            AddBoolLayer(controller, "Yura Shine", "Yura/Shine",
                PropertyClip("Shine_Off", "Body", "_Metallic", 0f),
                PropertyClip("Shine_On", "Body", "_Metallic", 1f));

            var asset = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            asset.name = "Yura Menu";
            asset.controls = new List<VRCExpressionsMenu.Control>
            {
                TestAvatar.ToggleControl("Hat", "Yura/Hat"),
                TestAvatar.ToggleControl("Shine", "Yura/Shine"),
            };

            var go = new GameObject("Yura Menu");
            go.transform.SetParent(avatar.Root.transform);
            var fixture = new MixedTakeoverFixture { Root = go, Asset = asset, Controller = controller };
            fixture.Installer = go.AddComponent<ModularAvatarMenuInstaller>();
            fixture.Installer.menuToAppend = asset;
            fixture.Parameters = go.AddComponent<ModularAvatarParameters>();
            foreach (var name in new[] { "Yura/Hat", "Yura/Shine" })
                fixture.Parameters.parameters.Add(new ParameterConfig
                {
                    nameOrPrefix = name,
                    syncType = ParameterSyncType.Bool,
                    defaultValue = 0f,
                    saved = true,
                    hasExplicitDefaultValue = true,
                });
            fixture.Merge = go.AddComponent<ModularAvatarMergeAnimator>();
            fixture.Merge.animator = controller;
            fixture.Merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            fixture.Merge.pathMode = MergeAnimatorPathMode.Absolute;
            fixture.Merge.matchAvatarWriteDefaults = true;

            // Its own installer, so the merged menu reads it as an item of its own rather than through the asset.
            var itemGo = new GameObject("Hat");
            itemGo.transform.SetParent(go.transform);
            itemGo.AddComponent<ModularAvatarMenuInstaller>();
            fixture.Item = itemGo.AddComponent<ModularAvatarMenuItem>();
            fixture.Item.Control = new VRCExpressionsMenu.Control
            {
                name = "Hat",
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "Yura/Hat" },
                value = 1f,
            };
            fixture.Item.label = "Hat";
            return fixture;
        }

        [Test]
        public void ReplacedAssetControlIsHiddenNotItsNamesake()
        {
            // The avatar already has a control of the same name in the same place, which the hide must not take.
            avatar.ExpressionsMenu.controls.Add(TestAvatar.ToggleControl("Hat", "Existing"));
            var menu = avatar.AddMenu();
            var fixture = AddMixedTakeoverPrefab();
            AvatarPlanCache.Invalidate();

            try
            {
                var chosen = ImportScanner.Scan(avatar.Root.transform, AvatarPlanCache.GetFor(menu).Tree)
                    .Where(c => c.Group != null && c.Supported).ToList();
                var created = ImportApply.Apply(menu, chosen, true);

                Assert.That(created.Count, Is.EqualTo(1), "the Modular Avatar item became a real entry");
                AvatarPlanCache.Invalidate();
                var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
                var named = tree.Root.Items.Where(i => i.Name == "Hat").ToList();

                var mine = named.Single(i => i.Asset != null);
                Assert.That(mine.Hidden, Is.True, "the installed control the entry replaced is hidden");
                var theirs = named.Single(i => i.Asset == null && i.Existing != null);
                Assert.That(theirs.Hidden, Is.False, "the avatar's own control of that name stays");
                Assert.That(tree.Root.Items.Single(i => i.Name == "Shine").Hidden, Is.False, "nothing else is hidden");
            }
            finally
            {
                Object.DestroyImmediate(fixture.Asset);
                Object.DestroyImmediate(fixture.Controller);
            }
        }

        [Test]
        public void ConvertedLayersDoNotLeaveTheirDeclarationBehind()
        {
            var menu = avatar.AddMenu();
            var fixture = AddMixedTakeoverPrefab();
            AvatarPlanCache.Invalidate();

            try
            {
                var chosen = ImportScanner.Scan(avatar.Root.transform, AvatarPlanCache.GetFor(menu).Tree)
                    .Where(c => c.Group != null && c.Supported).ToList();
                ImportApply.Apply(menu, chosen, true);

                Assert.That(menu.animators.Single().skipLayers, Is.EqualTo(new List<string> { "Yura Hat" }));
                Assert.That(menu.parameters.Select(p => p.name), Is.EqualTo(new[] { "Yura/Shine" }),
                    "the converted layer took its declaration with it; the merged one keeps its own");
            }
            finally
            {
                Object.DestroyImmediate(fixture.Asset);
                Object.DestroyImmediate(fixture.Controller);
            }
        }

        [Test]
        public void MergedLayerWhoseParameterAnotherLayerDrivesStaysMerged()
        {
            var controller = new AnimatorController { name = "Shading_FX" };
            AddBoolLayer(controller, "Shading SSAO", "Shading/SSAO",
                PropertyClip("SSAO_Off", "Body", "_SSAOAnimationToggle", 0f),
                PropertyClip("SSAO_On", "Body", "_SSAOAnimationToggle", 1f));
            // A "Reset" layer that switches SSAO off again. It stays merged, so the old name has to stay alive.
            var reset = AddLayerTo(controller, "Reset");
            var resetState = reset.AddState("Reset");
            reset.defaultState = resetState;
            resetState.AddStateMachineBehaviour<VRCAvatarParameterDriver>().parameters.Add(new VRC_AvatarParameterDriver.Parameter
            {
                type = VRC_AvatarParameterDriver.ChangeType.Set,
                name = "Shading/SSAO",
                value = 0f,
            });
            var fixture = AddMergeMenu(controller);
            AddMergeToggle(fixture, "SSAO", "Shading/SSAO");

            var candidates = MergeAnimatorImport.Scan(avatar.Root.transform, Tree());
            var item = candidates.Single(c => c.Draft != null);
            var merge = candidates.Single(c => c.MergeDraft != null);

            Assert.That(item.Supported, Is.True, item.Reason);
            Assert.That(item.Draft.IsParameterControl, Is.True, "the driver would stop working on a parameter of its own");
            Assert.That(item.Draft.parameterName, Is.EqualTo("Shading/SSAO"));
            Assert.That(item.Draft.properties, Is.Empty);
            Assert.That(merge.MergeDraft.skipLayers, Is.Empty, "both layers stay in the merge");
        }

        [Test]
        public void UnclaimedInstallerOnTheMergeObjectStillBlocksConversion()
        {
            var controller = new AnimatorController { name = "Shading_FX" };
            AddBoolLayer(controller, "Shading SSAO", "Shading/SSAO",
                PropertyClip("SSAO_Off", "Body", "_SSAOAnimationToggle", 0f),
                PropertyClip("SSAO_On", "Body", "_SSAOAnimationToggle", 1f));
            var fixture = AddMergeMenu(controller);
            AddMergeToggle(fixture, "SSAO", "Shading/SSAO");
            // The installer beside the Merge Animator also appends a finished menu. Its Menu Item is a submenu of
            // children, so no takeover claims that menu: Modular Avatar keeps installing it, control and all.
            var extra = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            extra.name = "Extra";
            extra.controls = new List<VRCExpressionsMenu.Control> { TestAvatar.ToggleControl("SSAO again", "Shading/SSAO") };
            fixture.Installer.menuToAppend = extra;

            try
            {
                var candidates = MergeAnimatorImport.Scan(avatar.Root.transform, Tree());
                var item = candidates.Single(c => c.Draft != null);
                var merge = candidates.Single(c => c.MergeDraft != null);

                Assert.That(item.Supported, Is.True, item.Reason);
                Assert.That(item.Draft.IsParameterControl, Is.True, "the appended control would be left driving a name nothing answers");
                Assert.That(item.Draft.parameterName, Is.EqualTo("Shading/SSAO"));
                Assert.That(merge.MergeDraft.skipLayers, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(extra);
            }
        }

        [Test]
        public void TakeoverOfAnUnknownInstallTargetIsRefused()
        {
            var menu = avatar.AddMenu();
            var fixture = AddTakeoverPrefab("Yura/Hat");
            // A menu another installer brings: it is nowhere in this avatar's tree, so the path cannot be read.
            var stranger = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            stranger.name = "Somewhere else";
            stranger.controls = new List<VRCExpressionsMenu.Control>();
            fixture.Installer.installTargetMenu = stranger;
            AvatarPlanCache.Invalidate();

            try
            {
                var candidates = ImportScanner.Scan(avatar.Root.transform, AvatarPlanCache.GetFor(menu).Tree)
                    .Where(c => c.MenuAssetDraft != null || c.MergeDraft != null).ToList();

                Assert.That(candidates, Is.Not.Empty);
                Assert.That(candidates.All(c => !c.Supported), Is.True, "the whole group waits until the target is known");
                Assert.That(candidates[0].Reason, Is.EqualTo(L.Text("ad.import.reason.install_target_unknown")));
            }
            finally
            {
                Object.DestroyImmediate(stranger);
                Object.DestroyImmediate(fixture.Asset);
                Object.DestroyImmediate(fixture.Controller);
            }
        }
#endif

        [Test]
        public void ApplyCreatesSubmenusAndEntriesWithUndo()
        {
            var menu = avatar.AddMenu();
            var draft = MenuEntry.Create(EntryKind.Toggle, "Hat");
            draft.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = true });
            var candidate = new ImportCandidate { Source = ImportSource.VRCFury, Path = new List<string> { "Clothes" }, Draft = draft, Supported = true };

            var created = ImportApply.Apply(menu, new List<ImportCandidate> { candidate }, false);

            Assert.That(menu.entries.Count, Is.EqualTo(2), "one submenu plus the imported entry");
            var submenu = menu.entries.Single(e => e.kind == EntryKind.Submenu);
            Assert.That(submenu.PlainName, Is.EqualTo("Clothes"));
            Assert.That(created.Single().parentId, Is.EqualTo(submenu.id));
            Assert.That(created.Single().id, Is.Not.EqualTo(draft.id), "the applied entry gets a fresh id");

            Undo.PerformUndo();
            Assert.That(menu.entries, Is.Empty, "one undo step reverts the whole import");
        }

        [Test]
        public void ApplyRemovesOriginalsOnlyWhenAsked()
        {
            var menu = avatar.AddMenu();

            var keepGo = new GameObject("Origin");
            keepGo.transform.SetParent(avatar.Root.transform);
            var keepOrigin = keepGo.AddComponent<BoxCollider>();
            var keepCandidate = new ImportCandidate { Path = new List<string>(), Draft = MenuEntry.Create(EntryKind.Toggle, "Keep"), Supported = true, OriginComponents = new List<Component> { keepOrigin } };
            ImportApply.Apply(menu, new List<ImportCandidate> { keepCandidate }, false);
            Assert.That(keepOrigin != null, Is.True, "not removed when removeOriginals is false");

            var removeGo = new GameObject("Origin2");
            removeGo.transform.SetParent(avatar.Root.transform);
            var removeOrigin = removeGo.AddComponent<BoxCollider>();
            var removeCandidate = new ImportCandidate { Path = new List<string>(), Draft = MenuEntry.Create(EntryKind.Toggle, "Remove"), Supported = true, OriginComponents = new List<Component> { removeOrigin } };
            ImportApply.Apply(menu, new List<ImportCandidate> { removeCandidate }, true);
            Assert.That(removeOrigin == null, Is.True, "removed when removeOriginals is true");
        }

        [Test]
        public void ImportedEntriesBuild()
        {
            var menu = avatar.AddMenu();
            var draft = MenuEntry.Create(EntryKind.Toggle, "Hat");
            draft.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = true });
            var candidate = new ImportCandidate { Path = new List<string>(), Draft = draft, Supported = true };
            ImportApply.Apply(menu, new List<ImportCandidate> { candidate }, false);

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                Assert.That(processed, Is.Not.Null);
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                GeneratedAssets.CleanUp();
            }
        }

        /// <summary>Adds a hand made toggle layer to the test avatar's FX controller: an Off state and an On state whose motion the caller sets, reached by one condition on paramName. Full weight and no mask, so only the thing under test makes it unsupported.</summary>
        AnimatorState AddFxToggleLayer(string layerName, string paramName, AnimatorControllerParameterType type, AnimatorConditionMode mode, float threshold)
        {
            if (avatar.Fx.parameters.All(p => p.name != paramName)) avatar.Fx.AddParameter(paramName, type);
            avatar.Fx.AddLayer(layerName);
            var layers = avatar.Fx.layers;
            int index = layers.Length - 1;
            layers[index].defaultWeight = 1f;
            avatar.Fx.layers = layers;
            var sm = avatar.Fx.layers[index].stateMachine;
            var off = sm.AddState("Off");
            var on = sm.AddState("On");
            sm.defaultState = off;
            on.writeDefaultValues = false;
            var transition = off.AddTransition(on);
            transition.hasExitTime = false;
            transition.AddCondition(mode, threshold, paramName);
            return on;
        }

        static void SetIsActiveCurve(AnimationClip clip, string path, float value)
            => AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0f, 1f / 60f, value));

        static void SetBlendShapeCurve(AnimationClip clip, string path, string shape, float value)
            => AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + shape), AnimationCurve.Constant(0f, 1f / 60f, value));

        static void SetMaterialSlotCurve(AnimationClip clip, string path, int slot, Material material)
            => AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve(path, typeof(SkinnedMeshRenderer), "m_Materials.Array.data[" + slot + "]"),
                new[] { new ObjectReferenceKeyframe { time = 0f, value = material }, new ObjectReferenceKeyframe { time = 1f / 60f, value = material } });

        static void SetMaterialPropertyCurve(AnimationClip clip, string path, string property, float value)
            => AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Renderer), "material." + property), AnimationCurve.Constant(0f, 1f / 60f, value));

        [Test]
        public void SimpleToggleLayerBecomesACandidate()
        {
            var on = AddFxToggleLayer("HatLayer", "Hat", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
            var clip = new AnimationClip { name = "HatOn" };
            SetIsActiveCurve(clip, "Hat", 1f);
            SetBlendShapeCurve(clip, "Body", "Blush", 100f);
            SetMaterialSlotCurve(clip, "Body", 1, avatar.MaterialB);
            on.motion = clip;
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Hat", "Hat"));

            var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.parameterName == "Hat");

            Assert.That(candidate.Supported, Is.True, candidate.Reason);
            Assert.That(candidate.Source, Is.EqualTo(ImportSource.FxLayer));
            Assert.That(candidate.OriginLayer, Is.EqualTo("HatLayer"));
            Assert.That(candidate.OriginControlPath, Is.EqualTo(new List<string> { "Hat" }));
            Assert.That(candidate.Draft.objects.Single(o => o.target == avatar.Hat).activeWhenOn, Is.True);
            var shape = candidate.Draft.blendShapes.Single();
            Assert.That(shape.renderer, Is.EqualTo(avatar.Body));
            Assert.That(shape.shape, Is.EqualTo("Blush"));
            Assert.That(shape.weight, Is.EqualTo(100f));
            var material = candidate.Draft.materials.Single();
            Assert.That(material.renderer, Is.EqualTo(avatar.Body));
            Assert.That(material.slot, Is.EqualTo(1));
            Assert.That(material.material, Is.EqualTo(avatar.MaterialB));
            Assert.That(candidate.Draft.parameterValue, Is.EqualTo(1f));
        }

        /// <summary>Takes the Undo records Unity's animator calls left for these objects off the shared Undo stack. A destroyed one is skipped.</summary>
        static void ClearUndoOf(params Object[] made)
        {
            foreach (var obj in made)
                if (obj != null) Undo.ClearUndo(obj);
        }

        [Test]
        public void AMissingStateOrTransitionInALayerDoesNotBreakTheConvertScan()
        {
            // A healthy layer, so there is a candidate the scan has to go on offering.
            var hatOn = AddFxToggleLayer("HatLayer", "Hat", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
            var hatClip = new AnimationClip { name = "HatOn" };
            SetIsActiveCurve(hatClip, "Hat", 1f);
            hatOn.motion = hatClip;
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Hat", "Hat"));

            // What a bad merge or a hand edited controller leaves behind: the layer still lists a state and a
            // transition, but the objects they name are gone. The on state's own way out is one of them.
            var earsOn = AddFxToggleLayer("EarsLayer", "Ears", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
            var earsMachine = avatar.Fx.layers.Single(l => l.name == "EarsLayer").stateMachine;
            var lostState = earsMachine.AddState("Lost");
            var lostTransition = earsOn.AddTransition(lostState);
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Ears", "Ears"));

            // The same for a layer that rests in its on state, whose way back out is gone.
            avatar.Fx.AddParameter("Inv", AnimatorControllerParameterType.Bool);
            avatar.Fx.AddLayer("InvLayer");
            var layers = avatar.Fx.layers;
            layers[layers.Length - 1].defaultWeight = 1f;
            avatar.Fx.layers = layers;
            var invMachine = avatar.Fx.layers[avatar.Fx.layers.Length - 1].stateMachine;
            var idle = invMachine.AddState("Idle");
            var other = invMachine.AddState("Other");
            invMachine.defaultState = idle;
            var leave = idle.AddTransition(other);
            leave.hasExitTime = false;
            leave.AddCondition(AnimatorConditionMode.IfNot, 0f, "Inv");
            var back = other.AddTransition(idle);
            back.hasExitTime = false;
            back.AddCondition(AnimatorConditionMode.If, 0f, "Inv");
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Inv", "Inv"));

            try
            {
                ClearUndoOf(lostTransition, lostState, back);
                Object.DestroyImmediate(lostTransition, true);
                Object.DestroyImmediate(lostState, true);
                Object.DestroyImmediate(back, true);

                List<ImportCandidate> candidates = null;
                Assert.DoesNotThrow(() => candidates = FxLayerImport.Scan(avatar.Root.transform, Tree()), "the scan reads every layer of the controller");
                Assert.That(candidates.Single(c => c.Draft.parameterName == "Hat").Supported, Is.True, "the healthy layer is still offered");
                Assert.That(candidates.Single(c => c.Draft.parameterName == "Ears").Supported, Is.False, "the broken layer is listed, as one that cannot be converted");
                Assert.That(candidates.Single(c => c.Draft.parameterName == "Inv").Supported, Is.False);
                Assert.DoesNotThrow(() => ImportScanner.Scan(avatar.Root.transform, Tree()), "and so is the scan of the whole avatar");
            }
            finally
            {
                ClearUndoOf(hatOn, earsOn, earsMachine, idle, other, leave, invMachine, avatar.Fx);
            }
        }

        /// <summary>A second AvatarDoc, switched off, that had renamed the avatar's own "Hat" control.</summary>
        AvatarDoc OlderDisabledWithAnEditOfHat()
        {
            var olderObject = new GameObject("Older");
            olderObject.transform.SetParent(avatar.Root.transform);
            var older = olderObject.AddComponent<AvatarDoc>();
            older.menuEdits.Add(new MenuEdit { id = "old", source = new List<string> { "Hat" }, occurrence = 0, name = "Old name" });
            older.enabled = false;
            AvatarPlanCache.Invalidate();
            return older;
        }

        [Test]
        public void ConvertDoesNotCarryOverAnEditOfADisabledComponent()
        {
            var on = AddFxToggleLayer("HatLayer", "Hat", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
            var clip = new AnimationClip { name = "HatOn" };
            SetIsActiveCurve(clip, "Hat", 1f);
            on.motion = clip;
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Hat", "Hat"));
            var menu = avatar.AddMenu();
            var older = OlderDisabledWithAnEditOfHat();

            var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.parameterName == "Hat");
            var created = ImportApply.Apply(menu, new List<ImportCandidate> { candidate }, true).Single();

            Assert.That(created.PlainName, Is.EqualTo("Hat"), "the rename was never shown in the tree, so it does not come along");
            Assert.That(older.menuEdits.Single().name, Is.EqualTo("Old name"), "and the switched off component keeps its edit");
        }

        [Test]
        public void AnEditOnALockedDisabledComponentDoesNotBlockConvert()
        {
            var on = AddFxToggleLayer("HatLayer", "Hat", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
            var clip = new AnimationClip { name = "HatOn" };
            SetIsActiveCurve(clip, "Hat", 1f);
            on.motion = clip;
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Hat", "Hat"));
            var menu = avatar.AddMenu();
            OlderDisabledWithAnEditOfHat().locked = true;

            var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.parameterName == "Hat");
            var created = ImportApply.Apply(menu, new List<ImportCandidate> { candidate }, true);

            Assert.That(created.Count, Is.EqualTo(1), "an edit the build never applies is not one the conversion would strand");
            Assert.That(candidate.Supported, Is.True);
        }

        [Test]
        public void FxConversionCarriesTheOscNote()
        {
            var on = AddFxToggleLayer("GlowLayer", "Glow", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
            var clip = new AnimationClip { name = "GlowOn" };
            SetIsActiveCurve(clip, "Hat", 1f);
            on.motion = clip;
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Glow", "Glow"));

            var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.parameterName == "Glow");

            Assert.That(candidate.Supported, Is.True, candidate.Reason);
            Assert.That(candidate.Notes, Does.Contain(L.Text("ad.import.note.osc")), "the entry gets a parameter of its own, so the old name stops working");
        }

        [Test]
        public void IntParameterWithAValueIsSupported()
        {
            var on = AddFxToggleLayer("OutfitLayer", "Outfit", AnimatorControllerParameterType.Int, AnimatorConditionMode.Equals, 2f);
            var clip = new AnimationClip { name = "OutfitOn" };
            SetIsActiveCurve(clip, "Glasses", 1f);
            on.motion = clip;
            avatar.Descriptor.expressionsMenu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "Outfit", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "Outfit" }, value = 2,
            });

            var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.parameterName == "Outfit");

            Assert.That(candidate.Supported, Is.True, candidate.Reason);
            Assert.That(candidate.Draft.parameterValue, Is.EqualTo(2f));
            Assert.That(candidate.Draft.objects.Single().target, Is.EqualTo(avatar.Glasses));
        }

        [Test]
        public void SharedParameterIsUnsupported()
        {
            AddFxToggleLayer("LayerA", "Shared", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
            AddFxToggleLayer("LayerB", "Shared", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Shared", "Shared"));

            var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.parameterName == "Shared");

            Assert.That(candidate.Supported, Is.False);
            Assert.That(candidate.Reason, Does.Contain("Shared"));
        }

        [Test]
        public void BlendTreeIsUnsupported()
        {
            var on = AddFxToggleLayer("PoofyLayer", "Poofy", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
            on.motion = new BlendTree { name = "PoofyTree", blendType = BlendTreeType.Simple1D, blendParameter = "Existing" };
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Poofy", "Poofy"));

            var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.parameterName == "Poofy");

            Assert.That(candidate.Supported, Is.False);
            Assert.That(candidate.Reason, Is.EqualTo(L.Text("ad.import.reason.fx_blend_tree")));
        }

        [Test]
        public void AnimatedCurveIsUnsupported()
        {
            var on = AddFxToggleLayer("WiggleLayer", "Wiggle", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
            var clip = new AnimationClip { name = "WiggleOn" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Hat", typeof(GameObject), "m_IsActive"),
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f)));
            on.motion = clip;
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Wiggle", "Wiggle"));

            var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.parameterName == "Wiggle");

            Assert.That(candidate.Supported, Is.False);
            Assert.That(candidate.Reason, Is.EqualTo(string.Format(L.Text("ad.import.reason.fx_unsupported_curve"), "m_IsActive", "GameObject")));
        }

        [Test]
        public void MaterialPropertyColorCurvesBecomeAColorTarget()
        {
            var on = AddFxToggleLayer("GlowLayer", "Glow", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
            var clip = new AnimationClip { name = "GlowOn" };
            SetMaterialPropertyCurve(clip, "Body", "_Color.r", 1f);
            SetMaterialPropertyCurve(clip, "Body", "_Color.g", 0.5f);
            SetMaterialPropertyCurve(clip, "Body", "_Color.b", 0.25f);
            SetMaterialPropertyCurve(clip, "Body", "_Color.a", 1f);
            on.motion = clip;
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Glow", "Glow"));

            var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.parameterName == "Glow");

            Assert.That(candidate.Supported, Is.True, candidate.Reason);
            var property = candidate.Draft.properties.Single();
            Assert.That(property.renderer, Is.EqualTo(avatar.Body));
            Assert.That(property.property, Is.EqualTo("_Color"));
            Assert.That(property.type, Is.EqualTo(PropertyValueType.Color));
            Assert.That(property.color, Is.EqualTo(new Color(1f, 0.5f, 0.25f, 1f)));
        }

        [Test]
        public void ApplyHidesTheOldControlAndListsTheLayer()
        {
            var on = AddFxToggleLayer("HatLayer", "Hat", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
            var clip = new AnimationClip { name = "HatOn" };
            SetIsActiveCurve(clip, "Hat", 1f);
            on.motion = clip;
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Hat", "Hat"));
            var menu = avatar.AddMenu();

            var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.parameterName == "Hat");
            ImportApply.Apply(menu, new List<ImportCandidate> { candidate }, true);

            Assert.That(menu.removedLayers.Any(r => r.layer == "HatLayer"), Is.True);
            var edit = menu.menuEdits.Single();
            Assert.That(edit.hide, Is.True);
            Assert.That(edit.source, Is.EqualTo(new List<string> { "Hat" }));
            Assert.That(edit.occurrence, Is.EqualTo(0));
            Assert.That(menu.removedLayers.Single().editId, Is.EqualTo(edit.id));
            Assert.That(menu.entries.Single(e => e.kind == EntryKind.Toggle).PlainName, Is.EqualTo("Hat"));
        }

        [Test]
        public void CandidateWithAnEditOnALockedComponentIsNotImported()
        {
            var toggle = AddFuryToggle("Clothes/Hat");
            VrcFuryTestHelper.AddAction(toggle, VrcFuryReader.ObjectToggleActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "obj", avatar.Hat);
                VrcFuryTestHelper.SetField(a, "mode", 0);
            });
            var menu = avatar.AddMenu();
            var makerObject = new GameObject("Maker");
            makerObject.transform.SetParent(avatar.Root.transform);
            var maker = makerObject.AddComponent<AvatarDoc>();
            maker.menuEdits.Add(new MenuEdit { id = "e", source = new List<string> { "Clothes", "Hat" }, name = "Cap" });
            maker.locked = true;
            AvatarPlanCache.Invalidate();

            var candidate = VrcFuryImport.Build(toggle, avatar.Root.transform);
            var created = ImportApply.Apply(menu, new List<ImportCandidate> { candidate }, true);

            Assert.That(created, Is.Empty, "the item stays where it is rather than importing and stranding the edit");
            Assert.That(maker.menuEdits.Count, Is.EqualTo(1), "the locked component's edit was not touched");
            Assert.That(menu.entries, Is.Empty);
            Assert.That(candidate.Supported, Is.False);
            Assert.That(candidate.Reason, Is.EqualTo(L.Text("ad.import.reason.edit_locked")));
        }

        [Test]
        public void ConvertCarriesTheEditsNameAndIconAndDeletesTheEdit()
        {
            var icon = new Texture2D(4, 4);
            try
            {
                var toggle = AddFuryToggle("Clothes/Hat");
                VrcFuryTestHelper.AddAction(toggle, VrcFuryReader.ObjectToggleActionTypeName, a =>
                {
                    VrcFuryTestHelper.SetField(a, "obj", avatar.Hat);
                    VrcFuryTestHelper.SetField(a, "mode", 0);
                });
                var menu = avatar.AddMenu();
                menu.AddEntry(EntryKind.Submenu, "Outfits");
                menu.menuEdits.Add(new MenuEdit
                {
                    id = "e",
                    source = new List<string> { "Clothes", "Hat" },
                    name = "<b>Cap</b>",
                    icon = icon,
                    hide = true,
                    move = true,
                    targetPath = "Outfits",
                });

                var candidate = VrcFuryImport.Build(toggle, avatar.Root.transform);
                var created = ImportApply.Apply(menu, new List<ImportCandidate> { candidate }, true).Single();

                Assert.That(created.name, Is.EqualTo("<b>Cap</b>"));
                Assert.That(created.icon, Is.SameAs(icon));
                var outfits = menu.entries.Single(e => e.kind == EntryKind.Submenu && e.PlainName == "Outfits");
                Assert.That(created.parentId, Is.EqualTo(outfits.id), "the move becomes where the entry lands");
                Assert.That(menu.entries.Any(e => e.kind == EntryKind.Submenu && e.PlainName == "Clothes"), Is.False);
                Assert.That(menu.menuEdits, Is.Empty, "the edit goes with the item it named");
            }
            finally
            {
                Object.DestroyImmediate(icon);
            }
        }

        [Test]
        public void FxCandidateInsideASubmenuLandsInThatSubmenu()
        {
            var clothes = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            clothes.name = "Clothes";
            clothes.controls = new List<VRCExpressionsMenu.Control> { TestAvatar.ToggleControl("Hat", "Hat") };
            try
            {
                avatar.Descriptor.expressionsMenu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = "Clothes",
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = clothes,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = "" },
                });
                var on = AddFxToggleLayer("HatLayer", "Hat", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
                var clip = new AnimationClip { name = "HatOn" };
                SetIsActiveCurve(clip, "Hat", 1f);
                on.motion = clip;
                var menu = avatar.AddMenu();

                var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.parameterName == "Hat");
                Assert.That(candidate.Path, Is.EqualTo(new List<string> { "Clothes" }));
                ImportApply.Apply(menu, new List<ImportCandidate> { candidate }, true);

                var tree = MenuModel.Build(avatar.Root.transform, avatar.Descriptor.expressionsMenu, new[] { menu });
                var plan = ParameterPlanner.Plan(tree, new Dictionary<string, PlannedType>());
                var context = new BuildContext(avatar.Root, null);
                MenuWriter.Write(context, avatar.Descriptor, tree, plan);

                var written = avatar.Descriptor.expressionsMenu.controls.Where(c => RichName.Strip(c.name) == "Clothes").ToList();
                Assert.That(written, Has.Count.EqualTo(1), "one Clothes, not two");
                Assert.That(written[0].subMenu.controls.Select(c => c.name), Is.EqualTo(new[] { "Hat" }), "the converted toggle, with the old control hidden");
                Assert.That(tree.Messages.Any(m => m.Key == "ad.info.same_submenu_merged"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(clothes);
            }
        }

        [Test]
        public void ConvertDoesNotTouchTheEditOfAnExistingControlWithTheSameName()
        {
            AvatarPlanCache.Invalidate();
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Hat", "Existing"));
            var toggle = AddFuryToggle("Hat");
            VrcFuryTestHelper.AddAction(toggle, VrcFuryReader.ObjectToggleActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "obj", avatar.Hat);
                VrcFuryTestHelper.SetField(a, "mode", 0);
            });
            var menu = avatar.AddMenu();
            // The edit names the avatar's own control, which comes first in tree order; the VRCFury row is the second.
            menu.menuEdits.Add(new MenuEdit { id = "e", source = new List<string> { "Hat" }, occurrence = 0, name = "Sunhat" });

            var candidate = VrcFuryImport.Build(toggle, avatar.Root.transform);
            var created = ImportApply.Apply(menu, new List<ImportCandidate> { candidate }, true).Single();

            Assert.That(menu.menuEdits.Select(e => e.id), Is.EqualTo(new[] { "e" }), "the existing control keeps its edit");
            Assert.That(created.name, Is.EqualTo("Hat"), "and the converted entry does not take that rename");
            AvatarPlanCache.Invalidate();
        }

        [Test]
        public void ConvertedLayerIsNotOfferedAgain()
        {
            var on = AddFxToggleLayer("HatLayer", "Hat", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
            var clip = new AnimationClip { name = "HatOn" };
            SetIsActiveCurve(clip, "Hat", 1f);
            on.motion = clip;
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Hat", "Hat"));
            var menu = avatar.AddMenu();

            var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.parameterName == "Hat");
            ImportApply.Apply(menu, new List<ImportCandidate> { candidate }, true);

            var after = MenuModel.Build(avatar.Root.transform, avatar.Descriptor.expressionsMenu, new[] { menu });
            var again = FxLayerImport.Scan(avatar.Root.transform, after).Single(c => c.Draft.parameterName == "Hat");
            Assert.That(again.Supported, Is.False);
            Assert.That(again.Reason, Is.EqualTo(L.Text("ad.import.reason.already_converted")));

            ImportApply.Apply(menu, new List<ImportCandidate> { candidate }, true);
            Assert.That(menu.entries.Count(e => e.kind == EntryKind.Toggle), Is.EqualTo(1), "a second import makes no second entry");
            Assert.That(menu.removedLayers.Count, Is.EqualTo(1));
            Assert.That(menu.menuEdits.Count, Is.EqualTo(1));
        }

        [Test]
        public void DuplicateLayerNamesAreUnsupported()
        {
            var on = AddFxToggleLayer("Toggle", "Hat", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
            var clip = new AnimationClip { name = "HatOn" };
            SetIsActiveCurve(clip, "Hat", 1f);
            on.motion = clip;
            // AnimatorController.AddLayer makes the name unique by itself, so the repeat is renamed in place.
            avatar.Fx.AddLayer("Spare");
            var layers = avatar.Fx.layers;
            layers[layers.Length - 1].name = "Toggle";
            avatar.Fx.layers = layers;
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Hat", "Hat"));

            var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.parameterName == "Hat");

            Assert.That(candidate.Supported, Is.False);
            Assert.That(candidate.Reason, Is.EqualTo(L.Text("ad.import.reason.layer_name_not_unique")));
        }

        [Test]
        public void ImportRecordsTheHideEditAsAPrefabOverride()
        {
            const string folder = GeneratedAssets.Folder;
            const string path = folder + "/ImportOverride.prefab";
            GameObject instance = null;
            try
            {
                var on = AddFxToggleLayer("HatLayer", "Hat", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
                var clip = new AnimationClip { name = "HatOn" };
                SetIsActiveCurve(clip, "Hat", 1f);
                on.motion = clip;
                avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Hat", "Hat"));
                var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.parameterName == "Hat");

                GeneratedAssets.Ensure();
                var source = new GameObject("ImportOverride");
                source.AddComponent<AvatarDoc>();
                var prefab = PrefabUtility.SaveAsPrefabAsset(source, path);
                Object.DestroyImmediate(source);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var menu = instance.GetComponent<AvatarDoc>();

                ImportApply.Apply(menu, new List<ImportCandidate> { candidate }, true);

                var paths = PrefabUtility.GetPropertyModifications(menu).Select(m => m.propertyPath).ToList();
                Assert.That(paths.Any(p => p.StartsWith("menuEdits")), Is.True, "the hide edit is kept as an override");
                Assert.That(paths.Any(p => p.StartsWith("removedLayers")), Is.True, "and so is the layer it left out");
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void RestoreLayerBringsBackTheOldControl()
        {
            var on = AddFxToggleLayer("HatLayer", "Hat", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
            var clip = new AnimationClip { name = "HatOn" };
            SetIsActiveCurve(clip, "Hat", 1f);
            on.motion = clip;
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Hat", "Hat"));
            var menu = avatar.AddMenu();
            var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.parameterName == "Hat");
            ImportApply.Apply(menu, new List<ImportCandidate> { candidate }, true);

            ImportApply.RestoreLayer(menu, "HatLayer");

            Assert.That(menu.removedLayers, Is.Empty);
            Assert.That(menu.menuEdits, Is.Empty);
        }

        [Test]
        public void OutfitSelectorLayerIsUnsupported()
        {
            avatar.Fx.AddParameter("Outfit", AnimatorControllerParameterType.Int);
            avatar.Fx.AddLayer("OutfitLayer");
            var layers = avatar.Fx.layers;
            int index = layers.Length - 1;
            layers[index].defaultWeight = 1f;
            avatar.Fx.layers = layers;
            var sm = avatar.Fx.layers[index].stateMachine;
            var off = sm.AddState("Off");
            var look1 = sm.AddState("Look1");
            var look2 = sm.AddState("Look2");
            sm.defaultState = off;
            var t1 = off.AddTransition(look1);
            t1.hasExitTime = false;
            t1.AddCondition(AnimatorConditionMode.Equals, 1f, "Outfit");
            var t2 = off.AddTransition(look2);
            t2.hasExitTime = false;
            t2.AddCondition(AnimatorConditionMode.Equals, 2f, "Outfit");
            avatar.Descriptor.expressionsMenu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "Outfit", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "Outfit" }, value = 1,
            });

            var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.parameterName == "Outfit");

            Assert.That(candidate.Supported, Is.False);
            Assert.That(candidate.Reason, Is.EqualTo(L.Text("ad.import.reason.shared_layer")));
        }

        [Test]
        public void SelectorBranchIntoASubStateMachineIsUnsupported()
        {
            avatar.Fx.AddParameter("Outfit", AnimatorControllerParameterType.Int);
            avatar.Fx.AddLayer("OutfitLayer");
            var layers = avatar.Fx.layers;
            int index = layers.Length - 1;
            layers[index].defaultWeight = 1f;
            avatar.Fx.layers = layers;
            var sm = avatar.Fx.layers[index].stateMachine;
            var off = sm.AddState("Off");
            var look1 = sm.AddState("Look1");
            var sub = sm.AddStateMachine("Sub");
            sm.defaultState = off;
            var t1 = off.AddTransition(look1);
            t1.hasExitTime = false;
            t1.AddCondition(AnimatorConditionMode.Equals, 1f, "Outfit");
            var t2 = off.AddTransition(sub);
            t2.hasExitTime = false;
            t2.AddCondition(AnimatorConditionMode.Equals, 2f, "Outfit");
            avatar.Descriptor.expressionsMenu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "Outfit", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "Outfit" }, value = 1,
            });

            var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.parameterName == "Outfit");

            Assert.That(candidate.Supported, Is.False);
            Assert.That(candidate.Reason, Is.EqualTo(L.Text("ad.import.reason.shared_layer")));
        }

        [Test]
        public void ParameterUsedByAPuppetControlIsUnsupported()
        {
            AddFxToggleLayer("HatLayer", "Hat", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Hat", "Hat"));
            avatar.Descriptor.expressionsMenu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "Puppet",
                type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "" },
                subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = "Hat" } },
            });

            var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.parameterName == "Hat");

            Assert.That(candidate.Supported, Is.False);
            Assert.That(candidate.Reason, Is.EqualTo(L.Text("ad.import.reason.shared_parameter_controls")));
        }

        [Test]
        public void ParameterSharedByTwoControlsIsUnsupported()
        {
            AddFxToggleLayer("TwoControlsLayer", "TwoControls", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("A", "TwoControls"));
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("B", "TwoControls"));

            var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).First(c => c.Draft.parameterName == "TwoControls");

            Assert.That(candidate.Supported, Is.False);
            Assert.That(candidate.Reason, Is.EqualTo(L.Text("ad.import.reason.shared_parameter_controls")));
        }

        [Test]
        public void LayerWithAStateBehaviourIsUnsupported()
        {
            var on = AddFxToggleLayer("HatLayer", "Hat", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
            on.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            var clip = new AnimationClip { name = "HatOn" };
            SetIsActiveCurve(clip, "Hat", 1f);
            on.motion = clip;
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Hat", "Hat"));

            var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.parameterName == "Hat");

            Assert.That(candidate.Supported, Is.False);
            Assert.That(candidate.Reason, Is.EqualTo(L.Text("ad.import.reason.behaviours")));
        }

        [Test]
        public void LayerControlMakesCandidatesUnsupported()
        {
            var on = AddFxToggleLayer("HatLayer", "Hat", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
            var clip = new AnimationClip { name = "HatOn" };
            SetIsActiveCurve(clip, "Hat", 1f);
            on.motion = clip;
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Hat", "Hat"));

            var idleState = avatar.Fx.layers[0].stateMachine.states[0].state;
            var layerControl = idleState.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
            layerControl.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
            layerControl.layer = 0;

            var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.parameterName == "Hat");

            Assert.That(candidate.Supported, Is.False);
            Assert.That(candidate.Reason, Is.EqualTo(L.Text("ad.import.reason.layer_control")));
        }

        [Test]
        public void MaskedOrWeightlessLayerIsUnsupported()
        {
            var on = AddFxToggleLayer("HatLayer", "Hat", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
            var clip = new AnimationClip { name = "HatOn" };
            SetIsActiveCurve(clip, "Hat", 1f);
            on.motion = clip;
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Hat", "Hat"));

            var mask = new AvatarMask();
            try
            {
                var layers = avatar.Fx.layers;
                layers[layers.Length - 1].avatarMask = mask;
                avatar.Fx.layers = layers;

                var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.parameterName == "Hat");

                Assert.That(candidate.Supported, Is.False);
                Assert.That(candidate.Reason, Is.EqualTo(L.Text("ad.import.reason.layer_masked")));
            }
            finally
            {
                Object.DestroyImmediate(mask);
            }
        }

        [Test]
        public void TransitionChainIsUnsupported()
        {
            var on = AddFxToggleLayer("HatLayer", "Hat", AnimatorControllerParameterType.Bool, AnimatorConditionMode.If, 0f);
            var clip = new AnimationClip { name = "HatOn" };
            SetIsActiveCurve(clip, "Hat", 1f);
            on.motion = clip;
            var sm = avatar.Fx.layers[avatar.Fx.layers.Length - 1].stateMachine;
            var fade = sm.AddState("Fade");
            var chain = on.AddTransition(fade);
            chain.hasExitTime = true;
            chain.exitTime = 1f;
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Hat", "Hat"));

            var candidate = FxLayerImport.Scan(avatar.Root.transform, Tree()).Single(c => c.Draft.parameterName == "Hat");

            Assert.That(candidate.Supported, Is.False);
            Assert.That(candidate.Reason, Is.EqualTo(L.Text("ad.import.reason.transition_chain")));
        }
    }
}
