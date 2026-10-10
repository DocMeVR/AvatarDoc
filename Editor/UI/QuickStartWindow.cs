#if DOCME_NDMF && DOCME_VRCSDK3A
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>A small window holding just the README's Quick start section, read only, scrollable.</summary>
    public sealed class QuickStartWindow : EditorWindow
    {
        // A script reload keeps the window and what is serialized in it, but not its panel.
        [SerializeField] string text = "";

        public static void Show(string text)
        {
            var window = CreateInstance<QuickStartWindow>();
            window.titleContent = new GUIContent(L.Text("ad.ui.welcome.quickstart"));
            window.minSize = new Vector2(360, 240);
            window.Init(text);
            window.ShowUtility();
        }

        /// <summary>Gives the window its text and builds the panel. A test builds a window this way instead of showing a real one.</summary>
        public void Init(string text)
        {
            this.text = text ?? "";
            Build();
        }

        /// <summary>Unity asks for the panel when the window is first shown and again after every script reload.</summary>
        public void CreateGUI() => Build();

        void Build()
        {
            var root = rootVisualElement;
            root.Clear();
            var uss = AvatarDoctorView.LoadStyleSheet();
            if (uss != null && !root.styleSheets.Contains(uss)) root.styleSheets.Add(uss);
            root.AddToClassList("ad-window");
            var field = new TextField { value = text, multiline = true, isReadOnly = true };
            field.AddToClassList("ad-summary-text");
            field.style.flexGrow = 1;
            root.Add(field);
        }
    }
}
#endif
