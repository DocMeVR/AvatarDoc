#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using UnityEditor;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>The panel kinds a first-use tip exists for. One id per kind, used as the store's own key and as the element name of its tip.</summary>
    public static class TipKinds
    {
        public const string Toggle = "toggle";
        public const string Radial = "radial";
        public const string Submenu = "submenu";
        public const string Trigger = "trigger";
        public const string Override = "override";
        public const string Preset = "preset";
        public const string Grabbable = "grabbable";
        public const string Animator = "animator";
        public const string MenuAsset = "menu_asset";
        public const string Header = "header";

        public static readonly string[] All =
        {
            Toggle, Radial, Submenu, Trigger, Override, Preset, Grabbable, Animator, MenuAsset, Header,
        };
    }

    /// <summary>Whether a first-use tip has already been dismissed, and turning them all back on. A view receives one of these rather than talking to EditorPrefs directly, so a test can hand it a store that never touches the real one.</summary>
    public interface ITipStore
    {
        bool IsDismissed(string tipKind);
        void Dismiss(string tipKind);
        void ResetAll();
    }

    /// <summary>The real store: one EditorPrefs key per tip kind, under a single AvatarDoc prefix, shared by every component and every project this Editor opens.</summary>
    public sealed class EditorPrefsTipStore : ITipStore
    {
        const string Prefix = "DocMe.AvatarDoc.Tip:";

        public bool IsDismissed(string tipKind) => EditorPrefs.GetBool(Prefix + tipKind, false);

        public void Dismiss(string tipKind) => EditorPrefs.SetBool(Prefix + tipKind, true);

        public void ResetAll()
        {
            foreach (var kind in TipKinds.All) EditorPrefs.DeleteKey(Prefix + kind);
        }
    }

    /// <summary>An in-memory store for tests and for a detached view: nothing it does ever reaches the real EditorPrefs.</summary>
    public sealed class MemoryTipStore : ITipStore
    {
        readonly HashSet<string> dismissed = new HashSet<string>();

        public bool IsDismissed(string tipKind) => dismissed.Contains(tipKind);

        public void Dismiss(string tipKind) => dismissed.Add(tipKind);

        public void ResetAll() => dismissed.Clear();
    }
}
#endif
