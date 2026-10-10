# DocMe AvatarDoctor: design

Status: approved design, 2026-09-16.

## Problem

Adding a toggle to a VRChat avatar by hand means editing an FX controller, an expression parameters asset and an
expressions menu asset, keeping all three in sync, and repeating it for every outfit and accessory. Modular Avatar and
VRCFury automate this, but their editors are lists of components spread over the hierarchy, so the final menu is hard
to see, and neither shows how a submenu will look in game.

## Goal

AvatarDoctor is a non-destructive toggle and menu builder for VRChat avatars:

- one component holds a menu of submenus, toggles and radials, each toggle switching objects, blendshapes and material
  slots;
- more components (for example inside an outfit prefab) install their own entries into a chosen submenu of the merged
  menu;
- everything is generated at build time (upload and play mode) by an NDMF plugin; source assets are never modified;
- a sleek UI Toolkit inspector shows the merged menu as a tree, the selected entry in detail, and the selected submenu
  as VRChat's radial wheel;
- triggers switch the same kinds of targets without a menu button, driven by another toggle, a parameter condition or a
  blendshape being animated anywhere on the avatar;
- edit-mode preview of any toggle state on NDMF proxy renderers.

It is written for a later public release (VPM package). It lives in `Packages/dev.docme.avatar-doctor` as an embedded package with its own private git repo (moved from `Assets/DocMe/AvatarDoctor` on 2026-10-10, asset ids kept).

## Non-goals for version 1

- Two-axis and four-axis puppets, buttons (momentary controls).
- Toggling components other than renderers' materials and GameObject active state (no `m_Enabled` on arbitrary
  components, no constraints, no PhysBone switching).
- Mesh deletion, bone merging, armature linking, animator merging. AvatarDoctor complements Modular Avatar for those.
- Cross-platform (Resonite) output. The plugin runs on the VRChat avatar platform only.
- Localizations other than English. The strings are in one JSON file so more can follow.

## Decisions and why

| Decision | Why |
|---|---|
| NDMF plugin | Play-mode apply, preview system, error UI and ordering with Modular Avatar and Face Rebase come for free. Same foundation as Face Rebase. |
| One component type for hub and installers | The hub is a component installed at the root. One editor, one mental model, and any component shows the merged result. |
| Flat entry list with parent ids | Unity cannot serialize a class that nests itself. A flat list works with prefabs, undo, multi-object edits and reordering. |
| One FX layer per control, explicit Off and On clips | Correct with Write Defaults on or off, readable in the Animator window, reliable material swaps. Modular Avatar does the same. |
| Blendshape-driven triggers rewrite clips at build | Animators cannot read blendshape weights. Rewriting every clip that animates the source catches gestures, face tracking and other tools without a parameter. Since plan 14 task S the rewrite adds one hidden copy of the shape's weight to every FX clip and the trigger has a layer of its own (see "Smooth blend shape triggers and requirements"). |
| Parameter name `AD/<Toggle name>`, not the menu path | Moving a toggle into another submenu keeps its name, so values saved by VRChat survive. |
| UI Toolkit, one view with two hosts | A real tree editor, drag and drop and a drawn wheel are far easier in UI Toolkit. The same view stacks in the inspector and spreads out in a window. |

## Verified platform facts (NDMF 1.14.1, Modular Avatar 1.17.1, VRCSDK 3.10.4, Unity 2022.3.22)

- Plugin registration: `[assembly: ExportsPlugin(typeof(AvatarDoctorPlugin))]`, `Plugin<T>`, `InPhase(BuildPhase.Transforming)`,
  `WithRequiredExtension(typeof(AnimatorServicesContext), seq => seq.Run(...).PreviewingWith(filter).BeforePlugin("nadena.dev.modular-avatar"))`.
  Passes default to the VRChat avatar platform when no platform attribute is given.
- FX access: `context.Extension<AnimatorServicesContext>().ControllerContext.Controllers[VRCAvatarDescriptor.AnimLayerType.FX]`
  gives a `VirtualAnimatorController`. `AddLayer(LayerPriority.Default, name)` creates a layer with a state machine.
  `VirtualClip.Create(name)`, `SetFloatCurve(EditorCurveBinding.FloatCurve(path, type, prop), curve)`,
  `SetObjectCurve(EditorCurveBinding.PPtrCurve(...), keyframes)`. Paths come from
  `ObjectPathRemapper.GetVirtualPathForObject(go)`. Commit and asset saving happen when the context deactivates.
- `VirtualBlendTree.Create`, `BlendType`, `BlendParameter`, `Children` (`VirtualChildMotion` with `Threshold`).
- Transitions: `VirtualStateTransition.Create()`, `Conditions` (immutable list of `AnimatorCondition`),
  `SetDestination(state)`, `ExitTime = null`, `Duration = 0`, `HasFixedDuration = true`, `CanTransitionToSelf`.
- Clip lookup for rewriting: `AnimatorServicesContext.AnimationIndex.GetClipsForBinding(binding)` and
  `EditClipsByBinding(bindings, clip => ...)` cover every clip in every playable layer.
- Descriptor: `context.VRChatAvatarDescriptor()` (assembly `nadena.dev.ndmf.vrchat`). NDMF has no API for expression
  parameters or menus; the plugin clones them itself, guarded by `context.IsTemporaryAsset(asset)`, and registers
  clones with `ObjectRegistry.RegisterReplacedObject(original, clone)`.
- SDK limits: `VRCExpressionParameters.MAX_PARAMETER_COST` (256), `VRCExpressionsMenu.MAX_CONTROLS` (8),
  `VRCExpressionParameters.TypeCost(valueType)`, `CalcTotalCost()`.
- Parameter introspection: `[ParameterProviderFor(typeof(AvatarDoctorMenu))]` class implementing `IParameterProvider`
  returning fresh `ProvidedParameter` objects (`WantSynced`, `DefaultValue`, `IsAnimatorOnly`, `ExpandTypeOnConflict`).
  `ParameterInfo.ForUI.GetParametersForObject(avatarRoot)` sums `BitUsage` for the memory bar and lists every parameter
  for the trigger picker.
- Errors: `ErrorReport.ReportError(localizer, severity, key, args...)`; any `UnityEngine.Object` argument becomes a
  clickable reference. `ErrorSeverity.Error` blocks the upload, `NonFatal` and `Information` do not. A `Localizer`
  is mandatory (missing keys render as the key).
- Preview: `IRenderFilter` with `CanEnableRenderers => true`; `GetTargetGroups(ComputeContext)` returns
  `RenderGroup.For(renderer).WithData(state, comparer)`; the node's `OnFrame(original, proxy)` runs after the proxy has
  been reset from the original each frame and may set `proxy.enabled`, `SetBlendShapeWeight` and `sharedMaterials`.
  `ComputeContext.Observe(obj)`, `Observe(obj, extractor)`, `GetComponentsInChildren`, `ActiveInHierarchy`,
  `GetAvatarRoots`. `TogglablePreviewNode.Create(() => name, qualifiedName, true)` for the preview menu entry.
- Modular Avatar pass names for ordering: plugin `nadena.dev.modular-avatar` (main) and
  `nadena.dev.modular-avatar.late-transform-stages` (component purge). Its "Fixup Expressions Menu" pass rescales icons
  and runs after its menu install; running before Modular Avatar lets AvatarDoctor's menus benefit from it.
- Write Defaults rule from Modular Avatar: count `WriteDefaultValues` over all states of non-blend-tree layers of the
  existing FX; unanimous wins, mixed means on.
- Modular Avatar hides a toggled-off object in preview by `proxy.enabled = false` on every renderer under it, walking
  the ancestor chain; the GameObject itself is never touched.

## Runtime assembly: `DocMe.AvatarDoctor`

No editor dependencies. Version define `DOCME_VRCSDK_BASE` on `com.vrchat.base` for `IEditorOnly`, as in Face Rebase.

### `AvatarDoctorMenu` (MonoBehaviour, IEditorOnly)

`[AddComponentMenu("DocMe/AvatarDoctor Menu")]`. Any number per avatar, anywhere under the root.

| Field | Type | Meaning |
|---|---|---|
| `installPath` | string | Submenu path in the merged menu, segments separated by `/`. Empty means the avatar's root menu. |
| `installMode` | enum `AppendItems`, `AsSubmenu` | Append the entries at the install path, or add one submenu there (named `menuName`, icon `menuIcon`) holding them. |
| `menuName`, `menuIcon` | string, Texture2D | Used by `AsSubmenu`. Default name is the GameObject name. |
| `parameterPrefix` | string | Default `AD`. Auto parameter names are `<prefix>/<name>`. |
| `entries` | List of `MenuEntry` | Flat list, see below. |
| `groups` | List of `ExclusiveGroup` | Group settings: `name`, `saved` (default true), `synced` (default true). Membership is by name on the toggles, so a group can span components. |

### `MenuEntry` (Serializable)

| Field | Type | Used by | Meaning |
|---|---|---|---|
| `id` | string | all | GUID assigned when created. Unique across components. Never changes. |
| `parentId` | string | Submenu, Toggle, Radial | Id of the parent submenu entry in the same component, empty for the component's own root. Triggers ignore it. |
| `kind` | enum `Submenu`, `Toggle`, `Radial`, `Trigger` | all | |
| `name` | string | all | Menu label, layer name, auto parameter name. |
| `icon` | Texture2D | Submenu, Toggle, Radial | Menu icon. |
| `defaultOn` | bool | Toggle | On at spawn. Sets the expression parameter default and the scene state. |
| `defaultValue` | float 0..1 | Radial | Value at spawn. |
| `saved`, `synced` | bool, default true | Toggle, Radial | Expression parameter flags. Ignored for a toggle in an exclusive group (the group's flags apply). |
| `parameterName` | string | Toggle, Radial | Empty means auto. A custom name binds to an existing or shared parameter. |
| `parameterValue` | float, default 1 | Toggle | Value the menu writes when the parameter is a custom int or float. |
| `exclusiveGroup` | string | Toggle | Group name, empty for none. |
| `groupValue` | int, default 0 | Toggle | Explicit int value inside the group. 0 means assigned by order. |
| `source` | enum `Toggle`, `Parameter`, `BlendShape` | Trigger | What switches the trigger. |
| `sourceToggleId`, `sourceToggleOn` | string, bool | Trigger (Toggle) | The other toggle's id (any component) and whether the trigger is on when that toggle is on (true) or off (false). |
| `sourceParameter`, `condition`, `conditionValue` | string, enum `IsTrue`, `IsFalse`, `Equals`, `NotEquals`, `Greater`, `Less`, float | Trigger (Parameter) | Animator parameter condition. |
| `sourceRenderer`, `sourceShape`, `sourceThreshold`, `linkMode` | SkinnedMeshRenderer, string, float (default 50), enum `Add`, `Replace` | Trigger (BlendShape) | Source blendshape; threshold for object and material effects and fixed-weight shapes; whether the source curve stays (`Add`) or is removed (`Replace`) from rewritten clips. |
| `linkStyle`, `smoothStart`, `smoothFull` | enum `Smooth` (0), `Switch` (1); float (default 0); float (default 100) | Trigger (BlendShape) | Plan 14 task S: blend between Start and Full, or switch at `sourceThreshold`. Smooth swaps objects and materials at `sourceThreshold`. |
| `objects` | List of `ObjectTarget` | Toggle, Trigger | `target` GameObject, `activeWhenOn` (default true; false means hidden when on). |
| `blendShapes` | List of `BlendShapeTarget` | Toggle, Radial, Trigger | `renderer`, `shape`, `weight` (Toggle and fixed Trigger effect), `minWeight` and `maxWeight` (Radial), `mode` enum `Fixed`, `Follow` and `multiplier` (BlendShape trigger effect). |
| `materials` | List of `MaterialTarget` | Toggle, Trigger | `renderer`, `slot`, `material` when on. |

Validation rules on the model (also shown live in the inspector) are listed under Errors and validation.

## Editor assembly: `DocMe.AvatarDoctor.Editor`

References: runtime assembly, `nadena.dev.ndmf`, `nadena.dev.ndmf.vrchat`, `VRC.SDK3A` (through the version define
`DOCME_VRCSDK3A`). Everything NDMF-specific is under `#if DOCME_NDMF`, like Face Rebase.

Folders: `Core` (pure model logic, testable without NDMF), `Build` (the plugin and its steps), `Preview`, `UI`,
`Localization`.

### Core: merged menu model

`MenuModel.Build(avatarRoot, components)` returns a `MenuTree`:

- Nodes: `MenuNode` (name, icon, children, `Origin`: existing menu asset, or component and entry id), `ControlNode`
  (toggle or radial with resolved parameter), plus a separate list of trigger rules.
- The root is the descriptor's existing expressions menu, read recursively into `MenuNode`s marked existing. Existing
  controls are kept verbatim as opaque leaves (their `VRCExpressionsMenu.Control` is carried along).
- Components are visited in hierarchy order (depth-first over the avatar), which makes merging deterministic.
- Each component installs at `installPath`: each segment is trimmed and matched exactly (case-sensitive) against the
  names of the current node's submenu children; a missing segment creates a new submenu node and records an
  Information message. `AsSubmenu` adds one node first.
- Inside a component, entries are placed under their `parentId` in list order. Orphans (parent id missing) go to the
  component's root with a warning.
- Overflow: after merging, any node with more than 8 controls keeps the first 7 and moves the rest into a chained
  `More` submenu (repeated as needed), recorded as an Information message. The inspector shows the slot counter in
  warning colour at 8 and marks the node when a split will happen.
- The tree is also what the inspector renders. Both the inspector and the build use the same `MenuModel`, so what the
  tree shows is what gets built.
- Targets that are not part of the avatar (an object outside the avatar root) are reported
  (`ad.warn.target_outside_avatar`) and skipped everywhere.
- Two controls that change the same target (object, blendshape, material slot) are reported as Information
  (`ad.info.target_shared`). Precedence when both are on: the control later in tree order wins, because its FX layer is
  added later. Members of one exclusive group are exempt (their layer resolves the overlap itself). The preview filter
  (phase 4) applies the same rule. Since plan 14 task P a material property is the exception for a trigger that is not
  met (see "Material properties on triggers"): its inactive state leaves a property another layer owns alone, and the
  preview writes Fade triggers after every other trigger but blend shape links, since their layers always end up last,
  and links after them, since their own layers are built after Fade's and the final pass copies their shape from
  every FX clip, Fade's Near clips included (plan 14 task S).

### Core: parameter planning

`ParameterPlanner.Plan(tree)` returns the parameter table:

| Control | Parameter | Type | Default | Bits when synced |
|---|---|---|---|---|
| Toggle, no group | `parameterName` or `<prefix>/<name>` | Bool (custom name: type of the existing parameter, else Bool) | `defaultOn` | 1 (8 for a custom int or float) |
| Toggle in group `G` | `<prefix>/<G>` | Int | value of the member with `defaultOn`, else 0 | 8 |
| Radial | `parameterName` or `<prefix>/<name>` | Float | `defaultValue` | 8 |
| Trigger | none (uses the source) | | | 0 |

Names are sanitised (trimmed, `/` inside a name replaced by `_`). Auto names that collide within the avatar get ` 2`,
` 3` appended in tree order. Auto-named toggles are always Bool.

Custom names follow the avatar's existing declaration:
- A name that exists as an expression parameter is reused: its type, saved, synced and default settings apply and the
  control's own flags are ignored (an Information message says so). A toggle is on by default when the existing default
  equals the control's value. Nothing is appended to the expression parameters for it and it costs no new bits.
- A name that exists only as an animator parameter (VRChat built-ins, contacts, PhysBones, other tools) binds to that
  parameter with its type; no expression parameter is created and no bits are counted.
- A new custom name is typed from the values that bind to it: Bool for the value 1, Int for another whole value, Float
  for a fractional value; several controls on one new name widen the type (Bool to Int to Float).
- A value the existing type cannot hold (a Bool asked for 3, an Int asked for 0.5) or a radial on a non-float parameter
  is `ad.error.parameter_type_conflict`.

Group values: explicit `groupValue` first, then remaining members get the lowest free values in tree order starting at
1. Two members with the same explicit value is an error and the later member is assigned automatically so the plan stays
consistent. Several members with `defaultOn` is a warning; the first wins.

Entry ids are GUIDs assigned on creation. Copying a component duplicates them; the model warns
(`ad.warn.duplicate_entry_id`) and still binds every control to its own parameter because bindings are keyed by the
control node, not by the id. The inspector reassigns duplicate ids when it loads a component.

`ParameterPlanner.CountBits(avatarRoot)` is the inspector's memory bar: AvatarDoctor's new synced bits from the plan,
everything else from `ParameterInfo.ForUI` minus AvatarDoctor's own provided parameters. The build's budget check uses
the same count: the existing asset's cost plus exactly the bits of the parameters AvatarDoctor appends.

### Build: `AvatarDoctorPlugin`

Qualified name `docme.avatar-doctor`, display name `DocMe AvatarDoctor`, theme colour teal `#3FC9A8`. One sequence in
`BuildPhase.Transforming`, requiring `AnimatorServicesContext`, `BeforePlugin("nadena.dev.modular-avatar")`, with the
preview filter attached. The pass, `Build toggles and menus`, runs these steps in order:

1. **Collect**: enabled `AvatarDoctorMenu` components under the avatar root. None: return.
2. **Model**: `MenuModel.Build`, `ParameterPlanner.Plan`. Report model errors and warnings; stop on errors.
3. **Capture base state**: for every target the scene's current value before AvatarDoctor changes anything:
   `activeSelf`, blendshape weight, material in slot. These are the Off values.
4. **Apply defaults to the scene**: for each toggle in tree order, write the default state (on targets if `defaultOn`,
   else off targets); radials write `lerp(min, max, defaultValue)`; blendshape triggers write their effect for the
   source's current weight. Later writers win.
5. **Animator**: get the FX controller, decide Write Defaults, add parameters, generate layers (below).
6. **Clip rewriting** for blendshape triggers (below; replaced in plan 14 task S by a layer per trigger and a hidden
   copy of the shape added in the final pass, see "Smooth blend shape triggers and requirements").
7. **Expression parameters**: clone the asset if not temporary (create one if missing), set `customExpressions = true`,
   append the planned parameters that are new. Reused and animator-only parameters are never written.
8. **Menus**: serialise the merged tree. New `VRCExpressionsMenu` for every AvatarDoctor node; existing nodes on an
   install path are cloned copy-on-write and their control appended; untouched existing menus keep their asset. The root
   is cloned (or created) and assigned to the descriptor. All new assets go through `context.AssetSaver.SaveAsset`.
9. **Cleanup**: `DestroyImmediate` every `AvatarDoctorMenu`.

Layer generation, all layers named `AD: <name>`, weight 1, `LayerPriority.Default`, every transition with no exit time,
duration 0, fixed duration:

- **Toggle**: states `Off` (default) and `On`, each with a clip writing every target: objects `m_IsActive` on the
  GameObject binding, blendshapes `blendShape.<name>` on the renderer, materials as PPtr curves
  `m_Materials.Array.data[<slot>]`. Off writes the captured base values. Transitions `Off -> On` and `On -> Off` on the
  parameter: bool `If` and `IfNot`; int `Equals` and `NotEqual`; float `Greater v - 0.5` and `Less v - 0.5`.
- **Exclusive group**: one layer, states `Off` (default) plus one per member. State k writes member k's on targets and
  every other member's off targets, on-targets winning where members overlap. Transitions from Any State with
  `Equals k` (and `Off` with `NotEqual` for every member value), `CanTransitionToSelf = false`.
- **Radial**: one state whose motion is a 1D blend tree on the float parameter, thresholds 0 and 1, child clips at
  `minWeight` and `maxWeight` for every shape.
- **Trigger, source Toggle**: like a toggle layer, conditions taken from the source toggle's planned parameter and value
  (inverted when `sourceToggleOn` is false).
- **Trigger, source Parameter**: like a toggle layer with the condition. The parameter is added to the FX controller if
  missing, typed from `ParameterInfo` (VRChat built-ins, contacts, PhysBones, other plugins); unknown names default to
  Float with a warning. No expression parameter is created.
- Write Defaults on every generated state follows the rule above. Every clip writes all of its targets in both states,
  so correctness does not depend on the setting.

Clip rewriting for **Trigger, source BlendShape** (the original design, replaced in plan 14 task S): binding
`B = blendShape.<sourceShape>` on the source renderer's path.
For every clip returned by `AnimationIndex.GetClipsForBinding(B)`:

- read the source curve `S(t)` in 0..100;
- blendshape effect `Follow`: write curve `S(t) * multiplier` (keys and tangents scaled), clamped to 0..100;
- blendshape effect `Fixed`: write a stepped curve, `weight` where `S(key) >= sourceThreshold`, else the captured base
  weight, one key per source key with constant tangents;
- object effect: stepped `m_IsActive`, `activeWhenOn` where the source is at or above the threshold, else the opposite;
- material effect: stepped PPtr curve, `material` where at or above the threshold, else the base material;
- `Replace`: remove the source curve from the clip after every link has been processed, so several links on the same
  source all see it; `Add`: keep it;
- an existing curve for an effect binding in the same clip is overwritten, reported as Information with the clip name;
- an effect that names the link's own source shape is skipped (in the clips and in the scene default) and warned once
  per entry (`ad.warn.link_effect_is_source`).

Clips inside blend trees are rewritten the same way and blend as the source does. Marker clips (VRChat proxy
animations) are immutable and skipped with a warning naming the clip.

### Preview: `AvatarDoctorPreview` (IRenderFilter)

- `CanEnableRenderers => true`. Preview menu entry "AvatarDoctor" through a `TogglablePreviewNode`.
- `PreviewState` holds `PublishedValue<ImmutableDictionary<string, float>> Overrides` keyed by entry id: toggles and
  triggers 1 or 0, radials 0..1. The inspector's Default, On, Off buttons and the radial slider write it; Default removes
  the key.
- `GetTargetGroups`: for each avatar root, observe every `AvatarDoctorMenu`, the overrides, and for blendshape
  triggers the source renderer's current weight. Compute the effective state of every control: override, else default.
  Build a per-renderer state for every renderer under the avatar: `visible` (walk the ancestor chain: any ancestor whose
  effective state hides it makes it hidden, any shown ancestor that is inactive in the scene makes it visible), a map
  of shape index to weight, a map of slot to material. Emit a group only for renderers whose state differs from the
  scene. Group data is a record with value equality so unchanged renderers keep their nodes.
- Node `OnFrame`: `proxy.enabled = visible`, then weights and materials on the proxy.
- The preview never touches the original renderer or GameObject.

### Inspector and window (UI Toolkit)

`AvatarDoctorMenuEditor : Editor` returns an `AvatarDoctorView` bound to the serialized object.
`AvatarDoctorWindow : EditorWindow` (`Tools/DocMe/AvatarDoctor`) hosts the same view for the selected component and
adds the USS class `ad-wide`, which switches the grid to tree beside detail. Structure as in the mockup:

- **Header**: install target field (dropdown listing every submenu path of the merged tree plus "Avatar root menu",
  free text allowed), install mode, parameter prefix, the memory bar (avatar total, AvatarDoctor share, free bits),
  and a preview status line when NDMF preview is off.
- **Tree**: Unity's `TreeView` over `MenuModel` rows. Row: kind icon, name, then chips: slot counter `n/8` on
  submenus, default dot, group chip `Outfits 1`, source chip with the other component's name, `existing` chip,
  trigger chip with the source, warning icon with tooltip. Toolbar: search, `+ Toggle`, `+ Radial`, `+ Submenu`,
  `+ Trigger`. Triggers sit under a `Triggers` group row at the bottom. Drag rows to reorder or reparent
  (`TreeView.reorderable`; a custom manipulator if 2022.3's tree cannot reparent). Dropping GameObjects from the
  Hierarchy onto the tree creates one toggle per object with that object as a shown target. Right-click: duplicate,
  delete, move to submenu.
- **Detail**: the selected entry through bound `PropertyField`s: name, icon, flags as switches, group dropdown
  (existing names plus "New group"), resolved parameter in monospace with an `auto` or `custom` tag and the bit cost,
  then Objects, Blendshapes and Materials sections as `ListView`s with drop zones. Blendshape rows: renderer picker,
  searchable shape picker, weight slider. Trigger detail: source type, its fields, then the same effect sections.
  Entries from other components show a note and a `Select that component` button instead. Existing menu items show a
  note only.
- **Wheel**: a custom `VisualElement` drawing eight wedges with `Painter2D` (arcs), the selected submenu's name in the
  centre with `n of 8`, icons or kind glyphs in the wedges, labels outside. Click a wedge to select, click a submenu
  wedge to descend; crumbs above go back. Hidden for triggers.
- **Scene preview**: a three-way Default, On, Off control on toggles and triggers, a slider on radials, writing
  `PreviewState.Overrides`. Blendshape triggers show a note that they follow the source weight.
- Styling: USS with Unity's theme variables (`--unity-colors-*`) for surfaces and text, one teal accent
  (`#3FC9A8` dark, `#128A6E` light chosen by `EditorGUIUtility.isProSkin`, applied as a class on the root), semantic
  colours for warnings and errors, violet for source chips. Icons are 32 px PNGs under `Editor/UI/Icons`, located
  through the GUID of a marker asset so the folder can move.
- Localization: an NDMF `Localizer` over `Editor/Localization/en-US.json`; elements carrying the `ndmf-tr` class get
  their label from the key, the same mechanism Modular Avatar uses. All inspector strings and error messages live in
  that file.
- Renaming a submenu in the editor updates `installPath` on every other component in the scene that points at it,
  with Undo.
- Every edit the inspector makes outside `SerializedObject` binding (add, duplicate, delete, move, rename, id repair)
  is Undo-recorded and, when the component sits on a prefab instance, recorded as an instance override with
  `PrefabUtility.RecordPrefabInstancePropertyModifications`; otherwise Unity drops the change on save.
- The inspector's merged plan includes disabled components (their rows carry a `disabled` chip) so a disabled
  component stays editable; the build still skips them.
- Copying a component duplicates its entry ids; the inspector reassigns them on load and remaps `parentId` and
  `sourceToggleId` through the same id map so the structure survives.

### Errors and validation

Live in the inspector (warning icon on the row plus a box in the detail) and at build through `ErrorReport`.

| Case | Build severity | Behaviour |
|---|---|---|
| Synced bits exceed 256 after planning | Error | Message lists AvatarDoctor's bits and the total. |
| Custom parameter name cannot hold the control's value, or a radial binds a non-float | Error | Names the parameter and the component. |
| Custom name reuses an existing expression parameter | Information | Its declaration (type, saved, synced, default) wins over the control's flags. |
| Two controls change the same target | Information | Names both entries; the later one in tree order wins. |
| Target outside the avatar | NonFatal | Target skipped. |
| Duplicated entry id (copied component) | NonFatal | Both controls still work; the inspector reassigns ids. |
| Two group members with the same explicit value | Error | |
| Target object, renderer, material or blendshape missing | NonFatal | Target skipped, references the entry's component. |
| Several `defaultOn` in a group | NonFatal | First wins. |
| Toggle, radial or trigger without effects | NonFatal | Nothing generated for it. |
| Trigger source toggle or parameter not found | NonFatal | Trigger skipped. |
| Marker clip animates a trigger source | NonFatal | Clip skipped, named. |
| Install path created submenus | Information | |
| Menu split into `More` pages | Information | |
| Effect curve overwrote an existing curve | Information | Names the clip. |
| Two components with `installMode` `AsSubmenu` and the same name at the same path | Information | Merged into one submenu. |

### Tests (`DocMe.AvatarDoctor.Tests`, editor NUnit)

- Core: merge order and install path resolution (new segments, existing menus, `AsSubmenu`), orphan handling,
  overflow splitting (9 and 16 controls), parameter naming and collision suffixes, bit counting, group value assignment
  with and without explicit values, validation cases from the table.
- Build: a generated test avatar (descriptor, body renderer with blendshapes, a few child objects, an FX controller
  with one existing layer, an existing menu with one submenu). Run the pass through a `BuildContext` with the
  animator services context activated and assert: layer names and counts, state motions and curve values for Off and
  On, group state matrix, radial blend tree thresholds, expression parameters (type, default, saved, synced), menu
  structure including a cloned existing submenu and an untouched one, Write Defaults choice with a unanimous-off
  controller, scene defaults applied, components destroyed.
- Clip rewriting: a controller whose clip animates the source shape; assert follow curves scale, fixed and object
  curves step at the threshold, `Replace` removes the source, blend tree children are rewritten.
- Running: the Unity Test Runner, or the temp menu approach used for Face Rebase when in auto mode.
- Manual: Gesture Manager in play mode (NDMF apply on play), and inspector screenshots reviewed on an artifact page
  before the UI is called done.

## Package layout

```
Packages/dev.docme.avatar-doctor/   (Assets/DocMe/AvatarDoctor/ until 2026-10-10)
  package.json                      (for a later VPM release; name dev.docme.avatar-doctor)
  README.md
  Docs~/design.md, plan-1-model-and-build.md, plan-2-inspector.md, plan-3-triggers.md, plan-4-preview-wheel-window.md
  Runtime/DocMe.AvatarDoctor.asmdef, AvatarDoctorMenu.cs, MenuEntry.cs, Targets.cs
  Editor/DocMe.AvatarDoctor.Editor.asmdef
    Core/MenuModel.cs, ParameterPlanner.cs, TriggerPlanner.cs, EffectMath.cs, ModelMessage.cs
    Build/AvatarDoctorPlugin.cs, AvatarDoctorBuilder.cs, BaseState.cs, ClipContent.cs, DefaultsApplier.cs,
      AnimatorBuilder.cs, ClipRewriter.cs, TriggerBuilder.cs, ParametersWriter.cs, MenuWriter.cs
    Preview/PreviewState.cs, PreviewModel.cs, AvatarDoctorPreview.cs, PublishedValueWatch.cs
    UI/AvatarDoctorMenuEditor.cs, AvatarDoctorWindow.cs, AvatarDoctorView.cs, HeaderView.cs, MenuTreeView.cs,
      TreeRow.cs, EntryDetailView.cs, TargetListView.cs, WheelModel.cs, WheelView.cs, KindGlyph.cs, MemoryBar.cs,
      ParameterPicker.cs, ShapePicker.cs, EntryEditing.cs, AvatarPlanCache.cs, HierarchyDropManipulator.cs,
      AvatarDoctor.uss
    Localization/L.cs, AvatarDoctor.en-US.json
    Introspection/AvatarDoctorParameterProvider.cs
  Tests/Editor/DocMe.AvatarDoctor.Tests.asmdef, *.cs
```

There is no `Validation.cs` (the model reports its own messages), no `*.uxml` (every view is built in code), and no
`Icons/` folder (see the phase 4 amendments below).

## Phases

Each phase ends with passing tests and a Gesture Manager check.

1. **Model and build**: runtime types, merged model, parameter planning, base state and defaults, toggle, group and
   radial layers, expression parameters and menus, parameter provider, errors with the localizer, tests. A temporary
   default inspector is enough to author test data.
2. **Inspector**: the UI Toolkit view in the inspector: header, tree, detail, drag and drop, live validation, memory bar,
   install path picker and rename propagation.
3. **Triggers and links**: trigger entries, toggle and parameter sources, blendshape clip rewriting, tests, trigger UI.
4. **Preview, wheel, window, README**: the render filter and preview controls, the wheel, the window host, icons and
   theme polish, the README for release.

## Phase 4 amendments (2026-09-17)

Rulings made while building the preview, wheel and window, amending the sections above:

- Icons stay vector glyphs (`KindGlyph`), drawn in code. The 32 px PNG icon folder described under "Inspector and
  window" is dropped for version 1; there is no `Editor/UI/Icons` folder to locate through a marker asset.
- Wheel wedges show the entry's own `icon` texture when one is set, and fall back to the kind glyph otherwise.
- In the scene preview, a condition trigger (source Toggle or Parameter) applies nothing until its `On` or `Off`
  button has been pressed; it starts idle rather than guessing a state, and `Default` clears the override and puts it
  back to idle. A blend shape link always follows the source shape's current weight in the scene and cannot be forced
  on or off.
- `PreviewModel.Compute`, called from `AvatarDoctorPreview.GroupsFor`, emits a render group for a renderer only when
  its computed state (visibility, blend shape weights, materials) differs from what the scene already shows; an
  unchanged renderer is left alone.
- The wheel always draws eight slots, whether or not the shown submenu has eight items; unused slots stay empty.
  Clicking a wedge is exactly a tree selection (the same row becomes selected); clicking a submenu wedge also makes
  that submenu the wheel's new centre.
- `AvatarDoctorWindow` follows the current Unity selection through `AvatarDoctorWindow.Resolve`: the selected
  object's own `AvatarDoctorMenu` if it has one, else the first one found under the selected avatar, else the first
  one found under the selected object itself.
- The inspector's tree shows the overflow `More` submenus the build will create for a menu past 8 controls, not
  just a warning about them, so the tree the author edits matches the tree that gets built.
- The shared-target Information message (`ad.info.target_shared`) considers triggers as well as menu controls: a
  trigger and a toggle, or two triggers, that change the same object, blend shape or material slot are reported
  together, the same as two toggles would be.
- A PhysBones prefix parameter reported by NDMF expands into its five typed sub-parameters (`_IsGrabbed`,
  `_IsPosed` as Bool; `_Angle`, `_Stretch`, `_Squish` as Float) before the trigger picker and parameter planning see
  it, so a trigger can watch any one of them by name.
- A `Replace` blend shape link that could not write any effect into a given clip keeps that clip's source curve
  instead of deleting it, so the shape does not silently stop animating; this is reported once per entry and clip
  (`ad.warn.replace_kept_source`).
- The preview handles `MeshRenderer` and `SkinnedMeshRenderer` only. Any other renderer under the avatar is left to
  the scene, since NDMF's proxies carry nothing the model knows how to write.
- A control's default state in the preview is the default of the parameter the plan binds it to, not the entry's own
  `Default on` or `Default`: a custom name pointed at an existing declaration takes that declaration's default. The
  preview plans this itself from the expression parameters asset and the FX controller asset, never through
  `ParameterInfo`, which would re-enter the preview.
- The wheel is hidden by default in the inspector and shown by default in the window. The tree's `Wheel` button flips
  an `EditorPrefs` key, one per host (`DocMe.AvatarDoctor.Wheel.Inspector` and `DocMe.AvatarDoctor.Wheel.Window`).
- Wedge labels sit inside the ring, under the wedge's icon, rather than around the outside of it.
- While the tree's search field holds text, the tree is fully expanded and drag reorder is off: a filtered tree does
  not show the order a drop would land in.

## Phase 5: overrides and pin mode (approved 2026-09-17)

Purpose: switch a blend shape off (or hold it at any value) in every animation that uses it, either permanently or
while a menu toggle is on, without touching the clip assets on disk. Example: `FTEffectsCringe` is animated by two
face tracking clips in the FX controller; the creator wants it silent.

**Override entry** (`EntryKind.Override`): a rule, not a menu control. It ignores `parentId`, adds no menu slot, no
parameter and no synced bits, and lists blend shape targets (`renderer`, `shape`, `weight` as the pinned value,
default 0). Overrides sit under an `Overrides` group row at the bottom of the tree, next to `Triggers`, and can be
added with `+ Override`, duplicated and deleted like triggers.

Build: a second pass of the plugin, `Pin overridden shapes`, in the Transforming phase after
`nadena.dev.modular-avatar`, so clips merged by Modular Avatar are covered too. The first pass records the overrides in
the build context state before it destroys the components. The pass resolves the source binding
(`blendShape.<shape>` on the renderer's path) and, for every clip `AnimationIndex.GetClipsForBinding` returns in any
playable layer, replaces the curve with a constant curve at the pinned value (two constant keys spanning the original
curve's length, so the clip keeps the length exit times and blend durations are measured against).
Marker clips are skipped with `ad.warn.marker_clip_skipped`. AvatarDoctor's own generated clips are rewritten like
any other, so an override always wins over a toggle on the same shape (`ad.info.target_shared` names both). The scene
default writes the pinned value to the mesh. Messages: `ad.info.override_applied` (shape, clip count) and
`ad.warn.override_unused` when nothing animates the shape (the scene value still changes). A shape that does not
exist or a renderer outside the avatar is reported with the existing target messages.

**Pin mode** (`ShapeEffectMode.Pin`) on blend shape targets of toggles, exclusive group members and condition-driven
triggers: while the control is on, the clip writes the target's weight, exactly like `Fixed`; while it is off, the
clip writes nothing for that shape, so the animations below AvatarDoctor's layers drive it again. `Fixed` keeps
writing the captured scene value when off. Radials and blend shape links do not offer `Pin`. Scene defaults and the
preview follow the same rule: a Pin target is written only while its control is on.

**Where is it used**: the inspector scans the descriptor's playable layer controllers once per plan version and
indexes every blend shape curve by renderer path and shape (`AnimationUsage`). Blend shape rows show a chip reading
`{0} clips` with the clip names in the tooltip; the Override panel lists the clips in full. The scan
sees the controllers on the descriptor only; animators merged by other tools at build time are not listed, and
neither is anything on a descriptor whose playable layers are not customized.

**Preview**: an Override pins the shape in the scene preview at all times; a Pin target forces the weight while its
control is on and leaves the scene value alone while it is off.

**Not in scope**: object or material overrides, scaling instead of pinning, editing clips on disk, and layers that
other tools append after Modular Avatar (they are not rewritten; the Information line lists what was).

### Phase 5 amendments (2026-09-17)

The build matches this section with the following differences, found by reading the implementation against it:

- **Pin layers force Write Defaults off, and live apart from the control's own layer.** This section says a Pin
  target's clip "writes nothing" for that shape while its control is off, as if that happened inside the control's
  existing On/Off states. In the build, a control with any Pin target instead gets a second, dedicated FX layer
  named `<layer name> (pin)` (for example `AD: Mute (pin)`), whose states all have Write Defaults forced off; the
  control's own main layer excludes every Pin target from its clips entirely and keeps following the avatar's usual
  Write Defaults decision. Because of this extra layer, the VRChat SDK's build validation may report a mixed Write
  Defaults notice on avatars that use Pin mode; that notice is expected.
- **`ad.warn.override_conflict` is new and is not named here.** Two overrides that both hold the same shape (the
  same renderer and blend shape) each get reported, one warning per colliding pair, naming both overrides and the
  shape; the later one in the merged tree still wins, exactly as an override wins over a toggle on the shared target.
- **A group's pin layer switches members directly through `AnyState`, not through its own `Off` state.** A plain
  toggle's or a condition trigger's pin layer has one pinned member and transitions Off to On and back. An exclusive
  group's pin layer instead has one `AnyState` transition per pinned member plus one to `Off`, the same topology as
  the group's main layer, so moving from one pinned member straight to another pinned member never passes through
  `Off` and never releases the shape in between. Its `Off` condition is scoped to the pinned members alone, so an
  unpinned member of the same group releases the shape as well. A toggle or condition trigger whose blend shape
  targets are all pinned gets no main layer at all: its main clips would be empty.
- **The usage index is a persistent, keyed memo with its own asset-change hook, not a per-plan scan.** `AnimationUsage`
  keeps one cached index per avatar descriptor instance, keyed by a fingerprint of that descriptor's controllers
  (their instance ids and clip counts); a rebuilt `AvatarPlan` reuses the cached index when that fingerprint has not
  changed, rather than rescanning on every plan version as this section implies. A dedicated `AssetPostprocessor`
  (`AnimationUsagePostprocessor`) clears the whole cache, and every cached `AvatarPlan` with it, whenever a `.anim`,
  `.controller` or `.overrideController` asset is imported, deleted or moved, so an edit made on disk is picked up
  without needing a new plan version to force it. An edit still open in the Animation window is not seen until the
  asset is saved: the import is what the hook listens for.

## Phase 6: rename, icon capture, rich names (approved 2026-09-17)

**Rename.** The component `AvatarDoctorMenu` becomes `AvatarDoc`: class, file (`Runtime/AvatarDoc.cs`, keeping the script's
asset id so scenes and prefabs stay bound), inspector title, add-component path `DocMe/AvatarDoc`, and every message or
label that names it. A `MovedFrom` attribute records the old name. The tool, package id, namespaces, window path and
parameter prefix stay `AvatarDoctor`.

**Icon capture.** Toggles, radials and submenus get a `Capture icon` button that opens the capture window
(`IconCaptureWindow`): a live 256 by 256 preview rendered by a temporary capture camera (a hidden object destroyed when
the window closes) that copies the Scene view camera's position, rotation and field of view each repaint, so the creator
frames the shot by moving the Scene view. `Fit` points the capture camera at the bounds of the entry's pieces and fills
the frame without touching the Scene view. The pieces are the entry's object targets, shown or hidden when on (amended 2026-09-18: a toggle that only hides
pieces captures them too), with their children, and the renderers of its blend shape and material targets; for a submenu, the pieces of its descendants.
During a render every other renderer in every loaded scene is hidden through `Renderer.forceRenderingOff` and restored
afterwards, so no object, component or scene is modified, unloaded or removed. The camera clears to transparent
(a solid colour is an option in the window) and renders to an ARGB render texture; `Capture` writes
`Assets/AvatarDoctor Icons/<avatar name>/<plain entry name>.png`, sets the importer to a VRChat menu icon (texture,
max size 256, alpha is transparency, sRGB, no mipmaps, clamp), and assigns the texture to `entry.icon` with Undo.
No new scene is created.

**Rich names.** VRChat renders expression menu names with TextMeshPro rich text, so `MenuEntry.name` may carry
`<color=#rrggbb>`, `<b>` and `<i>` tags. The detail panel shows a plain-text name field plus a style row (colour swatch,
`B`, `I`) that wraps the whole name; an `edit tags` switch exposes the raw string for partial styling by hand. A
preview label renders the styled name. `MenuEntry.PlainName` (tags stripped) is what the build and the model use for
parameter names, layer names, install path segments, submenu matching and rename propagation; tree rows and the wheel
render the styled name. Icons and rich names never change generated parameters.

**Tree state (added 2026-09-17).** Submenu rows and the Triggers and Overrides group rows start collapsed. The rows a
creator expands are saved per component in EditorPrefs (keyed by the component's global object id) and restored when
the inspector or window rebuilds, across domain reloads and editor restarts. Selecting an entry programmatically
(add, duplicate, move, wedge click, Hierarchy drop) expands its ancestors. The search filter expands everything
while active and restores the saved state when cleared.

**Not in scope**: capturing the toggle's On state for blend shape or material effects (those render with their
current scene values; a piece switched off in the scene is drawn anyway), icons for triggers and overrides, per-character styling in the UI (raw tags cover it), and
`<size>` or other tags (they pass through untouched).

### Phase 6 amendments (2026-09-18)

The build matches this section with the following differences, found by reading the implementation against it:

- **The tree's expansion is saved by polling, not by an event.** The "Tree state" paragraph says a creator's
  expansion choices are "saved per component" without saying how a hand click on the tree's own arrow is caught.
  This Unity version's `TreeView` has no `itemExpandedChanged` event, so `MenuTreeView.PollExpansion` instead diffs
  every row's live `IsExpanded` state against the saved set on the same 250 ms timer `AvatarDoctorView` already uses
  to poll the plan version, and saves once when something differs. It stays silent while `Refresh` itself is
  changing expansion (a `restoring` flag) or while the search filter has forced everything open.
- **The capture preview renders only when something it shows changed.** `IconCaptureWindow.Tick` runs on
  `EditorApplication.update` and calls `Poll`, which renders again, at most once every 0.2 seconds, when the Scene
  view camera's position, rotation, field of view or orthographic size changed, when the avatar's plan version
  (`AvatarPlanCache.VersionFor`) moved, or when a change event named something the last picture drew: a target
  renderer or its object, any transform or object from it up to its top object, or one of its materials (the
  window listens to `ObjectChangeEvents` while it is open). A window left open while nothing changes renders
  nothing. Every render goes through one hidden camera (`IconCapture.Rig`) made in `OnEnable` and destroyed in
  `OnDisable` (plan 13 task D).
- **`Fit` freezes the framing.** Nothing in this section says pressing `Fit` stops the preview from following the
  Scene view afterward. In the build, `Fit` stores the fitted frame and `Poll` ignores Scene view moves while a
  frame is stored, so the framing stays until `Fit` is pressed again or the window is reopened; an edit of a
  piece or a plan change still renders it again within that framing.
  `IconCaptureWindow.Open` clears the stored frame itself, for a window that is already open and only being pointed
  at another entry, and `OnEnable` clears it as well, so a domain reload drops it too.
- **The raw-tags field can stay visible past the edit that fixed it.** The section says the switch "exposes the raw
  string for partial styling by hand" but not what happens once that raw string becomes a recognised shape again.
  `NameStyleRow.Publish` only clears `showRaw` when the switch itself reads off, so a raw edit that resolves back to
  a plain colour, bold and italic combination leaves the raw field showing until the row reloads the entry (leaving
  and reselecting it, or reopening the inspector), even though the style buttons re-enable immediately.
- **An `x` button clears the colour.** The style row in this section is "colour swatch, `B`, `I`"; the shipped row
  also has a small `x` button next to the swatch (`NameStyleRow`'s `ad-style-clear-color`) that sets the colour back
  to none in one click, enabled only while a colour is set.
- **The recognised shape is stricter than "a colour, bold and italic tag."** `RichName`'s pattern requires the tags
  in exactly this order and nesting: an optional `<color=#......>` outermost, then an optional `<b>`, then an
  optional `<i>`, closing in reverse. It also only accepts a 6 or 8 digit hex colour (`#rrggbb` or `#rrggbbaa`); a
  hand-written 3 or 4 digit colour such as `<color=#fff>`, or the same three tags nested in a different order, does
  not match and is treated as `Custom`, the same as any other tag arrangement the row cannot reduce to one colour
  plus bold and italic.
- **The switch's label reads "Tags", not "edit tags".** This section calls it an "`edit tags` switch"; the shipped
  localisation key `ad.ui.style.raw` reads "Tags".
- **The capture camera lives for one render, on a hidden holder.** This section describes "a hidden object destroyed
  when the window closes". In the build `IconCapture.Render` makes the camera for each render and destroys it in a
  `finally`, and the camera is a child of a hidden holder object rather than a root object of its own. NDMF's
  `ProxyManager` hooks `Camera.onPreCull` for any camera that has no parent or no target texture, and during the
  hook it switches the proxied originals off and their proxies on, which would leave the icon showing proxies and
  hiding the pieces themselves. A parented camera with a target texture is skipped by that rule.
- **A piece switched off in the scene is drawn directly.** Nothing in this section says what happens to a target
  whose object is inactive, which a toggle that starts off usually is: the camera renders nothing for it and the
  icon comes out blank. `IconCapture.DrawSwitchedOff` instead draws those pieces itself, through a command buffer on
  the capture camera at `CameraEvent.AfterForwardOpaque` (a mesh renderer's shared mesh at its own matrix, a skinned
  renderer's `BakeMesh` result at its position and rotation), so nothing in the scene is switched on. `Graphics.DrawMesh`
  was tried first and does not reach an edit mode `camera.Render()`. Blend shape and material effects still render
  with their current scene values, since the bake reads them as they stand.

## Phase 7: menu edits, presets, import, coexistence (directed 2026-09-18)

The user asked for the three features recommended after phase 6 (editing the menu that already exists, presets, import)
and for AvatarDoctor to work on avatars that also use VRCFury or Modular Avatar. Small inspector changes asked for the
same day are recorded under "Phase 7 inspector changes" below.

### Verified facts (VRCFury as installed, Modular Avatar 1.17.1, NDMF 1.14.1)

- Build order at upload: NDMF runs every phase up to `Transforming` at callback order -11000 (the AvatarDoctor build
  pass runs here before Modular Avatar, then Modular Avatar, then the `Pin overridden shapes` pass after it). The whole
  VRCFury build runs at -10000. The NDMF `Optimizing` phase runs at -1025, after VRCFury. The VRCFury parameter
  compressor runs at the very end and rewrites the expression parameters asset in place.
- Modular Avatar writes the expressions menu in `Transforming` (`MenuInstallPluginPass`), installing into whatever
  menu the descriptor holds, which is the menu AvatarDoctor wrote. Its merged menu preview (`VirtualMenu`) is internal
  and closed to other assemblies. Its components (`ModularAvatarMenuItem`, `ModularAvatarMenuInstaller`,
  `ModularAvatarObjectToggle`, `ModularAvatarShapeChanger`, `ModularAvatarMaterialSetter`) are public, in the assembly
  `nadena.dev.modular-avatar.core`; `AvatarObjectReference.Get(component)` resolves a target. A blank menu item
  parameter gets a generated name during the build. Modular Avatar has nothing that moves, renames or re-icons an
  item of an existing menu.
- VRCFury is not an NDMF plugin. It merges the menu of the descriptor into a new one, splits pages with a `Next`
  control, and attaches its controller, clips and menu at the end of its callback. Every VRCFury type is internal: the
  component is `VF.Model.VRCFury` with one `[SerializeReference] content`; a toggle is `VF.Model.Feature.Toggle`
  (`name` is a slash separated menu path with `\/` as an escaped slash, `state.actions`, `saved`, `slider`,
  `defaultOn`, `defaultSliderValue`, `enableExclusiveTag`, `exclusiveTag`, `enableIcon`, `icon`, `useGlobalParam`,
  `globalParam`, `hasTransition`, `securityEnabled`, `separateLocal`, `holdButton`, `invertRestLogic`). Actions are
  `ObjectToggleAction` (`obj`, `mode` TurnOn, TurnOff, Toggle), `BlendShapeAction` (`blendShape`, `blendShapeValue`,
  `renderer`, `allRenderers`), `MaterialAction` (`renderer`, `materialIndex`, `mat`) and others. Parameter names are
  `VF<number>_<name>` and change between builds unless the toggle uses a global parameter. Other assemblies can read
  this data only through `SerializedObject` by field name.

### Coexistence rules

- AvatarDoctor never references VRCFury types; it reads VRCFury components through `SerializedObject` and recognises
  them by `GetType().FullName == "VF.Model.VRCFury"`. Modular Avatar components are read through their public API
  behind the define `DOCME_MA` (asmdef version define on `nadena.dev.modular-avatar`, assembly reference
  `nadena.dev.modular-avatar.core`, which Unity ignores when the package is absent). With neither package installed
  everything compiles and behaves as before.
- A third plugin sequence runs in `BuildPhase.Optimizing` (after VRCFury): `Finish after other tools`. It pins
  overridden shapes again (clips VRCFury generated only exist now; the pass is idempotent) and applies the menu edits
  that could not be resolved earlier.
- Foreign menu items are shown in the inspector tree on a best effort basis (`ForeignMenu`): Modular Avatar menu
  installers with a menu item or menu group (label or object name, submenus from children, `installTargetMenu`
  matched to the existing node that mirrors that asset, otherwise the root) and VRCFury toggles (their `name` path).
  Foreign rows carry a `Modular Avatar` or `VRCFury` chip, are read only apart from menu edits, are never written
  by the AvatarDoctor build, and do not count toward the AvatarDoctor page split: the other tool adds its own pages.
  The build model is made without foreign rows.
- Triggers and presets can name any parameter, so a VRCFury toggle with a global parameter or a Modular Avatar
  parameter can be a trigger source or a preset target. The parameter pickers list NDMF `ParameterInfo` names (this
  covers Modular Avatar) plus VRCFury global parameters read from the components.

### Menu edits

`AvatarDoc.menuEdits : List<MenuEdit>`. A `MenuEdit` names one existing or foreign control and changes how it
appears, without touching the source asset:

| Field | Meaning |
|---|---|
| `id` | GUID string |
| `source : List<string>` | plain names from the root down to the control (submenu names, then the control name) |
| `occurrence : int` | which match to take when several controls under that parent share the name, 0 first |
| `hide : bool` | remove the control from the built menu |
| `name : string` | new rich name; empty keeps the original |
| `icon : Texture2D` | new icon; null keeps the original |
| `move : bool`, `targetPath : string` | move the control (a submenu moves whole) into the submenu at this path, same format as `installPath`; missing submenus are created |
| `position : int` | index inside the target menu after the move, -1 appends (or keeps the place when not moving) |

Rules: names match plain (`RichName.Strip`) and exact. Edits apply in component order, then list order, after every
component has installed its entries, so install paths keep referring to the original menu and a target path may be a
submenu AvatarDoctor made. Moving a submenu into itself or its own descendant is an error
(`ad.error.edit_cycle`). An edit whose source is an AvatarDoctor entry is refused by the UI: those are edited
directly. `MenuModel.Build` applies every edit whose source resolves and marks the touched nodes so `MenuWriter`
rewrites them (`MenuNode.Edited`, counted by `ContainsGenerated`). At build, edits that did not resolve are kept in
`LateEditState` and tried again on the real menu assets by `MenuEditApplier`: once in the pass that runs after
Modular Avatar and once in `Finish after other tools`. The applier deep clones the reachable menu assets before
changing anything (`ObjectRegistry.RegisterReplacedObject`, saved through the build context), applies the edits,
and splits any menu it pushed past `VRCExpressionsMenu.MAX_CONTROLS` with a `More` page. An edit still unresolved
after the last pass reports `ad.warn.edit_source_missing`. In the inspector the model is built with foreign rows, so
the tree and the wheel show the edited result.

Inspector: existing and foreign rows become selectable and get a detail panel (`Name` with the style row and the
original as placeholder, `Icon`, `Move to` with the submenu path choices, `Hidden`, `Reset`). Edited rows show an
`edited` chip, hidden rows stay in the tree dimmed with a `hidden` chip. Dragging an existing or foreign row onto a
submenu row creates or updates the move; the context menu offers `Move`, `Hide` or `Show`, and `Reset`. All changes
go through `MenuEditing` with Undo.

### Presets

`EntryKind.Preset = 5`: a menu button that puts other controls into chosen states. Fields on `MenuEntry`:
`presetStates : List<PresetState>` (`entryId`, `on : bool`, `value : float` for radials) and
`presetParameters : List<PresetParameter>` (`name`, `value : float`) for parameters AvatarDoctor does not own.
A preset is a menu entry (style row, icon, capture of the pieces of the controls it switches on), lives in submenus
like a toggle, and costs no synced bits: its own parameter is a Bool that is neither saved nor synced.

Build: the menu control is a `Button` on the preset parameter. Layer `AD: <plain name> (preset)`: `Idle`, then
`Apply` when the parameter is true, back to `Idle` when it is false. `Apply` carries a
`VRCAvatarParameterDriver` with `localOnly = true` and one `Set` per target: the Bool of a toggle (or its Int or
Float binding value), the Float of a radial, the Int of an exclusive group set to the value of the member, a named
parameter as given. For an exclusive group only the member that is on is written; members that are off write the
group parameter to 0 only when the preset switches no member of that group on. Two members of one group both on is
`ad.error.preset_group_conflict`; a state whose entry no longer exists is `ad.warn.preset_target_missing`; a named
parameter that is neither in the expression parameters nor in the FX controller is `ad.warn.preset_parameter_unknown`.
Both states use an empty clip and follow the Write Defaults decision of the avatar.

Preview: the On button of a preset applies its states to `PreviewState` for the controls it names (one click, not a
held state); Default clears them. Inspector: `+ Preset` in the toolbar; the panel lists states (control picker across
every AvatarDoc on the avatar, On or Off, a slider for radials), named parameters, `+ Add`, and `From preview`,
which fills the list from the controls that currently have a preview override.

### Import

`ImportScanner.Scan(avatarRoot, tree)` returns `ImportCandidate`s: a source kind (`VRCFury`, `ModularAvatar`,
`FxLayer`), the menu path, a drafted `MenuEntry`, notes for everything that could not be carried over, and the
origin (components, or a layer name plus the menu control). `ImportWindow` (toolbar `Import`) lists them with
checkboxes, the source chip and the notes; `Import` creates the entries in the inspected component, under submenus
made from the path, with Undo. `Remove originals` (on by default) removes the imported VRCFury or Modular Avatar
components with `Undo.DestroyObjectImmediate`; for an FX layer it adds a hiding menu edit for the old control and
the layer name with the id of that edit to `AvatarDoc.removedLayers` (so a restore removes both without rescanning), and the build removes those layers from the FX clone before building
(`ad.warn.removed_layer_missing` when absent). Nothing is deleted from any asset.

Mapping:
- VRCFury toggle: path and name from `name`; `ObjectToggleAction` to object targets (`TurnOn` shown when on,
  `TurnOff` hidden when on, `Toggle` the opposite of the current state of the object); `BlendShapeAction` to blend
  shape targets (every skinned mesh with the shape when `allRenderers`); `MaterialAction` to material targets;
  `saved`, `defaultOn`; `slider` becomes a radial when every action is a blend shape action, with
  `defaultSliderValue`; the first exclusive tag becomes the exclusive group; icon; a global parameter becomes the
  custom parameter name. Notes for other actions, transitions, security, separate local state, hold button, inverted
  rest logic, extra tags.
- Modular Avatar: a `ModularAvatarMenuItem` of type Toggle with reactive components on the same object or below it
  (the rule Modular Avatar itself uses): `ObjectToggle` to object targets, `ShapeChanger` `Set` entries to blend
  shape targets (`Delete` is noted), `MaterialSetter` to material targets; `isSaved`, `isSynced`, `isDefault`,
  label, icon, a non blank parameter as the custom parameter name; path from parent menu items and the installer
  target. `Inverted` components are noted and skipped.
- FX layer: a Toggle control of the own menu of the avatar whose parameter drives exactly one FX layer with one on
  state (entered on the parameter being true or equal to the value of the control) whose motion is a clip made only
  of constant `m_IsActive`, `blendShape.*`, material slot and (amended 2026-09-19) `material.<property>` curves, which become
  material property targets. The entry reuses the existing parameter by
  name, so sync and saved settings stay with the parameter asset of the avatar. Anything else about the layer makes
  it a non candidate that is listed as unsupported with the reason.

### Phase 7 inspector changes (asked 2026-09-18, done before the plan)

- Capture includes object targets the entry hides, not only those it shows, so a toggle that only hides pieces can
  capture its icon.
- The button of the capture window reads `Save icon`, fills the row and carries the accent style (the window root
  now has the `ad-root` class the style variables live on).
- The style row exists only on entries that reach the VRChat menu (toggles, radials, submenus, and presets);
  triggers and overrides have a plain name field. The row is one line: `Style`, swatch, `x`, `B`, `I`, and `Tags` at
  the right end; the `In game` line shows only when the name carries styling.
- `Capture icon` sits at the right end of the icon field.
- Ticking `Custom name` releases the focus first, so the panel rebuilds at once and the text field appears.

### Material properties, blend time and custom animations (asked 2026-09-18, added to phase 7)

The user asked for controls that animate material properties and play custom animation clips. Both are new target
lists on toggles and radials (not on triggers, overrides or presets in this phase; triggers gained material properties
in plan 14 task P, see "Material properties on triggers"), next to objects, blend shapes and material slots. "Material property targets and dissolve" leaves the "Not in phase 7" list.

**Material property targets.** `MenuEntry.properties : List<MaterialPropertyTarget>`:

| Field | Meaning |
|---|---|
| `renderer : Renderer` | the renderer whose materials carry the property |
| `property : string` | shader property name such as `_MainHueShift` |
| `type : PropertyValueType` | `Float = 0`, `Color = 1`, `Vector = 2` |
| `value : float`, `color : Color` (HDR allowed), `vector : Vector4` | the value while a toggle is on, and the upper end of a radial |
| `minValue : float`, `minColor : Color`, `minVector : Vector4` | the lower end of a radial |

Unity animates a material property for the whole renderer (`material.<property>` on the renderer, with `.r .g .b .a`
for a colour and `.x .y .z .w` for a vector), so there is no slot. The base value, written while a toggle is off, is
read from the first shared material of the renderer that has the property; a property no material of the renderer
has is `ad.warn.property_missing` and the target is skipped. Radials blend from the lower to the upper end like blend
shapes do. Shared target reports and the override conflict rules treat `renderer + property` as one target key. The
scene preview shows property targets: the preview filter gives the proxy renderer cloned materials with the value
set and destroys them when the node is disposed. Scene defaults (`DefaultsApplier`) do not write material assets:
when a control is on by default the animator sets the value in the first frame, as it does for material slots.

**Blend time.** `MenuEntry.blendSeconds : float` (0 by default, toggles only, at most 10). The transitions between
the Off and On states of the main layer of the toggle take that long (fixed duration, no exit time), so blend shapes
and material properties fade. Object switches blend with them: Unity treats a blended `m_IsActive` above zero as
active, so a piece stays visible for the whole blend in both directions, which is what a dissolve needs. Pin layers
and exclusive group layers keep instant transitions in this phase; a group member with a blend time reports
`ad.info.blend_ignored_in_group`.

**Custom animations.** `MenuEntry.animations : List<AnimationTarget>` with `clip : AnimationClip` and
`pathRoot : Transform` (empty means the clip paths are relative to the avatar root; otherwise every path is prefixed
with the path from the avatar root to that object, which is what a prefab maker needs). Toggle: the On clip of the
control is the custom clips merged with the generated values; a binding that both write keeps the generated value
and reports `ad.info.animation_overridden`. The Off clip additionally writes the scene value of every binding the
custom clips animate (float bindings through `AnimationUtility.GetFloatValue`, object reference bindings through
`GetObjectReferenceValue`), so Write Defaults off avatars return to rest; a binding that does not resolve on the
avatar is `ad.warn.animation_binding_missing` and is left out of the Off clip. Radial: when a radial has custom
clips its state plays one merged clip with the radial parameter as motion time instead of the two clip blend tree;
the generated blend shape and property values become linear curves across the length of the clip (one second when
every custom clip is shorter than a frame). VRChat proxy clips are refused (`ad.warn.animation_proxy`). Custom clips
are not shown by the scene preview; the panel says so. Overrides still win: the pin pass runs over the merged clips.

**Inspector.** Two more sections in the target area of toggles and radials, in this order: Objects, Blend shapes,
Materials, Properties, Animations. A property row: renderer field, a property dropdown listing the properties of
the shaders on that renderer by display name and property name (floats, ranges, colours and vectors; a text field
for anything else), a value field that follows the type (slider for a range property, HDR colour field, four number
field), and for radials the lower end beside it. An animation row: clip field, path root field, remove. `Blend time`
sits under the flags row of a toggle as a seconds field. Icon capture counts the renderers of property targets as
pieces.

**Import.** VRCFury `MaterialPropertyAction` maps to a property target (`renderer2` or every renderer with the
property when `affectAllMeshes`; `LegacyAuto` resolves through the shader); `AnimationClipAction` maps to an
animation target with the clip and no path root, with the note that VRCFury may have rewritten paths itself.

### Not in phase 7

The sync budget hints, fix buttons on messages, batch icon capture,
templates, Japanese localisation, smoothed radials, VRCFury full controller menus as foreign rows.

### Phase 7 amendments (2026-09-19)

The build matches the sections above with the following differences, found while implementing and reviewing this
phase and recorded in the ledger (`progress.md`) and the task reports' Deviations sections:

- **Foreign items stay out of "More" pages.** `MenuModel.SplitOverflow`'s physical move used to sweep every item in
  the overflow tail, foreign or not, into the generated `More` node once counted; a fix partitions the tail first,
  so an item with `Foreign != ForeignSource.None` is re-appended to its original node right after `More` instead of
  moving inside it. Only the counting (which already excluded foreign items) needed no change.
- **Existing and foreign row keys come from the original path, not the current one.** `MenuNode.OriginalPath` and
  `MenuItem.OriginalPath` are stamped once, before `MenuEdits.Apply` runs, and `TreeRow.KeyFor`/`KeyForNode` key on
  them instead of the live `Path`. Without this, moving an existing or foreign row (which has no entry id of its
  own to key on) changed its tree key, dropping its selection and its expanded state.
- **Cosmetic edits skip the plan rebuild, and never as a no-op.** `AvatarPlanCache.NoteCosmetic` bumps the cache
  version and raises `Changed` without dropping a single cached plan, so every open view for the same avatar
  redraws from the plan it already holds. A rename only counts as cosmetic for a toggle or radial with a custom
  parameter name outside any exclusive group (nothing else reads the name into the plan); an icon is always
  cosmetic except a submenu's, which reaches the wheel through `MenuNode.Icon` instead of the live entry.
  `AvatarPlanCache.NoteSelfEdit` marks a write a panel just made so that write's own end-of-frame change event is
  not read back as an outside change a frame later.
- **The immediate refresh path is filtered, and floored.** `AvatarPlanCache.ShowsAtOnce` only raises `Changed`
  on the same frame for a changed `AvatarDoc`, a `VRCAvatarDescriptor`, or a GameObject that carries an `AvatarDoc`;
  every other change under an avatar (a renderer, a component) still marks that avatar's plan stale but waits for
  the existing 250 ms poller. Since plan 13 task D a transform marks nothing (no plan reads a position), a material
  or a mesh marks stale only the plans whose property or shape targets read it, and a hierarchy change only an
  avatar that gained or lost a transform. A rename or a reorder inside an avatar reaches the plan through the
  GameObject property, `ChangeChildrenOrder` or `ChangeGameObjectParent` event the editor publishes; one done by a
  script without Undo publishes none and is not seen until the next drop. `AvatarDoctorView.ScheduleRefresh` has a
  100 ms floor (`RefreshFloorMs`): a refresh asked for inside that window of the last one waits out the remainder,
  so a burst of changes cannot cost more than ten refreshes a second.
- **`AvatarDoc.removedLayers` holds a layer name and the id of the edit that hid it.** The first pass stored a
  plain `List<string>` and `Restore` re-derived the hiding edit by rescanning the current FX controller, which
  would silently do the wrong thing if the FX asset or the original control changed after the import. It is now
  `List<RemovedLayer> { layer, editId }`; `ImportApply.RestoreLayer` looks the edit up by `editId` directly and
  never rescans.
- **A hand made FX layer is a strict, narrow candidate.** Beyond "one parameter, one on state, a plain clip",
  `FxLayerImport` also refuses a layer whose parameter drives more than one destination state or is named by
  another menu control (including a puppet's sub parameters) or by animator layer control anywhere on the avatar,
  a layer with state behaviours, a masked or zero-weight layer, and a transition animation between the off and on
  states. Sub state machine destinations count toward the "more than one destination" check the same as a plain
  state.
- **A VRCFury toggle always imports as synced.** VRCFury's own `Toggle` model has no per-toggle synced field: every
  toggle it builds is synced. `VrcFuryImport.Build` never sets `draft.synced`, leaving `MenuEntry`'s own
  `synced = true` default, with a code comment recording why.
- **A Modular Avatar submenu-type menu item is a path segment, not a candidate.** `ModularAvatarImport.Scan` skips
  `ControlType.SubMenu` entirely; it has no reactive components of its own to lose, so only what is nested under it
  can become an import candidate.
- **Importing a Modular Avatar toggle also removes its own, otherwise-empty menu installer.** When
  `ModularAvatarMenuInstaller.menuToAppend` is null, the installer exists only to place this one item, so
  `Remove originals` deletes it alongside the item; an installer that also carries its own menu asset is left
  alone.
- **A preset pools every control by its bound parameter name, not only a real exclusive group.**
  `PresetBuilder.Operations` pools every non-radial state by `binding.Parameter.Name`; two toggles that are not in
  a group but happen to share a custom parameter name conflict the same way two members of one exclusive group do,
  raising `ad.error.preset_group_conflict` with the parameter name in place of a group name.
- **The model, not only the build, raises `preset_target_missing`.** `MenuModel.ValidatePreset` now also warns
  once per state whose `entryId` no longer resolves through `tree.FindControl`, matching what
  `PresetBuilder.Operations` already raised only at real build time; deleting a control referenced by a preset now
  shows a warning on the preset's own tree row without pressing Play or uploading.

### Phase 7 final wave amendments (2026-09-19)

The whole-phase review found one Critical and eleven Important cross task defects. Fixing them changed the
behaviour the sections above describe in these places.

- **A late message never carries the component.** `MenuEditApplier` runs after the first pass has destroyed every
  AvatarDoc, so passing the component to `ad.warn.edit_source_missing` or `ad.error.edit_cycle` made NDMF throw
  (`ObjectRegistry.GetReference` returns null for a destroyed object) and failed the upload. `PendingEdit` now
  carries the component's object name and, where NDMF offers one, an `ObjectReference` taken while the component
  was alive; the late messages use that.
- **The late applier resolves first, then applies.** Every pending edit is located on the fresh clone before any
  edit is applied, and the index is taken with `parent.controls.IndexOf(control)` at apply time. A rename can no
  longer move what a later hide resolves to, and hiding occurrence 0 no longer shifts occurrence 1.
- **Overflow pages are transparent when locating.** A SubMenu control with an empty parameter named `More`
  (Modular Avatar, AvatarDoctor) or `Next` (VRCFury) is descended into without consuming a path segment, at every
  level, so an edit still finds its item on a crowded avatar.
- **A move into a submenu that does not exist yet waits for the last pass.** `MenuModel.Build` takes an explicit
  `MenuEditMode`: `Build` defers such a move (it stays pending), `Inspector` creates the submenu so the tree can
  show where the item is going. In the late passes only the final one creates missing submenus. Without this, a
  move into a Modular Avatar submenu made a second submenu of that name beside the one Modular Avatar installs.
- **An FX layer import cannot be applied twice, and layer names must be unique.** `FxLayerImport.Scan` marks a
  candidate unsupported when its control is hidden by a menu edit or its layer is already listed in some
  AvatarDoc's `removedLayers` (`ad.import.reason.already_converted`), and when the layer's name is not unique in
  the FX controller (`ad.import.reason.layer_name_not_unique`). `ImportApply` refuses a layer already listed.
  `LayerRemover` removes only when exactly one layer carries the name and otherwise warns with
  `ad.warn.removed_layer_ambiguous`.
- **Orphaned menu edits are visible and removable, and a conversion carries them over.** The header lists every
  pending edit as `ad.warn.edit_source_missing` with a `Remove` button (`HeaderView.RemoveOrphanEdit`). An import
  with `Remove originals` takes the matching edit's name and icon into the draft entry, uses its target path as
  the entry's place, drops its `hide`, and deletes the edit in the same Undo group.
- **Prefab overrides are recorded after every write.** `ImportApply` marks the component changed once after the
  loop, so the hide edit and the `removedLayers` row are kept as prefab instance overrides.
- **Property targets see the material a slot swap brings in.** `MenuModel.PropertyExists` and the base value also
  accept a property carried by a material a material target of the same entry puts on that renderer (the dissolve
  case), and the base value then comes from that material. When the renderer's materials disagree on the current
  value, `ad.warn.property_differs` says the Off state writes the first one to all of them.
- **A radial's generated ramp is linear and its merged clip never loops.** The motion time clip uses
  `AnimationCurve.Linear`, matching the blend tree radial, and `loopTime` is forced false whatever the custom
  clips say, since a looping clip wraps at 1.0 and would snap the radial back to its start at 100 percent.
- **Override reporting belongs to the final run.** `ad.warn.override_unused` and `ad.info.override_applied` are
  said once, in the final run only, which is also the run whose count is complete; `ad.warn.override_target_gone`
  is skipped in the second run for a rule the first run pinned. When the final run never happens (the phase was
  skipped) those two lines are simply not said. The Optimizing sequence declares
  `BeforePlugin("com.anatawa12.avatar-optimizer")` and no longer requires `AnimatorServicesContext`: the pin pass
  asks for it inside the pass, and only when rules exist, so an avatar that uses nothing of AvatarDoctor's does
  not have its final controllers round tripped.
- **An entry submenu merges into an existing submenu of the same name.** A `Submenu` entry whose plain name equals
  an existing (non generated, non foreign) submenu under the same parent becomes that node instead of a second one
  beside it, reporting `ad.info.same_submenu_merged`. The entry keeps its own row (selectable, renamable) and its
  rich name and icon reach the merged control only where it sets them.
- **An existing submenu's node keeps its plain name.** `MenuModel.ReadExisting` puts the plain name in
  `Submenu.Name` and the raw text in `RichName`, so a styled existing submenu is a usable install and move target
  instead of putting markup into every path.
- **Row keys tell duplicates apart.** `TreeRow.KeyFor` appends the occurrence index when more than one item under
  the same parent shares an original path, so a refresh no longer snaps the selection to the first duplicate.
- **A toggle that is on by default starts in its On state.** `AnimatorBuilder` sets the layer's default state from
  `binding.OnByDefault`, so a blend time does not fade the toggle in at every avatar load. With Write Defaults the
  first frame is the same either way.
- **A build that stops on an error strips no layers.** `LayerRemover.Run` moved after the model and plan error
  check, so a failed build leaves the FX controller exactly as it found it.
- **A rename is not cosmetic when a preset names the entry.** Preset panels label their states by the control's
  path, so that rename rebuilds the plan like any other.
- **Duplicate and EnsureUniqueIds remap `presetStates[].entryId`.** A preset copied with the controls it names
  drives the copies, not the originals.
- **Reads of other tools are cheaper.** Every `SerializedObject` over a VRCFury component is disposed, and the
  walk that finds those components is cached per avatar root beside the provided parameter cache and dropped with
  it. The Import window rescans on `Undo.undoRedoPerformed` and on `AvatarPlanCache.Changed`.
- **`DOCME_MA` floor raised to 1.10.0** in both asmdefs, the oldest Modular Avatar version with the reactive
  components the code compiles against. The installed changelog does not reach back far enough to narrow it
  further.
- **Look**: the header shows one memory bar again. The compact duplicate under the title bar is gone; the title
  bar keeps the name and the free bits text, and the detailed bar with its legend stays where it was.
- **Play mode, checked not fixed**: NDMF skips the Optimizing phase only when VRCFury invokes NDMF itself
  (`AvatarProcessor.IsVRCFuryHack`, which looks for `VF.Menu.NdmfFirstMenuItem`). The installed VRCFury (1.1407.0)
  does not do that: its play mode trigger calls `VRCBuildPipelineCallbacks.OnPreprocessAvatar`, so NDMF's own
  `BuildFrameworkOptimizeHook` (callback order -1025) still runs after VRCFury (-10000). Late edits on VRCFury
  items and the second pin therefore do run in play mode here.

### Phase 7 follow-up round (2026-09-19)

The scoped re-review of the wave above found defects in the wave itself. These further changes belong with it.

- **Overflow page transparency is a fallback, not the rule.** Every submenu control has an empty parameter, so a
  creator's own submenu named `More` or `Next` looked like a page. `MenuEditApplier` now locates a source
  STRICTLY first, with the pages counted like any other submenu, which is exactly how the model numbers its items;
  only when that finds nothing is the walk retried with pages transparent. The same two step rule applies to move
  target paths (`PathExists`, `Reaches`, `FindOrCreateSubmenu`), so a target submenu another tool pushed onto an
  overflow page is found there instead of being created a second time one level up.
- **The final pass closes the animator services it opened.** `AvatarDoctorPlugin.FinishAfterOtherTools` activates
  `AnimatorServicesContext` recursively (plain activation does not bring its `VirtualControllerContext`
  dependency) and deactivates both in a `finally`. Deactivation commits the virtual controllers back to the
  descriptor, so an optimizer that runs later in `Optimizing` and knows nothing of the animator services reads the
  real ones.
- **A conversion matches its edit by origin.** `ImportApply` matched an edit by plain path plus
  `OriginOccurrence`, which only an FX candidate sets, so converting a foreign item whose path equalled an
  existing control's path carried over and deleted that control's edit. A foreign candidate is now matched to its
  row through the component that installs it, and the occurrence is taken the way `MenuEditing.OccurrenceOf`
  takes it.
- **Duplicate follows a trigger's source toggle too.** `EnsureUniqueIds` already remapped `sourceToggleId`;
  `EntryEditing.Duplicate` now does as well, through the shared `RemapLinks`.
- **`ad.info.same_submenu_merged` reads for both merges**: `"{0}" joins the submenu of the same name that is
  already in the menu. ({1})`.
- **A default on toggle's pin layer starts on** as well as its main layer. A default on toggle that is also saved
  and was left off fades out once while the avatar loads, which the README now says.
- **An override whose renderer disappears between the runs still reports what it pinned.** `OverrideState.Pinned`
  keeps the count of the last run that pinned anything, so the final run says `ad.info.override_applied` with it
  instead of falling silent.
- **The orphan edit box shows only the inspected component's edits**, so two AvatarDocs on one avatar do not both
  offer the same warning and the same button.

## Phase 8: test mode on the wheel, contact and PhysBone triggers, outfit sets (directed 2026-09-19)

The user picked five ideas from a list of community requests: testing controls from the wheel without play mode,
touch triggers ("boop the nose to toggle blush") built on the trigger setup that already exists, outfit sets, a
platform choice that keeps PC and Quest parameter lists identical, and a summary with a lock for prefab makers.

### Test mode on the wheel

The wheel gets a `Test` switch (off by default, remembered in EditorPrefs with the other wheel preferences). While
it is off the wheel behaves as before: a wedge click selects the row. While it is on, a wedge click drives the scene
preview through `PreviewState` instead, exactly as the preview row of the panel does, and changes no data:

- Toggle: flips between on and off (the effective state: its override when it has one, else its default). Members of
  an exclusive group follow the rule the preview already uses: switching one on switches the others of the group off.
- Preset: applies the preset (`PreviewState.ApplyPreset`).
- Radial: selects the wedge and shows a 0 to 1 slider under the wheel that writes the radial's override while it is
  dragged; a second click closes the slider.
- Submenu and the back wedge navigate as before; existing and foreign wedges do nothing in test mode (they have no
  preview).
- Wedges show their effective state: an on toggle and an applied radial above zero carry the accent fill, a radial
  shows its value as a thin arc.
- Under the wheel a strip `Triggers` lists every trigger of the avatar that does not depend on a toggle or blend shape
  alone (sources Parameter, Contact, PhysBone) as small press buttons, so a touch can be simulated: a press sets the
  trigger's preview override to on, a second press clears it. A trigger in `Flip a toggle` mode flips that toggle's
  preview state on each press instead.
- `Reset` clears every preview override (`PreviewState.ClearAll`). Leaving test mode does not clear them, so the
  creator can look at a state while editing; the header's existing preview indicator shows that overrides are active.

### Contact and PhysBone sources for triggers

`TriggerSource` gains `Contact = 3` and `PhysBone = 4`. Both reuse everything a trigger already has (effects held
while the condition is true, blend shape links, the rule group in the tree, messages, preview override) and add:

**Contact.** Fields on `MenuEntry`: `contactAnchor : Transform` (the object the receiver follows, usually a bone),
`contactOffset : Vector3`, `contactRadius : float` (0.05 by default, 0.005 to 1), `contactTags : List<string>`
(`Finger` and `Hand` by default; VRChat's standard tags are offered as check boxes, a text field adds custom ones),
`contactAllowSelf : bool` (true), `contactAllowOthers : bool` (true), `contactProximity : float` (0 by default:
touch; above 0: the condition is "closer than" using a Proximity receiver, the value being the fraction 0 to 1 that
VRChat reports). The build adds a `VRCContactReceiver` to the avatar clone on a new child object of the anchor
(`AD Contact <plain name>`), sphere shape, `localOnly = false`, receiver type `Constant` (or `Proximity`), with a
generated parameter `<prefix>/Contact/<plain name>` that is animator only (not in the expression parameters: contacts
are evaluated on every client, so nothing needs syncing in hold mode). Nothing is added to the scene. A missing
anchor is `ad.error.contact_no_anchor`; an anchor outside the avatar `ad.warn.target_outside_avatar`; no tags
`ad.error.contact_no_tags`; neither self nor others `ad.error.contact_nobody`.

**PhysBone.** Fields: `physBone : VRCPhysBone` (a component on the avatar), `physBoneEvent : PhysBoneEvent`
(`Grabbed = 0`, `Posed = 1`, `AngleAbove = 2`, `StretchAbove = 3`, `SquishAbove = 4`) and `physBoneThreshold : float`
(0.5, for the three float events). The condition reads the PhysBone's parameter with the matching suffix
(`_IsGrabbed`, `_IsPosed`, `_Angle`, `_Stretch`, `_Squish`). When the PhysBone has no parameter, the build gives the
clone's component one (`<prefix>/PB/<plain name>`); the scene component is never changed. When several triggers name
the same PhysBone they share its parameter. A missing PhysBone is `ad.error.physbone_missing`; one outside the avatar
the usual warning. PhysBone parameters are local to each client like contacts, so nothing is synced in hold mode.

**Mode.** `touchMode : TouchMode` for both sources: `Hold = 0` (the trigger's own effects apply while the condition
is true, which is what triggers do today) and `FlipToggle = 1` (each new touch flips an AvatarDoctor toggle:
`flipToggleId`; the trigger has no effects of its own and its target lists are hidden). Flip is built as one layer
`AD: <plain name> (flip)`: `Idle`; on touch while the toggle is off go to `Set on`, on touch while it is on go to
`Set off`; both carry a `VRCAvatarParameterDriver` with `localOnly = true` that sets the toggle's parameter to its
binding value or to 0 (a group member sets the group's Int to its value, or to 0); both return to `Idle` when the
touch ends. Because the driver runs only on the wearer's client and writes the toggle's own synced parameter, the
menu, the saved state and other players all stay in step. For `FlipToggle` with a contact, `contactAllowOthers`
decides whether other players can flip it. A flip target that is missing, is not a toggle, or is the member of a
group without an off state is `ad.error.flip_target`. Presets and triggers cannot be flip targets.

**Preview and test mode.** A contact or PhysBone trigger has the same preview override as every trigger; in `Flip`
mode a simulated touch flips the target toggle's override.

**Inspector.** The trigger panel's source choice gains `Contact` and `PhysBone`. Contact: `Anchor` (object field
with a `Pick bone` dropdown of the humanoid bones: Head, Chest, Hips, hands, feet), `Radius`, `Offset`, `Touched by`
(tag check boxes in two columns plus a custom field), `Self` and `Others` switches, `Distance` (touch, or closer
than a slider). PhysBone: a field that accepts a `VRCPhysBone` with a dropdown listing the avatar's PhysBones by
object name, the event choice, the threshold slider for float events. Both: `Mode` (`Hold effects` or `Flip a
toggle`) and, for flip, the toggle picker used by toggle sourced triggers. While a contact trigger is selected, the
Scene view shows the sphere at anchor plus offset in the accent colour with a position handle and a radius handle;
moving them writes `contactOffset` and `contactRadius` with Undo. Nothing is drawn when the component is disabled or
another row is selected.

### Outfit sets

A wizard (`+ Outfit set` in the toolbar) that produces ordinary entries in one Undo step and adds no runtime data of
its own. The creator names the set, adds outfits (a name plus the pieces, dropped from the Hierarchy) and picks a
mode:

- `One at a time`: a submenu named after the set with one toggle per outfit, all in an exclusive group named after
  the set, the default outfit on by default, each toggle showing its pieces and hiding the pieces listed under
  `Hidden while any outfit is on`; an optional `None` toggle shows nothing.
- `Mix freely`: a submenu with one toggle per piece (on by default for the pieces of the default outfit), one preset
  per outfit that switches its pieces on and every other piece of the set off, and an `Everything off` preset.

Shrink suggestions: for each piece the wizard lists blend shapes on the avatar's other skinned meshes whose name
matches the piece by name (lower case, letters and digits only, the prefixes `shrink`, `hide`, `off`, `kisekae` and
`cloth` removed; one name containing the other; names shorter than three characters ignored). A ticked suggestion
becomes a blend shape target (weight 100) on the toggle that shows the piece, so skin under clothing is pulled in
while the piece is on. Icons: with `Capture icons` on, every created entry with pieces gets an icon through the
batch capture (`IconBatch.CaptureMissing`: Scene view frame when there is one, else a synthesized front view, always
fitted); the same batch is offered as `Capture missing icons` for the whole component.

### Platform choice (PC, Quest, both)

VRChat syncs expression parameters by their position in the list, so the PC and the Quest build of one avatar must
declare the same synced parameters in the same order. Creators usually duplicate the avatar for Quest; the lists
drift apart the moment entries are deleted on one copy. Instead of deleting, an entry is excluded:

- `AvatarDoc.platform : PlatformChoice` (`Both = 0`, `PCOnly = 1`, `MobileOnly = 2`) is the default for the
  component; `MenuEntry.platform : EntryPlatform` (`Inherit = 0`, `Both = 1`, `PCOnly = 2`, `MobileOnly = 3`)
  overrides it per entry. A submenu's choice applies to everything inside it unless an entry inside sets its own.
- The build target decides the platform: Android and iOS are mobile, everything else is PC
  (`EditorUserBuildSettings.activeBuildTarget`).
- An entry excluded on the current platform still plans and writes its parameter exactly as on the other platform
  (same name, type, default, saved, synced, same position), but gets no menu control, no animator layers, no scene
  defaults, no contact receiver and takes no menu slot. Its targets are not validated, because the pieces are
  usually missing on that copy. Rules (triggers, overrides) and presets that are excluded are skipped entirely
  apart from a preset's parameter.
- Parity check: when another avatar in the open scenes has the same blueprint id (`PipelineManager.blueprintId`,
  non empty) and its planned parameter list (names, types, synced flags, in order, AvatarDoctor's and the asset's)
  differs from this one's, the inspector reports `ad.warn.platform_parity` naming the first difference.
- Inspector: a `Platform` row in the header (`Both`, `PC only`, `Quest only` as a segmented choice) and in every
  entry panel (`Inherit` plus the three); rows excluded on the active build target are dimmed with a `PC only` or
  `Quest only` chip; the header shows which platform the editor is currently building for.

### Summary and lock for prefab makers

A maker who sells or shares a prefab with an AvatarDoc on it needs to tell buyers what it adds, and wants buyers not
to break it by accident. The header gets a `Summary` button. The summary covers that one component: its name, the
synced bits it adds (with the avatar's total for context), its parameters (name, type, synced, saved, default), its
menu as an indented tree with the install path and the slots used per submenu, what it needs (NDMF; Modular Avatar
or VRCFury only when it relies on a parameter they provide), how many contact receivers and PhysBones it uses, its
platform choice, and the messages it raises right now. Two formats: plain text with indentation and no markup, for
store pages, and Markdown with a parameter table; a `Copy` button puts the text on the clipboard. `Lock`
(`AvatarDoc.locked`, with an optional `lockNote`) makes the component read only in the inspector: the tree, the add
buttons, import, the outfit wizard and the entry panels are disabled, a banner explains why and offers `Unlock`;
install path, install mode, parameter prefix and platform stay editable because those are the buyer's settings.
Locking changes nothing at build.

### Not in phase 8

Capsule shaped contacts, contact senders (the tool only receives), proximity driving a blend shape continuously,
automatic conversion of an avatar for Quest (shaders, polygon limits), per platform values of one entry.

### Phase 8 amendments (2026-09-19)

Rulings and deviations made while building test mode, touch triggers, outfit sets, platform choice, and summary
and lock, amending the sections above:

- **Flip target rule dropped.** "or is the member of a group without an off state" is removed from the flip
  target rule under Contact and PhysBone sources: `ExclusiveGroup` has no field that can make a group lack an off
  state (every group's own Int can always be set to 0), so the clause was unreachable in the current data model
  and was never implemented.
- **`Pick bone` offers two more bones than listed.** The shortcut list is Head, Neck, Chest, Spine, Hips, both
  hands, both feet and both index fingertips (`HumanBodyBones.Neck`, `.Spine`, `.LeftIndexDistal` and
  `.RightIndexDistal` beyond the ones named above), read from the avatar's humanoid `Animator`.
- **The `Touched by` tag list is fixed.** `Finger`, `Hand`, `Head`, `Torso` and `Foot` are shown directly; `HandL`,
  `HandR`, `FootL`, `FootR`, `FingerL`, `FingerR`, `FingerIndex`, `FingerMiddle`, `FingerRing` and `FingerLittle`
  sit behind `More tags`; a text field adds any other tag as its own removable chip.
- **An excluded control keeps a `MenuItem`, flagged `Excluded`.** The writer, the page split, the slot count and
  the wheel all skip it exactly as they already skip a `Hidden` one; the row survives, dimmed, so the inspector
  can still show and edit it. Deciding exclusion once in the model and checking one flag everywhere else stays
  unchanged.
- **An excluded submenu survives when something inside it still builds.** A submenu item is dropped only when
  nothing under it survives and it never held anything of its own; a child that overrides back to `Both` keeps the
  submenu it needs to live in. The same rule protects an existing or a foreign submenu control: an AvatarDoctor
  submenu merged into one of those keeps that control's own `MenuItem`, so excluding or hiding the AvatarDoctor
  side never silently drops a control the tool did not create; a submenu whose only survivors are `Hidden` items
  still falls away.
- **`Hidden while any outfit is on` only applies in `One at a time` mode.** `Mix freely` has no single "any
  outfit is on" state for such a piece to hide behind, since every piece already has its own toggle; the wizard
  neither shows this list nor reads it in that mode.
- **The entry panel's `Platform` field is a popup, not a segmented choice.** `Inherit / Both / PC only / Quest
  only` does not fit the entry panel's width against its 112px label column at the default inspector width; the
  header's own three item `Platform` row stays a segmented choice, since it only ever needs three.
- **A toggle without effects that belongs to an exclusive group is a legitimate off choice.** `ad.warn.no_effects`
  no longer fires for a toggle with no targets when it has an `Exclusive group` set (an outfit set's `None` option
  is exactly this case); a toggle with no targets and no group still warns as before.
- **Icon capture after an outfit set is its own Undo step**, not folded into the set's own Undo group.
  `IconBatch.CaptureMissing` keeps its own named Undo group, since it is also the standalone `Capture missing
  icons` action; nesting it inside the wizard's group without changing that action's own contract was not
  practical, so undoing a freshly built outfit set leaves its icons in place and needs a second undo to remove
  them.
- **The lock guards every entry data write, not just the buttons that usually reach it.** A locked component
  refuses `Add`, `Duplicate`, `Delete`, `Reorder`/`Move to`, renaming a submenu, every menu edit
  (`SetHidden`/`SetName`/`SetIcon`/`MoveTo`/removing/resetting), import, the outfit wizard, capturing an icon
  (including from an already open window), and every preset and target list write, whichever window or scene
  handle tries it, not only the ones with a visible disabled state. `EntryEditing.EnsureUniqueIds` is the one
  deliberate exception: it is an automatic integrity fix for a duplicate id, not a maker or buyer edit, and runs
  unconditionally so a copied component never keeps a corrupt id while locked.
- **`Capture missing icons` stays enabled while locked.** It changes an icon field but no menu structure or
  parameter, and the lock's own brief never named it; `IconCaptureWindow`'s `Save icon` button likewise stays
  enabled and shows the refusal status on the click instead of pre-disabling itself.
- **The platform parity check compares only the enabled `AvatarDoc` components that would actually build**, on
  both the current avatar and its twin, walked in the same order `AvatarDoctorBuilder` itself uses. A disabled
  component never raises or clears the warning, and the two sides cannot drift from what the build order actually
  writes; the check leaves out `ParametersWriter.AddProvided` (NDMF's own contribution) on both sides, which only
  matters when another plugin's parameter name forces a rename.

### Phase 8 final wave amendments (2026-09-19)

Rulings made while fixing the whole-phase review, amending the sections above:

- **A touch source outside the avatar is an error, not a warning.** A contact anchor or a PhysBone that does not
  sit under the avatar being built raises `ad.error.touch_outside_avatar` instead of `ad.warn.target_outside_avatar`,
  and `TouchBuilder.Describe` leaves that trigger undescribed. Since `Create`, `TriggerPlanner.PlanTouch` and
  `BuildFlipLayers` all key off the described triggers, no receiver is ever parented under a real scene bone and
  no real scene PhysBone ever has its parameter written, whatever else the build does.
- **The preview writes an exclusive group as one unit.** `PreviewModel.Compute` and `DefaultsApplier.Apply` build
  a group at its first included member, every other member off first and the selected (or on by default) member
  last, exactly as `AnimatorBuilder.BuildGroupLayer` does. Written member by member in tree order instead, a
  member that is off would write its own off value over an object, shape, material slot or material property it
  shares with the winner.
- **An outfit set's exclusive group name is plain, non empty and unique.** `OutfitSetBuilder.Apply` takes
  `RichName.Strip(SetName).Trim()` for the group, falls back to `ad.outfit.default_name` when that is empty, and
  appends " 2", " 3" until no `exclusiveGroup` or `groups` entry on the avatar already uses it. The submenu keeps
  the styled name the creator typed. The window reports the adjusted name in its status.
- **One notion of "is this group member on".** `PreviewState.Effective(entry, tree, plan)` answers through the
  same `GroupWinners` rule `PreviewModel` uses, so the wheel, the trigger strip and the panel agree with the
  scene; the panel's `On` button clears a group member's siblings the way `Flip` and `ApplyPreset` do.
- **The parity warning cannot go stale.** Dropping one avatar's cached plan also drops the plans of the other
  copies of the same blueprint id, so fixing one copy replans the other rather than leaving a baked warning about
  a difference that is gone.
- **One definition of a flip trigger.** `MenuEntry.IsFlipTrigger` (kind Trigger, source Contact or PhysBone, mode
  FlipToggle) is what the model, the planner, `TouchBuilder`, the preview, the wheel and the panel all ask.
  `touchMode` is kept when the source changes, so a stale `FlipToggle` on a trigger of another source means
  nothing, and a flip trigger's leftover target lists are neither previewed nor reported as shared.
- **`flipToggleId` follows reassigned ids**, alongside `sourceToggleId` and preset states, in
  `EntryEditing.RemapLinks`.
- **The lock does not depend on the panel's disabled state.** `DirectFields.Write` refuses any path under
  `entries`, `groups`, `menuEdits` or `removedLayers` on a locked component; every public setter of the detail
  panel, the touch panel's distance handlers, the preset panel and the target lists ask first; the target lists
  hold their row values instead of binding them, so a refused write leaves the data alone and the row shows what
  the data holds; the detail panel applies the lock before it can defer a rebuild for focus; an import never
  edits a locked component's menu edits; and the header's orphan edit `Remove` button is not offered while locked.
- **Touch data is repaired at build.** `Describe` clamps a contact radius to 0.005..1 and reads a threshold of 0
  on a float PhysBone event as 0.5, so data saved before those fields had their ranges cannot produce a dead
  contact or a trigger that fires on the slightest movement. Touch parameter names are also made unique against
  the plan's own names, not only against the avatar's existing ones.
- **A flip trigger whose toggle is not built here gets no receiver**, and the test mode trigger strip leaves out
  the triggers this platform does not build.
- **A PhysBone renamed by Modular Avatar is reported, not predicted.** Under `DOCME_MA`, a PhysBone whose prefix a
  `ModularAvatarParameters` at or above it lists as internal or renamed raises `ad.warn.physbone_renamed`;
  Modular Avatar's generated name is never guessed.
- **A group whose default member is excluded here reports `ad.info.group_default_excluded`**: the Int selects a
  value no state carries on this platform, so the group's Off state is what shows.
- **The summary counts every contact and PhysBone source** the component holds, whichever platform builds it, and
  names the platform limited ones the way the menu tree already marks them.
- **Preview overrides are global to the editor session.** Two copies of one avatar share them and a script reload
  clears them; documented rather than changed.

## Phase 9: merged animators and converting animator driven menus (directed 2026-09-19)

The user tried to convert a Modular Avatar submenu (`Shading Menu`: a Menu Installer, a submenu Menu Item with six
child items, a Parameters component and a Merge Animator with the controller `Shading_FX`) and nothing could be
converted, because the items' effects live in the merged controller, not in object, shape or material components.
Their request: "I want to be able to merge animators as well. So we can convert that shading menu. Or if you can find
another way to merge it." This phase does both: AvatarDoctor can merge an animator controller itself, and the
converter turns each simple layer of such a controller into a real entry, keeping the rest merged.

Amendment (2026-09-19): the visible name is AvatarDoc from here on. The text below still says AvatarDoctor where it
already did; it is left as it was written.

### Verified facts

- `Shading_FX` has six layers: three floats on 1D blend trees of constant clips (Min Brightness, Grayscale, Hair
  Shine) and three bools on two state layers (LTCGI, Oily Skin, SSAO), all animating material properties. Its
  Merge Animator uses layer type FX, relative path mode, match avatar write defaults; the Parameters component
  declares the six parameters (synced, saved, with defaults).
- NDMF's animator services clone a controller into a `VirtualAnimatorController` whose layers can be added to the
  virtual FX controller, with clip paths editable on the virtual clips: this is how Modular Avatar merges.

### Merged animators

`AvatarDoc.animators : List<AnimatorMerge>`:

| Field | Meaning |
|---|---|
| `id` | GUID string |
| `controller : RuntimeAnimatorController` | the controller whose layers join the avatar's FX controller (an override controller is resolved to its clips) |
| `pathRoot : Transform` | empty: clip paths start at the avatar root; else every path is prefixed with the path from the avatar root to this object (what Modular Avatar calls relative path mode) |
| `matchWriteDefaults : bool` (true) | states take the avatar's Write Defaults decision; a layer that is a single state with a direct blend tree keeps Write Defaults on |
| `skipLayers : List<string>` | layer names left out of the merge (the converter lists the layers it turned into entries) |
| `platform : EntryPlatform` | as for entries; an excluded merge adds no layers, and its parameters still come from the entries that declare them, so the parameter list stays identical on both platforms |

Build: after AvatarDoctor's own layers, each enabled merge clones its controller through the animator services,
drops skipped layers, rewrites paths for `pathRoot`, harmonises Write Defaults, and appends the layers to the FX
controller with unique names; its animator parameters are added to the FX controller when missing (a type clash with
an existing parameter is `ad.error.merge_parameter_type`; see "Phase 9 amendments" below, the merged type wins where
nothing but AvatarDoc's own parameter controls or declarations put the name there). The controller asset is never modified. A path root
outside the avatar is `ad.error.merge_outside_avatar` and the merge is skipped (nothing outside the build clone is
ever touched). A missing controller is `ad.warn.merge_no_controller`. A skipped layer name that the controller does
not have is `ad.warn.merge_skip_missing`. Layer control behaviours inside a merged controller refer to layers by
index: the merge remaps indices that point INTO the merged controller to the layer's final index, and when layers
are skipped while the controller uses layer control the merge refuses with `ad.error.merge_layer_control`.

Synced parameters are declared by menu entries, as everywhere else. For that, a toggle, radial or button style
entry may be a **parameter control**: it has a custom parameter name and no targets. It gets a menu control and a
planned parameter but no layer, and it does not warn about having no effects when a merged animator of the avatar
(or the avatar's own FX controller) uses that parameter; otherwise `ad.warn.parameter_control_unused` says the
parameter drives nothing. Parameters a merged controller needs but no menu entry declares stay animator only
(unsynced), which the merge row's panel lists so the creator can see them.

Inspector: an `Animators` group row in the tree (like `Triggers` and `Overrides`), one row per merge, `+ Animator`
in the toolbar. Panel: controller field, `Path root` with the same tooltip as custom animations, `Match write
defaults`, `Platform`, the layers as a check list (`skipLayers` is the unchecked ones), and a read only list of the
controller's parameters with who declares each (the entry's name, `existing on the avatar`, or `not synced`). No
scene preview: the panel says so. The summary lists merged animators; the lock guards them.

### Converting animator driven menus

The converter learns Modular Avatar menu items whose effect comes from a `ModularAvatarMergeAnimator` on the item's
object or above it (layer type FX only). For each such item (Toggle, Button or Radial Puppet) it reads the control's
parameter, finds the layer of that controller that uses it, and tries a **real conversion** with the same strict
rules as the hand made FX layer import, extended by one shape:

- Two state layer on a Bool or Int parameter with a constant clip: a Toggle entry with object, blend shape, material
  slot and material property targets (existing rules). A target may carry an explicit off value and the layer's
  default state may hold the on clip instead of the off one; see "Phase 9 amendments" below, both corrections to
  this bullet.
- NEW: one state with a 1D blend tree on a Float parameter whose children are constant clips: a Radial entry. Two
  children: their values are the radial's lower and upper end. More than two children, children that animate
  different bindings, non constant curves, nested trees or a threshold range other than 0 to 1: not convertible.
- Paths in the clips are resolved with the Merge Animator's path mode (relative: from its object or its
  `relativePathRoot`; absolute: from the avatar root). A path that does not resolve is a note and that curve is
  skipped; nothing importable left means not convertible.
- Defaults, saved and synced come from the `ModularAvatarParameters` entry of that parameter when there is one. The
  new entry gets its OWN parameter (the old name is not reused, since the old layer goes away with it), unless the
  parameter is also used elsewhere in the controller, which makes the item not convertible. See the amendment on
  undeclared parameters below: with no declaration, saved, synced and the default follow the menu item's own flags,
  not a forced unsynced state.

An item that is not convertible is still importable as a **parameter control**: same name, icon, kind, parameter
name, value, default, saved and synced, no targets. The candidate's notes say which of the two it became and why.
An item under a shared Merge Animator that cannot become an entry at all, such as a two axis radial puppet, blocks
the conversion of the whole group instead of being left behind on its own (see amendments). One more candidate
stands for the controller itself: an `AnimatorMerge` with the controller, the path root that matches the Merge
Animator's path mode, `matchWriteDefaults`, and `skipLayers` holding every layer that was really converted. When
every layer was converted the merge candidate is left out entirely. Choosing any item of such a controller selects
the merge candidate with it (they belong together): the whole set is one group that applies all or nothing (see
amendments), and `Remove originals` removes the menu items, their installer when it installs nothing else, the
Merge Animator and the Parameters component, in one Undo group; the controller asset and its clips are never
touched. Parameters that Modular Avatar would have renamed (internal or remapped) make the whole controller not
convertible (`ad.import.reason.ma_renamed_parameters`), because the names inside the controller would no longer
match what gets declared.

The right click `Convert to AvatarDoctor` on the submenu row converts the whole set. VRCFury full controllers are
not covered in this phase.

### Remove from avatar (asked 2026-09-19)

The user: "if I remove something from the originals it should also free up the parameters/synced values if its not
used anywhere else." Hiding an existing control only takes it out of the menu; its expression parameter keeps
costing synced bits and its animator layers keep running. `MenuEdit.remove : bool` goes further, still without
touching any asset: at build the control is left out (as with `hide`), and for every parameter that control used
(its parameter and its sub parameters) AvatarDoctor decides, with the strictness of the layer import, what else can
go:

- Layers: an FX layer goes when every transition condition in it reads only parameters of removed controls, it has
  no state behaviours, no mask, and the avatar uses no animator layer control (removing a layer shifts the
  positions that layer control points at; then no layer is removed and the parameter stays, not only the layer: see
  amendments). Layers are removed from the FX clone by the existing `LayerRemover` path, never from the controller
  asset. The plan itself is made in a dedicated build pass before the layers are taken off the descriptor for
  building; see the amendment on the Resolving phase below, since a planner reading the descriptor any later sees no
  layers at all.
- The parameter leaves the expression parameters, which frees its synced bits, only when nothing else uses it: no
  other menu control that survives (the avatar's own menu after edits, AvatarDoctor entries that reuse it by name,
  presets and triggers that name it), no remaining layer of ANY playable layer controller of the descriptor
  (conditions, blend tree parameters, motion time, speed, mirror, cycle offset, parameter drivers), no contact
  receiver or PhysBone on the avatar that writes it, and it is not one of VRChat's built in parameters. Otherwise it
  stays, and the inspector says what keeps it. The shared reader walk this reuses also covers several other tools;
  see the `ParameterUsage` amendment below for the full list.
- Items that Modular Avatar or VRCFury add cannot be removed this way (their parameters and layers do not exist
  yet when AvatarDoctor builds): those rows offer `Hide` and `Convert`, and converting with `Remove originals`
  already frees what the other tool would have declared.
- Inspector: the `Existing item` panel gets `Remove from avatar` under `Hidden` (choosing it implies hidden), with a
  result line computed from the plan: `Frees {0} bits and {1} layers` or `Keeps "{0}": {1}` per parameter. The row
  shows a `removed` chip. The header's memory bar counts the freed bits, so the creator sees the budget change at
  once. The PC and Quest parity check sees the shorter list like any other difference: `PlatformParity.ListOf` plans
  the removal itself and leaves every freed name out of the asset half of its list.
- Messages at build: `ad.info.removed_parameter` per freed parameter, `ad.info.removed_kept` with the reason per
  parameter that had to stay, `ad.warn.removed_layer_unsafe` (existing key) when layer control blocks layer removal.

### A menu switch for triggers (asked 2026-09-19)

The user: "There should also be a way to toggle the triggers/contacts and such." A trigger gets a gate:
`MenuEntry.gateToggleId : string` and `gateToggleOn : bool` (true). While the gate is set, the trigger only works
while that AvatarDoctor toggle is in the chosen state; an empty id means always. It applies to every trigger source
(toggle, parameter, blend shape, contact, PhysBone) and to both touch modes.

- Build: the gate is one more condition on the trigger. Hold triggers: every transition into the effect state also
  requires the gate, and the effect state is left as soon as the gate is no longer met. Flip triggers: `Idle` only
  reacts to a touch while the gate is met. Blend shape links (clip rewriting) cannot be gated by a parameter, so a
  gate on a blend shape sourced trigger is `ad.error.gate_on_link` and the panel does not offer it there (plan 14
  task S lifted this: a blend shape trigger and a Fade contact read the gate as one more requirement, and the key is
  gone). The gate
  reads the toggle's own parameter the way toggle sourced triggers already do (Bool, custom value, exclusive group
  member), so it syncs with the menu and needs no parameter of its own. A contact receiver stays on the avatar while
  the gate is off; only its effect is ignored.
- Validation: a gate that names a missing entry, a non toggle, a preset or the trigger's own flip target is
  `ad.error.gate_target`. A gate toggle that is excluded on the current platform still works, because its parameter
  exists on both platforms.
- Inspector: the trigger panel gets `Only while` with the toggle picker used elsewhere (first choice `(always)`) and
  an `On` or `Off` segmented choice, plus `Add a menu switch`: it creates a toggle named `<trigger name>` in the
  trigger's component (on by default, saved, synced, no targets: a legitimate switch, so it raises no
  `no_effects` warning while a trigger uses it as its gate), selects it as the gate, and reveals it. Test mode and
  the preview respect the gate: a trigger whose gate is not met in the preview does nothing when pressed, and its
  strip button is dimmed.
- `EntryEditing.RemapLinks` follows `gateToggleId` like the other id links; the summary lists the gate; the lock
  guards it.

### Pickers show only the current avatar (asked 2026-09-19)

The user: "When I filter/search for phys bones or item to toggle it should only show the objects on my current avatar
and its children not the whole scene." Unity's object picker lists every object of every open scene and cannot be
scoped to a hierarchy. Every field of the inspector that refers to something ON the avatar therefore opens
AvatarDoctor's own picker instead: object targets, blend shape, material and property renderers, the trigger's
source renderer, a contact's anchor, a trigger's PhysBone, the path root of custom animations and merged animators,
and the pieces of the outfit wizard.

- `AvatarObjectPicker`: a searchable dropdown (Unity's `AdvancedDropdown`, which brings a search box and keyboard
  navigation) listing the avatar's hierarchy as a tree, filtered to what the field accepts: any GameObject, objects
  with a `Renderer`, a `SkinnedMeshRenderer` (blend shape fields list only meshes that have blend shapes), a
  `VRCPhysBone`, or any Transform (anchors and path roots, with the humanoid bones offered first under `Bones`).
  Inactive objects are listed and marked; objects of other avatars, other scenes and assets never appear. Entries
  show the object name with its parent path in parentheses on the same line ("Name (Parent/Path)"), not on a
  genuinely dimmed second line as first drafted here, since `AdvancedDropdownItem` has no supported way to render
  two tones of text in one row (see amendments); searching matches the name and the path.
- The field keeps being a normal object field for display, drag and drop from the Hierarchy and ping on click. Only
  the small selector button at its right end is taken over: a click opens the avatar picker instead of Unity's
  scene wide one. Unity 2022.3 opens its own picker from the default action of a pointer or mouse down on that
  selector, and from a key down on the field itself, so the picker has to claim both (stop propagation, prevent the
  default action) or Unity's own picker would still open alongside it; see amendments. A drop of an object from
  another avatar is refused with the existing outside avatar message rather than accepted and warned about later.
- Asset fields (icons, materials, animation clips, animator controllers) keep Unity's own picker: assets are not
  part of the avatar.
- The PhysBone dropdown and `Pick bone` added in phase 8 become this one picker, so there is one way to pick.

### Triggers with several conditions (asked 2026-09-19)

The user: "for triggers there should be a way to make more cases when X or Y happens then or When X and Y or A and X
happens". The "when" of a trigger becomes a list of groups: the trigger is met when ANY group is met, and a group is
met when ALL of its conditions are. That is "(X and Y) or (A and X)" written as two groups. It maps directly onto an
animator: the conditions of one transition are anded, several transitions are ored.

- Data: `MenuEntry.when : List<ConditionGroup>`, `ConditionGroup { List<WhenCondition> all }`, and
  `WhenCondition` carries a `kind` (`Toggle`, `Parameter`, `Contact`, `PhysBone`) plus exactly the fields the flat
  single source fields have today for that kind (toggle id and state; parameter, comparison and value; the contact
  settings; the PhysBone, event and threshold). The type is named `WhenCondition` (its enum `WhenConditionKind`), not
  `TriggerCondition` as earlier drafts of this section said: the editor already had a class of that name; see the
  amendments below. While `when` is empty the trigger works from its flat fields exactly
  as before, so existing data needs no migration; the inspector moves the flat condition into the first group the
  first time a second condition is added. Blend shape links rewrite clips instead of using conditions and cannot be
  combined: a blend shape sourced trigger has no groups. (Plan 14 task S: a blend shape trigger's group now holds
  its requirements, while its shape stays in the flat fields.)
- Build: one enter transition per group carrying all its conditions (plus the gate of the menu switch on every one).
  Leaving needs the negation: the trigger is no longer met when EVERY group has at least one failed condition, which
  is one leave transition per combination of one negated condition from each group (plus one per failed gate
  condition), with a subsumed transition (one whose conditions are a superset of another kept one) dropped. Negation:
  `If` and `IfNot`, `Equals` and `NotEqual`, `Greater v` and `Less v` for a Float (see the amendment below: no step),
  and `Greater v` and `Less v + 1` for an Int. Sizes are capped so the layer stays small: at most 4 groups, 4
  conditions per group, and 64 leave transitions, checked on the expanded alternatives before any product between
  them is built (see amendments); beyond the cap `ad.error.trigger_too_complex` names the counts.
  Flip triggers use the same met and not met transitions for "touched" and "released". Every contact condition gets
  its own receiver on the build clone and every PhysBone condition resolves its parameter as today; conditions that
  name the same PhysBone or the same contact settings share theirs.
- Validation per condition as for the single source today (missing toggle, unknown parameter, contact without an
  anchor, outside the avatar, and so on), named by group and position. A group with no conditions is ignored with a
  warning; a trigger whose groups are all empty is `ad.warn.trigger_no_condition`.
- Inspector: the `When` section of the trigger panel shows the groups as cards captioned `Any of these` between them
  and `All of` inside: each condition is one compact row (kind choice, then the fields of that kind; contact and
  PhysBone rows expand their settings below when selected), `+ And` adds a condition to a group, `+ Or` adds a group,
  each has a remove button. Above the cards one sentence spells the whole rule out in words ("When (Hat is on and
  Nose is touched) or (GestureLeft equals 3)"), which is also what the summary prints. The scene handle edits the
  contact condition that is selected. Test mode and the preview keep treating a trigger as one thing that is
  pressed on or off.
- The lock, `RemapLinks` (toggle ids inside conditions), the platform rules (an excluded trigger builds nothing, a
  condition on an excluded toggle still works because the parameter exists) and the avatar scoped pickers apply to
  every condition.

### Toggles hidden from the menu (asked 2026-09-19)

The user: "maybe we can hide a toggle from the menu but use it as reference for triggers and such as well."
`MenuEntry.hiddenFromMenu : bool` on toggles and radials: the entry keeps everything (its parameter with its saved
and synced settings, its layer and effects, its place in an exclusive group, its default) but gets no menu control
and takes no menu slot. It is a state the avatar has without a button for it: flip triggers flip it, presets set it,
trigger conditions and gates read it, and a contact can therefore switch something that never clutters the menu.
The tree shows the row dimmed with a `no menu` chip, the wheel leaves it out in `Select` mode and shows it in `Test`
mode on a separate strip `Not in the menu` so it can still be tried, and the summary lists it under its own heading.
A hidden toggle that nothing can ever change and that is not on by default reports `ad.info.hidden_toggle_unreachable`.
"Nothing can ever change it" counts only a flip trigger or a preset that sets it: a trigger CONDITION that only reads
the toggle does not count, since reading it changes nothing about it (corrected from the first implementation; see
amendments). The parameter list is unchanged by hiding, so the PC and Quest
parity rule is unaffected. The switch sits beside `Default on` as `In menu` (on by default).

### Installing existing menu assets, and declared parameters (asked 2026-09-19)

The user: "We should also be able to just merge existing submenus when somebody wants to do that. Just like modular
avatar." Many prefabs ship a finished `VRCExpressionsMenu` asset, an animator controller and a parameter list. With
merged animators, this, and declared parameters, an AvatarDoc can carry such a prefab completely, the way a Modular
Avatar Menu Installer, Merge Animator and Parameters trio does.

- `AvatarDoc.menuAssets : List<MenuAssetInstall>`: `id`, `menu : VRCExpressionsMenu`, `installPath : string` (same
  format as the component's install path, relative to the root menu; empty means where the component installs),
  `asSubmenu : bool` (true: one submenu control holding the asset; false: the asset's controls are appended in
  place), `name` (rich, for the submenu; empty uses the asset's name), `icon`, `platform`. The asset is read into the
  merged model like the avatar's own menu (nested submenus included, cycles guarded) and written as clones, so the
  asset is never modified. A `platform` that leaves the install out still reads it into the model, flagged (see
  "Phase 9 amendments" below). Its controls count toward the page limit and split like everything else. Rows from a
  menu asset show an `asset` chip and can be renamed, re-iconed, hidden and moved through menu edits like existing
  rows; they resolve in the first pass because AvatarDoctor itself places them.
- `AvatarDoc.parameters : List<ParameterDeclaration>`: `name`, `type` (Bool, Int, Float), `defaultValue`, `saved`,
  `synced`. Declared parameters are planned in list order after the entries' parameters, identically on both
  platforms. A declaration whose name an entry already plans is `ad.warn.parameter_declared_twice` and the entry
  wins; one that already exists in the avatar's expression parameters reuses it (type clash:
  `ad.error.parameter_declared_type`). A control of a menu asset that names a parameter nobody declares is
  `ad.warn.parameter_undeclared` with an `Add declaration` action in the panel, because VRChat ignores menu
  controls whose parameter is not in the expression parameters; a merged animator's own parameter is not checked
  this way, since an undeclared one is simply an ordinary animator local parameter, and its panel already lists it
  as `not synced` with `Add control` (narrowed from a first draft of this sentence that also named merged
  animators; see amendments).
- Inspector: a `Menu assets` group row and a `Parameters` group row in the tree, `+ Menu asset` in the toolbar
  (dropping a menu asset onto the tree does the same), panels with the fields above; the parameters panel is a
  compact table with the bits each row costs and the total.
- Converting: a Modular Avatar Menu Installer with a `menuToAppend` asset becomes a `MenuAssetInstall` with
  `asSubmenu = false` (appended in place, matching what Modular Avatar itself does with `menuToAppend`; see
  amendments), and a Menu Item whose own submenu comes from a menu asset becomes one with `asSubmenu = true`; its
  `ModularAvatarParameters` become declarations (renamed or internal
  ones still make the group unsupported, as for merged animators); its Merge Animator becomes an `AnimatorMerge`.
  That is a lossless takeover of prefabs shaped like `Sera_Yura`, offered as one group in the import window and
  through `Convert to AvatarDoc` on the row. Where the layers are simple the converter still prefers real entries
  (Task 2); the creator can choose `Keep as it is` per group to take the menu, animator and parameters over
  unchanged.
- The summary lists menu assets and declared parameters; the lock guards both lists.

### Shapes on the outfit piece (asked 2026-09-19)

The user: "can we also add another Component like Blendshape Sync of Modular avatar that also can do the following:
When this Gameobject is on, set certain blendshape values of Y mesh to X. We put that on the outfit piece of the
avatar." and "I think its similar to our trigger, but sitting on the outfit piece." A second, small component,
`AvatarDocShapes` (visible name `AvatarDoc Shapes`, menu `AvatarDoc/AvatarDoc Shapes`), that sits on the outfit piece
itself, so a piece carries its own body fixes wherever it is dropped. It costs no parameter and no layer: like a
blend shape linked trigger it works by adding curves to the clips that already exist.

- `follow : List<ShapeFollow>`: `sourceMesh : SkinnedMeshRenderer`, `sourceShape`, `localShape` (empty means the same
  name). The piece's own mesh (the SkinnedMeshRenderer on the component's object) follows the source: at build the
  local weight is set to the source's weight on the clone, and every clip in the FX controller that animates
  `blendShape.<sourceShape>` on the source mesh gains the same curve for `blendShape.<localShape>` on the piece.
  A clip that already animates the local shape keeps its own curve (`ad.info.shapes_follow_kept_own`).
- `whileOn : List<ShapeWhileOn>`: `mesh : SkinnedMeshRenderer` (any mesh of the avatar, typically the body),
  `shape`, `value` (0 to 100). While the component's GameObject is on, the shape has that value; while it is off the
  shape is back at its rest value (the scene weight on the clone before this component touched it). Build: every
  clip that turns the object on gains the curve at `value`, every clip that turns it off gains the curve at the rest
  value, and when the object is on in the scene the clone starts with `value`. "Turns it on" means a constant
  `m_IsActive` 1 curve on the object itself, or on an ancestor while the object and everything between are on in the
  scene and never animated; "turns it off" likewise with 0. A curve that is not constant is skipped with
  `ad.warn.shapes_active_not_constant`. A clip that already animates that shape on that mesh keeps its own curve
  (`ad.warn.shapes_clip_owns_shape`). Two pieces that set the same shape to different values and can be on together
  report `ad.warn.shapes_conflict`; the larger value wins in clips where both apply, because shrink shapes are what
  this is for. AvatarDoc's own toggles, outfit sets, presets and triggers are covered because their generated clips
  carry `m_IsActive` curves like any other clip; so are merged animators and whatever Modular Avatar merged, because
  the pass runs after Modular Avatar. Clips VRCFury adds later in the build are not seen (README says so).
- The pass is `ShapesBuilder`, in the Transforming sequence that runs after Modular Avatar, working on the virtual
  clips of the animator services (never on assets). `platform` on the component like everywhere. The component is
  removed from the clone at the end like `AvatarDoc`.
- Validation (`ShapesModel`, pure, shared by the inspector and the build): mesh missing or outside the avatar
  (`ad.error.shapes_outside_avatar`), shape not on the mesh (`ad.warn.shapes_missing_shape`), `follow` rows on an
  object without a SkinnedMeshRenderer (`ad.error.shapes_no_mesh`), a `whileOn` row that targets the piece's own
  mesh is allowed, an object no clip ever switches and that is always on reports
  `ad.info.shapes_always_on` (the value is then simply applied at build).
- Scene preview and test mode: `PreviewModel` applies `whileOn` values for every piece whose object is on in the
  previewed state and `follow` weights from the previewed source weights, so flipping an outfit toggle on the wheel
  shows the body shrink at once.
- Inspector: its own UI Toolkit editor with the shared stylesheet and look: section `Follows` (rows: source mesh,
  source shape, local shape; `Add matching shapes` adds a row for every shape name both meshes have, skipping rows
  that exist) and section `While this object is on` (rows: mesh, shape, value slider 0 to 100). Mesh fields use the
  avatar scoped picker (`SkinnedMeshWithShapes`), shape fields the existing shape picker. Messages under the rows
  through `UiText.MessageLabel`. The summary of the avatar lists the pieces and their rows.
- Importing: a Modular Avatar Blendshape Sync component converts to `follow` rows and a Modular Avatar Shape
  Changer whose object is the piece converts to `whileOn` rows when all its shapes are of the `Set` kind; offered in
  the import window as candidates of their own, behind `DOCME_MA`.

### Not in phase 9

Layer types other than FX, VRCFury full controllers, editing a merged controller's layers, previewing merged
animators in the scene, blend trees with more than two children or two parameters.

### Phase 9 amendments

Recorded during implementation, one bullet per deviation or ruling. Each says what the spec above said, what is true
now, and why; the sentences above that these correct are also marked in place with a pointer to this list.

- **Explicit off values and inverted two state layers.** The spec said a two state layer becomes a Toggle only "with
  a constant clip", silent about the off state and about which state holds the on clip. Now: a blend shape or
  material property target on a Toggle can carry its own `explicitOff` value, written while the toggle is off
  instead of the value the scene had when the entry was made; the converter sets it automatically whenever the old
  layer's rest clip wrote a value the scene did not already hold, instead of refusing the layer. A two state layer
  whose DEFAULT state holds the on clip, with the other state reached by `IfNot` (the shape Modular Avatar itself
  builds), is also accepted now, written as an entry that starts on, provided the declared default is really true
  and the driving control's own value is above 0.5 (a value of 0 means the item is on while the parameter is false,
  so the rest state is not really the on state, and reading it inverted would swap the two clips). Why: refusing
  every layer whose off clip writes something, or whose on state happens to be the rest state, left simple, common
  menus such as the user's own `Shading_FX` unconvertible for no behavioural reason; both are accepted only under
  checked conditions that keep the conversion faithful.
- **Groups apply all or nothing; a merge group always removes its originals.** The spec did not say whether ticking
  or converting one item of a set that shares a Merge Animator, a takeover prefab or a layer affects only that item,
  or whether `Remove originals` is required for such a set. Now: every candidate that shares one of those is one
  group; ticking, importing or right click converting any member always expands to the whole group, and a group that
  would leave a Merge Animator's or a menu asset's originals behind while something else now drives what they drove
  is refused (`ad.import.reason.group_needs_removal`) unless `Remove originals` is on. Why: converting part of such a
  set, or converting it without removing the component that still merges the same controller, would merge the
  controller a second time or half convert a menu that has to move as one piece.
- **Undeclared Modular Avatar menu item parameters follow the item flags; the first ruling was reversed.** Checked
  against Modular Avatar's own `ParameterAssignerPass`: a menu item parameter that no `ModularAvatarParameters`
  declares is still created by Modular Avatar, with saved and synced taken from the OR of every item that names it
  and the default from whichever item is marked default. The first ruling (forcing such a parameter unsynced and
  unsaved) is reversed: the importer now reads the same facts, in the same order (a `ModularAvatarParameters`
  declaration first, then the avatar's own expression parameters, then the item flags). Why: the first ruling would
  have turned a toggle other players could already see into a local only one on import.
- **A Bool control with value 0 is never converted.** Not mentioned in the spec. A two state layer is only read as
  inverted (see above) when the driving item's own value is above 0.5; at 0 the layer's rest state is not its on
  state, so reading it inverted would reverse the two clips. This matches how VRChat itself reads a Bool control's
  `value`.
- **The avatar wide `ParameterUsage` walk.** The spec's "Remove from avatar" section named "no other menu control
  that survives... no remaining layer of ANY playable layer controller... no contact receiver or PhysBone... not a
  VRChat built in parameter" as the full reader list, shared with Import's "used elsewhere" check. The walk (one
  shared class, `Editor/Import/ParameterUsage.cs`, used by both) also counts: a parameter driver's copy source and a
  `VRCAnimatorPlayAudio` path; every `VRCContactReceiver.parameter`; every `VRCPhysBone`, expanded into its five
  generated suffixes (`_IsGrabbed`, `_IsPosed`, `_Angle`, `_Stretch`, `_Squish`); every VRCFury toggle's own global
  parameter and every parameter name inside a VRCFury Full Controller's own merged controllers (`globalParams` and
  every controller's own declarations alike); every non prefix Modular Avatar `ModularAvatarParameters` declaration
  and its `remapTo`, and every Modular Avatar menu item's own parameter; and, from this phase's own new lists, every
  AvatarDoc declared parameter and every installed menu asset control's parameter. Why: any of these can make
  removing or converting a name change the avatar's behaviour even though no AvatarDoc entry or FX layer reads it,
  so all of them have to count as readers or a removal or a conversion would be unsafe.
- **Removal is planned in the Resolving phase.** Not specified. The plan is made in a dedicated `BuildPhase.Resolving`
  pass, before any extension context (and so before NDMF's own animator services) opens, because by the time the
  pass that builds AvatarDoc's own layers runs, those services have already emptied the descriptor's controllers of
  their layers; a planner reading the descriptor there would see no layers and no readers and would free a parameter
  the avatar's own FX still uses. A build with no prepared plan (a direct call made before that pass ran) frees
  nothing rather than guessing. A layer the build cannot remove because animator layer control blocks it keeps its
  parameter too, not only its layer.
- **The runtime condition types are named `WhenCondition` and `WhenConditionKind`.** The spec text above named the
  serialized type `TriggerCondition`; that name was already taken by an editor class in `TriggerPlanner.cs`, so the
  runtime type is `WhenCondition` (its enum `WhenConditionKind`). Every sentence above naming `TriggerCondition`
  means `WhenCondition`.
- **Float negation keeps the threshold, with no step.** The spec said `Greater v` negates to `Less v + step` with
  step 0.0001 on a Float, the same as an Int's step of 1. A step on a Float would create a window (v, v+0.0001) where
  entering and leaving are both true, so the layer would flip every frame; keeping the same threshold instead means
  the two halves only disagree at the exact value, where the state simply holds, which is harmless. Only an Int
  keeps a step, and its step is 1.
- **The cap is checked on expanded alternatives before any product, and subsumed leave transitions are dropped.** Not
  specified. The count that trips `ad.error.trigger_too_complex` is taken on the fully expanded alternatives of the
  "met" disjunction before any product between them is built, so a group of several `NotEquals` conditions on
  distinct Floats cannot silently expand to a huge product first; building the negation itself also gives up midway,
  the moment the running product would pass the cap, rather than finishing an oversized one. A leave transition whose
  conditions are a superset of another kept transition's is dropped for both enter and leave, so "(X and Y) or (A and
  X)" builds exactly 2 leave transitions (`NOT X`, and `NOT Y and NOT A`), not one per raw combination. Why: without
  this an oversized condition set could hang the editor while typing, before ever reaching a build.
- **The unreachable hidden toggle note counts only flip triggers and presets.** The sentence above is corrected in
  place: the first implementation also counted a trigger condition (and a gate) that only reads the hidden toggle as
  a way to reach it, which is wrong, since reading it never changes it. Only a flip trigger or a preset that sets it,
  or being on by default, count toward "can be changed".
- **A two axis puppet member blocks its whole import group.** Not specified. If any item under a shared Merge
  Animator or takeover set cannot become an entry at all, such as a two axis (2D) radial puppet, which AvatarDoc has
  no matching control for, the whole group is refused rather than converting around it. Why: converting the rest and
  leaving that one item behind would still destroy the Merge Animator or Parameters component that item needs, since
  removal happens once for the whole set.
- **The picker claims the selector's events (default action), and lists one line rows.** Two sentences above are
  corrected in place: Unity 2022.3 opens its own object picker from the default action of a pointer or mouse down on
  the selector element, and from a key down on the field itself (the selector is not separately focusable), so
  `AvatarObjectPicker` has to claim all three (stop propagation, prevent the default action) or Unity's own scene
  wide picker would still open alongside it. A dropdown row is one line of text, "Name (Parent/Path)", not a
  genuinely two toned name-plus-dimmed-path layout, since `AdvancedDropdownItem` has no supported way to render two
  tones of text in one row.
- **A Menu Installer with `menuToAppend` appends in place.** The spec did not say whether such an installer becomes a
  submenu or an append. It becomes `asSubmenu = false` (appended in place), matching what Modular Avatar itself does
  with `menuToAppend`; only a Menu Item whose own submenu comes from a menu asset becomes `asSubmenu = true`.
- **The always on and never on notes are said by the build.** The spec lists `ad.info.shapes_always_on` under
  `ShapesModel`'s validation. Whether any clip ever switches an object is only known once the FX controller has
  been virtualised, so `ShapesBuilder` says it, together with its counterpart `ad.info.shapes_never_on` for a
  piece that is off in the scene and that no clip switches (not in the spec: without it such a piece would apply
  nothing and say nothing). The other four validation messages stay in the pure model, where the inspector reads
  them.
- **A clip writes the largest value of the pieces it turns ON, and the rest value only when it turns every one of
  them off.** The spec said "the larger value wins in clips where both apply". Taking one maximum over the on
  values and the rest values together would let a rest value above a piece's own value win in a clip that really
  turns that piece on: a body shape resting at 80 would stay at 80 in the clip that puts on a jacket holding it at
  30. So each (clip, mesh, shape) keeps the maximum of the on values and whether any piece is on at all; the rest
  value is written only where no piece of that clip is on.
- **Shapes on the outfit piece work on the FX controller only.** The spec named FX; the animator services index
  every virtualised controller of the avatar, so the pass filters both its loops through the clips the FX
  controller can reach. A Gesture or Action clip that happens to switch a piece is left alone, and a follow source
  animated by an emote is not copied into that emote, where an additive layer would apply it on top of the FX
  result.
- **A curve this pass wrote is not the clip's own.** The follow pass runs after the while on pass and re-reads the
  clips, so it would report a value the while on pass just wrote as `ad.info.shapes_follow_kept_own`. The pass
  keeps the pairs it wrote: where a follow row and a while on row of another piece meet on one local shape in one
  clip, the while on value wins and nothing is reported.
- **The AvatarDoc Shapes script icon is set through the script's own meta.** `EditorGUIUtility.SetIconForObject`
  plus a forced reimport did not take, so the icon line of `Runtime/AvatarDocShapes.cs.meta` points at
  `Editor/Icons/AvatarDocIcon.png`, exactly as `AvatarDoc.cs.meta` does.
- **`PlannedType` lives in the Runtime assembly.** Not specified. `ParameterDeclaration`'s type field and the entry
  planner's own `PlannedType` enum both live in `Runtime/ParameterDeclaration.cs`, because `AvatarDoc.parameters` is
  a serialized runtime field and a runtime field cannot use a type declared in the editor assembly. Every editor call
  site reads the same enum through the shared namespace, unchanged.
- **`ad.warn.parameter_undeclared` is for menu asset controls only.** The sentence above naming "a control of a menu
  asset or a parameter of a merged animator" is corrected in place: only a menu asset control's undeclared parameter
  warns. A merged animator's own undeclared parameter is an ordinary animator local parameter, which is normal, and
  the animator's own panel already lists it as `not synced` with `Add control`; warning about it too would only
  repeat that.
- **An install path of `/` means the avatar's root menu.** Not specified; only empty (meaning where the component
  installs) was named. `MenuModel.SplitPath` already reduced `/` to no segments, so nothing in the model needed to
  change; the tooltip states the rule, and a takeover writes `/` for a Modular Avatar installer whose own
  `installTargetMenu` names no target (installs at the avatar's root), so the takeover stays lossless on a
  component that itself installs as a submenu.
- **A takeover keeps hidden and removed asset controls quiet, hides the right control, and drops the right
  declaration.** Three related corrections found in review: a hide edit written for a converted item's replaced
  parameter now names that control's own occurrence in the rebuilt merged tree, not occurrence 0, so it can no
  longer hide an unrelated control of the same name at the same path that the avatar's own menu already had, and
  two same named asset controls no longer collapse into one hide; `ad.warn.parameter_undeclared` is skipped for a
  control that is hidden or excluded already, since a control the build never writes needs no parameter; and a
  takeover without `Keep as it is` drops a declaration whose name a converted layer replaced, when no remaining,
  non skipped layer of the merged controller reads it and no remaining visible asset control names it, instead of
  declaring a synced parameter nothing left drives. A Modular Avatar parameter marked not synced becomes no
  declaration at all, since Modular Avatar itself registers no expression parameter for one either.
- **The picker keeps its open stamp per selector.** Not specified. `AvatarObjectPicker`'s guard against a field's
  own click reopening the list it just closed is created per `TakeOverSelector` call and captured by that
  selector's own callbacks, not shared as one static value across every avatar object field, so opening one field
  right after closing another is never blocked by the other field's timing.
- **The merged controller's type wins where only AvatarDoc's own parameter controls or declarations put the name
  there.** The sentence above on `ad.error.merge_parameter_type` is corrected in place. A name that is in the FX
  controller only because AvatarDoc planned it, either for parameter controls alone or for a plain declaration of
  the component's own, is re-declared at the merged controller's type instead of raising the error, with the
  planned default carried over into that type; the expression parameter keeps the type the creator chose, which
  VRChat converts on its way in. A name the avatar itself declared, or that any generated layer reads, still keeps
  the declaration that is there and still raises the error. Why: a bought prefab whose Modular Avatar Parameters
  call a name Bool while its controller reads it as the Float of a blend tree is a very common shape, and Modular
  Avatar builds it without complaint; refusing to adopt the type left a `Keep as it is` takeover with a Bool under
  a blend tree and an error the creator could do nothing about.
- **A merged controller's play audio paths are prefixed by hand.** Not specified. `VRCAnimatorPlayAudio.SourcePath`
  is the one state behaviour path that NDMF's own object path index rewrites through platform bindings the merge
  does not have, so a merge with a `pathRoot` prefixes those paths itself, the same way it rewrites clip paths. Why:
  a relative path mode merge whose states play audio would otherwise point at an object path that only exists at the
  prefab's own root.
- **A platform limited menu asset is installed, flagged, not skipped.** The sentence above is corrected in place.
  An install the current platform leaves out is still read into the model, and every item it brought, nested
  submenus included, is flagged `Excluded`, exactly as an excluded entry's control is. The writer, the page split
  and the undeclared parameter check already skip flagged items, so nothing of it is built; `MarkExcluded` leaves
  an item that carries an asset alone, since that item's flag follows the asset's own platform choice. Why:
  skipping the install outright changed the shape of the model between PC and Quest, so a control of the asset was
  a reader of its parameter on one platform and not on the other, occurrences of same named controls moved under
  menu edits, and a submenu another component installs into by name landed in a different place, which could even
  reorder the parameter list.
- **`PlatformParity.ListOf` plans the removal.** The sentence above is corrected in place: the check ran on the
  whole expression parameters asset and never consulted `RemovalPlanner`, so a copy that frees a parameter looked
  identical to a twin that keeps it while every later position had shifted in game. `ListOf` now plans the removal
  from the same tree and leaves freed names out.
- **On the merge path a parameter another layer drives is never converted.** Not specified beyond the note
  `ad.import.note.driven_elsewhere` ("that keeps working"), which is true only where the entry keeps the old name.
  A Modular Avatar item whose layer would convert becomes a parameter control instead, with
  `ad.import.reason.driven_elsewhere` as the reason, when any other layer of the same merged controller writes
  the parameter: as a driver's target, as a driver's copy source, or as a `VRCAnimatorPlayAudio` parameter. Why: a
  converted entry on this path gets a parameter of its own and the driving layer stays merged, so the driver would
  be writing a name nothing answers any more. `FxLayerImport` keeps the note as it is, since its draft keeps the
  old name.
- **A takeover whose install target cannot be found is refused.** Not specified. A Modular Avatar Menu Installer
  whose `installTargetMenu` is set but is nowhere in this avatar's menu tree, usually a menu another installer
  brings, makes the whole takeover group unsupported with `ad.import.reason.install_target_unknown` instead of
  falling back to `/`. Why: `/` is the avatar's root, so the fallback would quietly move a whole prefab's menu to
  a place the creator never chose, and the takeover would no longer be lossless.

## Phase 10: capturing a shape set (directed 2026-09-20)

The user: "When we do blend shapes we should be able to capture the current blend shape setup of a certain mesh
(anything above 0) and the rest should be set to 0 (but don't show the 0s in the menu, it should be a checkbox)",
and the reason: "The use case for this is really to toggle this on, but the other blendshapes from other animations
kind of are additive so the face gets janky." So this is not only a way to fill rows quickly. While the toggle is
on, that mesh's shapes must be exactly what was captured, whatever else animates them; while it is off, nothing of
this entry may touch them, or face tracking and the other animations would be pinned to the scene values.

### Capturing

- The Blendshapes section of a toggle gets a `Capture` button beside `+ Add`. It opens the avatar scoped picker for
  skinned meshes with shapes. Picking a mesh adds one row per shape of that mesh whose weight is above 0 in the
  scene (above 0.0001), at the weight the scene holds, in Pin mode. A row this entry already has for that mesh and
  shape keeps its place and takes the captured weight; nothing is duplicated. A mesh where every shape is 0 adds no
  row and reports `ad.info.capture_nothing_above_zero`.
- Every Blendshapes section offers Capture (a radial's capture fills its upper end, an override's its pinned
  values). A toggle's Capture also adds the rule below; a trigger with effects of its own gets a rule only through
  Hold the rest (plan 15).
- Pin mode is what the use case needs: the On state forces the value over every layer below, the Off state writes
  nothing at all, so other animations keep the shape when the toggle is off. The captured rows are ordinary rows
  afterwards: editable, removable, and switchable to Fixed if the creator wants the softer behaviour.

### Everything else at 0

- `MenuEntry.shapeSets : List<ShapeSetRule>`, `ShapeSetRule { SkinnedMeshRenderer mesh; bool zeroOthers;
  ShapeSetValue value; bool holdTracked; }`. A toggle's capture adds a rule for its mesh with `zeroOthers` on and
  `value` `Zero`. The rule stands for every shape of that mesh this entry does not name: while the entry is on, each of
  them is held at the rule's value, and no row is shown for any of them. `zeroOthers` is the rule's on and off
  switch (read through `ShapeSetRule.On`); it keeps the name it was saved under.
- The inspector shows each rule under the Blendshapes list: the switch `Everything else on <mesh>` and a remove
  button, then `Hold at` with `Default` or `0`, the `Blink and visemes` switch, and a note saying how many shapes
  the rule holds and at what (`ad.info.shape_set_count`, `.default`, each with its singular). Dropping the rule
  leaves the captured rows alone.
- The rule is built into the same pin layer the pinned rows use, so it follows pin semantics exactly: forced while
  on, nothing written while off. An entry whose rows are all Fixed still gets its pin layer when it carries a rule.
- The rule writes every other shape of the mesh, not only the ones something else animates: a shape a tool outside
  AvatarDoc moves would otherwise survive, which is the jank the user is trying to end. The note names the count so
  the cost is visible, and `ad.warn.shape_set_large` says so above 150 shapes.
- A rule whose mesh is missing or outside the avatar is `ad.error.shape_set_outside_avatar`; two rules for one mesh
  on one entry are merged when the model is built (on if any is on, value and blink switch from the first one that
  is on); a rule on an entry that cannot hold one (`MenuEntry.OffersShapeSets`: a radial, an override, a flip
  trigger) is `ad.error.shape_set_not_a_toggle`, a key kept from when only toggles could. A rule costs no parameter
  and no bit.
- The scene preview and the wheel's test mode follow the rule: while the entry is previewed on, the other shapes of
  that mesh read the rule's value.
- The summary lists the rule under its entry with the mesh, the count and the value.

### Holding the rest at its default (asked 2026-10-10, plan 15 task R)

The creator: "when working with blendshapes there should be a toggle to pin all other blendshapes to the default
state (current state of avatar by selecting the mesh renderer)". Their choices: on toggles and triggers; the blink
and lip sync shapes settable per rule; the default read at upload.

- **What it holds.** Every shape of the rule's mesh except the ones the entry names (its rows, and on a toggle every
  shape its own Animations clips move, paths resolved the way `CustomClips.Collect` resolves them, path root
  included: those play in the main layer below the pin layer), a blend shape trigger's own shape (holding it would
  feed the trigger its own output), the shapes an included AvatarDoc Shapes piece drives (its while-on rows and the
  local shapes of its follow rows, from `ShapesModel.Plan`; the creator chose to leave those to the piece), and,
  unless `holdTracked` is on, the descriptor's eyelid and viseme or jaw flap shapes (`ShapeSetModel.HeldShapes`).
  What the avatar contributes is read once per tree (`MenuTree.HeldContext`, kept by the plan cache, which drops on
  any change to a component of the avatar) and once per build (`BaseState.HeldContext`); the pieces are read only
  when a rule asks.
- **At what.** `ShapeSetValue.Zero` (0, every rule saved before this) or `Default`: the shape's weight on the build
  clone before AvatarDoc changes anything, `BaseState.WeightOf`, the same value every Off state and every release
  to rest uses, so a held shape and an Off state never disagree. `BaseState` reads every shape of a rule's mesh
  before `DefaultsApplier` writes a default on toggle into the clone. AvatarDoc Shapes pieces write the clone later
  (`ShapesBuilder`, after Modular Avatar), which is why a rule leaves the shapes a piece drives alone.
- **Where.** A toggle and every trigger with effects of its own (`MenuEntry.OffersShapeSets`). A radial and a flip
  trigger keep the error, reworded: only a toggle or a trigger with effects can hold one.
- **How it is built.** A toggle and a Hold trigger (any condition source): in the pin layer, as before, the held
  shapes written at the rule's value in On and nothing in Off; `ReleasePinsToRest` hands them back to their default
  when nothing else owns them. A Fade contact: in the Near tree, at their default in the child at 0 and at the
  rule's value at Fully on (`ClipContent.WriteShapeSets`); Far writes nothing and the release pass decides. A blend
  shape trigger: like a Fixed row (`LinkLayerBuilder.Effects.Held`), from the default at Start to the value at Full
  in Smooth, held in On in Switch, nothing in the rest state. `DefaultsApplier` writes a default on toggle's held
  shapes and a blend shape trigger's at its source's weight into the clone, the same way it writes rows.
- **Preview order.** A toggle's or a Hold trigger's Pin rows and rule shapes are queued in `PreviewModel.WriteSwitch`
  and written in tree order after the controls and the other triggers, before Fade and blend shape triggers: the
  order `MovePinLayersLast` gives their layers, above every ordinary layer. So a held shape shows the rule's value
  whichever entry comes later in the tree.
- **Blink and visemes.** Left out of the held set by default, for stored rules too: a stored rule on the face mesh
  used to write 0 for the eyelid and viseme shapes in its pin layer's On state and registered them for the release
  pass, which then gave Off a rest curve as well, so FX bound those shapes in both states and VRChat's blinking and
  lip sync stopped for good, and no tracking pause was built. With `holdTracked` on they are held, and the entry
  gets a pause flag (`<prefix>/Pause/<name>`, named in `TouchBuilder.Describe` after every trigger's, so a trigger
  keeps its name) written 1 in every state where the hold is active and 0 in the rest: a pin layer's member state
  and its Off (an exclusive group's other members write 0 too), a Fade's Near children, a blend shape trigger's
  driven states. `BuildTrackingPauseLayers` reads it with the others. A held tracked shape is never registered for
  release: while the rule does not hold it, VRChat's tracking owns it. The parameter provider lists a toggle's flag
  as animator only, like a trigger's.
- **Pin rows on those shapes (fix round 1).** The same holds for a Pin row naming an eyelid or viseme shape (Capture
  makes one from a sleepy face): `BuildPinLayer` never registers a tracked shape for release, the rule
  `BuildFollowDistanceLayers` and `LinkLayerBuilder` already followed, and such a row raises the entry's pause flag
  while on (`ShapeSetModel.PinTracking`, counted in `TouchBuilder.Describe` for toggles and Hold triggers).
- **The pause at load.** Each pause layer starts in `Load` (no behaviour), which goes to `Idle` after an exit time of
  5 (an empty state lasts one second): VRChat ignores tracking control for a moment after load and `Paused` asks
  once, so a flag that is 1 from the first frame (a default on toggle that holds blink) is heard after the wait.
- **Inspector.** `Hold the rest` beside `Capture` opens the avatar's mesh picker and adds a Default rule with the
  blink and viseme shapes free; a mesh the entry already has a rule for keeps that rule, nothing is added (one Undo
  step, `EntryEditing.AddHoldRule`). Capture keeps adding its rule at 0, as first asked, on a toggle only: the
  creator chose that a trigger's Capture adds the captured rows alone. The
  rule's lines: the switch with the mesh name (a long name ends in an ellipsis; the tooltip names it in full) and
  the remove button on one line that never wraps, then `Hold at` with the `Default` or `0` choice and, only for a
  mesh the descriptor blinks or speaks with, `Blink and visemes` (that line wraps at the narrow end of the
  inspector), then the count note. With Hold the rest beside it, the section header wraps its last button onto a
  line of its own, at the right, when the three do not fit. Each control writes one Undo
  step. A section whose only content is a rule stays shown (`TargetListView.HasContent`). In Simple, a toggle's
  Blend shapes section shows `Hold the rest` with Capture and the rule lines while it has a rule; triggers show
  their whole panel as before.
- **Words.** `ad.warn.shape_set_large` speaks of holding, for both values; the summary line names the value
  (`ad.summary.shape_sets.line`, `.default`, each with its singular); "What this builds" says blinking or lip sync
  pauses while the effects are on when a rule holds those shapes.

### Pin layers go last

Pins only win over what sits below them in the FX stack. AvatarDoc builds before Modular Avatar, so until now a
layer Modular Avatar merged afterwards (face tracking is the common case) was written after the pin layer and took
the shape back. Pin layers are therefore created with the highest layer priority, and the pass that runs after
every other tool moves them to the end of FX before it hands the controller on, so a layer VRCFury merged outside
the animator services cannot override them either. That is what a pin means, and it is what makes the captured set
hold while the toggle is on. The move is skipped for an avatar with no pin layer, so nothing is round tripped for
avatars that do not use them.

## Phase 11: following another object, and shareable trigger prefabs (directed 2026-09-20)

The user: "For the Triggers we should add a section (Prefabs) that are customizable and saveable as asset that people
can spread, being read from a certain folder in assets and later on also from the packages (default ones that come).
My friend wants a grabbable object: grabbing an object with the left hand on his back with his hand (left or right
hand) and then it will be constraint (in this case parent constraint) on his hand instead of the back as example,
that should also be a thing." Two features. The grab case needs an effect AvatarDoc does not have yet, so that
effect comes first and the prefab library is written on top of it.

### Following another object

A new target list beside objects, blend shapes, materials, properties and animations: `MenuEntry.follows :
List<FollowTarget>`, `FollowTarget { GameObject target; Transform follows; bool keepOffset; float weight; }`. While
the control is on, or the trigger is met, `target` follows `follows` instead of hanging where it is; while it is
off, it is back where it belongs. It is the missing piece for "grab a thing off my back and it sticks to my hand".

- Build: the object gets one `VRCParentConstraint` created on the build clone (never in the scene). Its first
  source is the object's rest place, which is the transform it already hangs under, and every follow target of
  every entry that names this object adds one more source. The constraint is locked and active, all sources at
  weight 0 except the rest source at 1. The weights of one constraint are owned by ONE layer of its own, not by the
  entries: a rest state with the rest source at 1 and every claim at 0, one state per claiming entry with its own
  source at 1 and every other at 0, and transitions built from each claimant's parameter or conditions, the later
  entry in tree order first so it wins while several are met. Entry layers never write a source weight, because two
  entries writing the same constraint from two layers leaves the object hanging between two places. Blend time is
  the transition duration. At most 16 sources fit one constraint, so the rest place plus 15 claims: beyond that the
  extra rows are an error naming the object, never a curve VRChat silently drops.
- `keepOffset` means the pose the object has relative to the TARGET as the creator posed it in the scene: drop the
  sword into the hand, switch it on, and that is where it lands. Off, which is what a new row starts with, snaps the
  object onto the target's own origin, which is what the grab case wants. The offset is never the pose the object
  had under its rest parent: reusing those numbers under a hand puts the sword beside the hand, not in it.
- An object that already carries a constraint of its own, from the creator or from another tool, is not touched:
  `ad.error.follow_existing_constraint` names it. An object outside the avatar, a target outside the avatar and an
  object that is also driven by another tool's constraint are errors of the same kind.
- No parameter and no synced bit: the effect rides on the parameter the entry already has.
- The scene preview shows it: while the entry is previewed on, the object is drawn at the target, so a creator sees
  where the sword lands before uploading. Test mode on the wheel does the same.
- Platform: an excluded entry builds no constraint and no source, like its other effects.
- Blend time applies to the source weights, so a follow can fade like the rest of the entry.

### The grab case, as a recipe

Grabbing with a known hand is not one VRChat feature, it is three: the PhysBone on the object reports that it is
held, and a contact receiver on the object tagged `HandL` or `HandR` says which hand is near it. AvatarDoc already
has PhysBone and contact conditions and the condition groups of phase 9, so the recipe is: a trigger whose When is
"held AND left hand near" follows the left hand bone, a second trigger with the right hand follows the right hand
bone, and both are gated by nothing else. That works today apart from the follow effect, which is why it comes
first. What is missing afterwards is a way to hand that whole arrangement to someone else, which is the prefab
library.

### Grabbable objects, in one row (asked 2026-09-21)

The user, after reading the five step recipe for a grabbable sword: "Can we make this simpler? more automated?" So
the recipe becomes one entry. `EntryKind.Grabbable`: pick the object, and AvatarDoc builds the whole arrangement.

- `MenuEntry` fields for it: `grabObject : GameObject`, `grabHands : GrabHands` (Left, Right, Both; Both is the
  default), `grabKeepOffset : bool` (off by default, so the object snaps into the hand), `grabAnyone : bool` (on by
  default: other players may grab it too), `grabReturnSeconds : float` (the blend back to the rest place),
  `grabMenuToggle : bool` (off: a grabbable is not a menu control, it has no icon and takes no menu slot; on: the
  creator gets a toggle that allows grabbing at all).
- What the build creates, all on the clone and nothing in the creator's scene: a `VRCPhysBone` on the object with
  grabbing allowed, no movement of its own and a grab parameter; one `VRCContactReceiver` per allowed hand on the
  object, tagged `HandL` or `HandR`, sized from the object's bounds; the parameters those need, which are local and
  cost no synced bit; the follow sources and the follow layer of the phase 11 follow effect, with one claim per
  allowed hand; and, when `grabMenuToggle` is on, a normal toggle that switches the PhysBone's grabbing on and off.
- The hands are found from the avatar's humanoid rig (`HumanBodyBones.LeftHand`, `RightHand`). An avatar without a
  humanoid rig, or with a hand bone missing, is `ad.error.grab_no_hand` and nothing is built for that side.
- An object that already carries a PhysBone of its own keeps it: AvatarDoc adds the grab parameter to it rather
  than a second one, and says so with `ad.info.grab_uses_existing_physbone`. An object that already carries a
  constraint is the same error the follow effect reports.
- The inspector row is short on purpose: the object, which hands, keep offset, and a fold with the rest (anyone may
  grab, return time, the menu toggle). The panel says in one line what it will build, so the creator can see that
  it is a PhysBone plus contacts plus a constraint without reading the docs.
- The preview and the wheel's test mode show the object in the chosen hand, so a creator can check where it lands
  before uploading.
- A grabbable is also the first thing the prefab library will ship, once its form is settled with the user: the
  same row, saved and handed on.

## Phase 12: user friendliness (directed 2026-09-25)

The user: "Can you make the UX for avatar doc more user-friendly? Look up how the best methods would be. And
implement them." Partway through, once the tree could hold enough kinds of row to make one worth asking for: "give
a function to filter between tags."

### What the research found

Two research passes ran before any task: what creators actually complain about and argue over, and what good editor
tools do generally. The creators' top failures were invisible ones: Write Defaults mixing that silently breaks
toggles, toggles that never reset, and errors that only surface at upload. The 256 bit synced budget is often
confused with the far larger 8192 bit total, and the 8 slot page limit breeds unwanted submenu sprawl. The one real
philosophical complaint about one click tools was that they "hide the inner reason why people do things"; beginners
fear Unity itself and do not know which tool their purchase even needs. Good editor tools share a short list of
habits: defaults that just work, one level of Advanced per section, empty states that name the next action, inline
validation beside the field, errors that name the exact thing and the fix, primary actions as visible buttons with
right click kept only for extras, limits warned before they are hit and costed where they are added, and
contextual first-use tips instead of a guided tour. An audit of AvatarDoc against both found a heavy first screen
(about nine header controls and eleven add buttons with nothing built yet), every panel showing every section
regardless of use, key actions reachable only by right click, problems staying quiet until Summary was opened, and
the same idea going by two names in different places (Import in one, Convert in another; Hidden on one kind of row,
something else implied on another). The full evidence lives in `research-community.md`, `research-ux.md` and
`audit-avatardoc.md`; the plan built from it is `ux-plan.md`. The central design was never in question: the
research found no argument against a non destructive, component-described tool, and AvatarDoc's own tree already
answers the 8 slot sprawl complaint by splitting pages for the creator. Four tasks followed, in the order the plan
judged they would pay off soonest.

### Task 1: messages that say what to do

Every error and warning text was rewritten from a bare statement of fact to "what happened, then what to do",
naming the row, value or type involved and quoting the on-screen control the fix uses; internal words such as
gate, claim, binding and occurrence were swept out in favour of the words the UI itself shows. Seven call sites
needed a new argument passed alongside the message so the text could actually name the thing it now claims to name
(a parameter's existing and wanted type, a PhysBone's own name, the layers sharing a name, the rows overflowing a
follow limit). Three JSON keys turned out to hold advice (`:hint` suffixes) that no code ever read, so their
sentences were folded into the base message and the orphan keys removed. A new guard test file checks, from here
on, that no message uses a banned internal word, that every error and warning is at least two sentences and names
at least one thing, and that every call site passes at least as many arguments as its text's highest placeholder
needs. Both message tables in this README were rewritten row for row to match.

### Task 2: ready to upload, and costs where they are added

The header gained a status line, `Ready to upload` or a count of problems, with a `Show` button that selects and
reveals the next one and focuses the field a message names when it names one; the `Summary` button carries the
same count as a badge. A note under it states the Write Defaults setting AvatarDoc's own layers will use, matched
against the avatar's own FX layers, with a warning when the avatar disagrees with itself. Two of VRChat's own
upload-time SDK checks, the synced parameter budget and oversized or uncompressed menu icons, are now mirrored
inside the editor itself so both surface before an upload rather than during one; the SDK's own source was read to
confirm which of its checks were worth mirroring and which either could not fire on an AvatarDoc-built avatar at
all or were soft performance ranks rather than hard upload blocks. The `Synced` switch on a toggle or radial now
shows its own bit cost beside it, a submenu's tree row shows its slot count once it is nearly full, and every own
entry gained a `Builds: ...` line naming what it actually adds when it builds.

### Task 3: a first screen that invites, a slim toolbar, visible row actions, and a tag filter

An empty component now shows a welcome card instead of a bare drop hint: three starts (make a toggle from an
object, convert from VRCFury or Modular Avatar when either is actually found on the avatar, start an outfit set)
and a Quick start link, with the card gone the moment the tree has an entry. The header's `Install into`,
`Install as`, `Parameter prefix` and `Platform` fold behind one `Component settings` fold, closed by default, with
a dot when it hides a changed value; the memory bar stays visible outside the fold. The toolbar keeps only
`+ Toggle`, `+ Radial`, `+ Submenu` and `Convert` visible, moving everything else behind a `+ More` menu with a one
line description per item. A selected own row now shows `Move to`, `Duplicate` and `Delete` as small visible
buttons, a foreign row shows `Convert`, and every row grows a drag grip on hover. A tag filter sits beside search:
rows carry tags across four groups (Kind, From, State, Platform), ticking several tags of one group is OR, ticking
tags of different groups is AND, and a submenu kept visible only by a matching descendant is dimmed, the same
mechanism search already used and now shares.

### Task 4: panels that grow with you, one name for one idea, and first-use tips

A new toggle or radial now shows only the target section it actually uses, with a `+ Add effect` menu offering
every other kind with a one line description; removing a section's last row hides it again. Every own entry's
panel gained one `Advanced` fold at the bottom (Platform always, and for a toggle or radial also Parameter, Saved,
Synced and Blend time), closed by default with the same non default dot the header's own fold uses; a trigger now
starts as a single condition row and only grows groups once `+ And` or `+ Or` is actually pressed, folding back to
one row the moment only one condition is left. Every remaining "Import" in the UI and this README became "Convert",
every "Hidden" on an existing or foreign row became "In menu" with the same on-by-default sense an own entry's
switch already has, and a preset's old `Preview`/`Reset` buttons became the same `Default`/`On`/`Off` control every
toggle-like kind uses, with an Override showing the same control disabled and a reason underneath. The first time a
creator opens a panel of a given kind, or the header, a one line tip now explains what it is for and names the
first thing to do; a `Tips` switch inside the Component settings fold turns every dismissed tip back on, stored per
user in EditorPrefs, never in the scene, and the view itself defaults to an in-memory store so no test ever touches
a user's real preferences by accident.

### Phase 12 amendments

Recorded during implementation, one bullet per deviation or ruling, the same way earlier phases record theirs.

- **The Write Defaults warning does not say "majority".** The plan's own wording suggested AvatarDoc "follows the
  majority" when an avatar's FX layers disagree, but AvatarDoc's real build rule, unchanged by this phase, is that
  any disagreement at all resolves to Write Defaults on, regardless of which side has more states. The plan also
  said explicitly not to change what the build decides. Since the two instructions conflicted, the warning was
  written to say what actually happens instead of a "majority" claim that would be wrong on a real, unevenly split
  avatar.
- **Message argument checking is "at least as many", not "exactly as many".** The new guard test that checks a
  message call site passes enough arguments for its text's placeholders uses "at least" rather than strict
  equality: `ad.warn.edit_source_missing` is raised both from a real build, with a clickable object reference as a
  second argument, and from a live header preview with no such object to pass, and the text itself only ever needs
  the first argument. Requiring an exact count would force an artificial, unused second argument onto the preview
  call site.
- **Only one message is tagged for `Show` to focus a field.** `Show`'s ability to focus the exact field a message
  names, not just reveal and select its row, is wired onto `ad.error.parameter_type_conflict` only, the one
  mirrored SDK check that already had a natural field to point at. Tagging the rest of the roughly ninety existing
  messages with the right field name each is left for a future round, rather than guessed at here.
- **The narrow row width is 360 px, chosen, not measured.** The tree's row actions and less essential chips collapse
  once the row is narrower than 360 px. The first choice was 260 px, but the review of task 4 showed that the tree
  is almost as wide as the inspector, so at the default width of about 300 px the compact layout never switched on
  and a selected row still overflowed. 360 px makes the default width compact and keeps the full buttons for wider
  inspectors. Nobody has confirmed it visually against a real Editor window yet.
- **The Advanced fold is built for all seven own-entry kinds, not only Toggle, Radial and Trigger.** The plan's own
  text introduces growing panels through those three kinds, but its very next sentence says each panel gets ONE
  Advanced fold. Submenu, Override, Preset and Grabbable only ever have Platform to put in it, so their folds stay
  thin, but building the one mechanism once and reusing it everywhere was judged smaller and more consistent than
  inventing a rule for which panels get a fold and which do not.
- **A preset's new `Off` forces every named control to its own off value.** The plan says `On` applies the preset
  and `Default` resets it, but does not say what `Off` should do for a preset, which has no single "off state" of
  its own the way a toggle does. The symmetric, toggle-like reading was chosen so a preset stopped being the one
  kind whose preview control was shaped differently from everything else.
- **The "+ More" menu is a plain `GenericMenu`, one line per item, not a bespoke two-line popup.** The plan asks for
  each item's name and a one line description; that pair is shown as one string, `Name: Description`, inside
  Unity's own `GenericMenu` rather than a custom dropdown window, to keep the toolbar's own data source (the same
  list of names and descriptions) reusable without building a second, richer popup this round.
- **Three JSON `:hint` keys were deleted rather than kept dead.** `ad.info.menu_split:hint`, `ad.warn.target_missing
  :hint` and `ad.error.bits_exceeded:hint` existed in the localization file but were never read by any code, so
  their advice never reached a creator. Task 1 folded each sentence into its own message's base text and removed
  the three orphan keys, rather than leaving unread JSON behind for the message rewrite it was meant to fix.
- **A shared follow or grab constraint is credited to one entry only, fixed mid-phase.** Task 2's own "What this
  builds" line first counted a shared constraint on every entry that claims the same target object, which could
  show two entries each claiming "1 constraint" for what the real build shares as one. Task 4 fixed this with a
  single claim map built once per plan, so only the first entry in tree order is credited and the rest correctly
  show zero for that constraint.
- **A trigger's fold-back-to-one-row bug was found and fixed inside the same task.** Removing the second condition
  of a two-condition group used to leave a single-condition card standing forever, captioned "All of", instead of
  folding back to the plain row the plan calls for; the removal path was corrected to check the true condition
  count everywhere it drops back to zero or one, not only when a group is completely empty.
- **The Advanced fold's own non default dot had a stale-state bug, found and fixed inside the same task.** Toggling
  the fold open then closed, with no panel refresh in between, could leave the dot showing whatever it last showed
  rather than the fold's real current state; the fold now re-derives the dot from its last known value on every
  toggle, not only on a full rebuild.

## Phase 13: contacts that follow distance (directed 2026-09-26)

`TouchMode` gains `FollowDistance = 2`, a third mode for a Contact sourced trigger (append only). `MenuEntry` gains
`contactFullAt : float` (default 0.8, range 0.05 to 1), next to `contactProximity`: a per-entry setting, not a
per-condition one, since it belongs to the trigger as a whole rather than to any one condition of a "when" group.

`MenuEntry.IsFollowDistanceTrigger` is the one predicate the build and the inspector both ask: true only when the
source is Contact, the mode is FollowDistance, and the first condition the trigger actually reads (its flat fields,
or the first non-empty condition across its groups) is itself a contact. A trigger whose data says FollowDistance
but fails any part of that behaves and shows as Hold, the same as a stale `FlipToggle` on a trigger of another
source; in particular a PhysBone trigger is never offered the mode at all (its `SegmentedChoice` has two items, not
three), so a source switch is the only way stale data like this happens.

When Follow distance is picked on a trigger with more than one condition, or with an `Only while` gate, the data is
kept untouched and the build uses only the first condition: `ad.warn.follow_distance_one_contact` explains this, as
a warning, not an error, so it is shown but never blocks the build. `Only while`, while a gate is actually set,
stays visible in the inspector (only the empty "add a gate" state is hidden), since the toggle it names still
exists even though the build ignores it here. (Plan 14 task S replaced this: the rest of the first contact's card
and the gate are requirements now, and only conditions in other cards are ignored with the warning.)

**Build.** The receiver is built exactly as a Hold or Flip contact's, but always `ReceiverType.Proximity`, whatever
`contactProximity` holds, and its Float parameter is animator only, so the mode costs no synced bits and never
shares a receiver with another condition (sharing would force that condition's own receiver type to match). One FX
layer, `AD: <name> (distance)`, is built exactly like a real pin layer: both states Write Defaults off regardless of
the avatar's own choice, at `LayerPriority(int.MaxValue)`, and named into `pinLayerNames` so `MovePinLayersLast`
moves it after anything Modular Avatar or VRCFury merge in, face tracking included; it never counts as its own
owner (`AnimatorBuilder.SomeNonPinLayerOwns` skips a layer named in `pinLayerNames`, and since plan 14 task P every
layer that recorded the binding being released). It holds two states:

- `Far`, the trigger's off pose, minus its blend shapes and (since plan 14 task P) its material properties: those
  are left empty and handed to the same deferred release pass a pin layer's off state uses
  (`AnimatorBuilder.ReleasePinsToRest`, reusing `PinLayerState.Bindings` for shapes and `PinLayerState.PropertyRests`
  for property channels, under the layer's own name), so a shape or property something else already owns, such as
  face tracking's Direct tree or a toggle that moves the same property, is left alone instead of being masked while
  no hand is near. The rest value it releases to is read from the off pose's
  own computed weight (`offContent.ShapeWrites()`), the same value Near's off child shows, so nothing jumps at the
  boundary.
- `Near`, a Simple1D blend tree on the receiver's float, blending from the off pose (threshold 0) to the on pose
  (threshold `contactFullAt`). Every shape mode (Fixed, Follow, Pin) blends here the same way: there is no separate
  pin layer to reserve Pin's usual "no curve while off" rule for, so `TouchBuilder.WriteAllShapes` fills in the one
  case `ClipContent.WriteToggle` would otherwise still skip.

Far to Near and Near to Far transition with no exit time at the float crossing 0.001, in either direction.

**Tracking: one shared pause layer per face part, not a control per trigger.** A tracking control on the trigger's
own layer would set Eyes or Mouth back to Tracking every time a hand left, at every avatar load (the default
state), fighting a VRCFT template or any other system that already owns that part, and two triggers on the same
part (a Headpat and an Eye touch, say) would resume tracking the moment either hand left even while the other was
still near. Instead, `TouchBuilder.BuildTrackingPauseLayers` builds at most two shared layers, only when at least
one Follow distance trigger needs them: `AD: Blink pause` (Eyes) and `AD: Lip sync pause` (Mouth), each a pin layer
with three states and no curves of its own:

- `Idle`, the default, with no behaviour at all, so nothing changes at load and another system's own control over
  the part is never touched while no hand is near it.
- `Paused`, entered from Idle by one transition per contributing trigger's float (Greater 0.001, several
  transitions OR each other), sets the part to Animation on entry.
- `Release`, entered from Paused by one transition whose conditions are Less 0.001 on every contributing float
  (one transition, AND), sets the part back to Tracking on entry, then falls straight through to Idle (exit time 0,
  duration 0) so Idle's own lack of a behaviour is what actually holds the released value.

`TouchBuilder.FollowDistanceTracking` decides which part a trigger needs: an eyelid shape only counts while
`enableEyeLook` is on and `customEyeLookSettings.eyelidType` is Blendshapes; a mouth shape counts for
`LipSyncStyle.VisemeBlendShape` (matched against `VisemeBlendShapes`) or `LipSyncStyle.JawFlapBlendShape` (matched
against `MouthOpenBlendShapeName`, on the same `VisemeSkinnedMesh`). Every shape mode counts, Pin included, since
Follow distance has no separate pin layer to reserve Pin's usual exclusion for.

`AvatarDoctorPlugin.FinishAfterOtherTools` (the Optimizing phase pass, after every other tool has merged) calls
`TouchBuilder.RemoveForeignOwnedPauseLayers`: when the final controller already holds a `VRCAnimatorTrackingControl`
setting Eyes or Mouth to Animation in a layer that is not the pause layer just built, that other system already
owns the part, so AvatarDoc's own pause layer for it is deleted and `ad.warn.follow_distance_tracking_left_to` names
the layer left holding it. `PinLayerState.BlinkPauseLayerName` and `.LipSyncPauseLayerName` carry the built layer's
name from the build pass to this later one.

**Inspector.** The mode's `SegmentedChoice` reads "Hold", "Flip" and, on a Contact source only, "Distance" (short
so the row fits at the default width), each with its own one sentence tooltip; a PhysBone source's choice has only
the first two. In Follow distance, the `Distance` (touch or closer) choice is replaced by a `Fully on at` slider
(0.05 to 1) with a note explaining it; `+ And`, `+ Or` and an empty `Only while` are hidden; every blend shape row's
own Fixed/Pin choice is hidden (the stored value is kept, just unused); `+ Add effect` does not offer Follows, and
an existing Follows row stays with `ad.warn.follow_distance_no_follows` (`FollowBuilder.Handle` skips it, so the
build never claims a constraint for it). "What this builds" names the blend ("fades with distance, fully on at
80%") and, when a pause layer would be built, adds "Blinking pauses while a hand is near" or "Lip sync pauses while
a hand is near".

### Deviations from the phase 7 sketch and from the first pass at this task

- **Follow distance never shares a receiver with another condition.** The existing sharing key includes
  `contactProximity`, and Follow distance ignores that field for its own receiver type; sharing would risk forcing
  a Hold or Flip condition with the same settings into a Proximity receiver it never asked for, so Follow distance
  always builds its own.
- **A first pass gave each trigger its own tracking control and left the layer at default priority and Write
  Defaults; a heavy review found both broke on a real avatar** (a face tracking controller merged after AvatarDoc,
  or a Write Defaults on avatar, could mask the effect entirely, and a tracking control on the trigger's own layer
  fought VRCFT and other systems). The fix round above (pin layer treatment, the shared pause layers, the
  foreign-ownership check) replaces that design; the one_contact message was also downgraded from an error to a
  warning, since it never actually stopped the build and said so in a way that contradicted `MenuTree.HasErrors`.

### Task 8: three ready made Follow distance starts

`+ More`, after Trigger, gains a `Contact starts` submenu with three one click starts, each building a correctly
placed Follow distance trigger: `Headpat`, `Eye touch` and `Nose boop`. `Editor/Core/ContactStarts.cs` holds all of
it (placement maths and shape guessing), callable without a view so tests drive it directly; the menu items only
call `ContactStarts.Apply`.

**Placement.** Nothing is a fixed number: `scale` is the descriptor's `ViewPosition` height divided by 1.6 (a 1.6 m
avatar gives 1), and every position is computed in avatar space from `ViewPosition`, the avatar's own humanoid
bones (`Animator.GetBoneTransform`, so a rig without a Humanoid Animator counts as having no Head bone) and the
avatar root's own forward and up. A world centre and radius are then converted into `contactAnchor` plus
`contactOffset` (the anchor's own local space) and `contactRadius` exactly the way `ContactSceneHandle` does:
`anchor.InverseTransformPoint` for the offset, the world radius divided by the anchor's own `lossyScale.x` for the
radius. `Headpat` anchors the Head bone above and slightly behind `ViewPosition`, tags Hand and Finger, `Fully on`
at 0.7. `Eye touch` anchors `LeftEye` and `RightEye` when both exist (two triggers, `Eye touch L` and `Eye touch
R`), else the Head bone near `ViewPosition` (one trigger, `Eye touch`); Finger only, `Fully on` at 0.8. `Nose boop`
anchors the Head bone forward and slightly below `ViewPosition`, Finger only, `Fully on` at 0.8. Every trigger is
created with its source, mode and flat contact fields set directly (no `when` groups), the same shape
`MenuEntry.IsFollowDistanceTrigger` already reads for a trigger with no groups yet.

**Shape guess.** Each start adds one blend shape row on the descriptor's `VisemeSkinnedMesh`, else its eyelid mesh,
else the skinned mesh with the most blend shapes under the avatar. The row's shape is the first name, compared
without case, from a fixed candidate list per part (closed eyes, left eye with a fallback to closed eyes, right eye
the same, mouth open), left empty when nothing matches, weight 100, mode left at whatever a new row gets (Fixed).
If the guessed shape happens to be one the descriptor itself drives (an eyelid or a viseme or jaw flap shape), task
7's own tracking pause already handles it at build; nothing extra is added here.

**Names and Undo.** A start's entries take the next free name (`Headpat`, `Eye touch`, `Eye touch L`, `Eye touch
R`, `Nose boop`, else the same with " 2", " 3" and so on) against every `AvatarDoc` component's entries under the
avatar, the same way `OutfitSetBuilder.UniqueGroupName` avoids a repeated exclusive group name. A start that makes
more than one trigger is one Undo group (`Undo.IncrementCurrentGroup` / `Undo.CollapseUndoOperations`, the same
shape `EntryEditing.AddGateSwitch` and `OutfitSetBuilder.Apply` already use), refused the same silent way every
other add is while locked. An avatar with no Head bone refuses the start instead, with `ad.error.contact_start_no_head`
naming what is missing and what to do.

`Tests/Editor/TestAvatar.cs`'s `AddHumanoidRig` gained an `includeEyeBones` parameter (`LeftEye` and `RightEye`,
each a small child of Head) and a new `AddBodyShapes` helper that appends named blend shapes to the default Body
mesh without disturbing its existing `Blush` and `Eye_Big` shapes, so existing tests see no change.

## Simple and Advanced (asked 2026-10-08, plan 13 task G)

The creator asked for "a way to make a simple version of the component, where we can switch between simple and
advanced mode, at the very top". From mockups they chose the beginner set, keeping `+ Outfit set` and dropping
`+ Contact`, remembered per person, with Advanced as the default so nothing changes for anyone who never presses it.

**The switch.** A two part `SegmentedChoice`, `Simple | Advanced`, is the first child of `AvatarDoctorView`, above the
lock banner and the header, so the inspector and the window both have it. Its tooltip is the promise the whole mode
keeps: "Simple shows only what most avatars need; Advanced shows every tool. Nothing you made is hidden."

**One key per person.** `ViewModes` (`Editor/UI/ViewMode.cs`) keeps the mode as the string `Simple` or `Advanced`
under `DocMe.AvatarDoctor.Mode` in the view's `IViewPrefs`: one key for every AvatarDoc, never one per component and
never in the scene. Nothing is stored until the first press. A view built without a store (every test) uses an
in-memory one, so a test never touches the real key.

**Switching changes what is shown, nothing else.** `AvatarDoctorView.SetMode` saves the choice in its own store,
applies it to the header, the tree, the detail panel and the wheel, and raises the static `AvatarDoctorView.ModeChanged`.
The event carries no mode. It is a signal to read your own store again: a view listens only while it is on a panel,
reads its store when it comes back on one, and follows when the stored choice differs. Only the pressed view saves.
That keeps a test, whose views use memory stores, from reaching the creator's own inspector or window, which read the
real EditorPrefs. No plan is built, nothing is written to the component and nothing is recorded for Undo.

**Simple hides tools, never data.**
- Left out of Simple: every toolbar add but `+ Toggle`, `+ Radial`, `+ Submenu` and `+ Outfit set`, `Capture icons`,
  `Convert`, `Wheel` and the tag filter; the header's `Component settings` fold; on a toggle, radial or submenu the Style
  row, Rich text tags, Exclusive group, the `Builds` line, the scene preview control and the Advanced fold, and on a
  toggle or radial the Materials, Animations and Follows sections; `+ Add effect` offers Objects, Blend shapes and
  Properties only (Blend shapes and Properties on a radial, which has no Objects; since plan 14 task P,
  `EntryDetailView.SimpleOffers`). The Properties section hides while empty and shows once a row is added, as in
  Advanced. A toggle's Blend shapes section is always shown, with its Capture.
- Every other kind of row keeps its whole panel in both modes. In Simple it opens with one note, "This is an advanced
  item; switch to Advanced to add more like it.", except presets (`+ Outfit set` builds them when it mixes freely) and
  the automatic More pages.
- The exceptions that keep the promise: a part shows while it holds something. A section with rows, a name with a
  colour, bold, italic or hand written tags (Style row and Rich text tags), a toggle in an Exclusive group, an Advanced
  fold whose dot rule says a value differs, and a `Component settings` fold with a changed Install into, Install as,
  Parameter prefix or Platform. A new part like this appears at once. Taking it away is decided only when the panel is
  built for another entry or another mode (the header: when the mode changes): a part already shown stays until then,
  so the part the creator is using does not vanish when they put its last value back.
- The tree is untouched: every trigger, override, preset, grabbable, animator, menu asset, avatar menu row and foreign
  row stays visible and selectable, and the right click menus are the same in both modes.

**The paused tag filter.** Simple has no Filter button, and ticked tags hide rows, so a filter ticked in Advanced
stops narrowing the tree while Simple is on (`MenuTreeView.TagsApply`). The ticks are kept and apply again in Advanced.

**The hidden wheel.** The `Wheel` button is a tool, and the window shows the wheel by default, so Simple hides the wheel
itself as well (`AvatarDoctorView.UpdateWheel`). The wheel preference is untouched and comes back with Advanced.

**Where each part lives.** `MenuTreeView.SimpleAddNames` and `ModeAddIndexes` (the adds and `+ More`, which fits the
mode's list alone), `EntryDetailView.Trimmed` and `IsAdvancedItem` (the panels), `NameStyleRow` with `compact`,
`AdvancedFold.HiddenWhileDefault`, `HeaderView.SettingsNotAtDefault`.

**Later tools must decide how they behave in Simple.** A new tool, row, section or setting has to say, when it is
added, whether Simple hides it as a tool or shows it while it holds something, and the rule above holds: Simple never
hides what the creator made. `SimpleNeverHidesExistingData` and `SimplePanelsShowOnlyTheBasics` in
`Tests/Editor/SimpleModeTests.cs` are the place to add the case.

## Material properties on triggers (asked 2026-10-09, plan 14 task P)

The creator asked to animate material properties through "+ Add effect" on triggers and in Simple mode.

**Which entries build them.** `MenuEntry.BuildsProperties` is the one predicate: a toggle, a radial, and a trigger
that holds effects of its own (a Toggle, Parameter, Contact or PhysBone source, in Hold or Fade). A blend shape link
and a flip trigger do not (blend shape triggers get Properties in task S, and since then only a flip trigger does not). `HasEffects`, the model's property
warnings (`ad.warn.property_missing`, `ad.warn.property_differs`), the shared target report, the preview and the
panel's "+ Add effect" all ask it. `AvatarPlanCache.AssetsReadBy` already read every entry's property materials.

**Hold: Off rests a property in the final pass.** `TriggerBuilder` builds the On clip with the row's value as before,
but takes the property values out of the Off clip (`ClipContent.TakePropertyWrites`) and records each channel as a
`PinLayerState.PropertyRest` under the trigger layer's name. `AnimatorBuilder.ReleasePinsToRest` hands them back
with the same rule a pin's off state uses (`ReleaseLowest`): nothing is written while some other layer owns the
binding (`SomeNonPinLayerOwns`, which skips pin layers and every layer that recorded the binding, so the trigger's
own layer never counts as its owner); otherwise the lowest recorded layer gets the rest curve, an ordinary layer (a
Hold trigger's) before any pin layer, since `MovePinLayersLast` puts every pin layer above the ordinary ones right
after. Two Hold triggers on one property: the earlier one rests it, the later one's Off stays empty, and the later
one wins while both fire. A caller without a `PinLayerState` keeps the values in Off, as before. The ownership rule
only changes the result on a Write Defaults off avatar: on Write Defaults on the trigger's Off state writes the
property's default every frame whether it carries a curve or not, so a toggle below it that moves the same property
does not show while the trigger rests. A toggle's own main layer still writes its property rest unconditionally.

**Fade.** Near blends every property row from the off pose to the on pose through `WriteToggle(ShapeSet.All)`,
unchanged. Far hands its property values to `PinLayerState.PropertyRests` the same way (`TouchBuilder`), so the
distance layer, last in FX and always Write Defaults off, never masks a toggle that moves the same property.

**Preview.** `PreviewModel.WriteSwitch` writes a trigger's property rows like a toggle's. When a control or an
earlier trigger already wrote the property in that pass, the built state of a trigger forced Off carries no curve
for it: a Fade trigger, or a Hold trigger when the avatar's Write Defaults decision is off
(`AnimatorBuilder.DecideWriteDefaultsForAvatar`, cached), leaves the property alone, and a Hold trigger on a Write
Defaults on avatar writes the material's own value (the default Unity writes there), whatever Off value the row has.
Triggers are written in the order the build resolves them: the other triggers, then Fade triggers (their layers end
up last), then blend shape links (`ClipRewriter.Rewrite` runs after every layer is built, Fade's Near clips
included), each group in tree order.

**What this builds.** A Hold trigger now counts its own FX layer: before, a condition trigger counted none and an
objects-only Hold trigger said it built nothing. A toggle or a Hold trigger with pins counts its pin layer, and its
main layer only where the builder makes one: when the entry has no pins, or `WriteToggle(ShapeSet.MainOnly)` (plus a
toggle's own custom clips) has something besides the pins to write (`WhatThisBuildsModel.MainAndPinLayers`).

**Simple.** "+ Add effect" on a toggle offers Objects, Blend shapes and Properties; on a radial, Blend shapes and
Properties.

## Smooth blend shape triggers and requirements (asked 2026-10-09, plan 14 task S)

The creator asked for more conditions on blend shape triggers and Fade contacts, "and for the blend shape make it
smooth if possible". Approved: blend shape triggers blend smoothly, existing ones too, with a `Switch` choice for the
old hard switch; blend shape triggers and Fade contacts get `+ And` requirements; blend shape triggers get Properties.

**Data.** `MenuEntry.linkStyle : BlendShapeLinkStyle` (`Smooth = 0`, `Switch = 1`), `smoothStart` (0) and
`smoothFull` (100), in shape weight. Smooth is 0, so every stored trigger loads as Smooth with its threshold, mode
and effects untouched; the threshold becomes the point where objects and material slots swap. `EffectiveSmoothStart`
and `EffectiveSmoothFull` are what the build, the preview and "What this builds" read: both clamped to 0..100, a Full
left at 0 (the field Unity can skip the initialiser for) read as 100, Full kept at least 1 above Start. Crossed stored
values raise `ad.warn.smooth_range`.

**Requirements: the flat source fields stay, the requirements are the conditions after the driver.** Of the two
models the brief offered this is the smaller one. A blend shape trigger keeps its shape in `sourceRenderer` and
`sourceShape` and its requirements in `when[0]` (its first group holding a condition); a Fade contact already kept
its driving contact first in that group, so its requirements are simply the rest of that group. Making the shape a
fifth condition kind would have touched every switch on `WhenConditionKind` (rows, words, touch description, removal,
validation) and needed a migration for stored triggers; this needs none, since a blend shape trigger could never get
groups before (the panel never offered them). `MenuEntry.HasDriver` (a blend shape trigger, or
`IsFollowDistanceTrigger`), `RequirementGroupIndex`, `Requirements()` and `IgnoredConditionCount` are the one place
this is decided. `EffectiveGroups` of a blend shape trigger is its requirement group alone, so everything that reads
a trigger's conditions (removal's parameter usage, the summary's needs and touch counts, "What this builds", touch
description) sees requirements with no special case. `EntryEditing` gives a blend shape trigger an empty first group
(never a condition made from its flat fields) and never folds it back into flat fields. Conditions in any other group
are kept and ignored with `ad.warn.requirements_one_card` (blend shape) or `ad.warn.follow_distance_one_contact`
(Fade). The card holds at most `ConditionLogic.MaxPerGroup` rows with the driver included, so `+ And` switches off
after three requirements, with its own tooltip (`ad.ui.when.add_and.limit_driver.tip`: the old one points at `+ Or`,
which these triggers do not have); the model raises `ad.error.trigger_too_complex` past that. `Only while` is one
more requirement on both; `ad.error.gate_on_link` is gone.

**Verified: where an animated animator parameter works.**
- Unity's C# reference has the binding form, not the evaluation: an Animator level float is recorded as path `""`,
  type `Animator`, the property name as the name (`AnimationRecording.ProcessRootMotionModification`). Which
  controller's parameter such a curve writes is native code.
- VRChat's own wiki (Animations, the AAP section): an animated parameter "is only applied within the same Animator
  Controller", an AAP set in FX "does not propagate to the Gesture layer", the animated value is forced even while no
  state sets it, and parameter drivers cannot override it.
- NDMF: `VirtualClip.EditPaths` leaves `(Animator, "")` bindings alone on every path rewrite (`VirtualClip.cs` line
  318), so a merge with a path prefix never moves the copy; `AnimationIndex.GetClipsForBinding` covers the clips of
  every controller, so FX membership is filtered through `AllReachableNodes` of FX.
- Modular Avatar uses the same technique (`ReadablePropertyExtension`: `SetFloatCurve("", typeof(Animator), proxy,
  curve)` on every clip of a binding, the parameter declared Float) and reads it from FX only.
- VRCFury (read only): an AAP driven partly by a blend tree mixes in the parameter's default value
  (`FixPartiallyWeightedAapsService`), and an AAP must never be a synced expression parameter
  (`DisableSyncForAapsService`).

So the copy reaches every layer of FX, blend trees and transitions alike, and nothing else: a clip of Base,
Additive, Gesture or Action cannot drive the trigger. The old rewrite did reach those clips (it wrote effects into
every controller's clips); now `ClipRewriter` leaves them alone and `ad.warn.link_clips_outside_fx` names them. The
parameter's default is the source shape's weight on the build clone, so wherever a Write Defaults on layer falls back
to the default, the copy falls back to the same value the shape does.

**Mirror units: shape weight, 0 to 100.** The copy is the shape's own curve, keys and tangents and wrap modes alike,
so it equals the shape's weight at every moment of every clip, and Start, Full and the threshold map straight onto
tree thresholds and transition thresholds with no conversion anywhere.

**Build.** The planner names the copy `<prefix>/Shape/<trigger name>` in `TouchBuilder.Describe`, unique against
every existing and planned name (`TouchParameters.ShapeMirrors`), and resolves requirements and the gate the way any
condition is resolved (`TriggerPlanner.PlanRequirements`: `ConditionLogic.Enter` and `Leave`, so every requirement
joins every way in and adds one way out; none gives one empty way in and no extra way out). `LinkLayerBuilder`
builds one FX layer per blend shape trigger, `AD: <name> (shape)`, in the Transforming pass like Fade's: every state
Write Defaults off, `LayerPriority(int.MaxValue)`, named into `pinLayerNames`, so `TurnPinLayersWriteDefaultsOff` and
`MovePinLayersLast` treat it as they treat Fade's. The copy is declared in FX as Float and never added to the
expression parameters. The final pass (`FinishAfterOtherTools`), after `ReleasePinsToRest` and
`OverrideBuilder.RunFinal`, runs `ClipRewriter.Rewrite`: every FX clip that then moves the shape (Modular Avatar's and
VRCFury's included, and the rest curves the release pass just added) gets the copy, and with `Replace` the shape's own
curve goes once every trigger has copied it. This is later than the old rewrite, which ran in the Transforming pass
before Modular Avatar and so never reached a merged clip.

**The layer arrangement: one layer, up to three states.** The rest state (`Far`, `Off` in Switch) writes objects and
material slots at their off values; shapes and properties carry no curve there and go to the release pass under the
same rule as Fade's Far (`PinLayerState.Bindings`, `PropertyRests`), and a shape the descriptor blinks or speaks with
is never registered. A driven state below the switch point (`Near`, or `Following` in Switch when a Follow row moves
there) and one at or above it (`Near on`, or `On`) hold the same Simple1D tree on the copy and differ only in objects
and material slots, which cannot blend: a tree would pick a slot from its heavier child and blend `m_IsActive` as a
float, so a swap inside the tree would happen between children, not at the threshold. A state that can never be
reached is left out (Switch without Follow rows is exactly `Off` and `On`). The tree has a child at every weight where
some row bends (Start and Full; 0, 100 and `100 / multiplier` for a Follow row), so between neighbours every row is a
straight line and the blend is exact. Transitions: the rest state leaves at the lowest weight anything moves at plus
0.01 (float noise on a resting shape never wakes it), the switch sits 0.001 below the threshold (so exactly at the
threshold counts as above, as `EffectMath.LinkAbove` has it), every requirement joins every way in, and every driven
state has the shape's own way back to rest first, then one way out per requirement. One layer keeps the requirement
transitions in one place and the release rules on one rest state; two layers (one blending, one switching) would
have repeated every requirement's ways in and out in both.

**Add and Replace keep their meaning.** `Add`: the shape keeps moving as its animations say and the effects come on
top. `Replace`: the shape's own curve leaves every FX clip that moves it, so only the effects show, while the copy
still follows where the shape would have been. A Replace trigger with no effect it can build copies nothing and
keeps the shape's curves (`ad.warn.replace_kept_source`, unless another Replace trigger on the shape removed them).

**Fade contacts.** `TriggerPlanner` makes a `TriggerKind.Fade` plan carrying only the requirements, and
`BuildFollowDistanceLayers` adds them to its two transitions: Far to Near needs the receiver above 0.001 and every
requirement; Near goes back to Far when the receiver drops below 0.001 or any requirement fails, one transition each.
Without requirements and gate it builds exactly as before. A requirement contact gets its own receiver
(`TouchBuilder.DescribeConditions`), with its own Touch or Closer than, never shared with the driving contact; only
the driving contact shows `Fully on at` (`TouchPanel.IsFadeDriver`).

**The pause follows the effects, not the hand (fix round 1).** Reading the receiver float, the shared pause layers
paused blinking whenever a hand was near, even while a requirement failed and the effects rested, and even for a
Fade contact whose layer was never built because a requirement did not resolve; a blend shape trigger with an
eyelid or viseme effect got no pause at all. Now every driven layer whose effects move a shape the descriptor
blinks or speaks with (`TouchBuilder.FollowDistanceTracking`, which skips a blend shape trigger's own shape) gets
one animator only Float, `<prefix>/Pause/<trigger name>`, named in `TouchBuilder.Describe` with the mirrors
(`TouchParameters.PauseFlags`). The layer writes it 0 in its rest state and 1 in every state that moves the effects
(Fade's Near children; a blend shape trigger's Near, Near on, Following and On, every tree child included,
`ClipContent.SetAnimatorParameter`), and records it in `PinLayerState.PauseFlags` only when the layer is built.
`BuildTrackingPauseLayers` reads those flags instead of the receivers and runs after `LinkLayerBuilder.Build`;
`BuildPauseLayer` is unchanged (Greater and Less 0.001 on each flag). The driven states can only be reached while
every requirement holds, so the pause follows the effects exactly, a skipped layer contributes nothing, and blend
shape triggers get the pause at no extra cost.

**Fix round 1, the other changes.**
- `AnimationUsage` also records which clips play only outside FX (`ClipsOutsideFx`), from the same memoised scan,
  and `AvatarPlanCache.Build` raises `ad.warn.link_clips_outside_fx` for each blend shape trigger from it, so the
  inspector says it before a build. The usage index reads assets only, so "no FX clip moves this shape at all" is
  a build warning, `ad.warn.link_no_fx_clip`, raised by `ClipRewriter.Rewrite` when a trigger copied into no clip.
- `ClipRewriter.Rewrite`, the last step, also sets the copy's default from the shape's weight at that moment, since
  Shapes on the outfit pieces runs after AvatarDoc's own pass and can still move a weight.
- `AvatarDoctorParameterProvider` lists every copy and every pause flag as an animator only Float, never synced
  (`ParametersWriter.AddProvided` skips AvatarDoc's own entries, so the names are never made unique against
  themselves).
- Switching a blend shape trigger that has requirements to another source puts the picked source in as the first
  row of its group (`EntryEditing.PutFlatSourceFirst`), in the same Undo step, since that group then becomes the
  whole rule. A blend shape trigger's leftover extra cards are captioned "Kept but not used".

**Inspector.** The blend shape panel: `When`, its card (`ConditionGroupsPanel` with the shape as `driverRow`, then a
row per requirement, then `+ And`), `Style` (a `SegmentedChoice`), `Starts at` and `Fully on at` in Smooth, the one
threshold slider labelled `Swaps at` in Smooth and `Switch at` in Switch, `Mode` (Add or Replace), a note on the copy,
`Only while`, and `+ Add effect` with Objects, Blend shapes, Materials and Properties (no Follows). A Fade contact
offers `+ And` (in its card once it has one) and `Only while`, never `+ Or`. Both modes show the whole panel, as
before for triggers.

**Preview.** `PreviewModel` gives a blend shape trigger's rows their value at the source's current weight
(`EffectMath.LinkShapeWeight` and `LinkPropertyValue`, by style) and, where the built layer would rest, leaves a
shape or property another writer set this pass alone. A toggle requirement and the gate are read like any control;
a parameter, contact or PhysBone requirement counts as met, and `ad-preview-unchecked` says so under the preview
row. A Fade trigger forced On also rests while a toggle requirement is off.

**Words.** "What this builds" counts the layer and adds "blends from Start to Full" or "switches at", and "a hidden
copy of the shape's weight, no synced bits"; the summary lists the copy under `Animator only parameters`; the rule
sentence reads "When "Body/Blush" moves and "Hat" is on" (Fade: "When a hand comes near "Head" and ...");
`ad.warn.link_source_tracked` warns when the shape is one the descriptor blinks or speaks with, which no clip moves.
