<script setup lang="ts">
/**
 * @experimental
 * TStatusBanner — Inline status pill for conversation-level events.
 *
 * Use to surface transient or terminal conversation state — the agent
 * has stopped, the session is rate-limited, a task hit a tool-call budget,
 * etc. Purely visual; consumers wire in the actual state via props.
 *
 * Variants:
 *   - `stopped`  — amber (Manus "agent has stopped")
 *   - `warning`  — amber (generic)
 *   - `info`     — blue
 *   - `success`  — green
 *   - `error`    — red
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
.t-status-banner--stopped,
.t-status-banner--warning {
  background: #fff3e0;
  border-color: #f8d9a6;
  color: #8a5a1a;
}
.t-status-banner--info {
  background: #eff6ff;
  border-color: #bfdbfe;
  color: #1d4ed8;
}
.t-status-banner--success {
  background: #ecfdf5;
  border-color: #a7f3d0;
  color: #047857;
}
.t-status-banner--error {
  background: #fef2f2;
  border-color: #fecaca;
  color: #b91c1c;
}
</style>
