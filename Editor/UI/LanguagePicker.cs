#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using nadena.dev.ndmf.localization;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The language picker beside the Simple and Advanced switch: the current language's own name and an arrow. It
    /// offers the languages NDMF's and Modular Avatar's switchers offer, and choosing one changes the language they
    /// all share.
    /// </summary>
    public sealed class LanguagePicker : Button
    {
        public readonly Label Label;

        public LanguagePicker()
        {
            name = "ad-language";
            AddToClassList("ad-language");
            tooltip = L.Text("ad.ui.language.tip");
            Label = new Label(DisplayName(L.Language, Choices())) { name = "ad-language-label", pickingMode = PickingMode.Ignore };
            Add(Label);
            MenuTreeView.AddDropdownArrow(this);
            clicked += () => AdMenu.Open(worldBound, this, Items());
        }

        /// <summary>One item per language, each in its own name, the current one ticked.</summary>
        public static List<AdMenu.Item> Items()
        {
            var choices = Choices();
            return choices.Select(code => new AdMenu.Item
            {
                Text = DisplayName(code, choices),
                Checked = string.Equals(code, L.Language, StringComparison.OrdinalIgnoreCase),
                Action = () => L.SetLanguage(code),
            }).ToList();
        }

        /// <summary>
        /// Every language to offer, in lower case and code order: the ones NDMF knows, AvatarDoc's own and the current
        /// one. A plain code ("ja") is left out when a fuller one ("ja-jp") is there, as NDMF's own switcher does.
        /// </summary>
        public static List<string> Choices()
        {
            var all = new SortedSet<string>(LanguagePrefs.RegisteredLanguages, StringComparer.Ordinal);
            foreach (var code in LanguageTables.Codes) all.Add(code.ToLowerInvariant());
            if (!string.IsNullOrEmpty(L.Language)) all.Add(L.Language.ToLowerInvariant());
            return all.Where(code => code.Contains("-") || !all.Any(other => other.StartsWith(code + "-", StringComparison.Ordinal))).ToList();
        }

        static string BaseOf(string code) => (code ?? "").Split('-')[0].ToLowerInvariant();

        /// <summary>
        /// A language's name in that language: "English", "日本語". A script (zh-Hans, zh-Hant) is always named, since it
        /// tells two written forms apart; a region only when another choice shares the language. Two choices that would
        /// still read the same get their codes added.
        /// </summary>
        public static string DisplayName(string code, IReadOnlyCollection<string> among)
        {
            var name = Name(code, among);
            if (among != null && among.Any(other => !string.Equals(other, code, StringComparison.OrdinalIgnoreCase) && Name(other, among) == name))
                name += " (" + code + ")";
            return name;
        }

        static string Name(string code, IReadOnlyCollection<string> among)
        {
            var lower = (code ?? "").ToLowerInvariant();
            var parts = lower.Split('-');
            var all = new HashSet<string>((among ?? Array.Empty<string>()).Select(c => c.ToLowerInvariant())) { lower };
            bool script = parts.Skip(1).Any(p => p.Length == 4);
            bool shared = all.Count(c => BaseOf(c) == parts[0]) > 1;
            var name = NativeName(script || shared ? lower : parts[0]);
            if (string.IsNullOrEmpty(name)) return code;
            return char.ToUpper(name[0], CultureInfo.InvariantCulture) + name.Substring(1);
        }

        /// <summary>NDMF's own lookup of a language's name (internal to NDMF), so the picker reads exactly as NDMF's switcher does. Null when this NDMF version has none.</summary>
        static readonly MethodInfo ndmfNativeName = typeof(LanguagePrefs).GetMethod("GetLocaleNativeName",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);

        /// <summary>Whether NDMF's name lookup was found. Read by a test, so an NDMF update that removes it is noticed.</summary>
        public static bool UsesNdmfNames => ndmfNativeName != null && ndmfNativeName.ReturnType == typeof(string);

        /// <summary>A code's name from NDMF's list of language names, or from .NET's (which has no script names) when NDMF has none.</summary>
        static string NativeName(string code)
        {
            if (UsesNdmfNames)
            {
                try
                {
                    if (ndmfNativeName.Invoke(null, new object[] { code }) is string name && name.Length > 0 && !string.Equals(name, code, StringComparison.OrdinalIgnoreCase))
                        return name;
                }
                catch (Exception) { }
            }
            try { return CultureInfo.GetCultureInfo(code).NativeName; }
            catch (ArgumentException) { return code; }
        }
    }
}
#endif
