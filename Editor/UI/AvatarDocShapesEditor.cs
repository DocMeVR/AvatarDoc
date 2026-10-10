#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>The inspector of an AvatarDoc Shapes component: the follow rows, the while-on rows and what is wrong with them.</summary>
    [CustomEditor(typeof(AvatarDocShapes))]
    public sealed class AvatarDocShapesEditor : UnityEditor.Editor
    {
        static readonly string[] PlatformLabels = { "ad.ui.platform.inherit", "ad.ui.platform.both", "ad.ui.platform.pc", "ad.ui.platform.quest" };

        AvatarDocShapes component;
        DirectFields fields;
        VisualElement root;
        VisualElement messages;

        void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            L.LanguageChanged += OnLanguageChanged;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            L.LanguageChanged -= OnLanguageChanged;
        }

        void OnLanguageChanged()
        {
            if (root == null || target == null) return;
            Rebuild();
        }

        /// <summary>Unity does not rebuild a UI Toolkit inspector for an Undo, and the cards and buttons hold the array positions they were built for, so they would act on the wrong row.</summary>
        void OnUndoRedo()
        {
            if (root == null || target == null) return;
            serializedObject.Update();
            Rebuild();
        }

        public override VisualElement CreateInspectorGUI()
        {
            component = (AvatarDocShapes)target;
            root = new VisualElement();
            root.AddToClassList("ad-root");
            root.AddToClassList(EditorGUIUtility.isProSkin ? "ad-dark" : "ad-light");
            var uss = AvatarDoctorView.LoadStyleSheet();
            if (uss != null) root.styleSheets.Add(uss);
            Build();
            return root;
        }

        Transform AvatarRoot => component != null ? AvatarPlanCache.RootOf(component) : null;

        void Build()
        {
            root.Clear();
            fields = new DirectFields(serializedObject, Commit);
            serializedObject.Update();

            root.Add(Note("ad.ui.shapes.note"));
            root.Add(Section("ad.ui.shapes.follows", "ad.ui.shapes.follows.tip"));
            root.Add(Note("ad.ui.shapes.follows.note"));
            var follow = serializedObject.FindProperty(nameof(AvatarDocShapes.follow));
            if (follow.arraySize == 0) root.Add(Note("ad.ui.shapes.none"));
            for (int i = 0; i < follow.arraySize; i++) root.Add(FollowCard(follow, i));
            var followButtons = new VisualElement();
            followButtons.AddToClassList("ad-row");
            followButtons.Add(new Button(AddFollow) { text = L.Text("ad.ui.shapes.add"), name = "ad-shapes-add-follow", tooltip = L.Text("ad.ui.shapes.add_follow.tip") });
            var matching = new Button { text = L.Text("ad.ui.shapes.add_matching"), name = "ad-shapes-add-matching", tooltip = L.Text("ad.ui.shapes.add_matching.tip") };
            matching.clicked += () => AvatarObjectPicker.Show(matching, AvatarRoot, PickKind.SkinnedMeshWithShapes,
                picked => AddMatching(picked as SkinnedMeshRenderer));
            followButtons.Add(matching);
            root.Add(followButtons);

            root.Add(Section("ad.ui.shapes.while_on", "ad.ui.shapes.while_on.tip"));
            root.Add(Note("ad.ui.shapes.while_on.note"));
            var whileOn = serializedObject.FindProperty(nameof(AvatarDocShapes.whileOn));
            if (whileOn.arraySize == 0) root.Add(Note("ad.ui.shapes.none"));
            for (int i = 0; i < whileOn.arraySize; i++) root.Add(WhileOnCard(whileOn, i));
            var whileOnButtons = new VisualElement();
            whileOnButtons.AddToClassList("ad-row");
            whileOnButtons.Add(new Button(AddWhileOn) { text = L.Text("ad.ui.shapes.add"), name = "ad-shapes-add-while-on", tooltip = L.Text("ad.ui.shapes.add_while_on.tip") });
            root.Add(whileOnButtons);

            var platformRow = new VisualElement();
            platformRow.AddToClassList("ad-row");
            var platform = fields.EnumChoice(serializedObject.FindProperty(nameof(AvatarDocShapes.platform)),
                L.Text("ad.ui.platform"), PlatformLabels.Select(L.Text).ToList());
            platform.name = "ad-shapes-platform";
            platform.tooltip = L.Text("ad.ui.shapes.platform.tip");
            platform.AddToClassList("ad-grow");
            platformRow.Add(platform);
            root.Add(platformRow);

            messages = new VisualElement { name = "ad-shapes-messages" };
            root.Add(messages);
            LanguageFont.ApplyToChildren(root);
            ShowMessages();
        }

        VisualElement FollowCard(SerializedProperty list, int index)
        {
            var element = list.GetArrayElementAtIndex(index);
            var card = new VisualElement { name = "ad-shapes-follow-" + index };
            card.AddToClassList("ad-when-card");

            var head = new VisualElement();
            head.AddToClassList("ad-row");
            var mesh = fields.ObjectOnAvatar<SkinnedMeshRenderer>(element.FindPropertyRelative(nameof(ShapeFollow.sourceMesh)),
                L.Text("ad.ui.shapes.source_mesh"), () => AvatarRoot, PickKind.SkinnedMeshWithShapes, CommitAndRebuild);
            mesh.name = "ad-shapes-follow-mesh-" + index;
            mesh.tooltip = L.Text("ad.ui.shapes.source_mesh.tip");
            mesh.AddToClassList("ad-grow");
            head.Add(mesh);
            head.Add(Remove(list, index, "ad-shapes-follow-remove-" + index));
            card.Add(head);

            var row = component.follow.Count > index ? component.follow[index] : null;
            card.Add(ShapeRow("ad.ui.shapes.source_shape", row != null ? row.sourceShape : "", "ad-shapes-follow-shape-" + index,
                anchor => ShapePicker.Show(anchor, AvatarRoot, (renderer, shape) =>
                {
                    fields.Write(element.FindPropertyRelative(nameof(ShapeFollow.sourceMesh)).propertyPath, p => p.objectReferenceValue = renderer, null);
                    fields.Write(element.FindPropertyRelative(nameof(ShapeFollow.sourceShape)).propertyPath, p => p.stringValue = shape, CommitAndRebuild);
                })));
            card.Add(ShapeRow("ad.ui.shapes.local_shape", row != null && row.localShape.Length > 0 ? row.localShape : L.Text("ad.ui.shapes.same_name"),
                "ad-shapes-follow-local-" + index,
                anchor => ShapePicker.Show(anchor, component.transform, (renderer, shape) =>
                    fields.Write(element.FindPropertyRelative(nameof(ShapeFollow.localShape)).propertyPath, p => p.stringValue = shape, CommitAndRebuild))));
            return card;
        }

        VisualElement WhileOnCard(SerializedProperty list, int index)
        {
            var element = list.GetArrayElementAtIndex(index);
            var card = new VisualElement { name = "ad-shapes-while-on-" + index };
            card.AddToClassList("ad-when-card");

            var head = new VisualElement();
            head.AddToClassList("ad-row");
            var mesh = fields.ObjectOnAvatar<SkinnedMeshRenderer>(element.FindPropertyRelative(nameof(ShapeWhileOn.mesh)),
                L.Text("ad.ui.shapes.mesh"), () => AvatarRoot, PickKind.SkinnedMeshWithShapes, CommitAndRebuild);
            mesh.name = "ad-shapes-while-on-mesh-" + index;
            mesh.tooltip = L.Text("ad.ui.shapes.mesh.tip");
            mesh.AddToClassList("ad-grow");
            head.Add(mesh);
            head.Add(Remove(list, index, "ad-shapes-while-on-remove-" + index));
            card.Add(head);

            var row = component.whileOn.Count > index ? component.whileOn[index] : null;
            card.Add(ShapeRow("ad.ui.shapes.shape", row != null ? row.shape : "", "ad-shapes-while-on-shape-" + index,
                anchor => ShapePicker.Show(anchor, AvatarRoot, (renderer, shape) =>
                {
                    fields.Write(element.FindPropertyRelative(nameof(ShapeWhileOn.mesh)).propertyPath, p => p.objectReferenceValue = renderer, null);
                    fields.Write(element.FindPropertyRelative(nameof(ShapeWhileOn.shape)).propertyPath, p => p.stringValue = shape, CommitAndRebuild);
                })));

            var valueRow = new VisualElement();
            valueRow.AddToClassList("ad-row");
            // A slider writes on every pointer move of a drag, so its commit only saves: rebuilding would drop the
            // drag, and planning the avatar for the messages would cost a walk per move. The messages follow once
            // the pointer lets go, or the focus leaves after a typed value.
            var value = fields.Slider(element.FindPropertyRelative(nameof(ShapeWhileOn.value)), L.Text("ad.ui.shapes.value"), 0f, 100f, Save);
            value.RegisterCallback<PointerUpEvent>(_ => ShowMessages(), TrickleDown.TrickleDown);
            value.RegisterCallback<FocusOutEvent>(_ => ShowMessages());
            value.name = "ad-shapes-while-on-value-" + index;
            value.tooltip = L.Text("ad.ui.shapes.value.tip");
            value.AddToClassList("ad-grow");
            valueRow.Add(value);
            card.Add(valueRow);
            return card;
        }

        /// <summary>A label and a button that opens the shape picker: a shape name never fits an ordinary text field at this width.</summary>
        static VisualElement ShapeRow(string labelKey, string text, string name, System.Action<VisualElement> show)
        {
            var row = new VisualElement();
            row.AddToClassList("ad-row");
            var tip = L.Text(labelKey + ".tip");
            var label = new Label(L.Text(labelKey)) { tooltip = tip };
            label.AddToClassList("ad-label");
            row.Add(label);
            var button = new Button { text = string.IsNullOrEmpty(text) ? L.Text("ad.ui.pick.shape") : text, name = name, tooltip = tip };
            button.AddToClassList("ad-shape-pick");
            button.clicked += () => show(button);
            row.Add(button);
            return row;
        }

        Button Remove(SerializedProperty list, int index, string name)
        {
            var button = new Button(() =>
            {
                serializedObject.Update();
                list.DeleteArrayElementAtIndex(index);
                CommitAndRebuild();
            })
            { text = "x", name = name };
            button.AddToClassList("ad-w-24");
            return button;
        }

        void AddFollow()
        {
            serializedObject.Update();
            var list = serializedObject.FindProperty(nameof(AvatarDocShapes.follow));
            list.arraySize++;
            var element = list.GetArrayElementAtIndex(list.arraySize - 1);
            element.FindPropertyRelative(nameof(ShapeFollow.sourceMesh)).objectReferenceValue = null;
            element.FindPropertyRelative(nameof(ShapeFollow.sourceShape)).stringValue = "";
            element.FindPropertyRelative(nameof(ShapeFollow.localShape)).stringValue = "";
            CommitAndRebuild();
        }

        void AddWhileOn()
        {
            serializedObject.Update();
            var list = serializedObject.FindProperty(nameof(AvatarDocShapes.whileOn));
            list.arraySize++;
            var element = list.GetArrayElementAtIndex(list.arraySize - 1);
            element.FindPropertyRelative(nameof(ShapeWhileOn.mesh)).objectReferenceValue = null;
            element.FindPropertyRelative(nameof(ShapeWhileOn.shape)).stringValue = "";
            element.FindPropertyRelative(nameof(ShapeWhileOn.value)).floatValue = 100f;
            CommitAndRebuild();
        }

        void AddMatching(SkinnedMeshRenderer source)
        {
            if (source == null) return;
            AddMatchingShapes(component, source);
            serializedObject.Update();
            Rebuild();
        }

        /// <summary>
        /// One follow row for every shape name the source mesh and the piece's own mesh share, skipping the rows
        /// that are already there. Returns how many were added.
        /// </summary>
        public static int AddMatchingShapes(AvatarDocShapes shapes, SkinnedMeshRenderer source)
        {
            if (shapes == null || source == null || source.sharedMesh == null) return 0;
            var local = shapes.GetComponent<SkinnedMeshRenderer>();
            if (local == null || local.sharedMesh == null) return 0;
            // Taken by the shape each row writes on this piece, whatever mesh it reads: two rows writing one local
            // shape would fight, and the second would be reported as the clip keeping its own curve.
            var known = new HashSet<string>(shapes.follow.Where(r => r != null).Select(r => r.EffectiveLocalShape));
            var added = new List<string>();
            for (int i = 0; i < source.sharedMesh.blendShapeCount; i++)
            {
                var name = source.sharedMesh.GetBlendShapeName(i);
                if (local.sharedMesh.GetBlendShapeIndex(name) < 0 || !known.Add(name)) continue;
                added.Add(name);
            }
            if (added.Count == 0) return 0;
            Undo.RecordObject(shapes, "Add matching shapes");
            foreach (var name in added) shapes.follow.Add(new ShapeFollow { sourceMesh = source, sourceShape = name });
            EntryEditing.MarkChanged(shapes);
            return added.Count;
        }

        void Commit()
        {
            Save();
            ShowMessages();
        }

        void Save()
        {
            serializedObject.ApplyModifiedProperties();
            EntryEditing.MarkChanged(component);
        }

        void CommitAndRebuild()
        {
            Commit();
            Rebuild();
        }

        /// <summary>Rebuilds the panel, letting go of the focus first so a click is not left inside an element that is about to be dropped.</summary>
        void Rebuild()
        {
            var focused = root.panel?.focusController?.focusedElement as VisualElement;
            if (focused != null && root.Contains(focused)) focused.Blur();
            Build();
        }

        /// <summary>How often the messages were planned and redrawn. Read by the tests.</summary>
        public int MessageRefreshes { get; private set; }

        void ShowMessages()
        {
            if (messages == null) return;
            MessageRefreshes++;
            messages.Clear();
            if (component == null) return;
            var plan = ShapesModel.PlanFor(component);
            var mine = plan.Messages.Where(m => m.Args != null && m.Args.Any(a => ReferenceEquals(a, component)));
            EntryDetailView.AddMessageBoxes(messages, mine);
        }

        static Label Note(string key)
        {
            var label = new Label(L.Text(key));
            label.AddToClassList("ad-note");
            return label;
        }

        static Label Section(string key, string tooltipKey = null)
        {
            var label = new Label(L.Text(key)) { tooltip = tooltipKey != null ? L.Text(tooltipKey) : null };
            label.AddToClassList("ad-section");
            return label;
        }
    }
}
#endif
