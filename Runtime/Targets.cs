using System;
using UnityEngine;

namespace DocMe.AvatarDoctor
{
    public enum ShapeEffectMode { Fixed = 0, Follow = 1, Pin = 2 }

    [Serializable]
    public class ObjectTarget
    {
        public GameObject target;
        /// <summary>True: shown while the control is on. False: hidden while on.</summary>
        public bool activeWhenOn = true;
    }

    [Serializable]
    public class BlendShapeTarget
    {
        public SkinnedMeshRenderer renderer;
        public string shape = "";
        /// <summary>Weight while a toggle is on, or the fixed weight of a trigger effect.</summary>
        [Range(0f, 100f)] public float weight = 100f;
        /// <summary>Radial range.</summary>
        [Range(0f, 100f)] public float minWeight = 0f;
        [Range(0f, 100f)] public float maxWeight = 100f;
        /// <summary>Blendshape trigger effect: fixed weight, or follow the source curve times multiplier.</summary>
        public ShapeEffectMode mode;
        public float multiplier = 1f;
        /// <summary>Toggles only: the Off state writes minWeight instead of the weight the scene already holds. Never combined with Pin.</summary>
        public bool explicitOff;
    }

    [Serializable]
    public class MaterialTarget
    {
        public Renderer renderer;
        public int slot;
        public Material material;
    }

    public enum PropertyValueType { Float = 0, Color = 1, Vector = 2 }

    /// <summary>Animates a shader property across the whole renderer (every slot), unlike a MaterialTarget which swaps one slot.</summary>
    [Serializable]
    public class MaterialPropertyTarget
    {
        public Renderer renderer;
        public string property = "";
        public PropertyValueType type;
        /// <summary>Value while a toggle is on, and the upper end of a radial.</summary>
        public float value = 1f;
        [ColorUsage(true, true)] public Color color = Color.white;
        public Vector4 vector;
        /// <summary>Radial range: the lower end. A toggle uses it only while explicitOff is set.</summary>
        public float minValue;
        [ColorUsage(true, true)] public Color minColor = Color.black;
        public Vector4 minVector;
        /// <summary>Toggles only: the Off state writes the lower end instead of the value the materials already hold.</summary>
        public bool explicitOff;
    }

    /// <summary>A creator's own animation clip, merged with the generated values on build.</summary>
    [Serializable]
    public class AnimationTarget
    {
        public AnimationClip clip;
        /// <summary>Empty: the clip's paths are relative to the avatar root. Otherwise every path is prefixed with the path from the avatar root to this object.</summary>
        public Transform pathRoot;
    }

    /// <summary>While the control is on, or the trigger is met, target follows the follows transform instead of hanging where it belongs.</summary>
    [Serializable]
    public class FollowTarget
    {
        public GameObject target;
        public Transform follows;
        /// <summary>True: the source keeps the pose the object has relative to follows as it is posed in the scene right now. False (a new row's default): the object snaps onto follows' own origin.</summary>
        public bool keepOffset;
    }
}
