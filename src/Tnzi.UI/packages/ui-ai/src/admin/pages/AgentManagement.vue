<script setup lang="ts">
/**
 * AgentManagement — Agent CRUD, clone, version history, health check
 */

import {
  Button,
  Card,
  CardHeader,
  CardTitle,
  CardDescription,
  CardFooter,
  Badge,
  Input,
} from '../../primitives';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { useLocalSearch } from '@/composables/useLocalSearch';
import AdminPageShell from '../AdminPageShell.vue';

const t = useAiI18n();

export interface AgentItem {
  id: string;
  name: string;
  description?: string;
  status?: string;
  version?: number;
  createdAt: string;
}

const props = defineProps<{
  agents: AgentItem[];
  isLoading?: boolean;
}>();

const emit = defineEmits<{
  create: [];
  edit: [id: string];
  delete: [id: string];
  clone: [id: string];
  validate: [id: string];
  'view-versions': [id: string];
  'view-health': [];
  refresh: [];
}>();

const { query: searchQuery, filtered: filteredAgents } = useLocalSearch(
  () => props.agents,
  ['name', 'description'],
);
</script>

<template>
  <AdminPageShell :is-loading="isLoading">
    <template #header>
      <div class="flex items-center justify-between">
        <h2 class="text-lg font-semibold">{{ t.admin.agents }}</h2>
        <div class="flex items-center gap-2">
          <Button variant="outline" size="sm" @click="emit('view-health')">
            <Icon icon="lucide:heart-pulse" class="mr-1.5 size-4" />
            {{ t.admin.health }}
          </Button>
          <Button size="sm" @click="emit('create')">
            <Icon icon="lucide:plus" class="mr-1.5 size-4" />
            {{ t.admin.create }}
          </Button>
        </div>
      </div>
    </template>

    <template #toolbar>
      <div class="relative max-w-sm">
        <Icon icon="lucide:search" class="absolute left-2.5 top-2.5 size-4 text-muted-foreground" />
        <Input v-model="searchQuery" :placeholder="t.common.search" class="pl-9" />
      </div>
    </template>

    <div v-if="filteredAgents.length === 0" class="py-12 text-center text-muted-foreground">
      {{ t.admin.noData }}
    </div>
    <div v-else class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
      <Card v-for="agent in filteredAgents" :key="agent.id" class="hover:shadow-md transition-shadow">
        <CardHeader class="pb-2">
          <div class="flex items-start justify-between">
            <CardTitle class="text-sm">{{ agent.name }}</CardTitle>
            <Badge v-if="agent.version" variant="outline" class="text-[10px]">v{{ agent.version }}</Badge>
          </div>
          <CardDescription v-if="agent.description" class="text-xs line-clamp-2">
            {{ agent.description }}
          </CardDescription>
        </CardHeader>
        <CardFooter class="gap-1 pt-0">
          <Button variant="ghost" size="icon-sm" @click="emit('edit', agent.id)">
            <Icon icon="lucide:pencil" class="size-3.5" />
          </Button>
          <Button variant="ghost" size="icon-sm" @click="emit('clone', agent.id)">
            <Icon icon="lucide:copy" class="size-3.5" />
          </Button>
          <Button variant="ghost" size="icon-sm" @click="emit('validate', agent.id)">
            <Icon icon="lucide:check-circle" class="size-3.5" />
          </Button>
          <Button variant="ghost" size="icon-sm" @click="emit('view-versions', agent.id)">
            <Icon icon="lucide:history" class="size-3.5" />
          </Button>
          <div class="flex-1" />
          <Button variant="ghost" size="icon-sm" class="text-destructive" @click="emit('delete', agent.id)">
            <Icon icon="lucide:trash-2" class="size-3.5" />
          </Button>
        </CardFooter>
      </Card>
    </div>
  </AdminPageShell>
</template>
