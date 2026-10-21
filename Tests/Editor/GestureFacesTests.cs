#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Gesture faces: a face for each VRChat hand gesture, made as ordinary parameter triggers that pin the face's
    /// shapes. Built with both of AvatarDoc's passes and played back in edit mode, the way the avatar runs them.
    /// </summary>
    public class GestureFacesTests
    {
        TestAvatar avatar;
        AvatarDoc menu;
        readonly List<Object> made = new List<Object>();
        Action<VisualElement, List<AdMenu.Item>> previousShowMenu;
        Action<AvatarDoc> previousOpen;
        List<AdMenu.Item> shownMenu;

        const float Frame = 1f / 60f;
        const int Victory = 4;

        [SetUp]
        public void SetUp()
        {
            GeneratedAssets.Note();
            avatar = TestAvatar.Create();
            menu = avatar.AddMenu();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
            previousShowMenu = GestureFacesWindow.ShowMenu;
            GestureFacesWindow.ShowMenu = (anchor, items) => shownMenu = items;
            previousOpen = GestureFacesWindow.Open;
            shownMenu = null;
        }

        [TearDown]
        public void TearDown()
        {
            GestureFacesWindow.ShowMenu = previousShowMenu;
            GestureFacesWindow.Open = previousOpen;
            foreach (var obj in made)
                if (obj != null) Undo.ClearUndo(obj);
            Undo.ClearUndo(menu);
            avatar.Dispose();
            foreach (var obj in made)
                if (obj != null && !AssetDatabase.Contains(obj)) Object.DestroyImmediate(obj);
            made.Clear();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
            GeneratedAssets.CleanUp();
        }

        static string English(string key)
        {
            Assert.That(LocalizationFileTests.ShippedTexts("en-US").TryGetValue(key, out var text), Is.True, key + " is in the English file");
            return text;
        }

        // ---- the avatar ---------------------------------------------------------------------------------------------

        GestureFaceRow Row(int gesture, GestureHand hand, GestureTracking tracking, params (string shape, float weight)[] shapes)
        {
            var row = new GestureFaceRow { gesture = gesture, hand = hand, tracking = tracking };
            foreach (var (shape, weight) in shapes) row.shapes.Add(new GestureShape { renderer = avatar.Body, shape = shape, weight = weight });
            return row;
        }

        GestureFacesDraft Draft(params GestureFaceRow[] rows)
        {
            var draft = new GestureFacesDraft { component = menu, faceMesh = avatar.Body, menuSwitch = false };
            GestureFaces.SnapshotRest(draft);
            draft.rows.AddRange(rows);
            return draft;
        }

        List<MenuEntry> Apply(GestureFacesDraft draft)
        {
            var entries = GestureFaces.Apply(draft, out var status);
            Assert.That(entries, Is.Not.Empty, status);
            return entries;
        }

        List<MenuEntry> Triggers() => menu.entries.Where(e => e.kind == EntryKind.Trigger).ToList();

        /// <summary>The Bool face tracking templates set while tracking is on, as an expression parameter of the avatar.</summary>
        void TrackingParameter(string name = "LipTrackingActive")
        {
            var parameters = avatar.ExpressionParameters.parameters.ToList();
            parameters.Add(new VRCExpressionParameters.Parameter { name = name, valueType = VRCExpressionParameters.ValueType.Bool, saved = false, networkSynced = true });
            avatar.ExpressionParameters.parameters = parameters.ToArray();
        }

        void FullWeights()
        {
            var layers = avatar.Fx.layers;
            foreach (var layer in layers) layer.defaultWeight = 1f;
            avatar.Fx.layers = layers;
        }

        /// <summary>A face tracking layer the way the templates build one: a Direct tree, Write Defaults on, holding the shape at value, beside a disabled state.</summary>
        void FaceTracking(string shape, float value)
        {
            var parameter = "FT/" + shape;
            avatar.Fx.AddParameter(new AnimatorControllerParameter { name = parameter, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            var clip = TestAvatar.ShapeClip("FT " + shape, "Body", shape, value, value);
            made.Add(clip);
            var tree = new BlendTree { name = "FT " + shape, blendType = BlendTreeType.Direct };
            made.Add(tree);
            tree.children = new[] { new ChildMotion { motion = clip, directBlendParameter = parameter, timeScale = 1f } };
            avatar.Fx.AddLayer("FT " + shape);
            var machine = avatar.Fx.layers[avatar.Fx.layers.Length - 1].stateMachine;
            made.Add(machine);
            var tracking = machine.AddState("Tracking");
            made.Add(tracking);
            tracking.motion = tree;
            tracking.writeDefaultValues = true;
            machine.defaultState = tracking;
            var disabled = machine.AddState("Disabled");
            made.Add(disabled);
            disabled.writeDefaultValues = false;
            FullWeights();
        }

        static AnimatorController FxOf(TestAvatar a)
            => (AnimatorController)a.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;

        static void NoErrors(List<ErrorContext> errors)
            => Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error).Select(e => (e.TheError as SimpleError)?.TitleKey), Is.Empty);

        /// <summary>The whole build the way NDMF runs it: AvatarDoc's own pass, then the final pass after every other tool.</summary>
        AnimatorController BuildAll()
        {
            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            errors.AddRange(ErrorReport.CaptureErrors(() => AvatarDoctorPlugin.FinishAfterOtherTools(context)));
            NoErrors(errors);
            return FxOf(avatar);
        }

        static string Shown(IEnumerable<float> weights) => "(" + string.Join(", ", weights) + ")";

        // ---- played back --------------------------------------------------------------------------------------------

        [TestCase(20f)]
        [TestCase(60f)]
        [TestCase(144f)]
        public void PlayedBackAFaceShowsWhileItsGestureIsHeldAndRestsAfter(float framesPerSecond)
        {
            avatar.Body.SetBlendShapeWeight(0, 5f);
            // The right hand's gesture as the avatar's own FX declares it, so the playback can set it.
            avatar.Fx.AddParameter("GestureRight", AnimatorControllerParameterType.Int);
            Apply(Draft(Row(GestureFaces.Fist, GestureHand.Left, GestureTracking.Wins, ("Blush", 100f))));

            var fx = BuildAll();

            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond))
            {
                Assert.That(player.Settled("Blush").Last(), Is.EqualTo(5f).Within(0.01f), "no gesture: the scene's weight");
                player.Set("GestureLeft", GestureFaces.Fist);
                var held = player.Settled("Blush");
                Assert.That(held.Last(), Is.EqualTo(100f).Within(0.01f), "the left fist shows the face " + Shown(held));
                player.Set("GestureLeft", 2f);
                var other = player.Settled("Blush");
                Assert.That(other.Last(), Is.EqualTo(5f).Within(0.01f), "another gesture without a face lets it go " + Shown(other));
                player.Set("GestureRight", GestureFaces.Fist);
                Assert.That(player.Settled("Blush").Last(), Is.EqualTo(5f).Within(0.01f), "the right hand has no face of its own");
            }
        }

        [TestCase(20f)]
        [TestCase(60f)]
        [TestCase(144f)]
        public void PlayedBackAPausedFaceDoesNothingWhileFaceTrackingIsOnAndShowsWhileItIsOff(float framesPerSecond)
        {
            TrackingParameter();
            FaceTracking("Blush", 40f);
            var draft = Draft(Row(GestureFaces.Fist, GestureHand.Left, GestureTracking.Paused, ("Blush", 100f)));
            draft.trackingParameter = "LipTrackingActive";
            Apply(draft);

            var fx = BuildAll();

            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond))
            {
                player.Set("LipTrackingActive", 1f);
                player.Set("GestureLeft", GestureFaces.Fist);
                var tracked = player.Settled("Blush");
                Assert.That(tracked.Last(), Is.EqualTo(40f).Within(0.01f), "face tracking is on: the fist changes nothing " + Shown(tracked));
                player.Set("LipTrackingActive", 0f);
                var untracked = player.Settled("Blush");
                Assert.That(untracked.Last(), Is.EqualTo(100f).Within(0.01f), "face tracking is off: the face shows " + Shown(untracked));
                player.Set("LipTrackingActive", 1f);
                var back = player.Settled("Blush");
                Assert.That(back.Last(), Is.EqualTo(40f).Within(0.01f), "tracking comes back: it has the face again " + Shown(back));
            }
        }

        [TestCase(20f)]
        [TestCase(60f)]
        [TestCase(144f)]
        public void PlayedBackAWinningFaceOverridesFaceTrackingWhileHeldAndHandsItBackAfter(float framesPerSecond)
        {
            TrackingParameter();
            avatar.Fx.AddParameter("LipTrackingActive", AnimatorControllerParameterType.Bool);
            FaceTracking("Blush", 40f);
            var draft = Draft(Row(GestureFaces.Fist, GestureHand.Left, GestureTracking.Wins, ("Blush", 100f)));
            draft.trackingParameter = "LipTrackingActive";
            Apply(draft);

            var fx = BuildAll();

            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond))
            {
                player.Set("LipTrackingActive", 1f);
                Assert.That(player.Settled("Blush").Last(), Is.EqualTo(40f).Within(0.01f), "no gesture: face tracking's value");
                player.Set("GestureLeft", GestureFaces.Fist);
                var held = player.Settled("Blush");
                Assert.That(held.Last(), Is.EqualTo(100f).Within(0.01f), "held: the face wins over face tracking " + Shown(held));
                player.Set("GestureLeft", 0f);
                var after = player.Settled("Blush");
                Assert.That(after.Last(), Is.EqualTo(40f).Within(0.01f), "let go: face tracking has the shape again " + Shown(after));
            }
        }

        [TestCase(20f)]
        [TestCase(60f)]
        [TestCase(144f)]
        public void PlayedBackAWinningFaceHeldAloneLeavesFaceTrackingFreeOnShapesItDoesNotUse(float framesPerSecond)
        {
            TrackingParameter();
            avatar.Fx.AddParameter("LipTrackingActive", AnimatorControllerParameterType.Bool);
            FaceTracking("Eye_Big", 40f);
            var draft = Draft(
                Row(GestureFaces.Fist, GestureHand.Left, GestureTracking.Wins, ("Blush", 100f)),
                Row(Victory, GestureHand.Right, GestureTracking.Wins, ("Eye_Big", 100f)));
            draft.trackingParameter = "LipTrackingActive";
            Apply(draft);

            var fx = BuildAll();

            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond))
            {
                player.Set("LipTrackingActive", 1f);
                player.Set("GestureLeft", GestureFaces.Fist);
                player.Settled("Blush");
                Assert.That((player.Weight("Blush"), player.Weight("Eye_Big")), Is.EqualTo((100f, 40f)),
                    "the left fist holds its own shape only: face tracking keeps the shape another face uses");
                player.Set("GestureRight", Victory);
                player.Settled("Blush");
                Assert.That((player.Weight("Blush"), player.Weight("Eye_Big")), Is.EqualTo((0f, 100f)), "both hands: the right hand's face, the left face at rest");
                player.Set("GestureRight", 0f);
                player.Settled("Blush");
                Assert.That((player.Weight("Blush"), player.Weight("Eye_Big")), Is.EqualTo((100f, 40f)), "the right hand lets go: face tracking has its shape back");
            }
        }

        [TestCase(GestureOrder.RightWins, 20f)]
        [TestCase(GestureOrder.RightWins, 60f)]
        [TestCase(GestureOrder.RightWins, 144f)]
        [TestCase(GestureOrder.LeftWins, 20f)]
        [TestCase(GestureOrder.LeftWins, 60f)]
        [TestCase(GestureOrder.LeftWins, 144f)]
        public void PlayedBackBothHandsShowTheFaceOfTheHandThatWins(GestureOrder order, float framesPerSecond)
        {
            avatar.Body.SetBlendShapeWeight(0, 5f);
            var draft = Draft(
                Row(GestureFaces.Fist, GestureHand.Left, GestureTracking.Wins, ("Blush", 100f)),
                Row(Victory, GestureHand.Right, GestureTracking.Wins, ("Eye_Big", 100f)));
            draft.order = order;
            Apply(draft);

            var fx = BuildAll();

            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond))
            {
                player.Set("GestureLeft", GestureFaces.Fist);
                player.Settled("Blush");
                Assert.That((player.Weight("Blush"), player.Weight("Eye_Big")), Is.EqualTo((100f, 0f)), "the left fist alone");
                player.Set("GestureRight", Victory);
                player.Settled("Blush");
                var both = (player.Weight("Blush"), player.Weight("Eye_Big"));
                Assert.That(both, Is.EqualTo(order == GestureOrder.RightWins ? (5f, 100f) : (100f, 0f)),
                    order + ": only the winning hand's face shows, the other face's shapes at their rest weight");
                player.Set("GestureLeft", 0f);
                player.Settled("Blush");
                Assert.That((player.Weight("Blush"), player.Weight("Eye_Big")), Is.EqualTo((5f, 100f)), "the right victory alone");
            }
        }

        [TestCase(20f)]
        [TestCase(60f)]
        [TestCase(144f)]
        public void PlayedBackAShrinkKeyAtRestStaysWhileEveryFaceShows(float framesPerSecond)
        {
            avatar.AddBodyShapes("Shrink_Chest");
            int shrink = avatar.Mesh.GetBlendShapeIndex("Shrink_Chest");
            avatar.Body.SetBlendShapeWeight(shrink, 100f);
            var draft = GestureFaces.Read(menu);
            draft.menuSwitch = false;
            var fist = draft.rows.Single(r => r.gesture == GestureFaces.Fist);
            var victory = draft.rows.Single(r => r.gesture == Victory);
            fist.hand = GestureHand.Left;
            victory.hand = GestureHand.Right;
            avatar.Body.SetBlendShapeWeight(0, 70f);
            Assert.That(GestureFaces.Capture(draft, fist), Is.EqualTo(1), "the face's own shape, not the Shrink key at rest");
            Assert.That(GestureFaces.PosedShapes(draft).Select(s => s.shape), Is.EqualTo(new[] { "Blush" }), "only the face's shape is off its rest weight");
            GestureFaces.ResetPosed(draft);
            Assert.That((avatar.Body.GetBlendShapeWeight(0), avatar.Body.GetBlendShapeWeight(shrink)), Is.EqualTo((0f, 100f)), "put back: the Shrink key stays where it rests");
            avatar.Body.SetBlendShapeWeight(1, 80f);
            Assert.That(GestureFaces.Capture(draft, victory), Is.EqualTo(1));
            GestureFaces.ResetPosed(draft);
            Apply(draft);

            var fx = BuildAll();

            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond))
            {
                player.Set("GestureLeft", GestureFaces.Fist);
                player.Settled("Blush");
                Assert.That((player.Weight("Blush"), player.Weight("Shrink_Chest")), Is.EqualTo((70f, 100f)), "the fist's face, the Shrink key untouched");
                player.Set("GestureRight", Victory);
                player.Settled("Blush");
                Assert.That((player.Weight("Blush"), player.Weight("Eye_Big"), player.Weight("Shrink_Chest")), Is.EqualTo((0f, 80f, 100f)),
                    "both hands: the right face, the left face at rest, the Shrink key never at 0");
                player.Set("GestureLeft", 0f);
                player.Set("GestureRight", 0f);
                player.Settled("Blush");
                Assert.That(player.Weight("Shrink_Chest"), Is.EqualTo(100f));
            }
        }

        [TestCase(20f)]
        [TestCase(60f)]
        [TestCase(144f)]
        public void PlayedBackTheMenuSwitchStartsOnAndTurnsEveryFaceOff(float framesPerSecond)
        {
            var draft = Draft(Row(GestureFaces.Fist, GestureHand.Both, GestureTracking.Wins, ("Blush", 100f)));
            draft.menuSwitch = true;
            Apply(draft);
            var toggle = menu.entries.Single(e => e.kind == EntryKind.Toggle);
            Assert.That((toggle.name, toggle.defaultOn, toggle.saved, toggle.synced, toggle.gestureFace), Is.EqualTo((English("ad.ui.gestures.switch_name"), true, true, true, GestureFaces.SwitchTag)));
            Assert.That(Triggers().Select(t => t.gateToggleId), Is.All.EqualTo(toggle.id), "every face works only while the switch is on");
            var plan = AvatarPlanCache.GetFor(menu);
            var parameter = plan.Plan.Bindings.Single(b => b.Key.Entry.id == toggle.id).Value.Parameter.Name;

            var fx = BuildAll();

            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond))
            {
                player.Set("GestureRight", GestureFaces.Fist);
                Assert.That(player.Settled("Blush").Last(), Is.EqualTo(100f).Within(0.01f), "the switch starts on");
                player.Set(parameter, 0f);
                Assert.That(player.Settled("Blush").Last(), Is.EqualTo(0f).Within(0.01f), "switched off in the menu: no face");
                player.Set("GestureRight", 0f);
                player.Settled("Blush");
                player.Set("GestureLeft", GestureFaces.Fist);
                Assert.That(player.Settled("Blush").Last(), Is.EqualTo(0f).Within(0.01f), "on either hand");
                player.Set(parameter, 1f);
                Assert.That(player.Settled("Blush").Last(), Is.EqualTo(100f).Within(0.01f), "switched on again");
            }
        }

        [TestCase(GestureHand.Left, 20f)]
        [TestCase(GestureHand.Left, 60f)]
        [TestCase(GestureHand.Right, 144f)]
        [TestCase(GestureHand.Right, 60f)]
        public void PlayedBackFistStrengthFollowsHowHardTheHandSqueezesInVR(GestureHand hand, float framesPerSecond)
        {
            var draft = Draft(Row(GestureFaces.Fist, hand, GestureTracking.Wins, ("Blush", 100f)));
            draft.fistStrength = true;
            Apply(draft);

            var fx = BuildAll();

            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond))
            {
                player.Set("VRMode", 1f);
                player.Set(GestureFaces.GestureParameter(hand), GestureFaces.Fist);
                player.Set(GestureFaces.WeightParameter(hand), 0.5f);
                var half = player.Settled("Blush");
                Assert.That(half.Last(), Is.EqualTo(50f).Within(0.5f), "half a squeeze: half the face " + Shown(half));
                player.Set(GestureFaces.WeightParameter(hand), 1f);
                Assert.That(player.Settled("Blush").Last(), Is.EqualTo(100f).Within(0.5f), "a full squeeze: the whole face");
                player.Set(GestureFaces.GestureParameter(hand), 0f);
                var after = player.Settled("Blush");
                Assert.That(after.Last(), Is.EqualTo(0f).Within(0.01f), "let go: back to rest " + Shown(after));
            }
        }

        [TestCase(20f)]
        [TestCase(60f)]
        [TestCase(144f)]
        public void PlayedBackFistStrengthInVRGrowsFromNothingWithoutAJump(float framesPerSecond)
        {
            var draft = Draft(Row(GestureFaces.Fist, GestureHand.Left, GestureTracking.Wins, ("Blush", 100f)));
            draft.fistStrength = true;
            Apply(draft);

            var fx = BuildAll();

            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond))
            {
                player.Set("VRMode", 1f);
                player.Set("GestureLeft", GestureFaces.Fist);
                var untouched = player.Settled("Blush");
                Assert.That(untouched.Max(), Is.EqualTo(0f).Within(0.01f), "a fist with the Index trigger untouched: no squeeze yet, so no face " + Shown(untouched));
                player.Set("GestureLeftWeight", 0.02f);
                var touched = player.Settled("Blush");
                Assert.That(touched.Max(), Is.LessThanOrEqualTo(2.5f), "the first touch of the trigger shows a little of the face, never the whole face first " + Shown(touched));
                Assert.That(touched.Last(), Is.EqualTo(2f).Within(0.5f));
                player.Set("GestureLeftWeight", 0.5f);
                Assert.That(player.Settled("Blush").Last(), Is.EqualTo(50f).Within(0.5f), "it grows with the squeeze");
                player.Set("GestureLeftWeight", 0f);
                var dip = player.Settled("Blush");
                Assert.That(dip.Max(), Is.LessThanOrEqualTo(50.5f), "a weight that drops to 0 never flashes the whole face " + Shown(dip));
                Assert.That(dip.Last(), Is.EqualTo(0f).Within(0.01f));
            }
        }

        [TestCase(20f)]
        [TestCase(60f)]
        [TestCase(144f)]
        public void PlayedBackFistStrengthOnDesktopShowsTheWholeFace(float framesPerSecond)
        {
            var draft = Draft(Row(GestureFaces.Fist, GestureHand.Left, GestureTracking.Wins, ("Blush", 100f)));
            draft.fistStrength = true;
            Apply(draft);

            var fx = BuildAll();

            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond))
            {
                player.Set("VRMode", 0f);
                player.Set("GestureLeft", GestureFaces.Fist);
                var still = player.Settled("Blush");
                Assert.That(still.Last(), Is.EqualTo(100f).Within(0.01f), "on desktop there is no squeeze to follow: the whole face " + Shown(still));
                player.Set("GestureLeftWeight", 0.5f);
                Assert.That(player.Settled("Blush").Last(), Is.EqualTo(100f).Within(0.01f), "whatever weight desktop reports");
                player.Set("GestureLeft", 0f);
                Assert.That(player.Settled("Blush").Last(), Is.EqualTo(0f).Within(0.01f), "let go: at rest");
            }
        }

        [TestCase(GestureOrder.RightWins, 20f)]
        [TestCase(GestureOrder.RightWins, 144f)]
        [TestCase(GestureOrder.LeftWins, 20f)]
        [TestCase(GestureOrder.LeftWins, 60f)]
        [TestCase(GestureOrder.LeftWins, 144f)]
        public void PlayedBackFistStrengthKeepsTheOrderOfTheHands(GestureOrder order, float framesPerSecond)
        {
            var draft = Draft(
                Row(GestureFaces.Fist, GestureHand.Left, GestureTracking.Wins, ("Blush", 100f)),
                Row(Victory, GestureHand.Right, GestureTracking.Wins, ("Blush", 30f)));
            draft.fistStrength = true;
            draft.order = order;
            Apply(draft);

            var fx = BuildAll();

            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond))
            {
                player.Set("VRMode", 1f);
                player.Set("GestureLeft", GestureFaces.Fist);
                player.Set("GestureLeftWeight", 1f);
                Assert.That(player.Settled("Blush").Last(), Is.EqualTo(100f).Within(0.5f), "the left fist alone");
                player.Set("GestureRight", Victory);
                var both = player.Settled("Blush");
                Assert.That(both.Last(), Is.EqualTo(order == GestureOrder.RightWins ? 30f : 100f).Within(0.5f),
                    order + ": the winning hand's face, whichever of the two follows its strength " + Shown(both));
            }
        }

        [TestCase(GestureTracking.Paused, 20f)]
        [TestCase(GestureTracking.Paused, 144f)]
        [TestCase(GestureTracking.Wins, 20f)]
        [TestCase(GestureTracking.Wins, 144f)]
        public void PlayedBackAFistStrengthFaceOverFaceTracking(GestureTracking tracking, float framesPerSecond)
        {
            TrackingParameter();
            avatar.Fx.AddParameter("LipTrackingActive", AnimatorControllerParameterType.Bool);
            FaceTracking("Blush", 40f);
            var draft = Draft(Row(GestureFaces.Fist, GestureHand.Left, tracking, ("Blush", 100f)));
            draft.trackingParameter = "LipTrackingActive";
            draft.fistStrength = true;
            Apply(draft);

            var fx = BuildAll();

            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond))
            {
                player.Set("VRMode", 1f);
                player.Set("LipTrackingActive", 1f);
                player.Set("GestureLeft", GestureFaces.Fist);
                player.Set("GestureLeftWeight", 1f);
                var held = player.Settled("Blush");
                Assert.That(held.Last(), Is.EqualTo(tracking == GestureTracking.Wins ? 100f : 40f).Within(0.5f), tracking + " while tracked " + Shown(held));
                player.Set("GestureLeftWeight", 0f);
                var still = player.Settled("Blush");
                Assert.That(still.Last(), Is.EqualTo(40f).Within(0.5f), tracking + " with no squeeze in VR: face tracking has the shape " + Shown(still));
                player.Set("VRMode", 0f);
                var desktop = player.Settled("Blush");
                Assert.That(desktop.Last(), Is.EqualTo(tracking == GestureTracking.Wins ? 100f : 40f).Within(0.5f), tracking + " on desktop " + Shown(desktop));
                player.Set("GestureLeft", 0f);
                var after = player.Settled("Blush");
                Assert.That(after.Last(), Is.EqualTo(40f).Within(0.01f), "let go: face tracking's value " + Shown(after));
            }
        }

        [TestCase(20f)]
        [TestCase(60f)]
        [TestCase(144f)]
        public void PlayedBackAPausedFaceOnTheWinningHandLetsTheLosingFaceShowWhileTracked(float framesPerSecond)
        {
            TrackingParameter();
            avatar.Fx.AddParameter("LipTrackingActive", AnimatorControllerParameterType.Bool);
            var draft = Draft(
                Row(GestureFaces.Fist, GestureHand.Left, GestureTracking.Wins, ("Blush", 100f)),
                Row(Victory, GestureHand.Right, GestureTracking.Paused, ("Eye_Big", 100f)));
            draft.trackingParameter = "LipTrackingActive";
            Apply(draft);

            var fx = BuildAll();

            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond))
            {
                player.Set("LipTrackingActive", 1f);
                player.Set("GestureLeft", GestureFaces.Fist);
                player.Set("GestureRight", Victory);
                player.Settled("Blush");
                Assert.That((player.Weight("Blush"), player.Weight("Eye_Big")), Is.EqualTo((100f, 0f)), "tracked: the paused right face shows nothing, so the left face stays");
                player.Set("LipTrackingActive", 0f);
                player.Settled("Blush");
                Assert.That((player.Weight("Blush"), player.Weight("Eye_Big")), Is.EqualTo((0f, 100f)), "not tracked: the right face wins and the left face rests");
                player.Set("LipTrackingActive", 1f);
                player.Settled("Blush");
                Assert.That((player.Weight("Blush"), player.Weight("Eye_Big")), Is.EqualTo((100f, 0f)), "tracked again: the left face is back");
            }
        }

        /// <summary>A left fist that loses to right faces on every other gesture, Wins and Paused in turn, so the cards under it fill more than one trigger.</summary>
        GestureFacesDraft SplitDraft()
        {
            TrackingParameter();
            avatar.Fx.AddParameter("LipTrackingActive", AnimatorControllerParameterType.Bool);
            var draft = Draft(Row(GestureFaces.Fist, GestureHand.Left, GestureTracking.Wins, ("Blush", 100f)));
            for (int gesture = 2; gesture <= GestureFaces.GestureCount; gesture++)
                draft.rows.Add(Row(gesture, GestureHand.Right, gesture % 2 == 0 ? GestureTracking.Wins : GestureTracking.Paused, ("Eye_Big", 100f)));
            draft.trackingParameter = "LipTrackingActive";
            return draft;
        }

        [TestCase(20f)]
        [TestCase(60f)]
        [TestCase(144f)]
        public void PlayedBackAnUnderSplitInTwoStillHoldsTheLosingFace(float framesPerSecond)
        {
            Apply(SplitDraft());

            var fx = BuildAll();

            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond))
            {
                player.Set("GestureLeft", GestureFaces.Fist);
                foreach (int gesture in new[] { 2, 7 })
                {
                    player.Set("GestureRight", gesture);
                    player.Settled("Blush");
                    Assert.That((player.Weight("Blush"), player.Weight("Eye_Big")), Is.EqualTo((0f, 100f)), "right gesture " + gesture + ": the right face, the left face at rest");
                }
                player.Set("LipTrackingActive", 1f);
                player.Settled("Blush");
                Assert.That(player.Weight("Blush"), Is.EqualTo(100f), "tracked: the paused right face on 7 shows nothing, so the left face stays");
                player.Set("GestureRight", 0f);
                player.Settled("Blush");
                Assert.That(player.Weight("Blush"), Is.EqualTo(100f));
            }
        }

        [TestCase(20f)]
        [TestCase(60f)]
        [TestCase(144f)]
        public void PlayedBackReopeningOverAPosedSceneKeepsTheRestTheFacesWereMadeWith(float framesPerSecond)
        {
            Apply(Draft(
                Row(GestureFaces.Fist, GestureHand.Left, GestureTracking.Wins, ("Blush", 100f)),
                Row(Victory, GestureHand.Right, GestureTracking.Wins, ("Eye_Big", 100f))));
            avatar.Body.SetBlendShapeWeight(0, 70f);

            var read = GestureFaces.Read(menu);
            Apply(read);

            Assert.That(Triggers().Single(t => t.gestureFace == "left:1:under").blendShapes.Single().weight, Is.EqualTo(0f), "the rest the faces were made with, not the pose the scene showed");
            var fx = BuildAll();
            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond))
            {
                player.Set("GestureLeft", GestureFaces.Fist);
                player.Set("GestureRight", Victory);
                player.Settled("Blush");
                Assert.That((player.Weight("Blush"), player.Weight("Eye_Big")), Is.EqualTo((0f, 100f)), "both hands: the left face at the rest it was made with");
            }
        }

        // ---- what Make writes ---------------------------------------------------------------------------------------

        [Test]
        public void MakeWritesOrdinaryTaggedTriggersInOneUndoStep()
        {
            TrackingParameter();
            var draft = Draft(Row(GestureFaces.Fist, GestureHand.Both, GestureTracking.Paused, ("Blush", 100f)));
            draft.trackingParameter = "LipTrackingActive";
            draft.menuSwitch = true;
            Undo.IncrementCurrentGroup();

            var entries = GestureFaces.Apply(draft, out var status);

            Assert.That(status, Is.EqualTo(string.Format(English("ad.ui.gestures.done.one"), 1)));
            var triggers = Triggers();
            Assert.That(triggers.Select(t => t.gestureFace), Is.EqualTo(new[] { "left:1", "left:1:under", "right:1" }), "right wins, so its trigger comes last");
            Assert.That(triggers.Select(t => t.name), Is.EqualTo(new[] { "Gesture: Fist (left)", "Gesture: Fist (left), under the right hand", "Gesture: Fist (right)" }));
            Assert.That(entries, Is.SupersetOf(triggers));
            var toggle = menu.entries.Single(e => e.kind == EntryKind.Toggle);
            foreach (var trigger in triggers.Where(t => !t.gestureFace.EndsWith("under")))
            {
                bool left = trigger.gestureFace.StartsWith("left");
                Assert.That(trigger.source, Is.EqualTo(TriggerSource.Parameter));
                Assert.That(trigger.touchMode, Is.EqualTo(TouchMode.Hold));
                var conditions = trigger.EffectiveWhen.Single().all;
                Assert.That(conditions.Select(c => (c.kind, c.sourceParameter, c.condition)), Is.EqualTo(new[]
                {
                    (WhenConditionKind.Parameter, left ? "GestureLeft" : "GestureRight", ParameterCondition.Equals),
                    (WhenConditionKind.Parameter, "LipTrackingActive", ParameterCondition.IsFalse),
                }));
                Assert.That(conditions[0].conditionValue, Is.EqualTo(1f));
                Assert.That(trigger.blendShapes.Select(t => (t.renderer, t.shape, t.weight, t.mode)), Is.EqualTo(new[] { (avatar.Body, "Blush", 100f, ShapeEffectMode.Pin) }),
                    "a face holds its own shapes and nothing else");
                Assert.That(trigger.gateToggleId, Is.EqualTo(toggle.id));
            }
            var under = triggers[1];
            Assert.That(under.EffectiveWhen.Single().all.Select(c => (c.sourceParameter, c.condition, c.conditionValue)), Is.EqualTo(new[]
            {
                ("GestureLeft", ParameterCondition.Equals, 1f), ("GestureRight", ParameterCondition.Equals, 1f), ("LipTrackingActive", ParameterCondition.IsFalse, 0f),
            }), "only while the left face shows and the right hand shows a face too");
            Assert.That(under.blendShapes.Select(t => (t.shape, t.weight, t.mode)), Is.EqualTo(new[] { ("Blush", 0f, ShapeEffectMode.Pin) }), "the left face's shapes at their rest weight");
            Assert.That((menu.gestureFaces.trackingChosen, menu.gestureFaces.trackingParameter), Is.EqualTo((true, "LipTrackingActive")), "the window keeps its face tracking choice");
            Assert.That(AvatarPlanCache.GetFor(menu).Messages.Where(m => m.Severity != MessageSeverity.Info).Select(m => m.Key), Is.Empty, "nothing to warn about");

            Undo.PerformUndo();

            Assert.That(menu.entries, Is.Empty, "one Undo takes back the triggers and the switch");
        }

        [Test]
        public void MakingAgainEditsTheSameTriggers()
        {
            var draft = Draft(Row(GestureFaces.Fist, GestureHand.Both, GestureTracking.Wins, ("Blush", 100f)));
            draft.menuSwitch = true;
            Apply(draft);
            var left = Triggers().Single(t => t.gestureFace == "left:1");
            var toggle = menu.entries.Single(e => e.kind == EntryKind.Toggle);
            left.objects.Add(new ObjectTarget { target = avatar.Glasses });

            var again = GestureFaces.Read(menu);
            var fist = again.rows.Single(r => r.gesture == GestureFaces.Fist && r.shapes.Count > 0);
            fist.hand = GestureHand.Left;
            fist.shapes.Clear();
            fist.shapes.Add(new GestureShape { renderer = avatar.Body, shape = "Eye_Big", weight = 60f });
            Apply(again);

            var trigger = Triggers().Single();
            Assert.That(trigger.id, Is.EqualTo(left.id), "the same trigger, edited; with no right face the left face needs nothing under it");
            Assert.That(trigger.blendShapes.Select(t => (t.shape, t.weight)), Is.EqualTo(new[] { ("Eye_Big", 60f) }));
            Assert.That(trigger.objects.Single().target, Is.SameAs(avatar.Glasses), "what the creator added to it stays");
            Assert.That(menu.entries.Single(e => e.kind == EntryKind.Toggle).id, Is.EqualTo(toggle.id), "one switch, the same one");

            int count = menu.entries.Count;
            Apply(GestureFaces.Read(menu));
            Assert.That(menu.entries.Count, Is.EqualTo(count), "making again without a change adds nothing");
            Assert.That(Triggers().Single().id, Is.EqualTo(left.id));

            var cleared = GestureFaces.Read(menu);
            foreach (var row in cleared.rows) row.shapes.Clear();
            GestureFaces.Apply(cleared, out var status);
            Assert.That(menu.entries, Is.Empty, "no face left: the triggers and their switch go");
            Assert.That(status, Is.EqualTo(English("ad.ui.gestures.done.none")));
        }

        [Test]
        public void WhatTheCreatorAddsStaysAndACopyIsTheirOwn()
        {
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            Apply(Draft(Row(GestureFaces.Fist, GestureHand.Left, GestureTracking.Wins, ("Blush", 100f))));
            var trigger = Triggers().Single();
            var own = EntryEditing.AddCondition(menu, trigger, 0);
            own.kind = WhenConditionKind.Toggle;
            own.sourceToggleId = hat.id;
            var copy = EntryEditing.Duplicate(menu, trigger);
            Assert.That(copy.gestureFace, Is.Empty, "a copy is the creator's own trigger");

            var again = GestureFaces.Read(menu);
            again.rows.Single(r => r.shapes.Count > 0).shapes[0].weight = 70f;
            Apply(again);

            Assert.That(trigger.EffectiveWhen.Single().all.Select(c => c.kind), Is.EqualTo(new[] { WhenConditionKind.Parameter, WhenConditionKind.Toggle }),
                "the gesture first, then the creator's own condition");
            Assert.That(trigger.blendShapes.Single().weight, Is.EqualTo(70f));
            Assert.That(copy.blendShapes.Single().weight, Is.EqualTo(100f), "the copy is left alone");
            Assert.That(Triggers().Count, Is.EqualTo(2));
        }

        [Test]
        public void AConditionOfTheCreatorsOwnThatAParameterIsOffStaysTheirs()
        {
            TrackingParameter();
            var draft = Draft(
                Row(GestureFaces.Fist, GestureHand.Left, GestureTracking.Wins, ("Blush", 100f)),
                Row(Victory, GestureHand.Left, GestureTracking.Paused, ("Eye_Big", 100f)));
            draft.trackingParameter = "LipTrackingActive";
            Apply(draft);
            var fist = Triggers().Single(t => t.gestureFace == "left:1");
            var afk = EntryEditing.AddCondition(menu, fist, 0);
            afk.kind = WhenConditionKind.Parameter;
            afk.sourceParameter = "AFK";
            afk.condition = ParameterCondition.IsFalse;

            var read = GestureFaces.Read(menu);
            Assert.That(read.trackingParameter, Is.EqualTo("LipTrackingActive"), "the window's own choice, not a condition of the creator's");
            Assert.That(read.rows.Single(r => r.gesture == GestureFaces.Fist).tracking, Is.EqualTo(GestureTracking.Wins), "AFK is off does not make the face Paused");
            Apply(read);

            var victory = Triggers().Single(t => t.gestureFace == "left:4");
            Assert.That(victory.EffectiveWhen.Single().all.Select(c => c.sourceParameter), Is.EqualTo(new[] { "GestureLeft", "LipTrackingActive" }), "the Paused face keeps its tracking condition");
            Assert.That(fist.EffectiveWhen.Single().all.Select(c => (c.sourceParameter, c.condition)), Is.EqualTo(new[]
            {
                ("GestureLeft", ParameterCondition.Equals), ("AFK", ParameterCondition.IsFalse),
            }), "and the creator's own condition stays theirs");
        }

        [Test]
        public void ChoosingNoFaceTrackingIsRemembered()
        {
            TrackingParameter();
            var draft = GestureFaces.Read(menu);
            Assert.That(draft.trackingParameter, Is.EqualTo("LipTrackingActive"), "sanity: found");
            draft.trackingParameter = "";
            draft.menuSwitch = false;
            draft.rows[0].shapes.Add(new GestureShape { renderer = avatar.Body, shape = "Blush", weight = 100f });
            Apply(draft);

            var read = GestureFaces.Read(menu);

            Assert.That(read.trackingParameter, Is.EqualTo(""), "None stays None, found or not");
            Assert.That(read.rows.Select(r => r.tracking), Is.All.EqualTo(GestureTracking.Paused), "and the faces keep the choice they start with");
        }

        [Test]
        public void AGateOfTheCreatorsOwnStaysAndTheSwitchIsReadFromItsToggle()
        {
            var outfit = menu.AddEntry(EntryKind.Toggle, "Outfit A");
            outfit.objects.Add(new ObjectTarget { target = avatar.Hat });
            Apply(Draft(Row(GestureFaces.Fist, GestureHand.Left, GestureTracking.Wins, ("Blush", 100f))));
            var fist = Triggers().Single();
            fist.gateToggleId = outfit.id;

            var read = GestureFaces.Read(menu);
            Assert.That(read.menuSwitch, Is.False, "no switch toggle, so no switch, whatever gate a face has");
            Apply(read);

            Assert.That(fist.gateToggleId, Is.EqualTo(outfit.id), "the creator's Only while stays");
            Assert.That(menu.entries.Count(e => e.kind == EntryKind.Toggle), Is.EqualTo(1), "and no switch is made");

            read = GestureFaces.Read(menu);
            read.menuSwitch = true;
            Apply(read);
            Assert.That(fist.gateToggleId, Is.EqualTo(outfit.id), "a face with an Only while of its own keeps it when the switch comes");
        }

        [Test]
        public void TurningTheMenuSwitchOffTakesItsToggleAndItsGatesAway()
        {
            var draft = Draft(Row(GestureFaces.Fist, GestureHand.Both, GestureTracking.Wins, ("Blush", 100f)));
            draft.menuSwitch = true;
            Apply(draft);
            var again = GestureFaces.Read(menu);
            Assert.That(again.menuSwitch, Is.True, "sanity: the switch reads back on");

            again.menuSwitch = false;
            Apply(again);

            Assert.That(menu.entries.Any(e => e.kind == EntryKind.Toggle), Is.False);
            Assert.That(Triggers().Select(t => t.gateToggleId), Is.All.Empty);
            Assert.That(GestureFaces.Read(menu).menuSwitch, Is.False, "and it reads back off");
        }

        [Test]
        public void RemovingTheSwitchLeavesNothingPointingAtIt()
        {
            var draft = Draft(Row(GestureFaces.Fist, GestureHand.Left, GestureTracking.Wins, ("Blush", 100f)));
            draft.menuSwitch = true;
            Apply(draft);
            var toggle = menu.entries.Single(e => e.kind == EntryKind.Toggle);
            var copy = EntryEditing.Duplicate(menu, Triggers().Single());
            var own = menu.AddEntry(EntryKind.Trigger, "Glasses with the faces");
            own.sourceToggleId = toggle.id;
            own.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var grouped = menu.AddEntry(EntryKind.Trigger, "Hat with the faces");
            grouped.objects.Add(new ObjectTarget { target = avatar.Hat });
            grouped.when.Add(new ConditionGroup { all =
            {
                new WhenCondition { kind = WhenConditionKind.Toggle, sourceToggleId = toggle.id },
                new WhenCondition { kind = WhenConditionKind.Parameter, sourceParameter = "Seated", condition = ParameterCondition.IsTrue },
            } });
            Assert.That(copy.gateToggleId, Is.EqualTo(toggle.id), "sanity: the copy is gated on the switch");

            var read = GestureFaces.Read(menu);
            read.menuSwitch = false;
            Undo.IncrementCurrentGroup();
            Apply(read);

            Assert.That(menu.FindEntry(toggle.id), Is.Null);
            Assert.That(copy.gateToggleId, Is.Empty, "the copy no longer points at a toggle that is gone");
            Assert.That(own.sourceToggleId, Is.Empty);
            Assert.That(grouped.EffectiveWhen.Single().all.Select(c => (c.kind, c.sourceParameter)), Is.EqualTo(new[] { (WhenConditionKind.Parameter, "Seated") }), "a condition on the switch goes with it");
            var plan = AvatarPlanCache.GetFor(menu);
            Assert.That(plan.Messages.Where(m => m.Severity == MessageSeverity.Error).Select(m => m.Key), Is.Empty, "nothing stops the upload");

            Undo.PerformUndo();
            Assert.That(menu.FindEntry(toggle.id), Is.Not.Null, "one Undo puts the switch back");
            Assert.That(menu.FindEntry(copy.id).gateToggleId, Is.EqualTo(toggle.id), "and every link to it");
        }

        [Test]
        public void ReadingBackShowsWhatTheTriggersDo()
        {
            TrackingParameter();
            var draft = Draft(
                Row(GestureFaces.Fist, GestureHand.Both, GestureTracking.Paused, ("Blush", 100f)),
                Row(Victory, GestureHand.Right, GestureTracking.Wins, ("Eye_Big", 40f), ("Blush", 0f)));
            draft.trackingParameter = "LipTrackingActive";
            draft.order = GestureOrder.LeftWins;
            draft.fistStrength = true;
            draft.menuSwitch = true;
            Apply(draft);

            var read = GestureFaces.Read(menu);

            Assert.That(read.component, Is.SameAs(menu));
            Assert.That(read.faceMesh, Is.SameAs(avatar.Body));
            Assert.That((read.trackingParameter, read.order, read.fistStrength, read.menuSwitch), Is.EqualTo(("LipTrackingActive", GestureOrder.LeftWins, true, true)));
            Assert.That(read.rows.Select(r => r.gesture).Distinct().OrderBy(g => g), Is.EqualTo(Enumerable.Range(1, GestureFaces.GestureCount)), "a row for every gesture");
            var faces = read.rows.Where(r => r.shapes.Count > 0).ToList();
            Assert.That(faces.Select(r => (r.gesture, r.hand, r.tracking)), Is.EqualTo(new[]
            {
                (GestureFaces.Fist, GestureHand.Both, GestureTracking.Paused),
                (Victory, GestureHand.Right, GestureTracking.Wins),
            }));
            Assert.That(faces[0].shapes.Select(s => (s.shape, s.weight)), Is.EqualTo(new[] { ("Blush", 100f) }), "the triggers made to rest a face under the other hand are not faces");
            Assert.That(faces[1].shapes.Select(s => (s.shape, s.weight)), Is.EqualTo(new[] { ("Eye_Big", 40f), ("Blush", 0f) }), "a shape the face holds at 0 stays part of it");
        }

        [Test]
        public void TheLosingFaceRestsUnderTheWinningHandAtItsRestWeights()
        {
            avatar.Body.SetBlendShapeWeight(0, 5f);
            Apply(Draft(
                Row(GestureFaces.Fist, GestureHand.Left, GestureTracking.Wins, ("Blush", 100f)),
                Row(2, GestureHand.Right, GestureTracking.Wins, ("Eye_Big", 100f)),
                Row(3, GestureHand.Right, GestureTracking.Wins, ("Eye_Big", 50f)),
                Row(6, GestureHand.Right, GestureTracking.Wins, ("Eye_Big", 20f))));

            Assert.That(Triggers().Select(t => t.gestureFace), Is.EqualTo(new[] { "left:1", "left:1:under", "right:2", "right:3", "right:6" }));
            var fist = Triggers()[0];
            var under = Triggers()[1];
            Assert.That(fist.blendShapes.Select(t => (t.shape, t.weight)), Is.EqualTo(new[] { ("Blush", 100f) }));
            Assert.That(under.blendShapes.Select(t => (t.shape, t.weight, t.mode)), Is.EqualTo(new[] { ("Blush", 5f, ShapeEffectMode.Pin) }), "at the weight the face rests at");
            var groups = under.EffectiveWhen.Select(g => string.Join(" & ", g.all.Select(c => c.sourceParameter + " " + c.condition + " " + c.conditionValue))).ToList();
            Assert.That(groups, Is.EqualTo(new[]
            {
                "GestureLeft Equals 1 & GestureRight Greater 1 & GestureRight Less 4",
                "GestureLeft Equals 1 & GestureRight Equals 6",
            }), "while the right hand shows one of the gestures it has a face for");
            foreach (var face in Triggers().Where(t => t.gestureFace.StartsWith("right")))
                Assert.That(face.blendShapes.Select(t => t.shape), Is.EqualTo(new[] { "Eye_Big" }), face.name + " holds its own shape only");
            Assert.That(AvatarPlanCache.GetFor(menu).Messages.Where(m => m.Severity != MessageSeverity.Info).Select(m => m.Key), Is.Empty);
        }

        [Test]
        public void FistStrengthMakesTheFistFollowItsWeight()
        {
            var draft = Draft(
                Row(GestureFaces.Fist, GestureHand.Right, GestureTracking.Wins, ("Blush", 100f)),
                Row(Victory, GestureHand.Right, GestureTracking.Wins, ("Eye_Big", 100f)));
            draft.fistStrength = true;
            Apply(draft);

            var fist = Triggers().Single(t => t.gestureFace == "right:1");
            Assert.That(fist.IsFollowValueTrigger, Is.True, "the fist blends with its weight");
            Assert.That(fist.EffectiveWhen.Single().all.Select(c => (c.sourceParameter, c.condition)).First(), Is.EqualTo(("GestureRightWeight", ParameterCondition.Greater)), "its weight comes first, as the one that moves it");
            Assert.That(fist.EffectiveContactFullAt, Is.EqualTo(1f), "fully on at a full squeeze");
            var whole = Triggers().Single(t => t.gestureFace == "right:1:whole");
            Assert.That(whole.name, Is.EqualTo("Gesture: Fist (right), on desktop"));
            Assert.That(whole.touchMode, Is.EqualTo(TouchMode.Hold));
            Assert.That(whole.EffectiveWhen.Single().all.Select(c => (c.sourceParameter, c.condition, c.conditionValue)), Is.EqualTo(new[]
            {
                ("GestureRight", ParameterCondition.Equals, 1f), ("VRMode", ParameterCondition.Equals, 0f),
            }), "the whole face on desktop, where there is no squeeze to follow");
            Assert.That(whole.blendShapes.Select(t => (t.shape, t.weight)), Is.EqualTo(new[] { ("Blush", 100f) }));
            Assert.That(Triggers().Select(t => t.gestureFace), Is.EqualTo(new[] { "right:1", "right:1:whole", "right:4" }), "after the fade, so on desktop it wins whatever the weight");
            Assert.That(Triggers().Single(t => t.gestureFace == "right:4").IsFollowDistanceTrigger, Is.False, "every other gesture switches on");

            fist.objects.Add(new ObjectTarget { target = avatar.Glasses });
            Apply(GestureFaces.Read(menu));
            Assert.That(whole.objects.Select(o => o.target), Is.EqualTo(new[] { avatar.Glasses }), "what the creator adds to the Fist face shows on desktop too");
        }

        [Test]
        public void FaceTrackingIsFoundAmongTheAvatarsParametersLipTrackingFirst()
        {
            Assert.That(GestureFaces.DetectTracking(avatar.Root.transform), Is.EqualTo(""), "nothing to find");
            TrackingParameter("EyeTrackingActive");
            AvatarPlanCache.Invalidate();
            Assert.That(GestureFaces.DetectTracking(avatar.Root.transform), Is.EqualTo("EyeTrackingActive"));
            TrackingParameter("LipTrackingActive");
            AvatarPlanCache.Invalidate();
            Assert.That(GestureFaces.DetectTracking(avatar.Root.transform), Is.EqualTo("LipTrackingActive"), "lip tracking first");
            Assert.That(GestureFaces.Read(menu).trackingParameter, Is.EqualTo("LipTrackingActive"), "a new set of faces starts with it");

            avatar.ExpressionParameters.parameters = avatar.ExpressionParameters.parameters.Where(p => !p.name.EndsWith("TrackingActive")).ToArray();
            avatar.Fx.AddParameter(new AnimatorControllerParameter { name = "LipTrackingActive", type = AnimatorControllerParameterType.Bool });
            AvatarPlanCache.Invalidate();
            Assert.That(GestureFaces.DetectTracking(avatar.Root.transform), Is.EqualTo("LipTrackingActive"), "an FX parameter counts too");
        }

        [Test]
        public void FaceTrackingIsFoundInAParametersAssetAnotherComponentInstalls()
        {
            // As the face tracking templates' VRCFury prefab merges it: every name kept global.
            FullController(ParametersAsset(("LipTrackingActive", VRCExpressionParameters.ValueType.Bool), ("v2/JawOpen", VRCExpressionParameters.ValueType.Float)), "*");

            Assert.That(GestureFaces.DetectTracking(avatar.Root.transform), Is.EqualTo("LipTrackingActive"), "found in the parameters asset the face tracking prefab merges");
            Assert.That(GestureFaces.TrackingChoices(avatar.Root.transform, ""), Does.Contain("LipTrackingActive").And.Not.Contain("v2/JawOpen"), "its Bools only");
            var draft = GestureFaces.Read(menu);
            Assert.That(draft.trackingParameter, Is.EqualTo("LipTrackingActive"));
            draft.menuSwitch = false;
            draft.rows[0].shapes.Add(new GestureShape { renderer = avatar.Body, shape = "Blush", weight = 100f });
            Apply(draft);
            Assert.That(AvatarPlanCache.GetFor(menu).Messages.Select(m => m.Key), Has.None.EqualTo("ad.warn.parameter_type_unknown"), "the plan knows it is a Bool");

            var fx = BuildAll();

            Assert.That(fx.parameters.Single(p => p.name == "LipTrackingActive").type, Is.EqualTo(AnimatorControllerParameterType.Bool),
                "declared as the Bool the face tracking prefab merges later, never as a Float");
        }

        [Test]
        public void CaptureTakesTheFaceAsTheMeshShowsItNow()
        {
            var draft = Draft(Row(GestureFaces.Fist, GestureHand.Both, GestureTracking.Wins, ("Eye_Big", 30f)));
            var row = draft.rows[0];
            avatar.Body.SetBlendShapeWeight(0, 70f);

            Assert.That(GestureFaces.Capture(draft, row), Is.EqualTo(1));

            Assert.That(row.shapes.Select(s => (s.renderer, s.shape, s.weight)), Is.EqualTo(new[] { (avatar.Body, "Blush", 70f) }), "the face is what the mesh shows now");
            Assert.That(avatar.Body.GetBlendShapeWeight(0), Is.EqualTo(70f), "the scene is only read");
            avatar.Body.SetBlendShapeWeight(0, 0f);
            Assert.That(GestureFaces.Capture(draft, row), Is.EqualTo(0), "every shape where it rests");
            Assert.That(row.shapes.Single().shape, Is.EqualTo("Blush"), "and the face stays as it was");
        }

        [Test]
        public void CaptureTakesOnlyTheShapesThatDifferFromWhereTheFaceRests()
        {
            avatar.Body.SetBlendShapeWeight(1, 60f);
            var draft = GestureFaces.Read(menu);
            var row = draft.rows[0];
            avatar.Body.SetBlendShapeWeight(0, 70f);
            avatar.Body.SetBlendShapeWeight(1, 0f);

            Assert.That(GestureFaces.Capture(draft, row), Is.EqualTo(2));

            Assert.That(row.shapes.Select(s => (s.shape, s.weight)), Is.EqualTo(new[] { ("Blush", 70f), ("Eye_Big", 0f) }),
                "a shape set above its rest and one set below it; nothing the face leaves at rest");
            Assert.That(GestureFaces.PosedShapes(draft).Select(s => s.shape), Is.EqualTo(new[] { "Blush", "Eye_Big" }));
            Assert.That(GestureFaces.ResetPosed(draft), Is.EqualTo(2));
            Assert.That((avatar.Body.GetBlendShapeWeight(0), avatar.Body.GetBlendShapeWeight(1)), Is.EqualTo((0f, 60f)), "put back where each rests, not at 0");
            Assert.That(GestureFaces.PosedShapes(draft), Is.Empty);
        }

        [Test]
        public void TwoFacesForOneHandAreRefused()
        {
            var draft = Draft(
                Row(GestureFaces.Fist, GestureHand.Both, GestureTracking.Wins, ("Blush", 100f)),
                Row(GestureFaces.Fist, GestureHand.Left, GestureTracking.Wins, ("Eye_Big", 100f)),
                Row(Victory, GestureHand.Left, GestureTracking.Wins, ("Blush", 100f)),
                Row(Victory, GestureHand.Right, GestureTracking.Wins, ("Eye_Big", 100f)));

            Assert.That(GestureFaces.Overlaps(draft), Is.EqualTo(new[] { GestureFaces.Fist }), "a left and a right Victory row are fine");
            var entries = GestureFaces.Apply(draft, out var status);

            Assert.That(entries, Is.Empty);
            Assert.That(menu.entries, Is.Empty);
            Assert.That(status, Is.EqualTo(string.Format(English("ad.ui.gestures.overlap"), "Fist")));
        }

        [Test]
        public void ALockedComponentIsRefused()
        {
            menu.locked = true;
            var entries = GestureFaces.Apply(Draft(Row(GestureFaces.Fist, GestureHand.Both, GestureTracking.Wins, ("Blush", 100f))), out var status);
            Assert.That(entries, Is.Empty);
            Assert.That(menu.entries, Is.Empty);
            Assert.That(status, Is.EqualTo(English("ad.ui.locked.refused")));
        }

        [Test]
        public void ReopeningOverAPosedSceneStillOffersToPutTheFaceBack()
        {
            Apply(Draft(Row(GestureFaces.Fist, GestureHand.Left, GestureTracking.Wins, ("Blush", 100f))));
            Assert.That(menu.gestureFaces.rest.Select(r => (r.renderer, r.shape, r.weight)), Is.EqualTo(new[] { (avatar.Body, "Blush", 0f) }),
                "Make keeps where each shape of a face rests");
            avatar.Body.SetBlendShapeWeight(0, 70f);

            var read = GestureFaces.Read(menu);

            Assert.That(GestureFaces.PosedShapes(read).Select(s => s.shape), Is.EqualTo(new[] { "Blush" }), "the warning shows: the posed scene is not taken for the rest");
            var victory = read.rows.Single(r => r.gesture == Victory);
            Assert.That(GestureFaces.Capture(read, victory), Is.EqualTo(1), "and the pose can be captured");
            Assert.That(GestureFaces.ResetPosed(read), Is.EqualTo(1));
            Assert.That(avatar.Body.GetBlendShapeWeight(0), Is.EqualTo(0f), "put back at the rest Make kept");
        }

        /// <summary>A VRCFury Full Controller under the avatar that merges this parameters asset and keeps these names global.</summary>
        void FullController(VRCExpressionParameters parameters, params string[] globals)
        {
            var installer = new GameObject("FaceTracking");
            installer.transform.SetParent(avatar.Root.transform);
            var full = VrcFuryTestHelper.AddFeature(installer, "VF.Model.Feature.FullController");
            VrcFuryTestHelper.ConfigureFeature(full, model =>
            {
                var entryType = Type.GetType("VF.Model.Feature.FullController+ParamsEntry, VRCFury");
                Assume.That(entryType, Is.Not.Null);
                var entry = Activator.CreateInstance(entryType, true);
                VrcFuryTestHelper.SetField(entry, "parameters", VrcFuryTestHelper.BuildGuidWrapper("VF.Model.GuidParams", parameters));
                ((System.Collections.IList)VrcFuryTestHelper.GetField(model, "prms")).Add(entry);
                VrcFuryTestHelper.SetField(model, "globalParams", globals.ToList());
            });
            AvatarPlanCache.Invalidate();
        }

        VRCExpressionParameters ParametersAsset(params (string name, VRCExpressionParameters.ValueType type)[] parameters)
        {
            var asset = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            made.Add(asset);
            asset.parameters = parameters.Select(p => new VRCExpressionParameters.Parameter { name = p.name, valueType = p.type, saved = true, networkSynced = true }).ToArray();
            return asset;
        }

        [Test]
        public void OnlyTheNamesVRCFuryKeepsGlobalJoinThePlan()
        {
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.parameterName = "Hat/On";
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            FullController(ParametersAsset(
                    ("LipTrackingActive", VRCExpressionParameters.ValueType.Bool), ("Hat/On", VRCExpressionParameters.ValueType.Bool),
                    ("Face/Smile", VRCExpressionParameters.ValueType.Bool), ("Face/Hidden", VRCExpressionParameters.ValueType.Bool)),
                "LipTrackingActive", "Face/*", "!Face/Hidden");

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(plan.Existing.Keys, Does.Contain("LipTrackingActive").And.Contain("Face/Smile"), "an exact name and a prefix VRCFury keeps");
            Assert.That(plan.Existing.Keys, Does.Not.Contain("Hat/On").And.Not.Contain("Face/Hidden"), "VRCFury gives every other name one of its own");
            Assert.That(GestureFaces.TrackingChoices(avatar.Root.transform, ""), Does.Not.Contain("Hat/On"));
            var own = plan.Plan.Parameters.Single(p => p.Name == "Hat/On");
            Assert.That((own.IsAnimatorOnly, own.Synced), Is.EqualTo((false, true)), "the toggle's parameter is still AvatarDoc's own, synced");
        }

        [Test]
        public void TheUnderTriggerAsksForTheFacesOwnConditionsToo()
        {
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var outfit = menu.AddEntry(EntryKind.Toggle, "Outfit A");
            outfit.objects.Add(new ObjectTarget { target = avatar.Glasses });
            Apply(Draft(
                Row(GestureFaces.Fist, GestureHand.Left, GestureTracking.Wins, ("Blush", 100f)),
                Row(2, GestureHand.Right, GestureTracking.Wins, ("Eye_Big", 100f)),
                Row(3, GestureHand.Right, GestureTracking.Wins, ("Eye_Big", 50f))));
            var fist = Triggers().Single(t => t.gestureFace == "left:1");
            var own = EntryEditing.AddCondition(menu, fist, 0);
            own.kind = WhenConditionKind.Toggle;
            own.sourceToggleId = hat.id;
            Triggers().Single(t => t.gestureFace == "right:2").gateToggleId = outfit.id;
            var under = Triggers().Single(t => t.gestureFace == "left:1:under");
            under.objects.Add(new ObjectTarget { target = avatar.Glasses });

            Apply(GestureFaces.Read(menu));

            string Words(WhenCondition c) => c.kind == WhenConditionKind.Toggle ? menu.FindEntry(c.sourceToggleId).name : c.sourceParameter + " " + c.condition + " " + c.conditionValue;
            var groups = under.EffectiveWhen.Select(g => string.Join(" & ", g.all.Select(Words))).ToList();
            Assert.That(groups, Is.EqualTo(new[]
            {
                "GestureLeft Equals 1 & GestureRight Equals 3 & Hat",
                "GestureLeft Equals 1 & GestureRight Equals 2 & Outfit A & Hat",
            }), "the left face's own condition on every card, and the right face with an Only while of its own on a card of its own");
            Assert.That(under.objects, Is.Empty, "the window owns what the trigger under a face does");
        }

        [Test]
        public void ATypedNameAvatarDocCannotSeeIsSaidSoAndBuildsAsABool()
        {
            var draft = GestureFaces.Read(menu);
            draft.trackingParameter = "FT/Hidden/LipTrackingActive";
            draft.menuSwitch = false;
            draft.rows[0].shapes.Add(new GestureShape { renderer = avatar.Body, shape = "Blush", weight = 100f });
            var window = Window(draft);
            try
            {
                var note = window.rootVisualElement.Q<Label>("ad-gestures-tracking-unknown");
                Assert.That(note, Is.Not.Null, "AvatarDoc cannot see the name, so it says so");
                Assert.That(note.text, Is.EqualTo(string.Format(English("ad.ui.gestures.tracking.unknown"), "FT/Hidden/LipTrackingActive")));
                Assert.That(window.rootVisualElement.Q("ad-gestures-tracking-local"), Is.Null, "and claims nothing about its sync");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
            Apply(draft);
            Assert.That(AvatarPlanCache.GetFor(menu).Messages.Select(m => m.Key), Does.Contain("ad.warn.parameter_type_unknown_bool"));

            var fx = BuildAll();

            Assert.That(fx.parameters.Single(p => p.name == "FT/Hidden/LipTrackingActive").type, Is.EqualTo(AnimatorControllerParameterType.Bool), "read as on or off, so a Bool");
        }

        [Test]
        public void TheSwitchALockedComponentUsesStaysAndMakeSaysWhy()
        {
            var draft = Draft(Row(GestureFaces.Fist, GestureHand.Left, GestureTracking.Wins, ("Blush", 100f)));
            draft.menuSwitch = true;
            Apply(draft);
            var toggle = menu.entries.Single(e => e.kind == EntryKind.Toggle);
            var extras = new GameObject("Extras");
            extras.transform.SetParent(avatar.Root.transform);
            var other = avatar.AddMenu(extras);
            var gated = other.AddEntry(EntryKind.Trigger, "Glasses with the faces");
            gated.objects.Add(new ObjectTarget { target = avatar.Glasses });
            gated.gateToggleId = toggle.id;
            other.locked = true;
            AvatarPlanCache.Invalidate();

            var read = GestureFaces.Read(menu);
            read.menuSwitch = false;
            var entries = GestureFaces.Apply(read, out var status);

            Assert.That(entries, Is.Empty, "nothing is written");
            Assert.That(status, Is.EqualTo(string.Format(English("ad.ui.gestures.switch_locked"), "Extras")));
            Assert.That(menu.FindEntry(toggle.id), Is.Not.Null, "the switch stays");
            Assert.That(gated.gateToggleId, Is.EqualTo(toggle.id));
        }

        [Test]
        public void AnUnderTooLongForOneLayerIsSplit()
        {
            Apply(SplitDraft());

            var unders = Triggers().Where(t => t.gestureFace.StartsWith("left:1:under", StringComparison.Ordinal)).ToList();
            Assert.That(unders.Select(t => t.gestureFace), Is.EqualTo(new[] { "left:1:under", "left:1:under2" }));
            Assert.That(unders[1].name, Is.EqualTo("Gesture: Fist (left), under the right hand 2"));
            Assert.That(unders.Select(t => t.EffectiveWhen.Count), Is.EqualTo(new[] { 4, 2 }));
            Assert.That(AvatarPlanCache.GetFor(menu).Messages.Where(m => m.Severity != MessageSeverity.Info).Select(m => m.Key), Is.Empty, "each fits what a layer may hold");
        }

        // ---- the window ---------------------------------------------------------------------------------------------

        GestureFacesWindow Window(GestureFacesDraft draft)
        {
            var window = ScriptableObject.CreateInstance<GestureFacesWindow>();
            window.Init(draft);
            return window;
        }

        static IEnumerable<AdMenu.Item> Flatten(IEnumerable<AdMenu.Item> items)
        {
            foreach (var item in items)
            {
                yield return item;
                if (item.Children != null)
                    foreach (var child in Flatten(item.Children)) yield return child;
            }
        }

        [Test]
        public void TheWindowListsEveryGestureAndMakesTheFacesWithItsButton()
        {
            var window = Window(GestureFaces.Read(menu));
            try
            {
                var selection = Selection.objects;
                var root = window.rootVisualElement;
                Assert.That(root.Q<Button>("ad-gestures-mesh").text, Is.EqualTo("Body"), "the avatar's one face mesh");
                var titles = root.Query<Label>("ad-gestures-row-title").ToList().Select(l => l.text).ToList();
                Assert.That(titles, Is.EqualTo(new[] { "Fist", "Hand open", "Point", "Victory", "Rock and roll", "Hand gun", "Thumbs up" }));
                Assert.That(root.Q("ad-gestures-row-0").Q<SegmentedChoice>("ad-gestures-hand").Index, Is.EqualTo((int)GestureHand.Both));
                Assert.That(root.Q<Button>("ad-gestures-make").enabledSelf, Is.False, "no face yet");

                avatar.Body.SetBlendShapeWeight(0, 70f);
                LayoutProbe.Measure(root, () =>
                {
                    UiTestEvents.SimulateClick(window.rootVisualElement.Q("ad-gestures-row-0").Q<Button>("ad-gestures-capture"));
                });
                Assert.That(window.Draft.rows[0].shapes.Select(s => (s.shape, s.weight)), Is.EqualTo(new[] { ("Blush", 70f) }));
                Assert.That(window.rootVisualElement.Q("ad-gestures-row-0").Q<Slider>("ad-gestures-shape").value, Is.EqualTo(70f));
                Assert.That(window.rootVisualElement.Q("ad-gestures-row-0").Q<Label>("ad-gestures-shape-name").text, Is.EqualTo("Blush"));
                Assert.That(window.Status, Is.EqualTo(string.Format(English("ad.ui.gestures.captured.one"), 1, "Fist")));
                Assert.That(window.rootVisualElement.Q("ad-gestures-posed"), Is.Not.Null, "the scene still shows the face");

                UiTestEvents.SimulateClick(window.rootVisualElement.Q<Button>("ad-gestures-posed-reset"));
                Assert.That(avatar.Body.GetBlendShapeWeight(0), Is.EqualTo(0f), "put back where it rests, for the next face");
                Assert.That(window.rootVisualElement.Q("ad-gestures-posed"), Is.Null);

                UiTestEvents.SimulateClick(window.rootVisualElement.Q<Button>("ad-gestures-make"));

                Assert.That(Triggers().Select(t => t.gestureFace), Is.EqualTo(new[] { "left:1", "left:1:under", "right:1" }));
                Assert.That(window.Status, Is.EqualTo(string.Format(English("ad.ui.gestures.done.one"), 1)));
                Assert.That(Selection.objects, Is.EqualTo(selection), "the selection stays as it was");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void TheWindowOffersTheAvatarsBoolParametersForFaceTracking()
        {
            TrackingParameter();
            var window = Window(GestureFaces.Read(menu));
            try
            {
                var root = window.rootVisualElement;
                Assert.That(root.Q<TextField>("ad-gestures-tracking").value, Is.EqualTo("LipTrackingActive"), "found and filled in");
                Assert.That(root.Q("ad-gestures-row-0").Q<SegmentedChoice>("ad-gestures-row-tracking"), Is.Not.Null, "each face says what it does while tracking is on");
                UiTestEvents.SimulateClick(root.Q<Button>("ad-gestures-tracking-pick"));
                var items = Flatten(shownMenu).Where(i => i.Action != null).ToList();
                Assert.That(items.Select(i => i.Text), Does.Contain("LipTrackingActive").And.Contain("Existing"), "every Bool of the avatar");
                Assert.That(items.Select(i => i.Text), Does.Not.Contain("GestureLeft"), "never an Int");
                items.Single(i => i.Text == English("ad.ui.gestures.tracking.none")).Action();
                Assert.That(window.Draft.trackingParameter, Is.EqualTo(""));
                Assert.That(window.rootVisualElement.Q("ad-gestures-row-0").Q<SegmentedChoice>("ad-gestures-row-tracking"), Is.Null, "no face tracking: nothing to choose per face");
                Assert.That(window.rootVisualElement.Q<Label>("ad-gestures-tracking-none"), Is.Not.Null);

                LayoutProbe.Measure(window.rootVisualElement, () => window.rootVisualElement.Q<TextField>("ad-gestures-tracking").value = "FT/LipTrackingActive");
                Assert.That(window.Draft.trackingParameter, Is.EqualTo("FT/LipTrackingActive"), "any name can be typed");
                Assert.That(window.rootVisualElement.Q("ad-gestures-row-0").Q<SegmentedChoice>("ad-gestures-row-tracking"), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void TheWindowSaysWhenTheTrackingParameterIsNotSynced()
        {
            avatar.Fx.AddParameter(new AnimatorControllerParameter { name = "LipTrackingActive", type = AnimatorControllerParameterType.Bool });
            AvatarPlanCache.Invalidate();
            var window = Window(GestureFaces.Read(menu));
            try
            {
                var note = window.rootVisualElement.Q<Label>("ad-gestures-tracking-local");
                Assert.That(note, Is.Not.Null, "an FX parameter only: other players never see it on");
                Assert.That(note.text, Is.EqualTo(string.Format(English("ad.ui.gestures.tracking.local"), "LipTrackingActive")));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }

            TrackingParameter();
            AvatarPlanCache.Invalidate();
            window = Window(GestureFaces.Read(menu));
            try
            {
                Assert.That(window.rootVisualElement.Q("ad-gestures-tracking-local"), Is.Null, "a synced expression parameter needs no note");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void TheToolbarOpensTheWindowForThisComponent()
        {
            AvatarDoc opened = null;
            GestureFacesWindow.Open = component => opened = component;
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);

            view.Tree.MoreItems().Single(i => i.NameKey == "ad.ui.add.gesture_faces").Run();

            Assert.That(opened, Is.SameAs(menu));
            Assert.That(view.Tree.Q<ToolbarButton>("ad-add-gesture-faces"), Is.Not.Null);
            menu.locked = true;
            opened = null;
            view.Tree.MoreItems().Single(i => i.NameKey == "ad.ui.add.gesture_faces").Run();
            Assert.That(opened, Is.Null, "refused while locked");
        }

        /// <summary>A part of a Unity field, such as a slider's number box, or of a scroll bar: the field's own tooltip covers it.</summary>
        static bool InsideAField(VisualElement e)
        {
            for (var p = e.parent; p != null; p = p.parent)
                if (p.ClassListContains("unity-base-field") || p is Scroller) return true;
            return false;
        }

        const string LongJapaneseShape = "まばたき（笑い）とても長いシェイプの名前";
        const string LongShape = "Mouth_Smile_Wide_Open_With_A_Long_Name";

        GestureFacesDraft Busy()
        {
            TrackingParameter();
            avatar.AddBodyShapes(LongJapaneseShape, LongShape);
            var draft = GestureFaces.Read(menu);
            draft.fistStrength = true;
            draft.rows[0].shapes.Add(new GestureShape { renderer = avatar.Body, shape = "Blush", weight = 100f });
            draft.rows[0].shapes.Add(new GestureShape { renderer = avatar.Body, shape = LongJapaneseShape, weight = 60f });
            draft.rows[3].shapes.Add(new GestureShape { renderer = avatar.Body, shape = "Eye_Big", weight = 45f });
            draft.rows[3].shapes.Add(new GestureShape { renderer = avatar.Body, shape = LongShape, weight = 80f });
            draft.rows[3].hand = GestureHand.Left;
            draft.rows.Add(new GestureFaceRow { gesture = 4, hand = GestureHand.Left, shapes = { new GestureShape { renderer = avatar.Body, shape = "Blush", weight = 10f } } });
            avatar.Body.SetBlendShapeWeight(0, 30f);
            return draft;
        }

        [Test]
        public void EveryLabelledControlOfTheWindowHasATooltip()
        {
            var window = Window(Busy());
            try
            {
                var root = window.rootVisualElement;
                Assert.That(root.Q("ad-gestures-overlap"), Is.Not.Null, "sanity: the overlap warning shows");
                var missing = root.Query<VisualElement>().ToList()
                    .Where(e => e is Button || e is Toggle || e is TextField || e is Slider || e is SegmentedChoice || e.ClassListContains("ad-label"))
                    .Where(e => !InsideAField(e) && !(e.parent is SegmentedChoice) && string.IsNullOrEmpty(e.tooltip))
                    .Select(e => e.name + " " + e.GetType().Name).ToList();
                Assert.That(missing, Is.Empty);
                foreach (var choice in root.Query<SegmentedChoice>().ToList())
                    Assert.That(choice.Items.All(i => !string.IsNullOrEmpty(i.tooltip)), Is.True, choice.name + ": every choice says what it does");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void TheWindowFitsAt300And400InEveryLanguage()
        {
            var draft = Busy();
            var allow = new PanelAudit.AllowList();
            foreach (var language in new[] { "en-US", "ja-JP", "zh-Hans" })
            foreach (float width in new[] { 300f, 400f })
            using (L.UseTestLanguage(language, null))
            {
                var window = Window(draft);
                try
                {
                    var root = window.rootVisualElement;
                    LayoutProbe.Measure(root, () =>
                    {
                        LayoutProbe.Validate(root);
                        PanelAudit.Audit(root, root.worldBound.xMax, allow);
                        var texts = root.Query<TextElement>().ToList()
                            .Where(t => t.resolvedStyle.display == DisplayStyle.Flex && !string.IsNullOrEmpty(t.text) && t.resolvedStyle.whiteSpace != WhiteSpace.Normal);
                        foreach (var text in texts)
                        {
                            var size = text.MeasureTextSize(text.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                            Assert.That(text.contentRect.width, Is.GreaterThanOrEqualTo(size.x - 1f), language + " at " + width + ": \"" + text.text + "\" is cut short");
                        }
                        foreach (var name in root.Query<Label>("ad-gestures-shape-name").ToList())
                        {
                            Assert.That(name.worldBound.xMax, Is.LessThanOrEqualTo(root.worldBound.xMax + 0.5f), language + " at " + width + ": the shape name \"" + name.text + "\" stays inside the window");
                            Assert.That(name.resolvedStyle.textOverflow, Is.Not.EqualTo(TextOverflow.Ellipsis), "the whole name shows, wrapped when it is long");
                        }
                    }, width);
                }
                finally
                {
                    Object.DestroyImmediate(window);
                }
            }
        }
    }
}
#endif
