#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Builds one VRCParentConstraint per followed object, on the build clone only, and hands out a source index to
    /// every toggle or trigger that follows it. Source 0 is always the object's own rest place: the transform it
    /// already hangs under, at its current local offset. Every entry that names the object adds one more source, so
    /// two entries following the same object share one constraint instead of fighting over two. Never writes a
    /// source's weight: that belongs to FollowLayerBuilder alone, the one layer that owns every weight of a given
    /// constraint.
    /// </summary>
    public static class FollowBuilder
    {
        public static (FollowPlan Plan, List<ModelMessage> Messages) Build(BuildContext context, MenuTree tree)
        {
            var plan = new FollowPlan();
            var messages = new List<ModelMessage>();

            void Handle(MenuEntry entry, bool excluded, AvatarDoc component)
            {
                if (excluded || entry.follows.Count == 0) return;
                if (entry.kind != EntryKind.Toggle && entry.kind != EntryKind.Trigger) return;
                // A Follows effect cannot fade with distance: it has no on/off state of its own to ride a blend
                // between, so Follow distance never claims a target, and the inspector warns about it instead.
                if (entry.IsFollowDistanceTrigger) return;
                foreach (var follow in entry.follows)
                {
                    if (!IsUsable(tree, plan, follow, entry, component, messages)) continue;
                    var info = GetOrCreate(plan, follow.target);
                    // The model already refuses a 16th claim as an error, which blocks the whole build; this is a
                    // second line of defence so a build reached with stale or hand-edited data still never writes
                    // a binding VRChat has no slot for.
                    if (info.Claims.Count >= FollowPlan.MaxClaims) continue;
                    int index = info.Claims.Count + 1;
                    AddSource(info.Constraint, follow.target, follow.follows, follow.keepOffset, index);
                    info.Claims.Add(new FollowClaim { Entry = entry, SourceIndex = index });
                }
            }

            foreach (var control in tree.Controls) Handle(control.Entry, control.Excluded, control.Component);
            foreach (var trigger in tree.Triggers) Handle(trigger.Entry, trigger.Excluded, trigger.Component);
            return (plan, messages);
        }

        /// <summary>
        /// The same structural checks MenuModel already reports as errors, read again at build time so a build
        /// never acts on a row the model rejected. The one thing the model cannot have seen is a constraint another
        /// NDMF plugin puts on the clone between the model's own validation and this call, so that case is reported
        /// here too rather than silently dropping the row.
        /// </summary>
        static bool IsUsable(MenuTree tree, FollowPlan plan, FollowTarget follow, MenuEntry entry, AvatarDoc component, List<ModelMessage> messages)
        {
            if (follow.target == null || follow.follows == null) return false;
            if (follow.target.transform == tree.AvatarRoot) return false;
            if (follow.target == follow.follows.gameObject || follow.follows.IsChildOf(follow.target.transform)) return false;
            if (MenuModel.IsOutsideAvatar(tree.AvatarRoot, follow.target.transform)) return false;
            if (MenuModel.IsOutsideAvatar(tree.AvatarRoot, follow.follows)) return false;
            // An object this same build already gave a constraint to is fine to add another source to; one that
            // still carries a foreign constraint is left alone and reported, since the model could not have seen it.
            if (!plan.ByTarget.ContainsKey(follow.target) && MenuModel.HasExistingConstraint(follow.target))
            {
                messages.Add(ModelMessage.Error("ad.error.follow_existing_constraint", entry.PlainName, follow.target.name, component).For(entry.id));
                return false;
            }
            return true;
        }

        /// <summary>The followed object's own constraint entry, made on first use. Shared with GrabBuilder, which adds one claim of its own per allowed hand to the very same constraint an ordinary follow row would use.</summary>
        internal static FollowConstraint GetOrCreate(FollowPlan plan, GameObject target)
        {
            if (!plan.ByTarget.TryGetValue(target, out var info))
            {
                info = new FollowConstraint { Constraint = CreateConstraint(target) };
                plan.ByTarget[target] = info;
            }
            return info;
        }

        /// <summary>Creates the constraint on the clone, locked and active, with source 0 at the object's current rest place.</summary>
        static VRCParentConstraint CreateConstraint(GameObject target)
        {
            var constraint = target.AddComponent<VRCParentConstraint>();
            constraint.IsActive = true;
            constraint.Locked = true;
            constraint.GlobalWeight = 1f;
            var t = target.transform;
            constraint.Sources.Add(new VRCConstraintSource
            {
                SourceTransform = t.parent,
                Weight = 1f,
                ParentPositionOffset = t.localPosition,
                ParentRotationOffset = t.localEulerAngles,
            });
            return constraint;
        }

        /// <summary>
        /// Adds one claim's source. keepOffset reproduces the pose the object has relative to the followed
        /// transform as the creator posed it in the scene: drop the sword into the hand, switch the entry on, and
        /// that is where it lands. Off snaps the object onto the followed transform's own origin with no offset,
        /// which is what a new row starts with, since that is what the grab case wants out of the box. Starts at
        /// weight 0: FollowLayerBuilder's clips animate it, never this entry's own layer. Shared with GrabBuilder,
        /// whose own claims add a source the same way, one per allowed hand.
        /// </summary>
        internal static void AddSource(VRCParentConstraint constraint, GameObject target, Transform follows, bool keepOffset, int index)
        {
            Vector3 posOffset = Vector3.zero;
            Vector3 rotOffset = Vector3.zero;
            if (keepOffset)
            {
                var t = target.transform;
                posOffset = follows.InverseTransformPoint(t.position);
                rotOffset = (Quaternion.Inverse(follows.rotation) * t.rotation).eulerAngles;
            }
            constraint.Sources.Add(new VRCConstraintSource
            {
                SourceTransform = follows,
                Weight = 0f,
                ParentPositionOffset = posOffset,
                ParentRotationOffset = rotOffset,
            });
        }
    }
}
#endif
