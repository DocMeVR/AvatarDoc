#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;
#if DOCME_MA
using nadena.dev.modular_avatar.core;
#endif

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Parameters other tools report to NDMF. A tool that builds its own layer during the upload (Skin Touch does)
    /// uses its parameters although nothing on the avatar shows it before then. A Modular Avatar Parameters
    /// component stands in for such a tool: test assemblies cannot hold components of their own.
    /// </summary>
    public class OtherToolParametersTests
    {
        const string Parameter = "DocMe/SkinTouch";

        TestAvatar avatar;

        [SetUp]
        public void SetUp()
        {
            GeneratedAssets.Note();
            avatar = TestAvatar.Create();
            Platform.OverrideMobile = false;
            AvatarPlanCache.Invalidate();
        }

        [TearDown]
        public void TearDown()
        {
            avatar.Dispose();
            Platform.OverrideMobile = null;
            AvatarPlanCache.Invalidate();
            GeneratedAssets.CleanUp();
        }

        static MenuEntry ParameterToggle(AvatarDoc menu)
        {
            var entry = menu.AddEntry(EntryKind.Toggle, "Skin touch");
            entry.parameterName = Parameter;
            return entry;
        }

        GameObject Child(string name, GameObject parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent((parent != null ? parent : avatar.Root).transform, false);
            return go;
        }

        void Declare()
        {
            avatar.ExpressionParameters.parameters = avatar.ExpressionParameters.parameters
                .Concat(new[] { new VRCExpressionParameters.Parameter { name = Parameter, valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true } })
                .ToArray();
        }

        static bool Warns(AvatarPlan plan, MenuEntry entry)
        {
            return plan.Messages.Any(m => m.Key == "ad.warn.parameter_control_unused" && m.EntryIds.Contains(entry.id));
        }

        /// <summary>Runs AvatarDoc's own build step only, not Modular Avatar's passes after it.</summary>
        static List<ErrorContext> RunAvatarDocBuild(TestAvatar a)
        {
            var context = new BuildContext(a.Root, null);
            context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            var errors = ErrorReport.CaptureErrors(() => AvatarDoctorBuilder.Run(context));
            context.DeactivateAllExtensionContexts();
            return errors;
        }

        static bool BuildWarns(List<ErrorContext> errors)
        {
            return errors.Any(e => e.TheError is SimpleError s && s.TitleKey == "ad.warn.parameter_control_unused");
        }

        [Test]
        public void AParameterNothingReadsWarns()
        {
            var menu = avatar.AddMenu();
            var toggle = ParameterToggle(menu);

            Assert.That(Warns(AvatarPlanCache.GetFor(menu), toggle), Is.True);
        }

        [Test]
        public void ADeclarationAloneIsNotAReader()
        {
            Declare();
            var menu = avatar.AddMenu();
            var toggle = ParameterToggle(menu);

            Assert.That(Warns(AvatarPlanCache.GetFor(menu), toggle), Is.True);
        }

        [Test]
        public void AContactOnlyWritesItsParameter()
        {
            var receiver = Child("Sensor").AddComponent<VRCContactReceiver>();
            receiver.parameter = Parameter;
            receiver.receiverType = ContactReceiver.ReceiverType.Constant;
            receiver.collisionTags = new List<string> { "Hand" };
            var menu = avatar.AddMenu();
            var toggle = ParameterToggle(menu);

            Assert.That(Warns(AvatarPlanCache.GetFor(menu), toggle), Is.True);
        }

        [Test]
        public void OtherToolsStillHearAvatarDocsParameters()
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            AvatarPlanCache.GetFor(menu);

            var reported = ParameterInfo.ForUI.GetParametersForObject(avatar.Root).Where(p => p.Source is AvatarDoc).Select(p => p.EffectiveName);
            Assert.That(reported, Does.Contain("AD/Hat"), "AvatarDoc is quiet only while it asks NDMF itself");
        }

        [Test]
        public void TheUploadWarnsWhenNothingReads()
        {
            var menu = avatar.AddMenu();
            ParameterToggle(menu);

            Assert.That(BuildWarns(RunAvatarDocBuild(avatar)), Is.True);
        }

#if DOCME_MA
        static void AddOtherTool(GameObject on, bool synced)
        {
            var parameters = on.AddComponent<ModularAvatarParameters>();
            parameters.parameters.Add(new ParameterConfig
            {
                nameOrPrefix = Parameter,
                syncType = synced ? ParameterSyncType.Bool : ParameterSyncType.NotSynced,
                saved = true,
                defaultValue = 1f,
            });
        }

        [Test]
        public void AParameterAnotherToolReportsHasAReader()
        {
            var menu = avatar.AddMenu(Child("Skin Touch"));
            AddOtherTool(menu.gameObject, synced: false);
            var toggle = ParameterToggle(menu);

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(Warns(plan, toggle), Is.False);
            Assert.That(plan.Messages.Any(m => m.Key == "ad.info.parameter_animator_only"), Is.True, "the tool does not sync it, and the note says so");
        }

        [Test]
        public void TheOtherToolIsHeardWhenAvatarDocReportsTheNameFirst()
        {
            // NDMF keeps the first report of a name; an AvatarDoc on the avatar root comes before anything deeper down.
            var menu = avatar.AddMenu();
            var toggle = ParameterToggle(menu);
            AddOtherTool(Child("Skin Touch", Child("Props")), synced: false);

            Assert.That(Warns(AvatarPlanCache.GetFor(menu), toggle), Is.False);
            Assert.That(AvatarPlanCache.ProvidedFor(avatar.Root.transform).Any(p => p.Source is AvatarDoc), Is.False,
                "AvatarDoc leaves its own reports out of what it asks NDMF");
        }

        [Test]
        public void TheOtherToolIsHeardWhenTheAvatarDeclaresTheNameToo()
        {
            // The descriptor on the root reports every declared name first, so the root's merged report hides the tool's.
            Declare();
            AddOtherTool(Child("Skin Touch"), synced: true);
            var menu = avatar.AddMenu();
            var toggle = ParameterToggle(menu);

            Assert.That(Warns(AvatarPlanCache.GetFor(menu), toggle), Is.False);
        }

        [Test]
        public void TheUploadHearsTheOtherToolWhenTheAvatarDeclaresTheNameToo()
        {
            Declare();
            AddOtherTool(Child("Skin Touch"), synced: true);
            var menu = avatar.AddMenu();
            ParameterToggle(menu);

            Assert.That(BuildWarns(RunAvatarDocBuild(avatar)), Is.False);
        }

        [Test]
        public void AToolOnAnEditorOnlyObjectIsNotHeard()
        {
            // The upload removes EditorOnly objects before anything reads them.
            Declare();
            var editorOnly = Child("Notes");
            editorOnly.tag = "EditorOnly";
            AddOtherTool(Child("Skin Touch", editorOnly), synced: true);
            var menu = avatar.AddMenu();
            var toggle = ParameterToggle(menu);

            Assert.That(Warns(AvatarPlanCache.GetFor(menu), toggle), Is.True);
        }

        [Test]
        public void AvatarDocDeclaresAParameterAnotherToolSyncs()
        {
            // The tool may declare it only after Modular Avatar clears menu parameters nothing declares yet.
            AddOtherTool(Child("Skin Touch"), synced: true);
            var menu = avatar.AddMenu();
            var toggle = ParameterToggle(menu);
            toggle.synced = false;

            var plan = AvatarPlanCache.GetFor(menu);

            var planned = plan.Plan.Parameters.Single(p => p.Name == Parameter);
            Assert.That(planned.ReusesExisting || planned.IsAnimatorOnly, Is.False, "AvatarDoc declares it itself");
            Assert.That(planned.Synced, Is.True, "the tool needs it synced, whatever the toggle says");
            Assert.That(planned.Type, Is.EqualTo(PlannedType.Bool));
            Assert.That(plan.Messages.Any(m => m.Key == "ad.info.parameter_animator_only"), Is.False, "it syncs, so nothing may say it will not");
            Assert.That(Warns(plan, toggle), Is.False);
            Assert.That((plan.AvatarBits, plan.AvatarDoctorBits), Is.EqualTo((1, 1)), "the Existing parameter, and the shared one counted once, on AvatarDoc's side");
            Assert.That(ParameterPlanner.CountBits(avatar.Root.transform), Is.EqualTo((1, 1)));
        }

        [Test]
        public void TheUploadDeclaresANameAnotherToolSyncs()
        {
            AddOtherTool(Child("Skin Touch"), synced: true);
            var menu = avatar.AddMenu();
            ParameterToggle(menu);

            var errors = RunAvatarDocBuild(avatar);

            Assert.That(BuildWarns(errors), Is.False);
            var declared = avatar.Descriptor.expressionParameters.parameters.SingleOrDefault(p => p.name == Parameter);
            Assert.That(declared, Is.Not.Null, "declared before Modular Avatar's menu fixup, which clears menu parameters nothing declares");
            Assert.That(declared.networkSynced, Is.True);
            Assert.That(avatar.Descriptor.expressionsMenu.controls.Single(c => c.name == "Skin touch").parameter.name, Is.EqualTo(Parameter));
        }

        [Test]
        public void AToggleValueTheOtherToolsTypeCannotHoldIsAnError()
        {
            AddOtherTool(Child("Skin Touch"), synced: true);
            var menu = avatar.AddMenu();
            ParameterToggle(menu).parameterValue = 2f;

            var plan = AvatarPlanCache.GetFor(menu);

            Assert.That(plan.Messages.Any(m => m.Key == "ad.error.parameter_type_conflict"), Is.True, "the tool's Bool may not widen to an Int");
        }
#endif
    }
}
#endif
