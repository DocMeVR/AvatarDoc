import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { loadStrings, labelText, usedLabels } from '../lib/strings.mjs';

const strings = { 'ad.ui.add.toggle': 'Toggle', 'ad.ui.sentence': 'Rename {0} now' };

test('loadStrings strips a BOM and returns an object', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ad-strings-'));
  const file = path.join(dir, 'table.json');
  fs.writeFileSync(file, String.fromCharCode(0xfeff) + '{"a.b": "text"}');
  try {
    assert.deepEqual(loadStrings(file), { 'a.b': 'text' });
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('labelText returns the text of a key', () => {
  assert.equal(labelText(strings, 'ad.ui.add.toggle'), 'Toggle');
});

test('labelText names an unknown key', () => {
  assert.throws(
    () => labelText(strings, 'ad.ui.nope'),
    { message: 'Unknown UI label key "ad.ui.nope". It is not in AvatarDoctor.en-US.po: fix the key, or remove the label from the page.' },
  );
});

test('labelText refuses a text with a placeholder', () => {
  assert.throws(
    () => labelText(strings, 'ad.ui.sentence'),
    { message: 'Label "ad.ui.sentence" has a placeholder; write the sentence in the page instead.' },
  );
});

test('a fixture page with an unknown label key fails and names the key', () => {
  const page = 'Press <Label k="ad.ui.add.toggle" /> then <Label k="ad.ui.nope" />.';
  const keys = usedLabels(page);
  assert.deepEqual(keys, ['ad.ui.add.toggle', 'ad.ui.nope']);
  assert.throws(() => keys.forEach((k) => labelText(strings, k)), /ad\.ui\.nope/);
});

test('usedLabels also counts headings written with k=', () => {
  assert.deepEqual(usedLabels('### Objects [#objects k=ad.ui.target.objects]\n'), ['ad.ui.target.objects']);
});

test('labelText strips rich text tags', () => {
  assert.equal(labelText({ k: '<b>Bold</b> <color=#f00>red</color>' }, 'k'), 'Bold red');
});

test('labelParts shows English on English pages, and the translated text with the English after it on Japanese pages', async () => {
  const { labelParts } = await import('../lib/strings.mjs');
  const english = { 'ad.ui.welcome.toggle': 'Make a toggle', 'ad.ui.pc': 'PC', 'ad.ui.only_english': '<b>Only</b> English' };
  const japanese = { 'ad.ui.welcome.toggle': 'トグルを作る', 'ad.ui.pc': 'PC' };
  assert.deepEqual(labelParts(english, japanese, 'ad.ui.welcome.toggle', 'en'), { english: 'Make a toggle' });
  assert.deepEqual(labelParts(english, japanese, 'ad.ui.welcome.toggle', 'ja'), { local: 'トグルを作る', english: 'Make a toggle' });
  assert.deepEqual(labelParts(english, japanese, 'ad.ui.pc', 'ja'), { english: 'PC', isolate: true }, 'the same text is shown once');
  assert.deepEqual(labelParts(english, japanese, 'ad.ui.only_english', 'ja'), { english: 'Only English', isolate: true }, 'a key the language file lacks shows English, as the tool does');
});

test('labelParts keeps the English text left to right on a page in a language without a table, such as Arabic', async () => {
  const { labelParts } = await import('../lib/strings.mjs');
  assert.deepEqual(labelParts({ 'ad.ui.add.toggle': '+ Toggle' }, undefined, 'ad.ui.add.toggle', 'ar'), { english: '+ Toggle', isolate: true });
});

test('labelParts fails on a key English does not have, in every language', async () => {
  const { labelParts } = await import('../lib/strings.mjs');
  for (const lang of ['en', 'ja', 'ar']) {
    assert.throws(() => labelParts({ a: 'A' }, { 'ad.ui.nope': 'ない' }, 'ad.ui.nope', lang), /Unknown UI label key "ad\.ui\.nope"/);
  }
});
