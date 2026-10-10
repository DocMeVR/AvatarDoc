using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>AvatarObjectPicker: the avatar's own picker that replaces Unity's scene wide object selector on a field that names something on the avatar. Choices and Accepts are pure and are what these tests check; PickerSearchTests opens the menu itself.</summary>
    public class PickerTests
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
            AvatarObjectPicker.ShowOverride = null;
        }

        [Test]
        public void ChoicesListOnlyThisAvatarsObjects()
        {
            var other = TestAvatar.Create();
            try
            {
                var choices = AvatarObjectPicker.Choices(avatar.Root.transform, PickKind.GameObject);

                var targets = choices.Select(c => c.Target).ToList();
                Assert.That(targets.Contains(avatar.Hat), Is.True);
                Assert.That(targets.Contains(avatar.Glasses), Is.True);
                Assert.That(targets.Contains(other.Hat), Is.False);
                Assert.That(targets.Contains(other.Glasses), Is.False);
            }
            finally { other.Dispose(); }
        }

        [Test]
        public void NestedAvatarObjectsAreLeftOut()
        {
            var nestedRoot = new GameObject("NestedAvatar");
            nestedRoot.transform.SetParent(avatar.Root.transform);
            nestedRoot.AddComponent<VRCAvatarDescriptor>();
            var nestedChild = new GameObject("NestedHat");
            nestedChild.transform.SetParent(nestedRoot.transform);

            var targets = AvatarObjectPicker.Choices(avatar.Root.transform, PickKind.GameObject).Select(c => c.Target).ToList();

            Assert.That(targets.Contains(avatar.Hat), Is.True, "still this avatar's own object");
            Assert.That(targets.Contains(nestedRoot), Is.False, "the nested avatar's own root");
            Assert.That(targets.Contains(nestedChild), Is.False, "and everything under it");
        }

        [Test]
        public void RendererKindListsOnlyRenderers()
        {
            var hatRenderer = avatar.Hat.GetComponent<MeshRenderer>();

            var choices = AvatarObjectPicker.Choices(avatar.Root.transform, PickKind.Renderer);
            var targets = choices.Select(c => c.Target).ToList();

            Assert.That(targets.Contains(avatar.Body), Is.True);
            Assert.That(targets.Contains(hatRenderer), Is.True);
            Assert.That(choices.Any(c => (c.Target as Component)?.gameObject == avatar.Glasses), Is.False, "Glasses has no renderer");
            Assert.That(choices.All(c => c.Target is Renderer), Is.True);
        }

        [Test]
        public void ShapeKindSkipsMeshesWithoutBlendShapes()
        {
            var plain = new GameObject("PlainMesh");
            plain.transform.SetParent(avatar.Root.transform);
            var plainRenderer = plain.AddComponent<SkinnedMeshRenderer>();
            plainRenderer.sharedMesh = TestAvatar.QuadWithShapes();

            var targets = AvatarObjectPicker.Choices(avatar.Root.transform, PickKind.SkinnedMeshWithShapes).Select(c => c.Target).ToList();

            Assert.That(targets.Contains(avatar.Body), Is.True, "has blendshapes");
            Assert.That(targets.Contains(plainRenderer), Is.False, "no blendshapes on its mesh");
        }

        [Test]
        public void PhysBoneKindListsPhysBones()
        {
            var pb = avatar.Glasses.AddComponent<VRCPhysBone>();

            var choices = AvatarObjectPicker.Choices(avatar.Root.transform, PickKind.PhysBone);

            Assert.That(choices.Count, Is.EqualTo(1));
            Assert.That(choices[0].Target, Is.SameAs(pb));
        }

        [Test]
        public void TransformKindOffersBonesFirst()
        {
            // The test avatar has no Animator, so there is no humanoid to build a bones group from; this checks
            // that the group is simply absent rather than constructing a full humanoid rig for the test.
            var choices = AvatarObjectPicker.Choices(avatar.Root.transform, PickKind.Transform);

            Assert.That(choices.Select(c => c.Target), Does.Contain(avatar.Hat.transform));
            Assert.That(choices.Any(c => c.IsBone), Is.False, "no humanoid Animator, so nothing can be offered as a bone");
        }

        [Test]
        public void InactiveObjectsAreListedAndMarked()
        {
            avatar.Hat.SetActive(false);

            var choices = AvatarObjectPicker.Choices(avatar.Root.transform, PickKind.GameObject);

            var hat = choices.First(c => c.Target == avatar.Hat);
            Assert.That(hat.Inactive, Is.True);
            var glasses = choices.First(c => c.Target == avatar.Glasses);
            Assert.That(glasses.Inactive, Is.False);
        }

        [Test]
        public void AcceptsRefusesObjectsOfAnotherAvatarAndAssets()
        {
            var other = TestAvatar.Create();
            try
            {
                Assert.That(AvatarObjectPicker.Accepts(avatar.Root.transform, avatar.Hat, PickKind.GameObject), Is.True);
                Assert.That(AvatarObjectPicker.Accepts(avatar.Root.transform, other.Hat, PickKind.GameObject), Is.False, "another avatar's object");
                Assert.That(AvatarObjectPicker.Accepts(avatar.Root.transform, avatar.MaterialA, PickKind.GameObject), Is.False, "an asset is never a valid GameObject target");
                Assert.That(AvatarObjectPicker.Accepts(avatar.Root.transform, null, PickKind.GameObject), Is.True, "clearing the field is always allowed");
            }
            finally { other.Dispose(); }
        }

        [Test]
        public void ObjectTargetFieldUsesTheAvatarPicker()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            view.Tree.SelectEntry(toggle.id);

            var list = view.Detail.Q<TargetListView>("ad-objects");
            Assert.That(list, Is.Not.Null);
            var row = list.RowAt(0);
            var field = row.Q<ObjectField>("target");
            Assert.That(field, Is.Not.Null);

            var selector = field.Q<VisualElement>(className: ObjectField.selectorUssClassName);
            Assert.That(selector, Is.Not.Null);
            Assert.That(selector.userData, Is.EqualTo(PickKind.GameObject));
        }

        [Test]
        public void LockedComponentDoesNotOpenThePicker()
        {
            var menu = avatar.AddMenu();
            var trigger = menu.AddEntry(EntryKind.Trigger, "Poke");
            trigger.source = TriggerSource.Contact;
            trigger.contactAnchor = avatar.Hat.transform;
            menu.locked = true;
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            view.Tree.SelectEntry(trigger.id);

            var anchorField = view.Detail.Q<ObjectField>("ad-touch-anchor");
            Assert.That(anchorField, Is.Not.Null);
            var selector = anchorField.Q<VisualElement>(className: ObjectField.selectorUssClassName);
            Assert.That(selector.enabledInHierarchy, Is.False, "the whole panel is disabled while locked");

            bool opened = false;
            AvatarObjectPicker.ShowOverride = (_, __, ___, ____) => opened = true;

            bool result = AvatarObjectPicker.TryOpen(selector, () => avatar.Root.transform, PickKind.Transform, _ => { });

            Assert.That(result, Is.False);
            Assert.That(opened, Is.False);
        }
    }
}
