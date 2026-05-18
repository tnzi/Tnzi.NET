import type { CssVarMap, ColorRole, PaletteLevel, ThemeColors } from './types'
import { getColorPalette, colorToRgbTriplet } from './palette'

const ROLES: readonly ColorRole[] = ['primary', 'info', 'success', 'warning', 'error']
const LEVELS: readonly PaletteLevel[] = [50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 950]

/**
 * Generate the full CSS variable map for a theme given the base colors and mode.
 *
 * Naming convention:
 *   --tnzi-{role}              hex base color (500)
 *   --tnzi-{role}-rgb          R G B triplet for `rgb(var(...) / α)` syntax
 *   --tnzi-{role}-{lvl}        palette level hex (50~950)
 *   --tnzi-{role}-{lvl}-rgb    palette level RGB triplet
 *   --tnzi-base-text           primary text color
 *   --tnzi-layout-bg           body / layout background
 *   --tnzi-container-bg        card / container background
 *   --tnzi-shadow-*            box shadow tokens
 *
 * RGB triplet variant enables dynamic transparency:
 *   background: rgb(var(--tnzi-primary-rgb) / 0.5)
 */
export function buildCssVars(
  colors: ThemeColors,
  mode: 'light' | 'dark',
): CssVarMap {
  const vars: CssVarMap = {}

  for (const role of ROLES) {
    const palette = getColorPalette(colors[role])
    vars[`--tnzi-${role}`] = colors[role]
    vars[`--tnzi-${role}-rgb`] = colorToRgbTriplet(colors[role])
    for (const level of LEVELS) {
      const hex = palette[level]
      vars[`--tnzi-${role}-${level}`] = hex
      vars[`--tnzi-${role}-${level}-rgb`] = colorToRgbTriplet(hex)
    }
  }

  if (mode === 'light') {
    vars['--tnzi-base-text'] = 'rgb(51, 54, 57)'
    vars['--tnzi-base-text-muted'] = 'rgb(118, 124, 130)'
    vars['--tnzi-base-text-rgb'] = '51 54 57'
    vars['--tnzi-layout-bg'] = 'rgb(247, 250, 252)'
    vars['--tnzi-layout-bg-rgb'] = '247 250 252'
    vars['--tnzi-container-bg'] = 'rgb(255, 255, 255)'
    vars['--tnzi-container-bg-rgb'] = '255 255 255'
    vars['--tnzi-inverted'] = 'rgb(0, 20, 40)'
    vars['--tnzi-inverted-rgb'] = '0 20 40'
    vars['--tnzi-border'] = 'rgb(239, 239, 245)'
    vars['--tnzi-border-rgb'] = '239 239 245'
    vars['--tnzi-divider'] = 'rgb(231, 234, 237)'
    vars['--tnzi-header-blur-bg'] = 'rgba(255, 255, 255, 0.75)'
    vars['--tnzi-shadow-header'] = '0 1px 2px rgb(0 21 41 / 8%)'
    vars['--tnzi-shadow-sider'] = '2px 0 8px 0 rgb(29 35 41 / 5%)'
    vars['--tnzi-shadow-tab'] = '0 1px 2px 0 rgb(0 21 41 / 8%)'
    vars['--tnzi-shadow-card'] = '0 1px 2px 0 rgb(0 0 0 / 3%), 0 1px 6px -1px rgb(0 0 0 / 2%)'
    vars['--tnzi-shadow-drawer'] = '0 8px 24px rgb(0 0 0 / 12%)'
  } else {
    vars['--tnzi-base-text'] = 'rgb(224, 224, 224)'
    vars['--tnzi-base-text-muted'] = 'rgb(150, 150, 150)'
    vars['--tnzi-base-text-rgb'] = '224 224 224'
    vars['--tnzi-layout-bg'] = 'rgb(18, 18, 18)'
    vars['--tnzi-layout-bg-rgb'] = '18 18 18'
    vars['--tnzi-container-bg'] = 'rgb(28, 28, 28)'
    vars['--tnzi-container-bg-rgb'] = '28 28 28'
    vars['--tnzi-inverted'] = 'rgb(255, 255, 255)'
    vars['--tnzi-inverted-rgb'] = '255 255 255'
    vars['--tnzi-border'] = 'rgba(255, 255, 255, 0.09)'
    vars['--tnzi-border-rgb'] = '255 255 255'
    vars['--tnzi-divider'] = 'rgba(255, 255, 255, 0.06)'
    vars['--tnzi-header-blur-bg'] = 'rgba(20, 20, 24, 0.6)'
    vars['--tnzi-shadow-header'] = '0 1px 4px 0 rgb(0 0 0 / 40%)'
    vars['--tnzi-shadow-sider'] = '2px 0 8px 0 rgb(0 0 0 / 40%)'
    vars['--tnzi-shadow-tab'] = '0 1px 2px 0 rgb(0 0 0 / 40%)'
    vars['--tnzi-shadow-card'] = '0 1px 2px 0 rgb(0 0 0 / 30%), 0 1px 6px -1px rgb(0 0 0 / 25%)'
    vars['--tnzi-shadow-drawer'] = '0 8px 24px rgb(0 0 0 / 50%)'
  }

  return vars
}

/**
 * Inject the CSS variable map into `:root` as inline style.
 * Used by `createTnziUi()` plugin at app mount time.
 */
export function injectCssVars(vars: CssVarMap, target: HTMLElement = document.documentElement): void {
  for (const [key, value] of Object.entries(vars)) {
    target.style.setProperty(key, value)
  }
}

/**
 * Remove all Tnzi CSS variables from a target element.
 */
export function clearCssVars(target: HTMLElement = document.documentElement): void {
  const keys = Array.from(target.style).filter(k => k.startsWith('--tnzi-'))
  for (const key of keys) {
    target.style.removeProperty(key)
  }
}
