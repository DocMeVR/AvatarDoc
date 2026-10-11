using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    public class MenuModelTests
    {
        GameObject root;
        TestAvatar avatar;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Avatar");
            avatar = TestAvatar.Create();
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            avatar.Dispose();
        }

        [Test]
        public void AnOffValueOnAPinnedShapeIsAnError()
        {
            var menu = avatar.AddMenu();
            var entry = menu.AddEntry(EntryKind.Toggle, "Blush");
            entry.blendShapes.Add(new BlendShapeTarget
            {
                renderer = avatar.Body, shape = "Blush", weight = 100f,
                minWeight = 20f, explicitOff = true, mode = ShapeEffectMode.Pin,
            });

            var tree = MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, new[] { menu });

            Assert.That(tree.Messages.Any(m => m.Key == "ad.error.explicit_off_pin" && m.EntryIds.Contains(entry.id)), Is.True);
        }

        [Test]
        public void AddEntryAssignsUniqueIds()
        {
            var menu = root.AddComponent<AvatarDoc>();
            var a = menu.AddEntry(EntryKind.Toggle, "Hat");
            var b = menu.AddEntry(EntryKind.Toggle, "Hat");
            Assert.That(a.id, Is.Not.Empty);
            Assert.That(a.id, Is.Not.EqualTo(b.id));
            Assert.That(menu.FindEntry(b.id), Is.SameAs(b));
            Assert.That(a.HasEffects, Is.False);
        }

        static VRCExpressionsMenu Menu(params VRCExpressionsMenu.Control[] controls)
        {
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.controls = controls.ToList();
            return menu;
        }

        static VRCExpressionsMenu.Control Submenu(string name, VRCExpressionsMenu menu)
        {
            return new VRCExpressionsMenu.Control { name = name, type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = menu, parameter = new VRCExpressionsMenu.Control.Parameter { name = "" } };
        }

        static VRCExpressionsMenu.Control Toggle(string name, string parameter)
        {
            return new VRCExpressionsMenu.Control { name = name, type = VRCExpressionsMenu.Control.ControlType.Toggle, parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter }, value = 1 };
        }

        static MenuEntry WithObject(MenuEntry entry, GameObject go, bool activeWhenOn = true)
        {
            entry.objects.Add(new ObjectTarget { target = go, activeWhenOn = activeWhenOn });
            return entry;
        }

        [Test]
        public void EntriesNestUnderSubmenus()
        {
            var menu = root.AddComponent<AvatarDoc>();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            WithObject(menu.AddEntry(EntryKind.Toggle, "Casual", outfits.id), root);
            WithObject(menu.AddEntry(EntryKind.Toggle, "Hat"), root);

            var tree = MenuModel.Build(root.transform, null, new[] { menu });

            Assert.That(tree.Root.Items.Count, Is.EqualTo(2));
            Assert.That(tree.Root.Items[0].Submenu.Name, Is.EqualTo("Outfits"));
            Assert.That(tree.Root.Items[0].Submenu.Items[0].Control.Entry.name, Is.EqualTo("Casual"));
            Assert.That(tree.Root.Items[1].Control.Entry.name, Is.EqualTo("Hat"));
            Assert.That(tree.Controls.Select(c => c.Path), Is.EqualTo(new[] { "Outfits/Casual", "Hat" }));
            Assert.That(tree.Controls[0].LayerName, Is.EqualTo("AD: Casual"));
            Assert.That(tree.HasErrors, Is.False);
        }

        [Test]
        public void InstallPathCreatesMissingSubmenus()
        {
            var menu = root.AddComponent<AvatarDoc>();
            menu.installPath = "Outfits/Summer";
            WithObject(menu.AddEntry(EntryKind.Toggle, "Bikini"), root);

            var tree = MenuModel.Build(root.transform, null, new[] { menu });

            var outfits = tree.Root.Items.Single().Submenu;
            Assert.That(outfits.Name, Is.EqualTo("Outfits"));
            var summer = outfits.Items.Single().Submenu;
            Assert.That(summer.Name, Is.EqualTo("Summer"));
            Assert.That(summer.Items.Single().Control.Path, Is.EqualTo("Outfits/Summer/Bikini"));
            Assert.That(tree.Messages.Count(m => m.Key == "ad.info.path_created"), Is.EqualTo(2));
        }

        [Test]
        public void InstallPathReusesExistingSubmenu()
        {
            var old = Menu(Toggle("OldToggle", "Existing"));
            var existingRoot = Menu(Submenu("Old", old), Toggle("RootToggle", "Existing"));
            var menu = root.AddComponent<AvatarDoc>();
            menu.installPath = "Old";
            WithObject(menu.AddEntry(EntryKind.Toggle, "New"), root);

            var tree = MenuModel.Build(root.transform, existingRoot, new[] { menu });

            Assert.That(tree.Root.IsExisting, Is.True);
            Assert.That(tree.Root.ExistingMenu, Is.SameAs(existingRoot));
            Assert.That(tree.Root.Items.Count, Is.EqualTo(2));
            var oldNode = tree.Root.Items[0].Submenu;
            Assert.That(oldNode.IsExisting, Is.True);
            Assert.That(oldNode.ExistingMenu, Is.SameAs(old));
            Assert.That(oldNode.Items.Count, Is.EqualTo(2));
            Assert.That(oldNode.Items[0].Existing.name, Is.EqualTo("OldToggle"));
            Assert.That(oldNode.Items[1].Control.Entry.name, Is.EqualTo("New"));
            Assert.That(oldNode.ContainsGenerated(), Is.True);
            Assert.That(tree.Root.Items[1].Existing.name, Is.EqualTo("RootToggle"));
            Assert.That(tree.Messages.Any(m => m.Key == "ad.info.path_created"), Is.False);
        }

        [Test]
        public void UntouchedExistingSubmenuIsNotGenerated()
        {
            var old = Menu(Toggle("OldToggle", "Existing"));
            var existingRoot = Menu(Submenu("Old", old));
            var menu = root.AddComponent<AvatarDoc>();
            WithObject(menu.AddEntry(EntryKind.Toggle, "New"), root);

            var tree = MenuModel.Build(root.transform, existingRoot, new[] { menu });

            Assert.That(tree.Root.ContainsGenerated(), Is.True);
            Assert.That(tree.Root.Items[0].Submenu.ContainsGenerated(), Is.False);
        }

        [Test]
        public void AsSubmenuWrapsEntries()
        {
            var child = new GameObject("Pack");
            child.transform.SetParent(root.transform);
            var menu = child.AddComponent<AvatarDoc>();
            menu.installMode = InstallMode.AsSubmenu;
            WithObject(menu.AddEntry(EntryKind.Toggle, "Thing"), child);

            var tree = MenuModel.Build(root.transform, null, new[] { menu });

            var pack = tree.Root.Items.Single().Submenu;
            Assert.That(pack.Name, Is.EqualTo("Pack"));
            Assert.That(pack.Component, Is.SameAs(menu));
            Assert.That(pack.Items.Single().Control.Path, Is.EqualTo("Pack/Thing"));
        }

        [Test]
        public void TwoComponentsWithTheSameSubmenuNameMerge()
        {
            var a = root.AddComponent<AvatarDoc>();
            a.installMode = InstallMode.AsSubmenu; a.menuName = "Outfits";
            WithObject(a.AddEntry(EntryKind.Toggle, "A"), root);
            var child = new GameObject("Child"); child.transform.SetParent(root.transform);
            var b = child.AddComponent<AvatarDoc>();
            b.installMode = InstallMode.AsSubmenu; b.menuName = "Outfits";
            WithObject(b.AddEntry(EntryKind.Toggle, "B"), child);

            var tree = MenuModel.Build(root.transform, null, new[] { a, b });

            Assert.That(tree.Root.Items.Count, Is.EqualTo(1));
            Assert.That(tree.Root.Items[0].Submenu.Items.Select(i => i.Control.Entry.name), Is.EqualTo(new[] { "A", "B" }));
            Assert.That(tree.Messages.Any(m => m.Key == "ad.info.same_submenu_merged"), Is.True);
        }

        [Test]
        public void OrphanEntryGoesToRootWithWarning()
        {
            var menu = root.AddComponent<AvatarDoc>();
            WithObject(menu.AddEntry(EntryKind.Toggle, "Lost", "no-such-id"), root);

            var tree = MenuModel.Build(root.transform, null, new[] { menu });

            Assert.That(tree.Root.Items.Single().Control.Entry.name, Is.EqualTo("Lost"));
            Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.orphan_entry" && m.Severity == MessageSeverity.Warning), Is.True);
        }

        [Test]
        public void TriggersAreCollectedSeparately()
        {
            var menu = root.AddComponent<AvatarDoc>();
            WithObject(menu.AddEntry(EntryKind.Trigger, "Blink fix"), root);
            WithObject(menu.AddEntry(EntryKind.Toggle, "Hat"), root);

            var tree = MenuModel.Build(root.transform, null, new[] { menu });

            Assert.That(tree.Root.Items.Count, Is.EqualTo(1));
            Assert.That(tree.Triggers.Single().Entry.name, Is.EqualTo("Blink fix"));
        }

        [Test]
        public void ComponentsAreCollectedInHierarchyOrderAndDisabledOnesSkipped()
        {
            var second = new GameObject("Second"); second.transform.SetParent(root.transform);
            var first = new GameObject("First"); first.transform.SetParent(root.transform); first.transform.SetAsFirstSibling();
            var rootMenu = root.AddComponent<AvatarDoc>();
            var firstMenu = first.AddComponent<AvatarDoc>();
            var secondMenu = second.AddComponent<AvatarDoc>();
            secondMenu.enabled = false;

            var components = MenuModel.CollectComponents(root.transform);

            Assert.That(components, Is.EqualTo(new[] { rootMenu, firstMenu }));
        }

        [Test]
        public void MissingTargetsAndEmptyEntriesProduceWarnings()
        {
            var menu = root.AddComponent<AvatarDoc>();
            var missing = menu.AddEntry(EntryKind.Toggle, "Missing");
            missing.objects.Add(new ObjectTarget { target = null });
            menu.AddEntry(EntryKind.Toggle, "Empty");
            var badShape = menu.AddEntry(EntryKind.Toggle, "BadShape");
            var smr = new GameObject("Body").AddComponent<SkinnedMeshRenderer>();
            smr.transform.SetParent(root.transform);
            smr.sharedMesh = new Mesh();
            badShape.blendShapes.Add(new BlendShapeTarget { renderer = smr, shape = "Nope" });

            var tree = MenuModel.Build(root.transform, null, new[] { menu });

            Assert.That(tree.Messages.Count(m => m.Key == "ad.warn.target_missing"), Is.EqualTo(2));
            Assert.That(tree.Messages.Count(m => m.Key == "ad.warn.no_effects"), Is.EqualTo(1));
            Assert.That(tree.HasErrors, Is.False);
        }

        [Test]
        public void GroupMemberWithoutEffectsIsALegitimateOffChoice()
        {
            var menu = root.AddComponent<AvatarDoc>();
            var hat = new GameObject("Hat"); hat.transform.SetParent(root.transform);
            var casual = menu.AddEntry(EntryKind.Toggle, "Casual");
            casual.exclusiveGroup = "Outfits";
            casual.objects.Add(new ObjectTarget { target = hat });
            var none = menu.AddEntry(EntryKind.Toggle, "None");
            none.exclusiveGroup = "Outfits";

            var tree = MenuModel.Build(root.transform, null, new[] { menu });

            Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.no_effects" && m.EntryIds.Contains(none.id)), Is.False);
            Assert.That(tree.HasErrors, Is.False);
        }

        [Test]
        public void TargetsOutsideTheAvatarAreReported()
        {
            var stray = new GameObject("Stray");
            try
            {
                var menu = root.AddComponent<AvatarDoc>();
                WithObject(menu.AddEntry(EntryKind.Toggle, "Stray toggle"), stray);

                var tree = MenuModel.Build(root.transform, null, new[] { menu });

                Assert.That(tree.Messages.Count(m => m.Key == "ad.warn.target_outside_avatar"), Is.EqualTo(1));
                Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.target_missing"), Is.False);
                Assert.That(tree.HasErrors, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(stray);
            }
        }

        [Test]
        public void SharedTargetsAreReportedOnce()
        {
            var shared = new GameObject("Shared"); shared.transform.SetParent(root.transform);
            var outfit = new GameObject("Outfit"); outfit.transform.SetParent(root.transform);
            var menu = root.AddComponent<AvatarDoc>();
            WithObject(menu.AddEntry(EntryKind.Toggle, "A"), shared);
            WithObject(menu.AddEntry(EntryKind.Toggle, "B"), shared);
            WithObject(menu.AddEntry(EntryKind.Toggle, "C"), outfit).exclusiveGroup = "G";
            WithObject(menu.AddEntry(EntryKind.Toggle, "D"), outfit).exclusiveGroup = "G";

            var tree = MenuModel.Build(root.transform, null, new[] { menu });

            var shares = tree.Messages.Where(m => m.Key == "ad.info.target_shared").ToList();
            Assert.That(shares.Count, Is.EqualTo(1));
            Assert.That(shares[0].Args[0], Is.EqualTo("Shared"));
            Assert.That(shares[0].Args[1], Is.EqualTo("A, B"));
            Assert.That(tree.HasErrors, Is.False);
        }

        [Test]
        public void SharedTargetReportIncludesTriggers()
        {
            var menu = avatar.AddMenu();
            var toggle = MenuEntry.Create(EntryKind.Toggle, "Hat");
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat });
            var trigger = MenuEntry.Create(EntryKind.Trigger, "Hide on AFK");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "AFK";
            trigger.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = false });
            menu.entries.Add(toggle);
            menu.entries.Add(trigger);

            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            var shared = tree.Messages.Single(m => m.Key == "ad.info.target_shared");
            Assert.That(shared.EntryIds, Is.EquivalentTo(new[] { toggle.id, trigger.id }));
        }

        /// <summary>Custom animations only ever build on a toggle or a radial; a trigger holding nothing else is still empty.</summary>
        [Test]
        public void TriggerWithOnlyAnimationsHasNoEffects()
        {
            var clip = new AnimationClip { name = "X" };
            try
            {
                var menu = avatar.AddMenu();
                var trigger = MenuEntry.Create(EntryKind.Trigger, "Ghost");
                trigger.source = TriggerSource.Parameter;
                trigger.sourceParameter = "AFK";
                trigger.animations.Add(new AnimationTarget { clip = clip });
                menu.entries.Add(trigger);

                var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

                Assert.That(trigger.HasEffects, Is.False);
                Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.no_effects" && m.EntryIds.Contains(trigger.id)), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void SplitPathTrimsAndSkipsEmptySegments()
        {
            Assert.That(MenuModel.SplitPath(" Outfits / Summer //"), Is.EqualTo(new[] { "Outfits", "Summer" }));
            Assert.That(MenuModel.SplitPath(""), Is.Empty);
            Assert.That(MenuModel.SplitPath(null), Is.Empty);
        }

        [Test]
        public void CyclicParentIdsBecomeOrphansWithoutHanging()
        {
            var menu = root.AddComponent<AvatarDoc>();
            var a = menu.AddEntry(EntryKind.Submenu, "A");
            var b = menu.AddEntry(EntryKind.Submenu, "B");
            a.parentId = b.id;
            b.parentId = a.id;
            WithObject(menu.AddEntry(EntryKind.Toggle, "T", a.id), root);

            var tree = MenuModel.Build(root.transform, null, new[] { menu });

            Assert.That(tree.Messages.Count(m => m.Key == "ad.warn.orphan_entry"), Is.GreaterThanOrEqualTo(1));
            Assert.That(tree.Controls.Single().Entry.name, Is.EqualTo("T"));
        }

        [Test]
        public void CyclicExistingMenusAreReadOnce()
        {
            var x = Menu();
            var y = Menu();
            x.controls = new[] { Submenu("Y", y) }.ToList();
            y.controls = new[] { Submenu("X", x) }.ToList();
            var menu = root.AddComponent<AvatarDoc>();
            WithObject(menu.AddEntry(EntryKind.Toggle, "New"), root);

            var tree = MenuModel.Build(root.transform, x, new[] { menu });

            Assert.That(tree.Root.ExistingMenu, Is.SameAs(x));
            var yNode = tree.Root.Items[0].Submenu;
            Assert.That(yNode.IsExisting, Is.True);
            Assert.That(yNode.ExistingMenu, Is.SameAs(y));
            var xNode = yNode.Items.Single().Submenu;
            Assert.That(xNode.Items, Is.Empty);
        }

        [Test]
        public void SplitOverflowMovesExtrasToMorePages()
        {
            var menu = root.AddComponent<AvatarDoc>();
            for (int i = 0; i < 16; i++) WithObject(menu.AddEntry(EntryKind.Toggle, "T" + i), root);
            var tree = MenuModel.Build(root.transform, null, new[] { menu });

            MenuModel.SplitOverflow(tree, null);

            int max = VRCExpressionsMenu.MAX_CONTROLS;
            Assert.That(tree.Root.Items.Count, Is.EqualTo(max));
            var more = tree.Root.Items[max - 1].Submenu;
            Assert.That(more.Name, Is.EqualTo("More"));
            Assert.That(more.Items.Count, Is.EqualTo(max));
            var more2 = more.Items[max - 1].Submenu;
            Assert.That(more2.Name, Is.EqualTo("More"));
            Assert.That(more2.Items.Count, Is.EqualTo(16 - 2 * (max - 1)));
            Assert.That(more2.Items.All(i => i.Control != null), Is.True);
            Assert.That(tree.Messages.Count(m => m.Key == "ad.info.menu_split"), Is.EqualTo(1));
            Assert.That(tree.Controls.Count, Is.EqualTo(16));
        }

        [Test]
        public void SplitOverflowLeavesSmallMenusAlone()
        {
            var menu = root.AddComponent<AvatarDoc>();
            for (int i = 0; i < VRCExpressionsMenu.MAX_CONTROLS; i++) WithObject(menu.AddEntry(EntryKind.Toggle, "T" + i), root);
            var tree = MenuModel.Build(root.transform, null, new[] { menu });

            MenuModel.SplitOverflow(tree, null);

            Assert.That(tree.Root.Items.Count, Is.EqualTo(VRCExpressionsMenu.MAX_CONTROLS));
            Assert.That(tree.Root.Items.All(i => i.Control != null), Is.True);
            Assert.That(tree.Messages.Any(m => m.Key == "ad.info.menu_split"), Is.False);
        }

        [Test]
        public void SplitOverflowRefreshesPathsOfMovedSubmenus()
        {
            var menu = root.AddComponent<AvatarDoc>();
            for (int i = 0; i < VRCExpressionsMenu.MAX_CONTROLS; i++) WithObject(menu.AddEntry(EntryKind.Toggle, "T" + i), root);
            var sub = menu.AddEntry(EntryKind.Submenu, "Late");
            WithObject(menu.AddEntry(EntryKind.Toggle, "Inner", sub.id), root);
            var tree = MenuModel.Build(root.transform, null, new[] { menu });

            MenuModel.SplitOverflow(tree, null);

            var late = tree.AllNodes().Single(n => n.Name == "Late");
            Assert.That(late.Parent.Name, Is.EqualTo("More"));
            Assert.That(late.Path, Is.EqualTo("More/Late"));
        }

        [Test]
        public void SharedTargetsAreAggregatedPerTarget()
        {
            var menu = root.AddComponent<AvatarDoc>();
            WithObject(menu.AddEntry(EntryKind.Toggle, "A"), root);
            WithObject(menu.AddEntry(EntryKind.Toggle, "B"), root);
            WithObject(menu.AddEntry(EntryKind.Toggle, "C"), root);
            var radial = menu.AddEntry(EntryKind.Radial, "R");
            radial.objects.Add(new ObjectTarget { target = root });
            radial.blendShapes.Add(new BlendShapeTarget { shape = "x" });

            var tree = MenuModel.Build(root.transform, null, new[] { menu });

            var shared = tree.Messages.Where(m => m.Key == "ad.info.target_shared").ToList();
            Assert.That(shared.Count, Is.EqualTo(1));
            Assert.That((string)shared[0].Args[1], Is.EqualTo("A, B, C"));
        }

        [Test]
        public void OverridesAreRulesNotControls()
        {
            var menu = avatar.AddMenu();
            var rule = MenuEntry.Create(EntryKind.Override, "Silence cringe");
            rule.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 0f });
            menu.entries.Add(rule);
            var empty = MenuEntry.Create(EntryKind.Override, "Empty");
            menu.entries.Add(empty);

            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            Assert.That(tree.Overrides.Select(o => o.Entry.id), Is.EqualTo(new[] { rule.id, empty.id }));
            Assert.That(tree.Controls.Any(c => c.Entry.kind == EntryKind.Override), Is.False);
            Assert.That(tree.Root.Items.Any(i => i.Control != null && i.Control.Entry.kind == EntryKind.Override), Is.False);
            Assert.That(tree.Messages.Single(m => m.Key == "ad.warn.no_effects").EntryIds, Does.Contain(empty.id));
            Assert.That(rule.IsRule, Is.True);
        }

        [Test]
        public void SharedTargetReportIncludesOverrides()
        {
            var menu = avatar.AddMenu();
            var toggle = MenuEntry.Create(EntryKind.Toggle, "Blush");
            toggle.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 80f });
            var rule = MenuEntry.Create(EntryKind.Override, "Silence blush");
            rule.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 0f });
            menu.entries.Add(toggle);
            menu.entries.Add(rule);

            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            var shared = tree.Messages.Single(m => m.Key == "ad.info.target_shared_override");
            Assert.That(shared.EntryIds, Is.EquivalentTo(new[] { toggle.id, rule.id }));
            Assert.That(tree.Messages.Any(m => m.Key == "ad.info.target_shared"), Is.False, "the override, not the menu order, decides");
        }

        [Test]
        public void OverrideOnlyGroupsUseTheConflictWarning()
        {
            var menu = avatar.AddMenu();
            var first = MenuEntry.Create(EntryKind.Override, "Silence blush");
            first.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 0f });
            var second = MenuEntry.Create(EntryKind.Override, "Keep some blush");
            second.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 20f });
            menu.entries.Add(first);
            menu.entries.Add(second);

            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            Assert.That(tree.Messages.Any(m => m.Key == "ad.warn.override_conflict"), Is.True);
            Assert.That(tree.Messages.Any(m => m.Key.StartsWith("ad.info.target_shared")), Is.False);
        }

        [Test]
        public void ConflictingOverridesWarn()
        {
            var menu = avatar.AddMenu();
            var first = MenuEntry.Create(EntryKind.Override, "Silence blush");
            first.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 0f });
            var second = MenuEntry.Create(EntryKind.Override, "Keep some blush");
            second.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 20f });
            menu.entries.Add(first);
            menu.entries.Add(second);

            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            var conflict = tree.Messages.Single(m => m.Key == "ad.warn.override_conflict");
            Assert.That(conflict.EntryIds, Is.EquivalentTo(new[] { first.id, second.id }));

            var other = avatar.AddMenu(avatar.Hat);
            var third = MenuEntry.Create(EntryKind.Override, "Wide eyes");
            third.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 100f });
            other.entries.Add(third);
            menu.entries.Remove(second);

            var apart = MenuModel.Build(avatar.Root.transform, null, new[] { menu, other });

            Assert.That(apart.Messages.Any(m => m.Key == "ad.warn.override_conflict"), Is.False, "two overrides on different shapes do not conflict");
        }

        [Test]
        public void OverridesThatHoldAShapeAtTheSameValueDoNotConflict()
        {
            var menu = avatar.AddMenu();
            var first = MenuEntry.Create(EntryKind.Override, "No blush");
            first.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 20f });
            var second = MenuEntry.Create(EntryKind.Override, "No blush either");
            second.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 20f });
            menu.entries.Add(first);
            menu.entries.Add(second);

            var same = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            Assert.That(same.Messages.Any(m => m.Key == "ad.warn.override_conflict"), Is.False, "both hold the shape at 20, so whichever wins, the avatar looks the same");

            var third = MenuEntry.Create(EntryKind.Override, "Some blush");
            third.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 60f });
            menu.entries.Add(third);

            var differ = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            var conflict = differ.Messages.Single(m => m.Key == "ad.warn.override_conflict");
            Assert.That(conflict.EntryIds, Is.EquivalentTo(new[] { second.id, third.id }), "only the pair that holds it at different values");
        }

        [Test]
        public void ConflictingOverridesWarnAcrossComponents()
        {
            var first = avatar.AddMenu();
            var second = avatar.AddMenu(avatar.Hat);
            var one = MenuEntry.Create(EntryKind.Override, "Silence blush");
            one.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 0f });
            var two = MenuEntry.Create(EntryKind.Override, "Keep some blush");
            two.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 20f });
            first.entries.Add(one);
            second.entries.Add(two);

            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { first, second });

            var conflict = tree.Messages.Single(m => m.Key == "ad.warn.override_conflict");
            Assert.That(conflict.EntryIds, Is.EquivalentTo(new[] { one.id, two.id }));
        }

        [Test]
        public void ComponentIsNamedAvatarDocAndRemembersItsOldName()
        {
            var type = typeof(AvatarDoc);
            Assert.That(type.Name, Is.EqualTo("AvatarDoc"));
            var moved = type.GetCustomAttributes(typeof(UnityEngine.Scripting.APIUpdating.MovedFromAttribute), false);
            Assert.That(moved, Is.Not.Empty, "the old class name is recorded so serialized references keep resolving");
            var menu = type.GetCustomAttributes(typeof(AddComponentMenu), false).Cast<AddComponentMenu>().Single();
            Assert.That(menu.componentMenu, Is.EqualTo("DocMe/AvatarDoc"));
        }

        [Test]
        public void StyledNamesMatchInstallPathsAndStayOutOfGeneratedNames()
        {
            var first = avatar.AddMenu();
            var outfits = MenuEntry.Create(EntryKind.Submenu, "<color=#00FF00><b>Outfits</b></color>");
            var hat = MenuEntry.Create(EntryKind.Toggle, "<i>Hat</i>");
            hat.parentId = outfits.id;
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            first.entries.Add(outfits);
            first.entries.Add(hat);
            var second = avatar.AddMenu(avatar.Glasses);
            second.installPath = "Outfits";
            var glasses = MenuEntry.Create(EntryKind.Toggle, "Glasses");
            glasses.objects.Add(new ObjectTarget { target = avatar.Glasses });
            second.entries.Add(glasses);

            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { first, second });

            var node = tree.Root.Items.Single(i => i.Submenu != null && i.Submenu.EntryId == outfits.id).Submenu;
            Assert.That(node.Name, Is.EqualTo("Outfits"));
            Assert.That(node.RichName, Is.EqualTo("<color=#00FF00><b>Outfits</b></color>"));
            Assert.That(node.Items.Count, Is.EqualTo(2), "the second component installed into the styled submenu by its plain name");
            var control = tree.FindControl(hat.id);
            Assert.That(control.Path, Is.EqualTo("Outfits/Hat"));
            Assert.That(control.LayerName, Is.EqualTo("AD: Hat"));
            var plan = ParameterPlanner.Plan(tree, new Dictionary<string, ExistingParameter>());
            Assert.That(plan.Bindings[control].Parameter.Name, Is.EqualTo("AD/Hat"));
        }
    }
}
