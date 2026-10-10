#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf.preview;
using NUnit.Framework;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    public class PreviewFilterTests
    {
        TestAvatar avatar;
        AvatarDoc menu;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            menu = avatar.AddMenu();
            PreviewState.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            PreviewState.ClearAll();
            avatar.Dispose();
        }

        [Test]
        public void GroupsFollowTheOverrides()
        {
            var hide = MenuEntry.Create(EntryKind.Toggle, "Hide hat");
            hide.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = false });
            menu.entries.Add(hide);

            var groups = AvatarDoctorPreview.GroupsFor(new ComputeContext("AvatarDoctor test"), avatar.Root).ToList();
            Assert.That(groups, Is.Empty);

            PreviewState.Set(hide.id, 1f);
            groups = AvatarDoctorPreview.GroupsFor(new ComputeContext("AvatarDoctor test"), avatar.Root).ToList();
            var group = groups.Single();
            Assert.That(group.Renderers.Single(), Is.SameAs(avatar.Hat.GetComponent<MeshRenderer>()));
            Assert.That(group.GetData<RendererState>().Visible, Is.False);
        }

        [Test]
        public void NodeWritesVisibilityOnlyToTheProxy()
        {
            var original = avatar.Hat.GetComponent<MeshRenderer>();
            var proxyObject = Object.Instantiate(avatar.Hat);
            var proxy = proxyObject.GetComponent<MeshRenderer>();
            try
            {
                var node = new AvatarDoctorPreview.Node(new RendererState { Visible = false });
                node.OnFrame(original, proxy);
                Assert.That(proxy.enabled, Is.False);
                Assert.That(original.enabled, Is.True);
            }
            finally { Object.DestroyImmediate(proxyObject); }
        }

        [Test]
        public void NodeWritesShapesAndMaterialsToTheProxy()
        {
            var proxyObject = Object.Instantiate(avatar.Body.gameObject);
            var proxy = proxyObject.GetComponent<SkinnedMeshRenderer>();
            try
            {
                var state = new RendererState
                {
                    Shapes = ImmutableSortedDictionary<int, float>.Empty.Add(1, 30f),
                    Materials = ImmutableSortedDictionary<int, Material>.Empty.Add(1, avatar.MaterialB),
                };
                var node = new AvatarDoctorPreview.Node(state);
                Assert.That(node.WhatChanged & RenderAspects.Shapes, Is.Not.EqualTo((RenderAspects)0));
                node.OnFrame(avatar.Body, proxy);
                Assert.That(proxy.GetBlendShapeWeight(1), Is.EqualTo(30f));
                Assert.That(proxy.sharedMaterials[1], Is.SameAs(avatar.MaterialB));
                Assert.That(avatar.Body.GetBlendShapeWeight(1), Is.EqualTo(0f));
                Assert.That(avatar.Body.sharedMaterials[1], Is.Not.SameAs(avatar.MaterialB));
            }
            finally { Object.DestroyImmediate(proxyObject); }
        }
    }
}
#endif
