#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDKBase;
using Object = UnityEngine.Object;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Every kind of list row in a real inspector, attached to a shown window, at both ends and the middle of the
    /// default inspector width (300, 360 and 400 px), in English, Japanese and Simplified Chinese (in NDMF's font
    /// where NDMF has one), with long names a creator really has: a renderer named Body_Outfit_Long, a shape named
    /// Mouth_Smile_Wide_Open used by an FX clip, and shader properties such as "Emission Strength (HDR)". PanelAudit
    /// draws the lists' rows before it measures, so a slider squeezed to its knob or a remove button over a field
    /// fails here.
    /// </summary>
    public class ListRowAuditTests
    {
        static readonly float[] Widths = { 300f, 360f, 400f };
        static readonly string[] Languages = { "en-US", "ja-JP", "zh-Hans" };

        const string LongNamesShader = @"Shader ""Hidden/AvatarDocTests/LongPropertyNames""
{
    Properties
    {
        _EmissionStrength (""Emission Strength (HDR)"", Float) = 1
        _RimStrength (""Rim Light Strength (Range)"", Range(0, 10)) = 1
        [HDR] _EmissionColor (""Emission Colour (HDR)"", Color) = (1, 1, 1, 1)
        _ScrollSpeed (""Emission Scroll Speed (XYZW)"", Vector) = (0, 0, 0, 0)
    }
    SubShader { Pass { } }
}";

        TestAvatar avatar;
        SkinnedMeshRenderer outfit;
        Mesh outfitMesh;
        Shader shader;
        Material outfitMaterial;
        AnimationClip smileClip;
        AnimationClip danceClip;

        [SetUp]
        public void SetUp()
        {
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
            avatar = TestAvatar.Create();
            var outfitObject = new GameObject("Body_Outfit_Long");
            outfitObject.transform.SetParent(avatar.Root.transform);
            outfit = outfitObject.AddComponent<SkinnedMeshRenderer>();
            outfitMesh = TestAvatar.QuadWithShapes("Mouth_Smile_Wide_Open", "Blush", "Blink", "vrc.v_aa");
            outfit.sharedMesh = outfitMesh;
            shader = ShaderUtil.CreateShaderAsset(LongNamesShader, false);
            outfitMaterial = new Material(shader) { name = "Outfit_Material_Long" };
            outfit.sharedMaterials = new[] { outfitMaterial, avatar.MaterialA };

            var descriptor = avatar.Descriptor;
            descriptor.enableEyeLook = true;
            descriptor.customEyeLookSettings.eyelidType = VRCAvatarDescriptor.EyelidType.Blendshapes;
            descriptor.customEyeLookSettings.eyelidsSkinnedMesh = outfit;
            descriptor.customEyeLookSettings.eyelidsBlendshapes = new[] { outfitMesh.GetBlendShapeIndex("Blink"), -1, -1 };
            descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape;
            descriptor.VisemeSkinnedMesh = outfit;
            descriptor.VisemeBlendShapes = new string[(int)VRC_AvatarDescriptor.Viseme.Count];
            descriptor.VisemeBlendShapes[1] = "vrc.v_aa";

            smileClip = TestAvatar.ShapeClip("Smile_Clip", "Body_Outfit_Long", "Mouth_Smile_Wide_Open", 0f, 100f);
            avatar.AddFxClip("Smile", smileClip);
            danceClip = new AnimationClip { name = "Outfit_Long_Animation" };
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            Object.DestroyImmediate(outfitMaterial);
            Object.DestroyImmediate(shader);
            Object.DestroyImmediate(outfitMesh);
            Object.DestroyImmediate(smileClip);
            Object.DestroyImmediate(danceClip);
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        BlendShapeTarget Shape(string shape, float weight = 100f, ShapeEffectMode mode = ShapeEffectMode.Fixed, bool explicitOff = false) =>
            new BlendShapeTarget { renderer = outfit, shape = shape, weight = weight, minWeight = explicitOff ? 20f : 0f, maxWeight = 100f, mode = mode, explicitOff = explicitOff };

        MaterialPropertyTarget Property(string property, PropertyValueType type, bool explicitOff = false) => new MaterialPropertyTarget
        {
            renderer = outfit, property = property, type = type, value = 0.5f, minValue = 0.25f,
            color = Color.white, minColor = Color.black, vector = new Vector4(1f, 2f, 3f, 4f), minVector = Vector4.zero, explicitOff = explicitOff,
        };

        MenuEntry Trigger(AvatarDoc menu, string name, TriggerSource source)
        {
            var entry = menu.AddEntry(EntryKind.Trigger, name);
            entry.source = source;
            return entry;
        }

        MenuEntry Contact(AvatarDoc menu, string name, TouchMode mode)
        {
            var entry = Trigger(menu, name, TriggerSource.Contact);
            entry.contactAnchor = avatar.Root.transform;
            entry.touchMode = mode;
            return entry;
        }

        [Test]
        public void ObjectRowsFit()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Outfit");
            toggle.objects.Add(new ObjectTarget { target = outfit.gameObject, activeWhenOn = true });
            toggle.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = false });

            AuditEverywhere(menu, toggle);
        }

        [Test]
        public void BlendShapeRowsFitInEveryMode()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Smile");
            toggle.blendShapes.Add(Shape("Mouth_Smile_Wide_Open"));
            toggle.blendShapes.Add(Shape("Mouth_Smile_Wide_Open", explicitOff: true));
            toggle.blendShapes.Add(Shape("Mouth_Smile_Wide_Open", mode: ShapeEffectMode.Pin));
            var radial = menu.AddEntry(EntryKind.Radial, "Smile amount");
            radial.blendShapes.Add(Shape("Mouth_Smile_Wide_Open"));
            var link = Trigger(menu, "Smile link", TriggerSource.BlendShape);
            link.sourceRenderer = outfit;
            link.sourceShape = "Blush";
            link.blendShapes.Add(Shape("Mouth_Smile_Wide_Open"));
            link.blendShapes.Add(Shape("Mouth_Smile_Wide_Open", mode: ShapeEffectMode.Follow));
            var fade = Contact(menu, "Smile fade", TouchMode.FollowDistance);
            fade.blendShapes.Add(Shape("Mouth_Smile_Wide_Open"));
            Assert.That(fade.IsFollowDistanceTrigger, Is.True, "the fixture reaches the Fade rows");
            var over = menu.AddEntry(EntryKind.Override, "Silence");
            over.blendShapes.Add(Shape("Mouth_Smile_Wide_Open", weight: 0f));

            AuditEverywhere(menu, toggle, radial, link, fade, over);
        }

        [Test]
        public void PropertyRowsFitForEveryType()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Glow");
            foreach (var explicitOff in new[] { false, true })
            {
                toggle.properties.Add(Property("_EmissionStrength", PropertyValueType.Float, explicitOff));
                toggle.properties.Add(Property("_RimStrength", PropertyValueType.Float, explicitOff));
                toggle.properties.Add(Property("_EmissionColor", PropertyValueType.Color, explicitOff));
                toggle.properties.Add(Property("_ScrollSpeed", PropertyValueType.Vector, explicitOff));
            }
            toggle.properties.Add(Property("_TypedByHand", PropertyValueType.Float));
            toggle.properties.Add(new MaterialPropertyTarget());
            var radial = menu.AddEntry(EntryKind.Radial, "Glow amount");
            radial.properties.Add(Property("_EmissionStrength", PropertyValueType.Float));
            radial.properties.Add(Property("_RimStrength", PropertyValueType.Float));
            radial.properties.Add(Property("_EmissionColor", PropertyValueType.Color));
            radial.properties.Add(Property("_ScrollSpeed", PropertyValueType.Vector));

            AuditEverywhere(menu, toggle, radial);
        }

        [Test]
        public void MaterialSlotRowsFit()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Swap");
            toggle.materials.Add(new MaterialTarget { renderer = outfit, slot = 0, material = outfitMaterial });
            toggle.materials.Add(new MaterialTarget { renderer = outfit, slot = 1, material = null });

            AuditEverywhere(menu, toggle);
        }

        [Test]
        public void ShapeSetRuleRowsFit()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Face");
            toggle.blendShapes.Add(Shape("Mouth_Smile_Wide_Open", mode: ShapeEffectMode.Pin));
            toggle.shapeSets.Add(new ShapeSetRule { mesh = outfit, zeroOthers = true, value = ShapeSetValue.Default, holdBlink = true, holdVisemes = true });
            toggle.shapeSets.Add(new ShapeSetRule { mesh = avatar.Body, zeroOthers = true, value = ShapeSetValue.Zero });
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var pout = Trigger(menu, "Pout", TriggerSource.Toggle);
            pout.sourceToggleId = hat.id;
            pout.blendShapes.Add(Shape("Mouth_Smile_Wide_Open", mode: ShapeEffectMode.Pin));
            pout.shapeSets.Add(new ShapeSetRule { mesh = outfit, zeroOthers = true, value = ShapeSetValue.Zero, holdBlink = true, holdVisemes = true });

            AuditEverywhere(menu, toggle, pout);
        }

        [Test]
        public void TriggerAndContactRowsFit()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var pout = Trigger(menu, "Pout", TriggerSource.Toggle);
            pout.sourceToggleId = hat.id;
            pout.objects.Add(new ObjectTarget { target = outfit.gameObject });
            pout.blendShapes.Add(Shape("Mouth_Smile_Wide_Open"));
            pout.blendShapes.Add(Shape("Mouth_Smile_Wide_Open", explicitOff: true));
            pout.materials.Add(new MaterialTarget { renderer = outfit, slot = 0, material = outfitMaterial });
            pout.properties.Add(Property("_RimStrength", PropertyValueType.Float, explicitOff: true));
            pout.follows.Add(new FollowTarget { target = outfit.gameObject, follows = avatar.Hat.transform, keepOffset = true });
            var poke = Contact(menu, "Poke", TouchMode.Hold);
            poke.blendShapes.Add(Shape("Mouth_Smile_Wide_Open", mode: ShapeEffectMode.Pin));
            poke.properties.Add(Property("_EmissionColor", PropertyValueType.Color));
            var headpat = Contact(menu, "Headpat", TouchMode.FollowDistance);
            headpat.blendShapes.Add(Shape("Mouth_Smile_Wide_Open"));
            headpat.properties.Add(Property("_ScrollSpeed", PropertyValueType.Vector));
            var squeeze = Trigger(menu, "Squeeze", TriggerSource.PhysBone);
            squeeze.physBone = avatar.Hat.AddComponent<VRCPhysBone>();
            squeeze.objects.Add(new ObjectTarget { target = outfit.gameObject, activeWhenOn = false });
            squeeze.blendShapes.Add(Shape("Mouth_Smile_Wide_Open"));

            AuditEverywhere(menu, pout, poke, headpat, squeeze);
        }

        [Test]
        public void AnimationAndFollowRowsFit()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Dance");
            toggle.animations.Add(new AnimationTarget { clip = danceClip, pathRoot = outfit.transform });
            toggle.follows.Add(new FollowTarget { target = outfit.gameObject, follows = avatar.Hat.transform, keepOffset = true });

            AuditEverywhere(menu, toggle);
        }

        /// <summary>
        /// Each value in a property row says which it is: a toggle's on and off values, a radial's Min and Max (the
        /// same words a blend shape radial uses), every caption in the label look, a Vector's included.
        /// </summary>
        [Test]
        public void PropertyValuesCarryTheirCaptions()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Glow");
            var radial = menu.AddEntry(EntryKind.Radial, "Glow amount");
            foreach (var (property, type) in new[] { ("_EmissionStrength", PropertyValueType.Float), ("_RimStrength", PropertyValueType.Float), ("_EmissionColor", PropertyValueType.Color), ("_ScrollSpeed", PropertyValueType.Vector) })
            {
                toggle.properties.Add(Property(property, type, explicitOff: true));
                radial.properties.Add(Property(property, type));
            }
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);

            view.Tree.SelectEntry(toggle.id);
            var list = view.Detail.Q<TargetListView>("ad-properties");
            for (int i = 0; i < toggle.properties.Count; i++)
            {
                var row = list.RowAt(i);
                AssertCaption(row.Q<VisualElement>("value"), L.Text("ad.ui.target.on_value"), toggle.properties[i].property + "'s on value");
                AssertCaption(row.Q<VisualElement>("off-value"), L.Text("ad.ui.target.off_value"), toggle.properties[i].property + "'s off value");
            }

            view.Tree.SelectEntry(radial.id);
            list = view.Detail.Q<TargetListView>("ad-properties");
            for (int i = 0; i < radial.properties.Count; i++)
            {
                var row = list.RowAt(i);
                AssertCaption(row.Q<VisualElement>("min"), L.Text("ad.ui.min"), radial.properties[i].property + "'s lower end");
                AssertCaption(row.Q<VisualElement>("value"), L.Text("ad.ui.max"), radial.properties[i].property + "'s upper end");
            }
        }

        static void AssertCaption(VisualElement control, string expected, string what)
        {
            Assert.That(control, Is.Not.Null, what + " is shown");
            var caption = control.Q<Label>(className: "unity-base-field__label") ?? control.Q<Label>(className: "ad-caption");
            Assert.That(caption?.text, Is.EqualTo(expected), what + " carries its caption");
            if (caption.ClassListContains("ad-caption"))
                Assert.That(caption.ClassListContains("ad-label") && !caption.ClassListContains("ad-note"), Is.True, what + ": a Vector's caption has the label look, not the faint note look");
        }

        /// <summary>The 40 px number box is for a blend shape weight, 0 to 100, and nothing else: a property slider keeps Unity's own box.</summary>
        [Test]
        public void OnlyBlendShapeWeightsNarrowTheirNumberBox()
        {
            var menu = avatar.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Smile");
            toggle.blendShapes.Add(Shape("Mouth_Smile_Wide_Open", explicitOff: true));
            toggle.properties.Add(Property("_RimStrength", PropertyValueType.Float, explicitOff: true));
            var over = menu.AddEntry(EntryKind.Override, "Silence");
            over.blendShapes.Add(Shape("Mouth_Smile_Wide_Open", weight: 0f));
            var inspector = DocsShot.Inspector(menu);
            var reference = new Slider(0f, 1f) { showInputField = true };
            try
            {
                LayoutProbe.Measure(inspector, () =>
                {
                    inspector.parent.Add(reference);
                    var view = inspector.Q<AvatarDoctorView>();
                    view.Tree.SelectEntry(toggle.id);
                    LayoutProbe.SettleLists(inspector);
                    float unitys = NumberBox(reference).worldBound.width;
                    Assert.That(unitys, Is.GreaterThan(40.5f), "Unity's own number box is wider than a weight's");
                    var shapes = view.Detail.Q<TargetListView>("ad-blendshapes");
                    Assert.That(NumberBox(shapes.Q<Slider>("weight")).worldBound.width, Is.EqualTo(40f).Within(0.5f), "a weight's number box");
                    Assert.That(NumberBox(shapes.Q<Slider>("off-value")).worldBound.width, Is.EqualTo(40f).Within(0.5f), "an off weight's number box");
                    var properties = view.Detail.Q<TargetListView>("ad-properties");
                    Assert.That(NumberBox(properties.Q<Slider>("value")).worldBound.width, Is.EqualTo(unitys).Within(0.5f), "a property's on value keeps Unity's box");
                    Assert.That(NumberBox(properties.Q<Slider>("off-value")).worldBound.width, Is.EqualTo(unitys).Within(0.5f), "a property's off value keeps Unity's box");

                    view.Tree.SelectEntry(over.id);
                    LayoutProbe.SettleLists(inspector);
                    var pinned = view.Detail.Q<TargetListView>("ad-blendshapes").Q<Slider>("weight");
                    Assert.That(NumberBox(pinned).worldBound.width, Is.EqualTo(40f).Within(0.5f), "an override's pinned weight");
                });
            }
            finally
            {
                reference.RemoveFromHierarchy();
                DocsShot.ReleaseInspectors(inspector);
            }
        }

        static VisualElement NumberBox(VisualElement slider)
        {
            Assert.That(slider, Is.Not.Null, "the slider is shown");
            var box = slider.Q(className: "unity-base-slider__text-field");
            Assert.That(box, Is.Not.Null, "the slider shows its number box");
            return box;
        }

        /// <summary>Audits every entry's panel in a fresh inspector at each width and in each language, and fails once with everything found.</summary>
        void AuditEverywhere(AvatarDoc menu, params MenuEntry[] entries)
        {
            var report = new List<string>();
            var real = LanguageFont.Applier;
            try
            {
                foreach (var language in Languages)
                {
                    var font = language == "en-US" ? null : PanelAuditTests.TryNdmfFont(language);
                    LanguageFont.Applier = font.HasValue ? (Action<VisualElement>)(element => element.style.unityFontDefinition = font.Value) : real;
                    using (L.UseTestLanguage(language, null))
                        foreach (var width in Widths)
                            AuditAt(menu, entries, language, width, report);
                }
            }
            finally
            {
                LanguageFont.Applier = real;
            }
            Assert.That(report, Is.Empty, "list rows that do not fit:\n" + string.Join("\n", report));
        }

        static void AuditAt(AvatarDoc menu, MenuEntry[] entries, string language, float width, List<string> report)
        {
            var allow = PanelAuditTests.StandardAllowList();
            var inspector = DocsShot.Inspector(menu);
            try
            {
                LayoutProbe.Measure(inspector, () =>
                {
                    var view = inspector.Q<AvatarDoctorView>();
                    foreach (var entry in entries)
                    {
                        string where = language + " at " + width + " px, " + entry.name;
                        view.Tree.SelectEntry(entry.id);
                        LayoutProbe.SettleLists(inspector);
                        foreach (var problem in RowProblems(view.Detail)) report.Add(where + ": " + problem);
                        try
                        {
                            PanelAudit.Audit(view.Detail, view.Detail.worldBound.xMax, allow);
                        }
                        catch (AssertionException e)
                        {
                            report.Add(where + ": " + e.Message.Trim());
                        }
                    }
                }, width);
            }
            finally
            {
                DocsShot.ReleaseInspectors(inspector);
            }
        }

        /// <summary>
        /// What the audit's general rules do not see in a list: a panel with no row drawn, a remove button that is
        /// not at its row's top right (wrapped under the controls, say), and a shape or property button whose
        /// tooltip does not name in full the text its ellipsis may cut.
        /// </summary>
        static List<string> RowProblems(VisualElement detail)
        {
            var problems = new List<string>();
            var rows = detail.Query(className: "ad-target").ToList().Where(row => row.worldBound.height > 0f).ToList();
            if (rows.Count == 0) problems.Add("no list row was drawn, so nothing was measured");
            foreach (var row in rows)
            {
                var main = row.Children().FirstOrDefault(c => c.name == "ad-target-main");
                var remove = row.Children().FirstOrDefault(c => c.name == "remove");
                if (main == null || remove == null) continue;
                foreach (var sibling in row.Children().Where(c => c != remove && c.resolvedStyle.display != DisplayStyle.None))
                    if (remove.worldBound.xMax < sibling.worldBound.xMax - 0.5f)
                        problems.Add("the remove button ends at x " + remove.worldBound.xMax.ToString("0.0") + ", left of " + Name(sibling) + " ending at " + sibling.worldBound.xMax.ToString("0.0") + ": it is not at the row's right");
                if (Mathf.Abs(remove.worldBound.yMin - main.worldBound.yMin) > 2f)
                    problems.Add("the remove button's top is at y " + remove.worldBound.yMin.ToString("0.0") + ", the controls start at y " + main.worldBound.yMin.ToString("0.0") + ": it is not at the row's top");
            }
            foreach (var button in detail.Query<Button>().ToList().Where(b => (b.name == "shape" || b.name == "property" || b.name == "ad-trigger-shape") && b.worldBound.width > 0f))
                if (button.text != L.Text("ad.ui.property.pick") && (button.tooltip == null || !button.tooltip.Contains(button.text)))
                    problems.Add("the " + button.name + " button's tooltip does not name \"" + button.text + "\" in full");
            return problems;
        }

        static string Name(VisualElement e) => string.IsNullOrEmpty(e.name) ? e.GetType().Name : "#" + e.name;
    }
}
#endif
