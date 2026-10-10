#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Opens the package's own README at its Quick start section, from wherever the package folder actually sits.</summary>
    public static class QuickStart
    {
        /// <summary>Swapped in tests so opening the README never touches the real file system or launches a real viewer.</summary>
        public static Action<string> OpenOverride;

        /// <summary>The package's own root folder (LanguageTables finds it from the English table every copy ships); null when that file is missing.</summary>
        public static string PackageRoot() => LanguageTables.PackageRoot();

        /// <summary>The README path this package ships, or null when the package root cannot be found.</summary>
        public static string ReadmePath()
        {
            var root = PackageRoot();
            return root == null ? null : root + "/README.md";
        }

        /// <summary>The heading the Quick start section starts and ends at, so this only ever reads that one section, never the whole file.</summary>
        const string SectionHeading = "## Quick start";

        /// <summary>The Quick start section's own text, from its heading up to the next "## " heading, or null when the README or the section itself cannot be found.</summary>
        public static string QuickStartSection()
        {
            var path = ReadmePath();
            if (path == null || !File.Exists(path)) return null;
            var text = File.ReadAllText(path);
            int headingAt = text.IndexOf(SectionHeading, System.StringComparison.Ordinal);
            if (headingAt < 0) return null;
            int start = text.IndexOf('\n', headingAt);
            start = start < 0 ? text.Length : start + 1;
            int end = text.IndexOf("\n## ", start, System.StringComparison.Ordinal);
            if (end < 0) end = text.Length;
            return text.Substring(start, end - start).Trim();
        }

        /// <summary>
        /// Opens the Quick start section on its own, in a small window, since a file opened through whatever the
        /// platform's own default viewer for ".md" happens to be cannot be trusted to land on one heading inside
        /// it. Falls back to opening the whole README the plain way when the section itself cannot be found.
        /// </summary>
        public static void Open()
        {
            if (OpenOverride != null) { OpenOverride(ReadmePath()); return; }
            var section = QuickStartSection();
            if (section != null) { QuickStartWindow.Show(section); return; }
            OpenReadme();
        }

        /// <summary>Opens the whole README in the platform's default viewer for that file type. Does nothing when the file cannot be found.</summary>
        public static void OpenReadme()
        {
            var path = ReadmePath();
            if (path == null || !File.Exists(path)) return;
            Application.OpenURL("file://" + Path.GetFullPath(path));
        }
    }
}
#endif
