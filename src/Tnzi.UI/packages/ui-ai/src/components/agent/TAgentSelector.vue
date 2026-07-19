<script setup lang="ts">
/**
 * TAgentSelector — Agent card gallery with search
 */

import { NInput, NCard, NBadge, NScrollbar } from 'naive-ui';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { useLocalSearch } from '@/composables/useLocalSearch';
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
  <div class="space-y-3">
    <NInput v-model:value="searchQuery" :placeholder="t.common.search" clearable>
      <template #prefix>
        <Icon icon="lucide:search" class="size-4" />
      </template>
    </NInput>
    <NScrollbar style="max-height: 400px">
      <div v-if="filtered.length === 0" class="py-8 text-center text-sm t-agent-selector__empty">{{ t.common.noData }}</div>
      <div v-else class="grid grid-cols-2 gap-2">
        <NCard
          v-for="agent in filtered"
          :key="agent.id"
          size="small"
          hoverable
          class="t-agent-selector__card"
          :class="{ 't-agent-selector__card--selected': agent.id === selectedId }"
          @click="emit('select', agent.id)"
        >
          <div class="flex items-center gap-2 mb-1">
            <Icon :icon="agent.icon ?? 'lucide:bot'" class="size-5 shrink-0 t-agent-selector__icon" />
            <span class="text-sm font-medium truncate">{{ agent.name }}</span>
          </div>
          <p v-if="agent.description" class="text-xs t-agent-selector__desc line-clamp-2">{{ agent.description }}</p>
          <div v-if="agent.tags?.length" class="flex flex-wrap gap-1 mt-1.5">
            <NBadge
              v-for="tag in agent.tags"
              :key="tag"
              :value="tag"
              type="default"
              class="text-[10px]"
            />
          </div>
        </NCard>
      </div>
    </NScrollbar>
  </div>
</template>

<style scoped>
.t-agent-selector__empty {
  color: var(--tnzi-base-text-muted);
}
.t-agent-selector__card {
  cursor: pointer;
}
.t-agent-selector__card--selected {
  outline: 2px solid var(--tnzi-primary);
  outline-offset: -1px;
}
.t-agent-selector__icon {
  color: var(--tnzi-primary);
}
.t-agent-selector__desc {
  color: var(--tnzi-base-text-muted);
}
</style>
