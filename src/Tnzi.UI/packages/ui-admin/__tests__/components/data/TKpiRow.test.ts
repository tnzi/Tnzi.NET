import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'
import TKpiRow from '../../../src/components/data/TKpiRow.vue'
import TStatCard from '../../../src/components/data/TStatCard.vue'

describe('TKpiRow', () => {
  it('renders an NGrid (responsive screen, default cols "1 s:2 m:4", 12px gap)', () => {
    const w = mount(TKpiRow, {
      slots: { default: () => [h(TStatCard, { label: 'A', value: 1, animated: false })] },
    })
    const grid = w.findComponent({ name: 'Grid' })
    expect(grid.exists()).toBe(true)
    expect(grid.props('cols')).toBe('1 s:2 m:4')
    expect(grid.props('responsive')).toBe('screen')
    expect(grid.props('xGap')).toBe(12)
    expect(grid.props('yGap')).toBe(12)
    expect(w.classes()).toContain('t-kpi-row')
  })

  it('accepts a custom cols string and gap', () => {
    const w = mount(TKpiRow, {
      props: { cols: '1 s:2 m:3', gap: 16 },
      slots: { default: () => [h(TStatCard, { label: 'A', value: 1, animated: false })] },
    })
    const grid = w.findComponent({ name: 'Grid' })
    expect(grid.props('cols')).toBe('1 s:2 m:3')
    expect(grid.props('xGap')).toBe(16)
  })

  it('auto-wraps each direct child in a grid item', () => {
    const w = mount(TKpiRow, {
      slots: {
        default: () => [
          h(TStatCard, { label: 'A', value: 1, animated: false }),
          h(TStatCard, { label: 'B', value: 2, animated: false }),
          h(TStatCard, { label: 'C', value: 3, animated: false }),
        ],
      },
    })
    expect(w.findAllComponents({ name: 'GridItem' })).toHaveLength(3)
    expect(w.findAllComponents(TStatCard)).toHaveLength(3)
  })

  it('flattens v-for fragments and skips v-if placeholders', () => {
    const Host = defineComponent({
      components: { TKpiRow, TStatCard },
      template: `
        <TKpiRow>
          <TStatCard v-for="n in 3" :key="n" :label="'L' + n" :value="n" :animated="false" />
          <TStatCard v-if="false" label="hidden" :value="0" :animated="false" />
        </TKpiRow>
      `,
    })
    const w = mount(Host)
    const items = w.findAllComponents({ name: 'GridItem' })
    expect(items).toHaveLength(3)
    expect(w.text()).toContain('L1')
    expect(w.text()).toContain('L3')
    expect(w.text()).not.toContain('hidden')
  })

  it('passes extra classes through to the grid root (page-level hooks survive)', () => {
    const w = mount(TKpiRow, {
      attrs: { class: 'orders-page__stats' },
      slots: { default: () => [h(TStatCard, { label: 'A', value: 1, animated: false })] },
    })
    expect(w.classes()).toContain('orders-page__stats')
    expect(w.classes()).toContain('t-kpi-row')
  })
})
