#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Phase 12 task 4, plan item 7: Convert everywhere, In menu everywhere, and one preview control for every kind.</summary>
    public class NamingConsistencyTests
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

        [Test]
        public void ConvertIsTheOnlyNameForConverting()
        {
            var keys = new[]
            {
                "ad.ui.import", "ad.ui.import.intro", "ad.ui.import.none", "ad.ui.import.button",
                "ad.ui.import.button.one", "ad.ui.import.done", "ad.ui.import.rescanned",
                "ad.ui.import.remove.tip", "ad.ui.import.restore.tip", "ad.ui.import.candidate.tip",
                "ad.ui.welcome.convert.desc", "ad.ui.toolbar.convert", "ad.ui.convert", "ad.ui.row.convert.tip",
            };
            foreach (var key in keys)
                Assert.That(L.Text(key).ToLowerInvariant(), Does.Not.Contain("import"), key + ": \"" + L.Text(key) + "\"");

            Assert.That(L.Text("ad.ui.import"), Is.EqualTo("Convert"), "the window title itself");

            var menu = avatar.AddMenu();
            var view = View(menu);
            Assert.That(view.Q<Button>("ad-import-open").text, Is.EqualTo(L.Text("ad.ui.toolbar.convert")), "the toolbar button");
        }

        [Test]
        public void ExistingRowsUseInMenu()
        {
            var menu = avatar.AddMenu();
            var view = View(menu);
            var row = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Node == null && r.Name == "RootToggle");
            view.Tree.SelectKey(row.Key);

            var toggle = view.Detail.Q<Toggle>("ad-existing-in-menu");
            Assert.That(toggle, Is.Not.Null, "the existing row reads the same In menu switch an own entry has");
            Assert.That(toggle.label, Is.EqualTo(L.Text("ad.ui.in_menu")));
            Assert.That(toggle.tooltip, Is.EqualTo(L.Text("ad.ui.in_menu.tip")));
            Assert.That(toggle.value, Is.True, "not hidden yet, so In menu reads on");
            Assert.That(view.Detail.Q<Toggle>("ad-existing-hidden"), Is.Null, "Hidden is gone under that name; In menu is the only one now");

            view.Detail.SetExistingHidden(true);
            var updated = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Key == row.Key);
            view.Tree.SelectKey(updated.Key);
            Assert.That(view.Detail.Q<Toggle>("ad-existing-in-menu").value, Is.False, "hidden reads as In menu off");
        }

        [Test]
        public void PresetsUseTheSamePreviewControl()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var preset = menu.AddEntry(EntryKind.Preset, "Look");
            preset.presetStates.Add(new PresetState { entryId = hat.id, on = true });
            var view = View(menu);
            view.Tree.SelectEntry(preset.id);

            var choice = view.Detail.Q<SegmentedChoice>("ad-preview-choice");
            Assert.That(choice, Is.Not.Null, "a preset uses the same Default, On, Off control as a toggle");
            Assert.That(view.Detail.Q<Button>("ad-preset-apply"), Is.Null, "the old Apply button is gone");
            Assert.That(view.Detail.Q<Button>("ad-preset-default"), Is.Null, "the old Reset button is gone");

            choice.Pick(1);
            Assert.That(PreviewState.TryGet(hat.id, out var onValue) && onValue >= 0.5f, Is.True, "On applies the preset");

            choice.Pick(2);
            Assert.That(PreviewState.TryGet(hat.id, out var offValue) && offValue < 0.5f, Is.True, "Off forces the named control off");

            choice.Pick(0);
            Assert.That(PreviewState.TryGet(hat.id, out _), Is.False, "Default clears it");
        }

        [Test]
        public void OverridesShowADisabledPreviewControlWithAReason()
        {
            var menu = avatar.AddMenu();
            var pin = menu.AddEntry(EntryKind.Override, "Pin");
            var view = View(menu);
            view.Tree.SelectEntry(pin.id);

            var choice = view.Detail.Q<SegmentedChoice>("ad-preview-choice-disabled");
            Assert.That(choice, Is.Not.Null, "an override gets the same widget, disabled, rather than a bare note");
            Assert.That(choice.enabledSelf, Is.False);
            Assert.That(choice.tooltip, Is.Not.Null.And.Not.Empty);
        }
    }
}
#endif
