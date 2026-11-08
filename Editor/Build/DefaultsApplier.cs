#if DOCME_VRCSDK3A
using System.Collections.Generic;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Writes every control's default state into the scene objects, in tree order; later writers win. A shape a
    /// Write Defaults off layer holds (a Pin row, a shape set rule, a blend shape trigger's shapes) keeps its scene
    /// weight: at rest that layer weighs 0 only when its rests are the uploaded values, and a Write Defaults on layer
    /// that also moves the shape goes back to the uploaded value whenever it rests.
    /// </summary>
    public static class DefaultsApplier
    {
        public static void Apply(MenuTree tree, ParameterPlan plan, BaseState baseState, IReadOnlyList<TriggerPlan> triggers = null, FollowPlan followPlan = null)
        {
            var content = new ClipContent(tree.AvatarRoot);
            WriteControls(content, tree, plan, baseState, ShapeSet.MainOnly, followPlan);
            if (triggers != null)
            {
                // What the animator gives a trigger's source shape at load, pins included, for whether it starts on.
                ClipContent withPins = null;
                foreach (var trigger in triggers)
                {
                    // A trigger with requirements, or an Only while toggle, rests until the animator checks them,
                    // the same way a condition trigger leaves the scene at base.
                    if (trigger.Kind != TriggerKind.Link || trigger.Conditions.Count > 0 || trigger.Entry.HasGate) continue;
                    if (withPins == null)
                    {
                        withPins = new ClipContent(tree.AvatarRoot);
                        WriteControls(withPins, tree, plan, baseState, ShapeSet.All, followPlan);
                    }
                    var sourceWeight = withPins.TryGetShape(trigger.SourceRenderer, trigger.SourceShape, out var pending)
                        ? pending
                        : baseState.WeightOf(trigger.SourceRenderer, trigger.SourceShape);
                    content.WriteLinkDefault(trigger.Entry, sourceWeight, baseState, withShapes: false);
                    withPins.WriteLinkDefault(trigger.Entry, sourceWeight, baseState);
                }
            }
            foreach (var rule in tree.Overrides)
                if (!rule.Excluded) content.WriteOverride(rule.Entry);
            content.ApplyToScene();
        }

        static void WriteControls(ClipContent content, MenuTree tree, ParameterPlan plan, BaseState baseState, ShapeSet shapes, FollowPlan followPlan)
        {
            var builtGroups = new HashSet<PlannedParameter>();
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
                    if (builtGroups.Add(binding.Parameter)) WriteGroupDefault(content, plan, binding.Parameter, baseState, shapes);
                    continue;
                }
                content.WriteToggle(control.Entry, binding.OnByDefault, baseState, shapes, followPlan);
            }
        }

        /// <summary>The scene state of one exclusive group: every member off first, the member that is on by default last, the order AnimatorBuilder gives the group's own state. Member by member in tree order instead, a member that is off would write its own off value over a target it shares with the default member.</summary>
        static void WriteGroupDefault(ClipContent content, ParameterPlan plan, PlannedParameter group, BaseState baseState, ShapeSet shapes)
        {
            ControlNode onByDefault = null;
            foreach (var member in group.Controls)
            {
                if (member.Excluded) continue;
                if (plan.Bindings.TryGetValue(member, out var binding) && binding.OnByDefault) { onByDefault = member; continue; }
                content.WriteToggle(member.Entry, false, baseState, shapes);
            }
            if (onByDefault != null) content.WriteToggle(onByDefault.Entry, true, baseState, shapes);
        }
    }
}
#endif
