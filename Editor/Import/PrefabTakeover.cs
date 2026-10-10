#if DOCME_NDMF && DOCME_VRCSDK3A
#if DOCME_MA
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Takes a whole Modular Avatar prefab over as it is: a Menu Installer carrying a finished menu asset, or a
    /// Menu Item whose submenu comes from one, together with the Parameters component and the Merge Animator on
    /// the same object. All of it is offered as one group, so the menu, the declarations and the merged controller
    /// arrive together or not at all.
    /// </summary>
    public static class PrefabTakeover
    {
        /// <summary>Scans the avatar. The candidates found so far are read, never changed except to join a group: a set the merge importer already claimed keeps its own merged animator candidate.</summary>
        public static List<ImportCandidate> Scan(Transform avatarRoot, MenuTree tree, IReadOnlyList<ImportCandidate> found = null)
        {
            var result = new List<ImportCandidate>();
            if (avatarRoot == null) return result;
            foreach (var installer in avatarRoot.GetComponentsInChildren<ModularAvatarMenuInstaller>(true))
            {
                var item = installer.GetComponent<ModularAvatarMenuItem>();
                VRCExpressionsMenu asset = null;
                bool asSubmenu = false;
                var name = "";
                Texture2D icon = null;

                if (item != null && item.Control != null
                    && item.Control.type == VRCExpressionsMenu.Control.ControlType.SubMenu
                    && item.MenuSource == SubmenuSource.MenuAsset && item.Control.subMenu != null)
                {
                    asset = item.Control.subMenu;
                    asSubmenu = true;
                    name = string.IsNullOrEmpty(item.label) ? item.gameObject.name : item.label;
                    icon = item.Control.icon;
                }
                else if (item == null && installer.menuToAppend != null)
                {
                    // A Menu Installer appends the asset's own controls into its target menu, so the takeover
                    // appends them too rather than wrapping them in a submenu nobody asked for.
                    asset = installer.menuToAppend;
                }
                if (asset == null) continue;

                result.AddRange(ScanOne(avatarRoot, tree, installer, item, asset, asSubmenu, name, icon, found));
            }
            return result;
        }

        static List<ImportCandidate> ScanOne(Transform avatarRoot, MenuTree tree, ModularAvatarMenuInstaller installer,
            ModularAvatarMenuItem item, VRCExpressionsMenu asset, bool asSubmenu, string name, Texture2D icon,
            IReadOnlyList<ImportCandidate> found)
        {
            var made = new List<ImportCandidate>();
            var merge = MergeOn(installer.gameObject);
            var controller = merge != null ? MergeBuilder.Resolve(merge.animator) : null;
            var groupId = merge != null ? "merge:" + merge.GetInstanceID() : "takeover:" + installer.GetInstanceID();
            var siblings = found != null ? found.Where(c => c.Group == groupId).ToList() : new List<ImportCandidate>();

            var path = ParentPathOf(installer, tree, out bool targetUnknown);
            var install = MenuAssetInstall.Create(asset);
            install.asSubmenu = asSubmenu;
            // A single slash is the avatar's root menu, which is where a Menu Installer with no target menu goes.
            // An empty path would instead follow the component the import writes into, which may install itself
            // as a submenu, and the takeover would no longer be lossless.
            install.installPath = path.Count > 0 ? string.Join("/", path) : "/";
            install.name = asSubmenu ? name : "";
            install.icon = icon;

            var used = UsedNames(asset, controller);
            var declarations = new List<ParameterDeclaration>();
            var trims = new List<ParameterTrim>();
            var cleanup = new List<Component> { installer };
            if (item != null) cleanup.Add(item);

            string blocked = targetUnknown ? L.Text("ad.import.reason.install_target_unknown") : null;
            foreach (var component in installer.GetComponents<ModularAvatarParameters>())
            {
                var mine = new List<string>();
                foreach (var config in component.parameters)
                {
                    if (config.isPrefix || string.IsNullOrWhiteSpace(config.nameOrPrefix)) continue;
                    if (!used.Contains(config.nameOrPrefix)) continue;
                    if (config.internalParameter || !string.IsNullOrEmpty(config.remapTo))
                    {
                        if (blocked == null) blocked = string.Format(L.Text("ad.import.reason.ma_renamed_parameters"), config.nameOrPrefix);
                        continue;
                    }
                    // Modular Avatar registers no expression parameter for a declaration that is not synced, so
                    // the takeover declares none either and leaves that entry where it is.
                    if (config.syncType == ParameterSyncType.NotSynced) continue;
                    mine.Add(config.nameOrPrefix);
                    declarations.Add(DeclarationFor(config, controller, asset));
                }
                if (mine.Count == 0) continue;
                if (mine.Count == component.parameters.Count) cleanup.Add(component);
                else trims.Add(new ParameterTrim { Component = component, Names = mine });
            }

            var assetCandidate = new ImportCandidate
            {
                Source = ImportSource.MenuAsset,
                Group = groupId,
                Path = path,
                MenuAssetDraft = install,
                ParameterDrafts = declarations,
                OriginComponents = cleanup,
                Trims = trims,
                Supported = blocked == null,
                Reason = blocked,
            };
            assetCandidate.Notes.Add(L.Text("ad.import.note.takeover"));
            made.Add(assetCandidate);

            // The merged animator comes over unless the merge importer already offers it. Offering it even where
            // that importer converted every layer is what lets the creator still keep the whole set as it is; a
            // merge that ends up leaving every layer out is dropped again when the group is applied.
            if (merge != null && controller != null && !siblings.Any(c => c.MergeDraft != null))
            {
                var draft = AnimatorMerge.Create(merge.animator);
                draft.pathRoot = merge.pathMode == MergeAnimatorPathMode.Relative ? PathRootOf(merge) : null;
                draft.matchWriteDefaults = merge.matchAvatarWriteDefaults;
                var mergeBlocked = blocked;
                if (mergeBlocked == null && MenuModel.IsOutsideAvatar(avatarRoot, PathRootOf(merge)))
                    mergeBlocked = string.Format(L.Text("ad.error.merge_outside_avatar"), merge.animator.name, merge.gameObject.name);
                made.Add(new ImportCandidate
                {
                    Source = ImportSource.MergeAnimator,
                    Group = groupId,
                    MergeDraft = draft,
                    OriginComponents = new List<Component> { merge },
                    Supported = mergeBlocked == null,
                    Reason = mergeBlocked,
                });
            }

            var members = siblings.Count > 0 && siblings[0].GroupMembers != null ? siblings[0].GroupMembers : new List<ImportCandidate>(siblings);
            members.AddRange(made);
            foreach (var candidate in members) candidate.GroupMembers = members;
            // A refusal belongs to the whole set: one member that cannot come over stops all of it.
            if (blocked != null)
                foreach (var candidate in members)
                {
                    candidate.Supported = false;
                    candidate.Reason = blocked;
                }
            return made;
        }

        /// <summary>The Merge Animator beside the installer that merges into FX. Only the same object counts: one above it belongs to another set.</summary>
        static ModularAvatarMergeAnimator MergeOn(GameObject go)
        {
            foreach (var merge in go.GetComponents<ModularAvatarMergeAnimator>())
                if (merge.layerType == VRCAvatarDescriptor.AnimLayerType.FX && merge.animator != null) return merge;
            return null;
        }

        /// <summary>Every parameter name this set works with: what the menu asset's controls drive, and what the merged controller declares.</summary>
        static HashSet<string> UsedNames(VRCExpressionsMenu asset, AnimatorController controller)
        {
            var used = new HashSet<string>(MenuAssetPanel.ParametersOf(asset));
            if (controller != null)
                foreach (var parameter in controller.parameters) used.Add(parameter.name);
            return used;
        }

        static ParameterDeclaration DeclarationFor(ParameterConfig config, AnimatorController controller, VRCExpressionsMenu asset)
        {
            var declaration = ParameterDeclaration.Create(config.nameOrPrefix, TypeOf(config, controller, asset));
            declaration.defaultValue = config.defaultValue;
            declaration.saved = config.saved;
            declaration.synced = config.syncType != ParameterSyncType.NotSynced && !config.localOnly;
            return declaration;
        }

        /// <summary>The type a declaration takes: the sync type Modular Avatar states, else the merged controller's own, else what the menu asset's controls imply.</summary>
        static PlannedType TypeOf(ParameterConfig config, AnimatorController controller, VRCExpressionsMenu asset)
        {
            switch (config.syncType)
            {
                case ParameterSyncType.Bool: return PlannedType.Bool;
                case ParameterSyncType.Int: return PlannedType.Int;
                case ParameterSyncType.Float: return PlannedType.Float;
            }
            if (controller != null)
                foreach (var parameter in controller.parameters)
                {
                    if (parameter.name != config.nameOrPrefix) continue;
                    if (parameter.type == AnimatorControllerParameterType.Float) return PlannedType.Float;
                    if (parameter.type == AnimatorControllerParameterType.Int) return PlannedType.Int;
                    return PlannedType.Bool;
                }
            return MenuAssetPanel.TypeFor(asset, config.nameOrPrefix);
        }

        /// <summary>
        /// The plain path of the submenu this installer installs into, or the root when it names none. A target
        /// that is set but nowhere in this tree, usually a menu another installer brings, leaves unresolved true:
        /// taking the set over at the root instead would silently move the whole prefab's menu.
        /// </summary>
        static List<string> ParentPathOf(ModularAvatarMenuInstaller installer, MenuTree tree, out bool unresolved)
        {
            unresolved = false;
            if (installer.installTargetMenu == null || tree == null || tree.Root == null) return new List<string>();
            var node = FindNodeFor(tree.Root, installer.installTargetMenu);
            if (node == null)
            {
                unresolved = true;
                return new List<string>();
            }
            if (string.IsNullOrEmpty(node.Path)) return new List<string>();
            return node.Path.Split('/').ToList();
        }

        static MenuNode FindNodeFor(MenuNode node, VRCExpressionsMenu menu)
        {
            if (node.ExistingMenu == menu && node.Asset == null) return node;
            foreach (var item in node.Items)
            {
                if (item.Submenu == null) continue;
                var found = FindNodeFor(item.Submenu, menu);
                if (found != null) return found;
            }
            return null;
        }

        static Transform PathRootOf(ModularAvatarMergeAnimator merge)
        {
            var target = merge.relativePathRoot != null ? merge.relativePathRoot.Get(merge) : null;
            return target != null ? target.transform : merge.transform;
        }
    }
}
#endif
#endif
