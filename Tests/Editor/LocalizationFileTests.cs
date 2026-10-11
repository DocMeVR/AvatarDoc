#if DOCME_NDMF
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The guard on every language file in the project (AvatarDoctor.&lt;code&gt;.json, shipped or a contributor's own),
    /// and proof that each check catches its mistake and says in plain words which language, which key and what to
    /// fix. The proofs read files built in memory; only the drop-in test writes a file, into a temporary folder under
    /// Assets that it deletes again.
    /// </summary>
    public class LocalizationFileTests
    {
        static LanguageFile Make(string code, string json, bool shipped = false)
            => LanguageFile.Read("Test/AvatarDoctor." + code + ".json", code, shipped, Encoding.UTF8.GetBytes(json));

        static LanguageFile TestEnglish() => Make("en-US", "{\n  \"a\": \"Move {0} to {1}.\",\n  \"b\": \"Bee\",\n  \"c\": \"Sea {0}\"\n}", shipped: true);

        static LanguageFile RealEnglish()
        {
            var english = LanguageTables.ReadEnglish();
            Assert.That(english, Is.Not.Null, "AvatarDoctor.en-US.json must sit in the package's Editor/Localization folder");
            Assert.That(english.Error, Is.Null, "AvatarDoctor.en-US.json cannot be read: " + english.Error);
            return english;
        }

        static List<LanguageFile> RealFiles()
        {
            var files = LanguageTables.Files().Select(LanguageTables.Read).ToList();
            Assert.That(files.Any(f => f.Code == LanguageTables.English && f.Shipped), Is.True, "the shipped English file must be among the language files found");
            return files;
        }

        static void AssertNone(List<string> problems) => Assert.That(problems, Is.Empty, "\n" + string.Join("\n", problems));

        // ---- the guard on every real file ---------------------------------------------------------------------

        [Test]
        public void EveryLanguageFileIsValidJson()
        {
            AssertNone(RealFiles().SelectMany(LanguageCheck.Format).ToList());
        }

        [Test]
        public void NoLanguageFileWritesAKeyTwice()
        {
            AssertNone(RealFiles().SelectMany(LanguageCheck.DuplicateKeys).ToList());
        }

        [Test]
        public void NoLanguageHasAKeyEnglishLacks()
        {
            var english = RealEnglish();
            AssertNone(RealFiles().Where(f => f.Error == null).SelectMany(f => LanguageCheck.UnknownKeys(english, f)).ToList());
        }

        [Test]
        public void EveryTextKeepsTheEnglishPlaceholders()
        {
            var english = RealEnglish();
            AssertNone(RealFiles().Where(f => f.Error == null).SelectMany(f => LanguageCheck.Placeholders(english, f)).ToList());
        }

        [Test]
        public void NoLanguageHasAnEmptyText()
        {
            AssertNone(RealFiles().Where(f => f.Error == null).SelectMany(LanguageCheck.EmptyTexts).ToList());
        }

        [Test]
        public void EveryTextFormatsWithoutAStrayBrace()
        {
            var english = RealEnglish();
            AssertNone(RealFiles().Where(f => f.Error == null).SelectMany(f => LanguageCheck.Braces(english, f)).ToList());
        }

        [Test]
        public void EveryRichTextTagIsClosed()
        {
            AssertNone(RealFiles().Where(f => f.Error == null).SelectMany(LanguageCheck.Tags).ToList());
        }

        [Test]
        public void EveryLanguageKeepsTheEnglishOrder()
        {
            var english = RealEnglish();
            AssertNone(RealFiles().Where(f => f.Error == null).SelectMany(f => LanguageCheck.Order(english, f)).ToList());
        }

        [Test]
        public void EveryShippedLanguageHasEveryEnglishKey()
        {
            var english = RealEnglish();
            AssertNone(RealFiles().Where(f => f.Error == null && f.Shipped).SelectMany(f => LanguageCheck.MissingKeys(english, f)).ToList());
        }

        [Test]
        public void EveryKeyTheCodeNamesIsInEnglish()
        {
            var english = new HashSet<string>(RealEnglish().Entries.Select(e => e.Key));
            var missing = L.KnownKeys.Concat(L.UiKeys).Where(k => !english.Contains(k)).ToList();
            Assert.That(missing, Is.Empty, "these keys are listed in L.cs but AvatarDoctor.en-US.json has no text for them: " + string.Join(", ", missing)
                + ". Add a text for each to AvatarDoctor.en-US.json, or remove it from L.cs.");
        }

        [Test]
        public void EveryLanguageCallsTheToolAvatarDoc()
        {
            var problems = RealFiles().Where(f => f.Error == null).SelectMany(f => f.Entries
                    .Where(e => e.Text.Contains("AvatarDoctor"))
                    .Select(e => f.Code + ": \"" + e.Key + "\" calls the tool AvatarDoctor, its old inside name. Write AvatarDoc."))
                .ToList();
            AssertNone(problems);
        }

        [Test]
        public void EveryCountHasARealSingular()
        {
            AssertNone(CountProblems(RealEnglish(), NotSingularCounts));
        }

        [Test]
        public void EveryKeyTheCodeAsksForIsInEnglish()
        {
            var english = new HashSet<string>(RealEnglish().Entries.Select(e => e.Key));
            var listed = new HashSet<string>(L.KnownKeys.Concat(L.UiKeys));
            var root = Path.GetFullPath(Path.Combine(LanguageTables.PackageRoot() ?? "", "Editor"));
            var problems = new List<string>();
            int found = 0;
            foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                foreach (var (key, line) in KeysAskedFor(File.ReadAllText(file)))
                {
                    found++;
                    string where = Path.GetFileName(file) + ":" + line;
                    if (!english.Contains(key))
                        problems.Add(where + " asks for \"" + key + "\", but AvatarDoctor.en-US.json has no text for it, so the creator would see <" + key + ">. Add the text to all three tables and the key to L.UiKeys or L.KnownKeys.");
                    else if (!listed.Contains(key))
                        problems.Add(where + " asks for \"" + key + "\", which L.cs does not list. Add it to L.UiKeys or L.KnownKeys, beside its neighbours.");
                }
            }
            Assert.That(found, Is.GreaterThan(500), "expected to find the L.Text calls under " + root);
            AssertNone(problems);
        }

        [Test]
        public void EveryCountTheCodeSaysPicksItsSingular()
        {
            var table = RealEnglish().ToTable();
            var root = Path.GetFullPath(Path.Combine(LanguageTables.PackageRoot() ?? "", "Editor"));
            var problems = new List<string>();
            foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                foreach (Match m in TextCall.Matches(text))
                {
                    var key = Regex.Unescape(m.Groups[1].Value);
                    if (!table.TryGetValue(key, out var english) || !table.ContainsKey(key + ".one") || !Regex.IsMatch(english, @"\{\d+\}")) continue;
                    if (text.Contains("\"" + key + ".one\"")) continue;
                    problems.Add(Path.GetFileName(file) + ":" + LineOf(text, m.Index) + " says \"" + key + "\" with L.Text, which never picks its singular \"" + key
                        + ".one\". Say it with UiText.Count or UiText.CountKey.");
                }
            }
            AssertNone(problems);
        }

        // ---- counts and the keys the code asks for ---------------------------------------------------------------

        /// <summary>
        /// Placeholders a plural noun follows that never stand for one, so they need no singular: a limit, or a value
        /// that is only said above one. Each with the reason, by key and placeholder.
        /// </summary>
        static readonly Dictionary<string, string> NotSingularCounts = new Dictionary<string, string>
        {
            { "ad.error.bits_exceeded {0}", "only said above VRChat's limit of 256 bits" },
            { "ad.warn.bits_exceeded_vrcfury {0}", "only said above VRChat's limit of 256 bits" },
            { "ad.error.menu_icon_too_large {2}", "the height of a picture size, written as {1}x{2} pixels" },
            { "ad.warn.removed_layer_ambiguous {1}", "only said when two or more layers share a name" },
            { "ad.ui.memory.free_bits {1}", "VRChat's limit of 256 bits" },
            { "ad.summary.menu.slots {1}", "VRChat's limit of 8 controls on a page" },
            { "ad.error.follow_too_many_sources {1}", "only said above the limit" },
            { "ad.error.follow_too_many_sources {2}", "the limit, followed by the verb allows" },
            { "ad.ui.when.add_and.limit.tip {0}", "the fixed limit of conditions in a group" },
            { "ad.ui.when.add_and.limit_driver.tip {0}", "the fixed limit of rows in a card" },
        };

        /// <summary>Words ending in s that follow a placeholder without counting it: verbs, and the unit ms.</summary>
        static readonly HashSet<string> NotCountNouns = new HashSet<string> { "is", "has", "was", "its", "this", "stays", "equals", "as", "does", "ms" };

        /// <summary>A placeholder and the one or two words after it: "{0} bits", "{1} synced bits".</summary>
        static readonly Regex CountPlaceholder = new Regex(@"\{(\d+)\}((?:\s+[A-Za-z]+){1,2})");

        /// <summary>
        /// Every English text that counts something without a real singular: a "(s)" anywhere, or a placeholder a plural
        /// noun follows in a key with no ".one" twin, unless the list names that placeholder with a reason. A name in
        /// the list that no longer reads like a count is named too, so the list never outlives its texts.
        /// </summary>
        static List<string> CountProblems(LanguageFile english, IReadOnlyDictionary<string, string> notCounts)
        {
            var keys = new HashSet<string>(english.Entries.Select(e => e.Key));
            var problems = new List<string>();
            var used = new HashSet<string>();
            foreach (var entry in english.Entries)
            {
                if (entry.Text.Contains("(s)"))
                    problems.Add("\"" + entry.Key + "\" says \"(s)\". Write the plural, and give the key a \".one\" twin with the singular.");
                if (entry.Key.EndsWith(".one") || keys.Contains(entry.Key + ".one")) continue;
                foreach (Match m in CountPlaceholder.Matches(entry.Text))
                {
                    var words = m.Groups[2].Value.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (!words.Any(w => w.EndsWith("s") && !NotCountNouns.Contains(w))) continue;
                    var id = entry.Key + " {" + m.Groups[1].Value + "}";
                    if (notCounts.ContainsKey(id))
                    {
                        used.Add(id);
                        continue;
                    }
                    problems.Add("\"" + entry.Key + "\" counts with {" + m.Groups[1].Value + "} (\"" + m.Value.Trim() + "\") but has no singular. Add \"" + entry.Key
                        + ".one\" with the text for one and pick it with UiText.Count or UiText.CountKey; if {" + m.Groups[1].Value + "} is never one, add \"" + id + "\" to NotSingularCounts with the reason.");
                }
            }
            foreach (var stale in notCounts.Keys.Where(k => !used.Contains(k)))
                problems.Add("NotSingularCounts names " + stale + ", which no longer reads like a count. Take it off the list.");
            return problems;
        }

        static readonly Regex TextCall = new Regex("L\\.Text\\s*\\(\\s*\"((?:[^\"\\\\]|\\\\.)*)\"\\s*\\)");
        static readonly Regex KeyLiteral = new Regex("\"(ad\\.[a-z0-9_.]*[a-z0-9_])\"");
        static readonly Regex CountKeyCall = new Regex(@"UiText\.CountKey\s*\(");
        static readonly Regex Literal = new Regex("^\"((?:[^\"\\\\]|\\\\.)*)\"$");

        static int LineOf(string text, int index) => text.Take(index).Count(c => c == '\n') + 1;

        /// <summary>
        /// Every key a source file names, with its line: every "ad..." string literal, whatever it is handed to (L.Text,
        /// a toolbar add, a message, a caption), and the ".one" twin UiText.CountKey(n, "key") picks for one. A literal
        /// that ends in a dot is a prefix, not a key.
        /// </summary>
        static List<(string key, int line)> KeysAskedFor(string text)
        {
            var found = new List<(string key, int line)>();
            foreach (Match m in KeyLiteral.Matches(text))
                found.Add((m.Groups[1].Value, LineOf(text, m.Index)));
            foreach (Match m in CountKeyCall.Matches(text))
            {
                var inner = MessageGuardTests.ExtractBalanced(text, m.Index + m.Length - 1);
                if (inner == null) continue;
                var args = MessageGuardTests.SplitTopLevelArgs(inner);
                var key = args.Count >= 2 ? Literal.Match(args[1]) : Match.Empty;
                if (key.Success) found.Add((Regex.Unescape(key.Groups[1].Value) + ".one", LineOf(text, m.Index)));
            }
            return found;
        }

        // ---- finding the files --------------------------------------------------------------------------------

        [Test]
        public void ALanguageFileIsKnownByItsNameAlone()
        {
            Assert.That(LanguageTables.CodeFromFileName("AvatarDoctor.en-US.json"), Is.EqualTo("en-US"));
            Assert.That(LanguageTables.CodeFromFileName("AvatarDoctor.ja-JP.json"), Is.EqualTo("ja-JP"));
            Assert.That(LanguageTables.CodeFromFileName("AvatarDoctor.zh-Hans.json"), Is.EqualTo("zh-Hans"));
            Assert.That(LanguageTables.CodeFromFileName("AvatarDoctor.zh-hans.json"), Is.EqualTo("zh-Hans"), "a code in the wrong case is still that language");
            Assert.That(LanguageTables.CodeFromFileName("AvatarDoctor.ko-KR.json"), Is.EqualTo("ko-KR"), "a language AvatarDoc does not ship is found the same way");
            foreach (var name in new[] { "AvatarDoctor.json", "AvatarDoctor.ja-JP.txt", "Other.ja-JP.json", "AvatarDoctor.ja-JP.json.bak", "AvatarDoctor.japanese.json", "AvatarDoctor..json", null })
                Assert.That(LanguageTables.CodeFromFileName(name), Is.Null, (name ?? "null") + " is not a language file");
            Assert.That(LanguageTables.IsLanguageFile("AvatarDoctor.xx-YY.json"), Is.True, "shaped like a language file");
            Assert.That(LanguageTables.CodeFromFileName("AvatarDoctor.xx-YY.json"), Is.Null, "but xx-YY names no language, so it is not used (and saving it says so in the Console)");
        }

        [Test]
        public void TheShippedEnglishFileIsFound()
        {
            var shippedEnglish = LanguageTables.Files().Where(f => f.Code == LanguageTables.English && f.Shipped).ToList();
            Assert.That(shippedEnglish.Count, Is.EqualTo(1), "exactly one English file ships with the package");
            Assert.That(shippedEnglish[0].Path, Is.EqualTo(L.TablePath("en-US")));
            Assert.That(shippedEnglish[0].Path, Does.StartWith(LanguageTables.PackageRoot() + "/Editor/Localization/"));
            Assert.That(LanguageTables.Codes.First(), Is.EqualTo(LanguageTables.English), "English is listed first");
            Assert.That(LanguageTables.Files().First().Shipped, Is.True, "the shipped files come before anyone's own");
        }

        [Test]
        public void AFileOfYourOwnWinsOverTheShippedOneKeyByKey()
        {
            var shipped = Make("ja-JP", "{ \"a\": \"A shipped\", \"b\": \"B shipped\" }", shipped: true);
            var own = Make("ja-JP", "{ \"b\": \"B own\", \"c\": \"\" }");
            var broken = Make("ja-JP", "{ \"a\": \"A broken\", }");
            var table = LanguageTables.Merge(new[] { shipped, own, broken });
            Assert.That(table["a"], Is.EqualTo("A shipped"), "a key only the shipped file has stays; the unreadable file is left out");
            Assert.That(table["b"], Is.EqualTo("B own"), "your own file wins");
            Assert.That(table.ContainsKey("c"), Is.False, "an empty text is no text, so English shows");
        }

        [Test]
        public void AFileOfYourOwnUnderAssetsIsFoundAndShown()
        {
            var folder = TempAssetFolder.Create("LanguageDropIn");
            var path = folder + "/AvatarDoctor.ko-KR.json";
            try
            {
                File.WriteAllText(Path.GetFullPath(path), "{\n  \"ad.ui.mode.simple\": \"간단\",\n  \"ad.ui.mode.advanced\": \"고급\"\n}\n", new UTF8Encoding(false));
                AssetDatabase.ImportAsset(path);
                LanguageTables.Reload();
                var found = LanguageTables.Files().Where(f => f.Path == path).ToList();
                Assert.That(found.Count, Is.EqualTo(1), "a file under Assets is found by its name alone");
                Assert.That(found[0].Code, Is.EqualTo("ko-KR"));
                Assert.That(found[0].Shipped, Is.False, "a file outside the package is the creator's own");
                Assert.That(LanguageTables.Codes, Does.Contain("ko-KR"));
                using (L.UseTestLanguage("ko-KR", null))
                {
                    Assert.That(L.Text("ad.ui.mode.simple"), Is.EqualTo("간단"), "its texts show");
                    Assert.That(L.Text("ad.ui.mode.advanced"), Is.EqualTo("고급"));
                    Assert.That(L.Text("ad.ui.unlock"), Is.EqualTo(LanguageTables.Text(LanguageTables.English, "ad.ui.unlock")), "and English for every key it leaves out");
                }
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
                TempAssetFolder.Delete(folder);
                LanguageTables.Reload();
            }
            Assert.That(LanguageTables.Files().Any(f => f.Path == path), Is.False, "the test leaves no language file behind");
        }

        [Test]
        public void ContributorDocsStayOutOfTheInstalledPackage()
        {
            var root = LanguageTables.PackageRoot();
            Assert.That(root, Is.Not.Null);
            var fullRoot = Path.GetFullPath(root);
            foreach (var folder in new[] { "Editor", "Runtime" })
            {
                var full = Path.Combine(fullRoot, folder);
                if (!Directory.Exists(full)) continue;
                var docs = Directory.GetFiles(full, "*.md", SearchOption.AllDirectories).Select(p => p.Substring(fullRoot.Length + 1).Replace(Path.DirectorySeparatorChar, '/')).ToList();
                Assert.That(docs, Is.Empty, "a document under " + folder + "/ ships to every creator who installs AvatarDoc. Move it to Docs~/, which the release leaves out: " + string.Join(", ", docs));
            }
            Assert.That(File.Exists(Path.Combine(fullRoot, "Docs~", "translating.md")), Is.True, "the translators' guide lives in Docs~/translating.md");
            Assert.That(File.Exists(Path.Combine(fullRoot, "Docs~", "glossary.md")), Is.True, "the glossary lives in Docs~/glossary.md");
        }

        // ---- each check catches its mistake ------------------------------------------------------------------

        [Test]
        public void BrokenJsonIsNamedWithItsLine()
        {
            var file = Make("ja-JP", "{\n  \"a\": \"A\",\n  \"b\": \"B\",\n}");
            Assert.That(file.Error, Is.Not.Null);
            var problems = LanguageCheck.Format(file);
            Assert.That(problems.Count, Is.EqualTo(1));
            Assert.That(problems[0], Does.Contain("ja-JP").And.Contain("line 4").And.Contain("comma").And.Contain("Remove"));
        }

        [Test]
        public void OnlyStrictJsonIsRead()
        {
            var notJson = new Dictionary<string, string>
            {
                { "a comment", "{ // note\n \"a\": \"A\" }" },
                { "single quotes", "{ 'a': 'A' }" },
                { "a key without quotes", "{ a: \"A\" }" },
                { "a number for a text", "{ \"a\": 1 }" },
                { "a missing comma", "{ \"a\": \"A\" \"b\": \"B\" }" },
                { "a line break inside a text", "{ \"a\": \"A\nB\" }" },
                { "an unknown escape", "{ \"a\": \"A\\qB\" }" },
                { "something after the end", "{ \"a\": \"A\" } x" },
                { "no closing brace", "{ \"a\": \"A\"" },
                { "a text that never closes", "{ \"a\": \"A }" },
                { "a list, not pairs", "[ \"a\" ]" },
                { "nothing at all", "   " },
            };
            foreach (var pair in notJson)
            {
                var file = Make("ja-JP", pair.Value);
                Assert.That(file.Error, Is.Not.Null, pair.Key + " is not JSON and must not load");
                Assert.That(file.Entries, Is.Empty, pair.Key + ": a file that cannot be read gives no texts at all");
            }
            Assert.That(Make("ja-JP", "{}").Error, Is.Null, "an empty table is valid");
        }

        [Test]
        public void Utf8WithOrWithoutAByteOrderMarkReads()
        {
            var json = "{ \"a\": \"「{0}」を表示\" }";
            var plain = Encoding.UTF8.GetBytes(json);
            var withMark = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(plain).ToArray();
            foreach (var bytes in new[] { plain, withMark })
            {
                var file = LanguageFile.Read("Test/AvatarDoctor.ja-JP.json", "ja-JP", false, bytes);
                Assert.That(file.Error, Is.Null);
                Assert.That(file.Entries.Single().Text, Is.EqualTo("「{0}」を表示"));
            }

            var utf16 = LanguageFile.Read("Test/AvatarDoctor.ja-JP.json", "ja-JP", false, Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(json)).ToArray());
            Assert.That(LanguageCheck.Format(utf16).Single(), Does.Contain("ja-JP").And.Contain("UTF-16").And.Contain("UTF-8"));

            var latin = LanguageFile.Read("Test/AvatarDoctor.ja-JP.json", "ja-JP", false, new byte[] { (byte)'{', (byte)'"', (byte)'a', (byte)'"', (byte)':', (byte)'"', 0xE9, (byte)'"', (byte)'}' });
            Assert.That(LanguageCheck.Format(latin).Single(), Does.Contain("not saved as UTF-8"));
        }

        [Test]
        public void EscapesReadAsTheirCharacters()
        {
            var file = Make("ja-JP", "{ \"a\": \"\\\"{0}\\\"\\nline\\t\\u00e9\\\\\\/\" }");
            Assert.That(file.Error, Is.Null);
            Assert.That(file.Entries.Single().Text, Is.EqualTo("\"{0}\"\nline\té\\/"));
        }

        [Test]
        public void AKeyWrittenTwiceIsNamedWithItsLines()
        {
            var file = Make("ja-JP", "{\n  \"a\": \"A\",\n  \"b\": \"B\",\n  \"a\": \"A again\"\n}");
            var problems = LanguageCheck.DuplicateKeys(file);
            Assert.That(problems.Single(), Does.Contain("ja-JP").And.Contain("\"a\"").And.Contain("lines 2, 4").And.Contain("Keep one"));
            Assert.That(file.ToTable()["a"], Is.EqualTo("A again"), "the loader keeps the last, as other JSON readers do");
        }

        [Test]
        public void AKeyEnglishLacksIsNamed()
        {
            var problems = LanguageCheck.UnknownKeys(TestEnglish(), Make("ja-JP", "{ \"a\": \"{0} {1}\", \"bee\": \"B\" }"));
            Assert.That(problems.Single(), Does.Contain("ja-JP").And.Contain("\"bee\"").And.Contain("not in English").And.Contain("spelling"));
        }

        [Test]
        public void PlaceholdersMayMoveButNotChange()
        {
            var english = TestEnglish();
            Assert.That(LanguageCheck.Placeholders(english, Make("ja-JP", "{ \"a\": \"{1} に {0} を移動\" }")), Is.Empty, "moving a placeholder within the sentence is fine");
            Assert.That(LanguageCheck.Placeholders(english, Make("ja-JP", "{ \"a\": \"{0} と {0} を {1} に\" }")), Is.Empty, "using one twice is fine");
            var dropped = LanguageCheck.Placeholders(english, Make("ja-JP", "{ \"a\": \"{0} を移動\" }"));
            Assert.That(dropped.Single(), Does.Contain("ja-JP").And.Contain("\"a\"").And.Contain("{0} {1}").And.Contain("move them"));
            var added = LanguageCheck.Placeholders(english, Make("ja-JP", "{ \"b\": \"ビー {0}\" }"));
            Assert.That(added.Single(), Does.Contain("\"b\"").And.Contain("none").And.Contain("add none"));
            Assert.That(LanguageCheck.Placeholders("{0:0.0} and {1,5} and {2}"), Is.EqualTo(new[] { 0, 1, 2 }), "a formatted placeholder counts by its number");
        }

        [Test]
        public void AnEmptyTextIsNamed()
        {
            var problems = LanguageCheck.EmptyTexts(Make("ja-JP", "{\n  \"a\": \"{0} {1}\",\n  \"b\": \"  \"\n}"));
            Assert.That(problems.Single(), Does.Contain("ja-JP").And.Contain("\"b\"").And.Contain("line 3").And.Contain("Write its translation"));
        }

        [Test]
        public void AStrayBraceIsNamed()
        {
            var english = TestEnglish();
            Assert.That(LanguageCheck.Braces(english, Make("ja-JP", "{ \"a\": \"{{{1}}} に {0} を移動\", \"b\": \"ビー\" }")), Is.Empty, "{{ and }} show a brace and are fine");
            foreach (var broken in new[] { "「{0}」を{1}へ{", "{{0} を {1} へ", "{0} を {1} へ}", "{0} を {1 へ" })
            {
                var problems = LanguageCheck.Braces(english, Make("ja-JP", "{ \"a\": \"" + broken + "\" }"));
                Assert.That(problems.Count, Is.EqualTo(1), broken + " cannot be filled in, so it must be caught");
                Assert.That(problems[0], Does.Contain("ja-JP").And.Contain("\"a\"").And.Contain("{{").And.Contain("}}"));
            }
            var plain = LanguageCheck.Braces(english, Make("ja-JP", "{ \"b\": \"ビー {\" }"));
            Assert.That(plain.Single(), Does.Contain("\"b\"").And.Contain("brace"), "a text English shows as it is gets no brace English does not have");
            Assert.That(LanguageCheck.All(english, Make("ja-JP", "{ \"a\": \"{0}{1}{\" }")), Has.Some.Contains("{{"), "the save check runs it too");
        }

        [Test]
        public void AnUnclosedTagIsNamed()
        {
            Assert.That(LanguageCheck.Tags(Make("ja-JP", "{ \"a\": \"<b>{0}</b> を <i>{1}</i> に <color=#E0A83A>移動</color>\" }")), Is.Empty);
            var italic = LanguageCheck.Tags(Make("ja-JP", "{ \"a\": \"<b>{0}</b> を <i>{1} に\" }"));
            Assert.That(italic.Single(), Does.Contain("ja-JP").And.Contain("\"a\"").And.Contain("<i>").And.Contain("</i>"));
            var colour = LanguageCheck.Tags(Make("ja-JP", "{ \"c\": \"<color=red>シー {0}\" }"));
            Assert.That(colour.Single(), Does.Contain("\"c\"").And.Contain("</color>"));
            var bold = LanguageCheck.Tags(Make("ja-JP", "{ \"b\": \"ビー</b>\" }"));
            Assert.That(bold.Single(), Does.Contain("\"b\"").And.Contain("<b>"));
            Assert.That(LanguageCheck.All(TestEnglish(), Make("ja-JP", "{ \"b\": \"<b>ビー\" }")), Has.Some.Contains("</b>"), "the save check runs it too");
        }

        [Test]
        public void AKeyOutOfOrderIsNamedAndAMissingKeyIsNot()
        {
            var english = TestEnglish();
            var swapped = LanguageCheck.Order(english, Make("ja-JP", "{ \"a\": \"{0}{1}\", \"c\": \"シー {0}\", \"b\": \"ビー\" }"));
            Assert.That(swapped.Single(), Does.Contain("ja-JP").And.Contain("\"b\"").And.Contain("before \"c\"").And.Contain("same order"));
            Assert.That(LanguageCheck.Order(english, Make("ja-JP", "{ \"a\": \"{0}{1}\", \"c\": \"シー {0}\" }")), Is.Empty, "a missing key leaves the others in order");
        }

        [Test]
        public void AMissingKeyCountsOnlyForAShippedLanguage()
        {
            var english = TestEnglish();
            const string json = "{ \"a\": \"{1} に {0}\", \"c\": \"シー {0}\" }";
            var shipped = LanguageCheck.All(english, Make("ja-JP", json, shipped: true));
            Assert.That(shipped.Single(), Does.Contain("ja-JP").And.Contain("\"b\"").And.Contain("missing").And.Contain("every English key"));
            Assert.That(LanguageCheck.All(english, Make("ko-KR", json)), Is.Empty, "a language of your own may leave keys out; English shows for them");
        }

        [Test]
        public void ACountWithoutASingularIsNamed()
        {
            var english = Make("en-US", "{ \"a\": \"Frees {0} bits\", \"b\": \"{0} bits\", \"b.one\": \"{0} bit\", \"c\": \"{0} is on\", \"d\": \"Show {0} more\", \"e\": \"{0} value(s)\", \"f\": \"{1} of {0} slots\" }", shipped: true);
            var problems = CountProblems(english, new Dictionary<string, string> { { "f {0}", "a limit" }, { "gone {0}", "no longer in the table" } });

            Assert.That(problems.Count, Is.EqualTo(3), "\n" + string.Join("\n", problems));
            Assert.That(problems.Single(p => p.StartsWith("\"a\"")), Does.Contain("{0}").And.Contain("\"a.one\"").And.Contain("NotSingularCounts"));
            Assert.That(problems.Single(p => p.StartsWith("\"e\"")), Does.Contain("(s)"));
            Assert.That(problems.Single(p => p.Contains("gone {0}")), Does.Contain("Take it off"));
        }

        [Test]
        public void EveryWayTheCodeAsksForAKeyIsRead()
        {
            var source = "var a = L.Text(\"ad.ui.first\");\n"
                + "var b = UiText.Count(n, \"ad.ui.second.one\", \"ad.ui.second\");\n"
                + "var c = string.Format(L.Text(UiText.CountKey(Mathf.Max(n, 0), \"ad.ui.third\")), n);\n"
                + "AddAction(bar, \"ad-fourth\", \"ad.ui.fourth\", \"ad.ui.fourth.tip\", run);\n"
                + "if (key.StartsWith(\"ad.error.\")) return;";

            Assert.That(KeysAskedFor(source), Is.EquivalentTo(new[]
            {
                ("ad.ui.first", 1), ("ad.ui.second.one", 2), ("ad.ui.second", 2), ("ad.ui.third", 3), ("ad.ui.third.one", 3),
                ("ad.ui.fourth", 4), ("ad.ui.fourth.tip", 4),
            }), "every key literal, whoever takes it, and the singular a count key picks; a prefix is no key");
        }
    }
}
#endif
