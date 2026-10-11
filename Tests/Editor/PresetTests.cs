using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace DocMe.AvatarDoctor.Editor
{
    public class PresetTests
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

        MenuEntry Toggle(string name, GameObject target = null)
        {
            var entry = menu.AddEntry(EntryKind.Toggle, name);
            if (target != null) entry.objects.Add(new ObjectTarget { target = target });
            return entry;
        }

        MenuEntry Radial(string name)
        {
            var entry = menu.AddEntry(EntryKind.Radial, name);
            entry.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big" });
            return entry;
        }

        MenuEntry Preset(string name) => menu.AddEntry(EntryKind.Preset, name);

        (MenuTree tree, ParameterPlan plan) Model()
        {
            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });
            return (tree, ParameterPlanner.Plan(tree, new Dictionary<string, ExistingParameter>()));
        }

        static AnimatorController FxOf(TestAvatar a) => (AnimatorController)a.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
        static AnimatorControllerLayer Layer(AnimatorController fx, string name) => fx.layers.Single(l => l.name == name);
        static AnimatorState State(AnimatorControllerLayer layer, string name) => layer.stateMachine.states.Single(s => s.state.name == name).state;

        /// <summary>Model, plan, defaults, toggle layers, then preset layers, inside an NDMF context; returns the committed FX.</summary>
        AnimatorController BuildWithPresets(out List<ErrorContext> errors, out ParameterPlan plan)
        {
            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var virtualFx);
            var existing = ParametersWriter.ExistingParameters(avatar.Descriptor.expressionParameters, virtualFx);
            ParametersWriter.AddProvided(existing, ParameterInfo.ForContext(context).GetParametersForObject(avatar.Root));
            var builtPlan = ParameterPlanner.Plan(tree, existing);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, builtPlan, baseState);
            var captured = ErrorReport.CaptureErrors(() =>
            {
                L.Report(AnimatorBuilder.Build(asc, tree, builtPlan, baseState));
                L.Report(PresetBuilder.Build(asc, tree, builtPlan));
            });
            context.DeactivateAllExtensionContexts();
            errors = captured;
            plan = builtPlan;
            return FxOf(avatar);
        }

        [Test]
        public void PresetGetsAnUnsyncedBoolAndCostsNoBits()
        {
            var hat = Toggle("Hat", avatar.Hat);
            var preset = Preset("Casual");
            preset.presetStates.Add(new PresetState { entryId = hat.id, on = true });
            var (_, plan) = Model();

            var p = plan.Parameters.Single(x => x.Name == "AD/Casual");
            Assert.That(p.Type, Is.EqualTo(PlannedType.Bool));
            Assert.That(p.Saved, Is.False);
            Assert.That(p.Synced, Is.False);
            Assert.That(p.Bits, Is.EqualTo(0));
            Assert.That(plan.NewSyncedBits, Is.EqualTo(1), "only the toggle's own bit is counted");
        }

        [Test]
        public void MenuControlIsAButton()
        {
            var preset = Preset("Casual");
            preset.presetParameters.Add(new PresetParameter { name = "Foo" });
            var (tree, plan) = Model();
            var context = new BuildContext(avatar.Root, null);

            MenuWriter.Write(context, avatar.Descriptor, tree, plan);

            var control = avatar.Descriptor.expressionsMenu.controls.Single(c => c.name == "Casual");
            Assert.That(control.type, Is.EqualTo(VRCExpressionsMenu.Control.ControlType.Button));
            Assert.That(control.parameter.name, Is.EqualTo("AD/Casual"));
            Assert.That(control.value, Is.EqualTo(1f));
        }

        [Test]
        public void LayerHasIdleAndApplyWithALocalDriver()
        {
            var hat = Toggle("Hat", avatar.Hat);
            var preset = Preset("Casual");
            preset.presetStates.Add(new PresetState { entryId = hat.id, on = true });

            var fx = BuildWithPresets(out var errors, out _);

            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: Casual (preset)");
            var idle = State(layer, "Idle");
            var apply = State(layer, "Apply");
            Assert.That(layer.stateMachine.defaultState, Is.SameAs(idle));
            Assert.That(AnimationUtility.GetCurveBindings((AnimationClip)idle.motion), Is.Empty);
            Assert.That(AnimationUtility.GetCurveBindings((AnimationClip)apply.motion), Is.Empty);
            var toApply = idle.transitions.Single();
            Assert.That(toApply.destinationState, Is.SameAs(apply));
            Assert.That(toApply.conditions.Single().parameter, Is.EqualTo("AD/Casual"));
            Assert.That(toApply.conditions.Single().mode, Is.EqualTo(AnimatorConditionMode.If));
            var toIdle = apply.transitions.Single();
            Assert.That(toIdle.destinationState, Is.SameAs(idle));
            Assert.That(toIdle.conditions.Single().mode, Is.EqualTo(AnimatorConditionMode.IfNot));
            var driver = apply.behaviours.OfType<VRCAvatarParameterDriver>().Single();
            Assert.That(driver.localOnly, Is.True);
            var op = driver.parameters.Single();
            Assert.That(op.name, Is.EqualTo("AD/Hat"));
            Assert.That(op.value, Is.EqualTo(1f));
            Assert.That(op.type, Is.EqualTo(VRC_AvatarParameterDriver.ChangeType.Set));
        }

        [Test]
        public void OperationsCoverTogglesRadialsAndNamedParameters()
        {
            var hat = Toggle("Hat", avatar.Hat);
            var glasses = Toggle("Glasses", avatar.Glasses);
            var eyes = Radial("Eyes");
            var preset = Preset("Casual");
            preset.presetStates.Add(new PresetState { entryId = hat.id, on = true });
            preset.presetStates.Add(new PresetState { entryId = glasses.id, on = false });
            preset.presetStates.Add(new PresetState { entryId = eyes.id, value = 0.75f });
            preset.presetParameters.Add(new PresetParameter { name = "VRCEmote", value = 3f });
            var (tree, plan) = Model();
            var messages = new List<ModelMessage>();

            var ops = PresetBuilder.Operations(preset, tree, plan, messages);

            Assert.That(ops, Is.EquivalentTo(new[] { ("AD/Hat", 1f), ("AD/Glasses", 0f), ("AD/Eyes", 0.75f), ("VRCEmote", 3f) }));
            Assert.That(messages, Is.Empty);
        }

        [Test]
        public void GroupMemberOnWritesTheGroupValue()
        {
            var a = Toggle("Casual", avatar.Hat); a.exclusiveGroup = "Outfits";
            var b = Toggle("Suit", avatar.Glasses); b.exclusiveGroup = "Outfits";
            var preset = Preset("GoCasual");
            preset.presetStates.Add(new PresetState { entryId = a.id, on = true });
            var (tree, plan) = Model();
            var messages = new List<ModelMessage>();

            var ops = PresetBuilder.Operations(preset, tree, plan, messages);

            Assert.That(ops.Single(), Is.EqualTo(("AD/Outfits", 1f)));
        }

        [Test]
        public void GroupMembersOffWriteZeroOnce()
        {
            var a = Toggle("Casual", avatar.Hat); a.exclusiveGroup = "Outfits";
            var b = Toggle("Suit", avatar.Glasses); b.exclusiveGroup = "Outfits";
            var preset = Preset("NoOutfit");
            preset.presetStates.Add(new PresetState { entryId = a.id, on = false });
            preset.presetStates.Add(new PresetState { entryId = b.id, on = false });
            var (tree, plan) = Model();
            var messages = new List<ModelMessage>();

            var ops = PresetBuilder.Operations(preset, tree, plan, messages);

            Assert.That(ops, Is.EqualTo(new[] { ("AD/Outfits", 0f) }));
        }

        [Test]
        public void TwoMembersOnIsAnError()
        {
            var a = Toggle("Casual", avatar.Hat); a.exclusiveGroup = "Outfits";
            var b = Toggle("Suit", avatar.Glasses); b.exclusiveGroup = "Outfits";
            var preset = Preset("Conflict");
            preset.presetStates.Add(new PresetState { entryId = a.id, on = true });
            preset.presetStates.Add(new PresetState { entryId = b.id, on = true });
            var (tree, plan) = Model();
            var messages = new List<ModelMessage>();

            var ops = PresetBuilder.Operations(preset, tree, plan, messages);

            Assert.That(messages.Any(m => m.Key == "ad.error.preset_group_conflict" && m.Severity == MessageSeverity.Error), Is.True);
            Assert.That(ops.Single(), Is.EqualTo(("AD/Outfits", 1f)), "the first member on still wins the write");
        }

        [Test]
        public void MissingTargetWarns()
        {
            var preset = Preset("Ghost");
            preset.presetStates.Add(new PresetState { entryId = "does-not-exist", on = true });
            var (tree, plan) = Model();
            var messages = new List<ModelMessage>();

            var ops = PresetBuilder.Operations(preset, tree, plan, messages);

            Assert.That(ops, Is.Empty);
            Assert.That(messages.Any(m => m.Key == "ad.warn.preset_target_missing"), Is.True);
        }

        [Test]
        public void ARowThatNeverHadAControlAsksForOne()
        {
            var hat = Toggle("Hat", avatar.Hat);
            var preset = Preset("Look");
            preset.presetStates.Add(new PresetState { entryId = hat.id, on = true });
            preset.presetStates.Add(new PresetState { entryId = "", on = true });
            preset.presetStates.Add(new PresetState { entryId = "", on = false });
            var (tree, plan) = Model();

            var ask = tree.Messages.Single(m => m.Key == "ad.info.preset_pick_control");
            Assert.That(ask.Severity, Is.EqualTo(MessageSeverity.Info), "a new row is not a mistake yet");
            Assert.That(ask.EntryIds, Does.Contain(preset.id));
            Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.preset_target_missing"), Is.False, "nothing was deleted, so nothing is missing");

            var messages = new List<ModelMessage>();
            var ops = PresetBuilder.Operations(preset, tree, plan, messages);
            Assert.That(ops.Count, Is.EqualTo(1), "the build sets the Hat and passes over the empty rows");
            Assert.That(messages, Is.Empty, "without saying it again");
        }

        [Test]
        public void AnEmptyRowsButtonAsksToPickAControl()
        {
            Toggle("Hat", avatar.Hat);
            var preset = Preset("Look");
            preset.presetStates.Add(new PresetState { entryId = "", on = true });
            preset.presetStates.Add(new PresetState { entryId = "gone", on = true });
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);

            view.Tree.SelectEntry(preset.id);

            var buttons = UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.Button>(view.Detail, "ad-preset-control").ToList();
            Assert.That(buttons[0].text, Is.EqualTo(L.Text("ad.ui.preset.pick")));
            Assert.That(buttons[0].ClassListContains("ad-text-warn"), Is.False, "a row that never had a control is no warning");
            Assert.That(buttons[1].text, Is.EqualTo(L.Text("ad.ui.preset.missing")), "a control that was deleted still says so");
            Assert.That(buttons[1].ClassListContains("ad-text-warn"), Is.True);
        }

        [Test]
        public void TwoGroupMembersOnShowAsAnErrorBeforeTheBuild()
        {
            var a = Toggle("Casual", avatar.Hat); a.exclusiveGroup = "Outfits";
            var b = Toggle("Suit", avatar.Glasses); b.exclusiveGroup = "Outfits";
            var preset = Preset("Conflict");
            preset.presetStates.Add(new PresetState { entryId = a.id, on = true });
            preset.presetStates.Add(new PresetState { entryId = b.id, on = true });
            AvatarPlanCache.Invalidate();

            var plan = AvatarPlanCache.GetFor(menu);

            var error = plan.Messages.Single(m => m.Key == "ad.error.preset_group_conflict");
            Assert.That(error.Severity, Is.EqualTo(MessageSeverity.Error), "the same severity the build gives it");
            Assert.That(error.EntryIds, Does.Contain(preset.id), "shown on the preset's own row");
            Assert.That(plan.MessagesForId(preset.id).Any(m => m.Key == "ad.error.preset_group_conflict"), Is.True);
        }

        [Test]
        public void AnUnknownOtherParameterWarnsBeforeTheBuild()
        {
            var preset = Preset("SetMood");
            preset.presetParameters.Add(new PresetParameter { name = "Mystery", value = 1f });
            preset.presetParameters.Add(new PresetParameter { name = "Existing", value = 1f });
            AvatarPlanCache.Invalidate();

            var plan = AvatarPlanCache.GetFor(menu);

            var warnings = plan.Messages.Where(m => m.Key == "ad.warn.preset_parameter_unknown").ToList();
            Assert.That(warnings.Select(w => w.Args[0]), Is.EqualTo(new[] { "Mystery" }), "the avatar's own Existing is known");
            Assert.That(warnings[0].Severity, Is.EqualTo(MessageSeverity.Warning), "the same severity the build gives it");
            Assert.That(warnings[0].EntryIds, Does.Contain(preset.id));

            BuildWithPresets(out var errors, out _);
            Assert.That(errors.Count(e => e.TheError is SimpleError s && s.TitleKey == "ad.warn.preset_parameter_unknown"), Is.EqualTo(1), "the build says the same, once");
        }

        [Test]
        public void AVrcFuryGlobalParameterIsKnownToAPresetInTheInspectorAndTheBuild()
        {
            Assume.That(VrcFuryTestHelper.VrcFuryType, Is.Not.Null);
            var furyGo = new GameObject("Fury");
            furyGo.transform.SetParent(avatar.Root.transform);
            var toggle = VrcFuryTestHelper.AddToggle(furyGo, "Fury/Toggle");
            VrcFuryTestHelper.ConfigureToggle(toggle, t =>
            {
                VrcFuryTestHelper.SetField(t, "useGlobalParam", true);
                VrcFuryTestHelper.SetField(t, "globalParam", "MyGlobalParam");
            });
            var preset = Preset("Fury on");
            preset.presetParameters.Add(new PresetParameter { name = "MyGlobalParam", value = 1f });
            AvatarPlanCache.Invalidate();

            Assert.That(AvatarPlanCache.GetFor(menu).Messages.Any(m => m.Key == "ad.warn.preset_parameter_unknown"), Is.False,
                "VRCFury adds it later in the build, so the preset may set it");
            BuildWithPresets(out var errors, out _);
            Assert.That(errors.Any(e => e.TheError is SimpleError s && s.TitleKey == "ad.warn.preset_parameter_unknown"), Is.False, "and the build agrees");
        }

        [Test]
        public void UnknownNamedParameterWarns()
        {
            var preset = Preset("SetMood");
            preset.presetParameters.Add(new PresetParameter { name = "Mystery", value = 1f });

            BuildWithPresets(out var errors, out _);

            Assert.That(errors.Any(e => e.TheError is SimpleError s && s.TitleKey == "ad.warn.preset_parameter_unknown"), Is.True);
        }

        [Test]
        public void CustomParameterTogglesUseTheirBindingValue()
        {
            var mode = Toggle("Mode");
            mode.parameterName = "CustomMode";
            mode.parameterValue = 5f;
            var preset = Preset("SetMode");
            preset.presetStates.Add(new PresetState { entryId = mode.id, on = true });
            var (tree, plan) = Model();
            var messages = new List<ModelMessage>();

            var ops = PresetBuilder.Operations(preset, tree, plan, messages);

            Assert.That(ops.Single(), Is.EqualTo(("CustomMode", 5f)));
        }

        [Test]
        public void NamedParameterKnownOnlyToTheExpressionParametersDoesNotWarn()
        {
            avatar.ExpressionParameters.parameters = avatar.ExpressionParameters.parameters
                .Concat(new[] { new VRCExpressionParameters.Parameter { name = "Outfit", valueType = VRCExpressionParameters.ValueType.Int, saved = true, networkSynced = true } })
                .ToArray();
            var preset = Preset("SetOutfit");
            preset.presetParameters.Add(new PresetParameter { name = "Outfit", value = 2f });

            BuildWithPresets(out var errors, out _);

            Assert.That(errors.Any(e => e.TheError is SimpleError s && s.TitleKey == "ad.warn.preset_parameter_unknown"), Is.False,
                "the parameter is declared in the expression parameters asset, even though no FX layer references it");
        }

        [Test]
        public void SharedCustomParameterOnWinsOverOff()
        {
            var a = Toggle("A"); a.parameterName = "Mode"; a.parameterValue = 2f;
            var b = Toggle("B"); b.parameterName = "Mode"; b.parameterValue = 3f;
            var preset = Preset("SetA");
            preset.presetStates.Add(new PresetState { entryId = a.id, on = true });
            preset.presetStates.Add(new PresetState { entryId = b.id, on = false });
            var (tree, plan) = Model();
            var messages = new List<ModelMessage>();

            var ops = PresetBuilder.Operations(preset, tree, plan, messages);

            Assert.That(ops.Single(), Is.EqualTo(("Mode", 2f)));
            Assert.That(messages, Is.Empty);
        }

        [Test]
        public void SharedCustomParameterTwoOnIsAnError()
        {
            var a = Toggle("A"); a.parameterName = "Mode"; a.parameterValue = 2f;
            var b = Toggle("B"); b.parameterName = "Mode"; b.parameterValue = 3f;
            var preset = Preset("Conflict");
            preset.presetStates.Add(new PresetState { entryId = a.id, on = true });
            preset.presetStates.Add(new PresetState { entryId = b.id, on = true });
            var (tree, plan) = Model();
            var messages = new List<ModelMessage>();

            var ops = PresetBuilder.Operations(preset, tree, plan, messages);

            Assert.That(messages.Any(m => m.Key == "ad.error.preset_group_conflict" && m.Severity == MessageSeverity.Error), Is.True);
            Assert.That(ops.Single(), Is.EqualTo(("Mode", 2f)), "the first member on still wins the write");
        }

        [Test]
        public void ApplyPresetFillsThePreview()
        {
            var a = Toggle("Casual", avatar.Hat); a.exclusiveGroup = "Outfits";
            var b = Toggle("Suit", avatar.Glasses); b.exclusiveGroup = "Outfits";
            var eyes = Radial("Eyes");
            var plain = Toggle("Blush");
            var preset = Preset("GoCasual");
            preset.presetStates.Add(new PresetState { entryId = a.id, on = true });
            preset.presetStates.Add(new PresetState { entryId = eyes.id, value = 0.6f });
            preset.presetStates.Add(new PresetState { entryId = plain.id, on = true });
            var (tree, _) = Model();
            PreviewState.Set(b.id, 1f);

            PreviewState.ApplyPreset(preset, tree);

            Assert.That(PreviewState.TryGet(a.id, out var av) && av == 1f, Is.True);
            Assert.That(PreviewState.TryGet(b.id, out var bv) && bv == 0f, Is.True, "the preset clears the other group member");
            Assert.That(PreviewState.TryGet(eyes.id, out var ev) && Mathf.Approximately(ev, 0.6f), Is.True);
            Assert.That(PreviewState.TryGet(plain.id, out var pv) && pv == 1f, Is.True);
        }

        [Test]
        public void CaptureTargetsAreThePiecesSwitchedOn()
        {
            var hat = Toggle("Hat", avatar.Hat);
            var offToggle = Toggle("Glasses", avatar.Glasses);
            var eyes = Radial("Eyes");
            var preset = Preset("Casual");
            preset.presetStates.Add(new PresetState { entryId = hat.id, on = true });
            preset.presetStates.Add(new PresetState { entryId = offToggle.id, on = false });
            preset.presetStates.Add(new PresetState { entryId = eyes.id, value = 0.5f });
            var (tree, _) = Model();

            var targets = IconCapture.TargetsOf(preset, tree, avatar.Root.transform);

            Assert.That(targets, Is.EquivalentTo(new Renderer[] { avatar.Hat.GetComponent<MeshRenderer>(), avatar.Body }));
        }

        [Test]
        public void EmptyPresetWarns()
        {
            Preset("Nothing");
            var (tree, _) = Model();

            Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.preset_empty"), Is.True);
        }
    }
}
