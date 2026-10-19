import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import siteData from '../src/data/site.json' with { type: 'json' };
import locales from '../src/data/locales.json' with { type: 'json' };
import { loadPoStrings } from './lib/po.mjs';
import { messageEntries, renderMessages } from './lib/messages.mjs';
import { listFiles } from './lib/checks.mjs';
import { prescanPages } from './lib/prescan.mjs';
import { changelogPage, glossaryPage, translatePage, messagesPage, parseNotes } from './lib/pages.mjs';
import { checkTranslations } from './lib/translations.mjs';

const site = fileURLToPath(new URL('..', import.meta.url));
const pkg = path.resolve(site, '..');
const localization = path.join(pkg, 'Editor', 'Localization');
const table = path.join(localization, 'AvatarDoctor.en-US.po');
const docs = path.join(site, 'src', 'content', 'docs');

function write(file, text) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, text.endsWith('\n') ? text : text + '\n', 'utf8');
}

function read(file) {
  return fs.readFileSync(file, 'utf8');
}

const edit = (file) => `${siteData.githubRepo}/edit/main/${file}`;
const strings = loadPoStrings(table);
write(path.join(site, 'src', 'data', 'strings.en-US.json'), JSON.stringify(strings, null, 2));
for (const [key, { table: name }] of Object.entries(locales)) {
  if (key === 'root' || !name) continue;
  const local = loadPoStrings(path.join(localization, `AvatarDoctor.${name}.po`));
  write(path.join(site, 'src', 'data', `strings.${name}.json`), JSON.stringify(local, null, 2));
}

const notesFile = path.join(site, 'src', 'data', 'message-notes.yml');
const notes = fs.existsSync(notesFile) ? parseNotes(read(notesFile)) : {};
const unknownNotes = Object.keys(notes).filter((key) => !(key in strings));
if (unknownNotes.length > 0) {
  throw new Error(`src/data/message-notes.yml has notes for messages that no longer exist: ${unknownNotes.join(', ')}.`);
}
write(
  path.join(docs, 'reference', 'messages.md'),
  messagesPage(renderMessages(messageEntries(strings), notes), 'Editor/Localization/AvatarDoctor.en-US.po', edit('Website~/src/data/message-notes.yml'), strings['ad.ui.message.learn_more']),
);

write(path.join(docs, 'help', 'changelog.md'), changelogPage(read(path.join(pkg, 'CHANGELOG.md')), 'CHANGELOG.md', edit('CHANGELOG.md')));
write(path.join(docs, 'how-it-works', 'glossary.md'), glossaryPage(read(path.join(pkg, 'Docs~', 'glossary.md')), 'Docs~/glossary.md', edit('Docs~/glossary.md')));
write(path.join(docs, 'help', 'translate.md'), translatePage(read(path.join(pkg, 'Docs~', 'translating.md')), 'Docs~/translating.md', edit('Docs~/translating.md')));

const pages = listFiles(docs, ['.md', '.mdx']).map((file) => ({
  file: path.relative(site, file).split(path.sep).join('/'),
  source: read(file),
}));
const shotNames = new Set(
  listFiles(path.join(site, 'src', 'assets', 'shots'), ['.png']).map((file) =>
    path.relative(path.join(site, 'src', 'assets', 'shots'), file).split(path.sep).join('/').replace(/\.png$/, ''),
  ),
);
const translated = checkTranslations(pages, Object.keys(locales));
const problems = [...prescanPages(pages, strings, { shotNames, allowMissing: process.env.DOCS_ALLOW_MISSING_SHOTS === '1' }), ...translated.problems];
if (problems.length > 0) {
  throw new Error('Page problems found before the build:\n  ' + problems.join('\n  '));
}

write(
  path.join(site, 'src', 'data', 'translation-status.json'),
  JSON.stringify(Object.fromEntries(translated.translations.map(({ file, ...rest }) => [file, rest])), null, 2),
);

console.log('Generated strings, Messages, Changelog, Glossary, Help translate and the translation status.');
