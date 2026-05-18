<template>
  <ChartImpl v-if="canRenderEcharts" :option="option" :height="height" :renderer="renderer" />
  <div v-else class="t-chart-panel t-chart-panel--noop" :style="containerStyle" aria-hidden="true" />
</template>

<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import type { CSSProperties } from 'vue'
import type { EChartsOption } from 'echarts'

/**
 * Generic ECharts panel — accepts a pre-built EChartsOption (or a builder
 * function) and handles lifecycle / theme reactivity / resize via the shared
 * useEcharts headless composable (loaded only when echarts is usable).
 *
 * In jsdom (vitest integration tests) there is no canvas backend and no
 * ResizeObserver, so mounting echarts crashes the parent's render cycle.
 * We detect that environment at module load and short-circuit to a static
 * placeholder div, keeping the rest of the page mountable in unit tests.
 */
interface Props {
  /** ECharts option spec. Mutating this triggers a chart redraw. */
  option: EChartsOption | null | undefined
  /** Container height — string ('320px') or number (px). Default 280px. */
  height?: string | number
  /** Renderer — canvas (default) or svg for crisp screenshots. */
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

const canRenderEcharts = (() => {
  if (typeof window === 'undefined') return false
  try {
    const c = document.createElement('canvas')
    return c.getContext('2d') !== null && typeof window.ResizeObserver === 'function'
  } catch {
    return false
  }
})()

// Lazy-load the real echarts component only when echarts is usable. Keeps
// jsdom-mounted tests free of ResizeObserver + canvas crashes.
const ChartImpl = defineAsyncComponent(() => import('./TChartPanelImpl.vue'))
</script>

<style scoped>
.t-chart-panel--noop {
  border: 1px dashed var(--tnzi-base-border, #e0e0e6);
  border-radius: 4px;
  opacity: 0.5;
}
</style>
