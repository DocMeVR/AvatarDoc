#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Suggests blend shapes on the avatar's other meshes that a piece's name matches, so the wizard can offer a shrink target for skin under clothing. Pure and cheap: safe to call on every piece change.</summary>
    public static class ShrinkSuggester
    {
        static readonly string[] Prefixes = { "shrink", "hide", "off", "kisekae", "cloth" };

        public static List<ShrinkSuggestion> For(Transform avatarRoot, GameObject piece)
        {
            var result = new List<ShrinkSuggestion>();
            if (avatarRoot == null || piece == null) return result;

            string pieceName = Normalise(piece.name);
            if (pieceName.Length < 3) return result;

            var own = new HashSet<SkinnedMeshRenderer>(piece.GetComponentsInChildren<SkinnedMeshRenderer>(true));

            foreach (var renderer in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer == null || own.Contains(renderer)) continue;
                var mesh = renderer.sharedMesh;
                if (mesh == null) continue;
                int count = mesh.blendShapeCount;
                for (int i = 0; i < count; i++)
                {
                    string shapeName = mesh.GetBlendShapeName(i);
                    string normalised = StripPrefix(Normalise(shapeName));
                    if (normalised.Length < 3) continue;
                    if (!normalised.Contains(pieceName) && !pieceName.Contains(normalised)) continue;
                    result.Add(new ShrinkSuggestion { Renderer = renderer, Shape = shapeName, Piece = piece });
                }
            }

            result.Sort((a, b) =>
            {
                int byPath = string.CompareOrdinal(PathOf(avatarRoot, a.Renderer.transform), PathOf(avatarRoot, b.Renderer.transform));
                return byPath != 0 ? byPath : string.CompareOrdinal(a.Shape, b.Shape);
            });
            return result;
        }

        static string PathOf(Transform root, Transform t) => AnimationUtility.CalculateTransformPath(t, root);

        static string Normalise(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var chars = name.Where(char.IsLetterOrDigit).Select(c => char.ToLowerInvariant(c)).ToArray();
            return new string(chars);
        }

        static string StripPrefix(string normalised)
        {
            foreach (var prefix in Prefixes)
                if (normalised.StartsWith(prefix, StringComparison.Ordinal))
                    return normalised.Substring(prefix.Length);
            return normalised;
        }
    }
}
#endif
