#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>The links from the tool into the online guide.</summary>
    public class DocsLinksTests
    {
        TestAvatar avatar;
        Action<string> openBefore;
        Func<bool> vrcFuryBefore;
        List<string> opened;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            vrcFuryBefore = ParametersWriter.VrcFuryInstalled;
            ParametersWriter.VrcFuryInstalled = () => false;
            AvatarPlanCache.Invalidate();
            openBefore = DocsLinks.Open;
            opened = new List<string>();
            DocsLinks.Open = opened.Add;
        }

        [TearDown]
        public void TearDown()
        {
            DocsLinks.Open = openBefore;
            ParametersWriter.VrcFuryInstalled = vrcFuryBefore;
            avatar.Dispose();
            AvatarPlanCache.Invalidate();
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
            Pointer<PointerUpEvent>(target, EventType.MouseUp);
            Pointer<PointerDownEvent>(target, EventType.MouseDown);
            Pointer<PointerUpEvent>(target, EventType.MouseUp);
        }

        /// <summary>Moves LayoutProbe's frame up until the element sits near the top of the host window, so a pointer at its centre lands on it.</summary>
        static void BringIntoView(VisualElement view, VisualElement element)
        {
            view.parent.style.top = view.parent.resolvedStyle.top - (element.worldBound.y - 40f);
            LayoutProbe.Validate(view);
        }

        static VisualElement Host(VisualElement content)
        {
            var host = new VisualElement();
            var uss = AvatarDoctorView.LoadStyleSheet();
            if (uss != null) host.styleSheets.Add(uss);
            host.AddToClassList("ad-root");
            host.Add(content);
            return host;
        }

        AvatarDoctorView View(AvatarDoc menu) => new AvatarDoctorView(new SerializedObject(menu), menu, false, new MemoryViewPrefs());

        [Test]
        public void AnAnchorIsTheKeyInLowerCaseWithDashes()
        {
            Assert.That(DocsLinks.Anchor("ad.warn.bits_exceeded"), Is.EqualTo("ad-warn-bits-exceeded"));
        }

        [Test]
        public void PagesAndMessagesBuildTheFullAddress()
        {
            Assert.That(DocsLinks.Page("reference/messages"), Is.EqualTo("https://docmevr.github.io/AvatarDoc/reference/messages/"));
            Assert.That(DocsLinks.Page("/start-here/first-toggle/"), Is.EqualTo("https://docmevr.github.io/AvatarDoc/start-here/first-toggle/"));
            Assert.That(DocsLinks.Message("ad.warn.bits_exceeded"), Is.EqualTo("https://docmevr.github.io/AvatarDoc/reference/messages/#ad-warn-bits-exceeded"));
        }

        [Test]
        public void ACountOfOneUsesTheEntryOfItsBaseKey()
        {
            Assert.That(DocsLinks.Message("ad.error.trigger_too_complex.one"), Is.EqualTo(DocsLinks.Message("ad.error.trigger_too_complex")));
        }

        [Test]
        public void OnlyErrorWarningAndInfoKeysAreMessageKeys()
        {
            Assert.That(DocsLinks.IsMessageKey("ad.error.x"), Is.True);
            Assert.That(DocsLinks.IsMessageKey("ad.warn.x"), Is.True);
            Assert.That(DocsLinks.IsMessageKey("ad.info.x"), Is.True);
            Assert.That(DocsLinks.IsMessageKey("ad.ui.name"), Is.False);
            Assert.That(DocsLinks.IsMessageKey(null), Is.False);
            Assert.That(DocsLinks.LearnMoreButton("ad.ui.name"), Is.Null);
        }

        [Test]
        public void TheComponentsPointTheHelpButtonAtTheirReferencePages()
        {
            Assert.That(typeof(AvatarDoc).GetCustomAttribute<HelpURLAttribute>().URL, Is.EqualTo(DocsAddress.Base + "reference/inspector/"));
            Assert.That(typeof(AvatarDocShapes).GetCustomAttribute<HelpURLAttribute>().URL, Is.EqualTo(DocsAddress.Base + "reference/avatardoc-shapes/"));
        }

        [Test]
        public void AMessageBoxHasALearnMoreButtonThatOpensItsEntry()
        {
            var boxes = new VisualElement();
            EntryDetailView.AddMessageBoxes(boxes, new[] { ModelMessage.Warning("ad.warn.bits_exceeded", "x") });
            LayoutProbe.Measure(Host(boxes), () =>
            {
                var button = boxes.Q<Button>(className: "ad-learn-more");
                Assert.That(button, Is.Not.Null);
                Assert.That(button.text, Is.EqualTo(L.Text("ad.ui.message.learn_more")));
                Assert.That(button.tooltip, Is.EqualTo(L.Text("ad.ui.message.learn_more.tip")));
                Click(button);
            });
            Assert.That(opened, Is.EqualTo(new[] { DocsLinks.Message("ad.warn.bits_exceeded") }));
        }

        [Test]
        public void ACountOfOneMessageOpensTheEntryOfItsBaseKey()
        {
            var boxes = new VisualElement();
            EntryDetailView.AddMessageBoxes(boxes, new[] { ModelMessage.Error("ad.error.trigger_too_complex.one", "Ears", "1", "100") });
            LayoutProbe.Measure(Host(boxes), () => Click(boxes.Q<Button>(className: "ad-learn-more")));
            Assert.That(opened, Is.EqualTo(new[] { DocsLinks.Page("reference/messages") + "#ad-error-trigger-too-complex" }));
        }

        [Test]
        public void AMessageThatIsNotAMessageKeyGetsNoButton()
        {
            var boxes = new VisualElement();
            EntryDetailView.AddMessageBoxes(boxes, new[] { ModelMessage.Info("ad.ui.name") });
            Assert.That(boxes.Query<Button>(className: "ad-learn-more").ToList(), Is.Empty);
            Assert.That(boxes.Query(className: "ad-warnbox").ToList().Count, Is.EqualTo(1));
        }

        [Test]
        public void TheHeadersProblemLinesShowTheButton()
        {
            var menu = avatar.AddMenu();
            for (int i = 0; i < 40; i++) menu.parameters.Add(ParameterDeclaration.Create("P" + i, PlannedType.Int));
            var view = View(menu);
            if (!view.Header.ProblemsOpen) view.Header.ToggleProblemsOpen();
            var line = view.Header.Q<VisualElement>("ad-problem-line-ad.error.bits_exceeded");
            Assert.That(line, Is.Not.Null, "the problem line for the bits error");
            var button = line.Q<Button>(className: "ad-learn-more");
            Assert.That(button, Is.Not.Null, "its Learn more button");
            LayoutProbe.Measure(view, () =>
            {
                BringIntoView(view, button);
                Click(button);
            });
            Assert.That(opened, Is.EqualTo(new[] { DocsLinks.Message("ad.error.bits_exceeded") }));
        }

        [Test]
        public void ATooComplexTriggerLineShowsTheButton()
        {
            var menu = avatar.AddMenu();
            var trigger = menu.AddEntry(EntryKind.Trigger, "Ears");
            trigger.objects.Add(new ObjectTarget { target = avatar.Glasses });
            trigger.when = Enumerable.Range(0, 4).Select(g => new ConditionGroup
            {
                all = Enumerable.Range(0, 4).Select(i => new WhenCondition { kind = WhenConditionKind.Parameter, sourceParameter = "Voice", condition = ParameterCondition.NotEquals, conditionValue = 0.5f }).ToList()
            }).ToList();
            var view = View(menu);
            view.Tree.SelectEntry(trigger.id);
            var line = view.Detail.Q<VisualElement>("ad-too-complex");
            Assert.That(line, Is.Not.Null, "the panel names why the trigger cannot build");
            var button = line.Q<Button>(className: "ad-learn-more");
            Assert.That(button, Is.Not.Null);
            LayoutProbe.Measure(view, () =>
            {
                BringIntoView(view, button);
                Click(button);
            });
            Assert.That(opened, Is.EqualTo(new[] { DocsLinks.Page("reference/messages") + "#ad-error-trigger-too-complex" }));
        }

        [Test]
        public void TheCaptureStatusLineShowsTheButton()
        {
            var menu = avatar.AddMenu();
            avatar.AddBodyShapes("Blush");
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            var view = View(menu);
            view.Tree.SelectEntry(face.id);
            view.Detail.Q<TargetListView>("ad-blendshapes").CaptureFrom(avatar.Body);
            var status = view.Detail.Q<Label>("ad-shape-set-status");
            Assert.That(status, Is.Not.Null);
            var button = status.parent.Q<Button>(className: "ad-learn-more");
            Assert.That(button, Is.Not.Null, "the status line has a Learn more button");
            LayoutProbe.Measure(view, () =>
            {
                BringIntoView(view, button);
                Click(button);
            });
            Assert.That(opened, Is.EqualTo(new[] { DocsLinks.Message("ad.info.capture_nothing_above_zero") }));
        }

        [Test]
        public void ARefusedDropShowsTheButton()
        {
            var menu = avatar.AddMenu();
            var outer = menu.AddEntry(EntryKind.Submenu, "Outer");
            var inner = menu.AddEntry(EntryKind.Submenu, "Inner", outer.id);
            var view = View(menu);
            Assert.That(view.Tree.ApplyStructure(new[] { (view.Tree.RowFor(outer.id), view.Tree.RowFor(inner.id), 0) }), Is.False);
            var refusal = view.Tree.Q<VisualElement>("ad-drop-refusal");
            var button = refusal.Q<Button>(className: "ad-learn-more");
            Assert.That(button, Is.Not.Null, "the refusal has a Learn more button");
            LayoutProbe.Measure(view, () =>
            {
                BringIntoView(view, button);
                Click(button);
            });
            Assert.That(opened, Is.EqualTo(new[] { DocsLinks.Message("ad.info.drop_refused") }));
        }

        [Test]
        public void TheShapeSetRuleNoteShowsTheButtonAndFitsAt300And400()
        {
            var menu = avatar.AddMenu();
            avatar.AddBodyShapes("Blush");
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Pin });
            face.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true, value = ShapeSetValue.Zero });
            var view = View(menu);
            foreach (float width in new[] { 300f, 400f })
            {
                LayoutProbe.Measure(view, () =>
                {
                    view.Tree.SelectEntry(face.id);
                    LayoutProbe.Validate(view);
                    var note = view.Detail.Q<Label>("ad-shape-set-note");
                    var button = note.parent.Q<Button>(className: "ad-learn-more");
                    Assert.That(button, Is.Not.Null, "the count note has a Learn more button");
                    Assert.That(button.worldBound.xMax, Is.LessThanOrEqualTo(view.Detail.worldBound.xMax + 0.5f), width + ": the button stays inside the panel");
                    Assert.That(note.worldBound.xMax, Is.LessThanOrEqualTo(button.worldBound.xMin + 0.5f), width + ": the note ends before the button");
                    Assert.That(note.worldBound.height, Is.GreaterThan(0f), width + ": the note shows");
                }, width);
            }
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(face.id);
                LayoutProbe.Validate(view);
                var button = view.Detail.Q<Label>("ad-shape-set-note").parent.Q<Button>(className: "ad-learn-more");
                BringIntoView(view, button);
                Click(button);
            });
            Assert.That(opened, Is.EqualTo(new[] { DocsLinks.Message("ad.info.shape_set_count") }));
        }

        [Test]
        public void TheCaptureAndDropLinesFitAt300And400()
        {
            var menu = avatar.AddMenu();
            var outer = menu.AddEntry(EntryKind.Submenu, "Outer");
            var inner = menu.AddEntry(EntryKind.Submenu, "Inner", outer.id);
            avatar.AddBodyShapes("Blush");
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            var view = View(menu);
            view.Tree.ApplyStructure(new[] { (view.Tree.RowFor(outer.id), view.Tree.RowFor(inner.id), 0) });
            view.Tree.SelectEntry(face.id);
            view.Detail.Q<TargetListView>("ad-blendshapes").CaptureFrom(avatar.Body);
            foreach (float width in new[] { 300f, 400f })
            {
                LayoutProbe.Measure(view, () =>
                {
                    foreach (var line in new[] { view.Tree.Q<VisualElement>("ad-drop-refusal"), view.Detail.Q<VisualElement>("ad-shape-set-status-box") })
                    {
                        var button = line.Q<Button>(className: "ad-learn-more");
                        var text = line.Q<Label>();
                        Assert.That(button, Is.Not.Null);
                        Assert.That(button.worldBound.xMax, Is.LessThanOrEqualTo(view.worldBound.xMax + 0.5f), width + ": " + line.name + " keeps its button inside");
                        Assert.That(text.worldBound.xMax, Is.LessThanOrEqualTo(button.worldBound.xMin + 0.5f), width + ": " + line.name + " text ends before its button");
                    }
                }, width);
            }
        }

        [Test]
        public void TheShapesInspectorMessagesShowTheButton()
        {
            var bare = avatar.Glasses.AddComponent<AvatarDocShapes>();
            bare.follow.Add(new ShapeFollow { sourceMesh = avatar.Body, sourceShape = "Blush" });
            UnityEditor.Editor editor = null;
            try
            {
                editor = UnityEditor.Editor.CreateEditor(bare);
                var root = ((AvatarDocShapesEditor)editor).CreateInspectorGUI();
                var button = root.Q<VisualElement>("ad-shapes-messages").Q<Button>(className: "ad-learn-more");
                Assert.That(button, Is.Not.Null, "the piece has no mesh, and says so with a button");
                LayoutProbe.Measure(root, () =>
                {
                    BringIntoView(root, button);
                    Click(button);
                });
                Assert.That(opened, Is.EqualTo(new[] { DocsLinks.Message("ad.error.shapes_no_mesh") }));
            }
            finally
            {
                if (editor != null) UnityEngine.Object.DestroyImmediate(editor);
            }
        }

        [Test]
        public void TheQuickStartWindowOpensTheFullGuide()
        {
            var window = ScriptableObject.CreateInstance<QuickStartWindow>();
            try
            {
                window.Init("Step one.");
                var button = window.rootVisualElement.Q<Button>("ad-full-guide");
                Assert.That(button, Is.Not.Null);
                Assert.That(button.text, Is.EqualTo(L.Text("ad.ui.quickstart.full_guide")));
                Assert.That(button.tooltip, Is.EqualTo(L.Text("ad.ui.quickstart.full_guide.tip")));
                var children = window.rootVisualElement.Children().ToList();
                Assert.That(children.IndexOf(button), Is.LessThan(children.IndexOf(window.rootVisualElement.Q<TextField>())), "the button sits above the text");
                UiTestEvents.SimulateClick(button);
                Assert.That(opened, Is.EqualTo(new[] { DocsAddress.Base + "start-here/first-toggle/" }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        static readonly string[] MessageKeys = { "ad.error.bits_exceeded", "ad.error.trigger_too_complex", "ad.ui.message.learn_more" };

        /// <summary>Read when a test asks, not when the class loads, so a language file that cannot be read fails one test instead of every test of the class.</summary>
        static Dictionary<string, Dictionary<string, string>> Languages() => new Dictionary<string, Dictionary<string, string>>
        {
            { "en-US", null },
            { "ja-JP", DocsFixesTests.Real("ja-JP", MessageKeys) },
            { "zh-Hans", DocsFixesTests.Real("zh-Hans", MessageKeys) },
        };

        [Test]
        public void ABoxWithTheButtonFitsAt300And360InEveryLanguage()
        {
            foreach (var language in Languages())
            foreach (float width in new[] { 300f, 360f })
            {
                using (L.UseTestLanguage(language.Key, language.Value))
                {
                    var boxes = new VisualElement();
                    EntryDetailView.AddMessageBoxes(boxes, new[]
                    {
                        ModelMessage.Error("ad.error.bits_exceeded", "300", "256", "12"),
                        ModelMessage.Error("ad.error.trigger_too_complex", "A fairly long trigger name here", "4", "256"),
                    });
                    var host = Host(boxes);
                    LayoutProbe.Measure(host, () =>
                    {
                        var allow = new PanelAudit.AllowList().Allow(e => e.ClassListContains("ad-learn-more"), "a message box's own trailing link, not a labelled field row");
                        PanelAudit.Audit(host, host.worldBound.xMax, allow);
                        foreach (var button in boxes.Query<Button>(className: "ad-learn-more").ToList())
                            Assert.That(button.worldBound.xMax, Is.LessThanOrEqualTo(host.worldBound.xMax + 0.5f), language.Key + " at " + width + ": the button stays inside the box");
                    }, width);
                }
            }
        }

        static string PackageRoot()
        {
            var table = Path.GetFullPath(L.TablePath("en-US"));
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(table), "..", ".."));
        }

        static string ExpectedToolLinks()
        {
            var anchors = new List<string>();
            foreach (var key in LocalizationFileTests.ShippedTexts("en-US").Keys)
                if (DocsLinks.IsMessageKey(key) && !key.EndsWith(".one", StringComparison.Ordinal)) anchors.Add(DocsLinks.Anchor(key));
            anchors.Sort(StringComparer.Ordinal);
            var pages = new[] { "reference/inspector", "reference/avatardoc-shapes", DocsLinks.QuickStartPage, DocsLinks.MessagesPage };
            var json = new StringBuilder("{\n  \"pages\": [\n");
            json.Append(string.Join(",\n", pages.Select(p => "    \"" + p + "\"")));
            json.Append("\n  ],\n  \"anchors\": [\n");
            json.Append(string.Join(",\n", anchors.Select(k => "    \"" + k + "\"")));
            json.Append("\n  ]\n}\n");
            return json.ToString();
        }

        [Test]
        public void TheToolLinksFileIsCurrent()
        {
            var expected = ExpectedToolLinks();
            var file = Path.Combine(PackageRoot(), "Website~", "src", "data", "tool-links.json");
            if (Environment.GetEnvironmentVariable("DOCS_WRITE_TOOL_LINKS") == "1")
            {
                File.WriteAllText(file, expected, new UTF8Encoding(false));
                return;
            }
            var actual = File.Exists(file) ? File.ReadAllText(file).Replace("\r\n", "\n") : "";
            Assert.That(actual, Is.EqualTo(expected),
                "Website~/src/data/tool-links.json is out of date: run this test with DOCS_WRITE_TOOL_LINKS=1 to rewrite it, then commit it");
        }
    }
}
#endif
