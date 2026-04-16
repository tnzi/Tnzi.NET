<script setup lang="ts">
/**
 * ChatSidebar — Thread list with search, new chat, and agent gallery
 */

import { NButton, NInput, NScrollbar, NDivider } from 'naive-ui';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { useLocalSearch } from '@/composables/useLocalSearch';
const t = useAiI18n();

export interface ThreadItem {
  id: string;
  title: string;
  lastMessage?: string;
  updatedAt: string;
  isActive?: boolean;
}

const props = defineProps<{
  threads: ThreadItem[];
  activeThreadId?: string;
}>();

const emit = defineEmits<{
  'new-chat': [];
  'select-thread': [threadId: string];
  'delete-thread': [threadId: string];
}>();

const { query: searchQuery, filtered } = useLocalSearch(
  () => props.threads,
  ['title', 'lastMessage'],
);

function formatDate(dateStr: string): string {
  const date = new Date(dateStr);
  const now = new Date();
  const diffDays = Math.floor((now.getTime() - date.getTime()) / (1000 * 60 * 60 * 24));
  if (diffDays === 0) return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  if (diffDays < 7) return date.toLocaleDateString([], { weekday: 'short' });
  return date.toLocaleDateString([], { month: 'short', day: 'numeric' });
}
</script>

<template>
  <div class="flex h-full flex-col">
    <!-- Header -->
    <div class="p-3 space-y-2">
      <slot name="header">
        <NButton class="w-full" @click="emit('new-chat')">
          <template #icon><Icon icon="lucide:plus" /></template>
          {{ t.chat.newChat }}
        </NButton>
      </slot>
      <NInput v-model:value="searchQuery" :placeholder="t.common.search" size="small" clearable>
        <template #prefix>
          <Icon icon="lucide:search" class="size-4" />
        </template>
      </NInput>
    </div>

    <NDivider style="margin: 0" />

    <!-- Thread list -->
    <NScrollbar class="flex-1">
      <div class="p-2 space-y-0.5">
        <div
          v-for="thread in filtered"
          :key="thread.id"
          class="t-sidebar-thread"
          :class="{ 't-sidebar-thread--active': thread.id === activeThreadId }"
          @click="emit('select-thread', thread.id)"
        >
          <slot name="item" :thread="thread">
            <div class="flex-1 min-w-0">
              <div class="truncate font-medium">{{ thread.title }}</div>
              <div v-if="thread.lastMessage" class="truncate text-xs t-sidebar-thread__preview">
                {{ thread.lastMessage }}
              </div>
            </div>
            <span class="t-sidebar-thread__date">
              {{ formatDate(thread.updatedAt) }}
            </span>
            <NButton
              quaternary
              size="tiny"
              class="t-sidebar-thread__delete"
              @click.stop="emit('delete-thread', thread.id)"
            >
              <template #icon><Icon icon="lucide:trash-2" /></template>
            </NButton>
          </slot>
        </div>
      </div>
    </NScrollbar>

    <!-- Footer slot -->
    <slot name="footer" />
  </div>
</template>

<style scoped>
.t-sidebar-thread {
  display: flex;
  align-items: center;
  gap: 8px;
  border-radius: 6px;
  padding: 8px;
  font-size: 13px;
  cursor: pointer;
  transition: background-color 0.15s;
}
.t-sidebar-thread:hover {
  background-color: var(--tnzi-border);
}
.t-sidebar-thread--active {
  background-color: var(--tnzi-border);
  font-weight: 500;
}
.t-sidebar-thread__preview {
  color: var(--tnzi-base-text-muted);
}
.t-sidebar-thread__date {
  font-size: 10px;
  color: var(--tnzi-base-text-muted);
  flex-shrink: 0;
}
.t-sidebar-thread__delete {
  flex-shrink: 0;
  opacity: 0;
  transition: opacity 0.15s;
}
.t-sidebar-thread:hover .t-sidebar-thread__delete {
  opacity: 1;
}
</style>
