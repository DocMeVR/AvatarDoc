#if DOCME_NDMF && DOCME_VRCSDK3A
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    public enum GlyphKind { Toggle, Radial, Submenu, Trigger, Existing, More, Group, Warning, Override, Preset, Animator, Grabbable }

    /// <summary>A small vector glyph for an entry kind, drawn in the element's current text colour.</summary>
    public sealed class KindGlyph : VisualElement
    {
        GlyphKind kind;

        public GlyphKind Kind
        {
            get => kind;
            set { kind = value; MarkDirtyRepaint(); }
        }

        public KindGlyph(GlyphKind kind)
        {
            this.kind = kind;
            AddToClassList("ad-glyph");
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D;
            var color = resolvedStyle.color;
            p.strokeColor = color;
            p.fillColor = color;
            p.lineWidth = 1.4f;
            float w = resolvedStyle.width, h = resolvedStyle.height;
            var c = new Vector2(w / 2f, h / 2f);
            switch (kind)
            {
                case GlyphKind.Toggle:
                    p.BeginPath();
                    p.Arc(new Vector2(c.x - 3f, c.y), 3f, Angle.Degrees(90), Angle.Degrees(270), ArcDirection.Clockwise);
                    p.Arc(new Vector2(c.x + 3f, c.y), 3f, Angle.Degrees(270), Angle.Degrees(90), ArcDirection.Clockwise);
                    p.ClosePath();
                    p.Stroke();
                    p.BeginPath();
                    p.Arc(new Vector2(c.x + 3f, c.y), 1.8f, Angle.Degrees(0), Angle.Degrees(360), ArcDirection.Clockwise);
                    p.Fill();
                    break;
                case GlyphKind.Radial:
                    p.BeginPath();
                    p.Arc(c, 5.5f, Angle.Degrees(0), Angle.Degrees(360), ArcDirection.Clockwise);
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(c);
                    p.LineTo(c + new Vector2(3.2f, -3.6f));
                    p.Stroke();
                    break;
                case GlyphKind.Submenu:
                    p.BeginPath();
                    p.MoveTo(new Vector2(1.5f, 4f));
                    p.LineTo(new Vector2(5.5f, 4f));
                    p.LineTo(new Vector2(7f, 5.5f));
                    p.LineTo(new Vector2(12.5f, 5.5f));
                    p.LineTo(new Vector2(12.5f, 12f));
                    p.LineTo(new Vector2(1.5f, 12f));
                    p.ClosePath();
                    p.Stroke();
                    break;
                case GlyphKind.Trigger:
                case GlyphKind.Group:
                    p.BeginPath();
                    p.MoveTo(new Vector2(8f, 1.5f));
                    p.LineTo(new Vector2(3f, 8f));
                    p.LineTo(new Vector2(7f, 8f));
                    p.LineTo(new Vector2(6f, 12.5f));
                    p.LineTo(new Vector2(11f, 6f));
                    p.LineTo(new Vector2(7f, 6f));
                    p.ClosePath();
                    p.Fill();
                    break;
                case GlyphKind.Override:
                    p.BeginPath();
                    p.MoveTo(new Vector2(2f, 9f));
                    p.LineTo(new Vector2(12f, 9f));
                    p.LineTo(new Vector2(12f, 11.5f));
                    p.LineTo(new Vector2(2f, 11.5f));
                    p.ClosePath();
                    p.Fill();
                    p.BeginPath();
                    p.MoveTo(new Vector2(5.5f, 2.5f));
                    p.LineTo(new Vector2(8.5f, 2.5f));
                    p.LineTo(new Vector2(8.5f, 7f));
                    p.LineTo(new Vector2(5.5f, 7f));
                    p.ClosePath();
                    p.Fill();
                    break;
                case GlyphKind.Preset:
                    for (int row = 0; row < 3; row++)
                    {
                        float y = 3f + row * 4f;
                        p.BeginPath();
                        p.MoveTo(new Vector2(2f, y));
                        p.LineTo(new Vector2(12f, y));
                        p.Stroke();
                        p.BeginPath();
                        p.Arc(new Vector2(row == 1 ? 5f : 9f, y), 1.6f, Angle.Degrees(0), Angle.Degrees(360), ArcDirection.Clockwise);
                        p.Fill();
                    }
                    break;
                case GlyphKind.Animator:
                    for (int row = 0; row < 3; row++)
                    {
                        float y = 3f + row * 4.5f;
                        p.BeginPath();
                        p.MoveTo(new Vector2(c.x, y));
                        p.LineTo(new Vector2(11.5f, y + 2.25f));
                        p.LineTo(new Vector2(c.x, y + 4.5f));
                        p.LineTo(new Vector2(2f, y + 2.25f));
                        p.ClosePath();
                        if (row == 1) p.Fill(); else p.Stroke();
                    }
                    break;
                case GlyphKind.Existing:
                    p.BeginPath();
                    p.MoveTo(new Vector2(2.5f, 2.5f));
                    p.LineTo(new Vector2(11.5f, 2.5f));
                    p.LineTo(new Vector2(11.5f, 11.5f));
                    p.LineTo(new Vector2(2.5f, 11.5f));
                    p.ClosePath();
                    p.Stroke();
                    break;
                case GlyphKind.More:
                    for (int i = -1; i <= 1; i++)
                    {
                        p.BeginPath();
                        p.Arc(new Vector2(c.x + i * 4f, c.y), 1.3f, Angle.Degrees(0), Angle.Degrees(360), ArcDirection.Clockwise);
                        p.Fill();
                    }
                    break;
                case GlyphKind.Grabbable:
                    p.BeginPath();
                    p.Arc(c, 5f, Angle.Degrees(150), Angle.Degrees(50), ArcDirection.Clockwise);
                    p.Stroke();
                    p.BeginPath();
                    p.Arc(c, 2.2f, Angle.Degrees(0), Angle.Degrees(360), ArcDirection.Clockwise);
                    p.Fill();
                    p.BeginPath();
                    p.MoveTo(new Vector2(c.x, c.y + 5f));
                    p.LineTo(new Vector2(c.x, h - 1.5f));
                    p.Stroke();
                    break;
                case GlyphKind.Warning:
                    p.BeginPath();
                    p.MoveTo(new Vector2(c.x, 1.5f));
                    p.LineTo(new Vector2(w - 1.5f, h - 1.5f));
                    p.LineTo(new Vector2(1.5f, h - 1.5f));
                    p.ClosePath();
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(new Vector2(c.x, 5f));
                    p.LineTo(new Vector2(c.x, 8.5f));
                    p.Stroke();
                    break;
            }
        }
    }
}
#endif
