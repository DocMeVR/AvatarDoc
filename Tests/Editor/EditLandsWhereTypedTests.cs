#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// A field typed into without pressing Enter saves when the focus leaves it, and by then the creator may have
    /// clicked another row. The save has to land on the row the field was built for. Each test types into a field
    /// of one row, selects another row the way the tree does (the panel waits to rebuild while the field has the
    /// focus), then moves the focus away for real.
    /// </summary>
    public class EditLandsWhereTypedTests
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

        static AvatarDoctorView View(AvatarDoc menu) => new AvatarDoctorView(new SerializedObject(menu), menu);

        static void TypeSelectAndLeave(AvatarDoctorView view, TextField field, string text, Action selectOther)
        {
            field.Focus();
            MenuLifecycleTests.TypeWithoutSaving(field, text);
            selectOther();
            Assert.That(field.panel, Is.Not.Null, "the panel waits for the focus to leave before it rebuilds");
            view.Tree.Tree.Focus();
            var focused = view.panel.focusController.focusedElement as VisualElement;
            Assert.That(focused != field && !field.Contains(focused), Is.True, "the focus left the field");
        }

        static MenuEdit EditFor(AvatarDoc menu, string sourcePath) =>
            menu.menuEdits.FirstOrDefault(e => string.Join("/", e.source ?? new List<string>()) == sourcePath);

        static TreeRow Existing(AvatarDoctorView view, string name) => view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Name == name);

        [Test]
        public void SubmenuNameSavesOntoTheSubmenuItWasTypedFor()
        {
            var menu = avatar.AddMenu();
            var clothes = menu.AddEntry(EntryKind.Submenu, "Clothes");
            var accessories = menu.AddEntry(EntryKind.Submenu, "Accessories");
            var other = avatar.AddMenu(avatar.Hat);
            other.installPath = "Accessories";
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(clothes.id);
                var name = view.Detail.Q<TextField>("ad-name");

                TypeSelectAndLeave(view, name, "Outfits", () => view.Tree.SelectEntry(accessories.id));

                Assert.That(menu.FindEntry(clothes.id).name, Is.EqualTo("Outfits"), "the name lands on the submenu its field was built for");
                Assert.That(menu.FindEntry(accessories.id).name, Is.EqualTo("Accessories"), "the row selected since keeps its name");
                Assert.That(other.installPath, Is.EqualTo("Accessories"), "and nothing installed under that row is moved");
            });
        }

        [Test]
        public void ExistingItemNameSavesOntoTheItemItWasTypedFor()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectKey(Existing(view, "RootToggle").Key);
                var name = view.Detail.Q<TextField>("ad-name");

                TypeSelectAndLeave(view, name, "Renamed", () => view.Tree.SelectKey(Existing(view, "Old").Key));

                Assert.That(EditFor(menu, "RootToggle")?.name, Is.EqualTo("Renamed"), "the name lands on the item its field was built for");
                Assert.That(EditFor(menu, "Old")?.name ?? "", Is.Empty, "the item selected since keeps its name");
            });
        }

        [Test]
        public void ExistingItemMoveSavesOntoTheItemItWasTypedFor()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Submenu, "Outfits");
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectKey(Existing(view, "RootToggle").Key);
                var move = view.Detail.Q<TextField>("ad-existing-move");

                TypeSelectAndLeave(view, move, "Outfits", () => view.Tree.SelectKey(Existing(view, "Old").Key));

                var moved = EditFor(menu, "RootToggle");
                Assert.That(moved != null && moved.move && moved.targetPath == "Outfits", Is.True, "the move lands on the item its field was built for");
                Assert.That(EditFor(menu, "Old")?.move ?? false, Is.False, "the item selected since stays where it is");
            });
        }

        [Test]
        public void CustomContactTagSavesOntoTheTriggerItWasTypedFor()
        {
            var menu = avatar.AddMenu();
            var poke = Contact(menu, "Poke");
            var pat = Contact(menu, "Pat");
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(poke.id);
                var custom = view.Detail.Q<TextField>("ad-touch-custom-tag");

                TypeSelectAndLeave(view, custom, "Boop", () => view.Tree.SelectEntry(pat.id));

                Assert.That(menu.FindEntry(poke.id).contactTags, Does.Contain("Boop"), "the tag lands on the trigger its field was built for");
                Assert.That(menu.FindEntry(pat.id).contactTags, Does.Not.Contain("Boop"), "the trigger selected since gets no tag");
            });
        }

        [Test]
        public void RenameSubmenuRefusesAnEntryOfAnotherComponent()
        {
            var menu = avatar.AddMenu();
            var mine = menu.AddEntry(EntryKind.Submenu, "Mine");
            var other = avatar.AddMenu(avatar.Hat);
            var theirs = other.AddEntry(EntryKind.Submenu, "Theirs");
            var tree = AvatarPlanCache.GetFor(menu).Tree;

            EntryEditing.RenameSubmenu(menu, theirs, "Taken", tree);
            Assert.That(theirs.name, Is.EqualTo("Theirs"), "a component never renames an entry it does not hold");

            EntryEditing.RenameSubmenu(menu, mine, "Renamed", tree);
            Assert.That(mine.name, Is.EqualTo("Renamed"));
        }

        MenuEntry Contact(AvatarDoc menu, string name)
        {
            var entry = menu.AddEntry(EntryKind.Trigger, name);
            entry.objects.Add(new ObjectTarget { target = avatar.Glasses });
            entry.source = TriggerSource.Contact;
            entry.contactAnchor = avatar.Hat.transform;
            return entry;
        }
    }
}
#endif
