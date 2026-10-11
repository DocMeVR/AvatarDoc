import test from 'node:test';
import assert from 'node:assert/strict';
import { headingIds } from '../../src/plugins/heading-ids.mjs';

const strings = { 'ad.ui.target.objects': 'Objects' };

function tree(text, depth = 3) {
  return { type: 'root', children: [{ type: 'heading', depth, children: [{ type: 'text', value: text }] }] };
}

function run(text, file = 'reference/toggle.mdx') {
  const t = tree(text);
  headingIds({ strings })(t, { path: file });
  return t.children[0];
}

test('an id marker sets the id and leaves the plain text', () => {
  const h = run('Objects [#objects]');
  assert.equal(h.data.hProperties.id, 'objects');
  assert.equal(h.children[0].value, 'Objects');
});

test('a matching key passes', () => {
  const h = run('Objects [#objects k=ad.ui.target.objects]');
  assert.equal(h.data.hProperties.id, 'objects');
  assert.equal(h.children[0].value, 'Objects');
});

test('a changed label fails and names the heading, file, key and new text', () => {
  strings['ad.ui.target.objects'] = 'Things';
  try {
    assert.throws(
      () => run('Objects [#objects k=ad.ui.target.objects]', 'reference/toggle.mdx'),
      { message: 'Heading "Objects" in reference/toggle.mdx no longer matches ad.ui.target.objects ("Things"): update the heading' },
    );
  } finally {
    strings['ad.ui.target.objects'] = 'Objects';
  }
});

test('an unknown key fails', () => {
  assert.throws(() => run('Objects [#objects k=ad.ui.nope]'), /ad\.ui\.nope/);
});

test('a heading without a marker is left alone', () => {
  const h = run('Plain heading');
  assert.equal(h.data, undefined);
  assert.equal(h.children[0].value, 'Plain heading');
});

test('headings nested in other nodes are found', () => {
  const t = { type: 'root', children: [{ type: 'blockquote', children: [{ type: 'heading', depth: 2, children: [{ type: 'text', value: 'Deep [#deep]' }] }] }] };
  headingIds({ strings })(t, { path: 'x.md' });
  assert.equal(t.children[0].children[0].data.hProperties.id, 'deep');
});

test('the table cache follows the file when its text and time change', async () => {
  const fs = await import('node:fs');
  const os = await import('node:os');
  const path = await import('node:path');
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ad-heading-'));
  const file = path.join(dir, 'strings.json');
  try {
    fs.writeFileSync(file, JSON.stringify({ 'ad.ui.x': 'Old' }));
    const first = tree('Old [#x k=ad.ui.x]');
    headingIds({ stringsPath: file })(first, { path: 'a.mdx' });
    assert.equal(first.children[0].data.hProperties.id, 'x');
    fs.writeFileSync(file, JSON.stringify({ 'ad.ui.x': 'New' }));
    const later = new Date(Date.now() + 5000);
    fs.utimesSync(file, later, later);
    const second = tree('New [#x k=ad.ui.x]');
    headingIds({ stringsPath: file })(second, { path: 'a.mdx' });
    assert.equal(second.children[0].data.hProperties.id, 'x');
    assert.throws(() => headingIds({ stringsPath: file })(tree('Old [#x k=ad.ui.x]'), { path: 'a.mdx' }), /no longer matches/);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
