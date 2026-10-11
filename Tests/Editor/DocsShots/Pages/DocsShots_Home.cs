#if DOCME_NDMF && DOCME_VRCSDK3A
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    [Explicit, Category("DocsShots")]
    public class DocsShots_Home
    {
        [Test]
        public void Hero()
        {
            using (var avatar = DocsAvatar.Create()) HeroShot(avatar).Take();
        }

        /// <summary>A menu of toggles, a radial and an Outfits submenu, with the Smile toggle selected. Every entry has an effect, so the picture shows an avatar that is ready to upload: Glow brightens the glasses' emission colour.</summary>
        internal static DocsShotSpec HeroShot(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var smile = menu.AddEntry(EntryKind.Toggle, "Smile");
            smile.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Smile", weight = 100f });
            var glow = menu.AddEntry(EntryKind.Radial, "Glow");
            glow.properties.Add(new MaterialPropertyTarget
            {
                renderer = avatar.Glasses.GetComponent<MeshRenderer>(),
                property = "_EmissionColor",
                type = PropertyValueType.Color,
                minColor = Color.black,
                color = new Color(0.25f, 1f, 0.8f),
            });
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            menu.AddEntry(EntryKind.Toggle, "Jacket", outfits.id).objects.Add(new ObjectTarget { target = avatar.Jacket });
            menu.AddEntry(EntryKind.Toggle, "Hoodie", outfits.id).objects.Add(new ObjectTarget { target = avatar.Hoodie });
            return new DocsShotSpec
            {
                Page = "home",
                Name = "hero",
                View = DocsShot.Inspector(menu),
                Arrange = root => root.Q<AvatarDoctorView>().Tree.SelectEntry(smile.id),
            };
        }

        /// <summary>The whole Inspector with its parts numbered: 1 the header, 2 the memory bar, 3 the toolbar, 4 the search, 5 the menu tree, 6 the detail panel.</summary>
        [Test]
        public void InspectorOverview()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var menu = avatar.AddMenu();
                var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
                hat.objects.Add(new ObjectTarget { target = avatar.Hat });
                menu.AddEntry(EntryKind.Toggle, "Smile").blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Smile", weight = 100f });
                var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
                menu.AddEntry(EntryKind.Toggle, "Jacket", outfits.id).objects.Add(new ObjectTarget { target = avatar.Jacket });
                menu.AddEntry(EntryKind.Toggle, "Hoodie", outfits.id).objects.Add(new ObjectTarget { target = avatar.Hoodie });
                DocsShot.Take("reference-inspector", "overview", DocsShot.Inspector(menu), DocsShot.InspectorWidth, 900f,
                    root => root.Q<AvatarDoctorView>().Tree.SelectEntry(hat.id),
                    ("ad-header", 1), ("ad-membar", 2), ("ad-toolbar-actions", 3), ("ad-search", 4), ("ad-tree", 5), ("ad-detail", 6));
            }
        }
    }
}
#endif
