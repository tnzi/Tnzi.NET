import { describe, it, expect } from 'vitest'
import {
  parseColor,
  perceivedBrightness,
  relativeLuminance,
  isDarkSurface,
  surfaceTone,
} from '../../src/theme/surfaceTone'

describe('surfaceTone - parseColor', () => {
  it('parses #rgb shorthand', () => {
    expect(parseColor('#fff')).toEqual({ r: 255, g: 255, b: 255 })
    expect(parseColor('#000')).toEqual({ r: 0, g: 0, b: 0 })
    expect(parseColor('#0af')).toEqual({ r: 0, g: 170, b: 255 })
  })

  it('parses #rrggbb (and ignores the alpha byte)', () => {
    expect(parseColor('#2080F0')).toEqual({ r: 32, g: 128, b: 240 })
    expect(parseColor('#2080F0ff')).toEqual({ r: 32, g: 128, b: 240 })
  })

  it('parses rgb() / rgba()', () => {
    expect(parseColor('rgb(10, 20, 30)')).toEqual({ r: 10, g: 20, b: 30 })
    expect(parseColor('rgba(10, 20, 30, 0.5)')).toEqual({ r: 10, g: 20, b: 30 })
    expect(parseColor('rgb(255 255 255 / 1)')).toEqual({ r: 255, g: 255, b: 255 })
  })

  it('returns null for unparseable input', () => {
    expect(parseColor('')).toBeNull()
    expect(parseColor('not-a-color')).toBeNull()
    expect(parseColor('#12')).toBeNull()
    // parseInt('1g', 16) would parse the leading digit - must reject whole.
    expect(parseColor('#1g0000')).toBeNull()
    expect(parseColor('#zzz')).toBeNull()
  })
})

describe('surfaceTone - luminance + tone', () => {
  it('relativeLuminance: 1 for white, 0 for black, mid for grey', () => {
    expect(relativeLuminance('#ffffff')).toBeCloseTo(1, 5)
    expect(relativeLuminance('#000000')).toBeCloseTo(0, 5)
    const grey = relativeLuminance('#808080')!
    expect(grey).toBeGreaterThan(0.2)
    expect(grey).toBeLessThan(0.3)
    expect(relativeLuminance('bad')).toBeNull()
  })

  it('perceivedBrightness (YIQ) still available as a utility', () => {
    expect(perceivedBrightness('#ffffff')).toBe(255)
    expect(perceivedBrightness('#000000')).toBe(0)
  })

  it('isDarkSurface: dark/saturated true, light/bright false, null-ish false', () => {
    expect(isDarkSurface('#0F172A')).toBe(true) // navy
    expect(isDarkSurface('#2080F0')).toBe(true) // medium blue → light-text convention
    expect(isDarkSurface('#3B0764')).toBe(true) // deep violet
    expect(isDarkSurface('#FFFFFF')).toBe(false)
    expect(isDarkSurface('#F5F6F8')).toBe(false)
    expect(isDarkSurface(null)).toBe(false)
    expect(isDarkSurface(undefined)).toBe(false)
    expect(isDarkSurface('garbage')).toBe(false)
  })

  it('surfaceTone: null when no color, else dark/light by luminance', () => {
    expect(surfaceTone(null)).toBeNull()
    expect(surfaceTone(undefined)).toBeNull()
    expect(surfaceTone('#0F172A')).toBe('dark')
    expect(surfaceTone('#334155')).toBe('dark') // dark slate
    expect(surfaceTone('#FFFFFF')).toBe('light')
    expect(surfaceTone('#E5E7EB')).toBe('light')
  })

  it('bright saturated colors get DARK text (the luminance improvement over YIQ)', () => {
    // These are bright enough that dark text reads better - YIQ mis-ranked
    // them as "dark surface" (white text); luminance puts them light-side.
    expect(surfaceTone('#14B8A6')).toBe('light') // teal
    expect(surfaceTone('#F97316')).toBe('light') // orange
    expect(surfaceTone('#EAB308')).toBe('light') // amber
    // ...while dark/saturated jewel tones stay dark (light text).
    expect(surfaceTone('#6366F1')).toBe('dark') // indigo
    expect(surfaceTone('#064E3B')).toBe('dark') // deep emerald
  })
})
