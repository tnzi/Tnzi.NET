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

  it('renders the secondary meta text only when the item carries one', () => {
    const w = mount(TMetricBars, {
      props: { items: [{ label: 'A', value: 1, display: '$1', meta: '5 files' }, { label: 'B', value: 1 }] },
    })
    const metas = w.findAll('.t-metric-bars__meta')
    expect(metas).toHaveLength(1)
    expect(metas[0]!.text()).toBe('5 files')
  })

  /**
   * The drill-down surface. The point of these is the OFF case as much as the on
   * one: three call sites render this as a pure display list, and they must not
   * grow a tab stop or a pointer cursor because the component learned to click.
   */
  describe('row-click', () => {
    const items = [
      { label: 'Jane', value: 10, display: '$10' },
      { label: 'Sam', value: 5, display: '$5' },
    ]

    it('is inert by default: no button role, no tab stop, no event', async () => {
      const w = mount(TMetricBars, { props: { items } })
      const row = w.findAll('.t-metric-bars__row')[0]!
      expect(row.attributes('role')).toBeUndefined()
      expect(row.attributes('tabindex')).toBeUndefined()
      expect(row.classes()).not.toContain('t-metric-bars__row--clickable')

      await row.trigger('click')
      await row.trigger('keydown', { key: 'Enter' })
      expect(w.emitted('row-click')).toBeUndefined()
    })

    it('turns rows into keyboard-reachable controls when clickable', () => {
      const w = mount(TMetricBars, { props: { items, clickable: true } })
      for (const row of w.findAll('.t-metric-bars__row')) {
        expect(row.attributes('role')).toBe('button')
        expect(row.attributes('tabindex')).toBe('0')
        expect(row.classes()).toContain('t-metric-bars__row--clickable')
      }
    })

    it('emits the item, its index in `items`, and the pointer position', async () => {
      const w = mount(TMetricBars, { props: { items, clickable: true } })
      await w.findAll('.t-metric-bars__row')[1]!.trigger('click', { clientX: 120, clientY: 42 })

      const payload = w.emitted('row-click')?.[0]?.[0] as {
        item: { label: string }
        index: number
        clientX: number
        clientY: number
        nativeEvent: Event
      }
      expect(payload.index).toBe(1)
      expect(payload.item.label).toBe('Sam')
      expect(payload.clientX).toBe(120)
      expect(payload.clientY).toBe(42)
      expect(payload.nativeEvent.type).toBe('click')
    })

    it.each([['Enter'], [' ']])('activates on %s and anchors to the row', async (key) => {
      const w = mount(TMetricBars, { props: { items, clickable: true } })
      const row = w.findAll('.t-metric-bars__row')[0]!
      // happy-dom lays nothing out, so the rect the handler reads is stubbed -
      // the assertion is that it anchors to the ROW, not that layout works.
      ;(row.element as HTMLElement).getBoundingClientRect = () =>
        ({ left: 10, bottom: 30 }) as DOMRect

      await row.trigger('keydown', { key })

      const payload = w.emitted('row-click')?.[0]?.[0] as { index: number; clientX: number; clientY: number }
      expect(payload.index).toBe(0)
      expect(payload.clientX).toBe(10)
      expect(payload.clientY).toBe(30)
    })

    it('ignores keys that do not activate a button', async () => {
      const w = mount(TMetricBars, { props: { items, clickable: true } })
      const row = w.findAll('.t-metric-bars__row')[0]!
      await row.trigger('keydown', { key: 'Tab' })
      await row.trigger('keydown', { key: 'a' })
      expect(w.emitted('row-click')).toBeUndefined()
    })
  })
})
