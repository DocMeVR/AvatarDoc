#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Compares this avatar's parameter list against another copy of the same avatar open in the editor. VRChat
    /// syncs by position, so a PC copy and a Quest copy of one blueprint whose lists have drifted apart show each
    /// other the wrong toggles. Both sides come from ListOf, which follows the build's rules rather than the
    /// inspector's: what the two avatars will upload is the only thing worth comparing.
    /// </summary>
    public static class PlatformParity
    {
        /// <summary>What the message calls a position one list has and the other does not.</summary>
        const string Missing = "-";

        /// <summary>
        /// Each avatar's own list is kept until its own plan version moves, so editing one avatar never forces its
        /// twin's untouched entry to rebuild: AvatarPlanCache.VersionFor bumps for that avatar alone on a menu
        /// edit or a wider component change, and for every avatar at once (through the shared epoch it adds in)
        /// on a coarse invalidation. A reorder bumps it too, since generated parameters are appended in menu
        /// order and so a reorder can move where one lands; the removal plan and the avatar-wide parameter walk
        /// it is built from are both cheap to redo once cached, so recomputing on every reorder still stays fast.
        /// </summary>
        static readonly Dictionary<Transform, (int version, List<string> list)> lists = new Dictionary<Transform, (int, List<string>)>();
        static List<VRC.Core.PipelineManager> pipelines;
        static int cachedPipelineVersion = -1;

        /// <summary>The warning for the first place the two lists differ, or null when there is nothing to compare against or nothing differs.</summary>
        public static ModelMessage Check(Transform avatarRoot)
        {
            if (avatarRoot == null) return null;
            RefreshPipelines();
            var blueprint = BlueprintOf(avatarRoot);
            if (string.IsNullOrEmpty(blueprint)) return null;
            var other = Twin(avatarRoot, blueprint);
            if (other == null) return null;

            var mine = CachedListOf(avatarRoot);
            var theirs = CachedListOf(other);
            int count = Mathf.Max(mine.Count, theirs.Count);
            for (int i = 0; i < count; i++)
            {
                var here = i < mine.Count ? mine[i] : Missing;
                var there = i < theirs.Count ? theirs[i] : Missing;
                if (here == there) continue;
                return ModelMessage.Warning("ad.warn.platform_parity", other.gameObject.name, (i + 1).ToString(), here, there);
            }
            return null;
        }

        /// <summary>The pipeline list only needs redoing when an avatar could have appeared or disappeared from the open scenes, which a coarse invalidation is the closest signal for.</summary>
        static void RefreshPipelines()
        {
            if (cachedPipelineVersion == AvatarPlanCache.StructureVersion) return;
            cachedPipelineVersion = AvatarPlanCache.StructureVersion;
            pipelines = null;
        }

        static string BlueprintOf(Transform avatarRoot)
        {
            var pipeline = avatarRoot.GetComponent<VRC.Core.PipelineManager>();
            return pipeline != null ? pipeline.blueprintId : null;
        }

        /// <summary>Another avatar in the open scenes that carries the same blueprint id. The first one found: a third copy would only repeat what the second already says.</summary>
        static Transform Twin(Transform avatarRoot, string blueprint)
        {
            if (pipelines == null)
            {
                pipelines = new List<VRC.Core.PipelineManager>();
                foreach (var pipeline in Object.FindObjectsOfType<VRC.Core.PipelineManager>(true))
                    if (pipeline != null && pipeline.GetComponent<VRCAvatarDescriptor>() != null) pipelines.Add(pipeline);
            }
            foreach (var pipeline in pipelines)
            {
                if (pipeline == null || pipeline.transform == avatarRoot) continue;
                if (pipeline.blueprintId != blueprint) continue;
                return pipeline.transform;
            }
            return null;
        }

        /// <summary>How often an avatar's own synced list was actually rebuilt instead of reused. Read by the tests and the performance probe.</summary>
        public static int ListBuilds { get; private set; }

        static List<string> CachedListOf(Transform avatarRoot)
        {
            int version = AvatarPlanCache.VersionFor(avatarRoot);
            if (lists.TryGetValue(avatarRoot, out var cached) && cached.version == version) return cached.list;
            ListBuilds++;
            var list = ListOf(avatarRoot);
            lists[avatarRoot] = (version, list);
            return list;
        }

        /// <summary>
        /// The synced list one avatar will declare once it is built: what its expression parameters asset already
        /// holds minus what "Remove from avatar" frees, then what AvatarDoctor adds on top, in order. The model is
        /// built the way AvatarDoctorBuilder builds it, with the enabled components only, the avatar's own menu
        /// underneath and the build's edit mode, so both sides of the comparison answer the same question.
        /// </summary>
        public static List<string> ListOf(Transform avatarRoot)
        {
            var descriptor = avatarRoot != null ? avatarRoot.GetComponent<VRCAvatarDescriptor>() : null;
            var parameters = descriptor != null ? descriptor.expressionParameters : null;
            var list = new List<string>();
            if (avatarRoot == null)
            {
                if (parameters != null && parameters.parameters != null)
                    foreach (var p in parameters.parameters)
                        if (!string.IsNullOrEmpty(p.name)) list.Add(Describe(p.name, TypeName(p.valueType), p.networkSynced));
                return list;
            }

            var components = MenuModel.CollectComponents(avatarRoot);
            var tree = MenuModel.Build(avatarRoot, descriptor != null ? descriptor.expressionsMenu : null, components);
            var plan = ParameterPlanner.Plan(tree, ParametersWriter.ExistingParameters(parameters, ParametersWriter.FxOf(descriptor)));
            tree.Removal = RemovalPlanner.Plan(tree, descriptor);
            var freed = new HashSet<string>(tree.Removal.Parameters.Where(p => p.Freed).Select(p => p.Name));
            if (parameters != null && parameters.parameters != null)
                foreach (var p in parameters.parameters)
                    if (!string.IsNullOrEmpty(p.name) && !freed.Contains(p.name))
                        list.Add(Describe(p.name, TypeName(p.valueType), p.networkSynced));
            foreach (var planned in plan.Parameters)
                if (!planned.ReusesExisting && !planned.IsAnimatorOnly) list.Add(Describe(planned.Name, planned.Type.ToString(), planned.Synced));
            return list;
        }

        static string Describe(string name, string type, bool synced) => L.Aside(name, synced ? L.JoinList(new[] { type, L.Text("ad.summary.parameters.synced") }) : type);

        static string TypeName(VRCExpressionParameters.ValueType type)
        {
            switch (type)
            {
                case VRCExpressionParameters.ValueType.Int: return PlannedType.Int.ToString();
                case VRCExpressionParameters.ValueType.Float: return PlannedType.Float.ToString();
                default: return PlannedType.Bool.ToString();
            }
        }
    }
}
#endif
