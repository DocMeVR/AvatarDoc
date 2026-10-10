#if DOCME_NDMF && DOCME_VRCSDK3A
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Stacked bar: the avatar's other synced bits, AvatarDoctor's bits, free space; red when over the limit.</summary>
    public sealed class MemoryBar : VisualElement
    {
        public int AvatarBits { get; private set; }
        public int AvatarDoctorBits { get; private set; }
        public int Limit { get; private set; } = 1;

        public MemoryBar()
        {
            AddToClassList("ad-membar");
            generateVisualContent += Draw;
        }

        public void Set(int avatarBits, int avatarDoctorBits, int limit)
        {
            AvatarBits = avatarBits;
            AvatarDoctorBits = avatarDoctorBits;
            Limit = Mathf.Max(1, limit);
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D;
            float w = resolvedStyle.width, h = resolvedStyle.height;
            bool over = AvatarBits + AvatarDoctorBits > Limit;
            Rect(p, 0f, w, h, Colour("--ad-track", new Color(0f, 0f, 0f, 0.35f)));
            float avatarWidth = Mathf.Min(w, w * AvatarBits / Limit);
            float doctorWidth = Mathf.Min(w - avatarWidth, w * AvatarDoctorBits / Limit);
            Rect(p, 0f, avatarWidth, h, Colour("--ad-muted", new Color(1f, 1f, 1f, 0.72f)));
            Rect(p, avatarWidth, doctorWidth, h, over ? Colour("--ad-error", new Color(0.898f, 0.392f, 0.353f)) : Colour("--ad-accent", new Color(0.247f, 0.788f, 0.659f)));
        }

        static void Rect(Painter2D p, float x, float width, float height, Color color)
        {
            if (width <= 0f) return;
            p.fillColor = color;
            p.BeginPath();
            p.MoveTo(new Vector2(x, 0f));
            p.LineTo(new Vector2(x + width, 0f));
            p.LineTo(new Vector2(x + width, height));
            p.LineTo(new Vector2(x, height));
            p.ClosePath();
            p.Fill();
        }

        Color Colour(string variable, Color fallback)
        {
            // USS custom properties are not readable from C# in 2022.3, so the bar uses the same values the stylesheet declares.
            // Neither VisualElement.GetFirstAncestorWithClass nor GetFirstAncestorWhere exists in the 2022.3 UIElements API;
            // walking the documented `parent` chain is the equivalent.
            bool light = false;
            for (var ancestor = parent; ancestor != null; ancestor = ancestor.parent)
            {
                if (!ancestor.ClassListContains("ad-light")) continue;
                light = true;
                break;
            }
            switch (variable)
            {
                case "--ad-accent": return light ? new Color(0.071f, 0.541f, 0.431f) : new Color(0.247f, 0.788f, 0.659f);
                case "--ad-error": return light ? new Color(0.769f, 0.275f, 0.235f) : new Color(0.898f, 0.392f, 0.353f);
                case "--ad-muted": return light ? new Color(0f, 0f, 0f, 0.72f) : new Color(1f, 1f, 1f, 0.72f);
                case "--ad-track": return light ? new Color(0f, 0f, 0f, 0.15f) : new Color(0f, 0f, 0f, 0.35f);
                default: return fallback;
            }
        }
    }
}
#endif
