<script setup lang="ts">
/**
 * @experimental
 * TResourceEmpty - centred empty state for a resource page.
 *
 * An empty page is the one screen every user of a new account sees, so it
 * carries the pitch rather than an apology: a headline in the display face, a
 * line of explanation, optional suggestion rows that double as one-click
 * starters, and a single primary action.
 *
 * The suggestion rows are the part worth having in the framework - "here are
 * three things this page is for" converts far better than an empty box with a
 * Create button, and hand-rolling them per page is how they drift.
 */
import { Icon } from '@iconify/vue'

export interface ResourceSuggestion {
  readonly id: string
  readonly label: string
  readonly icon?: string
}

withDefaults(
  defineProps<{
    /** Display-face headline. */
    title?: string
    /** Supporting line under the headline. */
    description?: string
    /** Large glyph above the headline. */
    icon?: string
    /** One-click starters. Each emits `suggestion` with its id. */
    suggestions?: ReadonlyArray<ResourceSuggestion>
  }>(),
  {
    title: '',
    description: '',
    icon: '',
    suggestions: () => [],
  },
)

const emit = defineEmits<{
  suggestion: [id: string]
}>()
</script>

<template>
  <div class="t-resource-empty">
    <Icon v-if="icon" :icon="icon" class="t-resource-empty__icon" />

    <h2 v-if="title || $slots.title" class="t-resource-empty__title">
      <slot name="title">{{ title }}</slot>
    </h2>

    <p v-if="description || $slots.description" class="t-resource-empty__desc">
      <slot name="description">{{ description }}</slot>
    </p>

    <div v-if="suggestions.length" class="t-resource-empty__suggestions">
      <button
        v-for="s in suggestions"
        :key="s.id"
        type="button"
        class="t-resource-empty__suggestion"
        @click="emit('suggestion', s.id)"
      >
        <Icon v-if="s.icon" :icon="s.icon" class="t-resource-empty__suggestion-icon" />
        <span class="t-resource-empty__suggestion-label">{{ s.label }}</span>
        <Icon icon="lucide:arrow-right" class="t-resource-empty__suggestion-go" />
      </button>
    </div>

    <div v-if="$slots.action" class="t-resource-empty__action">
      <slot name="action" />
    </div>
  </div>
</template>

<style scoped>
.t-resource-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  width: 100%;
  max-width: 768px;
  margin: 0 auto;
}
.t-resource-empty__icon {
  font-size: 40px;
  color: var(--tnzi-ai-text-tertiary);
  margin-bottom: 18px;
}
.t-resource-empty__title {
  margin: 0 0 8px;
  font-family: var(--tnzi-ai-font-display);
  font-size: 28px;
  font-weight: 400;
  line-height: 1.4;
  color: var(--tnzi-ai-text);
}
.t-resource-empty__desc {
  margin: 0 0 24px;
  font-size: 14px;
  line-height: 1.6;
  color: var(--tnzi-ai-text-secondary);
  max-width: 560px;
}
.t-resource-empty__suggestions {
  display: flex;
  flex-direction: column;
  gap: 10px;
  width: 100%;
  margin-bottom: 24px;
}
.t-resource-empty__suggestion {
  display: flex;
  align-items: center;
  gap: 12px;
  width: 100%;
  padding: 14px 18px;
  border: 1px solid var(--tnzi-ai-border);
  background: var(--tnzi-ai-surface);
  border-radius: 12px;
  color: var(--tnzi-ai-text);
  font-family: inherit;
  font-size: 14px;
  text-align: left;
  cursor: pointer;
  transition: border-color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
              background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-resource-empty__suggestion:hover {
  border-color: var(--tnzi-ai-accent);
  background: var(--tnzi-ai-hover);
}
.t-resource-empty__suggestion-icon {
  flex-shrink: 0;
  font-size: 18px;
  color: var(--tnzi-ai-text-secondary);
}
.t-resource-empty__suggestion-label {
  flex: 1;
  min-width: 0;
}
/* The arrow is the affordance; it stays quiet until the row is hovered. */
.t-resource-empty__suggestion-go {
  flex-shrink: 0;
  font-size: 16px;
  color: var(--tnzi-ai-text-tertiary);
  opacity: 0;
  transition: opacity var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-resource-empty__suggestion:hover .t-resource-empty__suggestion-go { opacity: 1; }
.t-resource-empty__action {
  display: flex;
  align-items: center;
  gap: 8px;
}
</style>
