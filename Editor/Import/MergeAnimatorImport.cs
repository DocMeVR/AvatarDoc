#if DOCME_NDMF && DOCME_VRCSDK3A
#if DOCME_MA
using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Converts the menu items of a Modular Avatar Merge Animator. Each item's layer is read with the same strict
    /// rules as a hand made FX layer: what reads cleanly becomes a real entry with effects of its own, the rest
    /// becomes a parameter control on top of the controller, which AvatarDoc then merges itself.
    /// </summary>
    public static class MergeAnimatorImport
    {
        /// <summary>True when this menu item's effects come from a Merge Animator, so this importer owns it rather than the reactive component importer.</summary>
        public static bool Claims(ModularAvatarMenuItem item) => MergeFor(item) != null;

        /// <summary>
        /// The Merge Animator whose controller actually drives this item: the nearest one above it that merges
        /// into FX and declares the parameter the item names. An item that carries reactive components of its own
        /// belongs to the reactive importer whatever sits above it.
        /// </summary>
        static ModularAvatarMergeAnimator MergeFor(ModularAvatarMenuItem item)
        {
            if (item == null || item.Control == null) return null;
            if (HasReactiveComponent(item)) return null;
            var names = ParameterNamesOf(item.Control);
            if (names.Count == 0) return null;
            for (var t = item.transform; t != null; t = t.parent)
            {
                foreach (var merge in t.GetComponents<ModularAvatarMergeAnimator>())
                {
                    if (merge.layerType != VRCAvatarDescriptor.AnimLayerType.FX || merge.animator == null) continue;
                    var declared = MergeBuilder.ParametersOf(merge.animator);
                    if (names.Any(name => declared.Any(p => p.name == name))) return merge;
                }
            }
            return null;
        }

        /// <summary>The submenu items this Merge Animator installs: they hold no parameter of their own, so they are found by the hierarchy rather than by what they drive.</summary>
        static IEnumerable<ModularAvatarMenuItem> SubmenuItemsOf(ModularAvatarMergeAnimator merge)
        {
            foreach (var item in merge.GetComponentsInChildren<ModularAvatarMenuItem>(true))
            {
                if (item.Control == null || item.Control.type != VRCExpressionsMenu.Control.ControlType.SubMenu) continue;
                if (NearestMerge(item.transform) != merge) continue;
                yield return item;
            }
        }

        static ModularAvatarMergeAnimator NearestMerge(Transform start)
        {
            for (var t = start; t != null; t = t.parent)
            {
                foreach (var merge in t.GetComponents<ModularAvatarMergeAnimator>())
                    if (merge.layerType == VRCAvatarDescriptor.AnimLayerType.FX && merge.animator != null) return merge;
            }
            return null;
        }

        static bool HasReactiveComponent(ModularAvatarMenuItem item)
        {
            return ReactiveComponentsOf<ModularAvatarObjectToggle>(item).Any()
                || ReactiveComponentsOf<ModularAvatarShapeChanger>(item).Any()
                || ReactiveComponentsOf<ModularAvatarMaterialSetter>(item).Any();
        }

        /// <summary>Reactive components on the item's own object or below it, whose nearest Modular Avatar menu item ancestor is this one.</summary>
        static IEnumerable<T> ReactiveComponentsOf<T>(ModularAvatarMenuItem item) where T : Component
        {
            foreach (var component in item.GetComponentsInChildren<T>(true))
            {
                var nearest = component.GetComponentsInParent<ModularAvatarMenuItem>(true).FirstOrDefault();
                if (nearest == item) yield return component;
            }
        }

        static List<string> ParameterNamesOf(VRCExpressionsMenu.Control control)
        {
            var result = new List<string>();
            if (control.parameter != null && !string.IsNullOrEmpty(control.parameter.name)) result.Add(control.parameter.name);
            if (control.subParameters != null)
                foreach (var sub in control.subParameters)
                    if (sub != null && !string.IsNullOrEmpty(sub.name)) result.Add(sub.name);
            return result;
        }

        public static List<ImportCandidate> Scan(Transform avatarRoot, MenuTree tree, ParameterUsage usage = null)
        {
            var result = new List<ImportCandidate>();
            if (avatarRoot == null) return result;

            var byOrigin = new Dictionary<Component, ForeignItem>();
            foreach (var foreign in ForeignMenu.Read(avatarRoot, tree != null ? tree.Root : null))
                if (foreign.Origin != null && !byOrigin.ContainsKey(foreign.Origin)) byOrigin[foreign.Origin] = foreign;

            var owned = new Dictionary<ModularAvatarMergeAnimator, List<ModularAvatarMenuItem>>();
            foreach (var item in avatarRoot.GetComponentsInChildren<ModularAvatarMenuItem>(true))
            {
                var merge = MergeFor(item);
                if (merge == null) continue;
                if (!owned.TryGetValue(merge, out var list)) owned[merge] = list = new List<ModularAvatarMenuItem>();
                list.Add(item);
            }
            if (owned.Count == 0) return result;

            bool layerControlUnsafe = LayerControlUnsafe(avatarRoot);
            var reading = usage ?? ParameterUsage.For(avatarRoot, tree);
            var itemsByParameter = ItemsByParameter(avatarRoot);
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            var existing = ParametersWriter.ExistingParameters(descriptor != null ? descriptor.expressionParameters : null, (AnimatorController)null);
            foreach (var pair in owned)
                result.AddRange(ScanOne(avatarRoot, pair.Key, pair.Value, byOrigin, layerControlUnsafe, reading, itemsByParameter, existing));
            return result;
        }

        /// <summary>Every Modular Avatar menu item of the avatar that names each parameter, which is the set its build reads saved, synced and the default from when nothing declares it.</summary>
        static Dictionary<string, List<ModularAvatarMenuItem>> ItemsByParameter(Transform avatarRoot)
        {
            var result = new Dictionary<string, List<ModularAvatarMenuItem>>();
            foreach (var item in avatarRoot.GetComponentsInChildren<ModularAvatarMenuItem>(true))
            {
                if (item.Control == null) continue;
                foreach (var name in ParameterNamesOf(item.Control))
                {
                    if (!result.TryGetValue(name, out var list)) result[name] = list = new List<ModularAvatarMenuItem>();
                    list.Add(item);
                }
            }
            return result;
        }

        static List<ImportCandidate> ScanOne(Transform avatarRoot, ModularAvatarMergeAnimator merge,
            List<ModularAvatarMenuItem> items, IReadOnlyDictionary<Component, ForeignItem> byOrigin, bool layerControlUnsafe,
            ParameterUsage usage, IReadOnlyDictionary<string, List<ModularAvatarMenuItem>> itemsByParameter,
            IReadOnlyDictionary<string, ExistingParameter> existing)
        {
            var group = new List<ImportCandidate>();
            var controller = MergeBuilder.Resolve(merge.animator);
            if (controller == null) return group;

            var configs = new Dictionary<string, ParameterConfig>();
            var prefixes = new List<ParameterConfig>();
            ReadParameters(avatarRoot, merge, configs, prefixes);
            Func<string, ParameterConfig?> declared = name => Declaration(configs, prefixes, name);

            string blocked = null;
            if (layerControlUnsafe)
                blocked = L.Text("ad.import.reason.layer_control");
            else if (MenuModel.IsOutsideAvatar(avatarRoot, PathRootOf(merge)))
                blocked = string.Format(L.Text("ad.error.merge_outside_avatar"), merge.animator.name, merge.gameObject.name);
            else if (RenamedParameter(controller, declared, out string renamed))
                blocked = string.Format(L.Text("ad.import.reason.ma_renamed_parameters"), renamed);

            var leaves = items.Where(i => i.Control.type != VRCExpressionsMenu.Control.ControlType.SubMenu && byOrigin.ContainsKey(i)).ToList();
            var parameters = leaves.ToDictionary(i => i, i => ParameterOf(i.Control));
            // Two controls on one parameter would leave one of them driving a layer the other took away.
            if (blocked == null && parameters.Values.Where(p => !string.IsNullOrEmpty(p)).GroupBy(p => p).Any(g => g.Count() > 1))
                blocked = L.Text("ad.import.reason.shared_parameter_controls");

            var resolve = PathResolver(avatarRoot, merge);
            var facts = leaves.ToDictionary(i => i, i => FactsFor(i, parameters[i], IsRadial(i.Control), declared, existing, itemsByParameter));
            // Everything this set installs goes away with it, so none of it counts as another reader of the name.
            // A menu installer on the same object whose finished menu the takeover claims comes over with the set,
            // and a control of that menu on one of these names is replaced by the entry rather than left behind.
            // Any other installer keeps installing its menu, so its controls stay readers.
            var ours = new List<object> { merge };
            ours.AddRange(items);
            ours.AddRange(merge.GetComponents<ModularAvatarMenuInstaller>().Where(ClaimedByTakeover).Cast<object>());
            var ignore = ours.ToArray();
            // A parameter declaration beside the merge goes with the set only for the names the merged controller
            // declares: those are the ones CleanupComponents takes out. A declaration it leaves behind keeps
            // costing its bits, so for any other name it still counts as a reader.
            var declaringComponents = merge.GetComponents<ModularAvatarParameters>().Cast<object>().ToArray();
            var declaredByController = new HashSet<string>(controller.parameters.Select(p => p.name));
            var verdicts = new Dictionary<ModularAvatarMenuItem, LayerVerdict>();
            if (blocked == null)
                foreach (var item in leaves)
                {
                    var name = parameters[item];
                    var ignoreForItem = !string.IsNullOrEmpty(name) && declaredByController.Contains(name)
                        ? ignore.Concat(declaringComponents).ToArray()
                        : ignore;
                    verdicts[item] = Analyse(usage, ignoreForItem, controller, item, name, facts[item], resolve, avatarRoot);
                }

            // Two items whose parameters lead to one layer: converting both would drop the layer twice over.
            var claimedLayers = verdicts.Values.Where(v => v != null && v.Convertible).Select(v => v.LayerName).ToList();
            if (blocked == null && claimedLayers.Count != claimedLayers.Distinct().Count())
                blocked = L.Text("ad.import.reason.shared_parameter_controls");

            string groupId = "merge:" + merge.GetInstanceID();
            var convertedLayers = new List<string>();
            foreach (var item in leaves)
            {
                var verdict = blocked == null && verdicts.TryGetValue(item, out var found) ? found : null;
                group.Add(BuildItem(item, byOrigin[item], facts[item], parameters[item], groupId, blocked, verdict, convertedLayers));
            }
            if (group.Count == 0) return group;

            var cleanup = CleanupComponents(merge, SubmenuItemsOf(merge), controller, out var trims);
            if (blocked == null && convertedLayers.Count < controller.layers.Length)
            {
                var draft = AnimatorMerge.Create(merge.animator);
                draft.pathRoot = merge.pathMode == MergeAnimatorPathMode.Relative ? PathRootOf(merge) : null;
                draft.matchWriteDefaults = merge.matchAvatarWriteDefaults;
                draft.skipLayers = new List<string>(convertedLayers);
                group.Add(new ImportCandidate
                {
                    Source = ImportSource.MergeAnimator,
                    Group = groupId,
                    Supported = true,
                    MergeDraft = draft,
                    OriginComponents = cleanup,
                    Trims = trims,
                });
            }
            else if (blocked == null)
            {
                // Every layer became an entry, so there is nothing left to merge. The components that installed the
                // set still have to go with it, so they ride along with the first item instead.
                group[0].OriginComponents.AddRange(cleanup);
                group[0].Trims.AddRange(trims);
            }

            foreach (var candidate in group) candidate.GroupMembers = group;
            return group;
        }

        /// <summary>
        /// Reads one item's layer, and refuses a real conversion when the old parameter name is still needed
        /// somewhere on the avatar: a converted entry gets a parameter of its own, so the old name would vanish
        /// from under whatever else was reading it.
        /// </summary>
        static LayerVerdict Analyse(ParameterUsage usage, object[] ignore, AnimatorController controller,
            ModularAvatarMenuItem item, string parameter, ParameterFacts facts, Func<string, string> resolve, Transform avatarRoot)
        {
            if (string.IsNullOrEmpty(parameter)) return null;
            bool radial = IsRadial(item.Control);
            // A Button is momentary, which an entry cannot reproduce, so its layer is never read.
            if (!radial && item.Control.type != VRCExpressionsMenu.Control.ControlType.Toggle) return null;

            if (usage != null && usage.UsedBesides(parameter, ignore))
            {
                var blockedVerdict = new LayerVerdict();
                blockedVerdict.Reason = string.Format(L.Text("ad.import.reason.fx_parameter_shared"), parameter);
                return blockedVerdict;
            }

            var verdict = radial
                ? LayerAnalysis.Radial(controller, parameter, resolve, avatarRoot)
                : LayerAnalysis.Toggle(controller, parameter, item.Control.value, resolve, avatarRoot, facts.DefaultOn);
            // The layer that drives the name stays merged while the entry moves to a parameter of its own, so
            // the driver would be writing a name nothing answers any more. The item keeps the old name instead.
            if (verdict != null && verdict.Convertible && verdict.DrivenElsewhere)
            {
                verdict.Convertible = false;
                verdict.Reason = string.Format(L.Text("ad.import.reason.driven_elsewhere"), parameter);
            }
            return verdict;
        }

        static bool IsRadial(VRCExpressionsMenu.Control control) => control.type == VRCExpressionsMenu.Control.ControlType.RadialPuppet;

        /// <summary>The two shapes PrefabTakeover.Scan takes a finished menu over from: an installer with no Menu Item that appends one, or a Menu Item whose submenu comes from one.</summary>
        static bool ClaimedByTakeover(ModularAvatarMenuInstaller installer)
        {
            var item = installer.GetComponent<ModularAvatarMenuItem>();
            if (item == null) return installer.menuToAppend != null;
            return item.Control != null
                && item.Control.type == VRCExpressionsMenu.Control.ControlType.SubMenu
                && item.MenuSource == SubmenuSource.MenuAsset
                && item.Control.subMenu != null;
        }

        /// <summary>What the avatar says a menu item's parameter is worth: whether it is saved and synced, and where it starts.</summary>
        struct ParameterFacts
        {
            public bool Saved;
            public bool Synced;
            public bool DefaultOn;
            public float DefaultValue;
        }

        /// <summary>
        /// Saved, synced and the default of one item's parameter, in the one order that holds on both import
        /// paths: a Modular Avatar Parameters declaration first, then the avatar's own expression parameters,
        /// then the menu items themselves. A parameter nothing declares is still created by Modular Avatar out
        /// of the items that name it, so the flags come from there and not from the controller.
        /// </summary>
        static ParameterFacts FactsFor(ModularAvatarMenuItem item, string parameter, bool radial,
            Func<string, ParameterConfig?> declared, IReadOnlyDictionary<string, ExistingParameter> existing,
            IReadOnlyDictionary<string, List<ModularAvatarMenuItem>> itemsByParameter)
        {
            var facts = new ParameterFacts { Saved = item.isSaved, Synced = item.isSynced };
            if (string.IsNullOrEmpty(parameter)) return facts;

            List<ModularAvatarMenuItem> naming = null;
            if (itemsByParameter != null) itemsByParameter.TryGetValue(parameter, out naming);
            if (naming != null && naming.Count > 0)
            {
                facts.Saved = naming.Any(i => i.isSaved);
                facts.Synced = naming.Any(i => i.isSynced);
            }

            var config = declared(parameter);
            if (config.HasValue)
            {
                facts.Saved = config.Value.saved;
                facts.Synced = config.Value.syncType != ParameterSyncType.NotSynced && !config.Value.localOnly;
                facts.DefaultOn = config.Value.defaultValue > 0.5f;
                facts.DefaultValue = Mathf.Clamp01(config.Value.defaultValue);
                return facts;
            }

            if (existing != null && existing.TryGetValue(parameter, out var onTheAvatar) && onTheAvatar.IsExpressionParameter)
            {
                facts.Saved = onTheAvatar.Saved;
                facts.Synced = onTheAvatar.Synced;
                facts.DefaultOn = radial ? onTheAvatar.Default > 0.5f : Mathf.Approximately(onTheAvatar.Default, item.Control.value);
                facts.DefaultValue = Mathf.Clamp01(onTheAvatar.Default);
                return facts;
            }

            var defaultItem = naming != null ? naming.FirstOrDefault(i => i.isDefault) : null;
            facts.DefaultOn = defaultItem != null;
            facts.DefaultValue = defaultItem != null ? Mathf.Clamp01(defaultItem.Control.value) : 0f;
            return facts;
        }

        /// <summary>True when anything on the avatar drives FX layer weights by position, which makes leaving any FX layer out unsafe.</summary>
        static bool LayerControlUnsafe(Transform avatarRoot)
        {
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (FxLayerImport.AnyLayerControlTargetsFx(descriptor)) return true;
            foreach (var merge in avatarRoot.GetComponentsInChildren<ModularAvatarMergeAnimator>(true))
                if (LayerAnalysis.UsesLayerControl(MergeBuilder.Resolve(merge.animator))) return true;
            foreach (var component in avatarRoot.GetComponentsInChildren<AvatarDoc>(true))
                foreach (var merge in component.animators)
                    if (merge != null && LayerAnalysis.UsesLayerControl(MergeBuilder.Resolve(merge.controller))) return true;
            return false;
        }

        static string ParameterOf(VRCExpressionsMenu.Control control)
        {
            if (control.type == VRCExpressionsMenu.Control.ControlType.RadialPuppet)
                return control.subParameters != null && control.subParameters.Length > 0 && control.subParameters[0] != null ? control.subParameters[0].name : "";
            return control.parameter != null ? control.parameter.name : "";
        }

        static ImportCandidate BuildItem(ModularAvatarMenuItem item, ForeignItem foreign, ParameterFacts facts,
            string parameter, string groupId, string blocked, LayerVerdict verdict, List<string> convertedLayers)
        {
            var control = item.Control;
            var candidate = new ImportCandidate
            {
                Source = ImportSource.ModularAvatar,
                Group = groupId,
                Path = foreign.Path.Count > 0 ? foreign.Path.Take(foreign.Path.Count - 1).ToList() : new List<string>(),
                OriginComponents = new List<Component> { item },
            };
            var installer = item.GetComponent<ModularAvatarMenuInstaller>();
            if (installer != null && installer.menuToAppend == null) candidate.OriginComponents.Add(installer);

            bool radial = IsRadial(control);
            bool toggleLike = control.type == VRCExpressionsMenu.Control.ControlType.Toggle
                || control.type == VRCExpressionsMenu.Control.ControlType.Button;
            var draft = MenuEntry.Create(radial ? EntryKind.Radial : EntryKind.Toggle, foreign.RichName);
            draft.icon = control.icon;
            candidate.Draft = draft;

            if (!radial && !toggleLike)
            {
                candidate.Supported = false;
                candidate.Reason = L.Text("ad.import.reason.not_a_toggle");
                return candidate;
            }

            draft.saved = facts.Saved;
            draft.synced = facts.Synced;
            if (radial) draft.defaultValue = facts.DefaultValue;
            else draft.defaultOn = facts.DefaultOn;

            if (blocked != null)
            {
                draft.parameterName = parameter;
                candidate.Supported = false;
                candidate.Reason = blocked;
                return candidate;
            }
            if (string.IsNullOrEmpty(parameter))
            {
                candidate.Supported = false;
                candidate.Reason = L.Text("ad.import.reason.no_supported_action");
                return candidate;
            }

            if (verdict != null && verdict.Convertible)
            {
                // Read before the targets are copied in: kept as it is, this item stays a plain control on the
                // parameter the merged layer already reads.
                candidate.KeepDraft = ParameterControlOf(draft, parameter, radial, control.value);
                candidate.MergedParameter = parameter;
                LayerAnalysis.CopyTargets(verdict.Draft, draft);
                if (verdict.Inverted) draft.defaultOn = true;
                candidate.Notes.AddRange(verdict.Notes);
                candidate.Notes.Add(string.Format(L.Text("ad.import.note.merge_converted"), verdict.LayerName));
                candidate.Notes.Add(L.Text("ad.import.note.osc"));
                candidate.MergedLayer = verdict.LayerName;
                convertedLayers.Add(verdict.LayerName);
                candidate.Supported = true;
                return candidate;
            }

            draft.parameterName = parameter;
            if (!radial) draft.parameterValue = control.value;
            candidate.Notes.Add(string.Format(L.Text("ad.import.note.merge_parameter_control"),
                parameter, verdict != null && !string.IsNullOrEmpty(verdict.Reason) ? verdict.Reason : L.Text("ad.import.note.merge_button")));
            candidate.Supported = true;
            return candidate;
        }

        /// <summary>A copy of a drafted entry that drives the old parameter and nothing else, for a group the creator keeps as it is.</summary>
        static MenuEntry ParameterControlOf(MenuEntry draft, string parameter, bool radial, float value)
        {
            var copy = JsonUtility.FromJson<MenuEntry>(JsonUtility.ToJson(draft));
            copy.parameterName = parameter;
            if (!radial) copy.parameterValue = value;
            return copy;
        }

        /// <summary>
        /// The components that exist only to install this set: the Merge Animator, the submenu items and their
        /// installers. A parameter declaration beside it goes only when every entry in it belongs to this
        /// controller; otherwise just those entries are taken out of it and the component stays.
        /// </summary>
        static List<Component> CleanupComponents(ModularAvatarMergeAnimator merge, IEnumerable<ModularAvatarMenuItem> owned,
            AnimatorController controller, out List<ParameterTrim> trims)
        {
            var result = new List<Component> { merge };
            trims = new List<ParameterTrim>();
            var names = new HashSet<string>(controller.parameters.Select(p => p.name));
            foreach (var parameters in merge.GetComponents<ModularAvatarParameters>())
            {
                var mine = parameters.parameters.Where(c => !c.isPrefix && names.Contains(c.nameOrPrefix)).Select(c => c.nameOrPrefix).ToList();
                if (mine.Count == 0) continue;
                if (mine.Count == parameters.parameters.Count) result.Add(parameters);
                else trims.Add(new ParameterTrim { Component = parameters, Names = mine });
            }
            foreach (var item in owned)
            {
                if (item.Control.type != VRCExpressionsMenu.Control.ControlType.SubMenu) continue;
                result.Add(item);
                var installer = item.GetComponent<ModularAvatarMenuInstaller>();
                if (installer != null && installer.menuToAppend == null) result.Add(installer);
            }
            return result;
        }

        /// <summary>Takes the named declarations out of a Modular Avatar Parameters component that other things still use.</summary>
        public static void ApplyTrims(IEnumerable<ParameterTrim> trims)
        {
            if (trims == null) return;
            foreach (var trim in trims)
            {
                if (!(trim.Component is ModularAvatarParameters parameters)) continue;
                var wanted = new HashSet<string>(trim.Names);
                Undo.RecordObject(parameters, "Import into AvatarDoc");
                parameters.parameters = parameters.parameters.Where(c => c.isPrefix || !wanted.Contains(c.nameOrPrefix)).ToList();
                EditorUtility.SetDirty(parameters);
            }
        }

        /// <summary>Reads the parameter declarations that govern this Merge Animator: the component beside it first, then the ones above it up to the avatar root. The nearest declaration wins, as it does in Modular Avatar's own build.</summary>
        static void ReadParameters(Transform avatarRoot, ModularAvatarMergeAnimator merge,
            Dictionary<string, ParameterConfig> configs, List<ParameterConfig> prefixes)
        {
            for (var t = merge.transform; t != null; t = t.parent)
            {
                foreach (var component in t.GetComponents<ModularAvatarParameters>())
                    Collect(component, configs, prefixes);
                if (t == avatarRoot) break;
            }
        }

        static void Collect(ModularAvatarParameters component, Dictionary<string, ParameterConfig> configs, List<ParameterConfig> prefixes)
        {
            foreach (var config in component.parameters)
            {
                if (string.IsNullOrEmpty(config.nameOrPrefix)) continue;
                if (config.isPrefix) prefixes.Add(config);
                else if (!configs.ContainsKey(config.nameOrPrefix)) configs[config.nameOrPrefix] = config;
            }
        }

        /// <summary>
        /// True when Modular Avatar would give one of the controller's parameters another name at build time.
        /// The names inside the controller would then no longer match what AvatarDoc declares, so nothing of this
        /// set can be converted.
        /// </summary>
        static bool RenamedParameter(AnimatorController controller, Func<string, ParameterConfig?> declared, out string renamed)
        {
            renamed = "";
            foreach (var parameter in controller.parameters)
            {
                var found = declared(parameter.name);
                if (!found.HasValue) continue;
                if (!found.Value.internalParameter && string.IsNullOrEmpty(found.Value.remapTo)) continue;
                renamed = parameter.name;
                return true;
            }
            return false;
        }

        /// <summary>The declaration that applies to a parameter name: an exact one first, then the longest prefix declaration that covers it.</summary>
        static ParameterConfig? Declaration(IReadOnlyDictionary<string, ParameterConfig> configs, IReadOnlyList<ParameterConfig> prefixes, string name)
        {
            if (configs.TryGetValue(name, out var exact)) return exact;
            ParameterConfig? best = null;
            foreach (var prefix in prefixes)
            {
                if (!name.StartsWith(prefix.nameOrPrefix, StringComparison.Ordinal)) continue;
                if (best.HasValue && best.Value.nameOrPrefix.Length >= prefix.nameOrPrefix.Length) continue;
                best = prefix;
            }
            return best;
        }

        static Transform PathRootOf(ModularAvatarMergeAnimator merge)
        {
            var target = merge.relativePathRoot != null ? merge.relativePathRoot.Get(merge) : null;
            return target != null ? target.transform : merge.transform;
        }

        /// <summary>Turns a clip path of the merged controller into a path from the avatar root, the way the Merge Animator's path mode would at build time.</summary>
        static Func<string, string> PathResolver(Transform avatarRoot, ModularAvatarMergeAnimator merge)
        {
            if (merge.pathMode != MergeAnimatorPathMode.Relative) return p => p;
            var prefix = CustomClips.PrefixFor(avatarRoot, PathRootOf(merge));
            if (prefix.Length == 0) return p => p;
            return p => string.IsNullOrEmpty(p) ? prefix : prefix + "/" + p;
        }
    }
}
#endif
#endif
