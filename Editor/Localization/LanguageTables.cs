#if DOCME_NDMF
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Every language AvatarDoc can show. A language is a gettext file named AvatarDoctor.&lt;code&gt;.po anywhere in the
    /// project, found by its name alone, so adding a language means adding one file. Files are read when a language is
    /// first asked for and read again when one is saved, added, moved or deleted. A file in the old JSON format,
    /// AvatarDoctor.&lt;code&gt;.json, is not read: it gets one Console warning saying how to convert it.
    /// </summary>
    public static class LanguageTables
    {
        public const string English = "en-US";

        /// <summary>The English file's asset id. It travels with the file, so the package is found wherever it sits (Assets or Packages).</summary>
        public const string EnglishGuid = "983e0544c4fdb25459124fe8a621374d";

        public const string Extension = ".po";

        static readonly Regex FileNamePattern = new Regex(@"^AvatarDoctor\.([A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*)\.po$", RegexOptions.CultureInvariant);

        static readonly Regex OldFileNamePattern = new Regex(@"^AvatarDoctor\.([A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*)\.json$", RegexOptions.CultureInvariant);

        public readonly struct Found
        {
            public readonly string Code;
            public readonly string Path;
            public readonly bool Shipped;

            public Found(string code, string path, bool shipped)
            {
                Code = code;
                Path = path;
                Shipped = shipped;
            }
        }

        static List<Found> found;
        static readonly Dictionary<string, Dictionary<string, string>> tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Dictionary<string, string>> injected = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> warned = new HashSet<string>(StringComparer.Ordinal);
        static readonly HashSet<string> warnedOld = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Moves whenever what the tables hold may have changed: a reload, or a test's table coming or going.</summary>
        public static int Version { get; private set; }

        /// <summary>The language code a file name stands for, written the standard way ("ja-JP", "zh-Hans"); null when the name is not AvatarDoctor.&lt;code&gt;.po or no language has that code.</summary>
        public static string CodeFromFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            var match = FileNamePattern.Match(fileName);
            return match.Success ? Normalize(match.Groups[1].Value) : null;
        }

        /// <summary>A language code written the standard way, or null when no language has it. NDMF reads codes the same way, so a code it accepts is one this accepts.</summary>
        public static string Normalize(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            try
            {
                var name = CultureInfo.GetCultureInfo(code).Name;
                return string.IsNullOrEmpty(name) ? null : name;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>The package folder: three levels above the English table (Editor/Localization/the file). Null when the English table is missing.</summary>
        public static string PackageRoot()
        {
            var english = AssetDatabase.GUIDToAssetPath(EnglishGuid);
            if (string.IsNullOrEmpty(english) || Path.GetFileName(english) != "AvatarDoctor." + English + Extension)
                english = FindByName(null).Where(f => f.code == English).Select(f => f.path).FirstOrDefault();
            if (string.IsNullOrEmpty(english)) return null;
            var localization = Path.GetDirectoryName(english);
            var editor = localization == null ? null : Path.GetDirectoryName(localization);
            return editor == null ? null : Path.GetDirectoryName(editor)?.Replace('\\', '/');
        }

        /// <summary>
        /// Every language file, Packages included, by its name alone: Unity imports a .po file as an asset of its own
        /// kind, not as a TextAsset. Files in the old JSON format are added to old when it is given.
        /// </summary>
        static List<(string code, string path)> FindByName(List<string> old)
        {
            var files = new List<(string code, string path)>();
            foreach (var guid in AssetDatabase.FindAssets("AvatarDoctor"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var code = CodeFromFileName(Path.GetFileName(path));
                if (code != null)
                {
                    if (!files.Any(f => f.path == path)) files.Add((code, path));
                }
                else if (old != null && IsOldLanguageFile(path) && !old.Contains(path)) old.Add(path);
            }
            return files;
        }

        /// <summary>
        /// Every language file in the project: the package's own first, then the others by path, so a file outside the
        /// package wins over a shipped one key by key. A file in the old JSON format gets its warning the first time it
        /// is found, and again only after it was gone once.
        /// </summary>
        public static IReadOnlyList<Found> Files()
        {
            if (found != null) return found;
            var root = PackageRoot();
            var old = new List<string>();
            found = FindByName(old)
                .Select(f => new Found(f.code, f.path, root != null && f.path.StartsWith(root + "/", StringComparison.Ordinal)))
                .OrderBy(f => f.Shipped ? 0 : 1).ThenBy(f => f.Path, StringComparer.Ordinal)
                .ToList();
            warnedOld.RemoveWhere(path => !old.Contains(path));
            foreach (var path in old.Where(warnedOld.Add).ToList())
                Debug.LogWarning(OldFileWarning(path));
            return found;
        }

        /// <summary>The Console warning for a language file in the old JSON format, which AvatarDoc no longer reads.</summary>
        public static string OldFileWarning(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path ?? "") + Extension;
            return "[AvatarDoc] " + path + " is a language file in the old JSON format, which AvatarDoc no longer reads, so its texts do not show. "
                + "Make " + name + " from it: copy AvatarDoctor." + English + Extension + " from AvatarDoc's Editor/Localization folder, name the copy " + name
                + ", change its Language line to the code in that name, and put each translation after msgstr in place of the English. Then delete this file.";
        }

        public static LanguageFile Read(Found file)
        {
            byte[] bytes = null;
            try { bytes = File.ReadAllBytes(Path.GetFullPath(file.Path)); }
            catch (Exception) { }
            return LanguageFile.Read(file.Path, file.Code, file.Shipped, bytes);
        }

        /// <summary>The shipped English file, read from disk; null when it is missing.</summary>
        public static LanguageFile ReadEnglish()
        {
            foreach (var file in Files())
                if (file.Shipped && file.Code == English) return Read(file);
            return null;
        }

        /// <summary>Every language with a table: English first, then the others by code. A test's table counts while it is in place.</summary>
        public static IReadOnlyList<string> Codes
        {
            get
            {
                var codes = new SortedSet<string>(Files().Select(f => f.Code), StringComparer.OrdinalIgnoreCase);
                codes.UnionWith(injected.Keys);
                codes.Remove(English);
                var list = new List<string> { English };
                list.AddRange(codes);
                return list;
            }
        }

        /// <summary>One table's text for a key, or null when that table lacks it or has it blank. No fallback: L picks the order.</summary>
        public static string Text(string code, string key)
        {
            var table = TableFor(code);
            return table != null && table.TryGetValue(key, out var text) ? text : null;
        }

        /// <summary>Several files of one language as one table, later files winning key by key. A file that could not be read is left out.</summary>
        public static Dictionary<string, string> Merge(IEnumerable<LanguageFile> files)
        {
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                if (file == null || file.Error != null) continue;
                foreach (var pair in file.ToTable()) merged[pair.Key] = pair.Value;
            }
            return merged;
        }

        static Dictionary<string, string> TableFor(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            if (injected.TryGetValue(code, out var test)) return test;
            if (tables.TryGetValue(code, out var cached)) return cached;
            var files = Files().Where(f => string.Equals(f.Code, code, StringComparison.OrdinalIgnoreCase)).Select(Read).ToList();
            foreach (var file in files.Where(f => f.Error != null && warned.Add(f.Path)))
                Debug.LogWarning("[AvatarDoc] " + file.Path + " could not be read, so English shows in its place: " + file.Error);
            var table = files.Count == 0 ? null : Merge(files);
            tables[code] = table;
            return table;
        }

        /// <summary>Forgets every file found and read; the next text asked for reads them again.</summary>
        public static void Reload()
        {
            found = null;
            tables.Clear();
            warned.Clear();
            Version++;
        }

        /// <summary>
        /// For tests: puts a table in place of any file for this code, never written to disk. Returns the test table it
        /// replaced (null when there was none), for RestoreTestTable to put back, so two scopes on one code nest.
        /// </summary>
        public static Dictionary<string, string> PutTestTable(string code, IDictionary<string, string> texts)
        {
            injected.TryGetValue(code, out var previous);
            injected[code] = texts == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : texts.Where(p => !string.IsNullOrWhiteSpace(p.Value)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            Version++;
            return previous;
        }

        /// <summary>Puts back the test table PutTestTable replaced, or takes the code's test table away when it replaced none.</summary>
        public static void RestoreTestTable(string code, Dictionary<string, string> previous)
        {
            if (previous != null) injected[code] = previous;
            else injected.Remove(code);
            Version++;
        }

        /// <summary>Whether a path names a language file, by its name alone.</summary>
        public static bool IsLanguageFile(string path) => FileNamePattern.IsMatch(Path.GetFileName(path ?? ""));

        /// <summary>Whether a path names a language file in the old JSON format, which AvatarDoc no longer reads.</summary>
        public static bool IsOldLanguageFile(string path) => OldFileNamePattern.IsMatch(Path.GetFileName(path ?? ""));
    }

    /// <summary>
    /// Reads the languages again when a language file is saved, added, moved or deleted, and says in the Console what
    /// is wrong with a saved one. A file in the old JSON format reads them again too, so its warning shows at once.
    /// </summary>
    sealed class LanguageFileWatcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var touched = imported.Concat(deleted).Concat(moved).Concat(movedFrom).Where(p => LanguageTables.IsLanguageFile(p) || LanguageTables.IsOldLanguageFile(p)).ToList();
            if (touched.Count == 0) return;
            var saved = imported.Concat(moved).Where(LanguageTables.IsLanguageFile).ToList();
            EditorApplication.delayCall += () =>
            {
                L.ReloadLanguages();
                LanguageCheck.ReportToConsole(saved);
            };
        }
    }
}
#endif
