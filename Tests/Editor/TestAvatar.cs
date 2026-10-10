using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>A small avatar for build tests. Everything is in memory; Dispose destroys the root.</summary>
    public sealed class TestAvatar : IDisposable
    {
        public GameObject Root;
        public VRCAvatarDescriptor Descriptor;
        public SkinnedMeshRenderer Body;
        public Mesh Mesh;
        public GameObject Hat;
        public GameObject Glasses;
        public AnimatorController Fx;
        public VRCExpressionParameters ExpressionParameters;
        public VRCExpressionsMenu ExpressionsMenu;
        public VRCExpressionsMenu OldSubmenu;
        public Material MaterialA;
        public Material MaterialB;
        public Transform LeftHandBone;
        public Transform RightHandBone;
        public Transform HeadBone;
        public Transform LeftEyeBone;
        public Transform RightEyeBone;
        public Avatar HumanoidAvatar;

        public static TestAvatar Create(bool writeDefaultsOff = true)
        {
            var t = new TestAvatar();
            t.Root = new GameObject("TestAvatar");
            t.Descriptor = t.Root.AddComponent<VRCAvatarDescriptor>();

            var body = new GameObject("Body");
            body.transform.SetParent(t.Root.transform);
            t.Body = body.AddComponent<SkinnedMeshRenderer>();
            t.Mesh = QuadWithShapes("Blush", "Eye_Big");
            t.Body.sharedMesh = t.Mesh;
            t.MaterialA = new Material(Shader.Find("Standard")) { name = "A" };
            t.MaterialB = new Material(Shader.Find("Standard")) { name = "B" };
            t.Body.sharedMaterials = new[] { t.MaterialA, t.MaterialA };

            t.Hat = new GameObject("Hat");
            t.Hat.transform.SetParent(t.Root.transform);
            t.Hat.AddComponent<MeshFilter>();
            t.Hat.AddComponent<MeshRenderer>();
            t.Glasses = new GameObject("Glasses");
            t.Glasses.transform.SetParent(t.Root.transform);

            t.Fx = new AnimatorController { name = "FX" };
            t.Fx.AddLayer("Base");
            var idle = t.Fx.layers[0].stateMachine.AddState("Idle");
            idle.writeDefaultValues = !writeDefaultsOff;

            var types = new[]
            {
                VRCAvatarDescriptor.AnimLayerType.Base, VRCAvatarDescriptor.AnimLayerType.Additive,
                VRCAvatarDescriptor.AnimLayerType.Gesture, VRCAvatarDescriptor.AnimLayerType.Action, VRCAvatarDescriptor.AnimLayerType.FX,
            };
            t.Descriptor.customizeAnimationLayers = true;
            t.Descriptor.baseAnimationLayers = types.Select(type => new VRCAvatarDescriptor.CustomAnimLayer
            {
                type = type,
                isDefault = type != VRCAvatarDescriptor.AnimLayerType.FX,
                isEnabled = true,
                animatorController = type == VRCAvatarDescriptor.AnimLayerType.FX ? t.Fx : null,
            }).ToArray();
            t.Descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();

            t.ExpressionParameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            t.ExpressionParameters.name = "Params";
            t.ExpressionParameters.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "Existing", valueType = VRCExpressionParameters.ValueType.Bool, saved = true, networkSynced = true },
            };
            t.OldSubmenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            t.OldSubmenu.name = "Old";
            t.OldSubmenu.controls = new List<VRCExpressionsMenu.Control> { ToggleControl("OldToggle", "Existing") };
            t.ExpressionsMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            t.ExpressionsMenu.name = "Root";
            t.ExpressionsMenu.controls = new List<VRCExpressionsMenu.Control>
            {
                new VRCExpressionsMenu.Control { name = "Old", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = t.OldSubmenu, parameter = new VRCExpressionsMenu.Control.Parameter { name = "" } },
                ToggleControl("RootToggle", "Existing"),
            };
            t.Descriptor.customExpressions = true;
            t.Descriptor.expressionParameters = t.ExpressionParameters;
            t.Descriptor.expressionsMenu = t.ExpressionsMenu;
            return t;
        }

        public static VRCExpressionsMenu.Control ToggleControl(string name, string parameter)
        {
            return new VRCExpressionsMenu.Control { name = name, type = VRCExpressionsMenu.Control.ControlType.Toggle, parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter }, value = 1 };
        }

        public static Mesh QuadWithShapes(params string[] shapes)
        {
            var mesh = new Mesh { name = "Quad" };
            mesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateNormals();
            foreach (var shape in shapes)
            {
                var deltas = new[] { Vector3.zero, Vector3.zero, new Vector3(0, 0, 0.1f), new Vector3(0, 0, 0.1f) };
                mesh.AddBlendShapeFrame(shape, 100f, deltas, null, null);
            }
            return mesh;
        }

        public AvatarDoc AddMenu(GameObject on = null)
        {
            return (on ?? Root).AddComponent<AvatarDoc>();
        }

        /// <summary>
        /// Adds a minimal but valid humanoid rig under Root, with a real Animator and Avatar, so HumanoidHands can
        /// resolve LeftHandBone and RightHandBone. includeRightHand false leaves the right hand out of the human
        /// mapping, for a rig missing that bone. includeEyeBones true adds LeftEye and RightEye to the mapping,
        /// each a small child of Head, for tests of a start that needs the avatar's own eye bones.
        /// </summary>
        public void AddHumanoidRig(bool includeRightHand = true, bool includeEyeBones = false)
        {
            var animator = Root.AddComponent<Animator>();

            Transform Bone(string name, Transform parent, Vector3 localPosition)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = localPosition;
                return go.transform;
            }

            var hips = Bone("Hips", Root.transform, new Vector3(0f, 1f, 0f));
            var spine = Bone("Spine", hips, new Vector3(0f, 0.1f, 0f));
            var chest = Bone("Chest", spine, new Vector3(0f, 0.1f, 0f));
            var neck = Bone("Neck", chest, new Vector3(0f, 0.1f, 0f));
            var head = Bone("Head", neck, new Vector3(0f, 0.1f, 0f));
            var leftUpperLeg = Bone("LeftUpperLeg", hips, new Vector3(-0.1f, -0.05f, 0f));
            var leftLowerLeg = Bone("LeftLowerLeg", leftUpperLeg, new Vector3(0f, -0.45f, 0f));
            var leftFoot = Bone("LeftFoot", leftLowerLeg, new Vector3(0f, -0.45f, 0f));
            var rightUpperLeg = Bone("RightUpperLeg", hips, new Vector3(0.1f, -0.05f, 0f));
            var rightLowerLeg = Bone("RightLowerLeg", rightUpperLeg, new Vector3(0f, -0.45f, 0f));
            var rightFoot = Bone("RightFoot", rightLowerLeg, new Vector3(0f, -0.45f, 0f));
            var leftUpperArm = Bone("LeftUpperArm", chest, new Vector3(-0.15f, 0.05f, 0f));
            var leftLowerArm = Bone("LeftLowerArm", leftUpperArm, new Vector3(-0.3f, 0f, 0f));
            var leftHand = Bone("LeftHand", leftLowerArm, new Vector3(-0.3f, 0f, 0f));
            var rightUpperArm = Bone("RightUpperArm", chest, new Vector3(0.15f, 0.05f, 0f));
            var rightLowerArm = Bone("RightLowerArm", rightUpperArm, new Vector3(0.3f, 0f, 0f));
            var rightHand = Bone("RightHand", rightLowerArm, new Vector3(0.3f, 0f, 0f));
            var leftEye = includeEyeBones ? Bone("LeftEye", head, new Vector3(-0.03f, 0.02f, 0.05f)) : null;
            var rightEye = includeEyeBones ? Bone("RightEye", head, new Vector3(0.03f, 0.02f, 0.05f)) : null;

            var skeleton = Root.GetComponentsInChildren<Transform>(true)
                .Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale })
                .ToArray();

            var human = new List<HumanBone>();
            void Map(HumanBodyBones bone, Transform t) => human.Add(new HumanBone { humanName = HumanTrait.BoneName[(int)bone], boneName = t.name, limit = new HumanLimit { useDefaultValues = true } });
            Map(HumanBodyBones.Hips, hips);
            Map(HumanBodyBones.Spine, spine);
            Map(HumanBodyBones.Chest, chest);
            Map(HumanBodyBones.Neck, neck);
            Map(HumanBodyBones.Head, head);
            Map(HumanBodyBones.LeftUpperLeg, leftUpperLeg);
            Map(HumanBodyBones.LeftLowerLeg, leftLowerLeg);
            Map(HumanBodyBones.LeftFoot, leftFoot);
            Map(HumanBodyBones.RightUpperLeg, rightUpperLeg);
            Map(HumanBodyBones.RightLowerLeg, rightLowerLeg);
            Map(HumanBodyBones.RightFoot, rightFoot);
            Map(HumanBodyBones.LeftUpperArm, leftUpperArm);
            Map(HumanBodyBones.LeftLowerArm, leftLowerArm);
            Map(HumanBodyBones.LeftHand, leftHand);
            Map(HumanBodyBones.RightUpperArm, rightUpperArm);
            Map(HumanBodyBones.RightLowerArm, rightLowerArm);
            if (includeRightHand) Map(HumanBodyBones.RightHand, rightHand);
            if (includeEyeBones)
            {
                Map(HumanBodyBones.LeftEye, leftEye);
                Map(HumanBodyBones.RightEye, rightEye);
            }

            var description = new HumanDescription
            {
                human = human.ToArray(),
                skeleton = skeleton,
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0f,
            };
            HumanoidAvatar = AvatarBuilder.BuildHumanAvatar(Root, description);
            animator.avatar = HumanoidAvatar;
            LeftHandBone = leftHand;
            RightHandBone = rightHand;
            HeadBone = head;
            LeftEyeBone = leftEye;
            RightEyeBone = rightEye;
        }

        /// <summary>Adds shapes to Body's own mesh that are not already there, for a start's shape guess to find. Existing shapes and their frames are left alone.</summary>
        public void AddBodyShapes(params string[] shapes)
        {
            foreach (var shape in shapes)
            {
                if (Mesh.GetBlendShapeIndex(shape) >= 0) continue;
                var deltas = new[] { Vector3.zero, Vector3.zero, new Vector3(0, 0, 0.1f), new Vector3(0, 0, 0.1f) };
                Mesh.AddBlendShapeFrame(shape, 100f, deltas, null, null);
            }
        }

        public AnimatorState AddFxClip(string stateName, AnimationClip clip)
        {
            var state = Fx.layers[0].stateMachine.AddState(stateName);
            state.motion = clip;
            state.writeDefaultValues = false;
            return state;
        }

        public AnimatorState AddFxBlendTree(string stateName, AnimationClip child)
        {
            var tree = new BlendTree { name = stateName + " tree", blendType = BlendTreeType.Simple1D, blendParameter = "Existing", useAutomaticThresholds = false };
            tree.AddChild(child, 0f);
            tree.AddChild(child, 1f);
            var state = Fx.layers[0].stateMachine.AddState(stateName);
            state.motion = tree;
            state.writeDefaultValues = false;
            return state;
        }

        public static AnimationClip ShapeClip(string name, string renderPath, string shape, float startValue, float endValue)
        {
            var clip = new AnimationClip { name = name };
            var curve = new AnimationCurve(new Keyframe(0f, startValue), new Keyframe(1f, endValue));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(renderPath, typeof(SkinnedMeshRenderer), "blendShape." + shape), curve);
            return clip;
        }

        public void Dispose()
        {
            if (Root != null) UnityEngine.Object.DestroyImmediate(Root);
            Discard(Mesh);
            Discard(MaterialA);
            Discard(MaterialB);
            Discard(Fx);
            Discard(ExpressionParameters);
            Discard(ExpressionsMenu);
            Discard(OldSubmenu);
            Discard(HumanoidAvatar);
        }

        /// <summary>Drops an in-memory object the test created. Anything that ended up on disk is left to the test that saved it.</summary>
        static void Discard(UnityEngine.Object obj)
        {
            if (obj == null || AssetDatabase.Contains(obj)) return;
            UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
