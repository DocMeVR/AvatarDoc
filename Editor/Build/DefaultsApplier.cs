#if DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Writes every control's default state into the scene objects, in tree order; later writers win. A shape a Write
    /// Defaults off layer holds (a Pin row, a shape set rule, a blend shape trigger's shapes) gets its rest here, the
    /// value the animator falls back to, which Modular Avatar and VRCFury read from the scene as the rest of what they
    /// change themselves. Apply returns each such shape whose default look differs, and the final pass writes that
    /// look into the avatar together with the rest layer (RestLayerBuilder), so a player who sees the avatar with
    /// animations off sees what everyone else sees at load. A shape VRChat blinks or speaks with is not returned: VRChat
    /// stops moving a shape any layer writes, so no rest layer may hold it.
    /// </summary>
    public static class DefaultsApplier
    {
        const float Tolerance = 0.0001f;

        /// <summary>
        /// One blend shape whose default look is not the value the animator rests it at: a shape that a toggle on by
        /// default holds through a Write Defaults off layer (a Pin row, a shape set rule, a blend shape trigger's
        /// shape). Apply leaves Rest in the scene, which Modular Avatar and VRCFury read as the rest of what they
        /// change themselves; the final pass writes Rest from the rest layer on every frame and Look into the avatar
        /// (RestLayerBuilder).
        /// </summary>
        public sealed class UploadRest
        {
            public SkinnedMeshRenderer Renderer;
            public string Shape;
            public float Rest;
            public float Look;
        }

        public static List<UploadRest> Apply(MenuTree tree, ParameterPlan plan, BaseState baseState, IReadOnlyList<TriggerPlan> triggers = null, FollowPlan followPlan = null)
        {
            // What the animator rests at: everything but the shapes a Write Defaults off layer holds.
            var resting = new ClipContent(tree.AvatarRoot);
            WriteControls(resting, tree, plan, baseState, ShapeSet.MainOnly, followPlan);
            // What it shows at load: the pin layers come after every other layer and win while they are on, so their
            // shapes are written after every ordinary row, in the order the layers are built.
            var look = new ClipContent(tree.AvatarRoot);
            WriteControls(look, tree, plan, baseState, ShapeSet.MainOnly, followPlan);
            WriteControls(look, tree, plan, baseState, ShapeSet.PinsOnly, followPlan);
            if (triggers != null)
            {
                foreach (var trigger in triggers)
                {
                    // A trigger with requirements, or an Only while toggle, rests until the animator checks them,
                    // the same way a condition trigger leaves the scene at base.
                    if (trigger.Kind != TriggerKind.Link || trigger.Conditions.Count > 0 || trigger.Entry.HasGate) continue;
                    // What the animator gives a trigger's source shape at load, pins included, for whether it starts on.
                    var sourceWeight = look.TryGetShape(trigger.SourceRenderer, trigger.SourceShape, out var pending)
                        ? pending
                        : baseState.WeightOf(trigger.SourceRenderer, trigger.SourceShape);
                    resting.WriteLinkDefault(trigger.Entry, sourceWeight, baseState, withShapes: false);
                    look.WriteLinkDefault(trigger.Entry, sourceWeight, baseState);
                }
            }
            foreach (var rule in tree.Overrides)
            {
                if (rule.Excluded) continue;
                resting.WriteOverride(rule.Entry);
                look.WriteOverride(rule.Entry);
            }

            var (eyelids, mouth) = ShapeSetModel.TrackedShapes(tree.AvatarRoot != null ? tree.AvatarRoot.GetComponent<VRCAvatarDescriptor>() : null);
            var rests = new List<UploadRest>();
            foreach (var (renderer, shape, weight) in look.ShapeWrites())
            {
                if (eyelids.Contains((renderer, shape)) || mouth.Contains((renderer, shape))) continue;
                float rest = resting.TryGetShape(renderer, shape, out var restWeight) ? restWeight : baseState.WeightOf(renderer, shape);
                if (Mathf.Abs(weight - rest) <= Tolerance) continue;
                rests.Add(new UploadRest { Renderer = renderer, Shape = shape, Rest = rest, Look = weight });
            }
            resting.ApplyToScene();
            return rests;
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
                if (control.Entry.kind == EntryKind.Radial)
                {
                    // A radial holds nothing: its value is written with the ordinary rows only.
                    if (shapes != ShapeSet.PinsOnly) content.WriteRadial(control.Entry, binding.DefaultValue);
                    continue;
                }
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
