import fs from 'node:fs';

const BOM = String.fromCharCode(0xfeff);

export function stripBom(text) {
  return text.startsWith(BOM) ? text.slice(1) : text;
}

export function stripRichText(text) {
  return text.replace(/<\/?(?:b|i|u|color|size|sprite|material|quad|mark|noparse)(?:=[^>]*)?>/gi, '');
}

export function loadStrings(tablePath) {
  return JSON.parse(stripBom(fs.readFileSync(tablePath, 'utf8')));
}

export function labelText(strings, key) {
  if (!Object.prototype.hasOwnProperty.call(strings, key)) {
    throw new Error(
      `Unknown UI label key "${key}". It is not in AvatarDoctor.en-US.po: fix the key, or remove the label from the page.`,
    );
  }
  const text = stripRichText(strings[key]);
  if (/\{\d+\}/.test(text)) {
    throw new Error(`Label "${key}" has a placeholder; write the sentence in the page instead.`);
  }
  return text;
}

// How <Label> shows a key on a page in `lang`. English pages: the English text. A page whose language has a table with
// a different text: that text, then the English one, because the pictures show English. Any other page: the English
// text, kept left to right inside the page's own text. An unknown key fails in every language.
export function labelParts(english, local, key, lang = 'en') {
  const englishText = labelText(english, key);
  if (lang.split('-')[0] === 'en') return { english: englishText };
  if (local && Object.prototype.hasOwnProperty.call(local, key)) {
    const text = labelText(local, key);
    if (text !== englishText) return { local: text, english: englishText };
  }
  return { english: englishText, isolate: true };
}

export function usedLabelTags(source) {
  return [...source.matchAll(/<Label\s+k="([^"]+)"/g)].map((m) => m[1]);
}

export function usedHeadingKeys(source) {
  return [...source.matchAll(/\[#[A-Za-z][\w-]*\s+k=([A-Za-z0-9_.-]+)\]/g)].map((m) => m[1]);
}

export function usedLabels(source) {
  return [...usedLabelTags(source), ...usedHeadingKeys(source)];
}
