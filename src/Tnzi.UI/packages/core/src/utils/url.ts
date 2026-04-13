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
