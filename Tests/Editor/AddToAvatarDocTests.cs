#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Add to AvatarDoc: a blend shape slider's right click, the mesh's component menu or a material header's menu turns
    /// the value a shape or a property has now into a toggle or a radial, or one more effect of an entry the avatar has. The test
    /// shaders are in memory; the Poiyomi ones are only named like Poiyomi's.
    /// </summary>
    public class AddToAvatarDocTests
    {
        const string ValuesSource = @"Shader ""AvatarDocTests/Add Values""
{
    Properties
    {
        _Glow (""Glow"", Range(0, 4)) = 0.25
        _Tint (""Tint"", Color) = (0.2, 0.4, 0.6, 1)
        _Offset (""Offset"", Vector) = (1, 2, 3, 4)
        _Amount (""Amount"", Float) = 3
        _MainTex (""Texture"", 2D) = ""white"" { }
    }
    SubShader { Pass { } }
}";

        const string PoiyomiSource = @"Shader "".poiyomi/AvatarDocTests/Add Toon""
{
    Properties
    {
        _EmissionStrength (""Emission Strength"", Float) = 0
        _EmissionColor (""Emission Colour"", Color) = (0, 0, 0, 1)
        _Glow (""Glow"", Float) = 0
    }
    SubShader { Pass { } }
}";

        // Locked with the suffix BodyMat: _EmissionStrength was marked Renamed, _Glow was not marked and is baked.
        const string LockedSource = @"Shader ""Hidden/Locked/.poiyomi/AvatarDocTests/Add Toon/0a1b2c3d""
{
    Properties
    {
        _EmissionStrength_BodyMat (""Emission Strength"", Float) = 0
        _Glow (""Glow"", Float) = 0
    }
    SubShader { Pass { } }
}";

        TestAvatar avatar;
        Shader valuesShader;
        Shader poiyomiShader;
        Shader lockedShader;
        readonly List<Object> made = new List<Object>();
        Action<AddDraft> previousOpen;
        Action<VisualElement, List<AdMenu.Item>> previousShowMenu;
        Func<GameObject[]> previousSelected;
        Action<Exception> previousLog;
        GameObject[] selected = new GameObject[0];
        AddDraft opened;
        List<AdMenu.Item> shownMenu;

        [SetUp]
        public void SetUp()
        {
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
            avatar = TestAvatar.Create();
            valuesShader = ShaderUtil.CreateShaderAsset(ValuesSource, false);
            poiyomiShader = ShaderUtil.CreateShaderAsset(PoiyomiSource, false);
            lockedShader = ShaderUtil.CreateShaderAsset(LockedSource, false);
            previousOpen = AddToAvatarDocMenus.Open;
            AddToAvatarDocMenus.Open = draft => opened = draft;
            previousShowMenu = AddToAvatarDocWindow.ShowMenu;
            AddToAvatarDocWindow.ShowMenu = (anchor, items) => shownMenu = items;
            previousSelected = AddToAvatarDocMenus.Selected;
            AddToAvatarDocMenus.Selected = () => selected;
            previousLog = AddToAvatarDocMenus.Log;
            selected = new GameObject[0];
            opened = null;
            shownMenu = null;
        }

        [TearDown]
        public void TearDown()
        {
            AddToAvatarDocMenus.Open = previousOpen;
            AddToAvatarDocWindow.ShowMenu = previousShowMenu;
            AddToAvatarDocMenus.Selected = previousSelected;
            AddToAvatarDocMenus.Log = previousLog;
            avatar.Dispose();
            foreach (var obj in made) if (obj != null) Object.DestroyImmediate(obj);
            made.Clear();
            Object.DestroyImmediate(valuesShader);
            Object.DestroyImmediate(poiyomiShader);
            Object.DestroyImmediate(lockedShader);
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        T Made<T>(T obj) where T : Object
        {
            made.Add(obj);
            return obj;
        }

        Material Values(string name = "Values") => Made(new Material(valuesShader) { name = name });

        Material Poiyomi(string name = "Body Mat", bool locked = false) => Made(new Material(locked ? lockedShader : poiyomiShader) { name = name });

        /// <summary>A skinned mesh beside the avatar, not under it.</summary>
        SkinnedMeshRenderer Outside()
        {
            var go = Made(new GameObject("Loose"));
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = avatar.Mesh;
            renderer.sharedMaterials = new[] { avatar.MaterialA };
            return renderer;
        }

        static string English(string key)
        {
            Assert.That(LocalizationFileTests.ShippedTexts("en-US").TryGetValue(key, out var text), Is.True, key + " is in the English file");
            return text;
        }

        /// <summary>The items of a GenericMenu: their text, whether they can be chosen, and what choosing runs.</summary>
        static List<(string text, bool enabled, Action run)> Items(GenericMenu menu)
        {
            var list = (IList)typeof(GenericMenu).GetProperty("menuItems", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu);
            var items = new List<(string, bool, Action)>();
            foreach (var item in list)
            {
                var type = item.GetType();
                if ((bool)type.GetField("separator").GetValue(item)) continue;
                var content = (GUIContent)type.GetField("content").GetValue(item);
                var func = (GenericMenu.MenuFunction)type.GetField("func").GetValue(item);
                var func2 = (GenericMenu.MenuFunction2)type.GetField("func2").GetValue(item);
                var data = type.GetField("userData").GetValue(item);
                Action run = func != null ? () => func() : func2 != null ? () => func2(data) : (Action)null;
                items.Add((content.text, run != null, run));
            }
            return items;
        }

        SerializedProperty Weight(SkinnedMeshRenderer renderer, int index)
        {
            renderer.SetBlendShapeWeight(renderer.sharedMesh.blendShapeCount - 1, renderer.GetBlendShapeWeight(renderer.sharedMesh.blendShapeCount - 1));
            var serialized = new SerializedObject(renderer);
            return serialized.FindProperty("m_BlendShapeWeights.Array.data[" + index + "]");
        }

        static IEnumerable<AdMenu.Item> Flatten(IEnumerable<AdMenu.Item> items)
        {
            foreach (var item in items)
            {
                yield return item;
                if (item.Children != null)
                    foreach (var child in Flatten(item.Children)) yield return child;
            }
        }

        /// <summary>A part of a Unity field, such as a slider's number box, or of a scroll bar: the field's own tooltip covers it.</summary>
        static bool InsideAField(VisualElement e)
        {
            for (var p = e.parent; p != null; p = p.parent)
                if (p.ClassListContains("unity-base-field") || p is Scroller) return true;
            return false;
        }

        AddToAvatarDocWindow Window(AddDraft draft)
        {
            var window = ScriptableObject.CreateInstance<AddToAvatarDocWindow>();
            window.Init(draft);
            return window;
        }

        // ---- the three ways in -------------------------------------------------------------------------------------

        [Test]
        public void TheShapeHookAddsTheItemForAWeightsProperty()
        {
            avatar.AddMenu();
            avatar.Body.SetBlendShapeWeight(0, 70f);
            var menu = new GenericMenu();

            AddToAvatarDocMenus.OnPropertyMenu(menu, Weight(avatar.Body, 0));

            var items = Items(menu);
            Assert.That(items.Select(i => i.text), Is.EqualTo(new[] { "AvatarDoc/" + string.Format(English("ad.ui.add_from.item"), "\"Blush\"") }));
            Assert.That(items[0].enabled, Is.True);
            items[0].run();
            Assert.That(opened, Is.Not.Null, "choosing it opens the window");
            Assert.That(opened.renderer, Is.SameAs(avatar.Body));
            Assert.That(opened.isShape, Is.True);
            Assert.That(opened.shape, Is.EqualTo("Blush"));
            Assert.That(opened.on.x, Is.EqualTo(70f));
        }

        [Test]
        public void TheShapeHookLeavesOtherPropertiesAlone()
        {
            avatar.AddMenu();
            avatar.Body.SetBlendShapeWeight(1, 10f);
            var serialized = new SerializedObject(avatar.Body);
            foreach (var path in new[] { "m_Enabled", "m_BlendShapeWeights", "m_BlendShapeWeights.Array.size", "m_Materials.Array.data[0]" })
            {
                var menu = new GenericMenu();
                AddToAvatarDocMenus.OnPropertyMenu(menu, serialized.FindProperty(path));
                Assert.That(menu.GetItemCount(), Is.EqualTo(0), path);
            }
            var transformMenu = new GenericMenu();
            AddToAvatarDocMenus.OnPropertyMenu(transformMenu, new SerializedObject(avatar.Body.transform).FindProperty("m_LocalPosition"));
            Assert.That(transformMenu.GetItemCount(), Is.EqualTo(0), "another component's property");
            var none = new GenericMenu();
            AddToAvatarDocMenus.OnPropertyMenu(none, null);
            Assert.That(none.GetItemCount(), Is.EqualTo(0));
        }

        [Test]
        public void TheShapeHookOutsideAnAvatarShowsTheItemDisabledWithTheReason()
        {
            var loose = Outside();
            var menu = new GenericMenu();

            AddToAvatarDocMenus.OnPropertyMenu(menu, Weight(loose, 0));

            var items = Items(menu);
            Assert.That(items.Select(i => i.text), Is.EqualTo(new[] { "AvatarDoc/" + string.Format(English("ad.ui.add_from.item.outside"), "\"Blush\"") }));
            Assert.That(items[0].enabled, Is.False);
        }

        [Test]
        public void TheComponentMenuItemIsDisabledOutsideAnAvatar()
        {
            avatar.AddMenu();
            Assert.That(AddToAvatarDocMenus.CanAddFrom(new MenuCommand(Outside())), Is.False);
            Assert.That(AddToAvatarDocMenus.CanAddFrom(new MenuCommand(avatar.Body)), Is.True);
            Assert.That(AddToAvatarDocMenus.CanAddFrom(new MenuCommand(avatar.Hat.GetComponent<MeshRenderer>())), Is.True);
            Assert.That(AddToAvatarDocMenus.CanAddFrom(new MenuCommand(avatar.Hat.transform)), Is.False);
            Assert.That(AddToAvatarDocMenus.CanAddFrom(null), Is.False);

            var items = typeof(AddToAvatarDocMenus).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(m => m.GetCustomAttributes<UnityEditor.MenuItem>().Select(a => (a.menuItem, a.validate))).ToList();
            foreach (var path in new[] { "CONTEXT/SkinnedMeshRenderer/Add to AvatarDoc...", "CONTEXT/MeshRenderer/Add to AvatarDoc...", "CONTEXT/Material/Add to AvatarDoc..." })
            {
                Assert.That(items, Does.Contain((path, false)), path);
                Assert.That(items, Does.Contain((path, true)), path + " has a check that greys it out");
            }
        }

        [Test]
        public void TheComponentMenuOpensTheWindowWithNothingPickedYet()
        {
            avatar.AddMenu();
            AddToAvatarDocMenus.AddFrom(new MenuCommand(avatar.Body));
            Assert.That(opened, Is.Not.Null);
            Assert.That(opened.renderer, Is.SameAs(avatar.Body));
            Assert.That(AddToAvatarDoc.Picked(opened), Is.False);

            opened = null;
            AddToAvatarDocMenus.AddFrom(new MenuCommand(Outside()));
            Assert.That(opened, Is.Null, "nothing opens for a mesh outside an avatar");
        }

        [Test]
        public void TheMaterialHeaderItemIsGreyedOutUnlessASelectedMeshUnderAnAvatarUsesTheMaterial()
        {
            avatar.AddMenu();
            var values = Values();
            avatar.Body.sharedMaterials = new[] { avatar.MaterialA, values };
            var loose = Outside();
            loose.sharedMaterials = new[] { values };
            var realSelection = Selection.objects;

            Assert.That(AddToAvatarDocMenus.CanAddFromMaterial(new MenuCommand(values)), Is.False, "nothing selected: a material picked in the Project window");
            selected = new[] { avatar.Hat };
            Assert.That(AddToAvatarDocMenus.CanAddFromMaterial(new MenuCommand(values)), Is.False, "the selected mesh does not use it");
            selected = new[] { loose.gameObject };
            Assert.That(AddToAvatarDocMenus.CanAddFromMaterial(new MenuCommand(values)), Is.False, "the mesh that uses it is not part of an avatar");
            selected = new[] { avatar.Body.gameObject };
            Assert.That(AddToAvatarDocMenus.CanAddFromMaterial(new MenuCommand(values)), Is.True);
            Assert.That(AddToAvatarDocMenus.CanAddFromMaterial(new MenuCommand(avatar.Body)), Is.False, "the item belongs to a material");
            Assert.That(AddToAvatarDocMenus.CanAddFromMaterial(null), Is.False);
            Assert.That(Selection.objects, Is.EqualTo(realSelection), "the check never changes the selection");
        }

        [Test]
        public void TheMaterialHeaderOpensTheWindowWithThatMaterialsSlotPicked()
        {
            avatar.AddMenu();
            var values = Values();
            avatar.Body.sharedMaterials = new[] { avatar.MaterialA, values };
            selected = new[] { avatar.Hat, avatar.Body.gameObject };

            AddToAvatarDocMenus.AddFromMaterial(new MenuCommand(values));

            Assert.That(opened, Is.Not.Null);
            Assert.That(opened.renderer, Is.SameAs(avatar.Body), "the selected mesh that uses the material");
            Assert.That(opened.isShape, Is.False);
            Assert.That(opened.slot, Is.EqualTo(1));
            Assert.That(opened.property, Is.Empty, "the property is still picked in the window");
            Assert.That(AddToAvatarDoc.Picked(opened), Is.False);
            var window = Window(opened);
            try
            {
                Assert.That(window.rootVisualElement.Q<SegmentedChoice>("ad-add-kind").Index, Is.EqualTo(1), "Property");
                Assert.That(window.rootVisualElement.Q<Button>("ad-add-slot").text, Is.EqualTo("1: Values"));
                Assert.That(window.rootVisualElement.Q<Button>("ad-add-property").text, Is.EqualTo(English("ad.ui.property.pick")));
                UiTestEvents.SimulateClick(window.rootVisualElement.Q<Button>("ad-add-property"));
                Assert.That(Flatten(shownMenu).Where(i => i.Action != null).Select(i => i.SearchText), Is.EquivalentTo(new[] { "_Glow", "_Tint", "_Offset", "_Amount" }),
                    "the slot's own shader, without its texture");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void TheShapeHookIgnoresSeveralMeshesAndAWeightPastTheMeshsShapes()
        {
            avatar.AddMenu();
            var second = Made(new GameObject("Second")).AddComponent<SkinnedMeshRenderer>();
            second.transform.SetParent(avatar.Root.transform);
            second.sharedMesh = avatar.Mesh;
            second.SetBlendShapeWeight(1, 5f);
            avatar.Body.SetBlendShapeWeight(1, 5f);
            var both = new SerializedObject(new Object[] { avatar.Body, second });
            var several = new GenericMenu();
            AddToAvatarDocMenus.OnPropertyMenu(several, both.FindProperty("m_BlendShapeWeights.Array.data[0]"));
            Assert.That(several.GetItemCount(), Is.EqualTo(0), "two meshes selected at once");

            var past = Weight(avatar.Body, 1);
            var oneShape = Made(TestAvatar.QuadWithShapes("Only"));
            avatar.Body.sharedMesh = oneShape;
            var menu = new GenericMenu();
            AddToAvatarDocMenus.OnPropertyMenu(menu, past);
            Assert.That(menu.GetItemCount(), Is.EqualTo(0), "a weight the mesh has no shape for");
        }

        [Test]
        public void AFailureInsideTheRightClickMenuKeepsUnitysItemsAndIsReportedOnce()
        {
            avatar.AddMenu();
            avatar.Body.SetBlendShapeWeight(0, 70f);
            var reported = new List<Exception>();
            AddToAvatarDocMenus.Log = e => reported.Add(e);
            using (L.UseTestLanguage("xx", new Dictionary<string, string> { { "ad.ui.add_from.item", "Add {1}..." } }))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Copy"), false, () => { });
                Assert.DoesNotThrow(() => AddToAvatarDocMenus.OnPropertyMenu(menu, Weight(avatar.Body, 0)));
                Assert.DoesNotThrow(() => AddToAvatarDocMenus.OnPropertyMenu(menu, Weight(avatar.Body, 0)));
                Assert.That(Items(menu).Select(i => i.text), Is.EqualTo(new[] { "Copy" }), "Unity's own item stays");
            }
            Assert.That(reported.Count, Is.EqualTo(1), "one report however often it fails");
            Assert.That(reported[0], Is.InstanceOf<FormatException>());
        }

        // ---- what the window starts with -------------------------------------------------------------------------

        [Test]
        public void AShapeStartsAtItsWeightAndZero()
        {
            var menu = avatar.AddMenu();
            avatar.Body.SetBlendShapeWeight(0, 70f);

            var draft = AddToAvatarDoc.ForShape(avatar.Body, "Blush");

            Assert.That(draft.on.x, Is.EqualTo(70f));
            Assert.That(draft.off.x, Is.EqualTo(0f));
            Assert.That(draft.name, Is.EqualTo("Blush"));
            Assert.That(draft.make, Is.EqualTo(AddMake.NewToggle));
            Assert.That(draft.component, Is.SameAs(menu));
            Assert.That(draft.putBack, Is.True);
            Assert.That(AddToAvatarDoc.Now(draft).x, Is.EqualTo(70f));
        }

        [Test]
        public void AFloatStartsAtItsValueAndTheShadersDefault()
        {
            avatar.AddMenu();
            var material = Values();
            material.SetFloat("_Glow", 2f);
            avatar.Body.sharedMaterials = new[] { avatar.MaterialA, material };

            var draft = AddToAvatarDoc.ForProperty(avatar.Body, 1, "_Glow");

            Assert.That(draft.type, Is.EqualTo(PropertyValueType.Float));
            Assert.That(draft.on.x, Is.EqualTo(2f));
            Assert.That(draft.off.x, Is.EqualTo(0.25f), "the shader's own default, not the material's value");
            Assert.That(draft.name, Is.EqualTo("Glow"));
            Assert.That(AddToAvatarDoc.MaterialOf(draft), Is.SameAs(material));
        }

        [Test]
        public void AColourStartsAtItsColourAndTheShadersDefault()
        {
            avatar.AddMenu();
            var material = Values();
            material.SetColor("_Tint", Color.red);
            avatar.Body.sharedMaterials = new[] { material };

            var draft = AddToAvatarDoc.ForProperty(avatar.Body, 0, "_Tint");

            Assert.That(draft.type, Is.EqualTo(PropertyValueType.Color));
            Assert.That((Color)draft.on, Is.EqualTo(Color.red));
            Assert.That(Vector4.Distance(draft.off, new Vector4(0.2f, 0.4f, 0.6f, 1f)), Is.LessThan(1e-5f));
            Assert.That(AddToAvatarDoc.Rest(draft), Is.EqualTo(draft.off));
        }

        [Test]
        public void AVectorAndAPlainNumberStartAtTheShadersDefaultsToo()
        {
            avatar.AddMenu();
            var material = Values();
            material.SetVector("_Offset", new Vector4(5, 6, 7, 8));
            material.SetFloat("_Amount", 9f);
            avatar.Body.sharedMaterials = new[] { material };

            var vector = AddToAvatarDoc.ForProperty(avatar.Body, 0, "_Offset");
            var amount = AddToAvatarDoc.ForProperty(avatar.Body, 0, "_Amount");

            Assert.That(vector.type, Is.EqualTo(PropertyValueType.Vector));
            Assert.That(vector.on, Is.EqualTo(new Vector4(5, 6, 7, 8)));
            Assert.That(vector.off, Is.EqualTo(new Vector4(1, 2, 3, 4)));
            Assert.That(amount.on.x, Is.EqualTo(9f));
            Assert.That(amount.off.x, Is.EqualTo(3f));
        }

        [Test]
        public void TheFirstUnlockedComponentIsTheDefault()
        {
            var first = avatar.AddMenu();
            first.locked = true;
            var second = avatar.AddMenu(avatar.Glasses);
            var draft = AddToAvatarDoc.ForShape(avatar.Body, "Blush");
            Assert.That(AddToAvatarDoc.Components(draft), Is.EqualTo(new[] { first, second }));
            Assert.That(draft.component, Is.SameAs(second));
        }

        // ---- New toggle, New radial and Add to -------------------------------------------------------------------

        [Test]
        public void NewToggleMakesAToggleWithTheShapeInOneUndoStep()
        {
            var menu = avatar.AddMenu();
            avatar.Body.SetBlendShapeWeight(0, 70f);
            var draft = AddToAvatarDoc.ForShape(avatar.Body, "Blush");
            Undo.IncrementCurrentGroup();

            var entry = AddToAvatarDoc.Apply(draft, out var status);

            Assert.That(entry, Is.Not.Null, status);
            Assert.That(entry.kind, Is.EqualTo(EntryKind.Toggle));
            Assert.That(entry.name, Is.EqualTo("Blush"));
            var row = entry.blendShapes.Single();
            Assert.That((row.renderer, row.shape, row.weight), Is.EqualTo((avatar.Body, "Blush", 70f)));
            Assert.That(row.mode, Is.EqualTo(ShapeEffectMode.Fixed));
            Assert.That(row.explicitOff, Is.False, "put back to 0, the scene already rests at the off value");
            Assert.That(avatar.Body.GetBlendShapeWeight(0), Is.EqualTo(0f), "put back");
            Assert.That(status, Is.EqualTo(string.Format(English("ad.ui.add_from.done.toggle"), "Blush")));
            Assert.That((draft.make, draft.targetComponent, draft.targetId), Is.EqualTo((AddMake.AddTo, menu, entry.id)), "a second press adds to the toggle just made");

            Undo.PerformUndo();

            Assert.That(menu.entries, Is.Empty);
            Assert.That(avatar.Body.GetBlendShapeWeight(0), Is.EqualTo(70f), "one step takes the put back away too");
            AddToAvatarDoc.Settle(draft);
            Assert.That(draft.make, Is.EqualTo(AddMake.NewToggle), "with the toggle gone, the next press makes one again");
        }

        [Test]
        public void NewRadialRunsFromTheRestToTheValueInOneUndoStep()
        {
            var menu = avatar.AddMenu();
            var material = Values();
            material.SetFloat("_Glow", 2f);
            avatar.Body.sharedMaterials = new[] { material };
            var draft = AddToAvatarDoc.ForProperty(avatar.Body, 0, "_Glow");
            draft.make = AddMake.NewRadial;
            Undo.IncrementCurrentGroup();

            var entry = AddToAvatarDoc.Apply(draft, out var status);

            Assert.That(entry.kind, Is.EqualTo(EntryKind.Radial), status);
            var row = entry.properties.Single();
            Assert.That((row.renderer, row.property, row.type), Is.EqualTo(((Renderer)avatar.Body, "_Glow", PropertyValueType.Float)));
            Assert.That(row.minValue, Is.EqualTo(0.25f));
            Assert.That(row.value, Is.EqualTo(2f));
            Assert.That(material.GetFloat("_Glow"), Is.EqualTo(0.25f), "put back to the lowest");
            Assert.That(status, Is.EqualTo(string.Format(English("ad.ui.add_from.done.radial"), "Glow")));

            Undo.PerformUndo();

            Assert.That(menu.entries, Is.Empty);
            Assert.That(material.GetFloat("_Glow"), Is.EqualTo(2f));
        }

        [Test]
        public void AddToGivesAnExistingToggleOneMoreEffectInOneUndoStep()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var material = Values();
            material.SetColor("_Tint", Color.red);
            avatar.Body.sharedMaterials = new[] { material };
            var draft = AddToAvatarDoc.ForProperty(avatar.Body, 0, "_Tint");
            draft.make = AddMake.AddTo;
            draft.targetComponent = menu;
            draft.targetId = hat.id;
            Undo.IncrementCurrentGroup();

            var entry = AddToAvatarDoc.Apply(draft, out var status);

            Assert.That(entry, Is.SameAs(hat), status);
            Assert.That(menu.entries.Count, Is.EqualTo(1), "no new entry");
            var row = hat.properties.Single();
            Assert.That(row.color, Is.EqualTo(Color.red));
            Assert.That(row.type, Is.EqualTo(PropertyValueType.Color));
            Assert.That(row.explicitOff, Is.False);
            Assert.That(status, Is.EqualTo(string.Format(English("ad.ui.add_from.done.added"), "Hat")));

            Undo.PerformUndo();

            Assert.That(hat.properties, Is.Empty);
            Assert.That(hat.objects.Count, Is.EqualTo(1));
            Assert.That(material.GetColor("_Tint"), Is.EqualTo(Color.red));
        }

        [Test]
        public void AddToARadialRunsFromTheRestToTheValue()
        {
            var menu = avatar.AddMenu();
            var eyes = menu.AddEntry(EntryKind.Radial, "Eyes");
            avatar.Body.SetBlendShapeWeight(1, 80f);
            var draft = AddToAvatarDoc.ForShape(avatar.Body, "Eye_Big");
            draft.make = AddMake.AddTo;
            draft.targetComponent = menu;
            draft.targetId = eyes.id;

            AddToAvatarDoc.Apply(draft, out _);

            var row = eyes.blendShapes.Single();
            Assert.That((row.minWeight, row.maxWeight), Is.EqualTo((0f, 80f)));
        }

        [Test]
        public void AddToKeepsTheRowAnEntryAlreadyHasInItsPlace()
        {
            var menu = avatar.AddMenu();
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Eye_Big", weight = 100f });
            face.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            avatar.Body.SetBlendShapeWeight(0, 40f);
            var draft = AddToAvatarDoc.ForShape(avatar.Body, "Blush");
            draft.make = AddMake.AddTo;
            draft.targetComponent = menu;
            draft.targetId = face.id;

            AddToAvatarDoc.Apply(draft, out _);

            Assert.That(face.blendShapes.Select(r => (r.shape, r.weight)), Is.EqualTo(new[] { ("Eye_Big", 100f), ("Blush", 40f) }));
        }

        [Test]
        public void EveryWayIsRefusedWhileTheComponentIsLocked()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            var material = Poiyomi();
            material.SetFloat("_EmissionStrength", 2f);
            avatar.Body.sharedMaterials = new[] { material };
            avatar.Body.SetBlendShapeWeight(0, 70f);
            menu.locked = true;

            foreach (var make in new[] { AddMake.NewToggle, AddMake.NewRadial, AddMake.AddTo })
            foreach (bool shape in new[] { true, false })
            {
                var draft = shape ? AddToAvatarDoc.ForShape(avatar.Body, "Blush") : AddToAvatarDoc.ForProperty(avatar.Body, 0, "_EmissionStrength");
                draft.make = make;
                draft.targetComponent = menu;
                draft.targetId = hat.id;
                var entry = AddToAvatarDoc.Apply(draft, out var status);
                Assert.That(entry, Is.Null, make + (shape ? " shape" : " property"));
                Assert.That(status, Is.EqualTo(English("ad.ui.locked.refused")));
            }

            Assert.That(menu.entries.Count, Is.EqualTo(1));
            Assert.That(hat.blendShapes, Is.Empty);
            Assert.That(hat.properties, Is.Empty);
            Assert.That(avatar.Body.GetBlendShapeWeight(0), Is.EqualTo(70f), "nothing is put back");
            Assert.That(material.GetFloat("_EmissionStrength"), Is.EqualTo(2f));
            Assert.That(material.GetTag("_EmissionStrengthAnimated", false, ""), Is.EqualTo(""), "nothing is marked");
        }

        [Test]
        public void WithoutPutBackAToggleWritesItsOwnOffValue()
        {
            avatar.AddMenu();
            avatar.Body.SetBlendShapeWeight(0, 70f);
            var draft = AddToAvatarDoc.ForShape(avatar.Body, "Blush");
            draft.putBack = false;
            draft.off = new Vector4(10f, 0, 0, 0);

            var row = AddToAvatarDoc.Apply(draft, out _).blendShapes.Single();

            Assert.That(avatar.Body.GetBlendShapeWeight(0), Is.EqualTo(70f), "left as it is");
            Assert.That(row.explicitOff, Is.True, "the scene holds the on value, so off has to be written");
            Assert.That(row.minWeight, Is.EqualTo(10f));
        }

        [Test]
        public void PutBackSetsTheMaterialBackAndUndoRestoresIt()
        {
            avatar.AddMenu();
            var material = Values();
            material.SetColor("_Tint", Color.red);
            avatar.Body.sharedMaterials = new[] { material };
            var draft = AddToAvatarDoc.ForProperty(avatar.Body, 0, "_Tint");
            Undo.IncrementCurrentGroup();

            AddToAvatarDoc.Apply(draft, out _);

            Assert.That(Vector4.Distance(material.GetColor("_Tint"), new Vector4(0.2f, 0.4f, 0.6f, 1f)), Is.LessThan(1e-5f));
            Undo.PerformUndo();
            Assert.That(material.GetColor("_Tint"), Is.EqualTo(Color.red));
        }

        [Test]
        public void ANewEntryGoesIntoThePickedSubmenuWithItsName()
        {
            var menu = avatar.AddMenu();
            var face = menu.AddEntry(EntryKind.Submenu, "Face");
            var draft = AddToAvatarDoc.ForShape(avatar.Body, "Blush");
            draft.parentId = face.id;
            draft.name = "Cheeks";

            var entry = AddToAvatarDoc.Apply(draft, out _);

            Assert.That(entry.parentId, Is.EqualTo(face.id));
            Assert.That(entry.name, Is.EqualTo("Cheeks"));
        }

        // ---- Poiyomi ----------------------------------------------------------------------------------------------

        [Test]
        public void APoiyomiPropertyWithoutAMarkGetsItWithTheEntryInOneUndoStep()
        {
            var menu = avatar.AddMenu();
            var material = Poiyomi();
            material.SetFloat("_EmissionStrength", 2f);
            avatar.Body.sharedMaterials = new[] { material };
            var draft = AddToAvatarDoc.ForProperty(avatar.Body, 0, "_EmissionStrength");
            Assert.That(AddToAvatarDoc.Unmarked(draft), Is.EqualTo(new[] { material }), "found by the shader's name and the empty tag");
            Assert.That(draft.markForPoiyomi, Is.True, "on by default");
            Undo.IncrementCurrentGroup();

            AddToAvatarDoc.Apply(draft, out _);

            Assert.That(material.GetTag("_EmissionStrengthAnimated", false, ""), Is.EqualTo("1"));
            Undo.PerformUndo();
            Assert.That(material.GetTag("_EmissionStrengthAnimated", false, ""), Is.EqualTo(""));
            Assert.That(menu.entries, Is.Empty);
        }

        [Test]
        public void AnUntickedMarkLeavesThePoiyomiMaterialAlone()
        {
            avatar.AddMenu();
            var material = Poiyomi();
            avatar.Body.sharedMaterials = new[] { material };
            var draft = AddToAvatarDoc.ForProperty(avatar.Body, 0, "_EmissionStrength");
            draft.markForPoiyomi = false;

            AddToAvatarDoc.Apply(draft, out _);

            Assert.That(material.GetTag("_EmissionStrengthAnimated", false, ""), Is.EqualTo(""));
        }

        [Test]
        public void AMaterialThatIsNotPoiyomiIsNeverMarked()
        {
            avatar.AddMenu();
            var material = Values();
            avatar.Body.sharedMaterials = new[] { material };
            var draft = AddToAvatarDoc.ForProperty(avatar.Body, 0, "_Glow");

            Assert.That(AddToAvatarDoc.Unmarked(draft), Is.Empty);
            AddToAvatarDoc.Apply(draft, out _);
            Assert.That(material.GetTag("_GlowAnimated", false, ""), Is.EqualTo(""));
        }

        [Test]
        public void ARenamedPropertyShowsItsAnimatedNameAndKeepsItsMark()
        {
            avatar.AddMenu();
            var material = Poiyomi("Body Mat");
            material.SetOverrideTag("_EmissionStrengthAnimated", "2");
            avatar.Body.sharedMaterials = new[] { material };
            var draft = AddToAvatarDoc.ForProperty(avatar.Body, 0, "_EmissionStrength");

            Assert.That(AddToAvatarDoc.RenamedName(draft), Is.EqualTo("_EmissionStrength_BodyMat"));
            Assert.That(AddToAvatarDoc.Unmarked(draft), Is.Empty);
            AddToAvatarDoc.Apply(draft, out _);
            Assert.That(material.GetTag("_EmissionStrengthAnimated", false, ""), Is.EqualTo("2"));

            var window = Window(draft);
            try
            {
                var note = window.rootVisualElement.Q<Label>("ad-add-renamed");
                Assert.That(note, Is.Not.Null);
                Assert.That(note.text, Does.Contain("_EmissionStrength_BodyMat"));
                Assert.That(window.rootVisualElement.Q<Toggle>("ad-add-mark"), Is.Null, "nothing to mark");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ALockedMaterialOffersItsLockedNamesAndWarnsWhenTheWantedOneIsBaked()
        {
            avatar.AddMenu();
            var material = Poiyomi("Body Mat", locked: true);
            // Poiyomi keeps the marks it was locked with: _EmissionStrength was Renamed, _Glow was not marked.
            material.SetOverrideTag("_EmissionStrengthAnimated", "2");
            avatar.Body.sharedMaterials = new[] { material };
            var window = Window(AddToAvatarDoc.ForRenderer(avatar.Body));
            try
            {
                window.rootVisualElement.Q<SegmentedChoice>("ad-add-kind").Pick(1);
                UiTestEvents.SimulateClick(window.rootVisualElement.Q<Button>("ad-add-property"));
                Assert.That(shownMenu, Is.Not.Null, "the property button opens the list");
                var names = Flatten(shownMenu).Where(i => i.Action != null).Select(i => i.SearchText).ToList();
                Assert.That(names, Is.EquivalentTo(new[] { "_EmissionStrength_BodyMat", "_Glow" }), "the names the locked shader has");

                Flatten(shownMenu).Single(i => i.SearchText == "_Glow").Action();

                Assert.That(window.Draft.property, Is.EqualTo("_Glow"));
                Assert.That(AddToAvatarDoc.Baked(window.Draft), Is.EqualTo(new[] { material }));
                Assert.That(AddToAvatarDoc.Unmarked(window.Draft), Is.Empty, "a locked material cannot take a mark");
                var warning = window.rootVisualElement.Q("ad-add-baked");
                Assert.That(warning, Is.Not.Null);
                Assert.That(warning.Q<Label>().text, Is.EqualTo(string.Format(English("ad.ui.add_from.baked.one"), "_Glow", 1, "Body Mat")));
                Assert.That(window.rootVisualElement.Q<Toggle>("ad-add-mark"), Is.Null);

                Flatten(shownMenu).Single(i => i.SearchText == "_EmissionStrength_BodyMat").Action();
                Assert.That(AddToAvatarDoc.Baked(window.Draft), Is.Empty, "a renamed name is animated");
                Assert.That(window.rootVisualElement.Q("ad-add-baked"), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void TheWindowOffersTheMarkOnlyForAnUnmarkedPoiyomiProperty()
        {
            avatar.AddMenu();
            var material = Poiyomi();
            avatar.Body.sharedMaterials = new[] { material };
            var window = Window(AddToAvatarDoc.ForProperty(avatar.Body, 0, "_EmissionStrength"));
            try
            {
                LayoutProbe.Measure(window.rootVisualElement, () =>
                {
                    var mark = window.rootVisualElement.Q<Toggle>("ad-add-mark");
                    Assert.That(mark, Is.Not.Null);
                    Assert.That(mark.value, Is.True);
                    Assert.That(mark.text, Is.EqualTo(English("ad.ui.add_from.mark")));
                    mark.value = false;
                    Assert.That(window.Draft.markForPoiyomi, Is.False);
                });
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        // ---- the window --------------------------------------------------------------------------------------------

        [Test]
        public void TheWindowShowsWhatItAddsAndStartsAtTheValue()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Submenu, "Face");
            avatar.Body.SetBlendShapeWeight(0, 70f);
            var window = Window(AddToAvatarDoc.ForShape(avatar.Body, "Blush"));
            try
            {
                var root = window.rootVisualElement;
                Assert.That(root.Q<Label>("ad-add-mesh-name").text, Is.EqualTo("Body"));
                Assert.That(root.Q<Label>("ad-add-mesh-name").tooltip, Does.StartWith("Body\n"), "a long name ends in an ellipsis, so the tooltip names it in full");
                Assert.That(root.Q<Button>("ad-add-shape").text, Is.EqualTo("Blush"));
                Assert.That(root.Q<Label>("ad-add-now-value").text, Is.EqualTo("70"));
                Assert.That(root.Q<SegmentedChoice>("ad-add-make").Index, Is.EqualTo(0));
                Assert.That(root.Q<TextField>("ad-add-name").value, Is.EqualTo("Blush"));
                Assert.That(root.Q<Slider>("ad-add-on").value, Is.EqualTo(70f));
                Assert.That(root.Q<Slider>("ad-add-off").value, Is.EqualTo(0f));
                Assert.That(root.Q<Slider>("ad-add-on").label, Is.EqualTo(English("ad.ui.target.on_value")));
                Assert.That(root.Q<Toggle>("ad-add-put-back").value, Is.True);
                Assert.That(root.Q<Button>("ad-add-submenu").text, Is.EqualTo(English("ad.ui.menu.move.root")));
                Assert.That(root.Q<Button>("ad-add-component"), Is.Null, "one component: nothing to choose");
                Assert.That(root.Q<Button>("ad-add-entry"), Is.Null, "only for Add to");

                root.Q<SegmentedChoice>("ad-add-make").Pick(1);
                Assert.That(window.Draft.make, Is.EqualTo(AddMake.NewRadial));
                Assert.That(window.rootVisualElement.Q<Slider>("ad-add-on").label, Is.EqualTo(English("ad.ui.max")), "a radial's highest");
                Assert.That(window.rootVisualElement.Q<Slider>("ad-add-off").label, Is.EqualTo(English("ad.ui.min")));

                window.rootVisualElement.Q<SegmentedChoice>("ad-add-make").Pick(2);
                Assert.That(window.rootVisualElement.Q<Button>("ad-add-entry"), Is.Not.Null);
                Assert.That(window.rootVisualElement.Q<TextField>("ad-add-name"), Is.Null, "an entry you have keeps its name");
                Assert.That(window.rootVisualElement.Q<Button>("ad-add-run").enabledSelf, Is.False, "no entry picked yet");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void TheWindowAddsWithItsButtonAndSaysWhatItMade()
        {
            var menu = avatar.AddMenu();
            var face = menu.AddEntry(EntryKind.Submenu, "Face");
            avatar.Body.SetBlendShapeWeight(0, 70f);
            var window = Window(AddToAvatarDoc.ForShape(avatar.Body, "Blush"));
            try
            {
                var selection = Selection.objects;
                LayoutProbe.Measure(window.rootVisualElement, () =>
                {
                    UiTestEvents.SimulateClick(window.rootVisualElement.Q<Button>("ad-add-submenu"));
                    Flatten(shownMenu).Single(i => i.Text == "Face").Action();
                    Assert.That(window.Draft.parentId, Is.EqualTo(face.id));
                    window.rootVisualElement.Q<TextField>("ad-add-name").value = "Cheeks";
                    window.rootVisualElement.Q<Slider>("ad-add-on").value = 55f;

                    UiTestEvents.SimulateClick(window.rootVisualElement.Q<Button>("ad-add-run"));
                });

                var made = menu.entries.Single(e => e.kind == EntryKind.Toggle);
                Assert.That((made.name, made.parentId), Is.EqualTo(("Cheeks", face.id)));
                Assert.That(made.blendShapes.Single().weight, Is.EqualTo(55f));
                Assert.That(window.rootVisualElement.Q<Label>("ad-add-status").text, Is.EqualTo(string.Format(English("ad.ui.add_from.done.toggle"), "Cheeks")));
                Assert.That(Selection.objects, Is.EqualTo(selection), "the selection stays as it was");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void PressingAddTwiceLeavesOneEntry()
        {
            var menu = avatar.AddMenu();
            avatar.Body.SetBlendShapeWeight(0, 70f);
            foreach (var make in new[] { AddMake.NewToggle, AddMake.NewRadial })
            {
                menu.entries.Clear();
                avatar.Body.SetBlendShapeWeight(0, 70f);
                var draft = AddToAvatarDoc.ForShape(avatar.Body, "Blush");
                draft.make = make;
                var window = Window(draft);
                try
                {
                    UiTestEvents.SimulateClick(window.rootVisualElement.Q<Button>("ad-add-run"));
                    UiTestEvents.SimulateClick(window.rootVisualElement.Q<Button>("ad-add-run"));

                    var entry = menu.entries.Single();
                    Assert.That(entry.kind, Is.EqualTo(make == AddMake.NewRadial ? EntryKind.Radial : EntryKind.Toggle));
                    var row = entry.blendShapes.Single();
                    Assert.That(make == AddMake.NewRadial ? row.maxWeight : row.weight, Is.EqualTo(70f), make + ": the second press keeps the value");
                    Assert.That(window.rootVisualElement.Q<SegmentedChoice>("ad-add-make").Index, Is.EqualTo((int)AddMake.AddTo));
                    Assert.That(window.rootVisualElement.Q<Button>("ad-add-entry").text, Is.EqualTo("Blush"));
                    Assert.That(window.Status, Is.EqualTo(string.Format(English("ad.ui.add_from.done.added"), "Blush")));
                }
                finally
                {
                    Object.DestroyImmediate(window);
                }
            }
        }

        [Test]
        public void AMeshOrAnEntryGoneWhileTheWindowIsOpenIsRefused()
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            var draft = AddToAvatarDoc.ForShape(avatar.Body, "Blush");
            draft.make = AddMake.AddTo;
            draft.targetComponent = menu;
            draft.targetId = hat.id;
            var window = Window(draft);
            try
            {
                menu.entries.Remove(hat);
                Assert.That(window.Add(), Is.Null);
                Assert.That(window.Status, Is.EqualTo(English("ad.ui.add_from.entry.pick")));
                Assert.That(menu.entries, Is.Empty);

                Object.DestroyImmediate(avatar.Body.gameObject);
                Assert.That(window.Add(), Is.Null);
                Assert.That(window.Status, Is.EqualTo(English("ad.ui.add_from.gone")));
                Assert.That(window.rootVisualElement.Q("ad-add-outside").Q<Label>().text, Is.EqualTo(English("ad.ui.add_from.gone")));
                Assert.That(menu.entries, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void PutBackOnAPrefabInstanceIsKeptAsAnOverride()
        {
            var folder = TempAssetFolder.Create("AvatarDocAddToTests");
            var meshPath = folder + "/Quad.asset";
            var prefabPath = folder + "/Avatar.prefab";
            var mesh = TestAvatar.QuadWithShapes("Blush");
            GameObject source = null, instance = null;
            try
            {
                AssetDatabase.CreateAsset(mesh, meshPath);
                source = new GameObject("PrefabAvatar");
                source.AddComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
                source.AddComponent<AvatarDoc>();
                var face = new GameObject("Face").AddComponent<SkinnedMeshRenderer>();
                face.transform.SetParent(source.transform);
                face.sharedMesh = mesh;
                face.SetBlendShapeWeight(0, 70f);
                var prefab = PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
                Object.DestroyImmediate(source);
                source = null;
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var renderer = instance.GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(70f));

                var entry = AddToAvatarDoc.Apply(AddToAvatarDoc.ForShape(renderer, "Blush"), out var status);

                Assert.That(entry, Is.Not.Null, status);
                Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(0f));
                var modifications = PrefabUtility.GetPropertyModifications(instance) ?? new PropertyModification[0];
                Assert.That(modifications.Any(m => m.target is SkinnedMeshRenderer && m.propertyPath == "m_BlendShapeWeights.Array.data[0]" && m.value == "0"), Is.True,
                    "the weight put back is written as an override on the instance");
                Assert.That(modifications.Any(m => m.propertyPath != null && m.propertyPath.StartsWith("entries")), Is.True, "and so is the new toggle");
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                if (source != null) Object.DestroyImmediate(source);
                AssetDatabase.DeleteAsset(prefabPath);
                AssetDatabase.DeleteAsset(meshPath);
                TempAssetFolder.Delete(folder);
            }
        }

        [Test]
        public void TheWindowAddsToTheEntryPickedFromItsList()
        {
            var menu = avatar.AddMenu();
            var face = menu.AddEntry(EntryKind.Submenu, "Face");
            var smile = menu.AddEntry(EntryKind.Toggle, "Smile", face.id);
            menu.AddEntry(EntryKind.Radial, "Eyes");
            menu.AddEntry(EntryKind.Trigger, "Wink");
            avatar.Body.SetBlendShapeWeight(0, 70f);
            var draft = AddToAvatarDoc.ForShape(avatar.Body, "Blush");
            draft.make = AddMake.AddTo;
            var window = Window(draft);
            try
            {
                UiTestEvents.SimulateClick(window.rootVisualElement.Q<Button>("ad-add-entry"));
                var choices = Flatten(shownMenu).Where(i => i.Action != null).ToList();
                Assert.That(choices.Select(i => i.Text), Is.EqualTo(new[] { "Smile", "Eyes" }), "toggles and radials only");
                Assert.That(choices[0].Note, Is.EqualTo("Face/Smile"));
                choices[0].Action();
                Assert.That(window.rootVisualElement.Q<Button>("ad-add-entry").text, Is.EqualTo("Smile"));

                window.Add();

                Assert.That(smile.blendShapes.Single().weight, Is.EqualTo(70f));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void TheWindowRefusesWhileLockedAndSaysWhy()
        {
            var menu = avatar.AddMenu();
            menu.locked = true;
            var window = Window(AddToAvatarDoc.ForShape(avatar.Body, "Blush"));
            try
            {
                Assert.That(window.Add(), Is.Null);
                Assert.That(menu.entries, Is.Empty);
                Assert.That(window.rootVisualElement.Q<Label>("ad-add-status").text, Is.EqualTo(English("ad.ui.locked.refused")));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void TheWindowOffersToAddAComponentToAnAvatarWithoutOne()
        {
            var window = Window(AddToAvatarDoc.ForShape(avatar.Body, "Blush"));
            try
            {
                Assert.That(window.rootVisualElement.Q<Button>("ad-add-run").enabledSelf, Is.False);
                var add = window.rootVisualElement.Q<Button>("ad-add-component-add");
                Assert.That(add, Is.Not.Null);
                Undo.IncrementCurrentGroup();

                UiTestEvents.SimulateClick(add);

                var component = avatar.Root.GetComponent<AvatarDoc>();
                Assert.That(component, Is.Not.Null, "added on the avatar's root");
                Assert.That(window.Draft.component, Is.SameAs(component));
                Assert.That(window.rootVisualElement.Q<Button>("ad-add-run").enabledSelf, Is.True);
                Assert.That(window.rootVisualElement.Q<Button>("ad-add-component-add"), Is.Null);

                Undo.PerformUndo();
                Assert.That(avatar.Root.GetComponent<AvatarDoc>(), Is.Null, "one Undo takes it away");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void TheWindowLetsYouChooseTheComponentWhenThereAreSeveral()
        {
            var first = avatar.AddMenu();
            var second = avatar.AddMenu(avatar.Glasses);
            var window = Window(AddToAvatarDoc.ForShape(avatar.Body, "Blush"));
            try
            {
                UiTestEvents.SimulateClick(window.rootVisualElement.Q<Button>("ad-add-component"));
                Assert.That(Flatten(shownMenu).Where(i => i.Action != null).Select(i => i.Text), Is.EqualTo(new[] { "TestAvatar", "Glasses" }));
                Flatten(shownMenu).Single(i => i.Text == "Glasses").Action();
                window.Add();
                Assert.That(first.entries, Is.Empty);
                Assert.That(second.entries.Single().name, Is.EqualTo("Blush"));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void TheWindowPicksAShapeOrAPropertyWhenOpenedFromTheComponentMenu()
        {
            avatar.AddMenu();
            var material = Values();
            material.SetFloat("_Glow", 3f);
            avatar.Body.sharedMaterials = new[] { material };
            avatar.Body.SetBlendShapeWeight(1, 20f);
            var child = new GameObject("Child").AddComponent<SkinnedMeshRenderer>();
            child.transform.SetParent(avatar.Body.transform);
            child.sharedMesh = Made(TestAvatar.QuadWithShapes("Child_Shape"));
            var window = Window(AddToAvatarDoc.ForRenderer(avatar.Body));
            try
            {
                Assert.That(window.rootVisualElement.Q<Button>("ad-add-run").enabledSelf, Is.False, "nothing picked yet");
                Assert.That(window.rootVisualElement.Q<Button>("ad-add-shape").text, Is.EqualTo(English("ad.ui.pick.shape")));
                UiTestEvents.SimulateClick(window.rootVisualElement.Q<Button>("ad-add-shape"));
                Assert.That(shownMenu.Select(i => i.Text), Is.EqualTo(new[] { "Blush", "Eye_Big" }), "this mesh's own shapes, none of a mesh under it");
                shownMenu.Single(i => i.Text == "Eye_Big").Action();
                Assert.That(window.Draft.renderer, Is.SameAs(avatar.Body));
                Assert.That(window.Draft.shape, Is.EqualTo("Eye_Big"));
                Assert.That(window.Draft.on.x, Is.EqualTo(20f));
                Assert.That(window.rootVisualElement.Q<Button>("ad-add-run").enabledSelf, Is.True);

                window.rootVisualElement.Q<SegmentedChoice>("ad-add-kind").Pick(1);
                Assert.That(window.Draft.isShape, Is.False);
                UiTestEvents.SimulateClick(window.rootVisualElement.Q<Button>("ad-add-property"));
                Flatten(shownMenu).Single(i => i.SearchText == "_Glow").Action();
                Assert.That(window.Draft.property, Is.EqualTo("_Glow"));
                Assert.That(window.Draft.on.x, Is.EqualTo(3f));
                Assert.That(window.rootVisualElement.Q<Slider>("ad-add-on").highValue, Is.EqualTo(4f), "a range shows its own slider");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void EveryLabelledControlOfTheWindowHasATooltip()
        {
            var menu = avatar.AddMenu();
            avatar.AddMenu(avatar.Glasses);
            var material = Poiyomi();
            avatar.Body.sharedMaterials = new[] { material };
            foreach (var draft in new[] { AddToAvatarDoc.ForShape(avatar.Body, "Blush"), AddToAvatarDoc.ForProperty(avatar.Body, 0, "_EmissionColor") })
            foreach (var make in new[] { AddMake.NewToggle, AddMake.AddTo })
            {
                draft.make = make;
                var window = Window(draft);
                try
                {
                    var root = window.rootVisualElement;
                    var missing = root.Query<VisualElement>().ToList()
                        .Where(e => e is Button || e is Toggle || e is TextField || e is Slider || e is ColorField || e is FloatField || e is Vector4Field
                                    || e is SegmentedChoice || e.ClassListContains("ad-label"))
                        .Where(e => !InsideAField(e) && !(e.parent is SegmentedChoice) && string.IsNullOrEmpty(e.tooltip))
                        .Select(e => e.name + " " + e.GetType().Name).ToList();
                    Assert.That(missing, Is.Empty, make + (draft.isShape ? " shape" : " property"));
                    foreach (var choice in root.Query<SegmentedChoice>().ToList())
                        Assert.That(choice.Items.All(i => !string.IsNullOrEmpty(i.tooltip)), Is.True, choice.name + ": every choice says what it does");
                }
                finally
                {
                    Object.DestroyImmediate(window);
                }
            }
        }

        [Test]
        public void TheWindowFitsAt300And400InEveryLanguage()
        {
            var menu = avatar.AddMenu();
            avatar.AddMenu(avatar.Glasses);
            menu.AddEntry(EntryKind.Submenu, "Expressions and faces");
            menu.AddEntry(EntryKind.Toggle, "Smile");
            var poiyomi = Poiyomi();
            poiyomi.SetOverrideTag("_EmissionColorAnimated", "2");
            var values = Values();
            avatar.Body.sharedMaterials = new[] { poiyomi, values };
            var allow = new PanelAudit.AllowList();

            foreach (var language in new[] { "en-US", "ja-JP", "zh-Hans" })
            foreach (float width in new[] { 300f, 400f })
            using (L.UseTestLanguage(language, null))
            {
                var drafts = new List<AddDraft>
                {
                    AddToAvatarDoc.ForShape(avatar.Body, "Blush"),
                    AddToAvatarDoc.ForProperty(avatar.Body, 0, "_EmissionStrength"),
                    AddToAvatarDoc.ForProperty(avatar.Body, 0, "_EmissionColor"),
                    AddToAvatarDoc.ForProperty(avatar.Body, 1, "_Offset"),
                    AddToAvatarDoc.ForRenderer(avatar.Body),
                };
                drafts[2].make = AddMake.AddTo;
                drafts[3].make = AddMake.NewRadial;
                foreach (var draft in drafts)
                {
                    var window = Window(draft);
                    try
                    {
                        var root = window.rootVisualElement;
                        LayoutProbe.Measure(root, () =>
                        {
                            LayoutProbe.Validate(root);
                            PanelAudit.Audit(root, root.worldBound.xMax, allow);
                            var texts = root.Query<TextElement>().ToList()
                                .Where(t => t.resolvedStyle.display == DisplayStyle.Flex && !string.IsNullOrEmpty(t.text) && t.resolvedStyle.whiteSpace != WhiteSpace.Normal);
                            foreach (var text in texts)
                            {
                                var size = text.MeasureTextSize(text.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                                Assert.That(text.contentRect.width, Is.GreaterThanOrEqualTo(size.x - 1f), language + " at " + width + ": \"" + text.text + "\" is cut short");
                            }
                        }, width);
                    }
                    finally
                    {
                        Object.DestroyImmediate(window);
                    }
                }
            }
        }
    }
}
#endif
