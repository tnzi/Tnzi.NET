<script setup lang="ts">
/**
 * `TWidgetPieChart` — reusable pie/donut chart widget.
 *
 * Use this for "distribution by category" panels — top sources, channel
 * split, time-of-day breakdown. Donut by default (inner radius 40%);
 * pass `:donut="false"` for a flat pie.
 */
import { computed, watch } from 'vue'
import { useEcharts } from '../../headless/useEcharts'
import type { EChartsOption } from 'echarts'
import type { ChartSeriesPoint } from '../../components/pages/TDashboardPage.vue'

interface Props {
  data: ChartSeriesPoint[]
  /** Body height in pixels. Default 240. */
  height?: number
  /** Donut (inner radius) vs flat pie. Default `true`. */
  donut?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  height: 240,
  donut: true,
})

const heightStyle = computed(() => ({ height: `${props.height}px` }))

const { containerRef, setOption } = useEcharts({
  optionFactory: (mode) => buildOption(mode),
})

function buildOption(mode: 'light' | 'dark'): EChartsOption {
  const textColor = mode === 'dark' ? '#d6d6d6' : '#444'
  return {
    backgroundColor: 'transparent',
    tooltip: { trigger: 'item' },
    legend: {
      orient: 'vertical',
      left: 'left',
      textStyle: { color: textColor },
    },
    series: [
      {
        type: 'pie',
        radius: props.donut ? ['40%', '70%'] : '70%',
        avoidLabelOverlap: false,
        label: { show: false },
        labelLine: { show: false },
        data: props.data,
      },
    ],
  }
}

watch(
  () => [props.data, props.donut],
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
