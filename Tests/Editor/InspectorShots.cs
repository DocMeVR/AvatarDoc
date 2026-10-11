#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Screenshots a real, on screen UI Toolkit view for a design review, through Unity's own screen buffer
    /// (InternalEditorUtility.ReadScreenPixel) rather than a mock render, so what lands in the PNG is pixel for
    /// pixel what a human would see. Call it from a TEMPORARY test file for a design review, or through
    /// DocsShot.Take from the explicit DocsShots classes for the docs site. For a review: build a throwaway
    /// AvatarDoc on a TestAvatar, build a fresh AvatarDoctorView per width (a reused view keeps its first window's
    /// resolved Yoga sizes on a second, differently sized pass), call Capture(view, path, width) for each shot,
    /// then view.RemoveFromHierarchy(), and delete the temporary test file and its .meta again once the review is
    /// done. See Tests/Editor/InspectorShotsExample.cs.txt for a minimal copy and paste starting point.
    ///
    /// Every call parks a throwaway EditorWindow at a fixed screen position and forces it to the front with plain
    /// Win32 calls, because a screen read only ever shows whatever is actually topmost there and Unity's own
    /// EditorWindow.Focus() cannot reliably win that fight from an automated call (Windows' anti focus stealing
    /// rule silently refuses to hand real OS foreground to a background process on its own say so). That means
    /// each call steals the machine's foreground focus, however briefly, from whatever the person at the keyboard
    /// was doing, once per capture; it always lets go again (the window is closed in a finally block) but the
    /// interruption itself is real, so this is for a deliberate, short review run, not something to leave looping.
    /// </summary>
    public static class InspectorShots
    {
        static readonly Vector2 ScreenPos = new Vector2(100f, 100f);

        static readonly MethodInfo RepaintImmediatelyMethod =
            typeof(EditorWindow).GetMethod("RepaintImmediately", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        class HostWindow : EditorWindow { }

        /// <summary>What a crop sees right after the read: the host window and the frame around the view, still open and laid out as in the picture, the picture's size in pixels, and whether the window held the front of the screen for the read.</summary>
        public sealed class ShotFrame
        {
            public readonly EditorWindow Window;
            public readonly VisualElement Frame;
            public readonly int Width;
            public readonly int Height;
            public readonly bool InFront;

            internal ShotFrame(EditorWindow window, VisualElement frame, int width, int height, bool inFront)
            {
                Window = window;
                Frame = frame;
                Width = width;
                Height = height;
                InFront = inFront;
            }
        }

        /// <summary>
        /// Hosts <paramref name="view"/> in a throwaway, pinned size EditorWindow at a fixed screen position, raises
        /// it above whatever else is on screen, reads its real pixels and writes them to <paramref name="pngPath"/>
        /// (its containing folder is created if missing), then always closes the window. Returns
        /// <paramref name="pngPath"/>. <paramref name="beforeCapture"/>, when given, runs once right after the
        /// window opens and again before every retry's read: raising the window back to OS foreground during a
        /// retry can itself reset which element the panel thinks is focused or selected, silently undoing state the
        /// caller only set once before calling this, so anything transient (a row selected, a field focused, a fold
        /// opened, the panel's own frames run so list rows draw) belongs in there rather than only being set before
        /// the call. <paramref name="crop"/>, when given, runs once after the read while the window is still open and
        /// returns the part of the picture to keep, measured from its top left corner; it may throw to stop the
        /// capture, and then nothing is written. Without it the whole window is kept.
        /// </summary>
        public static string Capture(VisualElement view, string pngPath, float width, float height = 900f, Action<VisualElement> beforeCapture = null, Func<ShotFrame, RectInt> crop = null)
        {
            InHostWindow(view, width, height, (window, frame) => ReadAndWrite(window, frame, view, pngPath, beforeCapture, crop));
            return pngPath;
        }

        /// <summary>
        /// Opens the throwaway host window Capture reads: <paramref name="view"/> inside a frame exactly
        /// <paramref name="width"/> points wide, the window pinned to <paramref name="width"/> by
        /// <paramref name="height"/> at a fixed screen position, shown, focused and laid out. Runs
        /// <paramref name="inWindow"/> with the window and the frame, then always closes the window. It reads no
        /// pixels, so a normal test can measure the same window a capture would read.
        /// </summary>
        public static void InHostWindow(VisualElement view, float width, float height, Action<EditorWindow, VisualElement> inWindow)
        {
            var frame = new VisualElement { name = "inspector-shot-frame" };
            frame.style.position = Position.Absolute;
            frame.style.left = 0f;
            frame.style.top = 0f;
            frame.style.width = width;
            frame.style.minHeight = height;
            frame.style.flexDirection = FlexDirection.Column;
            frame.Add(view);

            var window = ScriptableObject.CreateInstance<HostWindow>();
            try
            {
                window.titleContent = new GUIContent("InspectorShotsHost_" + Guid.NewGuid().ToString("N"));
                var rect = new Rect(ScreenPos.x, ScreenPos.y, width, height);
                // Show() restores whatever size Unity last remembered for this EditorWindow TYPE, ignoring the size
                // just set on it, so minSize and maxSize pin it to the exact rect and position is set again after
                // Show() in case showing moved it.
                window.position = rect;
                window.minSize = new Vector2(width, height);
                window.maxSize = new Vector2(width, height);
                window.Show();
                window.position = rect;
                window.rootVisualElement.Add(frame);
                window.Focus();
                LayoutProbe.Validate(view);
                if (Mathf.Abs(frame.worldBound.width - width) > 0.5f)
                    throw new InvalidOperationException("InspectorShots.Capture: asked for " + width + " points but the frame laid out at " + frame.worldBound.width + ".");
                if (Mathf.Abs(window.position.width - width) > 0.5f)
                    throw new InvalidOperationException("InspectorShots.Capture: the real OS window is " + window.position.width + " points wide, not the requested " + width + "; a screen capture at this width would be wrong.");
                inWindow(window, frame);
            }
            finally
            {
                frame.RemoveFromHierarchy();
                window.Close();
            }
        }

        /// <summary>Raises the open host window to the front, reads its pixels until a read is confirmed in front or five seconds pass, crops the picture and writes it.</summary>
        static void ReadAndWrite(EditorWindow window, VisualElement frame, VisualElement view, string pngPath, Action<VisualElement> beforeCapture, Func<ShotFrame, RectInt> crop)
        {
            string titleMarker = window.titleContent.text;
            IntPtr hwnd = IntPtr.Zero;

            void RaiseToFront()
            {
                if (hwnd == IntPtr.Zero) hwnd = FindByTitleSubstring(titleMarker);
                if (hwnd == IntPtr.Zero) return;
                Raise(hwnd);
            }

            void ForceRepaint()
            {
                if (RepaintImmediatelyMethod != null) RepaintImmediatelyMethod.Invoke(window, null);
                else window.Repaint();
            }

            // One pixel per point of window.position, the way Unity's own ScreenShotting reads a window.
            Texture2D CapturePixels()
            {
                var rect = window.position;
                int w = Mathf.Max(1, (int)rect.width);
                int h = Mathf.Max(1, (int)rect.height);
                var pixels = InternalEditorUtility.ReadScreenPixel(new Vector2(rect.x, rect.y), w, h);
                var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
                read.SetPixels(pixels);
                read.Apply();
                return read;
            }

            Texture2D tex = null;
            try
            {
                RaiseToFront();
                ForceRepaint();
                beforeCapture?.Invoke(view);
                LayoutProbe.Validate(view);

                // Forces a real, synchronous repaint and reads it back; retries for up to five real wall clock
                // seconds (never an unbounded loop), only trusting a read taken at the instant this window actually
                // held OS foreground both right before and right after the read (a won SetForegroundWindow can land
                // before the desktop compositor has actually flipped a new frame to the buffer ReadScreenPixel
                // samples, so a single "was it foreground" check right before the read is not enough on its own).
                // Whatever the newest attempt was is kept as a fallback if none is ever confirmed.
                double deadline = EditorApplication.timeSinceStartup + 5.0;
                bool confirmed = false;
                do
                {
                    RaiseToFront();
                    System.Threading.Thread.Sleep(150);
                    beforeCapture?.Invoke(view);
                    ForceRepaint();
                    System.Threading.Thread.Sleep(60);
                    beforeCapture?.Invoke(view);
                    bool heldBefore = hwnd != IntPtr.Zero && IsForeground(hwnd);
                    ForceRepaint();
                    var candidate = CapturePixels();
                    bool heldAfter = hwnd != IntPtr.Zero && IsForeground(hwnd);
                    bool blank = IsBlank(candidate);
                    if (!blank && heldBefore && heldAfter)
                    {
                        if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                        tex = candidate;
                        confirmed = true;
                        break;
                    }
                    if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                    tex = candidate;
                    System.Threading.Thread.Sleep(40);
                }
                while (EditorApplication.timeSinceStartup < deadline);

                if (crop != null)
                {
                    var kept = Crop(tex, crop(new ShotFrame(window, frame, tex.width, tex.height, confirmed)));
                    UnityEngine.Object.DestroyImmediate(tex);
                    tex = kept;
                }
                if (IsBlank(tex)) Debug.LogWarning("[InspectorShots] " + Path.GetFileName(pngPath) + " is still blank after retrying for 5s.");
                else if (!confirmed) Debug.LogWarning("[InspectorShots] " + Path.GetFileName(pngPath) + ": never confirmed OS foreground at the moment of capture; the frame may show whatever else was on screen instead.");
                var directory = Path.GetDirectoryName(pngPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllBytes(pngPath, tex.EncodeToPNG());
            }
            finally
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        /// <summary>
        /// A new texture holding <paramref name="area"/> of <paramref name="source"/>, the area measured the way a
        /// picture is read: from its top left corner, rows counting down. A texture counts its rows from the
        /// bottom, so the area's top edge is turned into a row count from the bottom before the copy. An area
        /// reaching past the picture is cut at its edge.
        /// </summary>
        public static Texture2D Crop(Texture2D source, RectInt area)
        {
            int x = Mathf.Clamp(area.x, 0, source.width);
            int y = Mathf.Clamp(area.y, 0, source.height);
            int w = Mathf.Min(area.width, source.width - x);
            int h = Mathf.Min(area.height, source.height - y);
            if (w <= 0 || h <= 0)
                throw new ArgumentException("InspectorShots.Crop: " + area + " leaves nothing of a " + source.width + " by " + source.height + " picture.");
            var kept = new Texture2D(w, h, TextureFormat.RGBA32, false);
            kept.SetPixels(source.GetPixels(x, source.height - y - h, w, h));
            kept.Apply();
            return kept;
        }

        static bool IsBlank(Texture2D tex)
        {
            var pixels = tex.GetPixels32();
            if (pixels.Length == 0) return true;
            var first = pixels[0];
            for (int i = 1; i < pixels.Length; i++)
                if (!pixels[i].Equals(first)) return false;
            return true;
        }

        // ---- Plain Win32, reached the same way LayoutProbe reaches ValidateLayout: nothing here is public API,
        // and none of it touches the creator's own windows, only this method's own throwaway one. ----

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

        static readonly IntPtr HwndTopmost = new IntPtr(-1);
        const uint SwpNoMove = 0x0002, SwpNoSize = 0x0001, SwpShowWindow = 0x0040;

        static bool IsForeground(IntPtr hWnd) => hWnd != IntPtr.Zero && GetForegroundWindow() == hWnd;

        static IntPtr FindByTitleSubstring(string needle)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd)) return true;
                var sb = new StringBuilder(256);
                GetWindowText(hWnd, sb, sb.Capacity);
                if (sb.ToString().IndexOf(needle, StringComparison.Ordinal) < 0) return true;
                found = hWnd;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>Marks the window topmost (so it draws above other applications' windows too, not only Unity's own) and, if something else currently holds OS foreground, borrows that window's input thread just long enough to hand foreground over without Windows silently refusing the request.</summary>
        static void Raise(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            SetWindowPos(hWnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
            var current = GetForegroundWindow();
            if (current == hWnd) return;
            uint currentThread = GetWindowThreadProcessId(current, out _);
            uint thisThread = GetCurrentThreadId();
            bool attached = currentThread != thisThread && AttachThreadInput(thisThread, currentThread, true);
            try { SetForegroundWindow(hWnd); }
            finally { if (attached) AttachThreadInput(thisThread, currentThread, false); }
        }
    }
}
#endif
