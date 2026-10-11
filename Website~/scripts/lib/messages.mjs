import { stripRichText } from './strings.mjs';

const FAMILIES = [
  { title: 'Errors', prefix: 'ad.error.' },
  { title: 'Warnings', prefix: 'ad.warn.' },
  { title: 'Notes', prefix: 'ad.info.' },
];

const HEADING_LIMIT = 80;

export function anchor(key) {
  return key.toLowerCase().replace(/[._]/g, '-');
}

function parentOf(key, known) {
  let k = key;
  while (k.includes('.')) {
    k = k.slice(0, k.lastIndexOf('.'));
    if (known.has(k)) return k;
  }
  return null;
}

function rootOf(key, known) {
  let k = key;
  for (let p = parentOf(k, known); p; p = parentOf(k, known)) k = p;
  return k;
}

export function messageEntries(strings) {
  return FAMILIES.map(({ title, prefix }) => {
    const keys = Object.keys(strings)
      .filter((key) => key.startsWith(prefix) && !key.endsWith('.one'))
      .sort();
    const known = new Set(keys);
    const entries = keys.filter((key) => !parentOf(key, known)).map((key) => ({ key, text: strings[key], variants: [] }));
    const byKey = new Map(entries.map((e) => [e.key, e]));
    for (const key of keys) {
      if (parentOf(key, known)) byKey.get(rootOf(key, known)).variants.push({ key, text: strings[key] });
    }
    return { title, entries };
  });
}

export function plainMessage(text) {
  return stripRichText(text).replace(/\{\d+\}/g, '…');
}

function escapeLine(text) {
  return text
    .replace(/\\/g, '\\\\')
    .replace(/&/g, '&amp;')
    .replace(/[*`|[\]_~]/g, (c) => '\\' + c)
    .replace(/</g, '&lt;')
    .replace(/\{/g, '&#123;')
    .replace(/^(\s*)([-+>#])/, '$1\\$2')
    .replace(/^(\s*\d+)([.)])/, '$1\\$2');
}

function firstSentence(plain) {
  const m = /[.!?](?=\s|$)/.exec(plain);
  return m ? plain.slice(0, m.index + 1) : plain;
}

function readableTail(key) {
  const tail = key.split('.').pop().replace(/_/g, ' ');
  return tail.charAt(0).toUpperCase() + tail.slice(1);
}

function headingText(key, plain) {
  const sentence = firstSentence(plain.replace(/\s+/g, ' ').trim());
  if (/^["']?…["']?(\s|$)/.test(sentence)) return escapeLine(readableTail(key));
  if (escapeLine(sentence).length <= HEADING_LIMIT) return escapeLine(sentence);
  const words = sentence.split(' ');
  while (words.length > 1) {
    words.pop();
    const candidate = escapeLine(words.join(' ').replace(/[,;:.]$/, '')) + '...';
    if (candidate.length <= HEADING_LIMIT) return candidate;
  }
  return escapeLine(readableTail(key));
}

function quote(plain) {
  return plain.split(/\r?\n/).map((line) => `> ${escapeLine(line)}`);
}

export function renderMessages(groups, notes = {}) {
  const out = [];
  for (const group of groups) {
    if (group.entries.length === 0) continue;
    out.push(`## ${group.title}`, '');
    for (const { key, text, variants = [] } of group.entries) {
      const plain = plainMessage(text);
      out.push(`### ${headingText(key, plain)} [#${anchor(key)}]`, '');
      out.push(...quote(plain), '');
      if (variants.length > 0) {
        out.push('**Other forms**', '');
        for (const v of variants) out.push(`- <span id="${anchor(v.key)}"></span>${escapeLine(plainMessage(v.text))}`);
        out.push('');
      }
      if (notes[key]) out.push('**What to do**', '', notes[key], '');
    }
  }
  return out.join('\n');
}
