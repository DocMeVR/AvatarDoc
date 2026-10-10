#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Inside the Inspector, Unity marks every Foldout's toggle as an inspector toggle and its theme pulls that toggle
    /// left into the inspector's gutter, which drew AvatarDoc's fold arrows outside their panel. A plain test window
    /// never gets that class, so this test hosts the real editor the way the Inspector does.
    /// </summary>
    public class InspectorFoldTests
    {
        const string InspectorToggleClass = "unity-foldout__toggle--inspector";

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

        struct FoldPlace
        {
            public bool InspectorClass;
            public float Offset;
            public string Detail;
        }

        [Test]
        public void FoldArrowsSitInsideTheirPanelUnderTheInspector()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var grab = menu.AddEntry(EntryKind.Grabbable, "Cup");
            var selections = new[] { (string)null, hat.id, grab.id };

            var plain = new Dictionary<string, FoldPlace>();
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            LayoutProbe.Measure(view, () =>
            {
                foreach (var id in selections) Collect(view, id, plain);
            }, 360f, 900f);

            var inspected = new Dictionary<string, FoldPlace>();
            // The real editor builds its view on EditorPrefs; here it gets stores that never reach them.
            var oldViewPrefs = AvatarDocEditor.MakeViewPrefs;
            var oldTipStore = AvatarDocEditor.MakeTipStore;
            UnityEditor.Editor editor = null;
            try
            {
                AvatarDocEditor.MakeViewPrefs = () => new MemoryViewPrefs();
                AvatarDocEditor.MakeTipStore = () => new MemoryTipStore();
                editor = UnityEditor.Editor.CreateEditor(menu);
                var inspector = new InspectorElement(editor);
                LayoutProbe.Measure(inspector, () =>
                {
                    var hosted = inspector.Q<AvatarDoctorView>();
                    Assert.That(hosted, Is.Not.Null, "the InspectorElement built AvatarDoc's own editor");
                    foreach (var id in selections) Collect(hosted, id, inspected);
                }, 360f, 900f);
            }
            finally
            {
                AvatarDocEditor.MakeViewPrefs = oldViewPrefs;
                AvatarDocEditor.MakeTipStore = oldTipStore;
                if (editor != null) UnityEngine.Object.DestroyImmediate(editor);
            }

            Assert.That(inspected.Keys, Is.EquivalentTo(new[] { "ad-settings-fold-header", "ad-advanced-fold-toggle", "ad-grab-fold-toggle" }), "every AvatarDoc fold was measured");
            foreach (var pair in inspected)
            {
                var fold = pair.Value;
                Assert.That(fold.InspectorClass, Is.True, pair.Key + ": the test hosts the fold the way the Inspector does, so its toggle carries " + InspectorToggleClass);
                Assert.That(fold.Offset, Is.GreaterThanOrEqualTo(-1f), pair.Key + ": the arrow sits inside its panel, at or right of the panel's content edge. Inspector: " + fold.Detail + " Plain window: " + plain[pair.Key].Detail);
                Assert.That(fold.Offset, Is.LessThanOrEqualTo(plain[pair.Key].Offset + 1f), pair.Key + ": the arrow is not pushed further in than it sits outside the Inspector. Inspector: " + fold.Detail + " Plain window: " + plain[pair.Key].Detail);
            }
        }

        /// <summary>Selects the entry (or nothing), settles the layout and records every shown fold: whether its toggle carries the inspector class, and how far its arrow sits right of its panel's content edge.</summary>
        static void Collect(AvatarDoctorView view, string entryId, Dictionary<string, FoldPlace> into)
        {
            if (entryId == null) view.Tree.ClearSelection();
            else view.Tree.SelectEntry(entryId);
            LayoutProbe.Validate(view);
            foreach (var fold in view.Query<Foldout>().ToList())
            {
                if (fold.resolvedStyle.display == DisplayStyle.None || fold.worldBound.width <= 0f) continue;
                var toggle = fold.Q<Toggle>(className: Foldout.toggleUssClassName);
                var input = toggle?.Q(className: Foldout.inputUssClassName);
                var checkmark = toggle?.Q(className: Foldout.checkmarkUssClassName);
                Assert.That(checkmark, Is.Not.Null, fold.name + ": the fold has Unity's own toggle, input and arrow");
                VisualElement panel = null;
                VisualElement body = null;
                for (var parent = fold.parent; parent != null && panel == null; parent = parent.parent)
                {
                    if (body == null && parent.ClassListContains("ad-panel-body")) body = parent;
                    if (parent.ClassListContains("ad-panel")) panel = parent;
                }
                Assert.That(panel, Is.Not.Null, fold.name + " sits inside an AvatarDoc panel");
                float inner = panel.worldBound.xMin + panel.resolvedStyle.borderLeftWidth + panel.resolvedStyle.paddingLeft + (body != null ? body.resolvedStyle.paddingLeft : 0f);
                into[fold.name] = new FoldPlace
                {
                    InspectorClass = toggle.ClassListContains(InspectorToggleClass),
                    Offset = checkmark.worldBound.xMin - inner,
                    Detail = string.Format("arrow x {0:0.#}, panel content edge x {1:0.#}, toggle margin-left {2:0.#} padding-left {3:0.#}, input margin-left {4:0.#} padding-left {5:0.#}, arrow margin-left {6:0.#} width {7:0.#}, fold margin-left {8:0.#}.",
                        checkmark.worldBound.xMin, inner, toggle.resolvedStyle.marginLeft, toggle.resolvedStyle.paddingLeft,
                        input.resolvedStyle.marginLeft, input.resolvedStyle.paddingLeft, checkmark.resolvedStyle.marginLeft, checkmark.resolvedStyle.width, fold.resolvedStyle.marginLeft),
                };
            }
        }
    }
}
#endif
