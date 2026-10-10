#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using nadena.dev.ndmf;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Plain text or Markdown, for a store page or a shared prefab's readme.</summary>
    public enum SummaryFormat { Plain = 0, Markdown = 1 }

    /// <summary>
    /// Builds a copyable summary of what one AvatarDoc component adds to the avatar: its own parameters, its own
    /// slice of the merged menu, what it needs, its touch sources and its current messages. A pure read of the plan
    /// and the component: no scene write, no asset write, no clipboard.
    /// </summary>
    public static class SummaryBuilder
    {
        public static string Build(AvatarPlan plan, AvatarDoc component, SummaryFormat format)
        {
            if (plan == null || component == null) return "";
            bool markdown = format == SummaryFormat.Markdown;
            var sb = new StringBuilder();

            AppendTitle(sb, component, markdown);

            var owned = plan.Plan.Parameters.Where(p => p.Controls.Any(c => c.Component == component)).ToList();
            var normal = owned.Where(p => !p.IsAnimatorOnly).ToList();
            var animatorOnly = owned.Where(p => p.IsAnimatorOnly).ToList();
            // A declaration has its own heading further down, but it costs its bits like any other parameter.
            var declared = plan.Plan.Parameters.Where(p => p.Declared && p.Component == component && !p.IsAnimatorOnly).ToList();
            int ownBits = normal.Concat(declared).Where(p => !p.ReusesExisting).Sum(p => p.Bits);
            sb.Append(string.Format(L.Text("ad.summary.bits"), ownBits, plan.AvatarDoctorBits)).Append('\n').Append('\n');

            // Every blend shape trigger's hidden copy of its shape: a Float only the animator sees, never synced.
            var mirrors = plan.Triggers.Where(t => t.Kind == TriggerKind.Link && t.Component == component && !string.IsNullOrEmpty(t.MirrorParameter)).Select(t => t.MirrorParameter).ToList();
            AppendParameters(sb, normal, animatorOnly, mirrors, component, markdown);
            AppendMenu(sb, plan, component, markdown);
            AppendHiddenToggles(sb, plan, component, markdown);
            AppendShapeSets(sb, plan, component, markdown);
            AppendFollows(sb, plan, component, markdown);
            AppendGrabbables(sb, plan, component, markdown);
            AppendAnimators(sb, plan, component, markdown);
            AppendMenuAssets(sb, plan, component, markdown);
            AppendDeclarations(sb, component, markdown);
            AppendRemovals(sb, plan, component, markdown);
            AppendShapes(sb, plan, markdown);
            AppendNeeds(sb, plan, component, markdown);
            AppendTouch(sb, plan, component, markdown);
            AppendConditionGroups(sb, plan, component, markdown);
            AppendGatedTriggers(sb, plan, component, markdown);
            AppendPlatform(sb, component, markdown);
            AppendMessages(sb, plan, component, markdown);

            sb.Append(L.Text("ad.summary.footer")).Append('\n');
            return sb.ToString();
        }

        static void AppendTitle(StringBuilder sb, AvatarDoc component, bool markdown)
        {
            var name = RichName.Strip(component.EffectiveMenuName);
            sb.Append(markdown ? "## " : "").Append(name).Append('\n').Append('\n');
        }

        static void AppendParameters(StringBuilder sb, List<PlannedParameter> normal, List<PlannedParameter> animatorOnly, List<string> mirrors, AvatarDoc component, bool markdown)
        {
            if (normal.Count > 0)
            {
                Heading(sb, "ad.summary.parameters", markdown);
                if (markdown)
                {
                    sb.Append("| Name | Type | ").Append(L.Text("ad.summary.parameters.synced")).Append(" | ")
                      .Append(L.Text("ad.summary.parameters.saved")).Append(" | ").Append(L.Text("ad.summary.parameters.default")).Append(" | Notes |\n");
                    sb.Append("|---|---|---|---|---|---|\n");
                    foreach (var p in normal)
                    {
                        sb.Append("| ").Append(EscapeMarkdown(p.Name)).Append(" | ").Append(p.Type).Append(" | ")
                          .Append(SyncedWord(p)).Append(" | ").Append(YesNo(p.Saved)).Append(" | ")
                          .Append(FormatDefault(p)).Append(" | ").Append(EscapeMarkdown(ParameterNotes(p, component))).Append(" |\n");
                    }
                }
                else
                {
                    int width = normal.Max(p => p.Name.Length) + 2;
                    foreach (var p in normal)
                    {
                        sb.Append("  ").Append(p.Name.PadRight(width)).Append(p.Type.ToString().PadRight(7));
                        sb.Append(SyncedWord(p)).Append(' ');
                        if (p.Saved) sb.Append(L.Text("ad.summary.parameters.saved")).Append(' ');
                        sb.Append(string.Format(L.Text("ad.summary.parameters.default_value"), FormatDefault(p)));
                        var notes = ParameterNotes(p, component);
                        if (notes.Length > 0) sb.Append("  (").Append(notes).Append(')');
                        sb.Append('\n');
                    }
                }
                sb.Append('\n');
            }

            if (animatorOnly.Count > 0 || mirrors.Count > 0)
            {
                Heading(sb, "ad.summary.parameters.animator_only", markdown);
                foreach (var p in animatorOnly)
                {
                    if (markdown) sb.Append("- ").Append(p.Name).Append(" (").Append(p.Type).Append(")\n");
                    else sb.Append("  ").Append(p.Name).Append("  ").Append(p.Type).Append('\n');
                }
                var note = L.Text("ad.summary.parameters.mirror");
                foreach (var name in mirrors)
                {
                    if (markdown) sb.Append("- ").Append(name).Append(" (").Append(PlannedType.Float).Append(", ").Append(note).Append(")\n");
                    else sb.Append("  ").Append(name).Append("  ").Append(PlannedType.Float).Append("  (").Append(note).Append(")\n");
                }
                sb.Append('\n');
            }
        }

        static string ParameterNotes(PlannedParameter p, AvatarDoc component)
        {
            if (p.ReusesExisting) return L.Text("ad.summary.parameters.uses_existing");
            if (p.GroupName != null && p.Controls.Any(c => c.Component != component)) return L.Text("ad.summary.parameters.shared_group");
            return "";
        }

        static string FormatDefault(PlannedParameter p)
        {
            return p.Type == PlannedType.Bool ? (p.Default >= 0.5f ? "1" : "0") : p.Default.ToString(CultureInfo.InvariantCulture);
        }

        static string YesNo(bool value) => L.Text(value ? "ad.summary.yes" : "ad.summary.no");

        /// <summary>What a parameter's own sync state says about it: "synced", or "local" for one that never leaves the client.</summary>
        static string SyncedWord(PlannedParameter p) => L.Text(p.Synced ? "ad.summary.parameters.synced" : "ad.summary.local");

        /// <summary>Escapes a pipe in free text before it goes into a Markdown table cell, so a name or a note with one in it cannot split the row.</summary>
        static string EscapeMarkdown(string text) => (text ?? "").Replace("|", "\\|");

        static void AppendMenu(StringBuilder sb, AvatarPlan plan, AvatarDoc component, bool markdown)
        {
            Heading(sb, "ad.summary.menu", markdown);
            sb.Append(string.IsNullOrEmpty(component.installPath)
                ? L.Text("ad.summary.menu.install_root")
                : string.Format(L.Text("ad.summary.menu.install_path"), component.installPath)).Append('\n');

            var lines = MenuLines(plan.Tree.Root, component, 0);
            if (lines.Count == 0)
            {
                sb.Append(L.Text("ad.summary.menu.none")).Append('\n');
            }
            else
            {
                foreach (var (depth, text) in lines)
                {
                    sb.Append(new string(' ', depth * 2));
                    sb.Append(markdown ? "- " : "").Append(text).Append('\n');
                }
            }
            sb.Append('\n');
        }

        static List<(int depth, string text)> MenuLines(MenuNode node, AvatarDoc component, int depth)
        {
            var lines = new List<(int, string)>();
            foreach (var item in node.Items)
            {
                // A menu edit removed this item from the built menu; it never reaches whoever installs the
                // prefab, so the summary has no business describing it either.
                if (item.Hidden) continue;
                // A toggle or radial hidden from the menu, or a grabbable with no menu toggle, is listed under
                // its own heading instead.
                if (item.Control != null && item.Control.Entry.NoMenuControl) continue;
                if (item.Submenu != null)
                {
                    if (item.Foreign != ForeignSource.None) continue;
                    var sub = item.Submenu;
                    var childLines = MenuLines(sub, component, depth + 1);
                    bool owns = sub.Component == component && sub.EntryId != null;
                    if (childLines.Count == 0 && !owns) continue;
                    int slots = sub.Items.Count(i => !i.Hidden && !i.Excluded && i.Foreign == ForeignSource.None && !(i.Control != null && i.Control.Entry.NoMenuControl));
                    var name = RichName.Strip(string.IsNullOrEmpty(sub.RichName) ? sub.Name : sub.RichName);
                    var subEntry = sub.EntryId != null && sub.Component != null ? sub.Component.FindEntry(sub.EntryId) : null;
                    var note = subEntry != null ? PlatformNote(sub.Component, subEntry) : "";
                    lines.Add((depth, name + note + " (" + string.Format(L.Text("ad.summary.menu.slots"), slots, VRCExpressionsMenu.MAX_CONTROLS, CurrentPlatformName()) + ")"));
                    lines.AddRange(childLines);
                }
                else if (item.Control != null && item.Control.Component == component)
                {
                    var entry = item.Control.Entry;
                    lines.Add((depth, entry.PlainName + PlatformNote(item.Control.Component, entry)));
                }
            }
            return lines;
        }

        /// <summary>The build target the slot count is counted for: the reader has to know which platform "2/8" describes, since a PC only or Quest only entry counts on one but not the other.</summary>
        static string CurrentPlatformName() => L.Text(Platform.IsMobileTarget ? "ad.ui.platform.name.quest" : "ad.ui.platform.name.pc");

        static string PlatformNote(AvatarDoc component, MenuEntry entry) => PlatformNote(Platform.Effective(component, entry));

        static string PlatformNote(AvatarDoc component, AnimatorMerge merge) => PlatformNote(Platform.Effective(component, merge));

        static string PlatformNote(PlatformChoice choice)
        {
            switch (choice)
            {
                case PlatformChoice.PCOnly: return " (" + L.Text("ad.ui.chip.pc_only") + ")";
                case PlatformChoice.MobileOnly: return " (" + L.Text("ad.ui.chip.quest_only") + ")";
                default: return "";
            }
        }

        /// <summary>Toggles and radials hidden from the menu, under their own heading: they have no control and no slot, but they still hold a parameter, a layer and a default.</summary>
        static void AppendHiddenToggles(StringBuilder sb, AvatarPlan plan, AvatarDoc component, bool markdown)
        {
            var hidden = plan.Tree.Controls.Where(c => c.Component == component
                && (c.Entry.kind == EntryKind.Toggle || c.Entry.kind == EntryKind.Radial) && c.Entry.hiddenFromMenu).ToList();
            if (hidden.Count == 0) return;
            Heading(sb, "ad.summary.hidden", markdown);
            foreach (var control in hidden)
                sb.Append(markdown ? "- " : "  ").Append(control.Entry.PlainName + PlatformNote(component, control.Entry)).Append('\n');
            sb.Append('\n');
        }

        /// <summary>Every rule of this component's toggles and triggers, naming the mesh, how many shapes it holds and what at.</summary>
        static void AppendShapeSets(StringBuilder sb, AvatarPlan plan, AvatarDoc component, bool markdown)
        {
            var context = plan.Tree.HeldContext;
            var lines = new List<string>();
            void Add(MenuEntry entry)
            {
                foreach (var rule in ShapeSetModel.EffectiveRules(entry))
                {
                    int count = ShapeSetModel.CoveredShapeCount(entry, rule, context);
                    string key = rule.value == ShapeSetValue.Default ? "ad.summary.shape_sets.line.default" : "ad.summary.shape_sets.line";
                    if (count == 1) key += ".one";
                    lines.Add(string.Format(L.Text(key), entry.PlainName, rule.mesh.name, count));
                }
            }
            foreach (var control in plan.Tree.Controls)
                if (control.Component == component) Add(control.Entry);
            foreach (var trigger in plan.Tree.Triggers)
                if (trigger.Component == component && trigger.Entry.OffersShapeSets) Add(trigger.Entry);
            if (lines.Count == 0) return;
            Heading(sb, "ad.summary.shape_sets", markdown);
            foreach (var line in lines)
                sb.Append(markdown ? "- " : "  ").Append(line).Append('\n');
            sb.Append('\n');
        }

        /// <summary>Every follow of this component's toggles and triggers, naming the object and what it follows.</summary>
        static void AppendFollows(StringBuilder sb, AvatarPlan plan, AvatarDoc component, bool markdown)
        {
            var lines = new List<(string entry, string target, string follows)>();
            foreach (var control in plan.Tree.Controls)
            {
                if (control.Component != component) continue;
                foreach (var follow in control.Entry.follows)
                    if (follow.target != null && follow.follows != null)
                        lines.Add((control.Entry.PlainName, follow.target.name, follow.follows.name));
            }
            foreach (var trigger in plan.Tree.Triggers)
            {
                if (trigger.Component != component) continue;
                foreach (var follow in trigger.Entry.follows)
                    if (follow.target != null && follow.follows != null)
                        lines.Add((trigger.Entry.PlainName, follow.target.name, follow.follows.name));
            }
            if (lines.Count == 0) return;
            Heading(sb, "ad.summary.follows", markdown);
            foreach (var (entry, target, follows) in lines)
                sb.Append(markdown ? "- " : "  ").Append(string.Format(L.Text("ad.summary.follows.line"), entry, target, follows)).Append('\n');
            sb.Append('\n');
        }

        /// <summary>Every grabbable of this component, naming the object and which hand or hands answer to it.</summary>
        static void AppendGrabbables(StringBuilder sb, AvatarPlan plan, AvatarDoc component, bool markdown)
        {
            var lines = new List<(string entry, string obj, string hands)>();
            foreach (var control in plan.Tree.Controls)
            {
                if (control.Component != component || control.Entry.kind != EntryKind.Grabbable || control.Entry.grabObject == null) continue;
                var hands = control.Entry.grabHands == GrabHands.Both ? L.Text("ad.ui.grab.hands.both")
                    : control.Entry.grabHands == GrabHands.Left ? L.Text("ad.ui.grab.hands.left") : L.Text("ad.ui.grab.hands.right");
                lines.Add((control.Entry.PlainName, control.Entry.grabObject.name, hands));
            }
            if (lines.Count == 0) return;
            Heading(sb, "ad.summary.grabbables", markdown);
            foreach (var (entry, obj, hands) in lines)
                sb.Append(markdown ? "- " : "  ").Append(string.Format(L.Text("ad.summary.grabbables.line"), entry, obj, hands)).Append('\n');
            sb.Append('\n');
        }

        /// <summary>Every trigger of this component whose rule is spelled out in groups, in the same words the panel shows above its cards.</summary>
        static void AppendConditionGroups(StringBuilder sb, AvatarPlan plan, AvatarDoc component, bool markdown)
        {
            var grouped = plan.Tree.Triggers.Where(t => t.Component == component && t.Entry.UsesWhenGroups).ToList();
            if (grouped.Count == 0) return;
            Heading(sb, "ad.summary.conditions", markdown);
            foreach (var trigger in grouped)
                sb.Append(markdown ? "- " : "  ")
                  .Append(string.Format(L.Text("ad.summary.conditions.line"), trigger.Entry.PlainName, ConditionLogic.Sentence(trigger.Entry, plan.Tree)))
                  .Append('\n');
            sb.Append('\n');
        }

        /// <summary>Every trigger of this component that only works while a menu switch is in a chosen state.</summary>
        static void AppendGatedTriggers(StringBuilder sb, AvatarPlan plan, AvatarDoc component, bool markdown)
        {
            var gated = plan.Tree.Triggers.Where(t => t.Component == component && t.Entry.HasGate).ToList();
            if (gated.Count == 0) return;
            Heading(sb, "ad.summary.gated_triggers", markdown);
            foreach (var trigger in gated)
            {
                var gate = plan.Tree.FindControl(trigger.Entry.gateToggleId);
                var gateName = gate != null ? gate.Entry.PlainName : "?";
                var state = L.Text(trigger.Entry.gateToggleOn ? "ad.ui.trigger.toggle.on" : "ad.ui.trigger.toggle.off");
                sb.Append(markdown ? "- " : "  ").Append(string.Format(L.Text("ad.summary.gated_triggers.line"), trigger.Entry.PlainName, gateName, state)).Append('\n');
            }
            sb.Append('\n');
        }

        /// <summary>The animator controllers this component merges, with how many of their layers really join the FX controller.</summary>
        static void AppendAnimators(StringBuilder sb, AvatarPlan plan, AvatarDoc component, bool markdown)
        {
            var merges = plan.Tree.Merges.Where(m => m.Component == component && m.Merge.controller != null).ToList();
            if (merges.Count == 0) return;
            Heading(sb, "ad.summary.animators", markdown);
            foreach (var node in merges)
            {
                var layers = MergeBuilder.LayerNames(node.Merge.controller);
                int merged = layers.Count(name => !node.Merge.skipLayers.Contains(name));
                sb.Append(markdown ? "- " : "  ")
                  .Append(string.Format(L.Text("ad.summary.animators.layers"), node.Merge.controller.name, merged))
                  .Append(PlatformNote(component, node.Merge)).Append('\n');
            }
            sb.Append('\n');
        }

        /// <summary>The finished menu assets this component installs, with how many controls each one brings.</summary>
        static void AppendMenuAssets(StringBuilder sb, AvatarPlan plan, AvatarDoc component, bool markdown)
        {
            var installs = plan.Tree.MenuAssets.Where(a => a.Component == component && a.Install.menu != null).ToList();
            if (installs.Count == 0) return;
            Heading(sb, "ad.summary.menu_assets", markdown);
            foreach (var node in installs)
            {
                int controls = node.Install.menu.controls != null ? node.Install.menu.controls.Count : 0;
                sb.Append(markdown ? "- " : "  ")
                  .Append(string.Format(L.Text("ad.summary.menu_assets.line"), node.Install.menu.name, controls))
                  .Append('\n');
            }
            sb.Append('\n');
        }

        /// <summary>The parameters this component declares itself, with the bits each one costs.</summary>
        static void AppendDeclarations(StringBuilder sb, AvatarDoc component, bool markdown)
        {
            var declarations = component.parameters.Where(p => p != null && !string.IsNullOrWhiteSpace(p.name)).ToList();
            if (declarations.Count == 0) return;
            Heading(sb, "ad.summary.declared", markdown);
            foreach (var declaration in declarations)
                sb.Append(markdown ? "- " : "  ")
                  .Append(string.Format(L.Text("ad.summary.declared.line"), declaration.name, declaration.type, declaration.Bits))
                  .Append('\n');
            sb.Append('\n');
        }

        /// <summary>
        /// What this component's "Remove from avatar" edits take out: the control that goes, and under it every
        /// parameter that is freed with it or the reason one has to stay. Whoever installs the prefab is giving
        /// something up here, so the summary says what.
        /// </summary>
        static void AppendRemovals(StringBuilder sb, AvatarPlan plan, AvatarDoc component, bool markdown)
        {
            var edits = component.menuEdits.Where(e => e != null && e.remove).ToList();
            if (edits.Count == 0) return;
            var removal = plan.Tree != null ? plan.Tree.Removal : null;
            Heading(sb, "ad.summary.removed", markdown);
            foreach (var edit in edits)
            {
                sb.Append(markdown ? "- " : "  ").Append(string.Join("/", edit.source)).Append('\n');
                if (removal == null) continue;
                foreach (var parameter in removal.For(edit))
                    sb.Append(markdown ? "  - " : "    ")
                      .Append(parameter.Freed
                          ? string.Format(L.Text("ad.summary.removed.freed"), parameter.Name, parameter.Bits)
                          : string.Format(L.Text("ad.summary.removed.kept"), parameter.Name, L.Text(parameter.KeptBecause)))
                      .Append('\n');
            }
            sb.Append('\n');
        }

        /// <summary>The outfit pieces of this avatar that carry blend shape rules of their own, with how many rows each one has. They belong to no component, so every summary of the avatar names them.</summary>
        static void AppendShapes(StringBuilder sb, AvatarPlan plan, bool markdown)
        {
            var root = plan.Tree != null ? plan.Tree.AvatarRoot : null;
            if (root == null) return;
            var pieces = ShapesModel.Components(root);
            if (pieces.Count == 0) return;
            Heading(sb, "ad.summary.shapes", markdown);
            foreach (var piece in pieces)
                sb.Append(markdown ? "- " : "  ")
                  .Append(string.Format(L.Text("ad.summary.shapes.line"), piece.gameObject.name, piece.follow.Count, piece.whileOn.Count))
                  .Append('\n');
            sb.Append('\n');
        }

        static void AppendNeeds(StringBuilder sb, AvatarPlan plan, AvatarDoc component, bool markdown)
        {
            Heading(sb, "ad.summary.needs", markdown);
            var needs = new List<string> { L.Text("ad.summary.needs.ndmf"), L.Text("ad.summary.needs.vrcsdk") };
            needs.AddRange(ExternalNeeds(plan, component));
            foreach (var need in needs) sb.Append(markdown ? "- " : "  ").Append(need).Append('\n');
            sb.Append('\n');
        }

        /// <summary>Modular Avatar or VRCFury, only when this component reads a parameter one of them provides: a trigger's parameter source, or a preset's named parameter.</summary>
        static IEnumerable<string> ExternalNeeds(AvatarPlan plan, AvatarDoc component)
        {
            var names = new List<string>();
            foreach (var entry in component.entries)
            {
                if (entry.kind == EntryKind.Trigger)
                {
                    foreach (var group in entry.EffectiveWhen)
                        foreach (var condition in group.all)
                        {
                            if (condition == null || condition.kind != WhenConditionKind.Parameter) continue;
                            var name = (condition.sourceParameter ?? "").Trim();
                            if (name.Length > 0) names.Add(name);
                        }
                }
                else if (entry.kind == EntryKind.Preset)
                {
                    foreach (var parameter in entry.presetParameters)
                    {
                        var name = (parameter.name ?? "").Trim();
                        if (name.Length > 0) names.Add(name);
                    }
                }
            }
            if (names.Count == 0 || plan.AvatarRoot == null) yield break;

            var vrcFuryGlobals = new HashSet<string>(VrcFuryReader.GlobalParameters(plan.AvatarRoot));
            var provided = AvatarPlanCache.ProvidedFor(plan.AvatarRoot);
            bool needsMa = false, needsFury = false;
            foreach (var name in names.Distinct())
            {
                if (vrcFuryGlobals.Contains(name)) needsFury = true;
                else if (provided.Any(p => p.EffectiveName == name && IsModularAvatarSource(p.Source))) needsMa = true;
            }
            if (needsMa) yield return L.Text("ad.summary.needs.modular_avatar");
            if (needsFury) yield return L.Text("ad.summary.needs.vrcfury");
        }

        /// <summary>Recognises a Modular Avatar component by its runtime type name, the same way VrcFuryReader recognises VRCFury: no compile-time reference to either plugin.</summary>
        static bool IsModularAvatarSource(Component source)
        {
            var fullName = source != null ? source.GetType().FullName : null;
            return fullName != null && fullName.StartsWith("nadena.dev.modular_avatar.");
        }

        static void AppendTouch(StringBuilder sb, AvatarPlan plan, AvatarDoc component, bool markdown)
        {
            int contacts = 0, physBones = 0;
            var limited = new List<string>();
            foreach (var trigger in plan.Tree.Triggers)
            {
                // A store page written on PC still has to name the Quest only sources, so the count follows the
                // component's own platform choices rather than the build target the editor happens to be on.
                if (trigger.Component != component || !trigger.Entry.HasEffects) continue;
                int before = contacts + physBones;
                foreach (var group in trigger.Entry.EffectiveWhen)
                    foreach (var condition in group.all)
                    {
                        if (condition == null) continue;
                        if (condition.kind == WhenConditionKind.Contact) contacts++;
                        else if (condition.kind == WhenConditionKind.PhysBone) physBones++;
                    }
                if (contacts + physBones == before) continue;
                var note = PlatformNote(component, trigger.Entry);
                if (note.Length > 0) limited.Add(trigger.Entry.PlainName + note);
            }
            if (contacts == 0 && physBones == 0) return;
            Heading(sb, "ad.summary.touch", markdown);
            if (contacts > 0) sb.Append(markdown ? "- " : "  ").Append(UiText.Count(contacts, "ad.summary.touch.contacts.one", "ad.summary.touch.contacts")).Append('\n');
            if (physBones > 0) sb.Append(markdown ? "- " : "  ").Append(UiText.Count(physBones, "ad.summary.touch.physbones.one", "ad.summary.touch.physbones")).Append('\n');
            foreach (var name in limited) sb.Append(markdown ? "- " : "  ").Append(name).Append('\n');
            sb.Append('\n');
        }

        static void AppendPlatform(StringBuilder sb, AvatarDoc component, bool markdown)
        {
            var label = component.platform == PlatformChoice.PCOnly ? L.Text("ad.ui.platform.pc")
                : component.platform == PlatformChoice.MobileOnly ? L.Text("ad.ui.platform.quest")
                : L.Text("ad.ui.platform.both");
            sb.Append(string.Format(L.Text("ad.summary.platform"), label)).Append('\n').Append('\n');
        }

        /// <summary>The messages this component's own entries or menu edits raise right now.</summary>
        static void AppendMessages(StringBuilder sb, AvatarPlan plan, AvatarDoc component, bool markdown)
        {
            var ownIds = new HashSet<string>(component.entries.Select(e => e.id));
            var ownEdits = new HashSet<string>(component.menuEdits.Select(e => e.id));
            var messages = plan.Messages.Where(m =>
                (m.Args != null && m.Args.Contains(component)) ||
                m.EntryIds.Any(ownIds.Contains) ||
                (!string.IsNullOrEmpty(m.EditId) && ownEdits.Contains(m.EditId))
            ).Distinct().ToList();
            if (messages.Count == 0) return;
            Heading(sb, "ad.summary.messages", markdown);
            foreach (var message in messages)
            {
                var severity = message.Severity == MessageSeverity.Error ? L.Text("ad.summary.messages.error")
                    : message.Severity == MessageSeverity.Warning ? L.Text("ad.summary.messages.warning")
                    : L.Text("ad.summary.messages.info");
                sb.Append(markdown ? "- " : "  ").Append(severity).Append(": ").Append(UiText.Format(message)).Append('\n');
            }
            sb.Append('\n');
        }

        static void Heading(StringBuilder sb, string key, bool markdown)
        {
            sb.Append(markdown ? "## " : "").Append(L.Text(key)).Append('\n');
        }
    }
}
#endif
