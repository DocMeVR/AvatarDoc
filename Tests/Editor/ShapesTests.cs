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
    public class ShapesTests
    {
        TestAvatar avatar;
        GameObject piece;
        SkinnedMeshRenderer pieceMesh;
        Mesh pieceMeshAsset;

        [SetUp]
        public void SetUp()
        {
            GeneratedAssets.Note();
            avatar = TestAvatar.Create();
            Platform.OverrideMobile = false;
            piece = new GameObject("Jacket");
            piece.transform.SetParent(avatar.Root.transform);
            pieceMesh = piece.AddComponent<SkinnedMeshRenderer>();
            pieceMeshAsset = TestAvatar.QuadWithShapes("Blush", "Chest");
            pieceMesh.sharedMesh = pieceMeshAsset;
        }

        [TearDown]
        public void TearDown()
        {
            Platform.OverrideMobile = null;
            avatar.Dispose();
            if (pieceMeshAsset != null) Object.DestroyImmediate(pieceMeshAsset);
            GeneratedAssets.CleanUp();
        }

        AvatarDocShapes AddShapes(GameObject on = null) => (on ?? piece).AddComponent<AvatarDocShapes>();

        static ShapeWhileOn WhileOn(AvatarDocShapes shapes, SkinnedMeshRenderer mesh, string shape, float value)
        {
            var row = new ShapeWhileOn { mesh = mesh, shape = shape, value = value };
            shapes.whileOn.Add(row);
            return row;
        }

        static ShapeFollow Follow(AvatarDocShapes shapes, SkinnedMeshRenderer source, string shape, string local = "")
        {
            var row = new ShapeFollow { sourceMesh = source, sourceShape = shape, localShape = local };
            shapes.follow.Add(row);
            return row;
        }

        AnimatorController Fx() => (AnimatorController)avatar.Descriptor.baseAnimationLayers
            .Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;

        static AnimationCurve ShapeCurve(Motion motion, string path, string shape)
            => AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + shape));

        static AnimationCurve ShapeCurve(AnimationClip clip, string path, string shape)
            => AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + shape));

        static AnimatorState State(AnimatorController fx, string layerName, string stateName)
            => fx.layers.Single(l => l.name == layerName).stateMachine.states.Single(s => s.state.name == stateName).state;

        static AnimationClip ActiveClip(string name, string path, float from, float to)
        {
            var clip = new AnimationClip { name = name };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive"),
                new AnimationCurve(new Keyframe(0f, from), new Keyframe(1f, to)));
            return clip;
        }

        /// <summary>Every curve of a clip as text, bindings and key values alike, so a changed value counts as a change.</summary>
        static List<string> Curves(AnimationClip clip)
        {
            return AnimationUtility.GetCurveBindings(clip)
                .Select(b => b.path + ":" + b.propertyName + "=" + string.Join(",",
                    AnimationUtility.GetEditorCurve(clip, b).keys.Select(k => k.time + "/" + k.value)))
                .ToList();
        }

        static bool Said(List<ErrorContext> errors, string key)
            => errors.Any(e => e.TheError is SimpleError s && s.TitleKey == key);

        /// <summary>Runs the shapes pass alone, the way the plugin runs it after Modular Avatar, and commits the controllers back.</summary>
        List<ErrorContext> RunShapes()
        {
            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var errors = ErrorReport.CaptureErrors(() => ShapesBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            return errors;
        }

        /// <summary>Runs AvatarDoc's own build and then the shapes pass in one context, as the plugin does in two sequences.</summary>
        List<ErrorContext> RunBuildAndShapes()
        {
            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var errors = ErrorReport.CaptureErrors(() =>
            {
                AvatarDoctorBuilder.Run(context);
                ShapesBuilder.Run(context);
            });
            context.DeactivateAllExtensionContexts();
            return errors;
        }

        MenuEntry PieceToggle(AvatarDoc menu, GameObject target = null, string name = "Jacket")
        {
            var entry = menu.AddEntry(EntryKind.Toggle, name);
            entry.objects.Add(new ObjectTarget { target = target ?? piece, activeWhenOn = true });
            return entry;
        }

        [Test]
        public void FollowCopiesTheSourceCurveOntoThePiece()
        {
            var menu = avatar.AddMenu();
            var blush = menu.AddEntry(EntryKind.Toggle, "Blush");
            blush.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 80f });
            var shapes = AddShapes();
            Follow(shapes, avatar.Body, "Blush");

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                var fx = (AnimatorController)processed.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers
                    .Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
                var on = ShapeCurve(State(fx, "AD: Blush", "On").motion, "Jacket", "Blush");
                Assert.That(on, Is.Not.Null, "the on clip carries the piece's own shape");
                Assert.That(on.Evaluate(0f), Is.EqualTo(80f));
                var off = ShapeCurve(State(fx, "AD: Blush", "Off").motion, "Jacket", "Blush");
                Assert.That(off, Is.Not.Null);
                Assert.That(off.Evaluate(0f), Is.EqualTo(0f));
                Assert.That(processed.GetComponentsInChildren<AvatarDocShapes>(true), Is.Empty, "the component is gone from the clone");
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                GeneratedAssets.CleanUp();
            }
        }

        [Test]
        public void FollowSetsTheStartWeightOnTheClone()
        {
            avatar.Body.SetBlendShapeWeight(0, 45f);
            var shapes = AddShapes();
            Follow(shapes, avatar.Body, "Blush");

            RunShapes();

            Assert.That(pieceMesh.GetBlendShapeWeight(0), Is.EqualTo(45f));
        }

        [Test]
        public void FollowKeepsAClipsOwnCurve()
        {
            var clip = TestAvatar.ShapeClip("Both", "Body", "Blush", 0f, 100f);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Jacket", typeof(SkinnedMeshRenderer), "blendShape.Blush"),
                new AnimationCurve(new Keyframe(0f, 25f), new Keyframe(1f, 25f)));
            avatar.AddFxClip("Both", clip);
            var shapes = AddShapes();
            Follow(shapes, avatar.Body, "Blush");

            var errors = RunShapes();

            var kept = ShapeCurve(State(Fx(), "Base", "Both").motion, "Jacket", "Blush");
            Assert.That(kept.Evaluate(1f), Is.EqualTo(25f), "the clip's own curve stays");
            Assert.That(Said(errors, "ad.info.shapes_follow_kept_own"), Is.True);
        }

        [Test]
        public void WhileOnAddsTheValueToTheClipThatTurnsThePieceOn()
        {
            var menu = avatar.AddMenu();
            PieceToggle(menu);
            var shapes = AddShapes();
            WhileOn(shapes, avatar.Body, "Eye_Big", 100f);

            RunBuildAndShapes();

            var fx = Fx();
            var on = ShapeCurve(State(fx, "AD: Jacket", "On").motion, "Body", "Eye_Big");
            Assert.That(on, Is.Not.Null, "the clip that turns the piece on carries the value");
            Assert.That(on.Evaluate(0f), Is.EqualTo(100f));
            var off = ShapeCurve(State(fx, "AD: Jacket", "Off").motion, "Body", "Eye_Big");
            Assert.That(off, Is.Not.Null, "the clip that turns it off carries the rest value");
            Assert.That(off.Evaluate(0f), Is.EqualTo(0f));
        }

        [Test]
        public void WhileOnStartsAtTheValueWhenThePieceIsOnInTheScene()
        {
            var shapes = AddShapes();
            WhileOn(shapes, avatar.Body, "Eye_Big", 70f);

            var errors = RunShapes();

            Assert.That(avatar.Body.GetBlendShapeWeight(1), Is.EqualTo(70f));
            Assert.That(Said(errors, "ad.info.shapes_always_on"), Is.True, "nothing ever switches this piece, so the value simply applies");
        }

        [Test]
        public void WhileOnFollowsAnAncestorToggle()
        {
            var holder = new GameObject("Outfit");
            holder.transform.SetParent(avatar.Root.transform);
            piece.transform.SetParent(holder.transform);
            var menu = avatar.AddMenu();
            PieceToggle(menu, holder, "Outfit");
            var shapes = AddShapes();
            WhileOn(shapes, avatar.Body, "Eye_Big", 100f);

            RunBuildAndShapes();

            var fx = Fx();
            Assert.That(ShapeCurve(State(fx, "AD: Outfit", "On").motion, "Body", "Eye_Big").Evaluate(0f), Is.EqualTo(100f));
            Assert.That(ShapeCurve(State(fx, "AD: Outfit", "Off").motion, "Body", "Eye_Big").Evaluate(0f), Is.EqualTo(0f));
        }

        [Test]
        public void NonConstantActiveCurveIsSkippedWithAWarning()
        {
            avatar.AddFxClip("Fade", ActiveClip("Fade", "Jacket", 0f, 1f));
            var shapes = AddShapes();
            WhileOn(shapes, avatar.Body, "Eye_Big", 100f);

            var errors = RunShapes();

            Assert.That(ShapeCurve(State(Fx(), "Base", "Fade").motion, "Body", "Eye_Big"), Is.Null, "a curve that is not constant is left alone");
            Assert.That(Said(errors, "ad.warn.shapes_active_not_constant"), Is.True);
        }

        [Test]
        public void ClipThatOwnsTheShapeKeepsItsCurve()
        {
            var clip = ActiveClip("Wear", "Jacket", 1f, 1f);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"),
                new AnimationCurve(new Keyframe(0f, 20f), new Keyframe(1f, 20f)));
            avatar.AddFxClip("Wear", clip);
            var shapes = AddShapes();
            WhileOn(shapes, avatar.Body, "Eye_Big", 100f);

            var errors = RunShapes();

            Assert.That(ShapeCurve(State(Fx(), "Base", "Wear").motion, "Body", "Eye_Big").Evaluate(0f), Is.EqualTo(20f));
            Assert.That(Said(errors, "ad.warn.shapes_clip_owns_shape"), Is.True);
        }

        [Test]
        public void OneAnimationTurningTwoPiecesOnHoldsTheLargerValue()
        {
            var second = new GameObject("Skirt");
            second.transform.SetParent(avatar.Root.transform);
            var clip = ActiveClip("Wear", "Jacket", 1f, 1f);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Skirt", typeof(GameObject), "m_IsActive"),
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f)));
            avatar.AddFxClip("Wear", clip);
            var jacketShapes = AddShapes();
            WhileOn(jacketShapes, avatar.Body, "Eye_Big", 40f);
            WhileOn(AddShapes(second), avatar.Body, "Eye_Big", 90f);
            var inspector = ShapesModel.PlanFor(jacketShapes).Messages;

            var errors = RunShapes();

            Assert.That(ShapeCurve(State(Fx(), "Base", "Wear").motion, "Body", "Eye_Big").Evaluate(0f), Is.EqualTo(90f));
            Assert.That(Said(errors, "ad.info.shapes_larger_wins"), Is.True, "one animation turns both on, so the larger value really wins, and a note says so");
            Assert.That(Said(errors, "ad.warn.shapes_conflict"), Is.False, "the two pieces never fight: one clip switches both");
            Assert.That(inspector.Select(m => m.Key), Does.Contain("ad.info.shapes_larger_wins").And.Not.Contain("ad.warn.shapes_conflict"), "the inspector says the same before a build");
        }

        /// <summary>
        /// Two pieces on separate toggles hold one body shape. Each toggle's layer writes the piece's value in its On
        /// state and the rest value in its Off state, and with Write Defaults off the later FX layer decides the shape.
        /// The preview follows the same rule, and the warning names the piece that wins.
        /// </summary>
        [Test]
        public void TwoPiecesOnSeparateTogglesShowTheLaterLayerNotTheLargerValue()
        {
            var socks = new GameObject("Socks");
            socks.transform.SetParent(avatar.Root.transform);
            var menu = avatar.AddMenu();
            var jacket = PieceToggle(menu);
            var socksToggle = PieceToggle(menu, socks, "Socks");
            var jacketShapes = AddShapes();
            WhileOn(jacketShapes, avatar.Body, "Eye_Big", 100f);
            WhileOn(AddShapes(socks), avatar.Body, "Eye_Big", 60f);
            int eyeBig = avatar.Body.sharedMesh.GetBlendShapeIndex("Eye_Big");

            float Previewed(bool jacketOn, bool socksOn)
            {
                var shapes = PreviewModel.Compute(avatar.Root.transform, new[] { menu },
                    new Dictionary<string, float> { { jacket.id, jacketOn ? 1f : 0f }, { socksToggle.id, socksOn ? 1f : 0f } }, SceneReader.Direct);
                return shapes.TryGetValue(avatar.Body, out var body) && body.Shapes.TryGetValue(eyeBig, out var weight) ? weight : avatar.Body.GetBlendShapeWeight(eyeBig);
            }
            var preview = new[] { Previewed(true, false), Previewed(false, true), Previewed(true, true) };
            var inspector = ShapesModel.PlanFor(jacketShapes).Messages.Single(m => m.Key == "ad.warn.shapes_conflict");

            var errors = RunBuildAndShapes();

            Assert.That(errors.Where(e => e.TheError.Severity == nadena.dev.ndmf.ErrorSeverity.Error), Is.Empty);
            var fx = Fx();
            var layers = fx.layers.Select(l => l.name).ToList();
            int jacketLayer = layers.FindIndex(n => n.Contains("Jacket"));
            int socksLayer = layers.FindIndex(n => n.Contains("Socks"));
            Assert.That(jacketLayer, Is.GreaterThanOrEqualTo(0));
            Assert.That(socksLayer, Is.GreaterThan(jacketLayer), "the Socks toggle comes later in the menu, so its layer comes later in FX");
            float Value(int layer, string state) => ShapeCurve(State(fx, layers[layer], state).motion, "Body", "Eye_Big").Evaluate(0f);
            Assert.That((Value(jacketLayer, "On"), Value(jacketLayer, "Off")), Is.EqualTo((100f, 0f)), "the jacket's layer: its value while on, the rest value while off");
            Assert.That((Value(socksLayer, "On"), Value(socksLayer, "Off")), Is.EqualTo((60f, 0f)), "the socks' layer the same");

            float Built(bool jacketOn, bool socksOn) => Value(socksLayer, socksOn ? "On" : "Off");
            Assert.That((preview[0], Built(true, false)), Is.EqualTo((0f, 0f)), "jacket on, socks off: the socks' Off state puts the shape back, in the preview as in the build");
            Assert.That((preview[1], Built(false, true)), Is.EqualTo((60f, 60f)), "socks on, jacket off: the socks' value");
            Assert.That((preview[2], Built(true, true)), Is.EqualTo((60f, 60f)), "both on: the later layer's value, not the larger one");

            Assert.That(Said(errors, "ad.warn.shapes_conflict"), Is.True, "the build warns as the inspector does");
            Assert.That(new[] { inspector.Args[0], inspector.Args[1] }, Is.EqualTo(new object[] { "Jacket", "Socks" }), "the Socks piece wins: its toggle comes later");
            Assert.That(inspector.Severity, Is.EqualTo(MessageSeverity.Warning));
        }

        [Test]
        public void PiecesOnSeparateTogglesWarnEvenAtTheSameValue()
        {
            var socks = new GameObject("Socks");
            socks.transform.SetParent(avatar.Root.transform);
            var menu = avatar.AddMenu();
            PieceToggle(menu);
            PieceToggle(menu, socks, "Socks");
            var jacketShapes = AddShapes();
            var socksShapes = AddShapes(socks);
            WhileOn(jacketShapes, avatar.Body, "Eye_Big", 100f);
            WhileOn(socksShapes, avatar.Body, "Eye_Big", 100f);

            var inspector = ShapesModel.PlanFor(jacketShapes).Messages.Where(m => m.Key == "ad.warn.shapes_conflict").ToList();
            var errors = RunBuildAndShapes();

            Assert.That(inspector.Count, Is.EqualTo(1), "switching the later toggle off still undoes the other piece's value");
            Assert.That(inspector[0].Args, Has.Member(jacketShapes).And.Member(socksShapes), "both pieces' own inspectors show it");
            Assert.That(Said(errors, "ad.warn.shapes_conflict"), Is.True, "and the build says it too");
        }

        [Test]
        public void PiecesOfOneExclusiveGroupDoNotConflict()
        {
            var socks = new GameObject("Socks");
            socks.transform.SetParent(avatar.Root.transform);
            var menu = avatar.AddMenu();
            PieceToggle(menu).exclusiveGroup = "Outfits";
            PieceToggle(menu, socks, "Socks").exclusiveGroup = "Outfits";
            var jacketShapes = AddShapes();
            WhileOn(jacketShapes, avatar.Body, "Eye_Big", 100f);
            WhileOn(AddShapes(socks), avatar.Body, "Eye_Big", 60f);

            var inspector = ShapesModel.PlanFor(jacketShapes).Messages;
            var errors = RunBuildAndShapes();

            Assert.That(inspector.Any(m => m.Key == "ad.warn.shapes_conflict" || m.Key == "ad.info.shapes_larger_wins"), Is.False,
                "one layer switches both, and only one of them is on at a time");
            Assert.That(Said(errors, "ad.warn.shapes_conflict") || Said(errors, "ad.info.shapes_larger_wins"), Is.False);
        }

        [Test]
        public void APieceThatIsAlwaysOnLosesToAToggledOne()
        {
            var socks = new GameObject("Socks");
            socks.transform.SetParent(avatar.Root.transform);
            var menu = avatar.AddMenu();
            PieceToggle(menu, socks, "Socks");
            var jacketShapes = AddShapes();
            WhileOn(jacketShapes, avatar.Body, "Eye_Big", 100f);
            WhileOn(AddShapes(socks), avatar.Body, "Eye_Big", 60f);

            var conflict = ShapesModel.PlanFor(jacketShapes).Messages.Single(m => m.Key == "ad.warn.shapes_conflict");

            Assert.That(new[] { conflict.Args[0], conflict.Args[1] }, Is.EqualTo(new object[] { "Jacket", "Socks" }),
                "nothing switches the jacket, so its value sits under the Socks toggle, whose Off state puts the shape back");
        }

        /// <summary>The names a shapes conflict gives, loser then winner, in the inspector and in the build's report.</summary>
        static object[] Pair(ModelMessage message) => new[] { message.Args[0], message.Args[1] };

        static object[] Pair(List<ErrorContext> errors)
        {
            var error = errors.Select(e => e.TheError).OfType<SimpleError>().Single(s => s.TitleKey == "ad.warn.shapes_conflict");
            return new object[] { error.TitleSubst[0], error.TitleSubst[1] };
        }

        [Test]
        public void TheWinnerIsThePieceOfTheLaterLayerNotTheLaterObject()
        {
            var socks = new GameObject("Socks");
            socks.transform.SetParent(avatar.Root.transform);
            var menu = avatar.AddMenu();
            PieceToggle(menu);
            var jacketShapes = AddShapes();
            WhileOn(jacketShapes, avatar.Body, "Eye_Big", 100f);
            WhileOn(AddShapes(socks), avatar.Body, "Eye_Big", 60f);

            var inspector = ShapesModel.PlanFor(jacketShapes).Messages.Single(m => m.Key == "ad.warn.shapes_conflict");
            var errors = RunBuildAndShapes();

            Assert.That(Pair(inspector), Is.EqualTo(new object[] { "Socks", "Jacket" }),
                "the Socks come later in the hierarchy, but nothing switches them, so the Jacket toggle's layer decides");
            Assert.That(Pair(errors), Is.EqualTo(new object[] { "Socks", "Jacket" }), "and the build names the same winner");
            Assert.That(L.Text("ad.warn.shapes_conflict"), Does.Contain("one exclusive group"));
            using (L.UseTestLanguage("ja-JP", null)) Assert.That(L.Text("ad.warn.shapes_conflict"), Does.Contain("排他グループ"));
            using (L.UseTestLanguage("zh-Hans", null))
            {
                Assert.That(L.Text("ad.warn.shapes_conflict"), Does.Contain("互斥组"));
                Assert.That(L.Text("ad.warn.shapes_conflict"), Does.Not.Contain("切换"), "切换 is the blend shape trigger's Switch style");
            }
        }

        [Test]
        public void AMergedAnimatorComesAfterTheToggles()
        {
            const string folder = GeneratedAssets.Folder;
            GeneratedAssets.Ensure();
            try
            {
                var socks = new GameObject("Socks");
                socks.transform.SetParent(avatar.Root.transform);
                var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/ShapesSocks.controller");
                var wear = ActiveClip("Wear socks", "Socks", 1f, 1f);
                var bare = ActiveClip("Bare feet", "Socks", 0f, 0f);
                AssetDatabase.AddObjectToAsset(wear, controller);
                AssetDatabase.AddObjectToAsset(bare, controller);
                controller.AddMotion(bare);
                controller.AddMotion(wear);
                AssetDatabase.SaveAssets();
                var menu = avatar.AddMenu();
                menu.animators.Add(AnimatorMerge.Create(controller));
                PieceToggle(menu);
                var jacketShapes = AddShapes();
                WhileOn(jacketShapes, avatar.Body, "Eye_Big", 100f);
                WhileOn(AddShapes(socks), avatar.Body, "Eye_Big", 60f);

                var inspector = ShapesModel.PlanFor(jacketShapes).Messages.Single(m => m.Key == "ad.warn.shapes_conflict");
                var errors = RunBuildAndShapes();

                Assert.That(Pair(inspector), Is.EqualTo(new object[] { "Jacket", "Socks" }), "merged animators join FX after every toggle, so the merged Socks layer decides");
                Assert.That(Pair(errors), Is.EqualTo(new object[] { "Jacket", "Socks" }), "as in the build");
            }
            finally
            {
                GeneratedAssets.CleanUp();
            }
        }

        [Test]
        public void ABlendShapeTriggerComesAfterEveryOtherLayer()
        {
            var socks = new GameObject("Socks");
            socks.transform.SetParent(avatar.Root.transform);
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var menu = avatar.AddMenu();
            var link = menu.AddEntry(EntryKind.Trigger, "Blushing jacket");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            link.objects.Add(new ObjectTarget { target = piece, activeWhenOn = true });
            var thumbs = menu.AddEntry(EntryKind.Trigger, "Thumbs up socks");
            thumbs.source = TriggerSource.Parameter;
            thumbs.sourceParameter = "GestureRight";
            thumbs.condition = ParameterCondition.Equals;
            thumbs.conditionValue = 7f;
            thumbs.objects.Add(new ObjectTarget { target = socks, activeWhenOn = true });
            var jacketShapes = AddShapes();
            WhileOn(jacketShapes, avatar.Body, "Eye_Big", 100f);
            WhileOn(AddShapes(socks), avatar.Body, "Eye_Big", 60f);

            var inspector = ShapesModel.PlanFor(jacketShapes).Messages.Single(m => m.Key == "ad.warn.shapes_conflict");
            var errors = RunBuildAndShapes();

            Assert.That(Pair(inspector), Is.EqualTo(new object[] { "Socks", "Jacket" }),
                "the blend shape trigger comes first in the menu, but its layer goes after every other one, so it decides");
            Assert.That(Pair(errors), Is.EqualTo(new object[] { "Socks", "Jacket" }), "as in the build");
        }

        [Test]
        public void TwoPiecesThatStayOnHoldTheLargerValue()
        {
            var socks = new GameObject("Socks");
            socks.transform.SetParent(avatar.Root.transform);
            var jacketShapes = AddShapes();
            WhileOn(jacketShapes, avatar.Body, "Eye_Big", 40f);
            WhileOn(AddShapes(socks), avatar.Body, "Eye_Big", 90f);

            var note = ShapesModel.PlanFor(jacketShapes).Messages.Single(m => m.Key == "ad.info.shapes_larger_wins");
            var errors = RunShapes();

            Assert.That(note.Args[3], Is.EqualTo("90"));
            Assert.That(Said(errors, "ad.info.shapes_larger_wins"), Is.True);
            Assert.That(avatar.Body.GetBlendShapeWeight(avatar.Body.sharedMesh.GetBlendShapeIndex("Eye_Big")), Is.EqualTo(90f), "nothing switches either, so the larger value is set once on the avatar");
            Assert.That(L.Text("ad.info.shapes_larger_wins"), Does.Contain("both stay on"), "the note covers pieces that stay on, not only ones an animation turns on");
        }

        [Test]
        public void MergedAnimatorClipsAreCovered()
        {
            const string folder = GeneratedAssets.Folder;
            GeneratedAssets.Ensure();
            try
            {
                var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/ShapesMerge.controller");
                var clip = ActiveClip("Wear", "Jacket", 1f, 1f);
                AssetDatabase.AddObjectToAsset(clip, controller);
                controller.AddMotion(clip);
                AssetDatabase.SaveAssets();
                var menu = avatar.AddMenu();
                menu.animators.Add(AnimatorMerge.Create(controller));
                var shapes = AddShapes();
                WhileOn(shapes, avatar.Body, "Eye_Big", 100f);

                RunBuildAndShapes();

                var covered = Fx().animationClips.Where(c => ShapeCurve(c, "Body", "Eye_Big") != null).ToList();
                Assert.That(covered, Is.Not.Empty, "the merged clip that turns the piece on carries the value");
                Assert.That(covered.Any(c => ShapeCurve(c, "Body", "Eye_Big").Evaluate(0f) == 100f), Is.True);
            }
            finally
            {
                GeneratedAssets.CleanUp();
            }
        }

        [Test]
        public void TheSourceAssetsAreNeverModified()
        {
            const string folder = GeneratedAssets.Folder;
            GeneratedAssets.Ensure();
            try
            {
                var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/ShapesUntouched.controller");
                var clip = ActiveClip("Wear", "Jacket", 1f, 1f);
                AssetDatabase.AddObjectToAsset(clip, controller);
                controller.AddMotion(clip);
                AssetDatabase.SaveAssets();
                var before = Curves(clip);
                var menu = avatar.AddMenu();
                menu.animators.Add(AnimatorMerge.Create(controller));
                var shapes = AddShapes();
                WhileOn(shapes, avatar.Body, "Eye_Big", 100f);

                RunBuildAndShapes();

                Assert.That(Curves(clip), Is.EqualTo(before), "the clip asset keeps its bindings and their values");
            }
            finally
            {
                GeneratedAssets.CleanUp();
            }
        }

        [Test]
        public void ExcludedPlatformDoesNothing()
        {
            avatar.AddFxClip("Wear", ActiveClip("Wear", "Jacket", 1f, 1f));
            var shapes = AddShapes();
            shapes.platform = EntryPlatform.MobileOnly;
            WhileOn(shapes, avatar.Body, "Eye_Big", 100f);

            RunShapes();

            Assert.That(ShapeCurve(State(Fx(), "Base", "Wear").motion, "Body", "Eye_Big"), Is.Null);
            Assert.That(avatar.Body.GetBlendShapeWeight(1), Is.EqualTo(0f));
        }

        [Test]
        public void ShapesAddNoParameterAndNoLayer()
        {
            var menu = avatar.AddMenu();
            PieceToggle(menu);
            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            var layersBefore = Fx().layers.Select(l => l.name).ToList();
            var parametersBefore = avatar.Descriptor.expressionParameters.parameters.Select(p => p.name).ToList();
            var animatorParametersBefore = Fx().parameters.Select(p => p.name).ToList();

            try
            {
                foreach (bool mobile in new[] { false, true })
                {
                    Platform.OverrideMobile = mobile;
                    string on = mobile ? " on Quest" : " on PC";
                    var shapes = AddShapes();
                    WhileOn(shapes, avatar.Body, "Eye_Big", 100f);
                    RunShapes();

                    Assert.That(Fx().layers.Select(l => l.name).ToList(), Is.EqualTo(layersBefore), "no layer is added" + on);
                    Assert.That(avatar.Descriptor.expressionParameters.parameters.Select(p => p.name).ToList(), Is.EqualTo(parametersBefore),
                        "the expression parameters are the same with the component as without it" + on);
                    Assert.That(Fx().parameters.Select(p => p.name).ToList(), Is.EqualTo(animatorParametersBefore),
                        "and so are the animator's own" + on);
                }
            }
            finally
            {
                Platform.OverrideMobile = false;
            }
        }

        [Test]
        public void ValidationNamesMissingShapesAndOutsideMeshes()
        {
            var outside = new GameObject("Outside");
            var outsideMesh = outside.AddComponent<SkinnedMeshRenderer>();
            outsideMesh.sharedMesh = pieceMeshAsset;
            var shapes = AddShapes();
            WhileOn(shapes, outsideMesh, "Blush", 100f);
            WhileOn(shapes, avatar.Body, "NotAShape", 100f);
            var bare = AddShapes(avatar.Glasses);
            Follow(bare, avatar.Body, "Blush");

            try
            {
                var plan = ShapesModel.Plan(avatar.Root.transform);
                var keys = plan.Messages.Select(m => m.Key).ToList();
                Assert.That(keys, Does.Contain("ad.error.shapes_outside_avatar"));
                Assert.That(keys, Does.Contain("ad.warn.shapes_missing_shape"));
                Assert.That(keys, Does.Contain("ad.error.shapes_no_mesh"));
            }
            finally
            {
                Object.DestroyImmediate(outside);
            }
        }

        [Test]
        public void PreviewShowsWhileOnValuesForAPreviewedToggle()
        {
            var menu = avatar.AddMenu();
            var toggle = PieceToggle(menu);
            var shapes = AddShapes();
            WhileOn(shapes, avatar.Body, "Eye_Big", 100f);

            var on = PreviewModel.Compute(avatar.Root.transform, new[] { menu },
                new Dictionary<string, float> { { toggle.id, 1f } }, SceneReader.Direct);
            var off = PreviewModel.Compute(avatar.Root.transform, new[] { menu },
                new Dictionary<string, float> { { toggle.id, 0f } }, SceneReader.Direct);

            Assert.That(on[avatar.Body].Shapes[1], Is.EqualTo(100f), "the body shows the piece's value while the toggle is on");
            Assert.That(off.ContainsKey(avatar.Body) && off[avatar.Body].Shapes.ContainsKey(1), Is.False, "and rests while it is off");
        }

        [Test]
        public void AddMatchingShapesAddsEachSharedShapeOnce()
        {
            var shapes = AddShapes();

            int first = AvatarDocShapesEditor.AddMatchingShapes(shapes, avatar.Body);
            int second = AvatarDocShapesEditor.AddMatchingShapes(shapes, avatar.Body);

            Assert.That(first, Is.EqualTo(1), "only the name both meshes have");
            Assert.That(second, Is.EqualTo(0), "a row that exists is not added twice");
            var row = shapes.follow.Single();
            Assert.That(row.sourceMesh, Is.EqualTo(avatar.Body));
            Assert.That(row.sourceShape, Is.EqualTo("Blush"));
        }

        [Test]
        public void RestValueNeverOverridesAPieceTheClipTurnsOn()
        {
            var skirt = new GameObject("Skirt");
            skirt.transform.SetParent(avatar.Root.transform);
            avatar.Body.SetBlendShapeWeight(1, 80f);
            var clip = ActiveClip("Outfit", "Jacket", 1f, 1f);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Skirt", typeof(GameObject), "m_IsActive"),
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 0f)));
            avatar.AddFxClip("Outfit", clip);
            WhileOn(AddShapes(), avatar.Body, "Eye_Big", 30f);
            WhileOn(AddShapes(skirt), avatar.Body, "Eye_Big", 60f);

            RunShapes();

            Assert.That(ShapeCurve(State(Fx(), "Base", "Outfit").motion, "Body", "Eye_Big").Evaluate(0f), Is.EqualTo(30f),
                "the piece this clip really turns on decides, not the rest value of the one it turns off");
        }

        [Test]
        public void GestureClipsAreLeftAlone()
        {
            var gesture = new AnimatorController { name = "Gesture" };
            gesture.AddLayer("Base");
            var state = gesture.layers[0].stateMachine.AddState("Wave");
            state.motion = ActiveClip("Wave", "Jacket", 1f, 1f);
            state.writeDefaultValues = false;
            var layers = avatar.Descriptor.baseAnimationLayers;
            for (int i = 0; i < layers.Length; i++)
                if (layers[i].type == VRCAvatarDescriptor.AnimLayerType.Gesture)
                {
                    layers[i].isDefault = false;
                    layers[i].animatorController = gesture;
                }
            avatar.Descriptor.baseAnimationLayers = layers;
            var shapes = AddShapes();
            WhileOn(shapes, avatar.Body, "Eye_Big", 100f);

            try
            {
                RunShapes();

                var built = (AnimatorController)avatar.Descriptor.baseAnimationLayers
                    .Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.Gesture).animatorController;
                foreach (var clip in built.animationClips)
                    Assert.That(ShapeCurve(clip, "Body", "Eye_Big"), Is.Null, "only the FX controller carries these shapes");
            }
            finally
            {
                Object.DestroyImmediate(gesture);
            }
        }

        [Test]
        public void FollowDoesNotMistakeThisPassesCurvesForTheClipsOwn()
        {
            var skirt = new GameObject("Skirt");
            skirt.transform.SetParent(avatar.Root.transform);
            var clip = ActiveClip("Wear", "Jacket", 1f, 1f);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Skirt", typeof(GameObject), "m_IsActive"),
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f)));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"),
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 100f)));
            avatar.AddFxClip("Wear", clip);
            Follow(AddShapes(), avatar.Body, "Blush");
            WhileOn(AddShapes(skirt), pieceMesh, "Blush", 50f);

            var errors = RunShapes();

            Assert.That(ShapeCurve(State(Fx(), "Base", "Wear").motion, "Jacket", "Blush").Evaluate(1f), Is.EqualTo(50f),
                "the while-on value wins over the follow");
            Assert.That(Said(errors, "ad.info.shapes_follow_kept_own"), Is.False, "and the clip is not blamed for a curve this pass wrote");
        }

        [Test]
        public void PieceThatIsNeverOnIsReported()
        {
            piece.SetActive(false);
            var shapes = AddShapes();
            WhileOn(shapes, avatar.Body, "Eye_Big", 100f);

            var errors = RunShapes();

            Assert.That(avatar.Body.GetBlendShapeWeight(1), Is.EqualTo(0f), "nothing is applied");
            Assert.That(Said(errors, "ad.info.shapes_never_on"), Is.True);
        }

        [Test]
        public void AddMatchingShapesSkipsALocalShapeThatIsTaken()
        {
            var other = new GameObject("Other");
            other.transform.SetParent(avatar.Root.transform);
            var otherMesh = other.AddComponent<SkinnedMeshRenderer>();
            var otherMeshAsset = TestAvatar.QuadWithShapes("Blush");
            otherMesh.sharedMesh = otherMeshAsset;
            var shapes = AddShapes();

            try
            {
                int first = AvatarDocShapesEditor.AddMatchingShapes(shapes, avatar.Body);
                int second = AvatarDocShapesEditor.AddMatchingShapes(shapes, otherMesh);

                Assert.That(first, Is.EqualTo(1));
                Assert.That(second, Is.EqualTo(0), "another mesh with the same name would write a shape that is already taken");
                Assert.That(shapes.follow.Count, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(otherMeshAsset);
            }
        }

#if DOCME_MA
        [Test]
        public void BlendshapeSyncConvertsToFollowRows()
        {
            var sync = piece.AddComponent<ModularAvatarBlendshapeSync>();
            var reference = new AvatarObjectReference();
            reference.Set(avatar.Body.gameObject);
            sync.Bindings = new List<BlendshapeBinding>
            {
                new BlendshapeBinding { ReferenceMesh = reference, Blendshape = "Blush", LocalBlendshape = "Chest" },
            };

            var candidate = ShapesImport.Scan(avatar.Root.transform).Single();

            Assert.That(candidate.Supported, Is.True);
            Assert.That(candidate.ShapesDraft.Piece, Is.EqualTo(piece));
            var row = candidate.ShapesDraft.Follow.Single();
            Assert.That(row.sourceMesh, Is.EqualTo(avatar.Body));
            Assert.That(row.sourceShape, Is.EqualTo("Blush"));
            Assert.That(row.localShape, Is.EqualTo("Chest"));
            Assert.That(candidate.OriginComponents, Does.Contain(sync));
        }

        [Test]
        public void SetShapeChangerConvertsToWhileOnRows()
        {
            var changer = piece.AddComponent<ModularAvatarShapeChanger>();
            var reference = new AvatarObjectReference();
            reference.Set(avatar.Body.gameObject);
            changer.Shapes = new List<ChangedShape>
            {
                new ChangedShape { Object = reference, ShapeName = "Eye_Big", ChangeType = ShapeChangeType.Set, Value = 100f },
            };

            var candidate = ShapesImport.Scan(avatar.Root.transform).Single();

            Assert.That(candidate.Supported, Is.True);
            var row = candidate.ShapesDraft.WhileOn.Single();
            Assert.That(row.mesh, Is.EqualTo(avatar.Body));
            Assert.That(row.shape, Is.EqualTo("Eye_Big"));
            Assert.That(row.value, Is.EqualTo(100f));

            var deleteRef = new AvatarObjectReference();
            deleteRef.Set(avatar.Body.gameObject);
            changer.Shapes.Add(new ChangedShape { Object = deleteRef, ShapeName = "Blush", ChangeType = ShapeChangeType.Delete });
            var refused = ShapesImport.Scan(avatar.Root.transform).Single();
            Assert.That(refused.Supported, Is.False, "a Delete shape is not something AvatarDoc Shapes can do");
            Assert.That(refused.Reason, Is.Not.Empty);
        }
#endif
    }
}
#endif
