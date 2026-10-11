#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDKBase;
using Object = UnityEngine.Object;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The avatar the docs pictures show: a humanoid with parts a creator recognises (Body, a Face with expression
    /// and VRChat shapes, a hat, a jacket, a hoodie, glasses and shoes), Standard materials named after their use, a
    /// hair PhysBone and a contact anchor on the head, and the descriptor's blink and visemes on the face. It is a
    /// TestAvatar renamed and extended, all in memory; Dispose destroys everything it made.
    /// </summary>
    public sealed class DocsAvatar : IDisposable
    {
        public static readonly string[] FaceShapes =
        {
            "Blush", "Smile", "Smile_Eyes", "Mouth_Smile_Wide", "Brow_Angry", "Eye_Wide", "Cheek_Puff", "Tongue_Out",
            "vrc.blink", "vrc.v_aa", "vrc.v_oh", "vrc.v_ih",
        };

        public static readonly string[] BodyShapes = { "Chest_Big", "Shrink_Feet" };

        public TestAvatar Test;
        public GameObject Root;
        public VRCAvatarDescriptor Descriptor;
        public SkinnedMeshRenderer Body;
        public SkinnedMeshRenderer Face;
        public GameObject Hat;
        public GameObject Jacket;
        public GameObject Hoodie;
        public GameObject Glasses;
        public GameObject Shoes;
        public Transform HeadContact;
        public VRCPhysBone HairBone;
        public Material Skin;
        public Material Cloth;
        public Material Metal;

        Mesh faceMesh;
        Mesh partMesh;

        public static DocsAvatar Create()
        {
            var test = TestAvatar.Create();
            var avatar = new DocsAvatar { Test = test, Root = test.Root, Descriptor = test.Descriptor, Body = test.Body };
            try
            {
                avatar.Build();
            }
            catch
            {
                avatar.Dispose();
                throw;
            }
            AvatarPlanCache.Invalidate();
            return avatar;
        }

        public AvatarDoc AddMenu(GameObject on = null) => Test.AddMenu(on);

        void Build()
        {
            Root.name = "Avatar";
            Skin = Test.MaterialA;
            Skin.name = "Skin";
            Cloth = Test.MaterialB;
            Cloth.name = "Cloth";
            Metal = new Material(Shader.Find("Standard")) { name = "Metal" };
            RenameExistingMenu();

            // The TestAvatar's body mesh carries its test shapes. Body gets one with body shapes, and Test.Mesh
            // points at it so the TestAvatar's own Dispose still destroys it.
            var testBody = Test.Mesh;
            Test.Mesh = TestAvatar.QuadWithShapes(BodyShapes);
            Test.Mesh.name = "Body";
            Body.sharedMesh = Test.Mesh;
            Object.DestroyImmediate(testBody);

            faceMesh = TestAvatar.QuadWithShapes(FaceShapes);
            faceMesh.name = "Face";
            Face = new GameObject("Face").AddComponent<SkinnedMeshRenderer>();
            Face.transform.SetParent(Root.transform, false);
            Face.sharedMesh = faceMesh;
            Face.sharedMaterials = new[] { Skin };

            partMesh = TestAvatar.QuadWithShapes();
            partMesh.name = "Part";
            Hat = Part(Test.Hat, "Hat", Cloth);
            Jacket = Part(null, "Jacket", Cloth);
            Hoodie = Part(null, "Hoodie", Cloth);
            Glasses = Part(Test.Glasses, "Glasses", Metal);
            Shoes = Part(null, "Shoes", Cloth);

            Test.AddHumanoidRig();
            var head = Test.HeadBone;
            HeadContact = new GameObject("Head Contact").transform;
            HeadContact.SetParent(head, false);
            HeadContact.localPosition = new Vector3(0f, 0.12f, 0f);
            var hair = new GameObject("Hair").transform;
            hair.SetParent(head, false);
            hair.localPosition = new Vector3(0f, 0.1f, -0.05f);
            var hairEnd = new GameObject("Hair_End").transform;
            hairEnd.SetParent(hair, false);
            hairEnd.localPosition = new Vector3(0f, -0.2f, -0.05f);
            HairBone = hair.gameObject.AddComponent<VRCPhysBone>();

            Descriptor.enableEyeLook = true;
            Descriptor.customEyeLookSettings.eyelidType = VRCAvatarDescriptor.EyelidType.Blendshapes;
            Descriptor.customEyeLookSettings.eyelidsSkinnedMesh = Face;
            Descriptor.customEyeLookSettings.eyelidsBlendshapes = new[] { faceMesh.GetBlendShapeIndex("vrc.blink"), -1, -1 };
            Descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape;
            Descriptor.VisemeSkinnedMesh = Face;
            var visemes = new string[(int)VRC_AvatarDescriptor.Viseme.Count];
            for (int i = 0; i < visemes.Length; i++) visemes[i] = "";
            visemes[(int)VRC_AvatarDescriptor.Viseme.aa] = "vrc.v_aa";
            visemes[(int)VRC_AvatarDescriptor.Viseme.ih] = "vrc.v_ih";
            visemes[(int)VRC_AvatarDescriptor.Viseme.oh] = "vrc.v_oh";
            Descriptor.VisemeBlendShapes = visemes;
        }

        /// <summary>The TestAvatar's own expressions menu with the names an avatar would have: an Emotes submenu with a Wave toggle, and a Sit toggle.</summary>
        void RenameExistingMenu()
        {
            Test.ExpressionParameters.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "Sit", valueType = VRCExpressionParameters.ValueType.Bool, saved = true, networkSynced = true },
                new VRCExpressionParameters.Parameter { name = "Wave", valueType = VRCExpressionParameters.ValueType.Bool, saved = false, networkSynced = true },
            };
            Test.OldSubmenu.name = "Emotes";
            Test.OldSubmenu.controls = new List<VRCExpressionsMenu.Control> { TestAvatar.ToggleControl("Wave", "Wave") };
            Test.ExpressionsMenu.controls[0].name = "Emotes";
            Test.ExpressionsMenu.controls[1] = TestAvatar.ToggleControl("Sit", "Sit");
        }

        GameObject Part(GameObject existing, string name, Material material)
        {
            var part = existing != null ? existing : new GameObject(name);
            part.transform.SetParent(Root.transform, false);
            if (!part.TryGetComponent(out MeshFilter filter)) filter = part.AddComponent<MeshFilter>();
            if (!part.TryGetComponent(out MeshRenderer renderer)) renderer = part.AddComponent<MeshRenderer>();
            filter.sharedMesh = partMesh;
            renderer.sharedMaterials = new[] { material };
            return part;
        }

        public void Dispose()
        {
            Test?.Dispose();
            Discard(faceMesh);
            Discard(partMesh);
            Discard(Metal);
            AvatarPlanCache.Invalidate();
        }

        static void Discard(Object obj)
        {
            if (obj == null || AssetDatabase.Contains(obj)) return;
            Object.DestroyImmediate(obj);
        }
    }
}
#endif
