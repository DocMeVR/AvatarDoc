#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using Object = UnityEngine.Object;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>The test hand: what it measures, what it writes to the scene preview, and how every way of ending it leaves the preview.</summary>
    public class TouchTestTests
    {
        TestAvatar avatar;
        AvatarDoc menu;
        Func<GameObject> selectedBefore;

        [SetUp]
        public void SetUp()
        {
            avatar = TestAvatar.Create();
            avatar.AddHumanoidRig();
            menu = avatar.AddMenu();
            selectedBefore = TouchTest.SelectedObject;
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            TouchTest.Stop();
            TouchTest.SelectedObject = selectedBefore;
            avatar.Dispose();
            AvatarPlanCache.Invalidate();
            PreviewState.ClearAll();
        }

        Vector3 Head => avatar.HeadBone.position;

        MenuEntry Contact(string name, TouchMode mode = TouchMode.Hold, float radius = 0.1f)
        {
            var entry = menu.AddEntry(EntryKind.Trigger, name);
            entry.source = TriggerSource.Contact;
            entry.touchMode = mode;
            entry.contactAnchor = avatar.HeadBone;
            entry.contactRadius = radius;
            entry.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Body, shape = "Blush", weight = 100f });
            return entry;
        }

        MenuEntry Toggle(string name, bool defaultOn = false)
        {
            var entry = menu.AddEntry(EntryKind.Toggle, name);
            entry.defaultOn = defaultOn;
            entry.objects.Add(new ObjectTarget { target = avatar.Glasses });
            return entry;
        }

        static float? Preview(MenuEntry entry) => PreviewState.TryGet(entry.id, out var value) ? value : (float?)null;

        void Start()
        {
            AvatarPlanCache.Invalidate();
            TouchTest.Start(menu);
            Assert.That(TouchTest.Running, Is.True, "the test hand starts");
        }

        /// <summary>Moves the hand to this distance from the head contact's centre, along the avatar's right.</summary>
        void HandAt(float distance) => TouchTest.MoveHand(Head + avatar.Root.transform.right * distance);

        static List<Delegate> SceneHandlers()
        {
            var field = typeof(SceneView).GetField("duringSceneGui", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, "SceneView keeps its duringSceneGui handlers in a field of that name");
            var handlers = field.GetValue(null) as Delegate;
            return handlers == null ? new List<Delegate>() : handlers.GetInvocationList().ToList();
        }

        static bool TestHandSubscribed() => SceneHandlers().Any(h => h.Method.DeclaringType == typeof(TouchTest));

        // ---- the sphere and the maths ------------------------------------------------------------------------

        [Test]
        public void TheTestMeasuresTheSphereTheBuildMakes()
        {
            GeneratedAssets.Note();
            avatar.Hat.transform.localPosition = new Vector3(0.1f, 1.5f, 0.05f);
            // Not uniform, so a rule that reads only one axis of the scale would measure another sphere.
            avatar.Hat.transform.localScale = new Vector3(1f, 2f, 1f);
            var trigger = menu.AddEntry(EntryKind.Trigger, "Poke");
            trigger.source = TriggerSource.Contact;
            trigger.contactAnchor = avatar.Hat.transform;
            trigger.contactOffset = new Vector3(0f, 0.1f, 0.02f);
            trigger.contactRadius = 0.03f;
            trigger.objects.Add(new ObjectTarget { target = avatar.Glasses });
            var condition = WhenCondition.FromEntry(trigger);
            var root = avatar.Root.transform;
            var expectedCentre = root.InverseTransformPoint(TouchProbe.Centre(condition));
            float expectedRadius = TouchProbe.WorldRadius(condition);

            GameObject processed = null;
            try
            {
                processed = nadena.dev.ndmf.AvatarProcessor.ManualProcessAvatar(avatar.Root);
                var receiver = processed.transform.Find("Hat").GetComponentInChildren<VRCContactReceiver>();
                Assert.That(receiver, Is.Not.Null, "the build makes a receiver for the contact");
                var scale = receiver.transform.lossyScale;
                float builtRadius = receiver.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                var builtCentre = processed.transform.InverseTransformPoint(receiver.transform.TransformPoint(receiver.position));
                Assert.That(Vector3.Distance(builtCentre, expectedCentre), Is.LessThan(0.0001f), "the same centre as the receiver the build makes");
                Assert.That(expectedRadius, Is.EqualTo(builtRadius).Within(0.0001f), "the same radius, scaled the way VRChat scales a contact");
                Assert.That(expectedRadius, Is.EqualTo(0.06f).Within(0.0001f), "the anchor's scale counts");
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                GeneratedAssets.CleanUp();
            }
        }

        [Test]
        public void ProximityIsZeroAtTheEdgeAndOneAtTheCentre()
        {
            var centre = new Vector3(0f, 1f, 0f);
            Assert.That(TouchProbe.Proximity(centre, 0.1f, centre), Is.EqualTo(1f), "the centre");
            Assert.That(TouchProbe.Proximity(centre, 0.1f, centre + Vector3.right * 0.1f), Is.EqualTo(0f).Within(0.0001f), "the edge");
            Assert.That(TouchProbe.Proximity(centre, 0.1f, centre + Vector3.up * 0.05f), Is.EqualTo(0.5f).Within(0.0001f), "halfway in");
            Assert.That(TouchProbe.Proximity(centre, 0.1f, centre + Vector3.forward * 0.3f), Is.EqualTo(0f), "outside stays at zero");
        }

        // ---- the three modes --------------------------------------------------------------------------------

        [Test]
        public void HoldIsOnOnlyWhileTheHandIsInside()
        {
            var poke = Contact("Poke");
            Start();

            HandAt(0.2f);
            Assert.That(Preview(poke), Is.Null, "outside the sphere the trigger shows nothing");
            HandAt(0.1005f);
            Assert.That(Preview(poke), Is.Null, "just outside the edge it is not touched yet");
            HandAt(0.05f);
            Assert.That(Preview(poke), Is.EqualTo(1f), "inside, the trigger is on");
            Assert.That(TouchTest.Triggers.Single(t => t.Entry == poke).Touched, Is.True);
            HandAt(0.3f);
            Assert.That(Preview(poke), Is.Null, "leaving switches it off again");
        }

        [Test]
        public void CloserThanWaitsUntilTheHandIsThatClose()
        {
            var poke = Contact("Poke");
            poke.contactProximity = 0.5f;
            Start();

            HandAt(0.06f);
            Assert.That(Preview(poke), Is.Null, "inside, but not closer than half the radius");
            HandAt(0.04f);
            Assert.That(Preview(poke), Is.EqualTo(1f), "closer than half the radius");
        }

        [Test]
        public void FlipFlipsItsToggleOnceEachTimeTheHandEnters()
        {
            var hat = Toggle("Hat");
            var boop = Contact("Boop", TouchMode.FlipToggle);
            boop.blendShapes.Clear();
            boop.flipToggleId = hat.id;
            Start();

            HandAt(0.05f);
            Assert.That(Preview(hat), Is.EqualTo(1f), "the first touch flips the hat on");
            TouchTest.Evaluate();
            HandAt(0.04f);
            Assert.That(Preview(hat), Is.EqualTo(1f), "staying inside flips nothing more");
            Assert.That(Preview(boop), Is.Null, "a flip trigger never gets a preview of its own");
            HandAt(0.3f);
            Assert.That(Preview(hat), Is.EqualTo(1f), "leaving keeps the flipped state");
            HandAt(0.05f);
            Assert.That(Preview(hat), Is.EqualTo(0f), "entering again flips it off");
            HandAt(0.3f);
            HandAt(0.05f);
            Assert.That(Preview(hat), Is.EqualTo(1f), "and on again");
        }

        [Test]
        public void FadeFollowsTheDistanceFromTheEdgeToFullyOnAt()
        {
            var pat = Contact("Headpat", TouchMode.FollowDistance);
            pat.contactFullAt = 0.8f;
            Start();

            HandAt(0.1f);
            Assert.That(Preview(pat), Is.Null, "at the edge nothing shows");
            HandAt(0.08f);
            Assert.That(Preview(pat), Is.EqualTo(0.25f).Within(0.0001f), "a fifth of the way in is a quarter of Fully on at 0.8");
            HandAt(0.06f);
            Assert.That(Preview(pat), Is.EqualTo(0.5f).Within(0.0001f), "halfway to Fully on at");
            HandAt(0.02f);
            Assert.That(Preview(pat), Is.EqualTo(1f).Within(0.0001f), "at Fully on at the effects are fully on");
            HandAt(0f);
            Assert.That(Preview(pat), Is.EqualTo(1f), "and stay so up to the centre");
            Assert.That(TouchTest.Triggers.Single().Amount, Is.EqualTo(1f));
        }

        [Test]
        public void TheScenePreviewDrawsAFadeTriggerPartWay()
        {
            var pat = Contact("Headpat", TouchMode.FollowDistance);
            pat.properties.Add(new MaterialPropertyTarget { renderer = avatar.Body, property = "_Glossiness", type = PropertyValueType.Float, value = 1f, minValue = 0.2f, explicitOff = true });
            pat.objects.Add(new ObjectTarget { target = avatar.Hat, activeWhenOn = true });
            pat.materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 0, material = avatar.MaterialB });
            avatar.Hat.SetActive(false);
            var hat = avatar.Hat.GetComponent<MeshRenderer>();

            Dictionary<Renderer, RendererState> At(float amount)
                => PreviewModel.Compute(avatar.Root.transform, new[] { menu }, new Dictionary<string, float> { [pat.id] = amount }, SceneReader.Direct);

            var half = At(0.5f);
            Assert.That(half[avatar.Body].Shapes[avatar.Mesh.GetBlendShapeIndex("Blush")], Is.EqualTo(50f).Within(0.001f), "the shape halfway to its weight");
            Assert.That(half[avatar.Body].Properties["_Glossiness"].Value.x, Is.EqualTo(0.6f).Within(0.0001f), "the property halfway from its off value");
            Assert.That(half[hat].Visible, Is.True, "an object the effects show is on as soon as the fade starts");
            Assert.That(half[avatar.Body].Materials.ContainsKey(0), Is.False, "a material swaps only past halfway");
            Assert.That(At(0.6f)[avatar.Body].Materials[0], Is.EqualTo(avatar.MaterialB), "past halfway the swapped material shows");
            Assert.That(At(1f)[avatar.Body].Shapes[avatar.Mesh.GetBlendShapeIndex("Blush")], Is.EqualTo(100f), "On is the whole effect, as before");

            var hold = Contact("Poke");
            Assert.That(PreviewModel.Compute(avatar.Root.transform, new[] { menu }, new Dictionary<string, float> { [hold.id] = 0.4f }, SceneReader.Direct)
                .ContainsKey(avatar.Body), Is.False, "a Hold trigger still reads a value under half as off");
        }

        // ---- tags and who ------------------------------------------------------------------------------------

        [Test]
        public void TheHandCountsAsHandAndFingerUntilSwitched()
        {
            var poke = Contact("Poke");
            var nod = Contact("Nod");
            nod.contactTags = new List<string> { "Head" };
            var left = Contact("Left only");
            left.contactTags = new List<string> { "HandL" };
            var tail = Contact("Tail brush");
            tail.contactTags = new List<string> { "Tail" };
            var step = Contact("Step");
            step.contactTags = new List<string> { "FootR" };
            var index = Contact("Right index");
            index.contactTags = new List<string> { "FingerIndexR" };
            Start();
            HandAt(0.05f);

            Assert.That(Preview(poke), Is.EqualTo(1f), "Finger and Hand by default");
            Assert.That(Preview(left), Is.EqualTo(1f), "the hand stands for both hands");
            Assert.That(Preview(index), Is.EqualTo(1f), "and for each finger of each side");
            Assert.That(Preview(nod), Is.Null);
            TouchTest.SetPart(TouchTestPart.Head);
            Assert.That(Preview(nod), Is.EqualTo(1f), "switched to Head");
            Assert.That(Preview(poke), Is.Null);
            TouchTest.SetPart(TouchTestPart.Foot);
            Assert.That(Preview(step), Is.EqualTo(1f), "a foot stands for both feet");
            TouchTest.SetPart(TouchTestPart.Custom);
            Assert.That(Preview(tail), Is.Null, "no custom tag typed yet");
            TouchTest.SetCustomTag(" Tail ");
            Assert.That(Preview(tail), Is.EqualTo(1f), "a custom tag of its own");
            Assert.That(TouchTest.Hand.CustomTag, Is.EqualTo("Tail"));
        }

        [Test]
        public void TheHandIsSomeoneElsesUntilSwitched()
        {
            var mine = Contact("Self only");
            mine.contactAllowOthers = false;
            var theirs = Contact("Others only");
            theirs.contactAllowSelf = false;
            Start();
            HandAt(0.05f);

            Assert.That(TouchTest.Hand.Self, Is.False, "another player's hand by default");
            Assert.That(Preview(theirs), Is.EqualTo(1f));
            Assert.That(Preview(mine), Is.Null);
            TouchTest.SetSelf(true);
            Assert.That(Preview(mine), Is.EqualTo(1f), "your own hand");
            Assert.That(Preview(theirs), Is.Null);
        }

        // ---- requirements and the gate -------------------------------------------------------------------------

        [Test]
        public void ARequirementToggleAndTheGateFollowThePreview()
        {
            var shy = Toggle("Shy");
            var poke = Contact("Poke");
            poke.when.Add(new ConditionGroup { all = { WhenCondition.FromEntry(poke), new WhenCondition { kind = WhenConditionKind.Toggle, sourceToggleId = shy.id, sourceToggleOn = true } } });
            var gate = Toggle("Pats allowed");
            var pat = Contact("Headpat", TouchMode.FollowDistance);
            pat.gateToggleId = gate.id;
            pat.gateToggleOn = true;
            Start();
            HandAt(0f);

            Assert.That(Preview(poke), Is.Null, "the toggle it needs is off");
            Assert.That(TouchTest.Triggers.Single(t => t.Entry == poke).Blocked, Is.True, "touched, but held back");
            Assert.That(Preview(pat), Is.Null, "its gate is off");
            PreviewState.Set(shy.id, 1f);
            PreviewState.Set(gate.id, 1f);
            TouchTest.Evaluate();
            Assert.That(Preview(poke), Is.EqualTo(1f), "the toggle is on in the preview now");
            Assert.That(Preview(pat), Is.EqualTo(1f), "and so is the gate");
            Assert.That(TouchTest.Triggers.Single(t => t.Entry == poke).Blocked, Is.False);
        }

        [Test]
        public void AContactRequirementIsMeasuredAndAParameterCountsAsMet()
        {
            var pat = Contact("Headpat", TouchMode.FollowDistance);
            var driver = WhenCondition.FromEntry(pat);
            var otherHand = new WhenCondition { kind = WhenConditionKind.Contact, contactAnchor = avatar.LeftHandBone, contactRadius = 0.05f, contactTags = new List<string> { "Hand" } };
            pat.when.Add(new ConditionGroup { all = { driver, otherHand } });
            var poke = Contact("Poke");
            poke.when.Add(new ConditionGroup { all = { WhenCondition.FromEntry(poke), new WhenCondition { kind = WhenConditionKind.Parameter, sourceParameter = "Existing", condition = ParameterCondition.IsTrue } } });
            var face = Contact("Face only");
            face.when.Add(new ConditionGroup { all = { new WhenCondition { kind = WhenConditionKind.Parameter, sourceParameter = "Existing", condition = ParameterCondition.IsTrue } } });
            face.when.Add(new ConditionGroup { all = { new WhenCondition { kind = WhenConditionKind.Contact, contactAnchor = avatar.LeftHandBone, contactRadius = 0.05f, contactTags = new List<string> { "Hand" } } } });
            Start();
            HandAt(0f);

            Assert.That(Preview(pat), Is.Null, "the hand is not at the other contact the Fade needs");
            Assert.That(Preview(poke), Is.EqualTo(1f), "a parameter the preview cannot read counts as met");
            Assert.That(Preview(face), Is.Null, "a group with no contact in it is left to the preview's own buttons");
            TouchTest.MoveHand(avatar.LeftHandBone.position);
            Assert.That(Preview(face), Is.EqualTo(1f), "its contact group is touched");
            Assert.That(TouchTest.Triggers.Single(t => t.Entry == pat).Contacts.Count, Is.EqualTo(2), "both spheres of the Fade are measured");
        }

        [Test]
        public void PhysBoneAndBlendShapeTriggersAreLeftOut()
        {
            var bone = new GameObject("Hair");
            bone.transform.SetParent(avatar.HeadBone, false);
            var physBone = bone.AddComponent<VRCPhysBone>();
            var pull = menu.AddEntry(EntryKind.Trigger, "Pull");
            pull.source = TriggerSource.PhysBone;
            pull.physBone = physBone;
            pull.objects.Add(new ObjectTarget { target = avatar.Hat });
            var link = menu.AddEntry(EntryKind.Trigger, "Link");
            link.source = TriggerSource.BlendShape;
            link.sourceRenderer = avatar.Body;
            link.sourceShape = "Eye_Big";
            link.objects.Add(new ObjectTarget { target = avatar.Hat });
            link.when.Add(new ConditionGroup { all = { new WhenCondition { kind = WhenConditionKind.Contact, contactAnchor = avatar.HeadBone, contactRadius = 0.1f } } });

            Assert.That(TouchProbe.Simulates(pull), Is.False);
            Assert.That(TouchProbe.Simulates(link), Is.False, "a blend shape trigger follows its shape, not a hand");
            Assert.That(TouchProbe.HasTouchTriggers(AvatarPlanCache.GetFor(menu).Tree), Is.False);
            Contact("Poke");
            AvatarPlanCache.Invalidate();
            Assert.That(TouchProbe.HasTouchTriggers(AvatarPlanCache.GetFor(menu).Tree), Is.True);
            Start();
            HandAt(0f);
            Assert.That(TouchTest.Triggers.Select(t => t.Entry.name), Is.EqualTo(new[] { "Poke" }));
            Assert.That(Preview(pull), Is.Null);
            Assert.That(Preview(link), Is.Null);
        }

        // ---- every way the test ends -----------------------------------------------------------------------------

        [Test]
        public void StopRemovesEveryValueItSetAndKeepsTheRest()
        {
            var shirt = Toggle("Shirt");
            var hat = Toggle("Hat");
            var poke = Contact("Poke");
            var boop = Contact("Boop", TouchMode.FlipToggle);
            boop.blendShapes.Clear();
            boop.flipToggleId = hat.id;
            PreviewState.Set(shirt.id, 1f);
            PreviewState.Set(poke.id, 0f);
            Start();

            Assert.That(Preview(poke), Is.Null, "the hand takes the trigger over while it runs");
            Assert.That(TestHandSubscribed(), Is.True, "the hand draws in the Scene view");
            HandAt(0.05f);
            Assert.That(Preview(poke), Is.EqualTo(1f));
            Assert.That(Preview(hat), Is.EqualTo(1f));

            TouchTest.Stop();

            Assert.That(TouchTest.Running, Is.False);
            Assert.That(Preview(poke), Is.EqualTo(0f), "the trigger's own preview comes back");
            Assert.That(Preview(hat), Is.Null, "the flipped toggle goes back to its default");
            Assert.That(Preview(shirt), Is.EqualTo(1f), "a preview the test never touched stays");
            Assert.That(TestHandSubscribed(), Is.False, "no Scene view handler is left behind");
            Assert.That(TouchTest.Listening, Is.False, "and no editor event either");
        }

        [Test]
        public void APreviewChangedDuringTheTestIsLeftAsItIs()
        {
            var hat = Toggle("Hat");
            var boop = Contact("Boop", TouchMode.FlipToggle);
            boop.blendShapes.Clear();
            boop.flipToggleId = hat.id;
            Start();
            HandAt(0.05f);
            Assert.That(Preview(hat), Is.EqualTo(1f));

            PreviewState.Set(hat.id, 0f);
            TouchTest.Stop();

            Assert.That(Preview(hat), Is.EqualTo(0f), "the creator set it after the hand did, so it stays theirs");
        }

        [Test]
        public void PlayModeEndsTheTest()
        {
            var poke = Contact("Poke");
            Start();
            HandAt(0f);
            Assert.That(Preview(poke), Is.EqualTo(1f));

            TouchTest.OnPlayModeChanged(PlayModeStateChange.ExitingEditMode);

            Assert.That(TouchTest.Running, Is.False, "entering Play mode ends the test");
            Assert.That(Preview(poke), Is.Null);
            Assert.That(TestHandSubscribed(), Is.False);
        }

        [Test]
        public void AScriptReloadEndsTheTest()
        {
            var poke = Contact("Poke");
            Start();
            HandAt(0f);

            TouchTest.OnBeforeAssemblyReload();

            Assert.That(TouchTest.Running, Is.False);
            Assert.That(Preview(poke), Is.Null);
            Assert.That(TestHandSubscribed(), Is.False);
            Assert.That(TouchTest.Listening, Is.False);
        }

        [Test]
        public void SelectingAnotherAvatarEndsTheTest()
        {
            var poke = Contact("Poke");
            Start();
            HandAt(0f);
            using (var other = TestAvatar.Create())
            {
                TouchTest.SelectedObject = () => avatar.HeadBone.gameObject;
                TouchTest.OnSelectionChanged();
                Assert.That(TouchTest.Running, Is.True, "a bone of the same avatar keeps the test");
                TouchTest.SelectedObject = () => null;
                TouchTest.OnSelectionChanged();
                Assert.That(TouchTest.Running, Is.True, "an empty selection alone keeps it too");

                TouchTest.SelectedObject = () => other.Body.gameObject;
                TouchTest.OnSelectionChanged();

                Assert.That(TouchTest.Running, Is.False, "another avatar ends the test");
                Assert.That(Preview(poke), Is.Null);
                Assert.That(TestHandSubscribed(), Is.False);
            }
        }

        /// <summary>Starts the hand from the header of a real Inspector of the menu, then closes that Inspector, the way selecting something else closes it.</summary>
        void StartInAnInspectorAndCloseIt()
        {
            var inspector = DocsShot.Inspector(menu);
            try
            {
                LayoutProbe.Measure(inspector, () =>
                {
                    UiTestEvents.SimulateClick(inspector.Q<AvatarDoctorView>().Header.Q<Button>("ad-touch-test"));
                    Assert.That(TouchTest.RunsOn(avatar.Root.transform), Is.True, "the Inspector's header starts the hand");
                    HandAt(0f);
                });
            }
            finally
            {
                DocsShot.ReleaseInspectors(inspector);
            }
            Assert.That(TouchTest.ViewCheckPending, Is.True, "a closed view asks for a check once the editor has settled");
        }

        [Test]
        public void ClosingTheLastInspectorOfTheAvatarEndsTheTest()
        {
            var poke = Contact("Poke");
            var light = new GameObject("Not an avatar");
            try
            {
                var first = new AvatarDoctorView(new SerializedObject(menu), menu);
                LayoutProbe.Measure(first, () =>
                {
                    StartInAnInspectorAndCloseIt();
                    TouchTest.SelectedObject = () => light;
                    TouchTest.CheckViews();
                    Assert.That(TouchTest.Running, Is.True, "another view still shows the avatar");
                });
                TouchTest.CheckViews();

                Assert.That(TouchTest.Running, Is.False, "with no view of the avatar left and something else selected, the test ends");
                Assert.That(Preview(poke), Is.Null);
                Assert.That(TestHandSubscribed(), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(light);
            }
        }

        [Test]
        public void AnEmptySelectionOrTheAvatarsOwnMeshKeepsTheHandWhenItsInspectorCloses()
        {
            var poke = Contact("Poke");
            TouchTest.SelectedObject = () => null;
            StartInAnInspectorAndCloseIt();
            TouchTest.CheckViews();
            Assert.That(TouchTest.Running, Is.True, "a click into empty Scene view space keeps the hand");
            Assert.That(Preview(poke), Is.EqualTo(1f));

            TouchTest.Stop();
            TouchTest.SelectedObject = () => avatar.Body.gameObject;
            StartInAnInspectorAndCloseIt();
            TouchTest.CheckViews();
            Assert.That(TouchTest.Running, Is.True, "a click on the avatar's own mesh keeps it too");
        }

        // ---- nothing is written -----------------------------------------------------------------------------------

        static List<string> UndoRecords()
        {
            Undo.FlushUndoRecordObjects();
            var method = typeof(Undo).GetMethod("GetRecords", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(List<string>), typeof(int).MakeByRefType() }, null);
            Assert.That(method, Is.Not.Null, "Undo keeps an internal GetRecords in Unity 2022.3");
            var records = new List<string>();
            var args = new object[] { records, 0 };
            method.Invoke(null, args);
            records.Add("cursor " + args[1]);
            return records;
        }

        string Snapshot()
        {
            var parts = new List<string> { EditorJsonUtility.ToJson(menu), EditorUtility.GetDirtyCount(menu).ToString() };
            foreach (var t in avatar.Root.GetComponentsInChildren<Transform>(true))
                parts.Add(t.name + " " + t.localPosition + " " + t.localRotation + " " + t.localScale + " " + t.gameObject.activeSelf + " " + t.GetComponents<Component>().Length);
            parts.Add(string.Join(",", Enumerable.Range(0, avatar.Mesh.blendShapeCount).Select(i => avatar.Body.GetBlendShapeWeight(i))));
            return string.Join("\n", parts);
        }

        [Test]
        public void TheTestWritesNothingToTheAvatarAndAddsNoUndoStep()
        {
            var hat = Toggle("Hat");
            Contact("Poke");
            Contact("Headpat", TouchMode.FollowDistance);
            var boop = Contact("Boop", TouchMode.FlipToggle);
            boop.blendShapes.Clear();
            boop.flipToggleId = hat.id;
            AvatarPlanCache.Invalidate();
            var before = Snapshot();
            var undoBefore = UndoRecords();
            var selectedObject = Selection.activeObject;

            TouchTest.Start(menu);
            HandAt(0.05f);
            HandAt(0f);
            TouchTest.SetPart(TouchTestPart.Head);
            TouchTest.SetSelf(true);
            TouchTest.SetPart(TouchTestPart.Hand);
            HandAt(0.3f);
            TouchTest.Stop();

            Assert.That(Snapshot(), Is.EqualTo(before), "the component, the objects and the shapes are as they were");
            Assert.That(UndoRecords(), Is.EqualTo(undoBefore), "no Undo step was added");
            Assert.That(Selection.activeObject, Is.EqualTo(selectedObject), "the selection did not change");
            Assert.That(PreviewState.Overrides.Value, Is.Empty, "nothing is left in the preview");
        }

        [Test]
        public void WhileTheHandRunsTheContactHandlesStepAsideAndADragChangesNothing()
        {
            var poke = Contact("Poke");
            var grouped = Contact("Grouped");
            grouped.when.Add(new ConditionGroup { all = { WhenCondition.FromEntry(grouped) } });
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            view.Tree.SelectEntry(poke.id);
            var row = view.Detail.Row;
            Assert.That(ContactSceneHandle.Editable(row), Is.True, "before the test, the contact's own handles show");

            Start();
            Assert.That(ContactSceneHandle.Editable(row), Is.False, "while the hand runs, only the hand has handles");
            var undoBefore = UndoRecords();
            ContactSceneHandle.Apply(poke, menu, new Vector3(0.3f, 0.2f, 0.1f), 0.5f);
            ContactSceneHandle.Apply(grouped.when[0].all[0], menu, new Vector3(0.3f, 0.2f, 0.1f), 0.5f);

            Assert.That(poke.contactOffset, Is.EqualTo(Vector3.zero), "a drag cannot move the contact");
            Assert.That(poke.contactRadius, Is.EqualTo(0.1f), "or resize it");
            Assert.That(grouped.when[0].all[0].contactOffset, Is.EqualTo(Vector3.zero));
            Assert.That(grouped.when[0].all[0].contactRadius, Is.EqualTo(0.1f));
            Assert.That(UndoRecords(), Is.EqualTo(undoBefore), "no Move contact step");
            TouchTest.Stop();
            Assert.That(ContactSceneHandle.Editable(row), Is.True, "the handles come back when the hand goes");
        }

        [Test]
        public void TheHandleTheStartsAndTheTestDrawOneSphere()
        {
            avatar.HeadBone.localScale = new Vector3(-1f, 2f, 1f);
            var mirrored = new WhenCondition { kind = WhenConditionKind.Contact, contactAnchor = avatar.HeadBone, contactOffset = new Vector3(0f, 0.05f, 0f), contactRadius = 3f };
            var (centre, radius) = ContactSceneHandle.SphereOf(mirrored);
            Assert.That(centre, Is.EqualTo(TouchProbe.Centre(mirrored)));
            Assert.That(radius, Is.EqualTo(TouchProbe.WorldRadius(mirrored)), "the handle draws the sphere the test measures");
            Assert.That(radius, Is.EqualTo(2f).Within(0.0001f), "clamped to 1 like the build, then scaled by the largest axis, a mirrored one included");

            avatar.HeadBone.localScale = new Vector3(1f, 2f, 1f);
            var created = ContactStarts.Apply(menu, ContactStarts.StartKind.Headpat, out var errorKey);
            Assert.That(errorKey, Is.Null);
            float avatarScale = avatar.Descriptor.ViewPosition.y / 1.6f;
            Assert.That(TouchProbe.WorldRadius(WhenCondition.FromEntry(created.Single())), Is.EqualTo(0.14f * avatarScale).Within(0.0001f),
                "on a stretched head the start's sphere is still the size it was placed at");
        }

        [Test]
        public void AFadePublishesWholePercents()
        {
            var pat = Contact("Headpat", TouchMode.FollowDistance);
            pat.contactFullAt = 0.8f;
            Start();

            HandAt(0.1f * (1f - 0.8f / 3f));

            Assert.That(Preview(pat), Is.EqualTo(0.33f).Within(0.00001f), "a third of the way is 33 percent");
            Assert.That(TouchTest.Triggers.Single().Amount, Is.EqualTo(1f / 3f).Within(0.001f), "the hand keeps the exact amount for its label");
        }

        [Test]
        public void ADragPublishesAFadeAtMostEvery50Milliseconds()
        {
            var pat = Contact("Headpat", TouchMode.FollowDistance);
            pat.contactFullAt = 0.8f;
            var right = avatar.Root.transform.right;
            double now = 100.0;
            var clockBefore = TouchTest.Clock;
            try
            {
                TouchTest.Clock = () => now;
                Start();

                TouchTest.DragHand(Head + right * 0.06f);
                Assert.That(Preview(pat), Is.EqualTo(0.5f).Within(0.0001f), "coming in shows at once");
                now += 0.01;
                TouchTest.DragHand(Head + right * 0.04f);
                Assert.That(Preview(pat), Is.EqualTo(0.5f).Within(0.0001f), "within 50 ms a new amount waits");
                Assert.That(TouchTest.PublishPending, Is.True);
                now += 0.01;
                TouchTest.DragHand(Head + right * 0.02f);
                TouchTest.Tick();
                Assert.That(Preview(pat), Is.EqualTo(0.5f).Within(0.0001f), "still too soon");
                now += 0.05;
                TouchTest.Tick();
                Assert.That(Preview(pat), Is.EqualTo(1f), "the latest amount shows once 50 ms have passed");
                Assert.That(TouchTest.PublishPending, Is.False);

                now += 0.001;
                TouchTest.DragHand(Head + right * 0.3f);
                Assert.That(Preview(pat), Is.Null, "leaving shows at once");
                now += 0.001;
                TouchTest.DragHand(Head + right * 0.06f);
                now += 0.001;
                TouchTest.DragHand(Head + right * 0.04f);
                Assert.That(TouchTest.PublishPending, Is.True);
                TouchTest.Stop();
                Assert.That(Preview(pat), Is.Null, "Stop drops what was waiting and puts the preview back");
                Assert.That(TouchTest.PublishPending, Is.False);
                Assert.That(TouchTest.Listening, Is.False);
            }
            finally
            {
                TouchTest.Clock = clockBefore;
            }
        }

        [Test]
        public void AFlipPublishesItsChangesOnce()
        {
            var red = Toggle("Red");
            red.exclusiveGroup = "Colour";
            var blue = Toggle("Blue", defaultOn: true);
            blue.exclusiveGroup = "Colour";
            var boop = Contact("Boop", TouchMode.FlipToggle);
            boop.blendShapes.Clear();
            boop.flipToggleId = red.id;
            Start();
            int changes = 0;
            var watch = new PublishedValueWatch<System.Collections.Immutable.ImmutableDictionary<string, float>>(PreviewState.Overrides, _ => changes++);
            watch.Start();
            try
            {
                HandAt(0.05f);
            }
            finally
            {
                watch.Stop();
            }

            Assert.That(changes, Is.EqualTo(1), "the flip and the other member of its group go out in one change");
            Assert.That(Preview(red), Is.EqualTo(1f));
            Assert.That(Preview(blue), Is.EqualTo(0f));
            TouchTest.Stop();
            Assert.That(Preview(red), Is.Null);
            Assert.That(Preview(blue), Is.Null);
        }

        [Test]
        public void AGroupOfTogglesAloneFollowsThePreviewWhileTheHandRuns()
        {
            var shy = Toggle("Shy");
            var poke = Contact("Poke");
            poke.when.Add(new ConditionGroup { all = { WhenCondition.FromEntry(poke) } });
            poke.when.Add(new ConditionGroup { all = { new WhenCondition { kind = WhenConditionKind.Toggle, sourceToggleId = shy.id, sourceToggleOn = true } } });
            Start();
            HandAt(0.3f);
            Assert.That(Preview(poke), Is.Null, "nothing touches it and its toggle is off");

            PreviewState.Set(shy.id, 1f);
            TouchTest.Evaluate();

            Assert.That(Preview(poke), Is.EqualTo(1f), "its group of a toggle alone is met, as it would be in VRChat");
            Assert.That(TouchTest.Triggers.Single().Touched, Is.False);
        }

        [Test]
        public void ATriggerWhoseWhenIsAPhysBoneReactsThroughAContactGroup()
        {
            var bone = new GameObject("Hair");
            bone.transform.SetParent(avatar.HeadBone, false);
            var pull = menu.AddEntry(EntryKind.Trigger, "Pull or pat");
            pull.source = TriggerSource.PhysBone;
            pull.physBone = bone.AddComponent<VRCPhysBone>();
            pull.objects.Add(new ObjectTarget { target = avatar.Hat });
            pull.when.Add(new ConditionGroup { all = { new WhenCondition { kind = WhenConditionKind.PhysBone, physBone = pull.physBone } } });
            pull.when.Add(new ConditionGroup { all = { new WhenCondition { kind = WhenConditionKind.Contact, contactAnchor = avatar.HeadBone, contactRadius = 0.1f } } });

            Assert.That(TouchProbe.Simulates(pull), Is.True, "its second group is a contact");
            Start();
            HandAt(0f);
            Assert.That(Preview(pull), Is.EqualTo(1f));
        }

        [Test]
        public void AFadeWaitsForARequirementOnlyWhenItsDriverIsTouched()
        {
            var pat = Contact("Headpat", TouchMode.FollowDistance);
            var otherHand = new WhenCondition { kind = WhenConditionKind.Contact, contactAnchor = avatar.LeftHandBone, contactRadius = 0.05f };
            pat.when.Add(new ConditionGroup { all = { WhenCondition.FromEntry(pat), otherHand } });
            Start();

            TouchTest.MoveHand(avatar.LeftHandBone.position);
            var probed = TouchTest.Triggers.Single();
            Assert.That(probed.Touched, Is.True, "the hand is in the sphere of its requirement");
            Assert.That(probed.Blocked, Is.False, "but far from the sphere that fades it, so it waits for nothing");
            Assert.That(probed.Engaged, Is.False, "and the hand's label leaves it out");

            HandAt(0f);
            probed = TouchTest.Triggers.Single();
            Assert.That(probed.Blocked, Is.True, "at its own sphere, it waits for the other one");
        }

        [Test]
        public void ResetPreviewWhileTheHandRunsForgetsThePreviewsFromBefore()
        {
            var poke = Contact("Poke");
            PreviewState.Set(poke.id, 0f);
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            Start();
            HandAt(0f);

            view.Header.ResetPreview();
            TouchTest.Evaluate();
            Assert.That(Preview(poke), Is.EqualTo(1f), "the hand keeps its own trigger");
            TouchTest.Stop();

            Assert.That(Preview(poke), Is.Null, "Reset preview cleared the Off it had before, so it does not come back");
        }

        [Test]
        public void UnityBlendsAFadesObjectsAndMaterialsTheWayThePreviewDraws()
        {
            GeneratedAssets.Note();
            var pat = menu.AddEntry(EntryKind.Trigger, "Headpat");
            pat.source = TriggerSource.Contact;
            pat.touchMode = TouchMode.FollowDistance;
            pat.contactAnchor = avatar.HeadBone;
            pat.contactRadius = 0.1f;
            pat.contactFullAt = 1f;
            var shown = new ObjectTarget { target = avatar.Hat, activeWhenOn = true };
            var hidden = new ObjectTarget { target = avatar.Glasses, activeWhenOn = false };
            pat.objects.Add(shown);
            pat.objects.Add(hidden);
            pat.materials.Add(new MaterialTarget { renderer = avatar.Body, slot = 0, material = avatar.MaterialB });
            avatar.Hat.SetActive(false);

            GameObject processed = null;
            try
            {
                processed = nadena.dev.ndmf.AvatarProcessor.ManualProcessAvatar(avatar.Root);
                var fx = (UnityEditor.Animations.AnimatorController)processed.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers
                    .First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
                var parameter = fx.parameters.Select(p => p.name).Single(n => n.Contains("/Contact/"));
                foreach (float amount in new[] { 0.3f, 0.7f })
                {
                    var (hatOn, glassesOn, material) = Play(processed, fx, parameter, amount);
                    Assert.That(hatOn, Is.EqualTo(EffectMath.FadeObjectActive(shown, amount)), amount + ": an object the effects show, the way the preview draws it");
                    Assert.That(glassesOn, Is.EqualTo(EffectMath.FadeObjectActive(hidden, amount)), amount + ": an object the effects hide");
                    Assert.That(material.StartsWith(avatar.MaterialB.name), Is.EqualTo(EffectMath.FadeMaterialOn(amount)), amount + ": the swapped material, " + material);
                }
            }
            finally
            {
                if (processed != null && processed != avatar.Root) Object.DestroyImmediate(processed);
                GeneratedAssets.CleanUp();
            }
        }

        /// <summary>Plays the built FX on the processed avatar from its rest, with the contact's Proximity at this value, for half a second.</summary>
        static (bool hatOn, bool glassesOn, string material) Play(GameObject processed, UnityEditor.Animations.AnimatorController fx, string parameter, float value)
        {
            var hat = processed.transform.Find("Hat").gameObject;
            var glasses = processed.transform.Find("Glasses").gameObject;
            var body = processed.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
            hat.SetActive(false);
            glasses.SetActive(true);
            var rest = body.sharedMaterials;
            var graph = UnityEngine.Playables.PlayableGraph.Create("Touch test fade");
            try
            {
                graph.SetTimeUpdateMode(UnityEngine.Playables.DirectorUpdateMode.Manual);
                var playable = UnityEngine.Animations.AnimatorControllerPlayable.Create(graph, fx);
                var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "FX", processed.GetComponent<Animator>());
                output.SetSourcePlayable(playable);
                playable.SetFloat(parameter, value);
                for (int frame = 0; frame < 30; frame++) graph.Evaluate(1f / 60f);
                return (hat.activeSelf, glasses.activeSelf, body.sharedMaterials[0] != null ? body.sharedMaterials[0].name : "");
            }
            finally
            {
                graph.Destroy();
                body.sharedMaterials = rest;
            }
        }

        // ---- the buttons ------------------------------------------------------------------------------------------

        [Test]
        public void ThePanelSaysWhenTheHandPassesATriggerBy()
        {
            var empty = Contact("Empty");
            empty.blendShapes.Clear();
            var poke = Contact("Poke");
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);

            view.Tree.SelectEntry(empty.id);
            var note = view.Detail.Q<Label>("ad-touch-test-trigger-note");
            Assert.That(note, Is.Not.Null);
            Assert.That(note.ClassListContains("ad-hidden"), Is.False, "with no effect yet the hand passes it by, and the panel says so");
            view.Tree.SelectEntry(poke.id);
            Assert.That(view.Detail.Q<Label>("ad-touch-test-trigger-note").ClassListContains("ad-hidden"), Is.True, "a trigger the hand measures needs no note");

            menu.enabled = false;
            AvatarPlanCache.Invalidate();
            view.Refresh();
            Assert.That(view.Detail.Q<Label>("ad-touch-test-trigger-note").ClassListContains("ad-hidden"), Is.False, "a switched off component builds nothing for the hand to touch");
        }

        [Test]
        public void ThePreviewRowPicksNoChoiceForAFadePartWay()
        {
            var pat = Contact("Headpat", TouchMode.FollowDistance);
            pat.contactFullAt = 0.8f;
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(pat.id);
                var choice = view.Detail.Q<SegmentedChoice>("ad-preview-choice");
                Start();
                HandAt(0.06f);
                view.Detail.RefreshPreview();
                Assert.That(choice.Index, Is.EqualTo(-1), "part way, neither On nor Off is picked");
                HandAt(0f);
                view.Detail.RefreshPreview();
                Assert.That(choice.Index, Is.EqualTo(1), "fully on is On");
                TouchTest.Stop();
                view.Detail.RefreshPreview();
                Assert.That(choice.Index, Is.EqualTo(0), "and with no preview, Default");
            });
        }

        [Test]
        public void TheHeaderShowsTheButtonOnlyForAnAvatarWithATouchTrigger()
        {
            Toggle("Hat");
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            Assert.That(view.Header.Q("ad-touch-test-block").ClassListContains("ad-hidden"), Is.True, "no touch trigger, no test hand");

            Contact("Poke");
            AvatarPlanCache.Invalidate();
            view.Refresh();
            Assert.That(view.Header.Q("ad-touch-test-block").ClassListContains("ad-hidden"), Is.False);
            Assert.That(view.Header.Q("ad-touch-test-settings").ClassListContains("ad-hidden"), Is.True, "the hand's settings show while it runs");
        }

        [Test]
        public void ThePanelButtonStartsTheHandJustOutsideThatTriggersSphere()
        {
            var poke = Contact("Poke");
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            LayoutProbe.Measure(view, () =>
            {
                view.Tree.SelectEntry(poke.id);
                var button = view.Detail.Q<Button>("ad-touch-test-trigger");
                Assert.That(button, Is.Not.Null, "a contact trigger's panel has the button");
                Assert.That(button.text, Is.EqualTo(L.Text("ad.ui.touch_test.start")));
                UiTestEvents.SimulateClick(button);

                Assert.That(TouchTest.Running, Is.True);
                float distance = Vector3.Distance(TouchTest.Hand.Position, Head);
                Assert.That(distance, Is.GreaterThan(0.1f), "it starts outside the sphere");
                Assert.That(distance, Is.LessThan(0.3f), "but right beside it");
                Assert.That(Preview(poke), Is.Null);
                Assert.That(button.text, Is.EqualTo(L.Text("ad.ui.touch_test.stop")), "the button stops it now");
                Assert.That(view.Header.Q<Button>("ad-touch-test").text, Is.EqualTo(L.Text("ad.ui.touch_test.stop")), "so does the header's");
                Assert.That(view.Header.Q("ad-touch-test-settings").ClassListContains("ad-hidden"), Is.False);

                UiTestEvents.SimulateClick(button);
                Assert.That(TouchTest.Running, Is.False);
                Assert.That(button.text, Is.EqualTo(L.Text("ad.ui.touch_test.start")));
            });
        }

        [Test]
        public void TheHeaderButtonStartsTheHandInFrontOfTheHead()
        {
            Contact("Poke");
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            LayoutProbe.Measure(view, () =>
            {
                UiTestEvents.SimulateClick(view.Header.Q<Button>("ad-touch-test"));

                var root = avatar.Root.transform;
                var eyes = root.TransformPoint(avatar.Descriptor.ViewPosition);
                var offset = TouchTest.Hand.Position - eyes;
                Assert.That(Vector3.Dot(offset, root.forward), Is.GreaterThan(0.05f), "in front of the face");
                Assert.That(offset.magnitude, Is.LessThan(0.5f), "close to it");

                var tagRow = view.Header.Q<TextField>("ad-touch-test-tag").parent;
                Assert.That(tagRow.ClassListContains("ad-hidden"), Is.True, "no tag field for a hand");
                Assert.That(view.Header.Q<Label>("ad-touch-test-part-label").text, Is.EqualTo(L.Text("ad.ui.touch_test.part.hand")));
                Assert.That(view.Header.Q<Button>("ad-touch-test-part").Q(className: "ad-dropdown-arrow"), Is.Not.Null, "Counts as shows that it opens a menu");
                PickPart(view, TouchTestPart.Custom);
                Assert.That(TouchTest.Hand.Part, Is.EqualTo(TouchTestPart.Custom));
                Assert.That(view.Header.Q<Label>("ad-touch-test-part-label").text, Is.EqualTo(L.Text("ad.ui.touch_test.part.custom")));
                Assert.That(tagRow.ClassListContains("ad-hidden"), Is.False, "a custom tag gets its field");
                view.Header.Q<TextField>("ad-touch-test-tag").value = "Tail";
                Assert.That(TouchTest.Hand.CustomTag, Is.EqualTo("Tail"));
                view.Header.Q<SegmentedChoice>("ad-touch-test-who").Pick(1);
                Assert.That(TouchTest.Hand.Self, Is.True);
            });
        }

        [Test]
        public void PhysBoneTriggersHaveNoButton()
        {
            var bone = new GameObject("Hair");
            bone.transform.SetParent(avatar.HeadBone, false);
            var pull = menu.AddEntry(EntryKind.Trigger, "Pull");
            pull.source = TriggerSource.PhysBone;
            pull.physBone = bone.AddComponent<VRCPhysBone>();
            pull.objects.Add(new ObjectTarget { target = avatar.Hat });
            var view = new AvatarDoctorView(new SerializedObject(menu), menu);
            view.Tree.SelectEntry(pull.id);
            Assert.That(view.Detail.Q<Button>("ad-touch-test-trigger"), Is.Null);
        }

        [Test]
        public void TheButtonsAndTheHandsSettingsFitAt300And400InEveryLanguage()
        {
            var hat = Toggle("Hat");
            var poke = Contact("Poke");
            var pat = Contact("Headpat", TouchMode.FollowDistance);
            var boop = Contact("Boop", TouchMode.FlipToggle);
            boop.blendShapes.Clear();
            boop.flipToggleId = hat.id;
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
                    try
                    {
                        UiTestEvents.SimulateClick(view.Header.Q<Button>("ad-touch-test"));
                        LayoutProbe.Validate(view);
                        var longest = TouchTestControls.PartLabels.OrderByDescending(t => t.Length).First();
                        PickPart(view, (TouchTestPart)TouchTestControls.PartLabels.IndexOf(longest));
                        LayoutProbe.Validate(view);
                        Whole(new TextElement[] { view.Header.Q<Label>("ad-touch-test-part-label") }, language, width);
                        PickPart(view, TouchTestPart.Custom);
                        LayoutProbe.Validate(view);
                        Audit(view.Header, language, width, allow);
                        var shown = new List<TextElement>
                        {
                            view.Header.Q("ad-touch-test-row").Q<Label>(), view.Header.Q<Button>("ad-touch-test"),
                            view.Header.Q("ad-touch-test-part-row").Q<Label>(), view.Header.Q<Label>("ad-touch-test-part-label"),
                            view.Header.Q<TextField>("ad-touch-test-tag").labelElement, view.Header.Q("ad-touch-test-who-row").Q<Label>(),
                        };
                        shown.AddRange(view.Header.Q<SegmentedChoice>("ad-touch-test-who").Items);
                        Whole(shown, language, width);
                        foreach (var entry in new[] { poke, pat, boop })
                        {
                            view.Tree.SelectEntry(entry.id);
                            LayoutProbe.Validate(view);
                            Audit(view.Detail, language, width, allow);
                            Whole(new TextElement[] { view.Detail.Q<Button>("ad-touch-test-trigger"), view.Detail.Q("ad-touch-test-trigger-row").Q<Label>() }, language, width);
                        }
                        TouchTest.Stop();
                        LayoutProbe.Validate(view);
                        Audit(view.Header, language, width, allow);
                        Whole(new TextElement[] { view.Header.Q<Button>("ad-touch-test"), view.Detail.Q<Button>("ad-touch-test-trigger") }, language, width);
                    }
                    finally
                    {
                        TouchTest.Stop();
                    }
                }, width);
            }
        }

        static void Pointer<T>(VisualElement target, EventType type, int button) where T : PointerEventBase<T>, new()
        {
            using (var e = PointerEventBase<T>.GetPooled(new Event { type = type, button = button, mousePosition = target.worldBound.center }))
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        /// <summary>Opens Counts as and picks a part with a real click on its row, the way the creator does.</summary>
        static void PickPart(AvatarDoctorView view, TouchTestPart part)
        {
            var panelRoot = view.panel.visualTree;
            try
            {
                UiTestEvents.SimulateClick(view.Header.Q<Button>("ad-touch-test-part"));
                var menu = panelRoot.Q<AdMenu>();
                Assert.That(menu, Is.Not.Null, "Counts as opens its menu");
                Assert.That(menu.Items.Select(i => i.Text), Is.EqualTo(TouchTestControls.PartLabels), "every part, in order");
                Assert.That(menu.Items.Count(i => i.Checked), Is.EqualTo(1), "the current part is ticked");
                Pointer<PointerDownEvent>(menu.Rows[(int)part], EventType.MouseDown, 0);
                Pointer<PointerUpEvent>(menu.Rows[(int)part], EventType.MouseUp, 0);
                Assert.That(TouchTest.Hand.Part, Is.EqualTo(part));
            }
            finally
            {
                foreach (var left in panelRoot.Query<AdMenu>().ToList()) left.Close(false);
            }
        }

        static void Whole(IEnumerable<TextElement> texts, string language, float width)
        {
            foreach (var text in texts)
            {
                Assert.That(text.worldBound.width, Is.GreaterThan(0f), language + " at " + width + ": \"" + text.text + "\" shows");
                var size = text.MeasureTextSize(text.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                Assert.That(text.contentRect.width, Is.GreaterThanOrEqualTo(size.x - 1f), language + " at " + width + ": \"" + text.text + "\" is cut short");
            }
        }

        static void Audit(VisualElement part, string language, float width, PanelAudit.AllowList allow)
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
    }
}
#endif
