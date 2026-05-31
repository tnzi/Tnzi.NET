import { describe, it, expect } from 'vitest'
import { defaultThemeSettings, mergeThemeSettings } from '../../src/theme/settings'

describe('defaultThemeSettings', () => {
  it('has 5 color roles', () => {
    expect(Object.keys(defaultThemeSettings.colors)).toHaveLength(5)
    expect(defaultThemeSettings.colors.primary).toBe('#0d9488')
  })

  it('has palettes generated for all roles', () => {
    expect(defaultThemeSettings.palettes.primary[500]).toBe('#0d9488')
    expect(defaultThemeSettings.palettes.primary[50]).toMatch(/^#[0-9a-f]{6}$/i)
    expect(defaultThemeSettings.palettes.info[500]).toBe('#2080f0')
  })

  it('defaults to light mode', () => {
    expect(defaultThemeSettings.mode).toBe('light')
  })
})

describe('mergeThemeSettings', () => {
  it('merges a partial color override', () => {
    const merged = mergeThemeSettings({ colors: { primary: '#ff0000' } })
    expect(merged.colors.primary).toBe('#ff0000')
    expect(merged.colors.info).toBe('#2080f0')
  })

  it('regenerates palettes when colors change', () => {
    const merged = mergeThemeSettings({ colors: { primary: '#ff0000' } })
    expect(merged.palettes.primary[500]).toBe('#ff0000')
    expect(merged.palettes.primary[100]).not.toBe(defaultThemeSettings.palettes.primary[100])
  })

  it('preserves mode override', () => {
    const merged = mergeThemeSettings({ mode: 'dark' })
    expect(merged.mode).toBe('dark')
  })
})
