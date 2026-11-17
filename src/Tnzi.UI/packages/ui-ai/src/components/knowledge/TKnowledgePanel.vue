<script setup lang="ts">
/**
 * TKnowledgePanel - Knowledge base selection panel
 *
 * Each base is a card button that toggles it (`aria-pressed` while it is
 * among `selectedIds`); the list scrolls inside 300px.
 */

import { Icon } from '@iconify/vue';
import { formatAiMessage, useAiI18n } from '../../i18n/index';
const t = useAiI18n();

export interface KnowledgeBase {
  id: string;
  name: string;
  description?: string;
  documentCount?: number;
  icon?: string;
}

defineProps<{
  bases: KnowledgeBase[];
  selectedIds?: string[];
}>();

const emit = defineEmits<{
  toggle: [baseId: string];
}>();
</script>

<template>
  <div class="t-knowledge-panel">
    <div class="t-knowledge-panel__title">
      <Icon icon="lucide:library" class="t-knowledge-panel__title-icon" aria-hidden="true" />
      {{ t.knowledge.panel }}
    </div>
    <div class="t-knowledge-panel__scroll t-ai-scroll">
      <div v-if="bases.length === 0" class="t-knowledge-panel__empty">{{ t.knowledge.noBase }}</div>
      <div v-else class="t-knowledge-panel__list">
        <button
          v-for="base in bases"
          :key="base.id"
          type="button"
          class="t-knowledge-panel__card"
          :class="{ 'is-selected': selectedIds?.includes(base.id) }"
          :aria-pressed="selectedIds?.includes(base.id) ?? false"
          :data-base-id="base.id"
          @click="emit('toggle', base.id)"
        >
          <Icon :icon="base.icon ?? 'lucide:database'" class="t-knowledge-panel__icon" aria-hidden="true" />
          <span class="t-knowledge-panel__text">
            <span class="t-knowledge-panel__name">{{ base.name }}</span>
            <span v-if="base.description" class="t-knowledge-panel__desc">{{ base.description }}</span>
          </span>
          <span v-if="base.documentCount != null" class="t-knowledge-panel__count">
            {{ formatAiMessage(t.knowledge.documentCount, { count: base.documentCount }) }}
          </span>
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.t-knowledge-panel {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.t-knowledge-panel__title {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  font-weight: 500;
  color: var(--tnzi-ai-text);
}
.t-knowledge-panel__title-icon {
  font-size: 16px;
  color: var(--tnzi-ai-accent-text);
}
.t-knowledge-panel__scroll {
  position: relative;
  max-height: 300px;
  overflow-y: auto;
}
.t-knowledge-panel__empty {
  padding: 24px 0;
  text-align: center;
  font-size: 13px;
  color: var(--tnzi-ai-text-secondary);
}
.t-knowledge-panel__list {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.t-knowledge-panel__card {
  display: flex;
  align-items: center;
  gap: 12px;
  width: 100%;
  min-width: 0;
  padding: 10px 12px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 12px;
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  font-family: inherit;
  text-align: left;
  cursor: pointer;
  transition: border-color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
    background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-knowledge-panel__card:hover {
  border-color: var(--tnzi-ai-border-strong);
}
.t-knowledge-panel__card:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 2px;
}
.t-knowledge-panel__card.is-selected {
  border-color: var(--tnzi-ai-accent);
  background: var(--tnzi-ai-accent-soft);
}
.t-knowledge-panel__icon {
  flex-shrink: 0;
  font-size: 18px;
  color: var(--tnzi-ai-text-secondary);
}
.t-knowledge-panel__text {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-width: 0;
}
.t-knowledge-panel__name,
.t-knowledge-panel__desc {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-knowledge-panel__name {
  font-size: 13px;
  font-weight: 500;
}
.t-knowledge-panel__desc {
  font-size: 12px;
  color: var(--tnzi-ai-text-secondary);
}
.t-knowledge-panel__count {
  flex-shrink: 0;
  font-size: 12px;
  font-variant-numeric: tabular-nums;
  color: var(--tnzi-ai-text-secondary);
}
</style>
