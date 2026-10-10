#if DOCME_NDMF && DOCME_VRCSDK3A
using DocMe.AvatarDoctor.Editor;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEngine;

[assembly: ExportsPlugin(typeof(AvatarDoctorPlugin))]

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Runs on the VRChat avatar platform only (NDMF's default when no platform attribute is given).</summary>
    public class AvatarDoctorPlugin : Plugin<AvatarDoctorPlugin>
    {
        public override string QualifiedName => "docme.avatar-doctor";
        public override string DisplayName => "DocMe AvatarDoc";
        public override Color? ThemeColor => new Color(0x3F / 255f, 0xC9 / 255f, 0xA8 / 255f, 1f);

        protected override void Configure()
        {
            // The controllers the descriptor points at are still whole in this phase. It runs after the passes
            // that delete components, so a plan is never made for an AvatarDoc that the build then drops: NDMF
            // takes EditorOnly objects out here, and Modular Avatar filters by platform. A constraint naming a
            // plugin that is not installed is an optional dependency to NDMF's resolver, which skips it.
            InPhase(BuildPhase.Resolving)
                .AfterPlugin("nadena.dev.ndmf.InternalPasses")
                .AfterPlugin("nadena.dev.modular-avatar")
                .Run("Plan what removed controls free", RemovalPrepare.Run);

            InPhase(BuildPhase.Transforming)
                .WithRequiredExtension(typeof(AnimatorServicesContext), sequence =>
                    sequence.Run("Build toggles and menus", AvatarDoctorBuilder.Run)
                        .PreviewingWith(new AvatarDoctorPreview())
                        .BeforePlugin("nadena.dev.modular-avatar"));

            InPhase(BuildPhase.Transforming)
                .AfterPlugin("nadena.dev.modular-avatar")
                .WithRequiredExtension(typeof(AnimatorServicesContext), sequence =>
                    // The shapes of the outfit pieces go first: the pin pass then holds an overridden shape at
                    // its value in the clips this one has just written into, exactly as it does for any other.
                    sequence.Run("Shapes on the outfit pieces", ShapesBuilder.Run)
                        .Then.Run("Pin overridden shapes", OverrideBuilder.RunAfterModularAvatar)
                        .Then.Run("Apply menu edits after Modular Avatar", MenuEditApplier.RunAfterModularAvatar));

            // No required extension here: the menu applier needs none, and asking for the animator services on
            // every avatar in the project would round trip the final controllers of avatars that use nothing of
            // AvatarDoctor's. The pin pass asks for them itself, and only when there is a rule to pin.
            InPhase(BuildPhase.Optimizing)
                .BeforePlugin("com.anatawa12.avatar-optimizer")
                .Run("Finish after other tools", FinishAfterOtherTools);
        }

        /// <summary>
        /// The last pass: pin what only exists now, copy each blend shape trigger's shape into its hidden parameter,
        /// move every pin layer to the end of FX, then apply whatever menu edits are still waiting. The animator
        /// services are opened here rather than declared on the sequence, so an avatar with neither an override rule
        /// nor a pin layer never has its final controllers round tripped, and they are closed again before the pass
        /// returns. Deactivating commits the virtual controllers back to the descriptor (VirtualControllerContext
        /// writes them on deactivation, AnimatorServicesContext rewrites the animation paths first), so an optimizer
        /// that runs later and knows nothing of the animator services, and VRCFury's own layers merged straight into
        /// the real controller, both read the controller as this pass leaves it.
        /// </summary>
        public static void FinishAfterOtherTools(BuildContext context)
        {
            var overrides = context.GetState<OverrideState>();
            var pins = context.GetState<PinLayerState>();
            bool needsOverrides = overrides.Ready && overrides.Rules.Count > 0;
            bool needsPinMove = pins.Ready && pins.HasAny;
            if (needsOverrides || needsPinMove)
            {
                var asc = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
                try
                {
                    // Read before anything below takes a layer out of FX or moves one: a control in another
                    // controller that fades an FX layer names it by position, and has to name the same layer after.
                    var aimedFromElsewhere = needsPinMove ? AnimatorBuilder.FxLayerControlsElsewhere(asc) : null;
                    // VRCFury runs between AvatarDoc's passes and its Write Defaults fixer may have turned these on.
                    if (needsPinMove) AnimatorBuilder.TurnPinLayersWriteDefaultsOff(asc, pins.Names);
                    // Handing a shape back to rest has to run before both of these: before RunFinal, so an override
                    // that flattens the same shape also flattens the rest curve this adds, rather than the pin's
                    // last-writer position letting it escape the override whenever the trigger is off; before the
                    // move, so the pin layers are still in the relative order that decides which one is lowest.
                    if (needsPinMove) AnimatorBuilder.ReleasePinsToRest(asc, pins);
                    // This is the first point every layer any tool will ever add is already in the controller, so
                    // it is the only place that can tell whether a VRCFT template, a sleep or an AFK setup already
                    // owns Eyes or Mouth tracking; when one does, Follow distance's own pause layer for that part
                    // steps aside instead of fighting it. Before the move, so a dropped layer never needs moving.
                    if (needsPinMove) L.Report(TouchBuilder.RemoveForeignOwnedPauseLayers(asc, pins));
                    if (needsOverrides) OverrideBuilder.RunFinal(context);
                    // Every clip any tool will add is in FX now, the rest curves above included, and every
                    // override has flattened what it pins: the hidden copy of each blend shape trigger's shape is
                    // taken from the clips exactly as they will play.
                    if (needsPinMove) L.Report(ClipRewriter.Rewrite(asc, pins));
                    if (needsPinMove)
                    {
                        AnimatorBuilder.MovePinLayersLast(asc, pins.Names);
                        AnimatorBuilder.ReaimFxLayerControls(asc, aimedFromElsewhere);
                    }
                }
                finally
                {
                    context.DeactivateExtensionContext<AnimatorServicesContext>();
                    context.DeactivateExtensionContext<VirtualControllerContext>();
                }
            }
            MenuEditApplier.RunFinal(context);
        }
    }
}
#endif
