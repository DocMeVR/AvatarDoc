#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// While the author is typing, the entry panel waits to rebuild, and its fields and lists stay live. If an Undo,
    /// another tool or a removal moves the entry in the component's list in the meantime, the position those
    /// controls were built for holds another entry. Every write has to find its own entry by id when it happens,
    /// and be refused when that entry is gone.
    /// </summary>
    public class EditsKeepTheirEntryTests
    {
        TestAvatar avatar;
        AvatarDoc menuUnderTest;
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
            if (menuUnderTest != null) Undo.ClearUndo(menuUnderTest);
            menuUnderTest = null;
            avatar.Dispose();
            foreach (var extra in extras)
                if (extra != null) Object.DestroyImmediate(extra);
            extras.Clear();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        static AvatarDoctorView View(AvatarDoc menu) => new AvatarDoctorView(new SerializedObject(menu), menu);

        MenuEntry Toggle(AvatarDoc menu, string name, GameObject on)
        {
            var entry = menu.AddEntry(EntryKind.Toggle, name);
            entry.objects.Add(new ObjectTarget { target = on });
            return entry;
        }

        /// <summary>Four toggles; the one called Target sits at index 2, Fourth right after it.</summary>
        (AvatarDoc menu, string first, string target, string fourth) FourToggles()
        {
            var menu = avatar.AddMenu();
            menuUnderTest = menu;
            var first = Toggle(menu, "First", avatar.Hat);
            Toggle(menu, "Second", avatar.Hat);
            var target = Toggle(menu, "Target", avatar.Hat);
            var fourth = Toggle(menu, "Fourth", avatar.Glasses);
            return (menu, first.id, target.id, fourth.id);
        }

        /// <summary>
        /// Lets the panel process the binding requests made while the view was built, which the editor's own loop does
        /// every frame and a test that runs in one go never reaches: a ListView takes its controller from the binding.
        /// </summary>
        static void PumpBindings(VisualElement element)
        {
            var panel = element.panel;
            var method = panel.GetType().GetMethod("UpdateBindings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "no UpdateBindings method found on " + panel.GetType() + " in this Unity version");
            method.Invoke(panel, null);
        }

        static TextField GroupField(AvatarDoctorView view) => view.Detail.Query<TextField>().ToList().First(f => f.label == L.Text("ad.ui.group"));

        static void Remove(AvatarDoctorView view, AvatarDoc menu, string id)
        {
            Assert.That(EntryEditing.Delete(menu, menu.FindEntry(id)), Is.True);
            AvatarPlanCache.Invalidate();
            view.Refresh();
        }

        /// <summary>The panel of the selected row with a typed value waiting in its Group field, so it holds its rebuild back.</summary>
        static TextField HoldThePanel(AvatarDoctorView view, string id)
        {
            view.Tree.SelectEntry(id);
            LayoutProbe.Validate(view);
            var group = GroupField(view);
            group.Focus();
            MenuLifecycleTests.TypeWithoutSaving(group, "Outfit");
            return group;
        }

        [Test]
        public void ATypedFieldSavesOntoItsOwnEntryAfterAnEarlierEntryIsRemoved()
        {
            var (menu, first, target, fourth) = FourToggles();
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                var group = HoldThePanel(view, target);
                Assert.That(menu.entries.IndexOf(menu.FindEntry(target)), Is.EqualTo(2), "the entry sits at index 2 when its field is built");

                Remove(view, menu, first);
                Assert.That(group.panel, Is.Not.Null, "the panel waits for the focus to leave before it rebuilds");
                Assert.That(menu.entries.IndexOf(menu.FindEntry(fourth)), Is.EqualTo(2), "and index 2 now holds another entry");
                view.Tree.Tree.Focus();

                Assert.That(menu.FindEntry(target).exclusiveGroup, Is.EqualTo("Outfit"), "the typed group lands on the entry the field was built for");
                Assert.That(menu.FindEntry(fourth).exclusiveGroup ?? "", Is.Empty, "the entry that took its place keeps its own");
            });
        }

        [Test]
        public void ATypedNameSavesOntoItsOwnEntryAfterAnEarlierEntryIsRemoved()
        {
            var (menu, first, target, fourth) = FourToggles();
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(target);
                LayoutProbe.Validate(view);
                var name = view.Detail.Q<TextField>("ad-name");
                name.Focus();
                MenuLifecycleTests.TypeWithoutSaving(name, "Renamed");

                Remove(view, menu, first);
                Assert.That(name.panel, Is.Not.Null, "the panel waits for the focus to leave before it rebuilds");
                view.Tree.Tree.Focus();

                Assert.That(menu.FindEntry(target).name, Is.EqualTo("Renamed"), "the typed name lands on the entry the field was built for");
                Assert.That(menu.FindEntry(fourth).name, Is.EqualTo("Fourth"), "the entry that took its place keeps its name");
            });
        }

        [Test]
        public void ACustomContactTagSavesOntoItsOwnTriggerAfterAnEarlierEntryIsRemoved()
        {
            var menu = avatar.AddMenu();
            menuUnderTest = menu;
            var spacer = Toggle(menu, "Spacer", avatar.Hat);
            var poke = Contact(menu, "Poke");
            var pat = Contact(menu, "Pat");
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(poke.id);
                LayoutProbe.Validate(view);
                var custom = view.Detail.Q<TextField>("ad-touch-custom-tag");
                custom.Focus();
                MenuLifecycleTests.TypeWithoutSaving(custom, "Boop");

                Remove(view, menu, spacer.id);
                Assert.That(custom.panel, Is.Not.Null, "the panel waits for the focus to leave before it rebuilds");
                view.Tree.Tree.Focus();

                Assert.That(menu.FindEntry(poke.id).contactTags, Does.Contain("Boop"), "the tag lands on the trigger its field was built for");
                Assert.That(menu.FindEntry(pat.id).contactTags, Does.Not.Contain("Boop"), "the trigger that took its place gets no tag");
            });
        }

        MenuEntry Contact(AvatarDoc menu, string name)
        {
            var entry = menu.AddEntry(EntryKind.Trigger, name);
            entry.objects.Add(new ObjectTarget { target = avatar.Glasses });
            entry.source = TriggerSource.Contact;
            entry.contactAnchor = avatar.Hat.transform;
            return entry;
        }

        [Test]
        public void ATypedFieldIsRefusedWhenItsEntryIsGone()
        {
            var (menu, _, target, fourth) = FourToggles();
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                var group = HoldThePanel(view, target);

                Remove(view, menu, target);
                Assert.That(group.panel, Is.Not.Null, "the panel waits for the focus to leave before it rebuilds");
                Assert.DoesNotThrow(() => view.Tree.Tree.Focus());

                Assert.That(menu.FindEntry(target), Is.Null, "the entry is gone");
                Assert.That(menu.entries.Select(e => e.exclusiveGroup ?? ""), Is.All.Empty, "and the typed group went to nobody: not to the entry now at its old place");
                Assert.That(menu.FindEntry(fourth).exclusiveGroup ?? "", Is.Empty);
            });
        }

        [Test]
        public void ATargetListWritesToItsOwnEntryAfterAnEarlierEntryIsRemoved()
        {
            var (menu, first, target, fourth) = FourToggles();
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                HoldThePanel(view, target);
                var objects = view.Detail.Q<TargetListView>("ad-objects");

                Remove(view, menu, first);
                Assert.That(view.Detail.Q<TargetListView>("ad-objects"), Is.SameAs(objects), "the list is the one built before the removal");

                objects.WriteField(0, nameof(ObjectTarget.target), p => p.objectReferenceValue = avatar.Body.gameObject);
                objects.AddObjects(new[] { avatar.Glasses });

                var mine = menu.FindEntry(target);
                var theirs = menu.FindEntry(fourth);
                Assert.That(mine.objects.Select(o => o.target), Is.EqualTo(new Object[] { avatar.Body.gameObject, avatar.Glasses }),
                    "the row edit and the added row both landed on the entry the list was built for");
                Assert.That(theirs.objects.Select(o => o.target), Is.EqualTo(new Object[] { avatar.Glasses }), "the entry that took its place is untouched");
            });
        }

        [Test]
        public void ATargetListRemovesItsOwnRowAfterAnEarlierEntryIsRemoved()
        {
            var (menu, first, target, fourth) = FourToggles();
            menu.FindEntry(target).objects.Add(new ObjectTarget { target = avatar.Glasses });
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                HoldThePanel(view, target);
                var objects = view.Detail.Q<TargetListView>("ad-objects");

                Remove(view, menu, first);
                objects.RemoveAt(0);

                Assert.That(menu.FindEntry(target).objects.Select(o => o.target), Is.EqualTo(new Object[] { avatar.Glasses }), "the first row of its own list went");
                Assert.That(menu.FindEntry(fourth).objects.Count, Is.EqualTo(1), "the other entry kept its row");
            });
        }

        [Test]
        public void ATargetListIsRefusedWhenItsEntryIsGone()
        {
            var (menu, _, target, fourth) = FourToggles();
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                HoldThePanel(view, target);
                var objects = view.Detail.Q<TargetListView>("ad-objects");

                Remove(view, menu, target);
                Assert.DoesNotThrow(() =>
                {
                    objects.WriteField(0, nameof(ObjectTarget.target), p => p.objectReferenceValue = avatar.Body.gameObject);
                    objects.AddObjects(new[] { avatar.Body.gameObject });
                    objects.RemoveAt(0);
                });

                var theirs = menu.FindEntry(fourth);
                Assert.That(theirs.objects.Select(o => o.target), Is.EqualTo(new Object[] { avatar.Glasses }), "nothing reached the entry that took the old place");
            });
        }

        [Test]
        public void ATargetListFollowsItsEntryWhenItWritesAfterAMove()
        {
            var (menu, first, target, _) = FourToggles();
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                HoldThePanel(view, target);
                var objects = view.Detail.Q<TargetListView>("ad-objects");
                var before = objects.BoundPath;

                Remove(view, menu, first);
                objects.AddObjects(new[] { avatar.Glasses });

                Assert.That(objects.BoundPath, Is.Not.EqualTo(before), "after a write the list is bound where its entry sits now");
                Assert.That(objects.BoundPath, Does.StartWith("entries.Array.data[1]."));
                Assert.That(objects.Count, Is.EqualTo(2), "and it counts its own entry's rows");
            });
        }

        [Test]
        public void ANewShapeRowGoesToItsOwnEntryAfterAnEarlierEntryIsRemoved()
        {
            var (menu, first, target, fourth) = FourToggles();
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                HoldThePanel(view, target);
                var shapes = view.Detail.Q<TargetListView>("ad-blendshapes");

                Remove(view, menu, first);
                shapes.AddShape(avatar.Body, "Blush");

                Assert.That(menu.FindEntry(target).blendShapes.Select(b => b.shape), Is.EqualTo(new[] { "Blush" }), "the shape row landed on its own entry");
                Assert.That(menu.FindEntry(fourth).blendShapes, Is.Empty, "and not on the one that took its place");
            });
        }

        /// <summary>Installs three menu assets, A, B and C, and returns the ids of B and C.</summary>
        (string middle, string last) ThreeMenuAssets(AvatarDoctorView view)
        {
            var installs = new List<MenuAssetInstall>();
            foreach (var name in new[] { "A", "B", "C" })
            {
                var asset = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                asset.name = name;
                asset.controls = new List<VRCExpressionsMenu.Control> { TestAvatar.ToggleControl("Item" + name, "Pack/" + name) };
                extras.Add(asset);
                installs.Add(view.Tree.AddMenuAsset(asset));
            }
            return (installs[1].id, installs[2].id);
        }

        [Test]
        public void AMenuAssetPathSavesOntoItsOwnInstallAfterAnEarlierOneIsRemoved()
        {
            var menu = avatar.AddMenu();
            menuUnderTest = menu;
            var view = View(menu);
            var (middle, last) = ThreeMenuAssets(view);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectKey(middle);
                LayoutProbe.Validate(view);
                var path = view.Detail.Q<TextField>("ad-menu-asset-path");
                path.Focus();
                MenuLifecycleTests.TypeWithoutSaving(path, "Somewhere");

                Assert.That(EntryEditing.RemoveMenuAsset(menu, menu.menuAssets[0]), Is.True);
                AvatarPlanCache.Invalidate();
                view.Refresh();
                Assert.That(path.panel, Is.Not.Null, "the panel waits for the focus to leave before it rebuilds");
                view.Tree.Tree.Focus();

                Assert.That(menu.menuAssets.Single(a => a.id == middle).installPath, Is.EqualTo("Somewhere"), "the typed path lands on the install it was built for");
                Assert.That(menu.menuAssets.Single(a => a.id == last).installPath ?? "", Is.Empty, "the install that took its place keeps its own");
            });
        }

        [Test]
        public void AMenuAssetPlaceChoiceWritesToItsOwnInstallAfterAnEarlierOneIsRemoved()
        {
            var menu = avatar.AddMenu();
            menuUnderTest = menu;
            var view = View(menu);
            var (middle, last) = ThreeMenuAssets(view);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectKey(middle);
                LayoutProbe.Validate(view);
                view.Detail.Q<TextField>("ad-menu-asset-path").Focus();
                var place = view.Detail.Q<SegmentedChoice>("ad-menu-asset-place");

                Assert.That(EntryEditing.RemoveMenuAsset(menu, menu.menuAssets[0]), Is.True);
                AvatarPlanCache.Invalidate();
                view.Refresh();
                place.Pick(1);

                Assert.That(menu.menuAssets.Single(a => a.id == middle).asSubmenu, Is.False, "Append lands on the install the choice was built for");
                Assert.That(menu.menuAssets.Single(a => a.id == last).asSubmenu, Is.True, "the install that took its place stays a submenu");
            });
        }

        MenuEntry Preset(AvatarDoc menu, string name, string parameter)
        {
            var preset = menu.AddEntry(EntryKind.Preset, name);
            preset.presetParameters.Add(new PresetParameter { name = parameter, value = 1f });
            return preset;
        }

        [Test]
        public void ATypedPresetParameterSavesOntoItsOwnPresetAfterAnEarlierEntryIsRemoved()
        {
            var menu = avatar.AddMenu();
            menuUnderTest = menu;
            var spacer = Toggle(menu, "Spacer", avatar.Hat);
            var mine = Preset(menu, "Mine", "A");
            var theirs = Preset(menu, "Theirs", "B");
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(mine.id);
                LayoutProbe.Validate(view);
                var name = view.Detail.Q<TextField>("ad-preset-param-name");
                name.Focus();
                MenuLifecycleTests.TypeWithoutSaving(name, "Typed");

                Remove(view, menu, spacer.id);
                Assert.That(name.panel, Is.Not.Null, "the panel waits for the focus to leave before it rebuilds");
                view.Tree.Tree.Focus();

                Assert.That(menu.FindEntry(mine.id).presetParameters[0].name, Is.EqualTo("Typed"), "the typed name lands on the preset its field was built for");
                Assert.That(menu.FindEntry(theirs.id).presetParameters[0].name, Is.EqualTo("B"), "the preset that took its place keeps its own");
            });
        }

        [Test]
        public void ATypedPresetParameterIsRefusedWhenItsPresetIsGone()
        {
            var menu = avatar.AddMenu();
            menuUnderTest = menu;
            var mine = Preset(menu, "Mine", "A");
            var theirs = Preset(menu, "Theirs", "B");
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(mine.id);
                LayoutProbe.Validate(view);
                var name = view.Detail.Q<TextField>("ad-preset-param-name");
                name.Focus();
                MenuLifecycleTests.TypeWithoutSaving(name, "Typed");

                Remove(view, menu, mine.id);
                Assert.DoesNotThrow(() => view.Tree.Tree.Focus());

                Assert.That(menu.FindEntry(theirs.id).presetParameters[0].name, Is.EqualTo("B"), "nothing reached the preset that took the old place");
            });
        }

        [Test]
        public void APresetStateChoiceWritesToItsOwnPresetAfterAnEarlierEntryIsRemoved()
        {
            var menu = avatar.AddMenu();
            menuUnderTest = menu;
            var spacer = Toggle(menu, "Spacer", avatar.Hat);
            var hat = Toggle(menu, "Hat", avatar.Hat);
            var mine = menu.AddEntry(EntryKind.Preset, "Mine");
            mine.presetStates.Add(new PresetState { entryId = hat.id, on = true });
            var theirs = menu.AddEntry(EntryKind.Preset, "Theirs");
            theirs.presetStates.Add(new PresetState { entryId = hat.id, on = true });
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(mine.id);
                LayoutProbe.Validate(view);
                view.Detail.Q<TextField>("ad-name").Focus();
                var onOff = view.Detail.Q<SegmentedChoice>("ad-preset-on");

                Remove(view, menu, spacer.id);
                onOff.Pick(1);

                Assert.That(menu.FindEntry(mine.id).presetStates[0].on, Is.False, "Off lands on the preset the choice was built for");
                Assert.That(menu.FindEntry(theirs.id).presetStates[0].on, Is.True, "the preset that took its place keeps its own");
            });
        }

        MenuEntry ToggleTrigger(AvatarDoc menu, string name, string toggleId)
        {
            var trigger = menu.AddEntry(EntryKind.Trigger, name);
            trigger.objects.Add(new ObjectTarget { target = avatar.Glasses });
            trigger.when.Add(new ConditionGroup { all = { new WhenCondition { kind = WhenConditionKind.Toggle, sourceToggleId = toggleId, sourceToggleOn = true } } });
            return trigger;
        }

        [Test]
        public void AConditionWrittenAfterAnEarlierEntryIsRemovedLandsOnItsOwnTrigger()
        {
            var menu = avatar.AddMenu();
            menuUnderTest = menu;
            var spacer = Toggle(menu, "Spacer", avatar.Hat);
            var hat = Toggle(menu, "Hat", avatar.Hat);
            var boot = Toggle(menu, "Boot", avatar.Glasses);
            var mine = ToggleTrigger(menu, "Mine", hat.id);
            var theirs = ToggleTrigger(menu, "Theirs", hat.id);
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(mine.id);
                LayoutProbe.Validate(view);
                view.Detail.Q<TextField>("ad-name").Focus();
                var pick = view.Detail.Q<DropdownField>("ad-when-toggle");

                Remove(view, menu, spacer.id);
                pick.value = "Boot";

                Assert.That(menu.FindEntry(mine.id).when[0].all[0].sourceToggleId, Is.EqualTo(boot.id), "the toggle lands on the condition of the trigger the dropdown was built for");
                Assert.That(menu.FindEntry(theirs.id).when[0].all[0].sourceToggleId, Is.EqualTo(hat.id), "the trigger that took its place keeps its own");
            });
        }

        [Test]
        public void AConditionSwitchWrittenAfterAnEarlierEntryIsRemovedLandsOnItsOwnTrigger()
        {
            var menu = avatar.AddMenu();
            menuUnderTest = menu;
            var spacer = Toggle(menu, "Spacer", avatar.Hat);
            var hat = Toggle(menu, "Hat", avatar.Hat);
            var mine = ToggleTrigger(menu, "Mine", hat.id);
            var theirs = ToggleTrigger(menu, "Theirs", hat.id);
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(mine.id);
                LayoutProbe.Validate(view);
                view.Detail.Q<TextField>("ad-name").Focus();
                var state = view.Detail.Q<SegmentedChoice>("ad-when-toggle-state");

                Remove(view, menu, spacer.id);
                state.Pick(1);

                Assert.That(menu.FindEntry(mine.id).when[0].all[0].sourceToggleOn, Is.False, "Off lands on the condition of the trigger the switch was built for");
                Assert.That(menu.FindEntry(theirs.id).when[0].all[0].sourceToggleOn, Is.True, "the trigger that took its place keeps its own");
            });
        }

        [Test]
        public void AConditionParameterPickedAfterAnEarlierEntryIsRemovedLandsOnItsOwnTrigger()
        {
            var menu = avatar.AddMenu();
            menuUnderTest = menu;
            var spacer = Toggle(menu, "Spacer", avatar.Hat);
            var mine = ParameterTrigger(menu, "Mine", "A");
            var theirs = ParameterTrigger(menu, "Theirs", "B");
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(mine.id);
                LayoutProbe.Validate(view);
                view.Detail.Q<TextField>("ad-name").Focus();
                var condition = view.EntryProperty(menu.FindEntry(mine.id)).FindPropertyRelative(nameof(MenuEntry.when))
                    .GetArrayElementAtIndex(0).FindPropertyRelative(nameof(ConditionGroup.all)).GetArrayElementAtIndex(0);

                Remove(view, menu, spacer.id);
                view.Detail.SetConditionParameter(condition, "Picked");

                Assert.That(menu.FindEntry(mine.id).when[0].all[0].sourceParameter, Is.EqualTo("Picked"), "the pick lands on the condition of the trigger the picker was built for");
                Assert.That(menu.FindEntry(theirs.id).when[0].all[0].sourceParameter, Is.EqualTo("B"), "the trigger that took its place keeps its own");
            });
        }

        MenuEntry ParameterTrigger(AvatarDoc menu, string name, string parameter)
        {
            var trigger = menu.AddEntry(EntryKind.Trigger, name);
            trigger.objects.Add(new ObjectTarget { target = avatar.Glasses });
            trigger.when.Add(new ConditionGroup { all = { new WhenCondition { kind = WhenConditionKind.Parameter, sourceParameter = parameter } } });
            return trigger;
        }

        [Test]
        public void AConditionOfAnEntryWhoseIndexStartsLikeTheShownOnesIsWrittenWhereItIs()
        {
            var menu = avatar.AddMenu();
            menuUnderTest = menu;
            var hat = Toggle(menu, "Hat", avatar.Hat);
            var boot = Toggle(menu, "Boot", avatar.Glasses);
            var shown = ToggleTrigger(menu, "Shown", hat.id);
            for (int i = 3; i < 25; i++) Toggle(menu, "Filler" + i, avatar.Hat);
            var far = ToggleTrigger(menu, "Far", hat.id);
            Assert.That(menu.entries.IndexOf(shown), Is.EqualTo(2));
            Assert.That(menu.entries.IndexOf(far), Is.EqualTo(25), "the other trigger's path begins with the shown entry's index digits");
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(shown.id);
                LayoutProbe.Validate(view);
                var farCondition = view.EntryProperty(menu.FindEntry(far.id)).FindPropertyRelative(nameof(MenuEntry.when))
                    .GetArrayElementAtIndex(0).FindPropertyRelative(nameof(ConditionGroup.all)).GetArrayElementAtIndex(0);

                view.Detail.SetConditionToggle(farCondition, boot.id);

                Assert.That(menu.FindEntry(far.id).when[0].all[0].sourceToggleId, Is.EqualTo(boot.id), "the condition of entry 25 is written at 25");
                Assert.That(menu.FindEntry(shown.id).when[0].all[0].sourceToggleId, Is.EqualTo(hat.id), "and not moved to the shown entry at 2");
            });
        }

        [Test]
        public void ADraggedRowOfAHeldListReordersItsOwnEntryNotTheOneAtTheOldPlace()
        {
            var (menu, first, target, fourth) = FourToggles();
            menu.FindEntry(target).objects.Add(new ObjectTarget { target = avatar.Glasses });
            menu.FindEntry(fourth).objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                HoldThePanel(view, target);
                var objects = view.Detail.Q<TargetListView>("ad-objects");
                var listView = objects.Q<ListView>();
                PumpBindings(view);
                Assert.That(listView.viewController, Is.Not.Null, "the list is bound to its entry's rows");
                Assert.That(menu.FindEntry(target).objects.Select(o => o.target), Is.EqualTo(new Object[] { avatar.Hat, avatar.Glasses }));
                Assert.That(menu.FindEntry(fourth).objects.Select(o => o.target), Is.EqualTo(new Object[] { avatar.Glasses, avatar.Hat }));

                Remove(view, menu, first);
                Assert.That(view.Detail.Q<TargetListView>("ad-objects"), Is.SameAs(objects), "the list is the one built before the removal");
                listView.viewController.Move(0, 1);

                Assert.That(menu.FindEntry(fourth).objects.Select(o => o.target), Is.EqualTo(new Object[] { avatar.Glasses, avatar.Hat }),
                    "dropping a dragged row does not reorder the entry that took the old place");
                Assert.That(menu.FindEntry(target).objects.Select(o => o.target), Is.EqualTo(new Object[] { avatar.Glasses, avatar.Hat }),
                    "the rows of the entry the list belongs to moved instead");
            });
        }

        [Test]
        public void AHeldListThatStillSitsAtItsEntryKeepsTakingDrags()
        {
            var (menu, _, target, _) = FourToggles();
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                HoldThePanel(view, target);
                var listView = view.Detail.Q<TargetListView>("ad-objects").Q<ListView>();
                PumpBindings(view);
                var controller = listView.viewController;
                Assert.That(controller, Is.Not.Null, "the list is bound to its entry's rows");

                // A change that rebuilds the panel without moving the entry: the panel is held, the list is where its entry is.
                menu.FindEntry(target).properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness" });
                AvatarPlanCache.Invalidate();
                view.Refresh();

                Assert.That(listView.reorderable, Is.True, "a panel that is only held does not stop its lists taking drags");
                Assert.That(listView.viewController, Is.SameAs(controller), "and its lists are left bound as they were");
            });
        }

        [Test]
        public void AHeldListTheAuthorIsWorkingInIsPointedAtItsEntryToo()
        {
            var (menu, first, target, fourth) = FourToggles();
            menu.FindEntry(target).materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 0, material = avatar.MaterialB });
            menu.FindEntry(fourth).materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 1, material = avatar.MaterialA });
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(target);
                LayoutProbe.Validate(view);
                PumpBindings(view);
                LayoutProbe.Validate(view);
                var materials = view.Detail.Q<TargetListView>("ad-materials");
                var listView = materials.Q<ListView>();
                listView.Focus();
                var focused = view.panel.focusController.focusedElement as VisualElement;
                Assert.That(focused != null && (focused == listView || listView.Contains(focused)), Is.True, "the focus is in the rows of the list");

                Remove(view, menu, first);

                Assert.That(materials.BoundPath, Does.StartWith("entries.Array.data[1]."), "the list was pointed at its entry although the author was in it");
                Assert.That(menu.FindEntry(fourth).materials.Count, Is.EqualTo(1));
            });
        }

        [Test]
        public void AHeldListOfAnEntryThatIsGoneIsSwitchedOffAndMovesNothing()
        {
            var (menu, _, target, fourth) = FourToggles();
            menu.FindEntry(target).objects.Add(new ObjectTarget { target = avatar.Glasses });
            menu.FindEntry(fourth).objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                HoldThePanel(view, target);
                var listView = view.Detail.Q<TargetListView>("ad-objects").Q<ListView>();
                PumpBindings(view);
                Assert.That(listView.viewController, Is.Not.Null, "the list is bound to its entry's rows");

                Remove(view, menu, target);

                Assert.That(listView.enabledSelf, Is.False, "the rows of a list whose entry is gone cannot be used");
                Assert.That(listView.viewController, Is.Null, "and the list is no longer bound to the place where another entry sits");
                Assert.That(menu.FindEntry(fourth).objects.Select(o => o.target), Is.EqualTo(new Object[] { avatar.Glasses, avatar.Hat }));
            });
        }

        [Test]
        public void APanelBuiltRightAfterAnEntryWasRemovedElsewhereAnchorsToItsOwnEntry()
        {
            var (menu, first, target, _) = FourToggles();
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                // Another view or a script removes the first entry; this view has not refreshed since.
                Assert.That(EntryEditing.Delete(menu, menu.FindEntry(first)), Is.True);

                view.Tree.SelectEntry(target);
                LayoutProbe.Validate(view);
                GroupField(view).value = "Outfit";

                Assert.That(menu.FindEntry(target).exclusiveGroup, Is.EqualTo("Outfit"), "the group lands on the entry the panel is for");
                Assert.That(menu.entries.Where(e => e.id != target).Select(e => e.exclusiveGroup ?? ""), Is.All.Empty, "and on none of the others");
            });
        }
    }
}
#endif
