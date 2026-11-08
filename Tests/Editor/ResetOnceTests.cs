#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using Object = UnityEngine.Object;
#if DOCME_MA
using nadena.dev.modular_avatar.core;
using VRC.SDK3.Avatars.ScriptableObjects;
#endif

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// A layer that pins a face shape writes nothing for it while it is inactive. Leaving the active side goes through
    /// a one frame Reset state that writes the rest value once, then the idle state, which writes nothing, so face
    /// tracking and blinking keep the shape whatever the release rule guessed about who owns it.
    /// </summary>
    public class ResetOnceTests
    {
        TestAvatar avatar;
        AvatarDoc menu;
        readonly List<Object> made = new List<Object>();

        const float Frame = 1f / 60f;

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
            foreach (var obj in made)
                if (obj != null) Undo.ClearUndo(obj);
            avatar.Dispose();
            foreach (var obj in made)
                if (obj != null && !AssetDatabase.Contains(obj)) Object.DestroyImmediate(obj);
            made.Clear();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
            GeneratedAssets.CleanUp();
        }

        static AnimatorController FxOf(TestAvatar a)
            => (AnimatorController)a.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;

        static AnimatorControllerLayer Layer(AnimatorController fx, string name) => fx.layers.Single(l => l.name == name);
        static AnimatorState State(AnimatorControllerLayer layer, string name) => layer.stateMachine.states.Single(s => s.state.name == name).state;
        static string[] StateNames(AnimatorControllerLayer layer) => layer.stateMachine.states.Select(s => s.state.name).ToArray();

        static AnimationCurve Shape(Motion motion, string shape, string path = "Body")
            => AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + shape));

        static List<string> Shapes(Motion motion, string path)
            => AnimationUtility.GetCurveBindings((AnimationClip)motion)
                .Where(b => b.path == path && b.type == typeof(SkinnedMeshRenderer) && b.propertyName.StartsWith("blendShape."))
                .Select(b => b.propertyName.Substring("blendShape.".Length)).ToList();

        static void NoErrors(List<ErrorContext> errors)
            => Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error).Select(e => (e.TheError as SimpleError)?.TitleKey), Is.Empty);

        /// <summary>The whole build the way NDMF runs it: AvatarDoc's own pass, then the final pass the Optimizing phase runs after every other tool.</summary>
        AnimatorController BuildAll(out List<ErrorContext> errors, System.Action<VirtualAnimatorController> betweenPasses = null)
        {
            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            if (betweenPasses != null)
            {
                var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
                asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx);
                betweenPasses(fx);
                context.DeactivateAllExtensionContexts();
            }
            errors.AddRange(ErrorReport.CaptureErrors(() => AvatarDoctorPlugin.FinishAfterOtherTools(context)));
            return FxOf(avatar);
        }

        /// <summary>
        /// The way back of one layer: every way out of its active states leads to Reset and nowhere else, Reset lasts 1/60
        /// of a second and then falls through to the idle state, and nothing but Reset leads to the idle state. The layer
        /// starts in Load, which plays Reset's clip and passes to Reset, so the rests are written at load too. Returns Reset.
        /// </summary>
        static AnimatorState AssertResetsOnce(AnimatorControllerLayer layer, string idleName, params string[] activeNames)
        {
            var idle = State(layer, idleName);
            var reset = State(layer, "Reset");
            var load = State(layer, "Load");
            Assert.That(layer.stateMachine.defaultState, Is.SameAs(load), layer.name + ": starts in Load, so the avatar loads with the rests written once");
            var start = load.transitions.Single();
            Assert.That(start.destinationState, Is.SameAs(reset), "Load passes to Reset");
            Assert.That(start.hasExitTime, Is.True);
            Assert.That(start.exitTime, Is.EqualTo(1f), "after 1/60 of a second, the way Reset leaves");
            Assert.That(start.duration, Is.EqualTo(0f));
            Assert.That(start.conditions, Is.Empty);
            Assert.That(load.motion, Is.SameAs(reset.motion), "Load plays Reset's own clip, so the first frame writes the rests whichever of the two it samples");
            Assert.That(load.writeDefaultValues, Is.False);
            Assert.That(layer.stateMachine.anyStateTransitions, Is.Empty, layer.name + ": nothing jumps from Any State, so every way out passes Reset");
            foreach (var activeName in activeNames)
            {
                var active = State(layer, activeName);
                var ways = active.transitions.Where(t => !activeNames.Contains(t.destinationState.name)).ToList();
                Assert.That(ways, Is.Not.Empty, layer.name + " " + activeName + ": has a way out");
                Assert.That(ways.Select(t => t.destinationState.name), Is.All.EqualTo("Reset"), layer.name + " " + activeName + ": every way out goes to Reset, never straight to " + idleName);
            }
            var back = reset.transitions.Single();
            Assert.That(back.destinationState, Is.SameAs(idle), "Reset falls through to " + idleName);
            Assert.That(back.hasExitTime, Is.True, "Reset leaves by exit time");
            Assert.That(back.exitTime, Is.EqualTo(1f));
            Assert.That(back.duration, Is.EqualTo(0f));
            Assert.That(back.conditions, Is.Empty, "with no condition, so it always leaves");
            Assert.That(reset.writeDefaultValues, Is.False);
            Assert.That(((AnimationClip)reset.motion).length, Is.EqualTo(Frame).Within(0.00001f), "one frame long");
            foreach (var child in layer.stateMachine.states)
            {
                // Idle entering itself again is how a faded layer sets its weight to 0 again; it leads nowhere new.
                if (child.state == reset || child.state == idle) continue;
                Assert.That(child.state.transitions.Where(t => t.destinationState == idle), Is.Empty, child.state.name + ": only Reset leads back to " + idleName);
            }
            return reset;
        }

        // ---- the creator's avatar in small ----------------------------------------------------------------------

        /// <summary>Gives every layer of the avatar's own FX full weight, the way a real controller sets its layers.</summary>
        void FullWeights()
        {
            var layers = avatar.Fx.layers;
            foreach (var layer in layers) layer.defaultWeight = 1f;
            avatar.Fx.layers = layers;
        }

        AnimatorStateMachine AddFxLayer(string name)
        {
            avatar.Fx.AddLayer(name);
            var machine = avatar.Fx.layers[avatar.Fx.layers.Length - 1].stateMachine;
            made.Add(machine);
            return machine;
        }

        AnimatorState AddState(AnimatorStateMachine machine, string name, Motion motion, bool writeDefaults)
        {
            var state = machine.AddState(name);
            made.Add(state);
            state.motion = motion;
            state.writeDefaultValues = writeDefaults;
            return state;
        }

        /// <summary>
        /// A face tracking layer the way OSCmooth makes them: a Direct tree, Write Defaults on, its child holding the
        /// shape at value through a parameter at weight 1, and a layer control in another layer that can fade it. Its
        /// ownership guess fails for that control, as on the creator's avatar.
        /// </summary>
        void FaceTrackingUnderLayerControl(string shape, float value)
        {
            var parameter = "FT/" + shape;
            avatar.Fx.AddParameter(new AnimatorControllerParameter { name = parameter, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            var clip = TestAvatar.ShapeClip("FT " + shape, "Body", shape, value, value);
            made.Add(clip);
            var tree = new BlendTree { name = "FT " + shape, blendType = BlendTreeType.Direct };
            made.Add(tree);
            tree.children = new[] { new ChildMotion { motion = clip, directBlendParameter = parameter, timeScale = 1f } };
            var tracking = AddFxLayer("FT " + shape);
            tracking.defaultState = AddState(tracking, "Tracking", tree, true);
            int trackingIndex = avatar.Fx.layers.Length - 1;

            var switcher = AddFxLayer("FT Switch");
            switcher.defaultState = AddState(switcher, "On", null, false);
            var off = AddState(switcher, "Off", null, false);
            var control = off.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
            made.Add(control);
            control.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
            control.layer = trackingIndex;
            control.goalWeight = 0f;
            FullWeights();
        }

        /// <summary>A blink loop the way a VRCFury blink setup builds one: an idle state with no motion, a blink state that animates the shape, both Write Defaults off, looping by exit time. No ownership clause accepts it.</summary>
        void BlinkLoop(string shape)
        {
            var blinkClip = TestAvatar.ShapeClip("Blink " + shape, "Body", shape, 0f, 100f);
            made.Add(blinkClip);
            var machine = AddFxLayer("Blink");
            var idle = AddState(machine, "Idle", null, false);
            var blink = AddState(machine, "Blink", blinkClip, false);
            machine.defaultState = idle;
            var toBlink = idle.AddTransition(blink);
            made.Add(toBlink);
            toBlink.hasExitTime = true;
            toBlink.exitTime = 4f;
            toBlink.duration = 0f;
            var toIdle = blink.AddTransition(idle);
            made.Add(toIdle);
            toIdle.hasExitTime = true;
            toIdle.exitTime = 1f;
            toIdle.duration = 0f;
            FullWeights();
        }

        MenuEntry Headpat(string shape)
        {
            var pat = menu.AddEntry(EntryKind.Trigger, "Headpat");
            pat.source = TriggerSource.Contact;
            pat.contactAnchor = avatar.Hat.transform;
            pat.contactTags = new List<string> { "Hand" };
            pat.touchMode = TouchMode.FollowDistance;
            pat.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = shape, weight = 100f, mode = ShapeEffectMode.Pin });
            return pat;
        }

        [Test]
        public void AFollowDistancePinLeavesFaceTrackingAndBlinkingAloneWhileNoHandIsNear()
        {
            FaceTrackingUnderLayerControl("Blush", 70f);
            BlinkLoop("Blush");
            avatar.Body.SetBlendShapeWeight(0, 5f);
            Headpat("Blush");

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Headpat (distance)");
            Assert.That(Shape(State(layer, "Far").motion, "Blush"), Is.Null,
                "Far writes nothing for Blush: from the last FX layer a rest curve here held the shape over face tracking and blinking on every frame no hand was near");
            var reset = AssertResetsOnce(layer, "Far", "Near");
            Assert.That(Shape(reset.motion, "Blush").Evaluate(0f), Is.EqualTo(5f), "the shape goes back to its rest value once, on the way out");
        }

        // ---- every layer that pins a shape ----------------------------------------------------------------------

        [Test]
        public void APinnedToggleResetsOnceThenLetsGo()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            avatar.Body.SetBlendShapeWeight(0, 22f);

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Face (pin)");
            Assert.That(Shape(State(layer, "Off").motion, "Blush"), Is.Null, "Off writes nothing for the pinned shape");
            Assert.That(Shape(State(layer, "On").motion, "Blush").Evaluate(0f), Is.EqualTo(100f));
            var reset = AssertResetsOnce(layer, "Off", "On");
            Assert.That(Shape(reset.motion, "Blush").Evaluate(0f), Is.EqualTo(22f));
            Assert.That(State(layer, "Off").transitions.Single(t => t.destinationState.name != "Off").destinationState.name, Is.EqualTo("On"), "Off still goes straight to On");
        }

        [Test]
        public void AHoldContactPinResetsOnceThenLetsGo()
        {
            var pat = menu.AddEntry(EntryKind.Trigger, "Pat");
            pat.source = TriggerSource.Contact;
            pat.contactAnchor = avatar.Hat.transform;
            pat.contactTags = new List<string> { "Hand" };
            pat.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            avatar.Body.SetBlendShapeWeight(0, 22f);

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Pat (pin)");
            Assert.That(Shape(State(layer, "Off").motion, "Blush"), Is.Null);
            var reset = AssertResetsOnce(layer, "Off", "On");
            Assert.That(Shape(reset.motion, "Blush").Evaluate(0f), Is.EqualTo(22f));
        }

        [Test]
        public void AFlipTriggersToggleResetsOnceThenLetsGo()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            var flip = menu.AddEntry(EntryKind.Trigger, "Boop");
            flip.source = TriggerSource.Contact;
            flip.contactAnchor = avatar.Hat.transform;
            flip.contactTags = new List<string> { "Hand" };
            flip.touchMode = TouchMode.FlipToggle;
            flip.flipToggleId = face.id;
            avatar.Body.SetBlendShapeWeight(0, 22f);

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var flipLayer = Layer(fx, "AD: Boop (flip)");
            foreach (var child in flipLayer.stateMachine.states)
                Assert.That(Shapes(child.state.motion, "Body"), Is.Empty, child.state.name + ": the flip layer only drives the toggle's parameter, it holds no shape");
            var layer = Layer(fx, "AD: Face (pin)");
            Assert.That(Shape(State(layer, "Off").motion, "Blush"), Is.Null);
            var reset = AssertResetsOnce(layer, "Off", "On");
            Assert.That(Shape(reset.motion, "Blush").Evaluate(0f), Is.EqualTo(22f));
        }

        MenuEntry Link()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var link = menu.AddEntry(EntryKind.Trigger, "Blush link");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Fixed, weight = 80f });
            return link;
        }

        [Test]
        public void ASmoothBlendShapeTriggerResetsOnceThenLetsGo()
        {
            Link();
            avatar.Body.SetBlendShapeWeight(1, 12f);

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Blush link (shape)");
            Assert.That(Shape(State(layer, "Far").motion, "Eye_Big"), Is.Null, "the resting state writes nothing for the shape it moves");
            var reset = AssertResetsOnce(layer, "Far", "Near");
            Assert.That(Shape(reset.motion, "Eye_Big").Evaluate(0f), Is.EqualTo(12f));
        }

        [Test]
        public void ABlendShapeTriggerWithAnObjectResetsOnceFromBothDrivenStates()
        {
            var link = Link();
            link.sourceThreshold = 50f;
            link.objects.Add(new ObjectTarget { target = avatar.Glasses, activeWhenOn = true });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Blush link (shape)");
            Assert.That(StateNames(layer), Is.EquivalentTo(new[] { "Far", "Near", "Near on", "Reset", "Load" }));
            Assert.That(Shape(State(layer, "Far").motion, "Eye_Big"), Is.Null);
            var reset = AssertResetsOnce(layer, "Far", "Near", "Near on");
            Assert.That(Shape(reset.motion, "Eye_Big").Evaluate(0f), Is.EqualTo(0f));
            Assert.That(AnimationUtility.GetEditorCurve((AnimationClip)State(layer, "Far").motion, EditorCurveBinding.FloatCurve("Glasses", typeof(GameObject), "m_IsActive")).Evaluate(0f),
                Is.EqualTo(0f), "an object is not a face shape: the resting state still switches it off itself");
        }

        [Test]
        public void AnExclusiveGroupGoesThroughResetBetweenMembers()
        {
            var a = menu.AddEntry(EntryKind.Toggle, "A");
            a.exclusiveGroup = "G";
            a.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            var b = menu.AddEntry(EntryKind.Toggle, "B");
            b.exclusiveGroup = "G";
            b.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 100f, mode = ShapeEffectMode.Pin });
            avatar.Body.SetBlendShapeWeight(0, 5f);
            avatar.Body.SetBlendShapeWeight(1, 7f);

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: G (pin)");
            var off = State(layer, "Off");
            var reset = AssertResetsOnce(layer, "Off", "A", "B");
            Assert.That(Shapes(off.motion, "Body"), Is.Empty, "Off writes no shape");
            Assert.That(Shapes(State(layer, "A").motion, "Body"), Is.EqualTo(new[] { "Blush" }), "a member writes only its own pins");
            Assert.That(Shapes(State(layer, "B").motion, "Body"), Is.EqualTo(new[] { "Eye_Big" }));
            Assert.That(Shape(reset.motion, "Blush").Evaluate(0f), Is.EqualTo(5f));
            Assert.That(Shape(reset.motion, "Eye_Big").Evaluate(0f), Is.EqualTo(7f));
            Assert.That(off.transitions.Where(t => t.destinationState != off).Select(t => t.destinationState.name), Is.EquivalentTo(new[] { "A", "B" }), "Off goes to the member chosen");
            foreach (var name in new[] { "A", "B" })
            {
                var member = State(layer, name);
                var enter = off.transitions.Single(t => t.destinationState == member).conditions.Single();
                var leave = member.transitions.Single().conditions.Single();
                Assert.That(enter.mode, Is.EqualTo(AnimatorConditionMode.Equals), name + " is entered when it is chosen");
                Assert.That(leave.mode, Is.EqualTo(AnimatorConditionMode.NotEqual), name + " is left as soon as it is not");
                Assert.That(leave.parameter, Is.EqualTo(enter.parameter));
                Assert.That(leave.threshold, Is.EqualTo(enter.threshold));
            }
        }

        [Test]
        public void AHoldTheRestRuleLeavesItsPrunedShapesOutOfResetToo()
        {
            var go = new GameObject("Face");
            go.transform.SetParent(avatar.Root.transform);
            var faceMesh = go.AddComponent<SkinnedMeshRenderer>();
            faceMesh.sharedMesh = TestAvatar.QuadWithShapes("A", "D");
            made.Add(faceMesh.sharedMesh);
            var other = menu.AddEntry(EntryKind.Toggle, "Other");
            other.blendShapes.Add(new BlendShapeTarget { renderer = faceMesh, shape = "A", weight = 100f });
            var holder = menu.AddEntry(EntryKind.Toggle, "Holder");
            holder.shapeSets.Add(new ShapeSetRule { mesh = faceMesh, zeroOthers = true, value = ShapeSetValue.Default });
            holder.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            avatar.Body.SetBlendShapeWeight(0, 22f);

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Holder (pin)");
            Assert.That(Shapes(State(layer, "On").motion, "Face"), Is.EqualTo(new[] { "A" }), "D moves nowhere else, so the rule drops it; Other moves A, so the rule holds it");
            var reset = AssertResetsOnce(layer, "Off", "On");
            Assert.That(Shapes(reset.motion, "Face"), Does.Not.Contain("D"), "the dropped shape gets no reset either");
            Assert.That(Shapes(reset.motion, "Face"), Does.Contain("A"), "Other's own layer owns A, and the pin still rests it once on the way out");
            Assert.That(Shape(reset.motion, "Blush").Evaluate(0f), Is.EqualTo(22f), "the Pin row still resets once");
            Assert.That(Shapes(State(layer, "Off").motion, "Face"), Is.Empty);
        }

        [Test]
        public void AHoldTriggersPropertyResetsOnceInItsOwnLayer()
        {
            avatar.MaterialA.SetFloat("_Glossiness", 0.2f);
            var source = menu.AddEntry(EntryKind.Toggle, "Source");
            source.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var glow = menu.AddEntry(EntryKind.Trigger, "Glow");
            glow.source = TriggerSource.Toggle;
            glow.sourceToggleId = source.id;
            glow.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 0.9f });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Glow");
            var gloss = EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "material._Glossiness");
            Assert.That(AnimationUtility.GetEditorCurve((AnimationClip)State(layer, "Off").motion, gloss), Is.Null, "Off writes nothing for the property");
            var reset = AssertResetsOnce(layer, "Off", "On");
            Assert.That(AnimationUtility.GetEditorCurve((AnimationClip)reset.motion, gloss).Evaluate(0f), Is.EqualTo(0.2f).Within(0.0001f));
        }

        MenuEntry Glow(float offValue = -1f)
        {
            avatar.MaterialA.SetFloat("_Glossiness", 0.2f);
            var source = menu.AddEntry(EntryKind.Toggle, "Source");
            source.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var glow = menu.AddEntry(EntryKind.Trigger, "Glow");
            glow.source = TriggerSource.Toggle;
            glow.sourceToggleId = source.id;
            var row = new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 0.9f };
            if (offValue >= 0f)
            {
                row.explicitOff = true;
                row.minValue = offValue;
            }
            glow.properties.Add(row);
            return glow;
        }

        [Test]
        public void AHoldTriggersLayerStillOwnsItsFixedShapeWithItsResetAndLoad()
        {
            Glow().blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 60f, mode = ShapeEffectMode.Pin });
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 100f, mode = ShapeEffectMode.Pin });
            avatar.Body.SetBlendShapeWeight(0, 5f);
            avatar.Body.SetBlendShapeWeight(1, 7f);

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var main = Layer(fx, "AD: Glow");
            Assert.That(StateNames(main), Is.EquivalentTo(new[] { "Off", "On", "Reset", "Load" }), "sanity: the trigger's layer rests its property through Reset and Load");
            Assert.That(Shape(State(main, "Off").motion, "Blush").Evaluate(0f), Is.EqualTo(5f), "sanity: Off and On both write the Fixed shape");
            Assert.That(Shape(State(main, "On").motion, "Blush").Evaluate(0f), Is.EqualTo(100f));
            var pin = Layer(fx, "AD: Face (pin)");
            var reset = AssertResetsOnce(pin, "Off", "On");
            Assert.That(Shape(reset.motion, "Blush").Evaluate(0f), Is.EqualTo(5f),
                "Glow's layer owns Blush, and the pin still rests it once, so no copy of the avatar keeps the pinned 60");
            Assert.That(Shape(State(pin, "Load").motion, "Blush").Evaluate(0f), Is.EqualTo(5f), "and at load");
            Assert.That(Shape(reset.motion, "Eye_Big").Evaluate(0f), Is.EqualTo(7f), "sanity: the pin still resets what nobody owns");
        }

        [Test]
        public void AHoldTriggersLayerTurnedToWriteDefaultsOnBetweenThePassesKeepsItsOffValue()
        {
            Glow(offValue: 0.4f);

            // What VRCFury's Write Defaults fixer can do between AvatarDoc's two passes: every state of the trigger's
            // own layer, an ordinary layer the final pass leaves as it finds it, turned Write Defaults on.
            var fx = BuildAll(out var errors, virtualFx =>
            {
                foreach (var layer in virtualFx.Layers.Where(l => l.Name == "AD: Glow"))
                    foreach (var state in layer.StateMachine.AllStates())
                        state.WriteDefaultValues = true;
            });

            NoErrors(errors);
            var main = Layer(fx, "AD: Glow");
            var off = State(main, "Off");
            Assert.That(off.writeDefaultValues, Is.True, "sanity: the final pass leaves an ordinary layer's Write Defaults alone");
            var gloss = EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "material._Glossiness");
            var offCurve = AnimationUtility.GetEditorCurve((AnimationClip)off.motion, gloss);
            Assert.That(offCurve, Is.Not.Null, "Off now writes the property on every frame anyway, so it carries the rest value itself");
            Assert.That(offCurve.Evaluate(0f), Is.EqualTo(0.4f).Within(0.0001f));
            var sourceOn = off.transitions.First().conditions.First();
            using (var player = new Player(avatar.Root, fx, avatar.Body))
            {
                player.Settled("Blush");
                Assert.That(player.Property("_Glossiness"), Is.EqualTo(0.4f).Within(0.0001f), "resting at load: the row's off value");
                player.Set(sourceOn.parameter, 1f);
                player.Settled("Blush");
                Assert.That(player.Property("_Glossiness"), Is.EqualTo(0.9f).Within(0.0001f));
                player.Set(sourceOn.parameter, 0f);
                player.Play(30, "Blush");
                Assert.That(player.Property("_Glossiness"), Is.EqualTo(0.4f).Within(0.0001f), "and the off value stays while the trigger rests");
            }
        }

        // ---- an owner that really holds the shape ---------------------------------------------------------------

        void DirectFaceTracking(string shape, float value)
        {
            var parameter = "FT/" + shape;
            avatar.Fx.AddParameter(new AnimatorControllerParameter { name = parameter, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            var clip = TestAvatar.ShapeClip("FT " + shape, "Body", shape, value, value);
            made.Add(clip);
            var tree = new BlendTree { name = "FT " + shape, blendType = BlendTreeType.Direct };
            made.Add(tree);
            tree.children = new[] { new ChildMotion { motion = clip, directBlendParameter = parameter, timeScale = 1f } };
            var tracking = AddFxLayer("FT " + shape);
            tracking.defaultState = AddState(tracking, "Tracking", tree, true);
            AddState(tracking, "Disabled", null, false);
            FullWeights();
        }

        [Test]
        public void AShapeFaceTrackingOwnsStillRestsOnce()
        {
            DirectFaceTracking("Blush", 40f);
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 100f, mode = ShapeEffectMode.Pin });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Face (pin)");
            var reset = AssertResetsOnce(layer, "Off", "On");
            Assert.That(AnimatorBuilder.LastOwnedByAnotherLayer.Contains(EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blush")), Is.True, "sanity: face tracking's Direct tree owns Blush");
            Assert.That(Shape(reset.motion, "Blush"), Is.Not.Null, "face tracking's Direct tree owns Blush, and the pin still rests it once: on another player's copy face tracking may not write it again");
            Assert.That(Shape(reset.motion, "Eye_Big"), Is.Not.Null, "Eye_Big has no owner, so it resets once as always");
        }

        [Test]
        public void ALayerWithNothingToResetGoesStraightBack()
        {
            // A rule whose only shape moves nowhere else: the rule drops it, so the pin layer has nothing to rest.
            var go = new GameObject("Face");
            go.transform.SetParent(avatar.Root.transform);
            var faceMesh = go.AddComponent<SkinnedMeshRenderer>();
            faceMesh.sharedMesh = TestAvatar.QuadWithShapes("D");
            made.Add(faceMesh.sharedMesh);
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.shapeSets.Add(new ShapeSetRule { mesh = faceMesh, zeroOthers = true, value = ShapeSetValue.Default });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Face (pin)");
            Assert.That(StateNames(layer), Is.EquivalentTo(new[] { "Off", "On" }),
                "an empty clip lasts a whole second in Unity, so a Reset with nothing to write is left out rather than kept waiting");
            Assert.That(State(layer, "On").transitions.Single().destinationState.name, Is.EqualTo("Off"));
            Assert.That(Shapes(State(layer, "Off").motion, "Face"), Is.Empty);
        }

        // ---- played back ----------------------------------------------------------------------------------------

        /// <summary>
        /// The built FX controller, played on the avatar by a manual playable graph, one step of time per frame. Every
        /// VRCAnimatorLayerControl on a state the layers enter sets its FX layer's weight the way VRChat does, from the
        /// next frame on, or controlDelay frames later still. Below is a clip played under FX, as a world's dance plays
        /// under the avatar's FX.
        /// </summary>
        internal sealed class Player : System.IDisposable
        {
            PlayableGraph graph;
            AnimatorControllerPlayable playable;
            readonly AnimatorController fx;
            readonly SkinnedMeshRenderer body;
            readonly float step;
            readonly int controlDelay;
            readonly int[] current;
            readonly List<(int frame, int layer, float weight)> pending = new List<(int, int, float)>();
            int frame;

            public Player(GameObject root, AnimatorController fx, SkinnedMeshRenderer body, float step = Frame, int controlDelay = 0, AnimationClip below = null)
            {
                this.fx = fx;
                this.body = body;
                this.step = step;
                this.controlDelay = controlDelay;
                current = new int[fx.layers.Length];
                var animator = root.GetComponent<Animator>();
                if (animator == null) animator = root.AddComponent<Animator>();
                graph = PlayableGraph.Create("ResetOnceTests");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                playable = AnimatorControllerPlayable.Create(graph, fx);
                var output = AnimationPlayableOutput.Create(graph, "FX", animator);
                if (below != null)
                {
                    var mixer = AnimationLayerMixerPlayable.Create(graph, 2);
                    graph.Connect(AnimationClipPlayable.Create(graph, below), 0, mixer, 0);
                    graph.Connect(playable, 0, mixer, 1);
                    mixer.SetInputWeight(0, 1f);
                    mixer.SetInputWeight(1, 1f);
                    output.SetSourcePlayable(mixer);
                }
                else output.SetSourcePlayable(playable);
                graph.Play();
            }

            void Step()
            {
                frame++;
                foreach (var due in pending.Where(p => p.frame <= frame).ToList())
                {
                    playable.SetLayerWeight(due.layer, due.weight);
                    pending.Remove(due);
                }
                graph.Evaluate(step);
                var layers = fx.layers;
                for (int i = 0; i < layers.Length; i++)
                {
                    int hash = playable.GetCurrentAnimatorStateInfo(i).fullPathHash;
                    if (hash == current[i]) continue;
                    current[i] = hash;
                    var entered = layers[i].stateMachine.states.Select(s => s.state).FirstOrDefault(s => Animator.StringToHash(layers[i].name + "." + s.name) == hash);
                    if (entered == null) continue;
                    foreach (var control in entered.behaviours.OfType<VRCAnimatorLayerControl>())
                        if (control.playable == VRC_AnimatorLayerControl.BlendableLayer.FX)
                            pending.Add((frame + 1 + controlDelay, control.layer, control.goalWeight));
                }
            }

            /// <summary>The weight the layer of this name plays at now.</summary>
            public float LayerWeight(string layerName) => playable.GetLayerWeight(System.Array.FindIndex(fx.layers, l => l.name == layerName));

            /// <summary>How many frames Reset can last at this step, plus room for the transitions around it and a late layer control.</summary>
            public int Settle => Mathf.CeilToInt(Frame / step) + 4 + controlDelay;

            public void Set(string parameter, float value)
            {
                var declared = fx.parameters.Single(p => p.name == parameter);
                switch (declared.type)
                {
                    case AnimatorControllerParameterType.Bool: playable.SetBool(parameter, value > 0.5f); break;
                    case AnimatorControllerParameterType.Int: playable.SetInteger(parameter, Mathf.RoundToInt(value)); break;
                    default: playable.SetFloat(parameter, value); break;
                }
            }

            /// <summary>Plays the given number of frames and returns the shape's weight after each.</summary>
            public List<float> Play(int frames, string shape)
            {
                var weights = new List<float>();
                for (int i = 0; i < frames; i++)
                {
                    Step();
                    weights.Add(Weight(shape));
                }
                return weights;
            }

            /// <summary>Plays enough frames for any Reset to pass and returns the shape's weight after each.</summary>
            public List<float> Settled(string shape) => Play(Settle, shape);

            public float Weight(string shape) => body.GetBlendShapeWeight(body.sharedMesh.GetBlendShapeIndex(shape));

            /// <summary>The value a Float animator parameter has now, as the layers animate it.</summary>
            public float Float(string parameter) => playable.GetFloat(parameter);

            /// <summary>The value the animator gives a float material property of the body, read from the renderer's property block.</summary>
            public float Property(string property)
            {
                var block = new MaterialPropertyBlock();
                body.GetPropertyBlock(block);
                return block.GetFloat(property);
            }

            public void Dispose()
            {
                if (graph.IsValid()) graph.Destroy();
            }
        }

        static string Shown(IEnumerable<float> weights) => "(" + string.Join(", ", weights) + ")";

        [TestCase(20f)]
        [TestCase(60f)]
        [TestCase(144f)]
        public void PlayedBackFaceTrackingShowsWhileTheTriggerRestsAndAfterItEnds(float framesPerSecond)
        {
            FaceTrackingUnderLayerControl("Blush", 70f);
            BlinkLoop("Blush");
            avatar.Body.SetBlendShapeWeight(0, 5f);
            var pat = Headpat("Blush");

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var contact = TouchBuilder.ParameterFor(pat, "AD");
            using (var player = new Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond))
            {
                var resting = player.Settled("Blush");
                Assert.That(resting.Last(), Is.EqualTo(70f).Within(0.01f), "no hand near: face tracking's value shows, not the rest value " + Shown(resting));

                player.Set(contact, 1f);
                var patted = player.Settled("Blush");
                Assert.That(patted.Last(), Is.EqualTo(100f).Within(0.01f), "a hand fully near: the pin's value " + Shown(patted));

                player.Set(contact, 0f);
                var after = player.Settled("Blush");
                Assert.That(after, Does.Contain(5f), "on the way out the rest value is written once " + Shown(after));
                Assert.That(after.Count(w => Mathf.Abs(w - 5f) < 0.01f), Is.LessThanOrEqualTo(Mathf.CeilToInt(framesPerSecond / 60f) + 1),
                    "for 1/60 of a second, not for good " + Shown(after));
                Assert.That(after.Last(), Is.EqualTo(70f).Within(0.01f), "and then face tracking has the shape again " + Shown(after));
            }
        }

        [Test]
        public void PlayedBackATriggerThatComesBackDuringResetPinsAgain()
        {
            avatar.Body.SetBlendShapeWeight(0, 5f);
            var pat = Headpat("Blush");

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var contact = TouchBuilder.ParameterFor(pat, "AD");
            using (var player = new Player(avatar.Root, fx, avatar.Body))
            {
                player.Settled("Blush");
                player.Set(contact, 1f);
                Assert.That(player.Settled("Blush").Last(), Is.EqualTo(100f).Within(0.01f));
                player.Set(contact, 0f);
                var left = player.Play(1, "Blush");
                Assert.That(left.Last(), Is.EqualTo(5f).Within(0.01f), "the hand left: the rest value, this frame " + Shown(left));
                player.Set(contact, 1f);
                var back = player.Settled("Blush");
                Assert.That(back.Last(), Is.EqualTo(100f).Within(0.01f), "the hand came back while Reset played: the pin holds again a few frames later " + Shown(back));
            }
        }

        MenuEntry GroupMember(string name, string shape, bool defaultOn = false)
        {
            var member = menu.AddEntry(EntryKind.Toggle, name);
            member.exclusiveGroup = "G";
            member.defaultOn = defaultOn;
            member.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = shape, weight = 100f, mode = ShapeEffectMode.Pin });
            return member;
        }

        /// <summary>The group parameter and the value that picks the member, read from the pin layer's way from Off into it.</summary>
        static AnimatorCondition Picks(AnimatorController fx, string member)
            => State(Layer(fx, "AD: G (pin)"), "Off").transitions.Single(t => t.destinationState.name == member).conditions.Single();

        [Test]
        public void PlayedBackAGroupSwitchesFromOneMemberToTheNext()
        {
            GroupMember("A", "Blush");
            GroupMember("B", "Eye_Big");
            avatar.Body.SetBlendShapeWeight(0, 5f);
            avatar.Body.SetBlendShapeWeight(1, 7f);

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var a = Picks(fx, "A");
            var b = Picks(fx, "B");
            using (var player = new Player(avatar.Root, fx, avatar.Body))
            {
                player.Settled("Blush");
                player.Set(a.parameter, a.threshold);
                Assert.That(player.Settled("Blush").Last(), Is.EqualTo(100f).Within(0.01f), "A pins Blush");
                Assert.That(player.Weight("Eye_Big"), Is.EqualTo(7f).Within(0.01f));
                player.Set(b.parameter, b.threshold);
                var switched = player.Settled("Blush");
                Assert.That(switched.Last(), Is.EqualTo(5f).Within(0.01f), "switched to B: A's pin goes back to rest " + Shown(switched));
                Assert.That(player.Weight("Eye_Big"), Is.EqualTo(100f).Within(0.01f), "and B pins Eye_Big");
            }
        }

        [TestCase(20f)]
        [TestCase(60f)]
        [TestCase(144f)]
        public void PlayedBackAGroupLoadedOnAnotherMemberShowsTheDefaultMembersShapeAtRest(float framesPerSecond)
        {
            GroupMember("A", "Blush", defaultOn: true);
            GroupMember("B", "Eye_Big");
            avatar.Body.SetBlendShapeWeight(0, 5f);
            avatar.Body.SetBlendShapeWeight(1, 7f);

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            Assert.That(avatar.Body.GetBlendShapeWeight(0), Is.EqualTo(100f), "sanity: the uploaded avatar shows the default member's pin");
            var b = Picks(fx, "B");
            using (var player = new Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond))
            {
                player.Set(b.parameter, b.threshold);
                var loaded = player.Settled("Blush");
                Assert.That(loaded.First(), Is.EqualTo(5f).Within(0.01f), "from the very first frame, whatever its length " + Shown(loaded));
                Assert.That(loaded.Last(), Is.EqualTo(5f).Within(0.01f), "the saved choice is B: A's pin is put back to rest at load, not kept for good " + Shown(loaded));
                Assert.That(player.Weight("Eye_Big"), Is.EqualTo(100f).Within(0.01f), "and B pins Eye_Big");
            }
        }

        [TestCase(false, TestName = "PlayedBackADefaultOnToggleLoadedOffRestsItsPin")]
        [TestCase(true, TestName = "PlayedBackADefaultOnToggleLoadedOnHoldsItsPin")]
        public void PlayedBackADefaultOnToggleLoads(bool loadedOn)
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.defaultOn = true;
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            avatar.Body.SetBlendShapeWeight(0, 22f);

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var on = State(Layer(fx, "AD: Face (pin)"), "On").transitions.Single().conditions.Single();
            using (var player = new Player(avatar.Root, fx, avatar.Body))
            {
                player.Set(on.parameter, loadedOn ? 1f : 0f);
                var loaded = player.Settled("Blush");
                Assert.That(loaded.Last(), Is.EqualTo(loadedOn ? 100f : 22f).Within(0.01f), Shown(loaded));
            }
        }

        /// <summary>A gesture face the way a Write Defaults on avatar builds one: Idle plays nothing, Face holds the shape at value while GestureLeft is 1, both Write Defaults on. Returns the parameter.</summary>
        string GestureFace(string shape, float value, bool writeDefaults = true)
        {
            const string parameter = "GestureLeft";
            avatar.Fx.AddParameter(new AnimatorControllerParameter { name = parameter, type = AnimatorControllerParameterType.Int });
            var clip = TestAvatar.ShapeClip("Gesture " + shape, "Body", shape, value, value);
            made.Add(clip);
            var machine = AddFxLayer("Gesture " + shape);
            var idle = AddState(machine, "Idle", null, writeDefaults);
            var face = AddState(machine, "Face", clip, writeDefaults);
            machine.defaultState = idle;
            var toFace = idle.AddTransition(face);
            toFace.hasExitTime = false;
            toFace.duration = 0f;
            toFace.AddCondition(AnimatorConditionMode.Equals, 1f, parameter);
            var toIdle = face.AddTransition(idle);
            toIdle.hasExitTime = false;
            toIdle.duration = 0f;
            toIdle.AddCondition(AnimatorConditionMode.NotEqual, 1f, parameter);
            FullWeights();
            return parameter;
        }

        [TestCase(false, TestName = "PlayedBackADefaultOnToggleLetsAGestureHaveItsShapeOnceItIsOff")]
        [TestCase(true, TestName = "PlayedBackADefaultOnGroupMemberLetsAGestureHaveItsShapeOnceItIsOff")]
        public void PlayedBackADefaultOnPinLetsAGestureHaveItsShapeOnceItIsOff(bool inAGroup)
        {
            var gesture = GestureFace("Blush", 60f);
            avatar.Body.SetBlendShapeWeight(0, 5f);
            if (inAGroup)
            {
                GroupMember("A", "Blush", defaultOn: true);
                GroupMember("B", "Eye_Big");
            }
            else
            {
                var face = menu.AddEntry(EntryKind.Toggle, "Face");
                face.defaultOn = true;
                face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            }

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var on = inAGroup ? Picks(fx, "A") : State(Layer(fx, "AD: Face (pin)"), "On").transitions.Single().conditions.Single();
            // Off for both: no member picked. Another member picked keeps the group's layer at work, holding A's shape too.
            var off = 0f;
            using (var player = new Player(avatar.Root, fx, avatar.Body))
            {
                player.Set(on.parameter, inAGroup ? on.threshold : 1f);
                var loaded = player.Settled("Blush");
                Assert.That(loaded.Last(), Is.EqualTo(100f).Within(0.01f), "loaded on: the pin holds the shape " + Shown(loaded));

                player.Set(on.parameter, off);
                var left = player.Play(30, "Blush");
                Assert.That(left.Last(), Is.EqualTo(5f).Within(0.01f),
                    "turned off: the shape goes back to rest and stays there, though the gesture layer writes its default whenever it rests " + Shown(left));

                player.Set(gesture, 1f);
                Assert.That(player.Settled("Blush").Last(), Is.EqualTo(60f).Within(0.01f), "the gesture has the shape");
                player.Set(gesture, 0f);
                var after = player.Settled("Blush");
                Assert.That(after.Last(), Is.EqualTo(5f).Within(0.01f), "and lets it go back to rest " + Shown(after));
            }
            Assert.That(avatar.Body.GetBlendShapeWeight(0), Is.EqualTo(5f).Within(0.01f),
                "the gesture sits in the first three layers, where a rest layer after them would hide it: the uploaded avatar keeps the shape's rest, which the gesture goes back to");
            Assert.That(LayerIndex(fx, PinLayerState.RestLayerName), Is.EqualTo(-1), "and no rest layer is built for it");
        }

        // ---- the default look in the uploaded avatar --------------------------------------------------------

        MenuEntry FaceOnByDefault()
        {
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.defaultOn = true;
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            return face;
        }

        static int LayerIndex(AnimatorController fx, string name) => System.Array.FindIndex(fx.layers, l => l.name == name);

        /// <summary>Empty Write Defaults on layers added to the avatar's FX, after its own or in front of them, the way Modular Avatar's MMD relay and VRCFury's MMD Compatibility fill the first slots.</summary>
        void EmptyLayers(int count, bool first)
        {
            for (int i = 0; i < count; i++)
            {
                var machine = AddFxLayer("Empty " + i);
                machine.defaultState = AddState(machine, "Empty", null, true);
            }
            if (first)
            {
                var layers = avatar.Fx.layers.ToList();
                avatar.Fx.layers = layers.Skip(layers.Count - count).Concat(layers.Take(layers.Count - count)).ToArray();
            }
            FullWeights();
        }

        [TestCase(false, TestName = "PlayedBackTheRestLayerLetsAGestureHaveTheShapeOfAToggleThatStartsOn")]
        [TestCase(true, TestName = "PlayedBackTheRestLayerLetsAGestureHaveTheShapeOfAGroupMemberThatStartsOn")]
        public void PlayedBackTheRestLayerLetsAGestureHaveTheShape(bool inAGroup)
        {
            EmptyLayers(2, first: false);
            var gesture = GestureFace("Blush", 60f);
            avatar.Body.SetBlendShapeWeight(0, 5f);
            if (inAGroup)
            {
                GroupMember("A", "Blush", defaultOn: true);
                GroupMember("B", "Eye_Big");
            }
            else FaceOnByDefault();

            float betweenPasses = -1f;
            var fx = BuildAll(out var errors, _ => betweenPasses = avatar.Body.GetBlendShapeWeight(0));

            NoErrors(errors);
            Assert.That(betweenPasses, Is.EqualTo(5f).Within(0.01f), "between AvatarDoc's passes the scene keeps the rest: Modular Avatar and VRCFury read it there as the rest of what they change");
            Assert.That(avatar.Body.GetBlendShapeWeight(0), Is.EqualTo(100f).Within(0.01f), "the uploaded avatar shows the default look, for anyone who sees it with animations off");
            Assert.That(LayerIndex(fx, PinLayerState.RestLayerName), Is.EqualTo(3), "the rest layer sits right after the first three layers, under the gesture");
            var pinLayer = inAGroup ? "AD: G (pin)" : "AD: Face (pin)";
            var on = inAGroup ? Picks(fx, "A") : State(Layer(fx, pinLayer), "On").transitions.Single().conditions.Single();
            using (var player = new Player(avatar.Root, fx, avatar.Body))
            {
                player.Set(on.parameter, inAGroup ? on.threshold : 1f);
                Assert.That(player.Settled("Blush").Last(), Is.EqualTo(100f).Within(0.01f), "loaded on: the pin holds the shape");

                player.Set(on.parameter, 0f);
                var left = player.Play(30, "Blush");
                Assert.That(left.Last(), Is.EqualTo(5f).Within(0.01f), "turned off: the rest layer's 5, not the uploaded 100 " + Shown(left));
                Assert.That(player.LayerWeight(pinLayer), Is.EqualTo(0f), "and the pin layer rests at weight 0, so a world's dance gets through");

                player.Set(gesture, 1f);
                Assert.That(player.Settled("Blush").Last(), Is.EqualTo(60f).Within(0.01f), "the gesture has the shape");
                player.Set(gesture, 0f);
                var after = player.Settled("Blush");
                Assert.That(after.Last(), Is.EqualTo(5f).Within(0.01f), "and lets it go back to rest " + Shown(after));
            }
        }

        [Test]
        public void TheRestLayerLeavesTheFirstThreeLayersWhereTheyAre()
        {
            EmptyLayers(3, first: true);
            avatar.Body.SetBlendShapeWeight(0, 5f);
            FaceOnByDefault();
            var before = avatar.Fx.layers.Take(3).Select(l => l.name).ToList();

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            Assert.That(fx.layers.Take(3).Select(l => l.name), Is.EqualTo(before), "MMD worlds switch off the first three layers, where Modular Avatar and VRCFury keep their own MMD layers");
            Assert.That(LayerIndex(fx, PinLayerState.RestLayerName), Is.EqualTo(3));
            var rest = Layer(fx, PinLayerState.RestLayerName);
            Assert.That(rest.defaultWeight, Is.EqualTo(1f));
            Assert.That(rest.blendingMode, Is.EqualTo(AnimatorLayerBlendingMode.Override));
            Assert.That(rest.stateMachine.states.Length, Is.EqualTo(1), "one state, always on");
            var state = rest.stateMachine.defaultState;
            Assert.That(state.writeDefaultValues, Is.True, "Write Defaults on: a Write Defaults off layer above 0 would hold every shape a world's dance moves");
            Assert.That(state.transitions, Is.Empty);
            Assert.That(Shapes(state.motion, "Body"), Is.EqualTo(new[] { "Blush" }), "only the shape the uploaded avatar shows at another value");
            Assert.That(Shape(state.motion, "Blush").Evaluate(0f), Is.EqualTo(5f), "at the scene's weight from before the build");
        }

        [Test]
        public void TheRestLayerComesRightAfterTheAvatarsOwnLayersWhenThereAreFewerThanThree()
        {
            FaceOnByDefault();

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            Assert.That(fx.layers[0].name, Is.EqualTo("Base"));
            Assert.That(LayerIndex(fx, PinLayerState.RestLayerName), Is.EqualTo(1), "under every other layer but the avatar's own first one");
        }

        [Test]
        public void TheUploadShowsAPinOverALaterPlainRowOnTheSameShape()
        {
            EmptyLayers(2, first: false);
            avatar.Body.SetBlendShapeWeight(0, 5f);
            FaceOnByDefault();
            var plain = menu.AddEntry(EntryKind.Toggle, "Plain");
            plain.defaultOn = true;
            plain.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 30f });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            Assert.That(avatar.Body.GetBlendShapeWeight(0), Is.EqualTo(100f).Within(0.01f), "the pin layer comes last and wins at load whatever the menu order, so the uploaded avatar shows its 100");
            var rest = Layer(fx, PinLayerState.RestLayerName).stateMachine.defaultState.motion;
            Assert.That(Shape(rest, "Blush").Evaluate(0f), Is.EqualTo(30f), "and rests at Plain's 30");
            var on = State(Layer(fx, "AD: Face (pin)"), "On").transitions.Single().conditions.Single();
            using (var player = new Player(avatar.Root, fx, avatar.Body))
            {
                player.Set(on.parameter, 1f);
                Assert.That(player.Settled("Blush").Last(), Is.EqualTo(100f).Within(0.01f), "loaded on: the pin wins over Plain");
                player.Set(on.parameter, 0f);
                var left = player.Play(30, "Blush");
                Assert.That(left.Last(), Is.EqualTo(30f).Within(0.01f), "turned off: Plain's 30 " + Shown(left));
            }
        }

        [Test]
        public void AReplaceTriggersSourceThatAToggleStartsOnHoldsKeepsItsRestInTheUpload()
        {
            EmptyLayers(2, first: false);
            avatar.Body.SetBlendShapeWeight(0, 5f);
            FaceOnByDefault();
            var link = menu.AddEntry(EntryKind.Trigger, "Blush link");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            link.linkMode = BlendShapeLinkMode.Replace;
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Follow, multiplier = 1f });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            Assert.That(avatar.Body.GetBlendShapeWeight(0), Is.EqualTo(5f).Within(0.01f),
                "the trigger takes every curve of Blush out of FX, so Blush shows the uploaded value for good: it has to be the rest");
            var rest = (AnimationClip)Layer(fx, PinLayerState.RestLayerName).stateMachine.defaultState.motion;
            Assert.That(Shape(rest, "Blush"), Is.Null, "replaced, like every other curve of Blush");
            var hidden = AnimationUtility.GetCurveBindings(rest).Where(b => b.type == typeof(Animator)).ToList();
            Assert.That(hidden.Count, Is.EqualTo(1), "the trigger's hidden copy of Blush");
            Assert.That(AnimationUtility.GetEditorCurve(rest, hidden[0]).Evaluate(0f), Is.EqualTo(5f), "reads the rest while Face rests");
        }

        static IEnumerable<VirtualClip> ClipsOf(VirtualMotion motion)
        {
            if (motion is VirtualClip clip) yield return clip;
            else if (motion is VirtualBlendTree tree)
                foreach (var child in tree.Children)
                    if (child.Motion != null)
                        foreach (var leaf in ClipsOf(child.Motion)) yield return leaf;
        }

        [Test]
        public void AShapeAnotherToolCopiesFromAHeldOneGetsTheLookAndTheRestToo()
        {
            EmptyLayers(2, first: false);
            avatar.Body.SetBlendShapeWeight(0, 5f);
            avatar.Body.SetBlendShapeWeight(1, 20f);
            FaceOnByDefault();
            var source = EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blush");
            var copy = EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big");

            // What Modular Avatar's Blendshape Sync does between AvatarDoc's passes, with a remap to half: the copy gets
            // the source's weight from the scene, and the source's curve in every clip that moves the source.
            var fx = BuildAll(out var errors, controller =>
            {
                avatar.Body.SetBlendShapeWeight(1, avatar.Body.GetBlendShapeWeight(0) * 0.5f);
                foreach (var layer in controller.Layers)
                {
                    if (layer.StateMachine == null) continue;
                    foreach (var state in layer.StateMachine.AllStates())
                        foreach (var clip in ClipsOf(state.Motion))
                        {
                            var curve = clip.GetFloatCurve(source);
                            if (curve != null && curve.length > 0)
                                clip.SetFloatCurve(copy, AnimationCurve.Constant(0f, curve.keys[curve.length - 1].time, curve.Evaluate(0f) * 0.5f));
                        }
                }
            });

            NoErrors(errors);
            Assert.That(avatar.Body.GetBlendShapeWeight(0), Is.EqualTo(100f).Within(0.01f), "sanity: the source shows the look");
            Assert.That(avatar.Body.GetBlendShapeWeight(1), Is.EqualTo(50f).Within(0.01f),
                "the copy shows the look too, the source's 100 mapped to 50, so the two still match for anyone with animations off");
            var rest = Layer(fx, PinLayerState.RestLayerName).stateMachine.defaultState.motion;
            Assert.That(Shape(rest, "Eye_Big").Evaluate(0f), Is.EqualTo(2.5f).Within(0.01f), "and rests at the weight the tool gave it from the source's rest");
            var on = State(Layer(fx, "AD: Face (pin)"), "On").transitions.Single().conditions.Single();
            using (var player = new Player(avatar.Root, fx, avatar.Body))
            {
                player.Set(on.parameter, 1f);
                player.Settled("Blush");
                Assert.That(player.Weight("Eye_Big"), Is.EqualTo(50f).Within(0.01f), "loaded on: the copy follows the pin");
                player.Set(on.parameter, 0f);
                player.Play(30, "Blush");
                Assert.That(player.Weight("Eye_Big"), Is.EqualTo(2.5f).Within(0.01f), "turned off: the copy goes back to its rest with the source");
                Assert.That(player.Weight("Blush"), Is.EqualTo(5f).Within(0.01f));
            }
        }

#if DOCME_MA
        [Test]
        public void PlayedBackAWholeBuildWithModularAvatarKeepsItsMmdLayersFirst()
        {
            // A Modular Avatar toggle: its reactive components bring MA's own rest layer and its MMD relay.
            var itemGo = new GameObject("MA Hat");
            itemGo.transform.SetParent(avatar.Root.transform);
            itemGo.AddComponent<ModularAvatarMenuInstaller>();
            var item = itemGo.AddComponent<ModularAvatarMenuItem>();
            if (item.Control == null) item.Control = new VRCExpressionsMenu.Control();
            item.Control.type = VRCExpressionsMenu.Control.ControlType.Toggle;
            item.label = "MA Hat";
            var toggle = itemGo.AddComponent<ModularAvatarObjectToggle>();
            var reference = new AvatarObjectReference();
            reference.Set(avatar.Hat);
            toggle.Objects = new List<ToggledObject> { new ToggledObject { Object = reference, Active = true } };
            avatar.Body.SetBlendShapeWeight(0, 5f);
            FaceOnByDefault();

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                var descriptor = processed.GetComponent<VRCAvatarDescriptor>();
                var fx = (AnimatorController)descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
                var names = fx.layers.Select(l => l.name).ToList();
                var shown = string.Join(" | ", names);
                Assert.That(names.Take(3), Is.EqualTo(new[] { "Modular Avatar: MMD Dummy", "Modular Avatar: MMD Dummy", "Modular Avatar: MMD Control" }),
                    "Modular Avatar's MMD layers keep the three slots MMD worlds switch off: " + shown);
                Assert.That(names.IndexOf(PinLayerState.RestLayerName), Is.EqualTo(3), "the rest layer comes right after them, before Modular Avatar's own rest layer: " + shown);
                var body = processed.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == avatar.Body.name);
                Assert.That(body.GetBlendShapeWeight(0), Is.EqualTo(100f).Within(0.01f), "the uploaded avatar shows the default look");
                var on = State(Layer(fx, "AD: Face (pin)"), "On").transitions.Single().conditions.Single();
                using (var player = new Player(processed, fx, body))
                {
                    player.Set(on.parameter, 1f);
                    Assert.That(player.Settled("Blush").Last(), Is.EqualTo(100f).Within(0.01f), "loaded on: the pin holds the shape");
                    player.Set(on.parameter, 0f);
                    var left = player.Play(30, "Blush");
                    Assert.That(left.Last(), Is.EqualTo(5f).Within(0.01f), "turned off: the rest layer's 5 " + Shown(left));
                }
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                GeneratedAssets.CleanUp();
            }
        }
#endif

        [Test]
        public void ABlinkShapeAToggleStartsOnHoldsKeepsItsRestInTheUpload()
        {
            avatar.Descriptor.enableEyeLook = true;
            avatar.Descriptor.customEyeLookSettings.eyelidType = VRCAvatarDescriptor.EyelidType.Blendshapes;
            avatar.Descriptor.customEyeLookSettings.eyelidsSkinnedMesh = avatar.Body;
            avatar.Descriptor.customEyeLookSettings.eyelidsBlendshapes = new[] { avatar.Body.sharedMesh.GetBlendShapeIndex("Blush"), -1, -1 };
            avatar.Body.SetBlendShapeWeight(0, 5f);
            FaceOnByDefault();

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            Assert.That(avatar.Body.GetBlendShapeWeight(0), Is.EqualTo(5f).Within(0.01f), "VRChat stops blinking a shape any layer writes, so no rest layer may hold it, and the avatar keeps its rest");
            Assert.That(LayerIndex(fx, PinLayerState.RestLayerName), Is.EqualTo(-1));
        }

        [Test]
        public void NoRestLayerWhenNothingThatStartsOnHoldsAShape()
        {
            FaceOnByDefault().defaultOn = false;
            avatar.Body.SetBlendShapeWeight(0, 5f);

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            Assert.That(LayerIndex(fx, PinLayerState.RestLayerName), Is.EqualTo(-1));
            Assert.That(avatar.Body.GetBlendShapeWeight(0), Is.EqualTo(5f).Within(0.01f));
        }

        [Test]
        public void APinBesideAWriteDefaultsOnLayerKeepsItsRestWhenTheAvatarShowsAnother()
        {
            GestureFace("Blush", 60f);
            FaceOnByDefault().defaultOn = false;
            avatar.Body.SetBlendShapeWeight(0, 5f);

            // Another tool writes its own default look into the avatar after AvatarDoc's first pass.
            var fx = BuildAll(out var errors, _ => avatar.Body.SetBlendShapeWeight(0, 70f));

            NoErrors(errors);
            var on = State(Layer(fx, "AD: Face (pin)"), "On").transitions.Single().conditions.Single();
            using (var player = new Player(avatar.Root, fx, avatar.Body))
            {
                player.Set(on.parameter, 1f);
                Assert.That(player.Settled("Blush").Last(), Is.EqualTo(100f).Within(0.01f));
                player.Set(on.parameter, 0f);
                var left = player.Play(30, "Blush");
                Assert.That(left.Last(), Is.EqualTo(5f).Within(0.01f),
                    "the gesture layer writes nothing while it rests, so a pin layer at weight 0 would show the avatar's 70 " + Shown(left));
            }
        }

        [Test]
        public void PlayedBackABlendShapeTriggerThatStartsOnRestsWhenItsSourceLoadsOff()
        {
            var smile = menu.AddEntry(EntryKind.Toggle, "Smile");
            smile.defaultOn = true;
            smile.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            var link = menu.AddEntry(EntryKind.Trigger, "Blush link");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Fixed, weight = 80f });
            avatar.Body.SetBlendShapeWeight(1, 7f);

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            Assert.That(avatar.Body.GetBlendShapeWeight(1), Is.EqualTo(80f), "sanity: Smile starts on, so the uploaded avatar shows the trigger's on value");
            var smileOn = State(Layer(fx, "AD: Smile"), "On").transitions.Single().conditions.Single();
            using (var player = new Player(avatar.Root, fx, avatar.Body))
            {
                player.Set(smileOn.parameter, 0f);
                var loaded = player.Settled("Eye_Big");
                Assert.That(player.Weight("Blush"), Is.EqualTo(0f).Within(0.01f), "sanity: Smile loaded off");
                Assert.That(loaded.Last(), Is.EqualTo(7f).Within(0.01f), "the trigger's shape is at rest too " + Shown(loaded));
            }
        }

        [Test]
        public void PlayedBackAFadeOffValueShowsAtLoad()
        {
            var pat = menu.AddEntry(EntryKind.Trigger, "Pat");
            pat.source = TriggerSource.Contact;
            pat.contactAnchor = avatar.Hat.transform;
            pat.contactTags = new List<string> { "Hand" };
            pat.touchMode = TouchMode.FollowDistance;
            pat.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, explicitOff = true, minWeight = 30f });
            avatar.Body.SetBlendShapeWeight(0, 5f);

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            using (var player = new Player(avatar.Root, fx, avatar.Body))
            {
                var loaded = player.Settled("Blush");
                Assert.That(loaded.Last(), Is.EqualTo(30f).Within(0.01f), "no hand near at load: the row's off value, not the scene's " + Shown(loaded));
            }
        }

        [TestCase(false, TestName = "PlayedBackAHoldTriggersPropertyOffValueShowsAtLoadWithWriteDefaultsOff")]
        [TestCase(true, TestName = "PlayedBackAHoldTriggersPropertyOffValueShowsAtLoadWithWriteDefaultsOn")]
        public void PlayedBackAHoldTriggersPropertyOffValueShowsAtLoad(bool writeDefaultsOn)
        {
            using (var owned = TestAvatar.Create(writeDefaultsOff: !writeDefaultsOn))
            {
                owned.MaterialA.SetFloat("_Glossiness", 0.2f);
                var ownedMenu = owned.AddMenu();
                var source = ownedMenu.AddEntry(EntryKind.Toggle, "Source");
                source.objects.Add(new ObjectTarget { target = owned.Glasses });
                var glow = ownedMenu.AddEntry(EntryKind.Trigger, "Glow");
                glow.source = TriggerSource.Toggle;
                glow.sourceToggleId = source.id;
                glow.properties.Add(new MaterialPropertyTarget { renderer = owned.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 0.9f, explicitOff = true, minValue = 0.4f });

                var context = new BuildContext(owned.Root, null);
                context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
                var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
                context.DeactivateAllExtensionContexts();
                errors.AddRange(ErrorReport.CaptureErrors(() => AvatarDoctorPlugin.FinishAfterOtherTools(context)));
                NoErrors(errors);
                var fx = FxOf(owned);
                Assert.That(State(Layer(fx, "AD: Glow"), "Off").writeDefaultValues, Is.EqualTo(writeDefaultsOn), "sanity: the trigger's layer follows the avatar");
                var sourceOn = State(Layer(fx, "AD: Glow"), "Off").transitions.First().conditions.First();

                using (var player = new Player(owned.Root, fx, owned.Body))
                {
                    player.Settled("Blush");
                    Assert.That(player.Property("_Glossiness"), Is.EqualTo(0.4f).Within(0.0001f), "resting at load: the row's off value");
                    player.Set(sourceOn.parameter, 1f);
                    player.Settled("Blush");
                    Assert.That(player.Property("_Glossiness"), Is.EqualTo(0.9f).Within(0.0001f), "firing: the row's value");
                    player.Set(sourceOn.parameter, 0f);
                    player.Play(30, "Blush");
                    Assert.That(player.Property("_Glossiness"), Is.EqualTo(0.4f).Within(0.0001f), "rested again, and still half a second later");
                }
            }
        }

        [Test]
        public void PlayedBackTwoPinsOnOneShapeComeBackToRestWhicheverIsLeftLast()
        {
            var sleepy = menu.AddEntry(EntryKind.Toggle, "Sleepy");
            sleepy.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 90f, mode = ShapeEffectMode.Pin });
            var petSource = menu.AddEntry(EntryKind.Toggle, "PetSource");
            petSource.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var headpat = menu.AddEntry(EntryKind.Trigger, "Headpat");
            headpat.source = TriggerSource.Toggle;
            headpat.sourceToggleId = petSource.id;
            headpat.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 10f, mode = ShapeEffectMode.Pin });
            avatar.Body.SetBlendShapeWeight(0, 5f);

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            string ParameterOf(string layerName) => State(Layer(fx, layerName), "Off").transitions.First().conditions.First().parameter;
            var sleepyParameter = ParameterOf("AD: Sleepy (pin)");
            var headpatParameter = ParameterOf("AD: Headpat (pin)");
            using (var player = new Player(avatar.Root, fx, avatar.Body))
            {
                player.Play(2, "Blush");
                player.Set(sleepyParameter, 1f);
                Assert.That(player.Play(4, "Blush").Last(), Is.EqualTo(90f).Within(0.01f), "Sleepy alone: the upper pin, resting, does not mask it");
                player.Set(headpatParameter, 1f);
                Assert.That(player.Play(4, "Blush").Last(), Is.EqualTo(10f).Within(0.01f), "Headpat on top wins");
                player.Set(headpatParameter, 0f);
                Assert.That(player.Play(4, "Blush").Last(), Is.EqualTo(90f).Within(0.01f), "Headpat left: Sleepy shows again");
                player.Set(headpatParameter, 1f);
                player.Play(4, "Blush");
                player.Set(sleepyParameter, 0f);
                Assert.That(player.Play(4, "Blush").Last(), Is.EqualTo(10f).Within(0.01f), "Sleepy left under Headpat: Headpat still wins");
                player.Set(headpatParameter, 0f);
                var after = player.Play(4, "Blush");
                Assert.That(after.Last(), Is.EqualTo(5f).Within(0.01f), "Headpat left last: Blush is back at rest, not stuck at Headpat's value (" + string.Join(", ", after) + ")");
            }
        }
    }
}
#endif
