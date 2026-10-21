#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The window Copy to another avatar opens: the component and the avatar it goes to, the object of that avatar it
    /// goes on and what happens to a component already there, the objects marked Pick by hand when copying and the
    /// ones it could not find there, each with a field to pick it, the shapes, slots and properties that avatar lacks,
    /// and Copy. It never changes the selection.
    /// </summary>
    public sealed class CopyMenuWindow : EditorWindow
    {
        /// <summary>The width the window opens at, which the docs picture shows too.</summary>
        public const float OpeningWidth = 360f;
        const float OpeningHeight = 480f;

        /// <summary>How a list opens under its button. Replaceable by a test, which reads the items instead of showing them.</summary>
        public static Action<VisualElement, List<AdMenu.Item>> ShowMenu = (anchor, items) => AdMenu.Open(anchor.worldBound, anchor, items, search: items.Count > 8);

        // A window keeps only serialized fields across a script reload; the draft holds everything the creator chose.
        [SerializeField] CopyDraft draft = new CopyDraft();
        [SerializeField] string status = "";
        bool rebuildPending;

        public CopyDraft Draft => draft;
        public string Status => status;

        /// <summary>What the copy would do, as the window last read it.</summary>
        public CopyPlan Plan { get; private set; }

        public static void Show(CopyDraft value)
        {
            var window = GetWindow<CopyMenuWindow>(true, L.Text("ad.ui.copy_to.title"));
            window.minSize = new Vector2(OpeningWidth, OpeningHeight);
            window.Init(value);
            window.Show();
        }

        /// <summary>Gives the window what it copies and builds the panel. Tests call this after ScriptableObject.CreateInstance, so no real window shows.</summary>
        public void Init(CopyDraft value)
        {
            draft = value ?? new CopyDraft();
            status = "";
            Rebuild();
        }

        /// <summary>Unity asks for the panel when the window is first shown and again after every script reload.</summary>
        public void CreateGUI() => Rebuild();

        void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndo;
            AvatarPlanCache.Changed += OnCacheChanged;
            EditorApplication.hierarchyChanged += QueueRebuild;
            L.LanguageChanged += OnLanguageChanged;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndo;
            AvatarPlanCache.Changed -= OnCacheChanged;
            EditorApplication.hierarchyChanged -= QueueRebuild;
            L.LanguageChanged -= OnLanguageChanged;
        }

        void OnLanguageChanged()
        {
            titleContent = new GUIContent(L.Text("ad.ui.copy_to.title"));
            Rebuild();
        }

        /// <summary>An undo can take the copy back, so the line saying what was copied goes and the plan is read again.</summary>
        void OnUndo()
        {
            status = "";
            QueueRebuild();
        }

        void OnCacheChanged(Transform _) => QueueRebuild();

        /// <summary>Rebuilds once on the next frame, however many changes arrive before it.</summary>
        void QueueRebuild()
        {
            if (rebuildPending || rootVisualElement.childCount == 0) return;
            rebuildPending = true;
            rootVisualElement.schedule.Execute(() =>
            {
                rebuildPending = false;
                Rebuild();
            });
        }

        /// <summary>Copies the component, as the Copy button does, and says what happened under it.</summary>
        public AvatarDoc Copy()
        {
            var copy = MenuCopy.Apply(draft, out var said);
            status = said;
            Rebuild();
            return copy;
        }

        bool CanRun => Plan != null && !Plan.Blocked && !RefusesReplace;

        /// <summary>Replace would overwrite a locked component other than the copy the last press made.</summary>
        bool RefusesReplace => Plan != null && Plan.Where == CopyWhere.Replace && EntryEditing.Refuses(Plan.ReplaceTarget) && !Plan.ReplacesOwnCopy;

        void Rebuild()
        {
            var root = rootVisualElement;
            root.Clear();
            root.AddToClassList("ad-root");
            root.AddToClassList(EditorGUIUtility.isProSkin ? "ad-dark" : "ad-light");
            root.AddToClassList("ad-window");
            var uss = AvatarDoctorView.LoadStyleSheet();
            if (uss != null && !root.styleSheets.Contains(uss)) root.styleSheets.Add(uss);

            // As tall as what it holds, scrolling only in a window shorter than that.
            var body = new ScrollView { name = "ad-copy-scroll" };
            root.Add(body);

            var intro = new Label(L.Text("ad.ui.copy_to.intro")) { name = "ad-copy-intro" };
            intro.AddToClassList("ad-note");
            body.Add(intro);

            Plan = null;
            var sourceRoot = MenuCopy.AvatarOf(draft.source);
            if (sourceRoot == null)
            {
                body.Add(WarnBox("ad-copy-gone", L.Text("ad.ui.copy_to.gone")));
                LanguageFont.ApplyToChildren(root);
                return;
            }

            var fromRow = Row("ad-copy-from-row", "ad.ui.copy_to.from");
            var from = Value("ad-copy-from", Place(sourceRoot, MenuCopy.PathOf(sourceRoot, draft.source.transform)), "ad.ui.copy_to.from.tip");
            fromRow.Add(from);
            body.Add(fromRow);

            BuildTarget(body);
            Plan = MenuCopy.Plan(draft);
            if (Plan != null)
            {
                BuildWhere(body);
                BuildObjects(body);
                BuildChecks(body);
            }

            if (Plan != null && Plan.Blocked) body.Add(Note("ad-copy-blocked", L.Text("ad.ui.copy_to.run.blocked")));

            var run = new Button(() => Copy()) { text = L.Text("ad.ui.copy_to.run"), name = "ad-copy-run", tooltip = L.Text("ad.ui.copy_to.run.tip") };
            run.AddToClassList("ad-primary");
            run.AddToClassList("ad-add-run");
            run.SetEnabled(CanRun);
            body.Add(run);

            var said = new Label(status) { name = "ad-copy-status" };
            said.AddToClassList("ad-note");
            body.Add(said);
            LanguageFont.ApplyToChildren(root);
        }

        /// <summary>An avatar's name, then the place under it: "Avatar/MenuAddon", or the avatar's name alone for its root.</summary>
        static string Place(Transform root, string path) => string.IsNullOrEmpty(path) ? root.name : root.name + "/" + path;

        // ---- the other avatar --------------------------------------------------------------------------------------

        void BuildTarget(VisualElement body)
        {
            var targets = MenuCopy.Targets(draft.source);
            if (draft.target != null && !targets.Contains(draft.target)) MenuCopy.SetTarget(draft, null);
            if (draft.target == null && targets.Count > 0) MenuCopy.SetTarget(draft, targets[0]);

            var targetRow = Row("ad-copy-target-row", "ad.ui.copy_to.target");
            var target = Pick("ad-copy-target", draft.target != null ? draft.target.name : L.Text("ad.ui.copy_to.target.pick"), "ad.ui.copy_to.target.tip");
            bool severalScenes = targets.Select(t => t.gameObject.scene).Distinct().Count() > 1;
            target.clicked += () => ShowMenu(target, targets.Select(t => new AdMenu.Item
            {
                Text = t.name,
                Note = severalScenes ? t.gameObject.scene.name : "",
                Checked = t == draft.target,
                Action = () =>
                {
                    MenuCopy.SetTarget(draft, t);
                    status = "";
                    Rebuild();
                },
            }).ToList());
            target.SetEnabled(targets.Count > 0);
            targetRow.Add(target);
            body.Add(targetRow);

            if (targets.Count == 0)
            {
                var none = new Label(L.Text("ad.ui.copy_to.no_target")) { name = "ad-copy-no-target" };
                none.AddToClassList("ad-note");
                body.Add(none);
            }
        }

        // ---- where it goes -----------------------------------------------------------------------------------------

        void BuildWhere(VisualElement body)
        {
            var placeRow = Row("ad-copy-goes-on-row", "ad.ui.copy_to.goes_on");
            var place = Value("ad-copy-goes-on", Place(Plan.TargetRoot, Plan.DestinationIsFallback ? "" : Plan.DestinationPath), "ad.ui.copy_to.goes_on.tip");
            placeRow.Add(place);
            body.Add(placeRow);
            if (Plan.DestinationIsFallback)
            {
                var fallback = new Label(string.Format(L.Text("ad.ui.copy_to.goes_on.root"), L.Quoted(Plan.TargetRoot.name), L.Quoted(Plan.DestinationPath))) { name = "ad-copy-goes-on-root" };
                fallback.AddToClassList("ad-note");
                body.Add(fallback);
            }

            if (Plan.Existing.Count == 0) return;
            var whereRow = Row("ad-copy-where-row", "ad.ui.copy_to.where");
            var where = new SegmentedChoice(new[] { L.Text("ad.ui.copy_to.where.replace"), L.Text("ad.ui.copy_to.where.add") }, (int)Plan.Where,
                new[] { L.Text("ad.ui.copy_to.where.replace.tip"), L.Text("ad.ui.copy_to.where.add.tip") })
            {
                name = "ad-copy-where",
                tooltip = L.Text("ad.ui.copy_to.where.tip"),
            };
            where.AddToClassList("ad-add-choice");
            where.Changed += index =>
            {
                MenuCopy.SetWhere(draft, (CopyWhere)index);
                Rebuild();
            };
            whereRow.Add(where);
            body.Add(whereRow);

            if (Plan.Where != CopyWhere.Replace) return;
            if (Plan.Existing.Count > 1)
            {
                var replaceRow = Row("ad-copy-replace-row", "ad.ui.copy_to.replace");
                int at = Plan.Existing.IndexOf(Plan.ReplaceTarget);
                var replace = Pick("ad-copy-replace", ComponentName(at), "ad.ui.copy_to.replace.tip");
                replace.clicked += () => ShowMenu(replace, Plan.Existing.Select((c, i) => new AdMenu.Item
                {
                    Text = ComponentName(i),
                    Note = L.Aside(UiText.Count(c.entries.Count, "ad.ui.copy_to.entries.one", "ad.ui.copy_to.entries"), EntryEditing.Refuses(c) ? L.Text("ad.ui.add_from.component.locked") : ""),
                    Checked = c == Plan.ReplaceTarget,
                    Action = () =>
                    {
                        draft.replace = c;
                        Rebuild();
                    },
                }).ToList());
                replaceRow.Add(replace);
                body.Add(replaceRow);
            }
            if (RefusesReplace) body.Add(WarnBox("ad-copy-locked", L.Text("ad.ui.copy_to.locked")));
        }

        static string ComponentName(int index) => string.Format(L.Text("ad.ui.copy_to.replace.item"), index + 1);

        // ---- the objects it uses -----------------------------------------------------------------------------------

        void BuildObjects(VisualElement body)
        {
            var title = new Label(L.Text("ad.ui.copy_to.objects")) { name = "ad-copy-objects" };
            title.AddToClassList("ad-panel-title");
            title.AddToClassList("ad-copy-title");
            body.Add(title);

            if (Plan.References.Count == 0)
            {
                body.Add(Note("ad-copy-none-used", L.Text("ad.ui.copy_to.none_used")));
                return;
            }
            if (Plan.FoundCount > 0)
                body.Add(Note("ad-copy-found", UiText.Count(Plan.FoundCount, "ad.ui.copy_to.found.one", "ad.ui.copy_to.found")));

            int index = 0;
            foreach (var reference in Plan.Listed.ToList())
            {
                body.Add(ListedRow(reference, index));
                index++;
            }

            index = 0;
            foreach (var reference in Plan.Ambiguous.ToList())
            {
                body.Add(Note("ad-copy-ambiguous-" + index, string.Format(L.Text("ad.ui.copy_to.ambiguous"), L.Quoted(reference.AmbiguousAt), reference.SourceCount, reference.TargetCount)));
                index++;
            }
        }

        /// <summary>
        /// One object to pick: why it is listed (marked Pick by hand, not on the other avatar, or there without a
        /// component the copy needs) with the entries that use it, the field that picks the object of the other avatar,
        /// and under it whether what the field starts with is a suggestion and why it cannot stay empty.
        /// </summary>
        VisualElement ListedRow(CopyReference reference, int index)
        {
            var block = new VisualElement { name = "ad-copy-missing-" + index };
            block.AddToClassList("ad-copy-missing");
            var place = L.Quoted(Place(Plan.SourceRoot, reference.Path));
            var target = L.Quoted(Plan.TargetRoot.name);
            string text = reference.ByHand ? string.Format(L.Text("ad.ui.copy_to.by_hand_row"), place, target, Uses(reference.Uses))
                : reference.Found == null ? string.Format(L.Text("ad.ui.copy_to.missing"), place, target, Uses(reference.Uses))
                : string.Format(L.Text("ad.ui.copy_to.missing_component"), place, target, L.JoinList(reference.MissingParts.Select(p => p.GetType().Name).Distinct()), Uses(reference.Uses));
            block.Add(WarnBox("ad-copy-missing-text-" + index, text));

            var pickRow = Row("ad-copy-pick-row-" + index, "ad.ui.copy_to.use");
            var field = new ObjectField { objectType = reference.FieldType, allowSceneObjects = true, name = "ad-copy-pick-" + index, tooltip = L.Text("ad.ui.copy_to.use.tip") };
            field.SetValueWithoutNotify(reference.Value);
            var source = reference.Source;
            field.RegisterValueChangedCallback(e =>
            {
                if (MenuCopy.Pick(draft, source, e.newValue))
                {
                    status = "";
                    QueueRebuild();
                    return;
                }
                field.SetValueWithoutNotify(e.previousValue);
                status = string.Format(L.Text("ad.ui.copy_to.pick_outside"), L.Quoted(TransformName(e.newValue)), L.Quoted(Plan.TargetRoot.name));
                var said = rootVisualElement.Q<Label>("ad-copy-status");
                if (said != null) said.text = status;
            });
            field.AddToClassList("ad-add-field");
            pickRow.Add(field);
            block.Add(pickRow);

            if (!reference.HasPick && reference.Suggestion != null)
            {
                var suggested = Note("ad-copy-suggested-" + index, reference.SuggestedByPlace ? L.Text("ad.ui.copy_to.suggested.place")
                    : string.Format(L.Text("ad.ui.copy_to.suggested"), L.Quoted(TransformName(reference.Suggestion))));
                suggested.AddToClassList("ad-note-indent");
                block.Add(suggested);
            }
            if (reference.Need != CopyNeed.None)
            {
                var needs = Note("ad-copy-needs-" + index, L.Text(reference.Need == CopyNeed.PathRoot ? "ad.ui.copy_to.needs.path_root" : "ad.ui.copy_to.needs.required"));
                needs.AddToClassList("ad-note-indent");
                needs.AddToClassList("ad-copy-needs");
                block.Add(needs);
            }
            return block;
        }

        static string TransformName(Object value) => value is Component component ? component.gameObject.name : value != null ? value.name : "";

        static string Uses(IEnumerable<string> uses) => L.JoinList(uses.Select(L.Quoted));

        // ---- the checks --------------------------------------------------------------------------------------------

        void BuildChecks(VisualElement body)
        {
            var title = new Label(L.Text("ad.ui.copy_to.checks")) { name = "ad-copy-checks" };
            title.AddToClassList("ad-panel-title");
            title.AddToClassList("ad-copy-title");
            body.Add(title);
            if (Plan.Checks.Count == 0)
            {
                body.Add(Note("ad-copy-checks-none", L.Text("ad.ui.copy_to.checks.none")));
                return;
            }
            for (int i = 0; i < Plan.Checks.Count; i++)
            {
                var check = Plan.Checks[i];
                string key = check.Kind == CopyCheckKind.Shape ? "ad.ui.copy_to.check.shape" : check.Kind == CopyCheckKind.Slot ? "ad.ui.copy_to.check.slot" : "ad.ui.copy_to.check.property";
                var value = check.Kind == CopyCheckKind.Slot ? check.Value : L.Quoted(check.Value);
                body.Add(WarnBox("ad-copy-check-" + i, string.Format(L.Text(key), L.Quoted(check.Renderer.name), value, Uses(check.Uses))));
            }
        }

        // ---- building blocks ---------------------------------------------------------------------------------------

        /// <summary>A row with its caption at the label column; the caption carries the tooltip of the control after it.</summary>
        static VisualElement Row(string rowName, string labelKey)
        {
            var row = new VisualElement { name = rowName };
            row.AddToClassList("ad-row");
            var caption = new Label(L.Text(labelKey)) { tooltip = L.Text(labelKey + ".tip") };
            caption.AddToClassList("ad-label");
            row.Add(caption);
            return row;
        }

        /// <summary>A name after the label column; a long one ends in an ellipsis and the tooltip shows it whole.</summary>
        static Label Value(string labelName, string text, string tipKey)
        {
            var label = new Label(text) { name = labelName, tooltip = text + "\n" + L.Text(tipKey), displayTooltipWhenElided = false };
            label.AddToClassList("ad-add-value");
            return label;
        }

        /// <summary>A button that opens a list, at the label column; a long name ends in an ellipsis and the tooltip shows it whole.</summary>
        static Button Pick(string buttonName, string text, string tipKey)
        {
            var button = new Button { text = text, name = buttonName, tooltip = text + "\n" + L.Text(tipKey), displayTooltipWhenElided = false };
            button.AddToClassList("ad-add-pick");
            return button;
        }

        static Label Note(string labelName, string text)
        {
            var label = new Label(text) { name = labelName };
            label.AddToClassList("ad-note");
            return label;
        }

        static VisualElement WarnBox(string boxName, string text)
        {
            var box = new VisualElement { name = boxName };
            box.AddToClassList("ad-warnbox");
            box.Add(UiText.MessageLabel(text));
            return box;
        }
    }
}
#endif
