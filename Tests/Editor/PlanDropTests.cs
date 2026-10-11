#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// A cached plan is dropped only when something it reads changed. Every editor event is handed
    /// to AvatarPlanCache the way Unity hands it over: a batch of change events built with Unity's own stream
    /// builder, the hierarchy and undo handlers taken from Unity's own handler lists.
    /// </summary>
    public class PlanDropTests
    {
        TestAvatar avatar;
        Action<string> oldMenuLog;
        readonly List<string> menuLog = new List<string>();

        [SetUp]
        public void SetUp()
        {
            oldMenuLog = AdMenu.Log;
            menuLog.Clear();
            AdMenu.Log = menuLog.Add;
            GeneratedAssets.Note();
            avatar = TestAvatar.Create();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            AdMenu.Log = oldMenuLog;
            ForgetAnimatorUndo(avatar.Fx);
            avatar.Dispose();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
            GeneratedAssets.CleanUp();
        }

        // The batch Unity publishes at the end of a frame, built with its own builder.

        delegate void Push(ref ObjectChangeEventStream.Builder builder);

        static void Publish(params Push[] events)
        {
            var builder = new ObjectChangeEventStream.Builder(Allocator.Persistent);
            try
            {
                foreach (var push in events) push(ref builder);
                var stream = builder.ToStream(Allocator.Persistent);
                try { AvatarPlanCache.OnChangesPublished(ref stream); }
                finally { stream.Dispose(); }
            }
            finally
            {
                builder.Dispose();
            }
        }

        static Push Properties(Object target) => Properties(target.GetInstanceID());

        static Push Properties(int instanceId) => (ref ObjectChangeEventStream.Builder b) =>
        {
            var args = new ChangeGameObjectOrComponentPropertiesEventArgs(instanceId, default(Scene));
            b.PushChangeGameObjectOrComponentPropertiesEvent(ref args);
        };

        /// <summary>What EditorUtility.SetDirty publishes for the scene of the object it dirtied: a probe on 2026-10-09 saw one of these for every call, and no property change of its own.</summary>
        static Push SceneChanged(Scene scene) => (ref ObjectChangeEventStream.Builder b) =>
        {
            var args = new ChangeSceneEventArgs(scene);
            b.PushChangeSceneEvent(ref args);
        };

        static Push PrefabInstancesUpdated(Object instanceRoot) => (ref ObjectChangeEventStream.Builder b) =>
        {
            var ids = new NativeArray<int>(new[] { instanceRoot.GetInstanceID() }, Allocator.Persistent);
            try
            {
                var args = new UpdatePrefabInstancesEventArgs(default(Scene), ids.AsReadOnly());
                b.PushUpdatePrefabInstancesEvent(ref args);
            }
            finally
            {
                ids.Dispose();
            }
        };

        static Push AssetProperties(Object asset) => (ref ObjectChangeEventStream.Builder b) =>
        {
            var args = new ChangeAssetObjectPropertiesEventArgs(default(GUID), asset.GetInstanceID(), default(Scene));
            b.PushChangeAssetObjectPropertiesEvent(ref args);
        };

        static Push Created(Object made) => (ref ObjectChangeEventStream.Builder b) =>
        {
            var args = new CreateGameObjectHierarchyEventArgs(made.GetInstanceID(), default(Scene));
            b.PushCreateGameObjectHierarchyEvent(ref args);
        };

        static Push StructureChanged(Object root) => (ref ObjectChangeEventStream.Builder b) =>
        {
            var args = new ChangeGameObjectStructureHierarchyEventArgs(root.GetInstanceID(), default(Scene));
            b.PushChangeGameObjectStructureHierarchyEvent(ref args);
        };

        static Push Destroyed(int instanceId, Object parent) => (ref ObjectChangeEventStream.Builder b) =>
        {
            var args = new DestroyGameObjectHierarchyEventArgs(instanceId, parent.GetInstanceID(), default(Scene));
            b.PushDestroyGameObjectHierarchyEvent(ref args);
        };

        static Push Reparented(Object moved, Object previousParent, Object newParent) => (ref ObjectChangeEventStream.Builder b) =>
        {
            var args = new ChangeGameObjectParentEventArgs(moved.GetInstanceID(), default(Scene), previousParent.GetInstanceID(), default(Scene), newParent.GetInstanceID());
            b.PushChangeGameObjectParentEvent(ref args);
        };

        static bool OfPlanCache(Type type) => type == typeof(AvatarPlanCache) || (type != null && type.DeclaringType == typeof(AvatarPlanCache));

        /// <summary>
        /// Runs AvatarPlanCache's own hierarchyChanged handler. Unity keeps the handlers in the private
        /// EventWithPerformanceTracker m_HierarchyChangedEvent and raises them only from its editor loop, which a
        /// test that runs in one go never reaches; only this handler runs, not every other tool's.
        /// </summary>
        static void RaiseHierarchyChanged()
        {
            var field = typeof(EditorApplication).GetField("m_HierarchyChangedEvent", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, "Unity keeps hierarchyChanged's handlers in m_HierarchyChangedEvent");
            var tracker = field.GetValue(null);
            var handlers = new List<Action>();
            using (var each = (IEnumerator<Action>)tracker.GetType().GetMethod("GetEnumerator").Invoke(tracker, null))
                while (each.MoveNext()) handlers.Add(each.Current);
            var mine = handlers.Where(h => OfPlanCache(h.Method.DeclaringType)).ToList();
            Assert.That(mine.Count, Is.EqualTo(1), "AvatarPlanCache listens for the hierarchy changing");
            mine[0]();
        }

        static void RaiseUndo()
        {
            var mine = Undo.undoRedoPerformed.GetInvocationList().Where(d => OfPlanCache(d.Method.DeclaringType)).ToList();
            Assert.That(mine.Count, Is.EqualTo(1), "AvatarPlanCache listens for undo and redo");
            mine[0].DynamicInvoke();
        }

        // The avatar: a removal edit, so every plan build also reads the FX controller's layers.

        static AnimatorStateMachine AddBoolLayer(AnimatorController controller, string layerName, string parameter)
        {
            if (controller.parameters.All(p => p.name != parameter)) controller.AddParameter(parameter, AnimatorControllerParameterType.Bool);
            controller.AddLayer(layerName);
            var layers = controller.layers;
            layers[layers.Length - 1].defaultWeight = 1f;
            controller.layers = layers;
            var sm = controller.layers[controller.layers.Length - 1].stateMachine;
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

        /// <summary>Takes the Undo records Unity's animator calls left for the controller and its sub assets off the shared Undo stack before they are destroyed.</summary>
        static void ForgetAnimatorUndo(AnimatorController controller)
        {
            if (controller == null) return;
            foreach (var layer in controller.layers)
            {
                foreach (var machine in Machines(layer.stateMachine))
                {
                    foreach (var child in machine.states)
                    {
                        if (child.state == null) continue;
                        foreach (var transition in child.state.transitions) if (transition != null) Undo.ClearUndo(transition);
                        Undo.ClearUndo(child.state);
                    }
                    Undo.ClearUndo(machine);
                }
            }
            Undo.ClearUndo(controller);
        }

        static IEnumerable<AnimatorStateMachine> Machines(AnimatorStateMachine root)
        {
            if (root == null) yield break;
            yield return root;
            foreach (var child in root.stateMachines)
                foreach (var machine in Machines(child.stateMachine)) yield return machine;
        }

        AvatarDoc WithRemoval(TestAvatar on = null)
        {
            on = on ?? avatar;
            on.ExpressionsMenu.controls.Add(TestAvatar.ToggleControl("Hat", "HatParam"));
            var parameters = on.ExpressionParameters.parameters.ToList();
            parameters.Add(new VRCExpressionParameters.Parameter { name = "HatParam", valueType = VRCExpressionParameters.ValueType.Bool, saved = true, networkSynced = true });
            on.ExpressionParameters.parameters = parameters.ToArray();
            AddBoolLayer(on.Fx, "HatLayer", "HatParam");
            var menu = on.AddMenu();
            menu.menuEdits.Add(new MenuEdit { id = "hat-remove", source = new List<string> { "Hat" }, remove = true });
            AvatarPlanCache.Invalidate();
            return menu;
        }

        struct Counts
        {
            public int Plans, Walks, LayerReads, Version;
        }

        static Counts Now() => new Counts
        {
            Plans = AvatarPlanCache.PlanBuilds,
            Walks = AvatarPlanCache.ProvidedWalks,
            LayerReads = LayerAnalysis.LayerReadBuilds,
            Version = AvatarPlanCache.Version,
        };

        static void AssertNothingDropped(Counts before, AvatarPlan plan, AvatarDoc menu, string what)
        {
            Assert.That(AvatarPlanCache.Version, Is.EqualTo(before.Version), what + ": no open view is told to refresh");
            Assert.That(AvatarPlanCache.GetFor(menu), Is.SameAs(plan), what + ": the plan stands");
            Assert.That(AvatarPlanCache.PlanBuilds, Is.EqualTo(before.Plans), what + ": nothing is planned again");
            Assert.That(AvatarPlanCache.ProvidedWalks, Is.EqualTo(before.Walks), what + ": the avatar's parameter providers are not walked again");
            Assert.That(LayerAnalysis.LayerReadBuilds, Is.EqualTo(before.LayerReads), what + ": the FX controller is not read again");
        }

        [Test]
        public void AMaterialTextureShaderOrMeshChangeDropsNoPlan()
        {
            var menu = WithRemoval();
            var plan = AvatarPlanCache.GetFor(menu);
            var texture = new Texture2D(4, 4);
            try
            {
                var before = Now();

                Publish(AssetProperties(avatar.MaterialA));
                Publish(AssetProperties(texture), AssetProperties(avatar.MaterialA.shader), AssetProperties(avatar.Mesh));

                AssertNothingDropped(before, plan, menu, "a material slider dragged in its own inspector");
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void ATransformMovedUnderTheAvatarDropsNoPlan()
        {
            var menu = WithRemoval();
            var plan = AvatarPlanCache.GetFor(menu);
            var before = Now();
            int raised = 0;
            Action<Transform> handler = _ => raised++;
            AvatarPlanCache.Changed += handler;
            try
            {
                avatar.Hat.transform.position += Vector3.up;
                Publish(Properties(avatar.Hat.transform));

                AssertNothingDropped(before, plan, menu, "a Scene view drag under the avatar");
                Assert.That(raised, Is.EqualTo(0));
            }
            finally
            {
                AvatarPlanCache.Changed -= handler;
            }
        }

        [Test]
        public void AHierarchyChangeOutsideEveryAvatarDropsNoPlan()
        {
            var menu = WithRemoval();
            var plan = AvatarPlanCache.GetFor(menu);
            var elsewhere = new GameObject("Elsewhere");
            int raised = 0;
            Action<Transform> handler = _ => raised++;
            AvatarPlanCache.Changed += handler;
            try
            {
                var before = Now();

                RaiseHierarchyChanged();

                AssertNothingDropped(before, plan, menu, "an object made outside every avatar");
                Assert.That(raised, Is.EqualTo(0), "and no open view is asked to refresh");
            }
            finally
            {
                AvatarPlanCache.Changed -= handler;
                Object.DestroyImmediate(elsewhere);
            }
        }

        [Test]
        public void AHierarchyChangeUnderOneAvatarDropsOnlyThatAvatar()
        {
            using (var other = TestAvatar.Create())
            {
                other.Root.name = "OtherAvatar";
                var menu = WithRemoval();
                var otherMenu = other.AddMenu();
                var plan = AvatarPlanCache.GetFor(menu);
                var otherPlan = AvatarPlanCache.GetFor(otherMenu);
                var raised = new List<Transform>();
                Action<Transform> handler = raised.Add;
                AvatarPlanCache.Changed += handler;
                try
                {
                    var before = Now();
                    new GameObject("Scarf").transform.SetParent(avatar.Root.transform);

                    RaiseHierarchyChanged();

                    Assert.That(AvatarPlanCache.GetFor(otherMenu), Is.SameAs(otherPlan), "the other avatar's hierarchy is as it was");
                    Assert.That(AvatarPlanCache.GetFor(menu), Is.Not.SameAs(plan), "the avatar that gained an object plans again");
                    Assert.That(AvatarPlanCache.PlanBuilds, Is.EqualTo(before.Plans + 1));
                    Assert.That(AvatarPlanCache.ProvidedWalks, Is.EqualTo(before.Walks + 1), "a new object can carry a parameter of its own");
                    Assert.That(LayerAnalysis.LayerReadBuilds, Is.EqualTo(before.LayerReads), "no controller changed, so none is read again");
                    Assert.That(raised, Is.EqualTo(new[] { avatar.Root.transform }), "the open views of that avatar are told at once");
                    Assert.That(AvatarPlanCache.LastInvalidationReason, Does.Contain("TestAvatar"));
                }
                finally
                {
                    AvatarPlanCache.Changed -= handler;
                    ForgetAnimatorUndo(other.Fx);
                }
            }
        }

        [Test]
        public void AnAnimatorAssetChangeDropsThePlansAndTheControllerReads()
        {
            var menu = WithRemoval();
            var plan = AvatarPlanCache.GetFor(menu);
            var before = Now();
            var hatLayer = avatar.Fx.layers.Single(l => l.name == "HatLayer");

            Publish(AssetProperties(hatLayer.stateMachine.states[0].state));

            Assert.That(AvatarPlanCache.GetFor(menu), Is.Not.SameAs(plan));
            Assert.That(AvatarPlanCache.PlanBuilds, Is.EqualTo(before.Plans + 1));
            Assert.That(AvatarPlanCache.ProvidedWalks, Is.EqualTo(before.Walks + 1));
            Assert.That(LayerAnalysis.LayerReadBuilds, Is.EqualTo(before.LayerReads + 1), "a state edited in the Animator window can change what a layer reads");
            Assert.That(AvatarPlanCache.Version, Is.GreaterThan(before.Version));
            Assert.That(AvatarPlanCache.LastInvalidationReason, Does.Contain("AnimatorState"));
        }

        [Test]
        public void AnUndoDropsThePlansAndTheControllerReads()
        {
            var menu = WithRemoval();
            var plan = AvatarPlanCache.GetFor(menu);
            var before = Now();

            RaiseUndo();

            Assert.That(AvatarPlanCache.GetFor(menu), Is.Not.SameAs(plan));
            Assert.That(AvatarPlanCache.PlanBuilds, Is.EqualTo(before.Plans + 1));
            Assert.That(AvatarPlanCache.ProvidedWalks, Is.EqualTo(before.Walks + 1));
            Assert.That(LayerAnalysis.LayerReadBuilds, Is.EqualTo(before.LayerReads + 1), "an undo can take back an Animator window edit");
            Assert.That(AvatarPlanCache.LastInvalidationReason, Is.EqualTo("undo or redo"));
        }

        [Test]
        public void ADestroyedOrForeignObjectDropsNoPlan()
        {
            var menu = WithRemoval();
            var plan = AvatarPlanCache.GetFor(menu);
            var gone = new GameObject("Gone");
            gone.transform.SetParent(avatar.Root.transform);
            int goneId = gone.GetInstanceID();
            Object.DestroyImmediate(gone);
            var loose = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            try
            {
                var before = Now();

                AvatarPlanCache.NoteChangedObjects(new Object[] { null, loose });
                Publish(Properties(goneId));

                AssertNothingDropped(before, plan, menu, "an object destroyed before the batch, and one that is neither a GameObject nor a component");
            }
            finally
            {
                Object.DestroyImmediate(loose);
            }
        }

        [Test]
        public void AChangeOutsideEveryAvatarDropsNoPlan()
        {
            var menu = WithRemoval();
            var plan = AvatarPlanCache.GetFor(menu);
            var elsewhere = new GameObject("Elsewhere");
            var lamp = elsewhere.AddComponent<Light>();
            try
            {
                var before = Now();

                Publish(Properties(elsewhere), Properties(lamp));

                AssertNothingDropped(before, plan, menu, "a renamed object and a changed light outside every avatar");
            }
            finally
            {
                Object.DestroyImmediate(elsewhere);
            }
        }

        [Test]
        public void ObjectsMadeDestroyedOrMovedAreTakenToTheirOwnAvatar()
        {
            using (var other = TestAvatar.Create())
            {
                other.Root.name = "OtherAvatar";
                var menu = avatar.AddMenu();
                var otherMenu = other.AddMenu();
                var plan = AvatarPlanCache.GetFor(menu);
                var otherPlan = AvatarPlanCache.GetFor(otherMenu);
                var elsewhere = new GameObject("Elsewhere");
                try
                {
                    var before = Now();
                    Publish(Created(elsewhere), StructureChanged(elsewhere));
                    AssertNothingDropped(before, plan, menu, "an object made and changed outside every avatar");
                    Assert.That(AvatarPlanCache.GetFor(otherMenu), Is.SameAs(otherPlan));

                    var scarf = new GameObject("Scarf");
                    scarf.transform.SetParent(avatar.Root.transform);
                    int scarfId = scarf.GetInstanceID();
                    Object.DestroyImmediate(scarf);
                    Publish(Destroyed(scarfId, avatar.Root));

                    Assert.That(AvatarPlanCache.GetFor(menu), Is.Not.SameAs(plan), "an object gone before the batch ran names its avatar through its parent");
                    Assert.That(AvatarPlanCache.GetFor(otherMenu), Is.SameAs(otherPlan), "the other avatar lost nothing");
                    Assert.That(AvatarPlanCache.LastInvalidationReason, Does.Contain("a child of GameObject 'TestAvatar'"));

                    plan = AvatarPlanCache.GetFor(menu);
                    avatar.Glasses.transform.SetParent(other.Root.transform);
                    Publish(Reparented(avatar.Glasses, avatar.Root, other.Root));

                    Assert.That(AvatarPlanCache.GetFor(menu), Is.Not.SameAs(plan), "the avatar the object left plans again");
                    Assert.That(AvatarPlanCache.GetFor(otherMenu), Is.Not.SameAs(otherPlan), "and so does the one it joined");
                }
                finally
                {
                    Object.DestroyImmediate(elsewhere);
                    ForgetAnimatorUndo(other.Fx);
                }
            }
        }

        [Test]
        public void AChangeUnderTheAvatarNamesItsObjectInTheReason()
        {
            var menu = WithRemoval();
            AvatarPlanCache.GetFor(menu);

            Publish(Properties(avatar.Body));

            var reason = AvatarPlanCache.LastInvalidationReason;
            Assert.That(reason, Does.Contain("SkinnedMeshRenderer"), "the slow refresh line says what kind of object changed");
            Assert.That(reason, Does.Contain("Body"));
            Assert.That(reason, Does.Contain("TestAvatar"), "and under which avatar");
        }

        [Test]
        public void APlanSaysWhyItWasBuiltEvenAfterADropElsewhere()
        {
            using (var other = TestAvatar.Create())
            {
                other.Root.name = "OtherAvatar";
                var menu = avatar.AddMenu();
                var otherMenu = other.AddMenu();
                AvatarPlanCache.GetFor(menu);
                AvatarPlanCache.GetFor(otherMenu);

                Publish(Properties(avatar.Body));
                AvatarPlanCache.InvalidateMenu(other.Root.transform);
                var plan = AvatarPlanCache.GetFor(menu);

                Assert.That(plan.BuildReason, Does.Contain("SkinnedMeshRenderer 'Body'"), "the slow refresh line names the change under this avatar");
                Assert.That(plan.BuildReason, Does.Contain("TestAvatar"));
                Assert.That(AvatarPlanCache.GetFor(otherMenu).BuildReason, Does.Contain("OtherAvatar"), "and the other avatar's plan its own");
                Assert.That(AvatarPlanCache.LastInvalidationReason, Does.Contain("OtherAvatar"), "the latest drop of all was the other avatar's");

                RaiseUndo();

                Assert.That(AvatarPlanCache.GetFor(menu).BuildReason, Is.EqualTo("undo or redo"));
                ForgetAnimatorUndo(other.Fx);
            }
        }

        [Test]
        public void ADeleteIsNotRebuiltAgainWhenUnityReportsItsOwnWrite()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var boot = menu.AddEntry(EntryKind.Toggle, "Boot");
            boot.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            view.Tree.SelectEntry(hat.id);

            view.Tree.DeleteSelected();
            Undo.ClearUndo(menu);
            var plan = AvatarPlanCache.GetFor(menu);
            int version = AvatarPlanCache.Version;
            // What Unity publishes for the delete at the end of the frame: the scene marked dirty by the
            // EditorUtility.SetDirty in EntryEditing.MarkChanged, and the component's own property change.
            Publish(SceneChanged(menu.gameObject.scene), Properties(menu));

            Assert.That(AvatarPlanCache.Version, Is.EqualTo(version), "the delete already rebuilt what it changed");
            Assert.That(AvatarPlanCache.GetFor(menu), Is.SameAs(plan));
        }

        [Test]
        public void OwnEditsFromTheTreeThePanelAndTheHeaderAreNotNews()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            menu.menuEdits.Add(new MenuEdit { id = "orphan", source = new List<string> { "Gone" }, remove = true });
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);

            void Check(string what, Action edit)
            {
                edit();
                Undo.ClearUndo(menu);
                var plan = AvatarPlanCache.GetFor(menu);
                int version = AvatarPlanCache.Version;
                Publish(SceneChanged(menu.gameObject.scene), Properties(menu));
                Assert.That(AvatarPlanCache.Version, Is.EqualTo(version), what + ": Unity's report of the write is not news");
                Assert.That(AvatarPlanCache.GetFor(menu), Is.SameAs(plan), what);
            }

            Check("add", () => view.Tree.AddEntry(EntryKind.Toggle));
            view.Tree.SelectEntry(hat.id);
            Check("duplicate", () => view.Tree.DuplicateSelected());
            view.Tree.SelectEntry(hat.id);
            Check("move", () => view.Tree.MoveSelectedTo(outfits.id));
            Check("hide an existing item", () =>
            {
                view.Tree.SelectKey(view.Tree.RowsOfKind(RowKind.Existing).First(r => r.Name == "RootToggle").Key);
                view.Detail.SetExistingHidden(true);
            });
            Check("remove a leftover edit", () => view.Header.RemoveOrphanEdit("orphan"));
        }

        [Test]
        public void ASceneMarkedChangedBySomethingElseStillDropsThePlansButNotTheControllerReads()
        {
            var menu = WithRemoval();
            var plan = AvatarPlanCache.GetFor(menu);
            var before = Now();

            Publish(SceneChanged(avatar.Root.scene));

            Assert.That(AvatarPlanCache.GetFor(menu), Is.Not.SameAs(plan), "a script that changed something without Undo and marked the scene dirty says nothing about what");
            Assert.That(LayerAnalysis.LayerReadBuilds, Is.EqualTo(before.LayerReads), "but a scene change is no controller change");
            Assert.That(AvatarPlanCache.LastInvalidationReason, Does.Contain("ChangeScene"));
        }

        [Test]
        public void AnExpressionsMenuChangeDropsThePlansButNotTheControllerReads()
        {
            var menu = WithRemoval();
            var plan = AvatarPlanCache.GetFor(menu);
            var before = Now();

            Publish(AssetProperties(avatar.ExpressionsMenu));

            Assert.That(AvatarPlanCache.GetFor(menu), Is.Not.SameAs(plan), "the tree shows the avatar's own menu");
            Assert.That(LayerAnalysis.LayerReadBuilds, Is.EqualTo(before.LayerReads));
            Assert.That(AvatarPlanCache.LastInvalidationReason, Does.Contain("VRCExpressionsMenu"));
        }

        [Test]
        public void AnExpressionsMenuReimportedFromDiskDropsThePlan()
        {
            var menu = avatar.AddMenu();
            GeneratedAssets.Ensure();
            const string path = GeneratedAssets.Folder + "/PlanDropMenu.asset";
            var asset = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            asset.controls = new List<VRCExpressionsMenu.Control> { TestAvatar.ToggleControl("Hat", "Existing") };
            AssetDatabase.CreateAsset(asset, path);
            try
            {
                avatar.Descriptor.expressionsMenu = asset;
                AvatarPlanCache.Invalidate();
                var plan = AvatarPlanCache.GetFor(menu);

                // A version control tool putting the file back is an import, never an ObjectChangeEvents change.
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

                Assert.That(AvatarPlanCache.GetFor(menu), Is.Not.SameAs(plan), "the tree shows the menu as it is on disk now");
                Assert.That(AvatarPlanCache.LastInvalidationReason, Does.Contain("PlanDropMenu"));
            }
            finally
            {
                avatar.Descriptor.expressionsMenu = avatar.ExpressionsMenu;
                AssetDatabase.DeleteAsset(path);
            }
        }

        [Test]
        public void AMoveDropsThePreviewPlanToo()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            var components = new[] { menu };
            var preview = AvatarPlanCache.PreviewPlanFor(avatar.Root.transform, components);
            view.Tree.SelectEntry(hat.id);

            view.Tree.MoveSelectedTo(outfits.id);
            Undo.ClearUndo(menu);
            Publish(SceneChanged(menu.gameObject.scene), Properties(menu));

            var moved = AvatarPlanCache.PreviewPlanFor(avatar.Root.transform, components);
            Assert.That(moved, Is.Not.SameAs(preview), "the scene preview lets the later control in the new order win a shared target");

            AvatarPlanCache.InvalidateOrder(null);

            Assert.That(AvatarPlanCache.PreviewPlanFor(avatar.Root.transform, components), Is.Not.SameAs(moved), "and a move outside every avatar drops every preview plan");
        }

        [Test]
        public void AMaterialATargetReadsDropsOnlyItsAvatarsPlan()
        {
            var menu = avatar.AddMenu();
            var shine = menu.AddEntry(EntryKind.Toggle, "Shine");
            shine.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "PixelSnap", type = PropertyValueType.Float, value = 1f });
            AvatarPlanCache.Invalidate();
            var plan = AvatarPlanCache.GetFor(menu);
            Assert.That(plan.MessagesFor(menu, shine).Any(m => m.Key == "ad.warn.property_missing"), Is.True, "the Standard shader has no PixelSnap");
            var before = Now();

            Publish(AssetProperties(avatar.MaterialB));

            AssertNothingDropped(before, plan, menu, "a material no target reads");

            avatar.MaterialA.shader = Shader.Find("Sprites/Default");
            Publish(AssetProperties(avatar.MaterialA));

            var next = AvatarPlanCache.GetFor(menu);
            Assert.That(next, Is.Not.SameAs(plan), "the body's own material changed its shader");
            Assert.That(next.MessagesFor(menu, shine).Any(m => m.Key == "ad.warn.property_missing"), Is.False, "and now has the property");
            Assert.That(AvatarPlanCache.ProvidedWalks, Is.EqualTo(before.Walks), "a material carries no parameter, so the avatar wide walk stands");
            Assert.That(AvatarPlanCache.LastInvalidationReason, Does.Contain("Material 'A'"));
        }

        /// <summary>
        /// A guard, not proof of a fix: AssetsReadBy already read the materials of every entry's property rows,
        /// triggers included, before triggers built them. It keeps that read set from being narrowed to toggles and
        /// radials. Only the first warning depends on trigger rows being checked by the model.
        /// </summary>
        [Test]
        public void AMaterialATriggerPropertyReadsDropsItsAvatarsPlan()
        {
            var menu = avatar.AddMenu();
            var pat = menu.AddEntry(EntryKind.Trigger, "Pat");
            pat.source = TriggerSource.Contact;
            pat.contactAnchor = avatar.Hat.transform;
            pat.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "PixelSnap", type = PropertyValueType.Float, value = 1f });
            AvatarPlanCache.Invalidate();
            var plan = AvatarPlanCache.GetFor(menu);
            Assert.That(plan.MessagesFor(menu, pat).Any(m => m.Key == "ad.warn.property_missing"), Is.True, "a trigger's row on a material without the property is reported");
            var before = Now();

            Publish(AssetProperties(avatar.MaterialB));

            AssertNothingDropped(before, plan, menu, "a material no target reads");

            avatar.MaterialA.shader = Shader.Find("Sprites/Default");
            Publish(AssetProperties(avatar.MaterialA));

            var next = AvatarPlanCache.GetFor(menu);
            Assert.That(next, Is.Not.SameAs(plan), "the material the trigger's row reads changed its shader, so the plan is dropped");
            Assert.That(next.MessagesFor(menu, pat).Any(m => m.Key == "ad.warn.property_missing"), Is.False, "and the warning goes with it");
            Assert.That(AvatarPlanCache.LastInvalidationReason, Does.Contain("Material 'A'"));
        }

        [Test]
        public void AMeshAShapeTargetReadsDropsItsAvatarsPlan()
        {
            var menu = avatar.AddMenu();
            var smile = menu.AddEntry(EntryKind.Toggle, "Smile");
            smile.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Smile", weight = 100f });
            AvatarPlanCache.Invalidate();
            var plan = AvatarPlanCache.GetFor(menu);
            Assert.That(plan.MessagesFor(menu, smile).Any(m => m.Key == "ad.warn.target_missing"), Is.True, "the mesh has no Smile shape yet");

            var deltas = new Vector3[avatar.Mesh.vertexCount];
            avatar.Mesh.AddBlendShapeFrame("Smile", 100f, deltas, null, null);
            Publish(AssetProperties(avatar.Mesh));

            var next = AvatarPlanCache.GetFor(menu);
            Assert.That(next, Is.Not.SameAs(plan));
            Assert.That(next.MessagesFor(menu, smile).Any(m => m.Key == "ad.warn.target_missing"), Is.False, "the shape is there now");
        }

        [Test]
        public void ANoteForAWriteThatNeverHappenedHidesNoLaterWrite()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = true });
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);

            view.Detail.RenameSubmenu(outfits, "   ");
            Assert.That(outfits.name, Is.EqualTo("Outfits"), "a blank name writes nothing");
            view.Tree.SelectEntry(hat.id);
            view.Detail.Q<TargetListView>("ad-objects").WriteField(0, nameof(ObjectTarget.activeWhenOn), p => p.boolValue = false, live: true);
            Undo.ClearUndo(menu);
            Assert.That(hat.objects[0].activeWhenOn, Is.False);
            int version = AvatarPlanCache.Version;

            // A live write leaves its plan to Unity's own report of it.
            Publish(Properties(menu));

            Assert.That(AvatarPlanCache.Version, Is.GreaterThan(version), "the live write is news");

            AvatarPlanCache.NoteSelfEdit(menu);
            var so = new SerializedObject(menu);
            so.FindProperty(nameof(AvatarDoc.menuName)).stringValue = "Later";
            so.ApplyModifiedProperties();
            Undo.ClearUndo(menu);
            version = AvatarPlanCache.Version;
            Publish(Properties(menu));

            Assert.That(AvatarPlanCache.Version, Is.GreaterThan(version), "a note made before a later write is not spent on it");
        }

        [Test]
        public void ASceneMarkFromAnotherSceneIsNotTakenForAnOwnWrite()
        {
            var menu = WithRemoval();
            var plan = AvatarPlanCache.GetFor(menu);
            var before = Now();
            AvatarPlanCache.NoteSelfEdit(menu);

            Publish(SceneChanged(default(Scene)), Properties(menu));

            Assert.That(AvatarPlanCache.GetFor(menu), Is.Not.SameAs(plan), "the write marked its own scene; another scene was marked by something else");
            Assert.That(LayerAnalysis.LayerReadBuilds, Is.EqualTo(before.LayerReads), "and a scene mark is no controller change");
        }

        [Test]
        public void AChangeAboveTheAvatarRootReachesTheAvatarBelowIt()
        {
            var group = new GameObject("Group");
            try
            {
                avatar.Root.transform.SetParent(group.transform);
                var menu = avatar.AddMenu();
                var plan = AvatarPlanCache.GetFor(menu);

                Publish(StructureChanged(group));
                Assert.That(AvatarPlanCache.GetFor(menu), Is.Not.SameAs(plan), "a prefab merge reports the group that holds the avatar");

                plan = AvatarPlanCache.GetFor(menu);
                Publish(PrefabInstancesUpdated(group));
                Assert.That(AvatarPlanCache.GetFor(menu), Is.Not.SameAs(plan), "and so does a prefab instance update");

                plan = AvatarPlanCache.GetFor(menu);
                Publish(Created(group));
                Assert.That(AvatarPlanCache.GetFor(menu), Is.Not.SameAs(plan), "and a group made with the avatar inside it");

                plan = AvatarPlanCache.GetFor(menu);
                var before = Now();
                var elsewhere = new GameObject("Elsewhere");
                try
                {
                    Publish(StructureChanged(elsewhere));
                    AssertNothingDropped(before, plan, menu, "a group that holds no avatar");
                }
                finally
                {
                    Object.DestroyImmediate(elsewhere);
                }
            }
            finally
            {
                avatar.Root.transform.SetParent(null);
                Object.DestroyImmediate(group);
            }
        }

        [Test]
        public void AChangeUnderALooseAvatarDocReachesItsView()
        {
            var outfit = new GameObject("Outfit");
            var piece = new GameObject("Piece");
            piece.transform.SetParent(outfit.transform);
            var loose = outfit.AddComponent<AvatarDoc>();
            var elsewhere = new GameObject("Elsewhere");
            try
            {
                loose.AddEntry(EntryKind.Toggle, "Piece").objects.Add(new ObjectTarget { target = piece });
                AvatarPlanCache.GetFor(loose);
                int version = AvatarPlanCache.Version;

                Publish(Properties(piece));

                Assert.That(AvatarPlanCache.Version, Is.GreaterThan(version), "an AvatarDoc outside every avatar plans afresh, and its view hears its target changed");

                version = AvatarPlanCache.Version;
                Publish(Properties(elsewhere));

                Assert.That(AvatarPlanCache.Version, Is.EqualTo(version), "an object under no AvatarDoc at all changes nothing");
            }
            finally
            {
                Object.DestroyImmediate(outfit);
                Object.DestroyImmediate(elsewhere);
            }
        }

        [Test]
        public void ARemovedAvatarIsForgottenAndItsCopyPlansAgain()
        {
            var twin = TestAvatar.Create();
            try
            {
                avatar.Root.AddComponent<VRC.Core.PipelineManager>().blueprintId = "avtr_plan_drop_gone";
                twin.Root.AddComponent<VRC.Core.PipelineManager>().blueprintId = "avtr_plan_drop_gone";
                var menu = avatar.AddMenu();
                var twinMenu = twin.AddMenu();
                AvatarPlanCache.Invalidate();
                AvatarPlanCache.GetFor(twinMenu);
                var plan = AvatarPlanCache.GetFor(menu);
                int version = AvatarPlanCache.Version;

                ForgetAnimatorUndo(twin.Fx);
                Object.DestroyImmediate(twin.Root);
                RaiseHierarchyChanged();

                Assert.That(AvatarPlanCache.GetFor(menu), Is.Not.SameAs(plan), "the copy compared itself with the avatar that is gone");
                Assert.That(AvatarPlanCache.Version, Is.GreaterThan(version));
                Assert.That(AvatarPlanCache.LastInvalidationReason, Does.Contain("an avatar that was removed"));
            }
            finally
            {
                twin.Dispose();
            }
        }

        [Test]
        public void TheSlowRefreshLineNamesADropOnlyWhenTheRefreshBuiltThePlan()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            var lines = new List<string>();
            var oldLog = AvatarDoctorView.SlowRefreshLog;
            var oldThreshold = AvatarDoctorView.SlowRefreshThreshold;
            AvatarDoctorView.SlowRefreshLog = lines.Add;
            AvatarDoctorView.SlowRefreshThreshold = 0;
            try
            {
                AvatarPlanCache.InvalidateMenu(avatar.Root.transform);
                view.Refresh();
                view.Refresh();

                Assert.That(lines.Count, Is.EqualTo(2), "every refresh counts as slow at a threshold of 0");
                Assert.That(lines[0], Does.Contain("a menu edit in 'TestAvatar'"), "the refresh that planned names the drop behind it");
                Assert.That(lines[1], Does.Contain(L.Text("ad.log.plan_reused")), "the one that reused the plan says so instead of naming an older drop");
                Assert.That(lines[1], Does.Not.Contain("a menu edit"));
            }
            finally
            {
                AvatarDoctorView.SlowRefreshLog = oldLog;
                AvatarDoctorView.SlowRefreshThreshold = oldThreshold;
            }
        }

        // A drop while a row menu is open, in a shown window with real pointer events.

        static EditorWindow WindowOf(VisualElement element) =>
            Resources.FindObjectsOfTypeAll<EditorWindow>().First(w => w.rootVisualElement.panel == element.panel && w.rootVisualElement.Contains(element));

        static void Pointer<T>(VisualElement target, EventType type, int button) where T : PointerEventBase<T>, new()
        {
            using (var e = PointerEventBase<T>.GetPooled(new Event { type = type, button = button, mousePosition = target.worldBound.center }))
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        static void RightClick(VisualElement target)
        {
            var at = target.worldBound.center;
            Pointer<PointerDownEvent>(target, EventType.MouseDown, 1);
            var top = target.panel?.Pick(at) ?? target;
            using (var e = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 1, mousePosition = at }))
            {
                e.target = top;
                top.SendEvent(e);
            }
        }

        static void Key(VisualElement target, KeyCode code, char character = '\0')
        {
            using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = code, character = character }))
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        [Test]
        public void ADropWhileARowMenuIsOpenReachesTheViewOnceTheMenuCloses()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            LayoutProbe.Measure(view, () =>
            {
                var window = WindowOf(view);
                window.minSize = new Vector2(400f, 900f);
                window.maxSize = new Vector2(400f, 900f);
                LayoutProbe.Validate(view);
                var root = window.rootVisualElement;
                try
                {
                    var row = view.Tree.Tree.Query(className: "ad-tree-row").ToList().First(r => (r.userData as TreeRow)?.EntryId == hat.id);
                    RightClick(row);
                    var open = root.Q<AdMenu>();
                    Assert.That(open, Is.Not.Null, "a right click on a row opens its menu");
                    var plan = view.Plan;

                    Publish(Properties(avatar.Body));
                    view.CheckVersion();

                    Assert.That(open.IsClosed, Is.False, "the menu stays open");
                    Assert.That(view.RefreshHeldByMenu, Is.True, "the drop is remembered while the menu is open");
                    Assert.That(view.Plan, Is.SameAs(plan));

                    Key(open, KeyCode.Escape, (char)27);
                    view.CheckVersion();

                    Assert.That(view.RefreshHeldByMenu, Is.False, "the held refresh ran once the menu closed");
                    Assert.That(view.Plan, Is.Not.SameAs(plan), "and the view shows the plan built after the drop");
                }
                finally
                {
                    foreach (var left in root.Query<AdMenu>().ToList()) left.Close(false);
                }
            }, 400f, 900f);
        }
    }
}
#endif
