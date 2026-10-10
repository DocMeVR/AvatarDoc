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
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>A menu switch that gates a trigger (phase 9 task 6), and toggles hidden from the menu (its addition).</summary>
    public class TriggerGateTests
    {
        TestAvatar avatar;
        AvatarDoc menu;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            menu = avatar.AddMenu();
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

        static AnimatorController FxOf(TestAvatar a) => (AnimatorController)a.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
        static AnimatorControllerLayer Layer(AnimatorController fx, string name) => fx.layers.Single(l => l.name == name);
        static AnimatorState State(AnimatorControllerLayer layer, string name) => layer.stateMachine.states.Single(s => s.state.name == name).state;

        MenuEntry Trigger(string name)
        {
            var entry = menu.AddEntry(EntryKind.Trigger, name);
            entry.objects.Add(new ObjectTarget { target = avatar.Glasses });
            return entry;
        }

        /// <summary>Model, plan, defaults, toggle layers, then trigger layers, inside an NDMF context; returns the committed FX.</summary>
        AnimatorController BuildWithTriggers(out List<ErrorContext> errors, out MenuTree tree, out ParameterPlan plan)
        {
            var builtTree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var virtualFx);
            var existing = ParametersWriter.ExistingParameters(avatar.Descriptor.expressionParameters, virtualFx);
            ParametersWriter.AddProvided(existing, ParameterInfo.ForContext(context).GetParametersForObject(avatar.Root));
            var builtPlan = ParameterPlanner.Plan(builtTree, existing);
            var (plans, planMessages) = TriggerPlanner.Plan(builtTree, builtPlan, existing);
            var baseState = BaseState.Capture(builtTree);
            DefaultsApplier.Apply(builtTree, builtPlan, baseState);
            var captured = ErrorReport.CaptureErrors(() =>
            {
                L.Report(planMessages);
                L.Report(AnimatorBuilder.Build(asc, builtTree, builtPlan, baseState));
                L.Report(TriggerBuilder.Build(asc, builtTree, plans, baseState));
            });
            context.DeactivateAllExtensionContexts();
            errors = captured;
            tree = builtTree;
            plan = builtPlan;
            return FxOf(avatar);
        }

        /// <summary>The same pipeline plus touch receivers and flip layers, for a Contact or PhysBone sourced trigger.</summary>
        AnimatorController BuildFull(out List<ErrorContext> errors)
        {
            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var virtualFx);
            var existing = ParametersWriter.ExistingParameters(avatar.Descriptor.expressionParameters, virtualFx);
            var plan = ParameterPlanner.Plan(tree, existing);
            var touch = TouchBuilder.Describe(tree, existing, plan);
            var (plans, planMessages) = TriggerPlanner.Plan(tree, plan, existing, touch);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState, plans);
            var captured = ErrorReport.CaptureErrors(() =>
            {
                L.Report(planMessages);
                L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState));
                L.Report(TriggerBuilder.Build(asc, tree, plans, baseState));
                L.Report(TouchBuilder.Create(context, tree, touch));
                L.Report(TouchBuilder.BuildFlipLayers(asc, tree, plan, touch));
            });
            context.DeactivateAllExtensionContexts();
            errors = captured;
            return FxOf(avatar);
        }

        [Test]
        public void GatedHoldTriggerNeedsSourceAndGate()
        {
            var gate = menu.AddEntry(EntryKind.Toggle, "Enabled");
            var trigger = Trigger("Ears");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "AFK";
            trigger.condition = ParameterCondition.IsTrue;
            trigger.gateToggleId = gate.id;

            var fx = BuildWithTriggers(out var errors, out _, out _);

            Assert.That(errors, Is.Empty);
            var toOn = State(Layer(fx, "AD: Ears"), "Off").transitions.Single();
            Assert.That(toOn.conditions.Length, Is.EqualTo(2), "the source condition and the gate condition");
            Assert.That(toOn.conditions.Any(c => c.parameter == "AFK"), Is.True);
            Assert.That(toOn.conditions.Any(c => c.parameter == "AD/Enabled"), Is.True);
        }

        [Test]
        public void GatedHoldTriggerLeavesWhenTheGateCloses()
        {
            var gate = menu.AddEntry(EntryKind.Toggle, "Enabled");
            var trigger = Trigger("Ears");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "AFK";
            trigger.condition = ParameterCondition.IsTrue;
            trigger.gateToggleId = gate.id;

            var fx = BuildWithTriggers(out var errors, out _, out _);

            Assert.That(errors, Is.Empty);
            var on = State(Layer(fx, "AD: Ears"), "On");
            Assert.That(on.transitions.Length, Is.EqualTo(2), "leaving when the source drops, and leaving when the gate closes");
            Assert.That(on.transitions.Any(t => t.conditions.Length == 1 && t.conditions[0].parameter == "AFK"), Is.True);
            Assert.That(on.transitions.Any(t => t.conditions.Length == 1 && t.conditions[0].parameter == "AD/Enabled"), Is.True);
        }

        [Test]
        public void GateOffStateUsesTheOppositeCondition()
        {
            var gate = menu.AddEntry(EntryKind.Toggle, "Enabled");
            var trigger = Trigger("Ears");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "AFK";
            trigger.condition = ParameterCondition.IsTrue;
            trigger.gateToggleId = gate.id;
            trigger.gateToggleOn = false;

            var fx = BuildWithTriggers(out var errors, out _, out _);

            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: Ears");
            var enterGate = State(layer, "Off").transitions.Single().conditions.Single(c => c.parameter == "AD/Enabled");
            Assert.That(enterGate.mode, Is.EqualTo(AnimatorConditionMode.IfNot), "entering wants the gate toggle off");
            var leaveGate = State(layer, "On").transitions.Single(t => t.conditions.Length == 1 && t.conditions[0].parameter == "AD/Enabled").conditions[0];
            Assert.That(leaveGate.mode, Is.EqualTo(AnimatorConditionMode.If), "leaving fires as soon as the gate toggle turns on");
        }

        [Test]
        public void GateOnAGroupMemberUsesItsValue()
        {
            var a = menu.AddEntry(EntryKind.Toggle, "A");
            a.exclusiveGroup = "Outfits";
            var b = menu.AddEntry(EntryKind.Toggle, "B");
            b.exclusiveGroup = "Outfits";
            var trigger = Trigger("Ears");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "AFK";
            trigger.condition = ParameterCondition.IsTrue;
            trigger.gateToggleId = a.id;

            var fx = BuildWithTriggers(out var errors, out var tree, out var plan);

            Assert.That(errors, Is.Empty);
            var binding = plan.Bindings[tree.FindControl(a.id)];
            var layer = Layer(fx, "AD: Ears");
            var enterGate = State(layer, "Off").transitions.Single().conditions.Single(c => c.parameter == binding.Parameter.Name);
            Assert.That(enterGate.mode, Is.EqualTo(AnimatorConditionMode.Equals));
            Assert.That(enterGate.threshold, Is.EqualTo(binding.Value));
            var leaveGate = State(layer, "On").transitions.Single(t => t.conditions.Length == 1 && t.conditions[0].parameter == binding.Parameter.Name).conditions[0];
            Assert.That(leaveGate.mode, Is.EqualTo(AnimatorConditionMode.NotEqual));
            Assert.That(leaveGate.threshold, Is.EqualTo(binding.Value));
        }

        [Test]
        public void GatedFlipTriggerIgnoresTouchesWhileClosed()
        {
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var gate = menu.AddEntry(EntryKind.Toggle, "Enabled");
            var trigger = menu.AddEntry(EntryKind.Trigger, "Flip");
            trigger.source = TriggerSource.Contact;
            trigger.contactAnchor = avatar.Glasses.transform;
            trigger.touchMode = TouchMode.FlipToggle;
            trigger.flipToggleId = hat.id;
            trigger.gateToggleId = gate.id;

            var fx = BuildFull(out var errors);

            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: Flip (flip)");
            var idle = State(layer, "Idle");
            var setOn = State(layer, "Set on");
            var setOff = State(layer, "Set off");
            var toOn = idle.transitions.Single(t => t.destinationState == setOn);
            var toOff = idle.transitions.Single(t => t.destinationState == setOff);
            Assert.That(toOn.conditions.Length, Is.EqualTo(3), "the touch, the toggle being off, and the gate");
            Assert.That(toOn.conditions.Any(c => c.parameter == "AD/Enabled"), Is.True);
            Assert.That(toOff.conditions.Length, Is.EqualTo(3), "the touch, the toggle being on, and the gate");
            Assert.That(toOff.conditions.Any(c => c.parameter == "AD/Enabled"), Is.True);
            // Releasing an already open flip is not gated: only entering it is.
            Assert.That(setOn.transitions.Single().conditions.Any(c => c.parameter == "AD/Enabled"), Is.False);
        }

        [Test]
        public void GateOnABlendShapeLinkIsAnError()
        {
            var gate = menu.AddEntry(EntryKind.Toggle, "Enabled");
            var link = menu.AddEntry(EntryKind.Trigger, "Link");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            link.gateToggleId = gate.id;

            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            // A blend shape trigger has a layer of its own, so Only while is one more requirement on
            // it rather than an error; the old error key is gone.
            Assert.That(tree.Messages.Any(m => m.Severity == MessageSeverity.Error && m.EntryIds.Contains(link.id)), Is.False);
            var missing = menu.AddEntry(EntryKind.Trigger, "Link 2");
            missing.source = TriggerSource.BlendShape;
            missing.sourceRenderer = avatar.Body;
            missing.sourceShape = "Blush";
            missing.gateToggleId = "gone";
            tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });
            Assert.That(tree.Messages.Any(m => m.Key == "ad.error.gate_target" && m.EntryIds.Contains(missing.id)), Is.True, "a broken Only while is an error on it like on any trigger");
        }

        [Test]
        public void GateTargetMustBeAToggleAndNotTheFlipTarget()
        {
            var preset = menu.AddEntry(EntryKind.Preset, "Look");
            var badGate = Trigger("T1");
            badGate.source = TriggerSource.Parameter;
            badGate.sourceParameter = "AFK";
            badGate.gateToggleId = preset.id;

            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            var flip = menu.AddEntry(EntryKind.Trigger, "Flip");
            flip.source = TriggerSource.Contact;
            flip.contactAnchor = avatar.Glasses.transform;
            flip.touchMode = TouchMode.FlipToggle;
            flip.flipToggleId = hat.id;
            flip.gateToggleId = hat.id;

            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            Assert.That(tree.Messages.Count(m => m.Key == "ad.error.gate_target"), Is.EqualTo(2));
            Assert.That(tree.Messages.Any(m => m.Key == "ad.error.gate_target" && m.EntryIds.Contains(badGate.id)), Is.True, "a preset is not a toggle");
            Assert.That(tree.Messages.Any(m => m.Key == "ad.error.gate_target" && m.EntryIds.Contains(flip.id)), Is.True, "a gate cannot be the trigger's own flip target");
        }

        [Test]
        public void GateSwitchToggleDoesNotWarnAboutNoEffects()
        {
            var trigger = Trigger("Ears");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "AFK";
            var toggle = EntryEditing.AddGateSwitch(menu, trigger);

            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            Assert.That(toggle, Is.Not.Null);
            Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.no_effects" && m.EntryIds.Contains(toggle.id)), Is.False);
        }

        [Test]
        public void AddGateSwitchCreatesSelectsAndIsOneUndoStep()
        {
            var trigger = Trigger("Ears");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "AFK";

            Undo.IncrementCurrentGroup();
            var toggle = EntryEditing.AddGateSwitch(menu, trigger);

            Assert.That(toggle, Is.Not.Null);
            Assert.That(toggle.kind, Is.EqualTo(EntryKind.Toggle));
            Assert.That(toggle.PlainName, Is.EqualTo(trigger.PlainName));
            Assert.That(toggle.defaultOn, Is.True);
            Assert.That(toggle.saved, Is.True);
            Assert.That(toggle.synced, Is.True);
            Assert.That(trigger.gateToggleId, Is.EqualTo(toggle.id));
            Assert.That(menu.entries.Count, Is.EqualTo(2));

            Undo.PerformUndo();

            Assert.That(menu.entries.Count, Is.EqualTo(1), "the created toggle is undone");
            Assert.That(menu.entries[0].gateToggleId, Is.Empty, "the gate assignment is undone in the same step");
        }

        [Test]
        public void PreviewAndTestModeRespectTheGate()
        {
            var gate = menu.AddEntry(EntryKind.Toggle, "Enabled");
            var trigger = menu.AddEntry(EntryKind.Trigger, "Boop");
            trigger.source = TriggerSource.Contact;
            trigger.contactAnchor = avatar.Hat.transform;
            trigger.objects.Add(new ObjectTarget { target = avatar.Glasses });
            trigger.gateToggleId = gate.id;
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            view.TestMode = true;
            var wheel = view.Wheel;
            var button = wheel.Query<Button>(className: "ad-wheel-trigger").ToList().Single(b => (string)b.userData == trigger.id);
            Assert.That(button.enabledSelf, Is.False, "Enabled defaults off, so the gate starts closed");

            wheel.PressTrigger(trigger.id);

            Assert.That(PreviewState.TryGet(trigger.id, out _), Is.False, "the closed gate means the press does nothing");

            PreviewState.Set(gate.id, 1f);
            wheel.RefreshWedgeStates();

            Assert.That(button.enabledSelf, Is.True, "the gate is open now, so the button is no longer dimmed");
            wheel.PressTrigger(trigger.id);

            Assert.That(PreviewState.TryGet(trigger.id, out var value) && value >= 0.5f, Is.True, "the gate is open now, so the press sets the override");
        }

        [Test]
        public void DuplicatingRemapsTheGate()
        {
            var sub = EntryEditing.Add(menu, EntryKind.Submenu, "Pack", "");
            var gate = EntryEditing.Add(menu, EntryKind.Toggle, "Enabled", sub.id);
            var trigger = EntryEditing.Add(menu, EntryKind.Trigger, "Boop", sub.id);
            trigger.gateToggleId = gate.id;

            var copy = EntryEditing.Duplicate(menu, sub);

            var inside = EntryEditing.Descendants(menu, copy);
            var copiedGate = inside.Single(e => e.kind == EntryKind.Toggle);
            var copiedTrigger = inside.Single(e => e.kind == EntryKind.Trigger);
            Assert.That(copiedTrigger.gateToggleId, Is.EqualTo(copiedGate.id), "the copy gates on its own toggle");
            Assert.That(trigger.gateToggleId, Is.EqualTo(gate.id), "the original is left alone");
        }

        // --- Toggles hidden from the menu (addition) ---

        [Test]
        public void HiddenToggleHasNoControlButKeepsItsParameterAndLayer()
        {
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            toggle.hiddenFromMenu = true;
            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();

            var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();

            Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error), Is.Empty);
            var fx = FxOf(avatar);
            Assert.That(fx.layers.Select(l => l.name), Does.Contain("AD: Hat"), "the layer still builds");
            Assert.That(avatar.Descriptor.expressionsMenu.controls.Any(c => c.name == "Hat"), Is.False, "no menu control");
            Assert.That(avatar.Descriptor.expressionParameters.parameters.Any(p => p.name == "AD/Hat"), Is.True, "the parameter still exists");
        }

        [Test]
        public void HidingAToggleChangesNoParameter()
        {
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            var shownTree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });
            var shownPlan = ParameterPlanner.Plan(shownTree, new Dictionary<string, ExistingParameter>());

            toggle.hiddenFromMenu = true;
            var hiddenTree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });
            var hiddenPlan = ParameterPlanner.Plan(hiddenTree, new Dictionary<string, ExistingParameter>());

            Assert.That(hiddenPlan.Parameters.Select(p => (p.Name, p.Type, p.Synced, p.Saved, p.Default)),
                Is.EqualTo(shownPlan.Parameters.Select(p => (p.Name, p.Type, p.Synced, p.Saved, p.Default))));
        }

        [Test]
        public void FlipTriggerCanFlipAHiddenToggle()
        {
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            hat.hiddenFromMenu = true;
            var trigger = menu.AddEntry(EntryKind.Trigger, "Flip");
            trigger.source = TriggerSource.Contact;
            trigger.contactAnchor = avatar.Glasses.transform;
            trigger.touchMode = TouchMode.FlipToggle;
            trigger.flipToggleId = hat.id;

            var fx = BuildFull(out var errors);

            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: Flip (flip)");
            var driverOn = State(layer, "Set on").behaviours.OfType<VRC.SDK3.Avatars.Components.VRCAvatarParameterDriver>().Single();
            Assert.That(driverOn.parameters.Single().name, Is.EqualTo("AD/Hat"));
        }

        [Test]
        public void PresetCanSetAHiddenToggle()
        {
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            hat.hiddenFromMenu = true;
            var preset = menu.AddEntry(EntryKind.Preset, "Look");
            preset.presetStates.Add(new PresetState { entryId = hat.id, on = true });

            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.preset_target_missing"), Is.False);
            PreviewState.ApplyPreset(preset, tree);
            Assert.That(PreviewState.TryGet(hat.id, out var value) && value >= 0.5f, Is.True);
        }

        [Test]
        public void HiddenToggleShowsOnTheTestStrip()
        {
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            hat.hiddenFromMenu = true;
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            view.TestMode = true;
            var wheel = view.Wheel;

            Assert.That(wheel.Model.Wedges.Any(w => w.EntryId == hat.id), Is.False, "Select mode leaves it out");
            Assert.That(wheel.Model.HiddenControls.Any(h => h.entryId == hat.id), Is.True, "the test mode strip lists it");

            wheel.PressHiddenToggle(hat.id);

            Assert.That(PreviewState.TryGet(hat.id, out var value) && value >= 0.5f, Is.True);
        }

        [Test]
        public void UnreachableHiddenToggleIsReported()
        {
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            hat.hiddenFromMenu = true;

            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            Assert.That(tree.Messages.Any(m => m.Key == "ad.info.hidden_toggle_unreachable" && m.EntryIds.Contains(hat.id)), Is.True);

            hat.defaultOn = true;
            var onTree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            Assert.That(onTree.Messages.Any(m => m.Key == "ad.info.hidden_toggle_unreachable"), Is.False, "on by default is reachable in effect");
        }

        [Test]
        public void HiddenToggleThatIsOnlyReadIsStillUnreachable()
        {
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            hat.hiddenFromMenu = true;
            var gated = menu.AddEntry(EntryKind.Trigger, "Gated");
            gated.gateToggleId = hat.id;
            var reading = menu.AddEntry(EntryKind.Trigger, "Reading");
            reading.source = TriggerSource.Toggle;
            reading.sourceToggleId = hat.id;

            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            Assert.That(tree.Messages.Any(m => m.Key == "ad.info.hidden_toggle_unreachable" && m.EntryIds.Contains(hat.id)), Is.True,
                "a gate and a condition read the toggle, neither can change it");

            var flip = menu.AddEntry(EntryKind.Trigger, "Flip");
            flip.flipToggleId = hat.id;
            var flipTree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            Assert.That(flipTree.Messages.Any(m => m.Key == "ad.info.hidden_toggle_unreachable" && m.EntryIds.Contains(hat.id)), Is.False);
        }
    }
}
#endif
