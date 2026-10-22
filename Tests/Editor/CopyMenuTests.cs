#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using nadena.dev.ndmf;
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
using Object = UnityEngine.Object;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Copy to another avatar: one AvatarDoc component copied from one version of an avatar to another, every object it
    /// uses found there by its place under the avatar's root. The two test avatars are built the same way; a test changes
    /// the second one where it needs the versions to differ.
    /// </summary>
    public class CopyMenuTests
    {
        const string PlainShaderSource = @"Shader ""AvatarDocTests/Copy Plain"" { SubShader { Pass { } } }";

        TestAvatar a;
        TestAvatar b;
        readonly List<Object> made = new List<Object>();
        readonly List<Transform> avatars = new List<Transform>();
        Func<IEnumerable<Transform>> previousAvatars;
        Action<CopyDraft> previousOpen;
        Action<VisualElement, List<AdMenu.Item>> previousShowMenu;
        CopyDraft opened;
        List<AdMenu.Item> shownMenu;

        [SetUp]
        public void SetUp()
        {
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
            a = TestAvatar.Create();
            b = TestAvatar.Create();
            b.Root.name = "TestAvatar B";
            avatars.Clear();
            avatars.Add(a.Root.transform);
            avatars.Add(b.Root.transform);
            previousAvatars = MenuCopy.SceneAvatars;
            MenuCopy.SceneAvatars = () => avatars.Where(t => t != null).ToList();
            previousOpen = MenuCopy.Open;
            MenuCopy.Open = draft => opened = draft;
            previousShowMenu = CopyMenuWindow.ShowMenu;
            CopyMenuWindow.ShowMenu = (anchor, items) => shownMenu = items;
            opened = null;
            shownMenu = null;
        }

        [TearDown]
        public void TearDown()
        {
            MenuCopy.SceneAvatars = previousAvatars;
            MenuCopy.Open = previousOpen;
            CopyMenuWindow.ShowMenu = previousShowMenu;
            a.Dispose();
            b.Dispose();
            foreach (var obj in made) if (obj != null) Object.DestroyImmediate(obj);
            made.Clear();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        T Made<T>(T obj) where T : Object
        {
            made.Add(obj);
            return obj;
        }

        /// <summary>The object at path under the avatar's root, made with every step that is not there yet.</summary>
        static GameObject At(TestAvatar avatar, string path)
        {
            var at = avatar.Root.transform;
            foreach (var name in path.Split('/'))
            {
                var next = at.Find(name);
                if (next == null)
                {
                    next = new GameObject(name).transform;
                    next.SetParent(at, false);
                }
                at = next;
            }
            return at.gameObject;
        }

        static GameObject Sibling(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        CopyDraft Draft(AvatarDoc source, TestAvatar to = null) => new CopyDraft { source = source, target = (to ?? b).Root.transform };

        static AvatarDoc Copy(CopyDraft draft)
        {
            var copy = MenuCopy.Apply(draft, out var status);
            Assert.That(copy, Is.Not.Null, "the copy is refused: " + status);
            return copy;
        }

        static string English(string key)
        {
            Assert.That(LocalizationFileTests.ShippedTexts("en-US").TryGetValue(key, out var text), Is.True, key + " is in the English file");
            return text;
        }

        CopyMenuWindow Window(CopyDraft draft)
        {
            var window = ScriptableObject.CreateInstance<CopyMenuWindow>();
            window.Init(draft);
            return window;
        }

        static void Pointer<T>(VisualElement target, EventType type, int button) where T : PointerEventBase<T>, new()
        {
            using (var e = PointerEventBase<T>.GetPooled(new Event { type = type, button = button, mousePosition = target.worldBound.center }))
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        static IEnumerable<string> Ids(AvatarDoc menu)
            => menu.entries.Select(e => e.id).Concat(menu.menuEdits.Select(e => e.id)).Concat(menu.animators.Select(m => m.id)).Concat(menu.menuAssets.Select(m => m.id));

        // ---- finding the objects by their place ------------------------------------------------------------------

        [Test]
        public void AnObjectATransformAndAMeshAreFoundAtTheSamePlace()
        {
            var menu = a.AddMenu();
            var chestA = At(a, "Armature/Hips/Chest");
            var chestB = At(b, "Armature/Hips/Chest");
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = a.Hat });
            hat.blendShapes.Add(new BlendShapeTarget { renderer = a.Body, shape = "Blush" });
            hat.materials.Add(new MaterialTarget { renderer = a.Body, slot = 1, material = a.MaterialB });
            hat.animations.Add(new AnimationTarget { pathRoot = a.Glasses.transform });
            hat.follows.Add(new FollowTarget { target = a.Glasses, follows = chestA.transform });

            var copy = Copy(Draft(menu));

            var entry = copy.entries.Single();
            Assert.That(entry.objects[0].target, Is.SameAs(b.Hat), "a GameObject");
            Assert.That(entry.blendShapes[0].renderer, Is.SameAs(b.Body), "a skinned mesh");
            Assert.That(entry.materials[0].renderer, Is.SameAs(b.Body));
            Assert.That(entry.materials[0].material, Is.SameAs(a.MaterialB), "an asset stays as it is");
            Assert.That(entry.animations[0].pathRoot, Is.SameAs(b.Glasses.transform), "a Transform");
            Assert.That(entry.follows[0].follows, Is.SameAs(chestB.transform), "a Transform three steps down");
            Assert.That(copy.gameObject, Is.SameAs(b.Root), "the source is on the root, so the copy goes on the other root");
        }

        [Test]
        public void TheSecondComponentOfATypeStaysTheSecond()
        {
            var hairA = At(a, "Hair");
            hairA.AddComponent<VRCPhysBone>();
            var secondA = hairA.AddComponent<VRCPhysBone>();
            var hairB = At(b, "Hair");
            hairB.AddComponent<VRCPhysBone>();
            var secondB = hairB.AddComponent<VRCPhysBone>();
            var menu = a.AddMenu();
            var pull = menu.AddEntry(EntryKind.Trigger, "Pull");
            pull.source = TriggerSource.PhysBone;
            pull.physBone = secondA;
            pull.when.Add(new ConditionGroup { all = { new WhenCondition { kind = WhenConditionKind.PhysBone, physBone = secondA } } });
            pull.objects.Add(new ObjectTarget { target = a.Hat });

            var copy = Copy(Draft(menu));

            var entry = copy.entries.Single();
            Assert.That(entry.physBone, Is.SameAs(secondB));
            Assert.That(entry.when[0].all[0].physBone, Is.SameAs(secondB), "a reference inside a condition is found too");

            Object.DestroyImmediate(secondB);
            var plan = MenuCopy.Plan(Draft(menu));
            var missing = plan.Listed.Single();
            Assert.That(missing.Parts, Is.EqualTo(new Object[] { secondA }), "the object is there but not a second PhysBone on it");
            Assert.That(missing.Found, Is.SameAs(hairB));
            Assert.That(missing.Uses, Is.EqualTo(new[] { "Pull" }));
            Assert.That(missing.Need, Is.EqualTo(CopyNeed.Required), "a PhysBone trigger without its PhysBone is an error, so it must be picked");
        }

        [Test]
        public void TheAvatarRootMapsToTheOtherRoot()
        {
            var menu = a.AddMenu();
            var pat = menu.AddEntry(EntryKind.Trigger, "Pat");
            pat.source = TriggerSource.Contact;
            pat.contactAnchor = a.Root.transform;
            pat.objects.Add(new ObjectTarget { target = a.Root });

            var copy = Copy(Draft(menu));

            Assert.That(copy.entries[0].contactAnchor, Is.SameAs(b.Root.transform));
            Assert.That(copy.entries[0].objects[0].target, Is.SameAs(b.Root));
        }

        [Test]
        public void AnObjectOutsideTheAvatarIsLeftAsItIs()
        {
            var loose = Made(new GameObject("Prop"));
            var menu = a.AddMenu();
            var prop = menu.AddEntry(EntryKind.Toggle, "Prop");
            prop.objects.Add(new ObjectTarget { target = loose });
            prop.objects.Add(new ObjectTarget { target = a.Hat });

            var plan = MenuCopy.Plan(Draft(menu));
            var copy = Copy(Draft(menu));

            Assert.That(copy.entries[0].objects[0].target, Is.SameAs(loose));
            Assert.That(copy.entries[0].objects[1].target, Is.SameAs(b.Hat));
            Assert.That(plan.References.Select(r => r.Source), Is.EqualTo(new Object[] { a.Hat }), "only objects of the source avatar are found again");
        }

        [Test]
        public void SameNamedSiblingsMatchByTheirOrderAndSaySoWhenTheCountsDiffer()
        {
            Sibling(a.Root, "Ribbon");
            var secondA = Sibling(a.Root, "Ribbon");
            Sibling(b.Root, "Ribbon");
            var secondB = Sibling(b.Root, "Ribbon");
            var menu = a.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Ribbon").objects.Add(new ObjectTarget { target = secondA });

            var plan = MenuCopy.Plan(Draft(menu));
            Assert.That(plan.References.Single().Found, Is.SameAs(secondB));
            Assert.That(plan.References.Single().AmbiguousAt, Is.Null, "both have two: nothing to say");

            Sibling(b.Root, "Ribbon");
            plan = MenuCopy.Plan(Draft(menu));
            var reference = plan.References.Single();
            Assert.That(reference.Found, Is.SameAs(secondB), "still the second one");
            Assert.That((reference.AmbiguousAt, reference.SourceCount, reference.TargetCount), Is.EqualTo(("Ribbon", 2, 3)));

            var window = Window(Draft(menu));
            try
            {
                var note = window.rootVisualElement.Q<Label>("ad-copy-ambiguous-0");
                Assert.That(note, Is.Not.Null, "the window says so");
                Assert.That(note.text, Is.EqualTo(string.Format(English("ad.ui.copy_to.ambiguous"), "\"Ribbon\"", 2, 3)));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        // ---- objects that are not there --------------------------------------------------------------------------

        [Test]
        public void AMissingObjectIsListedWithItsEntriesAndPickedInTheWindow()
        {
            var earring = Sibling(a.Hat, "Earring");
            var menu = a.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = earring });
            menu.AddEntry(EntryKind.Toggle, "Jewels").follows.Add(new FollowTarget { target = earring, follows = a.Glasses.transform });

            var plan = MenuCopy.Plan(Draft(menu));
            var missing = plan.Listed.Single();
            Assert.That(missing.Path, Is.EqualTo("Hat/Earring"));
            Assert.That(missing.Need, Is.EqualTo(CopyNeed.None), "an object row and a follow left empty are skipped with a message");
            Assert.That(missing.Uses, Is.EqualTo(new[] { "Hat", "Jewels" }));
            Assert.That(missing.Suggestion, Is.Null, "the other avatar has no object of that name");

            var unpicked = Copy(Draft(menu));
            Assert.That(unpicked.entries[0].objects[0].target, Is.Null, "nothing is guessed: the field is left empty");
            Object.DestroyImmediate(unpicked);

            var window = Window(Draft(menu));
            try
            {
                LayoutProbe.Measure(window.rootVisualElement, () =>
                {
                    var root = window.rootVisualElement;
                    var text = root.Q("ad-copy-missing-text-0").Q<Label>().text;
                    Assert.That(text, Is.EqualTo(string.Format(English("ad.ui.copy_to.missing"), "\"TestAvatar/Hat/Earring\"", "\"TestAvatar B\"", "\"Hat\", \"Jewels\"")));
                    var field = root.Q<ObjectField>("ad-copy-pick-0");
                    Assert.That(field.value, Is.Null);
                    Assert.That(field.objectType, Is.EqualTo(typeof(GameObject)));
                    field.value = b.Glasses;
                    Assert.That(window.Copy(), Is.Not.Null, window.Status);
                });
                var copy = b.Root.GetComponent<AvatarDoc>();
                Assert.That(copy.entries[0].objects[0].target, Is.SameAs(b.Glasses), "the picked object");
                Assert.That(copy.entries[1].follows[0].target, Is.SameAs(b.Glasses), "every use of it");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void AUniqueSameNamedObjectIsSuggestedAndUsedUnlessAnotherIsPicked()
        {
            var earringA = Sibling(a.Hat, "Earring");
            var earringB = Sibling(b.Glasses, "Earring");
            var menu = a.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Earring").objects.Add(new ObjectTarget { target = earringA });

            var plan = MenuCopy.Plan(Draft(menu));
            Assert.That(plan.Listed.Single().Suggestion, Is.SameAs(earringB), "the only Earring on the other avatar, at another place");
            Assert.That(plan.Listed.Single().SuggestedByPlace, Is.False);

            var window = Window(Draft(menu));
            try
            {
                var root = window.rootVisualElement;
                Assert.That(root.Q<ObjectField>("ad-copy-pick-0").value, Is.SameAs(earringB), "the field starts at the suggestion");
                Assert.That(root.Q<Label>("ad-copy-suggested-0").text, Is.EqualTo(string.Format(English("ad.ui.copy_to.suggested"), "\"Earring\"")),
                    "and says it is one");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }

            var copy = Copy(Draft(menu));
            Assert.That(copy.entries[0].objects[0].target, Is.SameAs(earringB));
            Object.DestroyImmediate(copy);

            var draft = Draft(menu);
            MenuCopy.Pick(draft, earringA, b.Hat);
            Assert.That(Copy(draft).entries[0].objects[0].target, Is.SameAs(b.Hat), "a pick wins over the suggestion");
            Object.DestroyImmediate(b.Root.GetComponent<AvatarDoc>());

            Sibling(b.Body.gameObject, "Earring");
            Assert.That(MenuCopy.Plan(Draft(menu)).Listed.Single().Suggestion, Is.Null, "two of that name: no suggestion");
        }

        // ---- pick by hand when copying ---------------------------------------------------------------------------

        [Test]
        public void AMarkedObjectIsAlwaysPickedByHandAndTheMarkCarriesOver()
        {
            var menu = a.AddMenu();
            menu.pickByHand.Add(a.Hat);
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = a.Hat });
            hat.materials.Add(new MaterialTarget { renderer = a.Hat.GetComponent<MeshRenderer>(), slot = 0, material = a.MaterialB });
            hat.objects.Add(new ObjectTarget { target = a.Glasses });

            var plan = MenuCopy.Plan(Draft(menu));
            var marked = plan.Listed.Single();
            Assert.That(((Object)marked.Source, marked.ByHand), Is.EqualTo(((Object)a.Hat, true)), "never matched by its place");
            Assert.That(marked.Parts, Is.EquivalentTo(new Object[] { a.Hat, a.Hat.GetComponent<MeshRenderer>() }), "the object and its renderer are one pick");
            Assert.That((marked.Suggestion, marked.SuggestedByPlace), Is.EqualTo(((Object)b.Hat, true)), "the object at the same place is only a suggestion");
            Assert.That(plan.Checks, Is.Empty);

            var window = Window(Draft(menu));
            try
            {
                var root = window.rootVisualElement;
                Assert.That(root.Q("ad-copy-missing-text-0").Q<Label>().text,
                    Is.EqualTo(string.Format(English("ad.ui.copy_to.by_hand_row"), "\"TestAvatar/Hat\"", "\"TestAvatar B\"", "\"Hat\"")), "listed with the objects to pick");
                Assert.That(root.Q<ObjectField>("ad-copy-pick-0").value, Is.SameAs(b.Hat));
                Assert.That(root.Q<Label>("ad-copy-suggested-0").text, Is.EqualTo(English("ad.ui.copy_to.suggested.place")));
                Assert.That(root.Q("ad-copy-missing-1"), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }

            var copy = Copy(Draft(menu));
            Assert.That(copy.entries[0].objects[0].target, Is.SameAs(b.Hat), "the suggestion the field showed");
            Assert.That(copy.entries[0].materials[0].renderer, Is.SameAs(b.Hat.GetComponent<MeshRenderer>()), "its renderer from the same pick");
            Assert.That(copy.entries[0].objects[1].target, Is.SameAs(b.Glasses), "every other object is found by its place");
            Assert.That(copy.pickByHand, Is.EqualTo(new[] { b.Hat }), "the mark carries over to the object picked on the new version");
            Object.DestroyImmediate(copy);

            var draft = Draft(menu);
            MenuCopy.Pick(draft, a.Hat, null);
            var empty = Copy(draft);
            Assert.That(empty.entries[0].objects[0].target, Is.Null, "left empty, the field is copied empty");
            Assert.That(empty.entries[0].materials[0].renderer, Is.Null);
            Assert.That(empty.pickByHand, Is.Empty, "a mark with no pick is dropped");
            Assert.That(empty.entries.SelectMany(e => e.objects).Select(o => o.target).Where(t => t != null).All(t => t.transform.IsChildOf(b.Root.transform)), Is.True,
                "the copy never points into the source avatar");
        }

        [Test]
        public void PickByHandIsMarkedFromARowsRightClickInOneUndoStep()
        {
            var menu = a.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = a.Hat });
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(hat.id);
                LayoutProbe.SettleLists(view);
                var row = view.Detail.Q<TargetListView>("ad-objects").Query(className: "ad-target").ToList().First(r => r.userData is int);
                var panelRoot = view.panel.visualTree;
                try
                {
                    Pointer<PointerDownEvent>(row, EventType.MouseDown, 1);
                    Pointer<PointerUpEvent>(row, EventType.MouseUp, 1);
                    var open = panelRoot.Q<AdMenu>();
                    Assert.That(open, Is.Not.Null, "a right click on a target row opens its menu");
                    int at = open.Items.ToList().FindIndex(i => i.Text == English("ad.ui.copy_to.by_hand"));
                    Assert.That(at, Is.GreaterThanOrEqualTo(0), "it offers Pick by hand when copying");
                    Assert.That(open.Items[at].Note, Is.EqualTo("Hat"));
                    Assert.That(open.Items[at].Checked, Is.False);

                    Undo.IncrementCurrentGroup();
                    Pointer<PointerDownEvent>(open.Rows[at], EventType.MouseDown, 0);
                    Pointer<PointerUpEvent>(open.Rows[at], EventType.MouseUp, 0);
                    Assert.That(menu.pickByHand, Is.EqualTo(new[] { a.Hat }));

                    Undo.PerformUndo();
                    Assert.That(menu.pickByHand, Is.Empty, "one Undo takes the mark back");

                    view.SetMode(ViewMode.Simple);
                    LayoutProbe.SettleLists(view);
                    row = view.Detail.Q<TargetListView>("ad-objects").Query(className: "ad-target").ToList().First(r => r.userData is int);
                    Pointer<PointerDownEvent>(row, EventType.MouseDown, 1);
                    Assert.That(panelRoot.Query<AdMenu>().ToList(), Is.Empty, "an Advanced choice: Simple offers nothing");
                }
                finally
                {
                    foreach (var left in panelRoot.Query<AdMenu>().ToList()) left.Close(false);
                }
            });
        }

        [Test]
        public void ARightClickInsideANumberFieldKeepsItsOwnMenu()
        {
            var menu = a.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.materials.Add(new MaterialTarget { renderer = a.Body, slot = 1, material = a.MaterialB });
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(hat.id);
                LayoutProbe.SettleLists(view);
                var row = view.Detail.Q<TargetListView>("ad-materials").Query(className: "ad-target").ToList().First(r => r.userData is int);
                var input = row.Q<IntegerField>("slot").Q(className: TextInputBaseField<int>.inputUssClassName);
                var panelRoot = view.panel.visualTree;
                try
                {
                    Assert.That(input, Is.Not.Null);
                    Pointer<PointerDownEvent>(input, EventType.MouseDown, 1);
                    Assert.That(panelRoot.Query<AdMenu>().ToList(), Is.Empty, "the number box keeps its own Cut, Copy and Paste");
                    if (Application.platform != RuntimePlatform.OSXEditor)
                    {
                        using (var e = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, modifiers = EventModifiers.Control, mousePosition = row.worldBound.center }))
                        {
                            e.target = row;
                            row.SendEvent(e);
                        }
                        Assert.That(panelRoot.Query<AdMenu>().ToList(), Is.Empty, "Control and click is a right click only on a Mac");
                    }
                    Pointer<PointerDownEvent>(row, EventType.MouseDown, 1);
                    Assert.That(panelRoot.Q<AdMenu>(), Is.Not.Null, "beside the fields the row still has its menu");
                }
                finally
                {
                    foreach (var left in panelRoot.Query<AdMenu>().ToList()) left.Close(false);
                }
            });
        }

        [Test]
        public void EveryKindOfTargetRowOffersPickByHand()
        {
            var menu = a.AddMenu();
            var all = menu.AddEntry(EntryKind.Toggle, "Everything");
            all.objects.Add(new ObjectTarget { target = a.Hat });
            all.blendShapes.Add(new BlendShapeTarget { renderer = a.Body, shape = "Blush" });
            all.materials.Add(new MaterialTarget { renderer = a.Body, slot = 0, material = a.MaterialB });
            all.properties.Add(new MaterialPropertyTarget { renderer = a.Body, property = "_Glossiness" });
            all.animations.Add(new AnimationTarget { pathRoot = a.Glasses.transform });
            all.follows.Add(new FollowTarget { target = a.Glasses, follows = a.Hat.transform });
            menu.pickByHand.Add(a.Body.gameObject);
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            view.Tree.SelectEntry(all.id);

            List<(string, string, bool)> Offered(string list) => view.Detail.Q<TargetListView>(list).RowMenu(0).Select(i => (i.Text, i.Note, i.Checked)).ToList();
            var mark = English("ad.ui.copy_to.by_hand");
            Assert.That(Offered("ad-objects"), Is.EqualTo(new[] { (mark, "Hat", false) }));
            Assert.That(Offered("ad-blendshapes"), Is.EqualTo(new[] { (mark, "Body", true) }), "the mesh's object, ticked while it is marked");
            Assert.That(Offered("ad-materials"), Is.EqualTo(new[] { (mark, "Body", true) }));
            Assert.That(Offered("ad-properties"), Is.EqualTo(new[] { (mark, "Body", true) }));
            Assert.That(Offered("ad-animations"), Is.EqualTo(new[] { (mark, "Glasses", false) }), "the path root");
            Assert.That(Offered("ad-follows"), Is.EqualTo(new[] { (mark, "Glasses", false), (mark, "Hat", false) }), "both objects of a follow");

            menu.locked = true;
            Assert.That(view.Detail.Q<TargetListView>("ad-objects").RowMenu(0), Is.Empty, "a locked component offers no edit");
        }

        [Test]
        public void TheCopyRowAndThePickByHandListFitAt300And400InEveryLanguage()
        {
            var menu = a.AddMenu();
            menu.pickByHand.Add(a.Hat);
            menu.pickByHand.Add(Sibling(a.Hat, "Earring with a name far too long for a narrow inspector"));
            menu.pickByHand.Add(null);
            var allow = PanelAuditTests.StandardAllowList()
                .Allow(e => e.name == "ad-problems-arrow" || e.name == "ad-status-glyph", "the status row's own leading parts, before its spacer")
                .Allow(e => e.name == "ad-ready-show" || e.name == "ad-summary-toggle", "the status row's own trailing actions, after its spacer")
                .Allow(e => e.name == "ad-settings-fold-dot", "the fold's own dot, inside the foldout's own toggle row")
                .Allow(e => e.ClassListContains("ad-row") && e.Q("ad-install-mode") != null,
                    "the Install as row, older than this change: its dropdown grows to 24 px in Chinese at 300 px");
            foreach (var language in new[] { "en-US", "ja-JP", "zh-Hans" })
            foreach (float width in new[] { 300f, 400f })
            using (L.UseTestLanguage(language, null))
            {
                var view = new AvatarDoctorView(new SerializedObject(menu), menu);
                LayoutProbe.Measure(view, () =>
                {
                    if (!view.Header.SettingsFoldOpen) view.Header.ToggleSettingsFold();
                    LayoutProbe.Validate(view);
                    try
                    {
                        PanelAudit.Audit(view.Header, view.Header.worldBound.xMax, allow);
                    }
                    catch (AssertionException e)
                    {
                        var rows = view.Header.Q("ad-settings-fold-content").Children().Where(r => r.ClassListContains("ad-row"))
                            .Select(r => (r.name.Length > 0 ? r.name : (r.Children().FirstOrDefault() as TextElement)?.text ?? r.Children().FirstOrDefault()?.GetType().Name) + "=" + r.worldBound.height);
                        Assert.Fail(language + " at " + width + ": " + e.Message + "\nrows: " + string.Join(", ", rows));
                    }
                    var names = view.Header.Q("ad-by-hand-list").Query<Label>(className: "ad-by-hand-name").ToList().Select(l => l.text).ToList();
                    Assert.That(names.Count, Is.EqualTo(3));
                    Assert.That(names[2], Is.EqualTo(L.Text("ad.ui.copy_to.by_hand_gone")), "a deleted object still has its row, to take it off");
                    var texts = new TextElement[] { view.Header.Q("ad-copy-row").Q<Label>(), view.Header.Q<Button>("ad-copy-open"), view.Header.Q("ad-by-hand-row-0").Q<Label>() };
                    foreach (var text in texts)
                    {
                        var size = text.MeasureTextSize(text.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                        Assert.That(text.contentRect.width, Is.GreaterThanOrEqualTo(size.x - 1f), language + " at " + width + ": \"" + text.text + "\" is cut short");
                    }
                }, width);
            }
        }

        [Test]
        public void TheObjectsToPickByHandAreListedInTheSettingsAndEachCanBeTakenOff()
        {
            var menu = a.AddMenu();
            menu.pickByHand.Add(a.Hat);
            menu.pickByHand.Add(a.Glasses);
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            view.SetMode(ViewMode.Simple);
            var list = view.Header.Q("ad-by-hand-list");
            Assert.That(list, Is.Not.Null);
            Assert.That(view.Header.Q("ad-settings-fold-header").ClassListContains("ad-hidden"), Is.False, "a marked object is a setting the creator made, so Simple shows the fold");
            Assert.That(list.Query<Label>(className: "ad-by-hand-name").ToList().Select(l => l.text), Is.EqualTo(new[] { "Hat", "Glasses" }));

            Undo.IncrementCurrentGroup();
            UiTestEvents.SimulateClick(view.Header.Q<Button>("ad-by-hand-remove-0"));
            Assert.That(menu.pickByHand, Is.EqualTo(new[] { a.Glasses }));
            Assert.That(view.Header.Q("ad-by-hand-list").Query<Label>(className: "ad-by-hand-name").ToList().Select(l => l.text), Is.EqualTo(new[] { "Glasses" }));
            Undo.PerformUndo();
            Assert.That(menu.pickByHand, Is.EqualTo(new[] { a.Hat, a.Glasses }), "one Undo puts it back");

            menu.locked = true;
            view.Refresh();
            Assert.That(view.Header.Q<Button>("ad-by-hand-remove-0"), Is.Null, "a locked component lists them without the way to change them");
            Assert.That(MenuCopy.SetByHand(menu, a.Hat, false), Is.False);
            Assert.That(menu.pickByHand.Count, Is.EqualTo(2));
        }

        // ---- the checks ------------------------------------------------------------------------------------------

        [Test]
        public void TheChecksNameAShapeASlotAndAPropertyTheOtherAvatarLacks()
        {
            var plain = Made(ShaderUtil.CreateShaderAsset(PlainShaderSource, false));
            var bMesh = Made(TestAvatar.QuadWithShapes("Blush"));
            b.Body.sharedMesh = bMesh;
            var plainMaterial = Made(new Material(plain) { name = "Plain" });
            b.Body.sharedMaterials = new[] { plainMaterial };
            var menu = a.AddMenu();
            var face = menu.AddEntry(EntryKind.Toggle, "Face");
            face.blendShapes.Add(new BlendShapeTarget { renderer = a.Body, shape = "Eye_Big" });
            face.blendShapes.Add(new BlendShapeTarget { renderer = a.Body, shape = "Blush" });
            face.materials.Add(new MaterialTarget { renderer = a.Body, slot = 1, material = plainMaterial });
            face.properties.Add(new MaterialPropertyTarget { renderer = a.Body, property = "_Glossiness" });
            var wink = menu.AddEntry(EntryKind.Trigger, "Wink");
            wink.source = TriggerSource.BlendShape;
            wink.sourceRenderer = a.Body;
            wink.sourceShape = "Eye_Big";
            wink.objects.Add(new ObjectTarget { target = a.Hat });

            var plan = MenuCopy.Plan(Draft(menu));

            var checks = plan.Checks.Select(c => (c.Kind, c.Renderer, c.Value, string.Join(",", c.Uses))).ToList();
            Assert.That(checks, Is.EquivalentTo(new[]
            {
                (CopyCheckKind.Shape, (Renderer)b.Body, "Eye_Big", "Face,Wink"),
                (CopyCheckKind.Slot, (Renderer)b.Body, "1", "Face"),
                (CopyCheckKind.Property, (Renderer)b.Body, "_Glossiness", "Face"),
            }));

            var window = Window(Draft(menu));
            try
            {
                var lines = Enumerable.Range(0, 3).Select(i => window.rootVisualElement.Q("ad-copy-check-" + i)?.Q<Label>()?.text).ToList();
                Assert.That(lines, Has.Member(string.Format(English("ad.ui.copy_to.check.shape"), "\"Body\"", "\"Eye_Big\"", "\"Face\", \"Wink\"")));
                Assert.That(lines, Has.Member(string.Format(English("ad.ui.copy_to.check.slot"), "\"Body\"", "1", "\"Face\"")));
                Assert.That(lines, Has.Member(string.Format(English("ad.ui.copy_to.check.property"), "\"Body\"", "\"_Glossiness\"", "\"Face\"")));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }

            b.Body.sharedMesh = a.Mesh;
            b.Body.sharedMaterials = new[] { a.MaterialA, a.MaterialA };
            Assert.That(MenuCopy.Plan(Draft(menu)).Checks, Is.Empty, "the same mesh and materials: nothing to say");
        }

        // ---- replace or add another ------------------------------------------------------------------------------

        [Test]
        public void ReplaceKeepsTheIdsAndTheComponentItself()
        {
            var menu = a.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = a.Hat });
            var existing = b.AddMenu();
            existing.AddEntry(EntryKind.Toggle, "Old");

            var draft = Draft(menu);
            var plan = MenuCopy.Plan(draft);
            Assert.That(plan.Existing, Is.EqualTo(new[] { existing }));
            Assert.That(plan.Where, Is.EqualTo(CopyWhere.Replace), "one component there: Replace is the default");

            var copy = Copy(draft);

            Assert.That(copy, Is.SameAs(existing), "the component itself stays, so what refers to it still does");
            Assert.That(b.Root.GetComponents<AvatarDoc>().Length, Is.EqualTo(1));
            Assert.That(copy.entries.Select(e => (e.id, e.name)), Is.EqualTo(new[] { (hat.id, "Hat") }), "the source's entries with the source's ids");
            Assert.That(copy.entries[0].objects[0].target, Is.SameAs(b.Hat));
        }

        [Test]
        public void AddAnotherRenewsEveryIdAndEveryLinkToThem()
        {
            var menu = a.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            var jacket = menu.AddEntry(EntryKind.Toggle, "Jacket", outfits.id);
            var hoodie = menu.AddEntry(EntryKind.Toggle, "Hoodie", outfits.id);
            var allow = menu.AddEntry(EntryKind.Toggle, "Allow");
            var swap = menu.AddEntry(EntryKind.Trigger, "Swap");
            swap.source = TriggerSource.Toggle;
            swap.sourceToggleId = jacket.id;
            swap.gateToggleId = allow.id;
            swap.when.Add(new ConditionGroup { all = { new WhenCondition { kind = WhenConditionKind.Toggle, sourceToggleId = jacket.id } } });
            swap.when.Add(new ConditionGroup { all = { new WhenCondition { kind = WhenConditionKind.Toggle, sourceToggleId = hoodie.id } } });
            swap.objects.Add(new ObjectTarget { target = a.Hat });
            var boop = menu.AddEntry(EntryKind.Trigger, "Boop");
            boop.source = TriggerSource.Contact;
            boop.touchMode = TouchMode.FlipToggle;
            boop.flipToggleId = hoodie.id;
            boop.contactAnchor = a.Root.transform;
            var casual = menu.AddEntry(EntryKind.Preset, "Casual");
            casual.presetStates.Add(new PresetState { entryId = hoodie.id, on = true });
            casual.presetStates.Add(new PresetState { entryId = jacket.id, on = false });
            var edit = MenuEdit.Create(new[] { "RootToggle" });
            edit.hide = true;
            menu.menuEdits.Add(edit);
            menu.removedLayers.Add(new RemovedLayer { layer = "Old layer", editId = edit.id });
            menu.animators.Add(AnimatorMerge.Create(null));
            menu.menuAssets.Add(MenuAssetInstall.Create(null));
            var existing = b.AddMenu();
            existing.AddEntry(EntryKind.Toggle, "Old");
            var before = EditorJsonUtility.ToJson(menu);
            var sourceIds = Ids(menu).ToList();

            var draft = Draft(menu);
            MenuCopy.SetWhere(draft, CopyWhere.AddAnother);
            var copy = Copy(draft);

            Assert.That(copy, Is.Not.SameAs(existing));
            Assert.That(b.Root.GetComponents<AvatarDoc>(), Is.EqualTo(new[] { existing, copy }), "added beside the one there");
            Assert.That(existing.entries.Single().name, Is.EqualTo("Old"), "which is left as it was");
            var copyIds = Ids(copy).ToList();
            Assert.That(copyIds.Count, Is.EqualTo(sourceIds.Count));
            Assert.That(copyIds.Intersect(sourceIds), Is.Empty, "every entry, menu edit, merged animator and menu asset has a new id");
            Assert.That(copyIds.Distinct().Count(), Is.EqualTo(copyIds.Count));

            string New(MenuEntry entry) => copy.entries[menu.entries.IndexOf(entry)].id;
            MenuEntry Copied(MenuEntry entry) => copy.entries[menu.entries.IndexOf(entry)];
            Assert.That(Copied(jacket).parentId, Is.EqualTo(New(outfits)), "a parent");
            Assert.That(Copied(swap).sourceToggleId, Is.EqualTo(New(jacket)), "a trigger's toggle");
            Assert.That(Copied(swap).gateToggleId, Is.EqualTo(New(allow)), "a gate");
            Assert.That(Copied(swap).when.Select(g => g.all[0].sourceToggleId), Is.EqualTo(new[] { New(jacket), New(hoodie) }), "the conditions of every group");
            Assert.That(Copied(boop).flipToggleId, Is.EqualTo(New(hoodie)), "a flip target");
            Assert.That(Copied(casual).presetStates.Select(s => s.entryId), Is.EqualTo(new[] { New(hoodie), New(jacket) }), "a preset's states");
            Assert.That(copy.removedLayers.Single().editId, Is.EqualTo(copy.menuEdits.Single().id), "a removed layer's menu edit");
            Assert.That(EditorJsonUtility.ToJson(menu), Is.EqualTo(before), "the source is never changed");
        }

        [Test]
        public void ACopyWhereNothingIsKeepsTheIdsUnlessThatAvatarUsesOneAlready()
        {
            var menu = a.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = a.Hat });
            var casual = menu.AddEntry(EntryKind.Preset, "Casual");
            casual.presetStates.Add(new PresetState { entryId = hat.id });

            var kept = Copy(Draft(menu));
            Assert.That(kept.entries.Select(e => e.id), Is.EqualTo(new[] { hat.id, casual.id }),
                "a preset of another component of that avatar can still name these entries");
            Object.DestroyImmediate(kept);

            var other = b.AddMenu(b.Glasses);
            other.AddEntry(EntryKind.Toggle, "Glasses").id = hat.id;
            var renewed = Copy(Draft(menu));
            Assert.That(renewed.entries.Select(e => e.id).Intersect(new[] { hat.id, casual.id }), Is.Empty, "two components of one avatar never share an id");
            Assert.That(renewed.entries[1].presetStates[0].entryId, Is.EqualTo(renewed.entries[0].id));
        }

        [Test]
        public void TheCopyIsOneUndoStepAndLeavesTheSourceAndTheSelectionAlone()
        {
            var menu = a.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = a.Hat });
            var before = EditorJsonUtility.ToJson(menu);
            var selection = Selection.objects;

            Undo.IncrementCurrentGroup();
            Copy(Draft(menu));
            Assert.That(b.Root.GetComponent<AvatarDoc>(), Is.Not.Null);
            Assert.That(Selection.objects, Is.EqualTo(selection), "the selection stays as it was");
            Assert.That(EditorJsonUtility.ToJson(menu), Is.EqualTo(before), "the source stays as it was");
            Undo.PerformUndo();
            Assert.That(b.Root.GetComponent<AvatarDoc>(), Is.Null, "one Undo takes the added copy away");

            var existing = b.AddMenu();
            existing.AddEntry(EntryKind.Toggle, "Old");
            Undo.IncrementCurrentGroup();
            Copy(Draft(menu));
            Assert.That(existing.entries.Select(e => e.name), Is.EqualTo(new[] { "Hat" }));
            Undo.PerformUndo();
            Assert.That(existing.entries.Select(e => e.name), Is.EqualTo(new[] { "Old" }), "and one Undo puts a replaced one back");
            Assert.That(EditorJsonUtility.ToJson(menu), Is.EqualTo(before));
        }

        [Test]
        public void ALockedComponentThereRefusesReplace()
        {
            var menu = a.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = a.Hat });
            var existing = b.AddMenu();
            existing.AddEntry(EntryKind.Toggle, "Old");
            existing.locked = true;

            var draft = Draft(menu);
            Assert.That(MenuCopy.Apply(draft, out var status), Is.Null);
            Assert.That(status, Is.EqualTo(English("ad.ui.copy_to.locked")));
            Assert.That(existing.entries.Select(e => e.name), Is.EqualTo(new[] { "Old" }));

            var window = Window(Draft(menu));
            try
            {
                Assert.That(window.rootVisualElement.Q("ad-copy-locked"), Is.Not.Null, "the window says why");
                Assert.That(window.rootVisualElement.Q<Button>("ad-copy-run").enabledSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }

            MenuCopy.SetWhere(draft, CopyWhere.AddAnother);
            var copy = Copy(draft);
            Assert.That(copy, Is.Not.SameAs(existing), "another component is still added");
        }

        [Test]
        public void APrefabInstanceGetsTheCopyAsAnOverride()
        {
            var folder = TempAssetFolder.Create("AvatarDocCopyTests");
            var prefabPath = folder + "/Avatar.prefab";
            GameObject source = null, instance = null;
            try
            {
                source = new GameObject("PrefabAvatar");
                source.AddComponent<VRCAvatarDescriptor>();
                Sibling(source, "Hat");
                Sibling(source, "MenuAddon").AddComponent<AvatarDoc>();
                var prefab = PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
                Object.DestroyImmediate(source);
                source = null;
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                avatars.Add(instance.transform);
                var addon = At(a, "MenuAddon").AddComponent<AvatarDoc>();
                addon.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = a.Hat });
                var rootMenu = a.AddMenu();
                rootMenu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = a.Hat });

                Undo.IncrementCurrentGroup();
                var replaced = Copy(new CopyDraft { source = addon, target = instance.transform });
                var added = Copy(new CopyDraft { source = rootMenu, target = instance.transform });

                Assert.That(replaced, Is.SameAs(instance.transform.Find("MenuAddon").GetComponent<AvatarDoc>()));
                Assert.That(replaced.entries[0].objects[0].target, Is.SameAs(instance.transform.Find("Hat").gameObject));
                var modifications = PrefabUtility.GetPropertyModifications(instance) ?? new PropertyModification[0];
                Assert.That(modifications.Any(m => m.target is AvatarDoc && m.propertyPath != null && m.propertyPath.StartsWith("entries")), Is.True,
                    "the replaced component's data is an override on the instance");
                Assert.That(PrefabUtility.GetAddedComponents(instance).Select(c => c.instanceComponent), Has.Member(added),
                    "the added component is an added component of the instance");
                Assert.That(prefab.transform.Find("MenuAddon").GetComponent<AvatarDoc>().entries, Is.Empty, "the prefab asset stays as it was");
                Assert.That(prefab.GetComponents<AvatarDoc>(), Is.Empty);

                Undo.PerformUndo();
                Undo.PerformUndo();
                Assert.That(PrefabUtility.GetAddedComponents(instance), Is.Empty, "Undo takes the added component off the instance");
                Assert.That(instance.transform.Find("MenuAddon").GetComponent<AvatarDoc>().entries, Is.Empty, "and the replaced data back");
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                if (source != null) Object.DestroyImmediate(source);
                AssetDatabase.DeleteAsset(prefabPath);
                TempAssetFolder.Delete(folder);
            }
        }

        // ---- a round trip ----------------------------------------------------------------------------------------

        [Test]
        public void ARoundTripBuildsTheSameFxOnBothVersions() => RoundTrip(newIds: false);

        [Test]
        public void ARoundTripWithNewIdsBuildsTheSameFxToo() => RoundTrip(newIds: true);

        /// <summary>Copies an FT style menu onto a duplicate of its avatar and builds both. newIds adds the copy beside an empty AvatarDoc with Add another, which gives it new ids.</summary>
        void RoundTrip(bool newIds)
        {
            GeneratedAssets.Note();
            GameObject versionB = null, builtA = null, builtB = null;
            using (var avatar = DocsAvatar.Create())
            try
            {
                var menu = avatar.AddMenu();
                menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
                var smile = menu.AddEntry(EntryKind.Toggle, "Smile");
                smile.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Smile", weight = 100f });
                smile.shapeSets.Add(new ShapeSetRule { mesh = avatar.Face, zeroOthers = true, value = ShapeSetValue.Default });
                var blush = menu.AddEntry(EntryKind.Radial, "Blush");
                blush.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Blush", minWeight = 0f, maxWeight = 100f });
                var jacket = menu.AddEntry(EntryKind.Toggle, "Jacket");
                jacket.exclusiveGroup = "Outfit";
                jacket.groupValue = 1;
                jacket.objects.Add(new ObjectTarget { target = avatar.Jacket });
                var hoodie = menu.AddEntry(EntryKind.Toggle, "Hoodie");
                hoodie.exclusiveGroup = "Outfit";
                hoodie.groupValue = 2;
                hoodie.objects.Add(new ObjectTarget { target = avatar.Hoodie });
                var puff = menu.AddEntry(EntryKind.Trigger, "Puff");
                puff.source = TriggerSource.BlendShape;
                puff.sourceRenderer = avatar.Face;
                puff.sourceShape = "Smile";
                puff.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Cheek_Puff", mode = ShapeEffectMode.Follow, multiplier = 1f });
                var pat = menu.AddEntry(EntryKind.Trigger, "Headpat");
                pat.source = TriggerSource.Contact;
                pat.contactAnchor = avatar.HeadContact;
                pat.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Smile_Eyes", weight = 100f });
                var hair = menu.AddEntry(EntryKind.Trigger, "Hair pull");
                hair.source = TriggerSource.PhysBone;
                hair.physBone = avatar.HairBone;
                hair.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Brow_Angry", weight = 100f });
                var shiny = menu.AddEntry(EntryKind.Toggle, "Shiny");
                shiny.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", value = 1f });
                var casual = menu.AddEntry(EntryKind.Preset, "Casual");
                casual.presetStates.Add(new PresetState { entryId = hoodie.id, on = true });

                versionB = Object.Instantiate(avatar.Root);
                versionB.name = "Avatar (4)";
                Object.DestroyImmediate(versionB.GetComponent<AvatarDoc>());
                avatars.Clear();
                avatars.Add(avatar.Root.transform);
                avatars.Add(versionB.transform);

                if (newIds) versionB.AddComponent<AvatarDoc>();
                var draft = new CopyDraft { source = menu, target = versionB.transform };
                if (newIds) MenuCopy.SetWhere(draft, CopyWhere.AddAnother);
                Assert.That(MenuCopy.Plan(draft).Listed, Is.Empty, "a duplicate has every object");
                var copy = Copy(draft);
                if (newIds) Assert.That(copy.entries.Select(e => e.id).Intersect(menu.entries.Select(e => e.id)), Is.Empty);
                else Assert.That(copy.entries.Select(e => e.id), Is.EqualTo(menu.entries.Select(e => e.id)));

                // The test avatar's parameters are an in-memory asset, which NDMF counts as the build's own and writes into
                // in place; both versions share it, as two copies of one avatar share their assets. A saved asset is
                // cloned instead, so each build starts from the parameters the avatar has.
                var parameters = avatar.Test.ExpressionParameters;
                var unbuilt = parameters.parameters.ToArray();
                builtA = AvatarProcessor.ManualProcessAvatar(avatar.Root);
                parameters.parameters = unbuilt;
                builtB = AvatarProcessor.ManualProcessAvatar(versionB);
                var dumpA = Dump(builtA);
                var dumpB = Dump(builtB);
                Assert.That(dumpA, Does.Contain("layer AD: Hat"), "the dump reads the built layers");
                Assert.That(dumpB, Is.EqualTo(dumpA), "both versions build the same FX, parameters and menu");
            }
            finally
            {
                if (builtA != null && builtA != avatar.Root) Object.DestroyImmediate(builtA);
                if (builtB != null && builtB != versionB) Object.DestroyImmediate(builtB);
                if (versionB != null) Object.DestroyImmediate(versionB);
                GeneratedAssets.CleanUp();
            }
        }

        /// <summary>The built FX controller, the expression parameters and the menu as text: every layer, state, transition, clip curve and blend tree, in order.</summary>
        internal static string Dump(GameObject built)
        {
            var descriptor = built.GetComponent<VRCAvatarDescriptor>();
            var fx = (AnimatorController)descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            var text = new StringBuilder();
            foreach (var p in fx.parameters) text.AppendLine("param " + p.name + " " + p.type + " " + p.defaultBool + " " + p.defaultFloat + " " + p.defaultInt);
            foreach (var layer in fx.layers)
            {
                text.AppendLine("layer " + layer.name + " " + layer.defaultWeight + " " + layer.blendingMode);
                DumpMachine(layer.stateMachine, text, "  ");
            }
            foreach (var p in descriptor.expressionParameters.parameters)
                text.AppendLine("expression " + p.name + " " + p.valueType + " " + p.defaultValue + " " + p.saved + " " + p.networkSynced);
            DumpMenu(descriptor.expressionsMenu, text, "", new HashSet<VRCExpressionsMenu>());
            foreach (var receiver in built.GetComponentsInChildren<VRCContactReceiver>(true))
                text.AppendLine("receiver " + MenuCopy.PathOf(built.transform, receiver.transform) + " " + receiver.parameter + " " + receiver.receiverType);
            return text.ToString();
        }

        static void DumpMachine(AnimatorStateMachine machine, StringBuilder text, string indent)
        {
            foreach (var child in machine.states)
            {
                var state = child.state;
                text.AppendLine(indent + "state " + state.name + " wd " + state.writeDefaultValues + " behaviours " + string.Join(",", state.behaviours.Select(x => x.GetType().Name)));
                DumpMotion(state.motion, text, indent + "  ");
                foreach (var transition in state.transitions)
                    text.AppendLine(indent + "  to " + (transition.destinationState != null ? transition.destinationState.name : "exit") + " "
                        + string.Join(",", transition.conditions.Select(c => c.parameter + c.mode + c.threshold)));
            }
            foreach (var transition in machine.anyStateTransitions)
                text.AppendLine(indent + "any to " + (transition.destinationState != null ? transition.destinationState.name : "") + " "
                    + string.Join(",", transition.conditions.Select(c => c.parameter + c.mode + c.threshold)));
            foreach (var child in machine.stateMachines) DumpMachine(child.stateMachine, text, indent + "  ");
        }

        static void DumpMotion(Motion motion, StringBuilder text, string indent)
        {
            if (motion is AnimationClip clip)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip).OrderBy(x => x.path + x.propertyName))
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    text.AppendLine(indent + "curve " + binding.path + " " + binding.type.Name + " " + binding.propertyName + " "
                        + string.Join(",", curve.keys.Select(k => k.time + ":" + k.value)));
                }
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip).OrderBy(x => x.path + x.propertyName))
                    text.AppendLine(indent + "swap " + binding.path + " " + binding.propertyName + " "
                        + string.Join(",", AnimationUtility.GetObjectReferenceCurve(clip, binding).Select(k => k.value != null ? k.value.name : "none")));
            }
            else if (motion is BlendTree tree)
            {
                text.AppendLine(indent + "tree " + tree.blendType + " " + tree.blendParameter);
                foreach (var child in tree.children)
                {
                    text.AppendLine(indent + "  child " + child.threshold + " " + child.directBlendParameter);
                    DumpMotion(child.motion, text, indent + "    ");
                }
            }
        }

        static void DumpMenu(VRCExpressionsMenu menu, StringBuilder text, string indent, HashSet<VRCExpressionsMenu> seen)
        {
            if (menu == null || !seen.Add(menu)) return;
            foreach (var control in menu.controls)
            {
                text.AppendLine(indent + "control " + control.name + " " + control.type + " " + control.parameter?.name + " " + control.value);
                if (control.subMenu != null) DumpMenu(control.subMenu, text, indent + "  ", seen);
            }
        }


        // ---- what the review asked for -------------------------------------------------------------------------

        [Test]
        public void AnObjectUsedAsItselfAndAsItsMeshIsOnePick()
        {
            var earring = Sibling(a.Hat, "Earring");
            var mesh = earring.AddComponent<SkinnedMeshRenderer>();
            mesh.sharedMesh = a.Mesh;
            var menu = a.AddMenu();
            var toggle = menu.AddEntry(EntryKind.Toggle, "Earring");
            toggle.objects.Add(new ObjectTarget { target = earring });
            toggle.blendShapes.Add(new BlendShapeTarget { renderer = mesh, shape = "Blush" });
            var draft = Draft(menu);

            var row = MenuCopy.Plan(draft).Listed.Single();
            Assert.That(row.Parts, Is.EquivalentTo(new Object[] { earring, mesh }), "one row for the object");
            Assert.That(row.FieldType, Is.EqualTo(typeof(GameObject)));
            MenuCopy.Pick(draft, earring, b.Body.gameObject);
            var copy = Copy(draft);
            Assert.That(copy.entries[0].objects[0].target, Is.SameAs(b.Body.gameObject));
            Assert.That(copy.entries[0].blendShapes[0].renderer, Is.SameAs(b.Body), "its mesh from the same pick");
        }

        [Test]
        public void APathRootAndAHoldTheRestMeshMustBePickedBeforeCopying()
        {
            var clips = Sibling(a.Hat, "Clips");
            var face = Sibling(a.Hat, "Face").AddComponent<SkinnedMeshRenderer>();
            var bow = Sibling(a.Hat, "Bow");
            var menu = a.AddMenu();
            var smile = menu.AddEntry(EntryKind.Toggle, "Smile");
            smile.objects.Add(new ObjectTarget { target = bow });
            smile.animations.Add(new AnimationTarget { pathRoot = clips.transform });
            smile.shapeSets.Add(new ShapeSetRule { mesh = face, zeroOthers = true });
            var draft = Draft(menu);

            var plan = MenuCopy.Plan(draft);
            Assert.That(plan.Listed.Select(r => (r.Path, r.Need)), Is.EquivalentTo(new[]
            {
                ("Hat/Bow", CopyNeed.None), ("Hat/Clips", CopyNeed.PathRoot), ("Hat/Face", CopyNeed.Required),
            }), "an empty path root means the avatar's root, and an empty Hold the rest mesh is an error");
            Assert.That(plan.Blocked, Is.True);
            Assert.That(MenuCopy.Apply(draft, out var status), Is.Null);
            Assert.That(status, Is.EqualTo(English("ad.ui.copy_to.run.blocked")));
            Assert.That(b.Root.GetComponent<AvatarDoc>(), Is.Null);

            var window = Window(Draft(menu));
            try
            {
                var root = window.rootVisualElement;
                Assert.That(root.Query<Label>(className: "ad-copy-needs").ToList().Select(l => l.text),
                    Is.EquivalentTo(new[] { English("ad.ui.copy_to.needs.path_root"), English("ad.ui.copy_to.needs.required") }));
                Assert.That(root.Q<Button>("ad-copy-run").enabledSelf, Is.False);
                Assert.That(root.Q<Label>("ad-copy-blocked").text, Is.EqualTo(English("ad.ui.copy_to.run.blocked")));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }

            MenuCopy.Pick(draft, clips, b.Hat);
            MenuCopy.Pick(draft, face.gameObject, b.Body);
            Assert.That(MenuCopy.Plan(draft).Blocked, Is.False);
            var copy = Copy(draft);
            Assert.That(copy.entries[0].animations[0].pathRoot, Is.SameAs(b.Hat.transform));
            Assert.That(copy.entries[0].shapeSets[0].mesh, Is.SameAs(b.Body));
            Assert.That(copy.entries[0].objects[0].target, Is.Null, "an object row may stay empty: the build skips it with a message");
        }

        [Test]
        public void UndoingAnAddedCopyPutsTheChoiceBack()
        {
            var menu = a.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = a.Hat });
            var existing = b.AddMenu();
            var window = Window(Draft(menu));
            try
            {
                window.rootVisualElement.Q<SegmentedChoice>("ad-copy-where").Pick((int)CopyWhere.AddAnother);
                Undo.IncrementCurrentGroup();
                var copy = window.Copy();
                Assert.That(copy, Is.Not.Null.And.Not.SameAs(existing), window.Status);
                Assert.That(window.rootVisualElement.Q<SegmentedChoice>("ad-copy-where").Index, Is.EqualTo((int)CopyWhere.Replace), "a second press overwrites the copy just made");

                Undo.PerformUndo();
                Assert.That(b.Root.GetComponents<AvatarDoc>(), Is.EqualTo(new[] { existing }));
                Assert.That(MenuCopy.Plan(window.Draft).Where, Is.EqualTo(CopyWhere.AddAnother), "the copy is gone, so the choice from before it is back");
                window.Init(window.Draft);
                Assert.That(window.rootVisualElement.Q<SegmentedChoice>("ad-copy-where").Index, Is.EqualTo((int)CopyWhere.AddAnother));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void UseInsteadTakesOnlyObjectsOfTheOtherAvatar()
        {
            var earring = Sibling(a.Hat, "Earring");
            var menu = a.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Earring").objects.Add(new ObjectTarget { target = earring });
            var draft = Draft(menu);
            Assert.That(MenuCopy.Pick(draft, earring, a.Glasses), Is.False, "an object of the avatar it comes from");
            Assert.That(MenuCopy.Pick(draft, earring, Made(new GameObject("Loose"))), Is.False, "an object of no avatar");
            Assert.That(draft.picks, Is.Empty);
            Assert.That(MenuCopy.Pick(draft, earring, b.Glasses), Is.True);

            var window = Window(Draft(menu));
            try
            {
                LayoutProbe.Measure(window.rootVisualElement, () =>
                {
                    var field = window.rootVisualElement.Q<ObjectField>("ad-copy-pick-0");
                    field.value = a.Glasses;
                    Assert.That(window.Draft.picks, Is.Empty, "refused");
                    Assert.That(field.value, Is.Null, "the field goes back to what it held");
                    Assert.That(window.Status, Is.EqualTo(string.Format(English("ad.ui.copy_to.pick_outside"), "\"Glasses\"", "\"TestAvatar B\"")));
                });
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void TheSettingsButtonIsGreyedOutOnAPrefabAsset()
        {
            var folder = TempAssetFolder.Create("AvatarDocCopyTests");
            var path = folder + "/Asset.prefab";
            GameObject source = null;
            try
            {
                source = new GameObject("PrefabAvatar");
                source.AddComponent<VRCAvatarDescriptor>();
                source.AddComponent<AvatarDoc>();
                var prefab = PrefabUtility.SaveAsPrefabAsset(source, path);
                Object.DestroyImmediate(source);
                source = null;
                var asset = prefab.GetComponent<AvatarDoc>();
                var view = new AvatarDoctorView(new SerializedObject(asset), asset);
                Assert.That(view.Header.Q<Button>("ad-copy-open").enabledSelf, Is.False, "a prefab asset is copied from its instance in a scene");
                var menu = a.AddMenu();
                Assert.That(new AvatarDoctorView(new SerializedObject(menu), menu).Header.Q<Button>("ad-copy-open").enabledSelf, Is.True);
            }
            finally
            {
                if (source != null) Object.DestroyImmediate(source);
                AssetDatabase.DeleteAsset(path);
                TempAssetFolder.Delete(folder);
            }
        }

        [Test]
        public void ReplaceGivesNewIdsWhenAnotherComponentThereUsesThem()
        {
            var menu = a.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = a.Hat });
            var existing = b.AddMenu();
            existing.AddEntry(EntryKind.Toggle, "Old");
            Assert.That(MenuCopy.Plan(Draft(menu)).RenewsIds, Is.False, "nothing else there holds the ids");

            var other = b.AddMenu(b.Glasses);
            other.AddEntry(EntryKind.Toggle, "Glasses").id = hat.id;
            var plan = MenuCopy.Plan(Draft(menu));
            Assert.That((plan.Where, plan.RenewsIds), Is.EqualTo((CopyWhere.Replace, true)));
            var copy = Copy(Draft(menu));
            Assert.That(copy, Is.SameAs(existing));
            Assert.That(copy.entries.Single().id, Is.Not.EqualTo(hat.id));
            Assert.That(other.entries.Single().id, Is.EqualTo(hat.id), "the other component keeps its own");
        }

        [Test]
        public void ASecondPressOverwritesTheCopyOfALockedComponent()
        {
            var menu = a.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = a.Hat });
            menu.locked = true;
            var draft = Draft(menu);
            var first = Copy(draft);
            Assert.That(first.locked, Is.True, "the lock comes along");

            var second = MenuCopy.Apply(draft, out var status);
            Assert.That(second, Is.SameAs(first), status);
            Assert.That(status, Is.EqualTo(string.Format(English("ad.ui.copy_to.done.replaced"), "\"TestAvatar B\"")));
            Assert.That(b.Root.GetComponents<AvatarDoc>().Length, Is.EqualTo(1));
        }

        [Test]
        public void TheWalkEntersManagedReferences()
        {
            var probe = ScriptableObject.CreateInstance<CopyWalkProbe>();
            try
            {
                probe.held.target = a.Hat;
                using (var serialized = new SerializedObject(probe))
                    Assert.That(MenuCopy.ObjectFields(serialized).Select(f => f.propertyPath).ToList(), Has.Member("held.target"),
                        "a field added later as a managed reference is walked too");
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }

        [Test]
        public void EveryObjectFieldOfTheRuntimeDataIsFoundOnTheOtherAvatar()
        {
            var menu = a.AddMenu();
            var leaves = new List<(string path, Type type)>();
            Fill(menu, "", leaves, true);
            foreach (var (path, type) in leaves) Leaf(b, path, type);
            Assert.That(leaves.Count, Is.GreaterThanOrEqualTo(15), "every GameObject and Component field of the data, through every list: " + string.Join(", ", leaves.Select(l => l.path)));

            var plan = MenuCopy.Plan(Draft(menu));
            Assert.That(plan.Listed, Is.Empty, "each one is at the same place on the other avatar");
            var copy = Copy(Draft(menu));
            foreach (var (path, type) in leaves)
            {
                var value = Read(copy, path);
                Assert.That(value, Is.SameAs(Leaf(b, path, type)), path + " is the other avatar's own");
            }
        }

        static bool IsLeaf(Type type) => type == typeof(GameObject) || typeof(Component).IsAssignableFrom(type);

        static IEnumerable<FieldInfo> DataFields(Type type) => type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(f => (f.IsPublic || f.GetCustomAttribute<SerializeField>() != null) && f.GetCustomAttribute<NonSerializedAttribute>() == null && !f.IsInitOnly);

        /// <summary>Walks the data the way Unity serializes it, one element in every list. make fills every GameObject or Component field with an object of avatar a named after its path and adds the list elements; otherwise it only collects the paths.</summary>
        void Fill(object holder, string path, List<(string path, Type type)> leaves, bool make)
        {
            foreach (var field in DataFields(holder.GetType()))
            {
                if (field.Name == nameof(AvatarDoc.pickByHand)) continue;
                var type = field.FieldType;
                var at = path + field.Name;
                if (IsLeaf(type))
                {
                    if (make) field.SetValue(holder, Leaf(a, at, type));
                    leaves.Add((at, type));
                    continue;
                }
                if (typeof(Object).IsAssignableFrom(type) || type == typeof(string) || !type.IsClass) continue;
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                {
                    var item = type.GetGenericArguments()[0];
                    if (!item.IsClass || item == typeof(string) || typeof(Object).IsAssignableFrom(item)) continue;
                    var list = (IList)field.GetValue(holder);
                    if (list == null)
                    {
                        if (!make) continue;
                        list = (IList)Activator.CreateInstance(type);
                        field.SetValue(holder, list);
                    }
                    if (list.Count == 0 && make) list.Add(Activator.CreateInstance(item));
                    if (list.Count > 0) Fill(list[0], at + "-0-", leaves, make);
                    continue;
                }
                if (type.GetCustomAttribute<SerializableAttribute>() == null) continue;
                var inner = field.GetValue(holder);
                if (inner == null && make) field.SetValue(holder, inner = Activator.CreateInstance(type));
                if (inner != null) Fill(inner, at + "-", leaves, make);
            }
        }

        /// <summary>The value at a path Fill wrote: field names, with -0- for the first element of a list.</summary>
        static object Read(object holder, string path)
        {
            object at = holder;
            foreach (var part in path.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == "0") { at = ((IList)at)[0]; continue; }
                at = at.GetType().GetField(part, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(at);
            }
            return at;
        }

        /// <summary>The object named path under the avatar's root, with a component of the type a field holds.</summary>
        static Object Leaf(TestAvatar avatar, string path, Type type)
        {
            var go = At(avatar, path);
            if (type == typeof(GameObject)) return go;
            if (type == typeof(Transform)) return go.transform;
            var make = type == typeof(Renderer) ? typeof(MeshRenderer) : type;
            var existing = go.GetComponent(make);
            return existing != null ? existing : go.AddComponent(make);
        }

        // ---- the window and the ways in --------------------------------------------------------------------------

        [Test]
        public void TheWindowListsTheOtherAvatarsAndSaysWhereTheCopyGoes()
        {
            var c = TestAvatar.Create();
            try
            {
                c.Root.name = "TestAvatar C";
                avatars.Add(c.Root.transform);
                var addon = At(a, "MenuAddon").AddComponent<AvatarDoc>();
                At(b, "MenuAddon");
                var draft = MenuCopy.For(addon);
                Assert.That(draft.target, Is.SameAs(b.Root.transform), "the first other avatar");
                var window = Window(draft);
                try
                {
                    var root = window.rootVisualElement;
                    Assert.That(root.Q<Label>("ad-copy-from").text, Is.EqualTo("TestAvatar/MenuAddon"));
                    Assert.That(root.Q<Label>("ad-copy-goes-on").text, Is.EqualTo("TestAvatar B/MenuAddon"), "the same place on the other avatar");
                    Assert.That(root.Q("ad-copy-goes-on-root"), Is.Null);
                    Assert.That(root.Q<SegmentedChoice>("ad-copy-where"), Is.Null, "nothing there yet, so there is nothing to choose");

                    UiTestEvents.SimulateClick(root.Q<Button>("ad-copy-target"));
                    Assert.That(shownMenu.Select(i => i.Text), Is.EqualTo(new[] { "TestAvatar B", "TestAvatar C" }), "every avatar but its own");
                    shownMenu.Single(i => i.Text == "TestAvatar C").Action();

                    root = window.rootVisualElement;
                    Assert.That(root.Q<Button>("ad-copy-target").text, Is.EqualTo("TestAvatar C"));
                    Assert.That(root.Q<Label>("ad-copy-goes-on").text, Is.EqualTo("TestAvatar C"), "no MenuAddon there: the root");
                    Assert.That(root.Q<Label>("ad-copy-goes-on-root").text, Is.EqualTo(string.Format(English("ad.ui.copy_to.goes_on.root"), "\"TestAvatar C\"", "\"MenuAddon\"")));
                }
                finally
                {
                    Object.DestroyImmediate(window);
                }

                avatars.Remove(b.Root.transform);
                avatars.Remove(c.Root.transform);
                var alone = Window(MenuCopy.For(addon));
                try
                {
                    Assert.That(alone.rootVisualElement.Q<Label>("ad-copy-no-target").text, Is.EqualTo(English("ad.ui.copy_to.no_target")));
                    Assert.That(alone.rootVisualElement.Q<Button>("ad-copy-run").enabledSelf, Is.False);
                }
                finally
                {
                    Object.DestroyImmediate(alone);
                }
            }
            finally
            {
                c.Dispose();
            }
        }

        [Test]
        public void TheComponentMenuAndTheSettingsButtonOpenTheWindow()
        {
            var menu = a.AddMenu();
            var item = typeof(MenuCopy).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(m => m.GetCustomAttributes<UnityEditor.MenuItem>()).Where(at => !at.validate).Select(at => at.menuItem).ToList();
            Assert.That(item, Is.EqualTo(new[] { "CONTEXT/AvatarDoc/Copy to another avatar..." }), "on the component's own menu");

            Assert.That(MenuCopy.CanCopyFrom(new MenuCommand(menu)), Is.True);
            MenuCopy.CopyFrom(new MenuCommand(menu));
            Assert.That(opened?.source, Is.SameAs(menu));
            Assert.That(opened.target, Is.SameAs(b.Root.transform));

            var loose = Made(new GameObject("Loose")).AddComponent<AvatarDoc>();
            Assert.That(MenuCopy.CanCopyFrom(new MenuCommand(loose)), Is.False, "a component outside every avatar has no version to copy to");

            opened = null;
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            var button = view.Header.Q<Button>("ad-copy-open");
            Assert.That(button, Is.Not.Null, "a button under the component settings");
            Assert.That(button.text, Is.EqualTo(English("ad.ui.copy_to.open")));
            UiTestEvents.SimulateClick(button);
            Assert.That(opened?.source, Is.SameAs(menu));
        }

        /// <summary>A copy that shows every part of the window: a component already there that is locked and one that is not, an object not found with a suggestion and one without, same-named objects, an object marked Pick by hand when copying and a check.</summary>
        CopyDraft Busy()
        {
            var addon = At(a, "MenuAddon");
            var menu = addon.AddComponent<AvatarDoc>();
            var bAddon = At(b, "MenuAddon");
            bAddon.AddComponent<AvatarDoc>().locked = true;
            bAddon.AddComponent<AvatarDoc>();
            var earringA = Sibling(a.Hat, "Earring");
            Sibling(b.Glasses, "Earring");
            var charmA = Sibling(a.Hat, "Charm with a long name for a narrow window");
            Sibling(a.Root, "Ribbon");
            var ribbonA = Sibling(a.Root, "Ribbon");
            Sibling(b.Root, "Ribbon");
            Sibling(b.Root, "Ribbon");
            Sibling(b.Root, "Ribbon");
            menu.pickByHand.Add(a.Glasses);
            var outfit = menu.AddEntry(EntryKind.Toggle, "Summer outfit with a long name");
            outfit.objects.Add(new ObjectTarget { target = earringA });
            outfit.objects.Add(new ObjectTarget { target = charmA });
            outfit.objects.Add(new ObjectTarget { target = ribbonA });
            outfit.objects.Add(new ObjectTarget { target = a.Glasses });
            outfit.blendShapes.Add(new BlendShapeTarget { renderer = a.Body, shape = "Not on that mesh" });
            var draft = Draft(menu);
            MenuCopy.SetWhere(draft, CopyWhere.Replace);
            return draft;
        }

        [Test]
        public void EveryLabelledControlOfTheWindowHasATooltip()
        {
            var window = Window(Busy());
            try
            {
                var root = window.rootVisualElement;
                Assert.That(root.Q<Button>("ad-copy-replace"), Is.Not.Null, "the busy copy shows every part");
                var missing = root.Query<VisualElement>().ToList()
                    .Where(e => e is Button || e is Toggle || e is ObjectField || e is SegmentedChoice || e.ClassListContains("ad-label"))
                    .Where(e => !(e.parent is SegmentedChoice) && !(e.parent is ObjectField) && string.IsNullOrEmpty(e.tooltip))
                    .Select(e => e.name + " " + e.GetType().Name).ToList();
                Assert.That(missing, Is.Empty);
                foreach (var choice in root.Query<SegmentedChoice>().ToList())
                    Assert.That(choice.Items.All(i => !string.IsNullOrEmpty(i.tooltip)), Is.True, choice.name + ": every choice says what it does");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void TheWindowFitsAt300And400InEveryLanguage()
        {
            var draft = Busy();
            var allow = new PanelAudit.AllowList();
            foreach (var language in new[] { "en-US", "ja-JP", "zh-Hans" })
            foreach (float width in new[] { 300f, 400f })
            using (L.UseTestLanguage(language, null))
            {
                var window = Window(draft);
                try
                {
                    var root = window.rootVisualElement;
                    Assert.That(root.Q("ad-copy-missing-2"), Is.Not.Null, "both missing objects and the marked one are shown");
                    LayoutProbe.Measure(root, () =>
                    {
                        LayoutProbe.Validate(root);
                        PanelAudit.Audit(root, root.worldBound.xMax, allow);
                        var texts = root.Query<TextElement>().ToList()
                            .Where(t => t.resolvedStyle.display == DisplayStyle.Flex && !string.IsNullOrEmpty(t.text) && t.resolvedStyle.whiteSpace != WhiteSpace.Normal
                                        && !t.ClassListContains("ad-add-value") && !t.ClassListContains("ad-add-pick") && !(t.parent is ObjectField) && t.GetFirstAncestorOfType<ObjectField>() == null);
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
#endif
