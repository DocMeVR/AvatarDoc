import test from 'node:test';
import assert from 'node:assert/strict';
import { usedShots, checkShots, altProblem, minorOf } from '../lib/shots.mjs';

test('usedShots finds every Shot name in every page', () => {
  const used = usedShots([
    { file: 'a.mdx', source: '<Shot name="home/hero" alt="x." />\n<Shot\n  name="home/two"\n alt="y." />' },
    { file: 'b.mdx', source: 'no shots here' },
  ]);
  assert.deepEqual(used, [{ file: 'a.mdx', name: 'home/hero' }, { file: 'a.mdx', name: 'home/two' }]);
});

test('a fixture page with a missing shot gives one error naming page and shot', () => {
  const used = usedShots([{ file: 'guides/fixture.mdx', source: '<Shot name="x/missing" alt="A shot." />' }]);
  const { errors } = checkShots(used, { shots: {} }, { packageVersion: '0.3.0' });
  assert.equal(errors.length, 1);
  assert.match(errors[0], /guides\/fixture\.mdx/);
  assert.match(errors[0], /x\/missing/);
});

test('allowMissing turns the error into nothing', () => {
  const r = checkShots([{ file: 'a.mdx', name: 'x/y' }], { shots: {} }, { allowMissing: true, packageVersion: '0.3.0' });
  assert.deepEqual(r.errors, []);
  assert.equal(r.missing.length, 1);
});

test('an empty or absent manifest works', () => {
  for (const manifest of [undefined, {}, { shots: {} }]) {
    const r = checkShots([], manifest, { packageVersion: '0.3.0' });
    assert.deepEqual([r.errors, r.unused, r.older], [[], [], []]);
  }
});

test('unused shots and shots from an older minor are listed', () => {
  const manifest = { shots: {
    'home/hero': { version: '0.2.4', taken: '2026-10-01' },
    'home/old': { version: '0.3.1', taken: '2026-10-01' },
    'home/unused': { version: '0.3.0', taken: '2026-10-01' },
  } };
  const used = [{ file: 'i.mdx', name: 'home/hero' }, { file: 'i.mdx', name: 'home/old' }];
  const r = checkShots(used, manifest, { packageVersion: '0.3.0' });
  assert.deepEqual(r.errors, []);
  assert.deepEqual(r.unused, ['home/unused']);
  assert.deepEqual(r.older, ['home/hero (0.2.4)']);
});

test('minorOf reads major and minor', () => {
  assert.equal(minorOf('1.12.3'), '1.12');
});

test('altProblem checks length, full stop and the banned opening', () => {
  assert.equal(altProblem('The toggle row.'), null);
  assert.match(altProblem('Screenshot of the row.'), /Screenshot of/);
  assert.match(altProblem('No full stop'), /full stop/);
  assert.match(altProblem('a'.repeat(150) + '.'), /150/);
  assert.match(altProblem(''), /alt/i);
});

test('usedShots reads attributes whose values contain a greater-than sign', () => {
  const used = usedShots([{ file: 'a.mdx', source: '<Shot alt="Row > Row." name="x/y" />' }]);
  assert.deepEqual(used, [{ file: 'a.mdx', name: 'x/y' }]);
});

test('usedShots refuses a name that is not a plain string', () => {
  assert.throws(() => usedShots([{ file: 'a.mdx', source: '<Shot name={"x/y"} alt="A." />' }]), /a\.mdx.*name/);
  assert.throws(() => usedShots([{ file: 'a.mdx', source: '<Shot alt="A." />' }]), /a\.mdx.*name/);
});
