import type { CssVarMap, ThemeColors } from './types'
import { getColorPalette } from './palette'

/**
 * Generate the full CSS variable map for a theme given the base colors and mode.
 *
 * Naming convention:
 *   --tnzi-{role}       base color (500)
 *   --tnzi-{role}-{lvl} palette level (50~950)
 *   --tnzi-base-text    primary text color
 *   --tnzi-layout-bg    body / layout background
 *   --tnzi-container-bg card / container background
 *   --tnzi-shadow-*     box shadow tokens
 */
export function buildCssVars(
  colors: ThemeColors,
  mode: 'light' | 'dark',
): CssVarMap {
  const vars: CssVarMap = {}

  // 5 roles × 11 levels = 55 color vars + 5 base shortcuts
  const roles: Array<keyof ThemeColors> = ['primary', 'info', 'success', 'warning', 'error']
  for (const role of roles) {
    const palette = getColorPalette(colors[role])
    vars[`--tnzi-${role}`] = colors[role]
    for (const level of [50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 950] as const) {
      vars[`--tnzi-${role}-${level}`] = palette[level]
    }
  }

  // Functional tokens
  if (mode === 'light') {
    vars['--tnzi-base-text'] = 'rgb(51, 54, 57)'
    vars['--tnzi-base-text-muted'] = 'rgb(118, 124, 130)'
    vars['--tnzi-layout-bg'] = 'rgb(240, 242, 245)'
    vars['--tnzi-container-bg'] = '#ffffff'
    vars['--tnzi-inverted'] = 'rgb(0, 20, 40)'
    vars['--tnzi-border'] = 'rgb(239, 239, 245)'
    vars['--tnzi-shadow-header'] = '0 1px 4px 0 rgb(0 21 41 / 8%)'
    vars['--tnzi-shadow-sider'] = '2px 0 8px 0 rgb(29 35 41 / 5%)'
    vars['--tnzi-shadow-tab'] = '0 1px 2px 0 rgb(0 21 41 / 4%)'
    vars['--tnzi-shadow-card'] = '0 1px 2px 0 rgb(0 0 0 / 3%), 0 1px 6px -1px rgb(0 0 0 / 2%)'
  } else {
    vars['--tnzi-base-text'] = 'rgba(255, 255, 255, 0.82)'
    vars['--tnzi-base-text-muted'] = 'rgba(255, 255, 255, 0.52)'
    vars['--tnzi-layout-bg'] = 'rgb(16, 16, 20)'
    vars['--tnzi-container-bg'] = 'rgb(24, 24, 28)'
    vars['--tnzi-inverted'] = 'rgb(255, 255, 255)'
    vars['--tnzi-border'] = 'rgba(255, 255, 255, 0.09)'
    vars['--tnzi-shadow-header'] = '0 1px 4px 0 rgb(0 0 0 / 40%)'
    vars['--tnzi-shadow-sider'] = '2px 0 8px 0 rgb(0 0 0 / 40%)'
    vars['--tnzi-shadow-tab'] = '0 1px 2px 0 rgb(0 0 0 / 40%)'
    vars['--tnzi-shadow-card'] = '0 1px 2px 0 rgb(0 0 0 / 30%), 0 1px 6px -1px rgb(0 0 0 / 25%)'
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
