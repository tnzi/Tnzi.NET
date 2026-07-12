<script setup lang="ts">
/**
 * `TWidgetPieChart` — reusable pie/donut chart widget.
 *
 * Use this for "distribution by category" panels — top sources, channel
 * split, value-by-owner, time-of-day breakdown. Donut by default (inner
 * radius 40%); pass `:donut="false"` for a flat pie.
 *
 * Presentation is customisable so apps don't re-roll their own echarts
 * wrapper just to change the palette / legend placement / tooltip text:
 *   - `palette` — brand slice colours.
 *   - `legend` — where the legend sits (`left` default / `bottom` / `right`
 *     / `top` / `none`). `bottom` uses a scrollable horizontal legend, handy
 *     for many small slices.
 *   - `valueFormatter` — format the tooltip value (e.g. money / percentage).
 *   - `radius` / `center` — override the donut geometry.
 */
import { computed, watch } from 'vue'
import { useEcharts } from '../../headless/useEcharts'
import type { EChartsOption } from 'echarts'
import type { ChartSeriesPoint } from '../../components/pages/TDashboardPage.vue'

export type PieLegendPosition = 'left' | 'right' | 'top' | 'bottom' | 'none'

interface Props {
  data: ChartSeriesPoint[]
  /** Body height in pixels. Default 240. */
  height?: number
  /** Donut (inner radius) vs flat pie. Default `true`. */
  donut?: boolean
  /** Slice colours — cycles through for each segment. Defaults to the echarts theme palette. */
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

const heightStyle = computed(() => ({ height: `${props.height}px` }))

const { containerRef, setOption } = useEcharts({
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
</script>

<template>
  <div ref="containerRef" class="t-widget-pie-chart" :style="heightStyle" />
</template>

<style scoped>
.t-widget-pie-chart {
  width: 100%;
}
</style>
