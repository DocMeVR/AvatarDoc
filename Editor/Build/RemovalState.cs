#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.vrchat;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// What the removed controls free, worked out while the avatar's animator controllers are still whole. The
    /// animator services take the layers out of the controllers the descriptor points at as soon as they open, so
    /// a planner that ran inside the build pass would see an empty controller: no layer to leave out, and no layer
    /// reading a parameter either, which is exactly the false positive that frees a parameter the avatar needs.
    /// </summary>
    public sealed class RemovalState
    {
        public RemovalPlan Plan;
    }

    /// <summary>The pass that fills RemovalState, before any extension context opens.</summary>
    public static class RemovalPrepare
    {
        public static void Run(BuildContext context)
        {
            var components = MenuModel.CollectComponents(context.AvatarRootTransform);
            // Every other avatar pays nothing for this pass: without a removal there is no model to build.
            if (!components.Any(c => c != null && c.menuEdits.Any(e => e != null && e.remove))) return;
            var descriptor = context.VRChatAvatarDescriptor();
            if (descriptor == null) return;

            try
            {
                var tree = MenuModel.Build(context.AvatarRootTransform, descriptor.expressionsMenu, components);
                // Messages belong to the build pass, which reports the removal once it knows the build will go
                // ahead. fresh: this is a build clone the animator services are about to hollow out and NDMF will
                // throw away when the build ends, so it must read the controllers directly rather than through
                // whatever the inspector has cached for the live scene's own controllers.
                context.GetState<RemovalState>().Plan = RemovalPlanner.Plan(tree, descriptor, fresh: true);
            }
            finally
            {
                // Both readers cache what they found per avatar root, and this root is a build clone that is about
                // to be thrown away: RemovalPlanner.Plan reached ParameterUsage.AvatarWideFor above, which would
                // otherwise leave the clone's whole reader graph cached under a Transform nothing will ever query
                // again.
                VrcFuryReader.ClearCache(context.AvatarRootTransform);
                ParameterUsage.ClearAvatarCache(context.AvatarRootTransform);
            }
        }
    }
}
#endif
