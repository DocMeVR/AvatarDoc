#if DOCME_VRCSDK3A
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>The value every effect takes for a given control state. Build clips, scene defaults and the preview all read from here.</summary>
    public static class EffectMath
    {
        public static bool ObjectActive(ObjectTarget target, bool on) => on ? target.activeWhenOn : !target.activeWhenOn;

        /// <summary>A toggle's blend shape weight. Off falls back to what the scene already holds, unless the target carries an off value of its own.</summary>
        public static float ToggleShapeWeight(BlendShapeTarget target, bool on, float baseWeight)
        {
            if (on) return target.weight;
            return target.explicitOff && target.mode != ShapeEffectMode.Pin ? target.minWeight : baseWeight;
        }

        /// <summary>A Pin target's main clip carries a value only while its control is on. Its curves live in a pin layer with Write Defaults off, which writes nothing for it while the control is off: AvatarDoctor writes its rest once, at load and on the way out, unless another layer owns it, and then leaves it to whatever else moves it.</summary>
        public static bool WritesWhenOff(BlendShapeTarget target) => target.mode != ShapeEffectMode.Pin;

        public static float RadialShapeWeight(BlendShapeTarget target, float t) => Mathf.Lerp(target.minWeight, target.maxWeight, Mathf.Clamp01(t));

        public static bool LinkAbove(float sourceWeight, float threshold) => sourceWeight >= threshold;

        /// <summary>A Follow effect's weight: the source weight times the multiplier, kept inside the blendshape range.</summary>
        public static float FollowWeight(float sourceWeight, float multiplier) => Mathf.Clamp(sourceWeight * multiplier, 0f, 100f);

        public static float LinkShapeWeight(BlendShapeTarget target, float sourceWeight, float threshold, float baseWeight)
        {
            if (target.mode == ShapeEffectMode.Follow) return FollowWeight(sourceWeight, target.multiplier);
            return LinkAbove(sourceWeight, threshold) ? target.weight : baseWeight;
        }

        /// <summary>How far a Smooth blend shape trigger's effects are on at this shape weight: 0 at or below Start, 1 at or above Full, a straight line between.</summary>
        public static float SmoothFraction(float sourceWeight, float start, float full)
        {
            if (full <= start) return sourceWeight >= start ? 1f : 0f;
            return Mathf.Clamp01((sourceWeight - start) / (full - start));
        }

        /// <summary>How far a blend shape trigger's shape and property effects are on at this weight: the Smooth fraction, or all or nothing at the threshold for Switch.</summary>
        public static float LinkFraction(MenuEntry entry, float sourceWeight)
        {
            if (entry.linkStyle == BlendShapeLinkStyle.Switch) return LinkAbove(sourceWeight, entry.sourceThreshold) ? 1f : 0f;
            return SmoothFraction(sourceWeight, entry.EffectiveSmoothStart, entry.EffectiveSmoothFull);
        }

        /// <summary>A blend shape trigger's shape effect at one source weight, in either style. A Follow row is the source times its multiplier in both; any other row goes from its rest (the scene weight) to its weight.</summary>
        public static float LinkShapeWeight(MenuEntry entry, BlendShapeTarget target, float sourceWeight, float baseWeight)
        {
            if (target.mode == ShapeEffectMode.Follow) return FollowWeight(sourceWeight, target.multiplier);
            return Mathf.Lerp(baseWeight, target.weight, LinkFraction(entry, sourceWeight));
        }

        /// <summary>A blend shape trigger's property effect at one source weight: from the row's rest (its off value, else the material's own) to its value.</summary>
        public static Vector4 LinkPropertyValue(MenuEntry entry, MaterialPropertyTarget target, float sourceWeight, Vector4 baseValue)
            => Vector4.Lerp(PropertyValue(target, false, baseValue), PropertyValue(target, true, baseValue), LinkFraction(entry, sourceWeight));

        public static Material ToggleMaterial(MaterialTarget target, bool on, Material baseMaterial) => on ? target.material : baseMaterial;

        /// <summary>A property target's value while a toggle is on or off, packed into a Vector4 (float in x, colour in rgba, vector in xyzw). Off falls back to what the materials already hold, unless the target carries an off value of its own.</summary>
        public static Vector4 PropertyValue(MaterialPropertyTarget target, bool on, Vector4 baseValue)
        {
            if (on) return PackedValue(target);
            return target.explicitOff ? PackedMinValue(target) : baseValue;
        }

        /// <summary>A property target's value at position t (0..1) of a radial, blended from the lower to the upper end.</summary>
        public static Vector4 RadialPropertyValue(MaterialPropertyTarget target, float t) => Vector4.Lerp(PackedMinValue(target), PackedValue(target), Mathf.Clamp01(t));

        static Vector4 PackedValue(MaterialPropertyTarget target)
        {
            switch (target.type)
            {
                case PropertyValueType.Color: return target.color;
                case PropertyValueType.Vector: return target.vector;
                default: return new Vector4(target.value, 0f, 0f, 0f);
            }
        }

        static Vector4 PackedMinValue(MaterialPropertyTarget target)
        {
            switch (target.type)
            {
                case PropertyValueType.Color: return target.minColor;
                case PropertyValueType.Vector: return target.minVector;
                default: return new Vector4(target.minValue, 0f, 0f, 0f);
            }
        }
    }
}
#endif
