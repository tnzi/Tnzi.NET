import type { GlobalThemeOverrides } from 'naive-ui'
import type { ThemeSettings } from './types'

/**
 * Build a Naive UI GlobalThemeOverrides object from a Tnzi ThemeSettings.
 *
 * This is the bridge between Tnzi's palette-based theme system and Naive UI's
 * ConfigProvider. The generated overrides are passed to NConfigProvider via the
 * `theme-overrides` prop.
 *
 * Hover/pressed variants are computed from the 400/600 palette levels.
 * Consumer-provided `naiveOverrides` are shallow-merged at the top level so that
 * consumers can customize specific components (Menu, DataTable, etc.).
 */
export function buildNaiveThemeOverrides(settings: ThemeSettings): GlobalThemeOverrides {
  const { palettes, naiveOverrides = {} } = settings

  const base: GlobalThemeOverrides = {
    common: {
      primaryColor: palettes.primary[500],
      primaryColorHover: palettes.primary[400],
      primaryColorPressed: palettes.primary[600],
      primaryColorSuppl: palettes.primary[500],

      infoColor: palettes.info[500],
      infoColorHover: palettes.info[400],
      infoColorPressed: palettes.info[600],
      infoColorSuppl: palettes.info[500],

      successColor: palettes.success[500],
      successColorHover: palettes.success[400],
      successColorPressed: palettes.success[600],
      successColorSuppl: palettes.success[500],

      warningColor: palettes.warning[500],
      warningColorHover: palettes.warning[400],
      warningColorPressed: palettes.warning[600],
      warningColorSuppl: palettes.warning[500],

      errorColor: palettes.error[500],
      errorColorHover: palettes.error[400],
      errorColorPressed: palettes.error[600],
      errorColorSuppl: palettes.error[500],
    },
  }

  // Top-level spread lets consumers override any per-component section
  // (Menu, DataTable, ...). `common` is explicitly deep-merged so consumer
  // tweaks (e.g. primaryColorHover) win over our generated defaults without
  // wiping the rest of the base common section.
  return {
    ...naiveOverrides,
    common: {
      ...base.common,
      ...(naiveOverrides.common ?? {}),
    },
  }
}

/**
 * Resolve 'auto' mode to either 'light' or 'dark' based on system preference.
 *
 * Contract for `onChange`: it fires ONLY on subsequent system changes, never
 * on the initial resolution. Callers should seed their state from the returned
 * `resolved` value and use `onChange` purely for reactivity. This split keeps
 * the initial return synchronous and side-effect free.
 *
 * Returns the resolved mode and (when `onChange` is provided) a cleanup
 * function that removes the media query listener.
 */
export function resolveThemeMode(
  mode: 'light' | 'dark' | 'auto',
  onChange?: (resolved: 'light' | 'dark') => void,
): { resolved: 'light' | 'dark'; cleanup?: () => void } {
  if (mode !== 'auto') {
    return { resolved: mode }
  }
  if (typeof window === 'undefined') {
    return { resolved: 'light' } // SSR fallback
  }
  const mq = window.matchMedia('(prefers-color-scheme: dark)')
  const resolved = mq.matches ? 'dark' : 'light'
  if (onChange) {
    const handler = (e: MediaQueryListEvent) => onChange(e.matches ? 'dark' : 'light')
    mq.addEventListener('change', handler)
    return { resolved, cleanup: () => mq.removeEventListener('change', handler) }
  }
  return { resolved }
}
