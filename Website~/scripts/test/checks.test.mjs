import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { coverage, toolLinkProblems, oversizeShots } from '../lib/checks.mjs';

test('coverage lists short labels no page uses, skips tips, plurals and sentences', () => {
  const strings = {
    'ad.ui.add.toggle': 'Toggle',
    'ad.ui.add.radial': 'Radial',
    'ad.ui.add.toggle.tip': 'Adds a toggle.',
    'ad.ui.count.one': '1 row',
    'ad.ui.long': 'This is a rather long sentence, not a label.',
    'ad.warn.x': 'Short.',
  };
  assert.deepEqual(coverage(strings, new Set(['ad.ui.add.toggle'])), ['ad.ui.add.radial']);
});

test('toolLinkProblems names a missing page and a missing anchor', () => {
  const dist = fs.mkdtempSync(path.join(os.tmpdir(), 'ad-dist-'));
  try {
    fs.mkdirSync(path.join(dist, 'reference', 'messages'), { recursive: true });
    fs.mkdirSync(path.join(dist, 'start-here', 'first-toggle'), { recursive: true });
    fs.writeFileSync(path.join(dist, 'start-here', 'first-toggle', 'index.html'), '<html></html>');
    fs.writeFileSync(path.join(dist, 'reference', 'messages', 'index.html'), '<h3 id="ad-warn-a">A</h3>');
    const problems = toolLinkProblems(dist, { pages: ['start-here/first-toggle', 'reference/inspector'], anchors: ['ad-warn-a', 'ad-warn-b'] });
    assert.equal(problems.length, 2);
    assert.match(problems[0], /reference\/inspector/);
    assert.match(problems[1], /ad-warn-b/);
  } finally {
    fs.rmSync(dist, { recursive: true, force: true });
  }
});

test('toolLinkProblems accepts the empty starting file', () => {
  assert.deepEqual(toolLinkProblems(os.tmpdir(), { pages: [], anchors: [] }), []);
});

test('oversizeShots lists PNGs over 250 KB', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ad-shots-'));
  try {
    fs.mkdirSync(path.join(dir, 'p'));
    fs.writeFileSync(path.join(dir, 'p', 'big.png'), Buffer.alloc(260 * 1024));
    fs.writeFileSync(path.join(dir, 'p', 'ok.png'), Buffer.alloc(1024));
    const r = oversizeShots(dir);
    assert.equal(r.length, 1);
    assert.match(r[0], /p\/big\.png/);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
