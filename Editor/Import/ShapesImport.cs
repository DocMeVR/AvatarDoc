#if DOCME_NDMF && DOCME_VRCSDK3A
#if DOCME_MA
using System.Collections.Generic;
using nadena.dev.modular_avatar.core;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Reads Modular Avatar's Blendshape Sync and the Shape Changers that sit on an outfit piece of their own,
    /// and offers them as AvatarDoc Shapes components on the same piece: one candidate per piece.
    /// </summary>
    public static class ShapesImport
    {
        public static List<ImportCandidate> Scan(Transform avatarRoot)
        {
            var result = new List<ImportCandidate>();
            if (avatarRoot == null) return result;
            var byPiece = new Dictionary<GameObject, ImportCandidate>();

            foreach (var sync in avatarRoot.GetComponentsInChildren<ModularAvatarBlendshapeSync>(true))
            {
                if (sync == null || sync.GetComponent<SkinnedMeshRenderer>() == null) continue;
                var candidate = For(result, byPiece, sync.gameObject);
                candidate.OriginComponents.Add(sync);
                foreach (var binding in sync.Bindings)
                {
                    var source = MeshOf(binding.ReferenceMesh, sync);
                    if (source == null) continue;
                    candidate.ShapesDraft.Follow.Add(new ShapeFollow
                    {
                        sourceMesh = source,
                        sourceShape = binding.Blendshape ?? "",
                        localShape = binding.LocalBlendshape ?? "",
                    });
                }
            }

            foreach (var changer in avatarRoot.GetComponentsInChildren<ModularAvatarShapeChanger>(true))
            {
                if (changer == null) continue;
                // A changer under a Modular Avatar menu item is that item's own effect: the menu importer reads
                // it into a toggle, and reading it here as well would convert it twice.
                if (changer.GetComponentInParent<ModularAvatarMenuItem>(true) != null) continue;
                var candidate = For(result, byPiece, changer.gameObject);
                candidate.OriginComponents.Add(changer);
                foreach (var shape in changer.Shapes)
                {
                    if (shape == null) continue;
                    if (shape.ChangeType == ShapeChangeType.Delete)
                    {
                        candidate.Supported = false;
                        candidate.Reason = string.Format(L.Text("ad.import.reason.shapes_delete"), shape.ShapeName);
                        continue;
                    }
                    var mesh = MeshOf(shape.Object, changer);
                    if (mesh == null) continue;
                    candidate.ShapesDraft.WhileOn.Add(new ShapeWhileOn { mesh = mesh, shape = shape.ShapeName ?? "", value = shape.Value });
                }
            }

            // A piece whose components carried nothing AvatarDoc Shapes can hold is not worth a row of its own;
            // one that is refused keeps its place, so the creator reads why.
            result.RemoveAll(c => c.Supported && c.ShapesDraft.RowCount == 0);
            return result;
        }

        static SkinnedMeshRenderer MeshOf(AvatarObjectReference reference, Component container)
        {
            var target = reference != null ? reference.Get(container) : null;
            return target != null ? target.GetComponent<SkinnedMeshRenderer>() : null;
        }

        static ImportCandidate For(List<ImportCandidate> result, Dictionary<GameObject, ImportCandidate> byPiece, GameObject piece)
        {
            if (byPiece.TryGetValue(piece, out var existing)) return existing;
            var candidate = new ImportCandidate
            {
                Source = ImportSource.Shapes,
                ShapesDraft = new ShapesDraft { Piece = piece },
            };
            byPiece[piece] = candidate;
            result.Add(candidate);
            return candidate;
        }
    }
}
#endif
#endif
