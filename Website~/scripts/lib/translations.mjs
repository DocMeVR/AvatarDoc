import crypto from 'node:crypto';
import { stripBom } from './strings.mjs';

// A translated page lives under src/content/docs/<locale>/ with the same path as its English page, and records in its
// front matter the sourceHash of the English text it was translated from: sha256 of the file's text with LF line ends.

const DOCS = 'src/content/docs/';
const TRANSLATED_FOLDER = 'start-here/';
const FRONT_MATTER = /^---\r?\n([\s\S]*?)\r?\n---(?:\r?\n|$)/;
const HASH_LINE = /^sourceHash:[ \t]*(?:"([^"\r\n]*)"|'([^'\r\n]*)'|([^\s#]*))[ \t]*(?=\r?$)/m;

export function sourceHash(text) {
  return crypto.createHash('sha256').update(stripBom(text).replace(/\r\n?/g, '\n'), 'utf8').digest('hex');
}

export function readSourceHash(source) {
  const fm = FRONT_MATTER.exec(stripBom(source));
  if (!fm) return undefined;
  const m = HASH_LINE.exec(fm[1]);
  return m ? (m[1] ?? m[2] ?? m[3]) : undefined;
}

export function writeSourceHash(source, hash) {
  const text = stripBom(source);
  const fm = FRONT_MATTER.exec(text);
  if (!fm) throw new Error('The page has no front matter between --- lines.');
  const eol = fm[0].includes('\r\n') ? '\r\n' : '\n';
  const line = `sourceHash: "${hash}"`;
  const body = HASH_LINE.test(fm[1]) ? fm[1].replace(HASH_LINE, line) : fm[1] + eol + line;
  return `---${eol}${body}${eol}---${eol}` + text.slice(fm[0].length);
}

// docsPath is a page path inside src/content/docs, such as "ja/start-here/install.mdx".
export function translationOf(docsPath, localeKeys) {
  const [first, ...rest] = docsPath.split('/');
  if (rest.length === 0 || first === 'root' || !localeKeys.includes(first)) return undefined;
  return { locale: first, english: rest.join('/') };
}

export function pageUrl(docsPath) {
  const slug = docsPath.replace(/\.mdx?$/, '');
  if (slug === 'index') return '';
  return slug.replace(/\/index$/, '') + '/';
}

// pages: every page of the site as { file, source }, file relative to the site folder ("src/content/docs/...").
export function checkTranslations(pages, localeKeys) {
  const bySiteFile = new Map(pages.map((p) => [p.file, p.source]));
  const problems = [];
  const translations = [];
  for (const { file, source } of pages) {
    if (!file.startsWith(DOCS)) continue;
    const of = translationOf(file.slice(DOCS.length), localeKeys);
    if (!of) continue;
    const englishFile = DOCS + of.english;
    if (!bySiteFile.has(englishFile)) {
      problems.push(`${file} translates ${englishFile}, which does not exist. Rename the translation to match an English page, or remove it.`);
      continue;
    }
    const recorded = readSourceHash(source);
    if (recorded === undefined) {
      problems.push(`${file} has no sourceHash. Check it against ${englishFile}, then run: npm run source-hash -- ${file}`);
      continue;
    }
    if (!/^[0-9a-f]{64}$/.test(recorded)) {
      problems.push(`${file} has a sourceHash that is not 64 lowercase hex characters. Run: npm run source-hash -- ${file}`);
      continue;
    }
    translations.push({
      file,
      locale: of.locale,
      english: englishFile,
      englishUrl: pageUrl(of.english),
      stale: recorded !== sourceHash(bySiteFile.get(englishFile)),
    });
  }
  const untranslated = [];
  for (const { file } of pages) {
    if (!file.startsWith(DOCS + TRANSLATED_FOLDER)) continue;
    for (const locale of localeKeys) {
      if (locale === 'root') continue;
      const wanted = DOCS + locale + '/' + file.slice(DOCS.length);
      if (!bySiteFile.has(wanted)) untranslated.push({ english: file, locale, file: wanted });
    }
  }
  return { problems, translations, untranslated };
}

export function linkParts(text) {
  const parts = [];
  let at = 0;
  for (const m of text.matchAll(/\[([^\]]+)\]/g)) {
    if (m.index > at) parts.push({ text: text.slice(at, m.index) });
    parts.push({ text: m[1], link: true });
    at = m.index + m[0].length;
  }
  if (at < text.length) parts.push({ text: text.slice(at) });
  return parts;
}
