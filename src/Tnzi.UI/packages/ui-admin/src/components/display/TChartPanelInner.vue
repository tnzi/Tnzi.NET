<template>
  <div ref="containerRef" class="t-chart-panel" :style="containerStyle" />
</template>

<script setup lang="ts">
import { computed, watch } from 'vue'
import type { CSSProperties } from 'vue'
import type { EChartsOption } from 'echarts'
import { useEcharts } from '../../headless/useEcharts'

/** Internal real-echarts implementation. Loaded only when canvas + ResizeObserver
 *  are available - see TChartPanel.vue for the lazy-load guard. */
interface Props {
  option: EChartsOption | null | undefined
  height?: string | number
  renderer?: 'canvas' | 'svg'
}

const props = withDefaults(defineProps<Props>(), {
  height: 280,
  renderer: 'canvas',
})

const containerStyle = computed<CSSProperties>(() => ({
  width: '100%',
  height: typeof props.height === 'number' ? `${props.height}px` : props.height,
  minHeight: '120px',
}))

const { containerRef, setOption } = useEcharts({
  optionFactory: () => (props.option ?? { series: [] }) as EChartsOption,
  renderer: props.renderer,
})

watch(
  () => props.option,
  () => setOption(false),
  { deep: false },
)
</script>

<style scoped>
.t-chart-panel {
  width: 100%;
  position: relative;
}
</style>
