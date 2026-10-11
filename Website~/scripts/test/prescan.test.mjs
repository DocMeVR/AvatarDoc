import test from 'node:test';
import assert from 'node:assert/strict';
import { prescanPages } from '../lib/prescan.mjs';

const strings = { 'ad.ui.add.toggle': 'Toggle', 'ad.ui.objects': 'Objects' };

test('a bad label key is reported with the page file and the key', () => {
  const errors = prescanPages(
    [{ file: 'src/content/docs/guides/x.mdx', source: 'Press <Label k="ad.ui.nope" />.' }],
    strings,
    { shotNames: new Set(), allowMissing: false },
  );
  assert.equal(errors.length, 1);
  assert.match(errors[0], /^src\/content\/docs\/guides\/x\.mdx: /);
  assert.match(errors[0], /ad\.ui\.nope/);
});

test('an unknown key in a heading marker is reported', () => {
  const errors = prescanPages([{ file: 'p.mdx', source: '### Objects [#objects k=ad.ui.gone]' }], strings, { shotNames: new Set(), allowMissing: false });
  assert.match(errors[0], /^p\.mdx: .*ad\.ui\.gone/);
});

test('a missing shot is reported in strict mode and allowed otherwise', () => {
  const page = [{ file: 'p.mdx', source: '<Shot name="x/missing" alt="A." />' }];
  const strict = prescanPages(page, strings, { shotNames: new Set(), allowMissing: false });
  assert.equal(strict.length, 1);
  assert.match(strict[0], /^p\.mdx: .*x\/missing/);
  assert.deepEqual(prescanPages(page, strings, { shotNames: new Set(), allowMissing: true }), []);
  assert.deepEqual(prescanPages(page, strings, { shotNames: new Set(['x/missing']), allowMissing: false }), []);
});

test('a good page gives no errors', () => {
  assert.deepEqual(
    prescanPages([{ file: 'p.mdx', source: '<Label k="ad.ui.add.toggle" />\n### Objects [#objects k=ad.ui.objects]' }], strings, { shotNames: new Set(), allowMissing: false }),
    [],
  );
});

test('labels and shots inside code are ignored', () => {
  const fence = '`'.repeat(3);
  const source = [
    'Write `<Label k="ad.ui.nope" />` like this.',
    fence + 'mdx',
    '<Label k="ad.ui.nope" />',
    '<Shot name="x/missing" alt="A." />',
    fence,
    'Done.',
  ].join('\n');
  assert.deepEqual(prescanPages([{ file: 'p.mdx', source }], strings, { shotNames: new Set(), allowMissing: false }), []);
});

test('a label after a code block is still checked', () => {
  const fence = '`'.repeat(3);
  const source = [fence, '<Label k="ad.ui.fine" />', fence, '<Label k="ad.ui.nope" />'].join('\n');
  const errors = prescanPages([{ file: 'p.mdx', source }], strings, { shotNames: new Set(), allowMissing: false });
  assert.equal(errors.length, 1);
  assert.match(errors[0], /ad\.ui\.nope/);
});
