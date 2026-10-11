using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Every error, warning and info message names the exact thing, says what
    /// to do, in the words the creator sees on screen, never in AvatarDoc's own internal words, and every call
    /// site passes enough arguments to fill every placeholder its text uses.
    /// </summary>
    public class MessageGuardTests
    {
        /// <summary>Words the UI never shows. Found by auditing the actual message texts.</summary>
        static readonly string[] BannedWords =
        {
            "gate", "claim", "binding", "occurrence", "pin layer", "explicit off", "skiplayers", "parameter control",
        };

        static Dictionary<string, string> ReadLocalizationEntries()
        {
            var path = L.TablePath("en-US");
            Assert.That(path, Is.Not.Null, "the English localization file must exist");
            var text = File.ReadAllText(Path.GetFullPath(path));
            var result = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(text, "\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\""))
            {
                var key = Regex.Unescape(m.Groups[1].Value);
                var value = Regex.Unescape(m.Groups[2].Value);
                result[key] = value;
            }
            return result;
        }

        static IEnumerable<KeyValuePair<string, string>> InScope(Dictionary<string, string> entries, params string[] prefixes)
            => entries.Where(e => prefixes.Any(p => e.Key.StartsWith(p, StringComparison.Ordinal)));

        /// <summary>Sentences: a period followed by whitespace and a capital letter or a quote, or a period at the very end once a trailing "(...)" object reference is stripped off.</summary>
        static int SentenceCount(string text)
        {
            var trimmed = Regex.Replace(text, @"\s*\([^()]*\)\s*$", "");
            return Regex.Matches(trimmed, "\\.(?=\\s+[A-Z\"]|$)").Count;
        }

        [Test]
        public void NoMessageUsesInternalWords()
        {
            var entries = ReadLocalizationEntries();
            foreach (var entry in InScope(entries, "ad.error.", "ad.warn.", "ad.info."))
            {
                var lower = entry.Value.ToLowerInvariant();
                foreach (var word in BannedWords)
                    Assert.That(lower, Does.Not.Contain(word), $"{entry.Key} uses the internal word \"{word}\": \"{entry.Value}\"");
            }
        }

        /// <summary>
        /// Errors and warnings that are exempt from the two-sentence rule: each one is a deliberate call, not an
        /// oversight, recorded here so a future editor cannot silently add another one-sentence message.
        /// </summary>
        static readonly HashSet<string> OneSentenceExceptions = new HashSet<string>();

        [Test]
        public void EveryErrorAndWarningSaysWhatToDo()
        {
            var entries = ReadLocalizationEntries();
            foreach (var entry in InScope(entries, "ad.error.", "ad.warn."))
            {
                if (OneSentenceExceptions.Contains(entry.Key)) continue;
                Assert.That(SentenceCount(entry.Value), Is.GreaterThanOrEqualTo(2),
                    $"{entry.Key} is not two sentences (what happened, then what to do): \"{entry.Value}\"");
                bool hasPlaceholder = Regex.IsMatch(entry.Value, @"\{\d+\}");
                bool hasQuotedLabel = entry.Value.Contains("\"");
                Assert.That(hasPlaceholder || hasQuotedLabel, Is.True,
                    $"{entry.Key} names nothing: no placeholder and no quoted UI label: \"{entry.Value}\"");
            }
        }

        // ---- call sites: every argument list is at least as long as its text needs -----------------------------

        internal static List<string> SplitTopLevelArgs(string s)
        {
            var args = new List<string>();
            int depth = 0;
            int start = 0;
            bool inString = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (inString)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
                else if (c == ',' && depth == 0)
                {
                    args.Add(s.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }
            var last = s.Substring(start).Trim();
            if (last.Length > 0 || args.Count > 0) args.Add(last);
            return args;
        }

        /// <summary>The text strictly between the parenthesis that opens at openParenIndex and its match, or null when unbalanced.</summary>
        internal static string ExtractBalanced(string source, int openParenIndex)
        {
            int depth = 0;
            bool inString = false;
            for (int i = openParenIndex; i < source.Length; i++)
            {
                char c = source[i];
                if (inString)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0) return source.Substring(openParenIndex + 1, i - openParenIndex - 1);
                }
            }
            return null;
        }

        static readonly Regex KeyLiteral = new Regex("^\"((?:[^\"\\\\]|\\\\.)*)\"$");
        static readonly Regex CountKeyCall = new Regex(@"^UiText\.CountKey\s*\(");
        static readonly Regex TextCall = new Regex(@"^L\.Text\s*\(");

        /// <summary>The keys one key argument names: a literal, or both forms of UiText.CountKey(n, "key").</summary>
        static List<string> KeysOf(string argument)
        {
            var literal = KeyLiteral.Match(argument);
            if (literal.Success) return new List<string> { Regex.Unescape(literal.Groups[1].Value) };
            var call = CountKeyCall.Match(argument);
            if (!call.Success) return new List<string>();
            var inner = ExtractBalanced(argument, call.Index + call.Length - 1);
            var args = inner != null ? SplitTopLevelArgs(inner) : new List<string>();
            var key = args.Count >= 2 ? KeyLiteral.Match(args[1]) : Match.Empty;
            if (!key.Success) return new List<string>();
            var name = Regex.Unescape(key.Groups[1].Value);
            return new List<string> { name, name + ".one" };
        }

        /// <summary>The keys the first argument of string.Format names: L.Text("key"), or L.Text(UiText.CountKey(n, "key")).</summary>
        static List<string> TextKeysOf(string argument)
        {
            var call = TextCall.Match(argument);
            if (!call.Success) return new List<string>();
            int open = call.Index + call.Length - 1;
            var inner = ExtractBalanced(argument, open);
            bool whole = inner != null && open + inner.Length + 2 == argument.Length;
            return whole ? KeysOf(inner.Trim()) : new List<string>();
        }

        /// <summary>Every (key, argument count, file) a message is raised with, through ModelMessage.Error/Warning/Info directly or through string.Format(L.Text("key"), ...), with both forms of a count's key.</summary>
        static IEnumerable<(string key, int argCount, string file)> ScanCallSites(string root)
        {
            foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                foreach (Match m in Regex.Matches(text, @"ModelMessage\.(?:Error|Warning|Info)\s*\("))
                {
                    int open = m.Index + m.Length - 1;
                    var inner = ExtractBalanced(text, open);
                    if (inner == null) continue;
                    var args = SplitTopLevelArgs(inner);
                    if (args.Count == 0) continue;
                    foreach (var key in KeysOf(args[0])) yield return (key, args.Count - 1, file);
                }
                foreach (Match m in Regex.Matches(text, @"[Ss]tring\.Format\s*\("))
                {
                    int open = m.Index + m.Length - 1;
                    var inner = ExtractBalanced(text, open);
                    if (inner == null) continue;
                    var args = SplitTopLevelArgs(inner);
                    if (args.Count == 0) continue;
                    foreach (var key in TextKeysOf(args[0])) yield return (key, args.Count - 1, file);
                }
            }
        }

        [Test]
        public void MessageArgumentsMatchPlaceholders()
        {
            var entries = ReadLocalizationEntries();
            var root = Path.GetFullPath(Path.Combine(QuickStart.PackageRoot() ?? "", "Editor"));
            var callSites = ScanCallSites(root)
                .Where(c => c.key.StartsWith("ad.error.", StringComparison.Ordinal)
                    || c.key.StartsWith("ad.warn.", StringComparison.Ordinal)
                    || c.key.StartsWith("ad.info.", StringComparison.Ordinal))
                .ToList();
            Assert.That(callSites.Count, Is.GreaterThan(0), "expected to find ModelMessage call sites to scan under " + root);

            foreach (var (key, argCount, file) in callSites)
            {
                if (!entries.TryGetValue(key, out var text)) continue; // LocalizationTests already requires every key to exist.
                int maxPlaceholder = -1;
                foreach (Match pm in Regex.Matches(text, @"\{(\d+)\}"))
                    maxPlaceholder = Math.Max(maxPlaceholder, int.Parse(pm.Groups[1].Value));
                int needed = maxPlaceholder + 1;
                Assert.That(argCount, Is.GreaterThanOrEqualTo(needed),
                    $"{key} in {Path.GetFileName(file)} is raised with {argCount} argument(s) but its text needs {needed} (it uses {{{maxPlaceholder}}}): \"{text}\"");
            }
        }
    }
}
