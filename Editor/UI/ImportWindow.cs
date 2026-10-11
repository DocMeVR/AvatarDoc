#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Lists what VRCFury, Modular Avatar and hand made animator layers would install on the avatar, and converts the ticked ones into entries of one AvatarDoc.</summary>
    public sealed class ImportWindow : EditorWindow
    {
        // A window only keeps public or serialized fields across a domain reload, and losing this would blank it.
        [SerializeField] AvatarDoc component;
        List<ImportCandidate> candidates = new List<ImportCandidate>();
        List<bool> checkedFlags = new List<bool>();
        bool removeOriginals = true;
        string lastStatus = "";
        // What the last scan was made from. A change that leaves both as they were only changed how things read.
        int scannedStructure = -1;
        int scannedPlanVersion = -1;
        bool rescanPending;
        bool rescanForced;

        Button importButton;
        Label status;

        /// <summary>The width the window opens at: its smallest size, which the docs picture shows too.</summary>
        public const float OpeningWidth = 360f;
        const float OpeningHeight = 420f;

        public static void Open(AvatarDoc menu)
        {
            var window = GetWindow<ImportWindow>(true, L.Text("ad.ui.import"));
            window.Init(menu);
            window.minSize = new Vector2(OpeningWidth, OpeningHeight);
            window.Show();
        }

        /// <summary>Sets the component to scan and rebuilds. Tests call this after ScriptableObject.CreateInstance instead of Open, so no real window shows during a test run.</summary>
        public void Init(AvatarDoc menu)
        {
            // The same component again (Convert pressed while the window is open) keeps what the creator ticked; a
            // different one to convert into starts fresh.
            if (menu != component)
            {
                candidates = new List<ImportCandidate>();
                checkedFlags = new List<bool>();
            }
            component = menu;
            lastStatus = "";
            Rescan();
            if (rootVisualElement.childCount > 0) Rebuild();
        }

        /// <summary>
        /// Unity calls this once per window lifetime: on a first open, and again after a domain reload rebuilds
        /// every open window's GUI from scratch. A reload keeps the serialized component but not the plain
        /// candidate list, so this rescans whenever a component is already known rather than trusting whatever
        /// the list happens to hold. Public so a test can call it directly instead of forcing a real reload.
        /// </summary>
        public void CreateGUI()
        {
            if (component != null) Rescan();
            Rebuild();
        }

        void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndo;
            AvatarPlanCache.Changed += OnCacheChanged;
            L.LanguageChanged += OnLanguageChanged;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndo;
            AvatarPlanCache.Changed -= OnCacheChanged;
            L.LanguageChanged -= OnLanguageChanged;
        }

        /// <summary>The title, the notes and every candidate's reason were made in the old language, so the candidates are read again (keeping the ticks) and the panel is built again.</summary>
        void OnLanguageChanged()
        {
            titleContent = new GUIContent(L.Text("ad.ui.import"));
            if (rootVisualElement.childCount == 0) return;
            if (component != null) Rescan();
            Rebuild();
        }

        void OnCacheChanged(Transform _) => QueueRescan(force: false);

        void OnUndo() => QueueRescan(force: true);

        /// <summary>
        /// Something outside this window changed the avatar (an undo, an edit in the inspector), so the list it
        /// shows may no longer match the scene. The rescan waits for the next frame: however many changes arrive
        /// before then, such as one per pointer move while a contact handle is dragged, one rescan runs, and an
        /// exception inside it cannot take the edit that raised the change down with it.
        /// </summary>
        void QueueRescan(bool force)
        {
            if (component == null || rootVisualElement.childCount == 0) return;
            rescanForced |= force;
            if (rescanPending) return;
            rescanPending = true;
            rootVisualElement.schedule.Execute(RunPendingRescan);
        }

        /// <summary>
        /// Runs the rescan a change asked for, if one is waiting and the avatar's plan really changed since the
        /// last scan: a change that only moved a name or an icon leaves the candidates as they are. The window's
        /// scheduler calls it once per frame; public so a test can call it without waiting for one.
        /// </summary>
        public void RunPendingRescan()
        {
            if (!rescanPending) return;
            rescanPending = false;
            if (component == null) return;
            bool planMoved = AvatarPlanCache.StructureVersion != scannedStructure
                || AvatarPlanCache.VersionFor(AvatarPlanCache.RootOf(component)) != scannedPlanVersion;
            if (!rescanForced && !planMoved) return;
            Rescan();
            Rebuild();
        }

        public IReadOnlyList<ImportCandidate> Candidates => candidates;

        void Rescan()
        {
            var remembered = Remembered();
            var plan = component != null ? AvatarPlanCache.GetFor(component) : null;
            candidates = plan != null && plan.AvatarRoot != null ? ImportScanner.Scan(plan.AvatarRoot, plan.Tree) : new List<ImportCandidate>();
            checkedFlags = candidates.Select(c => c.Supported).ToList();
            // The scan hands out new candidates every time, so what the creator ticked, and what they asked to keep
            // as it is, comes back by the key a candidate keeps across scans. A row never seen before starts as usual.
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!remembered.TryGetValue(KeyOf(candidates[i]), out var before)) continue;
                checkedFlags[i] = candidates[i].Supported && before.ticked;
                candidates[i].KeepAsItIs = before.keep;
            }
            scannedStructure = AvatarPlanCache.StructureVersion;
            scannedPlanVersion = AvatarPlanCache.VersionFor(plan != null ? plan.AvatarRoot : null);
            rescanForced = false;
        }

        Dictionary<string, (bool ticked, bool keep)> Remembered()
        {
            var result = new Dictionary<string, (bool ticked, bool keep)>();
            for (int i = 0; i < candidates.Count && i < checkedFlags.Count; i++)
                result[KeyOf(candidates[i])] = (checkedFlags[i], candidates[i].KeepAsItIs);
            return result;
        }

        /// <summary>Names a candidate across scans by where it comes from: its source, the components it would remove, the layer and control it replaces, its place in the menu and its name.</summary>
        static string KeyOf(ImportCandidate c)
        {
            var origins = c.OriginComponents == null ? ""
                : string.Join(",", c.OriginComponents.Where(o => o != null).Select(o => o.GetInstanceID()).OrderBy(id => id));
            var what = c.ShapesDraft != null && c.ShapesDraft.Piece != null ? "piece" + c.ShapesDraft.Piece.GetInstanceID()
                : c.MenuAssetDraft != null && c.MenuAssetDraft.menu != null ? "asset" + c.MenuAssetDraft.menu.GetInstanceID()
                : c.MergeDraft != null && c.MergeDraft.controller != null ? "merge" + c.MergeDraft.controller.GetInstanceID()
                : c.Draft != null ? c.Draft.PlainName : "";
            return string.Join("|", c.Source, origins, c.OriginLayer ?? "", string.Join("/", c.OriginControlPath ?? new List<string>()),
                c.OriginOccurrence, string.Join("/", c.Path ?? new List<string>()), c.Group ?? "", what);
        }

        /// <summary>Ticks or unticks the row at index, and every other candidate of the same group with it: a group stands or falls together, so it ticks and unticks as one.</summary>
        public void SetChecked(int index, bool value)
        {
            if (index < 0 || index >= checkedFlags.Count || !candidates[index].Supported) return;
            checkedFlags[index] = value;
            var group = candidates[index].GroupMembers;
            if (group != null)
                foreach (var member in group)
                {
                    int i = candidates.IndexOf(member);
                    if (i >= 0 && candidates[i].Supported) checkedFlags[i] = value;
                }
            Rebuild();
        }

        /// <summary>Whether the row at this index is ticked. Public for tests; the row's own checkbox reads the same flag.</summary>
        public bool IsChecked(int index) => index >= 0 && index < checkedFlags.Count && checkedFlags[index];

        public void SetRemoveOriginals(bool value)
        {
            removeOriginals = value;
            Rebuild();
        }

        /// <summary>True when a ticked candidate belongs to a group and originals would not be removed: applying would merge the group's controller a second time, exactly what ad.import.reason.group_needs_removal already refuses at apply time. Shown ahead of time instead of letting the apply fail silently.</summary>
        bool NeedsGroupRemovalWarning()
        {
            if (removeOriginals) return false;
            for (int i = 0; i < candidates.Count; i++)
                if (checkedFlags[i] && !string.IsNullOrEmpty(candidates[i].Group)) return true;
            return false;
        }

        /// <summary>Creates entries for every ticked candidate, removing the originals when asked, then rescans and rebuilds. Returns the created entries. Refuses and rescans instead when a candidate's origin component has been destroyed since the scan (an Undo, a scene edit), since the whole list may no longer match the scene.</summary>
        public List<MenuEntry> RunImport()
        {
            if (component == null) return new List<MenuEntry>();
            if (component.locked)
            {
                lastStatus = L.Text("ad.ui.locked.refused");
                Rebuild();
                return new List<MenuEntry>();
            }
            if (candidates.Any(c => c.OriginComponents != null && c.OriginComponents.Any(o => o == null)))
            {
                lastStatus = L.Text("ad.ui.import.rescanned");
                Rescan();
                Rebuild();
                return new List<MenuEntry>();
            }
            var chosen = candidates.Where((c, i) => checkedFlags[i] && c.Supported).ToList();
            var created = ImportApply.Apply(component, chosen, removeOriginals);
            if (created.Count > 0)
            {
                AvatarPlanCache.NoteSelfEdit(component);
                AvatarPlanCache.InvalidateMenu(AvatarPlanCache.RootOf(component));
                AvatarDoctorView.RequestReveal(component, created[0].id);
            }
            var blocked = chosen.FirstOrDefault(c => !c.Supported);
            lastStatus = blocked != null ? blocked.Reason
                : created.Count > 0 ? string.Format(L.Text("ad.ui.import.done"), created.Count) : "";
            Rescan();
            Rebuild();
            return created;
        }

        /// <summary>Undoes an FX layer import by its row in the "layers left out of the build" section.</summary>
        public void RestoreLayer(int index)
        {
            if (component == null || index < 0 || index >= component.removedLayers.Count) return;
            if (component.locked)
            {
                lastStatus = L.Text("ad.ui.locked.refused");
                Rebuild();
                return;
            }
            if (ImportApply.RestoreLayer(component, component.removedLayers[index].layer)) AvatarPlanCache.NoteSelfEdit(component);
            AvatarPlanCache.InvalidateMenu(AvatarPlanCache.RootOf(component));
            Rescan();
            Rebuild();
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

            var intro = new Label(L.Text("ad.ui.import.intro"));
            intro.AddToClassList("ad-note");
            root.Add(intro);

            var header = new VisualElement();
            header.AddToClassList("ad-row");
            header.Add(new Button(SelectAll) { text = L.Text("ad.ui.import.select_all"), name = "ad-import-all", tooltip = L.Text("ad.ui.import.select_all.tip") });
            header.Add(new Button(SelectNone) { text = L.Text("ad.ui.import.select_none"), name = "ad-import-none", tooltip = L.Text("ad.ui.import.select_none.tip") });
            root.Add(header);

            var list = new ScrollView();
            list.AddToClassList("ad-import-list");
            root.Add(list);

            if (candidates.Count == 0)
            {
                var none = new Label(L.Text("ad.ui.import.none"));
                none.AddToClassList("ad-note");
                list.Add(none);
            }
            else
            {
                foreach (var group in Groups())
                {
                    if (group.indices.Count == 0) continue;
                    var caption = new Label(group.caption);
                    caption.AddToClassList("ad-panel-title");
                    list.Add(caption);
                    foreach (var index in group.indices) list.Add(CandidateRow(index));
                }
            }

            var removeToggle = new Toggle(L.Text("ad.ui.import.remove")) { value = removeOriginals, tooltip = L.Text("ad.ui.import.remove.tip"), name = "ad-import-remove" };
            removeToggle.RegisterValueChangedCallback(e => SetRemoveOriginals(e.newValue));
            root.Add(removeToggle);

            if (NeedsGroupRemovalWarning())
            {
                var warning = new VisualElement { name = "ad-import-group-warning" };
                warning.AddToClassList("ad-warnbox");
                warning.Add(UiText.MessageLabel(L.Text("ad.import.reason.group_needs_removal")));
                root.Add(warning);
            }

            importButton = new Button(() => RunImport()) { name = "ad-import-run", tooltip = L.Text("ad.ui.import.button.tip") };
            importButton.AddToClassList("ad-primary");
            root.Add(importButton);

            status = new Label(lastStatus);
            status.AddToClassList("ad-note");
            root.Add(status);

            if (component != null && component.removedLayers.Count > 0)
            {
                var title = new Label(L.Text("ad.ui.import.layers"));
                title.AddToClassList("ad-panel-title");
                root.Add(title);
                for (int i = 0; i < component.removedLayers.Count; i++)
                {
                    int index = i;
                    var row = new VisualElement();
                    row.AddToClassList("ad-row");
                    var name = new Label(component.removedLayers[i].layer);
                    name.AddToClassList("ad-grow");
                    row.Add(name);
                    row.Add(new Button(() => RestoreLayer(index)) { text = L.Text("ad.ui.import.restore"), tooltip = L.Text("ad.ui.import.restore.tip") });
                    root.Add(row);
                }
            }

            LanguageFont.ApplyToChildren(root);
            RefreshFooter();
        }

        IEnumerable<(string caption, List<int> indices)> Groups()
        {
            yield return (L.Text("ad.ui.chip.vrcfury"), IndicesOf(ImportSource.VRCFury));
            yield return (L.Text("ad.ui.chip.modular_avatar"), IndicesOf(ImportSource.ModularAvatar));
            yield return (L.Text("ad.ui.import.group.menu_asset"), IndicesOf(ImportSource.MenuAsset));
            yield return (L.Text("ad.ui.import.group.animator"), IndicesOf(ImportSource.MergeAnimator));
            yield return (L.Text("ad.ui.import.group.shapes"), IndicesOf(ImportSource.Shapes));
            yield return (L.Text("ad.ui.import.group.fx"), IndicesOf(ImportSource.FxLayer));
        }

        List<int> IndicesOf(ImportSource source) => Enumerable.Range(0, candidates.Count).Where(i => candidates[i].Source == source).ToList();

        VisualElement CandidateRow(int index)
        {
            var candidate = candidates[index];
            var row = new VisualElement();
            row.AddToClassList("ad-import-row");
            row.EnableInClassList("ad-unsupported", !candidate.Supported);

            var header = new VisualElement();
            header.AddToClassList("ad-row");

            if (candidate.Supported)
            {
                var toggle = new Toggle { value = checkedFlags[index], tooltip = L.Text("ad.ui.import.candidate.tip") };
                toggle.AddToClassList("ad-w-24");
                toggle.RegisterValueChangedCallback(e => SetChecked(index, e.newValue));
                header.Add(toggle);
            }
            else
            {
                var placeholder = new VisualElement();
                placeholder.AddToClassList("ad-w-24");
                header.Add(placeholder);
            }

            var pathBox = new VisualElement();
            pathBox.AddToClassList("ad-import-path");
            if (candidate.Path.Count > 0)
            {
                var parents = new Label(string.Join(" / ", candidate.Path) + " / ");
                parents.AddToClassList("ad-note");
                pathBox.Add(parents);
            }
            var name = new Label(RowName(candidate));
            name.style.flexShrink = 0;
            pathBox.Add(name);
            header.Add(pathBox);

            var kind = new Label(KindLabel(candidate));
            kind.AddToClassList("ad-import-meta");
            header.Add(kind);

            var summary = new Label(Summary(candidate));
            summary.AddToClassList("ad-import-meta");
            header.Add(summary);

            row.Add(header);

            if (candidate.Supported && candidate.MenuAssetDraft != null)
            {
                var keep = new Toggle(L.Text("ad.ui.import.keep")) { value = candidate.KeepAsItIs, tooltip = L.Text("ad.ui.import.keep.tip"), name = "ad-import-keep-" + index };
                keep.RegisterValueChangedCallback(e => SetKeepAsItIs(candidate, e.newValue));
                keep.style.marginLeft = 28;
                row.Add(keep);
            }

            var noteText = candidate.Supported ? string.Join(" ", candidate.Notes) : candidate.Reason ?? "";
            if (noteText.Length > 0)
            {
                var note = new Label(noteText);
                note.AddToClassList("ad-note");
                note.style.marginLeft = 28;
                row.Add(note);
            }

            return row;
        }

        static string RowName(ImportCandidate candidate)
        {
            if (candidate.ShapesDraft != null) return candidate.ShapesDraft.Piece != null ? candidate.ShapesDraft.Piece.name : "";
            if (candidate.MenuAssetDraft != null) return candidate.MenuAssetDraft.menu != null ? candidate.MenuAssetDraft.menu.name : "";
            if (candidate.Draft != null) return candidate.Draft.PlainName;
            var controller = candidate.MergeDraft != null ? candidate.MergeDraft.controller : null;
            return controller != null ? controller.name : "";
        }

        static string KindLabel(ImportCandidate candidate)
        {
            if (candidate.ShapesDraft != null) return L.Text("ad.ui.import.kind.shapes");
            if (candidate.MenuAssetDraft != null) return L.Text("ad.ui.import.kind.menu_asset");
            if (candidate.MergeDraft != null) return L.Text("ad.ui.import.kind.animator");
            return candidate.Draft != null && candidate.Draft.kind == EntryKind.Radial ? L.Text("ad.ui.detail.radial") : L.Text("ad.ui.detail.toggle");
        }

        /// <summary>The counts a candidate's row shows at its right: what it changes, or what a takeover or a merge keeps.</summary>
        public static string Summary(ImportCandidate candidate)
        {
            if (candidate.ShapesDraft != null)
                return ShapesModel.RowCounts(candidate.ShapesDraft.Follow.Count, candidate.ShapesDraft.WhileOn.Count);
            if (candidate.MenuAssetDraft != null)
            {
                int parameters = MenuAssetPanel.ParametersOf(candidate.MenuAssetDraft.menu).Count;
                return L.JoinList(new[]
                {
                    UiText.Count(parameters, "ad.ui.import.summary.parameters.one", "ad.ui.import.summary.parameters"),
                    UiText.Count(candidate.ParameterDrafts.Count, "ad.ui.import.summary.declarations.one", "ad.ui.import.summary.declarations"),
                });
            }
            if (candidate.MergeDraft != null)
            {
                int merged = Mathf.Max(MergeBuilder.LayerNames(candidate.MergeDraft.controller).Count - candidate.MergeDraft.skipLayers.Count, 0);
                return UiText.Count(merged, "ad.ui.import.summary.merged_layers.one", "ad.ui.import.summary.merged_layers");
            }
            var draft = candidate.Draft;
            if (draft == null) return "";
            var parts = new List<string>();
            if (draft.objects.Count > 0) parts.Add(UiText.Count(draft.objects.Count, "ad.ui.import.summary.objects.one", "ad.ui.import.summary.objects"));
            if (draft.blendShapes.Count > 0) parts.Add(UiText.Count(draft.blendShapes.Count, "ad.ui.import.summary.blendshapes.one", "ad.ui.import.summary.blendshapes"));
            if (draft.materials.Count > 0) parts.Add(UiText.Count(draft.materials.Count, "ad.ui.import.summary.materials.one", "ad.ui.import.summary.materials"));
            if (draft.properties.Count > 0) parts.Add(UiText.Count(draft.properties.Count, "ad.ui.import.summary.properties.one", "ad.ui.import.summary.properties"));
            if (draft.animations.Count > 0) parts.Add(UiText.Count(draft.animations.Count, "ad.ui.import.summary.animations.one", "ad.ui.import.summary.animations"));
            return L.JoinList(parts);
        }

        /// <summary>Sets Keep as it is on every candidate of this group: the menu, the declarations and the merged animator are one decision.</summary>
        void SetKeepAsItIs(ImportCandidate candidate, bool value)
        {
            candidate.KeepAsItIs = value;
            if (candidate.GroupMembers != null)
                foreach (var member in candidate.GroupMembers) member.KeepAsItIs = value;
        }

        void SelectAll()
        {
            for (int i = 0; i < checkedFlags.Count; i++) checkedFlags[i] = candidates[i].Supported;
            Rebuild();
        }

        void SelectNone()
        {
            for (int i = 0; i < checkedFlags.Count; i++) checkedFlags[i] = false;
            Rebuild();
        }

        void RefreshFooter()
        {
            if (importButton == null) return;
            int count = checkedFlags.Count(c => c);
            importButton.text = count == 1 ? L.Text("ad.ui.import.button.one") : string.Format(L.Text("ad.ui.import.button"), count);
            importButton.SetEnabled(count > 0);
        }
    }
}
#endif
