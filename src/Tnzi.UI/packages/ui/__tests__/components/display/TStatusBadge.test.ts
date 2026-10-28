import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { NTag } from 'naive-ui'
import TStatusBadge from '../../../src/components/display/TStatusBadge.vue'
import { EMPTY_DASH } from '../../../src/utils/placeholders'

const mapping = { active: { type: 'success' as const, label: 'Active' } }

describe('TStatusBadge', () => {
  it('maps a value to its label and tone', () => {
    const w = mount(TStatusBadge, { props: { value: 'active', mapping } })
    expect(w.findComponent(NTag).props('type')).toBe('success')
    expect(w.text()).toBe('Active')
  })

  // The value prop admits null / undefined; the stringified fallback label used
  // to render a grey pill reading "null" / "undefined" on rows without a status.
  it.each([null, undefined, '', '  '])('renders the empty placeholder for %j', (value) => {
    const w = mount(TStatusBadge, { props: { value, mapping } })
    expect(w.findComponent(NTag).exists()).toBe(false)
    expect(w.text()).toBe(EMPTY_DASH)
  })

  it('still renders an explicit label for an empty value', () => {
    const w = mount(TStatusBadge, { props: { value: null, label: 'None', type: 'default' as const } })
    expect(w.findComponent(NTag).exists()).toBe(true)
    expect(w.text()).toBe('None')
  })

  it('still honours a mapping entry for the empty key', () => {
    const w = mount(TStatusBadge, { props: { value: null, mapping: { null: { type: 'warning' as const, label: 'Unset' } } } })
    expect(w.findComponent(NTag).props('type')).toBe('warning')
    expect(w.text()).toBe('Unset')
  })

  it('keeps false and 0 as values', () => {
    expect(mount(TStatusBadge, { props: { value: false } }).findComponent(NTag).exists()).toBe(true)
    expect(mount(TStatusBadge, { props: { value: 0 } }).text()).toBe('0')
  })
})
