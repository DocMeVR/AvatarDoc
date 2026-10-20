import fs from 'node:fs';
import path from 'node:path';
import { stripBom } from './strings.mjs';

// Reads the tool's gettext .po language files the way its PoFile does: msgid is the key, msgstr the text, a text may
// go on over several quoted lines, and only the escapes \\, \", \n, \t and \r are read. poStrings leaves out what the
// tool does not show: old entries (#~), outdated texts (#, fuzzy) and empty texts.

const escapes = '\\"ntr';

function unescape(text) {
  return text.replace(/\\(.)/g, (_, c) => (c === 'n' ? '\n' : c === 't' ? '\t' : c === 'r' ? '\r' : c));
}

export function parsePo(source, name = 'the .po file') {
  const entries = [];
  let entry = null;
  let open = null;
  let fuzzy = false;
  const fail = (line, why) => {
    throw new Error(`${name}, line ${line}: ${why}`);
  };
  const quoted = (part, line) => {
    const m = /^"(.*)"$/.exec(part.trim());
    if (!m) fail(line, 'a text must stand in double quotes.');
    const inner = m[1];
    for (let i = 0; i < inner.length; i++) {
      if (inner[i] === '"') fail(line, 'a double quote inside a text must be written \\", and each line holds one text.');
      if (inner[i] !== '\\') continue;
      if (i + 1 >= inner.length) fail(line, 'the text ends in a lone backslash. Write \\\\ for a backslash.');
      i++;
      if (!escapes.includes(inner[i])) fail(line, `\\${inner[i]} is not an escape the tool reads.`);
    }
    return unescape(inner);
  };
  const finish = () => {
    if (!entry) return;
    if (entry.text === undefined) fail(entry.line, `the key "${entry.key}" has no msgstr line with its text.`);
    entries.push(entry);
    entry = null;
    open = null;
  };
  stripBom(source)
    .replace(/\r\n?/g, '\n')
    .split('\n')
    .forEach((raw, index) => {
      const n = index + 1;
      let line = raw.trim();
      if (!line) {
        if (entry && entry.text !== undefined) finish();
        return;
      }
      let obsolete = false;
      if (line.startsWith('#~')) {
        obsolete = true;
        line = line.slice(2).trim();
        if (!line) return;
      } else if (line.startsWith('#')) {
        finish();
        if (line.startsWith('#,') && line.slice(2).split(',').some((flag) => flag.trim() === 'fuzzy')) fuzzy = true;
        return;
      }
      if (line.startsWith('msgid ')) {
        finish();
        entry = { key: quoted(line.slice(6), n), line: n, obsolete, fuzzy };
        fuzzy = false;
        open = 'key';
      } else if (line.startsWith('msgstr ') && entry && entry.text === undefined) {
        entry.text = quoted(line.slice(7), n);
        open = 'text';
      } else if (line.startsWith('"') && open) {
        entry[open] += quoted(line, n);
      } else {
        fail(n, `this line is not understood: ${line}`);
      }
    });
  finish();
  return entries;
}

export function poStrings(source, name) {
  const strings = {};
  const seen = new Set();
  for (const { key, text, obsolete, fuzzy } of parsePo(source, name)) {
    if (obsolete || key === '' || seen.has(key)) continue;
    seen.add(key);
    if (fuzzy || !text.trim()) continue;
    strings[key] = text;
  }
  return strings;
}

export function loadPoStrings(file) {
  return poStrings(fs.readFileSync(file, 'utf8'), path.basename(file));
}
