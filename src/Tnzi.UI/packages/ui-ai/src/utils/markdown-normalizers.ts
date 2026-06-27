/**
 * Markdown normalizers — pure text-transform utilities for LLM output post-processing.
 *
 * These are framework-agnostic: no dependencies, no side effects, no Vue reactivity.
 * Ported from Fabrikam as standalone utilities so any consumer can use them directly.
 */

// ---------------------------------------------------------------------------
// CJK detection
// ---------------------------------------------------------------------------

/** Returns true if the code point is a CJK character (Han, Hiragana, Katakana, or Hangul). */
function isCjk(cp: number): boolean {
  return (
    (cp >= 0x4e00 && cp <= 0x9fff) || // CJK Unified Ideographs
    (cp >= 0x3040 && cp <= 0x309f) || // Hiragana
    (cp >= 0x30a0 && cp <= 0x30ff) || // Katakana
    (cp >= 0xac00 && cp <= 0xd7af)    // Hangul Syllables
  );
}

// ---------------------------------------------------------------------------
// normalizeCjkSpacing
// ---------------------------------------------------------------------------

/**
 * Adds a space between CJK (Chinese/Japanese/Korean) characters and
 * surrounding Latin / ASCII characters, since LLM output often omits them.
 *
 * Operates idempotently — running it twice produces the same result.
 *
 * @example
 * normalizeCjkSpacing('你好world')  // '你好 world'
 * normalizeCjkSpacing('Hello世界')  // 'Hello 世界'
 * normalizeCjkSpacing('你好 world')  // '你好 world'  (already spaced)
 */
export function normalizeCjkSpacing(text: string): string {
  if (!text) return text;

  // Step 1: Extract all code points from the input
  const codePoints: number[] = [];
  let i = 0;
  while (i < text.length) {
    const cp = text.codePointAt(i)!;
    codePoints.push(cp);
    i += cp > 0xffff ? 2 : 1;
  }

  // Step 2: Determine at which boundaries a space must be inserted.
  // A boundary (idx → idx+1) needs a space if:
  //   - left is CJK and right is NOT CJK  → insert AFTER left
  //   - left is NOT CJK and right is CJK  → insert BEFORE right
  // Skip if the right character is already horizontal whitespace (already spaced).
  const needSpaceAfter: boolean[] = codePoints.map(() => false);
  for (let idx = 0; idx < codePoints.length - 1; idx++) {
    const leftCp = codePoints[idx]!;
    const rightCp = codePoints[idx + 1]!;
    const leftIsCjk = isCjk(leftCp);
    const rightIsCjk = isCjk(rightCp);
    const rightIsWs = rightCp === 0x20 || rightCp === 0x09; // space or tab
    if (leftIsCjk !== rightIsCjk && !rightIsWs) {
      needSpaceAfter[idx] = true; // space goes after this character
    }
  }

  // Step 3: Reconstruct with spaces
  const result: number[] = [];
  for (let idx = 0; idx < codePoints.length; idx++) {
    result.push(codePoints[idx]!);
    if (needSpaceAfter[idx]) {
      result.push(0x20); // space
    }
  }

  return String.fromCodePoint(...result);
}

// ---------------------------------------------------------------------------
// stripInvisibleControlChars
// ---------------------------------------------------------------------------

/**
 * Invisible Unicode format characters that can corrupt markdown rendering.
 * Covers: soft-hyphen, variation selectors, word joiner, zero-width space/joiner,
 * LTR/RTL marks, and various format effect characters.
 */
const INVISIBLE_FORMAT_CHAR =
  // This class deliberately enumerates invisible / combining format code points
  // (incl. the combining grapheme joiner) so they can be stripped individually.
  // eslint-disable-next-line no-misleading-character-class
  /[\u00AD\u034F\u180E\u200B-\u200F\u202A-\u202E\u2060-\u2064\u206A-\u206F\uFEFF\uFFF9-\uFFFB]/g;

/**
 * Removes invisible Unicode characters that can appear in LLM output and
 * cause rendering issues or security concerns (e.g. homograph attacks).
 *
 * @example
 * stripInvisibleControlChars('a\u200bb')  // 'ab'
 * stripInvisibleControlChars('a\u200eb\u200fc')  // 'abc'
 */
export function stripInvisibleControlChars(text: string): string {
  return text.replace(INVISIBLE_FORMAT_CHAR, '');
}

// ---------------------------------------------------------------------------
// normalizeTimeFormat
// ---------------------------------------------------------------------------

/**
 * Normalises time representations produced by LLMs to consistent formats.
 *
 * Fixes common LLM formatting quirks:
 * - Removes spaces around colons in HH:MM  (e.g. "10 : 30" → "10:30")
 * - Adds dash between AM/PM time ranges     (e.g. "9 AM to 5 PM" → "9 AM - 5 PM")
 * - Removes spaces inside parenthetical ranges (e.g. "( 9:00 - 10:00 )" → "(9:00-10:00)")
 *
 * Passes well-formed ISO-8601 timestamps through unchanged.
 *
 * @example
 * normalizeTimeFormat('Meeting at 10 : 30')  // 'Meeting at 10:30'
 * normalizeTimeFormat('Hours: 9 AM to 5 PM')  // 'Hours: 9 AM - 5 PM'
 * normalizeTimeFormat('2026-04-16T10:30:00Z')  // '2026-04-16T10:30:00Z'
 */
export function normalizeTimeFormat(text: string): string {
  // Remove spaces around colons in HH:MM — "10 : 30" → "10:30"
  let result = text.replace(/(\d{1,2})\s*:\s*(\d{2})/g, '$1:$2');

  // Add dash between AM/PM time ranges — "9 AM to 5 PM" → "9 AM - 5 PM"
  result = result.replace(
    /(\b\d{1,2}\s?(?:AM|PM))\s+(?:to|-)\s+(\d{1,2}\s?(?:AM|PM)\b)/gi,
    '$1 - $2',
  );

  // Remove spaces inside parenthetical time ranges — "( 9:00 - 10:00 )" → "(9:00-10:00)"
  result = result.replace(
    /\(\s*(\d{1,2}:\d{2})\s*(?:[-]\s*)?(\d{1,2}:\d{2})\s*\)/g,
    '($1-$2)',
  );

  return result;
}
