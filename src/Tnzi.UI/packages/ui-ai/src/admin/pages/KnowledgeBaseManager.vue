<script setup lang="ts">
/**
 * KnowledgeBaseManager — Knowledge base card list with create/delete/upload actions
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

export interface KnowledgeBaseItem {
  id: string;
  name: string;
  description?: string;
  documentCount: number;
  totalChunks: number;
  lastUpdated?: string;
}

defineProps<{
  bases: KnowledgeBaseItem[];
  isLoading?: boolean;
}>();

const emit = defineEmits<{
  create: [];
  delete: [id: string];
  upload: [id: string];
  'view-documents': [id: string];
  refresh: [];
}>();
</script>

<template>
  <AdminPageShell :is-loading="isLoading">
    <template #header>
      <div class="flex items-center justify-between">
        <h2 class="text-lg font-semibold">{{ t.admin.knowledge }}</h2>
        <Button size="sm" @click="emit('create')">
          <Icon icon="lucide:plus" class="mr-1.5 size-4" />
          {{ t.admin.create }}
        </Button>
      </div>
    </template>

    <div v-if="bases.length === 0" class="py-12 text-center text-muted-foreground">
      {{ t.admin.noData }}
    </div>
    <div v-else class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
      <Card v-for="kb in bases" :key="kb.id" class="hover:shadow-md transition-shadow">
        <CardHeader class="pb-2">
          <div class="flex items-start justify-between">
            <div class="flex items-center gap-2">
              <Icon icon="lucide:library" class="size-4 text-primary" />
              <CardTitle class="text-sm">{{ kb.name }}</CardTitle>
            </div>
          </div>
          <CardDescription v-if="kb.description" class="text-xs line-clamp-2">
            {{ kb.description }}
          </CardDescription>
        </CardHeader>
        <CardContent class="pb-2">
          <div class="flex items-center gap-3 text-xs text-muted-foreground">
            <Badge variant="outline" class="text-[10px]">{{ kb.documentCount }} docs</Badge>
            <Badge variant="outline" class="text-[10px]">{{ kb.totalChunks }} chunks</Badge>
          </div>
        </CardContent>
        <CardFooter class="gap-1 pt-0">
          <Button variant="ghost" size="icon-sm" @click="emit('upload', kb.id)">
            <Icon icon="lucide:upload" class="size-3.5" />
          </Button>
          <Button variant="ghost" size="icon-sm" @click="emit('view-documents', kb.id)">
            <Icon icon="lucide:files" class="size-3.5" />
          </Button>
          <div class="flex-1" />
          <Button variant="ghost" size="icon-sm" class="text-destructive" @click="emit('delete', kb.id)">
            <Icon icon="lucide:trash-2" class="size-3.5" />
          </Button>
        </CardFooter>
      </Card>
    </div>
  </AdminPageShell>
</template>
