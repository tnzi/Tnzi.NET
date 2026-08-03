import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TMetricBars from '../../../src/components/display/TMetricBars.vue'

/**
 * TMetricBars renders plain divs - no naive-ui involved, so unlike the
 * TAddressFields suites this file needs no component mocking.
 *
 * (This suite used to live under src/components/form/__tests__/ even though
 * the component is in display/.)
 */
const styleWidth = (el: Element): string => (el as HTMLElement).style.width

describe('TMetricBars', () => {
  it('renders a row per item with value + scales the bar to the max', () => {
    const w = mount(TMetricBars, { props: { items: [{ label: 'A', value: 10 }, { label: 'B', value: 5 }] } })
    expect(w.findAll('.t-metric-bars__row')).toHaveLength(2)
    expect(w.text()).toContain('A')
    expect(w.text()).toContain('10')
    const fills = w.findAll('.t-metric-bars__fill')
    expect(styleWidth(fills[0]!.element)).toBe('100%')
    expect(styleWidth(fills[1]!.element)).toBe('50%')
  })

  it('respects an explicit max', () => {
    const w = mount(TMetricBars, { props: { items: [{ label: 'A', value: 5 }], max: 20 } })
    expect(styleWidth(w.find('.t-metric-bars__fill').element)).toBe('25%')
  })

  it('uses the display override for the value', () => {
    const w = mount(TMetricBars, { props: { items: [{ label: 'A', value: 1000, display: '$1,000' }] } })
    expect(w.text()).toContain('$1,000')
  })

  it('shows the empty text when there are no items', () => {
    const w = mount(TMetricBars, { props: { items: [], emptyText: 'Nothing' } })
    expect(w.text()).toContain('Nothing')
  })
})
