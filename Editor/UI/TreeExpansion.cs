#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Which tree rows a creator has expanded, saved per component so the inspector opens the way it was left.</summary>
    public static class TreeExpansion
    {
        const string Prefix = "DocMe.AvatarDoctor.Expanded:";

        public static string KeyFor(AvatarDoc component)
            => component == null ? Prefix + "none" : Prefix + GlobalObjectId.GetGlobalObjectIdSlow(component);

        public static HashSet<string> Load(IViewPrefs prefs, string key)
        {
            var stored = prefs.GetString(key, "");
            return new HashSet<string>(stored.Split('\n').Where(s => s.Length > 0));
        }

        public static void Save(IViewPrefs prefs, string key, IEnumerable<string> expandedRowKeys)
        {
            var joined = string.Join("\n", expandedRowKeys.Where(k => !string.IsNullOrEmpty(k)).Distinct());
            if (joined.Length == 0) prefs.DeleteKey(key);
            else prefs.SetString(key, joined);
        }

        public static void Clear(IViewPrefs prefs, string key) => prefs.DeleteKey(key);
    }
}
#endif
