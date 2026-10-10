#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using UnityEditor;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>A one field popup for typing a shader property name by hand, for a property the renderer's shaders do not list.</summary>
    public sealed class PropertyNamePopup : PopupWindowContent
    {
        readonly Action<string> onCommit;
        string text;
        bool committed;

        public PropertyNamePopup(string startingText, Action<string> onCommit)
        {
            this.onCommit = onCommit;
            text = startingText ?? "";
        }

        public static void Show(Rect anchor, string startingText, Action<string> onCommit)
        {
            PopupWindow.Show(anchor, new PropertyNamePopup(startingText, onCommit));
        }

        public override Vector2 GetWindowSize() => new Vector2(220f, 40f);

        public override void OnGUI(Rect rect)
        {
            GUI.SetNextControlName("ad-property-other-field");
            text = EditorGUILayout.TextField(L.Text("ad.ui.property.other"), text);
            GUI.FocusControl("ad-property-other-field");
            var current = Event.current;
            if (current.type == EventType.KeyDown && (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter))
            {
                Commit();
                current.Use();
                editorWindow.Close();
            }
        }

        public override void OnClose() => Commit();

        void Commit()
        {
            if (committed) return;
            committed = true;
            onCommit(text.Trim());
        }
    }
}
#endif
