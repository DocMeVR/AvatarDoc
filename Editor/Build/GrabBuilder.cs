#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Builds the whole "hold this in a hand" arrangement a grabbable entry describes: a PhysBone that reports
    /// held, one contact receiver per allowed hand, and the follow claims those two conditions drive, one per
    /// hand, through the very same FollowBuilder-made constraint an ordinary follow row would use. Everything is
    /// made on the build clone; the creator's scene is never touched.
    /// </summary>
    public static class GrabBuilder
    {
        /// <summary>The floor of the receiver radius, in world space, so a small ring is not unreachable. The bounds of the object decide the rest, so a long sword does not answer across the room.</summary>
        const float MinRadius = 0.03f;
        const float MaxRadius = 0.5f;
        /// <summary>The floor of the endpoint length a childless object's chain is given, so PhysBone always has one real bone to report a grab on.</summary>
        const float MinBoneLength = 0.05f;

        /// <summary>touch is what TouchBuilder.Describe planned: a trigger on the grabbable's own parameterless PhysBone already chose the name that PhysBone will carry.</summary>
        public static void Build(BuildContext context, MenuTree tree, FollowPlan follows, TouchBuilder.TouchParameters touch = null)
        {
            // Shared across every grabbable this build makes, so two entries of the same name never end up
            // reading or writing the same PhysBone or contact parameter.
            var used = new HashSet<string>();
            var noExisting = new Dictionary<string, ExistingParameter>();

            foreach (var control in tree.Controls)
            {
                if (control.Excluded) continue;
                var entry = control.Entry;
                if (entry.kind != EntryKind.Grabbable || !entry.HasEffects) continue;
                if (!IsUsable(tree, follows, entry, control.Component)) continue;

                bool left = entry.grabHands != GrabHands.Right;
                bool right = entry.grabHands != GrabHands.Left;
                var leftHand = left ? HumanoidHands.Hand(tree.AvatarRoot, true) : null;
                var rightHand = right ? HumanoidHands.Hand(tree.AvatarRoot, false) : null;
                // The model already refuses a missing hand as an error, which blocks the whole build; this is a
                // second line of defence, so stale or hand-edited data still never builds a claim with no hand.
                bool anyHand = (left && leftHand != null) || (right && rightHand != null);
                if (!anyHand) continue;

                var prefix = control.Component != null ? control.Component.parameterPrefix : "AD";
                var pb = ResolvePhysBone(tree, entry, prefix, noExisting, used, touch, out string grabbedParameter, out List<ModelMessage> physBoneMessages);
                tree.Messages.AddRange(physBoneMessages);
                if (pb == null) continue;
                if (entry.grabMenuToggle) follows.GrabPhysBones[entry] = pb;

                // Collected rather than ignored as each one is made: the endpoint above already decided whether
                // this chain needs a bone of its own from the object's children as they stood before any of this
                // ran, and that decision must not see a receiver that does not exist yet.
                var receivers = new List<Transform>();
                if (left && leftHand != null) AddHandClaim(tree, follows, entry, pb.parameter + " L", "HandL", leftHand, grabbedParameter, GrabHands.Left, receivers);
                if (right && rightHand != null) AddHandClaim(tree, follows, entry, pb.parameter + " R", "HandR", rightHand, grabbedParameter, GrabHands.Right, receivers);

                // Otherwise each receiver becomes a bone of the chain it reports on: PhysBone simulates every
                // transform under its root that is not ignored, so a receiver would move with the chain's own
                // physics and could drop the very contact it exists to hold, right while the prop is held.
                if (receivers.Count > 0)
                {
                    if (pb.ignoreTransforms == null) pb.ignoreTransforms = new List<Transform>();
                    pb.ignoreTransforms.AddRange(receivers);
                }
            }
        }

        /// <summary>
        /// The same structural checks the model already reports as errors, read again at build time so a build
        /// never acts on a row the model rejected. The one thing the model cannot have seen is a constraint, or a
        /// containing PhysBone chain, another NDMF plugin puts on the clone between the model's own validation and
        /// this call.
        /// </summary>
        static bool IsUsable(MenuTree tree, FollowPlan follows, MenuEntry entry, AvatarDoc component)
        {
            if (entry.grabObject == null) return false;
            if (MenuModel.IsOutsideAvatar(tree.AvatarRoot, entry.grabObject.transform)) return false;
            if (!follows.ByTarget.ContainsKey(entry.grabObject) && MenuModel.HasExistingConstraint(entry.grabObject))
            {
                tree.Messages.Add(ModelMessage.Error("ad.error.follow_existing_constraint", entry.PlainName, entry.grabObject.name, component).For(entry.id));
                return false;
            }
            var containing = MenuModel.FindContainingPhysBone(tree.AvatarRoot, entry.grabObject.transform);
            if (containing != null)
            {
                tree.Messages.Add(ModelMessage.Error("ad.error.grab_inside_physbone", entry.PlainName, containing.name, component).For(entry.id));
                return false;
            }
            return true;
        }

        /// <summary>
        /// The PhysBone this grabbable holds: the object's own, reused with an info message (and a warning if it
        /// looks unsuitable), or a new one with no movement and no drag of its own, grabbing allowed for its
        /// wearer and, when grabAnyone is on, for anyone. A childless object gets an endpoint, since a PhysBone
        /// with no child and no endpoint has no bone at all to report a grab on.
        /// </summary>
        static VRCPhysBone ResolvePhysBone(MenuTree tree, MenuEntry entry, string prefix, IReadOnlyDictionary<string, ExistingParameter> existing, HashSet<string> used, TouchBuilder.TouchParameters touch, out string grabbedParameter, out List<ModelMessage> messages)
        {
            messages = new List<ModelMessage>();
            var pb = entry.grabObject.GetComponent<VRCPhysBone>();
            if (pb != null)
            {
                messages.Add(ModelMessage.Info("ad.info.grab_uses_existing_physbone", entry.PlainName).For(entry.id));
                if (pb.allowGrabbing == VRCPhysBoneBase.AdvancedBool.False || (pb.rootTransform != null && pb.rootTransform != entry.grabObject.transform))
                    messages.Add(ModelMessage.Warning("ad.warn.grab_existing_physbone_unsuitable", entry.PlainName).For(entry.id));
                if (string.IsNullOrEmpty(pb.parameter))
                {
                    // A trigger on this same PhysBone already reads the name Describe planned for it; giving the
                    // PhysBone any other name would leave that trigger watching a parameter VRChat never sets.
                    var planned = touch?.Created.FirstOrDefault(info => info.Source != null && info.Source.kind == WhenConditionKind.PhysBone
                        && info.Source.physBone == pb && !string.IsNullOrEmpty(info.PhysBoneParameter));
                    pb.parameter = planned != null ? planned.PhysBoneParameter : TouchBuilder.UniqueTouchName(prefix + "/Grab/" + entry.PlainName, existing, used);
                }
                grabbedParameter = pb.parameter + "_IsGrabbed";
                return pb;
            }

            pb = entry.grabObject.AddComponent<VRCPhysBone>();
            pb.integrationType = VRCPhysBoneBase.IntegrationType.Simplified;
            pb.pull = 0f;
            pb.spring = 0f;
            pb.stiffness = 1f;
            pb.gravity = 0f;
            pb.gravityFalloff = 0f;
            // Reduces the effect of movement on the bone to nothing: the object holds still on its own.
            pb.immobile = 1f;
            // The SDK's own default (0.5) moves the chain toward the hand on grab; with pull and spring at 0
            // nothing ever brings it back, so a multi part prop stays deformed for the rest of the session.
            pb.grabMovement = 0f;
            pb.allowPosing = VRCPhysBoneBase.AdvancedBool.False;
            if (entry.grabAnyone)
            {
                pb.allowGrabbing = VRCPhysBoneBase.AdvancedBool.True;
            }
            else
            {
                pb.allowGrabbing = VRCPhysBoneBase.AdvancedBool.Other;
                pb.grabFilter = new VRCPhysBoneBase.PermissionFilter(true, false, DynamicsUsageFlags.Everything);
            }
            // A single transform with no child has no bone below its root; an endpoint gives the chain one, along
            // the object's own bounds so it is not an arbitrary length, with a floor so a tiny object still gets
            // a usable bone. endpointPosition is a LOCAL Y offset that VRChat multiplies by the bone's own lossy Y
            // scale alone, while BoundsLength and the floor are both world-space measurements, so the world length
            // has to be divided by that one axis, not the largest of the three the receiver radius uses for its
            // uniform sphere.
            if (pb.transform.childCount == 0)
            {
                float worldLength = Mathf.Max(BoundsLength(entry.grabObject), MinBoneLength);
                pb.endpointPosition = new Vector3(0f, WorldToLocalLength(worldLength, Mathf.Abs(pb.transform.lossyScale.y)), 0f);
            }
            pb.parameter = TouchBuilder.UniqueTouchName(prefix + "/Grab/" + entry.PlainName, existing, used);
            grabbedParameter = pb.parameter + "_IsGrabbed";
            return pb;
        }

        /// <summary>One hand's contact receiver and its follow claim on the shared constraint. Refuses, and reports, once the constraint is already at VRChat's own source ceiling, instead of leaving a receiver behind for a hand that would never work. The receiver's transform is appended to receivers, for the caller to keep out of the grab object's own PhysBone chain.</summary>
        static void AddHandClaim(MenuTree tree, FollowPlan follows, MenuEntry entry, string handParameter, string tag, Transform hand, string grabbedParameter, GrabHands whichHand, List<Transform> receivers)
        {
            var info = FollowBuilder.GetOrCreate(follows, entry.grabObject);
            if (info.Claims.Count >= FollowPlan.MaxClaims)
            {
                var names = new List<string>();
                foreach (var claim in info.Claims) names.Add("\"" + claim.Entry.PlainName + "\"");
                names.Add("\"" + entry.PlainName + "\"");
                tree.Messages.Add(ModelMessage.Error("ad.error.follow_too_many_sources", entry.grabObject.name,
                    (info.Claims.Count + 1).ToString(), FollowPlan.MaxClaims.ToString(), string.Join(", ", names)).For(entry.id));
                return;
            }

            receivers.Add(CreateContact(entry, tag, handParameter));
            int index = info.Claims.Count + 1;
            FollowBuilder.AddSource(info.Constraint, entry.grabObject, hand, entry.grabKeepOffset, index);
            info.Claims.Add(new FollowClaim
            {
                Entry = entry,
                SourceIndex = index,
                Grab = new GrabClaimInfo
                {
                    Hand = whichHand,
                    GrabbedParameter = grabbedParameter,
                    HandParameter = handParameter,
                    BlendSeconds = entry.grabReturnSeconds,
                },
            });
        }

        /// <summary>Creates one hand's contact receiver as a child of the grab object and returns its transform, for the caller to keep out of that object's own PhysBone chain.</summary>
        static Transform CreateContact(MenuEntry entry, string tag, string parameter)
        {
            var go = new GameObject("AD Grab " + tag + " " + entry.PlainName);
            go.transform.SetParent(entry.grabObject.transform, false);
            go.transform.localPosition = BoundsCenter(entry.grabObject);
            go.transform.localRotation = Quaternion.identity;

            var receiver = go.AddComponent<VRCContactReceiver>();
            receiver.shapeType = ContactBase.ShapeType.Sphere;
            receiver.radius = ReceiverRadius(entry);
            receiver.position = Vector3.zero;
            receiver.rotation = Quaternion.identity;
            receiver.allowSelf = true;
            // The only sources the constraint ever gets are the wearer's own hand bones, so another player's
            // hand satisfying this condition could never move the object to them anyway; it would only let a
            // stranger's nearby hand snap the object into the wearer's own hand instead.
            receiver.allowOthers = false;
            receiver.localOnly = false;
            receiver.collisionTags = new List<string> { tag };
            receiver.receiverType = ContactReceiver.ReceiverType.Constant;
            receiver.parameter = parameter;
            return go.transform;
        }

        /// <summary>The combined world space bounds of every renderer under the object that has bounds to give, and whether there was one at all.</summary>
        static Bounds CombinedBounds(GameObject target, out bool hasRenderer)
        {
            hasRenderer = false;
            var combined = new Bounds(target.transform.position, Vector3.zero);
            foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                if (!TryWorldBounds(renderer, out var bounds)) continue;
                if (hasRenderer) combined.Encapsulate(bounds);
                else combined = bounds;
                hasRenderer = true;
            }
            return combined;
        }

        /// <summary>
        /// A renderer's world space bounds. A mesh renderer that is not active in the hierarchy (a prop another
        /// toggle shows, or a hidden part of one, never woken on the build clone) is measured from its mesh's local
        /// bounds through its transform, the way Unity places an active one, so the answer does not depend on what
        /// Unity keeps for a renderer outside the active scene. A hidden mesh renderer with no mesh is left out.
        /// </summary>
        static bool TryWorldBounds(Renderer renderer, out Bounds world)
        {
            world = default;
            if (renderer == null) return false;
            if (renderer.gameObject.activeInHierarchy)
            {
                world = renderer.bounds;
                return true;
            }
            switch (renderer)
            {
                case SkinnedMeshRenderer skinned:
                    // A skinned mesh's local bounds sit in its root bone's space, or its own when it has none.
                    if (skinned.sharedMesh == null) return false;
                    world = ToWorld(skinned.localBounds, (skinned.rootBone != null ? skinned.rootBone : skinned.transform).localToWorldMatrix);
                    return true;
                case MeshRenderer _:
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) return false;
                    world = ToWorld(filter.sharedMesh.bounds, renderer.transform.localToWorldMatrix);
                    return true;
                default:
                    // Particles, lines, trails and sprites have no mesh to measure here, so Unity's own bounds stand.
                    world = renderer.bounds;
                    return true;
            }
        }

        /// <summary>The world space box around a local box's eight corners.</summary>
        static Bounds ToWorld(Bounds local, Matrix4x4 localToWorld)
        {
            var world = new Bounds(localToWorld.MultiplyPoint3x4(local.center), Vector3.zero);
            var e = local.extents;
            for (int corner = 0; corner < 8; corner++)
            {
                var offset = new Vector3((corner & 1) == 0 ? -e.x : e.x, (corner & 2) == 0 ? -e.y : e.y, (corner & 4) == 0 ? -e.z : e.z);
                world.Encapsulate(localToWorld.MultiplyPoint3x4(local.center + offset));
            }
            return world;
        }

        /// <summary>The local-space centre of every renderer under the object, or the object's own origin when it has none.</summary>
        static Vector3 BoundsCenter(GameObject target)
        {
            var bounds = CombinedBounds(target, out bool hasRenderer);
            return hasRenderer ? target.transform.InverseTransformPoint(bounds.center) : Vector3.zero;
        }

        /// <summary>The half diagonal of the object's own combined bounds, in world space, or 0 with no renderer.</summary>
        static float BoundsLength(GameObject target)
        {
            var bounds = CombinedBounds(target, out bool hasRenderer);
            return hasRenderer ? bounds.extents.magnitude : 0f;
        }

        /// <summary>
        /// The receiver's reach: the half diagonal of the object's own combined bounds, clamped to a floor (so a
        /// small ring is not unreachable) and a ceiling (so a very large mesh does not answer from across the
        /// room), scaled by the entry's own Reach multiplier, then converted from the world space measurement
        /// bounds give into the local value VRChat itself multiplies by the receiver transform's lossy scale.
        /// Skipping that conversion is exactly why a scaled prop used to answer at the wrong distance.
        /// </summary>
        static float ReceiverRadius(MenuEntry entry)
        {
            var bounds = CombinedBounds(entry.grabObject, out bool hasRenderer);
            float worldRadius = hasRenderer ? bounds.extents.magnitude : MinRadius;
            worldRadius = Mathf.Clamp(worldRadius, MinRadius, MaxRadius) * Mathf.Clamp(entry.grabReach, 0.1f, 5f);
            // A sphere reaches the same distance on every axis, so the largest lossy scale component is the one
            // that would otherwise let a non-uniformly scaled object answer too far on its shorter axes.
            return WorldToLocalLength(worldRadius, MaxLossyScaleComponent(entry.grabObject.transform));
        }

        /// <summary>Converts a world-space length into the local value a field would need so VRChat's own multiplication by the given scale reproduces that world length. Shared by the receiver radius (the largest lossy scale component, since a sphere is uniform) and the endpoint length (the Y component alone, since endpointPosition is a local Y offset).</summary>
        static float WorldToLocalLength(float worldLength, float scale)
        {
            return scale > 0.0001f ? worldLength / scale : worldLength;
        }

        static float MaxLossyScaleComponent(Transform t)
        {
            var s = t.lossyScale;
            return Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
        }
    }
}
#endif
