<script setup lang="ts">
/**
 * TTemplateRow - one template on a page with nothing on it yet: a row with its
 * icon, its name, a line on what it does and optionally a fact (`when`).
 * Pointing at it (or focusing it with the keyboard) turns the icon into a "+".
 * The `peek` slot holds what a page draws over the row, positioned against it.
 *
 * Rows go in a `<ul class="t-templates">` (two columns, one on a phone) under
 * a `<div class="t-templates-divider" />` (a wavy hairline), both global
 * classes of `controls.css`, inside `TEmptyState`'s default slot, so every page
 * offers templates alike.
 */
import { Icon } from '@iconify/vue'

withDefaults(
  defineProps<{
    icon: string
    name: string
    description: string
    when?: string
    whenIcon?: string
  }>(),
  { when: '', whenIcon: 'lucide:clock' },
)

const emit = defineEmits<{ pick: [] }>()
</script>

<template>
  <button type="button" class="t-template-row" :class="{ 'has-peek': $slots.peek }" @click="emit('pick')">
    <span class="t-template-row__tile" aria-hidden="true">
      <Icon :icon="icon" class="t-template-row__glyph" />
      <Icon icon="lucide:plus" class="t-template-row__plus" />
    </span>
    <span class="t-template-row__text">
      <span class="t-template-row__name">{{ name }}</span>
      <span class="t-template-row__description">{{ description }}</span>
      <span v-if="when" class="t-template-row__when"><Icon :icon="whenIcon" aria-hidden="true" />{{ when }}</span>
    </span>
    <slot name="peek" />
  </button>
</template>

<style scoped>
.t-template-row {
  position: relative;
  display: flex;
  align-items: flex-start;
  gap: 12px;
  width: 100%;
  min-height: 72px;
  padding: 14px 12px;
  border: none;
  border-radius: var(--tnzi-ai-radius-md, 8px);
  background: transparent;
  color: inherit;
  font: inherit;
  text-align: left;
  cursor: pointer;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-template-row:hover {
  background: var(--tnzi-ai-sidebar-hover, var(--tnzi-ai-hover));
}
.t-template-row:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 0;
}

/* The tile: the template's icon, and in the same cell a "+" that takes its place on hover. */
.t-template-row__tile {
  flex-shrink: 0;
  display: grid;
  place-items: center;
  width: 36px;
  height: 36px;
  border-radius: var(--tnzi-ai-radius-md, 8px);
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text-secondary);
  font-size: 17px;
}
.t-template-row__glyph,
.t-template-row__plus {
  grid-area: 1 / 1;
  transition: opacity var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-template-row__plus {
  opacity: 0;
}
.t-template-row:hover .t-template-row__glyph,
.t-template-row:focus-visible .t-template-row__glyph {
  opacity: 0;
}
.t-template-row:hover .t-template-row__plus,
.t-template-row:focus-visible .t-template-row__plus {
  opacity: 1;
}

.t-template-row__text {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}
/* Where a card can peek, the text keeps clear of its corner (card 128px + its 12px inset + a gap). */
@media (hover: hover) {
  .t-template-row.has-peek .t-template-row__text {
    padding-right: 148px;
  }
}
.t-template-row__name {
  font-size: 15px;
  font-weight: 500;
  color: var(--tnzi-ai-text);
}
.t-template-row__description {
  font-size: 13px;
  line-height: 1.45;
  color: var(--tnzi-ai-text-secondary);
}
.t-template-row__when {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  margin-top: 2px;
  font-size: 12px;
  color: var(--tnzi-ai-text-tertiary);
}
@media (prefers-reduced-motion: reduce) {
  .t-template-row__glyph,
  .t-template-row__plus {
    transition: none;
  }
}
</style>
