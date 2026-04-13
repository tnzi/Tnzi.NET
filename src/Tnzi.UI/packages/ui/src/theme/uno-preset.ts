import type { Preset } from 'unocss'

/**
 * UnoCSS preset that exposes Tnzi CSS variables as atomic utility classes.
 *
 * Generated classes:
 *   bg-primary / bg-primary-{50..950}           → background-color: var(--tnzi-primary[-level])
 *   text-primary / text-primary-{50..950}       → color: var(--tnzi-primary[-level])
 *   border-primary / border-primary-{50..950}   → border-color: var(--tnzi-primary[-level])
 *   (same for info/success/warning/error)
 *
 *   bg-tnzi-container / bg-tnzi-layout          → functional background tokens
 *   text-tnzi-base / text-tnzi-muted            → functional text tokens
 *   shadow-tnzi-header / shadow-tnzi-sider / shadow-tnzi-tab / shadow-tnzi-card
 *
 * Usage in consumer vite.config.ts:
 *   import { presetTnzi } from '@tnzi/ui/theme'
 *   import UnoCSS from 'unocss/vite'
 *   export default { plugins: [UnoCSS({ presets: [presetTnzi()] })] }
 */
export function presetTnzi(): Preset {
  const roles = ['primary', 'info', 'success', 'warning', 'error'] as const
  const levels = [50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 950] as const

  const colors: Record<string, string> = {}
  for (const role of roles) {
    colors[role] = `var(--tnzi-${role})`
    for (const level of levels) {
      colors[`${role}-${level}`] = `var(--tnzi-${role}-${level})`
    }
  }

  return {
    name: '@tnzi/ui/uno-preset',
    theme: {
      colors,
    },
    rules: [
      ['bg-tnzi-container', { 'background-color': 'var(--tnzi-container-bg)' }],
      ['bg-tnzi-layout', { 'background-color': 'var(--tnzi-layout-bg)' }],
      ['text-tnzi-base', { color: 'var(--tnzi-base-text)' }],
      ['text-tnzi-muted', { color: 'var(--tnzi-base-text-muted)' }],
      ['border-tnzi', { 'border-color': 'var(--tnzi-border)' }],
      ['shadow-tnzi-header', { 'box-shadow': 'var(--tnzi-shadow-header)' }],
      ['shadow-tnzi-sider', { 'box-shadow': 'var(--tnzi-shadow-sider)' }],
      ['shadow-tnzi-tab', { 'box-shadow': 'var(--tnzi-shadow-tab)' }],
      ['shadow-tnzi-card', { 'box-shadow': 'var(--tnzi-shadow-card)' }],
    ],
  }
}
