import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { effectScope, nextTick } from 'vue'
import { useTheme, createThemeContext } from '../../../src/headless/theme/useTheme'
import { defaultThemeSettings, mergeThemeSettings } from '../../../src/theme/settings'

describe('useTheme', () => {
  beforeEach(() => {
    if (typeof document !== 'undefined') {
      document.documentElement.classList.remove('dark')
      document.documentElement.removeAttribute('style')
    }
  })

  it('returns the current theme settings reactive ref', () => {
    const ctx = createThemeContext(defaultThemeSettings)
    const { settings } = useTheme(ctx)
    expect(settings.value.colors.primary).toBe('#0d9488')
  })

  it('setMode updates the mode reactively', () => {
    const ctx = createThemeContext(defaultThemeSettings)
    const { settings, setMode } = useTheme(ctx)
    expect(settings.value.mode).toBe('light')
    setMode('dark')
    expect(settings.value.mode).toBe('dark')
  })

  it('setColor updates base color and regenerates palette', () => {
    const ctx = createThemeContext(defaultThemeSettings)
    const { settings, setColor } = useTheme(ctx)
    setColor('primary', '#ff0000')
    expect(settings.value.colors.primary).toBe('#ff0000')
    expect(settings.value.palettes.primary[500]).toBe('#ff0000')
  })

  // Changing one colour is not a reset: a consumer's raw naive overrides and
  // the rest of its settings must survive it.
  it('setColor keeps the other settings', () => {
    const naiveOverrides = { common: { borderRadius: '10px' } }
    const ctx = createThemeContext(
      mergeThemeSettings({ naiveOverrides, presetName: 'azir', mode: 'dark' }),
    )
    ctx.setColor('primary', '#ff0000')
    expect(ctx.settings.value.naiveOverrides).toEqual(naiveOverrides)
    expect(ctx.settings.value.presetName).toBe('azir')
    expect(ctx.settings.value.mode).toBe('dark')
    expect(ctx.settings.value.colors.info).toBe(defaultThemeSettings.colors.info)
  })

  // A brand colour picked at runtime (an account preference) must carry its
  // readable foreground and text colour with it, without a reload.
  it('setColor recomputes the foreground and text companions of the primary', async () => {
    const ctx = createThemeContext(defaultThemeSettings)
    await nextTick()
    const root = document.documentElement.style
    expect(root.getPropertyValue('--tnzi-on-primary')).toBe('#ffffff')
    expect(ctx.naiveOverrides.value.Button).toBeUndefined()

    ctx.setColor('primary', '#FFD217')
    await nextTick()
    expect(root.getPropertyValue('--tnzi-on-primary')).toBe('#141414')
    expect(root.getPropertyValue('--tnzi-primary-text-on-light')).toBe('#816800')
    expect(ctx.naiveOverrides.value.Button?.textColorPrimary).toBe('#141414')

    ctx.setColor('primary', '#2563EB')
    await nextTick()
    expect(root.getPropertyValue('--tnzi-on-primary')).toBe('#ffffff')
    expect(ctx.naiveOverrides.value.Button).toBeUndefined()
  })

  it('applyPreset loads a preset from JSON', async () => {
    const ctx = createThemeContext(defaultThemeSettings)
    const { settings, applyPreset } = useTheme(ctx)
    applyPreset({
      name: 'custom',
      colors: { primary: '#646cff', info: '#0284c7', success: '#22c55e', warning: '#f59e0b', error: '#ef4444' },
      mode: 'light',
    })
    expect(settings.value.colors.primary).toBe('#646cff')
    expect(settings.value.presetName).toBe('custom')
  })

  it('resolvedMode computes "auto" correctly based on matchMedia', () => {
    const originalMM = window.matchMedia
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: vi.fn().mockReturnValue({
        matches: true,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      }),
    })
    try {
      const ctx = createThemeContext({ ...defaultThemeSettings, mode: 'auto' })
      const { resolvedMode } = useTheme(ctx)
      expect(resolvedMode.value).toBe('dark')
    } finally {
      Object.defineProperty(window, 'matchMedia', { configurable: true, value: originalMM })
    }
  })

  // 'auto' follows the OS colour scheme for the life of the page, not only at
  // the moment it was chosen.
  describe('auto mode follows OS colour-scheme changes', () => {
    let listeners: Set<(e: { matches: boolean }) => void>
    let osDark: boolean
    const originalMM = window.matchMedia

    function switchOs(dark: boolean) {
      osDark = dark
      for (const listener of [...listeners]) listener({ matches: dark })
    }

    beforeEach(() => {
      listeners = new Set()
      osDark = false
      Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: vi.fn(() => ({
          get matches() {
            return osDark
          },
          addEventListener: (_: string, fn: (e: { matches: boolean }) => void) => listeners.add(fn),
          removeEventListener: (_: string, fn: (e: { matches: boolean }) => void) => listeners.delete(fn),
        })),
      })
    })

    afterEach(() => {
      Object.defineProperty(window, 'matchMedia', { configurable: true, value: originalMM })
    })

    it('re-resolves and repaints the document when the OS switches', async () => {
      const ctx = createThemeContext({ ...defaultThemeSettings, mode: 'auto' })
      expect(ctx.resolvedMode.value).toBe('light')

      switchOs(true)
      expect(ctx.resolvedMode.value).toBe('dark')
      expect(ctx.isDark.value).toBe(true)
      await nextTick()
      expect(document.documentElement.classList.contains('dark')).toBe(true)

      switchOs(false)
      expect(ctx.resolvedMode.value).toBe('light')
      await nextTick()
      expect(document.documentElement.classList.contains('dark')).toBe(false)
    })

    // A context created inside a component or effect scope must not leave its
    // OS listener behind when that scope ends.
    it('drops the OS listener when the scope that created the context ends', () => {
      const scope = effectScope()
      scope.run(() => createThemeContext({ ...defaultThemeSettings, mode: 'auto' }))
      expect(listeners.size).toBe(1)

      scope.stop()
      expect(listeners.size).toBe(0)
    })

    it('stops listening once an explicit mode is chosen, and picks the OS up again on auto', async () => {
      const ctx = createThemeContext({ ...defaultThemeSettings, mode: 'auto' })
      ctx.setMode('light')
      await nextTick()
      expect(listeners.size).toBe(0)

      switchOs(true)
      expect(ctx.resolvedMode.value).toBe('light')

      ctx.setMode('auto')
      await nextTick()
      expect(ctx.resolvedMode.value).toBe('dark')
      switchOs(false)
      expect(ctx.resolvedMode.value).toBe('light')
    })
  })
})
