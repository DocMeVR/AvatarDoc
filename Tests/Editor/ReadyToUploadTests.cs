using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Phase 12 task 2, plan item 2: the header's ready line, its Show button, the Write Defaults line and the mirrored upload style checks.</summary>
    public class ReadyToUploadTests
    {
        TestAvatar avatar;
        System.Func<bool> detectVrcFury;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            // The budget line reads differently with VRCFury installed, as it is in this project; these tests
            // check the line a creator without it sees, unless a test says otherwise.
            detectVrcFury = ParametersWriter.VrcFuryInstalled;
            ParametersWriter.VrcFuryInstalled = () => false;
            AvatarPlanCache.Invalidate();
        }

        [TearDown]
        public void TearDown()
        {
            ParametersWriter.VrcFuryInstalled = detectVrcFury;
            avatar.Dispose();
            AvatarPlanCache.Invalidate();
        }

        AvatarDoctorView View(AvatarDoc menu) => new AvatarDoctorView(new SerializedObject(menu), menu);
        AvatarDoctorView View(AvatarDoc menu, IViewPrefs prefs) => new AvatarDoctorView(new SerializedObject(menu), menu, viewPrefs: prefs);

        Label ReadyLabel(AvatarDoctorView view) => view.Header.Q<Label>("ad-ready-label");
        Button ShowButton(AvatarDoctorView view) => view.Header.Q<Button>("ad-ready-show");
        Toggle ProblemsArrow(AvatarDoctorView view) => view.Header.Q<Toggle>("ad-problems-arrow");

        [Test]
        public void ReadyLineShowsReadyWithNoMessages()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);

            Assert.That(ReadyLabel(view).text, Is.EqualTo(L.Text("ad.ui.ready")));
            Assert.That(ReadyLabel(view).ClassListContains("ad-text-ok"), Is.True);
            Assert.That(ShowButton(view).ClassListContains("ad-hidden"), Is.True);
        }

        [Test]
        public void ReadyLineCountsErrorsAndWarnings()
        {
            var menu = avatar.AddMenu();
            var a = menu.AddEntry(EntryKind.Toggle, "A"); a.exclusiveGroup = "G"; a.groupValue = 1; a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var b = menu.AddEntry(EntryKind.Toggle, "B"); b.exclusiveGroup = "G"; b.groupValue = 1; b.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var view = View(menu);
            var plan = view.Plan;
            int errorCount = plan.Messages.Count(m => m.Severity == MessageSeverity.Error);
            Assert.That(errorCount, Is.GreaterThan(0), "two toggles of one group sharing a value must raise an error");

            Assert.That(ReadyLabel(view).text, Is.EqualTo(UiText.Count(errorCount, "ad.ui.ready.problems.one", "ad.ui.ready.problems")));
            Assert.That(ReadyLabel(view).ClassListContains("ad-text-error"), Is.True);
            Assert.That(ShowButton(view).ClassListContains("ad-hidden"), Is.False);
            Assert.That(ProblemsArrow(view).ClassListContains("ad-hidden"), Is.False);
        }

        /// <summary>
        /// Phase 12 task 10: the badge this test once checked is gone (it repeated the count the ready line's own
        /// text already carries); the arrow becoming reachable and the ready line's own text updating after a
        /// live refresh is the same behaviour in the merged, badge free header.
        /// </summary>
        [Test]
        public void SummaryButtonShowsTheProblemCount()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);
            Assert.That(ProblemsArrow(view).ClassListContains("ad-hidden"), Is.True, "nothing to fix, nothing to show");

            var a = menu.AddEntry(EntryKind.Toggle, "A"); a.exclusiveGroup = "G"; a.groupValue = 1; a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var b = menu.AddEntry(EntryKind.Toggle, "B"); b.exclusiveGroup = "G"; b.groupValue = 1; b.objects.Add(new ObjectTarget { target = avatar.Glasses });
            AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
            view.Refresh();
            int errorCount = view.Plan.Messages.Count(m => m.Severity == MessageSeverity.Error);

            Assert.That(ProblemsArrow(view).ClassListContains("ad-hidden"), Is.False);
            Assert.That(ReadyLabel(view).text, Is.EqualTo(UiText.Count(errorCount, "ad.ui.ready.problems.one", "ad.ui.ready.problems")), "the ready line's own text carries the count now, with no separate badge to repeat it");
        }

        [Test]
        public void ReadyLineShowsWarningsWhenThereAreNoErrors()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "A").objects.Add(new ObjectTarget { target = null });
            var view = View(menu);
            var plan = view.Plan;
            Assert.That(plan.Messages.Any(m => m.Severity == MessageSeverity.Error), Is.False);
            int warningCount = plan.Messages.Count(m => m.Severity == MessageSeverity.Warning);
            Assert.That(warningCount, Is.GreaterThan(0));

            Assert.That(ReadyLabel(view).text, Is.EqualTo(UiText.Count(warningCount, "ad.ui.ready.warnings.one", "ad.ui.ready.warnings")));
            Assert.That(ReadyLabel(view).ClassListContains("ad-text-warn"), Is.True);
        }

        [Test]
        public void ShowJumpsToTheFirstProblemRow()
        {
            var menu = avatar.AddMenu();
            var first = menu.AddEntry(EntryKind.Toggle, "First"); first.objects.Add(new ObjectTarget { target = null });
            var second = menu.AddEntry(EntryKind.Toggle, "Second"); second.objects.Add(new ObjectTarget { target = null });
            var view = View(menu);
            var warnings = view.Plan.Messages.Where(m => m.Severity == MessageSeverity.Warning && m.EntryIds.Count > 0).ToList();
            Assert.That(warnings.Count, Is.GreaterThanOrEqualTo(2));

            view.Header.ShowNextProblem();

            Assert.That(view.Tree.Selected, Is.Not.Null);
            Assert.That(view.Tree.Selected.EntryId, Is.EqualTo(warnings[0].EntryIds[0]));
        }

        [Test]
        public void ShowCyclesThroughProblems()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "First").objects.Add(new ObjectTarget { target = null });
            menu.AddEntry(EntryKind.Toggle, "Second").objects.Add(new ObjectTarget { target = null });
            var view = View(menu);
            var warnings = view.Plan.Messages.Where(m => m.Severity == MessageSeverity.Warning && m.EntryIds.Count > 0).ToList();
            Assert.That(warnings.Count, Is.EqualTo(2));

            view.Header.ShowNextProblem();
            var firstSelected = view.Tree.Selected.EntryId;
            view.Header.ShowNextProblem();
            var secondSelected = view.Tree.Selected.EntryId;

            Assert.That(secondSelected, Is.Not.EqualTo(firstSelected));
            Assert.That(secondSelected, Is.EqualTo(warnings[1].EntryIds[0]));
        }

        class FocusHostWindow : EditorWindow { }

        [Test]
        public void ShowFocusesAKnownField()
        {
            var menu = avatar.AddMenu();
            var a = menu.AddEntry(EntryKind.Radial, "A"); a.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", minWeight = 0f, maxWeight = 100f });
            a.parameterName = "Existing";
            var view = View(menu);
            var conflict = view.Plan.Messages.FirstOrDefault(m => m.Key == "ad.error.parameter_type_conflict");
            Assert.That(conflict, Is.Not.Null, "a radial reusing the avatar's own Bool parameter must conflict");
            Assert.That(conflict.FieldName, Is.EqualTo("ad-parameter-name-field"));

            var window = ScriptableObject.CreateInstance<FocusHostWindow>();
            try
            {
                window.Show();
                window.rootVisualElement.Add(view);

                view.Header.ShowNextProblem();

                var field = view.Detail.Q<TextField>("ad-parameter-name-field");
                Assert.That(field, Is.Not.Null, "the conflicting entry's custom parameter field must be showing");
                Assert.That(field.panel?.focusController?.focusedElement, Is.SameAs(field));
            }
            finally
            {
                view.RemoveFromHierarchy();
                window.Close();
            }
        }

        [Test]
        public void WriteDefaultsLineNamesTheMatchedMode()
        {
            var offMenu = avatar.AddMenu();
            var offView = View(offMenu);
            Assert.That(offView.Header.Q<Label>("ad-write-defaults").text,
                Is.EqualTo(string.Format(L.Text("ad.ui.write_defaults"), L.Text("ad.ui.write_defaults.off"))));

            avatar.Dispose();
            AvatarPlanCache.Invalidate();
            avatar = TestAvatar.Create(writeDefaultsOff: false);
            var onMenu = avatar.AddMenu();
            var onView = View(onMenu);
            Assert.That(onView.Header.Q<Label>("ad-write-defaults").text,
                Is.EqualTo(string.Format(L.Text("ad.ui.write_defaults"), L.Text("ad.ui.write_defaults.on"))));
        }

        [Test]
        public void MixedWriteDefaultsOnTheAvatarWarnsAndNamesTheLayers()
        {
            avatar.Fx.AddLayer("Mixed");
            var mixedState = avatar.Fx.layers[1].stateMachine.AddState("On");
            mixedState.writeDefaultValues = true;
            var menu = avatar.AddMenu();
            var view = View(menu);

            var warning = view.Plan.Messages.FirstOrDefault(m => m.Key == "ad.warn.write_defaults_mixed");
            Assert.That(warning, Is.Not.Null);
            Assert.That(warning.Args, Has.Some.Matches<object>(a => a is string s && s.Contains("Mixed")));
            Assert.That(warning.Args, Has.Some.Matches<object>(a => a is string s && s.Contains("Base")));
            // A build reached with a mix still needs a single answer: unanimous-off is the only way off wins.
            Assert.That(view.Header.Q<Label>("ad-write-defaults").text,
                Is.EqualTo(string.Format(L.Text("ad.ui.write_defaults"), L.Text("ad.ui.write_defaults.on"))));
            Assert.That(view.Header.Q<VisualElement>("ad-problems-list").Query<Label>(className: "ad-message-text").ToList()
                .Any(l => l.text.Contains("Mixed") && l.text.Contains("Base")), Is.True, "the mixed warning must show in the header's problem list, not only count toward Ready");
        }

        [Test]
        public void ShowOnAnAvatarWideProblemPointsAtIt()
        {
            var menu = avatar.AddMenu();
            for (int i = 0; i < 40; i++) menu.parameters.Add(ParameterDeclaration.Create("P" + i, PlannedType.Int));
            var view = View(menu);
            var error = view.Plan.Messages.FirstOrDefault(m => m.Key == "ad.error.bits_exceeded");
            Assert.That(error, Is.Not.Null);
            Assert.That(error.EntryIds.Count, Is.EqualTo(0), "an avatar wide problem names no entry");
            Assert.That(string.IsNullOrEmpty(error.EditId), Is.True);

            view.Header.ShowNextProblem();

            Assert.That(view.Header.LastFlashedPlanNoteKey, Is.EqualTo("ad.error.bits_exceeded"), "Show has nothing else to point at, so it points at its own line in the list");
            Assert.That(view.Header.ProblemsOpen, Is.True, "Show opens the list to reveal the line it flashes");
            var box = view.Header.Q<VisualElement>("ad-problem-line-ad.error.bits_exceeded");
            Assert.That(box, Is.Not.Null);
            Assert.That(box.ClassListContains("ad-flash"), Is.True, "the line flashes so \"Show\" is visibly doing something");
        }

        // ---- upload style checks mirrored from the VRChat SDK's own build validation --------------------------

        [Test]
        public void MirrorsExpressionParameterBudgetCheckInTheEditor()
        {
            var menu = avatar.AddMenu();
            for (int i = 0; i < 40; i++)
            {
                var declaration = ParameterDeclaration.Create("P" + i, PlannedType.Int);
                menu.parameters.Add(declaration);
            }
            var view = View(menu);

            Assert.That(view.Plan.AvatarBits + view.Plan.AvatarDoctorBits, Is.GreaterThan(VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.MAX_PARAMETER_COST));
            var error = view.Plan.Messages.FirstOrDefault(m => m.Key == "ad.error.bits_exceeded");
            Assert.That(error, Is.Not.Null, "the SDK's own \"too many parameters defined\" check must already show in the editor, before any upload");
        }

        [Test]
        public void BudgetOverrunIsAWarningWhenVrcFuryWillCompress()
        {
            ParametersWriter.VrcFuryInstalled = () => true;
            var menu = avatar.AddMenu();
            for (int i = 0; i < 40; i++) menu.parameters.Add(ParameterDeclaration.Create("P" + i, PlannedType.Int));
            var view = View(menu);

            Assert.That(view.Plan.Messages.Any(m => m.Key == "ad.error.bits_exceeded"), Is.False, "the inspector says what the build will do: VRCFury compresses at upload");
            var warning = view.Plan.Messages.FirstOrDefault(m => m.Key == "ad.warn.bits_exceeded_vrcfury");
            Assert.That(warning, Is.Not.Null);
            Assert.That(warning.Severity, Is.EqualTo(MessageSeverity.Warning));
        }

        const string TempParent = "Assets/TempAssets";
        const string Folder = TempParent + "/ReadyToUploadTests";

        Texture2D MakeIcon(string name, int size, int maxTextureSize, TextureImporterCompression compression)
        {
            if (!AssetDatabase.IsValidFolder(TempParent)) AssetDatabase.CreateFolder("Assets", "TempAssets");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(TempParent, "ReadyToUploadTests");
            var path = Folder + "/" + name + ".png";
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            var source = new Texture2D(size, size, TextureFormat.RGBA32, false);
            source.SetPixels32(pixels);
            source.Apply();
            File.WriteAllBytes(path, source.EncodeToPNG());
            Object.DestroyImmediate(source);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            // Left at its default, a non power of two size like 300 would be rounded to the nearest power of two on
            // import (256, in this case), which would silently pass the very check this is meant to fail.
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = maxTextureSize;
            importer.textureCompression = compression;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        [TearDown]
        public void DeleteTempIcons()
        {
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            if (AssetDatabase.IsValidFolder(TempParent) && AssetDatabase.FindAssets("", new[] { TempParent }).Length == 0) AssetDatabase.DeleteAsset(TempParent);
        }

        [Test]
        public void MirrorsMenuIconTooLargeCheck()
        {
            var menu = avatar.AddMenu();
            var big = menu.AddEntry(EntryKind.Toggle, "Big");
            big.objects.Add(new ObjectTarget { target = avatar.Hat });
            big.icon = MakeIcon("Big", 300, 1024, TextureImporterCompression.Compressed);
            var uncompressed = menu.AddEntry(EntryKind.Toggle, "Uncompressed");
            uncompressed.objects.Add(new ObjectTarget { target = avatar.Glasses });
            uncompressed.icon = MakeIcon("Uncompressed", 64, 256, TextureImporterCompression.Uncompressed);
            var fine = menu.AddEntry(EntryKind.Toggle, "Fine");
            fine.objects.Add(new ObjectTarget { target = avatar.Body.gameObject });
            fine.icon = MakeIcon("Fine", 64, 256, TextureImporterCompression.Compressed);

            var view = View(menu);
            var errors = view.Plan.Messages.Where(m => m.Key == "ad.error.menu_icon_too_large").ToList();

            Assert.That(errors.Any(e => e.EntryIds.Contains(big.id)), Is.True, "an icon over 256px whose importer still allows that must fail");
            Assert.That(errors.Any(e => e.EntryIds.Contains(uncompressed.id)), Is.True, "an uncompressed icon must fail regardless of size");
            Assert.That(errors.Any(e => e.EntryIds.Contains(fine.id)), Is.False, "a compressed, 256px or smaller icon must pass");
        }

        [Test]
        public void ADisabledComponentsIconDoesNotFailTheUploadCheck()
        {
            var menu = avatar.AddMenu();
            var big = menu.AddEntry(EntryKind.Toggle, "Big");
            big.objects.Add(new ObjectTarget { target = avatar.Hat });
            big.icon = MakeIcon("Big", 300, 1024, TextureImporterCompression.Compressed);
            menu.enabled = false;

            var view = View(menu);

            Assert.That(view.Plan.Messages.Any(m => m.Key == "ad.error.menu_icon_too_large"), Is.False, "the build never sees this component, so its icon cannot fail an upload");
        }

        // ---- phase 12 task 9: one status line instead of a stack of warnings -----------------------------------

        [Test]
        public void AnOrphanEditAloneStillShowsTheStatusAndItsRemove()
        {
            var menu = avatar.AddMenu();
            menu.menuEdits.Add(new MenuEdit { id = "orphan", source = new List<string> { "Gone" }, name = "Ghost" });
            var view = View(menu);

            // An orphaned edit is a warning the creator must act on: it counts in the ready line and the status
            // row stays reachable, the same as any other single warning would.
            Assert.That(ReadyLabel(view).text, Is.EqualTo(UiText.Count(1, "ad.ui.ready.warnings.one", "ad.ui.ready.warnings")));
            Assert.That(ReadyLabel(view).ClassListContains("ad-text-warn"), Is.True);
            Assert.That(view.Header.Q<Toggle>("ad-problems-arrow").ClassListContains("ad-hidden"), Is.False, "the arrow must be reachable");
            Assert.That(ShowButton(view).ClassListContains("ad-hidden"), Is.False);

            view.Header.ToggleProblemsOpen();
            var list = view.Header.Q<VisualElement>("ad-problems-list");
            var removeButton = list.Query<Button>().Build().FirstOrDefault(b => b.text == L.Text("ad.ui.edit.remove"));
            Assert.That(removeButton, Is.Not.Null, "the orphan edit's own line, with its Remove");

            Undo.IncrementCurrentGroup();
            UiTestEvents.SimulateClick(removeButton);

            Assert.That(menu.menuEdits, Is.Empty);
            Assert.That(view.Header.OrphanEditIds, Is.Empty);
            Assert.That(ReadyLabel(view).text, Is.EqualTo(L.Text("ad.ui.ready")), "nothing left to warn about");

            Undo.PerformUndo();

            Assert.That(menu.menuEdits.Any(e => e.id == "orphan"), Is.True, "Remove, clicked from the list, is undoable like every other write here");
        }

        [Test]
        public void StatusLineIsOneRowAndTheListIsClosedByDefault()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "A").objects.Add(new ObjectTarget { target = null });
            var view = View(menu);

            var statusRow = view.Header.Q<VisualElement>("ad-status-row");
            Assert.That(statusRow, Is.Not.Null);
            Assert.That(statusRow.ClassListContains("ad-row"), Is.True, "one flex row, not a stack of boxes");
            var children = statusRow.Children().ToList();
            Assert.That(children, Has.Member(ReadyLabel(view)));
            Assert.That(children, Has.Member(ShowButton(view)));
            Assert.That(children, Has.Member(view.Header.Q<Toggle>("ad-problems-arrow")));

            Assert.That(view.Header.ProblemsOpen, Is.False, "closed by default");
            Assert.That(view.Header.Q<VisualElement>("ad-problems-list").ClassListContains("ad-hidden"), Is.True);
        }

        [Test]
        public void NoArrowWhenReady()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);

            Assert.That(ReadyLabel(view).text, Is.EqualTo(L.Text("ad.ui.ready")));
            Assert.That(view.Header.Q<Toggle>("ad-problems-arrow").ClassListContains("ad-hidden"), Is.True);
            Assert.That(ShowButton(view).ClassListContains("ad-hidden"), Is.True);
        }

        [Test]
        public void ProblemListRemembersOpenPerComponent()
        {
            var prefs = new MemoryViewPrefs();
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "A").objects.Add(new ObjectTarget { target = null });
            var view = View(menu, prefs);

            view.Header.ToggleProblemsOpen();
            Assert.That(view.Header.ProblemsOpen, Is.True);

            var reopened = View(menu, prefs);
            Assert.That(reopened.Header.ProblemsOpen, Is.True, "the same component's own open state is remembered");

            var otherGo = new GameObject("Other");
            otherGo.transform.SetParent(avatar.Root.transform);
            var otherMenu = otherGo.AddComponent<AvatarDoc>();
            var otherView = View(otherMenu, prefs);
            Assert.That(otherView.Header.ProblemsOpen, Is.False, "a different component never shares another one's fold key");
        }

        [Test]
        public void ANewErrorOpensTheListOnce()
        {
            var menu = avatar.AddMenu();
            var a = menu.AddEntry(EntryKind.Toggle, "A"); a.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            Assert.That(view.Header.ProblemsOpen, Is.False);

            var b = menu.AddEntry(EntryKind.Toggle, "B"); b.exclusiveGroup = "G"; b.groupValue = 1; b.objects.Add(new ObjectTarget { target = avatar.Glasses });
            a.exclusiveGroup = "G"; a.groupValue = 1;
            AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
            view.Refresh();
            Assert.That(view.Plan.Messages.Any(m => m.Severity == MessageSeverity.Error), Is.True);

            Assert.That(view.Header.ProblemsOpen, Is.True, "a live 0 to more than 0 transition opens the list once");

            view.Header.ToggleProblemsOpen();
            Assert.That(view.Header.ProblemsOpen, Is.False, "the creator can still close it again");

            b.groupValue = 2;
            AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
            view.Refresh();
            Assert.That(view.Plan.Messages.Any(m => m.Severity == MessageSeverity.Error), Is.False);

            var c = menu.AddEntry(EntryKind.Toggle, "C"); c.exclusiveGroup = "G2"; c.groupValue = 1; c.objects.Add(new ObjectTarget { target = avatar.Body.gameObject });
            var d = menu.AddEntry(EntryKind.Toggle, "D"); d.exclusiveGroup = "G2"; d.groupValue = 1; d.objects.Add(new ObjectTarget { target = avatar.Body.gameObject });
            AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
            view.Refresh();
            Assert.That(view.Plan.Messages.Any(m => m.Severity == MessageSeverity.Error), Is.True, "a second, unrelated error now exists");

            Assert.That(view.Header.ProblemsOpen, Is.False, "it opens itself only once per session");
        }

        [Test]
        public void DuplicateMessagesMergeWithACount()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Same");
            menu.AddEntry(EntryKind.Toggle, "Same");
            var view = View(menu);

            var lines = view.Header.Q<VisualElement>("ad-problems-list").Query<Label>(className: "ad-message-text").Build().ToList();
            Assert.That(lines.Any(l => l.text.Contains("×2")), Is.True, "two entries raising the identical no-effects warning merge into one line ending in ×2");
        }

        [Test]
        public void OrphanEditIsRemovedFromTheListWithUndo()
        {
            var menu = avatar.AddMenu();
            menu.menuEdits.Add(new MenuEdit { id = "orphan", source = new List<string> { "Gone" }, name = "Ghost" });
            var view = View(menu);
            Assert.That(view.Header.OrphanEditIds, Is.EqualTo(new List<string> { "orphan" }));

            Undo.IncrementCurrentGroup();
            view.Header.RemoveOrphanEdit("orphan");

            Assert.That(menu.menuEdits, Is.Empty);
            Assert.That(view.Header.OrphanEditIds, Is.Empty);

            Undo.PerformUndo();

            Assert.That(menu.menuEdits.Any(e => e.id == "orphan"), Is.True, "removing an orphan edit from the list must be undoable, same as the box it replaced");
        }

        [Test]
        public void AvatarWideShowFlashesItsLine()
        {
            var menu = avatar.AddMenu();
            for (int i = 0; i < 40; i++) menu.parameters.Add(ParameterDeclaration.Create("P" + i, PlannedType.Int));
            var view = View(menu);
            var error = view.Plan.Messages.FirstOrDefault(m => m.Key == "ad.error.bits_exceeded");
            Assert.That(error, Is.Not.Null);
            Assert.That(view.Header.ProblemsOpen, Is.False, "closed until Show is used");

            view.Header.ShowNextProblem();

            Assert.That(view.Header.LastFlashedPlanNoteKey, Is.EqualTo("ad.error.bits_exceeded"));
            Assert.That(view.Header.ProblemsOpen, Is.True, "Show opens the list to reveal the line it flashes");
            var line = view.Header.Q<VisualElement>("ad-problem-line-ad.error.bits_exceeded");
            Assert.That(line, Is.Not.Null);
            Assert.That(line.ClassListContains("ad-flash"), Is.True);
        }

        [Test]
        public void LongListShowsSixThenMore()
        {
            var menu = avatar.AddMenu();
            for (int i = 0; i < 9; i++) menu.AddEntry(EntryKind.Toggle, "Empty" + i);
            var view = View(menu);
            int warningCount = view.Plan.Messages.Count(m => m.Severity == MessageSeverity.Warning);
            Assert.That(warningCount, Is.GreaterThanOrEqualTo(9), "nine entries with no effect targets each raise their own, distinctly named warning");

            var list = view.Header.Q<VisualElement>("ad-problems-list");
            var lineCount = list.Query<VisualElement>(className: "ad-problem-line").Build().ToList().Count;
            Assert.That(lineCount, Is.EqualTo(6), "at most six lines show at once");
            var more = list.Q<Button>("ad-problems-more");
            Assert.That(more, Is.Not.Null);
            Assert.That(more.text, Is.EqualTo(string.Format(L.Text("ad.ui.problems.more"), warningCount - 6)));

            view.Header.ShowAllProblems();

            lineCount = list.Query<VisualElement>(className: "ad-problem-line").Build().ToList().Count;
            Assert.That(lineCount, Is.EqualTo(warningCount), "Show N more expands the rest for this session");
        }

        [Test]
        public void WriteDefaultsLineLivesInComponentSettings()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);

            var label = view.Header.Q<Label>("ad-write-defaults");
            Assert.That(label, Is.Not.Null);
            var foldContent = view.Header.Q<VisualElement>("ad-settings-fold-content");
            Assert.That(foldContent.Children().ToList().IndexOf(label), Is.EqualTo(0), "Write Defaults is the fold content's first line");
            Assert.That(label.tooltip, Is.EqualTo(L.Text("ad.ui.write_defaults.tip")), "its tooltip travels with it");
        }
    }
}
