<script setup lang="ts">
/**
 * @experimental
 * TCitation - Single citation card for source/reference rendering.
 *
 * Displays a citation as a small surface-bg card with optional icon,
 * a strong title and a snippet preview. Use inside a `TCitationList`
 * (or any vertical stack) below an assistant message to surface the
 * sources backing the answer.
 *
 * The default slot can replace the body entirely if consumers need a
 * richer layout (multi-line snippet, link previews, etc.).
 */
import { Icon } from '@iconify/vue'

withDefaults(
  defineProps<{
    /** Citation title - typically the document name or page title. */
    title: string
    /** Snippet excerpted from the source. */
    snippet?: string
    /** Optional lucide icon. Defaults to `lucide:bookmark`. */
    icon?: string
    /** When set, makes the whole card a clickable link. */
    href?: string
  }>(),
  {
    snippet: '',
    icon: 'lucide:bookmark',
    href: '',
  },
)
</script>

<template>
  <component
    :is="href ? 'a' : 'div'"
    class="t-citation"
    :href="href || undefined"
    :target="href ? '_blank' : undefined"
    :rel="href ? 'noopener noreferrer' : undefined"
  >
    <Icon :icon="icon" class="t-citation__icon" />
    <div class="t-citation__body">
      <slot>
        <strong class="t-citation__title">{{ title }}</strong>
        <span v-if="snippet" class="t-citation__snippet">{{ snippet }}</span>
      </slot>
    </div>
  </component>
</template>

<style scoped>
.t-citation {
  display: flex;
  gap: 10px;
  padding: 10px 12px;
  background: var(--tnzi-ai-surface, #ffffff);
  border: 1px solid var(--tnzi-ai-border, #e5e5e5);
  border-radius: 10px;
  text-decoration: none;
  color: inherit;
  transition: background 150ms cubic-bezier(0.4, 0, 0.2, 1);
}
a.t-citation:hover {
  background: var(--tnzi-ai-hover, rgba(55, 53, 47, 0.04));
}
.t-citation__icon {
  color: var(--tnzi-ai-text-secondary, #6a6a6a);
  font-size: 16px;
  margin-top: 1px;
  flex-shrink: 0;
}
.t-citation__body {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}
.t-citation__title {
  font-size: 13px;
  font-weight: 500;
  color: var(--tnzi-ai-text, #1a1a1a);
}
.t-citation__snippet {
  font-size: 12px;
  color: var(--tnzi-ai-text-secondary, #6a6a6a);
}
</style>
