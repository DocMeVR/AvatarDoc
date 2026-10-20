#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Every blend shape on every skinned mesh under the avatar, in an AdMenu with a search field: a group per mesh, or the shapes themselves when only one mesh has any.</summary>
    public static class ShapePicker
    {
        /// <summary>Swapped in tests so opening the picker never opens a real menu.</summary>
        public static Action<Rect, Transform, Action<SkinnedMeshRenderer, string>> ShowOverride;

        /// <summary>For callers outside AvatarDoc, which hand over only the button's world rectangle: the button is looked for with AdMenu.FindTarget.</summary>
        public static void Show(Rect anchor, Transform avatarRoot, Action<SkinnedMeshRenderer, string> onPick)
        {
            if (ShowOverride != null)
            {
                ShowOverride(anchor, avatarRoot, onPick);
                return;
            }
            Open(anchor, AdMenu.FindTarget(anchor), avatarRoot, onPick);
        }

        /// <summary>Opens the picker on the control that asked for it.</summary>
        internal static void Show(VisualElement anchor, Transform avatarRoot, Action<SkinnedMeshRenderer, string> onPick) =>
            Show(anchor.worldBound, anchor, avatarRoot, onPick);

        /// <summary>Opens the picker under anchor in the window that hosts target, for a control that may be gone by now (target is then what outlives it).</summary>
        internal static void Show(Rect anchor, VisualElement target, Transform avatarRoot, Action<SkinnedMeshRenderer, string> onPick)
        {
            if (ShowOverride != null)
            {
                ShowOverride(anchor, avatarRoot, onPick);
                return;
            }
            Open(anchor, target, avatarRoot, onPick);
        }

        /// <summary>Opens the picker under anchor in the window that hosts target. Null when target is not on a panel.</summary>
        public static AdMenu Open(Rect anchor, VisualElement target, Transform avatarRoot, Action<SkinnedMeshRenderer, string> onPick) =>
            AdMenu.Open(anchor, target, Items(avatarRoot, onPick), search: true);

        /// <summary>One group per skinned mesh with shapes, named after its renderer, in hierarchy order, each mesh's shapes in mesh order.</summary>
        public static List<AdMenu.Item> Items(Transform avatarRoot, Action<SkinnedMeshRenderer, string> onPick)
        {
            var groups = new List<AdMenu.Item>();
            if (avatarRoot != null)
            {
                foreach (var renderer in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mesh = renderer.sharedMesh;
                    if (mesh == null || mesh.blendShapeCount == 0) continue;
                    var owner = renderer;
                    groups.Add(new AdMenu.Item { Text = renderer.name, Children = ItemsOf(renderer, shape => onPick(owner, shape)) });
                }
            }
            if (groups.Count == 0) return new List<AdMenu.Item> { new AdMenu.Item { Text = L.Text("ad.ui.pick.renderer"), Disabled = true } };
            return groups.Count == 1 ? groups[0].Children : groups;
        }

        /// <summary>One skinned mesh's own shapes, in mesh order, and none of the meshes under it.</summary>
        public static List<AdMenu.Item> ItemsOf(SkinnedMeshRenderer renderer, Action<string> onPick)
        {
            var mesh = renderer != null ? renderer.sharedMesh : null;
            var shapes = new List<AdMenu.Item>(mesh != null ? mesh.blendShapeCount : 0);
            if (mesh == null) return shapes;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                var shape = mesh.GetBlendShapeName(i);
                shapes.Add(new AdMenu.Item { Text = shape, Action = () => onPick(shape) });
            }
            return shapes;
        }
    }
}
#endif
