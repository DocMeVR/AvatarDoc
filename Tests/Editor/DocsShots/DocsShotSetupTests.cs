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
                        Assert.That(view.Q<TextField>("ad-group").value, Is.EqualTo("Outfits"));
                        Assert.That(view.Q<Toggle>("ad-default-on").value, Is.False);
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

        [Test]
        public void TheOverrideShotListsTheTwoClipsThatMoveThePinnedShape()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Guides2.OverridesPinned(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var rule = menu.entries.Single();
                    Assert.That((rule.kind, rule.blendShapes.Single().shape, rule.blendShapes.Single().weight), Is.EqualTo((EntryKind.Override, "Tongue_Out", 0f)));
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(Selected(view), Is.SameAs(rule));
                        var usage = view.Q("ad-override-usage");
                        Assert.That(usage.Query<Label>(className: "ad-usage-clip").ToList().Select(l => l.text), Is.EqualTo(new[] { "FT Tongue Out", "Gesture Silly Face" }));
                        Assert.That(usage.Q<Label>(className: "ad-section").text, Is.EqualTo(string.Format(L.Text("ad.ui.override.usage"), 2)));
                        Assert.That(view.Q<Slider>("weight").value, Is.EqualTo(0f), "Pinned value");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void ThePresetShotSetsAToggleOffAGroupMemberOnAndARadialHalfway()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Guides2.PresetsSets(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var look = menu.entries.Single(e => e.kind == EntryKind.Preset);
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(Selected(view), Is.SameAs(look));
                        Assert.That(view.Query<Button>("ad-preset-control").ToList().Select(b => b.text), Is.EqualTo(new[] { "Hat", "Outfits/Hoodie", "Smile" }), "each row names its control by its menu path");
                        Assert.That(view.Query<SegmentedChoice>("ad-preset-on").ToList().Select(c => c.Index), Is.EqualTo(new[] { 1, 0 }), "Hat off, Hoodie on");
                        Assert.That(view.Q<Slider>("ad-preset-value").value, Is.EqualTo(0.5f));
                        Assert.That(view.Q("ad-preset-param-name"), Is.Null, "Other parameters is empty");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheShapesShotHoldsShrinkFeetWhileTheShoesAreOn()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Guides2.ShrinkUnderClothesShapes(avatar);
                try
                {
                    var shapes = avatar.Shoes.GetComponent<AvatarDocShapes>();
                    var row = shapes.whileOn.Single();
                    Assert.That((row.mesh, row.shape, row.value), Is.EqualTo((avatar.Body, "Shrink_Feet", 100f)));
                    Assert.That(shapes.follow, Is.Empty);
                    Assert.That(ShapesModel.PlanFor(shapes).Messages.Select(m => m.Key), Is.Empty, "the row is whole");
                    Assert.That(Problems(MenuOf(avatar)), Is.Empty, "the Shoes toggle that switches the piece is healthy");
                    Rehearse(spec, view =>
                    {
                        Assert.That(view.Q<Button>("ad-shapes-while-on-shape-0").text, Is.EqualTo("Shrink_Feet"));
                        Assert.That(view.Q<Slider>("ad-shapes-while-on-value-0").value, Is.EqualTo(100f));
                        Assert.That(view.Q("ad-shapes-messages").childCount, Is.EqualTo(0), "no message under the rows");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheAnimatorShotShowsBothLayersMergedAndTwoParametersToGiveAControl()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Guides2.OwnAnimatorOrMenuAnimator(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var merge = menu.animators.Single();
                    Assert.That((merge.controller.name, merge.matchWriteDefaults, merge.skipLayers.Count), Is.EqualTo(("Sparkles FX", true, 0)), "a new merge's defaults");
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(view.Q<AvatarDoctorView>().Tree.Selected?.Merge, Is.SameAs(merge));
                        Assert.That(view.Q("ad-animator-layers").Query<Toggle>().ToList().Select(t => (t.label, t.value)), Is.EqualTo(new[] { ("Sparkles", true), ("Sparkles Colour", true) }));
                        Assert.That(view.Query<Button>("ad-animator-add-control").ToList().Count, Is.EqualTo(2), "nothing declares either parameter yet");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheExistingMenuShotShowsSitRenamedAndMovedIntoEmotes()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Guides2.ChangeTheExistingMenuItem(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        var row = view.Q<AvatarDoctorView>().Tree.Selected;
                        Assert.That(row?.Kind, Is.EqualTo(RowKind.Existing));
                        Assert.That(row.ParentRow?.Name, Is.EqualTo("Emotes"), "the move landed in Emotes");
                        Assert.That(view.Q<TextField>("ad-name").value, Is.EqualTo("Sit down"));
                        Assert.That(view.Q<TextField>("ad-existing-move").value, Is.EqualTo("Emotes"));
                        Assert.That(view.Q<Button>("ad-existing-reset").enabledSelf, Is.True, "Reset works once the item is edited");
                        Assert.That(view.Q<Toggle>("ad-existing-remove").value, Is.False);
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheConvertShotListsTheOtherToolsTogglesAndTheLayersItCannotConvert()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Guides2.ConvertWindow(avatar);
                try
                {
                    Assert.That(spec.Width, Is.EqualTo(ImportWindow.OpeningWidth), "the picture shows the window at the width it opens at");
                    var candidates = ImportScanner.Scan(avatar.Root.transform, AvatarPlanCache.GetFor(MenuOf(avatar)).Tree);
#if DOCME_MA
                    int supported = 3;
                    Assert.That(candidates.Where(c => c.Source == ImportSource.ModularAvatar).Select(c => c.Supported), Is.EqualTo(new[] { true }), "the Modular Avatar hoodie toggle");
#else
                    int supported = 2;
#endif
                    Assert.That(candidates.Where(c => c.Source == ImportSource.VRCFury).Select(c => c.Supported), Is.EqualTo(new[] { true, true }), "the two VRCFury toggles");
                    Assert.That(candidates.Where(c => c.Source == ImportSource.FxLayer).Select(c => (c.Draft.PlainName, c.Supported)), Is.EquivalentTo(new[] { ("Sit", false), ("Wave", false) }),
                        "the avatar's own Sit and Wave are listed with the reason they cannot be converted");
                    Assert.That(candidates.Count(c => c.Supported), Is.EqualTo(supported));
                    Rehearse(spec, view =>
                    {
                        Assert.That(view.Q<Button>("ad-import-run").text, Is.EqualTo(string.Format(L.Text("ad.ui.import.button"), supported)), "every row that can be converted starts ticked");
                        Assert.That(view.Q<Toggle>("ad-import-remove").value, Is.True, "Remove originals starts on");
                        var list = view.Q<ScrollView>(className: "ad-import-list");
                        Assert.That(list.contentViewport.layout.height, Is.GreaterThanOrEqualTo(list.contentContainer.layout.height - 0.5f), "the list shows every row without scrolling");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheOtherToolsShotSelectsTheVrcFuryGlassesRow()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Guides2.OtherToolRows(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        var tree = view.Q<AvatarDoctorView>().Tree;
                        var foreign = tree.RowsOfKind(RowKind.Foreign).Select(r => r.Name).ToList();
                        Assert.That(foreign, Is.SupersetOf(new[] { "Glasses", "Blush" }));
#if DOCME_MA
                        Assert.That(foreign, Does.Contain("Hoodie"));
                        Assert.That(view.Q<Label>(DocsShots_Guides2.ModularAvatarChipName).text, Is.EqualTo(L.Text("ad.ui.chip.modular_avatar")));
#endif
                        Assert.That(tree.Selected?.Kind, Is.EqualTo(RowKind.Foreign));
                        Assert.That(tree.Selected.Name, Is.EqualTo("Glasses"));
                        Assert.That(view.Q<Label>(DocsShots_Guides2.VrcFuryChipName).text, Is.EqualTo(L.Text("ad.ui.chip.vrcfury")));
                        Assert.That(view.Q("ad-existing-remove"), Is.Null, "a row another tool adds offers no Remove from avatar");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void ThePlatformShotShowsAPcOnlyRadialWhileBuildingForPc()
        {
            var before = Platform.OverrideMobile;
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Guides2.PcAndQuestPlatform(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    Assert.That(Platform.IsMobileTarget, Is.False, "the shot pins the build target to PC");
                    var glow = menu.entries.Single(e => e.name == "Glow");
                    Assert.That(glow.platform, Is.EqualTo(EntryPlatform.PCOnly));
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        var doctor = view.Q<AvatarDoctorView>();
                        Assert.That(Selected(view), Is.SameAs(glow));
                        Assert.That(doctor.Tree.Selected.Excluded, Is.False, "built on PC, so not left out");
                        Assert.That(view.Q<Label>(DocsShots_Guides2.PlatformChipName).text, Is.EqualTo(L.Text("ad.ui.chip.pc_only")));
                        Assert.That(doctor.Detail.Q<AdvancedFold>().IsOpen, Is.True);
                        Assert.That(view.Q<DropdownField>("ad-entry-platform").value, Is.EqualTo(L.Text("ad.ui.platform.pc")));
                    });
                }
                finally
                {
                    Release(spec);
                }
                Assert.That(Platform.OverrideMobile, Is.EqualTo(before), "the build target is given back");
            }
        }

        [Test]
        public void TheMemoryShotShowsTheAvatarsTwoBitsAndAvatarDocsSeventeen()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Guides2.ParameterMemoryBits(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var plan = AvatarPlanCache.GetFor(menu);
                    Assert.That((plan.AvatarBits, plan.AvatarDoctorBits), Is.EqualTo((2, 17)), "Sit and Wave; the Hat toggle 1, the Smile radial 8, the Outfits group 8");
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(Selected(view)?.name, Is.EqualTo("Smile"));
                        Assert.That(view.Q<AvatarDoctorView>().Detail.Q<AdvancedFold>().IsOpen, Is.True);
                        Assert.That(view.Q<Label>("ad-synced-cost").text, Is.EqualTo(L.Text("ad.ui.synced.cost.bits")), "+8 bits beside Synced");
                        Assert.That(view.Q<Label>("ad-builds-line").text, Does.Contain(UiText.Count(8, "ad.ui.builds.bits.one", "ad.ui.builds.bits")));
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheWheelShotIsInTestModeWithTheHatOnAndTheHeadpatOnTheStrip()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Guides2.TestAndPreviewWheel(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var hat = menu.entries.Single(e => e.name == "Hat");
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        var doctor = view.Q<AvatarDoctorView>();
                        Assert.That(doctor.Tree.Selected, Is.Null, "nothing selected, so the wheel shows the top of the menu");
                        Assert.That(doctor.Wheel.ClassListContains("ad-hidden"), Is.False, "the wheel shows");
                        Assert.That(doctor.TestMode, Is.True);
                        Assert.That(view.Q<SegmentedChoice>("ad-wheel-mode").Index, Is.EqualTo(1), "Test is picked");
                        Assert.That(PreviewState.TryGet(hat.id, out var value) && value >= 0.5f, Is.True, "the Hat is previewed on");
                        Assert.That(view.Query<Button>(className: "ad-wheel-trigger").ToList().Select(b => b.text), Is.EqualTo(new[] { "Headpat" }));
                    });
                    spec.Release();
                    Assert.That(PreviewState.TryGet(hat.id, out _), Is.False, "Release clears the Hat's preview");
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheRemovalShotFreesTheDanceParameterAndBothItsLayers()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Guides2.RemoveThingsResult(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var dance = AvatarPlanCache.GetFor(menu).Tree.Removal.Parameters.Single();
                    Assert.That((dance.Name, dance.Freed, dance.Bits), Is.EqualTo(("Dance", true, 8)));
                    Assert.That(dance.Layers, Is.EquivalentTo(new[] { "Dance", "Dance Music" }));
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        var row = view.Q<AvatarDoctorView>().Tree.Selected;
                        Assert.That(row?.Kind, Is.EqualTo(RowKind.Existing));
                        Assert.That((row.Name, row.Removed), Is.EqualTo(("Dance", true)));
                        Assert.That(view.Q<Toggle>("ad-existing-remove").value, Is.True);
                        Assert.That(view.Q<Toggle>("ad-existing-in-menu").enabledSelf, Is.False, "a removed item is out of the menu by that alone");
                        Assert.That(view.Q<Label>("ad-existing-remove-note").text, Is.EqualTo("Frees 8 bits and 2 layers"), "the line the page quotes");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceHeaderShotOpensTheProblemListAndComponentSettings()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference1.InspectorHeader(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    Assert.That(Problems(menu), Is.EqualTo(new[] { "ad.warn.no_effects" }), "one warning, so the status line counts it and offers Show");
                    Rehearse(spec, view =>
                    {
                        var doctor = view.Q<AvatarDoctorView>();
                        Assert.That(doctor.Header.ProblemsOpen, Is.True, "the problem list is open");
                        Assert.That(doctor.Header.SettingsFoldOpen, Is.True, "Component settings is open");
                        Assert.That(doctor.Content.resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "the picture shows the header alone");
                        Assert.That(view.Q<Label>("ad-ready-label").text, Is.EqualTo(UiText.Count(1, "ad.ui.ready.warnings.one", "ad.ui.ready.warnings")));
                        Assert.That(view.Q("ad-problems-list").childCount, Is.EqualTo(1), "one line for the one warning");
                        Assert.That(view.Q<TextField>("ad-install-path").value, Is.Empty, "Install into at its default, the avatar's root menu");
                        Assert.That(view.Q<DropdownField>("ad-install-mode").value, Is.EqualTo(L.Text("ad.ui.install.mode.append")));
                        Assert.That(view.Q<TextField>("ad-parameter-prefix").value, Is.EqualTo("AD"));
                        Assert.That(view.Q<SegmentedChoice>("ad-platform").Index, Is.EqualTo((int)PlatformChoice.Both));
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceToggleShotShowsANewToggleWithTheHat()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference1.ToggleOverview(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var hat = menu.entries.Single();
                    Assert.That((hat.kind, hat.objects.Single().target, hat.objects.Single().activeWhenOn), Is.EqualTo((EntryKind.Toggle, avatar.Hat, true)));
                    Assert.That((hat.defaultOn, hat.hiddenFromMenu, hat.saved, hat.synced, hat.exclusiveGroup, hat.blendSeconds), Is.EqualTo((false, false, true, true, "", 0f)),
                        "every other setting at a new toggle's default, as the page lists them");
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(Selected(view), Is.SameAs(hat));
                        Assert.That(view.Q<Toggle>("ad-default-on").value, Is.False);
                        Assert.That(view.Q<TextField>("ad-group").value, Is.Empty);
                        Assert.That(view.Q<Label>("ad-builds-line").text, Is.EqualTo(string.Format(L.Text("ad.ui.builds.line"), L.JoinList(new[]
                        {
                            UiText.Count(1, "ad.ui.builds.controls.one", "ad.ui.builds.controls"),
                            UiText.Count(1, "ad.ui.builds.bits.one", "ad.ui.builds.bits"),
                            UiText.Count(1, "ad.ui.builds.layers.one", "ad.ui.builds.layers"),
                        }))), "one menu control, one synced bit and one FX layer");
                        Assert.That(view.Q<AdvancedFold>().IsOpen, Is.False, "the Advanced fold has its own picture");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceToggleAdvancedShotOpensTheFoldAtItsDefaults()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference1.ToggleAdvanced(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var hat = menu.entries.Single();
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(Selected(view), Is.SameAs(hat));
                        Assert.That(view.Q<AdvancedFold>().IsOpen, Is.True);
                        Assert.That(view.Q<DropdownField>("ad-entry-platform").value, Is.EqualTo(L.Text("ad.ui.platform.inherit")));
                        Assert.That(view.Q<Toggle>(DocsShots_Reference1.SavedName).value && view.Q<Toggle>(DocsShots_Reference1.SyncedName).value, Is.True, "Saved and Synced start on");
                        Assert.That(view.Q<Label>("ad-synced-cost").text, Is.EqualTo(L.Text("ad.ui.synced.cost.bit")));
                        Assert.That(view.Q<FloatField>("ad-blend").value, Is.EqualTo(0f));
                        Assert.That(view.Q<Label>("ad-parameter-name").text, Is.EqualTo("AD/Hat"), "the automatic name: the prefix, a slash and the name");
                        Assert.That(view.Q<Toggle>("ad-parameter-custom").value, Is.False);
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceRadialShotStartsTheSmileAQuarterOfTheWay()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference1.RadialOverview(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var smile = menu.entries.Single();
                    Assert.That((smile.kind, smile.defaultValue), Is.EqualTo((EntryKind.Radial, 0.25f)));
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(Selected(view), Is.SameAs(smile));
                        Assert.That(view.Q<Slider>("ad-default-value").value, Is.EqualTo(0.25f));
                        Assert.That((view.Q<FloatField>("min").value, view.Q<FloatField>("max").value), Is.EqualTo((0f, 100f)));
                        Assert.That(view.Q<Slider>("ad-preview-slider").value, Is.EqualTo(0.25f), "with no preview set, the slider sits at Default");
                        Assert.That(view.Q<Label>("ad-builds-line").text, Does.Contain(UiText.Count(8, "ad.ui.builds.bits.one", "ad.ui.builds.bits")), "a radial costs 8 bits");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceSubmenuShotShowsOutfitsOpenWithThreeOfEightPlacesUsed()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference1.SubmenuOverview(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var outfits = menu.entries.Single(e => e.kind == EntryKind.Submenu);
                    Assert.That(menu.entries.Count(e => e.parentId == outfits.id), Is.EqualTo(3));
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        var tree = view.Q<AvatarDoctorView>().Tree;
                        Assert.That(Selected(view), Is.SameAs(outfits));
                        Assert.That(tree.Tree.IsExpanded(tree.RowFor(outfits.id).Id), Is.True, "the submenu is open in the tree");
                        Assert.That(view.Q<Label>(DocsShots_Reference1.SlotsNoteName).text, Is.EqualTo(string.Format(L.Text("ad.ui.slots"), 3, 8)));
                        Assert.That(view.Q<Label>("ad-builds-line").text, Is.EqualTo(string.Format(L.Text("ad.ui.builds.line"), UiText.Count(1, "ad.ui.builds.controls.one", "ad.ui.builds.controls"))),
                            "a submenu builds one menu control and nothing else");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceGroupShotShowsTheHoodieWithTheGroupNotesInItsAdvancedFold()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference1.ExclusiveGroupsOverview(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    Assert.That(menu.entries.Select(e => e.exclusiveGroup), Is.EqualTo(new[] { "Outfits", "Outfits", "Outfits" }));
                    Assert.That(menu.entries.Select(e => e.defaultOn), Is.EqualTo(new[] { true, false, false }));
                    Assert.That(menu.entries.Last().objects, Is.Empty, "No outfit is the group's empty choice");
                    Assert.That(Problems(menu), Is.Empty, "an empty toggle in a group is a choice, not a warning");
                    var parameter = AvatarPlanCache.GetFor(menu).Plan.Parameters.Single(p => p.GroupName == "Outfits");
                    Assert.That((parameter.Name, parameter.Type, parameter.Bits, parameter.Saved, parameter.Synced), Is.EqualTo(("AD/Outfits", PlannedType.Int, 8, true, true)),
                        "one saved and synced Int of 8 bits, named from the prefix and the group");
                    Rehearse(spec, view =>
                    {
                        Assert.That(Selected(view)?.name, Is.EqualTo("Hoodie"));
                        Assert.That(view.Q<TextField>("ad-group").value, Is.EqualTo("Outfits"));
                        Assert.That(view.Q<Label>(DocsShots_Reference1.GroupChipName).text, Is.EqualTo("Outfits 1"), "the Jacket is the group's first choice");
                        Assert.That(view.Q<AdvancedFold>().IsOpen, Is.True);
                        Assert.That(view.Q<Label>(className: "ad-flags-note").text, Is.EqualTo(L.Text("ad.ui.group.settings")));
                        Assert.That(view.Q<Label>("ad-blend-note").text, Is.EqualTo(L.Text("ad.ui.blend.group_note")));
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceObjectsShotShowsTheJacketShownAndTheHoodieHidden()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference1.ObjectsOverview(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    Assert.That(menu.entries.Single().objects.Select(o => (o.target, o.activeWhenOn)), Is.EqualTo(new[] { (avatar.Jacket, true), (avatar.Hoodie, false) }));
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(view.Query<ObjectField>("target").ToList().Select(f => f.value), Is.EqualTo(new UnityEngine.Object[] { avatar.Jacket, avatar.Hoodie }));
                        Assert.That(view.Query<SegmentedChoice>("mode").ToList().Select(c => c.Index), Is.EqualTo(new[] { 0, 1 }), "Shown, then Hidden");
                        Assert.That(view.Q<Label>(DocsShots_Reference1.DropAreaName).text, Is.EqualTo(L.Text("ad.ui.drop.objects")), "the marked drop area is the section's own");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceBlendShapeShotShowsAFixedRowWithAnOffValueAPinRowAndARule()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference1.BlendShapesOverview(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var smile = menu.entries.Single();
                    Assert.That(smile.blendShapes.Select(t => (t.shape, t.mode, t.explicitOff)), Is.EqualTo(new[] { ("Smile", ShapeEffectMode.Fixed, true), ("Smile_Eyes", ShapeEffectMode.Pin, false) }));
                    var rule = smile.shapeSets.Single();
                    Assert.That((rule.mesh, rule.On, rule.value), Is.EqualTo((avatar.Face, true, ShapeSetValue.Default)), "Hold the rest adds a rule at Default");
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(view.Query<SegmentedChoice>("effect-mode").ToList().Select(c => c.Index), Is.EqualTo(new[] { 0, 1 }), "Fixed, then Pin");
                        Assert.That(view.Query<Toggle>("explicit-off").ToList().First().value, Is.True);
                        Assert.That(view.Query<Slider>("off-value").ToList().First().value, Is.EqualTo(10f));
                        Assert.That(view.Q("hold-blink"), Is.Not.Null, "the face is the mesh the avatar blinks with");
                        int count = ShapeSetModel.CoveredShapeCount(smile, rule, ShapeSetModel.ContextFor(avatar.Root.transform));
                        Assert.That(view.Q<Label>("ad-shape-set-note").text, Is.EqualTo(UiText.Count(count, "ad.info.shape_set_count.default.one", "ad.info.shape_set_count.default")));
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferencePropertyShotShowsAGlowColourWithAnOffValueAndASmoothnessSlider()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference1.MaterialPropertiesOverview(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var rows = menu.entries.Single().properties;
                    var glasses = avatar.Glasses.GetComponent<MeshRenderer>();
                    Assert.That(PropertyPicker.Choices(glasses).Single(c => c.PropertyName == "_Glossiness").IsRange, Is.True, "Smoothness is a range, so its row shows a slider");
                    Assert.That(rows.Select(r => (r.property, r.type, r.explicitOff)), Is.EqualTo(new[] { ("_EmissionColor", PropertyValueType.Color, true), ("_Glossiness", PropertyValueType.Float, false) }));
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(view.Query<ColorField>("value").ToList().Single().value, Is.EqualTo(rows[0].color));
                        Assert.That(view.Query<Toggle>("explicit-off").ToList().First().value, Is.True);
                        Assert.That(view.Q<ColorField>("off-value").value, Is.EqualTo(Color.black));
                        Assert.That(view.Query<Slider>("value").ToList().Single().value, Is.EqualTo(0.9f));
                        Assert.That(view.Q("type"), Is.Null, "both properties are ones the shader lists, so no row asks for a type");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceSlotShotSwapsTheJacketAndTheShoesToMetal()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference1.MaterialSlotsOverview(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    Assert.That(menu.entries.Single().materials.Select(m => (m.slot, m.material)), Is.EqualTo(new[] { (0, avatar.Metal), (0, avatar.Metal) }));
                    Assert.That(Problems(menu), Is.Empty, "slot 0 exists on both");
                    Rehearse(spec, view =>
                    {
                        Assert.That(view.Query<ObjectField>("renderer").ToList().Select(f => f.value),
                            Is.EqualTo(new UnityEngine.Object[] { avatar.Jacket.GetComponent<MeshRenderer>(), avatar.Shoes.GetComponent<MeshRenderer>() }));
                        Assert.That(view.Query<ObjectField>("material").ToList().Select(f => f.value), Is.EqualTo(new UnityEngine.Object[] { avatar.Metal, avatar.Metal }));
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceTriggerShotShowsTwoCardsAndAGateInsteadOfWhen()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference1.TriggersOverview(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var trigger = menu.entries.Single(e => e.kind == EntryKind.Trigger);
                    Assert.That(trigger.when.Count, Is.EqualTo(2));
                    Assert.That(menu.FindEntry(trigger.gateToggleId)?.name, Is.EqualTo("Hair hiding"));
                    Assert.That(Problems(menu), Is.Empty, "a menu switch that a trigger uses as its gate needs no targets");
                    Rehearse(spec, view =>
                    {
                        Assert.That(Selected(view), Is.SameAs(trigger));
                        Assert.That(view.Query("ad-when-card").ToList().Count, Is.EqualTo(2));
                        Assert.That(view.Q<Label>("ad-when-sentence").text, Does.Contain("Hat").And.Contain("Hoodie"), "the sentence names both menu switches");
                        Assert.That(view.Q("ad-trigger-source"), Is.Null, "a trigger with cards has no When list: each row picks its own kind");
                        Assert.That(view.Q<DropdownField>("ad-trigger-gate").value, Is.EqualTo("Hair hiding"), "Only while shows in the panel once a toggle is picked");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceTouchShotShowsAHoldContactThatFiresCloserThanHalfTheRadius()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference1.TouchTriggersOverview(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var boop = menu.entries.Single();
                    Assert.That((boop.source, boop.touchMode, boop.contactAnchor), Is.EqualTo((TriggerSource.Contact, TouchMode.Hold, avatar.Test.HeadBone)));
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(Selected(view), Is.SameAs(boop));
                        Assert.That(view.Q<SegmentedChoice>("ad-touch-mode").Index, Is.EqualTo(0), "Hold");
                        Assert.That(view.Q<SegmentedChoice>("ad-touch-distance").Index, Is.EqualTo(1), "Closer than");
                        Assert.That(view.Q<Slider>("ad-touch-proximity").value, Is.EqualTo(0.5f));
                        Assert.That(view.Q<Toggle>("ad-touch-tag-Finger").value && view.Q<Toggle>("ad-touch-tag-Hand").value, Is.True, "a new trigger's tags");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceBlendShapeTriggerShotFollowsTheSmileWithAFollowSourceRow()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference1.BlendShapeTriggersOverview(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var blush = menu.entries.Single();
                    Assert.That((blush.linkStyle, blush.EffectiveSmoothStart, blush.EffectiveSmoothFull, blush.sourceThreshold), Is.EqualTo((BlendShapeLinkStyle.Smooth, 20f, 80f, 50f)));
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(Selected(view), Is.SameAs(blush));
                        Assert.That(view.Q<DropdownField>("ad-trigger-source").value, Is.EqualTo(L.Text("ad.ui.trigger.source.blendshape")));
                        Assert.That(view.Q<Slider>("ad-trigger-threshold").label, Is.EqualTo(L.Text("ad.ui.trigger.swap_at")), "Smooth names the switch point Swaps at");
                        Assert.That(view.Q<DropdownField>("effect-mode").value, Is.EqualTo(L.Text("ad.ui.effect.follow")));
                        Assert.That(view.Q<FloatField>("multiplier").value, Is.EqualTo(0.6f));
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceOverrideShotHoldsTongueOutAtZeroAndTheBuildsLineSaysNothingYet()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference2.OverridesOverview(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var rule = menu.entries.Single();
                    Assert.That((rule.kind, rule.name, rule.blendShapes.Single().shape, rule.blendShapes.Single().weight), Is.EqualTo((EntryKind.Override, "No tongue", "Tongue_Out", 0f)));
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(Selected(view), Is.SameAs(rule));
                        Assert.That(view.Q<Slider>("weight").label, Is.EqualTo(L.Text("ad.ui.override.value")));
                        Assert.That(view.Q("ad-override-usage").Query<Label>(className: "ad-usage-clip").ToList().Select(l => l.text), Is.EqualTo(new[] { "FT Tongue Out", "Gesture Silly Face" }));
                        Assert.That(view.Q("ad-preview-choice-disabled").enabledSelf, Is.False, "an override's preview row is greyed out");
                        Assert.That(view.Q<Label>("ad-builds-line").text, Is.EqualTo(L.Text("ad.ui.builds.nothing.empty")),
                            "the page tells the reader that an override's Builds line always says nothing yet; when the tool counts overrides, change the page");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceAnimatorShotNamesTheToggleThatDeclaresOneParameterAndOffersAControlForTheOther()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference2.MergedAnimatorsOverview(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var merge = menu.animators.Single();
                    Assert.That((merge.controller.name, merge.matchWriteDefaults, merge.platform, merge.skipLayers.Count), Is.EqualTo(("Sparkles FX", true, EntryPlatform.Inherit, 0)));
                    Assert.That(menu.entries.Single().parameterName, Is.EqualTo("Sparkles"), "the Glow toggle gives the controller's Sparkles a menu control of its own");
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(view.Q<AvatarDoctorView>().Tree.Selected?.Merge, Is.SameAs(merge));
                        Assert.That(view.Q<Toggle>("ad-animator-match-write-defaults").value, Is.True);
                        Assert.That(view.Q("ad-animator-layers").Query<Toggle>().ToList().Select(t => (t.label, t.value)), Is.EqualTo(new[] { ("Sparkles", true), ("Sparkles Colour", true) }));
                        Assert.That(view.Query<Label>("ad-animator-param-declared").ToList().Select(l => l.text), Is.EqualTo(new[] { "Glow", L.Text("ad.ui.animator.not_synced") }));
                        Assert.That(view.Query<Button>("ad-animator-add-control").ToList().Count, Is.EqualTo(1), "only the parameter nothing declares offers a control");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceMenuAssetShotInstallsAsASubmenuAndOffersOneDeclaration()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference2.MenuAssetOverview(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var install = menu.menuAssets.Single();
                    Assert.That((install.menu.name, install.asSubmenu, install.installPath, install.platform), Is.EqualTo(("Sparkles menu", true, "", EntryPlatform.Inherit)));
                    Assert.That(menu.parameters.Select(p => p.name), Is.EqualTo(new[] { "Sparkles Hue" }));
                    Assert.That(Problems(menu), Is.EqualTo(new[] { "ad.warn.parameter_undeclared" }), "the control that drives Sparkles has no declaration yet");
                    Rehearse(spec, view =>
                    {
                        Assert.That(view.Q<AvatarDoctorView>().Tree.Selected?.Asset, Is.SameAs(install));
                        Assert.That(view.Query<Label>("ad-menu-asset-param-declared").ToList().Select(l => l.text),
                            Is.EqualTo(new[] { L.Text("ad.ui.menu_asset.undeclared"), L.Text("ad.ui.menu_asset.declared") }));
                        Assert.That(view.Q<Button>("ad-menu-asset-declare-Sparkles").text, Is.EqualTo(L.Text("ad.ui.menu_asset.declare")));
                        Assert.That(view.Q<SegmentedChoice>("ad-menu-asset-place").Index, Is.EqualTo(0), "As submenu is the default");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceParametersShotShowsABoolAndAFloatWithTheirBitsAndTheTotal()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference2.ParametersOverview(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    Assert.That(menu.parameters.Select(p => (p.name, p.type, p.Bits, p.saved, p.synced)),
                        Is.EqualTo(new[] { ("Sparkles", PlannedType.Bool, 1, true, true), ("Sparkles Hue", PlannedType.Float, 8, true, true) }));
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(view.Q<AvatarDoctorView>().Tree.Selected?.Key, Is.EqualTo(TreeRow.ParametersRowKey));
                        Assert.That(view.Q<Label>("ad-parameters-total").text, Is.EqualTo(string.Format(L.Text("ad.ui.parameters.total"), 9)));
                        Assert.That(view.Q<TextField>("ad-parameter-name-1").value, Is.EqualTo("Sparkles Hue"));
                        Assert.That(view.Q<Button>("ad-parameters-add").text, Is.EqualTo(L.Text("ad.ui.parameters.add")));
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferencePresetShotHasThreeSetsAnEmptyRowAndOneOtherParameter()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference2.PresetsOverview(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var look = menu.entries.Single(e => e.kind == EntryKind.Preset);
                    Assert.That(look.presetStates.Count, Is.EqualTo(4));
                    Assert.That(look.presetParameters.Select(p => (p.name, p.value)), Is.EqualTo(new[] { ("Sparkles", 1f) }));
                    Assert.That(Problems(menu), Is.Empty, "the empty row is a note, and Sparkles is declared");
                    Rehearse(spec, view =>
                    {
                        Assert.That(Selected(view), Is.SameAs(look));
                        Assert.That(view.Query<Button>("ad-preset-control").ToList().Select(b => b.text),
                            Is.EqualTo(new[] { "Hat", "Outfits/Hoodie", "Smile", L.Text("ad.ui.preset.pick") }), "the last row has no control yet");
                        Assert.That(view.Q<Slider>("ad-preset-value").value, Is.EqualTo(0.5f));
                        Assert.That(view.Query<TextField>("ad-preset-param-name").ToList().Select(f => f.value), Is.EqualTo(new[] { "Sparkles" }));
                        Assert.That(view.Q<SegmentedChoice>("ad-preview-choice").Index, Is.EqualTo(0));
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceOutfitWindowShotIsOneAtATimeWithANoneOptionAndTheHatHidden()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference2.OutfitSetsOverview(avatar);
                try
                {
                    Assert.That(spec.Width, Is.EqualTo(OutfitSetWindow.MinWidth), "the picture shows the window at the width it opens at");
                    Assert.That(MenuOf(avatar).entries, Is.Empty, "the window only plans: nothing is built until Create outfit set");
                    Rehearse(spec, view =>
                    {
                        Assert.That(view.Q<TextField>("ad-outfit-name").value, Is.EqualTo("Outfits"));
                        Assert.That(view.Q<SegmentedChoice>("ad-outfit-mode").Index, Is.EqualTo(0), "One at a time");
                        Assert.That(view.Q<Toggle>("ad-outfit-none").value, Is.True);
                        Assert.That(view.Q<Toggle>("ad-outfit-capture").value, Is.True, "Capture icons starts on");
                        Assert.That(view.Q("ad-outfit-list").Query(className: "ad-panel").ToList().Count, Is.EqualTo(3), "Jacket, Hoodie and Shoes");
                        Assert.That(view.Q("ad-outfit-hidden").Query<Label>(className: "ad-piece-chip-label").ToList().Select(l => l.text), Is.EqualTo(new[] { "Hat" }));
                        Assert.That(view.Q<Button>("ad-outfit-create").text, Is.EqualTo(L.Text("ad.ui.outfit.create")));
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceShapesShotFollowsChestBigAndHoldsShrinkFeetOnTheSweater()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference2.AvatarDocShapesOverview(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var shapes = avatar.Root.GetComponentsInChildren<AvatarDocShapes>(true).Single();
                    Assert.That(shapes.gameObject.name, Is.EqualTo("Sweater"));
                    Assert.That(shapes.GetComponent<SkinnedMeshRenderer>(), Is.Not.Null, "Follows rows need the component on the piece's own mesh");
                    Assert.That(shapes.follow.Select(r => (r.sourceMesh, r.sourceShape, r.localShape)), Is.EqualTo(new[] { (avatar.Body, "Chest_Big", "") }));
                    Assert.That(shapes.whileOn.Select(r => (r.mesh, r.shape, r.value)), Is.EqualTo(new[] { (avatar.Body, "Shrink_Feet", 100f) }));
                    Assert.That(shapes.platform, Is.EqualTo(EntryPlatform.Inherit));
                    Assert.That(ShapesModel.PlanFor(shapes).Messages, Is.Empty, "every row resolves");
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(view.Q<Button>("ad-shapes-follow-shape-0").text, Is.EqualTo("Chest_Big"));
                        Assert.That(view.Q<Button>("ad-shapes-follow-local-0").text, Is.EqualTo(L.Text("ad.ui.shapes.same_name")));
                        Assert.That(view.Q<Button>("ad-shapes-while-on-shape-0").text, Is.EqualTo("Shrink_Feet"));
                        Assert.That(view.Q<Slider>("ad-shapes-while-on-value-0").value, Is.EqualTo(100f));
                        Assert.That(view.Q("ad-shapes-messages").childCount, Is.EqualTo(0));
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceConvertShotListsTheOtherToolsRowsWithAllAndNone()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference2.ConvertWindowOverview(avatar);
                try
                {
                    Assert.That(spec.Width, Is.EqualTo(ImportWindow.OpeningWidth));
                    var candidates = ImportScanner.Scan(avatar.Root.transform, AvatarPlanCache.GetFor(MenuOf(avatar)).Tree);
                    Assert.That(candidates.Where(c => c.Source == ImportSource.VRCFury).Select(c => c.Supported), Is.EqualTo(new[] { true, true }));
                    Assert.That(candidates.Where(c => c.Source == ImportSource.FxLayer).Select(c => c.Supported), Is.EqualTo(new[] { false, false }));
                    Rehearse(spec, view =>
                    {
                        Assert.That(view.Q<Button>("ad-import-none").text, Is.EqualTo(L.Text("ad.ui.import.select_none")));
                        Assert.That(view.Q<Toggle>("ad-import-remove").value, Is.True, "Remove originals starts on");
                        Assert.That(view.Query(className: "ad-unsupported").ToList().Count, Is.EqualTo(2), "Sit and Wave say why they stay");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceImportRowsShotShowsTheChipsOfTheOtherToolsRows()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference2.ImportRows(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        var tree = view.Q<AvatarDoctorView>().Tree;
                        Assert.That(tree.RowsOfKind(RowKind.Foreign).Select(r => r.Name), Is.SupersetOf(new[] { "Glasses", "Blush" }));
                        Assert.That(tree.Selected?.Name, Is.EqualTo("Glasses"));
                        Assert.That(view.Q<Label>(DocsShots_Guides2.VrcFuryChipName).text, Is.EqualTo(L.Text("ad.ui.chip.vrcfury")));
                        Assert.That(view.Q<Label>(DocsShots_Reference2.ImportLateNoteName).text,
                            Is.EqualTo(string.Format(L.Text("ad.ui.existing.late_note"), L.Text("ad.ui.chip.vrcfury"))));
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceIconCaptureShotShowsTheHatAgainstATransparentBackground()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference2.IconCaptureOverview(avatar);
                try
                {
                    Assert.That(spec.Width, Is.EqualTo(IconCaptureWindow.MinWidth));
                    Assert.That(MenuOf(avatar).entries.Single().name, Is.EqualTo("Hat"));
                    Rehearse(spec, view =>
                    {
                        Assert.That(view.Q<Image>("ad-capture-preview").image, Is.Not.Null, "the preview drew the hat");
                        Assert.That(view.Q<SegmentedChoice>("ad-capture-background").Index, Is.EqualTo(0), "Transparent is the default");
                        Assert.That(view.Q<Button>("ad-capture-save").enabledSelf, Is.True);
                        Assert.That(view.Q<Button>("ad-capture-fit").enabledSelf, Is.True);
                        Assert.That(view.Q<Toggle>("ad-capture-fill").value, Is.True, "Fill is on by default");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceWindowShotShowsTheTreeTheWheelAndThePanelSideBySide()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference2.WindowOverview(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    Assert.That(spec.Width, Is.EqualTo(DocsShots_Reference2.WindowWidth));
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        var doctor = (AvatarDoctorView)view;
                        Assert.That(doctor.ClassListContains("ad-wide"), Is.True);
                        Assert.That(doctor.Tree.Selected?.Name, Is.EqualTo("Jacket"));
                        Assert.That(doctor.Wheel.ClassListContains("ad-hidden"), Is.False, "the window shows the wheel by default");
                        Assert.That(doctor.Wheel.Model.Wedges.Select(w => w.Name), Is.EqualTo(new[] { "Jacket", "Hoodie", "Shoes" }));
                        Assert.That(doctor.Tree.worldBound.xMax, Is.LessThan(doctor.Detail.worldBound.xMin + 0.5f), "tree and panel sit side by side");
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceWheelShotIsInTestModeInsideTheOutfitsSubmenuWithAHiddenToggleOnTheStrip()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference2.WheelTestMode(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var hoodie = menu.entries.Single(e => e.name == "Hoodie");
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        var doctor = view.Q<AvatarDoctorView>();
                        Assert.That(doctor.TestMode, Is.True);
                        Assert.That(view.Q<SegmentedChoice>("ad-wheel-mode").Index, Is.EqualTo(1), "Test is picked");
                        Assert.That(doctor.Wheel.Model.Wedges.Select(w => w.Name), Is.EqualTo(new[] { "Jacket", "Hoodie", "Shoes" }), "the hidden toggle takes no wedge");
                        Assert.That(doctor.Wheel.Model.HiddenControls.Select(h => h.name), Is.EqualTo(new[] { "Glasses" }));
                        Assert.That(doctor.Wheel.Model.Wedges.Single(w => w.EntryId == hoodie.id).On, Is.True, "the Hoodie is previewed on");
                        Assert.That(doctor.Wheel.Model.Crumbs.Select(c => c.name), Is.EqualTo(new[] { L.Text("ad.ui.root_menu") }));
                        Assert.That(view.Q("ad-wheel-not-in-menu-label").parent.ClassListContains("ad-hidden"), Is.False, "the strip of hidden toggles shows");
                    });
                    spec.Release();
                    Assert.That(PreviewState.Overrides.Value, Is.Empty, "Release clears the previews the shot set");
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceScenePreviewShotHasTheHatOnAndTheResetButtonInTheHeader()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference2.ScenePreviewOverview(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var hat = menu.entries.Single();
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        Assert.That(Selected(view), Is.SameAs(hat));
                        Assert.That(view.Q<SegmentedChoice>("ad-preview-choice").Index, Is.EqualTo(1), "On is picked");
                        Assert.That(view.Q<Button>("ad-preview-reset").ClassListContains("ad-hidden"), Is.False, "Reset preview shows while a preview is set");
                    });
                    spec.Release();
                    Assert.That(PreviewState.TryGet(hat.id, out _), Is.False, "Release clears the Hat's preview");
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceSummaryShotOpensThePlainSummaryWithTheLockSwitchUnderIt()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference2.SummaryOverview(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    Assert.That(menu.locked, Is.False);
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        var doctor = view.Q<AvatarDoctorView>();
                        Assert.That(doctor.SummaryOpen, Is.True);
                        Assert.That(doctor.Summary.Format, Is.EqualTo(SummaryFormat.Plain));
                        Assert.That(doctor.SummaryText, Does.Contain("Hat").And.Contain("Smile").And.Contain(L.Text("ad.summary.footer")));
                        Assert.That(view.Q<Toggle>("ad-lock-toggle").value, Is.False);
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceLockedShotShowsTheBannerTheNoteAndTheDisabledAddButtons()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference2.LockedOverview(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    Assert.That(menu.locked, Is.True);
                    Assert.That(menu.lockNote, Is.Not.Empty);
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        var doctor = view.Q<AvatarDoctorView>();
                        Assert.That(doctor.Locked, Is.True);
                        Assert.That(view.Q("ad-lock-banner").ClassListContains("ad-hidden"), Is.False);
                        Assert.That(view.Q<Label>("ad-lock-banner-text").text, Is.EqualTo(L.Text("ad.ui.locked") + " " + menu.lockNote));
                        Assert.That(view.Q<Button>("ad-unlock").text, Is.EqualTo(L.Text("ad.ui.unlock")));
                        Assert.That(doctor.Header.SettingsFoldOpen, Is.True, "the settings a locked component still lets a buyer change are open");
                        Assert.That(view.Q<ToolbarButton>("ad-add-toggle").enabledSelf, Is.False);
                    });
                }
                finally
                {
                    Release(spec);
                }
            }
        }

        [Test]
        public void TheReferenceLanguagesShotOpensTheLanguageListUnderThePicker()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference2.LanguagesPicker(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, (view, keptBottom) =>
                    {
                        var open = view.panel.visualTree.Query<AdMenu>().ToList().SingleOrDefault();
                        try
                        {
                            Assert.That(open, Is.Not.Null, "the language list is open");
                            Assert.That(open.IsClosed, Is.False);
                            Assert.That(open.Items.Count, Is.GreaterThanOrEqualTo(3), "English, Japanese and Simplified Chinese at least");
                            Assert.That(open.Items.Count(i => i.Checked), Is.EqualTo(1), "the current language is ticked");
                            Assert.That(open.Frame.worldBound.yMax, Is.LessThanOrEqualTo(keptBottom + 0.5f), "the open list ends inside the picture");
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

        [Test]
        public void TheReferenceLimitsShotMovesTheNinthToggleOfTheOutfitsSubmenuToAMorePage()
        {
            using (var avatar = DocsAvatar.Create())
            {
                var spec = DocsShots_Reference2.LimitsMore(avatar);
                try
                {
                    var menu = MenuOf(avatar);

                    var outfits = menu.entries.Single(e => e.kind == EntryKind.Submenu);
                    Assert.That(menu.entries.Count(e => e.parentId == outfits.id), Is.EqualTo(9));
                    Assert.That(Problems(menu), Is.Empty);
                    Rehearse(spec, view =>
                    {
                        var tree = view.Q<AvatarDoctorView>().Tree;
                        Assert.That(tree.Selected?.Name, Is.EqualTo("Outfits"));
                        Assert.That(view.Q(DocsShots_Reference2.MoreRowName).Q<Label>(className: "ad-name").text, Is.EqualTo(string.Format(L.Text("ad.ui.more_page"), 2)));
                        Assert.That(view.Q<Label>(DocsShots_Reference2.LimitsSlotsName).text, Does.EndWith(" of " + VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.MAX_CONTROLS + " used"));
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
                Assert.That(spec.Markers.Length, Is.LessThanOrEqualTo(8), spec.Name + ": a picture numbers at most 8 markers");
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
