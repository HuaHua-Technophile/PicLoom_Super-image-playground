import { TEMPLATE_TAGS_LIMIT } from '../types';

/** Splits a comma (or Chinese comma) separated tag input into a clean list. */
export function parseTags(raw: string): string[] {
  const seen = new Set<string>();
  const result: string[] = [];
  for (const part of raw.split(/[,，]/)) {
    const tag = part.trim();
    if (!tag) {
      continue;
    }
    const key = tag.toLowerCase();
    if (seen.has(key)) {
      continue;
    }
    seen.add(key);
    result.push(tag);
    if (result.length >= TEMPLATE_TAGS_LIMIT) {
      break;
    }
  }
  return result;
}
