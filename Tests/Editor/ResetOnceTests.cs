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
                if (child.state == reset) continue;
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
            Assert.That(State(layer, "Off").transitions.Single().destinationState.name, Is.EqualTo("On"), "Off still goes straight to On");
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
            Assert.That(off.transitions.Select(t => t.destinationState.name), Is.EquivalentTo(new[] { "A", "B" }), "Off goes to the member chosen");
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
            Assert.That(Shapes(reset.motion, "Face"), Does.Not.Contain("A"), "Other's own layer writes A in both its states, so it owns A and nothing resets it");
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
            Assert.That(Shape(reset.motion, "Blush"), Is.Null,
                "Glow's layer writes Blush in every state that is not its own Reset or Load, so it owns Blush, and the pin writes no rest for it, not even at load");
            Assert.That(Shape(State(pin, "Load").motion, "Blush"), Is.Null);
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
        public void AShapeFaceTrackingOwnsGetsNoResetCurve()
        {
            DirectFaceTracking("Blush", 40f);
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 100f, mode = ShapeEffectMode.Pin });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Face (pin)");
            var reset = AssertResetsOnce(layer, "Off", "On");
            Assert.That(Shape(reset.motion, "Blush"), Is.Null, "face tracking's Direct tree owns Blush, so not even the one frame reset writes it");
            Assert.That(Shape(reset.motion, "Eye_Big"), Is.Not.Null, "Eye_Big has no owner, so it still resets once");
        }

        [Test]
        public void ALayerWithNothingToResetGoesStraightBack()
        {
            DirectFaceTracking("Blush", 40f);
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = Layer(fx, "AD: Face (pin)");
            Assert.That(StateNames(layer), Is.EquivalentTo(new[] { "Off", "On" }),
                "an empty clip lasts a whole second in Unity, so a Reset with nothing to write is left out rather than kept waiting");
            Assert.That(State(layer, "On").transitions.Single().destinationState.name, Is.EqualTo("Off"));
            Assert.That(Shape(State(layer, "Off").motion, "Blush"), Is.Null);
        }

        // ---- played back ----------------------------------------------------------------------------------------

        /// <summary>The built FX controller, played on the avatar by a manual playable graph, one step of time per frame.</summary>
        internal sealed class Player : System.IDisposable
        {
            PlayableGraph graph;
            AnimatorControllerPlayable playable;
            readonly AnimatorController fx;
            readonly SkinnedMeshRenderer body;
            readonly float step;

            public Player(GameObject root, AnimatorController fx, SkinnedMeshRenderer body, float step = Frame)
            {
                this.fx = fx;
                this.body = body;
                this.step = step;
                var animator = root.GetComponent<Animator>();
                if (animator == null) animator = root.AddComponent<Animator>();
                graph = PlayableGraph.Create("ResetOnceTests");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                playable = AnimatorControllerPlayable.Create(graph, fx);
                var output = AnimationPlayableOutput.Create(graph, "FX", animator);
                output.SetSourcePlayable(playable);
                graph.Play();
            }

            /// <summary>How many frames Reset can last at this step, plus room for the transitions around it.</summary>
            public int Settle => Mathf.CeilToInt(Frame / step) + 4;

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
                    graph.Evaluate(step);
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
            Assert.That(avatar.Body.GetBlendShapeWeight(0), Is.EqualTo(100f), "sanity: the build wrote the default member's pin into the avatar");
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
            Assert.That(avatar.Body.GetBlendShapeWeight(1), Is.EqualTo(80f), "sanity: the build wrote the trigger's on value into the avatar, since Smile starts on");
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
