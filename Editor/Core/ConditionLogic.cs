#if DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor.Animations;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The boolean core of a trigger's "when": negating one animator condition, turning a set of groups into the
    /// transitions that enter a state, and turning the same set into the transitions that leave it again. Pure
    /// throughout: a list of conditions in, a list of transitions out, no tree, no component and no animator.
    /// Every trigger source goes through here, so the flat single source, the groups, the gate and a flip layer
    /// all agree on what "met" and "no longer met" mean.
    /// </summary>
    public static class ConditionLogic
    {
        public const int MaxGroups = 4;
        public const int MaxPerGroup = 4;
        public const int MaxLeaveTransitions = 64;

        /// <summary>The step between two neighbouring values of an Int parameter. Not ( x &gt; v ) is x &lt; v + 1 there, since nothing sits between the two.</summary>
        public const float IntStep = 1f;

        /// <summary>
        /// The opposite of one condition. If and IfNot, Equals and NotEqual, and Greater and Less swap. On an Int
        /// the threshold moves by one, so the two halves meet without overlapping; on a Float it stays where it
        /// is, because a step would make a window in which entering and leaving are both true at once.
        /// </summary>
        public static AnimatorCondition Negate(AnimatorCondition c, bool isInt)
        {
            float step = isInt ? IntStep : 0f;
            switch (c.mode)
            {
                case AnimatorConditionMode.If: return Make(c.parameter, AnimatorConditionMode.IfNot, c.threshold);
                case AnimatorConditionMode.IfNot: return Make(c.parameter, AnimatorConditionMode.If, c.threshold);
                case AnimatorConditionMode.Equals: return Make(c.parameter, AnimatorConditionMode.NotEqual, c.threshold);
                case AnimatorConditionMode.NotEqual: return Make(c.parameter, AnimatorConditionMode.Equals, c.threshold);
                case AnimatorConditionMode.Greater: return Make(c.parameter, AnimatorConditionMode.Less, c.threshold + step);
                default: return Make(c.parameter, AnimatorConditionMode.Greater, c.threshold - step);
            }
        }

        /// <summary>
        /// The transitions into the trigger's On state: one per group, ANDed with each way the gate can be met.
        /// Both lists are in disjunctive form, one inner list per transition, and the conditions of one transition
        /// are ANDed. An empty gate leaves the groups as they are.
        /// </summary>
        public static List<List<AnimatorCondition>> Enter(IReadOnlyList<List<AnimatorCondition>> groups, IReadOnlyList<List<AnimatorCondition>> gate, Func<string, bool> isInt = null)
        {
            var result = new List<List<AnimatorCondition>>();
            if (groups == null) return result;
            foreach (var group in groups)
            {
                if (gate == null || gate.Count == 0) { result.Add(new List<AnimatorCondition>(group)); continue; }
                foreach (var alternative in gate)
                {
                    var merged = new List<AnimatorCondition>(group);
                    merged.AddRange(alternative);
                    result.Add(merged);
                }
            }
            return Clean(result, isInt);
        }

        /// <summary>
        /// The transitions out of the On state: the trigger is no longer met when every group has at least one
        /// failed condition, which is one transition per combination of one negated condition from each group,
        /// plus the same for the gate, since a closed gate alone is enough to leave. Combinations that can never
        /// fire and repeats of a combination already in the list are dropped.
        /// </summary>
        public static List<List<AnimatorCondition>> Leave(IReadOnlyList<List<AnimatorCondition>> groups, IReadOnlyList<List<AnimatorCondition>> gate, Func<string, bool> isInt = null)
        {
            var result = Opposite(groups, isInt, out bool abandoned);
            var gateSide = Opposite(gate, isInt, out bool gateAbandoned);
            // Half a negation would let the trigger leave on a rule it does not have; the callers refuse a trigger
            // this large before they ever get here, so nothing is lost by handing back nothing.
            if (abandoned || gateAbandoned) return new List<List<AnimatorCondition>>();
            result.AddRange(gateSide);
            return Clean(result, isInt);
        }

        /// <summary>
        /// The opposite of a whole set of alternatives: one transition per combination of one negated condition
        /// from each of them. Nothing to negate means nothing to leave by, so an empty set gives an empty list
        /// rather than a transition with no conditions, which would fire at once.
        /// </summary>
        public static List<List<AnimatorCondition>> Opposite(IReadOnlyList<List<AnimatorCondition>> alternatives, Func<string, bool> isInt = null)
        {
            return Opposite(alternatives, isInt, out _);
        }

        /// <param name="tooComplex">True when the product passed MaxLeaveTransitions and the work was abandoned, so the result is empty. The caller has already refused such a trigger; this is the backstop that keeps any caller from walking a product that would hang the editor.</param>
        public static List<List<AnimatorCondition>> Opposite(IReadOnlyList<List<AnimatorCondition>> alternatives, Func<string, bool> isInt, out bool tooComplex)
        {
            tooComplex = false;
            var result = new List<List<AnimatorCondition>>();
            if (alternatives == null || alternatives.Count == 0) return result;
            result.Add(new List<AnimatorCondition>());
            foreach (var alternative in alternatives)
            {
                if (result.Count * (long)alternative.Count > MaxLeaveTransitions)
                {
                    tooComplex = true;
                    return new List<List<AnimatorCondition>>();
                }
                var next = new List<List<AnimatorCondition>>();
                foreach (var combination in result)
                    foreach (var condition in alternative)
                    {
                        var merged = new List<AnimatorCondition>(combination) { Negate(condition, isInt != null && isInt(condition.parameter)) };
                        next.Add(merged);
                    }
                result = next;
                if (result.Count == 0) return result;
            }
            return result;
        }

        /// <summary>
        /// Drops transitions that can never fire, transitions that repeat one already in the list and a condition
        /// repeated inside one transition. A transition that asks for everything another one already asks for and
        /// more is dropped too: the shorter one fires first in every case the longer one would, and both count
        /// against what the layer may hold.
        /// </summary>
        static List<List<AnimatorCondition>> Clean(List<List<AnimatorCondition>> transitions, Func<string, bool> isInt)
        {
            var kept = new List<List<AnimatorCondition>>();
            var seen = new HashSet<string>();
            foreach (var transition in transitions)
            {
                var conditions = new List<AnimatorCondition>();
                foreach (var condition in transition)
                    if (!conditions.Any(c => Same(c, condition))) conditions.Add(condition);
                if (Impossible(conditions, isInt)) continue;
                if (!seen.Add(Key(conditions))) continue;
                kept.Add(conditions);
            }
            return kept.Where(t => !kept.Any(other => other.Count < t.Count && Covers(other, t))).ToList();
        }

        /// <summary>Whether every condition of the shorter transition is in the longer one, so the longer one only ever fires where the shorter one already has.</summary>
        static bool Covers(List<AnimatorCondition> shorter, List<AnimatorCondition> longer)
        {
            return shorter.All(c => longer.Any(other => Same(c, other)));
        }

        /// <summary>Whether these conditions contradict each other, so the transition could never fire: a parameter wanted true and false at once, a value wanted equal to and different from the same number, or a window with nothing in it.</summary>
        static bool Impossible(IReadOnlyList<AnimatorCondition> conditions, Func<string, bool> isInt)
        {
            for (int i = 0; i < conditions.Count; i++)
                for (int j = i + 1; j < conditions.Count; j++)
                {
                    var a = conditions[i];
                    var b = conditions[j];
                    if (a.parameter != b.parameter) continue;
                    if (Pair(a, b, AnimatorConditionMode.If, AnimatorConditionMode.IfNot)) return true;
                    if (Pair(a, b, AnimatorConditionMode.Equals, AnimatorConditionMode.NotEqual) && a.threshold == b.threshold) return true;
                    if (a.mode == AnimatorConditionMode.Equals && b.mode == AnimatorConditionMode.Equals && a.threshold != b.threshold) return true;
                    float step = isInt != null && isInt(a.parameter) ? IntStep : 0f;
                    if (a.mode == AnimatorConditionMode.Greater && b.mode == AnimatorConditionMode.Less && b.threshold <= a.threshold + step) return true;
                    if (b.mode == AnimatorConditionMode.Greater && a.mode == AnimatorConditionMode.Less && a.threshold <= b.threshold + step) return true;
                }
            return false;
        }

        static bool Pair(AnimatorCondition a, AnimatorCondition b, AnimatorConditionMode first, AnimatorConditionMode second)
        {
            return (a.mode == first && b.mode == second) || (a.mode == second && b.mode == first);
        }

        static bool Same(AnimatorCondition a, AnimatorCondition b)
        {
            return a.parameter == b.parameter && a.mode == b.mode && a.threshold == b.threshold;
        }

        static string Key(IEnumerable<AnimatorCondition> conditions)
        {
            return string.Join("&", conditions.Select(c => c.parameter + ":" + (int)c.mode + ":" + c.threshold.ToString("R", CultureInfo.InvariantCulture)).OrderBy(s => s));
        }

        static AnimatorCondition Make(string parameter, AnimatorConditionMode mode, float threshold)
        {
            return new AnimatorCondition { parameter = parameter, mode = mode, threshold = threshold };
        }

        /// <summary>
        /// How many transitions leaving the On state a set of already expanded alternatives needs: one per
        /// combination of a failed condition from each of them. Counted before any of them is built, and capped at
        /// int.MaxValue so a rule nobody could mean still names a number instead of overflowing.
        /// </summary>
        public static int LeaveEstimate(IReadOnlyList<List<AnimatorCondition>> alternatives)
        {
            if (alternatives == null || alternatives.Count == 0) return 0;
            long count = 1;
            foreach (var alternative in alternatives)
            {
                count *= Math.Max(1, alternative.Count);
                if (count > int.MaxValue) return int.MaxValue;
            }
            return (int)count;
        }

        /// <summary>How many transitions leaving the On state the groups need when each condition has one opposite, which is every condition but a comparison against a single float value. The inspector and the model count this way before anything is resolved; the planner counts the expanded alternatives instead.</summary>
        public static int LeaveEstimate(IReadOnlyList<ConditionGroup> groups)
        {
            if (groups == null || groups.Count == 0) return 0;
            long count = 1;
            foreach (var group in groups)
            {
                count *= Math.Max(1, group.all.Count);
                if (count > int.MaxValue) return int.MaxValue;
            }
            return (int)count;
        }

        /// <summary>Whether a set of groups is past one of the three caps. leaveCount is what LeaveEstimate makes of it, so the panel and the model can say the same thing before anything is built.</summary>
        public static bool TooComplex(IReadOnlyList<ConditionGroup> groups, out int groupCount, out int leaveCount)
        {
            groupCount = groups != null ? groups.Count : 0;
            leaveCount = LeaveEstimate(groups);
            if (groupCount > MaxGroups) return true;
            if (groups != null && groups.Any(g => g.all.Count > MaxPerGroup)) return true;
            return leaveCount > MaxLeaveTransitions;
        }

        /// <summary>The whole rule in words, as the panel shows it above the cards and the summary prints it.</summary>
        public static string Sentence(MenuEntry trigger, MenuTree tree)
        {
            if (trigger == null || trigger.kind != EntryKind.Trigger) return "";
            string rule;
            if (trigger.HasDriver)
            {
                // One driver, then its requirements: what the driver does, and everything that must also hold.
                var parts = new List<string> { DriverWords(trigger) };
                parts.AddRange(trigger.Requirements().Select(r => Words(r.condition, tree)));
                rule = string.Format(L.Text("ad.ui.when.sentence"), string.Join(L.Text("ad.ui.when.and"), parts));
            }
            else
            {
                var groups = trigger.EffectiveWhen;
                if (groups.Count == 0) return L.Text("ad.ui.when.nothing");

                var sb = new StringBuilder();
                for (int g = 0; g < groups.Count; g++)
                {
                    if (g > 0) sb.Append(L.Text("ad.ui.when.or"));
                    var parts = groups[g].all.Select(c => Words(c, tree)).ToList();
                    if (parts.Count > 1) sb.Append(L.Brackets(string.Join(L.Text("ad.ui.when.and"), parts)));
                    else sb.Append(parts[0]);
                }
                rule = string.Format(L.Text("ad.ui.when.sentence"), sb.ToString());
            }
            if (!trigger.HasGate) return rule;
            var gate = tree != null ? tree.FindControl(trigger.gateToggleId) : null;
            var gateName = gate != null ? gate.Entry.PlainName : L.Text("ad.ui.when.missing");
            return string.Format(L.Text("ad.ui.when.with_gate"), rule,
                string.Format(L.Text(trigger.gateToggleOn ? "ad.ui.when.toggle_on" : "ad.ui.when.toggle_off"), gateName));
        }

        /// <summary>What drives a blend shape trigger or a Fade trigger, in words: its shape moving, a hand coming near its contact, or its parameter's value.</summary>
        static string DriverWords(MenuEntry trigger)
        {
            if (trigger.source == TriggerSource.BlendShape)
            {
                var renderer = trigger.sourceRenderer != null ? trigger.sourceRenderer.name : L.Text("ad.ui.when.missing");
                var shape = string.IsNullOrEmpty(trigger.sourceShape) ? L.Text("ad.ui.when.missing") : trigger.sourceShape;
                return string.Format(L.Text("ad.ui.when.shape_moves"), renderer + "/" + shape);
            }
            var driver = trigger.EffectiveGroups().SelectMany(g => g.group.all).FirstOrDefault(c => c != null);
            if (trigger.IsFollowValueTrigger)
            {
                var parameter = (driver?.sourceParameter ?? "").Trim();
                return string.Format(L.Text("ad.ui.when.value_moves"), parameter.Length > 0 ? parameter : L.Text("ad.ui.when.missing"));
            }
            var anchor = driver != null && driver.contactAnchor != null ? driver.contactAnchor.name : L.Text("ad.ui.when.missing");
            return string.Format(L.Text("ad.ui.when.hand_near"), anchor);
        }

        /// <summary>One condition in words, such as "Hat is on" or "GestureLeft equals 3".</summary>
        public static string Words(WhenCondition condition, MenuTree tree)
        {
            if (condition == null) return L.Text("ad.ui.when.missing");
            switch (condition.kind)
            {
                case WhenConditionKind.Toggle:
                {
                    var control = tree != null ? tree.FindControl(condition.sourceToggleId) : null;
                    var name = control != null ? control.Entry.PlainName : L.Text("ad.ui.when.missing");
                    return string.Format(L.Text(condition.sourceToggleOn ? "ad.ui.when.toggle_on" : "ad.ui.when.toggle_off"), name);
                }
                case WhenConditionKind.Parameter:
                {
                    var name = (condition.sourceParameter ?? "").Trim();
                    if (name.Length == 0) name = L.Text("ad.ui.when.missing");
                    return string.Format(L.Text(ComparisonKey(condition.condition)), name, Number(condition.conditionValue));
                }
                case WhenConditionKind.Contact:
                {
                    var name = condition.contactAnchor != null ? condition.contactAnchor.name : L.Text("ad.ui.when.missing");
                    return string.Format(L.Text(condition.contactProximity > 0f ? "ad.ui.when.contact_near" : "ad.ui.when.contact"), name, Number(condition.contactProximity));
                }
                default:
                {
                    var name = condition.physBone != null ? condition.physBone.name : L.Text("ad.ui.when.missing");
                    return string.Format(L.Text(EventKey(condition.physBoneEvent)), name, Number(condition.physBoneThreshold));
                }
            }
        }

        static string ComparisonKey(ParameterCondition comparison)
        {
            switch (comparison)
            {
                case ParameterCondition.IsTrue: return "ad.ui.when.is_true";
                case ParameterCondition.IsFalse: return "ad.ui.when.is_false";
                case ParameterCondition.NotEquals: return "ad.ui.when.not_equals";
                case ParameterCondition.Greater: return "ad.ui.when.greater";
                case ParameterCondition.Less: return "ad.ui.when.less";
                default: return "ad.ui.when.equals";
            }
        }

        static string EventKey(PhysBoneEvent physBoneEvent)
        {
            switch (physBoneEvent)
            {
                case PhysBoneEvent.Posed: return "ad.ui.when.pb_posed";
                case PhysBoneEvent.AngleAbove: return "ad.ui.when.pb_angle";
                case PhysBoneEvent.StretchAbove: return "ad.ui.when.pb_stretch";
                case PhysBoneEvent.SquishAbove: return "ad.ui.when.pb_squish";
                default: return "ad.ui.when.pb_grabbed";
            }
        }

        static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>Where a condition sits, for a message that names it: the group and the place inside it, both counted from one.</summary>
        public static string Position(int groupIndex, int index)
        {
            return string.Format(L.Text("ad.ui.when.position"), groupIndex + 1, index + 1);
        }
    }
}
#endif
