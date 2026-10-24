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

        static int quiet;

        readonly AvatarDoc component;

        public AvatarDoctorParameterProvider(AvatarDoc component)
        {
            this.component = component;
        }

        /// <summary>
        /// What every tool but AvatarDoc reports for the avatar. NDMF keeps one report per name, the first it meets,
        /// so AvatarDoc's own report of a name (an AvatarDoc on the avatar root comes first) would hide another
        /// tool's report of the same name. While this asks, AvatarDoc reports nothing.
        /// </summary>
        internal static List<ProvidedParameter> FromOtherTools(ParameterInfo info, GameObject avatarRoot)
        {
            quiet++;
            try
            {
                return info.GetParametersForObject(avatarRoot).ToList();
            }
            finally
            {
                quiet--;
            }
        }

        /// <summary>
        /// The names other tools' components report, asked object by object below the avatar root. The root's
        /// merged report keeps the descriptor's entry for every name the expression parameters declare, which hides
        /// another tool's report of the same name. A tool's component on the root itself stays hidden that way.
        /// </summary>
        internal static HashSet<string> NamesFromOtherTools(ParameterInfo info, GameObject avatarRoot)
        {
            var names = new HashSet<string>();
            if (avatarRoot == null) return names;
            quiet++;
            try
            {
                foreach (Transform child in avatarRoot.transform)
                {
                    // The upload removes EditorOnly objects first, as NDMF's own walk skips them.
                    if (child.CompareTag("EditorOnly")) continue;
                    foreach (var parameter in info.GetParametersForObject(child.gameObject))
                        if (parameter.Namespace == ParameterNamespace.Animator && ParametersWriter.ReportedByAnotherTool(parameter))
                            names.Add(parameter.EffectiveName);
                }
            }
            finally
            {
                quiet--;
            }
            return names;
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
            if (quiet > 0) yield break;
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
                // An exclusive group belongs to its own component, whichever components its members sit on.
                bool mine = parameter.Declared || !parameter.Group.IsNone
                    ? parameter.Component == component
                    : parameter.Controls.Any(c => c.Component == component);
                if (!mine) continue;

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

            // Floats only the animator reads, never synced: a blend shape trigger's hidden copy of its shape, and the
            // pause flag of a driven layer or of a toggle or trigger whose rule holds a shape VRChat tracks.
            foreach (var node in tree.Triggers)
            {
                if (node.Component != component || node.Excluded) continue;
                if (touch.ShapeMirrors.TryGetValue(node.Entry.id, out var mirror)) yield return AnimatorOnlyFloat(mirror);
                if (touch.PauseFlags.TryGetValue(node.Entry.id, out var pause)) yield return AnimatorOnlyFloat(pause.Parameter);
            }
            foreach (var control in tree.Controls)
            {
                if (control.Component != component || control.Excluded) continue;
                if (touch.PauseFlags.TryGetValue(control.Entry.id, out var pause)) yield return AnimatorOnlyFloat(pause.Parameter);
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
