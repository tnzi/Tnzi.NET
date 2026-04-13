import { describe, it, expect, beforeEach } from 'vitest'
import { buildCssVars, injectCssVars, clearCssVars } from '../../src/theme/vars'
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
    // 5 bases + 5 roles × 11 levels = 60
    expect(colorKeys).toHaveLength(60)
    expect(vars['--tnzi-primary']).toBe('#18a058')
    expect(vars['--tnzi-primary-500']).toBe('#18a058')
    expect(vars['--tnzi-primary-50']).toMatch(/^#[0-9a-f]{6}$/i)
  })

  it('emits light-mode functional tokens', () => {
    const vars = buildCssVars(defaultThemeSettings.colors, 'light')
    expect(vars['--tnzi-base-text']).toBe('rgb(51, 54, 57)')
    expect(vars['--tnzi-layout-bg']).toBe('rgb(240, 242, 245)')
    expect(vars['--tnzi-container-bg']).toBe('#ffffff')
    expect(vars['--tnzi-shadow-header']).toContain('rgb(0 21 41 / 8%)')
  })

  it('emits dark-mode functional tokens distinct from light', () => {
    const light = buildCssVars(defaultThemeSettings.colors, 'light')
    const dark = buildCssVars(defaultThemeSettings.colors, 'dark')
    expect(dark['--tnzi-base-text']).not.toBe(light['--tnzi-base-text'])
    expect(dark['--tnzi-layout-bg']).toBe('rgb(16, 16, 20)')
    expect(dark['--tnzi-container-bg']).toBe('rgb(24, 24, 28)')
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
    expect(el.style.getPropertyValue('--tnzi-primary')).toBe('#18a058')
    clearCssVars(el)
    expect(el.style.getPropertyValue('--tnzi-primary')).toBe('')
  })
})
