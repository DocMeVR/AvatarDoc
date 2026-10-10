#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Rename, re-icon, hide, move and reset for one existing or foreign menu row, stored as a MenuEdit on some AvatarDoc of the avatar. Every operation records Undo exactly once.</summary>
    public static class MenuEditing
    {
        /// <summary>The menu edit for this row, wherever on the avatar it lives. With create true, makes one on menu when none is found. Refuses a row that is not existing or foreign: those are edited directly, not through a menu edit.</summary>
        public static MenuEdit EditFor(AvatarDoc menu, TreeRow row, bool create)
        {
            var found = Locate(menu, row, out var occurrence);
            if (found != null) return found;
            if (!create || !IsEligible(menu, row) || EntryEditing.Refuses(menu)) return null;

            Undo.RecordObject(menu, "Edit menu item");
            var edit = MenuEdit.Create(MenuEdits.SourceOf(row.Item), occurrence);
            menu.menuEdits.Add(edit);
            EntryEditing.MarkChanged(menu);
            return edit;
        }

        public static bool SetHidden(AvatarDoc menu, TreeRow row, bool hidden)
            => Apply(menu, row, hidden ? "Hide menu item" : "Show menu item", edit => edit.hide = hidden);

        /// <summary>
        /// Removes the row from the avatar, or puts it back. Returns false, changing nothing, for a foreign row:
        /// the parameters and layers of a Modular Avatar or VRCFury item do not exist yet when AvatarDoctor builds,
        /// so there is nothing here to free.
        /// </summary>
        public static bool SetRemoved(AvatarDoc menu, TreeRow row, bool removed)
        {
            if (row != null && row.Kind == RowKind.Foreign) return false;
            return Apply(menu, row, removed ? "Remove menu item" : "Keep menu item", edit => edit.remove = removed);
        }

        public static bool SetName(AvatarDoc menu, TreeRow row, string richName)
            => Apply(menu, row, "Rename menu item", edit => edit.name = richName ?? "");

        public static bool SetIcon(AvatarDoc menu, TreeRow row, Texture2D icon)
            => Apply(menu, row, "Change menu icon", edit => edit.icon = icon);

        /// <summary>
        /// Moves the row into the submenu at targetPath, empty for the root; null cancels a move, leaving the item
        /// where it is. Refuses (and changes nothing) a move that would put the row back where it already is, or,
        /// for a submenu row, into itself or one of its own descendants.
        /// </summary>
        public static bool MoveTo(AvatarDoc menu, TreeRow row, string targetPath, int position = -1)
        {
            if (targetPath != null && RefusesMove(row, targetPath)) return false;
            return Apply(menu, row, "Move menu item", edit =>
            {
                edit.move = targetPath != null;
                edit.targetPath = targetPath ?? "";
                edit.position = position;
            });
        }

        /// <summary>True when moving row to targetPath would be refused: a no-op back into the row's current parent, or, for a submenu row, into itself or one of its own descendants. Plain segments are compared, not raw strings.</summary>
        public static bool RefusesMove(TreeRow row, string targetPath)
        {
            if (row == null || targetPath == null) return false;
            var targetSegments = PlainSegments(targetPath);
            var currentParentPath = row.ParentRow?.Node?.Path ?? "";
            if (currentParentPath.Length > 0 && targetSegments.SequenceEqual(PlainSegments(currentParentPath))) return true;
            if (row.Node != null)
            {
                var ownSegments = PlainSegments(row.Node.Path);
                if (targetSegments.Length >= ownSegments.Length && ownSegments.SequenceEqual(targetSegments.Take(ownSegments.Length))) return true;
            }
            return false;
        }

        /// <summary>Removes the row's edit from whichever AvatarDoc holds it. False when the row has none or its holder is locked.</summary>
        public static bool Reset(AvatarDoc menu, TreeRow row)
        {
            var edit = row?.Item?.Edit;
            if (edit == null) return false;
            var owner = OwnerOf(menu, edit);
            if (EntryEditing.Refuses(owner)) return false;
            Undo.RecordObject(owner, "Reset menu item");
            owner.menuEdits.Remove(edit);
            EntryEditing.MarkChanged(owner);
            return true;
        }

        /// <summary>
        /// Removes the edit with this id from whichever AvatarDoc of the avatar holds it. Used for an edit whose
        /// source no longer exists, which has no row to reset. A removedLayers entry that names the edit is left
        /// alone: the layer it left out of the build belongs to the converted entry, not to this edit.
        /// </summary>
        public static bool RemoveById(AvatarDoc menu, string editId)
        {
            if (menu == null || string.IsNullOrEmpty(editId)) return false;
            foreach (var component in AllComponentsOf(menu))
            {
                var edit = component.menuEdits.FirstOrDefault(e => e.id == editId);
                if (edit == null) continue;
                if (EntryEditing.Refuses(component)) return false;
                Undo.RecordObject(component, "Remove menu edit");
                component.menuEdits.Remove(edit);
                EntryEditing.MarkChanged(component);
                return true;
            }
            return false;
        }

        /// <summary>Finds or creates the row's edit and applies mutate to it, recording Undo once on whichever AvatarDoc ends up holding it, then removes it again if it became empty. False, writing nothing, for a row that takes no edit or a locked holder.</summary>
        static bool Apply(AvatarDoc menu, TreeRow row, string label, Action<MenuEdit> mutate)
        {
            if (!IsEligible(menu, row)) return false;
            var existing = Locate(menu, row, out var occurrence);
            var owner = existing != null ? OwnerOf(menu, existing) : menu;
            if (EntryEditing.Refuses(owner)) return false;
            Undo.RecordObject(owner, label);
            var edit = existing ?? MenuEdit.Create(MenuEdits.SourceOf(row.Item), occurrence);
            if (existing == null) owner.menuEdits.Add(edit);
            mutate(edit);
            if (edit.IsEmpty) owner.menuEdits.Remove(edit);
            EntryEditing.MarkChanged(owner);
            return true;
        }

        static bool IsEligible(AvatarDoc menu, TreeRow row)
            => menu != null && row != null && (row.Kind == RowKind.Existing || row.Kind == RowKind.Foreign) && row.Item != null;

        /// <summary>The row's existing edit, wherever on the avatar it lives, and the occurrence a new one would need. Null when the row carries none yet, or is not existing or foreign.</summary>
        static MenuEdit Locate(AvatarDoc menu, TreeRow row, out int occurrence)
        {
            occurrence = 0;
            if (!IsEligible(menu, row)) return null;
            var plan = AvatarPlanCache.GetFor(menu);
            occurrence = OccurrenceOf(plan.Tree, row.Item);
            return Find(menu, row.Item.OriginalPath ?? "", occurrence);
        }

        static MenuEdit Find(AvatarDoc menu, string originalPath, int occurrence)
        {
            foreach (var component in AllComponentsOf(menu))
                foreach (var edit in component.menuEdits)
                    if (edit.occurrence == occurrence && string.Join("/", edit.source ?? new List<string>()) == originalPath)
                        return edit;
            return null;
        }

        static AvatarDoc OwnerOf(AvatarDoc menu, MenuEdit edit)
        {
            foreach (var component in AllComponentsOf(menu))
                if (component.menuEdits.Contains(edit)) return component;
            return menu;
        }

        /// <summary>The component being edited first, then the others on the avatar. A disabled other is left out: the tree never applies its edits, so one found there would change nothing anyone can see, and would come back when it is enabled.</summary>
        static IEnumerable<AvatarDoc> AllComponentsOf(AvatarDoc menu)
        {
            yield return menu;
            foreach (var other in AvatarDoctorView.OthersOf(menu))
                if (!MenuModel.IsDisabled(other)) yield return other;
        }

        /// <summary>The occurrence number stamped on the item when its model was built. Reading it from the item, rather than looking the item up in the current tree, keeps a row from an older plan correct.</summary>
        internal static int OccurrenceOf(MenuTree tree, MenuItem target) => target != null ? target.Occurrence : 0;

        static string[] PlainSegments(string path) => MenuModel.SplitPath(path).Select(RichName.Strip).ToArray();
    }
}
#endif
