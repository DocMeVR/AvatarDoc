import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
import { loadStrings } from '../../scripts/lib/strings.mjs';

const MARKER = /\s*\[#([A-Za-z][\w-]*)(?:\s+k=([A-Za-z0-9_.-]+))?\]\s*$/;
const LOCALE_FOLDER = /[\\/]content[\\/]docs[\\/]([^\\/]+)[\\/]/;

const cache = new Map();
function loadCached(file) {
  const mtime = fs.statSync(file).mtimeMs;
  const hit = cache.get(file);
  if (hit && hit.mtime === mtime) return hit.strings;
  const strings = loadStrings(file);
  cache.set(file, { mtime, strings });
  return strings;
}

const dataFile = (name) => fileURLToPath(new URL(`../data/${name}`, import.meta.url));

function defaultStrings(stringsPath) {
  return loadCached(stringsPath ?? dataFile('strings.en-US.json'));
}

function localeOf(where, locales) {
  const m = LOCALE_FOLDER.exec(where);
  return m && m[1] !== 'root' && Object.prototype.hasOwnProperty.call(locales, m[1]) ? m[1] : undefined;
}

function localTable(locale, locales, options) {
  const table = locale && locales[locale].table;
  if (!table) return undefined;
  if (options.localeStrings && options.localeStrings[locale]) return options.localeStrings[locale];
  return loadCached(dataFile(`strings.${table}.json`));
}

function textOf(node) {
  if (typeof node.value === 'string') return node.value;
  return (node.children || []).map(textOf).join('');
}

function visit(node, fn) {
  if (node.type === 'heading') fn(node);
  for (const child of node.children || []) visit(child, fn);
}

// A heading marker on a translated page (src/content/docs/<locale>/...) is checked against that language's table when
// the language has one, and against English otherwise.
export function headingIds(options = {}) {
  const locales = options.locales ?? JSON.parse(fs.readFileSync(dataFile('locales.json'), 'utf8'));
  return function transformer(tree, file) {
    const where = (file && (file.path || file.history?.[0])) || 'a page';
    const locale = localeOf(where, locales);
    visit(tree, (heading) => {
      const last = heading.children[heading.children.length - 1];
      if (!last || last.type !== 'text') return;
      const m = MARKER.exec(last.value);
      if (!m) return;
      last.value = last.value.slice(0, m.index);
      const text = textOf(heading).trim();
      if (m[2]) {
        const strings = options.strings ?? defaultStrings(options.stringsPath);
        if (!Object.prototype.hasOwnProperty.call(strings, m[2])) {
          throw new Error(`Heading "${text}" in ${where} uses the unknown UI label key ${m[2]}: fix the key.`);
        }
        const local = localTable(locale, locales, options);
        if (local && Object.prototype.hasOwnProperty.call(local, m[2])) {
          if (text !== local[m[2]]) {
            throw new Error(
              `Heading "${text}" in ${where} no longer matches ${m[2]} in the ${locales[locale].table} table ("${local[m[2]]}"): update the heading`,
            );
          }
        } else if (text !== strings[m[2]]) {
          throw new Error(
            `Heading "${text}" in ${where} no longer matches ${m[2]} ("${strings[m[2]]}"): update the heading`,
          );
        }
      }
      heading.data = { ...heading.data, hProperties: { ...heading.data?.hProperties, id: m[1] } };
    });
  };
}
