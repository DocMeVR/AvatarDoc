# Maintaining the docs site

The docs site lives in `Website~/`. It is an Astro Starlight site, built and published by GitHub Actions. This guide says how to keep it right when the tool changes. It is for people who work on AvatarDoc and it never ships in the package.

The rule: any change that adds, renames or removes a control, a setting or a message also updates the docs pages that show it, and re-shoots their screenshots, in the same change.

## Where things come from

Most facts are written once.

- **UI labels** come from `Editor/Localization/AvatarDoctor.en-US.json`. A page writes a label as `<Label k="ad.ui.add.toggle" />` and the build prints the current text. If you rename a button in the tool, every page changes with it. If you remove a key that a page uses, the build fails and names the page and the key.
- **Generated pages** are never edited by hand: Messages (from the English table and `Website~/src/data/message-notes.yml`), Changelog (from `CHANGELOG.md`), Glossary (from `Docs~/glossary.md`) and Help translate (from `Docs~/translating.md`). Each one starts with a comment that names its source, and its Edit page link points at that source on GitHub.
- **Addresses and the version** come from `Website~/src/data/site.json`. The version is read from `package.json` and printed on every page as "Docs for AvatarDoc x.y.z" in the footer, so it follows each release by itself.
- **Links from the tool** into the site are listed in `Website~/src/data/tool-links.json`. The build checks that each page and anchor exists.

## Add a page

1. Pick the kind: tutorial, guide, reference or troubleshooting entry.
2. Copy the matching file from `Website~/templates/` into the folder under `Website~/src/content/docs/`: `start-here`, `guides`, `reference`, `how-it-works` or `help`. Delete the template comments.
3. Fill in the title, the description and the steps. Write each control with `<Label k="..." />`.
4. Place the page in the sidebar. Start here, Guides, How it works and Help sort by `sidebar: order:` in the front matter, then by title: give a new page an order that puts it where a reader expects it. Reference is a hand written list in `Website~/astro.config.mjs` (the `reference/...` entries): add the new page there, or it is built but missing from the sidebar.
5. Add a picture with `<Shot name="<page>/<shot>" alt="..." />` only where a reader would be lost without it. Write the alt text in one sentence of 150 characters or less, ending with a full stop.
6. Run `npm run build` in `Website~/`.

Setting and message headings get a fixed link with `### Objects [#objects k=ad.ui.target.objects]`. The part after `#` is the link. The `k=` part makes the build fail when the label in the tool changes and the heading does not. Changing the wording of a heading never breaks a link.

## Update pages after a UI change

1. Find the pages that use the label. Search the pages for the key: `grep -r "k=\"ad.ui.add.toggle\"" Website~/src`. For a changed message, search for its key in `Website~/src/data/message-notes.yml` too.
2. Change the text. A renamed label needs no edit, because the page reads it from the table.
3. Re-shoot the pictures of those pages.
4. Run `npm run build`. At the end it prints the coverage report: the `ad.ui.` labels that no page mentions. It does not fail the build. A new control without docs shows up there.

## Re-shoot screenshots

The pictures come from the capture classes in `Tests/Editor/DocsShots/Pages/`, one `DocsShots_*` class for each part of the site. Each class builds a test avatar in code, so what a picture shows is written down as code. The classes are `[Explicit]`, so a normal test run skips them.

- In Unity's Test Runner window, choose EditMode and the category `DocsShots`, then press Run Selected.
- Or run a filtered test run with the class names. To re-shoot one page, filter by the method that takes its picture, for example `DocsShots_Reference1.ToggleOverview` for the Toggle page.
- Pictures are 480 px wide, the value of `DocsShot.InspectorWidth`. The tool itself must still fit 300 to 400 px; the layout tests check that.
- Each picture is a PNG of at most 250 KB, next to a `.json` file with its number markers. `manifest.json` records the AvatarDoc version of each shot.
- Afterwards, look at every picture before you commit. A shot can run without error and still show the wrong thing.

## Add a note to a message

The Messages page lists every message in the English table. When the text alone does not tell a creator what to do, add one line to `Website~/src/data/message-notes.yml`:

```
ad.info.example_key: Nothing to fix. To change it, press **Add**.
```

Use the message key, then a colon, then one line. Write UI labels in bold. Do not use quotes, lists or several lines. A note for a key that no longer exists fails the build.

## Preview locally

Work in `Website~/`.

- `npm install` once.
- `npm run dev` shows the site and reloads as you edit. Search does not work in this mode.
- `npm run build` then `npm run preview` shows the real site, with search.
- `npm test` runs the tests of the build scripts.

The build is strict. It fails on an unknown label key, a missing shot file, an oversize picture or an unknown message note. It also crawls the built pages and fails on any link to a page or a heading that does not exist, naming the page, the link and the target. (The links plugin skips relative links, so this crawl in `scripts/check.mjs` is the check that guards them.) While some pictures are not taken yet, set `DOCS_ALLOW_MISSING_SHOTS=1`. The build then lists the missing shots instead of failing. The build also lists shots no page uses and shots taken on an older minor version.

## Upgrade Starlight

Astro and Starlight are pinned to exact versions in `Website~/package.json`, with a committed `package-lock.json`. No update bot runs. A few times a year:

1. Change the versions and run `npm install`.
2. Run `npm run build` and `npm test`.
3. Commit it alone, as one `chore(site)` commit. The CI build and the links validator are the check.

## Add Japanese or Chinese pages later

1. Add the language to `locales` in `Website~/astro.config.mjs`.
2. Put the pages in a folder named for the language: `ja/` or `zh-cn/`, with the same page names.
3. Add a strings file for the language beside `strings.en-US.json`, from the tool's own table, so `<Label>` prints the translated text.
4. Re-shoot the pictures with the tool set to that language, and name them so they do not replace the English ones.

## Line endings

Files in `Website~/` and this file use LF line endings.
