/**
 * @tnzi/core/utils/url
 *
 * URL manipulation utilities.
 */

/**
 * Build URL with query params
 */
export function buildUrl(base: string, params?: Record<string, unknown>): string {
  if (!params || Object.keys(params).length === 0) {
    return base;
  }

  const searchParams = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null) {
      searchParams.append(key, String(value));
    }
  }

  const separator = base.includes('?') ? '&' : '?';
  return `${base}${separator}${searchParams.toString()}`;
}

/**
 * Parse query params
 */
export function parseQuery(queryString: string): Record<string, string> {
  const params = new URLSearchParams(queryString);
  const result: Record<string, string> = {};
  for (const [key, value] of params) {
    result[key] = value;
  }
  return result;
}

/** Schemes `safeLinkHref` lets through. A URL without a scheme (relative) also passes. */
const SAFE_LINK_SCHEMES: readonly string[] = ['http', 'https', 'mailto', 'tel', 'blob'];

/**
 * The URL itself when it is safe to bind to `<a :href>`, otherwise `undefined`.
 *
 * Vue does not sanitize `:href`, so a `javascript:` / `vbscript:` /
 * `data:text/html` URL from data (an attachment, a citation, a search hit)
 * would run on click. This is an allow-list: http(s), mailto:, tel:, blob:,
 * `data:` images / audio / video and relative URLs pass; every other scheme
 * is refused, as is an empty value.
 *
 * The scheme is read the way the browser's URL parser reads it, after dropping
 * leading control characters and spaces and every tab / newline: a pattern
 * match on the raw string lets `\u0001javascript:` or `java\tscript:` through.
 */
export function safeLinkHref(url: string | null | undefined): string | undefined {
  if (!url) return undefined;
  const stripped = url.replace(/[\t\n\r]/g, '');
  let start = 0;
  while (start < stripped.length && stripped.charCodeAt(start) <= 0x20) start++;
  const normalized = stripped.slice(start);
  const scheme = /^([a-z][a-z0-9+.-]*):/i.exec(normalized)?.[1]?.toLowerCase();
  if (scheme === undefined || SAFE_LINK_SCHEMES.includes(scheme)) return url;
  if (scheme === 'data' && /^data:(image|audio|video)\//i.test(normalized)) return url;
  return undefined;
}
