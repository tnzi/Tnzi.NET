import { describe, it, expect } from 'vitest';
import { formatNumber, formatCurrency, formatFileSize, clamp } from '../../src/utils/number';

describe('formatNumber', () => {
  it('inserts thousands separators for the default en-US locale', () => {
    expect(formatNumber(1234567.891)).toBe('1,234,567.891');
    expect(formatNumber(0)).toBe('0');
  });

  it('follows the requested locale', () => {
    expect(formatNumber(1234567.891, 'de-DE')).toBe('1.234.567,891');
  });
});

describe('formatCurrency', () => {
  it('formats with the currency symbol and two decimals by default', () => {
    expect(formatCurrency(1234.5)).toBe('$1,234.50');
    expect(formatCurrency(-3)).toBe('-$3.00');
  });

  it('follows the requested currency and locale', () => {
    // The space before the symbol is a non-breaking space in ICU output.
    expect(formatCurrency(1234.5, 'EUR', 'de-DE')).toMatch(/^1\.234,50\s€$/);
  });

  it('renders the fallback for null, undefined and NaN instead of "$NaN"', () => {
    expect(formatCurrency(null)).toBe('');
    expect(formatCurrency(undefined)).toBe('');
    expect(formatCurrency(Number.NaN)).toBe('');
    expect(formatCurrency(null, 'USD', 'en-US', { fallback: '-' })).toBe('-');
  });

  it('lets the caller override the fraction digits', () => {
    expect(
      formatCurrency(1.23456, 'USD', 'en-US', { minimumFractionDigits: 3, maximumFractionDigits: 3 })
    ).toBe('$1.235');
    expect(
      formatCurrency(1200, 'USD', 'en-US', { minimumFractionDigits: 0, maximumFractionDigits: 0 })
    ).toBe('$1,200');
  });
});

describe('formatFileSize', () => {
  it('shows whole bytes below 1 KiB', () => {
    expect(formatFileSize(0)).toBe('0 B');
    expect(formatFileSize(1023)).toBe('1023 B');
  });

  it('scales by 1024 with two decimals above that', () => {
    expect(formatFileSize(1024)).toBe('1.00 KB');
    expect(formatFileSize(1536)).toBe('1.50 KB');
    expect(formatFileSize(1024 * 1024)).toBe('1.00 MB');
    expect(formatFileSize(5 * 1024 ** 3)).toBe('5.00 GB');
    expect(formatFileSize(2 * 1024 ** 4)).toBe('2.00 TB');
  });

  it('stops at TB rather than inventing a larger unit', () => {
    expect(formatFileSize(1024 ** 5)).toBe('1024.00 TB');
  });
});

describe('clamp', () => {
  it('returns the value when it is inside the range', () => {
    expect(clamp(5, 0, 10)).toBe(5);
    expect(clamp(0, 0, 10)).toBe(0);
    expect(clamp(10, 0, 10)).toBe(10);
  });

  it('pins to the nearest bound outside the range', () => {
    expect(clamp(-1, 0, 10)).toBe(0);
    expect(clamp(11, 0, 10)).toBe(10);
  });
});
