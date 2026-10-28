/**
 * @tnzi/core/utils/string
 *
 * String manipulation utilities.
 */

/**
 * Truncate string with ellipsis. `maxLength` counts UTF-16 code units, the
 * ellipsis included; a cut that would fall inside a surrogate pair (an emoji,
 * a supplementary-plane character) moves one unit back, so the head never
 * ends in half a character that renders as U+FFFD.
 */
export function truncate(str: string, maxLength: number, ellipsis = '...'): string {
  if (str.length <= maxLength) return str;
  // When the ellipsis alone does not fit, `maxLength - ellipsis.length` goes
  // negative and `slice(0, -1)` keeps almost the whole string, so the result
  // came out longer than maxLength. Return as much of the ellipsis as fits.
  if (maxLength <= ellipsis.length) return ellipsis.slice(0, Math.max(0, maxLength));
  let end = maxLength - ellipsis.length;
  if (isHighSurrogate(str.charCodeAt(end - 1)) && isLowSurrogate(str.charCodeAt(end))) end -= 1;
  return str.slice(0, end) + ellipsis;
}

function isHighSurrogate(code: number): boolean {
  return code >= 0xd800 && code <= 0xdbff;
}

function isLowSurrogate(code: number): boolean {
  return code >= 0xdc00 && code <= 0xdfff;
}

/**
 * Capitalize first letter
 */
export function capitalize(str: string): string {
  return str.charAt(0).toUpperCase() + str.slice(1);
}

/**
 * Convert to slug
 */
export function slugify(str: string): string {
  return str
    .toLowerCase()
    .replace(/[^\w\s-]/g, '')
    .replace(/[\s_-]+/g, '-')
    .replace(/^-+|-+$/g, '');
}

/**
 * Generate random string
 */
export function randomString(length = 16): string {
  const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
  let result = '';
  for (let i = 0; i < length; i++) {
    result += chars.charAt(Math.floor(Math.random() * chars.length));
  }
  return result;
}
