#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    public sealed class Wedge
    {
        public string Name = "";
        public GlyphKind Kind;
        public Texture2D Icon;
        public string Key;
        public string EntryId;
        public bool IsSubmenu;
        public bool IsForeign;
        public bool Selected;
        /// <summary>The effective preview state: true for an on toggle or a radial above zero. Filled by WheelModel.For only when a plan is given.</summary>
        public bool On;
        /// <summary>0 or 1 for a toggle, the radial value for a radial. Filled by WheelModel.For only when a plan is given.</summary>
        public float Value;
    }

    /// <summary>What the wheel shows for one menu node: up to eight wedges in menu order, the title, and the crumbs back to the root.</summary>
    public sealed class WheelModel
    {
        public string Title = "";
        public string Key;
        public List<Wedge> Wedges = new List<Wedge>();
        public List<(string name, string key)> Crumbs = new List<(string name, string key)>();
        /// <summary>Toggles and radials of the shown node that are hidden from the menu: no wedge in Select mode, but tried from the test mode strip.</summary>
        public List<(string name, string entryId)> HiddenControls = new List<(string name, string entryId)>();
        public int Count => Wedges.Count;

        public static WheelModel For(MenuNode shown, string selectedKey, string rootTitle, AvatarPlan plan = null)
        {
            var model = new WheelModel();
            if (shown == null) { model.Title = rootTitle; return model; }
            bool isRoot = shown.Parent == null;
            model.Title = isRoot ? rootTitle : string.IsNullOrEmpty(shown.RichName) ? shown.Name : shown.RichName;
            model.Key = isRoot ? null : TreeRow.KeyForNode(shown);
            // The wheel stands for the menu VRChat shows, so anything left out on this platform, or a toggle or
            // radial hidden from the menu, is not in it; a hidden one still shows on the test mode strip instead.
            foreach (var hidden in shown.Items.Where(i => !i.Hidden && !i.Excluded && i.Control != null && i.Control.Entry.NoMenuControl))
                model.HiddenControls.Add((hidden.Control.Entry.name, hidden.Control.Entry.id));
            foreach (var item in shown.Items.Where(i => !i.Hidden && !i.Excluded && !(i.Control != null && i.Control.Entry.NoMenuControl)).Take(VRCExpressionsMenu.MAX_CONTROLS))
            {
                var wedge = new Wedge { Key = TreeRow.KeyFor(item, shown), IsForeign = item.Foreign != ForeignSource.None };
                if (item.Submenu != null)
                {
                    wedge.Name = string.IsNullOrEmpty(item.Submenu.RichName) ? item.Submenu.Name : item.Submenu.RichName;
                    wedge.Icon = item.Submenu.Icon;
                    wedge.IsSubmenu = true;
                    wedge.Kind = wedge.IsForeign || item.Submenu.IsExisting ? GlyphKind.Existing : item.Submenu.IsMorePage ? GlyphKind.More : GlyphKind.Submenu;
                    wedge.EntryId = item.Submenu.EntryId;
                }
                else if (item.Control != null)
                {
                    var entry = item.Control.Entry;
                    wedge.Name = entry.name;
                    wedge.Icon = entry.icon;
                    wedge.EntryId = entry.id;
                    wedge.Kind = entry.kind == EntryKind.Radial ? GlyphKind.Radial
                        : entry.kind == EntryKind.Preset ? GlyphKind.Preset
                        : entry.kind == EntryKind.Grabbable ? GlyphKind.Grabbable : GlyphKind.Toggle;
                    if (plan != null && entry.kind == EntryKind.Toggle)
                    {
                        wedge.On = PreviewState.Effective(entry, plan.Tree, plan.Plan) >= 0.5f;
                        wedge.Value = wedge.On ? 1f : 0f;
                    }
                    else if (plan != null && entry.kind == EntryKind.Radial)
                    {
                        wedge.Value = PreviewState.Effective(entry, plan.Tree, plan.Plan);
                        wedge.On = wedge.Value > 0f;
                    }
                }
                else if (item.Existing != null)
                {
                    // item.RichName tracks a menu edit's rename; item.Existing.name is the untouched source control.
                    wedge.Name = !string.IsNullOrEmpty(item.RichName) ? item.RichName : (item.Existing.name ?? "");
                    wedge.Icon = item.Icon != null ? item.Icon : item.Existing.icon;
                    wedge.IsSubmenu = item.Existing.type == VRCExpressionsMenu.Control.ControlType.SubMenu;
                    wedge.Kind = GlyphKind.Existing;
                }
                else if (wedge.IsForeign)
                {
                    wedge.Name = string.IsNullOrEmpty(item.RichName) ? item.Name : item.RichName;
                    wedge.Icon = item.Icon;
                    wedge.Kind = GlyphKind.Existing;
                }
                wedge.Selected = selectedKey != null && wedge.Key == selectedKey;
                model.Wedges.Add(wedge);
            }
            for (var cursor = shown.Parent; cursor != null; cursor = cursor.Parent)
                model.Crumbs.Insert(0, (cursor.Parent == null ? rootTitle : cursor.Name, cursor.Parent == null ? null : TreeRow.KeyForNode(cursor)));
            return model;
        }

        /// <summary>Index of the wedge under a point, or -1 outside the ring. Wedge 0 starts at the top; wedges run clockwise.</summary>
        public static int WedgeAt(Vector2 point, Vector2 centre, float outerRadius, float innerRadius, int count)
        {
            if (count <= 0) return -1;
            var delta = point - centre;
            float distance = delta.magnitude;
            if (distance < innerRadius || distance > outerRadius) return -1;
            float angle = Mathf.Atan2(delta.x, -delta.y) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;
            int index = (int)(angle / (360f / VRCExpressionsMenu.MAX_CONTROLS));
            return index < count ? index : -1;
        }
    }
}
#endif
