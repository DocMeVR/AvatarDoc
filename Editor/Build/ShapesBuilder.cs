#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The pass for the AvatarDoc Shapes components on the outfit pieces, run after Modular Avatar so every clip
    /// that switches a piece is already in the FX controller. It adds curves to those clips and sets the start
    /// weights on the clone; it never adds a parameter or a layer, and it never touches an asset.
    /// </summary>
    public static class ShapesBuilder
    {
        /// <summary>One clip that switches a piece, and whether it switches it on.</summary>
        struct Switch
        {
            public VirtualClip Clip;
            public bool On;
        }

        /// <summary>What one clip is to write for one shape: the largest value of the pieces it turns on, or the rest value when it turns every piece that asks for this shape off.</summary>
        struct Held
        {
            public float OnValue;
            public bool AnyOn;
            public float Rest;
        }

        public static void Run(BuildContext context)
        {
            var root = context.AvatarRootTransform;
            if (root == null) return;
            var all = root.GetComponentsInChildren<AvatarDocShapes>(true);
            if (all.Length == 0) return;

            var plan = ShapesModel.Plan(root);
            var messages = new List<ModelMessage>(plan.Messages);
            var pieces = plan.Included.ToList();
            if (pieces.Count > 0) Apply(context.Extension<AnimatorServicesContext>(), root, pieces, messages);
            L.Report(messages);

            foreach (var component in all)
                if (component != null) Object.DestroyImmediate(component);
        }

        static void Apply(AnimatorServicesContext asc, Transform root, List<ShapesPiece> pieces, List<ModelMessage> messages)
        {
            // Read before anything writes: the rest value of a shape is what the clone held before any piece
            // touched it, and two pieces on one shape must not read each other's work as the rest.
            var rest = new Dictionary<(SkinnedMeshRenderer, string), float>();
            foreach (var piece in pieces)
                foreach (var row in piece.WhileOn)
                {
                    var key = (row.Mesh, row.Shape);
                    if (!rest.ContainsKey(key)) rest[key] = Weight(row.Mesh, row.Shape);
                }

            // The spec says FX: a Gesture or Action clip that happens to switch the piece is none of this pass's
            // business, and a curve copied into an additive layer would be applied on top of the FX result.
            var fxClips = FxClips(asc);
            var switches = new Dictionary<ShapesPiece, List<Switch>>();
            foreach (var piece in pieces) switches[piece] = SwitchesOf(asc, root, piece, fxClips, messages);

            var ours = WhileOn(asc, root, pieces, switches, rest, messages);
            Follow(asc, pieces, fxClips, ours, messages);
        }

        /// <summary>Every clip the FX controller can reach. An avatar without one is left alone.</summary>
        static HashSet<VirtualClip> FxClips(AnimatorServicesContext asc)
        {
            var result = new HashSet<VirtualClip>();
            if (!asc.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) || fx == null) return result;
            foreach (var clip in fx.AllReachableNodes().OfType<VirtualClip>()) result.Add(clip);
            return result;
        }

        /// <summary>
        /// Writes the while-on values into the clips that switch each piece, and onto the clone for a piece that
        /// is on right now. Returns the curves this pass wrote, so the follow pass knows them from a clip's own.
        /// </summary>
        static HashSet<(VirtualClip, EditorCurveBinding)> WhileOn(AnimatorServicesContext asc, Transform root, List<ShapesPiece> pieces,
            Dictionary<ShapesPiece, List<Switch>> switches, Dictionary<(SkinnedMeshRenderer, string), float> rest,
            List<ModelMessage> messages)
        {
            var writes = new Dictionary<(VirtualClip, EditorCurveBinding), Held>();
            var owned = new HashSet<(VirtualClip, EditorCurveBinding)>();
            var scene = new Dictionary<(SkinnedMeshRenderer, string), float>();
            var saidOwned = new HashSet<(VirtualClip, EditorCurveBinding)>();
            var written = new HashSet<(VirtualClip, EditorCurveBinding)>();

            foreach (var piece in pieces)
            {
                var list = switches[piece];
                bool onInScene = ShapesModel.ActiveInAvatar(piece.Object, root);
                if (list.Count == 0 && piece.WhileOn.Count > 0)
                    messages.Add(onInScene
                        ? ModelMessage.Info("ad.info.shapes_always_on", piece.Name, piece.Component)
                        : ModelMessage.Info("ad.info.shapes_never_on", piece.Name, piece.Component));

                foreach (var row in piece.WhileOn)
                {
                    var binding = ShapeBinding(asc, row.Mesh, row.Shape);
                    float restValue = rest[(row.Mesh, row.Shape)];
                    foreach (var entry in list)
                    {
                        var key = (entry.Clip, binding);
                        if (owned.Contains(key)) continue;
                        // Nothing is written into a clip before every piece has been read, so a curve found here
                        // is always the clip's own.
                        if (entry.Clip.GetFloatCurve(binding) != null)
                        {
                            owned.Add(key);
                            if (saidOwned.Add(key))
                                messages.Add(ModelMessage.Warning("ad.warn.shapes_clip_owns_shape", entry.Clip.Name, row.Shape, piece.Name, piece.Component));
                            continue;
                        }
                        var held = writes.TryGetValue(key, out var existing) ? existing : new Held { Rest = restValue };
                        // A piece this clip turns off only offers the rest value, and only for a clip where no
                        // piece at all is on: otherwise the piece that is really worn would lose to a rest value
                        // above its own.
                        if (entry.On)
                        {
                            held.OnValue = held.AnyOn ? Mathf.Max(held.OnValue, row.Value) : row.Value;
                            held.AnyOn = true;
                        }
                        writes[key] = held;
                    }
                    if (!onInScene) continue;
                    var sceneKey = (row.Mesh, row.Shape);
                    scene[sceneKey] = scene.TryGetValue(sceneKey, out var sceneValue) ? Mathf.Max(sceneValue, row.Value) : row.Value;
                }
            }

            foreach (var pair in writes)
            {
                if (owned.Contains(pair.Key)) continue;
                var (clip, binding) = pair.Key;
                clip.SetFloatCurve(binding, Constant(pair.Value.AnyOn ? pair.Value.OnValue : pair.Value.Rest));
                written.Add(pair.Key);
            }
            foreach (var pair in scene)
            {
                var (mesh, shape) = pair.Key;
                int index = mesh.sharedMesh.GetBlendShapeIndex(shape);
                if (index >= 0) mesh.SetBlendShapeWeight(index, pair.Value);
            }
            return written;
        }

        /// <summary>Copies every source curve onto the piece's own shape, and sets the piece's start weight from the source's.</summary>
        static void Follow(AnimatorServicesContext asc, List<ShapesPiece> pieces, HashSet<VirtualClip> fxClips,
            HashSet<(VirtualClip, EditorCurveBinding)> ours, List<ModelMessage> messages)
        {
            foreach (var piece in pieces)
                foreach (var row in piece.Follows)
                {
                    var source = ShapeBinding(asc, row.Source, row.SourceShape);
                    var local = ShapeBinding(asc, piece.LocalMesh, row.LocalShape);
                    if (source.Equals(local)) continue;
                    foreach (var clip in asc.AnimationIndex.GetClipsForBinding(source).ToList())
                    {
                        if (!fxClips.Contains(clip)) continue;
                        if (clip.IsMarkerClip)
                        {
                            messages.Add(ModelMessage.Warning("ad.warn.marker_clip_skipped", clip.Name, piece.Name));
                            continue;
                        }
                        var curve = clip.GetFloatCurve(source);
                        if (curve == null || curve.length == 0) continue;
                        if (clip.GetFloatCurve(local) != null)
                        {
                            // A while-on row of this same pass wrote it: that value wins, and the clip is not
                            // blamed for something it never had.
                            if (!ours.Contains((clip, local)))
                                messages.Add(ModelMessage.Info("ad.info.shapes_follow_kept_own", clip.Name, row.LocalShape, piece.Name, piece.Component));
                            continue;
                        }
                        clip.SetFloatCurve(local, Copy(curve));
                    }
                    int index = piece.LocalMesh.sharedMesh.GetBlendShapeIndex(row.LocalShape);
                    if (index >= 0) piece.LocalMesh.SetBlendShapeWeight(index, Weight(row.Source, row.SourceShape));
                }
        }

        /// <summary>
        /// The clips that switch this piece: the ones with a constant active curve on the object itself, or, when
        /// nothing animates the object, the ones on the nearest ancestor that is animated, as long as everything
        /// between is on in the scene and animated by nothing.
        /// </summary>
        static List<Switch> SwitchesOf(AnimatorServicesContext asc, Transform root, ShapesPiece piece,
            HashSet<VirtualClip> fxClips, List<ModelMessage> messages)
        {
            var result = new List<Switch>();
            for (var cursor = piece.Object.transform; cursor != null; cursor = cursor.parent)
            {
                var binding = EditorCurveBinding.FloatCurve(asc.ObjectPathRemapper.GetVirtualPathForObject(cursor.gameObject), typeof(GameObject), "m_IsActive");
                var clips = asc.AnimationIndex.GetClipsForBinding(binding).Where(fxClips.Contains).ToList();
                foreach (var clip in clips)
                {
                    if (clip.IsMarkerClip)
                    {
                        messages.Add(ModelMessage.Warning("ad.warn.marker_clip_skipped", clip.Name, piece.Name));
                        continue;
                    }
                    var curve = clip.GetFloatCurve(binding);
                    if (!TryConstant(curve, out float value))
                    {
                        messages.Add(ModelMessage.Warning("ad.warn.shapes_active_not_constant", clip.Name, piece.Name, piece.Component));
                        continue;
                    }
                    result.Add(new Switch { Clip = clip, On = value >= 0.5f });
                }
                // Whatever animates this object decides; an ancestor above it says nothing about when the piece
                // is really on. Without such a clip the object has to be on and stay on for its parent to decide.
                if (clips.Count > 0 || cursor == root || !cursor.gameObject.activeSelf) break;
            }
            return result;
        }

        static EditorCurveBinding ShapeBinding(AnimatorServicesContext asc, SkinnedMeshRenderer renderer, string shape)
            => EditorCurveBinding.FloatCurve(asc.ObjectPathRemapper.GetVirtualPathForObject(renderer.gameObject), typeof(SkinnedMeshRenderer), "blendShape." + shape);

        static float Weight(SkinnedMeshRenderer renderer, string shape)
        {
            int index = renderer != null && renderer.sharedMesh != null ? renderer.sharedMesh.GetBlendShapeIndex(shape) : -1;
            return index >= 0 ? renderer.GetBlendShapeWeight(index) : 0f;
        }

        /// <summary>The value of a curve that holds one value the whole way through. False for anything that moves.</summary>
        static bool TryConstant(AnimationCurve curve, out float value)
        {
            value = 0f;
            if (curve == null || curve.length == 0) return false;
            value = curve.keys[0].value;
            for (int i = 1; i < curve.length; i++)
                if (!Mathf.Approximately(curve.keys[i].value, value)) return false;
            return true;
        }

        /// <summary>Two keys at the same value, the shape every generated AvatarDoc curve has.</summary>
        static AnimationCurve Constant(float value)
        {
            var curve = new AnimationCurve(
                new Keyframe(0f, value) { inTangent = 0f, outTangent = 0f },
                new Keyframe(1f / 60f, value) { inTangent = 0f, outTangent = 0f });
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
            }
            return curve;
        }

        /// <summary>A curve of its own with the same keys, so the two clips never share one object.</summary>
        static AnimationCurve Copy(AnimationCurve source)
            => new AnimationCurve(source.keys) { preWrapMode = source.preWrapMode, postWrapMode = source.postWrapMode };
    }
}
#endif
