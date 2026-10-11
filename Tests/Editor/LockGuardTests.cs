using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Every helper that writes an AvatarDoc's entry data refuses to while the component is locked, not just the buttons that usually call them.</summary>
    public class LockGuardTests
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

        Component AddFuryToggle(string name)
        {
            var go = new GameObject("Fury");
            go.transform.SetParent(avatar.Root.transform);
            return VrcFuryTestHelper.AddToggle(go, name);
        }

        [Test]
        public void LockedComponentRefusesEveryWritePath()
        {
            var menu = avatar.AddMenu();
            var folder = menu.AddEntry(EntryKind.Submenu, "Folder");
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat", folder.id);
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var trigger = menu.AddEntry(EntryKind.Trigger, "Blink");
            trigger.source = TriggerSource.Contact;
            trigger.contactAnchor = avatar.Root.transform;
            trigger.contactTags = new List<string> { "Finger" };
            var shine = menu.AddEntry(EntryKind.Toggle, "Shine");
            shine.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 0.25f });
            var preset = menu.AddEntry(EntryKind.Preset, "Look");
            preset.presetStates.Add(new PresetState { entryId = hat.id, on = true });
            preset.presetParameters.Add(new PresetParameter { name = "Existing", value = 1f });
            var physBone = avatar.Glasses.AddComponent<VRCPhysBone>();
            menu.removedLayers.Add(new RemovedLayer { layer = "SomeLayer", editId = "some-edit" });

            var plan = AvatarPlanCache.GetFor(menu);
            var (_, byId) = TreeRow.Build(plan, menu);
            var oldRow = byId.Values.First(r => r.Kind == RowKind.Existing && r.Name == "Old");

            menu.locked = true;

            int entryCount = menu.entries.Count;
            int editCount = menu.menuEdits.Count;
            int layerCount = menu.removedLayers.Count;

            Assert.That(EntryEditing.Add(menu, EntryKind.Toggle, "New", ""), Is.Null, "EntryEditing.Add");
            Assert.That(EntryEditing.Duplicate(menu, hat), Is.Null, "EntryEditing.Duplicate");
            EntryEditing.Delete(menu, hat);
            Assert.That(EntryEditing.MoveTo(menu, hat, ""), Is.False, "EntryEditing.MoveTo");
            EntryEditing.RenameSubmenu(menu, folder, "Renamed", plan.Tree);

            Assert.That(MenuEditing.MoveTo(menu, oldRow, ""), Is.False, "MenuEditing.MoveTo");
            MenuEditing.SetHidden(menu, oldRow, true);
            MenuEditing.SetName(menu, oldRow, "X");
            MenuEditing.Reset(menu, oldRow);
            Assert.That(MenuEditing.RemoveById(menu, "some-edit"), Is.False, "MenuEditing.RemoveById");

            var candidate = new ImportCandidate { Supported = true, Draft = MenuEntry.Create(EntryKind.Toggle, "Imported"), Path = new List<string>() };
            Assert.That(ImportApply.Apply(menu, new List<ImportCandidate> { candidate }, false), Is.Empty, "ImportApply.Apply");
            ImportApply.RestoreLayer(menu, "SomeLayer");

            var spec = new OutfitSpec { SetName = "Set", Outfits = new List<Outfit> { new Outfit { Name = "A", Pieces = new List<GameObject> { avatar.Hat } } } };
            Assert.That(OutfitSetBuilder.Apply(menu, spec), Is.Empty, "OutfitSetBuilder.Apply");

            Assert.That(IconBatch.CaptureMissing(menu, plan.Tree, plan.AvatarRoot), Is.EqualTo(0), "IconBatch.CaptureMissing");
            var texture = new Texture2D(2, 2);
            try
            {
                IconCapture.Assign(menu, hat, texture);
                Assert.That(hat.icon, Is.Null, "IconCapture.Assign");
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            ContactSceneHandle.Apply(trigger, menu, new Vector3(1f, 2f, 3f), 0.5f);

            // The panel's own setters and the fields it builds, called straight out rather than through the
            // buttons: the lock must hold without the panel's disabled state having anything to do with it.
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            view.Tree.SelectEntry(trigger.id);
            view.Detail.SetTriggerSource(TriggerSource.PhysBone);
            view.Detail.SetTouchMode(TouchMode.FlipToggle);
            view.Detail.SetContactAnchor(avatar.Hat.transform);
            view.Detail.ToggleContactTag("Head", true);
            view.Detail.SetPhysBone(physBone);
            view.Detail.SetContactProximity(0.75f, false);
            view.Detail.SetContactProximity(0.4f, true);
            view.Detail.SetPhysBoneEvent(PhysBoneEvent.Posed);
            view.Detail.SetFlipToggle(hat.id);
            view.Detail.SetSourceToggle(hat.id);
            view.Detail.SetSourceParameter("AFK");
            view.Detail.SetSourceShape(avatar.Body, "Blush");

            Assert.That(trigger.source, Is.EqualTo(TriggerSource.Contact), "SetTriggerSource");
            Assert.That(trigger.touchMode, Is.EqualTo(TouchMode.Hold), "SetTouchMode");
            Assert.That(trigger.contactAnchor, Is.EqualTo(avatar.Root.transform), "SetContactAnchor");
            Assert.That(trigger.contactTags, Is.EqualTo(new List<string> { "Finger" }), "ToggleContactTag");
            Assert.That(trigger.physBone, Is.Null, "SetPhysBone");
            Assert.That(trigger.physBoneEvent, Is.EqualTo(PhysBoneEvent.Grabbed), "SetPhysBoneEvent");
            Assert.That(trigger.contactProximity, Is.EqualTo(0f), "SetContactProximity, from the distance choice and from its slider");
            Assert.That(trigger.flipToggleId, Is.Empty, "SetFlipToggle");
            Assert.That(trigger.sourceToggleId, Is.Empty, "SetSourceToggle");
            Assert.That(trigger.sourceParameter, Is.Empty, "SetSourceParameter");
            Assert.That(trigger.sourceRenderer, Is.Null, "SetSourceShape");

            // A field made by DirectFields writes its SerializedProperty itself, so it asks the lock on its own.
            var serialized = new SerializedObject(menu);
            var fields = new DirectFields(serialized, () => serialized.ApplyModifiedProperties());
            int hatIndex = menu.entries.IndexOf(hat);
            var nameProperty = serialized.FindProperty(nameof(AvatarDoc.entries)).GetArrayElementAtIndex(hatIndex).FindPropertyRelative(nameof(MenuEntry.name));
            fields.Write(nameProperty.propertyPath, p => p.stringValue = "Renamed by a buyer", null);
            fields.Write(nameof(AvatarDoc.parameterPrefix), p => p.stringValue = "Mine", null);

            Assert.That(hat.name, Is.EqualTo("Hat"), "DirectFields.Write refuses a path under entries");
            Assert.That(menu.parameterPrefix, Is.EqualTo("Mine"), "the buyer's own settings stay editable");

            // A target row holds its value instead of binding it, so its write asks the lock too.
            view.Tree.SelectEntry(hat.id);
            var objects = view.Detail.Q<TargetListView>("ad-objects");
            objects.WriteField(0, nameof(ObjectTarget.target), p => p.objectReferenceValue = avatar.Glasses);

            Assert.That(hat.objects[0].target, Is.EqualTo(avatar.Hat), "a target row refuses the write and keeps what the data holds");

            // A property row's value controls write by their own path rather than by row index.
            view.Tree.SelectEntry(shine.id);
            var properties = view.Detail.Q<TargetListView>("ad-properties");
            var valuePath = view.EntryProperty(shine).FindPropertyRelative(nameof(MenuEntry.properties))
                .GetArrayElementAtIndex(0).FindPropertyRelative(nameof(MaterialPropertyTarget.value)).propertyPath;
            properties.WriteValueAt(valuePath, p => p.floatValue = 0.9f, true);

            Assert.That(shine.properties[0].value, Is.EqualTo(0.25f).Within(0.0001f), "TargetListView.WriteValueAt");

            // The preset panel's own setters, which write the preset's states and named parameters directly.
            view.Tree.SelectEntry(preset.id);
            var presetPanel = view.Detail.Q<PresetPanel>();
            presetPanel.AddPresetState(folder.id, true, 1f);
            presetPanel.RemovePresetState(0);
            presetPanel.AddPresetParameter("Another", 1f);
            presetPanel.RemovePresetParameter(0);
            PreviewState.Set(hat.id, 1f);
            presetPanel.FillPresetFromPreview();
            PreviewState.ClearAll();

            Assert.That(preset.presetStates.Count, Is.EqualTo(1), "the preset's states are untouched");
            Assert.That(preset.presetStates[0].entryId, Is.EqualTo(hat.id));
            Assert.That(preset.presetParameters.Count, Is.EqualTo(1), "the preset's named parameters are untouched");
            Assert.That(preset.presetParameters[0].name, Is.EqualTo("Existing"));

            Assert.That(menu.entries.Count, Is.EqualTo(entryCount), "no entry was added or removed");
            Assert.That(menu.menuEdits.Count, Is.EqualTo(editCount), "no menu edit was created");
            Assert.That(menu.removedLayers.Count, Is.EqualTo(layerCount), "no removed layer entry changed");
            Assert.That(hat.name, Is.EqualTo("Hat"));
            Assert.That(hat.parentId, Is.EqualTo(folder.id));
            Assert.That(folder.name, Is.EqualTo("Folder"));
            Assert.That(trigger.contactOffset, Is.EqualTo(Vector3.zero));
            Assert.That(trigger.contactRadius, Is.EqualTo(0.05f).Within(0.0001f));
        }

        /// <summary>
        /// A toggle named Hat, in a group and with a parameter name of its own, shown in a window. The tests below lock
        /// the component after the panel was built, the way the other view of the component or a prefab maker's lock
        /// reaches a panel that is already on screen and enabled: each writer has to ask the lock itself rather than
        /// lean on the panel's disabled state.
        /// </summary>
        void WithAPanelBuiltBeforeTheLock(System.Action<AvatarDoc, AvatarDoctorView> body)
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            hat.exclusiveGroup = "Outfit";
            hat.parameterName = "Mine";
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            LayoutProbe.Measure(view, () =>
            {
                try
                {
                    view.Tree.SelectEntry(hat.id);
                    LayoutProbe.Validate(view);
                    body(menu, view);
                }
                finally
                {
                    foreach (var left in view.panel.visualTree.Query<AdMenu>().ToList()) left.Close(false);
                }
            });
        }

        [Test]
        public void LockedComponentRefusesTheGroupMenu()
        {
            WithAPanelBuiltBeforeTheLock((menu, view) =>
            {
                var group = view.Detail.Query<TextField>().ToList().First(f => f.label == L.Text("ad.ui.group"));
                UiTestEvents.SimulateClick(group.parent.Q<Button>());
                var open = view.panel.visualTree.Q<AdMenu>();
                Assert.That(open, Is.Not.Null, "the Group picker opened its menu");

                menu.locked = true;
                open.Commit(0);

                Assert.That(menu.entries.Single().exclusiveGroup, Is.EqualTo("Outfit"), "None from the Group menu changed nothing");
            });
        }

        [Test]
        public void LockedComponentRefusesTheNameField()
        {
            WithAPanelBuiltBeforeTheLock((menu, view) =>
            {
                menu.locked = true;
                view.Detail.Q<NameStyleRow>().SetPlain("Renamed by a buyer");

                Assert.That(menu.entries.Single().name, Is.EqualTo("Hat"));
            });
        }

        [Test]
        public void LockedComponentRefusesTheCustomNameSwitchTurnedOff()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            hat.parameterName = "Mine";
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(hat.id);
                LayoutProbe.Validate(view);
                var custom = view.Detail.Q<Toggle>("ad-parameter-custom");
                Assert.That(custom.value, Is.True, "Hat has a parameter name of its own");

                menu.locked = true;
                custom.value = false;

                Assert.That(menu.entries.Single().parameterName, Is.EqualTo("Mine"));
                Assert.That(custom.value, Is.True, "and the switch shows what the data holds");
            });
        }

        [Test]
        public void LockedComponentRefusesTheCustomNameSwitchTurnedOn()
        {
            var menu = avatar.AddMenu();
            var boot = menu.AddEntry(EntryKind.Toggle, "Boot");
            boot.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(boot.id);
                LayoutProbe.Validate(view);
                var custom = view.Detail.Q<Toggle>("ad-parameter-custom");
                Assert.That(custom.value, Is.False);

                menu.locked = true;
                custom.value = true;

                Assert.That(menu.FindEntry(boot.id).parameterName ?? "", Is.Empty);
                Assert.That(custom.value, Is.False, "and the switch shows what the data holds");
            });
        }

        [Test]
        public void OpenImportWindowRefusesAfterLocking()
        {
            Assume.That(VrcFuryTestHelper.VrcFuryType, Is.Not.Null);
            var menu = avatar.AddMenu();
            var toggle = AddFuryToggle("Hat");
            VrcFuryTestHelper.AddAction(toggle, VrcFuryReader.ObjectToggleActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "obj", avatar.Hat);
                VrcFuryTestHelper.SetField(a, "mode", 0);
            });
            var window = ScriptableObject.CreateInstance<ImportWindow>();
            try
            {
                window.Init(menu);
                var index = window.Candidates.ToList().FindIndex(c => c.Source == ImportSource.VRCFury);
                Assert.That(index, Is.GreaterThanOrEqualTo(0));
                window.SetChecked(index, true);
                menu.locked = true;

                var created = window.RunImport();

                Assert.That(created, Is.Empty);
                Assert.That(menu.entries, Is.Empty, "the locked component refused the import");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void OpenOutfitWindowRefusesAfterLocking()
        {
            var menu = avatar.AddMenu();
            var window = ScriptableObject.CreateInstance<OutfitSetWindow>();
            try
            {
                window.Init(menu);
                window.SetName("Set");
                window.AddOutfit("Outfit A");
                window.AddPiece(0, avatar.Hat);
                menu.locked = true;

                var created = window.Create();

                Assert.That(created, Is.Empty);
                Assert.That(menu.entries, Is.Empty, "the locked component refused outfit creation");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void SceneHandleDoesNotWriteWhileLocked()
        {
            var menu = avatar.AddMenu();
            var trigger = menu.AddEntry(EntryKind.Trigger, "Blink");
            trigger.source = TriggerSource.Contact;
            trigger.contactAnchor = avatar.Root.transform;
            trigger.contactTags = new List<string> { "Finger" };
            menu.locked = true;

            ContactSceneHandle.Apply(trigger, menu, new Vector3(1f, 2f, 3f), 0.5f);

            Assert.That(trigger.contactOffset, Is.EqualTo(Vector3.zero));
            Assert.That(trigger.contactRadius, Is.EqualTo(0.05f).Within(0.0001f));
        }

        [Test]
        public void LockedComponentRefusesAnimatorWrites()
        {
            var menu = avatar.AddMenu();
            var controller = new AnimatorController { name = "Merged" };
            try
            {
                var merge = EntryEditing.AddAnimator(menu, controller);
                Assert.That(merge, Is.Not.Null, "an unlocked component still takes one");
                merge.skipLayers.Add("Kept");

                menu.locked = true;

                Assert.That(EntryEditing.AddAnimator(menu, controller), Is.Null, "EntryEditing.AddAnimator");
                Assert.That(EntryEditing.RemoveAnimator(menu, merge), Is.False, "EntryEditing.RemoveAnimator");
                EntryEditing.SetLayerSkipped(menu, merge, "Kept", false);

                var serialized = new SerializedObject(menu);
                var fields = new DirectFields(serialized, () => serialized.ApplyModifiedProperties());
                var property = serialized.FindProperty(nameof(AvatarDoc.animators)).GetArrayElementAtIndex(0)
                    .FindPropertyRelative(nameof(AnimatorMerge.matchWriteDefaults));
                fields.Write(property.propertyPath, p => p.boolValue = false, null);

                Assert.That(menu.animators.Count, Is.EqualTo(1), "no merged animator was added or removed");
                Assert.That(menu.animators[0].skipLayers, Is.EqualTo(new List<string> { "Kept" }), "EntryEditing.SetLayerSkipped");
                Assert.That(menu.animators[0].matchWriteDefaults, Is.True, "DirectFields.Write refuses a path under animators");
            }
            finally
            {
                Object.DestroyImmediate(controller);
            }
        }

        [Test]
        public void LockedComponentRefusesGateWrites()
        {
            var menu = avatar.AddMenu();
            var trigger = menu.AddEntry(EntryKind.Trigger, "Ears");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "AFK";
            var gate = menu.AddEntry(EntryKind.Toggle, "Enabled");

            menu.locked = true;

            Assert.That(EntryEditing.AddGateSwitch(menu, trigger), Is.Null, "EntryEditing.AddGateSwitch");
            Assert.That(menu.entries.Count, Is.EqualTo(2), "no toggle was added while locked");

            var serialized = new SerializedObject(menu);
            var fields = new DirectFields(serialized, () => serialized.ApplyModifiedProperties());
            var triggerIndex = menu.entries.IndexOf(trigger);
            var entryProperty = serialized.FindProperty(nameof(AvatarDoc.entries)).GetArrayElementAtIndex(triggerIndex);
            fields.Write(entryProperty.FindPropertyRelative(nameof(MenuEntry.gateToggleId)).propertyPath, p => p.stringValue = gate.id, null);
            fields.Write(entryProperty.FindPropertyRelative(nameof(MenuEntry.gateToggleOn)).propertyPath, p => p.boolValue = false, null);

            Assert.That(trigger.gateToggleId, Is.Empty, "DirectFields.Write refuses a path under entries (gateToggleId)");
            Assert.That(trigger.gateToggleOn, Is.True, "DirectFields.Write refuses a path under entries (gateToggleOn)");
        }
    }
}
