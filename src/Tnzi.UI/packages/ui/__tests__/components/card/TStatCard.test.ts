import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TStatCard from '../../../src/components/card/TStatCard.vue'

const stubs = {
  'n-card': { template: '<div class="n-card" :style="$attrs.style"><slot /></div>', inheritAttrs: false },
  'n-statistic': { template: '<div><slot /></div>' },
  'n-skeleton': true,
  'n-number-animation': true,
}

function borderVar(color?: string): string {
  const wrapper = mount(TStatCard, {
    props: { title: 'Revenue', value: 42, ...(color ? { color } : {}) },
    global: { stubs },
  })
  return wrapper.find('.t-stat-card').attributes('style') ?? ''
}

describe('TStatCard accent colour', () => {
  it('resolves a semantic role to its palette token', () => {
    expect(borderVar('success')).toContain('--t-stat-border-color: var(--tnzi-success-500)')
    expect(borderVar('warning')).toContain('--t-stat-border-color: var(--tnzi-warning-500)')
    expect(borderVar('primary')).toContain('--t-stat-border-color: var(--tnzi-primary-500)')
  })

  it('defaults to the info role', () => {
    expect(borderVar()).toContain('--t-stat-border-color: var(--tnzi-info-500)')
  })

  // The card used to hard-code #2080f0 / #18a058 / #f0a020 / #d03050, four of
  // which were byte-identical to the theme defaults - so a consumer who changed
  // their palette got a card that quietly stayed on the old colours. These
  // assertions pin the replacement AND the legacy names' continued support.
  it('maps legacy colour names onto theme roles instead of frozen hexes', () => {
    expect(borderVar('blue')).toContain('var(--tnzi-info-500)')
    expect(borderVar('green')).toContain('var(--tnzi-success-500)')
    expect(borderVar('orange')).toContain('var(--tnzi-warning-500)')
    expect(borderVar('red')).toContain('var(--tnzi-error-500)')
    expect(borderVar('purple')).toContain('var(--tnzi-primary-500)')
  })

  it('emits no literal hex for any accepted colour', () => {
    // Scope the assertion to our own custom property: the element also carries
    // Naive UI's own `--n-*` variables, several of which are legitimately hex.
    for (const c of ['primary', 'info', 'success', 'warning', 'error', 'blue', 'green', 'orange', 'red', 'purple']) {
      const declaration = borderVar(c)
        .split(';')
        .map((d) => d.trim())
        .find((d) => d.startsWith('--t-stat-border-color'))

      expect(declaration, `no --t-stat-border-color emitted for color="${c}"`).toBeDefined()
      expect(declaration).not.toMatch(/#[0-9a-f]{3,6}/i)
    }
  })
})
