import { ref, computed, watch, type Ref, type ComputedRef, type InjectionKey, inject, provide } from 'vue'
import type { ThemeSettings, ThemeColors } from '../../theme/types'
import { mergeThemeSettings } from '../../theme/settings'
import { buildCssVars, injectCssVars } from '../../theme/vars'
import { buildNaiveThemeOverrides, resolveThemeMode } from '../../theme/naive-bridge'
import { applyThemeModeToDocument } from '../../theme/document'
import type { GlobalThemeOverrides } from 'naive-ui'
import type { ThemeMode } from '@tnzi/core/types'

export interface ThemeContext {
  settings: Ref<ThemeSettings>
  resolvedMode: ComputedRef<'light' | 'dark'>
  /** True when resolvedMode === 'dark'. Convenience for binding UI toggles. */
  isDark: ComputedRef<boolean>
  naiveOverrides: ComputedRef<GlobalThemeOverrides>
  setMode: (mode: ThemeMode) => void
  setColor: (role: keyof ThemeColors, color: string) => void
  applyPreset: (preset: Partial<ThemeSettings> & { name: string }) => void
  reset: () => void
  /** Toggle between light and dark mode. */
  toggleTheme: () => void
}

export const THEME_CONTEXT_KEY: InjectionKey<ThemeContext> = Symbol('tnzi-theme')

/**
 * Create a fresh theme context. Used by `createTnziUi()` plugin at install time
 * and by tests that need an isolated context.
 */
export function createThemeContext(initial: ThemeSettings): ThemeContext {
  const settings = ref<ThemeSettings>({ ...initial })

  // The OS colour scheme, kept live while the mode is 'auto'. A computed over
  // `matchMedia(...).matches` alone has no reactive dependency on the OS, so
  // 'auto' would only be resolved at the moment it was chosen. The OS listener
  // goes through `onCleanup`: it is removed when the mode changes and when the
  // scope that created this context (a component, an effect scope) ends.
  const systemMode = ref<'light' | 'dark'>('light')
  watch(
    () => settings.value.mode,
    (mode, _previous, onCleanup) => {
      if (mode !== 'auto') return
      const { resolved, cleanup } = resolveThemeMode('auto', (next) => {
        systemMode.value = next
      })
      systemMode.value = resolved
      if (cleanup) onCleanup(cleanup)
    },
    { immediate: true, flush: 'sync' },
  )

  const resolvedMode = computed<'light' | 'dark'>(() => {
    const mode = settings.value.mode
    return mode === 'auto' ? systemMode.value : mode
  })

  const naiveOverrides = computed<GlobalThemeOverrides>(() => {
    return buildNaiveThemeOverrides(settings.value)
  })

  function setMode(mode: ThemeMode) {
    settings.value = { ...settings.value, mode }
  }

  // Rebuilt through `mergeThemeSettings` so the palettes follow the new colour,
  // but from the CURRENT settings: passing only colours and mode reset the
  // consumer's raw naive overrides, preset name and flags to the defaults.
  function setColor(role: keyof ThemeColors, color: string) {
    const { palettes: _palettes, ...current } = settings.value
    settings.value = mergeThemeSettings({ ...current, colors: { ...current.colors, [role]: color } })
  }

  function applyPreset(preset: Partial<ThemeSettings> & { name: string }) {
    settings.value = {
      ...mergeThemeSettings(preset),
      presetName: preset.name,
    }
  }

  function reset() {
    settings.value = { ...initial }
  }

  const isDark = computed(() => resolvedMode.value === 'dark')

  function toggleTheme() {
    setMode(resolvedMode.value === 'dark' ? 'light' : 'dark')
  }

  // Sync CSS variables and dark class to document when settings change.
  // The dark class goes through `applyThemeModeToDocument` rather than a local
  // classList call so this package has exactly one writer of that class.
  if (typeof window !== 'undefined') {
    watch(
      [settings, resolvedMode],
      ([_, mode]) => {
        const vars = buildCssVars(settings.value.colors, mode)
        injectCssVars(vars)
        applyThemeModeToDocument(mode)
      },
      { immediate: true, deep: true },
    )
  }

  return {
    settings,
    resolvedMode,
    isDark,
    naiveOverrides,
    setMode,
    setColor,
    applyPreset,
    reset,
    toggleTheme,
  }
}

/**
 * Consume the theme context. Must be called inside a component whose ancestor
 * has provided a ThemeContext (typically via `createTnziUi()` plugin).
 *
 * For tests and standalone usage, pass an explicit context from `createThemeContext()`.
 */
export function useTheme(context?: ThemeContext): ThemeContext {
  if (context) return context
  const injected = inject(THEME_CONTEXT_KEY)
  if (!injected) {
    throw new Error(
      'useTheme: no theme context found. Did you install the @tnzi/ui plugin via createTnziUi()?',
    )
  }
  return injected
}

/**
 * Provide a theme context to descendant components.
 * Used by `createTnziUi()` internally; exported for advanced use (e.g. nested sub-apps).
 */
export function provideTheme(context: ThemeContext) {
  provide(THEME_CONTEXT_KEY, context)
}
