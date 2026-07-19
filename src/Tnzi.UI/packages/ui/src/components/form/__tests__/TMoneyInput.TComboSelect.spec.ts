import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'

// Stub the two naive-ui controls so we can inspect the props the wrappers wire
// (format/parse fns, filterable/tag flags, merged options) without a provider.
vi.mock('naive-ui', () => ({
  NInputNumber: {
    name: 'NInputNumber',
    props: ['value', 'precision', 'showButton', 'format', 'parse', 'min', 'max', 'clearable', 'placeholder', 'disabled'],
    emits: ['update:value'],
    template: '<div class="inn"><slot name="prefix" /><slot name="suffix" /></div>',
  },
  NSelect: {
    name: 'NSelect',
    // Boolean-typed so bare attributes (`filterable`, `tag`) resolve to `true`
    // rather than the empty-string raw attribute value.
    props: {
      value: {},
      options: {},
      filterable: Boolean,
      tag: Boolean,
      clearable: Boolean,
      placeholder: {},
      disabled: Boolean,
    },
    emits: ['update:value'],
    template: '<div class="nsel" />',
  },
}))

import TMoneyInput from '../fields/TMoneyInput.vue'
import TComboSelect from '../fields/TComboSelect.vue'

describe('TMoneyInput', () => {
  it('defaults to 2-decimal precision, no spinner, $ prefix', () => {
    const w = mount(TMoneyInput, { props: { modelValue: 1234.5 } })
    const inn = w.findComponent({ name: 'NInputNumber' })
    expect(inn.props('precision')).toBe(2)
    expect(inn.props('showButton')).toBe(false)
    expect(w.text()).toContain('$')
  })

  it('format applies thousand separators + fixed precision (null → empty)', () => {
    const w = mount(TMoneyInput, { props: { modelValue: 0 } })
    const format = w.findComponent({ name: 'NInputNumber' }).props('format') as (v: number | null) => string
    expect(format(1234567.5)).toBe('1,234,567.50')
    expect(format(null)).toBe('')
  })

  it('parse strips separators and returns a number or null', () => {
    const w = mount(TMoneyInput, { props: {} })
    const parse = w.findComponent({ name: 'NInputNumber' }).props('parse') as (s: string) => number | null
    expect(parse('1,234.50')).toBe(1234.5)
    expect(parse('')).toBeNull()
  })

  it('emits update:modelValue from the inner update:value', async () => {
    const w = mount(TMoneyInput, { props: { modelValue: null } })
    await w.findComponent({ name: 'NInputNumber' }).vm.$emit('update:value', 42)
    expect(w.emitted('update:modelValue')?.[0]).toEqual([42])
  })
})

describe('TComboSelect', () => {
  it('is a filterable + tag select', () => {
    const sel = mount(TComboSelect, { props: { options: ['a', 'b'] } }).findComponent({ name: 'NSelect' })
    expect(sel.props('filterable')).toBe(true)
    expect(sel.props('tag')).toBe(true)
  })

  it('normalizes string options to {label,value}', () => {
    const w = mount(TComboSelect, { props: { options: ['a'] } })
    expect(w.findComponent({ name: 'NSelect' }).props('options')).toEqual([{ label: 'a', value: 'a' }])
  })

  it('keeps a hand-typed value visible by prepending it to the options', () => {
    const w = mount(TComboSelect, { props: { modelValue: 'typed', options: ['a'] } })
    expect(w.findComponent({ name: 'NSelect' }).props('options')).toEqual([
      { label: 'typed', value: 'typed' },
      { label: 'a', value: 'a' },
    ])
  })

  it('emits update:modelValue as string|null', async () => {
    const w = mount(TComboSelect, { props: {} })
    await w.findComponent({ name: 'NSelect' }).vm.$emit('update:value', 'x')
    expect(w.emitted('update:modelValue')?.[0]).toEqual(['x'])
  })
})
