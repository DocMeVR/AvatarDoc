import test from 'node:test';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { checkTranslations, linkParts, pageUrl, readSourceHash, sourceHash, translationOf, writeSourceHash } from '../lib/translations.mjs';
import { listFiles } from '../lib/checks.mjs';

const sha = (text) => crypto.createHash('sha256').update(text, 'utf8').digest('hex');
const locales = ['root', 'ja', 'ar'];

test('sourceHash is the sha256 of the text with LF line ends and no byte order mark', () => {
  assert.equal(sourceHash('a\nb\n'), sha('a\nb\n'));
  assert.equal(sourceHash('a\r\nb\r\n'), sha('a\nb\n'));
  assert.equal(sourceHash(String.fromCharCode(0xfeff) + 'a\nb\n'), sha('a\nb\n'));
  assert.notEqual(sourceHash('a\nb\n'), sourceHash('a\nb'));
});

test('readSourceHash reads the front matter line, quoted or not', () => {
  const hash = 'a'.repeat(64);
  assert.equal(readSourceHash(`---\ntitle: X\nsourceHash: "${hash}"\n---\nBody`), hash);
  assert.equal(readSourceHash(`---\nsourceHash: ${hash}\ntitle: X\n---\n`), hash);
  assert.equal(readSourceHash(`---\r\ntitle: X\r\nsourceHash: '${hash}'\r\n---\r\n`), hash);
  assert.equal(readSourceHash('---\ntitle: X\n---\nsourceHash: "abc"\n'), undefined, 'only the front matter counts');
  assert.equal(readSourceHash('No front matter.'), undefined);
});

test('writeSourceHash replaces the line, or adds it at the end of the front matter, and keeps the body', () => {
  const old = 'b'.repeat(64);
  const hash = 'c'.repeat(64);
  const body = '\nimport X from "y";\n\n## Heading [#id]\n\n---\n\nText.\n';
  const replaced = writeSourceHash(`---\ntitle: X\nsourceHash: "${old}"\nsidebar:\n  order: 2\n---\n${body}`, hash);
  assert.equal(replaced, `---\ntitle: X\nsourceHash: "${hash}"\nsidebar:\n  order: 2\n---\n${body}`);
  const added = writeSourceHash(`---\ntitle: X\n---\n${body}`, hash);
  assert.equal(added, `---\ntitle: X\nsourceHash: "${hash}"\n---\n${body}`);
  assert.equal(writeSourceHash(added, hash), added, 'writing the same hash again changes nothing');
  assert.throws(() => writeSourceHash('No front matter.', hash), /front matter/);
});

test('translationOf finds the language folder and the English page', () => {
  assert.deepEqual(translationOf('ja/start-here/install.mdx', locales), { locale: 'ja', english: 'start-here/install.mdx' });
  assert.deepEqual(translationOf('ar/index.mdx', locales), { locale: 'ar', english: 'index.mdx' });
  assert.equal(translationOf('guides/convert.mdx', locales), undefined);
  assert.equal(translationOf('index.mdx', locales), undefined);
  assert.equal(translationOf('root/index.mdx', locales), undefined);
});

test('pageUrl turns a page path into its address under the site base', () => {
  assert.equal(pageUrl('index.mdx'), '');
  assert.equal(pageUrl('start-here/install.mdx'), 'start-here/install/');
  assert.equal(pageUrl('help/changelog.md'), 'help/changelog/');
  assert.equal(pageUrl('guides/index.mdx'), 'guides/');
});

const english = '---\ntitle: Install\n---\n\nSteps.\n';
const translated = (hash) => `---\ntitle: インストール\nsourceHash: "${hash}"\n---\n\n手順。\n`;

test('a translation of the current English text is up to date, one of an older text is listed as behind', () => {
  const { problems, translations } = checkTranslations(
    [
      { file: 'src/content/docs/start-here/install.mdx', source: english },
      { file: 'src/content/docs/ja/start-here/install.mdx', source: translated(sourceHash(english)) },
      { file: 'src/content/docs/ar/start-here/install.mdx', source: translated(sourceHash('---\ntitle: Install\n---\n\nOld steps.\n')) },
    ],
    locales,
  );
  assert.deepEqual(problems, []);
  assert.deepEqual(
    translations.map((t) => [t.file, t.locale, t.english, t.englishUrl, t.stale]),
    [
      ['src/content/docs/ja/start-here/install.mdx', 'ja', 'src/content/docs/start-here/install.mdx', 'start-here/install/', false],
      ['src/content/docs/ar/start-here/install.mdx', 'ar', 'src/content/docs/start-here/install.mdx', 'start-here/install/', true],
    ],
  );
});

test('the English page changing only its line ends keeps the translation up to date', () => {
  const { translations } = checkTranslations(
    [
      { file: 'src/content/docs/start-here/install.mdx', source: english.replace(/\n/g, '\r\n') },
      { file: 'src/content/docs/ja/start-here/install.mdx', source: translated(sourceHash(english)) },
    ],
    locales,
  );
  assert.equal(translations[0].stale, false);
});

test('a translation without a hash, with a bad hash or without an English page fails and says what to do', () => {
  const { problems, translations } = checkTranslations(
    [
      { file: 'src/content/docs/start-here/install.mdx', source: english },
      { file: 'src/content/docs/ja/start-here/install.mdx', source: '---\ntitle: X\n---\n' },
      { file: 'src/content/docs/ar/start-here/install.mdx', source: translated('ABC') },
      { file: 'src/content/docs/ja/start-here/gone.mdx', source: translated(sourceHash(english)) },
      { file: 'src/content/docs/guides/convert.mdx', source: '---\ntitle: Convert\n---\n' },
    ],
    locales,
  );
  assert.equal(translations.length, 0);
  assert.equal(problems.length, 3);
  assert.match(problems[0], /^src\/content\/docs\/ja\/start-here\/install\.mdx has no sourceHash\..*npm run source-hash -- src\/content\/docs\/ja\/start-here\/install\.mdx$/);
  assert.match(problems[1], /^src\/content\/docs\/ar\/start-here\/install\.mdx has a sourceHash that is not 64 lowercase hex/);
  assert.match(problems[2], /^src\/content\/docs\/ja\/start-here\/gone\.mdx translates src\/content\/docs\/start-here\/gone\.mdx, which does not exist\./);
});

test('linkParts marks the bracketed words as the link', () => {
  assert.deepEqual(linkParts('The [English page] is the reference.'), [
    { text: 'The ' },
    { text: 'English page', link: true },
    { text: ' is the reference.' },
  ]);
  assert.deepEqual(linkParts('[英語のページ]です。'), [{ text: '英語のページ', link: true }, { text: 'です。' }]);
  assert.deepEqual(linkParts('No link.'), [{ text: 'No link.' }]);
});

test('every translated page of the site records a hash and has an English page', () => {
  const site = fileURLToPath(new URL('../..', import.meta.url));
  const docs = path.join(site, 'src', 'content', 'docs');
  const siteLocales = Object.keys(JSON.parse(fs.readFileSync(path.join(site, 'src', 'data', 'locales.json'), 'utf8')));
  const pages = listFiles(docs, ['.md', '.mdx']).map((file) => ({
    file: path.relative(site, file).split(path.sep).join('/'),
    source: fs.readFileSync(file, 'utf8'),
  }));
  const { problems, translations } = checkTranslations(pages, siteLocales);
  assert.deepEqual(problems, []);
  assert.ok(translations.length > 0, 'the site has translated pages');
});

test('an English Start here page without a translation in a language is listed, other pages are not', () => {
  const { problems, untranslated } = checkTranslations(
    [
      { file: 'src/content/docs/start-here/install.mdx', source: english },
      { file: 'src/content/docs/start-here/new-page.mdx', source: english },
      { file: 'src/content/docs/guides/convert.mdx', source: english },
      { file: 'src/content/docs/ja/start-here/install.mdx', source: translated(sourceHash(english)) },
      { file: 'src/content/docs/ar/start-here/install.mdx', source: translated(sourceHash(english)) },
    ],
    locales,
  );
  assert.deepEqual(problems, []);
  assert.deepEqual(untranslated, [
    { english: 'src/content/docs/start-here/new-page.mdx', locale: 'ja', file: 'src/content/docs/ja/start-here/new-page.mdx' },
    { english: 'src/content/docs/start-here/new-page.mdx', locale: 'ar', file: 'src/content/docs/ar/start-here/new-page.mdx' },
  ]);
});
