#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.vrchat;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Resolves menu edits that AvatarDoctorBuilder could not apply to its own model, against the real menu assets
    /// left behind by Modular Avatar and VRCFury. Runs once after Modular Avatar and once at the very end of the
    /// build, in BuildPhase.Optimizing.
    /// </summary>
    public static class MenuEditApplier
    {
        public static void RunAfterModularAvatar(BuildContext context) => Run(context, final: false);

        public static void RunFinal(BuildContext context) => Run(context, final: true);

        static void Run(BuildContext context, bool final)
        {
            var state = context.GetState<LateEditState>();
            if (state.Pending.Count == 0) return;
            if (final) state.Final = true;

            var descriptor = context.VRChatAvatarDescriptor();
            var messages = new List<ModelMessage>();
            var root = Apply(descriptor.expressionsMenu, state.Pending, m => context.AssetSaver.SaveAsset(m), messages, final);
            if (root != descriptor.expressionsMenu)
            {
                descriptor.expressionsMenu = root;
                descriptor.customExpressions = true;
            }
            L.Report(messages);
        }

        /// <summary>
        /// Applies what resolves of pending against root and its reachable submenus. Returns a deep clone of root
        /// with the edits applied, or root itself unchanged when nothing resolved. Resolved edits are removed from
        /// pending; with final true, whatever is still left reports ad.warn.edit_source_missing.
        /// </summary>
        public static VRCExpressionsMenu Apply(VRCExpressionsMenu root, List<PendingEdit> pending, Action<VRCExpressionsMenu> save, List<ModelMessage> messages, bool final)
        {
            if (root == null || !pending.Any(p => Applies(root, p.Edit, final)))
            {
                if (final) WarnRemaining(pending, messages);
                return root;
            }

            var menus = new List<VRCExpressionsMenu>();
            var newRoot = Clone(root, new HashSet<VRCExpressionsMenu>(), save, menus);
            var touched = new HashSet<VRCExpressionsMenu>();

            // Every edit is located on the untouched clone first. Applying as we go would let a rename or a hide
            // change what the next edit's source names, which the model (matching on the original path) never does.
            var resolved = new List<(PendingEdit pendingEdit, VRCExpressionsMenu parent, VRCExpressionsMenu.Control control)>();
            foreach (var pendingEdit in pending.ToList())
            {
                if (!Applies(newRoot, pendingEdit.Edit, final)) continue;
                if (Locate(newRoot, pendingEdit.Edit, out var parent, out _, out var control))
                    resolved.Add((pendingEdit, parent, control));
            }

            foreach (var (pendingEdit, parent, control) in resolved)
            {
                int index = parent.controls != null ? parent.controls.IndexOf(control) : -1;
                // An earlier edit took the control out from under this one; leaving it pending keeps it honest.
                if (index < 0) continue;
                ApplyOne(newRoot, parent, index, control, pendingEdit, menus, touched, save, messages);
                pending.Remove(pendingEdit);
            }

            SplitOverflow(menus.Where(touched.Contains).ToList(), save, messages);

            if (final) WarnRemaining(pending, messages);
            return newRoot;
        }

        static void ApplyOne(VRCExpressionsMenu root, VRCExpressionsMenu parent, int index, VRCExpressionsMenu.Control control, PendingEdit pendingEdit, List<VRCExpressionsMenu> menus, HashSet<VRCExpressionsMenu> touched, Action<VRCExpressionsMenu> save, List<ModelMessage> messages)
        {
            var edit = pendingEdit.Edit;
            var target = parent;
            if (edit.move)
            {
                var segments = MenuModel.SplitPath(edit.targetPath);
                if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu && control.subMenu != null && Reaches(root, segments, control.subMenu))
                {
                    messages.Add(ModelMessage.Error("ad.error.edit_cycle", SourceLabel(edit), pendingEdit.Context));
                    return;
                }
                target = root;
                foreach (var segment in segments)
                    target = FindOrCreateSubmenu(target, segment, menus, touched, save);
            }

            if (!string.IsNullOrEmpty(edit.name)) control.name = edit.name;
            if (edit.icon != null) control.icon = edit.icon;

            // A late edit runs after the parameters are written, so remove can only take the control itself here.
            if (edit.hide || edit.remove)
            {
                parent.controls.RemoveAt(index);
                return;
            }

            if (target != parent)
            {
                parent.controls.RemoveAt(index);
                Insert(target, control, edit.position);
                touched.Add(target);
            }
            else if (edit.position >= 0)
            {
                parent.controls.RemoveAt(index);
                Insert(target, control, edit.position);
            }
        }

        static void Insert(VRCExpressionsMenu menu, VRCExpressionsMenu.Control control, int position)
        {
            if (menu.controls == null) menu.controls = new List<VRCExpressionsMenu.Control>();
            menu.controls.Insert(MenuEdits.ClampPosition(position, menu.controls.Count), control);
        }

        /// <summary>An existing submenu of parent by plain name, or a new saved asset added as a control of parent. Either way, parent counts as touched: it either linked to an existing submenu or grew a new control. A submenu another tool pushed onto an overflow page is found there rather than made a second time.</summary>
        static VRCExpressionsMenu FindOrCreateSubmenu(VRCExpressionsMenu parent, string name, List<VRCExpressionsMenu> menus, HashSet<VRCExpressionsMenu> touched, Action<VRCExpressionsMenu> save)
        {
            if (parent.controls == null) parent.controls = new List<VRCExpressionsMenu.Control>();
            var found = FindChild(parent, name, false) ?? FindChild(parent, name, true);
            if (found != null) return found;
            var existing = parent.controls.FirstOrDefault(c => c.type == VRCExpressionsMenu.Control.ControlType.SubMenu && MenuEdits.SegmentMatches(name, c.name));

            var created = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            created.name = name;
            created.controls = new List<VRCExpressionsMenu.Control>();
            save(created);
            menus.Add(created);
            touched.Add(parent);

            if (existing != null)
            {
                existing.subMenu = created;
            }
            else
            {
                parent.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = name,
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = created,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = "" },
                    subParameters = Array.Empty<VRCExpressionsMenu.Control.Parameter>(),
                    labels = Array.Empty<VRCExpressionsMenu.Control.Label>(),
                });
            }
            return created;
        }

        /// <summary>
        /// Whether this pass can carry the edit out: its source has to resolve, and, outside the last pass, a move
        /// has to have every segment of its target path already there. Another tool running later may still install
        /// a submenu of that name, and creating one here would leave the avatar with two.
        /// </summary>
        static bool Applies(VRCExpressionsMenu root, MenuEdit edit, bool final)
        {
            if (!Locate(root, edit, out _, out _, out _)) return false;
            return final || !edit.move || PathExists(root, MenuModel.SplitPath(edit.targetPath));
        }

        /// <summary>True when every segment of targetPath already names a submenu, so a move into it creates nothing. An empty path is the root, which always exists.</summary>
        static bool PathExists(VRCExpressionsMenu root, string[] targetPath)
        {
            return Walk(root, targetPath, false) != null || Walk(root, targetPath, true) != null;
        }

        /// <summary>The menu targetPath names, walking from root, or null when a segment does not exist. With pages transparent, a segment may sit inside an overflow page of the level it belongs to.</summary>
        static VRCExpressionsMenu Walk(VRCExpressionsMenu root, string[] targetPath, bool pagesTransparent)
        {
            var current = root;
            foreach (var segment in targetPath)
            {
                current = FindChild(current, segment, pagesTransparent);
                if (current == null) return null;
            }
            return current;
        }

        /// <summary>A submenu of menu by plain name. With pagesTransparent, one that an overflow page of menu carries counts as menu's own.</summary>
        static VRCExpressionsMenu FindChild(VRCExpressionsMenu menu, string segment, bool pagesTransparent)
        {
            return FindChild(menu, segment, pagesTransparent, new HashSet<VRCExpressionsMenu>());
        }

        static VRCExpressionsMenu FindChild(VRCExpressionsMenu menu, string segment, bool pagesTransparent, HashSet<VRCExpressionsMenu> visited)
        {
            if (menu == null || !visited.Add(menu)) return null;
            var controls = menu.controls ?? new List<VRCExpressionsMenu.Control>();
            foreach (var candidate in controls)
                if (candidate.type == VRCExpressionsMenu.Control.ControlType.SubMenu && candidate.subMenu != null
                    && MenuEdits.SegmentMatches(segment, candidate.name))
                    return candidate.subMenu;
            if (!pagesTransparent) return null;
            foreach (var candidate in controls)
            {
                if (!IsOverflowPage(candidate)) continue;
                var found = FindChild(candidate.subMenu, segment, true, visited);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>True when walking targetPath from root, using only submenus that already exist, would step into or through moving.</summary>
        static bool Reaches(VRCExpressionsMenu root, string[] targetPath, VRCExpressionsMenu moving)
        {
            return Reaches(root, targetPath, moving, false) || Reaches(root, targetPath, moving, true);
        }

        static bool Reaches(VRCExpressionsMenu root, string[] targetPath, VRCExpressionsMenu moving, bool pagesTransparent)
        {
            var current = root;
            if (current == moving) return true;
            foreach (var segment in targetPath)
            {
                current = FindChild(current, segment, pagesTransparent);
                if (current == null) return false;
                if (current == moving) return true;
            }
            return false;
        }

        /// <summary>Moves controls beyond the SDK limit into a chained "More" submenu, mirroring MenuModel.SplitOverflow for real assets.</summary>
        static void SplitOverflow(List<VRCExpressionsMenu> menus, Action<VRCExpressionsMenu> save, List<ModelMessage> messages)
        {
            foreach (var menu in menus.ToList())
            {
                var controls = menu.controls ?? new List<VRCExpressionsMenu.Control>();
                if (controls.Count <= VRCExpressionsMenu.MAX_CONTROLS) continue;
                messages.Add(ModelMessage.Info("ad.info.menu_split", menu.name));
                var current = menu;
                while (current.controls.Count > VRCExpressionsMenu.MAX_CONTROLS)
                {
                    const int keep = VRCExpressionsMenu.MAX_CONTROLS - 1;
                    var more = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                    more.name = "More";
                    more.controls = current.controls.Skip(keep).ToList();
                    current.controls.RemoveRange(keep, current.controls.Count - keep);
                    save(more);
                    menus.Add(more);
                    current.controls.Add(new VRCExpressionsMenu.Control
                    {
                        name = "More",
                        icon = null,
                        type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                        subMenu = more,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = "" },
                        subParameters = Array.Empty<VRCExpressionsMenu.Control.Parameter>(),
                        labels = Array.Empty<VRCExpressionsMenu.Control.Label>(),
                    });
                    current = more;
                }
            }
        }

        static void WarnRemaining(List<PendingEdit> pending, List<ModelMessage> messages)
        {
            foreach (var pendingEdit in pending)
                messages.Add(ModelMessage.Warning("ad.warn.edit_source_missing", SourceLabel(pendingEdit.Edit), pendingEdit.Context));
        }

        /// <summary>
        /// Clones menu and everything under it. An asset reached through two different controls (a shared submenu)
        /// gets one clone per reference, matching how MenuModel.ReadExisting turns a shared asset into two
        /// independent MenuNode subtrees: editing one path must never affect the other. ancestors guards only a
        /// genuine cycle back to a menu still on the current path, which stops with an empty stand-in instead of
        /// recursing forever.
        /// </summary>
        static VRCExpressionsMenu Clone(VRCExpressionsMenu menu, HashSet<VRCExpressionsMenu> ancestors, Action<VRCExpressionsMenu> save, List<VRCExpressionsMenu> order)
        {
            if (menu == null) return null;
            if (!ancestors.Add(menu))
            {
                var stub = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                stub.name = menu.name;
                stub.controls = new List<VRCExpressionsMenu.Control>();
                ObjectRegistry.RegisterReplacedObject(menu, stub);
                order.Add(stub);
                save(stub);
                return stub;
            }
            try
            {
                var clone = UnityEngine.Object.Instantiate(menu);
                clone.name = menu.name;
                ObjectRegistry.RegisterReplacedObject(menu, clone);
                order.Add(clone);
                foreach (var control in clone.controls ?? new List<VRCExpressionsMenu.Control>())
                    if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu && control.subMenu != null)
                        control.subMenu = Clone(control.subMenu, ancestors, save, order);
                save(clone);
                return clone;
            }
            finally
            {
                ancestors.Remove(menu);
            }
        }

        /// <summary>Finds the control a menu edit's source names, walking submenu links from root with a visited set so a cycle in the real assets stops instead of hanging.</summary>
        static bool Locate(VRCExpressionsMenu root, MenuEdit edit, out VRCExpressionsMenu parent, out int index, out VRCExpressionsMenu.Control control)
        {
            var targetPath = string.Join("/", edit.source ?? new List<string>());
            // Strictly first, with the pages counted like any other submenu, which is exactly how the model numbers
            // its items. Only when that finds nothing is the source retried with the pages another tool added at
            // build time seen through: a creator's own submenu named "More" keeps its own path either way.
            int seen = 0;
            if (Locate(root, targetPath, edit.occurrence, new HashSet<VRCExpressionsMenu>(), "", ref seen, out parent, out index, out control, false)) return true;
            seen = 0;
            return Locate(root, targetPath, edit.occurrence, new HashSet<VRCExpressionsMenu>(), "", ref seen, out parent, out index, out control, true);
        }

        static bool Locate(VRCExpressionsMenu node, string targetPath, int occurrence, HashSet<VRCExpressionsMenu> visited, string path, ref int seen, out VRCExpressionsMenu parent, out int index, out VRCExpressionsMenu.Control control, bool pagesTransparent)
        {
            parent = null;
            index = -1;
            control = null;
            if (node == null || !visited.Add(node)) return false;
            try
            {
                var controls = node.controls ?? new List<VRCExpressionsMenu.Control>();
                for (int i = 0; i < controls.Count; i++)
                {
                    var candidate = controls[i];
                    var plain = RichName.Strip(candidate.name ?? "");
                    var itemPath = path.Length == 0 ? plain : path + "/" + plain;
                    if (itemPath == targetPath)
                    {
                        if (seen == occurrence) { parent = node; index = i; control = candidate; return true; }
                        seen++;
                    }
                    // An overflow page is a place the SDK limit forced items into, not a place the creator named,
                    // so on the second attempt its contents are searched at the path of the menu that holds it.
                    if (candidate.type == VRCExpressionsMenu.Control.ControlType.SubMenu && candidate.subMenu != null
                        && Locate(candidate.subMenu, targetPath, occurrence, visited, pagesTransparent && IsOverflowPage(candidate) ? path : itemPath, ref seen, out parent, out index, out control, pagesTransparent))
                        return true;
                }
                return false;
            }
            finally
            {
                visited.Remove(node);
            }
        }

        /// <summary>A page another tool (or AvatarDoctor itself) added to carry the controls past the SDK limit: a submenu control with no parameter, named "More" (Modular Avatar and AvatarDoctor) or "Next" (VRCFury).</summary>
        static bool IsOverflowPage(VRCExpressionsMenu.Control control)
        {
            if (control.type != VRCExpressionsMenu.Control.ControlType.SubMenu || control.subMenu == null) return false;
            if (control.parameter != null && !string.IsNullOrEmpty(control.parameter.name)) return false;
            var plain = RichName.Strip(control.name ?? "");
            return plain == "More" || plain == "Next";
        }

        static string SourceLabel(MenuEdit edit) => string.Join("/", edit.source ?? new List<string>());
    }
}
#endif
