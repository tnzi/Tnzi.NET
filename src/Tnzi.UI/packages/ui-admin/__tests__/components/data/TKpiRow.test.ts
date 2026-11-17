import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'
import { readFileSync } from 'fs'
import { join } from 'path'
import TKpiRow from '../../../src/components/data/TKpiRow.vue'
import TKpiCard from '../../../src/components/data/TKpiCard.vue'

describe('TKpiRow', () => {
  it('renders an NGrid (responsive screen, default cols "1 s:2 m:4", 12px gap)', () => {
    const w = mount(TKpiRow, {
      slots: { default: () => [h(TKpiCard, { label: 'A', value: 1, animated: false })] },
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
      slots: { default: () => [h(TKpiCard, { label: 'A', value: 1, animated: false })] },
    })
    const grid = w.findComponent({ name: 'Grid' })
    expect(grid.props('cols')).toBe('1 s:2 m:3')
    expect(grid.props('xGap')).toBe(16)
  })

  it('auto-wraps each direct child in a grid item', () => {
    const w = mount(TKpiRow, {
      slots: {
        default: () => [
          h(TKpiCard, { label: 'A', value: 1, animated: false }),
          h(TKpiCard, { label: 'B', value: 2, animated: false }),
          h(TKpiCard, { label: 'C', value: 3, animated: false }),
        ],
      },
    })
    expect(w.findAllComponents({ name: 'GridItem' })).toHaveLength(3)
    expect(w.findAllComponents(TKpiCard)).toHaveLength(3)
  })

  it('flattens v-for fragments and skips v-if placeholders', () => {
    const Host = defineComponent({
      components: { TKpiRow, TKpiCard },
      template: `
        <TKpiRow>
          <TKpiCard v-for="n in 3" :key="n" :label="'L' + n" :value="n" :animated="false" />
          <TKpiCard v-if="false" label="hidden" :value="0" :animated="false" />
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
      slots: { default: () => [h(TKpiCard, { label: 'A', value: 1, animated: false })] },
    })
    expect(w.classes()).toContain('orders-page__stats')
    expect(w.classes()).toContain('t-kpi-row')
  })

  describe('tiles in one row are the same height', () => {
    // jsdom has no layout, so the height chain is held by its two halves: the
    // structure (every grid item is a cell, the card body is the flex column
    // the footer sits in) and the CSS rules that make the stretch happen.
    const read = (name: string) =>
      readFileSync(join(__dirname, '..', '..', '..', 'src', 'components', 'data', name), 'utf-8')
    const styleOf = (source: string) => source.slice(source.indexOf('<style'))
    const rule = (css: string, selector: string) => {
      const start = css.indexOf(`${selector} {`)
      expect(start, `rule ${selector}`).toBeGreaterThanOrEqual(0)
      return css.slice(start, css.indexOf('}', start)).replace(/\s+/g, ' ')
    }

    it('marks every grid item as a cell, with or without a footer on the card', () => {
      const w = mount(TKpiRow, {
        slots: {
          default: () => [
            h(TKpiCard, { label: 'A', value: 1, animated: false }),
            h(TKpiCard, { label: 'B', value: 2, animated: false }, { footer: () => 'one invite pending' }),
            h(TKpiCard, { label: 'C', value: 3, animated: false }, { extra: () => 'tag' }),
          ],
        },
      })
      const items = w.findAllComponents({ name: 'GridItem' })
      expect(items).toHaveLength(3)
      for (const item of items) expect(item.classes()).toContain('t-kpi-row__cell')
      expect(w.findAll('.t-kpi-row__cell > .t-stat-card')).toHaveLength(3)
      expect(w.findAll('.t-stat-card > .t-stat-card__body')).toHaveLength(3)
      expect(w.find('.t-stat-card__body > .t-stat-card__footer').exists()).toBe(true)
    })

    it('stretches the cell child to the cell, which the grid stretches to the row', () => {
      const css = styleOf(read('TKpiRow.vue'))
      // Global, not scoped: the child is the page's vnode, out of this component's scope.
      expect(css.startsWith('<style>')).toBe(true)
      expect(rule(css, '.t-kpi-row__cell')).toMatch(/display: flex;.*flex-direction: column;/)
      expect(rule(css, '.t-kpi-row__cell > *')).toContain('flex: 1 1 auto;')
    })

    it('keeps the card content at the top and the footer on the bottom edge', () => {
      const css = styleOf(read('TKpiCard.vue'))
      expect(rule(css, '.t-stat-card :deep(.t-stat-card__body)')).toMatch(/display: flex;.*flex-direction: column;/)
      expect(rule(css, '.t-stat-card__footer')).toContain('margin-top: auto;')
    })
  })
})
