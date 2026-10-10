#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>A name field with whole-name styling (colour, bold, italic) and a raw-tags fallback. Raises Changed with the composed rich name.</summary>
    public sealed class NameStyleRow : VisualElement
    {
        public event Action<string> Changed;
        public string Value { get; private set; } = "";

        readonly TextField plain;
        readonly TextField raw;
        readonly ColorField color;
        readonly Button clearColor;
        readonly Button bold;
        readonly Button italic;
        readonly Toggle rawToggle;
        readonly Label preview;
        readonly VisualElement previewRow;
        readonly Label customNote;
        readonly VisualElement styleLine;
        readonly VisualElement rawRow;
        readonly bool styled;
        readonly bool compact;
        bool styleRowsShown;
        RichName.Style style;
        bool showRaw;
        bool loading;

        /// <param name="styled">False for entries that never reach the VRChat menu: they get the name field alone.</param>
        /// <param name="compact">True shows the name field alone while the name carries no style; a name that does keeps the style rows, so a style the creator set is never hidden.</param>
        /// <param name="keepStyleRows">With compact, shows the style rows from the start: they were shown for this name before the panel was built again, and a part already shown stays until another entry is selected.</param>
        public NameStyleRow(string label, bool styled = true, bool compact = false, bool keepStyleRows = false)
        {
            this.styled = styled;
            this.compact = compact;
            styleRowsShown = !compact || keepStyleRows;
            AddToClassList("ad-name-style");
            plain = new TextField(label) { name = "ad-name", isDelayed = true, tooltip = L.Text("ad.ui.name.tip") };
            plain.RegisterValueChangedCallback(e => { if (!loading) SetPlain(e.newValue); });
            Add(plain);
            raw = new TextField(label) { name = "ad-name-raw", isDelayed = true, tooltip = L.Text("ad.ui.name.tip") };
            raw.RegisterValueChangedCallback(e => { if (!loading) SetRawText(e.newValue); });
            Add(raw);
            var line = new VisualElement();
            styleLine = line;
            line.AddToClassList("ad-row");
            var styleLabel = new Label(L.Text("ad.ui.style")) { tooltip = L.Text("ad.ui.style.tip") };
            styleLabel.AddToClassList("ad-label");
            line.Add(styleLabel);
            color = new ColorField { name = "ad-style-color", showAlpha = false, showEyeDropper = false, tooltip = L.Text("ad.ui.style.color.tip") };
            color.AddToClassList("ad-style-color");
            color.RegisterValueChangedCallback(e => { if (!loading) SetColor(e.newValue); });
            line.Add(color);
            clearColor = new Button(() => SetColor(null)) { text = "x", name = "ad-style-clear-color", tooltip = L.Text("ad.ui.style.clear_color.tip") };
            line.Add(clearColor);
            bold = new Button(() => SetBold(!style.Bold)) { text = L.Text("ad.ui.style.bold"), name = "ad-style-bold", tooltip = L.Text("ad.ui.style.bold.tip") };
            bold.AddToClassList("ad-style-bold");
            line.Add(bold);
            italic = new Button(() => SetItalic(!style.Italic)) { text = L.Text("ad.ui.style.italic"), name = "ad-style-italic", tooltip = L.Text("ad.ui.style.italic.tip") };
            italic.AddToClassList("ad-style-italic");
            line.Add(italic);
            Add(line);
            // Its own labelled row under Style, not crammed onto the same line: the swatch, x, B and I already
            // fill the row at the default inspector width, so a fifth control there ran its own checkbox off the
            // right edge.
            rawRow = new VisualElement();
            rawRow.AddToClassList("ad-row");
            var rawLabel = new Label(L.Text("ad.ui.style.raw_label")) { tooltip = L.Text("ad.ui.style.raw.tip") };
            rawLabel.AddToClassList("ad-label");
            rawRow.Add(rawLabel);
            rawToggle = new Toggle { name = "ad-style-raw", tooltip = L.Text("ad.ui.style.raw.tip") };
            rawToggle.RegisterValueChangedCallback(e => { if (!loading) SetRaw(e.newValue); });
            rawRow.Add(rawToggle);
            Add(rawRow);
            customNote = new Label(L.Text("ad.ui.style.custom"));
            customNote.AddToClassList("ad-note");
            customNote.AddToClassList("ad-note-indent");
            Add(customNote);
            previewRow = new VisualElement();
            previewRow.AddToClassList("ad-row");
            var previewLabel = new Label(L.Text("ad.ui.style.preview")) { tooltip = L.Text("ad.ui.style.preview.tip") };
            previewLabel.AddToClassList("ad-label");
            previewRow.Add(previewLabel);
            preview = new Label { name = "ad-name-preview", enableRichText = true };
            preview.AddToClassList("ad-name-preview");
            previewRow.Add(preview);
            Add(previewRow);
            if (!styled)
            {
                line.AddToClassList("ad-hidden");
                rawRow.AddToClassList("ad-hidden");
                previewRow.AddToClassList("ad-hidden");
            }
        }

        /// <summary>Whether the name carries a colour, bold, italic or hand written tags.</summary>
        bool StyleInUse => Value != style.Plain || style.Custom;

        /// <summary>
        /// Whether the style rows have been shown since this row was built. Once shown they stay, even after the last
        /// style is taken off, so the row the creator is using does not vanish under the pointer; a name that gains a
        /// style shows them at once.
        /// </summary>
        public bool StyleRowsShown => styleRowsShown;

        /// <summary>Shows a stored name without raising Changed.</summary>
        public void Load(string richName)
        {
            loading = true;
            Value = richName ?? "";
            style = RichName.Parse(Value);
            if (style.Custom) showRaw = true;
            Refresh();
            loading = false;
        }

        public void SetPlain(string text)
        {
            if (!styled) { Publish((text ?? "").Trim()); return; }
            if (style.Custom) return;
            style.Plain = (text ?? "").Trim();
            Publish(RichName.Compose(style.Plain, style.Color, style.Bold, style.Italic));
        }

        public void SetRawText(string text)
        {
            Publish((text ?? "").Trim());
        }

        public void SetColor(Color? value)
        {
            if (style.Custom) return;
            style.Color = value;
            Publish(RichName.Compose(style.Plain, style.Color, style.Bold, style.Italic));
        }

        public void SetBold(bool value)
        {
            if (style.Custom) return;
            style.Bold = value;
            Publish(RichName.Compose(style.Plain, style.Color, style.Bold, style.Italic));
        }

        public void SetItalic(bool value)
        {
            if (style.Custom) return;
            style.Italic = value;
            Publish(RichName.Compose(style.Plain, style.Color, style.Bold, style.Italic));
        }

        public void SetRaw(bool value)
        {
            showRaw = value || style.Custom;
            Refresh();
        }

        void Publish(string richName)
        {
            if (richName == Value) { Refresh(); return; }
            Value = richName;
            style = RichName.Parse(Value);
            if (!style.Custom && rawToggle.value == false) showRaw = false;
            Refresh();
            Changed?.Invoke(Value);
        }

        void Refresh()
        {
            bool custom = styled && style.Custom;
            bool rawVisible = styled && (showRaw || custom);
            plain.SetValueWithoutNotify(styled ? style.Plain : Value);
            raw.SetValueWithoutNotify(Value);
            plain.EnableInClassList("ad-hidden", rawVisible);
            raw.EnableInClassList("ad-hidden", !rawVisible);
            color.SetValueWithoutNotify(style.Color ?? Color.white);
            color.SetEnabled(!custom);
            clearColor.SetEnabled(!custom && style.Color.HasValue);
            bold.SetEnabled(!custom);
            italic.SetEnabled(!custom);
            bold.EnableInClassList("ad-primary", style.Bold);
            italic.EnableInClassList("ad-primary", style.Italic);
            rawToggle.SetValueWithoutNotify(rawVisible);
            rawToggle.SetEnabled(!custom);
            customNote.EnableInClassList("ad-hidden", !custom);
            preview.text = Value;
            if (StyleInUse) styleRowsShown = true;
            bool hideStyle = !styled || !styleRowsShown;
            styleLine.EnableInClassList("ad-hidden", hideStyle);
            rawRow.EnableInClassList("ad-hidden", hideStyle);
            if (styled) previewRow.EnableInClassList("ad-hidden", Value == style.Plain && !custom);
        }
    }
}
#endif
