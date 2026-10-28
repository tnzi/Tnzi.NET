import type { CssVarMap, ColorRole, PaletteLevel, ThemeColors } from './types'
import { getColorPalette, colorToRgbTriplet, getContrastTextColor, getReadableColor } from './palette'

const ROLES: readonly ColorRole[] = ['primary', 'info', 'success', 'warning', 'error']
const LEVELS: readonly PaletteLevel[] = [50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 950]

/**
 * The surfaces `--tnzi-primary-text-on-light` / `-on-dark` are guaranteed to
 * read on (4.5:1): every light surface from white down to the light reference,
 * every dark surface from black up to the dark reference.
 *
 * The light reference is a light grey rather than the page canvas because
 * accent-coloured text also sits on side panels and rails a step darker than
 * the canvas; a colour that reads there reads on every lighter surface. The
 * dark reference is the lightest dark surface in the palette (the dark
 * container, rgb(28, 28, 28)).
 */
export const READABLE_TEXT_BACKGROUND = { light: '#ebebeb', dark: '#1c1c1c' } as const

/**
 * Generate the full CSS variable map for a theme given the base colors and mode.
 *
 * Naming convention:
 *   --tnzi-{role}              hex base color (500)
 *   --tnzi-{role}-rgb          R G B triplet for `rgb(var(...) / α)` syntax
 *   --tnzi-{role}-{lvl}        palette level hex (50~950)
 *   --tnzi-{role}-{lvl}-rgb    palette level RGB triplet
 *   --tnzi-on-primary          text / icons on a primary fill (white or a near-black ink)
 *   --tnzi-primary-text-on-light / -on-dark
 *                              the primary as text on light / dark surfaces (4.5:1);
 *                              the stylesheet's `--tnzi-primary-text` picks one per mode
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
  /** Base corner radius. Omitted keeps the default, so existing callers are
   *  unaffected and simply get the structural tokens they had none of. */
  radius: number = DEFAULT_RADIUS,
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

  // Mode-independent on purpose: the fill is the same colour in both modes,
  // and the text pair is resolved per mode by the stylesheet alias, so a
  // writer that cannot follow the mode (an inline snapshot) still gets both.
  vars['--tnzi-on-primary'] = getContrastTextColor(colors.primary)
  vars['--tnzi-primary-text-on-light'] = getReadableColor(colors.primary, READABLE_TEXT_BACKGROUND.light)
  vars['--tnzi-primary-text-on-dark'] = getReadableColor(colors.primary, READABLE_TEXT_BACKGROUND.dark)

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

  Object.assign(vars, buildStructuralVars(radius))

  return vars
}

/**
 * Shape and motion, the half of a theme that is not colour.
 *
 * Kept apart from the palette because it does not vary with light/dark: a
 * radius or a duration is the same in both modes, and emitting it twice invites
 * the two copies to drift. `buildCssVars` folds it in so callers get one map.
 *
 * This layer exists because the packages above had grown their own: ui-admin
 * carried `--tnzi-admin-radius-*` (driven by its theme store, and separately
 * pushed into naive's `common.borderRadius`), and ui-ai carried literal
 * `--tnzi-ai-modal-radius` / `--tnzi-ai-duration-*` / `--tnzi-ai-easing`. Two
 * parallel systems mean "make the corners rounder" is two changes that can
 * disagree. Now both derive from here.
 *
 * @param radius base corner radius in px. The scale is derived rather than
 *        configured per-step: a product picks one number, and sm/lg/xl keep
 *        their proportions instead of needing four decisions.
 */
export function buildStructuralVars(radius = DEFAULT_RADIUS): CssVarMap {
  const r = clampRadius(radius)
  return {
    '--tnzi-radius': `${r}px`,
    '--tnzi-radius-sm': `${Math.max(0, Math.round(r * 0.5))}px`,
    '--tnzi-radius-lg': `${Math.round(r * 2)}px`,
    '--tnzi-radius-xl': `${Math.round(r * 3)}px`,
    /* Full-round. A token rather than a literal so "pill" is one concept. */
    '--tnzi-radius-pill': '999px',

    '--tnzi-duration-fast': '120ms',
    '--tnzi-duration-base': '200ms',
    '--tnzi-duration-slow': '320ms',
    /* Material's standard decelerate curve - what the sidebar width animation
       and every hover transition in the ecosystem already used by hand. */
    '--tnzi-easing': 'cubic-bezier(0.2, 0, 0, 1)',
  }
}

/** Matches the range ui-admin's radius slider already offered. */
export const DEFAULT_RADIUS = 6
const MAX_RADIUS = 16

export function clampRadius(radius: number): number {
  if (!Number.isFinite(radius)) return DEFAULT_RADIUS
  return Math.max(0, Math.min(MAX_RADIUS, Math.round(radius)))
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
