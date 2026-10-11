import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { coverage, toolLinkProblems, oversizeShots, brokenLinks } from '../lib/checks.mjs';

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

test('brokenLinks names the page, the link and the target of a missing page and a missing hash', () => {
  const dist = fs.mkdtempSync(path.join(os.tmpdir(), 'ad-links-'));
  const put = (rel, html) => {
    fs.mkdirSync(path.join(dist, path.dirname(rel)), { recursive: true });
    fs.writeFileSync(path.join(dist, rel), html);
  };
  try {
    put('index.html', '<a href="/AvatarDoc/guides/a/#top">good</a><a href="/AvatarDoc/guides/gone/">page</a><a href="https://example.com/x#y">out</a>');
    put('guides/a/index.html', '<h2 id="top">T</h2><a href="../b/#nope">hash</a><a href="#top">same</a><a href="../b/#here">rel</a>');
    put('guides/b/index.html', '<h2 id="here">H</h2>');
    const problems = brokenLinks(dist, '/AvatarDoc');
    assert.equal(problems.length, 2);
    assert.match(problems.find((p) => p.startsWith('index:')), /^index: link "\/AvatarDoc\/guides\/gone\/" .*\/guides\/gone\//);
    assert.match(problems.find((p) => p.startsWith('guides/a/')), /^guides\/a\/: link "\.\.\/b\/#nope" .*#nope.*\/guides\/b\//);
  } finally {
    fs.rmSync(dist, { recursive: true, force: true });
  }
});
