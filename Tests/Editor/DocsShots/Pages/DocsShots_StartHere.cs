#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The pictures of the Start here pages: Your first toggle, Your first outfit selector, Your first headpat and
    /// Simple or Advanced. Each setup builds the same state the page's clicks leave, so the picture shows where its
    /// steps lead. DocsShotSetupTests runs every setup without the screen read.
    /// </summary>
    [Explicit, Category("DocsShots")]
    public class DocsShots_StartHere
    {
        const string Page = "start-here";

        [Test] public void FirstToggleWelcome() { using (var avatar = DocsAvatar.Create()) FirstToggleWelcome(avatar).Take(); }
        [Test] public void FirstToggleDone() { using (var avatar = DocsAvatar.Create()) FirstToggleDone(avatar).Take(); }
        [Test] public void FirstOutfitWindow() { using (var avatar = DocsAvatar.Create()) FirstOutfitWindow(avatar).Take(); }
        [Test] public void FirstOutfitDone() { using (var avatar = DocsAvatar.Create()) FirstOutfitDone(avatar).Take(); }
        [Test] public void FirstHeadpatContact() { using (var avatar = DocsAvatar.Create()) FirstHeadpatContact(avatar).Take(); }
        [Test] public void SimpleMode() { using (var avatar = DocsAvatar.Create()) SimpleMode(avatar).Take(); }
        [Test] public void AdvancedMode() { using (var avatar = DocsAvatar.Create()) AdvancedMode(avatar).Take(); }

        /// <summary>A new, empty component: the welcome card with 1 Make a toggle, 2 Start an outfit set and 3 Quick start. Nothing is selected.</summary>
        internal static DocsShotSpec FirstToggleWelcome(DocsAvatar avatar) => new DocsShotSpec
        {
            Page = Page,
            Name = "first-toggle-welcome",
            View = DocsShot.Inspector(avatar.AddMenu()),
            Arrange = root => root.Q<AvatarDoctorView>().Tree.ClearSelection(),
            Markers = new[] { ("ad-welcome-toggle", 1), ("ad-welcome-outfit", 2), ("ad-welcome-quickstart", 3) },
        };

        /// <summary>The toggle Make a toggle makes from the picked hat, selected, with 1 its Objects list.</summary>
        internal static DocsShotSpec FirstToggleDone(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var inspector = DocsShot.Inspector(menu);
            try
            {
                // What the welcome card's picker does with the object picked in it.
                inspector.Q<AvatarDoctorView>().Tree.AddTogglesFor(new[] { avatar.Hat });
                var hat = menu.entries.Single();
                return new DocsShotSpec
                {
                    Page = Page,
                    Name = "first-toggle-done",
                    View = inspector,
                    Arrange = root => root.Q<AvatarDoctorView>().Tree.SelectEntry(hat.id),
                    Markers = new[] { ("ad-objects", 1) },
                };
            }
            catch
            {
                DocsShot.ReleaseInspectors(inspector);
                throw;
            }
        }

        /// <summary>
        /// The Outfit set window at the width it opens at, after Jacket, Hoodie and Shoes were dropped on its empty
        /// list and split into one outfit each: 1 the set name, 2 the outfit cards, 3 Create outfit set.
        /// </summary>
        internal static DocsShotSpec FirstOutfitWindow(DocsAvatar avatar)
        {
            var window = OutfitWindow(avatar.AddMenu(), avatar);
            return new DocsShotSpec
            {
                Page = Page,
                Name = "first-outfit-window",
                View = window.rootVisualElement,
                Width = OutfitSetWindow.MinWidth,
                Markers = new[] { ("ad-outfit-name", 1), ("ad-outfit-list", 2), ("ad-outfit-create", 3) },
                Release = () => Object.DestroyImmediate(window),
            };
        }

        /// <summary>
        /// The tree after Create outfit set, with the new Outfits submenu selected and its arrow opened. Capture
        /// icons is turned off here, because it saves icon files into the project; the detail panel, which would
        /// show the submenu's captured icon, is left out of the picture for that reason, so the shot ends under
        /// the tree.
        /// </summary>
        internal static DocsShotSpec FirstOutfitDone(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var window = OutfitWindow(menu, avatar);
            try
            {
                window.SetCaptureIcons(false);
                if (window.Create().Count == 0)
                    throw new InvalidOperationException("The outfit set window built nothing for the docs avatar: " + window.LastStatus);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
            var submenu = menu.entries.First(e => e.kind == EntryKind.Submenu);
            return new DocsShotSpec
            {
                Page = Page,
                Name = "first-outfit-done",
                View = DocsShot.Inspector(menu),
                Arrange = root =>
                {
                    var view = root.Q<AvatarDoctorView>();
                    view.Tree.SelectEntry(submenu.id);
                    view.Tree.SetExpanded(view.Tree.RowFor(submenu.id).Key, true);
                    view.Detail.style.display = DisplayStyle.None;
                },
            };
        }

        /// <summary>
        /// The trigger the Headpat start makes, after Blush was picked in its blend shape row: 1 the anchor on the
        /// Head bone, 2 the tags that touch it, 3 the blend shape it fades in. Only the trigger's panel is in the
        /// picture: the mode row, the header and the tree are left out, so the whole panel fits the window.
        /// </summary>
        internal static DocsShotSpec FirstHeadpatContact(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var created = ContactStarts.Apply(menu, ContactStarts.StartKind.Headpat, out var errorKey);
            if (errorKey != null || created.Count != 1)
                throw new InvalidOperationException("The Headpat start made nothing on the docs avatar: " + (errorKey != null ? L.Text(errorKey) : "no trigger came back."));
            var headpat = created[0];
            // What picking Blush in the row's shape list writes; the row already points at the face mesh.
            headpat.blendShapes[0].shape = "Blush";
            return new DocsShotSpec
            {
                Page = Page,
                Name = "first-headpat-contact",
                View = DocsShot.Inspector(menu),
                Arrange = root =>
                {
                    var view = root.Q<AvatarDoctorView>();
                    view.Tree.RevealEntry(headpat.id);
                    view.Q("ad-mode-row").style.display = DisplayStyle.None;
                    view.Header.style.display = DisplayStyle.None;
                    view.Left.style.display = DisplayStyle.None;
                },
                Markers = new[] { ("ad-touch-anchor", 1), ("ad-touch-tag-flags", 2), ("ad-blendshapes", 3) },
            };
        }

        /// <summary>The menu of the mode pictures in Simple, with the Hat toggle selected: 1 the mode switch, 2 the header, 3 the toolbar, 4 the selected toggle's panel.</summary>
        internal static DocsShotSpec SimpleMode(DocsAvatar avatar) => ModeShot(avatar, ViewMode.Simple, "simple-mode");

        /// <summary>The same menu and selection as SimpleMode, in Advanced, with the same four markers.</summary>
        internal static DocsShotSpec AdvancedMode(DocsAvatar avatar) => ModeShot(avatar, ViewMode.Advanced, "advanced-mode");

        static DocsShotSpec ModeShot(DocsAvatar avatar, ViewMode mode, string name)
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            menu.AddEntry(EntryKind.Toggle, "Smile").blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Smile", weight = 100f });
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            menu.AddEntry(EntryKind.Toggle, "Jacket", outfits.id).objects.Add(new ObjectTarget { target = avatar.Jacket });
            menu.AddEntry(EntryKind.Toggle, "Hoodie", outfits.id).objects.Add(new ObjectTarget { target = avatar.Hoodie });
            return new DocsShotSpec
            {
                Page = Page,
                Name = name,
                View = DocsShot.Inspector(menu, mode),
                Arrange = root => root.Q<AvatarDoctorView>().Tree.SelectEntry(hat.id),
                Markers = new[] { ("ad-mode", 1), ("ad-header", 2), ("ad-toolbar-actions", 3), ("ad-detail", 4) },
            };
        }

        /// <summary>
        /// An Outfit set window that is never shown, named Outfits, holding Jacket, Hoodie and Shoes as one outfit
        /// each: what dropping the three objects on its empty list and pressing One outfit per object leaves.
        /// The caller destroys it.
        /// </summary>
        static OutfitSetWindow OutfitWindow(AvatarDoc menu, DocsAvatar avatar)
        {
            var window = ScriptableObject.CreateInstance<OutfitSetWindow>();
            try
            {
                window.Init(menu);
                window.SetName("Outfits");
                foreach (var piece in new[] { avatar.Jacket, avatar.Hoodie, avatar.Shoes })
                {
                    window.AddOutfit(piece.name);
                    if (!window.AddPiece(window.OutfitCount - 1, piece))
                        throw new InvalidOperationException("The outfit set window refused " + piece.name + ": " + window.LastStatus);
                }
                return window;
            }
            catch
            {
                Object.DestroyImmediate(window);
                throw;
            }
        }
    }
}
#endif
