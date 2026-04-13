import { describe, it, expect } from 'vitest'
import { presetTnzi } from '../../src/theme/uno-preset'

describe('presetTnzi', () => {
  it('returns a preset with name @tnzi/ui/uno-preset', () => {
    const preset = presetTnzi()
    expect(preset.name).toBe('@tnzi/ui/uno-preset')
  })

  it('defines 5 color roles × 11 levels + 5 base = 60 entries', () => {
    const preset = presetTnzi()
    const colorKeys = Object.keys((preset.theme as any)?.colors ?? {})
    expect(colorKeys).toContain('primary')
    expect(colorKeys).toContain('primary-50')
    expect(colorKeys).toContain('primary-950')
    expect(colorKeys).toContain('info-500')
    expect(colorKeys).toContain('error-50')
    expect(colorKeys.length).toBe(5 + 5 * 11)
  })

  it('maps colors to CSS variables', () => {
    const preset = presetTnzi()
    const colors = (preset.theme as any)?.colors as Record<string, string>
    expect(colors.primary).toBe('var(--tnzi-primary)')
    expect(colors['primary-500']).toBe('var(--tnzi-primary-500)')
  })

  it('exposes rules for functional tokens', () => {
    const preset = presetTnzi()
    const ruleNames = preset.rules?.map(r => r[0]) ?? []
    expect(ruleNames).toContain('bg-tnzi-container')
    expect(ruleNames).toContain('text-tnzi-base')
    expect(ruleNames).toContain('shadow-tnzi-header')
  })

  it('rule bodies reference CSS variables declared in vars.ts', () => {
    const preset = presetTnzi()
    const rulesByName = new Map(
      (preset.rules ?? []).map(r => [r[0] as string, r[1] as Record<string, string>]),
    )
    // Catches preset↔vars.ts token-name drift (e.g. --tnzi-continer-bg typo)
    expect(rulesByName.get('bg-tnzi-container')).toEqual({ 'background-color': 'var(--tnzi-container-bg)' })
    expect(rulesByName.get('text-tnzi-base')).toEqual({ color: 'var(--tnzi-base-text)' })
    expect(rulesByName.get('shadow-tnzi-header')).toEqual({ 'box-shadow': 'var(--tnzi-shadow-header)' })
    expect(rulesByName.get('border-tnzi')).toEqual({ 'border-color': 'var(--tnzi-border)' })
  })
})
