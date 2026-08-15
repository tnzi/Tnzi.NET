<script setup lang="ts">
/**
 * `TWidgetPieChart` - reusable pie/donut chart widget.
 *
 * Use this for "distribution by category" panels - top sources, channel
 * split, value-by-owner, time-of-day breakdown. Donut by default (inner
 * radius 40%); pass `:donut="false"` for a flat pie.
 *
 * Presentation is customisable so apps don't re-roll their own echarts
 * wrapper just to change the palette / legend placement / tooltip text:
 *   - `palette` - brand slice colours.
 *   - `legend` - where the legend sits (`left` default / `bottom` / `right`
 *     / `top` / `none`). `bottom` uses a scrollable horizontal legend, handy
 *     for many small slices.
 *   - `valueFormatter` - format the tooltip value (e.g. money / percentage).
 *   - `radius` / `center` - override the donut geometry.
 *
 * ## Interaction
 *
 * A distribution chart answers "how much per category"; the next question is
 * always "which records make up this slice". Two events carry that through to
 * the host, so a drill-down does not require re-rolling the echarts wrapper:
 *
 * ```vue
 * <TWidgetPieChart
 *   ref="chartRef" :data="byStaff"
 *   @slice-click="openDrilldown"
 *   @legend-click="onLegendClick"
 * />
 * ```
 * ```ts
 * function openDrilldown(e: PieSliceClickEvent) {
 *   // `e.index` maps back to the original `data` entry even when `name` is a
 *   // localised label rather than a stable key.
 *   loadFilesFor(byStaff.value[e.index])
 * }
 *
 * function onLegendClick(e: PieLegendClickEvent) {
 *   e.preventDefault()          // suppress the built-in show/hide toggle
 *   openMenu({ x: e.clientX, y: e.clientY }, [
 *     // the default toggle, now under the host's control
 *     { label: e.visible ? 'Hide slice' : 'Show slice',
 *       run: () => chartRef.value?.setSliceVisible(e.name, !e.visible) },
 *     { label: 'View records', run: () => loadFilesFor(byStaff.value[e.index]) },
 *   ])
 * }
 * ```
 *
 * `preventDefault()` must be called synchronously (DOM semantics) - the
 * built-in toggle is restored during the same task, so awaiting a menu choice
 * first is too late. Not calling it leaves the show/hide toggle exactly as it
 * has always behaved.
 */
import { computed, onBeforeUnmount, onMounted, watch } from 'vue'
import { useEcharts } from '../../headless/useEcharts'
import type { EChartsOption } from 'echarts'
import type { ChartSeriesPoint } from '../pages/TDashboardPage.vue'

export type PieLegendPosition = 'left' | 'right' | 'top' | 'bottom' | 'none'

/** Payload of `slice-click` - a segment of the pie was clicked. */
export interface PieSliceClickEvent {
  /** The clicked data point's `name`. */
  name: string
  /** The clicked data point's `value`. */
  value: number
  /**
   * Position of the point in the `data` prop. Use this rather than `name` to
   * map back to your own records - `name` is often a localised display label,
   * not a stable key.
   */
  index: number
  /** Share of the total as echarts computed it (0-100). */
  percent: number
  /** The `data` entry itself, when `index` still resolves against `data`. */
  item?: ChartSeriesPoint
  /**
   * Viewport coordinates of the click, for positioning a context menu.
   * `null` when the change did not come from a pointer (a programmatic
   * `dispatchAction`).
   */
  clientX: number | null
  clientY: number | null
  /**
   * The pointer event that produced the click. `null` for a programmatic
   * change, and also on the touch path (a `TouchEvent` is not a `MouseEvent`)
   * - prefer `clientX` / `clientY`, which are populated for both.
   */
  nativeEvent: MouseEvent | null
}

/** Payload of `legend-click` - a legend entry was clicked. */
export interface PieLegendClickEvent {
  /** The clicked legend entry's name (= the data point's `name`). */
  name: string
  /**
   * Position of the matching entry in the `data` prop, or `-1` when the legend
   * name no longer matches any entry (data replaced mid-interaction).
   */
  index: number
  /**
   * Whether the slice is currently visible. This is the state *before* the
   * built-in toggle - i.e. what it stays as when you call `preventDefault()`.
   */
  visible: boolean
  /** The `data` entry itself, when `index` still resolves against `data`. */
  item?: ChartSeriesPoint
  /**
   * Viewport coordinates of the click, for positioning a context menu.
   * `null` when the toggle was dispatched programmatically.
   */
  clientX: number | null
  clientY: number | null
  /**
   * The pointer event that produced the click. `null` for a programmatic
   * toggle, and also on the touch path (a `TouchEvent` is not a `MouseEvent`)
   * - prefer `clientX` / `clientY`, which are populated for both.
   */
  nativeEvent: MouseEvent | null
  /**
   * Suppress the built-in show/hide toggle so the host owns the interaction.
   * MUST be called synchronously. Use the exposed `setSliceVisible()` to
   * perform the toggle later on the host's terms.
   */
  preventDefault: () => void
}

interface Props {
  data: ChartSeriesPoint[]
  /** Body height in pixels. Default 240. */
  height?: number
  /** Donut (inner radius) vs flat pie. Default `true`. */
  donut?: boolean
  /** Slice colours - cycles through for each segment. Defaults to the echarts theme palette. */
  palette?: string[]
  /** Legend placement. Default `left`. `bottom` = scrollable horizontal legend. */
  legend?: PieLegendPosition
  /**
   * Format the tooltip value. Receives the raw value, slice name and echarts
   * percent. When omitted the default `name: value (percent%)` tooltip is used.
   */
  valueFormatter?: (value: number, name: string, percent: number) => string
  /** Override the donut/pie radius (echarts `series.radius`). */
  radius?: [string, string] | string
  /** Override the pie centre (echarts `series.center`). Default `['50%', '50%']`. */
  center?: [string, string]
}

const props = withDefaults(defineProps<Props>(), {
  height: 240,
  donut: true,
  palette: undefined,
  legend: 'left',
  valueFormatter: undefined,
  radius: undefined,
  center: undefined,
})

const emit = defineEmits<{
  /** A slice was clicked - drill into the records behind it. */
  (e: 'slice-click', payload: PieSliceClickEvent): void
  /**
   * A legend entry was clicked. Call `payload.preventDefault()` to take the
   * interaction over from the built-in show/hide toggle.
   */
  (e: 'legend-click', payload: PieLegendClickEvent): void
}>()

const heightStyle = computed(() => ({ height: `${props.height}px` }))

const { containerRef, chart, setOption } = useEcharts({
  optionFactory: (mode) => buildOption(mode),
})

function legendOption(textColor: string): EChartsOption['legend'] {
  switch (props.legend) {
    case 'none':
      return { show: false }
    case 'top':
      return { orient: 'horizontal', top: 0, textStyle: { color: textColor } }
    case 'bottom':
      return {
        type: 'scroll',
        orient: 'horizontal',
        bottom: 0,
        itemWidth: 11,
        itemHeight: 11,
        textStyle: { color: textColor, fontSize: 11 },
      }
    case 'right':
      return { orient: 'vertical', right: 0, textStyle: { color: textColor } }
    case 'left':
    default:
      return { orient: 'vertical', left: 'left', textStyle: { color: textColor } }
  }
}

function buildOption(mode: 'light' | 'dark'): EChartsOption {
  const textColor = mode === 'dark' ? '#d6d6d6' : '#444'
  const borderColor = mode === 'dark' ? '#1f2937' : '#ffffff'
  const radius = props.radius ?? (props.donut ? ['40%', '70%'] : '70%')
  return {
    backgroundColor: 'transparent',
    color: props.palette,
    tooltip: {
      trigger: 'item',
      // echarts types the tooltip param as a broad union; narrow it here since
      // a single-item pie tooltip always carries name/value/percent.
      formatter: props.valueFormatter
        ? (p: unknown) => {
            const d = p as { name: string; value: number; percent: number }
            return props.valueFormatter!(d.value, d.name, d.percent)
          }
        : undefined,
    },
    legend: legendOption(textColor),
    series: [
      {
        type: 'pie',
        radius,
        center: props.center ?? ['50%', '50%'],
        avoidLabelOverlap: true,
        itemStyle: { borderColor, borderWidth: 2 },
        label: { show: false },
        labelLine: { show: false },
        data: props.data,
      },
    ],
  }
}

watch(
  () => [props.data, props.donut, props.palette, props.legend, props.radius, props.center],
  () => setOption(true),
  { deep: true },
)

/* -------------------------------------------------------------------------- */
/* Interaction                                                                */
/* -------------------------------------------------------------------------- */

/** The subset of echarts' mouse-event payload a single-series pie carries. */
interface EchartsClickParams {
  componentType?: string
  dataIndex?: number
  name?: string
  value?: unknown
  percent?: number
}

/** The `legendselectchanged` payload - post-toggle visibility per legend name. */
interface EchartsLegendSelectChangedParams {
  name?: string
  selected?: Record<string, boolean>
}

// `legendselectchanged` carries no pointer information, yet a host that wants
// to open a menu at the cursor needs it. So the pointer is picked up straight
// off the DOM event that echarts is in the middle of processing:
//
//   container, CAPTURE  -> remember   (runs before anything inside)
//     zrender's listener on its own viewport div, bubble
//       -> legend item click -> legendToggleSelect -> legendselectchanged
//   container, BUBBLE   -> forget     (runs after everything inside)
//
// The window is exactly one DOM dispatch, so a later programmatic
// `dispatchAction` can never be reported as "the user just clicked".
//
// ★ Do NOT clear this on a microtask: the HTML spec runs a microtask
// checkpoint BETWEEN event listener invocations (the JS stack empties after
// each one), so a `queueMicrotask` clear fires before zrender's listener ever
// runs and every payload gets `nativeEvent: null`. Measured in Chrome:
// `capture:set | microtask:cleared | descendantBubble:sees=NULL`.
//
// ★ `touchend` is listened to as well because zrender's touch branch
// (no PointerEvent support) synthesises the element click INSIDE the
// `touchend` dispatch - the DOM `click` has not happened yet at that point.
const POINTER_EVENT_TYPES = ['click', 'touchend'] as const

let lastPointer: MouseEvent | null = null
let lastPoint: { x: number; y: number } | null = null
let forgetTimer: ReturnType<typeof setTimeout> | undefined

function pointOf(e: Event): { x: number; y: number } | null {
  const mouse = e as MouseEvent
  if (typeof mouse.clientX === 'number') return { x: mouse.clientX, y: mouse.clientY }
  const touch = (e as TouchEvent).changedTouches?.[0]
  return touch ? { x: touch.clientX, y: touch.clientY } : null
}

function rememberPointer(e: Event): void {
  // `nativeEvent` stays a MouseEvent - a TouchEvent has no clientX, so it is
  // reported through the coordinates only rather than widening the type (which
  // would break every existing `e.nativeEvent?.clientX` read).
  lastPointer = typeof (e as MouseEvent).clientX === 'number' ? (e as MouseEvent) : null
  lastPoint = pointOf(e)
  // Backstop: the bubble-phase clear never runs if something inside the chart
  // stops propagation. A stale pointer reported as fresh is the one failure
  // mode worth spending a timer on.
  clearTimeout(forgetTimer)
  forgetTimer = setTimeout(forgetPointer, 0)
}

function forgetPointer(): void {
  lastPointer = null
  lastPoint = null
  clearTimeout(forgetTimer)
  forgetTimer = undefined
}

function indexOfName(name: string): number {
  return props.data?.findIndex((d) => d.name === name) ?? -1
}

function onSeriesClick(params: EchartsClickParams): void {
  // Only pie segments - other components (title, legend when `triggerEvent`
  // is on) share the same `click` channel.
  if (params.componentType !== 'series') return
  const index = params.dataIndex ?? -1
  const item = index >= 0 ? props.data?.[index] : undefined
  emit('slice-click', {
    name: params.name ?? item?.name ?? '',
    value: typeof params.value === 'number' ? params.value : (item?.value ?? 0),
    index,
    percent: params.percent ?? 0,
    item,
    clientX: lastPoint?.x ?? null,
    clientY: lastPoint?.y ?? null,
    nativeEvent: lastPointer,
  })
}

function onLegendSelectChanged(params: EchartsLegendSelectChangedParams): void {
  const name = params.name
  if (!name) return
  // echarts has already applied the toggle by the time this fires, so
  // `selected` is the post-click state and the pre-click state is its inverse.
  const visibleAfter = params.selected?.[name] !== false
  const index = indexOfName(name)
  let prevented = false

  emit('legend-click', {
    name,
    index,
    visible: !visibleAfter,
    item: index >= 0 ? props.data?.[index] : undefined,
    clientX: lastPoint?.x ?? null,
    clientY: lastPoint?.y ?? null,
    nativeEvent: lastPointer,
    preventDefault: () => {
      prevented = true
    },
  })

  if (!prevented) return
  // Put the slice back. `legendSelect` / `legendUnSelect` emit
  // `legendselected` / `legendunselected` - NOT `legendselectchanged` - so
  // this restore cannot re-enter the handler above. It also lands in the same
  // task as the click, so the reverted state is what the browser paints.
  setSliceVisible(name, !visibleAfter)
}

/**
 * Show or hide a slice - the same effect as the built-in legend toggle.
 * Exposed so a host that took the legend interaction over with
 * `preventDefault()` can still perform it on its own terms.
 */
function setSliceVisible(name: string, visible: boolean): void {
  chart.value?.dispatchAction({ type: visible ? 'legendSelect' : 'legendUnSelect', name })
}

watch(
  chart,
  (instance) => {
    if (!instance) return
    instance.on('click', (params: unknown) => onSeriesClick(params as EchartsClickParams))
    instance.on('legendselectchanged', (params: unknown) =>
      onLegendSelectChanged(params as EchartsLegendSelectChangedParams),
    )
  },
  { immediate: true },
)

onMounted(() => {
  const el = containerRef.value
  if (!el) return
  for (const type of POINTER_EVENT_TYPES) {
    el.addEventListener(type, rememberPointer, true)
    el.addEventListener(type, forgetPointer, false)
  }
})

onBeforeUnmount(() => {
  const el = containerRef.value
  for (const type of POINTER_EVENT_TYPES) {
    el?.removeEventListener(type, rememberPointer, true)
    el?.removeEventListener(type, forgetPointer, false)
  }
  forgetPointer()
})

defineExpose({
  setSliceVisible,
  /**
   * The underlying echarts instance (`null` before mount / where canvas is
   * unavailable). Escape hatch for capabilities this widget does not wrap -
   * prefer `setSliceVisible()` for visibility, and note that dispatching
   * `legendToggleSelect` yourself re-enters `legend-click`.
   */
  chart,
})
</script>

<template>
  <div ref="containerRef" class="t-widget-pie-chart" :style="heightStyle" />
</template>

<style scoped>
.t-widget-pie-chart {
  width: 100%;
}
</style>
