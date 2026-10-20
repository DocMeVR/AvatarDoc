#if DOCME_NDMF
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The guard on every language file in the project (AvatarDoctor.&lt;code&gt;.po, shipped or a contributor's own),
    /// and proof that each check catches its mistake and says in plain words which language, which key and what to
    /// fix. The proofs read files built in memory; the drop-in tests write a file into a temporary folder under Assets
    /// that they delete again, and TheShippedTranslationsFollowEnglish rewrites a shipped translation only when English
    /// changed under it.
    /// </summary>
    public class LocalizationFileTests
    {
        static LanguageFile Make(string code, string po, bool shipped = false)
            => LanguageFile.Read("Test/AvatarDoctor." + code + ".po", code, shipped, Encoding.UTF8.GetBytes(po));

        /// <summary>A .po text with one entry for each key and text and no header: entry n starts on line 3n + 1.</summary>
        static string Po(params (string key, string text)[] entries)
            => string.Concat(entries.Select(e => "msgid \"" + PoFile.Escape(e.key) + "\"\nmsgstr \"" + PoFile.Escape(e.text) + "\"\n\n"));

        static LanguageFile TestEnglish() => Make("en-US", Po(("a", "Move {0} to {1}."), ("b", "Bee"), ("c", "Sea {0}")), shipped: true);

        static LanguageFile RealEnglish()
        {
            var english = LanguageTables.ReadEnglish();
            Assert.That(english, Is.Not.Null, "AvatarDoctor.en-US.po must sit in the package's Editor/Localization folder");
            Assert.That(english.Error, Is.Null, "AvatarDoctor.en-US.po cannot be read: " + english.Error);
            return english;
        }

        static List<LanguageFile> RealFiles()
        {
            var files = LanguageTables.Files().Select(LanguageTables.Read).ToList();
            Assert.That(files.Any(f => f.Code == LanguageTables.English && f.Shipped), Is.True, "the shipped English file must be among the language files found");
            return files;
        }

        static void AssertNone(List<string> problems) => Assert.That(problems, Is.Empty, "\n" + string.Join("\n", problems));

        /// <summary>The texts of a shipped language file by key, read straight from the file: the first of a key written twice, and no old entry.</summary>
        public static Dictionary<string, string> ShippedTexts(string language)
        {
            var path = L.TablePath(language);
            Assert.That(path, Is.Not.Null, "AvatarDoc ships a " + language + " language file");
            var file = PoFile.Parse(File.ReadAllText(Path.GetFullPath(path)));
            Assert.That(file.Problems, Is.Empty, path + " cannot be read: " + string.Join(" ", file.Problems));
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in file.Entries)
                if (!entry.obsolete && !texts.ContainsKey(entry.key)) texts[entry.key] = entry.text;
            return texts;
        }

        // ---- the guard on every real file ---------------------------------------------------------------------

        [Test]
        public void EveryLanguageFileIsValidPo()
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
            AssertNone(RealFiles().Where(f => f.Error == null).SelectMany(f => LanguageCheck.UnknownKeys(english, f).Concat(LanguageCheck.Obsolete(f))).ToList());
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
        public void NoShippedLanguageHasAnOutdatedText()
        {
            var english = RealEnglish();
            AssertNone(RealFiles().Where(f => f.Error == null && f.Shipped).SelectMany(f => LanguageCheck.Outdated(english, f)).ToList());
        }

        [Test]
        public void TheShippedTranslationsFollowEnglish()
        {
            var english = PoFile.Parse(File.ReadAllText(Path.GetFullPath(L.TablePath(LanguageTables.English))));
            Assert.That(english.Problems, Is.Empty, "the English file must read before the translations can follow it");
            var rewritten = LanguageTables.Files().Where(f => f.Shipped && f.Code != LanguageTables.English)
                .Where(f => LanguageFileUpdater.Update(f.Path, f.Code, english)).Select(f => f.Path).ToList();
            Assert.That(rewritten, Is.Empty, "These files were out of step with English and were rewritten: " + string.Join(", ", rewritten)
                + ". A new key has an empty text, a translation whose English was reworded is marked #, fuzzy with the old English under #. was:, a key English dropped"
                + " moved to the end as an old entry (#~), and a #. was: line left after its translation was checked is gone. The rewrite also puts the whole file in"
                + " AvatarDoc's own layout (one line per text, Unix line ends), so a file saved by Poedit with line wrapping shows more changed lines than the update"
                + " itself; turn off line wrapping in Poedit to avoid that. Look at the diff, translate what it marks, then run the tests again.");
        }

        /// <summary>What a .po text holds, without its layout: the header fields and every entry with its comments, flags and texts.</summary>
        static string ContentOf(string text)
        {
            var file = PoFile.Parse(text);
            var sb = new StringBuilder();
            foreach (var comment in file.HeaderComments) sb.Append("above the header ").Append(comment).Append('\n');
            foreach (var (name, value) in file.Header) sb.Append("header ").Append(name).Append(": ").Append(value).Append('\n');
            foreach (var e in file.Entries)
                sb.Append(e.obsolete ? "old " : "").Append(e.key).Append(" = ").Append(e.text).Append(" | en ").Append(e.english ?? "(none)").Append(" | was ").Append(e.previousEnglish ?? "(none)")
                    .Append(e.fuzzy ? " | fuzzy" : "").Append(" | ").Append(string.Join(" ", e.comments)).Append('\n');
            return sb.ToString();
        }

        [Test]
        public void TheShippedFilesReadAndWriteBackUnchanged()
        {
            var englishTexts = ShippedTexts(LanguageTables.English);
            var shipped = LanguageTables.Files().Where(f => f.Shipped).ToList();
            Assert.That(shipped.Select(f => f.Code), Is.SupersetOf(new[] { "en-US", "ja-JP", "zh-Hans" }), "AvatarDoc ships at least English, Japanese and Simplified Chinese");
            foreach (var found in shipped)
            {
                var text = File.ReadAllText(Path.GetFullPath(found.Path));
                var file = PoFile.Parse(text);
                Assert.That(file.Problems, Is.Empty, found.Path);
                Assert.That(ContentOf(file.Write()), Is.EqualTo(ContentOf(text)), found.Path + " written back holds the same header, texts, comments and marks, so the updater"
                    + " changes only what English changed. The layout is not compared: a file saved by Poedit with wrapped lines, or with Windows line ends, passes.");
                Assert.That(file.HeaderValue(PoFile.LanguageField), Is.EqualTo(found.Code), found.Path + " names its language in its header");
                Assert.That(file.HeaderValue("Content-Type"), Is.EqualTo("text/plain; charset=UTF-8"), found.Path + " says it is UTF-8");
                Assert.That(file.Entries.Select(e => e.key).ToList(), Is.EqualTo(englishTexts.Keys.ToList()), found.Path + " has the English keys in the English order");
                Assert.That(file.Entries.Where(e => e.fuzzy || e.obsolete).Select(e => e.key), Is.Empty, found.Path + " has no outdated and no old entry");
                foreach (var entry in file.Entries)
                {
                    if (found.Code == LanguageTables.English)
                        Assert.That(entry.english, Is.Null, "the English file needs no #. en: line (" + entry.key + ")");
                    else
                        Assert.That(entry.english, Is.EqualTo(LanguageFileUpdater.Source(englishTexts[entry.key])), found.Path + ": " + entry.key + " names the English it was made from");
                }
            }
        }

        [Test]
        public void EveryKeyTheCodeNamesIsInEnglish()
        {
            var english = new HashSet<string>(RealEnglish().Entries.Select(e => e.Key));
            var missing = L.KnownKeys.Concat(L.UiKeys).Where(k => !english.Contains(k)).ToList();
            Assert.That(missing, Is.Empty, "these keys are listed in L.cs but AvatarDoctor.en-US.po has no text for them: " + string.Join(", ", missing)
                + ". Add a text for each to AvatarDoctor.en-US.po, or remove it from L.cs.");
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
                        problems.Add(where + " asks for \"" + key + "\", but AvatarDoctor.en-US.po has no text for it, so the creator would see <" + key + ">. Add the text to all three files and the key to L.UiKeys or L.KnownKeys.");
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
            Assert.That(LanguageTables.CodeFromFileName("AvatarDoctor.en-US.po"), Is.EqualTo("en-US"));
            Assert.That(LanguageTables.CodeFromFileName("AvatarDoctor.ja-JP.po"), Is.EqualTo("ja-JP"));
            Assert.That(LanguageTables.CodeFromFileName("AvatarDoctor.zh-Hans.po"), Is.EqualTo("zh-Hans"));
            Assert.That(LanguageTables.CodeFromFileName("AvatarDoctor.zh-hans.po"), Is.EqualTo("zh-Hans"), "a code in the wrong case is still that language");
            Assert.That(LanguageTables.CodeFromFileName("AvatarDoctor.ko-KR.po"), Is.EqualTo("ko-KR"), "a language AvatarDoc does not ship is found the same way");
            foreach (var name in new[] { "AvatarDoctor.po", "AvatarDoctor.ja-JP.txt", "Other.ja-JP.po", "AvatarDoctor.ja-JP.po.bak", "AvatarDoctor.japanese.po", "AvatarDoctor..po", "AvatarDoctor.ja-JP.json", null })
                Assert.That(LanguageTables.CodeFromFileName(name), Is.Null, (name ?? "null") + " is not a language file");
            Assert.That(LanguageTables.IsLanguageFile("AvatarDoctor.xx-YY.po"), Is.True, "shaped like a language file");
            Assert.That(LanguageTables.CodeFromFileName("AvatarDoctor.xx-YY.po"), Is.Null, "but xx-YY names no language, so it is not used (and saving it says so in the Console)");
            Assert.That(LanguageTables.IsLanguageFile("AvatarDoctor.ja-JP.json"), Is.False, "a file in the old JSON format is no language file any more");
            Assert.That(LanguageTables.IsOldLanguageFile("Assets/Mine/AvatarDoctor.ja-JP.json"), Is.True, "but it is known, so a warning can name it");
            Assert.That(LanguageTables.IsOldLanguageFile("AvatarDoctor.ja-JP.po"), Is.False);
        }

        [Test]
        public void TheShippedEnglishFileIsFound()
        {
            var shippedEnglish = LanguageTables.Files().Where(f => f.Code == LanguageTables.English && f.Shipped).ToList();
            Assert.That(shippedEnglish.Count, Is.EqualTo(1), "exactly one English file ships with the package");
            Assert.That(shippedEnglish[0].Path, Is.EqualTo(L.TablePath("en-US")));
            Assert.That(shippedEnglish[0].Path, Does.StartWith(LanguageTables.PackageRoot() + "/Editor/Localization/"));
            Assert.That(shippedEnglish[0].Path, Does.EndWith("/AvatarDoctor.en-US.po"));
            Assert.That(AssetDatabase.GUIDToAssetPath(LanguageTables.EnglishGuid), Is.EqualTo(shippedEnglish[0].Path), "EnglishGuid is the English file's asset id");
            Assert.That(LanguageTables.Codes.First(), Is.EqualTo(LanguageTables.English), "English is listed first");
            Assert.That(LanguageTables.Files().First().Shipped, Is.True, "the shipped files come before anyone's own");
        }

        [Test]
        public void AFileOfYourOwnWinsOverTheShippedOneKeyByKey()
        {
            var shipped = Make("ja-JP", Po(("a", "A shipped"), ("b", "B shipped")), shipped: true);
            var own = Make("ja-JP", Po(("b", "B own"), ("c", "")));
            var broken = Make("ja-JP", "msgid \"a\"\nmsgstr A broken\n");
            var table = LanguageTables.Merge(new[] { shipped, own, broken });
            Assert.That(table["a"], Is.EqualTo("A shipped"), "a key only the shipped file has stays; the unreadable file is left out");
            Assert.That(table["b"], Is.EqualTo("B own"), "your own file wins");
            Assert.That(table.ContainsKey("c"), Is.False, "an empty text is no text, so English shows");
        }

        [Test]
        public void AFileOfYourOwnUnderAssetsIsFoundAndShown()
        {
            var folder = TempAssetFolder.Create("LanguageDropIn");
            var path = folder + "/AvatarDoctor.ko-KR.po";
            try
            {
                File.WriteAllText(Path.GetFullPath(path),
                    "msgid \"\"\nmsgstr \"\"\n\"Language: ko-KR\\n\"\n\"Content-Type: text/plain; charset=UTF-8\\n\"\n\n"
                    + "#. en: Simple\nmsgid \"ad.ui.mode.simple\"\nmsgstr \"간단\"\n\n"
                    + "#. en: Advanced\n#. was: More\n#, fuzzy\nmsgid \"ad.ui.mode.advanced\"\nmsgstr \"고급\"\n\n"
                    + "#~ msgid \"ad.ui.gone\"\n#~ msgstr \"사라짐\"\n", new UTF8Encoding(false));
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
                    Assert.That(L.Text("ad.ui.mode.advanced"), Is.EqualTo(LanguageTables.Text(LanguageTables.English, "ad.ui.mode.advanced")), "an outdated text counts as missing, so English shows");
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
        public void AnOldJsonLanguageFileGetsOneWarningAndIsNotRead()
        {
            var folder = TempAssetFolder.Create("LanguageJson");
            var path = folder + "/AvatarDoctor.ko-KR.json";
            var warnings = new List<string>();
            void Listen(string message, string stack, LogType type)
            {
                if (type == LogType.Warning && message.Contains(path)) warnings.Add(message);
            }
            try
            {
                File.WriteAllText(Path.GetFullPath(path), "{\n  \"ad.ui.mode.simple\": \"간단\"\n}\n", new UTF8Encoding(false));
                AssetDatabase.ImportAsset(path);
                Application.logMessageReceived += Listen;
                LanguageTables.Reload();
                var files = LanguageTables.Files();
                LanguageTables.Reload();
                LanguageTables.Files();
                Application.logMessageReceived -= Listen;

                Assert.That(files.Any(f => f.Path == path), Is.False, "a file in the old JSON format is not read");
                Assert.That(warnings.Count, Is.EqualTo(1), "one warning, however often the languages are read again");
                Assert.That(warnings[0], Is.EqualTo(LanguageTables.OldFileWarning(path)));
                Assert.That(warnings[0], Does.Contain("JSON").And.Contain("AvatarDoctor.ko-KR.po").And.Contain("AvatarDoctor.en-US.po").And.Contain("msgstr"),
                    "it says what to do: make a .po file of the same name from the English one");
                if (LanguageTables.Files().All(f => f.Code != "ko-KR"))
                    using (L.UseTestLanguage("ko-KR", null))
                        Assert.That(L.Text("ad.ui.mode.simple"), Is.EqualTo(LanguageTables.Text(LanguageTables.English, "ad.ui.mode.simple")), "its texts do not show");
            }
            finally
            {
                Application.logMessageReceived -= Listen;
                AssetDatabase.DeleteAsset(path);
                TempAssetFolder.Delete(folder);
                LanguageTables.Reload();
                LanguageTables.Files();
            }
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
        public void BrokenPoIsNamedWithItsLine()
        {
            var file = Make("ja-JP", "msgid \"a\"\nmsgstr \"A\"\n\nmsgid \"b\"\nmsgstr B\n");
            Assert.That(file.Error, Is.Not.Null);
            var problems = LanguageCheck.Format(file);
            Assert.That(problems.Count, Is.EqualTo(1));
            Assert.That(problems[0], Does.Contain("ja-JP").And.Contain("line 5").And.Contain("double quotes").And.Contain("AvatarDoctor.en-US.po"));
        }

        [Test]
        public void OnlyWellFormedPoIsRead()
        {
            var notPo = new Dictionary<string, string>
            {
                { "JSON", "{ \"a\": \"A\" }" },
                { "a text without quotes", "msgid \"a\"\nmsgstr A\n" },
                { "a key without quotes", "msgid a\nmsgstr \"A\"\n" },
                { "a key without its text", "msgid \"a\"\n\nmsgid \"b\"\nmsgstr \"B\"\n" },
                { "a text that never closes", "msgid \"a\"\nmsgstr \"A\n" },
                { "a stray line", "msgid \"a\"\nmsgstr \"A\"\nhello\n" },
                { "a text line with nothing before it", "\"A\"\n" },
                { "a text without its key", "msgstr \"A\"\n" },
                { "an unknown escape", "msgid \"a\"\nmsgstr \"A\\qB\"\n" },
                { "a double quote inside a text", "msgid \"a\"\nmsgstr \"say \"hi\" now\"\n" },
                { "a lone backslash before the closing quote", "msgid \"a\"\nmsgstr \"abc\\\"\n" },
                { "two texts on one line", "msgid \"a\"\nmsgstr \"x\" \"y\"\n" },
            };
            foreach (var pair in notPo)
            {
                var file = Make("ja-JP", pair.Value);
                Assert.That(file.Error, Is.Not.Null, pair.Key + " is not a .po entry and must not load");
                Assert.That(file.Entries, Is.Empty, pair.Key + ": a file that cannot be read gives no texts at all");
            }
            Assert.That(Make("ja-JP", notPo["an unknown escape"]).Error, Does.Contain("line 2").And.Contain("\\q").And.Contain("\\\\"), "it names the line and the escapes that work");
            Assert.That(Make("ja-JP", notPo["a double quote inside a text"]).Error, Does.Contain("line 2").And.Contain("\\\""));
            Assert.That(Make("ja-JP", "msgid \"a\"\nmsgstr \"\\\\ \\\" \\n \\t \\r\"\n").Error, Is.Null, "the escapes \\\\, \\\", \\n, \\t and \\r are read");
            Assert.That(Make("ja-JP", "").Error, Is.Null, "an empty file is a language with no texts yet");
            Assert.That(Make("ja-JP", "msgid \"\"\nmsgstr \"Language: ja-JP\\n\"\n").Error, Is.Null, "and so is one with only its header");
        }

        [Test]
        public void Utf8WithOrWithoutAByteOrderMarkReads()
        {
            var po = "msgid \"a\"\nmsgstr \"「{0}」を表示\"\n";
            var plain = Encoding.UTF8.GetBytes(po);
            var withMark = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(plain).ToArray();
            var windows = Encoding.UTF8.GetBytes(po.Replace("\n", "\r\n"));
            foreach (var bytes in new[] { plain, withMark, windows })
            {
                var file = LanguageFile.Read("Test/AvatarDoctor.ja-JP.po", "ja-JP", false, bytes);
                Assert.That(file.Error, Is.Null);
                Assert.That(file.Entries.Single().Text, Is.EqualTo("「{0}」を表示"), "with or without a byte order mark, with Windows or Unix line ends");
            }

            var utf16 = LanguageFile.Read("Test/AvatarDoctor.ja-JP.po", "ja-JP", false, Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(po)).ToArray());
            Assert.That(LanguageCheck.Format(utf16).Single(), Does.Contain("ja-JP").And.Contain("UTF-16").And.Contain("UTF-8"));

            var latin = LanguageFile.Read("Test/AvatarDoctor.ja-JP.po", "ja-JP", false, Encoding.ASCII.GetBytes("msgid \"a\"\nmsgstr \"").Concat(new byte[] { 0xE9, (byte)'"', (byte)'\n' }).ToArray());
            Assert.That(LanguageCheck.Format(latin).Single(), Does.Contain("not saved as UTF-8"));
        }

        [Test]
        public void EscapesReadAsTheirCharacters()
        {
            var file = Make("ja-JP", "msgid \"a\"\nmsgstr \"\\\"{0}\\\"\\nline\\t\\\\\"\n\nmsgid \"b\"\nmsgstr \"\"\n\"first \"\n\"second\"\n");
            Assert.That(file.Error, Is.Null);
            Assert.That(file.Entries[0].Text, Is.EqualTo("\"{0}\"\nline\t\\"));
            Assert.That(file.Entries[1].Text, Is.EqualTo("first second"), "a text may go on over several lines in quotes, as Poedit writes a long one");
        }

        [Test]
        public void AKeyWrittenTwiceIsNamedWithItsLines()
        {
            var file = Make("ja-JP", Po(("a", "A"), ("b", "B"), ("a", "A again")));
            var problems = LanguageCheck.DuplicateKeys(file);
            Assert.That(problems.Single(), Does.Contain("ja-JP").And.Contain("\"a\"").And.Contain("lines 1, 7").And.Contain("Keep one"));
            Assert.That(file.ToTable()["a"], Is.EqualTo("A"), "the loader keeps the first, as PoFile.Find does");
        }

        [Test]
        public void AKeyEnglishLacksIsNamed()
        {
            var problems = LanguageCheck.UnknownKeys(TestEnglish(), Make("ja-JP", Po(("a", "{0} {1}"), ("bee", "B"))));
            Assert.That(problems.Single(), Does.Contain("ja-JP").And.Contain("\"bee\"").And.Contain("not in English").And.Contain("spelling"));
        }

        [Test]
        public void AnOldEntryIsNamed()
        {
            var file = Make("ja-JP", Po(("a", "{1} に {0}")) + "#. en: Gone\n#~ msgid \"gone\"\n#~ msgstr \"消えた\"\n");
            Assert.That(file.Error, Is.Null);
            Assert.That(file.Entries.Select(e => e.Key), Is.EqualTo(new[] { "a" }), "an old entry is no text AvatarDoc shows");
            Assert.That(file.Obsolete.Single().Key, Is.EqualTo("gone"));
            Assert.That(file.Obsolete.Single().Text, Is.EqualTo("消えた"));
            Assert.That(LanguageCheck.UnknownKeys(TestEnglish(), file), Is.Empty, "an old entry is no unknown key: it says itself that English dropped it");
            var problems = LanguageCheck.Obsolete(file);
            Assert.That(problems.Single(), Does.Contain("ja-JP").And.Contain("\"gone\"").And.Contain("line 5").And.Contain("#~").And.Contain("Delete"));
            Assert.That(LanguageCheck.All(TestEnglish(), file), Has.Some.Contains("\"gone\""), "the save check names it too");
        }

        [Test]
        public void PlaceholdersMayMoveButNotChange()
        {
            var english = TestEnglish();
            Assert.That(LanguageCheck.Placeholders(english, Make("ja-JP", Po(("a", "{1} に {0} を移動")))), Is.Empty, "moving a placeholder within the sentence is fine");
            Assert.That(LanguageCheck.Placeholders(english, Make("ja-JP", Po(("a", "{0} と {0} を {1} に")))), Is.Empty, "using one twice is fine");
            var dropped = LanguageCheck.Placeholders(english, Make("ja-JP", Po(("a", "{0} を移動"))));
            Assert.That(dropped.Single(), Does.Contain("ja-JP").And.Contain("\"a\"").And.Contain("{0} {1}").And.Contain("move them"));
            var added = LanguageCheck.Placeholders(english, Make("ja-JP", Po(("b", "ビー {0}"))));
            Assert.That(added.Single(), Does.Contain("\"b\"").And.Contain("none").And.Contain("add none"));
            Assert.That(LanguageCheck.Placeholders("{0:0.0} and {1,5} and {2}"), Is.EqualTo(new[] { 0, 1, 2 }), "a formatted placeholder counts by its number");
        }

        [Test]
        public void AnEmptyTextIsNamed()
        {
            var problems = LanguageCheck.EmptyTexts(Make("ja-JP", Po(("a", "{0} {1}"), ("b", "  "))));
            Assert.That(problems.Single(), Does.Contain("ja-JP").And.Contain("\"b\"").And.Contain("line 4").And.Contain("Write its translation"));
        }

        [Test]
        public void AStrayBraceIsNamed()
        {
            var english = TestEnglish();
            Assert.That(LanguageCheck.Braces(english, Make("ja-JP", Po(("a", "{{{1}}} に {0} を移動"), ("b", "ビー")))), Is.Empty, "{{ and }} show a brace and are fine");
            foreach (var broken in new[] { "「{0}」を{1}へ{", "{{0} を {1} へ", "{0} を {1} へ}", "{0} を {1 へ" })
            {
                var problems = LanguageCheck.Braces(english, Make("ja-JP", Po(("a", broken))));
                Assert.That(problems.Count, Is.EqualTo(1), broken + " cannot be filled in, so it must be caught");
                Assert.That(problems[0], Does.Contain("ja-JP").And.Contain("\"a\"").And.Contain("{{").And.Contain("}}"));
            }
            var plain = LanguageCheck.Braces(english, Make("ja-JP", Po(("b", "ビー {"))));
            Assert.That(plain.Single(), Does.Contain("\"b\"").And.Contain("brace"), "a text English shows as it is gets no brace English does not have");
            Assert.That(LanguageCheck.All(english, Make("ja-JP", Po(("a", "{0}{1}{")))), Has.Some.Contains("{{"), "the save check runs it too");
        }

        [Test]
        public void AnUnclosedTagIsNamed()
        {
            Assert.That(LanguageCheck.Tags(Make("ja-JP", Po(("a", "<b>{0}</b> を <i>{1}</i> に <color=#E0A83A>移動</color>")))), Is.Empty);
            var italic = LanguageCheck.Tags(Make("ja-JP", Po(("a", "<b>{0}</b> を <i>{1} に"))));
            Assert.That(italic.Single(), Does.Contain("ja-JP").And.Contain("\"a\"").And.Contain("<i>").And.Contain("</i>"));
            var colour = LanguageCheck.Tags(Make("ja-JP", Po(("c", "<color=red>シー {0}"))));
            Assert.That(colour.Single(), Does.Contain("\"c\"").And.Contain("</color>"));
            var bold = LanguageCheck.Tags(Make("ja-JP", Po(("b", "ビー</b>"))));
            Assert.That(bold.Single(), Does.Contain("\"b\"").And.Contain("<b>"));
            Assert.That(LanguageCheck.All(TestEnglish(), Make("ja-JP", Po(("b", "<b>ビー")))), Has.Some.Contains("</b>"), "the save check runs it too");
        }

        [Test]
        public void AKeyOutOfOrderIsNamedAndAMissingKeyIsNot()
        {
            var english = TestEnglish();
            var swapped = LanguageCheck.Order(english, Make("ja-JP", Po(("a", "{0}{1}"), ("c", "シー {0}"), ("b", "ビー"))));
            Assert.That(swapped.Single(), Does.Contain("ja-JP").And.Contain("\"b\"").And.Contain("before \"c\"").And.Contain("same order"));
            Assert.That(LanguageCheck.Order(english, Make("ja-JP", Po(("a", "{0}{1}"), ("c", "シー {0}")))), Is.Empty, "a missing key leaves the others in order");
        }

        [Test]
        public void AMissingKeyCountsOnlyForAShippedLanguage()
        {
            var english = TestEnglish();
            var po = Po(("a", "{1} に {0}"), ("c", "シー {0}"));
            var shipped = LanguageCheck.All(english, Make("ja-JP", po, shipped: true));
            Assert.That(shipped.Single(), Does.Contain("ja-JP").And.Contain("\"b\"").And.Contain("missing").And.Contain("every English key"));
            Assert.That(LanguageCheck.All(english, Make("ko-KR", po)), Is.Empty, "a language of your own may leave keys out; English shows for them");
        }

        [Test]
        public void AnOutdatedTextFailsTheShippedGuard()
        {
            var english = TestEnglish();
            var po = "#. en: Move {0} to {1}.\nmsgid \"a\"\nmsgstr \"{1} に {0}\"\n\n"
                + "#. en: Bee\n#. was: B\n#, fuzzy\nmsgid \"b\"\nmsgstr \"ビー\"\n\n"
                + "#. en: Sea\nmsgid \"c\"\nmsgstr \"シー {0}\"\n";
            var shipped = Make("ja-JP", po, shipped: true);
            Assert.That(shipped.Entries.Single(e => e.Key == "b").Fuzzy, Is.True);
            Assert.That(shipped.ToTable().ContainsKey("b"), Is.False, "an outdated text counts as missing, so English shows in its place");

            var problems = LanguageCheck.Outdated(english, shipped);
            Assert.That(problems.Count, Is.EqualTo(2), "\n" + string.Join("\n", problems));
            Assert.That(problems.Single(p => p.Contains("\"b\"")), Does.Contain("ja-JP").And.Contain("line 8").And.Contain("#, fuzzy").And.Contain("\"B\"").And.Contain("\"Bee\""));
            Assert.That(problems.Single(p => p.Contains("\"c\"")), Does.Contain("\"Sea\"").And.Contain("\"Sea {0}\"").And.Contain("#. en:"),
                "a translation made from an older English is named even before it is marked");
            Assert.That(LanguageCheck.All(english, shipped).Count(p => p.Contains("#, fuzzy") || p.Contains("#. en:")), Is.EqualTo(2), "the save check names them");
            Assert.That(LanguageCheck.All(english, Make("ko-KR", po)), Has.Some.Contains("\"b\""), "and names them in a file of your own too");
            Assert.That(LanguageCheck.Outdated(english, Make("ja-JP", Po(("a", "{1} に {0}")), shipped: true)), Is.Empty, "a file that does not say its English has nothing to compare");
        }

        [Test]
        public void ARewordedEnglishTextMakesTheTranslationOutdatedAndShowsEnglish()
        {
            var english = new PoFile();
            english.Add(new PoFile.Entry { key = "ad.ui.mode.simple", text = "Simple view" });
            english.Add(new PoFile.Entry { key = "ad.ui.mode.advanced", text = "Advanced" });
            var ja = PoFile.Parse("#. en: Simple\nmsgid \"ad.ui.mode.simple\"\nmsgstr \"シンプル\"\n\n#. en: Advanced\nmsgid \"ad.ui.mode.advanced\"\nmsgstr \"詳細\"\n");

            LanguageFileUpdater.UpdateTranslation(ja, "ja-JP", LanguageFileUpdater.EnglishKeys(english));
            var simple = ja.Find("ad.ui.mode.simple");
            Assert.That(simple.fuzzy, Is.True, "the English it was made from was reworded");
            Assert.That(simple.english, Is.EqualTo("Simple view"));
            Assert.That(simple.previousEnglish, Is.EqualTo("Simple"));
            Assert.That(simple.text, Is.EqualTo("シンプル"), "the translation stays for the translator to check");
            Assert.That(ja.Find("ad.ui.mode.advanced").fuzzy, Is.False, "an English that did not change leaves its translation alone");
            Assert.That(ja.HeaderValue(PoFile.LanguageField), Is.EqualTo("ja-JP"));
            Assert.That(ja.HeaderValue("Content-Type"), Is.EqualTo("text/plain; charset=UTF-8"));

            var written = ja.Write();
            Assert.That(written, Does.Contain("#. en: Simple view\n#. was: Simple\n#, fuzzy\nmsgid \"ad.ui.mode.simple\"\nmsgstr \"シンプル\"\n"), "the change shows in the file's diff");
            var file = LanguageFile.Read("Test/AvatarDoctor.ja-JP.po", "ja-JP", true, Encoding.UTF8.GetBytes(written));
            Assert.That(file.Error, Is.Null);
            Assert.That(file.ToTable().ContainsKey("ad.ui.mode.simple"), Is.False, "an outdated translation counts as missing");
            using (L.UseTestLanguage("ja-JP", file.ToTable()))
            {
                Assert.That(L.Text("ad.ui.mode.simple"), Is.EqualTo(LanguageTables.Text(LanguageTables.English, "ad.ui.mode.simple")), "so English shows until it is translated again");
                Assert.That(L.Text("ad.ui.mode.advanced"), Is.EqualTo("詳細"));
            }
            var problem = LanguageCheck.Outdated(Make("en-US", english.Write(), shipped: true), file).Single();
            Assert.That(problem, Does.Contain("ja-JP").And.Contain("\"ad.ui.mode.simple\"").And.Contain("\"Simple view\"").And.Contain("#, fuzzy"));

            LanguageFileUpdater.UpdateTranslation(ja, "ja-JP", LanguageFileUpdater.EnglishKeys(english));
            Assert.That(ja.Write(), Is.EqualTo(written), "running it again changes nothing");
        }

        [Test]
        public void ANewKeyArrivesEmptyInEnglishOrderAndADroppedKeyMovesToTheEnd()
        {
            var ja = PoFile.Parse("#. en: Bee\nmsgid \"b\"\nmsgstr \"ビー\"\n\n#. en: Gone\nmsgid \"gone\"\nmsgstr \"消えた\"\n\n#. en: Sea {0}\nmsgid \"c\"\nmsgstr \"シー {0}\"\n");
            var wanted = new List<(string key, string english)> { ("a", "Move {0} to {1}."), ("b", "Bee"), ("c", "Sea {0}") };
            LanguageFileUpdater.UpdateTranslation(ja, "ja-JP", wanted);

            Assert.That(ja.Entries.Select(e => e.key), Is.EqualTo(new[] { "a", "b", "c", "gone" }), "English order, then the dropped key at the end");
            Assert.That(ja.Find("a").text, Is.Empty, "a new key has no translation yet, so English shows for it");
            Assert.That(ja.Find("a").english, Is.EqualTo("Move {0} to {1}."), "and it names its English for the translator");
            Assert.That(ja.Find("a").fuzzy, Is.False);
            var gone = ja.Entries.Last();
            Assert.That(gone.obsolete, Is.True);
            Assert.That(gone.text, Is.EqualTo("消えた"), "a translation is never deleted");
            Assert.That(ja.Write(), Does.Contain("#. en: Gone\n#~ msgid \"gone\"\n#~ msgstr \"消えた\"\n"));

            LanguageFileUpdater.UpdateTranslation(ja, "ja-JP", wanted.Concat(new[] { ("gone", "Gone") }).ToList());
            Assert.That(ja.Text("gone"), Is.EqualTo("消えた"), "a key English has again gets its translation back");
            Assert.That(ja.Entries.Any(e => e.obsolete), Is.False);
        }

        [Test]
        public void TheUpdaterWritesAFileOnlyWhenEnglishChanged()
        {
            var folder = TempAssetFolder.Create("LanguageUpdater");
            var path = folder + "/Updater.ja-JP.po";
            try
            {
                var english = PoFile.Parse("msgid \"a\"\nmsgstr \"Apple\"\n\nmsgid \"b\"\nmsgstr \"Bee\"\n");
                var current = "msgid \"\"\nmsgstr \"\"\n\"Language: ja-JP\\n\"\n\"Content-Type: text/plain; charset=UTF-8\\n\"\n\n"
                    + "#. en: Apple\nmsgid \"a\"\nmsgstr \"りんご\"\n\n#. en: Bee\nmsgid \"b\"\nmsgstr \"ビー\"\n";
                File.WriteAllText(Path.GetFullPath(path), current, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(path);
                Assert.That(LanguageFileUpdater.Update(path, "ja-JP", english), Is.False, "a file in step with English is left as it is");
                Assert.That(File.ReadAllText(Path.GetFullPath(path)), Is.EqualTo(current));

                english.Find("b").text = "Honey bee";
                Assert.That(LanguageFileUpdater.Update(path, "ja-JP", english), Is.True, "a reworded English text rewrites the file");
                var written = File.ReadAllText(Path.GetFullPath(path));
                Assert.That(written, Does.Contain("#. en: Honey bee\n#. was: Bee\n#, fuzzy\nmsgid \"b\"\nmsgstr \"ビー\"\n"));
                Assert.That(written, Does.Not.Contain("\r"));
                Assert.That(LanguageFileUpdater.Update(path, "ja-JP", english), Is.False, "and only once");
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
                TempAssetFolder.Delete(folder);
            }
        }

        static void DeleteTemp(string path, string folder)
        {
            AssetDatabase.DeleteAsset(path);
            var full = Path.GetFullPath(path);
            if (File.Exists(full)) File.Delete(full);
            TempAssetFolder.Delete(folder);
        }

        [Test]
        public void TheUpdaterLeavesAFileItCannotReadUntouched()
        {
            var folder = TempAssetFolder.Create("LanguageUpdater");
            var path = folder + "/ShiftJis.ja-JP.po";
            var full = Path.GetFullPath(path);
            try
            {
                var english = PoFile.Parse("msgid \"a\"\nmsgstr \"Simple view\"\n");
                var shiftJis = Encoding.ASCII.GetBytes("msgid \"\"\r\nmsgstr \"\"\r\n\"Language: ja-JP\\n\"\r\n\r\n#. en: Simple\r\nmsgid \"a\"\r\nmsgstr \"")
                    .Concat(new byte[] { 0x83, 0x56, 0x83, 0x93, 0x83, 0x76, 0x83, 0x8B })
                    .Concat(Encoding.ASCII.GetBytes("\"\r\n")).ToArray();
                File.WriteAllBytes(full, shiftJis);
                Assert.That(LanguageFile.Read(path, "ja-JP", true, shiftJis).Error, Does.Contain("not saved as UTF-8"), "the file is Shift-JIS, as Notepad saves it on a Japanese Windows");
                Assert.That(LanguageFileUpdater.Update(path, "ja-JP", english), Is.False, "a file AvatarDoc cannot read is not rewritten, even when English changed");
                Assert.That(File.ReadAllBytes(full), Is.EqualTo(shiftJis), "its bytes are exactly as they were, so the translator's work is not lost");

                var broken = Encoding.UTF8.GetBytes("#. en: Simple\nmsgid \"a\"\nmsgstr シンプル\n");
                File.WriteAllBytes(full, broken);
                Assert.That(LanguageFileUpdater.Update(path, "ja-JP", english), Is.False, "nor is a file with a line AvatarDoc does not understand");
                Assert.That(File.ReadAllBytes(full), Is.EqualTo(broken));
            }
            finally
            {
                DeleteTemp(path, folder);
            }
        }

        [Test]
        public void TheUpdaterLeavesAFileInStepAloneWhateverItsLayout()
        {
            var folder = TempAssetFolder.Create("LanguageUpdater");
            var path = folder + "/Layout.ja-JP.po";
            var full = Path.GetFullPath(path);
            var english = PoFile.Parse("msgid \"a\"\nmsgstr \"A long English text that Poedit wraps over two lines because it is long.\"\n\nmsgid \"b\"\nmsgstr \"Bee\"\n");
            var poedit = "# Japanese translation of AvatarDoc.\nmsgid \"\"\nmsgstr \"\"\n\"Project-Id-Version: AvatarDoc\\n\"\n\"Language: ja-JP\\n\"\n"
                + "\"Content-Type: text/plain; charset=UTF-8\\n\"\n\"X-Generator: Poedit 3.4\\n\"\n\n"
                + "#. en: A long English text that Poedit wraps over two lines because it is long.\nmsgid \"a\"\nmsgstr \"\"\n\"とても長い日本語の文で、\"\n\"Poedit が折り返します。\"\n\n"
                + "#. en: Bee\nmsgid \"b\"\nmsgstr \"ビー\"\n";
            try
            {
                foreach (var (what, text) in new[] { ("a file Poedit saved with wrapped lines", poedit), ("the same file with Windows line ends", poedit.Replace("\n", "\r\n")) })
                {
                    File.WriteAllText(full, text, new UTF8Encoding(false));
                    Assert.That(LanguageFileUpdater.Update(path, "ja-JP", english), Is.False, what + " is in step with English, so it is left as it is");
                    Assert.That(File.ReadAllText(full), Is.EqualTo(text), what + " keeps its layout");
                    Assert.That(ContentOf(PoFile.Parse(text).Write()), Is.EqualTo(ContentOf(text)), what + " holds the same content written in AvatarDoc's layout, its first comment included");
                }

                english.Find("b").text = "Honey bee";
                Assert.That(LanguageFileUpdater.Update(path, "ja-JP", english), Is.True, "a reworded English text still rewrites it");
                var written = PoFile.Parse(File.ReadAllText(full));
                Assert.That(written.Text("a"), Is.EqualTo("とても長い日本語の文で、Poedit が折り返します。"), "with every text kept");
                Assert.That(written.Find("b").fuzzy, Is.True);
                Assert.That(written.HeaderValue("X-Generator"), Is.EqualTo("Poedit 3.4"), "and every header line");
                Assert.That(File.ReadAllText(full), Does.StartWith("# Japanese translation of AvatarDoc.\nmsgid \"\""), "and the comment above the header");
            }
            finally
            {
                DeleteTemp(path, folder);
            }
        }

        [Test]
        public void ALaterCopyOfAKeyMovesToTheEndWithItsText()
        {
            var ja = PoFile.Parse("#. en: Apple\nmsgid \"a\"\nmsgstr \"りんご\"\n\n#. en: Bee\nmsgid \"b\"\nmsgstr \"ビー\"\n\n#. en: Apple\nmsgid \"a\"\nmsgstr \"林檎\"\n");
            LanguageFileUpdater.UpdateTranslation(ja, "ja-JP", new List<(string key, string english)> { ("a", "Apple"), ("b", "Bee") });
            Assert.That(ja.Entries.Select(e => (e.key, e.obsolete)), Is.EqualTo(new[] { ("a", false), ("b", false), ("a", true) }), "the later copy moves to the end as an old entry");
            Assert.That(ja.Text("a"), Is.EqualTo("りんご"), "the first copy stays the text AvatarDoc shows");
            Assert.That(ja.Entries.Last().text, Is.EqualTo("林檎"), "and the later one keeps its text: a translation is never deleted");
            Assert.That(ja.Write(), Does.Contain("#~ msgid \"a\"\n#~ msgstr \"林檎\"\n"));
        }

        [Test]
        public void ASecondRewordingKeepsTheEnglishTheTranslationWasMadeFrom()
        {
            var ja = PoFile.Parse("#. en: Apple\nmsgid \"a\"\nmsgstr \"りんご\"\n");
            LanguageFileUpdater.UpdateTranslation(ja, "ja-JP", new List<(string key, string english)> { ("a", "An apple") });
            LanguageFileUpdater.UpdateTranslation(ja, "ja-JP", new List<(string key, string english)> { ("a", "One apple") });
            var a = ja.Find("a");
            Assert.That(a.fuzzy, Is.True);
            Assert.That(a.english, Is.EqualTo("One apple"), "#. en: follows English");
            Assert.That(a.previousEnglish, Is.EqualTo("Apple"), "#. was: keeps the English the translation was made from, not the one in between");
            Assert.That(a.text, Is.EqualTo("りんご"));
        }

        [Test]
        public void AWasLineLeftAfterTheCheckIsRemoved()
        {
            var ja = PoFile.Parse("#. en: An apple\n#. was: Apple\nmsgid \"a\"\nmsgstr \"一つのりんご\"\n");
            Assert.That(ja.Find("a").fuzzy, Is.False, "the translator checked it and deleted only the #, fuzzy line, as Poedit does");
            LanguageFileUpdater.UpdateTranslation(ja, "ja-JP", new List<(string key, string english)> { ("a", "An apple") });
            Assert.That(ja.Find("a").previousEnglish, Is.Null, "the update removes the left over #. was: line");
            Assert.That(ja.Write(), Does.Not.Contain("#. was:"));
            Assert.That(ja.Text("a"), Is.EqualTo("一つのりんご"), "and keeps the checked translation");
        }

        [Test]
        public void TheEnglishATranslationWasMadeFromIsKeptWithoutSpacesAtItsEnds()
        {
            var wanted = new List<(string key, string english)> { ("and", " and "), ("b", "Bee") };
            var ja = new PoFile();
            LanguageFileUpdater.UpdateTranslation(ja, "ja-JP", wanted);
            ja.Find("and").text = " かつ ";
            ja.Find("b").text = "ビー";
            var again = PoFile.Parse(ja.Write());
            Assert.That(again.Find("and").english, Is.EqualTo("and"), "a comment line keeps no spaces at its ends");
            LanguageFileUpdater.UpdateTranslation(again, "ja-JP", wanted);
            Assert.That(again.Find("and").fuzzy, Is.False, "so spaces at the ends of an English text never make its translation outdated");
            Assert.That(again.Text("and"), Is.EqualTo(" かつ "), "the text itself keeps its spaces: it stands in quotes");
            var english = Make("en-US", Po(("and", " and "), ("b", "Bee")), shipped: true);
            Assert.That(LanguageCheck.Outdated(english, Make("ja-JP", again.Write(), shipped: true)), Is.Empty);

            var own = PoFile.Parse("msgid \"b\"\nmsgstr \"ビー\"\n");
            LanguageFileUpdater.UpdateTranslation(own, "ko-KR", wanted);
            Assert.That(own.Find("b").fuzzy, Is.False, "a translation that never said its English takes today's without turning outdated");
            Assert.That(own.Find("b").english, Is.EqualTo("Bee"));
            Assert.That(own.Text("b"), Is.EqualTo("ビー"));
        }

        [Test]
        public void ACountWithoutASingularIsNamed()
        {
            var english = Make("en-US", Po(("a", "Frees {0} bits"), ("b", "{0} bits"), ("b.one", "{0} bit"), ("c", "{0} is on"), ("d", "Show {0} more"), ("e", "{0} value(s)"), ("f", "{1} of {0} slots")), shipped: true);
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
