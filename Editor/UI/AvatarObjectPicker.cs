#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
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
        /// <summary>Swapped in tests so opening the picker never shows a real IMGUI window.</summary>
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

        /// <summary>Opens the searchable dropdown at the button's world rect, scoped to this avatar.</summary>
        public static void Show(Rect buttonRect, Transform avatarRoot, PickKind kind, Action<UnityEngine.Object> onPick)
        {
            if (ShowOverride != null) { ShowOverride(buttonRect, avatarRoot, kind, onPick); return; }
            new PickerDropdown(avatarRoot, kind, onPick).Show(buttonRect);
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
            // leave the default action running and open the scene wide selector beside this one.
            selector.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || !CanOpen(selector, avatarRoot)) return;
                Claim(evt);
                OpenOnce(stamp, selector, avatarRoot, kind, onPick);
            }, TrickleDown.TrickleDown);
            selector.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0 || !CanOpen(selector, avatarRoot)) return;
                Claim(evt);
                OpenOnce(stamp, selector, avatarRoot, kind, onPick);
            }, TrickleDown.TrickleDown);
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter && evt.keyCode != KeyCode.Space) return;
                if (!CanOpen(selector, avatarRoot)) return;
                Claim(evt);
                OpenOnce(stamp, selector, avatarRoot, kind, onPick);
            }, TrickleDown.TrickleDown);
        }

        /// <summary>When one selector last opened its list. One of these per selector, held by that selector's own callbacks.</summary>
        sealed class OpenStamp
        {
            public double At = -1d;
        }

        static void Claim(EventBase evt)
        {
            evt.StopImmediatePropagation();
            evt.PreventDefault();
        }

        /// <summary>One press arrives as a pointer event and as a mouse event: only the first of the two opens this selector's list.</summary>
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
            var root = avatarRoot();
            Show(selector.worldBound, root, kind, onPick);
            return true;
        }

        sealed class PickerDropdown : AdvancedDropdown
        {
            sealed class Item : AdvancedDropdownItem
            {
                public readonly UnityEngine.Object Target;
                public Item(string label, UnityEngine.Object target) : base(label) { Target = target; }
            }

            readonly Transform avatarRoot;
            readonly PickKind kind;
            readonly Action<UnityEngine.Object> onPick;

            public PickerDropdown(Transform avatarRoot, PickKind kind, Action<UnityEngine.Object> onPick) : base(new AdvancedDropdownState())
            {
                this.avatarRoot = avatarRoot;
                this.kind = kind;
                this.onPick = onPick;
                minimumSize = new Vector2(280f, 320f);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem(L.Text("ad.ui.pick.avatar_object"));
                var choices = Choices(avatarRoot, kind);
                AdvancedDropdownItem bones = null;
                for (int i = 0; i < choices.Count; i++)
                {
                    var choice = choices[i];
                    var item = new Item(LabelOf(choice), choice.Target);
                    if (choice.IsBone)
                    {
                        if (bones == null)
                        {
                            bones = new AdvancedDropdownItem(L.Text("ad.ui.pick.bones"));
                            root.AddChild(bones);
                        }
                        bones.AddChild(item);
                    }
                    else root.AddChild(item);
                }
                if (choices.Count == 0) root.AddChild(new AdvancedDropdownItem(L.Text("ad.ui.pick.none")) { enabled = false });
                return root;
            }

            static string LabelOf(PickChoice choice)
            {
                var label = choice.Name;
                if (choice.Inactive) label += " " + L.Text("ad.ui.pick.inactive");
                if (!string.IsNullOrEmpty(choice.ParentPath)) label += "  (" + choice.ParentPath + ")";
                return label;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (item is Item picked) onPick(picked.Target);
            }
        }
    }
}
#endif
