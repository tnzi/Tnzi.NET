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
});

describe('formatDate (fixed template)', () => {
  it('formats with the default yyyy-MM-dd pattern', () => {
    expect(formatDate('2026-05-29T08:30:00')).toBe('2026-05-29');
  });

  it('honors a custom format token set', () => {
    expect(formatDate('2026-05-29T08:30:05', 'yyyy/MM/dd HH:mm:ss')).toBe('2026/05/29 08:30:05');
  });
});
