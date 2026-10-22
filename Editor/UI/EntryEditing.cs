#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Undo-recorded edits of a component's entry list. Sibling order is list order.</summary>
    public static class EntryEditing
    {
        /// <summary>Where the line about ids repaired in another component goes. Replaceable by a test that reads it.</summary>
        public static Action<string> Log = message => Debug.Log(message);

        public static List<MenuEntry> Siblings(AvatarDoc menu, string parentId)
        {
            parentId = parentId ?? "";
            return menu.entries.Where(e => !e.IsRule && (e.parentId ?? "") == parentId).ToList();
        }

        public static List<MenuEntry> Descendants(AvatarDoc menu, MenuEntry entry)
        {
            var result = new List<MenuEntry>();
            if (entry.kind != EntryKind.Submenu) return result;
            var queue = new Queue<string>();
            queue.Enqueue(entry.id);
            var seen = new HashSet<string> { entry.id };
            while (queue.Count > 0)
            {
                var parent = queue.Dequeue();
                foreach (var child in menu.entries.Where(e => e.parentId == parent))
                {
                    if (!seen.Add(child.id)) continue;
                    result.Add(child);
                    if (child.kind == EntryKind.Submenu) queue.Enqueue(child.id);
                }
            }
            return result;
        }

        public static bool IsDescendantOf(AvatarDoc menu, MenuEntry entry, string ancestorId)
        {
            return Descendants(menu, menu.FindEntry(ancestorId) ?? new MenuEntry { id = ancestorId, kind = EntryKind.Submenu }).Contains(entry);
        }

        /// <summary>Whether a write to this component's entry data is refused: no component at all, or one its maker has locked. The one guard every entry-data mutator (here, in MenuEditing, and in the helpers built on them) calls first.</summary>
        public static bool Refuses(AvatarDoc menu) => menu == null || menu.locked;

        /// <summary>Marks an edited component dirty. On a prefab instance the edit also has to be written as an override, or it is lost on the next reload.</summary>
        internal static void MarkChanged(UnityEngine.Object target)
        {
            EditorUtility.SetDirty(target);
            if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }

        /// <summary>Gives the members of one exclusive group the values 1, 2, 3 and so on in the order given, in one Undo step. Refused, changing nothing, when any component holding a member is locked.</summary>
        public static bool NumberGroupMembers(IReadOnlyList<ControlNode> members)
        {
            var live = members.Where(m => m != null && m.Component != null && !m.Disabled).ToList();
            if (live.Count == 0 || live.Any(m => Refuses(m.Component))) return false;
            var components = live.Select(m => m.Component).Distinct().ToArray();
            Undo.RecordObjects(components, "Give each member its own value");
            for (int i = 0; i < live.Count; i++)
            {
                var entry = live[i].Component.FindEntry(live[i].Entry.id);
                if (entry != null) entry.groupValue = i + 1;
            }
            foreach (var component in components) MarkChanged(component);
            return true;
        }

        /// <summary>The members of from's exclusive group of this name, those other components link to it included. Empty when there is none.</summary>
        static List<ControlNode> GroupMembers(MenuTree tree, AvatarDoc from, string name)
        {
            if (tree == null || from == null || string.IsNullOrWhiteSpace(name)) return new List<ControlNode>();
            var group = new GroupKey(from, name.Trim());
            return tree.Controls.Where(c => !c.Disabled && c.Component != null && tree.GroupOf(c) == group).ToList();
        }

        /// <summary>Whether JoinGroup would do it: from has a group of that name, and no component holding one of its members is locked.</summary>
        public static bool CanJoinGroup(MenuTree tree, AvatarDoc from, string name, AvatarDoc into)
        {
            if (into == null || from == into) return false;
            var members = GroupMembers(tree, from, name);
            return members.Count > 0 && !members.Any(m => Refuses(m.Component));
        }

        /// <summary>
        /// Makes from's exclusive group of this name part of into's group of that name: every member of from's group
        /// links to into, in one Undo step, and a member that sits on into itself becomes into's own again. Refused,
        /// changing nothing, when a component holding one of the members is locked.
        /// </summary>
        public static bool JoinGroup(MenuTree tree, AvatarDoc from, string name, AvatarDoc into) => JoinGroup(tree, from, name, into, out _);

        /// <summary>The same, handing out the components it changed.</summary>
        public static bool JoinGroup(MenuTree tree, AvatarDoc from, string name, AvatarDoc into, out AvatarDoc[] changed)
        {
            changed = Array.Empty<AvatarDoc>();
            if (!CanJoinGroup(tree, from, name, into)) return false;
            var members = GroupMembers(tree, from, name);
            var components = members.Select(m => m.Component).Distinct().ToArray();
            changed = components;
            Undo.RecordObjects(components, "Join exclusive groups");
            foreach (var member in members)
            {
                var entry = member.Component.FindEntry(member.Entry.id);
                if (entry != null) entry.groupComponent = member.Component == into ? null : into;
            }
            foreach (var component in components) MarkChanged(component);
            return true;
        }

        /// <summary>
        /// Gives the component a parameter prefix none of the others has: the one a new component on its object
        /// starts with (AvatarDoc.PrefixFor), with 2, 3 and so on after it while that is taken too. One Undo step.
        /// Not refused on a locked component, whose prefix is the buyer's to set. False when it already has that prefix.
        /// </summary>
        public static bool GiveOwnPrefix(AvatarDoc component, IEnumerable<AvatarDoc> others)
        {
            if (component == null) return false;
            var taken = new HashSet<string>((others ?? Enumerable.Empty<AvatarDoc>()).Where(o => o != null && o != component).Select(o => (o.parameterPrefix ?? "").Trim()));
            var wanted = AvatarDoc.PrefixFor(component.gameObject);
            var chosen = wanted;
            for (int i = 2; taken.Contains(chosen); i++) chosen = wanted + i;
            if (chosen == component.parameterPrefix) return false;
            Undo.RecordObject(component, "Give this component its own prefix");
            component.parameterPrefix = chosen;
            MarkChanged(component);
            return true;
        }

        /// <summary>
        /// Marks the property "Animated (when locked)" on every unlocked Poiyomi material that does not mark it yet, in
        /// one Undo step, so Poiyomi keeps it animated when it locks them at upload: the renderer's own, the ones the
        /// entry swaps onto it and the ones given in swapped (the rest of the avatar's swaps). A variant's mark goes on
        /// its root. Refused, changing nothing, while the component that holds the entry is locked.
        /// </summary>
        public static bool MarkAnimatedForPoiyomi(AvatarDoc menu, MenuEntry entry, Renderer renderer, string property, IEnumerable<Material> swapped = null)
        {
            if (Refuses(menu) || entry == null) return false;
            var reached = entry.materials.Where(m => m != null && m.renderer == renderer).Select(m => m.material);
            if (swapped != null) reached = reached.Concat(swapped);
            return PoiyomiLock.MarkAnimated(PoiyomiLock.Unmarked(renderer, property, reached), property);
        }

        public static MenuEntry Add(AvatarDoc menu, EntryKind kind, string name, string parentId)
        {
            if (Refuses(menu)) return null;
            Undo.RecordObject(menu, "Add " + kind);
            var entry = menu.AddEntry(kind, name, parentId);
            MarkChanged(menu);
            return entry;
        }

        /// <summary>
        /// Gives a toggle or a radial its blend shape row for this shape: the row it already has keeps its place and
        /// takes the values, else a new row is added. A toggle's row writes on while on; while off it goes back to the
        /// scene's weight when offIsRest, else it writes off. A pinned row stays pinned and only takes on. A radial's
        /// row runs from off at its lowest to on at its highest. Refused, changing nothing, on a locked component or
        /// an entry that is neither a toggle nor a radial. One Undo step.
        /// </summary>
        public static BlendShapeTarget PutShape(AvatarDoc menu, MenuEntry entry, SkinnedMeshRenderer renderer, string shape, float on, float off, bool offIsRest)
        {
            if (Refuses(menu) || entry == null || renderer == null || string.IsNullOrEmpty(shape)) return null;
            if (entry.kind != EntryKind.Toggle && entry.kind != EntryKind.Radial) return null;
            Undo.RecordObject(menu, "Add blend shape");
            var row = entry.blendShapes.FirstOrDefault(t => t != null && t.renderer == renderer && t.shape == shape);
            if (row == null)
            {
                row = new BlendShapeTarget { renderer = renderer, shape = shape, mode = ShapeEffectMode.Fixed };
                entry.blendShapes.Add(row);
            }
            if (entry.kind == EntryKind.Radial)
            {
                row.minWeight = off;
                row.maxWeight = on;
            }
            else
            {
                row.weight = on;
                bool pinned = row.mode == ShapeEffectMode.Pin;
                row.explicitOff = !pinned && !offIsRest;
                if (row.explicitOff) row.minWeight = off;
            }
            MarkChanged(menu);
            return row;
        }

        /// <summary>
        /// Gives a toggle or a radial its material property row for this property on the renderer, the same way
        /// PutShape does for a blend shape: a toggle writes on while on and, unless offIsRest, off while off; a
        /// radial runs from off to on. on and off are packed: a number in x, a colour in rgba, a vector in xyzw.
        /// Refused, changing nothing, on a locked component. One Undo step.
        /// </summary>
        public static MaterialPropertyTarget PutProperty(AvatarDoc menu, MenuEntry entry, Renderer renderer, string property, PropertyValueType type, Vector4 on, Vector4 off, bool offIsRest)
        {
            if (Refuses(menu) || entry == null || renderer == null || string.IsNullOrEmpty(property)) return null;
            if (entry.kind != EntryKind.Toggle && entry.kind != EntryKind.Radial) return null;
            Undo.RecordObject(menu, "Add material property");
            var row = entry.properties.FirstOrDefault(t => t != null && t.renderer == renderer && t.property == property);
            if (row == null)
            {
                row = new MaterialPropertyTarget { renderer = renderer, property = property };
                entry.properties.Add(row);
            }
            row.type = type;
            bool radial = entry.kind == EntryKind.Radial;
            row.explicitOff = !radial && !offIsRest;
            row.value = on.x;
            row.color = on;
            row.vector = on;
            if (radial || row.explicitOff)
            {
                row.minValue = off.x;
                row.minColor = off;
                row.minVector = off;
            }
            MarkChanged(menu);
            return row;
        }

        /// <summary>
        /// Creates a grabbable entry: both hands, snapped into the hand (keep offset off), anyone may grab it, a
        /// quick return, no menu control until the creator turns one on. The object is left for the creator to
        /// pick. One Undo step.
        /// </summary>
        public static MenuEntry AddGrabbable(AvatarDoc menu, string parentId)
        {
            if (Refuses(menu)) return null;
            Undo.RecordObject(menu, "Add Grabbable");
            var entry = menu.AddEntry(EntryKind.Grabbable, "New Grabbable", parentId);
            entry.grabHands = GrabHands.Both;
            entry.grabKeepOffset = false;
            entry.grabAnyone = true;
            entry.grabReturnSeconds = 0.15f;
            entry.grabReach = 1f;
            entry.grabMenuToggle = false;
            MarkChanged(menu);
            return entry;
        }

        public static MenuEntry Duplicate(AvatarDoc menu, MenuEntry entry)
        {
            if (Refuses(menu)) return null;
            Undo.RecordObject(menu, "Duplicate " + entry.PlainName);
            var idMap = new Dictionary<string, string>();
            var copy = Clone(entry, idMap);
            copy.name = RichName.Append(entry.name, " Copy");
            int index = menu.entries.IndexOf(entry);
            var descendants = Descendants(menu, entry);
            var copies = new List<MenuEntry> { copy };
            foreach (var descendant in descendants) copies.Add(Clone(descendant, idMap));
            // A copy is the creator's own entry: the Gesture faces window keeps editing the one it made.
            foreach (var c in copies) c.gestureFace = "";
            foreach (var c in copies.Skip(1)) c.parentId = idMap.TryGetValue(c.parentId, out var mapped) ? mapped : c.parentId;
            // A preset or a trigger copied along with the controls it names has to follow the copies.
            foreach (var c in copies) RemapLinks(c, idMap);
            int insertAt = index + 1;
            foreach (var descendant in descendants) insertAt = Math.Max(insertAt, menu.entries.IndexOf(descendant) + 1);
            menu.entries.InsertRange(insertAt, copies);
            MarkChanged(menu);
            return copy;
        }

        /// <summary>Points a preset's states, a trigger's source toggle, the toggles its conditions read and a touch trigger's flip target at the new ids of the entries that were copied or re-identified in the same operation. Anything else they name is left alone.</summary>
        static void RemapLinks(MenuEntry entry, Dictionary<string, string> idMap)
        {
            if (!string.IsNullOrEmpty(entry.sourceToggleId) && idMap.TryGetValue(entry.sourceToggleId, out var source)) entry.sourceToggleId = source;
            foreach (var (_, condition, _, _) in entry.StoredConditions())
                if (condition.kind == WhenConditionKind.Toggle && !string.IsNullOrEmpty(condition.sourceToggleId)
                    && idMap.TryGetValue(condition.sourceToggleId, out var inCondition)) condition.sourceToggleId = inCondition;
            if (!string.IsNullOrEmpty(entry.flipToggleId) && idMap.TryGetValue(entry.flipToggleId, out var flip)) entry.flipToggleId = flip;
            if (!string.IsNullOrEmpty(entry.gateToggleId) && idMap.TryGetValue(entry.gateToggleId, out var gate)) entry.gateToggleId = gate;
            foreach (var state in entry.presetStates)
                if (!string.IsNullOrEmpty(state.entryId) && idMap.TryGetValue(state.entryId, out var target)) state.entryId = target;
        }

        static MenuEntry Clone(MenuEntry source, Dictionary<string, string> idMap)
        {
            var copy = JsonUtility.FromJson<MenuEntry>(JsonUtility.ToJson(source));
            copy.id = Guid.NewGuid().ToString("N");
            idMap[source.id] = copy.id;
            return copy;
        }

        /// <summary>Deletes the entry and everything under it. False, writing nothing, on a locked component.</summary>
        public static bool Delete(AvatarDoc menu, MenuEntry entry)
        {
            if (Refuses(menu)) return false;
            Undo.RecordObject(menu, "Delete " + entry.PlainName);
            foreach (var descendant in Descendants(menu, entry)) menu.entries.Remove(descendant);
            menu.entries.Remove(entry);
            MarkChanged(menu);
            return true;
        }

        public static bool MoveTo(AvatarDoc menu, MenuEntry entry, string newParentId)
        {
            return Reorder(menu, entry, newParentId, int.MaxValue);
        }

        /// <summary>Puts the entry under newParentId at siblingIndex among that parent's non-trigger entries. Refuses moves into the entry itself or its descendants.</summary>
        public static bool Reorder(AvatarDoc menu, MenuEntry entry, string newParentId, int siblingIndex)
        {
            if (Refuses(menu)) return false;
            newParentId = newParentId ?? "";
            if (newParentId == entry.id) return false;
            if (newParentId.Length > 0 && menu.FindEntry(newParentId) == null) return false;
            if (newParentId.Length > 0 && IsDescendantOf(menu, menu.FindEntry(newParentId), entry.id)) return false;

            Undo.RecordObject(menu, "Move " + entry.PlainName);
            menu.entries.Remove(entry);
            entry.parentId = newParentId;
            var siblings = Siblings(menu, newParentId);
            int listIndex;
            if (siblingIndex >= siblings.Count) listIndex = siblings.Count == 0 ? menu.entries.Count : menu.entries.IndexOf(siblings[siblings.Count - 1]) + 1;
            else listIndex = menu.entries.IndexOf(siblings[Math.Max(0, siblingIndex)]);
            menu.entries.Insert(listIndex, entry);
            MarkChanged(menu);
            return true;
        }

        /// <summary>Renames a submenu of this component and rewrites the install paths of other components that point at it or below it. An entry this component does not hold is refused: its Undo record and its lock belong to another component. False when nothing was written: a refusal, a blank name or the same name.</summary>
        public static bool RenameSubmenu(AvatarDoc menu, MenuEntry submenu, string newName, MenuTree tree)
        {
            if (Refuses(menu) || submenu == null || !menu.entries.Contains(submenu)) return false;
            newName = (newName ?? "").Trim();
            if (newName.Length == 0 || newName == submenu.name) return false;
            var node = tree.AllNodes().FirstOrDefault(n => n.EntryId == submenu.id && n.Component == menu);
            var oldPath = node != null ? node.Path : null;
            Undo.RecordObject(menu, "Rename " + submenu.PlainName);
            submenu.name = newName;
            MarkChanged(menu);
            if (oldPath == null) return true;

            var oldSegments = MenuModel.SplitPath(oldPath);
            // A style-only change leaves the plain segment alone, and the install paths of the other components
            // are plain, so there is nothing for them to follow.
            if (oldSegments.Length == 0 || RichName.Strip(newName) == oldSegments[oldSegments.Length - 1]) return true;
            foreach (var other in tree.Components)
            {
                if (other == menu) continue;
                var segments = MenuModel.SplitPath(other.installPath);
                if (segments.Length < oldSegments.Length) continue;
                if (!oldSegments.SequenceEqual(segments.Take(oldSegments.Length))) continue;
                segments[oldSegments.Length - 1] = RichName.Strip(newName);
                Undo.RecordObject(other, "Rename " + submenu.PlainName);
                other.installPath = string.Join("/", segments);
                MarkChanged(other);
            }
            return true;
        }

        /// <summary>
        /// Creates an ordinary toggle to gate a trigger: named after the trigger, on by default, saved, synced,
        /// no targets, and sets it as the trigger's gate. The model skips the no-effects warning for exactly this
        /// kind of toggle, since a plain switch is what it is meant to be. One Undo group.
        /// </summary>
        public static MenuEntry AddGateSwitch(AvatarDoc menu, MenuEntry trigger)
        {
            if (Refuses(menu) || trigger == null || trigger.kind != EntryKind.Trigger) return null;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add menu switch");
            Undo.RecordObject(menu, "Add menu switch");
            MenuEntry toggle;
            try
            {
                toggle = menu.AddEntry(EntryKind.Toggle, trigger.PlainName, "");
                toggle.defaultOn = true;
                toggle.saved = true;
                toggle.synced = true;
                trigger.gateToggleId = toggle.id;
            }
            finally
            {
                MarkChanged(menu);
                Undo.CollapseUndoOperations(group);
            }
            return toggle;
        }

        /// <summary>
        /// Gives a trigger its first group, so a second condition can join it. Does nothing once the trigger has
        /// groups. A trigger's flat single source moves into the group first; the flat fields are left as they are,
        /// simply not read any more, which keeps an undo of the whole step trivial. A blend shape trigger's shape
        /// stays in its flat fields, since it is the driver and not a condition, so its first group starts empty
        /// and holds requirements only.
        /// </summary>
        static void EnsureGroups(MenuEntry trigger)
        {
            if (trigger.when == null) trigger.when = new List<ConditionGroup>();
            if (trigger.when.Count > 0) return;
            if (trigger.source == TriggerSource.BlendShape) trigger.when.Add(new ConditionGroup());
            else trigger.when.Add(new ConditionGroup { all = { WhenCondition.FromEntry(trigger) } });
        }

        /// <summary>Adds a condition to one group of a trigger, moving the flat single source into the first group if that has not happened yet. On a blend shape trigger or a Fade contact it is a requirement, added to the card the driver reads. Returns the condition, or null when the write is refused.</summary>
        public static WhenCondition AddCondition(AvatarDoc menu, MenuEntry trigger, int groupIndex)
        {
            if (Refuses(menu) || trigger == null || trigger.kind != EntryKind.Trigger) return null;
            Undo.RecordObject(menu, "Add condition");
            EnsureGroups(trigger);
            if (trigger.HasDriver && trigger.RequirementGroupIndex >= 0) groupIndex = trigger.RequirementGroupIndex;
            if (groupIndex < 0 || groupIndex >= trigger.when.Count) groupIndex = trigger.when.Count - 1;
            var condition = new WhenCondition();
            trigger.when[groupIndex].all.Add(condition);
            MarkChanged(menu);
            return condition;
        }

        /// <summary>Puts the condition the trigger's flat source fields describe first in one of its groups: what a trigger that was following a blend shape keeps when its source is switched away while it has requirements, since its group becomes its whole rule.</summary>
        public static WhenCondition PutFlatSourceFirst(AvatarDoc menu, MenuEntry trigger, int groupIndex)
        {
            if (Refuses(menu) || trigger == null || trigger.kind != EntryKind.Trigger || trigger.source == TriggerSource.BlendShape) return null;
            if (trigger.when == null || groupIndex < 0 || groupIndex >= trigger.when.Count || trigger.when[groupIndex] == null) return null;
            Undo.RecordObject(menu, "Change trigger source");
            var condition = WhenCondition.FromEntry(trigger);
            trigger.when[groupIndex].all.Insert(0, condition);
            MarkChanged(menu);
            return condition;
        }

        /// <summary>Adds a group with one condition in it: the "or" of the rule.</summary>
        public static WhenCondition AddConditionGroup(AvatarDoc menu, MenuEntry trigger)
        {
            if (Refuses(menu) || trigger == null || trigger.kind != EntryKind.Trigger) return null;
            Undo.RecordObject(menu, "Add condition group");
            EnsureGroups(trigger);
            var condition = new WhenCondition();
            trigger.when.Add(new ConditionGroup { all = { condition } });
            MarkChanged(menu);
            return condition;
        }

        /// <summary>Removes one condition. A group left empty by it goes too, and the trigger folds back to its flat single condition row the moment only one condition is left anywhere in its rule, not only once every group is gone.</summary>
        public static bool RemoveCondition(AvatarDoc menu, MenuEntry trigger, int groupIndex, int index)
        {
            if (Refuses(menu) || trigger == null || !trigger.UsesWhenGroups) return false;
            if (groupIndex < 0 || groupIndex >= trigger.when.Count) return false;
            var group = trigger.when[groupIndex];
            if (group == null || index < 0 || index >= group.all.Count) return false;
            var removed = group.all[index];
            Undo.RecordObject(menu, "Remove condition");
            group.all.RemoveAt(index);
            if (group.all.Count == 0) trigger.when.RemoveAt(groupIndex);
            CollapseToFlatIfOneConditionLeft(trigger, removed);
            MarkChanged(menu);
            return true;
        }

        /// <summary>Removes a whole group of a trigger's rule, folding back to the flat single condition row the same way RemoveCondition does when that leaves only one condition standing.</summary>
        public static bool RemoveConditionGroup(AvatarDoc menu, MenuEntry trigger, int groupIndex)
        {
            if (Refuses(menu) || trigger == null || !trigger.UsesWhenGroups) return false;
            if (groupIndex < 0 || groupIndex >= trigger.when.Count) return false;
            var last = trigger.when[groupIndex].all.FirstOrDefault(c => c != null);
            Undo.RecordObject(menu, "Remove condition group");
            trigger.when.RemoveAt(groupIndex);
            CollapseToFlatIfOneConditionLeft(trigger, last);
            MarkChanged(menu);
            return true;
        }

        /// <summary>
        /// Once a trigger's rule holds one condition or none across every remaining group, the groups themselves
        /// are cleared so the panel folds back to its one plain row instead of a single-condition card still
        /// captioned "All of": a group card only earns its keep once "+ And" or "+ Or" has actually built a rule
        /// with more than one condition in it. fallback supplies the condition to write onto the flat fields when
        /// none is left in when itself (the condition that was just removed), the same value a trigger with no
        /// groups at all would already read.
        /// </summary>
        static void CollapseToFlatIfOneConditionLeft(MenuEntry trigger, WhenCondition fallback)
        {
            var remaining = trigger.when.Where(g => g != null && g.all != null).SelectMany(g => g.all).Where(c => c != null).ToList();
            // A blend shape trigger's flat fields hold its shape, never a condition: with no requirement left the
            // groups simply go, and the shape stays exactly as it was.
            if (trigger.source == TriggerSource.BlendShape)
            {
                if (remaining.Count == 0) trigger.when.Clear();
                return;
            }
            if (remaining.Count > 1) return;
            var keep = remaining.Count == 1 ? remaining[0] : fallback;
            if (keep != null) keep.CopyToEntry(trigger);
            trigger.when.Clear();
        }

        /// <summary>Switches one condition to another kind. The fields of the kind it leaves keep their values, so switching back and forth costs nothing.</summary>
        public static bool SetConditionKind(AvatarDoc menu, MenuEntry trigger, int groupIndex, int index, WhenConditionKind kind)
        {
            if (Refuses(menu) || trigger == null || !trigger.UsesWhenGroups) return false;
            if (groupIndex < 0 || groupIndex >= trigger.when.Count) return false;
            var group = trigger.when[groupIndex];
            if (group == null || index < 0 || index >= group.all.Count) return false;
            var condition = group.all[index];
            if (condition == null || condition.kind == kind) return false;
            Undo.RecordObject(menu, "Change condition");
            condition.kind = kind;
            // The same gap a source switch leaves on the entry itself: data that came back from an undo or a
            // prefab override skips the field initialisers, so a contact with no radius could never be touched.
            if (kind == WhenConditionKind.Contact && condition.contactRadius == 0f)
            {
                condition.contactRadius = 0.05f;
                if (condition.contactTags == null || condition.contactTags.Count == 0)
                    condition.contactTags = new List<string> { "Finger", "Hand" };
                condition.contactAllowSelf = true;
                condition.contactAllowOthers = true;
            }
            if (kind == WhenConditionKind.PhysBone && condition.physBoneThreshold == 0f) condition.physBoneThreshold = 0.5f;
            MarkChanged(menu);
            return true;
        }

        public static AnimatorMerge AddAnimator(AvatarDoc menu, RuntimeAnimatorController controller)
        {
            if (Refuses(menu)) return null;
            Undo.RecordObject(menu, "Add animator");
            var merge = AnimatorMerge.Create(controller);
            menu.animators.Add(merge);
            MarkChanged(menu);
            return merge;
        }

        public static bool RemoveAnimator(AvatarDoc menu, AnimatorMerge merge)
        {
            if (Refuses(menu) || merge == null) return false;
            Undo.RecordObject(menu, "Remove animator");
            bool removed = menu.animators.Remove(merge);
            if (removed) MarkChanged(menu);
            return removed;
        }

        /// <summary>Checks or unchecks one layer of a merged animator. skipLayers holds the unchecked ones, so skipping is what is written down. False when nothing was written.</summary>
        public static bool SetLayerSkipped(AvatarDoc menu, AnimatorMerge merge, string layer, bool skipped)
        {
            if (Refuses(menu) || merge == null || string.IsNullOrEmpty(layer)) return false;
            if (merge.skipLayers.Contains(layer) == skipped) return false;
            Undo.RecordObject(menu, skipped ? "Skip layer" : "Merge layer");
            if (skipped) merge.skipLayers.Add(layer);
            else merge.skipLayers.Remove(layer);
            MarkChanged(menu);
            return true;
        }

        public static MenuAssetInstall AddMenuAsset(AvatarDoc menu, VRCExpressionsMenu asset)
        {
            if (Refuses(menu)) return null;
            Undo.RecordObject(menu, "Add menu asset");
            var install = MenuAssetInstall.Create(asset);
            menu.menuAssets.Add(install);
            MarkChanged(menu);
            return install;
        }

        public static bool RemoveMenuAsset(AvatarDoc menu, MenuAssetInstall install)
        {
            if (Refuses(menu) || install == null) return false;
            Undo.RecordObject(menu, "Remove menu asset");
            bool removed = menu.menuAssets.Remove(install);
            if (removed) MarkChanged(menu);
            return removed;
        }

        /// <summary>A name no declaration of this component has yet: "New parameter", then "New parameter 2" and on.</summary>
        public static string NewDeclarationName(AvatarDoc menu)
        {
            var start = L.Text("ad.ui.parameters.new");
            var taken = new HashSet<string>(menu.parameters.Where(p => p != null).Select(p => p.name));
            if (!taken.Contains(start)) return start;
            for (int i = 2; ; i++)
                if (!taken.Contains(start + " " + i)) return start + " " + i;
        }

        /// <summary>Declares a parameter this component carries itself. A name it already declares is returned as it is.</summary>
        public static ParameterDeclaration AddDeclaration(AvatarDoc menu, string name, PlannedType type)
        {
            if (Refuses(menu)) return null;
            var trimmed = (name ?? "").Trim();
            var found = menu.parameters.FirstOrDefault(p => p != null && p.name == trimmed);
            if (found != null) return found;
            Undo.RecordObject(menu, "Add parameter");
            var declaration = ParameterDeclaration.Create(trimmed, type);
            menu.parameters.Add(declaration);
            MarkChanged(menu);
            return declaration;
        }

        public static bool RemoveDeclaration(AvatarDoc menu, ParameterDeclaration declaration)
        {
            if (Refuses(menu) || declaration == null) return false;
            Undo.RecordObject(menu, "Remove parameter");
            bool removed = menu.parameters.Remove(declaration);
            if (removed) MarkChanged(menu);
            return removed;
        }

        /// <summary>
        /// Captures a mesh's blend shapes above 0 into the entry's rows: a row the entry already has for that shape
        /// keeps its place and takes the new weight, nothing else about it changes beyond what intoUpperEnd and
        /// mode say below. One shared path for every section: intoUpperEnd writes maxWeight and zeroes minWeight (a
        /// radial's two ends) instead of the plain weight field; mode, when given, is written onto every row this
        /// call touches (Pin for a toggle or a trigger condition, Fixed for a blend shape link); left null a row's
        /// mode is untouched (a radial's ends carry none, an override pins by kind regardless of it). withRule adds
        /// the entry's rule for the mesh at 0, or turns on the one it has, leaving that one's value as it is; only a
        /// toggle's Capture asks for it. Returns how many shapes were captured, 0 when refused or the mesh has none
        /// above 0, in which case nothing else here runs.
        /// </summary>
        public static int CaptureShapeSet(AvatarDoc menu, MenuEntry entry, SkinnedMeshRenderer mesh, bool intoUpperEnd = false, ShapeEffectMode? mode = ShapeEffectMode.Pin, bool withRule = true)
        {
            if (Refuses(menu) || entry == null || mesh == null) return 0;
            var captured = ShapeSetModel.Capture(mesh, entry.blendShapes);
            if (captured.Count == 0) return 0;
            // Capture only reads the scene; every write happens here, after the snapshot, so an undo of this call
            // restores the rows it changed instead of leaving them at their captured weight.
            Undo.RecordObject(menu, "Capture blend shapes");
            foreach (var row in captured)
            {
                var target = row.Existing;
                if (target == null)
                {
                    target = new BlendShapeTarget { renderer = mesh, shape = row.Shape };
                    entry.blendShapes.Add(target);
                }
                if (intoUpperEnd) { target.maxWeight = row.Weight; target.minWeight = 0f; }
                else target.weight = row.Weight;
                if (mode.HasValue) target.mode = mode.Value;
            }
            if (withRule)
            {
                var rule = entry.shapeSets.FirstOrDefault(r => r != null && r.mesh == mesh);
                if (rule == null) entry.shapeSets.Add(new ShapeSetRule { mesh = mesh, zeroOthers = true, holdBlink = false, holdVisemes = false });
                else rule.zeroOthers = true;
            }
            MarkChanged(menu);
            return captured.Count;
        }

        /// <summary>
        /// Adds a rule that holds every other shape of mesh at its default while the entry is on, with the shapes
        /// VRChat blinks and speaks with left free (Hold blink and Hold visemes off). A rule the entry already has for
        /// that mesh is kept as it is and nothing is added. One Undo step.
        /// </summary>
        public static bool AddHoldRule(AvatarDoc menu, MenuEntry entry, SkinnedMeshRenderer mesh)
        {
            if (Refuses(menu) || entry == null || mesh == null || !entry.OffersShapeSets) return false;
            if (entry.shapeSets.Any(r => r != null && r.mesh == mesh)) return false;
            Undo.RecordObject(menu, "Hold the rest");
            entry.shapeSets.Add(new ShapeSetRule { mesh = mesh, zeroOthers = true, value = ShapeSetValue.Default, holdBlink = false, holdVisemes = false });
            MarkChanged(menu);
            return true;
        }

        /// <summary>Chooses what a rule holds the mesh's other shapes at: their default or 0.</summary>
        public static bool SetShapeSetValue(AvatarDoc menu, MenuEntry entry, int ruleIndex, ShapeSetValue value)
        {
            if (Refuses(menu) || entry == null || ruleIndex < 0 || ruleIndex >= entry.shapeSets.Count) return false;
            var rule = entry.shapeSets[ruleIndex];
            if (rule == null || rule.value == value) return false;
            Undo.RecordObject(menu, "Change shape set");
            rule.value = value;
            MarkChanged(menu);
            return true;
        }

        /// <summary>Chooses whether a rule holds the shapes the avatar descriptor blinks with too (Hold blink).</summary>
        public static bool SetShapeSetHoldBlink(AvatarDoc menu, MenuEntry entry, int ruleIndex, bool value)
        {
            if (Refuses(menu) || entry == null || ruleIndex < 0 || ruleIndex >= entry.shapeSets.Count) return false;
            var rule = entry.shapeSets[ruleIndex];
            if (rule == null || rule.holdBlink == value) return false;
            Undo.RecordObject(menu, "Change shape set");
            rule.holdBlink = value;
            MarkChanged(menu);
            return true;
        }

        /// <summary>Chooses whether a rule holds the shapes the avatar descriptor speaks with too (Hold visemes).</summary>
        public static bool SetShapeSetHoldVisemes(AvatarDoc menu, MenuEntry entry, int ruleIndex, bool value)
        {
            if (Refuses(menu) || entry == null || ruleIndex < 0 || ruleIndex >= entry.shapeSets.Count) return false;
            var rule = entry.shapeSets[ruleIndex];
            if (rule == null || rule.holdVisemes == value) return false;
            Undo.RecordObject(menu, "Change shape set");
            rule.holdVisemes = value;
            MarkChanged(menu);
            return true;
        }

        /// <summary>Switches a rule on or off. The captured rows are left alone either way.</summary>
        public static bool SetShapeSetZeroOthers(AvatarDoc menu, MenuEntry entry, int ruleIndex, bool value)
        {
            if (Refuses(menu) || entry == null || ruleIndex < 0 || ruleIndex >= entry.shapeSets.Count) return false;
            var rule = entry.shapeSets[ruleIndex];
            if (rule == null || rule.zeroOthers == value) return false;
            Undo.RecordObject(menu, "Change shape set");
            rule.zeroOthers = value;
            MarkChanged(menu);
            return true;
        }

        /// <summary>Drops a rule. The rows it captured stay on the entry.</summary>
        public static bool RemoveShapeSet(AvatarDoc menu, MenuEntry entry, int ruleIndex)
        {
            if (Refuses(menu) || entry == null || ruleIndex < 0 || ruleIndex >= entry.shapeSets.Count) return false;
            Undo.RecordObject(menu, "Remove shape set");
            entry.shapeSets.RemoveAt(ruleIndex);
            MarkChanged(menu);
            return true;
        }

        /// <summary>
        /// Gives a fresh id to every entry, merged animator, installed menu asset and menu edit whose id an earlier
        /// owner already holds, earlier meaning higher up in the order the components sit on the avatar. The first
        /// owner keeps its id, since presets, gates and flip targets of other components may name it; the copies
        /// change. It repairs every component of the avatar whichever view asks, and leaves a locked component as
        /// it is, so the model's duplicate warning goes on standing for it. Each component other than menu that it
        /// repairs gets one console line. Returns how many ids changed.
        /// </summary>
        public static int EnsureUniqueIds(AvatarDoc menu, IEnumerable<AvatarDoc> others)
        {
            var entryIds = new HashSet<string>();
            var mergeIds = new HashSet<string>();
            var installIds = new HashSet<string>();
            var editIds = new HashSet<string>();
            int changed = 0;
            foreach (var component in InHierarchyOrder(menu, others))
            {
                int repaired = EnsureUniqueIdsOf(component, entryIds, mergeIds, installIds, editIds);
                changed += repaired;
                // The component being viewed is the one the creator is looking at; another one changing behind
                // their back makes the scene dirty, so the console says which and why.
                if (repaired > 0 && component != menu) Log(string.Format(L.Text("ad.log.ids_repaired"), component.gameObject.name));
            }
            return changed;
        }

        /// <summary>The given components in the order they sit on the avatar, the same order the model reads them in. One outside that hierarchy, which should not happen, goes last.</summary>
        static List<AvatarDoc> InHierarchyOrder(AvatarDoc menu, IEnumerable<AvatarDoc> others)
        {
            var wanted = new List<AvatarDoc>();
            if (menu != null) wanted.Add(menu);
            if (others != null)
                foreach (var other in others)
                    if (other != null && !wanted.Contains(other)) wanted.Add(other);
            if (menu == null) return wanted;
            var top = AvatarPlanCache.RootOf(menu) ?? menu.transform.root;
            var order = top.GetComponentsInChildren<AvatarDoc>(true).ToList();
            return wanted.OrderBy(c => { int at = order.IndexOf(c); return at < 0 ? int.MaxValue : at; }).ToList();
        }

        static int EnsureUniqueIdsOf(AvatarDoc component, HashSet<string> entryIds, HashSet<string> mergeIds, HashSet<string> installIds, HashSet<string> editIds)
        {
            // A locked component still claims its ids, so nothing after it is given one it holds.
            bool locked = Refuses(component);
            bool recorded = false;
            void Record()
            {
                if (recorded) return;
                Undo.RecordObject(component, "Fix entry ids");
                recorded = true;
            }
            bool Claim(HashSet<string> taken, string id) => !string.IsNullOrEmpty(id) && taken.Add(id);

            int changed = 0;
            var idMap = new Dictionary<string, string>();
            var reassigned = new List<MenuEntry>();
            foreach (var entry in component.entries)
            {
                if (Claim(entryIds, entry.id) || locked) continue;
                Record();
                var previous = entry.id;
                entry.id = Guid.NewGuid().ToString("N");
                entryIds.Add(entry.id);
                if (!string.IsNullOrEmpty(previous)) idMap[previous] = entry.id;
                reassigned.Add(entry);
                changed++;
            }

            // A component copied whole keeps every id of its source, so each reassigned entry still links to the
            // source's entries: its parent and its trigger source move to the copies that came with it. Entries that
            // kept their id keep their links, so the first occurrence of a repeated id keeps its own children.
            foreach (var entry in reassigned)
            {
                if (!string.IsNullOrEmpty(entry.parentId) && idMap.TryGetValue(entry.parentId, out var parent)) entry.parentId = parent;
                RemapLinks(entry, idMap);
            }

            foreach (var merge in component.animators)
            {
                if (merge == null || Claim(mergeIds, merge.id) || locked) continue;
                Record();
                merge.id = Guid.NewGuid().ToString("N");
                mergeIds.Add(merge.id);
                changed++;
            }
            foreach (var install in component.menuAssets)
            {
                if (install == null || Claim(installIds, install.id) || locked) continue;
                Record();
                install.id = Guid.NewGuid().ToString("N");
                installIds.Add(install.id);
                changed++;
            }

            var editMap = new Dictionary<string, string>();
            var keptHere = new HashSet<string>();
            foreach (var edit in component.menuEdits)
            {
                if (edit == null) continue;
                if (Claim(editIds, edit.id)) { keptHere.Add(edit.id); continue; }
                if (locked) continue;
                Record();
                var previous = edit.id;
                edit.id = Guid.NewGuid().ToString("N");
                editIds.Add(edit.id);
                // The layer an import left out names the edit that hides its old control; it follows the copy's own edit.
                if (!string.IsNullOrEmpty(previous) && !keptHere.Contains(previous)) editMap[previous] = edit.id;
                changed++;
            }
            if (editMap.Count > 0)
                foreach (var removed in component.removedLayers)
                    if (removed != null && editMap.TryGetValue(removed.editId ?? "", out var own)) removed.editId = own;

            if (recorded) MarkChanged(component);
            return changed;
        }
    }
}
#endif
