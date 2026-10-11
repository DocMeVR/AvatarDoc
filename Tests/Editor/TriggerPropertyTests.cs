#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Material properties on every trigger that holds effects of its own (Toggle, Parameter,
    /// Contact and PhysBone sources, Hold and Fade), built with the same rest and release rules as the rest of
    /// the trigger, shown by the scene preview, checked by the plan and offered by "+ Add effect".
    /// </summary>
    public class TriggerPropertyTests
    {
        TestAvatar avatar;
        AvatarDoc menu;

        [SetUp]
        public void SetUp()
        {
            GeneratedAssets.Note();
            avatar = TestAvatar.Create();
            menu = avatar.AddMenu();
            avatar.MaterialA.SetFloat("_Glossiness", 0.2f);
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
            GeneratedAssets.CleanUp();
        }

        static AnimatorController FxOf(TestAvatar a)
            => (AnimatorController)a.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;

        static AnimatorControllerLayer Layer(AnimatorController fx, string name) => fx.layers.Single(l => l.name == name);

        static AnimatorState State(AnimatorControllerLayer layer, string name) => layer.stateMachine.states.Single(s => s.state.name == name).state;

        static AnimationCurve Glossiness(Motion motion)
            => AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "material._Glossiness"));

        static MaterialPropertyTarget Shine(TestAvatar a, float value) => new MaterialPropertyTarget { renderer = a.Body, property = "_Glossiness", type = PropertyValueType.Float, value = value };

        /// <summary>The whole build the way NDMF runs it: AvatarDoc's own pass, then the final pass the Optimizing phase runs after every other tool.</summary>
        /// <param name="betweenPasses">Runs on the FX controller after AvatarDoc's own pass and before the final one, where Modular Avatar and VRCFury would change it.</param>
        AnimatorController BuildAll(TestAvatar target = null, System.Action<VirtualAnimatorController> betweenPasses = null)
        {
            target = target ?? avatar;
            var context = new BuildContext(target.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error), Is.Empty);
            if (betweenPasses != null)
            {
                var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
                asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx);
                betweenPasses(fx);
                context.DeactivateAllExtensionContexts();
            }
            AvatarDoctorPlugin.FinishAfterOtherTools(context);
            return FxOf(target);
        }

        static List<string> LayerNames(AnimatorController fx) => fx.layers.Select(l => l.name).ToList();

        MenuEntry ContactTrigger(string name, TouchMode mode)
        {
            var entry = menu.AddEntry(EntryKind.Trigger, name);
            entry.source = TriggerSource.Contact;
            entry.contactAnchor = avatar.Hat.transform;
            entry.touchMode = mode;
            return entry;
        }

        MenuEntry HatToggle()
        {
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            return hat;
        }

        AvatarDoctorView View() => new AvatarDoctorView(new SerializedObject(menu), menu);

        // Build

        [Test]
        public void HoldTriggerPropertyHoldsItsValueAndRestsAfter()
        {
            var pat = ContactTrigger("Pat", TouchMode.Hold);
            pat.properties.Add(Shine(avatar, 0.7f));

            var fx = BuildAll();

            var layer = Layer(fx, "AD: Pat");
            var on = Glossiness(State(layer, "On").motion);
            var off = Glossiness(State(layer, "Off").motion);
            Assert.That(on, Is.Not.Null, "the On state writes the property");
            Assert.That(on.Evaluate(0f), Is.EqualTo(0.7f).Within(0.0001f), "at the row's value");
            Assert.That(off, Is.Not.Null, "nothing else animates the property, so Off hands it back to rest itself");
            Assert.That(off.Evaluate(0f), Is.EqualTo(0.2f).Within(0.0001f), "at the material's own value");
        }

        [Test]
        public void HoldTriggerPropertyLeavesItsRestToALayerThatOwnsIt()
        {
            var toggle = menu.AddEntry(EntryKind.Toggle, "Shiny");
            toggle.properties.Add(Shine(avatar, 0.9f));
            var pat = ContactTrigger("Pat", TouchMode.Hold);
            pat.properties.Add(Shine(avatar, 0.7f));

            var fx = BuildAll();

            var triggerLayer = Layer(fx, "AD: Pat");
            Assert.That(Glossiness(State(triggerLayer, "On").motion).Evaluate(0f), Is.EqualTo(0.7f).Within(0.0001f));
            Assert.That(Glossiness(State(triggerLayer, "Off").motion), Is.Null,
                "the toggle's own layer writes the property in every state, so it owns it: the trigger's Off leaves it alone and the toggle shows through while the trigger is not met");
            var toggleLayer = Layer(fx, "AD: Shiny");
            Assert.That(Glossiness(State(toggleLayer, "Off").motion).Evaluate(0f), Is.EqualTo(0.2f).Within(0.0001f), "the toggle keeps its own rest");
            Assert.That(Glossiness(State(toggleLayer, "On").motion).Evaluate(0f), Is.EqualTo(0.9f).Within(0.0001f));
        }

        [Test]
        public void HoldTriggerPropertyRestsOnAWriteDefaultsOnAvatarToo()
        {
            using (var owned = TestAvatar.Create(writeDefaultsOff: false))
            {
                owned.MaterialA.SetFloat("_Glossiness", 0.3f);
                var ownedMenu = owned.AddMenu();
                var pat = ownedMenu.AddEntry(EntryKind.Trigger, "Pat");
                pat.source = TriggerSource.Contact;
                pat.contactAnchor = owned.Hat.transform;
                pat.properties.Add(Shine(owned, 0.7f));

                var fx = BuildAll(owned);

                var layer = Layer(fx, "AD: Pat");
                Assert.That(State(layer, "Off").writeDefaultValues, Is.True, "the trigger's layer follows the avatar's Write Defaults");
                var off = Glossiness(State(layer, "Off").motion);
                Assert.That(off, Is.Not.Null, "the trigger's own Write Defaults on layer is not another layer that owns the property");
                Assert.That(off.Evaluate(0f), Is.EqualTo(0.3f).Within(0.0001f));
            }
        }

        [Test]
        public void AHoldAndAFadeTriggerOnOnePropertyRestItOnce()
        {
            var hold = ContactTrigger("Hold", TouchMode.Hold);
            hold.properties.Add(Shine(avatar, 0.7f));
            var fade = ContactTrigger("Fade", TouchMode.FollowDistance);
            fade.properties.Add(Shine(avatar, 0.9f));

            var fx = BuildAll();

            var holdOff = Glossiness(State(Layer(fx, "AD: Hold"), "Off").motion);
            Assert.That(holdOff, Is.Not.Null, "the Hold trigger's layer sits below every pin layer, so it is the one that rests the property");
            Assert.That(holdOff.Evaluate(0f), Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(Glossiness(State(Layer(fx, "AD: Fade (distance)"), "Far").motion), Is.Null,
                "Far, above it, leaves the property alone, so the Hold trigger still shows while no hand is near the Fade contact");
        }

        [Test]
        public void TwoHoldTriggersOnOnePropertyRestOnlyTheLowerOne()
        {
            var first = ContactTrigger("First", TouchMode.Hold);
            first.properties.Add(Shine(avatar, 0.6f));
            var second = ContactTrigger("Second", TouchMode.Hold);
            second.properties.Add(Shine(avatar, 0.8f));

            var fx = BuildAll();

            var names = LayerNames(fx);
            Assert.That(names.IndexOf("AD: Second"), Is.GreaterThan(names.IndexOf("AD: First")), "the later trigger's layer sits above, so it wins while both fire");
            var firstOff = Glossiness(State(Layer(fx, "AD: First"), "Off").motion);
            Assert.That(firstOff, Is.Not.Null, "the lower layer rests the property");
            Assert.That(firstOff.Evaluate(0f), Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(Glossiness(State(Layer(fx, "AD: Second"), "Off").motion), Is.Null, "the upper Off stays empty, so the lower trigger shows through while only it fires");
            Assert.That(Glossiness(State(Layer(fx, "AD: First"), "On").motion).Evaluate(0f), Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(Glossiness(State(Layer(fx, "AD: Second"), "On").motion).Evaluate(0f), Is.EqualTo(0.8f).Within(0.0001f));
        }

        [Test]
        public void TheReleaseRestsAnOrdinaryLayerBeforeAPinLayerWhateverTheirOrder()
        {
            var hold = ContactTrigger("Hold", TouchMode.Hold);
            hold.properties.Add(Shine(avatar, 0.7f));
            var fade = ContactTrigger("Fade", TouchMode.FollowDistance);
            fade.properties.Add(Shine(avatar, 0.9f));

            // A tool between the passes leaves the distance layer below the Hold layer; the final pass moves every
            // pin layer back above the ordinary ones, so the Hold layer is still the one an animation falls through to.
            var fx = BuildAll(betweenPasses: virtualFx =>
                virtualFx.Layers = virtualFx.Layers.OrderBy(l => l.Name == "AD: Fade (distance)" ? 0 : 1).ToList());

            var names = LayerNames(fx);
            Assert.That(names.IndexOf("AD: Fade (distance)"), Is.GreaterThan(names.IndexOf("AD: Hold")), "the final pass moved the distance layer last again");
            var holdOff = Glossiness(State(Layer(fx, "AD: Hold"), "Off").motion);
            Assert.That(holdOff, Is.Not.Null, "the ordinary layer rests the property, though the distance layer had the smaller index when the release ran");
            Assert.That(holdOff.Evaluate(0f), Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(Glossiness(State(Layer(fx, "AD: Fade (distance)"), "Far").motion), Is.Null, "Far, last in FX after the move, leaves it alone");
        }

        [Test]
        public void FadeTriggerPropertyBlendsInTheNearTreeAndRestsAtFar()
        {
            var pat = ContactTrigger("Pat", TouchMode.FollowDistance);
            pat.contactFullAt = 0.6f;
            pat.properties.Add(Shine(avatar, 0.7f));
            Assert.That(pat.IsFollowDistanceTrigger, Is.True);

            var fx = BuildAll();

            var layer = Layer(fx, "AD: Pat (distance)");
            var tree = (BlendTree)State(layer, "Near").motion;
            Assert.That(tree.children.Length, Is.EqualTo(2));
            Assert.That(tree.children[0].threshold, Is.EqualTo(0f));
            Assert.That(Glossiness(tree.children[0].motion).Evaluate(0f), Is.EqualTo(0.2f).Within(0.0001f), "the blend starts at rest");
            Assert.That(tree.children[1].threshold, Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(Glossiness(tree.children[1].motion).Evaluate(0f), Is.EqualTo(0.7f).Within(0.0001f), "and reaches the row's value at Fully on");
            var far = Glossiness(State(layer, "Far").motion);
            Assert.That(far, Is.Not.Null, "nothing else animates the property, so Far rests it");
            Assert.That(far.Evaluate(0f), Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void FadeFarLeavesAPropertyToALayerThatOwnsIt()
        {
            var toggle = menu.AddEntry(EntryKind.Toggle, "Shiny");
            toggle.properties.Add(Shine(avatar, 0.9f));
            var pat = ContactTrigger("Pat", TouchMode.FollowDistance);
            pat.properties.Add(Shine(avatar, 0.7f));

            var fx = BuildAll();

            var layer = Layer(fx, "AD: Pat (distance)");
            Assert.That(Glossiness(State(layer, "Far").motion), Is.Null,
                "Fade's own rule: Far carries no curve for a binding another layer owns, or this layer, last in FX, would hide the toggle for good");
            var tree = (BlendTree)State(layer, "Near").motion;
            Assert.That(Glossiness(tree.children[1].motion).Evaluate(0f), Is.EqualTo(0.7f).Within(0.0001f), "the blend itself is unchanged");
        }

        [Test]
        public void ToggleAndParameterSourcedTriggersBuildTheirProperties()
        {
            var hat = HatToggle();
            var whileHat = menu.AddEntry(EntryKind.Trigger, "WhileHat");
            whileHat.source = TriggerSource.Toggle;
            whileHat.sourceToggleId = hat.id;
            whileHat.properties.Add(Shine(avatar, 0.6f));
            var whileExisting = menu.AddEntry(EntryKind.Trigger, "WhileExisting");
            whileExisting.source = TriggerSource.Parameter;
            whileExisting.sourceParameter = "Existing";
            whileExisting.condition = ParameterCondition.IsTrue;
            whileExisting.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Color", type = PropertyValueType.Color, color = new Color(1f, 0.5f, 0.25f, 1f) });
            avatar.MaterialA.SetColor("_Color", new Color(0.1f, 0.2f, 0.3f, 0.4f));

            var fx = BuildAll();

            var toggleSourced = Layer(fx, "AD: WhileHat");
            Assert.That(Glossiness(State(toggleSourced, "On").motion).Evaluate(0f), Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(Glossiness(State(toggleSourced, "Off").motion).Evaluate(0f), Is.EqualTo(0.2f).Within(0.0001f));
            var parameterSourced = Layer(fx, "AD: WhileExisting");
            var on = (AnimationClip)State(parameterSourced, "On").motion;
            var off = (AnimationClip)State(parameterSourced, "Off").motion;
            foreach (var (channel, onValue, offValue) in new[] { ("r", 1f, 0.1f), ("g", 0.5f, 0.2f), ("b", 0.25f, 0.3f), ("a", 1f, 0.4f) })
            {
                var binding = EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "material._Color." + channel);
                Assert.That(AnimationUtility.GetEditorCurve(on, binding).Evaluate(0f), Is.EqualTo(onValue).Within(0.0001f), "On " + channel);
                Assert.That(AnimationUtility.GetEditorCurve(off, binding).Evaluate(0f), Is.EqualTo(offValue).Within(0.0001f), "Off " + channel);
            }
        }

        [Test]
        public void PropertyEffectsKeepConditionGroupsAndTheGate()
        {
            var hat = HatToggle();
            var gate = menu.AddEntry(EntryKind.Toggle, "Switch");
            var either = menu.AddEntry(EntryKind.Trigger, "Either");
            either.when.Add(new ConditionGroup { all = { new WhenCondition { kind = WhenConditionKind.Toggle, sourceToggleId = hat.id, sourceToggleOn = true } } });
            either.when.Add(new ConditionGroup { all = { new WhenCondition { kind = WhenConditionKind.Parameter, sourceParameter = "Existing", condition = ParameterCondition.IsTrue } } });
            either.gateToggleId = gate.id;
            either.properties.Add(Shine(avatar, 0.8f));

            var fx = BuildAll();

            var layer = Layer(fx, "AD: Either");
            var off = State(layer, "Off");
            var on = State(layer, "On");
            Assert.That(Glossiness(on.motion).Evaluate(0f), Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(Glossiness(off.motion).Evaluate(0f), Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(off.transitions.Length, Is.EqualTo(2), "one way in per group");
            Assert.That(off.transitions.All(t => t.destinationState == on && t.conditions.Length == 2), Is.True, "each with its own condition and the gate");
            Assert.That(on.transitions.Length, Is.GreaterThan(0));
            Assert.That(on.transitions.All(t => t.destinationState == off), Is.True);
        }

        // Model and plan

        [Test]
        public void APropertyRowIsAnEffectOnEveryTriggerThatHoldsEffects()
        {
            var hat = HatToggle();
            var bone = avatar.Hat.AddComponent<VRCPhysBone>();
            var toggleSourced = menu.AddEntry(EntryKind.Trigger, "T");
            toggleSourced.source = TriggerSource.Toggle;
            toggleSourced.sourceToggleId = hat.id;
            var parameterSourced = menu.AddEntry(EntryKind.Trigger, "P");
            parameterSourced.source = TriggerSource.Parameter;
            parameterSourced.sourceParameter = "Existing";
            var hold = ContactTrigger("C", TouchMode.Hold);
            var fade = ContactTrigger("F", TouchMode.FollowDistance);
            var physBone = menu.AddEntry(EntryKind.Trigger, "B");
            physBone.source = TriggerSource.PhysBone;
            physBone.physBone = bone;
            var link = menu.AddEntry(EntryKind.Trigger, "L");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            var flip = ContactTrigger("X", TouchMode.FlipToggle);
            flip.flipToggleId = "";
            // A blend shape trigger builds property rows too.
            var builds = new[] { toggleSourced, parameterSourced, hold, fade, physBone, link };
            foreach (var entry in builds.Concat(new[] { flip })) entry.properties.Add(Shine(avatar, 0.7f));

            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });

            foreach (var entry in builds)
            {
                Assert.That(entry.HasEffects, Is.True, entry.name + ": a property row alone is an effect");
                Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.no_effects" && m.EntryIds.Contains(entry.id)), Is.False, entry.name);
            }
            Assert.That(flip.HasEffects, Is.False, "a flip trigger has no effects of its own");
        }

        [Test]
        public void TriggerPropertyRowsGetThePropertyWarnings()
        {
            var second = new Material(Shader.Find("Standard")) { name = "C" };
            try
            {
                second.SetFloat("_Glossiness", 0.8f);
                avatar.Body.sharedMaterials = new[] { avatar.MaterialA, second };
                var hat = HatToggle();
                var ghost = menu.AddEntry(EntryKind.Trigger, "Ghost");
                ghost.source = TriggerSource.Toggle;
                ghost.sourceToggleId = hat.id;
                ghost.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_NoSuchProperty", type = PropertyValueType.Float, value = 1f });
                var shine = ContactTrigger("Shine", TouchMode.Hold);
                shine.properties.Add(Shine(avatar, 1f));

                var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });

                Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.property_missing" && m.EntryIds.Contains(ghost.id)), Is.True, "a trigger row on a material without the property is reported");
                Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.property_differs" && m.EntryIds.Contains(shine.id)), Is.True, "and so are materials that disagree");
            }
            finally
            {
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void ATriggerPropertySharedWithAToggleIsReported()
        {
            var toggle = menu.AddEntry(EntryKind.Toggle, "Shiny");
            toggle.properties.Add(Shine(avatar, 0.9f));
            var pat = ContactTrigger("Pat", TouchMode.Hold);
            pat.properties.Add(Shine(avatar, 0.7f));

            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });

            var shared = tree.Messages.Single(m => m.Key == "ad.info.target_shared");
            Assert.That(shared.Args[0], Is.EqualTo("Body material._Glossiness"));
            Assert.That(shared.EntryIds, Is.EquivalentTo(new[] { toggle.id, pat.id }));
        }

        [Test]
        public void WhatThisBuildsCountsATriggerWithOnlyPropertyRows()
        {
            var hat = HatToggle();
            var trigger = menu.AddEntry(EntryKind.Trigger, "Shine");
            trigger.source = TriggerSource.Toggle;
            trigger.sourceToggleId = hat.id;
            trigger.properties.Add(Shine(avatar, 0.7f));
            var view = View();
            var row = view.Tree.RowFor(trigger.id);

            var summary = WhatThisBuildsModel.Compute(view.Plan, row);

            Assert.That(summary.FxLayers, Is.EqualTo(1), "the trigger's own layer holds the property");
            Assert.That(WhatThisBuildsModel.Describe(view.Plan, row), Is.EqualTo(string.Format(L.Text("ad.ui.builds.line"), UiText.Count(1, "ad.ui.builds.layers.one", "ad.ui.builds.layers"))));
        }

        [Test]
        public void WhatThisBuildsCountsOnlyTheLayersTheBuildMakes()
        {
            var hat = HatToggle();
            var pinOnlyTrigger = menu.AddEntry(EntryKind.Trigger, "PinOnly");
            pinOnlyTrigger.source = TriggerSource.Toggle;
            pinOnlyTrigger.sourceToggleId = hat.id;
            pinOnlyTrigger.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            var pinAndObject = menu.AddEntry(EntryKind.Trigger, "PinAndObject");
            pinAndObject.source = TriggerSource.Toggle;
            pinAndObject.sourceToggleId = hat.id;
            pinAndObject.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 100f, mode = ShapeEffectMode.Pin });
            pinAndObject.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var pinOnlyToggle = menu.AddEntry(EntryKind.Toggle, "PinToggle");
            pinOnlyToggle.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 50f, mode = ShapeEffectMode.Pin });
            var view = View();
            var counted = new Dictionary<string, int>();
            foreach (var entry in new[] { pinOnlyTrigger, pinAndObject, pinOnlyToggle })
                counted[entry.name] = WhatThisBuildsModel.Compute(view.Plan, view.Tree.RowFor(entry.id)).FxLayers;
            Assert.That(counted["PinOnly"], Is.EqualTo(1), "a Hold trigger with nothing but Pin shapes builds its pin layer alone");
            Assert.That(counted["PinAndObject"], Is.EqualTo(2), "an object keeps its main layer beside the pin layer");
            Assert.That(counted["PinToggle"], Is.EqualTo(1), "the same holds for a toggle");

            var names = LayerNames(BuildAll());

            foreach (var pair in counted)
                Assert.That(names.Count(n => n == "AD: " + pair.Key || n == "AD: " + pair.Key + " (pin)"), Is.EqualTo(pair.Value), pair.Key + ": the count matches the layers the build made");
        }

        // Scene preview

        [Test]
        public void AFiringHoldTriggerShowsItsPropertyInThePreview()
        {
            var pat = ContactTrigger("Pat", TouchMode.Hold);
            pat.properties.Add(Shine(avatar, 0.7f));
            var components = new[] { menu };

            var idle = PreviewModel.Compute(avatar.Root.transform, components, new Dictionary<string, float>(), SceneReader.Direct);
            Assert.That(idle.ContainsKey(avatar.Body), Is.False, "a trigger nobody fires changes nothing");

            var firing = PreviewModel.Compute(avatar.Root.transform, components, new Dictionary<string, float> { [pat.id] = 1f }, SceneReader.Direct);
            Assert.That(firing[avatar.Body].Properties["_Glossiness"].Value.x, Is.EqualTo(0.7f).Within(0.0001f), "firing shows the row's value");

            var forcedOff = PreviewModel.Compute(avatar.Root.transform, components, new Dictionary<string, float> { [pat.id] = 0f }, SceneReader.Direct);
            Assert.That(forcedOff.ContainsKey(avatar.Body), Is.False, "forced off rests at the material's own value, so the preview has nothing to change");
        }

        [Test]
        public void ATriggerForcedOffLeavesAToggleOwnedPropertyInThePreview()
        {
            var toggle = menu.AddEntry(EntryKind.Toggle, "Shiny");
            toggle.defaultOn = true;
            toggle.properties.Add(Shine(avatar, 0.9f));
            var pat = ContactTrigger("Pat", TouchMode.Hold);
            pat.properties.Add(Shine(avatar, 0.7f));
            var components = new[] { menu };

            var forcedOff = PreviewModel.Compute(avatar.Root.transform, components, new Dictionary<string, float> { [pat.id] = 0f }, SceneReader.Direct);
            Assert.That(forcedOff[avatar.Body].Properties["_Glossiness"].Value.x, Is.EqualTo(0.9f).Within(0.0001f),
                "the toggle's layer owns the property in the build, so the trigger's Off leaves it to the toggle, and so does the preview");

            var firing = PreviewModel.Compute(avatar.Root.transform, components, new Dictionary<string, float> { [pat.id] = 1f }, SceneReader.Direct);
            Assert.That(firing[avatar.Body].Properties["_Glossiness"].Value.x, Is.EqualTo(0.7f).Within(0.0001f), "a firing trigger still wins, built after every menu control");
        }

        [Test]
        public void ARestingHoldTriggerPutsTheMaterialValueBackOnAWriteDefaultsOnAvatar()
        {
            using (var owned = TestAvatar.Create(writeDefaultsOff: false))
            {
                owned.MaterialA.SetFloat("_Glossiness", 0.3f);
                var ownedMenu = owned.AddMenu();
                var toggle = ownedMenu.AddEntry(EntryKind.Toggle, "Shiny");
                toggle.defaultOn = true;
                toggle.properties.Add(Shine(owned, 0.9f));
                var hold = ownedMenu.AddEntry(EntryKind.Trigger, "Hold");
                hold.source = TriggerSource.Contact;
                hold.contactAnchor = owned.Hat.transform;
                hold.properties.Add(Shine(owned, 0.7f));
                var fade = ownedMenu.AddEntry(EntryKind.Trigger, "Fade");
                fade.source = TriggerSource.Contact;
                fade.contactAnchor = owned.Hat.transform;
                fade.touchMode = TouchMode.FollowDistance;
                fade.properties.Add(Shine(owned, 0.5f));
                var components = new[] { ownedMenu };

                var holdOff = PreviewModel.Compute(owned.Root.transform, components, new Dictionary<string, float> { [hold.id] = 0f }, SceneReader.Direct);
                Assert.That(holdOff.TryGetValue(owned.Body, out var holdState) && holdState.Properties.ContainsKey("_Glossiness"), Is.False,
                    "a Write Defaults on Off state writes the material's own value back over the toggle, so the preview shows the material as it is");

                var fadeOff = PreviewModel.Compute(owned.Root.transform, components, new Dictionary<string, float> { [fade.id] = 0f }, SceneReader.Direct);
                Assert.That(fadeOff[owned.Body].Properties["_Glossiness"].Value.x, Is.EqualTo(0.9f).Within(0.0001f),
                    "Far is Write Defaults off on every avatar, so it still leaves the property to the toggle");
            }
        }

        [Test]
        public void ARestingHoldTriggerWithAnOffValueShowsTheMaterialValueOnAWriteDefaultsOnAvatar()
        {
            using (var owned = TestAvatar.Create(writeDefaultsOff: false))
            {
                owned.MaterialA.SetFloat("_Glossiness", 0.3f);
                var ownedMenu = owned.AddMenu();
                var toggle = ownedMenu.AddEntry(EntryKind.Toggle, "Shiny");
                toggle.defaultOn = true;
                toggle.properties.Add(Shine(owned, 0.9f));
                var hold = ownedMenu.AddEntry(EntryKind.Trigger, "Hold");
                hold.source = TriggerSource.Contact;
                hold.contactAnchor = owned.Hat.transform;
                var row = Shine(owned, 0.7f);
                row.explicitOff = true;
                row.minValue = 0.45f;
                hold.properties.Add(row);
                var components = new[] { ownedMenu };

                var holdOff = PreviewModel.Compute(owned.Root.transform, components, new Dictionary<string, float> { [hold.id] = 0f }, SceneReader.Direct);

                Assert.That(holdOff.TryGetValue(owned.Body, out var state) && state.Properties.ContainsKey("_Glossiness"), Is.False,
                    "the toggle owns the property, so the built Off state carries no curve for it, and Write Defaults on writes the material's own value there, not the row's Off value");
            }
        }

        [Test]
        public void ABlendShapeLinkSeesAFadeTriggersShapeInThePreview()
        {
            var fade = ContactTrigger("Fade", TouchMode.FollowDistance);
            fade.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            var link = menu.AddEntry(EntryKind.Trigger, "Wide eyes");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            link.sourceThreshold = 50f;
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Fixed, weight = 40f });

            var states = PreviewModel.Compute(avatar.Root.transform, new[] { menu }, new Dictionary<string, float> { [fade.id] = 1f }, SceneReader.Direct);

            int eyeBig = avatar.Mesh.GetBlendShapeIndex("Eye_Big");
            Assert.That(states[avatar.Body].Shapes.TryGetValue(eyeBig, out var weight) ? weight : -1f, Is.EqualTo(40f),
                "the build copies Blush from the Fade trigger's Near clips for the link too, so the preview writes links after Fade triggers and the link reads Blush at 100");
        }

        [Test]
        public void ThePreviewWritesFadeTriggersAfterHoldTriggersLikeTheBuild()
        {
            var fade = ContactTrigger("Fade", TouchMode.FollowDistance);
            var fadeRow = Shine(avatar, 0.9f);
            fadeRow.explicitOff = true;
            fadeRow.minValue = 0.6f;
            fade.properties.Add(fadeRow);
            var hold = ContactTrigger("Hold", TouchMode.Hold);
            var holdRow = Shine(avatar, 0.7f);
            holdRow.explicitOff = true;
            holdRow.minValue = 0.4f;
            hold.properties.Add(holdRow);
            var components = new[] { menu };

            var bothOn = PreviewModel.Compute(avatar.Root.transform, components, new Dictionary<string, float> { [fade.id] = 1f, [hold.id] = 1f }, SceneReader.Direct);
            Assert.That(bothOn[avatar.Body].Properties["_Glossiness"].Value.x, Is.EqualTo(0.9f).Within(0.0001f), "the Fade trigger is first in the tree, but its layer sits above every Hold layer");
            var bothOff = PreviewModel.Compute(avatar.Root.transform, components, new Dictionary<string, float> { [fade.id] = 0f, [hold.id] = 0f }, SceneReader.Direct);
            Assert.That(bothOff[avatar.Body].Properties["_Glossiness"].Value.x, Is.EqualTo(0.4f).Within(0.0001f), "both resting: the Hold layer holds the rest, and Far leaves it alone");

            var fx = BuildAll();

            var names = LayerNames(fx);
            Assert.That(names.IndexOf("AD: Fade (distance)"), Is.GreaterThan(names.IndexOf("AD: Hold")), "the build agrees: Fade's layer is above");
            Assert.That(Glossiness(State(Layer(fx, "AD: Hold"), "Off").motion).Evaluate(0f), Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(Glossiness(State(Layer(fx, "AD: Fade (distance)"), "Far").motion), Is.Null);
        }

        // Inspector

        [Test]
        public void AddEffectOffersPropertiesOnEveryTriggerButBlendShapeAndFlip()
        {
            var hat = HatToggle();
            var bone = avatar.Hat.AddComponent<VRCPhysBone>();
            var toggleSourced = menu.AddEntry(EntryKind.Trigger, "T");
            toggleSourced.source = TriggerSource.Toggle;
            toggleSourced.sourceToggleId = hat.id;
            var parameterSourced = menu.AddEntry(EntryKind.Trigger, "P");
            parameterSourced.source = TriggerSource.Parameter;
            parameterSourced.sourceParameter = "Existing";
            var hold = ContactTrigger("C", TouchMode.Hold);
            var fade = ContactTrigger("F", TouchMode.FollowDistance);
            var physBone = menu.AddEntry(EntryKind.Trigger, "B");
            physBone.source = TriggerSource.PhysBone;
            physBone.physBone = bone;
            var link = menu.AddEntry(EntryKind.Trigger, "L");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            var flip = ContactTrigger("X", TouchMode.FlipToggle);
            var view = View();

            var withFollows = new[] { "ad.ui.objects", "ad.ui.blendshapes", "ad.ui.materials", "ad.ui.targets.properties", "ad.ui.targets.follows" };
            foreach (var entry in new[] { toggleSourced, parameterSourced, hold, physBone })
            {
                view.Tree.SelectEntry(entry.id);
                Assert.That(view.Detail.EffectSections.Select(s => s.NameKey), Is.EqualTo(withFollows), entry.name + " offers Properties after Materials");
                var section = view.Detail.Q<TargetListView>("ad-properties");
                Assert.That(section, Is.Not.Null, entry.name);
                Assert.That(section.ClassListContains("ad-hidden"), Is.True, entry.name + ": no row yet, so the section waits for + Add effect");
            }

            view.Tree.SelectEntry(fade.id);
            Assert.That(view.Detail.EffectSections.Select(s => s.NameKey), Is.EqualTo(new[] { "ad.ui.objects", "ad.ui.blendshapes", "ad.ui.materials", "ad.ui.targets.properties" }), "Fade offers Properties too, and still no Follows");

            // A blend shape trigger offers Properties too, and still no Follows.
            view.Tree.SelectEntry(link.id);
            Assert.That(view.Detail.EffectSections.Select(s => s.NameKey), Is.EqualTo(new[] { "ad.ui.objects", "ad.ui.blendshapes", "ad.ui.materials", "ad.ui.targets.properties" }), "a blend shape trigger offers Properties, and no Follows");
            Assert.That(view.Detail.Q<TargetListView>("ad-properties"), Is.Not.Null);

            view.Tree.SelectEntry(flip.id);
            Assert.That(view.Detail.EffectSections, Is.Empty, "a flip trigger has no effects of its own");
            Assert.That(view.Detail.Q<Button>("ad-add-effect"), Is.Null);
            Assert.That(view.Detail.Q<TargetListView>("ad-properties"), Is.Null);
        }

        [Test]
        public void AddingAPropertyToATriggerShowsItsSection()
        {
            var pat = ContactTrigger("Pat", TouchMode.Hold);
            pat.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var view = View();
            view.Tree.SelectEntry(pat.id);
            var properties = view.Detail.EffectSections.Single(s => s.NameKey == "ad.ui.targets.properties").View;
            Assert.That(properties.ClassListContains("ad-hidden"), Is.True);

            properties.AddProperty(avatar.Body, "_Glossiness");

            Assert.That(menu.entries.Single(e => e.id == pat.id).properties.Count, Is.EqualTo(1), "the row went into the trigger");
            var shown = view.Detail.Q<TargetListView>("ad-properties");
            Assert.That(shown.ClassListContains("ad-hidden"), Is.False, "the section shows while it has a row");
            Assert.That(shown.Count, Is.EqualTo(1));
        }
    }
}
#endif
