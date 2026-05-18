import { describe, it, expect } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import {
  themePresets,
  defaultPreset,
  darkPreset,
  compactPreset,
  azirPreset,
  exportThemeConfig,
  importThemeConfig,
} from '../../src/presets'
import { useAdminThemeStore } from '../../src/stores/useAdminThemeStore'

describe('themePresets', () => {
  it('ships exactly four built-in presets', () => {
    expect(themePresets).toHaveLength(4)
    expect(themePresets.map((p) => p.name)).toEqual([
      'default',
      'dark',
      'compact',
      'azir',
    ])
  })

  it('default preset uses #646cff primary, 6 px radius, light scheme', () => {
    expect(defaultPreset.primaryColor).toBe('#646cff')
    expect(defaultPreset.themeRadius).toBe(6)
    expect(defaultPreset.themeScheme).toBe('light')
  })

  it('dark preset flips scheme but keeps default palette', () => {
    expect(darkPreset.themeScheme).toBe('dark')
    expect(darkPreset.primaryColor).toBe(defaultPreset.primaryColor)
  })

  it('compact preset uses tighter heights', () => {
    expect(compactPreset.layout.headerHeight).toBeLessThan(
      defaultPreset.layout.headerHeight,
    )
    expect(compactPreset.layout.tabHeight).toBeLessThan(
      defaultPreset.layout.tabHeight,
    )
  })

  it('azir preset uses a different brand color', () => {
    expect(azirPreset.primaryColor).not.toBe(defaultPreset.primaryColor)
    expect(azirPreset.primaryColor).toBe('#0ea5e9')
  })
})

describe('exportThemeConfig / importThemeConfig', () => {
  it('roundtrips a settings snapshot', () => {
    const snapshot = { primary: '#646cff', siderWidth: 220 }
    const json = exportThemeConfig(snapshot)
    const parsed = importThemeConfig(json)
    expect(parsed).toEqual(snapshot)
  })

  it('export adds a version stamp', () => {
    const json = exportThemeConfig({})
    const obj = JSON.parse(json) as { version: number; settings: unknown }
    expect(obj.version).toBe(1)
    expect(obj.settings).toEqual({})
  })

  it('import throws on unknown version', () => {
    const bad = JSON.stringify({ version: 99, settings: {} })
    expect(() => importThemeConfig(bad)).toThrow(/Unsupported theme config version/)
  })

  it('import throws on missing settings object', () => {
    const bad = JSON.stringify({ version: 1 })
    expect(() => importThemeConfig(bad)).toThrow(/missing `settings` object/)
  })

  it('import throws on non-object payload', () => {
    expect(() => importThemeConfig('"a string"')).toThrow(/not a JSON object/)
  })
})

describe('useAdminThemeStore.applyPreset', () => {
  it('applies radius / layout / pageTransition from a preset', () => {
    setActivePinia(createPinia())
    const store = useAdminThemeStore()
    // Bump radius up first so applyPreset(compactPreset) → 4 is observably
    // a change (the default radius itself is now 4, matching compact).
    store.setThemeRadius(12)
    const initial = {
      themeRadius: store.themeRadius,
      siderWidth: store.siderWidth,
      headerHeight: store.headerHeight,
      pageTransition: store.pageTransition,
    }
    store.applyPreset(compactPreset)
    expect(store.themeRadius).toBe(compactPreset.themeRadius)
    expect(store.siderWidth).toBe(compactPreset.layout.siderWidth)
    expect(store.headerHeight).toBe(compactPreset.layout.headerHeight)
    expect(store.pageTransition).toBe(compactPreset.pageTransition)
    expect(store.themeRadius).not.toBe(initial.themeRadius)
  })

  it('setThemeRadius clamps to 0-16', () => {
    setActivePinia(createPinia())
    const store = useAdminThemeStore()
    store.setThemeRadius(-5)
    expect(store.themeRadius).toBe(0)
    store.setThemeRadius(99)
    expect(store.themeRadius).toBe(16)
    store.setThemeRadius(8.6)
    expect(store.themeRadius).toBe(9)
  })
})
