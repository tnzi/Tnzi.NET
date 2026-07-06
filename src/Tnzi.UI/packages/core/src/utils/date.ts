/**
 * @tnzi/core/utils/date
 *
 * Date formatting utilities.
 */

/**
 * Format date to ISO string
 */
export function formatDate(date: Date | string, format = 'yyyy-MM-dd'): string {
  const d = typeof date === 'string' ? new Date(date) : date;

  const year = d.getFullYear();
  const month = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  const hours = String(d.getHours()).padStart(2, '0');
  const minutes = String(d.getMinutes()).padStart(2, '0');
  const seconds = String(d.getSeconds()).padStart(2, '0');

  return format
    .replace('yyyy', String(year))
    .replace('MM', month)
    .replace('dd', day)
    .replace('HH', hours)
    .replace('mm', minutes)
    .replace('ss', seconds);
}

/**
 * Locale-aware date+time formatter (`toLocaleString`).
 *
 * Null/undefined/empty → `fallback` (default `''`). Invalid input falls back
 * to the original string when one was given. Use this for "human" timestamps
 * in tables/detail panes; use {@link formatDate} for fixed `yyyy-MM-dd` output.
 */
export function formatDateTime(
  value: string | number | Date | null | undefined,
  options?: { fallback?: string },
): string {
  if (value === null || value === undefined || value === '') return options?.fallback ?? '';
  // `new Date(invalid)` does not throw — it yields an Invalid Date whose
  // toLocaleString() is the literal "Invalid Date". Guard explicitly so
  // unparseable input falls back to the original string (or fallback).
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) {
    return typeof value === 'string' ? value : (options?.fallback ?? '');
  }
  return d.toLocaleString();
}

/**
 * Locale-aware date-only formatter (`toLocaleDateString`). Same null/fallback
 * semantics as {@link formatDateTime}.
 *
 * `utc: true` renders the UTC calendar date instead of the local one. Use it
 * for date-only fields the backend stores as UTC midnight (posting dates,
 * rate dates, period start/end): without it, any viewer west of UTC sees the
 * previous day. Leave it off for real timestamps (creation/modification
 * times), where the local calendar date is the correct rendering.
 */
export function formatDateOnly(
  value: string | number | Date | null | undefined,
  options?: { fallback?: string; utc?: boolean },
): string {
  if (value === null || value === undefined || value === '') return options?.fallback ?? '';
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) {
    return typeof value === 'string' ? value : (options?.fallback ?? '');
  }
  return d.toLocaleDateString(undefined, options?.utc ? { timeZone: 'UTC' } : undefined);
}

/**
 * Format relative time
 */
export function formatRelativeTime(date: Date | string): string {
  const d = typeof date === 'string' ? new Date(date) : date;
  const now = new Date();
  const diff = now.getTime() - d.getTime();

  const seconds = Math.floor(diff / 1000);
  const minutes = Math.floor(seconds / 60);
  const hours = Math.floor(minutes / 60);
  const days = Math.floor(hours / 24);

  if (seconds < 60) return 'just now';
  if (minutes < 60) return `${minutes}m ago`;
  if (hours < 24) return `${hours}h ago`;
  if (days < 7) return `${days}d ago`;
  return formatDate(d, 'yyyy-MM-dd');
}
