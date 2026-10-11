import test from 'node:test';
import assert from 'node:assert/strict';
import { anchor, messageEntries, renderMessages, plainMessage } from '../lib/messages.mjs';

test('anchor lower-cases and turns dots and underscores into dashes', () => {
  assert.equal(anchor('ad.warn.bits_exceeded'), 'ad-warn-bits-exceeded');
});

test('messageEntries keeps only the three message families, grouped and sorted', () => {
  const groups = messageEntries({
    'ad.warn.b': 'B.', 'ad.warn.a': 'A.', 'ad.error.z': 'Z.', 'ad.info.i': 'I.', 'ad.ui.add.toggle': 'Toggle',
  });
  assert.deepEqual(groups.map((g) => g.title), ['Errors', 'Warnings', 'Notes']);
  assert.deepEqual(groups[0].entries.map((e) => e.key), ['ad.error.z']);
  assert.deepEqual(groups[1].entries.map((e) => e.key), ['ad.warn.a', 'ad.warn.b']);
  assert.deepEqual(groups[2].entries.map((e) => e.key), ['ad.info.i']);
});

const risky = {
  'ad.warn.bits_exceeded': 'Needs <b>{0}</b> bits of <color=#f00>{1}</color>. Remove some.',
  'ad.warn.pipe': 'Pick a | b. Then go on.',
  'ad.warn.angle': 'Use a<b and {curly} braces. Done.',
  'ad.warn.long': 'This first sentence is much too long to be a heading because it keeps going on and on and on and on forever. Next.',
};

function render(notes = {}) {
  return renderMessages(messageEntries(risky), notes);
}

test('rich text tags are stripped and placeholders become an ellipsis', () => {
  assert.equal(plainMessage(risky['ad.warn.bits_exceeded']), 'Needs … bits of …. Remove some.');
});

test('a pipe is escaped, a raw < and { are written as entities', () => {
  const md = render();
  assert.match(md, /Pick a \\\| b\./);
  assert.match(md, /a&lt;b and &#123;curly} braces\./);
  assert.doesNotMatch(md, /<b>|<color/);
});

test('each entry has a short heading with its anchor and the full text as a quote', () => {
  const md = render();
  assert.match(md, /^### Needs … bits of …\. \[#ad-warn-bits-exceeded\]$/m);
  assert.match(md, /^> Needs … bits of …\. Remove some\.$/m);
  for (const line of md.split('\n').filter((l) => l.startsWith('### '))) {
    const text = line.replace(/^### /, '').replace(/ \[#[^\]]+\]$/, '');
    assert.ok(text.length <= 80, 'heading too long: ' + text);
  }
});

test('a long first sentence is cut at a word and still at most 80 characters', () => {
  const md = render();
  const line = md.split('\n').find((l) => l.includes('[#ad-warn-long]'));
  const text = line.replace(/^### /, '').replace(/ \[#[^\]]+\]$/, '');
  assert.ok(text.length <= 80);
  assert.ok(text.endsWith('...'));
});

test('a note follows under What to do', () => {
  const md = render({ 'ad.warn.pipe': 'Pick one side.' });
  assert.match(md, /\*\*What to do\*\*\n\nPick one side\./);
  assert.equal((md.match(/What to do/g) || []).length, 1);
});

test('every anchor of the risky fixture is present and unique', () => {
  const md = render();
  const ids = [...md.matchAll(/\[#([a-z0-9-]+)\]$/gm)].map((m) => m[1]);
  assert.deepEqual(ids.sort(), ['ad-warn-angle', 'ad-warn-bits-exceeded', 'ad-warn-long', 'ad-warn-pipe']);
});

test('the Warnings group heading is a second level heading', () => {
  assert.match(render(), /^## Warnings$/m);
});

const more = {
  'ad.warn.star': 'Use *this* and `that` & "quoted" ~x~ a_b_c.',
  'ad.warn.lead': '- not a list\n1. not numbered\n# not a heading',
  'ad.info.target_shared': '{0} is changed by more than one control. Later wins.',
  'ad.info.count': 'Holds {0} shapes.',
  'ad.info.count.one': 'Holds {0} shape.',
  'ad.info.count.default': 'Holds {0} shapes at default.',
  'ad.info.count.default.one': 'Holds {0} shape at default.',
};

test('risky characters and line starts are escaped', () => {
  const md = renderMessages(messageEntries(more), {});
  assert.match(md, /Use \\\*this\\\* and \\`that\\` &amp; "quoted" \\~x\\~ a\\_b\\_c\./);
  assert.match(md, /^> \\- not a list$/m);
  assert.match(md, /^> 1\\\. not numbered$/m);
  assert.match(md, /^> \\# not a heading$/m);
});

test('plural twins are skipped and dotted children fold into their parent', () => {
  const groups = messageEntries(more);
  const notes = groups.find((g) => g.title === 'Notes').entries;
  assert.deepEqual(notes.map((e) => e.key), ['ad.info.count', 'ad.info.target_shared']);
  assert.deepEqual(notes[0].variants.map((v) => v.key), ['ad.info.count.default']);
  const md = renderMessages(groups, {});
  assert.match(md, /<span id="ad-info-count-default"><\/span>Holds … shapes at default\./);
  assert.doesNotMatch(md, /### .*\[#ad-info-count-default\]/);
});

test('a heading that starts with a placeholder uses the key tail', () => {
  const md = renderMessages(messageEntries(more), {});
  assert.match(md, /^### Target shared \[#ad-info-target-shared\]$/m);
});

test('a cut heading ends with three dots, never the placeholder glyph', () => {
  const md = render();
  const line = md.split('\n').find((l) => l.includes('[#ad-warn-long]'));
  assert.match(line, /\.\.\. \[#ad-warn-long\]$/);
});

test('the Messages page keeps the contents list to second level headings', async () => {
  const { messagesPage } = await import('../lib/pages.mjs');
  assert.match(messagesPage('body', 'x'), /tableOfContents:\n {2}maxHeadingLevel: 2\n/);
});
