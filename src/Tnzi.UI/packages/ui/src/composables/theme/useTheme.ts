import { ref, computed, watch, type Ref, type ComputedRef, type InjectionKey, inject, provide } from 'vue'
import type { ThemeSettings, ThemeColors } from '../../theme/types'
import { mergeThemeSettings } from '../../theme/settings'
import { buildCssVars, injectCssVars } from '../../theme/vars'
import { buildNaiveThemeOverrides, resolveThemeMode } from '../../theme/naive-bridge'
import type { GlobalThemeOverrides } from 'naive-ui'

export interface ThemeContext {
  settings: Ref<ThemeSettings>
  resolvedMode: ComputedRef<'light' | 'dark'>
  /** True when resolvedMode === 'dark'. Convenience for binding UI toggles. */
  isDark: ComputedRef<boolean>
  naiveOverrides: ComputedRef<GlobalThemeOverrides>
  setMode: (mode: 'light' | 'dark' | 'auto') => void
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

  const resolvedMode = computed<'light' | 'dark'>(() => {
    return resolveThemeMode(settings.value.mode).resolved
  })

  const naiveOverrides = computed<GlobalThemeOverrides>(() => {
    return buildNaiveThemeOverrides(settings.value)
  })

  function setMode(mode: 'light' | 'dark' | 'auto') {
    settings.value = { ...settings.value, mode }
  }

  function setColor(role: keyof ThemeColors, color: string) {
    const newColors = { ...settings.value.colors, [role]: color }
    settings.value = mergeThemeSettings({ colors: newColors as ThemeColors, mode: settings.value.mode })
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

  // Sync CSS variables and dark class to document when settings change
  if (typeof window !== 'undefined') {
    watch(
      [settings, resolvedMode],
      ([_, mode]) => {
        const vars = buildCssVars(settings.value.colors, mode)
        injectCssVars(vars)
        if (mode === 'dark') {
          document.documentElement.classList.add('dark')
        } else {
          document.documentElement.classList.remove('dark')
        }
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
