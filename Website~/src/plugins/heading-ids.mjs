import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
import { loadStrings } from '../../scripts/lib/strings.mjs';

const MARKER = /\s*\[#([A-Za-z][\w-]*)(?:\s+k=([A-Za-z0-9_.-]+))?\]\s*$/;

let cached;
function defaultStrings(stringsPath) {
  const file = stringsPath ?? fileURLToPath(new URL('../data/strings.en-US.json', import.meta.url));
  const mtime = fs.statSync(file).mtimeMs;
  if (!cached || cached.file !== file || cached.mtime !== mtime) cached = { file, mtime, strings: loadStrings(file) };
  return cached.strings;
}

function textOf(node) {
  if (typeof node.value === 'string') return node.value;
  return (node.children || []).map(textOf).join('');
}

function visit(node, fn) {
  if (node.type === 'heading') fn(node);
  for (const child of node.children || []) visit(child, fn);
}

export function headingIds(options = {}) {
  return function transformer(tree, file) {
    const where = (file && (file.path || file.history?.[0])) || 'a page';
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
        if (text !== strings[m[2]]) {
          throw new Error(
            `Heading "${text}" in ${where} no longer matches ${m[2]} ("${strings[m[2]]}"): update the heading`,
          );
        }
      }
      heading.data = { ...heading.data, hProperties: { ...heading.data?.hProperties, id: m[1] } };
    });
  };
}
