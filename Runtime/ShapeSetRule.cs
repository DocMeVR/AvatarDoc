using System;
using UnityEngine;

namespace DocMe.AvatarDoctor
{
    /// <summary>What a shape set rule holds the other shapes of its mesh at. Zero is 0, so every rule saved before the choice existed loads as Zero.</summary>
    public enum ShapeSetValue { Zero = 0, Default = 1 }

    /// <summary>
    /// A mesh a toggle or a trigger holds the rest of while it is on: every shape of the mesh the entry does not name
    /// is held at 0 or at its default (its weight in the scene at upload), in the same layer the entry's pinned rows
    /// use; while it is off, nothing of the rule touches the mesh. The shapes the avatar descriptor blinks with are left
    /// free unless holdBlink is on, and the ones it speaks with unless holdVisemes is on. At upload only the held shapes
    /// something else moves are written; the others already stay where the rule would hold them.
    /// </summary>
    [Serializable]
    public sealed class ShapeSetRule : ISerializationCallbackReceiver
    {
        public SkinnedMeshRenderer mesh;
        /// <summary>The rule's on and off switch. The name is from the first version of the rule, which could only hold at 0; read it through <see cref="On"/>.</summary>
        public bool zeroOthers = true;
        public ShapeSetValue value = ShapeSetValue.Zero;
        /// <summary>Whether the descriptor's eyelid shapes on this mesh are held too, which pauses VRChat's blinking while the rule holds. Off by default.</summary>
        public bool holdBlink;
        /// <summary>Whether the descriptor's viseme and jaw flap shapes on this mesh are held too, which pauses lip sync while the rule holds. Off by default.</summary>
        public bool holdVisemes;

        /// <summary>
        /// The one blink and viseme switch rules were saved with before holdBlink and holdVisemes. A rule loaded with it
        /// on gets both switches on and the field cleared, so it is never saved on again; it stays serialized only so
        /// scenes and prefabs saved before the split still load their choice.
        /// </summary>
        [SerializeField, HideInInspector] bool holdTracked;

        public bool On => zeroOthers;

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (!holdTracked) return;
            holdBlink = true;
            holdVisemes = true;
            holdTracked = false;
        }
    }
}
