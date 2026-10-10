#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Measures a view's real, laid out geometry for the phase 12 task 9 alignment tests (Part B and Part D). A
    /// plain, unattached VisualElement tree never actually resolves a layout pass inside a synchronous test method:
    /// every resolvedStyle and worldBound stays NaN or zero with no frame ever pumped between test steps, the same
    /// limitation ViewTests already worked around for MeasureIndent by testing its math on synthetic inputs instead
    /// of a real layout. This follows the FocusHostWindow pattern instead: it attaches the view to a real host
    /// window sized the way the creator's own inspector is, then forces UI Toolkit's panel to run its style and
    /// layout pass synchronously, through the panel's own internal ValidateLayout, so worldBound reads the same
    /// numbers a screenshot would show.
    /// </summary>
    public static class LayoutProbe
    {
        class ProbeHostWindow : EditorWindow { }

        /// <summary>
        /// Lays the view out at exactly width points (360 by default, the creator's own default inspector width; the
        /// header is also checked at 800), forces a synchronous layout pass, runs measure against the now real
        /// geometry, then always closes the window and detaches the view, whether measure throws or not. The width
        /// is held by a fixed frame around the view rather than by the window: Unity reopens an EditorWindow type at
        /// the size it last had, so a window asked for 800 after one of 360 can still lay out at 360.
        /// </summary>
        public static void Measure(VisualElement view, Action measure, float width = 360f, float height = 900f)
        {
            var window = ScriptableObject.CreateInstance<ProbeHostWindow>();
            var frame = new VisualElement { name = "layout-probe-frame" };
            frame.style.position = Position.Absolute;
            frame.style.left = 0f;
            frame.style.top = 0f;
            frame.style.width = width;
            frame.style.minHeight = height;
            try
            {
                window.position = new Rect(0, 0, width, height);
                window.Show();
                frame.Add(view);
                window.rootVisualElement.Add(frame);
                Validate(view);
                if (Mathf.Abs(frame.worldBound.width - width) > 0.5f)
                    throw new InvalidOperationException("LayoutProbe.Measure: asked for " + width + " points but the frame laid out at " + frame.worldBound.width + ".");
                measure();
            }
            finally
            {
                view.RemoveFromHierarchy();
                frame.RemoveFromHierarchy();
                window.Close();
            }
        }

        /// <summary>
        /// Forces the element's panel to run its style and layout pass right now, through the panel's own internal
        /// ValidateLayout (UI Toolkit's BaseVisualElementPanel/Panel), reached by reflection since it sits outside
        /// the public IPanel surface. Fails with a clear message, rather than silently measuring stale zeros, if
        /// this Unity version does not expose the method under this name.
        /// </summary>
        public static void Validate(VisualElement element)
        {
            var panel = element.panel;
            if (panel == null)
                throw new InvalidOperationException("LayoutProbe.Validate: the element has no panel; add it to a window's rootVisualElement first.");
            var method = panel.GetType().GetMethod("ValidateLayout", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
                throw new InvalidOperationException("LayoutProbe.Validate: no ValidateLayout method found on " + panel.GetType()
                    + " in this Unity version; Part B and Part D cannot measure a real layout this way.");
            // One pass can measure text before its font has the glyphs it needs, and settle a row a couple of
            // points short; the next pass then measures the real text. Repeat until nothing moves, so a test never
            // reads a half settled layout.
            string previous = null;
            for (int pass = 0; pass < 6; pass++)
            {
                method.Invoke(panel, null);
                var current = Signature(panel.visualTree);
                if (current == previous) return;
                previous = current;
            }
            throw new InvalidOperationException("LayoutProbe.Validate: the layout was still moving after 6 passes.");
        }

        /// <summary>Every element's laid out rectangle in one string, for telling whether another pass changed anything.</summary>
        static string Signature(VisualElement root)
        {
            var text = new System.Text.StringBuilder();
            root.Query<VisualElement>().ForEach(e =>
            {
                var r = e.worldBound;
                text.Append(r.x.ToString("0.0")).Append(',').Append(r.y.ToString("0.0")).Append(',')
                    .Append(r.width.ToString("0.0")).Append(',').Append(r.height.ToString("0.0")).Append(';');
            });
            return text.ToString();
        }
    }
}
#endif
