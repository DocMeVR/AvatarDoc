#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// What one entry's row adds to the avatar when it builds: menu controls, synced bits, FX layers, and the
    /// components a trigger, a follow or a grabbable creates on the upload copy. Every number here comes from the
    /// plan the inspector already holds (or a read of the entry's own fields and the real scene objects they name,
    /// the same reads MenuModel's own validation already makes), never a second build or a guess.
    /// </summary>
    public static class WhatThisBuildsModel
    {
        public readonly struct Summary
        {
            public readonly int MenuControls;
            public readonly int Bits;
            public readonly int FxLayers;
            public readonly int Contacts;
            public readonly int PhysBones;
            public readonly int Constraints;
            /// <summary>The animation clips an override rewrites.</summary>
            public readonly int RewrittenClips;

            public Summary(int menuControls, int bits, int fxLayers, int contacts, int physBones, int constraints, int rewrittenClips = 0)
            {
                RewrittenClips = rewrittenClips;
                MenuControls = menuControls;
                Bits = bits;
                FxLayers = fxLayers;
                Contacts = contacts;
                PhysBones = physBones;
                Constraints = constraints;
            }

            public bool IsEmpty => MenuControls == 0 && Bits == 0 && FxLayers == 0 && Contacts == 0 && PhysBones == 0 && Constraints == 0 && RewrittenClips == 0;
        }

        /// <summary>The one line for a row's detail panel: the parts that are not zero, comma separated, or a reason nothing builds.</summary>
        public static string Describe(AvatarPlan plan, TreeRow row)
        {
            var entry = row?.Entry;
            if (plan == null || entry == null) return "";
            var summary = Compute(plan, row);
            if (!summary.IsEmpty)
            {
                var parts = new List<string>();
                if (summary.MenuControls > 0) parts.Add(UiText.Count(summary.MenuControls, "ad.ui.builds.controls.one", "ad.ui.builds.controls"));
                if (summary.Bits > 0) parts.Add(UiText.Count(summary.Bits, "ad.ui.builds.bits.one", "ad.ui.builds.bits"));
                if (summary.FxLayers > 0) parts.Add(UiText.Count(summary.FxLayers, "ad.ui.builds.layers.one", "ad.ui.builds.layers"));
                if (summary.Contacts > 0) parts.Add(UiText.Count(summary.Contacts, "ad.ui.builds.contacts.one", "ad.ui.builds.contacts"));
                if (summary.PhysBones > 0) parts.Add(UiText.Count(summary.PhysBones, "ad.ui.builds.physbones.one", "ad.ui.builds.physbones"));
                if (summary.Constraints > 0) parts.Add(UiText.Count(summary.Constraints, "ad.ui.builds.constraints.one", "ad.ui.builds.constraints"));
                if (summary.RewrittenClips > 0) parts.Add(UiText.Count(summary.RewrittenClips, "ad.ui.builds.clips.one", "ad.ui.builds.clips"));
                if (entry.kind == EntryKind.Trigger && entry.source == TriggerSource.BlendShape)
                {
                    parts.Add(entry.linkStyle == BlendShapeLinkStyle.Switch
                        ? string.Format(L.Text("ad.ui.builds.switch"), Number(entry.sourceThreshold))
                        : string.Format(L.Text("ad.ui.builds.smooth"), Number(entry.EffectiveSmoothStart), Number(entry.EffectiveSmoothFull)));
                    parts.Add(L.Text("ad.ui.builds.mirror"));
                }
                if (entry.IsFollowDistanceTrigger)
                    parts.Add(string.Format(L.Text("ad.ui.builds.follow_distance"), Mathf.RoundToInt(entry.EffectiveContactFullAt * 100f)));
                // A Fade contact or a blend shape trigger that moves a shape VRChat blinks or speaks with pauses that
                // part of tracking while its effects move, through the shared pause layers; a toggle or a Hold
                // trigger does while its pin layer holds one (a Pin row, or a rule).
                if (entry.HasDriver || entry.OffersShapeSets)
                {
                    var descriptor = plan.AvatarRoot != null ? plan.AvatarRoot.GetComponent<VRCAvatarDescriptor>() : null;
                    bool eyes, mouth;
                    if (entry.HasDriver) TouchBuilder.FollowDistanceTracking(entry, plan.Tree.HeldContext, out eyes, out mouth);
                    else ShapeSetModel.PinTracking(entry, plan.Tree.HeldContext, out eyes, out mouth);
                    if (eyes || mouth)
                    {
                        bool fade = entry.IsFollowDistanceTrigger;
                        var fx = descriptor != null ? ParametersWriter.FxOf(descriptor) : null;
                        var (eyesOwner, mouthOwner) = TouchBuilder.ExistingTrackingControlOwners(fx);
                        if (eyes) parts.Add(eyesOwner != null ? string.Format(L.Text("ad.ui.builds.blink_left_to"), eyesOwner) : L.Text(fade ? "ad.ui.builds.pauses_blink" : "ad.ui.builds.pauses_blink_shape"));
                        if (mouth) parts.Add(mouthOwner != null ? string.Format(L.Text("ad.ui.builds.lipsync_left_to"), mouthOwner) : L.Text(fade ? "ad.ui.builds.pauses_lipsync" : "ad.ui.builds.pauses_lipsync_shape"));
                    }
                }
                return string.Format(L.Text("ad.ui.builds.line"), L.JoinList(parts));
            }
            if (row.OwnerDisabled) return L.Text("ad.ui.builds.nothing.disabled");
            if (row.Excluded) return string.Format(L.Text("ad.ui.builds.nothing.platform"), HeaderView.CurrentPlatformName);
            if (entry.kind == EntryKind.Override && entry.blendShapes.Any(t => t != null && t.renderer != null && !string.IsNullOrEmpty(t.shape))) return L.Text("ad.ui.builds.nothing.no_clips");
            return L.Text("ad.ui.builds.nothing.empty");
        }

        /// <summary>The raw counts, for tests to check without parsing the formatted line.</summary>
        public static Summary Compute(AvatarPlan plan, TreeRow row)
        {
            var entry = row?.Entry;
            if (plan == null || entry == null || row.Excluded) return default;

            int menuControls = entry.kind == EntryKind.Submenu ? 1 : !entry.IsRule && !entry.NoMenuControl ? 1 : 0;

            int bits = 0;
            foreach (var parameter in plan.Plan.Parameters)
                if (!parameter.ReusesExisting && parameter.Controls.Any(c => c.Entry == entry))
                    bits += parameter.Bits;

            int fxLayers = FxLayerCount(entry, plan.AvatarRoot);

            int contacts = 0, physBones = 0, constraints = 0;
            if (entry.kind == EntryKind.Trigger)
            {
                // Follow distance reads its driving contact and its requirements, whatever else its stale data
                // still carries in other cards; a blend shape trigger's groups are its requirements alone.
                if (entry.IsFollowDistanceTrigger)
                    contacts = 1 + entry.Requirements().Count(r => r.condition.kind == WhenConditionKind.Contact);
                else
                    foreach (var group in entry.EffectiveWhen)
                        foreach (var condition in group.all)
                            if (condition != null && condition.kind == WhenConditionKind.Contact) contacts++;
            }
            else if (entry.kind == EntryKind.Grabbable && entry.grabObject != null)
            {
                if (entry.grabObject.GetComponent<VRCPhysBone>() == null) physBones = 1;
                contacts = entry.grabHands == GrabHands.Both ? 2 : 1;
                if (!MenuModel.HasExistingConstraint(entry.grabObject) && ClaimsTarget(plan, entry, entry.grabObject)) constraints++;
            }

            if (entry.kind == EntryKind.Toggle || entry.kind == EntryKind.Trigger)
            {
                var targets = new HashSet<UnityEngine.GameObject>();
                foreach (var follow in entry.follows)
                    if (follow.target != null && follow.follows != null && !MenuModel.HasExistingConstraint(follow.target) && ClaimsTarget(plan, entry, follow.target))
                        targets.Add(follow.target);
                constraints += targets.Count;
            }

            int clips = entry.kind == EntryKind.Override && plan.Usage != null
                ? entry.blendShapes.Where(t => t != null && t.renderer != null).SelectMany(t => plan.Usage.ClipsFor(t.renderer, t.shape)).Distinct().Count()
                : 0;

            return new Summary(menuControls, bits, fxLayers, contacts, physBones, constraints, clips);
        }

        /// <summary>Whether this entry is the one credited with a shared constraint's target: the plan's own claim map says so, or, for a plan built without one (a test's own hand built AvatarPlan), every entry is credited on its own, the way the model worked before entries could share credit.</summary>
        static bool ClaimsTarget(AvatarPlan plan, MenuEntry entry, UnityEngine.GameObject target)
        {
            if (plan.ConstraintClaims == null) return true;
            return plan.ConstraintClaims.TryGetValue(target, out var claimedBy) && claimedBy == entry.id;
        }

        static string Number(float value) => value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// The FX layers this entry's own control or trigger would add: matches AnimatorBuilder.Build's and
        /// TriggerBuilder.Build's own choices (a parameter control drives its parameter and nothing else; an
        /// exclusive group shares one layer across its members; a pinned toggle or trigger adds a pin layer of its
        /// own and keeps its main layer only when something besides its pins goes there; a blend shape trigger,
        /// a flip and a Fade contact build one layer of their own), without building anything to find out.
        /// </summary>
        static int FxLayerCount(MenuEntry entry, Transform avatarRoot)
        {
            if (entry.IsParameterControl) return 0;
            switch (entry.kind)
            {
                case EntryKind.Preset:
                case EntryKind.Radial:
                    return entry.HasEffects ? 1 : 0;
                case EntryKind.Toggle:
                    if (!entry.HasEffects) return 0;
                    if (!string.IsNullOrWhiteSpace(entry.exclusiveGroup)) return 1;
                    return MainAndPinLayers(entry, avatarRoot);
                case EntryKind.Trigger:
                    if (!entry.HasEffects) return 0;
                    if (entry.IsFlipTrigger || entry.IsFollowDistanceTrigger || entry.source == TriggerSource.BlendShape) return 1;
                    return MainAndPinLayers(entry, avatarRoot);
                default:
                    return 0;
            }
        }

        /// <summary>A toggle's or a condition trigger's own layers: the main layer, which the builder leaves out when the entry has pins and nothing else for it to write, plus the pin layer.</summary>
        static int MainAndPinLayers(MenuEntry entry, Transform avatarRoot)
        {
            if (!entry.HasPins) return 1;
            return WritesOutsideItsPins(entry, avatarRoot) ? 2 : 1;
        }

        /// <summary>Whether ClipContent.WriteToggle with ShapeSet.MainOnly, plus a toggle's own custom clips, would write anything for this entry: the condition both builders keep the main layer of a pinned entry on.</summary>
        static bool WritesOutsideItsPins(MenuEntry entry, Transform avatarRoot)
        {
            foreach (var target in entry.objects)
                if (target.target != null && !MenuModel.IsOutsideAvatar(avatarRoot, target.target.transform)) return true;
            foreach (var target in entry.blendShapes)
                if (target.mode != ShapeEffectMode.Pin && MenuModel.ShapeExists(target.renderer, target.shape) && !MenuModel.IsOutsideAvatar(avatarRoot, target.renderer.transform)) return true;
            foreach (var target in entry.materials)
                if (MenuModel.SlotExists(target) && !MenuModel.IsOutsideAvatar(avatarRoot, target.renderer.transform)) return true;
            if (entry.BuildsProperties)
                foreach (var target in entry.properties)
                    if (MenuModel.PropertyExists(target, entry) && !MenuModel.IsOutsideAvatar(avatarRoot, target.renderer.transform)) return true;
            return entry.kind == EntryKind.Toggle && entry.animations.Count > 0 && avatarRoot != null
                && CustomClips.Collect(entry, avatarRoot, new List<ModelMessage>()).Count > 0;
        }
    }
}
#endif
