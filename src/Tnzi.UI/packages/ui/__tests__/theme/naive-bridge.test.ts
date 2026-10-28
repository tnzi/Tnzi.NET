import { describe, it, expect, vi, afterEach } from 'vitest'
import type { GlobalThemeOverrides } from 'naive-ui'
import { buildNaiveThemeOverrides, resolveThemeMode } from '../../src/theme/naive-bridge'
import { CONTRAST_TEXT_DARK } from '../../src/theme/palette'
import { defaultThemeSettings, mergeThemeSettings } from '../../src/theme/settings'

describe('buildNaiveThemeOverrides', () => {
  it('generates a common section with primary color', () => {
    const overrides = buildNaiveThemeOverrides(defaultThemeSettings)
    expect(overrides.common?.primaryColor).toBe('#0d9488')
  })

  it('generates hover/pressed/suppl variants from palette', () => {
    const overrides = buildNaiveThemeOverrides(defaultThemeSettings)
    expect(overrides.common?.primaryColorHover).toBeDefined()
    expect(overrides.common?.primaryColorPressed).toBeDefined()
    expect(overrides.common?.primaryColorSuppl).toBeDefined()
    // Hover should be lighter than pressed
    expect(overrides.common?.primaryColorHover).not.toBe(overrides.common?.primaryColorPressed)
  })

  it('applies all 5 semantic color roles', () => {
    const overrides = buildNaiveThemeOverrides(defaultThemeSettings)
    expect(overrides.common?.primaryColor).toBe('#0d9488')
    expect(overrides.common?.infoColor).toBe('#2080f0')
    expect(overrides.common?.successColor).toBe('#18a058')
    expect(overrides.common?.warningColor).toBe('#f0a020')
    expect(overrides.common?.errorColor).toBe('#d03050')
  })

  it('merges naiveOverrides from settings', () => {
    const settings = {
      ...defaultThemeSettings,
      naiveOverrides: { Menu: { itemHeight: '50px' } },
    }
    const overrides = buildNaiveThemeOverrides(settings)
    expect((overrides as any).Menu?.itemHeight).toBe('50px')
  })

  it('consumer common keys override generated base', () => {
    const settings = {
      ...defaultThemeSettings,
      naiveOverrides: { common: { primaryColorHover: '#ff00ff' } },
    }
    const overrides = buildNaiveThemeOverrides(settings)
    expect(overrides.common?.primaryColorHover).toBe('#ff00ff')
    // Non-overridden base keys must survive the merge
    expect(overrides.common?.primaryColor).toBe('#0d9488')
    expect(overrides.common?.errorColor).toBe('#d03050')
  })

  it('is pure - does not mutate input settings', () => {
    const naiveOverrides = { common: { primaryColorHover: '#000000' } }
    const settings = { ...defaultThemeSettings, naiveOverrides }
    const snapshot = JSON.stringify(naiveOverrides)
    buildNaiveThemeOverrides(settings)
    expect(JSON.stringify(naiveOverrides)).toBe(snapshot)
  })

  describe('foreground on a primary fill', () => {
    const withPrimary = (primary: string, naiveOverrides?: GlobalThemeOverrides) =>
      buildNaiveThemeOverrides(mergeThemeSettings({ colors: { primary }, naiveOverrides }))

    it('paints the ink on every primary fill naive draws when the primary is light', () => {
      const overrides = withPrimary('#FFD217')
      expect(overrides.Button).toMatchObject({
        textColorPrimary: CONTRAST_TEXT_DARK,
        textColorHoverPrimary: CONTRAST_TEXT_DARK,
        textColorPressedPrimary: CONTRAST_TEXT_DARK,
        textColorFocusPrimary: CONTRAST_TEXT_DARK,
        textColorDisabledPrimary: CONTRAST_TEXT_DARK,
      })
      expect(overrides.Checkbox?.checkMarkColor).toBe(CONTRAST_TEXT_DARK)
      expect(overrides.Tag?.textColorChecked).toBe(CONTRAST_TEXT_DARK)
      expect(overrides.Calendar?.dateTextColorCurrent).toBe(CONTRAST_TEXT_DARK)
      expect(overrides.DatePicker?.itemTextColorActive).toBe(CONTRAST_TEXT_DARK)
      expect(overrides.Steps?.indicatorTextColorProcess).toBe(CONTRAST_TEXT_DARK)
      expect(overrides.FloatButton?.textColorPrimary).toBe(CONTRAST_TEXT_DARK)
    })

    // naive's own choice (white in light mode, black in dark mode) stays for
    // every primary white reads on, the default teal included.
    it.each(['#0d9488', '#2563eb'])('leaves naive alone when white reads on the primary (%s)', (primary) => {
      const overrides = withPrimary(primary)
      expect(Object.keys(overrides)).toEqual(['common'])
    })

    it('lets a consumer key win inside a generated section and keeps the rest', () => {
      const overrides = withPrimary('#FFD217', {
        Button: { textColorPrimary: '#000000', heightMedium: '40px' },
        Menu: { itemHeight: '50px' },
      })
      expect(overrides.Button?.textColorPrimary).toBe('#000000')
      expect(overrides.Button?.heightMedium).toBe('40px')
      expect(overrides.Button?.textColorHoverPrimary).toBe(CONTRAST_TEXT_DARK)
      expect(overrides.Menu?.itemHeight).toBe('50px')
    })

    it('passes a consumer section through unchanged when the primary is dark', () => {
      const overrides = withPrimary('#2563eb', { Button: { heightMedium: '40px' } })
      expect(overrides.Button).toEqual({ heightMedium: '40px' })
    })
  })
})

describe('resolveThemeMode', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('returns mode unchanged for explicit light', () => {
    expect(resolveThemeMode('light')).toEqual({ resolved: 'light' })
  })

  it('returns mode unchanged for explicit dark', () => {
    expect(resolveThemeMode('dark')).toEqual({ resolved: 'dark' })
  })

  it('falls back to light in SSR (no window)', () => {
    vi.stubGlobal('window', undefined)
    expect(resolveThemeMode('auto')).toEqual({ resolved: 'light' })
    // (vi.unstubAllGlobals in afterEach already handles cleanup)
  })

  it('resolves auto via matchMedia and attaches listener with cleanup', () => {
    const removeListener = vi.fn()
    const addListener = vi.fn()
    const mockWindow = {
      matchMedia: vi.fn().mockReturnValue({
        matches: true,
        addEventListener: addListener,
        removeEventListener: removeListener,
      }),
    }
    vi.stubGlobal('window', mockWindow)

    const onChange = vi.fn()
    const result = resolveThemeMode('auto', onChange)

    expect(result.resolved).toBe('dark')
    expect(mockWindow.matchMedia).toHaveBeenCalledWith('(prefers-color-scheme: dark)')
    expect(addListener).toHaveBeenCalledTimes(1)
    expect(onChange).not.toHaveBeenCalled() // per contract - only on change, not initial

    result.cleanup?.()
    expect(removeListener).toHaveBeenCalledTimes(1)
    // Same handler identity for add + remove
    expect(removeListener.mock.calls[0][1]).toBe(addListener.mock.calls[0][1])
  })

  it('auto without onChange returns resolved without cleanup', () => {
    vi.stubGlobal('window', {
      matchMedia: vi.fn().mockReturnValue({
        matches: false,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      }),
    })
    const result = resolveThemeMode('auto')
    expect(result).toEqual({ resolved: 'light' })
    expect(result.cleanup).toBeUndefined()
  })

  // Safari before 14 only has the deprecated addListener; resolving must not
  // throw there (the theme context subscribes on every 'auto').
  it('auto with onChange still resolves when the MediaQueryList has no addEventListener', () => {
    vi.stubGlobal('window', {
      matchMedia: vi.fn().mockReturnValue({ matches: true, addListener: vi.fn() }),
    })
    const result = resolveThemeMode('auto', vi.fn())
    expect(result).toEqual({ resolved: 'dark' })
  })
})
