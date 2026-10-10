using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;

namespace DocMe.AvatarDoctor.Editor
{
    public class PropertyTargetTests
    {
        TestAvatar avatar;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
        }

        (MenuTree tree, ParameterPlan plan) Model(params AvatarDoc[] menus)
        {
            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, menus);
            return (tree, ParameterPlanner.Plan(tree, new Dictionary<string, PlannedType> { { "Existing", PlannedType.Bool } }));
        }

        static AnimatorController FxOf(TestAvatar a)
        {
            return (AnimatorController)a.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
        }

        /// <summary>Runs model, defaults and the animator builder inside an NDMF context and returns the committed FX controller.</summary>
        AnimatorController BuildAnimator(out List<ErrorContext> errors, params AvatarDoc[] menus)
        {
            var (tree, plan) = Model(menus);
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var captured = ErrorReport.CaptureErrors(() => L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState)));
            context.DeactivateAllExtensionContexts();
            errors = captured;
            return FxOf(avatar);
        }

        static AnimatorControllerLayer Layer(AnimatorController fx, string name) => fx.layers.Single(l => l.name == name);

        static AnimatorState State(AnimatorControllerLayer layer, string name) => layer.stateMachine.states.Single(s => s.state.name == name).state;

        static float FloatValue(Motion motion, string path, System.Type type, string property)
        {
            var curve = AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve(path, type, property));
            Assert.That(curve, Is.Not.Null, path + " " + property);
            return curve.Evaluate(0f);
        }

        static bool HasCurve(Motion motion, string path, System.Type type, string property)
        {
            return AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve(path, type, property)) != null;
        }

        [Test]
        public void FloatPropertyWritesOnAndBaseValues()
        {
            avatar.MaterialA.SetFloat("_Glossiness", 0.2f);
            var menu = avatar.AddMenu();
            var shine = menu.AddEntry(EntryKind.Toggle, "Shine");
            shine.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 0.9f });

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: Shine");
            var off = State(layer, "Off");
            var on = State(layer, "On");
            Assert.That(FloatValue(off.motion, "Body", typeof(SkinnedMeshRenderer), "material._Glossiness"), Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(FloatValue(on.motion, "Body", typeof(SkinnedMeshRenderer), "material._Glossiness"), Is.EqualTo(0.9f).Within(0.0001f));
        }

        [Test]
        public void ColourPropertyWritesFourCurves()
        {
            avatar.MaterialA.SetColor("_Color", new Color(0.1f, 0.2f, 0.3f, 0.4f));
            var menu = avatar.AddMenu();
            var tint = menu.AddEntry(EntryKind.Toggle, "Tint");
            tint.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Color", type = PropertyValueType.Color, color = new Color(1f, 0.5f, 0.25f, 0.75f) });

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: Tint");
            var off = State(layer, "Off");
            var on = State(layer, "On");
            Assert.That(FloatValue(off.motion, "Body", typeof(SkinnedMeshRenderer), "material._Color.r"), Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(FloatValue(off.motion, "Body", typeof(SkinnedMeshRenderer), "material._Color.g"), Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(FloatValue(off.motion, "Body", typeof(SkinnedMeshRenderer), "material._Color.b"), Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(FloatValue(off.motion, "Body", typeof(SkinnedMeshRenderer), "material._Color.a"), Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(FloatValue(on.motion, "Body", typeof(SkinnedMeshRenderer), "material._Color.r"), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(FloatValue(on.motion, "Body", typeof(SkinnedMeshRenderer), "material._Color.g"), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(FloatValue(on.motion, "Body", typeof(SkinnedMeshRenderer), "material._Color.b"), Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(FloatValue(on.motion, "Body", typeof(SkinnedMeshRenderer), "material._Color.a"), Is.EqualTo(0.75f).Within(0.0001f));
        }

        // The Standard shader exposes _MainTex_ST automatically for its _MainTex slot, so it is used as the vector
        // property here rather than falling back to ClipContent.SetProperty directly.
        [Test]
        public void VectorPropertyWritesFourCurves()
        {
            Assert.That(avatar.MaterialA.HasProperty("_MainTex_ST"), Is.True, "Standard shader is expected to expose _MainTex_ST");
            avatar.MaterialA.SetVector("_MainTex_ST", new Vector4(1f, 1f, 0f, 0f));
            var menu = avatar.AddMenu();
            var tile = menu.AddEntry(EntryKind.Toggle, "Tile");
            tile.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_MainTex_ST", type = PropertyValueType.Vector, vector = new Vector4(2f, 3f, 4f, 5f) });

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: Tile");
            var off = State(layer, "Off");
            var on = State(layer, "On");
            Assert.That(FloatValue(off.motion, "Body", typeof(SkinnedMeshRenderer), "material._MainTex_ST.x"), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(FloatValue(off.motion, "Body", typeof(SkinnedMeshRenderer), "material._MainTex_ST.y"), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(FloatValue(off.motion, "Body", typeof(SkinnedMeshRenderer), "material._MainTex_ST.z"), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(FloatValue(off.motion, "Body", typeof(SkinnedMeshRenderer), "material._MainTex_ST.w"), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(FloatValue(on.motion, "Body", typeof(SkinnedMeshRenderer), "material._MainTex_ST.x"), Is.EqualTo(2f).Within(0.0001f));
            Assert.That(FloatValue(on.motion, "Body", typeof(SkinnedMeshRenderer), "material._MainTex_ST.y"), Is.EqualTo(3f).Within(0.0001f));
            Assert.That(FloatValue(on.motion, "Body", typeof(SkinnedMeshRenderer), "material._MainTex_ST.z"), Is.EqualTo(4f).Within(0.0001f));
            Assert.That(FloatValue(on.motion, "Body", typeof(SkinnedMeshRenderer), "material._MainTex_ST.w"), Is.EqualTo(5f).Within(0.0001f));
        }

        [Test]
        public void RadialPropertyBlendsBetweenTheEnds()
        {
            var menu = avatar.AddMenu();
            var dial = menu.AddEntry(EntryKind.Radial, "Shine");
            dial.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, minValue = 0.1f, value = 0.8f });

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: Shine");
            var state = layer.stateMachine.states.Single().state;
            var tree = (BlendTree)state.motion;
            Assert.That(FloatValue(tree.children[0].motion, "Body", typeof(SkinnedMeshRenderer), "material._Glossiness"), Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(FloatValue(tree.children[1].motion, "Body", typeof(SkinnedMeshRenderer), "material._Glossiness"), Is.EqualTo(0.8f).Within(0.0001f));
        }

        [Test]
        public void MissingPropertyWarnsAndIsSkipped()
        {
            var menu = avatar.AddMenu();
            var ghost = menu.AddEntry(EntryKind.Toggle, "Ghost");
            ghost.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_NoSuchProperty", type = PropertyValueType.Float, value = 1f });

            var (tree, _) = Model(menu);

            Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.property_missing" && m.EntryIds.Contains(ghost.id)), Is.True);

            var fx = BuildAnimator(out _, menu);
            var on = State(Layer(fx, "AD: Ghost"), "On");
            Assert.That(HasCurve(on.motion, "Body", typeof(SkinnedMeshRenderer), "material._NoSuchProperty"), Is.False);
        }

        [Test]
        public void BaseValueComesFromTheFirstMaterialThatHasIt()
        {
            var unlit = new Material(Shader.Find("Unlit/Color"));
            try
            {
                avatar.MaterialA.SetFloat("_Glossiness", 0.42f);
                // Slot 0 has no _Glossiness at all, so the base value must come from slot 1, the first that has it.
                avatar.Body.sharedMaterials = new[] { unlit, avatar.MaterialA };
                var menu = avatar.AddMenu();
                var shine = menu.AddEntry(EntryKind.Toggle, "Shine");
                shine.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 1f });

                var fx = BuildAnimator(out var errors, menu);

                Assert.That(errors, Is.Empty);
                var off = State(Layer(fx, "AD: Shine"), "Off");
                Assert.That(FloatValue(off.motion, "Body", typeof(SkinnedMeshRenderer), "material._Glossiness"), Is.EqualTo(0.42f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(unlit);
            }
        }

        [Test]
        public void PropertyOnlyOnTheSwappedMaterialIsAccepted()
        {
            var unlit = new Material(Shader.Find("Unlit/Color"));
            try
            {
                // Nothing on the renderer has _Glossiness; the material this very toggle swaps in does.
                avatar.Body.sharedMaterials = new[] { unlit, unlit };
                avatar.MaterialA.SetFloat("_Glossiness", 0.3f);
                var menu = avatar.AddMenu();
                var dissolve = menu.AddEntry(EntryKind.Toggle, "Dissolve");
                dissolve.materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 0, material = avatar.MaterialA });
                dissolve.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 1f });

                var (tree, _) = Model(menu);
                Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.property_missing" && m.EntryIds.Contains(dissolve.id)), Is.False);

                var fx = BuildAnimator(out var errors, menu);

                Assert.That(errors, Is.Empty);
                var layer = Layer(fx, "AD: Dissolve");
                Assert.That(FloatValue(State(layer, "On").motion, "Body", typeof(SkinnedMeshRenderer), "material._Glossiness"), Is.EqualTo(1f).Within(0.0001f));
                Assert.That(FloatValue(State(layer, "Off").motion, "Body", typeof(SkinnedMeshRenderer), "material._Glossiness"), Is.EqualTo(0.3f).Within(0.0001f),
                    "the base value comes from the material the toggle swaps in");
            }
            finally
            {
                Object.DestroyImmediate(unlit);
            }
        }

        [Test]
        public void DifferingMaterialValuesWarn()
        {
            var second = new Material(Shader.Find("Standard")) { name = "C" };
            try
            {
                avatar.MaterialA.SetFloat("_Glossiness", 0.2f);
                second.SetFloat("_Glossiness", 0.8f);
                avatar.Body.sharedMaterials = new[] { avatar.MaterialA, second };
                var menu = avatar.AddMenu();
                var shine = menu.AddEntry(EntryKind.Toggle, "Shine");
                shine.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 1f });

                var (tree, _) = Model(menu);

                var message = tree.Messages.Single(m => m.Key == "ad.warn.property_differs");
                Assert.That(message.EntryIds, Does.Contain(shine.id));
                Assert.That(message.Args[1], Is.EqualTo("Body"));
                Assert.That(message.Args[2], Is.EqualTo("_Glossiness"));
            }
            finally
            {
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void BlendTimeSetsBothTransitionDurations()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            hat.blendSeconds = 2.5f;
            var glasses = menu.AddEntry(EntryKind.Toggle, "Glasses");
            glasses.objects.Add(new ObjectTarget { target = avatar.Glasses });
            glasses.blendSeconds = 15f;

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            var hatLayer = Layer(fx, "AD: Hat");
            var hatOff = State(hatLayer, "Off");
            var hatOn = State(hatLayer, "On");
            var toOn = hatOff.transitions.Single();
            var toOff = hatOn.transitions.Single();
            Assert.That(toOn.duration, Is.EqualTo(2.5f));
            Assert.That(toOn.hasFixedDuration, Is.True);
            Assert.That(toOn.hasExitTime, Is.False);
            Assert.That(toOff.duration, Is.EqualTo(2.5f));

            var glassesLayer = Layer(fx, "AD: Glasses");
            Assert.That(State(glassesLayer, "Off").transitions.Single().duration, Is.EqualTo(10f), "clamped to 10 seconds");
        }

        [Test]
        public void BlendTimeIsIgnoredInGroupsWithAnInfo()
        {
            var menu = avatar.AddMenu();
            var casual = menu.AddEntry(EntryKind.Toggle, "Casual");
            casual.objects.Add(new ObjectTarget { target = avatar.Hat });
            casual.exclusiveGroup = "Outfits";
            casual.blendSeconds = 3f;
            var suit = menu.AddEntry(EntryKind.Toggle, "Suit");
            suit.objects.Add(new ObjectTarget { target = avatar.Glasses });
            suit.exclusiveGroup = "Outfits";

            var (tree, _) = Model(menu);

            Assert.That(tree.Messages.Any(m => m.Key == "ad.info.blend_ignored_in_group" && m.EntryIds.Contains(casual.id)), Is.True);

            var fx = BuildAnimator(out var errors, menu);
            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: Outfits");
            Assert.That(layer.stateMachine.anyStateTransitions.Select(t => t.duration), Is.All.EqualTo(0f));
        }

        [Test]
        public void SharedPropertyTargetsAreReported()
        {
            var menu = avatar.AddMenu();
            var a = menu.AddEntry(EntryKind.Toggle, "A");
            a.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 1f });
            var b = menu.AddEntry(EntryKind.Toggle, "B");
            b.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 0.5f });

            var (tree, _) = Model(menu);

            var message = tree.Messages.Single(m => m.Key == "ad.info.target_shared");
            Assert.That(message.Args[0], Is.EqualTo("Body material._Glossiness"));
            Assert.That(message.EntryIds, Is.EquivalentTo(new[] { a.id, b.id }));
        }

        [Test]
        public void CaptureTargetsIncludePropertyRenderers()
        {
            var entry = MenuEntry.Create(EntryKind.Toggle, "Shine");
            entry.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 1f });

            var targets = IconCapture.TargetsOf(entry, null, avatar.Root.transform);

            Assert.That(targets, Is.EquivalentTo(new Renderer[] { avatar.Body }));
        }
    }
}
