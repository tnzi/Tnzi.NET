<script setup lang="ts">
/**
 * @experimental
 * TStatusBanner - Inline status pill for conversation-level events.
 *
 * Use to surface transient or terminal conversation state - the agent
 * has stopped, the session is rate-limited, a task hit a tool-call budget,
 * etc. Purely visual; consumers wire in the actual state via props.
 *
 * Variants:
 *   - `stopped` - amber (Manus "agent has stopped")
 *   - `warning` - amber (generic)
 *   - `info` - blue
 *   - `success` - green
 *   - `error` - red
 */
import { computed } from 'vue'
import { Icon } from '@iconify/vue'

export type StatusBannerVariant = 'stopped' | 'warning' | 'info' | 'success' | 'error'

const props = withDefaults(
  defineProps<{
    variant?: StatusBannerVariant
    /** Optional icon override; defaults to a variant-specific lucide icon. */
    icon?: string
    /** Text label; ignored when the default slot has content. */
    label?: string
  }>(),
  {
    variant: 'stopped',
    icon: '',
    label: '',
  },
)

const resolvedIcon = computed((): string => {
  if (props.icon) return props.icon
  switch (props.variant) {
    case 'stopped':
      return 'lucide:octagon-pause'
    case 'warning':
      return 'lucide:triangle-alert'
    case 'info':
      return 'lucide:info'
    case 'success':
      return 'lucide:circle-check'
    case 'error':
      return 'lucide:circle-alert'
    default:
      return 'lucide:info'
  }
})
</script>

<template>
  <div
    class="t-status-banner"
    :class="`t-status-banner--${variant}`"
    role="status"
  >
    <Icon :icon="resolvedIcon" class="t-status-banner__icon" />
    <span class="t-status-banner__label">
      <slot>{{ label }}</slot>
    </span>
  </div>
</template>

<style scoped>
.t-status-banner {
  align-self: flex-start;
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 8px 14px;
  border-radius: 999px;
  font-size: 13px;
  line-height: 1;
  border: 1px solid transparent;
}
.t-status-banner__icon {
  font-size: 15px;
  flex-shrink: 0;
}
/* Each variant is derived from one status token, so the banner follows the
   palette into dark mode instead of staying a glaring pale block on a dark
   canvas (the previous hardcoded pastels had no .dark counterpart at all). */
.t-status-banner--stopped,
.t-status-banner--warning {
  background: color-mix(in srgb, var(--tnzi-ai-warning) 12%, var(--tnzi-ai-surface));
  border-color: color-mix(in srgb, var(--tnzi-ai-warning) 34%, transparent);
  color: color-mix(in srgb, var(--tnzi-ai-warning) 78%, var(--tnzi-ai-text));
}
.t-status-banner--info {
  background: color-mix(in srgb, var(--tnzi-ai-info) 12%, var(--tnzi-ai-surface));
  border-color: color-mix(in srgb, var(--tnzi-ai-info) 34%, transparent);
  color: color-mix(in srgb, var(--tnzi-ai-info) 78%, var(--tnzi-ai-text));
}
.t-status-banner--success {
  background: color-mix(in srgb, var(--tnzi-ai-success) 12%, var(--tnzi-ai-surface));
  border-color: color-mix(in srgb, var(--tnzi-ai-success) 34%, transparent);
  color: color-mix(in srgb, var(--tnzi-ai-success) 78%, var(--tnzi-ai-text));
}
.t-status-banner--error {
  background: color-mix(in srgb, var(--tnzi-ai-danger) 12%, var(--tnzi-ai-surface));
  border-color: color-mix(in srgb, var(--tnzi-ai-danger) 34%, transparent);
  color: color-mix(in srgb, var(--tnzi-ai-danger) 78%, var(--tnzi-ai-text));
}
</style>
