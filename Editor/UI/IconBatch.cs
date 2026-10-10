#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Captures icons for every entry of a component that has none yet, one Undo group for the whole batch.</summary>
    public static class IconBatch
    {
        const int Size = 256;
        static readonly Color Transparent = new Color(0f, 0f, 0f, 0f);
        static readonly HashSet<EntryKind> Capturable = new HashSet<EntryKind> { EntryKind.Toggle, EntryKind.Radial, EntryKind.Submenu, EntryKind.Preset };

        /// <summary>Captures an icon for each entry without one that has pieces to show. Frames from the Scene view when one is active, else a synthesized front view. Returns how many icons were made.</summary>
        public static int CaptureMissing(AvatarDoc menu, MenuTree tree, Transform avatarRoot, IEnumerable<MenuEntry> only = null)
        {
            if (EntryEditing.Refuses(menu) || tree == null) return 0;

            var candidates = new List<(MenuEntry Entry, List<Renderer> Targets)>();
            foreach (var entry in only ?? menu.entries)
            {
                if (entry == null || entry.icon != null || !Capturable.Contains(entry.kind)) continue;
                if (!menu.entries.Contains(entry)) continue;
                var targets = IconCapture.TargetsOf(entry, tree, avatarRoot);
                if (targets.Count == 0) continue;
                candidates.Add((entry, targets));
            }
            if (candidates.Count == 0) return 0;

            bool hasSceneView = SceneView.lastActiveSceneView != null && SceneView.lastActiveSceneView.camera != null;
            string avatarName = avatarRoot != null ? avatarRoot.name : menu.gameObject.name;
            int made = 0;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Capture missing icons");
            try
            {
                foreach (var (entry, targets) in candidates)
                {
                    var bounds = IconCapture.BoundsOf(targets);
                    var baseFrame = hasSceneView ? IconCapture.FromSceneView() : Synthesized(avatarRoot, bounds);
                    var frame = IconCapture.Fit(baseFrame, bounds);
                    var texture = IconCapture.RenderFitted(frame, targets, Size, Transparent);
                    try
                    {
                        var asset = IconCapture.Save(texture, avatarName, entry.PlainName);
                        IconCapture.Assign(menu, entry, asset);
                        made++;
                    }
                    finally
                    {
                        Object.DestroyImmediate(texture);
                    }
                }
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
            }
            return made;
        }

        /// <summary>A camera on the avatar's forward axis, in front of the bounds, looking back at their centre.</summary>
        static IconCapture.Frame Synthesized(Transform avatarRoot, Bounds bounds)
        {
            var forward = avatarRoot != null ? avatarRoot.forward : Vector3.forward;
            var rotation = Quaternion.LookRotation(-forward, Vector3.up);
            return new IconCapture.Frame { Position = bounds.center + forward, Rotation = rotation, FieldOfView = 30f };
        }
    }
}
#endif
