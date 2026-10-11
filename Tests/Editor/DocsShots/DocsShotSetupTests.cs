#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Runs the setup of every Home and Start here picture the way a capture would, in a host window, without
    /// reading the screen: the avatar is healthy where the page says so, the entry the page talks about is
    /// selected, and every marker finds its element whole inside the picture.
    /// </summary>
    public class DocsShotSetupTests
    {
        [SetUp]
        public void SetUp()
        {
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        [Test]
        public void TheHomeHeroShowsNoProblem()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Home.HeroShot(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    Assert.That(Problems(menu), Is.Empty, "the hero shows an avatar that is ready to upload");
                    var glow = menu.entries.Single(e => e.name == "Glow");
                    Assert.That(glow.properties.Count, Is.EqualTo(1), "Glow drives a material property");
                    Rehearse(spec, view => Assert.That(Selected(view)?.name, Is.EqualTo("Smile")));
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheWelcomeShotShowsTheCardWithItsThreeStarts()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_StartHere.FirstToggleWelcome(avatar);
                try
                {
                    Assert.That(MenuOf(avatar).entries, Is.Empty, "the welcome card shows for a component with no entries");
                    Rehearse(spec, view =>
                    {
                        var card = view.Q<AvatarDoctorView>().Tree.Q("ad-welcome-card");
                        Assert.That(card.ClassListContains("ad-hidden"), Is.False, "the welcome card shows");
                        Assert.That(view.Q<Button>("ad-welcome-convert").parent.ClassListContains("ad-hidden"), Is.True,
                            "the test avatar has nothing to convert, so the picture shows the three starts it marks");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheFirstToggleShotShowsTheToggleMakeAToggleMakes()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_StartHere.FirstToggleDone(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var hat = menu.entries.Single();
                    Assert.That(hat.kind, Is.EqualTo(EntryKind.Toggle));
                    Assert.That(hat.name, Is.EqualTo("Hat"), "the toggle is named after the object");
                    Assert.That(hat.defaultOn, Is.False, "a new toggle starts off");
                    Assert.That(hat.objects.Single().target, Is.EqualTo(avatar.Hat));
                    Assert.That(hat.objects.Single().activeWhenOn, Is.True, "the hat is shown while the toggle is on");
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(Selected(view), Is.SameAs(hat), "the new toggle is selected");
                        Assert.That(view.Q("ad-welcome-card").ClassListContains("ad-hidden"), Is.True, "the welcome card is gone");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheOutfitWindowShotHoldsOneOutfitPerObject()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_StartHere.FirstOutfitWindow(avatar);
                try
                {
                    Rehearse(spec, view =>
                    {
                        Assert.That(view.Q<TextField>("ad-outfit-name").value, Is.EqualTo("Outfits"));
                        Assert.That(view.Q<SegmentedChoice>("ad-outfit-mode").Index, Is.EqualTo((int)OutfitMode.OneAtATime));
                        var cards = view.Q("ad-outfit-list").Children().ToList();
                        Assert.That(cards.Select(c => c.Q<TextField>().value), Is.EqualTo(new[] { "Jacket", "Hoodie", "Shoes" }));
                        Assert.That(cards.Select(c => c.Query(className: "ad-piece-chip").ToList().Count), Is.EqualTo(new[] { 1, 1, 1 }), "one piece each");
                        Assert.That(cards[0].Q(className: "ad-outfit-default").ClassListContains("ad-chip-on"), Is.True, "the first outfit is the default");
                        Assert.That(view.Q(className: "ad-outfit-shrinks"), Is.Null, "the test avatar has no shrink shapes for these pieces");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheOutfitDoneShotShowsTheNewSubmenuOpenAndSelected()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_StartHere.FirstOutfitDone(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var submenu = menu.entries.Single(e => e.kind == EntryKind.Submenu);
                    Assert.That(submenu.name, Is.EqualTo("Outfits"));
                    var toggles = menu.entries.Where(e => e.parentId == submenu.id).ToList();
                    Assert.That(toggles.Select(t => t.name), Is.EqualTo(new[] { "Jacket", "Hoodie", "Shoes" }));
                    Assert.That(toggles.All(t => t.kind == EntryKind.Toggle && t.exclusiveGroup == "Outfits"), Is.True, "one toggle per outfit, in one exclusive group");
                    Assert.That(toggles.Select(t => t.defaultOn), Is.EqualTo(new[] { true, false, false }), "the default outfit starts on");
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        var tree = view.Q<AvatarDoctorView>().Tree;
                        Assert.That(Selected(view), Is.SameAs(submenu), "Create outfit set selects the new submenu");
                        Assert.That(tree.Tree.IsExpanded(tree.RowFor(submenu.id).Id), Is.True, "the submenu is open, as after pressing its arrow");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheHeadpatShotShowsTheTriggerTheHeadpatStartMakes()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_StartHere.FirstHeadpatContact(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var headpat = menu.entries.Single();
                    Assert.That(headpat.name, Is.EqualTo("Headpat"));
                    Assert.That(headpat.kind, Is.EqualTo(EntryKind.Trigger));
                    Assert.That(headpat.source, Is.EqualTo(TriggerSource.Contact));
                    Assert.That(headpat.IsFollowDistanceTrigger, Is.True, "the start makes a Fade trigger");
                    Assert.That(headpat.contactAnchor, Is.EqualTo(avatar.Test.HeadBone), "anchored on the Head bone");
                    Assert.That(headpat.contactTags, Is.EquivalentTo(new[] { "Hand", "Finger" }));
                    Assert.That(headpat.contactAllowSelf && headpat.contactAllowOthers, Is.True, "your own hands and other players' hands both count");
                    var row = headpat.blendShapes.Single();
                    Assert.That((row.renderer, row.shape, row.weight), Is.EqualTo((avatar.Face, "Blush", 100f)));
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        var tree = view.Q<AvatarDoctorView>().Tree;
                        Assert.That(Selected(view), Is.SameAs(headpat), "the new trigger is selected");
                        Assert.That(tree.RowFor(headpat.id).ParentRow.Kind, Is.EqualTo(RowKind.TriggersGroup), "it sits under Triggers in the tree");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [TestCase(ViewMode.Simple)]
        [TestCase(ViewMode.Advanced)]
        public void TheModeShotsShowTheSameMenuInEachMode(ViewMode mode)
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = mode == ViewMode.Simple ? DocsShots_StartHere.SimpleMode(avatar) : DocsShots_StartHere.AdvancedMode(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(view.Q<AvatarDoctorView>().Mode, Is.EqualTo(mode));
                        Assert.That(Selected(view)?.name, Is.EqualTo("Hat"));
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        static AvatarDoc MenuOf(DocsAvatar avatar) => avatar.Root.GetComponentsInChildren<AvatarDoc>(true).Single();

        static MenuEntry Selected(VisualElement view) => view.Q<AvatarDoctorView>()?.Tree.Selected?.Entry;

        static List<string> Problems(AvatarDoc menu)
        {
            AvatarPlanCache.Invalidate();
            return AvatarPlanCache.GetFor(menu).Messages.Where(m => m.Severity != MessageSeverity.Info).Select(m => m.Key).ToList();
        }

        /// <summary>The capture's own steps short of the screen read: the host window, the arrange step, the list rows, the crop and the markers. Each marker's element must be shown and end inside the kept picture.</summary>
        static void Rehearse(DocsShotSpec spec, Action<VisualElement> check)
        {
            InspectorShots.InHostWindow(spec.View, spec.Width, spec.Height, (window, frame) =>
            {
                spec.Arrange?.Invoke(spec.View);
                DocsShot.SettleLists(spec.View);
                var shot = new InspectorShots.ShotFrame(window, frame, (int)window.position.width, (int)window.position.height, inFront: true);
                var area = DocsShot.CropAndMark(shot, spec.View, spec.Markers, new List<DocsShot.Marker>());
                float top = frame.worldBound.y;
                foreach (var (elementName, _) in spec.Markers)
                {
                    var bound = DocsShot.FindMarked(spec.View, elementName).worldBound;
                    Assert.That(bound.width > 0f && bound.height > 0f, Is.True, spec.Name + ": \"" + elementName + "\" is shown");
                    Assert.That(bound.yMax - top, Is.LessThanOrEqualTo(area.height + 0.5f), spec.Name + ": \"" + elementName + "\" ends inside the picture");
                }
                check(spec.View);
            });
        }

        /// <summary>Destroys the shot's inspector editor and whatever else its setup made, whether the test passed or not.</summary>
        static void Release(DocsShotSpec spec)
        {
            DocsShot.ReleaseInspectors(spec.View);
            spec.Release?.Invoke();
        }
    }
}
#endif
