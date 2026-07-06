import { describe, it, expect } from 'vitest';
import { formatDateTime, formatDateOnly, formatDate } from '../../utils/date';

describe('formatDateTime', () => {
  const iso = '2026-05-29T08:30:00.000Z';

  it('returns localized date-time string for a valid value', () => {
    expect(formatDateTime(iso)).toBe(new Date(iso).toLocaleString());
  });

  it('accepts Date and number inputs', () => {
    const d = new Date(iso);
    expect(formatDateTime(d)).toBe(d.toLocaleString());
    expect(formatDateTime(d.getTime())).toBe(d.toLocaleString());
  });

  it('returns empty string for null / undefined / empty', () => {
    expect(formatDateTime(null)).toBe('');
    expect(formatDateTime(undefined)).toBe('');
    expect(formatDateTime('')).toBe('');
  });

  it('honors a custom fallback for nullish input', () => {
    expect(formatDateTime(null, { fallback: '—' })).toBe('—');
  });

  it('returns the original string for an unparseable date (not "Invalid Date")', () => {
    expect(formatDateTime('not-a-date')).toBe('not-a-date');
    expect(formatDateTime('not-a-date', { fallback: '—' })).toBe('not-a-date');
  });
});

describe('formatDateOnly', () => {
  const iso = '2026-05-29T08:30:00.000Z';

  it('returns localized date-only string for a valid value', () => {
    expect(formatDateOnly(iso)).toBe(new Date(iso).toLocaleDateString());
  });

  it('returns fallback for nullish input', () => {
    expect(formatDateOnly(null)).toBe('');
    expect(formatDateOnly(undefined, { fallback: 'n/a' })).toBe('n/a');
  });

  it('utc: true renders the UTC calendar date regardless of local timezone', () => {
    // A date-only value stored as UTC midnight must not shift a day for
    // viewers west of UTC.
    const utcMidnight = '2026-07-01T00:00:00Z';
    expect(formatDateOnly(utcMidnight, { utc: true })).toBe(
      new Date(utcMidnight).toLocaleDateString(undefined, { timeZone: 'UTC' }),
    );
    // Sanity: the rendered string contains the UTC day (1), whatever the locale.
    expect(formatDateOnly(utcMidnight, { utc: true })).toMatch(/1/);
  });

  it('utc option keeps null/invalid semantics', () => {
    expect(formatDateOnly(null, { utc: true, fallback: '—' })).toBe('—');
    expect(formatDateOnly('not-a-date', { utc: true })).toBe('not-a-date');
  });
});

describe('formatDate (fixed template)', () => {
  it('formats with the default yyyy-MM-dd pattern', () => {
    expect(formatDate('2026-05-29T08:30:00')).toBe('2026-05-29');
  });

  it('honors a custom format token set', () => {
    expect(formatDate('2026-05-29T08:30:05', 'yyyy/MM/dd HH:mm:ss')).toBe('2026/05/29 08:30:05');
  });
});
