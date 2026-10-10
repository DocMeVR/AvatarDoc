#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Live preview of a control's pieces from the Scene view camera, with Fit and Save icon.</summary>
    public sealed class IconCaptureWindow : EditorWindow
    {
        const int Size = 256;

        // A window only keeps public or serialized fields across a domain reload, and losing these would blank it.
        [SerializeField] AvatarDoc component;
        [SerializeField] string entryId = "";
        Image preview;
        Label status;
        Button fit;
        Toggle fillToggle;
        Button capture;
        SegmentedChoice backgroundMode;
        ColorField backgroundColor;
        Texture2D current;
        IconCapture.Frame? fitted;
        IconCapture.Frame lastFrame;
        double lastRender;
        /// <summary>The avatar's plan version the picture was rendered at, so a change to what it shows is noticed.</summary>
        int lastPlanVersion;
        bool detachedFill = true;
        /// <summary>The one hidden camera every render of this window goes through, made when the window opens and destroyed when it closes.</summary>
        IconCapture.Rig rig;
        /// <summary>
        /// What the last picture drew, by instance id: each target renderer and its object, every transform and
        /// object from the renderer up to its top object, and the renderer's materials. A change event naming one of
        /// them sets drawnChanged, and Poll renders again.
        /// </summary>
        readonly HashSet<int> drawn = new HashSet<int>();
        bool drawnChanged;

        const string FillPrefKey = "DocMe.AvatarDoctor.Capture.Fill";

        /// <summary>The texture the preview shows, exactly what "Save icon" would write. Read by tests.</summary>
        public Texture2D Current => current;

        /// <summary>Whether a fitted or hand framed shot is zoomed onto what is actually drawn. On by default, remembered in EditorPrefs with the window's other preferences. A detached window (a test) never reads or writes that shared key: it keeps its own choice in a field instead.</summary>
        public bool Fill
        {
            get => rootVisualElement.panel == null ? detachedFill : EditorPrefs.GetBool(FillPrefKey, true);
            set
            {
                if (rootVisualElement.panel == null) detachedFill = value;
                else EditorPrefs.SetBool(FillPrefKey, value);
                fillToggle?.SetValueWithoutNotify(value);
                RenderNow();
            }
        }

        public static void Open(AvatarDoc component, string entryId)
        {
            var window = GetWindow<IconCaptureWindow>(true, L.Text("ad.ui.capture.title"));
            window.component = component;
            window.entryId = entryId;
            window.fitted = null;
            window.minSize = new Vector2(300f, 380f);
            window.Show();
            // A window that opens for the first time builds itself in CreateGUI. One that was already open keeps
            // its panel, so it is rebuilt here for the entry it has just been pointed at.
            if (window.rootVisualElement.childCount > 0) window.Rebuild();
        }

        /// <summary>Points a window at an entry and builds its panel right away, for a test built with ScriptableObject.CreateInstance that never shows the window, so CreateGUI never runs on its own.</summary>
        public void Init(AvatarDoc component, string entryId)
        {
            this.component = component;
            this.entryId = entryId;
            fitted = null;
            Rebuild();
        }

        void CreateGUI() => Rebuild();

        void OnEnable()
        {
            // The frozen frame is not serialized, so a domain reload drops it and the preview follows the Scene view again.
            fitted = null;
            rig = new IconCapture.Rig();
            EditorApplication.update += Tick;
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            L.LanguageChanged += OnLanguageChanged;
        }

        void OnLanguageChanged()
        {
            titleContent = new GUIContent(L.Text("ad.ui.capture.title"));
            if (rootVisualElement.childCount > 0) Rebuild();
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            ObjectChangeEvents.changesPublished -= OnChangesPublished;
            L.LanguageChanged -= OnLanguageChanged;
            rig?.Dispose();
            rig = null;
            if (current != null) DestroyImmediate(current);
        }

        void Rebuild()
        {
            // A rebuild for another entry or another language keeps the background the creator chose.
            int background = backgroundMode != null ? backgroundMode.Index : 0;
            var backgroundValue = backgroundColor != null ? backgroundColor.value : Color.white;
            var root = rootVisualElement;
            root.Clear();
            root.AddToClassList("ad-root");
            root.AddToClassList(EditorGUIUtility.isProSkin ? "ad-dark" : "ad-light");
            root.AddToClassList("ad-capture");
            var uss = AvatarDoctorView.LoadStyleSheet();
            if (uss != null) root.styleSheets.Add(uss);
            preview = new Image { scaleMode = ScaleMode.ScaleToFit, name = "ad-capture-preview" };
            preview.AddToClassList("ad-capture-preview");
            root.Add(preview);
            var row = new VisualElement();
            row.AddToClassList("ad-row");
            var label = new Label(L.Text("ad.ui.capture.background")) { tooltip = L.Text("ad.ui.capture.background.tip") };
            label.AddToClassList("ad-label");
            row.Add(label);
            var modes = new List<string> { L.Text("ad.ui.capture.transparent"), L.Text("ad.ui.capture.solid") };
            backgroundMode = new SegmentedChoice(modes, background) { name = "ad-capture-background", tooltip = L.Text("ad.ui.capture.background.tip") };
            backgroundMode.Changed += _ => { backgroundColor.EnableInClassList("ad-hidden", Transparent); RenderNow(); };
            row.Add(backgroundMode);
            backgroundColor = new ColorField { value = backgroundValue, showAlpha = false, tooltip = L.Text("ad.ui.capture.color.tip") };
            backgroundColor.EnableInClassList("ad-hidden", Transparent);
            backgroundColor.RegisterValueChangedCallback(_ => RenderNow());
            row.Add(backgroundColor);
            root.Add(row);
            var buttons = new VisualElement();
            buttons.AddToClassList("ad-row");
            fit = new Button(() => { fitted = IconCapture.Fit(FrameSource(), IconCapture.BoundsOf(Targets())); RenderNow(); }) { text = L.Text("ad.ui.capture.fit"), name = "ad-capture-fit", tooltip = L.Text("ad.ui.capture.fit.tip") };
            buttons.Add(fit);
            fillToggle = new Toggle(L.Text("ad.ui.capture.fill")) { value = Fill, tooltip = L.Text("ad.ui.capture.fill.tip"), name = "ad-capture-fill" };
            fillToggle.AddToClassList("ad-compact");
            fillToggle.RegisterValueChangedCallback(e => Fill = e.newValue);
            buttons.Add(fillToggle);
            capture = new Button(Capture) { text = L.Text("ad.ui.capture.save"), name = "ad-capture-save", tooltip = L.Text("ad.ui.capture.save.tip") };
            capture.AddToClassList("ad-primary");
            capture.AddToClassList("ad-capture-save");
            buttons.Add(capture);
            root.Add(buttons);
            status = new Label();
            status.AddToClassList("ad-note");
            root.Add(status);
            RenderNow();
        }

        MenuEntry Entry => component != null ? component.FindEntry(entryId) : null;

        /// <summary>True while the background dropdown is on its first choice, which clears to alpha zero.</summary>
        bool Transparent => backgroundMode == null || backgroundMode.Index <= 0;

        List<Renderer> Targets()
        {
            var entry = Entry;
            if (entry == null || component == null) return new List<Renderer>();
            var plan = AvatarPlanCache.GetFor(component);
            return IconCapture.TargetsOf(entry, plan.Tree, plan.AvatarRoot);
        }

        /// <summary>How many times the preview was rendered. Read by the tests.</summary>
        public int Renders { get; private set; }

        /// <summary>Where the live frame comes from: the Scene view camera. A test hands it a frame of its own.</summary>
        public Func<IconCapture.Frame> FrameSource = IconCapture.FromSceneView;

        /// <summary>The time Poll measures its pauses with. A test hands it a clock of its own.</summary>
        public Func<double> Clock = () => EditorApplication.timeSinceStartup;

        void Tick()
        {
            if (rootVisualElement.panel == null || position.width <= 0f || position.height <= 0f) return;
            Poll();
        }

        /// <summary>
        /// Notes a change to something the last picture drew: a property, a component added or removed, or a new
        /// parent of a piece or of anything above it, or an edit of one of its materials. Public so a test can hand it
        /// a batch the way Unity does.
        /// </summary>
        public void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            for (int i = 0; i < stream.length && !drawnChanged; i++)
            {
                int id;
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var properties);
                        id = properties.instanceId;
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructure:
                        stream.GetChangeGameObjectStructureEvent(i, out var structure);
                        id = structure.instanceId;
                        break;
                    case ObjectChangeKind.ChangeGameObjectParent:
                        stream.GetChangeGameObjectParentEvent(i, out var parent);
                        id = parent.instanceId;
                        break;
                    case ObjectChangeKind.ChangeAssetObjectProperties:
                        stream.GetChangeAssetObjectPropertiesEvent(i, out var asset);
                        id = asset.instanceId;
                        break;
                    default:
                        continue;
                }
                if (drawn.Contains(id)) drawnChanged = true;
            }
        }

        /// <summary>Notes what a picture of these targets draws, for OnChangesPublished.</summary>
        void RememberDrawn(List<Renderer> targets)
        {
            drawn.Clear();
            drawnChanged = false;
            foreach (var renderer in targets)
            {
                if (renderer == null) continue;
                drawn.Add(renderer.GetInstanceID());
                for (var t = renderer.transform; t != null; t = t.parent)
                {
                    drawn.Add(t.GetInstanceID());
                    drawn.Add(t.gameObject.GetInstanceID());
                }
                foreach (var material in renderer.sharedMaterials)
                    if (material != null) drawn.Add(material.GetInstanceID());
            }
        }

        /// <summary>
        /// Renders again, at most five times a second, when the Scene view frame moved (a fitted frame holds still),
        /// the avatar's plan changed, or something the last picture drew changed: a piece moved, a material edited.
        /// A window left open while nothing changes draws nothing at all.
        /// </summary>
        public void Poll()
        {
            if (Clock() - lastRender < 0.2) return;
            bool moved = fitted == null && !Same(FrameSource(), lastFrame);
            if (!moved && !drawnChanged && PlanVersion() == lastPlanVersion) return;
            RenderNow();
        }

        int PlanVersion() => AvatarPlanCache.VersionFor(component != null ? AvatarPlanCache.RootOf(component) : null);

        static bool Same(IconCapture.Frame a, IconCapture.Frame b)
        {
            return a.Orthographic == b.Orthographic
                && (a.Position - b.Position).sqrMagnitude < 0.00000001f
                && Quaternion.Angle(a.Rotation, b.Rotation) < 0.01f
                && Mathf.Abs(a.FieldOfView - b.FieldOfView) < 0.01f
                && Mathf.Abs(a.OrthographicSize - b.OrthographicSize) < 0.001f;
        }

        void RenderNow() => RenderNow(IconCapture.PreviewWorkingSize);

        void RenderNow(int workingSize)
        {
            if (capture == null || preview == null || rig == null) return;
            lastRender = Clock();
            var targets = Targets();
            lastPlanVersion = PlanVersion();
            RememberDrawn(targets);
            var frame = fitted ?? FrameSource();
            lastFrame = frame;
            bool any = targets.Count > 0 && Entry != null;
            capture.SetEnabled(any);
            fit.SetEnabled(any);
            status.text = any ? "" : L.Text("ad.ui.capture.none");
            if (!any) { preview.image = null; return; }
            var background = Transparent ? new Color(0f, 0f, 0f, 0f) : backgroundColor.value;
            Renders++;
            var next = Fill ? rig.RenderFitted(frame, targets, Size, background, workingSize: workingSize) : rig.Render(frame, targets, Size, background);
            if (current != null) DestroyImmediate(current);
            current = next;
            preview.image = current;
        }

        void Capture()
        {
            var entry = Entry;
            if (entry == null || current == null) return;
            if (component.locked)
            {
                status.text = L.Text("ad.ui.locked.refused");
                return;
            }
            // The preview renders at a smaller working size while the Scene view moves; the saved icon is rendered once more at full quality from the same frame.
            RenderNow(IconCapture.WorkingSize);
            if (current == null) return;
            var plan = AvatarPlanCache.GetFor(component);
            string avatarName = plan.AvatarRoot != null ? plan.AvatarRoot.name : component.gameObject.name;
            var asset = IconCapture.Save(current, avatarName, entry.PlainName, entry.icon);
            if (IconCapture.Assign(component, entry, asset)) AvatarPlanCache.NoteSelfEdit(component);
            AvatarPlanCache.InvalidateMenu(AvatarPlanCache.RootOf(component));
            status.text = string.Format(L.Text("ad.ui.capture.saved"), AssetDatabase.GetAssetPath(asset));
        }
    }
}
#endif
