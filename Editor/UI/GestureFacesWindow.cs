#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The Gesture faces window: the face mesh, the parameter face tracking sets, whose face wins while both hands
    /// show one, Fist strength and the menu switch, then a row per gesture and hand with its face, its Capture, and
    /// what it does while face tracking is on, and Make. Nothing reaches the component before Make, and the window
    /// never changes the selection.
    /// </summary>
    public sealed class GestureFacesWindow : EditorWindow
    {
        /// <summary>The width the window opens at, which the docs picture shows too.</summary>
        public const float OpeningWidth = 360f;
        const float OpeningHeight = 560f;

        /// <summary>How a list opens under its button. Replaceable by a test, which reads the items instead of showing them.</summary>
        public static Action<VisualElement, List<AdMenu.Item>> ShowMenu = (anchor, items) => AdMenu.Open(anchor.worldBound, anchor, items, search: items.Count > 8);

        /// <summary>Opens the window for a component, starting from the faces its triggers show. Replaceable by a test, which reads the component instead of opening a window.</summary>
        public static Action<AvatarDoc> Open = component => Show(GestureFaces.Read(component));

        // A window keeps only serialized fields across a script reload; the draft holds everything the creator chose.
        [SerializeField] GestureFacesDraft draft = new GestureFacesDraft();
        [SerializeField] string status = "";
        bool rebuildPending;

        public GestureFacesDraft Draft => draft;
        public string Status => status;

        public static void Show(GestureFacesDraft value)
        {
            var window = GetWindow<GestureFacesWindow>(true, L.Text("ad.ui.gestures.title"));
            window.minSize = new Vector2(OpeningWidth, OpeningHeight);
            window.Init(value);
            window.Show();
        }

        /// <summary>Gives the window its faces and builds the panel. Tests call this after ScriptableObject.CreateInstance, so no real window shows.</summary>
        public void Init(GestureFacesDraft value)
        {
            draft = value ?? new GestureFacesDraft();
            GestureFaces.SnapshotRest(draft);
            status = "";
            Rebuild();
        }

        /// <summary>Unity asks for the panel when the window is first shown and again after every script reload.</summary>
        public void CreateGUI() => Rebuild();

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

        void OnLanguageChanged()
        {
            titleContent = new GUIContent(L.Text("ad.ui.gestures.title"));
            Rebuild();
        }

        /// <summary>An undo can take back what Make wrote or move a shape away from where it rests again, so the line saying what happened goes and the scene is read again.</summary>
        void OnUndo()
        {
            status = "";
            QueueRebuild();
        }

        void OnCacheChanged(Transform _) => QueueRebuild();

        /// <summary>Coming back from setting a face up in the mesh's own inspector: the warning about shapes away from where they rest follows, and nothing else is built again, so a click into a slider keeps its drag.</summary>
        void OnFocus()
        {
            if (rootVisualElement.childCount > 0 && PosedKey() != shownPosed) QueueRebuild();
        }

        /// <summary>The shapes the warning names, as the last build of the panel showed them.</summary>
        string shownPosed = "";

        string PosedKey() => string.Join("|", GestureFaces.PosedShapes(draft).Select(s => s.renderer.GetInstanceID() + "/" + s.shape));

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

        /// <summary>Makes the faces, as the Make button does, and says what happened under it.</summary>
        public List<MenuEntry> Make()
        {
            var made = GestureFaces.Apply(draft, out var said);
            status = said;
            Rebuild();
            return made;
        }

        /// <summary>Captures the face mesh's shapes that are away from where they rest as one row's face, as its Capture button does.</summary>
        public int Capture(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= draft.rows.Count || draft.faceMesh == null) return 0;
            var row = draft.rows[rowIndex];
            int count = GestureFaces.Capture(draft, row);
            status = count == 0
                ? string.Format(L.Text("ad.ui.gestures.capture.nothing"), draft.faceMesh.name)
                : string.Format(L.Text(UiText.CountKey(count, "ad.ui.gestures.captured")), count, GestureFaces.GestureName(row.gesture));
            Rebuild();
            return count;
        }

        /// <summary>Puts the shapes the faces use back where they rest in the scene, as the button under the warning does.</summary>
        public int ResetPosed()
        {
            int count = GestureFaces.ResetPosed(draft);
            if (count > 0) status = string.Format(L.Text(UiText.CountKey(count, "ad.ui.gestures.posed.done")), count);
            Rebuild();
            return count;
        }

        void AddRow(int gesture)
        {
            bool leftTaken = draft.rows.Any(r => r.gesture == gesture && r.hand == GestureHand.Left);
            draft.rows.Add(new GestureFaceRow { gesture = gesture, hand = leftTaken ? GestureHand.Right : GestureHand.Left });
            draft.rows = draft.rows.OrderBy(r => r.gesture).ToList();
            Rebuild();
        }

        bool CanMake()
        {
            if (draft.component == null || EntryEditing.Refuses(draft.component)) return false;
            if (GestureFaces.Overlaps(draft).Count > 0) return false;
            bool anyFace = draft.rows.Any(GestureFaces.HasFace);
            bool anyMade = draft.component.entries.Any(e => e != null && !string.IsNullOrEmpty(e.gestureFace));
            return anyFace || anyMade;
        }

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
            var body = new ScrollView { name = "ad-gestures-scroll" };
            root.Add(body);

            var intro = new Label(L.Text("ad.ui.gestures.intro")) { name = "ad-gestures-intro" };
            intro.AddToClassList("ad-note");
            body.Add(intro);

            var avatarRoot = draft.component != null ? AvatarPlanCache.RootOf(draft.component) : null;
            if (avatarRoot == null)
            {
                body.Add(WarnBox("ad-gestures-gone", L.Text("ad.ui.gestures.gone")));
                LanguageFont.ApplyToChildren(root);
                return;
            }
            foreach (var row in draft.rows) row.shapes.RemoveAll(s => s == null || s.renderer == null);

            BuildSettings(body, avatarRoot);
            BuildRows(body);

            var posed = GestureFaces.PosedShapes(draft);
            shownPosed = PosedKey();
            if (posed.Count > 0)
            {
                var box = WarnBox("ad-gestures-posed", string.Format(L.Text(UiText.CountKey(posed.Count, "ad.ui.gestures.posed")), posed.Count,
                    L.JoinList(posed.Select(s => s.renderer.name).Distinct()), L.JoinList(posed.Select(s => s.shape))));
                body.Add(box);
                var reset = new Button(() => ResetPosed()) { text = L.Text("ad.ui.gestures.posed.reset"), name = "ad-gestures-posed-reset", tooltip = L.Text("ad.ui.gestures.posed.reset.tip") };
                reset.AddToClassList("ad-add-component-add");
                body.Add(reset);
            }

            if (EntryEditing.Refuses(draft.component)) body.Add(WarnBox("ad-gestures-locked", L.Text("ad.ui.locked.refused")));

            var make = new Button(() => Make()) { text = L.Text("ad.ui.gestures.make"), name = "ad-gestures-make", tooltip = L.Text("ad.ui.gestures.make.tip") };
            make.AddToClassList("ad-primary");
            make.AddToClassList("ad-add-run");
            make.SetEnabled(CanMake());
            body.Add(make);

            var said = new Label(status) { name = "ad-gestures-status" };
            said.AddToClassList("ad-note");
            body.Add(said);
            LanguageFont.ApplyToChildren(root);
        }

        void BuildSettings(VisualElement body, Transform avatarRoot)
        {
            var meshRow = Row("ad-gestures-mesh-row", "ad.ui.gestures.mesh");
            var mesh = Pick("ad-gestures-mesh", draft.faceMesh != null ? draft.faceMesh.name : L.Text("ad.ui.gestures.mesh.pick"), "ad.ui.gestures.mesh.tip");
            mesh.clicked += () => ShowMenu(mesh, AvatarObjectPicker.Items(avatarRoot, PickKind.SkinnedMeshWithShapes, picked =>
            {
                if (!(picked is SkinnedMeshRenderer renderer)) return;
                draft.faceMesh = renderer;
                GestureFaces.SnapshotRest(draft);
                Rebuild();
            }));
            meshRow.Add(mesh);
            body.Add(meshRow);

            var trackingRow = Row("ad-gestures-tracking-row", "ad.ui.gestures.tracking");
            bool tracked = !string.IsNullOrEmpty(draft.trackingParameter);
            // Any name can be typed, for a face tracking setup that brings its parameter in a way AvatarDoc cannot see.
            var tracking = new TextField { name = "ad-gestures-tracking", value = draft.trackingParameter ?? "", isDelayed = true, tooltip = L.Text("ad.ui.gestures.tracking.tip") };
            tracking.AddToClassList("ad-gestures-tracking");
            tracking.RegisterValueChangedCallback(e =>
            {
                draft.trackingParameter = (e.newValue ?? "").Trim();
                Rebuild();
            });
            trackingRow.Add(tracking);
            var pick = new Button { name = "ad-gestures-tracking-pick", tooltip = L.Text("ad.ui.gestures.tracking.pick.tip") };
            EntryDetailView.AddDropdownIcon(pick);
            pick.clicked += () => ShowMenu(pick, TrackingItems(avatarRoot));
            trackingRow.Add(pick);
            body.Add(trackingRow);
            if (!tracked)
            {
                var none = new Label(L.Text("ad.ui.gestures.tracking.none_note")) { name = "ad-gestures-tracking-none" };
                none.AddToClassList("ad-note");
                none.AddToClassList("ad-note-indent");
                body.Add(none);
            }
            else if (!GestureFaces.TrackingKnown(avatarRoot, draft.trackingParameter))
            {
                var unknown = new Label(string.Format(L.Text("ad.ui.gestures.tracking.unknown"), draft.trackingParameter)) { name = "ad-gestures-tracking-unknown" };
                unknown.AddToClassList("ad-note");
                unknown.AddToClassList("ad-note-indent");
                body.Add(unknown);
            }
            else if (!GestureFaces.TrackingSynced(avatarRoot, draft.trackingParameter))
            {
                var local = new Label(string.Format(L.Text("ad.ui.gestures.tracking.local"), draft.trackingParameter)) { name = "ad-gestures-tracking-local" };
                local.AddToClassList("ad-note");
                local.AddToClassList("ad-note-indent");
                body.Add(local);
            }

            var orderRow = Row("ad-gestures-order-row", "ad.ui.gestures.order");
            var order = new SegmentedChoice(new[] { L.Text("ad.ui.gestures.order.right"), L.Text("ad.ui.gestures.order.left") }, (int)draft.order,
                new[] { L.Text("ad.ui.gestures.order.right.tip"), L.Text("ad.ui.gestures.order.left.tip") })
            {
                name = "ad-gestures-order",
                tooltip = L.Text("ad.ui.gestures.order.tip"),
            };
            order.AddToClassList("ad-add-choice");
            order.Changed += index => draft.order = (GestureOrder)index;
            orderRow.Add(order);
            body.Add(orderRow);

            var fistRow = Row("ad-gestures-fist-row", "ad.ui.gestures.fist");
            var fist = Check("ad-gestures-fist", L.Text("ad.ui.gestures.fist.check"), "ad.ui.gestures.fist.tip", draft.fistStrength);
            fist.RegisterValueChangedCallback(e => draft.fistStrength = e.newValue);
            fistRow.Add(fist);
            body.Add(fistRow);

            var switchRow = Row("ad-gestures-switch-row", "ad.ui.gestures.switch");
            var menuSwitch = Check("ad-gestures-switch", L.Text("ad.ui.gestures.switch.check"), "ad.ui.gestures.switch.tip", draft.menuSwitch);
            menuSwitch.RegisterValueChangedCallback(e => draft.menuSwitch = e.newValue);
            switchRow.Add(menuSwitch);
            body.Add(switchRow);
        }

        /// <summary>None, then every Bool of the avatar, the one face tracking was found as marked.</summary>
        List<AdMenu.Item> TrackingItems(Transform avatarRoot)
        {
            var found = GestureFaces.DetectTracking(avatarRoot);
            var items = new List<AdMenu.Item>
            {
                new AdMenu.Item
                {
                    Text = L.Text("ad.ui.gestures.tracking.none"),
                    Checked = string.IsNullOrEmpty(draft.trackingParameter),
                    Action = () => { draft.trackingParameter = ""; Rebuild(); },
                },
            };
            foreach (var name in GestureFaces.TrackingChoices(avatarRoot, found))
            {
                var picked = name;
                items.Add(new AdMenu.Item
                {
                    Text = name,
                    Note = name == found ? L.Text("ad.ui.gestures.tracking.found") : "",
                    Checked = name == draft.trackingParameter,
                    Action = () => { draft.trackingParameter = picked; Rebuild(); },
                });
            }
            return items;
        }

        void BuildRows(VisualElement body)
        {
            var title = new Label(L.Text("ad.ui.gestures.faces")) { name = "ad-gestures-faces" };
            title.AddToClassList("ad-panel-title");
            title.AddToClassList("ad-copy-title");
            body.Add(title);

            var overlaps = GestureFaces.Overlaps(draft);
            if (overlaps.Count > 0)
                body.Add(WarnBox("ad-gestures-overlap", string.Join(" ", overlaps.Select(g => string.Format(L.Text("ad.ui.gestures.overlap"), GestureFaces.GestureName(g))))));

            for (int i = 0; i < draft.rows.Count; i++) body.Add(Card(i));

            var add = new Button { text = L.Text("ad.ui.gestures.add_row"), name = "ad-gestures-add-row", tooltip = L.Text("ad.ui.gestures.add_row.tip") };
            add.clicked += () => ShowMenu(add, Enumerable.Range(1, GestureFaces.GestureCount).Select(g =>
            {
                int gesture = g;
                return new AdMenu.Item { Text = GestureFaces.GestureName(gesture), Action = () => AddRow(gesture) };
            }).ToList());
            add.AddToClassList("ad-add-component-add");
            body.Add(add);
        }

        VisualElement Card(int index)
        {
            var row = draft.rows[index];
            var card = new VisualElement { name = "ad-gestures-row-" + index };
            card.AddToClassList("ad-panel");
            card.AddToClassList("ad-gestures-row");

            var header = new VisualElement();
            header.AddToClassList("ad-panel-title");
            var name = new Label(GestureFaces.GestureName(row.gesture)) { name = "ad-gestures-row-title" };
            name.AddToClassList("ad-gestures-row-title");
            header.Add(name);
            var spacer = new VisualElement();
            spacer.AddToClassList("ad-spacer");
            header.Add(spacer);
            var hand = new SegmentedChoice(new[] { L.Text("ad.ui.gestures.hand.left"), L.Text("ad.ui.gestures.hand.right"), L.Text("ad.ui.gestures.hand.both") }, (int)row.hand,
                new[] { L.Text("ad.ui.gestures.hand.left.tip"), L.Text("ad.ui.gestures.hand.right.tip"), L.Text("ad.ui.gestures.hand.both.tip") })
            {
                name = "ad-gestures-hand",
                tooltip = L.Text("ad.ui.gestures.hand.tip"),
            };
            hand.Changed += choice =>
            {
                row.hand = (GestureHand)choice;
                Rebuild();
            };
            header.Add(hand);
            var remove = new Button(() =>
            {
                draft.rows.Remove(row);
                Rebuild();
            }) { text = "×", name = "ad-gestures-row-remove", tooltip = L.Text("ad.ui.gestures.row.remove.tip") };
            remove.AddToClassList("ad-gestures-remove");
            header.Add(remove);
            card.Add(header);

            var content = new VisualElement();
            content.AddToClassList("ad-panel-body");
            if (row.shapes.Count == 0)
            {
                var empty = new Label(L.Text("ad.ui.gestures.row.empty")) { name = "ad-gestures-row-empty" };
                empty.AddToClassList("ad-note");
                content.Add(empty);
            }
            foreach (var shape in row.shapes.ToList()) content.Add(ShapeRow(row, shape));

            var buttons = new VisualElement { name = "ad-gestures-row-buttons" };
            buttons.AddToClassList("ad-row");
            buttons.AddToClassList("ad-gestures-buttons");
            var capture = new Button(() => Capture(draft.rows.IndexOf(row))) { text = L.Text("ad.ui.gestures.capture"), name = "ad-gestures-capture", tooltip = L.Text("ad.ui.gestures.capture.tip") };
            capture.AddToClassList("ad-gestures-capture");
            capture.SetEnabled(draft.faceMesh != null);
            buttons.Add(capture);
            var addShape = new Button { text = L.Text("ad.ui.gestures.add_shape"), name = "ad-gestures-add-shape", tooltip = L.Text("ad.ui.gestures.add_shape.tip") };
            addShape.clicked += () => ShowMenu(addShape, ShapePicker.ItemsOf(draft.faceMesh, picked =>
            {
                if (!row.shapes.Any(s => s.renderer == draft.faceMesh && s.shape == picked))
                    row.shapes.Add(new GestureShape { renderer = draft.faceMesh, shape = picked, weight = 100f });
                Rebuild();
            }));
            addShape.SetEnabled(draft.faceMesh != null);
            buttons.Add(addShape);
            content.Add(buttons);

            if (!string.IsNullOrEmpty(draft.trackingParameter))
            {
                var trackingRow = Row("ad-gestures-row-tracking-row", "ad.ui.gestures.row.tracking");
                var tracking = new SegmentedChoice(new[] { L.Text("ad.ui.gestures.paused"), L.Text("ad.ui.gestures.wins") }, (int)row.tracking,
                    new[] { L.Text("ad.ui.gestures.paused.tip"), L.Text("ad.ui.gestures.wins.tip") })
                {
                    name = "ad-gestures-row-tracking",
                    tooltip = L.Text("ad.ui.gestures.row.tracking.tip"),
                };
                tracking.AddToClassList("ad-add-choice");
                tracking.Changed += choice => row.tracking = (GestureTracking)choice;
                trackingRow.Add(tracking);
                content.Add(trackingRow);
            }
            card.Add(content);
            return card;
        }

        /// <summary>One shape of a face: its name on a line of its own, wrapping when it is long, with its mesh's name too when it is not the face mesh and the button that takes it out, then its slider.</summary>
        VisualElement ShapeRow(GestureFaceRow row, GestureShape shape)
        {
            var block = new VisualElement();
            block.AddToClassList("ad-gestures-shape-block");
            var line = new VisualElement();
            line.AddToClassList("ad-row");
            var label = shape.renderer == draft.faceMesh ? shape.shape : shape.renderer.name + "/" + shape.shape;
            var name = new Label(label) { name = "ad-gestures-shape-name", tooltip = L.Text("ad.ui.gestures.shape.tip") };
            name.AddToClassList("ad-gestures-shape-name");
            line.Add(name);
            var remove = new Button(() =>
            {
                row.shapes.Remove(shape);
                Rebuild();
            }) { text = "×", name = "ad-gestures-shape-remove", tooltip = L.Text("ad.ui.gestures.shape.remove.tip") };
            remove.AddToClassList("ad-gestures-remove");
            line.Add(remove);
            block.Add(line);
            var slider = new Slider(0f, 100f) { showInputField = true, name = "ad-gestures-shape", tooltip = L.Text("ad.ui.gestures.shape.tip") };
            slider.SetValueWithoutNotify(shape.weight);
            slider.AddToClassList("ad-gestures-shape");
            slider.RegisterValueChangedCallback(e => shape.weight = e.newValue);
            block.Add(slider);
            return block;
        }

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

        /// <summary>A button that opens a list, at the label column; a long name ends in an ellipsis and the tooltip shows it whole.</summary>
        static Button Pick(string buttonName, string text, string tipKey)
        {
            var button = new Button { text = text, name = buttonName, tooltip = text + "\n" + L.Text(tipKey), displayTooltipWhenElided = false };
            button.AddToClassList("ad-add-pick");
            return button;
        }

        /// <summary>A check box at the label column with its sentence after it, wrapping onto more lines in a narrow window.</summary>
        static Toggle Check(string toggleName, string text, string tipKey, bool value)
        {
            var toggle = new Toggle { text = text, value = value, name = toggleName, tooltip = L.Text(tipKey) };
            toggle.AddToClassList("ad-add-check");
            return toggle;
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
