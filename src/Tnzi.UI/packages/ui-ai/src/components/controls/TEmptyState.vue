<script setup lang="ts">
/**
 * TEmptyState - a page with nothing on it yet: a drawing of what the page
 * holds, a line saying so, and the action that fills it (an outlined button
 * with its icon, in the `action` slot). Whatever the page offers besides, such
 * as templates to start from (`TTemplateRow` in a `.t-templates` under a
 * `.t-templates-divider`), goes in the default slot under them.
 *
 * Every content page's empty state is this one, so they read alike; a panel or
 * a dialog with nothing in it uses `TStateView`'s compact empty instead.
 *
 * The drawing is one of `TEmptyArt`'s (`art`), or the page's own in the `#art`
 * slot (an `<svg class="t-empty-art">`, see `TEmptyArt`).
 */
import TEmptyArt from './TEmptyArt.vue'
import type { EmptyArt } from './empty-art'

withDefaults(defineProps<{ art?: EmptyArt | null; title: string; text?: string }>(), { art: null, text: '' })
</script>

<template>
  <section class="t-empty-state" data-empty>
    <slot name="art">
      <TEmptyArt v-if="art" :name="art" />
    </slot>
    <p class="t-empty-state__title">{{ title }}</p>
    <p v-if="text" class="t-empty-state__text">{{ text }}</p>
    <div v-if="$slots.action" class="t-empty-state__action"><slot name="action" /></div>
    <slot />
  </section>
</template>

<style scoped>
.t-empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 24px 0 32px;
  text-align: center;
}
.t-empty-state__title {
  margin: 14px 0 0;
  font-size: 14px;
  color: var(--tnzi-ai-text-secondary);
  overflow-wrap: anywhere;
}
.t-empty-state__text {
  max-width: 440px;
  margin: 4px 0 0;
  font-size: 13px;
  line-height: 1.6;
  color: var(--tnzi-ai-text-tertiary);
  overflow-wrap: anywhere;
}
.t-empty-state__action {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 8px;
  margin-top: 12px;
}
</style>
