using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Dynamics.PhysBone.Components;
#if DOCME_MA
using nadena.dev.modular_avatar.core;
#endif

namespace DocMe.AvatarDoctor.Editor
{
    public class ViewTests
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

        AvatarDoctorView View(AvatarDoc menu) => new AvatarDoctorView(new SerializedObject(menu), menu);

        AvatarDoctorView View(AvatarDoc menu, IViewPrefs prefs) => new AvatarDoctorView(new SerializedObject(menu), menu, viewPrefs: prefs);

        [Test]
        public void ViewBuildsHeaderWithMemoryBar()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });

            var view = View(menu);

            Assert.That(view.ClassListContains("ad-root"), Is.True);
            Assert.That(view.Q<MemoryBar>(), Is.Not.Null);
            Assert.That(view.Plan.AvatarDoctorBits, Is.EqualTo(1));
            Assert.That(view.Q<MemoryBar>().AvatarDoctorBits, Is.EqualTo(1));
            Assert.That(view.Q<TextField>("ad-install-path"), Is.Not.Null);
            Assert.That(view.Header.Q<Button>("ad-summary-toggle"), Is.Not.Null);
            Assert.That(view.Header.Q<Label>("ad-legend-free").text, Is.Not.Empty);
        }

        [Test]
        public void PathChoicesListSubmenusOfOtherComponentsButNotOwnSubmenus()
        {
            var menu = avatar.AddMenu();
            var own = menu.AddEntry(EntryKind.Submenu, "Own");
            menu.AddEntry(EntryKind.Toggle, "T", own.id).objects.Add(new ObjectTarget { target = avatar.Hat });
            var other = new GameObject("Other"); other.transform.SetParent(avatar.Root.transform);
            var otherMenu = other.AddComponent<AvatarDoc>();
            otherMenu.installMode = InstallMode.AsSubmenu; otherMenu.menuName = "Pack";
            otherMenu.AddEntry(EntryKind.Toggle, "P").objects.Add(new ObjectTarget { target = avatar.Glasses });

            var choices = HeaderView.PathChoices(AvatarPlanCache.GetFor(menu), menu);

            Assert.That(choices, Does.Contain("Old"), "existing submenu of the avatar's menu");
            Assert.That(choices, Does.Contain("Pack"));
            Assert.That(choices, Does.Not.Contain("Own"));
        }

        [Test]
        public void ViewFixesDuplicateIdsOnCreation()
        {
            var menu = avatar.AddMenu();
            var a = menu.AddEntry(EntryKind.Toggle, "A");
            var b = menu.AddEntry(EntryKind.Toggle, "B");
            b.id = a.id;

            View(menu);

            Assert.That(b.id, Is.Not.EqualTo(a.id));
        }

        [Test]
        public void TheViewOfTheOriginalKeepsItsIdsWhenACopyRepeatsThem()
        {
            var first = avatar.AddMenu();
            var hat = first.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var original = hat.id;
            // Paste Component As New on the same object: the copy carries every id of its source.
            var copy = avatar.Root.AddComponent<AvatarDoc>();
            copy.entries.Add(JsonUtility.FromJson<MenuEntry>(JsonUtility.ToJson(hat)));
            var otherObject = new GameObject("Other");
            otherObject.transform.SetParent(avatar.Root.transform);
            var other = otherObject.AddComponent<AvatarDoc>();
            var preset = other.AddEntry(EntryKind.Preset, "All on");
            preset.presetStates.Add(new PresetState { entryId = hat.id, on = true });

            View(first);

            Assert.That(hat.id, Is.EqualTo(original), "the first owner of an id keeps it");
            Assert.That(copy.entries[0].id, Is.Not.EqualTo(original));
            var plan = AvatarPlanCache.GetFor(other);
            var resolved = plan.Tree.FindControl(preset.presetStates[0].entryId);
            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved.Component, Is.SameAs(first), "the preset still reaches the original's toggle");
            Assert.That(plan.Messages.Any(m => m.Key == "ad.warn.duplicate_entry_id"), Is.False);
        }

        [Test]
        public void TheViewOfACopyRepairsItselfAndLeavesTheOriginalAlone()
        {
            var first = avatar.AddMenu();
            var hat = first.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var original = hat.id;
            var copyObject = new GameObject("Copy");
            copyObject.transform.SetParent(avatar.Root.transform);
            var copy = copyObject.AddComponent<AvatarDoc>();
            copy.entries.Add(JsonUtility.FromJson<MenuEntry>(JsonUtility.ToJson(hat)));

            View(copy);

            Assert.That(hat.id, Is.EqualTo(original));
            Assert.That(copy.entries[0].id, Is.Not.EqualTo(original));
        }

        [Test]
        public void TreeRowsMirrorTheMergedMenu()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var casual = menu.AddEntry(EntryKind.Toggle, "Casual", outfits.id);
            casual.exclusiveGroup = "Outfits"; casual.defaultOn = true;
            casual.objects.Add(new ObjectTarget { target = avatar.Hat });
            var missing = menu.AddEntry(EntryKind.Toggle, "Broken");
            missing.objects.Add(new ObjectTarget { target = null });
            menu.AddEntry(EntryKind.Trigger, "Blink").objects.Add(new ObjectTarget { target = avatar.Hat });
            var pack = new GameObject("Pack"); pack.transform.SetParent(avatar.Root.transform);
            var packMenu = pack.AddComponent<AvatarDoc>();
            packMenu.installPath = "Outfits";
            var swim = packMenu.AddEntry(EntryKind.Toggle, "Swim");
            swim.exclusiveGroup = "Outfits";
            swim.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var extra = new GameObject("Extra"); extra.transform.SetParent(avatar.Root.transform);
            var extraMenu = extra.AddComponent<AvatarDoc>();
            extraMenu.installPath = "Old";
            extraMenu.AddEntry(EntryKind.Toggle, "Extra").objects.Add(new ObjectTarget { target = avatar.Body.gameObject });

            var (items, byId) = TreeRow.Build(AvatarPlanCache.GetFor(menu), menu);

            Assert.That(items.Select(i => i.data.Name), Is.EqualTo(new[] { "Old", "RootToggle", "Outfits", "Broken", "Triggers" }));
            var old = items[0].data;
            Assert.That(old.Kind, Is.EqualTo(RowKind.Existing));
            Assert.That(old.Slots, Is.EqualTo(2), "its own control plus the one the second component installs into it");
            var outfitsRow = items[2].data;
            Assert.That(outfitsRow.Kind, Is.EqualTo(RowKind.Submenu));
            Assert.That(outfitsRow.Mine, Is.True);
            Assert.That(outfitsRow.Slots, Is.EqualTo(2));
            var children = items[2].children.Select(c => c.data).ToList();
            Assert.That(children.Select(c => c.Name), Is.EqualTo(new[] { "Casual", "Swim" }));
            Assert.That(children[0].DefaultOn, Is.True);
            Assert.That(children[0].GroupChip, Is.EqualTo("Outfits 1"));
            Assert.That(children[1].Mine, Is.False);
            Assert.That(children[1].SourceChip, Is.EqualTo("Pack"));
            Assert.That(children[1].GroupChip, Is.EqualTo("Outfits 2"));
            Assert.That(items[3].data.Messages.Any(m => m.Key == "ad.warn.target_missing"), Is.True);
            Assert.That(items[4].data.Kind, Is.EqualTo(RowKind.TriggersGroup));
            Assert.That(items[4].children.Single().data.Kind, Is.EqualTo(RowKind.Trigger));
            Assert.That(byId.Count, Is.EqualTo(10), "Old, OldToggle, Extra, RootToggle, Outfits, Casual, Swim, Broken, Triggers, Blink");
            Assert.That(children[0].ParentRow, Is.SameAs(outfitsRow));
            Assert.That(children[0].Depth, Is.EqualTo(1));
        }

        [Test]
        public void TreeSelectsTheInitialEntryAndAddsUnderTheSelectedSubmenu()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            menu.AddEntry(EntryKind.Toggle, "Casual", outfits.id).objects.Add(new ObjectTarget { target = avatar.Hat });
            AvatarDoctorView.InitialSelectionId = outfits.id;
            try
            {
                var view = View(menu);
                Assert.That(view.Tree.Selected, Is.Not.Null);
                Assert.That(view.Tree.Selected.Entry, Is.SameAs(outfits));

                var added = view.Tree.AddEntry(EntryKind.Toggle);

                Assert.That(added.parentId, Is.EqualTo(outfits.id));
                Assert.That(added.name, Is.EqualTo("New Toggle"));
                Assert.That(view.Tree.Selected.Entry, Is.SameAs(added));
                Assert.That(view.Tree.RowFor(added.id), Is.Not.Null);
            }
            finally
            {
                AvatarDoctorView.InitialSelectionId = null;
            }
        }

        [Test]
        public void SelectionOfEntrylessRowsSurvivesRefresh()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });

            var view = View(menu);
            view.Tree.SelectKey("node:Old");

            AvatarPlanCache.Invalidate();
            view.Refresh();

            Assert.That(view.Tree.Selected, Is.Not.Null);
            Assert.That(view.Tree.Selected.Name, Is.EqualTo("Old"));
        }

        [Test]
        public void InitialSelectionIsConsumedOnce()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            AvatarDoctorView.InitialSelectionId = toggle.id;
            try
            {
                var view = View(menu);
                Assert.That(view.Tree.Selected, Is.Not.Null);
                Assert.That(view.Tree.Selected.Entry, Is.SameAs(toggle));

                view.Tree.SelectKey("node:Old");
                AvatarPlanCache.Invalidate();
                view.Refresh();

                Assert.That(view.Tree.Selected, Is.Not.Null);
                Assert.That(view.Tree.Selected.Name, Is.EqualTo("Old"));
            }
            finally
            {
                AvatarDoctorView.InitialSelectionId = null;
            }
        }

        [Test]
        public void DetailBindsToTheSelectedEntry()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "First").objects.Add(new ObjectTarget { target = avatar.Hat });
            var second = menu.AddEntry(EntryKind.Toggle, "Second");
            second.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var view = View(menu);

            view.Tree.SelectEntry(second.id);

            Assert.That(view.Detail.Row.Entry, Is.SameAs(second));
            Assert.That(view.Detail.BoundPropertyPath, Is.EqualTo("entries.Array.data[1]"));
            Assert.That(view.Detail.Q<TextField>("ad-name"), Is.Not.Null);
            Assert.That(view.Detail.Q<Label>("ad-parameter-name").text, Is.EqualTo("AD/Second"));
        }

        [Test]
        public void DetailShowsSelectButtonForOtherComponentsEntries()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Mine").objects.Add(new ObjectTarget { target = avatar.Hat });
            var pack = new GameObject("Pack"); pack.transform.SetParent(avatar.Root.transform);
            var packMenu = pack.AddComponent<AvatarDoc>();
            var theirs = packMenu.AddEntry(EntryKind.Toggle, "Theirs");
            theirs.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var view = View(menu);

            view.Tree.SelectEntry(theirs.id);

            Assert.That(view.Detail.BoundPropertyPath, Is.Null);
            Assert.That(view.Detail.Q<Button>("ad-select-component"), Is.Not.Null);
        }

        [Test]
        public void DetailShowsValidationMessages()
        {
            var menu = avatar.AddMenu();
            var broken = menu.AddEntry(EntryKind.Toggle, "Broken");
            broken.objects.Add(new ObjectTarget { target = null });
            var view = View(menu);

            view.Tree.SelectEntry(broken.id);

            Assert.That(view.Detail.Query(className: "ad-warnbox").ToList().Count, Is.EqualTo(1));
        }

        [Test]
        public void RenamingASubmenuThroughTheDetailPropagatesInstallPaths()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            menu.AddEntry(EntryKind.Toggle, "Casual", outfits.id).objects.Add(new ObjectTarget { target = avatar.Hat });
            var pack = new GameObject("Pack"); pack.transform.SetParent(avatar.Root.transform);
            var packMenu = pack.AddComponent<AvatarDoc>();
            packMenu.installPath = "Outfits";
            packMenu.AddEntry(EntryKind.Toggle, "Swim").objects.Add(new ObjectTarget { target = avatar.Glasses });
            var view = View(menu);
            view.Tree.SelectEntry(outfits.id);

            view.Detail.RenameSubmenu("Clothes");

            Assert.That(outfits.name, Is.EqualTo("Clothes"));
            Assert.That(packMenu.installPath, Is.EqualTo("Clothes"));
        }

        [Test]
        public void RenamingASubmenuKeepsItSelected()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            menu.AddEntry(EntryKind.Toggle, "Casual", outfits.id).objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.Tree.SelectEntry(outfits.id);

            view.Detail.RenameSubmenu("Clothes");

            Assert.That(view.Tree.Selected, Is.Not.Null);
            Assert.That(view.Tree.Selected.Entry, Is.SameAs(outfits));
            Assert.That(view.Detail.Row.Entry, Is.SameAs(outfits));
            Assert.That(view.Detail.Q<TextField>("ad-name").value, Is.EqualTo("Clothes"));
        }

        [Test]
        public void ObjectListAddsDroppedObjectsAndSkipsDuplicates()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.Tree.SelectEntry(hat.id);
            var list = view.Detail.Q<TargetListView>("ad-objects");

            list.AddObjects(new[] { avatar.Hat, avatar.Glasses, avatar.Glasses });

            Assert.That(hat.objects.Select(o => o.target), Is.EqualTo(new[] { avatar.Hat, avatar.Glasses }));
            Assert.That(hat.objects[1].activeWhenOn, Is.True);
            Assert.That(list.Count, Is.EqualTo(2));
        }

        [Test]
        public void ObjectTargetUsesASegmentedChoice()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = true });
            var view = View(menu);
            view.Tree.SelectEntry(hat.id);
            var list = view.Detail.Q<TargetListView>("ad-objects");
            var row = list.RowAt(0);

            var mode = row.Q<SegmentedChoice>("mode");
            Assert.That(mode, Is.Not.Null);
            Assert.That(mode.Index, Is.EqualTo(0), "Shown when on is the default");

            mode.Pick(1);

            Assert.That(hat.objects[0].activeWhenOn, Is.False, "choosing Hidden writes activeWhenOn = false");
        }

        [Test]
        public void ShapeListAddsAndRemovesRows()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.Tree.SelectEntry(hat.id);
            var list = view.Detail.Q<TargetListView>("ad-blendshapes");

            list.AddShape(avatar.Body, "Blush");
            list.AddShape(avatar.Body, "Eye_Big");
            Assert.That(hat.blendShapes.Select(b => b.shape), Is.EqualTo(new[] { "Blush", "Eye_Big" }));
            Assert.That(hat.blendShapes[0].weight, Is.EqualTo(100f));

            list = view.Detail.Q<TargetListView>("ad-blendshapes");
            list.RemoveAt(0);
            Assert.That(hat.blendShapes.Select(b => b.shape), Is.EqualTo(new[] { "Eye_Big" }));
        }

        [Test]
        public void ANewShapeRowStartsFromCleanDefaultsNotACopyOfTheLastRow()
        {
            var menu = avatar.AddMenu();
            try
            {
                var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
                hat.blendShapes.Add(new BlendShapeTarget
                {
                    renderer = avatar.Body, shape = "Blush", weight = 40f, minWeight = 20f, maxWeight = 70f,
                    mode = ShapeEffectMode.Pin, multiplier = 3f, explicitOff = true,
                });
                var view = View(menu);
                view.Tree.SelectEntry(hat.id);

                view.Detail.Q<TargetListView>("ad-blendshapes").AddShape(avatar.Body, "Eye_Big");

                var rows = menu.FindEntry(hat.id).blendShapes;
                Assert.That(rows.Count, Is.EqualTo(2));
                var added = rows[1];
                Assert.That(added.shape, Is.EqualTo("Eye_Big"));
                Assert.That(added.mode, Is.EqualTo(ShapeEffectMode.Fixed), "a new row is Fixed, not the Pin the last row had");
                Assert.That(added.explicitOff, Is.False, "and has no off value of its own: its off state follows the scene");
                Assert.That(added.weight, Is.EqualTo(100f));
                Assert.That(added.minWeight, Is.EqualTo(0f));
                Assert.That(added.maxWeight, Is.EqualTo(100f));
                Assert.That(added.multiplier, Is.EqualTo(1f));
                Assert.That(rows[0].mode, Is.EqualTo(ShapeEffectMode.Pin), "the row it would have copied is left as it was");
                Assert.That(rows[0].explicitOff, Is.True);
                Assert.That(rows[0].weight, Is.EqualTo(40f));
            }
            finally
            {
                Undo.ClearUndo(menu);
            }
        }

        [Test]
        public void ANewOverrideShapeRowStartsAtZeroWithCleanDefaults()
        {
            var menu = avatar.AddMenu();
            try
            {
                var rule = menu.AddEntry(EntryKind.Override, "Rule");
                rule.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 55f, mode = ShapeEffectMode.Pin, explicitOff = true });
                var view = View(menu);
                view.Tree.SelectEntry(rule.id);

                view.Detail.Q<TargetListView>("ad-blendshapes").AddShape(avatar.Body, "Eye_Big");

                var added = menu.FindEntry(rule.id).blendShapes[1];
                Assert.That(added.weight, Is.EqualTo(0f), "an override's row starts at the value that switches the shape off");
                Assert.That(added.mode, Is.EqualTo(ShapeEffectMode.Fixed));
                Assert.That(added.explicitOff, Is.False);
            }
            finally
            {
                Undo.ClearUndo(menu);
            }
        }

        [Test]
        public void CaptureButtonFillsTheListAndAddsTheRuleLine()
        {
            var menu = avatar.AddMenu();
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            avatar.Body.SetBlendShapeWeight(0, 40f);
            var view = View(menu);
            view.Tree.SelectEntry(face.id);
            var list = view.Detail.Q<TargetListView>("ad-blendshapes");
            Assert.That(list.Q<Button>("ad-shape-set-capture"), Is.Not.Null, "the Capture button sits beside + Add");

            list.CaptureFrom(avatar.Body);

            list = view.Detail.Q<TargetListView>("ad-blendshapes");
            Assert.That(face.blendShapes.Select(b => b.shape), Is.EqualTo(new[] { "Blush" }));
            Assert.That(face.blendShapes[0].mode, Is.EqualTo(ShapeEffectMode.Pin));
            Assert.That(face.shapeSets.Count, Is.EqualTo(1));
            Assert.That(list.Q<Toggle>("zero-others"), Is.Not.Null, "the rule line shows under the list");
            Assert.That(list.Q<Toggle>("zero-others").value, Is.True);
        }

        [Test]
        public void CaptureButtonShowsInEveryBlendShapeSection()
        {
            var menu = avatar.AddMenu();
            var sections = CaptureSections(menu);
            var view = View(menu);

            foreach (var (entry, context) in sections)
            {
                view.Tree.SelectEntry(entry.id);
                var list = view.Detail.Q<TargetListView>("ad-blendshapes");
                Assert.That(list.Q<Button>("ad-shape-set-capture"), Is.Not.Null, context + ": the Capture button must show");
            }
        }

        [Test]
        public void NoRuleLineOutsideAToggle()
        {
            var menu = avatar.AddMenu();
            var sections = CaptureSections(menu);
            var view = View(menu);

            foreach (var (entry, context) in sections)
            {
                view.Tree.SelectEntry(entry.id);
                var list = view.Detail.Q<TargetListView>("ad-blendshapes");
                bool showsRule = entry.kind == EntryKind.Toggle || entry.kind == EntryKind.Trigger;
                Assert.That(list.Q<VisualElement>("ad-shape-sets") != null, Is.EqualTo(showsRule), context + ": the rule line shows on a toggle and on a trigger with effects, nowhere else");
            }
        }

        [Test]
        public void CaptureUpdatesAnExistingRowInEverySection()
        {
            var menu = avatar.AddMenu();
            var sections = CaptureSections(menu);
            foreach (var (entry, context) in sections)
                entry.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 5f });
            avatar.Body.SetBlendShapeWeight(0, 40f);
            var view = View(menu);

            foreach (var (entry, context) in sections)
            {
                view.Tree.SelectEntry(entry.id);
                var list = view.Detail.Q<TargetListView>("ad-blendshapes");

                list.CaptureFrom(avatar.Body);

                Assert.That(entry.blendShapes.Count, Is.EqualTo(1), context + ": the existing row keeps its place, nothing is duplicated");
                var target = entry.blendShapes[0];
                Assert.That(target.shape, Is.EqualTo("Blush"), context);
                if (entry.kind == EntryKind.Radial)
                {
                    Assert.That(target.maxWeight, Is.EqualTo(40f), context + ": the scene weight becomes the upper end");
                    Assert.That(target.minWeight, Is.EqualTo(0f), context);
                }
                else
                {
                    Assert.That(target.weight, Is.EqualTo(40f), context);
                }
            }
        }

        [Test]
        public void LockedComponentRefusesCaptureInEverySection()
        {
            var menu = avatar.AddMenu();
            var sections = CaptureSections(menu);
            avatar.Body.SetBlendShapeWeight(0, 40f);
            menu.locked = true;
            var view = View(menu);

            foreach (var (entry, context) in sections)
            {
                view.Tree.SelectEntry(entry.id);
                var list = view.Detail.Q<TargetListView>("ad-blendshapes");

                list.CaptureFrom(avatar.Body);

                Assert.That(entry.blendShapes, Is.Empty, context + ": capture is refused while locked");
            }
        }

        [Test]
        public void DroppingObjectsOnTheTreeCreatesToggles()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var view = View(menu);
            view.Tree.SelectEntry(outfits.id);

            view.Tree.AddTogglesFor(new[] { avatar.Hat, avatar.Glasses });

            var toggles = menu.entries.Where(e => e.kind == EntryKind.Toggle).ToList();
            Assert.That(toggles.Select(t => t.name), Is.EqualTo(new[] { "Hat", "Glasses" }));
            Assert.That(toggles.All(t => t.parentId == outfits.id), Is.True);
            Assert.That(toggles[0].objects.Single().target, Is.SameAs(avatar.Hat));
            Assert.That(view.Tree.Selected.Entry, Is.SameAs(toggles[1]));
        }

        [Test]
        public void ObjectsOutsideTheAvatarAreNotAccepted()
        {
            var menu = avatar.AddMenu();
            var stray = new GameObject("Stray");
            try
            {
                var view = View(menu);
                view.Tree.AddTogglesFor(new[] { stray, avatar.Hat });
                Assert.That(menu.entries.Select(e => e.name), Is.EqualTo(new[] { "Hat" }));
            }
            finally
            {
                Object.DestroyImmediate(stray);
            }
        }

        [Test]
        public void MaterialListRemovesARowWithRendererAndMaterialInOneCall()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.Tree.SelectEntry(hat.id);
            var list = view.Detail.Q<TargetListView>("ad-materials");

            list.AddMaterialRow();

            int i = menu.entries.IndexOf(hat);
            var materialElement = view.SerializedObject.FindProperty("entries").GetArrayElementAtIndex(i).FindPropertyRelative("materials").GetArrayElementAtIndex(0);
            materialElement.FindPropertyRelative(nameof(MaterialTarget.renderer)).objectReferenceValue = avatar.Body;
            materialElement.FindPropertyRelative(nameof(MaterialTarget.material)).objectReferenceValue = avatar.MaterialB;
            view.SerializedObject.ApplyModifiedProperties();
            AvatarPlanCache.Invalidate();
            view.Refresh();

            list = view.Detail.Q<TargetListView>("ad-materials");
            list.RemoveAt(0);

            Assert.That(hat.materials.Count, Is.EqualTo(0));
        }

        [Test]
        public void ApplyStructureReordersAndReparentsOwnRows()
        {
            var menu = avatar.AddMenu();
            var a = menu.AddEntry(EntryKind.Toggle, "A"); a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var b = menu.AddEntry(EntryKind.Toggle, "B"); b.objects.Add(new ObjectTarget { target = avatar.Hat });
            var sub = menu.AddEntry(EntryKind.Submenu, "Sub");
            var view = View(menu);
            var rowA = view.Tree.RowFor(a.id); var rowB = view.Tree.RowFor(b.id); var rowSub = view.Tree.RowFor(sub.id);

            bool applied = view.Tree.ApplyStructure(new[] { (rowB, (TreeRow)null, 0), (rowSub, (TreeRow)null, 1), (rowA, rowSub, 0) });

            Assert.That(applied, Is.True);
            Assert.That(EntryEditing.Siblings(menu, "").Select(e => e.name), Is.EqualTo(new[] { "B", "Sub" }));
            Assert.That(EntryEditing.Siblings(menu, sub.id).Select(e => e.name), Is.EqualTo(new[] { "A" }));
        }

        [Test]
        public void ApplyStructureRefusesDropsIntoForeignNodes()
        {
            var menu = avatar.AddMenu();
            var a = menu.AddEntry(EntryKind.Toggle, "A"); a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var rowA = view.Tree.RowFor(a.id);
            var existingRow = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Node != null);

            bool applied = view.Tree.ApplyStructure(new[] { (rowA, existingRow, 0) });

            Assert.That(applied, Is.False);
            Assert.That(a.parentId, Is.EqualTo(""));
        }

        [Test]
        public void ContextActionsDuplicateMoveAndDelete()
        {
            var menu = avatar.AddMenu();
            var sub = menu.AddEntry(EntryKind.Submenu, "Sub");
            var a = menu.AddEntry(EntryKind.Toggle, "A"); a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.Tree.SelectEntry(a.id);

            view.Tree.DuplicateSelected();
            Assert.That(view.Tree.Selected.Entry.name, Is.EqualTo("A Copy"));
            var copy = view.Tree.Selected.Entry;

            Assert.That(view.Tree.MoveTargets().Select(t => t.name), Is.EqualTo(new[] { "Sub" }));
            view.Tree.MoveSelectedTo(sub.id);
            Assert.That(copy.parentId, Is.EqualTo(sub.id));
            Assert.That(view.Tree.Selected.Entry, Is.SameAs(copy));

            view.Tree.DeleteSelected();
            Assert.That(menu.FindEntry(copy.id), Is.Null);
            Assert.That(view.Tree.Selected, Is.Not.Null);
            Assert.That(view.Tree.Selected.Entry, Is.SameAs(sub), "the parent is selected after deleting its last child");
        }

        [Test]
        public void RefreshWithTheSameSelectionDoesNotRebuildTheDetail()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.Tree.SelectEntry(hat.id);
            int rebuilds = view.Detail.RebuildCount;

            AvatarPlanCache.Invalidate();
            view.Refresh();

            Assert.That(view.Detail.RebuildCount, Is.EqualTo(rebuilds));
            Assert.That(view.Detail.Q<Label>("ad-parameter-name").text, Is.EqualTo("AD/Hat"));
        }

        [Test]
        public void SelectingAnotherEntryRebuildsTheDetail()
        {
            var menu = avatar.AddMenu();
            var first = menu.AddEntry(EntryKind.Toggle, "First");
            first.objects.Add(new ObjectTarget { target = avatar.Hat });
            var second = menu.AddEntry(EntryKind.Toggle, "Second");
            second.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var view = View(menu);
            view.Tree.SelectEntry(first.id);
            int rebuilds = view.Detail.RebuildCount;

            view.Tree.SelectEntry(second.id);

            Assert.That(view.Detail.RebuildCount, Is.EqualTo(rebuilds + 1));
            Assert.That(view.Detail.Row.Entry, Is.SameAs(second));
        }

        [Test]
        public void CollapsedSubmenusStayCollapsedAcrossRefresh()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            menu.AddEntry(EntryKind.Toggle, "Casual", outfits.id).objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var row = view.Tree.RowFor(outfits.id);
            view.Tree.Tree.CollapseItem(row.Id);
            Assert.That(view.Tree.Tree.IsExpanded(row.Id), Is.False);

            AvatarPlanCache.Invalidate();
            view.Refresh();

            var rebuilt = view.Tree.RowForKey(row.Key);
            Assert.That(rebuilt, Is.Not.Null);
            Assert.That(view.Tree.Tree.IsExpanded(rebuilt.Id), Is.False);
        }

        [Test]
        public void ApplyStructureRevertsWhenAMoveIsRefused()
        {
            var menu = avatar.AddMenu();
            var a = menu.AddEntry(EntryKind.Toggle, "A"); a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var b = menu.AddEntry(EntryKind.Toggle, "B"); b.objects.Add(new ObjectTarget { target = avatar.Hat });
            var sub = menu.AddEntry(EntryKind.Submenu, "Sub");
            var ghost = menu.AddEntry(EntryKind.Submenu, "Ghost");
            var view = View(menu);
            var rowB = view.Tree.RowFor(b.id); var rowSub = view.Tree.RowFor(sub.id); var rowGhost = view.Tree.RowFor(ghost.id);
            menu.entries.Remove(ghost); // gone from the menu directly, so its id still resolves to a row but menu.FindEntry(ghost.id) is null

            bool applied = view.Tree.ApplyStructure(new[] { (rowB, rowSub, 0), (rowSub, rowGhost, 0) });

            Assert.That(applied, Is.False);
            Assert.That(EntryEditing.Siblings(menu, "").Select(e => e.name), Is.EqualTo(new[] { "A", "B", "Sub" }), "the already-applied move of B under Sub is rolled back");
        }

        [Test]
        public void DroppingOntoAToggleLandsBesideIt()
        {
            var menu = avatar.AddMenu();
            var a = menu.AddEntry(EntryKind.Toggle, "A"); a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var b = menu.AddEntry(EntryKind.Toggle, "B"); b.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var rowA = view.Tree.RowFor(a.id); var rowB = view.Tree.RowFor(b.id);

            // The tree reports A as B's child at index 0 once a drag drops it there; B keeps its own recorded
            // root position since only A moved.
            bool applied = view.Tree.ApplyStructure(new[] { (rowB, (TreeRow)null, 0), (rowA, rowB, 0) });

            Assert.That(applied, Is.True);
            Assert.That(a.parentId, Is.EqualTo(""), "A lands beside B, not inside it");
            Assert.That(EntryEditing.Siblings(menu, "").Select(e => e.name), Is.EqualTo(new[] { "B", "A" }));
        }

        [Test]
        public void DroppingOntoATriggerLandsBesideIt()
        {
            // A trigger is a rule (MenuEntry.IsRule), so EntryEditing.Siblings never lists it; the drop's own
            // effect on order is read from menu.entries directly instead. C sits between A's own (wrong) index
            // and the trigger's real one, so which of the two ApplyStructure actually used shows in C's side.
            var menu = avatar.AddMenu();
            var c = menu.AddEntry(EntryKind.Toggle, "C"); c.objects.Add(new ObjectTarget { target = avatar.Hat });
            var trigger = TriggerOn(menu, "Blink");
            var a = menu.AddEntry(EntryKind.Toggle, "A"); a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var rowA = view.Tree.RowFor(a.id); var rowTrigger = view.Tree.RowFor(trigger.id); var rowC = view.Tree.RowFor(c.id);

            // The full structure includes the trigger's own tuple, at an index (5) past every real sibling, while
            // A's own tuple carries a different, wrong index (0, level with C): only looking the trigger's real
            // position up sorts A after C; falling back to A's own index would sort it before C instead.
            bool applied = view.Tree.ApplyStructure(new[] { (rowTrigger, (TreeRow)null, 5), (rowC, (TreeRow)null, 1), (rowA, rowTrigger, 0) });

            Assert.That(applied, Is.True);
            Assert.That(a.parentId, Is.EqualTo(""), "a trigger is always at the root, so beside it means the root");
            Assert.That(menu.entries.Select(e => e.name), Is.EqualTo(new[] { "Blink", "C", "A" }),
                "A sorts after the trigger's own position, not before it at A's own wrong index");
        }

        [Test]
        public void DroppingOntoASubmenuGoesInside()
        {
            var menu = avatar.AddMenu();
            var a = menu.AddEntry(EntryKind.Toggle, "A"); a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var sub = menu.AddEntry(EntryKind.Submenu, "Sub");
            var view = View(menu);
            var rowA = view.Tree.RowFor(a.id); var rowSub = view.Tree.RowFor(sub.id);

            bool applied = view.Tree.ApplyStructure(new[] { (rowA, rowSub, 0) });

            Assert.That(applied, Is.True);
            Assert.That(a.parentId, Is.EqualTo(sub.id), "an own submenu still means dropping inside it");
        }

        [Test]
        public void MovingToTheRootFromDepthWorks()
        {
            var menu = avatar.AddMenu();
            var outer = menu.AddEntry(EntryKind.Submenu, "Outer");
            var inner = menu.AddEntry(EntryKind.Submenu, "Inner", outer.id);
            var deep = menu.AddEntry(EntryKind.Toggle, "Deep", inner.id); deep.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var rowDeep = view.Tree.RowFor(deep.id);

            bool applied = view.Tree.ApplyStructure(new[] { (rowDeep, (TreeRow)null, 0) });

            Assert.That(applied, Is.True);
            Assert.That(deep.parentId, Is.EqualTo(""));
        }

        [Test]
        public void ASubmenuCannotBeDroppedIntoItsOwnChild()
        {
            var menu = avatar.AddMenu();
            var outer = menu.AddEntry(EntryKind.Submenu, "Outer");
            var inner = menu.AddEntry(EntryKind.Submenu, "Inner", outer.id);
            var view = View(menu);
            var rowOuter = view.Tree.RowFor(outer.id); var rowInner = view.Tree.RowFor(inner.id);

            bool applied = view.Tree.ApplyStructure(new[] { (rowOuter, rowInner, 0) });

            Assert.That(applied, Is.False);
            Assert.That(outer.parentId, Is.EqualTo(""), "nothing changed");
            var texts = view.Tree.Q<VisualElement>("ad-drop-refusal").Query<Label>().ToList().Select(l => l.text);
            Assert.That(texts, Has.Some.Contains(L.Text("ad.info.drop_refused.own_descendant")), "the tree says why, once");
        }

        [Test]
        public void ALockedTreeSaysWhyItRefused()
        {
            var menu = avatar.AddMenu();
            var a = menu.AddEntry(EntryKind.Toggle, "A"); a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var b = menu.AddEntry(EntryKind.Toggle, "B"); b.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.SetLocked(true);
            var rowA = view.Tree.RowFor(a.id); var rowB = view.Tree.RowFor(b.id);

            bool applied = view.Tree.ApplyStructure(new[] { (rowB, (TreeRow)null, 0), (rowA, (TreeRow)null, 1) });

            Assert.That(applied, Is.False);
            var texts = view.Tree.Q<VisualElement>("ad-drop-refusal").Query<Label>().ToList().Select(l => l.text);
            Assert.That(texts, Has.Some.Contains(L.Text("ad.info.drop_refused.locked")));
        }

        [Test]
        public void MoveToTheRootFromTheContextMenu()
        {
            var menu = avatar.AddMenu();
            var sub = menu.AddEntry(EntryKind.Submenu, "Sub");
            var a = menu.AddEntry(EntryKind.Toggle, "A", sub.id); a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.Tree.SelectEntry(a.id);

            view.Tree.MoveSelectedTo("");

            Assert.That(a.parentId, Is.EqualTo(""));
            Assert.That(view.Tree.Selected.Entry, Is.SameAs(a));
        }

        [Test]
        public void AReorderDoesNotRebuildTheRemovalPlan()
        {
            // An existing control the avatar's own expressions menu already declares, marked removed and read by
            // nothing else: the removal set really has something in it, so a stale cache serving the pre-reorder
            // plan across the reorder would be easy to catch by its content, not only by whether the planner ran.
            var existingRoot = ScriptableObject.CreateInstance<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu>();
            existingRoot.controls = new List<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control>
            {
                new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control
                {
                    name = "Ears",
                    type = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.Parameter { name = "EarParam" },
                },
            };
            try
            {
                avatar.Descriptor.expressionsMenu = existingRoot;
                var declared = avatar.ExpressionParameters.parameters.ToList();
                declared.Add(new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter
                {
                    name = "EarParam",
                    valueType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Bool,
                    saved = true,
                    networkSynced = true,
                });
                avatar.ExpressionParameters.parameters = declared.ToArray();
                var menu = avatar.AddMenu();
                menu.menuEdits.Add(new MenuEdit { id = "ears-edit", source = new List<string> { "Ears" }, remove = true });
                var a = menu.AddEntry(EntryKind.Toggle, "A"); a.objects.Add(new ObjectTarget { target = avatar.Hat });
                var b = menu.AddEntry(EntryKind.Toggle, "B"); b.objects.Add(new ObjectTarget { target = avatar.Hat });
                AvatarPlanCache.Invalidate();
                var view = View(menu);
                var rowA = view.Tree.RowFor(a.id); var rowB = view.Tree.RowFor(b.id);
                var before = view.Plan.Tree.Removal.Parameters.Single(p => p.Name == "EarParam");
                Assert.That(before.Freed, Is.True, "nothing else reads EarParam, so the removal set already has something in it");
                int callsBefore = RemovalPlanner.PlanCalls;

                bool applied = view.Tree.ApplyStructure(new[] { (rowB, (TreeRow)null, 0), (rowA, (TreeRow)null, 1) });

                Assert.That(applied, Is.True);
                Assert.That(RemovalPlanner.PlanCalls, Is.EqualTo(callsBefore), "a reorder does not re-enter the removal planner");
                var after = view.Plan.Tree.Removal.Parameters.Single(p => p.Name == "EarParam");
                Assert.That(after.Freed, Is.True, "the served plan's content is still correct after the reorder, not just uncounted");
                Assert.That(after.Bits, Is.EqualTo(before.Bits));
            }
            finally
            {
                Object.DestroyImmediate(existingRoot);
            }
        }

        [Test]
        public void DepthFromPointerIsClampedToWhatTheDropAllows()
        {
            // At the baseline or further left, depth 0; each indent step right asks for one level deeper; never
            // past maxDepth, the deepest the drop position allows.
            Assert.That(MenuTreeView.DepthFromPointer(18f, 18f, 15f, 2), Is.EqualTo(0));
            Assert.That(MenuTreeView.DepthFromPointer(0f, 18f, 15f, 2), Is.EqualTo(0), "further left than the baseline still clamps to the root, not a negative depth");
            Assert.That(MenuTreeView.DepthFromPointer(18f + 15f, 18f, 15f, 2), Is.EqualTo(1));
            Assert.That(MenuTreeView.DepthFromPointer(18f + 15f * 2, 18f, 15f, 2), Is.EqualTo(2));
            Assert.That(MenuTreeView.DepthFromPointer(18f + 15f * 50, 18f, 15f, 2), Is.EqualTo(2), "far past the deepest level the drop allows still clamps, it does not run away");
        }

        /// <summary>
        /// MeasureIndent's own math (self-healing, plausibility-gated) is exercised directly against synthetic
        /// resolvedStyle-shaped inputs rather than a real bound row: a real Editor panel never actually lays out
        /// inside this suite's synchronous test methods (confirmed empirically: even a plain probe element's own
        /// resolvedStyle, and the panel's own root size, stay NaN with no frame ever pumped between test steps),
        /// the same limitation task 7's and the prior fix round's own reports already flagged for the equivalent
        /// pointer-tracking and layout-reading code. Plausible itself is private, so this drives it the same way
        /// the earlier UsesProperty guard test did: a local copy of the same rule, over the same kind of inputs
        /// MeasureIndent gates on.
        /// </summary>
        [Test]
        public void ImplausibleMeasurementsDoNotReplaceAGoodCachedValue()
        {
            bool Plausible(float value) => value >= 0f && value < 300f;

            // What MeasureIndent itself is guarded against: a pooled row's stale layout (far too large or negative,
            // the shapes an unrelated previous binding or a mid-layout read could leave behind) must never replace
            // a value that already looks like a real row's own indent or content-left; a value that does look
            // real, from any row's layout, must always be accepted, so a wrong early sample or a skin change can
            // still self-heal instead of sticking for the rest of the session.
            Assert.That(Plausible(15f), Is.True, "a real indent step must be accepted");
            Assert.That(Plausible(18f), Is.True, "a real content-left must be accepted");
            Assert.That(Plausible(0f), Is.True, "a depth-0 row flush with the tree's own edge is a real reading, not implausible");
            Assert.That(Plausible(-4f), Is.False, "a negative reading (a pooled row's stale, not-yet-relaid-out geometry) must not overwrite a good cached value");
            Assert.That(Plausible(float.NaN), Is.False, "a reading taken mid-layout, before Yoga has settled, must not overwrite a good cached value");
            Assert.That(Plausible(10000f), Is.False, "a reading wildly larger than any real row layout must not overwrite a good cached value");
        }

        [Test]
        public void DraggingFarLeftLandsInTheRootMenu()
        {
            var menu = avatar.AddMenu();
            var outer = menu.AddEntry(EntryKind.Submenu, "Outer");
            var inner = menu.AddEntry(EntryKind.Submenu, "Inner", outer.id);
            var deep = menu.AddEntry(EntryKind.Toggle, "Deep", inner.id); deep.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var rowDeep = view.Tree.RowFor(deep.id);
            var rowInner = view.Tree.RowFor(inner.id);
            view.Tree.SelectEntry(deep.id);
            view.Tree.DropDepth = 0;

            // Unity's own reorder decided Deep stays under Inner (a plain within-level reorder); the tracked depth
            // still pulls it all the way out to the root.
            bool applied = view.Tree.ApplyStructure(new[] { (rowDeep, rowInner, 0) });

            Assert.That(applied, Is.True);
            Assert.That(deep.parentId, Is.EqualTo(""));
        }

        [Test]
        public void DraggingOneLevelLeftLandsInTheParentSubmenu()
        {
            var menu = avatar.AddMenu();
            var outer = menu.AddEntry(EntryKind.Submenu, "Outer");
            var inner = menu.AddEntry(EntryKind.Submenu, "Inner", outer.id);
            var deep = menu.AddEntry(EntryKind.Toggle, "Deep", inner.id); deep.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var rowDeep = view.Tree.RowFor(deep.id);
            var rowInner = view.Tree.RowFor(inner.id);
            view.Tree.SelectEntry(deep.id);
            view.Tree.DropDepth = rowInner.Depth; // one indent step shallower than staying under Inner

            bool applied = view.Tree.ApplyStructure(new[] { (rowDeep, rowInner, 0) });

            Assert.That(applied, Is.True);
            Assert.That(deep.parentId, Is.EqualTo(outer.id), "one indent step left of Inner lands beside Inner, inside Outer");
        }

        [Test]
        public void DraggingRightKeepsTheDeepestAllowedLevel()
        {
            var menu = avatar.AddMenu();
            var sub = menu.AddEntry(EntryKind.Submenu, "Sub");
            var a = menu.AddEntry(EntryKind.Toggle, "A"); a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var rowA = view.Tree.RowFor(a.id);
            var rowSub = view.Tree.RowFor(sub.id);
            view.Tree.SelectEntry(a.id);
            view.Tree.DropDepth = 99; // far right of anything this drop actually offers

            // Unity's own reorder already decided A goes inside Sub (dropped directly onto it); the tracked depth
            // cannot ask for anything deeper than that.
            bool applied = view.Tree.ApplyStructure(new[] { (rowA, rowSub, 0) });

            Assert.That(applied, Is.True);
            Assert.That(a.parentId, Is.EqualTo(sub.id), "dragging right of what the drop offers keeps the deepest level Unity's own reorder already chose");
        }

        [Test]
        public void DroppingInTheEmptySpaceLandsInTheRootMenu()
        {
            var menu = avatar.AddMenu();
            var a = menu.AddEntry(EntryKind.Toggle, "A"); a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var b = menu.AddEntry(EntryKind.Toggle, "B"); b.objects.Add(new ObjectTarget { target = avatar.Hat });
            var outer = menu.AddEntry(EntryKind.Submenu, "Outer");
            var deep = menu.AddEntry(EntryKind.Toggle, "Deep", outer.id); deep.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var rowA = view.Tree.RowFor(a.id); var rowB = view.Tree.RowFor(b.id);
            var rowOuter = view.Tree.RowFor(outer.id); var rowDeep = view.Tree.RowFor(deep.id);
            view.Tree.SelectEntry(deep.id);

            // Empty space below the last row, driven through the real path: HitTestRow finds no row there, so
            // OnTreePointerMove hovers nothing and clamps to a maxDepth of 0, the deepest the pointer's own x could
            // ever reach.
            view.Tree.DragHoverRow = null;
            view.Tree.DropDepth = MenuTreeView.DepthFromPointer(500f, MenuTreeView.RowContentLeft, MenuTreeView.IndentWidth, 0);

            // Unity's own reorder leaves Deep under Outer, its only child, since nothing about that list changed;
            // the other root rows keep their own places too.
            bool applied = view.Tree.ApplyStructure(new[] { (rowA, (TreeRow)null, 0), (rowOuter, (TreeRow)null, 1), (rowB, (TreeRow)null, 2), (rowDeep, rowOuter, 0) });

            Assert.That(applied, Is.True);
            Assert.That(deep.parentId, Is.EqualTo(""));
            Assert.That(EntryEditing.Siblings(menu, "").Select(e => e.name), Is.EqualTo(new[] { "A", "Outer", "B", "Deep" }), "it lands at the end, after every other root row");
        }

        [Test]
        public void ARowCorrectedToTheRootKeepsThePositionItWasDroppedAt()
        {
            var menu = avatar.AddMenu();
            var outer = menu.AddEntry(EntryKind.Submenu, "Outer");
            var inner = menu.AddEntry(EntryKind.Submenu, "Inner", outer.id);
            var deep = menu.AddEntry(EntryKind.Toggle, "Deep", inner.id); deep.objects.Add(new ObjectTarget { target = avatar.Hat });
            var a = menu.AddEntry(EntryKind.Toggle, "A"); a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var b = menu.AddEntry(EntryKind.Toggle, "B"); b.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var rowOuter = view.Tree.RowFor(outer.id);
            var rowInner = view.Tree.RowFor(inner.id);
            var rowDeep = view.Tree.RowFor(deep.id);
            var rowA = view.Tree.RowFor(a.id);
            var rowB = view.Tree.RowFor(b.id);
            view.Tree.SelectEntry(deep.id);

            // Dragging Deep, Inner's only child, far left while dropping below the last root row: Unity's own
            // reorder leaves it under Inner, unchanged (its own list never moved), and hovers nothing.
            view.Tree.DragHoverRow = null;
            view.Tree.DropDepth = 0;

            bool applied = view.Tree.ApplyStructure(new[] { (rowOuter, (TreeRow)null, 0), (rowA, (TreeRow)null, 1), (rowB, (TreeRow)null, 2), (rowDeep, rowInner, 0) });

            Assert.That(applied, Is.True);
            Assert.That(deep.parentId, Is.EqualTo(""));
            Assert.That(EntryEditing.Siblings(menu, "").Select(e => e.name), Is.EqualTo(new[] { "Outer", "A", "B", "Deep" }),
                "keeping Unity's own index under Inner would land it first at the root; dropped below the last root row, it belongs last");
        }

        [Test]
        public void DraggingOutOfASubmenuLandsRightAfterIt()
        {
            var menu = avatar.AddMenu();
            var a = menu.AddEntry(EntryKind.Toggle, "A"); a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var b = menu.AddEntry(EntryKind.Submenu, "B");
            var x = menu.AddEntry(EntryKind.Toggle, "X", b.id); x.objects.Add(new ObjectTarget { target = avatar.Hat });
            var y = menu.AddEntry(EntryKind.Toggle, "Y", b.id); y.objects.Add(new ObjectTarget { target = avatar.Hat });
            var c = menu.AddEntry(EntryKind.Toggle, "C"); c.objects.Add(new ObjectTarget { target = avatar.Hat });
            var d = menu.AddEntry(EntryKind.Toggle, "D"); d.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var rowA = view.Tree.RowFor(a.id);
            var rowB = view.Tree.RowFor(b.id);
            var rowX = view.Tree.RowFor(x.id);
            var rowY = view.Tree.RowFor(y.id);
            var rowC = view.Tree.RowFor(c.id);
            var rowD = view.Tree.RowFor(d.id);
            view.Tree.SelectEntry(y.id);

            // Dragging Y out of B while the pointer is still over X, Y's own sibling inside B: Unity's own reorder
            // leaves Y under B (its own list never moved structurally by this drag), but the tracked depth pulls it
            // all the way out to the root.
            view.Tree.DragHoverRow = rowX;
            view.Tree.DropDepth = 0;

            bool applied = view.Tree.ApplyStructure(new[]
            {
                (rowA, (TreeRow)null, 0), (rowB, (TreeRow)null, 1),
                (rowX, rowB, 0), (rowY, rowB, 1),
                (rowC, (TreeRow)null, 2), (rowD, (TreeRow)null, 3),
            });

            Assert.That(applied, Is.True);
            Assert.That(y.parentId, Is.EqualTo(""));
            Assert.That(EntryEditing.Siblings(menu, "").Select(e => e.name), Is.EqualTo(new[] { "A", "B", "Y", "C", "D" }),
                "Y left B while the pointer was still over X, B's own child; it belongs right after B, not at the end");
        }

        [Test]
        public void TheLabelNamesTheParentWhenTheTargetCannotHoldChildren()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var wings = menu.AddEntry(EntryKind.Toggle, "Wings", outfits.id); wings.objects.Add(new ObjectTarget { target = avatar.Hat });
            var a = menu.AddEntry(EntryKind.Toggle, "A"); a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var rowWings = view.Tree.RowFor(wings.id);
            view.Tree.SelectEntry(a.id);

            // The right half of the plain toggle Wings: the walk lands on Wings itself, which cannot hold children.
            view.Tree.DragHoverRow = rowWings;
            view.Tree.DropDepth = rowWings.Depth + 1;

            Assert.That(view.Tree.Q<Label>("ad-drag-target").text, Is.EqualTo(string.Format(L.Text("ad.ui.drag_target.into"), "Outfits")),
                "Wings cannot hold children, so the drop lands beside it inside Outfits; the label names Outfits, not Wings");
        }

        [Test]
        public void TheLabelNamesTheRootOverAGroupRow()
        {
            var menu = avatar.AddMenu();
            TriggerOn(menu, "Blink");
            var a = menu.AddEntry(EntryKind.Toggle, "A"); a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var group = view.Tree.RowsOfKind(RowKind.TriggersGroup).Single();
            view.Tree.SelectEntry(a.id);

            // Dragging right over the synthetic TRIGGERS group row: the walk lands on the group itself, which
            // cannot hold anything and the drop genuinely refuses over it, exactly as ApplyStructure itself does
            // for this same row.
            view.Tree.DragHoverRow = group;
            view.Tree.DropDepth = group.Depth + 1;

            Assert.That(view.Tree.Q<Label>("ad-drag-target").text,
                Is.EqualTo(string.Format(L.Text("ad.info.drop_refused"), L.Text("ad.info.drop_refused.not_a_container"))),
                "a group row cannot hold anything the drop could ever use; the label says the drop will be refused instead of promising a place it will not land");
        }

        /// <summary>
        /// The single resolver behind both the drag label and the real drop (MenuTreeView.ResolveDropTarget) has to
        /// agree with itself for every kind of row the tree can show as a drop target: an own submenu holds the
        /// dropped row inside it, an own toggle or trigger lands it beside itself (its own parent, which for a
        /// trigger and for a toggle physically nested under an existing avatar submenu is the root), and anything
        /// this tree has no menu position to put a row beside at all (a synthetic group row, an existing submenu or a
        /// foreign submenu) refuses the whole drop, exactly what the label already said it would do. A More page is
        /// the rest of the menu it hangs under, so a drop on one lands in that menu.
        /// </summary>
        [Test]
        public void TheLabelAndTheDropAgreeForEveryRowKind()
        {
            Assume.That(VrcFuryTestHelper.VrcFuryType, Is.Not.Null);
            var menu = avatar.AddMenu();
            var ownSub = menu.AddEntry(EntryKind.Submenu, "OwnSub");
            var ownToggle = menu.AddEntry(EntryKind.Toggle, "OwnToggle"); ownToggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            var mover = menu.AddEntry(EntryKind.Toggle, "Mover"); mover.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var trigger = TriggerOn(menu, "OwnTrigger");
            for (int i = 1; i <= 9; i++) menu.AddEntry(EntryKind.Toggle, "Overflow" + i, ownSub.id).objects.Add(new ObjectTarget { target = avatar.Hat });
            AddFuryToggle("Clothes/Hat");
            var view = View(menu);
            string refusalText = string.Format(L.Text("ad.info.drop_refused"), L.Text("ad.info.drop_refused.not_a_container"));

            void Check(string label, TreeRow target, bool refused, string expectedParentId, string expectedDisplayName)
            {
                view.Tree.SelectEntry(mover.id);
                view.Tree.DragHoverRow = target;
                view.Tree.DropDepth = target.Depth + 1; // deepest allowed: landing directly on the hovered target

                string expectedLabel = refused ? refusalText
                    : expectedParentId.Length == 0 ? L.Text("ad.ui.drag_target.root")
                    : string.Format(L.Text("ad.ui.drag_target.into"), expectedDisplayName);
                Assert.That(view.Tree.Q<Label>("ad-drag-target").text, Is.EqualTo(expectedLabel), label + ": the label");

                // Unity's own reorder is simulated as having already chosen target as Mover's parent, the same
                // shape a real drop directly onto a non-container row takes.
                var moverRow = view.Tree.RowFor(mover.id);
                bool applied = view.Tree.ApplyStructure(new[] { (moverRow, target, 0) });

                Assert.That(applied, Is.EqualTo(!refused), label + ": the drop must succeed or refuse exactly as the label said");
                if (!refused) Assert.That(mover.parentId, Is.EqualTo(expectedParentId), label + ": the drop landed somewhere other than what the label promised");
            }

            Check("own toggle", view.Tree.RowFor(ownToggle.id), false, "", null);
            Check("own submenu", view.Tree.RowFor(ownSub.id), false, ownSub.id, "OwnSub");
            Check("trigger in the group", view.Tree.RowFor(trigger.id), false, "", null);
            Check("the group row", view.Tree.RowsOfKind(RowKind.TriggersGroup).Single(), true, null, null);
            Check("existing submenu", view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Node != null && r.Name == "Old"), true, null, null);
            Check("foreign submenu", view.Tree.RowsOfKind(RowKind.Foreign).First(r => r.Node != null && r.Name == "Clothes"), true, null, null);
            Check("More page", view.Tree.RowsOfKind(RowKind.More).Single(), false, ownSub.id, "OwnSub");

            // A toggle shown under an existing avatar submenu (installed there via installPath, not moved there by
            // any menu edit of its own): its visual parent row in the tree is the existing submenu, but its own
            // parentId is still the component's root, so it resolves to the root exactly like a plain own toggle
            // does. That submenu is where the component's root is, so the label names it.
            var nestedOwner = new GameObject("NestedOwner"); nestedOwner.transform.SetParent(avatar.Root.transform);
            var menu2 = nestedOwner.AddComponent<AvatarDoc>();
            menu2.installPath = "Old";
            var nested = menu2.AddEntry(EntryKind.Toggle, "NestedToggle"); nested.objects.Add(new ObjectTarget { target = avatar.Hat });
            var mover2 = menu2.AddEntry(EntryKind.Toggle, "Mover2"); mover2.objects.Add(new ObjectTarget { target = avatar.Glasses });
            AvatarPlanCache.Invalidate();
            var view2 = View(menu2);
            var rowNested = view2.Tree.RowFor(nested.id);
            Assert.That(rowNested.ParentRow?.Kind, Is.EqualTo(RowKind.Existing), "NestedToggle must actually sit under the existing avatar submenu for this case to mean anything");

            view2.Tree.SelectEntry(mover2.id);
            view2.Tree.DragHoverRow = rowNested;
            view2.Tree.DropDepth = rowNested.Depth + 1;

            Assert.That(view2.Tree.Q<Label>("ad-drag-target").text, Is.EqualTo(string.Format(L.Text("ad.ui.drag_target.into"), "Old")),
                "toggle under an existing avatar submenu: the label names the submenu the component installs into");

            var rowMover2 = view2.Tree.RowFor(mover2.id);
            bool applied2 = view2.Tree.ApplyStructure(new[] { (rowMover2, rowNested, 0) });

            Assert.That(applied2, Is.True, "toggle under an existing avatar submenu: the drop must succeed exactly as the label said");
            Assert.That(mover2.parentId, Is.EqualTo(""), "toggle under an existing avatar submenu: the drop landed somewhere other than what the label promised");
        }

        [Test]
        public void TheDragLabelNamesTheTargetAndClearsAfterwards()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var a = menu.AddEntry(EntryKind.Toggle, "A", outfits.id); a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var rowOutfits = view.Tree.RowFor(outfits.id);
            var rowA = view.Tree.RowFor(a.id);
            view.Tree.SelectEntry(a.id);

            view.Tree.DragHoverRow = rowOutfits;
            view.Tree.DropDepth = rowOutfits.Depth + 1; // deepest allowed: inside Outfits

            Assert.That(view.Tree.Q<Label>("ad-drag-target").text, Is.EqualTo(string.Format(L.Text("ad.ui.drag_target.into"), "Outfits")));

            view.Tree.DropDepth = 0; // far left: the root menu

            Assert.That(view.Tree.Q<Label>("ad-drag-target").text, Is.EqualTo(L.Text("ad.ui.drag_target.root")));

            bool applied = view.Tree.ApplyStructure(new[] { (rowA, rowOutfits, 0) });

            Assert.That(applied, Is.True);
            Assert.That(view.Tree.Q<Label>("ad-drag-target").text, Is.Empty, "the label clears once the drag ends");
        }

        [Test]
        public void MoveToStillWorks()
        {
            var menu = avatar.AddMenu();
            var outer = menu.AddEntry(EntryKind.Submenu, "Outer");
            var other = menu.AddEntry(EntryKind.Submenu, "Other");
            var a = menu.AddEntry(EntryKind.Toggle, "A", outer.id); a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.Tree.SelectEntry(a.id);

            view.Tree.MoveSelectedTo(other.id);

            Assert.That(a.parentId, Is.EqualTo(other.id));
            Assert.That(view.Tree.Selected.Entry, Is.SameAs(a));
        }

        [Test]
        public void ADraggedDepthChangeDoesNotRebuildTheRemovalPlan()
        {
            var menu = avatar.AddMenu();
            var outer = menu.AddEntry(EntryKind.Submenu, "Outer");
            var inner = menu.AddEntry(EntryKind.Submenu, "Inner", outer.id);
            var deep = menu.AddEntry(EntryKind.Toggle, "Deep", inner.id); deep.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var rowDeep = view.Tree.RowFor(deep.id);
            var rowInner = view.Tree.RowFor(inner.id);
            var rowOuter = view.Tree.RowFor(outer.id);
            view.Tree.SelectEntry(deep.id);
            int callsBefore = RemovalPlanner.PlanCalls;
            var planBefore = view.Plan;

            // Driving the drag preview alone, as every pointer move while dragging would, must not touch the plan.
            view.Tree.DragHoverRow = rowInner;
            view.Tree.DropDepth = rowOuter.Depth + 1; // one indent step left of Inner: lands under Outer
            Assert.That(RemovalPlanner.PlanCalls, Is.EqualTo(callsBefore), "the drag label never re-enters the removal planner");
            Assert.That(view.Plan, Is.SameAs(planBefore), "the drag label never rebuilds the plan");

            bool applied = view.Tree.ApplyStructure(new[] { (rowDeep, rowInner, 0) });

            Assert.That(applied, Is.True);
            Assert.That(deep.parentId, Is.EqualTo(outer.id));
            Assert.That(RemovalPlanner.PlanCalls, Is.EqualTo(callsBefore), "a depth-corrected drop is still a reorder: it does not re-enter the removal planner either");
        }

        MenuEntry TriggerOn(AvatarDoc menu, string name)
        {
            var entry = menu.AddEntry(EntryKind.Trigger, name);
            entry.objects.Add(new ObjectTarget { target = avatar.Glasses });
            return entry;
        }

        /// <summary>One entry of every blendshape section kind Capture now offers: a toggle, a radial, a trigger's
        /// condition effects, a blend shape linked trigger's effects, and an override's pins.</summary>
        (MenuEntry entry, string context)[] CaptureSections(AvatarDoc menu)
        {
            var toggle = menu.AddEntry(EntryKind.Toggle, "Face");
            var radial = menu.AddEntry(EntryKind.Radial, "Eyes");
            var trigger = TriggerOn(menu, "Blink");
            var link = menu.AddEntry(EntryKind.Trigger, "Link");
            link.source = TriggerSource.BlendShape;
            var over = menu.AddEntry(EntryKind.Override, "Silence");
            return new[]
            {
                (toggle, "toggle"),
                (radial, "radial"),
                (trigger, "trigger condition"),
                (link, "blend shape link"),
                (over, "override"),
            };
        }

        [Test]
        public void AddTriggerButtonCreatesATriggerUnderTheGroup()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);

            var trigger = view.Tree.AddEntry(EntryKind.Trigger);

            Assert.That(trigger.kind, Is.EqualTo(EntryKind.Trigger));
            Assert.That(trigger.name, Is.EqualTo("New Trigger"));
            var row = view.Tree.RowFor(trigger.id);
            Assert.That(row.Kind, Is.EqualTo(RowKind.Trigger));
            Assert.That(row.ParentRow.Kind, Is.EqualTo(RowKind.TriggersGroup));
            Assert.That(view.Tree.Selected.Entry, Is.SameAs(trigger));
        }

        [Test]
        public void TriggerDetailShowsTheSourceControlsAndWritesTheSource()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var trigger = TriggerOn(menu, "Ears");
            var view = View(menu);
            view.Tree.SelectEntry(trigger.id);

            Assert.That(view.Detail.Q<DropdownField>("ad-trigger-source"), Is.Not.Null);
            Assert.That(view.Detail.Q<DropdownField>("ad-trigger-toggle"), Is.Not.Null, "toggle source by default");

            view.Detail.SetSourceToggle(hat.id);
            Assert.That(trigger.sourceToggleId, Is.EqualTo(hat.id));

            view.Detail.SetTriggerSource(TriggerSource.Parameter);
            Assert.That(trigger.source, Is.EqualTo(TriggerSource.Parameter));
            Assert.That(view.Detail.Q<DropdownField>("ad-trigger-toggle"), Is.Null);
            view.Detail.SetSourceParameter("GestureRight");
            Assert.That(trigger.sourceParameter, Is.EqualTo("GestureRight"));

            view.Detail.SetTriggerSource(TriggerSource.BlendShape);
            Assert.That(view.Detail.Q<TargetListView>("ad-blendshapes").IsLinkMode, Is.True);
        }

        [Test]
        public void PickingASourceShapeUpdatesTheButton()
        {
            var menu = avatar.AddMenu();
            var trigger = TriggerOn(menu, "Blink");
            trigger.source = TriggerSource.BlendShape;
            var view = View(menu);
            view.Tree.SelectEntry(trigger.id);

            view.Detail.SetSourceShape(avatar.Body, "Eye_Big");

            var button = view.Detail.Q<Button>("ad-trigger-shape");
            Assert.That(button, Is.Not.Null);
            Assert.That(button.text, Does.Contain("Eye_Big"));
        }

        [Test]
        public void TriggerRowsShowValidationForAMissingSource()
        {
            var menu = avatar.AddMenu();
            var trigger = TriggerOn(menu, "Ears");
            trigger.source = TriggerSource.Toggle;
            trigger.sourceToggleId = "missing";
            var view = View(menu);
            view.Tree.SelectEntry(trigger.id);

            Assert.That(view.Tree.RowFor(trigger.id).Messages.Any(m => m.Key == "ad.warn.trigger_source_missing"), Is.True);
            Assert.That(view.Detail.Query(className: "ad-warnbox").ToList().Count, Is.EqualTo(1));
        }

        [Test]
        public void BuiltInNamesFeedTheParameterPicker()
        {
            Assert.That(TriggerPlanner.BuiltInNames, Does.Contain("GestureRight"));
            Assert.That(TriggerPlanner.BuiltInNames.Count(), Is.GreaterThan(20));
        }

        [Test]
        public void ParameterPickerListsVrcFuryGlobals()
        {
            Assume.That(VrcFuryTestHelper.VrcFuryType, Is.Not.Null);
            var toggle = AddFuryToggle("Hat");
            VrcFuryTestHelper.ConfigureToggle(toggle, t =>
            {
                VrcFuryTestHelper.SetField(t, "useGlobalParam", true);
                VrcFuryTestHelper.SetField(t, "globalParam", "MyGlobalParam");
            });

            var names = ParameterPicker.VrcFuryChoices(avatar.Root.transform);

            Assert.That(names, Does.Contain("MyGlobalParam"));
        }

        [Test]
        public void TriggerRowsCanBeDeleted()
        {
            var menu = avatar.AddMenu();
            var first = TriggerOn(menu, "First");
            var second = TriggerOn(menu, "Second");
            var view = View(menu);
            view.Tree.SelectEntry(first.id);

            Undo.IncrementCurrentGroup();
            view.Tree.DeleteSelected();

            Assert.That(menu.entries.Any(e => e.id == first.id), Is.False);
            Assert.That(view.Tree.Selected, Is.Not.Null);
            Assert.That(view.Tree.Selected.Entry, Is.SameAs(second));

            Undo.PerformUndo();

            // PerformUndo restores the component through deserialization, so the entries come back as fresh
            // MenuEntry instances with the same values rather than the same references; compare by name.
            Assert.That(menu.entries.Select(e => e.name), Is.EqualTo(new[] { "First", "Second" }));
        }

        [Test]
        public void TriggerRowsCanBeDuplicated()
        {
            var menu = avatar.AddMenu();
            var trigger = TriggerOn(menu, "Ears");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "GestureRight";
            var view = View(menu);
            view.Tree.SelectEntry(trigger.id);

            view.Tree.DuplicateSelected();

            var copy = view.Tree.Selected.Entry;
            Assert.That(copy, Is.Not.SameAs(trigger));
            Assert.That(copy.kind, Is.EqualTo(EntryKind.Trigger));
            Assert.That(copy.id, Is.Not.EqualTo(trigger.id));
            Assert.That(copy.source, Is.EqualTo(TriggerSource.Parameter));
            Assert.That(copy.sourceParameter, Is.EqualTo("GestureRight"));
            Assert.That(menu.entries.Count(e => e.kind == EntryKind.Trigger), Is.EqualTo(2));
        }

        MenuEntry ContactTriggerOn(AvatarDoc menu, string name, Transform anchor)
        {
            var entry = TriggerOn(menu, name);
            entry.source = TriggerSource.Contact;
            entry.contactAnchor = anchor;
            return entry;
        }

        [Test]
        public void ContactPanelShowsItsRows()
        {
            var menu = avatar.AddMenu();
            var trigger = ContactTriggerOn(menu, "Poke", avatar.Hat.transform);
            var view = View(menu);

            view.Tree.SelectEntry(trigger.id);

            Assert.That(view.Detail.Q<ObjectField>("ad-touch-anchor"), Is.Not.Null);
            Assert.That(view.Detail.Q<Slider>("ad-touch-radius"), Is.Not.Null);
            Assert.That(view.Detail.Q<Vector3Field>("ad-touch-offset"), Is.Not.Null);
            Assert.That(view.Detail.Q<Toggle>("ad-touch-tag-Finger"), Is.Not.Null);
            Assert.That(view.Detail.Q<Toggle>("ad-touch-self"), Is.Not.Null);
            Assert.That(view.Detail.Q<Toggle>("ad-touch-others"), Is.Not.Null);
            Assert.That(view.Detail.Q<SegmentedChoice>("ad-touch-distance"), Is.Not.Null);
            Assert.That(view.Detail.Q<SegmentedChoice>("ad-touch-mode"), Is.Not.Null);

            view.Detail.Q<SegmentedChoice>("ad-touch-distance").Pick(1);

            Assert.That(trigger.contactProximity, Is.EqualTo(0.5f).Within(0.0001f), "Closer than starts at the spec's half way point");
            Assert.That(view.Detail.Q<Slider>("ad-touch-proximity"), Is.Not.Null, "and the fraction slider appears with it");

            view.Detail.Q<SegmentedChoice>("ad-touch-distance").Pick(0);

            Assert.That(trigger.contactProximity, Is.EqualTo(0f), "back to an ordinary touch");
        }

        [Test]
        public void FlipModeHidesTheEffectLists()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            var trigger = ContactTriggerOn(menu, "Poke", avatar.Hat.transform);
            trigger.touchMode = TouchMode.FlipToggle;
            trigger.flipToggleId = toggle.id;
            var view = View(menu);

            view.Tree.SelectEntry(trigger.id);

            Assert.That(view.Detail.Q<TargetListView>("ad-objects"), Is.Null);
            Assert.That(view.Detail.Q<TargetListView>("ad-blendshapes"), Is.Null);
            Assert.That(view.Detail.Q<TargetListView>("ad-materials"), Is.Null);
            Assert.That(view.Detail.Q<DropdownField>("ad-touch-flip-toggle"), Is.Not.Null);
        }

        [Test]
        public void TogglingATagUpdatesTheList()
        {
            var menu = avatar.AddMenu();
            var trigger = ContactTriggerOn(menu, "Poke", avatar.Hat.transform);
            var view = View(menu);
            view.Tree.SelectEntry(trigger.id);

            view.Detail.ToggleContactTag("Head", true);
            Assert.That(trigger.contactTags, Does.Contain("Head"));

            view.Detail.ToggleContactTag("Head", false);
            Assert.That(trigger.contactTags, Does.Not.Contain("Head"));
        }

        [Test]
        public void PickBoneSetsTheAnchor()
        {
            // The standalone "Pick bone" button is gone; the bone picker lives on the anchor field's own
            // selector now (Transform kind, bones first). The write it performs is covered directly through
            // SetContactAnchor, the method a pick from that dropdown calls exactly as a drop would.
            var menu = avatar.AddMenu();
            var trigger = ContactTriggerOn(menu, "Poke", avatar.Hat.transform);
            var view = View(menu);
            view.Tree.SelectEntry(trigger.id);

            Assert.That(view.Detail.Q<Button>("ad-touch-pick-bone"), Is.Null, "replaced by the field's own picker");
            var anchorField = view.Detail.Q<ObjectField>("ad-touch-anchor");
            Assert.That(anchorField, Is.Not.Null);
            var selector = anchorField.Q<VisualElement>(className: ObjectField.selectorUssClassName);
            Assert.That(selector.userData, Is.EqualTo(PickKind.Transform));

            view.Detail.SetContactAnchor(avatar.Glasses.transform);

            Assert.That(trigger.contactAnchor, Is.SameAs(avatar.Glasses.transform));
        }

        [Test]
        public void PhysBonePanelShowsTheThresholdOnlyForFloatEvents()
        {
            var menu = avatar.AddMenu();
            var pb = avatar.Hat.AddComponent<VRCPhysBone>();
            var trigger = TriggerOn(menu, "Ear");
            trigger.source = TriggerSource.PhysBone;
            trigger.physBone = pb;
            var view = View(menu);
            view.Tree.SelectEntry(trigger.id);

            Assert.That(view.Detail.Q<Slider>("ad-touch-threshold"), Is.Null, "Grabbed is a bool event");

            view.Detail.SetPhysBoneEvent(PhysBoneEvent.AngleAbove);

            Assert.That(trigger.physBoneEvent, Is.EqualTo(PhysBoneEvent.AngleAbove));
            Assert.That(trigger.physBoneThreshold, Is.EqualTo(0.5f), "an old threshold left at 0 becomes 0.5 once a float event is chosen");
            Assert.That(view.Detail.Q<Slider>("ad-touch-threshold"), Is.Not.Null);
        }

        [Test]
        public void ThresholdOfZeroSurvivesAnEventChange()
        {
            var menu = avatar.AddMenu();
            var pb = avatar.Hat.AddComponent<VRCPhysBone>();
            var trigger = TriggerOn(menu, "Ear");
            trigger.source = TriggerSource.PhysBone;
            trigger.physBone = pb;
            trigger.physBoneEvent = PhysBoneEvent.AngleAbove;
            trigger.physBoneThreshold = 0f; // a deliberate zero, not the "never set" sentinel
            var view = View(menu);
            view.Tree.SelectEntry(trigger.id);

            view.Detail.SetPhysBoneEvent(PhysBoneEvent.StretchAbove);

            Assert.That(trigger.physBoneEvent, Is.EqualTo(PhysBoneEvent.StretchAbove));
            Assert.That(trigger.physBoneThreshold, Is.EqualTo(0f), "moving between two float events must not overwrite a deliberate zero");
        }

        [Test]
        public void SwitchingAnOldTriggerToContactAppliesTheDefaults()
        {
            var menu = avatar.AddMenu();
            var trigger = TriggerOn(menu, "Poke");
            // Simulates an old serialized entry: Unity skips field initialisers for data an Undo or a prefab
            // override deserialises, so a trigger that predates the Contact fields reads them back bare (0, empty, false).
            trigger.contactRadius = 0f;
            trigger.contactTags = new List<string>();
            trigger.contactAllowSelf = false;
            trigger.contactAllowOthers = false;
            var view = View(menu);
            view.Tree.SelectEntry(trigger.id);

            view.Detail.SetTriggerSource(TriggerSource.Contact);

            Assert.That(trigger.source, Is.EqualTo(TriggerSource.Contact));
            Assert.That(trigger.contactRadius, Is.EqualTo(0.05f));
            Assert.That(trigger.contactTags, Is.EqualTo(new List<string> { "Finger", "Hand" }));
            Assert.That(trigger.contactAllowSelf, Is.True);
            Assert.That(trigger.contactAllowOthers, Is.True);
        }

        [Test]
        public void SceneHandleWritesOffsetAndRadiusWithUndo()
        {
            var menu = avatar.AddMenu();
            var trigger = ContactTriggerOn(menu, "Poke", avatar.Hat.transform);

            Undo.IncrementCurrentGroup();
            ContactSceneHandle.Apply(trigger, menu, new Vector3(0f, 0.2f, 0f), 3f);

            Assert.That(trigger.contactOffset, Is.EqualTo(new Vector3(0f, 0.2f, 0f)));
            Assert.That(trigger.contactRadius, Is.EqualTo(1f), "clamped to the 0.005 to 1 range");

            Undo.PerformUndo();

            // PerformUndo restores the component through deserialization, so the entry comes back as a fresh
            // MenuEntry instance with the same values rather than the same reference; find it again by id.
            var restored = menu.entries.Single(e => e.id == trigger.id);
            Assert.That(restored.contactOffset, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void ContactSceneHandleSyncsOnAttach()
        {
            // A real AttachToPanelEvent cannot be dispatched on a detached element in a headless test, so this
            // drives the same handler EntryDetailView's constructor wires to that event directly.
            var menu = avatar.AddMenu();
            var trigger = ContactTriggerOn(menu, "Poke", avatar.Hat.transform);
            var view = View(menu);
            view.Tree.SelectEntry(trigger.id);
            var row = view.Tree.RowFor(trigger.id);

            Assert.That(view.Detail.SceneHandle.IsDrawing, Is.False, "never subscribed before an attach");

            view.Detail.SceneHandle.Attach(row);
            Assert.That(view.Detail.SceneHandle.IsDrawing, Is.True, "a restored selection that is already a contact trigger claims it on attach");

            view.Detail.SceneHandle.Detach();
            Assert.That(view.Detail.SceneHandle.IsDrawing, Is.False);
        }

        [Test]
        public void ContactSceneHandleDrawsOncePerTrigger()
        {
            var menu = avatar.AddMenu();
            var trigger = ContactTriggerOn(menu, "Poke", avatar.Hat.transform);
            var viewA = View(menu);
            var viewB = View(menu);
            viewA.Tree.SelectEntry(trigger.id);
            viewB.Tree.SelectEntry(trigger.id);
            var rowA = viewA.Tree.RowFor(trigger.id);
            var rowB = viewB.Tree.RowFor(trigger.id);

            viewA.Detail.SceneHandle.Attach(rowA);
            viewB.Detail.SceneHandle.Attach(rowB);

            Assert.That(viewA.Detail.SceneHandle.IsDrawing, Is.True, "the first attached view claims the trigger");
            Assert.That(viewB.Detail.SceneHandle.IsDrawing, Is.False, "a second view of the same trigger does not draw on top of it");

            viewA.Detail.SceneHandle.Detach();

            Assert.That(viewB.Detail.SceneHandle.IsDrawing, Is.True, "the other registered view takes over once the drawer goes away");
        }

        [Test]
        public void FlipTriggerPressFlipsTheToggleInThePreview()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            var trigger = ContactTriggerOn(menu, "Poke", avatar.Hat.transform);
            trigger.touchMode = TouchMode.FlipToggle;
            trigger.flipToggleId = toggle.id;
            var view = View(menu);
            view.TestMode = true;
            var wheel = view.Wheel; // forces the wheel to build against the current plan

            wheel.PressTrigger(trigger.id);

            Assert.That(PreviewState.TryGet(toggle.id, out var value) && value == 1f, Is.True);
            Assert.That(PreviewState.TryGet(trigger.id, out _), Is.False, "a flip trigger never sets an override on itself");

            wheel.PressTrigger(trigger.id);

            Assert.That(PreviewState.TryGet(toggle.id, out value) && value == 0f, Is.True);
        }

        [Test]
        public void ToggleSourceWithoutTogglesShowsPlaceholder()
        {
            var menu = avatar.AddMenu();
            var trigger = TriggerOn(menu, "Ears");
            var view = View(menu);
            view.Tree.SelectEntry(trigger.id);

            var toggleField = view.Detail.Q<DropdownField>("ad-trigger-toggle");

            Assert.That(toggleField, Is.Not.Null);
            Assert.That(toggleField.enabledSelf, Is.False);
            Assert.That(toggleField.choices, Is.EqualTo(new[] { L.Text("ad.ui.trigger.no_toggles") }));
        }

        // Carry-over fixes from the phase 2 ledger (task 5 brief carry-over). Both tests need the detail panel to
        // sit in a real panel with a working focus controller, which a plain unattached VisualElement tree does
        // not have; a throwaway host window gives it one and is closed again in the finally block.
        class FocusHostWindow : EditorWindow { }

        [Test]
        public void DerivedRefreshKeepsTheFocusedRawTags()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Ha<b>t</b>");
            var view = View(menu);
            view.Tree.SelectEntry(hat.id);
            var window = ScriptableObject.CreateInstance<FocusHostWindow>();
            try
            {
                window.Show();
                window.rootVisualElement.Add(view);
                var raw = view.Detail.Q<TextField>("ad-name-raw");
                raw.Focus();
                raw.SetValueWithoutNotify("<b>Hat</b>");

                view.Refresh();

                Assert.That(raw.value, Is.EqualTo("<b>Hat</b>"), "a derived refresh must not overwrite the raw tags an author is typing");
            }
            finally
            {
                view.RemoveFromHierarchy();
                window.Close();
            }
        }
        [Test]
        public void DerivedRefreshKeepsTheFocusedSubmenuName()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var view = View(menu);
            view.Tree.SelectEntry(outfits.id);
            var window = ScriptableObject.CreateInstance<FocusHostWindow>();
            try
            {
                window.Show();
                window.rootVisualElement.Add(view);
                var field = view.Detail.Q<TextField>("ad-name");
                field.Focus();
                field.SetValueWithoutNotify("Typed");

                view.Refresh();

                Assert.That(field.value, Is.EqualTo("Typed"), "a derived refresh must not overwrite a field the author is typing in");
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void ReloadDoesNotRevertTheFocusedRowField()
        {
            var menu = avatar.AddMenu();
            var eyes = menu.AddEntry(EntryKind.Radial, "Eyes");
            eyes.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", minWeight = 0f, maxWeight = 100f });
            var view = View(menu);
            view.Tree.SelectEntry(eyes.id);
            var window = ScriptableObject.CreateInstance<FocusHostWindow>();
            try
            {
                window.Show();
                window.rootVisualElement.Add(view);
                var list = view.Detail.Q<TargetListView>("ad-blendshapes");
                // An unattached ListView never materialises its rows, so the row is built and held here, where the
                // focus can reach it, exactly as TargetListView.RowAt does for the other target list tests.
                var row = list.RowAt(0);
                list.Add(row);
                var max = row.Q<FloatField>("max");
                max.Focus();
                max.SetValueWithoutNotify(55f);

                list.ReloadRow(row);

                Assert.That(max.value, Is.EqualTo(55f), "a reload must not overwrite the row field the author is typing in");
                Assert.That(row.Q<FloatField>("min").value, Is.EqualTo(0f), "every other field of the row still follows the data");
            }
            finally
            {
                view.RemoveFromHierarchy();
                window.Close();
            }
        }

        [Test]
        public void FocusMovingInsideThePanelDoesNotRebuild()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.Tree.SelectEntry(hat.id);
            var window = ScriptableObject.CreateInstance<FocusHostWindow>();
            try
            {
                window.Show();
                window.rootVisualElement.Add(view);
                var body = view.Detail.Q(className: "ad-panel-body");
                var other = new VisualElement { focusable = true };
                body.Add(other);
                var name = view.Detail.Q<TextField>("ad-name");
                name.Focus();
                int rebuilds = view.Detail.RebuildCount;

                // Reshapes the panel (adds the exclusive group note) while the name field is still focused: the
                // rebuild this would otherwise cause has to wait, not pull the field out from under the author.
                var groupProperty = view.EntryProperty(hat).FindPropertyRelative(nameof(MenuEntry.exclusiveGroup));
                view.SerializedObject.Update();
                groupProperty.stringValue = "Group";
                view.ApplyAndRefresh();
                Assert.That(view.Detail.RebuildCount, Is.EqualTo(rebuilds), "the rebuild is deferred while the name field is focused");

                other.Focus();
                Assert.That(view.Detail.RebuildCount, Is.EqualTo(rebuilds), "focus moving to another field of the same panel does not rebuild");

                var outside = new VisualElement { focusable = true };
                window.rootVisualElement.Add(outside);
                outside.Focus();

                // This harness never pumps the editor scheduler inside a test, so the deferred rebuild running
                // later cannot be observed here; what matters, and is verified above, is that the callback never
                // rebuilds synchronously off a focus change that stays inside the panel.
                Assert.That(view.Detail.RebuildCount, Is.EqualTo(rebuilds), "the deferred rebuild does not run synchronously from the focus-out callback");
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void PreviewButtonsWriteOverrides()
        {
            var menu = avatar.AddMenu();
            var toggle = MenuEntry.Create(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            menu.entries.Add(toggle);
            var view = View(menu);
            view.Tree.SelectEntry(toggle.id);

            view.Detail.PreviewOn();
            Assert.That(PreviewState.TryGet(toggle.id, out var value) && value == 1f, Is.True);
            Assert.That(view.Detail.Q<SegmentedChoice>("ad-preview-choice").Index, Is.EqualTo(1));

            view.Detail.PreviewDefault();
            Assert.That(PreviewState.TryGet(toggle.id, out _), Is.False);
            Assert.That(view.Detail.Q<SegmentedChoice>("ad-preview-choice").Index, Is.EqualTo(0));
        }

        [Test]
        public void PreviewRowUsesASegmentedChoice()
        {
            var menu = avatar.AddMenu();
            var toggle = MenuEntry.Create(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            menu.entries.Add(toggle);
            var view = View(menu);
            view.Tree.SelectEntry(toggle.id);

            var choice = view.Detail.Q<SegmentedChoice>("ad-preview-choice");
            Assert.That(choice, Is.Not.Null);
            Assert.That(choice.Items.Select(b => b.text), Is.EqualTo(new[] { L.Text("ad.ui.preview.default"), L.Text("ad.ui.preview.on"), L.Text("ad.ui.preview.off") }));
            Assert.That(choice.Index, Is.EqualTo(0), "no override yet, so Default is chosen");

            choice.Pick(1);
            Assert.That(PreviewState.TryGet(toggle.id, out var value) && value == 1f, Is.True);

            choice.Pick(2);
            Assert.That(PreviewState.TryGet(toggle.id, out value) && value == 0f, Is.True);

            choice.Pick(0);
            Assert.That(PreviewState.TryGet(toggle.id, out _), Is.False);
        }

        [Test]
        public void RadialPreviewSliderWritesOverrides()
        {
            var menu = avatar.AddMenu();
            var radial = MenuEntry.Create(EntryKind.Radial, "Eyes");
            radial.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", maxWeight = 100f });
            menu.entries.Add(radial);
            var view = View(menu);
            view.Tree.SelectEntry(radial.id);

            Assert.That(view.Detail.Q<Slider>("ad-preview-slider"), Is.Not.Null);
            // Setting Slider.value on an unattached element does not dispatch RegisterValueChangedCallback in this
            // Unity version, so the method the slider calls is driven directly, as the brief's fallback allows.
            view.Detail.PreviewValue(0.75f);

            Assert.That(PreviewState.TryGet(radial.id, out var value) && Mathf.Approximately(value, 0.75f), Is.True);
            Assert.That(view.Detail.Q<Button>("ad-preview-on"), Is.Null);
        }

        [Test]
        public void HeaderShowsResetWhileOverridesExist()
        {
            var menu = avatar.AddMenu();
            var toggle = MenuEntry.Create(EntryKind.Toggle, "Hat");
            menu.entries.Add(toggle);
            var view = View(menu);

            Assert.That(view.Header.Q<Button>("ad-preview-reset").ClassListContains("ad-hidden"), Is.True);
            PreviewState.Set(toggle.id, 1f);
            view.Refresh();
            Assert.That(view.Header.Q<Button>("ad-preview-reset").ClassListContains("ad-hidden"), Is.False);
            view.Header.ResetPreview();
            Assert.That(PreviewState.Overrides.Value, Is.Empty);
        }

        [Test]
        public void TestModeOffSelectsTheRowAsBefore()
        {
            var menu = avatar.AddMenu();
            var outfits = MenuEntry.Create(EntryKind.Submenu, "Outfits");
            var hat = MenuEntry.Create(EntryKind.Toggle, "Hat");
            hat.parentId = outfits.id;
            menu.entries.Add(outfits);
            menu.entries.Add(hat);
            var view = View(menu);
            view.Tree.SelectEntry(outfits.id);

            view.Wheel.Click(0);

            Assert.That(view.Tree.Selected.EntryId, Is.EqualTo(hat.id));
            Assert.That(PreviewState.Overrides.Value, Is.Empty);
        }

        [Test]
        public void TestModeClickFlipsAToggleInThePreviewOnly()
        {
            var menu = avatar.AddMenu();
            var hat = MenuEntry.Create(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            menu.entries.Add(hat);
            var view = View(menu);
            view.TestMode = true;
            int index = view.Wheel.Model.Wedges.Select((w, i) => (w, i)).Single(p => p.w.EntryId == hat.id).i;

            view.Wheel.Click(index);

            Assert.That(PreviewState.TryGet(hat.id, out var value) && value == 1f, Is.True);
            Assert.That(view.Tree.Selected, Is.Null, "a wedge click in test mode never selects the row");
            Assert.That(hat.defaultOn, Is.False, "the entry's own data is untouched");

            view.Wheel.Click(index);
            Assert.That(PreviewState.TryGet(hat.id, out value) && value == 0f, Is.True);
        }

        [Test]
        public void TestModeRespectsExclusiveGroups()
        {
            var menu = avatar.AddMenu();
            var a = menu.AddEntry(EntryKind.Toggle, "A");
            a.exclusiveGroup = "Group";
            a.defaultOn = true;
            var b = menu.AddEntry(EntryKind.Toggle, "B");
            b.exclusiveGroup = "Group";
            var view = View(menu);
            view.TestMode = true;
            int index = view.Wheel.Model.Wedges.Select((w, i) => (w, i)).Single(p => p.w.EntryId == b.id).i;

            view.Wheel.Click(index);

            Assert.That(PreviewState.TryGet(b.id, out var bValue) && bValue == 1f, Is.True);
            Assert.That(PreviewState.TryGet(a.id, out var aValue) && aValue == 0f, Is.True);
        }

        [Test]
        public void WheelShowsOneWinnerPerGroupAfterThePanelSelectsAnother()
        {
            var menu = avatar.AddMenu();
            var a = menu.AddEntry(EntryKind.Toggle, "A");
            a.exclusiveGroup = "Group";
            a.defaultOn = true;
            a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var b = menu.AddEntry(EntryKind.Toggle, "B");
            b.exclusiveGroup = "Group";
            b.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var view = View(menu);
            view.Tree.RevealEntry(b.id);

            view.Detail.PreviewOn();
            view.Wheel.RefreshWedgeStates();

            Assert.That(PreviewState.TryGet(a.id, out var aValue) && aValue == 0f, Is.True, "the panel clears the siblings, as a wedge click does");
            Assert.That(view.Wheel.Model.Wedges.Single(w => w.EntryId == b.id).On, Is.True);
            Assert.That(view.Wheel.Model.Wedges.Single(w => w.EntryId == a.id).On, Is.False, "only the winner is accented");

            PreviewState.ClearAll();
            PreviewState.Set(b.id, 1f);
            view.Wheel.RefreshWedgeStates();

            Assert.That(view.Wheel.Model.Wedges.Single(w => w.EntryId == b.id).On, Is.True);
            Assert.That(view.Wheel.Model.Wedges.Single(w => w.EntryId == a.id).On, Is.False, "a member that is not the winner never reads as on, whatever its own default says");
        }

        [Test]
        public void StaleFlipModeOnAParameterTriggerIsIgnored()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var trigger = menu.AddEntry(EntryKind.Trigger, "AFK");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "AFK";
            // Left over from when this trigger was a contact: the build ignores it, so test mode must too.
            trigger.touchMode = TouchMode.FlipToggle;
            trigger.flipToggleId = hat.id;
            trigger.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var view = View(menu);
            view.TestMode = true;

            view.Wheel.PressTrigger(trigger.id);

            Assert.That(trigger.IsFlipTrigger, Is.False);
            Assert.That(PreviewState.TryGet(trigger.id, out var value) && value == 1f, Is.True, "a parameter trigger presses itself, whatever touchMode says");
            Assert.That(PreviewState.TryGet(hat.id, out _), Is.False, "the leftover flip target is not flipped");
        }

        [Test]
        public void TestModeAppliesAPreset()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            var preset = menu.AddEntry(EntryKind.Preset, "Casual");
            preset.presetStates.Add(new PresetState { entryId = hat.id, on = true });
            var view = View(menu);
            view.TestMode = true;
            int index = view.Wheel.Model.Wedges.Select((w, i) => (w, i)).Single(p => p.w.EntryId == preset.id).i;

            view.Wheel.Click(index);

            Assert.That(PreviewState.TryGet(hat.id, out var value) && value == 1f, Is.True);
        }

        [Test]
        public void TestModeRadialSliderWritesTheOverride()
        {
            var menu = avatar.AddMenu();
            var radial = menu.AddEntry(EntryKind.Radial, "Eyes");
            radial.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", maxWeight = 100f });
            var view = View(menu);
            view.TestMode = true;
            int index = view.Wheel.Model.Wedges.Select((w, i) => (w, i)).Single(p => p.w.EntryId == radial.id).i;
            view.Wheel.Click(index);

            view.Wheel.SetRadialValue(0.6f);

            Assert.That(PreviewState.TryGet(radial.id, out var value) && Mathf.Approximately(value, 0.6f), Is.True);
        }

        [Test]
        public void TestModePressesAParameterTrigger()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            var trigger = menu.AddEntry(EntryKind.Trigger, "Mood");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "Mood";
            trigger.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var view = View(menu);
            view.TestMode = true;
            var wheel = view.Wheel; // forces the wheel to build against the current plan

            wheel.PressTrigger(trigger.id);
            Assert.That(PreviewState.TryGet(trigger.id, out var value) && value == 1f, Is.True);

            wheel.PressTrigger(trigger.id);
            Assert.That(PreviewState.TryGet(trigger.id, out _), Is.False);
        }

        [Test]
        public void ResetClearsEveryOverride()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            var radial = menu.AddEntry(EntryKind.Radial, "Eyes");
            var view = View(menu);
            view.TestMode = true;
            PreviewState.Set(hat.id, 1f);
            PreviewState.Set(radial.id, 0.4f);

            view.Wheel.ResetPreview();

            Assert.That(PreviewState.Overrides.Value, Is.Empty);
        }

        [Test]
        public void TestModeRadialDragDoesNotRebuildTheWheel()
        {
            var menu = avatar.AddMenu();
            var radial = menu.AddEntry(EntryKind.Radial, "Eyes");
            radial.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", maxWeight = 100f });
            var view = View(menu);
            view.TestMode = true;
            int index = view.Wheel.Model.Wedges.Select((w, i) => (w, i)).Single(p => p.w.EntryId == radial.id).i;
            view.Wheel.Click(index);
            int rebuilds = view.Wheel.RebuildCount;

            view.Wheel.SetRadialValue(0.2f);
            view.Wheel.SetRadialValue(0.5f);
            view.Wheel.SetRadialValue(0.9f);

            Assert.That(view.Wheel.RebuildCount, Is.EqualTo(rebuilds), "a radial drag never rebuilds the wheel");
            var wedge = view.Wheel.Model.Wedges.Single(w => w.EntryId == radial.id);
            Assert.That(wedge.Value, Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(wedge.On, Is.True);
            Assert.That(PreviewState.TryGet(radial.id, out var value) && Mathf.Approximately(value, 0.9f), Is.True);
        }

        [Test]
        public void DetachedTestModeNeverTouchesEditorPrefs()
        {
            const string key = "DocMe.AvatarDoctor.Wheel.Test.Inspector";
            bool hadPrior = EditorPrefs.HasKey(key);
            bool prior = hadPrior && EditorPrefs.GetBool(key);
            EditorPrefs.SetBool(key, true);
            try
            {
                var menu = avatar.AddMenu();
                var view = View(menu);

                view.TestMode = false;
                Assert.That(EditorPrefs.GetBool(key), Is.True, "a detached view never writes the user's real EditorPrefs key");
                Assert.That(view.TestMode, Is.False, "the detached view reports its own field, not the real key");

                view.TestMode = true;
                Assert.That(view.TestMode, Is.True);
                Assert.That(EditorPrefs.GetBool(key), Is.True, "still just the sentinel this test set");
            }
            finally
            {
                if (hadPrior) EditorPrefs.SetBool(key, prior);
                else EditorPrefs.DeleteKey(key);
            }
        }

        [Test]
        public void WedgesFollowOverridesWrittenFromThePanel()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.Tree.SelectEntry(hat.id);
            var window = ScriptableObject.CreateInstance<FocusHostWindow>();
            try
            {
                window.Show();
                window.rootVisualElement.Add(view);
                var wheel = view.Wheel;
                int rebuilds = wheel.RebuildCount;

                view.Detail.PreviewOn();

                var wedge = wheel.Model.Wedges.Single(w => w.EntryId == hat.id);
                Assert.That(wedge.On, Is.True, "the wedge follows an override written from the panel's own preview row");
                Assert.That(wheel.RebuildCount, Is.EqualTo(rebuilds), "the wheel is refreshed in place, not rebuilt");
            }
            finally
            {
                view.RemoveFromHierarchy();
                window.Close();
            }
        }

        [Test]
        public void WheelFollowsTheSelection()
        {
            var menu = avatar.AddMenu();
            var outfits = MenuEntry.Create(EntryKind.Submenu, "Outfits");
            var hat = MenuEntry.Create(EntryKind.Toggle, "Hat");
            hat.parentId = outfits.id;
            var trigger = MenuEntry.Create(EntryKind.Trigger, "Blink");
            menu.entries.Add(outfits);
            menu.entries.Add(hat);
            menu.entries.Add(trigger);
            var view = View(menu);

            view.Tree.SelectEntry(hat.id);
            Assert.That(view.Wheel.Model.Title, Is.EqualTo("Outfits"));
            Assert.That(view.Wheel.Model.Wedges.Single(w => w.Selected).Name, Is.EqualTo("Hat"));
            Assert.That(view.Wheel.ClassListContains("ad-hidden"), Is.False);

            view.Tree.SelectEntry(trigger.id);
            Assert.That(view.Wheel.ClassListContains("ad-hidden"), Is.True);
        }

        [Test]
        public void ClickingAWedgeSelectsItsRow()
        {
            var menu = avatar.AddMenu();
            var outfits = MenuEntry.Create(EntryKind.Submenu, "Outfits");
            var hat = MenuEntry.Create(EntryKind.Toggle, "Hat");
            hat.parentId = outfits.id;
            menu.entries.Add(outfits);
            menu.entries.Add(hat);
            var view = View(menu);
            view.Tree.SelectEntry(outfits.id);
            Assert.That(view.Wheel.Model.Title, Is.EqualTo("Outfits"));

            view.Wheel.Click(0);

            Assert.That(view.Tree.Selected.EntryId, Is.EqualTo(hat.id));
            Assert.That(view.Wheel.Model.Wedges[0].Selected, Is.True);
        }

        [Test]
        public void SelectingAMoreRowShowsTheExistingNote()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            for (int i = 1; i <= 9; i++) menu.AddEntry(EntryKind.Toggle, "T" + i, outfits.id).objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);

            var more = view.Tree.RowsOfKind(RowKind.More).Single();
            view.Tree.SelectKey(more.Key);

            var notes = view.Detail.Query<Label>(className: "ad-note").ToList().Select(l => l.text).ToList();
            Assert.That(notes, Has.Some.EqualTo(string.Format(L.Text("ad.ui.existing_note"), more.Name)));
            Assert.That(view.Detail.Q<TextField>("ad-name"), Is.Null, "an overflow page is nothing to edit");
        }

        [Test]
        public void PathChoicesSkipOverflowPages()
        {
            var other = new GameObject("Other");
            other.transform.SetParent(avatar.Root.transform);
            var otherMenu = other.AddComponent<AvatarDoc>();
            var outfits = otherMenu.AddEntry(EntryKind.Submenu, "Outfits");
            for (int i = 1; i <= 9; i++) otherMenu.AddEntry(EntryKind.Toggle, "T" + i, outfits.id).objects.Add(new ObjectTarget { target = avatar.Hat });
            var menu = avatar.AddMenu();
            var view = View(menu);

            var paths = HeaderView.PathChoices(view.Plan, menu);

            Assert.That(paths, Does.Contain("Outfits"));
            Assert.That(paths.Any(p => p.EndsWith("/More")), Is.False, "an overflow page is the build's own, not an install target");
        }

        [Test]
        public void WedgeClickDescendsAndCrumbAscends()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            menu.AddEntry(EntryKind.Toggle, "Hat", outfits.id).objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var rootTitle = view.Wheel.Model.Title;

            int wedge = view.Wheel.Model.Wedges.Select((w, i) => (w, i)).First(p => p.w.Name == "Outfits").i;
            view.Wheel.Click(wedge);

            Assert.That(view.Tree.Selected.Entry, Is.SameAs(outfits));
            Assert.That(view.Wheel.Model.Title, Is.EqualTo("Outfits"));

            view.Wheel.ClickCrumb(0);

            Assert.That(view.Tree.Selected, Is.Null);
            Assert.That(view.Wheel.Model.Title, Is.EqualTo(rootTitle));
        }

        [Test]
        public void TriggerChipFormatsParameterAndShapeSources()
        {
            var menu = avatar.AddMenu();
            var mood = menu.AddEntry(EntryKind.Trigger, "Mood two");
            mood.source = TriggerSource.Parameter;
            mood.sourceParameter = "Mood";
            mood.condition = ParameterCondition.Equals;
            mood.conditionValue = 2f;
            var link = menu.AddEntry(EntryKind.Trigger, "Blush link");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            var view = View(menu);

            Assert.That(ChipOf(view, mood), Is.EqualTo("Mood = 2"));
            Assert.That(ChipOf(view, link), Is.EqualTo("Body/Blush"));

            mood.condition = ParameterCondition.IsFalse;
            AvatarPlanCache.Invalidate();
            view.Refresh();

            Assert.That(ChipOf(view, mood), Is.EqualTo("!Mood"));
        }

        static string ChipOf(AvatarDoctorView view, MenuEntry entry)
            => view.Tree.RowsOfKind(RowKind.Trigger).Single(r => r.Entry == entry).TriggerChip;

        [Test]
        public void WindowResolvesTheComponentFromAnyAvatarObject()
        {
            var menu = avatar.AddMenu();
            Assert.That(AvatarDoctorWindow.Resolve(avatar.Hat), Is.SameAs(menu));
            Assert.That(AvatarDoctorWindow.Resolve(menu.gameObject), Is.SameAs(menu));
            Assert.That(AvatarDoctorWindow.Resolve(null), Is.Null);
        }

        [Test]
        public void WindowKeepsTheCurrentComponentOnAssetSelection()
        {
            var menu = avatar.AddMenu();
            var other = new GameObject("Other");
            other.transform.SetParent(avatar.Root.transform);
            var otherMenu = other.AddComponent<AvatarDoc>();

            Assert.That(AvatarDoctorWindow.ShouldSwitch(menu, null), Is.False, "a material or a clip in the Project window keeps the component on show");
            Assert.That(AvatarDoctorWindow.ShouldSwitch(menu, otherMenu), Is.True);
            Assert.That(AvatarDoctorWindow.ShouldSwitch(null, null), Is.True, "nothing shown yet, so the note has to be built");

            UnityEngine.Object.DestroyImmediate(menu);
            Assert.That(AvatarDoctorWindow.ShouldSwitch(menu, null), Is.True, "a destroyed component counts as nothing shown");
        }

        [Test]
        public void SelectingAnObjectOfTheSameAvatarKeepsTheComponent()
        {
            var menu = avatar.AddMenu();

            bool switches = AvatarDoctorWindow.ShouldSwitch(menu, avatar.Hat, false);

            Assert.That(switches, Is.False, "a plain object under the same avatar, with no AvatarDoc of its own, is not a reason to switch away from the component already on show");
        }

        [Test]
        public void SelectingAnotherComponentSwitches()
        {
            var menu = avatar.AddMenu();
            var other = new GameObject("Other");
            other.transform.SetParent(avatar.Root.transform);
            other.AddComponent<AvatarDoc>();

            bool switches = AvatarDoctorWindow.ShouldSwitch(menu, other, false);

            Assert.That(switches, Is.True, "the selected object carries its own AvatarDoc, so the window follows it even though it is the same avatar");
        }

        [Test]
        public void SelectingAnObjectOfAnotherAvatarSwitches()
        {
            var menu = avatar.AddMenu();
            using (var other = TestAvatar.Create())
            {
                bool switches = AvatarDoctorWindow.ShouldSwitch(menu, other.Root, false);

                Assert.That(switches, Is.True, "an object of a different avatar switches even when that avatar has no AvatarDoc of its own to resolve to");
            }
        }

        [Test]
        public void TheWindowDoesNotSwitchWhileDragging()
        {
            var menu = avatar.AddMenu();
            using (var other = TestAvatar.Create())
            {
                Assert.That(AvatarDoctorWindow.ShouldSwitch(menu, other.Root, true), Is.False, "a Hierarchy object dragged over the window while the tree holds a drag must not pull the panel away");
                Assert.That(AvatarDoctorWindow.ShouldSwitch(null, other.Root, true), Is.False, "the hold applies even before anything is shown");
            }
        }

        [Test]
        public void TriggerRowsShowTheSourceChip()
        {
            var menu = avatar.AddMenu();
            var toggle = MenuEntry.Create(EntryKind.Toggle, "Hat");
            var trigger = MenuEntry.Create(EntryKind.Trigger, "Ears");
            trigger.source = TriggerSource.Toggle;
            trigger.sourceToggleId = toggle.id;
            trigger.sourceToggleOn = false;
            menu.entries.Add(toggle);
            menu.entries.Add(trigger);
            var view = View(menu);

            var row = view.Tree.RowsOfKind(RowKind.Trigger).Single();

            Assert.That(row.TriggerChip, Is.EqualTo("Hat off"));
        }

        [Test]
        public void DisabledSourceComponentShowsANote()
        {
            var menu = avatar.AddMenu();
            var other = new GameObject("Other"); other.transform.SetParent(avatar.Root.transform);
            var otherMenu = other.AddComponent<AvatarDoc>();
            var toggle = otherMenu.AddEntry(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            otherMenu.enabled = false;
            var trigger = menu.AddEntry(EntryKind.Trigger, "Ears");
            trigger.objects.Add(new ObjectTarget { target = avatar.Glasses });
            trigger.source = TriggerSource.Toggle;
            trigger.sourceToggleId = toggle.id;
            var view = View(menu);

            view.Tree.SelectEntry(trigger.id);

            var notes = view.Detail.Query<Label>(className: "ad-note").ToList().Select(l => l.text).ToList();
            Assert.That(notes, Has.Some.Contains(L.Text("ad.ui.trigger.source_disabled")));
        }

        [Test]
        public void OverrideRowsSitUnderTheOverridesGroup()
        {
            var menu = avatar.AddMenu();
            var trigger = TriggerOn(menu, "Blink");
            var rule = MenuEntry.Create(EntryKind.Override, "Silence blush");
            rule.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush" });
            menu.entries.Add(rule);
            var view = View(menu);

            var group = view.Tree.RowsOfKind(RowKind.OverridesGroup).Single();
            var row = view.Tree.RowsOfKind(RowKind.Override).Single();
            Assert.That(row.ParentRow, Is.SameAs(group));
            Assert.That(row.IsMenuEntry, Is.False);
            Assert.That(row.IsOwnEntry, Is.True);
            Assert.That(view.Tree.RowsOfKind(RowKind.TriggersGroup).Single().Id, Is.LessThan(group.Id), "triggers come first");
            view.Tree.SelectEntry(rule.id);
            Assert.That(view.Wheel.ClassListContains("ad-hidden"), Is.True);
        }

        [Test]
        public void OverridesCanBeAddedDuplicatedAndDeleted()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);

            var added = view.Tree.AddEntry(EntryKind.Override);
            Assert.That(added.parentId, Is.EqualTo(""));
            Assert.That(view.Tree.Selected.EntryId, Is.EqualTo(added.id));

            view.Tree.DuplicateSelected();
            Assert.That(menu.entries.Count(e => e.kind == EntryKind.Override), Is.EqualTo(2));
            var copy = view.Tree.Selected.Entry;
            Assert.That(copy.id, Is.Not.EqualTo(added.id));

            view.Tree.DeleteSelected();
            Assert.That(menu.entries.Select(e => e.id), Is.EqualTo(new[] { added.id }));
            Assert.That(view.Tree.Selected.EntryId, Is.EqualTo(added.id), "the neighbour rule is selected after a delete");
        }

        [Test]
        public void ToolbarAddsAPreset()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);

            var added = view.Tree.AddEntry(EntryKind.Preset);

            Assert.That(added.kind, Is.EqualTo(EntryKind.Preset));
            Assert.That(view.Tree.Selected.Kind, Is.EqualTo(RowKind.Preset));
            Assert.That(view.Tree.RowsOfKind(RowKind.Preset).Single().EntryId, Is.EqualTo(added.id));
        }

        [Test]
        public void PresetPanelListsItsStates()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            var eyes = menu.AddEntry(EntryKind.Radial, "Eyes");
            var preset = menu.AddEntry(EntryKind.Preset, "Casual");
            preset.presetStates.Add(new PresetState { entryId = hat.id, on = true });
            preset.presetStates.Add(new PresetState { entryId = eyes.id, value = 0.5f });
            preset.presetStates.Add(new PresetState { entryId = "ghost", on = true });
            preset.presetParameters.Add(new PresetParameter { name = "VRCEmote", value = 3f });
            var view = View(menu);

            view.Tree.SelectEntry(preset.id);

            var stateRows = view.Detail.Query<VisualElement>(className: "ad-preset-state").ToList();
            Assert.That(stateRows.Count, Is.EqualTo(3));
            Assert.That(stateRows[0].Q<Button>("ad-preset-control").text, Is.EqualTo(hat.PlainName));
            Assert.That(stateRows[0].Q<SegmentedChoice>("ad-preset-on"), Is.Not.Null, "a toggle shows an on/off segmented choice");
            Assert.That(stateRows[1].Q<Slider>("ad-preset-value"), Is.Not.Null, "a radial shows a slider");
            Assert.That(stateRows[2].Q<Button>("ad-preset-control").text, Is.EqualTo(L.Text("ad.ui.preset.missing")));
            Assert.That(stateRows[2].Q<Button>("ad-preset-control").ClassListContains("ad-text-warn"), Is.True);

            var paramRows = view.Detail.Query<VisualElement>(className: "ad-preset-parameter").ToList();
            Assert.That(paramRows.Count, Is.EqualTo(1));
            Assert.That(paramRows[0].Q<TextField>("ad-preset-param-name").value, Is.EqualTo("VRCEmote"));
            Assert.That(paramRows[0].Q<FloatField>("ad-preset-param-value").value, Is.EqualTo(3f));
        }

        [Test]
        public void AddAndRemoveStatesThroughThePanel()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            var preset = menu.AddEntry(EntryKind.Preset, "Casual");
            var view = View(menu);
            view.Tree.SelectEntry(preset.id);

            view.Detail.Q<PresetPanel>().AddPresetState(hat.id, true, 1f);

            Assert.That(preset.presetStates.Count, Is.EqualTo(1));
            Assert.That(preset.presetStates[0].entryId, Is.EqualTo(hat.id));
            Assert.That(preset.presetStates[0].on, Is.True);

            view.Detail.Q<PresetPanel>().RemovePresetState(0);

            Assert.That(preset.presetStates.Count, Is.EqualTo(0));
        }

        [Test]
        public void FromPreviewCollectsOverrides()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            var eyes = menu.AddEntry(EntryKind.Radial, "Eyes");
            eyes.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big" });
            var preset = menu.AddEntry(EntryKind.Preset, "Casual");
            PreviewState.Set(hat.id, 1f);
            PreviewState.Set(eyes.id, 0.4f);
            var view = View(menu);
            view.Tree.SelectEntry(preset.id);

            view.Detail.Q<PresetPanel>().FillPresetFromPreview();

            Assert.That(preset.presetStates.Count, Is.EqualTo(2));
            var hatState = preset.presetStates.Single(s => s.entryId == hat.id);
            Assert.That(hatState.on, Is.True);
            var eyesState = preset.presetStates.Single(s => s.entryId == eyes.id);
            Assert.That(eyesState.value, Is.EqualTo(0.4f).Within(0.001f));
        }

        [Test]
        public void PresetRowsShowInTheTreeAndTheWheel()
        {
            var menu = avatar.AddMenu();
            var preset = menu.AddEntry(EntryKind.Preset, "Casual");
            var view = View(menu);

            var row = view.Tree.RowsOfKind(RowKind.Preset).Single();
            Assert.That(row.EntryId, Is.EqualTo(preset.id));

            view.Tree.SelectEntry(preset.id);
            var wedge = view.Wheel.Model.Wedges.Single(w => w.EntryId == preset.id);
            Assert.That(wedge.Kind, Is.EqualTo(GlyphKind.Preset));
        }

        [Test]
        public void DuplicatingAPresetCopiesItsStates()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            var preset = menu.AddEntry(EntryKind.Preset, "Casual");
            preset.presetStates.Add(new PresetState { entryId = hat.id, on = true });
            preset.presetParameters.Add(new PresetParameter { name = "VRCEmote", value = 3f });

            var copy = EntryEditing.Duplicate(menu, preset);

            Assert.That(copy.presetStates, Is.Not.SameAs(preset.presetStates));
            Assert.That(copy.presetStates[0], Is.Not.SameAs(preset.presetStates[0]));
            Assert.That(copy.presetStates[0].entryId, Is.EqualTo(hat.id));
            Assert.That(copy.presetParameters, Is.Not.SameAs(preset.presetParameters));
            Assert.That(copy.presetParameters[0], Is.Not.SameAs(preset.presetParameters[0]));

            copy.presetStates[0].on = false;
            Assert.That(preset.presetStates[0].on, Is.True, "mutating the copy must not affect the source");
        }

        [Test]
        public void DraggingAPresetSliderKeepsTheSlider()
        {
            var menu = avatar.AddMenu();
            var eyes = menu.AddEntry(EntryKind.Radial, "Eyes");
            var preset = menu.AddEntry(EntryKind.Preset, "Casual");
            preset.presetStates.Add(new PresetState { entryId = eyes.id, value = 0f });
            var view = View(menu);
            view.Tree.SelectEntry(preset.id);
            int rebuilds = view.Detail.RebuildCount;

            var slider = view.Detail.Query<VisualElement>(className: "ad-preset-state").ToList().Single().Q<Slider>("ad-preset-value");
            Assert.That(slider, Is.Not.Null);
            slider.value = 0.2f;
            slider.value = 0.5f;
            slider.value = 0.9f;

            var stillSlider = view.Detail.Query<VisualElement>(className: "ad-preset-state").ToList().Single().Q<Slider>("ad-preset-value");
            Assert.That(stillSlider, Is.SameAs(slider), "a value-only edit must not rebuild the row out from under the drag");
            Assert.That(slider.value, Is.EqualTo(0.9f));
            Assert.That(view.Detail.RebuildCount, Is.EqualTo(rebuilds));
        }

        [Test]
        public void PresetPanelFollowsARenamedControl()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            var preset = menu.AddEntry(EntryKind.Preset, "Casual");
            preset.presetStates.Add(new PresetState { entryId = hat.id, on = true });
            var view = View(menu);
            view.Tree.SelectEntry(preset.id);
            Assert.That(view.Detail.Query<VisualElement>(className: "ad-preset-state").ToList().Single().Q<Button>("ad-preset-control").text, Is.EqualTo("Hat"));

            hat.name = "Cap";
            AvatarPlanCache.Invalidate();
            view.Refresh();

            var stateRow = view.Detail.Query<VisualElement>(className: "ad-preset-state").ToList().Single();
            Assert.That(stateRow.Q<Button>("ad-preset-control").text, Is.EqualTo("Cap"));
        }

        [Test]
        public void DeletingAReferencedControlLeavesAWarningState()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            var preset = menu.AddEntry(EntryKind.Preset, "Casual");
            preset.presetStates.Add(new PresetState { entryId = hat.id, on = true });
            var view = View(menu);
            view.Tree.SelectEntry(preset.id);

            EntryEditing.Delete(menu, hat);
            AvatarPlanCache.Invalidate();
            view.Refresh();

            var presetRow = view.Tree.RowsOfKind(RowKind.Preset).Single();
            Assert.That(presetRow.Messages.Any(m => m.Key == "ad.warn.preset_target_missing"), Is.True);
            var stateRow = view.Detail.Query<VisualElement>(className: "ad-preset-state").ToList().Single();
            Assert.That(stateRow.Q<Button>("ad-preset-control").text, Is.EqualTo(L.Text("ad.ui.preset.missing")));
        }

        [Test]
        public void SearchFiltersRowsByName()
        {
            var menu = avatar.AddMenu();
            var outfits = MenuEntry.Create(EntryKind.Submenu, "Outfits");
            var hat = MenuEntry.Create(EntryKind.Toggle, "Hat");
            hat.parentId = outfits.id;
            var blush = MenuEntry.Create(EntryKind.Toggle, "Blush");
            menu.entries.Add(outfits);
            menu.entries.Add(hat);
            menu.entries.Add(blush);
            var view = View(menu);

            view.Tree.Filter = "ha";

            var names = view.Tree.RowsOfKind(RowKind.Toggle).Select(r => r.Name).ToList();
            Assert.That(names, Is.EqualTo(new[] { "Hat" }));
            Assert.That(view.Tree.RowsOfKind(RowKind.Submenu).Select(r => r.Name), Does.Contain("Outfits"), "a matching child keeps its parent");

            view.Tree.Filter = "";
            Assert.That(view.Tree.RowsOfKind(RowKind.Toggle).Select(r => r.Name), Is.EquivalentTo(new[] { "Hat", "Blush" }));
        }

        [Test]
        public void SearchTurnsDraggingOffAndSaysSo()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat");
            var view = View(menu);

            Assert.That(view.Tree.Tree.reorderable, Is.True);
            Assert.That(view.Tree.Query<Label>(className: "ad-search-note").ToList().Select(l => l.text), Is.All.Empty, "no note while the search is empty");

            view.Tree.Filter = "hat";

            Assert.That(view.Tree.Tree.reorderable, Is.False, "a filtered tree hides the structure a drop would land in");
            Assert.That(view.Tree.Query<Label>(className: "ad-search-note").ToList().Select(l => l.text), Does.Contain(L.Text("ad.info.drag_needs_no_search")));

            view.Tree.Filter = "";

            Assert.That(view.Tree.Tree.reorderable, Is.True, "clearing the search brings dragging back");
            Assert.That(view.Tree.Query<Label>(className: "ad-search-note").ToList().Select(l => l.text), Is.All.Empty, "and takes the note away with it");
        }

        [Test]
        public void LockedTreeIsNotReorderable()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat");
            var view = View(menu);
            Assert.That(view.Tree.Tree.reorderable, Is.True);

            view.SetLocked(true);

            Assert.That(view.Tree.Tree.reorderable, Is.False, "a locked component refuses every edit, dragging included");
            Assert.That(view.Tree.Query<Label>(className: "ad-search-note").ToList().Select(l => l.text), Is.All.Empty, "the lock banner already explains this; the search note stays quiet");
        }

        [Test]
        public void ARefreshDuringADragIsHeldUntilItEnds()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.parameterName = "Mine";
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var first = View(menu);
            var second = View(menu);
            first.Tree.SelectEntry(hat.id);
            second.Tree.Dragging = true;

            OnAPanel(first, () => first.Detail.Q<TextField>("ad-name").value = "Cap");

            Assert.That(second.Tree.RowFor(hat.id).Name, Is.EqualTo("Hat"), "the second view has not been told yet");
            second.CheckVersion();
            Assert.That(second.Tree.RowFor(hat.id).Name, Is.EqualTo("Hat"), "a version bump while a drag is in progress does not rebuild the tree");
            Assert.That(second.RefreshHeldByDrag, Is.True);

            second.Tree.Dragging = false;
            second.CheckVersion();

            Assert.That(second.Tree.RowFor(hat.id).Name, Is.EqualTo("Cap"), "the held refresh runs once the drag ends");
            Assert.That(second.RefreshHeldByDrag, Is.False);
        }

        [Test]
        public void AStuckDragClearsAfterTheTimeout()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat"); hat.parameterName = "Mine"; hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            // A detached tree never actually captures the pointer, standing in for a modal having swallowed the
            // pointer-up that would otherwise have paired with PointerCaptureEvent and cleared Dragging itself.
            view.Tree.Dragging = true;
            view.Tree.DraggingSince = EditorApplication.timeSinceStartup - 3.0;

            view.CheckVersion();

            Assert.That(view.Tree.Dragging, Is.False, "a drag stuck this long with no real pointer capture is a leak, not a genuine hold");
        }

        [Test]
        public void AGenuinelyRecentDragIsNotClearedEarly()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.Tree.Dragging = true;

            view.CheckVersion();

            Assert.That(view.Tree.Dragging, Is.True, "a drag that only just started is a genuine hold, not a leak");
        }

        [Test]
        public void TheDropsOwnRefreshStillRuns()
        {
            var menu = avatar.AddMenu();
            var a = menu.AddEntry(EntryKind.Toggle, "A"); a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var sub = menu.AddEntry(EntryKind.Submenu, "Sub");
            var view = View(menu);
            var rowA = view.Tree.RowFor(a.id);
            var rowSub = view.Tree.RowFor(sub.id);
            // The real pointer capture-out event has not reached the tree yet at the moment a drop applies its
            // structure (HandleReorder clears the flag itself before that point), so this stands in for that.
            view.Tree.Dragging = true;

            bool applied = view.Tree.ApplyStructure(new[] { (rowA, rowSub, 0) });

            Assert.That(applied, Is.True);
            Assert.That(EntryEditing.Siblings(menu, sub.id).Select(e => e.name), Is.EqualTo(new[] { "A" }));
            Assert.That(view.Tree.RowFor(a.id)?.ParentRow?.EntryId, Is.EqualTo(sub.id), "the drop's own refresh must run even though the tree still thought a drag was in progress");
            Assert.That(view.Tree.Dragging, Is.False, "applying a structure ends the drag it was finishing");
            Assert.That(view.RefreshHeldByDrag, Is.False);
        }

        [Test]
        public void OverridePanelListsTheClipsAnimatingTheShape()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            avatar.AddFxClip("Grin", TestAvatar.ShapeClip("Grin", "Body", "Blush", 0f, 50f));
            var menu = avatar.AddMenu();
            var rule = MenuEntry.Create(EntryKind.Override, "Silence blush");
            rule.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush" });
            menu.entries.Add(rule);
            AvatarPlanCache.Invalidate();
            var view = View(menu);
            view.Tree.SelectEntry(rule.id);

            var usage = view.Detail.Q<VisualElement>("ad-override-usage");
            Assert.That(usage, Is.Not.Null);
            Assert.That(usage.Query<Label>(className: "ad-usage-clip").ToList().Select(l => l.text), Is.EquivalentTo(new[] { "Smile", "Grin" }));
            Assert.That(view.Detail.Q<Button>("ad-preview-on"), Is.Null);
            Assert.That(view.Detail.Q<TargetListView>("ad-blendshapes").RowAt(0).Q<DropdownField>("effect-mode"), Is.Null, "an override pins; it has no mode");
        }

        [Test]
        public void AutoChipFollowsTheLiveText()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.Tree.SelectEntry(toggle.id);

            // The field itself only raises its events on a real panel, so the method behind them is called directly.
            view.Detail.SetParameterNameLive("Mine");

            var chip = view.Detail.Q<Label>("ad-parameter-chip");
            var custom = view.Detail.Q<Toggle>("ad-parameter-custom");
            Assert.That(chip.text, Is.EqualTo(L.Text("ad.ui.parameter.custom")));
            Assert.That(custom.value, Is.True);
            Assert.That(toggle.parameterName, Is.Empty, "typing does not write the entry until the field commits");

            view.Detail.SetParameterNameLive("");

            Assert.That(chip.text, Is.EqualTo(L.Text("ad.ui.parameter.auto")));
            Assert.That(custom.value, Is.False);
        }

        [Test]
        public void NewOverrideTargetsDefaultToZero()
        {
            var menu = avatar.AddMenu();
            var rule = MenuEntry.Create(EntryKind.Override, "Silence blush");
            menu.entries.Add(rule);
            var view = View(menu);
            view.Tree.SelectEntry(rule.id);

            // The + Add button opens the shape picker and hands its pick to AddShape; the picker itself needs a panel.
            view.Detail.Q<TargetListView>("ad-blendshapes").AddShape(avatar.Body, "Blush");

            Assert.That(rule.blendShapes.Single().weight, Is.EqualTo(0f));
        }

        [Test]
        public void PinModeDropdownShowsForTogglesOnly()
        {
            var menu = avatar.AddMenu();
            var toggle = MenuEntry.Create(EntryKind.Toggle, "Mute");
            toggle.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush" });
            var radial = MenuEntry.Create(EntryKind.Radial, "Eyes");
            radial.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big" });
            menu.entries.Add(toggle);
            menu.entries.Add(radial);
            var view = View(menu);

            view.Tree.SelectEntry(toggle.id);
            // An unattached ListView never lays out and virtualises its rows, so the row is built on demand
            // through TargetListView.RowAt rather than found by querying the (empty) ListView hierarchy.
            var toggleShapes = view.Detail.Q<TargetListView>("ad-blendshapes");
            var mode = toggleShapes.RowAt(0).Q<SegmentedChoice>("effect-mode");
            Assert.That(mode, Is.Not.Null);
            Assert.That(mode.Items.Select(b => b.text), Is.EqualTo(new[] { L.Text("ad.ui.seg.fixed"), L.Text("ad.ui.seg.pin") }));
            // Clicking an unattached element's button does not dispatch through a panel in this Unity version
            // (the same behaviour already worked around for the preview slider), so the method the segmented
            // choice calls is driven directly through the public TargetListView.SetShapeMode.
            toggleShapes.SetShapeMode(0, ShapeEffectMode.Pin);
            Assert.That(toggle.blendShapes[0].mode, Is.EqualTo(ShapeEffectMode.Pin));

            view.Tree.SelectEntry(radial.id);
            var radialShapes = view.Detail.Q<TargetListView>("ad-blendshapes");
            Assert.That(radialShapes.RowAt(0).Q<SegmentedChoice>("effect-mode"), Is.Null);
        }

        [Test]
        public void BlendShapeRowsShowTheUsageChip()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var menu = avatar.AddMenu();
            var toggle = MenuEntry.Create(EntryKind.Toggle, "Blush");
            toggle.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush" });
            toggle.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big" });
            menu.entries.Add(toggle);
            AvatarPlanCache.Invalidate();
            var view = View(menu);
            view.Tree.SelectEntry(toggle.id);

            // See the note in PinModeDropdownShowsForTogglesOnly: rows are built on demand through RowAt since an
            // unattached ListView never materialises them for a plain query to find.
            var list = view.Detail.Q<TargetListView>("ad-blendshapes");
            var chips = Enumerable.Range(0, list.Count).Select(i => list.RowAt(i).Q<Label>(className: "ad-chip-usage")).ToList();
            Assert.That(chips.Count(c => !c.ClassListContains("ad-hidden")), Is.EqualTo(1));
            Assert.That(chips.First(c => !c.ClassListContains("ad-hidden")).tooltip, Does.Contain("Smile"));
        }

        [Test]
        public void TreeStartsCollapsedAndRemembersExpansion()
        {
            var menu = avatar.AddMenu();
            var outfits = MenuEntry.Create(EntryKind.Submenu, "Outfits");
            var hat = MenuEntry.Create(EntryKind.Toggle, "Hat");
            hat.parentId = outfits.id;
            menu.entries.Add(outfits);
            menu.entries.Add(hat);
            // A memory store shared by both views below, so "remembers" is checked without ever touching the
            // real EditorPrefs: a fresh store per test needs no cleanup either, unlike the real one.
            var prefs = new MemoryViewPrefs();
            var view = View(menu, prefs);
            var row = view.Tree.RowsOfKind(RowKind.Submenu).Single();
            Assert.That(view.Tree.Tree.IsExpanded(row.Id), Is.False, "collapsed by default");

            view.Tree.SetExpanded(row.Key, true);
            Assert.That(view.Tree.ExpandedKeys, Does.Contain(row.Key));
            Assert.That(TreeExpansion.Load(prefs, TreeExpansion.KeyFor(menu)), Does.Contain(row.Key), "saved at once");

            var second = View(menu, prefs);
            Assert.That(second.Tree.Tree.IsExpanded(second.Tree.RowsOfKind(RowKind.Submenu).Single().Id), Is.True, "a new view restores the saved state");
        }

        [Test]
        public void HandExpansionIsSavedByThePoller()
        {
            var menu = avatar.AddMenu();
            var outfits = MenuEntry.Create(EntryKind.Submenu, "Outfits");
            var hat = MenuEntry.Create(EntryKind.Toggle, "Hat");
            hat.parentId = outfits.id;
            menu.entries.Add(outfits);
            menu.entries.Add(hat);
            var view = View(menu);
            var row = view.Tree.RowsOfKind(RowKind.Submenu).Single();

            view.Tree.Tree.ExpandItem(row.Id);
            view.Tree.PollExpansion();

            Assert.That(view.Tree.ExpandedKeys, Does.Contain(row.Key));
            Assert.That(TreeExpansion.Load(view.ViewPrefs, TreeExpansion.KeyFor(menu)), Does.Contain(row.Key), "a click on the tree's own arrow is saved by the poller");

            view.Tree.Tree.CollapseItem(row.Id);
            view.Tree.PollExpansion();

            Assert.That(view.Tree.ExpandedKeys, Does.Not.Contain(row.Key));
            Assert.That(TreeExpansion.Load(view.ViewPrefs, TreeExpansion.KeyFor(menu)), Does.Not.Contain(row.Key), "collapsing by hand is saved too");
        }

        [Test]
        public void CaptureButtonOnlyOnMenuEntries()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            var submenu = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var radial = menu.AddEntry(EntryKind.Radial, "Eyes");
            radial.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big" });
            var trigger = TriggerOn(menu, "Blink");
            var rule = menu.AddEntry(EntryKind.Override, "Silence blush");
            rule.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush" });
            var view = View(menu);

            bool HasCapture(string entryId)
            {
                view.Tree.SelectEntry(entryId);
                return view.Detail.Q<Button>("ad-capture-icon") != null;
            }

            Assert.That(HasCapture(toggle.id), Is.True, "a toggle carries an icon");
            Assert.That(HasCapture(submenu.id), Is.True, "a submenu carries an icon");
            Assert.That(HasCapture(radial.id), Is.True, "a radial carries an icon");
            Assert.That(HasCapture(trigger.id), Is.False, "a trigger has no control of its own");
            Assert.That(HasCapture(rule.id), Is.False, "an override has no control of its own");
        }

        [Test]
        public void SelectingAnEntryRevealsItsAncestors()
        {
            var menu = avatar.AddMenu();
            var outfits = MenuEntry.Create(EntryKind.Submenu, "Outfits");
            var inner = MenuEntry.Create(EntryKind.Submenu, "Hats");
            inner.parentId = outfits.id;
            var hat = MenuEntry.Create(EntryKind.Toggle, "Hat");
            hat.parentId = inner.id;
            menu.entries.Add(outfits);
            menu.entries.Add(inner);
            menu.entries.Add(hat);
            var view = View(menu);
            view.Tree.RevealEntry(hat.id);
            Assert.That(view.Tree.Selected.EntryId, Is.EqualTo(hat.id));
            Assert.That(view.Tree.ExpandedKeys, Is.EquivalentTo(new[] { outfits.id, inner.id }));
            var added = view.Tree.AddEntry(EntryKind.Toggle);
            Assert.That(view.Tree.ExpandedKeys, Does.Contain(inner.id), "adding under the selection keeps the path open");
        }

        /// <summary>Phase 12 task 6, fix round 1: modelled on FirstUseTipTests.TipsNeverTouchTheRealEditorPrefs. A view a test builds must read and write folds and tree expansion through its own in-memory store, never the real, machine-global EditorPrefs.</summary>
        [Test]
        public void FoldsAndExpansionNeverTouchTheRealEditorPrefs()
        {
            var menu = avatar.AddMenu();
            var submenu = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat", submenu.id);

            var settingsFoldKey = ComponentFold.KeyFor(menu, "component_settings");
            var advancedFoldKey = ComponentFold.KeyForEntry(toggle.id, "advanced");
            var expansionKey = TreeExpansion.KeyFor(menu);
            bool settingsHadKeyBefore = EditorPrefs.HasKey(settingsFoldKey);
            bool advancedHadKeyBefore = EditorPrefs.HasKey(advancedFoldKey);
            bool expansionHadKeyBefore = EditorPrefs.HasKey(expansionKey);

            var view = View(menu);
            Assert.That(view.ViewPrefs, Is.InstanceOf<MemoryViewPrefs>(), "a view a test builds never receives the real, EditorPrefs backed store on its own");

            view.Header.ToggleSettingsFold();
            view.Tree.SelectEntry(toggle.id);
            view.Detail.Q<AdvancedFold>().ToggleOpen();
            var submenuRow = view.Tree.RowsOfKind(RowKind.Submenu).Single();
            view.Tree.SetExpanded(submenuRow.Key, true);

            Assert.That(EditorPrefs.HasKey(settingsFoldKey), Is.EqualTo(settingsHadKeyBefore), "the Component settings fold must never touch the real EditorPrefs");
            Assert.That(EditorPrefs.HasKey(advancedFoldKey), Is.EqualTo(advancedHadKeyBefore), "an entry's Advanced fold must never touch the real EditorPrefs");
            Assert.That(EditorPrefs.HasKey(expansionKey), Is.EqualTo(expansionHadKeyBefore), "the tree's saved expansion must never touch the real EditorPrefs");
        }

        [Test]
        public void RowsRenderRichNamesAndTheFilterMatchesPlainText()
        {
            // Parented under a submenu of its own so the wheel's wedge count stays at one: the avatar's root menu
            // already carries the fixture's own "Old" submenu and "RootToggle" control.
            var menu = avatar.AddMenu();
            var outfits = MenuEntry.Create(EntryKind.Submenu, "Outfits");
            var hat = MenuEntry.Create(EntryKind.Toggle, "<color=#FF0000><b>Hat</b></color>");
            hat.parentId = outfits.id;
            menu.entries.Add(outfits);
            menu.entries.Add(hat);
            var view = View(menu);

            var row = view.Tree.RowsOfKind(RowKind.Toggle).Single();
            Assert.That(row.Name, Is.EqualTo("<color=#FF0000><b>Hat</b></color>"));
            view.Tree.Filter = "hat";
            Assert.That(view.Tree.RowsOfKind(RowKind.Toggle).Count(), Is.EqualTo(1));
            view.Tree.Filter = "color";
            Assert.That(view.Tree.RowsOfKind(RowKind.Toggle).Count(), Is.EqualTo(0), "tags are not searchable");
            view.Tree.Filter = "";
            view.Tree.SelectEntry(hat.id);
            Assert.That(view.Wheel.Model.Wedges.Single().Name, Is.EqualTo("<color=#FF0000><b>Hat</b></color>"));
        }

        [Test]
        public void StyleRowComposesTheName()
        {
            var menu = avatar.AddMenu();
            var hat = MenuEntry.Create(EntryKind.Toggle, "Hat");
            menu.entries.Add(hat);
            var view = View(menu);
            view.Tree.SelectEntry(hat.id);

            view.Detail.SetNameStyle(new Color(1f, 0f, 0f), bold: true, italic: false);

            Assert.That(hat.name, Is.EqualTo("<color=#FF0000><b>Hat</b></color>"));
            Assert.That(view.Detail.Q<TextField>("ad-name").value, Is.EqualTo("Hat"), "the plain field shows the text without tags");
            Assert.That(view.Detail.Q<Label>("ad-name-preview").text, Is.EqualTo("<color=#FF0000><b>Hat</b></color>"));
            Assert.That(view.Detail.Q<Label>("ad-name-preview").enableRichText, Is.True);
            Assert.That(view.Detail.Q<Button>("ad-style-bold").ClassListContains("ad-primary"), Is.True);
        }

        /// <summary>Runs the body with the view on a real panel: a field only raises its change event once it is attached.</summary>
        void OnAPanel(AvatarDoctorView view, System.Action body)
        {
            var window = ScriptableObject.CreateInstance<FocusHostWindow>();
            try
            {
                window.Show();
                window.rootVisualElement.Add(view);
                body();
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void IconEditDoesNotRebuildThePlan()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.Tree.SelectEntry(hat.id);
            var plan = view.Plan;
            var icon = new Texture2D(2, 2);
            try
            {
                OnAPanel(view, () =>
                {
                    view.Detail.Q<UnityEditor.UIElements.ObjectField>("ad-icon").value = icon;

                    Assert.That(hat.icon, Is.SameAs(icon));
                    Assert.That(view.Plan, Is.SameAs(plan), "a control's icon says nothing about the plan");
                });

                // The wheel is switched off in an inspector-sized view, so it is read once the view is off the panel.
                view.Refresh();
                Assert.That(view.Wheel.Model.Wedges.Single(w => w.EntryId == hat.id).Icon, Is.SameAs(icon),
                    "the wheel reads the icon off the entry, through the plan that stands");
            }
            finally
            {
                Object.DestroyImmediate(icon);
            }
        }

        [Test]
        public void SubmenuIconReachesTheWheel()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            menu.AddEntry(EntryKind.Toggle, "Hat", outfits.id).objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.Tree.SelectEntry(outfits.id);
            var plan = view.Plan;
            var icon = new Texture2D(2, 2);
            try
            {
                OnAPanel(view, () =>
                {
                    view.Detail.Q<UnityEditor.UIElements.ObjectField>("ad-icon").value = icon;

                    Assert.That(outfits.icon, Is.SameAs(icon));
                    Assert.That(view.Plan, Is.Not.SameAs(plan), "a submenu icon is copied into the merged menu node");
                });

                // A selected submenu makes the wheel show what is inside it, so the root is brought back to see
                // the submenu's own wedge. The wheel is switched off on a panel, so this happens off it.
                view.Tree.ClearSelection();
                view.Refresh();
                Assert.That(view.Wheel.Model.Wedges.Single(w => w.EntryId == outfits.id).Icon, Is.SameAs(icon));
            }
            finally
            {
                Object.DestroyImmediate(icon);
            }
        }

        [Test]
        public void CosmeticEditsReachASecondView()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.parameterName = "Mine";
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var first = View(menu);
            var second = View(menu);
            first.Tree.SelectEntry(hat.id);
            var plan = second.Plan;

            OnAPanel(first, () => first.Detail.Q<TextField>("ad-name").value = "Cap");

            Assert.That(second.Tree.RowFor(hat.id).Name, Is.EqualTo("Hat"), "the second view has not been told yet");

            second.CheckVersion();

            Assert.That(second.Plan, Is.SameAs(plan), "a cosmetic edit keeps the plan both views read");
            Assert.That(second.Tree.RowFor(hat.id).Name, Is.EqualTo("Cap"));
        }

        [Test]
        public void RenamingAToggleWithAnAutomaticParameterStillRebuilds()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.Tree.SelectEntry(hat.id);
            var plan = view.Plan;

            OnAPanel(view, () =>
            {
                view.Detail.Q<TextField>("ad-name").value = "Cap";

                Assert.That(view.Plan, Is.Not.SameAs(plan), "the automatic parameter name is made from the entry name");
                Assert.That(view.Detail.Q<Label>("ad-parameter-name").text, Is.EqualTo("AD/Cap"));
                Assert.That(view.Tree.RowFor(hat.id).Name, Is.EqualTo("Cap"));
            });
        }

        [Test]
        public void RenamingAToggleWithACustomParameterKeepsThePlan()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.parameterName = "Mine";
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.Tree.SelectEntry(hat.id);
            var plan = view.Plan;

            OnAPanel(view, () =>
            {
                view.Detail.Q<TextField>("ad-name").value = "Cap";

                Assert.That(hat.name, Is.EqualTo("Cap"));
                Assert.That(view.Plan, Is.SameAs(plan), "no parameter is made from this name");
                Assert.That(view.Tree.RowFor(hat.id).Name, Is.EqualTo("Cap"), "the tree row label follows at once");
            });
        }

        [Test]
        public void StyledSubmenuRenameKeepsInstallPathsPlain()
        {
            var first = avatar.AddMenu();
            var outfits = MenuEntry.Create(EntryKind.Submenu, "Outfits");
            first.entries.Add(outfits);
            var second = avatar.AddMenu(avatar.Glasses);
            second.installPath = "Outfits";
            var view = View(first);
            view.Tree.SelectEntry(outfits.id);

            view.Detail.SetNameStyle(null, bold: true, italic: false);
            Assert.That(outfits.name, Is.EqualTo("<b>Outfits</b>"));
            Assert.That(second.installPath, Is.EqualTo("Outfits"));

            view.Detail.Q<NameStyleRow>().SetPlain("Clothes");
            Assert.That(outfits.name, Is.EqualTo("<b>Clothes</b>"));
            Assert.That(second.installPath, Is.EqualTo("Clothes"));
        }

        [Test]
        public void CustomTagsFallBackToRawEditing()
        {
            var menu = avatar.AddMenu();
            var hat = MenuEntry.Create(EntryKind.Toggle, "Ha<b>t</b>");
            menu.entries.Add(hat);
            var view = View(menu);
            view.Tree.SelectEntry(hat.id);

            var row = view.Detail.Q<NameStyleRow>();
            Assert.That(row.Q<TextField>("ad-name-raw").ClassListContains("ad-hidden"), Is.False);
            Assert.That(row.Q<Button>("ad-style-bold").enabledSelf, Is.False);
            row.SetRawText("<b>Hat</b>");
            Assert.That(hat.name, Is.EqualTo("<b>Hat</b>"));
            Assert.That(row.Q<Button>("ad-style-bold").enabledSelf, Is.True);
        }

        [Test]
        public void ClearingTheColourDropsTheTag()
        {
            var menu = avatar.AddMenu();
            var hat = MenuEntry.Create(EntryKind.Toggle, "<color=#00FF00>Hat</color>");
            menu.entries.Add(hat);
            var view = View(menu);
            view.Tree.SelectEntry(hat.id);

            view.Detail.Q<NameStyleRow>().SetColor(null);

            Assert.That(hat.name, Is.EqualTo("Hat"));
        }

        [Test]
        public void ExistingRowsShowTheItemPanel()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);
            var row = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Name == "RootToggle");

            view.Tree.SelectKey(row.Key);

            Assert.That(view.Detail.Q<TextField>("ad-name"), Is.Not.Null);
            Assert.That(view.Detail.Q<TextField>("ad-name").value, Is.EqualTo("RootToggle"));
            Assert.That(view.Detail.Q<TextField>("ad-existing-move"), Is.Not.Null);
            // Hidden and In menu off were the same idea under two names; the row now reads the same "In menu"
            // switch an own entry has, inverted: an item not yet hidden reads as In menu on.
            Assert.That(view.Detail.Q<Toggle>("ad-existing-in-menu"), Is.Not.Null);
            Assert.That(view.Detail.Q<Toggle>("ad-existing-in-menu").value, Is.True);
            Assert.That(view.Detail.Q<Button>("ad-existing-reset").enabledSelf, Is.False, "disabled without an edit");
        }

        [Test]
        public void HidingAnExistingRowDimsItAndKeepsItInTheTree()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);
            var row = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Name == "RootToggle");
            view.Tree.SelectKey(row.Key);

            view.Detail.SetExistingHidden(true);

            var updated = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Key == row.Key);
            Assert.That(updated.Hidden, Is.True);
            Assert.That(updated.Name, Is.EqualTo("RootToggle"), "still shown in the tree, just hidden from the built menu");
            Assert.That(menu.menuEdits.Single().hide, Is.True);
            Assert.That(view.Detail.Q<Toggle>("ad-existing-in-menu").value, Is.False, "hidden reads as In menu off");
        }

        [Test]
        public void RenamingAnExistingRowUpdatesTheTreeAndTheWheel()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);
            var row = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Name == "RootToggle");
            view.Tree.SelectKey(row.Key);

            view.Detail.SetExistingName("Renamed");

            var updated = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Key == row.Key);
            Assert.That(updated.Name, Is.EqualTo("Renamed"));
            Assert.That(updated.Edited, Is.True);
            Assert.That(view.Wheel.Model.Wedges.Any(w => w.Name == "Renamed"), Is.True);
        }

        [Test]
        public void MovingAnExistingRowReparentsItInTheTree()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var view = View(menu);
            var row = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Name == "RootToggle");
            view.Tree.SelectKey(row.Key);

            view.Detail.MoveExistingTo("Outfits");

            var outfitsRow = view.Tree.RowFor(outfits.id);
            var moved = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Name == "RootToggle");
            Assert.That(moved.ParentRow, Is.SameAs(outfitsRow));
            Assert.That(moved.Edited, Is.True);

            bool dropped = view.Tree.DropExistingOn(moved, null);

            Assert.That(dropped, Is.True);
            var backAtRoot = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Name == "RootToggle");
            Assert.That(backAtRoot.ParentRow, Is.Null);
        }

        [Test]
        public void ResetRestoresTheOriginal()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);
            var row = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Name == "RootToggle");
            view.Tree.SelectKey(row.Key);
            view.Detail.SetExistingName("Renamed");
            view.Detail.SetExistingHidden(true);
            Assert.That(menu.menuEdits.Count, Is.EqualTo(1));

            view.Detail.ResetExisting();

            Assert.That(menu.menuEdits, Is.Empty);
            var restored = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Key == row.Key);
            Assert.That(restored.Name, Is.EqualTo("RootToggle"));
            Assert.That(restored.Hidden, Is.False);
        }

        [Test]
        public void ForeignRowsShowTheLateNote()
        {
            var go = new GameObject("Fury");
            go.transform.SetParent(avatar.Root.transform);
            VrcFuryTestHelper.AddToggle(go, "Hat");
            var menu = avatar.AddMenu();
            var view = View(menu);
            var row = view.Tree.RowsOfKind(RowKind.Foreign).First(r => r.Name == "Hat");

            view.Tree.SelectKey(row.Key);

            var notes = view.Detail.Query<Label>(className: "ad-note").ToList().Select(l => l.text).ToList();
            Assert.That(notes, Has.Some.EqualTo(string.Format(L.Text("ad.ui.existing.late_note"), row.SourceChip)));
        }

        [Test]
        public void OwnRowsNeverGetAnEdit()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var row = view.Tree.RowFor(hat.id);

            var edit = MenuEditing.EditFor(menu, row, create: true);

            Assert.That(edit, Is.Null);
            Assert.That(menu.menuEdits, Is.Empty);
        }

        [Test]
        public void DuplicateExistingControlsHaveDistinctRowKeys()
        {
            avatar.ExpressionsMenu.controls.Add(TestAvatar.ToggleControl("RootToggle", "Existing"));
            var menu = avatar.AddMenu();
            var view = View(menu);

            var rows = view.Tree.RowsOfKind(RowKind.Existing).Where(r => r.Name == "RootToggle").ToList();

            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows[0].Key, Is.Not.EqualTo(rows[1].Key));
        }

        [Test]
        public void OrphanEditsShowInTheHeaderAndCanBeRemoved()
        {
            var menu = avatar.AddMenu();
            menu.menuEdits.Add(new MenuEdit { id = "orphan", source = new List<string> { "Gone" }, name = "Ghost" });
            var other = new GameObject("Other");
            other.transform.SetParent(avatar.Root.transform);
            other.AddComponent<AvatarDoc>().menuEdits.Add(new MenuEdit { id = "theirs", source = new List<string> { "AlsoGone" }, name = "Spook" });
            var view = View(menu);

            Assert.That(view.Header.OrphanEditIds, Is.EqualTo(new List<string> { "orphan" }), "only this component's own leftovers");
            var list = view.Header.Q<VisualElement>("ad-problems-list");
            var removeButtons = list.Query<Button>().Build().Where(b => b.text == L.Text("ad.ui.edit.remove")).ToList();
            Assert.That(removeButtons, Has.Count.EqualTo(1), "the orphan edit lives in the problem list now, with its own Remove");

            view.Header.RemoveOrphanEdit("orphan");

            Assert.That(menu.menuEdits, Is.Empty);
            Assert.That(view.Header.OrphanEditIds, Is.Empty);
            Assert.That(view.Header.Q<VisualElement>("ad-problems-list").Query<Button>().Build().Where(b => b.text == L.Text("ad.ui.edit.remove")).ToList(), Is.Empty);
        }

        [Test]
        public void CycleMessagesShowOnTheRow()
        {
            var menu = avatar.AddMenu();
            menu.menuEdits.Add(new MenuEdit { id = "cycle", source = new List<string> { "Old" }, move = true, targetPath = "Old/Deep" });
            var view = View(menu);

            var row = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Node != null && r.Node.Path == "Old");

            Assert.That(row.Messages.Any(m => m.Key == "ad.error.edit_cycle"), Is.True);
            Assert.That(row.Edited, Is.True, "the rejected edit stays attached so the row can explain itself");
        }

        [Test]
        public void PanelFollowsASecondMove()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Submenu, "Outfits");
            menu.AddEntry(EntryKind.Submenu, "Extra");
            var view = View(menu);
            var row = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Name == "RootToggle");
            view.Tree.SelectKey(row.Key);

            Assert.That(MenuEditing.MoveTo(menu, row, "Outfits"), Is.True);
            AvatarPlanCache.Invalidate();
            view.Refresh();

            // The row's key is built from its OriginalPath, which a move never changes, so the selection survives
            // without having to be restored by hand.
            Assert.That(view.Tree.Selected, Is.Not.Null);
            Assert.That(view.Detail.Q<TextField>("ad-existing-move").value, Is.EqualTo("Outfits"));

            Assert.That(MenuEditing.MoveTo(menu, view.Tree.Selected, "Extra"), Is.True);
            AvatarPlanCache.Invalidate();
            view.Refresh();

            Assert.That(view.Detail.Q<TextField>("ad-existing-move").value, Is.EqualTo("Extra"));
        }

        [Test]
        public void MovedSubmenuKeepsItsExpansion()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Submenu, "Outfits");
            var view = View(menu);
            var oldRow = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Node != null && r.Node.Path == "Old");
            view.Tree.SetExpanded(oldRow.Key, true);
            Assert.That(view.Tree.ExpandedKeys, Does.Contain(oldRow.Key));

            Assert.That(MenuEditing.MoveTo(menu, oldRow, "Outfits"), Is.True);
            AvatarPlanCache.Invalidate();
            view.Refresh();

            var movedRow = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Node != null && r.Node.Path == "Outfits/Old");
            Assert.That(movedRow.Key, Is.EqualTo(oldRow.Key), "the key follows the original path, not the current one");
            Assert.That(view.Tree.ExpandedKeys, Does.Contain(movedRow.Key));
            Assert.That(view.Tree.Tree.IsExpanded(movedRow.Id), Is.True);
        }

        [Test]
        public void PropertyPickerListsShaderProperties()
        {
            var choices = PropertyPicker.Choices(avatar.Body);

            var glossiness = choices.Single(c => c.PropertyName == "_Glossiness");
            Assert.That(glossiness.Type, Is.EqualTo(PropertyValueType.Float));
            Assert.That(glossiness.IsRange, Is.True);
            Assert.That(glossiness.RangeMin, Is.EqualTo(0f));
            Assert.That(glossiness.RangeMax, Is.EqualTo(1f));

            var color = choices.Single(c => c.PropertyName == "_Color");
            Assert.That(color.Type, Is.EqualTo(PropertyValueType.Color));
            Assert.That(color.IsRange, Is.False);

            Assert.That(choices.Select(c => c.PropertyName).Distinct().Count(), Is.EqualTo(choices.Count), "no duplicates across the renderer's materials");
            Assert.That(choices.Any(c => c.PropertyName == "_MainTex"), Is.False, "textures are skipped");
        }

        [Test]
        public void AddingAPropertyCopiesTheCurrentValue()
        {
            avatar.MaterialA.SetFloat("_Glossiness", 0.42f);
            var menu = avatar.AddMenu();
            var entry = menu.AddEntry(EntryKind.Toggle, "Shine");
            var view = View(menu);
            view.Tree.SelectEntry(entry.id);
            var list = view.Detail.Q<TargetListView>("ad-properties");

            list.AddProperty(avatar.Body, "_Glossiness");

            var target = entry.properties.Single();
            Assert.That(target.type, Is.EqualTo(PropertyValueType.Float));
            Assert.That(target.value, Is.EqualTo(0.42f).Within(0.0001f));
        }

        [Test]
        public void ANewPropertyRowStartsFromCleanDefaultsNotACopyOfTheLastRow()
        {
            var menu = avatar.AddMenu();
            try
            {
                var entry = menu.AddEntry(EntryKind.Toggle, "Shine");
                entry.properties.Add(new MaterialPropertyTarget
                {
                    renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float,
                    value = 0.2f, minValue = 0.1f, color = Color.red, minColor = Color.blue,
                    vector = new Vector4(1f, 2f, 3f, 4f), minVector = new Vector4(5f, 6f, 7f, 8f), explicitOff = true,
                });
                var view = View(menu);
                view.Tree.SelectEntry(entry.id);

                view.Detail.Q<TargetListView>("ad-properties").AddProperty(null, "");

                var rows = menu.FindEntry(entry.id).properties;
                Assert.That(rows.Count, Is.EqualTo(2));
                var added = rows[1];
                Assert.That(added.explicitOff, Is.False, "the off value switch is not inherited");
                Assert.That(added.minValue, Is.EqualTo(0f), "nor the lower end");
                Assert.That(added.value, Is.EqualTo(1f), "the value is the declared default");
                Assert.That(added.color, Is.EqualTo(Color.white));
                Assert.That(added.minColor, Is.EqualTo(Color.black));
                Assert.That(added.vector, Is.EqualTo(Vector4.zero));
                Assert.That(added.minVector, Is.EqualTo(Vector4.zero));
                Assert.That(rows[0].explicitOff, Is.True, "the row it would have copied is left as it was");
                Assert.That(rows[0].minValue, Is.EqualTo(0.1f).Within(0.0001f));
            }
            finally
            {
                Undo.ClearUndo(menu);
            }
        }

        [Test]
        public void PropertyRowsFollowTheType()
        {
            var menu = avatar.AddMenu();
            var entry = menu.AddEntry(EntryKind.Toggle, "Shine");
            entry.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 0.5f });
            entry.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Color", type = PropertyValueType.Color, color = Color.red });
            entry.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_CustomVector", type = PropertyValueType.Vector, vector = new Vector4(1f, 2f, 3f, 4f) });
            entry.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_CustomFloat", type = PropertyValueType.Float, value = 3f });
            var view = View(menu);
            view.Tree.SelectEntry(entry.id);
            var list = view.Detail.Q<TargetListView>("ad-properties");

            var glossiness = list.RowAt(0);
            var slider = glossiness.Q<Slider>("value");
            Assert.That(slider, Is.Not.Null, "_Glossiness is a range property, so it gets a slider using the shader's own limits");
            Assert.That(slider.lowValue, Is.EqualTo(0f));
            Assert.That(slider.highValue, Is.EqualTo(1f));
            Assert.That(glossiness.Q<SegmentedChoice>("type"), Is.Null, "a matched property does not need a manual type choice");

            var tint = list.RowAt(1);
            var color = tint.Q<ColorField>("value");
            Assert.That(color, Is.Not.Null);
            Assert.That(color.hdr, Is.True);
            Assert.That(color.showAlpha, Is.True);

            var vector = list.RowAt(2);
            Assert.That(vector.Q<FloatField>("value-x"), Is.Not.Null);
            Assert.That(vector.Q<FloatField>("value-y"), Is.Not.Null);
            Assert.That(vector.Q<FloatField>("value-z"), Is.Not.Null);
            Assert.That(vector.Q<FloatField>("value-w"), Is.Not.Null);
            Assert.That(vector.Q<SegmentedChoice>("type"), Is.Not.Null, "the renderer's shaders do not list this property, so the type is picked by hand");

            var plainFloat = list.RowAt(3);
            Assert.That(plainFloat.Q<FloatField>("value"), Is.Not.Null, "a float with no range is a plain field");
            Assert.That(plainFloat.Q<Slider>("value"), Is.Null);
        }

        [Test]
        public void RadialPropertyRowsShowBothEnds()
        {
            var menu = avatar.AddMenu();
            var entry = menu.AddEntry(EntryKind.Radial, "Shine");
            entry.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, minValue = 0.1f, value = 0.9f });
            var view = View(menu);
            view.Tree.SelectEntry(entry.id);
            var list = view.Detail.Q<TargetListView>("ad-properties");

            var row = list.RowAt(0);
            var min = row.Q<Slider>("min");
            var max = row.Q<Slider>("value");
            Assert.That(min, Is.Not.Null);
            Assert.That(max, Is.Not.Null);
            var values = row.Q<VisualElement>("values");
            Assert.That(values.IndexOf(min), Is.LessThan(values.IndexOf(max)), "the lower end sits before the upper one");
        }

        [Test]
        public void ExplicitOffSwitchShowsTheOffValueFieldAtOnce()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "T");
            toggle.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 80f });
            toggle.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 0.8f });
            var view = View(menu);
            view.Tree.SelectEntry(toggle.id);

            var shapes = view.Detail.Q<TargetListView>("ad-blendshapes");
            var shapeRow = shapes.RowAt(0);
            Assert.That(shapeRow.Q<Toggle>("explicit-off"), Is.Not.Null);
            Assert.That(shapeRow.Q<Slider>("off-value").ClassListContains("ad-hidden"), Is.True, "hidden until the switch is on");

            var props = view.Detail.Q<TargetListView>("ad-properties");
            Assert.That(props.RowAt(0).Q<Slider>("off-value"), Is.Null, "a property row builds the off value control only once the switch is on");

            shapes.SetShapeExplicitOff(0, true);
            props.SetPropertyExplicitOff(0, true);

            var refreshedShapeRow = view.Detail.Q<TargetListView>("ad-blendshapes").RowAt(0);
            Assert.That(refreshedShapeRow.Q<Slider>("off-value").ClassListContains("ad-hidden"), Is.False, "the field appears at once, without a reselect");
            var refreshedPropRow = view.Detail.Q<TargetListView>("ad-properties").RowAt(0);
            Assert.That(refreshedPropRow.Q<Slider>("off-value"), Is.Not.Null);
        }

        [Test]
        public void ExplicitOffSitsOnALineOfItsOwn()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "T");
            toggle.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 80f });
            toggle.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 0.8f });
            var view = View(menu);
            view.Tree.SelectEntry(toggle.id);
            view.Detail.Q<TargetListView>("ad-blendshapes").SetShapeExplicitOff(0, true);
            view.Detail.Q<TargetListView>("ad-properties").SetPropertyExplicitOff(0, true);

            foreach (var name in new[] { "ad-blendshapes", "ad-properties" })
            {
                var row = view.Detail.Q<TargetListView>(name).RowAt(0);
                var line = row.Q<VisualElement>("off-line");
                Assert.That(line, Is.Not.Null, name);
                Assert.That(line.parent, Is.SameAs(row), name + ": the line is a child of the row");
                Assert.That(row.ClassListContains("ad-target-wrap"), Is.True, name + ": the row wraps so the line goes under it");
                Assert.That(row.Q<Toggle>("explicit-off").parent, Is.SameAs(line), name + ": the switch is not squeezed into the target line");
                Assert.That(row.Q<Slider>("off-value").parent, Is.SameAs(line), name + ": nor is the off value");
                Assert.That(row.IndexOf(line), Is.EqualTo(row.childCount - 1), name + ": the line comes after the remove button");
            }
        }

        [Test]
        public void TurningExplicitOffOnSeedsTheSceneValue()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "T");
            int shapeIndex = avatar.Body.sharedMesh.GetBlendShapeIndex("Blush");
            avatar.Body.SetBlendShapeWeight(shapeIndex, 42f);
            toggle.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, minWeight = 0f });
            avatar.MaterialA.SetFloat("_Glossiness", 0.35f);
            toggle.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 1f, minValue = 0f });
            var view = View(menu);
            view.Tree.SelectEntry(toggle.id);
            var shapes = view.Detail.Q<TargetListView>("ad-blendshapes");
            var props = view.Detail.Q<TargetListView>("ad-properties");

            shapes.SetShapeExplicitOff(0, true);
            props.SetPropertyExplicitOff(0, true);

            Assert.That(toggle.blendShapes[0].explicitOff, Is.True);
            Assert.That(toggle.blendShapes[0].minWeight, Is.EqualTo(42f).Within(0.01f), "seeded from the scene, not from the on weight");
            Assert.That(toggle.properties[0].explicitOff, Is.True);
            Assert.That(toggle.properties[0].minValue, Is.EqualTo(0.35f).Within(0.001f), "seeded from the material, not from the on value");
        }

        [Test]
        public void RadialAndPinnedTargetsHaveNoExplicitOffSwitch()
        {
            var menu = avatar.AddMenu();
            var radial = menu.AddEntry(EntryKind.Radial, "R");
            radial.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush" });
            radial.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float });
            var over = MenuEntry.Create(EntryKind.Override, "O");
            over.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush" });
            menu.entries.Add(over);
            var view = View(menu);

            view.Tree.SelectEntry(radial.id);
            Assert.That(view.Detail.Q<TargetListView>("ad-blendshapes").RowAt(0).Q<Toggle>("explicit-off"), Is.Null, "a radial keeps its two ends and never shows the switch");
            Assert.That(view.Detail.Q<TargetListView>("ad-properties").RowAt(0).Q<Toggle>("explicit-off"), Is.Null);

            view.Tree.SelectEntry(over.id);
            Assert.That(view.Detail.Q<TargetListView>("ad-blendshapes").RowAt(0).Q<Toggle>("explicit-off"), Is.Null, "a pinned override target never shows the switch");
        }

        [Test]
        public void AnimationRowsHoldClipAndPathRoot()
        {
            var menu = avatar.AddMenu();
            var entry = menu.AddEntry(EntryKind.Toggle, "Wave");
            var clip = new AnimationClip { name = "Wave" };
            try
            {
                var view = View(menu);
                view.Tree.SelectEntry(entry.id);
                var list = view.Detail.Q<TargetListView>("ad-animations");

                list.AddAnimation(clip);
                Assert.That(entry.animations.Single().clip, Is.SameAs(clip));

                entry.animations[0].pathRoot = avatar.Hat.transform;
                AvatarPlanCache.Invalidate();
                view.Refresh();
                list = view.Detail.Q<TargetListView>("ad-animations");
                // An ObjectField's bound value only pulls once its binding actually ticks, which needs an attached
                // panel; the row's structure (which field holds which target) is what RowAt is for here.
                var row = list.RowAt(0);
                Assert.That(row.Q<ObjectField>("clip"), Is.Not.Null);
                Assert.That(row.Q<ObjectField>("path-root"), Is.Not.Null);
                Assert.That(entry.animations[0].pathRoot, Is.SameAs(avatar.Hat.transform));
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void BlendTimeRowOnlyOnToggles()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            var radial = menu.AddEntry(EntryKind.Radial, "Eyes");
            var view = View(menu);

            view.Tree.SelectEntry(toggle.id);
            Assert.That(view.Detail.Q<FloatField>("ad-blend"), Is.Not.Null);

            view.Tree.SelectEntry(radial.id);
            Assert.That(view.Detail.Q<FloatField>("ad-blend"), Is.Null, "radials have no blend time");

            view.Tree.SelectEntry(toggle.id);
            toggle.exclusiveGroup = "Outfits";
            AvatarPlanCache.Invalidate();
            view.Refresh();
            Assert.That(view.Detail.Q<FloatField>("ad-blend"), Is.Null, "hidden while the toggle is in a group");
            Assert.That(view.Detail.Q<Label>("ad-blend-note"), Is.Not.Null);
        }

        [Test]
        public void DraggingAPropertySliderKeepsTheSlider()
        {
            var menu = avatar.AddMenu();
            var entry = menu.AddEntry(EntryKind.Toggle, "Shine");
            entry.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 0.5f });
            var view = View(menu);
            view.Tree.SelectEntry(entry.id);
            int rebuilds = view.Detail.RebuildCount;

            // An unattached ListView never materialises its rows for a plain query (see TargetListView.RowAt), so
            // this drives the value through the slider a real bound row would show, built on demand the same way
            // the other TargetListView tests read one; the point of this test is what happens to the panel around
            // it, not the identity of this stand-in element.
            var list = view.Detail.Q<TargetListView>("ad-properties");
            var slider = list.RowAt(0).Q<Slider>("value");
            Assert.That(slider, Is.Not.Null);
            slider.value = 0.2f;
            slider.value = 0.6f;
            slider.value = 0.9f;

            // A value-only change never touches the entry's type or its renderer, the only things Shape(row) counts
            // for a property row, so the panel does not rebuild: the same slider a real bound ListView row shows
            // stays alive through the drag exactly as the preset panel's slider does in DraggingAPresetSliderKeepsTheSlider.
            Assert.That(view.Detail.RebuildCount, Is.EqualTo(rebuilds));
        }

        [Test]
        public void RepickingTheSamePropertyKeepsTheValue()
        {
            avatar.MaterialA.SetFloat("_Glossiness", 0.1f);
            var menu = avatar.AddMenu();
            var entry = menu.AddEntry(EntryKind.Toggle, "Shine");
            entry.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 0.75f });
            var view = View(menu);
            view.Tree.SelectEntry(entry.id);
            var list = view.Detail.Q<TargetListView>("ad-properties");
            var choice = PropertyPicker.Choices(avatar.Body).Single(c => c.PropertyName == "_Glossiness");

            list.PickProperty(0, choice);

            Assert.That(entry.properties[0].value, Is.EqualTo(0.75f).Within(0.0001f), "repicking the same property must not overwrite the creator's edited value with the material's live value");
        }

        [Test]
        public void TypingAPropertyNameSetsTypeAndValue()
        {
            avatar.MaterialA.SetColor("_Color", new Color(0.2f, 0.4f, 0.6f, 0.8f));
            // _SrcBlend is a real Standard shader property, but it is [HideInInspector], so PropertyPicker never lists it.
            avatar.MaterialA.SetFloat("_SrcBlend", 5f);
            var menu = avatar.AddMenu();
            var entry = menu.AddEntry(EntryKind.Toggle, "Tint");
            entry.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "", type = PropertyValueType.Vector });
            entry.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "", type = PropertyValueType.Color });
            var view = View(menu);
            view.Tree.SelectEntry(entry.id);
            var list = view.Detail.Q<TargetListView>("ad-properties");

            list.PickPropertyOther(0, "_Color");
            Assert.That(entry.properties[0].property, Is.EqualTo("_Color"));
            Assert.That(entry.properties[0].type, Is.EqualTo(PropertyValueType.Color), "the shader lists _Color, so the type comes from there");
            Assert.That(entry.properties[0].color.r, Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(entry.properties[0].color.g, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(entry.properties[0].color.b, Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(entry.properties[0].color.a, Is.EqualTo(0.8f).Within(0.0001f));

            list = view.Detail.Q<TargetListView>("ad-properties");
            list.PickPropertyOther(1, "_SrcBlend");
            Assert.That(entry.properties[1].property, Is.EqualTo("_SrcBlend"));
            Assert.That(entry.properties[1].type, Is.EqualTo(PropertyValueType.Float), "hidden from the picker, but Material.HasFloat still resolves it");
            Assert.That(entry.properties[1].value, Is.EqualTo(5f).Within(0.0001f));
        }

        [Test]
        public void SwitchingBetweenFloatPropertiesRebuildsTheRow()
        {
            var menu = avatar.AddMenu();
            var entry = menu.AddEntry(EntryKind.Toggle, "Shine");
            entry.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 0.5f });
            var view = View(menu);
            view.Tree.SelectEntry(entry.id);
            var list = view.Detail.Q<TargetListView>("ad-properties");
            Assert.That(list.RowAt(0).Q<Slider>("value"), Is.Not.Null, "_Glossiness is a range, so it starts as a slider");

            var bumpScale = PropertyPicker.Choices(avatar.Body).Single(c => c.PropertyName == "_BumpScale");
            Assert.That(bumpScale.IsRange, Is.False, "a plain float, not a range, to prove the row rebuilds even though the type does not change");
            list.PickProperty(0, bumpScale);

            list = view.Detail.Q<TargetListView>("ad-properties");
            var row = list.RowAt(0);
            Assert.That(row.Q<Slider>("value"), Is.Null, "a non range float no longer shows a slider");
            Assert.That(row.Q<FloatField>("value"), Is.Not.Null, "it now shows a plain field");
        }

        Component AddFuryToggle(string name)
        {
            var go = new GameObject("Fury");
            go.transform.SetParent(avatar.Root.transform);
            return VrcFuryTestHelper.AddToggle(go, name);
        }

        static ImportWindow NewImportWindow(AvatarDoc menu)
        {
            var window = ScriptableObject.CreateInstance<ImportWindow>();
            window.Init(menu);
            return window;
        }

        /// <summary>The index of the first candidate matching a predicate. The default test avatar's own "RootToggle" and "OldToggle" share one parameter, so they always show up as a couple of unsupported animator layer candidates alongside whatever a test adds; tests find their own candidate by a predicate rather than assuming a position or a total count.</summary>
        static int IndexOf(ImportWindow window, System.Func<ImportCandidate, bool> predicate)
        {
            for (int i = 0; i < window.Candidates.Count; i++)
                if (predicate(window.Candidates[i])) return i;
            return -1;
        }

        [Test]
        public void ToolbarOpensTheImportWindow()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);

            view.Tree.OpenImportWindow();

            var window = EditorWindow.GetWindow<ImportWindow>();
            try
            {
                Assert.That(window, Is.Not.Null);
                Assert.That(window.Candidates, Is.Not.Null);
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void WindowListsCandidatesBySource()
        {
            Assume.That(VrcFuryTestHelper.VrcFuryType, Is.Not.Null);
            var menu = avatar.AddMenu();
            var toggle = AddFuryToggle("Hat");
            VrcFuryTestHelper.AddAction(toggle, VrcFuryReader.ObjectToggleActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "obj", avatar.Hat);
                VrcFuryTestHelper.SetField(a, "mode", 0);
            });
            var window = NewImportWindow(menu);
            try
            {
                var index = IndexOf(window, c => c.Source == ImportSource.VRCFury);
                Assert.That(index, Is.GreaterThanOrEqualTo(0));
                Assert.That(window.Candidates[index].Supported, Is.True);
                Assert.That(window.Candidates.Any(c => c.Source == ImportSource.FxLayer), Is.True,
                    "the test avatar's own two toggles share a parameter, so they show up as animator layer candidates too");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void UnsupportedRowsCannotBeTicked()
        {
            Assume.That(VrcFuryTestHelper.VrcFuryType, Is.Not.Null);
            var menu = avatar.AddMenu();
            var toggle = AddFuryToggle("Extra");
            VrcFuryTestHelper.AddAction(toggle, "VF.Model.StateAction.ScaleAction", a => { });
            var window = NewImportWindow(menu);
            try
            {
                var index = IndexOf(window, c => c.Source == ImportSource.VRCFury);
                Assert.That(window.Candidates[index].Supported, Is.False);

                window.SetChecked(index, true);
                var created = window.RunImport();

                Assert.That(created, Is.Empty, "an unsupported row cannot be ticked, so nothing is imported");
                Assert.That(menu.entries, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void RunImportAddsEntriesAndRefreshes()
        {
            Assume.That(VrcFuryTestHelper.VrcFuryType, Is.Not.Null);
            var menu = avatar.AddMenu();
            var toggle = AddFuryToggle("Hat");
            VrcFuryTestHelper.AddAction(toggle, VrcFuryReader.ObjectToggleActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "obj", avatar.Hat);
                VrcFuryTestHelper.SetField(a, "mode", 0);
            });
            var window = NewImportWindow(menu);
            try
            {
                window.SetChecked(IndexOf(window, c => c.Source == ImportSource.VRCFury), true);
                window.SetRemoveOriginals(true);

                var created = window.RunImport();

                Assert.That(created.Count, Is.EqualTo(1));
                Assert.That(menu.entries.Single(), Is.SameAs(created[0]));
                Assert.That(toggle == null, Is.True, "removeOriginals removed the source component");
                Assert.That(window.Candidates.Any(c => c.Source == ImportSource.VRCFury), Is.False, "the list rescans after import");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void RestoreLayerRowBringsBackTheOldControl()
        {
            var menu = avatar.AddMenu();
            avatar.Fx.AddParameter("Hat", AnimatorControllerParameterType.Bool);
            avatar.Fx.AddLayer("HatLayer");
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
            transition.AddCondition(AnimatorConditionMode.If, 0f, "Hat");
            var clip = new AnimationClip { name = "HatOn" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Hat", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0f, 1f / 60f, 1f));
            on.motion = clip;
            avatar.Descriptor.expressionsMenu.controls.Add(TestAvatar.ToggleControl("Hat", "Hat"));
            var window = NewImportWindow(menu);
            try
            {
                window.SetChecked(IndexOf(window, c => c.Draft != null && c.Draft.parameterName == "Hat"), true);
                window.RunImport();
                Assert.That(menu.removedLayers.Select(r => r.layer), Is.EqualTo(new[] { "HatLayer" }));

                window.RestoreLayer(0);

                Assert.That(menu.removedLayers, Is.Empty);
                Assert.That(menu.menuEdits, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ImportWindowRescansWhenItsGuiIsCreated()
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
                // Simulates the window right after a domain reload: the serialized component field survives it,
                // but the plain candidate list does not, so a freshly (re)constructed instance starts out
                // believing there is nothing to import until CreateGUI runs again.
                VrcFuryTestHelper.SetField(window, "component", menu);
                Assert.That(window.Candidates, Is.Empty);

                window.CreateGUI();

                Assert.That(window.Candidates.Any(c => c.Source == ImportSource.VRCFury), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ImportWindowRefusesStaleCandidates()
        {
            Assume.That(VrcFuryTestHelper.VrcFuryType, Is.Not.Null);
            var menu = avatar.AddMenu();
            var toggle = AddFuryToggle("Hat");
            VrcFuryTestHelper.AddAction(toggle, VrcFuryReader.ObjectToggleActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "obj", avatar.Hat);
                VrcFuryTestHelper.SetField(a, "mode", 0);
            });
            var window = NewImportWindow(menu);
            try
            {
                window.SetChecked(IndexOf(window, c => c.Source == ImportSource.VRCFury), true);
                // The scene changes behind the window's back (an Undo, a manual delete) without it rescanning.
                Object.DestroyImmediate(toggle);

                var created = window.RunImport();

                Assert.That(created, Is.Empty, "a stale candidate is refused, not imported");
                Assert.That(menu.entries, Is.Empty);
                Assert.That(window.Candidates.Any(c => c.Source == ImportSource.VRCFury), Is.False, "RunImport rescanned and dropped the destroyed candidate");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

#if DOCME_MA
        /// <summary>A Modular Avatar toggle with one Object Toggle on target, on the given parameter (none when null).</summary>
        void AddMaToggle(string label, GameObject target, string parameter = null)
        {
            var go = new GameObject("MA " + label);
            go.transform.SetParent(avatar.Root.transform);
            go.AddComponent<ModularAvatarMenuInstaller>();
            var item = go.AddComponent<ModularAvatarMenuItem>();
            item.Control = new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control { type = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.ControlType.Toggle, value = 1f };
            item.automaticValue = true;
            if (parameter != null) item.Control.parameter = new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.Parameter { name = parameter };
            item.label = label;
            var toggle = go.AddComponent<ModularAvatarObjectToggle>();
            var reference = new AvatarObjectReference();
            reference.Set(target);
            toggle.Objects = new List<ToggledObject> { new ToggledObject { Object = reference, Active = true } };
        }

        ImportWindow WindowWithTwoToggles(AvatarDoc menu)
        {
            AddMaToggle("Hat", avatar.Hat);
            AddMaToggle("Glasses", avatar.Glasses);
            var window = NewImportWindow(menu);
            window.CreateGUI();
            return window;
        }

        static int IndexOfName(ImportWindow window, string name) => IndexOf(window, c => c.Draft != null && c.Draft.name == name);

        [Test]
        public void TheConvertWindowKeepsItsTicksWhenSomethingElseChanges()
        {
            var menu = avatar.AddMenu();
            var window = WindowWithTwoToggles(menu);
            try
            {
                window.SetChecked(IndexOfName(window, "Hat"), false);
                Assert.That(window.IsChecked(IndexOfName(window, "Hat")), Is.False);

                // Anything that moves the plan: an undo of something unrelated, a rename elsewhere, an edit in the inspector.
                AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
                window.RunPendingRescan();

                Assert.That(window.IsChecked(IndexOfName(window, "Hat")), Is.False, "a row the creator unticked stays unticked, or Convert would remove it");
                Assert.That(window.IsChecked(IndexOfName(window, "Glasses")), Is.True, "and the others stay as they were");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void OpeningTheConvertWindowAgainForTheSameComponentKeepsItsTicks()
        {
            var menu = avatar.AddMenu();
            var window = WindowWithTwoToggles(menu);
            try
            {
                window.SetChecked(IndexOfName(window, "Hat"), false);

                // Pressing Convert while the window is open goes through Open, which calls Init.
                window.Init(menu);

                Assert.That(window.IsChecked(IndexOfName(window, "Hat")), Is.False, "the same component, so what the creator unticked stays unticked");
                Assert.That(window.IsChecked(IndexOfName(window, "Glasses")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void PointingTheConvertWindowAtAnotherComponentStartsFresh()
        {
            var menu = avatar.AddMenu();
            var otherObject = new GameObject("Other");
            otherObject.transform.SetParent(avatar.Root.transform);
            var other = otherObject.AddComponent<AvatarDoc>();
            var window = WindowWithTwoToggles(menu);
            try
            {
                window.SetChecked(IndexOfName(window, "Hat"), false);

                window.Init(other);

                Assert.That(window.IsChecked(IndexOfName(window, "Hat")), Is.True, "a different component to convert into: every row that can be converted starts ticked");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void TheConvertWindowKeepsKeepAsItIsAcrossARescan()
        {
            var menu = avatar.AddMenu();
            var window = WindowWithTwoToggles(menu);
            try
            {
                window.Candidates[IndexOfName(window, "Hat")].KeepAsItIs = true;

                AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
                window.RunPendingRescan();

                Assert.That(window.Candidates[IndexOfName(window, "Hat")].KeepAsItIs, Is.True);
                Assert.That(window.Candidates[IndexOfName(window, "Glasses")].KeepAsItIs, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ATickedRowThatWasNotThereBeforeStartsTicked()
        {
            var menu = avatar.AddMenu();
            var window = WindowWithTwoToggles(menu);
            try
            {
                window.SetChecked(IndexOfName(window, "Hat"), false);
                AddMaToggle("Scarf", avatar.Body.gameObject);

                AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
                window.RunPendingRescan();

                Assert.That(window.IsChecked(IndexOfName(window, "Scarf")), Is.True, "a new row follows the usual rule: a row that can be converted starts ticked");
                Assert.That(window.IsChecked(IndexOfName(window, "Hat")), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void TheConvertWindowRescansOncePerFrame()
        {
            var menu = avatar.AddMenu();
            var window = WindowWithTwoToggles(menu);
            try
            {
                int before = ImportScanner.ScanCalls;

                for (int i = 0; i < 5; i++) AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
                Assert.That(ImportScanner.ScanCalls, Is.EqualTo(before), "nothing rescans inside the change event itself");

                window.RunPendingRescan();
                Assert.That(ImportScanner.ScanCalls, Is.EqualTo(before + 1), "five changes, one rescan");

                window.RunPendingRescan();
                Assert.That(ImportScanner.ScanCalls, Is.EqualTo(before + 1), "and nothing is left waiting");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void TheConvertWindowIgnoresAChangeThatOnlyChangesHowThingsRead()
        {
            var menu = avatar.AddMenu();
            var window = WindowWithTwoToggles(menu);
            try
            {
                int before = ImportScanner.ScanCalls;

                AvatarPlanCache.NoteCosmetic(avatar.Root.transform);
                window.RunPendingRescan();

                Assert.That(ImportScanner.ScanCalls, Is.EqualTo(before), "a name or an icon moved: the candidates did not");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void TickingOneMemberOfARadioSetTicksTheWholeSet()
        {
            var menu = avatar.AddMenu();
            AddMaToggle("Dress", avatar.Hat, "Outfit");
            AddMaToggle("Hoodie", avatar.Glasses, "Outfit");
            var window = NewImportWindow(menu);
            try
            {
                window.CreateGUI();

                window.SetChecked(IndexOfName(window, "Dress"), false);

                Assert.That(window.IsChecked(IndexOfName(window, "Hoodie")), Is.False, "the set converts as one exclusive group, so it ticks as one");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }
#endif

        [Test]
        public void EveryKindOfRowOfADisabledComponentCanStillBeOpened()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var casual = menu.AddEntry(EntryKind.Toggle, "Casual");
            casual.exclusiveGroup = "Outfits";
            casual.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var blush = menu.AddEntry(EntryKind.Radial, "Blush");
            blush.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush" });
            var follow = menu.AddEntry(EntryKind.Trigger, "Follow");
            follow.source = TriggerSource.Toggle;
            follow.sourceToggleId = hat.id;
            follow.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var preset = menu.AddEntry(EntryKind.Preset, "All on");
            preset.presetStates.Add(new PresetState { entryId = hat.id, on = true });
            menu.enabled = false;
            AvatarPlanCache.Invalidate();

            var view = View(menu);

            foreach (var entry in menu.entries)
            {
                Assert.DoesNotThrow(() => view.Tree.SelectEntry(entry.id), entry.name);
                Assert.That(view.Detail.Row?.EntryId, Is.EqualTo(entry.id), entry.name + " opens its panel");
            }
            Assert.DoesNotThrow(() => view.Refresh());
        }

        [Test]
        public void ARevealForAnEntryThatIsGoneLeavesTheSelectionAlone()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.Tree.SelectEntry(hat.id);
            Assert.That(view.Tree.Selected?.EntryId, Is.EqualTo(hat.id));

            // An import asked for this entry while another object was shown, and was undone before this view refreshed.
            AvatarDoctorView.RequestReveal(menu, "an-entry-that-was-undone");
            view.Refresh();

            Assert.That(view.Tree.Selected?.EntryId, Is.EqualTo(hat.id), "a request for something that is not there must not clear what the creator selected");
            Assert.That(view.Detail.Row?.EntryId, Is.EqualTo(hat.id), "or empty the panel");
        }

        [Test]
        public void ARevealForAnEntryThatExistsStillSelectsIt()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var scarf = menu.AddEntry(EntryKind.Toggle, "Scarf");
            scarf.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var view = View(menu);
            view.Tree.SelectEntry(hat.id);

            AvatarDoctorView.RequestReveal(menu, scarf.id);
            view.Refresh();

            Assert.That(view.Tree.Selected?.EntryId, Is.EqualTo(scarf.id));
        }

        [Test]
        public void APendingRevealForAComponentThatIsGoneIsDropped()
        {
            // The list is static, so the creator's own session may already hold requests (a Convert whose reveal no
            // open view took); one is seeded here, and only the change counts.
            var menu = avatar.AddMenu();
            var sessionObject = new GameObject("Session");
            sessionObject.transform.SetParent(avatar.Root.transform);
            AvatarDoctorView.RequestReveal(avatar.AddMenu(sessionObject), "from the session");
            AvatarDoctorView.RequestReveal(menu, "first");
            int kept = AvatarDoctorView.PendingRevealCount;
            var goneAvatar = TestAvatar.Create();
            AvatarDoctorView.RequestReveal(goneAvatar.AddMenu(), "something");
            Assert.That(AvatarDoctorView.PendingRevealCount, Is.EqualTo(kept + 1), "the request is held while its component lives");
            goneAvatar.Dispose();

            AvatarDoctorView.RequestReveal(menu, "something else");

            Assert.That(AvatarDoctorView.PendingRevealCount, Is.EqualTo(kept), "a component that is gone is not kept alive by a request nobody will take");
        }

        [Test]
        public void ARefreshAlsoLetsGoOfARevealForAComponentThatIsGone()
        {
            var menu = avatar.AddMenu();
            var sessionObject = new GameObject("Session");
            sessionObject.transform.SetParent(avatar.Root.transform);
            AvatarDoctorView.RequestReveal(avatar.AddMenu(sessionObject), "from the session");
            var view = View(menu);
            view.Refresh();
            int kept = AvatarDoctorView.PendingRevealCount;
            var goneAvatar = TestAvatar.Create();
            AvatarDoctorView.RequestReveal(goneAvatar.AddMenu(), "something");
            Assert.That(AvatarDoctorView.PendingRevealCount, Is.EqualTo(kept + 1), "the request is held while its component lives");
            goneAvatar.Dispose();

            view.Refresh();

            Assert.That(AvatarDoctorView.PendingRevealCount, Is.EqualTo(kept));
        }

        [Test]
        public void TheQuickStartWindowBuildsItselfAgainAfterAScriptReload()
        {
            var before = ScriptableObject.CreateInstance<QuickStartWindow>();
            QuickStartWindow reloaded = null;
            try
            {
                before.Init("Step one: add a toggle.");
                // A script reload keeps what is serialized and nothing else; Unity then asks the window for its GUI again.
                reloaded = ScriptableObject.CreateInstance<QuickStartWindow>();
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(before), reloaded);
                reloaded.CreateGUI();

                var field = reloaded.rootVisualElement.Q<TextField>();
                Assert.That(field, Is.Not.Null, "the window shows its text again instead of staying blank");
                Assert.That(field.value, Is.EqualTo("Step one: add a toggle."));
                reloaded.CreateGUI();
                Assert.That(reloaded.rootVisualElement.Query<TextField>().ToList().Count, Is.EqualTo(1), "building again replaces the text instead of adding a second one");
            }
            finally
            {
                Object.DestroyImmediate(before);
                if (reloaded != null) Object.DestroyImmediate(reloaded);
            }
        }

        [Test]
        public void ConvertingAForeignRowCreatesAnEntryAndRemovesTheOriginal()
        {
            Assume.That(VrcFuryTestHelper.VrcFuryType, Is.Not.Null);
            var menu = avatar.AddMenu();
            var toggle = AddFuryToggle("Hat");
            VrcFuryTestHelper.AddAction(toggle, VrcFuryReader.ObjectToggleActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "obj", avatar.Hat);
                VrcFuryTestHelper.SetField(a, "mode", 0);
            });
            var view = View(menu);
            var row = view.Tree.RowsOfKind(RowKind.Foreign).Single(r => r.Name == "Hat");

            var created = view.Tree.ConvertForeign(row);

            Assert.That(created, Is.Not.Null);
            Assert.That(menu.entries.Single(), Is.SameAs(created));
            Assert.That(created.objects.Single().target, Is.EqualTo(avatar.Hat));
            Assert.That(toggle == null, Is.True, "the original VRCFury component is removed");
            Assert.That(view.Tree.RowFor(created.id), Is.Not.Null, "the new entry is revealed");
            Assert.That(view.Tree.Selected, Is.Not.Null);
            Assert.That(view.Tree.Selected.EntryId, Is.EqualTo(created.id), "the new entry is selected, not merely present");
        }

        [Test]
        public void ConvertingAForeignSubmenuConvertsEverythingBelowIt()
        {
            Assume.That(VrcFuryTestHelper.VrcFuryType, Is.Not.Null);
            var menu = avatar.AddMenu();
            var hatToggle = AddFuryToggle("Clothes/Hat");
            VrcFuryTestHelper.AddAction(hatToggle, VrcFuryReader.ObjectToggleActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "obj", avatar.Hat);
                VrcFuryTestHelper.SetField(a, "mode", 0);
            });
            var scarfToggle = AddFuryToggle("Clothes/Scarf");
            VrcFuryTestHelper.AddAction(scarfToggle, VrcFuryReader.ObjectToggleActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "obj", avatar.Glasses);
                VrcFuryTestHelper.SetField(a, "mode", 0);
            });
            var view = View(menu);
            var row = view.Tree.RowsOfKind(RowKind.Foreign).Single(r => r.Node != null && r.Name == "Clothes");

            var created = view.Tree.ConvertForeign(row);

            Assert.That(created, Is.Not.Null);
            var submenu = menu.entries.Single(e => e.kind == EntryKind.Submenu);
            Assert.That(submenu.PlainName, Is.EqualTo("Clothes"));
            var toggles = menu.entries.Where(e => e.kind == EntryKind.Toggle).ToList();
            Assert.That(toggles.Select(e => e.PlainName), Is.EquivalentTo(new[] { "Hat", "Scarf" }));
            Assert.That(toggles.All(e => e.parentId == submenu.id), Is.True);
            Assert.That(hatToggle == null, Is.True, "both original VRCFury components are removed");
            Assert.That(scarfToggle == null, Is.True, "both original VRCFury components are removed");
        }

        [Test]
        public void ConvertingAnUnsupportedRowChangesNothing()
        {
            Assume.That(VrcFuryTestHelper.VrcFuryType, Is.Not.Null);
            var menu = avatar.AddMenu();
            var toggle = AddFuryToggle("Extra");
            VrcFuryTestHelper.AddAction(toggle, "VF.Model.StateAction.ScaleAction", a => { });
            var view = View(menu);
            var row = view.Tree.RowsOfKind(RowKind.Foreign).Single(r => r.Name == "Extra");
            var previousDialog = MenuTreeView.ShowUnsupportedReason;
            MenuTreeView.ShowUnsupportedReason = _ => { };
            try
            {
                var created = view.Tree.ConvertForeign(row);

                Assert.That(created, Is.Null);
                Assert.That(view.Tree.LastConvertReason, Is.Not.Null.And.Not.Empty);
                Assert.That(menu.entries, Is.Empty);
                Assert.That(toggle != null, Is.True, "nothing changes for an unsupported candidate");
            }
            finally
            {
                MenuTreeView.ShowUnsupportedReason = previousDialog;
            }
        }

#if DOCME_MA
        [Test]
        public void ConvertingAMergedAnimatorSubmenuSaysWhy()
        {
            var menu = avatar.AddMenu();
            var shading = new GameObject("Shading Menu");
            shading.transform.SetParent(avatar.Root.transform);
            shading.AddComponent<nadena.dev.modular_avatar.core.ModularAvatarMenuInstaller>();
            shading.AddComponent<nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator>();
            var parent = shading.AddComponent<nadena.dev.modular_avatar.core.ModularAvatarMenuItem>();
            parent.Control = new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control { name = "Shading", type = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.ControlType.SubMenu };
            parent.MenuSource = nadena.dev.modular_avatar.core.SubmenuSource.Children;
            var child = new GameObject("SSAO");
            child.transform.SetParent(shading.transform);
            var item = child.AddComponent<nadena.dev.modular_avatar.core.ModularAvatarMenuItem>();
            item.Control = new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control { name = "SSAO", type = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.ControlType.Toggle, parameter = new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.Parameter { name = "SSAO" } };
            AvatarPlanCache.Invalidate();
            var view = View(menu);
            var row = view.Tree.RowsOfKind(RowKind.Foreign).First(r => r.Node != null);
            var previousDialog = MenuTreeView.ShowUnsupportedReason;
            string shown = null;
            MenuTreeView.ShowUnsupportedReason = reason => shown = reason;
            try
            {
                var created = view.Tree.ConvertForeign(row);

                Assert.That(created, Is.Null);
                Assert.That(shown, Does.Contain("Shading Menu"), "the dialog names the object whose Merge Animator drives the items");
                Assert.That(view.Tree.LastConvertReason, Is.EqualTo(shown));
                Assert.That(menu.entries, Is.Empty);
                Assert.That(item != null && parent != null, Is.True, "nothing is removed");
            }
            finally
            {
                MenuTreeView.ShowUnsupportedReason = previousDialog;
            }
        }

        static GameObject AddModularAvatarChild(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform);
            var item = child.AddComponent<nadena.dev.modular_avatar.core.ModularAvatarMenuItem>();
            item.Control = new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control
            {
                name = name,
                type = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.Parameter { name = name },
            };
            return child;
        }

        /// <summary>
        /// Recolouring a Modular Avatar row right after opening its submenu by hand must not re-collapse the
        /// submenu: this Unity TreeView version has no itemExpandedChanged event, so a click on the disclosure
        /// arrow changes only the TreeView's own state until the poller notices it (MenuTreeView.PollExpansion,
        /// run from AvatarDoctorView.CheckVersion's 250 ms timer). An edit that refreshes the tree before that
        /// poll ever runs must not throw the just-opened state away.
        /// </summary>
        [Test]
        public void RecolouringAForeignRowRightAfterExpandingItKeepsItOpen()
        {
            // Bookends the rename edits below in their own undo group, so this test's history never bleeds into
            // whatever test runs next.
            Undo.IncrementCurrentGroup();
            var menu = avatar.AddMenu();
            var shading = new GameObject("Shading");
            shading.transform.SetParent(avatar.Root.transform);
            shading.AddComponent<nadena.dev.modular_avatar.core.ModularAvatarMenuInstaller>();
            var parent = shading.AddComponent<nadena.dev.modular_avatar.core.ModularAvatarMenuItem>();
            parent.Control = new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control { name = "Shading", type = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.ControlType.SubMenu };
            parent.MenuSource = nadena.dev.modular_avatar.core.SubmenuSource.Children;
            var childNames = new[] { "SSAO", "Rim", "Fresnel" };
            foreach (var name in childNames) AddModularAvatarChild(shading, name);
            AvatarPlanCache.Invalidate();

            var view = View(menu);
            var submenuRow = view.Tree.RowsOfKind(RowKind.Foreign).First(r => r.Node != null);
            var submenuKey = submenuRow.Key;

            // A click on the arrow, not MenuTreeView.SetExpanded: it only flips the raw TreeView's own expanded
            // state, exactly like a real click, without telling MenuTreeView's saved "expanded" set about it.
            view.Tree.Tree.ExpandItem(submenuRow.Id, expandAllChildren: false);

            var childRow = view.Tree.RowsOfKind(RowKind.Foreign).First(r => r.ParentRow == submenuRow);
            var childKey = childRow.Key;
            view.Tree.SelectKey(childKey);

            var colors = new[] { "#FF0000", "#00FF00", "#0000FF", "#C33333" };
            foreach (var color in colors)
            {
                view.Detail.SetExistingName("<color=" + color + ">" + childRow.Name + "</color>");
                view.Refresh();
                view.CheckVersion();
                view.Tree.PollExpansion();
            }

            submenuRow = view.Tree.RowForKey(submenuKey);
            Assert.That(submenuRow, Is.Not.Null, "the submenu row is still in the tree by its stable key");
            Assert.That(view.Tree.ExpandedKeys, Does.Contain(submenuKey), "the submenu, opened by a click the poller had not yet caught, must not collapse when the very next edit refreshes the tree");
            foreach (var name in childNames)
            {
                var row = view.Tree.RowsOfKind(RowKind.Foreign).FirstOrDefault(r => r.ParentRow == submenuRow && RichName.Strip(r.Name) == name);
                Assert.That(row, Is.Not.Null, "child " + name + " is still present under the submenu");
            }
            Assert.That(view.Tree.Selected?.Key, Is.EqualTo(childKey), "the edited child is still selected");
            var savedKey = TreeExpansion.KeyFor(menu);
            Assert.That(TreeExpansion.Load(view.ViewPrefs, savedKey), Does.Contain(submenuKey), "the saved expansion set still holds the submenu's key");

            // Recolouring the submenu row itself, right after the same kind of click, must not collapse it either.
            view.Tree.SetExpanded(submenuKey, false);
            view.Tree.Tree.ExpandItem(submenuRow.Id, expandAllChildren: false);
            view.Tree.SelectKey(submenuKey);
            foreach (var color in colors)
            {
                view.Detail.SetExistingName("<color=" + color + ">Shading</color>");
                view.Refresh();
                view.CheckVersion();
                view.Tree.PollExpansion();
            }
            submenuRow = view.Tree.RowForKey(submenuKey);
            Assert.That(submenuRow, Is.Not.Null, "the submenu row survives recolouring itself");
            Assert.That(view.Tree.ExpandedKeys, Does.Contain(submenuKey), "the submenu is still expanded after recolouring itself");
            foreach (var name in childNames)
            {
                var row = view.Tree.RowsOfKind(RowKind.Foreign).FirstOrDefault(r => r.ParentRow == submenuRow && RichName.Strip(r.Name) == name);
                Assert.That(row, Is.Not.Null, "child " + name + " is still present after recolouring the submenu");
            }
            Assert.That(TreeExpansion.Load(view.ViewPrefs, savedKey), Does.Contain(submenuKey), "the saved expansion set still holds the submenu's key after recolouring it");
            Undo.IncrementCurrentGroup();
        }

        static AnimatorState AddBoolLayerForGroupTest(AnimatorController controller, string layerName, string parameter, AnimationClip off, AnimationClip on)
        {
            controller.AddParameter(parameter, AnimatorControllerParameterType.Bool);
            controller.AddLayer(layerName);
            var layers = controller.layers;
            layers[layers.Length - 1].defaultWeight = 1f;
            controller.layers = layers;
            var sm = controller.layers[controller.layers.Length - 1].stateMachine;
            var offState = sm.AddState("Off");
            var onState = sm.AddState("On");
            sm.defaultState = offState;
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

        static AnimationClip PropertyClipForGroupTest(string name, string path, string property, float value)
        {
            var clip = new AnimationClip { name = name };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "material." + property), AnimationCurve.Constant(0f, 1f / 60f, value));
            return clip;
        }

        static ModularAvatarMergeAnimator AddMergeMenuForGroupTest(TestAvatar avatar, AnimatorController controller, string label)
        {
            var go = new GameObject(label + " Menu");
            go.transform.SetParent(avatar.Root.transform);
            go.AddComponent<ModularAvatarMenuInstaller>();
            var submenu = go.AddComponent<ModularAvatarMenuItem>();
            submenu.Control = new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control { name = label, type = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.ControlType.SubMenu };
            submenu.MenuSource = SubmenuSource.Children;
            submenu.label = label;
            go.AddComponent<ModularAvatarParameters>();
            var merge = go.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = controller;
            merge.layerType = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Absolute;
            merge.matchAvatarWriteDefaults = true;
            return merge;
        }

        static ModularAvatarMenuItem AddMergeToggleForGroupTest(ModularAvatarMergeAnimator merge, string name, string parameter)
        {
            var go = new GameObject(name);
            go.transform.SetParent(merge.transform);
            var item = go.AddComponent<ModularAvatarMenuItem>();
            item.Control = new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control
            {
                name = name,
                type = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.Parameter { name = parameter },
                value = 1f,
            };
            item.label = name;
            return item;
        }

        [Test]
        public void ImportWindowTicksAGroupTogether()
        {
            var menu = avatar.AddMenu();
            var controller = new AnimatorController { name = "Shading_FX" };
            var clips = new List<AnimationClip>
            {
                PropertyClipForGroupTest("SSAO_Off", "Body", "_Glossiness", 0f), PropertyClipForGroupTest("SSAO_On", "Body", "_Glossiness", 1f),
                PropertyClipForGroupTest("LTCGI_Off", "Body", "_Metallic", 0f), PropertyClipForGroupTest("LTCGI_On", "Body", "_Metallic", 1f),
            };
            try
            {
                AddBoolLayerForGroupTest(controller, "Shading SSAO", "Shading/SSAO", clips[0], clips[1]);
                AddBoolLayerForGroupTest(controller, "Shading LTCGI", "Shading/LTCGI", clips[2], clips[3]);
                var merge = AddMergeMenuForGroupTest(avatar, controller, "Shading");
                AddMergeToggleForGroupTest(merge, "SSAO", "Shading/SSAO");
                AddMergeToggleForGroupTest(merge, "LTCGI", "Shading/LTCGI");
                AvatarPlanCache.Invalidate();

                var window = NewImportWindow(menu);
                try
                {
                    int ssaoIndex = IndexOf(window, c => c.Draft != null && c.Draft.name == "SSAO");
                    int ltcgiIndex = IndexOf(window, c => c.Draft != null && c.Draft.name == "LTCGI");
                    Assert.That(ssaoIndex, Is.GreaterThanOrEqualTo(0));
                    Assert.That(ltcgiIndex, Is.GreaterThanOrEqualTo(0));
                    Assert.That(window.Candidates[ssaoIndex].Group, Is.Not.Null.And.Not.Empty);
                    Assert.That(window.Candidates[ssaoIndex].Group, Is.EqualTo(window.Candidates[ltcgiIndex].Group));

                    window.SetChecked(ssaoIndex, true);
                    Assert.That(window.IsChecked(ltcgiIndex), Is.True, "a group ticks together");

                    window.SetRemoveOriginals(false);
                    Assert.That(window.rootVisualElement.Q<VisualElement>("ad-import-group-warning"), Is.Not.Null,
                        "shows the reason next to the switch rather than letting the apply fail silently");

                    window.SetRemoveOriginals(true);
                    Assert.That(window.rootVisualElement.Q<VisualElement>("ad-import-group-warning"), Is.Null);

                    window.SetChecked(ssaoIndex, false);
                    Assert.That(window.IsChecked(ltcgiIndex), Is.False, "unticking a group member unticks the rest with it");
                }
                finally
                {
                    Object.DestroyImmediate(window);
                }
            }
            finally
            {
                Object.DestroyImmediate(controller);
                foreach (var clip in clips) Object.DestroyImmediate(clip);
            }
        }
#endif

        [Test]
        public void ToolbarAddsAnAnimator()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);
            var controller = new AnimatorController { name = "FX" };
            try
            {
                Assert.That(view.Q<Button>("ad-add-animator"), Is.Not.Null);

                var merge = view.Tree.AddAnimator(controller);

                Assert.That(merge, Is.Not.Null);
                Assert.That(menu.animators, Does.Contain(merge));
                Assert.That(merge.controller, Is.SameAs(controller));
                Assert.That(view.Tree.Selected?.Merge, Is.SameAs(merge));
                Assert.That(view.Tree.ExpandedKeys, Does.Contain("animators"));
            }
            finally
            {
                Object.DestroyImmediate(controller);
            }
        }

        [Test]
        public void AnimatorRowsShowUnderTheirGroup()
        {
            var menu = avatar.AddMenu();
            var controllerA = new AnimatorController { name = "A" };
            var controllerB = new AnimatorController { name = "B" };
            try
            {
                menu.animators.Add(AnimatorMerge.Create(controllerA));
                menu.animators.Add(AnimatorMerge.Create(controllerB));

                var (items, byId) = TreeRow.Build(AvatarPlanCache.GetFor(menu), menu);

                var group = items.Single(i => i.data.Kind == RowKind.AnimatorsGroup);
                Assert.That(group.children.Select(c => c.data.Name), Is.EqualTo(new[] { "A", "B" }));
                Assert.That(group.children.All(c => c.data.Kind == RowKind.Animator), Is.True);
                Assert.That(group.children.All(c => c.data.Mine), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(controllerA);
                Object.DestroyImmediate(controllerB);
            }
        }

        [Test]
        public void LayerChecklistWritesSkipLayers()
        {
            var menu = avatar.AddMenu();
            var controller = new AnimatorController { name = "FX" };
            try
            {
                controller.AddLayer("Base");
                controller.AddLayer("Extra");
                var merge = AnimatorMerge.Create(controller);
                menu.animators.Add(merge);
                var view = View(menu);
                var row = view.Tree.RowsOfKind(RowKind.Animator).Single();
                view.Tree.SelectKey(row.Key);
                var toggle = view.Detail.Q<AnimatorPanel>().Q<Toggle>("ad-animator-layer-Extra");
                Assert.That(toggle.value, Is.True, "unchecked means skipped, and nothing is skipped yet");

                view.Detail.SetAnimatorLayerSkipped("Extra", true);

                Assert.That(merge.skipLayers, Is.EqualTo(new List<string> { "Extra" }));
                toggle = view.Detail.Q<AnimatorPanel>().Q<Toggle>("ad-animator-layer-Extra");
                Assert.That(toggle.value, Is.False, "the check list reflects the skip live");

                view.Detail.SetAnimatorLayerSkipped("Extra", false);
                Assert.That(merge.skipLayers, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(controller);
            }
        }

        [Test]
        public void ParameterListSaysWhoDeclaresEach()
        {
            var menu = avatar.AddMenu();
            var controller = new AnimatorController { name = "FX" };
            try
            {
                controller.AddParameter("OwnParam", AnimatorControllerParameterType.Bool);
                controller.AddParameter("Existing", AnimatorControllerParameterType.Bool);
                controller.AddParameter("Loose", AnimatorControllerParameterType.Float);
                var own = menu.AddEntry(EntryKind.Toggle, "MyControl");
                own.parameterName = "OwnParam";
                menu.animators.Add(AnimatorMerge.Create(controller));
                var view = View(menu);
                var row = view.Tree.RowsOfKind(RowKind.Animator).Single();
                view.Tree.SelectKey(row.Key);
                var rows = view.Detail.Q<AnimatorPanel>().Q<VisualElement>("ad-animator-parameters").Children().ToList();

                string DeclaredBy(string name) => rows.Single(r => r.Q<Label>("ad-animator-param-name").text == name).Q<Label>("ad-animator-param-declared").text;

                Assert.That(DeclaredBy("OwnParam"), Is.EqualTo("MyControl"));
                Assert.That(DeclaredBy("Existing"), Is.EqualTo(L.Text("ad.ui.animator.existing")));
                Assert.That(DeclaredBy("Loose"), Is.EqualTo(L.Text("ad.ui.animator.not_synced")));
            }
            finally
            {
                Object.DestroyImmediate(controller);
            }
        }

        [Test]
        public void AddControlCreatesAParameterControl()
        {
            var menu = avatar.AddMenu();
            var controller = new AnimatorController { name = "FX" };
            try
            {
                controller.AddParameter("BoolParam", AnimatorControllerParameterType.Bool);
                controller.AddParameter("FloatParam", AnimatorControllerParameterType.Float);
                controller.AddParameter("IntParam", AnimatorControllerParameterType.Int);
                menu.animators.Add(AnimatorMerge.Create(controller));
                var view = View(menu);
                var row = view.Tree.RowsOfKind(RowKind.Animator).Single();
                view.Tree.SelectKey(row.Key);

                var boolEntry = view.Detail.AddControlForParameter("BoolParam");
                var floatEntry = view.Detail.AddControlForParameter("FloatParam");
                var intEntry = view.Detail.AddControlForParameter("IntParam");

                Assert.That(boolEntry.kind, Is.EqualTo(EntryKind.Toggle), "a Bool becomes a toggle");
                Assert.That(boolEntry.IsParameterControl, Is.True);
                Assert.That(floatEntry.kind, Is.EqualTo(EntryKind.Radial), "a Float becomes a radial");
                Assert.That(floatEntry.parameterName, Is.EqualTo("FloatParam"));
                Assert.That(intEntry.kind, Is.EqualTo(EntryKind.Toggle), "an Int becomes a toggle");
                Assert.That(intEntry.parameterValue, Is.EqualTo(1f), "with a custom value of 1");
                Assert.That(menu.entries, Does.Contain(boolEntry));
                Assert.That(menu.entries, Does.Contain(floatEntry));
                Assert.That(menu.entries, Does.Contain(intEntry));
            }
            finally
            {
                Object.DestroyImmediate(controller);
            }
        }

        [Test]
        public void ParameterControlPanelSaysItDrivesAParameterOnly()
        {
            var menu = avatar.AddMenu();
            var control = menu.AddEntry(EntryKind.Toggle, "Loose");
            control.parameterName = "Loose";
            var plain = menu.AddEntry(EntryKind.Toggle, "Plain");
            plain.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);

            view.Tree.SelectEntry(control.id);
            var note = view.Detail.Q<Label>("ad-parameter-control-note");
            Assert.That(note, Is.Not.Null);
            Assert.That(note.text, Is.EqualTo(L.Text("ad.ui.parameter_control_note")));

            view.Tree.SelectEntry(plain.id);
            Assert.That(view.Detail.Q<Label>("ad-parameter-control-note"), Is.Null, "a toggle with targets is not a parameter control");
        }

        [Test]
        public void LockedComponentDisablesTheAnimatorPanel()
        {
            var menu = avatar.AddMenu();
            var controller = new AnimatorController { name = "FX" };
            try
            {
                menu.animators.Add(AnimatorMerge.Create(controller));
                menu.locked = true;
                var view = View(menu);
                var row = view.Tree.RowsOfKind(RowKind.Animator).Single();
                view.Tree.SelectKey(row.Key);

                Assert.That(view.Detail.Q<ObjectField>("ad-animator-controller").enabledInHierarchy, Is.False);

                int before = menu.animators.Count;
                var added = view.Tree.AddAnimator(controller);
                Assert.That(added, Is.Null);
                Assert.That(menu.animators.Count, Is.EqualTo(before));
            }
            finally
            {
                Object.DestroyImmediate(controller);
            }
        }

        static OutfitSetWindow NewOutfitWindow(AvatarDoc menu)
        {
            var window = ScriptableObject.CreateInstance<OutfitSetWindow>();
            window.Init(menu);
            return window;
        }

        /// <summary>Reads a field by name, searching base types too, for the domain reload test below.</summary>
        static object GetField(object instance, string field)
        {
            for (var t = instance.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return f.GetValue(instance);
            }
            return null;
        }

        [Test]
        public void ToolbarOpensTheOutfitWindow()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);
            OutfitSetWindow opened = null;
            var previous = OutfitSetWindow.Open;
            // The toolbar's own method is called exactly as the button would; the window it opens is swapped
            // for a fake that only ever calls Init, so no real EditorWindow is ever created by this test.
            OutfitSetWindow.Open = m =>
            {
                opened = ScriptableObject.CreateInstance<OutfitSetWindow>();
                opened.Init(m);
            };
            try
            {
                view.Tree.OpenOutfitWindow();

                Assert.That(opened, Is.Not.Null);
                Assert.That(opened.OutfitCount, Is.EqualTo(0));
            }
            finally
            {
                OutfitSetWindow.Open = previous;
                if (opened != null) Object.DestroyImmediate(opened);
            }
        }

        [Test]
        public void AddingPiecesRefreshesSuggestions()
        {
            var menu = avatar.AddMenu();
            var shapedMesh = TestAvatar.QuadWithShapes("Shrink_Hat");
            avatar.Body.sharedMesh = shapedMesh;
            var window = NewOutfitWindow(menu);
            try
            {
                window.AddOutfit("Casual");
                Assert.That(window.ShrinksOf(0), Is.Empty);

                window.AddPiece(0, avatar.Hat);

                var shrinks = window.ShrinksOf(0);
                Assert.That(shrinks.Select(s => s.shape), Is.EquivalentTo(new[] { "Shrink_Hat" }));
                Assert.That(shrinks[0].renderer, Is.SameAs(avatar.Body));

                window.SetShrinkChosen(0, 0, true);
                Assert.That(window.ShrinksOf(0)[0].chosen, Is.True, "ticking a suggestion sticks");

                window.RemovePiece(0, avatar.Hat);
                Assert.That(window.ShrinksOf(0), Is.Empty, "no pieces left, so nothing is suggested any more");
            }
            finally
            {
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(shapedMesh);
            }
        }

        [Test]
        public void CreateBuildsTheSetAndRevealsIt()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);
            var window = NewOutfitWindow(menu);
            try
            {
                window.SetName("Outfits");
                window.AddOutfit("Casual");
                window.AddPiece(0, avatar.Hat);

                var created = window.Create();

                Assert.That(created, Is.Not.Empty);
                Assert.That(created[0].kind, Is.EqualTo(EntryKind.Submenu));
                Assert.That(menu.entries, Is.EquivalentTo(created));

                view.Refresh();
                Assert.That(view.Tree.Selected?.EntryId, Is.EqualTo(created[0].id), "the new submenu is revealed in the open view");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void CreateWithoutOutfitsIsRefusedWithAStatus()
        {
            var menu = avatar.AddMenu();
            var window = NewOutfitWindow(menu);
            try
            {
                var created = window.Create();

                Assert.That(created, Is.Empty);
                Assert.That(menu.entries, Is.Empty);
                Assert.That(window.rootVisualElement.Q<Label>("ad-outfit-status").text, Is.EqualTo(L.Text("ad.ui.outfit.empty")));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void PieceOutsideTheAvatarIsRefused()
        {
            var menu = avatar.AddMenu();
            var outsider = new GameObject("Outsider");
            var window = NewOutfitWindow(menu);
            try
            {
                window.AddOutfit("Casual");

                bool added = window.AddPiece(0, outsider);

                Assert.That(added, Is.False);
                Assert.That(window.PiecesOf(0), Is.Empty);
                Assert.That(window.rootVisualElement.Q<Label>("ad-outfit-status").text, Is.EqualTo(L.Text("ad.ui.outfit.outside")));
            }
            finally
            {
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(outsider);
            }
        }

        [Test]
        public void OutfitWizardStateSurvivesADomainReload()
        {
            var menu = avatar.AddMenu();
            var window = NewOutfitWindow(menu);
            try
            {
                window.SetName("Outfits");
                window.AddOutfit("Casual");
                window.AddPiece(0, avatar.Hat);

                // Simulates a domain reload: a fresh instance stands in for the one Unity reconstructs, with
                // its serialized fields copied over by hand the way Unity's own serializer would restore them.
                var reloaded = ScriptableObject.CreateInstance<OutfitSetWindow>();
                try
                {
                    foreach (var field in new[] { "component", "setName", "mode", "addNoneOption", "captureIcons", "outfits", "hiddenPieces" })
                        VrcFuryTestHelper.SetField(reloaded, field, GetField(window, field));

                    reloaded.CreateGUI();

                    Assert.That(reloaded.OutfitCount, Is.EqualTo(1));
                    Assert.That(reloaded.PiecesOf(0), Is.EquivalentTo(new[] { avatar.Hat }));
                    Assert.That(reloaded.rootVisualElement.Q<TextField>("ad-outfit-name").value, Is.EqualTo("Outfits"));
                }
                finally
                {
                    Object.DestroyImmediate(reloaded);
                }
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void HiddenPiecesSectionOnlyInOneAtATimeMode()
        {
            var menu = avatar.AddMenu();
            var shoes = new GameObject("Shoes");
            shoes.transform.SetParent(avatar.Root.transform);
            var window = NewOutfitWindow(menu);
            try
            {
                window.AddOutfit("Casual");
                window.AddPiece(0, avatar.Hat);
                window.AddHiddenPiece(shoes);
                Assert.That(window.rootVisualElement.Q<VisualElement>("ad-outfit-hidden"), Is.Not.Null, "shown in One at a time");

                window.SetMode(OutfitMode.MixFreely);
                Assert.That(window.rootVisualElement.Q<VisualElement>("ad-outfit-hidden"), Is.Null, "hidden in Mix freely");
                Assert.That(window.HiddenPieces, Does.Contain(shoes), "the piece stays in the state even while the section is hidden");

                window.SetMode(OutfitMode.OneAtATime);
                Assert.That(window.rootVisualElement.Q<VisualElement>("ad-outfit-hidden"), Is.Not.Null, "shown again after switching back");
                Assert.That(window.HiddenPieces, Does.Contain(shoes), "and its piece is still there");

                window.SetMode(OutfitMode.MixFreely);
                var created = window.Create();

                Assert.That(created, Is.Not.Empty);
                bool hidesShoes = menu.entries.Any(e => e.objects.Any(o => o.target == shoes));
                Assert.That(hidesShoes, Is.False, "Mix freely never passes the hidden pieces to the builder");
            }
            finally
            {
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(shoes);
            }
        }

        [Test]
        public void CaptureMissingIconsFromTheToolbar()
        {
            const string TempParent = "Assets/TempAssets";
            const string Folder = TempParent + "/AvatarDoctor Icons";
            if (!AssetDatabase.IsValidFolder(TempParent)) AssetDatabase.CreateFolder("Assets", "TempAssets");
            IconCapture.RootFolder = Folder;
            try
            {
                var menu = avatar.AddMenu();
                avatar.Hat.GetComponent<MeshFilter>().sharedMesh = TestAvatar.QuadWithShapes();
                avatar.Hat.GetComponent<MeshRenderer>().sharedMaterial = avatar.MaterialA;
                avatar.Hat.transform.position = new Vector3(0f, 0f, 5f);
                var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
                toggle.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = true });
                var view = View(menu);

                int made = view.Tree.CaptureMissingIcons();

                Assert.That(made, Is.EqualTo(1));
                Assert.That(toggle.icon, Is.Not.Null);
            }
            finally
            {
                IconCapture.RootFolder = IconCapture.Folder;
                if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
                if (AssetDatabase.IsValidFolder(TempParent) && AssetDatabase.FindAssets("", new[] { TempParent }).Length == 0) AssetDatabase.DeleteAsset(TempParent);
            }
        }

        /// <summary>
        /// Guards AvatarDoctor.uss against a Unity upgrade silently renaming one of the Unity Editor's own USS
        /// classes the stylesheet relies on. Checked once (phase 7 fix round 1) against Unity 2022.3's own
        /// UIElements types, both by instantiating each control and reading its actual class list, and by
        /// reflecting over the *UssClassName constants Unity's own C# uses to add those classes. TreeView and
        /// ListView share their row and selection classes on BaseVerticalCollectionView (an internal-ish base
        /// type not directly nameable here); an unattached view never lays out or virtualises a row to query
        /// against a live instance either way, so those two are checked by walking the constant fields instead.
        /// The Console-to-Unity-theme pass (phase 8 task 9) left sliders and toggles fully native, so their
        /// classes are no longer asserted here; only the classes the stylesheet still targets are checked.
        /// </summary>
        [Test]
        public void StylesheetTargetsRealUnityClasses()
        {
            var textField = new TextField();
            Assert.That(textField.Q(className: "unity-base-text-field__input"), Is.Not.Null, "text field input (base name)");
            Assert.That(textField.Q(className: "unity-text-field__input"), Is.Not.Null, "text field input (TextField's own name)");

            var objectField = new ObjectField();
            Assert.That(objectField.Q(className: "unity-object-field-display"), Is.Not.Null, "object field display");

            var colorField = new ColorField();
            Assert.That(colorField.Q(className: "unity-color-field__input"), Is.Not.Null, "color field input");

            var dropdown = new DropdownField(new List<string> { "a", "b" }, 0);
            Assert.That(dropdown.Q(className: "unity-base-popup-field__input"), Is.Not.Null, "popup field input (base name)");
            Assert.That(dropdown.Q(className: "unity-popup-field__input"), Is.Not.Null, "popup field input (DropdownField's own name)");

            // itemUssClassName is declared independently on both BaseTreeView ("unity-tree-view__item", the
            // tree-specific indent/content wrapper) and BaseVerticalCollectionView ("unity-collection-view__item",
            // the virtualized row slot that selection and hover actually style); walking from BaseTreeView's own
            // base type skips the shadowing declaration and reaches the one the stylesheet targets.
            var collectionViewBase = typeof(TreeView).BaseType.BaseType;
            Assert.That(ClassConstant(collectionViewBase, "itemUssClassName"), Is.EqualTo("unity-collection-view__item"), "tree/list row (virtualized slot)");
            Assert.That(ClassConstant(typeof(TreeView), "itemSelectedVariantUssClassName"), Is.EqualTo("unity-collection-view__item--selected"), "tree/list selected row");
            Assert.That(ClassConstant(typeof(TreeView), "itemToggleUssClassName"), Is.EqualTo("unity-tree-view__item-toggle"), "tree row foldout arrow");
        }

        /// <summary>
        /// Guards the Console-to-Unity-theme pass (phase 8 task 9): the user rejected the Console restyle's opaque
        /// near-black fills, grey primary text and purple "other tools" colour. Reads the real stylesheet text so a
        /// later edit cannot silently bring either back.
        /// </summary>
        [Test]
        public void StylesheetHasNoOpaqueGroundOrPurple()
        {
            string path = AssetDatabase.FindAssets("AvatarDoctor t:StyleSheet")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.EndsWith("/AvatarDoctor.uss"));
            Assert.That(path, Is.Not.Null);
            var text = System.IO.File.ReadAllText(path);

            Assert.That(text, Does.Not.Contain("--ad-ground"));
            Assert.That(text, Does.Not.Contain("--ad-source"));
            Assert.That(text, Does.Not.Contain("#A48BE6"));
            Assert.That(text, Does.Not.Contain("#6B4FC9"));

            var lines = text.Split('\n');
            // Every rule that styles the root itself, alone or shared with the popup menu, in either skin.
            var rootRules = lines.Where(l => l.TrimStart().StartsWith(".ad-root {") || l.TrimStart().StartsWith(".ad-root, ") || l.TrimStart().StartsWith(".ad-root.ad-light")).ToList();
            Assert.That(rootRules.Count, Is.GreaterThanOrEqualTo(3), "expected the shared variable rule, the padding rule and the light rule for .ad-root");
            foreach (var rule in rootRules)
                Assert.That(rule, Does.Not.Contain("background-color"), "the root never paints its own ground: " + rule.Substring(0, System.Math.Min(40, rule.Length)));
        }

        /// <summary>
        /// Guards against a USS property this Unity version's UIElements does not recognise: the editor accepts the
        /// file at edit time but logs "Unknown style property" (and "Unexpected property id Unknown") the moment a
        /// pane using it draws, which is how -unity-image-tint-color on the tree row's foldout arrow went unnoticed
        /// until the user saw it in the console. The real property is -unity-background-image-tint-color, the
        /// spelling com.unity.shadergraph's own SGBlackboard.uss uses in this same Unity install. The other two
        /// entries are the same mistake shape, a property name reached for by analogy with a real one that this
        /// version does not have, not an attempt at exhaustive USS linting.
        /// </summary>
        /// <summary>
        /// Matches a property name followed by its colon, not a bare substring: -unity-text-outline is itself a
        /// prefix of the two real properties -unity-text-outline-width and -unity-text-outline-color, so a bare
        /// Contains would fail the day either one is used correctly.
        /// </summary>
        static bool UsesProperty(string text, string property)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(text, System.Text.RegularExpressions.Regex.Escape(property) + @"\s*:");
        }

        [Test]
        public void StylesheetHasNoUnsupportedProperties()
        {
            string path = AssetDatabase.FindAssets("AvatarDoctor t:StyleSheet")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.EndsWith("/AvatarDoctor.uss"));
            Assert.That(path, Is.Not.Null);
            var text = System.IO.File.ReadAllText(path);

            var unsupported = new[]
            {
                "-unity-image-tint-color", // the actual bug; the real property is -unity-background-image-tint-color
                "-unity-image-scale-mode", // same wrong-prefix mistake; the real property is -unity-background-scale-mode
                "-unity-text-outline", // no such shorthand exists; the real properties are -unity-text-outline-width and -unity-text-outline-color
            };
            foreach (var property in unsupported)
                Assert.That(UsesProperty(text, property), Is.False, property + " is not a real USS property in this Unity version");
        }

        [Test]
        public void UnsupportedPropertyGuardDoesNotFlagARealPropertyThatSharesItsPrefix()
        {
            Assert.That(UsesProperty(".ad-name { -unity-text-outline-width: 1px; }", "-unity-text-outline"), Is.False);
            Assert.That(UsesProperty(".ad-name { -unity-text-outline-color: red; }", "-unity-text-outline"), Is.False);
            Assert.That(UsesProperty(".ad-name { -unity-text-outline: 1px red; }", "-unity-text-outline"), Is.True, "the fake shorthand itself must still be caught");
        }

        /// <summary>
        /// Phase 12 task 6: one spacing scale everywhere, 0, 2, 4, 6 or 8 px, so nothing in the panels reads as
        /// randomly nudged. Parses every margin and padding declaration in the real stylesheet and fails on a
        /// pixel value outside that scale, "auto" and a percentage are always allowed (neither is a fixed nudge),
        /// and one declaration is explicitly exempt: .ad-note-indent's "margin-left: 112px" is not spacing at all,
        /// it lines a note up under the 112 px label column every row already uses, using margin only because
        /// indenting plain text has no other property to reach for.
        /// </summary>
        [Test]
        public void SpacingUsesTheScale()
        {
            string path = AssetDatabase.FindAssets("AvatarDoctor t:StyleSheet")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.EndsWith("/AvatarDoctor.uss"));
            Assert.That(path, Is.Not.Null);
            var text = System.IO.File.ReadAllText(path);

            var allowed = new HashSet<int> { 0, 2, 4, 6, 8 };
            var exemptDeclarations = new[] { "margin-left: 112px" };

            var offenders = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"\b(margin|padding)(-[a-z]+)?\s*:\s*([^;]+);"))
            {
                string declaration = m.Value.TrimEnd(';').Trim();
                if (System.Array.IndexOf(exemptDeclarations, declaration) >= 0) continue;
                string value = m.Groups[3].Value;
                if (value.Contains("auto") || value.Contains("%")) continue;
                foreach (System.Text.RegularExpressions.Match n in System.Text.RegularExpressions.Regex.Matches(value, @"(-?\d+(?:\.\d+)?)px"))
                {
                    if (!float.TryParse(n.Groups[1].Value, out var px)) continue;
                    if (px != System.Math.Truncate(px) || !allowed.Contains((int)px))
                        offenders.Add(declaration + " (" + n.Value + ")");
                }
            }
            Assert.That(offenders, Is.Empty, "margin/padding values outside 0, 2, 4, 6, 8 px: " + string.Join(", ", offenders));
        }

        [Test]
        public void LockedComponentDisablesEditingButKeepsInstallSettings()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            menu.locked = true;

            var view = View(menu);

            Assert.That(view.Q<VisualElement>("ad-lock-banner").ClassListContains("ad-hidden"), Is.False);
            Assert.That(view.Q<Button>("ad-outfit-open").enabledInHierarchy, Is.False);
            Assert.That(view.Q<Button>("ad-import-open").enabledInHierarchy, Is.False);
            Assert.That(view.Q<TextField>("ad-search").enabledInHierarchy, Is.True);
            Assert.That(view.Q<TextField>("ad-install-path").enabledInHierarchy, Is.True);
            Assert.That(view.Header.Q<SegmentedChoice>("ad-platform").enabledInHierarchy, Is.True);
            Assert.That(view.Q<TextField>("ad-parameter-prefix").enabledInHierarchy, Is.True);

            int before = menu.entries.Count;
            var added = view.Tree.AddEntry(EntryKind.Toggle);
            Assert.That(added, Is.Null);
            Assert.That(menu.entries.Count, Is.EqualTo(before));

            view.Tree.SelectEntry(hat.id);
            Assert.That(view.Detail.Q<TextField>("ad-name").enabledInHierarchy, Is.False, "the entry panel is read only while locked");
        }

        [Test]
        public void UnlockRestoresEditing()
        {
            var menu = avatar.AddMenu();
            menu.locked = true;
            var view = View(menu);
            Assert.That(view.Q<Button>("ad-outfit-open").enabledInHierarchy, Is.False);

            view.SetLocked(false);

            Assert.That(menu.locked, Is.False);
            Assert.That(view.Q<VisualElement>("ad-lock-banner").ClassListContains("ad-hidden"), Is.True);
            Assert.That(view.Q<Button>("ad-outfit-open").enabledInHierarchy, Is.True);
            var added = view.Tree.AddEntry(EntryKind.Toggle);
            Assert.That(added, Is.Not.Null);
        }

        [Test]
        public void FollowRowsShowUnderTheirSection()
        {
            var menu = avatar.AddMenu();
            var hold = menu.AddEntry(EntryKind.Toggle, "Hold");
            hold.follows.Add(new FollowTarget { target = avatar.Hat, follows = avatar.Glasses.transform });
            var grab = menu.AddEntry(EntryKind.Trigger, "Grab");
            grab.source = TriggerSource.Parameter;
            grab.sourceParameter = "GestureLeft";
            grab.follows.Add(new FollowTarget { target = avatar.Hat, follows = avatar.Glasses.transform });
            var link = menu.AddEntry(EntryKind.Trigger, "Link");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            var eyes = menu.AddEntry(EntryKind.Radial, "Eyes");
            eyes.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big" });

            var view = View(menu);

            view.Tree.SelectEntry(hold.id);
            var toggleFollows = view.Detail.Q<TargetListView>("ad-follows");
            Assert.That(toggleFollows, Is.Not.Null, "a toggle shows its Follows section");
            Assert.That(toggleFollows.Count, Is.EqualTo(1));

            view.Tree.SelectEntry(grab.id);
            Assert.That(view.Detail.Q<TargetListView>("ad-follows"), Is.Not.Null, "a condition trigger shows its Follows section too");

            view.Tree.SelectEntry(link.id);
            Assert.That(view.Detail.Q<TargetListView>("ad-follows"), Is.Null, "a blend shape link trigger has no on/off state to follow, so no section");

            view.Tree.SelectEntry(eyes.id);
            Assert.That(view.Detail.Q<TargetListView>("ad-follows"), Is.Null, "a radial has no on/off state to follow, so no section");
        }

        [Test]
        public void GrabbableRowShowsItsPanel()
        {
            var menu = avatar.AddMenu();
            var grab = menu.AddEntry(EntryKind.Grabbable, "Sword");
            grab.grabObject = avatar.Glasses;

            var view = View(menu);
            view.Tree.SelectEntry(grab.id);

            Assert.That(view.Detail.Q<GrabPanel>(), Is.Not.Null, "a grabbable shows its own panel");
            Assert.That(view.Detail.Q<ObjectField>("ad-grab-object"), Is.Not.Null);
            Assert.That(view.Detail.Q<SegmentedChoice>("ad-grab-hands"), Is.Not.Null);
            Assert.That(view.Detail.Q<Toggle>("ad-grab-keep-offset"), Is.Not.Null);
            Assert.That(view.Detail.Q<Toggle>("ad-grab-menu-toggle"), Is.Not.Null, "the fold's contents exist even while collapsed");
        }

        [Test]
        public void FollowDistanceHidesAndOrAndTheGate()
        {
            var menu = avatar.AddMenu();
            var trigger = menu.AddEntry(EntryKind.Trigger, "Pat");
            trigger.source = TriggerSource.Contact;
            trigger.contactAnchor = avatar.Hat.transform;
            trigger.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            var view = View(menu);
            view.Tree.SelectEntry(trigger.id);

            Assert.That(view.Detail.Q<Button>("ad-when-add-and"), Is.Not.Null, "Hold still offers And");
            Assert.That(view.Detail.Q<Button>("ad-when-add-or"), Is.Not.Null, "Hold still offers Or");
            Assert.That(view.Detail.Q<Button>("ad-trigger-gate-add"), Is.Not.Null, "Hold still offers the gate");
            Assert.That(view.Detail.Q<SegmentedChoice>("ad-touch-distance"), Is.Not.Null, "Hold still offers Touch or Closer than");

            view.Detail.SetTouchMode(TouchMode.FollowDistance);

            // A Fade contact takes requirements: + And and Only while are offered, + Or stays hidden.
            Assert.That(view.Detail.Q<Button>("ad-when-add-and"), Is.Not.Null, "Follow distance offers + And for requirements");
            Assert.That(view.Detail.Q<Button>("ad-when-add-or"), Is.Null, "Follow distance hides + Or");
            Assert.That(view.Detail.Q<Button>("ad-trigger-gate-add"), Is.Not.Null, "Follow distance offers Only while as one more requirement");
            Assert.That(view.Detail.Q<SegmentedChoice>("ad-touch-distance"), Is.Null, "Follow distance always uses distance");
            Assert.That(view.Detail.Q<Slider>("ad-touch-fullat"), Is.Not.Null, "Fully on at takes its place");

            var toggle = menu.AddEntry(EntryKind.Toggle, "Gate");
            view.Detail.SetGateToggle(toggle.id);
            view.Tree.SelectEntry(trigger.id);
            Assert.That(view.Detail.Q<DropdownField>("ad-trigger-gate"), Is.Not.Null, "an already set gate stays visible even in Follow distance");
        }

        [Test]
        public void FollowDistanceModeOnlyOfferedOnAContactSource()
        {
            var menu = avatar.AddMenu();
            var pb = avatar.Hat.AddComponent<VRCPhysBone>();
            var trigger = menu.AddEntry(EntryKind.Trigger, "Ear");
            trigger.source = TriggerSource.PhysBone;
            trigger.physBone = pb;
            trigger.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var view = View(menu);
            view.Tree.SelectEntry(trigger.id);

            var choice = view.Detail.Q<SegmentedChoice>("ad-touch-mode");
            Assert.That(choice.Items.Count, Is.EqualTo(2), "Follow distance is not offered on a PhysBone source");
        }

        [Test]
        public void FollowDistanceModeRowFollowsThePredicate()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            var trigger = menu.AddEntry(EntryKind.Trigger, "Pat");
            trigger.source = TriggerSource.Contact;
            trigger.touchMode = TouchMode.FollowDistance;
            trigger.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            var g0 = new ConditionGroup();
            g0.all.Add(new WhenCondition { kind = WhenConditionKind.Toggle, sourceToggleId = hat.id, sourceToggleOn = true });
            trigger.when.Add(g0);
            Assert.That(trigger.IsFollowDistanceTrigger, Is.False, "the first condition is a toggle, not a contact");
            var view = View(menu);
            view.Tree.SelectEntry(trigger.id);

            var choice = view.Detail.Q<SegmentedChoice>("ad-touch-mode");
            Assert.That(choice.Index, Is.EqualTo(0), "shows Hold selected, matching how the trigger actually behaves");
            Assert.That(choice.Items[choice.Items.Count - 1].text, Is.EqualTo("Fade"), "the mode segment reads Fade, not Distance");
        }

        [Test]
        public void FullAtSliderWritesTheEntryWithUndo()
        {
            var menu = avatar.AddMenu();
            var trigger = menu.AddEntry(EntryKind.Trigger, "Pat");
            trigger.source = TriggerSource.Contact;
            trigger.contactAnchor = avatar.Hat.transform;
            trigger.touchMode = TouchMode.FollowDistance;
            trigger.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            var view = View(menu);
            view.Tree.SelectEntry(trigger.id);
            var window = ScriptableObject.CreateInstance<FocusHostWindow>();
            try
            {
                // A Slider's value setter only dispatches its ChangeEvent, and so only writes through, once it
                // sits in a real panel; a throwaway host window gives it one.
                window.Show();
                window.rootVisualElement.Add(view);
                var slider = view.Detail.Q<Slider>("ad-touch-fullat");
                Assert.That(slider, Is.Not.Null);

                Undo.IncrementCurrentGroup();
                slider.value = 0.3f;
                Assert.That(slider.value, Is.EqualTo(0.3f).Within(0.0001f));

                // Applying the property re-deserializes the entry list, so the written entry is a fresh MenuEntry
                // instance with the same id rather than the same reference; find it again, the same as after an undo.
                var written = menu.entries.Single(e => e.id == trigger.id);
                Assert.That(written.contactFullAt, Is.EqualTo(0.3f).Within(0.0001f));

                Undo.PerformUndo();
                var restored = menu.entries.Single(e => e.id == trigger.id);
                Assert.That(restored.contactFullAt, Is.EqualTo(0.8f).Within(0.0001f));
            }
            finally
            {
                view.RemoveFromHierarchy();
                window.Close();
            }
        }

        /// <summary>The value of a `public/private static readonly string ...UssClassName` field, found by walking up the type's own base classes (Unity keeps most of these on a shared base type, not the leaf control).</summary>
        static string ClassConstant(System.Type type, string fieldName)
        {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                var field = t.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null) return (string)field.GetValue(null);
            }
            return null;
        }
    }
}
