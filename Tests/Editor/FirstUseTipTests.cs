#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>One dismissible first-use tip per panel kind, and "Tips" in the Component settings fold turning them all back on.</summary>
    public class FirstUseTipTests
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

        [Test]
        public void TipShowsOnceAndCanBeDismissed()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            var view = View(menu);
            view.TipStore = new MemoryTipStore();
            view.Tree.SelectEntry(toggle.id);

            var tip = view.Detail.Q<VisualElement>("ad-tip-toggle");
            Assert.That(tip, Is.Not.Null, "the first time a toggle panel opens, its tip shows");
            Assert.That(tip.Q<Button>("ad-tip-close"), Is.Not.Null, "a close cross dismisses it");

            view.TipStore.Dismiss(TipKinds.Toggle);
            view.Detail.ForceRebuild();
            Assert.That(view.Detail.Q<VisualElement>("ad-tip-toggle"), Is.Null, "dismissed for good");

            // Leaving and coming back to this same panel kind must not bring it back on its own.
            view.Tree.ClearSelection();
            view.Tree.SelectEntry(toggle.id);
            Assert.That(view.Detail.Q<VisualElement>("ad-tip-toggle"), Is.Null);
        }

        [Test]
        public void TipsCanBeTurnedBackOn()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Hat");
            var view = View(menu);
            view.TipStore = new MemoryTipStore();
            view.Tree.SelectEntry(toggle.id);
            view.TipStore.Dismiss(TipKinds.Toggle);
            view.Detail.ForceRebuild();
            Assert.That(view.Detail.Q<VisualElement>("ad-tip-toggle"), Is.Null);

            Assert.That(view.Header.Q<Button>("ad-tips-switch"), Is.Not.Null, "Tips lives in the Component settings fold");
            view.Header.TurnTipsBackOn();

            Assert.That(view.Detail.Q<VisualElement>("ad-tip-toggle"), Is.Not.Null, "Tips brings every dismissed tip back at once");
        }

        [Test]
        public void TipsNeverTouchTheRealEditorPrefs()
        {
            const string prefix = "DocMe.AvatarDoc.Tip:";
            var before = TipKinds.All.Select(k => EditorPrefs.HasKey(prefix + k)).ToList();

            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Trigger, "Ears");
            var view = View(menu);
            Assert.That(view.TipStore, Is.InstanceOf<MemoryTipStore>(), "a view a test builds never receives the real, EditorPrefs backed store on its own");
            view.Tree.SelectEntry(toggle.id);
            foreach (var kind in TipKinds.All) view.TipStore.Dismiss(kind);
            view.Header.TurnTipsBackOn();

            for (int i = 0; i < TipKinds.All.Length; i++)
                Assert.That(EditorPrefs.HasKey(prefix + TipKinds.All[i]), Is.EqualTo(before[i]), TipKinds.All[i] + " must be untouched");
        }
    }
}
#endif
