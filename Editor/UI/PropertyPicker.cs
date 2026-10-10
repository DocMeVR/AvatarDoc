#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Shader properties a renderer's materials expose, for the material property target dropdown.</summary>
    public static class PropertyPicker
    {
        public readonly struct Choice
        {
            public readonly string DisplayName;
            public readonly string PropertyName;
            public readonly PropertyValueType Type;
            public readonly bool IsRange;
            public readonly float RangeMin;
            public readonly float RangeMax;

            public Choice(string displayName, string propertyName, PropertyValueType type, bool isRange, float rangeMin, float rangeMax)
            {
                DisplayName = displayName;
                PropertyName = propertyName;
                Type = type;
                IsRange = isRange;
                RangeMin = rangeMin;
                RangeMax = rangeMax;
            }
        }

        /// <summary>One renderer's choices and the shaders they were read from, so a swapped material is noticed.</summary>
        static readonly Dictionary<Renderer, (string shaders, List<Choice> choices)> cache = new Dictionary<Renderer, (string, List<Choice>)>();
        static int cachedVersion = -1;

        /// <summary>
        /// Every float, range, colour and vector property across the renderer's shared materials' shaders, without
        /// duplicates, skipping textures, ints and properties hidden from the inspector. Walking every property of
        /// every shader is not cheap and a property row asks on every bind, so the answer is kept until the plan
        /// cache moves on or the renderer's shaders change. The list is shared: callers read it, never change it.
        /// </summary>
        public static List<Choice> Choices(Renderer renderer)
        {
            if (renderer == null) return new List<Choice>();
            if (cachedVersion != AvatarPlanCache.Version)
            {
                cachedVersion = AvatarPlanCache.Version;
                cache.Clear();
            }
            var shaders = ShaderKey(renderer);
            if (cache.TryGetValue(renderer, out var cached) && cached.shaders == shaders) return cached.choices;
            var built = Walk(renderer);
            cache[renderer] = (shaders, built);
            return built;
        }

        /// <summary>The renderer's shared materials' shaders by instance id, in slot order: what the choices were read from.</summary>
        static string ShaderKey(Renderer renderer)
        {
            var key = new StringBuilder();
            foreach (var material in renderer.sharedMaterials)
            {
                var shader = material != null ? material.shader : null;
                key.Append(shader != null ? shader.GetInstanceID() : 0).Append(',');
            }
            return key.ToString();
        }

        static List<Choice> Walk(Renderer renderer)
        {
            var result = new List<Choice>();
            var seen = new HashSet<string>();
            foreach (var material in renderer.sharedMaterials)
            {
                var shader = material != null ? material.shader : null;
                if (shader == null) continue;
                int count = shader.GetPropertyCount();
                for (int i = 0; i < count; i++)
                {
                    string name = shader.GetPropertyName(i);
                    if (!seen.Add(name)) continue;
                    if ((shader.GetPropertyFlags(i) & ShaderPropertyFlags.HideInInspector) != 0) continue;

                    var propertyType = shader.GetPropertyType(i);
                    bool isRange = propertyType == ShaderPropertyType.Range;
                    PropertyValueType mapped;
                    switch (propertyType)
                    {
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            mapped = PropertyValueType.Float;
                            break;
                        case ShaderPropertyType.Color:
                            mapped = PropertyValueType.Color;
                            break;
                        case ShaderPropertyType.Vector:
                            mapped = PropertyValueType.Vector;
                            break;
                        default:
                            continue; // Texture and Int properties are skipped.
                    }
                    float rangeMin = 0f, rangeMax = 0f;
                    if (isRange)
                    {
                        var limits = shader.GetPropertyRangeLimits(i);
                        rangeMin = limits.x;
                        rangeMax = limits.y;
                    }
                    string display = shader.GetPropertyDescription(i);
                    if (string.IsNullOrEmpty(display)) display = name;
                    result.Add(new Choice(display, name, mapped, isRange, rangeMin, rangeMax));
                }
            }
            return result.OrderBy(c => c.DisplayName).ToList();
        }
    }
}
#endif
