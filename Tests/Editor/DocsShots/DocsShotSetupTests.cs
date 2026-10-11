#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Runs the setup of every docs picture the way a capture would, in a host window, without reading the
    /// screen: the avatar is healthy where the page says so, the entry the page talks about is selected, and every
    /// marker finds its element whole inside the picture.
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

        [Test]
        public void TheToggleClothingShotShowsOnePieceShownAndOneHidden()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Guides1.ToggleClothingObjects(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var jacket = menu.entries.Single();
                    Assert.That(jacket.objects.Select(o => (o.target, o.activeWhenOn)), Is.EqualTo(new[] { (avatar.Jacket, true), (avatar.Hoodie, false) }));
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(Selected(view), Is.SameAs(jacket));
                        Assert.That(view.Q<SegmentedChoice>("mode").Index, Is.EqualTo(0), "the first row, the jacket, reads Shown");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheSliderShotShowsARadialFromZeroToHundred()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Guides1.SlidersRadial(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var smile = menu.entries.Single();
                    Assert.That(smile.kind, Is.EqualTo(EntryKind.Radial));
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(Selected(view), Is.SameAs(smile));
                        Assert.That((view.Q<FloatField>("min").value, view.Q<FloatField>("max").value), Is.EqualTo((0f, 100f)), "a new row's Min and Max");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheColourShotShowsTheShaderNameOnTheButtonAndThePropertyInItsTooltip()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Guides1.ColourAndGlowProperty(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var row = menu.entries.Single().properties.Single();
                    var choice = PropertyPicker.Choices(row.renderer).Single(c => c.PropertyName == "_EmissionColor");
                    Assert.That(choice.Type, Is.EqualTo(PropertyValueType.Color));
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        var pick = view.Q<Button>("property");
                        Assert.That(pick.text, Is.EqualTo(choice.DisplayName), "the button shows the shader's own name for the property");
                        Assert.That(pick.tooltip, Does.Contain("_EmissionColor"), "its tooltip names the property");
                        Assert.That(view.Q<ColorField>("value").value, Is.EqualTo(row.color));
                        Assert.That(view.Q("type"), Is.Null, "a property the shader lists needs no type choice");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheMaterialShotSwapsSlotZeroOfTheJacket()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Guides1.SwapMaterialsSlot(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    Assert.That(Problems(menu), Is.Empty, "slot 0 exists on the jacket");
                    Rehearse(spec, view =>
                    {
                        Assert.That(view.Q<IntegerField>("slot").value, Is.EqualTo(0));
                        Assert.That(view.Q<ObjectField>("material").value, Is.EqualTo(avatar.Metal));
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheGroupShotShowsTheHoodieInTheOutfitsGroup()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Guides1.OneOfManyGroup(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    Assert.That(menu.entries.Select(e => e.exclusiveGroup), Is.EqualTo(new[] { "Outfits", "Outfits" }));
                    Assert.That(menu.entries.Select(e => e.defaultOn), Is.EqualTo(new[] { true, false }), "only the jacket starts on");
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(Selected(view)?.name, Is.EqualTo("Hoodie"));
                        Assert.That(view.Q<TextField>(DocsShots_Guides1.GroupFieldName).value, Is.EqualTo("Outfits"));
                        Assert.That(view.Q<Toggle>(DocsShots_Guides1.DefaultOnName).value, Is.False);
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheMenuStyleShotShowsTheStyledSubmenuOpenInTheTree()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Guides1.OrganiseTheMenuStyle(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var outfits = menu.entries.Single(e => e.kind == EntryKind.Submenu);
                    var style = RichName.Parse(outfits.name);
                    Assert.That((style.Plain, style.Bold, style.Color.HasValue), Is.EqualTo(("Outfits", true, true)));
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        var tree = view.Q<AvatarDoctorView>().Tree;
                        Assert.That(Selected(view), Is.SameAs(outfits));
                        Assert.That(tree.Tree.IsExpanded(tree.RowFor(outfits.id).Id), Is.True, "the submenu is open, showing its toggles");
                        Assert.That(view.Q<Label>("ad-name-preview").text, Is.EqualTo(outfits.name), "the In game line draws the styled name");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheHoldTheRestShotShowsTheRuleHoldTheRestAddsWithBothSwitchesOff()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Guides1.HoldTheRestRule(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var smile = menu.entries.Single();
                    var rule = smile.shapeSets.Single();
                    Assert.That((rule.mesh, rule.On, rule.value, rule.holdBlink, rule.holdVisemes), Is.EqualTo((avatar.Face, true, ShapeSetValue.Default, false, false)));
                    int count = ShapeSetModel.CoveredShapeCount(smile, rule, ShapeSetModel.ContextFor(avatar.Root.transform));
                    Assert.That(count, Is.EqualTo(DocsAvatar.FaceShapes.Length - 5), "every face shape but Smile, the blink shape and the three visemes");
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(view.Q<Toggle>("hold-blink").value || view.Q<Toggle>("hold-visemes").value, Is.False);
                        Assert.That(view.Q<Label>("ad-shape-set-note").text,
                            Is.EqualTo(UiText.Count(count, "ad.info.shape_set_count.default.one", "ad.info.shape_set_count.default")));
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheTouchShotShowsAFadeContactOnTheHead()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Guides1.ReactToTouchFade(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var poke = menu.entries.Single();
                    Assert.That(poke.IsFollowDistanceTrigger, Is.True);
                    Assert.That(poke.contactAnchor, Is.EqualTo(avatar.Test.HeadBone));
                    Assert.That(poke.contactTags, Is.EquivalentTo(new[] { "Finger", "Hand" }), "a new trigger's tags");
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(Selected(view), Is.SameAs(poke));
                        Assert.That(view.Q<SegmentedChoice>("ad-touch-mode").Index, Is.EqualTo(2), "Fade is picked");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void ThePhysBoneShotShowsAStretchEventWithItsAmount()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Guides1.ReactToPhysbonesEvent(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(Selected(view)?.name, Is.EqualTo("Hair pull"));
                        Assert.That(view.Q<DropdownField>("ad-touch-physbone-event").value, Is.EqualTo(L.Text("ad.ui.touch.event.stretch")));
                        Assert.That(view.Q<Slider>("ad-touch-threshold").value, Is.EqualTo(0.5f));
                        Assert.That(view.Q<SegmentedChoice>("ad-touch-mode").Items.Count, Is.EqualTo(2), "a PhysBone trigger offers Hold and Flip, not Fade");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheBlendShapeShotFollowsTheSmileInSmoothStyle()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Guides1.FollowABlendShapeSmooth(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var blush = menu.entries.Single();
                    Assert.That((blush.linkStyle, blush.EffectiveSmoothStart, blush.EffectiveSmoothFull, blush.sourceThreshold, blush.linkMode),
                        Is.EqualTo((BlendShapeLinkStyle.Smooth, 0f, 100f, 50f, BlendShapeLinkMode.Add)), "a new trigger's style, range, swap point and mode");
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(Selected(view), Is.SameAs(blush));
                        Assert.That(view.Q<Button>("ad-trigger-shape").text, Does.EndWith("Smile"));
                        Assert.That(view.Q<DropdownField>("effect-mode").value, Is.EqualTo(L.Text("ad.ui.effect.fixed")));
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheParameterShotOpensTheListWithGestureTypedInsideThePicture()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Guides1.LinkParameterPicker(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    Assert.That(Problems(menu), Is.Empty, "GestureRight is one of VRChat's own parameters");
                    Rehearse(spec, (view, keptBottom) =>
                    {
                        var picker = view.panel.visualTree.Query<AdMenu>().ToList().SingleOrDefault();
                        try
                        {
                            Assert.That(picker, Is.Not.Null, "the parameter list is open");
                            Assert.That(picker.IsClosed, Is.False);
                            Assert.That(picker.SearchField.value, Is.EqualTo("Gesture"));
                            Assert.That(picker.Items.Select(i => i.Text), Is.SupersetOf(new[] { "GestureLeft", "GestureRight" }), "the search lists VRChat's gesture parameters");
                            Assert.That(picker.Frame.worldBound.yMax, Is.LessThanOrEqualTo(keptBottom + 0.5f), "the open list ends inside the picture");
                            Assert.That(picker.Frame.worldBound.xMax, Is.LessThanOrEqualTo(spec.Width + 0.5f), "the open list stays inside the picture's width");
                        }
                        finally
                        {
                            DocsShots_Guides1.CloseMenus(view);
                        }
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
        static void Rehearse(DocsShotSpec spec, Action<VisualElement> check) => Rehearse(spec, (view, keptBottom) => check(view));

        /// <summary>The same, handing the check the bottom of the kept picture in world points, for a picture that shows something outside the view, such as an open menu.</summary>
        static void Rehearse(DocsShotSpec spec, Action<VisualElement, float> check)
        {
            InspectorShots.InHostWindow(spec.View, spec.Width, spec.Height, (window, frame) =>
            {
                spec.Arrange?.Invoke(spec.View);
                LayoutProbe.SettleLists(spec.View);
                var shot = new InspectorShots.ShotFrame(window, frame, (int)window.position.width, (int)window.position.height, inFront: true);
                var area = DocsShot.CropAndMark(shot, spec.View, spec.Markers, new List<DocsShot.Marker>());
                float top = frame.worldBound.y;
                foreach (var (elementName, _) in spec.Markers)
                {
                    var bound = DocsShot.FindMarked(spec.View, elementName).worldBound;
                    Assert.That(bound.width > 0f && bound.height > 0f, Is.True, spec.Name + ": \"" + elementName + "\" is shown");
                    Assert.That(bound.yMax - top, Is.LessThanOrEqualTo(area.height + 0.5f), spec.Name + ": \"" + elementName + "\" ends inside the picture");
                }
                check(spec.View, top + area.height);
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
