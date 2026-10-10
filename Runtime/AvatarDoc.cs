using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace DocMe.AvatarDoctor
{
    public enum InstallMode { AppendItems = 0, AsSubmenu = 1 }

    /// <summary>Which platforms a component builds on. The parameters are planned on both either way; only the menu controls and the layers are left out.</summary>
    public enum PlatformChoice { Both = 0, PCOnly = 1, MobileOnly = 2 }

    /// <summary>One FX layer an import removed, and the id of the hiding menu edit it added for the layer's old control, so a restore can undo both together.</summary>
    [Serializable]
    public class RemovedLayer
    {
        public string layer = "";
        public string editId = "";
    }

    /// <summary>
    /// A menu fragment. Entries install into the avatar's expressions menu at installPath at build time.
    /// Any number of these can exist on an avatar; they merge into one menu.
    /// </summary>
    [MovedFrom(true, sourceClassName: "AvatarDoctorMenu")]
    [AddComponentMenu("DocMe/AvatarDoc")]
    public class AvatarDoc : MonoBehaviour
#if DOCME_VRCSDK_BASE
        , VRC.SDKBase.IEditorOnly
#endif
    {
        /// <summary>Submenu path such as "Outfits/Summer". Empty means the avatar's root menu.</summary>
        public string installPath = "";
        public InstallMode installMode = InstallMode.AppendItems;
        public string menuName = "";
        public Texture2D menuIcon;
        public string parameterPrefix = "AD";
        /// <summary>The platform default for every entry that does not set its own.</summary>
        public PlatformChoice platform = PlatformChoice.Both;
        /// <summary>Set by a prefab's maker to keep buyers from editing the menu or the entries by accident. Install path, install mode, parameter prefix and platform stay editable.</summary>
        public bool locked;
        public string lockNote = "";
        public List<MenuEntry> entries = new List<MenuEntry>();
        public List<ExclusiveGroup> groups = new List<ExclusiveGroup>();
        public List<MenuEdit> menuEdits = new List<MenuEdit>();
        /// <summary>FX layers an import removed. The build strips them from the virtual FX controller; the asset is never touched.</summary>
        public List<RemovedLayer> removedLayers = new List<RemovedLayer>();
        /// <summary>Animator controllers whose layers join the avatar's FX controller at build time.</summary>
        public List<AnimatorMerge> animators = new List<AnimatorMerge>();
#if DOCME_VRCSDK3A
        /// <summary>Finished expressions menu assets installed into the merged menu. The assets themselves are never changed.</summary>
        public List<MenuAssetInstall> menuAssets = new List<MenuAssetInstall>();
#endif
        /// <summary>Parameters this component declares itself, for merged animators and installed menu assets that no entry gives a parameter.</summary>
        public List<ParameterDeclaration> parameters = new List<ParameterDeclaration>();

        public string EffectiveMenuName => string.IsNullOrWhiteSpace(menuName) ? gameObject.name : menuName.Trim();

        public MenuEntry FindEntry(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var entry in entries)
                if (entry.id == id) return entry;
            return null;
        }

        public MenuEntry AddEntry(EntryKind kind, string name, string parentId = "")
        {
            var entry = MenuEntry.Create(kind, name, parentId);
            entries.Add(entry);
            return entry;
        }

        // Present so Unity shows the enable checkbox; a disabled component is skipped at build.
        void OnEnable()
        {
        }
    }
}
