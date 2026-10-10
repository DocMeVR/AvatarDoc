#if DOCME_VRCSDK3A
using System.Collections.Generic;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Writes every control's default state into the scene objects, in tree order; later writers win.</summary>
    public static class DefaultsApplier
    {
        public static void Apply(MenuTree tree, ParameterPlan plan, BaseState baseState, IReadOnlyList<TriggerPlan> triggers = null, FollowPlan followPlan = null)
        {
            var content = new ClipContent(tree.AvatarRoot);
            var builtGroups = new HashSet<string>();
            foreach (var control in tree.Controls)
            {
                // A preset has no scene state of its own to default to; it only acts when pressed.
                if (control.Entry.kind == EntryKind.Preset) continue;
                // Nothing on this platform drives an excluded control, so the scene keeps whatever it already shows.
                if (control.Excluded) continue;
                // A parameter control has no scene state of its own: what its parameter drives lives elsewhere.
                if (control.Entry.IsParameterControl) continue;
                if (!plan.Bindings.TryGetValue(control, out var binding)) continue;
                if (control.Entry.kind == EntryKind.Radial) { content.WriteRadial(control.Entry, binding.DefaultValue); continue; }
                if (binding.Parameter.GroupName != null)
                {
                    if (builtGroups.Add(binding.Parameter.GroupName)) WriteGroupDefault(content, plan, binding.Parameter, baseState);
                    continue;
                }
                content.WriteToggle(control.Entry, binding.OnByDefault, baseState, ShapeSet.All, followPlan);
            }
            if (triggers != null)
                foreach (var trigger in triggers)
                {
                    // A trigger with requirements, or an Only while toggle, rests until the animator checks them,
                    // the same way a condition trigger leaves the scene at base.
                    if (trigger.Kind != TriggerKind.Link || trigger.Conditions.Count > 0 || trigger.Entry.HasGate) continue;
                    var sourceWeight = content.TryGetShape(trigger.SourceRenderer, trigger.SourceShape, out var pending)
                        ? pending
                        : baseState.WeightOf(trigger.SourceRenderer, trigger.SourceShape);
                    content.WriteLinkDefault(trigger.Entry, sourceWeight, baseState);
                }
            foreach (var rule in tree.Overrides)
                if (!rule.Excluded) content.WriteOverride(rule.Entry);
            content.ApplyToScene();
        }

        /// <summary>The scene state of one exclusive group: every member off first, the member that is on by default last, the order AnimatorBuilder gives the group's own state. Member by member in tree order instead, a member that is off would write its own off value over a target it shares with the default member.</summary>
        static void WriteGroupDefault(ClipContent content, ParameterPlan plan, PlannedParameter group, BaseState baseState)
        {
            ControlNode onByDefault = null;
            foreach (var member in group.Controls)
            {
                if (member.Excluded) continue;
                if (plan.Bindings.TryGetValue(member, out var binding) && binding.OnByDefault) { onByDefault = member; continue; }
                content.WriteToggle(member.Entry, false, baseState);
            }
            if (onByDefault != null) content.WriteToggle(onByDefault.Entry, true, baseState);
        }
    }
}
#endif
