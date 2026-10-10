using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace DocMe.AvatarDoctor.Editor
{
    public class PlanCacheTests
    {
        TestAvatar avatar;

        [SetUp]
        public void SetUp()
        {
            GeneratedAssets.Note();
            avatar = TestAvatar.Create();
            AvatarPlanCache.Invalidate();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            AvatarPlanCache.Invalidate();
            GeneratedAssets.CleanUp();
        }

        [Test]
        public void GetBuildsTreeAndPlanForTheAvatar()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(plan.Tree.Controls.Single().Entry, Is.SameAs(hat));
            Assert.That(plan.Plan.Parameters.Single().Name, Is.EqualTo("AD/Hat"));
            Assert.That(plan.AvatarDoctorBits, Is.EqualTo(1));
            Assert.That(plan.AvatarBits, Is.EqualTo(1), "the Existing parameter");
            Assert.That(plan.Messages, Is.Empty);
        }

        [Test]
        public void GetReturnsTheSameInstanceUntilInvalidated()
        {
            var menu = avatar.AddMenu();
            var first = AvatarPlanCache.GetFor(menu);
            Assert.That(AvatarPlanCache.GetFor(menu), Is.SameAs(first));

            AvatarPlanCache.Invalidate();

            var second = AvatarPlanCache.GetFor(menu);
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.Version, Is.GreaterThan(first.Version));
        }

        [Test]
        public void ProvidedParametersAreWalkedOncePerVersion()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            AvatarPlanCache.Invalidate();
            int walks = AvatarPlanCache.ProvidedWalks;

            AvatarPlanCache.GetFor(menu);
            Assert.That(AvatarPlanCache.ProvidedWalks, Is.EqualTo(walks + 1));

            AvatarPlanCache.GetFor(menu);
            AvatarPlanCache.ProvidedFor(avatar.Root.transform);
            ParameterPlanner.CountBits(avatar.Root.transform);
            Assert.That(AvatarPlanCache.ProvidedWalks, Is.EqualTo(walks + 1), "the parameter picker and the bit count read the walk the plan already did");

            AvatarPlanCache.Invalidate();
            AvatarPlanCache.GetFor(menu);
            Assert.That(AvatarPlanCache.ProvidedWalks, Is.EqualTo(walks + 2), "a dropped plan walks them again");
        }

        [Test]
        public void UnrelatedPropertyChangesDoNotRaiseChanged()
        {
            var menu = avatar.AddMenu();
            AvatarPlanCache.GetFor(menu);
            int version = AvatarPlanCache.Version;
            int raised = 0;
            System.Action<Transform> handler = _ => raised++;
            AvatarPlanCache.Changed += handler;
            try
            {
                AvatarPlanCache.NoteChangedObjects(new Object[] { avatar.Body });

                Assert.That(raised, Is.EqualTo(0), "a renderer changing is for the poller, not for an immediate rebuild of every open view");
                Assert.That(AvatarPlanCache.Version, Is.GreaterThan(version), "the plan is still marked stale");
                Assert.That(AvatarPlanCache.GetFor(menu).Version, Is.EqualTo(AvatarPlanCache.Version), "and the next read rebuilds it");

                AvatarPlanCache.NoteChangedObjects(new Object[] { menu });

                Assert.That(raised, Is.EqualTo(1), "a change to an AvatarDoc reaches the open views at once");
            }
            finally
            {
                AvatarPlanCache.Changed -= handler;
            }
        }

        [Test]
        public void DisabledComponentsStillAppearInTheInspectorPlan()
        {
            var menu = avatar.AddMenu();
            menu.enabled = false;
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(plan.Tree.Controls.Select(c => c.Entry), Does.Contain(hat));
            var (_, byId) = TreeRow.Build(plan, menu);
            var row = byId.Values.Single(r => r.Entry == hat);
            Assert.That(row.OwnerDisabled, Is.True);
        }

        /// <summary>A switched off older component on the avatar root and a newer one below it, each with a toggle named Hat.</summary>
        (AvatarDoc older, MenuEntry olderHat, AvatarDoc newer, MenuEntry newerHat) OlderDisabledAndNewer()
        {
            var older = avatar.AddMenu();
            var olderHat = older.AddEntry(EntryKind.Toggle, "Hat");
            olderHat.objects.Add(new ObjectTarget { target = avatar.Hat });
            older.enabled = false;
            var newerObject = new GameObject("Newer");
            newerObject.transform.SetParent(avatar.Root.transform);
            var newer = avatar.AddMenu(newerObject);
            var newerHat = newer.AddEntry(EntryKind.Toggle, "Hat");
            newerHat.objects.Add(new ObjectTarget { target = avatar.Glasses });
            AvatarPlanCache.Invalidate();
            return (older, olderHat, newer, newerHat);
        }

        [Test]
        public void ADisabledComponentDoesNotTakeAParameterNameFromTheEnabledOne()
        {
            var (_, _, newer, newerHat) = OlderDisabledAndNewer();

            var plan = AvatarPlanCache.GetFor(newer);

            var control = plan.Tree.Controls.Single(c => c.Entry == newerHat);
            Assert.That(plan.Plan.Bindings[control].Parameter.Name, Is.EqualTo("AD/Hat"), "the build skips the disabled component, so this is the first Hat");

            var builtTree = MenuModel.Build(avatar.Root.transform, avatar.Descriptor.expressionsMenu, MenuModel.CollectComponents(avatar.Root.transform));
            var built = ParameterPlanner.Plan(builtTree, ParametersWriter.ExistingParameters(avatar.Descriptor.expressionParameters, avatar.Fx));
            Assert.That(built.Bindings.Values.Single().Parameter.Name, Is.EqualTo("AD/Hat"));
            Assert.That(plan.Plan.Parameters.Select(p => p.Name), Is.EqualTo(built.Parameters.Select(p => p.Name)), "the inspector shows the parameters the build makes");
        }

        [Test]
        public void ADisabledComponentsBitsAreNotCounted()
        {
            var (older, _, newer, _) = OlderDisabledAndNewer();
            older.AddEntry(EntryKind.Toggle, "Cap").objects.Add(new ObjectTarget { target = avatar.Hat });
            older.AddEntry(EntryKind.Toggle, "Scarf").objects.Add(new ObjectTarget { target = avatar.Hat });
            AvatarPlanCache.Invalidate();

            var plan = AvatarPlanCache.GetFor(newer);

            Assert.That(plan.AvatarDoctorBits, Is.EqualTo(1), "only the enabled component's one Bool");
            Assert.That(plan.Plan.Parameters.Select(p => p.Name), Is.EqualTo(new[] { "AD/Hat" }));
        }

        [Test]
        public void ADisabledComponentsControlsTakeNoSlotInTheOverflowSplit()
        {
            var (older, _, newer, _) = OlderDisabledAndNewer();
            for (int i = 0; i < 9; i++)
                older.AddEntry(EntryKind.Toggle, "Spare " + i).objects.Add(new ObjectTarget { target = avatar.Hat });
            AvatarPlanCache.Invalidate();

            var plan = AvatarPlanCache.GetFor(newer);

            Assert.That(plan.Tree.AllNodes().Any(n => n.IsMorePage), Is.False, "ten controls of a component the build skips would have pushed this one onto a More page");
            Assert.That(plan.Messages.Any(m => m.Key == "ad.info.menu_split"), Is.False);
        }

        [Test]
        public void ADisabledComponentsGroupSettingsAreNotUsed()
        {
            var (older, _, newer, newerHat) = OlderDisabledAndNewer();
            older.groups.Add(new ExclusiveGroup { name = "Outfits", saved = false, synced = false });
            newerHat.exclusiveGroup = "Outfits";
            AvatarPlanCache.Invalidate();

            var plan = AvatarPlanCache.GetFor(newer);

            Assert.That(plan.Plan.Parameters.Single(p => p.GroupName == "Outfits").Synced, Is.True, "a group takes its settings from a component the build reads");
        }

        [Test]
        public void ADisabledComponentsDeclarationsAreNotPlanned()
        {
            var (older, _, newer, _) = OlderDisabledAndNewer();
            older.parameters.Add(ParameterDeclaration.Create("OldFlag", PlannedType.Bool));
            AvatarPlanCache.Invalidate();

            var plan = AvatarPlanCache.GetFor(newer);

            Assert.That(plan.Plan.Parameters.Any(p => p.Name == "OldFlag"), Is.False, "a declared parameter is only planned from a component the build reads");
            Assert.That(plan.AvatarDoctorBits, Is.EqualTo(1), "and costs no bits");
        }

        [Test]
        public void ATriggerThatReadsAToggleOfADisabledComponentSaysItsSourceIsMissing()
        {
            var (_, olderHat, newer, _) = OlderDisabledAndNewer();
            var trigger = newer.AddEntry(EntryKind.Trigger, "Ears");
            trigger.objects.Add(new ObjectTarget { target = avatar.Glasses });
            trigger.source = TriggerSource.Toggle;
            trigger.sourceToggleId = olderHat.id;
            AvatarPlanCache.Invalidate();

            var plan = AvatarPlanCache.GetFor(newer);

            Assert.That(plan.Messages.Any(m => m.Key == "ad.warn.trigger_source_missing" && m.EntryIds.Contains(trigger.id)), Is.True,
                "the build skips that toggle, so the trigger has nothing to read there, and the inspector says what the build will");
            Assert.That(plan.Triggers.Any(t => t.Entry == trigger), Is.False);
        }

        /// <summary>The messages the build's own model raises for the avatar: the components it reads are the enabled ones.</summary>
        List<string> KeysTheBuildRaises()
        {
            var tree = MenuModel.Build(avatar.Root.transform, avatar.Descriptor.expressionsMenu, MenuModel.CollectComponents(avatar.Root.transform));
            return tree.Messages.Select(m => m.Key).ToList();
        }

        [Test]
        public void APresetThatNamesAToggleOfADisabledComponentWarnsLikeTheBuild()
        {
            var (_, olderHat, newer, _) = OlderDisabledAndNewer();
            var preset = newer.AddEntry(EntryKind.Preset, "All on");
            preset.presetStates.Add(new PresetState { entryId = olderHat.id, on = true });
            AvatarPlanCache.Invalidate();

            var plan = AvatarPlanCache.GetFor(newer);

            Assert.That(plan.Messages.Any(m => m.Key == "ad.warn.preset_target_missing" && m.EntryIds.Contains(preset.id)), Is.True,
                "the build skips that toggle, so the state has nothing to set there");
            Assert.That(KeysTheBuildRaises(), Does.Contain("ad.warn.preset_target_missing"));
        }

        [Test]
        public void AFlipTargetOnADisabledComponentIsAnErrorLikeInTheBuild()
        {
            var (_, olderHat, newer, _) = OlderDisabledAndNewer();
            var trigger = newer.AddEntry(EntryKind.Trigger, "Boop");
            trigger.source = TriggerSource.Contact;
            trigger.contactAnchor = avatar.Glasses.transform;
            trigger.touchMode = TouchMode.FlipToggle;
            trigger.flipToggleId = olderHat.id;
            AvatarPlanCache.Invalidate();

            var plan = AvatarPlanCache.GetFor(newer);

            Assert.That(plan.Messages.Any(m => m.Key == "ad.error.flip_target" && m.EntryIds.Contains(trigger.id)), Is.True,
                "the build stops on this, so the inspector says so too");
            Assert.That(plan.Messages.Any(m => m.Key == "ad.info.flip_target_excluded"), Is.False, "it is switched off, not left out for the platform");
            Assert.That(KeysTheBuildRaises(), Does.Contain("ad.error.flip_target"));
        }

        [Test]
        public void AGateOnADisabledComponentIsAnErrorLikeInTheBuild()
        {
            var (_, olderHat, newer, _) = OlderDisabledAndNewer();
            var trigger = newer.AddEntry(EntryKind.Trigger, "Ears");
            trigger.objects.Add(new ObjectTarget { target = avatar.Glasses });
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "AFK";
            trigger.gateToggleId = olderHat.id;
            AvatarPlanCache.Invalidate();

            var plan = AvatarPlanCache.GetFor(newer);

            Assert.That(plan.Messages.Any(m => m.Key == "ad.error.gate_target" && m.EntryIds.Contains(trigger.id)), Is.True,
                "the build stops on this, so the inspector says so too instead of planning the trigger without its gate");
            Assert.That(KeysTheBuildRaises(), Does.Contain("ad.error.gate_target"));
        }

        [Test]
        public void ADisabledComponentsRowsStayInTheTreeAndAreMarkedAsLeftOut()
        {
            var (older, olderHat, _, _) = OlderDisabledAndNewer();

            var plan = AvatarPlanCache.GetFor(older);
            var (_, byId) = TreeRow.Build(plan, older);

            var row = byId.Values.Single(r => r.Entry == olderHat);
            Assert.That(row.Excluded, Is.True, "dimmed the way an entry left out for the platform is");
            Assert.That(row.OwnerDisabled, Is.True, "and still carrying its disabled chip");
            Assert.That(WhatThisBuildsModel.Describe(plan, row), Is.EqualTo(L.Text("ad.ui.builds.nothing.disabled")), "and it says the component is the reason, not the platform");
        }

        [Test]
        public void MessagesAreAttachedByEntryId()
        {
            var menu = avatar.AddMenu();
            var first = menu.AddEntry(EntryKind.Submenu, "First");
            var second = menu.AddEntry(EntryKind.Submenu, "Second");
            var whole = menu.AddEntry(EntryKind.Toggle, "Hat", first.id);
            whole.objects.Add(new ObjectTarget { target = avatar.Hat });
            var broken = menu.AddEntry(EntryKind.Toggle, "Hat", second.id);
            broken.objects.Add(new ObjectTarget { target = null });

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(plan.MessagesFor(menu, broken).Select(m => m.Key), Is.EqualTo(new[] { "ad.warn.target_missing" }));
            Assert.That(plan.MessagesFor(menu, whole), Is.Empty, "the entry of the same name is not implicated");
        }

        [Test]
        public void ComponentOutsideAnAvatarPlansItself()
        {
            var lonely = new GameObject("Lonely").AddComponent<AvatarDoc>();
            try
            {
                lonely.AddEntry(EntryKind.Toggle, "T").objects.Add(new ObjectTarget { target = lonely.gameObject });
                var plan = AvatarPlanCache.GetFor(lonely);
                Assert.That(plan.Tree.Controls.Count, Is.EqualTo(1));
                Assert.That(plan.Tree.Messages.Any(m => m.Key == "ad.warn.target_outside_avatar"), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(lonely.gameObject);
            }
        }

        [Test]
        public void PlanIncludesTriggersAndTheirMessages()
        {
            var menu = avatar.AddMenu();
            var t = menu.AddEntry(EntryKind.Trigger, "T");
            t.objects.Add(new ObjectTarget { target = avatar.Hat });
            t.source = TriggerSource.Toggle;
            t.sourceToggleId = "missing";

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(plan.Triggers, Is.Empty);
            Assert.That(plan.Messages.Any(m => m.Key == "ad.warn.trigger_source_missing"), Is.True);
            Assert.That(plan.MessagesFor(menu, t).Any(), Is.True);
        }

        [Test]
        public void PlanSeesFxOnlyParameters()
        {
            avatar.Fx.AddParameter("FaceTracking", AnimatorControllerParameterType.Float);
            var menu = avatar.AddMenu();
            var trigger = MenuEntry.Create(EntryKind.Trigger, "Mouth");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "FaceTracking";
            trigger.condition = ParameterCondition.Greater;
            trigger.conditionValue = 0.5f;
            // The brief's trigger carries no target; HasEffects requires at least one, so a target is added here
            // to keep the trigger from being skipped before it reaches the parameter type check under test.
            trigger.objects.Add(new ObjectTarget { target = avatar.Hat });
            menu.entries.Add(trigger);
            AvatarPlanCache.Invalidate();

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(plan.Triggers.Single().Condition.Type, Is.EqualTo(PlannedType.Float));
            Assert.That(plan.Messages.Any(m => m.Key == "ad.warn.parameter_type_unknown"), Is.False);
        }

        [Test]
        public void PlanShowsOverflowMoreRows()
        {
            var menu = avatar.AddMenu();
            for (int i = 0; i < 9; i++) menu.entries.Add(MenuEntry.Create(EntryKind.Toggle, "T" + i));
            AvatarPlanCache.Invalidate();

            var plan = AvatarPlanCache.GetFor(menu);
            var more = plan.Tree.Root.Items.Select(i => i.Submenu).FirstOrDefault(s => s != null && s.Name == "More" && s.EntryId == null);

            Assert.That(more, Is.Not.Null);
            // The fixture avatar's own expressions menu already holds two existing items (Old, RootToggle) that
            // count toward the root's control budget alongside the nine new toggles, so the split leaves four
            // entries (T5 to T8) on the overflow page instead of one; T8, the last one added, is still last on it.
            Assert.That(more.Items.Last().Control.Entry.name, Is.EqualTo("T8"));
            Assert.That(plan.Messages.Any(m => m.Key == "ad.info.menu_split"), Is.True);
        }

        [Test]
        public void UsageIndexListsClipsAnimatingAShape()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            avatar.AddFxClip("Wink", TestAvatar.ShapeClip("Wink", "Body", "Eye_Big", 0f, 100f));
            var menu = avatar.AddMenu();
            AvatarPlanCache.Invalidate();

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(plan.Usage.ClipsFor(avatar.Body, "Blush").Select(c => c.name), Is.EqualTo(new[] { "Smile" }));
            Assert.That(plan.Usage.ClipsFor(avatar.Body, "Eye_Big").Select(c => c.name), Is.EqualTo(new[] { "Wink" }));
            Assert.That(plan.Usage.ClipsFor(avatar.Body, "Missing"), Is.Empty);
        }

        [Test]
        public void UsageIgnoresControllersWhenLayersAreNotCustomised()
        {
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            avatar.Descriptor.customizeAnimationLayers = false;
            var menu = avatar.AddMenu();
            AvatarPlanCache.Invalidate();

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(plan.Usage.ClipsFor(avatar.Body, "Blush"), Is.Empty, "VRChat uses its own layers, so the avatar's controllers do not animate anything");
        }

        [Test]
        public void SavedClipChangesRefreshTheUsage()
        {
            const string folder = GeneratedAssets.Folder;
            const string path = folder + "/UsageRefresh.anim";
            var binding = EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Eye_Big");
            try
            {
                GeneratedAssets.Ensure();
                var clip = TestAvatar.ShapeClip("UsageRefresh", "Body", "Blush", 0f, 100f);
                AssetDatabase.CreateAsset(clip, path);
                avatar.AddFxClip("Saved", clip);
                var menu = avatar.AddMenu();
                AvatarPlanCache.Invalidate();
                Assert.That(AvatarPlanCache.GetFor(menu).Usage.ClipsFor(avatar.Body, "Eye_Big"), Is.Empty);

                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Linear(0f, 0f, 1f, 100f));
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

                Assert.That(AvatarPlanCache.GetFor(menu).Usage.ClipsFor(avatar.Body, "Eye_Big").Select(c => c.name), Is.EqualTo(new[] { "UsageRefresh" }));
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
            }
        }

        [Test]
        public void UsageIndexKeysByPathAndShape()
        {
            var face = new GameObject("Face");
            face.transform.SetParent(avatar.Root.transform);
            var renderer = face.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = TestAvatar.QuadWithShapes("Blush");
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            avatar.AddFxClip("Cheek", TestAvatar.ShapeClip("Cheek", "Face", "Blush", 0f, 100f));
            var menu = avatar.AddMenu();
            AvatarPlanCache.Invalidate();

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(plan.Usage.ClipsFor(avatar.Body, "Blush").Select(c => c.name), Is.EqualTo(new[] { "Smile" }), "the same shape name on another renderer is a different key");
            Assert.That(plan.Usage.ClipsFor(renderer, "Blush").Select(c => c.name), Is.EqualTo(new[] { "Cheek" }));
        }

        [Test]
        public void LockedComponentPlanMatchesUnlocked()
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            menu.AddEntry(EntryKind.Toggle, "Hat", outfits.id).objects.Add(new ObjectTarget { target = avatar.Hat });
            menu.AddEntry(EntryKind.Radial, "Fade").blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush" });

            var unlockedPlan = AvatarPlanCache.GetFor(menu);
            var unlockedParams = unlockedPlan.Plan.Parameters.Select(p => (p.Name, p.Type, p.Bits, p.Synced, p.Saved)).ToList();
            var unlockedPaths = unlockedPlan.Tree.Controls.Select(c => c.Path).OrderBy(p => p).ToList();
            int unlockedBits = unlockedPlan.AvatarDoctorBits;

            menu.locked = true;
            AvatarPlanCache.Invalidate();
            var lockedPlan = AvatarPlanCache.GetFor(menu);
            var lockedParams = lockedPlan.Plan.Parameters.Select(p => (p.Name, p.Type, p.Bits, p.Synced, p.Saved)).ToList();
            var lockedPaths = lockedPlan.Tree.Controls.Select(c => c.Path).OrderBy(p => p).ToList();

            Assert.That(lockedParams, Is.EqualTo(unlockedParams));
            Assert.That(lockedPaths, Is.EqualTo(unlockedPaths));
            Assert.That(lockedPlan.AvatarDoctorBits, Is.EqualTo(unlockedBits));
        }

        [Test]
        public void AvatarWideParameterUsageSurvivesAMenuOnlyEdit()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            var root = avatar.Root.transform;
            ParameterUsage.For(root, AvatarPlanCache.GetFor(menu).Tree);
            int builds = ParameterUsage.AvatarWideBuilds;

            // An AvatarDoc edit: NoteChangedObjects sees the changed object is an AvatarDoc, not a PhysBone, a
            // contact receiver or a controller, so it drops only the built tree and removal plan.
            AvatarPlanCache.NoteChangedObjects(new Object[] { menu });
            ParameterUsage.For(root, AvatarPlanCache.GetFor(menu).Tree);

            Assert.That(ParameterUsage.AvatarWideBuilds, Is.EqualTo(builds),
                "an AvatarDoc edit cannot add, remove or retarget a PhysBone, a contact receiver, a controller or a Modular Avatar component, so the avatar-wide walk still answers what it did before");
        }

        [Test]
        public void AvatarWideParameterUsageDropsWhenAPhysBoneChanges()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            var physBone = avatar.Glasses.AddComponent<VRCPhysBone>();
            physBone.parameter = "Tail";
            var root = avatar.Root.transform;
            ParameterUsage.For(root, AvatarPlanCache.GetFor(menu).Tree);
            int builds = ParameterUsage.AvatarWideBuilds;

            AvatarPlanCache.NoteChangedObjects(new Object[] { physBone });
            ParameterUsage.For(root, AvatarPlanCache.GetFor(menu).Tree);

            Assert.That(ParameterUsage.AvatarWideBuilds, Is.EqualTo(builds + 1),
                "a PhysBone is one of the things the avatar-wide walk itself reads, so a change to it has to redo the walk");
        }
    }
}
