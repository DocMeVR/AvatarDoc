#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Menu edits AvatarDoctorBuilder.Run could not resolve against its own model, carried to the passes that run after Modular Avatar and after VRCFury.</summary>
    public sealed class LateEditState
    {
        public List<PendingEdit> Pending = new List<PendingEdit>();
        /// <summary>Set once the last late pass has run, so nothing after it mistakes a leftover edit for one still waiting on a future pass.</summary>
        public bool Final;
    }
}
#endif
