#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
#if DOCME_MA
using nadena.dev.modular_avatar.core;
#endif

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>What kind of thing reads a parameter name. Removal needs the kind to say, in one word, what keeps a parameter alive.</summary>
    public enum UsageKind
    {
        /// <summary>A menu control: the avatar's own menu, or one another tool installs.</summary>
        MenuControl,
        /// <summary>An AvatarDoctor entry, preset or trigger that names the parameter.</summary>
        Entry,
        /// <summary>A layer of the avatar's own FX controller reads it.</summary>
        Layer,
        /// <summary>A playable layer other than FX, or a controller another tool merges, reads it.</summary>
        OtherController,
        /// <summary>A parameter driver writes it.</summary>
        Driver,
        /// <summary>A contact receiver or a PhysBone writes it.</summary>
        Contact,
        /// <summary>Another tool on the avatar declares or drives it: a VRCFury toggle on a global parameter, a Modular Avatar parameter declaration or menu item.</summary>
        OtherTool,
        /// <summary>A controller declares the name without any layer reading it. On its own this keeps nothing.</summary>
        Declaration,
    }

    /// <summary>One thing that reads a parameter name.</summary>
    public sealed class ParameterReader
    {
        public UsageKind Kind;
        /// <summary>The menu item, control node, controller or component behind this reader.</summary>
        public object Owner;
        /// <summary>The layer this reader sits in, for Layer and Driver. Empty for everything else.</summary>
        public string Layer = "";
    }

    /// <summary>
    /// Who on the avatar reads a parameter by name, gathered once per scan. A converted entry gets a parameter
    /// of its own, so the name it used to carry disappears: anything else that was reading it would quietly stop
    /// working, which is why every importer asks here before turning an item into a real entry.
    /// </summary>
    public sealed class ParameterUsage
    {
        /// <summary>The sub-parameters a PhysBone writes from its own prefix.</summary>
        internal static readonly string[] PhysBoneSuffixes = { "_IsGrabbed", "_IsPosed", "_Angle", "_Stretch", "_Squish" };

        readonly Dictionary<string, List<ParameterReader>> readers = new Dictionary<string, List<ParameterReader>>();
        /// <summary>The avatar-wide readers this usage was built from, shared with every other usage of the same avatar. Never mutated once set: only this instance's own readers dictionary is written to.</summary>
        IReadOnlyDictionary<string, List<ParameterReader>> baseReaders;

        /// <summary>
        /// The avatar-wide part of a scan, kept per avatar root: FX and other controllers, contact receivers,
        /// PhysBones, VRCFury and Modular Avatar components other than the merged menu. None of it reads an
        /// AvatarDoc's own entries or menu order, only the avatar's components and controller assets, so it holds
        /// across a rename, an added entry, a retargeted control or any other edit confined to one AvatarDoc's own
        /// data. AvatarPlanCache drops it the same way it drops everything else that actually depends on those
        /// components: NoteChangedObjects and InvalidateAll, never a menu-only edit.
        /// </summary>
        static readonly Dictionary<Transform, ParameterUsage> avatarWide = new Dictionary<Transform, ParameterUsage>();

        /// <summary>How often the avatar-wide part was actually rebuilt instead of reused. Read by the tests and the performance probe.</summary>
        public static int AvatarWideBuilds { get; private set; }

        /// <summary>How many avatar roots currently hold a cached avatar-wide reader graph. Read by the tests, to prove a build clears the entry it made for its own clone rather than leaving it behind.</summary>
        public static int AvatarWideCacheCount => avatarWide.Count;

        /// <summary>Forgets the avatar-wide reader cache for this avatar, or every avatar when root is null.</summary>
        public static void ClearAvatarCache(Transform avatarRoot)
        {
            if (avatarRoot == null) avatarWide.Clear();
            else avatarWide.Remove(avatarRoot);
        }

        /// <summary>
        /// fresh skips the shared cache entirely, reading and writing neither avatarWide nor controllerReads: the
        /// build's own removal plan asks for this, since its clone is thrown away as soon as the build ends and has
        /// no business sharing state with whatever the inspector has cached for the live scene's own controllers.
        /// </summary>
        static ParameterUsage AvatarWideFor(Transform avatarRoot, bool fresh = false)
        {
            if (!fresh && avatarWide.TryGetValue(avatarRoot, out var cached)) return cached;
            AvatarWideBuilds++;
            var usage = new ParameterUsage();

            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor != null)
            {
                var fx = ParametersWriter.FxOf(descriptor);
                foreach (var controller in FxLayerImport.AllControllersOf(descriptor))
                    usage.AddController(controller, controller, controller == fx ? UsageKind.Layer : UsageKind.OtherController, fresh);
            }

            foreach (var receiver in avatarRoot.GetComponentsInChildren<VRCContactReceiver>(true))
                usage.Add(receiver.parameter, receiver, UsageKind.Contact);
            foreach (var physBone in avatarRoot.GetComponentsInChildren<VRCPhysBone>(true))
            {
                if (string.IsNullOrWhiteSpace(physBone.parameter)) continue;
                foreach (var suffix in PhysBoneSuffixes)
                    usage.Add(physBone.parameter.Trim() + suffix, physBone, UsageKind.Contact);
            }

            // What another tool declares or drives never reaches the merged menu model: its menu items carry no
            // parameter there and its own parameter declarations are components, not menu controls. Read from the
            // components, or a parameter an AvatarDoctor control shares with a VRCFury toggle would look unused.
            foreach (var name in VrcFuryReader.GlobalParameters(avatarRoot))
                usage.Add(name, avatarRoot, UsageKind.OtherTool);
            foreach (var name in VrcFuryReader.FullControllerParameters(avatarRoot))
                usage.Add(name, avatarRoot, UsageKind.OtherTool);

#if DOCME_MA
            foreach (var merge in avatarRoot.GetComponentsInChildren<ModularAvatarMergeAnimator>(true))
                usage.AddController(MergeBuilder.Resolve(merge.animator), merge, UsageKind.OtherController, fresh);
            foreach (var installer in avatarRoot.GetComponentsInChildren<ModularAvatarMenuInstaller>(true))
                usage.AddMenuAsset(installer.menuToAppend, installer, new HashSet<VRCExpressionsMenu>());
            foreach (var item in avatarRoot.GetComponentsInChildren<ModularAvatarMenuItem>(true))
            {
                if (item.Control == null) continue;
                foreach (var name in NamesOf(item.Control)) usage.Add(name, item, UsageKind.OtherTool);
                // A submenu that comes from a finished asset drives whatever its own controls name, all the way
                // down. Nothing of that reaches the merged menu model, so it is read from the component here.
                if (item.MenuSource == SubmenuSource.MenuAsset)
                    usage.AddMenuAsset(item.Control.subMenu, item, new HashSet<VRCExpressionsMenu>());
            }
            foreach (var parameters in avatarRoot.GetComponentsInChildren<ModularAvatarParameters>(true))
            {
                if (parameters.parameters == null) continue;
                foreach (var config in parameters.parameters)
                {
                    if (config.isPrefix || string.IsNullOrWhiteSpace(config.nameOrPrefix)) continue;
                    usage.Add(config.nameOrPrefix, parameters, UsageKind.OtherTool);
                    usage.Add(config.remapTo, parameters, UsageKind.OtherTool);
                }
            }
#endif
            if (!fresh) avatarWide[avatarRoot] = usage;
            return usage;
        }

        /// <summary>
        /// Everything on this avatar that reads a parameter name. Built once and shared by the importers of one
        /// scan. fresh bypasses the shared, avatar-keyed cache: the build's own removal plan passes it, since its
        /// clone must never be answered from whatever the inspector cached for the live avatar, or leave behind a
        /// reader graph keyed to a clone that is about to be destroyed.
        /// </summary>
        public static ParameterUsage For(Transform avatarRoot, MenuTree tree, bool fresh = false)
        {
            var usage = new ParameterUsage();
            if (avatarRoot == null) return usage;
            usage.baseReaders = AvatarWideFor(avatarRoot, fresh).readers;

            // A switched off component is not built, so nothing it holds reads a name: not its entries, its
            // declarations, its merged controllers, nor the controls of a menu asset it installs.
            if (tree != null && tree.Root != null)
            {
                var switchedOff = new HashSet<MenuAssetInstall>(tree.MenuAssets.Where(a => MenuModel.IsDisabled(a.Component)).Select(a => a.Install));
                usage.ReadMenu(tree.Root, switchedOff);
            }
            if (tree != null)
            {
                foreach (var control in tree.Controls)
                {
                    if (control.Disabled || control.Unused) continue;
                    var name = control.Entry.parameterName;
                    if (!string.IsNullOrWhiteSpace(name)) usage.Add(name.Trim(), control, UsageKind.Entry);
                }
                foreach (var component in tree.Components)
                    if (component != null && !MenuModel.IsDisabled(component))
                        foreach (var entry in component.entries)
                            foreach (var name in EntryNames(entry))
                                usage.Add(name, component, UsageKind.Entry);
                foreach (var node in tree.Merges)
                    if (node.Merge != null && !MenuModel.IsDisabled(node.Component)) usage.AddController(MergeBuilder.Resolve(node.Merge.controller), node, UsageKind.OtherController, fresh);
                // A declaration keeps a parameter in the expression parameters on its own, so it reads the name as
                // much as an entry does. A control of an installed menu asset is read with the rest of the menu.
                foreach (var component in tree.Components)
                    if (component != null && !MenuModel.IsDisabled(component))
                        foreach (var declaration in component.parameters)
                            if (declaration != null) usage.Add(declaration.name, component, UsageKind.Entry);
            }

            return usage;
        }

        /// <summary>True when anything other than the given readers reads this parameter.</summary>
        public bool UsedBesides(string parameter, params object[] ignore)
        {
            foreach (var reader in Readers(parameter))
            {
                bool ignored = false;
                if (ignore != null)
                    foreach (var one in ignore)
                        if (ReferenceEquals(one, reader.Owner)) { ignored = true; break; }
                if (!ignored) return true;
            }
            return false;
        }

        /// <summary>Everything that reads this parameter name: the avatar-wide readers first, then this scan's own menu readers, in the order each side was found.</summary>
        public IReadOnlyList<ParameterReader> Readers(string parameter)
        {
            if (string.IsNullOrWhiteSpace(parameter)) return System.Array.Empty<ParameterReader>();
            var key = parameter.Trim();
            List<ParameterReader> own;
            readers.TryGetValue(key, out own);
            List<ParameterReader> shared = null;
            baseReaders?.TryGetValue(key, out shared);
            if (shared == null || shared.Count == 0) return (IReadOnlyList<ParameterReader>)own ?? System.Array.Empty<ParameterReader>();
            if (own == null || own.Count == 0) return shared;
            var combined = new List<ParameterReader>(shared.Count + own.Count);
            combined.AddRange(shared);
            combined.AddRange(own);
            return combined;
        }

        /// <summary>Parameter names an entry names itself: a preset's named targets and a trigger watching a parameter.</summary>
        static IEnumerable<string> EntryNames(MenuEntry entry)
        {
            if (entry == null) yield break;
            foreach (var parameter in entry.presetParameters)
                if (parameter != null && !string.IsNullOrWhiteSpace(parameter.name)) yield return parameter.name.Trim();
            // Every condition of a trigger reads its parameter, whether the trigger still works from its flat
            // source fields or names several conditions in groups.
            foreach (var group in entry.EffectiveWhen)
                foreach (var condition in group.all)
                    if (condition != null && condition.kind == WhenConditionKind.Parameter && !string.IsNullOrWhiteSpace(condition.sourceParameter))
                        yield return condition.sourceParameter.Trim();
        }

#if DOCME_MA
        /// <summary>Every parameter the controls of a finished menu asset name, nested submenus included. The visited set stops an asset that reaches itself.</summary>
        void AddMenuAsset(VRCExpressionsMenu menu, object owner, HashSet<VRCExpressionsMenu> visited)
        {
            if (menu == null || menu.controls == null || !visited.Add(menu)) return;
            foreach (var control in menu.controls)
            {
                if (control == null) continue;
                foreach (var name in NamesOf(control)) Add(name, owner, UsageKind.OtherTool);
                if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu) AddMenuAsset(control.subMenu, owner, visited);
            }
            visited.Remove(menu);
        }
#endif

        void ReadMenu(MenuNode node, HashSet<MenuAssetInstall> skipped)
        {
            foreach (var item in node.Items)
            {
                if (item.Asset != null && skipped.Contains(item.Asset)) continue;
                if (item.Existing != null)
                    foreach (var name in NamesOf(item.Existing)) Add(name, item, UsageKind.MenuControl);
                if (item.Submenu != null) ReadMenu(item.Submenu, skipped);
            }
        }

        /// <summary>One name a controller reads, with the layer it sits in. The kind is filled in by the caller, which alone knows whether this controller is the avatar's own FX.</summary>
        struct ControllerRead
        {
            public string Name;
            public string Layer;
            /// <summary>The controller declares the name without a layer reading it.</summary>
            public bool IsDeclaration;
            /// <summary>A parameter driver or a play audio behaviour writes it.</summary>
            public bool IsDriver;
        }

        // Deserialising a controller is the expensive part, and the inspector asks for the same handful of
        // controllers on every refresh, so what was read from each one is kept until the asset is imported again.
        // Keyed by instance id AND EditorUtility.GetDirtyCount, not the instance id alone: the count changes on
        // every edit the Animator window makes, saved or not (both AddLayer and AddCondition route through the
        // controller's shared undo handler, which marks the asset dirty the same way any other Undo-recorded
        // change does), so an edit nobody explicitly invalidated for still misses instead of reading what the
        // controller used to say. DropCaches stays for the one case dirty count cannot see: an asset reimported
        // from disk keeps neither its old instance id nor its old dirty count, so nothing to key against survives,
        // but the entries this leaves behind would otherwise sit here unused forever.
        static readonly Dictionary<(int id, int dirty), List<ControllerRead>> controllerReads = new Dictionary<(int, int), List<ControllerRead>>();

        /// <summary>Forgets what was read from the controller assets. Called when one of them is imported, the way MergeBuilder's caches are dropped.</summary>
        public static void DropCaches() => controllerReads.Clear();

        void AddController(AnimatorController controller, object owner, UsageKind layerKind, bool fresh = false)
        {
            if (controller == null) return;
            foreach (var read in ReadsOf(controller, fresh))
                Add(read.Name, owner, read.IsDeclaration ? UsageKind.Declaration : read.IsDriver ? UsageKind.Driver : layerKind, read.Layer);
        }

        static List<ControllerRead> ReadsOf(AnimatorController controller, bool fresh = false)
        {
            var key = (controller.GetInstanceID(), EditorUtility.GetDirtyCount(controller));
            if (!fresh && controllerReads.TryGetValue(key, out var cached)) return cached;
            var reads = new List<ControllerRead>();
            foreach (var parameter in controller.parameters)
                reads.Add(new ControllerRead { Name = parameter.name, Layer = "", IsDeclaration = true });
            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine == null) continue;
                foreach (var transition in LayerAnalysis.Transitions(layer.stateMachine))
                    foreach (var condition in transition.conditions)
                        reads.Add(new ControllerRead { Name = condition.parameter, Layer = layer.name });
                foreach (var state in LayerAnalysis.States(layer.stateMachine))
                {
                    if (state.speedParameterActive) reads.Add(new ControllerRead { Name = state.speedParameter, Layer = layer.name });
                    if (state.timeParameterActive) reads.Add(new ControllerRead { Name = state.timeParameter, Layer = layer.name });
                    if (state.mirrorParameterActive) reads.Add(new ControllerRead { Name = state.mirrorParameter, Layer = layer.name });
                    if (state.cycleOffsetParameterActive) reads.Add(new ControllerRead { Name = state.cycleOffsetParameter, Layer = layer.name });
                    ReadMotion(reads, state.motion, layer.name);
                    ReadDrivers(reads, state.behaviours, layer.name);
                }
                foreach (var machine in LayerAnalysis.StateMachines(layer.stateMachine))
                    ReadDrivers(reads, machine.behaviours, layer.name);
            }
            if (!fresh) controllerReads[key] = reads;
            return reads;
        }

        static void ReadMotion(List<ControllerRead> reads, Motion motion, string layer)
        {
            if (!(motion is BlendTree tree)) return;
            reads.Add(new ControllerRead { Name = tree.blendParameter, Layer = layer });
            bool is2D = tree.blendType == BlendTreeType.SimpleDirectional2D || tree.blendType == BlendTreeType.FreeformDirectional2D || tree.blendType == BlendTreeType.FreeformCartesian2D;
            if (is2D) reads.Add(new ControllerRead { Name = tree.blendParameterY, Layer = layer });
            foreach (var child in tree.children) ReadMotion(reads, child.motion, layer);
        }

        static void ReadDrivers(List<ControllerRead> reads, IEnumerable<StateMachineBehaviour> behaviours, string layer)
        {
            if (behaviours == null) return;
            foreach (var behaviour in behaviours)
            {
                if (behaviour is VRCAvatarParameterDriver driver && driver.parameters != null)
                    foreach (var parameter in driver.parameters)
                    {
                        reads.Add(new ControllerRead { Name = parameter.name, Layer = layer, IsDriver = true });
                        // A Copy entry reads its source: freeing that name would leave the copy reading nothing.
                        reads.Add(new ControllerRead { Name = parameter.source, Layer = layer, IsDriver = true });
                    }
                if (behaviour is VRCAnimatorPlayAudio audio)
                    reads.Add(new ControllerRead { Name = audio.ParameterName, Layer = layer, IsDriver = true });
            }
        }

        static IEnumerable<string> NamesOf(VRCExpressionsMenu.Control control)
        {
            if (control.parameter != null && !string.IsNullOrEmpty(control.parameter.name)) yield return control.parameter.name;
            if (control.subParameters == null) yield break;
            foreach (var sub in control.subParameters)
                if (sub != null && !string.IsNullOrEmpty(sub.name)) yield return sub.name;
        }

        void Add(string parameter, object owner, UsageKind kind, string layer = "")
        {
            if (string.IsNullOrWhiteSpace(parameter)) return;
            var key = parameter.Trim();
            if (!readers.TryGetValue(key, out var list)) readers[key] = list = new List<ParameterReader>();
            if (list.Any(r => ReferenceEquals(r.Owner, owner) && r.Kind == kind && r.Layer == (layer ?? ""))) return;
            list.Add(new ParameterReader { Kind = kind, Owner = owner, Layer = layer ?? "" });
        }
    }
}
#endif
