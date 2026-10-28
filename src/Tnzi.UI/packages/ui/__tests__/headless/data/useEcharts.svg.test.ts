import { describe, it, expect } from 'vitest'
import { createApp, defineComponent, h, nextTick, ref } from 'vue'
import { useEcharts } from '../../../src/headless/data/useEcharts'

/**
 * `renderer: 'svg'` is a documented option, but only the canvas renderer was
 * registered: zrender threw "Renderer 'svg' is not imported", the init guard
 * swallowed it, and the chart box stayed empty with no error anywhere. Real
 * echarts here (no mocks): the SVG renderer needs no canvas, so it renders
 * under happy-dom.
 */
describe('useEcharts - SVG renderer', () => {
  it('draws into an <svg> when renderer is svg', async () => {
    const Host = defineComponent({
      setup() {
        const { containerRef } = useEcharts({
          optionFactory: () => ({
            xAxis: { type: 'category', data: ['a', 'b'] },
            yAxis: { type: 'value' },
            series: [{ type: 'bar', data: [1, 2] }],
          }),
          renderer: 'svg',
          mode: ref('light'),
        })
        return () => h('div', { ref: containerRef, style: 'width: 400px; height: 300px' })
      },
    })
    const root = document.createElement('div')
    document.body.appendChild(root)
    const app = createApp(Host)
    app.mount(root)
    await nextTick()
    await nextTick()

    expect(root.querySelector('svg')).not.toBeNull()

    app.unmount()
    root.remove()
  })
})
