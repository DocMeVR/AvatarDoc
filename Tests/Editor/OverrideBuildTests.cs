#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
#if DOCME_MA
using nadena.dev.modular_avatar.core;
#endif
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace DocMe.AvatarDoctor.Editor
{
    public class OverrideBuildTests
    {
        TestAvatar avatar;
        AvatarDoc menu;

        [SetUp]
        public void SetUp()
        {
            GeneratedAssets.Note();
            avatar = TestAvatar.Create();
            menu = avatar.AddMenu();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            GeneratedAssets.CleanUp();
        }

        static AnimationCurve CurveOf(Motion motion, string path, string property)
            => AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), property));

        static AnimatorState State(AnimatorController fx, string layerName, string stateName)
            => fx.layers.Single(l => l.name == layerName).stateMachine.states.Single(s => s.state.name == stateName).state;

        [Test]
        public void PinReplacesEveryCurveWithAConstant()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            avatar.AddFxClip("Frown", TestAvatar.ShapeClip("Frown", "Body", "Blush", 100f, 20f));
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var rules = new List<OverrideRule> { new OverrideRule { Renderer = avatar.Body, Shape = "Blush", Value = 0f, EntryName = "Silence blush", EntryId = "r1" } };

            var messages = OverrideBuilder.Pin(asc, rules);
            context.DeactivateAllExtensionContexts();

            var fx = avatar.Descriptor.baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
            foreach (var name in new[] { "Smile", "Frown" })
            {
                var curve = CurveOf(State(fx, "Base", name).motion, "Body", "blendShape.Blush");
                Assert.That(curve, Is.Not.Null);
                Assert.That(curve.Evaluate(0f), Is.EqualTo(0f));
                Assert.That(curve.Evaluate(0.5f), Is.EqualTo(0f));
                Assert.That(curve.Evaluate(1f), Is.EqualTo(0f));
            }
            var applied = messages.Single(m => m.Says("ad.info.override_applied"));
            Assert.That(applied.Args[2], Is.EqualTo("2"));
            Assert.That(applied.EntryIds, Does.Contain("r1"));
        }

        [Test]
        public void PinKeepsTheClipLength()
        {
            var source = new AnimationClip { name = "Smile" };
            AnimationUtility.SetEditorCurve(source, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"),
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(2f, 100f)));
            avatar.AddFxClip("Smile", source);
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var rules = new List<OverrideRule> { new OverrideRule { Renderer = avatar.Body, Shape = "Blush", Value = 30f, EntryName = "Hold blush", EntryId = "r5" } };

            OverrideBuilder.Pin(asc, rules);
            context.DeactivateAllExtensionContexts();

            var fx = avatar.Descriptor.baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
            var committed = (AnimationClip)State(fx, "Base", "Smile").motion;
            var curve = CurveOf(committed, "Body", "blendShape.Blush");
            Assert.That(curve.keys.Length, Is.EqualTo(2));
            Assert.That(curve.keys[1].time, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(curve.Evaluate(0f), Is.EqualTo(30f));
            Assert.That(curve.Evaluate(1f), Is.EqualTo(30f));
            Assert.That(curve.Evaluate(2f), Is.EqualTo(30f));
            Assert.That(committed.length, Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        public void UnusedOverrideWarns()
        {
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var rules = new List<OverrideRule> { new OverrideRule { Renderer = avatar.Body, Shape = "Eye_Big", Value = 50f, EntryName = "Big eyes", EntryId = "r2" } };

            var messages = OverrideBuilder.Pin(asc, rules);
            context.DeactivateAllExtensionContexts();

            Assert.That(messages.Single().Key, Is.EqualTo("ad.warn.override_unused"));
            Assert.That(messages.Single().EntryIds, Does.Contain("r2"));
        }

        [Test]
        public void DestroyedRendererWarns()
        {
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var rules = new List<OverrideRule> { new OverrideRule { Renderer = null, Shape = "Blush", Value = 0f, EntryName = "Silence blush", EntryId = "r6" } };

            var messages = OverrideBuilder.Pin(asc, rules);
            context.DeactivateAllExtensionContexts();

            var gone = messages.Single();
            Assert.That(gone.Key, Is.EqualTo("ad.warn.override_target_gone"));
            Assert.That(gone.Args[0], Is.EqualTo("Silence blush"));
            Assert.That(gone.Args[1], Is.EqualTo("Blush"));
            Assert.That(gone.EntryIds, Does.Contain("r6"));
        }

        [Test]
        public void WholePipelinePinsAndSetsTheScene()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            avatar.Body.SetBlendShapeWeight(0, 60f);
            var rule = MenuEntry.Create(EntryKind.Override, "Silence blush");
            rule.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 0f });
            menu.entries.Add(rule);

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                var descriptor = processed.GetComponent<VRCAvatarDescriptor>();
                var fx = descriptor.baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
                var curve = CurveOf(State(fx, "Base", "Smile").motion, "Body", "blendShape.Blush");
                Assert.That(curve.Evaluate(1f), Is.EqualTo(0f));
                var body = processed.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
                Assert.That(body.GetBlendShapeWeight(0), Is.EqualTo(0f));
                Assert.That(processed.GetComponentsInChildren<AvatarDoc>(true), Is.Empty);
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                GeneratedAssets.CleanUp();
            }
        }

        [Test]
        public void ReadyGateLeavesClipsUntouchedWhenTheModelFails()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var first = menu.AddEntry(EntryKind.Toggle, "A");
            first.exclusiveGroup = "Outfits";
            first.groupValue = 1;
            first.objects.Add(new ObjectTarget { target = avatar.Hat });
            var second = menu.AddEntry(EntryKind.Toggle, "B");
            second.exclusiveGroup = "Outfits";
            second.groupValue = 1;
            second.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var rule = MenuEntry.Create(EntryKind.Override, "Silence blush");
            rule.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 0f });
            menu.entries.Add(rule);

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                Assert.That(processed, Is.Not.Null, "the pipeline reports the model error without throwing");
                var fx = processed.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
                var curve = CurveOf(State(fx, "Base", "Smile").motion, "Body", "blendShape.Blush");
                Assert.That(curve, Is.Not.Null);
                Assert.That(curve.Evaluate(0f), Is.EqualTo(0f));
                Assert.That(curve.Evaluate(1f), Is.EqualTo(100f));
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                GeneratedAssets.CleanUp();
            }
        }

#if DOCME_MA
        [Test]
        public void ClipsMergedByModularAvatarArePinned()
        {
            const string folder = GeneratedAssets.Folder;
            GeneratedAssets.Ensure();
            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/MergedFx.controller");
            var merged = TestAvatar.ShapeClip("Merged", "Body", "Blush", 0f, 100f);
            AssetDatabase.AddObjectToAsset(merged, controller);
            controller.AddMotion(merged);
            AssetDatabase.SaveAssets();

            var holder = new GameObject("Merge");
            holder.transform.SetParent(avatar.Root.transform);
            var merge = holder.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = controller;
            merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Absolute;
            var rule = MenuEntry.Create(EntryKind.Override, "Silence blush");
            rule.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 0f });
            menu.entries.Add(rule);

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                var descriptor = processed.GetComponent<VRCAvatarDescriptor>();
                var fx = descriptor.baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
                var pinned = fx.animationClips.Where(c => CurveOf(c, "Body", "blendShape.Blush") != null).ToList();
                Assert.That(pinned, Is.Not.Empty, "Modular Avatar merged the clip into the FX controller");
                foreach (var clip in pinned)
                {
                    var curve = CurveOf(clip, "Body", "blendShape.Blush");
                    Assert.That(curve.Evaluate(0f), Is.EqualTo(0f), clip.name);
                    Assert.That(curve.Evaluate(1f), Is.EqualTo(0f), clip.name);
                }
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                AssetDatabase.DeleteAsset(folder);
            }
        }
#endif

        [Test]
        public void OverrideWinsOverAPinOnTheSameShape()
        {
            var mute = menu.AddEntry(EntryKind.Toggle, "Mute");
            mute.objects.Add(new ObjectTarget { target = avatar.Hat });
            mute.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 0f, mode = ShapeEffectMode.Pin });
            var rule = MenuEntry.Create(EntryKind.Override, "Hold blush");
            rule.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 50f });
            menu.entries.Add(rule);

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                var descriptor = processed.GetComponent<VRCAvatarDescriptor>();
                var fx = descriptor.baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
                Assert.That(CurveOf(State(fx, "AD: Mute (pin)", "On").motion, "Body", "blendShape.Blush").Evaluate(0f), Is.EqualTo(50f));
                Assert.That(CurveOf(State(fx, "AD: Mute", "On").motion, "Body", "blendShape.Blush"), Is.Null);
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                GeneratedAssets.CleanUp();
            }
        }
    }
}
#endif
