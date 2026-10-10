#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>The merged tree and plan of one avatar, plus the messages and the bit budget the inspector shows.</summary>
    public sealed class AvatarPlan
    {
        public Transform AvatarRoot;
        public MenuTree Tree;
        public ParameterPlan Plan;
        public List<TriggerPlan> Triggers = new List<TriggerPlan>();
        public List<ModelMessage> Messages = new List<ModelMessage>();
        public AnimationUsage Usage = AnimationUsage.Empty;
        public int AvatarBits;
        public int AvatarDoctorBits;
        public int Version;
        /// <summary>Why this plan was built: what dropped its avatar's plan before it. Read by the slow refresh console line.</summary>
        public string BuildReason = "";
        /// <summary>The Write Defaults mode AvatarDoc's own layers will build with, read from the avatar's own FX controller by AnimatorBuilder.DecideWriteDefaultsForAvatar.</summary>
        public bool WriteDefaultsOn = true;
        /// <summary>
        /// Which entry's own row gets credit for a shared constraint's target: a follow or a grabbable claims a
        /// target's one constraint for the first entry that names it, in tree order, exactly as the real build
        /// shares one constraint between every row that claims the same object. Computed once here, with the plan,
        /// not per refresh, so "What this builds" can ask it without walking the tree again.
        /// </summary>
        public Dictionary<GameObject, string> ConstraintClaims = new Dictionary<GameObject, string>();

        Dictionary<string, List<ModelMessage>> byEntryId;
        Dictionary<string, List<ModelMessage>> byEditId;

        /// <summary>The messages that name this entry. Entry ids are unique across the avatar, so the component only says whose inspector is asking.</summary>
        public IEnumerable<ModelMessage> MessagesFor(AvatarDoc component, MenuEntry entry)
        {
            return entry == null ? Enumerable.Empty<ModelMessage>() : MessagesForId(entry.id);
        }

        /// <summary>The messages filed under this id directly: an entry id, or any other id a message was given with For (a merged animator's id, for instance).</summary>
        public IEnumerable<ModelMessage> MessagesForId(string id)
        {
            if (string.IsNullOrEmpty(id)) return Enumerable.Empty<ModelMessage>();
            if (byEntryId == null)
            {
                byEntryId = new Dictionary<string, List<ModelMessage>>();
                foreach (var message in Messages)
                    foreach (var entryId in message.EntryIds)
                    {
                        if (!byEntryId.TryGetValue(entryId, out var list)) byEntryId[entryId] = list = new List<ModelMessage>();
                        if (!list.Contains(message)) list.Add(message);
                    }
            }
            return byEntryId.TryGetValue(id, out var found) ? found : Enumerable.Empty<ModelMessage>();
        }

        /// <summary>The messages that name this menu edit, so an existing or foreign row can show why it did (or did not) take effect.</summary>
        public IEnumerable<ModelMessage> MessagesForEdit(string editId)
        {
            if (string.IsNullOrEmpty(editId)) return Enumerable.Empty<ModelMessage>();
            if (byEditId == null)
            {
                byEditId = new Dictionary<string, List<ModelMessage>>();
                foreach (var message in Messages)
                {
                    if (string.IsNullOrEmpty(message.EditId)) continue;
                    if (!byEditId.TryGetValue(message.EditId, out var list)) byEditId[message.EditId] = list = new List<ModelMessage>();
                    list.Add(message);
                }
            }
            return byEditId.TryGetValue(editId, out var found) ? found : Enumerable.Empty<ModelMessage>();
        }
    }

    /// <summary>The merged tree and parameter plan the scene preview works from. Smaller than an AvatarPlan: no foreign menu, no messages, no usage index.</summary>
    public sealed class PreviewPlan
    {
        public MenuTree Tree;
        public ParameterPlan Plan;
        public string Components = "";
    }

    /// <summary>
    /// Builds the model once per avatar root and hands the same instance out until something it reads changes. An
    /// editor event is classified before anything is dropped: a change names the object it touched, so only the
    /// avatar that object lives under plans again; a hierarchy change drops only an avatar that gained or lost a
    /// transform; an animator, expressions menu or parameters asset drops every plan, and a material or a mesh only
    /// the plans that read it. An undo, an animator asset change or import and an explicit Invalidate also drop the
    /// controller reads.
    /// </summary>
    public static class AvatarPlanCache
    {
        static readonly Dictionary<Transform, AvatarPlan> plans = new Dictionary<Transform, AvatarPlan>();
        static readonly Dictionary<Transform, List<ProvidedParameter>> provided = new Dictionary<Transform, List<ProvidedParameter>>();
        static readonly Dictionary<Transform, PreviewPlan> previewPlans = new Dictionary<Transform, PreviewPlan>();
        /// <summary>
        /// What a control removes and what keeps a parameter alive, per avatar. This answers to which names a menu
        /// edit marks for removal and to what else on the avatar reads them, never to where an item sits in the
        /// tree, so a reorder or a reparent (InvalidateOrder) leaves it standing; anything that can change what is
        /// marked for removal or what reads a name (InvalidateAll, or dropping one avatar's plan) clears it.
        /// </summary>
        static readonly Dictionary<Transform, RemovalPlan> removalPlans = new Dictionary<Transform, RemovalPlan>();
        static readonly List<ProvidedParameter> noParameters = new List<ProvidedParameter>();
        /// <summary>
        /// Objects this inspector has just written itself, one note per write. The first change event that names one
        /// repeats what the panel already folded in and is spent on it; a second one is news, and so is one that
        /// comes long after the write, or after a later write moved the object's dirty count past the noted one.
        /// </summary>
        static readonly Dictionary<int, SelfNote> selfEdited = new Dictionary<int, SelfNote>();

        /// <summary>One self edit: when it was noted, the object's dirty count right after the write, and the scene the write marked changed.</summary>
        struct SelfNote
        {
            public double Time;
            public int Dirty;
            public Scene Scene;
        }
        /// <summary>How long a self edit note waits for Unity's report of the write. The report comes at the end of the same frame, after the panel's own refresh, however slow that refresh is.</summary>
        const double SelfEditSeconds = 10.0;
        /// <summary>
        /// Each cached avatar's transform count and blueprint id, read when its caches were first filled after a wide
        /// drop. A hierarchy change that leaves the count alone happened somewhere else; the blueprint finds the
        /// copies of an avatar that is gone, whose plans compare themselves with it.
        /// </summary>
        static readonly Dictionary<Transform, (int count, string blueprint)> snapshots = new Dictionary<Transform, (int, string)>();
        /// <summary>
        /// The materials and meshes each cached plan read, by instance id: the materials of a property target's
        /// renderer (and the materials the same entry swaps in), the mesh of a shape target. A change to one of them
        /// drops that avatar's plan; a change to a material or a mesh no plan read drops nothing.
        /// </summary>
        static readonly Dictionary<Transform, HashSet<int>> assetsRead = new Dictionary<Transform, HashSet<int>>();
        /// <summary>AvatarDoc components outside every avatar that a view asked a plan for. Such a plan is built afresh on every ask, so a change under the same top object only has to move Version for its view to look again.</summary>
        static readonly HashSet<AvatarDoc> looseComponents = new HashSet<AvatarDoc>();
        /// <summary>Why each avatar's plan was last dropped by something that named the avatar. allDropReason stands for every avatar since the last drop of them all.</summary>
        static readonly Dictionary<Transform, string> dropReasons = new Dictionary<Transform, string>();
        static string allDropReason = "";
        static int version;
        /// <summary>
        /// Bumped only by a real structural change (InvalidateAll): an add, a remove, a rename, a target change,
        /// a remove-flag flip, anything besides a move. A move bumps Version, for the poller, but not this, so a
        /// cache that only cares about what could change (PlatformParity's cross-avatar comparison, which reads
        /// generated-parameter order and so is not itself order-independent enough for InvalidateOrder to clear
        /// it directly) can key off this instead and survive a reorder or a reparent.
        /// </summary>
        static int structureVersion;
        /// <summary>
        /// Per avatar root, how many times that avatar's own served plan could have changed: a menu edit, a move
        /// or a wider component change all bump the one avatar that happened under. Added to globalEpoch, which a
        /// coarse InvalidateAll bumps instead, since a coarse event does not know which avatar it touched and so
        /// has to mark every one of them stale. PlatformParity keys its per-avatar comparison cache off this
        /// instead of Version or StructureVersion, so editing one avatar never forces a twin's own untouched
        /// entry to rebuild, and a reorder (which does not touch StructureVersion) still recomputes it.
        /// </summary>
        static readonly Dictionary<Transform, int> planVersions = new Dictionary<Transform, int>();
        static int globalEpoch;
        /// <summary>The platform the cached plans were built for. The active build target can change while the editor is open, and a test can stand in for it.</summary>
        static bool? builtForMobile;

        public static int Version => version;

        /// <summary>See structureVersion.</summary>
        public static int StructureVersion => structureVersion;

        /// <summary>See planVersions and globalEpoch.</summary>
        public static int VersionFor(Transform avatarRoot)
        {
            int local = avatarRoot != null && planVersions.TryGetValue(avatarRoot, out var v) ? v : 0;
            return globalEpoch + local;
        }

        static void TouchPlanVersion(Transform avatarRoot)
        {
            if (avatarRoot == null) return;
            planVersions.TryGetValue(avatarRoot, out var v);
            planVersions[avatarRoot] = v + 1;
        }

        /// <summary>How often the NDMF parameter providers were walked across the whole avatar. Read by the tests and the performance probe.</summary>
        public static int ProvidedWalks { get; private set; }

        /// <summary>How often a preview plan was built instead of reused. Read by the tests and the performance probe.</summary>
        public static int PreviewPlanBuilds { get; private set; }

        /// <summary>How often an inspector plan was built instead of reused. Read by the tests and the performance probe.</summary>
        public static int PlanBuilds { get; private set; }

        /// <summary>
        /// Raised when an open panel has something to show at once: a plan was dropped by an undo, a hierarchy
        /// change or an import, an AvatarDoc or a descriptor was written, or a cosmetic edit moved a name or an
        /// icon. The argument is the avatar root it happened under, or null for every avatar. A change to anything
        /// else under an avatar only marks the plan stale, for the poller to pick up.
        /// </summary>
        public static event System.Action<Transform> Changed;

        [InitializeOnLoadMethod]
        static void Hook()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            ObjectChangeEvents.changesPublished += OnChangesPublished;
        }

        /// <summary>
        /// Why a plan was last dropped, in a few words: which editor event or which caller, and the object or the
        /// avatar it named. Read by the console line of a menu that closed by itself; the slow refresh line reads
        /// the plan's own BuildReason, which a later drop elsewhere cannot overwrite.
        /// </summary>
        public static string LastInvalidationReason { get; private set; } = "";

        /// <summary>Why this avatar's plan was last dropped, or why every plan was, when nothing since named this avatar.</summary>
        static string ReasonFor(Transform avatarRoot)
        {
            return avatarRoot != null && dropReasons.TryGetValue(avatarRoot, out var reason) ? reason : allDropReason;
        }

        /// <summary>An undo or a redo says nothing about what it put back, an Animator window edit included, so every plan goes and the controller reads with them.</summary>
        static void OnUndoRedo() => InvalidateAll("undo or redo", notify: true, controllers: true);

        /// <summary>
        /// Unity says only that some hierarchy changed. An avatar that gained or lost a transform, or that is gone,
        /// plans again. Anything else leaves the plans standing: a rename or a move inside an avatar arrives in
        /// ObjectChangeEvents with the object it touched, and an object outside every avatar is read by no plan.
        /// </summary>
        static void OnHierarchyChanged()
        {
            List<Transform> changed = null;
            foreach (var pair in snapshots)
            {
                if (pair.Key != null && pair.Key.hierarchyCount == pair.Value.count) continue;
                if (changed == null) changed = new List<Transform>();
                changed.Add(pair.Key);
            }
            if (changed == null) return;
            var reason = "hierarchy changed under " + string.Join(", ", changed.Select(r => r != null ? Quote(r) : "an avatar that was removed"));
            LastInvalidationReason = reason;
            foreach (var root in changed)
            {
                var blueprint = snapshots[root].blueprint;
                snapshots.Remove(root);
                if (root == null)
                {
                    DropGone(root, blueprint, reason);
                    continue;
                }
                TouchPlanVersion(root);
                Drop(root, wide: true, reason);
            }
            version++;
            structureVersion++;
            Changed?.Invoke(changed.Count == 1 && changed[0] != null ? changed[0] : null);
        }

        /// <summary>Forgets everything kept for an avatar that was destroyed, and drops the plans of its copies, which compared themselves with it.</summary>
        static void DropGone(Transform gone, string blueprint, string reason)
        {
            plans.Remove(gone);
            previewPlans.Remove(gone);
            removalPlans.Remove(gone);
            provided.Remove(gone);
            planVersions.Remove(gone);
            dropReasons.Remove(gone);
            assetsRead.Remove(gone);
            ParameterUsage.ClearAvatarCache(gone);
            VrcFuryReader.ClearCache(gone);
            if (string.IsNullOrEmpty(blueprint)) return;
            foreach (var twin in plans.Keys.Where(r => r != null && BlueprintOf(r) == blueprint).ToList())
            {
                TouchPlanVersion(twin);
                DropOne(twin, wide: false, reason);
            }
        }

        static string Quote(Transform avatarRoot) => avatarRoot != null ? "'" + avatarRoot.name + "'" : "a component outside every avatar";

        static string Describe(Object target) => target != null ? target.GetType().Name + " '" + target.name + "'" : "a destroyed object";

        /// <summary>
        /// Takes one batch of ObjectChangeEvents, the way Unity publishes it at the end of a frame. An event that
        /// names a scene object is taken to the avatar that object lives under. An animator asset drops every plan
        /// and the controller reads; an expressions menu or parameters asset drops every plan; a material or a mesh
        /// drops only the plans that read it, and any other asset (a texture, a shader) is read by no plan.
        /// EditorUtility.SetDirty marks a scene changed without saying what changed: a mark of a scene this
        /// inspector's own noted write is in is that write's mark, a mark of any other scene drops every plan.
        /// Public so a test can hand it a batch the way Unity does.
        /// </summary>
        public static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            var changed = new List<Change>();
            var otherAssets = new List<int>();
            var markedScenes = new List<Scene>();
            var ownScenes = new HashSet<Scene>();
            string animatorAsset = null;
            string expressionsAsset = null;
            double now = EditorApplication.timeSinceStartup;
            for (int i = 0; i < stream.length; i++)
            {
                var kind = stream.GetEventType(i);
                switch (kind)
                {
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                    {
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var e);
                        if (SpendSelfEdit(e.instanceId, now, out var ownScene)) { ownScenes.Add(ownScene); break; }
                        changed.Add(new Change(e.instanceId, kind + " of"));
                        break;
                    }
                    case ObjectChangeKind.ChangeGameObjectStructure:
                    {
                        stream.GetChangeGameObjectStructureEvent(i, out var e);
                        changed.Add(new Change(e.instanceId, kind + " of"));
                        break;
                    }
                    case ObjectChangeKind.CreateGameObjectHierarchy:
                    {
                        stream.GetCreateGameObjectHierarchyEvent(i, out var e);
                        changed.Add(new Change(e.instanceId, kind + " of", below: true));
                        break;
                    }
                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                    {
                        stream.GetChangeGameObjectStructureHierarchyEvent(i, out var e);
                        changed.Add(new Change(e.instanceId, kind + " of", below: true));
                        break;
                    }
                    case ObjectChangeKind.ChangeGameObjectParent:
                    {
                        stream.GetChangeGameObjectParentEvent(i, out var e);
                        changed.Add(new Change(e.instanceId, kind + " of"));
                        changed.Add(new Change(e.previousParentInstanceId, kind + ", away from"));
                        break;
                    }
                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                    {
                        stream.GetDestroyGameObjectHierarchyEvent(i, out var e);
                        changed.Add(new Change(e.instanceId, kind + " of"));
                        changed.Add(new Change(e.parentInstanceId, kind + ", a child of"));
                        break;
                    }
                    case ObjectChangeKind.ChangeChildrenOrder:
                    {
                        stream.GetChangeChildrenOrderEvent(i, out var e);
                        changed.Add(new Change(e.instanceId, kind + " of"));
                        break;
                    }
                    case ObjectChangeKind.UpdatePrefabInstances:
                    {
                        stream.GetUpdatePrefabInstancesEvent(i, out var e);
                        foreach (var id in e.instanceIds) changed.Add(new Change(id, kind + " of", below: true));
                        break;
                    }
                    case ObjectChangeKind.ChangeAssetObjectProperties:
                    {
                        stream.GetChangeAssetObjectPropertiesEvent(i, out var e);
                        NoteAsset(e.instanceId, e.guid, ref animatorAsset, ref expressionsAsset, otherAssets);
                        break;
                    }
                    case ObjectChangeKind.CreateAssetObject:
                    {
                        stream.GetCreateAssetObjectEvent(i, out var e);
                        NoteAsset(e.instanceId, e.guid, ref animatorAsset, ref expressionsAsset, otherAssets);
                        break;
                    }
                    case ObjectChangeKind.DestroyAssetObject:
                    {
                        stream.GetDestroyAssetObjectEvent(i, out var e);
                        NoteAsset(e.instanceId, e.guid, ref animatorAsset, ref expressionsAsset, otherAssets);
                        break;
                    }
                    case ObjectChangeKind.ChangeScene:
                    {
                        stream.GetChangeSceneEvent(i, out var e);
                        markedScenes.Add(e.scene);
                        break;
                    }
                }
            }
            selfEdited.Clear();
            if (animatorAsset != null) { InvalidateAll("animator asset changed: " + animatorAsset, notify: false, controllers: true); return; }
            if (expressionsAsset != null) { InvalidateAll("expressions asset changed: " + expressionsAsset, notify: false, controllers: false); return; }
            if (markedScenes.Any(scene => !ownScenes.Contains(scene))) { InvalidateAll("change event: ChangeScene, a scene marked changed by a write that named no object", notify: false, controllers: false); return; }
            NoteReadAssets(otherAssets);
            NoteChanged(changed);
        }

        /// <summary>One object a change event named, how it named it, and whether an avatar below it counts as changed too (a hierarchy made, or one a prefab merge or update rewrote).</summary>
        readonly struct Change
        {
            public readonly Object Target;
            public readonly string What;
            public readonly bool Below;

            public Change(int instanceId, string what, bool below = false)
            {
                Target = EditorUtility.InstanceIDToObject(instanceId);
                What = what;
                Below = below;
            }

            public Change(Object target, string what)
            {
                Target = target;
                What = what;
                Below = false;
            }
        }

        /// <summary>
        /// Whether this change event repeats a write the inspector made itself, and the scene that write marked. The
        /// note stands for one event, not for the whole batch: a second change to the same object in the same batch
        /// really is news, and so is one after a later write moved the dirty count, or one long after the note.
        /// </summary>
        static bool SpendSelfEdit(int instanceId, double now, out Scene scene)
        {
            scene = default(Scene);
            if (!selfEdited.TryGetValue(instanceId, out var note)) return false;
            selfEdited.Remove(instanceId);
            if (now - note.Time > SelfEditSeconds) return false;
            var target = EditorUtility.InstanceIDToObject(instanceId);
            if (target == null || EditorUtility.GetDirtyCount(target) != note.Dirty) return false;
            scene = note.Scene;
            return true;
        }

        /// <summary>Remembers the first animator asset and the first expressions asset a batch changed, by type and name, for the reason the drop gives, and every other asset for the plans that read it.</summary>
        static void NoteAsset(int instanceId, GUID guid, ref string animatorAsset, ref string expressionsAsset, List<int> otherAssets)
        {
            var asset = EditorUtility.InstanceIDToObject(instanceId);
            System.Type type;
            string described;
            if (asset != null)
            {
                type = asset.GetType();
                described = Describe(asset);
            }
            else
            {
                // A sub asset gone before the batch ran: the file it lived in says what it belonged to.
                var path = AssetDatabase.GUIDToAssetPath(guid.ToString());
                type = string.IsNullOrEmpty(path) ? null : AssetDatabase.GetMainAssetTypeAtPath(path);
                described = "a part of " + path;
            }
            if (type != null && IsAnimatorAsset(type)) { if (animatorAsset == null) animatorAsset = described; }
            else if (type != null && (typeof(VRCExpressionsMenu).IsAssignableFrom(type) || typeof(VRCExpressionParameters).IsAssignableFrom(type)))
            {
                if (expressionsAsset == null) expressionsAsset = described;
            }
            else otherAssets.Add(instanceId);
        }

        /// <summary>Drops the plans that read a changed material or mesh, narrow: no parameter or component walk reads one.</summary>
        static void NoteReadAssets(List<int> changed)
        {
            if (changed.Count == 0 || assetsRead.Count == 0) return;
            List<Transform> roots = null;
            string described = null;
            foreach (var pair in assetsRead)
            {
                if (pair.Key == null) continue;
                foreach (var id in changed)
                {
                    if (!pair.Value.Contains(id)) continue;
                    if (roots == null) roots = new List<Transform>();
                    roots.Add(pair.Key);
                    if (described == null) described = Describe(EditorUtility.InstanceIDToObject(id));
                    break;
                }
            }
            if (roots == null) return;
            var reason = described + " changed, read by " + string.Join(", ", roots.Select(Quote));
            LastInvalidationReason = reason;
            foreach (var root in roots)
            {
                TouchPlanVersion(root);
                DropOne(root, wide: false, reason);
            }
            version++;
            structureVersion++;
        }

        /// <summary>The materials a plan's property targets read and the meshes its shape targets read, by instance id.</summary>
        static HashSet<int> AssetsReadBy(MenuTree tree, VRCAvatarDescriptor descriptor)
        {
            var ids = new HashSet<int>();
            void AddMesh(SkinnedMeshRenderer renderer)
            {
                if (renderer != null && renderer.sharedMesh != null) ids.Add(renderer.sharedMesh.GetInstanceID());
            }
            foreach (var component in tree.Components)
            {
                if (component == null) continue;
                foreach (var entry in component.entries)
                {
                    if (entry == null) continue;
                    foreach (var target in entry.blendShapes) if (target != null) AddMesh(target.renderer);
                    foreach (var rule in entry.shapeSets) if (rule != null) AddMesh(rule.mesh);
                    AddMesh(entry.sourceRenderer);
                    foreach (var target in entry.properties)
                    {
                        if (target == null || target.renderer == null) continue;
                        foreach (var material in target.renderer.sharedMaterials)
                            if (material != null) ids.Add(material.GetInstanceID());
                    }
                    foreach (var target in entry.materials)
                        if (target != null && target.material != null) ids.Add(target.material.GetInstanceID());
                }
            }
            if (descriptor != null)
            {
                AddMesh(descriptor.customEyeLookSettings.eyelidsSkinnedMesh);
                AddMesh(descriptor.VisemeSkinnedMesh);
            }
            return ids;
        }

        /// <summary>A controller and everything inside one: a state machine, a state, a transition, a behaviour, a clip or a blend tree, and an avatar mask.</summary>
        static bool IsAnimatorAsset(System.Type type)
        {
            return typeof(RuntimeAnimatorController).IsAssignableFrom(type)
                || typeof(AnimatorStateMachine).IsAssignableFrom(type)
                || typeof(AnimatorState).IsAssignableFrom(type)
                || typeof(AnimatorTransitionBase).IsAssignableFrom(type)
                || typeof(StateMachineBehaviour).IsAssignableFrom(type)
                || typeof(Motion).IsAssignableFrom(type)
                || typeof(AvatarMask).IsAssignableFrom(type);
        }

        /// <summary>
        /// Takes one batch of changed scene objects: their avatars' plans are dropped, and an open panel is told at
        /// once only when one of them is an AvatarDoc or the avatar descriptor. A renderer or a component under the
        /// avatar changes per pointer move while something is dragged, so those wait for the poller. A transform
        /// holds nothing a plan reads. A destroyed object and one that is neither a GameObject nor a component (the
        /// object a prefab instance keeps its overrides in) name no avatar; a destroyed component already arrives as
        /// a structure change of its GameObject. An object outside every avatar drops nothing, but a hierarchy made
        /// or rewritten above avatars drops the avatars below it, and a change under an AvatarDoc outside every
        /// avatar moves Version for that component's view. An AvatarDoc is the only kind of
        /// object this cannot see cause a wide drop for: it never adds, removes or retargets a PhysBone, a contact
        /// receiver, a controller asset or a Modular Avatar component by itself, so a batch made up only of
        /// AvatarDoc changes drops just the built tree and removal plan, the same as InvalidateMenu, and leaves the
        /// avatar-wide parameter walk and the NDMF-provided list standing.
        /// </summary>
        public static void NoteChangedObjects(IReadOnlyList<Object> changed)
        {
            var named = new List<Change>(changed.Count);
            foreach (var target in changed) named.Add(new Change(target, ObjectChangeKind.ChangeGameObjectOrComponentProperties + " of"));
            NoteChanged(named);
        }

        static void NoteChanged(List<Change> changed)
        {
            var roots = new List<Transform>();
            var wideRoots = new HashSet<Transform>();
            var described = new Dictionary<Transform, List<string>>();
            HashSet<Transform> looseTops = null;
            bool immediate = false;
            bool loose = false;
            void AddChange(Transform root, string change, bool wide)
            {
                if (!described.TryGetValue(root, out var names))
                {
                    described[root] = names = new List<string>();
                    roots.Add(root);
                }
                names.Add(change);
                if (wide) wideRoots.Add(root);
            }
            foreach (var change in changed)
            {
                var target = change.Target;
                if (target == null || target is Transform) continue;
                if (!(target is GameObject) && !(target is Component)) continue;
                var root = AvatarRootOf(target);
                if (root != null)
                {
                    AddChange(root, change.What + " " + Describe(target), !(target is AvatarDoc));
                    immediate |= ShowsAtOnce(target);
                    continue;
                }
                // A group or a prefab instance that holds avatars: a hierarchy made with them inside, or one a prefab
                // merge or update rewrote, changed every avatar below it.
                if (change.Below && target is GameObject group)
                    foreach (var below in CachedRoots().Where(r => r.IsChildOf(group.transform)).ToList())
                        AddChange(below, change.What + " " + Describe(target), true);
                // An AvatarDoc outside every avatar is planned afresh on every ask, so its view only has to hear that
                // something under the same top object changed.
                if (target is AvatarDoc) loose = true;
                else if (looseComponents.Count > 0)
                {
                    if (looseTops == null)
                    {
                        looseComponents.RemoveWhere(c => c == null);
                        looseTops = new HashSet<Transform>(looseComponents.Select(c => c.transform.root));
                    }
                    var top = (target is GameObject go ? go.transform : ((Component)target).transform).root;
                    loose |= looseTops.Contains(top);
                }
            }
            if (roots.Count == 0)
            {
                if (loose) version++;
                return;
            }
            var reason = string.Join("; ", roots.Select(root => NamesOf(described[root]) + " under " + Quote(root)));
            LastInvalidationReason = reason;
            foreach (var root in roots)
            {
                TouchPlanVersion(root);
                Drop(root, wideRoots.Contains(root), reason);
            }
            version++;
            structureVersion++;
            if (immediate) Changed?.Invoke(roots.Count == 1 ? roots[0] : null);
        }

        /// <summary>Every avatar root that holds a cached plan, preview plan or parameter walk.</summary>
        static IEnumerable<Transform> CachedRoots()
        {
            return plans.Keys.Concat(previewPlans.Keys).Concat(provided.Keys).Concat(snapshots.Keys).Where(r => r != null).Distinct();
        }

        /// <summary>The first three changes, and how many more there were.</summary>
        static string NamesOf(List<string> names)
        {
            var shown = string.Join(", ", names.Take(3));
            return names.Count > 3 ? shown + " and " + (names.Count - 3) + " more" : shown;
        }

        /// <summary>Whether a changed object is one an open panel reads directly, and so has to show without waiting for the poller.</summary>
        static bool ShowsAtOnce(Object target)
        {
            if (target is AvatarDoc || target is VRCAvatarDescriptor) return true;
            return target is GameObject go && (go.GetComponent<AvatarDoc>() != null || go.GetComponent<VRCAvatarDescriptor>() != null);
        }

        static Transform AvatarRootOf(Object target)
        {
            var transform = target is GameObject go ? go.transform : target is Component component ? component.transform : null;
            return transform != null ? nadena.dev.ndmf.runtime.RuntimeUtil.FindAvatarInParents(transform) : null;
        }

        /// <summary>
        /// Drops this avatar's plan and, with it, the plans of the other copies of the same blueprint. A parity
        /// warning lives in the plan of the copy that raised it, so fixing one copy has to replan the other: on
        /// its own, the changed root's plan would leave a warning about a difference that is gone. Only the
        /// avatars that already hold a plan are looked at, so this costs nothing on an avatar without a twin. A
        /// twin's own components never changed just because this avatar did, so a twin is always dropped narrow
        /// regardless of how the primary root was dropped.
        /// </summary>
        static void Drop(Transform avatarRoot, bool wide, string reason)
        {
            DropOne(avatarRoot, wide, reason);
            var blueprint = BlueprintOf(avatarRoot);
            if (string.IsNullOrEmpty(blueprint)) return;
            List<Transform> twins = null;
            foreach (var root in plans.Keys)
            {
                if (root == null || root == avatarRoot || BlueprintOf(root) != blueprint) continue;
                if (twins == null) twins = new List<Transform>();
                twins.Add(root);
            }
            if (twins == null) return;
            foreach (var twin in twins) DropOne(twin, wide: false, reason);
        }

        /// <summary>
        /// Drops the built tree, the removal plan and the preview plan for this avatar. wide additionally drops
        /// the NDMF-provided parameter list, the avatar-wide parameter walk and the VRCFury component cache: the
        /// three things that read the avatar's other components rather than this one AvatarDoc's own data, so
        /// only a change that could touch one of those components needs to pay for redoing them.
        /// </summary>
        static void DropOne(Transform avatarRoot, bool wide, string reason)
        {
            plans.Remove(avatarRoot);
            previewPlans.Remove(avatarRoot);
            removalPlans.Remove(avatarRoot);
            assetsRead.Remove(avatarRoot);
            dropReasons[avatarRoot] = reason;
            if (!wide) return;
            snapshots.Remove(avatarRoot);
            provided.Remove(avatarRoot);
            ParameterUsage.ClearAvatarCache(avatarRoot);
            VrcFuryReader.ClearCache(avatarRoot);
        }

        /// <summary>Notes the avatar's transform count and blueprint the first time one of its caches is filled after a wide drop, for OnHierarchyChanged to compare with.</summary>
        static void Remember(Transform avatarRoot)
        {
            if (avatarRoot != null && !snapshots.ContainsKey(avatarRoot)) snapshots[avatarRoot] = (avatarRoot.hierarchyCount, BlueprintOf(avatarRoot));
        }

        static string BlueprintOf(Transform avatarRoot)
        {
            if (avatarRoot == null) return null;
            var pipeline = avatarRoot.GetComponent<VRC.Core.PipelineManager>();
            return pipeline != null ? pipeline.blueprintId : null;
        }

        /// <summary>
        /// Says that this object was written by a panel that has already rebuilt what the write changed. The change
        /// event Unity publishes for it at the end of the frame then repeats work instead of finding anything new.
        /// </summary>
        public static void NoteSelfEdit(Object target)
        {
            if (target == null) return;
            var scene = target is Component component ? component.gameObject.scene : target is GameObject go ? go.scene : default(Scene);
            selfEdited[target.GetInstanceID()] = new SelfNote { Time = EditorApplication.timeSinceStartup, Dirty = EditorUtility.GetDirtyCount(target), Scene = scene };
        }

        /// <summary>Drops every plan when the editor now builds for the other platform. Every entry point that hands a plan out asks first, so a switched build target never shows a plan made for the platform before it.</summary>
        static void CheckPlatform()
        {
            bool mobile = Platform.IsMobileTarget;
            if (builtForMobile == mobile) return;
            var reason = builtForMobile == null ? "first plan since the scripts were loaded" : "build target switched";
            builtForMobile = mobile;
            InvalidateAll(reason, notify: false, controllers: false);
        }

        /// <summary>Drops every plan and everything read from the controllers behind them.</summary>
        public static void Invalidate() => InvalidateAll("Invalidate", notify: true, controllers: true);

        /// <summary>Drops every plan after an asset was read from disk: an import is never an ObjectChangeEvents change. controllers for an animation or a controller, whose reads go with it.</summary>
        internal static void InvalidateImported(string path, bool controllers) => InvalidateAll("asset imported: " + path, notify: true, controllers);

        /// <summary>
        /// Drops every plan, for an event that cannot say which avatar it touched. Every avatar's per-root version
        /// moves too, through the shared epoch, known or not yet seen. controllers also drops what was read from
        /// the controller assets: LayerAnalysis's layer reads (with TouchBuilder's tracking control owners and the
        /// cached Write Defaults and layer control answers) and ParameterUsage's controller reads. Their
        /// (instance id, GetDirtyCount) keys miss an edit that touched only a state, a transition or a behaviour
        /// (AddCondition records its undo on the transition), so an undo or redo, an animator asset change or
        /// import and an explicit Invalidate drop them here; nothing else can change a controller.
        /// </summary>
        static void InvalidateAll(string reason, bool notify, bool controllers)
        {
            LastInvalidationReason = reason;
            allDropReason = reason;
            dropReasons.Clear();
            version++;
            structureVersion++;
            globalEpoch++;
            plans.Clear();
            provided.Clear();
            previewPlans.Clear();
            removalPlans.Clear();
            snapshots.Clear();
            assetsRead.Clear();
            looseComponents.RemoveWhere(c => c == null);
            ParameterUsage.ClearAvatarCache(null);
            VrcFuryReader.ClearCache();
            if (controllers)
            {
                LayerAnalysis.DropCache();
                ParameterUsage.DropCaches();
            }
            if (notify) Changed?.Invoke(null);
        }

        /// <summary>
        /// A move: a reorder or a reparent that changes where entries sit without adding, removing, renaming or
        /// changing what any of them read or write. The tree still has to be rebuilt so the new order and parents
        /// show, but the removal plan and the avatar-wide NDMF parameter walk answer exactly what they did before
        /// the move, so this drops only the built plan and leaves those standing instead of paying to redo them.
        /// Generated parameters are appended in menu order, so a reorder can still move where they land: the twin
        /// comparison has to see that, so this also touches this avatar's own plan version (PlatformParity keys
        /// its cache off it) and drops the built plan of any twin, so an open twin inspector replans against the
        /// new order too instead of showing a comparison that is one move stale.
        /// </summary>
        public static void InvalidateOrder(Transform avatarRoot)
        {
            var reason = "a move in the menu of " + Quote(avatarRoot);
            LastInvalidationReason = reason;
            version++;
            if (avatarRoot == null)
            {
                plans.Clear();
                previewPlans.Clear();
                dropReasons.Clear();
                allDropReason = reason;
                globalEpoch++;
                Changed?.Invoke(null);
                return;
            }
            TouchPlanVersion(avatarRoot);
            plans.Remove(avatarRoot);
            // The scene preview lets the later control in tree order win a shared object, shape or slot.
            previewPlans.Remove(avatarRoot);
            dropReasons[avatarRoot] = reason;
            var blueprint = BlueprintOf(avatarRoot);
            if (!string.IsNullOrEmpty(blueprint))
            {
                List<Transform> twins = null;
                foreach (var root in plans.Keys)
                {
                    if (root == null || root == avatarRoot || BlueprintOf(root) != blueprint) continue;
                    if (twins == null) twins = new List<Transform>();
                    twins.Add(root);
                }
                if (twins != null)
                    foreach (var twin in twins)
                    {
                        plans.Remove(twin);
                        dropReasons[twin] = reason;
                    }
            }
            Changed?.Invoke(avatarRoot);
        }

        /// <summary>
        /// An edit confined to one AvatarDoc's own serialized data: a rename, an add or remove, a retargeted
        /// control, a flag flip, a hide, a reset or a move of an existing row. None of it can add, remove or
        /// retarget a PhysBone, a contact receiver, a controller asset or a Modular Avatar component, so the
        /// avatar-wide parameter walk and the NDMF-provided parameter list both still answer what they did before
        /// the edit; only the built tree and the removal plan, which do read this avatar's own entries, are
        /// dropped, the same as a wide drop minus the three avatar-wide caches. A twin is dropped narrow too, so
        /// its own open inspector replans and sees this avatar's new state in the comparison.
        /// </summary>
        public static void InvalidateMenu(Transform avatarRoot)
        {
            var reason = "a menu edit in " + Quote(avatarRoot);
            if (avatarRoot == null) { InvalidateAll(reason, notify: true, controllers: false); return; }
            LastInvalidationReason = reason;
            version++;
            structureVersion++;
            TouchPlanVersion(avatarRoot);
            Drop(avatarRoot, wide: false, reason);
            Changed?.Invoke(avatarRoot);
        }

        /// <summary>
        /// A change that alters how something reads but not what the plan says. The plans stand, so every open view
        /// redraws from the one it already holds; the version moves so a view that only polls notices too.
        /// </summary>
        public static void NoteCosmetic(Transform avatarRoot)
        {
            version++;
            Changed?.Invoke(avatarRoot);
        }

        /// <summary>Every parameter the avatar's NDMF providers offer, walked once per avatar and kept until its plan is dropped.</summary>
        public static IReadOnlyList<ProvidedParameter> ProvidedFor(Transform avatarRoot)
        {
            if (avatarRoot == null) return noParameters;
            if (provided.TryGetValue(avatarRoot, out var cached)) return cached;
            Remember(avatarRoot);
            ProvidedWalks++;
            var walked = ParameterInfo.ForUI.GetParametersForObject(avatarRoot.gameObject).ToList();
            provided[avatarRoot] = walked;
            return walked;
        }

        /// <summary>
        /// The removal plan for this avatar, walked once and kept across a reorder or a reparent (InvalidateOrder):
        /// what a control removes and what keeps a parameter alive reads which names are marked for removal and
        /// what else on the avatar reads them, an avatar-wide walk that costs nothing to skip when a move is all
        /// that happened. Anything that could change either of those (InvalidateAll, or dropping this avatar's
        /// plan) clears the cached answer first, so this only ever recomputes when it has to.
        /// </summary>
        static RemovalPlan RemovalPlanFor(Transform avatarRoot, MenuTree tree, VRCAvatarDescriptor descriptor)
        {
            if (avatarRoot != null && removalPlans.TryGetValue(avatarRoot, out var cached)) return cached;
            var plan = RemovalPlanner.Plan(tree, descriptor);
            if (avatarRoot != null) removalPlans[avatarRoot] = plan;
            return plan;
        }

        /// <summary>
        /// The tree and parameter plan the preview needs for this set of components, reused while the avatar's plan
        /// stands. The preview sees only the enabled components and none of the avatar's existing menu, so this is
        /// a plan of its own rather than the inspector's.
        /// </summary>
        public static PreviewPlan PreviewPlanFor(Transform avatarRoot, IReadOnlyList<AvatarDoc> components)
        {
            CheckPlatform();
            // A cached entry is dropped the moment its avatar's plan is, so one that is still here was built for
            // the editor as it stands; only the component set has to be checked against it.
            var key = string.Join(",", components.Select(c => c != null ? c.GetInstanceID() : 0));
            if (avatarRoot != null && previewPlans.TryGetValue(avatarRoot, out var cached) && cached.Components == key)
                return cached;
            Remember(avatarRoot);
            PreviewPlanBuilds++;
            var tree = MenuModel.Build(avatarRoot, null, components);
            var descriptor = avatarRoot != null ? avatarRoot.GetComponent<VRCAvatarDescriptor>() : null;
            var existing = ParametersWriter.ExistingParameters(descriptor != null ? descriptor.expressionParameters : null, ParametersWriter.FxOf(descriptor));
            var plan = new PreviewPlan { Tree = tree, Plan = ParameterPlanner.Plan(tree, existing), Components = key };
            if (avatarRoot != null) previewPlans[avatarRoot] = plan;
            return plan;
        }

        public static Transform RootOf(Component component)
        {
            return nadena.dev.ndmf.runtime.RuntimeUtil.FindAvatarInParents(component.transform);
        }

        public static AvatarPlan GetFor(AvatarDoc component)
        {
            CheckPlatform();
            var root = RootOf(component);
            if (root != null)
            {
                looseComponents.Remove(component);
                return Get(root);
            }
            looseComponents.Add(component);
            return Build(null, new[] { component });
        }

        public static AvatarPlan Get(Transform avatarRoot)
        {
            CheckPlatform();
            if (avatarRoot == null) return Build(null, new AvatarDoc[0]);
            if (plans.TryGetValue(avatarRoot, out var cached)) return cached;
            Remember(avatarRoot);
            // Every component under the root, in hierarchy order and without the enabled filter the build pass uses:
            // a disabled component still has to show itself and its place in the merged menu in its own inspector.
            // The model flags its rows left out and the planners skip them, so the names, the bits, the removals
            // and the menu slots are the ones of the components the build reads.
            var plan = Build(avatarRoot, avatarRoot.GetComponentsInChildren<AvatarDoc>(true));
            plans[avatarRoot] = plan;
            assetsRead[avatarRoot] = AssetsReadBy(plan.Tree, avatarRoot.GetComponent<VRCAvatarDescriptor>());
            return plan;
        }

        static AvatarPlan Build(Transform avatarRoot, IReadOnlyList<AvatarDoc> components)
        {
            PlanBuilds++;
            var descriptor = avatarRoot != null ? avatarRoot.GetComponent<VRCAvatarDescriptor>() : null;
            var existingMenu = descriptor != null ? descriptor.expressionsMenu : null;
            var foreign = avatarRoot != null ? ForeignMenu.Read(avatarRoot, MenuModel.ReadExistingRoot(existingMenu)) : new List<ForeignItem>();
            var tree = MenuModel.Build(avatarRoot, existingMenu, components, foreign, MenuEditMode.Inspector);
            MenuModel.SplitOverflow(tree, null);
            var walked = ProvidedFor(avatarRoot);
            var existing = ParametersWriter.ExistingParameters(descriptor != null ? descriptor.expressionParameters : null, ParametersWriter.FxOf(descriptor));
            if (avatarRoot != null) ParametersWriter.AddProvided(existing, walked);
            var plan = ParameterPlanner.Plan(tree, existing);
            tree.Removal = RemovalPlanFor(avatarRoot, tree, descriptor);
            var touch = TouchBuilder.Describe(tree, existing, plan);
            var (triggers, triggerMessages) = TriggerPlanner.Plan(tree, plan, existing, touch);
            var result = new AvatarPlan
            {
                AvatarRoot = avatarRoot, Tree = tree, Plan = plan, Triggers = triggers, Version = version, BuildReason = ReasonFor(avatarRoot),
                ConstraintClaims = MenuModel.ClaimedConstraintTargets(tree),
            };
            result.Messages.AddRange(tree.Messages);
            result.Messages.AddRange(plan.Messages);
            result.Messages.AddRange(triggerMessages);
            var parity = PlatformParity.Check(avatarRoot);
            if (parity != null) result.Messages.Add(parity);
            result.AvatarDoctorBits = plan.NewSyncedBits;
            if (avatarRoot != null)
                result.AvatarBits = Mathf.Max(0, walked.Where(p => !(p.Source is AvatarDoc)).Sum(p => p.BitUsage) - tree.Removal.FreedBits);
            result.Usage = AnimationUsage.ForDescriptor(descriptor, avatarRoot);
            // An animated animator parameter only reaches its own controller's layers, so a blend shape trigger
            // cannot follow its shape in a Gesture, Action or Additive clip. The usage index is memoised per
            // controller set, so this reads no asset on a refresh; the build says the same for merged clips.
            foreach (var trigger in triggers)
            {
                if (trigger.Kind != TriggerKind.Link) continue;
                var outside = result.Usage.ClipsOutsideFx(trigger.SourceRenderer, trigger.SourceShape);
                if (outside.Count > 0)
                    result.Messages.Add(ModelMessage.Warning("ad.warn.link_clips_outside_fx", trigger.Entry.PlainName, trigger.SourceRenderer.name + "/" + trigger.SourceShape, string.Join(", ", outside.Select(c => c.name))).For(trigger.Entry.id));
            }

            // Mirrors the VRChat SDK's own build validation ("VRCExpressionParameters has too many parameters
            // defined."), computed the same way AvatarDoctorBuilder computes it at real build time, so the
            // creator sees it here instead of only once upload fails.
            int totalBits = result.AvatarBits + result.AvatarDoctorBits;
            var budget = ParametersWriter.BudgetMessage(totalBits, plan.NewSyncedBits);
            if (budget != null) result.Messages.Add(budget);

            // Mirrors VRChat's own guidance that Write Defaults must be consistent across the whole avatar: read
            // from the avatar's own saved FX controller, never built, so this costs nothing beyond what ValidateMerges already pays to read the same asset.
            var fx = ParametersWriter.FxOf(descriptor);
            var writeDefaults = AnimatorBuilder.DecideWriteDefaultsForAvatar(fx);
            result.WriteDefaultsOn = writeDefaults.WriteDefaults;
            if (writeDefaults.Mixed)
                result.Messages.Add(ModelMessage.Warning("ad.warn.write_defaults_mixed", string.Join(", ", writeDefaults.OnLayers), string.Join(", ", writeDefaults.OffLayers)));

            return result;
        }
    }
}
#endif
