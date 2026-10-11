#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The creator's own complaint that delete "takes very long". Measured against a
    /// component with 150 entries, 40 of them carrying 30 blend shape targets each: Undo.RecordObject was not the
    /// cost, already under a microsecond a call and actually faster than Undo.RegisterCompleteObjectUndo on this
    /// object; DirectFields.Reload was, its field lookups each resolving a full property path back through a
    /// 150 entry array on every one of a row's several bound fields. The guard below is a real, generous bound
    /// against that regression, not the proof itself: the report carries the measured before and after numbers.
    /// </summary>
    public class PerformanceTests
    {
        TestAvatar avatar;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        [Test]
        public void DeleteOnALargeComponentIsFast()
        {
            var shapeNames = Enumerable.Range(0, 30).Select(i => "Shape" + i).ToArray();
            avatar.AddBodyShapes(shapeNames);
            var menu = avatar.AddMenu();
            MenuEntry target = null;
            for (int i = 0; i < 150; i++)
            {
                var entry = menu.AddEntry(EntryKind.Toggle, "Entry" + i);
                entry.objects.Add(new ObjectTarget { target = avatar.Hat });
                if (i < 40)
                    foreach (var shape in shapeNames)
                        entry.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = shape, minWeight = 0f, maxWeight = 100f });
                if (i == 75) target = entry;
            }
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            view.Tree.SelectEntry(target.id);
            view.Refresh();

            var sw = Stopwatch.StartNew();
            view.Tree.DeleteSelected();
            sw.Stop();

            Assert.That(sw.Elapsed.TotalMilliseconds, Is.LessThan(250),
                "delete on a large component must stay fast: took " + sw.Elapsed.TotalMilliseconds.ToString("F1") + "ms");
        }

        /// <summary>
        /// The creator's real avatar carries a 62 layer FX controller, and a second Refresh()
        /// right after the first, with nothing invalidated, still cost over a second on it. AvatarPlanCache.GetFor
        /// already returns the same cached plan on such a call, so the fix was never here: it was the AssetDatabase
        /// work (the stylesheet's project wide search, a toolbar icon lookup on every new view) that used to run
        /// once per view instead of once per domain reload. This guard builds a controller of the same shape (60
        /// layers, 3 states and 2 clips each) and asserts a second refresh stays under 50ms and never re-walks a
        /// single controller: ParameterUsage's and LayerAnalysis's own build counters, and RemovalPlanner.PlanCalls,
        /// must not move between the two calls.
        /// </summary>
        [Test]
        public void ASecondRefreshWithNothingInvalidatedIsCheap()
        {
            var clipA = new AnimationClip { name = "ClipA" };
            var clipB = new AnimationClip { name = "ClipB" };
            try
            {
                for (int i = 0; i < 60; i++)
                {
                    var parameter = "Layer" + i + "Param";
                    avatar.Fx.AddParameter(parameter, AnimatorControllerParameterType.Float);
                    avatar.Fx.AddLayer("Layer" + i);
                    var stateMachine = avatar.Fx.layers[avatar.Fx.layers.Length - 1].stateMachine;
                    var s1 = stateMachine.AddState("S1"); s1.motion = clipA; s1.writeDefaultValues = false;
                    var s2 = stateMachine.AddState("S2"); s2.motion = clipB; s2.writeDefaultValues = false;
                    var s3 = stateMachine.AddState("S3"); s3.motion = clipA; s3.writeDefaultValues = false;
                    var forward = s1.AddTransition(s2);
                    forward.hasExitTime = false;
                    forward.AddCondition(AnimatorConditionMode.Greater, 0.5f, parameter);
                    var back = s2.AddTransition(s3);
                    back.hasExitTime = false;
                    back.AddCondition(AnimatorConditionMode.Less, 0.5f, parameter);
                }

                var menu = avatar.AddMenu();
                menu.AddEntry(EntryKind.Toggle, "Only entry");
                var view = new AvatarDoctorView(new SerializedObject(menu), menu);
                view.Refresh();

                int wideBefore = ParameterUsage.AvatarWideBuilds;
                int layerBefore = LayerAnalysis.LayerReadBuilds;
                int planBefore = RemovalPlanner.PlanCalls;

                var sw = Stopwatch.StartNew();
                view.Refresh();
                sw.Stop();

                Assert.That(sw.Elapsed.TotalMilliseconds, Is.LessThan(50),
                    "a second refresh with nothing invalidated must stay cheap even with a 60 layer FX controller: took " + sw.Elapsed.TotalMilliseconds.ToString("F1") + "ms");
                Assert.That(ParameterUsage.AvatarWideBuilds, Is.EqualTo(wideBefore), "a second refresh must not re-walk the avatar's parameter readers");
                Assert.That(LayerAnalysis.LayerReadBuilds, Is.EqualTo(layerBefore), "a second refresh must not re-walk the FX controller's layers");
                Assert.That(RemovalPlanner.PlanCalls, Is.EqualTo(planBefore), "a second refresh must not rebuild the removal plan");
            }
            finally
            {
                Object.DestroyImmediate(clipA);
                Object.DestroyImmediate(clipB);
            }
        }

        /// <summary>A plain two state layer switched by one bool, at full weight, so a removal can take it out.</summary>
        static void AddBoolLayer(AnimatorController controller, string layerName, string parameter)
        {
            controller.AddParameter(parameter, AnimatorControllerParameterType.Bool);
            controller.AddLayer(layerName);
            var layers = controller.layers;
            layers[layers.Length - 1].defaultWeight = 1f;
            controller.layers = layers;
            var machine = controller.layers[controller.layers.Length - 1].stateMachine;
            var off = machine.AddState("Off");
            var on = machine.AddState("On");
            machine.defaultState = off;
            var toOn = off.AddTransition(on);
            toOn.hasExitTime = false;
            toOn.AddCondition(AnimatorConditionMode.If, 0f, parameter);
            var toOff = on.AddTransition(off);
            toOff.hasExitTime = false;
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0f, parameter);
        }

        static void ForgetAnimatorUndo(AnimatorController controller)
        {
            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine == null) continue;
                foreach (var child in layer.stateMachine.states)
                {
                    if (child.state == null) continue;
                    foreach (var transition in child.state.transitions) if (transition != null) Undo.ClearUndo(transition);
                    Undo.ClearUndo(child.state);
                }
                Undo.ClearUndo(layer.stateMachine);
            }
            Undo.ClearUndo(controller);
        }

        /// <summary>
        /// Every plan build asked for the Write Defaults decision and, with a removal edit, whether a
        /// layer control targets FX; both walked every state of the controller. They are kept per controller and
        /// dirty count until the controller reads are dropped.
        /// </summary>
        [Test]
        public void AMenuEditDoesNotWalkTheControllerStatesAgain()
        {
            avatar.ExpressionsMenu.controls.Add(TestAvatar.ToggleControl("Hat", "HatParam"));
            var parameters = avatar.ExpressionParameters.parameters.ToList();
            parameters.Add(new VRCExpressionParameters.Parameter { name = "HatParam", valueType = VRCExpressionParameters.ValueType.Bool, saved = true, networkSynced = true });
            avatar.ExpressionParameters.parameters = parameters.ToArray();
            AddBoolLayer(avatar.Fx, "HatLayer", "HatParam");
            var menu = avatar.AddMenu();
            menu.menuEdits.Add(new MenuEdit { id = "hat-remove", source = new List<string> { "Hat" }, remove = true });
            try
            {
                AvatarPlanCache.Invalidate();
                AvatarPlanCache.GetFor(menu);
                int writeDefaults = AnimatorBuilder.WriteDefaultsReads;
                int layerControl = LayerAnalysis.LayerControlReads;

                AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
                AvatarPlanCache.GetFor(menu);

                Assert.That(AnimatorBuilder.WriteDefaultsReads, Is.EqualTo(writeDefaults), "a menu edit replans without walking the FX states for Write Defaults");
                Assert.That(LayerAnalysis.LayerControlReads, Is.EqualTo(layerControl), "or for a layer control");

                EditorUtility.SetDirty(avatar.Fx);
                AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
                AvatarPlanCache.GetFor(menu);

                Assert.That(AnimatorBuilder.WriteDefaultsReads, Is.EqualTo(writeDefaults + 1), "an edited controller is read again");
                Assert.That(LayerAnalysis.LayerControlReads, Is.GreaterThan(layerControl));

                writeDefaults = AnimatorBuilder.WriteDefaultsReads;
                AvatarPlanCache.Invalidate();
                AvatarPlanCache.GetFor(menu);

                Assert.That(AnimatorBuilder.WriteDefaultsReads, Is.EqualTo(writeDefaults + 1), "and so is one after the controller reads were dropped");
            }
            finally
            {
                ForgetAnimatorUndo(avatar.Fx);
            }
        }

        /// <summary>
        /// The creator's refresh took 428 ms, 423 of them in the plan, on a 62 layer FX with removal
        /// edits. AnimatorController.layers builds a new array on every read, and the layer reads asked for it about
        /// twice per layer inside a loop over every layer, so a cold read grew with the square of the layer count.
        /// A cold plan of the same shape (60 layers, three removed toggles) has to stay well under that.
        /// </summary>
        [Test]
        public void AColdPlanOfASixtyLayerControllerWithRemovalsIsFast()
        {
            var parameters = avatar.ExpressionParameters.parameters.ToList();
            for (int i = 0; i < 60; i++)
            {
                AddBoolLayer(avatar.Fx, "Layer" + i, "P" + i);
                parameters.Add(new VRCExpressionParameters.Parameter { name = "P" + i, valueType = VRCExpressionParameters.ValueType.Bool, saved = true, networkSynced = true });
            }
            avatar.ExpressionParameters.parameters = parameters.ToArray();
            for (int i = 0; i < 6; i++) avatar.ExpressionsMenu.controls.Add(TestAvatar.ToggleControl("Toggle" + i, "P" + i));
            var menu = avatar.AddMenu();
            for (int i = 0; i < 3; i++) menu.menuEdits.Add(new MenuEdit { id = "remove-" + i, source = new List<string> { "Toggle" + i }, remove = true });
            var layerReadsOf = typeof(LayerAnalysis).GetMethod("LayerReadsOf", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            try
            {
                // An untimed plan first, so the timed one never pays for code compiled on its first use.
                AvatarPlanCache.GetFor(menu);
                AvatarPlanCache.Invalidate();
                var planClock = Stopwatch.StartNew();
                var plan = AvatarPlanCache.GetFor(menu);
                planClock.Stop();

                AvatarPlanCache.Invalidate();
                var readClock = Stopwatch.StartNew();
                layerReadsOf.Invoke(null, new object[] { avatar.Fx, false });
                readClock.Stop();

                Assert.That(plan.Tree.Removal.Layers, Is.EqualTo(new[] { "Layer0", "Layer1", "Layer2" }), "the plan really read every layer: the three removed toggles take their layers with them");
                Assert.That(readClock.Elapsed.TotalMilliseconds, Is.LessThan(60),
                    "a cold read of a 60 layer controller: took " + readClock.Elapsed.TotalMilliseconds.ToString("F1") + " ms (plan " + planClock.Elapsed.TotalMilliseconds.ToString("F1") + " ms)");
                Assert.That(planClock.Elapsed.TotalMilliseconds, Is.LessThan(120),
                    "a cold plan of an avatar with a 60 layer controller and removal edits: took " + planClock.Elapsed.TotalMilliseconds.ToString("F1") + " ms (layer read " + readClock.Elapsed.TotalMilliseconds.ToString("F1") + " ms)");
            }
            finally
            {
                ForgetAnimatorUndo(avatar.Fx);
            }
        }
    }
}
#endif
