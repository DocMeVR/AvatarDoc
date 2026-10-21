#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>What Add to AvatarDoc makes from the value: a new toggle, a new radial, or one more effect of an entry the avatar has.</summary>
    public enum AddMake { NewToggle = 0, NewRadial = 1, AddTo = 2 }

    /// <summary>The Add to AvatarDoc window's working state. Serializable, so an open window keeps it across a script reload.</summary>
    [Serializable]
    public sealed class AddDraft
    {
        public Renderer renderer;
        /// <summary>True: a blend shape of a skinned mesh. False: a material property of one slot.</summary>
        public bool isShape;
        public string shape = "";
        public int slot;
        public string property = "";
        public PropertyValueType type;
        public AddMake make;
        public string name = "";
        public AvatarDoc component;
        public string parentId = "";
        public AvatarDoc targetComponent;
        public string targetId = "";
        /// <summary>The value while a toggle is on, or a radial's highest. Packed: a number in x, a colour in rgba, a vector in xyzw.</summary>
        public Vector4 on;
        /// <summary>The value while a toggle is off, or a radial's lowest.</summary>
        public Vector4 off;
        public bool putBack = true;
        public bool markForPoiyomi = true;
        /// <summary>The entry the last Add made, which the draft now adds to, and what it was made as: an undo that takes the entry away brings New toggle or New radial back.</summary>
        public string madeId = "";
        public AddMake madeAs;
    }

    /// <summary>
    /// Turns the value a blend shape or a material property has now into a toggle or a radial, or one more effect of an
    /// entry the avatar has: the value becomes the On value (a radial's highest), 0 or the shader's default the Off
    /// value (a radial's lowest). Poiyomi is recognised by its shader name and material tags only (PoiyomiLock).
    /// </summary>
    public static class AddToAvatarDoc
    {
        /// <summary>The avatar a renderer in a scene belongs to, or null: a renderer outside every avatar, or one of a prefab asset.</summary>
        public static Transform AvatarOf(Renderer renderer)
        {
            if (renderer == null || EditorUtility.IsPersistent(renderer)) return null;
            return RuntimeUtil.FindAvatarInParents(renderer.transform);
        }

        public static bool CanAdd(Renderer renderer) => AvatarOf(renderer) != null;

        /// <summary>A draft for the renderer's component menu: nothing picked yet, a blend shape first on a skinned mesh that has some.</summary>
        public static AddDraft ForRenderer(Renderer renderer)
        {
            var draft = new AddDraft { renderer = renderer, isShape = HasShapes(renderer) };
            EnsureComponent(draft);
            return draft;
        }

        public static AddDraft ForShape(SkinnedMeshRenderer renderer, string shape)
        {
            var draft = new AddDraft { renderer = renderer };
            EnsureComponent(draft);
            PickShape(draft, renderer, shape);
            return draft;
        }

        /// <summary>A draft for a material's header menu: that slot of the mesh picked, its property left to pick.</summary>
        public static AddDraft ForSlot(Renderer renderer, int slot)
        {
            var draft = new AddDraft { renderer = renderer };
            EnsureComponent(draft);
            PickSlot(draft, slot);
            return draft;
        }

        public static AddDraft ForProperty(Renderer renderer, int slot, string property)
        {
            var draft = new AddDraft { renderer = renderer };
            EnsureComponent(draft);
            PickProperty(draft, slot, property);
            return draft;
        }

        /// <summary>
        /// Brings the draft in line with the scene before the window shows it: the component (EnsureComponent), and an
        /// entry the last Add made that is gone again, by an undo, turns the draft back to making a new one.
        /// </summary>
        public static void Settle(AddDraft draft)
        {
            if (draft == null) return;
            EnsureComponent(draft);
            if (string.IsNullOrEmpty(draft.madeId)) return;
            if (draft.make == AddMake.AddTo && draft.targetId == draft.madeId && Target(draft) == null) draft.make = draft.madeAs;
            if (draft.make != AddMake.AddTo || draft.targetId != draft.madeId) draft.madeId = "";
        }

        /// <summary>Keeps the component a new entry goes into, while it is still one of the avatar's; otherwise picks the first one that is not locked, else the first.</summary>
        public static void EnsureComponent(AddDraft draft)
        {
            if (draft == null) return;
            var components = Components(draft);
            if (draft.component != null && components.Contains(draft.component)) return;
            draft.component = components.FirstOrDefault(c => !EntryEditing.Refuses(c)) ?? components.FirstOrDefault();
            draft.parentId = "";
        }

        /// <summary>Sets the draft to a blend shape: its weight now is the On value, 0 the Off value, and its name the entry's name.</summary>
        public static void PickShape(AddDraft draft, SkinnedMeshRenderer renderer, string shape)
        {
            if (draft == null) return;
            draft.renderer = renderer;
            draft.isShape = true;
            draft.shape = shape ?? "";
            draft.type = PropertyValueType.Float;
            Refill(draft);
        }

        /// <summary>Sets the draft to a property of one material slot: its value now is the On value, the shader's own default the Off value, and its display name the entry's name.</summary>
        public static void PickProperty(AddDraft draft, int slot, string property)
        {
            if (draft == null) return;
            draft.isShape = false;
            draft.slot = slot;
            draft.property = property ?? "";
            var material = MaterialOf(draft);
            var index = ShaderIndex(material, draft.property);
            draft.type = index >= 0 ? TypeOf(material.shader.GetPropertyType(index)) : PropertyValueType.Float;
            Refill(draft);
        }

        /// <summary>Moves the draft to another material slot: the property stays when that slot's material has it too, else none is picked.</summary>
        public static void PickSlot(AddDraft draft, int slot)
        {
            if (draft == null) return;
            var materials = draft.renderer != null ? draft.renderer.sharedMaterials : new Material[0];
            var material = slot >= 0 && slot < materials.Length ? materials[slot] : null;
            if (PoiyomiLock.NameOn(material, draft.property) != null)
            {
                PickProperty(draft, slot, draft.property);
                return;
            }
            draft.slot = slot;
            draft.property = "";
            Refill(draft);
        }

        /// <summary>Switches between a blend shape and a material property without picking one yet.</summary>
        public static void SetKind(AddDraft draft, bool isShape)
        {
            if (draft == null || draft.isShape == isShape) return;
            draft.isShape = isShape;
            if (isShape) draft.shape = "";
            else draft.property = "";
            Refill(draft);
        }

        static void Refill(AddDraft draft)
        {
            draft.on = Now(draft);
            draft.off = Rest(draft);
            draft.name = ValueName(draft);
        }

        /// <summary>Whether the blend shape or the property the draft names is there to read.</summary>
        public static bool Picked(AddDraft draft)
        {
            if (draft == null || draft.renderer == null) return false;
            if (draft.isShape) return draft.renderer is SkinnedMeshRenderer skinned && MenuModel.ShapeExists(skinned, draft.shape);
            var material = MaterialOf(draft);
            return !string.IsNullOrEmpty(draft.property) && PoiyomiLock.NameOn(material, draft.property) != null;
        }

        static bool HasShapes(Renderer renderer) => renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null && skinned.sharedMesh.blendShapeCount > 0;

        /// <summary>Whether the window offers both: a skinned mesh with blend shapes and at least one material.</summary>
        public static bool OffersBoth(AddDraft draft) => draft != null && HasShapes(draft.renderer) && draft.renderer.sharedMaterials.Any(m => m != null);

        public static Material MaterialOf(AddDraft draft)
        {
            if (draft == null || draft.renderer == null) return null;
            var materials = draft.renderer.sharedMaterials;
            return draft.slot >= 0 && draft.slot < materials.Length ? materials[draft.slot] : null;
        }

        /// <summary>The value the blend shape or the property has in the scene right now.</summary>
        public static Vector4 Now(AddDraft draft)
        {
            if (!Picked(draft)) return Vector4.zero;
            if (draft.isShape)
            {
                var skinned = (SkinnedMeshRenderer)draft.renderer;
                return new Vector4(skinned.GetBlendShapeWeight(skinned.sharedMesh.GetBlendShapeIndex(draft.shape)), 0f, 0f, 0f);
            }
            return MenuModel.ReadProperty(MaterialOf(draft), draft.property, draft.type);
        }

        /// <summary>Where the value rests: 0 for a blend shape, the shader's own default for a property.</summary>
        public static Vector4 Rest(AddDraft draft)
        {
            if (!Picked(draft) || draft.isShape) return Vector4.zero;
            var material = MaterialOf(draft);
            int index = ShaderIndex(material, draft.property);
            if (index < 0) return Vector4.zero;
            var shader = material.shader;
            switch (shader.GetPropertyType(index))
            {
                case ShaderPropertyType.Color:
                case ShaderPropertyType.Vector:
                    return shader.GetPropertyDefaultVectorValue(index);
                default:
                    return new Vector4(shader.GetPropertyDefaultFloatValue(index), 0f, 0f, 0f);
            }
        }

        /// <summary>The slider's ends: a blend shape's frame weights, a Range property's limits. False for a plain number.</summary>
        public static bool Range(AddDraft draft, out float min, out float max)
        {
            min = 0f;
            max = 100f;
            if (draft == null) return false;
            if (draft.isShape)
            {
                if (!Picked(draft)) return true;
                var mesh = ((SkinnedMeshRenderer)draft.renderer).sharedMesh;
                int shape = mesh.GetBlendShapeIndex(draft.shape);
                for (int frame = 0; frame < mesh.GetBlendShapeFrameCount(shape); frame++)
                {
                    float weight = mesh.GetBlendShapeFrameWeight(shape, frame);
                    min = Mathf.Min(min, weight);
                    max = Mathf.Max(max, weight);
                }
                return true;
            }
            var material = MaterialOf(draft);
            int index = ShaderIndex(material, draft.property);
            if (index < 0 || material.shader.GetPropertyType(index) != ShaderPropertyType.Range) return false;
            var limits = material.shader.GetPropertyRangeLimits(index);
            min = limits.x;
            max = limits.y;
            return true;
        }

        /// <summary>Whether a colour property takes values above 1.</summary>
        public static bool IsHdr(AddDraft draft)
        {
            var material = MaterialOf(draft);
            int index = draft != null && !draft.isShape ? ShaderIndex(material, draft.property) : -1;
            return index >= 0 && (material.shader.GetPropertyFlags(index) & ShaderPropertyFlags.HDR) != 0;
        }

        /// <summary>The blend shape's name, or the property's display name without the options some shader inspectors add after "--".</summary>
        public static string ValueName(AddDraft draft)
        {
            if (draft == null) return "";
            if (draft.isShape) return draft.shape ?? "";
            var material = MaterialOf(draft);
            int index = ShaderIndex(material, draft.property);
            return index >= 0 ? CleanDisplayName(material.shader.GetPropertyDescription(index), draft.property) : draft.property ?? "";
        }

        /// <summary>A shader's display name as a name for an entry: what comes before "--{", trimmed, or the property's own name when nothing is left.</summary>
        public static string CleanDisplayName(string display, string property)
        {
            var name = display ?? "";
            int options = name.IndexOf("--{", StringComparison.Ordinal);
            if (options >= 0) name = name.Substring(0, options);
            name = name.Trim();
            return name.Length > 0 ? name : property ?? "";
        }

        static int ShaderIndex(Material material, string property)
        {
            if (material == null || material.shader == null || string.IsNullOrEmpty(property)) return -1;
            var name = PoiyomiLock.NameOn(material, property) ?? property;
            return material.shader.FindPropertyIndex(name);
        }

        static PropertyValueType TypeOf(ShaderPropertyType type)
        {
            switch (type)
            {
                case ShaderPropertyType.Color: return PropertyValueType.Color;
                case ShaderPropertyType.Vector: return PropertyValueType.Vector;
                default: return PropertyValueType.Float;
            }
        }

        /// <summary>Every AvatarDoc of the renderer's avatar, in the order they sit on it, read from the avatar's cached plan.</summary>
        public static List<AvatarDoc> Components(AddDraft draft)
        {
            var root = draft != null ? AvatarOf(draft.renderer) : null;
            if (root == null) return new List<AvatarDoc>();
            return AvatarPlanCache.Get(root).Tree.Components.Where(c => c != null).ToList();
        }

        /// <summary>The submenus a new entry of the component can go into, in list order.</summary>
        public static List<MenuEntry> Submenus(AvatarDoc component)
            => component == null ? new List<MenuEntry>() : component.entries.Where(e => e != null && e.kind == EntryKind.Submenu).ToList();

        /// <summary>Every toggle and radial of the avatar's components, each with its component and its place in that component's menu.</summary>
        public static List<(AvatarDoc component, MenuEntry entry, string path)> Targets(AddDraft draft)
        {
            var targets = new List<(AvatarDoc, MenuEntry, string)>();
            foreach (var component in Components(draft))
                foreach (var entry in component.entries)
                    if (entry != null && (entry.kind == EntryKind.Toggle || entry.kind == EntryKind.Radial))
                        targets.Add((component, entry, PathOf(component, entry)));
            return targets;
        }

        /// <summary>The submenus above an entry and its own name, joined by "/".</summary>
        public static string PathOf(AvatarDoc component, MenuEntry entry)
        {
            var parts = new List<string> { entry.PlainName };
            var seen = new HashSet<string> { entry.id };
            for (var parent = component.FindEntry(entry.parentId); parent != null && seen.Add(parent.id); parent = component.FindEntry(parent.parentId))
                parts.Insert(0, parent.PlainName);
            return string.Join("/", parts);
        }

        /// <summary>The entry Add to gives one more effect, or null when none is picked or it is gone.</summary>
        public static MenuEntry Target(AddDraft draft)
        {
            if (draft == null || draft.targetComponent == null) return null;
            var entry = draft.targetComponent.FindEntry(draft.targetId);
            return entry != null && (entry.kind == EntryKind.Toggle || entry.kind == EntryKind.Radial) ? entry : null;
        }

        /// <summary>Whether the values are a radial's two ends rather than a toggle's On and Off.</summary>
        public static bool MakesRadial(AddDraft draft)
        {
            if (draft == null) return false;
            if (draft.make == AddMake.AddTo) return Target(draft)?.kind == EntryKind.Radial;
            return draft.make == AddMake.NewRadial;
        }

        /// <summary>The materials besides the renderer's own a property curve on it reaches: the ones the entry swaps onto it, and the ones the rest of the avatar does.</summary>
        static List<Material> Swapped(AddDraft draft)
        {
            var swapped = new List<Material>();
            var target = draft.make == AddMake.AddTo ? Target(draft) : null;
            if (target != null)
                swapped.AddRange(target.materials.Where(m => m != null && m.renderer == draft.renderer && m.material != null).Select(m => m.material));
            var root = AvatarOf(draft.renderer);
            if (root != null) swapped.AddRange(AvatarPlanCache.Get(root).Tree.SwappedOnto(draft.renderer));
            return swapped;
        }

        /// <summary>The unlocked Poiyomi materials the property reaches that do not mark it: the ones Add marks Animated when its box is ticked.</summary>
        public static List<Material> Unmarked(AddDraft draft)
        {
            if (draft == null || draft.isShape || !Picked(draft)) return new List<Material>();
            return PoiyomiLock.Unmarked(draft.renderer, draft.property, Swapped(draft));
        }

        /// <summary>The locked Poiyomi materials the property reaches that fixed it at its value: only unlocking, marking and locking again helps.</summary>
        public static List<Material> Baked(AddDraft draft)
        {
            if (draft == null || draft.isShape || !Picked(draft)) return new List<Material>();
            return PoiyomiLock.Baked(draft.renderer, draft.property, Swapped(draft));
        }

        /// <summary>The name the slot's Poiyomi material animates the property under once locked, when it is marked Renamed; null otherwise.</summary>
        public static string RenamedName(AddDraft draft)
        {
            if (draft == null || draft.isShape || !Picked(draft)) return null;
            return PoiyomiLock.RenamedName(MaterialOf(draft), draft.property);
        }

        /// <summary>Adds an AvatarDoc component to the avatar's root object, in one Undo step, and makes it the draft's component.</summary>
        public static AvatarDoc AddComponent(AddDraft draft)
        {
            var root = draft != null ? AvatarOf(draft.renderer) : null;
            if (root == null) return null;
            var component = Undo.AddComponent<AvatarDoc>(root.gameObject);
            draft.component = component;
            AvatarPlanCache.InvalidateMenu(root);
            return component;
        }

        /// <summary>
        /// Makes the entry or adds the effect, in one Undo step: the row, the Poiyomi mark when ticked, and the put back
        /// when ticked. Refused, changing nothing, when the component is locked or what the draft names is gone; status
        /// then says why, and otherwise what was made. The selection is left as it is.
        /// </summary>
        public static MenuEntry Apply(AddDraft draft, out string status)
        {
            status = "";
            if (draft == null || !Picked(draft) || AvatarOf(draft.renderer) == null)
            {
                status = L.Text("ad.ui.add_from.gone");
                return null;
            }
            var entry = draft.make == AddMake.AddTo ? Target(draft) : null;
            var menu = draft.make == AddMake.AddTo ? draft.targetComponent : draft.component;
            if (draft.make == AddMake.AddTo && entry == null)
            {
                status = L.Text("ad.ui.add_from.entry.pick");
                return null;
            }
            if (menu == null)
            {
                status = string.Format(L.Text("ad.ui.add_from.no_component"), AvatarOf(draft.renderer).name);
                return null;
            }
            if (EntryEditing.Refuses(menu))
            {
                status = L.Text("ad.ui.locked.refused");
                return null;
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add to AvatarDoc");
            try
            {
                if (entry == null)
                {
                    var kind = draft.make == AddMake.NewRadial ? EntryKind.Radial : EntryKind.Toggle;
                    var name = string.IsNullOrWhiteSpace(draft.name) ? ValueName(draft) : draft.name.Trim();
                    var parent = Submenus(menu).Any(s => s.id == draft.parentId) ? draft.parentId : "";
                    entry = EntryEditing.Add(menu, kind, name, parent);
                }
                bool offIsRest = SameValue(RestAfter(draft), draft.off);
                if (draft.isShape)
                    EntryEditing.PutShape(menu, entry, (SkinnedMeshRenderer)draft.renderer, draft.shape, draft.on.x, draft.off.x, offIsRest);
                else
                {
                    EntryEditing.PutProperty(menu, entry, draft.renderer, draft.property, draft.type, draft.on, draft.off, offIsRest);
                    if (draft.markForPoiyomi) EntryEditing.MarkAnimatedForPoiyomi(menu, entry, draft.renderer, draft.property, Swapped(draft));
                }
                if (draft.putBack) PutBack(draft);
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
            }

            AvatarPlanCache.NoteSelfEdit(menu);
            AvatarPlanCache.InvalidateMenu(AvatarPlanCache.RootOf(menu));
            AvatarDoctorView.RequestReveal(menu, entry.id);
            status = draft.make == AddMake.AddTo ? string.Format(L.Text("ad.ui.add_from.done.added"), entry.PlainName)
                : string.Format(L.Text(entry.kind == EntryKind.Radial ? "ad.ui.add_from.done.radial" : "ad.ui.add_from.done.toggle"), entry.PlainName);
            // A second press then updates the row of the entry just made instead of making another one.
            if (draft.make != AddMake.AddTo)
            {
                draft.madeAs = draft.make;
                draft.make = AddMake.AddTo;
                draft.targetComponent = menu;
                draft.targetId = entry.id;
                draft.madeId = entry.id;
            }
            return entry;
        }

        /// <summary>
        /// What a toggle's off state goes back to once Add is done: the off value where it is put back, else what the
        /// scene holds. For a property, the first material on the renderer that has it, the one the build reads.
        /// </summary>
        static Vector4 RestAfter(AddDraft draft)
        {
            if (draft.isShape) return draft.putBack ? draft.off : Now(draft);
            var read = MenuModel.MaterialWithProperty(draft.renderer, draft.property);
            if (read == MaterialOf(draft)) return draft.putBack ? draft.off : Now(draft);
            return MenuModel.ReadProperty(read, draft.property, draft.type);
        }

        static bool SameValue(Vector4 a, Vector4 b) => (a - b).sqrMagnitude < 1e-8f;

        /// <summary>Sets the blend shape or the slot's material back to the Off value (a radial's lowest), with Undo.</summary>
        static void PutBack(AddDraft draft)
        {
            if (draft.isShape)
            {
                var skinned = (SkinnedMeshRenderer)draft.renderer;
                Undo.RecordObject(skinned, "Add to AvatarDoc");
                skinned.SetBlendShapeWeight(skinned.sharedMesh.GetBlendShapeIndex(draft.shape), draft.off.x);
                EntryEditing.MarkChanged(skinned);
                return;
            }
            var material = MaterialOf(draft);
            var name = PoiyomiLock.NameOn(material, draft.property) ?? draft.property;
            Undo.RecordObject(material, "Add to AvatarDoc");
            switch (draft.type)
            {
                case PropertyValueType.Color: material.SetColor(name, draft.off); break;
                case PropertyValueType.Vector: material.SetVector(name, draft.off); break;
                default: material.SetFloat(name, draft.off.x); break;
            }
            EditorUtility.SetDirty(material);
        }
    }
}
#endif
