#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    [CustomEditor(typeof(AvatarDoc))]
    public sealed class AvatarDocEditor : UnityEditor.Editor
    {
        /// <summary>Where the Inspector's view keeps its fold and expansion state. A test swaps in a store that never reaches the real EditorPrefs.</summary>
        public static Func<IViewPrefs> MakeViewPrefs = () => new EditorPrefsViewPrefs();

        /// <summary>Where the Inspector's view reads which first-use tips were dismissed. A test swaps in a store that never reaches the real EditorPrefs.</summary>
        public static Func<ITipStore> MakeTipStore = () => new EditorPrefsTipStore();

        public override VisualElement CreateInspectorGUI()
        {
            var view = new AvatarDoctorView(serializedObject, (AvatarDoc)target, viewPrefs: MakeViewPrefs());
            view.TipStore = MakeTipStore();
            return view;
        }
    }
}
#endif
