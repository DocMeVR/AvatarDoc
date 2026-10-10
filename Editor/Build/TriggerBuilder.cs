#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>One FX layer per condition-driven trigger: Off and On states switched by the planned conditions.</summary>
    public static class TriggerBuilder
    {
        public static List<ModelMessage> Build(AnimatorServicesContext asc, MenuTree tree, IReadOnlyList<TriggerPlan> plans, BaseState baseState, ICollection<string> pinLayerNames = null, PinLayerState pinState = null)
        {
            var messages = new List<ModelMessage>();
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return messages;
            bool writeDefaults = AnimatorBuilder.DecideWriteDefaults(fx, pinLayerNames);

            foreach (var plan in plans)
            {
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
                    // Off rests every property the trigger moves unless another layer owns it, the same rule a
                    // pin's off state follows. Only the final pass sees every layer Modular Avatar and VRCFury
                    // merge in, so Off hands its property values to it; with no final pass to hand them to, Off
                    // keeps them.
                    if (pinState != null)
                        foreach (var (renderer, property, value) in offContent.TakePropertyWrites())
                            pinState.PropertyRests.Add(new PinLayerState.PropertyRest { LayerName = name, Renderer = renderer, Property = property, RestValue = value });
                    var layer = fx.AddLayer(LayerPriority.Default, name);
                    var sm = layer.StateMachine;
                    var off = sm.AddState("Off", offContent.ToClip(asc, name + " Off"), new Vector3(0f, 0f, 0f));
                    var on = sm.AddState("On", onContent.ToClip(asc, name + " On"), new Vector3(0f, 120f, 0f));
                    off.WriteDefaultValues = writeDefaults;
                    on.WriteDefaultValues = writeDefaults;
                    sm.DefaultState = off;
                    off.Transitions = ImmutableList.CreateRange(enter.Select(list => AnimatorBuilder.Transition(on, ImmutableList.CreateRange(list))));
                    on.Transitions = ImmutableList.CreateRange(leave.Select(list => AnimatorBuilder.Transition(off, ImmutableList.CreateRange(list))));
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
