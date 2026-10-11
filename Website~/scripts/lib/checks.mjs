import fs from 'node:fs';
import path from 'node:path';

export function listFiles(dir, extensions) {
  if (!fs.existsSync(dir)) return [];
  const found = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) found.push(...listFiles(full, extensions));
    else if (extensions.some((e) => entry.name.endsWith(e))) found.push(full);
  }
  return found.sort();
}

export function coverage(strings, usedKeys) {
  return Object.keys(strings)
    .filter((key) => key.startsWith('ad.ui.') && !key.endsWith('.tip') && !key.endsWith('.one'))
    .filter((key) => strings[key].length <= 30 && !/\{\d+\}/.test(strings[key]))
    .filter((key) => !usedKeys.has(key))
    .sort();
}

export function toolLinkProblems(distDir, links) {
  const problems = [];
  for (const page of links.pages || []) {
    if (!fs.existsSync(path.join(distDir, page, 'index.html'))) {
      problems.push(`Tool link page "${page}" is missing from the build: dist/${page}/index.html does not exist.`);
    }
  }
  const anchors = links.anchors || [];
  if (anchors.length > 0) {
    const messages = path.join(distDir, 'reference', 'messages', 'index.html');
    if (!fs.existsSync(messages)) {
      problems.push('The Messages page is missing from the build, so no tool link anchor can be checked.');
    } else {
      const html = fs.readFileSync(messages, 'utf8');
      for (const a of anchors) {
        if (!html.includes(`id="${a}"`)) problems.push(`Tool link anchor "${a}" is missing from the Messages page.`);
      }
    }
  }
  return problems;
}

export function oversizeShots(shotsDir, limitBytes = 250 * 1024) {
  return listFiles(shotsDir, ['.png'])
    .filter((file) => fs.statSync(file).size > limitBytes)
    .map((file) => `${path.relative(shotsDir, file).split(path.sep).join('/')} (${Math.round(fs.statSync(file).size / 1024)} KB)`);
}
