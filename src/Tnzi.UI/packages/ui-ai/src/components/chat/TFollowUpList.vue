<script setup lang="ts">
/**
 * @experimental
 * TFollowUpList — Manus-style "Suggested follow-ups" vertical list.
 *
 * Renders a compact stack of full-width rows, each with a leading icon,
 * the suggestion text, and a trailing arrow. Use after a task completes
 * to nudge the user toward relevant next steps.
 *
 * Distinct from the horizontal `Suggestions` pill chip component —
 * this is a post-task "here's what to try next" list, not a prompt
 * starter chip row.
 */
import { Icon } from '@iconify/vue'

export interface FollowUpItem {
  /** The suggestion text displayed in the row. */
  text: string
  /** Optional lucide icon name. Defaults to `lucide:file-text`. */
  icon?: string
}

withDefaults(
  defineProps<{
    /** Items to render. Can be plain strings or `{ text, icon }` objects. */
    items: readonly (string | FollowUpItem)[]
    /** Section label above the list. Hidden when empty. */
    label?: string
    /** Default icon used when an item doesn't specify one. */
    defaultIcon?: string
  }>(),
  {
    label: 'Suggested follow-ups',
    defaultIcon: 'lucide:file-text',
  },
)

const emit = defineEmits<{
  select: [text: string, index: number]
}>()

function normalize(item: string | FollowUpItem): FollowUpItem {
  return typeof item === 'string' ? { text: item } : item
}
</script>

<template>
  <div v-if="items.length > 0" class="t-follow-up-list">
    <div v-if="label" class="t-follow-up-list__label">{{ label }}</div>
    <button
      v-for="(raw, i) in items"
      :key="i"
      type="button"
      class="t-follow-up-list__row"
      @click="emit('select', normalize(raw).text, i)"
    >
      <Icon
        :icon="normalize(raw).icon || defaultIcon"
        class="t-follow-up-list__icon"
      />
      <span class="t-follow-up-list__text">{{ normalize(raw).text }}</span>
      <Icon icon="lucide:arrow-right" class="t-follow-up-list__arrow" />
    </button>
  </div>
</template>

<style scoped>
.t-follow-up-list {
  display: flex;
  flex-direction: column;
  gap: 0;
}
.t-follow-up-list__label {
  font-size: 12px;
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
  padding: 0 2px 6px;
}
.t-follow-up-list__row {
  display: flex;
  align-items: center;
  gap: 12px;
  width: 100%;
  padding: 12px 14px;
  border: none;
  border-top: 1px solid var(--tnzi-ai-divider, #ececec);
  background: transparent;
  color: var(--tnzi-ai-text, #1a1a1a);
  font-family: inherit;
  font-size: 13px;
  line-height: 1.5;
  text-align: left;
  cursor: pointer;
  transition: background 150ms cubic-bezier(0.4, 0, 0.2, 1);
}
.t-follow-up-list__row:last-child {
  border-bottom: 1px solid var(--tnzi-ai-divider, #ececec);
}
.t-follow-up-list__row:hover {
  background: var(--tnzi-ai-hover, rgba(55, 53, 47, 0.04));
}
.t-follow-up-list__icon {
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
  font-size: 15px;
  flex-shrink: 0;
}
.t-follow-up-list__text {
  flex: 1;
  min-width: 0;
}
.t-follow-up-list__arrow {
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
  font-size: 15px;
  flex-shrink: 0;
}
</style>
