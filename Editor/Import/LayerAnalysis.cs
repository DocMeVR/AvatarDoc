#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using Object = UnityEngine.Object;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>What the strict analysis made of one animator layer: a draft with its targets filled, or a reason it cannot be converted.</summary>
    public sealed class LayerVerdict
    {
        public bool Convertible;
        public string Reason;
        /// <summary>The layer the parameter was traced to, set as soon as it is known, even when the verdict is no.</summary>
        public string LayerName;
        public MenuEntry Draft;
        public List<string> Notes = new List<string>();
        /// <summary>The layer rested in its on state, so the entry made from it starts on.</summary>
        public bool Inverted;
        /// <summary>Another layer of the same controller writes this parameter. Harmless where the entry keeps the old name, fatal where it gets one of its own.</summary>
        public bool DrivenElsewhere;
    }

    /// <summary>
    /// Reads one layer of an animator controller as a toggle or a radial. Conservative by design: converting a
    /// layer means the build stops running it, so anything that cannot be read with confidence is a no. The path
    /// resolver turns a clip path into a path from the avatar root, so the same analysis serves the avatar's own
    /// FX controller and a controller another tool merges from somewhere in the hierarchy.
    /// </summary>
    public static class LayerAnalysis
    {
        static readonly Regex MaterialSlot = new Regex(@"^m_Materials\.Array\.data\[(\d+)\]$");
        const float Tolerance = 0.0001f;

        /// <summary>One layer's shape problem, if any, and every parameter name its transitions, states and motions read. Read-only: nothing here changes what the layer does.</summary>
        internal sealed class LayerReadInfo
        {
            /// <summary>
            /// This layer's own name, read at the same time as everything else here. The cache this sits in is kept
            /// per controller instance until the asset is reimported, so a layer inserted or removed by the
            /// Animator window in between (which reaches AvatarPlanCache only as a coarse, non-reimport event) can
            /// leave a stale list whose entries no longer line up with the controller's current layers by position.
            /// The layer's own name is what RemovalPlanner keys a cached entry back to a live layer by instead: the
            /// build already removes a layer by name (LayerShapeProblem refuses a repeated one for exactly that
            /// reason), so this relies on nothing the system does not already depend on.
            /// </summary>
            public string LayerName;
            public string ShapeProblem;
            public readonly List<string> Parameters = new List<string>();
        }

        /// <summary>A controller's layer reads, and the same list keyed once by layer name: RemovableLayers needs the by-name lookup on every removal-plan build, so it is built alongside the list itself rather than rebuilt from it every time.</summary>
        sealed class LayerReadCacheEntry
        {
            public readonly List<LayerReadInfo> List;
            public readonly IReadOnlyDictionary<string, LayerReadInfo> ByName;

            public LayerReadCacheEntry(List<LayerReadInfo> list)
            {
                List = list;
                var byName = new Dictionary<string, LayerReadInfo>();
                foreach (var info in list)
                    if (!string.IsNullOrEmpty(info.LayerName)) byName[info.LayerName] = info;
                ByName = byName;
            }
        }

        // Walking a layer's transitions, states and blend trees means deserialising the controller's native data,
        // which costs the same whether or not anything ends up reading it. RemovalPlanner asks this on every
        // removal-plan build, so what each layer reads is worked out once per controller and kept until the asset
        // is imported again, the same way ParameterUsage keeps its own per-controller reads. Keyed by instance id
        // AND EditorUtility.GetDirtyCount, not the instance id alone, so a layer or a condition added in the
        // Animator window without saving misses the cache instead of reading what the controller used to say; see
        // ParameterUsage.controllerReads for why that count can be trusted for this.
        static readonly Dictionary<(int id, int dirty), LayerReadCacheEntry> layerReadCache = new Dictionary<(int, int), LayerReadCacheEntry>();

        /// <summary>How often a controller's layers were actually walked instead of reused. Read by the tests and the performance probe.</summary>
        public static int LayerReadBuilds { get; private set; }

        /// <summary>How often the name-keyed lookup was actually built, always alongside a LayerReadBuilds walk and never on its own. Read by the tests, to prove RemovableLayers reuses the cached lookup instead of rebuilding it on every removal-plan call.</summary>
        public static int LayerReadByNameBuilds { get; private set; }

        /// <summary>How many controllers currently hold a cached layer read. Read by the tests, to prove a fresh read leaves nothing behind for a controller it read.</summary>
        public static int CachedControllerCount => layerReadCache.Count;

        /// <summary>Whether each controller holds an FX layer control, keyed like layerReadCache: the inspector asks on every removal plan build and the Convert scan once per toggle.</summary>
        static readonly Dictionary<(int id, int dirty), bool> layerControlCache = new Dictionary<(int, int), bool>();

        /// <summary>How often a controller was walked for an FX layer control instead of answered from the cache. Read by the tests.</summary>
        public static int LayerControlReads { get; private set; }

        /// <summary>
        /// Forgets what was read from every controller: the layer reads and layer control answers here, TouchBuilder's
        /// tracking control owners and AnimatorBuilder's Write Defaults decisions. Called when a controller asset is
        /// imported, and by AvatarPlanCache for an undo or an animator asset change.
        /// </summary>
        internal static void DropCache()
        {
            layerReadCache.Clear();
            layerControlCache.Clear();
            TouchBuilder.DropTrackingCache();
            AnimatorBuilder.DropEditorWriteDefaults();
        }

        /// <summary>
        /// Every layer of the controller, in order, with its shape problem (if any) and the parameter names its
        /// transitions, states and motion read. Cached per controller instance, unless fresh: the build's own
        /// removal plan passes it, since a controller it reads through a throwaway clone must never be answered
        /// from, or left sitting in, whatever the inspector has cached for the live scene's own controller.
        /// </summary>
        internal static IReadOnlyList<LayerReadInfo> LayerReadsOf(AnimatorController controller, bool fresh = false) => EntryFor(controller, fresh).List;

        /// <summary>The same reads as LayerReadsOf, keyed by each layer's own name: built once alongside the list itself and reused for as long as the list is, instead of rebuilt by every caller.</summary>
        internal static IReadOnlyDictionary<string, LayerReadInfo> LayerReadsByNameOf(AnimatorController controller, bool fresh = false) => EntryFor(controller, fresh).ByName;

        static LayerReadCacheEntry EntryFor(AnimatorController controller, bool fresh)
        {
            var key = (controller.GetInstanceID(), EditorUtility.GetDirtyCount(controller));
            if (!fresh && layerReadCache.TryGetValue(key, out var cached)) return cached;
            LayerReadBuilds++;
            var result = new List<LayerReadInfo>();
            // AnimatorController.layers builds a new array of every layer on each read, so it is read once and handed
            // down; a read per layer inside this loop grows with the square of the layer count.
            var layers = controller.layers;
            var nameCounts = NameCounts(layers);
            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                var info = new LayerReadInfo { LayerName = layer.name };
                if (layer.stateMachine == null) { result.Add(info); continue; }
                info.ShapeProblem = LayerShapeProblem(layers, nameCounts, layer, i);
                foreach (var transition in Transitions(layer.stateMachine))
                    foreach (var condition in transition.conditions)
                        AddParameter(info.Parameters, condition.parameter);
                foreach (var state in States(layer.stateMachine))
                {
                    if (state.speedParameterActive) AddParameter(info.Parameters, state.speedParameter);
                    if (state.timeParameterActive) AddParameter(info.Parameters, state.timeParameter);
                    if (state.mirrorParameterActive) AddParameter(info.Parameters, state.mirrorParameter);
                    if (state.cycleOffsetParameterActive) AddParameter(info.Parameters, state.cycleOffsetParameter);
                    AddMotionParameters(info.Parameters, state.motion);
                }
                result.Add(info);
            }
            LayerReadByNameBuilds++;
            var entry = new LayerReadCacheEntry(result);
            if (!fresh) layerReadCache[key] = entry;
            return entry;
        }

        static void AddMotionParameters(List<string> parameters, Motion motion)
        {
            if (!(motion is BlendTree tree)) return;
            AddParameter(parameters, tree.blendParameter);
            bool is2D = tree.blendType == BlendTreeType.SimpleDirectional2D || tree.blendType == BlendTreeType.FreeformDirectional2D || tree.blendType == BlendTreeType.FreeformCartesian2D;
            if (is2D) AddParameter(parameters, tree.blendParameterY);
            foreach (var child in tree.children) AddMotionParameters(parameters, child.motion);
        }

        static void AddParameter(List<string> parameters, string parameter)
        {
            if (string.IsNullOrWhiteSpace(parameter)) return;
            var name = parameter.Trim();
            if (!parameters.Contains(name)) parameters.Add(name);
        }

        /// <summary>
        /// A two state layer switched by one condition on the parameter, whose on state holds a constant clip.
        /// declaredDefaultOn is what the avatar says the parameter starts at; it decides whether a layer that
        /// rests in its on state may be read the other way round. Null falls back to the controller's own default.
        /// </summary>
        public static LayerVerdict Toggle(AnimatorController controller, string parameter, float controlValue,
            Func<string, string> resolvePath, Transform avatarRoot, bool? declaredDefaultOn = null)
        {
            var verdict = new LayerVerdict();
            if (controller == null || string.IsNullOrEmpty(parameter)) return Fail(verdict, L.Text("ad.import.reason.fx_no_layer"));
            if (UsesLayerControl(controller)) return Fail(verdict, L.Text("ad.import.reason.layer_control"));

            var layers = controller.layers;
            var layerIndices = new List<int>();
            for (int i = 0; i < layers.Length; i++)
                if (layers[i].stateMachine != null && Transitions(layers[i].stateMachine).Any(t => t.conditions.Any(c => c.parameter == parameter)))
                    layerIndices.Add(i);

            if (layerIndices.Count == 0)
                return Fail(verdict, L.Text("ad.import.reason.fx_no_layer"));
            if (layerIndices.Count > 1 || UsedElsewhere(layers, parameter))
                return Fail(verdict, string.Format(L.Text("ad.import.reason.fx_parameter_shared"), parameter));

            int layerIndex = layerIndices[0];
            var layer = layers[layerIndex];
            verdict.LayerName = layer.name;

            var shape = LayerShapeProblem(layers, NameCounts(layers), layer, layerIndex);
            if (shape != null) return Fail(verdict, shape);

            var paramType = ParamTypeOf(controller, parameter) ?? AnimatorControllerParameterType.Bool;
            var defaultState = layer.stateMachine.defaultState;
            var paramTransitions = Transitions(layer.stateMachine).Where(t => t.conditions.Any(c => c.parameter == parameter)).ToList();

            // A destination is either a state or a sub state machine (AnyState and entry transitions can target
            // either too); either kind reached through a condition on this parameter counts as another branch of
            // the layer, since a sub state machine hides its own states from a control that reuses one value only.
            var otherDestinations = new HashSet<Object>();
            foreach (var t in paramTransitions)
            {
                Object destination = t.destinationState != null ? (Object)t.destinationState : t.destinationStateMachine;
                if (destination == null || destination == (Object)defaultState) continue;
                otherDestinations.Add(destination);
            }

            if (otherDestinations.Count > 1)
                return Fail(verdict, L.Text("ad.import.reason.shared_layer"));
            if (otherDestinations.Count == 0)
                return Fail(verdict, L.Text("ad.import.reason.fx_no_on_state"));
            if (!(otherDestinations.Single() is AnimatorState other))
                return Fail(verdict, L.Text("ad.import.reason.fx_no_on_state"));

            AnimatorState onState = null;
            AnimatorState offState = null;
            foreach (var t in paramTransitions)
            {
                if (t.destinationState != other) continue;
                bool localMatched = false;
                bool localOther = false;
                foreach (var condition in t.conditions)
                {
                    if (condition.parameter == parameter)
                    {
                        if (IsOnCondition(paramType, condition, controlValue)) localMatched = true;
                    }
                    else localOther = true;
                }
                if (localMatched && localOther)
                    return Fail(verdict, L.Text("ad.import.reason.fx_other_condition"));
                if (localMatched) { onState = other; offState = defaultState; }
            }

            if (onState == null)
            {
                // The layer may rest in its on state: the parameter starts true and the layer leaves that state
                // only while it is false. Read that way round only when the shape says so beyond doubt.
                var inverted = InvertedProblem(controller, layer, parameter, paramType, controlValue, defaultState, other, declaredDefaultOn);
                if (inverted != null) return Fail(verdict, inverted);
                onState = defaultState;
                offState = other;
                verdict.Inverted = true;
            }

            if (onState.transitions.Any(t => t != null && t.hasExitTime && t.destinationState != onState))
                return Fail(verdict, L.Text("ad.import.reason.transition_chain"));

            var motion = onState.motion;
            if (motion is BlendTree)
                return Fail(verdict, L.Text("ad.import.reason.fx_blend_tree"));
            if (!(motion is AnimationClip clip))
                return Fail(verdict, L.Text("ad.import.reason.fx_no_motion"));

            var draft = MenuEntry.Create(EntryKind.Toggle, "");
            verdict.Draft = draft;
            var problem = ReadClip(avatarRoot, resolvePath, clip, draft, verdict.Notes);
            if (problem != null) return Fail(verdict, problem);

            var offProblem = ReadOffState(avatarRoot, resolvePath, offState, clip, draft, verdict.Notes);
            if (offProblem != null) return Fail(verdict, offProblem);

            if (ParameterDrivenElsewhere(layers, parameter, layerIndex))
            {
                verdict.DrivenElsewhere = true;
                verdict.Notes.Add(L.Text("ad.import.note.driven_elsewhere"));
            }
            verdict.Convertible = true;
            return verdict;
        }

        /// <summary>
        /// Why this layer may NOT be read as an inverted toggle, or null when it may. Everything has to line up:
        /// a Bool parameter, exactly two states, exactly the two mirrored transitions between them on that
        /// parameter alone, no exit time, nothing entering from AnyState or entry, and a parameter the avatar
        /// really does start at true. Anything looser would be a guess about which clip is the rest state.
        /// </summary>
        static string InvertedProblem(AnimatorController controller, AnimatorControllerLayer layer, string parameter,
            AnimatorControllerParameterType paramType, float controlValue, AnimatorState defaultState, AnimatorState other, bool? declaredDefaultOn)
        {
            if (paramType != AnimatorControllerParameterType.Bool) return L.Text("ad.import.reason.fx_no_on_state");
            // The control has to mean "true is on" for the resting state to be the on state. A control that sets
            // the parameter to 0 is on while the parameter is false, so its clips are the other way round again
            // and reading the layer inverted would swap them.
            if (controlValue <= 0.5f) return L.Text("ad.import.reason.fx_no_on_state");
            var machine = layer.stateMachine;
            if (machine.stateMachines.Length > 0 || machine.states.Length != 2) return L.Text("ad.import.reason.fx_no_on_state");
            if (machine.anyStateTransitions.Length > 0 || machine.entryTransitions.Length > 0) return L.Text("ad.import.reason.fx_no_on_state");
            if (defaultState == null || other == null || defaultState == other) return L.Text("ad.import.reason.fx_no_on_state");
            if (defaultState.transitions.Length != 1 || other.transitions.Length != 1) return L.Text("ad.import.reason.fx_no_on_state");

            var leave = defaultState.transitions[0];
            var back = other.transitions[0];
            if (leave == null || back == null) return L.Text("ad.import.reason.fx_no_on_state");
            if (leave.destinationState != other || back.destinationState != defaultState) return L.Text("ad.import.reason.fx_no_on_state");
            if (leave.hasExitTime || back.hasExitTime) return L.Text("ad.import.reason.transition_chain");
            if (!SingleCondition(leave, parameter, AnimatorConditionMode.IfNot)) return L.Text("ad.import.reason.fx_no_on_state");
            if (!SingleCondition(back, parameter, AnimatorConditionMode.If)) return L.Text("ad.import.reason.fx_no_on_state");

            bool startsOn = declaredDefaultOn ?? DefaultBoolOf(controller, parameter);
            return startsOn ? null : L.Text("ad.import.reason.inverted_default_mismatch");
        }

        static bool SingleCondition(AnimatorStateTransition transition, string parameter, AnimatorConditionMode mode)
        {
            return transition.conditions.Length == 1
                && transition.conditions[0].parameter == parameter
                && transition.conditions[0].mode == mode;
        }

        static bool DefaultBoolOf(AnimatorController controller, string parameter)
        {
            foreach (var declared in controller.parameters)
                if (declared.name == parameter) return declared.defaultBool;
            return false;
        }

        /// <summary>One state holding a 1D blend tree of two constant clips at 0 and 1 on the parameter.</summary>
        public static LayerVerdict Radial(AnimatorController controller, string parameter,
            Func<string, string> resolvePath, Transform avatarRoot)
        {
            var verdict = new LayerVerdict();
            if (controller == null || string.IsNullOrEmpty(parameter)) return Fail(verdict, L.Text("ad.import.reason.fx_no_layer"));
            if (UsesLayerControl(controller)) return Fail(verdict, L.Text("ad.import.reason.layer_control"));

            // A condition on the parameter, or a state that reads it as its speed, time, mirror or cycle offset,
            // means it does more than position this blend tree.
            var layers = controller.layers;
            foreach (var other in layers)
            {
                if (other.stateMachine == null) continue;
                if (Transitions(other.stateMachine).Any(t => t.conditions.Any(c => c.parameter == parameter)))
                    return Fail(verdict, string.Format(L.Text("ad.import.reason.fx_parameter_shared"), parameter));
                if (States(other.stateMachine).Any(s => ReadsAsStateParameter(s, parameter)))
                    return Fail(verdict, string.Format(L.Text("ad.import.reason.fx_parameter_shared"), parameter));
            }

            var layerIndices = new List<int>();
            for (int i = 0; i < layers.Length; i++)
            {
                var stateMachine = layers[i].stateMachine;
                if (stateMachine == null) continue;
                if (States(stateMachine).Any(s => s.motion != null && MotionUsesParameter(s.motion, parameter)))
                    layerIndices.Add(i);
            }

            if (layerIndices.Count == 0)
                return Fail(verdict, L.Text("ad.import.reason.fx_no_layer"));
            if (layerIndices.Count > 1)
                return Fail(verdict, string.Format(L.Text("ad.import.reason.fx_parameter_shared"), parameter));
            if (ParamTypeOf(controller, parameter) != AnimatorControllerParameterType.Float)
                return Fail(verdict, RadialShape());

            int layerIndex = layerIndices[0];
            var layer = layers[layerIndex];
            verdict.LayerName = layer.name;

            var shape = LayerShapeProblem(layers, NameCounts(layers), layer, layerIndex);
            if (shape != null) return Fail(verdict, shape);

            var machine = layer.stateMachine;
            if (machine.stateMachines.Length > 0 || machine.states.Length != 1) return Fail(verdict, RadialShape());
            if (machine.anyStateTransitions.Length > 0 || machine.entryTransitions.Length > 0) return Fail(verdict, RadialShape());
            var state = machine.states[0].state;
            if (state == null || state.transitions.Length > 0) return Fail(verdict, RadialShape());
            if (state.speedParameterActive || state.timeParameterActive || state.mirrorParameterActive || state.cycleOffsetParameterActive)
                return Fail(verdict, RadialShape());
            if (!(state.motion is BlendTree tree)) return Fail(verdict, RadialShape());
            if (tree.blendType != BlendTreeType.Simple1D || tree.blendParameter != parameter) return Fail(verdict, RadialShape());
            if (tree.useAutomaticThresholds || NormalizesBlendValues(tree)) return Fail(verdict, RadialShape());

            var children = tree.children;
            if (children.Length != 2) return Fail(verdict, RadialShape());
            if (!Mathf.Approximately(children[0].threshold, 0f) || !Mathf.Approximately(children[1].threshold, 1f)) return Fail(verdict, RadialShape());
            if (!Mathf.Approximately(tree.minThreshold, 0f) || !Mathf.Approximately(tree.maxThreshold, 1f)) return Fail(verdict, RadialShape());
            foreach (var child in children)
            {
                if (!Mathf.Approximately(child.timeScale, 1f) || child.mirror || !Mathf.Approximately(child.cycleOffset, 0f))
                    return Fail(verdict, RadialShape());
            }
            if (!(children[0].motion is AnimationClip lower) || !(children[1].motion is AnimationClip upper)) return Fail(verdict, RadialShape());

            if (ParameterDrivenElsewhere(layers, parameter, layerIndex))
            {
                verdict.DrivenElsewhere = true;
                verdict.Notes.Add(L.Text("ad.import.note.driven_elsewhere"));
            }

            var draft = MenuEntry.Create(EntryKind.Radial, "");
            verdict.Draft = draft;
            var problem = ReadRadialClips(avatarRoot, resolvePath, lower, upper, draft, verdict.Notes);
            if (problem != null) return Fail(verdict, problem);
            verdict.Convertible = true;
            return verdict;
        }

        /// <summary>The blend tree's normalised blend values flag, which the editor API does not expose.</summary>
        static bool NormalizesBlendValues(BlendTree tree)
        {
            using (var so = new SerializedObject(tree))
            {
                var property = so.FindProperty("m_NormalizedBlendValues");
                return property != null && property.boolValue;
            }
        }

        /// <summary>Moves the targets the analysis read onto the draft the caller keeps.</summary>
        public static void CopyTargets(MenuEntry from, MenuEntry to)
        {
            if (from == null || to == null) return;
            to.objects.AddRange(from.objects);
            to.blendShapes.AddRange(from.blendShapes);
            to.materials.AddRange(from.materials);
            to.properties.AddRange(from.properties);
        }

        static string RadialShape() => L.Text("ad.import.reason.radial_shape");

        static LayerVerdict Fail(LayerVerdict verdict, string reason)
        {
            verdict.Convertible = false;
            verdict.Reason = reason;
            return verdict;
        }

        /// <summary>How many layers carry each name, counted once per read of the controller's layers.</summary>
        static Dictionary<string, int> NameCounts(AnimatorControllerLayer[] layers)
        {
            var counts = new Dictionary<string, int>();
            foreach (var layer in layers)
            {
                var name = layer.name ?? "";
                counts.TryGetValue(name, out var count);
                counts[name] = count + 1;
            }
            return counts;
        }

        /// <summary>What makes a layer unsafe to take out of the build whatever its states look like. Null when nothing does. Shared with RemovalPlanner, which leaves layers out on the same terms. layers and nameCounts are the controller's layers as the caller read them once.</summary>
        static string LayerShapeProblem(AnimatorControllerLayer[] layers, Dictionary<string, int> nameCounts, AnimatorControllerLayer layer, int layerIndex)
        {
            // The build leaves a layer out by name, so a repeated name would take the other layers with it.
            if (nameCounts.TryGetValue(layer.name ?? "", out var count) && count > 1)
                return L.Text("ad.import.reason.layer_name_not_unique");
            if (LayerHasBehaviours(layer.stateMachine))
                return L.Text("ad.import.reason.behaviours");
            if (layer.avatarMask != null || (layer.defaultWeight <= 0f && layerIndex != 0))
                return L.Text("ad.import.reason.layer_masked");
            for (int i = 0; i < layers.Length; i++)
                if (i != layerIndex && layers[i].syncedLayerIndex == layerIndex)
                    return L.Text("ad.import.reason.synced_layer");
            return null;
        }

        static bool ReadsAsStateParameter(AnimatorState state, string parameter)
        {
            return (state.speedParameterActive && state.speedParameter == parameter)
                || (state.timeParameterActive && state.timeParameter == parameter)
                || (state.mirrorParameterActive && state.mirrorParameter == parameter)
                || (state.cycleOffsetParameterActive && state.cycleOffsetParameter == parameter);
        }

        /// <summary>
        /// True when a VRCAnimatorLayerControl behaviour inside this controller drives FX layer weights: it names its
        /// layer by position, so leaving a layer out would move what it points at. Kept per controller instance and
        /// GetDirtyCount until DropCache; fresh reads the controller and leaves nothing behind, for the build's clone.
        /// </summary>
        internal static bool UsesLayerControl(AnimatorController controller, bool fresh = false)
        {
            if (controller == null) return false;
            var key = (controller.GetInstanceID(), EditorUtility.GetDirtyCount(controller));
            if (!fresh && layerControlCache.TryGetValue(key, out var cached)) return cached;
            LayerControlReads++;
            bool uses = ReadsLayerControl(controller);
            if (!fresh) layerControlCache[key] = uses;
            return uses;
        }

        static bool ReadsLayerControl(AnimatorController controller)
        {
            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine == null) continue;
                foreach (var sm in StateMachines(layer.stateMachine))
                    if (HasFxLayerControl(sm.behaviours)) return true;
                foreach (var state in States(layer.stateMachine))
                    if (HasFxLayerControl(state.behaviours)) return true;
            }
            return false;
        }

        internal static bool HasFxLayerControl(IEnumerable<StateMachineBehaviour> behaviours)
        {
            if (behaviours == null) return false;
            foreach (var behaviour in behaviours)
                if (behaviour is VRCAnimatorLayerControl layerControl && layerControl.playable == VRC_AnimatorLayerControl.BlendableLayer.FX)
                    return true;
            return false;
        }

        /// <summary>True when a parameter driver or a play audio behaviour on a state or state machine of a DIFFERENT layer names this parameter, as its target or as the source it copies from.</summary>
        static bool ParameterDrivenElsewhere(AnimatorControllerLayer[] layers, string paramName, int excludeLayerIndex)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                if (i == excludeLayerIndex) continue;
                var stateMachine = layers[i].stateMachine;
                if (stateMachine == null) continue;
                foreach (var sm in StateMachines(stateMachine))
                    if (DriverNames(sm.behaviours, paramName)) return true;
                foreach (var state in States(stateMachine))
                    if (DriverNames(state.behaviours, paramName)) return true;
            }
            return false;
        }

        static bool DriverNames(IEnumerable<StateMachineBehaviour> behaviours, string paramName)
        {
            if (behaviours == null) return false;
            foreach (var behaviour in behaviours)
            {
                if (behaviour is VRCAvatarParameterDriver driver && driver.parameters != null)
                    foreach (var parameter in driver.parameters)
                    {
                        if (parameter.name == paramName) return true;
                        // A Copy entry reads its source, so that name is as much in use as the target.
                        if (parameter.source == paramName) return true;
                    }
                if (behaviour is VRCAnimatorPlayAudio audio && audio.ParameterName == paramName) return true;
            }
            return false;
        }

        static bool LayerHasBehaviours(AnimatorStateMachine root)
        {
            if (root == null) return false;
            foreach (var sm in StateMachines(root))
            {
                if (sm.behaviours != null && sm.behaviours.Length > 0) return true;
                foreach (var cs in sm.states)
                    if (cs.state != null && cs.state.behaviours != null && cs.state.behaviours.Length > 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Reads the layer's rest state onto the draft the on clip already filled. A toggle returns to what the
        /// scene holds, so a rest clip that writes something else has to be carried over as an explicit off value
        /// or the converted toggle would quietly rest somewhere else. Anything that cannot be carried over that
        /// way is a refusal, never a note: it would change how the avatar looks.
        /// </summary>
        static string ReadOffState(Transform avatarRoot, Func<string, string> resolvePath, AnimatorState offState, AnimationClip onClip, MenuEntry draft, List<string> notes)
        {
            if (offState == null || offState.motion == null) return null;
            if (!(offState.motion is AnimationClip offClip)) return L.Text("ad.import.reason.off_values_differ");

            var onBindings = new HashSet<EditorCurveBinding>(AnimationUtility.GetCurveBindings(onClip));
            var propertyCurves = new List<(Renderer renderer, string baseProperty, string suffix, float value)>();

            foreach (var binding in AnimationUtility.GetCurveBindings(offClip))
            {
                var curve = AnimationUtility.GetEditorCurve(offClip, binding);
                if (curve == null || curve.length == 0) continue;
                if (!IsConstant(curve)) return L.Text("ad.import.reason.off_values_differ");
                if (!onBindings.Contains(binding)) return L.Text("ad.import.reason.off_values_differ");
                var target = Resolve(avatarRoot, resolvePath, binding.path);
                // The on side already noted a path that does not resolve, and skipped its curve with it.
                if (target == null) continue;
                float offValue = curve.keys[0].value;

                if (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive")
                {
                    var objectTarget = draft.objects.FirstOrDefault(t => t.target == target.gameObject);
                    if (objectTarget == null) return L.Text("ad.import.reason.off_values_differ");
                    // A toggle always shows the opposite of its on state when off; anything else is a shape it cannot take.
                    if (offValue > 0.5f == objectTarget.activeWhenOn) return L.Text("ad.import.reason.off_values_differ");
                    continue;
                }

                if (typeof(SkinnedMeshRenderer).IsAssignableFrom(binding.type) && binding.propertyName.StartsWith("blendShape."))
                {
                    var shape = binding.propertyName.Substring("blendShape.".Length);
                    var renderer = target.GetComponent<SkinnedMeshRenderer>();
                    var shapeTarget = draft.blendShapes.FirstOrDefault(t => t.renderer == renderer && t.shape == shape);
                    if (shapeTarget == null) return L.Text("ad.import.reason.off_values_differ");
                    shapeTarget.minWeight = offValue;
                    shapeTarget.explicitOff = !SceneShapeMatches(renderer, shape, offValue);
                    continue;
                }

                if (typeof(Renderer).IsAssignableFrom(binding.type) && binding.propertyName.StartsWith("material."))
                {
                    var renderer = target.GetComponent<Renderer>();
                    if (renderer == null) return L.Text("ad.import.reason.off_values_differ");
                    var (baseProperty, suffix) = SplitSuffix(binding.propertyName.Substring("material.".Length));
                    propertyCurves.Add((renderer, baseProperty, suffix, offValue));
                    continue;
                }

                return L.Text("ad.import.reason.off_values_differ");
            }

            var offObjectBindings = AnimationUtility.GetObjectReferenceCurveBindings(offClip);
            if (offObjectBindings.Length > 0)
            {
                var onObjectBindings = new HashSet<EditorCurveBinding>(AnimationUtility.GetObjectReferenceCurveBindings(onClip));
                foreach (var binding in offObjectBindings)
                {
                    if (!onObjectBindings.Contains(binding)) return L.Text("ad.import.reason.off_values_differ");
                    var target = Resolve(avatarRoot, resolvePath, binding.path);
                    if (target == null) continue;
                    var match = MaterialSlot.Match(binding.propertyName);
                    var renderer = target.GetComponent<Renderer>();
                    if (renderer == null || !match.Success) return L.Text("ad.import.reason.off_values_differ");
                    int slot = int.Parse(match.Groups[1].Value);
                    if (slot < 0 || slot >= renderer.sharedMaterials.Length) return L.Text("ad.import.reason.off_values_differ");
                    var keys = AnimationUtility.GetObjectReferenceCurve(offClip, binding);
                    var distinct = keys.Select(k => k.value).Distinct().ToList();
                    // A material slot has no off value of its own: the toggle puts back whatever the scene holds.
                    if (distinct.Count != 1 || renderer.sharedMaterials[slot] != distinct[0] as Material)
                        return L.Text("ad.import.reason.off_values_differ");
                }
            }

            foreach (var group in propertyCurves.GroupBy(p => (p.renderer, p.baseProperty)))
            {
                var propertyTarget = draft.properties.FirstOrDefault(t => t.renderer == group.Key.renderer && t.property == group.Key.baseProperty);
                if (propertyTarget == null) return L.Text("ad.import.reason.off_values_differ");
                var bySuffix = group.ToDictionary(p => p.suffix, p => p.value);
                if (!TryPackOffValue(propertyTarget.type, bySuffix, out var packed)) return L.Text("ad.import.reason.off_values_differ");
                WriteOffValue(propertyTarget, packed);
                propertyTarget.explicitOff = !ScenePropertyMatches(propertyTarget, draft, packed);
            }

            if (draft.blendShapes.Any(t => t.explicitOff) || draft.properties.Any(t => t.explicitOff))
                notes.Add(L.Text("ad.import.note.off_values"));
            return null;
        }

        static bool TryPackOffValue(PropertyValueType type, IReadOnlyDictionary<string, float> bySuffix, out Vector4 packed)
        {
            packed = Vector4.zero;
            switch (type)
            {
                case PropertyValueType.Color:
                    if (bySuffix.Count != 4 || !new[] { "r", "g", "b", "a" }.All(bySuffix.ContainsKey)) return false;
                    packed = new Vector4(bySuffix["r"], bySuffix["g"], bySuffix["b"], bySuffix["a"]);
                    return true;
                case PropertyValueType.Vector:
                    if (bySuffix.Count != 4 || !new[] { "x", "y", "z", "w" }.All(bySuffix.ContainsKey)) return false;
                    packed = new Vector4(bySuffix["x"], bySuffix["y"], bySuffix["z"], bySuffix["w"]);
                    return true;
                default:
                    if (bySuffix.Count != 1 || !bySuffix.ContainsKey("")) return false;
                    packed = new Vector4(bySuffix[""], 0f, 0f, 0f);
                    return true;
            }
        }

        static void WriteOffValue(MaterialPropertyTarget target, Vector4 packed)
        {
            switch (target.type)
            {
                case PropertyValueType.Color: target.minColor = packed; break;
                case PropertyValueType.Vector: target.minVector = packed; break;
                default: target.minValue = packed.x; break;
            }
        }

        static bool SceneShapeMatches(SkinnedMeshRenderer renderer, string shape, float value)
        {
            if (renderer == null || renderer.sharedMesh == null) return false;
            int index = renderer.sharedMesh.GetBlendShapeIndex(shape);
            if (index < 0) return false;
            return Mathf.Abs(renderer.GetBlendShapeWeight(index) - value) <= Tolerance;
        }

        /// <summary>True when the materials already hold this off value, so the toggle needs no off value of its own.</summary>
        static bool ScenePropertyMatches(MaterialPropertyTarget target, MenuEntry draft, Vector4 packed)
        {
            var material = MenuModel.MaterialWithProperty(target.renderer, target.property, draft);
            if (material == null) return false;
            var scene = MenuModel.ReadProperty(material, target.property, target.type);
            for (int i = 0; i < 4; i++)
                if (Mathf.Abs(scene[i] - packed[i]) > Tolerance) return false;
            return true;
        }

        /// <summary>Fills the draft from one constant clip. Null when it read something, otherwise the reason it could not.</summary>
        static string ReadClip(Transform avatarRoot, Func<string, string> resolvePath, AnimationClip clip, MenuEntry draft, List<string> notes)
        {
            bool any = false;
            var propertyCurves = new List<(Renderer renderer, string baseProperty, string suffix, float value)>();

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var target = Resolve(avatarRoot, resolvePath, binding.path);
                if (target == null)
                {
                    notes.Add(L.Text("ad.import.note.fx_path_missing"));
                    continue;
                }

                if (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive")
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (!IsConstant(curve))
                        return Unsupported(binding.propertyName, "GameObject");
                    draft.objects.Add(new ObjectTarget { target = target.gameObject, activeWhenOn = curve.keys[0].value > 0.5f });
                    any = true;
                    continue;
                }

                if (typeof(SkinnedMeshRenderer).IsAssignableFrom(binding.type) && binding.propertyName.StartsWith("blendShape."))
                {
                    var renderer = target.GetComponent<SkinnedMeshRenderer>();
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (renderer == null || !IsConstant(curve))
                        return Unsupported(binding.propertyName, "SkinnedMeshRenderer");
                    draft.blendShapes.Add(new BlendShapeTarget { renderer = renderer, shape = binding.propertyName.Substring("blendShape.".Length), weight = curve.keys[0].value });
                    any = true;
                    continue;
                }

                if (typeof(Renderer).IsAssignableFrom(binding.type) && binding.propertyName.StartsWith("material."))
                {
                    var renderer = target.GetComponent<Renderer>();
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (renderer == null || !IsConstant(curve))
                        return Unsupported(binding.propertyName, "Renderer");
                    var (baseProperty, suffix) = SplitSuffix(binding.propertyName.Substring("material.".Length));
                    propertyCurves.Add((renderer, baseProperty, suffix, curve.keys[0].value));
                    continue;
                }

                return Unsupported(binding.propertyName, binding.type.Name);
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                var target = Resolve(avatarRoot, resolvePath, binding.path);
                if (target == null)
                {
                    notes.Add(L.Text("ad.import.note.fx_path_missing"));
                    continue;
                }

                var match = MaterialSlot.Match(binding.propertyName);
                if (!typeof(Renderer).IsAssignableFrom(binding.type) || !match.Success)
                    return Unsupported(binding.propertyName, binding.type.Name);

                var slotRenderer = target.GetComponent<Renderer>();
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                var distinct = keys.Select(k => k.value).Distinct().ToList();
                if (slotRenderer == null || distinct.Count != 1)
                    return Unsupported(binding.propertyName, "Renderer");

                draft.materials.Add(new MaterialTarget { renderer = slotRenderer, slot = int.Parse(match.Groups[1].Value), material = distinct[0] as Material });
                any = true;
            }

            foreach (var group in propertyCurves.GroupBy(p => (p.renderer, p.baseProperty)))
            {
                var bySuffix = group.ToDictionary(p => p.suffix, p => p.value);
                if (bySuffix.Count == 1 && bySuffix.ContainsKey(""))
                {
                    draft.properties.Add(new MaterialPropertyTarget { renderer = group.Key.renderer, property = group.Key.baseProperty, type = PropertyValueType.Float, value = bySuffix[""] });
                    any = true;
                }
                else if (bySuffix.Count == 4 && new[] { "r", "g", "b", "a" }.All(bySuffix.ContainsKey))
                {
                    draft.properties.Add(new MaterialPropertyTarget { renderer = group.Key.renderer, property = group.Key.baseProperty, type = PropertyValueType.Color, color = new Color(bySuffix["r"], bySuffix["g"], bySuffix["b"], bySuffix["a"]) });
                    any = true;
                }
                else if (bySuffix.Count == 4 && new[] { "x", "y", "z", "w" }.All(bySuffix.ContainsKey))
                {
                    draft.properties.Add(new MaterialPropertyTarget { renderer = group.Key.renderer, property = group.Key.baseProperty, type = PropertyValueType.Vector, vector = new Vector4(bySuffix["x"], bySuffix["y"], bySuffix["z"], bySuffix["w"]) });
                    any = true;
                }
                else
                {
                    // Half a colour or a vector is not something an entry can hold, and dropping it would lose
                    // part of what the layer did.
                    return string.Format(L.Text("ad.import.reason.property_partial"), group.Key.baseProperty);
                }
            }

            return any ? null : L.Text("ad.import.reason.no_supported_action");
        }

        /// <summary>Fills a radial draft from the two ends of a blend tree. Both clips have to animate the same things, constantly.</summary>
        static string ReadRadialClips(Transform avatarRoot, Func<string, string> resolvePath, AnimationClip lower, AnimationClip upper, MenuEntry draft, List<string> notes)
        {
            if (AnimationUtility.GetObjectReferenceCurveBindings(lower).Length > 0 || AnimationUtility.GetObjectReferenceCurveBindings(upper).Length > 0)
                return L.Text("ad.import.reason.radial_bindings");

            var lowerBindings = AnimationUtility.GetCurveBindings(lower);
            var upperBindings = AnimationUtility.GetCurveBindings(upper);
            if (lowerBindings.Length != upperBindings.Length || !new HashSet<EditorCurveBinding>(lowerBindings).SetEquals(upperBindings))
                return L.Text("ad.import.reason.radial_bindings");

            bool any = false;
            var propertyCurves = new List<(Renderer renderer, string baseProperty, string suffix, float low, float high)>();

            foreach (var binding in lowerBindings)
            {
                var target = Resolve(avatarRoot, resolvePath, binding.path);
                if (target == null)
                {
                    notes.Add(L.Text("ad.import.note.fx_path_missing"));
                    continue;
                }

                var lowCurve = AnimationUtility.GetEditorCurve(lower, binding);
                var highCurve = AnimationUtility.GetEditorCurve(upper, binding);
                if (!IsConstant(lowCurve) || !IsConstant(highCurve))
                    return Unsupported(binding.propertyName, binding.type.Name);
                float low = lowCurve.keys[0].value;
                float high = highCurve.keys[0].value;

                if (typeof(SkinnedMeshRenderer).IsAssignableFrom(binding.type) && binding.propertyName.StartsWith("blendShape."))
                {
                    var renderer = target.GetComponent<SkinnedMeshRenderer>();
                    if (renderer == null) return Unsupported(binding.propertyName, "SkinnedMeshRenderer");
                    draft.blendShapes.Add(new BlendShapeTarget
                    {
                        renderer = renderer,
                        shape = binding.propertyName.Substring("blendShape.".Length),
                        weight = high,
                        minWeight = low,
                        maxWeight = high,
                    });
                    any = true;
                    continue;
                }

                if (typeof(Renderer).IsAssignableFrom(binding.type) && binding.propertyName.StartsWith("material."))
                {
                    var renderer = target.GetComponent<Renderer>();
                    if (renderer == null) return Unsupported(binding.propertyName, "Renderer");
                    var (baseProperty, suffix) = SplitSuffix(binding.propertyName.Substring("material.".Length));
                    propertyCurves.Add((renderer, baseProperty, suffix, low, high));
                    continue;
                }

                return Unsupported(binding.propertyName, binding.type.Name);
            }

            foreach (var group in propertyCurves.GroupBy(p => (p.renderer, p.baseProperty)))
            {
                var bySuffix = group.ToDictionary(p => p.suffix, p => (p.low, p.high));
                if (bySuffix.Count == 1 && bySuffix.ContainsKey(""))
                {
                    draft.properties.Add(new MaterialPropertyTarget
                    {
                        renderer = group.Key.renderer,
                        property = group.Key.baseProperty,
                        type = PropertyValueType.Float,
                        minValue = bySuffix[""].low,
                        value = bySuffix[""].high,
                    });
                    any = true;
                }
                else if (bySuffix.Count == 4 && new[] { "r", "g", "b", "a" }.All(bySuffix.ContainsKey))
                {
                    draft.properties.Add(new MaterialPropertyTarget
                    {
                        renderer = group.Key.renderer,
                        property = group.Key.baseProperty,
                        type = PropertyValueType.Color,
                        minColor = new Color(bySuffix["r"].low, bySuffix["g"].low, bySuffix["b"].low, bySuffix["a"].low),
                        color = new Color(bySuffix["r"].high, bySuffix["g"].high, bySuffix["b"].high, bySuffix["a"].high),
                    });
                    any = true;
                }
                else if (bySuffix.Count == 4 && new[] { "x", "y", "z", "w" }.All(bySuffix.ContainsKey))
                {
                    draft.properties.Add(new MaterialPropertyTarget
                    {
                        renderer = group.Key.renderer,
                        property = group.Key.baseProperty,
                        type = PropertyValueType.Vector,
                        minVector = new Vector4(bySuffix["x"].low, bySuffix["y"].low, bySuffix["z"].low, bySuffix["w"].low),
                        vector = new Vector4(bySuffix["x"].high, bySuffix["y"].high, bySuffix["z"].high, bySuffix["w"].high),
                    });
                    any = true;
                }
                else
                {
                    return string.Format(L.Text("ad.import.reason.property_partial"), group.Key.baseProperty);
                }
            }

            return any ? null : L.Text("ad.import.reason.no_supported_action");
        }

        static string Unsupported(string property, string typeName)
            => string.Format(L.Text("ad.import.reason.fx_unsupported_curve"), property, typeName);

        static (string baseProperty, string suffix) SplitSuffix(string body)
        {
            if (body.Length > 2 && body[body.Length - 2] == '.')
            {
                var c = body[body.Length - 1];
                if ("rgbaxyzw".IndexOf(c) >= 0)
                    return (body.Substring(0, body.Length - 2), c.ToString());
            }
            return (body, "");
        }

        static bool IsConstant(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0) return false;
            var first = curve.keys[0].value;
            for (int i = 1; i < curve.length; i++)
                if (!Mathf.Approximately(curve.keys[i].value, first)) return false;
            return true;
        }

        static Transform Resolve(Transform avatarRoot, Func<string, string> resolvePath, string path)
        {
            if (avatarRoot == null) return null;
            var resolved = resolvePath == null ? path : resolvePath(path);
            return string.IsNullOrEmpty(resolved) ? avatarRoot : avatarRoot.Find(resolved);
        }

        static bool IsOnCondition(AnimatorControllerParameterType type, AnimatorCondition condition, float controlValue)
        {
            switch (type)
            {
                case AnimatorControllerParameterType.Bool:
                    return condition.mode == AnimatorConditionMode.If && controlValue > 0.5f;
                case AnimatorControllerParameterType.Int:
                    return (condition.mode == AnimatorConditionMode.Equals && Mathf.Approximately(condition.threshold, controlValue))
                        || (condition.mode == AnimatorConditionMode.Greater && Mathf.Approximately(condition.threshold, controlValue - 0.5f));
                default:
                    return condition.mode == AnimatorConditionMode.Greater && condition.threshold < controlValue;
            }
        }

        internal static AnimatorControllerParameterType? ParamTypeOf(AnimatorController controller, string name)
        {
            foreach (var parameter in controller.parameters)
                if (parameter.name == name) return parameter.type;
            return null;
        }

        /// <summary>True when the parameter drives a blend tree, a motion time, a speed, a mirror or a cycle offset parameter anywhere in the controller.</summary>
        static bool UsedElsewhere(AnimatorControllerLayer[] layers, string paramName)
        {
            foreach (var layer in layers)
            {
                if (layer.stateMachine == null) continue;
                foreach (var state in States(layer.stateMachine))
                {
                    if (ReadsAsStateParameter(state, paramName)) return true;
                    if (state.motion != null && MotionUsesParameter(state.motion, paramName)) return true;
                }
            }
            return false;
        }

        internal static bool MotionUsesParameter(Motion motion, string paramName)
        {
            if (!(motion is BlendTree tree)) return false;
            bool is2D = tree.blendType == BlendTreeType.SimpleDirectional2D || tree.blendType == BlendTreeType.FreeformDirectional2D || tree.blendType == BlendTreeType.FreeformCartesian2D;
            if (tree.blendParameter == paramName) return true;
            if (is2D && tree.blendParameterY == paramName) return true;
            foreach (var child in tree.children)
                if (child.motion != null && MotionUsesParameter(child.motion, paramName)) return true;
            return false;
        }

        /// <summary>The state machine and every sub state machine under it. A sub state machine whose object is missing (a bad merge, a hand edited controller) is left out, with everything under it.</summary>
        internal static IEnumerable<AnimatorStateMachine> StateMachines(AnimatorStateMachine root)
        {
            if (root == null) yield break;
            yield return root;
            foreach (var child in root.stateMachines)
            {
                if (child.stateMachine == null) continue;
                foreach (var sm in StateMachines(child.stateMachine))
                    yield return sm;
            }
        }

        /// <summary>Every state in the state machine and its sub state machines. A state whose object is missing is left out.</summary>
        internal static IEnumerable<AnimatorState> States(AnimatorStateMachine root)
        {
            foreach (var sm in StateMachines(root))
                foreach (var cs in sm.states)
                    if (cs.state != null) yield return cs.state;
        }

        /// <summary>Every transition of a layer's state machine: any-state, entry, and state-to-state, including sub state machines. A transition whose object is missing is left out.</summary>
        internal static IEnumerable<AnimatorTransitionBase> Transitions(AnimatorStateMachine root)
        {
            foreach (var sm in StateMachines(root))
            {
                foreach (var t in sm.anyStateTransitions)
                    if (t != null) yield return t;
                foreach (var t in sm.entryTransitions)
                    if (t != null) yield return t;
            }
            foreach (var state in States(root))
                foreach (var t in state.transitions)
                    if (t != null) yield return t;
        }
    }
}
#endif
