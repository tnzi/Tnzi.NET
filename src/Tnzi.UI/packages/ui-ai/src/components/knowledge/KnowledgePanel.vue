<script setup lang="ts">
/**
 * KnowledgePanel — Knowledge base selection panel
 */

import { Card, ScrollArea } from '../../primitives';
import { Icon } from '@iconify/vue';
import { cn } from '@/lib/utils';
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
    <ScrollArea class="max-h-[300px]">
      <div v-if="bases.length === 0" class="py-6 text-center text-sm text-muted-foreground">{{ t.knowledge.noBase }}</div>
      <div v-else class="space-y-1">
        <Card
          v-for="base in bases"
          :key="base.id"
          :class="cn('cursor-pointer transition-colors hover:bg-accent/50 p-0', selectedIds?.includes(base.id) && 'ring-2 ring-primary')"
          @click="emit('toggle', base.id)"
        >
          <div class="flex items-center gap-3 px-3 py-2">
            <Icon :icon="base.icon ?? 'lucide:database'" class="size-5 shrink-0 text-muted-foreground" />
            <div class="flex-1 min-w-0">
              <div class="text-sm font-medium truncate">{{ base.name }}</div>
              <div v-if="base.description" class="text-xs text-muted-foreground truncate">{{ base.description }}</div>
            </div>
            <span v-if="base.documentCount != null" class="text-xs text-muted-foreground tabular-nums">{{ base.documentCount }} docs</span>
          </div>
        </Card>
      </div>
    </ScrollArea>
  </div>
</template>
