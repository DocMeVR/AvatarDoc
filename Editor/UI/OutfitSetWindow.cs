#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>One outfit card of the wizard's working state. Serializable so the window survives a domain reload.</summary>
    [Serializable]
    public sealed class OutfitDraft
    {
        public string name = "";
        public List<GameObject> pieces = new List<GameObject>();
        public List<ShrinkDraft> shrinks = new List<ShrinkDraft>();
    }

    /// <summary>A shrink suggestion as the wizard keeps it: the suggester's own result plus whether it is ticked.</summary>
    [Serializable]
    public sealed class ShrinkDraft
    {
        public SkinnedMeshRenderer renderer;
        public string shape = "";
        public GameObject piece;
        public bool chosen;
    }

    /// <summary>The outfit set wizard: names a set, collects outfits from pieces dropped off the Hierarchy, offers shrink suggestions, and turns it all into ordinary entries through OutfitSetBuilder.</summary>
    public sealed class OutfitSetWindow : EditorWindow
    {
        /// <summary>The number of suggestions a card shows before folding the rest behind "Show all".</summary>
        const int VisibleShrinks = 8;

        // A window only keeps public or serialized fields across a domain reload, and losing these would blank it.
        // OutfitSpec/Outfit/ShrinkSuggestion (the core's own model) are plain classes with no [Serializable]
        // attribute, so the wizard's own working state lives in these dedicated serializable drafts instead and
        // is converted into an OutfitSpec only when Create() actually builds something.
        [SerializeField] AvatarDoc component;
        [SerializeField] string setName = "";
        [SerializeField] OutfitMode mode = OutfitMode.OneAtATime;
        [SerializeField] bool addNoneOption;
        [SerializeField] bool captureIcons = true;
        [SerializeField] List<OutfitDraft> outfits = new List<OutfitDraft>();
        [SerializeField] List<GameObject> hiddenPieces = new List<GameObject>();

        string lastStatus = "";

        /// <summary>What the window last told the creator: a refusal, or the name the set ended up with. Read by the tests.</summary>
        public string LastStatus => lastStatus;
        /// <summary>Several objects dropped on the still empty outfit list, waiting for the creator to choose how to split them. Not serialized: an in progress drop does not need to survive a reload.</summary>
        List<GameObject> pendingDrop;
        readonly HashSet<int> expandedShrinks = new HashSet<int>();
        /// <summary>True only once this instance has actually been shown as a real window, so a test built with CreateInstance never has Close() called on it.</summary>
        bool everShown;

        /// <summary>Opens the wizard for a component. A field, not a fixed method, so a test can swap in a fake that only calls Init, never showing a real window.</summary>
        public static Action<AvatarDoc> Open = menu =>
        {
            var window = GetWindow<OutfitSetWindow>(true, L.Text("ad.ui.outfit.title"));
            window.everShown = true;
            window.minSize = new Vector2(380f, 480f);
            window.Init(menu);
            window.Show();
        };

        /// <summary>Sets the component and resets the wizard to a blank set, then rebuilds if the GUI already exists. Tests call this after ScriptableObject.CreateInstance instead of Open, so no real window shows during a test run.</summary>
        public void Init(AvatarDoc menu)
        {
            component = menu;
            setName = "";
            mode = OutfitMode.OneAtATime;
            addNoneOption = false;
            captureIcons = true;
            outfits = new List<OutfitDraft>();
            hiddenPieces = new List<GameObject>();
            pendingDrop = null;
            expandedShrinks.Clear();
            lastStatus = "";
            if (rootVisualElement.childCount > 0) Rebuild();
        }

        /// <summary>
        /// Unity calls this once per window lifetime: on a first open, and again after a domain reload rebuilds
        /// every open window's GUI from scratch. The working state itself survived the reload in the serialized
        /// fields above, so this only has to redraw from them, not recompute anything.
        /// </summary>
        public void CreateGUI() => Rebuild();

        void OnEnable() => L.LanguageChanged += OnLanguageChanged;

        void OnDisable() => L.LanguageChanged -= OnLanguageChanged;

        void OnLanguageChanged()
        {
            titleContent = new GUIContent(L.Text("ad.ui.outfit.title"));
            if (rootVisualElement.childCount > 0) Rebuild();
        }

        public int OutfitCount => outfits.Count;
        public IReadOnlyList<GameObject> PiecesOf(int index) => index >= 0 && index < outfits.Count ? outfits[index].pieces : Array.Empty<GameObject>();
        public IReadOnlyList<ShrinkDraft> ShrinksOf(int index) => index >= 0 && index < outfits.Count ? outfits[index].shrinks : (IReadOnlyList<ShrinkDraft>)Array.Empty<ShrinkDraft>();
        public IReadOnlyList<GameObject> HiddenPieces => hiddenPieces;

        public void SetName(string value) => setName = value ?? "";

        public void SetMode(OutfitMode value)
        {
            mode = value;
            Rebuild();
        }

        public void SetAddNoneOption(bool value)
        {
            addNoneOption = value;
            Rebuild();
        }

        public void SetCaptureIcons(bool value)
        {
            captureIcons = value;
            Rebuild();
        }

        /// <summary>Adds a card; the first one added becomes the default until another card is chosen as the default.</summary>
        public void AddOutfit(string name)
        {
            outfits.Add(new OutfitDraft { name = name ?? "" });
            Rebuild();
        }

        public void RemoveOutfit(int index)
        {
            if (index < 0 || index >= outfits.Count) return;
            outfits.RemoveAt(index);
            expandedShrinks.Remove(index);
            Rebuild();
        }

        /// <summary>Renames a card without a full rebuild, so the text field the creator is typing into keeps its focus.</summary>
        public void SetOutfitName(int index, string name)
        {
            if (index < 0 || index >= outfits.Count) return;
            outfits[index].name = name ?? "";
        }

        /// <summary>Moves a card to the front of the list, which is what makes it the default outfit.</summary>
        public void SetDefaultOutfit(int index)
        {
            if (index <= 0 || index >= outfits.Count) return;
            var outfit = outfits[index];
            outfits.RemoveAt(index);
            outfits.Insert(0, outfit);
            Rebuild();
        }

        /// <summary>Adds a piece to an outfit and refreshes its shrink suggestions. Refused with a status message when the piece is not part of this avatar; ignored (not an error) when the outfit already has it.</summary>
        public bool AddPiece(int outfitIndex, GameObject piece)
        {
            if (outfitIndex < 0 || outfitIndex >= outfits.Count || piece == null) return false;
            if (!AcceptsPiece(piece))
            {
                lastStatus = L.Text("ad.ui.outfit.outside");
                Rebuild();
                return false;
            }
            var outfit = outfits[outfitIndex];
            if (!outfit.pieces.Contains(piece)) outfit.pieces.Add(piece);
            RefreshShrinks(outfitIndex);
            Rebuild();
            return true;
        }

        public void RemovePiece(int outfitIndex, GameObject piece)
        {
            if (outfitIndex < 0 || outfitIndex >= outfits.Count) return;
            outfits[outfitIndex].pieces.Remove(piece);
            RefreshShrinks(outfitIndex);
            Rebuild();
        }

        public void AddHiddenPiece(GameObject piece)
        {
            if (piece == null) return;
            if (!AcceptsPiece(piece))
            {
                lastStatus = L.Text("ad.ui.outfit.outside");
                Rebuild();
                return;
            }
            if (!hiddenPieces.Contains(piece)) hiddenPieces.Add(piece);
            Rebuild();
        }

        public void RemoveHiddenPiece(GameObject piece)
        {
            hiddenPieces.Remove(piece);
            Rebuild();
        }

        /// <summary>Ticks or clears one of an outfit's shrink suggestions, by its index in the full (not the "show first eight") list.</summary>
        public void SetShrinkChosen(int outfitIndex, int suggestionIndex, bool chosen)
        {
            if (outfitIndex < 0 || outfitIndex >= outfits.Count) return;
            var shrinks = outfits[outfitIndex].shrinks;
            if (suggestionIndex < 0 || suggestionIndex >= shrinks.Count) return;
            shrinks[suggestionIndex].chosen = chosen;
            Rebuild();
        }

        bool AcceptsPiece(GameObject go)
        {
            if (go == null || component == null) return false;
            var root = AvatarPlanCache.RootOf(component);
            return root != null && !MenuModel.IsOutsideAvatar(root, go.transform);
        }

        /// <summary>Recomputes an outfit's shrink suggestions from its current pieces, keeping the chosen state of a suggestion that is still offered (matched by renderer, shape and piece).</summary>
        void RefreshShrinks(int outfitIndex)
        {
            var outfit = outfits[outfitIndex];
            var previous = outfit.shrinks;
            var next = new List<ShrinkDraft>();
            var root = component != null ? AvatarPlanCache.RootOf(component) : null;
            if (root != null)
            {
                foreach (var piece in outfit.pieces)
                {
                    if (piece == null) continue;
                    foreach (var suggestion in ShrinkSuggester.For(root, piece))
                    {
                        bool chosen = previous.Any(p => p.chosen && p.renderer == suggestion.Renderer && p.shape == suggestion.Shape && p.piece == suggestion.Piece);
                        next.Add(new ShrinkDraft { renderer = suggestion.Renderer, shape = suggestion.Shape, piece = suggestion.Piece, chosen = chosen });
                    }
                }
            }
            outfit.shrinks = next;
        }

        /// <summary>Builds the spec from the wizard's drafts, creates the entries, captures icons, reveals the result and closes. Refused with a status message when nothing would be built (no outfit has a piece and no None option is added).</summary>
        public List<MenuEntry> Create()
        {
            if (component == null) return new List<MenuEntry>();
            if (component.locked)
            {
                lastStatus = L.Text("ad.ui.locked.refused");
                Rebuild();
                return new List<MenuEntry>();
            }
            var spec = BuildSpec();
            var created = OutfitSetBuilder.Apply(component, spec, out var adjustedName);
            if (created.Count == 0)
            {
                lastStatus = L.Text("ad.ui.outfit.empty");
                Rebuild();
                return created;
            }

            // IconBatch.CaptureMissing keeps its own Undo group (it is also the standalone "Capture missing
            // icons" action), so it cannot join OutfitSetBuilder.Apply's group without changing that contract;
            // it runs directly after instead, as its own step.
            if (captureIcons)
            {
                var root = AvatarPlanCache.RootOf(component);
                if (root != null)
                {
                    var tree = MenuModel.Build(root, null, root.GetComponentsInChildren<AvatarDoc>(true));
                    IconBatch.CaptureMissing(component, tree, root, created);
                }
            }

            AvatarPlanCache.NoteSelfEdit(component);
            AvatarPlanCache.InvalidateMenu(AvatarPlanCache.RootOf(component));
            AvatarDoctorView.RequestReveal(component, created[0].id);
            lastStatus = string.Format(L.Text("ad.ui.outfit.created"), adjustedName);
            if (everShown) Close();
            else Rebuild();
            return created;
        }

        OutfitSpec BuildSpec()
        {
            var spec = new OutfitSpec
            {
                SetName = setName ?? "",
                Mode = mode,
                AddNoneOption = mode == OutfitMode.OneAtATime && addNoneOption,
                DefaultOutfit = 0,
            };
            foreach (var draft in outfits)
            {
                var outfit = new Outfit { Name = draft.name ?? "" };
                outfit.Pieces.AddRange(draft.pieces.Where(p => p != null));
                foreach (var shrink in draft.shrinks)
                    if (shrink.renderer != null) outfit.Shrinks.Add(new ShrinkSuggestion { Renderer = shrink.renderer, Shape = shrink.shape, Piece = shrink.piece, Chosen = shrink.chosen });
                spec.Outfits.Add(outfit);
            }
            // Mix freely has no single "any outfit is on" state for a piece to hide behind (every piece has its
            // own toggle), so the builder never reads this list in that mode; kept out of the spec here too,
            // even though the pieces themselves stay in the window's own state in case the creator switches back.
            if (mode == OutfitMode.OneAtATime) spec.HiddenWhileAnyOutfit.AddRange(hiddenPieces.Where(h => h != null));
            return spec;
        }

        void Rebuild()
        {
            var root = rootVisualElement;
            root.Clear();
            root.AddToClassList("ad-root");
            root.AddToClassList(EditorGUIUtility.isProSkin ? "ad-dark" : "ad-light");
            root.AddToClassList("ad-window");
            var uss = AvatarDoctorView.LoadStyleSheet();
            if (uss != null) root.styleSheets.Add(uss);

            var intro = new Label(L.Text("ad.ui.outfit.intro"));
            intro.AddToClassList("ad-note");
            root.Add(intro);

            var nameRow = new VisualElement();
            nameRow.AddToClassList("ad-row");
            var nameField = new TextField(L.Text("ad.ui.outfit.name")) { value = setName, name = "ad-outfit-name", tooltip = L.Text("ad.ui.outfit.name.tip") };
            nameField.RegisterValueChangedCallback(e => SetName(e.newValue));
            nameRow.Add(nameField);
            root.Add(nameRow);

            var modeRow = new VisualElement();
            modeRow.AddToClassList("ad-row");
            var modeLabel = new Label(L.Text("ad.ui.outfit.mode")) { tooltip = L.Text("ad.ui.outfit.mode.tip") };
            modeLabel.AddToClassList("ad-label");
            modeRow.Add(modeLabel);
            var modeChoice = new SegmentedChoice(new List<string> { L.Text("ad.ui.outfit.mode.one"), L.Text("ad.ui.outfit.mode.mix") }, (int)mode) { name = "ad-outfit-mode", tooltip = L.Text("ad.ui.outfit.mode.tip") };
            modeChoice.Changed += index => SetMode((OutfitMode)index);
            modeRow.Add(modeChoice);
            root.Add(modeRow);

            if (mode == OutfitMode.OneAtATime)
            {
                var noneToggle = new Toggle(L.Text("ad.ui.outfit.none_option")) { value = addNoneOption, name = "ad-outfit-none", tooltip = L.Text("ad.ui.outfit.none_option.tip") };
                noneToggle.RegisterValueChangedCallback(e => SetAddNoneOption(e.newValue));
                root.Add(noneToggle);
            }

            var list = new VisualElement { name = "ad-outfit-list" };
            list.AddToClassList("ad-outfit-list");
            root.Add(list);

            if (outfits.Count == 0) list.Add(BuildEmptyDrop());
            else for (int i = 0; i < outfits.Count; i++) list.Add(BuildOutfitCard(i));

            var addOutfit = new Button(() => AddOutfit(null)) { text = L.Text("ad.ui.outfit.add"), name = "ad-outfit-add", tooltip = L.Text("ad.ui.outfit.add.tip") };
            root.Add(addOutfit);

            if (mode == OutfitMode.OneAtATime)
            {
                var hiddenTitle = new Label(L.Text("ad.ui.outfit.hidden")) { tooltip = L.Text("ad.ui.outfit.hidden.tip") };
                hiddenTitle.AddToClassList("ad-section");
                root.Add(hiddenTitle);
                root.Add(BuildHiddenSection());
            }

            var captureToggle = new Toggle(L.Text("ad.ui.outfit.capture")) { value = captureIcons, name = "ad-outfit-capture", tooltip = L.Text("ad.ui.outfit.capture.tip") };
            captureToggle.RegisterValueChangedCallback(e => SetCaptureIcons(e.newValue));
            root.Add(captureToggle);

            var createButton = new Button(() => Create()) { text = L.Text("ad.ui.outfit.create"), name = "ad-outfit-create", tooltip = L.Text("ad.ui.outfit.create.tip") };
            createButton.AddToClassList("ad-primary");
            root.Add(createButton);

            var status = new Label(lastStatus) { name = "ad-outfit-status" };
            status.AddToClassList("ad-note");
            root.Add(status);
        }

        VisualElement BuildEmptyDrop()
        {
            var container = new VisualElement();
            if (pendingDrop != null && pendingDrop.Count > 1)
            {
                var note = new Label(L.Text("ad.ui.outfit.drop"));
                note.AddToClassList("ad-note");
                container.Add(note);
                var row = new VisualElement();
                row.AddToClassList("ad-row");
                row.Add(new Button(() => ResolvePendingDrop(splitEach: true)) { text = L.Text("ad.ui.outfit.split.each"), tooltip = L.Text("ad.ui.outfit.split.each.tip") });
                row.Add(new Button(() => ResolvePendingDrop(splitEach: false)) { text = L.Text("ad.ui.outfit.split.all"), tooltip = L.Text("ad.ui.outfit.split.all.tip") });
                container.Add(row);
                return container;
            }
            container.Add(DropRow(AcceptsPiece, HandleInitialDrop, go => HandleInitialDrop(new List<GameObject> { go })));
            return container;
        }

        /// <summary>A single object dropped on the empty list becomes one outfit right away; several offer the split choice instead.</summary>
        void HandleInitialDrop(List<GameObject> objects)
        {
            var valid = objects.Where(AcceptsPiece).ToList();
            if (valid.Count == 0) return;
            if (valid.Count == 1)
            {
                AddOutfit(valid[0].name);
                AddPiece(0, valid[0]);
                return;
            }
            pendingDrop = valid;
            Rebuild();
        }

        void ResolvePendingDrop(bool splitEach)
        {
            var objects = pendingDrop;
            pendingDrop = null;
            if (objects == null) return;
            if (splitEach)
            {
                foreach (var go in objects)
                {
                    var draft = new OutfitDraft { name = go.name };
                    draft.pieces.Add(go);
                    outfits.Add(draft);
                    RefreshShrinks(outfits.Count - 1);
                }
            }
            else
            {
                var draft = new OutfitDraft { name = objects[0].name };
                draft.pieces.AddRange(objects);
                outfits.Add(draft);
                RefreshShrinks(outfits.Count - 1);
            }
            Rebuild();
        }

        VisualElement BuildOutfitCard(int index)
        {
            var outfit = outfits[index];
            var card = new VisualElement();
            card.AddToClassList("ad-panel");

            var title = new VisualElement();
            title.AddToClassList("ad-panel-title");
            var nameField = new TextField { value = outfit.name, tooltip = L.Text("ad.ui.outfit.card_name.tip") };
            nameField.AddToClassList("ad-grow");
            nameField.RegisterValueChangedCallback(e => SetOutfitName(index, e.newValue));
            title.Add(nameField);
            var defaultButton = new Label(L.Text("ad.ui.outfit.default")) { tooltip = L.Text("ad.ui.outfit.default.tip") };
            defaultButton.AddToClassList("ad-chip");
            defaultButton.AddToClassList("ad-outfit-default");
            defaultButton.EnableInClassList("ad-chip-on", index == 0);
            defaultButton.RegisterCallback<ClickEvent>(_ => SetDefaultOutfit(index));
            title.Add(defaultButton);
            var remove = new Button(() => RemoveOutfit(index)) { text = "×" };
            remove.AddToClassList("ad-w-24");
            title.Add(remove);
            card.Add(title);

            var body = new VisualElement();
            body.AddToClassList("ad-panel-body");

            var pieces = new VisualElement();
            pieces.AddToClassList("ad-outfit-pieces");
            foreach (var piece in outfit.pieces)
                if (piece != null) pieces.Add(PieceChip(piece.name, piece.name, () => RemovePiece(index, piece)));
            body.Add(pieces);

            body.Add(DropRow(AcceptsPiece, objects => { foreach (var go in objects) AddPiece(index, go); }, go => AddPiece(index, go)));

            if (outfit.shrinks.Count > 0) body.Add(BuildShrinks(index));

            card.Add(body);
            return card;
        }

        VisualElement BuildShrinks(int outfitIndex)
        {
            var shrinks = outfits[outfitIndex].shrinks;
            var container = new VisualElement();
            container.AddToClassList("ad-outfit-shrinks");
            var title = new Label(L.Text("ad.ui.outfit.shrinks")) { tooltip = L.Text("ad.ui.outfit.shrinks.tip") };
            title.AddToClassList("ad-section");
            container.Add(title);

            bool expanded = expandedShrinks.Contains(outfitIndex);
            int shown = expanded ? shrinks.Count : Mathf.Min(shrinks.Count, VisibleShrinks);
            for (int i = 0; i < shown; i++)
            {
                var shrink = shrinks[i];
                int captured = i;
                string rendererName = shrink.renderer != null ? shrink.renderer.name : "?";
                var box = new Toggle(L.Labelled(rendererName, shrink.shape)) { value = shrink.chosen, tooltip = L.Text("ad.ui.outfit.shrink_toggle.tip") };
                box.RegisterValueChangedCallback(e => SetShrinkChosen(outfitIndex, captured, e.newValue));
                container.Add(box);
            }
            if (!expanded && shrinks.Count > VisibleShrinks)
            {
                var more = new Button(() => { expandedShrinks.Add(outfitIndex); Rebuild(); }) { text = string.Format(L.Text("ad.ui.outfit.show_all"), shrinks.Count), tooltip = L.Text("ad.ui.outfit.show_all.tip") };
                container.Add(more);
            }
            return container;
        }

        VisualElement BuildHiddenSection()
        {
            var container = new VisualElement { name = "ad-outfit-hidden" };
            var chips = new VisualElement();
            chips.AddToClassList("ad-outfit-pieces");
            foreach (var piece in hiddenPieces)
                if (piece != null) chips.Add(PieceChip(piece.name, piece.name, () => RemoveHiddenPiece(piece)));
            container.Add(chips);

            container.Add(DropRow(AcceptsPiece, objects => { foreach (var go in objects) AddHiddenPiece(go); }, AddHiddenPiece));
            return container;
        }

        /// <summary>A drop zone with an "Add piece" button beside it, for a picker scoped to the avatar rather than a drag from the Hierarchy.</summary>
        VisualElement DropRow(Func<GameObject, bool> accepts, Action<List<GameObject>> onDrop, Action<GameObject> onPick)
        {
            var row = new VisualElement();
            row.AddToClassList("ad-row");
            var drop = new Label(L.Text("ad.ui.outfit.drop"));
            drop.AddToClassList("ad-drop-hint");
            drop.AddToClassList("ad-grow");
            drop.AddManipulator(new HierarchyDropManipulator(drop, accepts, onDrop));
            row.Add(drop);
            var add = new Button { text = L.Text("ad.ui.outfit.add_piece"), name = "ad-outfit-add-piece", tooltip = L.Text("ad.ui.outfit.add_piece.tip") };
            var root = component != null ? AvatarPlanCache.RootOf(component) : null;
            add.clicked += () => AvatarObjectPicker.Show(add, root, PickKind.GameObject, picked => { if (picked is GameObject go) onPick(go); });
            add.SetEnabled(root != null);
            row.Add(add);
            return row;
        }

        /// <summary>A removable chip: the label elides a long name rather than pushing the window wider, and shows the full name as a tooltip.</summary>
        static VisualElement PieceChip(string label, string tooltip, Action onRemove)
        {
            var chip = new VisualElement();
            chip.AddToClassList("ad-chip");
            chip.AddToClassList("ad-piece-chip");
            var text = new Label(label) { tooltip = tooltip };
            text.AddToClassList("ad-piece-chip-label");
            chip.Add(text);
            var remove = new Button(onRemove) { text = "×" };
            remove.AddToClassList("ad-piece-chip-remove");
            chip.Add(remove);
            return chip;
        }
    }
}
#endif
