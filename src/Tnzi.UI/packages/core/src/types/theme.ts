/**
 * Theme Types - Theme configuration and customization
 */

/**
 * Theme mode.
 *
 * `'auto'` means "follow the OS colour scheme". It used to be spelled
 * `'system'` here while every UI-side type (`@tnzi/ui`'s `useTheme`,
 * `TThemeSchemaSwitch`, `@tnzi/ui-admin`'s `AdminThemeSchema`) spelled it
 * `'auto'`, so the two halves of the ecosystem could not be assigned to each
 * other. One spelling now: `'auto'`.
 *
 * Values persisted by an older build still say `'system'`; read them back
 * through {@link normalizeThemeMode} rather than casting.
 */
export type ThemeMode = 'light' | 'dark' | 'auto';

/** Every valid {@link ThemeMode}, in the order `toggleTheme` cycles through. */
export const THEME_MODES: readonly ThemeMode[] = ['light', 'dark', 'auto'] as const;

/**
 * Coerce an untrusted value (persisted state, query string, backend payload)
 * into a valid {@link ThemeMode}.
 *
 * Maps the legacy `'system'` spelling onto `'auto'` so a browser that stored a
 * theme under an older build keeps its choice instead of silently falling back
 * to light. Anything unrecognised yields `fallback`.
 *
 * @param value - Untrusted input.
 * @param fallback - Returned when `value` is not a recognised mode. Defaults to `'light'`.
 */
export function normalizeThemeMode(value: unknown, fallback: ThemeMode = 'light'): ThemeMode {
  if (value === 'light' || value === 'dark' || value === 'auto') return value;
  // Legacy spelling written by builds before the 'system' → 'auto' unification.
  if (value === 'system') return 'auto';
  return fallback;
}
