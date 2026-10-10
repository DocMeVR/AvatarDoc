#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Searchable dropdown of every blendshape on every skinned mesh under the avatar, grouped by renderer.</summary>
    public sealed class ShapePicker : AdvancedDropdown
    {
        sealed class ShapeItem : AdvancedDropdownItem
        {
            public readonly SkinnedMeshRenderer Renderer;
            public readonly string Shape;
            public ShapeItem(SkinnedMeshRenderer renderer, string shape) : base(shape) { Renderer = renderer; Shape = shape; }
        }

        readonly Transform avatarRoot;
        readonly Action<SkinnedMeshRenderer, string> onPick;

        ShapePicker(Transform avatarRoot, Action<SkinnedMeshRenderer, string> onPick) : base(new AdvancedDropdownState())
        {
            this.avatarRoot = avatarRoot;
            this.onPick = onPick;
            minimumSize = new Vector2(260f, 320f);
        }

        /// <summary>Swapped in tests so opening the picker never shows a real dropdown window.</summary>
        public static Action<Rect, Transform, Action<SkinnedMeshRenderer, string>> ShowOverride;

        public static void Show(Rect anchor, Transform avatarRoot, Action<SkinnedMeshRenderer, string> onPick)
        {
            if (ShowOverride != null)
            {
                ShowOverride(anchor, avatarRoot, onPick);
                return;
            }
            new ShapePicker(avatarRoot, onPick).Show(anchor);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem(L.Text("ad.ui.pick.shape"));
            int found = 0;
            if (avatarRoot != null)
            {
                foreach (var renderer in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mesh = renderer.sharedMesh;
                    if (mesh == null || mesh.blendShapeCount == 0) continue;
                    var group = new AdvancedDropdownItem(renderer.name);
                    for (int i = 0; i < mesh.blendShapeCount; i++) group.AddChild(new ShapeItem(renderer, mesh.GetBlendShapeName(i)));
                    root.AddChild(group);
                    found++;
                }
            }
            if (found == 0) root.AddChild(new AdvancedDropdownItem(L.Text("ad.ui.pick.renderer")) { enabled = false });
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is ShapeItem shape) onPick(shape.Renderer, shape.Shape);
        }
    }
}
#endif
