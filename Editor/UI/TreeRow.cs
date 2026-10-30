#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    public enum RowKind
    {
        Submenu, Toggle, Radial, Trigger, Existing, More, TriggersGroup, Override, OverridesGroup, Foreign, Preset,
        AnimatorsGroup, Animator, MenuAssetsGroup, MenuAsset, ParametersGroup, Parameter, Grabbable,
    }

    /// <summary>One row of the merged menu tree as the inspector shows it.</summary>
    public sealed class TreeRow
    {
        /// <summary>The key of the Parameters row, the group every declared parameter sits under.</summary>
        public const string ParametersRowKey = "parameters";

        public int Id;
        public RowKind Kind;
        public string Name = "";
        public MenuNode Node;
        public ControlNode Control;
        public TriggerNode Trigger;
        public OverrideNode Override;
        public AnimatorMerge Merge;
        public MenuAssetInstall Asset;
        public ParameterDeclaration Declaration;
        public VRCExpressionsMenu.Control ExistingControl;
        /// <summary>The merged-menu item this row mirrors. Set for Existing, Foreign, own Submenu and own control rows; null for groups, triggers, overrides and More pages' synthetic wrapper is still set.</summary>
        public MenuItem Item;
        /// <summary>A menu edit changes how this item appears. Never true for a row an AvatarDoc entry made: those are edited directly, not through a menu edit.</summary>
        public bool Edited => Item != null && Item.Edit != null;
        /// <summary>A menu edit removed this item from the built menu; it stays in the tree, dimmed.</summary>
        public bool Hidden => Item != null && Item.Hidden;
        /// <summary>A toggle or radial the creator hid from the menu, or a grabbable whose menu toggle is off: it keeps its parameter, layer and default, but has no control and takes no slot.</summary>
        public bool NoMenu => Control != null && Control.Entry.NoMenuControl;
        /// <summary>This row shows a control that came in with an installed menu asset, so it carries the asset chip.</summary>
        public bool FromAsset => Item != null && Item.Asset != null;
        /// <summary>A menu edit takes this item out of the avatar, with the parameter and the layers nothing else needs.</summary>
        public bool Removed => Item != null && Item.Edit != null && Item.Edit.remove;
        /// <summary>The platform choice leaves this entry out of the build the editor currently targets.</summary>
        public bool Excluded;
        /// <summary>Which platform this entry is limited to, for its chip. Null when it is built on both.</summary>
        public string PlatformChip;
        public AvatarDoc Owner;
        public MenuEntry Entry;
        public bool Mine;
        public bool DefaultOn;
        public string GroupChip;
        public string SourceChip;
        /// <summary>Trigger rows only: what makes it fire, in short form (a toggle's name, a parameter condition, or a shape).</summary>
        public string TriggerChip;
        public int Slots;
        public List<ModelMessage> Messages = new List<ModelMessage>();
        public TreeRow ParentRow;
        public int Depth;
        /// <summary>Stable identity of this row across a refresh, since ids are reassigned every Build call.</summary>
        public string Key;
        /// <summary>Set while a search or tag filter is active, for a row kept only because a descendant matches, not because it itself does: it stays visible so the structure reads, but dimmed. Never set outside a filter.</summary>
        public bool FilterDimmed;

        public string EntryId => Entry?.id;
        public bool IsMenuEntry => Mine && Entry != null && !Entry.IsRule;
        /// <summary>Any entry of this component, trigger included. Used for delete, duplicate and the context menu, which all work on triggers too.</summary>
        public bool IsOwnEntry => Mine && Entry != null;
        /// <summary>The component this row comes from is switched off, so the build will skip it.</summary>
        public bool OwnerDisabled => Owner != null && !Owner.enabled;

        /// <summary>
        /// The stable row key of a menu item: the entry id when the item is one of ours, else a path-based key.
        /// An existing or foreign item's key is built from its OriginalPath, which a menu edit's move never changes,
        /// so the selection and the saved expansion set survive a move. A node AvatarDoctor creates (an install
        /// path segment or a move target) has no OriginalPath of its own and falls back to its current Path.
        /// </summary>
        internal static string KeyFor(MenuItem item, MenuNode parent)
        {
            if (item.Submenu != null && item.Submenu.EntryId != null) return item.Submenu.EntryId;
            if (item.Control != null) return item.Control.Entry.id;
            var key = BaseKey(item);
            if (parent == null) return key;

            // Two controls of the same menu can carry the same name, and then the same original path. The key has
            // to tell them apart, or a refresh puts the selection on the first of them and the next edit in the
            // panel lands on the wrong control.
            int before = 0;
            int total = 0;
            foreach (var candidate in parent.Items)
            {
                if (candidate.Control != null || (candidate.Submenu != null && candidate.Submenu.EntryId != null)) continue;
                if (BaseKey(candidate) != key) continue;
                if (candidate == item) before = total;
                total++;
            }
            return total > 1 ? key + "#" + before : key;
        }

        static string BaseKey(MenuItem item)
        {
            if (item.Submenu != null) return "node:" + (item.Submenu.OriginalPath.Length > 0 ? item.Submenu.OriginalPath : item.Submenu.Path);
            return (item.Foreign != ForeignSource.None ? "foreign:" : "existing:") + item.OriginalPath;
        }

        /// <summary>The localized chip text for a foreign item's source.</summary>
        internal static string SourceChipFor(ForeignSource source)
        {
            switch (source)
            {
                case ForeignSource.ModularAvatar: return L.Text("ad.ui.chip.modular_avatar");
                case ForeignSource.VRCFury: return L.Text("ad.ui.chip.vrcfury");
                default: return "";
            }
        }

        /// <summary>The chip text for an entry limited to one platform, whether or not that is the one being built. Null when it is built on both.</summary>
        internal static string PlatformChipFor(AvatarDoc component, MenuEntry entry)
        {
            switch (Platform.Effective(component, entry))
            {
                case PlatformChoice.PCOnly: return L.Text("ad.ui.chip.pc_only");
                case PlatformChoice.MobileOnly: return L.Text("ad.ui.chip.quest_only");
                default: return null;
            }
        }

        /// <summary>The chip text for a menu asset limited to one platform, whether or not that is the one being built. Null when it is installed on both.</summary>
        internal static string PlatformChipFor(AvatarDoc component, MenuAssetInstall install)
        {
            switch (Platform.Effective(component, install))
            {
                case PlatformChoice.PCOnly: return L.Text("ad.ui.chip.pc_only");
                case PlatformChoice.MobileOnly: return L.Text("ad.ui.chip.quest_only");
                default: return null;
            }
        }

        /// <summary>The chip text for a merged animator limited to one platform, whether or not that is the one being built. Null when it is built on both.</summary>
        internal static string PlatformChipFor(AvatarDoc component, AnimatorMerge merge)
        {
            switch (Platform.Effective(component, merge))
            {
                case PlatformChoice.PCOnly: return L.Text("ad.ui.chip.pc_only");
                case PlatformChoice.MobileOnly: return L.Text("ad.ui.chip.quest_only");
                default: return null;
            }
        }

        internal static string KeyForNode(MenuNode node) => node.EntryId != null ? node.EntryId : "node:" + (node.OriginalPath.Length > 0 ? node.OriginalPath : node.Path);

        /// <summary>
        /// "More (page 2)" for the first automatic overflow page under a node, "More (page 3)" for one chained under
        /// that, and so on: the built menu control itself is always named plain "More" (SplitOverflow, and every
        /// test that reads the built asset, depend on that name), so the numbering is display only, worked out from
        /// how many More pages this one sits under, including itself.
        /// </summary>
        static string MorePageLabel(MenuNode node)
        {
            int page = 1;
            for (var cursor = node; cursor != null && cursor.IsMorePage; cursor = cursor.Parent) page++;
            return string.Format(L.Text("ad.ui.more_page"), page);
        }

        public static (List<TreeViewItemData<TreeRow>> items, Dictionary<int, TreeRow> byId) Build(AvatarPlan plan, AvatarDoc mine)
        {
            var byId = new Dictionary<int, TreeRow>();
            int nextId = 1;
            var items = BuildChildren(plan, mine, plan.Tree.Root, null, 0, ref nextId, byId);

            if (plan.Tree.Triggers.Count > 0)
            {
                var group = new TreeRow { Id = nextId++, Kind = RowKind.TriggersGroup, Name = L.Text("ad.ui.triggers"), Mine = true, Owner = mine, Depth = 0, Key = "triggers" };
                byId[group.Id] = group;
                var triggerItems = new List<TreeViewItemData<TreeRow>>();
                foreach (var trigger in plan.Tree.Triggers)
                {
                    var row = new TreeRow
                    {
                        Id = nextId++, Kind = RowKind.Trigger, Name = trigger.Entry.name, Trigger = trigger, Owner = trigger.Component,
                        Entry = trigger.Entry, Mine = trigger.Component == mine, ParentRow = group, Depth = 1,
                        SourceChip = trigger.Component == mine ? null : trigger.Component.gameObject.name,
                        TriggerChip = TriggerChipFor(plan.Tree, trigger.Entry),
                        Key = trigger.Entry.id,
                        Excluded = trigger.Excluded,
                        PlatformChip = PlatformChipFor(trigger.Component, trigger.Entry),
                    };
                    row.Messages.AddRange(plan.MessagesFor(trigger.Component, trigger.Entry));
                    byId[row.Id] = row;
                    triggerItems.Add(new TreeViewItemData<TreeRow>(row.Id, row));
                }
                items.Add(new TreeViewItemData<TreeRow>(group.Id, group, triggerItems));
            }

            if (plan.Tree.Overrides.Count > 0)
            {
                var group = new TreeRow { Id = nextId++, Kind = RowKind.OverridesGroup, Name = L.Text("ad.ui.overrides"), Mine = true, Owner = mine, Depth = 0, Key = "overrides" };
                byId[group.Id] = group;
                var overrideItems = new List<TreeViewItemData<TreeRow>>();
                foreach (var node in plan.Tree.Overrides)
                {
                    var row = new TreeRow
                    {
                        Id = nextId++, Kind = RowKind.Override, Name = node.Entry.name, Override = node, Owner = node.Component,
                        Entry = node.Entry, Mine = node.Component == mine, ParentRow = group, Depth = 1,
                        SourceChip = node.Component == mine ? null : node.Component.gameObject.name,
                        Key = node.Entry.id,
                        Excluded = node.Excluded,
                        PlatformChip = PlatformChipFor(node.Component, node.Entry),
                    };
                    row.Messages.AddRange(plan.MessagesFor(node.Component, node.Entry));
                    byId[row.Id] = row;
                    overrideItems.Add(new TreeViewItemData<TreeRow>(row.Id, row));
                }
                items.Add(new TreeViewItemData<TreeRow>(group.Id, group, overrideItems));
            }

            if (plan.Tree.Merges.Count > 0)
            {
                var group = new TreeRow { Id = nextId++, Kind = RowKind.AnimatorsGroup, Name = L.Text("ad.ui.animators"), Mine = true, Owner = mine, Depth = 0, Key = "animators" };
                byId[group.Id] = group;
                var animatorItems = new List<TreeViewItemData<TreeRow>>();
                foreach (var node in plan.Tree.Merges)
                {
                    var merge = node.Merge;
                    var row = new TreeRow
                    {
                        Id = nextId++, Kind = RowKind.Animator, Name = merge.controller != null ? merge.controller.name : L.Text("ad.ui.animator.unnamed"),
                        Merge = merge, Owner = node.Component,
                        Mine = node.Component == mine, ParentRow = group, Depth = 1,
                        SourceChip = node.Component == mine ? null : node.Component.gameObject.name,
                        Key = merge.id,
                        Excluded = node.Excluded,
                        PlatformChip = PlatformChipFor(node.Component, merge),
                    };
                    row.Messages.AddRange(plan.MessagesForId(merge.id));
                    byId[row.Id] = row;
                    animatorItems.Add(new TreeViewItemData<TreeRow>(row.Id, row));
                }
                items.Add(new TreeViewItemData<TreeRow>(group.Id, group, animatorItems));
            }

            if (plan.Tree.MenuAssets.Count > 0)
            {
                var group = new TreeRow { Id = nextId++, Kind = RowKind.MenuAssetsGroup, Name = L.Text("ad.ui.menu_assets"), Mine = true, Owner = mine, Depth = 0, Key = "menuassets" };
                byId[group.Id] = group;
                var assetItems = new List<TreeViewItemData<TreeRow>>();
                foreach (var node in plan.Tree.MenuAssets)
                {
                    var install = node.Install;
                    var row = new TreeRow
                    {
                        Id = nextId++, Kind = RowKind.MenuAsset,
                        Name = install.menu != null ? install.PlainName : L.Text("ad.ui.menu_asset.unnamed"),
                        Asset = install, Owner = node.Component,
                        Mine = node.Component == mine, ParentRow = group, Depth = 1,
                        SourceChip = node.Component == mine ? null : node.Component.gameObject.name,
                        Key = install.id,
                        Excluded = node.Excluded,
                        PlatformChip = PlatformChipFor(node.Component, install),
                    };
                    row.Messages.AddRange(plan.MessagesForId(install.id));
                    byId[row.Id] = row;
                    assetItems.Add(new TreeViewItemData<TreeRow>(row.Id, row));
                }
                items.Add(new TreeViewItemData<TreeRow>(group.Id, group, assetItems));
            }

            var declared = plan.Tree.Components
                .Where(c => c != null)
                .SelectMany(c => c.parameters.Where(p => p != null).Select(p => (component: c, declaration: p)))
                .ToList();
            if (declared.Count > 0)
            {
                var group = new TreeRow { Id = nextId++, Kind = RowKind.ParametersGroup, Name = L.Text("ad.ui.parameters"), Mine = true, Owner = mine, Depth = 0, Key = ParametersRowKey };
                byId[group.Id] = group;
                var parameterItems = new List<TreeViewItemData<TreeRow>>();
                foreach (var pair in declared)
                {
                    var row = new TreeRow
                    {
                        Id = nextId++, Kind = RowKind.Parameter, Name = pair.declaration.name,
                        Declaration = pair.declaration, Owner = pair.component,
                        Mine = pair.component == mine, ParentRow = group, Depth = 1,
                        SourceChip = pair.component == mine ? null : pair.component.gameObject.name,
                        Key = ParameterPlanner.DeclarationId(pair.declaration.name),
                    };
                    row.Messages.AddRange(plan.MessagesForId(row.Key));
                    byId[row.Id] = row;
                    parameterItems.Add(new TreeViewItemData<TreeRow>(row.Id, row));
                }
                items.Add(new TreeViewItemData<TreeRow>(group.Id, group, parameterItems));
            }
            return (items, byId);
        }

        /// <summary>Short form of what makes a trigger fire, for its row's chip.</summary>
        static string TriggerChipFor(MenuTree tree, MenuEntry entry)
        {
            // A blend shape trigger's groups hold its requirements only; its chip names its shape.
            if (entry.UsesWhenGroups && entry.source != TriggerSource.BlendShape)
            {
                var conditions = entry.EffectiveWhen.SelectMany(g => g.all).Where(c => c != null).ToList();
                if (conditions.Count == 0) return "";
                var first = ConditionLogic.Words(conditions[0], tree);
                return conditions.Count > 1 ? string.Format(L.Text("ad.ui.when.chip_more"), first, conditions.Count - 1) : first;
            }
            switch (entry.source)
            {
                case TriggerSource.Toggle:
                {
                    var control = tree.FindControl(entry.sourceToggleId);
                    if (control == null) return "";
                    return entry.sourceToggleOn ? control.Entry.PlainName : string.Format(L.Text("ad.ui.chip.trigger_off"), control.Entry.PlainName);
                }
                case TriggerSource.Parameter:
                {
                    var name = (entry.sourceParameter ?? "").Trim();
                    var v = entry.conditionValue.ToString(CultureInfo.InvariantCulture);
                    switch (entry.condition)
                    {
                        case ParameterCondition.IsTrue: return name;
                        case ParameterCondition.IsFalse: return "!" + name;
                        case ParameterCondition.NotEquals: return name + " != " + v;
                        case ParameterCondition.Greater: return name + " > " + v;
                        case ParameterCondition.Less: return name + " < " + v;
                        default: return name + " = " + v;
                    }
                }
                default:
                    return entry.sourceRenderer != null ? entry.sourceRenderer.name + "/" + entry.sourceShape : "";
            }
        }

        static List<TreeViewItemData<TreeRow>> BuildChildren(AvatarPlan plan, AvatarDoc mine, MenuNode node, TreeRow parent, int depth, ref int nextId, Dictionary<int, TreeRow> byId)
        {
            var result = new List<TreeViewItemData<TreeRow>>();
            foreach (var item in node.Items)
            {
                TreeRow row;
                if (item.Submenu != null)
                {
                    var sub = item.Submenu;
                    bool foreign = item.Foreign != ForeignSource.None;
                    bool generated = !sub.IsExisting || sub.ContainsGenerated();
                    var subEntry = sub.EntryId != null && sub.Component != null ? sub.Component.FindEntry(sub.EntryId) : null;
                    row = new TreeRow
                    {
                        Id = nextId++, Node = sub, Name = sub.IsMorePage ? MorePageLabel(sub) : string.IsNullOrEmpty(sub.RichName) ? sub.Name : sub.RichName, Owner = sub.Component, ParentRow = parent, Depth = depth,
                        // A node that carries an entry id is that entry's row, even where it merged into a submenu
                        // the avatar's own menu already had.
                        Kind = foreign ? RowKind.Foreign : sub.EntryId != null ? RowKind.Submenu : sub.IsExisting ? RowKind.Existing : sub.IsMorePage ? RowKind.More : RowKind.Submenu,
                        Mine = !foreign && sub.Component == mine && sub.EntryId != null,
                        Entry = subEntry,
                        Slots = generated ? sub.Items.Count(i => !i.Hidden && !i.Excluded && i.Foreign == ForeignSource.None && !(i.Control != null && i.Control.Entry.NoMenuControl)) : 0,
                        ExistingControl = item.Existing,
                        Item = item,
                        Key = KeyFor(item, node),
                        Excluded = item.Excluded,
                        PlatformChip = subEntry != null ? PlatformChipFor(sub.Component, subEntry) : null,
                    };
                    if (foreign) row.SourceChip = SourceChipFor(item.Foreign);
                    else if (!row.Mine && !sub.IsExisting && sub.Component != null && sub.Component != mine) row.SourceChip = sub.Component.gameObject.name;
                    if (row.Entry != null) row.Messages.AddRange(plan.MessagesFor(sub.Component, row.Entry));
                    AddEditMessages(plan, row, item);
                    byId[row.Id] = row;
                    var children = BuildChildren(plan, mine, sub, row, depth + 1, ref nextId, byId);
                    result.Add(new TreeViewItemData<TreeRow>(row.Id, row, children));
                    continue;
                }
                if (item.Control != null)
                {
                    var control = item.Control;
                    row = new TreeRow
                    {
                        Id = nextId++, Control = control, Entry = control.Entry, Owner = control.Component, Name = control.Entry.name,
                        Kind = control.Entry.kind == EntryKind.Radial ? RowKind.Radial : control.Entry.kind == EntryKind.Preset ? RowKind.Preset
                            : control.Entry.kind == EntryKind.Grabbable ? RowKind.Grabbable : RowKind.Toggle,
                        Mine = control.Component == mine, ParentRow = parent, Depth = depth,
                        Item = item,
                        Key = KeyFor(item, node),
                        Excluded = control.Excluded,
                        // A left out entry's chip says why, in the place a platform chip says it.
                        PlatformChip = control.Unused ? L.Text("ad.ui.chip.unused") : PlatformChipFor(control.Component, control.Entry),
                    };
                    if (!row.Mine) row.SourceChip = control.Component.gameObject.name;
                    if (plan.Plan.Bindings.TryGetValue(control, out var binding))
                    {
                        row.DefaultOn = binding.OnByDefault;
                        if (binding.Parameter.GroupName != null) row.GroupChip = binding.Parameter.GroupName + " " + binding.Value;
                    }
                    row.Messages.AddRange(plan.MessagesFor(control.Component, control.Entry));
                }
                else if (item.Foreign != ForeignSource.None)
                {
                    row = new TreeRow
                    {
                        Id = nextId++, Kind = RowKind.Foreign, Name = string.IsNullOrEmpty(item.RichName) ? item.Name : item.RichName,
                        ParentRow = parent, Depth = depth,
                        SourceChip = SourceChipFor(item.Foreign),
                        Item = item,
                        Key = KeyFor(item, node),
                    };
                }
                else
                {
                    var name = !string.IsNullOrEmpty(item.RichName) ? item.RichName : (item.Existing != null ? item.Existing.name : "");
                    row = new TreeRow
                    {
                        Id = nextId++, Kind = RowKind.Existing, ExistingControl = item.Existing, Name = name,
                        ParentRow = parent, Depth = depth,
                        Item = item,
                        Key = KeyFor(item, node),
                    };
                }
                AddEditMessages(plan, row, item);
                byId[row.Id] = row;
                result.Add(new TreeViewItemData<TreeRow>(row.Id, row));
            }
            return result;
        }

        /// <summary>Attaches the messages about this item's own menu edit (a cycle, most often) to its row. Only existing and foreign rows can carry a menu edit.</summary>
        static void AddEditMessages(AvatarPlan plan, TreeRow row, MenuItem item)
        {
            if (item.Edit == null || (row.Kind != RowKind.Existing && row.Kind != RowKind.Foreign)) return;
            row.Messages.AddRange(plan.MessagesForEdit(item.Edit.id));
        }
    }
}
#endif
