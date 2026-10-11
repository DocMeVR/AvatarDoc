#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The pictures of the first eleven guides, one per page, each at the step where a reader needs to see the panel:
    /// toggles, sliders, colours, material swaps, exclusive groups, names, Hold the rest, contacts, PhysBones, blend
    /// shape triggers and parameter triggers. Each setup builds the same state the guide's steps leave. Every picture
    /// but the menu one shows only the selected entry's panel, so it stays readable at the site's width.
    /// DocsShotSetupTests runs every setup without the screen read.
    /// </summary>
    [Explicit, Category("DocsShots")]
    public class DocsShots_Guides1
    {
        const string Page = "guides";

        /// <summary>The names the group shot gives its unnamed controls, so the markers can find them.</summary>
        internal const string GroupFieldName = "docs-group-field";
        internal const string GroupButtonName = "docs-group-button";
        internal const string DefaultOnName = "docs-default-on";

        [Test] public void ToggleClothingObjects() { using (var avatar = DocsAvatar.Create()) ToggleClothingObjects(avatar).Take(); }
        [Test] public void SlidersRadial() { using (var avatar = DocsAvatar.Create()) SlidersRadial(avatar).Take(); }
        [Test] public void ColourAndGlowProperty() { using (var avatar = DocsAvatar.Create()) ColourAndGlowProperty(avatar).Take(); }
        [Test] public void SwapMaterialsSlot() { using (var avatar = DocsAvatar.Create()) SwapMaterialsSlot(avatar).Take(); }
        [Test] public void OneOfManyGroup() { using (var avatar = DocsAvatar.Create()) OneOfManyGroup(avatar).Take(); }
        [Test] public void OrganiseTheMenuStyle() { using (var avatar = DocsAvatar.Create()) OrganiseTheMenuStyle(avatar).Take(); }
        [Test] public void HoldTheRestRule() { using (var avatar = DocsAvatar.Create()) HoldTheRestRule(avatar).Take(); }
        [Test] public void ReactToTouchFade() { using (var avatar = DocsAvatar.Create()) ReactToTouchFade(avatar).Take(); }
        [Test] public void ReactToPhysbonesEvent() { using (var avatar = DocsAvatar.Create()) ReactToPhysbonesEvent(avatar).Take(); }
        [Test] public void FollowABlendShapeSmooth() { using (var avatar = DocsAvatar.Create()) FollowABlendShapeSmooth(avatar).Take(); }
        [Test] public void LinkParameterPicker() { using (var avatar = DocsAvatar.Create()) LinkParameterPicker(avatar).Take(); }

        /// <summary>A Jacket toggle that shows the jacket and hides the hoodie: 1 its Objects section, 2 the first row's Shown or Hidden choice, 3 the Scene preview buttons.</summary>
        internal static DocsShotSpec ToggleClothingObjects(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var jacket = menu.AddEntry(EntryKind.Toggle, "Jacket");
            jacket.objects.Add(new ObjectTarget { target = avatar.Jacket, activeWhenOn = true });
            jacket.objects.Add(new ObjectTarget { target = avatar.Hoodie, activeWhenOn = false });
            return PanelShot(menu, jacket, "toggle-clothing-objects", ("ad-objects", 1), ("mode", 2), ("ad-preview-choice", 3));
        }

        /// <summary>A Smile radial on the face's Smile shape from 0 to 100: 1 its Default slider, 2 the row's Min and Max, 3 the Scene preview slider.</summary>
        internal static DocsShotSpec SlidersRadial(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var smile = menu.AddEntry(EntryKind.Radial, "Smile");
            smile.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Smile", minWeight = 0f, maxWeight = 100f });
            return PanelShot(menu, smile, "sliders-radial", ("ad-default-value", 1), ("range", 2), ("ad-preview-slider", 3));
        }

        /// <summary>A Glow toggle that sets the glasses' emission colour, as picking _EmissionColor and a colour leaves it: 1 the renderer, 2 the property button, 3 the On value.</summary>
        internal static DocsShotSpec ColourAndGlowProperty(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var glow = menu.AddEntry(EntryKind.Toggle, "Glow");
            glow.properties.Add(new MaterialPropertyTarget
            {
                renderer = avatar.Glasses.GetComponent<MeshRenderer>(),
                property = "_EmissionColor",
                type = PropertyValueType.Color,
                color = new Color(0.25f, 1f, 0.8f),
            });
            return PanelShot(menu, glow, "colour-and-glow-property", ("renderer", 1), ("property", 2), ("value", 3));
        }

        /// <summary>A Metal jacket toggle that puts the Metal material in the jacket's slot 0: 1 the renderer, 2 the slot, 3 the material.</summary>
        internal static DocsShotSpec SwapMaterialsSlot(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var metal = menu.AddEntry(EntryKind.Toggle, "Metal jacket");
            metal.materials.Add(new MaterialTarget { renderer = avatar.Jacket.GetComponent<MeshRenderer>(), slot = 0, material = avatar.Metal });
            return PanelShot(menu, metal, "swap-materials-slot", ("renderer", 1), ("slot", 2), ("material", 3));
        }

        /// <summary>
        /// Jacket and Hoodie toggles in the Outfits group, the jacket on by default, with the Hoodie selected: 1 its
        /// Exclusive group field, 2 the arrow button that lists the groups, 3 Default on. Those three controls have no
        /// names of their own, so the arrange step names them after finding them by their labels.
        /// </summary>
        internal static DocsShotSpec OneOfManyGroup(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var jacket = menu.AddEntry(EntryKind.Toggle, "Jacket");
            jacket.objects.Add(new ObjectTarget { target = avatar.Jacket });
            jacket.exclusiveGroup = "Outfits";
            jacket.defaultOn = true;
            var hoodie = menu.AddEntry(EntryKind.Toggle, "Hoodie");
            hoodie.objects.Add(new ObjectTarget { target = avatar.Hoodie });
            hoodie.exclusiveGroup = "Outfits";
            var spec = PanelShot(menu, hoodie, "one-of-many-group", (GroupFieldName, 1), (GroupButtonName, 2), (DefaultOnName, 3));
            var select = spec.Arrange;
            spec.Arrange = root =>
            {
                select(root);
                NameGroupControls(root.Q<AvatarDoctorView>().Detail);
            };
            return spec;
        }

        /// <summary>
        /// An Outfits submenu with a bold, coloured name, open in the tree with Jacket and Hoodie inside, and selected:
        /// 1 its Name field, 2 the Style row's colour swatch, 3 the In game line, 4 Capture icon. The tree stays in the
        /// picture, since it draws the styled name too; the mode row and the header are left out.
        /// </summary>
        internal static DocsShotSpec OrganiseTheMenuStyle(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var outfits = menu.AddEntry(EntryKind.Submenu, RichName.Compose("Outfits", new Color(1f, 0.62f, 0.5f), true, false));
            menu.AddEntry(EntryKind.Toggle, "Jacket", outfits.id).objects.Add(new ObjectTarget { target = avatar.Jacket });
            menu.AddEntry(EntryKind.Toggle, "Hoodie", outfits.id).objects.Add(new ObjectTarget { target = avatar.Hoodie });
            return new DocsShotSpec
            {
                Page = Page,
                Name = "organise-the-menu-style",
                View = DocsShot.Inspector(menu),
                Arrange = root =>
                {
                    var view = root.Q<AvatarDoctorView>();
                    view.Tree.SelectEntry(outfits.id);
                    view.Tree.SetExpanded(view.Tree.RowFor(outfits.id).Key, true);
                    view.Q("ad-mode-row").style.display = DisplayStyle.None;
                    view.Header.style.display = DisplayStyle.None;
                },
                Markers = new[] { ("ad-name", 1), ("ad-style-color", 2), ("ad-name-preview", 3), ("ad-capture-icon", 4) },
            };
        }

        /// <summary>
        /// A Smile toggle with its Smile row and the rule Hold the rest adds for the face, holding the rest at their
        /// default with blinking and lip sync left free: 1 Hold the rest, 2 the rule's switch, 3 Hold at, 4 Hold
        /// blink, 5 the count note. The face is the mesh the descriptor blinks and speaks with, so both switches show.
        /// </summary>
        internal static DocsShotSpec HoldTheRestRule(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var smile = menu.AddEntry(EntryKind.Toggle, "Smile");
            smile.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Smile", weight = 100f });
            EntryEditing.AddHoldRule(menu, smile, avatar.Face);
            return PanelShot(menu, smile, "hold-the-rest-rule",
                ("ad-shape-set-hold", 1), ("zero-others", 2), ("ad-shape-set-value", 3), ("hold-blink", 4), ("ad-shape-set-note", 5));
        }

        /// <summary>
        /// A Cheek poke contact trigger made by hand: anchored on the Head bone, in Fade, puffing the cheek. 1 When,
        /// 2 Mode, 3 Fully on at, 4 Anchor, 5 the + And that adds a requirement.
        /// </summary>
        internal static DocsShotSpec ReactToTouchFade(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var poke = menu.AddEntry(EntryKind.Trigger, "Cheek poke");
            poke.source = TriggerSource.Contact;
            poke.touchMode = TouchMode.FollowDistance;
            poke.contactAnchor = avatar.Test.HeadBone;
            poke.contactOffset = new Vector3(0.05f, 0.02f, 0.06f);
            poke.contactRadius = 0.04f;
            poke.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Cheek_Puff", weight = 100f });
            return PanelShot(menu, poke, "react-to-touch-fade",
                ("ad-trigger-source", 1), ("ad-touch-mode", 2), ("ad-touch-fullat", 3), ("ad-touch-anchor", 4), ("ad-when-add-and", 5));
        }

        /// <summary>A Hair pull trigger on the hair PhysBone, firing once it is stretched past 0.5 and widening the eyes: 1 When, 2 Mode, 3 the PhysBone, 4 the Event, 5 the Amount.</summary>
        internal static DocsShotSpec ReactToPhysbonesEvent(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var pull = menu.AddEntry(EntryKind.Trigger, "Hair pull");
            pull.source = TriggerSource.PhysBone;
            pull.physBone = avatar.HairBone;
            pull.physBoneEvent = PhysBoneEvent.StretchAbove;
            pull.physBoneThreshold = 0.5f;
            pull.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Eye_Wide", weight = 100f });
            return PanelShot(menu, pull, "react-to-physbones-event",
                ("ad-trigger-source", 1), ("ad-touch-mode", 2), ("ad-touch-physbone", 3), ("ad-touch-physbone-event", 4), ("ad-touch-threshold", 5));
        }

        /// <summary>A Smile blush trigger that follows the face's Smile shape in Smooth style and fades Blush in: 1 the followed shape, 2 Style, 3 Starts at, 4 Fully on at, 5 Mode.</summary>
        internal static DocsShotSpec FollowABlendShapeSmooth(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var blush = menu.AddEntry(EntryKind.Trigger, "Smile blush");
            blush.source = TriggerSource.BlendShape;
            blush.sourceRenderer = avatar.Face;
            blush.sourceShape = "Smile";
            blush.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Blush", weight = 100f, mode = ShapeEffectMode.Fixed });
            return PanelShot(menu, blush, "follow-a-blend-shape-smooth",
                ("ad-trigger-shape", 1), ("ad-trigger-style", 2), ("ad-trigger-smooth-start", 3), ("ad-trigger-smooth-full", 4), ("ad-trigger-mode", 5));
        }

        /// <summary>
        /// A trigger on the parameter GestureRight with the parameter list open under its arrow button and Gesture typed
        /// in the list's search field: 1 When, 2 the Parameter field. The list is opened through ParameterPicker's public
        /// Open with the button itself as the target, which is what its Show overloads do once they found the button;
        /// the menu sits in the window's root, outside the view, so it carries no marker of its own.
        /// </summary>
        internal static DocsShotSpec LinkParameterPicker(DocsAvatar avatar)
        {
            var menu = avatar.AddMenu();
            var thumbs = menu.AddEntry(EntryKind.Trigger, "Thumbs up blush");
            thumbs.source = TriggerSource.Parameter;
            thumbs.sourceParameter = "GestureRight";
            thumbs.condition = ParameterCondition.Equals;
            thumbs.conditionValue = 7f;
            thumbs.blendShapes.Add(new BlendShapeTarget { renderer = avatar.Face, shape = "Blush", weight = 100f });
            var spec = PanelShot(menu, thumbs, "link-to-a-toggle-or-parameter-picker", ("ad-trigger-source", 1), ("ad-trigger-parameter", 2));
            var select = spec.Arrange;
            spec.Arrange = root =>
            {
                CloseMenus(root);
                select(root);
                LayoutProbe.SettleLists(root);
                OpenParameterPicker(root, "Gesture");
            };
            return spec;
        }

        /// <summary>Opens the parameter list under the arrow button of the selected trigger's Parameter field and types into its search field. Returns the menu.</summary>
        internal static AdMenu OpenParameterPicker(VisualElement root, string search)
        {
            var view = root.Q<AvatarDoctorView>();
            var button = view.Q<TextField>("ad-trigger-parameter").parent.Children().OfType<Button>().First();
            var picker = ParameterPicker.Open(button.worldBound, button, view.Plan, _ => { });
            if (picker == null) throw new System.InvalidOperationException("The parameter list did not open: the Parameter field's arrow button is not in a window.");
            picker.SearchField.value = search;
            return picker;
        }

        /// <summary>The menus open in the window that hosts root, closed so the next arrange step starts from the panel alone.</summary>
        internal static void CloseMenus(VisualElement root)
        {
            if (root.panel == null) return;
            foreach (var open in root.panel.visualTree.Query<AdMenu>().ToList()) open.Close(false);
        }

        /// <summary>Names the Exclusive group field, its arrow button and Default on of the selected toggle's panel, found by their labels.</summary>
        internal static void NameGroupControls(VisualElement detail)
        {
            var field = detail.Query<TextField>().Where(f => f.label == L.Text("ad.ui.group")).First();
            field.name = GroupFieldName;
            field.parent.Children().OfType<Button>().First().name = GroupButtonName;
            detail.Query<Toggle>().Where(t => t.label == L.Text("ad.ui.default_on")).First().name = DefaultOnName;
        }

        /// <summary>A picture of one entry's panel alone: the entry revealed and selected, the mode row, the header and the tree left out.</summary>
        internal static DocsShotSpec PanelShot(AvatarDoc menu, MenuEntry entry, string name, params (string, int)[] markers) => new DocsShotSpec
        {
            Page = Page,
            Name = name,
            View = DocsShot.Inspector(menu),
            Arrange = root =>
            {
                var view = root.Q<AvatarDoctorView>();
                view.Tree.RevealEntry(entry.id);
                view.Q("ad-mode-row").style.display = DisplayStyle.None;
                view.Header.style.display = DisplayStyle.None;
                view.Left.style.display = DisplayStyle.None;
            },
            Markers = markers,
        };
    }
}
#endif
