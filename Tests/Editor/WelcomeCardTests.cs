#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>The empty tree's welcome card, the empty detail panel's hint, and the header's Component settings fold.</summary>
    public class WelcomeCardTests
    {
        TestAvatar avatar;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        AvatarDoctorView View(AvatarDoc menu) => new AvatarDoctorView(new SerializedObject(menu), menu);

        AvatarDoctorView View(AvatarDoc menu, IViewPrefs prefs) => new AvatarDoctorView(new SerializedObject(menu), menu, viewPrefs: prefs);

        Component AddFuryToggle(string name)
        {
            var go = new GameObject("Fury");
            go.transform.SetParent(avatar.Root.transform);
            return VrcFuryTestHelper.AddToggle(go, name);
        }

        [Test]
        public void AnEmptyComponentShowsTheWelcomeCard()
        {
            var menu = avatar.AddMenu();

            var view = View(menu);

            var card = view.Tree.Q<VisualElement>("ad-welcome-card");
            Assert.That(card, Is.Not.Null);
            Assert.That(card.ClassListContains("ad-hidden"), Is.False, "an empty component shows the welcome card");
            Assert.That(view.Tree.Q<Button>("ad-welcome-toggle"), Is.Not.Null);
            Assert.That(view.Tree.Q<Button>("ad-welcome-outfit"), Is.Not.Null);
            Assert.That(view.Tree.Q<Button>("ad-welcome-quickstart"), Is.Not.Null);
            Assert.That(card.Q<Label>(className: "ad-drop-hint"), Is.Not.Null, "the drop hint still lives, as the card's last line");
        }

        [Test]
        public void TheConvertStartAppearsOnlyWhenSomethingCanBeConverted()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);

            var convertButton = view.Tree.Q<Button>("ad-welcome-convert");
            Assert.That(convertButton, Is.Not.Null);
            Assert.That(convertButton.parent.ClassListContains("ad-hidden"), Is.True,
                "nothing on a plain avatar can be converted, so the start stays hidden");
        }

        [Test]
        public void TheConvertStartShowsWithACountWhenSomethingCanBeConverted()
        {
            Assume.That(VrcFuryTestHelper.VrcFuryType, Is.Not.Null);
            var menu = avatar.AddMenu();
            var toggle = AddFuryToggle("Hat");
            VrcFuryTestHelper.AddAction(toggle, VrcFuryReader.ObjectToggleActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "obj", avatar.Hat);
                VrcFuryTestHelper.SetField(a, "mode", 0);
            });

            var view = View(menu);

            var convertButton = view.Tree.Q<Button>("ad-welcome-convert");
            Assert.That(convertButton.parent.ClassListContains("ad-hidden"), Is.False, "one convertible item must show the start");
            Assert.That(convertButton.text, Does.Contain("1"));
            Assert.That(convertButton.text, Does.Contain(L.Text("ad.ui.chip.vrcfury")));
        }

        [Test]
        public void TheConvertCountFollowsAComponentAddedLater()
        {
            Assume.That(VrcFuryTestHelper.VrcFuryType, Is.Not.Null);
            var menu = avatar.AddMenu();
            var view = View(menu);
            var convertButton = view.Tree.Q<Button>("ad-welcome-convert");
            Assert.That(convertButton.parent.ClassListContains("ad-hidden"), Is.True, "nothing to convert yet");

            var toggle = AddFuryToggle("Hat");
            VrcFuryTestHelper.AddAction(toggle, VrcFuryReader.ObjectToggleActionTypeName, a =>
            {
                VrcFuryTestHelper.SetField(a, "obj", avatar.Hat);
                VrcFuryTestHelper.SetField(a, "mode", 0);
            });
            // A component added anywhere in the scene reaches AvatarPlanCache only as a coarse structural change,
            // never as a menu edit of this component's own, so the welcome card's own scan cache has to drop with it.
            AvatarPlanCache.Invalidate();
            view.Refresh();

            Assert.That(convertButton.parent.ClassListContains("ad-hidden"), Is.False, "the scan must not stay stale from before the component existed");
            Assert.That(convertButton.text, Does.Contain("1"));
        }

        [Test]
        public void TheWelcomeCardLeavesWithTheFirstEntry()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);
            Assert.That(view.Tree.Q<VisualElement>("ad-welcome-card").ClassListContains("ad-hidden"), Is.False);

            var hat = view.Tree.AddEntry(EntryKind.Toggle);
            Assert.That(hat, Is.Not.Null);

            Assert.That(view.Tree.Q<VisualElement>("ad-welcome-card").ClassListContains("ad-hidden"), Is.True,
                "the card disappears the moment the component has its first entry");
        }

        [Test]
        public void TheEmptyDetailPanelSaysWhatToDo()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);

            view.Tree.ClearSelection();

            var note = view.Detail.Q<Label>(className: "ad-note");
            Assert.That(note, Is.Not.Null);
            Assert.That(note.text, Is.EqualTo(L.Text("ad.ui.detail.select_hint")));
            Assert.That(note.text, Does.Contain("Drag"), "it names the next action, not only that nothing is selected");
        }

        [Test]
        public void ComponentSettingsFoldIsClosedByDefaultAndRemembered()
        {
            var menu = avatar.AddMenu();
            // A memory store shared by both views below, so "remembered" is checked without ever touching the
            // real EditorPrefs: a fresh store per test needs no cleanup either, unlike the real one.
            var prefs = new MemoryViewPrefs();
            var view = View(menu, prefs);
            Assert.That(view.Header.SettingsFoldOpen, Is.False, "closed by default");
            Assert.That(view.Header.Q<VisualElement>("ad-settings-fold-content").ClassListContains("ad-hidden"), Is.True);

            view.Header.ToggleSettingsFold();
            Assert.That(view.Header.SettingsFoldOpen, Is.True);
            Assert.That(view.Header.Q<VisualElement>("ad-settings-fold-content").ClassListContains("ad-hidden"), Is.False);

            // A second view of the same component, sharing the same store, as reopening the inspector would build, remembers the choice.
            var reopened = View(menu, prefs);
            Assert.That(reopened.Header.SettingsFoldOpen, Is.True);
        }

        [Test]
        public void TheFoldShowsADotWhenItHidesAChangedValue()
        {
            var menu = avatar.AddMenu();
            menu.installPath = "Outfits";
            var view = View(menu);

            Assert.That(view.Header.SettingsFoldOpen, Is.False);
            Assert.That(view.Header.SettingsFoldDotVisible, Is.True, "closed while a value differs from default");

            view.Header.ToggleSettingsFold();
            Assert.That(view.Header.SettingsFoldDotVisible, Is.False, "no need for a hint once the values are visible");

            view.Header.ToggleSettingsFold();
            Assert.That(view.Header.SettingsFoldDotVisible, Is.True, "closing again with the same non default value shows it again");

            var so = new SerializedObject(menu);
            so.FindProperty(nameof(AvatarDoc.installPath)).stringValue = "";
            so.ApplyModifiedProperties();
            view.Refresh();
            Assert.That(view.Header.SettingsFoldDotVisible, Is.False, "back at every default, closed, hides the dot");
        }

        [Test]
        public void TheMemoryBarStaysVisibleAboveTheSettingsFold()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);

            var body = view.Header.Q<VisualElement>(className: "ad-panel-body");
            var bar = view.Header.Q<MemoryBar>();
            var foldHeader = view.Header.Q<VisualElement>("ad-settings-fold-header");
            var problemsList = view.Header.Q<VisualElement>("ad-problems-list");
            var statusRow = view.Header.Q<VisualElement>("ad-status-row");
            Assert.That(bar, Is.Not.Null);
            Assert.That(foldHeader, Is.Not.Null);
            var siblings = body.Children().ToList();
            Assert.That(siblings, Has.Member(bar), "the memory bar sits directly in the header's own body");
            Assert.That(siblings, Has.Member(foldHeader));
            Assert.That(siblings, Has.Member(problemsList), "the problem list sits directly in the header's own body");
            Assert.That(siblings, Has.Member(statusRow));

            int statusIndex = siblings.IndexOf(statusRow);
            int listIndex = siblings.IndexOf(problemsList);
            int barIndex = siblings.IndexOf(bar);
            int foldIndex = siblings.IndexOf(foldHeader);
            Assert.That(statusIndex, Is.LessThan(foldIndex), "the status line must not fold away");
            Assert.That(listIndex, Is.LessThan(foldIndex), "the problem list must not fold away");
            Assert.That(barIndex, Is.LessThan(foldIndex), "the memory bar must not fold away with Install into / Install as / Prefix / Platform");
        }
    }
}
#endif
