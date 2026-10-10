#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Finds everything VRCFury or Modular Avatar would install on this avatar and offers it for import.</summary>
    public static class ImportScanner
    {
        /// <summary>How many times Scan has actually walked the avatar, for a test to prove a caller only rescans when it truly must (the welcome card's Convert start, most often).</summary>
        public static int ScanCalls;

        public static List<ImportCandidate> Scan(Transform avatarRoot, MenuTree tree)
        {
            ScanCalls++;
            var result = new List<ImportCandidate>();
            if (avatarRoot == null) return result;
            // Walked once for the whole scan: every importer asks it per item, and it reads every controller
            // of the avatar.
            var usage = ParameterUsage.For(avatarRoot, tree);

            foreach (var component in VrcFuryReader.Components(avatarRoot))
            {
                using (var so = new SerializedObject(component))
                    if (!VrcFuryReader.IsFeature(VrcFuryReader.Content(so), VrcFuryReader.ToggleTypeName)) continue;
                var candidate = VrcFuryImport.Build(component, avatarRoot);
                if (candidate != null) result.Add(candidate);
            }

#if DOCME_MA
            result.AddRange(ModularAvatarImport.Scan(avatarRoot, tree));
            result.AddRange(MergeAnimatorImport.Scan(avatarRoot, tree, usage));
            result.AddRange(ShapesImport.Scan(avatarRoot));
            // Last of the Modular Avatar readers: it joins the groups the merge importer has already made.
            result.AddRange(PrefabTakeover.Scan(avatarRoot, tree, result));
#endif
            result.AddRange(FxLayerImport.Scan(avatarRoot, tree, usage));
            return result;
        }
    }
}
#endif
