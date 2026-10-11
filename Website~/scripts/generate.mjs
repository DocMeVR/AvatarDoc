import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { loadStrings } from './lib/strings.mjs';
import { messageEntries, renderMessages } from './lib/messages.mjs';
import { listFiles } from './lib/checks.mjs';
import { prescanPages } from './lib/prescan.mjs';
import { changelogPage, glossaryPage, translatePage, messagesPage, parseNotes } from './lib/pages.mjs';

const site = fileURLToPath(new URL('..', import.meta.url));
const pkg = path.resolve(site, '..');
const table = path.join(pkg, 'Editor', 'Localization', 'AvatarDoctor.en-US.json');
const docs = path.join(site, 'src', 'content', 'docs');

function write(file, text) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, text.endsWith('\n') ? text : text + '\n', 'utf8');
}

function read(file) {
  return fs.readFileSync(file, 'utf8');
}

const strings = loadStrings(table);
write(path.join(site, 'src', 'data', 'strings.en-US.json'), JSON.stringify(strings, null, 2));

const notesFile = path.join(site, 'src', 'data', 'message-notes.yml');
const notes = fs.existsSync(notesFile) ? parseNotes(read(notesFile)) : {};
const unknownNotes = Object.keys(notes).filter((key) => !(key in strings));
if (unknownNotes.length > 0) {
  throw new Error(`src/data/message-notes.yml has notes for messages that no longer exist: ${unknownNotes.join(', ')}.`);
}
write(
  path.join(docs, 'reference', 'messages.md'),
  messagesPage(renderMessages(messageEntries(strings), notes), 'Editor/Localization/AvatarDoctor.en-US.json'),
);

write(path.join(docs, 'help', 'changelog.md'), changelogPage(read(path.join(pkg, 'CHANGELOG.md')), 'CHANGELOG.md'));
write(path.join(docs, 'how-it-works', 'glossary.md'), glossaryPage(read(path.join(pkg, 'Docs~', 'glossary.md')), 'Docs~/glossary.md'));
write(path.join(docs, 'help', 'translate.md'), translatePage(read(path.join(pkg, 'Docs~', 'translating.md')), 'Docs~/translating.md'));

const pages = listFiles(docs, ['.md', '.mdx']).map((file) => ({
  file: path.relative(site, file).split(path.sep).join('/'),
  source: read(file),
}));
const shotNames = new Set(
  listFiles(path.join(site, 'src', 'assets', 'shots'), ['.png']).map((file) =>
    path.relative(path.join(site, 'src', 'assets', 'shots'), file).split(path.sep).join('/').replace(/\.png$/, ''),
  ),
);
const problems = prescanPages(pages, strings, { shotNames, allowMissing: process.env.DOCS_ALLOW_MISSING_SHOTS === '1' });
if (problems.length > 0) {
  throw new Error('Page problems found before the build:\n  ' + problems.join('\n  '));
}

console.log('Generated strings, Messages, Changelog, Glossary and Help translate.');
