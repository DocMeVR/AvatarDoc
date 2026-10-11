#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Takes the docs site's pictures: the view in a real window at a fixed width, every list's rows drawn, the
    /// window's tab strip and the empty space under the view cropped away, numbered marker rectangles written
    /// beside the picture, and the shot listed in the site's manifest. Each picture takes the screen's focus for a
    /// moment (see InspectorShots), so only the explicit DocsShots classes call Take, and only after the creator's OK.
    /// </summary>
    public static class DocsShot
    {
        public const string LightThemeMessage = "Docs screenshots are taken in Unity's dark theme. Switch Edit > Preferences > General > Editor Theme to Dark, then run the DocsShots tests again.";

        const string NotInFrontMessage = "The docs screenshot window never came to the front of the screen, so the picture could show another window. Move other windows away from the top left of the screen, leave the mouse and keyboard alone, then run the DocsShots tests again.";

        /// <summary>The docs site's limit for one picture, the same 250 KB its own check uses.</summary>
        public const long SizeLimitBytes = 250 * 1024;

        const string ShotsFolder = "Website~/src/assets/shots";
        const int MaxSettleFrames = 30;
        static readonly Regex KebabCase = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$");

        /// <summary>Whether the editor shows its dark theme. A test swaps it to try the refusal without touching the creator's theme.</summary>
        public static Func<bool> IsDarkTheme = () => EditorGUIUtility.isProSkin;

        /// <summary>One numbered rectangle over a picture, in the picture's own pixels from its top left corner.</summary>
        public struct Marker
        {
            public int N, X, Y, W, H;
        }

        static readonly List<(InspectorElement element, UnityEditor.Editor editor)> inspectors = new List<(InspectorElement, UnityEditor.Editor)>();

        internal static int OpenInspectors => inspectors.Count;

        /// <summary>Where the picture of <paramref name="shot"/> on <paramref name="page"/> goes: the site's shot folder inside this package. Both names are kebab case.</summary>
        public static string ShotPath(string page, string shot)
        {
            CheckName(page, nameof(page));
            CheckName(shot, nameof(shot));
            return ShotsRoot() + "/" + page + "/" + shot + ".png";
        }

        public static string ManifestPath() => ShotsRoot() + "/manifest.json";

        public static string PackageVersion()
        {
            var path = PackageRoot() + "/package.json";
            var version = (DocsJson.Parse(File.ReadAllText(path)) as DocsJson.Map)?["version"] as string;
            if (string.IsNullOrEmpty(version)) throw new InvalidOperationException("DocsShot: " + path + " has no version.");
            return version;
        }

        public static Marker ToMarker(Rect frameRect, Rect elementRect, float pixelsPerPoint, int number) => new Marker
        {
            N = number,
            X = Mathf.RoundToInt((elementRect.x - frameRect.x) * pixelsPerPoint),
            Y = Mathf.RoundToInt((elementRect.y - frameRect.y) * pixelsPerPoint),
            W = Mathf.RoundToInt(elementRect.width * pixelsPerPoint),
            H = Mathf.RoundToInt(elementRect.height * pixelsPerPoint),
        };

        /// <summary>
        /// The part of a window's picture to keep, from its top left corner: the rows from the frame's top down,
        /// which leaves out the tab strip above it, to the bottom of the view's content when that ends before the
        /// picture does. <paramref name="frameTop"/> is the frame's top in the window's panel, in points: the panel
        /// and the read both start at the top of the window, tab strip included. <paramref name="contentBottom"/>
        /// is in points from the frame's top.
        /// </summary>
        public static RectInt CropArea(float frameTop, float contentBottom, int imageWidth, int imageHeight, float pixelsPerPoint)
        {
            int top = Mathf.Clamp(Mathf.RoundToInt(frameTop * pixelsPerPoint), 0, imageHeight - 1);
            int height = imageHeight - top;
            int content = Mathf.CeilToInt(contentBottom * pixelsPerPoint);
            if (content > 0 && content < height) height = content;
            return new RectInt(0, top, imageWidth, height);
        }

        /// <summary>A warning for a scaled display, null at one pixel per point. The screen read takes one pixel per point whatever the display's scale, so a picture taken on a scaled display needs a closer look.</summary>
        public static string ScaledDisplayProblem(float pixelsPerPoint)
        {
            if (Mathf.Approximately(pixelsPerPoint, 1f)) return null;
            return "The display is scaled (" + pixelsPerPoint.ToString("0.##", CultureInfo.InvariantCulture) + " pixels per point), but the screen read takes one pixel per point. Look at the picture closely before it is committed.";
        }

        /// <summary>The marker cut to the picture's edges. A marker wholly outside the picture stops the run, naming the element.</summary>
        public static Marker InsidePicture(Marker marker, int width, int height, string elementName)
        {
            int left = Mathf.Max(0, marker.X), top = Mathf.Max(0, marker.Y);
            int right = Mathf.Min(width, marker.X + marker.W), bottom = Mathf.Min(height, marker.Y + marker.H);
            if (right <= left || bottom <= top)
                throw new InvalidOperationException("DocsShot: the marked element \"" + elementName + "\" is not in the picture: it is hidden, empty or below the window.");
            return new Marker { N = marker.N, X = left, Y = top, W = right - left, H = bottom - top };
        }

        /// <summary>The element a marker points at: the one with that name, or, for an element that has no name, the first one carrying that USS class.</summary>
        public static VisualElement FindMarked(VisualElement view, string elementName)
        {
            var element = view.Q(elementName) ?? view.Q(className: elementName);
            if (element == null)
                throw new InvalidOperationException("DocsShot: nothing named \"" + elementName + "\" or carrying that class is in the view, so it cannot be marked.");
            return element;
        }

        /// <summary>The manifest with this shot's version and date set; every other shot and field is kept, and the shots are sorted by name.</summary>
        public static string MergeManifest(string existingJson, string key, string version, string taken)
        {
            var manifest = string.IsNullOrWhiteSpace(existingJson) ? new DocsJson.Map() : DocsJson.Parse(existingJson) as DocsJson.Map;
            if (manifest == null) throw new FormatException("DocsShot: manifest.json does not hold a JSON object.");
            var shots = manifest["shots"] as DocsJson.Map ?? new DocsJson.Map();
            var entry = shots[key] as DocsJson.Map ?? new DocsJson.Map();
            entry["version"] = version;
            entry["taken"] = taken;
            shots[key] = entry;
            manifest["shots"] = shots.SortedByKey();
            return DocsJson.Write(manifest);
        }

        /// <summary>The marker file the site's Shot component reads: the picture's size in pixels, the AvatarDoc version and the markers.</summary>
        public static string MarkerJson(int width, int height, string version, IReadOnlyList<Marker> markers)
        {
            var file = new DocsJson.Map
            {
                ["width"] = width,
                ["height"] = height,
                ["version"] = version,
                ["markers"] = markers.Select(m => (object)new DocsJson.Map { ["n"] = m.N, ["x"] = m.X, ["y"] = m.Y, ["w"] = m.W, ["h"] = m.H }).ToList(),
            };
            return DocsJson.Write(file);
        }

        public static string SizeProblem(long bytes)
        {
            if (bytes <= SizeLimitBytes) return null;
            long kilobytes = (long)Math.Round(bytes / 1024.0);
            return "The picture is " + kilobytes.ToString(CultureInfo.InvariantCulture) + " KB, over the docs site's 250 KB limit. Take it with less on screen or at a smaller height.";
        }

        /// <summary>
        /// Runs the view's panel the way the editor runs it between frames, until every list has drawn its rows.
        /// A synchronous capture never reaches those frames, so a bound ListView stays empty: its binding request
        /// waits for the panel's Bindings update (VisualTreeBindingsUpdater), and a list that sizes its rows waits
        /// for a Fill it schedules on the panel's scheduler (DynamicHeightVirtualizationController.ScheduleFill).
        /// Each frame does what UIElementsUtility.UpdateSchedulers does for an editor panel, then the layout pass a
        /// repaint does: Panel.timerEventScheduler.UpdateScheduledEvents(), Panel.UpdateBindings(), then
        /// LayoutProbe.Validate (Panel.ValidateLayout, which sends GeometryChangedEvent at once, and a list makes
        /// rows for its new size from that event). Panel and TimerEventScheduler are internal types, so these
        /// members are reached by reflection. After the first frame, when the bindings have landed, every ListView is
        /// rebuilt (Rebuild drops rows made for an earlier size and makes them again for the list's current one),
        /// then frames run until the layout stops changing. A shown list that drew fewer rows than it should stops
        /// the run instead of shipping an empty or short list (see CheckRowsDrawn).
        /// </summary>
        public static void SettleLists(VisualElement view)
        {
            var panel = view.panel ?? throw new InvalidOperationException("DocsShot.SettleLists: the view is not in a window; add it to a window's rootVisualElement first.");
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var updateBindings = panel.GetType().GetMethod("UpdateBindings", Flags, null, Type.EmptyTypes, null);
            var scheduler = panel.GetType().GetProperty("timerEventScheduler", Flags)?.GetValue(panel);
            var updateScheduled = scheduler?.GetType().GetMethod("UpdateScheduledEvents", Flags, null, Type.EmptyTypes, null);
            if (updateBindings == null || updateScheduled == null)
                throw new InvalidOperationException("DocsShot.SettleLists: " + panel.GetType() + " has no UpdateBindings or timerEventScheduler.UpdateScheduledEvents in this Unity version, so list rows cannot be drawn before a capture.");

            void Frame()
            {
                updateScheduled.Invoke(scheduler, null);
                updateBindings.Invoke(panel, null);
                LayoutProbe.Validate(view);
            }

            Frame();
            foreach (var list in view.Query<ListView>().ToList()) list.Rebuild();
            string previous = null;
            for (int frame = 0; frame < MaxSettleFrames; frame++)
            {
                Frame();
                var current = LayoutProbe.Signature(view);
                if (current == previous)
                {
                    CheckRowsDrawn(view);
                    return;
                }
                previous = current;
            }
            throw new InvalidOperationException("DocsShot.SettleLists: the view was still changing after " + MaxSettleFrames + " frames.");
        }

        /// <summary>
        /// An Inspector for <paramref name="menu"/> as the creator sees it, shown in <paramref name="mode"/>, with its
        /// folds, wheel switches and first-use tips kept in memory instead of the creator's EditorPrefs. Every
        /// first-use tip counts as seen unless <paramref name="showTips"/> is set, for a page about the tips. Pass it
        /// to Take, which destroys its editor once the shot is done.
        /// </summary>
        public static InspectorElement Inspector(AvatarDoc menu, ViewMode mode = ViewMode.Advanced, bool showTips = false)
        {
            var prefs = new MemoryViewPrefs();
            ViewModes.Save(prefs, mode);
            var tips = new MemoryTipStore();
            if (!showTips)
                foreach (var kind in TipKinds.All) tips.Dismiss(kind);
            var oldViewPrefs = AvatarDocEditor.MakeViewPrefs;
            var oldTipStore = AvatarDocEditor.MakeTipStore;
            UnityEditor.Editor editor = null;
            try
            {
                AvatarDocEditor.MakeViewPrefs = () => prefs;
                AvatarDocEditor.MakeTipStore = () => tips;
                editor = UnityEditor.Editor.CreateEditor(menu);
                // The editor builds its view while the element binds, inside this constructor.
                var element = new InspectorElement(editor);
                inspectors.Add((element, editor));
                // The view built its header tip before the editor handed it the tip store; one refresh reads the store.
                element.Q<AvatarDoctorView>()?.Refresh();
                return element;
            }
            catch
            {
                if (editor != null) Object.DestroyImmediate(editor);
                throw;
            }
            finally
            {
                AvatarDocEditor.MakeViewPrefs = oldViewPrefs;
                AvatarDocEditor.MakeTipStore = oldTipStore;
            }
        }

        /// <summary>
        /// Captures <paramref name="view"/> at <paramref name="width"/> points into the picture of
        /// <paramref name="shot"/> on <paramref name="page"/>, writes its marker file and lists it in the manifest.
        /// <paramref name="arrange"/> runs before every read, the way InspectorShots' beforeCapture does, and the
        /// list rows are drawn after it. Each marker names an element by its name or, for an element without one,
        /// by a USS class it carries. The run stops with a message, writing nothing, in the light theme, when the
        /// window never came to the front, or when a marked element is missing.
        /// </summary>
        public static void Take(string page, string shot, VisualElement view, float width = 360f, float height = 900f, Action<VisualElement> arrange = null, params (string elementName, int number)[] markers)
        {
            try
            {
                if (!IsDarkTheme()) throw new InvalidOperationException(LightThemeMessage);
                if (view == null) throw new ArgumentNullException(nameof(view));
                var png = ShotPath(page, shot);
                var key = page + "/" + shot;
                var manifestPath = ManifestPath();
                var version = PackageVersion();
                // Merged before the capture, so a manifest that cannot be read stops the run before the screen is touched.
                var manifest = MergeManifest(File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : null, key, version,
                    DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                var scaled = ScaledDisplayProblem(EditorGUIUtility.pixelsPerPoint);
                if (scaled != null) Debug.LogWarning("[DocsShot] " + key + ": " + scaled);

                var recorded = new List<Marker>();
                int imageWidth = 0, imageHeight = 0;
                InspectorShots.Capture(view, png, width, height,
                    root =>
                    {
                        arrange?.Invoke(root);
                        SettleLists(root);
                    },
                    frame =>
                    {
                        var area = CropAndMark(frame, view, markers, recorded);
                        imageWidth = area.width;
                        imageHeight = area.height;
                        return area;
                    });

                var utf8 = new UTF8Encoding(false);
                File.WriteAllText(Path.ChangeExtension(png, ".json"), MarkerJson(imageWidth, imageHeight, version, recorded), utf8);
                File.WriteAllText(manifestPath, manifest, utf8);
                long bytes = new FileInfo(png).Length;
                var problem = SizeProblem(bytes);
                if (problem != null) Debug.LogWarning("[DocsShot] " + key + ": " + problem);
                Debug.Log("[DocsShot] " + key + ": " + imageWidth + " by " + imageHeight + " pixels, " + (bytes / 1024) + " KB, " + recorded.Count + (recorded.Count == 1 ? " marker." : " markers."));
            }
            finally
            {
                ReleaseInspectors(view);
            }
        }

        /// <summary>
        /// The crop of one shot, run while the window is still open: stops the run when the window was not in front
        /// for the read, then returns the part of the picture to keep and records each marker in its pixels.
        /// </summary>
        internal static RectInt CropAndMark(InspectorShots.ShotFrame frame, VisualElement view, IReadOnlyList<(string elementName, int number)> markers, List<Marker> recorded)
        {
            if (!frame.InFront) throw new InvalidOperationException(NotInFrontMessage);
            // The read takes one pixel per point of window.position, as Unity's own ScreenShotting does, so points
            // are pixels here. A scaled display is flagged by Take instead.
            const float scale = 1f;
            var frameRect = frame.Frame.worldBound;
            var area = CropArea(frameRect.y, ContentBottom(view) - frameRect.y, frame.Width, frame.Height, scale);
            recorded.Clear();
            foreach (var (elementName, number) in markers)
                recorded.Add(InsidePicture(ToMarker(frameRect, FindMarked(view, elementName).worldBound, scale, number), area.width, area.height, elementName));
            return area;
        }

        /// <summary>Destroys the editors of the inspectors made by <see cref="Inspector"/> that are, or sit inside, <paramref name="view"/>.</summary>
        internal static void ReleaseInspectors(VisualElement view)
        {
            for (int i = inspectors.Count - 1; i >= 0; i--)
            {
                var (element, editor) = inspectors[i];
                if (view == null || (element != view && !view.Contains(element))) continue;
                if (editor != null) Object.DestroyImmediate(editor);
                inspectors.RemoveAt(i);
            }
        }

        /// <summary>The bottom of what the view shows, in world points: its children's lowest edge plus its own bottom padding, so a view stretched to fill the frame still crops to its content.</summary>
        static float ContentBottom(VisualElement view)
        {
            float bottom = float.NegativeInfinity;
            foreach (var child in view.hierarchy.Children())
                if (child.resolvedStyle.display != DisplayStyle.None) bottom = Mathf.Max(bottom, child.worldBound.yMax);
            if (float.IsNegativeInfinity(bottom)) return view.worldBound.yMax;
            return Mathf.Min(view.worldBound.yMax, bottom + view.resolvedStyle.paddingBottom + view.resolvedStyle.borderBottomWidth);
        }

        /// <summary>
        /// Stops the run when a shown list did not draw what it should: every row when the list fits without its
        /// vertical scroller, or at least one row when it scrolls.
        /// </summary>
        internal static void CheckRowsDrawn(VisualElement view)
        {
            foreach (var list in view.Query<ListView>().ToList())
            {
                int count = list.itemsSource?.Count ?? 0;
                if (count == 0 || !IsShown(list)) continue;
                int shown = list.Query(className: BaseVerticalCollectionView.itemUssClassName).ToList()
                    .Count(row => row.GetFirstAncestorOfType<BaseVerticalCollectionView>() == list && row.resolvedStyle.display != DisplayStyle.None);
                var scroller = list.Q<ScrollView>()?.verticalScroller;
                bool scrolls = scroller != null && IsShown(scroller);
                if (scrolls ? shown > 0 : shown == count) continue;
                string where = !string.IsNullOrEmpty(list.name) ? " \"" + list.name + "\""
                    : list.parent != null && !string.IsNullOrEmpty(list.parent.name) ? " in \"" + list.parent.name + "\"" : "";
                throw new InvalidOperationException("DocsShot.SettleLists: a list" + where + " holds " + count + (count == 1 ? " item" : " items")
                    + " but drew " + shown + (shown == 1 ? " row" : " rows") + ", so the picture would show it " + (shown == 0 ? "empty." : "short."));
            }
        }

        static bool IsShown(VisualElement element)
        {
            for (var e = element; e != null; e = e.hierarchy.parent)
                if (e.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        static void CheckName(string name, string parameter)
        {
            if (name == null || !KebabCase.IsMatch(name))
                throw new ArgumentException("DocsShot: \"" + name + "\" is not a kebab case name (small letters and digits joined by single dashes).", parameter);
        }

        static string ShotsRoot() => PackageRoot() + "/" + ShotsFolder;

        static string PackageRoot() => QuickStart.PackageRoot() ?? throw new InvalidOperationException("DocsShot: the AvatarDoc package folder was not found.");
    }

    /// <summary>The part of JSON the shot files need. Objects keep their key order; numbers read back as double.</summary>
    static class DocsJson
    {
        public sealed class Map
        {
            readonly List<string> keys = new List<string>();
            readonly Dictionary<string, object> values = new Dictionary<string, object>();

            public IReadOnlyList<string> Keys => keys;

            public object this[string key]
            {
                get => values.TryGetValue(key, out var value) ? value : null;
                set
                {
                    if (!values.ContainsKey(key)) keys.Add(key);
                    values[key] = value;
                }
            }

            public Map SortedByKey()
            {
                var sorted = new Map();
                foreach (var key in keys.OrderBy(k => k, StringComparer.Ordinal)) sorted[key] = values[key];
                return sorted;
            }
        }

        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            int at = 0;
            var value = ReadValue(text, ref at);
            SkipSpace(text, ref at);
            if (at != text.Length) throw Error(text, at, "more text after the end");
            return value;
        }

        public static string Write(object value)
        {
            var text = new StringBuilder();
            WriteValue(text, value, 0);
            return text.Append('\n').ToString();
        }

        static object ReadValue(string s, ref int at)
        {
            SkipSpace(s, ref at);
            if (at >= s.Length) throw Error(s, at, "the text ends where a value should be");
            switch (s[at])
            {
                case '{': return ReadMap(s, ref at);
                case '[': return ReadList(s, ref at);
                case '"': return ReadString(s, ref at);
            }
            if (ReadWord(s, ref at, "true")) return true;
            if (ReadWord(s, ref at, "false")) return false;
            if (ReadWord(s, ref at, "null")) return null;
            int start = at;
            while (at < s.Length && "+-0123456789.eE".IndexOf(s[at]) >= 0) at++;
            if (at == start || !double.TryParse(s.Substring(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                throw Error(s, start, "a value should be here");
            return number;
        }

        static Map ReadMap(string s, ref int at)
        {
            var map = new Map();
            at++;
            SkipSpace(s, ref at);
            if (at < s.Length && s[at] == '}')
            {
                at++;
                return map;
            }
            while (true)
            {
                SkipSpace(s, ref at);
                if (at >= s.Length || s[at] != '"') throw Error(s, at, "a name in quotes should be here");
                var key = ReadString(s, ref at);
                SkipSpace(s, ref at);
                Expect(s, ref at, ':');
                map[key] = ReadValue(s, ref at);
                SkipSpace(s, ref at);
                if (at < s.Length && s[at] == ',')
                {
                    at++;
                    continue;
                }
                Expect(s, ref at, '}');
                return map;
            }
        }

        static List<object> ReadList(string s, ref int at)
        {
            var list = new List<object>();
            at++;
            SkipSpace(s, ref at);
            if (at < s.Length && s[at] == ']')
            {
                at++;
                return list;
            }
            while (true)
            {
                list.Add(ReadValue(s, ref at));
                SkipSpace(s, ref at);
                if (at < s.Length && s[at] == ',')
                {
                    at++;
                    continue;
                }
                Expect(s, ref at, ']');
                return list;
            }
        }

        static string ReadString(string s, ref int at)
        {
            var text = new StringBuilder();
            at++;
            while (at < s.Length)
            {
                char c = s[at++];
                if (c == '"') return text.ToString();
                if (c != '\\')
                {
                    text.Append(c);
                    continue;
                }
                if (at >= s.Length) break;
                char escape = s[at++];
                switch (escape)
                {
                    case '"': case '\\': case '/': text.Append(escape); break;
                    case 'b': text.Append('\b'); break;
                    case 'f': text.Append('\f'); break;
                    case 'n': text.Append('\n'); break;
                    case 'r': text.Append('\r'); break;
                    case 't': text.Append('\t'); break;
                    case 'u':
                        if (at + 4 > s.Length || !int.TryParse(s.Substring(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                            throw Error(s, at, "four hex digits should follow \\u");
                        text.Append((char)code);
                        at += 4;
                        break;
                    default: throw Error(s, at - 1, "unknown escape \\" + escape);
                }
            }
            throw Error(s, at, "a string is not closed");
        }

        static bool ReadWord(string s, ref int at, string word)
        {
            if (string.CompareOrdinal(s, at, word, 0, word.Length) != 0) return false;
            at += word.Length;
            return true;
        }

        static void Expect(string s, ref int at, char c)
        {
            if (at >= s.Length || s[at] != c) throw Error(s, at, "'" + c + "' should be here");
            at++;
        }

        static void SkipSpace(string s, ref int at)
        {
            while (at < s.Length && char.IsWhiteSpace(s[at])) at++;
        }

        static FormatException Error(string s, int at, string what)
        {
            int line = 1;
            for (int i = 0; i < at && i < s.Length; i++) if (s[i] == '\n') line++;
            return new FormatException("JSON line " + line + ": " + what + ".");
        }

        static void WriteValue(StringBuilder text, object value, int depth)
        {
            switch (value)
            {
                case null: text.Append("null"); break;
                case string s: WriteString(text, s); break;
                case bool b: text.Append(b ? "true" : "false"); break;
                case int i: text.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: text.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case float f: text.Append(((double)f).ToString("R", CultureInfo.InvariantCulture)); break;
                case double d: text.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case Map map: WriteMap(text, map, depth); break;
                case IList list: WriteList(text, list, depth); break;
                default: throw new ArgumentException("DocsJson cannot write a " + value.GetType().Name + ".");
            }
        }

        /// <summary>A map of plain values goes on one line, as a marker or a manifest entry reads best; any other map takes a line per key.</summary>
        static void WriteMap(StringBuilder text, Map map, int depth)
        {
            if (map.Keys.Count == 0)
            {
                text.Append("{}");
                return;
            }
            bool flat = map.Keys.All(k => !(map[k] is Map) && !(map[k] is IList));
            if (flat)
            {
                text.Append("{ ");
                for (int i = 0; i < map.Keys.Count; i++)
                {
                    if (i > 0) text.Append(", ");
                    WriteString(text, map.Keys[i]);
                    text.Append(": ");
                    WriteValue(text, map[map.Keys[i]], depth + 1);
                }
                text.Append(" }");
                return;
            }
            text.Append("{\n");
            for (int i = 0; i < map.Keys.Count; i++)
            {
                text.Append(' ', (depth + 1) * 2);
                WriteString(text, map.Keys[i]);
                text.Append(": ");
                WriteValue(text, map[map.Keys[i]], depth + 1);
                text.Append(i < map.Keys.Count - 1 ? ",\n" : "\n");
            }
            text.Append(' ', depth * 2).Append('}');
        }

        static void WriteList(StringBuilder text, IList list, int depth)
        {
            if (list.Count == 0)
            {
                text.Append("[]");
                return;
            }
            text.Append("[\n");
            for (int i = 0; i < list.Count; i++)
            {
                text.Append(' ', (depth + 1) * 2);
                WriteValue(text, list[i], depth + 1);
                text.Append(i < list.Count - 1 ? ",\n" : "\n");
            }
            text.Append(' ', depth * 2).Append(']');
        }

        static void WriteString(StringBuilder text, string s)
        {
            text.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (c < ' ') text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else text.Append(c);
                        break;
                }
            }
            text.Append('"');
        }
    }
}
#endif
