<script setup lang="ts">
/**
 * SkillManagement — Skill grid with batch operations, import/export
 */

import { ref } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { useLocalSearch } from '@/composables/useLocalSearch';
import { Button } from '@/primitives/ui/button';
import { Card, CardHeader, CardTitle, CardDescription, CardFooter } from '@/primitives/ui/card';
import { Badge } from '@/primitives/ui/badge';
import { Input } from '@/primitives/ui/input';
import AdminPageShell from '../AdminPageShell.vue';

const t = useAiI18n();

export interface SkillItem {
  id: string;
  name: string;
  slug: string;
  description?: string;
  category?: string;
  isEnabled: boolean;
  isBuiltIn?: boolean;
}

const props = defineProps<{
  skills: SkillItem[];
  isLoading?: boolean;
}>();

const emit = defineEmits<{
  create: [];
  edit: [id: string];
  delete: [id: string];
  'batch-delete': [ids: string[]];
  'batch-enable': [ids: string[]];
  'batch-disable': [ids: string[]];
  import: [];
  export: [];
  refresh: [];
}>();

const selectedIds = ref<Set<string>>(new Set());

const { query: searchQuery, filtered: filteredSkills } = useLocalSearch(
  () => props.skills,
  ['name', 'slug'],
);

function toggleSelection(id: string): void {
  const next = new Set(selectedIds.value);
  if (next.has(id)) {
    next.delete(id);
  } else {
    next.add(id);
  }
  selectedIds.value = next;
}

function handleBatchDelete(): void {
  emit('batch-delete', [...selectedIds.value]);
  selectedIds.value = new Set();
}

function handleBatchEnable(): void {
  emit('batch-enable', [...selectedIds.value]);
  selectedIds.value = new Set();
}

function handleBatchDisable(): void {
  emit('batch-disable', [...selectedIds.value]);
  selectedIds.value = new Set();
}
</script>

<template>
  <AdminPageShell :is-loading="isLoading">
    <template #header>
      <div class="flex items-center justify-between">
        <h2 class="text-lg font-semibold">{{ t.admin.skills }}</h2>
        <div class="flex items-center gap-2">
          <Button variant="outline" size="sm" @click="emit('import')">
            <Icon icon="lucide:upload" class="mr-1.5 size-4" />
            {{ t.admin.import }}
          </Button>
          <Button variant="outline" size="sm" @click="emit('export')">
            <Icon icon="lucide:download" class="mr-1.5 size-4" />
            {{ t.admin.export }}
          </Button>
          <Button size="sm" @click="emit('create')">
            <Icon icon="lucide:plus" class="mr-1.5 size-4" />
            {{ t.admin.create }}
          </Button>
        </div>
      </div>
    </template>

    <template #toolbar>
      <div class="flex items-center gap-2">
        <div class="relative max-w-sm flex-1">
          <Icon icon="lucide:search" class="absolute left-2.5 top-2.5 size-4 text-muted-foreground" />
          <Input v-model="searchQuery" :placeholder="t.common.search" class="pl-9" />
        </div>
        <template v-if="selectedIds.size > 0">
          <Button variant="outline" size="sm" @click="handleBatchEnable">
            {{ t.admin.batchEnable }}
          </Button>
          <Button variant="outline" size="sm" @click="handleBatchDisable">
            {{ t.admin.batchDisable }}
          </Button>
          <Button variant="destructive" size="sm" @click="handleBatchDelete">
            {{ t.admin.batchDelete }}
          </Button>
        </template>
      </div>
    </template>

    <div v-if="filteredSkills.length === 0" class="py-12 text-center text-muted-foreground">
      {{ t.admin.noData }}
    </div>
    <div v-else class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
      <Card
        v-for="skill in filteredSkills"
        :key="skill.id"
        :class="['hover:shadow-md transition-shadow cursor-pointer', selectedIds.has(skill.id) && 'ring-2 ring-primary']"
        @click="toggleSelection(skill.id)"
      >
        <CardHeader class="pb-2">
          <div class="flex items-start justify-between">
            <CardTitle class="text-sm">{{ skill.name }}</CardTitle>
            <div class="flex items-center gap-1">
              <Badge v-if="skill.isBuiltIn" variant="secondary" class="text-[10px]">
                {{ t.skill.builtIn }}
              </Badge>
              <Badge :variant="skill.isEnabled ? 'default' : 'outline'" class="text-[10px]">
                {{ skill.isEnabled ? t.admin.active : t.admin.inactive }}
              </Badge>
            </div>
          </div>
          <CardDescription v-if="skill.description" class="text-xs line-clamp-2">
            {{ skill.description }}
          </CardDescription>
        </CardHeader>
        <CardFooter class="gap-1 pt-0">
          <Badge v-if="skill.category" variant="outline" class="text-[10px]">{{ skill.category }}</Badge>
          <div class="flex-1" />
          <Button variant="ghost" size="icon-sm" @click.stop="emit('edit', skill.id)">
            <Icon icon="lucide:pencil" class="size-3.5" />
          </Button>
          <Button
            v-if="!skill.isBuiltIn"
            variant="ghost"
            size="icon-sm"
            class="text-destructive"
            @click.stop="emit('delete', skill.id)"
          >
            <Icon icon="lucide:trash-2" class="size-3.5" />
          </Button>
        </CardFooter>
      </Card>
    </div>
  </AdminPageShell>
</template>
