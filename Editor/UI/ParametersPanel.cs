#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The compact table of the parameters this component declares itself: name, type, default, saved and synced
    /// per row, the bits each one costs, and the total at the bottom.
    /// </summary>
    public sealed class ParametersPanel : VisualElement
    {
        static readonly string[] TypeLabels = { "Bool", "Int", "Float" };

        readonly EntryDetailView detail;
        readonly AvatarDoctorView owner;

        public ParametersPanel(EntryDetailView detail, AvatarDoctorView owner)
        {
            this.detail = detail;
            this.owner = owner;
            name = "ad-parameters-table";

            var declarations = owner.Menu != null ? owner.Menu.parameters : new List<ParameterDeclaration>();
            if (declarations.Count == 0)
            {
                var none = new Label(L.Text("ad.ui.parameters.none"));
                none.AddToClassList("ad-note");
                Add(none);
            }
            for (int i = 0; i < declarations.Count; i++)
            {
                if (declarations[i] == null) continue;
                Add(Card(declarations[i], i));
            }

            int total = declarations.Where(d => d != null).Sum(d => d.Bits);
            var totalLabel = new Label(string.Format(L.Text("ad.ui.parameters.total"), total)) { name = "ad-parameters-total" };
            totalLabel.AddToClassList("ad-note");
            Add(totalLabel);

            var add = new Button(() => detail.AddDeclarationRow()) { text = L.Text("ad.ui.parameters.add"), name = "ad-parameters-add", tooltip = L.Text("ad.ui.parameters.add.tip") };
            Add(add);
        }

        VisualElement Card(ParameterDeclaration declaration, int index)
        {
            var property = owner.DeclarationProperty(declaration);
            var card = new VisualElement { name = "ad-parameter-row-" + index };
            card.AddToClassList("ad-when-card");
            if (property == null) return card;

            var head = new VisualElement();
            head.AddToClassList("ad-row");
            var nameField = detail.Fields.Text(property.FindPropertyRelative(nameof(ParameterDeclaration.name)), null, owner.ApplyAndRefresh);
            nameField.name = "ad-parameter-name-" + index;
            nameField.tooltip = L.Text("ad.ui.parameters.row_name.tip");
            nameField.AddToClassList("ad-grow");
            head.Add(nameField);
            var bits = new Label(string.Format(L.Text("ad.ui.parameters.bits"), declaration.Bits));
            bits.AddToClassList("ad-note");
            head.Add(bits);
            var remove = new Button(() => detail.RemoveDeclarationRow(declaration)) { text = "x", name = "ad-parameter-remove-" + index };
            remove.AddToClassList("ad-w-24");
            head.Add(remove);
            card.Add(head);

            var second = new VisualElement();
            second.AddToClassList("ad-row");
            var type = detail.Fields.EnumSegments(property.FindPropertyRelative(nameof(ParameterDeclaration.type)), TypeLabels, owner.ApplyAndRefresh);
            type.name = "ad-parameter-type-" + index;
            type.tooltip = L.Text("ad.ui.parameters.type.tip");
            type.AddToClassList("ad-grow");
            second.Add(type);
            var value = detail.Fields.Float(property.FindPropertyRelative(nameof(ParameterDeclaration.defaultValue)), null, owner.ApplyAndRefresh);
            value.name = "ad-parameter-default-" + index;
            value.tooltip = L.Text("ad.ui.parameters.default.tip");
            value.AddToClassList("ad-w-70");
            second.Add(value);
            card.Add(second);

            var third = new VisualElement();
            third.AddToClassList("ad-row");
            third.AddToClassList("ad-flags");
            var saved = detail.Fields.Toggle(property.FindPropertyRelative(nameof(ParameterDeclaration.saved)), L.Text("ad.ui.saved"), owner.ApplyAndRefresh);
            saved.name = "ad-parameter-saved-" + index;
            saved.tooltip = L.Text("ad.ui.saved.tip");
            third.Add(saved);
            var synced = detail.Fields.Toggle(property.FindPropertyRelative(nameof(ParameterDeclaration.synced)), L.Text("ad.ui.synced"), owner.ApplyAndRefresh);
            synced.name = "ad-parameter-synced-" + index;
            synced.tooltip = L.Text("ad.ui.synced.tip");
            synced.AddToClassList("ad-compact");
            third.Add(synced);
            card.Add(third);

            return card;
        }
    }
}
#endif
