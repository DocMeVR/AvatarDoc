using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DocMe.AvatarDoctor
{
    /// <summary>Changes how one existing or foreign menu control appears in the built menu, without touching the source asset.</summary>
    [Serializable]
    public sealed class MenuEdit
    {
        public string id = "";
        /// <summary>Plain names from the root down to the control: submenu names, then the control name.</summary>
        public List<string> source = new List<string>();
        /// <summary>Which match to take when several controls under the same parent share the name, 0 first.</summary>
        public int occurrence;
        public bool hide;
        /// <summary>Leave the control out of the build and, when nothing else needs them, its expression parameter and the FX layers it alone drives. Implies hide.</summary>
        public bool remove;
        /// <summary>New rich name. Empty keeps the original.</summary>
        public string name = "";
        /// <summary>New icon. Null keeps the original.</summary>
        public Texture2D icon;
        public bool move;
        public string targetPath = "";
        /// <summary>Index inside the target menu after the move. -1 appends, or keeps the place when not moving.</summary>
        public int position = -1;

        public static MenuEdit Create(IEnumerable<string> source, int occurrence = 0)
        {
            return new MenuEdit
            {
                id = Guid.NewGuid().ToString("N"),
                source = source?.ToList() ?? new List<string>(),
                occurrence = occurrence,
            };
        }

        public bool IsEmpty => !hide && !remove && string.IsNullOrEmpty(name) && icon == null && !move && position < 0;
    }
}
