<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'

/**
 * Animated number counter — rolls from `startValue` to `endValue` over
 * `duration` ms with a customizable easing curve. Used by dashboards
 * (KPI cards), counters in landing pages, and any place a big number
 * deserves a moment of attention.
 *
 * Inspired by soybean-admin's `CountTo` (src/components/custom/count-to.vue).
 */
interface Props {
  /** Starting number. Default 0. */
  startValue?: number
  /** Target number. Required. */
  endValue: number
  /** Duration in ms. Default 1500. */
  duration?: number
  /** Number of decimals to display. Default 0. */
  decimals?: number
  /** Decimal separator. Default `.`. */
  decimal?: string
  /** Thousands separator. Default `,`. Pass empty string to disable. */
  separator?: string
  /** Prefix text (e.g. `$`). */
  prefix?: string
  /** Suffix text (e.g. `%`). */
  suffix?: string
  /**
   * Easing function. `linear` | `ease-out` | `ease-in-out`. Default `ease-out`
   * which feels best for KPI counters (fast start, soft landing).
   */
  easing?: 'linear' | 'ease-out' | 'ease-in-out'
  /** Auto-play on mount. Default true. */
  autoplay?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  startValue: 0,
  duration: 1500,
  decimals: 0,
  decimal: '.',
  separator: ',',
  prefix: '',
  suffix: '',
  easing: 'ease-out',
  autoplay: true,
})

const emit = defineEmits<{
  end: [value: number]
  start: [value: number]
}>()

const currentValue = ref(props.startValue)
let rafId: number | null = null
let startTime: number | null = null

function easeFn(t: number): number {
  if (props.easing === 'linear') return t
  if (props.easing === 'ease-in-out') {
    return t < 0.5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2
  }
  // ease-out (default)
  return 1 - Math.pow(1 - t, 3)
}

function tick(now: number): void {
  if (startTime === null) startTime = now
  const elapsed = now - startTime
  const progress = Math.min(elapsed / props.duration, 1)
  const eased = easeFn(progress)
  currentValue.value =
    props.startValue + (props.endValue - props.startValue) * eased
  if (progress < 1) {
    rafId = requestAnimationFrame(tick)
  } else {
    currentValue.value = props.endValue
    rafId = null
    startTime = null
    emit('end', props.endValue)
  }
}

function start(): void {
  if (rafId !== null) cancelAnimationFrame(rafId)
  startTime = null
  currentValue.value = props.startValue
  emit('start', props.startValue)
  rafId = requestAnimationFrame(tick)
}

function pause(): void {
  if (rafId !== null) {
    cancelAnimationFrame(rafId)
    rafId = null
  }
}

const displayed = computed(() => {
  const n = currentValue.value
  const fixed = n.toFixed(props.decimals)
  const parts = fixed.split('.')
  const intPart = parts[0] ?? '0'
  const decPart = parts[1]
  const grouped = props.separator
    ? intPart.replace(/\B(?=(\d{3})+(?!\d))/g, props.separator)
    : intPart
  return decPart ? `${grouped}${props.decimal}${decPart}` : grouped
})

onMounted(() => {
  if (props.autoplay) start()
})

watch(
  () => props.endValue,
  () => {
    if (props.autoplay) start()
  },
)

defineExpose({ start, pause, currentValue })
</script>

<template>
  <span class="t-count-to">
    <span v-if="prefix" class="t-count-to__prefix">{{ prefix }}</span>
    <span class="t-count-to__value">{{ displayed }}</span>
    <span v-if="suffix" class="t-count-to__suffix">{{ suffix }}</span>
  </span>
</template>

<style scoped>
.t-count-to {
  font-variant-numeric: tabular-nums;
  font-feature-settings: 'tnum';
}
</style>
