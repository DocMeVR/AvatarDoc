#if DOCME_NDMF
using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.localization;
using UnityEditor;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Every text AvatarDoc shows, by key, in the language NDMF and Modular Avatar share. Each language is one file
    /// (LanguageTables); English is the default and fills any key a language lacks. The key lists below name every key
    /// the code uses, so a test can check English has them all.
    /// </summary>
    public static class L
    {
        public static readonly string[] KnownKeys =
        {
            "ad.info.path_created", "ad.info.same_submenu_merged", "ad.info.menu_split", "ad.warn.orphan_entry",
            "ad.warn.target_missing", "ad.warn.no_effects", "ad.warn.group_multiple_defaults", "ad.error.group_value_conflict",
            "ad.error.parameter_type_conflict", "ad.error.bits_exceeded", "ad.warn.bits_exceeded_vrcfury", "ad.error.no_fx_layer", "ad.info.curve_overwritten",
            "ad.warn.marker_clip_skipped", "ad.warn.trigger_source_missing", "ad.warn.parameter_type_unknown",
            "ad.warn.duplicate_entry_id", "ad.info.parameter_reused", "ad.warn.target_outside_avatar", "ad.info.target_shared",
            "ad.info.target_shared_override", "ad.warn.link_effect_is_source", "ad.info.parameter_animator_only", "ad.warn.replace_kept_source",
            "ad.info.override_applied", "ad.info.override_applied.one", "ad.warn.override_unused", "ad.warn.override_conflict", "ad.warn.override_target_gone",
            "ad.error.edit_cycle", "ad.warn.edit_own_entry", "ad.warn.edit_source_missing",
            "ad.warn.preset_empty", "ad.warn.preset_target_missing", "ad.info.preset_pick_control", "ad.error.preset_group_conflict", "ad.warn.preset_parameter_unknown",
            "ad.warn.property_missing", "ad.warn.property_differs", "ad.info.blend_ignored_in_group",
            "ad.warn.poiyomi_not_animated", "ad.warn.poiyomi_not_animated.one", "ad.warn.poiyomi_baked", "ad.warn.poiyomi_baked.one",
            "ad.warn.animation_proxy", "ad.info.animation_overridden", "ad.info.animation_overridden.one", "ad.warn.animation_binding_missing", "ad.warn.animation_binding_missing.one",
            "ad.import.note.unsupported_action", "ad.import.note.transition", "ad.import.note.security",
            "ad.import.note.separate_local", "ad.import.note.hold_button", "ad.import.note.invert_rest_logic",
            "ad.import.note.extra_tags", "ad.import.note.clip_paths", "ad.import.note.shape_changer_delete",
            "ad.import.note.reactive_inverted", "ad.import.note.slot_missing",
            "ad.import.reason.no_supported_action", "ad.import.reason.not_a_toggle", "ad.import.reason.ma_merge_animator", "ad.import.reason.nothing_here", "ad.import.reason.nothing_under",
            "ad.import.note.fx_path_missing", "ad.import.reason.property_partial",
            "ad.import.reason.fx_no_layer", "ad.import.reason.fx_parameter_shared",
            "ad.import.reason.fx_other_condition", "ad.import.reason.fx_no_on_state", "ad.import.reason.fx_blend_tree",
            "ad.import.reason.fx_no_motion", "ad.import.reason.fx_unsupported_curve", "ad.warn.removed_layer_missing",
            "ad.import.reason.shared_layer", "ad.import.reason.shared_parameter_controls", "ad.import.reason.behaviours",
            "ad.import.reason.layer_control", "ad.import.reason.layer_masked", "ad.import.reason.transition_chain",
            "ad.import.note.driven_elsewhere", "ad.import.note.off_values", "ad.warn.removed_layer_unsafe",
            "ad.import.reason.edit_locked", "ad.import.reason.already_converted", "ad.import.reason.layer_name_not_unique", "ad.warn.removed_layer_ambiguous",
            "ad.import.reason.off_values_differ", "ad.import.reason.inverted_default_mismatch",
            "ad.import.reason.group_needs_removal", "ad.import.note.osc",
            "ad.import.reason.radial_shape", "ad.import.reason.radial_bindings", "ad.import.reason.synced_layer",
            "ad.import.reason.ma_renamed_parameters", "ad.import.note.merge_converted",
            "ad.import.note.merge_parameter_control", "ad.import.note.merge_button", "ad.import.note.ma_radio_set",
            "ad.error.contact_no_anchor", "ad.error.contact_no_tags", "ad.error.contact_nobody", "ad.error.physbone_missing",
            "ad.error.flip_target", "ad.error.touch_parameter_type", "ad.error.touch_outside_avatar",
            "ad.info.flip_target_excluded", "ad.warn.platform_parity",
            "ad.warn.physbone_renamed", "ad.info.group_default_excluded",
            "ad.warn.merge_no_controller", "ad.warn.merge_override_no_controller", "ad.warn.merge_skip_missing", "ad.error.merge_outside_avatar",
            "ad.error.explicit_off_pin",
            "ad.error.merge_parameter_type", "ad.error.merge_layer_control", "ad.error.merge_synced_layer",
            "ad.warn.parameter_control_unused",
            "ad.info.removed_parameter", "ad.info.removed_parameter.one", "ad.info.removed_kept",
            "ad.removal.kept.control", "ad.removal.kept.entry", "ad.removal.kept.layer",
            "ad.removal.kept.other_controller", "ad.removal.kept.driver", "ad.removal.kept.contact",
            "ad.removal.kept.builtin", "ad.removal.kept.other_tool",
            "ad.warn.converted_layer_unsafe",
            "ad.error.gate_target", "ad.info.hidden_toggle_unreachable",
            "ad.error.trigger_too_complex", "ad.error.trigger_too_complex.one", "ad.warn.trigger_no_condition", "ad.warn.condition_group_empty",
            "ad.warn.menu_asset_missing", "ad.warn.parameter_declared_twice", "ad.error.parameter_declared_type",
            "ad.warn.parameter_undeclared", "ad.import.note.takeover",
            "ad.info.shapes_follow_kept_own", "ad.info.shapes_always_on", "ad.info.shapes_never_on", "ad.warn.shapes_active_not_constant",
            "ad.warn.shapes_clip_owns_shape", "ad.warn.shapes_conflict", "ad.info.shapes_larger_wins", "ad.warn.shapes_missing_shape",
            "ad.error.shapes_outside_avatar", "ad.error.shapes_no_mesh", "ad.import.reason.shapes_delete",
            "ad.info.capture_nothing_above_zero", "ad.info.shape_set_count", "ad.info.shape_set_count.one",
            "ad.info.shape_set_count.default", "ad.info.shape_set_count.default.one", "ad.info.shape_set_pruned", "ad.info.shape_set_pruned.one",
            "ad.error.shape_set_outside_avatar", "ad.error.shape_set_not_a_toggle",
            "ad.error.follow_not_on_off", "ad.error.follow_self", "ad.error.follow_outside_avatar", "ad.error.follow_existing_constraint",
            "ad.error.follow_no_rest_parent", "ad.error.follow_too_many_sources", "ad.warn.follow_claim_unresolved",
            "ad.error.grab_no_object", "ad.error.grab_outside_avatar", "ad.error.grab_no_hand", "ad.info.grab_uses_existing_physbone",
            "ad.error.grab_inside_physbone", "ad.warn.grab_existing_physbone_unsuitable",

            // Upload style checks mirrored from the VRChat SDK's own build validation.
            "ad.error.menu_icon_too_large", "ad.warn.write_defaults_mixed",

            // The tag filter's own drag-off note, split by which filter is active.
            "ad.info.drag_needs_no_search_or_tags", "ad.info.drag_needs_no_tags",

            // Contacts that follow distance.
            "ad.warn.follow_distance_one_contact", "ad.warn.follow_distance_no_follows", "ad.warn.follow_distance_tracking_left_to",

            // Ready made contact starts.
            "ad.error.contact_start_no_head",

            // Smooth blend shape triggers and requirements.
            "ad.warn.link_clips_outside_fx", "ad.warn.link_source_tracked", "ad.warn.smooth_range", "ad.warn.requirements_one_card", "ad.warn.link_no_fx_clip",
        };

        public static readonly string[] UiKeys =
        {
            "ad.ui.menu", "ad.ui.install", "ad.ui.install.root", "ad.ui.install.pick", "ad.ui.install.mode", "ad.ui.install.mode.append", "ad.ui.install.mode.submenu", "ad.ui.menu_name", "ad.ui.menu_icon",
            "ad.ui.prefix", "ad.ui.memory.avatar", "ad.ui.memory.avatar.one", "ad.ui.memory.ad", "ad.ui.memory.ad.one", "ad.ui.memory.free", "ad.ui.memory.free_bits", "ad.ui.memory.over", "ad.ui.memory.over.one", "ad.ui.disabled", "ad.ui.no_avatar",
            "ad.ui.other_components", "ad.ui.other_components.one", "ad.ui.add.toggle", "ad.ui.add.radial", "ad.ui.add.submenu", "ad.ui.triggers", "ad.ui.triggers.note", "ad.ui.triggers.note.one",
            "ad.ui.existing", "ad.ui.disabled_chip", "ad.ui.drop.tree", "ad.ui.detail.toggle", "ad.ui.detail.radial", "ad.ui.detail.submenu",
            "ad.ui.detail.trigger", "ad.ui.detail.none", "ad.ui.name", "ad.ui.icon", "ad.ui.default_on", "ad.ui.default_value", "ad.ui.saved",
            "ad.ui.synced", "ad.ui.group", "ad.ui.group.none", "ad.ui.group.settings", "ad.ui.parameter", "ad.ui.parameter.auto",
            "ad.ui.parameter.custom", "ad.ui.parameter.use_custom", "ad.ui.parameter.bits", "ad.ui.parameter.bits.one", "ad.ui.parameter.no_bits",
            "ad.ui.parameter.value", "ad.ui.parameter.group_note", "ad.ui.group.value", "ad.ui.group.number", "ad.ui.poiyomi.mark_animated", "ad.ui.slots",
            "ad.ui.objects", "ad.ui.blendshapes", "ad.ui.materials", "ad.ui.add",
            "ad.ui.seg.shown", "ad.ui.seg.hidden", "ad.ui.drop.objects",
            "ad.ui.weight", "ad.ui.min", "ad.ui.max", "ad.ui.slot", "ad.ui.other_component", "ad.ui.select_component", "ad.ui.existing_note",
            "ad.ui.trigger_note", "ad.ui.menu.rename", "ad.ui.menu.duplicate", "ad.ui.menu.delete", "ad.ui.menu.move", "ad.ui.menu.move.root", "ad.ui.pick.shape",
            "ad.ui.pick.renderer", "ad.ui.add.trigger", "ad.ui.trigger.when", "ad.ui.trigger.source.toggle", "ad.ui.trigger.source.parameter",
            "ad.ui.trigger.source.blendshape", "ad.ui.trigger.toggle", "ad.ui.trigger.no_toggles", "ad.ui.trigger.toggle.on", "ad.ui.trigger.toggle.off",
            "ad.ui.trigger.parameter", "ad.ui.trigger.condition", "ad.ui.trigger.condition.is_true", "ad.ui.trigger.condition.is_false",
            "ad.ui.trigger.condition.equals", "ad.ui.trigger.condition.not_equals", "ad.ui.trigger.condition.greater", "ad.ui.trigger.condition.less", "ad.ui.trigger.value", "ad.ui.trigger.shape",
            "ad.ui.trigger.mode", "ad.ui.trigger.link_note", "ad.ui.trigger.parameter_note", "ad.ui.effect.fixed", "ad.ui.effect.follow",
            "ad.ui.seg.fixed", "ad.ui.seg.pin", "ad.ui.effect.multiplier", "ad.ui.override.value", "ad.ui.override.usage", "ad.ui.override.usage.one", "ad.ui.override.unused",
            "ad.ui.usage.chip", "ad.ui.usage.chip.one", "ad.ui.pick.parameter.avatar", "ad.ui.pick.parameter.doctor",
            "ad.ui.pick.parameter.vrchat", "ad.ui.preview", "ad.ui.preview.default", "ad.ui.preview.on", "ad.ui.preview.off",
            "ad.ui.preview.follows", "ad.ui.preview.off_note", "ad.ui.preview.reset", "ad.ui.preview.always",
            "ad.ui.wheel", "ad.ui.wheel.count", "ad.ui.wheel.select", "ad.ui.wheel.test", "ad.ui.wheel.reset", "ad.ui.copy", "ad.ui.root_menu", "ad.ui.window.none",
            "ad.ui.chip.trigger_off", "ad.ui.trigger.source_disabled",
            "ad.ui.add.override", "ad.ui.overrides", "ad.ui.overrides.note", "ad.ui.overrides.note.one", "ad.ui.detail.override", "ad.ui.override_note",
            "ad.ui.style", "ad.ui.style.preview", "ad.ui.style.bold", "ad.ui.style.italic", "ad.ui.style.raw_label", "ad.ui.style.custom",
            "ad.ui.capture", "ad.ui.capture.title", "ad.ui.capture.fit", "ad.ui.capture.background", "ad.ui.capture.transparent",
            "ad.ui.capture.solid", "ad.ui.capture.save", "ad.ui.capture.none", "ad.ui.capture.saved",
            "ad.ui.capture.fill", "ad.ui.capture.fill.tip",
            "ad.ui.chip.modular_avatar", "ad.ui.chip.vrcfury",
            "ad.ui.existing.title", "ad.ui.existing.move", "ad.ui.existing.stay", "ad.ui.existing.reset",
            "ad.ui.existing.late_note", "ad.ui.existing.original", "ad.ui.existing.hide", "ad.ui.existing.show",
            "ad.ui.existing.move_refused",
            "ad.ui.existing.remove", "ad.ui.existing.remove.tip", "ad.ui.existing.remove_frees", "ad.ui.existing.remove_frees.bits", "ad.ui.existing.remove_frees.bits.one",
            "ad.ui.existing.remove_frees.layers", "ad.ui.existing.remove_frees.layers.one", "ad.ui.existing.remove_keeps",
            "ad.ui.chip.edited", "ad.ui.chip.hidden", "ad.ui.chip.removed", "ad.ui.edit.remove",
            "ad.ui.add.preset", "ad.ui.detail.preset", "ad.ui.preset.sets", "ad.ui.preset.add", "ad.ui.preset.from_preview",
            "ad.ui.preset.from_preview.tip", "ad.ui.preset.on", "ad.ui.preset.off", "ad.ui.preset.parameters",
            "ad.ui.preset.parameters.tip", "ad.ui.preset.none", "ad.ui.preset.missing", "ad.ui.preset.pick",
            "ad.ui.targets.properties", "ad.ui.targets.animations", "ad.ui.property.pick", "ad.ui.property.other",
            "ad.ui.property.type.float", "ad.ui.property.type.color",
            "ad.ui.property.type.vector", "ad.ui.animation.path_root", "ad.ui.animation.path_root.tip",
            "ad.ui.animations.no_preview", "ad.ui.blend", "ad.ui.blend.tip", "ad.ui.blend.group_note",
            "ad.ui.import", "ad.ui.import.tooltip", "ad.ui.import.intro", "ad.ui.import.select_all", "ad.ui.import.select_none",
            "ad.ui.import.none", "ad.ui.import.remove", "ad.ui.import.remove.tip", "ad.ui.import.button", "ad.ui.import.button.one",
            "ad.ui.import.done", "ad.ui.import.layers", "ad.ui.import.restore", "ad.ui.import.group.fx",
            "ad.ui.import.group.animator", "ad.ui.import.kind.animator", "ad.ui.import.summary.merged_layers", "ad.ui.import.summary.merged_layers.one",
            "ad.ui.import.summary.objects", "ad.ui.import.summary.objects.one", "ad.ui.import.summary.blendshapes", "ad.ui.import.summary.blendshapes.one",
            "ad.ui.import.summary.materials", "ad.ui.import.summary.materials.one", "ad.ui.import.summary.properties", "ad.ui.import.summary.properties.one",
            "ad.ui.import.summary.animations", "ad.ui.import.summary.animations.one", "ad.ui.convert", "ad.ui.ok",
            "ad.ui.import.rescanned",
            "ad.ui.trigger.source.contact", "ad.ui.trigger.source.physbone", "ad.ui.touch.anchor",
            "ad.ui.touch.radius", "ad.ui.touch.offset", "ad.ui.touch.tags", "ad.ui.touch.more_tags", "ad.ui.touch.custom_tag",
            "ad.ui.touch.who", "ad.ui.touch.self", "ad.ui.touch.others", "ad.ui.touch.distance", "ad.ui.touch.touch",
            "ad.ui.touch.closer", "ad.ui.touch.physbone", "ad.ui.touch.event", "ad.ui.touch.event.grabbed", "ad.ui.touch.event.posed",
            "ad.ui.touch.event.angle", "ad.ui.touch.event.stretch", "ad.ui.touch.event.squish", "ad.ui.touch.threshold",
            "ad.ui.touch.mode", "ad.ui.touch.hold", "ad.ui.touch.flip", "ad.ui.touch.follow", "ad.ui.touch.flip_target", "ad.ui.touch.simulate",
            "ad.ui.touch.scene_hint", "ad.ui.touch.fullat", "ad.ui.touch.fullat_note",
            "ad.outfit.everything_off", "ad.outfit.none", "ad.outfit.default_name",
            "ad.ui.add.outfit", "ad.ui.outfit.title", "ad.ui.outfit.intro", "ad.ui.outfit.name", "ad.ui.outfit.mode",
            "ad.ui.outfit.mode.one", "ad.ui.outfit.mode.mix", "ad.ui.outfit.none_option", "ad.ui.outfit.add",
            "ad.ui.outfit.default", "ad.ui.outfit.drop", "ad.ui.outfit.hidden", "ad.ui.outfit.shrinks",
            "ad.ui.outfit.show_all", "ad.ui.outfit.capture", "ad.ui.outfit.create", "ad.ui.outfit.empty",
            "ad.ui.outfit.split.each", "ad.ui.outfit.split.all", "ad.ui.outfit.outside", "ad.ui.outfit.created", "ad.ui.outfit.add_piece",
            "ad.ui.capture_missing", "ad.ui.capture_missing.done", "ad.ui.capture_missing.done.one",
            "ad.ui.platform", "ad.ui.platform.inherit", "ad.ui.platform.both", "ad.ui.platform.pc", "ad.ui.platform.quest",
            "ad.ui.platform.current", "ad.ui.platform.name.pc", "ad.ui.platform.name.quest",
            "ad.ui.chip.pc_only", "ad.ui.chip.quest_only",
            "ad.ui.summary", "ad.ui.summary.plain", "ad.ui.summary.markdown", "ad.ui.summary.copy", "ad.ui.summary.copied",
            "ad.ui.lock", "ad.ui.lock.tip", "ad.ui.lock.note", "ad.ui.locked", "ad.ui.locked.refused", "ad.ui.unlock",
            "ad.summary.local", "ad.summary.bits", "ad.summary.bits.adds", "ad.summary.bits.adds.one", "ad.summary.bits.total", "ad.summary.bits.total.one", "ad.summary.platform", "ad.summary.parameters", "ad.summary.parameters.animator_only",
            "ad.summary.parameters.synced", "ad.summary.parameters.saved", "ad.summary.parameters.default",
            "ad.summary.parameters.default_value", "ad.summary.parameters.uses_existing", "ad.summary.parameters.shared_group",
            "ad.summary.yes", "ad.summary.no", "ad.summary.menu", "ad.summary.menu.install_path", "ad.summary.menu.install_root",
            "ad.summary.menu.none", "ad.summary.menu.slots", "ad.summary.needs", "ad.summary.needs.ndmf", "ad.summary.needs.vrcsdk",
            "ad.summary.needs.modular_avatar", "ad.summary.needs.vrcfury", "ad.summary.touch", "ad.summary.touch.contacts", "ad.summary.touch.contacts.one",
            "ad.summary.touch.physbones", "ad.summary.touch.physbones.one", "ad.summary.messages", "ad.summary.messages.info", "ad.summary.messages.warning",
            "ad.summary.messages.error", "ad.summary.footer",
            "ad.summary.animators", "ad.summary.animators.layers", "ad.summary.animators.layers.one",
            "ad.ui.animators", "ad.ui.animators.note", "ad.ui.animator_note", "ad.ui.animator.unnamed", "ad.ui.detail.animator",
            "ad.ui.add.animator", "ad.ui.animator.controller", "ad.ui.animator.match_write_defaults", "ad.ui.animator.layers",
            "ad.ui.animator.parameters", "ad.ui.animator.no_preview", "ad.ui.animator.existing", "ad.ui.animator.not_synced", "ad.ui.animator.declared_synced", "ad.ui.animator.declared_local",
            "ad.ui.animator.add_control", "ad.ui.parameter_control_note",
            "ad.ui.target.explicit_off", "ad.ui.target.explicit_off.tip", "ad.ui.target.on_value", "ad.ui.target.off_value",
            "ad.ui.trigger.gate", "ad.ui.trigger.gate.always", "ad.ui.trigger.gate.add",
            "ad.ui.in_menu", "ad.ui.chip.no_menu", "ad.ui.chip.problem", "ad.ui.wheel.not_in_menu",
            "ad.summary.hidden", "ad.summary.gated_triggers", "ad.summary.gated_triggers.line",
            "ad.ui.fires", "ad.ui.when.any_of", "ad.ui.when.all_of", "ad.ui.when.add_and", "ad.ui.when.add_or",
            "ad.ui.when.remove", "ad.ui.when.remove_group", "ad.ui.when.settings", "ad.ui.when.chip_more", "ad.ui.when.kind.toggle", "ad.ui.when.kind.parameter",
            "ad.ui.when.kind.contact", "ad.ui.when.kind.physbone", "ad.ui.when.note", "ad.ui.when.pick_toggle",
            "ad.ui.when.sentence", "ad.ui.when.and", "ad.ui.when.or", "ad.ui.when.nothing",
            "ad.ui.when.missing", "ad.ui.when.position", "ad.ui.when.with_gate",
            "ad.ui.when.toggle_on", "ad.ui.when.toggle_off", "ad.ui.when.is_true", "ad.ui.when.is_false",
            "ad.ui.when.equals", "ad.ui.when.not_equals", "ad.ui.when.greater", "ad.ui.when.less",
            "ad.ui.when.contact", "ad.ui.when.contact_near", "ad.ui.when.pb_grabbed", "ad.ui.when.pb_posed",
            "ad.ui.when.pb_angle", "ad.ui.when.pb_stretch", "ad.ui.when.pb_squish",
            "ad.summary.conditions", "ad.summary.conditions.line",
            "ad.ui.pick.bones", "ad.ui.pick.inactive", "ad.ui.pick.none",
            "ad.ui.menu_assets", "ad.ui.menu_assets.note", "ad.ui.menu_assets.tip",
            "ad.ui.add.menu_asset", "ad.ui.detail.menu_asset", "ad.ui.menu_asset.unnamed",
            "ad.ui.menu_asset.asset", "ad.ui.menu_asset.path", "ad.ui.menu_asset.path.tip",
            "ad.ui.menu_asset.place", "ad.ui.menu_asset.place.submenu", "ad.ui.menu_asset.place.append",
            "ad.ui.menu_asset.parameters", "ad.ui.menu_asset.note", "ad.ui.menu_asset.declare",
            "ad.ui.menu_asset.declared", "ad.ui.menu_asset.undeclared", "ad.ui.chip.asset",
            "ad.ui.parameters", "ad.ui.parameters.note", "ad.ui.parameters.tip",
            "ad.ui.parameters.none", "ad.ui.parameters.total", "ad.ui.parameters.total.one", "ad.ui.parameters.bits", "ad.ui.parameters.bits.one",
            "ad.ui.parameters.add", "ad.ui.parameters.new",
            "ad.ui.import.group.menu_asset", "ad.ui.import.kind.menu_asset", "ad.ui.import.summary.parameters", "ad.ui.import.summary.parameters.one",
            "ad.ui.import.summary.declarations", "ad.ui.import.summary.declarations.one",
            "ad.ui.import.keep", "ad.ui.import.keep.tip", "ad.summary.menu_assets",
            "ad.summary.menu_assets.line", "ad.summary.menu_assets.line.one", "ad.summary.declared", "ad.summary.declared.line", "ad.summary.declared.line.one",
            "ad.ui.shapes.note", "ad.ui.shapes.follows", "ad.ui.shapes.follows.note", "ad.ui.shapes.while_on",
            "ad.ui.shapes.while_on.note", "ad.ui.shapes.source_mesh", "ad.ui.shapes.source_shape",
            "ad.ui.shapes.local_shape", "ad.ui.shapes.same_name", "ad.ui.shapes.mesh", "ad.ui.shapes.shape",
            "ad.ui.shapes.value", "ad.ui.shapes.add", "ad.ui.shapes.add_matching", "ad.ui.shapes.add_matching.tip",
            "ad.ui.shapes.none", "ad.ui.import.group.shapes", "ad.ui.import.kind.shapes", "ad.ui.shapes.count.follows", "ad.ui.shapes.count.follows.one",
            "ad.ui.shapes.count.while_on", "ad.summary.shapes",
            "ad.import.reason.driven_elsewhere", "ad.import.reason.install_target_unknown",
            "ad.summary.removed", "ad.summary.removed.freed", "ad.summary.removed.freed.one", "ad.summary.removed.kept",
            "ad.ui.shape_set.capture", "ad.ui.shape_set.zero_others", "ad.summary.shape_sets", "ad.summary.shape_sets.line",
            "ad.summary.shape_sets.line.one", "ad.summary.shape_sets.line.default", "ad.summary.shape_sets.line.default.one",
            "ad.ui.shape_set.hold", "ad.ui.shape_set.hold_at", "ad.ui.shape_set.value.default", "ad.ui.shape_set.value.zero",
            "ad.ui.shape_set.hold_blink", "ad.ui.shape_set.hold_visemes",
            "ad.ui.targets.follows", "ad.ui.target.keep_offset", "ad.summary.follows", "ad.summary.follows.line",
            "ad.summary.grabbables", "ad.summary.grabbables.line",
            "ad.ui.add.grabbable", "ad.ui.detail.grabbable", "ad.ui.grab.object", "ad.ui.grab.hands",
            "ad.ui.grab.hands.both", "ad.ui.grab.hands.left", "ad.ui.grab.hands.right", "ad.ui.grab.summary",
            "ad.ui.grab.anyone", "ad.ui.grab.return_seconds", "ad.ui.grab.menu_toggle", "ad.ui.grab.reach", "ad.ui.grab.fold",

            // One sentence tooltips on every labelled control.
            "ad.ui.select_component.tip", "ad.ui.icon.tip", "ad.ui.capture.tip", "ad.ui.platform.tip",
            "ad.ui.default_on.tip", "ad.ui.saved.tip", "ad.ui.synced.tip", "ad.ui.in_menu.tip", "ad.ui.group.tip", "ad.ui.group.value.tip", "ad.ui.group.number.tip", "ad.ui.poiyomi.mark_animated.tip",
            "ad.ui.parameter.tip", "ad.ui.parameter.use_custom.tip", "ad.ui.parameter.value.tip",
            "ad.ui.preview.tip", "ad.ui.preview.default.tip", "ad.ui.touch.simulate.tip", "ad.ui.chip.no_menu.tip",
            "ad.ui.trigger.when.tip", "ad.ui.trigger.toggle.tip", "ad.ui.trigger.toggle_state.tip",
            "ad.ui.trigger.parameter.tip", "ad.ui.trigger.condition.tip", "ad.ui.trigger.value.tip",
            "ad.ui.trigger.shape.tip", "ad.ui.trigger.mode.tip",
            "ad.ui.trigger.gate.tip", "ad.ui.trigger.gate_state.tip", "ad.ui.trigger.gate.add.tip",
            "ad.ui.name.tip", "ad.ui.style.tip", "ad.ui.style.color.tip", "ad.ui.style.clear_color.tip",
            "ad.ui.style.bold.tip", "ad.ui.style.italic.tip", "ad.ui.style.raw.tip", "ad.ui.style.preview.tip",
            "ad.ui.existing.move.tip", "ad.ui.existing.reset.tip",
            "ad.ui.animator.controller.tip", "ad.ui.animator.match_write_defaults.tip", "ad.ui.animator.layers.tip",
            "ad.ui.animator.parameters.tip", "ad.ui.animator.add_control.tip",
            "ad.ui.menu_asset.asset.tip", "ad.ui.menu_asset.place.tip", "ad.ui.menu_asset.name.tip",
            "ad.ui.menu_asset.parameters.tip", "ad.ui.menu_asset.declare.tip",
            "ad.ui.parameters.add.tip", "ad.ui.parameters.row_name.tip", "ad.ui.parameters.type.tip", "ad.ui.parameters.default.tip",
            "ad.ui.preset.sets.tip", "ad.ui.preset.add.tip", "ad.ui.preset.parameters.add.tip", "ad.ui.preset.control.tip",
            "ad.ui.preset.value.tip", "ad.ui.preset.on.tip", "ad.ui.preset.param_name.tip", "ad.ui.preset.param_value.tip",
            "ad.ui.touch.anchor.tip", "ad.ui.touch.radius.tip", "ad.ui.touch.offset.tip", "ad.ui.touch.who.tip",
            "ad.ui.touch.self.tip", "ad.ui.touch.others.tip", "ad.ui.touch.tags.tip", "ad.ui.touch.more_tags.tip",
            "ad.ui.touch.custom_tag_chip.tip", "ad.ui.touch.custom_tag.tip", "ad.ui.touch.distance.tip",
            "ad.ui.touch.proximity.tip", "ad.ui.touch.physbone.tip", "ad.ui.touch.event.tip", "ad.ui.touch.threshold.tip",
            "ad.ui.touch.mode.tip", "ad.ui.touch.flip_target.tip", "ad.ui.touch.fullat.tip", "ad.ui.touch.radius_follow.tip",
            "ad.ui.touch.hold.tip", "ad.ui.touch.flip.tip", "ad.ui.touch.follow.tip",
            "ad.ui.fires.tip", "ad.ui.when.add_and.tip", "ad.ui.when.add_and.limit.tip", "ad.ui.when.add_or.tip", "ad.ui.when.all_of.tip",
            "ad.ui.when.kind.tip", "ad.ui.when.settings.tip", "ad.ui.when.any_of.tip",
            "ad.ui.objects.tip", "ad.ui.blendshapes.tip", "ad.ui.materials.tip", "ad.ui.targets.properties.tip", "ad.ui.targets.animations.tip",
            "ad.ui.objects.add.tip", "ad.ui.blendshapes.add.tip", "ad.ui.materials.add.tip", "ad.ui.targets.properties.add.tip", "ad.ui.targets.animations.add.tip",
            "ad.ui.shape_set.capture.tip", "ad.ui.target.object.tip", "ad.ui.target.mode.tip", "ad.ui.target.shape.tip",
            "ad.ui.min.tip", "ad.ui.max.tip", "ad.ui.effect.link_mode.tip", "ad.ui.weight.tip", "ad.ui.effect.multiplier.tip",
            "ad.ui.override.value.tip", "ad.ui.seg.fixed_pin.tip", "ad.ui.target.off_value.tip",
            "ad.ui.target.renderer.tip", "ad.ui.slot.tip", "ad.ui.target.material.tip", "ad.ui.property.renderer.tip", "ad.ui.property.pick_button.tip",
            "ad.ui.target.clip.tip", "ad.ui.usage.chip.tip", "ad.ui.property.value_type.tip",
            "ad.ui.property.min.tip", "ad.ui.property.max.tip", "ad.ui.property.value.tip", "ad.ui.shape_set.zero_others.tip",
            "ad.ui.shape_set.hold.tip", "ad.ui.shape_set.value.tip", "ad.ui.shape_set.value.default.tip", "ad.ui.shape_set.value.zero.tip",
            "ad.ui.shape_set.hold_blink.tip", "ad.ui.shape_set.hold_visemes.tip", "ad.ui.shape_set.remove.tip",
            "ad.ui.summary.format.tip", "ad.ui.summary.text.tip", "ad.ui.summary.copy.tip", "ad.ui.lock.note.tip",
            "ad.ui.summary.toggle.tip", "ad.ui.install.tip", "ad.ui.install.mode.tip", "ad.ui.menu_name.tip",
            "ad.ui.menu_icon.tip", "ad.ui.prefix.tip", "ad.ui.platform.component.tip", "ad.ui.preview.reset.tip", "ad.ui.edit.remove.tip",
            "ad.ui.memory.bar.tip",
            "ad.ui.wheel.mode.tip", "ad.ui.wheel.ring.tip", "ad.ui.wheel.radial_slider.tip", "ad.ui.wheel.triggers_strip.tip",
            "ad.ui.wheel.not_in_menu.tip", "ad.ui.wheel.trigger_button.tip", "ad.ui.wheel.hidden_button.tip", "ad.ui.wheel.crumb.tip",
            "ad.ui.add.toggle.tip", "ad.ui.add.radial.tip", "ad.ui.add.submenu.tip", "ad.ui.add.trigger.tip",
            "ad.ui.add.override.tip", "ad.ui.add.preset.tip", "ad.ui.add.animator.tip", "ad.ui.add.menu_asset.tip",
            "ad.ui.add.outfit.tip", "ad.ui.wheel.toggle.tip", "ad.ui.search.tip", "ad.ui.search.placeholder", "ad.log.slow_refresh",
            "ad.ui.shapes.follows.tip", "ad.ui.shapes.add_follow.tip", "ad.ui.shapes.while_on.tip", "ad.ui.shapes.add_while_on.tip",
            "ad.ui.shapes.platform.tip", "ad.ui.shapes.source_mesh.tip", "ad.ui.shapes.mesh.tip", "ad.ui.shapes.value.tip",
            "ad.ui.shapes.source_shape.tip", "ad.ui.shapes.local_shape.tip", "ad.ui.shapes.shape.tip",
            "ad.ui.import.select_all.tip", "ad.ui.import.select_none.tip", "ad.ui.import.button.tip",
            "ad.ui.import.restore.tip", "ad.ui.import.candidate.tip",
            "ad.ui.outfit.name.tip", "ad.ui.outfit.mode.tip", "ad.ui.outfit.none_option.tip", "ad.ui.outfit.add.tip",
            "ad.ui.outfit.hidden.tip", "ad.ui.outfit.capture.tip", "ad.ui.outfit.create.tip",
            "ad.ui.outfit.split.each.tip", "ad.ui.outfit.split.all.tip", "ad.ui.outfit.card_name.tip", "ad.ui.outfit.default.tip",
            "ad.ui.outfit.shrinks.tip", "ad.ui.outfit.shrink_toggle.tip", "ad.ui.outfit.show_all.tip", "ad.ui.outfit.add_piece.tip",
            "ad.ui.capture.background.tip", "ad.ui.capture.color.tip", "ad.ui.capture.fit.tip", "ad.ui.capture.save.tip",
            "ad.ui.chip.asset.tip", "ad.ui.unlock.tip", "ad.ui.default_value.tip",
            "ad.ui.targets.follows.tip", "ad.ui.targets.follows.add.tip", "ad.ui.target.follow_object.tip",
            "ad.ui.target.follow_target.tip", "ad.ui.target.keep_offset.tip",
            "ad.ui.add.grabbable.tip", "ad.ui.grab.object.tip", "ad.ui.grab.hands.tip", "ad.ui.grab.anyone.tip",
            "ad.ui.grab.return_seconds.tip", "ad.ui.grab.menu_toggle.tip", "ad.ui.grab.reach.tip", "ad.ui.grab.fold.tip",

            // Search disables reordering, so a visible note explains it while it does.
            "ad.info.drag_needs_no_search",

            // A drop the tree cannot apply says why once instead of reverting silently.
            "ad.info.drop_refused", "ad.info.drop_refused.locked", "ad.info.drop_refused.own_descendant", "ad.info.drop_refused.not_a_container",

            // A one line preview of where a drag will land, from the pointer's horizontal position.
            "ad.ui.drag_target.into", "ad.ui.drag_target.root",

            // Ready to upload, and costs where they are added.
            "ad.ui.slots.chip", "ad.ui.more_page",
            "ad.ui.synced.cost.none", "ad.ui.synced.cost.bit", "ad.ui.synced.cost.bits",
            "ad.ui.ready", "ad.ui.ready.problems", "ad.ui.ready.problems.one", "ad.ui.ready.warnings", "ad.ui.ready.warnings.one", "ad.ui.ready.show", "ad.ui.ready.show.tip", "ad.ui.ready.tip",
            "ad.ui.problems.arrow.tip", "ad.ui.problems.more", "ad.ui.problems.line_count",
            "ad.ui.message.learn_more", "ad.ui.message.learn_more.tip", "ad.ui.quickstart.full_guide", "ad.ui.quickstart.full_guide.tip",
            "ad.ui.write_defaults", "ad.ui.write_defaults.on", "ad.ui.write_defaults.off", "ad.ui.write_defaults.tip",
            "ad.ui.builds.line", "ad.ui.builds.controls", "ad.ui.builds.controls.one", "ad.ui.builds.bits", "ad.ui.builds.bits.one", "ad.ui.builds.layers", "ad.ui.builds.layers.one",
            "ad.ui.builds.contacts", "ad.ui.builds.contacts.one", "ad.ui.builds.physbones", "ad.ui.builds.physbones.one", "ad.ui.builds.constraints", "ad.ui.builds.constraints.one",
            "ad.ui.builds.follow_distance", "ad.ui.builds.pauses_blink", "ad.ui.builds.pauses_lipsync",
            "ad.ui.builds.blink_left_to", "ad.ui.builds.lipsync_left_to",
            "ad.ui.part.blinking", "ad.ui.part.lipsync",
            "ad.ui.builds.nothing.platform", "ad.ui.builds.nothing.empty", "ad.ui.builds.nothing.no_clips", "ad.ui.builds.clips", "ad.ui.builds.clips.one", "ad.ui.builds.nothing.disabled", "ad.ui.builds.tip",

            // A first screen that invites, a slim toolbar, visible row actions, and a tag filter.
            "ad.ui.detail.select_hint",
            "ad.ui.settings_fold", "ad.ui.settings_fold.tip", "ad.ui.settings_fold.dot.tip",
            "ad.ui.toolbar.convert", "ad.ui.more", "ad.ui.more.tip",
            "ad.ui.welcome.title", "ad.ui.welcome.toggle", "ad.ui.welcome.toggle.desc", "ad.ui.welcome.toggle.tip",
            "ad.ui.welcome.convert.title", "ad.ui.welcome.convert", "ad.ui.welcome.convert.one", "ad.ui.welcome.convert.desc",
            "ad.ui.welcome.convert.mixed_source", "ad.ui.welcome.convert.tip",
            "ad.ui.welcome.outfit", "ad.ui.welcome.outfit.desc", "ad.ui.welcome.outfit.tip",
            "ad.ui.welcome.quickstart", "ad.ui.welcome.quickstart.tip",
            "ad.ui.row.move.tip", "ad.ui.row.duplicate.tip", "ad.ui.row.delete.tip", "ad.ui.row.convert.tip", "ad.ui.row.rename.tip",
            "ad.ui.shortcut.delete", "ad.ui.menu.back",
            "ad.ui.add.contact", "ad.ui.add.contact.tip", "ad.ui.toolbar.capture",
            "ad.ui.capture_missing.tip",
            "ad.ui.tagfilter", "ad.ui.tagfilter.count", "ad.ui.tagfilter.tip", "ad.ui.tagfilter.clear", "ad.ui.tagfilter.clear.tip",
            "ad.ui.tagfilter.chip.remove.tip",
            "ad.ui.tagfilter.group.kind", "ad.ui.tagfilter.group.from", "ad.ui.tagfilter.group.state", "ad.ui.tagfilter.group.platform",
            "ad.ui.tagfilter.from.avatardoc", "ad.ui.tagfilter.from.avatar_menu", "ad.ui.tagfilter.state.has_problems",

            // Panels that grow with you, one name for one idea, and first-use tips.
            "ad.ui.advanced_fold", "ad.ui.advanced_fold.tip", "ad.ui.advanced_fold.dot.tip",
            "ad.ui.add_effect", "ad.ui.add_effect.tip",
            "ad.ui.add_effect.objects", "ad.ui.add_effect.blendshapes", "ad.ui.add_effect.materials",
            "ad.ui.add_effect.properties", "ad.ui.add_effect.animations", "ad.ui.add_effect.follows",
            "ad.ui.tips_switch_label", "ad.ui.tips_switch", "ad.ui.tips_switch.tip", "ad.ui.tip.dismiss.tip", "ad.ui.row.more.tip",
            "ad.tip.toggle", "ad.tip.radial", "ad.tip.submenu", "ad.tip.trigger", "ad.tip.override",
            "ad.tip.preset", "ad.tip.grabbable", "ad.tip.animator", "ad.tip.menu_asset", "ad.tip.header",

            // Ready made contact starts.
            "ad.ui.add.contact_starts",
            "ad.ui.add.contact_starts.headpat", "ad.ui.add.contact_starts.headpat.tip",
            "ad.ui.add.contact_starts.eye_touch", "ad.ui.add.contact_starts.eye_touch.tip",
            "ad.ui.add.contact_starts.nose_boop", "ad.ui.add.contact_starts.nose_boop.tip",

            // Stability pass: the console line for a menu that closed by itself, and its reasons.
            "ad.log.menu_closed", "ad.log.menu_closed.view_gone", "ad.log.menu_closed.removed", "ad.log.menu_closed.resized",
            "ad.log.menu_closed.undo", "ad.log.menu_closed.window_focus", "ad.log.menu_closed.focus_taken",
            "ad.log.menu_closed.other_app", "ad.log.menu_closed.nothing", "ad.log.ids_repaired", "ad.log.plan_reused",

            // The Simple and Advanced switch.
            "ad.ui.mode.simple", "ad.ui.mode.advanced", "ad.ui.mode.tip", "ad.ui.advanced_item.note", "ad.tip.header.simple",

            // Smooth blend shape triggers and requirements.
            "ad.ui.trigger.style", "ad.ui.trigger.style.tip", "ad.ui.trigger.style.smooth", "ad.ui.trigger.style.smooth.tip",
            "ad.ui.trigger.style.switch", "ad.ui.trigger.style.switch.tip", "ad.ui.trigger.smooth_start", "ad.ui.trigger.smooth_start.tip",
            "ad.ui.trigger.smooth_full", "ad.ui.trigger.smooth_full.tip", "ad.ui.trigger.switch_at", "ad.ui.trigger.switch_at.tip",
            "ad.ui.trigger.swap_at", "ad.ui.trigger.swap_at.tip", "ad.ui.trigger.mode.add", "ad.ui.trigger.mode.replace",
            "ad.ui.when.shape_moves", "ad.ui.when.hand_near", "ad.ui.when.add_requirement.tip", "ad.ui.when.add_and.limit_driver.tip",
            "ad.ui.when.requirements_note", "ad.ui.preview.unchecked", "ad.ui.builds.smooth", "ad.ui.builds.switch", "ad.ui.builds.mirror",
            "ad.summary.parameters.mirror", "ad.ui.when.ignored", "ad.ui.when.ignored.tip", "ad.ui.builds.pauses_blink_shape", "ad.ui.builds.pauses_lipsync_shape",

            // The language picker beside the Simple and Advanced switch.
            "ad.ui.language.tip",

            // Punctuation and words the code puts between pieces of text.
            "ad.ui.list.separator", "ad.ui.aside", "ad.ui.brackets", "ad.ui.quoted", "ad.ui.labelled",
            "ad.summary.parameters.name", "ad.summary.parameters.type", "ad.summary.parameters.notes", "ad.ui.blend.unit",

            // The search field at the top of every picker.
            "ad.ui.pick.search.tip", "ad.ui.pick.no_match", "ad.ui.pick.more", "ad.ui.pick.more.one",

            // Add to AvatarDoc: the right click items and the window they open.
            "ad.ui.add_from.item", "ad.ui.add_from.item.outside", "ad.ui.add_from.title", "ad.ui.add_from.intro", "ad.ui.add_from.outside",
            "ad.ui.add_from.gone", "ad.ui.add_from.mesh", "ad.ui.add_from.mesh.tip", "ad.ui.add_from.kind", "ad.ui.add_from.kind.tip",
            "ad.ui.add_from.kind.shape", "ad.ui.add_from.kind.shape.tip", "ad.ui.add_from.kind.property", "ad.ui.add_from.kind.property.tip",
            "ad.ui.add_from.shape", "ad.ui.add_from.shape.tip", "ad.ui.add_from.material", "ad.ui.add_from.material.tip", "ad.ui.add_from.material.none",
            "ad.ui.add_from.property",
            "ad.ui.add_from.property.tip", "ad.ui.add_from.now", "ad.ui.add_from.now.tip", "ad.ui.add_from.make.tip", "ad.ui.add_from.make.toggle",
            "ad.ui.add_from.make.toggle.tip", "ad.ui.add_from.make.radial", "ad.ui.add_from.make.radial.tip", "ad.ui.add_from.make.add_to",
            "ad.ui.add_from.make.add_to.tip", "ad.ui.add_from.no_component", "ad.ui.add_from.add_component", "ad.ui.add_from.add_component.tip",
            "ad.ui.add_from.entry", "ad.ui.add_from.entry.tip", "ad.ui.add_from.entry.pick", "ad.ui.add_from.entry.none", "ad.ui.add_from.name.tip",
            "ad.ui.add_from.component", "ad.ui.add_from.component.tip", "ad.ui.add_from.component.locked", "ad.ui.add_from.submenu",
            "ad.ui.add_from.submenu.tip", "ad.ui.add_from.on.tip", "ad.ui.add_from.off.tip", "ad.ui.add_from.max.tip", "ad.ui.add_from.min.tip",
            "ad.ui.add_from.after", "ad.ui.add_from.put_back", "ad.ui.add_from.put_back.radial", "ad.ui.add_from.put_back.tip",
            "ad.ui.add_from.poiyomi", "ad.ui.add_from.mark", "ad.ui.add_from.mark.tip", "ad.ui.add_from.renamed", "ad.ui.add_from.baked",
            "ad.ui.add_from.baked.one", "ad.ui.add_from.add", "ad.ui.add_from.add.tip", "ad.ui.add_from.done.toggle", "ad.ui.add_from.done.radial",
            "ad.ui.add_from.done.added",

            // Copy to another avatar: the window, the settings row and Pick by hand when copying.
            "ad.ui.copy_to.title", "ad.ui.copy_to.intro", "ad.ui.copy_to.gone", "ad.ui.copy_to.from", "ad.ui.copy_to.from.tip",
            "ad.ui.copy_to.target", "ad.ui.copy_to.target.tip", "ad.ui.copy_to.target.pick", "ad.ui.copy_to.no_target", "ad.ui.copy_to.goes_on",
            "ad.ui.copy_to.goes_on.tip", "ad.ui.copy_to.goes_on.root", "ad.ui.copy_to.where", "ad.ui.copy_to.where.tip", "ad.ui.copy_to.where.replace",
            "ad.ui.copy_to.where.replace.tip", "ad.ui.copy_to.where.add", "ad.ui.copy_to.where.add.tip", "ad.ui.copy_to.replace", "ad.ui.copy_to.replace.tip",
            "ad.ui.copy_to.replace.item", "ad.ui.copy_to.entries.one", "ad.ui.copy_to.entries", "ad.ui.copy_to.locked", "ad.ui.copy_to.objects",
            "ad.ui.copy_to.none_used", "ad.ui.copy_to.found.one", "ad.ui.copy_to.found", "ad.ui.copy_to.missing", "ad.ui.copy_to.missing_component",
            "ad.ui.copy_to.by_hand_row", "ad.ui.copy_to.use", "ad.ui.copy_to.use.tip", "ad.ui.copy_to.suggested", "ad.ui.copy_to.suggested.place",
            "ad.ui.copy_to.pick_outside", "ad.ui.copy_to.needs.path_root", "ad.ui.copy_to.needs.required", "ad.ui.copy_to.ambiguous", "ad.ui.copy_to.checks",
            "ad.ui.copy_to.checks.none", "ad.ui.copy_to.check.shape", "ad.ui.copy_to.check.slot", "ad.ui.copy_to.check.property", "ad.ui.copy_to.run",
            "ad.ui.copy_to.run.tip", "ad.ui.copy_to.run.blocked", "ad.ui.copy_to.done.added", "ad.ui.copy_to.done.replaced", "ad.ui.copy_to.row",
            "ad.ui.copy_to.open", "ad.ui.copy_to.open.tip", "ad.ui.copy_to.by_hand", "ad.ui.copy_to.by_hand.description", "ad.ui.copy_to.by_hand_list",
            "ad.ui.copy_to.by_hand_list.tip", "ad.ui.copy_to.by_hand.remove.tip", "ad.ui.copy_to.by_hand_gone",
        };

        static Localizer localizer;

        /// <summary>NDMF's view of AvatarDoc's texts, for the messages in NDMF's error report. It answers through the same lookup as Text, so it follows the current language and falls back to English the same way.</summary>
        public static Localizer Localizer => localizer ?? (localizer = new Localizer(LanguageTables.English,
            () => new List<(string, Func<string, string>)> { (LanguageTables.English, Find) }));

        /// <summary>The asset path of a language's table: the shipped one first, by its asset id for English; null when there is none.</summary>
        public static string TablePath(string language)
        {
            var code = LanguageTables.Normalize(language) ?? language;
            if (code == LanguageTables.English)
            {
                var byId = AssetDatabase.GUIDToAssetPath(LanguageTables.EnglishGuid);
                if (!string.IsNullOrEmpty(byId)) return byId;
            }
            foreach (var file in LanguageTables.Files())
                if (string.Equals(file.Code, code, StringComparison.OrdinalIgnoreCase)) return file.Path;
            return null;
        }

        static string testLanguage;

        /// <summary>The language AvatarDoc shows, in lower case as NDMF keeps it: the one NDMF and Modular Avatar share, or a test's while one is in place.</summary>
        public static string Language => testLanguage ?? LanguagePrefs.Language;

        /// <summary>Chooses the shared language, so NDMF and Modular Avatar follow. While a test's language is in place, only the test's changes and nothing is saved.</summary>
        public static void SetLanguage(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return;
            code = code.ToLowerInvariant();
            if (testLanguage == null)
            {
                LanguagePrefs.Language = code;
                return;
            }
            if (code == testLanguage) return;
            testLanguage = code;
            RaiseLanguageChanged();
        }

        /// <summary>Raised after the language AvatarDoc shows changed: chosen in AvatarDoc, in Modular Avatar or in NDMF, or a language file was saved.</summary>
        public static event Action LanguageChanged;

        /// <summary>Moves on every change LanguageChanged reports, so something built in an earlier language can tell.</summary>
        public static int LanguageVersion { get; private set; }

        /// <summary>How many listen to LanguageChanged right now. Read by the tests that check a view lets go when it leaves its panel.</summary>
        public static int LanguageListenerCount => LanguageChanged?.GetInvocationList().Length ?? 0;

        static void RaiseLanguageChanged()
        {
            LanguageVersion++;
            var listeners = LanguageChanged;
            if (listeners == null) return;
            foreach (Action listener in listeners.GetInvocationList())
            {
                try { listener(); }
                catch (Exception e) { UnityEngine.Debug.LogException(e); }
            }
        }

        [InitializeOnLoadMethod]
        static void Hook()
        {
            LanguagePrefs.RegisterLanguageChangeCallback(typeof(L), _ => RaiseLanguageChanged());
            RegisterLanguages();
        }

        /// <summary>Adds every language file's language to NDMF's list, so NDMF's and Modular Avatar's language switchers offer it too.</summary>
        public static void RegisterLanguages()
        {
            foreach (var file in LanguageTables.Files()) LanguagePrefs.RegisterLanguage(file.Code);
        }

        /// <summary>Reads every language file again, offers any new language to NDMF and redraws every open AvatarDoc view and window.</summary>
        public static void ReloadLanguages()
        {
            LanguageTables.Reload();
            RegisterLanguages();
            RaiseLanguageChanged();
        }

        /// <summary>
        /// For tests: shows AvatarDoc in a language made of these texts, in place of any file for that code, until the
        /// returned scope is disposed. With no texts, only the language changes and the files answer as usual, which
        /// pins a test run to English with UseTestLanguage("en-US", null). Every open view follows, as it does for a
        /// real change. Nothing reaches NDMF's shared choice or EditorPrefs meanwhile: SetLanguage changes only the
        /// test's language.
        /// </summary>
        public static IDisposable UseTestLanguage(string code, IDictionary<string, string> texts) => new TestLanguage(code, texts);

        sealed class TestLanguage : IDisposable
        {
            readonly string code;
            readonly bool hasTable;
            readonly Dictionary<string, string> previousTable;
            readonly string previous;
            bool disposed;

            public TestLanguage(string code, IDictionary<string, string> texts)
            {
                this.code = code;
                hasTable = texts != null;
                previous = testLanguage;
                if (hasTable) previousTable = LanguageTables.PutTestTable(code, texts);
                testLanguage = code.ToLowerInvariant();
                RaiseLanguageChanged();
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (hasTable) LanguageTables.RestoreTestTable(code, previousTable);
                testLanguage = previous;
                RaiseLanguageChanged();
            }
        }

        /// <summary>
        /// The tables a text is looked for in, in order: the language itself, then any other form of the same language
        /// (zh-Hant tries zh-Hans), then English. NDMF orders its own tables the same way.
        /// </summary>
        public static IReadOnlyList<string> LookupOrder(string language)
        {
            var order = new List<string>();
            var codes = LanguageTables.Codes;
            foreach (var code in codes)
                if (string.Equals(code, language, StringComparison.OrdinalIgnoreCase)) order.Add(code);
            var baseLanguage = (language ?? "").Split('-')[0];
            if (baseLanguage.Length > 0)
            {
                foreach (var code in codes)
                {
                    if (order.Contains(code)) continue;
                    if (string.Equals(code, baseLanguage, StringComparison.OrdinalIgnoreCase) || code.StartsWith(baseLanguage + "-", StringComparison.OrdinalIgnoreCase))
                        order.Add(code);
                }
            }
            if (!order.Contains(LanguageTables.English)) order.Add(LanguageTables.English);
            return order;
        }

        static string orderLanguage;
        static int orderVersion = -1;
        static string[] order = Array.Empty<string>();

        /// <summary>A key's text in the current language, from English when that language lacks it; null when English lacks it too.</summary>
        static string Find(string key)
        {
            if (key == null) return null;
            var language = Language;
            if (orderVersion != LanguageTables.Version || !string.Equals(orderLanguage, language, StringComparison.Ordinal))
            {
                order = LookupOrder(language).ToArray();
                orderLanguage = language;
                orderVersion = LanguageTables.Version;
            }
            foreach (var code in order)
            {
                var text = LanguageTables.Text(code, key);
                if (text != null) return text;
            }
            return null;
        }

        /// <summary>A key's text in the current language. A key the language lacks shows its English text; one English lacks too shows as &lt;key&gt;.</summary>
        public static string Text(string key) => Find(key) ?? "<" + key + ">";

        /// <summary>Items listed the way the current language lists them: "a, b" in English, "a、b" in Japanese and Chinese.</summary>
        public static string JoinList(IEnumerable<string> items) => string.Join(Text("ad.ui.list.separator"), items);

        /// <summary>A text with a note after it in the language's own brackets: "Hat (Body)", "Hat（Body）". No note, no brackets.</summary>
        public static string Aside(string text, string note) => string.IsNullOrEmpty(note) ? text : string.Format(Text("ad.ui.aside"), text, note);

        /// <summary>A text in the language's own brackets: "(a and b)", "（a かつ b）".</summary>
        public static string Brackets(string text) => string.Format(Text("ad.ui.brackets"), text);

        /// <summary>A name in the language's own quotes: "Hat" in English, 「Hat」 in Japanese, “Hat” in Chinese.</summary>
        public static string Quoted(string name) => string.Format(Text("ad.ui.quoted"), name);

        /// <summary>A label and its text, joined by the language's own colon: "Error: ...", "エラー：...".</summary>
        public static string Labelled(string label, string text) => string.Format(Text("ad.ui.labelled"), label, text);

        public static void Report(ModelMessage message)
        {
            var severity = message.Severity == MessageSeverity.Error ? ErrorSeverity.Error
                : message.Severity == MessageSeverity.Warning ? ErrorSeverity.NonFatal
                : ErrorSeverity.Information;
            ErrorReport.ReportError(Localizer, severity, message.Key, message.Args ?? Array.Empty<object>());
        }

        public static void Report(IEnumerable<ModelMessage> messages)
        {
            foreach (var message in messages) Report(message);
        }
    }
}
#endif
