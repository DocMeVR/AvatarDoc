#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The three ways into Add to AvatarDoc: a blend shape slider's right click, through Unity's public
    /// EditorApplication.contextualPropertyMenu; the mesh's component menu; and the menu of a material's header, which
    /// Unity draws under the selected mesh whatever the shader's own inspector is, so it works for Poiyomi too.
    /// </summary>
    [InitializeOnLoad]
    public static class AddToAvatarDocMenus
    {
        const string SkinnedItem = "CONTEXT/SkinnedMeshRenderer/Add to AvatarDoc...";
        const string MeshItem = "CONTEXT/MeshRenderer/Add to AvatarDoc...";
        const string MaterialItem = "CONTEXT/Material/Add to AvatarDoc...";

        /// <summary>Opens the window for a draft. Replaceable by a test, which reads the draft instead of opening a window.</summary>
        public static Action<AddDraft> Open = draft => AddToAvatarDocWindow.Show(draft);

        /// <summary>The objects selected in the Hierarchy, the active one first. Replaceable by a test, which never changes the real selection.</summary>
        public static Func<GameObject[]> Selected = () =>
        {
            var active = Selection.activeGameObject;
            var all = Selection.gameObjects;
            return active != null ? new[] { active }.Concat(all.Where(g => g != active)).ToArray() : all;
        };

        /// <summary>Where a failure inside Unity's right-click menu is reported, once for each place it goes to. Replaceable by a test that reads it.</summary>
        public static Action<Exception> Log = Debug.LogException;

        static readonly Regex WeightPath = new Regex(@"^m_BlendShapeWeights\.Array\.data\[(\d+)\]$");
        static Action<Exception> loggedTo;

        static AddToAvatarDocMenus()
        {
            EditorApplication.contextualPropertyMenu -= OnPropertyMenu;
            EditorApplication.contextualPropertyMenu += OnPropertyMenu;
        }

        /// <summary>
        /// Adds "AvatarDoc/Add "shape"..." for the weight of one blend shape of one skinned mesh, and nothing for any
        /// other property. It runs inside Unity's own menu, so a failure is caught and reported once: Unity's items and
        /// other tools' stay.
        /// </summary>
        public static void OnPropertyMenu(GenericMenu menu, SerializedProperty property)
        {
            try
            {
                if (menu == null || property == null) return;
                var match = WeightPath.Match(property.propertyPath ?? "");
                if (!match.Success) return;
                var serialized = property.serializedObject;
                if (serialized == null || serialized.targetObjects.Length != 1 || !(serialized.targetObject is SkinnedMeshRenderer renderer)) return;
                var mesh = renderer.sharedMesh;
                if (mesh == null || !int.TryParse(match.Groups[1].Value, out int index) || index >= mesh.blendShapeCount) return;
                var shape = mesh.GetBlendShapeName(index);
                // A slash in a menu text starts a submenu.
                var quoted = L.Quoted(shape.Replace('/', '\u2215'));
                if (AddToAvatarDoc.CanAdd(renderer))
                    menu.AddItem(new GUIContent("AvatarDoc/" + string.Format(L.Text("ad.ui.add_from.item"), quoted)), false, () => Open(AddToAvatarDoc.ForShape(renderer, shape)));
                else
                    menu.AddDisabledItem(new GUIContent("AvatarDoc/" + string.Format(L.Text("ad.ui.add_from.item.outside"), quoted)));
            }
            catch (Exception e)
            {
                if (loggedTo == Log) return;
                loggedTo = Log;
                Log(e);
            }
        }

        /// <summary>Whether the component menu's item can be chosen: a renderer that is part of an avatar in the scene.</summary>
        public static bool CanAddFrom(MenuCommand command) => command != null && command.context is Renderer renderer && AddToAvatarDoc.CanAdd(renderer);

        /// <summary>Opens the window for the renderer the component menu was opened on, with nothing picked yet.</summary>
        public static void AddFrom(MenuCommand command)
        {
            if (!CanAddFrom(command)) return;
            Open(AddToAvatarDoc.ForRenderer((Renderer)command.context));
        }

        /// <summary>The first renderer of the selected objects that uses the material and is part of an avatar in the scene, with the first slot it uses it in; null when there is none.</summary>
        public static (Renderer renderer, int slot)? RendererUsing(Material material, IEnumerable<GameObject> selected)
        {
            if (material == null || selected == null) return null;
            foreach (var go in selected)
            {
                if (go == null) continue;
                foreach (var renderer in go.GetComponents<Renderer>())
                {
                    if (!AddToAvatarDoc.CanAdd(renderer)) continue;
                    int slot = Array.IndexOf(renderer.sharedMaterials, material);
                    if (slot >= 0) return (renderer, slot);
                }
            }
            return null;
        }

        /// <summary>Whether the material header's item can be chosen: a selected mesh under an avatar uses the material.</summary>
        public static bool CanAddFromMaterial(MenuCommand command) => command != null && RendererUsing(command.context as Material, Selected()) != null;

        /// <summary>Opens the window for the selected mesh that uses the material, with that material's slot picked and the property left to pick.</summary>
        public static void AddFromMaterial(MenuCommand command)
        {
            var found = command != null ? RendererUsing(command.context as Material, Selected()) : null;
            if (found == null) return;
            Open(AddToAvatarDoc.ForSlot(found.Value.renderer, found.Value.slot));
        }

        [UnityEditor.MenuItem(SkinnedItem)]
        static void AddFromSkinnedMesh(MenuCommand command) => AddFrom(command);

        [UnityEditor.MenuItem(SkinnedItem, true)]
        static bool CanAddFromSkinnedMesh(MenuCommand command) => CanAddFrom(command);

        [UnityEditor.MenuItem(MeshItem)]
        static void AddFromMesh(MenuCommand command) => AddFrom(command);

        [UnityEditor.MenuItem(MeshItem, true)]
        static bool CanAddFromMesh(MenuCommand command) => CanAddFrom(command);

        [UnityEditor.MenuItem(MaterialItem)]
        static void AddFromMaterialHeader(MenuCommand command) => AddFromMaterial(command);

        [UnityEditor.MenuItem(MaterialItem, true)]
        static bool CanAddFromMaterialHeader(MenuCommand command) => CanAddFromMaterial(command);
    }
}
#endif
