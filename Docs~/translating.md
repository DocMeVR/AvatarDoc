# Translating AvatarDoc

Every word AvatarDoc shows comes from a language file in the package's `Editor/Localization` folder. This guide is for
anyone who wants to add a language or keep one up to date. You do not need to know how to program: a language is one
text file. The guide and the glossary (`Docs~/glossary.md`, shown as the Glossary page on the docs site) sit in `Docs~`, so
they stay out of the package creators install.

## How it works

- Every text has a key and the text itself. A language file writes them as a `msgid` line with the key and a `msgstr`
  line with the text:

  ```
  msgid "ad.ui.mode.simple"
  msgstr "Simple"
  ```

  The key tells AvatarDoc where the text goes; the text after `msgstr` is what people see.
- The files are gettext `.po` files, the format that translation tools such as Poedit, Weblate and Crowdin open. A
  plain text editor works too.
- `AvatarDoctor.en-US.po` is English. It is the default, and the reference every other language follows.
- Every other language is one file with the same keys: `AvatarDoctor.ja-JP.po` for Japanese,
  `AvatarDoctor.zh-Hans.po` for Simplified Chinese. Above each entry, a `#. en:` line holds the English the
  translation was made from, so the check can tell when English changed:

  ```
  #. en: Simple
  msgid "ad.ui.mode.simple"
  msgstr "シンプル"
  ```

- AvatarDoc uses the language chosen in NDMF, the same choice Modular Avatar uses. Change it with the language button
  at the top of the AvatarDoc inspector, or in Modular Avatar or NDMF: all of them follow.
- A key that a language does not have shows in English, and so does a key whose text is empty or marked outdated.
  Nothing breaks.
- When no file has the chosen language, AvatarDoc tries another form of the same language first (Traditional Chinese
  falls back to Simplified Chinese if only that exists), then English. NDMF does the same.

## File names

A language file is named `AvatarDoctor.<code>.po`, where `<code>` is the language code: `ja-JP`, `zh-Hans`,
`zh-Hant`, `ko-KR`, `fr-FR`. These are the codes NDMF and Modular Avatar use. Save the file as UTF-8, with or without
a byte order mark (BOM). Windows and Unix line ends both work.

The file starts with a header: an entry with an empty `msgid`, whose `Language` line names the language.

```
msgid ""
msgstr ""
"Language: ja-JP\n"
"Content-Type: text/plain; charset=UTF-8\n"
```

AvatarDoc finds a language file by its name alone, anywhere in the project. Any other name is ignored, and a file
saved with a code that names no language gets a Console warning saying so.

Earlier versions of AvatarDoc used `AvatarDoctor.<code>.json` files. AvatarDoc no longer reads them: a JSON language
file in the project gets one Console warning that says how to make the `.po` file from it.

## Add a language

1. Copy `Editor/Localization/AvatarDoctor.en-US.po` and rename the copy for your language, for example
   `AvatarDoctor.ko-KR.po`.
2. In the header at the top, change `Language: en-US` to your code, for example `Language: ko-KR`.
3. Put the copy anywhere under `Assets` to try it in your own project, or in `Editor/Localization` to ship it with
   AvatarDoc.
4. Replace the English after each `msgstr` with your translation. Never change a `msgid`.
5. Save. Unity reads the file at once: the language appears in AvatarDoc's language button and in NDMF's and Modular
   Avatar's language lists. If something in the file needs fixing, the Console says what and where.

That is all. No code changes.

The check can tell that an English text was reworded only for an entry with a `#. en:` line, and a copy of the
English file has none. To have them from the start, copy a shipped translation such as `AvatarDoctor.ja-JP.po` in
step 1 instead, change its `Language` line, and replace every text after `msgstr`: the English stays in the `#. en:`
line above each entry. In a copy of the AvatarDoc repository you can also put your file in `Editor/Localization` and
run every check once (see "Run the check" below): it adds a `#. en:` line to every entry that lacks one.

You can do step 4 in Poedit: open the file, and Poedit lists each key as the source text and the text after `msgstr`
as its translation, with the `#. en:` line as a note for translators. Turn off line wrapping in Poedit's preferences
(Wrap at, on the Advanced page). AvatarDoc reads wrapped lines, but when the check updates a shipped file it writes
each text on one line, so a wrapped file would show many changed lines. If Poedit also saves a `.mo` file beside it,
delete that file: AvatarDoc does not use it.

A file of your own outside `Editor/Localization` may leave keys out: English shows for those. A file in
`Editor/Localization` ships with every copy of AvatarDoc, so it must have every key (see "Shipped languages are
complete" below).

To try a better translation of a shipped language, save your own file with the same name anywhere under `Assets`
(for example your own `AvatarDoctor.ja-JP.po`). Its texts win over the shipped ones, key by key, and any key you
leave out still comes from the shipped file.

## Update a language after English changed

New keys are added to English as AvatarDoc grows, and English texts are reworded. Until a translation follows, the
English text shows in its place.

1. Run every check (see "Run the check" below). For a shipped language, the check first brings the file in step with
   English and saves it:
   - a new key gets its entry, with the English in a `#. en:` line and an empty `msgstr`, in the English order;
   - a translation whose English was reworded is marked `#, fuzzy`, with today's English in its `#. en:` line and the
     English it was made from in a `#. was:` line;
   - a key English no longer has moves to the end of the file as an old entry, with `#~` in front of its lines. Its
     translation is kept, never deleted.

   Then the check names every entry that needs you.
2. Translate each empty `msgstr`.
3. For each entry marked `#, fuzzy`, compare the translation with the new English and fix it. Then delete its
   `#, fuzzy` line and its `#. was:` line. Poedit shows these entries as needing work: fix them there, then mark them
   done. Poedit keeps the `#. was:` line; the next run of the check removes it.
4. Delete the old `#~` entries once you no longer need their text.

If English changed "Unlock" to "Unlock editing", the Japanese entry would look like this until step 3:

```
#. en: Unlock editing
#. was: Unlock
#, fuzzy
msgid "ad.ui.unlock"
msgstr "ロック解除"
```

For a file of your own outside `Editor/Localization`, saving it runs the check: it names every entry marked
`#, fuzzy` and every entry whose `#. en:` line differs from today's English. An entry without a `#. en:` line has
nothing to compare, so the check cannot tell when its English was reworded ("Add a language" says how to get the
lines). A new key shows in English until you copy its entry, with its `#. en:` line, from a shipped translation such
as `AvatarDoctor.ja-JP.po` into your file and translate it.

## Rules for the texts

### Placeholders

`{0}`, `{1}` and so on are filled in by AvatarDoc with a name or a number. Keep every placeholder the English text
has, and add none. You may move them within the sentence to suit your language's word order, and use one more than
once.

```
#. en: AvatarDoc created the submenu \"{0}\" for the install path of {1}.
msgid "ad.info.path_created"
msgstr "{1}のインストールパス用に、AvatarDocがサブメニュー「{0}」を作成しました。"
```

A brace that is not part of a placeholder stops AvatarDoc from filling the text in. To show a brace in a text that
has placeholders, write it twice: `{{` for `{` and `}}` for `}`.

### Singular and plural: keys ending in `.one`

Some texts come in pairs: the key ending in `.one` is used for exactly one, the other for every other number.

```
msgid "ad.ui.memory.avatar.one"
msgstr "Avatar {0} bit"

msgid "ad.ui.memory.avatar"
msgstr "Avatar {0} bits"
```

If your language has no plural forms (Japanese, Chinese and Korean, for example), write the same text in both.
AvatarDoc does not read the plural lines of gettext (`msgid_plural`, `msgstr[0]`): keep to the `.one` keys.

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

A space at the start or the end of a text counts, so keep it inside the quotes: `msgstr ", "`.

### Rich text tags and line breaks

- If an English text uses a tag such as `<b>`, `<i>` or `<color=...>`, keep the tag and its closing pair.
- `\n` is a line break. Keep it where the English has one.
- A text stands in double quotes, so a double quote inside a text is written `\"` and a backslash `\\`. A double
  quote without its backslash, or a backslash before any letter but `n`, `t` and `r`, makes the file unreadable, and
  the Console names the line.
- A long text may go on over several lines, each in its own double quotes. AvatarDoc joins them with nothing between,
  so put the space at the end of a line where your language needs one. Poedit writes long texts this way.

### Quotes around names

English puts names in double quotes: `\"{0}\"`. Use your own language's quotes instead: 「{0}」 in Japanese,
“{0}” in Chinese. These need no backslash.

### What stays in English

Product names and technical names stay as they are: AvatarDoc, VRChat, Unity, NDMF, Modular Avatar, VRCFury,
PhysBone, Contact, FX, Write Defaults and AudioLink, and the names of layers, parameters and assets (the creator's own
names). The glossary (`Docs~/glossary.md`, the Glossary page on the docs site) has the full list, and the agreed word in each language for every other
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
  key and what to fix. This run also brings the shipped languages in step with English, as "Update a language after
  English changed" describes: look at the changes to their files, and run it again once you have translated them. A
  file that is already in step is left exactly as it is, and a file AvatarDoc cannot read is never rewritten.

The check finds:

- a file that is not a valid `.po` file or not saved as UTF-8 (it names the line)
- a key written twice
- a key that English does not have, and an old `#~` entry
- a translation marked `#, fuzzy`, or one whose `#. en:` line differs from today's English
- a placeholder that was added, dropped or changed
- a brace that is not part of a placeholder
- a `<b>`, `<i>` or `<color=...>` tag that is not closed
- an empty text
- keys that are not in the English order
- for a language in `Editor/Localization`: any English key that is missing

## Shipped languages are complete

A language file in `Editor/Localization` ships with every copy of AvatarDoc. It must have every English key, in the
English order, with no empty text and none marked `#, fuzzy`, and the tests enforce it: AvatarDoc is not released
while a shipped language has a gap. Whoever adds or rewords English texts also arranges their translation into every
shipped language before the release.
