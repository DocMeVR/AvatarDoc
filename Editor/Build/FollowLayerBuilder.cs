#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Builds one FX layer per followed object, the sole writer of that object's constraint source weights. An
    /// entry's own layer (a toggle's, a group's or a trigger's) never touches them: each of those only knows its
    /// own claim, so two of them writing the same constraint from two layers would leave the object hanging
    /// between two places whenever both are met. This layer owns every weight instead: a Rest state (the rest
    /// source at 1, every claim at 0) and one state per claiming entry (its own source at 1, every other source,
    /// rest included, at 0), its blend seconds as the transition duration both ways.
    /// "The later entry wins while several are met" cannot rest on AnyState list order alone: once a claim state
    /// is entered, its own transition there is a self transition and is dropped, so an earlier claim elsewhere in
    /// the list would still be takeable and the layer would slide back to it, then back to the later one, forever
    /// (with zero blend time, every single frame). Instead there is no AnyState here at all: Rest transitions to
    /// every claim, later claim checked first; each claim state transitions only to LATER claims (their own Enter,
    /// checked in order) and only after those, back to Rest on its own Leave. A transition to an earlier claim
    /// never exists, so the layer can never slide backward into one that is still met.
    /// </summary>
    public static class FollowLayerBuilder
    {
        public static List<ModelMessage> Build(AnimatorServicesContext asc, MenuTree tree, ParameterPlan plan, FollowPlan followPlan, IReadOnlyList<TriggerPlan> triggerPlans, ICollection<string> pinLayerNames = null)
        {
            var messages = new List<ModelMessage>();
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return messages;
            bool writeDefaults = AnimatorBuilder.DecideWriteDefaults(fx, pinLayerNames);
            var triggerByEntry = (triggerPlans ?? new List<TriggerPlan>()).ToDictionary(p => p.Entry);

            foreach (var pair in followPlan.ByTarget)
                BuildLayer(asc, fx, writeDefaults, pair.Key, pair.Value, tree, plan, triggerByEntry, messages);
            return messages;
        }

        sealed class Claim
        {
            public MenuEntry Entry;
            public int Index;
            public VirtualState State;
            public IReadOnlyList<ImmutableList<AnimatorCondition>> Enter;
            public IReadOnlyList<ImmutableList<AnimatorCondition>> Leave;
            public float Blend;
            public bool OnByDefault;
            /// <summary>Set for a grabbable's own claim. Two grab claims of the SAME entry never hand the object to each other while it is held (see the transition loop below); every other pair still follows the general "later entry wins" rule.</summary>
            public bool IsGrabClaim;
        }

        static void BuildLayer(AnimatorServicesContext asc, VirtualAnimatorController fx, bool writeDefaults, GameObject target, FollowConstraint info, MenuTree tree, ParameterPlan plan, Dictionary<MenuEntry, TriggerPlan> triggerByEntry, List<ModelMessage> messages)
        {
            var name = AnimatorBuilder.UniqueLayerName(fx, "AD: Follow " + target.name);
            var layer = fx.AddLayer(LayerPriority.Default, name);
            var sm = layer.StateMachine;

            var rest = sm.AddState("Rest", RestClip(asc, name, info), new Vector3(0f, 0f, 0f));
            rest.WriteDefaultValues = writeDefaults;
            sm.DefaultState = rest;

            // Resolved first, so a claim that never resolves (its trigger turned out to be a blend shape link, or
            // its toggle carries no binding) leaves no unreachable state behind and is reported instead of
            // silently never following.
            var claims = new List<Claim>();
            int row = 1;
            foreach (var followClaim in info.Claims)
            {
                var entry = followClaim.Entry;
                if (!Resolve(fx, tree, plan, triggerByEntry, followClaim, out var enter, out var leave, out float blend, out bool onByDefault, out string stateName))
                {
                    messages.Add(ModelMessage.Warning("ad.warn.follow_claim_unresolved", entry.PlainName, target.name, ComponentOf(tree, entry)).For(entry.id));
                    continue;
                }
                var state = sm.AddState(stateName, ClaimClip(asc, name, stateName, info, followClaim.SourceIndex), new Vector3(300f, 120f * row++, 0f));
                state.WriteDefaultValues = writeDefaults;
                claims.Add(new Claim { Entry = entry, Index = followClaim.SourceIndex, State = state, Enter = enter, Leave = leave, Blend = blend, OnByDefault = onByDefault, IsGrabClaim = followClaim.Grab != null });
            }

            // Rest: one transition per claim, later claim checked first, so the later one wins when several are
            // met starting from Rest.
            var restTransitions = new List<VirtualStateTransition>();
            for (int i = claims.Count - 1; i >= 0; i--)
                foreach (var enter in claims[i].Enter)
                    restTransitions.Add(AnimatorBuilder.Transition(claims[i].State, enter, claims[i].Blend));
            rest.Transitions = ImmutableList.CreateRange(restTransitions);

            // Each claim: a transition to every claim after it in the list (their own Enter, earliest of the
            // later ones checked first) so a later claim being met always wins even mid-hold, then its own Leave
            // transitions back to Rest. Nothing here ever transitions to an EARLIER claim.
            // The one exception: two grab claims of the SAME grabbable never hand the object to each other this
            // way. The hand that grabbed it keeps it until release, or bringing a free hand near an already held
            // prop would steal it into that hand the instant its own contact condition is met. Both hands
            // starting to meet their conditions at once from Rest still resolves deterministically: the later
            // claim is still checked first there.
            for (int i = 0; i < claims.Count; i++)
            {
                var transitions = new List<VirtualStateTransition>();
                for (int j = i + 1; j < claims.Count; j++)
                {
                    if (claims[i].IsGrabClaim && claims[j].IsGrabClaim && claims[i].Entry == claims[j].Entry) continue;
                    foreach (var enter in claims[j].Enter)
                        transitions.Add(AnimatorBuilder.Transition(claims[j].State, enter, claims[j].Blend));
                }
                foreach (var leave in claims[i].Leave)
                    transitions.Add(AnimatorBuilder.Transition(rest, leave, claims[i].Blend));
                claims[i].State.Transitions = ImmutableList.CreateRange(transitions);
            }

            // A claim on by default starts in its own state; with more than one, the later entry's still wins.
            var defaultClaim = claims.LastOrDefault(c => c.OnByDefault);
            if (defaultClaim != null)
            {
                sm.DefaultState = defaultClaim.State;
                BakeDefault(info, defaultClaim.Index);
            }
        }

        /// <summary>The AvatarDoc component that owns this entry, for a message naming it; null if the entry is not found among either list, which does not happen in practice.</summary>
        static AvatarDoc ComponentOf(MenuTree tree, MenuEntry entry)
        {
            var control = tree.Controls.FirstOrDefault(c => c.Entry == entry);
            if (control != null) return control.Component;
            return tree.Triggers.FirstOrDefault(t => t.Entry == entry)?.Component;
        }

        /// <summary>
        /// The enter/leave conditions, blend seconds, default-on flag and state name of one claim: a grabbable's
        /// own claim (its PhysBone held AND its hand's contact near, ANDed the same way a trigger's condition
        /// group is), a toggle (its own binding), or a condition trigger (its own plan). False when none of these
        /// resolve: a toggle excluded after the follow plan ran, a trigger this platform never built, or a blend
        /// shape linked trigger, which rewrites clips from a continuous source value and has no on/off state to
        /// follow with.
        /// </summary>
        static bool Resolve(VirtualAnimatorController fx, MenuTree tree, ParameterPlan plan, Dictionary<MenuEntry, TriggerPlan> triggerByEntry, FollowClaim claim,
            out IReadOnlyList<ImmutableList<AnimatorCondition>> enter, out IReadOnlyList<ImmutableList<AnimatorCondition>> leave, out float blend, out bool onByDefault, out string stateName)
        {
            var entry = claim.Entry;
            enter = null;
            leave = null;
            blend = Mathf.Clamp(entry.blendSeconds, 0f, 10f);
            onByDefault = false;
            stateName = entry.PlainName;
            if (claim.Grab != null)
            {
                blend = Mathf.Clamp(claim.Grab.BlendSeconds, 0f, 10f);
                stateName = entry.PlainName + (claim.Grab.Hand == GrabHands.Left ? " (Left)" : " (Right)");
                AnimatorBuilder.EnsureParameter(fx, claim.Grab.GrabbedParameter, PlannedType.Bool, 0f);
                AnimatorBuilder.EnsureParameter(fx, claim.Grab.HandParameter, PlannedType.Bool, 0f);
                var group = new List<TriggerCondition>
                {
                    TriggerPlanner.BuildCondition(claim.Grab.GrabbedParameter, PlannedType.Bool, ParameterCondition.IsTrue, 0f),
                    TriggerPlanner.BuildCondition(claim.Grab.HandParameter, PlannedType.Bool, ParameterCondition.IsTrue, 0f),
                };
                // With its own menu toggle, the entry's own parameter is ANDed in too: a disabled PhysBone simply
                // stops updating its own "held" parameter, it does not force it false, so without this a claim
                // already held would stay held (the contact rides along in the hand) until the wearer grabbed
                // and released once more. ANDing the toggle here means Leave's own De Morgan expansion always
                // carries one transition on the toggle alone, so turning it off drops the claim whatever the
                // disabled bone still reports.
                var control = tree.Controls.FirstOrDefault(c => c.Entry == entry);
                if (control != null && plan.Bindings.TryGetValue(control, out var toggleBinding))
                    group.Add(TriggerPlanner.BuildCondition(toggleBinding.Parameter.Name, toggleBinding.Parameter.Type, ParameterCondition.Equals, toggleBinding.Value));
                var met = TriggerPlanner.AndGate(Enumerable.Empty<AnimatorCondition>(), group);
                enter = ConditionLogic.Enter(met, null).Select(list => ImmutableList.CreateRange(list)).ToList();
                leave = ConditionLogic.Leave(met, null).Select(list => ImmutableList.CreateRange(list)).ToList();
                return true;
            }
            if (entry.kind == EntryKind.Toggle)
            {
                var control = tree.Controls.FirstOrDefault(c => c.Entry == entry);
                if (control == null || !plan.Bindings.TryGetValue(control, out var binding)) return false;
                enter = new[] { AnimatorBuilder.Conditions(binding.Parameter, binding.Value, true) };
                leave = new[] { AnimatorBuilder.Conditions(binding.Parameter, binding.Value, false) };
                onByDefault = binding.OnByDefault;
                return true;
            }
            if (!triggerByEntry.TryGetValue(entry, out var triggerPlan) || triggerPlan.Kind != TriggerKind.Condition) return false;
            enter = triggerPlan.Enter.Select(list => ImmutableList.CreateRange(list)).ToList();
            leave = triggerPlan.Leave.Select(list => ImmutableList.CreateRange(list)).ToList();
            return true;
        }

        static VirtualClip RestClip(AnimatorServicesContext asc, string layerName, FollowConstraint info)
        {
            var content = new ClipContent();
            content.SetFollowWeight(info.Constraint, 0, 1f);
            foreach (var claim in info.Claims) content.SetFollowWeight(info.Constraint, claim.SourceIndex, 0f);
            return content.ToClip(asc, layerName + " Rest");
        }

        static VirtualClip ClaimClip(AnimatorServicesContext asc, string layerName, string stateName, FollowConstraint info, int claimIndex)
        {
            var content = new ClipContent();
            content.SetFollowWeight(info.Constraint, 0, 0f);
            foreach (var claim in info.Claims) content.SetFollowWeight(info.Constraint, claim.SourceIndex, claim.SourceIndex == claimIndex ? 1f : 0f);
            return content.ToClip(asc, layerName + " " + stateName);
        }

        /// <summary>Writes the same default straight onto the clone's real constraint, so a Write-Defaults-off avatar (and the Unity editor, before any animator plays a frame) already shows a claim that starts on.</summary>
        static void BakeDefault(FollowConstraint info, int winningIndex)
        {
            var content = new ClipContent();
            content.SetFollowWeight(info.Constraint, 0, 0f);
            foreach (var claim in info.Claims) content.SetFollowWeight(info.Constraint, claim.SourceIndex, claim.SourceIndex == winningIndex ? 1f : 0f);
            content.ApplyToScene();
        }
    }
}
#endif
