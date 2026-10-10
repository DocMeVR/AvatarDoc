using System;
using System.Collections.Generic;
using UnityEngine;

namespace DocMe.AvatarDoctor
{
    /// <summary>One shape of the piece's own mesh that copies a shape of another mesh of the avatar.</summary>
    [Serializable]
    public sealed class ShapeFollow
    {
        public SkinnedMeshRenderer sourceMesh;
        public string sourceShape = "";
        /// <summary>Empty means the shape of the same name on the piece's own mesh.</summary>
        public string localShape = "";

        /// <summary>The shape this row writes on the piece: its own name, or the source name when none is given.</summary>
        public string EffectiveLocalShape => string.IsNullOrWhiteSpace(localShape) ? (sourceShape ?? "").Trim() : localShape.Trim();
    }

    /// <summary>One shape of any mesh of the avatar that holds a value while the piece is on.</summary>
    [Serializable]
    public sealed class ShapeWhileOn
    {
        public SkinnedMeshRenderer mesh;
        public string shape = "";
        [Range(0f, 100f)] public float value = 100f;
    }

    /// <summary>
    /// Blend shape rules that ride along with one outfit piece: the piece's own shapes follow another mesh, and
    /// shapes anywhere on the avatar hold a value while the piece is on. Costs no parameter and no layer: the
    /// build adds the values to the clips that already switch the piece.
    /// </summary>
    [AddComponentMenu("AvatarDoc/AvatarDoc Shapes")]
    public sealed class AvatarDocShapes : MonoBehaviour
#if DOCME_VRCSDK_BASE
        , VRC.SDKBase.IEditorOnly
#endif
    {
        public List<ShapeFollow> follow = new List<ShapeFollow>();
        public List<ShapeWhileOn> whileOn = new List<ShapeWhileOn>();
        /// <summary>Which platforms this piece's rules build on. Inherit means both: nothing encloses this component.</summary>
        public EntryPlatform platform = EntryPlatform.Inherit;

        // Present so Unity shows the enable checkbox; a disabled component is skipped at build.
        void OnEnable()
        {
        }
    }
}
