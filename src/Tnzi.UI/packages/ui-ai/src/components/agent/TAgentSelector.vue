<script setup lang="ts">
/**
 * TAgentSelector - Agent card gallery with search
 *
 * Each agent is a card button (`aria-pressed` on the selected one) in a grid
 * of cards at least 180px wide, so a phone gets one column and a wide panel
 * two or more. The list scrolls inside 400px under the search field.
 */

import { Icon } from '@iconify/vue';
import { useAiI18n } from '../../i18n/index';
import { useLocalSearch } from '../../headless/useLocalSearch';
import TTag from '../controls/TTag.vue';
const t = useAiI18n();

export interface AgentOption {
  id: string;
  name: string;
  description?: string;
  icon?: string;
  tags?: string[];
}

const props = defineProps<{
  agents: AgentOption[];
  selectedId?: string;
}>();

const emit = defineEmits<{
  select: [agentId: string];
}>();

const { query: searchQuery, filtered } = useLocalSearch(
  () => props.agents,
  ['name', 'description'],
);
</script>

<template>
  <div class="t-agent-selector">
    <div class="t-agent-selector__search">
      <Icon icon="lucide:search" class="t-agent-selector__search-icon" aria-hidden="true" />
      <input
        v-model="searchQuery"
        type="search"
        class="t-input t-agent-selector__search-input"
        :placeholder="t.common.search"
        :aria-label="t.common.search"
      />
    </div>
    <div class="t-agent-selector__scroll t-ai-scroll">
      <div v-if="filtered.length === 0" class="t-agent-selector__empty">{{ t.common.noData }}</div>
      <div v-else class="t-agent-selector__grid">
        <button
          v-for="agent in filtered"
          :key="agent.id"
          type="button"
          class="t-agent-selector__card"
          :class="{ 'is-selected': agent.id === selectedId }"
          :aria-pressed="agent.id === selectedId"
          :data-agent-id="agent.id"
          @click="emit('select', agent.id)"
        >
          <span class="t-agent-selector__head">
            <Icon :icon="agent.icon ?? 'lucide:bot'" class="t-agent-selector__icon" aria-hidden="true" />
            <span class="t-agent-selector__name">{{ agent.name }}</span>
          </span>
          <span v-if="agent.description" class="t-agent-selector__desc">{{ agent.description }}</span>
          <span v-if="agent.tags?.length" class="t-agent-selector__tags">
            <TTag v-for="tag in agent.tags" :key="tag" size="sm">{{ tag }}</TTag>
          </span>
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.t-agent-selector {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.t-agent-selector__search {
  position: relative;
}
.t-agent-selector__search-icon {
  position: absolute;
  top: 50%;
  left: 10px;
  transform: translateY(-50%);
  font-size: 14px;
  color: var(--tnzi-ai-text-tertiary);
  pointer-events: none;
}
.t-agent-selector__search-input {
  padding-left: 30px;
}
.t-agent-selector__scroll {
  position: relative;
  max-height: 400px;
  overflow-y: auto;
}
.t-agent-selector__empty {
  padding: 32px 0;
  text-align: center;
  font-size: 13px;
  color: var(--tnzi-ai-text-secondary);
}
.t-agent-selector__grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
  gap: 8px;
}
.t-agent-selector__card {
  display: flex;
  flex-direction: column;
  gap: 4px;
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
.t-agent-selector__card:hover {
  border-color: var(--tnzi-ai-border-strong);
}
.t-agent-selector__card:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 2px;
}
.t-agent-selector__card.is-selected {
  border-color: var(--tnzi-ai-accent);
  background: var(--tnzi-ai-accent-soft);
}
.t-agent-selector__head {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.t-agent-selector__icon {
  flex-shrink: 0;
  font-size: 18px;
  color: var(--tnzi-ai-accent-text);
}
.t-agent-selector__name {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 13px;
  font-weight: 500;
}
.t-agent-selector__desc {
  display: -webkit-box;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
  overflow: hidden;
  font-size: 12px;
  line-height: 1.45;
  color: var(--tnzi-ai-text-secondary);
}
.t-agent-selector__tags {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  margin-top: 2px;
}
</style>
