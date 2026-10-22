using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace DocMe.AvatarDoctor.Editor
{
    public class BuildTests
    {
        TestAvatar avatar;

        [SetUp]
        public void SetUp()
        {
            GeneratedAssets.Note();
            avatar = TestAvatar.Create();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            GeneratedAssets.CleanUp();
        }

        static MenuEntry Toggle(AvatarDoc menu, string name, GameObject target = null, bool activeWhenOn = true)
        {
            var entry = menu.AddEntry(EntryKind.Toggle, name);
            if (target != null) entry.objects.Add(new ObjectTarget { target = target, activeWhenOn = activeWhenOn });
            return entry;
        }

        (MenuTree tree, ParameterPlan plan) Model(params AvatarDoc[] menus)
        {
            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, menus);
            return (tree, ParameterPlanner.Plan(tree, new Dictionary<string, PlannedType> { { "Existing", PlannedType.Bool } }));
        }

        static ControlBinding BindingOf(ParameterPlan plan, MenuTree tree, MenuEntry entry) => plan.Bindings[tree.Controls.Single(c => c.Entry == entry)];

        [Test]
        public void BaseStateCapturesSceneValuesBeforeChanges()
        {
            var menu = avatar.AddMenu();
            var t = Toggle(menu, "Hat", avatar.Hat);
            t.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100 });
            t.materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 1, material = avatar.MaterialB });
            avatar.Hat.SetActive(false);
            avatar.Body.SetBlendShapeWeight(0, 30f);
            var (tree, _) = Model(menu);

            var baseState = BaseState.Capture(tree);

            Assert.That(baseState.ActiveOf(avatar.Hat), Is.False);
            Assert.That(baseState.WeightOf(avatar.Body, "Blush"), Is.EqualTo(30f));
            Assert.That(baseState.MaterialOf(avatar.Body, 1), Is.SameAs(avatar.MaterialA));
        }

        [Test]
        public void ClipContentWritesOnAndOffValues()
        {
            var menu = avatar.AddMenu();
            var t = Toggle(menu, "Hat", avatar.Hat);
            t.objects.Add(new ObjectTarget { target = avatar.Glasses, activeWhenOn = false });
            t.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 80 });
            t.materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 1, material = avatar.MaterialB });
            avatar.Body.SetBlendShapeWeight(0, 10f);
            var (tree, _) = Model(menu);
            var baseState = BaseState.Capture(tree);

            var on = new ClipContent();
            on.WriteToggle(t, true, baseState);
            var off = new ClipContent();
            off.WriteToggle(t, false, baseState);

            Assert.That(on.Count, Is.EqualTo(4));
            on.ApplyToScene();
            Assert.That(avatar.Hat.activeSelf, Is.True);
            Assert.That(avatar.Glasses.activeSelf, Is.False);
            Assert.That(avatar.Body.GetBlendShapeWeight(0), Is.EqualTo(80f));
            Assert.That(avatar.Body.sharedMaterials[1], Is.SameAs(avatar.MaterialB));
            off.ApplyToScene();
            Assert.That(avatar.Hat.activeSelf, Is.False);
            Assert.That(avatar.Glasses.activeSelf, Is.True);
            Assert.That(avatar.Body.GetBlendShapeWeight(0), Is.EqualTo(10f));
            Assert.That(avatar.Body.sharedMaterials[1], Is.SameAs(avatar.MaterialA));
        }

        [Test]
        public void DefaultsApplierWritesDefaultStatesIntoTheScene()
        {
            var menu = avatar.AddMenu();
            var hat = Toggle(menu, "Hat", avatar.Hat);
            hat.defaultOn = true;
            var glasses = Toggle(menu, "Glasses", avatar.Glasses);
            var eyes = menu.AddEntry(EntryKind.Radial, "Eyes");
            eyes.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", minWeight = 20, maxWeight = 60 });
            eyes.defaultValue = 0.5f;
            avatar.Hat.SetActive(false);
            var (tree, plan) = Model(menu);
            var baseState = BaseState.Capture(tree);

            DefaultsApplier.Apply(tree, plan, baseState);

            Assert.That(avatar.Hat.activeSelf, Is.True);
            Assert.That(avatar.Glasses.activeSelf, Is.False);
            Assert.That(avatar.Body.GetBlendShapeWeight(1), Is.EqualTo(40f));
        }

        [Test]
        public void GroupDefaultMemberIsOnAndOthersOff()
        {
            var menu = avatar.AddMenu();
            var a = Toggle(menu, "A", avatar.Hat); a.exclusiveGroup = "G";
            var b = Toggle(menu, "B", avatar.Glasses); b.exclusiveGroup = "G"; b.defaultOn = true;
            var (tree, plan) = Model(menu);
            var baseState = BaseState.Capture(tree);

            DefaultsApplier.Apply(tree, plan, baseState);

            Assert.That(avatar.Hat.activeSelf, Is.False);
            Assert.That(avatar.Glasses.activeSelf, Is.True);
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

        static Object ObjectValue(Motion motion, string path, System.Type type, string property)
        {
            var keys = AnimationUtility.GetObjectReferenceCurve((AnimationClip)motion, EditorCurveBinding.PPtrCurve(path, type, property));
            Assert.That(keys, Is.Not.Null, path + " " + property);
            return keys[0].value;
        }

        [Test]
        public void AnOffValueOfItsOwnIsWrittenIntoTheOffState()
        {
            var menu = avatar.AddMenu();
            var skin = Toggle(menu, "Oily skin");
            skin.properties.Add(new MaterialPropertyTarget
            {
                renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float,
                value = 1f, minValue = 0.6f, explicitOff = true,
            });
            skin.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 80f, minWeight = 25f, explicitOff = true });
            avatar.MaterialA.SetFloat("_Glossiness", 0.1f);
            avatar.Body.SetBlendShapeWeight(0, 5f);

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: Oily skin");
            var off = State(layer, "Off");
            var on = State(layer, "On");
            Assert.That(FloatValue(off.motion, "Body", typeof(SkinnedMeshRenderer), "material._Glossiness"), Is.EqualTo(0.6f).Within(0.0001f), "the off state writes the target's own value, not the material's");
            Assert.That(FloatValue(on.motion, "Body", typeof(SkinnedMeshRenderer), "material._Glossiness"), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(FloatValue(off.motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), Is.EqualTo(25f), "the off state writes the shape's own value, not the scene weight");
            Assert.That(FloatValue(on.motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), Is.EqualTo(80f));
        }

        [Test]
        public void ToggleLayerHasExplicitOffAndOnStates()
        {
            var menu = avatar.AddMenu();
            var hat = Toggle(menu, "Hat", avatar.Hat);
            hat.objects.Add(new ObjectTarget { target = avatar.Glasses, activeWhenOn = false });
            hat.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 75 });
            hat.materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 1, material = avatar.MaterialB });
            avatar.Body.SetBlendShapeWeight(0, 5f);

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: Hat");
            var off = State(layer, "Off");
            var on = State(layer, "On");
            Assert.That(layer.stateMachine.defaultState, Is.SameAs(off));
            Assert.That(FloatValue(off.motion, "Hat", typeof(GameObject), "m_IsActive"), Is.EqualTo(0f));
            Assert.That(FloatValue(on.motion, "Hat", typeof(GameObject), "m_IsActive"), Is.EqualTo(1f));
            Assert.That(FloatValue(off.motion, "Glasses", typeof(GameObject), "m_IsActive"), Is.EqualTo(1f));
            Assert.That(FloatValue(on.motion, "Glasses", typeof(GameObject), "m_IsActive"), Is.EqualTo(0f));
            Assert.That(FloatValue(off.motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), Is.EqualTo(5f));
            Assert.That(FloatValue(on.motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), Is.EqualTo(75f));
            Assert.That(ObjectValue(off.motion, "Body", typeof(SkinnedMeshRenderer), "m_Materials.Array.data[1]"), Is.SameAs(avatar.MaterialA));
            Assert.That(ObjectValue(on.motion, "Body", typeof(SkinnedMeshRenderer), "m_Materials.Array.data[1]"), Is.SameAs(avatar.MaterialB));

            var toOn = off.transitions.Single();
            Assert.That(toOn.destinationState, Is.SameAs(on));
            Assert.That(toOn.conditions.Single().parameter, Is.EqualTo("AD/Hat"));
            Assert.That(toOn.conditions.Single().mode, Is.EqualTo(AnimatorConditionMode.If));
            Assert.That(toOn.hasExitTime, Is.False);
            Assert.That(toOn.duration, Is.EqualTo(0f));
            var toOff = on.transitions.Single();
            Assert.That(toOff.conditions.Single().mode, Is.EqualTo(AnimatorConditionMode.IfNot));
            Assert.That(fx.parameters.Single(p => p.name == "AD/Hat").type, Is.EqualTo(AnimatorControllerParameterType.Bool));
        }

        [Test]
        public void WriteDefaultsFollowsTheExistingController()
        {
            var menu = avatar.AddMenu();
            Toggle(menu, "Hat", avatar.Hat);
            var fx = BuildAnimator(out _, menu);
            Assert.That(State(Layer(fx, "AD: Hat"), "On").writeDefaultValues, Is.False);

            avatar.Dispose();
            avatar = TestAvatar.Create(writeDefaultsOff: false);
            menu = avatar.AddMenu();
            Toggle(menu, "Hat", avatar.Hat);
            fx = BuildAnimator(out _, menu);
            Assert.That(State(Layer(fx, "AD: Hat"), "On").writeDefaultValues, Is.True);
        }

        [Test]
        public void WriteDefaultsIsOnWhenTheExistingControllerIsMixed()
        {
            var second = avatar.Fx.layers[0].stateMachine.AddState("Other");
            second.writeDefaultValues = true;
            var menu = avatar.AddMenu();
            Toggle(menu, "Hat", avatar.Hat);
            var fx = BuildAnimator(out _, menu);
            Assert.That(State(Layer(fx, "AD: Hat"), "Off").writeDefaultValues, Is.True);
        }

        [Test]
        public void CustomIntParameterUsesEqualsConditions()
        {
            var menu = avatar.AddMenu();
            var hat = Toggle(menu, "Hat", avatar.Hat);
            hat.parameterName = "Mode";
            hat.parameterValue = 3;
            var fx = BuildAnimator(out _, menu);

            var layer = Layer(fx, "AD: Hat");
            var toOn = State(layer, "Off").transitions.Single();
            Assert.That(toOn.conditions.Single().mode, Is.EqualTo(AnimatorConditionMode.Equals));
            Assert.That(toOn.conditions.Single().threshold, Is.EqualTo(3f));
            Assert.That(fx.parameters.Single(p => p.name == "Mode").type, Is.EqualTo(AnimatorControllerParameterType.Int));
        }

        [Test]
        public void EntriesWithoutEffectsGetNoLayer()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Empty");
            var fx = BuildAnimator(out _, menu);
            Assert.That(fx.layers.Any(l => l.name == "AD: Empty"), Is.False);
        }

        [Test]
        public void GroupLayerHasOneStatePerMemberPlusOff()
        {
            var menu = avatar.AddMenu();
            var a = Toggle(menu, "Casual", avatar.Hat); a.exclusiveGroup = "Outfits";
            var b = Toggle(menu, "Suit", avatar.Glasses); b.exclusiveGroup = "Outfits"; b.defaultOn = true;
            b.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100 });

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: Outfits");
            var off = State(layer, "Off");
            var casual = State(layer, "Casual");
            var suit = State(layer, "Suit");
            Assert.That(layer.stateMachine.defaultState, Is.SameAs(off));
            Assert.That(FloatValue(off.motion, "Hat", typeof(GameObject), "m_IsActive"), Is.EqualTo(0f));
            Assert.That(FloatValue(off.motion, "Glasses", typeof(GameObject), "m_IsActive"), Is.EqualTo(0f));
            Assert.That(FloatValue(casual.motion, "Hat", typeof(GameObject), "m_IsActive"), Is.EqualTo(1f));
            Assert.That(FloatValue(casual.motion, "Glasses", typeof(GameObject), "m_IsActive"), Is.EqualTo(0f));
            Assert.That(FloatValue(casual.motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), Is.EqualTo(0f));
            Assert.That(FloatValue(suit.motion, "Hat", typeof(GameObject), "m_IsActive"), Is.EqualTo(0f));
            Assert.That(FloatValue(suit.motion, "Glasses", typeof(GameObject), "m_IsActive"), Is.EqualTo(1f));
            Assert.That(FloatValue(suit.motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush"), Is.EqualTo(100f));

            var any = layer.stateMachine.anyStateTransitions;
            Assert.That(any.Length, Is.EqualTo(3));
            var toSuit = any.Single(t => t.destinationState == suit);
            Assert.That(toSuit.conditions.Single().mode, Is.EqualTo(AnimatorConditionMode.Equals));
            Assert.That(toSuit.conditions.Single().threshold, Is.EqualTo(2f));
            Assert.That(toSuit.canTransitionToSelf, Is.False);
            var toOff = any.Single(t => t.destinationState == off);
            Assert.That(toOff.conditions.Select(c => c.mode).Distinct().Single(), Is.EqualTo(AnimatorConditionMode.NotEqual));
            Assert.That(toOff.conditions.Select(c => c.threshold), Is.EquivalentTo(new[] { 1f, 2f }));
            var parameter = fx.parameters.Single(p => p.name == "AD/Outfits");
            Assert.That(parameter.type, Is.EqualTo(AnimatorControllerParameterType.Int));
            Assert.That(parameter.defaultInt, Is.EqualTo(2));
            Assert.That(fx.layers.Any(l => l.name == "AD: Casual" || l.name == "AD: Suit"), Is.False);
        }

        [Test]
        public void RadialLayerUsesAOneDimensionalBlendTree()
        {
            var menu = avatar.AddMenu();
            var eyes = menu.AddEntry(EntryKind.Radial, "Eyes");
            eyes.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", minWeight = 20, maxWeight = 60 });
            eyes.defaultValue = 0.5f;

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: Eyes");
            var state = layer.stateMachine.states.Single().state;
            var tree = (BlendTree)state.motion;
            Assert.That(tree.blendType, Is.EqualTo(BlendTreeType.Simple1D));
            Assert.That(tree.blendParameter, Is.EqualTo("AD/Eyes"));
            Assert.That(tree.children.Select(c => c.threshold), Is.EqualTo(new[] { 0f, 1f }));
            Assert.That(FloatValue(tree.children[0].motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"), Is.EqualTo(20f));
            Assert.That(FloatValue(tree.children[1].motion, "Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big"), Is.EqualTo(60f));
            var parameter = fx.parameters.Single(p => p.name == "AD/Eyes");
            Assert.That(parameter.type, Is.EqualTo(AnimatorControllerParameterType.Float));
            Assert.That(parameter.defaultFloat, Is.EqualTo(0.5f));
        }

        [Test]
        public void ExistingTypesPreferExpressionParametersOverAnimator()
        {
            avatar.Fx.AddParameter("Existing", AnimatorControllerParameterType.Float);
            avatar.Fx.AddParameter("AnimOnly", AnimatorControllerParameterType.Int);
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var fx = asc.ControllerContext.Controllers[VRCAvatarDescriptor.AnimLayerType.FX];

            var types = ParametersWriter.ExistingParameters(avatar.ExpressionParameters, fx);
            context.DeactivateAllExtensionContexts();

            Assert.That(types["Existing"].Type, Is.EqualTo(PlannedType.Bool));
            Assert.That(types["Existing"].IsExpressionParameter, Is.True);
            Assert.That(types["AnimOnly"].Type, Is.EqualTo(PlannedType.Int));
            Assert.That(types["AnimOnly"].IsExpressionParameter, Is.False);
        }

        [Test]
        public void ParametersAreAppendedAndReusedOnesLeftAlone()
        {
            var menu = avatar.AddMenu();
            var hat = Toggle(menu, "Hat", avatar.Hat); hat.defaultOn = true; hat.saved = false;
            var reuse = Toggle(menu, "Reuse", avatar.Glasses); reuse.parameterName = "Existing"; reuse.defaultOn = true;
            var eyes = menu.AddEntry(EntryKind.Radial, "Eyes"); eyes.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big" }); eyes.synced = false;
            var (tree, plan) = Model(menu);
            var context = new BuildContext(avatar.Root, null);

            ParametersWriter.Write(context, avatar.Descriptor, plan);

            var asset = avatar.Descriptor.expressionParameters;
            Assert.That(asset, Is.SameAs(avatar.ExpressionParameters), "temporary asset is edited in place");
            Assert.That(avatar.Descriptor.customExpressions, Is.True);
            var names = asset.parameters.Select(p => p.name).ToArray();
            Assert.That(names, Is.EqualTo(new[] { "Existing", "AD/Hat", "AD/Eyes" }));
            var existing = asset.parameters[0];
            Assert.That(existing.saved, Is.True);
            Assert.That(existing.networkSynced, Is.True);
            Assert.That(existing.defaultValue, Is.EqualTo(0f));
            Assert.That(plan.Messages.Any(m => m.Key == "ad.info.parameter_reused"), Is.True);
            var hatParam = asset.parameters[1];
            Assert.That(hatParam.valueType, Is.EqualTo(VRCExpressionParameters.ValueType.Bool));
            Assert.That(hatParam.defaultValue, Is.EqualTo(1f));
            Assert.That(hatParam.saved, Is.False);
            Assert.That(hatParam.networkSynced, Is.True);
            Assert.That(asset.parameters[2].valueType, Is.EqualTo(VRCExpressionParameters.ValueType.Float));
            Assert.That(asset.parameters[2].networkSynced, Is.False);
            Assert.That(ParametersWriter.SyncedCost(asset), Is.EqualTo(2));
        }

        [Test]
        public void PersistentParametersAssetIsClonedNotEdited()
        {
            var folder = TempAssetFolder.Create("AvatarDocBuildTests");
            var path = folder + "/Params.asset";
            AssetDatabase.CreateAsset(avatar.ExpressionParameters, path);
            try
            {
                var menu = avatar.AddMenu();
                Toggle(menu, "Hat", avatar.Hat);
                var (tree, plan) = Model(menu);
                var context = new BuildContext(avatar.Root, null);

                ParametersWriter.Write(context, avatar.Descriptor, plan);

                var original = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(path);
                Assert.That(avatar.Descriptor.expressionParameters, Is.Not.SameAs(original));
                Assert.That(original.parameters.Length, Is.EqualTo(1));
                Assert.That(avatar.Descriptor.expressionParameters.parameters.Length, Is.EqualTo(2));
            }
            finally
            {
                TempAssetFolder.Delete(folder);
                avatar.ExpressionParameters = null;
            }
        }

        [Test]
        public void MissingParametersAssetIsCreated()
        {
            avatar.Descriptor.expressionParameters = null;
            var menu = avatar.AddMenu();
            Toggle(menu, "Hat", avatar.Hat);
            var (tree, plan) = Model(menu);
            var context = new BuildContext(avatar.Root, null);

            ParametersWriter.Write(context, avatar.Descriptor, plan);

            Assert.That(avatar.Descriptor.expressionParameters, Is.Not.Null);
            Assert.That(avatar.Descriptor.expressionParameters.parameters.Single().name, Is.EqualTo("AD/Hat"));
        }

        static VRCExpressionsMenu.Control ControlNamed(VRCExpressionsMenu menu, string name) => menu.controls.Single(c => c.name == name);

        /// <summary>
        /// Builds the model and plan for menu against an empty root (so the fixture's own "Old" submenu does not
        /// show up next to the one being tested), writes the menu and returns the written root. Mirrors the other
        /// menu writer tests' inline steps.
        /// </summary>
        VRCExpressionsMenu WriteMenus(AvatarDoc menu)
        {
            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });
            var plan = ParameterPlanner.Plan(tree, new Dictionary<string, PlannedType>());
            var context = new BuildContext(avatar.Root, null);
            MenuWriter.Write(context, avatar.Descriptor, tree, plan);
            return avatar.Descriptor.expressionsMenu;
        }

        [Test]
        public void MenuIsWrittenWithTogglesRadialsAndSubmenus()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var hat = Toggle(menu, "Hat", avatar.Hat); hat.parentId = outfits.id;
            var eyes = menu.AddEntry(EntryKind.Radial, "Eyes"); eyes.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big" });
            var (tree, plan) = Model(menu);
            var context = new BuildContext(avatar.Root, null);

            MenuWriter.Write(context, avatar.Descriptor, tree, plan);

            var root = avatar.Descriptor.expressionsMenu;
            Assert.That(root, Is.Not.SameAs(avatar.ExpressionsMenu), "root gained items, so it is a new asset");
            Assert.That(root.controls.Select(c => c.name), Is.EqualTo(new[] { "Old", "RootToggle", "Outfits", "Eyes" }));
            Assert.That(ControlNamed(root, "Old").subMenu, Is.SameAs(avatar.OldSubmenu), "untouched submenu keeps its asset");
            Assert.That(ControlNamed(root, "RootToggle").parameter.name, Is.EqualTo("Existing"));
            var outfitsMenu = ControlNamed(root, "Outfits").subMenu;
            Assert.That(ControlNamed(root, "Outfits").type, Is.EqualTo(VRCExpressionsMenu.Control.ControlType.SubMenu));
            var hatControl = ControlNamed(outfitsMenu, "Hat");
            Assert.That(hatControl.type, Is.EqualTo(VRCExpressionsMenu.Control.ControlType.Toggle));
            Assert.That(hatControl.parameter.name, Is.EqualTo("AD/Hat"));
            Assert.That(hatControl.value, Is.EqualTo(1f));
            var eyesControl = ControlNamed(root, "Eyes");
            Assert.That(eyesControl.type, Is.EqualTo(VRCExpressionsMenu.Control.ControlType.RadialPuppet));
            Assert.That(eyesControl.subParameters.Single().name, Is.EqualTo("AD/Eyes"));
            Assert.That(avatar.Descriptor.customExpressions, Is.True);
        }

        [Test]
        public void MenuWriterKeepsRichControlNamesAndPlainAssetNames()
        {
            var menu = avatar.AddMenu();
            var outfits = MenuEntry.Create(EntryKind.Submenu, "<b>Outfits</b>");
            var hat = MenuEntry.Create(EntryKind.Toggle, "<color=#FF0000>Hat</color>");
            hat.parentId = outfits.id;
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            menu.entries.Add(outfits);
            menu.entries.Add(hat);

            var root = WriteMenus(menu);

            var submenuControl = root.controls.Single(c => c.type == VRCExpressionsMenu.Control.ControlType.SubMenu);
            Assert.That(submenuControl.name, Is.EqualTo("<b>Outfits</b>"));
            Assert.That(submenuControl.subMenu.name, Is.EqualTo("Outfits"));
            Assert.That(submenuControl.subMenu.controls.Single().name, Is.EqualTo("<color=#FF0000>Hat</color>"));
        }

        [Test]
        public void AsSubmenuKeepsTheStyledMenuName()
        {
            var menu = avatar.AddMenu();
            menu.installMode = InstallMode.AsSubmenu;
            menu.menuName = "<b>Extras</b>";
            Toggle(menu, "Hat", avatar.Hat);

            var root = WriteMenus(menu);

            var control = root.controls.Single(c => c.type == VRCExpressionsMenu.Control.ControlType.SubMenu);
            Assert.That(control.name, Is.EqualTo("<b>Extras</b>"), "the styled name reaches VRChat");
            Assert.That(control.subMenu.name, Is.EqualTo("Extras"), "the asset keeps the plain name");
        }

        [Test]
        public void InstallingIntoAnExistingSubmenuClonesOnlyThatPath()
        {
            var menu = avatar.AddMenu();
            menu.installPath = "Old";
            Toggle(menu, "New", avatar.Hat);
            var (tree, plan) = Model(menu);
            var context = new BuildContext(avatar.Root, null);

            MenuWriter.Write(context, avatar.Descriptor, tree, plan);

            var root = avatar.Descriptor.expressionsMenu;
            var old = ControlNamed(root, "Old").subMenu;
            Assert.That(old, Is.Not.SameAs(avatar.OldSubmenu));
            Assert.That(old.controls.Select(c => c.name), Is.EqualTo(new[] { "OldToggle", "New" }));
            Assert.That(avatar.OldSubmenu.controls.Count, Is.EqualTo(1), "source asset unchanged");
        }

        [Test]
        public void GroupMembersWriteTheirValueOnTheSharedParameter()
        {
            var menu = avatar.AddMenu();
            var a = Toggle(menu, "Casual", avatar.Hat); a.exclusiveGroup = "Outfits";
            var b = Toggle(menu, "Suit", avatar.Glasses); b.exclusiveGroup = "Outfits";
            var (tree, plan) = Model(menu);
            var context = new BuildContext(avatar.Root, null);

            MenuWriter.Write(context, avatar.Descriptor, tree, plan);

            var root = avatar.Descriptor.expressionsMenu;
            Assert.That(ControlNamed(root, "Casual").parameter.name, Is.EqualTo("AD/Outfits"));
            Assert.That(ControlNamed(root, "Casual").value, Is.EqualTo(1f));
            Assert.That(ControlNamed(root, "Suit").value, Is.EqualTo(2f));
        }

        [Test]
        public void MissingRootMenuIsCreated()
        {
            avatar.Descriptor.expressionsMenu = null;
            var menu = avatar.AddMenu();
            Toggle(menu, "Hat", avatar.Hat);
            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });
            var plan = ParameterPlanner.Plan(tree, new Dictionary<string, PlannedType>());
            var context = new BuildContext(avatar.Root, null);

            MenuWriter.Write(context, avatar.Descriptor, tree, plan);

            Assert.That(avatar.Descriptor.expressionsMenu.controls.Single().name, Is.EqualTo("Hat"));
        }

        List<ErrorContext> RunFullBuild()
        {
            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            return errors;
        }

        static bool HasError(List<ErrorContext> errors, string key)
        {
            return errors.Any(e => e.TheError is SimpleError s && s.TitleKey == key);
        }

        [Test]
        public void RunBuildsLayersParametersAndMenusAndRemovesComponents()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var casual = Toggle(menu, "Casual", avatar.Hat); casual.parentId = outfits.id; casual.exclusiveGroup = "Outfits"; casual.defaultOn = true;
            var suit = Toggle(menu, "Suit", avatar.Glasses); suit.parentId = outfits.id; suit.exclusiveGroup = "Outfits";
            var eyes = menu.AddEntry(EntryKind.Radial, "Eyes"); eyes.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big" });
            var pack = new GameObject("Pack"); pack.transform.SetParent(avatar.Root.transform);
            var packMenu = avatar.AddMenu(pack);
            packMenu.installPath = "Outfits";
            var swim = Toggle(packMenu, "Swim", avatar.Glasses);
            swim.exclusiveGroup = "Outfits";
            swim.groupComponent = menu;

            var errors = RunFullBuild();

            Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error), Is.Empty);
            var fx = FxOf(avatar);
            Assert.That(fx.layers.Select(l => l.name), Is.EquivalentTo(new[] { "Base", "AD: Outfits", "AD: Eyes" }));
            Assert.That(Layer(fx, "AD: Outfits").stateMachine.states.Select(s => s.state.name), Is.EquivalentTo(new[] { "Off", "Casual", "Suit", "Swim" }));
            var parameters = avatar.Descriptor.expressionParameters.parameters.Select(p => p.name).ToArray();
            Assert.That(parameters, Is.EqualTo(new[] { "Existing", "AD/Outfits", "AD/Eyes" }));
            Assert.That(avatar.Descriptor.expressionParameters.parameters[1].defaultValue, Is.EqualTo(1f));
            var root = avatar.Descriptor.expressionsMenu;
            var outfitsMenu = ControlNamed(root, "Outfits").subMenu;
            Assert.That(outfitsMenu.controls.Select(c => c.name), Is.EqualTo(new[] { "Casual", "Suit", "Swim" }));
            Assert.That(ControlNamed(outfitsMenu, "Swim").value, Is.EqualTo(3f));
            Assert.That(avatar.Hat.activeSelf, Is.True, "default member applied to the scene");
            Assert.That(avatar.Glasses.activeSelf, Is.False);
            Assert.That(avatar.Root.GetComponentsInChildren<AvatarDoc>(true), Is.Empty);
        }

        void OverBudget()
        {
            var menu = avatar.AddMenu();
            int radials = VRCExpressionParameters.MAX_PARAMETER_COST / 8 + 1;
            for (int i = 0; i < radials; i++)
            {
                var r = menu.AddEntry(EntryKind.Radial, "R" + i);
                r.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big" });
            }
        }

        [Test]
        public void SyncedBitsOverTheLimitBlockTheBuild()
        {
            OverBudget();
            // VRCFury is installed in this project; without it nothing would compress the parameters at upload.
            var detect = ParametersWriter.VrcFuryInstalled;
            ParametersWriter.VrcFuryInstalled = () => false;
            List<ErrorContext> errors;
            try
            {
                errors = RunFullBuild();
            }
            finally
            {
                ParametersWriter.VrcFuryInstalled = detect;
            }

            Assert.That(HasError(errors, "ad.error.bits_exceeded"), Is.True);
            Assert.That(FxOf(avatar).layers.Any(l => l.name.StartsWith("AD:")), Is.False);
            Assert.That(avatar.Descriptor.expressionsMenu, Is.SameAs(avatar.ExpressionsMenu));
        }

        [Test]
        public void SyncedBitsOverTheLimitWarnWhenVrcFuryWillCompressThem()
        {
            OverBudget();
            var detect = ParametersWriter.VrcFuryInstalled;
            ParametersWriter.VrcFuryInstalled = () => true;
            List<ErrorContext> errors;
            try
            {
                errors = RunFullBuild();
            }
            finally
            {
                ParametersWriter.VrcFuryInstalled = detect;
            }

            Assert.That(HasError(errors, "ad.error.bits_exceeded"), Is.False, "VRCFury compresses parameters at upload, so the count here is not the last word");
            var warning = errors.SingleOrDefault(e => e.TheError is SimpleError s && s.TitleKey == "ad.warn.bits_exceeded_vrcfury");
            Assert.That(warning, Is.Not.Null);
            Assert.That(warning.TheError.Severity, Is.EqualTo(ErrorSeverity.NonFatal));
            Assert.That(FxOf(avatar).layers.Any(l => l.name == "AD: R0"), Is.True, "the build goes on");
            Assert.That(avatar.Descriptor.expressionsMenu, Is.Not.SameAs(avatar.ExpressionsMenu), "the menu is written");
            Assert.That(avatar.Descriptor.expressionParameters.parameters.Select(p => p.name), Does.Contain("AD/R0"), "and so are the parameters");
        }

        static System.Type ProviderType => typeof(AvatarDoctorPlugin).Assembly.GetType("DocMe.AvatarDoctor.Editor.AvatarDoctorParameterProvider");

        static object ProviderMemo() => ProviderType.GetField("memoTree", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);

        [Test]
        public void TheParameterProviderKeepsNothingOfABuild()
        {
            var menu = avatar.AddMenu();
            Toggle(menu, "Hat", avatar.Hat);

            // The build asks every provider through ParameterInfo; the answer for a build clone must not outlive it.
            var errors = RunFullBuild();

            Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error), Is.Empty);
            Assert.That(ProviderMemo(), Is.Null, "the clone's plan, with its transforms and components, is let go once the build is done with it");
        }

        /// <summary>
        /// Every handler subscribed to EditorApplication.hierarchyChanged right now. Unity 2022.3 keeps them in the
        /// private EventWithPerformanceTracker m_HierarchyChangedEvent and raises them only from its own editor loop,
        /// which a test that runs in one go never reaches, so they are read here by reflection.
        /// </summary>
        static List<System.Action> HierarchyChangedHandlers()
        {
            var field = typeof(EditorApplication).GetField("m_HierarchyChangedEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(field, Is.Not.Null, "Unity keeps hierarchyChanged's handlers in m_HierarchyChangedEvent");
            var handlers = new List<System.Action>();
            var tracker = field.GetValue(null);
            using (var each = (IEnumerator<System.Action>)tracker.GetType().GetMethod("GetEnumerator").Invoke(tracker, null))
                while (each.MoveNext()) handlers.Add(each.Current);
            return handlers;
        }

        [Test]
        public void TheParameterProviderLetsGoOfAnAvatarThatIsGone()
        {
            var menu = avatar.AddMenu();
            Toggle(menu, "Hat", avatar.Hat);
            ParameterInfo.ForUI.GetParametersForObject(avatar.Root).ToList();
            Assert.That(ProviderMemo(), Is.Not.Null, "the inspector's own question leaves a plan behind, which is what keeps the next one cheap");

            Object.DestroyImmediate(avatar.Root);
            // Only the provider's own handler runs: raising the whole event would run every other tool's too.
            var handler = HierarchyChangedHandlers().SingleOrDefault(h => h.Method.DeclaringType == ProviderType);
            Assert.That(handler, Is.Not.Null, "the provider listens for the hierarchy changing");
            handler();

            Assert.That(ProviderMemo(), Is.Null, "the memo of a destroyed avatar is dropped when the hierarchy changes");
        }

        [Test]
        public void NoComponentsMeansNoChanges()
        {
            var errors = RunFullBuild();

            Assert.That(errors, Is.Empty);
            Assert.That(avatar.Descriptor.expressionsMenu, Is.SameAs(avatar.ExpressionsMenu));
            Assert.That(avatar.Descriptor.expressionParameters, Is.SameAs(avatar.ExpressionParameters));
            Assert.That(FxOf(avatar).layers.Length, Is.EqualTo(1));
        }

        [Test]
        public void OverflowingMenusAreSplitDuringTheBuild()
        {
            var menu = avatar.AddMenu();
            menu.installMode = InstallMode.AsSubmenu;
            menu.menuName = "Many";
            for (int i = 0; i < VRCExpressionsMenu.MAX_CONTROLS + 1; i++) Toggle(menu, "T" + i, avatar.Hat);

            var errors = RunFullBuild();

            Assert.That(HasError(errors, "ad.info.menu_split"), Is.True);
            var many = ControlNamed(avatar.Descriptor.expressionsMenu, "Many").subMenu;
            Assert.That(many.controls.Count, Is.EqualTo(VRCExpressionsMenu.MAX_CONTROLS));
            Assert.That(many.controls.Last().name, Is.EqualTo("More"));
            Assert.That(many.controls.Last().subMenu.controls.Count, Is.EqualTo(2));
        }

        [Test]
        public void DefaultOnToggleStartsItsPinLayerOn()
        {
            var menu = avatar.AddMenu();
            var hat = Toggle(menu, "Hat", avatar.Hat);
            hat.defaultOn = true;
            hat.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 70f, mode = ShapeEffectMode.Pin });

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            Assert.That(Layer(fx, "AD: Hat").stateMachine.defaultState.name, Is.EqualTo("On"));
            Assert.That(Layer(fx, "AD: Hat (pin)").stateMachine.defaultState.name, Is.EqualTo("On"), "the pin layer follows its main layer");
        }

        [Test]
        public void ParameterProviderListsThisComponentsParameters()
        {
            var menu = avatar.AddMenu();
            var hat = Toggle(menu, "Hat", avatar.Hat); hat.defaultOn = true;
            var eyes = menu.AddEntry(EntryKind.Radial, "Eyes"); eyes.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big" }); eyes.synced = false;

            var provided = ParameterInfo.ForUI.GetParametersForObject(avatar.Root).Where(p => p.Source == menu).ToList();

            var hatParam = provided.Single(p => p.EffectiveName == "AD/Hat");
            Assert.That(hatParam.ParameterType, Is.EqualTo(AnimatorControllerParameterType.Bool));
            Assert.That(hatParam.WantSynced, Is.True);
            Assert.That(hatParam.DefaultValue, Is.EqualTo(1f));
            Assert.That(hatParam.BitUsage, Is.EqualTo(1));
            var eyesParam = provided.Single(p => p.EffectiveName == "AD/Eyes");
            Assert.That(eyesParam.WantSynced, Is.False);
            Assert.That(eyesParam.BitUsage, Is.EqualTo(0));
        }

        [Test]
        public void DuplicatedComponentsStillBindEachControlToItsOwnParameter()
        {
            var menu = avatar.AddMenu();
            var hat = Toggle(menu, "Hat", avatar.Hat);
            var host = new GameObject("Copy"); host.transform.SetParent(avatar.Root.transform);
            var copy = avatar.AddMenu(host);
            var copied = copy.AddEntry(EntryKind.Toggle, "Hat");
            copied.id = hat.id;
            copied.objects.Add(new ObjectTarget { target = avatar.Glasses });

            var errors = RunFullBuild();

            Assert.That(HasError(errors, "ad.warn.duplicate_entry_id"), Is.True);
            var fx = FxOf(avatar);
            Assert.That(FloatValue(State(Layer(fx, "AD: Hat"), "On").motion, "Hat", typeof(GameObject), "m_IsActive"), Is.EqualTo(1f));
            Assert.That(FloatValue(State(Layer(fx, "AD: Hat (2)"), "On").motion, "Glasses", typeof(GameObject), "m_IsActive"), Is.EqualTo(1f));
            var parameters = avatar.Descriptor.expressionParameters.parameters.Select(p => p.name).ToArray();
            Assert.That(parameters, Does.Contain("AD/Hat"));
            Assert.That(parameters, Does.Contain("AD/Hat 2"));
            var hatControls = avatar.Descriptor.expressionsMenu.controls.Where(c => c.name == "Hat").ToList();
            Assert.That(hatControls.Count, Is.EqualTo(2));
            Assert.That(hatControls.Select(c => c.parameter.name), Is.EquivalentTo(new[] { "AD/Hat", "AD/Hat 2" }));
        }

        [Test]
        public void AnimatorOnlyCustomNameIsNotAddedToExpressionParameters()
        {
            avatar.Fx.AddParameter("GestureRight", AnimatorControllerParameterType.Int);
            var menu = avatar.AddMenu();
            var hat = Toggle(menu, "Hat", avatar.Hat);
            hat.parameterName = "GestureRight";
            hat.parameterValue = 5;

            // Built the way a creator without VRCFury builds, where an overrun would be this error.
            var detect = ParametersWriter.VrcFuryInstalled;
            ParametersWriter.VrcFuryInstalled = () => false;
            List<ErrorContext> errors;
            try
            {
                errors = RunFullBuild();
            }
            finally
            {
                ParametersWriter.VrcFuryInstalled = detect;
            }

            Assert.That(HasError(errors, "ad.error.bits_exceeded"), Is.False);
            Assert.That(avatar.Descriptor.expressionParameters.parameters.Select(p => p.name), Is.EqualTo(new[] { "Existing" }));
            var fx = FxOf(avatar);
            var toOn = State(Layer(fx, "AD: Hat"), "Off").transitions.Single();
            Assert.That(toOn.conditions.Single().mode, Is.EqualTo(AnimatorConditionMode.Equals));
            Assert.That(toOn.conditions.Single().threshold, Is.EqualTo(5f));
            Assert.That(fx.parameters.Count(p => p.name == "GestureRight"), Is.EqualTo(1));
            Assert.That(fx.parameters.Single(p => p.name == "GestureRight").type, Is.EqualTo(AnimatorControllerParameterType.Int));
        }

        [Test]
        public void TargetOutsideTheAvatarAnimatesNothing()
        {
            var stray = new GameObject("Stray");
            try
            {
                var menu = avatar.AddMenu();
                Toggle(menu, "Stray", stray);

                var fx = BuildAnimator(out _, menu);

                var on = (AnimationClip)State(Layer(fx, "AD: Stray"), "On").motion;
                Assert.That(AnimationUtility.GetEditorCurve(on, EditorCurveBinding.FloatCurve("Stray", typeof(GameObject), "m_IsActive")), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(stray);
            }
        }

        [Test]
        public void ParameterProviderDedupesAcrossComponents()
        {
            var first = avatar.AddMenu();
            Toggle(first, "Hat", avatar.Hat);
            var host = new GameObject("Second"); host.transform.SetParent(avatar.Root.transform);
            var second = avatar.AddMenu(host);
            Toggle(second, "Hat", avatar.Glasses);

            var provided = ParameterInfo.ForUI.GetParametersForObject(avatar.Root).Where(p => p.Source is AvatarDoc).ToList();

            Assert.That(provided.Count(p => p.EffectiveName == "AD/Hat"), Is.EqualTo(1));
            Assert.That(provided.Count(p => p.EffectiveName == "AD/Hat 2"), Is.EqualTo(1));
        }

        [Test]
        public void CountBitsSeparatesAvatarDoctorFromTheRest()
        {
            var menu = avatar.AddMenu();
            Toggle(menu, "Hat", avatar.Hat);

            var (avatarBits, avatarDoctorBits) = ParameterPlanner.CountBits(avatar.Root.transform);

            Assert.That(avatarBits, Is.EqualTo(1));
            Assert.That(avatarDoctorBits, Is.EqualTo(1));
        }

        [Test]
        public void GroupMembersSharingATargetLetTheOnValueWin()
        {
            var menu = avatar.AddMenu();
            var a = Toggle(menu, "A", avatar.Hat); a.exclusiveGroup = "G";
            var b = Toggle(menu, "B", avatar.Hat); b.exclusiveGroup = "G";

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            var layer = Layer(fx, "AD: G");
            Assert.That(FloatValue(State(layer, "A").motion, "Hat", typeof(GameObject), "m_IsActive"), Is.EqualTo(1f));
            Assert.That(FloatValue(State(layer, "B").motion, "Hat", typeof(GameObject), "m_IsActive"), Is.EqualTo(1f));
            Assert.That(FloatValue(State(layer, "Off").motion, "Hat", typeof(GameObject), "m_IsActive"), Is.EqualTo(0f));
        }

        [Test]
        public void MaterialSwapOnAMeshRendererUsesTheRendererType()
        {
            var renderer = avatar.Hat.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { avatar.MaterialA };
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.materials.Add(new MaterialTarget { renderer = renderer, slot = 0, material = avatar.MaterialB });

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            var on = State(Layer(fx, "AD: Hat"), "On").motion;
            Assert.That(ObjectValue(on, "Hat", typeof(MeshRenderer), "m_Materials.Array.data[0]"), Is.SameAs(avatar.MaterialB));
        }

        bool WriteDefaultsOfFx()
        {
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            bool result = AnimatorBuilder.DecideWriteDefaults(asc.ControllerContext.Controllers[VRCAvatarDescriptor.AnimLayerType.FX]);
            context.DeactivateAllExtensionContexts();
            return result;
        }

        [Test]
        public void AdditiveLayersDoNotCountForWriteDefaults()
        {
            avatar.Fx.AddLayer("Additive");
            var layers = avatar.Fx.layers;
            layers[1].blendingMode = AnimatorLayerBlendingMode.Additive;
            avatar.Fx.layers = layers;
            avatar.Fx.layers[1].stateMachine.AddState("Loud").writeDefaultValues = true;

            Assert.That(WriteDefaultsOfFx(), Is.False);
        }

        [Test]
        public void SingleBlendTreeLayersDoNotCountForWriteDefaults()
        {
            avatar.Fx.AddParameter("Blend", AnimatorControllerParameterType.Float);
            avatar.Fx.AddLayer("Tree");
            var state = avatar.Fx.layers[1].stateMachine.AddState("Blend");
            state.writeDefaultValues = true;
            state.motion = new BlendTree { name = "Tree", blendType = BlendTreeType.Simple1D, blendParameter = "Blend" };

            Assert.That(WriteDefaultsOfFx(), Is.False);
        }

        [Test]
        public void WholePipelineProducesAWorkingAvatar()
        {
            var menu = avatar.AddMenu();
            Toggle(menu, "Hat", avatar.Hat);
            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                var descriptor = processed.GetComponent<VRCAvatarDescriptor>();
                var fx = (AnimatorController)descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
                Assert.That(fx.layers.Any(l => l.name == "AD: Hat"), Is.True);
                Assert.That(descriptor.expressionParameters.parameters.Any(p => p.name == "AD/Hat"), Is.True);
                Assert.That(descriptor.expressionsMenu.controls.Any(c => c.name == "Hat"), Is.True);
                Assert.That(processed.GetComponentsInChildren<AvatarDoc>(true), Is.Empty);
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                GeneratedAssets.CleanUp();
            }
        }

        /// <summary>Adds a layer with one state. An empty layer does not survive a full pipeline run, so a test that wants to see a layer at the end has to give it something to hold.</summary>
        static void AddLayerWithAState(AnimatorController controller, string name)
        {
            controller.AddLayer(name);
            var layers = controller.layers;
            layers[layers.Length - 1].name = name;
            layers[layers.Length - 1].stateMachine.AddState("Idle");
            controller.layers = layers;
        }

        [Test]
        public void RemovedLayersLeaveTheBuiltController()
        {
            AddLayerWithAState(avatar.Fx, "OldLayer");
            var menu = avatar.AddMenu();
            Toggle(menu, "Hat", avatar.Hat);
            menu.removedLayers.Add(new RemovedLayer { layer = "OldLayer" });

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                var descriptor = processed.GetComponent<VRCAvatarDescriptor>();
                var fx = (AnimatorController)descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
                Assert.That(fx.layers.Any(l => l.name == "OldLayer"), Is.False, "the hand made layer was removed");
                Assert.That(fx.layers.Any(l => l.name == "AD: Hat"), Is.True, "the generated layer still exists");
                Assert.That(descriptor.expressionParameters.parameters.Count(p => p.name == "Existing"), Is.EqualTo(1), "the untouched parameter was not duplicated");
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                GeneratedAssets.CleanUp();
            }
        }

        [Test]
        public void RemovedLayersStayWhenTheModelFails()
        {
            AddLayerWithAState(avatar.Fx, "OldLayer");
            var menu = avatar.AddMenu();
            var first = Toggle(menu, "A", avatar.Hat);
            first.exclusiveGroup = "Outfits";
            first.groupValue = 1;
            var second = Toggle(menu, "B", avatar.Glasses);
            second.exclusiveGroup = "Outfits";
            second.groupValue = 1;
            menu.removedLayers.Add(new RemovedLayer { layer = "OldLayer" });

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                var fx = (AnimatorController)processed.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers
                    .First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
                Assert.That(fx.layers.Any(l => l.name == "OldLayer"), Is.True, "a build that stops on an error changes nothing");
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                GeneratedAssets.CleanUp();
            }
        }

        [Test]
        public void MissingRemovedLayerWarns()
        {
            var menu = avatar.AddMenu();
            menu.removedLayers.Add(new RemovedLayer { layer = "Ghost" });
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var messages = new List<ModelMessage>();

            LayerRemover.Run(asc, new List<AvatarDoc> { menu }, messages);
            context.DeactivateAllExtensionContexts();

            var warning = messages.Single();
            Assert.That(warning.Key, Is.EqualTo("ad.warn.removed_layer_missing"));
            Assert.That(warning.Args[0], Is.EqualTo("Ghost"));
        }

        /// <summary>Adds a layer that really carries this name: AnimatorController.AddLayer makes the name unique by itself, which is exactly what the duplicate-name tests must not get.</summary>
        static void AddLayerNamed(AnimatorController controller, string name)
        {
            controller.AddLayer(name);
            var layers = controller.layers;
            layers[layers.Length - 1].name = name;
            controller.layers = layers;
        }

        [Test]
        public void LayerRemoverRefusesAmbiguousNames()
        {
            AddLayerNamed(avatar.Fx, "Toggle");
            AddLayerNamed(avatar.Fx, "Toggle");
            int before = avatar.Fx.layers.Length;
            var menu = avatar.AddMenu();
            menu.removedLayers.Add(new RemovedLayer { layer = "Toggle" });
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var messages = new List<ModelMessage>();

            LayerRemover.Run(asc, new List<AvatarDoc> { menu }, messages);
            context.DeactivateAllExtensionContexts();

            var fx = (AnimatorController)avatar.Descriptor.baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            Assert.That(fx.layers.Length, Is.EqualTo(before), "neither layer was removed");
            var warning = messages.Single();
            Assert.That(warning.Key, Is.EqualTo("ad.warn.removed_layer_ambiguous"));
            Assert.That(warning.Args[0], Is.EqualTo("Toggle"));
        }

        [Test]
        public void LayerRemoverRefusesWithLayerControl()
        {
            avatar.Fx.AddLayer("OldLayer");
            var idleState = avatar.Fx.layers[0].stateMachine.states[0].state;
            var layerControl = idleState.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
            layerControl.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
            layerControl.layer = 0;
            var menu = avatar.AddMenu();
            menu.removedLayers.Add(new RemovedLayer { layer = "OldLayer" });

            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var messages = new List<ModelMessage>();

            LayerRemover.Run(asc, new List<AvatarDoc> { menu }, messages);
            context.DeactivateAllExtensionContexts();

            var fx = (AnimatorController)avatar.Descriptor.baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            Assert.That(fx.layers.Any(l => l.name == "OldLayer"), Is.True, "the layer was not removed");
            var warning = messages.Single();
            Assert.That(warning.Key, Is.EqualTo("ad.warn.converted_layer_unsafe"));
            Assert.That(warning.Args[0], Is.EqualTo("OldLayer"));
            Assert.That(warning.Args[1], Is.SameAs(menu), "and it names the component that asked for the removal");
        }
    }
}
