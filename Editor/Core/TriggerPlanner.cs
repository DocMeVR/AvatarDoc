#if DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Condition: an Off and On layer. Link: a blend shape trigger, its own layer driven by the hidden copy of its shape. Fade: a Follow distance contact, whose layer TouchBuilder builds; the plan carries only its requirements.</summary>
    public enum TriggerKind { Condition = 0, Link = 1, Fade = 2 }

    /// <summary>An animator condition for a trigger: which parameter, its type, and the transitions that switch the trigger on and off.</summary>
    public sealed class TriggerCondition
    {
        public string Parameter = "";
        public PlannedType Type;
        public float Default;
        /// <summary>Each inner list is one transition (its conditions are ANDed); several lists are ORed.</summary>
        public List<List<AnimatorCondition>> OnTransitions = new List<List<AnimatorCondition>>();
        public List<List<AnimatorCondition>> OffTransitions = new List<List<AnimatorCondition>>();
    }

    public sealed class TriggerPlan
    {
        public TriggerNode Node;
        public TriggerKind Kind;
        /// <summary>Every condition of the trigger's "when", resolved in the order the groups hold them. A trigger with no groups of its own has exactly one, made from its flat source fields.</summary>
        public List<TriggerCondition> Conditions = new List<TriggerCondition>();
        /// <summary>The first resolved condition. A single source trigger has only this one.</summary>
        public TriggerCondition Condition => Conditions.Count > 0 ? Conditions[0] : null;
        /// <summary>The conditions that must also hold for this trigger to work, built the same way a toggle sourced trigger's own condition is. Empty when the trigger has no gate.</summary>
        public List<TriggerCondition> Gate = new List<TriggerCondition>();
        /// <summary>The transitions into the On state, gate included: one inner list per transition.</summary>
        public List<List<AnimatorCondition>> Enter = new List<List<AnimatorCondition>>();
        /// <summary>The transitions back out of the On state, gate included.</summary>
        public List<List<AnimatorCondition>> Leave = new List<List<AnimatorCondition>>();
        public SkinnedMeshRenderer SourceRenderer;
        public string SourceShape = "";
        public float Threshold;
        public bool Replace;
        public BlendShapeLinkStyle Style;
        /// <summary>Smooth only: where the effects start and where they are fully on, in shape weight, as MenuEntry.EffectiveSmoothStart and EffectiveSmoothFull give them.</summary>
        public float SmoothStart;
        public float SmoothFull = 100f;
        /// <summary>The hidden Float parameter that copies the source shape's weight, in the same 0 to 100 units, from every FX clip that moves it. Never an expression parameter.</summary>
        public string MirrorParameter = "";
        /// <summary>The trigger's pause flag: a blend shape trigger's when its effects or a rule move a shape VRChat blinks or speaks with, a Hold trigger's when a rule holds one. Null otherwise.</summary>
        public TouchBuilder.PauseFlag PauseFlag;
        public MenuEntry Entry => Node.Entry;
        public AvatarDoc Component => Node.Component;
    }

    public static class TriggerPlanner
    {
        static readonly Dictionary<string, PlannedType> BuiltIns = new Dictionary<string, PlannedType>
        {
            { "IsLocal", PlannedType.Bool }, { "Viseme", PlannedType.Int }, { "Voice", PlannedType.Float },
            { "GestureLeft", PlannedType.Int }, { "GestureRight", PlannedType.Int },
            { "GestureLeftWeight", PlannedType.Float }, { "GestureRightWeight", PlannedType.Float },
            { "AngularY", PlannedType.Float }, { "VelocityX", PlannedType.Float }, { "VelocityY", PlannedType.Float },
            { "VelocityZ", PlannedType.Float }, { "VelocityMagnitude", PlannedType.Float }, { "Upright", PlannedType.Float },
            { "Grounded", PlannedType.Bool }, { "Seated", PlannedType.Bool }, { "AFK", PlannedType.Bool },
            { "TrackingType", PlannedType.Int }, { "VRMode", PlannedType.Int }, { "MuteSelf", PlannedType.Bool },
            { "InStation", PlannedType.Bool }, { "Earmuffs", PlannedType.Bool }, { "IsOnFriendsList", PlannedType.Bool },
            { "AvatarVersion", PlannedType.Int }, { "ScaleModified", PlannedType.Bool }, { "ScaleFactor", PlannedType.Float },
            { "ScaleFactorInverse", PlannedType.Float }, { "EyeHeightAsMeters", PlannedType.Float }, { "EyeHeightAsPercent", PlannedType.Float },
            { "VRCEmote", PlannedType.Int }, { "VRCFaceBlendH", PlannedType.Float }, { "VRCFaceBlendV", PlannedType.Float },
            { "IsAnimatorEnabled", PlannedType.Bool },
        };

        public static PlannedType? BuiltInType(string name)
        {
            return BuiltIns.TryGetValue(name, out var type) ? type : (PlannedType?)null;
        }

        /// <summary>Every VRChat built-in parameter name a trigger can watch, for the parameter picker.</summary>
        public static IEnumerable<string> BuiltInNames => BuiltIns.Keys;

        public static (List<TriggerPlan> plans, List<ModelMessage> messages) Plan(MenuTree tree, ParameterPlan plan, IReadOnlyDictionary<string, ExistingParameter> existing, TouchBuilder.TouchParameters touch = null)
        {
            var plans = new List<TriggerPlan>();
            var messages = new List<ModelMessage>();
            foreach (var node in tree.Triggers)
            {
                var entry = node.Entry;
                if (node.Excluded) continue;
                if (!entry.HasEffects) continue;
                // A flip trigger has no effect layer of its own; TouchBuilder.BuildFlipLayers builds the flip layer
                // instead. Its transitions come out of the same conditions, so the size is counted here all the
                // same: the inspector has to say so, and the build has to stop before it writes anything.
                if (entry.IsFlipTrigger)
                {
                    var aside = new List<ModelMessage>();
                    if (Resolve(tree, plan, node, existing, touch, aside, out _, out var flipMet))
                        LeaveTooComplex(node, flipMet, AndGate(Enumerable.Empty<AnimatorCondition>(), BuildGate(tree, plan, entry)), messages);
                    continue;
                }
                // Follow distance builds its own Far/Near layer from the receiver's float directly
                // (TouchBuilder.BuildFollowDistanceLayers); its plan carries only the requirements that layer adds.
                TriggerPlan result;
                if (entry.IsFollowDistanceTrigger) result = PlanFade(tree, plan, node, existing, touch, messages);
                else if (entry.source == TriggerSource.BlendShape) result = PlanLink(tree, plan, node, existing, touch, messages);
                else result = PlanConditions(tree, plan, node, existing, touch, messages);
                if (result != null) plans.Add(result);
            }
            return (plans, messages);
        }

        /// <summary>
        /// The whole "when" of one trigger: every condition of every group resolved to a parameter and its
        /// transitions, the gate folded in, and the two transition sets the layer is built from. Fails as a whole
        /// when any one condition does not resolve, since a trigger missing part of its rule would fire on the
        /// rest of it.
        /// </summary>
        static TriggerPlan PlanConditions(MenuTree tree, ParameterPlan plan, TriggerNode node, IReadOnlyDictionary<string, ExistingParameter> existing, TouchBuilder.TouchParameters touch, List<ModelMessage> messages)
        {
            if (!Resolve(tree, plan, node, existing, touch, messages, out var conditions, out var met)) return null;
            var result = new TriggerPlan { Node = node, Kind = TriggerKind.Condition, Conditions = conditions };
            if (touch != null && touch.PauseFlags.TryGetValue(node.Entry.id, out var pause)) result.PauseFlag = pause;
            result.Gate = BuildGate(tree, plan, node.Entry);
            var gate = AndGate(Enumerable.Empty<AnimatorCondition>(), result.Gate);
            if (LeaveTooComplex(node, met, gate, messages)) return null;
            var isInt = IntLookup(conditions.Concat(result.Gate));
            result.Enter = ConditionLogic.Enter(met, gate, isInt);
            result.Leave = ConditionLogic.Leave(met, gate, isInt);
            return result;
        }

        /// <summary>
        /// Whether leaving this trigger's On state would take more transitions than a layer may hold, counted from
        /// the expanded alternatives before a single combination is built: a comparison that has two ways to be
        /// true doubles the work, so counting the conditions the author wrote would miss it by orders of
        /// magnitude. A trigger this large builds nothing. The model already reports the shapes it can see for
        /// itself, so those are not named twice.
        /// </summary>
        internal static bool LeaveTooComplex(TriggerNode node, List<List<AnimatorCondition>> met, List<List<AnimatorCondition>> gate, List<ModelMessage> messages)
        {
            long leave = (long)ConditionLogic.LeaveEstimate(met) + ConditionLogic.LeaveEstimate(gate);
            if (leave <= ConditionLogic.MaxLeaveTransitions) return false;
            if (!ConditionLogic.TooComplex(node.Entry.EffectiveWhen, out _, out _))
                messages.Add(ModelMessage.Error(UiText.CountKey(node.Entry.EffectiveWhen.Count, "ad.error.trigger_too_complex"), node.Entry.PlainName,
                    node.Entry.EffectiveWhen.Count.ToString(),
                    Math.Min(leave, int.MaxValue).ToString()).For(node.Entry.id));
            return true;
        }

        /// <summary>
        /// Resolves every condition of a trigger's groups and the disjunctive form of "met": one inner list per
        /// way the trigger can be on, its conditions ANDed. Shared by the planner and TouchBuilder's flip layer,
        /// which needs exactly the same conditions for its "touched" transitions.
        /// </summary>
        internal static bool Resolve(MenuTree tree, ParameterPlan plan, TriggerNode node, IReadOnlyDictionary<string, ExistingParameter> existing, TouchBuilder.TouchParameters touch, List<ModelMessage> messages, out List<TriggerCondition> conditions, out List<List<AnimatorCondition>> met)
        {
            var entry = node.Entry;
            conditions = new List<TriggerCondition>();
            met = new List<List<AnimatorCondition>>();
            // A rule past the caps is reported by the model and builds nothing. Multiplying out its ways to be met
            // would stall every plan before anything counted them: twenty "is not" conditions on a float are a
            // million combinations. Follow distance never reads its groups, so it is never too complex.
            if (!entry.IsFollowDistanceTrigger && ConditionLogic.TooComplex(entry.EffectiveWhen, out _, out _)) return false;
            bool any = false;
            foreach (var (groupIndex, group) in entry.EffectiveGroups())
            {
                var inGroup = new List<TriggerCondition>();
                for (int i = 0; i < group.all.Count; i++)
                {
                    var condition = group.all[i];
                    if (condition == null) continue;
                    var resolved = ResolveOne(tree, plan, node, condition, existing, touch, groupIndex, i, messages);
                    if (resolved == null) { conditions = new List<TriggerCondition>(); met = new List<List<AnimatorCondition>>(); return false; }
                    inGroup.Add(resolved);
                }
                if (inGroup.Count == 0) continue;
                conditions.AddRange(inGroup);
                met.AddRange(AndGate(Enumerable.Empty<AnimatorCondition>(), inGroup));
                any = true;
            }
            return any;
        }

        /// <summary>One condition of any kind, resolved the way that kind's single source trigger has always been resolved. Null when it names something that is not there; the message says which condition it was.</summary>
        static TriggerCondition ResolveOne(MenuTree tree, ParameterPlan plan, TriggerNode node, WhenCondition condition, IReadOnlyDictionary<string, ExistingParameter> existing, TouchBuilder.TouchParameters touch, int groupIndex, int index, List<ModelMessage> messages)
        {
            var entry = node.Entry;
            var name = entry.UsesWhenGroups ? entry.PlainName + ConditionLogic.Position(groupIndex, index) : entry.PlainName;
            switch (condition.kind)
            {
                case WhenConditionKind.Toggle:
                {
                    var resolved = ToggleCondition(tree, plan, condition.sourceToggleId, condition.sourceToggleOn);
                    if (resolved == null) messages.Add(ModelMessage.Warning("ad.warn.trigger_source_missing", name, node.Component).For(entry.id));
                    return resolved;
                }
                case WhenConditionKind.Parameter:
                    return ParameterConditionFor(condition, name, node, existing, messages);
                default:
                {
                    var key = TouchBuilder.TouchKey(entry, groupIndex, index);
                    if (touch == null || !touch.ByTriggerId.TryGetValue(key, out var info)) return null;
                    return BuildCondition(info.Parameter, info.Type, info.Mode, info.Value);
                }
            }
        }

        /// <summary>A ready TriggerCondition for one parameter, type and comparison. Shared by the planner and TouchBuilder's flip layer, which needs the same touch condition a hold trigger would use.</summary>
        internal static TriggerCondition BuildCondition(string parameter, PlannedType type, ParameterCondition mode, float value)
        {
            var condition = new TriggerCondition { Parameter = parameter, Type = type };
            Fill(condition, type, mode, value);
            return condition;
        }

        /// <summary>A ready TriggerCondition for a toggle in the chosen state (Bool, a valued Int or an exclusive group member), or null when the toggle does not resolve. Shared by a toggle sourced trigger's own condition and a trigger's gate, which reads a toggle the same way.</summary>
        static TriggerCondition ToggleCondition(MenuTree tree, ParameterPlan plan, string toggleId, bool wantOn)
        {
            var control = tree.FindControl(toggleId);
            if (control == null || control.Entry.kind != EntryKind.Toggle || !plan.Bindings.TryGetValue(control, out var binding)) return null;
            var condition = new TriggerCondition { Parameter = binding.Parameter.Name, Type = binding.Parameter.Type, Default = binding.Parameter.Default };
            Fill(condition, binding.Parameter.Type, ParameterCondition.Equals, binding.Value);
            if (!wantOn) Swap(condition);
            return condition;
        }

        /// <summary>A condition on a parameter the avatar already has, an animator only one or a VRChat built-in. An unknown name still builds, as a Float, with the warning it has always had.</summary>
        static TriggerCondition ParameterConditionFor(WhenCondition source, string name, TriggerNode node, IReadOnlyDictionary<string, ExistingParameter> existing, List<ModelMessage> messages)
        {
            var parameter = (source.sourceParameter ?? "").Trim();
            if (parameter.Length == 0)
            {
                messages.Add(ModelMessage.Warning("ad.warn.trigger_source_missing", name, node.Component).For(node.Entry.id));
                return null;
            }
            var condition = new TriggerCondition { Parameter = parameter };
            if (existing != null && existing.TryGetValue(parameter, out var known))
            {
                condition.Type = known.Type;
                condition.Default = known.Default;
            }
            else
            {
                var builtIn = BuiltInType(parameter);
                if (builtIn.HasValue) condition.Type = builtIn.Value;
                else
                {
                    condition.Type = PlannedType.Float;
                    messages.Add(ModelMessage.Warning("ad.warn.parameter_type_unknown", parameter, name).For(node.Entry.id));
                }
            }
            Fill(condition, condition.Type, source.condition, source.conditionValue);
            return condition;
        }

        /// <summary>Whether each parameter these conditions read is an Int, which is what negating a Greater or a Less has to know.</summary>
        internal static Func<string, bool> IntLookup(IEnumerable<TriggerCondition> conditions)
        {
            var map = new Dictionary<string, bool>();
            foreach (var condition in conditions)
                if (!string.IsNullOrEmpty(condition.Parameter)) map[condition.Parameter] = condition.Type == PlannedType.Int;
            return parameter => parameter != null && map.TryGetValue(parameter, out var isInt) && isInt;
        }

        /// <summary>The gate condition of a trigger, if it has one that resolves: a toggle sourced condition for gateToggleId in the gateToggleOn state. Empty (not null) when there is no gate or it does not resolve, so a caller can AND it onto its own conditions without a null check; validation reports the broken reference separately.</summary>
        internal static List<TriggerCondition> BuildGate(MenuTree tree, ParameterPlan plan, MenuEntry entry)
        {
            var result = new List<TriggerCondition>();
            if (string.IsNullOrEmpty(entry.gateToggleId)) return result;
            var condition = ToggleCondition(tree, plan, entry.gateToggleId, entry.gateToggleOn);
            if (condition != null) result.Add(condition);
            return result;
        }

        /// <summary>Every combination of the given fixed conditions ANDed with one on-transition from each gate condition. With no gate, the single base combination unchanged.</summary>
        internal static List<List<AnimatorCondition>> AndGate(IEnumerable<AnimatorCondition> baseConditions, List<TriggerCondition> gate)
        {
            var combos = new List<List<AnimatorCondition>> { new List<AnimatorCondition>(baseConditions) };
            if (gate == null) return combos;
            foreach (var g in gate)
            {
                var next = new List<List<AnimatorCondition>>();
                foreach (var combo in combos)
                    foreach (var onList in g.OnTransitions)
                    {
                        var merged = new List<AnimatorCondition>(combo);
                        merged.AddRange(onList);
                        next.Add(merged);
                    }
                combos = next;
            }
            return combos;
        }

        static TriggerPlan PlanLink(MenuTree tree, ParameterPlan plan, TriggerNode node, IReadOnlyDictionary<string, ExistingParameter> existing, TouchBuilder.TouchParameters touch, List<ModelMessage> messages)
        {
            var entry = node.Entry;
            if (!MenuModel.ShapeExists(entry.sourceRenderer, entry.sourceShape) || MenuModel.IsOutsideAvatar(tree.AvatarRoot, entry.sourceRenderer.transform))
            {
                messages.Add(ModelMessage.Warning("ad.warn.trigger_source_missing", entry.PlainName, node.Component).For(entry.id));
                return null;
            }
            if (entry.blendShapes.Any(t => t.renderer == entry.sourceRenderer && t.shape == entry.sourceShape))
                messages.Add(ModelMessage.Warning("ad.warn.link_effect_is_source", entry.PlainName, entry.sourceShape).For(entry.id));
            // VRChat writes its blinking and lip sync shapes after the animator, so no clip ever moves them and the
            // hidden copy never sees them move.
            var descriptor = tree.AvatarRoot != null ? tree.AvatarRoot.GetComponent<VRCAvatarDescriptor>() : null;
            var (eyelids, mouth) = TouchBuilder.DescriptorDrivenShapes(descriptor);
            var sourceKey = (entry.sourceRenderer, entry.sourceShape);
            if (eyelids.Contains(sourceKey) || mouth.Contains(sourceKey))
                messages.Add(ModelMessage.Warning("ad.warn.link_source_tracked", entry.PlainName, entry.sourceRenderer.name + "/" + entry.sourceShape, node.Component).For(entry.id));
            string mirror;
            if (touch == null || !touch.ShapeMirrors.TryGetValue(entry.id, out mirror))
                mirror = TouchBuilder.ShapeMirrorFor(entry, node.Component != null ? node.Component.parameterPrefix : "AD");
            var result = new TriggerPlan
            {
                Node = node, Kind = TriggerKind.Link, SourceRenderer = entry.sourceRenderer, SourceShape = entry.sourceShape,
                Threshold = entry.sourceThreshold, Replace = entry.linkMode == BlendShapeLinkMode.Replace,
                Style = entry.linkStyle, SmoothStart = entry.EffectiveSmoothStart, SmoothFull = entry.EffectiveSmoothFull,
                MirrorParameter = mirror,
            };
            if (touch != null && touch.PauseFlags.TryGetValue(entry.id, out var pause)) result.PauseFlag = pause;
            return PlanRequirements(tree, plan, node, existing, touch, messages, result) ? result : null;
        }

        /// <summary>A Fade contact's requirements, for the Far and Near layer TouchBuilder builds from its receiver. Null when a requirement does not resolve, so the layer is never built on the rest of its rule.</summary>
        static TriggerPlan PlanFade(MenuTree tree, ParameterPlan plan, TriggerNode node, IReadOnlyDictionary<string, ExistingParameter> existing, TouchBuilder.TouchParameters touch, List<ModelMessage> messages)
        {
            var result = new TriggerPlan { Node = node, Kind = TriggerKind.Fade };
            return PlanRequirements(tree, plan, node, existing, touch, messages, result) ? result : null;
        }

        /// <summary>
        /// The requirements of a trigger with one driver (a blend shape trigger or a Fade contact) and its Only
        /// while toggle, resolved the way any condition is: all must hold to enter the driven state, and any one
        /// failing leaves it, one transition each. With none, Enter is one empty transition and Leave is empty, so
        /// the layer is built from its driver alone, exactly as before requirements existed.
        /// </summary>
        static bool PlanRequirements(MenuTree tree, ParameterPlan plan, TriggerNode node, IReadOnlyDictionary<string, ExistingParameter> existing, TouchBuilder.TouchParameters touch, List<ModelMessage> messages, TriggerPlan result)
        {
            var conditions = new List<TriggerCondition>();
            foreach (var (groupIndex, index, condition) in node.Entry.Requirements())
            {
                var resolved = ResolveOne(tree, plan, node, condition, existing, touch, groupIndex, index, messages);
                if (resolved == null) return false;
                conditions.Add(resolved);
            }
            result.Conditions = conditions;
            result.Gate = BuildGate(tree, plan, node.Entry);
            var met = AndGate(Enumerable.Empty<AnimatorCondition>(), conditions);
            var gate = AndGate(Enumerable.Empty<AnimatorCondition>(), result.Gate);
            if (LeaveTooComplex(node, met, gate, messages)) return false;
            var isInt = IntLookup(conditions.Concat(result.Gate));
            result.Enter = ConditionLogic.Enter(met, gate, isInt);
            result.Leave = ConditionLogic.Leave(met, gate, isInt);
            return true;
        }

        static AnimatorCondition Cond(string parameter, AnimatorConditionMode mode, float threshold)
        {
            return new AnimatorCondition { parameter = parameter, mode = mode, threshold = threshold };
        }

        static List<AnimatorCondition> One(params AnimatorCondition[] conditions) => conditions.ToList();

        /// <summary>
        /// The transitions one comparison makes. Only the "on" side is written here: the "off" side is its
        /// opposite, which ConditionLogic works out, so a comparison is described once and one negation rule
        /// serves the single source, the groups and the gate alike.
        /// </summary>
        static void Fill(TriggerCondition c, PlannedType type, ParameterCondition mode, float v)
        {
            var p = c.Parameter;
            c.OnTransitions.Clear();
            c.OffTransitions.Clear();
            switch (type)
            {
                case PlannedType.Bool:
                {
                    bool wantTrue = mode == ParameterCondition.IsTrue || mode == ParameterCondition.Greater
                        || (mode == ParameterCondition.Equals && v != 0f) || (mode == ParameterCondition.NotEquals && v == 0f);
                    c.OnTransitions.Add(One(Cond(p, wantTrue ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f)));
                    break;
                }
                case PlannedType.Int:
                {
                    int i = Mathf.RoundToInt(v);
                    switch (mode)
                    {
                        case ParameterCondition.NotEquals: c.OnTransitions.Add(One(Cond(p, AnimatorConditionMode.NotEqual, i))); break;
                        case ParameterCondition.Greater: c.OnTransitions.Add(One(Cond(p, AnimatorConditionMode.Greater, i))); break;
                        case ParameterCondition.Less: c.OnTransitions.Add(One(Cond(p, AnimatorConditionMode.Less, i))); break;
                        case ParameterCondition.IsTrue: c.OnTransitions.Add(One(Cond(p, AnimatorConditionMode.NotEqual, 0))); break;
                        case ParameterCondition.IsFalse: c.OnTransitions.Add(One(Cond(p, AnimatorConditionMode.Equals, 0))); break;
                        default: c.OnTransitions.Add(One(Cond(p, AnimatorConditionMode.Equals, i))); break;
                    }
                    break;
                }
                default:
                {
                    // A float parameter has no Equals of its own, so "the same value" is a narrow window instead.
                    const float window = 0.005f;
                    switch (mode)
                    {
                        case ParameterCondition.NotEquals:
                            c.OnTransitions.Add(One(Cond(p, AnimatorConditionMode.Less, v - window)));
                            c.OnTransitions.Add(One(Cond(p, AnimatorConditionMode.Greater, v + window)));
                            break;
                        case ParameterCondition.Greater: c.OnTransitions.Add(One(Cond(p, AnimatorConditionMode.Greater, v))); break;
                        case ParameterCondition.Less: c.OnTransitions.Add(One(Cond(p, AnimatorConditionMode.Less, v))); break;
                        case ParameterCondition.IsTrue: c.OnTransitions.Add(One(Cond(p, AnimatorConditionMode.Greater, 0.5f))); break;
                        case ParameterCondition.IsFalse: c.OnTransitions.Add(One(Cond(p, AnimatorConditionMode.Less, 0.5f))); break;
                        default: c.OnTransitions.Add(One(Cond(p, AnimatorConditionMode.Greater, v - window), Cond(p, AnimatorConditionMode.Less, v + window))); break;
                    }
                    break;
                }
            }
            c.OffTransitions.AddRange(ConditionLogic.Opposite(c.OnTransitions, _ => type == PlannedType.Int));
        }

        static void Swap(TriggerCondition c)
        {
            var on = c.OnTransitions;
            c.OnTransitions = c.OffTransitions;
            c.OffTransitions = on;
        }
    }
}
#endif
