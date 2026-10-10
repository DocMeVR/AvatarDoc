#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>One FX layer per preset: Idle and Apply, with a parameter driver on Apply that sets every state and named parameter the preset names.</summary>
    public static class PresetBuilder
    {
        public static List<ModelMessage> Build(AnimatorServicesContext asc, MenuTree tree, ParameterPlan plan, ICollection<string> pinLayerNames = null)
        {
            var messages = new List<ModelMessage>();
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return messages;
            bool writeDefaults = AnimatorBuilder.DecideWriteDefaults(fx, pinLayerNames);
            var descriptor = tree.AvatarRoot != null ? tree.AvatarRoot.GetComponent<VRCAvatarDescriptor>() : null;
            var expressionParameters = descriptor != null ? descriptor.expressionParameters : null;

            foreach (var control in tree.Controls)
            {
                var entry = control.Entry;
                if (entry.kind != EntryKind.Preset) continue;
                // Its parameter stays in the plan so both platforms declare the same list; only the layer goes.
                if (control.Excluded) continue;
                if (!plan.Bindings.TryGetValue(control, out var binding)) continue;

                AnimatorBuilder.EnsureParameter(fx, binding.Parameter.Name, binding.Parameter.Type, binding.Parameter.Default);

                var operations = Operations(entry, tree, plan, messages);
                var component = tree.Components.FirstOrDefault(c => c.entries.Contains(entry));
                foreach (var op in operations)
                    if (!IsKnownParameter(op.parameter, fx, expressionParameters, plan))
                        messages.Add(ModelMessage.Warning("ad.warn.preset_parameter_unknown", op.parameter, entry.PlainName, component).For(entry.id));

                var name = AnimatorBuilder.UniqueLayerName(fx, "AD: " + entry.PlainName + " (preset)");
                var layer = fx.AddLayer(LayerPriority.Default, name);
                var sm = layer.StateMachine;
                var idle = sm.AddState("Idle", new ClipContent(tree.AvatarRoot).ToClip(asc, name + " Idle"), new Vector3(0f, 0f, 0f));
                var apply = sm.AddState("Apply", new ClipContent(tree.AvatarRoot).ToClip(asc, name + " Apply"), new Vector3(0f, 120f, 0f));
                idle.WriteDefaultValues = writeDefaults;
                apply.WriteDefaultValues = writeDefaults;
                sm.DefaultState = idle;

                var driver = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
                driver.localOnly = true;
                driver.parameters = operations.Select(op => new VRC_AvatarParameterDriver.Parameter
                {
                    type = VRC_AvatarParameterDriver.ChangeType.Set,
                    name = op.parameter,
                    value = op.value,
                }).ToList();
                apply.Behaviours = ImmutableList.Create<StateMachineBehaviour>(driver);

                var onCondition = ImmutableList.Create(new AnimatorCondition { parameter = binding.Parameter.Name, mode = AnimatorConditionMode.If, threshold = 0f });
                var offCondition = ImmutableList.Create(new AnimatorCondition { parameter = binding.Parameter.Name, mode = AnimatorConditionMode.IfNot, threshold = 0f });
                idle.Transitions = ImmutableList.Create(AnimatorBuilder.Transition(apply, onCondition));
                apply.Transitions = ImmutableList.Create(AnimatorBuilder.Transition(idle, offCondition));
            }
            // The model reports a missing target while it validates, so the build does not say it again.
            messages.RemoveAll(m => m.Key == "ad.warn.preset_target_missing");
            return messages;
        }

        /// <summary>The Set operations a preset's driver writes: one per state it names, plus its named parameters as given. Pure: resolves entries and bindings from the tree and the plan only, so a caller can test it without an animator.</summary>
        public static List<(string parameter, float value)> Operations(MenuEntry preset, MenuTree tree, ParameterPlan plan, List<ModelMessage> messages)
        {
            var result = new List<(string parameter, float value)>();
            var component = tree.Components.FirstOrDefault(c => c.entries.Contains(preset));
            // Every toggle state the preset names is pooled by the parameter name it is bound to, exclusive group
            // or not: several toggles can share one custom parameter the same way a group shares its int, so the
            // same on-wins, off-writes-zero-once, two-on-is-a-conflict rule applies to both without telling them apart.
            var unitValue = new Dictionary<string, float>();
            var unitHasOff = new HashSet<string>();
            var unitConflictReported = new HashSet<string>();
            var unitLabel = new Dictionary<string, string>();

            foreach (var state in preset.presetStates)
            {
                var target = tree.FindControl(state.entryId);
                if (target == null)
                {
                    messages.Add(ModelMessage.Warning("ad.warn.preset_target_missing", preset.PlainName, component).For(preset.id));
                    continue;
                }
                if (!plan.Bindings.TryGetValue(target, out var binding)) continue;
                var entry = target.Entry;

                if (entry.kind == EntryKind.Radial)
                {
                    result.Add((binding.Parameter.Name, state.value));
                    continue;
                }

                var paramName = binding.Parameter.Name;
                unitLabel[paramName] = binding.Parameter.GroupName ?? paramName;
                if (state.on)
                {
                    if (unitValue.ContainsKey(paramName))
                    {
                        if (!Mathf.Approximately(unitValue[paramName], binding.Value) && unitConflictReported.Add(paramName))
                            messages.Add(ModelMessage.Error("ad.error.preset_group_conflict", preset.PlainName, unitLabel[paramName], component).For(preset.id));
                    }
                    else
                    {
                        unitValue[paramName] = binding.Value;
                    }
                }
                else
                {
                    unitHasOff.Add(paramName);
                }
            }

            foreach (var pair in unitValue) result.Add((pair.Key, pair.Value));
            foreach (var paramName in unitHasOff)
                if (!unitValue.ContainsKey(paramName)) result.Add((paramName, 0f));

            foreach (var named in preset.presetParameters) result.Add((named.name, named.value));

            return result;
        }

        /// <summary>True when the avatar already knows this parameter: its FX controller declares it (AvatarDoctor's own controls included, ensured there before a preset is built), its expression parameters declare it, or the plan is about to add it.</summary>
        static bool IsKnownParameter(string name, VirtualAnimatorController fx, VRCExpressionParameters expressionParameters, ParameterPlan plan)
        {
            if (fx.Parameters.ContainsKey(name)) return true;
            if (expressionParameters != null && expressionParameters.parameters != null)
                foreach (var p in expressionParameters.parameters)
                    if (p.name == name) return true;
            return plan.Parameters.Any(p => p.Name == name);
        }
    }
}
#endif
