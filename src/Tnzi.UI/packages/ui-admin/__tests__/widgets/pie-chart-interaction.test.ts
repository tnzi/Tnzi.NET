/**
 * `TWidgetPieChart` interaction surface - slice drill-down + host-owned legend.
 *
 * These have to go through a mocked `useEcharts`: the real one calls
 * `echarts.init`, which throws in happy-dom (no canvas backend) and leaves
 * `chart` null, so no handler would ever be bound and every assertion here
 * would pass vacuously.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref, shallowRef } from 'vue'

/** Handlers the component registered on the chart instance, by event name. */
const handlers = new Map<string, (params: unknown) => void>()
/** Actions the component dispatched back into echarts. */
const dispatched: Array<Record<string, unknown>> = []

const fakeChart = {
  on(event: string, handler: (params: unknown) => void) {
    handlers.set(event, handler)
  },
  dispatchAction(payload: Record<string, unknown>) {
    dispatched.push(payload)
  },
}

vi.mock('../../src/headless/useEcharts', () => ({
  useEcharts: () => ({
    containerRef: ref<HTMLDivElement | null>(null),
    chart: shallowRef(fakeChart),
    setOption: vi.fn(),
    resize: vi.fn(),
    dispose: vi.fn(),
  }),
}))

import TWidgetPieChart from '../../src/components/widgets/TWidgetPieChart.vue'
import type {
  PieLegendClickEvent,
  PieSliceClickEvent,
} from '../../src/components/widgets/TWidgetPieChart.vue'

const DATA = [
  { name: 'Alice', value: 10 },
  { name: 'Bob', value: 30 },
]

function mountChart(listeners: Record<string, unknown> = {}) {
  return mount(TWidgetPieChart, { props: { data: DATA, ...listeners } })
}

/**
 * Dispatch a real click the way zrender receives one: on a child of the chart
 * container, so the component's capture listener runs first and its bubble
 * listener runs last. `duringDispatch` stands in for zrender's own bubble
 * listener - that is where echarts turns the click into its events, and the
 * pointer has to still be readable at exactly that point.
 *
 * ★ The browser also runs a microtask checkpoint between listeners; happy-dom
 * does not, so this cannot reproduce the `queueMicrotask` bug that made every
 * payload `null`. That one is pinned by the Chrome measurement recorded in the
 * component's comment, not by this test.
 */
function clickInside(
  wrapper: ReturnType<typeof mountChart>,
  duringDispatch: (e: MouseEvent) => void,
): MouseEvent {
  const zrenderViewport = document.createElement('div')
  wrapper.element.appendChild(zrenderViewport)
  const native = new MouseEvent('click', { bubbles: true, clientX: 11, clientY: 22 })
  zrenderViewport.addEventListener('click', () => duringDispatch(native))
  zrenderViewport.dispatchEvent(native)
  return native
}

/** Fire what echarts would fire for a click on a pie segment. */
function fireSeriesClick(params: Record<string, unknown>): void {
  handlers.get('click')?.(params)
}

/** Fire what echarts fires AFTER it has already applied the legend toggle. */
function fireLegendToggle(name: string, selected: Record<string, boolean>): void {
  handlers.get('legendselectchanged')?.({ name, selected })
}

describe('TWidgetPieChart interaction', () => {
  beforeEach(() => {
    handlers.clear()
    dispatched.length = 0
  })

  it('binds the chart handlers it needs', () => {
    mountChart()
    expect([...handlers.keys()].sort()).toEqual(['click', 'legendselectchanged'])
  })

  describe('slice-click', () => {
    it('reports name, value, percent and the position in `data`', () => {
      const onSliceClick = vi.fn()
      mountChart({ onSliceClick })

      fireSeriesClick({
        componentType: 'series',
        dataIndex: 1,
        name: 'Bob',
        value: 30,
        percent: 75,
      })

      expect(onSliceClick).toHaveBeenCalledTimes(1)
      const payload = onSliceClick.mock.calls[0][0] as PieSliceClickEvent
      expect(payload.name).toBe('Bob')
      expect(payload.value).toBe(30)
      expect(payload.index).toBe(1)
      expect(payload.percent).toBe(75)
      expect(payload.item).toEqual({ name: 'Bob', value: 30 })
    })

    it('ignores clicks that are not on a series (title / legend / axis)', () => {
      const onSliceClick = vi.fn()
      mountChart({ onSliceClick })

      fireSeriesClick({ componentType: 'title', name: 'Bob' })
      fireSeriesClick({ componentType: 'legend', name: 'Bob', dataIndex: 1 })

      expect(onSliceClick).not.toHaveBeenCalled()
    })

    it('carries the pointer that produced the click', () => {
      const onSliceClick = vi.fn()
      const wrapper = mountChart({ onSliceClick })

      const native = clickInside(wrapper, () =>
        fireSeriesClick({ componentType: 'series', dataIndex: 0, name: 'Alice', value: 10 }),
      )

      const payload = onSliceClick.mock.calls[0][0] as PieSliceClickEvent
      expect(payload.clientX).toBe(11)
      expect(payload.clientY).toBe(22)
      expect(payload.nativeEvent).toBe(native)
    })

    it('does not report a stale pointer for a later programmatic event', () => {
      const onSliceClick = vi.fn()
      const wrapper = mountChart({ onSliceClick })

      // A full dispatch happens, then the chart is driven programmatically.
      clickInside(wrapper, () => undefined)
      fireSeriesClick({ componentType: 'series', dataIndex: 0, name: 'Alice', value: 10 })

      const payload = onSliceClick.mock.calls[0][0] as PieSliceClickEvent
      expect(payload.clientX).toBeNull()
      expect(payload.nativeEvent).toBeNull()
    })

    it('falls back to a macrotask clear when propagation is stopped', async () => {
      const onSliceClick = vi.fn()
      const wrapper = mountChart({ onSliceClick })

      // Nothing clears on the way out, so only the backstop can.
      clickInside(wrapper, (e) => e.stopPropagation())
      await new Promise((r) => setTimeout(r, 1))
      fireSeriesClick({ componentType: 'series', dataIndex: 0, name: 'Alice', value: 10 })

      expect((onSliceClick.mock.calls[0][0] as PieSliceClickEvent).clientX).toBeNull()
    })
  })

  describe('legend-click', () => {
    it('reports the pre-toggle visibility and the position in `data`', () => {
      const onLegendClick = vi.fn()
      mountChart({ onLegendClick })

      // echarts just hid Bob, so Bob WAS visible when the user clicked.
      fireLegendToggle('Bob', { Alice: true, Bob: false })

      const payload = onLegendClick.mock.calls[0][0] as PieLegendClickEvent
      expect(payload.name).toBe('Bob')
      expect(payload.index).toBe(1)
      expect(payload.visible).toBe(true)
      expect(payload.item).toEqual({ name: 'Bob', value: 30 })
    })

    it('carries the pointer, so the host can place a menu at the cursor', () => {
      const onLegendClick = vi.fn()
      const wrapper = mountChart({ onLegendClick })

      const native = clickInside(wrapper, () =>
        fireLegendToggle('Bob', { Alice: true, Bob: false }),
      )

      const payload = onLegendClick.mock.calls[0][0] as PieLegendClickEvent
      expect(payload.clientX).toBe(11)
      expect(payload.clientY).toBe(22)
      expect(payload.nativeEvent).toBe(native)
    })

    it('reports index -1 when the legend name is no longer in `data`', () => {
      const onLegendClick = vi.fn()
      mountChart({ onLegendClick })

      fireLegendToggle('Carol', { Carol: false })

      const payload = onLegendClick.mock.calls[0][0] as PieLegendClickEvent
      expect(payload.index).toBe(-1)
      expect(payload.item).toBeUndefined()
    })

    it('leaves the built-in toggle alone when the host does not take over', () => {
      mountChart({ onLegendClick: vi.fn() })

      fireLegendToggle('Bob', { Alice: true, Bob: false })

      expect(dispatched).toEqual([])
    })

    it('leaves the built-in toggle alone when nobody listens at all', () => {
      mountChart()

      fireLegendToggle('Bob', { Alice: true, Bob: false })

      expect(dispatched).toEqual([])
    })

    it('restores a hidden slice when the host calls preventDefault()', () => {
      mountChart({
        onLegendClick: (e: PieLegendClickEvent) => e.preventDefault(),
      })

      fireLegendToggle('Bob', { Alice: true, Bob: false })

      // `legendSelect` (not `legendToggleSelect`) - it emits `legendselected`,
      // so the restore cannot re-enter the handler.
      expect(dispatched).toEqual([{ type: 'legendSelect', name: 'Bob' }])
    })

    it('re-hides a slice when preventDefault() cancels a re-show', () => {
      mountChart({
        onLegendClick: (e: PieLegendClickEvent) => e.preventDefault(),
      })

      // echarts just re-showed Bob, so Bob WAS hidden.
      fireLegendToggle('Bob', { Alice: true, Bob: true })

      expect(dispatched).toEqual([{ type: 'legendUnSelect', name: 'Bob' }])
    })
  })

  describe('setSliceVisible', () => {
    it('lets a host that took the legend over perform the toggle itself', () => {
      const wrapper = mountChart()
      const vm = wrapper.vm as unknown as {
        setSliceVisible: (name: string, visible: boolean) => void
      }

      vm.setSliceVisible('Bob', false)
      vm.setSliceVisible('Bob', true)

      expect(dispatched).toEqual([
        { type: 'legendUnSelect', name: 'Bob' },
        { type: 'legendSelect', name: 'Bob' },
      ])
    })
  })
})
