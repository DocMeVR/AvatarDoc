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
using VRC.SDK3.Dynamics.Contact.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>A trigger whose "when" is a list of groups: met while any group is met, a group met while all of its conditions are (phase 9 task 8).</summary>
    public class ConditionGroupTests
    {
        TestAvatar avatar;
        AvatarDoc menu;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            menu = avatar.AddMenu();
            Platform.OverrideMobile = false;
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            Platform.OverrideMobile = null;
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        static AnimatorController FxOf(TestAvatar a) => (AnimatorController)a.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
        static AnimatorControllerLayer Layer(AnimatorController fx, string name) => fx.layers.Single(l => l.name == name);
        static AnimatorState State(AnimatorControllerLayer layer, string name) => layer.stateMachine.states.Single(s => s.state.name == name).state;

        static AnimatorCondition Cond(string parameter, AnimatorConditionMode mode, float threshold)
        {
            return new AnimatorCondition { parameter = parameter, mode = mode, threshold = threshold };
        }

        static List<AnimatorCondition> Group(params AnimatorCondition[] conditions) => conditions.ToList();

        MenuEntry Trigger(string name)
        {
            var entry = menu.AddEntry(EntryKind.Trigger, name);
            entry.objects.Add(new ObjectTarget { target = avatar.Glasses });
            return entry;
        }

        MenuEntry Toggle(string name, GameObject target)
        {
            var entry = menu.AddEntry(EntryKind.Toggle, name);
            entry.objects.Add(new ObjectTarget { target = target });
            return entry;
        }

        static WhenCondition OnToggle(MenuEntry toggle, bool on = true)
        {
            return new WhenCondition { kind = WhenConditionKind.Toggle, sourceToggleId = toggle.id, sourceToggleOn = on };
        }

        static WhenCondition OnParameter(string parameter, ParameterCondition comparison, float value = 0f)
        {
            return new WhenCondition { kind = WhenConditionKind.Parameter, sourceParameter = parameter, condition = comparison, conditionValue = value };
        }

        static WhenCondition OnContact(Transform anchor)
        {
            return new WhenCondition { kind = WhenConditionKind.Contact, contactAnchor = anchor };
        }

        static void Groups(MenuEntry trigger, params WhenCondition[][] groups)
        {
            trigger.when = groups.Select(g => new ConditionGroup { all = g.ToList() }).ToList();
        }

        /// <summary>The whole chain a real build runs: model, parameters, touch, trigger plans, layers, receivers and flip layers.</summary>
        AnimatorController BuildFull(out List<ErrorContext> errors, out MenuTree tree)
        {
            var builtTree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var virtualFx);
            var existing = ParametersWriter.ExistingParameters(avatar.Descriptor.expressionParameters, virtualFx);
            ParametersWriter.AddProvided(existing, ParameterInfo.ForContext(context).GetParametersForObject(avatar.Root));
            var plan = ParameterPlanner.Plan(builtTree, existing);
            var touch = TouchBuilder.Describe(builtTree, existing, plan);
            var (plans, planMessages) = TriggerPlanner.Plan(builtTree, plan, existing, touch);
            var baseState = BaseState.Capture(builtTree);
            DefaultsApplier.Apply(builtTree, plan, baseState, plans);
            var captured = ErrorReport.CaptureErrors(() =>
            {
                L.Report(planMessages);
                L.Report(AnimatorBuilder.Build(asc, builtTree, plan, baseState));
                L.Report(TriggerBuilder.Build(asc, builtTree, plans, baseState));
                L.Report(TouchBuilder.Create(context, builtTree, touch));
                L.Report(TouchBuilder.BuildFlipLayers(asc, builtTree, plan, touch, existing));
            });
            context.DeactivateAllExtensionContexts();
            errors = captured;
            tree = builtTree;
            return FxOf(avatar);
        }

        /// <summary>Model, parameters and touch names, then the trigger plans: what the inspector's own cached plan runs on every change.</summary>
        (List<TriggerPlan> plans, List<ModelMessage> messages) PlanTriggers()
        {
            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
            var existing = ParametersWriter.ExistingParameters(avatar.ExpressionParameters, avatar.Fx);
            var plan = ParameterPlanner.Plan(tree, existing);
            var touch = TouchBuilder.Describe(tree, existing, plan);
            return TriggerPlanner.Plan(tree, plan, existing, touch);
        }

        /// <summary>The conditions of each transition as text, with the thresholds written the same way whatever the editor's own number format is.</summary>
        static string[] ConditionsOf(AnimatorStateTransition[] transitions)
        {
            return transitions
                .Select(t => string.Join(" & ", t.conditions.Select(c => c.parameter + " " + c.mode + " " + c.threshold.ToString(System.Globalization.CultureInfo.InvariantCulture))))
                .ToArray();
        }

        // --- The boolean core ---

        [Test]
        public void NegateCoversEveryMode()
        {
            Assert.That(ConditionLogic.Negate(Cond("p", AnimatorConditionMode.If, 0f), false).mode, Is.EqualTo(AnimatorConditionMode.IfNot));
            Assert.That(ConditionLogic.Negate(Cond("p", AnimatorConditionMode.IfNot, 0f), false).mode, Is.EqualTo(AnimatorConditionMode.If));

            var notEqual = ConditionLogic.Negate(Cond("p", AnimatorConditionMode.Equals, 3f), true);
            Assert.That(notEqual.mode, Is.EqualTo(AnimatorConditionMode.NotEqual));
            Assert.That(notEqual.threshold, Is.EqualTo(3f));
            Assert.That(ConditionLogic.Negate(Cond("p", AnimatorConditionMode.NotEqual, 3f), true).mode, Is.EqualTo(AnimatorConditionMode.Equals));

            var intGreater = ConditionLogic.Negate(Cond("p", AnimatorConditionMode.Greater, 2f), true);
            Assert.That(intGreater.mode, Is.EqualTo(AnimatorConditionMode.Less));
            Assert.That(intGreater.threshold, Is.EqualTo(3f), "nothing sits between 2 and 3, so the two halves meet");
            var intLess = ConditionLogic.Negate(Cond("p", AnimatorConditionMode.Less, 3f), true);
            Assert.That(intLess.mode, Is.EqualTo(AnimatorConditionMode.Greater));
            Assert.That(intLess.threshold, Is.EqualTo(2f));

            var floatGreater = ConditionLogic.Negate(Cond("p", AnimatorConditionMode.Greater, 0.5f), false);
            Assert.That(floatGreater.mode, Is.EqualTo(AnimatorConditionMode.Less));
            Assert.That(floatGreater.threshold, Is.EqualTo(0.5f), "a float keeps the same threshold: a step would make entering and leaving both true at once");
            Assert.That(ConditionLogic.Negate(floatGreater, false).mode, Is.EqualTo(AnimatorConditionMode.Greater), "negating twice gives the condition back");
        }

        [Test]
        public void LeaveIsTheProductOfNegations()
        {
            var groups = new List<List<AnimatorCondition>>
            {
                Group(Cond("a", AnimatorConditionMode.If, 0f), Cond("b", AnimatorConditionMode.If, 0f)),
                Group(Cond("c", AnimatorConditionMode.If, 0f), Cond("d", AnimatorConditionMode.If, 0f)),
            };

            var leave = ConditionLogic.Leave(groups, null);

            Assert.That(leave.Count, Is.EqualTo(4), "one per combination of a failed condition from each group");
            foreach (var transition in leave)
            {
                Assert.That(transition.Count, Is.EqualTo(2));
                Assert.That(transition.All(c => c.mode == AnimatorConditionMode.IfNot), Is.True);
            }
            var keys = leave.Select(t => string.Join("+", t.Select(c => c.parameter).OrderBy(n => n))).ToList();
            Assert.That(keys, Is.EquivalentTo(new[] { "a+c", "a+d", "b+c", "b+d" }));
        }

        [Test]
        public void ContradictoryLeaveTransitionsAreDropped()
        {
            var opposite = new List<List<AnimatorCondition>>
            {
                Group(Cond("p", AnimatorConditionMode.Greater, 2f)),
                Group(Cond("p", AnimatorConditionMode.Less, 5f)),
            };

            Assert.That(ConditionLogic.Leave(opposite, null, _ => true), Is.Empty, "below 3 and above 4 at once can never happen");

            var same = new List<List<AnimatorCondition>>
            {
                Group(Cond("p", AnimatorConditionMode.If, 0f)),
                Group(Cond("p", AnimatorConditionMode.If, 0f)),
            };
            var leave = ConditionLogic.Leave(same, null);
            Assert.That(leave.Count, Is.EqualTo(1), "the same negation twice is one transition");
            Assert.That(leave[0].Count, Is.EqualTo(1));
        }

        [Test]
        [Timeout(30000)]
        public void SubsumedLeaveTransitionsAreDropped()
        {
            var groups = new List<List<AnimatorCondition>>
            {
                Group(Cond("X", AnimatorConditionMode.If, 0f), Cond("Y", AnimatorConditionMode.If, 0f)),
                Group(Cond("A", AnimatorConditionMode.If, 0f), Cond("X", AnimatorConditionMode.If, 0f)),
            };

            var leave = ConditionLogic.Leave(groups, null);

            // "(X and Y) or (A and X)" is no longer met when X is off, or when neither Y nor A is on. The two
            // combinations that also name X beside one of those are covered by "X is off" on its own.
            Assert.That(leave.Count, Is.EqualTo(2));
            Assert.That(leave.Any(t => t.Count == 1 && t[0].parameter == "X"), Is.True);
            Assert.That(leave.Any(t => t.Count == 2 && t.Select(c => c.parameter).OrderBy(n => n).SequenceEqual(new[] { "A", "Y" })), Is.True);
        }

        [Test]
        [Timeout(30000)]
        public void OppositeAbandonsAnOversizedProduct()
        {
            var alternatives = Enumerable.Range(0, 8)
                .Select(i => Group(
                    Cond("a" + i, AnimatorConditionMode.If, 0f), Cond("b" + i, AnimatorConditionMode.If, 0f),
                    Cond("c" + i, AnimatorConditionMode.If, 0f), Cond("d" + i, AnimatorConditionMode.If, 0f)))
                .ToList();

            var result = ConditionLogic.Opposite(alternatives, null, out bool tooComplex);

            Assert.That(tooComplex, Is.True, "4 to the power of 8 is far past the cap");
            Assert.That(result, Is.Empty, "the work is abandoned rather than walked to the end");
        }

        // --- Building ---

        [Test]
        public void TwoGroupsGiveTwoEnterTransitions()
        {
            var hat = Toggle("Hat", avatar.Hat);
            var trigger = Trigger("Ears");
            Groups(trigger, new[] { OnToggle(hat) }, new[] { OnParameter("AFK", ParameterCondition.IsTrue) });

            var fx = BuildFull(out var errors, out _);

            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: Ears");
            var enter = State(layer, "Off").transitions;
            Assert.That(enter.Length, Is.EqualTo(2), "one transition per group");
            Assert.That(enter.Any(t => t.conditions.Single().parameter == "AD/Hat"), Is.True);
            Assert.That(enter.Any(t => t.conditions.Single().parameter == "AFK"), Is.True);
            var leave = State(layer, "On").transitions;
            Assert.That(leave.Length, Is.EqualTo(1), "leaving needs both groups to fail at once");
            Assert.That(leave[0].conditions.Length, Is.EqualTo(2));
            Assert.That(leave[0].conditions.All(c => c.mode == AnimatorConditionMode.IfNot), Is.True);
        }

        [Test]
        public void GateJoinsEveryEnterAndAddsItsOwnLeave()
        {
            var hat = Toggle("Hat", avatar.Hat);
            var gate = menu.AddEntry(EntryKind.Toggle, "Enabled");
            var trigger = Trigger("Ears");
            Groups(trigger, new[] { OnToggle(hat) }, new[] { OnParameter("AFK", ParameterCondition.IsTrue) });
            trigger.gateToggleId = gate.id;

            var fx = BuildFull(out var errors, out _);

            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: Ears");
            var enter = State(layer, "Off").transitions;
            Assert.That(enter.Length, Is.EqualTo(2));
            Assert.That(enter.All(t => t.conditions.Any(c => c.parameter == "AD/Enabled")), Is.True, "the gate is on every way in");
            var leave = State(layer, "On").transitions;
            Assert.That(leave.Length, Is.EqualTo(2), "both groups failing, or the gate closing");
            Assert.That(leave.Any(t => t.conditions.Length == 1 && t.conditions[0].parameter == "AD/Enabled"), Is.True);
        }

        [Test]
        public void FlatFieldsStillBuildAsOneGroup()
        {
            var trigger = Trigger("Voice");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "Voice";
            trigger.condition = ParameterCondition.Equals;
            trigger.conditionValue = 0.5f;

            var fx = BuildFull(out var errors, out _);

            Assert.That(errors, Is.Empty);
            Assert.That(trigger.when, Is.Empty, "the flat fields are left where they are");
            var layer = Layer(fx, "AD: Voice");
            // The baseline of the single source build: one way in, with the window around the value, and two ways out.
            Assert.That(ConditionsOf(State(layer, "Off").transitions), Is.EqualTo(new[] { "Voice Greater 0.495 & Voice Less 0.505" }));
            Assert.That(ConditionsOf(State(layer, "On").transitions), Is.EqualTo(new[] { "Voice Less 0.495", "Voice Greater 0.505" }));
        }

        [Test]
        public void TwoContactConditionsGetTwoReceivers()
        {
            var trigger = Trigger("Boop");
            trigger.source = TriggerSource.Contact;
            Groups(trigger, new[] { OnContact(avatar.Hat.transform), OnContact(avatar.Glasses.transform) });

            var fx = BuildFull(out var errors, out _);

            Assert.That(errors, Is.Empty);
            var receivers = avatar.Root.GetComponentsInChildren<VRCContactReceiver>(true);
            Assert.That(receivers.Length, Is.EqualTo(2), "one receiver per contact condition");
            Assert.That(receivers.Select(r => r.parameter).Distinct().Count(), Is.EqualTo(2), "each carries a parameter of its own");
            var enter = State(Layer(fx, "AD: Boop"), "Off").transitions.Single();
            Assert.That(enter.conditions.Length, Is.EqualTo(2), "both contacts have to be touched at once");
        }

        [Test]
        public void SameContactSettingsShareOneReceiver()
        {
            var trigger = Trigger("Boop");
            trigger.source = TriggerSource.Contact;
            Groups(trigger, new[] { OnContact(avatar.Hat.transform) }, new[] { OnContact(avatar.Hat.transform) });

            var fx = BuildFull(out var errors, out _);

            Assert.That(errors, Is.Empty);
            var receivers = avatar.Root.GetComponentsInChildren<VRCContactReceiver>(true);
            Assert.That(receivers.Length, Is.EqualTo(1), "the same settings twice make one receiver");
            var enter = State(Layer(fx, "AD: Boop"), "Off").transitions;
            Assert.That(enter.Length, Is.EqualTo(1), "both groups read the same parameter, so the two ways in are one");
            Assert.That(enter[0].conditions.Single().parameter, Is.EqualTo(receivers[0].parameter));
        }

        [Test]
        public void FlipTriggerWithTwoGroupsFlipsOncePerMet()
        {
            var hat = Toggle("Hat", avatar.Hat);
            var trigger = menu.AddEntry(EntryKind.Trigger, "Flip");
            trigger.source = TriggerSource.Contact;
            trigger.contactAnchor = avatar.Glasses.transform;
            trigger.touchMode = TouchMode.FlipToggle;
            trigger.flipToggleId = hat.id;
            Groups(trigger, new[] { OnContact(avatar.Glasses.transform) }, new[] { OnParameter("AFK", ParameterCondition.IsTrue) });

            var fx = BuildFull(out var errors, out _);

            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: Flip (flip)");
            var idle = State(layer, "Idle");
            var setOn = State(layer, "Set on");
            var setOff = State(layer, "Set off");
            Assert.That(idle.transitions.Count(t => t.destinationState == setOn), Is.EqualTo(2), "one way in per group");
            Assert.That(idle.transitions.Count(t => t.destinationState == setOff), Is.EqualTo(2));
            Assert.That(idle.transitions.Where(t => t.destinationState == setOn).All(t => t.conditions.Any(c => c.parameter == "AD/Hat")), Is.True,
                "each one still checks the toggle it flips");
            Assert.That(setOn.transitions.Single().conditions.Length, Is.EqualTo(2), "it goes back to idle once both groups are done");
        }

        [Test]
        public void ConditionOnAnExcludedToggleStillBuilds()
        {
            var hat = Toggle("Hat", avatar.Hat);
            hat.platform = EntryPlatform.MobileOnly;
            var trigger = Trigger("Ears");
            Groups(trigger, new[] { OnToggle(hat) });

            var fx = BuildFull(out var errors, out var tree);

            Assert.That(errors, Is.Empty);
            Assert.That(tree.Controls.Single(c => c.Entry == hat).Excluded, Is.True, "the toggle itself is not built here");
            var enter = State(Layer(fx, "AD: Ears"), "Off").transitions.Single();
            Assert.That(enter.conditions.Single().parameter, Is.EqualTo("AD/Hat"), "its parameter exists on both platforms, so the condition still works");
        }

        // --- Validation ---

        [Test]
        public void EmptyGroupWarnsAndIsIgnored()
        {
            var hat = Toggle("Hat", avatar.Hat);
            var trigger = Trigger("Ears");
            Groups(trigger, new[] { OnToggle(hat) }, new WhenCondition[0]);

            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.condition_group_empty" && m.EntryIds.Contains(trigger.id)), Is.True);
            Assert.That(trigger.EffectiveWhen.Count, Is.EqualTo(1), "the empty group is left out of the rule");

            Groups(trigger, new WhenCondition[0]);
            var emptyTree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            Assert.That(emptyTree.Messages.Any(m => m.Key == "ad.warn.trigger_no_condition" && m.EntryIds.Contains(trigger.id)), Is.True);
        }

        [Test]
        public void TooManyCombinationsIsAnError()
        {
            var trigger = Trigger("Ears");
            var groups = new List<WhenCondition[]>();
            for (int g = 0; g < 4; g++)
                groups.Add(Enumerable.Range(0, 4).Select(i => OnParameter("AFK", ParameterCondition.IsTrue)).ToArray());
            Groups(trigger, groups.ToArray());

            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            Assert.That(tree.Messages.Any(m => m.Says("ad.error.trigger_too_complex") && m.EntryIds.Contains(trigger.id)), Is.True,
                "4 groups of 4 conditions are 256 ways to stop, past the 64 the layer may hold");
            Assert.That(ConditionLogic.TooComplex(trigger.EffectiveWhen, out int groupCount, out int leaveCount), Is.True);
            Assert.That(groupCount, Is.EqualTo(4));
            Assert.That(leaveCount, Is.EqualTo(256));
        }

        [Test]
        [Timeout(30000)]
        public void ExpandedAlternativesCountTowardTheCap()
        {
            var trigger = Trigger("Ears");
            // "is not" on a float has two ways to be true, so four of them in one group are sixteen ways in and
            // four to the power of sixteen ways out. The counting has to see that before it builds anything.
            Groups(trigger, Enumerable.Range(0, 4).Select(i => OnParameter("Voice", ParameterCondition.NotEquals, 0.5f)).ToArray());

            var (plans, messages) = PlanTriggers();

            var message = messages.SingleOrDefault(m => m.Says("ad.error.trigger_too_complex") && m.EntryIds.Contains(trigger.id));
            Assert.That(message, Is.Not.Null, "the expanded alternatives are what counts");
            Assert.That(int.Parse(message.Args[2].ToString()), Is.GreaterThan(ConditionLogic.MaxLeaveTransitions), "the message names the real count");
            Assert.That(plans.Any(p => p.Entry == trigger), Is.False, "a trigger that is too complex builds nothing");
        }

        [Test]
        public void SentenceSpellsOutTheRule()
        {
            var hat = Toggle("Hat", avatar.Hat);
            var trigger = Trigger("Ears");
            Groups(trigger,
                new[] { OnToggle(hat), OnContact(avatar.Glasses.transform) },
                new[] { OnParameter("GestureLeft", ParameterCondition.Equals, 3f) });
            var plan = AvatarPlanCache.GetFor(menu);

            var sentence = ConditionLogic.Sentence(trigger, plan.Tree);

            Assert.That(sentence, Is.EqualTo("When (\"Hat\" is on and \"Glasses\" is touched) or GestureLeft equals 3"));
            var summary = SummaryBuilder.Build(plan, menu, SummaryFormat.Plain);
            Assert.That(summary, Does.Contain(sentence), "the summary prints the same sentence");
        }

        [Test]
        public void ParameterNamedInAConditionGroupIsKeptByRemoval()
        {
            var existingMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            existingMenu.name = "Root";
            existingMenu.controls = new List<VRCExpressionsMenu.Control>
            {
                new VRCExpressionsMenu.Control
                {
                    name = "Old hat",
                    type = VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = "HatParam" },
                    value = 1,
                },
            };
            avatar.Descriptor.expressionsMenu = existingMenu;
            avatar.ExpressionParameters.parameters = avatar.ExpressionParameters.parameters
                .Concat(new[] { new VRCExpressionParameters.Parameter { name = "HatParam", valueType = VRCExpressionParameters.ValueType.Bool } })
                .ToArray();
            menu.menuEdits.Add(new MenuEdit { id = "hat-edit", source = new List<string> { "Old hat" }, remove = true });

            try
            {
                var freedPlan = Removal();
                Assert.That(freedPlan.Parameters.Single(p => p.Name == "HatParam").Freed, Is.True, "nothing reads it yet");

                var trigger = Trigger("Ears");
                Groups(trigger, new[] { OnParameter("HatParam", ParameterCondition.IsTrue) });

                var keptPlan = Removal();

                var parameter = keptPlan.Parameters.Single(p => p.Name == "HatParam");
                Assert.That(parameter.Freed, Is.False);
                Assert.That(parameter.KeptBecause, Is.EqualTo("ad.removal.kept.entry"));
            }
            finally
            {
                avatar.Descriptor.expressionsMenu = avatar.ExpressionsMenu;
                Object.DestroyImmediate(existingMenu);
            }
        }

        RemovalPlan Removal()
        {
            var tree = MenuModel.Build(avatar.Root.transform, avatar.Descriptor.expressionsMenu, new[] { menu });
            return RemovalPlanner.Plan(tree, avatar.Descriptor);
        }

        // --- Editing ---

        [Test]
        public void AddingASecondConditionMovesTheFlatOneIntoTheFirstGroup()
        {
            var hat = Toggle("Hat", avatar.Hat);
            var trigger = Trigger("Ears");
            trigger.sourceToggleId = hat.id;
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            view.Tree.SelectEntry(trigger.id);

            Assert.That(view.Detail.Q<Button>("ad-when-add-and"), Is.Not.Null, "the buttons are offered before there are any groups");
            Assert.That(view.Detail.Q<VisualElement>("ad-when-card"), Is.Null);

            view.Detail.AddCondition(0);

            Assert.That(trigger.when.Count, Is.EqualTo(1));
            Assert.That(trigger.when[0].all.Count, Is.EqualTo(2), "the single source moved into the group and the new condition joined it");
            Assert.That(trigger.when[0].all[0].kind, Is.EqualTo(WhenConditionKind.Toggle));
            Assert.That(trigger.when[0].all[0].sourceToggleId, Is.EqualTo(hat.id));
            Assert.That(view.Detail.Q<VisualElement>("ad-when-card"), Is.Not.Null, "the panel shows the card now");

            view.Detail.AddConditionGroup();

            Assert.That(trigger.when.Count, Is.EqualTo(2));
            Assert.That(view.Detail.Query<VisualElement>("ad-when-card").ToList().Count, Is.EqualTo(2));
        }

        [Test]
        public void SelectingAnotherTriggerResetsTheSelectedCondition()
        {
            var hat = Toggle("Hat", avatar.Hat);
            var first = Trigger("First");
            Groups(first, new[] { OnToggle(hat) }, new[] { OnContact(avatar.Glasses.transform) });
            var second = Trigger("Second");
            Groups(second, new[] { OnToggle(hat) });
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            view.Tree.SelectEntry(first.id);

            view.Detail.SelectCondition(1, 0);
            Assert.That(view.Detail.SelectedConditionKey, Is.EqualTo("1.0"));

            view.Tree.SelectEntry(second.id);

            Assert.That(view.Detail.SelectedConditionKey, Is.EqualTo("0.0"), "the second trigger starts at its first condition");
            Assert.That(view.Detail.TouchConditionOf(view.Detail.Row), Is.SameAs(second.when[0].all[0]));
        }

        [Test]
        public void RemovingTheLastConditionLeavesMatchingFlatFields()
        {
            var hat = Toggle("Hat", avatar.Hat);
            var trigger = Trigger("Ears");
            trigger.sourceToggleId = hat.id;
            Groups(trigger, new[] { OnParameter("AFK", ParameterCondition.IsTrue) });

            EntryEditing.RemoveCondition(menu, trigger, 0, 0);

            Assert.That(trigger.when, Is.Empty);
            Assert.That(trigger.source, Is.EqualTo(TriggerSource.Parameter), "the flat fields match the condition that was last shown");
            Assert.That(trigger.sourceParameter, Is.EqualTo("AFK"));
            Assert.That(trigger.condition, Is.EqualTo(ParameterCondition.IsTrue));
        }

        [Test]
        public void DuplicatingRemapsToggleIdsInsideConditions()
        {
            var sub = EntryEditing.Add(menu, EntryKind.Submenu, "Pack", "");
            var hat = EntryEditing.Add(menu, EntryKind.Toggle, "Hat", sub.id);
            var trigger = EntryEditing.Add(menu, EntryKind.Trigger, "Ears", sub.id);
            Groups(trigger, new[] { OnToggle(hat) });

            var copy = EntryEditing.Duplicate(menu, sub);

            var inside = EntryEditing.Descendants(menu, copy);
            var copiedToggle = inside.Single(e => e.kind == EntryKind.Toggle);
            var copiedTrigger = inside.Single(e => e.kind == EntryKind.Trigger);
            Assert.That(copiedTrigger.when[0].all[0].sourceToggleId, Is.EqualTo(copiedToggle.id), "the copy reads its own toggle");
            Assert.That(trigger.when[0].all[0].sourceToggleId, Is.EqualTo(hat.id), "the original is left alone");
        }

        [Test]
        public void LockedComponentRefusesConditionWrites()
        {
            var hat = Toggle("Hat", avatar.Hat);
            var trigger = Trigger("Ears");
            Groups(trigger, new[] { OnToggle(hat) });
            menu.locked = true;

            Assert.That(EntryEditing.AddCondition(menu, trigger, 0), Is.Null);
            Assert.That(EntryEditing.AddConditionGroup(menu, trigger), Is.Null);
            Assert.That(EntryEditing.RemoveCondition(menu, trigger, 0, 0), Is.False);
            Assert.That(EntryEditing.RemoveConditionGroup(menu, trigger, 0), Is.False);
            Assert.That(EntryEditing.SetConditionKind(menu, trigger, 0, 0, WhenConditionKind.Parameter), Is.False);
            Assert.That(trigger.when.Count, Is.EqualTo(1));
            Assert.That(trigger.when[0].all.Count, Is.EqualTo(1));
            Assert.That(trigger.when[0].all[0].kind, Is.EqualTo(WhenConditionKind.Toggle));

            var serialized = new SerializedObject(menu);
            var fields = new DirectFields(serialized, () => serialized.ApplyModifiedProperties());
            int index = menu.entries.IndexOf(trigger);
            var path = "entries.Array.data[" + index + "].when.Array.data[0].all.Array.data[0].sourceToggleId";
            fields.Write(path, p => p.stringValue = "somewhere else", null);
            Assert.That(trigger.when[0].all[0].sourceToggleId, Is.EqualTo(hat.id), "a nested write into when is refused too");

            ContactSceneHandle.Apply(trigger.when[0].all[0], menu, new Vector3(1f, 2f, 3f), 0.5f);
            Assert.That(trigger.when[0].all[0].contactOffset, Is.EqualTo(Vector3.zero));

            menu.locked = false;
            fields.Write(path, p => p.stringValue = "somewhere else", null);
            Assert.That(trigger.when[0].all[0].sourceToggleId, Is.EqualTo("somewhere else"), "and goes through once the lock is off");
        }

        [Test]
        [Timeout(30000)]
        public void FlipTriggerTooComplexIsReportedByThePlan()
        {
            var hat = Toggle("Hat", avatar.Hat);
            var trigger = Trigger("Flip");
            trigger.source = TriggerSource.Contact;
            trigger.contactAnchor = avatar.Glasses.transform;
            trigger.touchMode = TouchMode.FlipToggle;
            trigger.flipToggleId = hat.id;
            Groups(trigger, Enumerable.Range(0, 4).Select(i => OnParameter("Voice", ParameterCondition.NotEquals, 0.5f)).ToArray());

            var (plans, messages) = PlanTriggers();

            var message = messages.SingleOrDefault(m => m.Says("ad.error.trigger_too_complex") && m.EntryIds.Contains(trigger.id));
            Assert.That(message, Is.Not.Null, "the flip layer is counted before the build ever reaches it");
            Assert.That(int.Parse(message.Args[2].ToString()), Is.GreaterThan(ConditionLogic.MaxLeaveTransitions));
            Assert.That(plans.Any(p => p.Entry == trigger), Is.False, "a flip trigger builds its layer in TouchBuilder, not here");
        }

        [Test]
        public void ARuleOfTwentyConditionsIsNotMultipliedOutOnEveryPlan()
        {
            var trigger = Trigger("Ears");
            Groups(trigger, new[] { OnParameter("Voice", ParameterCondition.NotEquals, 0.5f) });
            PlanTriggers();
            // "is not" on a float has two ways to be true: twenty of them in one group are about a million ways in.
            Groups(trigger, Enumerable.Range(0, 20).Select(i => OnParameter("Voice", ParameterCondition.NotEquals, 0.5f)).ToArray());

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var (plans, _) = PlanTriggers();
            watch.Stop();

            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(200), "a rule past the caps is refused before anything is multiplied out, or every refresh stalls");
            Assert.That(plans.Any(p => p.Entry == trigger), Is.False, "and it builds nothing");
            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });
            Assert.That(tree.Messages.Any(m => m.Says("ad.error.trigger_too_complex") && m.EntryIds.Contains(trigger.id)), Is.True, "the model says why");
            var shown = AvatarPlanCache.GetFor(menu);
            Assert.That(shown.Messages.Any(m => m.Says("ad.error.trigger_too_complex") && m.EntryIds.Contains(trigger.id)), Is.True, "and so does the inspector's own plan");
        }

        [Test]
        public void ARuleWithinTheCapsIsStillPlanned()
        {
            var trigger = Trigger("Ears");
            Groups(trigger, Enumerable.Range(0, ConditionLogic.MaxPerGroup).Select(i => OnParameter("AFK", ParameterCondition.IsTrue)).ToArray());

            var (plans, messages) = PlanTriggers();

            Assert.That(plans.Any(p => p.Entry == trigger), Is.True, "the cap only refuses what the model already calls too complex");
            Assert.That(messages.Any(m => m.Says("ad.error.trigger_too_complex")), Is.False);
        }

        [Test]
        public void PlusAndIsDisabledOnAGroupThatIsFull()
        {
            var trigger = Trigger("Ears");
            Groups(trigger, Enumerable.Range(0, ConditionLogic.MaxPerGroup).Select(i => OnParameter("AFK", ParameterCondition.IsTrue)).ToArray());
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            view.Tree.SelectEntry(trigger.id);

            var inCard = view.Detail.Q<Button>("ad-when-add-and-0");
            var below = view.Detail.Q<Button>("ad-when-add-and");

            Assert.That(inCard.enabledSelf, Is.False, "a group holds at most " + ConditionLogic.MaxPerGroup + " conditions");
            Assert.That(below.enabledSelf, Is.False, "the button under the cards adds to the last group, which is full");
            var why = string.Format(L.Text("ad.ui.when.add_and.limit.tip"), ConditionLogic.MaxPerGroup);
            Assert.That(inCard.tooltip, Is.EqualTo(why));
            Assert.That(below.tooltip, Is.EqualTo(why));
            Assert.That(why, Does.Contain(ConditionLogic.MaxPerGroup.ToString()), "the tooltip names the number");
        }

        [Test]
        public void PlusAndStaysOnForAGroupWithRoom()
        {
            var trigger = Trigger("Ears");
            Groups(trigger, Enumerable.Range(0, ConditionLogic.MaxPerGroup - 1).Select(i => OnParameter("AFK", ParameterCondition.IsTrue)).ToArray());
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            view.Tree.SelectEntry(trigger.id);

            var inCard = view.Detail.Q<Button>("ad-when-add-and-0");
            var below = view.Detail.Q<Button>("ad-when-add-and");

            Assert.That(inCard.enabledSelf, Is.True);
            Assert.That(below.enabledSelf, Is.True);
            Assert.That(inCard.tooltip, Is.EqualTo(L.Text("ad.ui.when.add_and.tip")));
        }
    }
}
#endif
