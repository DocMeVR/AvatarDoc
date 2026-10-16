#if DOCME_NDMF
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Keeps a translation in step with English: adds every new key with its English and an empty text, in English
    /// order; marks a translation whose English changed as outdated ("#, fuzzy", with the English it was made from under
    /// "#. was:"); moves a key English dropped to the end as an old entry ("#~"); never deletes a translation.
    /// LocalizationFileTests.TheShippedTranslationsFollowEnglish runs it on the shipped files.
    /// </summary>
    public static class LanguageFileUpdater
    {
        /// <summary>
        /// The English a translation records under "#. en:". A comment line loses the spaces at its ends, so they are
        /// left out here too, and spaces at the ends of an English text never make its translation outdated.
        /// </summary>
        public static string Source(string english) => (english ?? "").Trim();

        /// <summary>Every key of the English file with its text, in the file's order. A key written twice counts once; an old entry not at all.</summary>
        public static List<(string key, string english)> EnglishKeys(PoFile english)
        {
            var keys = new List<(string key, string english)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in english.Entries)
                if (!entry.obsolete && seen.Add(entry.key)) keys.Add((entry.key, entry.text));
            return keys;
        }

        /// <summary>Brings a translation in step with the wanted keys and their English, and gives it a header with its language and encoding when it has none.</summary>
        public static void UpdateTranslation(PoFile file, string code, IReadOnlyList<(string key, string english)> wanted)
        {
            if (file.HeaderValue(PoFile.LanguageField) == null) file.SetHeader(PoFile.LanguageField, code);
            if (file.HeaderValue("Content-Type") == null) file.SetHeader("Content-Type", "text/plain; charset=UTF-8");

            var old = new Dictionary<string, PoFile.Entry>(StringComparer.Ordinal);
            var order = new List<PoFile.Entry>();
            var twice = new List<PoFile.Entry>();
            foreach (var entry in file.Entries)
            {
                if (old.ContainsKey(entry.key))
                {
                    twice.Add(entry);
                    continue;
                }
                old[entry.key] = entry;
                order.Add(entry);
            }

            var result = new List<PoFile.Entry>();
            foreach (var (key, english) in wanted)
            {
                var source = Source(english);
                if (old.TryGetValue(key, out var entry))
                {
                    old.Remove(key);
                    entry.obsolete = false;
                    Follow(entry, source);
                    result.Add(entry);
                }
                else
                    result.Add(new PoFile.Entry { key = key, english = source });
            }
            foreach (var entry in order)
            {
                if (!old.ContainsKey(entry.key)) continue;
                entry.obsolete = true;
                result.Add(entry);
            }
            foreach (var entry in twice)
            {
                entry.obsolete = true;
                result.Add(entry);
            }
            file.SetEntries(result);
        }

        static void Follow(PoFile.Entry entry, string source)
        {
            if (entry.english == null)
            {
                entry.english = source;
                return;
            }
            if (Source(entry.english) == source)
            {
                entry.english = source;
                if (!entry.fuzzy) entry.previousEnglish = null;
                return;
            }
            if (entry.text.Length > 0)
            {
                if (!entry.fuzzy) entry.previousEnglish = entry.english;
                entry.fuzzy = true;
            }
            entry.english = source;
        }

        /// <summary>
        /// Brings one translation file in step with English and writes it when that changed what it holds; a file in
        /// step keeps its layout, such as Poedit's wrapped lines or Windows line ends. A file AvatarDoc cannot read (not
        /// UTF-8, or a line it does not understand) is never written, so a translator's work in it is not lost. False
        /// when nothing was written.
        /// </summary>
        public static bool Update(string path, string code, PoFile english)
        {
            var full = Path.GetFullPath(path);
            if (!File.Exists(full)) return false;
            var text = LanguageFile.Decode(File.ReadAllBytes(full), out var error);
            if (error != null) return false;
            var file = PoFile.Parse(text);
            if (file.Problems.Count > 0) return false;
            var before = file.Write();
            UpdateTranslation(file, code, EnglishKeys(english));
            var updated = file.Write();
            if (updated == before) return false;
            File.WriteAllText(full, updated, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
            return true;
        }
    }
}
#endif
