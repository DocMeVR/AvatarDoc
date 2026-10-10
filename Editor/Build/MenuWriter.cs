#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    public static class MenuWriter
    {
        public static void Write(BuildContext context, VRCAvatarDescriptor descriptor, MenuTree tree, ParameterPlan plan)
        {
            descriptor.expressionsMenu = Serialize(context, tree.Root, plan);
            descriptor.customExpressions = true;
        }

        static VRCExpressionsMenu Serialize(BuildContext context, MenuNode node, ParameterPlan plan)
        {
            if (node.IsExisting && !node.ContainsGenerated()) return node.ExistingMenu;

            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.name = node.Asset == null && node.IsExisting && node.ExistingMenu != null ? node.ExistingMenu.name : node.Name;
            // A menu asset AvatarDoc installs is not a menu of the avatar being replaced: it is copied in, and the
            // same asset may be installed more than once, so it is never registered as the original of one clone.
            if (node.Asset == null && node.IsExisting && node.ExistingMenu != null) ObjectRegistry.RegisterReplacedObject(node.ExistingMenu, menu);

            menu.controls = new List<VRCExpressionsMenu.Control>();
            foreach (var item in node.Items)
            {
                if (item.Hidden) continue;
                // Left out on this platform: its parameter is still written, so the other platform's list matches.
                if (item.Excluded) continue;
                if (item.Foreign != ForeignSource.None) continue;
                // A toggle or radial hidden from the menu, or a grabbable whose menu toggle is off, still gets its
                // parameter, layer and defaults; it just gets no control here and takes no slot.
                if (item.Control != null && item.Control.Entry.NoMenuControl) continue;
                if (item.Submenu != null)
                {
                    var child = Serialize(context, item.Submenu, plan);
                    var control = item.Existing != null ? CloneControl(item.Existing) : new VRCExpressionsMenu.Control
                    {
                        name = string.IsNullOrEmpty(item.Submenu.RichName) ? item.Submenu.Name : item.Submenu.RichName,
                        icon = item.Submenu.Icon,
                        type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = "" },
                        subParameters = Array.Empty<VRCExpressionsMenu.Control.Parameter>(),
                        labels = Array.Empty<VRCExpressionsMenu.Control.Label>(),
                    };
                    // A submenu entry merged into an existing submenu keeps the original control, but its own
                    // styled name and icon still win where it sets them.
                    if (item.Existing != null && item.Submenu.EntryId != null)
                    {
                        if (!string.IsNullOrEmpty(item.Submenu.RichName)) control.name = item.Submenu.RichName;
                        if (item.Submenu.Icon != null) control.icon = item.Submenu.Icon;
                    }
                    if (item.Edit != null) { control.name = item.RichName; control.icon = item.Icon; }
                    control.subMenu = child;
                    menu.controls.Add(control);
                }
                else if (item.Existing != null)
                {
                    var control = CloneControl(item.Existing);
                    if (item.Edit != null) { control.name = item.RichName; control.icon = item.Icon; }
                    menu.controls.Add(control);
                }
                else if (item.Control != null && plan.Bindings.TryGetValue(item.Control, out var binding))
                {
                    menu.controls.Add(ControlFor(item.Control.Entry, binding));
                }
            }

            context.AssetSaver.SaveAsset(menu);
            return menu;
        }

        static VRCExpressionsMenu.Control ControlFor(MenuEntry entry, ControlBinding binding)
        {
            var control = new VRCExpressionsMenu.Control
            {
                name = entry.name,
                icon = entry.icon,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "" },
                subParameters = Array.Empty<VRCExpressionsMenu.Control.Parameter>(),
                labels = Array.Empty<VRCExpressionsMenu.Control.Label>(),
            };
            if (entry.kind == EntryKind.Radial)
            {
                control.type = VRCExpressionsMenu.Control.ControlType.RadialPuppet;
                control.subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = binding.Parameter.Name } };
            }
            else if (entry.kind == EntryKind.Preset)
            {
                control.type = VRCExpressionsMenu.Control.ControlType.Button;
                control.parameter = new VRCExpressionsMenu.Control.Parameter { name = binding.Parameter.Name };
                control.value = binding.Value;
            }
            else
            {
                control.type = VRCExpressionsMenu.Control.ControlType.Toggle;
                control.parameter = new VRCExpressionsMenu.Control.Parameter { name = binding.Parameter.Name };
                control.value = binding.Value;
            }
            return control;
        }

        static VRCExpressionsMenu.Control CloneControl(VRCExpressionsMenu.Control source)
        {
            return new VRCExpressionsMenu.Control
            {
                name = source.name,
                icon = source.icon,
                type = source.type,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = source.parameter?.name ?? "" },
                value = source.value,
                style = source.style,
                subMenu = source.subMenu,
                subParameters = (source.subParameters ?? Array.Empty<VRCExpressionsMenu.Control.Parameter>())
                    .Select(p => new VRCExpressionsMenu.Control.Parameter { name = p?.name ?? "" }).ToArray(),
                labels = (source.labels ?? Array.Empty<VRCExpressionsMenu.Control.Label>()).ToArray(),
            };
        }
    }
}
#endif
