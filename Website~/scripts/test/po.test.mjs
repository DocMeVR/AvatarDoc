import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { parsePo, poStrings, loadPoStrings } from '../lib/po.mjs';

const header = 'msgid ""\nmsgstr ""\n"Language: ja-JP\\n"\n"Content-Type: text/plain; charset=UTF-8\\n"\n\n';

test('poStrings reads each msgid as a key and its msgstr as the text, and leaves the header out', () => {
  const source = header + '#. en: Simple\nmsgid "ad.ui.mode.simple"\nmsgstr "シンプル"\n\nmsgid "ad.ui.mode.advanced"\nmsgstr "詳細"\n';
  assert.deepEqual(poStrings(source), { 'ad.ui.mode.simple': 'シンプル', 'ad.ui.mode.advanced': '詳細' });
});

test('a text may go on over several quoted lines', () => {
  assert.deepEqual(poStrings('msgid "a"\nmsgstr ""\n"first "\n"second"\n'), { a: 'first second' });
});

test('escapes read as their characters', () => {
  assert.deepEqual(poStrings('msgid "a"\nmsgstr "\\"{0}\\"\\nline\\t\\\\"\n'), { a: '"{0}"\nline\t\\' });
});

test('comments are skipped, and so are old entries, outdated texts and empty texts, as in the tool', () => {
  const source =
    '# a note\n#. en: A\nmsgid "a"\nmsgstr "A"\n\n#. en: B\n#, fuzzy\nmsgid "b"\nmsgstr "B"\n\nmsgid "c"\nmsgstr ""\n\nmsgid "d"\nmsgstr "  "\n\n#. en: Gone\n#~ msgid "gone"\n#~ msgstr "Gone"\n';
  assert.deepEqual(poStrings(source), { a: 'A' });
  assert.deepEqual(
    parsePo(source).map((e) => [e.key, e.obsolete, e.fuzzy]),
    [
      ['a', false, false],
      ['b', false, true],
      ['c', false, false],
      ['d', false, false],
      ['gone', true, false],
    ],
  );
});

test('a fuzzy mark among other flags counts, and belongs to the next entry only', () => {
  const source = '#, c-format, fuzzy\nmsgid "a"\nmsgstr "A"\n\nmsgid "b"\nmsgstr "B"\n';
  assert.deepEqual(
    parsePo(source).map((e) => [e.key, e.fuzzy]),
    [
      ['a', true],
      ['b', false],
    ],
  );
});

test('a quote, a backslash or an escape the tool does not read makes the file unreadable', () => {
  assert.throws(() => poStrings('msgid "a"\nmsgstr "A\\qB"\n', 'x.po'), /x\.po, line 2: \\q is not an escape/);
  assert.throws(() => poStrings('msgid "a"\nmsgstr "say "hi" now"\n', 'x.po'), /x\.po, line 2: a double quote inside a text/);
  assert.throws(() => poStrings('msgid "a"\nmsgstr "abc\\"\n', 'x.po'), /x\.po, line 2: the text ends in a lone backslash/);
  assert.throws(() => poStrings('msgid "a"\nmsgstr "x" "y"\n', 'x.po'), /x\.po, line 2: a double quote inside a text/);
  assert.deepEqual(poStrings('msgid "a"\nmsgstr "\\\\ \\" \\n \\t \\r"\n'), { a: '\\ " \n \t \r' });
});

test('a byte order mark and Windows line ends are read', () => {
  assert.deepEqual(poStrings(String.fromCharCode(0xfeff) + 'msgid "a"\r\nmsgstr "A"\r\n'), { a: 'A' });
});

test('the first of a key written twice wins, as in the tool', () => {
  assert.deepEqual(poStrings('msgid "a"\nmsgstr "first"\n\nmsgid "a"\nmsgstr "second"\n'), { a: 'first' });
});

test('a line that is not understood names the file and the line', () => {
  assert.throws(() => poStrings('msgid "a"\nmsgstr "A"\n\nmsgid "b"\nmsgstr B\n', 'AvatarDoctor.ja-JP.po'), {
    message: 'AvatarDoctor.ja-JP.po, line 5: a text must stand in double quotes.',
  });
  assert.throws(() => poStrings('{ "a": "A" }', 'x.po'), /x\.po, line 1: this line is not understood/);
  assert.throws(() => poStrings('msgid "a"\n\nmsgid "b"\nmsgstr "B"\n', 'x.po'), /line 1: the key "a" has no msgstr line/);
});

test('the English file of the tool loads', () => {
  const file = fileURLToPath(new URL('../../../Editor/Localization/AvatarDoctor.en-US.po', import.meta.url));
  const strings = loadPoStrings(file);
  assert.ok(Object.keys(strings).length > 1000, 'it holds every key of the tool');
  assert.equal(strings['ad.ui.list.separator'], ', ', 'a text keeps the spaces at its ends');
  assert.equal(strings['ad.ui.quoted'], '"{0}"');
  assert.ok(!Object.prototype.hasOwnProperty.call(strings, ''), 'the header is no text');
});

test('loadPoStrings names the file in an error', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ad-po-'));
  const file = path.join(dir, 'AvatarDoctor.en-US.po');
  fs.writeFileSync(file, 'msgid "a"\nnot a line\n');
  try {
    assert.throws(() => loadPoStrings(file), /AvatarDoctor\.en-US\.po, line 2/);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
