using System;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Data holding an object reference inside a managed reference, for the test that the copy walks those too.</summary>
    public sealed class CopyWalkProbe : ScriptableObject
    {
        [Serializable]
        public sealed class Holder
        {
            public GameObject target;
        }

        [SerializeReference] public Holder held = new Holder();
    }
}
