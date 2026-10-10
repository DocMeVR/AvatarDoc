#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Drops that go through the tree's own reorder event, the way a real drag does, on a menu that also holds rows
    /// AvatarDoc does not own: the avatar's own menu items and the pages VRChat's eight item limit splits a long
    /// menu into. The tests that call ApplyStructure directly never pass through that event, which is how a drop
    /// that shifted one of those rows could be thrown away without a word.
    /// </summary>
    public class DragAcrossMenusTests
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
            foreach (var doc in avatar.Root.GetComponentsInChildren<AvatarDoc>(true)) Undo.ClearUndo(doc);
            avatar.Dispose();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        static AvatarDoctorView View(AvatarDoc menu) => new AvatarDoctorView(new SerializedObject(menu), menu);

        /// <summary>
        /// Moves one tree item the way Unity's own drag controller does, through the tree's view controller, so the
        /// tree raises its own reorder event. Falls back to raising the handler directly only if this Unity version's
        /// controller does not raise it on its own.
        /// </summary>
        static void DragRow(AvatarDoctorView view, TreeRow row, TreeRow newParent, int childIndex)
        {
            var tree = view.Tree.Tree;
            int raised = 0;
            System.Action<int, int> count = (_, __) => raised++;
            tree.itemIndexChanged += count;
            try
            {
                var controller = tree.viewController;
                var move = controller.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "Move" && m.GetParameters().Length == 4);
                Assert.That(move, Is.Not.Null, "the tree view controller has no Move(id, newParentId, childIndex, rebuildTree) in this Unity version");
                int parentId = newParent != null ? newParent.Id : -1;
                move.Invoke(controller, new object[] { row.Id, parentId, childIndex, true });
                if (raised == 0) view.Tree.HandleReorder(row.Id, parentId);
            }
            finally
            {
                tree.itemIndexChanged -= count;
            }
        }

        static TreeRow Named(AvatarDoctorView view, RowKind kind, string name)
            => view.Tree.RowsOfKind(kind).First(r => RichName.Strip(r.Name) == name);

        static List<string> RefusalTexts(AvatarDoctorView view)
            => view.Tree.Q<VisualElement>("ad-drop-refusal").Query<Label>().ToList().Select(l => l.text).ToList();

        /// <summary>A component whose root entries sit under the avatar's own Old submenu (installPath) or under a submenu of its own (As submenu), with the three toggles A, B, C and one submenu of its own.</summary>
        AvatarDoc Installed(bool asSubmenu, out MenuEntry a, out MenuEntry b, out MenuEntry c, out MenuEntry effects)
        {
            var menu = avatar.AddMenu();
            if (asSubmenu) menu.installMode = InstallMode.AsSubmenu;
            else menu.installPath = "Old";
            a = menu.AddEntry(EntryKind.Toggle, "A");
            b = menu.AddEntry(EntryKind.Toggle, "B");
            c = menu.AddEntry(EntryKind.Toggle, "C");
            effects = menu.AddEntry(EntryKind.Submenu, "Effects");
            foreach (var toggle in new[] { a, b, c }) toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            return menu;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DraggingIntoASubmenuLandsWhenTheComponentIsInstalledBelowAnotherRow(bool asSubmenu)
        {
            var menu = Installed(asSubmenu, out var a, out _, out _, out var effects);
            var view = View(menu);
            var holder = view.Tree.RowFor(a.id).ParentRow;
            Assert.That(holder, Is.Not.Null, "the component's toggles do not sit at the top of the tree");
            Assert.That(holder.Mine, Is.False, "the row above them is the avatar's Old submenu or the one AvatarDoc made, not an entry of its own");

            DragRow(view, view.Tree.RowFor(a.id), view.Tree.RowFor(effects.id), 0);

            Assert.That(a.parentId, Is.EqualTo(effects.id), "the toggle goes into Effects");
            Assert.That(RefusalTexts(view), Is.Empty, "and the tree refused nothing");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReorderingLandsWhenTheComponentIsInstalledBelowAnotherRow(bool asSubmenu)
        {
            var menu = Installed(asSubmenu, out var a, out var b, out var c, out _);
            var view = View(menu);
            var holder = view.Tree.RowFor(a.id).ParentRow;

            DragRow(view, view.Tree.RowFor(c.id), holder, 0);

            Assert.That(menu.entries.Select(e => e.name), Is.EqualTo(new[] { "C", "A", "B", "Effects" }), "C moved in front of its neighbours");
            Assert.That(menu.entries.Select(e => e.parentId ?? ""), Is.All.Empty, "and everything is still at the component's root");
            Assert.That(RefusalTexts(view), Is.Empty);
        }

        [Test]
        public void DraggingOutOfASubmenuLandsWhenTheComponentIsInstalledBelowAnotherRow()
        {
            var menu = Installed(false, out var a, out _, out _, out var effects);
            var inside = menu.AddEntry(EntryKind.Toggle, "Inside", effects.id);
            inside.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var holder = view.Tree.RowFor(a.id).ParentRow;

            DragRow(view, view.Tree.RowFor(inside.id), holder, 0);

            Assert.That(inside.parentId ?? "", Is.EqualTo(""), "dropped on the row that holds the component, the toggle leaves Effects for the component's root");
            Assert.That(RefusalTexts(view), Is.Empty);
        }

        [TestCase(false, "Old")]
        [TestCase(true, "TestAvatar")]
        public void TheLabelNamesTheRowThatHoldsTheComponent(bool asSubmenu, string rowName)
        {
            var menu = Installed(asSubmenu, out var a, out _, out _, out _);
            var view = View(menu);
            view.Tree.SelectEntry(a.id);
            var holder = view.Tree.RowFor(a.id).ParentRow;
            string expected = string.Format(L.Text("ad.ui.drag_target.into"), rowName);

            view.Tree.DragHoverRow = holder;
            view.Tree.DropDepth = holder.Depth + 1;
            Assert.That(view.Tree.Q<Label>("ad-drag-target").text, Is.EqualTo(expected), "dropping on the row lands inside it, and the label says so");

            view.Tree.DragHoverRow = view.Tree.RowFor(a.id);
            view.Tree.DropDepth = 0;
            Assert.That(view.Tree.Q<Label>("ad-drag-target").text, Is.EqualTo(expected),
                "the far left of the pointer is the component's root, which is that row, not the avatar's root menu");
        }

        [Test]
        public void TheLabelStillSaysRootMenuForAComponentAtTheTopOfTheMenu()
        {
            var menu = avatar.AddMenu();
            var a = menu.AddEntry(EntryKind.Toggle, "A");
            a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.Tree.SelectEntry(a.id);

            view.Tree.DragHoverRow = view.Tree.RowFor(a.id);
            view.Tree.DropDepth = 0;

            Assert.That(view.Tree.Q<Label>("ad-drag-target").text, Is.EqualTo(L.Text("ad.ui.drag_target.root")));
        }

        [Test]
        public void ARowOfAnotherPlaceStillRefusesTheDrop()
        {
            var menu = Installed(false, out var a, out _, out _, out _);
            var view = View(menu);
            var rootToggle = Named(view, RowKind.Existing, "RootToggle");

            view.Tree.SelectEntry(a.id);
            view.Tree.DragHoverRow = rootToggle;
            view.Tree.DropDepth = rootToggle.Depth + 1;

            Assert.That(view.Tree.Q<Label>("ad-drag-target").text, Is.EqualTo(string.Format(L.Text("ad.info.drop_refused"), L.Text("ad.info.drop_refused.not_a_container"))),
                "an existing row that does not hold the component is still no place to drop");
        }

        [Test]
        public void OnlyTheMovedRowCanRefuseADrop()
        {
            var menu = avatar.AddMenu();
            var a = menu.AddEntry(EntryKind.Toggle, "A"); a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var b = menu.AddEntry(EntryKind.Toggle, "B"); b.objects.Add(new ObjectTarget { target = avatar.Hat });
            var sub = menu.AddEntry(EntryKind.Submenu, "Sub");
            var view = View(menu);
            var rowA = view.Tree.RowFor(a.id);
            var rowB = view.Tree.RowFor(b.id);
            var rowSub = view.Tree.RowFor(sub.id);
            var oldRow = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Node != null && r.Name == "Old");
            // B stays under the row it was built under, which this drop cannot resolve: it keeps its parent and does not stop A.
            rowB.ParentRow = oldRow;

            bool applied = view.Tree.ApplyStructure(new[] { (rowA, rowSub, 0), (rowB, oldRow, 0) });

            Assert.That(applied, Is.True, "a row that did not move cannot refuse the drop");
            Assert.That(a.parentId, Is.EqualTo(sub.id));
            Assert.That(b.parentId ?? "", Is.EqualTo(""), "and it keeps the parent it had");
            Assert.That(RefusalTexts(view), Is.Empty);
        }

        [Test]
        public void DraggingOutOfASubmenuPastTheAvatarsOwnRowsLands()
        {
            var menu = avatar.AddMenu();
            var effects = menu.AddEntry(EntryKind.Submenu, "Effects");
            var headpat = menu.AddEntry(EntryKind.Toggle, "Headpat", effects.id);
            headpat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            Assert.That(view.Tree.RowsOfKind(RowKind.Existing).Any(), Is.True, "the test avatar's own menu shows beside AvatarDoc's rows");

            DragRow(view, view.Tree.RowFor(headpat.id), null, 0);

            Assert.That(headpat.parentId ?? "", Is.EqualTo(""), "dropped at the top of the root, in front of the avatar's own rows, the toggle leaves Effects");
        }

        [Test]
        public void DraggingIntoASubmenuPastTheAvatarsOwnRowsLands()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var effects = menu.AddEntry(EntryKind.Submenu, "Effects");
            var view = View(menu);

            DragRow(view, view.Tree.RowFor(hat.id), view.Tree.RowFor(effects.id), 0);

            Assert.That(hat.parentId, Is.EqualTo(effects.id));
        }

        [Test]
        public void DropsWorkWhenTheMenuSpillsOntoAMorePage()
        {
            var menu = avatar.AddMenu();
            var effects = menu.AddEntry(EntryKind.Submenu, "Effects");
            var headpat = menu.AddEntry(EntryKind.Toggle, "Headpat", effects.id);
            headpat.objects.Add(new ObjectTarget { target = avatar.Hat });
            for (int i = 0; i < 10; i++) menu.AddEntry(EntryKind.Toggle, "T" + i).objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            Assert.That(view.Tree.RowsOfKind(RowKind.More).Any(), Is.True, "thirteen root items spill onto a More page");

            var paged = menu.entries.Last(e => e.kind == EntryKind.Toggle && e.parentId != effects.id);
            Assert.That(view.Tree.RowFor(paged.id).ParentRow?.Kind, Is.EqualTo(RowKind.More), "the last toggle sits on the More page");
            DragRow(view, view.Tree.RowFor(paged.id), view.Tree.RowFor(effects.id), 0);
            Assert.That(paged.parentId, Is.EqualTo(effects.id), "a toggle on a More page moves into a submenu");

            view = View(menu);
            DragRow(view, view.Tree.RowFor(headpat.id), null, 0);
            Assert.That(headpat.parentId ?? "", Is.EqualTo(""), "a toggle leaves a submenu while other own rows sit on a More page");
        }

        [Test]
        public void DroppingOntoAMorePageLandsInItsMenu()
        {
            var menu = avatar.AddMenu();
            var effects = menu.AddEntry(EntryKind.Submenu, "Effects");
            var headpat = menu.AddEntry(EntryKind.Toggle, "Headpat", effects.id);
            headpat.objects.Add(new ObjectTarget { target = avatar.Hat });
            for (int i = 0; i < 10; i++) menu.AddEntry(EntryKind.Toggle, "T" + i).objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var more = view.Tree.RowsOfKind(RowKind.More).First();

            DragRow(view, view.Tree.RowFor(headpat.id), more, 0);

            Assert.That(headpat.parentId ?? "", Is.EqualTo(""), "a More page is part of its menu, so the toggle joins the root");
            var rootOwn = menu.entries.Where(e => string.IsNullOrEmpty(e.parentId)).ToList();
            Assert.That(rootOwn.IndexOf(headpat), Is.GreaterThan(rootOwn.IndexOf(effects)), "dropped at the top of the More page, it lands after everything on the first page");
        }

        [Test]
        public void DraggingTheAvatarsOwnRowPastItsNeighboursMovesIt()
        {
            var menu = avatar.AddMenu();
            var effects = menu.AddEntry(EntryKind.Submenu, "Effects");
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var existing = Named(view, RowKind.Existing, "RootToggle");

            DragRow(view, existing, view.Tree.RowFor(effects.id), 0);

            Assert.That(menu.menuEdits.Any(e => e.source != null && e.source.LastOrDefault() == "RootToggle"), Is.True, "moving the avatar's own toggle into Effects records a menu edit");
        }
    }
}
#endif
