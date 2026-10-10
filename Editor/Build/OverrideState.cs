#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>One override target, kept in the build context between the first pass and the pass that runs after Modular Avatar.</summary>
    public sealed class OverrideRule
    {
        public SkinnedMeshRenderer Renderer;
        public string Shape = "";
        public float Value;
        public string EntryName = "";
        public string EntryId = "";
    }

    /// <summary>Rules recorded by AvatarDoctorBuilder.Run for OverrideBuilder.Run. Ready is false when the first pass stopped on errors.</summary>
    public sealed class OverrideState
    {
        public bool Ready;
        public readonly List<OverrideRule> Rules = new List<OverrideRule>();
        /// <summary>Keys of the warnings already reported, so a second run (after VRCFury) does not repeat them.</summary>
        public readonly HashSet<string> Reported = new HashSet<string>();
        /// <summary>How many clips each rule pinned the last time it pinned any, whatever became of its renderer afterwards.</summary>
        public readonly Dictionary<OverrideRule, int> Pinned = new Dictionary<OverrideRule, int>();

        public int PinnedCount(OverrideRule rule) => Pinned.TryGetValue(rule, out var count) ? count : 0;
        /// <summary>Rules that met a marker clip in some run: "nothing animates this" would be the wrong thing to say about those.</summary>
        public readonly HashSet<OverrideRule> Markers = new HashSet<OverrideRule>();
    }
}
#endif
