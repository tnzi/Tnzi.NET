<script setup lang="ts">
/**
 * TKnowledgePanel - Knowledge base selection panel
 */

import { NCard, NScrollbar } from 'naive-ui';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
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
  <div class="space-y-2">
    <div class="flex items-center gap-2 text-sm font-medium">
      <Icon icon="lucide:library" class="size-4 text-primary" />
      {{ t.knowledge.panel }}
    </div>
    <NScrollbar style="max-height: 300px">
      <div v-if="bases.length === 0" class="py-6 text-center text-sm t-knowledge-panel__empty">{{ t.knowledge.noBase }}</div>
      <div v-else class="space-y-1">
        <NCard
          v-for="base in bases"
          :key="base.id"
          size="small"
          hoverable
          class="t-knowledge-panel__card"
          :class="{ 't-knowledge-panel__card--selected': selectedIds?.includes(base.id) }"
          @click="emit('toggle', base.id)"
        >
          <div class="flex items-center gap-3">
            <Icon :icon="base.icon ?? 'lucide:database'" class="size-5 shrink-0 t-knowledge-panel__icon" />
            <div class="flex-1 min-w-0">
              <div class="text-sm font-medium truncate">{{ base.name }}</div>
              <div v-if="base.description" class="text-xs t-knowledge-panel__desc truncate">{{ base.description }}</div>
            </div>
            <span v-if="base.documentCount != null" class="text-xs t-knowledge-panel__count tabular-nums">{{ base.documentCount }} docs</span>
          </div>
        </NCard>
      </div>
    </NScrollbar>
  </div>
</template>

<style scoped>
.t-knowledge-panel__empty { color: var(--tnzi-base-text-muted); }
.t-knowledge-panel__card { cursor: pointer; }
.t-knowledge-panel__card--selected { outline: 2px solid var(--tnzi-primary); outline-offset: -1px; }
.t-knowledge-panel__icon { color: var(--tnzi-base-text-muted); }
.t-knowledge-panel__desc { color: var(--tnzi-base-text-muted); }
.t-knowledge-panel__count { color: var(--tnzi-base-text-muted); }
</style>
