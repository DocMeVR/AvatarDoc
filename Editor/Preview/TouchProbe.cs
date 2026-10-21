#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>The body part the test hand stands for, which decides the contact tags it carries.</summary>
    public enum TouchTestPart { Hand = 0, Head = 1, Torso = 2, Foot = 3, Custom = 4 }

    /// <summary>What a touch trigger does with the hand: holds its effects, flips a toggle, or fades its effects in.</summary>
    public enum TouchTestMode { Hold = 0, Flip = 1, Fade = 2 }

    /// <summary>The test hand: where its centre is, which body part it stands for and whose body it belongs to.</summary>
    public sealed class TestHand
    {
        public Vector3 Position;
        public TouchTestPart Part = TouchTestPart.Hand;
        /// <summary>The tag the hand carries while Part is Custom, trimmed.</summary>
        public string CustomTag = "";
        /// <summary>The wearer's own body. False, the default, is another player's.</summary>
        public bool Self;
    }

    /// <summary>One contact sphere of a trigger, where the build's receiver sits, measured against the hand.</summary>
    public sealed class ProbedContact
    {
        public WhenCondition Condition;
        /// <summary>False for a contact the build makes no receiver for: no anchor, or one outside the avatar.</summary>
        public bool Built;
        public Vector3 Centre;
        public float Radius;
        /// <summary>The receiver's Proximity value, 0 at the edge or beyond and 1 at the centre; 0 while the hand's tags or owner do not count.</summary>
        public float Proximity;
        /// <summary>Whether this contact's own condition holds: inside it, closer than its fraction, or for a Fade's driver, anywhere above zero.</summary>
        public bool Touched;
    }

    /// <summary>What one touch trigger does with the hand where it is now.</summary>
    public sealed class ProbedTrigger
    {
        public TriggerNode Node;
        public MenuEntry Entry => Node.Entry;
        public TouchTestMode Mode;
        public readonly List<ProbedContact> Contacts = new List<ProbedContact>();
        /// <summary>Hold and Flip: a group the hand touches holds. Fade: the driver is touched and every requirement holds.</summary>
        public bool Met;
        public bool GateMet = true;
        /// <summary>Met and its gate met: the trigger reacts.</summary>
        public bool Active;
        /// <summary>The hand is at the trigger, but a requirement or its gate holds it back.</summary>
        public bool Blocked;
        /// <summary>Fade only: how far its effects are on, from 0 to 1.</summary>
        public float Amount;
        public bool Touched => Contacts.Exists(c => c.Touched);
        /// <summary>The hand is at the trigger: in the sphere that fades a Fade, in any sphere of a Hold or a Flip. The hand's label lists these.</summary>
        public bool Engaged;
    }

    /// <summary>
    /// Measures the avatar's contact touch triggers against the test hand the way VRChat measures a receiver: each
    /// receiver sits where the build puts it (TouchBuilder.CreateContact: a child of the anchor at the offset, the radius
    /// clamped, scaled by the anchor's largest axis), and a Proximity value runs from 0 at its edge to 1 at its centre,
    /// read at the hand's centre. A toggle condition or gate reads the scene preview; a parameter or a PhysBone
    /// condition counts as met, as everywhere else in the preview. A trigger with no contact among its conditions, a
    /// blend shape trigger and a parameter Fade are left out.
    /// </summary>
    public static class TouchProbe
    {
        static readonly string[] HandTags =
        {
            "Hand", "Finger", "HandL", "HandR", "FingerL", "FingerR", "FingerIndex", "FingerMiddle", "FingerRing", "FingerLittle",
            "FingerIndexL", "FingerIndexR", "FingerMiddleL", "FingerMiddleR", "FingerRingL", "FingerRingR", "FingerLittleL", "FingerLittleR",
        };
        static readonly string[] FootTags = { "Foot", "FootL", "FootR" };

        /// <summary>The contact tags the hand carries: a hand stands for both hands and every finger, a foot for both feet.</summary>
        public static IReadOnlyCollection<string> TagsOf(TouchTestPart part, string customTag)
        {
            switch (part)
            {
                case TouchTestPart.Head: return new[] { "Head" };
                case TouchTestPart.Torso: return new[] { "Torso" };
                case TouchTestPart.Foot: return FootTags;
                case TouchTestPart.Custom:
                    var tag = (customTag ?? "").Trim();
                    return tag.Length > 0 ? new[] { tag } : new string[0];
                default: return HandTags;
            }
        }

        /// <summary>How VRChat scales a contact on this transform: by the largest axis of its scale.</summary>
        public static float Scale(Transform transform)
        {
            if (transform == null) return 1f;
            var scale = transform.lossyScale;
            return Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        }

        /// <summary>Where the build's receiver for this contact sits in the world.</summary>
        public static Vector3 Centre(WhenCondition condition) => condition.contactAnchor.TransformPoint(condition.contactOffset);

        /// <summary>The build's receiver radius for this contact, in the world.</summary>
        public static float WorldRadius(WhenCondition condition) => Mathf.Clamp(condition.contactRadius, 0.005f, 1f) * Scale(condition.contactAnchor);

        /// <summary>A Proximity receiver's value for a point: 1 at the centre, 0 at the edge and beyond.</summary>
        public static float Proximity(Vector3 centre, float radius, Vector3 point)
        {
            if (radius <= 0f) return 0f;
            return Mathf.Clamp01(1f - Vector3.Distance(centre, point) / radius);
        }

        /// <summary>Whether this contact senses the hand at all: one of its tags is one the hand carries, and it lets in the hand's owner.</summary>
        public static bool Senses(WhenCondition condition, TestHand hand)
        {
            if (condition == null || hand == null) return false;
            if (hand.Self ? !condition.contactAllowSelf : !condition.contactAllowOthers) return false;
            if (condition.contactTags == null) return false;
            var carried = TagsOf(hand.Part, hand.CustomTag);
            return condition.contactTags.Any(t => t != null && carried.Contains(t.Trim()));
        }

        /// <summary>Whether a trigger is one the test hand sets off: a trigger with a contact among the conditions the build reads, whatever its When starts with, but not one a blend shape drives or a parameter fades.</summary>
        public static bool Simulates(MenuEntry entry)
        {
            if (entry == null || entry.kind != EntryKind.Trigger) return false;
            if (entry.source == TriggerSource.BlendShape || entry.IsFollowValueTrigger) return false;
            return entry.EffectiveGroups().Any(g => g.group.all.Any(c => c != null && c.kind == WhenConditionKind.Contact));
        }

        /// <summary>The triggers of this tree the build makes and the hand sets off.</summary>
        public static IEnumerable<TriggerNode> TouchTriggers(MenuTree tree)
        {
            if (tree == null) yield break;
            foreach (var node in tree.Triggers)
                if (!node.Excluded && node.Entry.HasEffects && Simulates(node.Entry)) yield return node;
        }

        public static bool HasTouchTriggers(MenuTree tree) => TouchTriggers(tree).Any();

        public static List<ProbedTrigger> Probe(MenuTree tree, ParameterPlan plan, TestHand hand)
        {
            var probed = new List<ProbedTrigger>();
            foreach (var node in TouchTriggers(tree))
                probed.Add(ProbeOne(tree, plan, node, hand));
            return probed;
        }

        static ProbedTrigger ProbeOne(MenuTree tree, ParameterPlan plan, TriggerNode node, TestHand hand)
        {
            var entry = node.Entry;
            var result = new ProbedTrigger
            {
                Node = node,
                Mode = entry.IsFlipTrigger ? TouchTestMode.Flip : entry.IsFollowDistanceTrigger ? TouchTestMode.Fade : TouchTestMode.Hold,
                GateMet = PreviewState.GateMet(entry, tree, plan),
            };
            if (result.Mode == TouchTestMode.Fade)
            {
                var driver = Measure(TouchBuilder.FollowDistancePrimary(entry).condition, hand, tree.AvatarRoot, fadeDriver: true);
                result.Contacts.Add(driver);
                bool requirements = true;
                foreach (var (_, _, condition) in entry.Requirements())
                    requirements &= Holds(condition, hand, tree, plan, result);
                result.Met = driver.Touched && requirements;
                result.Active = result.Met && result.GateMet;
                result.Amount = result.Active ? Mathf.Clamp01(driver.Proximity / entry.EffectiveContactFullAt) : 0f;
                result.Engaged = driver.Touched;
            }
            else
            {
                foreach (var (_, group) in entry.EffectiveGroups())
                {
                    bool hasContact = false, onlyToggles = true, all = true;
                    foreach (var condition in group.all)
                    {
                        if (condition == null) continue;
                        hasContact |= condition.kind == WhenConditionKind.Contact;
                        onlyToggles &= condition.kind == WhenConditionKind.Toggle;
                        all &= Holds(condition, hand, tree, plan, result);
                    }
                    // A group of toggles alone reads the preview, as the build reads the toggles; one that needs a
                    // parameter or a PhysBone and no contact would always count as met, so it is left out.
                    if ((hasContact || onlyToggles) && all) result.Met = true;
                }
                result.Active = result.Met && result.GateMet;
                result.Engaged = result.Touched;
            }
            result.Blocked = result.Engaged && !result.Active;
            return result;
        }

        /// <summary>Whether one condition holds with the hand where it is: a contact is measured, and kept on the result for drawing; a toggle reads the preview; a parameter or a PhysBone counts as met.</summary>
        static bool Holds(WhenCondition condition, TestHand hand, MenuTree tree, ParameterPlan plan, ProbedTrigger result)
        {
            switch (condition.kind)
            {
                case WhenConditionKind.Contact:
                    var contact = Measure(condition, hand, tree.AvatarRoot, fadeDriver: false);
                    result.Contacts.Add(contact);
                    return contact.Touched;
                case WhenConditionKind.Toggle:
                    return PreviewState.ToggleMet(condition.sourceToggleId, condition.sourceToggleOn, tree, plan);
                default:
                    return true;
            }
        }

        static ProbedContact Measure(WhenCondition condition, TestHand hand, Transform avatarRoot, bool fadeDriver)
        {
            var contact = new ProbedContact { Condition = condition };
            if (condition == null || condition.contactAnchor == null || MenuModel.IsOutsideAvatar(avatarRoot, condition.contactAnchor)) return contact;
            contact.Built = true;
            contact.Centre = Centre(condition);
            contact.Radius = WorldRadius(condition);
            if (!Senses(condition, hand)) return contact;
            contact.Proximity = Proximity(contact.Centre, contact.Radius, hand.Position);
            // A Fade's driver is always a Proximity receiver, entered above 0.001 (BuildFadeLayer); a Closer than
            // contact compares the same value with its own fraction; a plain touch is any overlap.
            if (fadeDriver) contact.Touched = contact.Proximity > 0.001f;
            else if (condition.contactProximity > 0f) contact.Touched = contact.Proximity > condition.contactProximity;
            else contact.Touched = Vector3.Distance(contact.Centre, hand.Position) < contact.Radius;
            return contact;
        }
    }
}
#endif
