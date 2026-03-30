<script setup lang="ts">
/**
 * PersonaManagement — Persona card grid CRUD
 */

import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { useLocalSearch } from '@/composables/useLocalSearch';
import { Button } from '@/primitives/ui/button';
import { Card, CardHeader, CardTitle, CardDescription, CardFooter } from '@/primitives/ui/card';
import { Badge } from '@/primitives/ui/badge';
import { Input } from '@/primitives/ui/input';
import AdminPageShell from '../AdminPageShell.vue';

const t = useAiI18n();

export interface PersonaItem {
  id: string;
  name: string;
  description?: string;
  systemPrompt?: string;
  isDefault?: boolean;
  createdAt: string;
}

const props = defineProps<{
  personas: PersonaItem[];
  isLoading?: boolean;
}>();

const emit = defineEmits<{
  create: [];
  edit: [id: string];
  delete: [id: string];
  'set-default': [id: string];
  refresh: [];
}>();

const { query: searchQuery, filtered: filteredPersonas } = useLocalSearch(
  () => props.personas,
  ['name', 'description'],
);
</script>

<template>
  <AdminPageShell :is-loading="isLoading">
    <template #header>
      <div class="flex items-center justify-between">
        <h2 class="text-lg font-semibold">{{ t.admin.personas }}</h2>
        <Button size="sm" @click="emit('create')">
          <Icon icon="lucide:plus" class="mr-1.5 size-4" />
          {{ t.admin.create }}
        </Button>
      </div>
    </template>

    <template #toolbar>
      <div class="relative max-w-sm">
        <Icon icon="lucide:search" class="absolute left-2.5 top-2.5 size-4 text-muted-foreground" />
        <Input v-model="searchQuery" :placeholder="t.common.search" class="pl-9" />
      </div>
    </template>

    <div v-if="filteredPersonas.length === 0" class="py-12 text-center text-muted-foreground">
      {{ t.admin.noData }}
    </div>
    <div v-else class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
      <Card v-for="persona in filteredPersonas" :key="persona.id" class="hover:shadow-md transition-shadow">
        <CardHeader class="pb-2">
          <div class="flex items-start justify-between">
            <div class="flex items-center gap-2">
              <Icon icon="lucide:user-circle" class="size-4 text-primary" />
              <CardTitle class="text-sm">{{ persona.name }}</CardTitle>
            </div>
            <Badge v-if="persona.isDefault" variant="default" class="text-[10px]">Default</Badge>
          </div>
          <CardDescription v-if="persona.description" class="text-xs line-clamp-2">
            {{ persona.description }}
          </CardDescription>
        </CardHeader>
        <CardFooter class="gap-1 pt-0">
          <Button
            v-if="!persona.isDefault"
            variant="ghost"
            size="sm"
            class="text-xs"
            @click="emit('set-default', persona.id)"
          >
            Set default
          </Button>
          <div class="flex-1" />
          <Button variant="ghost" size="icon-sm" @click="emit('edit', persona.id)">
            <Icon icon="lucide:pencil" class="size-3.5" />
          </Button>
          <Button variant="ghost" size="icon-sm" class="text-destructive" @click="emit('delete', persona.id)">
            <Icon icon="lucide:trash-2" class="size-3.5" />
          </Button>
        </CardFooter>
      </Card>
    </div>
  </AdminPageShell>
</template>
