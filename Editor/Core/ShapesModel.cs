#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>A follow row resolved against the avatar: both meshes exist and both shapes are really on them.</summary>
    public sealed class ShapesFollowRow
    {
        public ShapeFollow Row;
        public SkinnedMeshRenderer Source;
        public string SourceShape;
        public string LocalShape;
    }

    /// <summary>A while-on row resolved against the avatar: the mesh is part of it and the shape is really on it.</summary>
    public sealed class ShapesWhileOnRow
    {
        public ShapeWhileOn Row;
        public SkinnedMeshRenderer Mesh;
        public string Shape;
        public float Value;
    }

    /// <summary>One AvatarDoc Shapes component with its rows resolved.</summary>
    public sealed class ShapesPiece
    {
        public AvatarDocShapes Component;
        public GameObject Object;
        /// <summary>The piece's own skinned mesh, the one follow rows write to. Null when the object carries none.</summary>
        public SkinnedMeshRenderer LocalMesh;
        /// <summary>Left out on the platform the editor is building for.</summary>
        public bool Excluded;
        public List<ShapesFollowRow> Follows = new List<ShapesFollowRow>();
        public List<ShapesWhileOnRow> WhileOn = new List<ShapesWhileOnRow>();

        public string Name => Component != null ? Component.gameObject.name : "";
    }

    /// <summary>Every AvatarDoc Shapes component of one avatar, resolved, with what is wrong with the rows.</summary>
    public sealed class ShapesPlan
    {
        public Transform AvatarRoot;
        public List<ShapesPiece> Pieces = new List<ShapesPiece>();
        public List<ModelMessage> Messages = new List<ModelMessage>();

        public IEnumerable<ShapesPiece> Included => Pieces.Where(p => !p.Excluded);
    }

    /// <summary>
    /// Reads the AvatarDoc Shapes components of an avatar the same way for the inspector and for the build: which
    /// rows really resolve, what is wrong with the rest, and which pieces fight over one shape. Pure: nothing is
    /// written, no clip and no scene value is touched.
    /// </summary>
    public static class ShapesModel
    {
        /// <summary>Every enabled AvatarDoc Shapes component under this root, in hierarchy order.</summary>
        public static List<AvatarDocShapes> Components(Transform avatarRoot)
        {
            if (avatarRoot == null) return new List<AvatarDocShapes>();
            return avatarRoot.GetComponentsInChildren<AvatarDocShapes>(true).Where(c => c != null && c.enabled).ToList();
        }

        public static ShapesPlan Plan(Transform avatarRoot)
        {
            var plan = new ShapesPlan { AvatarRoot = avatarRoot };
            if (avatarRoot == null) return plan;
            foreach (var component in Components(avatarRoot))
                plan.Pieces.Add(Read(plan, avatarRoot, component));
            Conflicts(plan);
            return plan;
        }

        /// <summary>The plan for one component alone, for an inspector on a piece that sits outside an avatar.</summary>
        public static ShapesPlan PlanFor(AvatarDocShapes component)
        {
            var root = component != null ? AvatarPlanCache.RootOf(component) : null;
            if (root != null) return Plan(root);
            var plan = new ShapesPlan();
            if (component != null) plan.Pieces.Add(Read(plan, null, component));
            return plan;
        }

        static ShapesPiece Read(ShapesPlan plan, Transform avatarRoot, AvatarDocShapes component)
        {
            var piece = new ShapesPiece
            {
                Component = component,
                Object = component.gameObject,
                LocalMesh = component.GetComponent<SkinnedMeshRenderer>(),
                Excluded = !Platform.Included(component.platform),
            };
            string name = piece.Name;

            if (piece.LocalMesh == null && component.follow.Any(r => r != null))
                plan.Messages.Add(ModelMessage.Error("ad.error.shapes_no_mesh", name, component));

            foreach (var row in component.follow)
            {
                if (row == null) continue;
                if (piece.LocalMesh == null) continue;
                string source = (row.sourceShape ?? "").Trim();
                string local = row.EffectiveLocalShape;
                if (row.sourceMesh == null || MenuModel.IsOutsideAvatar(avatarRoot, row.sourceMesh.transform))
                {
                    plan.Messages.Add(ModelMessage.Error("ad.error.shapes_outside_avatar", name, source, component));
                    continue;
                }
                if (!MenuModel.ShapeExists(row.sourceMesh, source))
                {
                    plan.Messages.Add(ModelMessage.Warning("ad.warn.shapes_missing_shape", name, row.sourceMesh.name, source, component));
                    continue;
                }
                if (!MenuModel.ShapeExists(piece.LocalMesh, local))
                {
                    plan.Messages.Add(ModelMessage.Warning("ad.warn.shapes_missing_shape", name, piece.LocalMesh.name, local, component));
                    continue;
                }
                piece.Follows.Add(new ShapesFollowRow { Row = row, Source = row.sourceMesh, SourceShape = source, LocalShape = local });
            }

            foreach (var row in component.whileOn)
            {
                if (row == null) continue;
                string shape = (row.shape ?? "").Trim();
                if (row.mesh == null || MenuModel.IsOutsideAvatar(avatarRoot, row.mesh.transform))
                {
                    plan.Messages.Add(ModelMessage.Error("ad.error.shapes_outside_avatar", name, shape, component));
                    continue;
                }
                if (!MenuModel.ShapeExists(row.mesh, shape))
                {
                    plan.Messages.Add(ModelMessage.Warning("ad.warn.shapes_missing_shape", name, row.mesh.name, shape, component));
                    continue;
                }
                piece.WhileOn.Add(new ShapesWhileOnRow { Row = row, Mesh = row.mesh, Shape = shape, Value = Mathf.Clamp(row.value, 0f, 100f) });
            }
            return piece;
        }

        /// <summary>
        /// Two pieces that hold one shape at different values while they are on. Both can be worn at once as far as
        /// the model can tell, so the pair is reported; the build lets the larger value win.
        /// </summary>
        static void Conflicts(ShapesPlan plan)
        {
            var seen = new Dictionary<(SkinnedMeshRenderer, string), ShapesWhileOnRow>();
            var owner = new Dictionary<(SkinnedMeshRenderer, string), ShapesPiece>();
            var said = new HashSet<(SkinnedMeshRenderer, string)>();
            foreach (var piece in plan.Included)
                foreach (var row in piece.WhileOn)
                {
                    var key = (row.Mesh, row.Shape);
                    if (!seen.TryGetValue(key, out var first))
                    {
                        seen[key] = row;
                        owner[key] = piece;
                        continue;
                    }
                    if (owner[key] == piece || Mathf.Approximately(first.Value, row.Value) || !said.Add(key)) continue;
                    plan.Messages.Add(ModelMessage.Warning("ad.warn.shapes_conflict", owner[key].Name, piece.Name, row.Shape, owner[key].Component));
                }
        }

        /// <summary>Whether this object is on in the scene: itself and everything above it up to the avatar root.</summary>
        public static bool ActiveInAvatar(GameObject target, Transform avatarRoot)
        {
            if (target == null) return false;
            for (var cursor = target.transform; cursor != null; cursor = cursor.parent)
            {
                if (!cursor.gameObject.activeSelf) return false;
                if (cursor == avatarRoot) break;
            }
            return true;
        }
    }
}
#endif
