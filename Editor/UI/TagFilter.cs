#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The tag filter beside the tree's search field: every row carries zero or more tags across four groups
    /// (Kind, From, State, Platform); ticking several tags of one group combines them with OR, ticking tags of
    /// different groups combines those groups with AND. Pure and stateless: the tree owns which tags are ticked.
    /// </summary>
    public static class TagFilter
    {
        public const string GroupKind = "kind";
        public const string GroupFrom = "from";
        public const string GroupState = "state";
        public const string GroupPlatform = "platform";

        /// <summary>The four groups, in the order the filter menu lists them.</summary>
        public static readonly string[] Groups = { GroupKind, GroupFrom, GroupState, GroupPlatform };

        public static string TagId(string group, string value) => group + ":" + value;

        public static string GroupOf(string tagId) => tagId.Split(':')[0];

        /// <summary>The localized name of a group, for the filter menu's headings and a chip's group prefix.</summary>
        public static string GroupLabel(string group)
        {
            switch (group)
            {
                case GroupKind: return L.Text("ad.ui.tagfilter.group.kind");
                case GroupFrom: return L.Text("ad.ui.tagfilter.group.from");
                case GroupState: return L.Text("ad.ui.tagfilter.group.state");
                case GroupPlatform: return L.Text("ad.ui.tagfilter.group.platform");
                default: return group;
            }
        }

        /// <summary>The localized name of one tag, read the same way the row's own chip text is, so the filter menu and the tree agree on wording.</summary>
        public static string TagLabel(string tagId)
        {
            var value = tagId.Substring(tagId.IndexOf(':') + 1);
            switch (value)
            {
                case "toggle": return L.Text("ad.ui.detail.toggle");
                case "radial": return L.Text("ad.ui.detail.radial");
                case "submenu": return L.Text("ad.ui.detail.submenu");
                case "trigger": return L.Text("ad.ui.detail.trigger");
                case "override": return L.Text("ad.ui.detail.override");
                case "preset": return L.Text("ad.ui.detail.preset");
                case "grabbable": return L.Text("ad.ui.detail.grabbable");
                case "animator": return L.Text("ad.ui.detail.animator");
                case "menu_asset": return L.Text("ad.ui.detail.menu_asset");
                case "avatardoc": return L.Text("ad.ui.tagfilter.from.avatardoc");
                case "avatar_menu": return L.Text("ad.ui.tagfilter.from.avatar_menu");
                case "vrcfury": return L.Text("ad.ui.chip.vrcfury");
                case "modular_avatar": return L.Text("ad.ui.chip.modular_avatar");
                case "edited": return L.Text("ad.ui.chip.edited");
                case "hidden": return L.Text("ad.ui.chip.hidden");
                case "removed": return L.Text("ad.ui.chip.removed");
                case "no_menu": return L.Text("ad.ui.chip.no_menu");
                case "asset": return L.Text("ad.ui.chip.asset");
                case "has_problems": return L.Text("ad.ui.tagfilter.state.has_problems");
                case "pc": return L.Text("ad.ui.chip.pc_only");
                case "quest": return L.Text("ad.ui.chip.quest_only");
                default: return value;
            }
        }

        static string KindValue(RowKind kind)
        {
            switch (kind)
            {
                case RowKind.Toggle: return "toggle";
                case RowKind.Radial: return "radial";
                case RowKind.Submenu: return "submenu";
                case RowKind.Trigger: return "trigger";
                case RowKind.Override: return "override";
                case RowKind.Preset: return "preset";
                case RowKind.Grabbable: return "grabbable";
                case RowKind.Animator: return "animator";
                case RowKind.MenuAsset: return "menu_asset";
                default: return null;
            }
        }

        static string FromValue(TreeRow row)
        {
            if (row.Kind == RowKind.Foreign)
            {
                var foreign = row.Item?.Foreign ?? ForeignSource.None;
                return foreign == ForeignSource.VRCFury ? "vrcfury" : foreign == ForeignSource.ModularAvatar ? "modular_avatar" : null;
            }
            if (row.Kind == RowKind.Existing) return "avatar_menu";
            return row.Owner != null ? "avatardoc" : null;
        }

        /// <summary>Every tag id this row carries, across all four groups. A row can carry several, including more than one from the same group only when that is genuinely true (State often is).</summary>
        public static IEnumerable<string> TagsFor(TreeRow row)
        {
            var kind = KindValue(row.Kind);
            if (kind != null) yield return TagId(GroupKind, kind);

            var from = FromValue(row);
            if (from != null) yield return TagId(GroupFrom, from);

            if (row.Edited) yield return TagId(GroupState, "edited");
            if (row.Hidden) yield return TagId(GroupState, "hidden");
            if (row.Removed) yield return TagId(GroupState, "removed");
            if (row.NoMenu) yield return TagId(GroupState, "no_menu");
            if (row.FromAsset) yield return TagId(GroupState, "asset");
            if (row.Messages.Count > 0) yield return TagId(GroupState, "has_problems");

            if (row.PlatformChip != null)
            {
                if (row.PlatformChip == L.Text("ad.ui.chip.pc_only")) yield return TagId(GroupPlatform, "pc");
                else if (row.PlatformChip == L.Text("ad.ui.chip.quest_only")) yield return TagId(GroupPlatform, "quest");
            }
        }

        /// <summary>Every tag id present on at least one of these rows, grouped and in a stable order, for the filter menu to offer only what actually exists on this avatar right now.</summary>
        public static List<string> Available(IEnumerable<TreeRow> rows)
        {
            var seen = new HashSet<string>();
            var ordered = new List<string>();
            foreach (var row in rows)
                foreach (var tag in TagsFor(row))
                    if (seen.Add(tag)) ordered.Add(tag);
            return ordered;
        }

        /// <summary>True when a row satisfies the ticked tags: OR within a group, AND across the groups that have any tag ticked at all. True for every row when nothing is ticked.</summary>
        public static bool Matches(TreeRow row, IReadOnlyCollection<string> activeTagIds)
        {
            if (activeTagIds == null || activeTagIds.Count == 0) return true;
            var rowTags = new HashSet<string>(TagsFor(row));
            foreach (var group in activeTagIds.GroupBy(GroupOf))
                if (!group.Any(rowTags.Contains)) return false;
            return true;
        }
    }
}
#endif
