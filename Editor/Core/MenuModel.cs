#if DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>A submenu of the merged menu. Existing nodes mirror an asset of the avatar's own menu.</summary>
    public sealed class MenuNode
    {
        public string Name = "";
        /// <summary>The raw name, tags and all, as VRChat shows it. Empty when this node carries no styling of its own.</summary>
        public string RichName = "";
        public Texture2D Icon;
        public bool IsExisting;
        public VRCExpressionsMenu ExistingMenu;
        /// <summary>The install that brought this submenu in, when it comes from a menu asset AvatarDoc places itself. Null for everything else.</summary>
        public MenuAssetInstall Asset;
        public List<MenuItem> Items = new List<MenuItem>();
        public AvatarDoc Component;
        public string EntryId;
        /// <summary>The node this one hangs under. Null on the root.</summary>
        public MenuNode Parent;
        /// <summary>Submenu names from the root down to this node, joined by "/". Empty on the root.</summary>
        public string Path = "";
        /// <summary>The plain path this existing or foreign submenu had right after every component installed, before any menu edit moved it. Empty for a node AvatarDoctor creates from an install path or a move target: those have no position of their own to remember, so their key follows Path instead.</summary>
        public string OriginalPath = "";
        /// <summary>An overflow page the build adds to carry the controls past the eighth. It belongs to no entry and cannot be edited or installed into.</summary>
        public bool IsMorePage;
        /// <summary>A menu edit changed this node's own items (usually by moving one out), without leaving an edited item behind to say so.</summary>
        public bool Edited;
        /// <summary>This submenu entry is not built on the platform the editor targets. Its items are excluded with it unless they set a platform of their own.</summary>
        public bool Excluded;

        /// <summary>True when this node or anything below it holds an AvatarDoctor control or submenu, or was touched by a menu edit. Foreign items are never a reason on their own: the other tool writes them.</summary>
        public bool ContainsGenerated()
        {
            if (!IsExisting) return true;
            // A menu asset is AvatarDoc's own to place, so it is always written out as clones: the asset itself is
            // never handed to the descriptor and so can never be changed by anything downstream.
            if (Asset != null) return true;
            if (Edited) return true;
            foreach (var item in Items)
            {
                if (item.Foreign != ForeignSource.None) continue;
                if (item.Asset != null) return true;
                if (item.Control != null) return true;
                if (item.Edit != null) return true;
                if (item.Submenu != null && item.Submenu.ContainsGenerated()) return true;
            }
            return false;
        }
    }

    public enum ForeignSource { None, ModularAvatar, VRCFury }

    /// <summary>
    /// What a move whose target path does not exist yet should do. Build defers it, because another tool may still
    /// install a submenu of that name and creating one here would leave the avatar with two. Inspector creates it,
    /// so the tree shows where the item is going while the creator is still editing.
    /// </summary>
    public enum MenuEditMode { Build, Inspector }

    /// <summary>One control slot of a node: an existing control, a submenu, or an AvatarDoctor control.</summary>
    public sealed class MenuItem
    {
        public MenuNode Submenu;
        public VRCExpressionsMenu.Control Existing;
        public ControlNode Control;
        /// <summary>Plain name of this item, filled for every existing item so the writer and menu edits have one place to read from.</summary>
        public string Name = "";
        /// <summary>The raw name, tags and all, of this item. Empty when nothing has set it.</summary>
        public string RichName = "";
        public Texture2D Icon;
        /// <summary>The plain path this item had right after every component installed, before any menu edit moved it. Menu edits resolve against this, never the current path.</summary>
        public string OriginalPath = "";
        /// <summary>Which of the items sharing this OriginalPath this one is, in tree order, stamped before any menu edit runs. A row that outlives a plan rebuild still carries the right number.</summary>
        public int Occurrence;
        /// <summary>The menu edit that changed this item, if any.</summary>
        public MenuEdit Edit;
        /// <summary>Removed from the built menu by a menu edit. The item stays in Items so the inspector can still show it.</summary>
        public bool Hidden;
        /// <summary>Not built on the platform the editor targets. The item stays in Items so the inspector can still show it, dimmed, but nothing that writes the menu or counts slots sees it.</summary>
        public bool Excluded;
        public ForeignSource Foreign;
        /// <summary>The install this item came in with, when it is a control of a menu asset AvatarDoc places. Null for everything else.</summary>
        public MenuAssetInstall Asset;
        /// <summary>The Modular Avatar or VRCFury component that installs this foreign item, so a "Convert to AvatarDoc" action can find its import candidate. Null for anything that is not foreign.</summary>
        public Component Origin;
    }

    public sealed class ControlNode
    {
        public AvatarDoc Component;
        public MenuEntry Entry;
        public string Path = "";
        public string LayerName = "";
        /// <summary>Not built on the platform the editor targets. The control stays in MenuTree.Controls so its parameter is still planned, in the same place and with the same value.</summary>
        public bool Excluded;
        /// <summary>The component this control belongs to is switched off, so the build never sees it and nothing is planned for it: no name, no bits, no slot. The row stays in the tree, always Excluded as well.</summary>
        public bool Disabled;
    }

    public sealed class TriggerNode
    {
        public AvatarDoc Component;
        public MenuEntry Entry;
        /// <summary>Not built on the platform the editor targets. A trigger holds no parameter of its own, so it is skipped entirely.</summary>
        public bool Excluded;
    }

    /// <summary>One animator controller merged into the avatar's FX controller.</summary>
    public sealed class MergeNode
    {
        public AvatarDoc Component;
        public AnimatorMerge Merge;
        /// <summary>Not built on the platform the editor targets. Its layers are left out; the parameters its controls declare are planned either way.</summary>
        public bool Excluded;
    }

    /// <summary>One finished menu asset installed into the merged menu.</summary>
    public sealed class MenuAssetNode
    {
        public AvatarDoc Component;
        public MenuAssetInstall Install;
        /// <summary>Not built on the platform the editor targets. Its items are still read into the tree, flagged the same way, so both platforms see the same shape; nothing that writes the menu or counts slots takes them.</summary>
        public bool Excluded;
    }

    /// <summary>An override rule: blend shapes held at a value in every animation that uses them.</summary>
    public sealed class OverrideNode
    {
        public AvatarDoc Component;
        public MenuEntry Entry;
        /// <summary>Not built on the platform the editor targets. An override holds no parameter of its own, so it is skipped entirely.</summary>
        public bool Excluded;
    }

    /// <summary>A menu edit whose source did not resolve while the model was built. Later build passes try it again on the real menu assets.</summary>
    public sealed class PendingEdit
    {
        public AvatarDoc Component;
        public MenuEdit Edit;
        /// <summary>Name of the component's object, taken while it was still alive. The late passes run after every AvatarDoc has been destroyed, so they can never read the component again.</summary>
        public string ComponentName = "";
#if DOCME_NDMF
        /// <summary>Where the component sat, taken while it was still alive, so a late message can still point at it.</summary>
        public nadena.dev.ndmf.ObjectReference Reference;
#endif

        /// <summary>What a late message names the source by. Never the component itself: that one is gone by then.</summary>
        public object Context
        {
#if DOCME_NDMF
            get { return Reference != null ? (object)Reference : ComponentName; }
#else
            get { return ComponentName; }
#endif
        }
    }

    public sealed class MenuTree
    {
        public MenuNode Root;
        public List<ControlNode> Controls = new List<ControlNode>();
        public List<TriggerNode> Triggers = new List<TriggerNode>();
        public List<OverrideNode> Overrides = new List<OverrideNode>();
        public List<MergeNode> Merges = new List<MergeNode>();
        public List<MenuAssetNode> MenuAssets = new List<MenuAssetNode>();
        public List<ModelMessage> Messages = new List<ModelMessage>();
        public List<AvatarDoc> Components = new List<AvatarDoc>();
        public List<PendingEdit> PendingEdits = new List<PendingEdit>();
        /// <summary>The avatar this tree belongs to. Null when the model is built without an avatar, as in the inspector.</summary>
        public Transform AvatarRoot;
#if DOCME_NDMF
        /// <summary>What the removed controls of this tree take with them. Filled by the inspector plan and by the build from the same planner, so they cannot disagree.</summary>
        public RemovalPlan Removal;
#endif
        /// <summary>True when two entries of this avatar carry the same internal id, usually because a component was copied.</summary>
        public bool HasDuplicateIds;

        ShapeSetModel.HeldContext heldContext;

        /// <summary>What the shape set rules of this tree read from its avatar, read once per tree.</summary>
        public ShapeSetModel.HeldContext HeldContext => heldContext ?? (heldContext = ShapeSetModel.ContextFor(AvatarRoot));

        public bool HasErrors => Messages.Any(m => m.Severity == MessageSeverity.Error);

        /// <summary>The control with this entry id, or null when there is none or when several entries share the id.</summary>
        public ControlNode FindControl(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            ControlNode found = null;
            foreach (var control in Controls)
            {
                if (control.Entry.id != id) continue;
                if (found != null) return null;
                found = control;
            }
            return found;
        }

        public ExclusiveGroup FindGroup(string name)
        {
            foreach (var component in Components)
            {
                if (MenuModel.IsDisabled(component)) continue;
                foreach (var group in component.groups)
                    if (group.name == name) return group;
            }
            return null;
        }

        public IEnumerable<MenuNode> AllNodes()
        {
            var stack = new Stack<MenuNode>();
            stack.Push(Root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                yield return node;
                foreach (var item in node.Items)
                    if (item.Submenu != null) stack.Push(item.Submenu);
            }
        }
    }

    public static class MenuModel
    {
        public static List<AvatarDoc> CollectComponents(Transform avatarRoot)
        {
            return avatarRoot.GetComponentsInChildren<AvatarDoc>(true).Where(c => c.enabled).ToList();
        }

        /// <summary>
        /// A switched off component is skipped by the build. The inspector still shows its rows, flagged Excluded,
        /// but plans nothing from it: its parameters, bits, removals, menu edits and menu slots are not counted,
        /// or the names and the memory bar would describe an avatar that is never built.
        /// </summary>
        public static bool IsDisabled(AvatarDoc component) => component != null && !component.enabled;

        public static string[] SplitPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return Array.Empty<string>();
            return path.Split('/').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        }

        /// <summary>True when a target sits outside the avatar being built. Always false when no avatar root is known.</summary>
        public static bool IsOutsideAvatar(Transform avatarRoot, Transform target)
        {
            return avatarRoot != null && target != null && !target.IsChildOf(avatarRoot);
        }

        public static MenuTree Build(Transform avatarRoot, VRCExpressionsMenu existingRoot, IReadOnlyList<AvatarDoc> components, IReadOnlyList<ForeignItem> foreign = null, MenuEditMode editMode = MenuEditMode.Build)
        {
            var tree = new MenuTree { Components = components.ToList(), AvatarRoot = avatarRoot };
            tree.Root = existingRoot != null ? ReadExisting(existingRoot, new HashSet<VRCExpressionsMenu>(), "") : new MenuNode { Name = "Expressions" };

            foreach (var component in components)
                Install(tree, component);

            AssignOriginalPaths(tree.Root, "");
            if (foreign != null && foreign.Count > 0)
            {
                foreach (var item in foreign) AddForeignItem(tree.Root, item);
                AssignOriginalPaths(tree.Root, "");
            }
            StampOccurrences(tree.Root, new Dictionary<string, int>());
            MenuEdits.Apply(tree, editMode);

            ReportDuplicateIds(tree, components);
            AssignNodePaths(tree);
            CollectControls(tree, tree.Root, "");
            AssignLayerNames(tree);
            MarkExcluded(tree.Root);
            ValidateTargets(tree);
            ValidateMerges(tree);
            ReportSharedTargets(tree);
            ReportOverrideConflicts(tree);
            ReportUnreachableHiddenToggles(tree);
            ValidateFollowCounts(tree);
            ValidateIcons(tree);
            return tree;
        }

        /// <summary>The existing menu asset read as a plain node tree, without installing any AvatarDoc into it. Used to resolve a foreign installer's target menu before the real build runs.</summary>
        public static MenuNode ReadExistingRoot(VRCExpressionsMenu existingRoot)
        {
            return existingRoot != null ? ReadExisting(existingRoot, new HashSet<VRCExpressionsMenu>(), "") : new MenuNode { Name = "Expressions" };
        }

        /// <summary>Places one foreign item at its path, creating (or reusing, by plain name) a submenu for every segment before the last.</summary>
        static void AddForeignItem(MenuNode root, ForeignItem foreignItem)
        {
            if (foreignItem.Path == null || foreignItem.Path.Count == 0) return;
            var node = root;
            for (int i = 0; i < foreignItem.Path.Count - 1; i++)
                node = FindOrCreateForeignSubmenu(node, foreignItem.Path[i], foreignItem.Source, null, null);

            var lastName = foreignItem.Path[foreignItem.Path.Count - 1];
            var plain = RichName.Strip(lastName);
            if (foreignItem.IsSubmenu)
            {
                FindOrCreateForeignSubmenu(node, plain, foreignItem.Source, foreignItem.RichName, foreignItem.Icon);
                return;
            }

            node.Items.Add(new MenuItem
            {
                Foreign = foreignItem.Source,
                Name = plain,
                RichName = string.IsNullOrEmpty(foreignItem.RichName) ? plain : foreignItem.RichName,
                Icon = foreignItem.Icon,
                OriginalPath = string.Join("/", foreignItem.Path.Select(RichName.Strip)),
                Origin = foreignItem.Origin,
            });
        }

        /// <summary>Finds a submenu by its plain name under parent, whatever created it, or makes a new one marked as coming from source. Name and icon of a submenu item live on its MenuNode, matching every other submenu item.</summary>
        static MenuNode FindOrCreateForeignSubmenu(MenuNode parent, string name, ForeignSource source, string richName, Texture2D icon)
        {
            var plain = RichName.Strip(name);
            var found = parent.Items.FirstOrDefault(i => i.Submenu != null && i.Submenu.Name == plain);
            if (found != null) return found.Submenu;
            var subNode = new MenuNode { Name = plain, RichName = string.IsNullOrEmpty(richName) ? plain : richName, Icon = icon };
            parent.Items.Add(new MenuItem { Submenu = subNode, Foreign = source });
            return subNode;
        }

        /// <summary>True for an item that counts toward the SDK's per-page limit as far as AvatarDoctor's own split is concerned. Hidden items, items left out on this platform, a toggle or radial hidden from the menu, and foreign items (the other tool pages those itself) never count.</summary>
        static bool CountsTowardSplit(MenuItem item) => !item.Hidden && !item.Excluded && item.Foreign == ForeignSource.None && !IsHiddenFromMenu(item);

        /// <summary>Whether this item's control has no menu control of its own: a toggle or radial the creator hid from the menu, or a grabbable whose menu toggle is off. It takes no slot, but keeps its parameter, layer and place in the tree.</summary>
        static bool IsHiddenFromMenu(MenuItem item) => item.Control != null && item.Control.Entry.NoMenuControl;

        /// <summary>Moves controls beyond the SDK limit into chained "More" submenus. Existing untouched nodes are left alone. Hidden and foreign items do not count against the limit and are not counted as split candidates on their own; a foreign item past the cut stays on the node it was added to (after the new "More" entry), since the other tool pages its own items itself.</summary>
        public static void SplitOverflow(MenuTree tree, Texture2D moreIcon)
        {
            foreach (var node in tree.AllNodes().ToList())
            {
                if (node.IsExisting && !node.ContainsGenerated()) continue;
                if (node.Items.Count(CountsTowardSplit) <= VRCExpressionsMenu.MAX_CONTROLS) continue;
                tree.Messages.Add(ModelMessage.Info("ad.info.menu_split", node.Name));
                var current = node;
                while (current.Items.Count(CountsTowardSplit) > VRCExpressionsMenu.MAX_CONTROLS)
                {
                    int keep = VRCExpressionsMenu.MAX_CONTROLS - 1;
                    var more = new MenuNode
                    {
                        Name = "More",
                        IsMorePage = true,
                        Icon = moreIcon,
                        Component = node.Component,
                        Parent = current,
                        Path = current.Path.Length == 0 ? "More" : current.Path + "/More",
                    };
                    int cut = VisibleCutIndex(current.Items, keep);
                    var tail = current.Items.Skip(cut).ToList();
                    more.Items.AddRange(tail.Where(i => i.Foreign == ForeignSource.None));
                    var stayingForeign = tail.Where(i => i.Foreign != ForeignSource.None).ToList();
                    current.Items.RemoveRange(cut, current.Items.Count - cut);
                    current.Items.Add(new MenuItem { Submenu = more });
                    current.Items.AddRange(stayingForeign);
                    current = more;
                }
            }
            AssignNodePaths(tree);
        }

        /// <summary>Index right after the keepVisible-th counted item, counting hidden and foreign items as part of whatever side they land on.</summary>
        static int VisibleCutIndex(List<MenuItem> items, int keepVisible)
        {
            int seen = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (!CountsTowardSplit(items[i])) continue;
                if (seen == keepVisible) return i;
                seen++;
            }
            return items.Count;
        }

        static MenuNode ReadExisting(VRCExpressionsMenu menu, HashSet<VRCExpressionsMenu> visited, string path, MenuAssetInstall asset = null, AvatarDoc component = null)
        {
            var node = new MenuNode { Name = menu.name, IsExisting = true, ExistingMenu = menu, Asset = asset, Component = component };
            if (!visited.Add(menu)) return node;
            foreach (var control in menu.controls ?? new List<VRCExpressionsMenu.Control>())
            {
                var plain = RichName.Strip(control.name);
                var itemPath = path.Length == 0 ? plain : path + "/" + plain;
                var item = new MenuItem { Existing = control, Name = plain, RichName = control.name ?? "", Icon = control.icon, OriginalPath = itemPath, Asset = asset };
                if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu && control.subMenu != null)
                {
                    item.Submenu = ReadExisting(control.subMenu, visited, itemPath, asset, component);
                    // Plain in Name, styled in RichName: paths, install targets and move targets are all plain,
                    // and a styled name would put its own markup into every one of them.
                    item.Submenu.Name = plain;
                    item.Submenu.RichName = control.name ?? "";
                    item.Submenu.Icon = control.icon;
                }
                node.Items.Add(item);
            }
            visited.Remove(menu);
            return node;
        }

        /// <summary>Gives every item still missing one the plain path it has right after every component installed, before any menu edit runs. Existing items already carry theirs from ReadExisting. Also stamps an existing or foreign submenu's own OriginalPath, so its row and expansion key stay put across a later move.</summary>
        /// <summary>Numbers the items that share an original path in the order menu edits look them up: an item first, then what is inside it.</summary>
        static void StampOccurrences(MenuNode node, Dictionary<string, int> seen)
        {
            foreach (var item in node.Items)
            {
                var key = item.OriginalPath ?? "";
                seen.TryGetValue(key, out int count);
                item.Occurrence = count;
                seen[key] = count + 1;
                if (item.Submenu != null) StampOccurrences(item.Submenu, seen);
            }
        }

        static void AssignOriginalPaths(MenuNode node, string path)
        {
            foreach (var item in node.Items)
            {
                if (string.IsNullOrEmpty(item.OriginalPath))
                {
                    var name = item.Submenu != null ? item.Submenu.Name : item.Control != null ? item.Control.Entry.PlainName : item.Name;
                    item.OriginalPath = path.Length == 0 ? name : path + "/" + name;
                }
                if (item.Submenu != null)
                {
                    if ((item.Submenu.IsExisting || item.Foreign != ForeignSource.None) && string.IsNullOrEmpty(item.Submenu.OriginalPath))
                        item.Submenu.OriginalPath = item.OriginalPath;
                    AssignOriginalPaths(item.Submenu, item.OriginalPath);
                }
            }
        }

        /// <summary>Finds a submenu by its plain name, or makes one. Matching stays plain so a styled name still lands in the same submenu, while a created node carries the styled text VRChat shows.</summary>
        internal static MenuNode FindOrCreateSubmenu(MenuNode parent, string name, string richName, Texture2D icon, AvatarDoc component, out bool created)
        {
            var found = parent.Items.FirstOrDefault(i => i.Submenu != null && i.Submenu.Name == name);
            created = found == null;
            if (created)
            {
                found = new MenuItem { Submenu = new MenuNode { Name = name, RichName = string.IsNullOrEmpty(richName) ? name : richName, Icon = icon, Component = component } };
                parent.Items.Add(found);
            }
            return found.Submenu;
        }

        static void Install(MenuTree tree, AvatarDoc component)
        {
            var target = tree.Root;
            var targetPath = "";
            foreach (var segment in SplitPath(component.installPath))
            {
                target = FindOrCreateSubmenu(target, segment, segment, null, component, out bool created);
                targetPath = targetPath.Length == 0 ? segment : targetPath + "/" + segment;
                if (created) tree.Messages.Add(ModelMessage.Info("ad.info.path_created", segment, component));
            }

            if (component.installMode == InstallMode.AsSubmenu)
            {
                var name = RichName.Strip(component.EffectiveMenuName);
                target = FindOrCreateSubmenu(target, name, component.EffectiveMenuName, component.menuIcon, component, out bool created);
                targetPath = targetPath.Length == 0 ? name : targetPath + "/" + name;
                if (!created && !target.IsExisting)
                    tree.Messages.Add(ModelMessage.Info("ad.info.same_submenu_merged", name, component));
            }

            bool disabled = IsDisabled(component);
            foreach (var merge in component.animators)
                if (merge != null)
                    tree.Merges.Add(new MergeNode { Component = component, Merge = merge, Excluded = disabled || !Platform.Included(component, merge) });

            var byParent = new Dictionary<string, List<MenuEntry>>();
            var submenuIds = new HashSet<string>(component.entries.Where(e => e.kind == EntryKind.Submenu).Select(e => e.id));
            foreach (var entry in component.entries)
            {
                if (entry.kind == EntryKind.Trigger)
                {
                    tree.Triggers.Add(new TriggerNode { Component = component, Entry = entry, Excluded = disabled || !Platform.Included(component, entry) });
                    continue;
                }
                if (entry.kind == EntryKind.Override)
                {
                    tree.Overrides.Add(new OverrideNode { Component = component, Entry = entry, Excluded = disabled || !Platform.Included(component, entry) });
                    continue;
                }
                var parent = entry.parentId ?? "";
                if (parent.Length > 0 && (!submenuIds.Contains(parent) || IsInCycle(component, entry, submenuIds)))
                {
                    tree.Messages.Add(ModelMessage.Warning("ad.warn.orphan_entry", entry.PlainName, component).For(entry.id));
                    parent = "";
                }
                if (!byParent.TryGetValue(parent, out var list)) byParent[parent] = list = new List<MenuEntry>();
                list.Add(entry);
            }

            Place(tree, component, target, "", byParent, new HashSet<string>());
            InstallMenuAssets(tree, component, target, targetPath);
        }

        /// <summary>
        /// Installs this component's finished menu assets. Each one is read with the reader the avatar's own menu
        /// uses, so its items carry original paths and menu edits resolve on them in the first pass, and every node
        /// it makes is marked as coming from an asset, so the writer always clones instead of handing the asset on.
        /// </summary>
        static void InstallMenuAssets(MenuTree tree, AvatarDoc component, MenuNode componentTarget, string componentPath)
        {
            foreach (var install in component.menuAssets)
            {
                if (install == null) continue;
                bool excluded = IsDisabled(component) || !Platform.Included(component, install);
                tree.MenuAssets.Add(new MenuAssetNode { Component = component, Install = install, Excluded = excluded });
                if (install.menu == null)
                {
                    tree.Messages.Add(ModelMessage.Warning("ad.warn.menu_asset_missing", component).For(install.id));
                    continue;
                }
                var target = componentTarget;
                var path = componentPath;
                if (!string.IsNullOrWhiteSpace(install.installPath))
                {
                    target = tree.Root;
                    path = "";
                    foreach (var segment in SplitPath(install.installPath))
                    {
                        target = FindOrCreateSubmenu(target, segment, segment, null, component, out bool created);
                        path = path.Length == 0 ? segment : path + "/" + segment;
                        if (created) tree.Messages.Add(ModelMessage.Info("ad.info.path_created", segment, component));
                    }
                }

                var visited = new HashSet<VRCExpressionsMenu>();
                if (install.asSubmenu)
                {
                    var plain = install.PlainName;
                    var itemPath = path.Length == 0 ? plain : path + "/" + plain;
                    var node = ReadExisting(install.menu, visited, itemPath, install, component);
                    node.Name = plain;
                    node.RichName = install.EffectiveName;
                    node.Icon = install.icon;
                    node.OriginalPath = itemPath;
                    node.Excluded = excluded;
                    var item = new MenuItem
                    {
                        Submenu = node,
                        Asset = install,
                        Name = plain,
                        RichName = install.EffectiveName,
                        Icon = install.icon,
                        OriginalPath = itemPath,
                        Excluded = excluded,
                    };
                    if (excluded) ExcludeItems(node);
                    target.Items.Add(item);
                }
                else
                {
                    var read = ReadExisting(install.menu, visited, path, install, component);
                    if (excluded) ExcludeItems(read);
                    target.Items.AddRange(read.Items);
                }
            }
        }

        /// <summary>
        /// Flags everything a menu asset brought as not built on this platform. The items stay in the tree, so
        /// readers, occurrences and submenu matching read the same on PC and on Quest, while the writer, the
        /// slot count and the undeclared parameter check all skip them.
        /// </summary>
        static void ExcludeItems(MenuNode node)
        {
            foreach (var item in node.Items)
            {
                item.Excluded = true;
                if (item.Submenu != null)
                {
                    item.Submenu.Excluded = true;
                    ExcludeItems(item.Submenu);
                }
            }
        }

        static bool IsInCycle(AvatarDoc component, MenuEntry entry, HashSet<string> submenuIds)
        {
            var seen = new HashSet<string> { entry.id };
            var current = component.FindEntry(entry.parentId);
            while (current != null)
            {
                if (!seen.Add(current.id)) return true;
                if (!submenuIds.Contains(current.id)) return false;
                current = component.FindEntry(current.parentId);
            }
            return false;
        }

        static void Place(MenuTree tree, AvatarDoc component, MenuNode target, string parentId, Dictionary<string, List<MenuEntry>> byParent, HashSet<string> placed)
        {
            if (!byParent.TryGetValue(parentId, out var entries)) return;
            foreach (var entry in entries)
            {
                if (!placed.Add(entry.id)) continue;
                bool disabled = IsDisabled(component);
                bool excluded = disabled || !Platform.Included(component, entry);
                if (entry.kind == EntryKind.Submenu)
                {
                    var node = MergeTarget(target, entry);
                    if (node != null)
                    {
                        node.Component = component;
                        node.EntryId = entry.id;
                        if (entry.name != entry.PlainName) node.RichName = entry.name;
                        if (entry.icon != null) node.Icon = entry.icon;
                        tree.Messages.Add(ModelMessage.Info("ad.info.same_submenu_merged", entry.PlainName, component).For(entry.id));
                    }
                    else
                    {
                        node = new MenuNode { Name = entry.PlainName, RichName = entry.name, Icon = entry.icon, Component = component, EntryId = entry.id };
                        target.Items.Add(new MenuItem { Submenu = node });
                    }
                    node.Excluded = excluded;
                    Place(tree, component, node, entry.id, byParent, placed);
                }
                else
                {
                    target.Items.Add(new MenuItem { Control = new ControlNode { Component = component, Entry = entry, Excluded = excluded, Disabled = disabled } });
                }
            }
        }

        /// <summary>
        /// The submenu of the avatar's own menu a submenu entry of this name should become, instead of a second
        /// submenu beside it. Only a plain existing node counts: one another tool installs is that tool's to write,
        /// and one another entry already took is that entry's.
        /// </summary>
        static MenuNode MergeTarget(MenuNode parent, MenuEntry entry)
        {
            foreach (var item in parent.Items)
            {
                if (item.Submenu == null || item.Foreign != ForeignSource.None) continue;
                if (!item.Submenu.IsExisting || item.Submenu.EntryId != null) continue;
                // A menu asset's submenu belongs to that asset: an entry of the same name stays beside it.
                if (item.Submenu.Asset != null) continue;
                if (item.Submenu.Name == entry.PlainName) return item.Submenu;
            }
            return null;
        }

        /// <summary>A copied component keeps the entry ids of its source. Controls stay separate, so this only needs saying once per copy.</summary>
        static void ReportDuplicateIds(MenuTree tree, IReadOnlyList<AvatarDoc> components)
        {
            var seen = new HashSet<string>();
            foreach (var component in components)
            {
                foreach (var entry in component.entries)
                {
                    if (string.IsNullOrEmpty(entry.id)) continue;
                    if (seen.Add(entry.id)) continue;
                    tree.HasDuplicateIds = true;
                    tree.Messages.Add(ModelMessage.Warning("ad.warn.duplicate_entry_id", entry.PlainName, component).For(entry.id));
                }
            }
        }

        /// <summary>Recomputes Parent and Path on every node from the root down. Re-run after SplitOverflow moves nodes.</summary>
        public static void AssignNodePaths(MenuTree tree)
        {
            AssignNodePaths(tree.Root, null, "");
        }

        static void AssignNodePaths(MenuNode node, MenuNode parent, string path)
        {
            node.Parent = parent;
            node.Path = path;
            foreach (var item in node.Items)
            {
                if (item.Submenu == null) continue;
                var child = item.Submenu;
                AssignNodePaths(child, node, path.Length == 0 ? child.Name : path + "/" + child.Name);
            }
        }

        static void CollectControls(MenuTree tree, MenuNode node, string path)
        {
            foreach (var item in node.Items)
            {
                if (item.Control != null)
                {
                    item.Control.Path = path.Length == 0 ? item.Control.Entry.PlainName : path + "/" + item.Control.Entry.PlainName;
                    tree.Controls.Add(item.Control);
                }
                else if (item.Submenu != null)
                {
                    CollectControls(tree, item.Submenu, path.Length == 0 ? item.Submenu.Name : path + "/" + item.Submenu.Name);
                }
            }
        }

        /// <summary>
        /// Carries every control's and submenu's platform exclusion onto the items that hold them. Returns true when
        /// nothing under this node is built on the current platform. An excluded submenu that still holds something
        /// built stays, or that item would have nowhere to live; one AvatarDoctor made whose whole content fell away
        /// goes with it. An item that carries an existing or a foreign control is never dropped: the tool did not
        /// create it, so it is not the tool's to take out of the avatar's menu.
        /// </summary>
        static bool MarkExcluded(MenuNode node)
        {
            bool anyKept = false;
            foreach (var item in node.Items)
            {
                if (item.Submenu != null)
                {
                    bool empty = MarkExcluded(item.Submenu);
                    // An item carrying a menu asset was flagged where the asset was installed, and that flag
                    // follows the asset's own platform choice rather than what is left under it.
                    bool ours = item.Existing == null && item.Foreign == ForeignSource.None && item.Asset == null;
                    if (ours) item.Excluded = empty && (item.Submenu.Excluded || item.Submenu.Items.Count > 0);
                    // Another component may have installed into an excluded asset's submenu by name. Its items
                    // survive, so the submenu is written for them; the asset's own items stay flagged one by one.
                    else if (item.Asset != null && item.Excluded) item.Excluded = empty;
                }
                else if (item.Control != null)
                {
                    item.Excluded = item.Control.Excluded;
                }
                // A hidden item is not written either, so it cannot be the reason a submenu is kept.
                if (!item.Excluded && !item.Hidden) anyKept = true;
            }
            return !anyKept;
        }

        static void AssignLayerNames(MenuTree tree)
        {
            var used = new HashSet<string>();
            foreach (var control in tree.Controls)
            {
                var name = "AD: " + control.Entry.PlainName;
                var candidate = name;
                for (int i = 2; !used.Add(candidate); i++) candidate = name + " (" + i + ")";
                control.LayerName = candidate;
            }
        }

        /// <summary>Targets of an entry that is not built on this platform are left alone: on a copy made for that platform the pieces are usually missing, and saying so every time would bury the real warnings.</summary>
        static void ValidateTargets(MenuTree tree)
        {
            foreach (var control in tree.Controls)
            {
                if (control.Excluded) continue;
                if (control.Entry.kind == EntryKind.Preset) ValidatePreset(tree, control.Component, control.Entry);
                else ValidateEntry(tree, control.Component, control.Entry);
            }
            foreach (var trigger in tree.Triggers)
                if (!trigger.Excluded) ValidateEntry(tree, trigger.Component, trigger.Entry);
            foreach (var rule in tree.Overrides)
                if (!rule.Excluded) ValidateOverride(tree, rule.Component, rule.Entry);
        }

        /// <summary>
        /// Checks one merged animator's controller, its path root and the layer names it leaves out. A merge that
        /// is not built on this platform is left alone, like every other excluded piece. Messages name the merge
        /// by its own id, so the inspector can show them on its row the way it does for entries.
        /// </summary>
        static void ValidateMerges(MenuTree tree)
        {
            foreach (var node in tree.Merges)
            {
                if (node.Excluded) continue;
                var merge = node.Merge;
                if (merge.controller == null)
                {
                    tree.Messages.Add(ModelMessage.Warning("ad.warn.merge_no_controller", node.Component).For(merge.id));
                    continue;
                }
                if (IsOutsideAvatar(tree.AvatarRoot, merge.pathRoot))
                {
                    tree.Messages.Add(ModelMessage.Error("ad.error.merge_outside_avatar", merge.controller.name, node.Component).For(merge.id));
                    continue;
                }
                if (MergeBuilder.Resolve(merge.controller) == null)
                {
                    tree.Messages.Add(ModelMessage.Warning("ad.warn.merge_override_no_controller", merge.controller.name, node.Component).For(merge.id));
                    continue;
                }
                var layers = MergeBuilder.LayerNames(merge.controller);
                foreach (var skipped in merge.skipLayers)
                {
                    if (string.IsNullOrEmpty(skipped)) continue;
                    if (!layers.Contains(skipped))
                        tree.Messages.Add(ModelMessage.Warning("ad.warn.merge_skip_missing", skipped, merge.controller.name, node.Component).For(merge.id));
                }
            }
        }

        /// <summary>
        /// Mirrors the VRChat SDK's own "Images used for Actions & Moods are too large" build check for every icon
        /// an AvatarDoc component sets itself: an entry's own icon, and a component's own submenu icon when it
        /// installs as a submenu. A foreign or existing icon is left alone, since fixing it is not this build's to
        /// make. Reads only the texture's own import settings, never rebuilds anything.
        /// </summary>
        static void ValidateIcons(MenuTree tree)
        {
            foreach (var component in tree.Components)
            {
                if (component == null || IsDisabled(component)) continue;
                foreach (var entry in component.entries)
                    if (IconFailsUploadCheck(entry.icon, out int width, out int height))
                        tree.Messages.Add(ModelMessage.Error("ad.error.menu_icon_too_large", entry.PlainName, width, height, component).For(entry.id));
                if (component.installMode == InstallMode.AsSubmenu && IconFailsUploadCheck(component.menuIcon, out int subWidth, out int subHeight))
                    tree.Messages.Add(ModelMessage.Error("ad.error.menu_icon_too_large", component.EffectiveMenuName, subWidth, subHeight, component));
            }
        }

        /// <summary>The exact rule VRChat's own control panel validates icons with at upload: over 256 pixels on either side while the importer would still allow that, or a compression setting of Uncompressed.</summary>
        internal static bool IconFailsUploadCheck(Texture2D icon, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (icon == null) return false;
            width = icon.width;
            height = icon.height;
            var path = AssetDatabase.GetAssetPath(icon);
            if (string.IsNullOrEmpty(path) || !(AssetImporter.GetAtPath(path) is TextureImporter importer)) return false;
            var settings = importer.GetDefaultPlatformTextureSettings();
            const int maxIconSize = 256;
            if ((width > maxIconSize || height > maxIconSize) && settings.maxTextureSize > maxIconSize) return true;
            return settings.textureCompression == TextureImporterCompression.Uncompressed;
        }

        /// <summary>Whether anything on this avatar reads a parameter control's parameter. Without NDMF there is no build to read it, so nothing does.</summary>
        static bool ParameterHasAUser(MenuTree tree, string name)
        {
#if DOCME_NDMF
            return MergeBuilder.UsesParameter(tree, tree.AvatarRoot, name);
#else
            return false;
#endif
        }

        /// <summary>A preset has no targets of its own; it is empty when it sets no state and no named parameter. A state whose control was since deleted or renamed away warns too, so the inspector flags it as soon as the referenced entry disappears, not only at build time.</summary>
        static void ValidatePreset(MenuTree tree, AvatarDoc component, MenuEntry entry)
        {
            if (!entry.HasEffects) tree.Messages.Add(ModelMessage.Warning("ad.warn.preset_empty", entry.PlainName, component).For(entry.id));
            foreach (var state in entry.presetStates)
                if (FindBuilt(tree, component, state.entryId) == null)
                    tree.Messages.Add(ModelMessage.Warning("ad.warn.preset_target_missing", entry.PlainName, component).For(entry.id));
        }

        /// <summary>
        /// The control an entry of this component names, as the build finds it. The build's tree holds only the
        /// enabled components, so a toggle of a disabled one is as missing there as a deleted toggle, and the
        /// inspector, whose tree keeps the disabled rows, says the same. A disabled component's own entries are not
        /// validated at all (they are left out), so nothing is hidden from them here.
        /// </summary>
        static ControlNode FindBuilt(MenuTree tree, AvatarDoc component, string id)
        {
            var control = tree.FindControl(id);
            return control != null && control.Disabled && !IsDisabled(component) ? null : control;
        }

        /// <summary>An override's targets are validated like a toggle's, but only its blend shapes count: an override with objects or materials ignores them.</summary>
        static void ValidateOverride(MenuTree tree, AvatarDoc component, MenuEntry entry)
        {
            if (entry.blendShapes.Count == 0)
            {
                tree.Messages.Add(ModelMessage.Warning("ad.warn.no_effects", entry.PlainName, component).For(entry.id));
                return;
            }
            ValidateShapes(tree, entry, component);
        }

        static void ValidateShapes(MenuTree tree, MenuEntry entry, AvatarDoc component)
        {
            foreach (var target in entry.blendShapes)
            {
                if (!ShapeExists(target.renderer, target.shape)) tree.Messages.Add(ModelMessage.Warning("ad.warn.target_missing", entry.PlainName, component).For(entry.id));
                else if (IsOutsideAvatar(tree.AvatarRoot, target.renderer.transform)) tree.Messages.Add(ModelMessage.Warning("ad.warn.target_outside_avatar", entry.PlainName, component).For(entry.id));
                // A pinned shape writes nothing while the control is off, so an off value of its own would never
                // be reached: the two settings contradict each other rather than combine.
                if (target.explicitOff && target.mode == ShapeEffectMode.Pin)
                    tree.Messages.Add(ModelMessage.Error("ad.error.explicit_off_pin", entry.PlainName, target.shape, component).For(entry.id));
            }
        }

        /// <summary>
        /// A follow only makes sense on an entry with an on and off state: a radial has no such state, so a follow
        /// there is an error rather than a silently ignored row. Each row is then checked the way a constraint
        /// build would need it: a usable object with a rest parent to fall back to (so not the avatar root itself),
        /// a usable target that is not the object or one of its own descendants, neither outside the avatar, and
        /// the object not already carrying a constraint of its own.
        /// </summary>
        static void ValidateFollows(MenuTree tree, MenuEntry entry, AvatarDoc component)
        {
            if (entry.follows.Count == 0) return;
            // Follow distance never builds a Follows effect: the row stays, so a creator does not lose the data
            // switching modes, but nothing about a broken target here should block a build that never reads it.
            if (entry.IsFollowDistanceTrigger)
            {
                tree.Messages.Add(ModelMessage.Warning("ad.warn.follow_distance_no_follows", entry.PlainName, component).For(entry.id));
                return;
            }
            if (entry.kind != EntryKind.Toggle && entry.kind != EntryKind.Trigger)
            {
                tree.Messages.Add(ModelMessage.Error("ad.error.follow_not_on_off", entry.PlainName, component).For(entry.id));
                return;
            }
            foreach (var follow in entry.follows)
            {
                if (follow.target == null || follow.follows == null)
                {
                    tree.Messages.Add(ModelMessage.Warning("ad.warn.target_missing", entry.PlainName, component).For(entry.id));
                    continue;
                }
                if (follow.target.transform == tree.AvatarRoot)
                {
                    tree.Messages.Add(ModelMessage.Error("ad.error.follow_no_rest_parent", entry.PlainName, component).For(entry.id));
                    continue;
                }
                if (follow.target == follow.follows.gameObject || follow.follows.IsChildOf(follow.target.transform))
                {
                    tree.Messages.Add(ModelMessage.Error("ad.error.follow_self", entry.PlainName, component).For(entry.id));
                    continue;
                }
                if (IsOutsideAvatar(tree.AvatarRoot, follow.target.transform) || IsOutsideAvatar(tree.AvatarRoot, follow.follows))
                {
                    tree.Messages.Add(ModelMessage.Error("ad.error.follow_outside_avatar", entry.PlainName, component).For(entry.id));
                    continue;
                }
                if (HasExistingConstraint(follow.target))
                    tree.Messages.Add(ModelMessage.Error("ad.error.follow_existing_constraint", entry.PlainName, follow.target.name, component).For(entry.id));
            }
        }

        /// <summary>
        /// A grabbable needs an object inside the avatar with no constraint of its own already on it, and a
        /// humanoid rig with the hand bone (or bones) its grabHands choice needs. Each is its own error, at model
        /// time, so a build never reaches PhysBone or contact creation on a row the creator has not finished.
        /// </summary>
        static void ValidateGrabbable(MenuTree tree, AvatarDoc component, MenuEntry entry)
        {
            if (entry.grabObject == null)
            {
                tree.Messages.Add(ModelMessage.Error("ad.error.grab_no_object", entry.PlainName, component).For(entry.id));
                return;
            }
            if (IsOutsideAvatar(tree.AvatarRoot, entry.grabObject.transform))
            {
                tree.Messages.Add(ModelMessage.Error("ad.error.grab_outside_avatar", entry.PlainName, component).For(entry.id));
                return;
            }
            if (HasExistingConstraint(entry.grabObject))
                tree.Messages.Add(ModelMessage.Error("ad.error.follow_existing_constraint", entry.PlainName, entry.grabObject.name, component).For(entry.id));

            var containing = FindContainingPhysBone(tree.AvatarRoot, entry.grabObject.transform);
            if (containing != null)
                tree.Messages.Add(ModelMessage.Error("ad.error.grab_inside_physbone", entry.PlainName, containing.name, component).For(entry.id));

            bool needsLeft = entry.grabHands != GrabHands.Right;
            bool needsRight = entry.grabHands != GrabHands.Left;
            bool missingLeft = needsLeft && HumanoidHands.Hand(tree.AvatarRoot, true) == null;
            bool missingRight = needsRight && HumanoidHands.Hand(tree.AvatarRoot, false) == null;
            if (missingLeft || missingRight)
                tree.Messages.Add(ModelMessage.Error("ad.error.grab_no_hand", entry.PlainName, component).For(entry.id));
        }

        /// <summary>
        /// One object named by more follow rows than VRChat's own inline source slots can hold (0 is the rest
        /// place, so at most FollowPlan.MaxClaims rows may claim it) is an error naming the object and the count,
        /// rather than a build that silently drops a curve VRChat has no animatable binding for. Rows, not
        /// entries: FollowBuilder adds one source per row, so one entry naming the same object twice costs two
        /// slots, not one, and must count as two here or a second row can slip the count past what the builder
        /// actually does. Only rows that would otherwise be usable count toward the limit; one already refused for
        /// another reason never reaches here.
        /// </summary>
        static void ValidateFollowCounts(MenuTree tree)
        {
            var byTarget = new Dictionary<GameObject, List<MenuEntry>>();

            void Collect(MenuEntry entry, bool excluded)
            {
                if (excluded || entry.follows.Count == 0) return;
                if (entry.kind != EntryKind.Toggle && entry.kind != EntryKind.Trigger) return;
                foreach (var follow in entry.follows)
                {
                    if (follow.target == null || follow.follows == null) continue;
                    if (follow.target.transform == tree.AvatarRoot) continue;
                    if (follow.target == follow.follows.gameObject || follow.follows.IsChildOf(follow.target.transform)) continue;
                    if (IsOutsideAvatar(tree.AvatarRoot, follow.target.transform) || IsOutsideAvatar(tree.AvatarRoot, follow.follows)) continue;
                    if (!byTarget.TryGetValue(follow.target, out var list)) byTarget[follow.target] = list = new List<MenuEntry>();
                    list.Add(entry);
                }
            }

            foreach (var control in tree.Controls) Collect(control.Entry, control.Excluded);
            foreach (var trigger in tree.Triggers) Collect(trigger.Entry, trigger.Excluded);

            // A grabbable claims one source per allowed hand on the very same constraint, so its hands count
            // toward the same ceiling: leaving them out here would let a build reach a source VRChat has no slot
            // for without the model ever having named it.
            foreach (var control in tree.Controls)
            {
                var entry = control.Entry;
                if (control.Excluded || entry.kind != EntryKind.Grabbable || entry.grabObject == null) continue;
                if (IsOutsideAvatar(tree.AvatarRoot, entry.grabObject.transform)) continue;
                int hands = entry.grabHands == GrabHands.Both ? 2 : 1;
                if (!byTarget.TryGetValue(entry.grabObject, out var list)) byTarget[entry.grabObject] = list = new List<MenuEntry>();
                for (int i = 0; i < hands; i++) list.Add(entry);
            }

            foreach (var pair in byTarget)
            {
                var entries = pair.Value;
                if (entries.Count <= FollowPlan.MaxClaims) continue;
                var overflow = entries.Skip(FollowPlan.MaxClaims).ToList();
                var names = string.Join(", ", overflow.Select(e => "\"" + e.PlainName + "\""));
                tree.Messages.Add(ModelMessage.Error("ad.error.follow_too_many_sources", pair.Key.name, entries.Count.ToString(),
                        FollowPlan.MaxClaims.ToString(), names)
                    .For(overflow.Select(e => e.id).ToArray()));
            }
        }

        /// <summary>
        /// Which entry's own row a shared constraint's target credits, for "What this builds": the real build
        /// shares one constraint between every follow row and every grabbable that names the same object, so only
        /// the first entry to claim a target, in the same tree order ValidateFollowCounts already walks, is the
        /// one whose row counts it. An object already carrying a constraint of its own is never claimed at all,
        /// the same as the count itself already skips it.
        /// </summary>
        public static Dictionary<GameObject, string> ClaimedConstraintTargets(MenuTree tree)
        {
            var claims = new Dictionary<GameObject, string>();
            void Claim(GameObject target, string entryId)
            {
                if (target == null || HasExistingConstraint(target) || claims.ContainsKey(target)) return;
                claims[target] = entryId;
            }

            foreach (var control in tree.Controls)
            {
                var entry = control.Entry;
                if (control.Excluded) continue;
                if (entry.kind == EntryKind.Toggle)
                    foreach (var follow in entry.follows)
                        if (follow.target != null && follow.follows != null) Claim(follow.target, entry.id);
                if (entry.kind == EntryKind.Grabbable && entry.grabObject != null) Claim(entry.grabObject, entry.id);
            }
            foreach (var trigger in tree.Triggers)
            {
                if (trigger.Excluded) continue;
                foreach (var follow in trigger.Entry.follows)
                    if (follow.target != null && follow.follows != null) Claim(follow.target, trigger.Entry.id);
            }
            return claims;
        }

        /// <summary>
        /// The nearest VRCPhysBone above target, walking up to the avatar root, whose chain reaches target without
        /// ignoring it, or null. A grabbable's own new PhysBone would otherwise nest inside such a chain, with the
        /// belt (say) driving the same transform the grab constraint does, and nothing telling the creator why.
        /// </summary>
        public static Component FindContainingPhysBone(Transform avatarRoot, Transform target)
        {
            if (target == null) return null;
            for (var cursor = target.parent; cursor != null; cursor = cursor.parent)
            {
                var pb = cursor.GetComponent<VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone>();
                if (pb != null && (pb.ignoreTransforms == null || !pb.ignoreTransforms.Contains(target)))
                    return pb;
                if (cursor == avatarRoot) break;
            }
            return null;
        }

        /// <summary>True when the object already carries a constraint of its own: one of VRChat's constraint components, or a Unity constraint from another tool. A follow never touches such an object.</summary>
        public static bool HasExistingConstraint(GameObject go)
        {
            if (go == null) return false;
            if (go.GetComponent<VRC.SDK3.Dynamics.Constraint.Components.VRCParentConstraint>() != null) return true;
            if (go.GetComponent<VRC.SDK3.Dynamics.Constraint.Components.VRCPositionConstraint>() != null) return true;
            if (go.GetComponent<VRC.SDK3.Dynamics.Constraint.Components.VRCRotationConstraint>() != null) return true;
            if (go.GetComponent<VRC.SDK3.Dynamics.Constraint.Components.VRCScaleConstraint>() != null) return true;
            if (go.GetComponent<VRC.SDK3.Dynamics.Constraint.Components.VRCAimConstraint>() != null) return true;
            if (go.GetComponent<VRC.SDK3.Dynamics.Constraint.Components.VRCLookAtConstraint>() != null) return true;
            return go.GetComponent<UnityEngine.Animations.IConstraint>() != null;
        }

        /// <summary>
        /// A rule on anything but a toggle or a trigger with effects of its own is an error: a radial has two ends,
        /// which the rule's "while on" wording means nothing for, and a flip trigger has no effects to hold it with.
        /// A missing or outside-avatar mesh is an error too. Several rules for one mesh count as one here, the same
        /// merge the build follows.
        /// </summary>
        static void ValidateShapeSets(MenuTree tree, AvatarDoc component, MenuEntry entry)
        {
            if (entry.shapeSets == null || entry.shapeSets.Count == 0) return;
            if (!entry.OffersShapeSets)
            {
                tree.Messages.Add(ModelMessage.Error("ad.error.shape_set_not_a_toggle", entry.PlainName, component).For(entry.id));
                return;
            }
            foreach (var rule in entry.shapeSets)
            {
                if (rule == null) continue;
                if (rule.mesh == null || IsOutsideAvatar(tree.AvatarRoot, rule.mesh.transform))
                    tree.Messages.Add(ModelMessage.Error("ad.error.shape_set_outside_avatar", entry.PlainName, component).For(entry.id));
            }
            foreach (var rule in ShapeSetModel.EffectiveRules(entry))
            {
                // The cost regardless of the switch: a large rule the creator has turned off for now still warns,
                // since turning it back on does not shrink the mesh.
                int count = ShapeSetModel.CoveredShapeCount(entry, rule, tree.HeldContext);
                if (count > ShapeSetModel.LargeThreshold)
                    tree.Messages.Add(ModelMessage.Warning("ad.warn.shape_set_large", entry.PlainName, rule.mesh.name, count.ToString()).For(entry.id));
            }
        }

        static void ValidateEntry(MenuTree tree, AvatarDoc component, MenuEntry entry)
        {
            if (entry.kind == EntryKind.Trigger)
            {
                ValidateGate(tree, component, entry);
                ValidateWhen(tree, component, entry);
            }

            ValidateShapeSets(tree, component, entry);

            if (entry.kind == EntryKind.Grabbable)
            {
                ValidateGrabbable(tree, component, entry);
                return;
            }

            if (entry.IsFlipTrigger)
            {
                ValidateFlipTarget(tree, component, entry);
                return;
            }

            if (entry.IsParameterControl)
            {
                if (!ParameterHasAUser(tree, entry.parameterName))
                    tree.Messages.Add(ModelMessage.Warning("ad.warn.parameter_control_unused", entry.PlainName, entry.parameterName.Trim(), component).For(entry.id));
                return;
            }

            if (!entry.HasEffects)
            {
                // A toggle in an exclusive group with no targets is a legitimate "off" choice (such as a None
                // outfit), not an empty control left behind by accident, so it does not warn. Neither is a plain
                // switch a trigger uses as its gate: AddGateSwitch makes exactly this kind of toggle on purpose.
                bool legitimateOffChoice = entry.kind == EntryKind.Toggle && !string.IsNullOrWhiteSpace(entry.exclusiveGroup);
                bool gateSwitch = entry.kind == EntryKind.Toggle && IsGateTarget(tree, entry);
                if (!legitimateOffChoice && !gateSwitch) tree.Messages.Add(ModelMessage.Warning("ad.warn.no_effects", entry.PlainName, component).For(entry.id));
                return;
            }
            foreach (var target in entry.objects)
            {
                if (target.target == null) tree.Messages.Add(ModelMessage.Warning("ad.warn.target_missing", entry.PlainName, component).For(entry.id));
                else if (IsOutsideAvatar(tree.AvatarRoot, target.target.transform)) tree.Messages.Add(ModelMessage.Warning("ad.warn.target_outside_avatar", entry.PlainName, component).For(entry.id));
            }
            ValidateShapes(tree, entry, component);
            ValidateFollows(tree, entry, component);
            foreach (var target in entry.materials)
            {
                if (!SlotExists(target)) tree.Messages.Add(ModelMessage.Warning("ad.warn.target_missing", entry.PlainName, component).For(entry.id));
                else if (IsOutsideAvatar(tree.AvatarRoot, target.renderer.transform)) tree.Messages.Add(ModelMessage.Warning("ad.warn.target_outside_avatar", entry.PlainName, component).For(entry.id));
            }
            if (entry.BuildsProperties)
                foreach (var target in entry.properties)
                {
                    if (target.renderer == null) tree.Messages.Add(ModelMessage.Warning("ad.warn.target_missing", entry.PlainName, component).For(entry.id));
                    else if (IsOutsideAvatar(tree.AvatarRoot, target.renderer.transform)) tree.Messages.Add(ModelMessage.Warning("ad.warn.target_outside_avatar", entry.PlainName, component).For(entry.id));
                    else if (!PropertyExists(target, entry)) tree.Messages.Add(ModelMessage.Warning("ad.warn.property_missing", entry.PlainName, target.renderer.name, target.property).For(entry.id));
                    else if (MaterialsDisagree(target)) tree.Messages.Add(ModelMessage.Warning("ad.warn.property_differs", entry.PlainName, target.renderer.name, target.property).For(entry.id));
                }
            // Triggers, overrides and presets ignore custom animation clips; only a toggle or a radial ever merges them.
            if (entry.kind == EntryKind.Toggle || entry.kind == EntryKind.Radial)
                foreach (var target in entry.animations)
                {
                    if (target.clip == null) tree.Messages.Add(ModelMessage.Warning("ad.warn.target_missing", entry.PlainName, component).For(entry.id));
                    else if (IsOutsideAvatar(tree.AvatarRoot, target.pathRoot)) tree.Messages.Add(ModelMessage.Warning("ad.warn.target_outside_avatar", entry.PlainName, component).For(entry.id));
                    else if (CustomClips.IsProxy(target.clip)) tree.Messages.Add(ModelMessage.Warning("ad.warn.animation_proxy", entry.PlainName, target.clip.name).For(entry.id));
                }
            if (entry.kind == EntryKind.Toggle && entry.blendSeconds > 0f && !string.IsNullOrWhiteSpace(entry.exclusiveGroup))
                tree.Messages.Add(ModelMessage.Info("ad.info.blend_ignored_in_group", entry.PlainName).For(entry.id));
        }

        /// <summary>
        /// Checks a trigger's "when": the shape of its groups, and the Contact or PhysBone data each condition
        /// needs. A trigger that still works from its flat source fields is one condition here, so it is checked
        /// exactly as it always was; one with groups names the group and the place in every message.
        /// </summary>
        static void ValidateWhen(MenuTree tree, AvatarDoc component, MenuEntry entry)
        {
            if (entry.source == TriggerSource.BlendShape && entry.SmoothRangeCrossed)
                tree.Messages.Add(ModelMessage.Warning("ad.warn.smooth_range", entry.PlainName, entry.smoothStart.ToString("0.#"), entry.smoothFull.ToString("0.#"), component).For(entry.id));
            // A blend shape trigger or a Fade contact has one driver and reads only the card it is in: its other
            // conditions are requirements, and every condition outside that card is kept but never read, so a
            // stale or broken one there must not block a build that never looks at it.
            bool driven = entry.HasDriver;
            if (entry.UsesWhenGroups)
            {
                foreach (var group in entry.when)
                    if (group == null || group.all == null || group.all.All(c => c == null))
                        tree.Messages.Add(ModelMessage.Warning("ad.warn.condition_group_empty", entry.PlainName, component).For(entry.id));
                var groups = entry.EffectiveWhen;
                if (groups.Count == 0)
                {
                    if (!driven) tree.Messages.Add(ModelMessage.Warning("ad.warn.trigger_no_condition", entry.PlainName, component).For(entry.id));
                    return;
                }
                if (driven)
                {
                    // The card holds the driver too: the shape row of a blend shape trigger, the first contact of a Fade one.
                    int rows = entry.Requirements().Count() + 1;
                    if (rows > ConditionLogic.MaxPerGroup)
                        tree.Messages.Add(ModelMessage.Error("ad.error.trigger_too_complex", entry.PlainName, "1", (rows - 1 + (entry.HasGate ? 1 : 0)).ToString()).For(entry.id));
                }
                else if (ConditionLogic.TooComplex(groups, out int groupCount, out int leaveCount))
                    tree.Messages.Add(ModelMessage.Error("ad.error.trigger_too_complex", entry.PlainName, groupCount.ToString(), leaveCount.ToString()).For(entry.id));
            }

            var all = entry.EffectiveGroups().SelectMany(g => g.group.all.Select((condition, i) => (groupIndex: g.groupIndex, index: i, condition))).ToList();
            IEnumerable<(int groupIndex, int index, WhenCondition condition)> read = all;
            if (driven)
            {
                // A Fade contact's driver is the first condition it reads; a blend shape trigger's is its shape.
                var driver = entry.IsFollowDistanceTrigger ? all.Where(c => c.condition != null).Take(1) : Enumerable.Empty<(int groupIndex, int index, WhenCondition condition)>();
                read = driver.Concat(entry.Requirements());
            }
            foreach (var (groupIndex, i, condition) in read)
            {
                if (condition == null || !condition.IsTouch) continue;
                var name = entry.UsesWhenGroups ? entry.PlainName + ConditionLogic.Position(groupIndex, i) : entry.PlainName;
                ValidateTouchCondition(tree, component, entry, condition, name);
            }
        }

        /// <summary>Checks the Contact or PhysBone data one condition needs, whatever mode the trigger is in.</summary>
        static void ValidateTouchCondition(MenuTree tree, AvatarDoc component, MenuEntry entry, WhenCondition condition, string name)
        {
            if (condition.kind == WhenConditionKind.Contact)
            {
                if (condition.contactAnchor == null)
                    tree.Messages.Add(ModelMessage.Error("ad.error.contact_no_anchor", name, component).For(entry.id));
                else if (IsOutsideAvatar(tree.AvatarRoot, condition.contactAnchor))
                    tree.Messages.Add(ModelMessage.Error("ad.error.touch_outside_avatar", name, component).For(entry.id));
                if (condition.contactTags == null || condition.contactTags.Count == 0)
                    tree.Messages.Add(ModelMessage.Error("ad.error.contact_no_tags", name, component).For(entry.id));
                if (!condition.contactAllowSelf && !condition.contactAllowOthers)
                    tree.Messages.Add(ModelMessage.Error("ad.error.contact_nobody", name, component).For(entry.id));
            }
            else
            {
                if (condition.physBone == null)
                    tree.Messages.Add(ModelMessage.Error("ad.error.physbone_missing", name, component).For(entry.id));
                else if (IsOutsideAvatar(tree.AvatarRoot, condition.physBone.transform))
                    tree.Messages.Add(ModelMessage.Error("ad.error.touch_outside_avatar", name, component).For(entry.id));
            }
        }

        /// <summary>A flip target must resolve to a toggle: missing, or anything else, is an error. A toggle left out on this platform is not an error, since the same touch works on the other one.</summary>
        static void ValidateFlipTarget(MenuTree tree, AvatarDoc component, MenuEntry entry)
        {
            var target = FindBuilt(tree, component, entry.flipToggleId);
            if (target == null || target.Entry.kind != EntryKind.Toggle)
                tree.Messages.Add(ModelMessage.Error("ad.error.flip_target", entry.PlainName, component).For(entry.id));
            else if (target.Excluded)
                tree.Messages.Add(ModelMessage.Info("ad.info.flip_target_excluded", entry.PlainName, component).For(entry.id));
        }

        /// <summary>
        /// A trigger's menu switch: empty means always and needs no check. Otherwise it must resolve to a toggle
        /// that is not this same trigger's own flip target; a toggle excluded on this platform still works,
        /// because its parameter exists on both platforms either way. On a blend shape trigger and a Fade contact
        /// it is one more requirement, read like any other.
        /// </summary>
        static void ValidateGate(MenuTree tree, AvatarDoc component, MenuEntry entry)
        {
            if (string.IsNullOrEmpty(entry.gateToggleId)) return;
            var target = FindBuilt(tree, component, entry.gateToggleId);
            if (target == null || target.Entry.kind != EntryKind.Toggle || entry.gateToggleId == entry.flipToggleId)
                tree.Messages.Add(ModelMessage.Error("ad.error.gate_target", entry.PlainName, component).For(entry.id));
        }

        /// <summary>Whether any trigger of the avatar uses this entry as its gate, whatever platform that trigger builds on.</summary>
        /// <summary>A toggle a trigger reads: as its Only while gate, as its Toggle source, or as the toggle a Flip touch flips. Such a toggle needs no targets of its own, since the trigger's effects are what it switches.</summary>
        static bool IsGateTarget(MenuTree tree, MenuEntry entry) => tree.Triggers.Any(t => t.Entry.gateToggleId == entry.id
            || (t.Entry.source == TriggerSource.Toggle && t.Entry.sourceToggleId == entry.id)
            || (t.Entry.touchMode == TouchMode.FlipToggle && t.Entry.flipToggleId == entry.id));

        /// <summary>
        /// A toggle hidden from the menu that nothing can ever change: no flip trigger flips it, no preset sets it,
        /// and it does not start on. Conditions and gates only read a toggle, so they do not count.
        /// </summary>
        static void ReportUnreachableHiddenToggles(MenuTree tree)
        {
            var named = new HashSet<string>();
            foreach (var trigger in tree.Triggers)
            {
                var entry = trigger.Entry;
                if (!string.IsNullOrEmpty(entry.flipToggleId)) named.Add(entry.flipToggleId);
            }
            foreach (var control in tree.Controls)
            {
                if (control.Entry.kind != EntryKind.Preset) continue;
                foreach (var state in control.Entry.presetStates)
                    if (!string.IsNullOrEmpty(state.entryId)) named.Add(state.entryId);
            }
            foreach (var control in tree.Controls)
            {
                var entry = control.Entry;
                if (control.Excluded || entry.kind != EntryKind.Toggle || !entry.hiddenFromMenu || entry.defaultOn) continue;
                if (named.Contains(entry.id)) continue;
                tree.Messages.Add(ModelMessage.Info("ad.info.hidden_toggle_unreachable", entry.PlainName, control.Component).For(entry.id));
            }
        }

        /// <summary>Controls and triggers that write the same target fight each other while more than one is active, so the author gets one note per target. A group made only of overrides is left to ad.warn.override_conflict.</summary>
        static void ReportSharedTargets(MenuTree tree)
        {
            // Only what this platform builds can fight over a target; an excluded entry writes nothing here.
            var units = new List<(MenuEntry entry, AvatarDoc component)>();
            foreach (var control in tree.Controls) if (!control.Excluded) units.Add((control.Entry, control.Component));
            foreach (var trigger in tree.Triggers) if (!trigger.Excluded) units.Add((trigger.Entry, trigger.Component));
            foreach (var rule in tree.Overrides) if (!rule.Excluded) units.Add((rule.Entry, rule.Component));

            var byTarget = new Dictionary<string, List<(MenuEntry entry, AvatarDoc component)>>();
            var labels = new Dictionary<string, string>();
            foreach (var unit in units)
            {
                bool skipSourceShape = unit.entry.kind == EntryKind.Trigger && unit.entry.source == TriggerSource.BlendShape;
                foreach (var (key, label) in TargetKeys(tree, unit.entry, skipSourceShape))
                {
                    if (!byTarget.TryGetValue(key, out var list)) byTarget[key] = list = new List<(MenuEntry, AvatarDoc)>();
                    if (!list.Contains(unit)) list.Add(unit);
                    labels[key] = label;
                }
            }
            foreach (var pair in byTarget)
            {
                var count = pair.Value.Select(u => string.IsNullOrWhiteSpace(u.entry.exclusiveGroup) ? u.entry.id : "group:" + u.entry.exclusiveGroup.Trim()).Distinct().Count();
                if (count < 2) continue;
                if (pair.Value.All(u => u.entry.kind == EntryKind.Override)) continue;
                bool overridden = pair.Value.Any(u => u.entry.kind == EntryKind.Override);
                var names = string.Join(", ", pair.Value.Select(u => u.entry.PlainName));
                tree.Messages.Add(ModelMessage.Info(overridden ? "ad.info.target_shared_override" : "ad.info.target_shared", labels[pair.Key], names, pair.Value[pair.Value.Count - 1].component)
                    .For(pair.Value.Select(u => u.entry.id).ToArray()));
            }
        }

        /// <summary>Two overrides on the same shape both pin it in every clip, but only the later one's value survives, so each pair gets a note.</summary>
        static void ReportOverrideConflicts(MenuTree tree)
        {
            var byShape = new Dictionary<string, List<OverrideNode>>();
            var labels = new Dictionary<string, string>();
            foreach (var rule in tree.Overrides)
            {
                if (rule.Excluded) continue;
                foreach (var target in rule.Entry.blendShapes)
                {
                    if (!ShapeExists(target.renderer, target.shape) || IsOutsideAvatar(tree.AvatarRoot, target.renderer.transform)) continue;
                    var key = target.renderer.GetInstanceID() + ":" + target.shape;
                    if (!byShape.TryGetValue(key, out var list)) byShape[key] = list = new List<OverrideNode>();
                    if (!list.Contains(rule)) list.Add(rule);
                    labels[key] = target.renderer.name + "/" + target.shape;
                }
            }
            foreach (var pair in byShape)
            {
                var rules = pair.Value;
                for (int i = 1; i < rules.Count; i++)
                    tree.Messages.Add(ModelMessage.Warning("ad.warn.override_conflict", rules[i - 1].Entry.PlainName, rules[i].Entry.PlainName, labels[pair.Key], rules[i].Component)
                        .For(rules[i - 1].Entry.id, rules[i].Entry.id));
            }
        }

        /// <summary>Target keys of one entry (control or trigger). skipSourceShape drops the effect that repeats a link's own source, since that is already reported as a mistake elsewhere.</summary>
        static IEnumerable<(string key, string label)> TargetKeys(MenuTree tree, MenuEntry entry, bool skipSourceShape)
        {
            // A flip trigger writes nothing itself: whatever its target lists still hold is never built, so it
            // cannot fight another control over a target.
            if (entry.IsFlipTrigger) yield break;
            bool radial = entry.kind == EntryKind.Radial;
            bool overrideEntry = entry.kind == EntryKind.Override;
            if (!radial && !overrideEntry)
            {
                foreach (var t in entry.objects)
                    if (t.target != null && !IsOutsideAvatar(tree.AvatarRoot, t.target.transform))
                        yield return ("go:" + t.target.GetInstanceID(), t.target.name);
                foreach (var t in entry.materials)
                    if (SlotExists(t) && !IsOutsideAvatar(tree.AvatarRoot, t.renderer.transform))
                        yield return ("mat:" + t.renderer.GetInstanceID() + ":" + t.slot, t.renderer.name + " slot " + t.slot);
                foreach (var t in entry.follows)
                    if (t.target != null && !IsOutsideAvatar(tree.AvatarRoot, t.target.transform))
                        yield return ("follow:" + t.target.GetInstanceID(), t.target.name);
                if (entry.kind == EntryKind.Grabbable && entry.grabObject != null && !IsOutsideAvatar(tree.AvatarRoot, entry.grabObject.transform))
                    yield return ("go:" + entry.grabObject.GetInstanceID(), entry.grabObject.name);
            }
            foreach (var t in entry.blendShapes)
            {
                if (skipSourceShape && t.renderer == entry.sourceRenderer && t.shape == entry.sourceShape) continue;
                if (ShapeExists(t.renderer, t.shape) && !IsOutsideAvatar(tree.AvatarRoot, t.renderer.transform))
                    yield return ("shape:" + t.renderer.GetInstanceID() + ":" + t.shape, t.renderer.name + "/" + t.shape);
            }
            if (entry.BuildsProperties)
                foreach (var t in entry.properties)
                    if (PropertyExists(t, entry) && !IsOutsideAvatar(tree.AvatarRoot, t.renderer.transform))
                        yield return ("prop:" + t.renderer.GetInstanceID() + ":" + t.property, t.renderer.name + " material." + t.property);
        }

        public static bool SlotExists(MaterialTarget target)
        {
            return target.renderer != null && target.slot >= 0 && target.slot < target.renderer.sharedMaterials.Length;
        }

        public static bool ShapeExists(SkinnedMeshRenderer renderer, string shape)
        {
            return renderer != null && renderer.sharedMesh != null && !string.IsNullOrEmpty(shape) && renderer.sharedMesh.GetBlendShapeIndex(shape) >= 0;
        }

        /// <summary>The first shared material of the renderer that has this shader property, or null when none does.</summary>
        public static Material MaterialWithProperty(Renderer renderer, string property)
        {
            if (renderer == null || string.IsNullOrEmpty(property)) return null;
            foreach (var material in renderer.sharedMaterials)
                if (material != null && material.HasProperty(property)) return material;
            return null;
        }

        /// <summary>
        /// The material a property target reads its base value from: a shared material of the renderer, or, when
        /// none of those has the property, a material the same entry's material targets put on that renderer. That
        /// is the dissolve case, where the toggle swaps in the material that carries the property it animates.
        /// </summary>
        public static Material MaterialWithProperty(Renderer renderer, string property, MenuEntry entry)
        {
            var found = MaterialWithProperty(renderer, property);
            if (found != null || entry == null || renderer == null || string.IsNullOrEmpty(property)) return found;
            foreach (var target in entry.materials)
                if (target.renderer == renderer && target.material != null && target.material.HasProperty(property)) return target.material;
            return null;
        }

        public static bool PropertyExists(MaterialPropertyTarget target, MenuEntry entry = null)
        {
            return target != null && MaterialWithProperty(target.renderer, target.property, entry) != null;
        }

        /// <summary>True when more than one shared material of the renderer carries this property and they do not all hold the same value: the Off state writes the first one to every slot.</summary>
        public static bool MaterialsDisagree(MaterialPropertyTarget target)
        {
            if (target == null || target.renderer == null || string.IsNullOrEmpty(target.property)) return false;
            bool any = false;
            var first = Vector4.zero;
            foreach (var material in target.renderer.sharedMaterials)
            {
                if (material == null || !material.HasProperty(target.property)) continue;
                var value = ReadProperty(material, target.property, target.type);
                if (!any) { first = value; any = true; continue; }
                if ((first - value).sqrMagnitude > 1e-8f) return true;
            }
            return false;
        }

        /// <summary>A material's current value of a property, packed into a Vector4 the way EffectMath packs it (float in x, colour in rgba, vector in xyzw), or zero when the material is null. The one reader BaseState and PreviewModel both read the scene through.</summary>
        public static Vector4 ReadProperty(Material material, string property, PropertyValueType type)
        {
            if (material == null) return Vector4.zero;
            switch (type)
            {
                case PropertyValueType.Color: return material.GetColor(property);
                case PropertyValueType.Vector: return material.GetVector(property);
                default: return new Vector4(material.GetFloat(property), 0f, 0f, 0f);
            }
        }
    }
}
#endif
