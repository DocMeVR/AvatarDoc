#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Fields that read their SerializedProperty when they are made and write it back through the same
    /// SerializedObject, without Unity's binding system in between. The panel is therefore complete in the frame it
    /// is built, and a refresh reloads the values in place instead of rebuilding. Undo and prefab overrides are
    /// unchanged: every write still goes through ApplyModifiedProperties. A field built on an entry, a merged
    /// animator or an installed menu asset of an AvatarDoc keeps that item's id beside its path and finds the item
    /// again by it whenever it reads or writes: while a typed value waits to be saved the panel is not rebuilt,
    /// and an Undo or a removal in front of the item moves it to another place in its list.
    /// </summary>
    public sealed class DirectFields
    {
        readonly SerializedObject serializedObject;
        readonly Action onChanged;
        readonly List<Action> loaders = new List<Action>();

        /// <summary>The lists of an AvatarDoc whose items carry an id.</summary>
        static readonly string[] IdLists = { nameof(AvatarDoc.entries), nameof(AvatarDoc.animators), nameof(AvatarDoc.menuAssets) };

        /// <summary>The id of the list item a field was built for, and the place it was last found at.</summary>
        sealed class Anchor
        {
            public readonly string Id;
            public int Index;

            public Anchor(string id, int index)
            {
                Id = id;
                Index = index;
            }
        }

        /// <param name="onChanged">What a committed edit does when the field itself names nothing else.</param>
        public DirectFields(SerializedObject serializedObject, Action onChanged)
        {
            this.serializedObject = serializedObject;
            this.onChanged = onChanged;
        }

        /// <summary>Forgets the fields of a panel that is being rebuilt.</summary>
        public void Clear() => loaders.Clear();

        /// <summary>Rewrites every field from its property, leaving the one the author is working in as it is.</summary>
        public void Reload()
        {
            serializedObject.Update();
            for (int i = 0; i < loaders.Count; i++) loaders[i]();
        }

        public TextField Text(SerializedProperty property, string label, Action commit = null, bool delayed = true)
        {
            var path = property.propertyPath;
            var anchor = AnchorOf(path);
            var field = new TextField(label) { isDelayed = delayed };
            Register(field, () => field.SetValueWithoutNotify(Read(path, anchor, p => p.stringValue) ?? ""));
            field.RegisterValueChangedCallback(e => Write(path, anchor, p => p.stringValue = e.newValue ?? "", commit));
            return field;
        }

        public Toggle Toggle(SerializedProperty property, string label, Action commit = null)
        {
            var path = property.propertyPath;
            var anchor = AnchorOf(path);
            var field = new Toggle(label);
            Register(field, () => field.SetValueWithoutNotify(Read(path, anchor, p => p.boolValue)));
            field.RegisterValueChangedCallback(e => Write(path, anchor, p => p.boolValue = e.newValue, commit));
            return field;
        }

        /// <summary>A toggle that shows and writes the negation of the property, such as "In menu" over a stored hiddenFromMenu flag: old data with the field left at its default reads as the switch being on.</summary>
        public Toggle ToggleInverted(SerializedProperty property, string label, Action commit = null)
        {
            var path = property.propertyPath;
            var anchor = AnchorOf(path);
            var field = new Toggle(label);
            Register(field, () => field.SetValueWithoutNotify(!Read(path, anchor, p => p.boolValue)));
            field.RegisterValueChangedCallback(e => Write(path, anchor, p => p.boolValue = !e.newValue, commit));
            return field;
        }

        public FloatField Float(SerializedProperty property, string label, Action commit = null, bool delayed = true)
        {
            var path = property.propertyPath;
            var anchor = AnchorOf(path);
            var field = new FloatField(label) { isDelayed = delayed };
            Register(field, () => field.SetValueWithoutNotify(Read(path, anchor, p => p.floatValue)));
            field.RegisterValueChangedCallback(e => Write(path, anchor, p => p.floatValue = e.newValue, commit));
            return field;
        }

        /// <summary>A float field clamped to a range on every write, such as a seconds value that must stay between 0 and 10.</summary>
        public FloatField FloatClamped(SerializedProperty property, string label, float min, float max, Action commit = null, bool delayed = true)
        {
            var path = property.propertyPath;
            var anchor = AnchorOf(path);
            var field = new FloatField(label) { isDelayed = delayed };
            Register(field, () => field.SetValueWithoutNotify(Mathf.Clamp(Read(path, anchor, p => p.floatValue), min, max)));
            field.RegisterValueChangedCallback(e =>
            {
                float clamped = Mathf.Clamp(e.newValue, min, max);
                if (!Mathf.Approximately(clamped, e.newValue)) field.SetValueWithoutNotify(clamped);
                Write(path, anchor, p => p.floatValue = clamped, commit);
            });
            return field;
        }

        /// <summary>A slider writes while it is dragged, so its commit runs per move: it must not rebuild anything.</summary>
        public Slider Slider(SerializedProperty property, string label, float min, float max, Action commit = null)
        {
            var path = property.propertyPath;
            var anchor = AnchorOf(path);
            var field = new Slider(label, min, max) { showInputField = true };
            Register(field, () => field.SetValueWithoutNotify(Read(path, anchor, p => p.floatValue)));
            field.RegisterValueChangedCallback(e => Write(path, anchor, p => p.floatValue = e.newValue, commit));
            return field;
        }

        public ObjectField Object<T>(SerializedProperty property, string label, Action commit = null, bool allowSceneObjects = false) where T : UnityEngine.Object
        {
            var path = property.propertyPath;
            var anchor = AnchorOf(path);
            var field = new ObjectField(label) { objectType = typeof(T), allowSceneObjects = allowSceneObjects };
            Register(field, () => field.SetValueWithoutNotify(Read(path, anchor, p => p.objectReferenceValue)));
            field.RegisterValueChangedCallback(e => Write(path, anchor, p => p.objectReferenceValue = e.newValue, commit));
            return field;
        }

        /// <summary>An object field whose target is on the avatar rather than an asset: the selector button opens AvatarObjectPicker instead of Unity's scene wide one, but every write still goes through this field's own callback, exactly like a drop.</summary>
        public ObjectField ObjectOnAvatar<T>(SerializedProperty property, string label, Func<Transform> avatarRoot, PickKind kind, Action commit = null) where T : UnityEngine.Object
        {
            var field = Object<T>(property, label, commit, allowSceneObjects: true);
            AvatarObjectPicker.TakeOverSelector(field, avatarRoot, kind, picked => field.value = picked);
            return field;
        }

        /// <summary>A Vector3 field, such as a local offset a Scene view handle can also write directly on the object: Reload still picks it up since it re-reads through the SerializedObject.</summary>
        public Vector3Field Vector3(SerializedProperty property, string label, Action commit = null)
        {
            var path = property.propertyPath;
            var anchor = AnchorOf(path);
            var field = new Vector3Field(label);
            Register(field, () => field.SetValueWithoutNotify(Read(path, anchor, p => p.vector3Value)));
            field.RegisterValueChangedCallback(e => Write(path, anchor, p => p.vector3Value = e.newValue, commit));
            return field;
        }

        /// <summary>An enum property as a dropdown of its inspector names, written back by position.</summary>
        public DropdownField EnumChoice(SerializedProperty property, string label, Action commit = null)
        {
            return EnumChoice(property, label, new List<string>(property.enumDisplayNames), commit);
        }

        /// <summary>An enum property as a dropdown of the given labels, one per value in order, written back by position.</summary>
        public DropdownField EnumChoice(SerializedProperty property, string label, IReadOnlyList<string> labels, Action commit = null)
        {
            var path = property.propertyPath;
            var anchor = AnchorOf(path);
            var choices = new List<string>(labels);
            var field = new DropdownField(label, choices, Mathf.Clamp(property.enumValueIndex, 0, choices.Count - 1));
            Register(field, () =>
            {
                int index = Mathf.Clamp(Read(path, anchor, p => p.enumValueIndex), 0, choices.Count - 1);
                field.SetValueWithoutNotify(choices[index]);
            });
            field.RegisterValueChangedCallback(e =>
            {
                int index = choices.IndexOf(e.newValue);
                if (index >= 0) Write(path, anchor, p => p.enumValueIndex = index, commit);
            });
            return field;
        }

        /// <summary>An enum property as a segmented choice, one item per value in order, written back by position. The caller gives the row its own label.</summary>
        public SegmentedChoice EnumSegments(SerializedProperty property, IReadOnlyList<string> labels, Action commit = null)
        {
            var path = property.propertyPath;
            var anchor = AnchorOf(path);
            var choice = new SegmentedChoice(labels, Mathf.Clamp(property.enumValueIndex, 0, labels.Count - 1));
            Register(choice, () => choice.SetIndexWithoutNotify(Mathf.Clamp(Read(path, anchor, p => p.enumValueIndex), 0, labels.Count - 1)));
            choice.Changed += index => Write(path, anchor, p => p.enumValueIndex = index, commit);
            return choice;
        }

        /// <summary>Matches one array element inside an otherwise plain property path, such as "entries.Array.data[750]" or "entries.Array.data[750].name": the shape every path into one of an AvatarDoc's own entry, group, animator, menu asset or parameter lists takes, since none of the fields this class hands out reach into a second, nested array.</summary>
        static readonly Regex ArrayElementPath = new Regex(@"^([^.]+)\.Array\.data\[(\d+)\](?:\.(.+))?$");

        /// <summary>
        /// The property a stored path names. A plain SerializedObject.FindProperty walks the property tree by name
        /// for every segment of the path, which is cheap for a short path but, for a path that reaches into one
        /// element of a large array (an entry deep in a component with hundreds of them), was measured costing
        /// several milliseconds a field: a panel of six or seven fields reloading a row that far into the array
        /// added up to the whole detail panel taking tens of milliseconds to show, though nothing about the row
        /// itself was large. GetArrayElementAtIndex reaches the same element directly, so the array segment is
        /// resolved that way instead; everything before and after it still resolves by name, exactly as before.
        /// </summary>
        SerializedProperty Resolve(string path, Anchor anchor)
        {
            var match = ArrayElementPath.Match(path);
            if (!match.Success) return serializedObject.FindProperty(path);
            var array = serializedObject.FindProperty(match.Groups[1].Value);
            if (array == null) return null;
            int index = anchor != null ? Locate(anchor, array) : int.Parse(match.Groups[2].Value);
            if (index < 0 || index >= array.arraySize) return null;
            var element = array.GetArrayElementAtIndex(index);
            return match.Groups[3].Success ? element.FindPropertyRelative(match.Groups[3].Value) : element;
        }

        /// <summary>The anchor of the list item a path runs through, or null for a path that is not under an item with an id, or for an item that has none.</summary>
        Anchor AnchorOf(string path)
        {
            if (!(serializedObject.targetObject is AvatarDoc)) return null;
            var match = ArrayElementPath.Match(path);
            if (!match.Success || Array.IndexOf(IdLists, match.Groups[1].Value) < 0) return null;
            var array = serializedObject.FindProperty(match.Groups[1].Value);
            int index = int.Parse(match.Groups[2].Value);
            if (array == null || index < 0 || index >= array.arraySize) return null;
            var id = IdAt(array, index);
            return string.IsNullOrEmpty(id) ? null : new Anchor(id, index);
        }

        static string IdAt(SerializedProperty array, int index) => array.GetArrayElementAtIndex(index).FindPropertyRelative("id")?.stringValue;

        /// <summary>Where the anchor's item sits in the array now: the place it was last found at when it is still there, else wherever its id is. -1 when no item has it any more.</summary>
        static int Locate(Anchor anchor, SerializedProperty array)
        {
            int size = array.arraySize;
            if (anchor.Index >= 0 && anchor.Index < size && IdAt(array, anchor.Index) == anchor.Id) return anchor.Index;
            for (int i = 0; i < size; i++)
                if (IdAt(array, i) == anchor.Id) return anchor.Index = i;
            return -1;
        }

        T Read<T>(string path, Anchor anchor, Func<SerializedProperty, T> read)
        {
            var property = Resolve(path, anchor);
            return property != null ? read(property) : default(T);
        }

        /// <summary>
        /// A write for a control that is not one of the fields above, such as a segmented choice with labels of its own.
        /// Like theirs it finds the item the property is under by its id when it writes, with the id noted now.
        /// </summary>
        public Action<Action<SerializedProperty>> WriterFor(SerializedProperty property, Action commit = null)
        {
            var path = property.propertyPath;
            var anchor = AnchorOf(path);
            return write => Write(path, anchor, write, commit);
        }

        /// <summary>
        /// The write every field above makes, through the path as it is given. Public so the lock guard can be taken
        /// on its own, without a panel to dispatch a field's change event through.
        /// </summary>
        public void Write(string path, Action<SerializedProperty> write, Action commit) => Write(path, null, write, commit);

        /// <summary>Refuses a locked component's entry data, and an item that is no longer in its list: a write meant for it must not land on whichever item took its place.</summary>
        void Write(string path, Anchor anchor, Action<SerializedProperty> write, Action commit)
        {
            if (Refuses(path)) return;
            serializedObject.Update();
            var property = Resolve(path, anchor);
            if (property == null) return;
            write(property);
            (commit ?? onChanged)?.Invoke();
        }

        /// <summary>
        /// Whether this write touches the entry data of a locked component. Install path, install mode, parameter
        /// prefix and platform are the buyer's settings and stay editable, so only the lists the maker owns are
        /// refused here.
        /// </summary>
        bool Refuses(string path)
        {
            var doc = serializedObject.targetObject as AvatarDoc;
            if (doc == null || !doc.locked || path == null) return false;
            return path.StartsWith(nameof(AvatarDoc.entries)) || path.StartsWith(nameof(AvatarDoc.groups))
                || path.StartsWith(nameof(AvatarDoc.menuEdits)) || path.StartsWith(nameof(AvatarDoc.removedLayers))
                || path.StartsWith(nameof(AvatarDoc.animators)) || path.StartsWith(nameof(AvatarDoc.menuAssets))
                || path.StartsWith(nameof(AvatarDoc.parameters));
        }

        void Register(VisualElement field, Action load)
        {
            load();
            loaders.Add(() => { if (!Editing(field)) load(); });
        }

        /// <summary>Whether the focus sits in this field, so a reload would pull what is being typed out from under it. Shared with the target lists, whose rows reload the same way.</summary>
        internal static bool Editing(VisualElement field)
        {
            var focused = field.panel?.focusController?.focusedElement as VisualElement;
            return focused != null && (focused == field || field.Contains(focused));
        }
    }
}
#endif
