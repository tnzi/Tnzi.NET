<script setup lang="ts">
/**
 * TSuggestions - Suggestion buttons
 *
 * One row of pill-shaped suggestion buttons that scrolls sideways when it is
 * wider than its container (the package's thin scrollbar; a finger swipes it
 * on a phone), with a staggered fade-in. The chips are drawn as the landing
 * page's suggestions, so the two read as one kind of thing.
 */

import { Icon } from '@iconify/vue';
export interface SuggestionItem {
  text: string;
  icon?: string;
}

defineProps<{
  suggestions: SuggestionItem[];
}>();

const emit = defineEmits<{
  select: [text: string];
}>();
</script>

<template>
  <div class="t-suggestions t-ai-scroll">
    <button
      v-for="(item, index) in suggestions"
      :key="index"
      type="button"
      class="t-suggestions__chip"
      :style="{ animationDelay: `${index * 50}ms` }"
      @click="emit('select', item.text)"
    >
      <Icon v-if="item.icon" :icon="item.icon" aria-hidden="true" />
      <span>{{ item.text }}</span>
    </button>
  </div>
</template>

<style scoped>
.t-suggestions {
  position: relative;
  display: flex;
  gap: 8px;
  width: 100%;
  overflow-x: auto;
  overflow-y: hidden;
  /* Room for the thin bar under the chips, and for the focus outline. */
  padding: 2px 2px 6px;
  overscroll-behavior-x: contain;
}
.t-suggestions__chip {
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  height: 30px;
  padding: 0 12px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 999px;
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  font-family: inherit;
  font-size: 13px;
  white-space: nowrap;
  cursor: pointer;
  animation: t-suggestions-in var(--tnzi-ai-duration-base) var(--tnzi-ai-easing) both;
  transition:
    background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
    border-color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-suggestions__chip:hover {
  background: var(--tnzi-ai-hover);
  border-color: var(--tnzi-ai-control-border-hover);
}
.t-suggestions__chip:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 2px;
}
.t-suggestions__chip > .iconify {
  font-size: 15px;
  color: var(--tnzi-ai-text-secondary);
}
@keyframes t-suggestions-in {
  from {
    opacity: 0;
    transform: translateY(4px);
  }
}
@media (prefers-reduced-motion: reduce) {
  .t-suggestions__chip {
    animation: none;
  }
}
</style>
