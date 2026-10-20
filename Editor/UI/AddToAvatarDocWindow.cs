#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The small window Add to AvatarDoc opens: what it changes (the mesh, then its blend shape or a material slot and
    /// property, with the value it has now), what it makes (a new toggle, a new radial, or one more effect of an entry
    /// the avatar has), the values, putting the value back, Poiyomi's mark, and Add. It never changes the selection.
    /// </summary>
    public sealed class AddToAvatarDocWindow : EditorWindow
    {
        /// <summary>The width the window opens at, which the docs picture shows too.</summary>
        public const float OpeningWidth = 360f;
        const float OpeningHeight = 480f;

        /// <summary>How a list opens under its button. Replaceable by a test, which reads the items instead of showing them.</summary>
        public static Action<VisualElement, List<AdMenu.Item>> ShowMenu = (anchor, items) =>
            AdMenu.Open(anchor.worldBound, anchor, items, search: items.Count > 8 || items.Any(i => i.Children != null));

        // A window keeps only serialized fields across a script reload; the draft holds everything the creator chose.
        [SerializeField] AddDraft draft = new AddDraft();
        [SerializeField] string status = "";
        bool rebuildPending;

        public AddDraft Draft => draft;
        public string Status => status;

        public static void Show(AddDraft value)
        {
            var window = GetWindow<AddToAvatarDocWindow>(true, L.Text("ad.ui.add_from.title"));
            window.minSize = new Vector2(OpeningWidth, OpeningHeight);
            window.Init(value);
            window.Show();
        }

        /// <summary>Gives the window what it adds and builds the panel. Tests call this after ScriptableObject.CreateInstance, so no real window shows.</summary>
        public void Init(AddDraft value)
        {
            draft = value ?? new AddDraft();
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
            titleContent = new GUIContent(L.Text("ad.ui.add_from.title"));
            Rebuild();
        }

        /// <summary>An undo can take back what Add made and put the value back where it was, so the line saying what was made goes and the value now is read again.</summary>
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

        /// <summary>Makes the entry or adds the effect, as the Add button does, and says what happened under it.</summary>
        public MenuEntry Add()
        {
            var entry = AddToAvatarDoc.Apply(draft, out var said);
            status = said;
            Rebuild();
            return entry;
        }

        /// <summary>Adds an AvatarDoc component to the avatar's root, as the window's own button does.</summary>
        public void AddComponent()
        {
            AddToAvatarDoc.AddComponent(draft);
            Rebuild();
        }

        bool CanRun()
        {
            if (!AddToAvatarDoc.Picked(draft) || AddToAvatarDoc.AvatarOf(draft.renderer) == null) return false;
            return draft.make == AddMake.AddTo ? AddToAvatarDoc.Target(draft) != null : draft.component != null;
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
            var body = new ScrollView { name = "ad-add-scroll" };
            root.Add(body);

            var intro = new Label(L.Text("ad.ui.add_from.intro")) { name = "ad-add-intro" };
            intro.AddToClassList("ad-note");
            body.Add(intro);

            if (AddToAvatarDoc.AvatarOf(draft.renderer) == null)
            {
                body.Add(WarnBox("ad-add-outside", draft.renderer != null
                    ? string.Format(L.Text("ad.ui.add_from.outside"), draft.renderer.name)
                    : L.Text("ad.ui.add_from.gone")));
                LanguageFont.ApplyToChildren(root);
                return;
            }

            AddToAvatarDoc.Settle(draft);
            BuildWhat(body);
            BuildMake(body);
            if (AddToAvatarDoc.Picked(draft))
            {
                BuildValues(body);
                BuildOptions(body);
            }

            var run = new Button(() => Add()) { text = L.Text("ad.ui.add_from.add"), name = "ad-add-run", tooltip = L.Text("ad.ui.add_from.add.tip") };
            run.AddToClassList("ad-primary");
            run.AddToClassList("ad-add-run");
            run.SetEnabled(CanRun());
            body.Add(run);

            var said = new Label(status) { name = "ad-add-status" };
            said.AddToClassList("ad-note");
            body.Add(said);
            LanguageFont.ApplyToChildren(root);
        }

        // ---- what it changes ---------------------------------------------------------------------------------------

        void BuildWhat(VisualElement body)
        {
            var meshRow = Row("ad-add-mesh", "ad.ui.add_from.mesh");
            var meshName = new Label(draft.renderer.name) { name = "ad-add-mesh-name", tooltip = draft.renderer.name + "\n" + L.Text("ad.ui.add_from.mesh.tip"), displayTooltipWhenElided = false };
            meshName.AddToClassList("ad-add-value");
            meshRow.Add(meshName);
            body.Add(meshRow);

            if (AddToAvatarDoc.OffersBoth(draft))
            {
                var kindRow = Row("ad-add-kind-row", "ad.ui.add_from.kind");
                var kind = new SegmentedChoice(new[] { L.Text("ad.ui.add_from.kind.shape"), L.Text("ad.ui.add_from.kind.property") }, draft.isShape ? 0 : 1,
                    new[] { L.Text("ad.ui.add_from.kind.shape.tip"), L.Text("ad.ui.add_from.kind.property.tip") })
                {
                    name = "ad-add-kind",
                    tooltip = L.Text("ad.ui.add_from.kind.tip"),
                };
                kind.AddToClassList("ad-add-choice");
                kind.Changed += index =>
                {
                    AddToAvatarDoc.SetKind(draft, index == 0);
                    Rebuild();
                };
                kindRow.Add(kind);
                body.Add(kindRow);
            }

            if (draft.isShape)
            {
                var shapeRow = Row("ad-add-shape-row", "ad.ui.add_from.shape");
                var shape = Pick("ad-add-shape", AddToAvatarDoc.Picked(draft) ? draft.shape : L.Text("ad.ui.pick.shape"), "ad.ui.add_from.shape.tip");
                var skinned = (SkinnedMeshRenderer)draft.renderer;
                shape.clicked += () => ShowMenu(shape, ShapePicker.ItemsOf(skinned, picked =>
                {
                    AddToAvatarDoc.PickShape(draft, skinned, picked);
                    Rebuild();
                }));
                shapeRow.Add(shape);
                body.Add(shapeRow);
            }
            else
            {
                var slotRow = Row("ad-add-slot-row", "ad.ui.add_from.material");
                var slot = Pick("ad-add-slot", SlotText(draft.slot), "ad.ui.add_from.material.tip");
                slot.clicked += () => ShowMenu(slot, SlotItems());
                slotRow.Add(slot);
                body.Add(slotRow);

                var propertyRow = Row("ad-add-property-row", "ad.ui.add_from.property");
                var picked = AddToAvatarDoc.Picked(draft);
                var property = Pick("ad-add-property", picked ? AddToAvatarDoc.ValueName(draft) : L.Text("ad.ui.property.pick"), "ad.ui.add_from.property.tip");
                if (picked) property.tooltip = AddToAvatarDoc.ValueName(draft) + "\n" + draft.property + "\n" + L.Text("ad.ui.add_from.property.tip");
                property.clicked += () => ShowMenu(property, PropertyPicker.MenuItems(PropertyPicker.Choices(AddToAvatarDoc.MaterialOf(draft)), choice =>
                {
                    AddToAvatarDoc.PickProperty(draft, draft.slot, choice.PropertyName);
                    Rebuild();
                }));
                propertyRow.Add(property);
                body.Add(propertyRow);
            }

            if (!AddToAvatarDoc.Picked(draft)) return;
            var nowRow = Row("ad-add-now", "ad.ui.add_from.now");
            var now = AddToAvatarDoc.Now(draft);
            if (draft.type == PropertyValueType.Color && !draft.isShape)
            {
                var swatch = new ColorField { value = now, hdr = AddToAvatarDoc.IsHdr(draft), showAlpha = true, showEyeDropper = false, name = "ad-add-now-value", tooltip = L.Text("ad.ui.add_from.now.tip") };
                swatch.SetEnabled(false);
                nowRow.Add(swatch);
            }
            else
            {
                var text = new Label(Format(now, draft.isShape ? PropertyValueType.Float : draft.type)) { name = "ad-add-now-value", tooltip = L.Text("ad.ui.add_from.now.tip") };
                text.AddToClassList("ad-add-value");
                nowRow.Add(text);
            }
            body.Add(nowRow);
        }

        string SlotText(int slot)
        {
            var materials = draft.renderer.sharedMaterials;
            var material = slot >= 0 && slot < materials.Length ? materials[slot] : null;
            return L.Labelled(slot.ToString(CultureInfo.InvariantCulture), material != null ? material.name : L.Text("ad.ui.add_from.material.none"));
        }

        List<AdMenu.Item> SlotItems()
        {
            var items = new List<AdMenu.Item>();
            var materials = draft.renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                int slot = i;
                items.Add(new AdMenu.Item
                {
                    Text = SlotText(slot),
                    Checked = slot == draft.slot,
                    Disabled = materials[slot] == null,
                    Action = () =>
                    {
                        AddToAvatarDoc.PickSlot(draft, slot);
                        Rebuild();
                    },
                });
            }
            return items;
        }

        // ---- what it makes -----------------------------------------------------------------------------------------

        void BuildMake(VisualElement body)
        {
            var make = new SegmentedChoice(
                new[] { L.Text("ad.ui.add_from.make.toggle"), L.Text("ad.ui.add_from.make.radial"), L.Text("ad.ui.add_from.make.add_to") }, (int)draft.make,
                new[] { L.Text("ad.ui.add_from.make.toggle.tip"), L.Text("ad.ui.add_from.make.radial.tip"), L.Text("ad.ui.add_from.make.add_to.tip") })
            {
                name = "ad-add-make",
                tooltip = L.Text("ad.ui.add_from.make.tip"),
            };
            make.AddToClassList("ad-add-make");
            make.Changed += index =>
            {
                draft.make = (AddMake)index;
                draft.madeId = "";
                Rebuild();
            };
            body.Add(make);

            var components = AddToAvatarDoc.Components(draft);
            if (components.Count == 0)
            {
                var none = new VisualElement { name = "ad-add-no-component" };
                var avatar = AddToAvatarDoc.AvatarOf(draft.renderer);
                var note = new Label(string.Format(L.Text("ad.ui.add_from.no_component"), avatar != null ? avatar.name : ""));
                note.AddToClassList("ad-note");
                none.Add(note);
                var add = new Button(AddComponent) { text = L.Text("ad.ui.add_from.add_component"), name = "ad-add-component-add", tooltip = L.Text("ad.ui.add_from.add_component.tip") };
                add.AddToClassList("ad-add-component-add");
                none.Add(add);
                body.Add(none);
                return;
            }

            if (draft.make == AddMake.AddTo)
            {
                var targets = AddToAvatarDoc.Targets(draft);
                var entryRow = Row("ad-add-entry-row", "ad.ui.add_from.entry");
                var target = AddToAvatarDoc.Target(draft);
                var entry = Pick("ad-add-entry", target != null ? target.PlainName : L.Text("ad.ui.add_from.entry.pick"), "ad.ui.add_from.entry.tip");
                entry.clicked += () => ShowMenu(entry, TargetItems(targets, components.Count > 1));
                entry.SetEnabled(targets.Count > 0);
                entryRow.Add(entry);
                body.Add(entryRow);
                if (targets.Count == 0)
                {
                    var none = new Label(L.Text("ad.ui.add_from.entry.none")) { name = "ad-add-entry-none" };
                    none.AddToClassList("ad-note");
                    body.Add(none);
                }
                return;
            }

            var nameRow = new VisualElement { name = "ad-add-name-row" };
            nameRow.AddToClassList("ad-row");
            var nameField = new TextField(L.Text("ad.ui.name")) { value = draft.name, name = "ad-add-name", tooltip = L.Text("ad.ui.add_from.name.tip") };
            nameField.RegisterValueChangedCallback(e => draft.name = e.newValue);
            nameRow.Add(nameField);
            body.Add(nameRow);

            if (components.Count > 1)
            {
                var componentRow = Row("ad-add-component-row", "ad.ui.add_from.component");
                var component = Pick("ad-add-component", draft.component != null ? draft.component.gameObject.name : "", "ad.ui.add_from.component.tip");
                component.clicked += () => ShowMenu(component, components.Select(c => new AdMenu.Item
                {
                    Text = c.gameObject.name,
                    Note = EntryEditing.Refuses(c) ? L.Text("ad.ui.add_from.component.locked") : "",
                    Checked = c == draft.component,
                    Action = () =>
                    {
                        draft.component = c;
                        draft.parentId = "";
                        Rebuild();
                    },
                }).ToList());
                componentRow.Add(component);
                body.Add(componentRow);
            }

            var submenus = AddToAvatarDoc.Submenus(draft.component);
            var current = submenus.FirstOrDefault(s => s.id == draft.parentId);
            var submenuRow = Row("ad-add-submenu-row", "ad.ui.add_from.submenu");
            var submenu = Pick("ad-add-submenu", current != null ? current.PlainName : L.Text("ad.ui.menu.move.root"), "ad.ui.add_from.submenu.tip");
            submenu.clicked += () =>
            {
                var items = new List<AdMenu.Item>
                {
                    new AdMenu.Item { Text = L.Text("ad.ui.menu.move.root"), Checked = current == null, Action = () => { draft.parentId = ""; Rebuild(); } },
                };
                foreach (var s in submenus)
                {
                    var id = s.id;
                    var path = AddToAvatarDoc.PathOf(draft.component, s);
                    items.Add(new AdMenu.Item { Text = s.PlainName, Note = path == s.PlainName ? "" : path, Checked = id == draft.parentId, Action = () => { draft.parentId = id; Rebuild(); } });
                }
                ShowMenu(submenu, items);
            };
            submenuRow.Add(submenu);
            body.Add(submenuRow);
        }

        List<AdMenu.Item> TargetItems(List<(AvatarDoc component, MenuEntry entry, string path)> targets, bool severalComponents)
        {
            var items = new List<AdMenu.Item>();
            AvatarDoc heading = null;
            foreach (var (component, entry, path) in targets)
            {
                if (severalComponents && component != heading)
                {
                    heading = component;
                    items.Add(AdMenu.Item.Heading(component.gameObject.name));
                }
                var holder = component;
                var id = entry.id;
                items.Add(new AdMenu.Item
                {
                    Text = entry.PlainName,
                    Note = path == entry.PlainName ? "" : path,
                    SearchText = path,
                    Checked = draft.targetComponent == holder && draft.targetId == id,
                    Action = () =>
                    {
                        draft.targetComponent = holder;
                        draft.targetId = id;
                        Rebuild();
                    },
                });
            }
            return items;
        }

        // ---- the values --------------------------------------------------------------------------------------------

        void BuildValues(VisualElement body)
        {
            bool radial = AddToAvatarDoc.MakesRadial(draft);
            var on = ValueField("ad-add-on", L.Text(radial ? "ad.ui.max" : "ad.ui.target.on_value"), L.Text(radial ? "ad.ui.add_from.max.tip" : "ad.ui.add_from.on.tip"),
                draft.on, value => draft.on = value);
            var off = ValueField("ad-add-off", L.Text(radial ? "ad.ui.min" : "ad.ui.target.off_value"), L.Text(radial ? "ad.ui.add_from.min.tip" : "ad.ui.add_from.off.tip"),
                draft.off, value => draft.off = value);
            // A radial reads from its lowest to its highest, the order its rows in the panel use.
            if (radial)
            {
                body.Add(off);
                body.Add(on);
            }
            else
            {
                body.Add(on);
                body.Add(off);
            }
        }

        /// <summary>One value in its own row, with its own label at the column: a slider for a blend shape or a range, a number, a colour or four numbers.</summary>
        VisualElement ValueField(string fieldName, string label, string tip, Vector4 value, Action<Vector4> write)
        {
            var row = new VisualElement { name = fieldName + "-row" };
            row.AddToClassList("ad-row");
            VisualElement field;
            var type = draft.isShape ? PropertyValueType.Float : draft.type;
            switch (type)
            {
                case PropertyValueType.Color:
                {
                    var color = new ColorField(label) { value = value, hdr = AddToAvatarDoc.IsHdr(draft), showAlpha = true };
                    color.RegisterValueChangedCallback(e => write(e.newValue));
                    field = color;
                    break;
                }
                case PropertyValueType.Vector:
                {
                    var vector = new Vector4Field(label) { value = value };
                    vector.RegisterValueChangedCallback(e => write(e.newValue));
                    field = vector;
                    break;
                }
                default:
                    if (AddToAvatarDoc.Range(draft, out float min, out float max))
                    {
                        var slider = new Slider(label, min, max) { showInputField = true, value = value.x };
                        slider.RegisterValueChangedCallback(e => write(new Vector4(e.newValue, 0f, 0f, 0f)));
                        field = slider;
                    }
                    else
                    {
                        var number = new FloatField(label) { value = value.x };
                        number.RegisterValueChangedCallback(e => write(new Vector4(e.newValue, 0f, 0f, 0f)));
                        field = number;
                    }
                    break;
            }
            field.name = fieldName;
            field.tooltip = tip;
            field.AddToClassList("ad-add-field");
            row.Add(field);
            return row;
        }

        // ---- putting it back, Poiyomi ------------------------------------------------------------------------------

        void BuildOptions(VisualElement body)
        {
            bool radial = AddToAvatarDoc.MakesRadial(draft);
            var putBackRow = Row("ad-add-put-back-row", "ad.ui.add_from.after", "ad.ui.add_from.put_back.tip");
            var putBack = Check("ad-add-put-back", L.Text(radial ? "ad.ui.add_from.put_back.radial" : "ad.ui.add_from.put_back"), "ad.ui.add_from.put_back.tip", draft.putBack);
            putBack.RegisterValueChangedCallback(e => draft.putBack = e.newValue);
            putBackRow.Add(putBack);
            body.Add(putBackRow);

            if (draft.isShape) return;
            if (AddToAvatarDoc.Unmarked(draft).Count > 0)
            {
                var markRow = Row("ad-add-mark-row", "ad.ui.add_from.poiyomi", "ad.ui.add_from.mark.tip");
                var mark = Check("ad-add-mark", L.Text("ad.ui.add_from.mark"), "ad.ui.add_from.mark.tip", draft.markForPoiyomi);
                mark.RegisterValueChangedCallback(e => draft.markForPoiyomi = e.newValue);
                markRow.Add(mark);
                body.Add(markRow);
            }

            var renamed = AddToAvatarDoc.RenamedName(draft);
            if (renamed != null)
            {
                var note = new Label(string.Format(L.Text("ad.ui.add_from.renamed"), draft.property, renamed)) { name = "ad-add-renamed" };
                note.AddToClassList("ad-note");
                body.Add(note);
            }

            var baked = AddToAvatarDoc.Baked(draft);
            if (baked.Count > 0)
                body.Add(WarnBox("ad-add-baked", string.Format(L.Text(UiText.CountKey(baked.Count, "ad.ui.add_from.baked")), draft.property, baked.Count, L.JoinList(baked.Select(m => m.name)))));
        }

        // ---- building blocks ---------------------------------------------------------------------------------------

        /// <summary>A row with its caption at the label column; the caption carries the tooltip of the control after it.</summary>
        static VisualElement Row(string rowName, string labelKey, string tipKey = null)
        {
            var row = new VisualElement { name = rowName };
            row.AddToClassList("ad-row");
            var caption = new Label(L.Text(labelKey)) { tooltip = L.Text(tipKey ?? labelKey + ".tip") };
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

        static string Format(Vector4 value, PropertyValueType type)
        {
            string Number(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);
            if (type == PropertyValueType.Vector) return L.Brackets(L.JoinList(new[] { Number(value.x), Number(value.y), Number(value.z), Number(value.w) }));
            return Number(value.x);
        }
    }
}
#endif
