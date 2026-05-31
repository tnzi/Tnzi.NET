import { describe, it, expect, beforeEach, vi } from 'vitest'
import { useTheme, createThemeContext } from '../../../src/composables/theme/useTheme'
import { defaultThemeSettings } from '../../../src/theme/settings'

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

  it('applyPreset loads a preset from JSON', async () => {
    const ctx = createThemeContext(defaultThemeSettings)
    const { settings, applyPreset } = useTheme(ctx)
    applyPreset({
      name: 'custom',
      colors: { primary: '#646cff', info: '#0284c7', success: '#22c55e', warning: '#f59e0b', error: '#ef4444' },
      mode: 'light',
      isInfoFollowPrimary: false,
      recommendColor: false,
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
})
