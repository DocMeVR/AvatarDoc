using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DocMe.AvatarDoctor
{
    /// <summary>VRChat renders menu names as TextMeshPro rich text. These helpers keep the styled text apart from the plain text the build uses.</summary>
    public static class RichName
    {
        public struct Style
        {
            public string Plain;
            public Color? Color;
            public bool Bold;
            public bool Italic;
            public bool Custom;
        }

        static readonly Regex AnyTag = new Regex("<[^<>]+>", RegexOptions.Compiled);
        static readonly Regex Shape = new Regex(
            "^(?:<color=#(?<color>[0-9a-fA-F]{6}(?:[0-9a-fA-F]{2})?)>)?(?:<b>)?(?:<i>)?(?<plain>[^<>]*)(?:</i>)?(?:</b>)?(?:</color>)?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string Strip(string name) => string.IsNullOrEmpty(name) ? "" : AnyTag.Replace(name, "");

        public static Style Parse(string name)
        {
            name = name ?? "";
            var style = new Style { Plain = Strip(name) };
            if (name.IndexOf('<') < 0) return style;
            var match = Shape.Match(name);
            bool balanced = match.Success
                && Balanced(name, "<b>", "</b>", out style.Bold)
                && Balanced(name, "<i>", "</i>", out style.Italic)
                && Balanced(name, "<color=", "</color>", out bool colored);
            if (!balanced) { style.Custom = true; style.Bold = false; style.Italic = false; return style; }
            style.Plain = match.Groups["plain"].Value;
            if (match.Groups["color"].Success && ColorUtility.TryParseHtmlString("#" + match.Groups["color"].Value, out var color)) style.Color = color;
            return style;
        }

        static bool Balanced(string name, string open, string close, out bool present)
        {
            int opens = Count(name, open), closes = Count(name, close);
            present = opens == 1 && closes == 1;
            return opens == closes && opens <= 1;
        }

        static int Count(string text, string part)
        {
            int count = 0, index = 0;
            while ((index = text.IndexOf(part, index, StringComparison.OrdinalIgnoreCase)) >= 0) { count++; index += part.Length; }
            return count;
        }

        public static string Compose(string plain, Color? color, bool bold, bool italic)
        {
            plain = plain ?? "";
            string result = plain;
            if (italic) result = "<i>" + result + "</i>";
            if (bold) result = "<b>" + result + "</b>";
            if (color.HasValue) result = "<color=#" + ColorUtility.ToHtmlStringRGB(color.Value) + ">" + result + "</color>";
            return result;
        }

        /// <summary>Appends text inside the wrappers of a recognised shape; custom names get the suffix at the end.</summary>
        public static string Append(string rich, string suffix)
        {
            var style = Parse(rich);
            if (style.Custom || rich == null || rich.IndexOf('<') < 0) return (rich ?? "") + suffix;
            return Compose(style.Plain + suffix, style.Color, style.Bold, style.Italic);
        }
    }
}
