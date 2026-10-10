using System;

namespace DocMe.AvatarDoctor
{
    /// <summary>The value type a planned or declared parameter carries.</summary>
    public enum PlannedType { Bool = 0, Int = 1, Float = 2 }

    /// <summary>
    /// A parameter the component declares on its own, for a merged animator or an installed menu asset that needs
    /// the name in the expression parameters without a menu entry of AvatarDoc's own behind it.
    /// </summary>
    [Serializable]
    public sealed class ParameterDeclaration
    {
        public string name = "";
        public PlannedType type;
        public float defaultValue;
        public bool saved = true;
        public bool synced = true;

        /// <summary>Synced bits this declaration costs: none when it is not synced, one for a Bool, eight otherwise.</summary>
        public int Bits => !synced ? 0 : type == PlannedType.Bool ? 1 : 8;

        public static ParameterDeclaration Create(string name, PlannedType type)
        {
            return new ParameterDeclaration { name = (name ?? "").Trim(), type = type };
        }
    }
}
