#if DOCME_NDMF && DOCME_VRCSDK3A
#if DOCME_MA
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Converts a Modular Avatar toggle menu item, with its reactive components, into an import candidate.</summary>
    public static class ModularAvatarImport
    {
        public static List<ImportCandidate> Scan(Transform avatarRoot, MenuTree tree)
        {
            var result = new List<ImportCandidate>();
            if (avatarRoot == null) return result;
            var foreign = ForeignMenu.Read(avatarRoot, tree != null ? tree.Root : null);
            var built = new Dictionary<ModularAvatarMenuItem, List<ImportCandidate>>();
            foreach (var item in foreign)
            {
                if (item.Source != ForeignSource.ModularAvatar) continue;
                if (!(item.Origin is ModularAvatarMenuItem menuItem)) continue;
                if (menuItem.Control == null) continue;
                // A submenu item is a path segment, not something to import on its own; its children are scanned in turn.
                if (menuItem.Control.type == VRCExpressionsMenu.Control.ControlType.SubMenu) continue;
                // An item whose effects live in a merged animator belongs to the merge importer, which reads that
                // controller's layers instead of the reactive components this one looks for.
                if (MergeAnimatorImport.Claims(menuItem)) continue;
                var candidate = Build(menuItem, item);
                result.Add(candidate);
                if (!built.TryGetValue(menuItem, out var own)) built[menuItem] = own = new List<ImportCandidate>();
                own.Add(candidate);
            }
            ResolveSharedParameters(avatarRoot, built);
            return result;
        }

        /// <summary>
        /// Modular Avatar gives every item that names one parameter a value of its own, so toggles on a shared name
        /// are a radio set: one is on at a time. Converted one by one they would all write the same value to a Bool
        /// and switch together. The set converts whole, as one exclusive group named after the parameter, when
        /// every item in it is a toggle read on its own and their values can be told apart by a group. Otherwise
        /// every item is refused, since a control left behind would still drive a parameter nothing else makes; an
        /// item that could not be converted anyway keeps its own reason, so the creator can find the one that blocks the set.
        /// </summary>
        static void ResolveSharedParameters(Transform avatarRoot, Dictionary<ModularAvatarMenuItem, List<ImportCandidate>> built)
        {
            var sets = avatarRoot.GetComponentsInChildren<ModularAvatarMenuItem>(true)
                .Where(i => i.Control != null && i.Control.parameter != null && !string.IsNullOrEmpty(i.Control.parameter.name))
                .GroupBy(i => i.Control.parameter.name);
            foreach (var set in sets)
            {
                var members = set.ToList();
                if (members.Count < 2) continue;
                var candidates = members.Where(built.ContainsKey).SelectMany(m => built[m]).ToList();
                if (candidates.Count == 0) continue;

                bool whole = members.All(m => m.Control.type == VRCExpressionsMenu.Control.ControlType.Toggle
                    && built.TryGetValue(m, out var own) && own.All(c => c.Supported))
                    && !ExplicitValuesClash(members);
                foreach (var candidate in candidates)
                {
                    if (!whole)
                    {
                        if (!candidate.Supported) continue;
                        candidate.Supported = false;
                        candidate.Reason = L.Text("ad.import.reason.shared_parameter_controls");
                        continue;
                    }
                    candidate.Draft.exclusiveGroup = set.Key;
                    candidate.Draft.parameterName = "";
                    candidate.Draft.parameterValue = 1f;
                    candidate.GroupMembers = candidates;
                    candidate.Notes.Add(string.Format(L.Text("ad.import.note.ma_radio_set"), set.Key));
                }
            }
        }

        /// <summary>
        /// An item with an explicit value (automatic value off) is on while the parameter equals it, give or take
        /// 0.005. Two items on one value are one switch shown in two places, and an item on 0 comes on whenever
        /// another is switched off, because a menu toggle sets the parameter back to 0. A group of AvatarDoc toggles
        /// gives back neither, so a set with either is not converted.
        /// </summary>
        static bool ExplicitValuesClash(List<ModularAvatarMenuItem> members)
        {
            var values = members.Where(m => !m.automaticValue).Select(m => m.Control.value).ToList();
            for (int i = 0; i < values.Count; i++)
            {
                if (Mathf.Abs(values[i]) < 0.005f) return true;
                for (int j = i + 1; j < values.Count; j++)
                    if (Mathf.Abs(values[i] - values[j]) < 0.01f) return true;
            }
            return false;
        }

        static ImportCandidate Build(ModularAvatarMenuItem item, ForeignItem foreignItem)
        {
            var candidate = new ImportCandidate
            {
                Source = ImportSource.ModularAvatar,
                Path = foreignItem.Path.Count > 0 ? foreignItem.Path.Take(foreignItem.Path.Count - 1).ToList() : new List<string>(),
                OriginComponents = new List<Component> { item },
            };

            // An installer on the item's own object exists only to install this item; removing the item without it
            // would leave a dead installer behind. An installer that also carries its own menu asset is not this
            // item's alone, so it stays. Never look at a parent's installer: a parent submenu is the creator's own
            // structure, left alone even when this item is its only child.
            var installer = item.GetComponent<ModularAvatarMenuInstaller>();
            if (installer != null && installer.menuToAppend == null) candidate.OriginComponents.Add(installer);

            var control = item.Control;
            if (control.type != VRCExpressionsMenu.Control.ControlType.Toggle)
            {
                candidate.Draft = MenuEntry.Create(EntryKind.Toggle, foreignItem.RichName);
                candidate.Supported = false;
                candidate.Reason = MergedAnimatorReason(item) ?? L.Text("ad.import.reason.not_a_toggle");
                return candidate;
            }

            var draft = MenuEntry.Create(EntryKind.Toggle, foreignItem.RichName);
            draft.icon = control.icon;
            draft.saved = item.isSaved;
            draft.synced = item.isSynced;
            draft.defaultOn = item.isDefault;
            var parameterName = control.parameter != null ? control.parameter.name : "";
            if (!string.IsNullOrEmpty(parameterName))
            {
                draft.parameterName = parameterName;
                // An automatic value is picked by Modular Avatar's own build (1 for a lone toggle), so only an explicit one is carried over.
                if (!item.automaticValue) draft.parameterValue = control.value;
            }

            bool any = false;
            foreach (var toggle in ReactiveComponentsOf<ModularAvatarObjectToggle>(item))
            {
                candidate.OriginComponents.Add(toggle);
                foreach (var obj in toggle.Objects)
                {
                    var target = obj.Object != null ? obj.Object.Get(toggle) : null;
                    if (target == null) continue;
                    bool activeWhenOn = toggle.Inverted ? !obj.Active : obj.Active;
                    draft.objects.Add(new ObjectTarget { target = target, activeWhenOn = activeWhenOn });
                    any = true;
                }
            }
            foreach (var changer in ReactiveComponentsOf<ModularAvatarShapeChanger>(item))
            {
                candidate.OriginComponents.Add(changer);
                foreach (var shape in changer.Shapes)
                {
                    if (shape.ChangeType == ShapeChangeType.Delete)
                    {
                        candidate.Notes.Add(string.Format(L.Text("ad.import.note.shape_changer_delete"), shape.ShapeName));
                        continue;
                    }
                    var targetObj = shape.Object != null ? shape.Object.Get(changer) : null;
                    var renderer = targetObj != null ? targetObj.GetComponent<SkinnedMeshRenderer>() : null;
                    if (renderer == null) continue;
                    if (changer.Inverted)
                    {
                        candidate.Notes.Add(string.Format(L.Text("ad.import.note.reactive_inverted"), renderer.name + "/" + shape.ShapeName));
                        continue;
                    }
                    draft.blendShapes.Add(new BlendShapeTarget { renderer = renderer, shape = shape.ShapeName, weight = shape.Value });
                    any = true;
                }
            }
            foreach (var setter in ReactiveComponentsOf<ModularAvatarMaterialSetter>(item))
            {
                candidate.OriginComponents.Add(setter);
                foreach (var obj in setter.Objects)
                {
                    var targetObj = obj.Object != null ? obj.Object.Get(setter) : null;
                    var renderer = targetObj != null ? targetObj.GetComponent<Renderer>() : null;
                    if (renderer == null) continue;
                    if (setter.Inverted)
                    {
                        candidate.Notes.Add(string.Format(L.Text("ad.import.note.reactive_inverted"), renderer.name));
                        continue;
                    }
                    if (obj.MaterialIndex < 0 || obj.MaterialIndex >= renderer.sharedMaterials.Length)
                    {
                        candidate.Notes.Add(string.Format(L.Text("ad.import.note.slot_missing"), renderer.name));
                        continue;
                    }
                    draft.materials.Add(new MaterialTarget { renderer = renderer, slot = obj.MaterialIndex, material = obj.Material });
                    any = true;
                }
            }

            candidate.Draft = draft;
            candidate.Supported = any;
            if (!any) candidate.Reason = MergedAnimatorReason(item) ?? L.Text("ad.import.reason.no_supported_action");
            return candidate;
        }

        /// <summary>
        /// The reason to give for an item that carries no reactive components while a Merge Animator sits on it or above it:
        /// its effect lives in that animator controller, which the importer does not read. Null when there is none.
        /// </summary>
        static string MergedAnimatorReason(ModularAvatarMenuItem item)
        {
            var merge = item.GetComponentInParent<ModularAvatarMergeAnimator>(true);
            return merge == null ? null : string.Format(L.Text("ad.import.reason.ma_merge_animator"), merge.gameObject.name);
        }

        /// <summary>Reactive components on the item's own object or below it, whose nearest Modular Avatar menu item ancestor is this one. Mirrors the rule Modular Avatar's own build uses.</summary>
        static IEnumerable<T> ReactiveComponentsOf<T>(ModularAvatarMenuItem item) where T : Component
        {
            foreach (var component in item.GetComponentsInChildren<T>(true))
            {
                var nearest = component.GetComponentsInParent<ModularAvatarMenuItem>(true).FirstOrDefault();
                if (nearest == item) yield return component;
            }
        }
    }
}
#endif
#endif
