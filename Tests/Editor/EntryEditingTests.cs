using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    public class EntryEditingTests
    {
        GameObject root;
        AvatarDoc menu;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Avatar");
            menu = root.AddComponent<AvatarDoc>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
        }

        string[] Order(string parentId = "") => EntryEditing.Siblings(menu, parentId).Select(e => e.name).ToArray();

        [Test]
        public void AddAppendsUnderTheParent()
        {
            var sub = EntryEditing.Add(menu, EntryKind.Submenu, "Sub", "");
            var a = EntryEditing.Add(menu, EntryKind.Toggle, "A", sub.id);
            var b = EntryEditing.Add(menu, EntryKind.Toggle, "B", "");
            Assert.That(Order(), Is.EqualTo(new[] { "Sub", "B" }));
            Assert.That(Order(sub.id), Is.EqualTo(new[] { "A" }));
            Assert.That(a.id, Is.Not.Empty.And.Not.EqualTo(b.id));
        }

        [Test]
        public void DuplicatingASubmenuRemapsPresetTargetsInside()
        {
            var sub = EntryEditing.Add(menu, EntryKind.Submenu, "Pack", "");
            var toggle = EntryEditing.Add(menu, EntryKind.Toggle, "Hat", sub.id);
            var preset = EntryEditing.Add(menu, EntryKind.Preset, "All on", sub.id);
            preset.presetStates.Add(new PresetState { entryId = toggle.id, on = true });

            var copy = EntryEditing.Duplicate(menu, sub);

            var inside = EntryEditing.Descendants(menu, copy);
            var copiedToggle = inside.Single(e => e.kind == EntryKind.Toggle);
            var copiedPreset = inside.Single(e => e.kind == EntryKind.Preset);
            Assert.That(copiedToggle.id, Is.Not.EqualTo(toggle.id));
            Assert.That(copiedPreset.presetStates.Single().entryId, Is.EqualTo(copiedToggle.id), "the copy drives its own controls");
            Assert.That(preset.presetStates.Single().entryId, Is.EqualTo(toggle.id), "the original is left alone");
        }

        [Test]
        public void DuplicatingASubmenuRemapsTriggerSourcesInside()
        {
            var sub = EntryEditing.Add(menu, EntryKind.Submenu, "Pack", "");
            var toggle = EntryEditing.Add(menu, EntryKind.Toggle, "Hat", sub.id);
            var trigger = EntryEditing.Add(menu, EntryKind.Trigger, "Follow", sub.id);
            trigger.source = TriggerSource.Toggle;
            trigger.sourceToggleId = toggle.id;

            var copy = EntryEditing.Duplicate(menu, sub);

            var inside = EntryEditing.Descendants(menu, copy);
            var copiedToggle = inside.Single(e => e.kind == EntryKind.Toggle);
            var copiedTrigger = inside.Single(e => e.kind == EntryKind.Trigger);
            Assert.That(copiedTrigger.sourceToggleId, Is.EqualTo(copiedToggle.id), "the copy watches its own toggle");
            Assert.That(trigger.sourceToggleId, Is.EqualTo(toggle.id), "the original is left alone");
        }

        [Test]
        public void DuplicateCopiesTargetsAndDescendantsWithNewIds()
        {
            var sub = EntryEditing.Add(menu, EntryKind.Submenu, "Sub", "");
            var a = EntryEditing.Add(menu, EntryKind.Toggle, "A", sub.id);
            a.objects.Add(new ObjectTarget { target = root, activeWhenOn = false });
            a.blendShapes.Add(new BlendShapeTarget { shape = "Blush", weight = 40 });

            var copy = EntryEditing.Duplicate(menu, sub);

            Assert.That(copy.name, Is.EqualTo("Sub Copy"));
            Assert.That(copy.id, Is.Not.EqualTo(sub.id));
            Assert.That(Order(), Is.EqualTo(new[] { "Sub", "Sub Copy" }));
            var copiedChild = EntryEditing.Siblings(menu, copy.id).Single();
            Assert.That(copiedChild.name, Is.EqualTo("A"));
            Assert.That(copiedChild.id, Is.Not.EqualTo(a.id));
            Assert.That(copiedChild.objects.Single().target, Is.SameAs(root));
            Assert.That(copiedChild.objects.Single().activeWhenOn, Is.False);
            Assert.That(copiedChild.blendShapes.Single().weight, Is.EqualTo(40f));
            Assert.That(copiedChild.objects, Is.Not.SameAs(a.objects));
        }

        [Test]
        public void DuplicateOfAToggleLandsRightAfterTheOriginal()
        {
            EntryEditing.Add(menu, EntryKind.Toggle, "A", "");
            var b = EntryEditing.Add(menu, EntryKind.Toggle, "B", "");
            EntryEditing.Add(menu, EntryKind.Toggle, "C", "");
            EntryEditing.Duplicate(menu, b);
            Assert.That(Order(), Is.EqualTo(new[] { "A", "B", "B Copy", "C" }));
        }

        [Test]
        public void DuplicateAppendsInsideTheStyle()
        {
            var hat = EntryEditing.Add(menu, EntryKind.Toggle, "<b>Hat</b>", "");
            var copy = EntryEditing.Duplicate(menu, hat);
            Assert.That(copy.name, Is.EqualTo("<b>Hat Copy</b>"));
        }

        [Test]
        public void RenamePropagatesThePlainSegment()
        {
            var outfits = EntryEditing.Add(menu, EntryKind.Submenu, "<b>Outfits</b>", "");
            var pack = new GameObject("Pack"); pack.transform.SetParent(root.transform);
            var packMenu = pack.AddComponent<AvatarDoc>();
            packMenu.installPath = "Outfits/Extra";
            var tree = MenuModel.Build(root.transform, null, new[] { menu, packMenu });

            EntryEditing.RenameSubmenu(menu, outfits, "<b>Clothes</b>", tree);

            Assert.That(outfits.name, Is.EqualTo("<b>Clothes</b>"));
            Assert.That(packMenu.installPath, Is.EqualTo("Clothes/Extra"));
        }

        [Test]
        public void DeleteRemovesDescendants()
        {
            var sub = EntryEditing.Add(menu, EntryKind.Submenu, "Sub", "");
            var inner = EntryEditing.Add(menu, EntryKind.Submenu, "Inner", sub.id);
            EntryEditing.Add(menu, EntryKind.Toggle, "Deep", inner.id);
            EntryEditing.Add(menu, EntryKind.Toggle, "Keep", "");

            EntryEditing.Delete(menu, sub);

            Assert.That(menu.entries.Select(e => e.name), Is.EqualTo(new[] { "Keep" }));
        }

        [Test]
        public void MoveToRefusesOwnDescendant()
        {
            var sub = EntryEditing.Add(menu, EntryKind.Submenu, "Sub", "");
            var inner = EntryEditing.Add(menu, EntryKind.Submenu, "Inner", sub.id);

            Assert.That(EntryEditing.MoveTo(menu, sub, inner.id), Is.False);
            Assert.That(EntryEditing.MoveTo(menu, sub, sub.id), Is.False);
            Assert.That(sub.parentId, Is.EqualTo(""));
            Assert.That(EntryEditing.MoveTo(menu, inner, ""), Is.True);
            Assert.That(Order(), Is.EqualTo(new[] { "Sub", "Inner" }));
        }

        [Test]
        public void ReorderPlacesTheEntryAtTheSiblingIndex()
        {
            var a = EntryEditing.Add(menu, EntryKind.Toggle, "A", "");
            var b = EntryEditing.Add(menu, EntryKind.Toggle, "B", "");
            var c = EntryEditing.Add(menu, EntryKind.Toggle, "C", "");
            var sub = EntryEditing.Add(menu, EntryKind.Submenu, "Sub", "");

            Assert.That(EntryEditing.Reorder(menu, c, "", 0), Is.True);
            Assert.That(Order(), Is.EqualTo(new[] { "C", "A", "B", "Sub" }));
            Assert.That(EntryEditing.Reorder(menu, a, sub.id, 0), Is.True);
            Assert.That(Order(), Is.EqualTo(new[] { "C", "B", "Sub" }));
            Assert.That(Order(sub.id), Is.EqualTo(new[] { "A" }));
            Assert.That(EntryEditing.Reorder(menu, b, sub.id, 5), Is.True, "index past the end appends");
            Assert.That(Order(sub.id), Is.EqualTo(new[] { "A", "B" }));
        }

        [Test]
        public void RenameSubmenuPropagatesToInstallPaths()
        {
            var outfits = EntryEditing.Add(menu, EntryKind.Submenu, "Outfits", "");
            var summer = EntryEditing.Add(menu, EntryKind.Submenu, "Summer", outfits.id);
            var pack = new GameObject("Pack"); pack.transform.SetParent(root.transform);
            var packMenu = pack.AddComponent<AvatarDoc>();
            packMenu.installPath = "Outfits/Summer";
            var other = new GameObject("Other"); other.transform.SetParent(root.transform);
            var otherMenu = other.AddComponent<AvatarDoc>();
            otherMenu.installPath = "OutfitsExtra";
            var tree = MenuModel.Build(root.transform, null, new[] { menu, packMenu, otherMenu });

            EntryEditing.RenameSubmenu(menu, outfits, "Clothes", tree);

            Assert.That(outfits.name, Is.EqualTo("Clothes"));
            Assert.That(packMenu.installPath, Is.EqualTo("Clothes/Summer"));
            Assert.That(otherMenu.installPath, Is.EqualTo("OutfitsExtra"), "prefix match is segment-wise");
        }

        [Test]
        public void EnsureUniqueIdsReassignsCollisions()
        {
            var a = EntryEditing.Add(menu, EntryKind.Toggle, "A", "");
            var b = EntryEditing.Add(menu, EntryKind.Toggle, "B", "");
            b.id = a.id;
            var other = new GameObject("Other").AddComponent<AvatarDoc>();
            other.transform.SetParent(root.transform);
            var c = EntryEditing.Add(other, EntryKind.Toggle, "C", "");
            var d = EntryEditing.Add(menu, EntryKind.Toggle, "D", "");
            d.id = c.id;
            var sub = EntryEditing.Add(menu, EntryKind.Submenu, "Sub", "");
            var child = EntryEditing.Add(menu, EntryKind.Toggle, "Child", sub.id);
            var subCopy = EntryEditing.Add(menu, EntryKind.Submenu, "Sub2", "");
            subCopy.id = sub.id;

            int changed = EntryEditing.EnsureUniqueIds(menu, new[] { other });

            Assert.That(changed, Is.EqualTo(3));
            Assert.That(b.id, Is.Not.EqualTo(a.id));
            Assert.That(d.id, Is.Not.EqualTo(c.id));
            Assert.That(subCopy.id, Is.Not.EqualTo(sub.id));
            Assert.That(child.parentId, Is.EqualTo(sub.id), "children of the first occurrence keep their parent");
        }

        AvatarDoc CopyComponent(string name, params MenuEntry[] entries)
        {
            var copyObject = new GameObject(name);
            copyObject.transform.SetParent(root.transform);
            var copy = copyObject.AddComponent<AvatarDoc>();
            foreach (var entry in entries) copy.entries.Add(JsonUtility.FromJson<MenuEntry>(JsonUtility.ToJson(entry)));
            return copy;
        }

        [Test]
        public void TheFirstOwnerOfARepeatedIdKeepsIt()
        {
            var hat = EntryEditing.Add(menu, EntryKind.Toggle, "Hat", "");
            var original = hat.id;
            var copy = CopyComponent("Copy", hat);

            int changed = EntryEditing.EnsureUniqueIds(menu, new[] { copy });

            Assert.That(changed, Is.EqualTo(1));
            Assert.That(hat.id, Is.EqualTo(original), "other components may name this id, so the original keeps it");
            Assert.That(copy.entries[0].id, Is.Not.EqualTo(original), "the copy is the one that changes");
        }

        [Test]
        public void WhicheverViewOpensFixesEveryComponentOfTheAvatar()
        {
            var hat = EntryEditing.Add(menu, EntryKind.Toggle, "Hat", "");
            var original = hat.id;
            var copy = CopyComponent("Copy", hat);
            var third = CopyComponent("Third");
            EntryEditing.Add(third, EntryKind.Toggle, "Scarf", "");

            int changed = EntryEditing.EnsureUniqueIds(third, new[] { menu, copy });

            Assert.That(changed, Is.EqualTo(1), "the view of a component with nothing wrong still repairs the copy");
            Assert.That(hat.id, Is.EqualTo(original));
            Assert.That(copy.entries[0].id, Is.Not.EqualTo(original));
        }

        [Test]
        public void ALockedComponentKeepsItsIdsAndItsWarning()
        {
            var hat = EntryEditing.Add(menu, EntryKind.Toggle, "Hat", "");
            var copy = CopyComponent("Copy", hat);
            copy.locked = true;

            int changed = EntryEditing.EnsureUniqueIds(menu, new[] { copy });

            Assert.That(changed, Is.Zero, "a locked component is its maker's to change");
            Assert.That(copy.entries[0].id, Is.EqualTo(hat.id));
            var tree = MenuModel.Build(root.transform, null, new[] { menu, copy });
            Assert.That(tree.HasDuplicateIds, Is.True, "and the model still says so");
            Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.duplicate_entry_id"), Is.True);
        }

        [Test]
        public void MergeMenuAssetAndMenuEditIdsAreMadeUniqueToo()
        {
            menu.animators.Add(AnimatorMerge.Create(null));
            menu.menuAssets.Add(MenuAssetInstall.Create(null));
            var edit = MenuEdit.Create(new[] { "Old" });
            menu.menuEdits.Add(edit);
            menu.removedLayers.Add(new RemovedLayer { layer = "HatLayer", editId = edit.id });
            var mergeId = menu.animators[0].id;
            var installId = menu.menuAssets[0].id;
            var editId = edit.id;
            var copy = CopyComponent("Copy");
            EditorUtility.CopySerialized(menu, copy);

            int changed = EntryEditing.EnsureUniqueIds(menu, new[] { copy });

            Assert.That(changed, Is.EqualTo(3));
            Assert.That(copy.animators[0].id, Is.Not.EqualTo(mergeId));
            Assert.That(copy.menuAssets[0].id, Is.Not.EqualTo(installId));
            Assert.That(copy.menuEdits[0].id, Is.Not.EqualTo(editId));
            Assert.That(copy.removedLayers[0].editId, Is.EqualTo(copy.menuEdits[0].id), "the layer an import left out still points at its own hiding edit");
            Assert.That(menu.animators[0].id, Is.EqualTo(mergeId));
            Assert.That(menu.menuAssets[0].id, Is.EqualTo(installId));
            Assert.That(menu.menuEdits[0].id, Is.EqualTo(editId));
            Assert.That(menu.removedLayers[0].editId, Is.EqualTo(editId));
        }

        /// <summary>The lines EnsureUniqueIds writes while the action runs.</summary>
        static List<string> LinesWrittenBy(System.Action action)
        {
            var lines = new List<string>();
            var previous = EntryEditing.Log;
            EntryEditing.Log = lines.Add;
            try { action(); }
            finally { EntryEditing.Log = previous; }
            return lines;
        }

        [Test]
        public void RepairingAnotherComponentWritesOneLineNamingIt()
        {
            var hat = EntryEditing.Add(menu, EntryKind.Toggle, "Hat", "");
            var copy = CopyComponent("Copy of the outfits", hat);

            var lines = LinesWrittenBy(() => EntryEditing.EnsureUniqueIds(menu, new[] { copy }));

            Assert.That(lines.Count, Is.EqualTo(1));
            Assert.That(lines[0], Is.EqualTo(string.Format(L.Text("ad.log.ids_repaired"), "Copy of the outfits")));
            Assert.That(lines[0], Does.Contain("Copy of the outfits"), "it names the component");
        }

        [Test]
        public void EachRepairedComponentWritesItsOwnLine()
        {
            var hat = EntryEditing.Add(menu, EntryKind.Toggle, "Hat", "");
            var first = CopyComponent("First copy", hat);
            var second = CopyComponent("Second copy", hat);

            var lines = LinesWrittenBy(() => EntryEditing.EnsureUniqueIds(menu, new[] { first, second }));

            Assert.That(lines.Count, Is.EqualTo(2));
            Assert.That(lines.Any(l => l.Contains("First copy")), Is.True);
            Assert.That(lines.Any(l => l.Contains("Second copy")), Is.True);
        }

        [Test]
        public void RepairingTheViewedComponentItselfWritesNothing()
        {
            var a = EntryEditing.Add(menu, EntryKind.Toggle, "A", "");
            var b = EntryEditing.Add(menu, EntryKind.Toggle, "B", "");
            b.id = a.id;

            var lines = LinesWrittenBy(() => Assert.That(EntryEditing.EnsureUniqueIds(menu, new AvatarDoc[0]), Is.EqualTo(1)));

            Assert.That(lines, Is.Empty, "the creator is looking at this component, so only the ones changed elsewhere are announced");
        }

        [Test]
        public void NothingToRepairWritesNothing()
        {
            EntryEditing.Add(menu, EntryKind.Toggle, "Hat", "");
            var other = CopyComponent("Other");
            EntryEditing.Add(other, EntryKind.Toggle, "Scarf", "");

            var lines = LinesWrittenBy(() => Assert.That(EntryEditing.EnsureUniqueIds(menu, new[] { other }), Is.Zero));

            Assert.That(lines, Is.Empty);
        }

        [Test]
        public void ALockedComponentThatCannotBeRepairedWritesNothing()
        {
            var hat = EntryEditing.Add(menu, EntryKind.Toggle, "Hat", "");
            var copy = CopyComponent("Copy", hat);
            copy.locked = true;

            var lines = LinesWrittenBy(() => EntryEditing.EnsureUniqueIds(menu, new[] { copy }));

            Assert.That(lines, Is.Empty, "nothing was changed in it, so there is nothing to explain");
        }

        [Test]
        public void EnsureUniqueIdsRemapsParentsAndTriggerSources()
        {
            var sub = EntryEditing.Add(menu, EntryKind.Submenu, "Sub", "");
            var toggle = EntryEditing.Add(menu, EntryKind.Toggle, "A", sub.id);
            var trigger = EntryEditing.Add(menu, EntryKind.Trigger, "T", "");
            trigger.sourceToggleId = toggle.id;
            var copyObject = new GameObject("Copy");
            copyObject.transform.SetParent(root.transform);
            var copy = copyObject.AddComponent<AvatarDoc>();
            foreach (var entry in menu.entries) copy.entries.Add(JsonUtility.FromJson<MenuEntry>(JsonUtility.ToJson(entry)));

            int changed = EntryEditing.EnsureUniqueIds(copy, new[] { menu });

            Assert.That(changed, Is.EqualTo(3));
            Assert.That(copy.entries.Select(e => e.id).Intersect(menu.entries.Select(e => e.id)), Is.Empty);
            var copiedSub = copy.entries[0];
            var copiedToggle = copy.entries[1];
            var copiedTrigger = copy.entries[2];
            Assert.That(copiedToggle.parentId, Is.EqualTo(copiedSub.id));
            Assert.That(copiedTrigger.sourceToggleId, Is.EqualTo(copiedToggle.id));
            Assert.That(EntryEditing.Siblings(copy, copiedSub.id).Select(e => e.name), Is.EqualTo(new[] { "A" }));
        }

        [Test]
        public void EnsureUniqueIdsRemapsFlipTargets()
        {
            var toggle = EntryEditing.Add(menu, EntryKind.Toggle, "Blush", "");
            var trigger = EntryEditing.Add(menu, EntryKind.Trigger, "Boop", "");
            trigger.source = TriggerSource.Contact;
            trigger.touchMode = TouchMode.FlipToggle;
            trigger.flipToggleId = toggle.id;
            var copyObject = new GameObject("Copy");
            copyObject.transform.SetParent(root.transform);
            var copy = copyObject.AddComponent<AvatarDoc>();
            foreach (var entry in menu.entries) copy.entries.Add(JsonUtility.FromJson<MenuEntry>(JsonUtility.ToJson(entry)));

            EntryEditing.EnsureUniqueIds(copy, new[] { menu });

            Assert.That(copy.entries[1].flipToggleId, Is.EqualTo(copy.entries[0].id), "the copy flips its own toggle");
            Assert.That(trigger.flipToggleId, Is.EqualTo(toggle.id), "the original is left alone");
        }

        [Test]
        public void EditsOnAPrefabInstanceAreRecordedAsOverrides()
        {
            var folder = TempAssetFolder.Create("AvatarDocPrefabTests");
            var path = folder + "/Menu.prefab";
            var source = new GameObject("PrefabMenu");
            source.AddComponent<AvatarDoc>().AddEntry(EntryKind.Toggle, "Base");
            GameObject instance = null;
            try
            {
                var prefab = PrefabUtility.SaveAsPrefabAsset(source, path);
                Object.DestroyImmediate(source);
                source = null;
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

                EntryEditing.Add(instance.GetComponent<AvatarDoc>(), EntryKind.Toggle, "Added", "");

                var modifications = PrefabUtility.GetPropertyModifications(instance);
                Assert.That(modifications, Is.Not.Null);
                Assert.That(modifications.Any(m => m.propertyPath != null && m.propertyPath.StartsWith("entries")), Is.True,
                    "the added entry is written as an override on the instance");
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                if (source != null) Object.DestroyImmediate(source);
                AssetDatabase.DeleteAsset(path);
                TempAssetFolder.Delete(folder);
            }
        }

        [Test]
        public void OperationsAreUndoable()
        {
            Undo.IncrementCurrentGroup();
            var a = EntryEditing.Add(menu, EntryKind.Toggle, "A", "");
            Undo.IncrementCurrentGroup();
            EntryEditing.Delete(menu, a);
            Assert.That(menu.entries, Is.Empty);

            Undo.PerformUndo();

            Assert.That(menu.entries.Select(e => e.name), Is.EqualTo(new[] { "A" }));
        }
    }
}
