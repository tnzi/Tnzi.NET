<script setup lang="ts">
/**
 * TProgress - how much of something is used or done, as a thin bar.
 *
 * A rounded track in a 10% mix of the text colour with a fill in the tone:
 * `accent` (default) for progress and ordinary use, `warning` when a limit is
 * near, `danger` when it is reached. `value` is a percentage and is clamped to
 * 0..100, so a usage figure past its limit draws a full bar rather than an
 * overflowing one. `height` is the bar's thickness in px (6 by default);
 * the width is the container's unless a `width` is given.
 *
 * It is a `role="progressbar"` with `aria-valuenow`; `label` is what a screen
 * reader hears as its name. Without one the bar is decorative (the figures
 * beside it usually say the same in words) and is hidden from the
 * accessibility tree.
 */
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    /** Percentage, clamped to 0..100. */
    value: number
    tone?: 'accent' | 'warning' | 'danger'
    /** Bar thickness in px. */
    height?: number
    /** Fixed width (any CSS length). Default: the container's. */
    width?: string
    /** Accessible name. Without it the bar is decorative. */
    label?: string
  }>(),
  { tone: 'accent', height: 6, width: undefined, label: undefined },
)

const percent = computed(() => {
  const v = Number.isFinite(props.value) ? props.value : 0
  return Math.min(100, Math.max(0, v))
})
</script>

<template>
  <div
    class="t-progress"
    :class="`t-progress--${tone}`"
    :style="{ height: `${height}px`, width }"
    :role="label ? 'progressbar' : undefined"
    :aria-label="label"
    :aria-valuenow="label ? Math.round(percent) : undefined"
    :aria-valuemin="label ? 0 : undefined"
    :aria-valuemax="label ? 100 : undefined"
    :aria-hidden="label ? undefined : 'true'"
  >
    <div class="t-progress__fill" :style="{ width: `${percent}%` }" />
  </div>
</template>

<style scoped>
.t-progress {
  --t-progress-color: var(--tnzi-ai-accent);
  position: relative;
  overflow: hidden;
  min-width: 24px;
  border-radius: var(--tnzi-ai-radius-pill, 999px);
  background: color-mix(in srgb, var(--tnzi-ai-text) 10%, transparent);
}
.t-progress__fill {
  height: 100%;
  border-radius: inherit;
  background: var(--t-progress-color);
  transition: width var(--tnzi-ai-duration-base, 200ms) var(--tnzi-ai-easing, ease);
}
.t-progress--warning {
  --t-progress-color: var(--tnzi-ai-warning);
}
.t-progress--danger {
  --t-progress-color: var(--tnzi-ai-danger);
}
@media (prefers-reduced-motion: reduce) {
  .t-progress__fill {
    transition: none;
  }
}
</style>
