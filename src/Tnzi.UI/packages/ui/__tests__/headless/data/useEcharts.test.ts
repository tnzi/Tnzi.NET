import { describe, it, expect, vi, beforeEach } from 'vitest'
import { createApp, defineComponent, h, nextTick, ref } from 'vue'

const charts = vi.hoisted(() => ({
  init: vi.fn(),
  instances: [] as Array<{ el: HTMLElement; setOption: ReturnType<typeof vi.fn>; dispose: ReturnType<typeof vi.fn>; resize: ReturnType<typeof vi.fn> }>,
}))

vi.mock('echarts/core', () => ({
  use: vi.fn(),
  init: (el: HTMLElement) => {
    charts.init(el)
    const instance = { el, setOption: vi.fn(), dispose: vi.fn(), resize: vi.fn() }
    charts.instances.push(instance)
    return instance
  },
}))
vi.mock('echarts/charts', () => ({ BarChart: {}, LineChart: {}, PieChart: {}, RadarChart: {}, ScatterChart: {} }))
vi.mock('echarts/components', () => ({
  GridComponent: {}, LegendComponent: {}, TitleComponent: {}, TooltipComponent: {}, DataZoomComponent: {}, ToolboxComponent: {},
}))
vi.mock('echarts/renderers', () => ({ CanvasRenderer: {}, SVGRenderer: {} }))

import { useEcharts } from '../../../src/headless/data/useEcharts'

/**
 * A chart container that only renders once there is data (`v-if="hasData"`
 * with data that arrives after mount, as on a dashboard) was never
 * initialised: the chart was created in onMounted alone, when the container
 * did not exist yet, and the box stayed empty after the data came in.
 */
describe('useEcharts', () => {
  beforeEach(() => {
    charts.init.mockClear()
    charts.instances.length = 0
  })

  function mountLateHost() {
    const shown = ref(false)
    const Host = defineComponent({
      setup() {
        const { containerRef } = useEcharts({ optionFactory: () => ({ series: [] }), mode: ref('light') })
        return () => (shown.value ? h('div', { ref: containerRef, class: 'chart' }) : h('span'))
      },
    })
    const root = document.createElement('div')
    document.body.appendChild(root)
    const app = createApp(Host)
    app.mount(root)
    return { shown, root, unmount: () => { app.unmount(); root.remove() } }
  }

  it('initialises a container that appears after mount', async () => {
    const host = mountLateHost()
    expect(charts.init).not.toHaveBeenCalled()

    host.shown.value = true
    await nextTick()
    await nextTick()

    expect(charts.init).toHaveBeenCalledWith(host.root.querySelector('.chart'))
    expect(charts.instances[0]!.setOption).toHaveBeenCalled()
    host.unmount()
  })

  it('disposes the chart when its container goes away and starts over on a new one', async () => {
    const host = mountLateHost()
    host.shown.value = true
    await nextTick()
    await nextTick()

    host.shown.value = false
    await nextTick()
    await nextTick()
    expect(charts.instances[0]!.dispose).toHaveBeenCalled()

    host.shown.value = true
    await nextTick()
    await nextTick()
    expect(charts.instances).toHaveLength(2)
    expect(charts.instances[1]!.el).toBe(host.root.querySelector('.chart'))
    host.unmount()
    expect(charts.instances[1]!.dispose).toHaveBeenCalled()
  })
})
