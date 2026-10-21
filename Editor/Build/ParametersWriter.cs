#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    public static class ParametersWriter
    {
        /// <summary>Everything the avatar already declares. Animator names come first so an expression declaration wins over them.</summary>
        public static Dictionary<string, ExistingParameter> ExistingParameters(VRCExpressionParameters parameters, VirtualAnimatorController fx)
        {
            var existing = new Dictionary<string, ExistingParameter>();
            if (fx != null)
            {
                foreach (var pair in fx.Parameters)
                {
                    var acp = pair.Value;
                    existing[pair.Key] = new ExistingParameter
                    {
                        Type = ToPlanned(acp.type),
                        IsExpressionParameter = false,
                        Default = DefaultOf(acp),
                    };
                }
            }
            AddExpressionParameters(existing, parameters);
            return existing;
        }

        /// <summary>The FX controller asset the descriptor points at, or null when it uses the default or none.</summary>
        public static AnimatorController FxOf(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null || descriptor.baseAnimationLayers == null) return null;
            foreach (var layer in descriptor.baseAnimationLayers)
                if (layer.type == VRCAvatarDescriptor.AnimLayerType.FX && !layer.isDefault) return layer.animatorController as AnimatorController;
            return null;
        }

        /// <summary>Same map as the build sees, read from the controller asset instead of NDMF's virtual controller.</summary>
        public static Dictionary<string, ExistingParameter> ExistingParameters(VRCExpressionParameters parameters, AnimatorController fx)
        {
            var existing = new Dictionary<string, ExistingParameter>();
            if (fx != null && fx.parameters != null)
            {
                foreach (var p in fx.parameters)
                {
                    existing[p.name] = new ExistingParameter
                    {
                        Type = ToPlanned(p.type),
                        IsExpressionParameter = false,
                        Default = DefaultOf(p),
                    };
                }
            }
            AddExpressionParameters(existing, parameters);
            return existing;
        }

        /// <summary>The expression-parameter half shared by both ExistingParameters overloads, so they cannot drift apart.</summary>
        static void AddExpressionParameters(Dictionary<string, ExistingParameter> existing, VRCExpressionParameters parameters)
        {
            if (parameters == null || parameters.parameters == null) return;
            foreach (var p in parameters.parameters)
            {
                if (string.IsNullOrEmpty(p.name)) continue;
                existing[p.name] = new ExistingParameter
                {
                    Type = ToPlanned(p.valueType),
                    IsExpressionParameter = true,
                    Saved = p.saved,
                    Synced = p.networkSynced,
                    Default = p.defaultValue,
                };
            }
        }

        static readonly (string suffix, PlannedType type)[] PhysBoneSuffixes =
        {
            ("_IsGrabbed", PlannedType.Bool), ("_IsPosed", PlannedType.Bool), ("_Angle", PlannedType.Float),
            ("_Stretch", PlannedType.Float), ("_Squish", PlannedType.Float),
        };

        /// <summary>Folds in what NDMF reports for the avatar: contacts, other plugins, and a PhysBone prefix expanded into its typed sub-parameters. A declaration already in the map wins.</summary>
        public static void AddProvided(Dictionary<string, ExistingParameter> existing, IEnumerable<ProvidedParameter> provided)
        {
            if (existing == null || provided == null) return;
            foreach (var parameter in provided)
            {
                if (parameter == null) continue;
                if (parameter.Namespace == ParameterNamespace.PhysBonesPrefix)
                {
                    var prefix = parameter.EffectiveName;
                    if (string.IsNullOrEmpty(prefix)) continue;
                    foreach (var (suffix, type) in PhysBoneSuffixes)
                    {
                        var subName = prefix + suffix;
                        if (existing.ContainsKey(subName)) continue;
                        existing[subName] = new ExistingParameter { Type = type, IsExpressionParameter = false, Saved = false, Synced = false, Default = 0f };
                    }
                    continue;
                }
                if (parameter.Namespace != ParameterNamespace.Animator) continue;
                if (parameter.ParameterType == null || parameter.ParameterType.Value == AnimatorControllerParameterType.Trigger) continue;
                if (parameter.Source is AvatarDoc) continue;
                var name = parameter.EffectiveName;
                if (string.IsNullOrEmpty(name) || existing.ContainsKey(name)) continue;
                existing[name] = new ExistingParameter
                {
                    Type = ToPlanned(parameter.ParameterType.Value),
                    IsExpressionParameter = false,
                    Saved = false,
                    Synced = false,
                    Default = parameter.DefaultValue ?? 0f,
                };
            }
        }

        /// <summary>
        /// The parameters another tool merges into the avatar at build time under their own names: those of the
        /// parameters assets VRCFury Full Controllers merge that VRCFury keeps global, read by VrcFuryReader through
        /// serialized data alone so no VRCFury type is named. Every other name in such an asset is renamed by VRCFury,
        /// so it is left out.
        /// </summary>
        public static List<VRCExpressionParameters.Parameter> ReferencedParameters(Transform avatarRoot)
            => avatarRoot == null ? new List<VRCExpressionParameters.Parameter>() : VrcFuryReader.FullControllerGlobalParameters(avatarRoot);

        /// <summary>Folds in the names ReferencedParameters found. A declaration already in the map wins; the rest join as animator names, since the tool that refers to them declares them itself.</summary>
        public static void AddReferenced(Dictionary<string, ExistingParameter> existing, IEnumerable<VRCExpressionParameters.Parameter> referenced)
        {
            if (existing == null || referenced == null) return;
            foreach (var parameter in referenced)
            {
                if (parameter == null || string.IsNullOrEmpty(parameter.name) || existing.ContainsKey(parameter.name)) continue;
                existing[parameter.name] = new ExistingParameter
                {
                    Type = ToPlanned(parameter.valueType),
                    IsExpressionParameter = false,
                    Saved = false,
                    Synced = false,
                    Default = parameter.defaultValue,
                };
            }
        }

        public static int SyncedCost(VRCExpressionParameters parameters)
        {
            return parameters == null ? 0 : parameters.CalcTotalCost();
        }

        /// <summary>What the avatar's synced budget will be once this plan is written.</summary>
        public static int PredictedCost(VRCExpressionParameters parameters, ParameterPlan plan)
        {
            return SyncedCost(parameters) + plan.NewSyncedBits;
        }

        static bool? vrcFuryLoaded;

        /// <summary>
        /// Whether VRCFury is installed, read once from the names of the loaded assemblies, since no VRCFury type
        /// may be referenced. Its parameter compressor runs on every uploaded avatar, with or without VRCFury
        /// components. Replaceable by a test, which cannot install or remove a package.
        /// </summary>
        public static Func<bool> VrcFuryInstalled = () => vrcFuryLoaded ??=
            AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name?.StartsWith("VRCFury", StringComparison.Ordinal) == true);

        /// <summary>
        /// What a synced bit total says, for the build and the inspector alike: null within VRChat's limit; over
        /// it, an error that stops the build, or with VRCFury installed a warning, since VRCFury compresses
        /// parameters at upload until they fit.
        /// </summary>
        public static ModelMessage BudgetMessage(int totalBits, int newSyncedBits)
        {
            if (totalBits <= VRCExpressionParameters.MAX_PARAMETER_COST) return null;
            string total = totalBits.ToString(), limit = VRCExpressionParameters.MAX_PARAMETER_COST.ToString(), added = newSyncedBits.ToString();
            return VrcFuryInstalled()
                ? ModelMessage.Warning("ad.warn.bits_exceeded_vrcfury", total, limit, added)
                : ModelMessage.Error("ad.error.bits_exceeded", total, limit, added);
        }

        /// <summary>Writes the plan into a clone of the expression parameters. A removal plan takes its freed parameters out of the clone; the asset the avatar points at is never changed.</summary>
        public static void Write(BuildContext context, VRCAvatarDescriptor descriptor, ParameterPlan plan, RemovalPlan removal = null)
        {
            var asset = descriptor.expressionParameters;
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                asset.name = "AvatarDoc Parameters";
                asset.parameters = Array.Empty<VRCExpressionParameters.Parameter>();
            }
            else if (!context.IsTemporaryAsset(asset))
            {
                var clone = UnityEngine.Object.Instantiate(asset);
                clone.name = asset.name;
                ObjectRegistry.RegisterReplacedObject(asset, clone);
                asset = clone;
            }

            var list = (asset.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>()).ToList();
            if (removal != null)
            {
                var freed = new HashSet<string>(removal.Parameters.Where(p => p.Freed).Select(p => p.Name));
                list.RemoveAll(p => p != null && freed.Contains(p.name));
            }
            foreach (var planned in plan.Parameters)
            {
                if (planned.ReusesExisting || planned.IsAnimatorOnly) continue;
                list.Add(new VRCExpressionParameters.Parameter
                {
                    name = planned.Name,
                    valueType = ToSdk(planned.Type),
                    saved = planned.Saved,
                    networkSynced = planned.Synced,
                    defaultValue = planned.Default,
                });
            }
            asset.parameters = list.ToArray();

            descriptor.expressionParameters = asset;
            descriptor.customExpressions = true;
            context.AssetSaver.SaveAsset(asset);
        }

        static float DefaultOf(AnimatorControllerParameter parameter)
        {
            switch (parameter.type)
            {
                case AnimatorControllerParameterType.Float: return parameter.defaultFloat;
                case AnimatorControllerParameterType.Int: return parameter.defaultInt;
                default: return parameter.defaultBool ? 1f : 0f;
            }
        }

        static PlannedType ToPlanned(AnimatorControllerParameterType type)
        {
            switch (type)
            {
                case AnimatorControllerParameterType.Float: return PlannedType.Float;
                case AnimatorControllerParameterType.Int: return PlannedType.Int;
                default: return PlannedType.Bool;
            }
        }

        static PlannedType ToPlanned(VRCExpressionParameters.ValueType type)
        {
            switch (type)
            {
                case VRCExpressionParameters.ValueType.Int: return PlannedType.Int;
                case VRCExpressionParameters.ValueType.Float: return PlannedType.Float;
                default: return PlannedType.Bool;
            }
        }

        static VRCExpressionParameters.ValueType ToSdk(PlannedType type)
        {
            switch (type)
            {
                case PlannedType.Int: return VRCExpressionParameters.ValueType.Int;
                case PlannedType.Float: return VRCExpressionParameters.ValueType.Float;
                default: return VRCExpressionParameters.ValueType.Bool;
            }
        }
    }
}
#endif
