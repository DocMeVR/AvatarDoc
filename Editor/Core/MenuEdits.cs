#if DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Applies AvatarDoc.menuEdits to a built MenuTree: hide, rename, re-icon and move of items that already exist in the merged menu.</summary>
    public static class MenuEdits
    {
        public static void Apply(MenuTree tree, MenuEditMode mode = MenuEditMode.Build)
        {
            foreach (var component in tree.Components)
            {
                // The build never applies the edits of a switched off component, so neither does the tree: a
                // removal it holds must not free a parameter or a layer the built avatar keeps.
                if (MenuModel.IsDisabled(component)) continue;
                foreach (var edit in component.menuEdits)
                    Apply(tree, component, edit, mode);
            }
        }

        static void Apply(MenuTree tree, AvatarDoc component, MenuEdit edit, MenuEditMode mode)
        {
            if (edit.IsEmpty) return;
            if (!TryLocate(tree, edit, out var parent, out var item))
            {
                tree.PendingEdits.Add(NewPending(component, edit));
                return;
            }
            if (IsOwnEntry(item))
            {
                tree.Messages.Add(ModelMessage.Warning("ad.warn.edit_own_entry", SourceLabel(edit), component).ForEdit(edit.id));
                return;
            }

            var target = parent;
            if (edit.move)
            {
                var segments = MenuModel.SplitPath(edit.targetPath);
                if (item.Submenu != null && Reaches(tree.Root, segments, item.Submenu))
                {
                    // Left attached to the item (not applied) so the row can still show why the move did not happen.
                    item.Edit = edit;
                    tree.Messages.Add(ModelMessage.Error("ad.error.edit_cycle", SourceLabel(edit), component).ForEdit(edit.id));
                    return;
                }
                // Another tool may still install a submenu of this name, so the build waits for its last pass
                // rather than creating a second one beside it.
                if (mode == MenuEditMode.Build && !PathExists(tree.Root, segments))
                {
                    tree.PendingEdits.Add(NewPending(component, edit));
                    return;
                }
                target = tree.Root;
                foreach (var segment in segments)
                    target = MenuModel.FindOrCreateSubmenu(target, segment, segment, null, component, out _);
            }

            item.Edit = edit;
            // A removed control leaves the built menu exactly as a hidden one does; what else it takes with it is
            // RemovalPlanner's decision.
            if (edit.hide || edit.remove) item.Hidden = true;
            if (!string.IsNullOrEmpty(edit.name))
            {
                item.RichName = edit.name;
                item.Name = RichName.Strip(edit.name);
                if (item.Submenu != null)
                {
                    item.Submenu.RichName = edit.name;
                    item.Submenu.Name = item.Name;
                }
            }
            if (edit.icon != null)
            {
                item.Icon = edit.icon;
                if (item.Submenu != null) item.Submenu.Icon = edit.icon;
            }

            if (target != parent)
            {
                parent.Items.Remove(item);
                parent.Edited = true;
                target.Items.Insert(ClampPosition(edit.position, target.Items.Count), item);
            }
            else if (edit.position >= 0)
            {
                parent.Items.Remove(item);
                parent.Items.Insert(ClampPosition(edit.position, parent.Items.Count), item);
            }
        }

        /// <summary>A pending edit carrying everything a later pass needs, taken off the component while it is alive: the build destroys every AvatarDoc before those passes run.</summary>
        static PendingEdit NewPending(AvatarDoc component, MenuEdit edit)
        {
            var pending = new PendingEdit
            {
                Component = component,
                Edit = edit,
                ComponentName = component != null ? component.gameObject.name : "",
            };
#if DOCME_NDMF
            if (component != null) pending.Reference = nadena.dev.ndmf.ObjectRegistry.GetReference(component.gameObject);
#endif
            return pending;
        }

        /// <summary>True when a raw control or node name, stripped of styling, is exactly the given plain segment. Shared with MenuEditApplier so the model and the late passes agree on what a menu edit's source names match.</summary>
        internal static bool SegmentMatches(string plainSegment, string rawName) => plainSegment == RichName.Strip(rawName ?? "");

        /// <summary>Clamps an insert position into range, appending when negative or past the end. Shared with MenuEditApplier.</summary>
        internal static int ClampPosition(int position, int count) => position < 0 || position > count ? count : position;

        /// <summary>Controls and submenus a MenuEntry made. Those are edited directly, not through a menu edit.</summary>
        static bool IsOwnEntry(MenuItem item) => item.Control != null || (item.Submenu != null && item.Submenu.EntryId != null);

        /// <summary>True when every segment of targetPath already names a submenu, so a move into it creates nothing. An empty path is the root, which always exists.</summary>
        static bool PathExists(MenuNode root, string[] targetPath)
        {
            var current = root;
            foreach (var segment in targetPath)
            {
                var found = current.Items.FirstOrDefault(i => i.Submenu != null && SegmentMatches(segment, i.Submenu.Name))?.Submenu;
                if (found == null) return false;
                current = found;
            }
            return true;
        }

        /// <summary>True when walking targetPath from the root, using only submenus that already exist, would step into or through moving.</summary>
        static bool Reaches(MenuNode root, string[] targetPath, MenuNode moving)
        {
            var current = root;
            if (current == moving) return true;
            foreach (var segment in targetPath)
            {
                var found = current.Items.FirstOrDefault(i => i.Submenu != null && SegmentMatches(segment, i.Submenu.Name))?.Submenu;
                if (found == null) return false;
                current = found;
                if (current == moving) return true;
            }
            return false;
        }

        static bool TryLocate(MenuTree tree, MenuEdit edit, out MenuNode parent, out MenuItem item)
        {
            var targetPath = string.Join("/", edit.source ?? new List<string>());
            int seen = 0;
            return TryLocate(tree.Root, targetPath, edit.occurrence, ref seen, out parent, out item);
        }

        static bool TryLocate(MenuNode node, string targetPath, int occurrence, ref int seen, out MenuNode parent, out MenuItem item)
        {
            foreach (var candidate in node.Items)
            {
                if (candidate.OriginalPath == targetPath)
                {
                    if (seen == occurrence) { parent = node; item = candidate; return true; }
                    seen++;
                }
                if (candidate.Submenu != null && TryLocate(candidate.Submenu, targetPath, occurrence, ref seen, out parent, out item))
                    return true;
            }
            parent = null;
            item = null;
            return false;
        }

        /// <summary>The item a menu edit would touch, or null when its source does not resolve.</summary>
        public static MenuItem Find(MenuTree tree, MenuEdit edit) => TryLocate(tree, edit, out _, out var item) ? item : null;

        /// <summary>The plain path an item had in its original place, as a menu edit's source list.</summary>
        public static List<string> SourceOf(MenuItem item) => string.IsNullOrEmpty(item.OriginalPath) ? new List<string>() : item.OriginalPath.Split('/').ToList();

        static string SourceLabel(MenuEdit edit) => string.Join("/", edit.source ?? new List<string>());
    }
}
#endif
