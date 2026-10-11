#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    public class WheelModelTests
    {
        TestAvatar avatar;
        AvatarDoc menu;
        MenuEntry outfits, hat, glasses;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            menu = avatar.AddMenu();
            outfits = MenuEntry.Create(EntryKind.Submenu, "Outfits");
            hat = MenuEntry.Create(EntryKind.Toggle, "Hat");
            hat.parentId = outfits.id;
            glasses = MenuEntry.Create(EntryKind.Toggle, "Glasses");
            glasses.parentId = outfits.id;
            menu.entries.Add(outfits);
            menu.entries.Add(hat);
            menu.entries.Add(glasses);
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

        MenuTree Tree() => MenuModel.Build(avatar.Root.transform, null, new[] { menu });

        [Test]
        public void RootListsItemsInOrderWithKeys()
        {
            var tree = Tree();
            var model = WheelModel.For(tree.Root, null, "Avatar menu");

            Assert.That(model.Title, Is.EqualTo("Avatar menu"));
            Assert.That(model.Key, Is.Null);
            Assert.That(model.Wedges.Select(w => w.Name), Does.Contain("Outfits"));
            var wedge = model.Wedges.Single(w => w.Name == "Outfits");
            Assert.That(wedge.IsSubmenu, Is.True);
            Assert.That(wedge.Key, Is.EqualTo(outfits.id));
            Assert.That(model.Crumbs, Is.Empty);
        }

        [Test]
        public void SubmenuShowsItsItemsWithTheSelectionMarked()
        {
            var tree = Tree();
            var node = tree.Root.Items.Single(i => i.Submenu != null && i.Submenu.EntryId == outfits.id).Submenu;
            var model = WheelModel.For(node, glasses.id, "Avatar menu");

            Assert.That(model.Title, Is.EqualTo("Outfits"));
            Assert.That(model.Wedges.Select(w => w.Name), Is.EqualTo(new[] { "Hat", "Glasses" }));
            Assert.That(model.Wedges.Single(w => w.Selected).EntryId, Is.EqualTo(glasses.id));
            Assert.That(model.Crumbs.Select(c => c.name), Is.EqualTo(new[] { "Avatar menu" }));
            Assert.That(model.Crumbs[0].key, Is.Null);
        }

        [Test]
        public void WedgeAtRunsClockwiseFromTheTop()
        {
            var centre = new Vector2(100f, 100f);
            Assert.That(WheelModel.WedgeAt(new Vector2(110f, 30f), centre, 100f, 40f, 8), Is.EqualTo(0), "just right of the top");
            Assert.That(WheelModel.WedgeAt(new Vector2(170f, 90f), centre, 100f, 40f, 8), Is.EqualTo(1), "right side, above the middle");
            Assert.That(WheelModel.WedgeAt(new Vector2(170f, 110f), centre, 100f, 40f, 8), Is.EqualTo(2));
            Assert.That(WheelModel.WedgeAt(new Vector2(30f, 90f), centre, 100f, 40f, 8), Is.EqualTo(6), "left side, above the middle");
            Assert.That(WheelModel.WedgeAt(new Vector2(100f, 100f), centre, 100f, 40f, 8), Is.EqualTo(-1), "the centre is not a wedge");
            Assert.That(WheelModel.WedgeAt(new Vector2(100f, 100f + 150f), centre, 100f, 40f, 8), Is.EqualTo(-1), "outside the ring");
        }

        [Test]
        public void WedgesAreCappedAtEightSlots()
        {
            for (int i = 0; i < 10; i++) menu.entries.Add(MenuEntry.Create(EntryKind.Toggle, "T" + i));
            var model = WheelModel.For(Tree().Root, null, "Avatar menu");
            Assert.That(model.Wedges.Count, Is.EqualTo(8));
        }

        [Test]
        public void WedgesCarryTheEffectiveState()
        {
            var eyes = MenuEntry.Create(EntryKind.Radial, "Eyes");
            eyes.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big" });
            menu.entries.Add(eyes);
            PreviewState.Set(hat.id, 1f);
            PreviewState.Set(eyes.id, 0.6f);
            var plan = AvatarPlanCache.GetFor(menu);

            var model = WheelModel.For(plan.Tree.Root, null, "Avatar menu", plan);
            var outfitsNode = plan.Tree.Root.Items.Single(i => i.Submenu != null && i.Submenu.EntryId == outfits.id).Submenu;
            var subModel = WheelModel.For(outfitsNode, null, "Avatar menu", plan);

            var hatWedge = subModel.Wedges.Single(w => w.EntryId == hat.id);
            Assert.That(hatWedge.On, Is.True);
            Assert.That(hatWedge.Value, Is.EqualTo(1f));
            var glassesWedge = subModel.Wedges.Single(w => w.EntryId == glasses.id);
            Assert.That(glassesWedge.On, Is.False);
            var eyesWedge = model.Wedges.Single(w => w.EntryId == eyes.id);
            Assert.That(eyesWedge.On, Is.True);
            Assert.That(eyesWedge.Value, Is.EqualTo(0.6f).Within(0.001f));
        }

        /// <summary>
        /// The wheel's foreign wedges (another tool's items, the avatar's existing menu) must read
        /// as a neutral tone, never the accent and never the purple that used to mirror --ad-source. WheelView.Draw
        /// itself needs a live VisualElement to render, so this drives the pure fill-selection function it calls.
        /// </summary>
        [Test]
        public void ForeignWedgesAreNeutral()
        {
            var own = new Color(1f, 0f, 0f);
            var submenu = new Color(0f, 1f, 0f);
            var empty = new Color(0f, 0f, 1f);
            var accent = new Color(0.247f, 0.788f, 0.659f);
            var foreign = new Color(0.2f, 0.2f, 0.2f);
            var palette = new WheelView.WedgePalette(own, submenu, empty, accent, foreign);

            var foreignWedge = new Wedge { IsForeign = true };
            Assert.That(WheelView.FillFor(foreignWedge, palette), Is.EqualTo(foreign));
            Assert.That(WheelView.FillFor(foreignWedge, palette), Is.Not.EqualTo(accent));

            var ownWedge = new Wedge { IsForeign = false };
            Assert.That(WheelView.FillFor(ownWedge, palette), Is.EqualTo(own));

            var selectedForeign = new Wedge { IsForeign = true, Selected = true };
            Assert.That(WheelView.FillFor(selectedForeign, palette), Is.EqualTo(accent), "the browsed wedge is always accent, even a foreign one");

            Assert.That(WheelView.FillFor(null, palette), Is.EqualTo(empty));
        }
    }
}
#endif
