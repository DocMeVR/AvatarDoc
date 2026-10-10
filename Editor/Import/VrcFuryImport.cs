#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Converts a VRCFury toggle, read through VrcFuryReader, into an import candidate.</summary>
    public static class VrcFuryImport
    {
        /// <summary>Null when the component does not carry a Toggle feature.</summary>
        public static ImportCandidate Build(Component component, Transform avatarRoot)
        {
            var data = VrcFuryReader.ReadToggle(component);
            if (data == null) return null;

            var segments = VrcFuryReader.SplitMenuPath(data.Name);
            var leafName = segments.Count > 0 ? segments[segments.Count - 1] : "";
            var candidate = new ImportCandidate
            {
                Source = ImportSource.VRCFury,
                Path = segments.Count > 0 ? segments.Take(segments.Count - 1).ToList() : new List<string>(),
                OriginComponents = new List<Component> { component },
            };

            bool allBlendShape = data.Actions.Count > 0 && data.Actions.All(a => a.Kind == VrcFuryReader.VrcFuryActionKind.BlendShape);
            bool radial = data.Slider && allBlendShape;

            var draft = MenuEntry.Create(radial ? EntryKind.Radial : EntryKind.Toggle, leafName);
            // VRCFury has no per-toggle synced flag: every VRCFury toggle syncs, so the draft keeps MenuEntry's default of synced = true.
            draft.saved = data.Saved;
            draft.defaultOn = data.DefaultOn;
            if (radial) draft.defaultValue = data.DefaultSliderValue;
            draft.icon = data.Icon;
            if (!string.IsNullOrEmpty(data.GlobalParameter)) draft.parameterName = data.GlobalParameter;

            if (data.ExclusiveTags.Count > 0)
            {
                draft.exclusiveGroup = data.ExclusiveTags[0];
                if (data.ExclusiveTags.Count > 1)
                {
                    candidate.Notes.Add(string.Format(L.Text("ad.import.note.extra_tags"),
                        string.Join(", ", data.ExclusiveTags.Skip(1)), data.ExclusiveTags[0]));
                }
            }

            bool any = false;
            foreach (var action in data.Actions)
            {
                switch (action.Kind)
                {
                    case VrcFuryReader.VrcFuryActionKind.ObjectToggle:
                        if (action.ObjectToggleTarget != null)
                        {
                            bool activeWhenOn = action.ObjectToggleMode == VrcFuryReader.VrcFuryObjectToggleMode.TurnOn ? true
                                : action.ObjectToggleMode == VrcFuryReader.VrcFuryObjectToggleMode.TurnOff ? false
                                : !action.ObjectToggleTarget.activeSelf;
                            draft.objects.Add(new ObjectTarget { target = action.ObjectToggleTarget, activeWhenOn = activeWhenOn });
                            any = true;
                        }
                        break;

                    case VrcFuryReader.VrcFuryActionKind.BlendShape:
                        if (action.BlendShapeAllRenderers)
                        {
                            var renderers = avatarRoot != null
                                ? avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                                    .Where(r => r.sharedMesh != null && r.sharedMesh.GetBlendShapeIndex(action.BlendShapeName) >= 0)
                                : Enumerable.Empty<SkinnedMeshRenderer>();
                            foreach (var renderer in renderers)
                            {
                                draft.blendShapes.Add(BlendShapeTargetFor(renderer, action, radial));
                                any = true;
                            }
                        }
                        else if (action.BlendShapeRenderer is SkinnedMeshRenderer smr)
                        {
                            draft.blendShapes.Add(BlendShapeTargetFor(smr, action, radial));
                            any = true;
                        }
                        break;

                    case VrcFuryReader.VrcFuryActionKind.Material:
                        if (action.MaterialRenderer != null)
                        {
                            draft.materials.Add(new MaterialTarget { renderer = action.MaterialRenderer, slot = action.MaterialIndex, material = action.MaterialValue });
                            any = true;
                        }
                        break;

                    case VrcFuryReader.VrcFuryActionKind.MaterialProperty:
                        if (AddMaterialPropertyTargets(draft, action, avatarRoot)) any = true;
                        break;

                    case VrcFuryReader.VrcFuryActionKind.AnimationClip:
                        if (action.AnimationClip != null)
                        {
                            draft.animations.Add(new AnimationTarget { clip = action.AnimationClip, pathRoot = null });
                            candidate.Notes.Add(L.Text("ad.import.note.clip_paths"));
                            any = true;
                        }
                        break;

                    default:
                        candidate.Notes.Add(string.Format(L.Text("ad.import.note.unsupported_action"),
                            string.IsNullOrEmpty(action.TypeName) ? "?" : action.TypeName));
                        break;
                }
            }

            if (data.HasTransition) candidate.Notes.Add(L.Text("ad.import.note.transition"));
            if (data.SecurityEnabled) candidate.Notes.Add(L.Text("ad.import.note.security"));
            if (data.SeparateLocal) candidate.Notes.Add(L.Text("ad.import.note.separate_local"));
            if (data.HoldButton) candidate.Notes.Add(L.Text("ad.import.note.hold_button"));
            if (data.InvertRestLogic) candidate.Notes.Add(L.Text("ad.import.note.invert_rest_logic"));

            candidate.Draft = draft;
            candidate.Supported = any;
            if (!any) candidate.Reason = L.Text("ad.import.reason.no_supported_action");
            return candidate;
        }

        static BlendShapeTarget BlendShapeTargetFor(SkinnedMeshRenderer renderer, VrcFuryReader.VrcFuryAction action, bool radial)
        {
            var target = new BlendShapeTarget { renderer = renderer, shape = action.BlendShapeName };
            if (radial) { target.minWeight = 0f; target.maxWeight = action.BlendShapeValue; }
            else target.weight = action.BlendShapeValue;
            return target;
        }

        static bool AddMaterialPropertyTargets(MenuEntry draft, VrcFuryReader.VrcFuryAction action, Transform avatarRoot)
        {
            var renderers = new List<Renderer>();
            if (action.MaterialPropertyAffectAllMeshes)
            {
                if (avatarRoot != null)
                {
                    renderers.AddRange(avatarRoot.GetComponentsInChildren<Renderer>(true)
                        .Where(r => MenuModel.MaterialWithProperty(r, action.MaterialPropertyName) != null));
                }
            }
            else if (action.MaterialPropertyTargetObject != null)
            {
                var renderer = action.MaterialPropertyTargetObject.GetComponent<Renderer>();
                if (renderer != null) renderers.Add(renderer);
            }

            bool any = false;
            foreach (var renderer in renderers)
            {
                var type = ResolvePropertyType(action, renderer);
                var target = new MaterialPropertyTarget { renderer = renderer, property = action.MaterialPropertyName, type = type };
                switch (type)
                {
                    case PropertyValueType.Color: target.color = action.MaterialPropertyColor; break;
                    case PropertyValueType.Vector: target.vector = action.MaterialPropertyVector; break;
                    default: target.value = action.MaterialPropertyValue; break;
                }
                draft.properties.Add(target);
                any = true;
            }
            return any;
        }

        static PropertyValueType ResolvePropertyType(VrcFuryReader.VrcFuryAction action, Renderer renderer)
        {
            switch (action.MaterialPropertyKind)
            {
                case VrcFuryReader.VrcFuryMaterialPropertyType.Float: return PropertyValueType.Float;
                case VrcFuryReader.VrcFuryMaterialPropertyType.Color: return PropertyValueType.Color;
                case VrcFuryReader.VrcFuryMaterialPropertyType.Vector: return PropertyValueType.Vector;
                case VrcFuryReader.VrcFuryMaterialPropertyType.St: return PropertyValueType.Vector;
                default:
                    var match = PropertyPicker.Choices(renderer).FirstOrDefault(c => c.PropertyName == action.MaterialPropertyName);
                    return match.PropertyName == action.MaterialPropertyName ? match.Type : PropertyValueType.Float;
            }
        }
    }
}
#endif
