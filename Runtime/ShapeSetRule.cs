using System;
using UnityEngine;

namespace DocMe.AvatarDoctor
{
    /// <summary>
    /// A captured mesh on a toggle. While the toggle is on, every shape of the mesh the entry does not name is
    /// forced to 0, held in the entry's pin layer; while it is off, nothing of the rule touches the mesh.
    /// </summary>
    [Serializable]
    public sealed class ShapeSetRule
    {
        public SkinnedMeshRenderer mesh;
        public bool zeroOthers = true;
    }
}
