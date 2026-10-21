#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>What a copy does with the AvatarDoc component the object it goes on already has.</summary>
    public enum CopyWhere { Replace = 0, AddAnother = 1 }

    /// <summary>What an empty field would mean for an object the copy did not find: nothing worse than a row skipped with a message, a path root that silently becomes the avatar's root, or an error that stops the build.</summary>
    public enum CopyNeed { None = 0, PathRoot = 1, Required = 2 }

    /// <summary>The object of the other avatar the creator chose for one object of the source avatar. Null picked means the field is left empty.</summary>
    [Serializable]
    public sealed class CopyPick
    {
        /// <summary>The source avatar's GameObject the pick stands for, with every component of it the copy uses.</summary>
        public Object source;
        public Object picked;
    }

    /// <summary>The Copy to another avatar window's working state. Serializable, so an open window keeps it across a script reload.</summary>
    [Serializable]
    public sealed class CopyDraft
    {
        public AvatarDoc source;
        /// <summary>The root of the avatar the copy goes to.</summary>
        public Transform target;
        public CopyWhere where;
        /// <summary>Whether the creator chose Replace or Add another; until then it follows how many components are there.</summary>
        public bool whereChosen;
        /// <summary>The component Replace overwrites, when the object holds several.</summary>
        public AvatarDoc replace;
        public List<CopyPick> picks = new List<CopyPick>();
        /// <summary>The component the last Copy wrote, which a second press overwrites, and the choice from before that Copy: an undo that takes an added copy away puts the choice back.</summary>
        public AvatarDoc made;
        public bool hasMade;
        public CopyWhere whereBefore;
        public bool whereChosenBefore;
    }

    /// <summary>One object of the source avatar the component refers to, with every part of it the component uses (the object itself, its components), and what the copy refers to in its place.</summary>
    public sealed class CopyReference
    {
        public GameObject Source;
        /// <summary>The object itself and the components of it the fields hold, in the order the component first uses them.</summary>
        public readonly List<Object> Parts = new List<Object>();
        /// <summary>Where it sits under the source avatar's root, "Body/Hat"; empty for the root itself.</summary>
        public string Path = "";
        /// <summary>The object at the same place on the other avatar, or null.</summary>
        public GameObject Found;
        /// <summary>Marked Pick by hand when copying: never matched by its place.</summary>
        public bool ByHand;
        /// <summary>Listed in the window with a field to pick it: marked by hand, not at its place on the other avatar, or there without a component the copy needs.</summary>
        public bool Listed;
        /// <summary>What the field starts with: the object at the same place, else the one object of that name on the other avatar.</summary>
        public Object Suggestion;
        public bool SuggestedByPlace;
        public bool HasPick;
        public Object Picked;
        /// <summary>Set when same-named objects side by side made the place ambiguous: the place of that step and how many of that name each avatar has there.</summary>
        public string AmbiguousAt;
        public int SourceCount;
        public int TargetCount;
        public CopyNeed Need;
        /// <summary>The entries that use it, by name, in list order.</summary>
        public readonly List<string> Uses = new List<string>();

        /// <summary>The kind of object the window's field takes: the one component when that is all the copy uses of the object, else the object.</summary>
        public Type FieldType => Parts.Count == 1 ? Parts[0].GetType() : typeof(GameObject);

        /// <summary>What the field holds: the creator's pick, else the suggestion.</summary>
        public Object Value => HasPick ? Picked : Suggestion;

        /// <summary>A listed object the copy cannot go without, with nothing in its field.</summary>
        public bool Blocking => Listed && Need != CopyNeed.None && Value == null;

        /// <summary>The parts the object at the same place does not have: all of them when there is none.</summary>
        public IEnumerable<Object> MissingParts => Found == null ? Parts : Parts.Where(p => MenuCopy.Same(p, Found.transform) == null);

        /// <summary>What the copy writes for one part: the part on the object found at its place, or, for a listed object, the part on the object in its field.</summary>
        public Object ResultFor(Object part)
        {
            if (!Listed) return Found != null ? MenuCopy.Same(part, Found.transform) : null;
            var value = Value;
            if (value is GameObject go) return MenuCopy.Same(part, go.transform);
            if (value is Component component)
                return Parts.Count == 1 && component.GetType() == part.GetType() ? component : MenuCopy.Same(part, component.transform);
            return null;
        }
    }

    public enum CopyCheckKind { Shape = 0, Slot = 1, Property = 2 }

    /// <summary>A blend shape, a material slot or a material property the copy names that the other avatar's renderer does not have.</summary>
    public sealed class CopyCheck
    {
        public CopyCheckKind Kind;
        public Renderer Renderer;
        /// <summary>The shape's or the property's name, or the slot's number.</summary>
        public string Value = "";
        public readonly List<string> Uses = new List<string>();
    }

    /// <summary>What a copy would do, read from the scene: where it goes, what each object becomes, and what the other avatar lacks.</summary>
    public sealed class CopyPlan
    {
        public AvatarDoc Source;
        public Transform SourceRoot;
        public Transform TargetRoot;
        /// <summary>The object of the other avatar the copy goes on.</summary>
        public GameObject Destination;
        /// <summary>Where the component sits under the source avatar's root; empty for the root.</summary>
        public string DestinationPath = "";
        /// <summary>True when the other avatar has no object at that place, so the copy goes on its root.</summary>
        public bool DestinationIsFallback;
        public readonly List<AvatarDoc> Existing = new List<AvatarDoc>();
        public readonly List<CopyReference> References = new List<CopyReference>();
        public readonly List<CopyCheck> Checks = new List<CopyCheck>();

        /// <summary>The component Replace would overwrite, or null when it adds one.</summary>
        public AvatarDoc ReplaceTarget;
        public CopyWhere Where;
        /// <summary>Replace would overwrite the copy the last press of this window made, which a locked source made locked.</summary>
        public bool ReplacesOwnCopy;
        /// <summary>
        /// Whether the copy gets new ids. Add another onto an object that has an AvatarDoc always does, so two
        /// components on one avatar never share one. A copy onto an object with no AvatarDoc, and Replace, keep the
        /// source's ids, so links of other components of that avatar to the entries still work, unless another
        /// component of that avatar already holds one of them.
        /// </summary>
        public bool RenewsIds;

        /// <summary>The objects the window lists with a field to pick them.</summary>
        public IEnumerable<CopyReference> Listed => References.Where(r => r.Listed);
        public IEnumerable<CopyReference> Ambiguous => References.Where(r => r.AmbiguousAt != null);
        public int FoundCount => References.Count(r => !r.Listed);

        /// <summary>Whether an object the copy cannot go without has nothing picked yet.</summary>
        public bool Blocked => References.Any(r => r.Blocking);
    }

    /// <summary>
    /// Copies one AvatarDoc component from one version of an avatar to another. Every reference to an object of the
    /// source avatar, found by walking the component's serialized data, is found again on the other avatar by its place
    /// under the root: the same names from the root down, among same-named objects side by side the same position, and
    /// on that object the component of the same type at the same position. An object marked Pick by hand when copying,
    /// and one not found there, is listed for the creator to pick instead. Assets and objects outside the source avatar
    /// stay as they are, and the copy never refers to an object of the source avatar.
    /// </summary>
    public static class MenuCopy
    {
        /// <summary>The menu item on the AvatarDoc component's own menu.</summary>
        public const string MenuItemPath = "CONTEXT/AvatarDoc/Copy to another avatar...";

        /// <summary>Opens the window for a component. Replaceable by a test, which reads the draft instead of opening a window.</summary>
        public static Action<CopyDraft> Open = draft => CopyMenuWindow.Show(draft);

        /// <summary>The roots of the avatars in the open scenes. Replaceable by a test, which never reads the real scene.</summary>
        public static Func<IEnumerable<Transform>> SceneAvatars = () =>
        {
            var roots = new List<Transform>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var descriptor in go.GetComponentsInChildren<VRCAvatarDescriptor>(true))
                        if (descriptor != null && !roots.Contains(descriptor.transform)) roots.Add(descriptor.transform);
            }
            return roots;
        };

        static readonly Regex EntryPath = new Regex(@"^entries\.Array\.data\[(\d+)\]");
        static readonly Regex MergePath = new Regex(@"^animators\.Array\.data\[(\d+)\]");
        static readonly Regex AssetPath = new Regex(@"^menuAssets\.Array\.data\[(\d+)\]");
        static readonly Regex EntryField = new Regex(@"^entries\.Array\.data\[(\d+)\]\.(\w+)$");
        static readonly Regex ConditionField = new Regex(@"^entries\.Array\.data\[(\d+)\]\.when\.Array\.data\[(\d+)\]\.all\.Array\.data\[(\d+)\]\.(\w+)$");
        static readonly Regex RuleMesh = new Regex(@"^entries\.Array\.data\[\d+\]\.shapeSets\.Array\.data\[\d+\]\.mesh$");

        /// <summary>The avatar a component of a scene belongs to, or null: one outside every avatar, or one of a prefab asset.</summary>
        public static Transform AvatarOf(AvatarDoc component)
        {
            if (component == null || EditorUtility.IsPersistent(component)) return null;
            return AvatarPlanCache.RootOf(component);
        }

        public static bool CanCopy(AvatarDoc component) => AvatarOf(component) != null;

        /// <summary>Whether the component menu's item can be chosen: a component on an avatar in the scene.</summary>
        public static bool CanCopyFrom(MenuCommand command) => command != null && CanCopy(command.context as AvatarDoc);

        /// <summary>Opens the window for the component the menu was opened on.</summary>
        public static void CopyFrom(MenuCommand command)
        {
            if (!CanCopyFrom(command)) return;
            Open(For((AvatarDoc)command.context));
        }

        [UnityEditor.MenuItem(MenuItemPath)]
        static void CopyFromMenu(MenuCommand command) => CopyFrom(command);

        [UnityEditor.MenuItem(MenuItemPath, true)]
        static bool CanCopyFromMenu(MenuCommand command) => CanCopyFrom(command);

        /// <summary>A draft for a component, going to the first other avatar of the open scenes.</summary>
        public static CopyDraft For(AvatarDoc source)
        {
            var draft = new CopyDraft { source = source };
            draft.target = Targets(source).FirstOrDefault();
            return draft;
        }

        /// <summary>Every avatar of the open scenes the component can be copied to: all of them but its own, and none inside it or around it.</summary>
        public static List<Transform> Targets(AvatarDoc source)
        {
            var own = AvatarOf(source);
            var result = new List<Transform>();
            foreach (var root in SceneAvatars() ?? Enumerable.Empty<Transform>())
            {
                if (root == null || EditorUtility.IsPersistent(root) || result.Contains(root)) continue;
                if (own != null && (root == own || root.IsChildOf(own) || own.IsChildOf(root))) continue;
                result.Add(root);
            }
            return result;
        }

        /// <summary>Sets the avatar the copy goes to; what was chosen for the other one is let go of.</summary>
        public static void SetTarget(CopyDraft draft, Transform target)
        {
            if (draft == null || draft.target == target) return;
            draft.target = target;
            draft.whereChosen = false;
            draft.replace = null;
            draft.picks.Clear();
            draft.made = null;
            draft.hasMade = false;
        }

        public static void SetWhere(CopyDraft draft, CopyWhere where)
        {
            if (draft == null) return;
            draft.where = where;
            draft.whereChosen = true;
        }

        /// <summary>
        /// Chooses the object of the other avatar the copy uses for an object of the source avatar, the one source
        /// stands for or is part of. Null leaves the field empty. Refused, changing nothing, for an object that is not
        /// part of the other avatar.
        /// </summary>
        public static bool Pick(CopyDraft draft, Object source, Object picked)
        {
            var go = GameObjectOf(source);
            if (draft == null || go == null) return false;
            if (picked != null && !UnderAvatar(picked, draft.target)) return false;
            var pick = draft.picks.FirstOrDefault(p => p != null && p.source == go);
            if (pick == null) draft.picks.Add(pick = new CopyPick { source = go });
            pick.picked = picked;
            return true;
        }

        /// <summary>Brings the draft in line with the scene: a copy the last press added and an undo took away puts the choice from before it back.</summary>
        public static void Settle(CopyDraft draft)
        {
            if (draft == null || !draft.hasMade || draft.made != null) return;
            draft.where = draft.whereBefore;
            draft.whereChosen = draft.whereChosenBefore;
            draft.replace = null;
            draft.made = null;
            draft.hasMade = false;
        }

        // ---- the plan --------------------------------------------------------------------------------------------

        /// <summary>Reads what the copy would do. Null when the component, its avatar or the other avatar is gone.</summary>
        public static CopyPlan Plan(CopyDraft draft)
        {
            Settle(draft);
            var sourceRoot = draft != null ? AvatarOf(draft.source) : null;
            var targetRoot = draft != null ? draft.target : null;
            if (sourceRoot == null || targetRoot == null || targetRoot == sourceRoot) return null;
            var plan = new CopyPlan { Source = draft.source, SourceRoot = sourceRoot, TargetRoot = targetRoot };

            plan.DestinationPath = PathOf(sourceRoot, draft.source.transform);
            var destination = FindAt(sourceRoot, targetRoot, draft.source.transform, out _, out _, out _);
            plan.DestinationIsFallback = destination == null;
            plan.Destination = (destination != null ? destination : targetRoot).gameObject;
            plan.Existing.AddRange(plan.Destination.GetComponents<AvatarDoc>().Where(c => c != null));

            if (plan.Existing.Count == 0) plan.Where = CopyWhere.AddAnother;
            else if (draft.whereChosen) plan.Where = draft.where;
            else plan.Where = plan.Existing.Count == 1 ? CopyWhere.Replace : CopyWhere.AddAnother;
            if (plan.Where == CopyWhere.Replace)
            {
                plan.ReplaceTarget = draft.replace != null && plan.Existing.Contains(draft.replace) ? draft.replace : plan.Existing[0];
                plan.ReplacesOwnCopy = draft.hasMade && draft.made == plan.ReplaceTarget;
            }
            plan.RenewsIds = plan.Where == CopyWhere.AddAnother
                ? plan.Existing.Count > 0 || IdsClash(draft.source, targetRoot, null)
                : IdsClash(draft.source, targetRoot, plan.ReplaceTarget);

            var marked = new HashSet<GameObject>(draft.source.pickByHand.Where(g => g != null));
            var byObject = new Dictionary<GameObject, CopyReference>();
            using (var serialized = new SerializedObject(draft.source))
            {
                foreach (var (value, path) in SceneReferences(serialized, sourceRoot))
                {
                    var go = GameObjectOf(value);
                    if (!byObject.TryGetValue(go, out var reference))
                    {
                        reference = new CopyReference { Source = go, Path = PathOf(sourceRoot, go.transform), ByHand = marked.Contains(go) };
                        byObject[go] = reference;
                        plan.References.Add(reference);
                    }
                    if (!reference.Parts.Contains(value)) reference.Parts.Add(value);
                    var use = UseOf(draft.source, path);
                    if (!reference.Uses.Contains(use)) reference.Uses.Add(use);
                    var need = NeedOf(draft.source, path);
                    if (need > reference.Need) reference.Need = need;
                }
            }
            foreach (var reference in plan.References) Resolve(reference, sourceRoot, targetRoot, draft);
            AddChecks(plan, byObject);
            return plan;
        }

        /// <summary>Whether a component of the other avatar other than except already holds one of the ids of the source's entries, menu edits, merged animators or menu assets.</summary>
        static bool IdsClash(AvatarDoc source, Transform targetRoot, AvatarDoc except)
        {
            var own = new HashSet<string>(OwnIds(source).Where(id => !string.IsNullOrEmpty(id)));
            foreach (var other in targetRoot.GetComponentsInChildren<AvatarDoc>(true))
                if (other != null && other != source && other != except && OwnIds(other).Any(own.Contains)) return true;
            return false;
        }

        static IEnumerable<string> OwnIds(AvatarDoc component)
            => component.entries.Where(e => e != null).Select(e => e.id)
                .Concat(component.menuEdits.Where(e => e != null).Select(e => e.id))
                .Concat(component.animators.Where(m => m != null).Select(m => m.id))
                .Concat(component.menuAssets.Where(m => m != null).Select(m => m.id));

        /// <summary>Every object reference of the component's own data to an object under the source avatar, with its property path. Unity's own fields and the Pick by hand list are left out.</summary>
        static IEnumerable<(Object value, string path)> SceneReferences(SerializedObject serialized, Transform sourceRoot)
        {
            foreach (var property in ObjectFields(serialized))
            {
                var value = property.objectReferenceValue;
                if (UnderAvatar(value, sourceRoot)) yield return (value, property.propertyPath);
            }
        }

        /// <summary>Whether the walk goes into a property: a class, a list or a managed reference.</summary>
        static bool Enters(SerializedProperty property)
            => property.propertyType == SerializedPropertyType.Generic || property.propertyType == SerializedPropertyType.ManagedReference;

        /// <summary>Walks an object's own data, every class, list and managed reference of it, and hands out each object reference field. A field added to the data later is walked the same way. Unity's own m_ fields and the Pick by hand list are left out.</summary>
        public static IEnumerable<SerializedProperty> ObjectFields(SerializedObject serialized)
        {
            var property = serialized.GetIterator();
            bool enter = true;
            while (property.Next(enter))
            {
                enter = Enters(property);
                if (property.depth == 0 && (property.name.StartsWith("m_", StringComparison.Ordinal) || property.name == nameof(AvatarDoc.pickByHand)))
                {
                    enter = false;
                    continue;
                }
                if (property.propertyType == SerializedPropertyType.ObjectReference) yield return property;
            }
        }

        static bool UnderAvatar(Object value, Transform root)
        {
            if (value == null || root == null || EditorUtility.IsPersistent(value)) return false;
            var transform = TransformOf(value);
            return transform != null && transform.IsChildOf(root);
        }

        static Transform TransformOf(Object value)
        {
            if (value is GameObject go) return go.transform;
            if (value is Component component) return component.transform;
            return null;
        }

        static GameObject GameObjectOf(Object value) => value is GameObject go ? go : value is Component component ? component.gameObject : null;

        /// <summary>Who uses the reference at a property path: the entry's name, a merged animator's or a menu asset's, or the object the component is on.</summary>
        static string UseOf(AvatarDoc component, string path)
        {
            var entry = EntryPath.Match(path);
            if (entry.Success && int.TryParse(entry.Groups[1].Value, out int e) && e < component.entries.Count && component.entries[e] != null)
                return component.entries[e].PlainName;
            var merge = MergePath.Match(path);
            if (merge.Success && int.TryParse(merge.Groups[1].Value, out int m) && m < component.animators.Count && component.animators[m]?.controller != null)
                return component.animators[m].controller.name;
            var asset = AssetPath.Match(path);
            if (asset.Success && int.TryParse(asset.Groups[1].Value, out int a) && a < component.menuAssets.Count && component.menuAssets[a] != null)
                return component.menuAssets[a].PlainName;
            return component.gameObject.name;
        }

        /// <summary>
        /// What an empty field at a property path would do. A path root, of a custom animation or of a merged animator,
        /// would quietly mean the avatar's root. The model reports an error, which stops the build, for an empty Hold
        /// the rest mesh, a grabbable's object, and the anchor or the PhysBone of a contact or PhysBone condition the
        /// trigger reads. Every other field left empty is a row the build skips with a message.
        /// </summary>
        static CopyNeed NeedOf(AvatarDoc component, string path)
        {
            if (path.EndsWith(".pathRoot", StringComparison.Ordinal)) return CopyNeed.PathRoot;
            if (RuleMesh.IsMatch(path)) return CopyNeed.Required;
            var condition = ConditionField.Match(path);
            if (condition.Success)
            {
                var entry = EntryAt(component, condition.Groups[1].Value);
                int g = int.Parse(condition.Groups[2].Value), i = int.Parse(condition.Groups[3].Value);
                if (entry == null || entry.kind != EntryKind.Trigger || g >= entry.when.Count || entry.when[g] == null || i >= entry.when[g].all.Count) return CopyNeed.None;
                var read = entry.when[g].all[i];
                var field = condition.Groups[4].Value;
                if (read == null) return CopyNeed.None;
                if (field == nameof(WhenCondition.contactAnchor) && read.kind == WhenConditionKind.Contact) return CopyNeed.Required;
                if (field == nameof(WhenCondition.physBone) && read.kind == WhenConditionKind.PhysBone) return CopyNeed.Required;
                return CopyNeed.None;
            }
            var flat = EntryField.Match(path);
            if (flat.Success)
            {
                var entry = EntryAt(component, flat.Groups[1].Value);
                if (entry == null) return CopyNeed.None;
                var field = flat.Groups[2].Value;
                if (field == nameof(MenuEntry.grabObject) && entry.kind == EntryKind.Grabbable) return CopyNeed.Required;
                bool flatTrigger = entry.kind == EntryKind.Trigger && !entry.UsesWhenGroups;
                if (field == nameof(MenuEntry.contactAnchor) && flatTrigger && entry.source == TriggerSource.Contact) return CopyNeed.Required;
                if (field == nameof(MenuEntry.physBone) && flatTrigger && entry.source == TriggerSource.PhysBone) return CopyNeed.Required;
            }
            return CopyNeed.None;
        }

        static MenuEntry EntryAt(AvatarDoc component, string index)
            => int.TryParse(index, out int e) && e >= 0 && e < component.entries.Count ? component.entries[e] : null;

        /// <summary>
        /// Finds the object on the other avatar: at its place, unless it is marked Pick by hand. One marked, not there, or
        /// there without a component the copy needs, is listed, its field starting at the object at the same place, else
        /// the one object of that name, as a suggestion. The creator's pick counts only while it is part of the other
        /// avatar.
        /// </summary>
        static void Resolve(CopyReference reference, Transform sourceRoot, Transform targetRoot, CopyDraft draft)
        {
            var found = FindAt(sourceRoot, targetRoot, reference.Source.transform, out reference.AmbiguousAt, out reference.SourceCount, out reference.TargetCount);
            reference.Found = found != null ? found.gameObject : null;
            reference.Listed = reference.ByHand || reference.Found == null || reference.MissingParts.Any();
            if (!reference.Listed) return;

            var candidates = new List<(GameObject go, bool byPlace)>();
            if (reference.Found != null) candidates.Add((reference.Found, true));
            var named = targetRoot.GetComponentsInChildren<Transform>(true).Where(t => t.name == reference.Source.name).ToList();
            if (named.Count == 1 && named[0].gameObject != reference.Found) candidates.Add((named[0].gameObject, false));
            foreach (var (candidate, byPlace) in candidates)
            {
                var suggestion = reference.Parts.Count == 1 && reference.Parts[0] is Component part ? Same(part, candidate.transform) : candidate;
                if (suggestion == null) continue;
                reference.Suggestion = suggestion;
                reference.SuggestedByPlace = byPlace;
                break;
            }

            var pick = draft.picks.FirstOrDefault(p => p != null && p.source == reference.Source);
            if (pick != null && (pick.picked == null || UnderAvatar(pick.picked, targetRoot)))
            {
                reference.HasPick = true;
                reference.Picked = pick.picked;
            }
        }

        /// <summary>The object on target that stands where value stands on its own: the GameObject, or on it the component of value's own type at the same position among that type's components.</summary>
        internal static Object Same(Object value, Transform target)
        {
            if (target == null) return null;
            if (value is GameObject) return target.gameObject;
            if (!(value is Component component)) return null;
            var type = component.GetType();
            var own = component.gameObject.GetComponents<Component>().Where(c => c != null && c.GetType() == type).ToList();
            var there = target.gameObject.GetComponents<Component>().Where(c => c != null && c.GetType() == type).ToList();
            int index = own.IndexOf(component);
            return index >= 0 && index < there.Count ? there[index] : null;
        }

        /// <summary>The names from the root down to the object, joined by "/"; empty for the root itself.</summary>
        public static string PathOf(Transform root, Transform transform)
        {
            if (transform == null || root == null || transform == root) return "";
            var parts = new List<string>();
            for (var t = transform; t != null && t != root; t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts);
        }

        /// <summary>
        /// The transform under targetRoot at the place transform has under sourceRoot: at every step down, the child of
        /// that name, and among several of that name side by side the one at the same position. Null when a step is not
        /// there. ambiguousAt names the first step where the two avatars have a different number of that name side by
        /// side, with both counts.
        /// </summary>
        static Transform FindAt(Transform sourceRoot, Transform targetRoot, Transform transform, out string ambiguousAt, out int sourceCount, out int targetCount)
        {
            ambiguousAt = null;
            sourceCount = 0;
            targetCount = 0;
            if (transform == null || !transform.IsChildOf(sourceRoot)) return null;
            var chain = new List<Transform>();
            for (var t = transform; t != sourceRoot; t = t.parent) chain.Insert(0, t);
            var at = targetRoot;
            var place = new List<string>();
            foreach (var step in chain)
            {
                place.Add(step.name);
                var own = Named(step.parent, step.name);
                var there = Named(at, step.name);
                int index = own.IndexOf(step);
                if (ambiguousAt == null && own.Count != there.Count && (own.Count > 1 || there.Count > 1))
                {
                    ambiguousAt = string.Join("/", place);
                    sourceCount = own.Count;
                    targetCount = there.Count;
                }
                if (index < 0 || index >= there.Count) return null;
                at = there[index];
            }
            return at;
        }

        static List<Transform> Named(Transform parent, string name)
        {
            var list = new List<Transform>();
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name) list.Add(child);
            }
            return list;
        }

        /// <summary>What the copy refers to in place of value: value itself for an asset or an object outside the source avatar; otherwise what its reference resolves the part to, which is never an object of the source avatar.</summary>
        static Object Map(Object value, Transform sourceRoot, Dictionary<GameObject, CopyReference> byObject)
        {
            if (!UnderAvatar(value, sourceRoot)) return value;
            return byObject.TryGetValue(GameObjectOf(value), out var reference) ? reference.ResultFor(value) : null;
        }

        // ---- the checks ------------------------------------------------------------------------------------------

        static void AddChecks(CopyPlan plan, Dictionary<GameObject, CopyReference> byObject)
        {
            Renderer Mapped(Renderer renderer)
            {
                if (renderer == null || !UnderAvatar(renderer, plan.SourceRoot)) return null;
                return byObject.TryGetValue(renderer.gameObject, out var reference) ? reference.ResultFor(renderer) as Renderer : null;
            }

            void Add(CopyCheckKind kind, Renderer renderer, string value, MenuEntry entry)
            {
                var check = plan.Checks.FirstOrDefault(c => c.Kind == kind && c.Renderer == renderer && c.Value == value);
                if (check == null) plan.Checks.Add(check = new CopyCheck { Kind = kind, Renderer = renderer, Value = value });
                if (!check.Uses.Contains(entry.PlainName)) check.Uses.Add(entry.PlainName);
            }

            foreach (var entry in plan.Source.entries)
            {
                if (entry == null) continue;
                if (entry.kind == EntryKind.Trigger && entry.source == TriggerSource.BlendShape && !string.IsNullOrEmpty(entry.sourceShape)
                    && Mapped(entry.sourceRenderer) is SkinnedMeshRenderer driver && !MenuModel.ShapeExists(driver, entry.sourceShape))
                    Add(CopyCheckKind.Shape, driver, entry.sourceShape, entry);
                foreach (var row in entry.blendShapes)
                    if (row != null && !string.IsNullOrEmpty(row.shape) && Mapped(row.renderer) is SkinnedMeshRenderer mesh && !MenuModel.ShapeExists(mesh, row.shape))
                        Add(CopyCheckKind.Shape, mesh, row.shape, entry);
                foreach (var row in entry.materials)
                {
                    var renderer = row != null ? Mapped(row.renderer) : null;
                    if (renderer != null && (row.slot < 0 || row.slot >= renderer.sharedMaterials.Length))
                        Add(CopyCheckKind.Slot, renderer, row.slot.ToString(System.Globalization.CultureInfo.InvariantCulture), entry);
                }
                foreach (var row in entry.properties)
                {
                    var renderer = row != null && !string.IsNullOrEmpty(row.property) ? Mapped(row.renderer) : null;
                    if (renderer == null || MenuModel.MaterialWithProperty(renderer, row.property) != null) continue;
                    bool swappedIn = entry.materials.Any(m => m != null && m.material != null && Mapped(m.renderer) == renderer
                        && PoiyomiLock.NameOn(m.material, row.property) != null);
                    if (!swappedIn) Add(CopyCheckKind.Property, renderer, row.property, entry);
                }
            }
        }

        // ---- the copy --------------------------------------------------------------------------------------------

        /// <summary>
        /// Copies the component in one Undo step: Replace overwrites what the component there holds and keeps the
        /// component itself; Add another adds a component. CopyPlan.RenewsIds says when the copy gets new ids, every
        /// reference to them inside the copy rewritten. The copy's Pick by hand list holds the objects picked for the
        /// marked ones. Refused while an object the copy cannot go without has nothing picked, and for Replace on a
        /// locked component other than the copy this window just made. The source and the selection are left as they
        /// are. Returns the component that holds the copy, or null with status saying why.
        /// </summary>
        public static AvatarDoc Apply(CopyDraft draft, out string status)
        {
            var plan = Plan(draft);
            if (plan == null || plan.Source == null)
            {
                status = L.Text("ad.ui.copy_to.gone");
                return null;
            }
            if (plan.Blocked)
            {
                status = L.Text("ad.ui.copy_to.run.blocked");
                return null;
            }
            if (plan.Where == CopyWhere.Replace && EntryEditing.Refuses(plan.ReplaceTarget) && !plan.ReplacesOwnCopy)
            {
                status = L.Text("ad.ui.copy_to.locked");
                return null;
            }

            var byObject = plan.References.ToDictionary(r => r.Source, r => r);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Copy to another avatar");
            AvatarDoc copy;
            try
            {
                copy = plan.Where == CopyWhere.Replace ? plan.ReplaceTarget : Undo.AddComponent<AvatarDoc>(plan.Destination);
                using (var from = new SerializedObject(plan.Source))
                using (var to = new SerializedObject(copy))
                {
                    var field = from.GetIterator();
                    bool first = true;
                    while (field.Next(first))
                    {
                        first = false;
                        if (field.name.StartsWith("m_", StringComparison.Ordinal) && field.name != "m_Enabled") continue;
                        to.CopyFromSerializedProperty(field);
                    }
                    foreach (var reference in ObjectFields(to))
                    {
                        var value = reference.objectReferenceValue;
                        var mapped = Map(value, plan.SourceRoot, byObject);
                        if (mapped != value) reference.objectReferenceValue = mapped;
                    }
                    var picked = plan.References.Where(r => r.ByHand).Select(r => GameObjectOf(r.Value)).Where(g => g != null).Distinct().ToList();
                    var marks = to.FindProperty(nameof(AvatarDoc.pickByHand));
                    marks.arraySize = picked.Count;
                    for (int i = 0; i < picked.Count; i++) marks.GetArrayElementAtIndex(i).objectReferenceValue = picked[i];
                    if (plan.RenewsIds) RenewIds(to);
                    to.ApplyModifiedProperties();
                }
                EntryEditing.MarkChanged(copy);
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
            }

            AvatarPlanCache.InvalidateMenu(plan.TargetRoot);
            status = string.Format(L.Text(plan.Where == CopyWhere.Replace ? "ad.ui.copy_to.done.replaced" : "ad.ui.copy_to.done.added"), L.Quoted(PlaceOf(plan)));
            // A second press then overwrites this copy instead of adding one more; an undo that takes an added copy away
            // puts the choice from before it back (Settle).
            if (!draft.hasMade)
            {
                draft.whereBefore = draft.where;
                draft.whereChosenBefore = draft.whereChosen;
            }
            draft.made = copy;
            draft.hasMade = true;
            draft.where = CopyWhere.Replace;
            draft.whereChosen = true;
            draft.replace = copy;
            return copy;
        }

        /// <summary>Where the copy goes, written from the other avatar's name down: "Avatar/MenuAddon", or the avatar's name for its root.</summary>
        public static string PlaceOf(CopyPlan plan)
        {
            if (plan == null || plan.TargetRoot == null) return "";
            return plan.DestinationIsFallback || string.IsNullOrEmpty(plan.DestinationPath) ? plan.TargetRoot.name : plan.TargetRoot.name + "/" + plan.DestinationPath;
        }

        static readonly string[] IdLists = { nameof(AvatarDoc.entries), nameof(AvatarDoc.menuEdits), nameof(AvatarDoc.animators), nameof(AvatarDoc.menuAssets) };

        /// <summary>
        /// Gives every entry, menu edit, merged animator and menu asset of the copy a new id, then rewrites every text
        /// of the copy that held one of the old ids: a parent, a trigger's toggle, a gate, a flip target, a condition, a
        /// preset's state, a removed layer's edit, and any id field found the same way later, managed references included.
        /// </summary>
        static void RenewIds(SerializedObject copy)
        {
            var renewed = new Dictionary<string, string>();
            foreach (var listName in IdLists)
            {
                var list = copy.FindProperty(listName);
                if (list == null || !list.isArray) continue;
                for (int i = 0; i < list.arraySize; i++)
                {
                    var id = list.GetArrayElementAtIndex(i).FindPropertyRelative("id");
                    if (id == null || id.propertyType != SerializedPropertyType.String) continue;
                    var fresh = Guid.NewGuid().ToString("N");
                    if (!string.IsNullOrEmpty(id.stringValue) && !renewed.ContainsKey(id.stringValue)) renewed[id.stringValue] = fresh;
                    id.stringValue = fresh;
                }
            }
            if (renewed.Count == 0) return;
            var property = copy.GetIterator();
            bool enter = true;
            while (property.Next(enter))
            {
                enter = Enters(property);
                if (property.depth == 0 && property.name.StartsWith("m_", StringComparison.Ordinal)) { enter = false; continue; }
                if (property.propertyType == SerializedPropertyType.String && property.name != "id" && renewed.TryGetValue(property.stringValue, out var fresh))
                    property.stringValue = fresh;
            }
        }

        // ---- pick by hand when copying ---------------------------------------------------------------------------

        /// <summary>Marks an object Pick by hand when copying, or takes the mark off, in one Undo step. Refused on a locked component. False when nothing was written.</summary>
        public static bool SetByHand(AvatarDoc menu, GameObject target, bool mark)
        {
            if (EntryEditing.Refuses(menu) || target == null) return false;
            if (menu.pickByHand.Contains(target) == mark) return false;
            Undo.RecordObject(menu, mark ? "Pick by hand when copying" : "Stop picking by hand when copying");
            if (mark) menu.pickByHand.Add(target);
            else menu.pickByHand.RemoveAll(g => g == target);
            EntryEditing.MarkChanged(menu);
            return true;
        }

        /// <summary>Takes the list's row at index off, whatever it holds, in one Undo step: a row whose object was deleted holds nothing. Refused on a locked component.</summary>
        public static bool RemoveByHandAt(AvatarDoc menu, int index)
        {
            if (EntryEditing.Refuses(menu) || index < 0 || index >= menu.pickByHand.Count) return false;
            Undo.RecordObject(menu, "Stop picking by hand when copying");
            menu.pickByHand.RemoveAt(index);
            EntryEditing.MarkChanged(menu);
            return true;
        }
    }
}
#endif
