import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TDescriptions from '../../../src/components/display/TDescriptions.vue'
import { EMPTY_DASH } from '../../../src/utils/placeholders'

/**
 * `TDescriptions` is the read-only primitive every detail view and every
 * `TSchemaForm` in `readonlyLayout="descriptions"` mode renders through, and it
 * shipped with no tests at all. These lock the two behaviours that are easy to
 * break silently: the empty-value contract and the column cap.
 */
describe('TDescriptions', () => {
  const items = [
    { key: 'name', label: 'Name', value: 'Alice' },
    { key: 'note', label: 'Note', value: '' },
  ]

  it('renders a muted placeholder for blank values, never a blank cell', () => {
    const wrapper = mount(TDescriptions, { props: { items } })
    const values = wrapper.findAll('.t-desc__value')

    expect(values[0].text()).toBe('Alice')
    expect(values[1].text()).toBe(EMPTY_DASH)
    expect(values[1].classes()).toContain('t-desc__value--empty')
    // A real value must not be styled as absent.
    expect(values[0].classes()).not.toContain('t-desc__value--empty')
  })

  it('treats 0 and false as values, not as absences', () => {
    const wrapper = mount(TDescriptions, {
      props: { items: [{ label: 'Count', value: 0 }, { label: 'Flag', value: false }] },
    })
    const values = wrapper.findAll('.t-desc__value')

    // "we have no figure" and "the figure is zero" are different statements.
    expect(values[0].text()).toBe('0')
    expect(values[0].classes()).not.toContain('t-desc__value--empty')

    // Booleans read as Yes/No rather than the raw literal.
    expect(values[1].text()).toBe('No')
    expect(values[1].classes()).not.toContain('t-desc__value--empty')
  })

  it('routes boolean labels through translate when a resolver is supplied', () => {
    const wrapper = mount(TDescriptions, {
      props: {
        items: [{ label: 'Flag', value: true }],
        translate: (key: string) => (key === 'admin.common.yes' ? '是' : key),
      },
    })
    expect(wrapper.find('.t-desc__value').text()).toBe('是')
  })

  it('leaves a custom renderer alone rather than overriding it with the dash', () => {
    const wrapper = mount(TDescriptions, {
      props: { items: [{ label: 'Status', value: '', render: () => 'Active' }] },
    })
    const value = wrapper.find('.t-desc__value')

    expect(value.text()).toBe('Active')
    expect(value.classes()).not.toContain('t-desc__value--empty')
  })

  it('hides rows marked hidden', () => {
    const wrapper = mount(TDescriptions, {
      props: { items: [...items, { label: 'Secret', value: 'x', hidden: true }] },
    })
    expect(wrapper.findAll('.t-desc__value')).toHaveLength(2)
  })

  it('resolves labelKey through translate, falling back to label', () => {
    const wrapper = mount(TDescriptions, {
      props: {
        items: [
          { label: 'Raw', labelKey: 'fields.name', value: 'a' },
          { label: 'Plain', value: 'b' },
        ],
        translate: (key: string) => (key === 'fields.name' ? 'Translated' : key),
      },
    })
    const labels = wrapper.findAll('.t-desc__label')
    expect(labels[0].text()).toBe('Translated')
    expect(labels[1].text()).toBe('Plain')
  })

  // Regression: `maxColumns` used to be enforced with a `max-width`, which pinned
  // a single-column record to 280px and left the rest of the row blank. The cap
  // is about the column COUNT; the block must still fill its container.
  it('caps the column count without narrowing the block', () => {
    const single = mount(TDescriptions, { props: { items, maxColumns: 1 } })
    const style = single.find('.t-desc').attributes('style') ?? ''

    expect(style).not.toMatch(/max-width/)
    expect(style).toMatch(/grid-template-columns/)

    const uncapped = mount(TDescriptions, { props: { items } })
    expect(uncapped.find('.t-desc').attributes('style') ?? '').not.toMatch(/max-width/)
  })

  it('raises the per-track floor so auto-fit cannot exceed maxColumns', () => {
    const two = mount(TDescriptions, { props: { items, maxColumns: 2 } })
    // 100% / 2 = 50%: each track claims at least half the row, so a third
    // track can never fit.
    expect(two.find('.t-desc').attributes('style')).toContain('50%')
  })
})
