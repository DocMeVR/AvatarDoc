#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Turns an outfit spec into ordinary AvatarDoc entries in one Undo step. Adds no runtime data of its own.</summary>
    public static class OutfitSetBuilder
    {
        /// <summary>Creates the submenu, toggles and (in Mix freely) presets for a spec. Returns the created entries, submenu first, or an empty list when there is nothing to build.</summary>
        public static List<MenuEntry> Apply(AvatarDoc menu, OutfitSpec spec) => Apply(menu, spec, out _);

        /// <param name="adjustedName">The name the set ended up with: its exclusive group in One at a time, the submenu's own name in Mix freely. Empty when nothing was built.</param>
        public static List<MenuEntry> Apply(AvatarDoc menu, OutfitSpec spec, out string adjustedName)
        {
            adjustedName = "";
            var created = new List<MenuEntry>();
            if (EntryEditing.Refuses(menu) || spec == null || spec.Outfits == null || spec.Outfits.Count == 0) return created;

            var nonEmpty = new List<(int Index, Outfit Outfit)>();
            for (int i = 0; i < spec.Outfits.Count; i++)
            {
                var outfit = spec.Outfits[i];
                if (outfit?.Pieces != null && outfit.Pieces.Any(p => p != null)) nonEmpty.Add((i, outfit));
            }

            bool addsNone = spec.Mode == OutfitMode.OneAtATime && spec.AddNoneOption;
            if (nonEmpty.Count == 0 && !addsNone) return created;

            // A set name that is empty, styled or already taken must not reach the exclusive group: an empty
            // group is no group at all, tags would land in the group's parameter name, and a repeated name would
            // join a set that is already there.
            var plain = RichName.Strip(spec.SetName ?? "").Trim();
            bool named = plain.Length > 0;
            if (!named) plain = L.Text("ad.outfit.default_name");
            var groupName = UniqueGroupName(menu, plain);
            var submenuName = named ? spec.SetName.Trim() : plain;
            adjustedName = spec.Mode == OutfitMode.OneAtATime ? groupName : submenuName;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create outfit set");
            Undo.RecordObject(menu, "Create outfit set");
            try
            {
                var submenu = menu.AddEntry(EntryKind.Submenu, submenuName, "");
                created.Add(submenu);

                if (spec.Mode == OutfitMode.OneAtATime) BuildOneAtATime(menu, spec, submenu, nonEmpty, created, groupName);
                else BuildMixFreely(menu, spec, submenu, nonEmpty, created);
            }
            finally
            {
                EntryEditing.MarkChanged(menu);
                Undo.CollapseUndoOperations(group);
            }
            return created;
        }

        /// <summary>A group name nothing on the avatar uses yet: the plain set name, else the same with " 2", " 3" and so on.</summary>
        static string UniqueGroupName(AvatarDoc menu, string setName)
        {
            var taken = new HashSet<string>();
            foreach (var component in ComponentsOf(menu))
            {
                foreach (var existing in component.groups)
                    if (!string.IsNullOrWhiteSpace(existing.name)) taken.Add(existing.name.Trim());
                foreach (var entry in component.entries)
                    if (!string.IsNullOrWhiteSpace(entry.exclusiveGroup)) taken.Add(entry.exclusiveGroup.Trim());
            }
            var candidate = setName;
            for (int i = 2; taken.Contains(candidate); i++) candidate = setName + " " + i;
            return candidate;
        }

        static IEnumerable<AvatarDoc> ComponentsOf(AvatarDoc menu)
        {
            var root = AvatarPlanCache.RootOf(menu);
            return root != null ? (IEnumerable<AvatarDoc>)root.GetComponentsInChildren<AvatarDoc>(true) : new[] { menu };
        }

        static void BuildOneAtATime(AvatarDoc menu, OutfitSpec spec, MenuEntry submenu, List<(int Index, Outfit Outfit)> outfits, List<MenuEntry> created, string group)
        {
            foreach (var (index, outfit) in outfits)
            {
                var toggle = menu.AddEntry(EntryKind.Toggle, outfit.Name, submenu.id);
                toggle.exclusiveGroup = group;
                toggle.defaultOn = index == spec.DefaultOutfit;
                foreach (var piece in outfit.Pieces)
                    if (piece != null) toggle.objects.Add(new ObjectTarget { target = piece, activeWhenOn = true });
                if (spec.HiddenWhileAnyOutfit != null)
                    foreach (var hidden in spec.HiddenWhileAnyOutfit)
                        if (hidden != null) toggle.objects.Add(new ObjectTarget { target = hidden, activeWhenOn = false });
                AddChosenShrinks(toggle, outfit.Shrinks);
                created.Add(toggle);
            }

            if (spec.AddNoneOption)
            {
                var none = menu.AddEntry(EntryKind.Toggle, L.Text("ad.outfit.none"), submenu.id);
                none.exclusiveGroup = group;
                // Nothing else is default when the requested default does not land on a built outfit, so the
                // toggle that is actually on by default lives in the data rather than being implied by absence.
                none.defaultOn = !outfits.Any(o => o.Index == spec.DefaultOutfit);
                created.Add(none);
            }
        }

        static void BuildMixFreely(AvatarDoc menu, OutfitSpec spec, MenuEntry submenu, List<(int Index, Outfit Outfit)> outfits, List<MenuEntry> created)
        {
            // A piece that appears in more than one outfit gets one toggle: the dictionary keeps the first one.
            var order = new List<GameObject>();
            var toggleOf = new Dictionary<GameObject, MenuEntry>();
            foreach (var (_, outfit) in outfits)
                foreach (var piece in outfit.Pieces)
                    if (piece != null && !toggleOf.ContainsKey(piece))
                    {
                        order.Add(piece);
                        toggleOf[piece] = null;
                    }

            var defaultPieces = new HashSet<GameObject>();
            var defaultOutfit = outfits.FirstOrDefault(o => o.Index == spec.DefaultOutfit).Outfit;
            if (defaultOutfit != null)
                foreach (var piece in defaultOutfit.Pieces)
                    if (piece != null) defaultPieces.Add(piece);

            foreach (var piece in order)
            {
                var toggle = menu.AddEntry(EntryKind.Toggle, piece.name, submenu.id);
                toggle.objects.Add(new ObjectTarget { target = piece, activeWhenOn = true });
                toggle.defaultOn = defaultPieces.Contains(piece);
                toggleOf[piece] = toggle;
                created.Add(toggle);
            }

            foreach (var piece in order)
            {
                var toggle = toggleOf[piece];
                var seen = new HashSet<(SkinnedMeshRenderer, string)>();
                foreach (var (_, outfit) in outfits)
                    if (outfit.Shrinks != null)
                        foreach (var shrink in outfit.Shrinks)
                            if (shrink != null && shrink.Chosen && shrink.Piece == piece && shrink.Renderer != null && seen.Add((shrink.Renderer, shrink.Shape)))
                                toggle.blendShapes.Add(new BlendShapeTarget { renderer = shrink.Renderer, shape = shrink.Shape, weight = shrink.Weight });
            }

            foreach (var (_, outfit) in outfits)
            {
                var preset = menu.AddEntry(EntryKind.Preset, outfit.Name, submenu.id);
                var pieces = new HashSet<GameObject>(outfit.Pieces.Where(p => p != null));
                foreach (var piece in order)
                    preset.presetStates.Add(new PresetState { entryId = toggleOf[piece].id, on = pieces.Contains(piece) });
                created.Add(preset);
            }

            var off = menu.AddEntry(EntryKind.Preset, L.Text("ad.outfit.everything_off"), submenu.id);
            foreach (var piece in order)
                off.presetStates.Add(new PresetState { entryId = toggleOf[piece].id, on = false });
            created.Add(off);
        }

        static void AddChosenShrinks(MenuEntry toggle, List<ShrinkSuggestion> shrinks)
        {
            if (shrinks == null) return;
            foreach (var shrink in shrinks)
                if (shrink != null && shrink.Chosen && shrink.Renderer != null)
                    toggle.blendShapes.Add(new BlendShapeTarget { renderer = shrink.Renderer, shape = shrink.Shape, weight = shrink.Weight });
        }
    }
}
#endif
