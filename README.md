# AvatarDoc

Build a VRChat avatar's expressions menu in one inspector: toggles, radials, outfit selectors and touch reactions, made at upload through NDMF.

<img src="https://raw.githubusercontent.com/DocMeVR/AvatarDoc/main/Website~/src/assets/shots/home/hero.png" alt="The AvatarDoc inspector with a menu of toggles, a radial and an Outfits submenu, and the Smile toggle selected." width="480">

## What it does

You add one AvatarDoc component to your avatar and describe the menu in it. AvatarDoc builds the animator layers, parameters and menu when you upload or press Play. Nothing is baked into the avatar while you edit.

- **Toggles, radials and submenus** that show objects, move blend shapes or change materials. Toggles can join a group where one is on at a time.
- **Outfit selectors** made from a handful of objects in one window.
- **Colour and glow** changes: a material value that follows a toggle or a slider.
- **Touch reactions**: a hand or finger near a spot on the avatar fades something in or out.
- **Converting** toggles made with VRCFury or Modular Avatar into AvatarDoc entries, and editing a menu that already exists.

<p>
<img src="https://raw.githubusercontent.com/DocMeVR/AvatarDoc/main/Website~/src/assets/shots/start-here/first-toggle-done.png" alt="The AvatarDoc inspector with a Hat toggle selected and its Objects list holding the hat." width="230">
<img src="https://raw.githubusercontent.com/DocMeVR/AvatarDoc/main/Website~/src/assets/shots/start-here/first-outfit-done.png" alt="The AvatarDoc menu tree with an Outfits submenu selected and open, holding three outfit toggles." width="230">
<img src="https://raw.githubusercontent.com/DocMeVR/AvatarDoc/main/Website~/src/assets/shots/guides/colour-and-glow-property.png" alt="A Glow toggle's Properties row with the renderer, the property button and the On value colour." width="230">
<img src="https://raw.githubusercontent.com/DocMeVR/AvatarDoc/main/Website~/src/assets/shots/guides/react-to-touch-fade.png" alt="A Cheek poke trigger's panel with its contact settings." width="230">
</p>

## Requirements

- Unity 2022.3
- VRChat SDK 3.10 or newer
- NDMF 1.14.1 or newer
- Modular Avatar is optional. AvatarDoc does not need it.

## Install

1. Add the DocMe listing to the VRChat Creator Companion: open **https://docmevr.github.io/vpm** and press **Add to VCC**, or add `https://docmevr.github.io/vpm/index.json` under Settings > Packages > Add Repository.
2. AvatarDoc needs NDMF, which comes from its author's listing: add `https://vpm.nadena.dev/vpm.json` the same way (the DocMe listing page has a second **Add to VCC** button for it). If you use Modular Avatar, you have it already.
3. In your avatar project, press **Manage Project** and add **DocMe AvatarDoc**. VCC adds NDMF with it.

Without VCC: download the `.zip` from the latest release at https://github.com/DocMeVR/AvatarDoc/releases and unpack it into a new folder `Packages/dev.docme.avatar-doctor` in your project, or import the `.unitypackage`. Install NDMF first.

## Quick start

This makes a toggle that shows or hides a hat. The full tutorial, with pictures, is at https://docmevr.github.io/AvatarDoc/start-here/first-toggle/

1. In the Hierarchy, select your avatar.
2. In the inspector, press **Add Component**, type `AvatarDoc` and pick it.
3. In the welcome card under the empty menu tree, press **Make a toggle**.
4. Type the name of your hat and pick it in the list. A toggle named after the hat appears in the tree, and its **Objects** list holds the hat.
5. Press Play with Gesture Manager to try it, or upload as usual. AvatarDoc builds the menu either way.

## Languages

AvatarDoc speaks English, Japanese and Simplified Chinese. The language button at the right end of the **Simple | Advanced** row changes it. See [Translating AvatarDoc](https://github.com/DocMeVR/AvatarDoc/blob/main/Docs~/translating.md) to add one.

## Documentation

Everything else is on the docs site, [docmevr.github.io/AvatarDoc](https://docmevr.github.io/AvatarDoc/):

- [Start here](https://docmevr.github.io/AvatarDoc/start-here/install/): install, and three first tutorials.
- [Guides](https://docmevr.github.io/AvatarDoc/guides/toggle-clothing/): one page for each thing you want to do.
- [Reference](https://docmevr.github.io/AvatarDoc/reference/inspector/): every control and setting, and what each message means.
- [How it works](https://docmevr.github.io/AvatarDoc/how-it-works/layers-and-pins/): what AvatarDoc builds, and how it fits with other tools.
- [Help](https://docmevr.github.io/AvatarDoc/help/troubleshooting/): troubleshooting, the FAQ, how to report a bug and the changelog.

Start here is also in [日本語](https://docmevr.github.io/AvatarDoc/ja/start-here/install/) and [العربية](https://docmevr.github.io/AvatarDoc/ar/start-here/install/).

## How this was made

The code in this repository was written by AI (Claude, by Anthropic). The idea, the design, every feature, the
testing on real avatars and the decisions on how it should behave are mine (DocMe). I planned it and directed the
work; the AI typed it out. Bug reports and pull requests are welcome, and anything that gets merged goes through my
own review.

## License

AvatarDoc is MIT licensed. Copyright 2026 DocMe, https://github.com/DocMeVR . The full text is in LICENSE.md.
You may use it, change it and ship it inside paid avatar packages. Keep the copyright notice with it.

The popup menu is modelled on ThryEditor's RetainedMenu by Thryrallo (MIT).
