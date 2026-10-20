using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// What Poiyomi's lock does to an animated material property, read from plain material data only: the shader's
    /// name and the material's tags. Poiyomi locks every material at upload, a variant through its root, and leaves a
    /// material that is already locked as it is. A locked material animates a property only when its tag
    /// "&lt;property&gt;Animated" is set: "1" keeps the name, "2" ("Renamed (when locked)") animates it as
    /// "&lt;property&gt;_&lt;suffix&gt;". Every other property is fixed at its value.
    /// </summary>
    public static class PoiyomiLock
    {
        const string AnimatedTag = "Animated";
        const string SuffixTag = "thry_rename_suffix";
        const string LockedSuffixTag = "thry_locked_rename_suffix";
        const string LockedPrefix = "Hidden/Locked/";
        const string Tiling = "_ST";

        /// <summary>A material whose shader is named like Poiyomi's, locked or not.</summary>
        public static bool IsPoiyomi(Material material)
            => material != null && material.shader != null && material.shader.name.IndexOf("poiyomi", StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool IsLocked(Material material)
        {
            var root = Root(material);
            return IsPoiyomi(root) && root.shader.name.StartsWith(LockedPrefix, StringComparison.Ordinal);
        }

        /// <summary>The material Poiyomi locks for this one: a variant is locked through the top of its parent chain, with that material's name and tags.</summary>
        public static Material Root(Material material)
        {
            var root = material;
            for (int depth = 0; root != null && depth < 64; depth++)
            {
                var parent = root.parent;
                if (parent == null || parent == material) break;
                root = parent;
            }
            return root;
        }

        /// <summary>Trimmed, spaces removed, and every byte of its UTF-8 form that is not A to Z, a to z, _ or 0 to 9 written as two upper case hex digits.</summary>
        public static string CleanName(string name)
        {
            var bytes = Encoding.UTF8.GetBytes((name ?? "").Trim().Replace(" ", ""));
            var cleaned = new StringBuilder(bytes.Length);
            foreach (var b in bytes)
            {
                bool kept = (b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z') || b == '_' || (b >= '0' && b <= '9');
                if (kept) cleaned.Append((char)b);
                else cleaned.Append(b.ToString("X2"));
            }
            return cleaned.ToString();
        }

        /// <summary>
        /// What a renamed property gets after its name. A locked material keeps the suffix it was locked with (tag
        /// thry_locked_rename_suffix), whatever it is called now. Otherwise the tag thry_rename_suffix, or the
        /// material's name when that is missing, cleaned. Read from the root of a variant.
        /// </summary>
        public static string Suffix(Material material)
        {
            var root = Root(material);
            if (IsLocked(root))
            {
                var locked = root.GetTag(LockedSuffixTag, false, "");
                if (!string.IsNullOrEmpty(locked)) return locked;
            }
            return CleanName(root.GetTag(SuffixTag, false, root.name));
        }

        /// <summary>The material's mark for a property, read from the root of a variant: "" none, "1" animated, "2" renamed. A texture's tiling is marked with its texture.</summary>
        public static string Mark(Material material, string property) => Root(material).GetTag(MarkedName(material, property) + AnimatedTag, false, "");

        /// <summary>The name a mark is kept under: the texture for its tiling "&lt;texture&gt;_ST", the property itself otherwise.</summary>
        static string MarkedName(Material material, string property)
        {
            if (property.EndsWith(Tiling, StringComparison.Ordinal) && property.Length > Tiling.Length)
            {
                var texture = property.Substring(0, property.Length - Tiling.Length);
                var shader = material.shader;
                int index = shader != null ? shader.FindPropertyIndex(texture) : -1;
                if (index >= 0 && shader.GetPropertyType(index) == ShaderPropertyType.Texture) return texture;
            }
            return property;
        }

        /// <summary>Whether Poiyomi renames this property when it locks the material: marked "2", and not a name it never renames.</summary>
        static bool Renames(Material material, string name)
            => Root(material).GetTag(name + AnimatedTag, false, "") == "2"
               && !name.EndsWith("UV", StringComparison.Ordinal) && !name.EndsWith("Pan", StringComparison.Ordinal) && !name.EndsWith(AnimatedTag, StringComparison.Ordinal);

        /// <summary>The name Poiyomi animates this property under once it locked the material, or null when the name stays. A texture's tiling "&lt;texture&gt;_ST" becomes "&lt;texture&gt;_&lt;suffix&gt;_ST".</summary>
        public static string RenamedName(Material material, string property)
        {
            if (!IsPoiyomi(material) || string.IsNullOrEmpty(property)) return null;
            if (property.EndsWith(Tiling, StringComparison.Ordinal) && property.Length > Tiling.Length)
            {
                var texture = property.Substring(0, property.Length - Tiling.Length);
                if (Renames(material, texture)) return texture + "_" + Suffix(material) + Tiling;
            }
            return Renames(material, property) ? property + "_" + Suffix(material) : null;
        }

        /// <summary>The name this property had before Poiyomi renamed it, when it is already a renamed name (one picked from a locked material); null otherwise.</summary>
        static string OriginalName(Material material, string property)
        {
            if (!IsPoiyomi(material) || string.IsNullOrEmpty(property)) return null;
            var suffix = "_" + Suffix(material);
            if (property.EndsWith(suffix + Tiling, StringComparison.Ordinal) && property.Length > suffix.Length + Tiling.Length)
            {
                var texture = property.Substring(0, property.Length - suffix.Length - Tiling.Length);
                if (Renames(material, texture)) return texture + Tiling;
            }
            if (property.EndsWith(suffix, StringComparison.Ordinal) && property.Length > suffix.Length)
            {
                var original = property.Substring(0, property.Length - suffix.Length);
                if (Renames(material, original)) return original;
            }
            return null;
        }

        /// <summary>
        /// The name the material draws this property with: the name itself, or on a Poiyomi material the renamed or the
        /// original name of a renamed property. A locked material that kept the old definition beside the renamed one
        /// (Poiyomi copies a few standard names instead of renaming them) draws with the renamed one. Null when the
        /// material has none of them.
        /// </summary>
        public static string NameOn(Material material, string property)
        {
            if (material == null || string.IsNullOrEmpty(property)) return null;
            if (material.HasProperty(property))
            {
                if (!IsLocked(material)) return property;
                var used = RenamedName(material, property);
                return used != null && material.HasProperty(used) ? used : property;
            }
            if (!IsPoiyomi(material)) return null;
            var renamed = RenamedName(material, property);
            if (renamed != null && material.HasProperty(renamed)) return renamed;
            var original = OriginalName(material, property);
            if (original != null && material.HasProperty(original)) return original;
            return null;
        }

        /// <summary>
        /// Every name a curve for this property is written under on the renderer, with the material whose own value a
        /// rest of that name is read from (null: the value of the first material, as for the name itself). First the
        /// name itself; then for each Poiyomi material, the renderer's own and the ones swapped onto it, the name it
        /// renames the property to once locked. A name picked from a locked material also gets its original name while
        /// the material is unlocked, so it works in Play mode too, unless another material would still animate that
        /// original name in game (OtherAnimates).
        /// </summary>
        public static List<(string name, Material source)> CurveTargets(Renderer renderer, string property, IEnumerable<Material> swapped = null)
        {
            var targets = new List<(string name, Material source)> { (property, null) };
            if (renderer == null || string.IsNullOrEmpty(property)) return targets;
            void Add(string name, Material source)
            {
                if (!targets.Any(t => t.name == name)) targets.Add((name, source));
            }
            foreach (var material in MaterialsOf(renderer, swapped))
            {
                if (!IsPoiyomi(material) || NameOn(material, property) == null) continue;
                var renamed = RenamedName(material, property);
                if (renamed != null) Add(renamed, material);
                else if (!IsLocked(material))
                {
                    var original = OriginalName(material, property);
                    if (original != null && material.HasProperty(original) && !OtherAnimates(renderer, swapped, property, original)) Add(original, null);
                }
            }
            return targets;
        }

        /// <summary>
        /// Whether a material that did not give the property this renamed name still animates the original name once
        /// locked: a shader that is not Poiyomi's, or a Poiyomi material that marks it without renaming it or never
        /// locks it. The original name is a curve on the whole renderer, so in game it would reach that material too.
        /// </summary>
        static bool OtherAnimates(Renderer renderer, IEnumerable<Material> swapped, string renamedName, string original)
        {
            foreach (var other in MaterialsOf(renderer, swapped))
            {
                if (OriginalName(other, renamedName) != null || !other.HasProperty(original)) continue;
                if (!IsPoiyomi(other)) return true;
                if (RenamedName(other, original) == null && (Mark(other, original).Length > 0 || NeverLocked(other, original))) return true;
            }
            return false;
        }

        /// <summary>The names of CurveTargets alone.</summary>
        public static List<string> CurveNames(Renderer renderer, string property, IEnumerable<Material> swapped = null)
            => CurveTargets(renderer, property, swapped).Select(t => t.name).ToList();

        /// <summary>The unlocked Poiyomi materials (the roots of variants) on the renderer or swapped onto it that have the property and do not mark it: Poiyomi fixes it at its value when it locks them at upload.</summary>
        public static List<Material> Unmarked(Renderer renderer, string property, IEnumerable<Material> swapped = null) => Fixed(renderer, property, swapped, locked: false);

        /// <summary>The locked Poiyomi materials (the roots of variants) on the renderer or swapped onto it that fixed the property at its value: only unlocking, marking and locking again changes that.</summary>
        public static List<Material> Baked(Renderer renderer, string property, IEnumerable<Material> swapped = null) => Fixed(renderer, property, swapped, locked: true);

        static List<Material> Fixed(Renderer renderer, string property, IEnumerable<Material> swapped, bool locked)
        {
            var found = new List<Material>();
            if (renderer == null || string.IsNullOrEmpty(property)) return found;
            foreach (var material in MaterialsOf(renderer, swapped))
            {
                if (!IsPoiyomi(material) || IsLocked(material) != locked) continue;
                // A renamed property on a locked material, or its original name there, is animated.
                if (!material.HasProperty(property) || OriginalName(material, property) != null) continue;
                if (Mark(material, property).Length > 0)
                {
                    // Marked "Renamed" only after the lock: the locked shader still has the old name alone.
                    var renamed = locked ? RenamedName(material, property) : null;
                    if (renamed == null || material.HasProperty(renamed)) continue;
                }
                else if (NeverLocked(material, property)) continue;
                var root = Root(material);
                if (!found.Contains(root)) found.Add(root);
            }
            return found;
        }

        /// <summary>A property Poiyomi's lock leaves alone, a texture's tiling with its texture: a name ending in Animated, a display name ending in NL, a DoNotLock attribute, or a value that is not a texture but flagged as unchangeable texture data.</summary>
        static bool NeverLocked(Material material, string property)
        {
            var name = MarkedName(material, property);
            if (name.EndsWith(AnimatedTag, StringComparison.Ordinal)) return true;
            var shader = material.shader;
            int index = shader.FindPropertyIndex(name);
            if (index < 0) return false;
            if (shader.GetPropertyDescription(index).EndsWith("NL", StringComparison.Ordinal)) return true;
            if (shader.GetPropertyType(index) != ShaderPropertyType.Texture && (shader.GetPropertyFlags(index) & ShaderPropertyFlags.NonModifiableTextureData) != 0) return true;
            return Array.IndexOf(shader.GetPropertyAttributes(index), "DoNotLock") >= 0;
        }

        /// <summary>
        /// Marks the property "Animated (when locked)" on each unlocked Poiyomi material given, on the root of a variant
        /// (the material Poiyomi locks), in one Undo step. A locked material is left alone: it reads a new mark only
        /// when it is locked again. False when nothing was marked.
        /// </summary>
        public static bool MarkAnimated(IEnumerable<Material> materials, string property)
        {
            if (materials == null || string.IsNullOrEmpty(property)) return false;
            var roots = materials.Select(Root).Where(m => IsPoiyomi(m) && !IsLocked(m)).Distinct().ToArray();
            if (roots.Length == 0) return false;
            Undo.RecordObjects(roots, "Mark as animated");
            foreach (var root in roots)
            {
                root.SetOverrideTag(MarkedName(root, property) + AnimatedTag, "1");
                EditorUtility.SetDirty(root);
            }
            return true;
        }

        /// <summary>The renderer's own materials, then the ones swapped onto it, each once.</summary>
        static IEnumerable<Material> MaterialsOf(Renderer renderer, IEnumerable<Material> swapped)
        {
            var seen = new HashSet<Material>();
            foreach (var material in renderer.sharedMaterials)
                if (material != null && seen.Add(material)) yield return material;
            if (swapped == null) yield break;
            foreach (var material in swapped)
                if (material != null && seen.Add(material)) yield return material;
        }
    }
}
