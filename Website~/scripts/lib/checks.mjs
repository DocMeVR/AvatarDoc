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

function pageUrlOf(distDir, file) {
  const rel = path.relative(distDir, file).split(path.sep).join('/');
  return rel.replace(/index\.html$/, '');
}

function idsOf(html) {
  return new Set([...html.matchAll(/\sid="([^"]*)"/g)].map((m) => m[1]));
}

export function brokenLinks(distDir, base = '') {
  const pages = listFiles(distDir, ['.html']);
  const html = new Map(pages.map((file) => [pageUrlOf(distDir, file), fs.readFileSync(file, 'utf8')]));
  const idCache = new Map();
  const idsFor = (url) => {
    if (!idCache.has(url)) idCache.set(url, idsOf(html.get(url)));
    return idCache.get(url);
  };
  const prefix = base.replace(/\/$/, '');
  const problems = [];
  for (const [url, source] of html) {
    const from = '/' + url;
    for (const m of source.matchAll(/<a\s[^>]*?href="([^"]*)"/g)) {
      const href = m[1].replace(/&amp;/g, '&');
      if (href === '' || /^[a-z][a-z0-9+.-]*:/i.test(href) || href.startsWith('//')) continue;
      const hashAt = href.indexOf('#');
      const pathPart = (hashAt < 0 ? href : href.slice(0, hashAt)).split('?')[0];
      const hash = hashAt < 0 ? '' : decodeURIComponent(href.slice(hashAt + 1));
      let target;
      if (pathPart === '') target = from;
      else if (pathPart.startsWith('/')) {
        if (prefix && !pathPart.startsWith(prefix + '/') && pathPart !== prefix) {
          problems.push(`${url || 'index'}: link "${href}" does not start with the site base ${prefix}.`);
          continue;
        }
        target = pathPart.slice(prefix.length) || '/';
      } else target = new URL(pathPart, 'http://x' + from).pathname;
      const key = target.replace(/^\//, '');
      if (/\.[a-z0-9]+$/i.test(key)) {
        if (!fs.existsSync(path.join(distDir, key))) problems.push(`${url || 'index'}: link "${href}" points at a file that is not built: ${key}.`);
        continue;
      }
      const pageKey = key === '' || key.endsWith('/') ? key : key + '/';
      if (!html.has(pageKey)) {
        problems.push(`${url || 'index'}: link "${href}" points at a page that is not built: /${pageKey}.`);
        continue;
      }
      if (hash !== '' && !idsFor(pageKey).has(hash)) {
        problems.push(`${url || 'index'}: link "${href}" points at #${hash}, which /${pageKey} does not have.`);
      }
    }
  }
  return problems;
}
