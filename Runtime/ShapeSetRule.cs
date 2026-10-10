using System;
using UnityEngine;

namespace DocMe.AvatarDoctor
{
    /// <summary>What a shape set rule holds the other shapes of its mesh at. Zero is 0, so every rule saved before the choice existed loads as Zero.</summary>
    public enum ShapeSetValue { Zero = 0, Default = 1 }

    /// <summary>
    /// A mesh a toggle or a trigger holds the rest of while it is on: every shape of the mesh the entry does not name
    /// is held at 0 or at its default (its weight in the scene at upload), in the same layer the entry's pinned rows
    /// use; while it is off, nothing of the rule touches the mesh. The shapes the avatar descriptor blinks and speaks
    /// with are left free unless holdTracked is on.
    /// </summary>
    [Serializable]
    public sealed class ShapeSetRule
    {
        public SkinnedMeshRenderer mesh;
        /// <summary>The rule's on and off switch. The name is from the first version of the rule, which could only hold at 0; read it through <see cref="On"/>.</summary>
        public bool zeroOthers = true;
        public ShapeSetValue value = ShapeSetValue.Zero;
        /// <summary>Whether the descriptor's eyelid and viseme shapes on this mesh are held too. Off by default, so blinking and lip sync keep working while the rule holds.</summary>
        public bool holdTracked;

        public bool On => zeroOthers;
    }
}
