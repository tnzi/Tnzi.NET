<script setup lang="ts">
/**
 * TNotice - a line of context inside a page or dialog: what is about to
 * happen, or why something cannot.
 *
 * Tinted with its tone, an icon of the tone before the words. `error` is
 * announced (`role="alert"`); the other tones are read in place.
 */
import { computed } from 'vue'
import { Icon } from '@iconify/vue'

const props = withDefaults(defineProps<{ tone?: 'info' | 'warning' | 'error' | 'success' }>(), { tone: 'info' })

const ICONS = {
  info: 'lucide:info',
  warning: 'lucide:triangle-alert',
  error: 'lucide:circle-alert',
  success: 'lucide:circle-check',
} as const

const icon = computed(() => ICONS[props.tone])
</script>

<template>
  <div class="t-notice" :class="`t-notice--${tone}`" :role="tone === 'error' ? 'alert' : undefined">
    <Icon :icon="icon" class="t-notice__icon" aria-hidden="true" />
    <div class="t-notice__body"><slot /></div>
  </div>
</template>

<style scoped>
.t-notice {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  padding: 10px 12px;
  border-radius: var(--tnzi-ai-radius-lg, 12px);
  color: var(--tnzi-ai-text);
  font-size: 13px;
  line-height: 1.55;
}
.t-notice__icon {
  flex-shrink: 0;
  margin-top: 2px;
  font-size: 16px;
}
.t-notice__body {
  flex: 1;
  min-width: 0;
  overflow-wrap: anywhere;
}
.t-notice--info {
  background: color-mix(in srgb, var(--tnzi-ai-info) 8%, transparent);
}
.t-notice--info .t-notice__icon {
  color: var(--tnzi-ai-info);
}
.t-notice--warning {
  background: color-mix(in srgb, var(--tnzi-ai-warning) 10%, transparent);
}
.t-notice--warning .t-notice__icon {
  color: var(--tnzi-ai-warning);
}
.t-notice--error {
  background: var(--tnzi-ai-danger-soft);
}
.t-notice--error .t-notice__icon {
  color: var(--tnzi-ai-danger);
}
.t-notice--success {
  background: color-mix(in srgb, var(--tnzi-ai-success) 10%, transparent);
}
.t-notice--success .t-notice__icon {
  color: var(--tnzi-ai-success);
}
</style>
