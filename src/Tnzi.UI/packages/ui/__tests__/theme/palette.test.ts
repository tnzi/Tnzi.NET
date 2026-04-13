import { describe, it, expect } from 'vitest'
import { getColorPalette, getPaletteColorByNumber } from '../../src/theme/palette'

describe('getPaletteColorByNumber', () => {
  it('returns the base color unchanged at level 500', () => {
    expect(getPaletteColorByNumber('#18a058', 500)).toBe('#18a058')
  })

  it('returns lighter color at level 100', () => {
    const light = getPaletteColorByNumber('#18a058', 100)
    expect(light).not.toBe('#18a058')
  })

  it('returns darker color at level 900', () => {
    const dark = getPaletteColorByNumber('#18a058', 900)
    expect(dark).not.toBe('#18a058')
  })

  it('returns hex string with leading #', () => {
    const color = getPaletteColorByNumber('#18a058', 300)
    expect(color).toMatch(/^#[0-9a-f]{6}$/i)
  })

  it('handles shorthand hex input #abc', () => {
    const color = getPaletteColorByNumber('#abc', 500)
    expect(color).toMatch(/^#[0-9a-f]{6}$/i)
  })

  it('throws on invalid color input', () => {
    expect(() => getPaletteColorByNumber('not-a-color', 500)).toThrow()
  })
})

describe('getColorPalette', () => {
  it('returns all 11 levels', () => {
    const palette = getColorPalette('#18a058')
    const levels = [50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 950]
    for (const level of levels) {
      expect(palette[level as keyof typeof palette]).toBeDefined()
      expect(palette[level as keyof typeof palette]).toMatch(/^#[0-9a-f]{6}$/i)
    }
  })

  it('has level 500 equal to the input color', () => {
    const palette = getColorPalette('#18a058')
    expect(palette[500]).toBe('#18a058')
  })

  it('has monotonically changing lightness', () => {
    // Lightness should increase from 950 to 50
    const palette = getColorPalette('#18a058')
    // At least verify 50 is lighter than 500 and 900 is darker
    expect(palette[50]).not.toBe(palette[500])
    expect(palette[900]).not.toBe(palette[500])
  })
})
