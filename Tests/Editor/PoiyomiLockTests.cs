#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <summary>
    /// Poiyomi's lock, read from the shader's name and the material's tags only: a property marked "Renamed (when
    /// locked)" is animated under a second name once locked, one not marked at all is fixed at its value. The test
    /// shaders are in memory and only named like Poiyomi's.
    /// </summary>
    public class PoiyomiLockTests
    {
        const string UnlockedSource = @"Shader "".poiyomi/AvatarDocTests/Poiyomi Toon""
{
    Properties
    {
        _EmissionStrength (""Emission Strength"", Float) = 0
        _EmissionColor (""Emission Colour"", Color) = (0, 0, 0, 1)
        _MainTex (""Texture"", 2D) = ""white"" { }
        _MainTexUV (""UV"", Float) = 0
        _MainTexPan (""Panning"", Vector) = (0, 0, 0, 0)
        [DoNotLock] _LightingCap (""Max Brightness"", Range(0, 10)) = 1
        _ShineSpeed (""Shine Speed NL"", Float) = 0
        _GlowAnimated (""Glow Animated"", Float) = 0
        [DoNotLock] _DetailTex (""Detail"", 2D) = ""white"" { }
    }
    SubShader { Pass { } }
}";

        // Locked with the suffix BodyMat. _EmissionColor is one of the names Poiyomi duplicates instead of renaming:
        // the locked shader keeps the old definition beside the renamed one, and its code reads only the renamed one.
        const string LockedSource = @"Shader ""Hidden/Locked/.poiyomi/AvatarDocTests/Poiyomi Toon/0a1b2c3d""
{
    Properties
    {
        _EmissionStrength_BodyMat (""Emission Strength"", Float) = 0
        _EmissionColor (""Emission Colour"", Color) = (0, 0, 0, 1)
        _EmissionColor_BodyMat (""Emission Colour"", Color) = (0, 0, 0, 1)
        _Glow (""Glow"", Float) = 0
    }
    SubShader { Pass { } }
}";

        TestAvatar avatar;
        Shader unlockedShader;
        Shader lockedShader;
        readonly List<Object> made = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
            avatar = TestAvatar.Create();
            unlockedShader = ShaderUtil.CreateShaderAsset(UnlockedSource, false);
            lockedShader = ShaderUtil.CreateShaderAsset(LockedSource, false);
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            foreach (var obj in made) if (obj != null) Object.DestroyImmediate(obj);
            made.Clear();
            Object.DestroyImmediate(unlockedShader);
            Object.DestroyImmediate(lockedShader);
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        Material Poiyomi(string name, bool locked = false)
        {
            var material = new Material(locked ? lockedShader : unlockedShader) { name = name };
            made.Add(material);
            return material;
        }

        MenuEntry Toggle(AvatarDoc menu, string name, string property, float value = 1f)
        {
            var entry = menu.AddEntry(EntryKind.Toggle, name);
            entry.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = property, type = PropertyValueType.Float, value = value });
            return entry;
        }

        MenuTree Model(params AvatarDoc[] menus) => MenuModel.Build(avatar.Root.transform, avatar.ExpressionsMenu, menus);

        AnimatorController BuildAnimator(out List<ErrorContext> errors, params AvatarDoc[] menus)
        {
            var tree = Model(menus);
            var plan = ParameterPlanner.Plan(tree, new Dictionary<string, PlannedType> { { "Existing", PlannedType.Bool } });
            var baseState = BaseState.Capture(tree);
            DefaultsApplier.Apply(tree, plan, baseState);
            var context = new BuildContext(avatar.Root, null);
            var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            errors = ErrorReport.CaptureErrors(() => L.Report(AnimatorBuilder.Build(asc, tree, plan, baseState)));
            context.DeactivateAllExtensionContexts();
            return (AnimatorController)avatar.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
        }

        static AnimatorState State(AnimatorController fx, string layer, string state)
            => fx.layers.Single(l => l.name == layer).stateMachine.states.Single(s => s.state.name == state).state;

        /// <summary>The material property curves a clip writes on Body, by name.</summary>
        static List<string> MaterialCurves(Motion motion)
            => AnimationUtility.GetCurveBindings((AnimationClip)motion)
                .Where(b => b.path == "Body" && b.propertyName.StartsWith("material.", StringComparison.Ordinal))
                .Select(b => b.propertyName).OrderBy(n => n, StringComparer.Ordinal).ToList();

        static float Value(Motion motion, string property)
        {
            var curve = AnimationUtility.GetEditorCurve((AnimationClip)motion, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), property));
            Assert.That(curve, Is.Not.Null, property);
            return curve.Evaluate(0f);
        }

        AvatarDoctorView View(AvatarDoc menu) => new AvatarDoctorView(new SerializedObject(menu), menu, false, new MemoryViewPrefs());

        static string English(string key)
        {
            Assert.That(LocalizationFileTests.ShippedTexts("en-US").TryGetValue(key, out var text), Is.True, key + " is in the English file");
            return text;
        }

        // ---- the renaming rule ------------------------------------------------------------------------------------

        [Test]
        public void TheSuffixIsTheMaterialNameCleaned()
        {
            Assert.That(PoiyomiLock.CleanName(" Body Main "), Is.EqualTo("BodyMain"), "trimmed, spaces removed");
            Assert.That(PoiyomiLock.CleanName("Haut-Weiß"), Is.EqualTo("Haut2DWeiC39F"), "every other byte as two hex digits, of its UTF-8 form");
            Assert.That(PoiyomiLock.CleanName("体_1"), Is.EqualTo("E4BD93_1"), "a character beyond ASCII is every byte of it");
            Assert.That(PoiyomiLock.CleanName("a[b]^`\\"), Is.EqualTo("a5Bb5D5E605C"), "the bytes between Z and a that are not _");
            Assert.That(PoiyomiLock.Suffix(Poiyomi("Body Mat")), Is.EqualTo("BodyMat"));
        }

        [Test]
        public void ARenameSuffixTagWinsOverTheMaterialName()
        {
            var material = Poiyomi("Body Mat");
            material.SetOverrideTag("thry_rename_suffix", "My Glow");
            material.SetOverrideTag("_EmissionStrengthAnimated", "2");
            avatar.Body.sharedMaterials = new[] { material };
            var menu = avatar.AddMenu();
            Toggle(menu, "Glow", "_EmissionStrength");

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            Assert.That(PoiyomiLock.Suffix(material), Is.EqualTo("MyGlow"));
            Assert.That(MaterialCurves(State(fx, "AD: Glow", "On").motion), Is.EqualTo(new[] { "material._EmissionStrength", "material._EmissionStrength_MyGlow" }));
        }

        [Test]
        public void ARenamedPropertyIsWrittenUnderBothNames()
        {
            var material = Poiyomi("Body Mat");
            material.SetOverrideTag("_EmissionStrengthAnimated", "2");
            material.SetFloat("_EmissionStrength", 0.25f);
            avatar.Body.sharedMaterials = new[] { material };
            var menu = avatar.AddMenu();
            Toggle(menu, "Glow", "_EmissionStrength", 4f);

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            var on = State(fx, "AD: Glow", "On").motion;
            var off = State(fx, "AD: Glow", "Off").motion;
            Assert.That(MaterialCurves(on), Is.EqualTo(new[] { "material._EmissionStrength", "material._EmissionStrength_BodyMat" }),
                "the original name works while the material is unlocked, the renamed one once Poiyomi locked it at upload");
            Assert.That(MaterialCurves(off), Is.EqualTo(MaterialCurves(on)));
            Assert.That(Value(on, "material._EmissionStrength_BodyMat"), Is.EqualTo(4f).Within(0.0001f));
            Assert.That(Value(off, "material._EmissionStrength_BodyMat"), Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(Value(off, "material._EmissionStrength"), Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(Model(menu).Messages.Where(m => m.Key.Contains("poiyomi")), Is.Empty, "a renamed property is animated: nothing to warn about");
        }

        [Test]
        public void AnAnimatedPropertyIsWrittenOnce()
        {
            var material = Poiyomi("Body Mat");
            material.SetOverrideTag("_EmissionStrengthAnimated", "1");
            avatar.Body.sharedMaterials = new[] { material };
            var menu = avatar.AddMenu();
            Toggle(menu, "Glow", "_EmissionStrength");

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            Assert.That(MaterialCurves(State(fx, "AD: Glow", "On").motion), Is.EqualTo(new[] { "material._EmissionStrength" }));
            Assert.That(Model(menu).Messages.Where(m => m.Key.Contains("poiyomi")), Is.Empty);
        }

        [Test]
        public void UVAndPanPropertiesKeepTheirNames()
        {
            var material = Poiyomi("Body Mat");
            material.SetOverrideTag("_MainTexUVAnimated", "2");
            material.SetOverrideTag("_MainTexPanAnimated", "2");
            material.SetOverrideTag("_EmissionStrengthAnimated", "2");

            Assert.That(PoiyomiLock.RenamedName(material, "_MainTexUV"), Is.Null);
            Assert.That(PoiyomiLock.RenamedName(material, "_MainTexPan"), Is.Null);
            Assert.That(PoiyomiLock.RenamedName(material, "_EmissionStrength"), Is.EqualTo("_EmissionStrength_BodyMat"));

            avatar.Body.sharedMaterials = new[] { material };
            var menu = avatar.AddMenu();
            Toggle(menu, "Scroll", "_MainTexUV");
            var fx = BuildAnimator(out _, menu);
            Assert.That(MaterialCurves(State(fx, "AD: Scroll", "On").motion), Is.EqualTo(new[] { "material._MainTexUV" }));
        }

        [Test]
        public void ATexturesTilingIsRenamedWithItsTexture()
        {
            var material = Poiyomi("Body Mat");
            material.SetOverrideTag("_MainTexAnimated", "2");

            Assert.That(PoiyomiLock.RenamedName(material, "_MainTex_ST"), Is.EqualTo("_MainTex_BodyMat_ST"), "the texture's tiling follows the texture's own mark");
            Assert.That(PoiyomiLock.RenamedName(material, "_MainTex"), Is.EqualTo("_MainTex_BodyMat"));
        }

        [Test]
        public void ARenamedColourOnARadialWritesEveryChannelUnderBothNames()
        {
            var material = Poiyomi("Body Mat");
            material.SetOverrideTag("_EmissionColorAnimated", "2");
            avatar.Body.sharedMaterials = new[] { material };
            var menu = avatar.AddMenu();
            var dial = menu.AddEntry(EntryKind.Radial, "Tint");
            dial.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_EmissionColor", type = PropertyValueType.Color, minColor = Color.black, color = Color.red });

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            var tree = (BlendTree)fx.layers.Single(l => l.name == "AD: Tint").stateMachine.states.Single().state.motion;
            var expected = new[] { "a", "b", "g", "r" }.SelectMany(c => new[] { "material._EmissionColor." + c, "material._EmissionColor_BodyMat." + c }).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Assert.That(MaterialCurves(tree.children[0].motion), Is.EqualTo(expected));
            Assert.That(MaterialCurves(tree.children[1].motion), Is.EqualTo(expected));
            Assert.That(Value(tree.children[1].motion, "material._EmissionColor_BodyMat.r"), Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void AHoldTriggerWritesAndRestsBothNames()
        {
            var material = Poiyomi("Body Mat");
            material.SetOverrideTag("_EmissionStrengthAnimated", "2");
            material.SetFloat("_EmissionStrength", 0.25f);
            avatar.Body.sharedMaterials = new[] { material };
            var menu = avatar.AddMenu();
            var pat = menu.AddEntry(EntryKind.Trigger, "Pat");
            pat.source = TriggerSource.Contact;
            pat.contactAnchor = avatar.Hat.transform;
            pat.touchMode = TouchMode.Hold;
            pat.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_EmissionStrength", type = PropertyValueType.Float, value = 2f });

            GeneratedAssets.Note();
            try
            {
                var context = new BuildContext(avatar.Root, null);
                context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
                var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
                context.DeactivateAllExtensionContexts();
                Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error), Is.Empty);
                AvatarDoctorPlugin.FinishAfterOtherTools(context);
                var fx = (AnimatorController)avatar.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;

                var both = new[] { "material._EmissionStrength", "material._EmissionStrength_BodyMat" };
                var on = State(fx, "AD: Pat", "On").motion;
                Assert.That(MaterialCurves(on), Is.EqualTo(both));
                Assert.That(Value(on, "material._EmissionStrength_BodyMat"), Is.EqualTo(2f).Within(0.0001f));
                var reset = State(fx, "AD: Pat", "Reset").motion;
                Assert.That(MaterialCurves(reset), Is.EqualTo(both), "the trigger hands both names back to rest once on the way out");
                Assert.That(Value(reset, "material._EmissionStrength_BodyMat"), Is.EqualTo(0.25f).Within(0.0001f));
            }
            finally
            {
                GeneratedAssets.CleanUp();
            }
        }

        [Test]
        public void ANameTakenFromALockedMaterialIsNotSuffixedTwice()
        {
            var material = Poiyomi("Body Mat", locked: true);
            material.SetOverrideTag("_EmissionStrengthAnimated", "2");
            avatar.Body.sharedMaterials = new[] { material };
            var menu = avatar.AddMenu();
            Toggle(menu, "Glow", "_EmissionStrength_BodyMat");

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            Assert.That(MaterialCurves(State(fx, "AD: Glow", "On").motion), Is.EqualTo(new[] { "material._EmissionStrength_BodyMat" }));
            Assert.That(Model(menu).Messages.Where(m => m.Key.Contains("poiyomi") || m.Key == "ad.warn.property_missing"), Is.Empty,
                "the locked shader's own name for a renamed property is animated");
        }

        [Test]
        public void AnOriginalNameOnALockedMaterialFindsItsRenamedProperty()
        {
            var material = Poiyomi("Body Mat", locked: true);
            material.SetOverrideTag("_EmissionStrengthAnimated", "2");
            material.SetFloat("_EmissionStrength_BodyMat", 0.5f);
            avatar.Body.sharedMaterials = new[] { material };
            var menu = avatar.AddMenu();
            Toggle(menu, "Glow", "_EmissionStrength", 3f);

            Assert.That(Model(menu).Messages.Where(m => m.Key == "ad.warn.property_missing" || m.Key.Contains("poiyomi")), Is.Empty,
                "the locked material has the property under its renamed name");
            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            var off = State(fx, "AD: Glow", "Off").motion;
            Assert.That(MaterialCurves(State(fx, "AD: Glow", "On").motion), Does.Contain("material._EmissionStrength_BodyMat"));
            Assert.That(Value(off, "material._EmissionStrength_BodyMat"), Is.EqualTo(0.5f).Within(0.0001f), "the off value is read from the name the material has");
        }

        [Test]
        public void ANonPoiyomiMaterialIsLeftAlone()
        {
            avatar.MaterialA.SetOverrideTag("_GlossinessAnimated", "2");
            var menu = avatar.AddMenu();
            Toggle(menu, "Shine", "_Glossiness");

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            Assert.That(MaterialCurves(State(fx, "AD: Shine", "On").motion), Is.EqualTo(new[] { "material._Glossiness" }), "only a shader named like Poiyomi's is renamed");
            avatar.MaterialA.SetOverrideTag("_GlossinessAnimated", "");
            Assert.That(Model(menu).Messages.Where(m => m.Key.Contains("poiyomi")), Is.Empty, "and only one is warned about");
        }

        // ---- the warnings ---------------------------------------------------------------------------------------

        [Test]
        public void AnUnmarkedPropertyWarns()
        {
            var material = Poiyomi("Body Mat");
            avatar.Body.sharedMaterials = new[] { material, avatar.MaterialA };
            var menu = avatar.AddMenu();
            var glow = Toggle(menu, "Glow", "_EmissionStrength");

            var message = Model(menu).Messages.Single(m => m.Says("ad.warn.poiyomi_not_animated"));

            Assert.That(message.Key, Is.EqualTo("ad.warn.poiyomi_not_animated.one"));
            Assert.That(message.Severity, Is.EqualTo(MessageSeverity.Warning));
            Assert.That(message.EntryIds, Is.EqualTo(new[] { glow.id }));
            Assert.That(message.Args[1], Is.EqualTo("_EmissionStrength"));
            Assert.That(message.Args[2], Is.SameAs(avatar.Body), "the renderer, so the report can point at it");
            Assert.That(message.Args[4], Is.EqualTo("Body Mat"));
            Assert.That(UiText.Format(message), Does.Contain("\"Body Mat\"").And.Contain("\"_EmissionStrength\""));
        }

        [Test]
        public void TwoUnmarkedMaterialsShareOneWarning()
        {
            var first = Poiyomi("Body Mat");
            var second = Poiyomi("Skin");
            avatar.Body.sharedMaterials = new[] { first, second };
            var menu = avatar.AddMenu();
            Toggle(menu, "Glow", "_EmissionStrength");

            var message = Model(menu).Messages.Single(m => m.Says("ad.warn.poiyomi_not_animated"));

            Assert.That(message.Key, Is.EqualTo("ad.warn.poiyomi_not_animated"));
            Assert.That(message.Args[3], Is.EqualTo(2));
            Assert.That(message.Args[4], Is.EqualTo(L.JoinList(new[] { "Body Mat", "Skin" })));
        }

        [Test]
        public void APropertyPoiyomiNeverLocksIsNotWarnedAbout()
        {
            avatar.Body.sharedMaterials = new[] { Poiyomi("Body Mat") };
            var menu = avatar.AddMenu();
            Toggle(menu, "Cap", "_LightingCap");
            Toggle(menu, "Shine", "_ShineSpeed");
            Toggle(menu, "Glow", "_GlowAnimated");

            Assert.That(Model(menu).Messages.Where(m => m.Key.Contains("poiyomi")), Is.Empty,
                "DoNotLock, a display name ending in NL and a name ending in Animated are never fixed at their value");
        }

        [Test]
        public void ALockedMaterialThatFixedThePropertyWarnsWithoutAButton()
        {
            var material = Poiyomi("Body Mat", locked: true);
            avatar.Body.sharedMaterials = new[] { material };
            var menu = avatar.AddMenu();
            var tint = menu.AddEntry(EntryKind.Toggle, "Tint");
            tint.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_EmissionColor", type = PropertyValueType.Color, color = Color.red });
            var view = View(menu);

            var message = view.Plan.Messages.Single(m => m.Says("ad.warn.poiyomi_baked"));
            Assert.That(message.Key, Is.EqualTo("ad.warn.poiyomi_baked.one"));
            Assert.That(message.Severity, Is.EqualTo(MessageSeverity.Warning));
            Assert.That(message.EntryIds, Is.EqualTo(new[] { tint.id }));
            Assert.That(view.Plan.Messages.Any(m => m.Says("ad.warn.poiyomi_not_animated")), Is.False, "a locked material cannot be marked: it has to be unlocked first");

            view.Tree.SelectEntry(tint.id);
            Assert.That(view.Detail.Q<Button>("ad-poiyomi-mark"), Is.Null, "no button: unlock, mark and lock again by hand");
        }

        [Test]
        public void ARenameMarkedAfterTheLockStillWarns()
        {
            var material = Poiyomi("Body Mat", locked: true);
            material.SetOverrideTag("_GlowAnimated", "2");
            avatar.Body.sharedMaterials = new[] { material };
            var menu = avatar.AddMenu();
            Toggle(menu, "Glow", "_Glow");

            Assert.That(Model(menu).Messages.Select(m => m.Key), Does.Contain("ad.warn.poiyomi_baked.one"),
                "the locked shader has no renamed name yet, so the material was locked before the mark");
        }

        [Test]
        public void TheWarningPointsAtTheButton()
        {
            Assert.That(English("ad.warn.poiyomi_not_animated"), Does.Contain(English("ad.ui.poiyomi.mark_animated")));
            Assert.That(English("ad.warn.poiyomi_not_animated.one"), Does.Contain(English("ad.ui.poiyomi.mark_animated")));
        }

        [Test]
        public void MarkAsAnimatedSetsTheTagOnEveryMaterialInOneUndoStep()
        {
            var first = Poiyomi("Body Mat");
            var second = Poiyomi("Skin");
            var marked = Poiyomi("Marked");
            marked.SetOverrideTag("_EmissionStrengthAnimated", "2");
            avatar.Body.sharedMaterials = new[] { first, second, marked };
            var menu = avatar.AddMenu();
            var glow = Toggle(menu, "Glow", "_EmissionStrength");
            var view = View(menu);
            view.Tree.SelectEntry(glow.id);

            var button = view.Detail.Q<Button>("ad-poiyomi-mark");
            Assert.That(button, Is.Not.Null, "the warning box carries the way out");
            Assert.That(button.text, Is.EqualTo(L.Text("ad.ui.poiyomi.mark_animated")));
            Assert.That(button.tooltip, Is.EqualTo(L.Text("ad.ui.poiyomi.mark_animated.tip")));
            Assert.That(button.parent.ClassListContains("ad-message-actions"), Is.True, "in the row right under the warning");

            Undo.IncrementCurrentGroup();
            UiTestEvents.SimulateClick(button);

            Assert.That(first.GetTag("_EmissionStrengthAnimated", false, ""), Is.EqualTo("1"));
            Assert.That(second.GetTag("_EmissionStrengthAnimated", false, ""), Is.EqualTo("1"));
            Assert.That(marked.GetTag("_EmissionStrengthAnimated", false, ""), Is.EqualTo("2"), "a material that already marks it keeps its own mark");
            Assert.That(view.Plan.Messages.Any(m => m.Says("ad.warn.poiyomi_not_animated")), Is.False, "the warning is gone");
            Assert.That(view.Detail.Q<Button>("ad-poiyomi-mark"), Is.Null, "and so is its button");

            Undo.PerformUndo();

            Assert.That(first.GetTag("_EmissionStrengthAnimated", false, ""), Is.EqualTo(""), "one undo step takes both marks back");
            Assert.That(second.GetTag("_EmissionStrengthAnimated", false, ""), Is.EqualTo(""));
            Assert.That(marked.GetTag("_EmissionStrengthAnimated", false, ""), Is.EqualTo("2"));
        }

        [Test]
        public void ALockedComponentRefusesToMarkAsAnimated()
        {
            var material = Poiyomi("Body Mat");
            avatar.Body.sharedMaterials = new[] { material };
            var menu = avatar.AddMenu();
            var glow = Toggle(menu, "Glow", "_EmissionStrength");
            var view = View(menu);
            view.Tree.SelectEntry(glow.id);
            var button = view.Detail.Q<Button>("ad-poiyomi-mark");
            Assert.That(button, Is.Not.Null);
            menu.locked = true;

            UiTestEvents.SimulateClick(button);

            Assert.That(material.GetTag("_EmissionStrengthAnimated", false, ""), Is.EqualTo(""));
            Assert.That(EntryEditing.MarkAnimatedForPoiyomi(menu, glow, avatar.Body, "_EmissionStrength"), Is.False);
            Assert.That(material.GetTag("_EmissionStrengthAnimated", false, ""), Is.EqualTo(""));
            menu.locked = false;
            Assert.That(EntryEditing.MarkAnimatedForPoiyomi(menu, glow, avatar.Body, "_EmissionStrength"), Is.True, "unlocked, the same call marks it");
            Assert.That(material.GetTag("_EmissionStrengthAnimated", false, ""), Is.EqualTo("1"));
        }

        [Test]
        public void MarkAsAnimatedLeavesALockedMaterialAlone()
        {
            var locked = Poiyomi("Body Mat", locked: true);
            var plain = new Material(Shader.Find("Standard")) { name = "Plain" };
            made.Add(plain);

            Assert.That(PoiyomiLock.MarkAnimated(new[] { locked, plain }, "_EmissionColor"), Is.False, "a locked material ignores a new mark until it is locked again");
            Assert.That(locked.GetTag("_EmissionColorAnimated", false, ""), Is.EqualTo(""));
            Assert.That(plain.GetTag("_EmissionColorAnimated", false, ""), Is.EqualTo(""));
        }

        [Test]
        public void TheWarningAndItsButtonFitAt300And400InEveryLanguage()
        {
            foreach (var language in new[] { "en-US", "ja-JP", "zh-Hans" })
            foreach (float width in new[] { 300f, 400f })
            {
                using (L.UseTestLanguage(language, null))
                {
                    var material = Poiyomi("Body Mat");
                    avatar.Body.sharedMaterials = new[] { material };
                    var menu = avatar.AddMenu();
                    var glow = Toggle(menu, "Glow", "_EmissionStrength");
                    var view = View(menu);
                    view.Tree.SelectEntry(glow.id);
                    LayoutProbe.Measure(view, () =>
                    {
                        LayoutProbe.Validate(view);
                        var button = view.Detail.Q<Button>("ad-poiyomi-mark");
                        Assert.That(button, Is.Not.Null, language + " at " + width);
                        Assert.That(button.worldBound.xMax, Is.LessThanOrEqualTo(view.Detail.worldBound.xMax + 0.5f), language + " at " + width + ": the button stays inside the panel");
                        PanelAudit.Audit(view.Detail, view.Detail.worldBound.xMax, PanelAuditTests.StandardAllowList());
                    }, width);
                    Object.DestroyImmediate(menu);
                    AvatarPlanCache.Invalidate();
                }
            }
        }

        // ---- what the review of task 0 found -------------------------------------------------------------------

        /// <summary>The whole build the way NDMF runs it, AvatarDoc's own pass and then its final pass.</summary>
        AnimatorController BuildAll()
        {
            var fx = BuildAll(out var errors);
            Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error), Is.Empty);
            return fx;
        }

        /// <summary>The same, with every message of both passes.</summary>
        AnimatorController BuildAll(out List<ErrorContext> errors)
        {
            var context = new BuildContext(avatar.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            errors.AddRange(ErrorReport.CaptureErrors(() => AvatarDoctorPlugin.FinishAfterOtherTools(context)));
            return (AnimatorController)avatar.Descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
        }

        static IEnumerable<ModelMessage> PoiyomiOrMissing(MenuTree tree) => tree.Messages.Where(m => m.Key.Contains("poiyomi") || m.Key == "ad.warn.property_missing");

        [Test]
        public void ALockedMaterialKeepsTheSuffixItWasLockedWith()
        {
            // Duplicated with Ctrl+D after it was locked as "Body Mat": its name changed, its locked shader did not.
            var material = Poiyomi("Body Mat 1", locked: true);
            material.SetOverrideTag("thry_locked_rename_suffix", "BodyMat");
            material.SetOverrideTag("_EmissionStrengthAnimated", "2");
            avatar.Body.sharedMaterials = new[] { material };
            var menu = avatar.AddMenu();
            Toggle(menu, "Glow", "_EmissionStrength");
            Toggle(menu, "Picked", "_EmissionStrength_BodyMat");

            Assert.That(PoiyomiLock.Suffix(material), Is.EqualTo("BodyMat"));
            Assert.That(PoiyomiOrMissing(Model(menu)), Is.Empty, "the name picked from the locked shader is the renamed one: nothing is fixed, nothing is missing");
            var fx = BuildAnimator(out var errors, menu);
            Assert.That(errors, Is.Empty);
            Assert.That(MaterialCurves(State(fx, "AD: Glow", "On").motion), Is.EqualTo(new[] { "material._EmissionStrength", "material._EmissionStrength_BodyMat" }));
            Assert.That(MaterialCurves(State(fx, "AD: Picked", "On").motion), Is.EqualTo(new[] { "material._EmissionStrength_BodyMat" }));

            var unlocked = Poiyomi("Body Mat 1");
            unlocked.SetOverrideTag("thry_locked_rename_suffix", "Old");
            Assert.That(PoiyomiLock.Suffix(unlocked), Is.EqualTo("BodyMat1"), "an unlocked material is locked next with its own name");
        }

        [Test]
        public void ADuplicatedPropertyOnALockedMaterialIsReadAndPreviewedFromItsRenamedCopy()
        {
            var material = Poiyomi("Body Mat 1", locked: true);
            material.SetOverrideTag("thry_locked_rename_suffix", "BodyMat");
            material.SetOverrideTag("_EmissionColorAnimated", "2");
            material.SetColor("_EmissionColor", new Color(0.1f, 0.1f, 0.1f, 1f));
            material.SetColor("_EmissionColor_BodyMat", new Color(0.6f, 0.5f, 0.4f, 1f));
            avatar.Body.sharedMaterials = new[] { material };
            var menu = avatar.AddMenu();
            var tint = menu.AddEntry(EntryKind.Toggle, "Tint");
            tint.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_EmissionColor", type = PropertyValueType.Color, color = Color.red });

            Assert.That(PoiyomiOrMissing(Model(menu)), Is.Empty);
            var fx = BuildAnimator(out var errors, menu);
            Assert.That(errors, Is.Empty);
            var off = State(fx, "AD: Tint", "Off").motion;
            Assert.That(MaterialCurves(off), Does.Contain("material._EmissionColor_BodyMat.r"));
            Assert.That(Value(off, "material._EmissionColor_BodyMat.r"), Is.EqualTo(0.6f).Within(0.0001f), "the off value is the copy the locked shader uses, not the old one beside it");

            var states = PreviewModel.Compute(avatar.Root.transform, new[] { menu }, new Dictionary<string, float> { [tint.id] = 1f }, SceneReader.Direct);
            var proxyObject = Object.Instantiate(avatar.Body.gameObject);
            var node = new AvatarDoctorPreview.Node(states[avatar.Body]);
            try
            {
                node.OnFrame(avatar.Body, proxyObject.GetComponent<SkinnedMeshRenderer>());
                var shown = proxyObject.GetComponent<SkinnedMeshRenderer>().sharedMaterials[0];
                Assert.That(shown.GetColor("_EmissionColor_BodyMat").r, Is.EqualTo(1f).Within(0.0001f), "the preview writes the copy the locked shader draws with");
            }
            finally
            {
                node.Dispose();
                Object.DestroyImmediate(proxyObject);
            }
        }

        [Test]
        public void AVariantIsLockedThroughItsRoot()
        {
            var root = Poiyomi("Body Mat");
            root.SetOverrideTag("_EmissionStrengthAnimated", "2");
            var variant = Poiyomi("Body Mat Red");
            variant.parent = root;
            Assert.That(variant.parent, Is.SameAs(root), "precondition: Unity made it a variant");
            avatar.Body.sharedMaterials = new[] { variant };
            var menu = avatar.AddMenu();
            Toggle(menu, "Glow", "_EmissionStrength");

            Assert.That(PoiyomiLock.Suffix(variant), Is.EqualTo("BodyMat"), "Poiyomi locks the root, with the root's name");
            Assert.That(PoiyomiOrMissing(Model(menu)), Is.Empty, "the root marks it");
            var fx = BuildAnimator(out var errors, menu);
            Assert.That(errors, Is.Empty);
            Assert.That(MaterialCurves(State(fx, "AD: Glow", "On").motion), Is.EqualTo(new[] { "material._EmissionStrength", "material._EmissionStrength_BodyMat" }));
        }

        [Test]
        public void MarkAsAnimatedMarksTheRootOfAVariant()
        {
            var root = Poiyomi("Body Mat");
            var variant = Poiyomi("Body Mat Red");
            variant.parent = root;
            avatar.Body.sharedMaterials = new[] { variant };
            var menu = avatar.AddMenu();
            var glow = Toggle(menu, "Glow", "_EmissionStrength");

            var message = Model(menu).Messages.Single(m => m.Says("ad.warn.poiyomi_not_animated"));
            Assert.That(message.Args[4], Is.EqualTo("Body Mat"), "the warning names the material that is locked: the root");

            Undo.IncrementCurrentGroup();
            Assert.That(EntryEditing.MarkAnimatedForPoiyomi(menu, glow, avatar.Body, "_EmissionStrength"), Is.True);
            Assert.That(root.GetTag("_EmissionStrengthAnimated", false, ""), Is.EqualTo("1"), "the root's mark is the one its lock reads");
            Assert.That(PoiyomiOrMissing(Model(menu)), Is.Empty);

            Undo.PerformUndo();
            Assert.That(root.GetTag("_EmissionStrengthAnimated", false, ""), Is.EqualTo(""), "one undo step");
        }

        [Test]
        public void AMaterialAnotherToggleSwapsInGetsItsNameAndItsWarning()
        {
            var skin = Poiyomi("Skin");
            skin.SetOverrideTag("_EmissionStrengthAnimated", "2");
            var skinAlt = Poiyomi("Skin Alt");
            skinAlt.SetOverrideTag("_EmissionStrengthAnimated", "2");
            avatar.Body.sharedMaterials = new[] { skin };
            var menu = avatar.AddMenu();
            var swap = menu.AddEntry(EntryKind.Toggle, "Alt skin");
            swap.materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 0, material = skinAlt });
            Toggle(menu, "Glow", "_EmissionStrength");

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            Assert.That(MaterialCurves(State(fx, "AD: Glow", "On").motion),
                Is.EqualTo(new[] { "material._EmissionStrength", "material._EmissionStrength_Skin", "material._EmissionStrength_SkinAlt" }),
                "Poiyomi also locks the material the other toggle swaps in, under its own name");

            skinAlt.SetOverrideTag("_EmissionStrengthAnimated", "");
            var warning = Model(menu).Messages.Single(m => m.Says("ad.warn.poiyomi_not_animated"));
            Assert.That(warning.Args[4], Is.EqualTo("Skin Alt"));
        }

        [Test]
        public void ANamePickedFromALockedMaterialAlsoWritesTheOriginalOnceUnlocked()
        {
            var material = Poiyomi("Body Mat");
            material.SetOverrideTag("_EmissionStrengthAnimated", "2");
            avatar.Body.sharedMaterials = new[] { material };
            var menu = avatar.AddMenu();
            Toggle(menu, "Glow", "_EmissionStrength_BodyMat");

            Assert.That(PoiyomiOrMissing(Model(menu)), Is.Empty);
            var fx = BuildAnimator(out var errors, menu);
            Assert.That(errors, Is.Empty);
            Assert.That(MaterialCurves(State(fx, "AD: Glow", "On").motion), Is.EqualTo(new[] { "material._EmissionStrength", "material._EmissionStrength_BodyMat" }),
                "the original works in Play mode while the material is unlocked, the stored name once Poiyomi locked it");
        }

        [Test]
        public void APoiyomiMaterialTheSameToggleSwapsInIsRenamedToo()
        {
            var glowMat = Poiyomi("Glow Mat");
            glowMat.SetOverrideTag("_EmissionStrengthAnimated", "2");
            var menu = avatar.AddMenu();
            var dissolve = Toggle(menu, "Glow", "_EmissionStrength");
            dissolve.materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 0, material = glowMat });

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            Assert.That(MaterialCurves(State(fx, "AD: Glow", "On").motion), Is.EqualTo(new[] { "material._EmissionStrength", "material._EmissionStrength_GlowMat" }));
        }

        [Test]
        public void ABlendShapeTriggerWritesAndRestsBothNames()
        {
            var material = Poiyomi("Body Mat");
            material.SetOverrideTag("_EmissionStrengthAnimated", "2");
            material.SetFloat("_EmissionStrength", 0.25f);
            avatar.Body.sharedMaterials = new[] { material };
            avatar.AddFxClip("Smile", TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f));
            var menu = avatar.AddMenu();
            var link = menu.AddEntry(EntryKind.Trigger, "Blush link");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            link.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_EmissionStrength", type = PropertyValueType.Float, value = 2f });

            GeneratedAssets.Note();
            try
            {
                var fx = BuildAll();
                var layer = fx.layers.Single(l => l.name == "AD: Blush link (shape)");
                var both = new[] { "material._EmissionStrength", "material._EmissionStrength_BodyMat" };
                var tree = (BlendTree)layer.stateMachine.states.Single(s => s.state.name == "Near").state.motion;
                Assert.That(MaterialCurves(tree.children[1].motion), Is.EqualTo(both));
                Assert.That(Value(tree.children[1].motion, "material._EmissionStrength_BodyMat"), Is.EqualTo(2f).Within(0.0001f));
                var reset = layer.stateMachine.states.Single(s => s.state.name == "Reset").state.motion;
                Assert.That(MaterialCurves(reset), Is.EqualTo(both));
                Assert.That(Value(reset, "material._EmissionStrength_BodyMat"), Is.EqualTo(0.25f).Within(0.0001f));
            }
            finally
            {
                GeneratedAssets.CleanUp();
            }
        }

        [Test]
        public void ARadialWithACustomClipRampsBothNames()
        {
            var material = Poiyomi("Body Mat");
            material.SetOverrideTag("_EmissionStrengthAnimated", "2");
            avatar.Body.sharedMaterials = new[] { material };
            var clip = new AnimationClip { name = "Spin" };
            made.Add(clip);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Hat", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0f, 1f, 1f));
            var menu = avatar.AddMenu();
            var dial = menu.AddEntry(EntryKind.Radial, "Dial");
            dial.animations.Add(new AnimationTarget { clip = clip });
            dial.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_EmissionStrength", type = PropertyValueType.Float, minValue = 0f, value = 4f });

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            var motion = (AnimationClip)fx.layers.Single(l => l.name == "AD: Dial").stateMachine.states.Single().state.motion;
            Assert.That(MaterialCurves(motion), Is.EqualTo(new[] { "material._EmissionStrength", "material._EmissionStrength_BodyMat" }));
            var ramp = AnimationUtility.GetEditorCurve(motion, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "material._EmissionStrength_BodyMat"));
            Assert.That(ramp.Evaluate(ramp.keys[ramp.length - 1].time), Is.EqualTo(4f).Within(0.0001f));
        }

        [Test]
        public void ATexturesTilingIsRenamedThroughTheBuild()
        {
            var material = Poiyomi("Body Mat");
            material.SetOverrideTag("_MainTexAnimated", "2");
            avatar.Body.sharedMaterials = new[] { material };
            Assert.That(material.HasProperty("_MainTex_ST"), Is.True, "precondition: the material reports the texture's tiling");
            var menu = avatar.AddMenu();
            var tile = menu.AddEntry(EntryKind.Toggle, "Tile");
            tile.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_MainTex_ST", type = PropertyValueType.Vector, vector = new Vector4(2f, 2f, 0f, 0f) });

            Assert.That(PoiyomiOrMissing(Model(menu)), Is.Empty, "the texture marks its tiling");
            var fx = BuildAnimator(out var errors, menu);
            Assert.That(errors, Is.Empty);
            Assert.That(MaterialCurves(State(fx, "AD: Tile", "On").motion), Is.EqualTo(new[] { "x", "y", "z", "w" }
                .SelectMany(c => new[] { "material._MainTex_ST." + c, "material._MainTex_BodyMat_ST." + c }).OrderBy(n => n, StringComparer.Ordinal).ToList()));
        }

        [Test]
        public void ADoNotLockTexturesTilingIsNotWarnedAbout()
        {
            var material = Poiyomi("Body Mat");
            avatar.Body.sharedMaterials = new[] { material };
            Assert.That(material.HasProperty("_DetailTex_ST"), Is.True, "precondition: the material reports the texture's tiling");
            var menu = avatar.AddMenu();
            var tile = menu.AddEntry(EntryKind.Toggle, "Tile");
            tile.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_DetailTex_ST", type = PropertyValueType.Vector, vector = new Vector4(2f, 2f, 0f, 0f) });

            Assert.That(Model(menu).Messages.Where(m => m.Key.Contains("poiyomi")), Is.Empty, "the texture is never locked, so neither is its tiling");
        }

        [Test]
        public void ARowOfAnotherComponentShowsNoMarkButton()
        {
            avatar.Body.sharedMaterials = new[] { Poiyomi("Body Mat") };
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Mine").objects.Add(new ObjectTarget { target = avatar.Hat });
            var pack = new GameObject("Pack");
            pack.transform.SetParent(avatar.Root.transform);
            var packMenu = pack.AddComponent<AvatarDoc>();
            var theirs = packMenu.AddEntry(EntryKind.Toggle, "Theirs");
            theirs.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_EmissionStrength", type = PropertyValueType.Float, value = 1f });
            var view = View(menu);
            Assert.That(view.Plan.Messages.Any(m => m.Says("ad.warn.poiyomi_not_animated") && m.EntryIds.Contains(theirs.id)), Is.True, "precondition: the warning is there");

            view.Tree.SelectEntry(theirs.id);

            Assert.That(view.Detail.Q<Button>("ad-poiyomi-mark"), Is.Null, "the entry belongs to another component: it is marked from that component's own inspector");
        }

        [Test]
        public void EachRenamedNameRestsAtItsOwnMaterialsValue()
        {
            var skin = Poiyomi("Skin");
            skin.SetOverrideTag("_EmissionStrengthAnimated", "2");
            skin.SetFloat("_EmissionStrength", 0.2f);
            var cloth = Poiyomi("Cloth");
            cloth.SetOverrideTag("_EmissionStrengthAnimated", "2");
            cloth.SetFloat("_EmissionStrength", 0.7f);
            avatar.Body.sharedMaterials = new[] { skin, cloth };
            var menu = avatar.AddMenu();
            Toggle(menu, "Glow", "_EmissionStrength", 3f);
            var fixedOff = Toggle(menu, "Fixed off", "_EmissionStrength", 3f);
            fixedOff.properties[0].explicitOff = true;
            fixedOff.properties[0].minValue = 0.1f;

            var fx = BuildAnimator(out _, menu);

            var off = State(fx, "AD: Glow", "Off").motion;
            Assert.That(Value(off, "material._EmissionStrength_Skin"), Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(Value(off, "material._EmissionStrength_Cloth"), Is.EqualTo(0.7f).Within(0.0001f), "renamed, each material keeps its own value while off");
            Assert.That(Value(off, "material._EmissionStrength"), Is.EqualTo(0.2f).Within(0.0001f), "the shared name still rests at the first material's value");
            var explicitOff = State(fx, "AD: Fixed off", "Off").motion;
            Assert.That(Value(explicitOff, "material._EmissionStrength_Cloth"), Is.EqualTo(0.1f).Within(0.0001f), "an off value of the row's own is the same everywhere");
        }

        [Test]
        public void AnOriginalAndARenamedNameAreTheSameTarget()
        {
            var material = Poiyomi("Body Mat", locked: true);
            material.SetOverrideTag("_EmissionStrengthAnimated", "2");
            avatar.Body.sharedMaterials = new[] { material };
            var menu = avatar.AddMenu();
            var a = Toggle(menu, "A", "_EmissionStrength");
            var b = Toggle(menu, "B", "_EmissionStrength_BodyMat");

            var message = Model(menu).Messages.Single(m => m.Key == "ad.info.target_shared");

            Assert.That(message.EntryIds, Is.EquivalentTo(new[] { a.id, b.id }), "both move the property Poiyomi animates as _EmissionStrength_BodyMat");
        }

        // ---- what the second review found ---------------------------------------------------------------------

        const string LockedClothSource = @"Shader ""Hidden/Locked/.poiyomi/AvatarDocTests/Poiyomi Toon/4e5f6a7b""
{
    Properties
    {
        _EmissionStrength_Cloth (""Emission Strength"", Float) = 0
    }
    SubShader { Pass { } }
}";

        [Test]
        public void ASwapThatIsOnByDefaultStillNamesTheMaterialItReplaces()
        {
            var skin = Poiyomi("Skin");
            skin.SetOverrideTag("_EmissionStrengthAnimated", "2");
            var skinAlt = Poiyomi("Skin Alt");
            skinAlt.SetOverrideTag("_EmissionStrengthAnimated", "2");
            avatar.Body.sharedMaterials = new[] { skin };
            var menu = avatar.AddMenu();
            var swap = menu.AddEntry(EntryKind.Toggle, "Alt skin");
            swap.defaultOn = true;
            swap.materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 0, material = skinAlt });
            Toggle(menu, "Glow", "_EmissionStrength");

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            Assert.That(avatar.Body.sharedMaterials[0], Is.SameAs(skinAlt), "precondition: the build put the default swap on the mesh");
            Assert.That(MaterialCurves(State(fx, "AD: Glow", "On").motion),
                Is.EqualTo(new[] { "material._EmissionStrength", "material._EmissionStrength_Skin", "material._EmissionStrength_SkinAlt" }),
                "the swap's Off state puts Skin back, and Poiyomi locks Skin too");
        }

        [Test]
        public void ANamePickedFromALockedMaterialLeavesTheOriginalToAMaterialThatAnimatesIt()
        {
            var bodyMat = Poiyomi("Body Mat");
            bodyMat.SetOverrideTag("_EmissionStrengthAnimated", "2");
            var skin = Poiyomi("Skin");
            skin.SetOverrideTag("_EmissionStrengthAnimated", "1");
            avatar.Body.sharedMaterials = new[] { bodyMat, skin };
            var menu = avatar.AddMenu();
            Toggle(menu, "Glow", "_EmissionStrength_BodyMat");

            var fx = BuildAnimator(out var errors, menu);

            Assert.That(errors, Is.Empty);
            Assert.That(MaterialCurves(State(fx, "AD: Glow", "On").motion), Is.EqualTo(new[] { "material._EmissionStrength_BodyMat" }),
                "Skin animates _EmissionStrength under that name in game too, and the row was picked for Body Mat alone");
        }

        [Test]
        public void ASwapThatIsNeverBuiltNamesNoMaterial()
        {
            var skin = Poiyomi("Skin");
            skin.SetOverrideTag("_EmissionStrengthAnimated", "2");
            var ghostA = Poiyomi("Ghost A");
            var ghostB = Poiyomi("Ghost B");
            ghostB.SetOverrideTag("_EmissionStrengthAnimated", "2");
            avatar.Body.sharedMaterials = new[] { skin };
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var flip = menu.AddEntry(EntryKind.Trigger, "Flip");
            flip.source = TriggerSource.Contact;
            flip.contactAnchor = avatar.Hat.transform;
            flip.touchMode = TouchMode.FlipToggle;
            flip.flipToggleId = hat.id;
            flip.materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 0, material = ghostA });
            var badSlot = menu.AddEntry(EntryKind.Toggle, "Bad slot");
            badSlot.materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 5, material = ghostB });
            Toggle(menu, "Glow", "_EmissionStrength");
            Assert.That(flip.IsFlipTrigger, Is.True, "precondition");

            Assert.That(Model(menu).Messages.Where(m => m.Key.Contains("poiyomi")), Is.Empty, "a flip trigger builds no swap, and slot 5 does not exist");
            var fx = BuildAnimator(out _, menu);
            Assert.That(MaterialCurves(State(fx, "AD: Glow", "On").motion), Is.EqualTo(new[] { "material._EmissionStrength", "material._EmissionStrength_Skin" }));
        }

        [Test]
        public void MaterialsThatRenameThePropertyKeepTheirOwnValueAndDoNotDisagree()
        {
            var skin = Poiyomi("Skin");
            skin.SetOverrideTag("_EmissionStrengthAnimated", "2");
            skin.SetFloat("_EmissionStrength", 0.2f);
            var cloth = Poiyomi("Cloth");
            cloth.SetOverrideTag("_EmissionStrengthAnimated", "2");
            cloth.SetFloat("_EmissionStrength", 0.7f);
            avatar.Body.sharedMaterials = new[] { skin, cloth };
            var menu = avatar.AddMenu();
            Toggle(menu, "Glow", "_EmissionStrength");

            Assert.That(Model(menu).Messages.Any(m => m.Key == "ad.warn.property_differs"), Is.False, "in game each keeps its own value while the toggle is off");

            cloth.SetOverrideTag("_EmissionStrengthAnimated", "1");
            Assert.That(Model(menu).Messages.Any(m => m.Key == "ad.warn.property_differs"), Is.True, "Cloth now takes the first material's value under the shared name");
        }

        [Test]
        public void TwoRenamedNamesOfTwoMaterialsAreTwoTargets()
        {
            var clothShader = ShaderUtil.CreateShaderAsset(LockedClothSource, false);
            made.Add(clothShader);
            var bodyMat = Poiyomi("Body Mat", locked: true);
            bodyMat.SetOverrideTag("_EmissionStrengthAnimated", "2");
            var cloth = new Material(clothShader) { name = "Cloth" };
            made.Add(cloth);
            cloth.SetOverrideTag("_EmissionStrengthAnimated", "2");
            avatar.Body.sharedMaterials = new[] { bodyMat, cloth };
            var menu = avatar.AddMenu();
            Toggle(menu, "Body glow", "_EmissionStrength_BodyMat");
            Toggle(menu, "Cloth glow", "_EmissionStrength_Cloth");
            var all = Toggle(menu, "All glow", "_EmissionStrength");

            var shared = Model(menu).Messages.Where(m => m.Key == "ad.info.target_shared").ToList();

            Assert.That(shared.Count, Is.EqualTo(2), "each picked name shares its property with the row on the original name, and with nothing else");
            Assert.That(shared.All(m => m.EntryIds.Contains(all.id) && m.EntryIds.Count == 2), Is.True);
        }

        [Test]
        public void ABlendShapeTriggerNamesAClipThatMovesARenamedColour()
        {
            var material = Poiyomi("Body Mat");
            material.SetOverrideTag("_EmissionColorAnimated", "2");
            avatar.Body.sharedMaterials = new[] { material };
            var smile = TestAvatar.ShapeClip("Smile", "Body", "Blush", 0f, 100f);
            made.Add(smile);
            AnimationUtility.SetEditorCurve(smile, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "material._EmissionColor_BodyMat.r"), AnimationCurve.Constant(0f, 1f, 0.5f));
            avatar.AddFxClip("Smile", smile);
            var menu = avatar.AddMenu();
            var link = menu.AddEntry(EntryKind.Trigger, "Blush link");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Blush";
            link.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_EmissionColor", type = PropertyValueType.Color, color = Color.red });

            GeneratedAssets.Note();
            try
            {
                BuildAll(out var errors);
                Assert.That(errors.Any(e => e.TheError is SimpleError s && s.TitleKey == "ad.info.curve_overwritten"), Is.True,
                    "the clip moves the red of the name Poiyomi gives the colour once locked, which the trigger also writes");
            }
            finally
            {
                GeneratedAssets.CleanUp();
            }
        }

        // ---- the preview ----------------------------------------------------------------------------------------

        [Test]
        public void ThePreviewWritesTheNameTheMaterialHas()
        {
            var locked = Poiyomi("Body Mat", locked: true);
            locked.SetOverrideTag("_EmissionStrengthAnimated", "2");
            var unlocked = Poiyomi("Body Mat");
            unlocked.SetOverrideTag("_EmissionStrengthAnimated", "2");
            avatar.Body.sharedMaterials = new[] { locked, unlocked };
            var menu = avatar.AddMenu();
            var glow = Toggle(menu, "Glow", "_EmissionStrength", 3f);

            var states = PreviewModel.Compute(avatar.Root.transform, new[] { menu }, new Dictionary<string, float> { [glow.id] = 1f }, SceneReader.Direct);
            Assert.That(states.TryGetValue(avatar.Body, out var state) && state.Properties.ContainsKey("_EmissionStrength"), Is.True, "the preview shows the property though the locked material renamed it");

            var proxyObject = Object.Instantiate(avatar.Body.gameObject);
            var proxy = proxyObject.GetComponent<SkinnedMeshRenderer>();
            var node = new AvatarDoctorPreview.Node(state);
            try
            {
                node.OnFrame(avatar.Body, proxy);
                Assert.That(proxy.sharedMaterials[0].GetFloat("_EmissionStrength_BodyMat"), Is.EqualTo(3f).Within(0.0001f), "the locked material's copy gets its renamed name");
                Assert.That(proxy.sharedMaterials[1].GetFloat("_EmissionStrength"), Is.EqualTo(3f).Within(0.0001f), "the unlocked one keeps its own name");
                Assert.That(locked.GetFloat("_EmissionStrength_BodyMat"), Is.EqualTo(0f), "the real material is never written");

                // The next frame, as NDMF hands it over: the proxy holds the original materials again.
                var copy = proxy.sharedMaterials[0];
                copy.SetFloat("_EmissionStrength_BodyMat", 0f);
                proxy.sharedMaterials = new[] { locked, unlocked };
                node.OnFrame(avatar.Body, proxy);
                Assert.That(proxy.sharedMaterials[0], Is.SameAs(copy), "the copies are kept while the materials stay the same");
                Assert.That(copy.GetFloat("_EmissionStrength_BodyMat"), Is.EqualTo(3f).Within(0.0001f), "every frame writes the names the copies were made with");
            }
            finally
            {
                node.Dispose();
                Object.DestroyImmediate(proxyObject);
            }
        }
    }
}
#endif
