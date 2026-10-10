#if DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
#if DOCME_NDMF
using nadena.dev.ndmf.animator;
#endif

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Joins a merged animator's layers to the avatar's FX controller. Everything happens on NDMF's virtual
    /// clones, so the controller asset and its clips are left exactly as they are.
    /// </summary>
    public static class MergeBuilder
    {
        /// <summary>The AnimatorController behind a controller reference. An override controller is followed to the controller it overrides; its own clips come in through the clone.</summary>
        public static AnimatorController Resolve(RuntimeAnimatorController controller)
        {
            for (int depth = 0; controller is AnimatorOverrideController over && depth < 8; depth++)
                controller = over.runtimeAnimatorController;
            return controller as AnimatorController;
        }

        // Keyed by instance id AND EditorUtility.GetDirtyCount, not the instance id alone: the count changes on
        // every edit the Animator window makes, saved or not, the same way ParameterUsage.controllerReads and
        // LayerAnalysis's layer read cache are keyed, so a layer added or removed in memory misses this cache
        // instead of reporting what the controller used to look like.
        static readonly Dictionary<(int id, int dirty), IReadOnlyList<string>> layerCache = new Dictionary<(int, int), IReadOnlyList<string>>();

        /// <summary>The controller's layer names in order, for the panel's check list and for the skipped names the model validates.</summary>
        public static IReadOnlyList<string> LayerNames(RuntimeAnimatorController controller)
        {
            if (controller == null) return Array.Empty<string>();
            DropWhenPlanMoved();
            var key = (controller.GetInstanceID(), EditorUtility.GetDirtyCount(controller));
            if (layerCache.TryGetValue(key, out var cached)) return cached;
            var resolved = Resolve(controller);
            IReadOnlyList<string> read = resolved == null ? Array.Empty<string>() : resolved.layers.Select(l => l.name ?? "").ToList();
            layerCache[key] = read;
            return read;
        }

        /// <summary>Forgets what was read from the controller assets. Called when one of them is imported, so a layer or a parameter added in the Animator window shows up at once.</summary>
        public static void DropCaches()
        {
            layerCache.Clear();
            parameterCache.Clear();
        }

        /// <summary>
        /// True when the controller drives FX layer weights itself. Such a behaviour names its layer by position,
        /// so leaving a layer out would silently move what it points at.
        /// </summary>
        public static bool UsesLayerControl(RuntimeAnimatorController controller)
        {
            var resolved = Resolve(controller);
            if (resolved == null) return false;
            foreach (var layer in resolved.layers)
                if (layer.stateMachine != null && UsesLayerControl(layer.stateMachine))
                    return true;
            return false;
        }

        static bool UsesLayerControl(AnimatorStateMachine machine)
        {
            if (AimsAtFx(machine.behaviours)) return true;
            foreach (var child in machine.states)
                if (child.state != null && AimsAtFx(child.state.behaviours)) return true;
            foreach (var child in machine.stateMachines)
                if (child.stateMachine != null && UsesLayerControl(child.stateMachine)) return true;
            return false;
        }

        static bool AimsAtFx(StateMachineBehaviour[] behaviours)
        {
            if (behaviours == null) return false;
            foreach (var behaviour in behaviours)
                if (behaviour is VRCAnimatorLayerControl control && control.playable == VRC_AnimatorLayerControl.BlendableLayer.FX)
                    return true;
            return false;
        }

        static readonly Dictionary<(int id, int dirty), IReadOnlyList<(string name, AnimatorControllerParameterType type)>> parameterCache =
            new Dictionary<(int, int), IReadOnlyList<(string name, AnimatorControllerParameterType type)>>();
        static int cachedVersion = -1;

        /// <summary>Drops both caches when the inspector's plan has moved on, the way PropertyPicker drops its choices.</summary>
        static void DropWhenPlanMoved()
        {
#if DOCME_NDMF
            if (cachedVersion == AvatarPlanCache.Version) return;
            cachedVersion = AvatarPlanCache.Version;
            DropCaches();
#endif
        }

        /// <summary>
        /// The parameters a controller declares. Reading them deserialises the controller, and the inspector's
        /// plan asks per row, so the answer is kept per controller until the plan cache moves on.
        /// </summary>
        public static IReadOnlyList<(string name, AnimatorControllerParameterType type)> ParametersOf(RuntimeAnimatorController controller)
        {
            if (controller == null) return Array.Empty<(string, AnimatorControllerParameterType)>();
            DropWhenPlanMoved();
            var key = (controller.GetInstanceID(), EditorUtility.GetDirtyCount(controller));
            if (parameterCache.TryGetValue(key, out var cached)) return cached;
            var resolved = Resolve(controller);
            IReadOnlyList<(string name, AnimatorControllerParameterType type)> read = resolved == null
                ? Array.Empty<(string, AnimatorControllerParameterType)>()
                : resolved.parameters.Select(p => (p.name, p.type)).ToList();
            parameterCache[key] = read;
            return read;
        }

#if DOCME_NDMF
        /// <summary>
        /// Whether anything on the avatar reads this parameter: a merged animator of one of its components, or
        /// the avatar's own FX controller. An excluded merge counts too, since the parameter is planned on both
        /// platforms and the answer has to read the same on both.
        /// </summary>
        public static bool UsesParameter(MenuTree tree, Transform avatarRoot, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var wanted = name.Trim();
            if (tree != null)
                foreach (var node in tree.Merges)
                    if (node.Merge != null && Declares(node.Merge.controller, wanted)) return true;

            var root = avatarRoot != null ? avatarRoot : tree != null ? tree.AvatarRoot : null;
            var descriptor = root != null ? root.GetComponent<VRCAvatarDescriptor>() : null;
            return Declares(ParametersWriter.FxOf(descriptor), wanted);
        }

        static bool Declares(RuntimeAnimatorController controller, string name)
        {
            foreach (var parameter in ParametersOf(controller))
                if (parameter.name == name) return true;
            return false;
        }

        /// <summary>
        /// Appends every enabled merge's layers to the FX controller, after whatever is already there.
        /// writeDefaults is the avatar's decision, taken before AvatarDoc added layers of its own.
        /// </summary>
        public static List<ModelMessage> Build(AnimatorServicesContext asc, MenuTree tree, ParameterPlan plan, bool writeDefaults)
        {
            var messages = new List<ModelMessage>();
            if (asc == null || tree == null || tree.Merges.Count == 0) return messages;
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null)
            {
                messages.Add(ModelMessage.Error("ad.error.no_fx_layer"));
                return messages;
            }

            foreach (var node in tree.Merges)
            {
                if (node.Excluded || node.Merge == null || node.Merge.controller == null) continue;
                // The model already reports both of these; the guard is here as well because nothing outside the
                // avatar may be followed, whoever calls this.
                if (MenuModel.IsOutsideAvatar(tree.AvatarRoot, node.Merge.pathRoot)) continue;
                MergeOne(asc, fx, tree, node, plan, writeDefaults, messages);
            }
            return messages;
        }

        static void MergeOne(AnimatorServicesContext asc, VirtualAnimatorController fx, MenuTree tree, MergeNode node, ParameterPlan plan, bool writeDefaults, List<ModelMessage> messages)
        {
            var merge = node.Merge;
            // An override controller with no controller of its own has nothing to merge, and NDMF's clone throws on
            // one. The model already reports it (ad.warn.merge_override_no_controller), like a missing controller.
            if (Resolve(merge.controller) == null) return;
            var skipped = new HashSet<string>(merge.skipLayers.Where(name => !string.IsNullOrEmpty(name)));
            if (skipped.Count > 0 && UsesLayerControl(merge.controller))
            {
                messages.Add(ModelMessage.Error("ad.error.merge_layer_control", merge.controller.name, node.Component).For(merge.id));
                return;
            }
            if (SyncedToSkipped(merge.controller, skipped, out var synced, out var source))
            {
                messages.Add(ModelMessage.Error("ad.error.merge_synced_layer", merge.controller.name, synced, source, node.Component).For(merge.id));
                return;
            }

            // Cloning under the FX key lets NDMF turn the controller's own layer control indices into virtual
            // layer references, which the commit resolves to the positions these layers end up at.
            var clone = asc.ControllerContext.CloneContext.CloneDistinct(merge.controller, VRCAvatarDescriptor.AnimLayerType.FX);
            if (clone == null) return;

            var prefix = CustomClips.PrefixFor(tree.AvatarRoot, merge.pathRoot);
            if (prefix.Length > 0)
            {
                new AnimationIndex(new[] { clone }).ApplyPathPrefix(prefix);
                PrefixBehaviourPaths(clone, prefix);
            }

            MergeParameters(fx, clone, node, plan, messages);

            var layers = clone.Layers.ToList();
            // Unity plays the first layer of a controller at full weight whatever its serialised weight says.
            // Merged in, it is no longer first, so the weight it counted on has to be written down.
            if (layers.Count > 0) layers[0].DefaultWeight = 1f;
            foreach (var layer in layers)
            {
                if (layer.StateMachine == null || skipped.Contains(layer.Name)) continue;
                ApplyWriteDefaults(layer, merge.matchWriteDefaults, writeDefaults);
                layer.Name = AnimatorBuilder.UniqueLayerName(fx, layer.Name);
                fx.AddLayer(LayerPriority.Default, layer);
            }
        }

        static void MergeParameters(VirtualAnimatorController fx, VirtualAnimatorController clone, MergeNode node, ParameterPlan plan, List<ModelMessage> messages)
        {
            var merged = fx.Parameters;
            foreach (var pair in clone.Parameters)
            {
                if (merged.TryGetValue(pair.Key, out var declared))
                {
                    if (declared.type == pair.Value.type) continue;
                    var planned = plan != null ? plan.Parameters.FirstOrDefault(p => p.Name == pair.Key) : null;
                    if (OnlyParameterControlsRead(planned))
                    {
                        // VRChat converts between the expression parameter's type and the animator's, and no layer
                        // of ours reads this one, so the merged controller's type is the one that has to stand.
                        merged = merged.SetItem(pair.Key, Converted(pair.Value, planned.Default));
                        continue;
                    }
                    // Otherwise the declaration that is already there stays: changing its type would break
                    // whatever reads it, here or on the avatar.
                    var clash = ModelMessage.Error("ad.error.merge_parameter_type", node.Merge.controller.name, pair.Key,
                        pair.Value.type.ToString(), declared.type.ToString(), node.Component).For(node.Merge.id);
                    if (planned != null) clash.For(planned.Controls.Select(c => c.Entry.id).ToArray());
                    messages.Add(clash);
                    continue;
                }
                merged = merged.SetItem(pair.Key, pair.Value);
            }
            fx.Parameters = merged;
        }

        /// <summary>
        /// True when this name is in the FX controller only because AvatarDoc planned it for parameter controls or
        /// for a plain declaration: nothing generated reads it, and it is not a name the avatar already declared
        /// for itself, in its expression parameters or as an animator parameter. A declaration exists precisely to
        /// give a merged controller its parameter, so the type it reads the name at is the one that has to stand.
        /// </summary>
        static bool OnlyParameterControlsRead(PlannedParameter planned)
        {
            if (planned == null || planned.ReusesExisting || planned.IsAnimatorOnly) return false;
            if (planned.Declared) return !planned.InAvatarsFx;
            return planned.Controls.Count > 0 && planned.Controls.All(c => c.Entry.IsParameterControl);
        }

        /// <summary>The merged controller's parameter with the planned default carried over into its type.</summary>
        static AnimatorControllerParameter Converted(AnimatorControllerParameter parameter, float defaultValue)
        {
            var carried = new AnimatorControllerParameter { name = parameter.name, type = parameter.type };
            switch (parameter.type)
            {
                case AnimatorControllerParameterType.Bool:
                case AnimatorControllerParameterType.Trigger:
                    carried.defaultBool = defaultValue > 0.5f;
                    break;
                case AnimatorControllerParameterType.Int:
                    carried.defaultInt = Mathf.RoundToInt(defaultValue);
                    break;
                default:
                    carried.defaultFloat = defaultValue;
                    break;
            }
            return carried;
        }

        /// <summary>
        /// A layer that is kept while the layer it copies its states from is left out: merged on its own it would
        /// quietly stop being a copy, so the merge is refused instead.
        /// </summary>
        static bool SyncedToSkipped(RuntimeAnimatorController controller, HashSet<string> skipped, out string synced, out string source)
        {
            synced = source = "";
            if (skipped.Count == 0) return false;
            var resolved = Resolve(controller);
            if (resolved == null) return false;
            var layers = resolved.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                int index = layers[i].syncedLayerIndex;
                if (index < 0 || index >= layers.Length || index == i) continue;
                if (skipped.Contains(layers[i].name) || !skipped.Contains(layers[index].name)) continue;
                synced = layers[i].name;
                source = layers[index].name;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The path prefix for the one state behaviour that carries a path of its own. NDMF's index rewrites those
        /// through its platform bindings, which an index built out here does not have, so this one is done by hand.
        /// </summary>
        static void PrefixBehaviourPaths(VirtualAnimatorController clone, string prefix)
        {
            foreach (var node in clone.AllReachableNodes())
            {
                if (node is VirtualState state) PrefixBehaviourPaths(state.Behaviours, prefix);
                else if (node is VirtualStateMachine machine) PrefixBehaviourPaths(machine.Behaviours, prefix);
            }
        }

        static void PrefixBehaviourPaths(IEnumerable<StateMachineBehaviour> behaviours, string prefix)
        {
            foreach (var behaviour in behaviours)
            {
                if (!(behaviour is VRCAnimatorPlayAudio audio)) continue;
                audio.SourcePath = string.IsNullOrEmpty(audio.SourcePath) ? prefix : prefix + "/" + audio.SourcePath;
            }
        }

        static void ApplyWriteDefaults(VirtualLayer layer, bool match, bool writeDefaults)
        {
            bool forcedOn = NeedsWriteDefaults(layer);
            if (!forcedOn && !match) return;
            foreach (var state in layer.StateMachine.AllStates())
                state.WriteDefaultValues = forcedOn || writeDefaults;
        }

        /// <summary>
        /// A layer whose states must keep Write Defaults on whatever the avatar decided: an additive layer, and a
        /// layer that is one state holding a direct blend tree, the shape a parameter driven animator uses, which
        /// stops writing anything the moment Write Defaults goes off.
        /// </summary>
        static bool NeedsWriteDefaults(VirtualLayer layer)
        {
            if (layer.BlendingMode == AnimatorLayerBlendingMode.Additive) return true;
            var machine = layer.StateMachine;
            if (machine == null || machine.StateMachines.Count > 0 || machine.States.Count != 1) return false;
            if (machine.AnyStateTransitions.Count > 0) return false;
            var state = machine.DefaultState;
            if (state == null || state.Transitions.Count > 0) return false;
            if (!(state.Motion is VirtualBlendTree tree)) return false;
            return tree.AllReachableNodes().OfType<VirtualBlendTree>().Any(t => t.BlendType == BlendTreeType.Direct);
        }
#endif
    }
}
#endif
