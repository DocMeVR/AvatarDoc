#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Which tags are ticked, per component, for this Editor session only: an in memory table, never EditorPrefs
    /// and never written to the scene, so it is gone the moment the session ends (a domain reload, or the Editor
    /// closing) rather than surviving the way the tree's own expansion set does.
    /// </summary>
    public static class TagFilterMemory
    {
        static readonly Dictionary<string, HashSet<string>> stored = new Dictionary<string, HashSet<string>>();

        public static HashSet<string> Load(string key)
            => stored.TryGetValue(key, out var set) ? new HashSet<string>(set) : new HashSet<string>();

        public static void Save(string key, IEnumerable<string> tagIds)
        {
            var set = new HashSet<string>(tagIds);
            if (set.Count == 0) stored.Remove(key);
            else stored[key] = set;
        }

        public static void Clear(string key) => stored.Remove(key);

        /// <summary>Drops every component's remembered tags. Tests call this so one run's fixtures never leak into the next; production code never calls it.</summary>
        public static void ClearAll() => stored.Clear();
    }
}
#endif
