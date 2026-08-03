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
import { useTheme } from '../theme/useTheme'
import type { EChartsOption } from 'echarts'

// Idempotent registration: `echarts.use([...])` may be called any number of
// times - the registry is global and duplicate registrations are no-ops.
// Wrapping in a one-shot guard avoids the work, but the real reason it
// lives here (not at module top-level) is bundle-size: a top-level call
// is a *module side-effect*, which Rollup's tree-shaker can't drop even
// when `useEcharts` is unused. Moving the registration inside the
// composable body means consumers who never call `useEcharts()` pay
// nothing for echarts.
let registered = false
function ensureEchartsModulesRegistered(): void {
  if (registered) return
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
  registered = true
}

export interface UseEchartsOptions {
  /**
   * Function returning the ECharts option spec. Called on mount and again
   * whenever the consumer calls `setOption()`. Receives the active theme mode
   * so consumers can adjust colors / grid backgrounds.
   */
  optionFactory: (mode: 'light' | 'dark') => EChartsOption
  /**
   * Renderer - `'canvas'` (default, faster for dynamic data) or `'svg'`
   * (better for static charts that need to scale or be exported).
   */
  renderer?: 'canvas' | 'svg'
  /** ECharts initialization theme name - typically not needed since we
   *  rebuild the option on mode change. */
  themeName?: string
  /**
   * Explicit reactive theme mode source. When omitted the composable
   * pulls from `@tnzi/ui`'s `useTheme()` ThemeContext; when that throws
   * (no provider installed - e.g. a standalone consumer outside the
   * `createTnziUi` plugin shell) the composable falls back to a constant
   * `'light'` ref, which means dark-mode toggling on the consumer's side
   * will NOT propagate to the chart. Pass `mode` explicitly to wire up
   * your own reactive source.
   */
  mode?: Ref<'light' | 'dark'>
}

export interface UseEchartsReturn {
  /** Container ref - bind to a div with explicit height. */
  containerRef: Ref<HTMLDivElement | null>
  /** Reactive chart instance ref (null until mounted). */
  chart: Ref<echarts.ECharts | null>
  /** Re-run the option factory and apply the result. */
  setOption: (notMerge?: boolean) => void
  /** Force the chart to resize against its container's current size. */
  resize: () => void
  /** Tear down - automatically called by `onBeforeUnmount`. */
  dispose: () => void
}

/**
 * Resolve the active theme mode. Order of precedence:
 *   1. Explicit `opts.mode` ref supplied by the consumer (always wins).
 *   2. The `useTheme()` ThemeContext's `resolvedMode` ComputedRef.
 *   3. A constant `ref('light')` fallback (charts stuck in light mode).
 *
 * The try/catch around `useTheme()` is what enables standalone usage
 * outside the @tnzi/ui plugin shell, but the fallback is NON-reactive -
 * standalone consumers who want dark-mode reactivity MUST supply
 * `opts.mode` explicitly.
 */
function resolveThemeRef(opts: UseEchartsOptions): Ref<'light' | 'dark'> {
  if (opts.mode) return opts.mode
  try {
    const theme = useTheme()
    return theme.resolvedMode as Ref<'light' | 'dark'>
  } catch {
    return ref<'light' | 'dark'>('light')
  }
}

/**
 * Headless ECharts composable. Wires up:
 * - lifecycle (init on mount, dispose on unmount)
 * - theme reactivity (rebuilds option when `useTheme().resolvedMode` changes
 *   OR when an explicit `opts.mode` ref changes)
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
 *
 * Standalone usage (no `createTnziUi` plugin):
 * ```ts
 * const myDarkMode = ref<'light' | 'dark'>('light')
 * useEcharts({ optionFactory, mode: myDarkMode })
 * ```
 *
 * Sunk from `@tnzi/ui-admin` in 0.2.x. `echarts` is a peer dependency -
 * consumers install it themselves (admin / dashboard apps usually do
 * anyway).
 */
export function useEcharts(opts: UseEchartsOptions): UseEchartsReturn {
  // Lazy module registration - runs at first call, idempotent. This is
  // what lets consumers who never call useEcharts() avoid pulling echarts
  // chart/component code into their bundle.
  ensureEchartsModulesRegistered()

  const containerRef = ref<HTMLDivElement | null>(null)
  const chart = shallowRef<echarts.ECharts | null>(null)
  const modeRef = resolveThemeRef(opts)

  function setOption(notMerge = false): void {
    if (!chart.value) return
    chart.value.setOption(opts.optionFactory(modeRef.value), notMerge)
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
    // crashes the consumer's mount cycle, so we degrade gracefully -
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

  // Theme reactivity - rebuild option when the resolved mode toggles light/dark.
  watch(modeRef, () => setOption(true))

  // Auto-resize when the container resizes.
  useResizeObserver(containerRef, () => resize())

  onBeforeUnmount(() => {
    dispose()
  })

  return { containerRef, chart, setOption, resize, dispose }
}
