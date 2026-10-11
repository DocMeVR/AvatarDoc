#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The pictures of the first twelve Reference pages: the inspector, toggles, radials, submenus, exclusive groups,
    /// objects, blend shape rows, material properties, material slots, triggers, touch triggers and blend shape
    /// triggers. One overview per page, plus the header with its folds open and a toggle's Advanced fold, which a
    /// closed fold hides. The inspector's own overview is DocsShots_Home.InspectorOverview. Each picture shows a
    /// healthy avatar except the header one, which needs a warning to show the problem list.
    /// DocsShotSetupTests runs every setup without the screen read.
    /// </summary>
    [Explicit, Category("DocsShots")]
    public class DocsShots_Reference1
    {
        /// <summary>The names the shots give unnamed elements, so the markers can find them.</summary>
        internal const string SavedName = "docs-saved";
        internal const string SyncedName = "docs-synced";
        internal const string SlotsNoteName = "docs-slots-note";
        internal const string GroupChipName = "docs-group-chip";
        internal const string SubmenuRowName = "docs-submenu-row";
        internal const string DropAreaName = "docs-objects-drop";

        [Test] public void InspectorHeader() { using (var avatar = DocsAvatar.Create()) InspectorHeader(avatar).Take(); }
        [Test] public void ToggleOverview() { using (var avatar = DocsAvatar.Create()) ToggleOverview(avatar).Take(); }
        [Test] public void ToggleAdvanced() { using (var avatar = DocsAvatar.Create()) ToggleAdvanced(avatar).Take(); }
        [Test] public void RadialOverview() { using (var avatar = DocsAvatar.Create()) RadialOverview(avatar).Take(); }
        [Test] public void SubmenuOverview() { using (var avatar = DocsAvatar.Create()) SubmenuOverview(avatar).Take(); }
        [Test] public void ExclusiveGroupsOverview() { using (var avatar = DocsAvatar.Create()) ExclusiveGroupsOverview(avatar).Take(); }
        [Test] public void ObjectsOverview() { using (var avatar = DocsAvatar.Create()) ObjectsOverview(avatar).Take(); }
        [Test] public void BlendShapesOverview() { using (var avatar = DocsAvatar.Create()) BlendShapesOverview(avatar).Take(); }
        [Test] public void MaterialPropertiesOverview() { using (var avatar = DocsAvatar.Create()) MaterialPropertiesOverview(avatar).Take(); }
        [Test] public void MaterialSlotsOverview() { using (var avatar = DocsAvatar.Create()) MaterialSlotsOverview(avatar).Take(); }
        [Test] public void TriggersOverview() { using (var avatar = DocsAvatar.Create()) TriggersOverview(avatar).Take(); }
        [Test] public void TouchTriggersOverview() { using (var avatar = DocsAvatar.Create()) TouchTriggersOverview(avatar).Take(); }
        [Test] public void BlendShapeTriggersOverview() { using (var avatar = DocsAvatar.Create()) BlendShapeTriggersOverview(avatar).Take(); }

        /// <summary>
        /// The header alone, with the problem list and Component settings open, on a menu whose Scarf toggle has no
        /// targets yet: 1 the Simple and Advanced switch, 2 the language, 3 the status, 4 Show, 5 Summary, 6 the
        /// problem list, 7 the Component settings fold, 8 its settings. The tree and the panel are left out.
        /// </summary>
        internal static DocsShotSpec InspectorHeader(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            menu.AddEntry(EntryKind.Toggle, "Hat").objects.Add(new ObjectTarget { target = avatar.Hat });
            menu.AddEntry(EntryKind.Toggle, "Scarf");
            return new DocsShotSpec
            {
                Page = "reference-inspector",
                Name = "header",
                View = DocsShot.Inspector(menu),
                Arrange = root =>
                {
                    var view = root.Q<AvatarDoctorView>();
                    view.Content.style.display = DisplayStyle.None;
                    if (!view.Header.ProblemsOpen) view.Header.ToggleProblemsOpen();
                    if (!view.Header.SettingsFoldOpen) view.Header.ToggleSettingsFold();
                },
                Markers = new[]
                {
                    ("ad-mode", 1), ("ad-language", 2), ("ad-ready-label", 3), ("ad-ready-show", 4), ("ad-summary-toggle", 5),
                    ("ad-problems-list", 6), ("ad-settings-fold-header", 7), ("ad-settings-fold-content", 8),
                },
            };
        }

        /// <summary>A Hat toggle that shows the hat: 1 Name, 2 the Builds line, 3 Icon, 4 Exclusive group, 5 Default on and In menu, 6 Objects, 7 + Add effect, 8 Scene preview.</summary>
        internal static DocsShotSpec ToggleOverview(DocsAvatar avatar)
        {
            var (menu, hat) = HatToggle(avatar);
            return Panel("reference-toggle", "overview", menu, hat,
                ("ad-name", 1), ("ad-builds-line", 2), ("ad-icon", 3), ("ad-group", 4), ("ad-flags", 5), ("ad-objects", 6), ("ad-add-effect", 7), ("ad-preview-choice", 8));
        }

        /// <summary>
        /// The same Hat toggle with its Advanced fold open, every setting in it at its default: 1 the fold, 2 Platform,
        /// 3 Saved, 4 Synced, 5 the cost beside Synced, 6 Blend time, 7 the parameter's name, 8 Custom name.
        /// </summary>
        internal static DocsShotSpec ToggleAdvanced(DocsAvatar avatar)
        {
            var (menu, hat) = HatToggle(avatar);
            var spec = Panel("reference-toggle", "advanced", menu, hat,
                ("ad-advanced-fold-toggle", 1), ("ad-entry-platform", 2), (SavedName, 3), (SyncedName, 4), ("ad-synced-cost", 5),
                ("ad-blend", 6), ("ad-parameter-name", 7), ("ad-parameter-custom", 8));
            var select = spec.Arrange;
            spec.Arrange = root =>
            {
                select(root);
                var view = root.Q<AvatarDoctorView>();
                DocsShots_Guides2.OpenAdvancedFold(view.Detail);
                LayoutProbe.SettleLists(view);
                NameToggle(view.Detail, "ad.ui.saved", SavedName);
                NameToggle(view.Detail, "ad.ui.synced", SyncedName);
            };
            return spec;
        }

        /// <summary>A Smile radial on the face's Smile shape from 0 to 100 that starts a quarter of the way: 1 Icon, 2 Default, 3 In menu, 4 Blendshapes, 5 the row's Min and Max, 6 + Add effect, 7 the Scene preview slider, 8 the Advanced fold.</summary>
        internal static DocsShotSpec RadialOverview(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var smile = menu.AddEntry(EntryKind.Radial, "Smile");
            smile.defaultValue = 0.25f;
            smile.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Smile", minWeight = 0f, maxWeight = 100f });
            return Panel("reference-radial", "overview", menu, smile,
                ("ad-icon", 1), ("ad-default-value", 2), ("ad-flags", 3), ("ad-blendshapes", 4), ("range", 5), ("ad-add-effect", 6), ("ad-preview-slider", 7), ("ad-advanced-fold", 8));
        }

        /// <summary>
        /// An Outfits submenu with Jacket, Hoodie and Shoes inside, open in the tree and selected: 1 its row, 2 Name,
        /// 3 the Builds line, 4 Icon, 5 Capture icon, 6 the line that counts its places, 7 the Advanced fold.
        /// </summary>
        internal static DocsShotSpec SubmenuOverview(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, "Outfits");
            menu.AddEntry(EntryKind.Toggle, "Jacket", outfits.id).objects.Add(new ObjectTarget { target = avatar.Jacket });
            menu.AddEntry(EntryKind.Toggle, "Hoodie", outfits.id).objects.Add(new ObjectTarget { target = avatar.Hoodie });
            menu.AddEntry(EntryKind.Toggle, "Shoes", outfits.id).objects.Add(new ObjectTarget { target = avatar.Shoes });
            var spec = DocsShots_Guides2.TreeAndPanelShot(menu, view =>
            {
                view.Tree.SelectEntry(outfits.id);
                view.Tree.SetExpanded(view.Tree.RowFor(outfits.id).Key, true);
                LayoutProbe.SettleLists(view);
                NameTreeRow(view.Tree, "Outfits", SubmenuRowName);
                NameNote(view.Detail, string.Format(L.Text("ad.ui.slots"), 3, 8), SlotsNoteName);
            }, "overview",
                (SubmenuRowName, 1), ("ad-name", 2), ("ad-builds-line", 3), ("ad-icon", 4), ("ad-capture-icon", 5), (SlotsNoteName, 6), ("ad-advanced-fold", 7));
            spec.Page = "reference-submenu";
            return spec;
        }

        /// <summary>
        /// Jacket, Hoodie and No outfit in the Outfits group, the jacket on by default and No outfit with no targets,
        /// with the Hoodie selected and its Advanced fold open: 1 the group chip on the Jacket's row, 2 Exclusive group,
        /// 3 the arrow button that lists the groups, 4 Default on, 5 the note that the group is saved and synced, 6 the
        /// note in place of Blend time.
        /// </summary>
        internal static DocsShotSpec ExclusiveGroupsOverview(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var jacket = menu.AddEntry(EntryKind.Toggle, "Jacket");
            jacket.objects.Add(new ObjectTarget { target = avatar.Jacket });
            jacket.exclusiveGroup = "Outfits";
            jacket.defaultOn = true;
            var hoodie = menu.AddEntry(EntryKind.Toggle, "Hoodie");
            hoodie.objects.Add(new ObjectTarget { target = avatar.Hoodie });
            hoodie.exclusiveGroup = "Outfits";
            menu.AddEntry(EntryKind.Toggle, "No outfit").exclusiveGroup = "Outfits";
            var spec = DocsShots_Guides2.TreeAndPanelShot(menu, view =>
            {
                view.Tree.RevealEntry(hoodie.id);
                DocsShots_Guides2.OpenAdvancedFold(view.Detail);
                LayoutProbe.SettleLists(view);
                DocsShots_Guides2.NameChip(view.Tree, "Outfits 1", GroupChipName);
            }, "overview",
                (GroupChipName, 1), ("ad-group", 2), ("ad-group-pick", 3), ("ad-default-on", 4), ("ad-flags-note", 5), ("ad-blend-note", 6));
            spec.Page = "reference-exclusive-groups";
            return spec;
        }

        /// <summary>A Jacket toggle that shows the jacket and hides the hoodie: 1 Objects, 2 the first row's object, 3 its Shown or Hidden choice, 4 its remove button, 5 the drop area.</summary>
        internal static DocsShotSpec ObjectsOverview(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var jacket = menu.AddEntry(EntryKind.Toggle, "Jacket");
            jacket.objects.Add(new ObjectTarget { target = avatar.Jacket, activeWhenOn = true });
            jacket.objects.Add(new ObjectTarget { target = avatar.Hoodie, activeWhenOn = false });
            var spec = Panel("reference-objects", "overview", menu, jacket,
                ("ad-objects", 1), ("target", 2), ("mode", 3), ("remove", 4), (DropAreaName, 5));
            var select = spec.Arrange;
            spec.Arrange = root =>
            {
                select(root);
                // The welcome card in the tree carries the same class, so the section's own drop area gets a name.
                var hint = root.Q<AvatarDoctorView>().Detail.Q("ad-objects").Q(className: "ad-drop-hint")
                    ?? throw new InvalidOperationException("The Objects section shows no drop area.");
                hint.name = DropAreaName;
            };
            return spec;
        }

        /// <summary>
        /// A Smile toggle with a Fixed Smile row that sets its own off value, a Pin Smile_Eyes row and the rule Hold the
        /// rest adds for the face: 1 Capture, 2 Hold the rest, 3 the first row's shape, 4 Fixed or Pin, 5 Weight, 6 Set a
        /// value when off, 7 Off value, 8 the rule.
        /// </summary>
        internal static DocsShotSpec BlendShapesOverview(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var smile = menu.AddEntry(EntryKind.Toggle, "Smile");
            smile.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Smile", weight = 100f, mode = ShapeEffectMode.Fixed, explicitOff = true, minWeight = 10f });
            smile.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Smile_Eyes", weight = 80f, mode = ShapeEffectMode.Pin });
            EntryEditing.AddHoldRule(menu, smile, avatar.Face);
            return Panel("reference-blend-shapes", "overview", menu, smile,
                ("ad-shape-set-capture", 1), ("ad-shape-set-hold", 2), ("shape", 3), ("effect-mode", 4), ("weight", 5), ("explicit-off", 6), ("off-value", 7), ("ad-shape-set-row", 8));
        }

        /// <summary>
        /// A Glow toggle that sets the glasses' emission colour, with an off value of its own, and their smoothness: 1 the
        /// first row's renderer, 2 its property, 3 On value, 4 Set a value when off, 5 Off value.
        /// </summary>
        internal static DocsShotSpec MaterialPropertiesOverview(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var glow = menu.AddEntry(EntryKind.Toggle, "Glow");
            var glasses = avatar.Glasses.GetComponent<MeshRenderer>();
            glow.properties.Add(new MaterialPropertyTarget
            {
                renderer = glasses,
                property = "_EmissionColor",
                type = PropertyValueType.Color,
                color = new Color(0.25f, 1f, 0.8f),
                explicitOff = true,
                minColor = Color.black,
            });
            glow.properties.Add(new MaterialPropertyTarget { renderer = glasses, property = "_Glossiness", type = PropertyValueType.Float, value = 0.9f });
            return Panel("reference-material-properties", "overview", menu, glow,
                ("renderer", 1), ("property", 2), ("value", 3), ("explicit-off", 4), ("off-value", 5));
        }

        /// <summary>A Metal look toggle that puts the Metal material in slot 0 of the jacket and of the shoes: 1 Materials, 2 the first row's renderer, 3 Slot, 4 the material.</summary>
        internal static DocsShotSpec MaterialSlotsOverview(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var metal = menu.AddEntry(EntryKind.Toggle, "Metal look");
            metal.materials.Add(new MaterialTarget { renderer = avatar.Jacket.GetComponent<MeshRenderer>(), slot = 0, material = avatar.Metal });
            metal.materials.Add(new MaterialTarget { renderer = avatar.Shoes.GetComponent<MeshRenderer>(), slot = 0, material = avatar.Metal });
            return Panel("reference-material-slots", "overview", menu, metal,
                ("ad-materials", 1), ("renderer", 2), ("slot", 3), ("material", 4));
        }

        /// <summary>
        /// A Hide hair trigger that fires while the Hat is on or the Hoodie is on, only while its Hair hiding switch is
        /// on, and hides the hair: 1 Fires, 2 the first card, 3 the kind of its row, 4 the card's + And, 5 Any of these,
        /// 6 + Or, 7 Only while, 8 Objects.
        /// </summary>
        internal static DocsShotSpec TriggersOverview(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            var hoodie = menu.AddEntry(EntryKind.Toggle, "Hoodie");
            hoodie.objects.Add(new ObjectTarget { target = avatar.Hoodie });
            var hairHiding = menu.AddEntry(EntryKind.Toggle, "Hair hiding");
            hairHiding.defaultOn = true;
            var hide = menu.AddEntry(EntryKind.Trigger, "Hide hair");
            hide.when.Add(new ConditionGroup { all = { new WhenCondition { kind = WhenConditionKind.Toggle, sourceToggleId = hat.id, sourceToggleOn = true } } });
            hide.when.Add(new ConditionGroup { all = { new WhenCondition { kind = WhenConditionKind.Toggle, sourceToggleId = hoodie.id, sourceToggleOn = true } } });
            hide.gateToggleId = hairHiding.id;
            hide.objects.Add(new ObjectTarget { target = avatar.HairBone.gameObject, activeWhenOn = false });
            return Panel("reference-triggers", "overview", menu, hide,
                ("ad-when-sentence", 1), ("ad-when-card", 2), ("ad-when-kind", 3), ("ad-when-add-and-0", 4), ("ad-when-any", 5), ("ad-when-add-or", 6), ("ad-trigger-gate", 7), ("ad-objects", 8));
        }

        /// <summary>
        /// A Nose boop contact on the Head bone in Hold mode that fires once a finger or hand is closer than half its
        /// radius, and blushes: 1 When, 2 Mode, 3 Anchor, 4 Radius, 5 Offset, 6 Touched by, 7 Who, 8 Distance.
        /// </summary>
        internal static DocsShotSpec TouchTriggersOverview(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var boop = menu.AddEntry(EntryKind.Trigger, "Nose boop");
            boop.source = TriggerSource.Contact;
            boop.touchMode = TouchMode.Hold;
            boop.contactAnchor = avatar.Test.HeadBone;
            boop.contactOffset = new Vector3(0f, 0.05f, 0.09f);
            boop.contactRadius = 0.03f;
            boop.contactProximity = 0.5f;
            boop.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Blush", weight = 100f });
            return Panel("reference-touch-triggers", "overview", menu, boop,
                ("ad-trigger-source", 1), ("ad-touch-mode", 2), ("ad-touch-anchor", 3), ("ad-touch-radius", 4), ("ad-touch-offset", 5), ("ad-touch-tag-flags", 6), ("ad-touch-self", 7), ("ad-touch-distance", 8));
        }

        /// <summary>
        /// A Smile blush trigger that follows the face's Smile shape in Smooth style from 20 to 80, with a Blush row that
        /// copies the smile at 0.6: 1 When, 2 the followed shape, 3 Style, 4 Starts at, 5 Fully on at, 6 Swaps at,
        /// 7 Mode, 8 the row's Fixed weight or Follow source.
        /// </summary>
        internal static DocsShotSpec BlendShapeTriggersOverview(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var blush = menu.AddEntry(EntryKind.Trigger, "Smile blush");
            blush.source = TriggerSource.BlendShape;
            blush.sourceRenderer = avatar.Face;
            blush.sourceShape = "Smile";
            blush.smoothStart = 20f;
            blush.smoothFull = 80f;
            blush.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Follow, multiplier = 0.6f });
            return Panel("reference-blend-shape-triggers", "overview", menu, blush,
                ("ad-trigger-source", 1), ("ad-trigger-shape", 2), ("ad-trigger-style", 3), ("ad-trigger-smooth-start", 4), ("ad-trigger-smooth-full", 5),
                ("ad-trigger-threshold", 6), ("ad-trigger-mode", 7), ("effect-mode", 8));
        }

        static (AvatarDoc menu, MenuEntry hat) HatToggle(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var hat = menu.AddEntry(EntryKind.Toggle, "Hat");
            hat.objects.Add(new ObjectTarget { target = avatar.Hat });
            return (menu, hat);
        }

        /// <summary>One entry's panel alone, on a Reference page: the guides' panel picture with this page's folder.</summary>
        static DocsShotSpec Panel(string page, string name, AvatarDoc menu, MenuEntry entry, params (string, int)[] markers)
        {
            var spec = DocsShots_Guides1.PanelShot(menu, entry, name, markers);
            spec.Page = page;
            return spec;
        }

        /// <summary>Names the toggle in the panel that carries this label, so a marker can find it.</summary>
        static void NameToggle(VisualElement panel, string labelKey, string name)
        {
            var text = L.Text(labelKey);
            var toggle = panel.Query<Toggle>().Where(t => t.label == text).First()
                ?? throw new InvalidOperationException("The panel shows no switch labelled \"" + text + "\".");
            toggle.name = name;
        }

        /// <summary>Names the note in the panel that reads text.</summary>
        internal static void NameNote(VisualElement panel, string text, string name)
        {
            var note = panel.Query<Label>(className: "ad-note").Where(l => l.text == text).First()
                ?? throw new InvalidOperationException("The panel shows no note reading \"" + text + "\".");
            note.name = name;
        }

        /// <summary>Names the drawn tree row whose name reads text. The rows must be drawn first.</summary>
        internal static void NameTreeRow(VisualElement tree, string text, string name)
        {
            var row = tree.Query(className: "ad-tree-row").ToList().FirstOrDefault(r => r.Q<Label>(className: "ad-name")?.text == text)
                ?? throw new InvalidOperationException("No drawn row of the tree reads \"" + text + "\".");
            row.name = name;
        }
    }
}
#endif
