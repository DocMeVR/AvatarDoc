#if DOCME_NDMF
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// One language file: a gettext .po file in UTF-8, with or without a byte order mark, read through PoFile. Every
    /// msgid is a key and its msgstr the text. A line PoFile does not understand makes the whole file unreadable, so a
    /// file that loads here also loads in Poedit and other translation tools.
    /// </summary>
    public sealed class LanguageFile
    {
        public readonly struct Entry
        {
            public readonly string Key;
            public readonly string Text;
            public readonly int Line;

            /// <summary>The English this translation was made from ("#. en:"); null in the English file and in a file that does not say.</summary>
            public readonly string English;

            /// <summary>The English before its last change ("#. was:"), kept while the translation is outdated.</summary>
            public readonly string PreviousEnglish;

            /// <summary>Marked outdated ("#, fuzzy"): English shows in its place until the mark is removed.</summary>
            public readonly bool Fuzzy;

            public Entry(string key, string text, int line, string english = null, string previousEnglish = null, bool fuzzy = false)
            {
                Key = key;
                Text = text;
                Line = line;
                English = english;
                PreviousEnglish = previousEnglish;
                Fuzzy = fuzzy;
            }
        }

        /// <summary>The asset path, or a made up name for a file a test builds in memory.</summary>
        public string Path { get; }

        /// <summary>The language code from the file name, written the standard way: "ja-JP", "zh-Hans".</summary>
        public string Code { get; }

        /// <summary>Whether the file sits inside AvatarDoc's own package folder, so it ships with every copy.</summary>
        public bool Shipped { get; }

        /// <summary>The entries in file order, without the old ones. A key written twice appears twice.</summary>
        public IReadOnlyList<Entry> Entries { get; }

        /// <summary>The old entries ("#~"): keys English no longer has, kept so their translation is not lost. AvatarDoc shows none of them.</summary>
        public IReadOnlyList<Entry> Obsolete { get; }

        /// <summary>Why the file could not be read, in plain words with the line; null when it read.</summary>
        public string Error { get; }

        LanguageFile(string path, string code, bool shipped, IReadOnlyList<Entry> entries, IReadOnlyList<Entry> obsolete, string error)
        {
            Path = path;
            Code = code;
            Shipped = shipped;
            Entries = entries;
            Obsolete = obsolete;
            Error = error;
        }

        /// <summary>The text of a file's bytes in UTF-8, without its byte order mark; null, with the reason, when the bytes are not UTF-8.</summary>
        public static string Decode(byte[] bytes, out string error)
        {
            error = null;
            if (bytes == null)
            {
                error = "the file could not be opened.";
                return null;
            }
            if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
            {
                error = "the file is saved as UTF-16. Save it again as UTF-8.";
                return null;
            }
            int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            try { return new UTF8Encoding(false, true).GetString(bytes, start, bytes.Length - start); }
            catch (DecoderFallbackException)
            {
                error = "the file is not saved as UTF-8. Save it again as UTF-8.";
                return null;
            }
        }

        public static LanguageFile Read(string path, string code, bool shipped, byte[] bytes)
        {
            var empty = Array.Empty<Entry>();
            LanguageFile Unreadable(string error) => new LanguageFile(path, code, shipped, empty, empty, error);
            var text = Decode(bytes, out var decodeError);
            if (decodeError != null) return Unreadable(decodeError);
            var po = PoFile.Parse(text);
            if (po.Problems.Count > 0) return Unreadable(po.Problems[0]);
            Entry Of(PoFile.Entry e) => new Entry(e.key, e.text, e.line, e.english, e.previousEnglish, e.fuzzy);
            return new LanguageFile(path, code, shipped,
                po.Entries.Where(e => !e.obsolete).Select(Of).ToList(),
                po.Entries.Where(e => e.obsolete).Select(Of).ToList(),
                null);
        }

        /// <summary>The texts by key: the first of a key written twice, and no blank or outdated text, so English shows for those.</summary>
        public Dictionary<string, string> ToTable()
        {
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in Entries)
                if (seen.Add(entry.Key) && !entry.Fuzzy && !string.IsNullOrWhiteSpace(entry.Text)) table[entry.Key] = entry.Text;
            return table;
        }
    }
}
#endif
