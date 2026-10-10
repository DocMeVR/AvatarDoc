#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDKBase;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Creates the contact receivers and PhysBone parameters a Contact or PhysBone sourced trigger needs, on the
    /// avatar clone, and builds the flip layer for triggers in FlipToggle mode.
    /// </summary>
    public static class TouchBuilder
    {
        /// <summary>The resolved animator parameter and comparison one touch sourced trigger reads, whichever mode it is in.</summary>
        public sealed class TouchInfo
        {
            public string Parameter = "";
            public PlannedType Type;
            public ParameterCondition Mode;
            public float Value;
            /// <summary>PhysBone only: the prefix Create assigns to the PhysBone if it still has none by then. Equal to its own parameter otherwise, so Create's write is a no-op.</summary>
            public string PhysBoneParameter = "";
            /// <summary>Contact only: the receiver's radius, clamped to what the inspector allows. Old data saved before the field had a range can hold 0, which would make a receiver nothing can touch.</summary>
            public float Radius = 0.05f;
            /// <summary>The trigger this touch belongs to, and the condition of it that asked for the receiver or the PhysBone. Create writes from the condition, so a trigger with two contact conditions gets a receiver for each.</summary>
            public MenuEntry Owner;
            public WhenCondition Source;
            /// <summary>The name the receiver's GameObject takes on the clone. Empty on a PhysBone, which makes no object of its own.</summary>
            public string ObjectName = "";
            /// <summary>Follow distance only: the receiver is always Proximity, whatever the condition's own contactProximity holds.</summary>
            public bool ForceProximity;
        }

        public sealed class TouchParameters
        {
            /// <summary>What each condition reads, by TouchKey: the entry's own id while the trigger works from its flat source fields, the entry id plus the condition's place once it has groups.</summary>
            public Dictionary<string, TouchInfo> ByTriggerId = new Dictionary<string, TouchInfo>();
            /// <summary>One entry per component the build makes on the clone, in the order they were described. Conditions that share a receiver or a PhysBone appear once.</summary>
            public List<TouchInfo> Created = new List<TouchInfo>();
            /// <summary>Every blend shape trigger's hidden copy of its shape, by entry id: an animator only Float, named here so it never takes a name a touch or a control already has.</summary>
            public Dictionary<string, string> ShapeMirrors = new Dictionary<string, string>();
            /// <summary>The pause flag of every Fade contact and blend shape trigger that moves a shape VRChat blinks or speaks with, by entry id.</summary>
            public Dictionary<string, PauseFlag> PauseFlags = new Dictionary<string, PauseFlag>();
        }

        /// <summary>
        /// An animator only Float a driven layer (a Fade contact's or a blend shape trigger's) writes 0 in its rest
        /// state and 1 in every state that moves its effects, for the shared blink and lip sync pause layers to
        /// read. Those states are only reached while every requirement holds, so the pause follows the effects.
        /// </summary>
        public sealed class PauseFlag
        {
            public string Parameter = "";
            public bool Eyes;
            public bool Mouth;
        }

        /// <summary>The name a pause flag starts from, before it is made unique. Pure: depends only on the trigger and the prefix.</summary>
        public static string PauseFlagFor(MenuEntry trigger, string prefix)
        {
            var p = string.IsNullOrWhiteSpace(prefix) ? "AD" : prefix.Trim();
            return p + "/Pause/" + trigger.PlainName;
        }

        /// <summary>The name a blend shape trigger's hidden copy of its shape starts from, before it is made unique. Pure: depends only on the trigger and the prefix.</summary>
        public static string ShapeMirrorFor(MenuEntry trigger, string prefix)
        {
            var p = string.IsNullOrWhiteSpace(prefix) ? "AD" : prefix.Trim();
            return p + "/Shape/" + trigger.PlainName;
        }

        /// <summary>Where one touch condition's parameter is filed. A trigger that still works from its flat source fields keeps the entry's own id, so nothing that looked a trigger up by it has to change.</summary>
        public static string TouchKey(MenuEntry entry, int groupIndex, int index)
        {
            return entry.UsesWhenGroups ? entry.id + "#" + groupIndex + "." + index : entry.id;
        }

        /// <summary>
        /// The condition Follow distance is driven by: the first condition among the trigger's groups (or its flat
        /// fields when it has none), with its place for TouchKey, and whether conditions sit outside the card it
        /// is in, which the caller reports without letting it stop the build. The rest of that card are its
        /// requirements (MenuEntry.Requirements).
        /// </summary>
        internal static (int groupIndex, int index, WhenCondition condition, bool tooMany) FollowDistancePrimary(MenuEntry entry)
        {
            foreach (var (groupIndex, group) in entry.EffectiveGroups())
                for (int i = 0; i < group.all.Count; i++)
                    if (group.all[i] != null) return (groupIndex, i, group.all[i], entry.IgnoredConditionCount > 0);
            return (0, 0, null, false);
        }

        static readonly Dictionary<PhysBoneEvent, (string suffix, PlannedType type)> PhysBoneEvents = new Dictionary<PhysBoneEvent, (string, PlannedType)>
        {
            { PhysBoneEvent.Grabbed, ("_IsGrabbed", PlannedType.Bool) },
            { PhysBoneEvent.Posed, ("_IsPosed", PlannedType.Bool) },
            { PhysBoneEvent.AngleAbove, ("_Angle", PlannedType.Float) },
            { PhysBoneEvent.StretchAbove, ("_Stretch", PlannedType.Float) },
            { PhysBoneEvent.SquishAbove, ("_Squish", PlannedType.Float) },
        };

        /// <summary>The animator parameter a Contact trigger's receiver carries, or the parameter prefix a PhysBone gets when it has none of its own. Pure: depends only on the trigger and the prefix.</summary>
        public static string ParameterFor(MenuEntry trigger, string prefix)
        {
            var p = string.IsNullOrWhiteSpace(prefix) ? "AD" : prefix.Trim();
            return trigger.source == TriggerSource.PhysBone
                ? p + "/PB/" + trigger.PlainName
                : p + "/Contact/" + trigger.PlainName;
        }

        /// <summary>
        /// Names and types only: what every touch sourced trigger will read, and what a PhysBone without a
        /// parameter of its own will be given. Pure: no GameObject, no AddComponent, no write to any component,
        /// so the planner (a real build) and the inspector's cached plan (never a build) can both call it. Runs
        /// before TriggerPlanner.Plan.
        /// </summary>
        public static TouchParameters Describe(MenuTree tree, IReadOnlyDictionary<string, ExistingParameter> existing, ParameterPlan plan = null)
        {
            var touch = new TouchParameters();
            var used = new HashSet<string>();
            // The plan's own names are not in existing, and a control with a custom name can hold exactly the name
            // a touch source would take; the one declared first would then decide the type for both.
            if (plan != null)
                foreach (var parameter in plan.Parameters) used.Add(parameter.Name);
            var physBoneBase = new Dictionary<Component, string>();
            var descriptor = tree.AvatarRoot != null ? tree.AvatarRoot.GetComponent<VRCAvatarDescriptor>() : null;
            foreach (var node in tree.Triggers)
            {
                var entry = node.Entry;
                if (node.Excluded) continue;
                if (entry.kind != EntryKind.Trigger || !entry.HasEffects) continue;
                if (entry.HasDriver)
                {
                    FollowDistanceTracking(entry, descriptor, out bool eyes, out bool mouth);
                    if (eyes || mouth)
                    {
                        var flagPrefix = node.Component != null ? node.Component.parameterPrefix : "AD";
                        touch.PauseFlags[entry.id] = new PauseFlag { Parameter = UniqueTouchName(PauseFlagFor(entry, flagPrefix), existing, used), Eyes = eyes, Mouth = mouth };
                    }
                }
                // A flip trigger whose toggle is not built here has nothing to flip (ad.info.flip_target_excluded),
                // so it gets no receiver either: a contact costs on the platform where the count matters.
                if (entry.IsFlipTrigger && FlipTargetIsGone(tree, entry)) continue;
                // Follow distance is driven by one contact, forced to a Proximity receiver: the driver never shares
                // the per-condition receivers below, which only its requirements use.
                if (entry.IsFollowDistanceTrigger)
                {
                    DescribeFollowDistance(tree, node, existing, used, touch);
                    DescribeConditions(tree, node, entry.Requirements(), existing, used, physBoneBase, touch);
                    continue;
                }
                if (entry.source == TriggerSource.BlendShape)
                {
                    if (entry.IgnoredConditionCount > 0)
                        tree.Messages.Add(ModelMessage.Warning("ad.warn.requirements_one_card", entry.PlainName, node.Component).For(entry.id));
                    var prefix = node.Component != null ? node.Component.parameterPrefix : "AD";
                    touch.ShapeMirrors[entry.id] = UniqueTouchName(ShapeMirrorFor(entry, prefix), existing, used);
                }
                // A blend shape trigger's groups are its requirement group alone (MenuEntry.EffectiveGroups).
                DescribeConditions(tree, node, entry.EffectiveGroups().SelectMany(g => g.group.all.Select((condition, i) => (g.groupIndex, i, condition))), existing, used, physBoneBase, touch);
            }
            return touch;
        }

        /// <summary>
        /// The receiver or PhysBone of every touch condition given, filed by TouchKey. Two of them that ask for the
        /// very same contact read one receiver: a second one would cost a parameter and a component for a value the
        /// first already carries.
        /// </summary>
        static void DescribeConditions(MenuTree tree, TriggerNode node, IEnumerable<(int groupIndex, int index, WhenCondition condition)> conditions, IReadOnlyDictionary<string, ExistingParameter> existing, HashSet<string> used, Dictionary<Component, string> physBoneBase, TouchParameters touch)
        {
            var entry = node.Entry;
            var shared = new Dictionary<string, TouchInfo>();
            foreach (var (groupIndex, i, condition) in conditions)
            {
                if (condition == null || !condition.IsTouch) continue;
                var key = TouchKey(entry, groupIndex, i);
                var label = entry.UsesWhenGroups ? entry.PlainName + " " + (groupIndex + 1) + "." + (i + 1) : entry.PlainName;
                if (condition.kind == WhenConditionKind.Contact) DescribeContact(tree, node, condition, key, label, existing, used, shared, touch);
                else DescribePhysBone(tree, node, condition, key, existing, used, physBoneBase, touch);
            }
        }

        /// <summary>Whether a flip trigger has no toggle to flip on this platform: missing, or left out here.</summary>
        static bool FlipTargetIsGone(MenuTree tree, MenuEntry entry)
        {
            var target = tree.FindControl(entry.flipToggleId);
            return target == null || target.Excluded;
        }

        static void DescribeContact(MenuTree tree, TriggerNode node, WhenCondition condition, string key, string label, IReadOnlyDictionary<string, ExistingParameter> existing, HashSet<string> used, Dictionary<string, TouchInfo> shared, TouchParameters touch)
        {
            var entry = node.Entry;
            if (condition.contactAnchor == null) return;
            // Nothing a build makes may land outside the avatar being built. Create, the planner and the flip
            // layers all key off the described conditions, so leaving this one undescribed covers all three.
            if (MenuModel.IsOutsideAvatar(tree.AvatarRoot, condition.contactAnchor)) return;

            var settings = ContactSettings(condition);
            if (shared.TryGetValue(settings, out var already)) { touch.ByTriggerId[key] = already; return; }

            var prefix = node.Component != null ? node.Component.parameterPrefix : "AD";
            var name = UniqueTouchName(ParameterFor(entry, prefix), existing, used);
            bool proximity = condition.contactProximity > 0f;

            var info = new TouchInfo
            {
                Parameter = name,
                Type = proximity ? PlannedType.Float : PlannedType.Bool,
                Mode = proximity ? ParameterCondition.Greater : ParameterCondition.IsTrue,
                Value = proximity ? condition.contactProximity : 0f,
                Radius = Mathf.Clamp(condition.contactRadius, 0.005f, 1f),
                Owner = entry,
                Source = condition,
                ObjectName = "AD Contact " + label,
            };
            shared[settings] = info;
            touch.ByTriggerId[key] = info;
            touch.Created.Add(info);
        }

        /// <summary>
        /// Follow distance's own receiver: always Proximity and Float, whatever the condition's own contactProximity
        /// holds, and never shared with another condition's receiver, since sharing would force that other
        /// condition's receiver type to match this one's.
        /// </summary>
        static void DescribeFollowDistance(MenuTree tree, TriggerNode node, IReadOnlyDictionary<string, ExistingParameter> existing, HashSet<string> used, TouchParameters touch)
        {
            var entry = node.Entry;
            if (!entry.IsFollowDistanceTrigger) return;
            var (groupIndex, index, condition, tooMany) = FollowDistancePrimary(entry);
            if (tooMany) tree.Messages.Add(ModelMessage.Warning("ad.warn.follow_distance_one_contact", entry.PlainName, node.Component).For(entry.id));
            if (condition == null || condition.contactAnchor == null) return;
            if (MenuModel.IsOutsideAvatar(tree.AvatarRoot, condition.contactAnchor)) return;

            var prefix = node.Component != null ? node.Component.parameterPrefix : "AD";
            var name = UniqueTouchName(ParameterFor(entry, prefix), existing, used);
            var info = new TouchInfo
            {
                Parameter = name,
                Type = PlannedType.Float,
                Mode = ParameterCondition.Greater,
                Value = 0f,
                Radius = Mathf.Clamp(condition.contactRadius, 0.005f, 1f),
                Owner = entry,
                Source = condition,
                ObjectName = "AD Contact " + entry.PlainName,
                ForceProximity = true,
            };
            touch.ByTriggerId[TouchKey(entry, groupIndex, index)] = info;
            touch.Created.Add(info);
        }

        /// <summary>
        /// Whether a Fade contact's or a blend shape trigger's effects write a blend shape the avatar descriptor uses
        /// for blinking (eyelids, Blendshapes type, only while eye look is on) or for a viseme or a jaw flap shape
        /// (only while lip sync is set to read blend shapes), matched by mesh and shape name. Every shape mode counts
        /// here: neither has a separate pin layer of its own to reserve Pin's usual exclusion for.
        /// </summary>
        public static void FollowDistanceTracking(MenuEntry entry, VRCAvatarDescriptor descriptor, out bool eyes, out bool mouth)
        {
            eyes = false;
            mouth = false;
            var (eyelidShapes, mouthShapes) = DescriptorDrivenShapes(descriptor);
            foreach (var target in entry.blendShapes)
            {
                if (!MenuModel.ShapeExists(target.renderer, target.shape)) continue;
                // A blend shape trigger never writes its own shape, so that row moves nothing VRChat tracks.
                if (entry.source == TriggerSource.BlendShape && target.renderer == entry.sourceRenderer && target.shape == entry.sourceShape) continue;
                var key = (target.renderer, target.shape);
                if (eyelidShapes.Contains(key)) eyes = true;
                if (mouthShapes.Contains(key)) mouth = true;
            }
        }

        /// <summary>
        /// Every (mesh, shape) the avatar descriptor itself reads for blinking or for lip sync, the one place both
        /// FollowDistanceTracking (which pause layer a trigger needs) and BuildFollowDistanceLayers (which shapes
        /// Far must never hold, whatever LayerOwnsBinding says, since VRChat's own tracking owns them) ask.
        /// Eyelids count only while enableEyeLook is on and the eyelid type is Blendshapes; a mouth shape counts
        /// for LipSyncStyle.VisemeBlendShape (against VisemeBlendShapes) or LipSyncStyle.JawFlapBlendShape (against
        /// MouthOpenBlendShapeName), both on VisemeSkinnedMesh.
        /// </summary>
        internal static (HashSet<(SkinnedMeshRenderer renderer, string shape)> eyelids, HashSet<(SkinnedMeshRenderer renderer, string shape)> mouth) DescriptorDrivenShapes(VRCAvatarDescriptor descriptor)
        {
            var eyelids = new HashSet<(SkinnedMeshRenderer, string)>();
            var mouth = new HashSet<(SkinnedMeshRenderer, string)>();
            if (descriptor == null) return (eyelids, mouth);

            var eye = descriptor.customEyeLookSettings;
            if (descriptor.enableEyeLook && eye.eyelidType == VRCAvatarDescriptor.EyelidType.Blendshapes
                && eye.eyelidsSkinnedMesh != null && eye.eyelidsSkinnedMesh.sharedMesh != null && eye.eyelidsBlendshapes != null)
                foreach (var shapeIndex in eye.eyelidsBlendshapes)
                    if (shapeIndex >= 0 && shapeIndex < eye.eyelidsSkinnedMesh.sharedMesh.blendShapeCount)
                        eyelids.Add((eye.eyelidsSkinnedMesh, eye.eyelidsSkinnedMesh.sharedMesh.GetBlendShapeName(shapeIndex)));

            if (descriptor.lipSync == VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape && descriptor.VisemeSkinnedMesh != null && descriptor.VisemeBlendShapes != null)
                foreach (var shapeName in descriptor.VisemeBlendShapes)
                    if (!string.IsNullOrEmpty(shapeName)) mouth.Add((descriptor.VisemeSkinnedMesh, shapeName));
            if (descriptor.lipSync == VRC_AvatarDescriptor.LipSyncStyle.JawFlapBlendShape && descriptor.VisemeSkinnedMesh != null && !string.IsNullOrEmpty(descriptor.MouthOpenBlendShapeName))
                mouth.Add((descriptor.VisemeSkinnedMesh, descriptor.MouthOpenBlendShapeName));

            return (eyelids, mouth);
        }

        /// <summary>Everything about a contact condition that decides what its receiver looks like. Two conditions that read the same here share one receiver.</summary>
        static string ContactSettings(WhenCondition condition)
        {
            var tags = condition.contactTags != null ? string.Join(",", condition.contactTags.Where(t => !string.IsNullOrWhiteSpace(t)).OrderBy(t => t)) : "";
            return condition.contactAnchor.GetInstanceID() + "|" + condition.contactOffset + "|" + condition.contactRadius
                + "|" + tags + "|" + condition.contactAllowSelf + "|" + condition.contactAllowOthers + "|" + condition.contactProximity;
        }

        static void DescribePhysBone(MenuTree tree, TriggerNode node, WhenCondition condition, string key, IReadOnlyDictionary<string, ExistingParameter> existing, HashSet<string> used, Dictionary<Component, string> physBoneBase, TouchParameters touch)
        {
            var entry = node.Entry;
            var pb = condition.physBone;
            if (pb == null) return;
            if (MenuModel.IsOutsideAvatar(tree.AvatarRoot, pb.transform)) return;

            string basePrefix;
            if (!string.IsNullOrEmpty(pb.parameter))
            {
                // Reading an existing value is not a write, and it is not made unique: VRChat already writes
                // this PhysBone's own suffixed parameters under that exact name.
                basePrefix = pb.parameter;
            }
            else if (!physBoneBase.TryGetValue(pb, out basePrefix))
            {
                var prefix = node.Component != null ? node.Component.parameterPrefix : "AD";
                basePrefix = UniqueTouchName(ParameterFor(entry, prefix), existing, used);
                physBoneBase[pb] = basePrefix;
            }

#if DOCME_MA
            if (!string.IsNullOrEmpty(pb.parameter) && ModularAvatarRenames(pb, pb.parameter))
                tree.Messages.Add(ModelMessage.Warning("ad.warn.physbone_renamed", entry.PlainName, pb.name, node.Component).For(entry.id));
#endif

            var (suffix, type) = PhysBoneEvents[condition.physBoneEvent];
            var suffixed = basePrefix + suffix;
            if (!string.IsNullOrEmpty(pb.parameter) && existing.TryGetValue(suffixed, out var declared) && declared.Type != type)
                tree.Messages.Add(ModelMessage.Error("ad.error.touch_parameter_type", entry.PlainName, suffixed, type.ToString(), declared.Type.ToString(), node.Component).For(entry.id));

            bool boolEvent = type == PlannedType.Bool;
            // A threshold of 0 on a float event fires the moment the bone moves at all, which is never what the
            // author meant: old data saved before the field had its default gets the spec's 0.5 instead.
            float threshold = condition.physBoneThreshold == 0f ? 0.5f : condition.physBoneThreshold;
            var info = new TouchInfo
            {
                Parameter = suffixed,
                Type = type,
                Mode = boolEvent ? ParameterCondition.IsTrue : ParameterCondition.Greater,
                Value = boolEvent ? 0f : threshold,
                PhysBoneParameter = basePrefix,
                Owner = entry,
                Source = condition,
            };
            touch.ByTriggerId[key] = info;
            // The PhysBone itself is written once, whichever condition got there first; the rest read the same parameter.
            if (!touch.Created.Any(other => ReferenceEquals(other.Source.physBone, pb))) touch.Created.Add(info);
        }

#if DOCME_MA
        /// <summary>
        /// Whether a Modular Avatar Parameters component at or above this PhysBone renames its prefix at build.
        /// Modular Avatar's rename pass runs after AvatarDoctor's, so a trigger on such a PhysBone would watch a
        /// name that is gone by then. The generated name is not predicted here, only the fact of the rename.
        /// </summary>
        static bool ModularAvatarRenames(Component physBone, string prefix)
        {
            if (physBone == null || string.IsNullOrEmpty(prefix)) return false;
            for (var cursor = physBone.transform; cursor != null; cursor = cursor.parent)
            {
                var parameters = cursor.GetComponent<nadena.dev.modular_avatar.core.ModularAvatarParameters>();
                if (parameters == null || parameters.parameters == null) continue;
                foreach (var config in parameters.parameters)
                    if (config.isPrefix && config.nameOrPrefix == prefix && (config.internalParameter || !string.IsNullOrEmpty(config.remapTo)))
                        return true;
            }
            return false;
        }
#endif

        internal static string UniqueTouchName(string baseName, IReadOnlyDictionary<string, ExistingParameter> existing, HashSet<string> used)
        {
            var candidate = baseName;
            for (int i = 2; existing.ContainsKey(candidate) || used.Contains(candidate); i++)
                candidate = baseName + " " + i;
            used.Add(candidate);
            return candidate;
        }

        /// <summary>Creates a receiver per Contact trigger and assigns a parameter to a PhysBone that has none, on the avatar clone. Runs inside the build's error gate, after Describe's names are already planned.</summary>
        public static List<ModelMessage> Create(BuildContext context, MenuTree tree, TouchParameters touch)
        {
            var messages = new List<ModelMessage>();
            foreach (var info in touch.Created)
            {
                if (info.Source == null) continue;
                if (info.Source.kind == WhenConditionKind.Contact) CreateContact(info);
                else CreatePhysBone(info);
            }
            return messages;
        }

        static void CreateContact(TouchInfo info)
        {
            var condition = info.Source;
            if (condition.contactAnchor == null) return;

            var go = new GameObject(info.ObjectName);
            go.transform.SetParent(condition.contactAnchor, false);
            go.transform.localPosition = condition.contactOffset;
            go.transform.localRotation = Quaternion.identity;

            var receiver = go.AddComponent<VRCContactReceiver>();
            receiver.shapeType = ContactBase.ShapeType.Sphere;
            receiver.radius = info.Radius;
            receiver.position = Vector3.zero;
            receiver.rotation = Quaternion.identity;
            receiver.allowSelf = condition.contactAllowSelf;
            receiver.allowOthers = condition.contactAllowOthers;
            receiver.localOnly = false;
            receiver.collisionTags = new List<string>(condition.contactTags ?? new List<string>());

            bool proximity = info.ForceProximity || condition.contactProximity > 0f;
            receiver.receiverType = proximity ? ContactReceiver.ReceiverType.Proximity : ContactReceiver.ReceiverType.Constant;
            receiver.parameter = info.Parameter;
        }

        static void CreatePhysBone(TouchInfo info)
        {
            var pb = info.Source.physBone;
            if (pb == null) return;
            if (string.IsNullOrEmpty(pb.parameter)) pb.parameter = info.PhysBoneParameter;
        }

        /// <summary>One FX layer per FlipToggle trigger: Idle, Set on and Set off, with a local parameter driver that flips the target toggle's own parameter. The conditions come from the same resolver a hold trigger uses, so a flip fires once per way its "when" is met.</summary>
        public static List<ModelMessage> BuildFlipLayers(AnimatorServicesContext asc, MenuTree tree, ParameterPlan plan, TouchParameters touch, IReadOnlyDictionary<string, ExistingParameter> existing = null, ICollection<string> pinLayerNames = null)
        {
            var messages = new List<ModelMessage>();
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return messages;
            bool writeDefaults = AnimatorBuilder.DecideWriteDefaults(fx, pinLayerNames);

            foreach (var node in tree.Triggers)
            {
                var entry = node.Entry;
                if (node.Excluded) continue;
                if (entry.kind != EntryKind.Trigger || !entry.HasEffects) continue;
                if (!entry.IsFlipTrigger) continue;
                var target = tree.FindControl(entry.flipToggleId);
                if (target == null) continue;
                // The model already said so with ad.info.flip_target_excluded; there is no toggle here to flip.
                if (target.Excluded) continue;
                if (!plan.Bindings.TryGetValue(target, out var binding))
                {
                    messages.Add(ModelMessage.Error("ad.error.flip_target", entry.PlainName, node.Component).For(entry.id));
                    continue;
                }

                if (!TriggerPlanner.Resolve(tree, plan, node, existing, touch, messages, out var conditions, out var met)) continue;
                // Idle only reacts to a touch while the gate is met; releasing an already open flip is not gated.
                var gate = TriggerPlanner.AndGate(Enumerable.Empty<AnimatorCondition>(), TriggerPlanner.BuildGate(tree, plan, entry));
                if (TriggerPlanner.LeaveTooComplex(node, met, gate, messages)) continue;
                foreach (var condition in conditions)
                    AnimatorBuilder.EnsureParameter(fx, condition.Parameter, condition.Type, condition.Default);
                var isOff = AnimatorBuilder.Conditions(binding.Parameter, binding.Value, false);
                var isOn = AnimatorBuilder.Conditions(binding.Parameter, binding.Value, true);
                var isInt = TriggerPlanner.IntLookup(conditions);
                var enterSetOn = ConditionLogic.Enter(met.Select(m => m.Concat(isOff).ToList()).ToList(), gate, isInt);
                var enterSetOff = ConditionLogic.Enter(met.Select(m => m.Concat(isOn).ToList()).ToList(), gate, isInt);
                var released = ConditionLogic.Leave(met, null, isInt);

                var name = AnimatorBuilder.UniqueLayerName(fx, "AD: " + entry.PlainName + " (flip)");
                var layer = fx.AddLayer(LayerPriority.Default, name);
                var sm = layer.StateMachine;
                var idle = sm.AddState("Idle", new ClipContent(tree.AvatarRoot).ToClip(asc, name + " Idle"), new Vector3(0f, 0f, 0f));
                var setOn = sm.AddState("Set on", new ClipContent(tree.AvatarRoot).ToClip(asc, name + " Set on"), new Vector3(300f, 0f, 0f));
                var setOff = sm.AddState("Set off", new ClipContent(tree.AvatarRoot).ToClip(asc, name + " Set off"), new Vector3(300f, 150f, 0f));
                idle.WriteDefaultValues = writeDefaults;
                setOn.WriteDefaultValues = writeDefaults;
                setOff.WriteDefaultValues = writeDefaults;
                sm.DefaultState = idle;

                setOn.Behaviours = ImmutableList.Create<StateMachineBehaviour>(Driver(binding.Parameter.Name, binding.Value));
                setOff.Behaviours = ImmutableList.Create<StateMachineBehaviour>(Driver(binding.Parameter.Name, 0f));

                idle.Transitions = ImmutableList.CreateRange(
                    enterSetOn.Select(list => AnimatorBuilder.Transition(setOn, ImmutableList.CreateRange(list)))
                    .Concat(enterSetOff.Select(list => AnimatorBuilder.Transition(setOff, ImmutableList.CreateRange(list)))));
                var back = released.Select(list => ImmutableList.CreateRange(list)).ToList();
                setOn.Transitions = ImmutableList.CreateRange(back.Select(list => AnimatorBuilder.Transition(idle, list)));
                setOff.Transitions = ImmutableList.CreateRange(back.Select(list => AnimatorBuilder.Transition(idle, list)));
            }
            return messages;
        }

        /// <summary>
        /// One layer per Follow distance trigger: Far (the off pose, its blend shapes and material properties left
        /// to the same deferred release pass a pin layer's off state uses) and Near (a Simple1D blend from the off pose to the on pose,
        /// over the receiver's own float). Built exactly like a real pin layer, both states Write Defaults off, at
        /// the highest priority and named into pinLayerNames, so it always ends up above whatever it shares a
        /// binding with, face tracking merged in later included, and it never counts as its own owner. Every shape
        /// mode blends the same way here; there is no separate pin layer to reserve Pin's usual behaviour for. The
        /// receiver's float never enters the expression parameters, so this costs no bits.
        /// </summary>
        /// <param name="triggerPlans">The planner's plans, where each Fade contact's requirements are resolved. A Fade contact that has requirements but no plan (one of them did not resolve) builds nothing; one without requirements needs none.</param>
        public static List<ModelMessage> BuildFollowDistanceLayers(AnimatorServicesContext asc, MenuTree tree, TouchParameters touch, BaseState baseState, VRCAvatarDescriptor descriptor, ICollection<string> pinLayerNames, PinLayerState pinState, IReadOnlyList<TriggerPlan> triggerPlans = null)
        {
            var messages = new List<ModelMessage>();
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return messages;
            var (eyelidShapes, mouthShapes) = DescriptorDrivenShapes(descriptor);

            foreach (var node in tree.Triggers)
            {
                var entry = node.Entry;
                if (node.Excluded) continue;
                if (entry.kind != EntryKind.Trigger || !entry.HasEffects) continue;
                if (!entry.IsFollowDistanceTrigger) continue;
                var (groupIndex, index, condition, _) = FollowDistancePrimary(entry);
                if (condition == null || condition.kind != WhenConditionKind.Contact) continue;
                if (!touch.ByTriggerId.TryGetValue(TouchKey(entry, groupIndex, index), out var info)) continue;
                var requirements = triggerPlans?.FirstOrDefault(p => p.Kind == TriggerKind.Fade && p.Entry == entry);
                if (requirements == null && (entry.Requirements().Any() || entry.HasGate)) continue;

                AnimatorBuilder.EnsureParameter(fx, info.Parameter, PlannedType.Float, 0f);
                if (requirements != null)
                    foreach (var required in requirements.Conditions.Concat(requirements.Gate))
                        AnimatorBuilder.EnsureParameter(fx, required.Parameter, required.Type, required.Default);

                // Every shape mode (Fixed, Follow, Pin) writes the same off and on value here: Follow distance has
                // no separate pin layer, so Pin's usual "no curve while off" exclusion would only make the shape
                // jump at the boundary instead of blending, which WriteAllShapes avoids.
                var offContent = new ClipContent(tree.AvatarRoot);
                offContent.WriteToggle(entry, false, baseState, ShapeSet.All);
                WriteAllShapes(offContent, entry, false, baseState, tree.AvatarRoot);
                var onContent = new ClipContent(tree.AvatarRoot);
                onContent.WriteToggle(entry, true, baseState, ShapeSet.All);
                WriteAllShapes(onContent, entry, true, baseState, tree.AvatarRoot);

                var name = AnimatorBuilder.UniqueLayerName(fx, "AD: " + entry.PlainName + " (distance)");
                pinLayerNames?.Add(name);
                var layer = fx.AddLayer(new LayerPriority(int.MaxValue), name);
                var sm = layer.StateMachine;

                // Far carries no curve of its own for the shapes the on pose writes: the same deferred pass that
                // releases a pin's off state hands them back to rest only once nothing else, such as face
                // tracking's own Direct tree, already holds them, so blinking keeps working while no hand is near.
                // The rest value it releases to is the off pose's own value, the same one Near's off child shows,
                // so nothing jumps at the boundary; the only difference is a binding some other layer already owns.
                var farContent = new ClipContent(tree.AvatarRoot);
                farContent.WriteFarPose(entry, baseState);
                // A property follows the same rule as a shape here: Far rests it only when no other layer owns it,
                // or this layer, last in FX, would hide a toggle that moves the same property for good.
                if (pinState != null)
                    foreach (var (renderer, property, value) in farContent.TakePropertyWrites())
                        pinState.PropertyRests.Add(new PinLayerState.PropertyRest { LayerName = name, Renderer = renderer, Property = property, RestValue = value });
                // The pause layers read this flag: 0 in Far, 1 in Near, which is only reached while every
                // requirement holds, so blinking or lip sync pauses exactly while the effects move.
                if (touch.PauseFlags.TryGetValue(entry.id, out var pause))
                {
                    AnimatorBuilder.EnsureParameter(fx, pause.Parameter, PlannedType.Float, 0f);
                    farContent.SetAnimatorParameter(pause.Parameter, 0f);
                    offContent.SetAnimatorParameter(pause.Parameter, 1f);
                    onContent.SetAnimatorParameter(pause.Parameter, 1f);
                    pinState?.PauseFlags.Add(pause);
                }
                var far = sm.AddState("Far", farContent.ToClip(asc, name + " Far"), new Vector3(0f, 0f, 0f));
                far.WriteDefaultValues = false;
                sm.DefaultState = far;

                // A shape the descriptor itself drives (an eyelid or a viseme/jaw flap shape) is never registered
                // for release: VRChat's own blinking or lip sync owns it while no hand is near, whatever
                // LayerOwnsBinding would say, and Far must stay free of it even when nothing else currently holds
                // it (no pause layer built because another system already arbitrates, say).
                if (pinState != null)
                    foreach (var (renderer, shape, weight) in offContent.ShapeWrites())
                    {
                        var key = (renderer, shape);
                        if (eyelidShapes.Contains(key) || mouthShapes.Contains(key)) continue;
                        pinState.Bindings.Add(new PinLayerState.PinBinding { LayerName = name, Renderer = renderer, Shape = shape, RestValue = weight });
                    }

                float fullAt = entry.EffectiveContactFullAt;
                var nearTree = VirtualBlendTree.Create(name + " Near");
                nearTree.BlendType = BlendTreeType.Simple1D;
                nearTree.BlendParameter = info.Parameter;
                nearTree.UseAutomaticThresholds = false;
                nearTree.Children = ImmutableList.Create(
                    new VirtualBlendTree.VirtualChildMotion { Motion = offContent.ToClip(asc, name + " Near Off"), Threshold = 0f },
                    new VirtualBlendTree.VirtualChildMotion { Motion = onContent.ToClip(asc, name + " Near On"), Threshold = fullAt });
                var near = sm.AddState("Near", nearTree, new Vector3(300f, 0f, 0f));
                near.WriteDefaultValues = false;

                // Requirements join the way in (every one must hold) and add a way out each (any one failing
                // leaves). With none, this is the one transition each way the layer always had.
                var enter = requirements != null ? requirements.Enter : new List<List<AnimatorCondition>> { new List<AnimatorCondition>() };
                var leave = requirements != null ? requirements.Leave : new List<List<AnimatorCondition>>();
                far.Transitions = ImmutableList.CreateRange(enter.Select(e =>
                    AnimatorBuilder.Transition(near, ImmutableList.Create(Cond(info.Parameter, AnimatorConditionMode.Greater, 0.001f)).AddRange(e))));
                near.Transitions = ImmutableList.Create(AnimatorBuilder.Transition(far, ImmutableList.Create(Cond(info.Parameter, AnimatorConditionMode.Less, 0.001f))))
                    .AddRange(leave.Select(l => AnimatorBuilder.Transition(far, ImmutableList.CreateRange(l))));
            }
            return messages;
        }

        /// <summary>
        /// Writes every blend shape target's off or on value, whatever its mode: Follow distance treats Fixed,
        /// Follow and Pin the same, since there is no separate pin layer here to reserve Pin's usual "no curve
        /// while off" rule for. WriteToggle already writes a non-Pin target correctly with ShapeSet.All; this only
        /// has to cover the Pin targets it skips on its own off branch.
        /// </summary>
        static void WriteAllShapes(ClipContent content, MenuEntry entry, bool on, BaseState baseState, Transform avatarRoot)
        {
            if (on) return;
            foreach (var target in entry.blendShapes)
            {
                if (target.mode != ShapeEffectMode.Pin) continue;
                if (!MenuModel.ShapeExists(target.renderer, target.shape) || MenuModel.IsOutsideAvatar(avatarRoot, target.renderer.transform)) continue;
                content.SetShape(target.renderer, target.shape, EffectMath.ToggleShapeWeight(target, false, baseState.WeightOf(target.renderer, target.shape)));
            }
        }

        /// <summary>
        /// One shared layer per face part (Eyes, Mouth), for every driven layer (a Fade contact's or a blend shape
        /// trigger's) whose effects move it, instead of a tracking control per trigger: Idle (the default, no
        /// behaviour at all, so nothing changes at load or while another system owns the part), Paused (sets the
        /// part to Animation on entry, one transition in for each contributing layer's pause flag, an OR), Release
        /// (sets the part back to Tracking on entry, then falls straight through to Idle). It reads the flags the
        /// driven layers actually built (PinLayerState.PauseFlags), so it runs after every one of them, and a
        /// layer skipped for a requirement that did not resolve contributes nothing. A pin layer for ordering,
        /// built only when at least one layer needs the part.
        /// </summary>
        public static List<ModelMessage> BuildTrackingPauseLayers(AnimatorServicesContext asc, Transform avatarRoot, ICollection<string> pinLayerNames, PinLayerState pinState)
        {
            var messages = new List<ModelMessage>();
            if (pinState == null) return messages;
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return messages;

            var eyeParameters = pinState.PauseFlags.Where(f => f.Eyes).Select(f => f.Parameter).Distinct().ToList();
            var mouthParameters = pinState.PauseFlags.Where(f => f.Mouth).Select(f => f.Parameter).Distinct().ToList();
            if (eyeParameters.Count > 0)
                pinState.BlinkPauseLayerName = BuildPauseLayer(asc, fx, avatarRoot, "AD: Blink pause", eyeParameters, eyes: true, pinLayerNames);
            if (mouthParameters.Count > 0)
                pinState.LipSyncPauseLayerName = BuildPauseLayer(asc, fx, avatarRoot, "AD: Lip sync pause", mouthParameters, eyes: false, pinLayerNames);
            return messages;
        }

        static string BuildPauseLayer(AnimatorServicesContext asc, VirtualAnimatorController fx, Transform avatarRoot, string baseName, List<string> parameters, bool eyes, ICollection<string> pinLayerNames)
        {
            var name = AnimatorBuilder.UniqueLayerName(fx, baseName);
            pinLayerNames?.Add(name);
            var layer = fx.AddLayer(new LayerPriority(int.MaxValue), name);
            var sm = layer.StateMachine;
            var empty = new ClipContent(avatarRoot);
            var idle = sm.AddState("Idle", empty.ToClip(asc, name + " Idle"), new Vector3(0f, 0f, 0f));
            var paused = sm.AddState("Paused", empty.ToClip(asc, name + " Paused"), new Vector3(300f, 0f, 0f));
            var release = sm.AddState("Release", empty.ToClip(asc, name + " Release"), new Vector3(600f, 0f, 0f));
            idle.WriteDefaultValues = false;
            paused.WriteDefaultValues = false;
            release.WriteDefaultValues = false;
            sm.DefaultState = idle;

            paused.Behaviours = ImmutableList.Create<StateMachineBehaviour>(Tracking(eyes, !eyes, VRC_AnimatorTrackingControl.TrackingType.Animation));
            release.Behaviours = ImmutableList.Create<StateMachineBehaviour>(Tracking(eyes, !eyes, VRC_AnimatorTrackingControl.TrackingType.Tracking));

            idle.Transitions = ImmutableList.CreateRange(parameters.Select(p =>
                AnimatorBuilder.Transition(paused, ImmutableList.Create(Cond(p, AnimatorConditionMode.Greater, 0.001f)))));
            var releaseConditions = ImmutableList.CreateRange(parameters.Select(p => Cond(p, AnimatorConditionMode.Less, 0.001f)));
            paused.Transitions = ImmutableList.Create(AnimatorBuilder.Transition(release, releaseConditions));

            var releaseToIdle = VirtualStateTransition.Create();
            releaseToIdle.SetDestination(idle);
            releaseToIdle.Conditions = ImmutableList<AnimatorCondition>.Empty;
            releaseToIdle.ExitTime = 0f;
            releaseToIdle.Duration = 0f;
            releaseToIdle.HasFixedDuration = true;
            releaseToIdle.CanTransitionToSelf = false;
            release.Transitions = ImmutableList.Create(releaseToIdle);

            return name;
        }

        /// <summary>
        /// Runs after every other tool has merged: when the final controller already holds a tracking control that
        /// sets Eyes or Mouth to Animation, in a layer that is not the pause layer built above, that other system
        /// (a VRCFT template, a sleep or AFK setup) already owns the part, so AvatarDoc's own pause layer for it is
        /// removed and a warning names the layer left holding it.
        /// </summary>
        public static List<ModelMessage> RemoveForeignOwnedPauseLayers(AnimatorServicesContext asc, PinLayerState pins)
        {
            var messages = new List<ModelMessage>();
            if (pins == null || (string.IsNullOrEmpty(pins.BlinkPauseLayerName) && string.IsNullOrEmpty(pins.LipSyncPauseLayerName))) return messages;
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return messages;

            if (!string.IsNullOrEmpty(pins.BlinkPauseLayerName))
            {
                var foreign = ForeignTrackingLayer(fx, pins.BlinkPauseLayerName, eyes: true);
                if (foreign != null)
                {
                    RemoveLayerNamed(fx, pins.BlinkPauseLayerName);
                    pins.Names.Remove(pins.BlinkPauseLayerName);
                    messages.Add(ModelMessage.Warning("ad.warn.follow_distance_tracking_left_to", L.Text("ad.ui.part.blinking"), foreign));
                }
            }
            if (!string.IsNullOrEmpty(pins.LipSyncPauseLayerName))
            {
                var foreign = ForeignTrackingLayer(fx, pins.LipSyncPauseLayerName, eyes: false);
                if (foreign != null)
                {
                    RemoveLayerNamed(fx, pins.LipSyncPauseLayerName);
                    pins.Names.Remove(pins.LipSyncPauseLayerName);
                    messages.Add(ModelMessage.Warning("ad.warn.follow_distance_tracking_left_to", L.Text("ad.ui.part.lipsync"), foreign));
                }
            }
            return messages;
        }

        /// <summary>
        /// VRCFury's own tracking arbitration layer, matched by name only (no C# reference to any VRCFury type):
        /// it rewrites every VRCAnimatorTrackingControl it finds, AvatarDoc's pause layer included, into parameter
        /// drivers it merges itself, then applies the merged result here. Finding it must not read as "another
        /// system already owns this part": it is VRCFury carrying out AvatarDoc's own request alongside everyone
        /// else's, and removing AvatarDoc's pause layer would also remove the state machine that still feeds it,
        /// silencing AvatarDoc's own contribution to the merge (found under Packages/com.vrcfury.vrcfury/Editor-Avatars/Service/TrackingConflictResolverService.cs).
        /// VRCFury prefixes every layer it creates with "[VF&lt;n&gt;] " (ControllerManager.NewLayerName), so the name is matched as a suffix.
        /// </summary>
        const string VrcfTrackingActorLayerName = "VRCF Tracking Control Actor";

        static bool IsVrcfTrackingActor(string layerName)
            => layerName != null && layerName.EndsWith(VrcfTrackingActorLayerName, System.StringComparison.Ordinal);

        static string ForeignTrackingLayer(VirtualAnimatorController fx, string ownName, bool eyes)
        {
            foreach (var layer in fx.Layers)
            {
                if (layer.Name == ownName || IsVrcfTrackingActor(layer.Name) || layer.StateMachine == null) continue;
                foreach (var state in layer.StateMachine.AllStates())
                {
                    if (state.Behaviours == null) continue;
                    foreach (var behaviour in state.Behaviours)
                    {
                        if (!(behaviour is VRCAnimatorTrackingControl control)) continue;
                        var value = eyes ? control.trackingEyes : control.trackingMouth;
                        if (value == VRC_AnimatorTrackingControl.TrackingType.Animation) return layer.Name;
                    }
                }
            }
            return null;
        }

        static void RemoveLayerNamed(VirtualAnimatorController fx, string name)
        {
            fx.Layers = fx.Layers.Where(l => l.Name != name).ToList();
        }

        static readonly Dictionary<(int id, int dirty), (string eyesLayer, string mouthLayer)> existingTrackingCache = new Dictionary<(int, int), (string, string)>();

        /// <summary>
        /// Forgets every controller's tracking control owners. Runs wherever LayerAnalysis drops its layer reads:
        /// editing a tracking control dirties the behaviour, not the controller whose dirty count keys this cache.
        /// </summary>
        internal static void DropTrackingCache() => existingTrackingCache.Clear();

        /// <summary>
        /// Whichever layer of the avatar's own saved FX controller already sets Eyes or Mouth tracking to
        /// Animation, for "What this builds" to say the pause is left to it. Read once per controller version and
        /// cached the way ParameterUsage caches controller reads (instance id plus EditorUtility.GetDirtyCount), so
        /// the live panel never re-scans the controller on every refresh. Null for a part nothing sets. AvatarDoc's
        /// own pause layer never appears here: it exists only inside a build, never in the saved asset this reads.
        /// </summary>
        public static (string eyesLayer, string mouthLayer) ExistingTrackingControlOwners(AnimatorController fx)
        {
            if (fx == null) return (null, null);
            var key = (fx.GetInstanceID(), EditorUtility.GetDirtyCount(fx));
            if (existingTrackingCache.TryGetValue(key, out var cached)) return cached;

            string eyesLayer = null, mouthLayer = null;
            foreach (var layer in fx.layers)
            {
                if (layer.stateMachine == null) continue;
                foreach (var state in LayerAnalysis.States(layer.stateMachine))
                {
                    if (state.behaviours == null) continue;
                    foreach (var behaviour in state.behaviours)
                    {
                        if (!(behaviour is VRCAnimatorTrackingControl control)) continue;
                        if (eyesLayer == null && control.trackingEyes == VRC_AnimatorTrackingControl.TrackingType.Animation) eyesLayer = layer.name;
                        if (mouthLayer == null && control.trackingMouth == VRC_AnimatorTrackingControl.TrackingType.Animation) mouthLayer = layer.name;
                    }
                }
            }
            var result = (eyesLayer, mouthLayer);
            existingTrackingCache[key] = result;
            return result;
        }

        static AnimatorCondition Cond(string parameter, AnimatorConditionMode mode, float threshold) => new AnimatorCondition { parameter = parameter, mode = mode, threshold = threshold };

        static VRCAnimatorTrackingControl Tracking(bool eyes, bool mouth, VRC_AnimatorTrackingControl.TrackingType type)
        {
            var control = ScriptableObject.CreateInstance<VRCAnimatorTrackingControl>();
            if (eyes) control.trackingEyes = type;
            if (mouth) control.trackingMouth = type;
            return control;
        }

        static VRCAvatarParameterDriver Driver(string parameter, float value)
        {
            var driver = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
            driver.localOnly = true;
            driver.parameters = new List<VRC_AvatarParameterDriver.Parameter>
            {
                new VRC_AvatarParameterDriver.Parameter { type = VRC_AvatarParameterDriver.ChangeType.Set, name = parameter, value = value },
            };
            return driver;
        }
    }
}
#endif
