#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Linq;
using nadena.dev.ndmf.runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>A dockable host for the same view the inspector shows, following the selected avatar.</summary>
    public sealed class AvatarDoctorWindow : EditorWindow
    {
        [UnityEditor.MenuItem("Tools/DocMe/AvatarDoc")]
        public static void Open() => GetWindow<AvatarDoctorWindow>("AvatarDoc");

        AvatarDoc current;
        AvatarDoctorView view;

        void OnEnable()
        {
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.hierarchyChanged += OnSelectionChanged;
            L.LanguageChanged += OnLanguageChanged;
            Rebuild();
        }

        void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.hierarchyChanged -= OnSelectionChanged;
            L.LanguageChanged -= OnLanguageChanged;
        }

        /// <summary>A view on show builds itself again; the note shown when nothing is selected is built here.</summary>
        void OnLanguageChanged()
        {
            if (view == null) Rebuild();
        }

        void OnSelectionChanged()
        {
            bool dragging = view != null && view.Tree.Dragging;
            if (view == null || ShouldSwitch(current, Selection.activeGameObject, dragging)) Rebuild();
        }

        /// <summary>Whether a selection change means another component. Selecting an asset resolves to nothing, and that keeps the component already on show along with its selection, filter and collapsed rows.</summary>
        public static bool ShouldSwitch(AvatarDoc current, AvatarDoc next)
        {
            if (current == null) return true;
            return next != null && next != current;
        }

        /// <summary>
        /// The window's real switching decision. Extends the identity check above with two more rules: a plain
        /// object that carries no AvatarDoc of its own is not itself a reason to switch when it belongs to the same
        /// avatar as the component on show, since Resolve's fallback search can land on a different component of
        /// that avatar purely because it happened to find it first; and no selection ever switches the window while
        /// a row drag holds the tree's pointer, so dragging a Hierarchy object over the window cannot pull the
        /// panel away mid drag.
        /// </summary>
        public static bool ShouldSwitch(AvatarDoc current, GameObject selected, bool dragging)
        {
            if (dragging) return false;
            if (current == null) return true;
            if (selected == null) return false;
            var own = selected.GetComponent<AvatarDoc>();
            if (own != null) return own != current;
            var selectedRoot = RuntimeUtil.FindAvatarInParents(selected.transform);
            var currentRoot = RuntimeUtil.FindAvatarInParents(current.transform);
            return selectedRoot == null || currentRoot == null || selectedRoot.gameObject != currentRoot.gameObject;
        }

        /// <summary>The component to show for a selected object: its own, else the first one under its avatar, else the first under the object itself.</summary>
        public static AvatarDoc Resolve(GameObject selected)
        {
            if (selected == null) return null;
            var own = selected.GetComponent<AvatarDoc>();
            if (own != null) return own;
            var root = RuntimeUtil.FindAvatarInParents(selected.transform);
            var scope = root != null ? root.gameObject : selected;
            return scope.GetComponentsInChildren<AvatarDoc>(true).FirstOrDefault();
        }

        void Rebuild()
        {
            current = Resolve(Selection.activeGameObject);
            rootVisualElement.Clear();
            view = null;
            if (current == null)
            {
                var note = new Label(L.Text("ad.ui.window.none"));
                note.AddToClassList("ad-note");
                // The view brings its own font; the note is built again on every language change.
                LanguageFont.Apply(note);
                rootVisualElement.Add(note);
                return;
            }
            view = new AvatarDoctorView(new SerializedObject(current), current, wide: true, viewPrefs: new EditorPrefsViewPrefs());
            view.TipStore = new EditorPrefsTipStore();
            rootVisualElement.Add(view);
        }
    }
}
#endif
