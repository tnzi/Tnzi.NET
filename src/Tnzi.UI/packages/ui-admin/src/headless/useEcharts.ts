import { onBeforeUnmount, onMounted, ref, shallowRef, watch, type Ref } from 'vue'
import * as echarts from 'echarts/core'
import {
  BarChart,
  LineChart,
  PieChart,
  RadarChart,
  ScatterChart,
} from 'echarts/charts'
import {
  GridComponent,
  LegendComponent,
  TitleComponent,
  TooltipComponent,
  DataZoomComponent,
  ToolboxComponent,
} from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import { useResizeObserver } from '@vueuse/core'
import { useTheme } from '@tnzi/ui'
import type { EChartsOption } from 'echarts'

// Register a useful subset of echarts modules. Consumers wanting more
// (Map, TreeMap, Sankey, etc.) call `echarts.use([...])` directly with
// the additional modules — the registry is global so this only needs
// to happen once per app.
echarts.use([
  BarChart,
  LineChart,
  PieChart,
  RadarChart,
  ScatterChart,
  GridComponent,
  LegendComponent,
  TitleComponent,
  TooltipComponent,
  DataZoomComponent,
  ToolboxComponent,
  CanvasRenderer,
])

export interface UseEchartsOptions {
  /**
   * Function returning the ECharts option spec. Called on mount and again
   * whenever the consumer calls `setOption()`. Receives the active theme mode
   * so consumers can adjust colors / grid backgrounds.
   */
  optionFactory: (mode: 'light' | 'dark') => EChartsOption
  /**
   * Renderer — `'canvas'` (default, faster for dynamic data) or `'svg'`
   * (better for static charts that need to scale or be exported).
   */
  renderer?: 'canvas' | 'svg'
  /** ECharts initialization theme name — typically not needed since we
   *  rebuild the option on mode change. */
  themeName?: string
}

export interface UseEchartsReturn {
  /** Container ref — bind to a div with explicit height. */
  containerRef: Ref<HTMLDivElement | null>
  /** Reactive chart instance ref (null until mounted). */
  chart: Ref<echarts.ECharts | null>
  /** Re-run the option factory and apply the result. */
  setOption: (notMerge?: boolean) => void
  /** Force the chart to resize against its container's current size. */
  resize: () => void
  /** Tear down — automatically called by `onBeforeUnmount`. */
  dispose: () => void
}

/**
 * Headless ECharts composable. Wires up:
 * - lifecycle (init on mount, dispose on unmount)
 * - theme reactivity (rebuilds option when `useTheme().mode.value` changes)
 * - automatic resize via ResizeObserver on the container
 *
 * Usage:
 * ```ts
 * const { containerRef } = useEcharts({
 *   optionFactory: (mode) => ({
 *     backgroundColor: 'transparent',
 *     xAxis: { type: 'category', data: ['A','B','C'] },
 *     yAxis: { type: 'value' },
 *     series: [{ type: 'bar', data: [10, 20, 30] }],
 *     textStyle: { color: mode === 'dark' ? '#ccc' : '#333' },
 *   }),
 * })
 * ```
 */
export function useEcharts(opts: UseEchartsOptions): UseEchartsReturn {
  const containerRef = ref<HTMLDivElement | null>(null)
  const chart = shallowRef<echarts.ECharts | null>(null)
  const theme = useTheme()

  function setOption(notMerge = false): void {
    if (!chart.value) return
    chart.value.setOption(opts.optionFactory(theme.resolvedMode.value), notMerge)
  }

  function resize(): void {
    chart.value?.resize()
  }

  function dispose(): void {
    if (chart.value) {
      chart.value.dispose()
      chart.value = null
    }
  }

  onMounted(() => {
    if (!containerRef.value) return
    // Wrap init: jsdom (used by vitest integration tests) has no canvas
    // backend, and echarts.init throws synchronously. Letting it bubble
    // crashes the consumer's mount cycle, so we degrade gracefully —
    // chart stays null, setOption / resize / dispose become no-ops.
    try {
      chart.value = echarts.init(containerRef.value, opts.themeName, {
        renderer: opts.renderer ?? 'canvas',
      })
      setOption(true)
    } catch {
      chart.value = null
    }
  })

  // Theme reactivity — rebuild option when the resolved mode toggles light/dark.
  watch(
    () => theme.resolvedMode.value,
    () => setOption(true),
  )

  // Auto-resize when the container resizes.
  useResizeObserver(containerRef, () => resize())

  onBeforeUnmount(() => {
    dispose()
  })

  return { containerRef, chart, setOption, resize, dispose }
}
