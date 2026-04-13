<script setup lang="ts">
/**
 * ProviderConfig — Provider card list with default model display
 */

import {
  Button,
  Card,
  CardHeader,
  CardTitle,
  CardDescription,
  CardContent,
  CardFooter,
  Badge,
} from '../../primitives';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import AdminPageShell from '../AdminPageShell.vue';

const t = useAiI18n();

export interface ProviderItem {
  id: string;
  name: string;
  type: string;
  defaultModel?: string;
  isEnabled: boolean;
  modelCount?: number;
}

defineProps<{
  providers: ProviderItem[];
  isLoading?: boolean;
}>();

const emit = defineEmits<{
  create: [];
  edit: [id: string];
  delete: [id: string];
  'test-connection': [id: string];
  refresh: [];
}>();
</script>

<template>
  <AdminPageShell :is-loading="isLoading">
    <template #header>
      <div class="flex items-center justify-between">
        <h2 class="text-lg font-semibold">{{ t.admin.providers }}</h2>
        <Button size="sm" @click="emit('create')">
          <Icon icon="lucide:plus" class="mr-1.5 size-4" />
          {{ t.admin.create }}
        </Button>
      </div>
    </template>

    <div v-if="providers.length === 0" class="py-12 text-center text-muted-foreground">
      {{ t.admin.noData }}
    </div>
    <div v-else class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
      <Card v-for="provider in providers" :key="provider.id" class="hover:shadow-md transition-shadow">
        <CardHeader class="pb-2">
          <div class="flex items-start justify-between">
            <div class="flex items-center gap-2">
              <Icon icon="lucide:cloud" class="size-4 text-primary" />
              <CardTitle class="text-sm">{{ provider.name }}</CardTitle>
            </div>
            <Badge :variant="provider.isEnabled ? 'default' : 'outline'" class="text-[10px]">
              {{ provider.isEnabled ? t.admin.active : t.admin.inactive }}
            </Badge>
          </div>
          <CardDescription class="text-xs">{{ provider.type }}</CardDescription>
        </CardHeader>
        <CardContent class="pb-2">
          <div class="text-xs text-muted-foreground space-y-1">
            <div v-if="provider.defaultModel">
              <span class="font-medium">{{ t.model.current }}:</span> {{ provider.defaultModel }}
            </div>
            <div v-if="provider.modelCount">
              {{ provider.modelCount }} models
            </div>
          </div>
        </CardContent>
        <CardFooter class="gap-1 pt-0">
          <Button variant="ghost" size="icon-sm" @click="emit('test-connection', provider.id)">
            <Icon icon="lucide:wifi" class="size-3.5" />
          </Button>
          <Button variant="ghost" size="icon-sm" @click="emit('edit', provider.id)">
            <Icon icon="lucide:pencil" class="size-3.5" />
          </Button>
          <div class="flex-1" />
          <Button variant="ghost" size="icon-sm" class="text-destructive" @click="emit('delete', provider.id)">
            <Icon icon="lucide:trash-2" class="size-3.5" />
          </Button>
        </CardFooter>
      </Card>
    </div>
  </AdminPageShell>
</template>
