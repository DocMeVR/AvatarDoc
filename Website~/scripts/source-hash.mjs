import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { stripBom } from './lib/strings.mjs';
import { readSourceHash, sourceHash, translationOf, writeSourceHash } from './lib/translations.mjs';

// Records that a translated page is up to date with its English page: run it after you bring the translation up to
// date. Usage: npm run source-hash -- src/content/docs/ja/start-here/install.mdx [more pages]

const site = fileURLToPath(new URL('..', import.meta.url));
const docs = path.join(site, 'src', 'content', 'docs');
const locales = JSON.parse(stripBom(fs.readFileSync(path.join(site, 'src', 'data', 'locales.json'), 'utf8')));

function resolvePage(arg) {
  for (const candidate of [path.resolve(arg), path.resolve(site, arg), path.resolve(docs, arg)]) {
    if (fs.existsSync(candidate) && fs.statSync(candidate).isFile()) return candidate;
  }
  return undefined;
}

const args = process.argv.slice(2);
if (args.length === 0) {
  console.error('Name the translated pages to mark as up to date, for example:\n  npm run source-hash -- src/content/docs/ja/start-here/install.mdx');
  process.exit(1);
}

let failed = false;
for (const arg of args) {
  const file = resolvePage(arg);
  const inDocs = file && path.relative(docs, file).split(path.sep).join('/');
  const of = inDocs && !inDocs.startsWith('..') ? translationOf(inDocs, Object.keys(locales)) : undefined;
  if (!file) {
    console.error(`${arg}: no such page.`);
    failed = true;
    continue;
  }
  if (!of) {
    console.error(`${arg}: not a translated page. Translated pages sit in a language folder under src/content/docs, such as ja/.`);
    failed = true;
    continue;
  }
  const englishFile = path.join(docs, ...of.english.split('/'));
  if (!fs.existsSync(englishFile)) {
    console.error(`${arg}: its English page src/content/docs/${of.english} does not exist.`);
    failed = true;
    continue;
  }
  const source = fs.readFileSync(file, 'utf8');
  const hash = sourceHash(fs.readFileSync(englishFile, 'utf8'));
  if (readSourceHash(source) === hash) {
    console.log(`${inDocs}: already up to date with ${of.english}.`);
    continue;
  }
  fs.writeFileSync(file, writeSourceHash(source, hash), 'utf8');
  console.log(`${inDocs}: sourceHash now matches ${of.english}.`);
}
process.exit(failed ? 1 : 0);
