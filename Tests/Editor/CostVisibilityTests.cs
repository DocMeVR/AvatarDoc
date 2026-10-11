using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Costs shown where they are added, and the "What this builds" line.</summary>
    public class CostVisibilityTests
    {
        TestAvatar avatar;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            AvatarPlanCache.Invalidate();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            AvatarPlanCache.Invalidate();
        }

        AvatarDoctorView View(AvatarDoc menu) => new AvatarDoctorView(new SerializedObject(menu), menu);

        [Test]
        public void SyncedSwitchShowsItsBitCost()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.Tree.SelectEntry(hat.id);

            var cost = view.Detail.Q<Label>("ad-synced-cost");
            Assert.That(cost, Is.Not.Null);
            Assert.That(cost.text, Is.EqualTo(L.Text("ad.ui.synced.cost.bit")), "a new synced Bool costs one bit");

            hat.synced = false;
            AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
            view.Refresh();
            cost = view.Detail.Q<Label>("ad-synced-cost");
            Assert.That(cost.text, Is.EqualTo(L.Text("ad.ui.synced.cost.none")), "turning Synced off costs nothing");

            hat.synced = true;
            var radial = menu.AddEntry(EntryKind.Radial, "Radial");
            radial.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", minWeight = 0f, maxWeight = 100f });
            AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
            view.Refresh();
            view.Tree.SelectEntry(radial.id);
            var radialCost = view.Detail.Q<Label>("ad-synced-cost");
            Assert.That(radialCost.text, Is.EqualTo(L.Text("ad.ui.synced.cost.bits")), "a new synced Float costs eight bits");

            var existing = menu.AddEntry(EntryKind.Toggle, "Existing");
            existing.parameterName = "Existing";
            existing.objects.Add(new ObjectTarget { target = avatar.Glasses });
            AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
            view.Refresh();
            view.Tree.SelectEntry(existing.id);
            var reuseCost = view.Detail.Q<Label>("ad-synced-cost");
            Assert.That(reuseCost.text, Is.EqualTo(L.Text("ad.ui.synced.cost.none")), "a parameter the avatar already has costs nothing new");
        }

        [Test]
        public void ANearlyFullPageShowsItsSlots()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            for (int i = 0; i < 6; i++) menu.AddEntry(EntryKind.Toggle, "T" + i, outfits.id).objects.Add(new ObjectTarget { target = avatar.Hat });
            var plan = AvatarPlanCache.GetFor(menu);
            var (items, _) = TreeRow.Build(plan, menu);
            var outfitsRow = items.Select(i => i.data).First(r => r.EntryId == outfits.id);
            Assert.That(outfitsRow.Slots, Is.EqualTo(6));
            Assert.That(MenuTreeView.SlotsChipText(outfitsRow), Is.Null, "a page with room left shows no slot chip");

            menu.AddEntry(EntryKind.Toggle, "T6", outfits.id).objects.Add(new ObjectTarget { target = avatar.Glasses });
            AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
            var (items2, _) = TreeRow.Build(AvatarPlanCache.GetFor(menu), menu);
            var outfitsRow2 = items2.Select(i => i.data).First(r => r.EntryId == outfits.id);
            Assert.That(outfitsRow2.Slots, Is.EqualTo(7));
            Assert.That(MenuTreeView.SlotsChipText(outfitsRow2), Is.EqualTo(string.Format(L.Text("ad.ui.slots.chip"), 7, 8)), "seven of eight is nearly full");
        }

        [Test]
        public void OverflowPagesAreNumbered()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            for (int i = 0; i < 20; i++) menu.AddEntry(EntryKind.Toggle, "T" + i, outfits.id).objects.Add(new ObjectTarget { target = avatar.Hat });
            var plan = AvatarPlanCache.GetFor(menu);
            var (_, byId) = TreeRow.Build(plan, menu);
            var moreNames = byId.Values.Where(r => r.Kind == RowKind.More).Select(r => r.Name).OrderBy(n => n).ToList();

            Assert.That(moreNames, Does.Contain(string.Format(L.Text("ad.ui.more_page"), 2)));
            Assert.That(moreNames, Does.Contain(string.Format(L.Text("ad.ui.more_page"), 3)));
            Assert.That(moreNames.Any(n => n == "More"), Is.False, "an overflow page is never shown as a bare \"More\"");
        }

        [Test]
        public void WhatThisBuildsCountsControlsBitsLayersAndComponents()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            var plan = view.Plan;
            var row = view.Tree.RowFor(hat.id);

            var summary = WhatThisBuildsModel.Compute(plan, row);
            Assert.That(summary.MenuControls, Is.EqualTo(1));
            Assert.That(summary.Bits, Is.EqualTo(1));
            Assert.That(summary.FxLayers, Is.EqualTo(1));
            Assert.That(summary.Contacts, Is.EqualTo(0));
            Assert.That(summary.PhysBones, Is.EqualTo(0));
            Assert.That(summary.Constraints, Is.EqualTo(0));

            view.Tree.SelectEntry(hat.id);
            var line = view.Detail.Q<Label>("ad-builds-line");
            Assert.That(line, Is.Not.Null);
            Assert.That(line.text, Is.EqualTo(string.Format(L.Text("ad.ui.builds.line"),
                UiText.Count(1, "ad.ui.builds.controls.one", "ad.ui.builds.controls") + ", "
                + UiText.Count(1, "ad.ui.builds.bits.one", "ad.ui.builds.bits") + ", "
                + UiText.Count(1, "ad.ui.builds.layers.one", "ad.ui.builds.layers"))));
        }

        [Test]
        public void WhatThisBuildsSaysWhyAnEmptyEntryBuildsNothing()
        {
            var menu = avatar.AddMenu();
            var empty = menu.AddEntry(EntryKind.Toggle, "Empty");
            // No target, hidden from the menu and not synced: nothing at all should be left to count.
            empty.hiddenFromMenu = true;
            empty.synced = false;
            var view = View(menu);
            view.Tree.SelectEntry(empty.id);

            var summary = WhatThisBuildsModel.Compute(view.Plan, view.Tree.RowFor(empty.id));
            Assert.That(summary.IsEmpty, Is.True);
            var line = view.Detail.Q<Label>("ad-builds-line");
            Assert.That(line.text, Is.EqualTo(L.Text("ad.ui.builds.nothing.empty")));
        }

        [Test]
        public void WhatThisBuildsCountsTouchAndGrabComponents()
        {
            avatar.AddHumanoidRig();
            var menu = avatar.AddMenu();
            var trigger = menu.AddEntry(EntryKind.Trigger, "Touch");
            trigger.source = TriggerSource.Contact;
            trigger.contactAnchor = avatar.Hat.transform;
            trigger.objects.Add(new ObjectTarget { target = avatar.Glasses });

            var grab = menu.AddEntry(EntryKind.Grabbable, "Grab");
            grab.grabObject = avatar.Glasses;
            grab.grabHands = GrabHands.Both;

            var view = View(menu);
            var plan = view.Plan;

            var touchSummary = WhatThisBuildsModel.Compute(plan, view.Tree.RowFor(trigger.id));
            Assert.That(touchSummary.Contacts, Is.EqualTo(1));

            var grabSummary = WhatThisBuildsModel.Compute(plan, view.Tree.RowFor(grab.id));
            Assert.That(grabSummary.PhysBones, Is.EqualTo(1), "the object has no PhysBone of its own yet");
            Assert.That(grabSummary.Contacts, Is.EqualTo(2), "both hands are allowed");
            Assert.That(grabSummary.Constraints, Is.EqualTo(1));

            // A PhysBone already on the object is reused, not built again.
            avatar.Glasses.AddComponent<VRCPhysBone>();
            AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
            view.Refresh();
            var reuseSummary = WhatThisBuildsModel.Compute(view.Plan, view.Tree.RowFor(grab.id));
            Assert.That(reuseSummary.PhysBones, Is.EqualTo(0));
        }

        [Test]
        public void ASharedConstraintIsCountedOnce()
        {
            // Two entries following the same target object share one constraint at build time; only the first of
            // them in tree order should say "1 constraint", the second says 0 for that same target.
            var menu = avatar.AddMenu();
            var first = menu.AddEntry(EntryKind.Toggle, "First");
            first.follows.Add(new FollowTarget { target = avatar.Hat, follows = avatar.Glasses.transform });
            var second = menu.AddEntry(EntryKind.Toggle, "Second");
            second.follows.Add(new FollowTarget { target = avatar.Hat, follows = avatar.Body.transform });
            var view = View(menu);
            var plan = view.Plan;

            var firstSummary = WhatThisBuildsModel.Compute(plan, view.Tree.RowFor(first.id));
            var secondSummary = WhatThisBuildsModel.Compute(plan, view.Tree.RowFor(second.id));

            Assert.That(firstSummary.Constraints, Is.EqualTo(1), "the first entry in tree order claims the shared target");
            Assert.That(secondSummary.Constraints, Is.EqualTo(0), "the second entry claims nothing new for the same target");

            // The claim map is computed once with the plan: asking again from the same plan must not change it.
            Assert.That(WhatThisBuildsModel.Compute(plan, view.Tree.RowFor(second.id)).Constraints, Is.EqualTo(0));
        }

        [Test]
        public void FollowDistanceCostsNoBits()
        {
            var menu = avatar.AddMenu();
            var trigger = menu.AddEntry(EntryKind.Trigger, "Pat");
            trigger.source = TriggerSource.Contact;
            trigger.contactAnchor = avatar.Hat.transform;
            trigger.touchMode = TouchMode.FollowDistance;
            trigger.contactFullAt = 0.8f;
            trigger.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            var view = View(menu);
            var plan = view.Plan;
            var row = view.Tree.RowFor(trigger.id);

            var summary = WhatThisBuildsModel.Compute(plan, row);

            // Bits can never be anything but 0 here: a trigger's own touch parameter never enters plan.Plan.Parameters
            // at all, whatever mode it is in. The real proof that the receiver's float costs nothing is the
            // expressionParameters check in FollowDistanceBuildsAProximityReceiverWithAnUnsyncedFloat.
            Assert.That(summary.Bits, Is.EqualTo(0));
            Assert.That(summary.Contacts, Is.EqualTo(1));
            Assert.That(summary.FxLayers, Is.EqualTo(1));

            var line = WhatThisBuildsModel.Describe(plan, row);
            Assert.That(line, Does.Contain("fades with distance, fully on at 80%"), "names the blend, not only the raw counts");
        }

        [Test]
        public void WhatThisBuildsShowsTheEffectiveFullAtAndWhoTrackingIsLeftTo()
        {
            var menu = avatar.AddMenu();
            var trigger = menu.AddEntry(EntryKind.Trigger, "Pat");
            trigger.source = TriggerSource.Contact;
            trigger.contactAnchor = avatar.Hat.transform;
            trigger.touchMode = TouchMode.FollowDistance;
            trigger.contactFullAt = 0f; // old data: the field initialiser is skipped on deserialise
            trigger.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            Assert.That(trigger.EffectiveContactFullAt, Is.EqualTo(0.8f).Within(0.0001f));

            // The avatar's own saved FX already has another tracking control setting Eyes to Animation, so the
            // pause line must say it is left to that layer instead of claiming AvatarDoc's own pause applies.
            avatar.Descriptor.enableEyeLook = true;
            avatar.Descriptor.customEyeLookSettings.eyelidType = VRCAvatarDescriptor.EyelidType.Blendshapes;
            avatar.Descriptor.customEyeLookSettings.eyelidsSkinnedMesh = avatar.Body;
            avatar.Descriptor.customEyeLookSettings.eyelidsBlendshapes = new[] { avatar.Body.sharedMesh.GetBlendShapeIndex("Blush"), -1, -1 };
            avatar.Fx.AddLayer("FT_Eye");
            var existingState = avatar.Fx.layers[avatar.Fx.layers.Length - 1].stateMachine.AddState("Idle");
            var control = ScriptableObject.CreateInstance<VRC.SDK3.Avatars.Components.VRCAnimatorTrackingControl>();
            control.trackingEyes = VRC.SDKBase.VRC_AnimatorTrackingControl.TrackingType.Animation;
            existingState.behaviours = new StateMachineBehaviour[] { control };

            var view = View(menu);
            var line = WhatThisBuildsModel.Describe(view.Plan, view.Tree.RowFor(trigger.id));

            Assert.That(line, Does.Contain("fully on at 80%"), "shows the effective value, not the raw 0");
            Assert.That(line, Does.Contain("Blinking is left to FT_Eye"));
            Assert.That(line, Does.Not.Contain("Blinking pauses"));
        }
    }
}
