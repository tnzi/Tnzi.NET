import type { GlobalThemeOverrides } from 'naive-ui'
import type { ThemeMode } from '@tnzi/core/types'
import type { ThemeSettings } from './types'
import { CONTRAST_TEXT_LIGHT, getContrastTextColor } from './palette'

/**
 * naive's own foreground on a primary FILL (a primary button, a checked box, a
 * checked tag, today's date, the active step) for a primary light enough that
 * white does not read on it.
 *
 * naive paints that foreground with `baseColor`: white in its light theme,
 * black in its dark one, whatever the primary is. Only the light-primary case
 * is overridden: for a dark primary the light theme's white is already what
 * `--tnzi-on-primary` says, and the dark theme's black is left as naive draws
 * it, so no existing product changes. For a light primary both modes get the
 * same ink, the one the package's own components put on the same fill.
 *
 * The keys are every `baseColor` / literal-white foreground naive draws on
 * `primaryColor` (naive-ui 2.45): Button (primary type, all states), Checkbox
 * check mark, checkable Tag, Calendar's current date, DatePicker's selected
 * item, Steps' active indicator, FloatButton (primary type). The primary used
 * as TEXT (Menu, Tabs, Pagination, text/ghost buttons) is not touched here.
 */
function onPrimaryFillOverrides(primary: string): GlobalThemeOverrides {
  const ink = getContrastTextColor(primary)
  if (ink === CONTRAST_TEXT_LIGHT) return {}
  return {
    Button: {
      textColorPrimary: ink,
      textColorHoverPrimary: ink,
      textColorPressedPrimary: ink,
      textColorFocusPrimary: ink,
      textColorDisabledPrimary: ink,
    },
    Checkbox: { checkMarkColor: ink },
    Tag: { textColorChecked: ink },
    Calendar: { dateTextColorCurrent: ink },
    DatePicker: { itemTextColorActive: ink },
    Steps: { indicatorTextColorProcess: ink },
    FloatButton: { textColorPrimary: ink },
  }
}

/**
 * Build a Naive UI GlobalThemeOverrides object from a Tnzi ThemeSettings.
 *
 * This is the bridge between Tnzi's palette-based theme system and Naive UI's
 * ConfigProvider. The generated overrides are passed to NConfigProvider via the
 * `theme-overrides` prop.
 *
 * Hover/pressed variants are computed from the 400/600 palette levels.
 * Consumer-provided `naiveOverrides` are merged section by section over the
 * generated ones (`common`, `Button`, ...): a consumer key wins, the generated
 * keys it does not name survive, and sections the bridge does not generate
 * (Menu, DataTable, ...) pass through as given.
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

      // soybean parity: Naive UI's default 3px radius reads as too sharp
      // next to soybean's 6px buttons / pagination items / inputs. The
      // cards still get 8px via per-component style - this base 6 only
      // affects buttons / inputs / pagination / popovers etc.
      borderRadius: '6px',
      borderRadiusSmall: '4px',
    },
  }

  // Every generated section is merged one level deep, so a consumer tweak
  // (e.g. `common.primaryColorHover`, `Button.heightMedium`) wins over the
  // generated value without wiping the rest of that section; sections the
  // bridge does not generate pass through untouched.
  const generated: Record<string, object> = { ...base, ...onPrimaryFillOverrides(palettes.primary[500]) }
  const consumer = naiveOverrides as Record<string, object | undefined>
  const merged: Record<string, object | undefined> = { ...consumer }
  for (const [section, values] of Object.entries(generated)) {
    merged[section] = { ...values, ...(consumer[section] ?? {}) }
  }
  return merged as GlobalThemeOverrides
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
  mode: ThemeMode,
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
  // `addEventListener` on MediaQueryList is missing from Safari before 14 and
  // from some test doubles; there the mode is still resolved, just not followed.
  if (onChange && typeof mq.addEventListener === 'function') {
    const handler = (e: MediaQueryListEvent) => onChange(e.matches ? 'dark' : 'light')
    mq.addEventListener('change', handler)
    return { resolved, cleanup: () => mq.removeEventListener('change', handler) }
  }
  return { resolved }
}
