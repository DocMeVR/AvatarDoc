#if DOCME_VRCSDK3A
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Resolves a grabbable's hand bones from the avatar's own humanoid rig.</summary>
    public static class HumanoidHands
    {
        /// <summary>The left or right hand bone of the avatar's humanoid rig, or null when the avatar has no humanoid Animator or that bone is missing. Pure: reads the rig, changes nothing.</summary>
        public static Transform Hand(Transform avatarRoot, bool left)
        {
            if (avatarRoot == null) return null;
            var animator = avatarRoot.GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.isHuman) return null;
            return animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        }
    }
}
#endif
