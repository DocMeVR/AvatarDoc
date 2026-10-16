import { stripCode } from './code.mjs';
import { labelText, usedLabelTags, usedHeadingKeys } from './strings.mjs';
import { usedShots } from './shots.mjs';

export function prescanPages(pages, strings, { shotNames, allowMissing }) {
  const errors = [];
  for (const { file, source: raw } of pages) {
    const source = stripCode(raw);
    for (const key of usedLabelTags(source)) {
      try {
        labelText(strings, key);
      } catch (e) {
        errors.push(`${file}: ${e.message}`);
      }
    }
    for (const key of usedHeadingKeys(source)) {
      if (!Object.prototype.hasOwnProperty.call(strings, key)) {
        errors.push(`${file}: Heading marker uses the unknown UI label key "${key}". It is not in AvatarDoctor.en-US.po.`);
      }
    }
    if (!allowMissing) {
      let shots = [];
      try {
        shots = usedShots([{ file, source }]);
      } catch (e) {
        errors.push(e.message);
      }
      for (const { name } of shots) {
        if (!shotNames.has(name)) {
          errors.push(`${file}: Shot "${name}" not found in src/assets/shots. Capture it (DocsShots tests) or fix the name.`);
        }
      }
    }
  }
  return errors;
}
