#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.localization;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The language AvatarDoc shows: the one NDMF and Modular Avatar share, English for any key a language lacks, the
    /// picker beside the Simple and Advanced switch, and every open view and window following a change at once. Every
    /// language here is a test table (L.UseTestLanguage), never a file, and nothing reaches NDMF's saved choice.
    /// </summary>
    public class LanguageSwitchTests
    {
        const string NdmfLanguageKey = "nadena.dev.ndmf.language-selection";

        static readonly Dictionary<string, string> Japanese = new Dictionary<string, string>
        {
            { "ad.ui.mode.simple", "シンプル" },
            { "ad.ui.mode.advanced", "詳細" },
            { "ad.ui.mode.tip", "シンプルはよく使うものだけ、詳細はすべてのツールを表示します。" },
        };

        TestAvatar avatar;
        MemoryViewPrefs prefs;
        bool realHadKey;
        string realValue;
        string realLanguage;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
            TagFilterMemory.ClearAll();
            prefs = new MemoryViewPrefs();
            realHadKey = EditorPrefs.HasKey(NdmfLanguageKey);
            realValue = EditorPrefs.GetString(NdmfLanguageKey, "");
            realLanguage = LanguagePrefs.Language;
        }

        [TearDown]
        public void TearDown()
        {
            try { avatar.Dispose(); }
            finally
            {
                AvatarPlanCache.Invalidate();
                PreviewState.ClearAll();
                TagFilterMemory.ClearAll();
            }
        }

        /// <summary>Read only: NDMF's saved language and the editor's current one are exactly as the test found them. Called at the end of each test, since the runner does not count a failing TearDown.</summary>
        void AssertRealLanguageUntouched()
        {
            Assert.That(EditorPrefs.HasKey(NdmfLanguageKey), Is.EqualTo(realHadKey), "NDMF's saved language key was touched");
            Assert.That(EditorPrefs.GetString(NdmfLanguageKey, ""), Is.EqualTo(realValue), "NDMF's saved language was changed");
            Assert.That(LanguagePrefs.Language, Is.EqualTo(realLanguage), "the editor's shared language was changed");
        }

        static string English(string key) => LanguageTables.Text(LanguageTables.English, key);

        AvatarDoctorView View(AvatarDoc menu, bool simple = false)
        {
            if (simple) ViewModes.Save(prefs, ViewMode.Simple);
            return new AvatarDoctorView(new SerializedObject(menu), menu, false, prefs);
        }

        MenuEntry Toggle(AvatarDoc menu, string name)
        {
            var entry = menu.AddEntry(EntryKind.Toggle, name);
            entry.objects.Add(new ObjectTarget { target = avatar.Hat });
            return entry;
        }

        static void Pointer<T>(VisualElement target, EventType type) where T : PointerEventBase<T>, new()
        {
            using (var e = PointerEventBase<T>.GetPooled(new Event { type = type, button = 0, mousePosition = target.worldBound.center }))
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        static void Click(VisualElement target)
        {
            Pointer<PointerDownEvent>(target, EventType.MouseDown);
            Pointer<PointerUpEvent>(target, EventType.MouseUp);
        }

        /// <summary>
        /// A real press and release on a button that opens a menu. The panel does not always see a synthetic pointer as
        /// over the button, so Unity's Clickable may not run the click on the release; then the click is run the way
        /// MenuLifecycleTests does it.
        /// </summary>
        static AdMenu OpenByClick(Button button)
        {
            Click(button);
            if (button.panel.visualTree.Q<AdMenu>() == null) UiTestEvents.SimulateClick(button);
            return button.panel.visualTree.Q<AdMenu>();
        }

        static void AssertTextFits(TextElement element, string what)
        {
            var size = element.MeasureTextSize(element.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
            Assert.That(element.contentRect.width, Is.GreaterThanOrEqualTo(size.x - 0.5f),
                what + " \"" + element.text + "\" is cut short: " + element.contentRect.width.ToString("0.0") + " wide for text that measures " + size.x.ToString("0.0"));
        }

        static bool ShowsLabel(VisualElement root, string text) => root.Query<Label>().ToList().Any(l => l.text == text);

        // ---- the texts -----------------------------------------------------------------------------------------

        [Test]
        public void EnglishShowsForAKeyALanguageLacks()
        {
            var texts = new Dictionary<string, string>(Japanese) { ["ad.ui.mode.advanced"] = "   " };
            using (L.UseTestLanguage("ja-JP", texts))
            {
                Assert.That(L.Language, Is.EqualTo("ja-jp"));
                Assert.That(L.Text("ad.ui.mode.simple"), Is.EqualTo("シンプル"));
                Assert.That(L.Text("ad.ui.mode.advanced"), Is.EqualTo(English("ad.ui.mode.advanced")), "a blank text shows the English one");
                Assert.That(L.Text("ad.ui.unlock"), Is.EqualTo(English("ad.ui.unlock")), "a key the language lacks shows the English text, never the key");
                Assert.That(L.Localizer.TryGetLocalizedString("ad.ui.mode.simple", out var ndmfText), Is.True);
                Assert.That(ndmfText, Is.EqualTo("シンプル"), "NDMF's error report reads the same language");
                Assert.That(L.Localizer.TryGetLocalizedString("ad.ui.unlock", out var ndmfFallback), Is.True);
                Assert.That(ndmfFallback, Is.EqualTo(English("ad.ui.unlock")), "and the same English fallback");
            }
            Assert.That(L.Text("ad.ui.mode.simple"), Is.EqualTo(English("ad.ui.mode.simple")), "the test's language is gone with its scope");
            Assert.That(L.Text("ad.no.such.key"), Is.EqualTo("<ad.no.such.key>"), "a key English lacks too still shows as itself in brackets");
            AssertRealLanguageUntouched();
        }

        [Test]
        public void ARunCanBePinnedToEnglishWhateverTheEditorShows()
        {
            using (L.UseTestLanguage("ja-JP", Japanese))
            {
                using (L.UseTestLanguage("en-US", null))
                {
                    Assert.That(L.Language, Is.EqualTo("en-us"));
                    Assert.That(L.Text("ad.ui.mode.simple"), Is.EqualTo(English("ad.ui.mode.simple")), "with no texts of its own, the English file answers");
                    Assert.That(L.Text("ad.ui.mode.tip"), Is.EqualTo(English("ad.ui.mode.tip")));
                }
                Assert.That(L.Text("ad.ui.mode.simple"), Is.EqualTo("シンプル"), "the outer language comes back");
            }
            AssertRealLanguageUntouched();
        }

        [Test]
        public void AnotherFormOfTheLanguageComesBeforeEnglish()
        {
            using (L.UseTestLanguage("sr-Latn", new Dictionary<string, string> { { "ad.ui.mode.simple", "Jednostavno" } }))
            {
                L.SetLanguage("sr-Cyrl");
                Assert.That(L.Language, Is.EqualTo("sr-cyrl"));
                Assert.That(L.LookupOrder("sr-cyrl").ToList(), Is.EqualTo(new[] { "sr-Latn", LanguageTables.English }));
                Assert.That(L.Text("ad.ui.mode.simple"), Is.EqualTo("Jednostavno"), "the other script of the same language is read before English, as NDMF does");
                Assert.That(L.Text("ad.ui.mode.advanced"), Is.EqualTo(English("ad.ui.mode.advanced")));
            }
            Assert.That(L.LookupOrder("en-us").Last(), Is.EqualTo(LanguageTables.English), "English always comes last");
            AssertRealLanguageUntouched();
        }

        [Test]
        public void TwoScopesOnOneLanguageNest()
        {
            using (L.UseTestLanguage("ja-JP", new Dictionary<string, string> { { "ad.ui.mode.simple", "外" } }))
            {
                using (L.UseTestLanguage("ja-JP", new Dictionary<string, string> { { "ad.ui.mode.simple", "内" } }))
                    Assert.That(L.Text("ad.ui.mode.simple"), Is.EqualTo("内"));
                Assert.That(L.Text("ad.ui.mode.simple"), Is.EqualTo("外"), "the inner scope puts the outer one's table back");
            }
            AssertRealLanguageUntouched();
        }

        [Test]
        public void EveryLanguageFileIsOfferedToNdmf()
        {
            foreach (var file in LanguageTables.Files())
                Assert.That(LanguagePrefs.RegisteredLanguages, Does.Contain(file.Code.ToLowerInvariant()), file.Path + " must be in NDMF's language list, so its switcher and Modular Avatar's offer it");
        }

        // ---- the picker ----------------------------------------------------------------------------------------

        [Test]
        public void TheNamesAreEachLanguagesOwn()
        {
            Assert.That(LanguagePicker.UsesNdmfNames, Is.True, "the names come from NDMF's own list, the one its switcher shows");
            Assert.That(LanguagePicker.DisplayName("en-us", new[] { "en-us", "ja-jp" }), Is.EqualTo("English"));
            Assert.That(LanguagePicker.DisplayName("ja-jp", new[] { "en-us", "ja-jp" }), Is.EqualTo("日本語"));
            var simplified = LanguagePicker.DisplayName("zh-hans", new[] { "zh-hans", "zh-hant" });
            var traditional = LanguagePicker.DisplayName("zh-hant", new[] { "zh-hans", "zh-hant" });
            Assert.That(simplified, Does.Contain("中文"));
            Assert.That(simplified, Is.Not.EqualTo(traditional), "the two written forms of Chinese are told apart");
            var american = LanguagePicker.DisplayName("en-us", new[] { "en-us", "en-gb" });
            Assert.That(american, Does.StartWith("English").And.Not.EqualTo("English"), "a region is named when two choices share the language");

            var choices = LanguagePicker.Choices();
            Assert.That(choices, Does.Contain("en-us"));
            foreach (var code in LanguageTables.Codes) Assert.That(choices, Does.Contain(code.ToLowerInvariant()), "AvatarDoc's own languages are offered");
            foreach (var code in LanguagePrefs.RegisteredLanguages.Where(c => c.Contains("-")))
                Assert.That(choices, Does.Contain(code), "every language NDMF knows is offered, as NDMF's own switcher does");
            Assert.That(choices.Where(c => !c.Contains("-")).Where(c => choices.Any(o => o.StartsWith(c + "-"))), Is.Empty, "a plain code is left out when a fuller one is there");
        }

        [Test]
        public void ThePickerSitsBesideTheSwitchInBothModes()
        {
            var menu = avatar.AddMenu();
            Toggle(menu, "Hat");
            foreach (var simple in new[] { false, true })
            {
                var view = View(menu, simple);
                var row = view.ElementAt(0);
                Assert.That(row.name, Is.EqualTo("ad-mode-row"));
                Assert.That(row.Contains(view.LanguagePicker), Is.True, "the picker is in the switch's row (Simple " + simple + ")");
                Assert.That(row.IndexOf(view.LanguagePicker), Is.GreaterThan(row.IndexOf(view.ModeChoice)), "after the switch");
                Assert.That(view.LanguagePicker.tooltip, Is.EqualTo(L.Text("ad.ui.language.tip")));
                Assert.That(view.LanguagePicker.tooltip, Does.EndWith(".").And.Not.Contain(". "), "a one sentence tooltip");
                Assert.That(view.LanguagePicker.Label.text, Is.EqualTo(LanguagePicker.DisplayName(L.Language, LanguagePicker.Choices())));
                LayoutProbe.Measure(view, () =>
                {
                    var picker = view.LanguagePicker;
                    Assert.That(picker.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                    Assert.That(picker.worldBound.width, Is.GreaterThan(0f), "shown in Simple " + simple);
                    Assert.That(picker.worldBound.height, Is.EqualTo(view.ModeChoice.worldBound.height).Within(1f), "as tall as the switch");
                    Assert.That(picker.worldBound.center.y, Is.EqualTo(view.ModeChoice.worldBound.center.y).Within(1f), "on the switch's line");
                    Assert.That(picker.worldBound.xMax, Is.EqualTo(row.worldBound.xMax).Within(1f), "at the row's right end");
                    Assert.That(picker.worldBound.xMax, Is.LessThanOrEqualTo(view.worldBound.xMax + 0.5f), "inside the view");
                    Assert.That(picker.worldBound.x, Is.GreaterThanOrEqualTo(view.ModeChoice.worldBound.xMax), "beside the switch, not over it");
                }, 360f);
            }
            AssertRealLanguageUntouched();
        }

        [Test]
        public void ThePickerFitsAt300And360InEveryLanguageItOffers()
        {
            var menu = avatar.AddMenu();
            Toggle(menu, "Hat");
            using (L.UseTestLanguage("ja-JP", Japanese))
            {
                foreach (var width in new[] { 300f, 360f })
                {
                    var view = View(menu);
                    LayoutProbe.Measure(view, () =>
                    {
                        foreach (var code in LanguagePicker.Choices())
                        {
                            L.SetLanguage(code);
                            LayoutProbe.Validate(view);
                            var what = code + " at " + width + " px";
                            var row = view.ElementAt(0);
                            PanelAudit.Audit(row, view.worldBound.xMax, new PanelAudit.AllowList());
                            AssertTextFits(view.LanguagePicker.Label, "the picker's name in " + what);
                            foreach (var item in view.ModeChoice.Items) AssertTextFits(item, "the switch in " + what);
                            Assert.That(view.LanguagePicker.worldBound.xMax, Is.LessThanOrEqualTo(view.worldBound.xMax + 0.5f), "the picker in " + what);
                            Assert.That(view.ModeChoice.worldBound.width, Is.EqualTo(200f).Within(1f), "the Simple and Advanced pill still fills to its 200 px in " + what);
                        }
                    }, width);
                }
            }
            AssertRealLanguageUntouched();
        }

        [Test]
        public void ChoosingALanguageInThePickerSwitchesEveryOpenView()
        {
            var first = avatar.AddMenu();
            var otherObject = new GameObject("Second");
            otherObject.transform.SetParent(avatar.Root.transform);
            var second = avatar.AddMenu(otherObject);
            Toggle(first, "Hat");
            Toggle(second, "Glasses");
            AvatarDoctorView a, b;
            using (L.UseTestLanguage("ja-JP", Japanese))
            {
                L.SetLanguage("en-us");
                a = View(first);
                b = View(second);
                LayoutProbe.Measure(a, () => LayoutProbe.Measure(b, () =>
                {
                    Assert.That(a.ModeChoice.Items[0].text, Is.EqualTo(English("ad.ui.mode.simple")));
                    int aBuilds = a.Rerenders, bBuilds = b.Rerenders;

                    LayoutProbe.Validate(a);
                    var menu = OpenByClick(a.LanguagePicker);
                    Assert.That(menu, Is.Not.Null, "a click opens the language list");
                    var choices = LanguagePicker.Choices();
                    Assert.That(menu.Items.Select(i => i.Text), Is.EqualTo(choices.Select(c => LanguagePicker.DisplayName(c, choices))), "every language, each in its own name");
                    Assert.That(menu.Items.Where(i => i.Checked).Select(i => i.Text), Is.EqualTo(new[] { LanguagePicker.DisplayName("en-us", choices) }), "the current one is ticked");
                    int japanese = choices.IndexOf("ja-jp");
                    Assert.That(japanese, Is.GreaterThanOrEqualTo(0));

                    Pointer<PointerUpEvent>(menu.Rows[japanese], EventType.MouseUp);

                    Assert.That(menu.IsClosed, Is.True);
                    Assert.That(L.Language, Is.EqualTo("ja-jp"));
                    Assert.That(a.ModeChoice.Items[0].text, Is.EqualTo("シンプル"), "the view it was chosen in is shown in the language");
                    Assert.That(b.ModeChoice.Items[0].text, Is.EqualTo("シンプル"), "and the other open view follows at once");
                    Assert.That(b.ModeChoice.tooltip, Is.EqualTo(Japanese["ad.ui.mode.tip"]));
                    Assert.That(a.LanguagePicker.Label.text, Is.EqualTo(LanguagePicker.DisplayName("ja-jp", choices)));
                    Assert.That(a.ModeChoice.Items[1].text, Is.EqualTo("詳細"));
                    Assert.That(a.Q<Button>("ad-unlock").text, Is.EqualTo(English("ad.ui.unlock")), "what the language lacks stays English");
                    Assert.That(a.Rerenders, Is.EqualTo(aBuilds + 1));
                    Assert.That(b.Rerenders, Is.EqualTo(bBuilds + 1));

                    LayoutProbe.Validate(b);
                    var again = OpenByClick(b.LanguagePicker);
                    Assert.That(again, Is.Not.Null, "the rebuilt picker opens its list too");
                    Assert.That(again.Items.Where(i => i.Checked).Select(i => i.Text), Is.EqualTo(new[] { LanguagePicker.DisplayName("ja-jp", choices) }), "a menu opened later reads the new language");
                    again.Close();
                }));
            }
            Assert.That(a.ModeChoice.Items[0].text, Is.EqualTo("シンプル"), "a view off its panel is not rebuilt");
            AssertRealLanguageUntouched();
        }

        [Test]
        public void ALanguageChangeKeepsWhatTheCreatorWasLookingAt()
        {
            var menu = avatar.AddMenu();
            var hat = Toggle(menu, "Hat");
            Toggle(menu, "Glasses");
            var view = View(menu, simple: true);
            LayoutProbe.Measure(view, () =>
            {
                var search = view.Tree.Q<TextField>("ad-search");
                search.value = "Ha";
                view.Tree.SelectEntry(hat.id);
                Assert.That(view.Tree.Selected?.Entry?.id, Is.EqualTo(hat.id));
                var oldTree = view.Tree;

                using (L.UseTestLanguage("ja-JP", Japanese))
                {
                    Assert.That(view.Tree, Is.Not.SameAs(oldTree), "the parts were built again");
                    Assert.That(view.ModeChoice.Items[0].text, Is.EqualTo("シンプル"));
                    Assert.That(view.Mode, Is.EqualTo(ViewMode.Simple), "the mode stays");
                    Assert.That(view.ModeChoice.Index, Is.EqualTo(0));
                    Assert.That(view.Tree.Q<TextField>("ad-search").value, Is.EqualTo("Ha"), "the search stays");
                    Assert.That(view.Tree.Selected?.Entry?.id, Is.EqualTo(hat.id), "the selected row stays");
                    Assert.That(view.Detail.Row?.Entry?.id, Is.EqualTo(hat.id), "and its panel is open");

                    view.OpenSummary();
                    view.SetSummaryFormat(SummaryFormat.Markdown);
                    L.SetLanguage("en-us");
                    Assert.That(view.SummaryOpen, Is.True, "an open Summary stays open");
                    Assert.That(view.Summary.ClassListContains("ad-hidden"), Is.False);
                    Assert.That(view.Summary.Format, Is.EqualTo(SummaryFormat.Markdown), "in the format the creator chose");
                    Assert.That(view.Summary.Q<SegmentedChoice>("ad-summary-format").Index, Is.EqualTo((int)SummaryFormat.Markdown), "and its switch shows it");
                    Assert.That(view.SummaryText, Does.StartWith("## "), "the text is Markdown");
                }
            });
            AssertRealLanguageUntouched();
        }

        [Test]
        public void AViewOffItsPanelCatchesUpWhenShownAgain()
        {
            var menu = avatar.AddMenu();
            Toggle(menu, "Hat");
            int listeners = L.LanguageListenerCount;
            var view = View(menu);
            Assert.That(L.LanguageListenerCount, Is.EqualTo(listeners), "a view on no panel listens to nothing");
            using (L.UseTestLanguage("ja-JP", Japanese))
            {
                Assert.That(view.Rerenders, Is.EqualTo(0), "a view on no panel is not rebuilt");
                LayoutProbe.Measure(view, () =>
                {
                    Assert.That(L.LanguageListenerCount, Is.EqualTo(listeners + 1), "a view on a panel listens");
                    view.CheckVersion();
                    Assert.That(view.Rerenders, Is.EqualTo(1), "its first poll on the panel builds it in the language chosen meanwhile");
                    Assert.That(view.ModeChoice.Items[0].text, Is.EqualTo("シンプル"));
                    view.CheckVersion();
                    Assert.That(view.Rerenders, Is.EqualTo(1), "and only once");
                });
                Assert.That(L.LanguageListenerCount, Is.EqualTo(listeners), "a view lets go when it leaves its panel");
            }
            AssertRealLanguageUntouched();
        }

        /// <summary>
        /// NDMF calls every registered callback when its language changes, whether Modular Avatar's switcher, NDMF's or
        /// AvatarDoc's own changed it. Setting the real language would write EditorPrefs, so this calls NDMF's own
        /// private trigger instead, with the language left as it is.
        /// </summary>
        [Test]
        public void AChangeMadeInNdmfReachesEveryOpenView()
        {
            var trigger = typeof(LanguagePrefs).GetMethod("TriggerLanguageChangeCallbacks", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(trigger, Is.Not.Null, "NDMF's language change trigger was not found; this NDMF version needs a new look at how AvatarDoc hears a change");
            var menu = avatar.AddMenu();
            Toggle(menu, "Hat");
            var view = View(menu);
            LayoutProbe.Measure(view, () =>
            {
                int version = L.LanguageVersion;
                int builds = view.Rerenders;
                trigger.Invoke(null, null);
                Assert.That(L.LanguageVersion, Is.EqualTo(version + 1), "AvatarDoc heard NDMF's change");
                Assert.That(view.Rerenders, Is.EqualTo(builds + 1), "and the open view was built again");
            });
            AssertRealLanguageUntouched();
        }

        [Test]
        public void AFullDropTakesInALanguageChange()
        {
            var menu = avatar.AddMenu();
            Toggle(menu, "Hat");
            AvatarPlanCache.GetFor(menu);
            using (L.UseTestLanguage("ja-JP", Japanese)) { }
            AvatarPlanCache.Invalidate();
            var plan = AvatarPlanCache.GetFor(menu);
            Assert.That(AvatarPlanCache.LastInvalidationReason, Is.EqualTo("Invalidate"), "a plan built after a full drop is already in the current language, so it is not dropped a second time");
            Assert.That(AvatarPlanCache.GetFor(menu), Is.SameAs(plan));
            AssertRealLanguageUntouched();
        }

        [Test]
        public void APlanBuiltInAnotherLanguageIsBuiltAgain()
        {
            var menu = avatar.AddMenu();
            Toggle(menu, "Hat");
            var before = AvatarPlanCache.GetFor(menu);
            Assert.That(AvatarPlanCache.GetFor(menu), Is.SameAs(before), "a plan stands while nothing changes");
            using (L.UseTestLanguage("ja-JP", Japanese))
            {
                var during = AvatarPlanCache.GetFor(menu);
                Assert.That(during, Is.Not.SameAs(before), "a plan's texts were made in the old language, so it is built again");
                Assert.That(AvatarPlanCache.GetFor(menu), Is.SameAs(during), "once");
            }
            AssertRealLanguageUntouched();
        }

        [Test]
        public void ALanguageChangeRedrawsTheWindows()
        {
            var menu = avatar.AddMenu();
            var hat = Toggle(menu, "Hat");
            var piece = new GameObject("Piece");
            piece.transform.SetParent(avatar.Root.transform);
            var pieceMesh = piece.AddComponent<SkinnedMeshRenderer>();
            var pieceMeshAsset = TestAvatar.QuadWithShapes("Blush");
            pieceMesh.sharedMesh = pieceMeshAsset;
            var shapes = piece.AddComponent<AvatarDocShapes>();
            var texts = new Dictionary<string, string>
            {
                { "ad.ui.import", "変換" }, { "ad.ui.import.intro", "変換の説明" },
                { "ad.ui.outfit.title", "衣装セット" }, { "ad.ui.outfit.intro", "衣装の説明" },
                { "ad.ui.capture.title", "アイコン撮影" }, { "ad.ui.capture.background", "背景" },
                { "ad.ui.shapes.note", "シェイプの説明" }, { "ad.ui.welcome.quickstart", "はじめに" },
            };
            int listeners = L.LanguageListenerCount;
            var import = ScriptableObject.CreateInstance<ImportWindow>();
            var outfits = ScriptableObject.CreateInstance<OutfitSetWindow>();
            var capture = ScriptableObject.CreateInstance<IconCaptureWindow>();
            var quickStart = ScriptableObject.CreateInstance<QuickStartWindow>();
            UnityEditor.Editor editor = null;
            try
            {
                import.Init(menu);
                import.CreateGUI();
                outfits.Init(menu);
                outfits.CreateGUI();
                capture.Init(menu, hat.id);
                capture.rootVisualElement.Q<SegmentedChoice>("ad-capture-background").Pick(1);
                capture.rootVisualElement.Q<ColorField>().value = Color.red;
                quickStart.Init("Quick start text");
                editor = UnityEditor.Editor.CreateEditor(shapes);
                var shapesRoot = ((AvatarDocShapesEditor)editor).CreateInspectorGUI();
                Assert.That(ShowsLabel(import.rootVisualElement, English("ad.ui.import.intro")), Is.True);
                Assert.That(L.LanguageListenerCount, Is.EqualTo(listeners + 5), "four windows and the Shapes inspector listen while they are open");

                using (L.UseTestLanguage("ja-JP", texts))
                {
                    Assert.That(import.titleContent.text, Is.EqualTo("変換"));
                    Assert.That(ShowsLabel(import.rootVisualElement, "変換の説明"), Is.True, "the Convert window is built again");
                    Assert.That(outfits.titleContent.text, Is.EqualTo("衣装セット"));
                    Assert.That(ShowsLabel(outfits.rootVisualElement, "衣装の説明"), Is.True, "the outfit set window is built again");
                    Assert.That(capture.titleContent.text, Is.EqualTo("アイコン撮影"));
                    Assert.That(ShowsLabel(capture.rootVisualElement, "背景"), Is.True, "the icon capture window is built again");
                    Assert.That(capture.rootVisualElement.Q<SegmentedChoice>("ad-capture-background").Index, Is.EqualTo(1), "keeping the solid background chosen before");
                    var colour = capture.rootVisualElement.Q<ColorField>();
                    Assert.That(colour.value, Is.EqualTo(Color.red), "and its colour");
                    Assert.That(colour.ClassListContains("ad-hidden"), Is.False, "with the colour field shown");
                    Assert.That(ShowsLabel(shapesRoot, "シェイプの説明"), Is.True, "the AvatarDoc Shapes inspector is built again");
                    Assert.That(quickStart.titleContent.text, Is.EqualTo("はじめに"));
                }

                Assert.That(import.titleContent.text, Is.EqualTo(English("ad.ui.import")), "and back");
                Assert.That(ShowsLabel(import.rootVisualElement, English("ad.ui.import.intro")), Is.True);
                Assert.That(ShowsLabel(shapesRoot, English("ad.ui.shapes.note")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(import);
                Object.DestroyImmediate(outfits);
                Object.DestroyImmediate(capture);
                Object.DestroyImmediate(quickStart);
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(pieceMeshAsset);
            }
            Assert.That(L.LanguageListenerCount, Is.EqualTo(listeners), "closed windows and a destroyed inspector let go");
            AssertRealLanguageUntouched();
        }
    }
}
#endif
