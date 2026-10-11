#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Panels that only show the sections they use, one Advanced fold per panel, and a trigger that starts as one condition.</summary>
    public class PanelsGrowTests
    {
        TestAvatar avatar;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            AvatarPlanCache.Invalidate();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            AvatarPlanCache.Invalidate();
        }

        AvatarDoctorView View(AvatarDoc menu) => new AvatarDoctorView(new SerializedObject(menu), menu);

        AvatarDoctorView View(AvatarDoc menu, IViewPrefs prefs) => new AvatarDoctorView(new SerializedObject(menu), menu, viewPrefs: prefs);

        [Test]
        public void ANewToggleShowsOnlyObjects()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            var view = View(menu);
            view.Tree.SelectEntry(toggle.id);

            var sections = view.Detail.EffectSections;
            var objects = sections.First(s => s.NameKey == "ad.ui.objects");
            Assert.That(objects.View.ClassListContains("ad-hidden"), Is.False, "Objects is a toggle's common starting place, so it shows even while empty");
            foreach (var section in sections.Where(s => s.NameKey != "ad.ui.objects"))
                Assert.That(section.View.ClassListContains("ad-hidden"), Is.True, section.NameKey + " has no row yet");
        }

        [Test]
        public void AddEffectListsEveryKindWithADescription()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            var view = View(menu);
            view.Tree.SelectEntry(toggle.id);

            var sections = view.Detail.EffectSections;
            var expectedNames = new[]
            {
                "ad.ui.objects", "ad.ui.blendshapes", "ad.ui.materials",
                "ad.ui.targets.properties", "ad.ui.targets.animations", "ad.ui.targets.follows",
            };
            Assert.That(sections.Select(s => s.NameKey), Is.EqualTo(expectedNames));
            foreach (var section in sections)
            {
                Assert.That(L.Text(section.NameKey), Is.Not.Null.And.Not.Empty, section.NameKey);
                Assert.That(L.Text(section.DescKey), Is.Not.Null.And.Not.Empty, section.DescKey + " (description for " + section.NameKey + ")");
                Assert.That(section.NameKey, Is.Not.EqualTo(section.DescKey));
            }
            Assert.That(view.Detail.Q<Button>("ad-add-effect"), Is.Not.Null);
        }

        [Test]
        public void RemovingTheLastRowHidesItsSection()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            var view = View(menu);
            view.Tree.SelectEntry(toggle.id);
            var materials = view.Detail.EffectSections.First(s => s.NameKey == "ad.ui.materials").View;
            Assert.That(materials.ClassListContains("ad-hidden"), Is.True, "no material row yet");

            materials.AddMaterialRow();
            Assert.That(materials.ClassListContains("ad-hidden"), Is.False, "a row was just added");

            materials.RemoveAt(0);
            Assert.That(materials.ClassListContains("ad-hidden"), Is.True, "empty again, so it hides");
        }

        [Test]
        public void AdvancedFoldIsClosedByDefaultAndRemembered()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            // A memory store shared by both views below, so "remembered" is checked without ever touching the
            // real EditorPrefs: a fresh store per test needs no cleanup either, unlike the real one.
            var prefs = new MemoryViewPrefs();
            var view = View(menu, prefs);
            view.Tree.SelectEntry(toggle.id);
            var fold = view.Detail.Q<AdvancedFold>();
            Assert.That(fold, Is.Not.Null);
            Assert.That(fold.IsOpen, Is.False, "closed by default");

            fold.ToggleOpen();
            Assert.That(fold.IsOpen, Is.True);

            // A different view of the very same entry, sharing the same store, must read the same, remembered state.
            var view2 = View(menu, prefs);
            view2.Tree.SelectEntry(toggle.id);
            var fold2 = view2.Detail.Q<AdvancedFold>();
            Assert.That(fold2.IsOpen, Is.True, "remembered per entry, not per view");
        }

        [Test]
        public void AdvancedFoldShowsADotWhenItHidesAChangedValue()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = View(menu);
            view.Tree.SelectEntry(toggle.id);
            var fold = view.Detail.Q<AdvancedFold>();
            Assert.That(fold.DotVisible, Is.False, "everything is default so far");

            toggle.blendSeconds = 1.5f;
            AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
            view.Refresh();

            Assert.That(fold.DotVisible, Is.True, "blend time is not at its default while the fold is closed");

            fold.ToggleOpen();
            Assert.That(fold.DotVisible, Is.False, "the dot only shows while the fold is closed");
        }

        [Test]
        public void ATriggerStartsAsOneCondition()
        {
            var menu = avatar.AddMenu();
            var trigger = menu.AddEntry(EntryKind.Trigger, "Ears");
            var view = View(menu);
            view.Tree.SelectEntry(trigger.id);

            Assert.That(trigger.UsesWhenGroups, Is.False, "a fresh trigger has no groups yet");
            Assert.That(view.Detail.Q<VisualElement>("ad-when-card"), Is.Null, "no group card shows until And or Or is pressed");
            Assert.That(view.Detail.Q<Label>("ad-when-any"), Is.Null, "no captions either");
        }

        [Test]
        public void AndOrRevealsGroupsAndFoldsBack()
        {
            var menu = avatar.AddMenu();
            var trigger = menu.AddEntry(EntryKind.Trigger, "Ears");
            var view = View(menu);
            view.Tree.SelectEntry(trigger.id);

            view.Detail.AddCondition(0);

            Assert.That(trigger.UsesWhenGroups, Is.True);
            Assert.That(trigger.when[0].all.Count, Is.EqualTo(2));
            Assert.That(view.Detail.Q<VisualElement>("ad-when-card"), Is.Not.Null, "a card appears once a condition is added");

            view.Detail.RemoveCondition(0, 1);

            Assert.That(trigger.UsesWhenGroups, Is.False, "one condition left folds back to the flat fields");
            Assert.That(view.Detail.Q<VisualElement>("ad-when-card"), Is.Null);
        }
    }
}
#endif
