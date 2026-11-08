#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Generates one FX layer per control: Off and On states for toggles, a state per member for groups, a blend tree for radials.</summary>
    public static class AnimatorBuilder
    {
        const string PinSuffix = " (pin)";

        /// <summary>
        /// pinLayerNames collects the exact, already-deduped name of every pin layer BuildPinLayer creates in this
        /// build, into PinLayerState.Names: a layer's display name can be spoofed (an entry called "Face (pin)", or
        /// a merged controller shipping a layer literally called "AD: Something (pin)"), an object reference
        /// cannot. Passing the same collection back into DecideWriteDefaults and MovePinLayersLast is what tells
        /// them a layer is really one of ours, both within this activation (the collection already holds what an
        /// earlier call in the same build made) and across the later one in Optimizing, where the virtual layer
        /// objects are freshly rebuilt from the committed controller and only the recorded names survive.
        /// </summary>
        /// <param name="touch">Where the pause flag of a toggle whose rule holds a shape VRChat blinks or speaks with is named (TouchParameters.PauseFlags). Null builds no flag.</param>
        public static List<ModelMessage> Build(AnimatorServicesContext asc, MenuTree tree, ParameterPlan plan, BaseState baseState, ICollection<string> pinLayerNames = null, FollowPlan followPlan = null, PinLayerState pinState = null, TouchBuilder.TouchParameters touch = null)
        {
            var messages = new List<ModelMessage>();
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null)
            {
                messages.Add(ModelMessage.Error("ad.error.no_fx_layer"));
                return messages;
            }

            bool writeDefaults = DecideWriteDefaults(fx, pinLayerNames);
            foreach (var parameter in plan.Parameters) EnsureParameter(fx, parameter);

            var builtGroups = new HashSet<PlannedParameter>();
            foreach (var control in tree.Controls)
            {
                // A preset gets its own layer from PresetBuilder, with a parameter driver instead of a toggle body.
                if (control.Entry.kind == EntryKind.Preset) continue;
                // Its parameter is still declared above, so the other platform's menu keeps working; only the layer goes.
                if (control.Excluded) continue;
                if (!plan.Bindings.TryGetValue(control, out var binding)) continue;
                if (binding.Parameter.GroupName != null)
                {
                    if (builtGroups.Add(binding.Parameter)) BuildGroupLayer(asc, fx, writeDefaults, binding.Parameter, plan, baseState, tree.AvatarRoot, messages, pinLayerNames, pinState, touch);
                    continue;
                }
                // A parameter control drives its parameter and nothing else: whatever reads it, usually a merged
                // animator, carries the effect, so a layer here would only fight with it.
                if (control.Entry.IsParameterControl) continue;
                if (!control.Entry.HasEffects) continue;
                if (control.Entry.kind == EntryKind.Radial) BuildRadialLayer(asc, fx, writeDefaults, control, binding, tree.AvatarRoot, messages, baseState);
                else BuildToggleLayer(asc, fx, writeDefaults, control, binding, baseState, tree.AvatarRoot, messages, pinLayerNames, followPlan, pinState, touch);
            }
            return messages;
        }

        /// <summary>
        /// Unanimous Write Defaults wins; mixed or no states means on. Additive layers, single-state blend tree
        /// layers and AvatarDoctor's own pin layers do not count; pinLayerNames says which layers those are (null
        /// or empty excludes none, exactly as if every layer here were ordinary).
        /// </summary>
        public static bool DecideWriteDefaults(VirtualAnimatorController fx, ICollection<string> pinLayerNames = null)
        {
            var states = fx.Layers
                .Where(l => l.StateMachine != null && l.BlendingMode != AnimatorLayerBlendingMode.Additive && !IsSingleBlendTreeLayer(l) && !IsKnownPinLayer(l, pinLayerNames))
                .SelectMany(l => l.StateMachine.AllStates())
                .ToList();
            if (states.Count == 0) return true;
            bool first = states[0].WriteDefaultValues;
            return states.All(s => s.WriteDefaultValues == first) ? first : true;
        }

        static bool IsSingleBlendTreeLayer(VirtualLayer layer)
        {
            var states = layer.StateMachine.AllStates().ToList();
            return states.Count == 1 && states[0].Motion is VirtualBlendTree;
        }

        /// <summary>What DecideWriteDefaultsForAvatar found: the decision the build would reach today, whether the avatar's own layers actually agree, and which of them sit on each side, by name, for the warning that names them.</summary>
        public readonly struct EditorWriteDefaultsDecision
        {
            public readonly bool WriteDefaults;
            public readonly bool Mixed;
            public readonly List<string> OnLayers;
            public readonly List<string> OffLayers;
            public EditorWriteDefaultsDecision(bool writeDefaults, bool mixed, List<string> onLayers, List<string> offLayers)
            {
                WriteDefaults = writeDefaults;
                Mixed = mixed;
                OnLayers = onLayers;
                OffLayers = offLayers;
            }
        }

        /// <summary>
        /// The same decision DecideWriteDefaults reaches, read from the avatar's own saved FX controller asset
        /// instead of NDMF's virtual one: the editor has no build under way to read a VirtualAnimatorController
        /// from, and this never builds one just to answer the question. Also reports whether the avatar's own
        /// layers actually agree, and which of them sit on each side, so a mismatch can be shown and named before
        /// upload rather than only once VRChat's own SDK complains. fx null (no FX layer yet) reports the same
        /// "on" default DecideWriteDefaults reaches for zero states, with nothing mixed.
        /// </summary>
        public static EditorWriteDefaultsDecision DecideWriteDefaultsForAvatar(AnimatorController fx)
        {
            if (fx == null) return ReadWriteDefaults(null);
            var key = (fx.GetInstanceID(), EditorUtility.GetDirtyCount(fx));
            if (editorWriteDefaults.TryGetValue(key, out var cached)) return cached;
            var decision = ReadWriteDefaults(fx);
            editorWriteDefaults[key] = decision;
            return decision;
        }

        /// <summary>
        /// DecideWriteDefaultsForAvatar's answer per controller instance and GetDirtyCount: every inspector plan
        /// build asks, and the answer walks every state of every layer. LayerAnalysis.DropCache clears it with the
        /// other controller reads, for an edit that touched only a state.
        /// </summary>
        static readonly Dictionary<(int id, int dirty), EditorWriteDefaultsDecision> editorWriteDefaults = new Dictionary<(int, int), EditorWriteDefaultsDecision>();

        /// <summary>How often a controller's states were walked for the Write Defaults decision instead of answered from the cache. Read by the tests.</summary>
        public static int WriteDefaultsReads { get; private set; }

        internal static void DropEditorWriteDefaults() => editorWriteDefaults.Clear();

        static EditorWriteDefaultsDecision ReadWriteDefaults(AnimatorController fx)
        {
            WriteDefaultsReads++;
            var eligible = new List<AnimatorControllerLayer>();
            var fxLayers = fx != null ? fx.layers : null;
            if (fxLayers != null)
                foreach (var layer in fxLayers)
                {
                    if (layer.stateMachine == null || layer.blendingMode == AnimatorLayerBlendingMode.Additive) continue;
                    var states = LayerAnalysis.States(layer.stateMachine).ToList();
                    if (states.Count == 0) continue;
                    if (states.Count == 1 && states[0].motion is UnityEditor.Animations.BlendTree) continue;
                    eligible.Add(layer);
                }

            var allStates = eligible.SelectMany(l => LayerAnalysis.States(l.stateMachine)).ToList();
            bool writeDefaults;
            if (allStates.Count == 0) writeDefaults = true;
            else
            {
                bool first = allStates[0].writeDefaultValues;
                writeDefaults = allStates.All(s => s.writeDefaultValues == first) ? first : true;
            }

            var onLayers = new List<string>();
            var offLayers = new List<string>();
            foreach (var layer in eligible)
            {
                var states = LayerAnalysis.States(layer.stateMachine).ToList();
                bool allOn = states.All(s => s.writeDefaultValues);
                bool allOff = states.All(s => !s.writeDefaultValues);
                if (allOn) onLayers.Add(layer.name);
                else if (allOff) offLayers.Add(layer.name);
                else { onLayers.Add(layer.name); offLayers.Add(layer.name); }
            }
            bool mixed = onLayers.Count > 0 && offLayers.Count > 0;
            return new EditorWriteDefaultsDecision(writeDefaults, mixed, onLayers, offLayers);
        }

        /// <summary>Whether this layer's exact name is one BuildPinLayer recorded. Never guessed from the name's shape alone: a name can be copied, an entry in a recorded set cannot.</summary>
        static bool IsKnownPinLayer(VirtualLayer layer, ICollection<string> pinLayerNames) => pinLayerNames != null && layer.Name != null && pinLayerNames.Contains(layer.Name);

        /// <summary>
        /// Moves every layer named in pinLayerNames to the end of the FX controller, in their existing order. The
        /// priority a pin layer is created with only orders it against layers NDMF's own animator services also
        /// placed; a layer merged straight into the real controller outside those services (VRCFury's own build
        /// step, or this same avatar's FX re-read at the start of the final pass) lands back at the default
        /// priority in physical order, so the move has to run again here, after every other tool, right before the
        /// controller commits. Safe to call with no recorded names: nothing moves.
        /// </summary>
        public static void MovePinLayersLast(AnimatorServicesContext asc, ICollection<string> pinLayerNames)
        {
            if (pinLayerNames == null || pinLayerNames.Count == 0) return;
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return;
            // The Layers setter clears and rebuilds the controller's own priority bookkeeping from scratch, unlike
            // RemoveLayer followed by AddLayer, which can find a layer already priority-tracked (one this pass
            // itself gave the highest priority to, still tracked from the same activation) and refuse it. A stable
            // sort keeps every layer's relative order within its own group (unnamed, then recorded).
            fx.Layers = fx.Layers.OrderBy(l => IsKnownPinLayer(l, pinLayerNames)).ToList();
        }

        /// <summary>
        /// Every state of every FX layer named in pinLayerNames (pin, distance and pause layers) goes back to Write
        /// Defaults off. VRCFury's Write Defaults fixer runs between AvatarDoc's two passes and can turn every layer
        /// of the avatar on; a pin layer's Off state, or a distance layer's Far state, would then write the default
        /// of a shape face tracking owns every frame, from the last FX layer, and freeze tracking on it.
        /// </summary>
        public static void TurnPinLayersWriteDefaultsOff(AnimatorServicesContext asc, ICollection<string> pinLayerNames)
        {
            if (pinLayerNames == null || pinLayerNames.Count == 0) return;
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return;
            foreach (var layer in fx.Layers)
            {
                if (layer.StateMachine == null || !IsKnownPinLayer(layer, pinLayerNames)) continue;
                foreach (var state in layer.StateMachine.AllStates()) state.WriteDefaultValues = false;
            }
        }

        /// <summary>
        /// Every VRCAnimatorLayerControl outside the FX controller that fades an FX layer, with the layer its index
        /// names in FX's current order. NDMF only turns a control's index into a virtual layer reference inside the
        /// controller its playable names (VRChatPlatformAnimatorBindings.VirtualizeStateBehaviour and
        /// CommitStateBehaviour), so a control in Action or Gesture that fades an FX layer keeps a plain position
        /// through any change to the FX layer list. Read before the final pass changes that list.
        /// </summary>
        public static Dictionary<VRCAnimatorLayerControl, VirtualLayer> FxLayerControlsElsewhere(AnimatorServicesContext asc)
        {
            var result = new Dictionary<VRCAnimatorLayerControl, VirtualLayer>();
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return result;
            var layers = fx.Layers.ToList();
            foreach (var controller in asc.ControllerContext.GetAllControllers())
            {
                if (ReferenceEquals(controller, fx)) continue;
                foreach (var control in LayerRemover.FxLayerControls(controller))
                    if (control.layer >= 0 && control.layer < layers.Count) result[control] = layers[control.layer];
            }
            return result;
        }

        /// <summary>Points every control FxLayerControlsElsewhere read at the position its layer holds now. A control whose layer has left FX keeps the index it had.</summary>
        public static void ReaimFxLayerControls(AnimatorServicesContext asc, IReadOnlyDictionary<VRCAnimatorLayerControl, VirtualLayer> targets)
        {
            if (targets == null || targets.Count == 0) return;
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return;
            var positions = new Dictionary<VirtualLayer, int>();
            int index = 0;
            foreach (var layer in fx.Layers) positions[layer] = index++;
            foreach (var pair in targets)
                if (pair.Key != null && positions.TryGetValue(pair.Value, out var position)) pair.Key.layer = position;
        }

        /// <summary>
        /// The FX layers some VRCAnimatorLayerControl can fade, so none of them can be trusted to hold a binding. A
        /// control inside FX names its layer by VirtualLayerIndex: NDMF rewrites the stored position into it
        /// whenever it clones or reactivates the controller (VRChatPlatformAnimatorBindings.VirtualizeStateBehaviour).
        /// A control in any other controller keeps a plain position, read against layers in the order they had
        /// before anything in the final pass reordered them.
        /// </summary>
        static HashSet<VirtualLayer> LayersUnderLayerControl(AnimatorServicesContext asc, VirtualAnimatorController fx, List<VirtualLayer> layers)
        {
            var byVirtualIndex = new Dictionary<int, VirtualLayer>();
            foreach (var layer in layers) byVirtualIndex[layer.VirtualLayerIndex] = layer;
            var result = new HashSet<VirtualLayer>();
            foreach (var controller in asc.ControllerContext.GetAllControllers())
            {
                bool insideFx = ReferenceEquals(controller, fx);
                foreach (var control in LayerRemover.FxLayerControls(controller))
                {
                    if (insideFx)
                    {
                        if (byVirtualIndex.TryGetValue(control.layer, out var target)) result.Add(target);
                    }
                    else if (control.layer >= 0 && control.layer < layers.Count) result.Add(layers[control.layer]);
                }
            }
            return result;
        }

        /// <summary>What the last final pass found owned by another layer, so a test of a whole build can read it; cleared when a build starts.</summary>
        public static readonly HashSet<EditorCurveBinding> LastOwnedByAnotherLayer = new HashSet<EditorCurveBinding>();

        /// <summary>The rest of a shape VRChat blinks or speaks with is 0, where its own blinking and lip sync start from; any other shape rests at its own value.</summary>
        internal static float TrackedRest(ShapeSetModel.HeldContext context, SkinnedMeshRenderer renderer, string shape, float rest)
            => context.IsEyelid(renderer, shape) || context.IsViseme(renderer, shape) ? 0f : rest;

        /// <summary>
        /// Gives every layer that pins a shape or moves a property its way back to rest: the rest value goes into the
        /// layer's Reset state, which plays for one frame on the way out, and never into the idle state, which writes
        /// nothing for it. An idle state that wrote the rest value would hold it from the last FX layer on every frame
        /// the layer is idle, over face tracking and blinking, whenever the guess below about who owns the shape was
        /// wrong; written in Reset, a wrong guess costs one frame. With nothing at all to write, a layer's Reset is left out
        /// (DropEmptyResets). This runs after Modular Avatar and VRCFury, the only point where every layer that will
        /// ever exist is already in the controller, and before OverrideBuilder.RunFinal, so an override's own flatten
        /// sees the rest curve this adds and the frame of reset shows the override's value too. Safe to call with
        /// nothing recorded: nothing changes.
        /// </summary>
        public static void ReleasePinsToRest(AnimatorServicesContext asc, PinLayerState pins)
        {
            if (pins == null || (pins.Bindings.Count == 0 && pins.PropertyRests.Count == 0 && pins.Resets.Count == 0)) return;
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return;

            var layers = fx.Layers.ToList();
            var byName = new Dictionary<string, VirtualLayer>();
            foreach (var layer in layers)
                if (layer.Name != null && !byName.ContainsKey(layer.Name)) byName[layer.Name] = layer;
            // A layer a control can fade cannot be trusted to hold anything. Modular Avatar's MMD relay, VRCFury's MMD
            // support and full body emotes add such controls to many face tracking avatars, aimed at other layers,
            // so only the layers they aim at are left out.
            var underLayerControl = LayersUnderLayerControl(asc, fx, layers);
            // The clips of every recorded Reset, which its Load plays too. They hold only rest curves and only play
            // for a moment at load and on the way out, so they never decide whether a layer owns a binding.
            var resetClips = new HashSet<VirtualClip>();
            foreach (var layerName in pins.Resets.Keys)
                if (byName.TryGetValue(layerName, out var recordedLayer) && ResetStateOf(recordedLayer, layerName, pins)?.Motion is VirtualClip resetClip)
                    resetClips.Add(resetClip);

            foreach (var group in pins.Bindings.GroupBy(b => (b.Renderer, b.Shape)))
            {
                var (renderer, shape) = group.Key;
                if (renderer == null || !MenuModel.ShapeExists(renderer, shape)) continue;
                var path = asc.ObjectPathRemapper.GetVirtualPathForObject(renderer.gameObject);
                var binding = EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + shape);
                ReleaseFromEveryLayer(asc, layers, byName, pins, underLayerControl, resetClips, binding, group.Select(b => (b.LayerName, b.RestValue)), isShape: true);
            }

            // A material property channel a trigger's inactive state left here goes by the same rule. Its binding
            // names the renderer's own type, the type ClipContent wrote the trigger's curves with.
            foreach (var group in pins.PropertyRests.GroupBy(r => (r.Renderer, r.Property)))
            {
                var (renderer, property) = group.Key;
                if (renderer == null || string.IsNullOrEmpty(property)) continue;
                var path = asc.ObjectPathRemapper.GetVirtualPathForObject(renderer.gameObject);
                var binding = EditorCurveBinding.FloatCurve(path, renderer.GetType(), property);
                ReleaseFromEveryLayer(asc, layers, byName, pins, underLayerControl, resetClips, binding, group.Select(r => (r.LayerName, r.RestValue)), isShape: false);
            }

            DropEmptyResets(byName, pins);
        }

        /// <summary>
        /// Hands one binding back to rest from every layer that recorded it: each of them writes the rest value into
        /// its own Reset. A blend shape rests even where some other layer owns it: a Write Defaults off layer that
        /// last wrote a shape keeps that value until something writes over it, and on other players' copies of the
        /// avatar the owner did not always do so (a headpat's closed eyes stayed, and the next pat closed them
        /// further); where the owner runs, it writes over the frame of rest straight away. A property another layer
        /// owns stays the owner's, or one frame of rest would hide a toggle. Whichever is left last puts the shape back, so it
        /// never stays at the value of a pin that was left after another one on the same shape. A layer left while a
        /// lower one still pins the same shape covers that pin for its moment of reset and no longer, so no layer
        /// needs to stand back for a lower one. A recorded ordinary layer (a trigger's own layer) whose idle state is
        /// Write Defaults on by now, after VRCFury's Write Defaults fixer say, writes the binding on every frame of
        /// that state anyway, the default when it has no curve, so the idle state gets the rest value as well.
        /// </summary>
        static void ReleaseFromEveryLayer(AnimatorServicesContext asc, List<VirtualLayer> layers, Dictionary<string, VirtualLayer> byName,
            PinLayerState pins, HashSet<VirtualLayer> underLayerControl, HashSet<VirtualClip> resetClips, EditorCurveBinding binding, IEnumerable<(string layerName, float restValue)> recorded, bool isShape)
        {
            var present = recorded.Where(r => r.layerName != null && byName.ContainsKey(r.layerName)).ToList();
            if (present.Count == 0) return;
            var recordedLayers = new HashSet<VirtualLayer>(present.Select(r => byName[r.layerName]));
            bool owned = SomeNonPinLayerOwns(asc, layers, pins.Names, recordedLayers, binding, underLayerControl, resetClips, out bool everyFrame);
            if (owned)
            {
                pins.OwnedByAnotherLayer.Add(binding);
                LastOwnedByAnotherLayer.Add(binding);
            }
            if (everyFrame) pins.WrittenEveryFrameByAnotherLayer.Add(binding);
            foreach (var (layerName, restValue) in present)
            {
                if (owned && !isShape) continue;
                var layer = byName[layerName];
                var reset = ResetStateOf(layer, layerName, pins);
                if (reset == null) continue;
                ReleaseToRest(reset, binding, restValue);
                if (owned || IsKnownPinLayer(layer, pins.Names)) continue;
                var idle = reset.Transitions.FirstOrDefault()?.DestinationState;
                if (idle != null && idle.WriteDefaultValues) ReleaseToRest(idle, binding, restValue);
            }
        }

        /// <summary>Writes the rest value into a state's clip, 1/60 of a second long, unless the clip already writes the binding.</summary>
        static void ReleaseToRest(VirtualState state, EditorCurveBinding binding, float restValue)
        {
            if (!(state.Motion is VirtualClip clip) || clip.GetFloatCurve(binding) != null) return;
            clip.SetFloatCurve(binding, AnimationCurve.Constant(0f, ResetSeconds, restValue));
        }

        /// <summary>How long a Reset state plays: 1/60 of a second, one frame at 60 frames a second and a few at higher rates.</summary>
        const float ResetSeconds = 1f / 60f;

        /// <summary>The Reset state AddResetState gave this layer, or null when it had none or another tool rebuilt the layer since.</summary>
        static VirtualState ResetStateOf(VirtualLayer layer, string layerName, PinLayerState pins)
        {
            if (layer?.StateMachine == null || !pins.Resets.TryGetValue(layerName, out int occurrence)) return null;
            return layer.StateMachine.AllStates().Where(s => s.Name == PinLayerState.ResetStateName).Skip(occurrence).FirstOrDefault();
        }

        /// <summary>
        /// Leaves out every Reset state that ended up with nothing to write, because every binding of its layer is
        /// owned elsewhere or held by VRChat's own tracking: an empty clip lasts a whole second in Unity, and a layer
        /// waiting in it could not come back on for that second. Every way into it goes straight to the state it would
        /// have fallen through to instead, and a layer that started in Load starts there.
        /// </summary>
        static void DropEmptyResets(Dictionary<string, VirtualLayer> byName, PinLayerState pins)
        {
            foreach (var layerName in pins.Resets.Keys.ToList())
            {
                if (!byName.TryGetValue(layerName, out var layer)) continue;
                var reset = ResetStateOf(layer, layerName, pins);
                if (reset == null || !(reset.Motion is VirtualClip clip)) continue;
                if (clip.GetFloatCurveBindings().Any() || clip.GetObjectCurveBindings().Any()) continue;
                var idle = reset.Transitions.FirstOrDefault()?.DestinationState;
                if (idle == null) continue;
                var sm = layer.StateMachine;
                foreach (var state in sm.AllStates())
                    foreach (var transition in state.Transitions)
                        if (transition.DestinationState == reset) transition.SetDestination(idle);
                foreach (var transition in sm.AnyStateTransitions)
                    if (transition.DestinationState == reset) transition.SetDestination(idle);
                foreach (var transition in sm.EntryTransitions)
                    if (transition.DestinationState == reset) transition.SetDestination(idle);
                if (sm.DefaultState == reset) sm.DefaultState = idle;
                sm.States = sm.States.RemoveAll(child => child.State == reset);
                var load = sm.DefaultState;
                if (load != null && load.Name == PinLayerState.LoadStateName && load.Transitions.Count == 1 && load.Transitions[0].DestinationState == idle)
                {
                    sm.DefaultState = idle;
                    sm.States = sm.States.RemoveAll(child => child.State == load);
                }
                pins.Resets.Remove(layerName);
            }
        }

        /// <summary>
        /// Adds the state a layer passes through on its way back to rest: Reset, Write Defaults as given, falling
        /// through to idle by exit time with no condition. Its clip starts empty; ReleasePinsToRest writes the rest
        /// values into it in the final pass, each 1/60 of a second long, so the layer writes them once on the way out
        /// and nothing at all while it is idle. Added after every other state of the layer. A layer that would start
        /// idle starts in Load instead, which passes straight to Reset, so the rests are written once at load too: the
        /// build writes the objects and materials of a blend shape trigger that starts on into the avatar, which a
        /// different source value would otherwise leave in place, and a row's own off
        /// value would otherwise show only after the effect had come and gone once. For a pin layer (fadesWhileIdle),
        /// Reset also writes once what the idle state writes (an object or material slot at its off value, a pause flag
        /// at 0): idle plays at weight 0 (FadePinLayersWhileIdle), where its own curves no longer count.
        /// </summary>
        internal static VirtualState AddResetState(VirtualStateMachine sm, string layerName, VirtualState idle, Vector3 position, bool writeDefaults, PinLayerState pinState, bool fadesWhileIdle = false)
        {
            int occurrence = sm.AllStates().Count(s => s.Name == PinLayerState.ResetStateName);
            var reset = sm.AddState(PinLayerState.ResetStateName, VirtualClip.Create(layerName + " Reset"), position);
            reset.WriteDefaultValues = writeDefaults;
            if (fadesWhileIdle && idle.Motion is VirtualClip idleClip) WriteOnceInto(idleClip, (VirtualClip)reset.Motion);
            var back = VirtualStateTransition.Create();
            back.SetDestination(idle);
            back.Conditions = ImmutableList<AnimatorCondition>.Empty;
            back.ExitTime = 1f;
            back.Duration = 0f;
            back.HasFixedDuration = true;
            back.CanTransitionToSelf = false;
            reset.Transitions = ImmutableList.Create(back);
            if (sm.DefaultState == idle)
            {
                // Reset as the start state itself would not do: Unity advances a start state by the first frame's
                // time before it samples it, so a first frame of 1/60 of a second or longer leaves Reset by exit time
                // unseen. A state entered by a transition is always sampled once. So the layer starts in Load, which
                // plays Reset's own clip and passes to Reset the same way Reset passes to idle: whichever of the two
                // the first frame samples, the rests are written.
                var load = sm.AddState(PinLayerState.LoadStateName, reset.Motion, position + new Vector3(0f, 80f, 0f));
                load.WriteDefaultValues = writeDefaults;
                var start = VirtualStateTransition.Create();
                start.SetDestination(reset);
                start.Conditions = ImmutableList<AnimatorCondition>.Empty;
                start.ExitTime = 1f;
                start.Duration = 0f;
                start.HasFixedDuration = true;
                start.CanTransitionToSelf = false;
                load.Transitions = ImmutableList.Create(start);
                sm.DefaultState = load;
            }
            pinState.Resets[layerName] = occurrence;
            if (fadesWhileIdle) pinState.Idles[layerName] = idle.Name;
            return reset;
        }

        /// <summary>Every curve of one clip, at its value at time 0, into another, ResetSeconds long.</summary>
        static void WriteOnceInto(VirtualClip from, VirtualClip into)
        {
            foreach (var binding in from.GetFloatCurveBindings())
            {
                var curve = from.GetFloatCurve(binding);
                if (curve != null && curve.length > 0) into.SetFloatCurve(binding, AnimationCurve.Constant(0f, ResetSeconds, curve.Evaluate(0f)));
            }
            foreach (var binding in from.GetObjectCurveBindings())
            {
                var keys = from.GetObjectCurve(binding);
                if (keys == null || keys.Length == 0) continue;
                into.SetObjectCurve(binding, new[]
                {
                    new ObjectReferenceKeyframe { time = 0f, value = keys[0].value },
                    new ObjectReferenceKeyframe { time = ResetSeconds, value = keys[0].value },
                });
            }
        }

        /// <summary>
        /// A Write Defaults off layer that weighs more than 0 makes FX hold every blend shape a playable below it
        /// animates, shapes no FX layer touches included, so a world's dance (an MMD world, a station) moves nothing
        /// on the face while one plays. Every pin, distance and trigger layer therefore weighs 0 in its idle state and
        /// 1 in each active state, through a VRCAnimatorLayerControl on that state aimed at the layer itself (by its
        /// virtual index, which NDMF turns into the final position on commit). The layer still loads at 1 and Load and
        /// Reset set nothing, so both play at the 1 the way in left, whether VRChat applies a control at once or a
        /// frame late; what idle writes, Reset writes too (AddResetState). A pause layer animates nothing and only
        /// runs tracking controls, which run at any weight, so it weighs 0 for good. Runs last in the final pass, after
        /// every pause layer that steps aside is gone. A layer that no layer with weight writes shows the value the
        /// avatar was uploaded with, not the last one written (measured by playback), so a layer only fades when every
        /// value its Reset writes is the avatar's own (RestsMatchTheAvatar); one that rests elsewhere (a row's own off
        /// value, an object switched off at rest but on in the scene) keeps its weight while idle, as before. The
        /// shapes a pin layer holds never go into the avatar (DefaultsApplier), so a toggle that starts on fades too.
        /// </summary>
        public static void FadePinLayersWhileIdle(AnimatorServicesContext asc, PinLayerState pins, Transform avatarRoot)
        {
            if (pins == null || pins.Names.Count == 0) return;
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return;
            foreach (var layer in fx.Layers)
            {
                if (layer.StateMachine == null || !IsKnownPinLayer(layer, pins.Names)) continue;
                if (layer.Name == pins.BlinkPauseLayerName || layer.Name == pins.LipSyncPauseLayerName)
                {
                    layer.DefaultWeight = 0f;
                    continue;
                }
                if (!pins.Idles.TryGetValue(layer.Name, out var idleName)) continue;
                var states = layer.StateMachine.AllStates().ToList();
                var idle = states.FirstOrDefault(s => s.Name == idleName);
                if (idle == null) continue;
                var reset = ResetStateOf(layer, layer.Name, pins);
                if (reset != null && !RestsMatchTheAvatar(asc, fx, avatarRoot, reset.Motion as VirtualClip, pins.WrittenEveryFrameByAnotherLayer)) continue;
                layer.DefaultWeight = 1f;
                foreach (var state in states)
                {
                    if (reset != null && (state == reset || (state.Name == PinLayerState.LoadStateName && state.Motion == reset.Motion))) continue;
                    var control = ScriptableObject.CreateInstance<VRCAnimatorLayerControl>();
                    control.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
                    control.layer = layer.VirtualLayerIndex;
                    control.goalWeight = state == idle ? 0f : 1f;
                    control.blendDuration = 0f;
                    state.Behaviours = state.Behaviours.Add(control);
                }
                // VRChat ignores some behaviours for a moment after the avatar loads, and an MMD helper's own layer
                // control can set the layer back to 1, so idle enters itself again every few seconds and sets 0 again.
                var refresh = VirtualStateTransition.Create();
                refresh.SetDestination(idle);
                refresh.Conditions = ImmutableList<AnimatorCondition>.Empty;
                refresh.ExitTime = IdleRefreshSeconds / StateSeconds(idle);
                refresh.Duration = 0f;
                refresh.HasFixedDuration = true;
                idle.Transitions = idle.Transitions.Add(refresh);
            }
        }

        /// <summary>How often a faded layer's idle state sets its weight to 0 again, in seconds.</summary>
        public const float IdleRefreshSeconds = 3f;

        /// <summary>How long one pass of a state's clip lasts: its last key, or a second for an empty clip, as Unity plays it.</summary>
        static float StateSeconds(VirtualState state)
        {
            float length = 0f;
            if (state.Motion is VirtualClip clip)
            {
                foreach (var binding in clip.GetFloatCurveBindings())
                {
                    var curve = clip.GetFloatCurve(binding);
                    if (curve != null && curve.length > 0) length = Mathf.Max(length, curve.keys[curve.length - 1].time);
                }
                foreach (var binding in clip.GetObjectCurveBindings())
                {
                    var keys = clip.GetObjectCurve(binding);
                    if (keys != null && keys.Length > 0) length = Mathf.Max(length, keys[keys.Length - 1].time);
                }
            }
            return length > 0f ? length : 1f;
        }

        /// <summary>Whether every value this Reset clip writes is the one the avatar has now, at the end of the build: an animator parameter its default, anything else its value on the avatar. A binding that cannot be read counts as different. A blend shape another layer writes on every frame does not count (writtenEveryFrame): the idle layer's weight never shows on it.</summary>
        static bool RestsMatchTheAvatar(AnimatorServicesContext asc, VirtualAnimatorController fx, Transform avatarRoot, VirtualClip reset, ICollection<EditorCurveBinding> writtenEveryFrame)
        {
            if (reset == null) return true;
            const float tolerance = 0.001f;
            foreach (var binding in reset.GetFloatCurveBindings())
            {
                var curve = reset.GetFloatCurve(binding);
                if (curve == null || curve.length == 0) continue;
                if (binding.propertyName.StartsWith("blendShape.") && writtenEveryFrame.Contains(binding)) continue;
                float rest = curve.Evaluate(0f);
                if (binding.type == typeof(Animator) && string.IsNullOrEmpty(binding.path))
                {
                    if (!fx.Parameters.TryGetValue(binding.propertyName, out var parameter) || Mathf.Abs(parameter.defaultFloat - rest) > tolerance) return false;
                    continue;
                }
                if (!TryRealBinding(asc, avatarRoot, binding, out var real) || !AnimationUtility.GetFloatValue(avatarRoot.gameObject, real, out float own)) return false;
                if (Mathf.Abs(own - rest) > tolerance) return false;
            }
            foreach (var binding in reset.GetObjectCurveBindings())
            {
                var keys = reset.GetObjectCurve(binding);
                if (keys == null || keys.Length == 0) continue;
                if (!TryRealBinding(asc, avatarRoot, binding, out var real) || !AnimationUtility.GetObjectReferenceValue(avatarRoot.gameObject, real, out var own)) return false;
                if (own != keys[0].value) return false;
            }
            return true;
        }

        /// <summary>The binding with its virtual path turned into the object's path under the avatar now.</summary>
        static bool TryRealBinding(AnimatorServicesContext asc, Transform avatarRoot, EditorCurveBinding binding, out EditorCurveBinding real)
        {
            real = binding;
            var target = asc.ObjectPathRemapper.GetObjectForPath(binding.path);
            if (target == null || !target.transform.IsChildOf(avatarRoot)) return false;
            real.path = AnimationUtility.CalculateTransformPath(target.transform, avatarRoot);
            return true;
        }

        /// <summary>
        /// Whether some layer other than one of our pin layers or one of the layers that recorded this binding for
        /// release (recordedLayers), and not additive, really owns this binding: its weight is
        /// above 0, no layer control could be fading it, and any one of these holds: every one of its
        /// states is Write Defaults on AND at least one of them actually animates this binding somewhere (Unity
        /// only writes a Write Defaults value for a property a layer itself touches, so a Write Defaults on layer
        /// that never comes near this shape, an unrelated toggle's own layer on a Write Defaults on avatar, say,
        /// does not hold it just by being on); every leaf clip reachable from every one of its states carries the
        /// binding; or some state's motion holds a Direct blend tree with a child, at any depth under it, that
        /// carries the binding. The last clause exists for face tracking: typically one state holding a Direct tree
        /// with dozens of children, each moving one shape, beside a disabled or idle state with Write Defaults off and no
        /// motion at all, which the first two clauses both fail. An owned shape still gets its frame of rest; an owned
        /// property skips it, and so does the idle state of an ordinary layer, for either. When every clause fails
        /// this says no, and the layer's Reset writes the rest value once; a wrong no costs that one frame and nothing more.
        /// everyFrame says an owner holds it by one of the first two clauses, which write the binding on every frame:
        /// only such an owner lets a resting pin layer fade over a rest the avatar does not have (RestsMatchTheAvatar).
        /// The Direct tree clause can name a layer that writes nothing for a while, face tracking switched off.
        /// </summary>
        static bool SomeNonPinLayerOwns(AnimatorServicesContext asc, List<VirtualLayer> layers, ICollection<string> pinLayerNames, HashSet<VirtualLayer> recordedLayers, EditorCurveBinding binding, HashSet<VirtualLayer> underLayerControl, HashSet<VirtualClip> resetClips, out bool everyFrame)
        {
            everyFrame = false;
            var clips = new HashSet<VirtualClip>(asc.AnimationIndex.GetClipsForBinding(binding));
            if (clips.Count == 0) return false;
            bool owned = false;
            foreach (var layer in layers)
            {
                if (layer.StateMachine == null) continue;
                if (IsKnownPinLayer(layer, pinLayerNames)) continue;
                if (recordedLayers.Contains(layer)) continue;
                if (layer.BlendingMode == AnimatorLayerBlendingMode.Additive) continue;
                if (layer.DefaultWeight <= 0f) continue;
                if (underLayerControl.Contains(layer)) continue;
                if (!LayerOwnsBinding(layer, clips, resetClips, out bool writesEveryFrame)) continue;
                owned = true;
                if (!writesEveryFrame) continue;
                everyFrame = true;
                return true;
            }
            return owned;
        }

        /// <summary>Whether this layer holds the binding by one of the three clauses SomeNonPinLayerOwns names, and whether by one of the first two, which write it on every frame. A recorded Reset or Load state (its clip in resetClips) does not count as one of its states.</summary>
        static bool LayerOwnsBinding(VirtualLayer layer, HashSet<VirtualClip> clipsForBinding, HashSet<VirtualClip> resetClips, out bool everyFrame)
        {
            everyFrame = false;
            var states = layer.StateMachine.AllStates().Where(s => !(s.Motion is VirtualClip clip && resetClips.Contains(clip))).ToList();
            if (states.Count == 0) return false;
            // Unity only writes a Write Defaults value for a property the layer itself animates somewhere; a layer
            // whose states are all Write Defaults on but that never touches this binding at all (an unrelated
            // toggle's own layer, say, on a Write Defaults on avatar) does not hold it just by being on.
            everyFrame = states.All(s => s.WriteDefaultValues) && states.Any(s => s.Motion != null && LeafClips(s.Motion).Any(clipsForBinding.Contains))
                || states.All(s => s.Motion != null && LeafClips(s.Motion).All(clipsForBinding.Contains));
            if (everyFrame) return true;
            return states.Any(s => s.Motion != null && AnyDirectTreeDrivesBinding(s.Motion, clipsForBinding));
        }

        /// <summary>
        /// Whether a Direct blend tree, anywhere in this motion at any depth, has at least one child, at any depth
        /// under that tree, that carries the binding. A Direct tree mixes every one of its children in at once
        /// rather than switching between them, so a child carrying the binding contributes whenever the tree does,
        /// unlike a 1D or 2D tree's children, which is why this clause asks for any one child rather than all of
        /// them the way the plain leaf-clip clause does.
        /// </summary>
        static bool AnyDirectTreeDrivesBinding(VirtualMotion motion, HashSet<VirtualClip> clipsForBinding)
        {
            if (!(motion is VirtualBlendTree tree)) return false;
            if (tree.BlendType == BlendTreeType.Direct)
                foreach (var child in tree.Children)
                    if (child.Motion != null && LeafClips(child.Motion).Any(clipsForBinding.Contains))
                        return true;
            foreach (var child in tree.Children)
                if (child.Motion != null && AnyDirectTreeDrivesBinding(child.Motion, clipsForBinding))
                    return true;
            return false;
        }

        /// <summary>Every clip a motion resolves to: itself, or every child of a blend tree, recursively.</summary>
        internal static IEnumerable<VirtualClip> LeafClips(VirtualMotion motion)
        {
            if (motion is VirtualClip clip)
            {
                yield return clip;
                yield break;
            }
            if (!(motion is VirtualBlendTree tree)) yield break;
            foreach (var child in tree.Children)
            {
                if (child.Motion == null) continue;
                foreach (var leaf in LeafClips(child.Motion))
                    yield return leaf;
            }
        }

        static void EnsureParameter(VirtualAnimatorController fx, PlannedParameter parameter) => EnsureParameter(fx, parameter.Name, parameter.Type, parameter.Default);

        /// <summary>Declares the parameter in the FX controller unless it is already there, in which case its own declaration stays.</summary>
        internal static void EnsureParameter(VirtualAnimatorController fx, string name, PlannedType type, float defaultValue)
        {
            if (fx.Parameters.ContainsKey(name)) return;
            var acp = new AnimatorControllerParameter { name = name };
            switch (type)
            {
                case PlannedType.Bool:
                    acp.type = AnimatorControllerParameterType.Bool;
                    acp.defaultBool = defaultValue > 0.5f;
                    break;
                case PlannedType.Int:
                    acp.type = AnimatorControllerParameterType.Int;
                    acp.defaultInt = Mathf.RoundToInt(defaultValue);
                    break;
                default:
                    acp.type = AnimatorControllerParameterType.Float;
                    acp.defaultFloat = defaultValue;
                    break;
            }
            fx.SetParameter(name, acp);
        }

        internal static string UniqueLayerName(VirtualAnimatorController fx, string name)
        {
            var candidate = name;
            for (int i = 2; fx.Layers.Any(l => l.Name == candidate); i++) candidate = name + " (" + i + ")";
            return candidate;
        }

        /// <summary>The condition that says a control's own parameter is at (on) or away from (off) its bound value. Also used by TouchBuilder's flip layer to read a toggle's current state.</summary>
        internal static ImmutableList<AnimatorCondition> Conditions(PlannedParameter parameter, float value, bool on)
        {
            switch (parameter.Type)
            {
                case PlannedType.Bool:
                    return ImmutableList.Create(new AnimatorCondition { parameter = parameter.Name, mode = on ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, threshold = 0f });
                case PlannedType.Int:
                    return ImmutableList.Create(new AnimatorCondition { parameter = parameter.Name, mode = on ? AnimatorConditionMode.Equals : AnimatorConditionMode.NotEqual, threshold = value });
                default:
                    return ImmutableList.Create(new AnimatorCondition { parameter = parameter.Name, mode = on ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less, threshold = value - 0.5f });
            }
        }

        internal static VirtualStateTransition Transition(VirtualState destination, ImmutableList<AnimatorCondition> conditions, float duration = 0f)
        {
            var transition = VirtualStateTransition.Create();
            transition.SetDestination(destination);
            transition.Conditions = conditions;
            transition.ExitTime = null;
            transition.Duration = duration;
            transition.HasFixedDuration = true;
            transition.CanTransitionToSelf = false;
            return transition;
        }

        static void BuildToggleLayer(AnimatorServicesContext asc, VirtualAnimatorController fx, bool writeDefaults, ControlNode control, ControlBinding binding, BaseState baseState, Transform avatarRoot, List<ModelMessage> messages, ICollection<string> pinLayerNames, FollowPlan followPlan = null, PinLayerState pinState = null, TouchBuilder.TouchParameters touch = null)
        {
            var offContent = new ClipContent(avatarRoot);
            offContent.WriteToggle(control.Entry, false, baseState, ShapeSet.MainOnly, followPlan);
            var onContent = new ClipContent(avatarRoot);
            onContent.WriteToggle(control.Entry, true, baseState, ShapeSet.MainOnly, followPlan);

            var custom = CustomClips.Collect(control.Entry, avatarRoot, messages);
            onContent.MergeCustom(custom, control.Entry, messages);
            offContent.WriteCustomOff(custom, baseState, control.Entry, messages);

            var name = UniqueLayerName(fx, control.LayerName);
            // A control whose blend shape targets are all pinned writes nothing outside its pin layer, so the main layer is left out.
            if (!control.Entry.HasPins || offContent.Count > 0 || onContent.Count > 0)
            {
                var layer = fx.AddLayer(LayerPriority.Default, name);
                var sm = layer.StateMachine;
                var off = sm.AddState("Off", offContent.ToClip(asc, name + " Off"), new Vector3(0f, 0f, 0f));
                var on = sm.AddState("On", onContent.ToClip(asc, name + " On"), new Vector3(0f, 120f, 0f));
                off.WriteDefaultValues = writeDefaults;
                on.WriteDefaultValues = writeDefaults;
                // A toggle that is on by default starts in On, or a blend time would fade it in at every avatar
                // load, for every viewer.
                sm.DefaultState = binding.OnByDefault ? on : off;
                float blend = Mathf.Clamp(control.Entry.blendSeconds, 0f, 10f);
                off.Transitions = ImmutableList.Create(Transition(on, Conditions(binding.Parameter, binding.Value, true), blend));
                on.Transitions = ImmutableList.Create(Transition(off, Conditions(binding.Parameter, binding.Value, false), blend));
            }

            if (control.Entry.HasPins)
            {
                var member = PinMember.Of("On", control.Entry, Conditions(binding.Parameter, binding.Value, true), Conditions(binding.Parameter, binding.Value, false));
                member.PauseFlag = PauseFlagOf(touch, control.Entry);
                BuildPinLayer(asc, fx, name, new[] { member }, baseState, avatarRoot, defaultOn: binding.OnByDefault, pinLayerNames: pinLayerNames, pinState: pinState);
            }
        }

        static TouchBuilder.PauseFlag PauseFlagOf(TouchBuilder.TouchParameters touch, MenuEntry entry)
            => touch != null && touch.PauseFlags.TryGetValue(entry.id, out var flag) ? flag : null;

        /// <summary>One state of a pin layer: the control whose pins it writes, and the conditions that enter and leave it.</summary>
        internal sealed class PinMember
        {
            public string StateName = "";
            public MenuEntry Entry;
            public IReadOnlyList<ImmutableList<AnimatorCondition>> Enter = new List<ImmutableList<AnimatorCondition>>();
            public IReadOnlyList<ImmutableList<AnimatorCondition>> Leave = new List<ImmutableList<AnimatorCondition>>();
            /// <summary>Written 1 in this member's state and 0 in every other state of the layer, for the shared blink and lip sync pause layers: set when a rule of the entry holds a shape VRChat blinks or speaks with.</summary>
            public TouchBuilder.PauseFlag PauseFlag;

            public static PinMember Of(string stateName, MenuEntry entry, ImmutableList<AnimatorCondition> enter, ImmutableList<AnimatorCondition> leave)
            {
                return new PinMember
                {
                    StateName = stateName,
                    Entry = entry,
                    Enter = new[] { enter },
                    Leave = new[] { leave },
                };
            }
        }

        /// <summary>
        /// Pin targets get their own layer with Write Defaults off, so Off, which writes no shape at all, really
        /// leaves the shape to whatever is below, rather than reasserting whatever Write Defaults would restore.
        /// Off goes to a member when the member's conditions hold; a member leaves through Reset, where
        /// ReleasePinsToRest writes the rest value of every shape the layer pins once, then Off. An exclusive group
        /// takes the same way from one member to the next, through Reset and Off, so a member state writes only its
        /// own pins and the switch costs a frame or two. The layer starts in Load, which passes to Reset and then
        /// Off, or in its member for a toggle that is on by default. Without a PinLayerState there is no final pass
        /// to fill Reset, so the layer starts in Off and the members go straight back to it.
        /// </summary>
        internal static void BuildPinLayer(AnimatorServicesContext asc, VirtualAnimatorController fx, string baseName, IReadOnlyList<PinMember> members, BaseState baseState, Transform avatarRoot, bool defaultOn = false, ICollection<string> pinLayerNames = null, PinLayerState pinState = null)
        {
            var name = UniqueLayerName(fx, baseName + PinSuffix);
            // Recorded under its exact, already-deduped name: this is the only place a pin layer is created, so
            // this is the only place that gets to say a name is really one of ours.
            pinLayerNames?.Add(name);
            // The highest priority there is, so NDMF's own layer ordering keeps this after every layer added at the
            // default priority, including one Modular Avatar merges later in the same build: a pin only wins over
            // what sits below it, and until this a face tracking layer merged after AvatarDoc took the shape back.
            var layer = fx.AddLayer(new LayerPriority(int.MaxValue), name);
            var sm = layer.StateMachine;

            // A member's pause flag is 1 in its own state and 0 in every other member's state and in Off.
            var flags = members.Select(m => m.PauseFlag).Where(f => f != null).Distinct().ToList();
            foreach (var flag in flags)
            {
                EnsureParameter(fx, flag.Parameter, PlannedType.Float, 0f);
                pinState?.PauseFlags.Add(flag);
            }
            var offContent = new ClipContent(avatarRoot);
            foreach (var flag in flags) offContent.SetAnimatorParameter(flag.Parameter, 0f);
            var off = sm.AddState("Off", offContent.ToClip(asc, name + " Off"), new Vector3(0f, 0f, 0f));
            off.WriteDefaultValues = false;
            sm.DefaultState = off;

            // Every shape a member pins rests once on the way out, one VRChat blinks or speaks with too, at 0, where
            // VRChat's own blinking and lip sync start from: the member already binds it, so the frame of rest takes
            // nothing from them, and a captured face's half closed eyes never become the rest.
            var heldContext = baseState.HeldContext ?? ShapeSetModel.ContextFor(avatarRoot);
            var states = new List<VirtualState>();
            foreach (var member in members)
            {
                var content = new ClipContent(avatarRoot);
                content.WriteToggle(member.Entry, true, baseState, ShapeSet.PinsOnly);
                if (pinState != null)
                    foreach (var (renderer, shape, _) in content.ShapeWrites())
                        pinState.Bindings.Add(new PinLayerState.PinBinding { LayerName = name, Renderer = renderer, Shape = shape, RestValue = TrackedRest(heldContext, renderer, shape, baseState.WeightOf(renderer, shape)) });
                foreach (var flag in flags) content.SetAnimatorParameter(flag.Parameter, flag == member.PauseFlag ? 1f : 0f);
                var state = sm.AddState(member.StateName, content.ToClip(asc, name + " " + member.StateName), new Vector3(0f, 120f * (states.Count + 1), 0f));
                state.WriteDefaultValues = false;
                states.Add(state);
                HeldShapePruner.Record(pinState, content, member.Entry, name, sm, state, -1, baseState, heldContext);
            }

            // The pin layer follows its main layer: a toggle that is on by default starts with its pins written.
            if (defaultOn && states.Count == 1) sm.DefaultState = states[0];

            var back = pinState != null ? AddResetState(sm, name, off, new Vector3(300f, 0f, 0f), false, pinState, fadesWhileIdle: true) : off;
            var offTransitions = ImmutableList.CreateBuilder<VirtualStateTransition>();
            for (int i = 0; i < members.Count; i++)
            {
                foreach (var conditions in members[i].Enter) offTransitions.Add(Transition(states[i], conditions));
                states[i].Transitions = ImmutableList.CreateRange(members[i].Leave.Select(conditions => Transition(back, conditions)));
            }
            off.Transitions = offTransitions.ToImmutable();
        }

        static void BuildGroupLayer(AnimatorServicesContext asc, VirtualAnimatorController fx, bool writeDefaults, PlannedParameter group, ParameterPlan plan, BaseState baseState, Transform avatarRoot, List<ModelMessage> messages, ICollection<string> pinLayerNames, PinLayerState pinState = null, TouchBuilder.TouchParameters touch = null)
        {
            var name = UniqueLayerName(fx, "AD: " + group.GroupName);
            var layer = fx.AddLayer(LayerPriority.Default, name);
            var sm = layer.StateMachine;

            // Members left out on this platform keep their group value, so the numbering matches the other platform,
            // but they get no state and no place in the "nothing selected" condition.
            var members = group.Controls.Where(member => !member.Excluded).ToList();

            var customByMember = new Dictionary<ControlNode, List<(EditorCurveBinding binding, AnimationCurve curve, ObjectReferenceKeyframe[] keys)>>();
            foreach (var member in members)
                if (member.Entry.animations.Count > 0)
                    customByMember[member] = CustomClips.Collect(member.Entry, avatarRoot, messages);

            var offContent = new ClipContent(avatarRoot);
            foreach (var member in members) offContent.WriteToggle(member.Entry, false, baseState, ShapeSet.MainOnly);
            // Leaving the group entirely also restores every member's custom bindings, not only the selected one's.
            foreach (var member in members)
                if (customByMember.TryGetValue(member, out var memberCustom))
                    offContent.WriteCustomOff(memberCustom, baseState, member.Entry, messages);
            var off = sm.AddState("Off", offContent.ToClip(asc, name + " Off"), new Vector3(0f, 0f, 0f));
            off.WriteDefaultValues = writeDefaults;
            sm.DefaultState = off;

            var anyState = new List<VirtualStateTransition>();
            anyState.Add(Transition(off, NoMemberSelected(group, plan, members)));

            int row = 1;
            foreach (var member in members)
            {
                var content = new ClipContent(avatarRoot);
                foreach (var other in members)
                    if (other != member) content.WriteToggle(other.Entry, false, baseState, ShapeSet.MainOnly);
                // Written last so the on values win where members share a target.
                content.WriteToggle(member.Entry, true, baseState, ShapeSet.MainOnly);
                if (customByMember.TryGetValue(member, out var ownCustom)) content.MergeCustom(ownCustom, member.Entry, messages);

                var state = sm.AddState(member.Entry.PlainName, content.ToClip(asc, name + " " + member.Entry.PlainName), new Vector3(300f, 120f * row++, 0f));
                state.WriteDefaultValues = writeDefaults;
                anyState.Add(Transition(state, ImmutableList.Create(new AnimatorCondition
                {
                    parameter = group.Name,
                    mode = AnimatorConditionMode.Equals,
                    threshold = plan.Bindings[member].Value,
                })));
            }
            sm.AnyStateTransitions = ImmutableList.CreateRange(anyState);

            // The pin layer has a state for the pinned members only, and each one is left the moment its own value
            // is not selected, so an unpinned member shown in its place leaves the pin layer resting in Off.
            var pinned = members.Where(member => member.Entry.HasPins).ToList();
            if (pinned.Count > 0)
                BuildPinLayer(asc, fx, name, pinned.Select(member => new PinMember
                {
                    StateName = member.Entry.PlainName,
                    Entry = member.Entry,
                    Enter = new[] { Conditions(group, plan.Bindings[member].Value, true) },
                    Leave = new[] { Conditions(group, plan.Bindings[member].Value, false) },
                    PauseFlag = PauseFlagOf(touch, member.Entry),
                }).ToList(), baseState, avatarRoot, pinLayerNames: pinLayerNames, pinState: pinState);
        }

        /// <summary>The "none of these members is selected" conditions of a group layer: the parameter differs from every one of their values.</summary>
        static ImmutableList<AnimatorCondition> NoMemberSelected(PlannedParameter group, ParameterPlan plan, IEnumerable<ControlNode> members)
        {
            return ImmutableList.CreateRange(members.Select(member => new AnimatorCondition
            {
                parameter = group.Name,
                mode = AnimatorConditionMode.NotEqual,
                threshold = plan.Bindings[member].Value,
            }));
        }

        static void BuildRadialLayer(AnimatorServicesContext asc, VirtualAnimatorController fx, bool writeDefaults, ControlNode control, ControlBinding binding, Transform avatarRoot, List<ModelMessage> messages, BaseState baseState)
        {
            var name = UniqueLayerName(fx, control.LayerName);
            var custom = CustomClips.Collect(control.Entry, avatarRoot, messages);
            var layer = fx.AddLayer(LayerPriority.Default, name);

            if (custom.Count > 0)
            {
                var content = new ClipContent(avatarRoot);
                content.WriteRadialRamp(control.Entry, CustomClips.Length(control.Entry), baseState);
                content.MergeCustom(custom, control.Entry, messages);
                var merged = layer.StateMachine.AddState("Blend", content.ToClip(asc, name), new Vector3(0f, 0f, 0f));
                merged.WriteDefaultValues = writeDefaults;
                merged.TimeParameter = binding.Parameter.Name;
                layer.StateMachine.DefaultState = merged;
                return;
            }

            var min = new ClipContent(avatarRoot);
            min.WriteRadial(control.Entry, 0f, baseState);
            var max = new ClipContent(avatarRoot);
            max.WriteRadial(control.Entry, 1f, baseState);

            var tree = VirtualBlendTree.Create(name);
            tree.BlendType = BlendTreeType.Simple1D;
            tree.BlendParameter = binding.Parameter.Name;
            tree.UseAutomaticThresholds = false;
            tree.Children = ImmutableList.Create(
                new VirtualBlendTree.VirtualChildMotion { Motion = min.ToClip(asc, name + " Min"), Threshold = 0f },
                new VirtualBlendTree.VirtualChildMotion { Motion = max.ToClip(asc, name + " Max"), Threshold = 1f });

            var state = layer.StateMachine.AddState("Blend", tree, new Vector3(0f, 0f, 0f));
            state.WriteDefaultValues = writeDefaults;
            layer.StateMachine.DefaultState = state;
        }
    }
}
#endif
