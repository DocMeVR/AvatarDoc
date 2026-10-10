#if DOCME_VRCSDK3A
using System.Collections.Generic;
using UnityEditor;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Which platform the editor builds for, and whether one entry is built on it. VRChat syncs expression
    /// parameters by position, so an entry left out here still keeps its parameter: only its menu control and its
    /// layers go.
    /// </summary>
    public static class Platform
    {
        /// <summary>Stands in for the real build target while a test runs. Null means the editor's own target.</summary>
        public static bool? OverrideMobile;

        public static bool IsMobileTarget
        {
            get { return OverrideMobile ?? IsMobile(EditorUserBuildSettings.activeBuildTarget); }
        }

        static bool IsMobile(BuildTarget target) => target == BuildTarget.Android || target == BuildTarget.iOS;

        /// <summary>The platform choice that decides this entry: its own, else the nearest submenu above it that sets one, else the component's.</summary>
        public static PlatformChoice Effective(AvatarDoc component, MenuEntry entry)
        {
            var fallback = component != null ? component.platform : PlatformChoice.Both;
            if (entry == null) return fallback;
            if (entry.platform != EntryPlatform.Inherit) return FromEntry(entry.platform);
            // Triggers and overrides ignore parentId, so nothing encloses them: they fall straight to the component.
            if (component == null || entry.IsRule) return fallback;

            var seen = new HashSet<string> { entry.id };
            var current = component.FindEntry(entry.parentId);
            while (current != null)
            {
                if (!seen.Add(current.id)) break;
                if (current.platform != EntryPlatform.Inherit) return FromEntry(current.platform);
                current = component.FindEntry(current.parentId);
            }
            return fallback;
        }

        /// <summary>The platform choice that decides this merged animator: its own, else the component's. A merge hangs under no submenu, so there is nothing between the two.</summary>
        public static PlatformChoice Effective(AvatarDoc component, AnimatorMerge merge)
        {
            var fallback = component != null ? component.platform : PlatformChoice.Both;
            if (merge == null || merge.platform == EntryPlatform.Inherit) return fallback;
            return FromEntry(merge.platform);
        }

        /// <summary>The platform choice that decides this menu asset install: its own, else the component's. An install hangs under no submenu entry, so there is nothing between the two.</summary>
        public static PlatformChoice Effective(AvatarDoc component, MenuAssetInstall install)
        {
            var fallback = component != null ? component.platform : PlatformChoice.Both;
            if (install == null || install.platform == EntryPlatform.Inherit) return fallback;
            return FromEntry(install.platform);
        }

        /// <summary>Whether this menu asset is installed on the platform the editor is currently targeting.</summary>
        public static bool Included(AvatarDoc component, MenuAssetInstall install) => Included(Effective(component, install));

        /// <summary>Whether this entry is built on the platform the editor is currently targeting.</summary>
        public static bool Included(AvatarDoc component, MenuEntry entry) => Included(Effective(component, entry));

        /// <summary>Whether this merged animator is built on the platform the editor is currently targeting.</summary>
        public static bool Included(AvatarDoc component, AnimatorMerge merge) => Included(Effective(component, merge));

        /// <summary>Whether a component that carries its own platform choice builds on the platform the editor targets. Inherit means both: nothing encloses such a component.</summary>
        public static bool Included(EntryPlatform platform) => Included(FromEntry(platform));

        static bool Included(PlatformChoice choice)
        {
            if (choice == PlatformChoice.Both) return true;
            return IsMobileTarget ? choice == PlatformChoice.MobileOnly : choice == PlatformChoice.PCOnly;
        }

        static PlatformChoice FromEntry(EntryPlatform platform)
        {
            switch (platform)
            {
                case EntryPlatform.PCOnly: return PlatformChoice.PCOnly;
                case EntryPlatform.MobileOnly: return PlatformChoice.MobileOnly;
                default: return PlatformChoice.Both;
            }
        }
    }
}
#endif
