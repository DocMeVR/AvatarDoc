#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The panel of one installed menu asset: the asset, where it goes, whether it becomes one submenu, its name
    /// and icon, its platform, and the parameters its controls drive with who declares each.
    /// </summary>
    public sealed class MenuAssetPanel : VisualElement
    {
        readonly EntryDetailView detail;
        readonly AvatarDoctorView owner;
        readonly TreeRow row;
        readonly VisualElement parametersBox = new VisualElement { name = "ad-menu-asset-parameters" };
        readonly VisualElement nameRow;
        SegmentedChoice place;

        MenuAssetInstall Install => row.Asset;

        /// <summary>How often a menu asset panel read the avatar's existing parameters. Read by the tests.</summary>
        public static int ExistingReads { get; private set; }

        public MenuAssetPanel(EntryDetailView detail, AvatarDoctorView owner, TreeRow row, SerializedProperty property)
        {
            this.detail = detail;
            this.owner = owner;
            this.row = row;

            var assetField = detail.Fields.Object<VRCExpressionsMenu>(property.FindPropertyRelative(nameof(MenuAssetInstall.menu)), L.Text("ad.ui.menu_asset.asset"), owner.ApplyAndRefresh);
            assetField.name = "ad-menu-asset-menu";
            assetField.tooltip = L.Text("ad.ui.menu_asset.asset.tip");
            Add(Bound(assetField));

            var pathField = detail.Fields.Text(property.FindPropertyRelative(nameof(MenuAssetInstall.installPath)), L.Text("ad.ui.menu_asset.path"), owner.ApplyAndRefresh);
            pathField.name = "ad-menu-asset-path";
            pathField.tooltip = L.Text("ad.ui.menu_asset.path.tip");
            Add(Bound(pathField));

            var placeRow = new VisualElement();
            placeRow.AddToClassList("ad-row");
            var placeLabel = new Label(L.Text("ad.ui.menu_asset.place")) { tooltip = L.Text("ad.ui.menu_asset.place.tip") };
            placeLabel.AddToClassList("ad-label");
            placeRow.Add(placeLabel);
            place = new SegmentedChoice(new List<string> { L.Text("ad.ui.menu_asset.place.submenu"), L.Text("ad.ui.menu_asset.place.append") },
                Install != null && !Install.asSubmenu ? 1 : 0) { name = "ad-menu-asset-place", tooltip = L.Text("ad.ui.menu_asset.place.tip") };
            var writePlace = detail.Fields.WriterFor(property.FindPropertyRelative(nameof(MenuAssetInstall.asSubmenu)), owner.ApplyAndRefresh);
            place.Changed += index => writePlace(p => p.boolValue = index == 0);
            placeRow.Add(place);
            Add(placeRow);

            var nameField = detail.Fields.Text(property.FindPropertyRelative(nameof(MenuAssetInstall.name)), L.Text("ad.ui.name"), owner.ApplyAndRefresh);
            nameField.name = "ad-menu-asset-name";
            nameField.tooltip = L.Text("ad.ui.menu_asset.name.tip");
            nameRow = Bound(nameField);
            Add(nameRow);

            var iconField = detail.Fields.Object<Texture2D>(property.FindPropertyRelative(nameof(MenuAssetInstall.icon)), L.Text("ad.ui.icon"), owner.ApplyCosmetic);
            iconField.name = "ad-menu-asset-icon";
            iconField.tooltip = L.Text("ad.ui.icon.tip");
            Add(Bound(iconField));

            var platformField = detail.Fields.EnumChoice(property.FindPropertyRelative(nameof(MenuAssetInstall.platform)), L.Text("ad.ui.platform"),
                new[] { L.Text("ad.ui.platform.inherit"), L.Text("ad.ui.platform.both"), L.Text("ad.ui.platform.pc"), L.Text("ad.ui.platform.quest") });
            platformField.name = "ad-menu-asset-platform";
            platformField.tooltip = L.Text("ad.ui.platform.tip");
            Add(Bound(platformField));

            var parametersTitle = new Label(L.Text("ad.ui.menu_asset.parameters")) { tooltip = L.Text("ad.ui.menu_asset.parameters.tip") };
            parametersTitle.AddToClassList("ad-label");
            parametersTitle.AddToClassList("ad-section");
            Add(parametersTitle);
            Add(parametersBox);

            var note = new Label(L.Text("ad.ui.menu_asset.note"));
            note.AddToClassList("ad-note");
            Add(note);

            Rebuild();
        }

        /// <summary>Rewrites the parameter list from the asset. The asset's own controls cannot be bound as SerializedProperty rows, so they are redrawn after every change.</summary>
        public void Rebuild()
        {
            parametersBox.Clear();
            var install = Install;
            if (place != null) place.SetIndexWithoutNotify(install != null && !install.asSubmenu ? 1 : 0);
            // Only a submenu shows a name of its own; appended controls keep the names the asset gives them.
            if (nameRow != null) nameRow.EnableInClassList("ad-hidden", install == null || !install.asSubmenu);
            var names = ParametersOf(install != null ? install.menu : null);
            if (names.Count == 0)
            {
                var none = new Label(L.Text("ad.ui.detail.none"));
                none.AddToClassList("ad-note");
                parametersBox.Add(none);
                return;
            }
            var planned = AnimatorPanel.PlannedByName(owner.Plan);
            ExistingReads++;
            var existing = AnimatorPanel.ExistingOf(owner.Plan);
            foreach (var name in names) parametersBox.Add(ParameterRow(name, planned, existing));
        }

        VisualElement ParameterRow(string parameterName, Dictionary<string, PlannedParameter> planned, Dictionary<string, ExistingParameter> existing)
        {
            var container = new VisualElement();
            container.AddToClassList("ad-target");

            var label = new Label(parameterName) { name = "ad-menu-asset-param-name" };
            label.AddToClassList("ad-grow");
            container.Add(label);

            bool undeclared;
            var declaredBy = DeclaredBy(parameterName, planned, existing, out undeclared);
            var declared = new Label(declaredBy) { name = "ad-menu-asset-param-declared" };
            declared.AddToClassList("ad-note");
            container.Add(declared);

            if (undeclared)
            {
                var add = new Button(() => detail.AddDeclarationForParameter(parameterName))
                {
                    text = L.Text("ad.ui.menu_asset.declare"),
                    name = "ad-menu-asset-declare-" + parameterName,
                    tooltip = L.Text("ad.ui.menu_asset.declare.tip"),
                };
                container.Add(add);
            }
            return container;
        }

        /// <summary>Who provides this parameter: the entry or declaration that plans it, "existing on the avatar" for one the avatar already had, or nothing, which offers the declaration.</summary>
        static string DeclaredBy(string parameterName, Dictionary<string, PlannedParameter> plannedByName, Dictionary<string, ExistingParameter> existing, out bool undeclared)
        {
            undeclared = false;
            plannedByName.TryGetValue(parameterName, out var planned);
            if (planned != null && planned.Controls.Count > 0) return planned.Controls[0].Entry.PlainName;
            if (planned != null && planned.ReusesExisting) return L.Text("ad.ui.animator.existing");
            if (planned != null) return L.Text("ad.ui.menu_asset.declared");
            if (TriggerPlanner.BuiltInType(parameterName).HasValue) return L.Text("ad.ui.pick.parameter.vrchat");
            if (existing.ContainsKey(parameterName)) return L.Text("ad.ui.animator.existing");

            undeclared = true;
            return L.Text("ad.ui.menu_asset.undeclared");
        }

        /// <summary>Every parameter name the controls of this asset drive, submenus included, in the order they are read. Cycles are guarded exactly as the model guards them.</summary>
        public static List<string> ParametersOf(VRCExpressionsMenu menu)
        {
            var result = new List<string>();
            Collect(menu, new HashSet<VRCExpressionsMenu>(), result);
            return result;
        }

        static void Collect(VRCExpressionsMenu menu, HashSet<VRCExpressionsMenu> visited, List<string> result)
        {
            if (menu == null || !visited.Add(menu)) return;
            foreach (var control in menu.controls ?? new List<VRCExpressionsMenu.Control>())
            {
                if (control == null) continue;
                foreach (var name in NamesOf(control))
                    if (!result.Contains(name)) result.Add(name);
                if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu) Collect(control.subMenu, visited, result);
            }
            visited.Remove(menu);
        }

        static IEnumerable<string> NamesOf(VRCExpressionsMenu.Control control)
        {
            if (control.parameter != null && !string.IsNullOrWhiteSpace(control.parameter.name)) yield return control.parameter.name.Trim();
            if (control.subParameters == null) yield break;
            foreach (var sub in control.subParameters)
                if (sub != null && !string.IsNullOrWhiteSpace(sub.name)) yield return sub.name.Trim();
        }

        /// <summary>
        /// The type a declaration for this parameter should have, read from the controls that drive it: a sub
        /// parameter of a puppet is a Float, a toggle on 1 a Bool, a toggle on another whole number an Int.
        /// </summary>
        public static PlannedType TypeFor(VRCExpressionsMenu menu, string parameterName)
        {
            var type = PlannedType.Bool;
            TypeFor(menu, parameterName, new HashSet<VRCExpressionsMenu>(), ref type);
            return type;
        }

        static void TypeFor(VRCExpressionsMenu menu, string parameterName, HashSet<VRCExpressionsMenu> visited, ref PlannedType type)
        {
            if (menu == null || !visited.Add(menu)) return;
            foreach (var control in menu.controls ?? new List<VRCExpressionsMenu.Control>())
            {
                if (control == null) continue;
                if (control.subParameters != null && control.subParameters.Any(p => p != null && p.name != null && p.name.Trim() == parameterName))
                {
                    type = PlannedType.Float;
                    return;
                }
                if (control.parameter != null && control.parameter.name != null && control.parameter.name.Trim() == parameterName)
                {
                    if (!Mathf.Approximately(control.value, Mathf.Round(control.value))) type = PlannedType.Float;
                    else if (!Mathf.Approximately(control.value, 1f) && type == PlannedType.Bool) type = PlannedType.Int;
                }
                if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu) TypeFor(control.subMenu, parameterName, visited, ref type);
            }
            visited.Remove(menu);
        }

        static VisualElement Bound(VisualElement field)
        {
            var row = new VisualElement();
            row.AddToClassList("ad-row");
            row.Add(field);
            return row;
        }
    }
}
#endif
