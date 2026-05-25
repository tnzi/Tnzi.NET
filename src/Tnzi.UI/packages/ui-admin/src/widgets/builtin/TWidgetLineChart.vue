<script setup lang="ts">
/**
 * `TWidgetLineChart` — reusable line chart widget.
 *
 * Wraps `useEcharts` so consumers can drop a small line/area chart into
 * any Workbench cell with just data. Reactively re-renders on prop
 * changes and adapts colors to the active theme mode (light/dark).
 */
import { computed, watch } from 'vue'
import { useEcharts } from '../../headless/useEcharts'
import type { EChartsOption } from 'echarts'

interface Series {
  name: string
  data: number[]
}

interface Props {
  categories: string[]
  series: Series[]
  /** Body height in pixels. Default 240. */
  height?: number
  /** Show area fill below the line. Default `true`. */
  area?: boolean
  /** Smooth (spline) lines. Default `true`. */
  smooth?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  height: 240,
  area: true,
  smooth: true,
})

const heightStyle = computed(() => ({ height: `${props.height}px` }))

const { containerRef, setOption } = useEcharts({
  optionFactory: (mode) => buildOption(mode),
})

function buildOption(mode: 'light' | 'dark'): EChartsOption {
  const textColor = mode === 'dark' ? '#d6d6d6' : '#444'
  const gridLine = mode === 'dark' ? '#3a3a3a' : '#eee'
  return {
    backgroundColor: 'transparent',
    grid: { left: 36, right: 16, top: 28, bottom: 28 },
    tooltip: { trigger: 'axis' },
    legend: { right: 0, top: 0, textStyle: { color: textColor } },
    xAxis: {
      type: 'category',
      data: props.categories,
      axisLabel: { color: textColor },
      axisLine: { lineStyle: { color: gridLine } },
    },
    yAxis: {
      type: 'value',
      axisLabel: { color: textColor },
      splitLine: { lineStyle: { color: gridLine } },
    },
    series: props.series.map((s) => ({
      name: s.name,
      data: s.data,
      type: 'line',
      smooth: props.smooth,
      symbolSize: 6,
      lineStyle: { width: 2 },
      areaStyle: props.area ? { opacity: 0.12 } : undefined,
    })),
  }
}

// Re-render when reactive props change. useEcharts internally watches the
// theme mode only, so explicit setOption() keeps the chart in sync with
// reactive data without relying on a deep-watch on series arrays.
watch(
  () => [props.categories, props.series, props.area, props.smooth],
  () => setOption(true),
  { deep: true },
)
</script>

<template>
  <div ref="containerRef" class="t-widget-line-chart" :style="heightStyle" />
</template>

<style scoped>
.t-widget-line-chart {
  width: 100%;
}
</style>
