#if DOCME_NDMF
using System;
using System.Collections.Generic;
using System.Text;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// One language file, read strictly: a single JSON object of "key": "text" pairs in UTF-8, with or without a byte
    /// order mark. Anything a strict JSON reader refuses (a comma after the last pair, a comment, single quotes, a
    /// value that is not text) makes the whole file unreadable, so a file that loads here also loads in any other
    /// translation tool.
    /// </summary>
    public sealed class LanguageFile
    {
        public readonly struct Entry
        {
            public readonly string Key;
            public readonly string Text;
            public readonly int Line;

            public Entry(string key, string text, int line)
            {
                Key = key;
                Text = text;
                Line = line;
            }
        }

        /// <summary>The asset path, or a made up name for a file a test builds in memory.</summary>
        public string Path { get; }

        /// <summary>The language code from the file name, written the standard way: "ja-JP", "zh-Hans".</summary>
        public string Code { get; }

        /// <summary>Whether the file sits inside AvatarDoc's own package folder, so it ships with every copy.</summary>
        public bool Shipped { get; }

        /// <summary>The pairs in file order. A key written twice appears twice.</summary>
        public IReadOnlyList<Entry> Entries { get; }

        /// <summary>Why the file could not be read, in plain words with the line; null when it read.</summary>
        public string Error { get; }

        LanguageFile(string path, string code, bool shipped, IReadOnlyList<Entry> entries, string error)
        {
            Path = path;
            Code = code;
            Shipped = shipped;
            Entries = entries;
            Error = error;
        }

        public static LanguageFile Read(string path, string code, bool shipped, byte[] bytes)
        {
            var empty = Array.Empty<Entry>();
            if (bytes == null) return new LanguageFile(path, code, shipped, empty, "the file could not be opened.");
            if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
                return new LanguageFile(path, code, shipped, empty, "the file is saved as UTF-16. Save it again as UTF-8.");
            int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes, start, bytes.Length - start); }
            catch (DecoderFallbackException) { return new LanguageFile(path, code, shipped, empty, "the file is not saved as UTF-8. Save it again as UTF-8."); }
            var reader = new Reader(text);
            var entries = new List<Entry>();
            var error = reader.ReadTable(entries);
            return new LanguageFile(path, code, shipped, error == null ? (IReadOnlyList<Entry>)entries : empty, error);
        }

        /// <summary>The texts by key: the last of a key written twice, and no blank text, so a blank one falls back to English.</summary>
        public Dictionary<string, string> ToTable()
        {
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in Entries)
                if (!string.IsNullOrWhiteSpace(entry.Text)) table[entry.Key] = entry.Text;
            return table;
        }

        sealed class Reader
        {
            readonly string text;
            int pos;
            int line = 1;
            int lineStart;

            public Reader(string text) => this.text = text;

            string Where => "line " + line + ", column " + (pos - lineStart + 1);

            bool AtEnd => pos >= text.Length;

            char Peek => text[pos];

            void SkipSpace()
            {
                while (!AtEnd)
                {
                    char c = Peek;
                    if (c == '\n') { pos++; line++; lineStart = pos; }
                    else if (c == ' ' || c == '\t' || c == '\r') pos++;
                    else return;
                }
            }

            /// <summary>Reads the whole file into entries. Returns why it failed, or null.</summary>
            public string ReadTable(List<Entry> entries)
            {
                SkipSpace();
                if (AtEnd) return "the file is empty. It needs { at the start and } at the end.";
                if (Peek != '{') return Where + ": the file must start with {.";
                pos++;
                SkipSpace();
                if (!AtEnd && Peek == '}') { pos++; return AfterEnd(); }
                string previous = null;
                while (true)
                {
                    SkipSpace();
                    if (AtEnd) return "the file ends before its closing }.";
                    if (Peek == '}') return Where + ": there is a comma after the last text" + After(previous) + ". Remove that comma.";
                    if (Peek != '"') return Where + ": expected a key in double quotes" + After(previous) + ".";
                    int keyLine = line;
                    var key = ReadString(out var keyError);
                    if (keyError != null) return keyError;
                    SkipSpace();
                    if (AtEnd || Peek != ':') return Where + ": expected : after the key \"" + key + "\".";
                    pos++;
                    SkipSpace();
                    if (AtEnd) return "the file ends after the key \"" + key + "\".";
                    if (Peek != '"') return Where + ": the text of \"" + key + "\" must be in double quotes.";
                    var value = ReadString(out var valueError);
                    if (valueError != null) return valueError;
                    entries.Add(new Entry(key, value, keyLine));
                    previous = key;
                    SkipSpace();
                    if (AtEnd) return "the file ends before its closing }.";
                    if (Peek == ',') { pos++; continue; }
                    if (Peek == '}') { pos++; return AfterEnd(); }
                    return Where + ": expected a comma or } after the text of \"" + key + "\".";
                }
            }

            static string After(string key) => key == null ? "" : " (after \"" + key + "\")";

            string AfterEnd()
            {
                SkipSpace();
                return AtEnd ? null : Where + ": there is more after the closing }. Remove it.";
            }

            string ReadString(out string error)
            {
                error = null;
                var where = Where;
                pos++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) { error = where + ": a text is not closed with a double quote."; return null; }
                    char c = text[pos++];
                    if (c == '"') return sb.ToString();
                    if (c == '\n' || c == '\r') { error = where + ": a text runs onto the next line. Write \\n for a line break inside a text."; return null; }
                    if (c < ' ') { error = where + ": a text holds a control character. Write \\t for a tab."; return null; }
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) { error = where + ": a text ends in a lone backslash."; return null; }
                    char e = text[pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (pos + 4 > text.Length || !int.TryParse(text.Substring(pos, 4), System.Globalization.NumberStyles.AllowHexSpecifier, null, out var unit))
                            {
                                error = where + ": \\u must be followed by four hexadecimal digits.";
                                return null;
                            }
                            sb.Append((char)unit);
                            pos += 4;
                            break;
                        default:
                            error = where + ": \\" + e + " is not a JSON escape. Write \\\\ for a backslash, \\\" for a double quote.";
                            return null;
                    }
                }
            }
        }
    }
}
#endif
