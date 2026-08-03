import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'

/**
 * Behavioural suite for TAddressFields: emit immutability, conditional
 * region select, optional country field.
 *
 * Deliberately a separate file from `TAddressFields.test.ts` (the keyMap /
 * prefix suite) because the two need incompatible module graphs: that one
 * mounts the REAL naive-ui NInput to read values back off it, this one stubs
 * naive-ui so it can count and drive components by name. `vi.mock` is
 * file-scoped, so they cannot share one file.
 */
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

import TAddressFields from '../../../src/components/form/fields/TAddressFields.vue'

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
