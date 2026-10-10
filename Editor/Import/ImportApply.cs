#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Creates AvatarDoc entries from chosen import candidates, and optionally removes the source components.</summary>
    public static class ImportApply
    {
        public static List<MenuEntry> Apply(AvatarDoc menu, IReadOnlyList<ImportCandidate> chosen, bool removeOriginals)
        {
            var created = new List<MenuEntry>();
            if (EntryEditing.Refuses(menu) || chosen == null || chosen.Count == 0) return created;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Import into AvatarDoc");

            var converted = ConvertedLayers(menu);
            bool changed = false;
            // The merged menu as the scan saw it, so a foreign candidate can be matched to the row it came from.
            var tree = removeOriginals && AvatarPlanCache.RootOf(menu) != null ? AvatarPlanCache.GetFor(menu).Tree : null;

            try
            {
                foreach (var batch in Batches(chosen))
                {
                    // A group stands or falls together: an entry created while a sibling was refused would leave
                    // the layer the sibling should have replaced out of the merge and driving nothing at all.
                    var refusal = RefusalFor(menu, tree, batch, removeOriginals);
                    if (refusal != null)
                    {
                        foreach (var member in batch) { member.Supported = false; member.Reason = refusal; }
                        continue;
                    }

                    Undo.IncrementCurrentGroup();
                    int batchGroup = Undo.GetCurrentGroup();
                    var madeHere = new List<MenuEntry>();
                    try
                    {
                        changed |= ApplyBatch(menu, tree, batch, removeOriginals, converted, madeHere);
                    }
                    catch
                    {
                        Undo.RevertAllDownToGroup(batchGroup);
                        throw;
                    }
                    created.AddRange(madeHere);
                }
            }
            finally
            {
                // Recorded once everything is written: on a prefab instance a modification made after the record
                // would not be kept as an override, and the lists would come back on the next reload.
                if (changed) EntryEditing.MarkChanged(menu);
                Undo.CollapseUndoOperations(group);
            }

            return created;
        }

        /// <summary>
        /// The chosen candidates plus every candidate that belongs to the same group. Candidates of one Merge
        /// Animator only make sense together: the entries that replace its layers and the merge that keeps the
        /// rest are one decision, so choosing any of them chooses all of them, and a member that cannot be
        /// converted is a reason to convert none of them. The merged animators go last, since they carry the
        /// components the whole set is installed by.
        /// </summary>
        static List<ImportCandidate> Expand(IReadOnlyList<ImportCandidate> chosen)
        {
            var seen = new HashSet<ImportCandidate>();
            var result = new List<ImportCandidate>();
            foreach (var candidate in chosen)
                if (candidate != null && seen.Add(candidate)) result.Add(candidate);
            foreach (var candidate in chosen)
            {
                if (candidate == null || candidate.GroupMembers == null) continue;
                foreach (var member in candidate.GroupMembers)
                    if (member != null && seen.Add(member)) result.Add(member);
            }
            return result.OrderBy(c => c.MergeDraft != null ? 1 : 0).ToList();
        }

        /// <summary>The expanded candidates split into what has to succeed or fail together: one list per group, and one of its own for every candidate that belongs to no group.</summary>
        static List<List<ImportCandidate>> Batches(IReadOnlyList<ImportCandidate> chosen)
        {
            var batches = new List<List<ImportCandidate>>();
            var byGroup = new Dictionary<string, List<ImportCandidate>>();
            foreach (var candidate in Expand(chosen))
            {
                if (string.IsNullOrEmpty(candidate.Group)) { batches.Add(new List<ImportCandidate> { candidate }); continue; }
                if (!byGroup.TryGetValue(candidate.Group, out var list))
                {
                    byGroup[candidate.Group] = list = new List<ImportCandidate>();
                    batches.Add(list);
                }
                list.Add(candidate);
            }
            return batches;
        }

        /// <summary>Why this batch cannot be applied at all, checked before anything is written. Null when it can.</summary>
        static string RefusalFor(AvatarDoc menu, MenuTree tree, List<ImportCandidate> batch, bool removeOriginals)
        {
            bool grouped = batch.Count > 0 && !string.IsNullOrEmpty(batch[0].Group);
            // Leaving the Merge Animator in place would merge its controller a second time, on top of the entries
            // that replaced its layers.
            if (grouped && !removeOriginals) return L.Text("ad.import.reason.group_needs_removal");
            foreach (var candidate in batch)
            {
                if (!candidate.Supported) return Spoken(candidate.Reason);
                if (candidate.Draft == null) continue;
                AvatarDoc editOwner = null;
                var carried = removeOriginals ? FindEdit(menu, tree, candidate, out editOwner) : null;
                // The edit that would come over with this item sits on a component its maker locked. Importing
                // anyway would strand it there, still hiding or renaming a control that is now an entry of ours.
                if (carried != null && EntryEditing.Refuses(editOwner)) return L.Text("ad.import.reason.edit_locked");
            }
            return null;
        }

        static string Spoken(string reason) => string.IsNullOrEmpty(reason) ? L.Text("ad.import.reason.no_supported_action") : reason;

        /// <summary>Writes one batch: its entries first, then the merged animator that keeps whatever they did not take.</summary>
        static bool ApplyBatch(AvatarDoc menu, MenuTree tree, List<ImportCandidate> batch, bool removeOriginals,
            HashSet<string> converted, List<MenuEntry> created)
        {
            bool changed = false;
            // Kept as it is: nothing of this set is converted, so every layer stays in the merge and the items
            // that could have become real entries stay plain controls on the parameters they already drove.
            bool keep = batch.Any(c => c.KeepAsItIs);
            // The layers the entries of this batch take over, and the parameters those layers used to read.
            var skipped = keep
                ? new List<string>()
                : batch.Where(c => c.Draft != null && !string.IsNullOrEmpty(c.MergedLayer)).Select(c => c.MergedLayer).Distinct().ToList();
            var replaced = keep
                ? new HashSet<string>()
                : new HashSet<string>(batch
                    .Where(c => c.Draft != null && !string.IsNullOrEmpty(c.MergedLayer) && !string.IsNullOrEmpty(c.MergedParameter))
                    .Select(c => c.MergedParameter));
            var installed = new List<MenuAssetInstall>();
            foreach (var candidate in batch)
            {
                if (candidate.MenuAssetDraft != null || (candidate.ParameterDrafts != null && candidate.ParameterDrafts.Count > 0))
                {
                    Undo.RecordObject(menu, "Import into AvatarDoc");
                    if (candidate.MenuAssetDraft != null)
                    {
                        var copy = CopyOfInstall(candidate.MenuAssetDraft);
                        menu.menuAssets.Add(copy);
                        installed.Add(copy);
                    }
                    foreach (var declaration in candidate.ParameterDrafts)
                    {
                        if (declaration == null || menu.parameters.Any(p => p != null && p.name == declaration.name)) continue;
                        if (Obsolete(declaration.name, replaced, skipped, batch)) continue;
                        menu.parameters.Add(CopyOfDeclaration(declaration));
                    }
                    changed = true;
                    RemoveOrigins(candidate, removeOriginals);
                    continue;
                }

                if (candidate.ShapesDraft != null)
                {
                    ApplyShapes(candidate.ShapesDraft);
                    RemoveOrigins(candidate, removeOriginals);
                    continue;
                }

                if (candidate.MergeDraft != null)
                {
                    // What the merge leaves out is read back from the entries this batch really created, never
                    // from what the scan hoped for.
                    var skip = new List<string>(skipped);
                    if (!SkipsEverything(candidate.MergeDraft.controller, skip))
                    {
                        Undo.RecordObject(menu, "Import into AvatarDoc");
                        menu.animators.Add(CopyOfMerge(candidate.MergeDraft, skip));
                        changed = true;
                    }
                    RemoveOrigins(candidate, removeOriginals);
                    continue;
                }

                if (keep && candidate.KeepDraft != null) candidate.Draft = candidate.KeepDraft;
                if (candidate.Draft == null) continue;
                // A layer some component already had left out of the build was converted once; a second entry
                // on the same parameter would fight the first one.
                if (!string.IsNullOrEmpty(candidate.OriginLayer) && converted.Contains(candidate.OriginLayer)) continue;

                // What the creator had already changed about the item being converted comes with it: the name
                // and the icon become the entry's own, a move becomes where the entry lands, and the hide goes,
                // since the creator asked for this item to stay.
                AvatarDoc editOwner = null;
                var carried = removeOriginals ? FindEdit(menu, tree, candidate, out editOwner) : null;
                var path = carried != null && carried.move ? MenuModel.SplitPath(carried.targetPath).ToList() : candidate.Path;

                string parentId = "";
                foreach (var segment in path)
                {
                    var plainSegment = RichName.Strip(segment);
                    var existing = EntryEditing.Siblings(menu, parentId)
                        .FirstOrDefault(e => e.kind == EntryKind.Submenu && RichName.Strip(e.name) == plainSegment);
                    parentId = existing != null ? existing.id : EntryEditing.Add(menu, EntryKind.Submenu, segment, parentId).id;
                }

                var entry = CloneWithFreshId(candidate.Draft);
                entry.parentId = parentId;
                if (carried != null)
                {
                    if (!string.IsNullOrEmpty(carried.name)) entry.name = carried.name;
                    if (carried.icon != null) entry.icon = carried.icon;
                }
                Undo.RecordObject(menu, "Import into AvatarDoc");
                menu.entries.Add(entry);
                changed = true;
                created.Add(entry);

                if (carried != null && editOwner != null)
                {
                    Undo.RecordObject(editOwner, "Import into AvatarDoc");
                    editOwner.menuEdits.Remove(carried);
                    if (editOwner != menu) EntryEditing.MarkChanged(editOwner);
                }

                RemoveOrigins(candidate, removeOriginals);

                if (keep) continue;
                if (removeOriginals && !string.IsNullOrEmpty(candidate.OriginLayer))
                {
                    var hideEdit = MenuEdit.Create(candidate.OriginControlPath, candidate.OriginOccurrence);
                    hideEdit.hide = true;
                    menu.menuEdits.Add(hideEdit);
                    menu.removedLayers.Add(new RemovedLayer { layer = candidate.OriginLayer, editId = hideEdit.id });
                    converted.Add(candidate.OriginLayer);
                }
            }

            // Last, so the occurrences are counted on the menu this whole batch really produced.
            if (installed.Count > 0 && replaced.Count > 0) HideReplacedAssetControls(menu, installed, replaced);
            return changed;
        }

        /// <summary>
        /// Whether a declaration that came with a menu asset has nothing left to drive: a converted entry took its
        /// layer and brought a parameter of its own, the layer is left out of the merge, and every control of the
        /// asset that named it is hidden with it. Declaring it anyway would cost synced bits for a parameter
        /// nothing reads. Any layer that still merges and reads the name keeps it.
        /// </summary>
        static bool Obsolete(string name, HashSet<string> replaced, List<string> skipped, List<ImportCandidate> batch)
        {
            if (string.IsNullOrEmpty(name) || !replaced.Contains(name)) return false;
            foreach (var candidate in batch)
            {
                if (candidate.MergeDraft == null) continue;
                var controller = MergeBuilder.Resolve(candidate.MergeDraft.controller);
                if (controller == null) continue;
                foreach (var layer in controller.layers)
                    if (!skipped.Contains(layer.name) && LayerReads(layer, name)) return false;
            }
            return true;
        }

        static bool LayerReads(AnimatorControllerLayer layer, string name)
        {
            if (layer.stateMachine == null) return false;
            foreach (var transition in LayerAnalysis.Transitions(layer.stateMachine))
                foreach (var condition in transition.conditions)
                    if (condition.parameter == name) return true;
            foreach (var state in LayerAnalysis.States(layer.stateMachine))
            {
                if (state.speedParameterActive && state.speedParameter == name) return true;
                if (state.timeParameterActive && state.timeParameter == name) return true;
                if (state.mirrorParameterActive && state.mirrorParameter == name) return true;
                if (state.cycleOffsetParameterActive && state.cycleOffsetParameter == name) return true;
                if (MotionReads(state.motion, name)) return true;
            }
            return false;
        }

        static bool MotionReads(Motion motion, string name)
        {
            if (!(motion is BlendTree tree)) return false;
            if (tree.blendParameter == name) return true;
            bool is2D = tree.blendType == BlendTreeType.SimpleDirectional2D || tree.blendType == BlendTreeType.FreeformDirectional2D
                || tree.blendType == BlendTreeType.FreeformCartesian2D;
            if (is2D && tree.blendParameterY == name) return true;
            foreach (var child in tree.children)
                if (MotionReads(child.motion, name)) return true;
            return false;
        }

        /// <summary>
        /// Writes a piece's rows onto an AvatarDoc Shapes component of its own, adding the component when the
        /// piece has none. A row the piece already carries is left alone, so importing the same scan twice does
        /// not double the list.
        /// </summary>
        static void ApplyShapes(ShapesDraft draft)
        {
            if (draft.Piece == null) return;
            var component = draft.Piece.GetComponent<AvatarDocShapes>();
            if (component == null) component = Undo.AddComponent<AvatarDocShapes>(draft.Piece);
            Undo.RecordObject(component, "Import into AvatarDoc");
            foreach (var row in draft.Follow)
            {
                if (component.follow.Any(r => r != null && r.sourceMesh == row.sourceMesh && r.sourceShape == row.sourceShape
                        && r.EffectiveLocalShape == row.EffectiveLocalShape)) continue;
                component.follow.Add(new ShapeFollow { sourceMesh = row.sourceMesh, sourceShape = row.sourceShape, localShape = row.localShape });
            }
            foreach (var row in draft.WhileOn)
            {
                if (component.whileOn.Any(r => r != null && r.mesh == row.mesh && r.shape == row.shape)) continue;
                component.whileOn.Add(new ShapeWhileOn { mesh = row.mesh, shape = row.shape, value = row.value });
            }
            EntryEditing.MarkChanged(component);
        }

        static void RemoveOrigins(ImportCandidate candidate, bool removeOriginals)
        {
            if (!removeOriginals) return;
#if DOCME_MA
            MergeAnimatorImport.ApplyTrims(candidate.Trims);
#endif
            if (candidate.OriginComponents == null) return;
            foreach (var origin in candidate.OriginComponents)
                if (origin != null) Undo.DestroyObjectImmediate(origin);
        }

        /// <summary>True when every layer the controller has is left out, so the merge would add nothing at all.</summary>
        static bool SkipsEverything(RuntimeAnimatorController controller, List<string> skip)
        {
            var layers = MergeBuilder.LayerNames(controller);
            return layers.Count > 0 && layers.All(skip.Contains);
        }

        /// <summary>A menu asset install with an id of its own, so importing the same scan twice never makes two rows that share one.</summary>
        static MenuAssetInstall CopyOfInstall(MenuAssetInstall source)
        {
            return new MenuAssetInstall
            {
                id = Guid.NewGuid().ToString("N"),
                menu = source.menu,
                installPath = source.installPath,
                asSubmenu = source.asSubmenu,
                name = source.name,
                icon = source.icon,
                platform = source.platform,
            };
        }

        static ParameterDeclaration CopyOfDeclaration(ParameterDeclaration source)
        {
            return new ParameterDeclaration
            {
                name = source.name,
                type = source.type,
                defaultValue = source.defaultValue,
                saved = source.saved,
                synced = source.synced,
            };
        }

        /// <summary>
        /// A control of the installed asset whose parameter a converted entry replaced would sit beside that entry
        /// driving nothing, so it is hidden by a menu edit. Nothing is written to the asset: the edit names the
        /// control by the path it has once AvatarDoc installs it.
        /// </summary>
        static void HideReplacedAssetControls(AvatarDoc menu, List<MenuAssetInstall> installed, HashSet<string> replaced)
        {
            var ids = new HashSet<string>(installed.Select(i => i.id));
            // A menu edit names its control by plain path AND occurrence over the whole merged menu, so the
            // numbers are read off the model as it now stands: a control of the avatar's own menu that happens to
            // carry the same name in the same place must keep its place, and two asset controls of one name are
            // told apart.
            var tree = CurrentTree(menu);
            Undo.RecordObject(menu, "Import into AvatarDoc");
            foreach (var node in tree.AllNodes())
                foreach (var item in node.Items)
                {
                    if (item.Asset == null || item.Existing == null || !ids.Contains(item.Asset.id)) continue;
                    if (!NamesAny(item.Existing, replaced)) continue;
                    var edit = MenuEdit.Create(MenuModel.SplitPath(item.OriginalPath ?? ""), item.Occurrence);
                    edit.hide = true;
                    menu.menuEdits.Add(edit);
                }
        }

        /// <summary>The merged menu as it stands right now, with everything this batch has written already in it.</summary>
        static MenuTree CurrentTree(AvatarDoc menu)
        {
            var root = AvatarPlanCache.RootOf(menu);
            var descriptor = root != null ? root.GetComponent<VRCAvatarDescriptor>() : null;
            var components = root != null ? MenuModel.CollectComponents(root) : new List<AvatarDoc>();
            if (!components.Contains(menu)) components.Add(menu);
            return MenuModel.Build(root, descriptor != null ? descriptor.expressionsMenu : null, components);
        }

        /// <summary>True when this control drives one of the named parameters, as its own or as a sub parameter.</summary>
        static bool NamesAny(VRCExpressionsMenu.Control control, HashSet<string> names)
        {
            if (control.parameter != null && names.Contains((control.parameter.name ?? "").Trim())) return true;
            if (control.subParameters == null) return false;
            foreach (var sub in control.subParameters)
                if (sub != null && names.Contains((sub.name ?? "").Trim())) return true;
            return false;
        }

        /// <summary>A merged animator draft with an id of its own, so importing the same scan twice never makes two rows that share one.</summary>
        static AnimatorMerge CopyOfMerge(AnimatorMerge source, List<string> skipLayers)
        {
            return new AnimatorMerge
            {
                id = Guid.NewGuid().ToString("N"),
                controller = source.controller,
                pathRoot = source.pathRoot,
                matchWriteDefaults = source.matchWriteDefaults,
                skipLayers = skipLayers,
                platform = source.platform,
            };
        }

        /// <summary>The menu edit that names the item being converted, wherever on the avatar it lives, and the component holding it. Matched on the same (plain path, occurrence) pair MenuEditing writes.</summary>
        static MenuEdit FindEdit(AvatarDoc menu, MenuTree tree, ImportCandidate candidate, out AvatarDoc owner)
        {
            owner = null;
            if (!TrySourceOf(tree, candidate, out var source, out int occurrence)) return null;
            foreach (var component in ComponentsOf(menu))
                foreach (var edit in component.menuEdits)
                    if (edit.occurrence == occurrence && string.Join("/", edit.source ?? new List<string>()) == source)
                    {
                        owner = component;
                        return edit;
                    }
            return null;
        }

        /// <summary>
        /// The plain path and occurrence a menu edit names this candidate's item by. An FX candidate carries both
        /// from the scan. A foreign candidate is matched to its row through the component that installs it, never
        /// through its path alone: a control of the avatar's own menu can carry the very same path, and its edit
        /// belongs to it, not to the item being converted.
        /// </summary>
        static bool TrySourceOf(MenuTree tree, ImportCandidate candidate, out string source, out int occurrence)
        {
            source = "";
            occurrence = 0;
            if (candidate.OriginControlPath != null && candidate.OriginControlPath.Count > 0)
            {
                source = string.Join("/", candidate.OriginControlPath);
                occurrence = candidate.OriginOccurrence;
                return source.Length > 0;
            }
            var item = ForeignItemOf(tree, candidate);
            if (item == null) return false;
            source = item.OriginalPath ?? "";
            if (source.Length == 0) return false;
            occurrence = MenuEditing.OccurrenceOf(tree, item);
            return true;
        }

        /// <summary>The foreign row of the merged menu this candidate came from, found by the component that installs it.</summary>
        static MenuItem ForeignItemOf(MenuTree tree, ImportCandidate candidate)
        {
            if (tree == null || candidate.OriginComponents == null || candidate.OriginComponents.Count == 0) return null;
            foreach (var node in tree.AllNodes())
                foreach (var item in node.Items)
                    if (item.Foreign != ForeignSource.None && item.Origin != null && candidate.OriginComponents.Contains(item.Origin))
                        return item;
            return null;
        }

        /// <summary>The component being converted into first, then the others on the avatar. A disabled other is left out: the tree never showed its edits, so converting must neither carry one over nor be stopped by it.</summary>
        static IEnumerable<AvatarDoc> ComponentsOf(AvatarDoc menu)
        {
            yield return menu;
            var root = AvatarPlanCache.RootOf(menu);
            if (root == null) yield break;
            foreach (var other in root.GetComponentsInChildren<AvatarDoc>(true))
                if (other != menu && !MenuModel.IsDisabled(other)) yield return other;
        }

        /// <summary>Layer names already left out of the build, from every AvatarDoc on the avatar, or from this one alone when it sits outside an avatar.</summary>
        static HashSet<string> ConvertedLayers(AvatarDoc menu)
        {
            var root = AvatarPlanCache.RootOf(menu);
            if (root != null) return FxLayerImport.ConvertedLayers(root);
            var own = new HashSet<string>();
            foreach (var removed in menu.removedLayers)
                if (removed != null && !string.IsNullOrEmpty(removed.layer)) own.Add(removed.layer);
            return own;
        }

        static MenuEntry CloneWithFreshId(MenuEntry source)
        {
            var copy = JsonUtility.FromJson<MenuEntry>(JsonUtility.ToJson(source));
            copy.id = Guid.NewGuid().ToString("N");
            return copy;
        }

        /// <summary>Undoes an FX layer import: drops its entry from removedLayers and removes the hiding menu edit by the id the import recorded for it, so the old control shows again. No rescanning: the edit id is stored precisely so this never has to guess which edit belongs to which layer. The FX layer asset was never touched, so nothing else needs restoring.</summary>
        public static bool RestoreLayer(AvatarDoc menu, string layerName)
        {
            if (EntryEditing.Refuses(menu) || string.IsNullOrEmpty(layerName)) return false;
            var removed = menu.removedLayers.FirstOrDefault(r => r.layer == layerName);
            if (removed == null) return false;

            Undo.RecordObject(menu, "Restore layer");
            var edit = menu.menuEdits.FirstOrDefault(e => e.id == removed.editId);
            if (edit != null) menu.menuEdits.Remove(edit);
            menu.removedLayers.Remove(removed);
            EntryEditing.MarkChanged(menu);
            return true;
        }
    }
}
#endif
