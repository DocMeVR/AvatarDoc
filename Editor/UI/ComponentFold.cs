#if DOCME_NDMF && DOCME_VRCSDK3A
using UnityEditor;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Whether a header fold (Component settings, so far) is open, saved per component the same way tree expansion is: closed by default, remembered across the session and between Editor restarts.</summary>
    public static class ComponentFold
    {
        const string Prefix = "DocMe.AvatarDoctor.Fold:";

        public static string KeyFor(AvatarDoc component, string foldName)
            => Prefix + foldName + ":" + (component == null ? "none" : GlobalObjectId.GetGlobalObjectIdSlow(component).ToString());

        /// <summary>The key for a fold that belongs to one entry rather than a whole component, such as each panel's own Advanced fold.</summary>
        public static string KeyForEntry(string entryId, string foldName)
            => Prefix + foldName + ":entry:" + (entryId ?? "none");

        public static bool Load(IViewPrefs prefs, string key) => prefs.GetBool(key, false);

        public static void Save(IViewPrefs prefs, string key, bool open) => prefs.SetBool(key, open);

        public static void Clear(IViewPrefs prefs, string key) => prefs.DeleteKey(key);
    }
}
#endif
