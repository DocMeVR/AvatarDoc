#if DOCME_NDMF
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The checks a language file must pass, each answering in plain words: which language, which key, and what to do.
    /// LocalizationFileTests runs them on every language file in the project; saving a language file runs them too and
    /// writes what is wrong to the Console.
    /// </summary>
    public static class LanguageCheck
    {
        static readonly Regex PlaceholderPattern = new Regex(@"\{(\d+)(?:[,:][^{}]*)?\}", RegexOptions.CultureInvariant);

        static string Name(LanguageFile file) => file.Code + " (" + file.Path + ")";

        /// <summary>The numbers of the {n} placeholders in a text, each once, smallest first.</summary>
        public static IReadOnlyList<int> Placeholders(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<int>();
            return PlaceholderPattern.Matches(text).Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).Distinct().OrderBy(n => n).ToList();
        }

        static string Show(IReadOnlyList<int> placeholders) => placeholders.Count == 0 ? "none" : string.Join(" ", placeholders.Select(n => "{" + n + "}"));

        /// <summary>Whether the file is valid JSON in UTF-8 and holds only "key": "text" pairs.</summary>
        public static List<string> Format(LanguageFile file)
        {
            var problems = new List<string>();
            if (file.Error != null) problems.Add(Name(file) + " cannot be read: " + file.Error + " Compare it with AvatarDoctor.en-US.json, which has the right shape.");
            return problems;
        }

        public static List<string> DuplicateKeys(LanguageFile file)
        {
            var problems = new List<string>();
            foreach (var group in file.Entries.GroupBy(e => e.Key).Where(g => g.Count() > 1))
                problems.Add(Name(file) + ": \"" + group.Key + "\" is written " + group.Count() + " times (lines " + string.Join(", ", group.Select(e => e.Line)) + "). Keep one of them.");
            return problems;
        }

        public static List<string> UnknownKeys(LanguageFile english, LanguageFile file)
        {
            var known = new HashSet<string>(english.Entries.Select(e => e.Key), StringComparer.Ordinal);
            return file.Entries.Where(e => !known.Contains(e.Key)).Select(e => e.Key).Distinct()
                .Select(key => Name(file) + ": \"" + key + "\" is not in English, so nothing shows it. Check its spelling against AvatarDoctor.en-US.json, or remove it if English no longer has it.")
                .ToList();
        }

        public static List<string> Placeholders(LanguageFile english, LanguageFile file)
        {
            var englishTexts = english.ToTable();
            var problems = new List<string>();
            foreach (var entry in file.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Text) || !englishTexts.TryGetValue(entry.Key, out var englishText)) continue;
                var expected = Placeholders(englishText);
                var actual = Placeholders(entry.Text);
                if (expected.SequenceEqual(actual)) continue;
                problems.Add(Name(file) + ": \"" + entry.Key + "\" has the placeholders " + Show(actual) + " but English has " + Show(expected)
                    + ". Keep every placeholder the English text has and add none; you may move them within the sentence.");
            }
            return problems;
        }

        /// <summary>
        /// Whether AvatarDoc can fill a text in. A text whose English has placeholders goes through string.Format with
        /// one argument for every placeholder English uses, exactly as the code does, so a lone or broken brace that
        /// would throw while the inspector draws is caught here. A text English shows as it is may hold no brace English
        /// does not hold.
        /// </summary>
        public static List<string> Braces(LanguageFile english, LanguageFile file)
        {
            var englishTexts = english.ToTable();
            var problems = new List<string>();
            foreach (var entry in file.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Text) || !englishTexts.TryGetValue(entry.Key, out var englishText)) continue;
                var expected = Placeholders(englishText);
                if (expected.Count == 0)
                {
                    if (entry.Text.IndexOfAny(BraceCharacters) >= 0 && englishText.IndexOfAny(BraceCharacters) < 0)
                        problems.Add(Name(file) + ": \"" + entry.Key + "\" has a brace { or } that English does not have. This text is shown as it is, so remove the brace.");
                    continue;
                }
                try { string.Format(System.Globalization.CultureInfo.InvariantCulture, entry.Text, new object[expected.Max() + 1]); }
                catch (FormatException)
                {
                    problems.Add(Name(file) + ": \"" + entry.Key + "\" has a { or } that is not a placeholder, so AvatarDoc cannot fill it in. Keep each placeholder as {0}, {1} and so on, and write {{ or }} for a brace that should show.");
                }
            }
            return problems;
        }

        static readonly char[] BraceCharacters = { '{', '}' };

        static readonly Regex[] TagPatterns =
        {
            new Regex("<b>", RegexOptions.CultureInvariant), new Regex("</b>", RegexOptions.CultureInvariant),
            new Regex("<i>", RegexOptions.CultureInvariant), new Regex("</i>", RegexOptions.CultureInvariant),
            new Regex("<color=[^>]*>", RegexOptions.CultureInvariant), new Regex("</color>", RegexOptions.CultureInvariant),
        };

        static readonly string[] TagNames = { "<b>", "</b>", "<i>", "</i>", "<color=...>", "</color>" };

        /// <summary>Whether every rich text tag a text opens is closed again: &lt;b&gt;, &lt;i&gt; and &lt;color=...&gt;.</summary>
        public static List<string> Tags(LanguageFile file)
        {
            var problems = new List<string>();
            foreach (var entry in file.Entries)
            {
                if (string.IsNullOrEmpty(entry.Text)) continue;
                for (int t = 0; t < TagPatterns.Length; t += 2)
                {
                    int opened = TagPatterns[t].Matches(entry.Text).Count;
                    int closed = TagPatterns[t + 1].Matches(entry.Text).Count;
                    if (opened == closed) continue;
                    problems.Add(Name(file) + ": \"" + entry.Key + "\" has " + opened + " " + TagNames[t] + " but " + closed + " " + TagNames[t + 1]
                        + ". Close every " + TagNames[t] + " with " + TagNames[t + 1] + ", or remove the one left over.");
                }
            }
            return problems;
        }

        public static List<string> EmptyTexts(LanguageFile file)
        {
            return file.Entries.Where(e => string.IsNullOrWhiteSpace(e.Text))
                .Select(e => Name(file) + ": \"" + e.Key + "\" is empty (line " + e.Line + "). Write its translation; English shows until you do.")
                .ToList();
        }

        /// <summary>The first key out of the English order, if any. Only keys both files have count, so a missing key never makes the rest look out of order.</summary>
        public static List<string> Order(LanguageFile english, LanguageFile file)
        {
            var position = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in english.Entries)
                if (!position.ContainsKey(entry.Key)) position[entry.Key] = position.Count;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var shared = file.Entries.Where(e => position.ContainsKey(e.Key) && seen.Add(e.Key)).ToList();
            for (int i = 1; i < shared.Count; i++)
            {
                if (position[shared[i].Key] > position[shared[i - 1].Key]) continue;
                return new List<string>
                {
                    Name(file) + ": \"" + shared[i].Key + "\" (line " + shared[i].Line + ") is out of order: in English it comes before \"" + shared[i - 1].Key
                    + "\". Put the keys in the same order as AvatarDoctor.en-US.json, so a change is easy to compare.",
                };
            }
            return new List<string>();
        }

        /// <summary>The English keys a file lacks. Only a shipped language must have them all; a file of your own may leave some out, and English shows for those.</summary>
        public static List<string> MissingKeys(LanguageFile english, LanguageFile file)
        {
            var have = new HashSet<string>(file.Entries.Select(e => e.Key), StringComparer.Ordinal);
            return english.Entries.Where(e => !have.Contains(e.Key)).Select(e => e.Key).Distinct()
                .Select(key => Name(file) + ": \"" + key + "\" is missing. A language that ships with AvatarDoc needs every English key: add it with its translation, in the same place as in AvatarDoctor.en-US.json.")
                .ToList();
        }

        /// <summary>Every problem of one file. Missing keys count only for a shipped language.</summary>
        public static List<string> All(LanguageFile english, LanguageFile file)
        {
            var problems = Format(file);
            if (file.Error != null || english == null || english.Error != null) return problems;
            problems.AddRange(DuplicateKeys(file));
            if (file.Code == LanguageTables.English && file.Shipped) return problems;
            problems.AddRange(UnknownKeys(english, file));
            problems.AddRange(Placeholders(english, file));
            problems.AddRange(Braces(english, file));
            problems.AddRange(Tags(file));
            problems.AddRange(EmptyTexts(file));
            problems.AddRange(Order(english, file));
            if (file.Shipped) problems.AddRange(MissingKeys(english, file));
            return problems;
        }

        /// <summary>Runs every check on the files just saved and writes one Console warning per file that has problems, listing them all.</summary>
        public static void ReportToConsole(IEnumerable<string> savedPaths)
        {
            var paths = new HashSet<string>(savedPaths ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            if (paths.Count == 0) return;
            foreach (var path in paths.Where(p => LanguageTables.CodeFromFileName(System.IO.Path.GetFileName(p)) == null))
                Debug.LogWarning("[AvatarDoc] " + path + " is not used: the part between \"AvatarDoctor.\" and \".json\" is not a language code NDMF knows. Use a code such as ja-JP, zh-Hans or ko-KR.");
            var english = LanguageTables.ReadEnglish();
            foreach (var found in LanguageTables.Files().Where(f => paths.Contains(f.Path)))
            {
                var problems = All(english, LanguageTables.Read(found));
                if (problems.Count == 0) continue;
                Debug.LogWarning("[AvatarDoc] " + found.Path + " needs a fix:\n" + string.Join("\n", problems));
            }
        }
    }
}
#endif
