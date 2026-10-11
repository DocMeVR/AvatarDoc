const FENCE = /^[ \t]*(`{3,}|~{3,})[^\n]*\n[\s\S]*?(?:\n[ \t]*\1[`~]*[ \t]*(?=\n|$)|(?![\s\S]))/gm;
const INLINE = /(`+)[^`\n]*?\1/g;

export function stripCode(source) {
  return source.replace(/\r\n/g, '\n').replace(FENCE, '').replace(INLINE, '');
}
