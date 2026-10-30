using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DocMe.AvatarDoctor
{
    public enum EntryKind { Submenu = 0, Toggle = 1, Radial = 2, Trigger = 3, Override = 4, Preset = 5, Grabbable = 6 }

    /// <summary>Which hand or hands a grabbable answers to.</summary>
    public enum GrabHands { Both = 0, Left = 1, Right = 2 }

    public enum TriggerSource { Toggle = 0, Parameter = 1, BlendShape = 2, Contact = 3, PhysBone = 4 }

    public enum ParameterCondition { IsTrue = 0, IsFalse = 1, Equals = 2, NotEquals = 3, Greater = 4, Less = 5 }

    public enum BlendShapeLinkMode { Add = 0, Replace = 1 }

    /// <summary>How a blend shape trigger's effects follow its shape: blended between Start and Full, or switched at one weight. Smooth is 0, so every trigger saved before the choice existed loads as Smooth.</summary>
    public enum BlendShapeLinkStyle { Smooth = 0, Switch = 1 }

    /// <summary>Which of a PhysBone's own parameters a PhysBone sourced trigger watches.</summary>
    public enum PhysBoneEvent { Grabbed = 0, Posed = 1, AngleAbove = 2, StretchAbove = 3, SquishAbove = 4 }

    /// <summary>What a Contact or PhysBone sourced trigger does with its condition: hold its own effects, flip another toggle, or blend its effects by how close the hand is. A Parameter sourced trigger holds its effects, or blends them with its first parameter's value (FollowValue).</summary>
    public enum TouchMode { Hold = 0, FlipToggle = 1, FollowDistance = 2, FollowValue = 3 }

    /// <summary>One entry's platform choice. Inherit takes the enclosing submenu's, or the component's when no submenu above it sets one.</summary>
    public enum EntryPlatform { Inherit = 0, Both = 1, PCOnly = 2, MobileOnly = 3 }

    /// <summary>Settings of an exclusive group, kept by the component the group belongs to. A toggle of another component joins the group only through its link (MenuEntry.groupComponent).</summary>
    [Serializable]
    public class ExclusiveGroup
    {
        public string name = "";
        public bool saved = true;
        public bool synced = true;
    }

    /// <summary>One entry of a menu. Entries form a flat list; parentId links an entry to its submenu.</summary>
    [Serializable]
    public class MenuEntry
    {
        public string id = "";
        public string parentId = "";
        public EntryKind kind;
        public string name = "";
        public Texture2D icon;
        /// <summary>Which platforms this entry is built on. Inherit follows the enclosing submenu, then the component.</summary>
        public EntryPlatform platform = EntryPlatform.Inherit;
        /// <summary>Which row of the Gesture faces window made this entry, such as "left:1" for its left Fist trigger, or "switch" for its menu switch. Empty for every entry the creator made; a copy made with Duplicate is the creator's own and starts empty.</summary>
        public string gestureFace = "";

        /// <summary>The name stripped of rich text tags. The build uses this for parameters, layer names, paths and messages.</summary>
        public string PlainName => RichName.Strip(name);

        // Toggle and Radial
        public bool defaultOn;
        [Range(0f, 1f)] public float defaultValue;
        public bool saved = true;
        public bool synced = true;
        public string parameterName = "";
        public float parameterValue = 1f;
        /// <summary>With a custom parameter name: the entry is left out of the build (no menu control, layer or parameter) when nothing on the avatar but AvatarDoc uses that name. For a prefab whose toggles switch parts another tool makes only when the creator turns them on.</summary>
        public bool onlyIfUsed;
        public string exclusiveGroup = "";
        /// <summary>The AvatarDoc whose exclusive group of that name this toggle joins. Empty means its own component.</summary>
        public AvatarDoc groupComponent;
        public int groupValue;
        /// <summary>Seconds the toggle's main layer takes to fade between Off and On. Zero means an instant switch. Ignored on a member of an exclusive group.</summary>
        public float blendSeconds;
        /// <summary>Toggles and radials only: the entry keeps its parameter, layer, group membership and default exactly as if this were false, but gets no menu control and takes no menu slot. False by default, so old data loads as shown in the menu.</summary>
        public bool hiddenFromMenu;

        // Trigger
        public TriggerSource source;
        public string sourceToggleId = "";
        public bool sourceToggleOn = true;
        public string sourceParameter = "";
        public ParameterCondition condition;
        public float conditionValue;
        public SkinnedMeshRenderer sourceRenderer;
        public string sourceShape = "";
        public float sourceThreshold = 50f;
        public BlendShapeLinkMode linkMode;
        public BlendShapeLinkStyle linkStyle;
        /// <summary>Smooth only: the shape weight at which the effects start to come on. Objects and material swaps still switch at sourceThreshold.</summary>
        public float smoothStart;
        /// <summary>Smooth only: the shape weight at which the effects are fully on.</summary>
        public float smoothFull = 100f;

        /// <summary>
        /// smoothStart and smoothFull as the build uses them: both clamped to 0..100, a Full left at 0 (a field Unity
        /// skips the initialiser for on deserialise) read as 100, and Full kept at least one above Start. The one
        /// place the build, the preview and "What this builds" read the range, so none of them shows a range the
        /// others do not use.
        /// </summary>
        public float EffectiveSmoothStart => Mathf.Min(Mathf.Clamp(smoothStart, 0f, 100f), 99f);

        public float EffectiveSmoothFull
        {
            get
            {
                float full = smoothFull <= 0f ? 100f : Mathf.Clamp(smoothFull, 0f, 100f);
                return Mathf.Max(full, EffectiveSmoothStart + 1f);
            }
        }

        /// <summary>Whether the stored Start and Full cross, so the build has to move Full above Start. The model reports it.</summary>
        public bool SmoothRangeCrossed => linkStyle == BlendShapeLinkStyle.Smooth && smoothFull > 0f && Mathf.Clamp(smoothFull, 0f, 100f) <= EffectiveSmoothStart;

        // Trigger sources: Contact
        public Transform contactAnchor;
        public Vector3 contactOffset;
        public float contactRadius = 0.05f;
        public List<string> contactTags = new List<string> { "Finger", "Hand" };
        public bool contactAllowSelf = true;
        public bool contactAllowOthers = true;
        /// <summary>Zero means an ordinary touch (a Constant receiver); above zero is the "closer than" fraction of a Proximity receiver.</summary>
        public float contactProximity;
        /// <summary>Follow distance only: the receiver's Proximity value at which the effects are fully on. A per-entry setting, not a per-condition one.</summary>
        [Range(0.05f, 1f)] public float contactFullAt = 0.8f;

        /// <summary>
        /// contactFullAt as the build actually uses it: old data left at 0 (a field Unity skips the initialiser
        /// for on deserialise) reads as 0.8, and every value is clamped to 0.05..1. The one place both the build
        /// and "What this builds" compute this, so the panel never shows a value the build does not use.
        /// </summary>
        public float EffectiveContactFullAt => Mathf.Clamp(contactFullAt <= 0f ? 0.8f : contactFullAt, 0.05f, 1f);

        // Trigger sources: PhysBone
#if DOCME_VRCSDK_BASE
        public VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone physBone;
#else
        public Component physBone;
#endif
        public PhysBoneEvent physBoneEvent;
        public float physBoneThreshold = 0.5f;

        // Contact and PhysBone: hold effects, or flip another toggle
        public TouchMode touchMode;
        public string flipToggleId = "";

        // Trigger: several conditions
        /// <summary>The trigger is met when any group is met, and a group is met when all of its conditions are. Empty means the trigger works from its flat source fields above, which is how every scene saved before this existed loads.</summary>
        public List<ConditionGroup> when = new List<ConditionGroup>();

        // Trigger: menu switch gate
        /// <summary>The id of an AvatarDoctor toggle that must be in the chosen state for this trigger to work. Empty means always. On a blend shape trigger or a Fade contact it is one more requirement.</summary>
        public string gateToggleId = "";
        public bool gateToggleOn = true;

        // Targets
        public List<ObjectTarget> objects = new List<ObjectTarget>();
        public List<BlendShapeTarget> blendShapes = new List<BlendShapeTarget>();
        public List<MaterialTarget> materials = new List<MaterialTarget>();
        public List<MaterialPropertyTarget> properties = new List<MaterialPropertyTarget>();
        /// <summary>Custom animation clips merged into the build. Toggles and radials only; triggers, overrides and presets ignore this.</summary>
        public List<AnimationTarget> animations = new List<AnimationTarget>();
        /// <summary>While this entry is on (a toggle) or met (a trigger), each target follows its transform instead of hanging where it belongs. Toggles and triggers only.</summary>
        public List<FollowTarget> follows = new List<FollowTarget>();
        /// <summary>Meshes whose other shapes this entry holds, at 0 or at their default, while it is on. Toggles and triggers with effects of their own only (<see cref="OffersShapeSets"/>).</summary>
        public List<ShapeSetRule> shapeSets = new List<ShapeSetRule>();

        // Grabbable: the whole "hold this in a hand" arrangement in one entry.
        /// <summary>The object a creator can pick up. Grabbable only.</summary>
        public GameObject grabObject;
        /// <summary>Which hand or hands may hold it. Both builds one claim per hand.</summary>
        public GrabHands grabHands = GrabHands.Both;
        /// <summary>Off (the default) snaps the object onto the hand's own origin; on keeps the pose it already has relative to the hand, as posed in the scene.</summary>
        public bool grabKeepOffset;
        /// <summary>On by default: other players may grab it too, not only its own wearer.</summary>
        public bool grabAnyone = true;
        /// <summary>Seconds the object takes to blend back to its rest place once released.</summary>
        public float grabReturnSeconds = 0.15f;
        /// <summary>Multiplies the receiver radius the object's own bounds would otherwise give it: how close a hand has to be, beyond the computed default.</summary>
        public float grabReach = 1f;
        /// <summary>Off (the default): the entry is not a menu control, has no icon and takes no menu slot, like a hidden toggle. On: a normal toggle that allows grabbing at all, with a parameter and a menu slot like any toggle's.</summary>
        public bool grabMenuToggle;

        // Preset
        public List<PresetState> presetStates = new List<PresetState>();
        public List<PresetParameter> presetParameters = new List<PresetParameter>();

        public static MenuEntry Create(EntryKind kind, string name, string parentId = "")
        {
            return new MenuEntry
            {
                id = Guid.NewGuid().ToString("N"),
                kind = kind,
                name = name ?? "",
                parentId = parentId ?? "",
            };
        }

        /// <summary>
        /// A touch sourced trigger that flips another toggle instead of holding effects of its own. touchMode is
        /// kept when the source changes, so a stale FlipToggle on a trigger of any other source means nothing:
        /// the build, the preview, the wheel and the panel all ask here rather than reading touchMode themselves.
        /// </summary>
        public bool IsFlipTrigger => kind == EntryKind.Trigger && (source == TriggerSource.Contact || source == TriggerSource.PhysBone) && touchMode == TouchMode.FlipToggle;

        /// <summary>
        /// A Fade trigger: its effects blend in with how close the touching hand is (a Contact source in
        /// FollowDistance), or with the value of a Float parameter (a Parameter source in FollowValue,
        /// <see cref="IsFollowValueTrigger"/>), instead of switching fully on or off. True only when the first
        /// condition the trigger actually reads is of its source's own kind; a trigger whose data says Fade but
        /// fails any of that behaves and shows as Hold, the same as a stale FlipToggle on a trigger of another
        /// source. This is the one predicate the build and the inspector both ask, so they never disagree about
        /// which trigger this mode really applies to.
        /// </summary>
        public bool IsFollowDistanceTrigger
        {
            get
            {
                if (kind != EntryKind.Trigger) return false;
                WhenConditionKind driver;
                if (source == TriggerSource.Contact && touchMode == TouchMode.FollowDistance) driver = WhenConditionKind.Contact;
                else if (source == TriggerSource.Parameter && touchMode == TouchMode.FollowValue) driver = WhenConditionKind.Parameter;
                else return false;
                foreach (var (_, group) in EffectiveGroups())
                    foreach (var condition in group.all)
                        if (condition != null) return condition.kind == driver;
                return false;
            }
        }

        /// <summary>
        /// The parameter kind of Fade: a parameter sourced trigger in FollowValue mode whose first condition is a
        /// parameter, so its effects blend with that Float's value from 0 to Fully on at while every other condition
        /// of its card holds. Always also an IsFollowDistanceTrigger; only the driver differs from a Fade contact.
        /// </summary>
        public bool IsFollowValueTrigger => source == TriggerSource.Parameter && IsFollowDistanceTrigger;

        /// <summary>A trigger only works while its gate toggle is in the chosen state. Empty gateToggleId means always, so this is false.</summary>
        public bool HasGate => kind == EntryKind.Trigger && !string.IsNullOrEmpty(gateToggleId);

        /// <summary>
        /// A trigger whose effects one driver moves: a blend shape trigger (its shape, from the flat source fields)
        /// or a Fade contact (its first contact condition). Every other condition it reads is a requirement: while
        /// any requirement fails, the effects rest, and while all hold, the driver moves them. There is no "or" for
        /// these, since there is one driver.
        /// </summary>
        public bool HasDriver => kind == EntryKind.Trigger && (source == TriggerSource.BlendShape || IsFollowDistanceTrigger);

        /// <summary>The group a driven trigger keeps its requirements in: the first one holding a condition, the same group a Fade contact's driver sits first in. -1 while it has none.</summary>
        public int RequirementGroupIndex
        {
            get
            {
                if (!HasDriver || !UsesWhenGroups) return -1;
                for (int g = 0; g < when.Count; g++)
                    if (when[g] != null && when[g].all != null && when[g].all.Any(c => c != null)) return g;
                return -1;
            }
        }

        /// <summary>A driven trigger's requirements, each with the place it is stored at: every condition of its requirement group, after the driver on a Fade contact.</summary>
        public IEnumerable<(int groupIndex, int index, WhenCondition condition)> Requirements()
        {
            int g = RequirementGroupIndex;
            if (g < 0) yield break;
            var all = when[g].all;
            bool skipDriver = source != TriggerSource.BlendShape;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null) continue;
                if (skipDriver) { skipDriver = false; continue; }
                yield return (g, i, all[i]);
            }
        }

        /// <summary>How many conditions a driven trigger keeps but never reads: every one outside its requirement group, left over from a time it had several groups.</summary>
        public int IgnoredConditionCount
        {
            get
            {
                int g = RequirementGroupIndex;
                if (g < 0) return 0;
                int count = 0;
                for (int i = 0; i < when.Count; i++)
                    if (i != g && when[i] != null && when[i].all != null) count += when[i].all.Count(c => c != null);
                return count;
            }
        }

        /// <summary>Whether this trigger carries its conditions in groups rather than in the flat source fields. A trigger whose groups are all empty still counts: it says nothing, and that is reported, rather than falling back to fields the author has left behind.</summary>
        public bool UsesWhenGroups => kind == EntryKind.Trigger && when != null && when.Count > 0;

        /// <summary>
        /// The conditions this trigger is really built from: its groups while it has any, or one group holding the
        /// single condition its flat fields describe. Empty groups are left out. A blend shape trigger's shape is
        /// not a condition: what it reads besides its shape is its requirement group, or nothing.
        /// </summary>
        public IReadOnlyList<ConditionGroup> EffectiveWhen
        {
            get
            {
                if (kind != EntryKind.Trigger) return new List<ConditionGroup>();
                return EffectiveGroups().Select(g => g.group).ToList();
            }
        }

        /// <summary>The groups the build works from, each with the place it is stored at, so a message or a generated name can say which group and which condition it means. Groups with no conditions are left out; a blend shape trigger has at most its requirement group.</summary>
        public IEnumerable<(int groupIndex, ConditionGroup group)> EffectiveGroups()
        {
            if (kind != EntryKind.Trigger) yield break;
            if (source == TriggerSource.BlendShape)
            {
                int requirementGroup = RequirementGroupIndex;
                if (requirementGroup >= 0) yield return (requirementGroup, when[requirementGroup]);
                yield break;
            }
            if (!UsesWhenGroups)
            {
                yield return (0, new ConditionGroup { all = { WhenCondition.FromEntry(this) } });
                yield break;
            }
            for (int g = 0; g < when.Count; g++)
            {
                var group = when[g];
                if (group == null || group.all == null) continue;
                if (group.all.All(c => c == null)) continue;
                yield return (g, group);
            }
        }

        /// <summary>Every condition of the groups as they are stored, with the group and the position each one sits at. The flat fields are not included: this is what writes and validation messages name.</summary>
        public IEnumerable<(ConditionGroup group, WhenCondition condition, int groupIndex, int index)> StoredConditions()
        {
            if (!UsesWhenGroups) yield break;
            for (int g = 0; g < when.Count; g++)
            {
                var group = when[g];
                if (group == null || group.all == null) continue;
                for (int i = 0; i < group.all.Count; i++)
                    if (group.all[i] != null) yield return (group, group.all[i], g, i);
            }
        }

        /// <summary>A preset counts its states and named parameters instead of the object, blend shape and material targets it never has. Properties count only where <see cref="BuildsProperties"/> says they are built, and custom animations only on a toggle or a radial.</summary>
        public bool HasEffects
        {
            get
            {
                if (kind == EntryKind.Preset) return presetStates.Count > 0 || presetParameters.Count > 0;
                if (kind == EntryKind.Grabbable) return grabObject != null;
                if (IsFlipTrigger) return !string.IsNullOrWhiteSpace(flipToggleId);
                bool targeted = objects.Count > 0 || blendShapes.Count > 0 || materials.Count > 0 || follows.Count > 0 || HasActiveShapeSetRule;
                bool toggleOrRadial = kind == EntryKind.Toggle || kind == EntryKind.Radial;
                return targeted || (BuildsProperties && properties.Count > 0) || (toggleOrRadial && animations.Count > 0);
            }
        }

        /// <summary>
        /// Whether the build writes this entry's material property rows: a toggle, a radial, and a trigger that holds
        /// effects of its own (every source in Hold or Fade, a blend shape trigger included). A flip trigger does
        /// not. The model, the build, the preview and the panel all ask here.
        /// </summary>
        public bool BuildsProperties
        {
            get
            {
                if (kind == EntryKind.Toggle || kind == EntryKind.Radial) return true;
                return kind == EntryKind.Trigger && !IsFlipTrigger;
            }
        }

        /// <summary>
        /// A toggle or radial that only drives the parameter it names: it gets a menu control and a planned
        /// parameter, but no layer, since whatever reads that parameter (usually a merged animator) holds the
        /// effect. Asked here rather than read from the fields, so every part of the build agrees on what one is.
        /// A member of an exclusive group is never one: the group owns its members' parameter, so a custom name
        /// on a member means nothing and a member with no targets is the group's "off" choice as it always was.
        /// </summary>
        public bool IsParameterControl
        {
            get
            {
                if (kind != EntryKind.Toggle && kind != EntryKind.Radial) return false;
                if (string.IsNullOrWhiteSpace(parameterName)) return false;
                if (kind == EntryKind.Toggle && !string.IsNullOrWhiteSpace(exclusiveGroup)) return false;
                return objects.Count == 0 && blendShapes.Count == 0 && materials.Count == 0
                    && properties.Count == 0 && animations.Count == 0 && follows.Count == 0 && !HasActiveShapeSetRule;
            }
        }

        /// <summary>A rule that really holds something: it has a mesh and is switched on. An inert placeholder (switched off) counts nowhere.</summary>
        bool HasActiveShapeSetRule => shapeSets.Any(r => r != null && r.mesh != null && r.On);

        /// <summary>Whether this entry can hold the rest of a mesh (a shape set rule): a toggle, or a trigger with effects of its own. A radial has two ends, and a flip trigger has no effects. The model, the build and the panel all ask here.</summary>
        public bool OffersShapeSets => kind == EntryKind.Toggle || (kind == EntryKind.Trigger && !IsFlipTrigger);

        /// <summary>True when a blend shape target is held only while the control is on, or an active shape set rule does, either of which needs a pin layer of its own.</summary>
        public bool HasPins => blendShapes.Any(t => t.mode == ShapeEffectMode.Pin) || HasActiveShapeSetRule;

        /// <summary>Triggers and overrides are rules: they ignore parentId, take no menu slot and bind no parameter.</summary>
        public bool IsRule => kind == EntryKind.Trigger || kind == EntryKind.Override;

        /// <summary>
        /// True when this entry has no control in the built menu and takes no slot: an ordinary toggle or radial
        /// hidden from the menu, or a grabbable whose own menu toggle is off. Everywhere a hidden toggle is
        /// handled reads this instead of the raw field, so a grabbable follows the exact same path rather than a
        /// second one.
        /// </summary>
        public bool NoMenuControl => kind == EntryKind.Grabbable ? !grabMenuToggle : hiddenFromMenu;
    }
}
