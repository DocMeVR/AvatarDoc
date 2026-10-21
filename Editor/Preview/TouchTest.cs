#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf.runtime;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// The test hand: a sphere the creator drags near the avatar in the Scene view, while the scene preview shows what
    /// the avatar's contact touch triggers do with it (TouchProbe). One hand at a time, on one avatar. It writes nothing
    /// but preview values: a Hold or Fade trigger's own while it runs, and the toggle a Flip trigger flips, all of one
    /// evaluation in one change. Every value it wrote goes back to what it was when the test ends, however it ends:
    /// Stop, Play mode, a script reload, another avatar selected, or the last AvatarDoc view of the avatar closed while
    /// something outside the avatar is selected. A value the creator changed after the hand last wrote it is the
    /// creator's and stays.
    /// </summary>
    public static class TouchTest
    {
        sealed class Session
        {
            public Transform Root;
            public readonly TestHand Hand = new TestHand();
            /// <summary>Each value the hand took over, as it was before the hand first wrote it: null for none.</summary>
            public readonly Dictionary<string, float?> Before = new Dictionary<string, float?>();
            /// <summary>What the hand last left each of those at: null for none.</summary>
            public readonly Dictionary<string, float?> Written = new Dictionary<string, float?>();
            /// <summary>The Hold and Fade triggers whose own value the hand drives.</summary>
            public readonly HashSet<string> Driven = new HashSet<string>();
            /// <summary>The Flip triggers that flipped and wait for the hand to leave, as their layer waits in Set on or Set off.</summary>
            public readonly HashSet<string> Fired = new HashSet<string>();
            public List<ProbedTrigger> Last = new List<ProbedTrigger>();
            /// <summary>When the hand last changed the preview, by Clock.</summary>
            public double LastPublish = double.NegativeInfinity;
            /// <summary>A drag moved a Fade part way sooner than PublishGap after the last change; Tick sends it.</summary>
            public bool Pending;
        }

        static Session session;
        static readonly HashSet<AvatarDoctorView> views = new HashSet<AvatarDoctorView>();
        static bool listening;
        static bool ticking;

        /// <summary>The shortest time between two changes a drag sends while only a Fade's amount moves. Every change rebuilds the scene preview of every avatar in the scene.</summary>
        const double PublishGap = 0.05;

        public static bool Running => session != null;

        /// <summary>The avatar the hand is on, null while no test runs.</summary>
        public static Transform Avatar => session?.Root;

        public static TestHand Hand => session?.Hand;

        /// <summary>What every touch trigger did with the hand at the last evaluation.</summary>
        public static IReadOnlyList<ProbedTrigger> Triggers => session != null ? (IReadOnlyList<ProbedTrigger>)session.Last : Array.Empty<ProbedTrigger>();

        /// <summary>Whether the hand listens to the Scene view and the editor events that end it. Only while it runs.</summary>
        public static bool Listening => listening;

        /// <summary>Whether a closed AvatarDoc view asked for CheckViews once the editor has settled.</summary>
        public static bool ViewCheckPending { get; private set; }

        /// <summary>Whether a drag's latest Fade amount still waits for Tick.</summary>
        public static bool PublishPending => session != null && session.Pending;

        /// <summary>What the editor has selected. A test hands in an object of its own instead of changing the creator's selection.</summary>
        public static Func<GameObject> SelectedObject = () => Selection.activeGameObject;

        /// <summary>The time in seconds the drag's spacing is measured with. A test hands in a clock of its own.</summary>
        public static Func<double> Clock = () => EditorApplication.timeSinceStartup;

        /// <summary>Raised when the hand starts, stops or changes what it stands for, for the buttons and rows that show it.</summary>
        public static event Action Changed;

        public static bool RunsOn(Transform avatarRoot) => session != null && avatarRoot != null && session.Root == avatarRoot;

        /// <summary>Whether the hand measures this trigger, and so draws its contacts itself.</summary>
        public static bool Measures(MenuEntry entry) => session != null && entry != null && session.Last.Any(t => t.Entry == entry);

        /// <summary>
        /// Starts the hand on the avatar of this component, ending any hand already running. It starts just outside the
        /// sphere of the given contact when there is one, else in front of the avatar's head. Refused in Play mode.
        /// </summary>
        public static void Start(AvatarDoc component, MenuEntry focus = null, WhenCondition at = null)
        {
            if (component == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var root = AvatarPlanCache.RootOf(component);
            if (root == null) return;
            Stop();
            session = new Session { Root = root };
            session.Hand.Position = StartPosition(root, ContactOf(focus, at));
            Listen(true);
            Evaluate();
            Changed?.Invoke();
            SceneView.RepaintAll();
        }

        /// <summary>Ends the hand and puts back every preview value it wrote that nobody changed since. A change a drag was still holding back is dropped.</summary>
        public static void Stop()
        {
            var ended = session;
            session = null;
            Listen(false);
            if (ended == null) return;
            var overrides = PreviewState.Overrides.Value;
            var restored = overrides;
            foreach (var pair in ended.Written)
            {
                bool has = overrides.TryGetValue(pair.Key, out var current);
                bool untouched = pair.Value.HasValue ? has && current == pair.Value.Value : !has;
                if (!untouched) continue;
                ended.Before.TryGetValue(pair.Key, out var before);
                restored = before.HasValue ? restored.SetItem(pair.Key, before.Value) : restored.Remove(pair.Key);
            }
            if (!ReferenceEquals(restored, overrides)) PreviewState.Overrides.Value = restored;
            Changed?.Invoke();
            SceneView.RepaintAll();
        }

        /// <summary>Measures every touch trigger against the hand again and writes what they do to the preview at once.</summary>
        public static void Evaluate() => Evaluate(false);

        /// <summary>
        /// Measures every touch trigger against the hand and writes what they do to the preview, in one change. With
        /// spacing (a drag, a Scene view frame), a change that only moves a Fade's amount waits until PublishGap has
        /// passed since the last one, and Tick sends it then; a flip, a touch that starts or ends, and every Hold go out
        /// at once.
        /// </summary>
        static void Evaluate(bool spaced)
        {
            var running = session;
            if (running == null) return;
            if (running.Root == null)
            {
                Stop();
                return;
            }
            var plan = AvatarPlanCache.Get(running.Root);
            var probed = TouchProbe.Probe(plan.Tree, plan.Plan, running.Hand);
            bool changed = !SameResult(running.Last, probed);
            running.Last = probed;
            if (changed) SceneView.RepaintAll();

            var overrides = PreviewState.Overrides.Value;
            var next = overrides;
            bool flipped = false;
            foreach (var trigger in probed.Where(t => t.Mode == TouchTestMode.Flip))
            {
                var id = trigger.Entry.id;
                if (running.Fired.Contains(id))
                {
                    if (!trigger.Met) running.Fired.Remove(id);
                    continue;
                }
                if (!trigger.Active) continue;
                var target = plan.Tree.FindControl(trigger.Entry.flipToggleId);
                if (target == null || target.Excluded || target.Entry.kind != EntryKind.Toggle) continue;
                next = PreviewState.Flipped(next, target.Entry, plan.Tree, plan.Plan);
                running.Fired.Add(id);
                flipped = true;
            }
            foreach (var gone in running.Fired.Where(id => !probed.Any(t => t.Mode == TouchTestMode.Flip && t.Entry.id == id)).ToList())
                running.Fired.Remove(gone);

            var wanted = new Dictionary<string, float?>();
            // Whole percents, the steps the hand's label shows: a finer step would only send more changes.
            foreach (var trigger in probed.Where(t => t.Mode != TouchTestMode.Flip))
                wanted[trigger.Entry.id] = trigger.Active ? (trigger.Mode == TouchTestMode.Fade ? Mathf.Round(trigger.Amount * 100f) / 100f : 1f) : (float?)null;
            // A trigger the hand drove that is gone, switched off or left out on this platform now shows nothing.
            foreach (var id in running.Driven)
                if (!wanted.ContainsKey(id)) wanted[id] = null;
            bool onlyAmounts = !flipped;
            foreach (var pair in wanted)
            {
                bool has = next.TryGetValue(pair.Key, out var current);
                if (pair.Value.HasValue)
                {
                    if (has && current == pair.Value.Value) continue;
                    // A Hold's value is always 1, so a value that only changes is a Fade's amount moving.
                    onlyAmounts &= has;
                    next = next.SetItem(pair.Key, pair.Value.Value);
                }
                else if (has)
                {
                    onlyAmounts = false;
                    next = next.Remove(pair.Key);
                }
            }

            double now = Clock();
            if (spaced && onlyAmounts && !ReferenceEquals(next, overrides) && now - running.LastPublish < PublishGap)
            {
                running.Pending = true;
                Ticking(true);
                return;
            }

            foreach (var key in overrides.Keys.Concat(next.Keys).Distinct().ToList())
            {
                bool had = overrides.TryGetValue(key, out var was);
                bool has = next.TryGetValue(key, out var value);
                if (had == has && (!had || was == value)) continue;
                if (!running.Before.ContainsKey(key)) running.Before[key] = had ? was : (float?)null;
                running.Written[key] = has ? value : (float?)null;
            }
            foreach (var pair in wanted)
            {
                running.Driven.Add(pair.Key);
                if (!running.Before.ContainsKey(pair.Key)) running.Before[pair.Key] = overrides.TryGetValue(pair.Key, out var was) ? was : (float?)null;
                running.Written[pair.Key] = pair.Value;
            }
            running.Pending = false;
            Ticking(false);
            if (ReferenceEquals(next, overrides)) return;
            running.LastPublish = now;
            PreviewState.Overrides.Value = next;
        }

        /// <summary>Sends a Fade amount a drag held back, once PublishGap has passed. Runs on every editor update while one waits.</summary>
        public static void Tick()
        {
            var running = session;
            if (running == null)
            {
                Ticking(false);
                return;
            }
            if (!running.Pending || Clock() - running.LastPublish < PublishGap) return;
            Evaluate(false);
        }

        static void Ticking(bool on)
        {
            if (on == ticking) return;
            ticking = on;
            if (on) EditorApplication.update += Tick;
            else EditorApplication.update -= Tick;
        }

        static bool SameResult(List<ProbedTrigger> a, List<ProbedTrigger> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Entry != b[i].Entry || a[i].Active != b[i].Active || a[i].Blocked != b[i].Blocked || a[i].Engaged != b[i].Engaged
                    || Mathf.RoundToInt(a[i].Amount * 100f) != Mathf.RoundToInt(b[i].Amount * 100f)) return false;
                if (a[i].Contacts.Count != b[i].Contacts.Count) return false;
                for (int c = 0; c < a[i].Contacts.Count; c++)
                    if (a[i].Contacts[c].Touched != b[i].Contacts[c].Touched || a[i].Contacts[c].Centre != b[i].Contacts[c].Centre) return false;
            }
            return true;
        }

        /// <summary>Moves the hand and writes what that does at once.</summary>
        public static void MoveHand(Vector3 position)
        {
            if (session == null) return;
            session.Hand.Position = position;
            Evaluate(false);
        }

        /// <summary>Moves the hand the way a drag in the Scene view does: a Fade's new amount goes out at most every PublishGap.</summary>
        public static void DragHand(Vector3 position)
        {
            if (session == null) return;
            session.Hand.Position = position;
            Evaluate(true);
        }

        public static void SetPart(TouchTestPart part)
        {
            if (session == null || session.Hand.Part == part) return;
            session.Hand.Part = part;
            Evaluate();
            Changed?.Invoke();
        }

        public static void SetCustomTag(string tag)
        {
            if (session == null) return;
            tag = (tag ?? "").Trim();
            if (session.Hand.CustomTag == tag) return;
            session.Hand.CustomTag = tag;
            Evaluate();
            Changed?.Invoke();
        }

        /// <summary>Whether the hand is the wearer's own, rather than another player's.</summary>
        public static void SetSelf(bool self)
        {
            if (session == null || session.Hand.Self == self) return;
            session.Hand.Self = self;
            Evaluate();
            Changed?.Invoke();
        }

        /// <summary>How tall the avatar is against VRChat's 1.6 m eye height, the scale the ready made contacts are placed by.</summary>
        static float AvatarScale(Transform root)
        {
            var descriptor = root.GetComponent<VRCAvatarDescriptor>();
            float height = descriptor != null && descriptor.ViewPosition.y > 0f ? descriptor.ViewPosition.y / 1.6f : 1f;
            return height * TouchProbe.Scale(root);
        }

        /// <summary>The hand's drawn radius: about a palm on this avatar. The measuring reads only its centre, which a dot marks.</summary>
        public static float HandRadius(Transform avatarRoot) => avatarRoot != null ? 0.035f * AvatarScale(avatarRoot) : 0.035f;

        static WhenCondition ContactOf(MenuEntry focus, WhenCondition at)
        {
            if (at != null && at.kind == WhenConditionKind.Contact && at.contactAnchor != null) return at;
            if (focus == null) return null;
            return focus.EffectiveGroups().SelectMany(g => g.group.all).FirstOrDefault(c => c != null && c.kind == WhenConditionKind.Contact && c.contactAnchor != null);
        }

        static Vector3 StartPosition(Transform root, WhenCondition contact)
        {
            float scale = AvatarScale(root);
            if (contact != null && !MenuModel.IsOutsideAvatar(root, contact.contactAnchor))
                return TouchProbe.Centre(contact) + root.forward * (TouchProbe.WorldRadius(contact) + 0.05f * scale);
            var descriptor = root.GetComponent<VRCAvatarDescriptor>();
            var eyes = descriptor != null ? root.TransformPoint(descriptor.ViewPosition) : root.position + root.up * (1.5f * scale);
            return eyes + root.forward * (0.25f * scale);
        }

        // ---- what ends the hand -----------------------------------------------------------------------------

        static void Listen(bool on)
        {
            SceneView.duringSceneGui -= OnSceneGui;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            Selection.selectionChanged -= OnSelectionChanged;
            PreviewState.Cleared -= OnPreviewCleared;
            Ticking(false);
            listening = on;
            if (!on) return;
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            Selection.selectionChanged += OnSelectionChanged;
            PreviewState.Cleared += OnPreviewCleared;
        }

        public static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) Stop();
        }

        public static void OnBeforeAssemblyReload() => Stop();

        /// <summary>Reset preview cleared every preview, the ones the hand took over included, so the end of the test brings none of them back.</summary>
        static void OnPreviewCleared()
        {
            if (session == null) return;
            foreach (var key in session.Before.Keys.ToList()) session.Before[key] = null;
        }

        /// <summary>Another avatar selected ends the hand. An object of the same avatar, or nothing, does not.</summary>
        public static void OnSelectionChanged()
        {
            if (session == null) return;
            var selected = SelectedObject?.Invoke();
            if (selected == null) return;
            var root = RuntimeUtil.FindAvatarInParents(selected.transform);
            if (root != null && root != session.Root) Stop();
        }

        /// <summary>Every AvatarDoc view says when it shows and when it closes, so the hand can end with the last view of its avatar.</summary>
        public static void NoteView(AvatarDoctorView view, bool attached)
        {
            if (view == null) return;
            if (attached)
            {
                views.Add(view);
                return;
            }
            views.Remove(view);
            if (session == null || ViewCheckPending) return;
            // The Inspector and the AvatarDoc window build a new view for the same avatar right after closing the old
            // one, so the check waits until the editor has settled.
            ViewCheckPending = true;
            EditorApplication.delayCall += CheckViews;
        }

        /// <summary>
        /// Ends the hand once no AvatarDoc view of its avatar is open and something outside the avatar is selected. A
        /// click that misses the hand selects the avatar's own mesh or nothing, which closes the Inspector's view too:
        /// that keeps the hand, and the Stop button beside it in the Scene view still ends it.
        /// </summary>
        public static void CheckViews()
        {
            ViewCheckPending = false;
            EditorApplication.delayCall -= CheckViews;
            views.RemoveWhere(v => v == null || v.panel == null);
            var running = session;
            if (running == null) return;
            if (running.Root == null)
            {
                Stop();
                return;
            }
            if (views.Any(v => v.Menu != null && AvatarPlanCache.RootOf(v.Menu) == running.Root)) return;
            var selected = SelectedObject?.Invoke();
            if (selected == null || RuntimeUtil.FindAvatarInParents(selected.transform) == running.Root) return;
            Stop();
        }

        // ---- the Scene view ------------------------------------------------------------------------------------

        static GUIStyle labelStyle;

        static Color Accent => EditorGUIUtility.isProSkin ? new Color(0.247f, 0.788f, 0.659f) : new Color(0.071f, 0.541f, 0.431f);

        static void OnSceneGui(SceneView sceneView)
        {
            if (session == null)
            {
                Listen(false);
                return;
            }
            var current = Event.current;
            if (current.type == EventType.Layout) Evaluate(true);
            var running = session;
            if (running == null || running.Root == null) return;
            float radius = HandRadius(running.Root);
            var hand = running.Hand.Position;

            if (current.type == EventType.Repaint) DrawContacts(running);

            var color = Handles.color;
            Handles.color = new Color(1f, 1f, 1f, 0.55f);
            EditorGUI.BeginChangeCheck();
            var dragged = Handles.FreeMoveHandle(hand, radius * 2f, Vector3.zero, Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck()) DragHand(dragged);
            if (session == null) return;
            if (current.type == EventType.Repaint)
            {
                // The point the contacts are measured at.
                Handles.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
                Handles.DotHandleCap(0, running.Hand.Position, Quaternion.identity, HandleUtility.GetHandleSize(running.Hand.Position) * 0.02f, EventType.Repaint);
            }
            Handles.color = color;
            var rotation = Tools.pivotRotation == PivotRotation.Local ? running.Root.rotation : Quaternion.identity;
            EditorGUI.BeginChangeCheck();
            var moved = Handles.PositionHandle(running.Hand.Position, rotation);
            if (EditorGUI.EndChangeCheck()) DragHand(moved);
            if (session == null) return;
            DrawHandPanel(running, radius);
        }

        static GUIStyle LabelStyle => labelStyle ?? (labelStyle = new GUIStyle(EditorStyles.helpBox) { fontSize = 11, richText = false, wordWrap = false });

        /// <summary>The hand's label above it, and a Stop button under the label, so the hand can be ended from the Scene view whatever the inspector shows.</summary>
        static void DrawHandPanel(Session running, float radius)
        {
            var text = new GUIContent(HandLabel(running));
            var stop = new GUIContent(L.Text("ad.ui.touch_test.stop"), L.Text("ad.ui.touch_test.stop.tip"));
            var point = HandleUtility.WorldToGUIPoint(running.Hand.Position + running.Root.up * (radius * 1.6f));
            var textSize = LabelStyle.CalcSize(text);
            var stopSize = GUI.skin.button.CalcSize(stop);
            var label = new Rect(point.x - textSize.x / 2f, point.y - textSize.y - stopSize.y - 4f, textSize.x, textSize.y);
            var button = new Rect(point.x - stopSize.x / 2f, label.yMax + 2f, stopSize.x, stopSize.y);
            Handles.BeginGUI();
            GUI.Label(label, text, LabelStyle);
            bool pressed = GUI.Button(button, stop);
            Handles.EndGUI();
            if (pressed) Stop();
        }

        static void DrawContacts(Session running)
        {
            var color = Handles.color;
            foreach (var trigger in running.Last)
                foreach (var contact in trigger.Contacts)
                {
                    if (!contact.Built) continue;
                    Handles.color = contact.Touched ? Accent : new Color(0.8f, 0.8f, 0.8f, 0.45f);
                    Handles.DrawWireDisc(contact.Centre, Vector3.up, contact.Radius);
                    Handles.DrawWireDisc(contact.Centre, Vector3.right, contact.Radius);
                    Handles.DrawWireDisc(contact.Centre, Vector3.forward, contact.Radius);
                    if (contact.Touched) Handles.Label(contact.Centre + Vector3.up * contact.Radius, trigger.Entry.PlainName, LabelStyle);
                }
            Handles.color = color;
        }

        /// <summary>The hand's own label: its name, then each trigger it is at, a Fade's with how far it is on and one held back by a requirement saying so.</summary>
        static string HandLabel(Session running)
        {
            var lines = new List<string> { L.Text("ad.ui.touch_test.hand") };
            foreach (var trigger in running.Last)
            {
                if (!trigger.Engaged) continue;
                var name = trigger.Entry.PlainName;
                if (trigger.Blocked) lines.Add(string.Format(L.Text("ad.ui.touch_test.blocked"), name));
                else if (trigger.Mode == TouchTestMode.Fade) lines.Add(string.Format(L.Text("ad.ui.touch_test.amount"), name, Mathf.RoundToInt(trigger.Amount * 100f)));
                else lines.Add(name);
            }
            return string.Join("\n", lines);
        }
    }
}
#endif
