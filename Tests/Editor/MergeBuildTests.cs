#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using Object = UnityEngine.Object;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Merged animator controllers and parameter controls: layers, paths, write defaults, parameters and layer control.</summary>
    public class MergeBuildTests
    {
        TestAvatar avatar;
        AvatarDoc menu;
        readonly List<Object> extras = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            GeneratedAssets.Note();
            avatar = TestAvatar.Create();
            menu = avatar.AddMenu();
            Platform.OverrideMobile = false;
            AvatarPlanCache.Invalidate();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            foreach (var extra in extras)
                if (extra != null && !AssetDatabase.Contains(extra)) Object.DestroyImmediate(extra);
            extras.Clear();
            Platform.OverrideMobile = null;
            AvatarPlanCache.Invalidate();
            GeneratedAssets.CleanUp();
        }

        AnimatorController Controller(string name)
        {
            var controller = new AnimatorController { name = name };
            extras.Add(controller);
            return controller;
        }

        AnimationClip FloatClip(string name, string path, System.Type type, string property, float value)
        {
            var clip = new AnimationClip { name = name };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property), AnimationCurve.Constant(0f, 1f, value));
            extras.Add(clip);
            return clip;
        }

        /// <summary>Adds a layer with one state holding this motion, and returns that state.</summary>
        static AnimatorState AddLayer(AnimatorController controller, string layerName, Motion motion, bool writeDefaults)
        {
            controller.AddLayer(layerName);
            var layers = controller.layers;
            var machine = layers[layers.Length - 1].stateMachine;
            var state = machine.AddState(layerName + " state");
            state.motion = motion;
            state.writeDefaultValues = writeDefaults;
            machine.defaultState = state;
            return state;
        }

        AnimatorMerge AddMerge(AvatarDoc doc, AnimatorController controller)
        {
            var merge = AnimatorMerge.Create(controller);
            doc.animators.Add(merge);
            return merge;
        }

        static AnimatorController FxOf(TestAvatar a)
        {
            return (AnimatorController)a.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
        }

        static MenuTree Model(TestAvatar a, AvatarDoc doc)
        {
            return MenuModel.Build(a.Root.transform, a.ExpressionsMenu, new[] { doc });
        }

        /// <summary>Plans and merges the way the build step does, then commits, so the committed FX controller can be read.</summary>
        static List<ModelMessage> RunMerge(TestAvatar a, AvatarDoc doc, out AnimatorController fx)
        {
            var context = new BuildContext(a.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var tree = MenuModel.Build(a.Root.transform, a.ExpressionsMenu, new[] { doc });
            var plan = ParameterPlanner.Plan(tree, new Dictionary<string, PlannedType> { { "Existing", PlannedType.Bool } });
            var virtualFx = asc.ControllerContext.Controllers[VRCAvatarDescriptor.AnimLayerType.FX];
            var messages = MergeBuilder.Build(asc, tree, plan, AnimatorBuilder.DecideWriteDefaults(virtualFx));
            context.DeactivateAllExtensionContexts();
            fx = FxOf(a);
            return messages;
        }

        static List<ErrorContext> RunFullBuild(TestAvatar a)
        {
            var context = new BuildContext(a.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            return errors;
        }

        static AnimatorState SingleState(AnimatorController fx, string layerName)
        {
            return fx.layers.Single(l => l.name == layerName).stateMachine.states.Single().state;
        }

        static List<string> Paths(Motion motion)
        {
            var clip = motion as AnimationClip;
            if (clip == null) return new List<string>();
            return AnimationUtility.GetCurveBindings(clip).Select(b => b.path).ToList();
        }

        [Test]
        public void MergedLayersJoinTheFxController()
        {
            const string folder = GeneratedAssets.Folder;
            GeneratedAssets.Ensure();
            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Shading_FX.controller");
            var clip = TestAvatar.ShapeClip("Shine", "Body", "Blush", 0f, 100f);
            AssetDatabase.AddObjectToAsset(clip, controller);
            AddLayer(controller, "Shine", clip, true);
            AddLayer(controller, "Grayscale", clip, true);
            AssetDatabase.SaveAssets();
            int layersBefore = controller.layers.Length;
            var bindingsBefore = AnimationUtility.GetCurveBindings(clip).Select(b => b.path + ":" + b.propertyName).ToList();

            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            AddMerge(menu, controller);

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                var fx = (AnimatorController)processed.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers
                    .Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
                var names = fx.layers.Select(l => l.name).ToList();

                Assert.That(names, Does.Contain("Shine"));
                Assert.That(names, Does.Contain("Grayscale"));
                Assert.That(names.IndexOf("Shine"), Is.GreaterThan(names.IndexOf("AD: Hat")), "merged layers come after AvatarDoc's own");
                Assert.That(names.IndexOf("Grayscale"), Is.EqualTo(names.IndexOf("Shine") + 1), "merged layers keep their relative order");
                Assert.That(controller.layers.Length, Is.EqualTo(layersBefore), "the controller asset keeps its layers");
                Assert.That(AnimationUtility.GetCurveBindings(clip).Select(b => b.path + ":" + b.propertyName).ToList(),
                    Is.EqualTo(bindingsBefore), "the clip asset keeps its bindings");
                Assert.That(controller.layers.Single(l => l.name == "Shine").stateMachine.states.Single().state.writeDefaultValues,
                    Is.True, "the controller asset keeps its own write defaults, although the avatar builds with them off");
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void PathRootPrefixesMergedClipPaths()
        {
            var controller = Controller("Tint_FX");
            controller.AddParameter("Shading/Tint", AnimatorControllerParameterType.Float);
            var shape = FloatClip("Shape", "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush", 100f);
            var self = FloatClip("Self", "", typeof(GameObject), "m_IsActive", 1f);
            var blend = new BlendTree { name = "Tint tree", blendType = BlendTreeType.Simple1D, blendParameter = "Shading/Tint", useAutomaticThresholds = false };
            blend.AddChild(shape, 0f);
            blend.AddChild(self, 1f);
            extras.Add(blend);
            AddLayer(controller, "Tint", blend, true);

            var merge = AddMerge(menu, controller);
            merge.pathRoot = avatar.Hat.transform;

            RunMerge(avatar, menu, out var fx);

            var merged = (BlendTree)SingleState(fx, "Tint").motion;
            Assert.That(Paths(merged.children[0].motion), Is.EqualTo(new[] { "Hat/Body" }), "a blend tree child is prefixed too");
            Assert.That(Paths(merged.children[1].motion), Is.EqualTo(new[] { "Hat" }), "the path root itself is what an empty path means");
            Assert.That(AnimationUtility.GetCurveBindings(shape).Select(b => b.path), Is.EqualTo(new[] { "Body" }), "the source clip is untouched");
        }

        [Test]
        public void SkippedLayersStayOut()
        {
            var controller = Controller("Two_FX");
            var clip = FloatClip("A", "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush", 50f);
            AddLayer(controller, "Kept", clip, true);
            AddLayer(controller, "Dropped", clip, true);

            var merge = AddMerge(menu, controller);
            merge.skipLayers.Add("Dropped");

            RunMerge(avatar, menu, out var fx);

            var names = fx.layers.Select(l => l.name).ToList();
            Assert.That(names, Does.Contain("Kept"));
            Assert.That(names, Does.Not.Contain("Dropped"));
        }

        [Test]
        public void MissingSkipNameWarns()
        {
            var controller = Controller("One_FX");
            var clip = FloatClip("A", "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush", 50f);
            AddLayer(controller, "Kept", clip, true);
            var merge = AddMerge(menu, controller);
            merge.skipLayers.Add("Nope");

            var tree = Model(avatar, menu);

            var message = tree.Messages.Single(m => m.Key == "ad.warn.merge_skip_missing");
            Assert.That(message.Args[0], Is.EqualTo("Nope"));
            Assert.That(message.EntryIds, Does.Contain(merge.id));
        }

        [Test]
        public void WriteDefaultsFollowTheAvatar()
        {
            var controller = Controller("Wd_FX");
            controller.AddParameter("Weight", AnimatorControllerParameterType.Float);
            var clip = FloatClip("A", "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush", 50f);
            AddLayer(controller, "Plain", clip, true);
            var direct = new BlendTree { name = "Direct tree", blendType = BlendTreeType.Direct };
            direct.AddChild(clip);
            var children = direct.children;
            children[0].directBlendParameter = "Weight";
            direct.children = children;
            extras.Add(direct);
            AddLayer(controller, "Direct", direct, false);

            AddMerge(menu, controller);
            RunMerge(avatar, menu, out var offFx);

            Assert.That(SingleState(offFx, "Plain").writeDefaultValues, Is.False, "the avatar builds with write defaults off");
            Assert.That(SingleState(offFx, "Direct").writeDefaultValues, Is.True, "a single state direct blend tree layer keeps write defaults on");

            using (var on = TestAvatar.Create(writeDefaultsOff: false))
            {
                var doc = on.AddMenu();
                AddMerge(doc, controller);

                RunMerge(on, doc, out var onFx);

                Assert.That(SingleState(onFx, "Plain").writeDefaultValues, Is.True, "the other avatar builds with write defaults on");
                Assert.That(SingleState(onFx, "Direct").writeDefaultValues, Is.True);
            }

            Assert.That(controller.layers.Single(l => l.name == "Direct").stateMachine.states.Single().state.writeDefaultValues,
                Is.False, "the controller asset keeps its own write defaults");
        }

        [Test]
        public void MergedParametersAreAddedAndTypeClashesAreErrors()
        {
            avatar.Fx.AddParameter("Existing", AnimatorControllerParameterType.Bool);
            var controller = Controller("Params_FX");
            controller.AddParameter("Shading/Tint", AnimatorControllerParameterType.Float);
            controller.AddParameter("Existing", AnimatorControllerParameterType.Float);
            var clip = FloatClip("A", "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush", 50f);
            AddLayer(controller, "Tint", clip, true);
            var merge = AddMerge(menu, controller);

            var messages = RunMerge(avatar, menu, out var fx);

            var added = fx.parameters.Single(p => p.name == "Shading/Tint");
            Assert.That(added.type, Is.EqualTo(AnimatorControllerParameterType.Float));
            Assert.That(fx.parameters.Single(p => p.name == "Existing").type, Is.EqualTo(AnimatorControllerParameterType.Bool), "the avatar's own declaration stays");
            var clash = messages.Single(m => m.Key == "ad.error.merge_parameter_type");
            Assert.That(clash.Severity, Is.EqualTo(MessageSeverity.Error));
            Assert.That(clash.Args[1], Is.EqualTo("Existing"));
            Assert.That(clash.EntryIds, Does.Contain(merge.id));
        }

        [Test]
        public void PathRootOutsideTheAvatarIsAnErrorAndMergesNothing()
        {
            var stranger = new GameObject("OtherAvatar");
            extras.Add(stranger);
            var controller = Controller("Outside_FX");
            var clip = FloatClip("A", "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush", 50f);
            AddLayer(controller, "Outside", clip, true);
            var merge = AddMerge(menu, controller);
            merge.pathRoot = stranger.transform;

            var tree = Model(avatar, menu);
            var outside = tree.Messages.Single(m => m.Key == "ad.error.merge_outside_avatar");
            Assert.That(outside.Severity, Is.EqualTo(MessageSeverity.Error));
            Assert.That(outside.EntryIds, Does.Contain(merge.id));

            RunMerge(avatar, menu, out var fx);

            Assert.That(fx.layers.Select(l => l.name), Does.Not.Contain("Outside"), "nothing outside the avatar is ever followed");
        }

        [Test]
        public void AnOverrideControllerWithNoControllerIsSkippedWithAWarning()
        {
            // An Animator Override Controller whose own "Controller" field is empty: there is nothing to merge.
            var empty = new AnimatorOverrideController { name = "Empty override" };
            extras.Add(empty);
            var merge = AnimatorMerge.Create(empty);
            menu.animators.Add(merge);
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });

            var tree = Model(avatar, menu);
            var said = tree.Messages.Where(m => m.Key == "ad.warn.merge_override_no_controller").ToList();
            Assert.That(said.Count, Is.EqualTo(1), "the inspector says so before any upload");
            Assert.That(said[0].Severity, Is.EqualTo(MessageSeverity.Warning));
            Assert.That(said[0].EntryIds, Does.Contain(merge.id));

            List<ErrorContext> errors = null;
            Assert.DoesNotThrow(() => errors = RunFullBuild(avatar), "the merge must not stop the pass halfway");

            Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error), Is.Empty, "nothing about it blocks the upload");
            Assert.That(errors.Count(e => e.TheError is SimpleError s && s.TitleKey == "ad.warn.merge_override_no_controller"), Is.EqualTo(1), "the build reports it once");
            Assert.That(avatar.Descriptor.expressionParameters.parameters.Select(p => p.name), Does.Contain("AD/Hat"), "the parameters are written");
            Assert.That(avatar.Descriptor.expressionsMenu, Is.Not.SameAs(avatar.ExpressionsMenu), "and so is the menu");
            Assert.That(avatar.Root.GetComponentsInChildren<AvatarDoc>(true), Is.Empty, "the pass ran to its end");
        }

        [Test]
        public void ExcludedMergeAddsNoLayersAndChangesNoParameter()
        {
            var controller = Controller("Quest_FX");
            controller.AddParameter("Shading/Tint", AnimatorControllerParameterType.Float);
            var clip = FloatClip("A", "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush", 50f);
            AddLayer(controller, "Desk only", clip, true);

            var control = menu.AddEntry(EntryKind.Radial, "Tint");
            control.parameterName = "Shading/Tint";
            var merge = AddMerge(menu, controller);
            merge.platform = EntryPlatform.PCOnly;

            Platform.OverrideMobile = false;
            RunFullBuild(avatar);
            var pcParameters = avatar.Descriptor.expressionParameters.parameters
                .Select(p => p.name + "|" + p.valueType + "|" + p.networkSynced).ToList();
            Assert.That(FxOf(avatar).layers.Select(l => l.name), Does.Contain("Desk only"));

            using (var quest = TestAvatar.Create())
            {
                var doc = quest.AddMenu();
                var questControl = doc.AddEntry(EntryKind.Radial, "Tint");
                questControl.parameterName = "Shading/Tint";
                var questMerge = AddMerge(doc, controller);
                questMerge.platform = EntryPlatform.PCOnly;

                Platform.OverrideMobile = true;
                RunFullBuild(quest);

                Assert.That(FxOf(quest).layers.Select(l => l.name), Does.Not.Contain("Desk only"), "an excluded merge adds no layer");
                Assert.That(quest.Descriptor.expressionParameters.parameters
                    .Select(p => p.name + "|" + p.valueType + "|" + p.networkSynced).ToList(),
                    Is.EqualTo(pcParameters), "the parameter list is identical on both platforms");
            }
        }

        [Test]
        public void ParameterControlGetsAControlAndNoLayer()
        {
            var controller = Controller("Ltcgi_FX");
            controller.AddParameter("Shading/LTCGI", AnimatorControllerParameterType.Bool);
            var clip = FloatClip("A", "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush", 50f);
            AddLayer(controller, "LTCGI", clip, true);
            AddMerge(menu, controller);

            var control = menu.AddEntry(EntryKind.Toggle, "LTCGI");
            control.parameterName = "Shading/LTCGI";
            Assert.That(control.IsParameterControl, Is.True);

            RunFullBuild(avatar);

            Assert.That(avatar.Descriptor.expressionParameters.parameters.Select(p => p.name), Does.Contain("Shading/LTCGI"));
            var written = avatar.Descriptor.expressionsMenu.controls.Single(c => c.name == "LTCGI");
            Assert.That(written.parameter.name, Is.EqualTo("Shading/LTCGI"));
            Assert.That(FxOf(avatar).layers.Select(l => l.name), Does.Not.Contain("AD: LTCGI"), "the merged animator holds the effect, so there is no layer of our own");
        }

        [Test]
        public void ParameterControlWithoutAUserWarns()
        {
            var control = menu.AddEntry(EntryKind.Toggle, "LTCGI");
            control.parameterName = "Shading/LTCGI";

            var messages = AvatarPlanCache.GetFor(menu).Messages;

            var warning = messages.Single(m => m.Key == "ad.warn.parameter_control_unused");
            Assert.That(warning.EntryIds, Does.Contain(control.id));
            Assert.That(messages.Any(m => m.Key == "ad.warn.no_effects" && m.EntryIds.Contains(control.id)), Is.False,
                "the parameter control message replaces the empty entry warning");
        }

        [Test]
        public void ParameterControlUsedByAMergeIsQuiet()
        {
            var controller = Controller("Ltcgi_FX");
            controller.AddParameter("Shading/LTCGI", AnimatorControllerParameterType.Bool);
            var clip = FloatClip("A", "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush", 50f);
            AddLayer(controller, "LTCGI", clip, true);
            AddMerge(menu, controller);

            var control = menu.AddEntry(EntryKind.Toggle, "LTCGI");
            control.parameterName = "Shading/LTCGI";

            var messages = AvatarPlanCache.GetFor(menu).Messages;

            Assert.That(messages.Any(m => m.Key == "ad.warn.parameter_control_unused"), Is.False);
            Assert.That(messages.Any(m => m.Key == "ad.warn.no_effects"), Is.False);
        }

        [Test]
        public void LayerControlIndicesAreRemapped()
        {
            var controller = Controller("Control_FX");
            var clip = FloatClip("A", "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush", 50f);
            var driver = AddLayer(controller, "Driver", clip, true);
            AddLayer(controller, "Driven", clip, true);
            var behaviour = driver.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
            behaviour.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
            behaviour.layer = 1;
            behaviour.goalWeight = 1f;

            AddMerge(menu, controller);

            RunMerge(avatar, menu, out var fx);

            var names = fx.layers.Select(l => l.name).ToList();
            var committed = SingleState(fx, "Driver").behaviours.OfType<VRCAnimatorLayerControl>().Single();
            Assert.That(committed.layer, Is.EqualTo(names.IndexOf("Driven")), "the index points at where the merged layer ended up");
            Assert.That(behaviour.layer, Is.EqualTo(1), "the controller asset keeps its own index");
        }

        [Test]
        public void LayerControlWithSkippedLayersRefuses()
        {
            var controller = Controller("Control_FX");
            var clip = FloatClip("A", "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush", 50f);
            var driver = AddLayer(controller, "Driver", clip, true);
            AddLayer(controller, "Driven", clip, true);
            var behaviour = driver.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
            behaviour.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
            behaviour.layer = 1;

            var merge = AddMerge(menu, controller);
            merge.skipLayers.Add("Driven");

            var messages = RunMerge(avatar, menu, out var fx);

            var refused = messages.Single(m => m.Key == "ad.error.merge_layer_control");
            Assert.That(refused.Severity, Is.EqualTo(MessageSeverity.Error));
            Assert.That(refused.EntryIds, Does.Contain(merge.id));
            Assert.That(fx.layers.Select(l => l.name), Does.Not.Contain("Driver"), "the whole merge is refused, not only the skipped layer");
        }

        [Test]
        public void ParameterControlAdoptsTheMergedControllersType()
        {
            var controller = Controller("Adopt_FX");
            controller.AddParameter("Shading/Tint", AnimatorControllerParameterType.Float);
            var clip = FloatClip("A", "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush", 50f);
            AddLayer(controller, "Tint", clip, true);
            AddMerge(menu, controller);

            var control = menu.AddEntry(EntryKind.Toggle, "Tint");
            control.parameterName = "Shading/Tint";

            var errors = RunFullBuild(avatar);

            Assert.That(errors.Where(e => e.TheError.Severity == ErrorSeverity.Error), Is.Empty, "VRChat converts the types, so there is nothing to report");
            Assert.That(FxOf(avatar).parameters.Single(p => p.name == "Shading/Tint").type, Is.EqualTo(AnimatorControllerParameterType.Float),
                "the merged controller's type is the one the animator needs");
            var written = avatar.Descriptor.expressionParameters.parameters.Single(p => p.name == "Shading/Tint");
            Assert.That(written.valueType, Is.EqualTo(VRCExpressionParameters.ValueType.Bool), "the menu control still syncs a bool");
            Assert.That(written.networkSynced, Is.True);
        }

        [Test]
        public void GroupMemberWithACustomNameAndNoTargetsStillBuilds()
        {
            var none = menu.AddEntry(EntryKind.Toggle, "None");
            none.exclusiveGroup = "Outfits";
            none.parameterName = "Ignored";
            none.defaultOn = true;
            var dress = menu.AddEntry(EntryKind.Toggle, "Dress");
            dress.exclusiveGroup = "Outfits";
            dress.objects.Add(new ObjectTarget { target = avatar.Hat });
            Assert.That(none.IsParameterControl, Is.False, "the group owns its members' parameter");

            var tree = Model(avatar, menu);
            Assert.That(tree.Messages.Any(m => m.EntryIds.Contains(none.id)), Is.False, "the off choice of a group says nothing");

            var errors = RunFullBuild(avatar);

            Assert.That(errors.Where(e => e.TheError.Severity == ErrorSeverity.Error), Is.Empty);
            Assert.That(avatar.Descriptor.expressionParameters.parameters.Select(p => p.name),
                Is.EqualTo(new[] { "Existing", "AD/Outfits" }), "the custom name on a member is ignored, as it always was");
            var states = FxOf(avatar).layers.Single(l => l.name == "AD: Outfits").stateMachine.states.Select(s => s.state.name).ToList();
            Assert.That(states, Does.Contain("None"));
            Assert.That(states, Does.Contain("Dress"));
            Assert.That(avatar.Hat.activeSelf, Is.False, "the group's default wrote the other member off in the scene");
        }

        [Test]
        public void PathRootPrefixesPlayAudioPaths()
        {
            var controller = Controller("Audio_FX");
            var clip = FloatClip("A", "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush", 50f);
            var state = AddLayer(controller, "Audio", clip, true);
            var audio = state.AddStateMachineBehaviour<VRCAnimatorPlayAudio>();
            audio.SourcePath = "Speaker";

            var merge = AddMerge(menu, controller);
            merge.pathRoot = avatar.Hat.transform;

            RunMerge(avatar, menu, out var fx);

            var committed = SingleState(fx, "Audio").behaviours.OfType<VRCAnimatorPlayAudio>().Single();
            Assert.That(committed.SourcePath, Is.EqualTo("Hat/Speaker"));
            Assert.That(audio.SourcePath, Is.EqualTo("Speaker"), "the controller asset keeps its own path");
        }

        [Test]
        public void EditingTheControllerAssetRefreshesItsParameters()
        {
            const string folder = GeneratedAssets.Folder;
            const string path = folder + "/Edited_FX.controller";
            GeneratedAssets.Ensure();
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            try
            {
                Assert.That(MergeBuilder.ParametersOf(controller).Select(p => p.name), Is.Empty, "nothing is declared yet");

                controller.AddParameter("Shading/Tint", AnimatorControllerParameterType.Float);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(path);

                Assert.That(MergeBuilder.ParametersOf(controller).Select(p => p.name), Is.EqualTo(new[] { "Shading/Tint" }),
                    "what the Animator window added shows up without an editor restart");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void SyncedLayerOnASkippedLayerRefuses()
        {
            var controller = Controller("Synced_FX");
            var clip = FloatClip("A", "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush", 50f);
            AddLayer(controller, "Source", clip, true);
            AddLayer(controller, "Copy", clip, true);
            var layers = controller.layers;
            Assert.That(layers[0].syncedLayerIndex, Is.EqualTo(-1), "a plain layer syncs to nothing");
            layers[1].syncedLayerIndex = 0;
            controller.layers = layers;

            var merge = AddMerge(menu, controller);
            merge.skipLayers.Add("Source");

            var messages = RunMerge(avatar, menu, out var fx);

            var refused = messages.Single(m => m.Key == "ad.error.merge_synced_layer");
            Assert.That(refused.Severity, Is.EqualTo(MessageSeverity.Error));
            Assert.That(refused.Args[1], Is.EqualTo("Copy"));
            Assert.That(refused.Args[2], Is.EqualTo("Source"));
            Assert.That(refused.EntryIds, Does.Contain(merge.id));
            Assert.That(fx.layers.Select(l => l.name), Does.Not.Contain("Copy"), "the whole merge is refused");
        }

        [Test]
        public void DeclaredBoolAdoptsTheMergedFloatType()
        {
            // The shape a takeover of a bought prefab leaves behind: Modular Avatar called the parameter a Bool,
            // the controller reads it as the Float of a blend tree, and nothing of ours reads it at all.
            var controller = Controller("Prefab_FX");
            controller.AddParameter("Prefab/Shine", AnimatorControllerParameterType.Float);
            var low = FloatClip("Shine_0", "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush", 0f);
            var high = FloatClip("Shine_1", "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush", 100f);
            var tree = new BlendTree
            {
                name = "Shine",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Prefab/Shine",
                useAutomaticThresholds = false,
            };
            tree.AddChild(low, 0f);
            tree.AddChild(high, 1f);
            extras.Add(tree);
            AddLayer(controller, "Shine", tree, true);
            AddMerge(menu, controller);
            menu.parameters.Add(ParameterDeclaration.Create("Prefab/Shine", PlannedType.Bool));

            var errors = RunFullBuild(avatar);

            Assert.That(errors.Any(e => e.TheError is SimpleError s && s.TitleKey == "ad.error.merge_parameter_type"), Is.False,
                "the merged controller's type simply wins");
            Assert.That(errors.Any(e => e.TheError.Severity == ErrorSeverity.Error), Is.False);
            var fx = FxOf(avatar);
            Assert.That(fx.parameters.Single(p => p.name == "Prefab/Shine").type, Is.EqualTo(AnimatorControllerParameterType.Float),
                "the blend tree reads a Float");
            Assert.That(avatar.Descriptor.expressionParameters.parameters.Single(p => p.name == "Prefab/Shine").valueType,
                Is.EqualTo(VRCExpressionParameters.ValueType.Bool), "and the declaration still costs one bit");
        }

        [Test]
        public void DeclarationDoesNotOverrideTheAvatarsOwnFxType()
        {
            // The avatar's own FX already declares Zip as a Bool, and a hand made layer of its own reads it.
            avatar.Fx.AddParameter("Zip", AnimatorControllerParameterType.Bool);
            var own = FloatClip("Zip_On", "Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big", 100f);
            var zipOn = AddLayer(avatar.Fx, "Zip", own, false);
            var zipMachine = avatar.Fx.layers.Single(l => l.name == "Zip").stateMachine;
            var zipOff = zipMachine.AddState("Zip off");
            zipMachine.defaultState = zipOff;
            var toOn = zipOff.AddTransition(zipOn);
            toOn.hasExitTime = false;
            toOn.AddCondition(AnimatorConditionMode.If, 0f, "Zip");

            var controller = Controller("Prefab_FX");
            controller.AddParameter("Zip", AnimatorControllerParameterType.Float);
            AddLayer(controller, "Prefab Zip", FloatClip("Prefab_Zip", "Body", typeof(SkinnedMeshRenderer), "blendShape.Blush", 50f), true);
            AddMerge(menu, controller);
            menu.parameters.Add(ParameterDeclaration.Create("Zip", PlannedType.Bool));

            var errors = RunFullBuild(avatar);

            Assert.That(errors.Any(e => e.TheError is SimpleError s && s.TitleKey == "ad.error.merge_parameter_type"), Is.True,
                "the avatar's own layer reads Zip as a Bool, so the clash is the creator's to settle");
            Assert.That(FxOf(avatar).parameters.Single(p => p.name == "Zip").type, Is.EqualTo(AnimatorControllerParameterType.Bool),
                "the avatar's own declaration stays");
        }
    }
}
#endif
