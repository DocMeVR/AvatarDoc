#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Tells NDMF (and through it Modular Avatar's tools) which parameters one component contributes.</summary>
    [ParameterProviderFor(typeof(AvatarDoc))]
    internal class AvatarDoctorParameterProvider : IParameterProvider
    {
        // NDMF asks every component of the avatar in turn and each answer needs the whole avatar planned, so the
        // last plan is kept and handed out again while the editor has not changed. AvatarPlanCache itself is not
        // used here: its plan reads ParameterInfo.ForUI, which is what asks this provider in the first place.
        // The existing map is read straight from the FX controller asset for the same reason: the virtual
        // controller and ParameterInfo both go through NDMF's parameter machinery, which would re-enter this
        // provider.
        static Transform memoRoot;
        static int memoVersion = -1;
        static MenuTree memoTree;
        static ParameterPlan memoPlan;
        static TouchBuilder.TouchParameters memoTouch;

        readonly AvatarDoc component;

        public AvatarDoctorParameterProvider(AvatarDoc component)
        {
            this.component = component;
        }

        [InitializeOnLoadMethod]
        static void ListenForDestroyedRoots() => EditorApplication.hierarchyChanged += DropDestroyedRoot;

        /// <summary>The memo holds its avatar's whole menu tree, transforms and components included; once that avatar is destroyed (a build clone thrown away, a Play mode avatar left behind) nothing will ask for it again.</summary>
        static void DropDestroyedRoot()
        {
            if (memoTree != null && memoRoot == null) Clear();
        }

        /// <summary>Drops the memo when it was planned for this root. The build calls this once the root's components are gone, so a build clone's tree is not kept alive after the build.</summary>
        internal static void Forget(Transform avatarRoot)
        {
            if (memoTree != null && (memoRoot == avatarRoot || memoRoot == null)) Clear();
        }

        static void Clear()
        {
            memoRoot = null;
            memoVersion = -1;
            memoTree = null;
            memoPlan = null;
            memoTouch = null;
        }

        static (MenuTree tree, ParameterPlan plan, TouchBuilder.TouchParameters touch) PlanFor(Transform avatarRoot)
        {
            DropDestroyedRoot();
            if (memoPlan != null && memoTree != null && memoRoot == avatarRoot && memoVersion == AvatarPlanCache.Version) return (memoTree, memoPlan, memoTouch);
            var descriptor = avatarRoot != null ? avatarRoot.GetComponent<VRCAvatarDescriptor>() : null;
            memoTree = MenuModel.Build(avatarRoot, null, MenuModel.CollectComponents(avatarRoot));
            var existing = ParametersWriter.ExistingParameters(descriptor != null ? descriptor.expressionParameters : null, ParametersWriter.FxOf(descriptor));
            memoPlan = ParameterPlanner.Plan(memoTree, existing);
            // The names every blend shape trigger's hidden copy and every pause flag will take, made unique the
            // same way the build makes them.
            memoTouch = TouchBuilder.Describe(memoTree, existing, memoPlan);
            memoRoot = avatarRoot;
            memoVersion = AvatarPlanCache.Version;
            return (memoTree, memoPlan, memoTouch);
        }

        public IEnumerable<ProvidedParameter> GetSuppliedParameters(BuildContext context = null)
        {
            // Names are deduplicated across the whole avatar, so the plan has to cover every component at once.
            var avatarRoot = nadena.dev.ndmf.runtime.RuntimeUtil.FindAvatarInParents(component.transform);
            MenuTree tree;
            ParameterPlan plan;
            TouchBuilder.TouchParameters touch;
            if (avatarRoot != null) (tree, plan, touch) = PlanFor(avatarRoot);
            else
            {
                var none = new Dictionary<string, ExistingParameter>();
                tree = MenuModel.Build(null, null, new[] { component });
                plan = ParameterPlanner.Plan(tree, none);
                touch = TouchBuilder.Describe(tree, none, plan);
            }

            foreach (var parameter in plan.Parameters)
            {
                if (parameter.ReusesExisting) continue;
                // A declaration has no control behind it, so the component that wrote it is the only owner there is.
                bool mine = parameter.Declared
                    ? parameter.Component == component
                    : parameter.Controls.Any(c => c.Component == component);
                if (!mine) continue;
                if (parameter.GroupName != null && parameter.Controls[0].Component != component) continue;

                var type = parameter.Type == PlannedType.Bool ? AnimatorControllerParameterType.Bool
                    : parameter.Type == PlannedType.Int ? AnimatorControllerParameterType.Int
                    : AnimatorControllerParameterType.Float;
                yield return new ProvidedParameter(parameter.Name, ParameterNamespace.Animator, component, AvatarDoctorPlugin.Instance, type)
                {
                    WantSynced = parameter.Synced,
                    DefaultValue = parameter.Default,
                    ExpandTypeOnConflict = true,
                    IsAnimatorOnly = parameter.IsAnimatorOnly,
                };
            }

            // Floats only the animator reads, never synced: a blend shape trigger's hidden copy of its shape and a
            // driven layer's pause flag.
            foreach (var node in tree.Triggers)
            {
                if (node.Component != component || node.Excluded) continue;
                if (touch.ShapeMirrors.TryGetValue(node.Entry.id, out var mirror)) yield return AnimatorOnlyFloat(mirror);
                if (touch.PauseFlags.TryGetValue(node.Entry.id, out var pause)) yield return AnimatorOnlyFloat(pause.Parameter);
            }
        }

        ProvidedParameter AnimatorOnlyFloat(string name)
        {
            return new ProvidedParameter(name, ParameterNamespace.Animator, component, AvatarDoctorPlugin.Instance, AnimatorControllerParameterType.Float)
            {
                WantSynced = false,
                IsAnimatorOnly = true,
            };
        }

        public void RemapParameters(ref ImmutableDictionary<(ParameterNamespace, string), ParameterMapping> nameMap, BuildContext context = null)
        {
        }
    }
}
#endif
