#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>One FX layer per preset: Idle and Apply, with a parameter driver on Apply that sets every state and named parameter the preset names.</summary>
    public static class PresetBuilder
    {
        /// <summary>
        /// Builds every preset's layer. alsoKnown names the parameters the avatar's other tools provide, so a preset
        /// that sets one of them by name is not warned about.
        /// </summary>
        public static List<ModelMessage> Build(AnimatorServicesContext asc, MenuTree tree, ParameterPlan plan, ICollection<string> pinLayerNames = null, IEnumerable<string> alsoKnown = null)
        {
            var messages = new List<ModelMessage>();
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return messages;
            bool writeDefaults = AnimatorBuilder.DecideWriteDefaults(fx, pinLayerNames);
            var descriptor = tree.AvatarRoot != null ? tree.AvatarRoot.GetComponent<VRCAvatarDescriptor>() : null;
            var expressionParameters = descriptor != null ? descriptor.expressionParameters : null;

            var known = new HashSet<string>(fx.Parameters.Keys);
            if (expressionParameters != null && expressionParameters.parameters != null)
                foreach (var p in expressionParameters.parameters)
                    if (p != null && !string.IsNullOrEmpty(p.name)) known.Add(p.name);
            if (alsoKnown != null) known.UnionWith(alsoKnown);
            messages.AddRange(Check(tree, plan, known));

            foreach (var control in tree.Controls)
            {
                var entry = control.Entry;
                if (entry.kind != EntryKind.Preset) continue;
                // Its parameter stays in the plan so both platforms declare the same list; only the layer goes.
                if (control.Excluded) continue;
                if (!plan.Bindings.TryGetValue(control, out var binding)) continue;

                AnimatorBuilder.EnsureParameter(fx, binding.Parameter.Name, binding.Parameter.Type, binding.Parameter.Default);

                var operations = Operations(entry, tree, plan, new List<ModelMessage>());

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
            return messages;
        }

        /// <summary>
        /// What the presets say before anything is built: two members of one exclusive group turned on at once, an
        /// error, and an Other parameter nothing knows, a warning. A parameter is known when the plan adds it, when
        /// known names it (the avatar's FX controller, its expression parameters, its other tools), or when VRCFury
        /// adds it later in the build under its global name. The build reports these, and the inspector shows the
        /// same before an upload. A missing control is the model's to report, so it is not said again here.
        /// </summary>
        public static List<ModelMessage> Check(MenuTree tree, ParameterPlan plan, ICollection<string> known)
        {
            var messages = new List<ModelMessage>();
            HashSet<string> vrcFuryGlobals = null;
            bool Knows(string name) => known.Contains(name) || plan.Parameters.Any(p => p.Name == name)
                || (vrcFuryGlobals ?? (vrcFuryGlobals = new HashSet<string>(VrcFuryReader.GlobalParameters(tree.AvatarRoot)))).Contains(name);

            foreach (var control in tree.Controls)
            {
                var entry = control.Entry;
                if (entry.kind != EntryKind.Preset || control.Excluded || !plan.Bindings.ContainsKey(control)) continue;
                var component = tree.Components.FirstOrDefault(c => c.entries.Contains(entry));
                foreach (var op in Operations(entry, tree, plan, messages))
                    if (!Knows(op.parameter))
                        messages.Add(ModelMessage.Warning("ad.warn.preset_parameter_unknown", op.parameter, entry.PlainName, component).For(entry.id));
            }
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
                // A row that never had a control sets nothing; the model already asks for one.
                if (string.IsNullOrEmpty(state.entryId)) continue;
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

    }
}
#endif
