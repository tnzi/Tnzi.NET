<script setup lang="ts">
/**
 * TBadge - a count (or a dot) on the corner of an icon: "3 sessions running"
 * at the foot of a card, unread notes on a bell.
 *
 * Give it an `icon`, or put the thing it counts in the default slot (an icon,
 * a `TIconButton`, an avatar); the count sits on its top right corner, ringed
 * in the colour of the surface it stands on (`--tnzi-ai-surface`; set
 * `--t-badge-ring` on it for another) so it reads against the icon. A count of 0 shows nothing; above `max` (99) it
 * reads `99+`. `dot` shows a dot instead of a number.
 *
 * `label` is what a screen reader hears for the whole badge ("3 sessions
 * running"), which the bare number does not say: with an `icon` the badge is
 * an image with that name (`role="img"`); around slotted content (a button
 * with its own name) it is extra words after that content. Without `label`
 * the count is decoration.
 *
 * Tones: `accent` (default), `neutral`, `warning`, `danger`.
 */
import { computed } from 'vue'
import { Icon } from '@iconify/vue'

const props = withDefaults(
  defineProps<{
    /** Iconify name of the icon it sits on; leave unset to badge the default slot instead. */
    icon?: string
    count?: number
    /** Above this the count reads `<max>+`. */
    max?: number
    /** A dot instead of the number. */
    dot?: boolean
    tone?: 'accent' | 'neutral' | 'warning' | 'danger'
    /** What a screen reader hears for the badge and what it counts. */
    label?: string
    /** Show the badge even when the count is 0. */
    showZero?: boolean
  }>(),
  { icon: '', count: 0, max: 99, dot: false, tone: 'accent', label: '', showZero: false },
)

const shown = computed(() => props.dot || props.count > 0 || props.showZero)
const text = computed(() => (props.count > props.max ? `${props.max}+` : String(Math.max(0, Math.floor(props.count)))))
</script>

<template>
  <span
    class="t-badge"
    :class="[`t-badge--${tone}`]"
    :role="icon && label ? 'img' : undefined"
    :aria-label="icon && label ? label : undefined"
    data-badge
  >
    <Icon v-if="icon" :icon="icon" class="t-badge__icon" aria-hidden="true" />
    <slot v-else />
    <span
      v-if="shown"
      class="t-badge__mark"
      :class="{ 't-badge__mark--dot': dot }"
      aria-hidden="true"
      data-badge-mark
    >{{ dot ? '' : text }}</span>
    <span v-if="!icon && label" class="sr-only">{{ label }}</span>
  </span>
</template>

<style scoped>
.t-badge {
  --t-badge-color: var(--tnzi-ai-accent);
  --t-badge-fg: var(--tnzi-ai-on-accent);
  position: relative;
  display: inline-flex;
  flex: none;
  align-items: center;
  justify-content: center;
  vertical-align: middle;
}
.t-badge__icon {
  font-size: 16px;
  color: var(--tnzi-ai-text-secondary);
}
.t-badge__mark {
  position: absolute;
  top: 0;
  right: 0;
  transform: translate(45%, -40%);
  box-sizing: border-box;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 16px;
  height: 16px;
  padding: 0 4px;
  border-radius: var(--tnzi-ai-radius-md, 8px);
  background: var(--t-badge-color);
  color: var(--t-badge-fg);
  box-shadow: 0 0 0 2px var(--t-badge-ring, var(--tnzi-ai-surface));
  font-size: 10.5px;
  font-weight: 600;
  line-height: 1;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
  pointer-events: none;
}
.t-badge__mark--dot {
  min-width: 8px;
  width: 8px;
  height: 8px;
  padding: 0;
  transform: translate(30%, -30%);
}
.t-badge--neutral {
  --t-badge-color: var(--tnzi-ai-text-secondary);
  --t-badge-fg: var(--tnzi-ai-surface);
}
.t-badge--warning {
  --t-badge-color: var(--tnzi-ai-warning);
  --t-badge-fg: var(--tnzi-ai-on-status);
}
.t-badge--danger {
  --t-badge-color: var(--tnzi-ai-danger);
  --t-badge-fg: var(--tnzi-ai-on-status);
}
</style>
