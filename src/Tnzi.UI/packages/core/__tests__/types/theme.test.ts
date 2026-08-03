import { describe, it, expect } from 'vitest';
import { normalizeThemeMode, THEME_MODES } from '../../src/types/theme';

describe('THEME_MODES', () => {
  it('lists exactly the three valid modes in cycle order', () => {
    expect(THEME_MODES).toEqual(['light', 'dark', 'auto']);
  });
});

describe('normalizeThemeMode', () => {
  it('passes valid modes through unchanged', () => {
    expect(normalizeThemeMode('light')).toBe('light');
    expect(normalizeThemeMode('dark')).toBe('dark');
    expect(normalizeThemeMode('auto')).toBe('auto');
  });

  // This is the whole reason the function exists. Deleting the legacy branch
  // would strand every browser that stored a theme under a pre-unification
  // build in light mode, with no error anywhere.
  it("maps the legacy 'system' spelling onto 'auto'", () => {
    expect(normalizeThemeMode('system')).toBe('auto');
  });

  it('returns the fallback for unrecognised values', () => {
    expect(normalizeThemeMode('purple')).toBe('light');
    expect(normalizeThemeMode('')).toBe('light');
    expect(normalizeThemeMode(null)).toBe('light');
    expect(normalizeThemeMode(undefined)).toBe('light');
    expect(normalizeThemeMode(42)).toBe('light');
    expect(normalizeThemeMode({ mode: 'dark' })).toBe('light');
  });

  it('honours an explicit fallback', () => {
    expect(normalizeThemeMode('nonsense', 'auto')).toBe('auto');
    expect(normalizeThemeMode(null, 'dark')).toBe('dark');
  });

  it('prefers the legacy mapping over the fallback', () => {
    expect(normalizeThemeMode('system', 'dark')).toBe('auto');
  });
});
