#if DOCME_VRCSDK3A
using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// A grabbable's own claim: which hand it answers to and the local parameters its PhysBone and its hand
    /// contact carry. Null on an ordinary follow row's claim, whose conditions come from the entry's own toggle
    /// binding or trigger plan instead.
    /// </summary>
    public sealed class GrabClaimInfo
    {
        public GrabHands Hand;
        public string GrabbedParameter = "";
        public string HandParameter = "";
        public float BlendSeconds;
    }

    /// <summary>One source of a followed object's constraint: the entry that claims it, the source index it was given, and, for a grabbable's own claim, which hand it answers to.</summary>
    public sealed class FollowClaim
    {
        public MenuEntry Entry;
        public int SourceIndex;
        public GrabClaimInfo Grab;
    }

    /// <summary>One followed object's constraint and every claim on a source of it, in tree order. Source 0 is always the object's rest place; a claim's own index is one plus its place in this list.</summary>
    sealed class FollowConstraint
    {
        public VRCParentConstraint Constraint;
        public readonly List<FollowClaim> Claims = new List<FollowClaim>();
    }

    /// <summary>
    /// What FollowBuilder and GrabBuilder made: the constraint and source index of every claim they built. Carries
    /// no NDMF dependency of its own, unlike FollowBuilder, so anything that only needs to read the plan
    /// (ClipContent, the follow layer builder's callers) can do so under the same guard as the rest of the model.
    /// </summary>
    public sealed class FollowPlan
    {
        /// <summary>VRChat's own inline slots run 0..15; source 0 is the rest place, so at most this many claims may share one object.</summary>
        public const int MaxClaims = 15;

        internal readonly Dictionary<GameObject, FollowConstraint> ByTarget = new Dictionary<GameObject, FollowConstraint>();

        /// <summary>The PhysBone a grabbable's own menu toggle enables and disables, one per grabbable entry that has a menu toggle. Read by AnimatorBuilder and DefaultsApplier so the toggle's own layer can write it.</summary>
        public readonly Dictionary<MenuEntry, VRCPhysBone> GrabPhysBones = new Dictionary<MenuEntry, VRCPhysBone>();

        /// <summary>The first source this entry claims on the object, for an ordinary follow row. A grabbable claims more than one (one per allowed hand); use TryGetGrabSource for those.</summary>
        public bool TryGetSource(MenuEntry entry, GameObject target, out VRCParentConstraint constraint, out int sourceIndex)
        {
            constraint = null;
            sourceIndex = -1;
            if (target == null || !ByTarget.TryGetValue(target, out var info)) return false;
            foreach (var claim in info.Claims)
            {
                if (claim.Entry != entry) continue;
                constraint = info.Constraint;
                sourceIndex = claim.SourceIndex;
                return true;
            }
            return false;
        }

        /// <summary>The source a grabbable's claim for one hand uses, or false when that hand was never built (not an allowed hand, or the humanoid rig had no bone for it).</summary>
        public bool TryGetGrabSource(MenuEntry entry, GameObject target, GrabHands hand, out VRCParentConstraint constraint, out int sourceIndex)
        {
            constraint = null;
            sourceIndex = -1;
            if (target == null || !ByTarget.TryGetValue(target, out var info)) return false;
            foreach (var claim in info.Claims)
            {
                if (claim.Entry != entry || claim.Grab == null || claim.Grab.Hand != hand) continue;
                constraint = info.Constraint;
                sourceIndex = claim.SourceIndex;
                return true;
            }
            return false;
        }

        /// <summary>How many claims currently share this object, for tests and messages; 0 when it has none or is not followed at all.</summary>
        public int ClaimCount(GameObject target) => target != null && ByTarget.TryGetValue(target, out var info) ? info.Claims.Count : 0;
    }
}
#endif
