using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDKBase;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
#if DOCME_MA
using nadena.dev.modular_avatar.core;
#endif

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Remove from avatar: the control leaves the built menu, and with it the parameter and the FX layer nothing else needs.</summary>
    public class RemovalTests
    {
        TestAvatar avatar;
        VRCExpressionsMenu existingRoot;

        [SetUp]
        public void SetUp()
        {
            GeneratedAssets.Note();
            avatar = TestAvatar.Create();
            existingRoot = Menu(Control("Hat", "HatParam"), Control("Ears", "EarParam"));
            existingRoot.name = "Root";
            avatar.Descriptor.expressionsMenu = existingRoot;
            Declare("HatParam", "EarParam");
            AddBoolLayer(avatar.Fx, "HatLayer", "HatParam");
            Platform.OverrideMobile = false;
            AvatarPlanCache.Invalidate();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            if (existingRoot != null) Object.DestroyImmediate(existingRoot);
            Platform.OverrideMobile = null;
            AvatarPlanCache.Invalidate();
            GeneratedAssets.CleanUp();
        }

        static VRCExpressionsMenu Menu(params VRCExpressionsMenu.Control[] controls)
        {
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.controls = controls.ToList();
            return menu;
        }

        static VRCExpressionsMenu.Control Control(string name, string parameter)
        {
            return new VRCExpressionsMenu.Control
            {
                name = name,
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter },
                value = 1,
            };
        }

        /// <summary>Adds bool expression parameters beside the ones the test avatar already declares.</summary>
        void Declare(params string[] names)
        {
            Declare(VRCExpressionParameters.ValueType.Bool, names);
        }

        void Declare(VRCExpressionParameters.ValueType type, params string[] names)
        {
            var list = avatar.ExpressionParameters.parameters.ToList();
            foreach (var name in names)
                list.Add(new VRCExpressionParameters.Parameter { name = name, valueType = type, saved = true, networkSynced = true });
            avatar.ExpressionParameters.parameters = list.ToArray();
        }

        static AnimatorStateMachine AddLayerTo(AnimatorController controller, string layerName)
        {
            controller.AddLayer(layerName);
            var layers = controller.layers;
            layers[layers.Length - 1].defaultWeight = 1f;
            controller.layers = layers;
            return controller.layers[controller.layers.Length - 1].stateMachine;
        }

        /// <summary>A plain two state layer switched by one bool: full weight, no mask, no behaviours, so only the thing under test can keep it.</summary>
        static AnimatorStateMachine AddBoolLayer(AnimatorController controller, string layerName, string parameter)
        {
            if (controller.parameters.All(p => p.name != parameter)) controller.AddParameter(parameter, AnimatorControllerParameterType.Bool);
            var sm = AddLayerTo(controller, layerName);
            var off = sm.AddState("Off");
            var on = sm.AddState("On");
            sm.defaultState = off;
            var toOn = off.AddTransition(on);
            toOn.hasExitTime = false;
            toOn.AddCondition(AnimatorConditionMode.If, 0f, parameter);
            var toOff = on.AddTransition(off);
            toOff.hasExitTime = false;
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0f, parameter);
            return sm;
        }

        AvatarDoc Removing(params string[] names)
        {
            var menu = avatar.AddMenu();
            foreach (var name in names)
                menu.menuEdits.Add(new MenuEdit { id = name + "-edit", source = new List<string> { name }, remove = true });
            return menu;
        }

        MenuTree Tree(params AvatarDoc[] menus) => MenuModel.Build(avatar.Root.transform, avatar.Descriptor.expressionsMenu, menus);

        RemovalPlan Plan(params AvatarDoc[] menus)
        {
            return RemovalPlanner.Plan(Tree(menus), avatar.Descriptor);
        }

        static RemovedParameter Of(RemovalPlan plan, string name) => plan.Parameters.Single(p => p.Name == name);

        AvatarDoctorView View(AvatarDoc menu) => new AvatarDoctorView(new SerializedObject(menu), menu);

        [Test]
        public void RemovedControlFreesItsUnusedParameter()
        {
            var folder = TempAssetFolder.Create("AvatarDocRemovalTests");
            AssetDatabase.CreateAsset(avatar.ExpressionParameters, folder + "/Params.asset");
            AddBoolLayer(avatar.Fx, "EarLayer", "EarParam");
            Removing("Hat");

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                var descriptor = processed.GetComponent<VRCAvatarDescriptor>();

                Assert.That(descriptor.expressionParameters.parameters.Any(p => p.name == "HatParam"), Is.False, "the freed parameter is gone from the built avatar");
                Assert.That(descriptor.expressionParameters.parameters.Any(p => p.name == "EarParam"), Is.True, "every other parameter stays");
                Assert.That(descriptor.expressionsMenu.controls.Any(c => c.name == "Hat"), Is.False, "the control is gone from the built menu");
                var builtFx = (AnimatorController)descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
                Assert.That(builtFx.layers.Any(l => l.name == "HatLayer"), Is.False, "the layer the parameter alone drove is gone from the built controller");
                Assert.That(builtFx.layers.Any(l => l.name == "EarLayer"), Is.True, "the layer of a control that stays is untouched");
                var source = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(folder + "/Params.asset");
                Assert.That(source.parameters.Any(p => p.name == "HatParam"), Is.True, "the source parameters asset is untouched");
                Assert.That(existingRoot.controls.Any(c => c.name == "Hat"), Is.True, "the source menu asset is untouched");
                Assert.That(avatar.Fx.layers.Any(l => l.name == "HatLayer"), Is.True, "the source FX controller is untouched");
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                GeneratedAssets.CleanUp();
                TempAssetFolder.Delete(folder);
                avatar.ExpressionParameters = null;
            }
        }

        [Test]
        public void ABuildLeavesNoReaderCacheBehind()
        {
            Removing("Hat");
            int before = ParameterUsage.AvatarWideCacheCount;

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);

                Assert.That(ParameterUsage.AvatarWideCacheCount, Is.EqualTo(before),
                    "RemovalPrepare reached ParameterUsage.AvatarWideFor for the build's own clone; its reader graph must not outlive the clone it was cached under");
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
            }
        }

        [Test]
        public void ParameterSharedWithAnotherControlStays()
        {
            existingRoot.controls.Add(Control("Hat again", "HatParam"));

            var plan = Plan(Removing("Hat"));

            var parameter = Of(plan, "HatParam");
            Assert.That(parameter.Freed, Is.False);
            Assert.That(parameter.KeptBecause, Is.EqualTo("ad.removal.kept.control"));
            Assert.That(plan.Layers, Is.Empty, "the layer stays with its parameter");
        }

        [Test]
        public void LayerReadsAreWalkedOncePerControllerAcrossRepeatedRemovalPlans()
        {
            var menu = Removing("Hat");
            var tree = Tree(menu);
            int before = LayerAnalysis.LayerReadBuilds;

            var first = RemovalPlanner.Plan(tree, avatar.Descriptor);

            Assert.That(LayerAnalysis.LayerReadBuilds, Is.EqualTo(before + 1), "the first call has to walk the FX controller's layers");
            Assert.That(Of(first, "HatParam").Freed, Is.True);

            var second = RemovalPlanner.Plan(tree, avatar.Descriptor);

            Assert.That(LayerAnalysis.LayerReadBuilds, Is.EqualTo(before + 1), "a second call against the same controller reuses what the first walked");
            Assert.That(Of(second, "HatParam").Freed, Is.True, "the reused reads still give the same, correct answer");
        }

        [Test]
        public void ACoarseInvalidationDropsTheLayerReadCache()
        {
            var menu = Removing("Hat");
            var tree = Tree(menu);
            RemovalPlanner.Plan(tree, avatar.Descriptor);
            int before = LayerAnalysis.LayerReadBuilds;

            // Undo, a hierarchy change, or an unrecognised ObjectChangeEvents batch: none of them says a controller
            // reimported, but any of them could be exactly why the FX controller now reads something new.
            AvatarPlanCache.Invalidate();

            var plan = RemovalPlanner.Plan(tree, avatar.Descriptor);

            Assert.That(LayerAnalysis.LayerReadBuilds, Is.EqualTo(before + 1), "a coarse invalidation has to drop the layer read cache too, or the plan goes on reading stale layer data forever");
            Assert.That(Of(plan, "HatParam").Freed, Is.True);
        }

        [Test]
        public void AMissingStateSubMachineOrTransitionDoesNotBreakThePlan()
        {
            // What a bad merge or a hand edited controller leaves behind: the state machine still lists the state,
            // the sub state machine and the transition, but the objects they name are gone.
            var machine = AddLayerTo(avatar.Fx, "Broken");
            var kept = machine.AddState("Kept");
            var lost = machine.AddState("Lost");
            machine.defaultState = kept;
            var transition = kept.AddTransition(lost);
            transition.AddCondition(AnimatorConditionMode.If, 0f, "HatParam");
            var sub = machine.AddStateMachine("Sub");
            var inside = sub.AddState("Inside");
            try
            {
                ClearUndoOf(transition, lost, sub);
                Object.DestroyImmediate(transition, true);
                Object.DestroyImmediate(lost, true);
                Object.DestroyImmediate(sub, true);
                // A removal edit, so the plan also reads every layer of the controller.
                var menu = Removing("Hat");

                AvatarPlan plan = null;
                Assert.DoesNotThrow(() => plan = AvatarPlanCache.GetFor(menu), "the inspector's plan is built on every refresh");
                Assert.That(plan.Tree, Is.Not.Null);
                var layerReadsOf = typeof(LayerAnalysis).GetMethod("LayerReadsOf", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                Assert.DoesNotThrow(() => layerReadsOf.Invoke(null, new object[] { avatar.Fx, true }));
                Assert.DoesNotThrow(() => AnimatorBuilder.DecideWriteDefaultsForAvatar(avatar.Fx));
                Assert.DoesNotThrow(() => TouchBuilder.ExistingTrackingControlOwners(avatar.Fx));
            }
            finally
            {
                ClearUndoOf(inside, kept, machine, avatar.Fx);
            }
        }

        /// <summary>Takes the Undo records Unity's animator calls left for these objects off the shared Undo stack, before the objects are destroyed. A destroyed one is skipped.</summary>
        static void ClearUndoOf(params Object[] made)
        {
            foreach (var obj in made)
                if (obj != null) Undo.ClearUndo(obj);
        }

        [Test]
        public void ADisabledComponentsRemovalIsNotPlanned()
        {
            var menu = Removing("Hat");
            menu.enabled = false;
            AvatarPlanCache.Invalidate();

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(plan.Tree.Removal.Parameters, Is.Empty, "the build skips a disabled component, so its removal frees nothing");
            Assert.That(plan.Tree.Removal.Layers, Is.Empty);
            var hat = plan.Tree.Root.Items.Single(i => i.Name == "Hat");
            Assert.That(hat.Hidden, Is.False, "and the control stays in the menu the build writes");
        }

        [Test]
        public void ADisabledComponentsEntryDoesNotKeepAParameterAlive()
        {
            var remover = Removing("Hat");
            var spare = new GameObject("Spare");
            spare.transform.SetParent(avatar.Root.transform);
            var disabled = avatar.AddMenu(spare);
            var reader = disabled.AddEntry(EntryKind.Toggle, "Reader");
            reader.parameterName = "HatParam";
            reader.objects.Add(new ObjectTarget { target = avatar.Hat });
            disabled.enabled = false;

            var plan = Plan(remover, disabled);

            Assert.That(Of(plan, "HatParam").Freed, Is.True, "an entry the build never sees reads nothing, so it cannot hold the parameter");
        }

        [Test]
        public void AnUnsavedAnimatorEditInvalidatesTheReadCache()
        {
            // EarLayer exists before the first plan, reading only EarParam, so the edit below is a condition added
            // to a layer that was already there, not a new layer or a new parameter: AddCondition registers undo
            // on the transition sub asset it edits, not necessarily on the controller's own dirty counter the read
            // cache is keyed on, so this is the one edit shape that key alone might not catch.
            var earLayer = AddBoolLayer(avatar.Fx, "EarLayer", "EarParam");
            var menu = Removing("Hat");
            var tree = Tree(menu);
            var first = RemovalPlanner.Plan(tree, avatar.Descriptor);
            Assert.That(Of(first, "HatParam").Freed, Is.True, "nothing but HatLayer reads HatParam yet");

            // No save, no reimport, so nothing tells AvatarPlanCache which controller changed or that one did at
            // all. The coarse invalidation this kind of edit actually raises still has to see it, which is what
            // AvatarPlanCache.Invalidate stands in for here.
            earLayer.states.Single(s => s.state.name == "Off").state.transitions[0].AddCondition(AnimatorConditionMode.If, 0f, "HatParam");
            AvatarPlanCache.Invalidate();

            var second = RemovalPlanner.Plan(tree, avatar.Descriptor);

            Assert.That(Of(second, "HatParam").Freed, Is.False,
                "EarLayer, which stays, now reads HatParam too from a condition-only edit; the read cache must not go on answering from before it");
        }

        [Test]
        public void TheBuildDoesNotReadAStaleReadCache()
        {
            var menu = Removing("Hat");
            // Warms the shared cache for avatar.Fx itself: the build clone's FX controller is this same asset
            // instance (nothing has cloned it yet at the point RemovalPrepare runs), so a build that trusted the
            // cache instead of reading fresh would find this entry and never notice.
            RemovalPlanner.Plan(Tree(menu), avatar.Descriptor);
            int cachedBefore = LayerAnalysis.CachedControllerCount;
            int buildsBefore = LayerAnalysis.LayerReadBuilds;

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);

                Assert.That(LayerAnalysis.LayerReadBuilds, Is.GreaterThan(buildsBefore),
                    "RemovalPrepare must walk the build clone's controller itself instead of answering from whatever the inspector already cached for the same asset");
                Assert.That(LayerAnalysis.CachedControllerCount, Is.EqualTo(cachedBefore),
                    "and must not add its own fresh read to that shared cache either");
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
            }
        }

        [Test]
        public void AnInsertedLayerDoesNotBorrowAnotherLayersReads()
        {
            var menu = Removing("Hat");
            var tree = Tree(menu);
            RemovalPlanner.Plan(tree, avatar.Descriptor); // warms LayerAnalysis's cache for the one layer that exists so far: HatLayer at index 0

            // A layer inserted ahead of it without any reimport: the cached reads still describe the old,
            // one-layer arrangement, so a lookup by position alone would now pair HatLayer's own cached reads with
            // whatever sits at index 0, GuardLayer, reading EarParam instead.
            AddBoolLayer(avatar.Fx, "GuardLayer", "EarParam");
            var layers = avatar.Fx.layers;
            var swap = layers[0]; layers[0] = layers[1]; layers[1] = swap;
            avatar.Fx.layers = layers;

            var plan = RemovalPlanner.Plan(tree, avatar.Descriptor);

            Assert.That(plan.Layers, Is.EqualTo(new[] { "HatLayer" }), "HatLayer is still found by its own name, not skipped or swapped for the layer that now sits where it used to");
            Assert.That(Of(plan, "HatParam").Freed, Is.True, "HatLayer's own reads did not change just because something else was inserted ahead of it");
        }

        [Test]
        public void RemovableLayersReusesTheCachedNameLookupInsteadOfRebuildingIt()
        {
            var menu = Removing("Hat");
            var tree = Tree(menu);
            int before = LayerAnalysis.LayerReadByNameBuilds;

            var first = RemovalPlanner.Plan(tree, avatar.Descriptor);

            Assert.That(LayerAnalysis.LayerReadByNameBuilds, Is.EqualTo(before + 1), "the first plan has to build the name-keyed lookup alongside the layer reads");
            Assert.That(Of(first, "HatParam").Freed, Is.True);

            RemovalPlanner.Plan(tree, avatar.Descriptor);
            RemovalPlanner.Plan(tree, avatar.Descriptor);

            Assert.That(LayerAnalysis.LayerReadByNameBuilds, Is.EqualTo(before + 1),
                "RemovableLayers must reuse the name-keyed lookup the cached layer reads already carry, not rebuild it from scratch on every removal-plan call");
        }

        [Test]
        public void ParameterReusedByAnEntryStays()
        {
            var menu = Removing("Hat");
            var entry = menu.AddEntry(EntryKind.Toggle, "New hat");
            entry.parameterName = "HatParam";
            entry.objects.Add(new ObjectTarget { target = avatar.Hat });

            var plan = Plan(menu);

            var parameter = Of(plan, "HatParam");
            Assert.That(parameter.Freed, Is.False);
            Assert.That(parameter.KeptBecause, Is.EqualTo("ad.removal.kept.entry"));
        }

        [Test]
        public void ParameterReadByAnotherLayerStays()
        {
            var shared = AddBoolLayer(avatar.Fx, "SharedLayer", "HatParam");
            var extra = shared.states[1].state.AddTransition(shared.states[0].state);
            extra.hasExitTime = false;
            extra.AddCondition(AnimatorConditionMode.If, 0f, "Existing");

            var plan = Plan(Removing("Hat"));

            var parameter = Of(plan, "HatParam");
            Assert.That(parameter.Freed, Is.False);
            Assert.That(parameter.KeptBecause, Is.EqualTo("ad.removal.kept.layer"));
            Assert.That(plan.Layers, Is.Empty, "a layer is never removed while its parameter stays");
        }

        [Test]
        public void ParameterReadByTheGestureControllerStays()
        {
            var gesture = new AnimatorController { name = "Gesture" };
            try
            {
                AddBoolLayer(gesture, "GestureHat", "HatParam");
                var layers = avatar.Descriptor.baseAnimationLayers;
                for (int i = 0; i < layers.Length; i++)
                {
                    if (layers[i].type != VRCAvatarDescriptor.AnimLayerType.Gesture) continue;
                    layers[i].isDefault = false;
                    layers[i].animatorController = gesture;
                }
                avatar.Descriptor.baseAnimationLayers = layers;

                var plan = Plan(Removing("Hat"));

                var parameter = Of(plan, "HatParam");
                Assert.That(parameter.Freed, Is.False);
                Assert.That(parameter.KeptBecause, Is.EqualTo("ad.removal.kept.other_controller"));
            }
            finally
            {
                Object.DestroyImmediate(gesture);
            }
        }

        [Test]
        public void ParameterWrittenByAContactStays()
        {
            existingRoot.controls.Add(Control("Tail", "Tail_IsGrabbed"));
            Declare("Tail_IsGrabbed");
            var contact = new GameObject("Contact");
            contact.transform.SetParent(avatar.Root.transform);
            var receiver = contact.AddComponent<VRCContactReceiver>();
            receiver.parameter = "HatParam";
            var bone = new GameObject("Tail");
            bone.transform.SetParent(avatar.Root.transform);
            bone.AddComponent<VRCPhysBone>().parameter = "Tail";

            var plan = Plan(Removing("Hat", "Tail"));

            Assert.That(Of(plan, "HatParam").KeptBecause, Is.EqualTo("ad.removal.kept.contact"));
            Assert.That(Of(plan, "Tail_IsGrabbed").KeptBecause, Is.EqualTo("ad.removal.kept.contact"), "a PhysBone writes the suffixed names");
        }

        [Test]
        public void BuildWithoutAPreparedPlanFreesNothing()
        {
            Removing("Hat");
            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();

            // No Resolving pass, so no plan: the controllers the descriptor points at are already empty here and
            // a plan made from them would free whatever their layers read.
            ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();

            Assert.That(avatar.Descriptor.expressionParameters.parameters.Any(p => p.name == "HatParam"), Is.True, "nothing is freed");
            var fx = (AnimatorController)avatar.Descriptor.baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            Assert.That(fx.layers.Any(l => l.name == "HatLayer"), Is.True, "and no layer is left out");
        }

        [Test]
        public void EditorOnlyComponentPlansNoRemoval()
        {
            var hidden = new GameObject("Hidden") { tag = "EditorOnly" };
            hidden.transform.SetParent(avatar.Root.transform);
            var menu = avatar.AddMenu(hidden);
            menu.menuEdits.Add(new MenuEdit { id = "hat-edit", source = new List<string> { "Hat" }, remove = true });

            GameObject processed = null;
            try
            {
                processed = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                var descriptor = processed.GetComponent<VRCAvatarDescriptor>();

                Assert.That(descriptor.expressionParameters.parameters.Any(p => p.name == "HatParam"), Is.True,
                    "the component was deleted before the plan was made, so nothing was freed for it");
                Assert.That(descriptor.expressionsMenu.controls.Any(c => c.name == "Hat"), Is.True, "and the control stays");
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                GeneratedAssets.CleanUp();
            }
        }

        [Test]
        public void ParameterAVrcFuryFullControllerMayReadStays()
        {
            var merged = new AnimatorController { name = "FuryMerged" };
            try
            {
                merged.AddParameter("HatParam", AnimatorControllerParameterType.Bool);
                var go = new GameObject("FuryFull");
                go.transform.SetParent(avatar.Root.transform);
                var component = VrcFuryTestHelper.AddFeature(go, "VF.Model.Feature.FullController");
                var entryType = VrcFuryTestHelper.FeatureType("VF.Model.Feature.FullController+ControllerEntry");
                Assume.That(entryType, Is.Not.Null);
                var entry = System.Activator.CreateInstance(entryType, true);
                VrcFuryTestHelper.SetField(entry, "controller", VrcFuryTestHelper.BuildGuidWrapper("VF.Model.GuidController", merged));
                VrcFuryTestHelper.ConfigureFeature(component, instance =>
                {
                    ((System.Collections.IList)VrcFuryTestHelper.GetField(instance, "controllers")).Add(entry);
                    VrcFuryTestHelper.SetField(instance, "globalParams", new List<string> { "EarParam" });
                });

                var plan = Plan(Removing("Hat", "Ears"));

                Assert.That(Of(plan, "HatParam").KeptBecause, Is.EqualTo("ad.removal.kept.other_tool"), "the merged controller may read it");
                Assert.That(Of(plan, "EarParam").KeptBecause, Is.EqualTo("ad.removal.kept.other_tool"), "and a global parameter is read by name");
            }
            finally
            {
                Object.DestroyImmediate(merged);
            }
        }

        [Test]
        public void ParameterCopiedByADriverStays()
        {
            var sm = AddBoolLayer(avatar.Fx, "CopyLayer", "Existing");
            var driver = sm.states[0].state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
            {
                type = VRC_AvatarParameterDriver.ChangeType.Copy,
                source = "HatParam",
                name = "Existing",
            });

            var plan = Plan(Removing("Hat"));

            var parameter = Of(plan, "HatParam");
            Assert.That(parameter.Freed, Is.False, "a driver that copies the parameter still reads it");
            Assert.That(parameter.KeptBecause, Is.EqualTo("ad.removal.kept.driver"));
        }

        [Test]
        public void ParameterAVrcFuryToggleDrivesStays()
        {
            var go = new GameObject("Fury");
            go.transform.SetParent(avatar.Root.transform);
            var component = VrcFuryTestHelper.AddToggle(go, "Fury hat");
            VrcFuryTestHelper.ConfigureToggle(component, instance =>
            {
                VrcFuryTestHelper.SetField(instance, "useGlobalParam", true);
                VrcFuryTestHelper.SetField(instance, "globalParam", "HatParam");
            });

            var plan = Plan(Removing("Hat"));

            var parameter = Of(plan, "HatParam");
            Assert.That(parameter.Freed, Is.False);
            Assert.That(parameter.KeptBecause, Is.EqualTo("ad.removal.kept.other_tool"));
        }

#if DOCME_MA
        [Test]
        public void ParameterAModularAvatarComponentDeclaresStays()
        {
            var go = new GameObject("MAParameters");
            go.transform.SetParent(avatar.Root.transform);
            var parameters = go.AddComponent<nadena.dev.modular_avatar.core.ModularAvatarParameters>();
            parameters.parameters = new List<nadena.dev.modular_avatar.core.ParameterConfig>
            {
                new nadena.dev.modular_avatar.core.ParameterConfig { nameOrPrefix = "HatParam" },
            };

            var plan = Plan(Removing("Hat"));

            var parameter = Of(plan, "HatParam");
            Assert.That(parameter.Freed, Is.False);
            Assert.That(parameter.KeptBecause, Is.EqualTo("ad.removal.kept.other_tool"));
        }
#endif

        [Test]
        public void DriverInAnotherControllerWithTheSameLayerNameKeepsTheParameter()
        {
            var gesture = new AnimatorController { name = "Gesture" };
            try
            {
                // The same layer name as the removable FX layer: only the controller tells the two apart.
                var sm = AddLayerTo(gesture, "HatLayer");
                var state = sm.AddState("Drive");
                sm.defaultState = state;
                var driver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter { name = "HatParam", value = 1f });
                var layers = avatar.Descriptor.baseAnimationLayers;
                for (int i = 0; i < layers.Length; i++)
                {
                    if (layers[i].type != VRCAvatarDescriptor.AnimLayerType.Gesture) continue;
                    layers[i].isDefault = false;
                    layers[i].animatorController = gesture;
                }
                avatar.Descriptor.baseAnimationLayers = layers;

                var plan = Plan(Removing("Hat"));

                var parameter = Of(plan, "HatParam");
                Assert.That(parameter.Freed, Is.False);
                Assert.That(parameter.KeptBecause, Is.EqualTo("ad.removal.kept.driver"));
                Assert.That(plan.Layers, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(gesture);
            }
        }

        [Test]
        public void LayerControlFromAnotherToolKeepsLayerAndParameter()
        {
            // The plan is made while nothing switches layers by position, the way the planner sees the avatar
            // before another tool has merged its own controller into the build.
            var plan = Plan(Removing("Hat"));
            Assert.That(Of(plan, "HatParam").Freed, Is.True, "the plan starts out freeing it");

            var idle = avatar.Fx.layers[0].stateMachine.states[0].state;
            var control = idle.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
            control.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
            control.layer = 0;

            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var messages = new List<ModelMessage>();
            LayerRemover.Run(asc, new List<AvatarDoc> { avatar.Root.GetComponent<AvatarDoc>() }, messages, plan);
            context.DeactivateAllExtensionContexts();

            var fx = (AnimatorController)avatar.Descriptor.baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            Assert.That(fx.layers.Any(l => l.name == "HatLayer"), Is.True, "the layer stays");
            Assert.That(Of(plan, "HatParam").Freed, Is.False, "and so does its parameter, or the layer would read a name nobody declares");
            Assert.That(Of(plan, "HatParam").KeptBecause, Is.EqualTo("ad.removal.kept.layer"));
            var warning = messages.Single(m => m.Key == "ad.warn.removed_layer_unsafe");
            Assert.That(warning.Args[0], Is.EqualTo("HatLayer"));
        }

        [Test]
        public void BothRowsOfASharedParameterShowTheResult()
        {
            existingRoot.controls.Add(Control("Hat again", "HatParam"));
            AvatarPlanCache.Invalidate();
            var menu = Removing("Hat", "Hat again");
            AvatarPlanCache.Invalidate();
            var view = View(menu);

            foreach (var name in new[] { "Hat", "Hat again" })
            {
                var row = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Name == name);
                view.Tree.SelectKey(row.Key);
                var note = view.Detail.Q<Label>("ad-existing-remove-note");
                Assert.That(note, Is.Not.Null, name + " shows a result line");
                Assert.That(note.text, Is.EqualTo("Frees 1 bit and 1 layer"), name);
            }
        }

        [Test]
        public void BuiltInParameterIsNeverRemoved()
        {
            existingRoot.controls.Add(Control("Emote", "VRCEmote"));
            Declare(VRCExpressionParameters.ValueType.Int, "VRCEmote");

            var plan = Plan(Removing("Emote"));

            var parameter = Of(plan, "VRCEmote");
            Assert.That(parameter.Freed, Is.False);
            Assert.That(parameter.KeptBecause, Is.EqualTo("ad.removal.kept.builtin"));
        }

        [Test]
        public void LayerDrivenOnlyByTheParameterIsRemoved()
        {
            var plan = Plan(Removing("Hat"));

            Assert.That(Of(plan, "HatParam").Freed, Is.True);
            Assert.That(Of(plan, "HatParam").Bits, Is.EqualTo(1));
            Assert.That(plan.Layers, Is.EqualTo(new[] { "HatLayer" }));
            Assert.That(plan.LayerControlBlocks, Is.False);
        }

        [Test]
        public void LayerWithBehavioursStaysAndKeepsTheParameter()
        {
            var layer = avatar.Fx.layers.Single(l => l.name == "HatLayer");
            layer.stateMachine.states[1].state.AddStateMachineBehaviour<VRCAnimatorTrackingControl>();

            var plan = Plan(Removing("Hat"));

            Assert.That(plan.Layers, Is.Empty);
            var parameter = Of(plan, "HatParam");
            Assert.That(parameter.Freed, Is.False);
            Assert.That(parameter.KeptBecause, Is.EqualTo("ad.removal.kept.layer"));
        }

        [Test]
        public void LayerControlBlocksLayerRemovalAndKeepsTheParameter()
        {
            var idle = avatar.Fx.layers[0].stateMachine.states[0].state;
            var control = idle.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
            control.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
            control.layer = 0;

            var plan = Plan(Removing("Hat"));

            Assert.That(plan.LayerControlBlocks, Is.True);
            Assert.That(plan.Layers, Is.Empty);
            var parameter = Of(plan, "HatParam");
            Assert.That(parameter.Freed, Is.False);
            Assert.That(parameter.KeptBecause, Is.EqualTo("ad.removal.kept.layer"));
        }

        [Test]
        public void RemoveImpliesHidden()
        {
            var menu = Removing("Hat");
            var tree = Tree(menu);

            var item = tree.Root.Items.Single(i => i.Name == "Hat");
            Assert.That(item.Hidden, Is.True, "a removed item is hidden everywhere hidden is read");

            var plan = ParameterPlanner.Plan(tree, new Dictionary<string, PlannedType>());
            var context = new BuildContext(avatar.Root, null);
            MenuWriter.Write(context, avatar.Descriptor, tree, plan);

            Assert.That(avatar.Descriptor.expressionsMenu.controls.Any(c => c.name == "Hat"), Is.False);
            Assert.That(avatar.Descriptor.expressionsMenu.controls.Any(c => c.name == "Ears"), Is.True);
        }

        [Test]
        public void ForeignRowsRefuseRemove()
        {
            var go = new GameObject("Fury");
            go.transform.SetParent(avatar.Root.transform);
            VrcFuryTestHelper.AddToggle(go, "Fury hat");
            AvatarPlanCache.Invalidate();
            var menu = avatar.AddMenu();
            var view = View(menu);
            var foreign = view.Tree.RowsOfKind(RowKind.Foreign).First(r => r.Name == "Fury hat");

            Assert.That(MenuEditing.SetRemoved(menu, foreign, true), Is.False);
            Assert.That(menu.menuEdits, Is.Empty);

            view.Tree.SelectKey(foreign.Key);
            Assert.That(view.Detail.Q<Toggle>("ad-existing-remove"), Is.Null, "the panel offers no switch on a foreign row");

            var existing = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Name == "Hat");
            view.Tree.SelectKey(existing.Key);
            Assert.That(view.Detail.Q<Toggle>("ad-existing-remove"), Is.Not.Null, "an existing row offers it");
        }

        [Test]
        public void MemoryBarCountsFreedBits()
        {
            var menu = avatar.AddMenu();
            var before = View(menu).Q<MemoryBar>().AvatarBits;

            menu.menuEdits.Add(new MenuEdit { id = "hat-edit", source = new List<string> { "Hat" }, remove = true });
            AvatarPlanCache.Invalidate();
            var view = View(menu);

            Assert.That(view.Q<MemoryBar>().AvatarBits, Is.EqualTo(before - 1), "the freed bit leaves the avatar's share of the bar");
            Assert.That(ParameterPlanner.CountBits(avatar.Root.transform).avatarBits, Is.EqualTo(before - 1));
        }

        [Test]
        public void PanelSaysWhatIsFreedAndWhatStays()
        {
            existingRoot.controls.Add(Control("Ears again", "EarParam"));
            AvatarPlanCache.Invalidate();
            var menu = avatar.AddMenu();
            var view = View(menu);
            var hat = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Name == "Hat");
            view.Tree.SelectKey(hat.Key);

            view.Detail.SetExistingRemoved(true);

            var note = view.Detail.Q<Label>("ad-existing-remove-note");
            Assert.That(note, Is.Not.Null);
            Assert.That(note.text, Is.EqualTo("Frees 1 bit and 1 layer"));
            Assert.That(note.selection.isSelectable, Is.True, "the result line can be selected and copied");

            var ears = view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Name == "Ears");
            view.Tree.SelectKey(ears.Key);
            view.Detail.SetExistingRemoved(true);

            var keptNote = view.Detail.Q<Label>("ad-existing-remove-note");
            Assert.That(keptNote.text, Is.EqualTo(string.Format(L.Text("ad.ui.existing.remove_keeps"), "EarParam", L.Text("ad.removal.kept.control"))));
        }

        /// <summary>A menu asset built on PC only, holding one more control on the avatar's own HatParam.</summary>
        VRCExpressionsMenu PcOnlyAsset(AvatarDoc menu)
        {
            var asset = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            asset.name = "Extras";
            asset.controls = new List<VRCExpressionsMenu.Control> { Control("Hat again", "HatParam") };
            var install = MenuAssetInstall.Create(asset);
            install.platform = EntryPlatform.PCOnly;
            menu.menuAssets.Add(install);
            return asset;
        }

        [Test]
        public void PcOnlyAssetKeepsTheParameterListIdentical()
        {
            var menu = Removing("Hat");
            var asset = PcOnlyAsset(menu);
            try
            {
                var onPc = PlatformParity.ListOf(avatar.Root.transform);
                Platform.OverrideMobile = true;
                var onQuest = PlatformParity.ListOf(avatar.Root.transform);

                Assert.That(onPc, Does.Contain("HatParam (Bool, synced)"), "the asset's own control still drives it");
                Assert.That(onQuest, Is.EqualTo(onPc), "an asset left out on one platform may not change the list");
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void PcOnlyAssetGivesTheSameRemovalPlanOnBothPlatforms()
        {
            var menu = Removing("Hat");
            var asset = PcOnlyAsset(menu);
            try
            {
                var onPc = Of(Plan(menu), "HatParam");
                Assert.That(onPc.Freed, Is.False);
                Assert.That(onPc.KeptBecause, Is.EqualTo("ad.removal.kept.control"));

                Platform.OverrideMobile = true;
                var onQuest = Of(Plan(menu), "HatParam");

                Assert.That(onQuest.Freed, Is.EqualTo(onPc.Freed));
                Assert.That(onQuest.KeptBecause, Is.EqualTo(onPc.KeptBecause));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ParameterAVrcFuryOverrideControllerMayReadStays()
        {
            var merged = new AnimatorController { name = "FuryMerged" };
            var over = new AnimatorOverrideController { name = "FuryOverride", runtimeAnimatorController = merged };
            try
            {
                merged.AddParameter("HatParam", AnimatorControllerParameterType.Bool);
                var go = new GameObject("FuryFull");
                go.transform.SetParent(avatar.Root.transform);
                var component = VrcFuryTestHelper.AddFeature(go, "VF.Model.Feature.FullController");
                var entryType = VrcFuryTestHelper.FeatureType("VF.Model.Feature.FullController+ControllerEntry");
                Assume.That(entryType, Is.Not.Null);
                var entry = System.Activator.CreateInstance(entryType, true);
                VrcFuryTestHelper.SetField(entry, "controller", VrcFuryTestHelper.BuildGuidWrapper("VF.Model.GuidController", over));
                VrcFuryTestHelper.ConfigureFeature(component, instance =>
                    ((System.Collections.IList)VrcFuryTestHelper.GetField(instance, "controllers")).Add(entry));

                var parameter = Of(Plan(Removing("Hat")), "HatParam");

                Assert.That(parameter.Freed, Is.False, "an override controller is followed to the controller it overrides");
                Assert.That(parameter.KeptBecause, Is.EqualTo("ad.removal.kept.other_tool"));
            }
            finally
            {
                Object.DestroyImmediate(over);
                Object.DestroyImmediate(merged);
            }
        }

        [Test]
        public void LayerRemovedByNameAndByThePlanIsNotAmbiguous()
        {
            var menu = Removing("Hat");
            menu.removedLayers.Add(new RemovedLayer { layer = "HatLayer", editId = "Hat-edit" });
            var tree = Tree(menu);
            var removal = RemovalPlanner.Plan(tree, avatar.Descriptor);
            Assert.That(removal.Layers, Does.Contain("HatLayer"), "the plan names the same layer");

            var messages = new List<ModelMessage>();
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            LayerRemover.Run(asc, new[] { menu }, messages, removal);
            var fx = asc.ControllerContext.Controllers[VRCAvatarDescriptor.AnimLayerType.FX];
            bool stillThere = fx.Layers.Any(l => l.Name == "HatLayer");
            context.DeactivateAllExtensionContexts();

            Assert.That(stillThere, Is.False, "the first pass took it out");
            Assert.That(messages.Any(m => m.Key == "ad.warn.removed_layer_ambiguous"), Is.False,
                "a layer that is already gone is not ambiguous");
            Assert.That(Of(removal, "HatParam").Freed, Is.True, "so the parameter is still freed");
        }

        [Test]
        public void AmbiguousLayerTakebackCountsTowardTheBudget()
        {
            var menu = Removing("Hat");
            var removal = RemovalPlanner.Plan(Tree(menu), avatar.Descriptor);
            Assert.That(removal.FreedBits, Is.EqualTo(1), "the plan frees HatParam with its layer");

            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            // Another tool's merge handed on a clone with a second layer of the same name.
            asc.ControllerContext.Controllers[VRCAvatarDescriptor.AnimLayerType.FX].AddLayer(LayerPriority.Default, "HatLayer");
            var messages = LayerRemover.KeepAmbiguous(asc, removal, menu);
            context.DeactivateAllExtensionContexts();

            Assert.That(messages.Any(m => m.Key == "ad.warn.removed_layer_ambiguous"), Is.True);
            Assert.That(removal.FreedBits, Is.EqualTo(0), "the bits the budget counts come back before it counts them");
            Assert.That(Of(removal, "HatParam").KeptBecause, Is.EqualTo("ad.removal.kept.layer"));
            Assert.That(removal.Layers, Is.Empty);
            Assert.That(removal.BlockedLayers, Is.Empty, "that list only feeds the layer control warning");
        }

#if DOCME_MA
        [Test]
        public void ParameterAnAppendedMenuAssetControlNamesStays()
        {
            var asset = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            asset.name = "Appended";
            asset.controls = new List<VRCExpressionsMenu.Control> { Control("Hat again", "HatParam") };
            try
            {
                var go = new GameObject("Appended menu");
                go.transform.SetParent(avatar.Root.transform);
                go.AddComponent<ModularAvatarMenuInstaller>().menuToAppend = asset;

                var parameter = Of(Plan(Removing("Hat")), "HatParam");

                Assert.That(parameter.Freed, Is.False, "a control inside the appended asset still drives it");
                Assert.That(parameter.KeptBecause, Is.EqualTo("ad.removal.kept.other_tool"));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
#endif
    }
}
