const TAG = /<Shot\b(?:[^>"'{]|"[^"]*"|'[^']*'|\{[^}]*\})*>/g;
const ATTRIBUTE = /([\w-]+)\s*=\s*("[^"]*"|'[^']*'|\{[^}]*\})/g;

export function usedShots(sources) {
  const used = [];
  for (const { file, source } of sources) {
    for (const tag of source.matchAll(TAG)) {
      const name = [...tag[0].matchAll(ATTRIBUTE)].find((m) => m[1] === 'name');
      if (!name || !/^["']/.test(name[2])) {
        throw new Error(`${file}: a Shot needs name="page/shot" as a plain string, not an expression.`);
      }
      used.push({ file, name: name[2].slice(1, -1) });
    }
  }
  return used;
}

export function minorOf(version) {
  const [major, minor] = version.split('.');
  return `${major}.${minor}`;
}

function isOlderMinor(version, packageVersion) {
  const [aMajor, aMinor] = version.split('.').map(Number);
  const [bMajor, bMinor] = packageVersion.split('.').map(Number);
  return aMajor < bMajor || (aMajor === bMajor && aMinor < bMinor);
}

export function checkShots(used, manifest, { allowMissing = false, packageVersion } = {}) {
  const known = (manifest && manifest.shots) || {};
  const missing = used
    .filter(({ name }) => !Object.prototype.hasOwnProperty.call(known, name))
    .map(
      ({ file, name }) =>
        `${file} uses the shot "${name}", which is not in src/assets/shots/manifest.json. Capture it (DocsShots tests) or fix the name.`,
    );
  const usedNames = new Set(used.map(({ name }) => name));
  return {
    errors: allowMissing ? [] : missing,
    missing,
    unused: Object.keys(known).filter((name) => !usedNames.has(name)).sort(),
    older: Object.entries(known)
      .filter(([, info]) => info.version && packageVersion && isOlderMinor(info.version, packageVersion))
      .map(([name, info]) => `${name} (${info.version})`)
      .sort(),
  };
}

const FULL_STOPS = { ja: '。' };

export function altProblem(alt, lang = 'en') {
  if (!alt || alt.trim() === '') return 'Every image needs alt text.';
  if (/^screenshot of/i.test(alt)) return 'Alt text must not start with "Screenshot of": say what the picture shows.';
  if (alt.length > 150) return `Alt text is ${alt.length} characters; the limit is 150.`;
  const stop = FULL_STOPS[lang.split('-')[0]] ?? '.';
  if (!alt.endsWith(stop)) return stop === '.' ? 'Alt text must end with a full stop.' : `Alt text in this language must end with "${stop}".`;
  return null;
}
