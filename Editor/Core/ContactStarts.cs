#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Builds the three ready made Follow distance contact starts (Headpat, Eye touch, Nose boop): correctly
    /// placed triggers a creator only has to nudge, each with a best guess blend shape row on the avatar's own
    /// face mesh. Everything here is placed from the avatar itself (its ViewPosition, its humanoid bones, its
    /// root's own forward and up), never from a fixed number. Callable without a view, so a test drives it
    /// directly the same way OutfitSetBuilder is driven.
    /// </summary>
    public static class ContactStarts
    {
        public enum StartKind { Headpat, EyeTouch, NoseBoop }

        static readonly string[] EyesClosedNames = { "Blink", "Blink_Both", "eye_close", "EyeClose", "Eyes_Closed", "eyes_closed", "まばたき", "目閉じ", "blink" };
        static readonly string[] LeftEyeNames = { "Blink_L", "BlinkLeft", "Blink_Left", "eye_close_L", "EyeClose_L", "ウィンクL", "ウィンク" };
        static readonly string[] RightEyeNames = { "Blink_R", "BlinkRight", "Blink_Right", "eye_close_R", "EyeClose_R", "ウィンクR", "ウィンク右" };
        static readonly string[] MouthOpenNames = { "vrc.v_aa", "MouthOpen", "Mouth_Open", "mouth_open", "あ", "A", "aa" };

        /// <summary>
        /// Creates one start's trigger or triggers on menu, in one Undo group, and returns them: empty when the
        /// write is refused (menu is null or locked, the same silent refusal every other add gives) or when the
        /// avatar has no Head bone, in which case errorKey names the L.Text key of the reason shown to the creator.
        /// </summary>
        public static List<MenuEntry> Apply(AvatarDoc menu, StartKind kind, out string errorKey)
        {
            errorKey = null;
            var created = new List<MenuEntry>();
            if (EntryEditing.Refuses(menu)) return created;

            var root = AvatarPlanCache.RootOf(menu);
            var animator = HumanAnimator(root);
            var head = animator != null ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head == null)
            {
                errorKey = "ad.error.contact_start_no_head";
                return created;
            }

            var descriptor = root.GetComponent<VRCAvatarDescriptor>();
            var viewPosition = descriptor != null ? descriptor.ViewPosition : Vector3.zero;
            float scale = viewPosition.y > 0f ? viewPosition.y / 1.6f : 1f;
            var up = root.up;
            var forward = root.forward;
            var viewWorld = root.TransformPoint(viewPosition);
            var face = FaceMesh(descriptor, root);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            string groupName = "Add " + StartName(kind);
            Undo.SetCurrentGroupName(groupName);
            Undo.RecordObject(menu, groupName);
            try
            {
                switch (kind)
                {
                    case StartKind.Headpat:
                        created.Add(Headpat(menu, head, viewWorld, up, forward, scale, face));
                        break;
                    case StartKind.EyeTouch:
                        EyeTouch(menu, animator, head, viewWorld, forward, scale, face, created);
                        break;
                    case StartKind.NoseBoop:
                        created.Add(NoseBoop(menu, head, viewWorld, up, forward, scale, face));
                        break;
                }
            }
            finally
            {
                EntryEditing.MarkChanged(menu);
                Undo.CollapseUndoOperations(group);
            }
            return created;
        }

        static string StartName(StartKind kind)
        {
            switch (kind)
            {
                case StartKind.Headpat: return "Headpat";
                case StartKind.EyeTouch: return "Eye touch";
                default: return "Nose boop";
            }
        }

        static MenuEntry Headpat(AvatarDoc menu, Transform head, Vector3 viewWorld, Vector3 up, Vector3 forward, float scale, SkinnedMeshRenderer face)
        {
            var centre = viewWorld + up * (0.12f * scale) - forward * (0.02f * scale);
            var entry = CreateTrigger(menu, "Headpat", head, centre, 0.14f * scale, new[] { "Hand", "Finger" }, 0.7f);
            AddGuessedShape(entry, face, EyesClosedNames);
            return entry;
        }

        static void EyeTouch(AvatarDoc menu, Animator animator, Transform head, Vector3 viewWorld, Vector3 forward, float scale, SkinnedMeshRenderer face, List<MenuEntry> created)
        {
            var leftEye = animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftEye) : null;
            var rightEye = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightEye) : null;
            if (leftEye != null && rightEye != null)
            {
                var left = CreateTrigger(menu, "Eye touch L", leftEye, leftEye.position + forward * (0.02f * scale), 0.045f * scale, new[] { "Finger" }, 0.8f);
                AddGuessedShape(left, face, LeftEyeNames, EyesClosedNames);
                created.Add(left);
                var right = CreateTrigger(menu, "Eye touch R", rightEye, rightEye.position + forward * (0.02f * scale), 0.045f * scale, new[] { "Finger" }, 0.8f);
                AddGuessedShape(right, face, RightEyeNames, EyesClosedNames);
                created.Add(right);
            }
            else
            {
                var centre = viewWorld + forward * (0.03f * scale);
                var entry = CreateTrigger(menu, "Eye touch", head, centre, 0.07f * scale, new[] { "Finger" }, 0.8f);
                AddGuessedShape(entry, face, EyesClosedNames);
                created.Add(entry);
            }
        }

        static MenuEntry NoseBoop(AvatarDoc menu, Transform head, Vector3 viewWorld, Vector3 up, Vector3 forward, float scale, SkinnedMeshRenderer face)
        {
            var centre = viewWorld + forward * (0.07f * scale) - up * (0.035f * scale);
            var entry = CreateTrigger(menu, "Nose boop", head, centre, 0.05f * scale, new[] { "Finger" }, 0.8f);
            AddGuessedShape(entry, face, MouthOpenNames);
            return entry;
        }

        /// <summary>
        /// Creates one Follow distance contact trigger anchored on anchor, with a fresh unique name, at the given
        /// world centre and radius. Converts the world placement into contactOffset and contactRadius the same
        /// way ContactSceneHandle does: offset in the anchor's own local space, radius divided by the scale a
        /// contact on the anchor gets (TouchProbe.Scale, its largest axis).
        /// </summary>
        static MenuEntry CreateTrigger(AvatarDoc menu, string baseName, Transform anchor, Vector3 worldCentre, float worldRadius, string[] tags, float fullAt)
        {
            var name = UniqueEntryName(menu, baseName);
            var entry = menu.AddEntry(EntryKind.Trigger, name, "");
            entry.source = TriggerSource.Contact;
            entry.touchMode = TouchMode.FollowDistance;
            entry.contactAnchor = anchor;
            float anchorScale = Mathf.Max(0.0001f, TouchProbe.Scale(anchor));
            entry.contactOffset = anchor.InverseTransformPoint(worldCentre);
            entry.contactRadius = Mathf.Clamp(worldRadius / anchorScale, 0.005f, 1f);
            entry.contactTags = new List<string>(tags);
            entry.contactAllowSelf = true;
            entry.contactAllowOthers = true;
            entry.contactFullAt = fullAt;
            return entry;
        }

        /// <summary>Adds one blend shape row on face for the first primary name it carries, checked without case, else the first fallback name once primary has none. The row is added with no shape (the panel's own "pick a shape" state) when neither list matches.</summary>
        static void AddGuessedShape(MenuEntry entry, SkinnedMeshRenderer face, string[] primary, string[] fallback = null)
        {
            if (face == null) return;
            var shape = FindShape(face, primary) ?? (fallback != null ? FindShape(face, fallback) : null);
            entry.blendShapes.Add(new BlendShapeTarget { renderer = face, shape = shape ?? "" });
        }

        /// <summary>The first candidate that names a shape on mesh, compared without case, in the candidates' own order. Null when none of them exist there.</summary>
        static string FindShape(SkinnedMeshRenderer mesh, IEnumerable<string> candidates)
        {
            if (mesh == null || mesh.sharedMesh == null) return null;
            var names = new List<string>(mesh.sharedMesh.blendShapeCount);
            for (int i = 0; i < mesh.sharedMesh.blendShapeCount; i++) names.Add(mesh.sharedMesh.GetBlendShapeName(i));
            foreach (var candidate in candidates)
            {
                var match = names.FirstOrDefault(n => string.Equals(n, candidate, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            return null;
        }

        /// <summary>The mesh a start's guessed shape lands on: the descriptor's own viseme mesh, else its eyelid mesh, else the skinned mesh with the most blend shapes under the avatar. Null when the avatar has no skinned mesh at all.</summary>
        static SkinnedMeshRenderer FaceMesh(VRCAvatarDescriptor descriptor, Transform root)
        {
            if (descriptor != null && descriptor.VisemeSkinnedMesh != null) return descriptor.VisemeSkinnedMesh;
            if (descriptor != null && descriptor.customEyeLookSettings.eyelidsSkinnedMesh != null) return descriptor.customEyeLookSettings.eyelidsSkinnedMesh;
            if (root == null) return null;
            return root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r != null && r.sharedMesh != null)
                .OrderByDescending(r => r.sharedMesh.blendShapeCount)
                .FirstOrDefault();
        }

        /// <summary>A start's own name, else the same with " 2", " 3" and so on: nothing any AvatarDoc component under the avatar already names.</summary>
        static string UniqueEntryName(AvatarDoc menu, string baseName)
        {
            var root = AvatarPlanCache.RootOf(menu);
            IEnumerable<AvatarDoc> components = root != null ? root.GetComponentsInChildren<AvatarDoc>(true) : new[] { menu };
            var taken = new HashSet<string>();
            foreach (var component in components)
                foreach (var entry in component.entries)
                    if (!string.IsNullOrWhiteSpace(entry.name)) taken.Add(entry.PlainName);
            var candidate = baseName;
            for (int i = 2; taken.Contains(candidate); i++) candidate = baseName + " " + i;
            return candidate;
        }

        /// <summary>The avatar's own humanoid Animator, or null when it has none or is not set to Humanoid. The one place every bone lookup here asks, so a rig without one always reads as having no Head bone either.</summary>
        static Animator HumanAnimator(Transform root)
        {
            if (root == null) return null;
            var animator = root.GetComponentInChildren<Animator>(true);
            return animator != null && animator.isHuman ? animator : null;
        }
    }
}
#endif
