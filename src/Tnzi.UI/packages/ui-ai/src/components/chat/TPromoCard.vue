<script setup lang="ts">
/**
 * @experimental
 * TPromoCard — Sidebar / footer promo block.
 *
 * Compact card with title + subtitle + trailing arrow, used in the
 * footer of expanded sidebars to surface things like
 * "Share with a friend / Get 500 credits each" or "Upgrade to Pro".
 *
 * Renders as a button so it's clickable end-to-end. Use the default
 * slot for custom content; for the common case pass `title` + `subtitle`
 * (+ optional `icon`).
 */
import { Icon } from '@iconify/vue'

withDefaults(
  defineProps<{
    title?: string
    subtitle?: string
    /** Lucide icon shown on the right. Defaults to chevron-right. */
    icon?: string
  }>(),
  {
    title: '',
    subtitle: '',
    icon: 'lucide:chevron-right',
  },
)

defineEmits<{
  click: []
}>()
</script>

<template>
  <button type="button" class="t-promo-card" @click="$emit('click')">
    <div class="t-promo-card__body">
      <slot>
        <div class="t-promo-card__title">{{ title }}</div>
        <div v-if="subtitle" class="t-promo-card__sub">{{ subtitle }}</div>
      </slot>
    </div>
    <Icon :icon="icon" class="t-promo-card__arrow" />
  </button>
</template>

<style scoped>
.t-promo-card {
  display: flex;
  align-items: center;
  gap: 12px;
  width: 100%;
  padding: 10px 14px;
  border: 1px solid var(--tnzi-ai-border, #e5e5e5);
  background: var(--tnzi-ai-surface, #ffffff);
  border-radius: 10px;
  text-align: left;
  cursor: pointer;
  transition: background 150ms cubic-bezier(0.4, 0, 0.2, 1);
  font-family: inherit;
}
.t-promo-card:hover {
  background: var(--tnzi-ai-hover, rgba(55, 53, 47, 0.04));
}
.t-promo-card__body {
  flex: 1;
  min-width: 0;
}
.t-promo-card__title {
  font-size: 13px;
  font-weight: 500;
  color: var(--tnzi-ai-text, #1a1a1a);
}
.t-promo-card__sub {
  font-size: 11px;
  color: var(--tnzi-ai-text-secondary, #6a6a6a);
  margin-top: 2px;
}
.t-promo-card__arrow {
  color: var(--tnzi-ai-text-secondary, #6a6a6a);
  font-size: 16px;
  flex-shrink: 0;
}
</style>
