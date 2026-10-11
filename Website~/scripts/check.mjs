import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { loadStrings, usedLabels, stripBom } from './lib/strings.mjs';
import { stripCode } from './lib/code.mjs';
import { usedShots, checkShots, minorOf } from './lib/shots.mjs';
import { coverage, toolLinkProblems, oversizeShots, brokenLinks, listFiles } from './lib/checks.mjs';

const site = fileURLToPath(new URL('..', import.meta.url));
const pkg = path.resolve(site, '..');
const docs = path.join(site, 'src', 'content', 'docs');
const shotsDir = path.join(site, 'src', 'assets', 'shots');
const siteData = JSON.parse(stripBom(fs.readFileSync(path.join(site, 'src', 'data', 'site.json'), 'utf8')));
const allowMissing = process.env.DOCS_ALLOW_MISSING_SHOTS === '1';

function readJson(file, fallback) {
  if (!fs.existsSync(file)) return fallback;
  return JSON.parse(stripBom(fs.readFileSync(file, 'utf8')));
}

const pages = listFiles(docs, ['.md', '.mdx']).map((file) => ({
  file: path.relative(site, file).split(path.sep).join('/'),
  source: fs.readFileSync(file, 'utf8'),
}));
const errors = [];

const packageVersion = readJson(path.join(pkg, 'package.json'), {}).version;
const shots = checkShots(usedShots(pages.map((p) => ({ file: p.file, source: stripCode(p.source) }))), readJson(path.join(shotsDir, 'manifest.json'), { shots: {} }), {
  allowMissing,
  packageVersion,
});
errors.push(...shots.errors);
if (allowMissing && shots.missing.length > 0) {
  console.warn(`\nShots not captured yet (${shots.missing.length}), allowed by DOCS_ALLOW_MISSING_SHOTS=1:`);
  shots.missing.forEach((m) => console.warn('  ' + m));
}
if (shots.unused.length > 0) {
  console.warn('\nShots no page uses:');
  shots.unused.forEach((name) => console.warn('  ' + name));
}
if (shots.older.length > 0) {
  console.warn(`\nShots older than ${packageVersion ? minorOf(packageVersion) : 'the package'}:`);
  shots.older.forEach((name) => console.warn('  ' + name));
}

const strings = loadStrings(path.join(site, 'src', 'data', 'strings.en-US.json'));
const used = new Set(pages.flatMap((p) => usedLabels(stripCode(p.source))));
const uncovered = coverage(strings, used);
console.log(`\nUI labels no page mentions yet (${uncovered.length})` + (uncovered.length ? ':' : ''));
uncovered.forEach((key) => console.log(`  ${key}  ${strings[key]}`));

errors.push(...toolLinkProblems(path.join(site, 'dist'), readJson(path.join(site, 'src', 'data', 'tool-links.json'), { pages: [], anchors: [] })));
errors.push(...brokenLinks(path.join(site, 'dist'), siteData.base).map((p) => 'Broken link: ' + p));
const homePage = path.join(site, 'dist', 'index.html');
if (packageVersion && fs.existsSync(homePage) && !fs.readFileSync(homePage, 'utf8').includes(`Docs for AvatarDoc ${packageVersion}`)) {
  errors.push(`The built pages do not print "Docs for AvatarDoc ${packageVersion}". Check src/components/Footer.astro.`);
}
errors.push(...oversizeShots(shotsDir).map((s) => `Shot over 250 KB: ${s}. Re-capture it smaller.`));

if (errors.length > 0) {
  console.error('\nDocs checks failed:');
  errors.forEach((e) => console.error('  ' + e));
  process.exit(1);
}
console.log('\nDocs checks passed.');
