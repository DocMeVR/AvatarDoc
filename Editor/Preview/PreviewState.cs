#if DOCME_NDMF && DOCME_VRCSDK3A
using System.Collections.Immutable;
using nadena.dev.ndmf.preview;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Preview overrides keyed by entry id: 1 or 0 for toggles and triggers, 0..1 for radials. No key means the default state.</summary>
    public static class PreviewState
    {
        public static readonly PublishedValue<ImmutableDictionary<string, float>> Overrides =
            new PublishedValue<ImmutableDictionary<string, float>>(ImmutableDictionary<string, float>.Empty, "AvatarDoctor preview overrides");

        public static void Set(string entryId, float value)
        {
            if (string.IsNullOrEmpty(entryId)) return;
            Overrides.Value = Overrides.Value.SetItem(entryId, value);
        }

        public static void Clear(string entryId)
        {
            if (string.IsNullOrEmpty(entryId)) return;
            Overrides.Value = Overrides.Value.Remove(entryId);
        }

        public static void ClearAll() => Overrides.Value = ImmutableDictionary<string, float>.Empty;

        public static bool TryGet(string entryId, out float value) => Overrides.Value.TryGetValue(entryId ?? "", out value);

        /// <summary>The effective state of a toggle or a radial: its override, else the default the parameter plan bound it to. Used by the wheel and other quick previews that only have the entry and the plan, not a scene reader.</summary>
        public static float Effective(MenuEntry entry, ParameterPlan plan)
        {
            if (entry == null) return 0f;
            if (TryGet(entry.id, out var value)) return value;
            var binding = BindingFor(entry, plan);
            switch (entry.kind)
            {
                case EntryKind.Toggle: return (binding != null ? binding.OnByDefault : entry.defaultOn) ? 1f : 0f;
                case EntryKind.Radial: return Mathf.Clamp01(binding != null ? binding.DefaultValue : entry.defaultValue);
            }
            return 0f;
        }

        /// <summary>
        /// The effective state with the exclusive groups taken into account: a member that is not its group's
        /// winner reads as off, whatever its own default says. The wheel, the trigger strip and the panel's
        /// preview row ask through here, so they agree with what PreviewModel draws in the scene.
        /// </summary>
        public static float Effective(MenuEntry entry, MenuTree tree, ParameterPlan plan)
        {
            if (entry == null) return 0f;
            if (tree != null && entry.kind == EntryKind.Toggle && !string.IsNullOrWhiteSpace(entry.exclusiveGroup))
            {
                var winners = PreviewModel.GroupWinners(tree, Overrides.Value);
                return winners.TryGetValue(entry.exclusiveGroup.Trim(), out var winner) && winner.Entry == entry ? 1f : 0f;
            }
            return Effective(entry, plan);
        }

        /// <summary>Whether a trigger's gate is met right now: always true with no gate, else whether the gate toggle's effective state (group membership included) matches gateToggleOn. A gate that does not resolve to a toggle is treated as always met; the model reports the broken reference on its own.</summary>
        public static bool GateMet(MenuEntry trigger, MenuTree tree, ParameterPlan plan)
        {
            if (trigger == null || string.IsNullOrEmpty(trigger.gateToggleId)) return true;
            var gate = tree?.FindControl(trigger.gateToggleId);
            if (gate == null || gate.Entry.kind != EntryKind.Toggle) return true;
            bool on = Effective(gate.Entry, tree, plan) >= 0.5f;
            return trigger.gateToggleOn == on;
        }

        /// <summary>Switches a control on the way the panel's On button and a test-mode click do: a group member's siblings are cleared, so an old override on a sibling cannot linger and win instead.</summary>
        public static void SetOn(MenuEntry entry, MenuTree tree)
        {
            if (entry == null) return;
            Set(entry.id, 1f);
            ClearSiblings(entry, tree);
        }

        /// <summary>Sets every other member of this toggle's exclusive group to 0. Does nothing for a toggle without a group.</summary>
        static void ClearSiblings(MenuEntry toggle, MenuTree tree)
        {
            if (tree == null || toggle.kind != EntryKind.Toggle || string.IsNullOrWhiteSpace(toggle.exclusiveGroup)) return;
            var group = toggle.exclusiveGroup.Trim();
            foreach (var other in tree.Controls)
            {
                if (other.Entry == toggle || other.Entry.kind != EntryKind.Toggle) continue;
                if (string.IsNullOrWhiteSpace(other.Entry.exclusiveGroup) || other.Entry.exclusiveGroup.Trim() != group) continue;
                Set(other.Entry.id, 0f);
            }
        }

        static ControlBinding BindingFor(MenuEntry entry, ParameterPlan plan)
        {
            if (plan == null) return null;
            foreach (var pair in plan.Bindings)
                if (pair.Key.Entry == entry) return pair.Value;
            return null;
        }

        /// <summary>Flips a toggle's effective state on to off and back, as a test-mode wedge click does. Switching a group member on clears its siblings' overrides to 0, the same rule ApplyPreset follows.</summary>
        public static void Flip(MenuEntry toggle, MenuTree tree, ParameterPlan plan)
        {
            if (toggle == null || toggle.kind != EntryKind.Toggle) return;
            bool next = Effective(toggle, tree, plan) < 0.5f;
            Set(toggle.id, next ? 1f : 0f);
            if (next) ClearSiblings(toggle, tree);
        }

        /// <summary>Applies a preset's states as one-off overrides: a group member set on clears its siblings, so an old override cannot linger and win instead.</summary>
        public static void ApplyPreset(MenuEntry preset, MenuTree tree)
        {
            if (preset == null || tree == null) return;
            foreach (var state in preset.presetStates)
            {
                var control = tree.FindControl(state.entryId);
                if (control == null) continue;
                var entry = control.Entry;
                if (entry.kind == EntryKind.Radial)
                {
                    Set(entry.id, Mathf.Clamp01(state.value));
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(entry.exclusiveGroup))
                {
                    var group = entry.exclusiveGroup.Trim();
                    Set(entry.id, state.on ? 1f : 0f);
                    if (state.on) ClearSiblings(entry, tree);
                    continue;
                }
                Set(entry.id, state.on ? 1f : 0f);
            }
        }
    }
}
#endif
