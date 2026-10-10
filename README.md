# AvatarDoc

AvatarDoc is a Unity editor tool that builds a VRChat avatar's expressions menu for you: toggles, radials,
submenus, exclusive groups, material swaps, menuless triggers, overrides that hold a blend shape at one value in
every animation that uses it, and presets that put several other controls into one state with a single button, all
described in one component and generated at upload time.

## What it does

You add one AvatarDoc component and describe a menu of toggles, radials and submenus, each one switching
GameObjects, blend shape weights or material slots. Toggles can join an exclusive group so only one member is on at
a time. Triggers copy the same effects onto another toggle, a parameter condition or an animated blend shape,
without adding a button to the menu. Nothing is baked into the avatar while you edit it: the component only holds a
description of the menu, and AvatarDoc generates the actual FX layers, expression parameters and expressions
menu each time you upload or enter Play mode. It builds through NDMF (the build framework that applies tools like
AvatarDoc and Modular Avatar to a copy of the avatar when you press Play or upload), so it runs cleanly next to
Modular Avatar on the same avatar. It can also rename, re-icon, hide or move a menu item the avatar already has,
including one VRCFury or Modular Avatar added, and it can convert a toggle built with VRCFury, Modular Avatar or a
hand made animator layer into its own menu; see Editing the existing menu, Convert and Using it with VRCFury and
Modular Avatar below.

## Requirements

- Unity 2022.3
- VRChat SDK 3.10 or newer
- NDMF 1.14.1 or newer
- Modular Avatar is optional; AvatarDoc does not need it.

## Install

Copy the `AvatarDoctor` folder into your project's `Assets` folder. A VRChat Creator Companion listing is planned
for a future release; until then, a manual copy is the way to install it.

## Quick start

1. Add the `DocMe/AvatarDoc` component to any object under the avatar.
2. With nothing on its menu yet, the inspector shows a welcome card with three ways to start: **Make a toggle**
   opens the avatar's own object picker and turns whatever you pick into a toggle; **Convert from VRCFury or Modular
   Avatar**, shown only when the avatar actually has something either tool could install, opens the Convert window
   with those items already listed; **Start an outfit set** opens the wizard that builds a whole outfit selector
   from a handful of objects at once. A **Quick start** link under the three opens this section by itself, and a
   line under the card still lets you drop objects from the Hierarchy straight onto the tree to make one toggle per
   object, the same as before.
3. The welcome card is gone once the tree has an entry, and that entry opens selected and ready to edit.
4. Pick an install path in the header's `Component settings` fold if the entries should land somewhere other than
   the avatar's root menu (Simple mode leaves that fold out; see Simple and Advanced below).
5. Press Play with Gesture Manager to try it in the editor, or upload as usual. AvatarDoc builds the menu either
   way.

## Simple and Advanced

A switch with two parts, `Simple` and `Advanced`, sits at the very top of the AvatarDoc inspector and of the AvatarDoc
window, above the status line. Advanced is the default and shows every tool, so nothing changes until you press
`Simple`. The choice is yours, not the component's: every AvatarDoc component follows it, in the inspector and in the
window, it is remembered between sessions, and pressing it in one place switches every open AvatarDoc at once.

Simple shows only what most avatars need:

- **The toolbar** keeps `+ Toggle`, `+ Radial`, `+ Submenu`, `+ Outfit set` and the search box. If the bar is too
  narrow for all four, `+ More` holds the rest of those four. The other adds, `Capture icons`, `Convert`, `Wheel`
  (and the wheel itself) and the tag filter are left out.
- **The header** keeps the status line, `Show`, `Summary` and the memory bar. The `Component settings` fold is left
  out.
- **A toggle** shows Name, Icon, Default on and In menu, then the Objects section and the Blend shapes section (with
  Capture and Hold the rest, even before the toggle has a shape, and the lines of any rule it has). Its
  `+ Add effect` offers those two and Properties (a material value, such as a hue shift or a glow).
- **A radial** shows Name, Icon, a Default slider in place of Default on, and In menu, then the Blend shapes section
  (with Capture). A radial has no Objects section, so its `+ Add effect` offers Blend shapes and Properties.
- **A submenu** shows Name and Icon, and how many of its slots are used.
- The Style row, Rich text tags, Exclusive group, the `Builds` line, the scene preview control and the Advanced fold
  are left out of all three.
- **Any other kind of item** (a trigger, an override, a grabbable, a merged animator, an installed menu asset, an item
  of the avatar's own menu or one another tool added) shows its whole panel, with one line at the top: "This is an
  advanced item; switch to Advanced to add more like it." A preset (`+ Outfit set` builds presets when it mixes
  freely) and the `More (page N)` rows a menu gets by itself past eight items show their whole panel without that
  line.
- The welcome card drops its Convert start, and the first-use tips use words that fit Simple. Right click menus are
  the same in both modes.

Simple leaves out tools, never what you made. The tree shows every item. A toggle that already uses Materials,
Animations or Follows still shows that section, the same as a Properties section that has rows; a name that has a style still shows its Style row; a
toggle in an Exclusive group still shows the group; the Advanced fold and the `Component settings` fold still show
while a setting inside is not at its default, with the same dot as in Advanced. A part that shows because it holds
something stays until you select another item or switch the mode (the `Component settings` fold stays until you
switch the mode), so the part you are using does not vanish when you put its last value back. A tag filter you ticked in Advanced pauses while Simple is on, since Simple has no filter
button to clear it with, and narrows the tree again when you switch back. Switching only changes what is shown: it
writes nothing to the component and builds nothing again.

## Finding your way around

The header, the tree and every panel now show the same status in more than one place, so a problem is never a
surprise at upload.

- **Ready to upload**: the header's first line reads `Ready to upload ✓` once nothing is wrong, or a count such as
  `3 problems` (or `2 warnings` once every error is gone) with a `Show` button beside it. Pressing `Show` selects
  the next problem's row, reveals it in the tree, and focuses the field the message is about when it names one;
  press it again to step to the next. The `Summary` button in the title row carries the same count as a small
  badge, so you can see it without opening the header's own line.
- **Write Defaults**: right under the ready line, a note reads `Matches your avatar: Write Defaults on` or `...off`,
  the setting AvatarDoc's own layers will use. AvatarDoc's own rule is simple: it matches the avatar's own layers
  when they agree, and if the avatar's own layers disagree with each other at all, AvatarDoc builds with Write
  Defaults on regardless of which side has more states. A warning explains this when your avatar's own layers are
  mixed.
- **The memory bar** stays visible under those two lines, above the fold described next: the avatar's own synced
  bits, AvatarDoc's added share, and how many of VRChat's 256 bits are still free.
- **Component settings**: `Install into`, `Install as`, `Parameter prefix` and `Platform` are tucked behind one
  `Component settings` fold, closed by default. A dot on the fold's header shows while it is closed and one of
  those settings is not at its default value, and disappears once you open it or put the setting back.
- **The toolbar** keeps only `+ Toggle`, `+ Radial`, `+ Submenu` and `Convert` visible, next to a `+ More` button:
  `+ Trigger`, `+ Override`, `+ Preset`, `+ Grabbable`, `+ Animator`, `+ Menu asset`, `+ Outfit set` and
  `Capture missing icons` live there instead, each with a one line description. `Wheel`, the search field and the
  tag filter button described below sit at the toolbar's other end.
- **Row actions and the drag grip**: select a row you own and small buttons appear at its right end: `Move to`,
  `Duplicate` and `Delete` for an entry, `Duplicate` and `Delete` alone for a trigger or an override, `Convert to
  AvatarDoc` alone for a foreign row. Right click still opens the same menu. Hovering any row shows a drag grip at
  its left edge you can use to pick it up instead of dragging the row by its name.
- **Dragging within the tree**: while dragging a row, its position left or right decides which submenu it lands in,
  the same way Unity's own Hierarchy works. Move the pointer left to step the row out of the submenu it is
  hovering, one level per step, down to the root menu; move it right to nest it one level deeper into whatever it
  is hovering.
- **The tag filter**: the filter button beside search offers every tag actually present on the tree right now,
  grouped as `Kind`, `From`, `State` and `Platform`. Ticking more than one tag in the same group is OR, either one
  matches; ticking tags from different groups is AND, every group you used has to match. A submenu kept visible
  only because something inside it still matches is dimmed, the same way search already dims it. Dragging a row
  while a tag filter is active works the same as while searching: a note asks you to clear the search field, the
  tags, or both, whichever is actually active, before you can move anything.
- **Panels that grow with you**: a new toggle or radial starts small, showing only the section it actually needs
  (Objects for a toggle or a trigger, Blend shapes for a radial); a `+ Add effect` button lists every other kind of
  effect the panel can carry, one line each, and picking one opens that section. Removing an effect's last row
  hides the section again. Every own entry's panel also has one `Advanced` fold at the bottom, Platform always and,
  for a toggle or radial, also Parameter, Saved, Synced and Blend time, closed by default, with the same non
  default dot the Component settings fold uses.
- **Costs where they are added**: the `Synced` switch on a toggle or radial shows what it costs right beside it,
  `+1 bit`, `+8 bits` or `0 bits`. Every own entry's panel has a `Builds: ...` line under its name summing up what
  it actually adds when it builds (menu controls, synced bits, FX layers, contacts, PhysBones, constraints), or
  explaining why it builds nothing yet. A submenu's row in the tree shows its own slot count, such as `7 of 8`,
  once it is nearly or already full, and an automatic overflow page is labelled `More (page 2)`, `More (page 3)`
  and so on in the tree, so what you see there is what gets built.
- **First-use tips**: the first time you open a panel of a given kind, or the header, a one line tip explains what
  it is for and names the first thing to do; its close cross dismisses it for good. The `Tips` switch inside the
  Component settings fold turns every dismissed tip back on.

## The component

- **Install path and mode**: `installPath` is a submenu path such as `Outfits/Summer`; empty means the avatar's
  root menu. `Append items` adds the entries directly at that path; `As submenu` wraps them in one new submenu
  there, named by `Submenu name` (default the GameObject's name) with an optional `Submenu icon`.
- **Parameter prefix**: default `AD`. Automatic parameter names are `<prefix>/<name>`, so changing the prefix
  changes every automatically named parameter at once.
- **The memory bar**: shows the avatar's own synced bits, AvatarDoc's added share, and how many of VRChat's 256
  bits are still free, or how far over the limit the current menu is.
- **The tree's toolbar**: `+ Toggle`, `+ Radial` and `+ Submenu` add entries at the current selection, and `Convert`
  opens a window that converts toggles built with VRCFury, Modular Avatar or a hand made animator layer into
  AvatarDoc entries, described under Convert below; see Finding your way around above for how these sit next to a
  `+ More` button. From that `+ More` button: `+ Preset` also adds at the current selection, `+ Trigger` adds to the
  trigger list at the bottom and `+ Override` to the override list below it; `+ Animator` adds a merged animator,
  described under Merged animators below, and dragging a `RuntimeAnimatorController` asset from the Project window
  onto the tree does the same; `+ Menu asset` adds an installed menu asset, described under Menu assets and
  parameters below, and dragging a `VRCExpressionsMenu` asset onto the tree does the same; `+ Outfit set` opens a
  wizard that builds a whole outfit selector at once, described under Outfit sets below; `Capture missing icons`
  captures an icon, the same way the outfit wizard's own `Capture icons` does, for every entry under this component
  that has pieces but no icon yet, so entries you add or rename afterwards can get one too; it is also available
  from a right click anywhere in the tree. Back in the toolbar itself, `Wheel` shows the wheel view described
  further down, a search field filters the tree down to rows whose name matches what you type, keeping a submenu
  visible whenever something inside it still matches, and the tag filter beside it does the same by tag, both
  described under Finding your way around above.
- **The look**: a dense, dark console style. A field with only two choices, such as `Shown`/`Hidden` or
  `Fixed`/`Pin`, shows as a small button pair instead of a dropdown.
- **The tree starts collapsed**: submenus and the `Triggers` and `Overrides` group rows begin closed, so open the
  ones you are working in. Each component remembers on its own which rows you left open and reopens them the same
  way next time you select it, even after restarting Unity. That memory belongs to the component where it sits, so a
  component you copy onto another object, or move, starts collapsed again. Adding, duplicating or moving an entry
  opens the tree down to its new spot so you can see where it landed, and so does clicking a wedge in the wheel or
  dropping objects from the Hierarchy onto the tree. While the search field has text, or a tag filter is active,
  every row is open, so nothing that matches can hide inside a closed submenu; clearing the search and the tags
  puts back the rows you had open yourself.
- **Several components on one avatar**: any number of AvatarDoc components can exist under the same avatar, each
  installing its own entries at its own install path; they merge into a single menu. Two components that both use
  `As submenu` with the same name at the same path merge into one submenu instead of creating two, and a `Submenu`
  entry whose name matches a submenu the avatar's own menu already has at that place merges into that one rather
  than sitting beside it. Selecting an
  entry that belongs to a different component shows a note naming that component's GameObject and a
  `Select that component` button that jumps to it; that entry can only be edited from its own component.
- **Disabled components**: a disabled AvatarDoc component still shows in the inspector, so you can keep editing it,
  but the build skips it entirely, and so does the plan the inspector shows: its rows carry a `disabled` chip and are
  dimmed like rows left out on this platform, `What this builds` says "Builds nothing: this component is disabled.",
  and it takes no parameter name, no synced bits and no menu slot. Its group settings, declared parameters and menu
  edits count again once you enable it.
- **The avatar's existing menu**: menu items the avatar already had before AvatarDoc was added show up in the
  merged tree too, with an `existing` chip; items VRCFury or Modular Avatar add show up as well, with a `VRCFury`
  or `Modular Avatar` chip. AvatarDoc never changes the original item or the asset behind it, but you can rename
  it, give it a different icon, hide it or move it into another submenu from its own panel in the inspector; see
  Editing the existing menu below. You can still install an AvatarDoc submenu into one of them by giving its
  path as the install path.

## Toggles, radials and groups

A toggle or radial can target any mix of:

- **Objects**: a GameObject's active state, shown when on by default or hidden when on if you flip that.
- **Blend shapes**: a weight on a renderer's blend shape (a toggle uses one weight; a radial blends between a `Min`
  and a `Max` weight as its value moves from 0 to 1).
- **Material slots**: the material in a renderer's slot while the control is on.
- **Material properties**: a shader property on all of a renderer's materials, such as a hue shift or a fade
  amount, held at one value while a toggle is on (a radial blends between a `Min` and a `Max` value, the same as a
  blend shape). Pick a renderer, then a property from a dropdown listing everything its shaders expose (floats,
  ranges, colours and vectors), or type one by hand through `Other...`. The value while a toggle is off comes from
  the first of the renderer's materials that has the property, and is written to all of them, so a renderer whose
  materials hold different values for it is flattened to that first value while the toggle is off: a warning says
  so when that happens. A property none of the renderer's materials have is skipped with a warning, unless a
  material slot on the same control swaps in a material that does have it (the dissolve case), in which case that
  material's value is the off value. It shows in the scene preview like a blend shape or a material slot does.
  Triggers can animate material properties too; see Triggers below.
- **Custom animations**: your own animation clip, merged into the control's generated one, with a `Path root` field
  for a clip made inside a prefab (leave it empty when the clip's paths already start at the avatar root). On a
  toggle, anything the clip animates that AvatarDoc is not already writing for that control is carried over as
  is; a binding both write keeps AvatarDoc's own value. Turning the toggle off restores the scene's own value for
  everything the clip touched, so a Write Defaults off avatar still returns to rest. A radial with a clip plays that
  one clip with the radial's own value as its position, instead of blending two states. Custom animations are not
  shown by the scene preview; the panel says so under the list.

Every blend shape row on a toggle, a member of an exclusive group, or a condition-driven trigger also carries a
two way switch, `Fixed` or `Pin`. `Fixed` (the default) writes the row's own weight while the control is on and the
shape's captured scene value while it is off, which also stops any of that shape's own animations from moving it
during that time. `Pin` instead writes the row's weight only while the control is on; while it is off, AvatarDoc
writes nothing for that shape at all, so whatever already animates it elsewhere on the avatar (gestures, face
tracking, another toggle) drives it again as normal, and if nothing else animates it, the shape simply keeps
whatever value it last had. Radials and a blend shape trigger's blend shape effects do not offer this choice; a
blend shape trigger's own `Fixed weight`/`Follow source` field is still a plain dropdown, since it has room for
the longer wording. Once a shape is already animated somewhere on the avatar, its row also shows a chip such as
`3 clips`, the same count an override's "Animated in *N* clips" list would show for that shape. That chip appears
on every blend shape row that has one, on toggles, radials, blend shape triggers and overrides alike.

Pin mode builds each control's pinned shapes into their own FX layer with Write Defaults off, so the VRChat SDK may
report a mixed Write Defaults notice on an avatar that uses Pin mode purely because of that extra layer; that
notice is expected and is not a sign the setup is broken.

**Holding the rest of a mesh.** The Blend shapes section of a toggle, and of every trigger with effects of its own,
has two buttons beside `+ Add`:

- `Capture` picks a mesh and adds a row for every shape of it that is above 0 in the scene right now, at that
  weight (in `Pin` mode, or `Fixed` on a blend shape trigger). On a toggle it also adds a rule that holds every
  other shape of that mesh at 0; on a trigger it adds the rows alone.
- `Hold the rest` picks a mesh and adds only the rule, holding every other shape of that mesh at its default: the
  weight it shows in the scene when you upload. If the entry already has a rule for that mesh, it keeps the one it
  has.

Each rule has a few lines under the list:

- `Everything else on <mesh>` switches the rule on or off without removing it. The `×` at its end removes the rule;
  the rows Capture added stay.
- `Hold at` chooses `Default` or `0`. With `Default`, every other shape stays exactly as the scene shows it, so
  whatever else moves it on the avatar (a gesture, face tracking, another toggle) cannot change it while this is on.
  With `0`, every other shape is cleared.
- `Blink and visemes` shows only for a mesh the avatar descriptor blinks or speaks with. Off (the default) leaves
  those shapes free, so blinking and lip sync keep working while the rule holds the rest. On holds them too, and
  VRChat's own blinking or lip sync pauses while the entry is on and comes back as soon as it is off. VRChat ignores
  such a pause for a moment after the avatar loads, so for a toggle that is on by default the pause starts about 5
  seconds after loading. A `Pin` row on one of those shapes, such as the half closed eyes Capture takes from a
  sleepy face, pauses blinking the same way while the entry is on, and leaves the shape to VRChat while it is off.
- A note says how many shapes the rule holds and at what, for example "Holds 37 other shapes at their default while
  this is on."

The shapes the entry has rows for keep their rows' values, and so do the shapes a toggle's own custom animations
move. A blend shape trigger never holds its own shape, and no rule holds a shape an AvatarDoc Shapes component
drives (see AvatarDoc Shapes below): a piece that changes the body while it is worn keeps doing so. A toggle holds
the rest while it is on and writes nothing for those shapes while it is off, the same as a `Pin` row.
A trigger in its usual mode holds them while it fires. A `Fade` contact and a Smooth blend shape trigger blend them
like any row, from their default at the start to the rule's value when fully on: with `Default` nothing visibly
moves, the shapes are simply held; with `0` they fade out. A Switch blend shape trigger holds them while it is on. A
radial cannot have a rule (it has two ends, not an on and an off), and neither can a `Flip` trigger, which has no
effects of its own; both report an error.

A rule adds no expression parameter and no synced bits. On a toggle or a trigger in its usual mode it adds curves
to the layer the entry's pinned rows use; a `Fade` contact puts them in its distance layer and a blend shape
trigger in its own shape layer. A rule above 150 shapes gets a warning, since every one of them is written each
time it turns on. The scene preview
and the wheel's test mode show the held shapes while the entry is previewed on, and the summary lists each rule with
its mesh, its count and what it holds at. Rules made before `Default` existed keep holding at 0, and now leave the
blink and viseme shapes free as well; before, a rule on the face mesh held those too, which stopped blinking and lip
sync altogether.

A `Fixed` blend shape or material property row on a toggle (not a radial, and not while the shape is pinned) also
has a `Set a value when off` switch. Off, the row behaves as described above: the shape or property returns to
whatever the scene had when the entry was made. On, an `Off value` field appears and AvatarDoc writes that value
instead while the toggle is off; turning the switch on for the first time fills the field from the current scene or
material value, so it starts as a faithful capture of what is there now. This is the same mechanism the animator
driven Modular Avatar menu converter (see Convert below) uses to carry over a layer's own rest values instead of
falling back to the scene; setting it by hand works the same way. A pinned shape cannot also have an off value of
its own, since a pinned shape writes nothing at all while the toggle is off: doing both is an error.

A toggle also has a `Blend time` field under its flags row: seconds, 0 to 10, default 0. The switch between its off
and on state takes that long to fade instead of snapping, so a blend shape or material property change eases in and
out instead of stepping. VRChat is expected to keep an object target visible for the whole fade in both directions,
since it treats an animated activation curve above zero as active; check this in game, since it has not been
verified there yet. Blend time only reaches the toggle's own main Off/On transition: a blend shape row set to `Pin`
still switches instantly, since that shape is written to its own separate FX layer regardless, and a toggle that
belongs to an exclusive group also keeps an instant switch, since the group has its own layer too; the field is
replaced by a note when the toggle is in a group. A toggle that is `Default on` starts its layers in the on state,
so it does not fade in at every avatar load; if such a toggle is also `Saved` and you left it off last session,
VRChat restores the off value and the toggle fades out once while the avatar loads.

`Default on` (toggles) and `Default` (radials) set the state at spawn. `Saved` and `Synced` control the expression
parameter's own flags, unless the toggle belongs to an **exclusive group**, whose own saved and synced settings
apply to every member instead. A group is just a shared name typed into `Exclusive group`; any toggle on any
AvatarDoc component that uses the same name joins the same group, and only one member of a group is on at a
time.

By default a control's parameter is named automatically from the prefix and the entry's name. Giving it a custom
name instead can bind it to a parameter that already exists on the avatar:

- If the name matches a parameter the avatar already declares as an expression parameter, AvatarDoc reuses it:
  that parameter's own type, saved, synced and default value apply, and the control's own flags are ignored. No new
  bits are added for it.
- If the name matches a parameter that exists only inside an animator, such as a VRChat built-in parameter, a
  PhysBone parameter or one declared by another tool, the control drives that parameter directly without adding an
  expression parameter of its own.

## Names and icons

Every entry keeps two versions of its name: what you type, styled, and the plain text the build actually uses. The
style row sits under the name field of everything that shows up in the VRChat menu: toggles, radials, submenus and
presets. A trigger or an override has no control in the menu, so it has a plain name field only.

- **The style row**: under the name field, on one line, a colour swatch and `B` and `I` buttons style the whole name at once with the `Tags` switch at the right end; an
  `x` next to the swatch clears the colour. A `Tags` switch exposes the raw text with its tags written out, for
  styling only part of the name by hand. Whenever the raw text is something the row cannot reduce to one colour plus
  bold and italic, such as unmatched or oddly nested tags, or a colour written with three or four hex digits instead
  of six or eight, the swatch and the `B` and `I` buttons disable themselves and a note tells you to keep editing the
  raw text; once your edit becomes recognisable again, the raw field itself keeps showing until you select something
  else and back, or reopen the inspector. Once a name carries any styling, an `In game` line under the row renders it the way VRChat will.
- **What VRChat shows**: the styled name, tags and all, is exactly what AvatarDoc writes into the generated
  toggle, radial, submenu and preset controls, so VRChat's own expressions menu shows the colour, bold and italic
  you set.
  A component installed with `As submenu` carries its `Submenu name` the same way: the control VRChat shows keeps the
  style, while the submenu asset behind it is named in plain text.
- **Where else it shows**: the tree's rows and the wheel's labels draw the styled name too, so the inspector reads
  the way the menu will look. The search field still matches the plain text, so a tag name is never found by it.
- **What the build uses instead**: the parameter name, the FX layer name, install path segments, submenu matching
  and every name in the message tables below all use the name with its tags stripped. Renaming a submenu only moves
  that plain text into other components' install paths, and duplicating an entry adds "Copy" inside the existing
  style rather than after the closing tags.

Every toggle, radial, submenu and preset also has a `Capture icon` button at the right end of its icon field that opens a small window:

- The window shows a live 256 by 256 preview framed by the Scene view camera; move the Scene view around and the
  preview follows. It also redraws when something it shows changes: a piece moved or rotated in the scene, its
  material edited in its own inspector, or the entry or a component on the avatar edited. While nothing changes
  the window draws nothing, so leaving it open costs nothing.
- `Fit` points the camera at the entry's own pieces and fills the frame with them, then freezes that framing until
  you press `Fit` again or close and reopen the window; until then, moving the Scene view no longer changes the
  preview.
- `Background` chooses `Transparent` or `Solid colour`; a colour field appears once you pick the solid option.
- `Save icon` writes the image to `Assets/AvatarDoc Icons/<avatar name>/<plain entry name>.png` and assigns it to
  the entry's icon automatically, with Undo. Capturing the same entry again overwrites its own picture, unless
  another entry uses that same file too (a copy of the entry does until it gets its own). Any other capture gets a
  file name nothing uses yet, such as `Hat 1.png`, so a second entry called Hat keeps its own picture.
- Only the entry's own pieces render: its object targets, whether the entry shows or hides them, together with their children, and
  the renderers behind its blend shape, material slot and material property targets; a submenu renders every
  descendant entry's pieces together, and a preset renders the pieces of every control it switches on (always both
  ends of a radial). Every other renderer in every open scene is hidden for the render and restored right
  afterward, so nothing in the scene changes, unloads or is removed.
- A piece that is switched off in the scene, as a toggle that starts off usually is, is drawn into the picture
  anyway. Nothing is switched on to do it: the mesh is drawn straight into the capture, and the object stays off.
- Blend shape and material effects render with whatever weight or material they currently have in the scene, not
  the toggle's On state, so setting the scene up by hand first gives the truest icon.

## Triggers

A trigger fires the same kinds of effects as a toggle (objects, blend shapes, material slots and material
properties) without adding a control to the menu. Its `When` field picks one of five sources:

- **Another toggle**: fires while a chosen toggle, on any AvatarDoc component on the avatar, is on or off.
- **A parameter**: fires while a parameter (VRChat built-ins, contacts, PhysBones, Modular Avatar, a VRCFury
  toggle's own global parameter, or AvatarDoc's own) meets a condition: is true, is false, equals, not equals,
  greater than or less than a value.
- **A blend shape is animated**: follows the live weight of a blend shape wherever an FX animation already moves
  it, blending its effects in smoothly or switching them at one weight. See Blend shape triggers below.
- **A contact**: fires from a sphere anchored to a bone that other players or your own body can touch or come near.
  See Touch triggers below.
- **A PhysBone**: fires from an existing PhysBone's grabbed, posed, angle, stretch or squish state. See Touch
  triggers below.

Every source but the blend shape is a condition trigger: it reads an existing toggle, parameter, contact or
PhysBone. None of the five costs synced bits or adds an expression parameter of its own.

**Material properties on a trigger.** Every trigger offers `Properties` in `+ Add effect`, the same rows a toggle
has: a renderer, a shader property and the value to use. A `Flip` trigger has no effects of its own, so it has none.

- **Hold** (a toggle, parameter, contact or PhysBone trigger in its usual mode): the property takes the row's value
  while the trigger fires and goes back to its rest value as soon as it stops. The rest value is the material's own
  value, or the row's `Off value` when `Set a value when off` is on. When some other part of the avatar already sets
  that same property, for example a toggle of your own that moves it, what happens between touches depends on the
  avatar's Write Defaults (the setting AvatarDoc reports in its header). With Write Defaults off, the trigger leaves
  the property to that toggle while it is not firing, so the toggle keeps working between touches. With Write
  Defaults on, a Hold trigger that is not firing puts the material's own value back over the toggle (not the row's
  `Off value`), so the toggle's change does not show; give the two different properties if you need both to work.
- **Fade** (a contact trigger in `Fade` mode): the property blends with how close the hand is, from its rest value
  at the edge of the radius to the row's value at `Fully on`, exactly as the blend shapes do. While no hand is near
  it rests, and when a toggle of yours already sets that property the trigger leaves it to that toggle, whatever the
  avatar's Write Defaults: this layer is always built with Write Defaults off.
- **Blend shape**: the property blends with the shape, from its rest value at `Starts at` to the row's value at
  `Fully on at` (or switches at `Switch at` in the Switch style), and rests the same way a Fade trigger's does.

**Holding the rest of a mesh** works on every trigger with effects of its own, the same way it does on a toggle
(see Toggles, radials and groups above): a Hold trigger holds the rest while it fires, a `Fade` contact and a blend
shape trigger blend it in with their other effects.

`+ And`, `+ Or` and `Only while` work the same with property rows as with any other effect. The scene preview shows
a trigger's property rows when you press `On` or `Off` on it. With `Off`, a property one of your toggles also sets
shows what the avatar will show: the toggle's value for a Fade trigger, or for a Hold trigger on a Write Defaults
off avatar, and the material's own value for a Hold trigger on a Write Defaults on avatar. When both a Hold and a
Fade trigger move the same property, the Fade trigger wins while both fire, whatever their order in the tree, since
its layer is always built last. "What this builds" counts the trigger's layers.

Any condition trigger, on its own or with several, can combine more than one condition. Press `+ And` under the
panel's `When` line to add a second condition next to the first: both then have to be true at once (`All of`). Press
`+ Or` instead to add a whole new group: the trigger fires while any group is fully true, not only the first
(`Any of these`). Each condition row picks its own kind (Toggle, Parameter, Contact, PhysBone) and shows that kind's
own fields; a Contact or PhysBone row expands its settings under a `Settings` button once it is the selected one, and
the Scene view handle follows whichever contact condition that is. A sentence above the cards spells the whole rule
out in words, such as "When (Hat is on and Nose is touched) or (GestureLeft equals 3)", and the summary prints the
same sentence. A group left with no conditions in it is dropped with a warning; if every group ends up empty the
trigger simply never fires. Keep it to at most 4 groups of at most 4 conditions each: past that, or past 64 ways for
the trigger to stop being met again, AvatarDoc refuses with an error naming the counts, since a rule that large would
make the generated layer too big to be worth building. `+ And` switches off once a group holds 4 conditions, and
its tooltip says so; use `+ Or` to start another group.

A blend shape trigger and a `Fade` contact work a little differently: one thing drives them (the shape, or how
close the hand is), so they offer `+ And` but no `+ Or`. Each `+ And` adds a requirement of any of the four kinds:
a toggle on or off, a parameter test, another contact or a PhysBone. While any requirement fails, the effects rest;
while all of them hold, the driver moves the effects as usual. `Only while` is offered on both and counts as one
more requirement. The card holds at most 4 rows, its first row (the shape, or the driving contact) included, so
`+ And` switches off after 3 requirements and its tooltip says so. A contact requirement gets its own contact on
the avatar, with its own `Touch` or `Closer than` choice; only the driving contact fades.

## Blend shape triggers

A trigger whose `When` is `A blend shape is animated` follows one shape: pick it with `Shape`, the first row of its
card. The effects follow that shape's live weight wherever an FX animation moves it, whether a gesture, face
tracking, another toggle of yours, or an animation Modular Avatar or VRCFury add.

- **Style** `Smooth` (the default, and what every blend shape trigger made before this became): blend shapes and
  material properties blend in as the shape moves, from rest at `Starts at` (default 0) to their full value at
  `Fully on at` (default 100). Objects and material slots cannot blend, so they switch at `Swaps at` (default 50).
  If `Fully on at` is not above `Starts at`, the effects are fully on just above `Starts at` and a warning says so.
- **Style** `Switch`: the whole trigger switches fully on once the shape reaches `Switch at` and off again below it,
  the way blend shape triggers always worked before.
- A `Follow source` blend shape row copies the shape times its `Multiplier` in both styles, clamped to 0 to 100.
- **Mode** `Add`: the shape keeps moving exactly as its own animations say, and the effects come on top.
  **Mode** `Replace`: the shape's own curve is taken out of every FX animation that moves it, so the shape itself
  stays at rest and only the effects show; the trigger still follows where the shape would have been. A `Replace`
  trigger with no effect it can build keeps the shape's curves and says so.

How it is built: AvatarDoc copies the shape's weight, key for key, into a hidden animator value (`AD/Shape/<name>`,
a Float the animator alone uses) in every FX animation that moves the shape, after Modular Avatar and VRCFury have
added theirs. The value is never an expression parameter and costs no synced bits: every player's own avatar
computes it from the same animations. The trigger then gets one FX layer of its own, `AD: <name> (shape)`, built
like a `Fade` trigger's: Write Defaults off, placed after everything Modular Avatar and VRCFury merge in, resting in
`Far` (`Off` in Switch), where it leaves every shape and property to whatever else moves them and never holds a
shape VRChat blinks or speaks with, and blending in `Near` (`On` in Switch). The summary and NDMF's parameter
list show the hidden value as animator only, and "What this builds" reads, for example, "1 FX layer, blends from 0
to 100, a hidden copy of the shape's weight, no synced bits". When an effect moves a shape VRChat blinks or speaks
with (an eyelid, a viseme or the jaw flap shape set on the avatar descriptor), the trigger shares the `Fade`
trigger's `AD: Blink pause` or `AD: Lip sync pause` layer: VRChat's own blinking or lip sync pauses while the
effects are on and comes back the moment they rest.

Limits worth knowing:
- An animated value only reaches the layers of its own controller, so only FX animations can drive the trigger. A
  shape moved by the Gesture, Action or Additive layer is not followed there; the inspector names those
  animations in a warning, and the build names any a tool merges in. (Before this change those animations were
  rewritten too.) When no FX animation moves the shape at all, the build says so: the effects then stay at the
  value the shape's resting weight gives them.
- A shape VRChat moves itself, outside every animation (the blinking and viseme or jaw flap shapes set on the
  avatar descriptor), cannot be followed, and a warning says so.
- VRChat's own proxy animations cannot be changed by any tool; they are left alone and the message table below
  names the skipped clip.
- A trigger cannot use its own shape as an effect; that row is ignored and reported once.
- There is no `Follows` effect here: the trigger has no plain on and off for a follow to ride on.

Every toggle and radial also has an `In menu` switch beside `Default on`, on by default. Switching it off keeps
everything about the entry (its parameter with its own saved and synced flags, its layer and effects, its place in
an exclusive group, its default) but removes its control from the built menu and gives it no menu slot: it becomes a
state the avatar has without a button for it, so a trigger's condition or gate can still read it, a flip trigger can
still flip it, and a preset can still set it, without ever cluttering the menu itself. The tree shows such a row
dimmed with a `no menu` chip; the wheel leaves it out while in `Select` mode and shows it in `Test` mode on its own
`Not in the menu` strip so it can still be tried (see Testing without play mode below); the summary lists it under
its own `Not in the menu` heading. If nothing can ever change a hidden toggle, meaning no flip trigger and no preset
sets it, and it is not `Default on` either, AvatarDoc reports it as unreachable at build (see the message table
below): a trigger condition that only reads it does not count, since reading it changes nothing about it.

## Touch triggers

A trigger sourced from a contact or a PhysBone reacts to touch instead of a toggle or a parameter: "boop the nose
to toggle blush" is a contact trigger with a `Flip a toggle` effect.

**Contact.** Pick `Contact` as the trigger's `When` source, then:

- **Anchor**: the object the contact sphere follows, usually a bone. Its selector opens the avatar picker described
  under Picking objects below, with the avatar's humanoid bones listed first when it has any; you can still drag any
  object onto it by hand.
- **Radius**: the sphere's size, 0.005 to 1, default 0.05.
- **Offset**: how far the sphere sits from the anchor.
- **Touched by**: which VRChat contact tags reach it. `Finger`, `Hand`, `Head`, `Torso` and `Foot` are always
  shown; `More tags` reveals the rest of VRChat's own tags (`HandL`, `HandR`, `FootL`, `FootR`, `FingerL`,
  `FingerR`, `FingerIndex`, `FingerMiddle`, `FingerRing`, `FingerLittle`); a text field adds any other tag you need
  as its own removable chip.
- **Who**: `Self` and `Others` switches decide whether your own colliders, other players' colliders, or both, can
  touch it.
- **Distance**: `Touch` fires only on contact; `Closer than` fires once a collider is within a fraction of the
  radius, using VRChat's own proximity value, instead of an exact touch.

While a contact trigger is selected and its component is enabled, the Scene view shows the sphere with a position
handle and a radius handle, in the inspector's accent colour; dragging them writes `Offset` and `Radius` straight
back onto the component, with Undo.

**PhysBone.** Pick `PhysBone` as the source, then point the field at a `VRCPhysBone` already on the avatar (its
selector opens the same avatar picker, listing only this avatar's PhysBones), pick an `Event` (`Grabbed`, `Posed`,
`Bent past`, `Stretched past`, `Squished past`), and, for the three "past" events, set the `Amount` it has to
cross. Several triggers can point at the same PhysBone; they share its generated parameter instead of each
creating one.

**What gets built.** A contact trigger adds a sphere-shaped contact receiver to a new child object of the anchor,
named `AD Contact <name>`, on the avatar copy at build time, never on the scene you are editing; a PhysBone
trigger gives the PhysBone its own parameter only if it does not already have one, again only on the build copy.
Both parameters are read by the animator only: neither is added to the expression parameters asset, so a contact
or PhysBone trigger, in `Hold effects` mode, never costs a synced bit.

**Mode.** Every contact and PhysBone trigger has a `Mode`:

- **`Hold`** (the default): the trigger's usual object, blend shape, material and property targets apply while the
  touch condition is true, exactly like a toggle- or parameter-sourced trigger, and stop the moment it is not.
- **`Flip`**: the trigger has no targets of its own; a `Toggle` field appears instead, and each new touch
  flips that toggle between on and off, through a small FX layer that sets the toggle's own parameter with a local
  parameter driver. Because that driver writes the toggle's real, synced parameter, the toggle's menu control, its
  saved state and every other player all stay in step with a flip the same way they would with a press in the
  menu. Presets and other triggers cannot be a flip target, and a target that is missing or is not a toggle is
  reported as an error.
- **`Fade`** (Contact sources only; a PhysBone trigger is never offered it, and a trigger whose data says this mode
  but whose first condition is not a contact shows `Hold` selected and behaves as one): the effects fade in with
  how close the hand is, instead of switching fully on or off. A headpat can close the eyes slowly as the hand
  comes down, touching an eye can close it the same way, and a nose boop can open the mouth as a finger gets near.
  In this mode the `Distance` (touch or closer) row is hidden and a `Fully on at` slider takes its place, showing
  the value the build actually uses (old data left at 0 reads and builds as 80%): the effects start at the edge of
  the radius and are fully on at that closeness. The first contact drives the fade; `+ And` adds requirements
  that must also hold (see the `+ And` paragraph under Triggers), `Only while` is one more of them, and `+ Or` is
  hidden. A card left over from before, other than the one the driving contact is in, is kept but not read, and a
  warning says so. If you made a `Fade` contact before requirements existed, check it once: the extra conditions in
  its first card and its `Only while` toggle used to be kept but ignored, and they count now, so the fade only
  happens while they hold too, and an `Only while` that points at a toggle you have since deleted now stops the
  upload until you pick another toggle or set it back to `(always)`. Every shape row fades from rest to its value
  the same way here, whatever mode is stored on it, so the row's own Fixed/Pin choice is hidden too. Every
  property row fades from rest to its value the same way. Follows cannot fade with distance: an existing Follows
  row stays with a warning, and the build skips it; `+ Add effect` does not offer it while this mode is picked.

  This mode is built as a pin layer (Write Defaults off, moved after anything Modular Avatar or VRCFury merge in,
  face tracking included), so it never fights or gets overridden by a merged controller regardless of the avatar's
  own Write Defaults choice. A shape the avatar descriptor itself drives for blinking or lip sync is never given a
  rest value by this trigger either: VRChat's own tracking is left to own it completely while no hand is near, the
  same as any other shape nothing here touches. When the effects move an eyelid or a viseme (or jaw flap) shape
  the avatar's descriptor actually reads for tracking (eye look on and its eyelid type is Blendshapes; lip sync
  set to read blend shapes), a shared `AD: Blink pause` or `AD: Lip sync pause` layer, built once for every Fade
  trigger that needs it, sets VRChat's own tracking to Animation on that part only while at least one contributing
  trigger's effects are on (a hand is near and every requirement holds), and back to Tracking the moment none are;
  it never touches tracking at avatar load. A hand near a contact whose requirement fails pauses nothing, and a
  trigger whose requirement cannot be built at all builds no pause either. If some other
  system (a VRCFT template, a sleep or AFK setup) already controls that part, AvatarDoc's own pause layer for it is
  dropped and a warning names the layer left holding it, rather than fighting it (VRCFury's own tracking arbiter
  does not count as another system: AvatarDoc's request already reaches it). "What this builds" says the same,
  live, whenever the avatar's own FX controller already shows another tracking control for that part.

**Ready made starts.** `+ More > Contact starts` offers three one click starts that build a correctly placed
`Fade` trigger for you: `Headpat` (over the head, tags Hand and Finger), `Eye touch` (over each eye when the
avatar's humanoid rig has `LeftEye` and `RightEye` bones, one trigger over the head otherwise) and `Nose boop`
(over the nose). Everything is placed from the avatar itself: its `View Position`, its humanoid bones, and its
root's own forward and up, scaled to the avatar's own height, never a fixed number. Nudge the sphere in the Scene
view if it sits off; the placement is a starting point, not a guarantee for every body shape. Each start also adds
one blend shape row, guessed by name (case is ignored) from the avatar's viseme mesh, else its eyelid mesh, else
the skinned mesh with the most blend shapes: closed eyes for `Headpat` and `Eye touch`, a mouth open shape for
`Nose boop`. When no name matches, the row is left with no shape picked, ready for you to choose one. A start that
makes two triggers (`Eye touch L` and `Eye touch R`) is one Undo step. An avatar with no Head bone (no Humanoid
Animator, or none assigned) refuses the start and says so.

**Other players.** For a contact-sourced `Flip a toggle` trigger, another player can flip the toggle only when
`Others` is switched on for that contact. A PhysBone-sourced one follows whatever the PhysBone's own grab and pose
permissions already allow; AvatarDoc does not add permissions of its own.

**Only while.** Every trigger, blend shape, contact and PhysBone ones included, has an `Only while` row below its
condition: pick a toggle and an On/Off choice, and the trigger only does anything while that toggle is in
the chosen state; the first choice, `(always)`, is the default and means no such limit. `Add a menu switch` creates
a plain toggle named after the trigger, on by default, saved and synced, with no targets of its own, sets it as the
gate straight away and selects it in the tree, so you get a working switch in one press instead of building a toggle
by hand first. For a contact trigger, switching the gate off does not remove the contact receiver from the avatar:
it stays exactly where it is, listening; only the trigger's own effect is ignored while the gate is not met. Test
mode and the scene preview respect the gate too: a trigger whose gate is not currently met does nothing when
pressed, and its button in the wheel's `Triggers` strip is dimmed to show it.

**Preview.** A `Hold effects` contact or PhysBone trigger previews the same way any condition trigger does
(`Default`/`On`/`Off`, in the panel or from the wheel's `Triggers` strip in test mode). A `Flip a toggle` trigger
shows a single `Simulate touch` button instead, in the panel and in the wheel's trigger strip alike, which flips
the target toggle's own preview rather than the trigger's.

Neither a contact nor a PhysBone can be exercised from the editor: check that a touch actually fires, that a
PhysBone event actually crosses its threshold, and that another player can really flip a toggle through a contact
or a PhysBone, in Play mode with Gesture Manager or in an actual instance with another player, not by reading the
inspector.

## Picking objects

Every field that points at something on the avatar itself, an object target, a blend shape or material renderer, a
trigger's contact anchor, a trigger's PhysBone, or the `Path root` of a custom animation or a merged animator, opens
AvatarDoc's own picker when you click the small circle at the field's right end, instead of Unity's own object
picker. Unity's own picker lists every object in every open scene and cannot be limited to one hierarchy; AvatarDoc's
picker lists only the current avatar's own objects and their children.

The picker is a searchable dropdown: type to filter, and use the arrow keys to move through the list. It only offers
what the field actually accepts (any GameObject, only objects with a renderer, only skinned meshes that have blend
shapes, only PhysBones, or any Transform), and a Transform field, such as a contact's Anchor or a `Path root`, lists
the avatar's own humanoid bones first under `Bones` when the avatar has a humanoid rig. An object switched off in
the Hierarchy is still listed, marked `(inactive)`; two same-named objects under different parents are told apart by
the parent path shown after the name. If nothing matches, it says so instead of showing an empty list.

The field otherwise works exactly as any other object field does: drag an object from the Hierarchy onto it, or
click its body to ping the current value. Fields that hold an asset instead of something on the avatar, an icon, a
material, an animation clip, an animator controller or a menu asset, keep Unity's own picker, since assets are not
part of the avatar. Locking a component disables its pickers along with everything else it disables. The outfit
wizard's `+ Add piece` button beside each drop zone opens the same avatar-scoped picker too, as an alternative to
dragging from the Hierarchy.

## Overrides

An override holds a blend shape at one value in every animation that already uses it, permanently, no button in the
menu, no synced bits. One sentence sums up why it exists: `FTEffectsCringe` is animated by two face tracking clips
in the FX controller, and an override on it holds the shape at 0 so those clips can never move it again.

Press `+ Override` in the tree's toolbar to add one. Overrides sit under an `Overrides` group row at the bottom of
the tree, below `Triggers`, and can be duplicated and deleted the same way any other entry is. An override's own
panel is just a name and a list of blend shape targets: pick a renderer and shape, then set `Pinned value`; 0
switches the shape off, any other number holds it there instead.

The panel's "Animated in *N* clips" list names every clip that already animates the pinned shape, and lets you
click a name to ping it in the Project window. That list only reads the avatar descriptor's own playable layer
controllers, the ones already sitting on the avatar before you press Play or upload; an animator another tool
merges in at build time does not show up in it.

The pin itself is applied to a build copy of the avatar, never to the clip assets on disk: AvatarDoc rewrites
the shape's curve to a flat, constant value inside the copy that gets uploaded or played, and your original
animation files stay exactly as they were. This rewrite runs twice: once right after Modular Avatar and once more
after VRCFury, so it reaches animations either tool merges into the avatar by build time, unlike the "Animated in
*N* clips" list, which only reads the avatar's own playable layers before either tool runs. The second pass is
quiet about anything the first pass already pinned; a layer some other tool appends after both those passes is not
reached, and is left alone. If two overrides end up pinning the same shape, both still take effect at build time
and a warning names them; the one that comes later in the merged tree wins.

## Merged animators

A merged animator brings an existing animator controller's layers straight into the avatar's FX controller, the way
a Modular Avatar Merge Animator does. Use it for a controller somebody else built (a shader's own animated menu, a
gimmick you got as a `.controller` asset) that is not worth rebuilding as AvatarDoc entries, or for whatever a
convert of an animator driven Modular Avatar menu leaves merged after turning what it safely can into real entries
(see Convert below).

Press `+ Animator` in the tree's toolbar, or drag a `RuntimeAnimatorController` asset from the Project window onto
the tree. Merged animators sit under an `Animators` group row, alongside `Triggers` and `Overrides`. Its panel:

- **`Controller`**: the animator controller whose layers join the build. The asset itself is never modified: its
  layers are cloned into the build copy of the avatar.
- **`Path root`**: an avatar object, picked with the avatar picker described above. Leave it empty when the
  controller's clips already animate paths starting at the avatar root; set it to wherever the controller was
  originally built against (a prefab's root, say) and AvatarDoc prefixes every path in its clips to match, the same
  way `Path root` works for a custom animation.
- **`Match write defaults`**: on by default. The merged layers' states take the avatar's own Write Defaults
  decision, the same as AvatarDoc's own layers do.
- **`Platform`**: the same `Both`/`PC only`/`Quest only`/`Inherit` choice every entry has. An excluded merge adds no
  layers on that platform, but its parameters still come from whatever declares them, so the parameter list stays
  identical on both platforms.
- **Layers**: a check list, one row per layer in the controller; unchecking a layer leaves it out of the merge
  entirely (this is also how a convert marks the layers it already turned into real entries).
- **Parameters**: a read only list of every parameter the controller declares, saying who is responsible for each:
  the name of an AvatarDoc entry driving it, `existing on the avatar` when the avatar's own expression parameters
  already have it, or `not synced` with an `Add control` button that creates a **parameter control** for it on the
  spot.

A parameter control is an ordinary toggle or radial with a custom parameter name and no targets: it gives a merged
animator's parameter a menu control and, if you want, synced bits, without adding a layer of its own, since the
merged controller's own layer already does the work. Its panel shows a `Drives a parameter only.` note in place of
the usual effects. If nothing on the avatar actually reads that parameter, a warning says so. If the parameter
name already exists under a clashing type, what happens depends on who else reads it: where the name is in the FX
controller only because AvatarDoc planned it, for parameter controls or for a declaration of its own, the merged
controller's type simply wins and nothing is reported. Where anything else reads it, the declaration that is
already there stays, the layers are still merged onto it, and an error names both types so you can settle it.

Two limits worth knowing: only FX layer type controllers can be merged (a Gesture, Action or other layer type
controller is not offered), and merged animators are not shown by the scene preview; the panel says so. Leaving a
layer out while the controller uses animator layer control, which refers to layers by their position, is refused
with an error, since leaving one out would point that layer control at the wrong layer.

## Menu assets and parameters

A menu asset installs a whole finished `VRCExpressionsMenu` (a submenu somebody already built, with its own icons
and controls) into your menu, the way a Modular Avatar Menu Installer does. Declared parameters let you add a
parameter of your own, for a menu asset's controls or a merged animator's parameters that no entry already declares
one for, without building a whole toggle just to get a parameter onto the avatar.

Press `+ Menu asset` in the tree's toolbar, or drag a `VRCExpressionsMenu` asset onto the tree. Menu assets sit
under a `Menu assets` group row. Its panel:

- **`Menu asset`**: the asset itself, picked with Unity's own picker (it is an asset, not something on the avatar).
  It is never modified: its controls, including any of its own nested submenus, are read the same way the avatar's
  own menu is and written into the build as copies.
- **`Install path`**: "Submenu path from the avatar's root menu, such as Outfits/Summer. Empty installs where this
  component installs, and a single slash installs into the avatar's root menu." (its own tooltip). So `/` always
  means the avatar's root menu, whatever this component's own install path is, and empty means wherever this
  component itself installs.
- **`Place`**: `As submenu` wraps the whole asset in one new submenu control at that path; `Append items` instead
  puts the asset's own top level controls directly into the menu at that path, with no extra submenu of its own.
- **`Name`** (hidden while appending, since there is no submenu control to name), **`Icon`** and **`Platform`**, the
  same as any submenu.
- Its own list of parameters, saying who declares each one: `declared here` when one of your own declared
  parameters already covers it, or `nobody declares it` with an `Add declaration` button, since VRChat ignores a
  menu control whose parameter is not in the expression parameters. A control that is hidden or removed (see
  Editing the existing menu and Removing things from the avatar above) is never checked this way, since the build
  never writes it either.

A row that came from a menu asset carries an `asset` chip and can be renamed, re-iconed, hidden or moved through
menu edits exactly like an existing row (see Editing the existing menu below); it counts toward the page split like
any other control.

**Declared parameters** live in their own `Parameters` group row: `+ Parameter` in that panel adds one with a name,
a type (Bool, Int or Float), a default value, and `Saved`/`Synced` switches, shown as a compact table with the bits
each row costs and a running total at the bottom. Declared parameters are planned after every entry's own
parameters, in the order they are listed, identically on PC and Quest. A declared name that an entry already plans
loses to the entry with a warning; one that matches an existing expression parameter of the avatar reuses it (a
type clash is an error).

**Taking over a Modular Avatar prefab.** A prefab built the way Modular Avatar itself builds one, a Menu Installer,
a submenu sourced from a menu asset or appended in place, a Parameters component and a Merge Animator together, can
be taken over as one group from the Convert window or with `Convert to AvatarDoc`: the installer becomes a menu asset
entry (one whose own installer targeted the avatar's root menu comes in with `/` as its install path, so it still
installs at the root even though this AvatarDoc might itself install as a submenu elsewhere), its declared
parameters become AvatarDoc's own declared parameters, and its Merge Animator becomes a merged animator, all
arriving and being removed from the original together. A Modular Avatar parameter marked not synced comes over as
no declaration at all, the same as Modular Avatar itself never gives one an expression parameter.

Where the merged controller's layers are simple enough, the converter still prefers turning them into real AvatarDoc
entries first (see Convert below). When it does, the menu asset's own control for a parameter a conversion replaced
is hidden rather than left showing the old, now unused control, and that hide targets exactly that control, never a
same named control the avatar's own menu already happens to have at the same place; a declaration for a name a
conversion replaced is dropped too, as long as nothing left in the merged controller or the menu asset still needs
it, so it does not keep costing synced bits for nothing. Tick `Keep as it is` on the group instead to carry the
menu, the parameters and the animator over completely unchanged, with no layer turned into a real entry and nothing
hidden or dropped. Renamed or internal parameters anywhere in such a set make the whole group unsupported, the same
as for a plain merged animator, since the names inside the controller would no longer match what gets declared.

## Presets

A preset is a button in the menu that puts several other controls into one state at once: turn on an outfit's
objects, set a couple of blend shapes and switch an exclusive group member, all with a single press. It costs no
synced bits: its own parameter is a plain Bool that is never saved or synced.

Press `+ Preset` in the tree's toolbar to add one; it has a name, icon and style row like a toggle, and lives in a
submenu the same way a toggle does. Its panel has two lists:

- **`Sets`**: one row per control it drives, added with `+ Add`. Each row has a button naming the control (its full
  path), an `On`/`Off` switch for a toggle or an exclusive group member, or a slider from 0 to 1 for a radial, and
  a `×` button to remove the row. `From preview` fills this list from whatever controls currently have a scene
  preview override, at the value each one is previewing.
- **`Other parameters`**: sets a parameter AvatarDoc does not own, such as one from VRCFury or Modular Avatar,
  to a given value. Its own `+ Add` adds a row with a name field (the same picker a trigger's `Parameter` field
  uses, so a VRCFury toggle's global parameter works here too), a value field and a remove button.

For an exclusive group, only the member you turn on in the preset is written; if every member you listed is turned
off, the group's own parameter is set to 0 once. Two members of the same group turned on in one preset is an error,
and only the first is applied; two toggles that are not in a group but share a custom parameter name behave the
same way, since only one of them can really be on at once. A row whose control was since deleted shows
`(missing control)` in amber and can still be clicked to point it at a different control. `This preset sets
nothing yet.` shows in the panel when both lists are empty; the same is reported as a warning at build time.

The preview row uses the same `Default`/`On`/`Off` control every toggle-like kind has: `On` puts every listed state
and parameter into the scene preview at once, the same as setting each one by hand, though it does not stay held
the way a toggle's own `On` keeps its targets switched; `Off` forces every named control to its own off value
instead; `Default` clears the preview.

## Outfit sets

`+ Outfit set` in the tree's toolbar opens a wizard that turns a handful of GameObjects into a full outfit
selector: a submenu, one exclusive group or one preset per outfit, and the shrink shapes that pull skin in under
clothing, all in a single Undo step. Everything it creates is an ordinary AvatarDoc entry afterwards: you can
rename it, re-icon it, add or remove targets, or delete it, exactly as if you had built it by hand.

Name the set, then add outfits with `+ Outfit` and drop GameObjects from the Hierarchy onto each one as its
pieces; dropping several objects on the very first, empty outfit offers `One outfit per object` or `One outfit
with all of them` instead of guessing which you meant. The card marked `Default` is the outfit that starts on;
click another card's own `Default` chip to make that one the default instead.

Two modes:

- **`One at a time`**: one submenu with one toggle per outfit, all sharing one exclusive group so only one is ever
  on. Pieces dropped under `Hidden while any outfit is on` (shown only in this mode) are turned off by every
  outfit's own toggle, so they disappear the moment any outfit is worn; turn on `Add a None option` for a toggle
  that shows nothing and hides those pieces too.
- **`Mix freely`**: one toggle per distinct piece, on by default for whichever outfit is marked `Default`, plus one
  preset per outfit that switches its own pieces on and every other piece in the set off, and an `Everything off`
  preset. There is no single "any outfit is on" state in this mode, so `Hidden while any outfit is on` does not
  apply and is not shown.

**Shrink shapes found by name.** For each piece, the wizard lists blend shapes on the avatar's other skinned
meshes whose name looks like the piece's own name: both names are reduced to lower case letters and digits only, a
leading `shrink`, `hide`, `off`, `kisekae` or `cloth` is stripped from the shape's name, and a match is anything
where one name contains the other (a name under three characters is never matched, on either side). More than
eight suggestions fold behind `Show all (N)`. A ticked suggestion becomes a blend shape target, at full weight, on
the toggle that shows the piece, so only tick the ones that actually correspond to that piece: the wizard cannot
tell a real match from a coincidental name. Dropping an object that is not part of the avatar is refused instead
of added.

**Icons.** Leave `Capture icons` on (the default) and every entry the wizard creates that has pieces gets an icon
automatically, the same way `Capture missing icons` does; that same action is also available at any later time
from a right click anywhere in the tree, so entries you add or rename afterwards can get icons too.

`Create outfit set` builds everything and jumps the inspector to the new submenu; with nothing to build (no
outfit has a piece, and no `None` option was added) it refuses instead and asks for at least one outfit with a
piece.

## AvatarDoc Shapes

`AvatarDoc Shapes` is a second, small component that sits on the outfit piece itself rather than on the avatar
root, so a piece carries its own body fixes wherever you drop it. The case it is made for: a jacket that pushes
the body's shrink shapes to 100 while it is worn, and whose own shapes (breast size and so on) follow the body's,
so the piece keeps fitting when you change the body. It costs no parameter and no layer: the values are written
into the animations that already switch the piece.

Add it with `Add Component` and `AvatarDoc/AvatarDoc Shapes`, on the piece's own object. For the `Follows`
section, put it on the object that carries the piece's skinned mesh.

**`Follows`.** The piece's own mesh copies a shape of another mesh:

- **`Source mesh`**: the mesh to read, picked with the avatar picker, usually the body.
- **`Source shape`**: the shape on that mesh. Picking a shape sets the mesh with it.
- **`On this piece`**: the shape this row writes on the piece. Left at `Same name` it uses the source's own name.
- **`Add matching shapes`**: pick a mesh and every shape name that mesh and the piece both have becomes a row,
  skipping the names a row already writes.

Every animation that moves the source shape now moves the piece's shape with it, and the piece starts at the
weight the source shape has in the scene.

**`While this object is on`.** Any mesh of the avatar holds a shape at a value while the piece is on:

- **`Mesh`**: the mesh to change, usually the body. The piece's own mesh is allowed too.
- **`Shape`**: the shape on that mesh. Picking a shape sets the mesh with it.
- **`Value`**: 0 to 100, on a slider.

While the piece is on, the shape holds that value; while it is off, the shape goes back to its rest value, which
is simply the weight the mesh has in the scene before the build. If the piece is on in the scene and nothing ever
switches it, the value is applied once and a note says so.

**`Platform`** works like everywhere else: `Both`, `PC only`, `Quest only` or `Inherit`. A piece left out on a
platform writes nothing there.

**What counts as turning the piece on.** An animation that switches the piece's own object on or off, or one that
switches a parent on or off while the piece and everything between it and that parent are on in the scene and
switched by nothing. Fading an object in or out instead of switching it is not a switch: that animation is left
alone and a warning names it.

**Which animations are covered.** Every clip in the avatar's FX controller at the moment the pass runs, which is
right after Modular Avatar: AvatarDoc's own toggles, outfit sets, presets and triggers, merged animators, and
whatever Modular Avatar merged. Only the FX controller; a Gesture or Action clip that happens to switch the piece
is left alone. Clips VRCFury adds later in the build are not seen. An animation that already writes the shape
itself keeps its own values and says so, so your own explicit choice always wins.

**Two pieces on one shape.** Both work: in an animation that turns several of them on, the larger value wins,
which is what shrink shapes want. A warning names the pair, including two pieces of one exclusive outfit set that
can never be worn at the same time, since nothing in the component itself knows they are exclusive.

**Coming from Modular Avatar.** `Convert` offers what it finds under `Shapes on a piece`: an `MA Blendshape Sync`
becomes `Follows` rows, and an `MA Shape Changer` that sits on a piece of its own becomes `While this object is
on` rows, as long as every shape it changes is a `Set`. One that deletes a shape is listed with its reason
instead, since holding a shape at a value is not the same as deleting it. Ticking such a row adds the component
to that piece with the rows filled in, and `Remove originals` takes the Modular Avatar components away.

The summary lists every piece of the avatar that carries the component, with how many rows each one has.

## Editing the existing menu

Select any row with an `existing` chip (from the avatar's own menu) or a `VRCFury`/`Modular Avatar` chip (added by
those tools), and its panel lets you change how it appears without touching the original item or the tool that
made it:

- **`Name`**: a styled name, with the original name shown underneath as `Original: ...`.
- **`Icon`**.
- **`Move to`**: a text field plus a dropdown of every submenu path, including `(stay where it is)` and `Root`.
- **`In menu`**: the same switch an own entry has, on by default; switching it off removes the item from the built
  menu without deleting anything.
- **`Remove from avatar`**: right under `In menu`, on a row with an `existing` chip only; see Removing things from
  the avatar below for what it actually does.
- **`Reset`**: greyed out until something is actually edited; clears every change back to the original.

You can also drag the row onto a submenu row (yours, an existing one, or a foreign one) to move it, or right click
it for `Move to`, `Hide`/`Show` and `Reset`. Moving a submenu into itself or into one of its own descendants is
refused, whichever way you try it.

Every one of these is a separate edit AvatarDoc keeps on its own component, applied at build time: once right
after Modular Avatar and, for a VRCFury item, once more after VRCFury, so it always lands on the menu those tools
actually built rather than the one AvatarDoc started from. A move into a submenu that does not exist yet waits
for that last pass, so it lands in the submenu another tool is about to install rather than creating a second one
beside it; a move into a submenu that is already there happens straight away. An item a full menu pushed onto a
`More` or `Next` page is still found by its ordinary path, so an edit keeps working on a crowded avatar. An edited
row shows an `edited` chip; a hidden row
stays in the tree, dimmed, with a `hidden` chip, so you can find and undo it later. If the item an edit points at
has since gone (deleted, renamed elsewhere, or converted), a warning names the edit and nothing else about the
build changes because of it. Such an edit has no row left to reset from, so the panel header lists it with a
`Remove` button of its own. An AvatarDoc entry of your own cannot be edited this way; open the entry itself
instead.

## Removing things from the avatar

Switching `In menu` off only takes an item out of the built menu: its parameter keeps costing synced bits and its
animator layer keeps running, exactly as if nothing had changed except the menu. `Remove from avatar` goes further.
It appears right under `In menu` in the panel of a row that came with the avatar rather than from an AvatarDoc
entry: an item that was already in the avatar's own menu before AvatarDoc was added, and a control of a menu asset
AvatarDoc installs (turning it on also turns `In menu` off).

Switching it on tells AvatarDoc to work out, with the same strict rules Convert uses to decide what can be converted,
whether the item's parameter and its FX layer can leave the build entirely, not just the menu:

- Its FX layer goes only when every one of its transition conditions reads a parameter of a removed item, it has no
  state behaviours, no mask, and the avatar does not use animator layer control anywhere. Animator layer control
  refers to layers by their position, so removing one would point it at the wrong layer; when the avatar uses it,
  the layer and its parameter are both kept, and a warning names why.
- Its parameter leaves the avatar's expression parameters, freeing its synced bits, only when nothing else on the
  avatar still needs it: no surviving menu control (the avatar's own menu after your edits, an AvatarDoc entry that
  reuses the name, a preset or a trigger condition naming it), no layer of any of the avatar's playable layers still
  reading it (a condition, a blend tree, motion time, speed, mirror, cycle offset or a parameter driver), no contact
  receiver or PhysBone writing it, nothing a VRCFury toggle's global parameter or Full Controller, a Modular Avatar
  declaration, or one of AvatarDoc's own menu assets or declared parameters still needs, and it is not one of
  VRChat's own built in parameters. When in doubt, it stays.

Nothing is ever written to your assets either way: a freed layer is removed only from the copy of the FX controller
the build makes, never from the controller asset itself.

The result appears right under the switch: `Frees {0} bits and {1} layers` when everything about it can really go,
or one `Keeps "{0}": {1}` line per parameter that has to stay and why. The header's memory bar shrinks by the freed
bits the moment you switch it on. The row keeps its place in the tree, with a `removed` chip.

Items VRCFury or Modular Avatar add cannot be removed this way, since their parameters and layers do not exist yet
when AvatarDoc builds: those rows offer `Hide` and `Convert` instead (see Convert below), and converting with
`Remove originals` on already frees whatever the other tool would have declared.

## Convert

`Convert` in the tree's toolbar opens a window listing every toggle AvatarDoc found that it can turn into an
AvatarDoc entry: VRCFury toggles, Modular Avatar toggles, hand made animator layers already driving a control in
the avatar's own menu, Modular Avatar menus driven by a merged animator, and finished menu asset prefabs, grouped
under `VRCFury`, `Modular Avatar`, `Animator layers`, `Merged animators` and `Menu assets` captions. Each row shows
a checkbox, the item's menu path, its kind, a short summary of what it targets (such as "2 objects, 1 blend
shape", or "3 layers stay merged" for a merged animator, or "6 parameters, 6 declarations" for a menu asset
takeover), and underneath it either notes about anything that could not be carried over or, for a row the window
cannot convert, the reason why, in place of a checkbox.

`All`/`None` tick or clear every row; `Remove originals` (on by default) also removes the source VRCFury or
Modular Avatar components, or, for a hand made layer, hides its old menu control and leaves the old layer out of
the FX controller the build produces. `Convert *N* items` creates the ticked rows as entries under submenus made to
match their original paths; everything it does, including `Remove originals`, undoes in one step.

If the item you convert already carries a rename, an icon or a move of your own, the new entry takes those over
and that edit goes away with the item it named; an `In menu` off on it is dropped, since you asked for the item to
be converted rather than hidden. A hand made layer an earlier conversion already converted is not offered a second time.

Right click a `VRCFury` or `Modular Avatar` row directly in the tree (not one with an `existing` chip) for a
`Convert to AvatarDoc` shortcut: on a single item it converts just that one and removes the original; on a
foreign submenu row it converts every supported item underneath it in one step, leaving anything unsupported behind
as a foreign row still. An unsupported row shows its reason in a dialog instead and changes nothing.

A hand made animator layer only becomes a candidate under strict rules: its parameter must drive exactly one
simple on/off layer, on its own, with a plain animation clip made of object, blend shape, material slot or
material property curves. A layer shared by more than one branch or by another menu control, one with state
behaviours, a masked or zero weight layer, one reached by animator layer control, a blend tree, or a transition
animation between its states, is listed with the reason it cannot be converted instead of a tickable row; the
reasons are in the table below.

`Remove originals` on a hand made layer does not delete it: the old menu control is hidden and the layer is left
out of the FX controller the build makes, without touching the FX asset itself. `Layers left out of the build`, at
the bottom of the window, lists every layer set aside this way with a `Restore` button that brings its control and
its layer back together. Nothing Convert does ever deletes an asset: VRCFury and Modular Avatar components are
removed from the scene with Undo, and everything else is an ordinary AvatarDoc entry or menu edit.

**Modular Avatar toggles that share a parameter.** Modular Avatar gives each menu item on one parameter a value of
its own, so toggles that share a parameter are a set where only one is on at a time. Convert keeps that: the set
becomes one exclusive group named after the parameter, the item marked as default starts on, and ticking or
converting any one of them ticks or converts them all. If any item on that parameter cannot be converted, none of the
set is offered, and the item that blocks it shows its own reason while the others say another control uses the same
parameter. A set is not offered either when two of its items have the same fixed value, or one has the fixed value 0,
because a group cannot give those back. A toggle on a parameter of its own keeps that parameter, and its value when
the value is not automatic.

**Modular Avatar menus driven by a merged animator.** When a Modular Avatar Toggle, Button or Radial Puppet menu
item's effect actually lives in a controller that a Modular Avatar Merge Animator brings in, rather than in the
item's own reactive components, AvatarDoc looks at the layer that controller uses for it and tries the same real
conversion a hand made layer gets, extended to also read a one state blend tree of two constant clips as a Radial.
Where that succeeds, the item becomes a real AvatarDoc entry with its own new parameter, and the old layer is left
out of what stays merged; where it does not, the item still comes in as a **parameter control**, keeping its old
parameter name, value and default with no targets of its own, so the menu keeps working exactly as it did, driven
by whatever stays merged. A Button never becomes a real entry, only a parameter control, since a Button's momentary
press has nothing an AvatarDoc entry can reproduce. Every item that shares one Merge Animator, plus the merge
itself, is one group: ticking or converting any one of them ticks or converts all of them together, and if the set
would leave the Merge Animator's originals behind while something else now drives what it drove, it can only be
converted with `Remove originals` on. `Remove originals` then removes the menu items, their installer if it
installs nothing else, the Merge Animator and its Parameters component together, in one step; the controller asset
and its clips are never touched, whether anything converted or not. A parameter Modular Avatar itself would rename
or make internal makes the whole set unsupported, since the names inside the controller would no longer match what
gets declared.

A finished prefab shaped like Modular Avatar's own menu asset takeover, a Menu Installer, a Parameters component
and a Merge Animator together, is picked up the same way and offered under `Menu assets`; see Taking over a
Modular Avatar prefab under Menu assets and parameters above for what `Keep as it is` does with such a set.

Every reason and note shown in the Convert window, exactly as it appears there:

| Reason shown | Meaning |
|---|---|
| None of this item's actions could be carried over. | Nothing on the item maps to an object, blend shape or material target. |
| This Modular Avatar menu item is not a Toggle. | Only Toggle-type Modular Avatar menu items can be converted. |
| This control's parameter does not drive any FX layer. | The control's own parameter is not read by any layer, so there is nothing to convert. |
| The parameter "*parameter*" is used by more than one layer, or elsewhere as a blend tree, motion time or speed parameter. | The parameter is shared too widely to convert safely. |
| A transition into the on state also checks another parameter, so it could not be carried over. | The on state depends on more than this one parameter. |
| The layer has no state reachable on this parameter. | No transition of the layer actually enters a state on this parameter's condition. |
| The on state's motion is a blend tree, which cannot be carried over. | Only a plain animation clip can be converted. |
| The on state has no motion. | There is nothing to read a value from. |
| The curve "*curve*" on *binding* could not be carried over. | The clip animates something other than an object, a blend shape or a material property. |
| The layer switches between several states on this parameter, so it cannot be converted one control at a time. | This looks like an outfit selector with more than one option, not a single toggle. |
| Other menu controls use the same parameter. | Converting this one would leave the others without their own layer; for Modular Avatar toggles that share a parameter, another item of the set cannot be converted, so none of it is. |
| The layer has state behaviours that a converted toggle would lose. | A converted toggle has no state behaviours of its own. |
| The avatar uses animator layer control, which refers to layers by position, so layers cannot be removed safely. | Removing a layer would shift every layer after it. |
| The layer has a mask or a weight of zero. | Only a plain, fully weighted layer can be read reliably. |
| The layer plays a transition animation between off and on. | Only an instant switch between two states can be converted. |
| This layer was already converted. | An earlier conversion already turned this layer into an entry; use `Restore` in the Convert window if you want the old one back instead. |
| Several layers share this name, so the old one cannot be left out safely. | Give the layer a name of its own in the FX controller, then scan again. |
| This item's effects come from an animator Modular Avatar merges from "*name*". AvatarDoc reads such a controller only when it is set and its layer type is FX. You can still rename, move, re-icon or hide the item. | The Merge Animator has no controller, or its layer type is not FX, so AvatarDoc has nothing to read there. |
| The layer is not one blend tree of two constant clips at 0 and 1 on this parameter, so it could not be carried over. | Only a one state layer whose motion is a two child blend tree at thresholds 0 and 1 can become a radial. |
| The two ends of the blend tree animate different things, so they could not be carried over. | Both children of the blend tree have to animate exactly the same bindings. |
| Another layer copies this one's states, so it cannot be left out of the build safely. | A synced layer would point at the wrong layer if this one left the merge; convert both, or neither. |
| Modular Avatar renames the parameter "*parameter*" at build time, so the names inside the merged animator would no longer match what gets declared. Nothing of this set can be converted. | An `MA Parameters` component above it marks that name internal or remapped; give it a plain name instead. |
| The old off state sets values that a toggle cannot reproduce. | The rest clip could not be read as a plain off value: check it uses a constant curve on a binding this toggle already writes. |
| The layer rests in its on state but the parameter does not default to true, so what it should rest on could not be told. | Set the parameter's own default to true, or fix the declared default in `MA Parameters`. |
| This set replaces a Merge Animator, so its originals have to be removed with it. | Turn on `Remove originals`; leaving the Merge Animator behind would merge its controller a second time. |
| The material property "*property*" is only partly animated, so it could not be carried over. | A colour or vector property needs every one of its components animated to be carried over. |
| This Shape Changer deletes the shape "*shape*". AvatarDoc Shapes can set a value, not delete a shape. | Holding a shape at a value is all `AvatarDoc Shapes` can do; delete the shape with Modular Avatar or a mesh tool instead. |

| Note shown | Meaning |
|---|---|
| The action "*action*" was not carried over. | That VRCFury action has no equivalent AvatarDoc target. |
| The toggle's transition animation was not carried over. | AvatarDoc toggles switch instantly, or fade with `Blend time`; VRCFury's own transition animation is not one of these. |
| The toggle's security lock was not carried over. | AvatarDoc has no VRChat security lock feature. |
| The toggle's separate local state was not carried over. | AvatarDoc has no local only override for a toggle's state. |
| The toggle's hold button behaviour was not carried over. | AvatarDoc has no hold to activate button behaviour. |
| The toggle's inverted rest logic was not carried over. | AvatarDoc has no equivalent setting. |
| Only "*tag*" became the exclusive group; the other tags (*tags*) were dropped. | AvatarDoc's exclusive group is a single name, not a list. |
| The clip's paths are used as they are. VRCFury may have rewritten them at build time, so check that they start at the avatar root. | A converted animation clip action's paths are not adjusted for you. |
| The blend shape "*shape*" is deleted by Modular Avatar; deleting a shape cannot be carried over. | AvatarDoc holds a blend shape at a weight; it cannot delete one. |
| Modular Avatar's Inverted option on "*name*" cannot be carried over; that reaction was skipped. | AvatarDoc has no inverted reaction for that target kind. |
| A material slot that does not exist on "*name*" was skipped. | The renderer does not have that many material slots. |
| A curve's path does not resolve on this avatar; it was skipped. | The animated object is not found under the avatar root. |
| Another layer drives this parameter; that keeps working. | Nothing to fix; this is only a note that the parameter is not exclusively yours. |
| The old off state sets values of its own; they were carried over as this toggle's off values. | Check the values under `Off value` on the entry's targets are the ones you want. |
| The merged animator layer "*layer*" became this entry's own effects. | This item converted to a real entry; its old layer is left out of what stays merged. |
| This item keeps the parameter "*parameter*" and stays driven by the merged animator: *reason* | The item became a parameter control instead of a real entry; the merged animator keeps the layer that drives it. |
| A Button is momentary, which an entry cannot reproduce. | A Button always becomes a parameter control, never a real entry. |
| Anything outside the avatar that set the old parameter, such as an OSC app, has to use the new one. | A real conversion gives the control a new parameter name of its own; update anything external still watching the old name. |
| The menu asset, its parameter declarations and the merged animator come over unchanged. | Shown when `Keep as it is` is ticked on the group; nothing in it becomes a real entry. |
| These items share the parameter "*parameter*", so they convert together as one exclusive group where only one can be on. | A Modular Avatar set where one item is on at a time becomes one exclusive group. |

## Using it with VRCFury and Modular Avatar

AvatarDoc works alongside VRCFury and Modular Avatar on the same avatar; with neither package installed it
behaves exactly as this document describes everywhere else.

**Build order**, in plain words: AvatarDoc builds its toggles, radials, submenus and presets first. Modular
Avatar runs next. AvatarDoc then pins any overridden shapes Modular Avatar just merged in and applies any
pending menu edits to existing or foreign items. VRCFury runs after that. AvatarDoc makes one more pass to pin
any shapes still left and apply any menu edits still pending, to reach VRCFury's own animations. Last of all,
VRCFury's own parameter compressor rewrites the expression parameters asset, which can affect AvatarDoc's own
parameters too.

**Foreign rows**: menu items VRCFury or Modular Avatar add show up in the tree with a `VRCFury` or `Modular
Avatar` chip, and as a tinted wedge in the wheel, on a best effort basis (their path and name, not their full
behaviour). You can rename, re-icon, hide or move them the same way as an existing item; see Editing the existing
menu above. They never count toward AvatarDoc's own page split, since each tool paginates its own items.

**Limits**:

- A VRCFury toggle's own parameter name changes on every build unless the toggle is set to use a global parameter,
  a VRCFury setting on the toggle itself. Give a toggle one if you want to name it from a trigger's `Parameter`
  field or a preset's `Other parameters` list; the parameter pickers list VRCFury's global parameters under a
  `VRCFury` heading.
- A VRCFury feature that is not a Toggle, such as its Full Controller feature, is not read at all and never shows
  up as a foreign row or a conversion candidate.
- VRCFury's own parameter compressor still runs last and may compress AvatarDoc's own parameters along with
  everything else on the avatar; that is VRCFury's normal behaviour, not something AvatarDoc controls.
- VRCFury may show its "broken mix of Write Defaults" dialog on an avatar where AvatarDoc has a blend shape in `Pin`
  mode or a `Follow distance` trigger. Choose `Skip`: AvatarDoc's own pin, distance and pause layers are meant to be
  Write Defaults off, and AvatarDoc's last pass turns them back off even after `Auto-Fix`.

## Platform choice (PC and Quest)

VRChat syncs an avatar's expression parameters by their position in the list, not by name, so a PC build and its
Quest copy have to declare the very same parameters in the very same order. Deleting a toggle on the Quest copy
because its pieces do not exist there breaks that the moment you do it. AvatarDoc's answer is to exclude an
entry instead of deleting it: its parameter still gets planned, with the same name, type, default, saved and
synced flags, in the same position as on the other platform, but it gets no menu control, no FX layers, no scene
default and, for a trigger, no contact receiver, and it takes no menu slot. Its targets are not even checked,
since they are usually the pieces missing on that copy.

- **Component default**: the header's `Platform` row (`Both`, `PC only`, `Quest only`) sets what every entry under
  this component is, unless an entry, or a submenu it sits in, says otherwise.
- **Per entry override**: every entry panel, including a submenu, a trigger, an override and a preset, has its own
  `Platform` field (`Inherit`, `Both`, `PC only`, `Quest only`). A submenu's choice applies to everything inside it
  until something inside picks its own.
- **What the build target decides**: Android and iOS are treated as Quest (mobile); everything else is PC, read
  straight from Unity's own active build target, so switching platform in Unity is what switches which entries
  actually build.
- **What an excluded entry keeps and loses**: it keeps its parameter, so the position and the synced-bit count
  never move between platforms; it loses its menu control, its FX layers, its scene default value, and, for a
  trigger, its contact receiver or PhysBone parameter assignment. A rule (a trigger or an override) or a preset
  excluded this way is skipped entirely at build, apart from a preset's own parameter, which is still planned like
  any other.
- **Chips**: a row excluded on the platform you are currently building for is dimmed and carries a `PC only` or
  `Quest only` chip; a row merely limited to one platform, even while it is the one currently included, carries
  the same chip unshaded, so you can see at a glance what will vanish if you switch build target.
- **`Building for PC` / `Building for Quest`**: a note under the header's `Platform` row names the platform the
  editor is building for right now.
- **The parity warning**: when another avatar open in one of your scenes shares this one's VRChat blueprint id (so
  the two are clearly PC/Quest copies of the same avatar) and its planned parameter list does not match this
  one's, name for name, type for type, in the same order, a warning above the memory bar names the first place
  they differ. It only ever sees avatars that are actually open in a scene right now.

## Summary and lock

For anyone who shares or sells a prefab with an AvatarDoc on it, a `Summary` button sits in the header's title
row. It opens a panel with a `Plain`/`Markdown` choice, a read-only text box, and a `Copy` button that puts the
text on the clipboard (`Copied.` flashes briefly to confirm it). `Plain` is meant for a store page; `Markdown`
adds a parameter table for a place that renders it.

The summary describes this one component: its name; the synced bits it adds, next to AvatarDoc's total on the
avatar; every parameter it declares (name, type, synced, saved, default; an unsynced one is marked `local`); its
menu as an indented tree, with its install path and, per submenu, how many of the platform's slots it uses; what
it needs (NDMF and the VRChat SDK always, Modular Avatar or VRCFury only when a trigger or a preset actually reads
one of their parameters); how many contact receivers and PhysBones it uses, if any; its platform choice; every
merged animator (controller name and how many of its layers merge) and every installed menu asset and declared
parameter; every trigger's own condition sentence and, for a gated one, which toggle gates it; every toggle that is
not in the menu; and whatever warnings or errors it is raising right now. A hidden menu item (see Editing the
existing menu) is left out entirely, since it does not actually build; an entry limited to one platform is marked
the same way its chip marks it in the tree.

`Lock` makes the component read only for whoever uses the prefab: the tree's add buttons, Convert, the outfit
wizard, dragging objects onto the tree, every row's edit menu, and every entry's own panel are all disabled, and a
banner across the top of the whole view explains that it is locked, shows the optional `Note for users` you
wrote, and offers `Unlock`. `Install into`, `Install as`, `Submenu name`, `Submenu icon`, `Parameter prefix` and
`Platform` stay editable, since those are the settings a buyer is expected to adjust for their own avatar. A
window already open when the component gets locked, such as Convert or the outfit wizard, refuses the moment you
press its own build button rather than closing on you. Locking changes nothing about what gets built: it only
stops further edits.

## Scene preview

Turn preview on from `Tools > NDM Framework > Enable Previews`, then switch on the entry named `AvatarDoc` under
`Tools > NDM Framework > Configure Previews`. With it on, every entry's detail panel shows a preview row:

- Toggles and condition triggers get a three way switch, `Default`, `On` and `Off`. `Default` clears any override
  and falls back to the entry's own default state; `On` and `Off` force it. A toggle's or a trigger's material
  property and material slot targets preview the same way its objects and blend shapes do.
- Radials get a slider from 0 to 1 instead.
- Presets get the same `Default`, `On` and `Off` control: `On` sets every state and parameter the preset lists,
  once, rather than staying held on; `Off` forces every named control to its own off value instead; `Default`
  clears the preview.
- Blend shape triggers show a note instead of the control: they always follow the source shape's current weight
  in the scene and cannot be forced on or off. `Smooth` shows the share of each effect that weight gives (a shape
  halfway between `Starts at` and `Fully on at` shows half of each effect), `Switch` shows all or nothing.
- A requirement that is a toggle is read from the preview's own state, the same as `Only while`. A requirement
  the scene cannot know (a parameter, a contact or a PhysBone) counts as met, and a note under the preview row
  says so. This holds for blend shape triggers and for a `Fade` trigger forced `On`.
- A contact or PhysBone trigger set to `Flip a toggle` shows a single `Simulate touch` button instead: pressing it
  flips the target toggle's preview, not the trigger's own state. One set to `Hold effects` previews like any
  other condition trigger, with the three way switch above.
- Overrides show the same `Default`, `On` and `Off` control too, disabled, with a note underneath explaining why:
  their pinned values are always applied in the preview, so there is nothing for the control to switch.

Custom animations do not show in the scene preview; the panel says so under the Animations list, so check a control
that uses one by testing it in Play mode instead.

A condition trigger shows nothing in the preview until you press `On` or `Off` on it; it starts idle rather than
guessing a state, and `Default` puts it back to idle. The header's `Reset preview` button clears every override you have set,
on any entry, in one press. The preview only writes to NDMF's temporary proxy renderers, never to your original
renderers or GameObjects.

## The wheel and the window

The tree's `Wheel` button shows the selected submenu the way VRChat's own radial menu draws it: always eight
slots, filled clockwise from the top, each used slot showing the entry's own icon texture if it has one, otherwise
a kind glyph, and a label. Clicking a wedge selects it in the tree, the same as clicking its row; clicking a
submenu wedge also makes it the wheel's new centre, and the crumbs above the wheel step back out again. Triggers
and overrides take no menu slot, so the wheel hides itself while one of their rows is selected. Existing and
foreign menu items take a slot too, tinted to match their chip; a foreign submenu's wedge opens into its own
children the same way one of your own does.

Open `Tools > DocMe > AvatarDoc` from the Unity menu for a dockable window with the same tree, detail and wheel
as the inspector, laid out side by side. The window follows your selection: your own AvatarDoc component if the
selected object has one, else the first component under the selected avatar, else the first component under the
selected object.

## Testing without play mode

The wheel has a `Select`/`Test` switch next to its crumbs, off (`Select`) by default and remembered separately for
the inspector and the dockable window. While it is `Select`, the wheel behaves exactly as described above: a click
selects the row. Switch to `Test` and a click drives the very same scene preview the panel's own preview row uses,
instead of selecting anything, and changes nothing in your data:

- A **toggle** wedge flips between its effective on and off state; a member of an exclusive group switches its
  siblings off the same way the panel's preview already does.
- A **preset** wedge applies the preset, the same as pressing `On` in its panel.
- A **radial** wedge opens a 0 to 1 slider under the wheel; dragging it writes the radial's preview value live, and
  a second click on the wedge closes the slider without losing the value.
- A **submenu** wedge, and the back wedge, still navigate the wheel as usual.
- An **existing** or **foreign** wedge does nothing: neither has a scene preview to drive.

A wedge with an active preview carries the accent fill (an on toggle, an applied preset) or a thin accent arc
around its rim (a radial, at its current value), so you can see what is on without opening a single panel.

Below the wheel, while `Test` is on and the avatar has any, a `Triggers` strip lists every trigger sourced from a
parameter, a contact or a PhysBone (not one sourced from a toggle or a blend shape, since those already work
through their own wedge or need no simulation) as a small press button: press it once to set that trigger's
preview override on, press again to clear it. A trigger in `Flip a toggle` mode flips its target toggle's preview
instead, exactly like the `Simulate touch` button in its own panel.

`Reset`, shown next to the switch while `Test` is on, clears every preview override on the wheel in one press, the
same `Reset preview` the header already offers. Leaving `Test` and going back to `Select` does not clear anything,
so you can keep a state visible while you edit; the header's own preview indicator keeps showing that overrides
are active either way.

Preview overrides are global to the editor session rather than kept per avatar, so two copies of one avatar (a PC
copy and its Quest twin) share them, and a script reload clears them all.

As with every scene preview, `Test` mode needs preview switched on to actually change anything on the avatar
(`Tools > NDM Framework > Enable Previews`, then the `AvatarDoc` entry under `Configure Previews`); without it,
pressing a wedge still remembers the override, you just will not see it on the mesh until preview is on.

## Messages

AvatarDoc reports problems both live in the inspector (a warning icon on the row, a box in the detail panel) and
at build time. In the tables below, an italic word such as *name* or *component* stands for the actual name, value
or component AvatarDoc fills in there; every other word is exactly what the message says. Every warning and
error you might see:

| Message | What to do |
|---|---|
| The entry "*name*" pointed at a submenu that no longer exists, so it was placed at the top of its component instead. Move it into a submenu that exists, or leave it here. (*component*) | Move it into a submenu that exists, or leave it where it is. |
| "*name*" has a missing target: an object, renderer, material slot or blend shape it pointed at is gone, so that row was skipped. Open "*name*" and fix or remove the empty target row. (*component*) | Open the entry and fix or remove the empty target row. |
| "*name*" targets an object that is not part of this avatar, so that target was skipped. Pick a target that is part of this avatar. (*component*) | Point the target at an object under the same avatar. |
| The entry "*name*" shares its internal id with another entry (usually a copied component). Both still work; the inspector will assign a new id. (*component*) | Nothing to do; reopening the inspector repairs the id. |
| "*name*" has no targets, so nothing was generated for it. Add an object, blend shape or other target with `+ Add`. (*component*) | Add an object, blend shape or material target, or delete the entry. |
| The exclusive group "*group*" has more than one toggle set as default; "*name*" is used because it comes first. Turn off `Default on` on the others. (*component*) | Turn off `Default on` on the toggles you did not mean to start on. |
| The exclusive group "*group*" has two toggles with the value *value*. Give each toggle its own value or set them to automatic. (*component*) | Change one toggle's value, or set both back to 0 for automatic. |
| The parameter "*parameter*" is already *type* here, but this control needs a *type* parameter. Pick a different name, or match the type. (*component*) | Pick a different parameter name, or change the control so it matches the existing parameter's type. |
| Synced parameters need *needed* bits but VRChat allows *limit*. AvatarDoc adds *added* of them. Turn off `Synced` on toggles that do not need to sync, or remove some. | Turn off `Synced` on toggles that do not need to sync, or remove some. |
| Synced parameters need *needed* bits but VRChat allows *limit*. AvatarDoc adds *added* of them. VRCFury is installed and compresses parameters at upload until they fit, so the build goes on. To fit without VRCFury's help, turn off `Synced` on toggles that do not need to sync, or remove some. | Usually nothing: VRCFury makes the parameters smaller when you upload, and says so itself if they still do not fit. If you would rather not rely on that, turn off `Synced` on toggles that do not need to sync, or remove some. |
| The avatar has no FX playable layer, so AvatarDoc has nowhere to add toggles. Add an FX controller under `Playable Layers` in the avatar's VRC Avatar Descriptor. | Add an FX controller under `Playable Layers` in the avatar's VRC Avatar Descriptor. |
| Your avatar's own FX layers mix Write Defaults on and off: *count* use it on, *count* use it off. Make every layer use the same setting; AvatarDoc otherwise builds with Write Defaults on. | Make every one of the avatar's own FX layers agree on Write Defaults, or leave it: AvatarDoc still builds, using Write Defaults on either way. |
| The icon on "*name*" is *width*x*height* pixels or is not compressed, and VRChat's upload check rejects that. Re-import it at 256x256 pixels or smaller with compression turned on. | Re-import the icon at 256 by 256 pixels or smaller, with compression turned on. |
| Clip "*clip*" is one of VRChat's own proxy animations, so "*name*" left it alone and does not follow the shape while it plays. This is expected and needs no fix. | Nothing to fix; VRChat owns this animation and no tool can change it. |
| The trigger "*name*" refers to a toggle, parameter, or blend shape that no longer exists, so it was skipped. Pick a source under `When` for it. (*component*) | Pick a valid toggle, parameter or blend shape under `When`. |
| The trigger "*name*" reads the parameter "*parameter*", but nothing on this avatar declares it, so it is treated as a Float. Declare "*parameter*" under `Parameters` with its real type if it should be a whole number or true/false. | Check the parameter name is correct, or declare it under `Parameters` with the right type. |
| "*name*" lists its own shape "*shape*" as one of its effects, so that effect is ignored. Remove "*shape*" from its effects list. | Remove that target row; a blend shape trigger cannot also drive its own shape. |
| "*name*" is set to Replace but has no effect it can build, so the shape's own curve in the clip "*clip*" was kept. Add an effect to the trigger, or set its Mode to Add. | Check the trigger's targets exist on this avatar, or switch `Mode` to `Add`. |
| "*name*" follows *shape*, but that shape also moves in *clips*, outside the FX layer, where the trigger cannot see it. Move that animation into the FX layer if the trigger should react to it there. | An animated value only reaches its own controller's layers; move those animations to FX, or accept that the trigger ignores them. |
| No animation in the FX layer moves *shape*, so "*name*" cannot follow that shape and its effects stay where the shape rests. Animate *shape* in an FX animation, or pick a shape one already moves. | Nothing on the FX layer moves that shape when the avatar is built, so the trigger has nothing to follow; animate it there or pick another shape. |
| "*name*" follows *shape*, which VRChat moves itself for blinking or lip sync outside every animation, so the trigger never sees it move. Pick a shape your own animations move instead. (*component*) | Pick another shape; VRChat's blinking and lip sync never go through an animation the trigger could read. |
| "*name*" starts at *start* but is fully on at *full*, which is not above it, so it is fully on just above Starts at instead. Set Fully on at higher than Starts at. (*component*) | Drag `Fully on at` above `Starts at`. |
| "*name*" reads only the card its shape is in, so the conditions in its other cards are kept but not used. Move what you need into the first card with + And, or remove the other cards. (*component*) | Move the requirements you want into the first card, then remove the other cards. |
| Fade on "*name*" reads only the card its first contact is in: that contact drives the fade and the rest of that card are requirements. The conditions in its other cards are kept but not used; move what you need into the first card with + And, or remove the other cards. (*component*) | Move the requirements you want into the first card, then remove the other cards. |
| The trigger "*name*" has no object set for `Anchor`, so its contact has nowhere to attach. Pick an object for `Anchor`. (*component*) | Set an `Anchor` for the contact, usually a bone. |
| The trigger "*name*" has no tags ticked under `Touched by`, so nothing can touch it. Tick at least one tag. (*component*) | Tick at least one tag under `Touched by`. |
| The trigger "*name*" has both `Self` and `Others` off, so its contact can never fire. Turn on `Self`, `Others`, or both. (*component*) | Turn on `Self`, `Others`, or both. |
| The trigger "*name*" has no PhysBone chosen, so it has nothing to watch. Pick a PhysBone in its `PhysBone` field. (*component*) | Point the trigger at a PhysBone on the avatar. |
| The trigger "*name*" cannot flip its `Toggle`: none is picked, or the one picked is not a toggle. Pick a toggle for it to flip. (*component*) | Pick a toggle in the `Toggle` field. |
| "*name*": the parameter "*parameter*" needs to be *type* here, but the avatar already has it as *type*. Rename the PhysBone's parameter, or match the type. (*component*) | Give the PhysBone a parameter name that does not collide with an existing parameter of another type, or match the type. |
| "*name*": its `Anchor` object or PhysBone belongs to another avatar. Pick one that is part of this avatar. | Point the `Anchor` or `PhysBone` field at an object under this avatar. Usually the component was copied from another copy of the avatar and kept pointing back at it. |
| "*name*" watches the PhysBone "*physbone*", but Modular Avatar renames its parameter at build, so the trigger would watch the old name. Give "*physbone*" a parameter that Modular Avatar does not rename. (*component*) | Use a PhysBone whose parameter prefix no `MA Parameters` above it lists as internal or renamed, or take the prefix out of that list. |
| Override "*name*" found no animation that moves "*shape*" on this avatar, so it only set the mesh's own value. Check that "*shape*" and its renderer are the ones you meant, or remove this override if that is all you wanted. (*component*) | Nothing to fix if the mesh-only change is what you wanted; otherwise check the shape name and renderer, or add an animation for it. |
| Overrides "*name*" and "*other*" both hold "*target*" at different values; "*other*" wins because it comes later. Remove one of them, or match their values. (*component*) | Point one override at a different shape, or delete the one you did not mean to keep. |
| Override "*name*": its renderer no longer exists at build time, so the shape "*shape*" was not pinned. Point it at a renderer that still exists on this avatar. | Check that another tool is not deleting or replacing that renderer during the build. |
| The change made to "*item*" would move its submenu into itself, so it was not applied. Pick a different `Move to` path for it. (*component*) | Point the move at a different submenu; moving one into itself or its own descendant is not possible. |
| The change made to "*item*" points at an AvatarDoc entry, so it was ignored. Edit "*item*" directly in its own panel instead. (*component*) | Edit that entry directly rather than through a menu edit. |
| The change saved for "*item*" cannot apply: that menu item was no longer found, so nothing changed. Use `Remove` to clear this edit. | Check the item was not deleted or renamed elsewhere; if it is gone for good, press `Remove` on the warning in the panel header. |
| Preset "*name*" sets nothing yet, so pressing it does nothing. Add a control or parameter with `+ Add`. | Add a state or a parameter to the preset, or delete it. |
| The preset "*name*" refers to a control that no longer exists, so that row was skipped. Remove that row from the preset. (*component*) | Open the preset and remove that row. |
| The preset "*name*" turns on two members of the exclusive group "*group*" at once, but only one can be on; only the first listed is applied. Remove one of them from the preset. (*component*) | Turn on only one member of the group in this preset. |
| The parameter "*parameter*" set by the preset "*name*" is not declared in the expression parameters or the FX controller, so it was written anyway. Declare "*parameter*" under `Parameters` if you want VRChat to recognise it. (*component*) | Declare the parameter under `Parameters`, or check the name is correct. |
| "*name*": no material on "*renderer*" has the property "*property*", so that row was skipped. Pick a property that exists on "*renderer*"'s materials. | Check the property name, or pick one the renderer's materials actually have. |
| "*name*": the materials on "*renderer*" already disagree on "*property*". Turning it off writes the first material's value to all of them; edit the materials first if you want a different one used. | Give those materials the same value for that property, or split the renderer, if the flattening is not what you want. |
| "*name*": the clip "*clip*" is one of VRChat's own proxy clips, so it was not merged. This is expected and needs no fix. | Pick a different clip; VRChat owns its own proxy animations and no tool can rewrite them. |
| "*name*": *count* value(s) in its custom animation do not match anything on this avatar, so they were left out of the off state. Check that clip's object paths and properties are correct. | Check the clip's paths match this avatar, or set `Path root` to where the clip was made. |
| The layer "*layer*" was already removed or renamed in the FX controller, so there was nothing here to remove. If you renamed it, update the name under `Layers left out of the build`. (*component*) | Nothing to fix if it is already gone. If you renamed it, update the name under `Layers left out of the build`. |
| Layer "*layer*" was not removed because the avatar uses animator layer control, and removing a layer would shift the ones after it by position. Remove that layer control first if you still want "*layer*" gone, then build again. (*component*) | Remove the layer control behaviour first if you still want it gone; animator layer control refers to layers by position, so AvatarDoc will not risk shifting them. |
| "*layer*" was not removed because *count* layers share that same name, so removing by name would take all of them. Give the FX controller's layers unique names, then build again. (*component*) | Give the layer a name of its own in the FX controller; AvatarDoc will not remove several layers at once by a shared name. |
| The layer "*layer*" and its parameter were kept because another controller in the build switches FX layers by their position, and removing one would shift the rest. Remove that layer control behaviour first if you still want "*layer*" gone, then build again. | Nothing to fix if that is fine; otherwise remove the animator layer control behaviour that switches layers by position, then try `Remove from avatar` again. |
| This merged animator has no `Controller` set, so nothing was merged from it. Pick a controller in its `Controller` field. (*component*) | Pick a controller in the merged animator's panel, or delete it if you no longer need it. |
| The merged animator "*name*" is an Animator Override Controller whose own `Controller` field is empty, so it has nothing to merge and was left out. Set that field on "*name*", or pick a different animator. (*component*) | Click the override controller in the Project window and pick a controller in its own `Controller` field, or pick a different animator in the merged animator's panel. |
| "*name*" has no layer named "*layer*" to leave out, so that entry under `Layers` does nothing. Remove it, or fix the spelling to match a real layer. (*component*) | The controller was probably edited since; update the layer checklist in the panel. |
| The merged animator "*name*" has its `Path root` set to an object outside this avatar, so it was not merged. Point `Path root` at an object on this avatar, or leave it empty. (*component*) | Point `Path root` at an object under this avatar, or leave it empty. |
| The merged animator "*name*" needs the parameter "*parameter*" as *type*, but the avatar already has it as *type*. Match the type or rename one of them. (*component*) | Rename the parameter on one side to match, or make sure every control on it is a parameter control so it can adopt the merged type on its own. |
| Animator "*name*": the layer "*layer*" is synced to "*layer*", but it is left out of the merge. Include it too, or turn off the layer sync in the controller. (*component*) | Also uncheck the layer it is synced to in the layers checklist, or check the first one back on. |
| The merged animator "*name*" changes layer weights itself, so leaving layers out of it would point those changes at the wrong layer. Merge every layer, or remove the layer control. (*component*) | Check every layer back on in the panel, or remove the animator layer control behaviour from the controller. |
| "*name*" drives the parameter "*parameter*", but nothing on this avatar reads it, so it has no effect yet. Remove it, or point it at a parameter a merged animator, VRCFury or Modular Avatar actually uses. (*component*) | Check the parameter name matches what a merged animator or another tool actually reads, or give the control real targets instead. |
| The toggle "*name*" turns on `Set a value when off` for the blend shape "*shape*", but that shape's effect is set to `Pin`, which writes nothing while off. Turn off `Set a value when off`, or change "*shape*" away from `Pin`. (*component*) | Turn off `Pin` for that shape, or turn off `Set a value when off`; the two cannot both apply. |
| The trigger "*name*" needs a toggle for `Only while`, but the one picked is missing, a preset, or the toggle this trigger already flips. Pick a different toggle for `Only while`. (*component*) | Point `Only while` at a different toggle. |
| "*name*" has too many conditions for VRChat to build: *count* group(s) create *count* way(s) to turn it back off. Use at most 4 groups of 4 conditions each. | Remove a group or a condition until it fits within 4 groups of 4 conditions each. |
| "*name*" has no condition under `When`, so it never fires. Add at least one condition there. | Add at least one condition to at least one group. |
| "*name*" has a group with no conditions in it, so that group was left out. Add a condition to it with `+ And`, or remove the empty group. | Add a condition to the group, or remove the group with its own remove button. |
| This menu asset install has no `Menu asset` chosen, so nothing was installed. Pick a menu asset for it. (*component*) | Pick a `VRCExpressionsMenu` asset in the menu asset's panel, or delete the row. |
| The parameter "*parameter*" is declared under `Parameters`, but an entry already plans one with this name, so the entry's own settings win. Remove this declaration, or give it a different name. (*component*) | Remove the declaration if the entry already covers it, or give the declaration a different name. |
| The declared parameter "*parameter*" is a *type* here, but the avatar already has it as *type*. Match the type or rename one of them. (*component*) | Change the declaration's type to match, or give it a different name. |
| Nothing declares the parameter "*parameter*" that the installed control "*control*" drives, so VRChat ignores that control. Add a declaration for "*parameter*" under `Parameters`. (*component*) | Press `Add declaration` in the menu asset's panel, or declare it yourself under `Parameters`. |
| AvatarDoc Shapes on "*piece*": the mesh for "*shape*" is missing or not part of this avatar, so that row was skipped. Point it at a mesh that is part of this avatar. (*component*) | Point the row's mesh at a mesh under the same avatar. |
| AvatarDoc Shapes on "*piece*": "*mesh*" has no shape called "*shape*", so that row was skipped. Pick a shape that exists on "*mesh*", or remove the row. (*component*) | Pick a shape the mesh really has, or fix the name. |
| AvatarDoc Shapes on "*piece*" has follow rows, but the object carries no skinned mesh of its own. Put the component on the piece's mesh or remove those rows. (*component*) | Move the component onto the object with the piece's skinned mesh, or delete the follow rows. |
| "*piece*" and "*other*" both hold the shape "*shape*" while they are on, at different values. The larger value wins when both are worn: give them the same value, or remove the rule from one of them. (*component*) | Nothing to fix if the larger value is what you want; otherwise give the two pieces the same value. |
| The animation "*clip*" fades "*piece*" in and out rather than switching it at a fixed point, so AvatarDoc left it alone and did not add this piece's shape rules to it. Change it to a plain on/off switch if you want those rules to apply there too. (*component*) | Make the object's own curve a constant 0 or 1 if that animation should count as switching the piece. |
| The animation "*clip*" already writes the shape "*shape*" itself, so it keeps its own values instead of the ones "*piece*" asks for. Remove "*shape*" from that animation if you want "*piece*"'s rule to apply there instead. (*component*) | Nothing to fix if that animation should decide; otherwise take the shape out of that animation. |
| This avatar and "*avatar*" share a blueprint but their parameters differ at position *position*: "*parameter*" here, "*parameter*" there, so other platforms would see the wrong toggles. Rebuild both platforms from the same menu order to fix it. | Make the two copies' menus match again, or check you meant to edit this one. |

A few messages are informational and never block an upload:

| Message | Meaning |
|---|---|
| AvatarDoc created the submenu "*submenu*" for the install path of *component*. | A new submenu was created to match an install path. |
| "*submenu*" joins the submenu of the same name that is already in the menu. (*component*) | Two submenus of that name ended up as one, whether they came from two components or from your entry and the avatar's own menu. |
| The submenu "*submenu*" has more controls than VRChat allows on one page; the extra controls were moved to a "More" page. Move some controls into their own submenu to avoid the extra page. | The submenu was split automatically; move some controls into their own submenu to avoid another one. |
| "*parameter*" already exists on the avatar, so its own saved, synced and default settings apply. (*component*) | A custom parameter name reused an existing expression parameter. |
| *target* is changed by more than one control (*entries*); the one built later wins: later menu entries first, triggers after every menu control. (*component*) | Two entries share a target; build order decides which wins. |
| "*target*" is written by *entries*; the override wins over every control, whatever the menu order. (*component*) | An override and a control share a shape; the override always wins. |
| The animation "*clip*" also moves something "*name*" moves, so while that trigger is on, the trigger's own value shows there. | A clip that moves a blend shape trigger's shape also moves one of its effects; the trigger's layer, last in FX, wins while it is on. |
| "*parameter*" already exists in the avatar's animator, so this control drives it without adding a new parameter, but it will not sync to other players over the network. Declare "*parameter*" under `Parameters` if you need it to sync. (*component*) | A custom parameter name matched an animator-only parameter; it works locally, but will not sync unless you declare it. |
| Override "*name*" holds "*shape*" at its value in *count* animations. | The override rewrote that many clips to hold the shape at its pinned value. |
| "*name*" is in an exclusive group, so its blend time is not used. | A blend time was set on a toggle that also belongs to an exclusive group, which always switches instantly. |
| "*name*": *count* generated value(s) replace the same curve in its custom animation. | The control's own generated value won over the custom clip on the same curve. |
| "*name*": its toggle is not built on this platform, so the touch does nothing here. | Nothing to fix; the flip target simply is not part of this platform's build. |
| The default of group "*group*" is not built on this platform, so nothing in it is on by default here. | Nothing to fix if that is what you meant; otherwise make a member that this platform builds the default one. |
| "*name*" left the expression parameters and freed *count* synced bits. | `Remove from avatar` succeeded for that parameter; the freed bits are available again. |
| "*name*" stays in the expression parameters: *reason* | `Remove from avatar` could not free that parameter; the reason names what still needs it. |
| The animation "*clip*" already writes "*shape*" on the piece "*piece*", so it keeps its own values. | That animation decides the piece's own shape; the follow row leaves it alone. |
| "*piece*" is always on, so its shape values are simply applied to the avatar. (*component*) | Nothing ever switches that piece, so its while-on values are written once. |
| Nothing turns this object on, so its shapes are never applied. | The piece is off in the scene and no animation switches it: give it a toggle, or switch it on in the scene. |
| "*name*" has no menu button, so nothing can turn it on unless something else drives it. Turn on `Default on`, add it to a preset, or add a trigger that flips it. (*component*) | An `In menu` off toggle with no flip trigger or preset to reach it, and not `Default on` either: nothing this avatar does will ever change it. Turn on `Default on`, add it to a preset, or give it a flip trigger. |

Convert shows its own notes and reasons in its window rather than on a tree row; see the tables under Convert above.

## Limits

- Eight controls per menu, VRChat's own limit. A ninth control moves automatically into a chained `More` submenu at
  build time, and the inspector's tree shows those overflow pages too, labelled `More (page 2)`, `More (page 3)` and
  so on, so what you see there is what gets built.
- 256 bits of synced parameter memory in total, shared between AvatarDoc and everything else on the avatar. The
  memory bar shows the avatar's own usage, AvatarDoc's added share and the bits still free.
- One FX playable layer per control: one per toggle, one per radial, one per exclusive group, one per condition
  trigger, one per blend shape trigger. A blend shape trigger also adds one hidden animator value, never synced.
- `AvatarDoc Shapes` writes into the FX controller only, and only into the clips that exist right after Modular
  Avatar; a clip VRCFury merges later in the build is not seen, and a Gesture or Action clip is left alone.
- An override rewrites the animations present when its two passes run, right after Modular Avatar and again right
  after VRCFury; it cannot reach a layer some other tool appends to the avatar after both of those points.
- Icons made by `Capture icon` are always 256 by 256 pixels. The capture reads the scene exactly as it currently
  sits, so a blend shape or material effect shows its current scene value rather than the toggle's On state. A piece
  switched off in the scene is the one exception: it is drawn anyway, without being switched on.
- Convert only offers a hand made animator layer whose parameter drives exactly one simple, unshared on/off layer
  with a plain animation clip; anything more complex is listed with the reason it was left out instead of a
  tickable row, and a VRCFury feature that is not a Toggle is not offered either. A Modular Avatar menu item under
  a Merge Animator is offered as a toggle or a radial, and a Button there is offered as a plain parameter control
  (nothing an entry builds is momentary, so its layer stays merged and the row says so); a two axis or four axis
  puppet and a submenu item are the ones listed with their reason instead. Without a Merge Animator only a Toggle
  is offered. See Convert above for what each reason means.
- On a prefab instance, six fields no longer show Unity's own blue override bar or offer `Revert`/`Apply` from
  their own right click menu: `Install as`, `Submenu name`, `Submenu icon`, an entry's `Icon`, and a trigger's
  `Condition` and `Mode`. The edit is still recorded normally, and `Revert`/`Apply` on the whole component still
  works; only the per-field affordance is gone.
- The inspector picks up an outside change, such as an undo or another tool editing the same avatar, within about a
  frame, instead of on a poll that could take up to a quarter of a second.
- Contacts are sphere shaped only; there is no capsule option.
- AvatarDoc only receives contacts; it cannot create a contact sender.
- `Closer than` is a threshold, not a continuous drive: a proximity trigger switches its effects on and off at the
  fraction you set, it does not blend them by distance.
- Shrink suggestions are found by name only, comparing a piece's name against blend shape names on the avatar's
  other meshes; nothing checks that a matched shape is actually the right one.
- The platform parity check only ever compares against another avatar's AvatarDoc components that are open in a
  scene right now; it cannot see a copy sitting in a closed scene or another project.

## License

AvatarDoc is MIT licensed. Copyright 2026 DocMe, https://github.com/DocMeVR . The full text is in LICENSE.md.
You may use it, change it and ship it inside paid avatar packages. Keep the copyright notice with it.

The popup menu is modelled on ThryEditor's RetainedMenu by Thryrallo (MIT).
