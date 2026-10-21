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

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Fade on a parameter trigger: its effects blend with a Float parameter's value, from 0 to Fully on at, while its
    /// other conditions hold. Built by the same layer a Fade contact builds, driven by the parameter instead of a
    /// receiver, and in the place its entry has among the other triggers.
    /// </summary>
    public class FollowValueTests
    {
        TestAvatar avatar;
        AvatarDoc menu;

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
            Undo.ClearUndo(menu);
            avatar.Dispose();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
            GeneratedAssets.CleanUp();
        }

        static string English(string key)
        {
            Assert.That(LocalizationFileTests.ShippedTexts("en-US").TryGetValue(key, out var text), Is.True, key + " is in the English file");
            return text;
        }

        MenuEntry Fade(string parameter, string name = "Strength")
        {
            var trigger = menu.AddEntry(EntryKind.Trigger, name);
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = parameter;
            trigger.condition = ParameterCondition.Greater;
            trigger.touchMode = TouchMode.FollowValue;
            trigger.contactFullAt = 1f;
            trigger.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            return trigger;
        }

        AnimatorController BuildAll(out List<ErrorContext> errors)
        {
            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            errors.AddRange(ErrorReport.CaptureErrors(() => AvatarDoctorPlugin.FinishAfterOtherTools(context)));
            return (AnimatorController)avatar.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
        }

        static void NoErrors(List<ErrorContext> errors)
            => Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error).Select(e => (e.TheError as SimpleError)?.TitleKey), Is.Empty);

        [Test]
        public void AParameterFadeBlendsItsEffectsWithTheFloatWhileItsRequirementsHold()
        {
            var fade = Fade("GestureLeftWeight");
            EntryEditing.AddCondition(menu, fade, 0);
            var requirement = fade.when[0].all[1];
            requirement.kind = WhenConditionKind.Parameter;
            requirement.sourceParameter = "GestureLeft";
            requirement.condition = ParameterCondition.Equals;
            requirement.conditionValue = 1f;
            Assert.That(fade.IsFollowValueTrigger && fade.IsFollowDistanceTrigger, Is.True);

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var layer = fx.layers.Single(l => l.name == "AD: Strength (follow)");
            var near = layer.stateMachine.states.Single(s => s.state.name == "Near").state;
            Assert.That(((BlendTree)near.motion).blendParameter, Is.EqualTo("GestureLeftWeight"), "the parameter itself drives the blend");
            Assert.That(fx.parameters.Single(p => p.name == "GestureLeftWeight").type, Is.EqualTo(AnimatorControllerParameterType.Float));
            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body))
            {
                player.Set("GestureLeftWeight", 0.5f);
                Assert.That(player.Settled("Blush").Last(), Is.EqualTo(0f).Within(0.01f), "the requirement fails: nothing moves");
                player.Set("GestureLeft", 1f);
                Assert.That(player.Settled("Blush").Last(), Is.EqualTo(50f).Within(0.5f), "half the value: half the effect");
                player.Set("GestureLeftWeight", 0f);
                Assert.That(player.Settled("Blush").Last(), Is.EqualTo(0f).Within(0.01f), "at 0: at rest");
            }
        }

        [Test]
        public void AParameterFadeOnABoolBuildsNothingAndSaysWhy()
        {
            var fade = Fade("Existing");

            var plan = AvatarPlanCache.GetFor(menu);
            Assert.That(plan.Messages.Select(m => m.Key), Does.Contain("ad.warn.follow_value_not_float"));
            Assert.That(plan.Triggers.Where(t => t.Entry == fade), Is.Empty);
            var fx = BuildAll(out var errors);
            NoErrors(errors);
            Assert.That(fx.layers.Select(l => l.name), Has.None.Contains("Strength"), "nothing is built for it");
        }

        [Test]
        public void AFadeLeftOnATriggerSwitchedToAParameterStaysHold()
        {
            var trigger = Fade("GestureLeftWeight");
            trigger.touchMode = TouchMode.FollowDistance;

            Assert.That(trigger.IsFollowDistanceTrigger, Is.False, "a contact's Fade left behind by a source switch means nothing on a parameter");
            Assert.That(trigger.IsFollowValueTrigger, Is.False);
            var fx = BuildAll(out var errors);
            NoErrors(errors);
            Assert.That(fx.layers.Select(l => l.name), Does.Contain("AD: Strength (pin)"), "built as Hold, as before");
        }

        AvatarDoctorView View() => new AvatarDoctorView(new SerializedObject(menu), menu);

        [Test]
        public void TheParameterPanelOffersFadeForAFloatOnly()
        {
            var trigger = Fade("Existing");
            trigger.touchMode = TouchMode.Hold;
            var view = View();
            view.Tree.SelectEntry(trigger.id);
            Assert.That(view.Detail.Q<SegmentedChoice>("ad-trigger-value-mode"), Is.Null, "a Bool has no value to follow");

            trigger.sourceParameter = "GestureLeftWeight";
            AvatarPlanCache.Invalidate();
            view = View();
            view.Tree.SelectEntry(trigger.id);
            var mode = view.Detail.Q<SegmentedChoice>("ad-trigger-value-mode");
            Assert.That(mode, Is.Not.Null, "a Float can be followed");
            Assert.That(mode.Items.Select(i => i.text), Is.EqualTo(new[] { English("ad.ui.touch.hold"), English("ad.ui.touch.follow") }));
            Assert.That(mode.Index, Is.EqualTo(0));
            Assert.That(view.Detail.Q<Slider>("ad-trigger-value-fullat"), Is.Null);

            Undo.IncrementCurrentGroup();
            mode.Pick(1);

            Assert.That(menu.FindEntry(trigger.id).touchMode, Is.EqualTo(TouchMode.FollowValue));
            Assert.That(view.Detail.Q<Slider>("ad-trigger-value-fullat"), Is.Not.Null, "Fully on at shows for Fade");
            Assert.That(view.Detail.Q("ad-trigger-condition"), Is.Null, "the value itself moves the effects, so there is no comparison");
            Assert.That(view.Detail.Q<SegmentedChoice>("ad-trigger-value-mode").Index, Is.EqualTo(1));
            Undo.PerformUndo();
            Assert.That(menu.FindEntry(trigger.id).touchMode, Is.EqualTo(TouchMode.Hold), "one Undo step");
        }

        [Test]
        public void TheFadesFirstParameterHasNoComparisonButItsRequirementsDo()
        {
            var fade = Fade("GestureLeftWeight");
            EntryEditing.AddCondition(menu, fade, 0);
            var requirement = fade.when[0].all[1];
            requirement.kind = WhenConditionKind.Parameter;
            requirement.sourceParameter = "GestureLeft";
            requirement.condition = ParameterCondition.Equals;
            requirement.conditionValue = 1f;
            var view = View();
            view.Tree.SelectEntry(fade.id);

            var rows = view.Detail.Query("ad-when-row").ToList();
            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(rows[0].Q("ad-when-comparison"), Is.Null, "its value moves the effects");
            Assert.That(rows[1].Q("ad-when-comparison"), Is.Not.Null, "a requirement compares as ever");
            Assert.That(view.Detail.Q<SegmentedChoice>("ad-trigger-value-mode").Index, Is.EqualTo(1), "Fade, under the cards");
            Assert.That(view.Detail.Q<Slider>("ad-trigger-value-fullat").value, Is.EqualTo(1f));
        }

        [TestCase(false, TestName = "ThePreviewShowsALaterHoldTriggerOverAnEarlierParameterFade")]
        [TestCase(true, TestName = "ThePreviewShowsALaterParameterFadeOverAnEarlierHoldTrigger")]
        public void ThePreviewKeepsTheTreeOrder(bool fadeLast)
        {
            MenuEntry Hold()
            {
                var hold = menu.AddEntry(EntryKind.Trigger, "Victory");
                hold.source = TriggerSource.Parameter;
                hold.sourceParameter = "GestureRight";
                hold.condition = ParameterCondition.Equals;
                hold.conditionValue = 4f;
                hold.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 30f, mode = ShapeEffectMode.Pin });
                return hold;
            }
            var first = fadeLast ? Hold() : Fade("GestureLeftWeight");
            var second = fadeLast ? Fade("GestureLeftWeight") : Hold();
            AvatarPlanCache.Invalidate();

            var states = PreviewModel.Compute(avatar.Root.transform, new[] { menu }, new Dictionary<string, float> { [first.id] = 1f, [second.id] = 1f }, SceneReader.Direct);

            Assert.That(states[avatar.Body].Shapes[avatar.Mesh.GetBlendShapeIndex("Blush")], Is.EqualTo(fadeLast ? 100f : 30f), "the later entry wins, as the build orders their layers");
        }

        [Test]
        public void ParameterFadePanelsFitAndAlignAt300And400()
        {
            var flat = Fade("GestureLeftWeight", "Flat");
            var grouped = Fade("GestureRightWeight", "Grouped");
            EntryEditing.AddCondition(menu, grouped, 0);
            var requirement = grouped.when[0].all[1];
            requirement.kind = WhenConditionKind.Parameter;
            requirement.sourceParameter = "GestureRight";
            requirement.condition = ParameterCondition.Equals;
            requirement.conditionValue = 1f;
            var hold = Fade("GestureLeftWeight", "Hold");
            hold.touchMode = TouchMode.Hold;
            var allow = new PanelAudit.AllowList()
                .Allow(e => e.name == "remove" && e.ClassListContains("ad-w-24"), "a target list row's own trailing x remove button")
                .Allow(e => e.ClassListContains("ad-chip-remove"), "a chip's own remove x")
                .AllowForRule(3, e => e is Toggle && e.parent != null && e.parent.ClassListContains("ad-flags"), "a compact flags row's own toggle keeps its inline caption");
            foreach (float width in new[] { 300f, 400f })
            {
                var view = View();
                LayoutProbe.Measure(view, () =>
                {
                    foreach (var entry in new[] { flat, grouped, hold })
                    {
                        view.Tree.SelectEntry(entry.id);
                        Assert.That(view.Detail.Q<SegmentedChoice>("ad-trigger-value-mode"), Is.Not.Null, entry.name + ": sanity, the Mode row shows");
                        LayoutProbe.Validate(view);
                        PanelAudit.Audit(view.Detail, view.Detail.worldBound.xMax, allow);
                    }
                }, width);
            }
        }

        [Test]
        public void WhatThisBuildsSaysTheFadeFollowsTheValue()
        {
            var fade = Fade("GestureLeftWeight");
            var view = View();
            var row = view.Tree.RowFor(fade.id);

            var line = WhatThisBuildsModel.Describe(view.Plan, row);
            var summary = WhatThisBuildsModel.Compute(view.Plan, row);

            Assert.That(line, Does.Contain(string.Format(English("ad.ui.builds.follow_value"), "GestureLeftWeight", "1")));
            Assert.That(summary.Contacts, Is.EqualTo(0), "a parameter needs no contact");
            Assert.That(summary.FxLayers, Is.EqualTo(1));
            Assert.That(ConditionLogic.Sentence(fade, view.Plan.Tree), Does.Contain(string.Format(English("ad.ui.when.value_moves"), "GestureLeftWeight")));
        }

        [Test]
        public void AParameterFadeWithASecondCardSaysOnlyItsFirstCardCounts()
        {
            var fade = Fade("GestureLeftWeight");
            fade.when = new List<ConditionGroup>
            {
                new ConditionGroup { all = { new WhenCondition { kind = WhenConditionKind.Parameter, sourceParameter = "GestureLeftWeight", condition = ParameterCondition.Greater } } },
                new ConditionGroup { all = { new WhenCondition { kind = WhenConditionKind.Parameter, sourceParameter = "GestureLeft", condition = ParameterCondition.Equals, conditionValue = 1f } } },
            };
            AvatarPlanCache.Invalidate();

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(plan.Messages.Where(m => m.Key == "ad.warn.follow_value_one_card").SelectMany(m => m.EntryIds), Is.EqualTo(new[] { fade.id }));
            var fx = BuildAll(out var errors);
            NoErrors(errors);
            Assert.That(fx.layers.Select(l => l.name), Does.Contain("AD: Strength (follow)"), "the first card still builds");
        }

        [TestCase(20f)]
        [TestCase(60f)]
        [TestCase(144f)]
        public void PlayedBackAParameterFadeOnAnEyelidShapePausesBlinkingOnlyWhileItMoves(float framesPerSecond)
        {
            avatar.AddBodyShapes("Blink");
            avatar.Descriptor.enableEyeLook = true;
            avatar.Descriptor.customEyeLookSettings.eyelidType = VRCAvatarDescriptor.EyelidType.Blendshapes;
            avatar.Descriptor.customEyeLookSettings.eyelidsSkinnedMesh = avatar.Body;
            avatar.Descriptor.customEyeLookSettings.eyelidsBlendshapes = new[] { avatar.Mesh.GetBlendShapeIndex("Blink"), -1, -1 };
            var fade = Fade("GestureLeftWeight");
            fade.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blink", weight = 100f, mode = ShapeEffectMode.Pin });
            AvatarPlanCache.Invalidate();

            var fx = BuildAll(out var errors);

            NoErrors(errors);
            var pause = fx.layers.Single(l => l.name == "AD: Blink pause");
            var flag = pause.stateMachine.states.Single(s => s.state.name == "Idle").state.transitions.Single().conditions.Single().parameter;
            var far = fx.layers.Single(l => l.name == "AD: Strength (follow)").stateMachine.states.Single(s => s.state.name == "Far").state;
            Assert.That(AnimationUtility.GetEditorCurve((AnimationClip)far.motion, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blink")), Is.Null,
                "while nothing moves, the eyelid is VRChat's");
            using (var player = new ResetOnceTests.Player(avatar.Root, fx, avatar.Body, 1f / framesPerSecond))
            {
                player.Settled("Blink");
                Assert.That(player.Float(flag), Is.EqualTo(0f), "at rest: blinking goes on");
                player.Set("GestureLeftWeight", 0.5f);
                Assert.That(player.Settled("Blink").Last(), Is.EqualTo(50f).Within(0.5f), "the fade moves the eyelid");
                Assert.That(player.Float(flag), Is.EqualTo(1f), "and blinking pauses while it does");
                player.Set("GestureLeftWeight", 0f);
                player.Settled("Blink");
                Assert.That(player.Float(flag), Is.EqualTo(0f), "back at 0: blinking comes back");
            }
        }
    }
}
#endif
