#if DOCME_VRCSDK3A
using System;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor
{
    /// <summary>
    /// A finished expressions menu asset installed into the merged menu. The asset is read like the avatar's own
    /// menu and written as clones, so it is never changed.
    /// </summary>
    [Serializable]
    public sealed class MenuAssetInstall
    {
        public string id = "";
        public VRCExpressionsMenu menu;
        /// <summary>Submenu path from the root menu, in the component's install path format. Empty installs where the component itself installs.</summary>
        public string installPath = "";
        /// <summary>True: one submenu control holds the asset. False: the asset's controls are appended in place.</summary>
        public bool asSubmenu = true;
        /// <summary>The submenu's name, tags and all. Empty uses the asset's own name.</summary>
        public string name = "";
        public Texture2D icon;
        public EntryPlatform platform;

        /// <summary>The submenu name this install shows, tags and all: its own, else the asset's name.</summary>
        public string EffectiveName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
                return menu != null ? menu.name : "";
            }
        }

        public string PlainName => RichName.Strip(EffectiveName);

        public static MenuAssetInstall Create(VRCExpressionsMenu menu)
        {
            return new MenuAssetInstall
            {
                id = Guid.NewGuid().ToString("N"),
                menu = menu,
            };
        }
    }
}
#endif
