#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>What kind of object a field on the avatar accepts.</summary>
    public enum PickKind { GameObject, Renderer, SkinnedMeshWithShapes, PhysBone, Transform }

    /// <summary>One row the avatar picker offers.</summary>
    public sealed class PickChoice
    {
        public UnityEngine.Object Target;
        public string Name;
        public string ParentPath;
        public bool Inactive;
        public bool IsBone;
    }

    /// <summary>
    /// Replaces Unity's scene wide object selector for fields that name something on the avatar: Unity's own
    /// picker lists every object of every open scene and cannot be limited to a hierarchy. Only the small selector
    /// button of an ObjectField is taken over; the field otherwise stays an ordinary ObjectField for display, drag
    /// and drop from the Hierarchy and ping on click.
    /// </summary>
    public static class AvatarObjectPicker
    {
        /// <summary>Swapped in tests so opening the picker never opens a real menu.</summary>
        public static Action<Rect, Transform, PickKind, Action<UnityEngine.Object>> ShowOverride;

        /// <summary>
        /// Every object of the avatar this kind of field accepts, hierarchy order, humanoid bones first for
        /// Transform. Pure: no window, no GUI, safe to call from a test. One GetComponentsInChildren walk for the
        /// kind's own content plus one small walk to find nested avatars, no per item allocation beyond the choice.
        /// </summary>
        public static List<PickChoice> Choices(Transform avatarRoot, PickKind kind)
        {
            var result = new List<PickChoice>();
            if (avatarRoot == null) return result;
            var nestedRoots = NestedAvatarRoots(avatarRoot);

            switch (kind)
            {
                case PickKind.Renderer:
                {
                    var renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
                    for (int i = 0; i < renderers.Length; i++)
                        AddChoice(result, renderers[i], renderers[i].transform, avatarRoot, nestedRoots, isBone: false);
                    break;
                }
                case PickKind.SkinnedMeshWithShapes:
                {
                    var renderers = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        var mesh = renderers[i].sharedMesh;
                        if (mesh == null || mesh.blendShapeCount == 0) continue;
                        AddChoice(result, renderers[i], renderers[i].transform, avatarRoot, nestedRoots, isBone: false);
                    }
                    break;
                }
                case PickKind.PhysBone:
                {
                    var physBones = avatarRoot.GetComponentsInChildren<VRCPhysBone>(true);
                    for (int i = 0; i < physBones.Length; i++)
                        AddChoice(result, physBones[i], physBones[i].transform, avatarRoot, nestedRoots, isBone: false);
                    break;
                }
                case PickKind.Transform:
                {
                    var bones = HumanoidBonesOf(avatarRoot);
                    var transforms = avatarRoot.GetComponentsInChildren<Transform>(true);
                    var boneChoices = new List<PickChoice>();
                    var restChoices = new List<PickChoice>();
                    for (int i = 0; i < transforms.Length; i++)
                    {
                        var t = transforms[i];
                        if (IsNested(t, nestedRoots)) continue;
                        bool isBone = bones != null && bones.Contains(t);
                        var choice = MakeChoice(t, t, avatarRoot, isBone);
                        (isBone ? boneChoices : restChoices).Add(choice);
                    }
                    result.AddRange(boneChoices);
                    result.AddRange(restChoices);
                    break;
                }
                default: // PickKind.GameObject
                {
                    var transforms = avatarRoot.GetComponentsInChildren<Transform>(true);
                    for (int i = 0; i < transforms.Length; i++)
                        AddChoice(result, transforms[i].gameObject, transforms[i], avatarRoot, nestedRoots, isBone: false);
                    break;
                }
            }
            return result;
        }

        static void AddChoice(List<PickChoice> result, UnityEngine.Object target, Transform transform, Transform avatarRoot, List<Transform> nestedRoots, bool isBone)
        {
            if (IsNested(transform, nestedRoots)) return;
            result.Add(MakeChoice(target, transform, avatarRoot, isBone));
        }

        static PickChoice MakeChoice(UnityEngine.Object target, Transform transform, Transform avatarRoot, bool isBone)
        {
            return new PickChoice
            {
                Target = target,
                Name = transform.name,
                ParentPath = ParentPathOf(transform, avatarRoot),
                Inactive = !transform.gameObject.activeInHierarchy,
                IsBone = isBone,
            };
        }

        static string ParentPathOf(Transform t, Transform avatarRoot)
        {
            if (t == avatarRoot) return "";
            var segments = new List<string>();
            for (var cursor = t.parent; cursor != null && cursor != avatarRoot; cursor = cursor.parent)
                segments.Insert(0, cursor.name);
            return string.Join("/", segments);
        }

        /// <summary>The avatar's own humanoid bones (from its Animator, when it has one), or null when it has none: the caller then offers no bones group at all rather than an empty one.</summary>
        static HashSet<Transform> HumanoidBonesOf(Transform avatarRoot)
        {
            var animator = avatarRoot.GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.isHuman) return null;
            var bones = new HashSet<Transform>();
            for (HumanBodyBones bone = 0; bone < HumanBodyBones.LastBone; bone++)
            {
                var t = animator.GetBoneTransform(bone);
                if (t != null) bones.Add(t);
            }
            return bones;
        }

        /// <summary>The roots of every avatar below this one: a VRCAvatarDescriptor other than this avatar's own is a nested avatar, and its objects are left out entirely.</summary>
        static List<Transform> NestedAvatarRoots(Transform avatarRoot)
        {
            var result = new List<Transform>();
            var descriptors = avatarRoot.GetComponentsInChildren<VRCAvatarDescriptor>(true);
            for (int i = 0; i < descriptors.Length; i++)
                if (descriptors[i].transform != avatarRoot) result.Add(descriptors[i].transform);
            return result;
        }

        static bool IsNested(Transform t, List<Transform> nestedRoots)
        {
            for (int i = 0; i < nestedRoots.Count; i++)
                if (t == nestedRoots[i] || t.IsChildOf(nestedRoots[i])) return true;
            return false;
        }

        /// <summary>Whether a candidate (from a drop, most often) is a valid target of this kind, on this avatar and not inside a nested one. True for null, since clearing a field is always allowed.</summary>
        public static bool Accepts(Transform avatarRoot, UnityEngine.Object candidate, PickKind kind)
        {
            if (candidate == null) return true;
            var transform = TransformOf(candidate, kind);
            if (transform == null) return false;
            if (avatarRoot == null) return true;
            if (!transform.IsChildOf(avatarRoot)) return false;
            return !IsNested(transform, NestedAvatarRoots(avatarRoot));
        }

        static Transform TransformOf(UnityEngine.Object candidate, PickKind kind)
        {
            switch (kind)
            {
                case PickKind.Renderer:
                    return candidate is Renderer r ? r.transform : null;
                case PickKind.SkinnedMeshWithShapes:
                    return candidate is SkinnedMeshRenderer smr && smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0 ? smr.transform : null;
                case PickKind.PhysBone:
                    return candidate is VRCPhysBone pb ? pb.transform : null;
                case PickKind.Transform:
                    return candidate as Transform;
                default:
                    return candidate is GameObject go ? go.transform : null;
            }
        }

        /// <summary>Opens the picker, an AdMenu with a search field, under a button's world rect, scoped to this avatar. For callers outside AvatarDoc, which hand over only the rectangle: the button is looked for with AdMenu.FindTarget.</summary>
        public static void Show(Rect buttonRect, Transform avatarRoot, PickKind kind, Action<UnityEngine.Object> onPick)
        {
            if (ShowOverride != null) { ShowOverride(buttonRect, avatarRoot, kind, onPick); return; }
            Open(buttonRect, AdMenu.FindTarget(buttonRect), avatarRoot, kind, onPick);
        }

        /// <summary>Opens the picker on the control that asked for it, scoped to this avatar.</summary>
        internal static void Show(VisualElement anchor, Transform avatarRoot, PickKind kind, Action<UnityEngine.Object> onPick)
        {
            if (ShowOverride != null) { ShowOverride(anchor.worldBound, avatarRoot, kind, onPick); return; }
            Open(anchor.worldBound, anchor, avatarRoot, kind, onPick);
        }

        /// <summary>Opens the picker under buttonRect in the window that hosts target. Null when target is not on a panel.</summary>
        public static AdMenu Open(Rect buttonRect, VisualElement target, Transform avatarRoot, PickKind kind, Action<UnityEngine.Object> onPick) =>
            AdMenu.Open(buttonRect, target, Items(avatarRoot, kind, onPick), search: true);

        /// <summary>
        /// The Choices as menu items: the object's name (marked when inactive) with its parent path as the dim text
        /// beside it, found by its name or its path under the avatar. Humanoid bones go in one Bones group, where the
        /// first of them would stand.
        /// </summary>
        public static List<AdMenu.Item> Items(Transform avatarRoot, PickKind kind, Action<UnityEngine.Object> onPick)
        {
            var items = new List<AdMenu.Item>();
            AdMenu.Item bones = null;
            foreach (var choice in Choices(avatarRoot, kind))
            {
                var target = choice.Target;
                var item = new AdMenu.Item
                {
                    Text = choice.Inactive ? choice.Name + " " + L.Text("ad.ui.pick.inactive") : choice.Name,
                    SearchText = choice.ParentPath.Length > 0 ? choice.ParentPath + "/" + choice.Name : choice.Name,
                    Note = choice.ParentPath,
                    Action = () => onPick(target),
                };
                if (choice.IsBone)
                {
                    if (bones == null)
                    {
                        bones = new AdMenu.Item { Text = L.Text("ad.ui.pick.bones"), Children = new List<AdMenu.Item>() };
                        items.Add(bones);
                    }
                    bones.Children.Add(item);
                }
                else items.Add(item);
            }
            if (items.Count == 0) items.Add(new AdMenu.Item { Text = L.Text("ad.ui.pick.none"), Disabled = true });
            return items;
        }

        /// <summary>
        /// Takes over an ObjectField's selector button: a click or a keyboard activation opens the avatar picker
        /// instead of Unity's own, in the TrickleDown phase with the event stopped so Unity's selector never sees
        /// it. Left alone (no avatar root, or the field disabled by a lock) so Unity's own selector still works.
        /// </summary>
        public static void TakeOverSelector(ObjectField field, Func<Transform> avatarRoot, PickKind kind, Action<UnityEngine.Object> onPick)
        {
            var selector = field.Q<VisualElement>(className: ObjectField.selectorUssClassName);
            if (selector == null) return;
            selector.userData = kind;
            // One stamp per selector: a click on a second field right after this one must still open its list.
            var stamp = new OpenStamp();
            // Unity opens its own selector from the default action of the mouse down on this element and from the
            // key down on the focused field, so both are claimed outright: stopping the propagation alone would
            // leave the default action running and open the scene wide selector beside this one. The avatar picker
            // itself opens on the release, as a button does: a menu opened by the press can lie under the pointer
            // when the window is short.
            selector.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || !CanOpen(selector, avatarRoot)) return;
                Claim(evt);
                stamp.Pressed = true;
            }, TrickleDown.TrickleDown);
            selector.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0 || !CanOpen(selector, avatarRoot)) return;
                Claim(evt);
            }, TrickleDown.TrickleDown);
            selector.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button != 0 || !stamp.Pressed) return;
                stamp.Pressed = false;
                if (!CanOpen(selector, avatarRoot)) return;
                Claim(evt);
                OpenOnce(stamp, selector, avatarRoot, kind, onPick);
            }, TrickleDown.TrickleDown);
            selector.RegisterCallback<PointerLeaveEvent>(_ => stamp.Pressed = false);
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter && evt.keyCode != KeyCode.Space) return;
                if (!CanOpen(selector, avatarRoot)) return;
                Claim(evt);
                OpenOnce(stamp, selector, avatarRoot, kind, onPick);
            }, TrickleDown.TrickleDown);
        }

        /// <summary>When one selector last opened its list, and whether a press on it waits for its release. One of these per selector, held by that selector's own callbacks.</summary>
        sealed class OpenStamp
        {
            public double At = -1d;
            public bool Pressed;
        }

        static void Claim(EventBase evt)
        {
            evt.StopImmediatePropagation();
            evt.PreventDefault();
        }

        /// <summary>Opens this selector's list unless it opened within the last quarter second, as a held key repeats.</summary>
        static void OpenOnce(OpenStamp stamp, VisualElement selector, Func<Transform> avatarRoot, PickKind kind, Action<UnityEngine.Object> onPick)
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - stamp.At < 0.25d) return;
            if (TryOpen(selector, avatarRoot, kind, onPick)) stamp.At = now;
        }

        /// <summary>False when a lock disables the selector or no avatar root can be found: Unity's own selector is then left to run.</summary>
        public static bool CanOpen(VisualElement selector, Func<Transform> avatarRoot)
        {
            if (selector == null || !selector.enabledInHierarchy) return false;
            return avatarRoot != null && avatarRoot() != null;
        }

        /// <summary>
        /// What a click or a keyboard activation of the selector does. False (Unity's own selector is left to run)
        /// when the selector is disabled by a lock or no avatar root can be found. Public so a test can drive the
        /// same decision without dispatching a real event, which a detached field cannot receive.
        /// </summary>
        public static bool TryOpen(VisualElement selector, Func<Transform> avatarRoot, PickKind kind, Action<UnityEngine.Object> onPick)
        {
            if (!CanOpen(selector, avatarRoot)) return false;
            Show(selector, avatarRoot(), kind, onPick);
            return true;
        }
    }
}
#endif
