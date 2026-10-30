#if DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
#if DOCME_NDMF
using nadena.dev.ndmf;
using VRC.SDK3.Avatars.Components;
#endif

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>A parameter the avatar already declares, either in its expression parameters or only inside the FX controller.</summary>
    public struct ExistingParameter
    {
        public PlannedType Type;
        /// <summary>True when the avatar declares this in VRCExpressionParameters. False means the name only exists in an animator.</summary>
        public bool IsExpressionParameter;
        public bool Saved;
        /// <summary>Other players see it: through the avatar's declaration, or, for a name that is not declared, because another tool reports it to NDMF as synced.</summary>
        public bool Synced;
        public float Default;
        /// <summary>Another tool's component on the avatar reports this name to NDMF, so something besides AvatarDoc uses it. VRChat's own components do not count (ParametersWriter.ReportedByAnotherTool).</summary>
        public bool UsedByAnotherTool;
    }

    public sealed class PlannedParameter
    {
        public string Name = "";
        public PlannedType Type;
        public float Default;
        public bool Saved;
        public bool Synced;
        /// <summary>The avatar already declares this name in its expression parameters.</summary>
        public bool ReusesExisting;
        /// <summary>The name exists in an animator only, so it must never be added to the expression parameters.</summary>
        public bool IsAnimatorOnly;
        /// <summary>A component declared this name itself, so no entry of ours reads it and nothing generated writes it.</summary>
        public bool Declared;
        /// <summary>The component this parameter belongs to when its controls do not say: the one whose declaration planned it, or the one an exclusive group belongs to.</summary>
        public AvatarDoc Component;
        /// <summary>A declaration of a name the avatar already has as an animator parameter, in its own FX controller or from another tool, so something of the avatar's own reads it at that type.</summary>
        public bool InAvatarsFx;
        /// <summary>The exclusive group's name, for the texts that show it. Null on every parameter that is not a group's.</summary>
        public string GroupName;
        /// <summary>The exclusive group this parameter is: its component and its name. None on every other parameter.</summary>
        public GroupKey Group;
        public List<ControlNode> Controls = new List<ControlNode>();

        public int Bits => IsAnimatorOnly || !Synced ? 0 : Type == PlannedType.Bool ? 1 : 8;
    }

    public sealed class ControlBinding
    {
        public ControlNode Control;
        public PlannedParameter Parameter;
        /// <summary>Value the menu writes for this control: 1 for a bool, the member value in a group, parameterValue for a custom int or float.</summary>
        public float Value;
        public bool OnByDefault;
        /// <summary>Radials only: the position the scene is set to before play, after any existing declaration had its say.</summary>
        public float DefaultValue;
    }

    public sealed class ParameterPlan
    {
        public List<PlannedParameter> Parameters = new List<PlannedParameter>();
        public Dictionary<ControlNode, ControlBinding> Bindings = new Dictionary<ControlNode, ControlBinding>();
        public List<ModelMessage> Messages = new List<ModelMessage>();

        /// <summary>Bits of the parameters the build will add to the expression parameters asset.</summary>
        public int NewSyncedBits => Parameters.Where(p => !p.ReusesExisting && !p.IsAnimatorOnly).Sum(p => p.Bits);
        public bool HasErrors => Messages.Any(m => m.Severity == MessageSeverity.Error);
    }

    public static class ParameterPlanner
    {
        public static string Sanitize(string name)
        {
            var s = (name ?? "").Trim().Replace('/', '_');
            return s.Length == 0 ? "Unnamed" : s;
        }

        /// <summary>Convenience overload for callers that only know the types of the avatar's declared parameters.</summary>
        public static ParameterPlan Plan(MenuTree tree, IReadOnlyDictionary<string, PlannedType> existingTypes)
        {
            var existing = new Dictionary<string, ExistingParameter>();
            foreach (var pair in existingTypes)
                existing[pair.Key] = new ExistingParameter { Type = pair.Value, IsExpressionParameter = true, Saved = true, Synced = true, Default = 0f };
            return Plan(tree, existing);
        }

        public static ParameterPlan Plan(MenuTree tree, IReadOnlyDictionary<string, ExistingParameter> existing)
        {
            var plan = new ParameterPlan();
            MenuModel.LeaveOutUnused(tree, existing);
            var byName = new Dictionary<string, PlannedParameter>();
            var groups = new Dictionary<GroupKey, PlannedParameter>();
            var reuseReported = new Dictionary<string, ModelMessage>();
            var animatorOnlyReported = new Dictionary<string, ModelMessage>();
            // Names another tool syncs, which AvatarDoc declares in that tool's type: the type may not widen.
            var sharedWithAnotherTool = new HashSet<string>();
            // Every automatic name before it is made unique, with the component that makes it.
            var automatic = new List<(string name, AvatarDoc component)>();
            string Automatic(AvatarDoc component, string name)
            {
                var wanted = component.parameterPrefix + "/" + Sanitize(name);
                automatic.Add((wanted, component));
                return UniqueName(wanted, existing, byName);
            }

            foreach (var control in tree.Controls)
            {
                // A switched off component is not built, so it takes no name, no value and no bits: the next
                // component's Hat is the first Hat, as it will be on the built avatar.
                if (control.Disabled) continue;
                var entry = control.Entry;
                if (control.Unused)
                {
                    plan.Messages.Add(ModelMessage.Info("ad.info.left_out_unused", entry.PlainName, entry.parameterName.Trim(), control.Component).For(entry.id));
                    continue;
                }
                // A grabbable with its own menu toggle off is not a menu control at all: nothing reads a
                // parameter for it, so it plans none and costs nothing, the same as any entry with no control.
                // With the toggle on it falls through to the generic branch below, which plans it a Bool exactly
                // like any other toggle.
                if (entry.kind == EntryKind.Grabbable && !entry.grabMenuToggle) continue;
                if (entry.kind == EntryKind.Preset)
                {
                    // Its own Bool, never saved or synced: the menu button needs a parameter to write, but a
                    // preset holds no state of its own worth keeping between sessions or across the network.
                    var presetName = Automatic(control.Component, entry.PlainName);
                    var presetParameter = new PlannedParameter { Name = presetName, Type = PlannedType.Bool, Saved = false, Synced = false };
                    byName[presetName] = presetParameter;
                    plan.Parameters.Add(presetParameter);
                    presetParameter.Controls.Add(control);
                    plan.Bindings[control] = new ControlBinding { Control = control, Parameter = presetParameter, Value = 1f, OnByDefault = false, DefaultValue = 0f };
                    continue;
                }
                var groupKey = tree.GroupOf(control);
                if (!groupKey.IsNone)
                {
                    // The group belongs to one component, which names it and keeps its settings, whichever of
                    // its members comes first: a member of another component joins it only through its link.
                    if (!groups.TryGetValue(groupKey, out var group))
                    {
                        var settings = tree.GroupSettings(groupKey);
                        group = new PlannedParameter
                        {
                            Name = Automatic(groupKey.Component, groupKey.Name),
                            Type = PlannedType.Int,
                            GroupName = groupKey.Name,
                            Group = groupKey,
                            Component = groupKey.Component,
                            Saved = settings?.saved ?? true,
                            Synced = settings?.synced ?? true,
                        };
                        groups[groupKey] = group;
                        byName[group.Name] = group;
                        plan.Parameters.Add(group);
                    }
                    group.Controls.Add(control);
                    plan.Bindings[control] = new ControlBinding { Control = control, Parameter = group };
                    continue;
                }

                bool custom = !string.IsNullOrWhiteSpace(entry.parameterName);
                var name = custom ? entry.parameterName.Trim() : Automatic(control.Component, entry.PlainName);
                var wantedType = entry.kind == EntryKind.Radial ? PlannedType.Float : (custom ? TypeForValue(entry.parameterValue) : PlannedType.Bool);
                float value = entry.kind == EntryKind.Radial ? 1f : (custom ? entry.parameterValue : 1f);

                if (!byName.TryGetValue(name, out var parameter))
                {
                    parameter = new PlannedParameter { Name = name, Type = wantedType };
                    if (custom && existing.TryGetValue(name, out var declared))
                    {
                        parameter.Type = declared.Type;
                        if (declared.IsExpressionParameter)
                        {
                            parameter.Default = declared.Default;
                            parameter.ReusesExisting = true;
                            parameter.Saved = declared.Saved;
                            parameter.Synced = declared.Synced;
                        }
                        else if (declared.Synced)
                        {
                            // Another tool syncs it, but may declare it only after Modular Avatar has cleared every
                            // menu parameter nothing declares yet (Skin Touch declares in Optimizing). So AvatarDoc
                            // declares it with the entry's settings, in the tool's type; a tool that declares it
                            // later finds it declared, and Modular Avatar merges its own declaration into it. It
                            // stays synced whatever the entry says: the tool needs it synced (Skin Touch leaves its
                            // toggle out when the declaration it finds is local only).
                            sharedWithAnotherTool.Add(name);
                            parameter.Synced = true;
                        }
                        else
                        {
                            parameter.Default = declared.Default;
                            parameter.IsAnimatorOnly = true;
                        }
                    }
                    byName[name] = parameter;
                    plan.Parameters.Add(parameter);
                }

                bool declaredElsewhere = parameter.ReusesExisting || parameter.IsAnimatorOnly;
                if (entry.kind == EntryKind.Radial)
                {
                    if (parameter.Type != PlannedType.Float)
                    {
                        plan.Messages.Add(ModelMessage.Error("ad.error.parameter_type_conflict", name, parameter.Type.ToString(), wantedType.ToString(), control.Component).For(entry.id).OnField("ad-parameter-name-field"));
                        continue;
                    }
                }
                else if (parameter.Controls.Any(c => c.Entry.kind == EntryKind.Radial))
                {
                    plan.Messages.Add(ModelMessage.Error("ad.error.parameter_type_conflict", name, parameter.Type.ToString(), wantedType.ToString(), control.Component).For(entry.id).OnField("ad-parameter-name-field"));
                    continue;
                }
                else if (declaredElsewhere || sharedWithAnotherTool.Contains(name))
                {
                    if (!CanHold(parameter.Type, value))
                    {
                        plan.Messages.Add(ModelMessage.Error("ad.error.parameter_type_conflict", name, parameter.Type.ToString(), wantedType.ToString(), control.Component).For(entry.id).OnField("ad-parameter-name-field"));
                        continue;
                    }
                }
                else
                {
                    parameter.Type = Widen(parameter.Type, value);
                }

                parameter.Controls.Add(control);
                bool onByDefault;
                if (declaredElsewhere)
                {
                    onByDefault = (entry.kind == EntryKind.Toggle && Mathf.Approximately(parameter.Default, value)) || entry.kind == EntryKind.Grabbable;
                    if (parameter.ReusesExisting && !reuseReported.ContainsKey(parameter.Name))
                    {
                        var reused = ModelMessage.Info("ad.info.parameter_reused", parameter.Name, control.Component);
                        reuseReported[parameter.Name] = reused;
                        plan.Messages.Add(reused);
                    }
                    else if (parameter.IsAnimatorOnly && !animatorOnlyReported.ContainsKey(parameter.Name))
                    {
                        var animatorOnly = ModelMessage.Info("ad.info.parameter_animator_only", parameter.Name, control.Component);
                        animatorOnlyReported[parameter.Name] = animatorOnly;
                        plan.Messages.Add(animatorOnly);
                    }
                }
                else
                {
                    parameter.Saved |= entry.saved;
                    parameter.Synced |= entry.synced;
                    // A grabbable's own menu toggle allows grabbing at all: it starts on, since the point of the
                    // toggle is to let a creator turn grabbing OFF when wanted, not to make a creator turn it on
                    // first.
                    onByDefault = (entry.kind == EntryKind.Toggle && entry.defaultOn) || entry.kind == EntryKind.Grabbable;
                    if (entry.kind == EntryKind.Radial) parameter.Default = entry.defaultValue;
                    else if (onByDefault) parameter.Default = value;
                }

                plan.Bindings[control] = new ControlBinding
                {
                    Control = control,
                    Parameter = parameter,
                    Value = value,
                    OnByDefault = onByDefault,
                    DefaultValue = entry.kind == EntryKind.Radial ? parameter.Default : 0f,
                };
            }

            // The message is raised on the first control that meets the name, but it speaks for every control that
            // ends up bound to the parameter, so its entries are named once the loop above has seen them all.
            foreach (var pair in reuseReported)
                pair.Value.For(byName[pair.Key].Controls.Select(c => c.Entry.id).ToArray());
            foreach (var pair in animatorOnlyReported)
                pair.Value.For(byName[pair.Key].Controls.Select(c => c.Entry.id).ToArray());

            foreach (var group in groups.Values) AssignGroupValues(plan, group);
            ReportClashingNames(tree, plan, automatic);
            PlanDeclarations(tree, plan, byName, existing);
            ReportUndeclaredAssetParameters(tree, plan, byName, existing);
            ReportUnreadParameters(tree, plan, existing);
            return plan;
        }

        /// <summary>
        /// A toggle or radial that only drives a parameter does nothing unless something else uses it: a merged
        /// animator, the avatar's own FX controller, or another tool's component that reports the name to NDMF (a
        /// tool that builds its own layer during the upload, which nothing can read before then). VRChat's own
        /// components do not count: the descriptor only declares names, contacts and PhysBones write theirs.
        /// </summary>
        static void ReportUnreadParameters(MenuTree tree, ParameterPlan plan, IReadOnlyDictionary<string, ExistingParameter> existing)
        {
            foreach (var control in tree.Controls)
            {
                var entry = control.Entry;
                if (control.Excluded || !entry.IsParameterControl) continue;
                var name = entry.parameterName.Trim();
                if (existing.TryGetValue(name, out var known) && known.UsedByAnotherTool) continue;
                if (HasAReader(tree, name)) continue;
                plan.Messages.Add(ModelMessage.Warning("ad.warn.parameter_control_unused", entry.PlainName, name, control.Component).For(entry.id));
            }
        }

        /// <summary>Without NDMF there is no build to read the parameter, so nothing does.</summary>
        static bool HasAReader(MenuTree tree, string name)
        {
#if DOCME_NDMF
            return MergeBuilder.UsesParameter(tree, tree.AvatarRoot, name);
#else
            return false;
#endif
        }

        /// <summary>
        /// Automatic names two components would both make: two copies of one prefab, an older prefab on AD beside
        /// the avatar's own AD, two groups of one name and prefix. The build still tells them apart, with a number
        /// in build order, but then adding, removing or moving a component can hand a saved value to another
        /// toggle, so each pair of components hears of it once, before upload. Args: the component a fix changes
        /// (MenuModel.TheOneToChange), the other one, the names.
        /// </summary>
        static void ReportClashingNames(MenuTree tree, ParameterPlan plan, List<(string name, AvatarDoc component)> automatic)
        {
            var clashes = new Dictionary<(AvatarDoc change, AvatarDoc keep), List<string>>();
            var order = new List<(AvatarDoc change, AvatarDoc keep)>();
            foreach (var same in automatic.GroupBy(a => a.name))
            {
                var makers = same.Select(a => a.component).Distinct().ToList();
                for (int i = 0; i < makers.Count; i++)
                    for (int j = i + 1; j < makers.Count; j++)
                    {
                        var change = MenuModel.TheOneToChange(tree, makers[i], makers[j]);
                        var pair = (change, change == makers[i] ? makers[j] : makers[i]);
                        if (!clashes.TryGetValue(pair, out var names))
                        {
                            clashes[pair] = names = new List<string>();
                            order.Add(pair);
                        }
                        if (!names.Contains(same.Key)) names.Add(same.Key);
                    }
            }
            foreach (var pair in order)
            {
                var names = clashes[pair];
                plan.Messages.Add(ModelMessage.Warning(names.Count == 1 ? "ad.warn.prefix_clash.one" : "ad.warn.prefix_clash", pair.change, pair.keep, L.JoinList(names)));
            }
        }

        /// <summary>The id a declaration's messages are filed under, so the row of that declaration can show them.</summary>
        public static string DeclarationId(string name) => "parameter:" + (name ?? "").Trim();

        /// <summary>
        /// The parameters the components declare themselves, planned after the entries in list order. Nothing
        /// about a declaration depends on what the current platform builds, so both platforms declare the same
        /// list. A name an entry already plans belongs to that entry; one the avatar already declares is reused.
        /// </summary>
        static void PlanDeclarations(MenuTree tree, ParameterPlan plan, Dictionary<string, PlannedParameter> byName,
            IReadOnlyDictionary<string, ExistingParameter> existing)
        {
            foreach (var component in tree.Components)
            {
                if (component == null || MenuModel.IsDisabled(component)) continue;
                foreach (var declaration in component.parameters)
                {
                    if (declaration == null) continue;
                    var name = (declaration.name ?? "").Trim();
                    if (name.Length == 0) continue;
                    if (byName.ContainsKey(name))
                    {
                        plan.Messages.Add(ModelMessage.Warning("ad.warn.parameter_declared_twice", name, component).For(DeclarationId(name)));
                        continue;
                    }

                    var planned = new PlannedParameter
                    {
                        Name = name,
                        Type = declaration.type,
                        Default = declaration.defaultValue,
                        Saved = declaration.saved,
                        Synced = declaration.synced,
                        Declared = true,
                        Component = component,
                    };
                    if (existing.TryGetValue(name, out var declared) && declared.IsExpressionParameter)
                    {
                        if (declared.Type != declaration.type)
                        {
                            plan.Messages.Add(ModelMessage.Error("ad.error.parameter_declared_type", name, declaration.type.ToString(), declared.Type.ToString(), component).For(DeclarationId(name)));
                            continue;
                        }
                        planned.ReusesExisting = true;
                        planned.Default = declared.Default;
                        planned.Saved = declared.Saved;
                        planned.Synced = declared.Synced;
                    }
                    // Only the avatar's own FX declares it: the declaration still adds it to the expression
                    // parameters, but the avatar's own layers keep the type they read it at.
                    else if (existing.ContainsKey(name))
                        planned.InAvatarsFx = true;
                    byName[name] = planned;
                    plan.Parameters.Add(planned);
                }
            }
        }

        /// <summary>
        /// Controls of an installed menu asset whose parameter nobody provides. VRChat ignores a menu control whose
        /// parameter is not in the expression parameters, so each such name is said once, with the install it came
        /// from named, and the panel offers to declare it.
        /// </summary>
        static void ReportUndeclaredAssetParameters(MenuTree tree, ParameterPlan plan, Dictionary<string, PlannedParameter> byName,
            IReadOnlyDictionary<string, ExistingParameter> existing)
        {
            if (tree.MenuAssets.Count == 0) return;
            var reported = new HashSet<string>();
            foreach (var node in tree.AllNodes())
                foreach (var item in node.Items)
                {
                    if (item.Asset == null || item.Existing == null) continue;
                    // Hidden, removed or left out on this platform: the control is never written, so a parameter
                    // it would have needed is nobody's problem.
                    if (item.Hidden || item.Excluded) continue;
                    foreach (var name in ControlParameters(item.Existing))
                    {
                        if (byName.ContainsKey(name) || existing.ContainsKey(name)) continue;
                        if (TriggerPlanner.BuiltInType(name).HasValue) continue;
                        if (!reported.Add(name)) continue;
                        var owner = tree.MenuAssets.FirstOrDefault(a => a.Install == item.Asset);
                        plan.Messages.Add(ModelMessage.Warning("ad.warn.parameter_undeclared", name, item.Name, owner != null ? owner.Component : null)
                            .For(item.Asset.id));
                    }
                }
        }

        /// <summary>The parameter names one menu control drives: its own and its sub parameters.</summary>
        static IEnumerable<string> ControlParameters(VRCExpressionsMenu.Control control)
        {
            if (control.parameter != null && !string.IsNullOrWhiteSpace(control.parameter.name)) yield return control.parameter.name.Trim();
            if (control.subParameters == null) yield break;
            foreach (var sub in control.subParameters)
                if (sub != null && !string.IsNullOrWhiteSpace(sub.name)) yield return sub.name.Trim();
        }

#if DOCME_NDMF
        /// <summary>Splits the avatar's synced bit budget into what everything else uses and what AvatarDoctor would add on top.</summary>
        public static (int avatarBits, int avatarDoctorBits) CountBits(Transform avatarRoot)
        {
            if (avatarRoot == null) return (0, 0);

            var provided = AvatarPlanCache.ProvidedFor(avatarRoot);
            var components = MenuModel.CollectComponents(avatarRoot);
            if (components.Count == 0) return (OtherToolsBits(provided, null), 0);

            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            var tree = MenuModel.Build(avatarRoot, descriptor != null ? descriptor.expressionsMenu : null, components);
            var existing = ParametersWriter.ExistingParameters(descriptor != null ? descriptor.expressionParameters : null, ParametersWriter.FxOf(descriptor));
            ParametersWriter.AddProvided(existing, provided);
            var plan = Plan(tree, existing);
            // Bits a removed control frees were counted as the avatar's, so they come off that side of the bar.
            tree.Removal = RemovalPlanner.Plan(tree, descriptor);
            return (Mathf.Max(0, OtherToolsBits(provided, plan) - tree.Removal.FreedBits), plan.NewSyncedBits);
        }

        /// <summary>
        /// The synced bits everything but AvatarDoc reports to NDMF. A name AvatarDoc declares itself (a toggle
        /// driving a parameter another tool syncs) is counted on AvatarDoc's side only: the avatar ends up with one
        /// parameter of that name, not two.
        /// </summary>
        public static int OtherToolsBits(IEnumerable<ProvidedParameter> provided, ParameterPlan plan)
        {
            var own = plan != null
                ? new HashSet<string>(plan.Parameters.Where(p => !p.ReusesExisting && !p.IsAnimatorOnly && p.Bits > 0).Select(p => p.Name))
                : new HashSet<string>();
            return provided.Where(p => p != null && !(p.Source is AvatarDoc) && !own.Contains(p.EffectiveName)).Sum(p => p.BitUsage);
        }
#endif

        static void AssignGroupValues(ParameterPlan plan, PlannedParameter group)
        {
            var used = new HashSet<int>();
            var fallBackToAutomatic = new HashSet<ControlNode>();
            foreach (var member in group.Controls)
            {
                int explicitValue = member.Entry.groupValue;
                if (explicitValue <= 0) continue;
                if (!used.Add(explicitValue))
                {
                    plan.Messages.Add(ModelMessage.Error("ad.error.group_value_conflict", group.GroupName, explicitValue.ToString(), member.Component).For(member.Entry.id));
                    fallBackToAutomatic.Add(member);
                    continue;
                }
                plan.Bindings[member].Value = explicitValue;
            }
            // The group's own component's toggles are numbered first, so a toggle of another component joining the
            // group never moves the numbers of the toggles that were in it, whatever their order in the menu.
            int next = 1;
            foreach (var member in group.Controls.OrderBy(m => m.Component == group.Component ? 0 : 1))
            {
                if (member.Entry.groupValue > 0 && !fallBackToAutomatic.Contains(member)) continue;
                while (used.Contains(next)) next++;
                used.Add(next);
                plan.Bindings[member].Value = next;
            }

            ControlNode defaultMember = null;
            foreach (var member in group.Controls)
            {
                if (!member.Entry.defaultOn) continue;
                if (defaultMember == null) defaultMember = member;
                else plan.Messages.Add(ModelMessage.Warning("ad.warn.group_multiple_defaults", group.GroupName, defaultMember.Entry.PlainName, member.Component).For(member.Entry.id));
            }
            group.Default = defaultMember == null ? 0f : plan.Bindings[defaultMember].Value;
            // The Int still selects the excluded member's value and no state carries it here, so the group's Off
            // state is what shows: no outfit, and the pieces the outfits hide stay visible.
            if (defaultMember != null && defaultMember.Excluded)
                plan.Messages.Add(ModelMessage.Info("ad.info.group_default_excluded", group.GroupName, defaultMember.Component).For(defaultMember.Entry.id));
            foreach (var member in group.Controls)
                plan.Bindings[member].OnByDefault = member == defaultMember;
        }

        static PlannedType TypeForValue(float value)
        {
            if (Mathf.Approximately(value, 1f)) return PlannedType.Bool;
            return IsWhole(value) ? PlannedType.Int : PlannedType.Float;
        }

        /// <summary>Grows a planned type so it can carry one more control's value. Types never shrink.</summary>
        static PlannedType Widen(PlannedType type, float value)
        {
            if (!IsWhole(value)) return PlannedType.Float;
            if (type == PlannedType.Bool && !Mathf.Approximately(value, 1f)) return PlannedType.Int;
            return type;
        }

        static bool CanHold(PlannedType type, float value)
        {
            switch (type)
            {
                case PlannedType.Bool: return Mathf.Approximately(value, 0f) || Mathf.Approximately(value, 1f);
                case PlannedType.Int: return IsWhole(value);
                default: return true;
            }
        }

        static bool IsWhole(float value) => Mathf.Approximately(value, Mathf.Round(value));

        static string UniqueName(string baseName, IReadOnlyDictionary<string, ExistingParameter> existing, Dictionary<string, PlannedParameter> byName)
        {
            var candidate = baseName;
            for (int i = 2; existing.ContainsKey(candidate) || byName.ContainsKey(candidate); i++)
                candidate = baseName + " " + i;
            return candidate;
        }
    }
}
#endif
