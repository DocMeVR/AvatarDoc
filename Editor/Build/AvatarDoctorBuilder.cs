#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using nadena.dev.ndmf.vrchat;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    public static class AvatarDoctorBuilder
    {
        public static void Run(BuildContext context)
        {
            var components = MenuModel.CollectComponents(context.AvatarRootTransform);
            if (components.Count == 0) return;

            var descriptor = context.VRChatAvatarDescriptor();
            var asc = context.Extension<AnimatorServicesContext>();
            asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx);

            var tree = MenuModel.Build(context.AvatarRootTransform, descriptor.expressionsMenu, components);
            context.GetState<LateEditState>().Pending.AddRange(tree.PendingEdits);

            var overrides = context.GetState<OverrideState>();
            foreach (var rule in tree.Overrides)
            {
                if (rule.Excluded) continue;
                foreach (var target in rule.Entry.blendShapes)
                    if (MenuModel.ShapeExists(target.renderer, target.shape) && !MenuModel.IsOutsideAvatar(context.AvatarRootTransform, target.renderer.transform))
                        overrides.Rules.Add(new OverrideRule { Renderer = target.renderer, Shape = target.shape, Value = target.weight, EntryName = rule.Entry.PlainName, EntryId = rule.Entry.id });
            }

            var existing = ParametersWriter.ExistingParameters(descriptor.expressionParameters, fx);
            ParametersWriter.AddProvided(existing, AvatarDoctorParameterProvider.FromOtherTools(ParameterInfo.ForContext(context), context.AvatarRootObject));
            ParametersWriter.MarkUsedByOtherTools(existing, AvatarDoctorParameterProvider.NamesFromOtherTools(ParameterInfo.ForContext(context), context.AvatarRootObject));
            ParametersWriter.AddReferenced(existing, ParametersWriter.ReferencedParameters(context.AvatarRootTransform));
            var plan = ParameterPlanner.Plan(tree, existing);
            // Planned in the Resolving phase, while the avatar's controllers still held their layers. Planning it
            // here instead would read controllers the animator services have already emptied, which frees whatever
            // their layers were reading, so a run without a prepared plan removes nothing.
            tree.Removal = context.GetState<RemovalState>().Plan ?? new RemovalPlan();
            // Another tool may have merged a controller that switches FX layers by position. The planner cannot see
            // it, so the budget below has to be told before it counts the freed bits.
            if (LayerRemover.AnyFxLayerControl(asc)) LayerRemover.KeepBlocked(tree.Removal);
            // The same goes for a planned layer the clone now carries twice: it stays, and so do its parameters.
            L.Report(LayerRemover.KeepAmbiguous(asc, tree.Removal, components[0]));
            L.Report(tree.Messages);
            L.Report(plan.Messages);
            int reported = tree.Messages.Count;

            var touch = TouchBuilder.Describe(tree, existing, plan);
            L.Report(tree.Messages.Skip(reported));
            reported = tree.Messages.Count;

            var (triggerPlans, triggerMessages) = TriggerPlanner.Plan(tree, plan, existing, touch);
            L.Report(triggerMessages);
            bool triggerErrors = triggerMessages.Any(m => m.Severity == MessageSeverity.Error);

            int total = ParametersWriter.PredictedCost(descriptor.expressionParameters, plan) - tree.Removal.FreedBits;
            var budget = ParametersWriter.BudgetMessage(total, plan.NewSyncedBits);
            if (budget != null) L.Report(budget);
            bool overBudget = budget != null && budget.Severity == MessageSeverity.Error;

            if (!tree.HasErrors && !plan.HasErrors && !overBudget && !triggerErrors)
            {
                overrides.Ready = true;
                // Stripping the imported layers belongs with the rest of the build: a run that stops on an error
                // has to leave the avatar exactly as it found it.
                var removalMessages = new List<ModelMessage>();
                LayerRemover.Run(asc, components, removalMessages, tree.Removal);
                foreach (var parameter in tree.Removal.Parameters)
                    removalMessages.Add(parameter.Freed
                        ? ModelMessage.Info(UiText.CountKey(parameter.Bits, "ad.info.removed_parameter"), parameter.Name, parameter.Bits.ToString())
                        : ModelMessage.Info("ad.info.removed_kept", parameter.Name, L.Text(parameter.KeptBecause)));
                L.Report(removalMessages);
                // Runs before the animator builder, on the build clone only, so the constraints and their sources
                // already exist by the time FollowLayerBuilder's own layer writes their weights.
                var (followPlan, followMessages) = FollowBuilder.Build(context, tree);
                L.Report(followMessages);
                // Adds a grabbable's PhysBone, its contacts and its own claims into the same plan, before anything
                // downstream reads it: the animator builder's toggle layer for a menu-toggled grabbable, and
                // FollowLayerBuilder's own layer for the object it grabs.
                GrabBuilder.Build(context, tree, followPlan, touch);
                L.Report(tree.Messages.Skip(reported));
                reported = tree.Messages.Count;
                var baseState = BaseState.Capture(tree);
                DefaultsApplier.Apply(tree, plan, baseState, triggerPlans, followPlan);
                var pins = context.GetState<PinLayerState>();
                // Taken before any layer of ours is added, so the merged layers follow what the avatar already did.
                bool writeDefaults = fx == null || AnimatorBuilder.DecideWriteDefaults(fx, pins.Names);
                var animatorMessages = AnimatorBuilder.Build(asc, tree, plan, baseState, pins.Names, followPlan, pins, touch);
                L.Report(animatorMessages);
                if (!animatorMessages.Any(m => m.Severity == MessageSeverity.Error))
                {
                    L.Report(TriggerBuilder.Build(asc, tree, triggerPlans, baseState, pins.Names, pins));
                    // Reuses the same parameter and trigger condition plumbing those layers just declared, so every
                    // follow condition's parameter is already in the controller by the time this builds its own.
                    L.Report(FollowLayerBuilder.Build(asc, tree, plan, followPlan, triggerPlans, pins.Names));
                    L.Report(TouchBuilder.Create(context, tree, touch));
                    L.Report(TouchBuilder.BuildFlipLayers(asc, tree, plan, touch, existing, pins.Names));
                    L.Report(TouchBuilder.BuildFollowDistanceLayers(asc, tree, touch, baseState, descriptor, pins.Names, pins, triggerPlans));
                    // Each blend shape trigger's own layer. Its hidden copy of the shape is written into the clips in
                    // the final pass, once Modular Avatar and VRCFury have merged theirs (ClipRewriter.Rewrite).
                    L.Report(LinkLayerBuilder.Build(asc, tree, triggerPlans, baseState, pins.Names, pins));
                    // After every driven layer: the pause reads the flags those layers wrote.
                    L.Report(TouchBuilder.BuildTrackingPauseLayers(asc, tree.AvatarRoot, pins.Names, pins));
                    L.Report(PresetBuilder.Build(asc, tree, plan, pins.Names, existing.Keys));
                    // Last of the layer writers: merged layers sit after every layer AvatarDoc makes itself. A
                    // trigger can carry a pin layer too (TriggerBuilder.Build, above), so Ready is set only once
                    // every layer writer has had its turn, or a pin-only trigger's layer would never be moved last.
                    L.Report(MergeBuilder.Build(asc, tree, plan, writeDefaults));
                    pins.Ready = true;
                    ParametersWriter.Write(context, descriptor, plan, tree.Removal);
                    MenuModel.SplitOverflow(tree, null);
                    L.Report(tree.Messages.Skip(reported));
                    MenuWriter.Write(context, descriptor, tree, plan);
                }
            }

            foreach (var component in components) Object.DestroyImmediate(component);
            // Nothing asks the provider about this clone once its components are gone, and its memo would keep
            // the clone's whole menu tree alive until some other avatar is planned.
            AvatarDoctorParameterProvider.Forget(context.AvatarRootTransform);
        }
    }
}
#endif
