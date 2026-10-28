import { describe, it, expect, beforeEach } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { buildCssVars, injectCssVars, clearCssVars, READABLE_TEXT_BACKGROUND } from '../../src/theme/vars'
import { CONTRAST_TEXT_DARK, CONTRAST_TEXT_LIGHT, contrastRatio } from '../../src/theme/palette'
import { defaultThemeSettings } from '../../src/theme/settings'

/**
 * Minimal CSSStyleDeclaration-compatible mock. The package's vitest
 * environment is 'node' and no happy-dom/jsdom is installed in the
 * monorepo, so we stub just enough of HTMLElement.style for
 * injectCssVars / clearCssVars to work against.
 *
 * Behavior covered:
 * - setProperty / getPropertyValue / removeProperty for any key
 *   (CSS custom properties and normal properties alike)
 * - Array.from(style) iterates declared property names, matching
 *   the spec of CSSStyleDeclaration's indexed getter
 * - Named property access (e.g. style.color) proxied to the backing
 *   map so existing non-tnzi properties survive clearCssVars()
 */
function createMockElement(): HTMLElement {
  const props = new Map<string, string>()
  const style: Record<string | symbol, unknown> = {
    setProperty(name: string, value: string) {
      props.set(name, value)
    },
    getPropertyValue(name: string) {
      return props.get(name) ?? ''
    },
    removeProperty(name: string) {
      const prev = props.get(name) ?? ''
      props.delete(name)
      return prev
    },
    [Symbol.iterator]() {
      return props.keys()
    },
  }
  const proxy = new Proxy(style, {
    get(target, prop, receiver) {
      if (prop in target) return Reflect.get(target, prop, receiver)
      if (typeof prop === 'string') return props.get(prop) ?? ''
      return undefined
    },
    set(target, prop, value) {
      if (typeof prop === 'string' && !(prop in target)) {
        props.set(prop, String(value))
        return true
      }
      return Reflect.set(target, prop, value)
    },
  })
  return { style: proxy } as unknown as HTMLElement
}

describe('buildCssVars', () => {
  it('generates 5 base color vars and 55 palette-level vars for light mode', () => {
    const vars = buildCssVars(defaultThemeSettings.colors, 'light')
    const colorKeys = Object.keys(vars).filter(k =>
      /^--tnzi-(primary|info|success|warning|error)(-\d+)?$/.test(k),
    )
    // 5 bases + 5 roles × 11 levels = 60 (the `-rgb` variants don't match this regex)
    expect(colorKeys).toHaveLength(60)
    expect(vars['--tnzi-primary']).toBe('#0d9488')
    expect(vars['--tnzi-primary-500']).toBe('#0d9488')
    expect(vars['--tnzi-primary-50']).toMatch(/^#[0-9a-f]{6}$/i)
  })

  it('emits matching `-rgb` triplet variant for every color var', () => {
    const vars = buildCssVars(defaultThemeSettings.colors, 'light')
    // RGB triplet format is "R G B" - space-separated, no rgb() wrapper.
    expect(vars['--tnzi-primary-rgb']).toMatch(/^\d+ \d+ \d+$/)
    expect(vars['--tnzi-primary-500-rgb']).toMatch(/^\d+ \d+ \d+$/)
    // #0d9488 == rgb(13, 148, 136)
    expect(vars['--tnzi-primary-rgb']).toBe('13 148 136')
  })

  it('emits light-mode functional tokens', () => {
    const vars = buildCssVars(defaultThemeSettings.colors, 'light')
    expect(vars['--tnzi-base-text']).toBe('rgb(51, 54, 57)')
    expect(vars['--tnzi-layout-bg']).toBe('rgb(247, 250, 252)')
    expect(vars['--tnzi-container-bg']).toBe('rgb(255, 255, 255)')
    expect(vars['--tnzi-shadow-header']).toContain('rgb(0 21 41 / 8%)')
  })

  it('emits dark-mode functional tokens distinct from light', () => {
    const light = buildCssVars(defaultThemeSettings.colors, 'light')
    const dark = buildCssVars(defaultThemeSettings.colors, 'dark')
    expect(dark['--tnzi-base-text']).not.toBe(light['--tnzi-base-text'])
    expect(dark['--tnzi-layout-bg']).toBe('rgb(18, 18, 18)')
    expect(dark['--tnzi-container-bg']).toBe('rgb(28, 28, 28)')
  })
})

describe('buildCssVars: readable companions of the primary', () => {
  const withPrimary = (primary: string, mode: 'light' | 'dark' = 'light') =>
    buildCssVars({ ...defaultThemeSettings.colors, primary }, mode)

  it('puts white on a dark primary and the ink on a light one', () => {
    expect(withPrimary('#2563eb')['--tnzi-on-primary']).toBe(CONTRAST_TEXT_LIGHT)
    expect(withPrimary('#FFD217')['--tnzi-on-primary']).toBe(CONTRAST_TEXT_DARK)
    expect(contrastRatio(CONTRAST_TEXT_DARK, '#FFD217')).toBeGreaterThanOrEqual(4.5)
  })

  it('keeps the default teal exactly as it was: white on the fill, the colour itself on dark surfaces', () => {
    const vars = withPrimary('#0d9488')
    expect(vars['--tnzi-on-primary']).toBe('#ffffff')
    expect(vars['--tnzi-primary-text-on-dark']).toBe('#0d9488')
  })

  it.each(['#FFD217', '#0d9488', '#2563eb', '#f97316', '#7c3aed'])(
    'gives %s a text colour that reads at 4.5:1 on both canvases',
    (primary) => {
      const vars = withPrimary(primary)
      for (const canvas of ['#ffffff', '#f8f8f7', READABLE_TEXT_BACKGROUND.light, 'rgb(247, 250, 252)']) {
        expect(contrastRatio(vars['--tnzi-primary-text-on-light'], canvas)).toBeGreaterThanOrEqual(4.5)
      }
      for (const canvas of ['#141414', '#0f0f0f', READABLE_TEXT_BACKGROUND.dark, 'rgb(18, 18, 18)']) {
        expect(contrastRatio(vars['--tnzi-primary-text-on-dark'], canvas)).toBeGreaterThanOrEqual(4.5)
      }
    },
  )

  // An inline writer that cannot follow the mode (a snapshot applied without a
  // theme context) writes these once; the stylesheet alias picks per mode.
  it('emits the same companions in light and dark mode', () => {
    for (const key of ['--tnzi-on-primary', '--tnzi-primary-text-on-light', '--tnzi-primary-text-on-dark']) {
      expect(withPrimary('#FFD217', 'dark')[key]).toBe(withPrimary('#FFD217', 'light')[key])
    }
  })

  it('leaves the mode alias to the stylesheet', () => {
    expect(withPrimary('#FFD217')['--tnzi-primary-text']).toBeUndefined()
  })

  describe('stylesheet defaults', () => {
    const css = readFileSync(resolve(__dirname, '../../src/styles/variables.css'), 'utf8')
    const block = (selector: string) => {
      const start = css.indexOf(`\n${selector} {`)
      return css.slice(start, css.indexOf('\n}', start))
    }
    const declared = (text: string, name: string) =>
      new RegExp(`${name}:\\s*([^;]+);`).exec(text)?.[1]?.trim()

    // Components render with these before the theme context writes its map.
    it('match what the builder computes for the default primary', () => {
      const vars = buildCssVars(defaultThemeSettings.colors, 'light')
      const root = block(':root')
      for (const key of ['--tnzi-on-primary', '--tnzi-primary-text-on-light', '--tnzi-primary-text-on-dark']) {
        expect(declared(root, key)).toBe(vars[key])
      }
    })

    it('repoint the mode alias in the dark block', () => {
      expect(declared(block(':root'), '--tnzi-primary-text')).toBe('var(--tnzi-primary-text-on-light)')
      expect(declared(block('.dark'), '--tnzi-primary-text')).toBe('var(--tnzi-primary-text-on-dark)')
    })
  })
})

describe('injectCssVars / clearCssVars', () => {
  let el: HTMLElement

  beforeEach(() => {
    el = createMockElement()
  })

  it('sets each var as an inline CSS property', () => {
    injectCssVars({ '--tnzi-primary': '#123456', '--tnzi-layout-bg': '#ffffff' }, el)
    expect(el.style.getPropertyValue('--tnzi-primary')).toBe('#123456')
    expect(el.style.getPropertyValue('--tnzi-layout-bg')).toBe('#ffffff')
  })

  it('clears only --tnzi-* properties, leaving others intact', () => {
    el.style.setProperty('--tnzi-primary', '#123456')
    el.style.setProperty('--other-var', 'keep-me')
    el.style.color = 'red'
    clearCssVars(el)
    expect(el.style.getPropertyValue('--tnzi-primary')).toBe('')
    expect(el.style.getPropertyValue('--other-var')).toBe('keep-me')
    expect(el.style.color).toBe('red')
  })

  it('inject then clear round-trip removes all vars', () => {
    const vars = buildCssVars(defaultThemeSettings.colors, 'light')
    injectCssVars(vars, el)
    expect(el.style.getPropertyValue('--tnzi-primary')).toBe('#0d9488')
    clearCssVars(el)
    expect(el.style.getPropertyValue('--tnzi-primary')).toBe('')
  })
})
