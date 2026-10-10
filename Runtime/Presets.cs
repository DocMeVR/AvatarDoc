using System;

namespace DocMe.AvatarDoctor
{
    /// <summary>One control a preset switches: the toggle, radial or group member it targets, and the state to set it to.</summary>
    [Serializable]
    public class PresetState
    {
        public string entryId = "";
        /// <summary>Toggles and group members: on or off. Ignored for a radial, which always writes value.</summary>
        public bool on = true;
        /// <summary>Radials only: the position to set.</summary>
        public float value = 1f;
    }

    /// <summary>A parameter a preset sets by name, for a parameter AvatarDoctor does not own.</summary>
    [Serializable]
    public class PresetParameter
    {
        public string name = "";
        public float value = 1f;
    }
}
