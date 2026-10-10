#if DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
#if DOCME_MA
using nadena.dev.modular_avatar.core;
#endif

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>One menu item another tool (Modular Avatar or VRCFury) will add at build time, read on a best effort basis for the inspector.</summary>
    public sealed class ForeignItem
    {
        public ForeignSource Source;
        /// <summary>Plain names from the root down to this item, submenus then the item itself.</summary>
        public List<string> Path = new List<string>();
        public string RichName = "";
        public Texture2D Icon;
        /// <summary>True when this item is itself a submenu (its children, if any, follow as further items with a longer path).</summary>
        public bool IsSubmenu;
        public Component Origin;
    }

    /// <summary>Reads the menu items Modular Avatar and VRCFury will install, without writing anything and without referencing any VRCFury type.</summary>
    public static class ForeignMenu
    {
        public static List<ForeignItem> Read(Transform avatarRoot, MenuNode existingRoot)
        {
            var result = new List<ForeignItem>();
            if (avatarRoot == null) return result;
            ReadVrcFury(avatarRoot, result);
#if DOCME_MA
            ReadModularAvatar(avatarRoot, existingRoot, result);
#endif
            return result;
        }

        static void ReadVrcFury(Transform avatarRoot, List<ForeignItem> result)
        {
            foreach (var component in VrcFuryReader.Components(avatarRoot))
            {
                using (var so = new SerializedObject(component))
                {
                    var content = VrcFuryReader.Content(so);
                    if (!VrcFuryReader.IsFeature(content, VrcFuryReader.ToggleTypeName)) continue;
                    var nameProp = content.FindPropertyRelative("name");
                    var path = VrcFuryReader.SplitMenuPath(nameProp != null ? nameProp.stringValue : "");
                    if (path.Count == 0) continue;
                    result.Add(new ForeignItem { Source = ForeignSource.VRCFury, Path = path, RichName = path[path.Count - 1], Origin = component });
                }
            }
        }

#if DOCME_MA
        static void ReadModularAvatar(Transform avatarRoot, MenuNode existingRoot, List<ForeignItem> result)
        {
            foreach (var installer in avatarRoot.GetComponentsInChildren<ModularAvatarMenuInstaller>(true))
            {
                var parentPath = ParentPathOf(installer, existingRoot);
                var item = installer.GetComponent<ModularAvatarMenuItem>();
                var group = installer.GetComponent<ModularAvatarMenuGroup>();
                var onPath = new HashSet<ModularAvatarMenuItem>();
                if (item != null)
                {
                    ReadMenuItem(item, parentPath, result, onPath);
                }
                else if (group != null)
                {
                    var target = group.targetObject != null ? group.targetObject : group.gameObject;
                    foreach (Transform child in target.transform)
                    {
                        var childItem = child.GetComponent<ModularAvatarMenuItem>();
                        if (childItem != null) ReadMenuItem(childItem, parentPath, result, onPath);
                    }
                }
                else if (installer.menuToAppend != null)
                {
                    ReadMenuAsset(installer.menuToAppend, parentPath, installer, result, new HashSet<VRCExpressionsMenu>());
                }
            }
        }

        /// <summary>
        /// onPath holds the items being read right now, from the first down to this one. A submenu that takes its
        /// children from the object holding it reaches itself again: Modular Avatar builds that as a looping menu,
        /// so the read stops at an item already on its path instead of recursing until the editor dies.
        /// </summary>
        static void ReadMenuItem(ModularAvatarMenuItem item, List<string> parentPath, List<ForeignItem> result, HashSet<ModularAvatarMenuItem> onPath)
        {
            if (item.Control == null || !onPath.Add(item)) return;
            var name = string.IsNullOrEmpty(item.label) ? item.gameObject.name : item.label;
            var plain = RichName.Strip(name);
            var path = Append(parentPath, plain);
            bool isSubmenu = item.Control.type == VRCExpressionsMenu.Control.ControlType.SubMenu;
            result.Add(new ForeignItem { Source = ForeignSource.ModularAvatar, Path = path, RichName = name, Icon = item.Control.icon, IsSubmenu = isSubmenu, Origin = item });

            if (isSubmenu && item.MenuSource == SubmenuSource.MenuAsset && item.Control.subMenu != null)
                ReadMenuAsset(item.Control.subMenu, path, null, result, new HashSet<VRCExpressionsMenu>());

            if (isSubmenu && item.MenuSource == SubmenuSource.Children)
            {
                var root = item.menuSource_otherObjectChildren != null ? item.menuSource_otherObjectChildren : item.gameObject;
                foreach (Transform child in root.transform)
                {
                    var childItem = child.GetComponent<ModularAvatarMenuItem>();
                    if (childItem != null) ReadMenuItem(childItem, path, result, onPath);
                }
            }
            onPath.Remove(item);
        }

        /// <summary>
        /// The controls of a menu asset another tool installs, submenus included, as foreign items under parentPath.
        /// Controls inside a submenu asset belong to no scene component of their own, so they carry no origin: they can
        /// be renamed, moved or hidden, and there is nothing of theirs to convert.
        /// </summary>
        static void ReadMenuAsset(VRCExpressionsMenu menu, List<string> parentPath, Component origin, List<ForeignItem> result, HashSet<VRCExpressionsMenu> visited)
        {
            if (menu == null || !visited.Add(menu)) return;
            foreach (var control in menu.controls ?? new List<VRCExpressionsMenu.Control>())
            {
                if (control == null) continue;
                var plain = RichName.Strip(control.name ?? "");
                var path = Append(parentPath, plain);
                bool isSubmenu = control.type == VRCExpressionsMenu.Control.ControlType.SubMenu;
                result.Add(new ForeignItem
                {
                    Source = ForeignSource.ModularAvatar,
                    Path = path,
                    RichName = control.name ?? plain,
                    Icon = control.icon,
                    IsSubmenu = isSubmenu,
                    Origin = origin,
                });
                if (isSubmenu && control.subMenu != null) ReadMenuAsset(control.subMenu, path, null, result, visited);
            }
            visited.Remove(menu);
        }

        /// <summary>The plain path of the node whose ExistingMenu matches the installer's target, or the root when there is no match.</summary>
        static List<string> ParentPathOf(ModularAvatarMenuInstaller installer, MenuNode existingRoot)
        {
            if (installer.installTargetMenu == null || existingRoot == null) return new List<string>();
            var node = FindNodeFor(existingRoot, installer.installTargetMenu);
            return node != null ? SplitNodePath(node) : new List<string>();
        }

        static MenuNode FindNodeFor(MenuNode node, VRCExpressionsMenu menu)
        {
            if (node.ExistingMenu == menu) return node;
            foreach (var item in node.Items)
            {
                if (item.Submenu == null) continue;
                var found = FindNodeFor(item.Submenu, menu);
                if (found != null) return found;
            }
            return null;
        }

        static List<string> SplitNodePath(MenuNode node) => string.IsNullOrEmpty(node.Path) ? new List<string>() : node.Path.Split('/').ToList();

        static List<string> Append(List<string> path, string segment)
        {
            var result = new List<string>(path) { segment };
            return result;
        }
#endif
    }
}
#endif
