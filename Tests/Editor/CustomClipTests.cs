using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;

namespace DocMe.AvatarDoctor.Editor
{
    public class CustomClipTests
    {
        TestAvatar avatar;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
        }

        (MenuTree tree, ParameterPlan plan) Model(params AvatarDoc[] menus)
        {
            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, menus);
            return (tree, ParameterPlanner.Plan(tree, new Dictionary<string, PlannedType> { { "Existing", PlannedType.Bool } }));
        }

        static AnimatorController FxOf(TestAvatar a)
        {
            return (AnimatorController)a.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
        }

        /// <summary>Runs model, defaults and the animator builder inside an NDMF context and returns the committed FX controller.</summary>
        AnimatorController BuildAnimator(out List<ErrorContext> errors, params AvatarDoc[] menus)
        {
            var (tree, plan) = Model(menus);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var captured = ErrorReport.CaptureErrors(() => L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState)));
            context.DeactivateAllExtensionContexts();
            errors = captured;
            return FxOf(avatar);
        }

        static AnimatorControllerLayer Layer(AnimatorController fx, string name) => fx.layers.Single(l => l.name == name);

        static AnimatorState State(AnimatorControllerLayer layer, string name) => layer.stateMachine.states.Single(s => s.state.name == name).state;

        static float FloatValue(Motion motion, string path, System.Type type, string property)
        {
            var curve = AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve(path, type, property));
            Assert.That(curve, Is.Not.Null, path + " " + property);
            return curve.Evaluate(0f);
        }

        static bool HasCurve(Motion motion, string path, System.Type type, string property)
        {
            return AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve(path, type, property)) != null;
        }

        [Test]
        public void ToggleOnClipContainsTheCustomCurves()
        {
            var clip = new AnimationClip { name = "Wag" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"), new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 100f)));
            try
            {
                var menu = avatar.AddMenu();
                var toggle = menu.AddEntry(EntryKind.Toggle, "Wag");
                toggle.animations.Add(new AnimationTarget { clip = clip });

                var fx = BuildAnimator(out var errors, menu);

                Assert.That(errors, Is.Empty);
                var on = State(Layer(fx, "AD: Wag"), "On");
                var merged = AnimationUtility.GetEditorCurve((AnimationClip)on.motion, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"));
                Assert.That(merged, Is.Not.Null);
                Assert.That(merged.Evaluate(0f), Is.EqualTo(0f).Within(0.001f));
                Assert.That(merged.Evaluate(1f), Is.EqualTo(100f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void GeneratedValuesWinOverCustomCurves()
        {
            var clip = new AnimationClip { name = "Clip" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), new AnimationCurve(new Keyframe(0f, 10f), new Keyframe(1f, 20f)));
            try
            {
                var menu = avatar.AddMenu();
                var toggle = menu.AddEntry(EntryKind.Toggle, "Smile");
                toggle.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 75f });
                toggle.animations.Add(new AnimationTarget { clip = clip });

                var fx = BuildAnimator(out var errors, menu);

                var on = State(Layer(fx, "AD: Smile"), "On");
                Assert.That(FloatValue(on.motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), Is.EqualTo(75f).Within(0.001f));
                Assert.That(errors.Any(e => e.TheError is SimpleError s && (s.TitleKey == "ad.info.animation_overridden" || s.TitleKey == "ad.info.animation_overridden.one")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void OffClipRestoresSceneValuesOfCustomBindings()
        {
            avatar.Hat.transform.localPosition = new Vector3(0f, 5f, 0f);
            var clip = new AnimationClip { name = "Bob" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.y"), new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 10f)));
            try
            {
                var menu = avatar.AddMenu();
                var toggle = menu.AddEntry(EntryKind.Toggle, "Bob");
                toggle.animations.Add(new AnimationTarget { clip = clip, pathRoot = avatar.Hat.transform });

                var fx = BuildAnimator(out var errors, menu);

                Assert.That(errors, Is.Empty);
                var off = State(Layer(fx, "AD: Bob"), "Off");
                var restored = AnimationUtility.GetEditorCurve((AnimationClip)off.motion, EditorCurveBinding.FloatCurve("Hat", typeof(Transform), "m_LocalPosition.y"));
                Assert.That(restored, Is.Not.Null);
                Assert.That(restored.Evaluate(0f), Is.EqualTo(5f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void PathRootPrefixesEveryPath()
        {
            var clip = new AnimationClip { name = "Local" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0f, 1f, 1f));
            try
            {
                var entry = MenuEntry.Create(EntryKind.Toggle, "X");
                entry.animations.Add(new AnimationTarget { clip = clip, pathRoot = avatar.Hat.transform });

                var result = CustomClips.Collect(entry, avatar.Root.transform, new List<ModelMessage>());

                Assert.That(result.Single().binding.path, Is.EqualTo("Hat"));
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void MissingBindingsWarnOnce()
        {
            var clip = new AnimationClip { name = "Ghost" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("NoSuchChild", typeof(Transform), "m_LocalPosition.y"), new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f)));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("AlsoMissing", typeof(Transform), "m_LocalScale.x"), new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 2f)));
            try
            {
                var menu = avatar.AddMenu();
                var toggle = menu.AddEntry(EntryKind.Toggle, "Ghost");
                toggle.animations.Add(new AnimationTarget { clip = clip });

                var fx = BuildAnimator(out var errors, menu);

                var warning = errors.Single(e => e.TheError is SimpleError s && (s.TitleKey == "ad.warn.animation_binding_missing" || s.TitleKey == "ad.warn.animation_binding_missing.one"));
                Assert.That(warning.TheError.ToMessage(), Does.Contain("2"));
                var off = State(Layer(fx, "AD: Ghost"), "Off");
                Assert.That(HasCurve(off.motion, "NoSuchChild", typeof(Transform), "m_LocalPosition.y"), Is.False);
                Assert.That(HasCurve(off.motion, "AlsoMissing", typeof(Transform), "m_LocalScale.x"), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void ProxyClipsAreRefused()
        {
            var clip = new AnimationClip { name = "proxy_Sit" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), AnimationCurve.Constant(0f, 1f, 100f));
            try
            {
                var menu = avatar.AddMenu();
                var toggle = menu.AddEntry(EntryKind.Toggle, "Sit");
                toggle.animations.Add(new AnimationTarget { clip = clip });

                var (tree, _) = Model(menu);
                Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.animation_proxy" && m.EntryIds.Contains(toggle.id)), Is.True);

                var fx = BuildAnimator(out var errors, menu);

                Assert.That(errors.Any(e => e.TheError is SimpleError s && s.TitleKey == "ad.warn.animation_proxy"), Is.True);
                var on = State(Layer(fx, "AD: Sit"), "On");
                Assert.That(HasCurve(on.motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void RadialWithClipsUsesMotionTime()
        {
            var clip = new AnimationClip { name = "Spin" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"), new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 50f)));
            try
            {
                var menu = avatar.AddMenu();
                var dial = menu.AddEntry(EntryKind.Radial, "Dial");
                dial.animations.Add(new AnimationTarget { clip = clip });

                var (tree, plan) = Model(menu);
                var control = tree.Controls.Single(c => c.Entry.PlainName == "Dial");
                var paramName = plan.Bindings[control].Parameter.Name;

                var fx = BuildAnimator(out var errors, menu);

                Assert.That(errors, Is.Empty);
                var layer = Layer(fx, "AD: Dial");
                var state = layer.stateMachine.states.Single().state;
                Assert.That(state.motion, Is.InstanceOf<AnimationClip>());
                Assert.That(state.timeParameterActive, Is.True);
                Assert.That(state.timeParameter, Is.EqualTo(paramName));
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void RadialRampIsLinearAtAQuarter()
        {
            var clip = new AnimationClip { name = "Spin" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Hat", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0f, 1f, 1f));
            try
            {
                var menu = avatar.AddMenu();
                var dial = menu.AddEntry(EntryKind.Radial, "Dial");
                dial.animations.Add(new AnimationTarget { clip = clip });
                dial.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", minWeight = 0f, maxWeight = 100f });

                var fx = BuildAnimator(out var errors, menu);

                Assert.That(errors, Is.Empty);
                var motion = (AnimationClip)Layer(fx, "AD: Dial").stateMachine.states.Single().state.motion;
                var curve = AnimationUtility.GetEditorCurve(motion, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"));
                Assert.That(curve, Is.Not.Null);
                float length = curve.keys[curve.length - 1].time;
                Assert.That(curve.Evaluate(length * 0.25f), Is.EqualTo(25f).Within(0.01f));
                Assert.That(curve.Evaluate(length * 0.5f), Is.EqualTo(50f).Within(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void RadialMergedClipNeverLoops()
        {
            var clip = new AnimationClip { name = "Spin" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Hat", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0f, 1f, 1f));
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            try
            {
                var menu = avatar.AddMenu();
                var dial = menu.AddEntry(EntryKind.Radial, "Dial");
                dial.animations.Add(new AnimationTarget { clip = clip });

                var fx = BuildAnimator(out var errors, menu);

                Assert.That(errors, Is.Empty);
                var motion = (AnimationClip)Layer(fx, "AD: Dial").stateMachine.states.Single().state.motion;
                Assert.That(AnimationUtility.GetAnimationClipSettings(motion).loopTime, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void RadialWithoutClipsKeepsTheBlendTree()
        {
            var menu = avatar.AddMenu();
            var dial = menu.AddEntry(EntryKind.Radial, "Dial");
            dial.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", minWeight = 0f, maxWeight = 100f });

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: Dial");
            var state = layer.stateMachine.states.Single().state;
            Assert.That(state.motion, Is.InstanceOf<BlendTree>());
        }

        [Test]
        public void PathRootOutsideTheAvatarIsSkippedWithAWarning()
        {
            var outside = new GameObject("Outside");
            var clip = new AnimationClip { name = "Ext" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0f, 1f, 1f));
            try
            {
                var menu = avatar.AddMenu();
                var toggle = menu.AddEntry(EntryKind.Toggle, "Ext");
                toggle.animations.Add(new AnimationTarget { clip = clip, pathRoot = outside.transform });

                var (tree, _) = Model(menu);
                Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.target_outside_avatar" && m.EntryIds.Contains(toggle.id)), Is.True);

                var fx = BuildAnimator(out var errors, menu);

                var on = State(Layer(fx, "AD: Ext"), "On");
                Assert.That(HasCurve(on.motion, "Outside", typeof(GameObject), "m_IsActive"), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(clip);
                Object.DestroyImmediate(outside);
            }
        }

        [Test]
        public void GroupOffRestoresASharedBindingOnce()
        {
            avatar.Hat.transform.localPosition = new Vector3(0f, 7f, 0f);
            var clipA = new AnimationClip { name = "A" };
            AnimationUtility.SetEditorCurve(clipA, EditorCurveBinding.FloatCurve("Hat", typeof(Transform), "m_LocalPosition.y"), new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 9f)));
            var clipB = new AnimationClip { name = "B" };
            AnimationUtility.SetEditorCurve(clipB, EditorCurveBinding.FloatCurve("Hat", typeof(Transform), "m_LocalPosition.y"), new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 4f)));
            try
            {
                var menu = avatar.AddMenu();
                var casual = menu.AddEntry(EntryKind.Toggle, "Casual");
                casual.objects.Add(new ObjectTarget { target = avatar.Glasses });
                casual.exclusiveGroup = "Outfits";
                casual.animations.Add(new AnimationTarget { clip = clipA });
                var suit = menu.AddEntry(EntryKind.Toggle, "Suit");
                suit.objects.Add(new ObjectTarget { target = avatar.Glasses });
                suit.exclusiveGroup = "Outfits";
                suit.animations.Add(new AnimationTarget { clip = clipB });

                var fx = BuildAnimator(out var errors, menu);

                Assert.That(errors.Any(e => e.TheError is SimpleError s && (s.TitleKey == "ad.warn.animation_binding_missing" || s.TitleKey == "ad.warn.animation_binding_missing.one")), Is.False);
                var off = State(Layer(fx, "AD: Outfits"), "Off");
                var restored = AnimationUtility.GetEditorCurve((AnimationClip)off.motion, EditorCurveBinding.FloatCurve("Hat", typeof(Transform), "m_LocalPosition.y"));
                Assert.That(restored, Is.Not.Null);
                Assert.That(restored.Evaluate(0f), Is.EqualTo(7f).Within(0.001f));
                Assert.That(AnimationUtility.GetCurveBindings((AnimationClip)off.motion).Count(b => b.path == "Hat" && b.propertyName == "m_LocalPosition.y"), Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(clipA);
                Object.DestroyImmediate(clipB);
            }
        }

        [Test]
        public void GroupMemberGetsItsOwnMergedClipAndGroupOffRestoresAllMembers()
        {
            avatar.Hat.transform.localPosition = new Vector3(0f, 3f, 0f);
            var clip = new AnimationClip { name = "A" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Hat", typeof(Transform), "m_LocalPosition.y"), new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 9f)));
            try
            {
                var menu = avatar.AddMenu();
                var casual = menu.AddEntry(EntryKind.Toggle, "Casual");
                casual.objects.Add(new ObjectTarget { target = avatar.Glasses });
                casual.exclusiveGroup = "Outfits";
                casual.animations.Add(new AnimationTarget { clip = clip });
                var suit = menu.AddEntry(EntryKind.Toggle, "Suit");
                suit.objects.Add(new ObjectTarget { target = avatar.Glasses });
                suit.exclusiveGroup = "Outfits";

                var fx = BuildAnimator(out var errors, menu);

                Assert.That(errors, Is.Empty);
                var layer = Layer(fx, "AD: Outfits");
                var casualState = layer.stateMachine.states.Single(s => s.state.name == "Casual").state;
                var merged = AnimationUtility.GetEditorCurve((AnimationClip)casualState.motion, EditorCurveBinding.FloatCurve("Hat", typeof(Transform), "m_LocalPosition.y"));
                Assert.That(merged, Is.Not.Null);
                Assert.That(merged.Evaluate(1f), Is.EqualTo(9f).Within(0.001f));

                var off = layer.stateMachine.states.Single(s => s.state.name == "Off").state;
                var restored = AnimationUtility.GetEditorCurve((AnimationClip)off.motion, EditorCurveBinding.FloatCurve("Hat", typeof(Transform), "m_LocalPosition.y"));
                Assert.That(restored, Is.Not.Null);
                Assert.That(restored.Evaluate(0f), Is.EqualTo(3f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }
    }
}
