#if DOCME_NDMF && DOCME_VRCSDK3A
namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>How much of AvatarDoc a view shows. Simple keeps to what most avatars need, Advanced shows every tool; neither ever hides something the creator made.</summary>
    public enum ViewMode { Simple, Advanced }

    /// <summary>Where the chosen mode is kept: one preference for the person, shared by every AvatarDoc component, never one per component.</summary>
    public static class ViewModes
    {
        public const string PrefKey = "DocMe.AvatarDoctor.Mode";

        /// <summary>The stored choice, Advanced until the person picks Simple, so nothing changes for anyone who never touches the switch.</summary>
        public static ViewMode Load(IViewPrefs prefs)
            => prefs.GetString(PrefKey, nameof(ViewMode.Advanced)) == nameof(ViewMode.Simple) ? ViewMode.Simple : ViewMode.Advanced;

        public static void Save(IViewPrefs prefs, ViewMode mode) => prefs.SetString(PrefKey, mode.ToString());
    }
}
#endif
