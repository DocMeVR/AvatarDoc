using System;
using System.Collections.Generic;
using UnityEngine;

namespace DocMe.AvatarDoctor
{
    /// <summary>What one condition of a trigger's "when" reads. The same four sources a trigger has had all along, minus the blend shape, which is a trigger's driver rather than a condition.</summary>
    public enum WhenConditionKind { Toggle = 0, Parameter = 1, Contact = 2, PhysBone = 3 }

    /// <summary>
    /// One condition of a trigger. Its fields carry the same names as the flat single source fields of MenuEntry,
    /// so a panel row, a validator or a builder can take either one as a SerializedProperty and read the same
    /// relative paths. Only the fields of the chosen kind mean anything; the rest keep their defaults.
    /// </summary>
    [Serializable]
    public sealed class WhenCondition
    {
        public WhenConditionKind kind;

        // Toggle
        public string sourceToggleId = "";
        public bool sourceToggleOn = true;

        // Parameter
        public string sourceParameter = "";
        public ParameterCondition condition;
        public float conditionValue;

        // Contact
        public Transform contactAnchor;
        public Vector3 contactOffset;
        public float contactRadius = 0.05f;
        public List<string> contactTags = new List<string> { "Finger", "Hand" };
        public bool contactAllowSelf = true;
        public bool contactAllowOthers = true;
        /// <summary>Zero means an ordinary touch (a Constant receiver); above zero is the "closer than" fraction of a Proximity receiver.</summary>
        public float contactProximity;

        // PhysBone
#if DOCME_VRCSDK_BASE
        public VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone physBone;
#else
        public Component physBone;
#endif
        public PhysBoneEvent physBoneEvent;
        public float physBoneThreshold = 0.5f;

        /// <summary>A condition that reads the entry's own flat fields, for a trigger that has no groups yet. The build, the summary and the panel all work from conditions, so the flat fields reach them through one of these.</summary>
        public static WhenCondition FromEntry(MenuEntry entry)
        {
            var condition = new WhenCondition
            {
                sourceToggleId = entry.sourceToggleId,
                sourceToggleOn = entry.sourceToggleOn,
                sourceParameter = entry.sourceParameter,
                condition = entry.condition,
                conditionValue = entry.conditionValue,
                contactAnchor = entry.contactAnchor,
                contactOffset = entry.contactOffset,
                contactRadius = entry.contactRadius,
                contactTags = new List<string>(entry.contactTags ?? new List<string>()),
                contactAllowSelf = entry.contactAllowSelf,
                contactAllowOthers = entry.contactAllowOthers,
                contactProximity = entry.contactProximity,
                physBone = entry.physBone,
                physBoneEvent = entry.physBoneEvent,
                physBoneThreshold = entry.physBoneThreshold,
            };
            switch (entry.source)
            {
                case TriggerSource.Parameter: condition.kind = WhenConditionKind.Parameter; break;
                case TriggerSource.Contact: condition.kind = WhenConditionKind.Contact; break;
                case TriggerSource.PhysBone: condition.kind = WhenConditionKind.PhysBone; break;
                default: condition.kind = WhenConditionKind.Toggle; break;
            }
            return condition;
        }

        /// <summary>Writes this condition's fields back onto an entry's flat fields, including the source that matches its kind, so a trigger whose last group was removed reads as the condition the author last saw rather than as whatever it was before it had groups.</summary>
        public void CopyToEntry(MenuEntry entry)
        {
            entry.source = KindSource(kind);
            entry.sourceToggleId = sourceToggleId;
            entry.sourceToggleOn = sourceToggleOn;
            entry.sourceParameter = sourceParameter;
            entry.condition = condition;
            entry.conditionValue = conditionValue;
            entry.contactAnchor = contactAnchor;
            entry.contactOffset = contactOffset;
            entry.contactRadius = contactRadius;
            entry.contactTags = new List<string>(contactTags ?? new List<string>());
            entry.contactAllowSelf = contactAllowSelf;
            entry.contactAllowOthers = contactAllowOthers;
            entry.contactProximity = contactProximity;
            entry.physBone = physBone;
            entry.physBoneEvent = physBoneEvent;
            entry.physBoneThreshold = physBoneThreshold;
        }

        public static TriggerSource KindSource(WhenConditionKind kind)
        {
            switch (kind)
            {
                case WhenConditionKind.Parameter: return TriggerSource.Parameter;
                case WhenConditionKind.Contact: return TriggerSource.Contact;
                case WhenConditionKind.PhysBone: return TriggerSource.PhysBone;
                default: return TriggerSource.Toggle;
            }
        }

        /// <summary>Whether this condition makes a contact receiver or reads a PhysBone, the two kinds that need a component on the build clone.</summary>
        public bool IsTouch => kind == WhenConditionKind.Contact || kind == WhenConditionKind.PhysBone;
    }

    /// <summary>One card of a trigger's "when": every condition in it has to be met.</summary>
    [Serializable]
    public sealed class ConditionGroup
    {
        public List<WhenCondition> all = new List<WhenCondition>();
    }
}
