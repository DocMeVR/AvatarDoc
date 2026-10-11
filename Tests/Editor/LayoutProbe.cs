#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Linq;
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

        const int MaxSettleFrames = 30;

        /// <summary>
        /// Runs the view's panel the way the editor runs it between frames, until every list has drawn its rows.
        /// A synchronous test never reaches those frames, so a bound ListView stays empty: its binding request waits
        /// for the panel's Bindings update (VisualTreeBindingsUpdater), and a list that sizes its rows waits for a
        /// Fill it schedules on the panel's scheduler (DynamicHeightVirtualizationController.ScheduleFill). Each
        /// frame does what UIElementsUtility.UpdateSchedulers does for an editor panel, then the layout pass a
        /// repaint does: Panel.timerEventScheduler.UpdateScheduledEvents(), Panel.UpdateBindings(), then Validate
        /// (Panel.ValidateLayout, which sends GeometryChangedEvent at once, and a list makes rows for its new size
        /// from that event). Panel and TimerEventScheduler are internal types, so these members are reached by
        /// reflection. After the first frame, when the bindings have landed, every ListView is rebuilt (Rebuild
        /// drops rows made for an earlier size and makes them again for the list's current one), then frames run
        /// until the layout stops changing. A shown list that drew fewer rows than it should stops the run instead
        /// of measuring or picturing an empty or short list (see CheckRowsDrawn). Used by the docs pictures and by
        /// PanelAudit, so both see the rows the creator sees.
        /// </summary>
        public static void SettleLists(VisualElement view)
        {
            var panel = view.panel ?? throw new InvalidOperationException("LayoutProbe.SettleLists: the view is not in a window; add it to a window's rootVisualElement first.");
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var updateBindings = panel.GetType().GetMethod("UpdateBindings", Flags, null, Type.EmptyTypes, null);
            var scheduler = panel.GetType().GetProperty("timerEventScheduler", Flags)?.GetValue(panel);
            var updateScheduled = scheduler?.GetType().GetMethod("UpdateScheduledEvents", Flags, null, Type.EmptyTypes, null);
            if (updateBindings == null || updateScheduled == null)
                throw new InvalidOperationException("LayoutProbe.SettleLists: " + panel.GetType() + " has no UpdateBindings or timerEventScheduler.UpdateScheduledEvents in this Unity version, so list rows cannot be drawn.");

            void Frame()
            {
                updateScheduled.Invoke(scheduler, null);
                updateBindings.Invoke(panel, null);
                Validate(view);
            }

            Frame();
            foreach (var list in view.Query<ListView>().ToList()) list.Rebuild();
            string previous = null;
            for (int frame = 0; frame < MaxSettleFrames; frame++)
            {
                Frame();
                var current = Signature(view);
                if (current == previous)
                {
                    CheckRowsDrawn(view);
                    return;
                }
                previous = current;
            }
            throw new InvalidOperationException("LayoutProbe.SettleLists: the view was still changing after " + MaxSettleFrames + " frames.");
        }

        /// <summary>
        /// Stops the run when a shown list did not draw what it should: every row when the list fits without its
        /// vertical scroller, or at least one row when it scrolls.
        /// </summary>
        public static void CheckRowsDrawn(VisualElement view)
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
                throw new InvalidOperationException("LayoutProbe.SettleLists: a list" + where + " holds " + count + (count == 1 ? " item" : " items")
                    + " but drew " + shown + (shown == 1 ? " row" : " rows") + ", so a picture or a layout audit would see it " + (shown == 0 ? "empty." : "short."));
            }
        }

        static bool IsShown(VisualElement element)
        {
            for (var e = element; e != null; e = e.hierarchy.parent)
                if (e.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        /// <summary>Every element's laid out rectangle in one string, for telling whether another pass changed anything.</summary>
        internal static string Signature(VisualElement root)
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
