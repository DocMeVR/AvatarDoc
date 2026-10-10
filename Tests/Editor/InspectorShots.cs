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
    /// pixel what a human would see. Call it only from a TEMPORARY test file: build a throwaway AvatarDoc on a
    /// TestAvatar, build a fresh AvatarDoctorView per width (a reused view keeps its first window's resolved Yoga
    /// sizes on a second, differently sized pass), call Capture(view, path, width) for each shot, then
    /// view.RemoveFromHierarchy(), and delete the temporary test file and its .meta again once the review is done.
    /// See Tests/Editor/InspectorShotsExample.cs.txt for a minimal copy and paste starting point.
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

        /// <summary>
        /// Hosts <paramref name="view"/> in a throwaway, pinned size EditorWindow at a fixed screen position, raises
        /// it above whatever else is on screen, reads its real pixels and writes them to <paramref name="pngPath"/>
        /// (its containing folder is created if missing), then always closes the window. Returns
        /// <paramref name="pngPath"/>. <paramref name="beforeCapture"/>, when given, runs once right after the
        /// window opens and again before every retry's read: raising the window back to OS foreground during a
        /// retry can itself reset which element the panel thinks is focused or selected, silently undoing state the
        /// caller only set once before calling this, so anything transient (a row selected, a field focused, a fold
        /// opened) belongs in there rather than only being set before the call.
        /// </summary>
        public static string Capture(VisualElement view, string pngPath, float width, float height = 900f, Action<VisualElement> beforeCapture = null)
        {
            var frame = new VisualElement { name = "inspector-shot-frame" };
            frame.style.position = Position.Absolute;
            frame.style.left = 0f;
            frame.style.top = 0f;
            frame.style.width = width;
            frame.style.minHeight = height;
            frame.style.flexDirection = FlexDirection.Column;
            frame.Add(view);

            string titleMarker = "InspectorShotsHost_" + Guid.NewGuid().ToString("N");
            var window = ScriptableObject.CreateInstance<HostWindow>();
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

            Texture2D CapturePixels()
            {
                var rect = window.position;
                int w = Mathf.Max(1, (int)rect.width);
                int h = Mathf.Max(1, (int)rect.height);
                var pixels = InternalEditorUtility.ReadScreenPixel(new Vector2(rect.x, rect.y), w, h);
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.SetPixels(pixels);
                tex.Apply();
                return tex;
            }

            try
            {
                window.titleContent = new GUIContent(titleMarker);
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
                RaiseToFront();
                ForceRepaint();
                beforeCapture?.Invoke(view);
                LayoutProbe.Validate(view);

                var directory = Path.GetDirectoryName(pngPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                // Forces a real, synchronous repaint and reads it back; retries for up to five real wall clock
                // seconds (never an unbounded loop), only trusting a read taken at the instant this window actually
                // held OS foreground both right before and right after the read (a won SetForegroundWindow can land
                // before the desktop compositor has actually flipped a new frame to the buffer ReadScreenPixel
                // samples, so a single "was it foreground" check right before the read is not enough on its own).
                // Whatever the newest attempt was is kept as a fallback if none is ever confirmed.
                double deadline = EditorApplication.timeSinceStartup + 5.0;
                Texture2D tex = null;
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

                if (IsBlank(tex)) Debug.LogWarning("[InspectorShots] " + Path.GetFileName(pngPath) + " is still blank after retrying for 5s.");
                else if (!confirmed) Debug.LogWarning("[InspectorShots] " + Path.GetFileName(pngPath) + ": never confirmed OS foreground at the moment of capture; the frame may show whatever else was on screen instead.");
                File.WriteAllBytes(pngPath, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                return pngPath;
            }
            finally
            {
                frame.RemoveFromHierarchy();
                window.Close();
            }
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
