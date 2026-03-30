<script setup lang="ts">
/**
 * TAgentSelector — Agent card gallery with search
 */

import { Icon } from '@iconify/vue';
import { cn } from '@/lib/utils';
import { useAiI18n } from '@/locale/index';
import { useLocalSearch } from '@/composables/useLocalSearch';
import { Input } from '@/primitives/ui/input';
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from '@/primitives/ui/card';
import { Badge } from '@/primitives/ui/badge';
import { ScrollArea } from '@/primitives/ui/scroll-area';

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
    <div class="relative">
      <Icon icon="lucide:search" class="absolute left-2.5 top-2.5 size-4 text-muted-foreground" />
      <Input v-model="searchQuery" :placeholder="t.common.search" class="pl-9" />
    </div>
    <ScrollArea class="max-h-[400px]">
      <div v-if="filtered.length === 0" class="py-8 text-center text-sm text-muted-foreground">{{ t.common.noData }}</div>
      <div v-else class="grid grid-cols-2 gap-2">
        <Card
          v-for="agent in filtered"
          :key="agent.id"
          :class="cn('cursor-pointer transition-colors hover:bg-accent/50', agent.id === selectedId && 'ring-2 ring-primary')"
          @click="emit('select', agent.id)"
        >
          <CardHeader class="p-3 pb-1">
            <div class="flex items-center gap-2">
              <Icon :icon="agent.icon ?? 'lucide:bot'" class="size-5 shrink-0 text-primary" />
              <CardTitle class="text-sm truncate">{{ agent.name }}</CardTitle>
            </div>
          </CardHeader>
          <CardContent class="p-3 pt-0">
            <CardDescription v-if="agent.description" class="text-xs line-clamp-2">{{ agent.description }}</CardDescription>
            <div v-if="agent.tags?.length" class="flex flex-wrap gap-1 mt-1.5">
              <Badge v-for="tag in agent.tags" :key="tag" variant="secondary" class="text-[10px] px-1.5 py-0">{{ tag }}</Badge>
            </div>
          </CardContent>
        </Card>
      </div>
    </ScrollArea>
  </div>
</template>
