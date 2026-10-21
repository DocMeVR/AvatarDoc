#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>One FX layer per condition-driven trigger: Off and On states switched by the planned conditions. A parameter Fade's layer is built here too, in the same order, so the later of two triggers that hold one shape wins whichever of the two kinds each is.</summary>
    public static class TriggerBuilder
    {
        public static List<ModelMessage> Build(AnimatorServicesContext asc, MenuTree tree, IReadOnlyList<TriggerPlan> plans, BaseState baseState, ICollection<string> pinLayerNames = null, PinLayerState pinState = null)
        {
            var messages = new List<ModelMessage>();
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return messages;
            bool writeDefaults = AnimatorBuilder.DecideWriteDefaults(fx, pinLayerNames);

            foreach (var plan in plans)
            {
                if (plan.Kind == TriggerKind.Fade && plan.Entry.IsFollowValueTrigger)
                {
                    TouchBuilder.BuildFollowValueLayer(asc, fx, tree, plan, baseState, pinLayerNames, pinState);
                    continue;
                }
                if (plan.Kind != TriggerKind.Condition) continue;
                foreach (var condition in plan.Conditions)
                    AnimatorBuilder.EnsureParameter(fx, condition.Parameter, condition.Type, condition.Default);
                var enter = plan.Enter;
                var leave = plan.Leave;

                var offContent = new ClipContent(tree.AvatarRoot);
                offContent.WriteToggle(plan.Entry, false, baseState, ShapeSet.MainOnly);
                var onContent = new ClipContent(tree.AvatarRoot);
                onContent.WriteToggle(plan.Entry, true, baseState, ShapeSet.MainOnly);

                var name = AnimatorBuilder.UniqueLayerName(fx, "AD: " + plan.Entry.PlainName);
                // A trigger whose blend shape targets are all pinned writes nothing outside its pin layer, so the main layer is left out.
                if (!plan.Entry.HasPins || offContent.Count > 0 || onContent.Count > 0)
                {
                    // With Write Defaults off, every property the trigger moves rests once at load and once on the way
                    // out, in Reset, unless another layer owns it, the same rule a pin layer follows; Off writes
                    // nothing for it. Only the final pass sees every layer Modular Avatar and VRCFury merge in, so Off
                    // hands its property values to it. With Write Defaults on, Off writes every property the layer
                    // animates on every frame anyway, the material's own value when it has no curve, so it keeps the
                    // rest values itself and a row's off value shows the whole time the trigger rests. With no final
                    // pass to hand them to, Off keeps them too.
                    var rests = pinState != null && !writeDefaults ? offContent.TakePropertyWrites() : new List<(Renderer renderer, string property, float value)>();
                    foreach (var (renderer, property, value) in rests)
                        pinState.PropertyRests.Add(new PinLayerState.PropertyRest { LayerName = name, Renderer = renderer, Property = property, RestValue = value });
                    var layer = fx.AddLayer(LayerPriority.Default, name);
                    var sm = layer.StateMachine;
                    var off = sm.AddState("Off", offContent.ToClip(asc, name + " Off"), new Vector3(0f, 0f, 0f));
                    var on = sm.AddState("On", onContent.ToClip(asc, name + " On"), new Vector3(0f, 120f, 0f));
                    off.WriteDefaultValues = writeDefaults;
                    on.WriteDefaultValues = writeDefaults;
                    sm.DefaultState = off;
                    var back = rests.Count > 0 ? AnimatorBuilder.AddResetState(sm, name, off, new Vector3(300f, 60f, 0f), writeDefaults, pinState) : off;
                    off.Transitions = ImmutableList.CreateRange(enter.Select(list => AnimatorBuilder.Transition(on, ImmutableList.CreateRange(list))));
                    on.Transitions = ImmutableList.CreateRange(leave.Select(list => AnimatorBuilder.Transition(back, ImmutableList.CreateRange(list))));
                }

                if (!plan.Entry.HasPins) continue;
                var member = new AnimatorBuilder.PinMember
                {
                    StateName = "On",
                    Entry = plan.Entry,
                    Enter = enter.Select(list => ImmutableList.CreateRange(list)).ToList(),
                    Leave = leave.Select(list => ImmutableList.CreateRange(list)).ToList(),
                    PauseFlag = plan.PauseFlag,
                };
                AnimatorBuilder.BuildPinLayer(asc, fx, name, new[] { member }, baseState, tree.AvatarRoot, pinLayerNames: pinLayerNames, pinState: pinState);
            }
            return messages;
        }

    }
}
#endif
