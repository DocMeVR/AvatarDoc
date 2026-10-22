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

    /// <summary>What the Gesture faces window keeps on the component between two runs, beside the triggers it makes.</summary>
    [Serializable]
    public class GestureFaceSettings
    {
        /// <summary>True once faces were made with a face tracking choice, None included, so the window does not look for one again.</summary>
        public bool trackingChosen;
        /// <summary>The Bool parameter that is on while face tracking is on. Empty: None.</summary>
        public string trackingParameter = "";
        /// <summary>Where each shape of the faces rests while no gesture is held, as it was when the faces were made, so a scene left posed is never taken for it.</summary>
        public List<GestureRestWeight> rest = new List<GestureRestWeight>();
    }

    /// <summary>One shape of a gesture face and the weight it rests at.</summary>
    [Serializable]
    public class GestureRestWeight
    {
        public SkinnedMeshRenderer renderer;
        public string shape = "";
        public float weight;
    }

    /// <summary>
    /// A menu fragment. Entries install into the avatar's expressions menu at installPath at build time.
    /// Any number of these can exist on an avatar; they merge into one menu.
    /// </summary>
    [MovedFrom(true, sourceClassName: "AvatarDoctorMenu")]
    [AddComponentMenu("DocMe/AvatarDoc")]
    [HelpURL(DocsAddress.Base + "reference/inspector/")]
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
        /// <summary>Objects of this avatar a copy to another avatar never looks for by their place, such as one a world drop holds: the copy window has the creator pick the object to use on the other avatar.</summary>
        public List<GameObject> pickByHand = new List<GameObject>();
        public GestureFaceSettings gestureFaces = new GestureFaceSettings();

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

        /// <summary>The longest prefix a component takes from its object's name, so the parameter names stay short.</summary>
        public const int NamePrefixLength = 16;

        /// <summary>
        /// The parameter prefix a component added to this object starts with: AD on an avatar's root, else the
        /// object's name with only its ASCII letters, digits and underscores kept, at most NamePrefixLength of them,
        /// or AD_ and four random characters when nothing of the name is left (a name in Japanese, say).
        /// </summary>
        public static string PrefixFor(GameObject at)
        {
            if (at == null) return "AD";
#if DOCME_VRCSDK_BASE
            if (at.GetComponent<VRC.SDKBase.VRC_AvatarDescriptor>() != null) return "AD";
#endif
            var kept = NamePrefix(at.name);
            return kept.Length > 0 ? kept : "AD_" + Guid.NewGuid().ToString("N").Substring(0, 4);
        }

        /// <summary>An object's name with only its ASCII letters, digits and underscores kept, the first NamePrefixLength of them. Empty when none are.</summary>
        public static string NamePrefix(string objectName)
        {
            var kept = new System.Text.StringBuilder();
            foreach (char c in objectName ?? "")
            {
                if (kept.Length == NamePrefixLength) break;
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_') kept.Append(c);
            }
            return kept.ToString();
        }

        /// <summary>Whether the component still has a prefix PrefixFor gives on its object: AD, its object's name as kept, or AD_ and four characters for a name nothing is kept of.</summary>
        public bool HasStartingPrefix
        {
            get
            {
                var prefix = parameterPrefix ?? "";
                if (prefix == "AD") return true;
                var kept = NamePrefix(gameObject.name);
                return kept.Length > 0 ? prefix == kept : System.Text.RegularExpressions.Regex.IsMatch(prefix, "^AD_[0-9a-f]{4}$");
            }
        }

        // Unity calls Reset when the component is added in the editor, never for one that is loaded or comes with a
        // prefab, so every component made before keeps the prefix it has. The prefix never follows a later rename.
        void Reset()
        {
            parameterPrefix = PrefixFor(gameObject);
        }

        // Present so Unity shows the enable checkbox; a disabled component is skipped at build.
        void OnEnable()
        {
        }
    }
}
