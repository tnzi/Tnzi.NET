import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TFilterChips from '../../../src/components/navigation/TFilterChips.vue'

/**
 * TFilterChips renders native buttons - no naive-ui involved, so this suite
 * needs no component mocking.
 *
 * The behaviour worth pinning here is the part the three hand-rolled bars this
 * component replaces each got a different subset of right: what a click on the
 * ALREADY-selected chip does, whether `count: 0` renders, and whether the chip
 * carries real pressed-state semantics.
 */
const CHIP = '.t-filter-chips__chip'
const ACTIVE = '.t-filter-chips__chip--active'

const options = [
  { key: 'all', label: 'All', count: 12 },
  { key: 'contract', label: 'Contracts', count: 7 },
  { key: 'letter', label: 'Letters', count: 5 },
]

describe('TFilterChips', () => {
  it('renders one chip per option, in the order given, with label and count', () => {
    const w = mount(TFilterChips, { props: { options, modelValue: 'all' } })
    const chips = w.findAll(CHIP)
    expect(chips).toHaveLength(3)
    expect(chips.map((c) => c.text())).toEqual(['All12', 'Contracts7', 'Letters5'])
  })

  it('omits the count element when an option carries none, but renders a zero count', () => {
    const w = mount(TFilterChips, {
      props: { options: [{ key: 'a', label: 'A' }, { key: 'b', label: 'B', count: 0 }] },
    })
    const counts = w.findAll('.t-filter-chips__count')
    expect(counts).toHaveLength(1)
    expect(counts[0]!.text()).toBe('0')
  })

  it('renders the colour dot only for options that ask for one', () => {
    const w = mount(TFilterChips, {
      props: { options: [{ key: 'a', label: 'A', color: '#2f80ed' }, { key: 'b', label: 'B' }] },
    })
    const dots = w.findAll('.t-filter-chips__dot')
    expect(dots).toHaveLength(1)
    expect((dots[0]!.element as HTMLElement).style.background).toBe('#2f80ed')
  })

  it('marks exactly the selected chip active and pressed', () => {
    const w = mount(TFilterChips, { props: { options, modelValue: 'contract' } })
    expect(w.findAll(ACTIVE)).toHaveLength(1)
    expect(w.findAll(ACTIVE)[0]!.text()).toContain('Contracts')
    expect(w.findAll(CHIP).map((c) => c.attributes('aria-pressed'))).toEqual(['false', 'true', 'false'])
  })

  it('marks nothing active when the model is null', () => {
    const w = mount(TFilterChips, { props: { options, modelValue: null } })
    expect(w.findAll(ACTIVE)).toHaveLength(0)
  })

  it('emits the key of an unselected chip that is clicked', async () => {
    const w = mount(TFilterChips, { props: { options, modelValue: 'all' } })
    await w.findAll(CHIP)[2]!.trigger('click')
    expect(w.emitted('update:modelValue')).toEqual([['letter']])
  })

  describe('clicking the selected chip', () => {
    it('does nothing by default', async () => {
      const w = mount(TFilterChips, { props: { options, modelValue: 'contract' } })
      await w.findAll(CHIP)[1]!.trigger('click')
      expect(w.emitted('update:modelValue')).toBeUndefined()
    })

    it('clears the selection when `clearable`', async () => {
      const w = mount(TFilterChips, { props: { options, modelValue: 'contract', clearable: true } })
      await w.findAll(CHIP)[1]!.trigger('click')
      expect(w.emitted('update:modelValue')).toEqual([[null]])
    })

    it('still selects an unselected chip when `clearable`', async () => {
      const w = mount(TFilterChips, { props: { options, modelValue: 'contract', clearable: true } })
      await w.findAll(CHIP)[0]!.trigger('click')
      expect(w.emitted('update:modelValue')).toEqual([['all']])
    })
  })

  // The enforcement point for `disabled` is the native attribute, not a JS
  // guard - a disabled <button> dispatches no click. So this asserts the
  // attribute is really on the element (the thing that does the work) as well
  // as the absence of an emit.
  it('marks a disabled option disabled, and it emits nothing when clicked', async () => {
    const w = mount(TFilterChips, {
      props: { options: [{ key: 'a', label: 'A' }, { key: 'b', label: 'B', disabled: true }] },
    })
    const chip = w.findAll(CHIP)[1]!
    expect(chip.attributes('disabled')).toBeDefined()
    expect(w.findAll(CHIP)[0]!.attributes('disabled')).toBeUndefined()
    await chip.trigger('click')
    expect(w.emitted('update:modelValue')).toBeUndefined()
  })

  it('keeps numeric keys numeric (backend enums are usable without stringifying)', async () => {
    const w = mount(TFilterChips, {
      props: { options: [{ key: 0, label: 'Draft' }, { key: 1, label: 'Filed' }], modelValue: 0 },
    })
    await w.findAll(CHIP)[1]!.trigger('click')
    expect(w.emitted('update:modelValue')).toEqual([[1]])
  })

  describe('accessibility', () => {
    it('groups the chips under a named group, with real buttons inside', () => {
      const w = mount(TFilterChips, { props: { options, ariaLabel: 'Filter by type' } })
      const group = w.find('.t-filter-chips')
      expect(group.attributes('role')).toBe('group')
      expect(group.attributes('aria-label')).toBe('Filter by type')
      // Native buttons carry Enter/Space activation and the focus ring for free;
      // a `role="button"` div would need both hand-rolled.
      expect(w.findAll(CHIP).every((c) => c.element.tagName === 'BUTTON')).toBe(true)
      expect(w.findAll(CHIP).every((c) => c.attributes('type') === 'button')).toBe(true)
    })

    it('names the group even when the caller supplies nothing', () => {
      const w = mount(TFilterChips, { props: { options } })
      expect(w.find('.t-filter-chips').attributes('aria-label')).toBe('Filter')
    })
  })
})
