#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.preview;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    public class PreviewModelTests
    {
        TestAvatar avatar;
        AvatarDoc menu;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            menu = avatar.AddMenu();
        }

        [TearDown]
        public void TearDown() => avatar.Dispose();

        MenuEntry Toggle(string name, bool defaultOn = false)
        {
            var entry = MenuEntry.Create(EntryKind.Toggle, name);
            entry.defaultOn = defaultOn;
            menu.entries.Add(entry);
            return entry;
        }

        Dictionary<Renderer, RendererState> Compute(Dictionary<string, float> overrides = null)
            => PreviewModel.Compute(avatar.Root.transform, new[] { menu }, overrides ?? new Dictionary<string, float>(), SceneReader.Direct);

        [Test]
        public void AnOffValueOfItsOwnShowsWhileTheToggleIsOff()
        {
            var skin = Toggle("Oily skin");
            skin.properties.Add(new MaterialPropertyTarget
            {
                renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float,
                value = 1f, minValue = 0.6f, explicitOff = true,
            });
            avatar.MaterialA.SetFloat("_Glossiness", 0.1f);

            var states = Compute();

            Assert.That(states[avatar.Body].Properties["_Glossiness"].Value.x, Is.EqualTo(0.6f).Within(0.0001f), "the preview rests on the toggle's own off value");
        }

        [Test]
        public void OverrideOnHidesTheObject()
        {
            var hide = Toggle("Hide hat");
            hide.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = false });
            var hat = avatar.Hat.GetComponent<MeshRenderer>();

            Assert.That(Compute().ContainsKey(hat), Is.False, "off by default, the hat stays as the scene has it");

            var states = Compute(new Dictionary<string, float> { [hide.id] = 1f });
            Assert.That(states[hat].Visible, Is.False);
        }

        [Test]
        public void DefaultOnWritesTheShapeWeight()
        {
            var blush = Toggle("Blush", defaultOn: true);
            blush.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 80f });

            var states = Compute();

            Assert.That(states[avatar.Body].Visible, Is.True);
            Assert.That(states[avatar.Body].Shapes[0], Is.EqualTo(80f));
        }

        [Test]
        public void RadialLerpsBetweenMinAndMax()
        {
            var radial = MenuEntry.Create(EntryKind.Radial, "Eyes");
            radial.defaultValue = 0.25f;
            radial.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", minWeight = 0f, maxWeight = 100f });
            menu.entries.Add(radial);

            Assert.That(Compute()[avatar.Body].Shapes[1], Is.EqualTo(25f));
            Assert.That(Compute(new Dictionary<string, float> { [radial.id] = 0.5f })[avatar.Body].Shapes[1], Is.EqualTo(50f));
        }

        [Test]
        public void LaterControlWinsOnASharedTarget()
        {
            var first = Toggle("First", defaultOn: true);
            first.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 30f });
            var second = Toggle("Second", defaultOn: true);
            second.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 70f });

            Assert.That(Compute()[avatar.Body].Shapes[0], Is.EqualTo(70f));
        }

        [Test]
        public void ExclusiveGroupKeepsOneMemberOn()
        {
            var a = Toggle("A", defaultOn: true);
            a.exclusiveGroup = "Outfits";
            a.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = true });
            var b = Toggle("B");
            b.exclusiveGroup = "Outfits";
            b.objects.Add(new ObjectTarget { target = avatar.Glasses, activeWhenOn = true });
            var hat = avatar.Hat.GetComponent<MeshRenderer>();
            var glasses = avatar.Glasses.GetComponent<MeshRenderer>();
            if (glasses == null) glasses = avatar.Glasses.AddComponent<MeshRenderer>();

            var byDefault = Compute();
            Assert.That(byDefault.ContainsKey(hat), Is.False, "A is on and the hat is active in the scene already");
            Assert.That(byDefault[glasses].Visible, Is.False, "B is off, so its object is forced inactive");

            var overridden = Compute(new Dictionary<string, float> { [b.id] = 1f });
            Assert.That(overridden[hat].Visible, Is.False, "the override on B switches A off");
            Assert.That(overridden.ContainsKey(glasses), Is.False);
        }

        /// <summary>A renderer of its own under the avatar, so a hidden piece can be told apart from the pieces the outfits switch on.</summary>
        MeshRenderer AddPiece(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(avatar.Root.transform);
            go.AddComponent<MeshFilter>();
            return go.AddComponent<MeshRenderer>();
        }

        [Test]
        public void SharedGroupTargetsFollowTheSelectedMember()
        {
            var shared = AddPiece("Shared");
            var members = new List<MenuEntry>();
            foreach (var name in new[] { "A", "B", "C" })
            {
                var member = Toggle(name);
                member.exclusiveGroup = "Outfits";
                member.objects.Add(new ObjectTarget { target = shared.gameObject, activeWhenOn = false });
                member.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
                member.materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 1, material = avatar.MaterialB });
                member.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 1f });
                members.Add(member);
            }

            var states = Compute(new Dictionary<string, float> { [members[0].id] = 1f });

            Assert.That(states[shared].Visible, Is.False, "the members that are off must not write the shared object back on");
            Assert.That(states[avatar.Body].Shapes[0], Is.EqualTo(100f), "the shared shape keeps the selected member's weight");
            Assert.That(states[avatar.Body].Materials[1], Is.SameAs(avatar.MaterialB), "the shared slot keeps the selected member's material");
            Assert.That(states[avatar.Body].Properties["_Glossiness"].Value.x, Is.EqualTo(1f), "the shared property keeps the selected member's value");
        }

        [Test]
        public void StaleGroupNameOnANonToggleIsIgnoredByThePreview()
        {
            // A preset that used to be a toggle keeps the group name in its data. Only toggles are group members,
            // so it must not claim the group's place: the group belongs where its first toggle member sits.
            var preset = MenuEntry.Create(EntryKind.Preset, "Was a toggle");
            preset.exclusiveGroup = "Outfits";
            menu.entries.Add(preset);
            var plain = Toggle("Plain", defaultOn: true);
            plain.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 30f });
            var casual = Toggle("Casual", defaultOn: true);
            casual.exclusiveGroup = "Outfits";
            casual.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 80f });
            var formal = Toggle("Formal");
            formal.exclusiveGroup = "Outfits";
            formal.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 10f });

            var states = Compute();

            Assert.That(states[avatar.Body].Shapes[0], Is.EqualTo(80f), "the group is written at its first toggle member, after the plain toggle before it");
        }

        [Test]
        public void HiddenPiecesStayHiddenForANonLastOutfit()
        {
            var shoes = AddPiece("Shoes");
            var casual = Toggle("Casual", defaultOn: true);
            casual.exclusiveGroup = "Outfits";
            casual.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = true });
            casual.objects.Add(new ObjectTarget { target = shoes.gameObject, activeWhenOn = false });
            var formal = Toggle("Formal");
            formal.exclusiveGroup = "Outfits";
            formal.objects.Add(new ObjectTarget { target = avatar.Glasses, activeWhenOn = true });
            formal.objects.Add(new ObjectTarget { target = shoes.gameObject, activeWhenOn = false });
            var none = Toggle("None");
            none.exclusiveGroup = "Outfits";

            var states = Compute();

            Assert.That(states[shoes].Visible, Is.False, "Casual is on, so the pieces hidden while any outfit is on stay hidden");
        }

        [Test]
        public void FlipTriggerIgnoresItsLeftoverTargets()
        {
            var leftover = AddPiece("Leftover");
            var hat = Toggle("Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = true });
            var trigger = MenuEntry.Create(EntryKind.Trigger, "Boop");
            trigger.source = TriggerSource.Contact;
            trigger.contactAnchor = avatar.Body.transform;
            trigger.touchMode = TouchMode.FlipToggle;
            trigger.flipToggleId = hat.id;
            // Left over from when the trigger held effects of its own; a flip trigger has no receiver for them.
            trigger.objects.Add(new ObjectTarget { target = leftover.gameObject, activeWhenOn = false });
            trigger.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = false });
            menu.entries.Add(trigger);

            var states = Compute(new Dictionary<string, float> { [trigger.id] = 1f });

            Assert.That(states.ContainsKey(leftover), Is.False, "a flip trigger writes nothing of its own");

            var tree = MenuModel.Build(avatar.Root.transform, null, new[] { menu });

            Assert.That(tree.Messages.Any(m => m.Key == "ad.info.target_shared"), Is.False, "its leftover targets are not reported as shared either");
        }

        [Test]
        public void LinkFollowsTheSourceWeight()
        {
            avatar.Body.SetBlendShapeWeight(0, 60f);
            var link = MenuEntry.Create(EntryKind.Trigger, "Blush link");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            link.sourceThreshold = 50f;
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Follow, multiplier = 0.5f });
            link.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = true });
            menu.entries.Add(link);

            var states = Compute();

            Assert.That(states[avatar.Body].Shapes[1], Is.EqualTo(30f));
            Assert.That(states.ContainsKey(avatar.Hat.GetComponent<MeshRenderer>()), Is.False, "above the threshold the hat is active, which the scene already shows");
        }

        [Test]
        public void LinkFollowsAPendingToggleValue()
        {
            var blush = Toggle("Blush", defaultOn: true);
            blush.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 80f });
            var link = MenuEntry.Create(EntryKind.Trigger, "Wide eyes");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            link.sourceThreshold = 50f;
            link.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", mode = ShapeEffectMode.Fixed, weight = 40f });
            menu.entries.Add(link);

            Assert.That(Compute()[avatar.Body].Shapes[1], Is.EqualTo(32f).Within(0.001f), "the link reads the weight the toggle writes in the same pass (80, so 80% of 40 in Smooth), not the stale scene weight");
        }

        [Test]
        public void ConditionTriggerAppliesNothingWithoutAnOverride()
        {
            var trigger = MenuEntry.Create(EntryKind.Trigger, "Hide on AFK");
            trigger.source = TriggerSource.Parameter;
            trigger.sourceParameter = "AFK";
            trigger.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = false });
            menu.entries.Add(trigger);
            var hat = avatar.Hat.GetComponent<MeshRenderer>();

            Assert.That(Compute().ContainsKey(hat), Is.False);
            Assert.That(Compute(new Dictionary<string, float> { [trigger.id] = 1f })[hat].Visible, Is.False);
        }

        [Test]
        public void DefaultComesFromTheBoundParameter()
        {
            avatar.ExpressionParameters.parameters = avatar.ExpressionParameters.parameters
                .Concat(new[] { new VRCExpressionParameters.Parameter { name = "Hat", valueType = VRCExpressionParameters.ValueType.Bool, saved = true, networkSynced = true, defaultValue = 1f } })
                .ToArray();
            avatar.Fx.AddParameter("Hat", AnimatorControllerParameterType.Bool);
            var hide = Toggle("Hide hat");
            hide.parameterName = "Hat";
            hide.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = false });
            var hat = avatar.Hat.GetComponent<MeshRenderer>();

            Assert.That(Compute()[hat].Visible, Is.False, "the declared default wins over the entry's own default off");
        }

        [Test]
        public void MaterialSwapWritesTheSlot()
        {
            var swap = Toggle("Swap", defaultOn: true);
            swap.materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 1, material = avatar.MaterialB });

            Assert.That(Compute()[avatar.Body].Materials[1], Is.SameAs(avatar.MaterialB));
            Assert.That(Compute(new Dictionary<string, float> { [swap.id] = 0f }).ContainsKey(avatar.Body), Is.False);
        }

        [Test]
        public void OffToggleWithSceneWeightIsNotEmitted()
        {
            var blush = Toggle("Blush", defaultOn: false);
            blush.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 80f });

            Assert.That(Compute().ContainsKey(avatar.Body), Is.False);

            var states = Compute(new Dictionary<string, float> { [blush.id] = 1f });
            Assert.That(states[avatar.Body].Shapes[0], Is.EqualTo(80f));
        }

        [Test]
        public void NestedRendererIsHiddenByAnAncestor()
        {
            var bell = new GameObject("Bell");
            bell.transform.SetParent(avatar.Hat.transform);
            var bellRenderer = bell.AddComponent<MeshRenderer>();

            var hide = Toggle("Hide hat", defaultOn: true);
            hide.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = false });

            var states = Compute();

            Assert.That(states[bellRenderer].Visible, Is.False);
        }

        [Test]
        public void WatchSurvivesRepeatedChanges()
        {
            var value = new PublishedValue<int>(0, "AvatarDoctor watch test");
            int calls = 0;
            var watch = new PublishedValueWatch<int>(value, _ => calls++);
            watch.Start();

            value.Value = 1;
            value.Value = 2;
            value.Value = 3;
            Assert.That(calls, Is.EqualTo(3), "a PublishedValue drops its handlers before firing, so the watch re-adds itself");

            watch.Stop();
            value.Value = 4;
            Assert.That(calls, Is.EqualTo(3), "a stopped watch hears nothing more");
        }

        [Test]
        public void EffectMathMatchesTheClipRules()
        {
            var shape = new BlendShapeTarget { weight = 80f, minWeight = 10f, maxWeight = 50f, mode = ShapeEffectMode.Follow, multiplier = 2f };
            Assert.That(EffectMath.ObjectActive(new ObjectTarget { activeWhenOn = false }, on: false), Is.True);
            Assert.That(EffectMath.ToggleShapeWeight(shape, on: false, baseWeight: 5f), Is.EqualTo(5f));
            Assert.That(EffectMath.RadialShapeWeight(shape, 0.5f), Is.EqualTo(30f));
            Assert.That(EffectMath.LinkShapeWeight(shape, 70f, 50f, 0f), Is.EqualTo(100f), "follow scales and clamps");
            shape.mode = ShapeEffectMode.Fixed;
            Assert.That(EffectMath.LinkShapeWeight(shape, 40f, 50f, 12f), Is.EqualTo(12f), "fixed below the threshold restores the base weight");
        }

        [Test]
        public void PinTargetLeavesTheSceneWhenOff()
        {
            avatar.Body.SetBlendShapeWeight(0, 60f);
            var baseBlush = Toggle("Base blush", defaultOn: true);
            baseBlush.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 40f });
            var mute = Toggle("Mute");
            mute.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 0f, mode = ShapeEffectMode.Pin });

            Assert.That(Compute()[avatar.Body].Shapes[0], Is.EqualTo(40f), "off: the released pin leaves the earlier toggle's write in place");
            Assert.That(Compute(new Dictionary<string, float> { [mute.id] = 1f })[avatar.Body].Shapes[0], Is.EqualTo(0f));
        }

        [Test]
        public void GroupMemberPinReleasesInThePreview()
        {
            avatar.Body.SetBlendShapeWeight(0, 60f);
            var a = Toggle("A", defaultOn: true);
            a.exclusiveGroup = "Outfits";
            a.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 0f, mode = ShapeEffectMode.Pin });
            var b = Toggle("B");
            b.exclusiveGroup = "Outfits";
            b.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 30f });

            Assert.That(Compute()[avatar.Body].Shapes[0], Is.EqualTo(0f), "A is the group's default member, so its pin holds the shape");

            var switched = Compute(new Dictionary<string, float> { [b.id] = 1f });

            Assert.That(switched[avatar.Body].Shapes.ContainsKey(0), Is.False, "A is off, so its pin releases and the scene weight stands");
            Assert.That(switched[avatar.Body].Shapes[1], Is.EqualTo(30f));
        }

        [Test]
        public void ComputeReusesTheCachedPlan()
        {
            var blush = Toggle("Blush", defaultOn: true);
            blush.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 80f });
            AvatarPlanCache.Invalidate();

            Compute();
            int builds = PreviewModel.PlanBuilds;
            Compute();
            Compute(new Dictionary<string, float> { [blush.id] = 0f });

            Assert.That(PreviewModel.PlanBuilds, Is.EqualTo(builds), "nothing changed, so the tree and the parameter plan stand");

            AvatarPlanCache.Invalidate();
            Compute();

            Assert.That(PreviewModel.PlanBuilds, Is.EqualTo(builds + 1), "a dropped plan is built again");
        }

        [Test]
        public void PreviewEmitsPropertyValues()
        {
            avatar.MaterialA.SetFloat("_Glossiness", 0.1f);
            var shine = Toggle("Shine", defaultOn: true);
            shine.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 0.9f });

            var entry = Compute()[avatar.Body].Properties["_Glossiness"];
            Assert.That(entry.Type, Is.EqualTo(PropertyValueType.Float));
            Assert.That(entry.Value.x, Is.EqualTo(0.9f).Within(0.0001f));

            Assert.That(Compute(new Dictionary<string, float> { [shine.id] = 0f }).ContainsKey(avatar.Body), Is.False, "off matches the scene value, so nothing is emitted");
        }

        [Test]
        public void OverridePinsInThePreview()
        {
            avatar.Body.SetBlendShapeWeight(0, 60f);
            var rule = MenuEntry.Create(EntryKind.Override, "Silence blush");
            rule.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 0f });
            menu.entries.Add(rule);

            Assert.That(Compute()[avatar.Body].Shapes[0], Is.EqualTo(0f));
        }
    }
}
#endif
