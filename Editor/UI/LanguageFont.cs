#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using nadena.dev.ndmf.localization;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// NDMF's font for the shared language (the one Modular Avatar shows), so Japanese and Chinese text takes that
    /// language's own character forms rather than whatever font the system falls back to. English keeps Unity's own
    /// font. NDMF keeps an element it has given a font following every later language, English included, and has no
    /// call to let go of one, so only elements built anew for each language get it: the parts a view builds again on
    /// a language change, the contents a window or inspector builds again, a menu.
    /// </summary>
    public static class LanguageFont
    {
        /// <summary>What gives one element the font. Replaceable by a test, which cannot change NDMF's language without saving it.</summary>
        public static Action<VisualElement> Applier = ApplyNdmfFont;

        /// <summary>Whether elements built now get NDMF's font: in every language but English.</summary>
        public static bool Applies
        {
            get
            {
                var code = L.Language ?? "";
                return code.Length > 0 && !code.Equals("en", StringComparison.OrdinalIgnoreCase) && !code.StartsWith("en-", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>Gives an element NDMF's font unless the language is English. Called before the element is added anywhere, so NDMF takes it in only once it is shown.</summary>
        public static void Apply(VisualElement element)
        {
            if (element != null && Applies) Applier(element);
        }

        /// <summary>
        /// Gives every child the root has now NDMF's font unless the language is English: at once when the root is
        /// shown, else when it is first shown, so nothing is handed to NDMF for a panel that never appears.
        /// </summary>
        public static void ApplyToChildren(VisualElement root)
        {
            if (root == null || !Applies) return;
            if (root.panel == null)
            {
                root.UnregisterCallback<AttachToPanelEvent>(ApplyOnAttach);
                root.RegisterCallback<AttachToPanelEvent>(ApplyOnAttach);
                return;
            }
            foreach (var child in root.Children()) Applier(child);
        }

        static void ApplyOnAttach(AttachToPanelEvent e)
        {
            var root = (VisualElement)e.currentTarget;
            root.UnregisterCallback<AttachToPanelEvent>(ApplyOnAttach);
            ApplyToChildren(root);
        }

        static void ApplyNdmfFont(VisualElement element)
        {
            // NDMF hands out the font of its own language: the one AvatarDoc shows, unless a test has put another in its place.
            if (string.Equals(L.Language, LanguagePrefs.Language, StringComparison.OrdinalIgnoreCase)) LanguagePrefs.ApplyFontPreferences(element);
        }
    }
}
#endif
