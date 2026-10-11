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
    { message: 'Unknown UI label key "ad.ui.nope". It is not in AvatarDoctor.en-US.json: fix the key, or remove the label from the page.' },
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
