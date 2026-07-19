import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'

// Only TAddressFields imports naive-ui; TMetricBars is pure divs so the mock
// below doesn't affect it.
vi.mock('naive-ui', () => ({
  NInput: {
    name: 'NInput',
    props: ['value', 'disabled', 'placeholder'],
    emits: ['update:value'],
    template: '<input class="ninput" />',
  },
  NSelect: {
    name: 'NSelect',
    props: { value: {}, options: {}, filterable: Boolean, clearable: Boolean, disabled: Boolean },
    emits: ['update:value'],
    template: '<div class="nsel" />',
  },
}))

import TMetricBars from '../../display/TMetricBars.vue'
import TAddressFields from '../fields/TAddressFields.vue'

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

describe('TAddressFields', () => {
  it('emits an immutable update when a field changes', async () => {
    const w = mount(TAddressFields, { props: { modelValue: { city: 'Toronto' } } })
    const inputs = w.findAllComponents({ name: 'NInput' }) // [street, unit, city, region, postal]
    await inputs[0]!.vm.$emit('update:value', '123 Main St')
    expect(w.emitted('update:modelValue')?.[0]?.[0]).toEqual({ city: 'Toronto', street: '123 Main St' })
  })

  it('renders region as a select when regionOptions supplied, else free text', () => {
    const withOpts = mount(TAddressFields, { props: { regionOptions: [{ label: 'ON', value: 'ON' }] } })
    expect(withOpts.findComponent({ name: 'NSelect' }).exists()).toBe(true)
    const noOpts = mount(TAddressFields, { props: {} })
    expect(noOpts.findComponent({ name: 'NSelect' }).exists()).toBe(false)
  })

  it('shows the country field only when showCountry', () => {
    const w = mount(TAddressFields, { props: { showCountry: true } })
    // street/unit/city/region/postal (5) + country (1), all NInput (no option lists)
    expect(w.findAllComponents({ name: 'NInput' }).length).toBe(6)
  })
})
