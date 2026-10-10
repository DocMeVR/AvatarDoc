#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Phase 12 task 9 Part D: walks every visible descendant of a real, laid out panel (through LayoutProbe) and
    /// fails, naming the element, on any of the five shapes the creator's screenshots flagged: a clipped right
    /// edge, a label or field off the row's own left edge or the 112px column, a boolean pushed to a row's far end
    /// by a growing sibling instead of sitting at a field's own column, a single line row whose height or vertical
    /// centre disagrees with its neighbours, or a button or chip whose text is off centre. A deliberate exception
    /// (a tree row's own trailing action button, a chip's own remove button) is named once in the allow list handed
    /// in, with the one line reason it does not have to answer to a rule built for an ordinary field row.
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
        }

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

        /// <summary>Runs every rule against every visible descendant of panel, against a content right edge measured from panelRightEdge.</summary>
        public static void Audit(VisualElement panel, float panelRightEdge, AllowList allow)
        {
            var all = panel.Query<VisualElement>().Build().ToList().Where(Visible).ToList();

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
                bool precededByGrowingField = siblings.Take(index).Any(s => s.ClassListContains("ad-grow"));
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
    }
}
#endif
