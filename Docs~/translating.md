# Translating AvatarDoc

Every word AvatarDoc shows comes from a language file in the package's `Editor/Localization` folder. This guide is for
anyone who wants to add a language or keep one up to date. You do not need to know how to program: a language is one
text file. The guide and `glossary.md` sit in `Docs~`, beside this file, so they stay out of the package creators
install.

## How it works

- Every text has a key and the text itself, written as `"ad.ui.mode.simple": "Simple"`. The key (before the colon)
  tells AvatarDoc where the text goes; the text (after the colon) is what people see.
- `AvatarDoctor.en-US.json` is English. It is the default, and the reference every other language follows.
- Every other language is one file with the same keys: `AvatarDoctor.ja-JP.json` for Japanese,
  `AvatarDoctor.zh-Hans.json` for Simplified Chinese.
- AvatarDoc uses the language chosen in NDMF, the same choice Modular Avatar uses. Change it with the language button
  at the top of the AvatarDoc inspector, or in Modular Avatar or NDMF: all of them follow.
- A key that a language does not have shows in English. Nothing breaks.
- When no file has the chosen language, AvatarDoc tries another form of the same language first (Traditional Chinese
  falls back to Simplified Chinese if only that exists), then English. NDMF does the same.

## File names

A language file is named `AvatarDoctor.<code>.json`, where `<code>` is the language code: `ja-JP`, `zh-Hans`,
`zh-Hant`, `ko-KR`, `fr-FR`. These are the codes NDMF and Modular Avatar use. Save the file as UTF-8, with or without
a byte order mark (BOM).

AvatarDoc finds a language file by its name alone, anywhere in the project. Any other name is ignored, and a file
saved with a code that names no language gets a Console warning saying so.

## Add a language

1. Copy `Editor/Localization/AvatarDoctor.en-US.json` and rename the copy for your language, for example
   `AvatarDoctor.ko-KR.json`.
2. Put the copy anywhere under `Assets` to try it in your own project, or in `Editor/Localization` to ship it with
   AvatarDoc.
3. Translate the text after each colon. Never change a key.
4. Save. Unity reads the file at once: the language appears in AvatarDoc's language button and in NDMF's and Modular
   Avatar's language lists. If something in the file needs fixing, the Console says what and where.

That is all. No code changes.

A file of your own outside `Editor/Localization` may leave keys out: English shows for those. A file in
`Editor/Localization` ships with every copy of AvatarDoc, so it must have every key (see "Shipped languages are
complete" below).

To try a better translation of a shipped language, save your own file with the same name anywhere under `Assets`
(for example your own `AvatarDoctor.ja-JP.json`). Its texts win over the shipped ones, key by key, and any key you
leave out still comes from the shipped file.

## Update a language after English changed

New keys are added to English as AvatarDoc grows. Until you translate them, they show in English.

1. Run the check (see "Run the check" below). It names every key your file is missing.
2. For each one, copy the line from `AvatarDoctor.en-US.json` into your file at the same place (the keys keep the
   English order) and translate it.
3. A key that English no longer has is reported as "not in English": delete it from your file.
4. When an English text was only reworded, its key stays the same, so the check cannot see it. Look at the changes to
   `AvatarDoctor.en-US.json` in the release notes or the repository history, and compare your translation with each
   changed line.

## Rules for the texts

### Placeholders

`{0}`, `{1}` and so on are filled in by AvatarDoc with a name or a number. Keep every placeholder the English text
has, and add none. You may move them within the sentence to suit your language's word order, and use one more than
once.

```
"ad.info.path_created": "AvatarDoc created the submenu \"{0}\" for the install path of {1}."
"ad.info.path_created": "{1} のインストール先として、サブメニュー「{0}」を作成しました。"
```

A brace that is not part of a placeholder stops AvatarDoc from filling the text in. To show a brace in a text that
has placeholders, write it twice: `{{` for `{` and `}}` for `}`.

### Singular and plural: keys ending in `.one`

Some texts come in pairs: the key ending in `.one` is used for exactly one, the other for every other number.

```
"ad.ui.memory.avatar.one": "Avatar {0} bit",
"ad.ui.memory.avatar": "Avatar {0} bits",
```

If your language has no plural forms (Japanese, Chinese and Korean, for example), write the same text in both.

### Punctuation between pieces of text

AvatarDoc puts some texts together from pieces: a list of names, a note after a name, a name in quotes. These keys
hold only the punctuation, so write your language's own:

| Key | English | Japanese | Simplified Chinese |
|---|---|---|---|
| `ad.ui.list.separator` | `, ` | `、` | `、` |
| `ad.ui.aside` | `{0} ({1})` | `{0}（{1}）` | `{0}（{1}）` |
| `ad.ui.brackets` | `({0})` | `（{0}）` | `（{0}）` |
| `ad.ui.quoted` | `"{0}"` | `「{0}」` | `“{0}”` |
| `ad.ui.labelled` | `{0}: {1}` | `{0}：{1}` | `{0}：{1}` |

### Rich text tags and line breaks

- If an English text uses a tag such as `<b>`, `<i>` or `<color=...>`, keep the tag and its closing pair.
- `\n` is a line break. Keep it where the English has one.
- The file is JSON, so a double quote inside a text is written `\"` and a backslash `\\`. Every text sits on one line.

### Quotes around names

English puts names in double quotes: `\"{0}\"`. Use your own language's quotes instead: 「{0}」 in Japanese,
“{0}” in Chinese. These need no backslash.

### What stays in English

Product names and technical names stay as they are: AvatarDoc, VRChat, Unity, NDMF, Modular Avatar, VRCFury,
PhysBone, Contact, FX, Write Defaults and AudioLink, and the names of layers, parameters and assets (the creator's own
names). `glossary.md` beside this guide has the full list, and the agreed word in each language for every other
VRChat, Unity and AvatarDoc term. Use those words, so one thing has one name everywhere.

### Length and tone

- Labels sit in a 112 px column of an inspector that is 300 to 400 px wide. Keep labels and buttons short.
- Tooltips and messages may be full sentences. A message says what happened, then what to do, as the English does.
- Use your language's own punctuation. No em dashes, and no dash joining two sentences.

## Run the check

There are two ways.

- **Save the file in Unity.** When it needs a fix, the Console shows one warning for the file. Click it to read every
  problem: each names the key and says what to do.
- **Run every check.** The tests come with a copy of the AvatarDoc repository (github.com/DocMeVR/AvatarDoc) placed in
  the project's `Packages` folder, not with the VCC download. In Unity, open `Window > General > Test Runner`, choose
  the `EditMode` tab, select `LocalizationFileTests` and press `Run Selected`. Each failure names the language, the
  key and what to fix.

The check finds:

- a file that is not valid JSON or not saved as UTF-8 (it names the line)
- a key written twice
- a key that English does not have
- a placeholder that was added, dropped or changed
- a brace that is not part of a placeholder
- a `<b>`, `<i>` or `<color=...>` tag that is not closed
- an empty text
- keys that are not in the English order
- for a language in `Editor/Localization`: any English key that is missing

## Shipped languages are complete

A language file in `Editor/Localization` ships with every copy of AvatarDoc. It must have every English key, in the
English order, with no empty text, and the tests enforce it: AvatarDoc is not released while a shipped language has a
gap. Whoever adds English keys also arranges their translation into every shipped language before the release.
