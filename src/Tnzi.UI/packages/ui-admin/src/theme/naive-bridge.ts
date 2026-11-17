import { computed, inject, isRef, type ComputedRef, type InjectionKey, type Ref } from 'vue'
import type { GlobalThemeOverrides } from 'naive-ui'
import { CONTRAST_TEXT_LIGHT, getContrastTextColor, type ThemeSettings } from '@tnzi/ui/theme'
import type { ThemeContext } from '@tnzi/ui/headless'

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
 * The consumer's raw `overrides` are merged section by section over the
 * generated ones (`common`, `Button`, ...): a consumer key wins, the generated
 * keys it does not name survive, and sections the bridge does not generate
 * (Menu, DataTable, ...) pass through as given.
 */
export function buildNaiveThemeOverrides(
  settings: ThemeSettings,
  overrides: GlobalThemeOverrides = {},
): GlobalThemeOverrides {
  const { palettes } = settings

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
  const consumer = overrides as Record<string, object | undefined>
  const merged: Record<string, object | undefined> = { ...consumer }
  for (const [section, values] of Object.entries(generated)) {
    merged[section] = { ...values, ...(consumer[section] ?? {}) }
  }
  return merged as GlobalThemeOverrides
}

/** What a function given as `naiveOverrides` is called with. */
export interface NaiveOverridesContext {
  /** The mode the theme context resolves to now (`auto` already followed). */
  readonly isDark: boolean
  /** The theme context's settings (palettes, colours, mode); `null` without a context. */
  readonly settings: ThemeSettings | null
}

/**
 * The consumer's raw naive overrides, as `createTnziUiAdmin({ naiveOverrides })`
 * (or `defineAdminApp({ pluginOptions: { naiveOverrides } })`) takes them:
 *
 * - an object: the same overrides in both modes;
 * - a `Ref` (or `computed`): whatever it holds now;
 * - a function of {@link NaiveOverridesContext}: called again whenever the mode
 *   or the theme settings change, and whenever a ref it reads changes. An
 *   application with one set of overrides for light and one for dark writes
 *   `({ isDark }) => (isDark ? DARK : LIGHT)`.
 *
 * Whichever it is, the shell merges the result over the overrides it generates
 * from the palette, and every naive component under `TAdminAppRoot` follows a
 * switch of mode without the application passing anything to the root.
 */
export type NaiveOverridesSource =
  | GlobalThemeOverrides
  | Ref<GlobalThemeOverrides>
  | ((context: NaiveOverridesContext) => GlobalThemeOverrides)

/**
 * The raw overrides a source gives for a context. Reading it inside a
 * `computed` tracks what the source reads (a ref's value, the refs a function
 * reads), so the result follows them.
 */
export function resolveNaiveOverrides(
  source: NaiveOverridesSource | undefined,
  context: NaiveOverridesContext,
): GlobalThemeOverrides {
  if (!source) return {}
  if (typeof source === 'function') return source(context) ?? {}
  if (isRef(source)) return source.value ?? {}
  return source
}

/**
 * The consumer's naive overrides source (see {@link NaiveOverridesSource}). The
 * theme context of `@tnzi/ui` knows nothing about a component library, so the
 * source is kept here, next to the bridge that merges it.
 */
export const ADMIN_NAIVE_OVERRIDES_KEY: InjectionKey<NaiveOverridesSource> = Symbol('tnzi-admin-naive-overrides')

/**
 * The naive overrides for a theme context: the generated palette tokens with
 * the consumer's raw overrides for the current mode merged over them,
 * following every change of the context's mode and settings (and of a ref the
 * source reads). Without a context only the raw overrides apply, as for the
 * light mode. Call it inside a component's setup (it injects the source).
 */
export function useNaiveThemeOverrides(
  themeContext: ThemeContext | null | undefined,
): ComputedRef<GlobalThemeOverrides> {
  const source = inject(ADMIN_NAIVE_OVERRIDES_KEY, undefined)
  return computed(() => {
    const settings = themeContext ? themeContext.settings.value : null
    const raw = resolveNaiveOverrides(source, {
      isDark: themeContext ? themeContext.isDark.value : false,
      settings,
    })
    return settings ? buildNaiveThemeOverrides(settings, raw) : raw
  })
}

/**
 * Compact density: smaller type and control heights. Pass it as
 * `createTnziUiAdmin({ naiveOverrides: COMPACT_NAIVE_OVERRIDES })`.
 */
export const COMPACT_NAIVE_OVERRIDES: GlobalThemeOverrides = {
  common: {
    fontSize: '13px',
    fontSizeSmall: '12px',
    fontSizeMedium: '13px',
    fontSizeLarge: '14px',
    heightSmall: '26px',
    heightMedium: '30px',
    heightLarge: '36px',
  },
}
