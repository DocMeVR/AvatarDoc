#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>How the preview reads the scene. The render filter routes every read through NDMF's ComputeContext; tests read directly.</summary>
    public sealed class SceneReader
    {
        public Func<GameObject, bool> ActiveSelf;
        public Func<Renderer, bool> RendererEnabled;
        public Func<SkinnedMeshRenderer, int, float> ShapeWeight;
        public Func<Renderer, Material[]> Materials;
        public Func<Transform, IEnumerable<Renderer>> Renderers;
        public Func<Transform, Vector3> WorldPosition;
        public Func<Transform, Quaternion> WorldRotation;

        public static SceneReader Direct => new SceneReader
        {
            ActiveSelf = go => go.activeSelf,
            RendererEnabled = r => r.enabled,
            ShapeWeight = (r, i) => r.GetBlendShapeWeight(i),
            Materials = r => r.sharedMaterials,
            Renderers = t => t.GetComponentsInChildren<Renderer>(true),
            WorldPosition = t => t.position,
            WorldRotation = t => t.rotation,
        };
    }

    /// <summary>A property target's value at one point in time, packed the way EffectMath packs it: float in x, colour in rgba, vector in xyzw.</summary>
    public readonly struct PropertyValueEntry
    {
        public readonly PropertyValueType Type;
        public readonly Vector4 Value;

        public PropertyValueEntry(PropertyValueType type, Vector4 value)
        {
            Type = type;
            Value = value;
        }
    }

    /// <summary>What the preview writes onto one proxy renderer. Value equality keeps NDMF nodes alive while nothing changes.</summary>
    public sealed class RendererState : IEquatable<RendererState>
    {
        public bool Visible = true;
        public ImmutableSortedDictionary<int, float> Shapes = ImmutableSortedDictionary<int, float>.Empty;
        public ImmutableSortedDictionary<int, Material> Materials = ImmutableSortedDictionary<int, Material>.Empty;
        public ImmutableSortedDictionary<string, PropertyValueEntry> Properties = ImmutableSortedDictionary<string, PropertyValueEntry>.Empty;
        /// <summary>Set when this renderer's own object is being previewed as following another transform. The renderer is drawn at FollowPosition/FollowRotation instead of its own.</summary>
        public bool HasFollow;
        public Vector3 FollowPosition;
        public Quaternion FollowRotation = Quaternion.identity;

        public bool Equals(RendererState other)
        {
            if (other == null) return false;
            if (Visible != other.Visible || Shapes.Count != other.Shapes.Count || Materials.Count != other.Materials.Count || Properties.Count != other.Properties.Count) return false;
            if (HasFollow != other.HasFollow) return false;
            if (HasFollow && (FollowPosition != other.FollowPosition || FollowRotation != other.FollowRotation)) return false;
            foreach (var pair in Shapes)
                if (!other.Shapes.TryGetValue(pair.Key, out var weight) || !Mathf.Approximately(weight, pair.Value)) return false;
            foreach (var pair in Materials)
                if (!other.Materials.TryGetValue(pair.Key, out var material) || material != pair.Value) return false;
            foreach (var pair in Properties)
                if (!other.Properties.TryGetValue(pair.Key, out var value) || value.Type != pair.Value.Type || value.Value != pair.Value.Value) return false;
            return true;
        }

        public override bool Equals(object obj) => Equals(obj as RendererState);

        // Only what Equals compares exactly: a rounded weight would let two states that are approximately equal hash apart.
        public override int GetHashCode()
        {
            int hash = Visible ? 17 : 19;
            hash = hash * 31 + Shapes.Count;
            hash = hash * 31 + Materials.Count;
            hash = hash * 31 + Properties.Count;
            return hash * 31 + (HasFollow ? 1 : 0);
        }
    }

    /// <summary>The effective state of every renderer under an avatar for the current overrides, computed the way the build would animate it.</summary>
    public static class PreviewModel
    {
        /// <summary>An override wins; otherwise a control follows the default of the parameter it is bound to, a link the source shape, and a condition trigger stays idle. The binding is null for triggers and for a control the plan did not bind.</summary>
        public static float EffectiveValue(MenuEntry entry, ControlBinding binding, IReadOnlyDictionary<string, float> overrides, SceneReader scene)
        {
            if (overrides != null && overrides.TryGetValue(entry.id, out var value)) return value;
            switch (entry.kind)
            {
                case EntryKind.Toggle: return (binding != null ? binding.OnByDefault : entry.defaultOn) ? 1f : 0f;
                case EntryKind.Radial: return Mathf.Clamp01(binding != null ? binding.DefaultValue : entry.defaultValue);
                case EntryKind.Trigger:
                    if (entry.source != TriggerSource.BlendShape || !MenuModel.ShapeExists(entry.sourceRenderer, entry.sourceShape)) return 0f;
                    return EffectMath.LinkAbove(SourceWeight(entry, scene), entry.sourceThreshold) ? 1f : 0f;
            }
            return 0f;
        }

        /// <summary>The weight a link reads from its source: what a control already wrote to that shape in this pass, else the scene. The build reads it the same way, through ClipContent.TryGetShape.</summary>
        static float SourceWeight(MenuEntry entry, SceneReader scene, IReadOnlyDictionary<SkinnedMeshRenderer, Dictionary<int, float>> pending = null)
        {
            int index = entry.sourceRenderer.sharedMesh.GetBlendShapeIndex(entry.sourceShape);
            if (pending != null && pending.TryGetValue(entry.sourceRenderer, out var map) && map.TryGetValue(index, out var written)) return written;
            return scene.ShapeWeight(entry.sourceRenderer, index);
        }

        /// <summary>How often a Compute call had to build the tree and the parameter plan instead of reusing the cached one. Read by the tests and the performance probe.</summary>
        public static int PlanBuilds => AvatarPlanCache.PreviewPlanBuilds;

        /// <param name="pieces">The AvatarDoc Shapes components to apply. Null reads them off the avatar; the render filter passes its own observed list.</param>
        public static Dictionary<Renderer, RendererState> Compute(Transform avatarRoot, IReadOnlyList<AvatarDoc> components,
            IReadOnlyDictionary<string, float> overrides, SceneReader scene, IReadOnlyList<AvatarDocShapes> pieces = null)
        {
            var result = new Dictionary<Renderer, RendererState>();
            if (avatarRoot == null) return result;
            // The default state of a control is the default of the parameter it ends up bound to, which a custom name
            // can hand to an existing declaration. Planned from the assets rather than through ParameterInfo, which
            // would re-enter the preview, and kept in the plan cache so an unchanged avatar plans once.
            var cached = AvatarPlanCache.PreviewPlanFor(avatarRoot, components);
            var tree = cached.Tree;
            var plan = cached.Plan;
            var objects = new Dictionary<GameObject, bool>();
            var shapes = new Dictionary<SkinnedMeshRenderer, Dictionary<int, float>>();
            var materials = new Dictionary<Renderer, Dictionary<int, Material>>();
            var properties = new Dictionary<Renderer, Dictionary<string, PropertyValueEntry>>();
            var follows = new Dictionary<GameObject, FollowTarget>();

            void WriteObject(ObjectTarget target, bool on) => WriteObjectActive(target, EffectMath.ObjectActive(target, on));
            void WriteObjectActive(ObjectTarget target, bool active)
            {
                if (target.target == null || MenuModel.IsOutsideAvatar(avatarRoot, target.target.transform)) return;
                objects[target.target] = active;
            }
            // Off means the object goes back where it belongs, so there is nothing here to preview; only a claim
            // while on is recorded, last one in tree order winning the same way a shared object target already does.
            void WriteFollow(FollowTarget target, bool on)
            {
                if (!on || target.target == null || target.follows == null) return;
                if (MenuModel.IsOutsideAvatar(avatarRoot, target.target.transform) || MenuModel.IsOutsideAvatar(avatarRoot, target.follows)) return;
                follows[target.target] = target;
            }
            float BaseWeight(BlendShapeTarget target) => scene.ShapeWeight(target.renderer, target.renderer.sharedMesh.GetBlendShapeIndex(target.shape));
            void WriteShape(BlendShapeTarget target, float weight)
            {
                if (!MenuModel.ShapeExists(target.renderer, target.shape) || MenuModel.IsOutsideAvatar(avatarRoot, target.renderer.transform)) return;
                if (!shapes.TryGetValue(target.renderer, out var map)) shapes[target.renderer] = map = new Dictionary<int, float>();
                map[target.renderer.sharedMesh.GetBlendShapeIndex(target.shape)] = weight;
            }
            Material BaseMaterial(MaterialTarget target)
            {
                var current = scene.Materials(target.renderer);
                return target.slot < current.Length ? current[target.slot] : null;
            }
            void WriteMaterial(MaterialTarget target, bool on)
            {
                if (!MenuModel.SlotExists(target) || MenuModel.IsOutsideAvatar(avatarRoot, target.renderer.transform)) return;
                if (!materials.TryGetValue(target.renderer, out var map)) materials[target.renderer] = map = new Dictionary<int, Material>();
                map[target.slot] = EffectMath.ToggleMaterial(target, on, BaseMaterial(target));
            }
            Vector4 BaseProperty(MaterialPropertyTarget target, MenuEntry entry) => MenuModel.ReadProperty(MenuModel.MaterialWithProperty(target.renderer, target.property, entry), target.property, target.type);
            // The properties only a resting trigger wrote. A resting trigger writes its rest once, from its Reset
            // state at load, so a layer above that rests the same property too writes its own rest over it.
            var restedOnly = new HashSet<(Renderer renderer, string property)>();
            void WriteProperty(MaterialPropertyTarget target, MenuEntry entry, Vector4 value, bool rest = false)
            {
                if (!MenuModel.PropertyExists(target, entry) || MenuModel.IsOutsideAvatar(avatarRoot, target.renderer.transform)) return;
                if (!properties.TryGetValue(target.renderer, out var map)) properties[target.renderer] = map = new Dictionary<string, PropertyValueEntry>();
                map[target.property] = new PropertyValueEntry(target.type, value);
                if (rest) restedOnly.Add((target.renderer, target.property));
                else restedOnly.Remove((target.renderer, target.property));
            }
            bool PropertyWritten(MaterialPropertyTarget target)
                => target.renderer != null && properties.TryGetValue(target.renderer, out var written) && written.ContainsKey(target.property ?? "");
            bool OnlyRested(MaterialPropertyTarget target) => target.renderer != null && restedOnly.Contains((target.renderer, target.property ?? ""));
            // A Hold trigger's layer follows the avatar's Write Defaults. Only Write Defaults off lets its Off state
            // leave a property to another layer: Write Defaults on writes the property every frame, so its Off state
            // carries the row's off value. Read once, and only when a resting trigger asks.
            bool? holdOffLeavesOwned = null;
            bool HoldOffLeavesOwned()
            {
                if (holdOffLeavesOwned == null)
                {
                    var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
                    holdOffLeavesOwned = !AnimatorBuilder.DecideWriteDefaultsForAvatar(ParametersWriter.FxOf(descriptor)).WriteDefaults;
                }
                return holdOffLeavesOwned.Value;
            }
            var heldContext = tree.HeldContext;
            float SceneWeight(SkinnedMeshRenderer renderer, string shape) => scene.ShapeWeight(renderer, renderer.sharedMesh.GetBlendShapeIndex(shape));
            // A toggle's or a Hold trigger's Pin rows and rules live in its pin layer, which the build moves above every
            // ordinary layer while keeping the pin layers' own order (MovePinLayersLast). So they are queued here and
            // written in tree order once the controls and the other triggers are done, before Fade contacts and blend
            // shape triggers, whose layers come after theirs. A parameter Fade's layer sits among the pin layers, in
            // its entry's place, so every row of it joins the queue in tree order.
            var pinned = new List<(BlendShapeTarget target, float weight)>();
            bool queuePinned = true;
            void WritePinned(BlendShapeTarget target, float weight)
            {
                if (queuePinned) pinned.Add((target, weight));
                else WriteShape(target, weight);
            }
            void FlushPinned()
            {
                queuePinned = false;
                foreach (var (target, weight) in pinned) WriteShape(target, weight);
                pinned.Clear();
            }
            void WriteSwitch(MenuEntry entry, bool on)
            {
                bool followValue = entry.IsFollowValueTrigger;
                bool pinLayer = entry.kind == EntryKind.Toggle || (entry.kind == EntryKind.Trigger && (!entry.HasDriver || followValue));
                foreach (var target in entry.objects) WriteObject(target, on);
                foreach (var target in entry.blendShapes)
                {
                    if (!on && !EffectMath.WritesWhenOff(target)) continue;
                    if (!MenuModel.ShapeExists(target.renderer, target.shape)) continue;
                    float weight = EffectMath.ToggleShapeWeight(target, on, BaseWeight(target));
                    if (on && pinLayer && (target.mode == ShapeEffectMode.Pin || followValue)) WritePinned(target, weight);
                    else WriteShape(target, weight);
                }
                // A shape set rule follows the same pin semantics as a pinned row: held at its value while on (0, or
                // the scene's own weight, which cancels whatever another previewed entry wrote there), released
                // entirely while off, so the preview shows exactly what the build would hold.
                if (on && entry.OffersShapeSets)
                    foreach (var rule in ShapeSetModel.EffectiveRules(entry))
                        foreach (var shape in ShapeSetModel.HeldShapes(entry, rule, heldContext))
                        {
                            var held = new BlendShapeTarget { renderer = rule.mesh, shape = shape };
                            float weight = ShapeSetModel.HeldWeight(rule, SceneWeight(rule.mesh, shape));
                            if (pinLayer) WritePinned(held, weight);
                            else WriteShape(held, weight);
                        }
                foreach (var target in entry.materials) WriteMaterial(target, on);
                if (entry.BuildsProperties)
                    foreach (var target in entry.properties)
                    {
                        // A Hold trigger's Off state on a Write Defaults on avatar writes the row's off value on every
                        // frame. Otherwise a trigger that is not met writes its off value once, from its Reset state
                        // at load: over nothing, or over another resting trigger's own once, since the later layer's
                        // Reset wins. A control, or a trigger that fires, written before this one writes the property
                        // on every frame and keeps it.
                        if (!on && entry.kind == EntryKind.Trigger)
                        {
                            var off = EffectMath.PropertyValue(target, false, BaseProperty(target, entry));
                            if (!entry.IsFollowDistanceTrigger && !HoldOffLeavesOwned()) WriteProperty(target, entry, off);
                            else if (!PropertyWritten(target) || OnlyRested(target)) WriteProperty(target, entry, off, rest: true);
                            continue;
                        }
                        WriteProperty(target, entry, EffectMath.PropertyValue(target, on, BaseProperty(target, entry)));
                    }
                if (entry.kind == EntryKind.Toggle || entry.kind == EntryKind.Trigger)
                    foreach (var target in entry.follows) WriteFollow(target, on);
            }
            // A Fade trigger part way through its Near blend, as the test hand leaves it: every shape, rule and
            // property between its off and on value, objects and material swaps the way Unity blends them. A
            // parameter Fade's layer sits among the pin layers, so its shapes join their queue. Follows cannot fade.
            void WriteFade(MenuEntry entry, float fraction)
            {
                bool pinned = entry.IsFollowValueTrigger;
                foreach (var target in entry.objects) WriteObjectActive(target, EffectMath.FadeObjectActive(target, fraction));
                foreach (var target in entry.blendShapes)
                {
                    if (!MenuModel.ShapeExists(target.renderer, target.shape)) continue;
                    float weight = EffectMath.FadeShapeWeight(target, fraction, BaseWeight(target));
                    if (pinned) WritePinned(target, weight);
                    else WriteShape(target, weight);
                }
                foreach (var rule in ShapeSetModel.EffectiveRules(entry))
                    foreach (var shape in ShapeSetModel.HeldShapes(entry, rule, heldContext))
                    {
                        var held = new BlendShapeTarget { renderer = rule.mesh, shape = shape };
                        float rest = SceneWeight(rule.mesh, shape);
                        float weight = Mathf.Lerp(rest, ShapeSetModel.HeldWeight(rule, rest), fraction);
                        if (pinned) WritePinned(held, weight);
                        else WriteShape(held, weight);
                    }
                foreach (var target in entry.materials) WriteMaterial(target, EffectMath.FadeMaterialOn(fraction));
                foreach (var target in entry.properties) WriteProperty(target, entry, EffectMath.FadePropertyValue(target, fraction, BaseProperty(target, entry)));
            }

            var winners = GroupWinners(tree, overrides);
            var builtGroups = new HashSet<string>();

            // A trigger's gate reads a toggle the same way any other control does: its override, else the group's
            // winner, else its default. A gate that does not resolve to a toggle is treated as always met; the
            // model reports the broken reference on its own.
            bool ToggleMet(string toggleId, bool wantOn)
            {
                if (string.IsNullOrEmpty(toggleId)) return true;
                var gate = tree.FindControl(toggleId);
                if (gate == null || gate.Entry.kind != EntryKind.Toggle) return true;
                bool on;
                if (!string.IsNullOrWhiteSpace(gate.Entry.exclusiveGroup))
                    on = winners.TryGetValue(gate.Entry.exclusiveGroup.Trim(), out var winner) && winner == gate;
                else
                    on = EffectiveValue(gate.Entry, BindingOf(plan, gate), overrides, scene) >= 0.5f;
                return wantOn == on;
            }
            bool GateMet(MenuEntry gated) => ToggleMet(gated.gateToggleId, gated.gateToggleOn);
            // A blend shape trigger's or a Fade contact's requirements: a toggle is read like the gate; a
            // parameter, a contact or a PhysBone is something the scene cannot know, so it counts as met (the
            // panel says so under the preview control).
            bool RequirementsMet(MenuEntry driven)
            {
                if (!GateMet(driven)) return false;
                foreach (var (_, _, condition) in driven.Requirements())
                    if (condition.kind == WhenConditionKind.Toggle && !ToggleMet(condition.sourceToggleId, condition.sourceToggleOn)) return false;
                return true;
            }
            bool ShapeWritten(BlendShapeTarget target)
                => shapes.TryGetValue(target.renderer, out var written) && written.ContainsKey(target.renderer.sharedMesh.GetBlendShapeIndex(target.shape));
            foreach (var control in tree.Controls)
            {
                // Nothing on this platform drives an excluded control, so the preview leaves its targets alone.
                if (control.Excluded) continue;
                var entry = control.Entry;
                var binding = BindingOf(plan, control);
                if (entry.kind == EntryKind.Grabbable)
                {
                    // No override forces it held, so a grabbable previews at its own rest place by default, the
                    // same as the built layer's Rest state; forcing it on shows it in the hand it would land in,
                    // the first allowed hand when both are allowed.
                    bool held = overrides != null && overrides.TryGetValue(entry.id, out var forcedGrab) && forcedGrab >= 0.5f;
                    if (held && entry.grabObject != null)
                    {
                        bool leftFirst = entry.grabHands != GrabHands.Right;
                        var hand = HumanoidHands.Hand(avatarRoot, leftFirst);
                        if (hand != null) WriteFollow(new FollowTarget { target = entry.grabObject, follows = hand, keepOffset = entry.grabKeepOffset }, true);
                    }
                    continue;
                }
                if (entry.kind == EntryKind.Radial)
                {
                    float t = EffectiveValue(entry, binding, overrides, scene);
                    foreach (var target in entry.blendShapes) WriteShape(target, EffectMath.RadialShapeWeight(target, t));
                    foreach (var target in entry.properties) WriteProperty(target, entry, EffectMath.RadialPropertyValue(target, t));
                    continue;
                }
                // Only a toggle is ever a group member, the same rule GroupWinners, PreviewState and
                // ParameterPlanner follow: a stale group name left on an entry of another kind means nothing.
                if (entry.kind == EntryKind.Toggle && !string.IsNullOrWhiteSpace(entry.exclusiveGroup))
                {
                    // A group is one state in the build (AnimatorBuilder.BuildGroupLayer), so it is written as one
                    // unit here too, at its first included member: every other member off first, the winner last.
                    // Written member by member in tree order instead, a member that is off would write its own off
                    // value over a target it shares with the winner.
                    var group = entry.exclusiveGroup.Trim();
                    if (!builtGroups.Add(group)) continue;
                    winners.TryGetValue(group, out var winner);
                    foreach (var member in tree.Controls)
                    {
                        if (member == winner || member.Excluded || member.Entry.kind != EntryKind.Toggle) continue;
                        var memberGroup = member.Entry.exclusiveGroup;
                        if (string.IsNullOrWhiteSpace(memberGroup) || memberGroup.Trim() != group) continue;
                        WriteSwitch(member.Entry, false);
                    }
                    if (winner != null) WriteSwitch(winner.Entry, true);
                    continue;
                }
                WriteSwitch(entry, EffectiveValue(entry, binding, overrides, scene) >= 0.5f);
            }
            // The build puts every Fade contact's layer above every other trigger's, and every blend shape trigger's
            // layer above those (MovePinLayersLast keeps the order they were built in), and copies a blend shape
            // trigger's shape from every FX clip in the final pass, Fade's Near clips included. So the order here is:
            // the other triggers, parameter Fades among them, then Fade contacts, then blend shape triggers, each
            // group in tree order.
            bool FadeContact(MenuEntry entry) => entry.IsFollowDistanceTrigger && !entry.IsFollowValueTrigger;
            foreach (var trigger in tree.Triggers.OrderBy(t => t.Entry.source == TriggerSource.BlendShape ? 2 : FadeContact(t.Entry) ? 1 : 0))
            {
                if (queuePinned && (trigger.Entry.source == TriggerSource.BlendShape || FadeContact(trigger.Entry))) FlushPinned();
                if (trigger.Excluded) continue;
                var entry = trigger.Entry;
                // A flip trigger acts on its target toggle's override, never on effects of its own.
                if (entry.IsFlipTrigger) continue;
                if (entry.source == TriggerSource.BlendShape)
                {
                    if (!MenuModel.ShapeExists(entry.sourceRenderer, entry.sourceShape)) continue;
                    float weight = SourceWeight(entry, scene, shapes);
                    bool met = RequirementsMet(entry);
                    bool above = met && EffectMath.LinkAbove(weight, entry.sourceThreshold);
                    // At rest the built layer sits in its rest state, which leaves a shape or a property to any
                    // other layer that moves it and rests it only where none does.
                    bool resting = !met || (!above && EffectMath.LinkFraction(entry, weight) <= 0f
                        && !entry.blendShapes.Any(t => t.mode == ShapeEffectMode.Follow && EffectMath.FollowWeight(weight, t.multiplier) > 0f));
                    foreach (var target in entry.objects) WriteObject(target, above);
                    foreach (var target in entry.materials) WriteMaterial(target, above);
                    foreach (var target in entry.blendShapes)
                    {
                        if (target.renderer == entry.sourceRenderer && target.shape == entry.sourceShape) continue;
                        if (!MenuModel.ShapeExists(target.renderer, target.shape)) continue;
                        if (resting)
                        {
                            if (!ShapeWritten(target)) WriteShape(target, BaseWeight(target));
                            continue;
                        }
                        WriteShape(target, EffectMath.LinkShapeWeight(entry, target, weight, BaseWeight(target)));
                    }
                    // A rule's shapes move like Fixed rows, from their default to the rule's value.
                    foreach (var rule in ShapeSetModel.EffectiveRules(entry))
                        foreach (var shape in ShapeSetModel.HeldShapes(entry, rule, heldContext))
                        {
                            var held = new BlendShapeTarget { renderer = rule.mesh, shape = shape };
                            float rest = SceneWeight(rule.mesh, shape);
                            if (resting)
                            {
                                if (!ShapeWritten(held)) WriteShape(held, rest);
                                continue;
                            }
                            WriteShape(held, Mathf.Lerp(rest, ShapeSetModel.HeldWeight(rule, rest), EffectMath.LinkFraction(entry, weight)));
                        }
                    foreach (var target in entry.properties)
                    {
                        if (resting)
                        {
                            if (!PropertyWritten(target) || OnlyRested(target)) WriteProperty(target, entry, EffectMath.PropertyValue(target, false, BaseProperty(target, entry)), rest: true);
                            continue;
                        }
                        WriteProperty(target, entry, EffectMath.LinkPropertyValue(entry, target, weight, BaseProperty(target, entry)));
                    }
                }
                else if (overrides != null && overrides.TryGetValue(entry.id, out var forced))
                {
                    if (!(entry.HasDriver ? RequirementsMet(entry) : GateMet(entry))) continue;
                    if (entry.IsFollowDistanceTrigger && forced > 0f && forced < 1f) WriteFade(entry, forced);
                    else WriteSwitch(entry, forced >= 0.5f);
                }
            }
            if (queuePinned) FlushPinned();
            foreach (var rule in tree.Overrides)
            {
                if (rule.Excluded) continue;
                foreach (var target in rule.Entry.blendShapes)
                    if (MenuModel.ShapeExists(target.renderer, target.shape)) WriteShape(target, target.weight);
            }

            ApplyPieces(avatarRoot, cached, pieces, objects, shapes, scene);

            foreach (var renderer in scene.Renderers(avatarRoot))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                bool visible = scene.RendererEnabled(renderer) && Visible(renderer.transform, avatarRoot, objects, scene);
                bool sceneVisible = scene.RendererEnabled(renderer) && Visible(renderer.transform, avatarRoot, null, scene);
                var state = new RendererState { Visible = visible };
                if (renderer is SkinnedMeshRenderer skinned && shapes.TryGetValue(skinned, out var shapeMap)) state.Shapes = DifferingShapes(skinned, shapeMap, scene);
                if (materials.TryGetValue(renderer, out var materialMap)) state.Materials = DifferingMaterials(renderer, materialMap, scene);
                if (properties.TryGetValue(renderer, out var propertyMap)) state.Properties = DifferingProperties(renderer, propertyMap);
                // A renderer on the followed object itself, or on any of its descendants (a sword whose blade is a
                // child mesh), moves the same rigid amount: the delta from the object's own current pose to where
                // it would land is computed once per followed object and then applied to this renderer's own pose,
                // so a multi-part prop previews as one piece instead of splitting.
                var follow = FindFollow(renderer.transform, avatarRoot, follows);
                if (follow != null)
                {
                    var originalPosition = scene.WorldPosition(follow.target.transform);
                    var originalRotation = scene.WorldRotation(follow.target.transform);
                    var (newPosition, newRotation) = FollowPose(follow, scene);
                    var delta = newRotation * Quaternion.Inverse(originalRotation);
                    var rendererPosition = scene.WorldPosition(renderer.transform);
                    var rendererRotation = scene.WorldRotation(renderer.transform);
                    state.HasFollow = true;
                    state.FollowPosition = newPosition + delta * (rendererPosition - originalPosition);
                    state.FollowRotation = delta * rendererRotation;
                }
                if (visible == sceneVisible && state.Shapes.IsEmpty && state.Materials.IsEmpty && state.Properties.IsEmpty && !state.HasFollow) continue;
                result[renderer] = state;
            }
            return result;
        }

        /// <summary>The followed object this renderer's own transform sits on or under, the nearest one walking up from it, or null when it is not part of any current claim.</summary>
        static FollowTarget FindFollow(Transform start, Transform avatarRoot, Dictionary<GameObject, FollowTarget> follows)
        {
            for (var cursor = start; cursor != null; cursor = cursor.parent)
            {
                if (follows.TryGetValue(cursor.gameObject, out var follow)) return follow;
                if (cursor == avatarRoot) break;
            }
            return null;
        }

        /// <summary>
        /// Where a followed object would sit in the preview. Off snaps onto the followed transform's own origin.
        /// On leaves the object exactly where it already is: keepOffset means the pose the object has relative to
        /// the followed transform as the creator posed it in the scene, which in the editor right now is simply
        /// wherever the object already sits, the same offset math the build's constraint bakes in at build time.
        /// </summary>
        static (Vector3 position, Quaternion rotation) FollowPose(FollowTarget follow, SceneReader scene)
        {
            if (follow.keepOffset) return (scene.WorldPosition(follow.target.transform), scene.WorldRotation(follow.target.transform));
            return (scene.WorldPosition(follow.follows), scene.WorldRotation(follow.follows));
        }

        /// <summary>
        /// What the AvatarDoc Shapes components of the outfit pieces add: the while-on values of the pieces on in the
        /// state being previewed, and the piece's own follow shapes at the weight the source shows in that state. A
        /// shape a control already writes keeps that value, the same rule the build follows when a clip already
        /// animates the shape. Where pieces hold one shape, the build's rule decides: the piece switched in the latest
        /// FX layer decides it, and while that layer's pieces are off the shape rests, whatever the others do; pieces
        /// of that one layer that are on together give the larger value.
        /// </summary>
        static void ApplyPieces(Transform avatarRoot, PreviewPlan cached, IReadOnlyList<AvatarDocShapes> pieces,
            Dictionary<GameObject, bool> objects, Dictionary<SkinnedMeshRenderer, Dictionary<int, float>> shapes, SceneReader scene)
        {
            var list = pieces ?? ShapesModel.Components(avatarRoot);
            if (list.Count == 0) return;
            var written = new Dictionary<(SkinnedMeshRenderer, int), float>();
            var switchesOf = cached.PieceSwitches ?? (cached.PieceSwitches = ShapesModel.PredictSwitches(avatarRoot, cached.Tree, AnimationUsage.ForDescriptor(avatarRoot.GetComponent<VRCAvatarDescriptor>(), avatarRoot)));
            var held = new Dictionary<(SkinnedMeshRenderer, int), List<(int layer, bool on, float value)>>();

            bool Taken(SkinnedMeshRenderer renderer, int index)
                => shapes.TryGetValue(renderer, out var map) && map.ContainsKey(index);

            void Hold(SkinnedMeshRenderer renderer, string shape, float weight)
            {
                if (!MenuModel.ShapeExists(renderer, shape) || MenuModel.IsOutsideAvatar(avatarRoot, renderer.transform)) return;
                int index = renderer.sharedMesh.GetBlendShapeIndex(shape);
                if (Taken(renderer, index)) return;
                written[(renderer, index)] = weight;
            }

            foreach (var component in list)
            {
                if (component == null || !component.enabled || !Platform.Included(component.platform)) continue;
                bool visible = Visible(component.transform, avatarRoot, objects, scene);
                var switches = switchesOf.For(component.gameObject, scene.ActiveSelf);
                int layer = switches.Count > 0 ? switches.Max(s => s.Layer) : -1;
                foreach (var row in component.whileOn)
                {
                    if (row == null || row.mesh == null) continue;
                    string shape = (row.shape ?? "").Trim();
                    if (!MenuModel.ShapeExists(row.mesh, shape) || MenuModel.IsOutsideAvatar(avatarRoot, row.mesh.transform)) continue;
                    var key = (row.mesh, row.mesh.sharedMesh.GetBlendShapeIndex(shape));
                    if (Taken(key.Item1, key.Item2)) continue;
                    if (!held.TryGetValue(key, out var holders)) held[key] = holders = new List<(int, bool, float)>();
                    holders.Add((layer, visible, Mathf.Clamp(row.value, 0f, 100f)));
                }
                if (!visible) continue;

                var local = component.GetComponent<SkinnedMeshRenderer>();
                if (local == null) continue;
                foreach (var row in component.follow)
                {
                    if (row == null || row.sourceMesh == null) continue;
                    string sourceShape = (row.sourceShape ?? "").Trim();
                    if (!MenuModel.ShapeExists(row.sourceMesh, sourceShape)) continue;
                    int sourceIndex = row.sourceMesh.sharedMesh.GetBlendShapeIndex(sourceShape);
                    float weight = shapes.TryGetValue(row.sourceMesh, out var map) && map.TryGetValue(sourceIndex, out var previewed)
                        ? previewed
                        : scene.ShapeWeight(row.sourceMesh, sourceIndex);
                    Hold(local, row.EffectiveLocalShape, weight);
                }
            }

            foreach (var pair in held)
            {
                int latest = pair.Value.Max(h => h.layer);
                var on = pair.Value.Where(h => h.layer == latest && h.on).ToList();
                if (on.Count > 0) written[pair.Key] = on.Max(h => h.value);
            }

            foreach (var pair in written)
            {
                var (renderer, index) = pair.Key;
                if (!shapes.TryGetValue(renderer, out var map)) shapes[renderer] = map = new Dictionary<int, float>();
                map[index] = pair.Value;
            }
        }

        /// <summary>Keeps only the slots whose written material differs from what the scene already shows there, so a control that writes its own off value does not count as a write.</summary>
        static ImmutableSortedDictionary<int, Material> DifferingMaterials(Renderer renderer, Dictionary<int, Material> materialMap, SceneReader scene)
        {
            var current = scene.Materials(renderer);
            var builder = ImmutableSortedDictionary.CreateBuilder<int, Material>();
            foreach (var pair in materialMap)
            {
                var baseMaterial = pair.Key < current.Length ? current[pair.Key] : null;
                if (pair.Value != baseMaterial) builder[pair.Key] = pair.Value;
            }
            return builder.ToImmutable();
        }

        /// <summary>Keeps only the properties whose written value differs from what the scene already shows there, so a control that writes its own off value does not count as a write.</summary>
        static ImmutableSortedDictionary<string, PropertyValueEntry> DifferingProperties(Renderer renderer, Dictionary<string, PropertyValueEntry> written)
        {
            var builder = ImmutableSortedDictionary.CreateBuilder<string, PropertyValueEntry>();
            foreach (var pair in written)
            {
                var baseValue = MenuModel.ReadProperty(MenuModel.MaterialWithProperty(renderer, pair.Key), pair.Key, pair.Value.Type);
                if (pair.Value.Value != baseValue) builder[pair.Key] = pair.Value;
            }
            return builder.ToImmutable();
        }

        /// <summary>Keeps only the shapes whose written weight differs from what the scene already shows there, so a control that writes its own off value does not count as a write.</summary>
        static ImmutableSortedDictionary<int, float> DifferingShapes(SkinnedMeshRenderer renderer, Dictionary<int, float> written, SceneReader scene)
        {
            var builder = ImmutableSortedDictionary.CreateBuilder<int, float>();
            foreach (var pair in written)
                if (!Mathf.Approximately(scene.ShapeWeight(renderer, pair.Key), pair.Value)) builder[pair.Key] = pair.Value;
            return builder.ToImmutable();
        }

        /// <summary>Walks from the renderer up to the avatar root: a forced state wins over the scene, an unforced inactive object hides everything below it.</summary>
        static bool Visible(Transform start, Transform avatarRoot, Dictionary<GameObject, bool> forced, SceneReader scene)
        {
            for (var cursor = start; cursor != null; cursor = cursor.parent)
            {
                bool active = forced != null && forced.TryGetValue(cursor.gameObject, out var state) ? state : scene.ActiveSelf(cursor.gameObject);
                if (!active) return false;
                if (cursor == avatarRoot) break;
            }
            return true;
        }

        /// <summary>The one member of each exclusive group that is on: the last overridden-on member in tree order, else the first defaultOn member. Shared with PreviewState, so the wheel, test mode and the panel answer "is this member on" the same way the scene preview does.</summary>
        internal static Dictionary<string, ControlNode> GroupWinners(MenuTree tree, IReadOnlyDictionary<string, float> overrides)
        {
            var winners = new Dictionary<string, ControlNode>();
            foreach (var control in tree.Controls)
            {
                if (control.Excluded) continue;
                var entry = control.Entry;
                if (entry.kind != EntryKind.Toggle || string.IsNullOrWhiteSpace(entry.exclusiveGroup)) continue;
                string group = entry.exclusiveGroup.Trim();
                float value = 0f;
                bool overridden = overrides != null && overrides.TryGetValue(entry.id, out value);
                if (overridden && value >= 0.5f) winners[group] = control;
                else if (!overridden && entry.defaultOn && !winners.ContainsKey(group)) winners[group] = control;
            }
            return winners;
        }

        static ControlBinding BindingOf(ParameterPlan plan, ControlNode control)
            => plan != null && plan.Bindings.TryGetValue(control, out var binding) ? binding : null;
    }
}
#endif
