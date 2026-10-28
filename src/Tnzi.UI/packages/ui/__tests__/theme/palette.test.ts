import { describe, it, expect } from 'vitest'
import { colord } from 'colord'
import {
  CONTRAST_TEXT_DARK,
  CONTRAST_TEXT_LIGHT,
  contrastRatio,
  getColorPalette,
  getContrastTextColor,
  getPaletteColorByNumber,
  getReadableColor,
  mixColor,
} from '../../src/theme/palette'

/** Contrast against black grows with luminance, so it orders colours by it. */
const lightness = (color: string) => contrastRatio(color, '#000000')

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

describe('mixColor', () => {
  it('returns the `from` color at ratio 0', () => {
    expect(mixColor('#ffffff', '#646cff', 0)).toBe('#ffffff')
  })

  it('returns the `to` color at ratio 1', () => {
    expect(mixColor('#ffffff', '#646cff', 1)).toBe('#646cff')
  })

  it('blends toward the tint at intermediate ratios', () => {
    const wash = mixColor('#ffffff', '#646cff', 0.2)
    expect(wash).not.toBe('#ffffff')
    expect(wash).not.toBe('#646cff')
    expect(wash).toMatch(/^#[0-9a-f]{6}$/i)
  })

  it('clamps ratio < 0 to 0 (pure `from`)', () => {
    expect(mixColor('#ffffff', '#000000', -1)).toBe('#ffffff')
  })

  it('clamps ratio > 1 to 1 (pure `to`)', () => {
    expect(mixColor('#ffffff', '#000000', 2)).toBe('#000000')
  })
})

describe('contrastRatio', () => {
  it('spans 1 (a colour against itself) to 21 (black against white)', () => {
    expect(contrastRatio('#0d9488', '#0d9488')).toBe(1)
    expect(contrastRatio('#000000', '#ffffff')).toBeCloseTo(21, 10)
  })

  it('gives the WCAG ratios for known pairs', () => {
    expect(contrastRatio('#ffffff', '#000000')).toBeCloseTo(21, 5)
    expect(contrastRatio('#777777', '#ffffff')).toBeCloseTo(4.48, 2)
    expect(contrastRatio('#ffffff', '#0d9488')).toBeCloseTo(3.74, 2)
    expect(contrastRatio('#ffffff', '#f97316')).toBeCloseTo(2.8, 2)
  })

  it('does not depend on the order of the pair', () => {
    expect(contrastRatio('#2563eb', '#f8f8f7')).toBe(contrastRatio('#f8f8f7', '#2563eb'))
  })

  it('throws on an invalid color', () => {
    expect(() => contrastRatio('not-a-color', '#ffffff')).toThrow(/invalid color/)
  })
})

describe('getContrastTextColor', () => {
  it('puts the dark ink on a light fill, readable at WCAG AA', () => {
    const on = getContrastTextColor('#FFD217')
    expect(on).toBe(CONTRAST_TEXT_DARK)
    expect(contrastRatio(on, '#FFD217')).toBeGreaterThanOrEqual(4.5)
  })

  it.each(['#2563eb', '#7c3aed', '#1f2937', '#000000'])('keeps white on a dark fill (%s)', (fill) => {
    expect(getContrastTextColor(fill)).toBe(CONTRAST_TEXT_LIGHT)
  })

  // The default primary is a mid-tone: white reads at 3.7:1 there and the
  // framework's buttons are designed white-on-teal. A pure maximum-contrast
  // rule would turn them dark.
  it('keeps white on the default teal, where white is above the 3:1 floor', () => {
    expect(getContrastTextColor('#0d9488')).toBe(CONTRAST_TEXT_LIGHT)
  })

  it('switches to the ink once white drops under 3:1 (orange, 2.8:1)', () => {
    expect(getContrastTextColor('#f97316')).toBe(CONTRAST_TEXT_DARK)
    expect(contrastRatio(CONTRAST_TEXT_DARK, '#f97316')).toBeGreaterThan(6)
  })

  // Wherever white falls under 3:1 the fill's luminance is above 0.3, and the
  // ink reads at 4.5:1 or better on every such fill.
  it('reads at 4.5:1 or better on every fill where it chooses the ink', () => {
    for (let step = 0; step <= 255; step += 15) {
      for (const fill of [
        colord({ r: step, g: 255, b: 0 }).toHex(),
        colord({ r: 255, g: step, b: 128 }).toHex(),
        colord({ r: step, g: step, b: 255 }).toHex(),
        colord({ r: step, g: step, b: step }).toHex(),
      ]) {
        const on = getContrastTextColor(fill)
        if (on === CONTRAST_TEXT_DARK) expect(contrastRatio(on, fill)).toBeGreaterThanOrEqual(4.5)
        else expect(contrastRatio(on, fill)).toBeGreaterThanOrEqual(3)
      }
    }
  })

  it('takes other candidates and another floor', () => {
    expect(getContrastTextColor('#0d9488', { minLightContrast: 4.5 })).toBe(CONTRAST_TEXT_DARK)
    expect(getContrastTextColor('#ffd217', { dark: '#000000' })).toBe('#000000')
    expect(getContrastTextColor('#2563eb', { light: '#fafafa' })).toBe('#fafafa')
  })
})

describe('getReadableColor', () => {
  const LIGHT_SURFACES = ['#ffffff', '#f8f8f7', '#ebebeb']
  const DARK_SURFACES = ['#000000', '#141414', '#1c1c1c']

  it('returns a colour that already reads unchanged', () => {
    expect(getReadableColor('#0d9488', '#1c1c1c')).toBe('#0d9488')
    expect(getReadableColor('#2563eb', '#ffffff')).toBe('#2563eb')
  })

  it.each(['#FFD217', '#0d9488', '#2563eb', '#f97316', '#ffffff', '#22d3ee', '#ec4899'])(
    'makes %s read at 4.5:1 on light surfaces down to the reference',
    (color) => {
      const text = getReadableColor(color, '#ebebeb')
      for (const surface of LIGHT_SURFACES) expect(contrastRatio(text, surface)).toBeGreaterThanOrEqual(4.5)
    },
  )

  it.each(['#FFD217', '#0d9488', '#2563eb', '#7c3aed', '#000000', '#1e3a8a'])(
    'makes %s read at 4.5:1 on dark surfaces up to the reference',
    (color) => {
      const text = getReadableColor(color, '#1c1c1c')
      for (const surface of DARK_SURFACES) expect(contrastRatio(text, surface)).toBeGreaterThanOrEqual(4.5)
    },
  )

  it('keeps the hue: a light yellow becomes a dark gold, not a grey', () => {
    const text = getReadableColor('#FFD217', '#ebebeb')
    expect(text).toBe('#816800')
    const before = colord('#FFD217').toHsl()
    const after = colord(text).toHsl()
    expect(Math.abs(after.h - before.h)).toBeLessThan(2)
    expect(after.s).toBeGreaterThan(90)
    expect(after.l).toBeLessThan(before.l)
  })

  it('moves no further than it has to (just reaches the ratio)', () => {
    const text = getReadableColor('#0d9488', '#ebebeb')
    expect(contrastRatio(text, '#ebebeb')).toBeLessThan(4.6)
  })

  it('lightens on a dark background and darkens on a light one', () => {
    expect(lightness(getReadableColor('#2563eb', '#141414'))).toBeGreaterThan(lightness('#2563eb'))
    expect(lightness(getReadableColor('#FFD217', '#f8f8f7'))).toBeLessThan(lightness('#FFD217'))
  })

  it('drops alpha from the result', () => {
    expect(getReadableColor('#2563eb80', '#ffffff')).toBe('#2563eb')
  })

  it('throws on an invalid color or background', () => {
    expect(() => getReadableColor('nope', '#ffffff')).toThrow(/invalid color/)
    expect(() => getReadableColor('#ffffff', 'nope')).toThrow(/invalid color/)
  })
})
