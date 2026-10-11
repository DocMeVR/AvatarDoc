#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Walks every visible descendant of a real, laid out panel (through LayoutProbe) and
    /// fails, naming the element, on any of the five shapes the creator's screenshots flagged: a clipped right
    /// edge, a label or field off the row's own left edge or the 112px column, a boolean pushed to a row's far end
    /// by a growing sibling instead of sitting at a field's own column, a single line row whose height or vertical
    /// centre disagrees with its neighbours, or a button or chip whose text is off centre. It draws every list's rows
    /// first and holds them to four more rules (6 to 9, see RowFindings): a slider's track and a text or object box
    /// stay usable, nothing in a row spills past the row or into a neighbour, and a text that does not fit ends in an
    /// ellipsis. A deliberate exception (a tree row's own trailing action button, a chip's own remove button) is named
    /// once in the allow list handed in, with the one line reason it does not have to answer to a rule built for an
    /// ordinary field row.
    /// </summary>
    public static class PanelAudit
    {
        public sealed class AllowList
        {
            readonly List<(Func<VisualElement, bool> Match, string Reason, int Rule)> entries = new List<(Func<VisualElement, bool>, string, int)>();

            /// <summary>Exempts matching elements from every rule.</summary>
            public AllowList Allow(Func<VisualElement, bool> match, string reason)
            {
                entries.Add((match, reason, 0));
                return this;
            }

            /// <summary>Exempts matching elements from one numbered rule only; every other rule still measures them.</summary>
            public AllowList AllowForRule(int rule, Func<VisualElement, bool> match, string reason)
            {
                entries.Add((match, reason, rule));
                return this;
            }

            public bool Allows(VisualElement element) => entries.Any(e => e.Rule == 0 && e.Match(element));

            public bool Allows(VisualElement element, int rule) => entries.Any(e => (e.Rule == 0 || e.Rule == rule) && e.Match(element));

            /// <summary>Only an allowance made for this very rule. An allowance from every rule does not reach rules 6 to 9.</summary>
            public bool AllowsOnly(VisualElement element, int rule) => entries.Any(e => e.Rule == rule && e.Match(element));
        }

        /// <summary>The narrowest a slider's track or a text or object field's box may be in a list row and still be used.</summary>
        public const float MinUsableWidth = 60f;

        static string Describe(VisualElement e)
        {
            string name = !string.IsNullOrEmpty(e.name) ? "#" + e.name : e.GetType().Name;
            var classes = string.Join(".", e.GetClasses());
            string self = classes.Length > 0 ? name + " (." + classes + ")" : name;
            var chain = new List<string>();
            for (var p = e.parent; p != null && chain.Count < 6; p = p.parent)
                chain.Add(!string.IsNullOrEmpty(p.name) ? "#" + p.name : (p.GetClasses().FirstOrDefault() ?? p.GetType().Name));
            return self + " under " + string.Join(" < ", chain);
        }

        static bool Visible(VisualElement e) => e.resolvedStyle.display != DisplayStyle.None
            && !float.IsNaN(e.worldBound.width) && e.worldBound.width > 0f && !float.IsNaN(e.worldBound.height) && e.worldBound.height > 0f;

        /// <summary>
        /// Whether an element still occupies its place in the row's own horizontal flow, for finding what comes
        /// right after a label. Looser than Visible: an .ad-spacer is a real flex item that pushes whatever follows
        /// it over, even though it draws nothing and so resolves to zero height under the row's own align-items
        /// centre (only align-items stretch would give an empty element a real height).
        /// </summary>
        static bool InFlow(VisualElement e) => e.resolvedStyle.display != DisplayStyle.None
            && e.resolvedStyle.position != Position.Absolute && !float.IsNaN(e.worldBound.width);

        /// <summary>
        /// Runs every rule against every visible descendant of panel, against a content right edge measured from
        /// panelRightEdge. The panel's lists draw their rows first (LayoutProbe.SettleLists): a synchronous layout
        /// never binds a ListView's rows.
        /// </summary>
        public static void Audit(VisualElement panel, float panelRightEdge, AllowList allow)
        {
            LayoutProbe.SettleLists(panel);
            var all = panel.Query<VisualElement>().Build().ToList().Where(Visible).ToList();

            var rowFindings = RowFindings(panel, allow);
            if (rowFindings.Count > 0)
                Assert.Fail("rows do not fit, " + rowFindings.Count + (rowFindings.Count == 1 ? " finding: " : " findings: ") + string.Join("\n", rowFindings));

            // Rule 1: nothing's right edge passes the panel's own content right edge.
            foreach (var e in all)
            {
                if (allow.Allows(e)) continue;
                Assert.That(e.worldBound.xMax, Is.LessThanOrEqualTo(panelRightEdge + 0.5f),
                    Describe(e) + " is clipped: its right edge (" + e.worldBound.xMax.ToString("0.0")
                    + ") passes the panel's own content right edge (" + panelRightEdge.ToString("0.0") + ")");
            }

            // Rule 2: a labelled row's own caption starts at the row's left edge, and whatever follows it starts
            // at the 112px field column. Only fires for a row with a standalone caption Label (ad-label or a
            // field's own unity-base-field__label used directly as the row's first flow child), never for a row
            // whose first child is itself a field carrying its own built in label further inside it.
            foreach (var row in all.Where(e => e.ClassListContains("ad-row")))
            {
                if (allow.Allows(row)) continue;
                var flow = row.Children().Where(InFlow).ToList();
                if (flow.Count == 0) continue;
                bool isCaption = flow[0].ClassListContains("ad-label") || flow[0].ClassListContains("unity-base-field__label");
                if (!isCaption) continue;
                var label = flow[0];
                if (!allow.Allows(label))
                    Assert.That(label.worldBound.x, Is.EqualTo(row.worldBound.x).Within(1f), Describe(label) + " does not start at its row's own left edge");
                if (flow.Count <= 1) continue;
                var field = flow[1];
                // A caption immediately followed by a spacer is a title-plus-trailing-action row (the section
                // header's own "+ Add", the header title's own Summary): the spacer says everything after it is a
                // trailing action, not a field the 112px column measures.
                if (field.ClassListContains("ad-spacer")) continue;
                if (!allow.Allows(field))
                {
                    Assert.That(field.worldBound.x, Is.EqualTo(row.worldBound.x + 112f).Within(1f),
                        Describe(field) + " does not start at the 112px label column");
                }
            }

            // Rule 3: a boolean field (a Toggle) never lands wherever a growing sibling before it happens to leave
            // off, and a standalone boolean (the sole real control in its own row) sits at the field column, same
            // as every other field type does, so a form's inputs still line up in one column even when one of them
            // happens to be a checkbox.
            foreach (var toggle in all.OfType<Toggle>())
            {
                if (allow.Allows(toggle, 3)) continue;
                var parent = toggle.parent;
                if (parent == null) continue;
                var siblings = parent.Children().Where(InFlow).ToList();
                int index = siblings.IndexOf(toggle);
                // Growing is read from the laid out style, not a class name: list rows grow their controls through
                // classes of their own. A spacer grows too, but only to push a row's trailing actions to its end.
                bool precededByGrowingField = siblings.Take(index).Any(s => s.resolvedStyle.flexGrow > 0f && !s.ClassListContains("ad-spacer"));
                Assert.That(precededByGrowingField, Is.False,
                    Describe(toggle) + " sits after a growing field in the same row: a boolean must never be pushed wherever a grow field leaves off (this is exactly the shape 'Keep offset' had before it was fixed); give it a row of its own instead");

                // A caption Label beside it (its own row's ad-label, or another field's built in one) is not a
                // competing control; a second Toggle, or any other field, means this one is not standing alone.
                bool isRow = parent.ClassListContains("ad-row");
                var otherControls = siblings.Where(s => s != toggle && !s.ClassListContains("ad-label")
                    && !s.ClassListContains("unity-base-field__label") && !s.ClassListContains("ad-spacer")).ToList();
                if (isRow && otherControls.Count == 0 && !allow.Allows(toggle, 3))
                {
                    Assert.That(toggle.worldBound.x, Is.EqualTo(parent.worldBound.x + 112f).Within(1f),
                        Describe(toggle) + " is a standalone boolean and does not start at the 112px field column");
                }
            }

            // Rule 4: single line rows sharing one parent share one height, and their label and field sit on the
            // same vertical centre. Grouped by immediate parent rather than by some notion of "section", since that
            // is exactly the grouping every panel already builds its rows under. A row taller than a plain single
            // line (a wrapped sentence, a card with several lines inside it) is its own shape by design and is
            // never compared against a plain field row's height.
            const float singleLineCeiling = 24f;
            var rowsByParent = all.Where(e => e.ClassListContains("ad-row") && !e.ClassListContains("ad-target-wrap")
                && e.worldBound.height <= singleLineCeiling).GroupBy(e => e.parent);
            foreach (var group in rowsByParent)
            {
                var rows = group.Where(r => !allow.Allows(r)).ToList();
                if (rows.Count < 2) continue;
                float height = rows[0].worldBound.height;
                float centre = rows[0].worldBound.y + height / 2f;
                foreach (var row in rows.Skip(1))
                {
                    Assert.That(row.worldBound.height, Is.EqualTo(height).Within(1f), Describe(row) + " does not share its sibling rows' own height (" + row.worldBound.height.ToString("0.0") + " against " + height.ToString("0.0") + " for " + Describe(rows[0]) + ")");
                    var flow = row.Children().Where(Visible).Where(c => c.resolvedStyle.position != Position.Absolute).ToList();
                    if (flow.Count == 0) continue;
                    float rowCentre = flow[0].worldBound.y + flow[0].worldBound.height / 2f;
                    float expected = row.worldBound.y + row.worldBound.height / 2f;
                    Assert.That(rowCentre, Is.EqualTo(expected).Within(1f), Describe(flow[0]) + " does not sit on its own row's vertical centre");
                }
            }

            // Rule 5: a chip's own label text sits on the chip's own vertical centre.
            foreach (var chip in all.Where(e => e.ClassListContains("ad-chip") || e.ClassListContains("ad-filter-chip") || e.ClassListContains("ad-piece-chip")))
            {
                if (allow.Allows(chip)) continue;
                var label = chip.Q<Label>();
                if (label == null || label == chip) continue;
                float chipCentre = chip.worldBound.y + chip.worldBound.height / 2f;
                float labelCentre = label.worldBound.y + label.worldBound.height / 2f;
                Assert.That(labelCentre, Is.EqualTo(chipCentre).Within(1f), Describe(chip) + " text is not centred on the chip's own vertical centre");
            }
        }

        /// <summary>The containers AvatarDoc lays a row's controls out in, side by side.</summary>
        static readonly string[] RowClasses = { "ad-row", "ad-target", "ad-target-main", "ad-target-off", "ad-property-values", "ad-property-vector", "ad-flags" };

        /// <summary>
        /// Rules 6 to 9, every finding at once:
        /// 6. a slider in a list row keeps a track of at least MinUsableWidth;
        /// 7. a text or object field in a list row keeps a box of at least MinUsableWidth;
        /// 8. in every row, nothing a control draws passes the row's own edges or reaches into a sibling's (the remove
        ///    button over a slider whose label and track spill out of its own box);
        /// 9. a text in a list row that does not fit ends in an ellipsis, with a tooltip on it or around it in the row.
        /// </summary>
        static List<string> RowFindings(VisualElement panel, AllowList allow)
        {
            var findings = new List<string>();
            var shown = panel.Query<VisualElement>().Build().ToList().Where(e => Displayed(e, panel)).ToList();
            foreach (var e in shown)
            {
                if (!InListRow(e, panel)) continue;
                if (IsSlider(e) && !allow.AllowsOnly(e, 6))
                {
                    var track = e.Q(className: "unity-base-slider__drag-container");
                    float width = track != null && Displayed(track, panel) ? ShownWidth(track, panel) : 0f;
                    if (width < MinUsableWidth - 0.5f)
                        findings.Add("rule 6: " + Describe(e) + " shows a track only " + width.ToString("0.0") + " px wide, under " + MinUsableWidth + " px");
                }
                if (IsTextOrObjectField(e) && !InsideSlider(e) && !allow.AllowsOnly(e, 7))
                {
                    var input = e.hierarchy.Children().FirstOrDefault(c => c.ClassListContains("unity-base-field__input"));
                    float width = input != null && Displayed(input, panel) ? ShownWidth(input, panel) : 0f;
                    if (width < MinUsableWidth - 0.5f)
                        findings.Add("rule 7: " + Describe(e) + " shows a box only " + width.ToString("0.0") + " px wide, under " + MinUsableWidth + " px");
                }
                if (e is TextElement text && !allow.AllowsOnly(e, 9))
                {
                    var cut = CutShort(text);
                    if (cut != null) findings.Add("rule 9: " + Describe(e) + " " + cut);
                }
            }

            foreach (var row in shown.Where(e => RowClasses.Any(e.ClassListContains)))
            {
                if (allow.AllowsOnly(row, 8)) continue;
                var box = row.worldBound;
                if (float.IsNaN(box.width)) continue;
                var parts = row.hierarchy.Children()
                    .Where(c => c.resolvedStyle.display != DisplayStyle.None && c.resolvedStyle.position != Position.Absolute && !allow.AllowsOnly(c, 8))
                    .Select(c => (Element: c, Extent: Extent(c)))
                    .Where(p => !float.IsNaN(p.Extent.width))
                    .ToList();
                foreach (var (element, extent) in parts)
                {
                    if (extent.xMin < box.xMin - 0.5f || extent.xMax > box.xMax + 0.5f || extent.yMin < box.yMin - 0.5f || extent.yMax > box.yMax + 0.5f)
                        findings.Add("rule 8: " + Describe(element) + " is clipped by its row: it draws over " + Show(extent) + ", the row is " + Show(box));
                }
                for (int i = 0; i < parts.Count; i++)
                    for (int j = i + 1; j < parts.Count; j++)
                    {
                        var a = parts[i].Extent;
                        var b = parts[j].Extent;
                        float across = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
                        float down = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                        if (across > 0.5f && down > 0.5f)
                            findings.Add("rule 8: " + Describe(parts[i].Element) + " (" + Show(a) + ") overlaps " + Describe(parts[j].Element) + " (" + Show(b) + ")");
                    }
            }
            return findings;
        }

        static string Show(Rect r) => "x " + r.xMin.ToString("0.0") + " to " + r.xMax.ToString("0.0") + ", y " + r.yMin.ToString("0.0") + " to " + r.yMax.ToString("0.0");

        /// <summary>How much of the element's width shows: its box, cut by every box above it up to the panel that hides what passes its edges.</summary>
        static float ShownWidth(VisualElement e, VisualElement panel)
        {
            var box = e.worldBound;
            if (float.IsNaN(box.width)) return 0f;
            float left = box.xMin, right = box.xMax;
            for (var p = e.hierarchy.parent; p != null; p = p.hierarchy.parent)
            {
                if (Hides(p))
                {
                    left = Mathf.Max(left, p.worldBound.xMin);
                    right = Mathf.Min(right, p.worldBound.xMax);
                }
                if (p == panel) break;
            }
            return Mathf.Max(0f, right - left);
        }

        /// <summary>Whether the element and everything above it up to the panel is laid out, however small it came out.</summary>
        static bool Displayed(VisualElement e, VisualElement panel)
        {
            for (var p = e; p != null; p = p.hierarchy.parent)
            {
                if (p.resolvedStyle.display == DisplayStyle.None) return false;
                if (p == panel) break;
            }
            return !float.IsNaN(e.worldBound.width);
        }

        /// <summary>Whether the element is, or sits inside, one row of a list: a target list's row, or a shape set rule's lines.</summary>
        static bool InListRow(VisualElement e, VisualElement panel)
        {
            for (var p = e; p != null; p = p.hierarchy.parent)
            {
                if (p.ClassListContains("ad-target") || p.name == "ad-shape-set-row") return true;
                if (p == panel) break;
            }
            return false;
        }

        static bool IsSlider(VisualElement e) => e is BaseSlider<float> || e is BaseSlider<int>;

        static bool InsideSlider(VisualElement e)
        {
            for (var p = e.hierarchy.parent; p != null; p = p.hierarchy.parent)
                if (IsSlider(p)) return true;
            return false;
        }

        static bool IsTextOrObjectField(VisualElement e)
        {
            if (e is UnityEditor.UIElements.ObjectField) return true;
            for (var type = e.GetType(); type != null; type = type.BaseType)
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(TextInputBaseField<>)) return true;
            return false;
        }

        /// <summary>
        /// Where an element draws: its own box and, inside it, every laid out child's, except what is cut off for it
        /// (a box that hides its overflow, a text input that scrolls its text) or placed over it on purpose (an
        /// absolutely positioned part, such as a slider's knob).
        /// </summary>
        static Rect Extent(VisualElement e)
        {
            var rect = e.worldBound;
            if (float.IsNaN(rect.width) || Hides(e) || e.ClassListContains("unity-base-text-field__input")) return rect;
            foreach (var child in e.hierarchy.Children())
            {
                if (child.resolvedStyle.display == DisplayStyle.None || child.resolvedStyle.position == Position.Absolute) continue;
                var inner = Extent(child);
                if (float.IsNaN(inner.width) || (inner.width <= 0f && inner.height <= 0f)) continue;
                rect = Rect.MinMaxRect(Mathf.Min(rect.xMin, inner.xMin), Mathf.Min(rect.yMin, inner.yMin), Mathf.Max(rect.xMax, inner.xMax), Mathf.Max(rect.yMax, inner.yMax));
            }
            return rect;
        }

        static readonly MethodInfo ShouldElideMethod = typeof(TextElement).GetMethod("ShouldElide", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo StyleField = typeof(VisualElement).GetField("m_Style", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly PropertyInfo OverflowProperty = StyleField?.FieldType.GetProperty("overflow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        /// <summary>Whether the element cuts off what its children draw past its edges (overflow: hidden), read from its computed style (VisualElement.m_Style, internal), since resolvedStyle does not carry overflow.</summary>
        static bool Hides(VisualElement e)
        {
            if (StyleField == null || OverflowProperty == null)
                throw new InvalidOperationException("PanelAudit: VisualElement.m_Style.overflow was not found in this Unity version, so a row's clipped parts cannot be told apart.");
            return OverflowProperty.GetValue(StyleField.GetValue(e)).ToString() == "Hidden";
        }

        /// <summary>Null when the text shows whole, or ends in an ellipsis with a tooltip on it or around it in its row; otherwise what is wrong.</summary>
        static string CutShort(TextElement text)
        {
            if (string.IsNullOrEmpty(text.text) || !(text.worldBound.width > 0f)) return null;
            if (text.ClassListContains("unity-object-field-display__label")) return null;
            for (var p = text.hierarchy.parent; p != null; p = p.hierarchy.parent)
                if (p.ClassListContains("unity-base-text-field__input")) return null;
            var box = text.contentRect;
            var natural = text.MeasureTextSize(text.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
            bool cut = text.resolvedStyle.whiteSpace == WhiteSpace.NoWrap
                ? natural.x > box.width + 0.5f
                : text.MeasureTextSize(text.text, box.width, VisualElement.MeasureMode.Exactly, 0f, VisualElement.MeasureMode.Undefined).y > box.height + 1f;
            if (!cut) return null;
            if (ShouldElideMethod == null)
                throw new InvalidOperationException("PanelAudit: TextElement.ShouldElide was not found in this Unity version, so an ellipsis cannot be told apart from a cut.");
            bool elides = (bool)ShouldElideMethod.Invoke(text, null) && text.resolvedStyle.whiteSpace == WhiteSpace.NoWrap;
            bool explained = false;
            for (VisualElement p = text; p != null && !explained; p = p.hierarchy.parent)
            {
                explained = !string.IsNullOrEmpty(p.tooltip);
                if (p.ClassListContains("ad-target") || p.name == "ad-shape-set-row") break;
            }
            if (elides && explained) return null;
            return "\"" + text.text + "\" is cut short: " + box.width.ToString("0.0") + " px wide for text that measures " + natural.x.ToString("0.0")
                + (elides ? ", and has no tooltip to read it in full" : ", without an ellipsis");
        }
    }
}
#endif
