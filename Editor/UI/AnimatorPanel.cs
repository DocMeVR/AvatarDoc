#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The panel of one merged animator: controller, path root, write defaults, platform, a layer check list that
    /// writes skipLayers, and a read only list of the controller's parameters with who declares each.
    /// </summary>
    public sealed class AnimatorPanel : VisualElement
    {
        readonly EntryDetailView detail;
        readonly AvatarDoctorView owner;
        readonly TreeRow row;
        readonly SerializedProperty property;
        readonly VisualElement layersBox = new VisualElement { name = "ad-animator-layers" };
        readonly VisualElement parametersBox = new VisualElement { name = "ad-animator-parameters" };

        AnimatorMerge Merge => row.Merge;

        /// <summary>How often an animator panel read the avatar's existing parameters. Read by the tests.</summary>
        public static int ExistingReads { get; private set; }

        public AnimatorPanel(EntryDetailView detail, AvatarDoctorView owner, TreeRow row, SerializedProperty property)
        {
            this.detail = detail;
            this.owner = owner;
            this.row = row;
            this.property = property;

            var controllerField = detail.Fields.Object<RuntimeAnimatorController>(property.FindPropertyRelative(nameof(AnimatorMerge.controller)), L.Text("ad.ui.animator.controller"), owner.ApplyAndRefresh);
            controllerField.name = "ad-animator-controller";
            controllerField.tooltip = L.Text("ad.ui.animator.controller.tip");
            Add(Bound(controllerField));

            var pathRootField = detail.Fields.ObjectOnAvatar<Transform>(property.FindPropertyRelative(nameof(AnimatorMerge.pathRoot)), L.Text("ad.ui.animation.path_root"), () => owner.Plan?.AvatarRoot, PickKind.Transform, owner.ApplyAndRefresh);
            pathRootField.name = "ad-animator-path-root";
            pathRootField.tooltip = L.Text("ad.ui.animation.path_root.tip");
            Add(Bound(pathRootField));

            var matchWriteDefaultsRow = new VisualElement();
            matchWriteDefaultsRow.AddToClassList("ad-row");
            var matchWriteDefaultsLabel = new Label(L.Text("ad.ui.animator.match_write_defaults"));
            matchWriteDefaultsLabel.AddToClassList("ad-label");
            matchWriteDefaultsRow.Add(matchWriteDefaultsLabel);
            var matchWriteDefaults = detail.Fields.Toggle(property.FindPropertyRelative(nameof(AnimatorMerge.matchWriteDefaults)), "");
            matchWriteDefaults.name = "ad-animator-match-write-defaults";
            matchWriteDefaults.tooltip = L.Text("ad.ui.animator.match_write_defaults.tip");
            matchWriteDefaultsRow.Add(matchWriteDefaults);
            Add(matchWriteDefaultsRow);

            var platformField = detail.Fields.EnumChoice(property.FindPropertyRelative(nameof(AnimatorMerge.platform)), L.Text("ad.ui.platform"),
                new[] { L.Text("ad.ui.platform.inherit"), L.Text("ad.ui.platform.both"), L.Text("ad.ui.platform.pc"), L.Text("ad.ui.platform.quest") });
            platformField.name = "ad-animator-platform";
            platformField.tooltip = L.Text("ad.ui.platform.tip");
            Add(Bound(platformField));

            var layersTitle = new Label(L.Text("ad.ui.animator.layers")) { tooltip = L.Text("ad.ui.animator.layers.tip") };
            layersTitle.AddToClassList("ad-label");
            layersTitle.AddToClassList("ad-section");
            Add(layersTitle);
            Add(layersBox);

            var parametersTitle = new Label(L.Text("ad.ui.animator.parameters")) { tooltip = L.Text("ad.ui.animator.parameters.tip") };
            parametersTitle.AddToClassList("ad-label");
            parametersTitle.AddToClassList("ad-section");
            Add(parametersTitle);
            Add(parametersBox);

            var note = new Label(L.Text("ad.ui.animator.no_preview"));
            note.AddToClassList("ad-note");
            Add(note);

            Rebuild();
        }

        /// <summary>Rewrites the layer check list and the parameter list from the controller. Called after every structural change, since the controller's own layers and parameters cannot be bound as SerializedProperty rows.</summary>
        public void Rebuild()
        {
            RebuildLayers();
            RebuildParameters();
        }

        void RebuildLayers()
        {
            layersBox.Clear();
            var merge = Merge;
            var layers = MergeBuilder.LayerNames(merge.controller);
            if (layers.Count == 0)
            {
                var none = new Label(L.Text("ad.ui.detail.none"));
                none.AddToClassList("ad-note");
                layersBox.Add(none);
                return;
            }
            foreach (var layer in layers)
            {
                var name = layer;
                var toggle = new Toggle(layer) { value = !merge.skipLayers.Contains(layer), name = "ad-animator-layer-" + layer, tooltip = L.Text("ad.ui.animator.layers.tip") };
                toggle.RegisterValueChangedCallback(e =>
                {
                    toggle.Blur();
                    detail.SetAnimatorLayerSkipped(name, !e.newValue);
                });
                layersBox.Add(toggle);
            }
        }

        void RebuildParameters()
        {
            parametersBox.Clear();
            var merge = Merge;
            var parameters = MergeBuilder.ParametersOf(merge.controller);
            if (parameters.Count == 0)
            {
                var none = new Label(L.Text("ad.ui.detail.none"));
                none.AddToClassList("ad-note");
                parametersBox.Add(none);
                return;
            }
            var planned = PlannedByName(owner.Plan);
            ExistingReads++;
            var existing = ExistingOf(owner.Plan);
            foreach (var parameter in parameters)
            {
                parametersBox.Add(ParameterRow(parameter.name, planned, existing));
            }
        }

        /// <summary>The plan's parameters by name, first one kept, so each row looks its own up instead of walking the list.</summary>
        internal static Dictionary<string, PlannedParameter> PlannedByName(AvatarPlan plan)
        {
            var byName = new Dictionary<string, PlannedParameter>();
            if (plan?.Plan == null) return byName;
            foreach (var planned in plan.Plan.Parameters)
                if (planned.Name != null && !byName.ContainsKey(planned.Name)) byName[planned.Name] = planned;
            return byName;
        }

        /// <summary>The parameters the avatar already declares, read once per rebuild: the read copies the FX controller's whole parameter list.</summary>
        internal static Dictionary<string, ExistingParameter> ExistingOf(AvatarPlan plan)
        {
            var descriptor = plan?.AvatarRoot != null ? plan.AvatarRoot.GetComponent<VRCAvatarDescriptor>() : null;
            return ParametersWriter.ExistingParameters(descriptor != null ? descriptor.expressionParameters : null, ParametersWriter.FxOf(descriptor));
        }

        VisualElement ParameterRow(string parameterName, Dictionary<string, PlannedParameter> planned, Dictionary<string, ExistingParameter> existing)
        {
            var container = new VisualElement();
            container.AddToClassList("ad-target");

            var name = new Label(parameterName) { name = "ad-animator-param-name" };
            name.AddToClassList("ad-grow");
            container.Add(name);

            bool notSynced;
            var declaredBy = DeclaredBy(parameterName, planned, existing, out notSynced);
            var declared = new Label(declaredBy) { name = "ad-animator-param-declared" };
            declared.AddToClassList("ad-note");
            container.Add(declared);

            if (notSynced)
            {
                var add = new Button(() => detail.AddControlForParameter(parameterName)) { text = L.Text("ad.ui.animator.add_control"), name = "ad-animator-add-control", tooltip = L.Text("ad.ui.animator.add_control.tip") };
                container.Add(add);
            }
            return container;
        }

        /// <summary>Who declares this controller parameter: the entry that names it as its own, "existing on the avatar" for one the avatar already had before this merge, "declared" with its real sync state for one a component declares, or "not synced" for one nothing declares.</summary>
        static string DeclaredBy(string parameterName, Dictionary<string, PlannedParameter> plannedByName, Dictionary<string, ExistingParameter> existing, out bool notSynced)
        {
            notSynced = false;
            plannedByName.TryGetValue(parameterName, out var planned);
            if (planned != null && planned.Controls.Count > 0) return planned.Controls[0].Entry.PlainName;
            if (planned != null && (planned.ReusesExisting || planned.IsAnimatorOnly)) return L.Text("ad.ui.animator.existing");
            if (existing.ContainsKey(parameterName)) return L.Text("ad.ui.animator.existing");

            notSynced = true;
            if (planned != null && planned.Declared) return L.Text(planned.Synced ? "ad.ui.animator.declared_synced" : "ad.ui.animator.declared_local");
            return L.Text("ad.ui.animator.not_synced");
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
