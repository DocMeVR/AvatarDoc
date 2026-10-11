#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Rename, re-icon, hide, move and reset for one existing or foreign menu row. Every field goes through
    /// MenuEditing, with Undo, and writes to the row the panel was built for: the Name and Move to fields save when
    /// the focus leaves them, which can be the click that selects another row.
    /// </summary>
    public sealed class ExistingItemPanel : VisualElement
    {
        /// <summary>The name row EntryDetailView protects while it is focused, same as an own entry's.</summary>
        public NameStyleRow NameRow { get; }

        public ExistingItemPanel(EntryDetailView detail, AvatarDoctorView owner, TreeRow row)
        {
            var item = row.Item;
            var edit = item?.Edit;

            NameRow = new NameStyleRow(L.Text("ad.ui.name"), styled: true);
            NameRow.Load(CurrentName(item));
            NameRow.Changed += name => detail.SetExistingName(row, name);
            Add(Bound(NameRow));
            var original = OriginalName(item);
            if (original.Length > 0)
            {
                var originalNote = new Label(string.Format(L.Text("ad.ui.existing.original"), original));
                originalNote.AddToClassList("ad-note");
                originalNote.AddToClassList("ad-note-indent");
                Add(originalNote);
            }

            var iconField = new ObjectField(L.Text("ad.ui.icon")) { objectType = typeof(Texture2D), value = item?.Icon, tooltip = L.Text("ad.ui.icon.tip") };
            iconField.RegisterValueChangedCallback(e =>
            {
                if (MenuEditing.SetIcon(owner.Menu, row, e.newValue as Texture2D)) AvatarPlanCache.NoteSelfEdit(owner.Menu);
                AvatarPlanCache.InvalidateMenu(owner.Plan?.AvatarRoot);
                owner.Refresh();
            });
            Add(Bound(iconField));

            var moveRow = new VisualElement();
            moveRow.AddToClassList("ad-row");
            var moveField = new TextField(L.Text("ad.ui.existing.move")) { name = "ad-existing-move", isDelayed = true };
            moveField.SetValueWithoutNotify(edit != null && edit.move ? edit.targetPath : "");
            moveField.tooltip = L.Text("ad.ui.existing.move.tip");
            moveRow.Add(moveField);
            Button pick = null;
            pick = new Button(() => ShowMovePopup(pick, owner, row, moveField, detail));
            EntryDetailView.AddDropdownIcon(pick);
            moveRow.Add(pick);
            Add(moveRow);
            var moveNote = new Label { name = "ad-existing-move-note" };
            moveNote.AddToClassList("ad-note");
            moveNote.AddToClassList("ad-note-indent");
            moveNote.AddToClassList("ad-hidden");
            Add(moveNote);
            moveField.RegisterValueChangedCallback(e =>
            {
                if (detail.MoveExistingTo(row, e.newValue))
                {
                    moveNote.text = "";
                    moveNote.AddToClassList("ad-hidden");
                }
                else
                {
                    // The move was refused (a no-op back into the current parent, or a cycle): the field must not
                    // keep showing a value that was never actually applied.
                    moveField.SetValueWithoutNotify(e.previousValue);
                    moveNote.text = L.Text("ad.ui.existing.move_refused");
                    moveNote.RemoveFromClassList("ad-hidden");
                }
            });

            // The same "In menu" control an own entry's Hidden from Menu switch already uses, with the same
            // inverted value: Hidden and In menu off were the same idea under two names, so this row reads it and
            // writes it the same way that one does. Combined with Remove into one flags row, the same shape every
            // other pair of short, self captioned checkboxes in the inspector already uses.
            var flagsRow = new VisualElement();
            flagsRow.AddToClassList("ad-row");
            flagsRow.AddToClassList("ad-flags");
            var inMenuToggle = new Toggle(L.Text("ad.ui.in_menu")) { name = "ad-existing-in-menu", value = item == null || !item.Hidden, tooltip = L.Text("ad.ui.in_menu.tip") };
            // A removed item is out of the menu by that alone, so the switch would snap back on every click.
            inMenuToggle.SetEnabled(!(edit != null && edit.remove));
            inMenuToggle.RegisterValueChangedCallback(e => detail.SetExistingHidden(!e.newValue));
            flagsRow.Add(inMenuToggle);

            if (row.Kind != RowKind.Foreign)
            {
                var removeToggle = new Toggle(L.Text("ad.ui.existing.remove")) { name = "ad-existing-remove", value = edit != null && edit.remove };
                removeToggle.AddToClassList("ad-compact");
                removeToggle.tooltip = L.Text("ad.ui.existing.remove.tip");
                removeToggle.RegisterValueChangedCallback(e => detail.SetExistingRemoved(e.newValue));
                flagsRow.Add(removeToggle);
            }
            Add(flagsRow);

            if (row.Kind != RowKind.Foreign && edit != null && edit.remove)
                foreach (var line in ResultLines(owner, edit))
                {
                    var note = UiText.MessageLabel(line);
                    note.name = "ad-existing-remove-note";
                    note.AddToClassList("ad-note-indent");
                    Add(note);
                }

            var resetButton = new Button(detail.ResetExisting) { text = L.Text("ad.ui.existing.reset"), name = "ad-existing-reset", tooltip = L.Text("ad.ui.existing.reset.tip") };
            resetButton.SetEnabled(edit != null);
            Add(resetButton);

            if (row.Kind == RowKind.Foreign)
            {
                var note = new Label(string.Format(L.Text("ad.ui.existing.late_note"), row.SourceChip));
                note.AddToClassList("ad-note");
                Add(note);
            }

            if (row.Messages.Count > 0)
            {
                var messageBoxes = new VisualElement();
                EntryDetailView.AddMessageBoxes(messageBoxes, row.Messages);
                Add(messageBoxes);
            }
        }

        /// <summary>Offers "stay", the avatar root and every submenu path that would not be refused; picking one writes the move at once.</summary>
        static void ShowMovePopup(VisualElement anchor, AvatarDoctorView owner, TreeRow row, TextField moveField, EntryDetailView detail)
        {
            var edit = row.Item?.Edit;
            var popup = new List<AdMenu.Item>();
            popup.Add(new AdMenu.Item { Text = L.Text("ad.ui.existing.stay"), Checked = edit == null || !edit.move, Action = () =>
            {
                moveField.SetValueWithoutNotify("");
                detail.MoveExistingTo(row, null);
            } });
            if (!MenuEditing.RefusesMove(row, ""))
            {
                popup.Add(new AdMenu.Item { Text = L.Text("ad.ui.menu.move.root"), Checked = edit != null && edit.move && edit.targetPath.Length == 0, Action = () =>
                {
                    // detail.MoveExistingTo treats an empty string as "stay" (an empty text field), so the root
                    // (also an empty target path) has to go through MenuEditing directly.
                    moveField.SetValueWithoutNotify("");
                    if (MenuEditing.MoveTo(owner.Menu, row, "")) { AvatarPlanCache.NoteSelfEdit(owner.Menu); AvatarPlanCache.InvalidateOrder(owner.Plan?.AvatarRoot); owner.Refresh(); }
                } });
            }
            foreach (var path in HeaderView.PathChoices(owner.Plan, owner.Menu))
            {
                if (MenuEditing.RefusesMove(row, path)) continue;
                var target = path;
                popup.Add(new AdMenu.Item { Text = target.Replace("/", " › "), Checked = edit != null && edit.move && edit.targetPath == target, Action = () =>
                {
                    moveField.SetValueWithoutNotify(target);
                    detail.MoveExistingTo(row, target);
                } });
            }
            AdMenu.Open(anchor.worldBound, anchor, popup);
        }

        /// <summary>What this row's removal does, straight from the plan the build uses: what it frees first, then one line per parameter that has to stay and what keeps it.</summary>
        static List<string> ResultLines(AvatarDoctorView owner, MenuEdit edit)
        {
            var lines = new List<string>();
            var removal = owner.Plan?.Tree?.Removal;
            if (removal == null) return lines;
            var mine = removal.For(edit).ToList();
            var freed = mine.Where(p => p.Freed).ToList();
            if (freed.Count > 0)
            {
                var bits = UiText.Count(freed.Sum(p => p.Bits), "ad.ui.existing.remove_frees.bits.one", "ad.ui.existing.remove_frees.bits");
                var layers = UiText.Count(freed.SelectMany(p => p.Layers).Distinct().Count(), "ad.ui.existing.remove_frees.layers.one", "ad.ui.existing.remove_frees.layers");
                lines.Add(string.Format(L.Text("ad.ui.existing.remove_frees"), bits, layers));
            }
            foreach (var kept in mine.Where(p => !p.Freed))
                lines.Add(string.Format(L.Text("ad.ui.existing.remove_keeps"), kept.Name, L.Text(kept.KeptBecause)));
            return lines;
        }

        static VisualElement Bound(VisualElement field)
        {
            var row = new VisualElement();
            row.AddToClassList("ad-row");
            row.Add(field);
            return row;
        }

        /// <summary>The name to show: the edit's, when it renames the item, else the item's current rich name.</summary>
        internal static string CurrentName(MenuItem item)
        {
            if (item == null) return "";
            return !string.IsNullOrEmpty(item.Edit?.name) ? item.Edit.name : item.RichName;
        }

        /// <summary>The last segment of the item's original path: its name before any menu edit touched it.</summary>
        static string OriginalName(MenuItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.OriginalPath)) return "";
            var segments = item.OriginalPath.Split('/');
            return segments[segments.Length - 1];
        }
    }
}
#endif
