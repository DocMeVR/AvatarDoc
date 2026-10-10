using System;
using System.Collections.Generic;
using UnityEngine;

namespace DocMe.AvatarDoctor
{
    /// <summary>
    /// An animator controller whose layers join the avatar's FX controller at build time. The controller asset and
    /// its clips are never changed: the build works on clones.
    /// </summary>
    [Serializable]
    public sealed class AnimatorMerge
    {
        public string id = "";
        public RuntimeAnimatorController controller;
        /// <summary>Empty: clip paths start at the avatar root. Otherwise every path is prefixed with the path from the avatar root to this object.</summary>
        public Transform pathRoot;
        /// <summary>States take the avatar's Write Defaults decision. A layer that is one state with a direct blend tree keeps Write Defaults on either way.</summary>
        public bool matchWriteDefaults = true;
        /// <summary>Layer names left out of the merge.</summary>
        public List<string> skipLayers = new List<string>();
        public EntryPlatform platform;

        public static AnimatorMerge Create(RuntimeAnimatorController controller)
        {
            return new AnimatorMerge
            {
                id = Guid.NewGuid().ToString("N"),
                controller = controller,
            };
        }
    }
}
