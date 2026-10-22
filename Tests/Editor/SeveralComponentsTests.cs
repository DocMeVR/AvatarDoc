#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Several AvatarDoc components on one avatar: groups that belong to their component, the prefix a new component takes, and the warnings that tell two components apart.</summary>
    public class SeveralComponentsTests
    {
        TestAvatar avatar;
        readonly List<Object> made = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
            avatar = TestAvatar.Create();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            foreach (var obj in made) if (obj != null) Object.DestroyImmediate(obj);
            made.Clear();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        /// <summary>The avatar's own component on its root, with the prefix every component made with 0.6.0 or earlier has.</summary>
        AvatarDoc RootMenu()
        {
            var menu = avatar.AddMenu();
            menu.parameterPrefix = "AD";
            return menu;
        }

        /// <summary>A component on its own object under the avatar's root, as a prefab brings one.</summary>
        AvatarDoc ChildMenu(string objectName, string prefix)
        {
            var host = new GameObject(objectName);
            host.transform.SetParent(avatar.Root.transform, false);
            var menu = host.AddComponent<AvatarDoc>();
            menu.parameterPrefix = prefix;
            return menu;
        }

        /// <summary>An object under the avatar's root for a toggle to show.</summary>
        GameObject Piece(string name)
        {
            var piece = new GameObject(name);
            piece.transform.SetParent(avatar.Root.transform, false);
            return piece;
        }

        MenuEntry Toggle(AvatarDoc menu, string name)
        {
            var entry = menu.AddEntry(EntryKind.Toggle, name);
            entry.objects.Add(new ObjectTarget { target = Piece(name) });
            return entry;
        }

        MenuEntry Member(AvatarDoc menu, string name, string group)
        {
            var entry = Toggle(menu, name);
            entry.exclusiveGroup = group;
            return entry;
        }

        static AvatarDoctorView View(AvatarDoc menu) => new AvatarDoctorView(new SerializedObject(menu), menu, false, new MemoryViewPrefs());

        static ControlBinding Binding(AvatarPlan plan, MenuEntry entry) => plan.Plan.Bindings.Single(b => b.Key.Entry == entry).Value;

        static List<ModelMessage> Messages(AvatarDoc menu, string key) => AvatarPlanCache.GetFor(menu).Messages.Where(m => m.Says(key)).ToList();

        /// <summary>Builds the avatar in place, the way the upload does, and returns its FX controller.</summary>
        AnimatorController Build()
        {
            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error).Select(e => e.TheError.ToString()), Is.Empty);
            return (AnimatorController)avatar.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
        }

        static IEnumerable<string> States(AnimatorController fx, string layer) => fx.layers.Single(l => l.name == layer).stateMachine.states.Select(s => s.state.name);

        /// <summary>Opens the Exclusive group list of the selected toggle and returns it.</summary>
        static AdMenu OpenGroupList(AvatarDoctorView view)
        {
            UiTestEvents.SimulateClick(view.Detail.Q<Button>("ad-group-pick"));
            var open = view.panel.visualTree.Q<AdMenu>();
            Assert.That(open, Is.Not.Null, "the arrow opens the group list");
            return open;
        }

        static void CloseMenus(AvatarDoctorView view)
        {
            if (view.panel == null) return;
            foreach (var left in view.panel.visualTree.Query<AdMenu>().ToList()) left.Close(false);
        }

        static void OpenAdvanced(AvatarDoctorView view)
        {
            var fold = view.Detail.Q<AdvancedFold>();
            if (fold != null && !fold.IsOpen) fold.ToggleOpen();
            LayoutProbe.Validate(view);
        }

        // ---- groups belong to their component -------------------------------------------------------------------

        [Test]
        public void TwoComponentsWithAnOutfitsGroupEachBuildAGroupOfTheirOwn()
        {
            var own = RootMenu();
            var jacket = Member(own, "Jacket", "Outfits");
            var hoodie = Member(own, "Hoodie", "Outfits");
            var prefab = ChildMenu("Mayo menu", "Mayo");
            var kimono = Member(prefab, "Kimono", "Outfits");
            var dress = Member(prefab, "Dress", "Outfits");

            var plan = AvatarPlanCache.GetFor(own);

            var groups = plan.Plan.Parameters.Where(p => p.GroupName == "Outfits").ToList();
            Assert.That(groups.Select(p => p.Name), Is.EquivalentTo(new[] { "AD/Outfits", "Mayo/Outfits" }), "each group is named after its own component's prefix");
            Assert.That(groups.Single(p => p.Name == "AD/Outfits").Controls.Select(c => c.Entry), Is.EqualTo(new[] { jacket, hoodie }));
            Assert.That(groups.Single(p => p.Name == "Mayo/Outfits").Controls.Select(c => c.Entry), Is.EqualTo(new[] { kimono, dress }));
            Assert.That(Binding(plan, kimono).Value, Is.EqualTo(1f), "each group numbers its own members");
            Assert.That(Binding(plan, dress).Value, Is.EqualTo(2f));

            var fx = Build();
            var layers = fx.layers.Where(l => l.name.StartsWith("AD: Outfits")).ToList();
            Assert.That(layers.Count, Is.EqualTo(2), "two groups, two layers");
            Assert.That(layers.Select(l => string.Join(",", l.stateMachine.states.Select(s => s.state.name))),
                Is.EquivalentTo(new[] { "Off,Jacket,Hoodie", "Off,Kimono,Dress" }), "a toggle of one group never turns off a toggle of the other");
        }

        [Test]
        public void ALinkedMemberJoinsTheOtherComponentsGroupAndUsesItsSettings()
        {
            var own = RootMenu();
            own.groups.Add(new ExclusiveGroup { name = "Outfits", saved = false, synced = true });
            var jacket = Member(own, "Jacket", "Outfits");
            var hoodie = Member(own, "Hoodie", "Outfits");
            var prefab = ChildMenu("Mayo menu", "Mayo");
            prefab.groups.Add(new ExclusiveGroup { name = "Outfits", saved = true, synced = false });
            var kimono = Member(prefab, "Kimono", "Outfits");
            kimono.groupComponent = own;
            var dress = Member(prefab, "Dress", "Outfits");

            var plan = AvatarPlanCache.GetFor(own);

            var joined = plan.Plan.Parameters.Single(p => p.Name == "AD/Outfits");
            Assert.That(joined.Controls.Select(c => c.Entry), Is.EqualTo(new[] { jacket, hoodie, kimono }), "the linked Kimono is a member of the avatar's own group");
            Assert.That(Binding(plan, kimono).Value, Is.EqualTo(3f));
            Assert.That((joined.Saved, joined.Synced), Is.EqualTo((false, true)), "the group's settings come from the component it belongs to");
            var left = plan.Plan.Parameters.Single(p => p.Name == "Mayo/Outfits");
            Assert.That(left.Controls.Select(c => c.Entry), Is.EqualTo(new[] { dress }), "the Dress stays in its own component's group");
            Assert.That(left.Synced, Is.False);

            var provided = ParameterInfo.ForUI.GetParametersForObject(avatar.Root).ToList();
            Assert.That(provided.Where(p => p.EffectiveName == "AD/Outfits").Select(p => p.Source), Is.EqualTo(new Object[] { own }), "the group's own component provides its parameter");

            var fx = Build();
            Assert.That(States(fx, "AD: Outfits"), Is.EqualTo(new[] { "Off", "Jacket", "Hoodie", "Kimono" }));
        }

        [Test]
        public void TheLinkSurvivesAPrefabInstanceAndAChangeToItsPrefab()
        {
            var folder = TempAssetFolder.Create("AvatarDocSeveralComponentsTests");
            var path = folder + "/Kimono set.prefab";
            GameObject source = null, instance = null;
            try
            {
                var own = RootMenu();
                Member(own, "Jacket", "Outfits");
                source = new GameObject("Kimono set");
                var shipped = source.AddComponent<AvatarDoc>();
                shipped.parameterPrefix = "Kimono";
                shipped.AddEntry(EntryKind.Toggle, "Kimono").exclusiveGroup = "Outfits";
                var prefab = PrefabUtility.SaveAsPrefabAsset(source, path);
                Object.DestroyImmediate(source);
                source = null;
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, avatar.Root.transform);
                var placed = instance.GetComponent<AvatarDoc>();
                AvatarPlanCache.Invalidate();

                var view = View(placed);
                LayoutProbe.Measure(view, () =>
                {
                    try
                    {
                        view.Tree.SelectEntry(placed.entries[0].id);
                        LayoutProbe.Validate(view);
                        var list = OpenGroupList(view);
                        list.Commit(list.Items.ToList().FindIndex(i => i.Text == L.Aside("Outfits", avatar.Root.name)));
                    }
                    finally
                    {
                        CloseMenus(view);
                    }
                });

                Assert.That(placed.entries[0].groupComponent, Is.SameAs(own), "picking the avatar's group links the prefab's toggle to it");
                var modifications = PrefabUtility.GetPropertyModifications(instance) ?? new PropertyModification[0];
                Assert.That(modifications.Any(m => m.target is AvatarDoc && m.propertyPath != null && m.propertyPath.EndsWith(nameof(MenuEntry.groupComponent)) && m.objectReference == own), Is.True,
                    "the link is an override of the instance");

                var contents = PrefabUtility.LoadPrefabContents(path);
                contents.GetComponent<AvatarDoc>().AddEntry(EntryKind.Toggle, "Obi");
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                PrefabUtility.UnloadPrefabContents(contents);

                Assert.That(placed.entries.Count, Is.EqualTo(2), "the instance follows its prefab");
                Assert.That(placed.entries[0].groupComponent, Is.SameAs(own), "and keeps its link");
                AvatarPlanCache.Invalidate();
                var plan = AvatarPlanCache.GetFor(own);
                var groups = string.Join("; ", plan.Plan.Parameters.Where(p => p.GroupName != null).Select(p => p.Name + ": " + string.Join(",", p.Controls.Select(c => c.Entry.name))));
                Assert.That(plan.Plan.Parameters.Single(p => p.Name == "AD/Outfits").Controls.Select(c => c.Entry.name).ToList(), Is.EqualTo(new[] { "Jacket", "Kimono" }), groups);
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                if (source != null) Object.DestroyImmediate(source);
                AssetDatabase.DeleteAsset(path);
                TempAssetFolder.Delete(folder);
            }
        }

        [Test]
        public void ACopyToAnotherAvatarPointsTheLinkAtThatAvatarsOwnComponent()
        {
            var other = TestAvatar.Create();
            other.Root.name = "TestAvatar B";
            var previous = MenuCopy.SceneAvatars;
            MenuCopy.SceneAvatars = () => new[] { avatar.Root.transform, other.Root.transform };
            try
            {
                var own = RootMenu();
                Member(own, "Jacket", "Outfits");
                var otherOwn = other.AddMenu();
                otherOwn.parameterPrefix = "AD";
                otherOwn.AddEntry(EntryKind.Toggle, "Jacket").exclusiveGroup = "Outfits";
                var prefab = ChildMenu("Kimono set", "Kimono");
                var kimono = prefab.AddEntry(EntryKind.Toggle, "Kimono");
                kimono.exclusiveGroup = "Outfits";
                kimono.groupComponent = own;
                new GameObject("Kimono set").transform.SetParent(other.Root.transform, false);

                var copy = MenuCopy.Apply(new CopyDraft { source = prefab, target = other.Root.transform }, out var status);

                Assert.That(copy, Is.Not.Null, "the copy is refused: " + status);
                Assert.That(copy.transform.parent, Is.SameAs(other.Root.transform));
                Assert.That(copy.entries.Single().groupComponent, Is.SameAs(otherOwn), "the link points at the other avatar's component at the same place");
                Assert.That(kimono.groupComponent, Is.SameAs(own), "the source keeps its own");
                AvatarPlanCache.Invalidate();
                var plan = AvatarPlanCache.GetFor(otherOwn);
                Assert.That(plan.Plan.Parameters.Single(p => p.GroupName == "Outfits").Controls.Select(c => c.Component), Is.EqualTo(new[] { otherOwn, copy }));
            }
            finally
            {
                MenuCopy.SceneAvatars = previous;
                other.Dispose();
            }
        }

        [Test]
        public void ThePreviewKeepsTwoSameNamedGroupsApartAndALinkedMemberInItsGroup()
        {
            var own = RootMenu();
            var jacket = Member(own, "Jacket", "Outfits");
            jacket.defaultOn = true;
            var prefab = ChildMenu("Mayo menu", "Mayo");
            var kimono = Member(prefab, "Kimono", "Outfits");
            kimono.defaultOn = true;
            var plan = AvatarPlanCache.GetFor(own);

            Assert.That(PreviewState.Effective(jacket, plan.Tree, plan.Plan), Is.EqualTo(1f));
            Assert.That(PreviewState.Effective(kimono, plan.Tree, plan.Plan), Is.EqualTo(1f), "the prefab's group has its own default");
            PreviewState.SetOn(kimono, plan.Tree);
            Assert.That(PreviewState.Effective(jacket, plan.Tree, plan.Plan), Is.EqualTo(1f), "turning the Kimono on leaves the other group alone");

            PreviewState.ClearAll();
            kimono.groupComponent = own;
            AvatarPlanCache.Invalidate();
            plan = AvatarPlanCache.GetFor(own);
            PreviewState.SetOn(kimono, plan.Tree);
            Assert.That(PreviewState.Effective(jacket, plan.Tree, plan.Plan), Is.EqualTo(0f), "a linked Kimono turns the Jacket off");
            Assert.That(PreviewState.Effective(kimono, plan.Tree, plan.Plan), Is.EqualTo(1f));
        }

        // ---- Join them --------------------------------------------------------------------------------------------

        [Test]
        public void TheJoinNoteNamesBothComponentsOnlyForSameNamedGroupsWithoutALink()
        {
            var own = RootMenu();
            var jacket = Member(own, "Jacket", "Outfits");
            Member(own, "Cap", "Hats");
            var prefab = ChildMenu("Mayo menu", "Mayo");
            var kimono = Member(prefab, "Kimono", "Outfits");
            var dress = Member(prefab, "Dress", "Outfits");
            Member(prefab, "Beret", "Hat");

            var note = Messages(own, "ad.info.group_same_name").Single();

            Assert.That(note.Severity, Is.EqualTo(MessageSeverity.Info), "two groups apart is a correct setup, so it is a note, not a warning");
            Assert.That(note.Args, Is.EqualTo(new object[] { own, prefab, "Outfits" }), "it names both components, the one whose toggles would move second, and the group");
            Assert.That(note.EntryIds, Is.EquivalentTo(new[] { jacket.id, kimono.id, dress.id }), "it shows on every member of both groups");
            var text = UiText.Format(note);
            Assert.That(text, Does.Contain(avatar.Root.name).And.Contain("Mayo menu").And.Contain(L.Text("ad.ui.group.join")));

            kimono.groupComponent = own;
            AvatarPlanCache.Invalidate();
            Assert.That(Messages(own, "ad.info.group_same_name").Single().EntryIds, Is.EquivalentTo(new[] { jacket.id, kimono.id, dress.id }), "the Dress still has a group of that name of its own");

            dress.groupComponent = own;
            AvatarPlanCache.Invalidate();
            Assert.That(Messages(own, "ad.info.group_same_name"), Is.Empty, "linked members join the group, so nothing is left to say");
        }

        [Test]
        public void TwoGroupsKeptApartOnPurposeLeaveTheAvatarReadyToUpload()
        {
            var own = RootMenu();
            Member(own, "Jacket", "Outfits");
            var prefab = ChildMenu("Mayo menu", "Mayo");
            Member(prefab, "Kimono", "Outfits");
            prefab.locked = true;

            Assert.That(AvatarPlanCache.GetFor(own).Messages.Where(m => m.Severity != MessageSeverity.Info).Select(m => m.Key), Is.Empty);
            foreach (var menu in new[] { own, prefab })
            {
                var view = View(menu);
                Assert.That(view.Q<Label>("ad-ready-label").text, Is.EqualTo(L.Text("ad.ui.ready")), "every inspector of the avatar says it is ready");
            }
        }

        [Test]
        public void JoinThemLinksTheOtherComponentsMembersInOneUndoStep()
        {
            var own = RootMenu();
            var jacket = Member(own, "Jacket", "Outfits");
            var prefab = ChildMenu("Mayo menu", "Mayo");
            var kimono = Member(prefab, "Kimono", "Outfits");
            var dress = Member(prefab, "Dress", "Outfits");
            var view = View(own);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(jacket.id);
                LayoutProbe.Validate(view);
                var button = view.Detail.Q<Button>("ad-group-join");
                Assert.That(button, Is.Not.Null, "the note carries the way out");
                Assert.That(button.text, Is.EqualTo(L.Text("ad.ui.group.join")));
                Assert.That(button.tooltip, Is.EqualTo(string.Format(L.Text("ad.ui.group.join.tip"), "Mayo menu", avatar.Root.name)), "the tooltip says whose toggles move where");
                Assert.That(button.enabledSelf, Is.True);
                Assert.That(button.parent.ClassListContains("ad-message-actions"), Is.True, "in the row right under the note");

                Undo.IncrementCurrentGroup();
                UiTestEvents.SimulateClick(button);

                Assert.That(kimono.groupComponent, Is.SameAs(own));
                Assert.That(dress.groupComponent, Is.SameAs(own));
                Assert.That(jacket.groupComponent, Is.Null, "the avatar's own toggle stays in its own group");
                Assert.That(view.Plan.Plan.Parameters.Single(p => p.GroupName == "Outfits").Name, Is.EqualTo("AD/Outfits"), "one group, which keeps the avatar's own name");
                Assert.That(view.Detail.Q<Button>("ad-group-join"), Is.Null, "the note is gone");

                Undo.PerformUndo();

                Assert.That((kimono.groupComponent, dress.groupComponent), Is.EqualTo(((AvatarDoc)null, (AvatarDoc)null)), "one undo step takes both links back");
            });
        }

        [Test]
        public void JoinThemMovesTheAvatarsTogglesWhenThePrefabIsLocked()
        {
            var own = RootMenu();
            var jacket = Member(own, "Jacket", "Outfits");
            var hoodie = Member(own, "Hoodie", "Outfits");
            var prefab = ChildMenu("Mayo menu", "Mayo");
            var kimono = Member(prefab, "Kimono", "Outfits");
            prefab.locked = true;

            var note = Messages(own, "ad.info.group_same_name").Single();
            Assert.That(note.Args, Is.EqualTo(new object[] { prefab, own, "Outfits" }), "the locked prefab keeps its toggles, so the avatar's toggles are the ones that move");
            Assert.That(UiText.Format(note), Does.Contain(string.Format("the toggles of {0} join the group of {1}", avatar.Root.name, "Mayo menu")));

            var view = View(own);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(jacket.id);
                LayoutProbe.Validate(view);
                var button = view.Detail.Q<Button>("ad-group-join");
                Assert.That(button.enabledSelf, Is.True, "one side is unlocked, so Join them works");
                Assert.That(button.tooltip, Is.EqualTo(string.Format(L.Text("ad.ui.group.join.tip"), avatar.Root.name, "Mayo menu")));

                Undo.IncrementCurrentGroup();
                UiTestEvents.SimulateClick(button);
            });

            Assert.That((jacket.groupComponent, hoodie.groupComponent), Is.EqualTo((prefab, prefab)), "the avatar's toggles join the prefab's group");
            Assert.That(kimono.groupComponent, Is.Null, "nothing of the locked prefab changed");
            AvatarPlanCache.Invalidate();
            var joined = AvatarPlanCache.GetFor(own).Plan.Parameters.Single(p => p.GroupName == "Outfits");
            Assert.That((joined.Name, joined.Controls.Count), Is.EqualTo(("Mayo/Outfits", 3)));
            Undo.PerformUndo();
            Assert.That((jacket.groupComponent, hoodie.groupComponent), Is.EqualTo(((AvatarDoc)null, (AvatarDoc)null)), "one undo step");
        }

        [Test]
        public void JoinThemIsGreyedOutOnlyWhileBothComponentsAreLocked()
        {
            var own = RootMenu();
            var jacket = Member(own, "Jacket", "Outfits");
            var prefab = ChildMenu("Mayo menu", "Mayo");
            var kimono = Member(prefab, "Kimono", "Outfits");
            prefab.locked = true;
            own.locked = true;
            var view = View(prefab);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(kimono.id);
                LayoutProbe.Validate(view);
                var button = view.Detail.Q<Button>("ad-group-join");
                Assert.That(button, Is.Not.Null);
                Assert.That(button.enabledSelf, Is.False, "neither side's toggles can move");
                Assert.That(button.tooltip, Is.EqualTo(L.Text("ad.ui.group.join.locked.tip")));
            });

            var tree = AvatarPlanCache.GetFor(own).Tree;
            Assert.That(EntryEditing.JoinGroup(tree, prefab, "Outfits", own), Is.False);
            Assert.That(EntryEditing.JoinGroup(tree, own, "Outfits", prefab), Is.False);
            Assert.That((jacket.groupComponent, kimono.groupComponent), Is.EqualTo(((AvatarDoc)null, (AvatarDoc)null)), "nothing changed");
        }

        // ---- the group list ---------------------------------------------------------------------------------------

        [Test]
        public void TheGroupListOffersTheOtherComponentsGroupsByComponentAndPickingOneSetsTheLink()
        {
            var own = RootMenu();
            Member(own, "Jacket", "Outfits");
            // In a group of its own already, so a pick changes no row of the panel and it refreshes in place.
            var bag = Member(own, "Bag", "Bags");
            var prefab = ChildMenu("Mayo menu", "Mayo");
            Member(prefab, "Kimono", "Outfits");
            Member(prefab, "Beret", "Hats");
            var view = View(own);
            LayoutProbe.Measure(view, () =>
            {
                try
                {
                    view.Tree.SelectEntry(bag.id);
                    LayoutProbe.Validate(view);
                    var list = OpenGroupList(view);
                    var texts = list.Items.Select(i => i.Text).ToList();
                    Assert.That(texts, Is.EqualTo(new[] { L.Text("ad.ui.group.none"), "Bags", "Outfits", L.Aside("Hats", "Mayo menu"), L.Aside("Outfits", "Mayo menu") }),
                        "the component's own groups, then the other component's, each with its component");
                    Assert.That(list.Items.Single(i => i.Checked).Text, Is.EqualTo("Bags"));

                    list.Commit(texts.IndexOf(L.Aside("Outfits", "Mayo menu")));

                    Assert.That(bag.exclusiveGroup, Is.EqualTo("Outfits"));
                    Assert.That(bag.groupComponent, Is.SameAs(prefab), "the pick stores a link to that component");
                    Assert.That(view.Plan.Plan.Parameters.Single(p => p.Name == "Mayo/Outfits").Controls.Select(c => c.Entry), Has.Member(bag));
                    LayoutProbe.Validate(view);
                    var note = view.Detail.Q<Label>("ad-group-owner");
                    Assert.That(note, Is.Not.Null);
                    Assert.That(note.ClassListContains("ad-hidden"), Is.False);
                    Assert.That(note.text, Is.EqualTo(string.Format(L.Text("ad.ui.group.linked"), "Mayo menu")), "the row says whose group it is");

                    list = OpenGroupList(view);
                    Assert.That(list.Items.Single(i => i.Checked).Text, Is.EqualTo(L.Aside("Outfits", "Mayo menu")), "the linked group is ticked, not the own one of the same name");
                    list.Commit(list.Items.ToList().FindIndex(i => i.Text == "Outfits"));

                    Assert.That(bag.groupComponent, Is.Null, "picking the own group clears the link");
                    LayoutProbe.Validate(view);
                    Assert.That(view.Detail.Q<Label>("ad-group-owner").ClassListContains("ad-hidden"), Is.True);
                }
                finally
                {
                    CloseMenus(view);
                }
            });
        }

        [Test]
        public void TypingANewGroupNameMakesAGroupOfTheTogglesOwnComponent()
        {
            var own = RootMenu();
            var prefab = ChildMenu("Mayo menu", "Mayo");
            Member(prefab, "Kimono", "Outfits");
            var bag = Member(own, "Bag", "Outfits");
            bag.groupComponent = prefab;
            var view = View(own);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(bag.id);
                LayoutProbe.Validate(view);
                Undo.IncrementCurrentGroup();
                view.Detail.Q<TextField>("ad-group").value = "Bags";
            });

            Assert.That(bag.exclusiveGroup, Is.EqualTo("Bags"));
            Assert.That(bag.groupComponent, Is.Null, "a typed name is a group of the toggle's own component");
            Undo.PerformUndo();
            Assert.That((bag.exclusiveGroup, bag.groupComponent), Is.EqualTo(("Outfits", prefab)), "one undo step puts the name and the link back");
        }

        // ---- the prefix a new component takes -----------------------------------------------------------------------

        [Test]
        public void ANewComponentTakesADOnTheAvatarRootAndItsObjectsNameElsewhere()
        {
            Assert.That(Undo.AddComponent<AvatarDoc>(avatar.Root).parameterPrefix, Is.EqualTo("AD"), "the avatar's own menu keeps AD");

            var ears = Piece("Cat Ears (v2)!");
            Assert.That(Undo.AddComponent<AvatarDoc>(ears).parameterPrefix, Is.EqualTo("CatEarsv2"), "ASCII letters, digits and _ are kept");

            var loose = new GameObject("Kimono_Set 3");
            made.Add(loose);
            Assert.That(ObjectFactory.AddComponent<AvatarDoc>(loose).parameterPrefix, Is.EqualTo("Kimono_Set3"), "a prefab's own object outside any avatar");

            var shop = Piece("Kitsune Ears and Tail Set by Shop v1.2");
            Assert.That(Undo.AddComponent<AvatarDoc>(shop).parameterPrefix, Is.EqualTo("KitsuneEarsandTa"), "a long name is cut to 16 characters, so the names stay short");

            var japanese = Piece("猫耳");
            Assert.That(Undo.AddComponent<AvatarDoc>(japanese).parameterPrefix, Does.Match("^AD_[0-9a-z]{4}$"), "nothing is left of the name, so AD and four random characters");
            Assert.That(AvatarDoc.PrefixFor(avatar.Root), Is.EqualTo("AD"));
        }

        [Test]
        public void AComponentThatExistsKeepsItsPrefixWhenItsObjectIsRenamedCopiedOrMadeAPrefab()
        {
            var folder = TempAssetFolder.Create("AvatarDocSeveralComponentsTests");
            var path = folder + "/Old pack.prefab";
            GameObject instance = null;
            try
            {
                var old = ChildMenu("Old pack", "AD");
                Toggle(old, "Hat");
                old.gameObject.name = "Renamed pack";
                Assert.That(old.parameterPrefix, Is.EqualTo("AD"), "a rename never moves the prefix");

                var copy = Object.Instantiate(old.gameObject, avatar.Root.transform);
                Assert.That(copy.GetComponent<AvatarDoc>().parameterPrefix, Is.EqualTo("AD"), "a copy keeps it");

                var prefab = PrefabUtility.SaveAsPrefabAsset(old.gameObject, path);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Assert.That(prefab.GetComponent<AvatarDoc>().parameterPrefix, Is.EqualTo("AD"), "so does a prefab made from it");
                Assert.That(instance.GetComponent<AvatarDoc>().parameterPrefix, Is.EqualTo("AD"), "and every instance placed from it");

                AvatarPlanCache.Invalidate();
                Assert.That(AvatarPlanCache.GetFor(old).Plan.Parameters.Select(p => p.Name), Has.Member("AD/Hat"));
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                AssetDatabase.DeleteAsset(path);
                TempAssetFolder.Delete(folder);
            }
        }

        [Test]
        public void SimpleModeCountsThePrefixANewComponentTookAsItsDefault()
        {
            var ears = Piece("Cat Ears");
            var menu = Undo.AddComponent<AvatarDoc>(ears);
            Toggle(menu, "Ears");

            Assert.That(menu.HasStartingPrefix, Is.True, "CatEars is the prefix a component on Cat Ears starts with");
            var prefs = new MemoryViewPrefs();
            ViewModes.Save(prefs, ViewMode.Simple);
            var view = new AvatarDoctorView(new SerializedObject(menu), menu, false, prefs);
            Assert.That(view.Header.Q("ad-settings-fold-header").ClassListContains("ad-hidden"), Is.True, "Simple keeps Component settings folded away");

            menu.parameterPrefix = "Ears";
            Assert.That(menu.HasStartingPrefix, Is.False);
            menu.parameterPrefix = "AD";
            Assert.That(menu.HasStartingPrefix, Is.True, "AD, the prefix of every component made before, counts too");
            var japanese = Undo.AddComponent<AvatarDoc>(Piece("猫耳"));
            Assert.That(japanese.HasStartingPrefix, Is.True);
        }

        // ---- automatic names that clash -----------------------------------------------------------------------------

        [Test]
        public void TwoComponentsThatWouldMakeTheSameAutomaticNamesGetOneWarningNamingBoth()
        {
            var own = RootMenu();
            Toggle(own, "Hat");
            Member(own, "Casual", "Outfits");
            var older = ChildMenu("Jacket pack", "AD");
            Toggle(older, "Hat");
            Member(older, "Kimono", "Outfits");
            Toggle(older, "Scarf");

            var clash = Messages(own, "ad.warn.prefix_clash").Single();

            Assert.That(clash.Key, Is.EqualTo("ad.warn.prefix_clash"), "two names take the plural text");
            Assert.That(clash.Severity, Is.EqualTo(MessageSeverity.Warning));
            Assert.That(clash.Args[0], Is.SameAs(older), "the component that is not on the avatar root is the one to change");
            Assert.That(clash.Args[1], Is.SameAs(own));
            Assert.That(clash.Args[2] as string, Does.Contain("AD/Hat").And.Contain("AD/Outfits").And.Not.Contain("Scarf"), "it names the clashing names only");
            Assert.That(clash.EntryIds, Is.Empty, "it speaks about the whole component");
            var plan = AvatarPlanCache.GetFor(own);
            Assert.That(plan.Plan.Parameters.Select(p => p.Name), Is.SupersetOf(new[] { "AD/Hat", "AD/Hat 2", "AD/Outfits", "AD/Outfits 2" }), "the build still tells them apart as before");

            older.parameterPrefix = "Jacketpack";
            AvatarPlanCache.Invalidate();
            Assert.That(Messages(own, "ad.warn.prefix_clash"), Is.Empty);
        }

        [Test]
        public void OneClashingNameTakesTheSingularTextAndTheLaterComponentChangesWhenNeitherIsOnTheRoot()
        {
            RootMenu();
            var first = ChildMenu("CatEars", "CatEars");
            Toggle(first, "Ears");
            var second = ChildMenu("CatEars", "CatEars");
            Toggle(second, "Ears");

            var clash = Messages(first, "ad.warn.prefix_clash").Single();

            Assert.That(clash.Key, Is.EqualTo("ad.warn.prefix_clash.one"));
            Assert.That(clash.Args[0], Is.SameAs(second), "two copies of one prefab: the later one changes");
            Assert.That(clash.Args[1], Is.SameAs(first));
            Assert.That(clash.Args[2], Is.EqualTo("CatEars/Ears"));

            var tree = AvatarPlanCache.GetFor(first).Tree;
            Assert.That(EntryEditing.GiveOwnPrefix(second, tree.Components), Is.True);
            Assert.That(second.parameterPrefix, Is.EqualTo("CatEars2"), "the object's name is taken, so it gets a number");
            Assert.That(first.parameterPrefix, Is.EqualTo("CatEars"));
        }

        [Test]
        public void GiveThisComponentItsOwnPrefixFixesTheClashInOneUndoStepEvenWhileLocked()
        {
            var own = RootMenu();
            Toggle(own, "Hat");
            var older = ChildMenu("Jacket pack", "AD");
            Toggle(older, "Hat");
            older.locked = true;

            var ownView = View(own);
            LayoutProbe.Measure(ownView, () =>
            {
                if (!ownView.Header.ProblemsOpen) ownView.Header.ToggleProblemsOpen();
                LayoutProbe.Validate(ownView);
                Assert.That(ownView.Header.Q("ad-problem-line-ad.warn.prefix_clash.one"), Is.Not.Null, "the avatar's own inspector names the clash too");
                Assert.That(ownView.Header.Q<Button>("ad-prefix-own"), Is.Null, "but the button belongs to the component that changes");
            });

            var view = View(older);
            LayoutProbe.Measure(view, () =>
            {
                if (!view.Header.ProblemsOpen) view.Header.ToggleProblemsOpen();
                LayoutProbe.Validate(view);
                var button = view.Header.Q<Button>("ad-prefix-own");
                Assert.That(button, Is.Not.Null);
                Assert.That(button.text, Is.EqualTo(L.Text("ad.ui.prefix.own")));
                Assert.That(button.tooltip, Is.EqualTo(L.Text("ad.ui.prefix.own.tip")));
                Assert.That(button.tooltip, Does.Contain("defaults"), "the tooltip says the toggles start from their defaults once");

                Undo.IncrementCurrentGroup();
                UiTestEvents.SimulateClick(button);

                Assert.That(older.parameterPrefix, Is.EqualTo("Jacketpack"), "the prefix stays editable on a locked component");
                Assert.That(view.Plan.Messages.Any(m => m.Says("ad.warn.prefix_clash")), Is.False, "the warning is gone");
                Assert.That(view.Header.Q<Button>("ad-prefix-own"), Is.Null);
            });
            Undo.PerformUndo();
            Assert.That(older.parameterPrefix, Is.EqualTo("AD"), "one undo step puts it back");
        }

        // ---- custom names that another component uses too -----------------------------------------------------------

        [Test]
        public void ACustomNameAnotherComponentUsesTooSaysWhoInTheParameterRow()
        {
            var own = RootMenu();
            var coat = Toggle(own, "Coat");
            coat.parameterName = "Coat";
            var scarf = Toggle(own, "Scarf");
            scarf.parameterName = "ScarfOn";
            var ears = ChildMenu("CatEars", "CatEars");
            Toggle(ears, "Jacket").parameterName = "Coat";
            var view = View(own);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(coat.id);
                OpenAdvanced(view);
                var note = view.Detail.Q<Label>("ad-parameter-shared");
                Assert.That(note, Is.Not.Null);
                Assert.That(note.ClassListContains("ad-hidden"), Is.False);
                Assert.That(note.text, Is.EqualTo(L.Labelled(L.Text("ad.ui.parameter.also_used_by"), L.Aside("Jacket", "CatEars"))));
                Assert.That(note.text, Is.EqualTo("Also used by: Jacket (CatEars)"));

                view.Tree.SelectEntry(scarf.id);
                OpenAdvanced(view);
                Assert.That(view.Detail.Q<Label>("ad-parameter-shared").ClassListContains("ad-hidden"), Is.True, "a name nobody else uses says nothing");
            });
            Assert.That(AvatarPlanCache.GetFor(own).Messages.Where(m => m.Severity != MessageSeverity.Info).Select(m => m.Key), Is.Empty, "sharing a custom name stays allowed");
        }

        // ---- the review's round -------------------------------------------------------------------------------------

        [TestCase("switched off")]
        [TestCase("removed")]
        [TestCase("on another avatar")]
        public void ALinkThatCannotBeFollowedLeavesTheToggleInItsOwnComponentsGroup(string how)
        {
            var own = RootMenu();
            Member(own, "Jacket", "Outfits");
            var prefab = ChildMenu("Mayo menu", "Mayo");
            var kimono = Member(prefab, "Kimono", "Outfits");
            TestAvatar other = null;
            try
            {
                if (how == "on another avatar")
                {
                    other = TestAvatar.Create();
                    var there = other.AddMenu();
                    there.AddEntry(EntryKind.Toggle, "Coat").exclusiveGroup = "Outfits";
                    kimono.groupComponent = there;
                }
                else
                {
                    var old = ChildMenu("Old menu", "Old");
                    Member(old, "Coat", "Outfits");
                    kimono.groupComponent = old;
                    if (how == "switched off") old.enabled = false;
                    else Object.DestroyImmediate(old);
                }
                AvatarPlanCache.Invalidate();

                var plan = AvatarPlanCache.GetFor(own);
                Assert.That(plan.Plan.Parameters.Single(p => p.Name == "Mayo/Outfits").Controls.Select(c => c.Entry), Is.EqualTo(new[] { kimono }), how + ": the Kimono is in its own component's group");
                var view = View(prefab);
                LayoutProbe.Measure(view, () =>
                {
                    view.Tree.SelectEntry(kimono.id);
                    LayoutProbe.Validate(view);
                    Assert.That(view.Detail.Q<Label>("ad-group-owner").ClassListContains("ad-hidden"), Is.True, how + ": no note names another component");
                });
                var fx = Build();
                Assert.That(fx.layers.Where(l => l.name.StartsWith("AD: Outfits")).Select(l => string.Join(",", l.stateMachine.states.Select(s => s.state.name))),
                    Has.Member("Off,Kimono"), how + ": the build agrees");
            }
            finally
            {
                other?.Dispose();
            }
        }

        [Test]
        public void AGroupMemberWithALeftoverCustomNameSaysNothingAboutWhoElseUsesIt()
        {
            var own = RootMenu();
            var jacket = Member(own, "Jacket", "Outfits");
            jacket.parameterName = "Left over";
            var prefab = ChildMenu("Mayo menu", "Mayo");
            Member(prefab, "Kimono", "Outfits").groupComponent = own;
            var view = View(own);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(jacket.id);
                OpenAdvanced(view);
                var note = view.Detail.Q<Label>("ad-parameter-shared");
                Assert.That(note == null || note.ClassListContains("ad-hidden"), Is.True, "a member uses the group's parameter, which its own custom name does not name");
            });
        }

        [Test]
        public void ACustomNameAnotherComponentDeclaresSaysSoToo()
        {
            var own = RootMenu();
            var coat = Toggle(own, "Coat");
            coat.parameterName = "Coat";
            var prefab = ChildMenu("Mayo menu", "Mayo");
            prefab.parameters.Add(ParameterDeclaration.Create("Coat", PlannedType.Bool));
            var view = View(own);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(coat.id);
                OpenAdvanced(view);
                var note = view.Detail.Q<Label>("ad-parameter-shared");
                Assert.That(note.ClassListContains("ad-hidden"), Is.False);
                Assert.That(note.text, Is.EqualTo(L.Labelled(L.Text("ad.ui.parameter.also_used_by"), L.Aside(L.Text("ad.ui.parameters"), "Mayo menu"))),
                    "the other component's Parameters list names it");
            });
        }

        [Test]
        public void JoiningKeepsTheNumbersOfTheGroupsOwnToggles()
        {
            var own = RootMenu();
            var jacket = Member(own, "Jacket", "Outfits");
            var hoodie = Member(own, "Hoodie", "Outfits");
            var prefab = ChildMenu("Mayo menu", "Mayo");
            // In the avatar's existing Old submenu, so the Kimono comes before the avatar's own toggles in the menu.
            prefab.installPath = "Old";
            var kimono = Member(prefab, "Kimono", "Outfits");
            var plan = AvatarPlanCache.GetFor(own);
            Assert.That((Binding(plan, jacket).Value, Binding(plan, hoodie).Value), Is.EqualTo((1f, 2f)));

            kimono.groupComponent = own;
            AvatarPlanCache.Invalidate();
            plan = AvatarPlanCache.GetFor(own);

            Assert.That(plan.Tree.Controls.IndexOf(plan.Tree.Controls.Single(c => c.Entry == kimono)), Is.LessThan(plan.Tree.Controls.IndexOf(plan.Tree.Controls.Single(c => c.Entry == jacket))), "the Kimono comes first in the menu");
            Assert.That((Binding(plan, jacket).Value, Binding(plan, hoodie).Value, Binding(plan, kimono).Value), Is.EqualTo((1f, 2f, 3f)),
                "the group's own toggles keep their numbers, so a saved choice still picks the same outfit");
        }

        [Test]
        public void TheGroupListAlsoOffersAGroupThatOnlyHasSettings()
        {
            var own = RootMenu();
            own.groups.Add(new ExclusiveGroup { name = "Hats", saved = false });
            var bag = Member(own, "Bag", "Bags");
            var view = View(own);
            LayoutProbe.Measure(view, () =>
            {
                try
                {
                    view.Tree.SelectEntry(bag.id);
                    LayoutProbe.Validate(view);
                    var list = OpenGroupList(view);
                    Assert.That(list.Items.Select(i => i.Text), Is.EqualTo(new[] { L.Text("ad.ui.group.none"), "Bags", "Hats" }));
                }
                finally
                {
                    CloseMenus(view);
                }
            });
        }

        [Test]
        public void TheAvatarsOwnInspectorSelectsTheComponentThePrefixWarningAsksToChange()
        {
            var own = RootMenu();
            Toggle(own, "Hat");
            var older = ChildMenu("Jacket pack", "AD");
            Toggle(older, "Hat");
            var previous = HeaderView.SelectComponent;
            Object selected = null;
            HeaderView.SelectComponent = component => selected = component;
            try
            {
                var view = View(own);
                LayoutProbe.Measure(view, () =>
                {
                    if (!view.Header.ProblemsOpen) view.Header.ToggleProblemsOpen();
                    LayoutProbe.Validate(view);
                    var button = view.Header.Q<Button>("ad-prefix-select");
                    Assert.That(button, Is.Not.Null, "the avatar's own inspector offers the way to the other component");
                    Assert.That(button.text, Is.EqualTo(L.Text("ad.ui.select_component")));
                    Assert.That(button.tooltip, Is.EqualTo(L.Text("ad.ui.prefix.select.tip")));
                    UiTestEvents.SimulateClick(button);
                });
                Assert.That(selected, Is.SameAs(older));

                var olderView = View(older);
                LayoutProbe.Measure(olderView, () =>
                {
                    if (!olderView.Header.ProblemsOpen) olderView.Header.ToggleProblemsOpen();
                    LayoutProbe.Validate(olderView);
                    Assert.That(olderView.Header.Q<Button>("ad-prefix-select"), Is.Null, "the component that changes has its own button instead");
                });
            }
            finally
            {
                HeaderView.SelectComponent = previous;
            }
        }

        [Test]
        public void ASecondComponentThatSharesNothingBuildsAsBefore()
        {
            GeneratedAssets.Note();
            GameObject built = null;
            using (var docs = DocsAvatar.Create())
            try
            {
                var menu = docs.AddMenu();
                menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = docs.Hat });
                var jacket = menu.AddEntry(EntryKind.Toggle, "Jacket");
                jacket.exclusiveGroup = "Outfits";
                jacket.objects.Add(new ObjectTarget { target = docs.Jacket });
                var set = docs.AddMenu(DocsShots_Guides2.Child(docs, "Kimono set"));
                set.parameterPrefix = "KimonoSet";
                set.installMode = InstallMode.AsSubmenu;
                var kimono = set.AddEntry(EntryKind.Toggle, "Kimono");
                kimono.exclusiveGroup = "Robes";
                kimono.defaultOn = true;
                kimono.objects.Add(new ObjectTarget { target = docs.Hoodie });
                set.AddEntry(EntryKind.Toggle, "No robe").exclusiveGroup = "Robes";
                set.AddEntry(EntryKind.Toggle, "Shoes").objects.Add(new ObjectTarget { target = docs.Shoes });

                Assert.That(AvatarPlanCache.GetFor(menu).Messages.Where(m => m.Severity != MessageSeverity.Info).Select(m => m.Key), Is.Empty, "nothing clashes");
                var provided = Provided(docs.Root);
                built = AvatarProcessor.ManualProcessAvatar(docs.Root);
                var dump = Lines(InvariantDump(built) + provided + SceneDefaults(built, docs));
                if (dump != Lines(TwoComponentBuild))
                    Assert.Fail("the build changed:\n" + dump.Replace("\n", " || "));
            }
            finally
            {
                if (built != null && built != docs.Root) Object.DestroyImmediate(built);
                GeneratedAssets.CleanUp();
            }
        }

        // ---- layout ---------------------------------------------------------------------------------------------------

        [Test]
        public void TheNewRowsAndWarningsFitAt300And400InEveryLanguage()
        {
            var own = RootMenu();
            Toggle(own, "Hat");
            Member(own, "Jacket", "Outfits");
            Toggle(own, "Coat").parameterName = "Coat";
            var older = ChildMenu("Jacket pack with a long name", "AD");
            Toggle(older, "Hat");
            var kimono = Member(older, "Kimono", "Outfits");
            var obi = Member(older, "Obi", "Sashes");
            obi.groupComponent = ChildMenu("Mayo menu of a shop with a long name", "Mayo");
            Member(obi.groupComponent, "Belt", "Sashes");
            var shared = Toggle(older, "Raincoat");
            shared.parameterName = "Coat";
            var allow = PanelAuditTests.StandardAllowList()
                .Allow(e => e.name == "ad-problems-arrow" || e.name == "ad-status-glyph", "the status row's own leading parts, before its spacer")
                .Allow(e => e.name == "ad-ready-show" || e.name == "ad-summary-toggle", "the status row's own trailing actions, after its spacer")
                .Allow(e => e.name == "ad-settings-fold-dot" || e.name == "ad-advanced-fold-dot", "a fold's own dot, inside the foldout's own toggle row");
            foreach (var language in new[] { "en-US", "ja-JP", "zh-Hans" })
            foreach (float width in new[] { 300f, 400f })
            using (L.UseTestLanguage(language, null))
            {
                var view = View(older);
                LayoutProbe.Measure(view, () =>
                {
                    if (!view.Header.ProblemsOpen) view.Header.ToggleProblemsOpen();
                    LayoutProbe.Validate(view);
                    Assert.That(view.Header.Q<Button>("ad-prefix-own"), Is.Not.Null);
                    Audit(view.Header, allow, language, width);
                    view.Tree.SelectEntry(kimono.id);
                    LayoutProbe.Validate(view);
                    Assert.That(view.Detail.Q<Button>("ad-group-join"), Is.Not.Null);
                    Audit(view.Detail, allow, language, width);
                    try
                    {
                        var list = OpenGroupList(view);
                        LayoutProbe.Validate(view);
                        var card = list.Card.worldBound;
                        var window = view.panel.visualTree.worldBound;
                        Assert.That(list.Items.Select(i => i.Text), Has.Member(L.Aside("Sashes", "Mayo menu of a shop with a long name")));
                        Assert.That(card.xMin >= window.xMin - 0.5f && card.xMax <= window.xMax + 0.5f, Is.True, language + " at " + width + ": the group list stays inside the window, " + card + " in " + window);
                    }
                    finally
                    {
                        CloseMenus(view);
                    }
                    view.Tree.SelectEntry(obi.id);
                    OpenAdvanced(view);
                    Assert.That(view.Detail.Q<Label>("ad-group-owner").ClassListContains("ad-hidden"), Is.False);
                    Audit(view.Detail, allow, language, width);
                    view.Tree.SelectEntry(shared.id);
                    OpenAdvanced(view);
                    Assert.That(view.Detail.Q<Label>("ad-parameter-shared").ClassListContains("ad-hidden"), Is.False);
                    Audit(view.Detail, allow, language, width);
                }, width);
                var ownView = View(own);
                LayoutProbe.Measure(ownView, () =>
                {
                    if (!ownView.Header.ProblemsOpen) ownView.Header.ToggleProblemsOpen();
                    LayoutProbe.Validate(ownView);
                    Assert.That(ownView.Header.Q<Button>("ad-prefix-select"), Is.Not.Null);
                    Audit(ownView.Header, allow, language, width);
                }, width);
            }
        }

        static void Audit(VisualElement part, PanelAudit.AllowList allow, string language, float width)
        {
            try
            {
                PanelAudit.Audit(part, part.worldBound.xMax, allow);
            }
            catch (AssertionException e)
            {
                Assert.Fail(language + " at " + width + ": " + e.Message);
            }
        }

        // ---- one component builds as it did ---------------------------------------------------------------------

        [Test]
        public void ASingleComponentAvatarBuildsTheSameFxParametersAndMenuAsBefore()
        {
            GeneratedAssets.Note();
            GameObject built = null;
            using (var docs = DocsAvatar.Create())
            try
            {
                var menu = docs.AddMenu();
                menu.parameterPrefix = "AD";
                menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = docs.Hat });
                var glasses = menu.AddEntry(EntryKind.Toggle, "Glasses");
                glasses.parameterName = "GlassesOn";
                glasses.objects.Add(new ObjectTarget { target = docs.Glasses });
                var blush = menu.AddEntry(EntryKind.Radial, "Blush");
                blush.blendShapes.Add(new BlendShapeTarget { renderer = docs.Face, shape = "Blush", minWeight = 0f, maxWeight = 100f });
                var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
                var jacket = menu.AddEntry(EntryKind.Toggle, "Jacket", outfits.id);
                jacket.exclusiveGroup = "Outfit";
                jacket.defaultOn = true;
                jacket.objects.Add(new ObjectTarget { target = docs.Jacket });
                var hoodie = menu.AddEntry(EntryKind.Toggle, "Hoodie", outfits.id);
                hoodie.exclusiveGroup = "Outfit";
                hoodie.objects.Add(new ObjectTarget { target = docs.Hoodie });
                menu.AddEntry(EntryKind.Toggle, "No outfit", outfits.id).exclusiveGroup = "Outfit";
                var colours = menu.AddEntry(EntryKind.Toggle, "Red shoes");
                colours.exclusiveGroup = "Shoes";
                colours.objects.Add(new ObjectTarget { target = docs.Shoes });
                menu.groups.Add(new ExclusiveGroup { name = "Shoes", saved = false, synced = true });
                var sparkle = menu.AddEntry(EntryKind.Trigger, "Sparkle");
                sparkle.source = TriggerSource.Toggle;
                sparkle.sourceToggleId = hoodie.id;
                sparkle.blendShapes.Add(new BlendShapeTarget { renderer = docs.Face, shape = "Smile", weight = 100f });
                var casual = menu.AddEntry(EntryKind.Preset, "Casual");
                casual.presetStates.Add(new PresetState { entryId = hoodie.id, on = true });

                var provided = Provided(docs.Root);
                built = AvatarProcessor.ManualProcessAvatar(docs.Root);
                var dump = Lines(InvariantDump(built) + provided + SceneDefaults(built, docs));
                if (dump != Lines(SingleComponentBuild))
                    Assert.Fail("the build changed:\n" + dump.Replace("\n", " || "));
            }
            finally
            {
                if (built != null && built != docs.Root) Object.DestroyImmediate(built);
                GeneratedAssets.CleanUp();
            }
        }

        /// <summary>The built FX controller, expression parameters and menu as CopyMenuTests.Dump writes them, with numbers written the same on every machine.</summary>
        static string InvariantDump(GameObject built)
        {
            var culture = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                return CopyMenuTests.Dump(built);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = culture;
            }
        }

        /// <summary>What NDMF says the avatar's AvatarDoc components provide before the build, by name.</summary>
        static string Provided(GameObject root)
        {
            var text = new System.Text.StringBuilder();
            foreach (var p in ParameterInfo.ForUI.GetParametersForObject(root).Where(p => p.Source is AvatarDoc).OrderBy(p => p.EffectiveName, System.StringComparer.Ordinal))
                text.Append("provided ").Append(p.EffectiveName).Append(' ').Append(p.ParameterType).Append(' ').Append(p.WantSynced).Append(' ')
                    .Append(p.DefaultValue.HasValue ? p.DefaultValue.Value.ToString(CultureInfo.InvariantCulture) : "none").Append(' ').Append(((AvatarDoc)p.Source).name).Append('\n');
            return text.ToString();
        }

        /// <summary>The scene the build leaves on the built avatar: whether each piece shows, and every face shape that is not at 0.</summary>
        static string SceneDefaults(GameObject built, DocsAvatar docs)
        {
            var text = new System.Text.StringBuilder();
            foreach (var piece in new[] { docs.Hat, docs.Glasses, docs.Jacket, docs.Hoodie, docs.Shoes })
            {
                var there = built.transform.Find(MenuCopy.PathOf(docs.Root.transform, piece.transform));
                text.Append("scene ").Append(piece.name).Append(' ').Append(there != null && there.gameObject.activeSelf).Append('\n');
            }
            var face = built.transform.Find(MenuCopy.PathOf(docs.Root.transform, docs.Face.transform)).GetComponent<SkinnedMeshRenderer>();
            for (int i = 0; i < face.sharedMesh.blendShapeCount; i++)
                if (face.GetBlendShapeWeight(i) != 0f)
                    text.Append("scene Face ").Append(face.sharedMesh.GetBlendShapeName(i)).Append(' ').Append(face.GetBlendShapeWeight(i).ToString(CultureInfo.InvariantCulture)).Append('\n');
            return text.ToString();
        }

        /// <summary>A dump as lines without the spaces at their ends, and without empty lines at either end.</summary>
        static string Lines(string text) => string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd())).Trim('\n');

        /// <summary>
        /// What the avatar of ASingleComponentAvatarBuildsTheSameFxParametersAndMenuAsBefore builds. The FX, expression
        /// parameter and menu lines were taken from 0.6.0, before components could own their groups; the Shoes
        /// group's settings (Saved off) and the provided and scene lines at the end were added later.
        /// </summary>
        const string SingleComponentBuild = @"
param __ModularAvatarInternal/One Float False 1 0
param AD/Blush Float False 0 0
param AD/Casual Bool False 0 0
param AD/Hat Bool False 0 0
param AD/Outfit Int False 0 1
param AD/Shoes Int False 0 0
param GlassesOn Bool False 0 0
layer Base 1 Override
  state Idle wd False behaviours
layer AD: Hat 1 Override
  state Off wd False behaviours
    curve Hat GameObject m_IsActive 0:0,0.01666667:0
    to On AD/HatIf0
  state On wd False behaviours
    curve Hat GameObject m_IsActive 0:1,0.01666667:1
    to Off AD/HatIfNot0
layer AD: Glasses 1 Override
  state Off wd False behaviours
    curve Glasses GameObject m_IsActive 0:0,0.01666667:0
    to On GlassesOnIf0
  state On wd False behaviours
    curve Glasses GameObject m_IsActive 0:1,0.01666667:1
    to Off GlassesOnIfNot0
layer AD: Blush 1 Override
  state Blend wd False behaviours
    tree Simple1D AD/Blush
      child 0 Blend
        curve Face SkinnedMeshRenderer blendShape.Blush 0:0,0.01666667:0
      child 1 Blend
        curve Face SkinnedMeshRenderer blendShape.Blush 0:100,0.01666667:100
layer AD: Outfit 1 Override
  state Off wd False behaviours
    curve Hoodie GameObject m_IsActive 0:0,0.01666667:0
    curve Jacket GameObject m_IsActive 0:0,0.01666667:0
  state Jacket wd False behaviours
    curve Hoodie GameObject m_IsActive 0:0,0.01666667:0
    curve Jacket GameObject m_IsActive 0:1,0.01666667:1
  state Hoodie wd False behaviours
    curve Hoodie GameObject m_IsActive 0:1,0.01666667:1
    curve Jacket GameObject m_IsActive 0:0,0.01666667:0
  state No outfit wd False behaviours
    curve Hoodie GameObject m_IsActive 0:0,0.01666667:0
    curve Jacket GameObject m_IsActive 0:0,0.01666667:0
  any to Off AD/OutfitNotEqual1,AD/OutfitNotEqual2,AD/OutfitNotEqual3
  any to Jacket AD/OutfitEquals1
  any to Hoodie AD/OutfitEquals2
  any to No outfit AD/OutfitEquals3
layer AD: Shoes 1 Override
  state Off wd False behaviours
    curve Shoes GameObject m_IsActive 0:0,0.01666667:0
  state Red shoes wd False behaviours
    curve Shoes GameObject m_IsActive 0:1,0.01666667:1
  any to Off AD/ShoesNotEqual1
  any to Red shoes AD/ShoesEquals1
layer AD: Sparkle 1 Override
  state Off wd False behaviours
    curve Face SkinnedMeshRenderer blendShape.Smile 0:0,0.01666667:0
    to On AD/OutfitEquals2
  state On wd False behaviours
    curve Face SkinnedMeshRenderer blendShape.Smile 0:100,0.01666667:100
    to Off AD/OutfitNotEqual2
layer AD: Casual (preset) 1 Override
  state Idle wd False behaviours
    to Apply AD/CasualIf0
  state Apply wd False behaviours VRCAvatarParameterDriver
    to Idle AD/CasualIfNot0
expression Sit Bool 0 True True
expression Wave Bool 0 False True
expression AD/Hat Bool 0 True True
expression GlassesOn Bool 0 True True
expression AD/Blush Float 0 True True
expression AD/Outfit Int 1 True True
expression AD/Shoes Int 0 False True
expression AD/Casual Bool 0 False False
control Emotes SubMenu  1
  control Wave Toggle Wave 1
control Sit Toggle Sit 1
control Hat Toggle AD/Hat 1
control Glasses Toggle GlassesOn 1
control Blush RadialPuppet  1
control Outfits SubMenu  1
  control Jacket Toggle AD/Outfit 1
  control Hoodie Toggle AD/Outfit 2
  control No outfit Toggle AD/Outfit 3
control Red shoes Toggle AD/Shoes 1
control Casual Button AD/Casual 1
provided AD/Blush Float True 0 Avatar
provided AD/Casual Bool False 0 Avatar
provided AD/Hat Bool True 0 Avatar
provided AD/Outfit Int True 1 Avatar
provided AD/Shoes Int True 0 Avatar
provided GlassesOn Bool True 0 Avatar
scene Hat False
scene Glasses False
scene Jacket True
scene Hoodie False
scene Shoes False
";

        /// <summary>What the avatar of ASecondComponentThatSharesNothingBuildsAsBefore builds: an avatar's own menu beside a prefab's, which share no group and no prefix.</summary>
        const string TwoComponentBuild = @"
param __ModularAvatarInternal/One Float False 1 0
param AD/Hat Bool False 0 0
param AD/Outfits Int False 0 0
param KimonoSet/Robes Int False 0 1
param KimonoSet/Shoes Bool False 0 0
layer Base 1 Override
  state Idle wd False behaviours
layer AD: Hat 1 Override
  state Off wd False behaviours
    curve Hat GameObject m_IsActive 0:0,0.01666667:0
    to On AD/HatIf0
  state On wd False behaviours
    curve Hat GameObject m_IsActive 0:1,0.01666667:1
    to Off AD/HatIfNot0
layer AD: Outfits 1 Override
  state Off wd False behaviours
    curve Jacket GameObject m_IsActive 0:0,0.01666667:0
  state Jacket wd False behaviours
    curve Jacket GameObject m_IsActive 0:1,0.01666667:1
  any to Off AD/OutfitsNotEqual1
  any to Jacket AD/OutfitsEquals1
layer AD: Robes 1 Override
  state Off wd False behaviours
    curve Hoodie GameObject m_IsActive 0:0,0.01666667:0
  state Kimono wd False behaviours
    curve Hoodie GameObject m_IsActive 0:1,0.01666667:1
  state No robe wd False behaviours
    curve Hoodie GameObject m_IsActive 0:0,0.01666667:0
  any to Off KimonoSet/RobesNotEqual1,KimonoSet/RobesNotEqual2
  any to Kimono KimonoSet/RobesEquals1
  any to No robe KimonoSet/RobesEquals2
layer AD: Shoes 1 Override
  state Off wd False behaviours
    curve Shoes GameObject m_IsActive 0:0,0.01666667:0
    to On KimonoSet/ShoesIf0
  state On wd False behaviours
    curve Shoes GameObject m_IsActive 0:1,0.01666667:1
    to Off KimonoSet/ShoesIfNot0
expression Sit Bool 0 True True
expression Wave Bool 0 False True
expression AD/Hat Bool 0 True True
expression AD/Outfits Int 0 True True
expression KimonoSet/Robes Int 1 True True
expression KimonoSet/Shoes Bool 0 True True
control Emotes SubMenu  1
  control Wave Toggle Wave 1
control Sit Toggle Sit 1
control Hat Toggle AD/Hat 1
control Jacket Toggle AD/Outfits 1
control Kimono set SubMenu  1
  control Kimono Toggle KimonoSet/Robes 1
  control No robe Toggle KimonoSet/Robes 2
  control Shoes Toggle KimonoSet/Shoes 1
provided AD/Hat Bool True 0 Avatar
provided AD/Outfits Int True 0 Avatar
provided KimonoSet/Robes Int True 1 Kimono set
provided KimonoSet/Shoes Bool True 0 Kimono set
scene Hat False
scene Glasses True
scene Jacket False
scene Hoodie True
scene Shoes False
";
    }
}
#endif
