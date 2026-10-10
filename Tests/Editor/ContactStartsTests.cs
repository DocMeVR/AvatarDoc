#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Phase 13 task 8: the three ready made Follow distance contact starts (Headpat, Eye touch, Nose boop).</summary>
    public class ContactStartsTests
    {
        TestAvatar avatar;
        AvatarDoc menu;

        [SetUp]
        public void SetUp()
        {
            GeneratedAssets.Note();
            avatar = TestAvatar.Create();
            menu = avatar.AddMenu();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            GeneratedAssets.CleanUp();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        AvatarDoctorView View(AvatarDoc m) => new AvatarDoctorView(new SerializedObject(m), m);

        static void AssertVector3(Vector3 actual, Vector3 expected, string message = null)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f), message);
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f), message);
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.001f), message);
        }

        [Test]
        public void ContactStartsAreListedInMoreWithDescriptions()
        {
            var view = View(menu);
            var items = view.Tree.ContactStartItems();

            Assert.That(items.Select(i => i.NameKey), Is.EqualTo(new[]
            {
                "ad.ui.add.contact_starts.headpat", "ad.ui.add.contact_starts.eye_touch", "ad.ui.add.contact_starts.nose_boop",
            }));
            foreach (var item in items)
            {
                Assert.That(L.Text(item.NameKey), Is.Not.Null.And.Not.Empty, item.NameKey);
                Assert.That(L.Text(item.DescriptionKey), Is.Not.Null.And.Not.Empty, item.DescriptionKey);
                Assert.That(item.NameKey, Is.Not.EqualTo(item.DescriptionKey));
            }

            var moreMenu = view.Tree.MoreMenu;
            var triggerIndex = moreMenu.ToList().FindIndex(i => i.Text == L.Text("ad.ui.add.trigger"));
            Assert.That(triggerIndex, Is.GreaterThanOrEqualTo(0), "Trigger must still be in the menu");
            Assert.That(moreMenu[triggerIndex].Description, Is.EqualTo(L.Text("ad.ui.add.trigger.tip")));

            var group = moreMenu.Last();
            Assert.That(group.Text, Is.EqualTo(L.Text("ad.ui.add.contact")), "the starts sit in a submenu as the last add");
            Assert.That(group.Children.Select(c => c.Text), Is.EqualTo(new[]
            {
                L.Text("ad.ui.add.contact_starts.headpat"), L.Text("ad.ui.add.contact_starts.eye_touch"), L.Text("ad.ui.add.contact_starts.nose_boop"),
            }));
            Assert.That(group.Children.Select(c => c.Description), Is.EqualTo(new[]
            {
                L.Text("ad.ui.add.contact_starts.headpat.tip"), L.Text("ad.ui.add.contact_starts.eye_touch.tip"), L.Text("ad.ui.add.contact_starts.nose_boop.tip"),
            }));
        }

        [Test]
        public void HeadpatSitsAboveTheHeadScaledToTheAvatar()
        {
            avatar.AddHumanoidRig();
            avatar.Descriptor.ViewPosition = new Vector3(0f, 1.6f, 0f);
            avatar.HeadBone.position = new Vector3(0f, 1.6f, 0f);
            var created1 = ContactStarts.Apply(menu, ContactStarts.StartKind.Headpat, out var error1);
            Assert.That(error1, Is.Null);
            var headpat1 = created1[0];

            using (var avatar2 = TestAvatar.Create())
            {
                var menu2 = avatar2.AddMenu();
                avatar2.AddHumanoidRig();
                avatar2.Descriptor.ViewPosition = new Vector3(0f, 3.2f, 0f);
                avatar2.HeadBone.position = new Vector3(0f, 3.2f, 0f);
                var created2 = ContactStarts.Apply(menu2, ContactStarts.StartKind.Headpat, out var error2);
                Assert.That(error2, Is.Null);
                var headpat2 = created2[0];

                Assert.That(headpat1.contactAnchor, Is.EqualTo(avatar.HeadBone));
                Assert.That(headpat2.contactAnchor, Is.EqualTo(avatar2.HeadBone));
                AssertVector3(headpat2.contactOffset, headpat1.contactOffset * 2f, "a 2x avatar gives twice the offset");
                Assert.That(headpat2.contactRadius, Is.EqualTo(headpat1.contactRadius * 2f).Within(0.0001f), "a 2x avatar gives twice the radius");
                Assert.That(headpat1.contactTags, Is.EqualTo(new List<string> { "Hand", "Finger" }));
                Assert.That(headpat1.contactFullAt, Is.EqualTo(0.7f).Within(0.0001f));
                Assert.That(headpat1.contactAllowSelf, Is.True);
                Assert.That(headpat1.contactAllowOthers, Is.True);
                Assert.That(headpat1.IsFollowDistanceTrigger, Is.True);
            }
        }

        [Test]
        public void EyeTouchMakesOnePerEyeWhenEyeBonesExist()
        {
            avatar.AddHumanoidRig(includeEyeBones: true);
            avatar.Descriptor.ViewPosition = new Vector3(0f, 1.6f, 0f);

            var created = ContactStarts.Apply(menu, ContactStarts.StartKind.EyeTouch, out var errorKey);

            Assert.That(errorKey, Is.Null);
            Assert.That(created.Select(e => e.name), Is.EqualTo(new[] { "Eye touch L", "Eye touch R" }));
            Assert.That(created[0].contactAnchor, Is.EqualTo(avatar.LeftEyeBone));
            Assert.That(created[1].contactAnchor, Is.EqualTo(avatar.RightEyeBone));
            foreach (var entry in created)
            {
                Assert.That(entry.source, Is.EqualTo(TriggerSource.Contact));
                Assert.That(entry.touchMode, Is.EqualTo(TouchMode.FollowDistance));
                Assert.That(entry.contactTags, Is.EqualTo(new List<string> { "Finger" }));
                Assert.That(entry.contactFullAt, Is.EqualTo(0.8f).Within(0.0001f));
                Assert.That(entry.IsFollowDistanceTrigger, Is.True);
            }
        }

        [Test]
        public void EyeTouchFallsBackToOneWithoutEyeBones()
        {
            avatar.AddHumanoidRig();
            avatar.Descriptor.ViewPosition = new Vector3(0f, 1.6f, 0f);

            var created = ContactStarts.Apply(menu, ContactStarts.StartKind.EyeTouch, out var errorKey);

            Assert.That(errorKey, Is.Null);
            Assert.That(created.Count, Is.EqualTo(1));
            Assert.That(created[0].name, Is.EqualTo("Eye touch"));
            Assert.That(created[0].contactAnchor, Is.EqualTo(avatar.HeadBone));
            Assert.That(created[0].IsFollowDistanceTrigger, Is.True);
        }

        [Test]
        public void NoseBoopGuessesTheMouthOpenShape()
        {
            avatar.AddHumanoidRig();
            avatar.Descriptor.ViewPosition = new Vector3(0f, 1.6f, 0f);
            avatar.AddBodyShapes("MouthOpen");

            var created = ContactStarts.Apply(menu, ContactStarts.StartKind.NoseBoop, out var errorKey);

            Assert.That(errorKey, Is.Null);
            var entry = created[0];
            Assert.That(entry.blendShapes.Count, Is.EqualTo(1));
            Assert.That(entry.blendShapes[0].renderer, Is.EqualTo(avatar.Body));
            Assert.That(entry.blendShapes[0].shape, Is.EqualTo("MouthOpen"));
            Assert.That(entry.blendShapes[0].weight, Is.EqualTo(100f).Within(0.0001f));
        }

        [Test]
        public void ShapeGuessIgnoresCaseAndFallsBackToBothEyes()
        {
            avatar.AddHumanoidRig(includeEyeBones: true);
            avatar.Descriptor.ViewPosition = new Vector3(0f, 1.6f, 0f);
            // Upper case, and only the shared "both eyes" name: neither eye has a dedicated left or right shape.
            avatar.AddBodyShapes("BLINK");

            var created = ContactStarts.Apply(menu, ContactStarts.StartKind.EyeTouch, out var errorKey);

            Assert.That(errorKey, Is.Null);
            var left = created.First(e => e.name == "Eye touch L");
            var right = created.First(e => e.name == "Eye touch R");
            Assert.That(left.blendShapes[0].shape, Is.EqualTo("BLINK"));
            Assert.That(right.blendShapes[0].shape, Is.EqualTo("BLINK"));
        }

        [Test]
        public void NoMatchingShapeLeavesTheRowEmpty()
        {
            avatar.AddHumanoidRig();
            avatar.Descriptor.ViewPosition = new Vector3(0f, 1.6f, 0f);
            // avatar.Body only carries "Blush" and "Eye_Big", neither of which matches Headpat's guess list.

            var created = ContactStarts.Apply(menu, ContactStarts.StartKind.Headpat, out var errorKey);

            Assert.That(errorKey, Is.Null);
            var entry = created[0];
            Assert.That(entry.blendShapes.Count, Is.EqualTo(1));
            Assert.That(entry.blendShapes[0].renderer, Is.EqualTo(avatar.Body));
            Assert.That(entry.blendShapes[0].shape, Is.Empty);
        }

        [Test]
        public void AStartIsOneUndoStep()
        {
            avatar.AddHumanoidRig(includeEyeBones: true);
            avatar.Descriptor.ViewPosition = new Vector3(0f, 1.6f, 0f);

            Undo.IncrementCurrentGroup();
            var created = ContactStarts.Apply(menu, ContactStarts.StartKind.EyeTouch, out var errorKey);

            Assert.That(errorKey, Is.Null);
            Assert.That(created.Count, Is.EqualTo(2));
            Assert.That(menu.entries.Count, Is.EqualTo(2));

            Undo.PerformUndo();

            Assert.That(menu.entries.Count, Is.EqualTo(0), "both triggers a start makes are undone together");
        }

        [Test]
        public void AStartWithoutAHeadBoneIsRefused()
        {
            // No AddHumanoidRig at all: the avatar has no Animator, so no Head bone either.
            var created = ContactStarts.Apply(menu, ContactStarts.StartKind.Headpat, out var errorKey);

            Assert.That(created, Is.Empty);
            Assert.That(errorKey, Is.EqualTo("ad.error.contact_start_no_head"));
            Assert.That(L.Text(errorKey), Is.Not.Null.And.Not.Empty);
            Assert.That(menu.entries.Count, Is.EqualTo(0));
        }

        [Test]
        public void AStartIsRefusedWhileLocked()
        {
            avatar.AddHumanoidRig();
            avatar.Descriptor.ViewPosition = new Vector3(0f, 1.6f, 0f);
            menu.locked = true;

            var created = ContactStarts.Apply(menu, ContactStarts.StartKind.Headpat, out var errorKey);

            Assert.That(created, Is.Empty);
            Assert.That(errorKey, Is.Null);
            Assert.That(menu.entries.Count, Is.EqualTo(0));
        }

        [Test]
        public void StartsBuildFollowDistanceTriggers()
        {
            avatar.AddHumanoidRig();
            avatar.Descriptor.ViewPosition = new Vector3(0f, 1.6f, 0f);
            var created = ContactStarts.Apply(menu, ContactStarts.StartKind.Headpat, out var errorKey);
            Assert.That(errorKey, Is.Null);
            var entry = created[0];
            Assert.That(entry.IsFollowDistanceTrigger, Is.True);

            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
            var existing = ParametersWriter.ExistingParameters(avatar.Descriptor.expressionParameters, avatar.Fx);
            var plan = ParameterPlanner.Plan(tree, existing);
            var touch = TouchBuilder.Describe(tree, existing, plan);
            var context = new BuildContext(avatar.Root, null);
            TouchBuilder.Create(context, tree, touch);

            var receiver = entry.contactAnchor.GetComponentInChildren<VRCContactReceiver>();
            Assert.That(receiver, Is.Not.Null, "the trigger's own contact anchor gets a receiver on the clone");
            Assert.That(receiver.receiverType, Is.EqualTo(ContactReceiver.ReceiverType.Proximity), "Follow distance always forces a Proximity receiver");
            Assert.That(receiver.collisionTags, Is.EqualTo(new List<string> { "Hand", "Finger" }));
        }
    }
}
#endif
