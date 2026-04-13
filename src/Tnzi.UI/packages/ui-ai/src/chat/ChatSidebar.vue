<script setup lang="ts">
/**
 * ChatSidebar — Thread list with search, new chat, and agent gallery
 */

import { Button, Input, ScrollArea, Separator } from '../primitives';
import { Icon } from '@iconify/vue';
import { cn } from '@/lib/utils';
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
        <Button class="w-full gap-2" @click="emit('new-chat')">
          <Icon icon="lucide:plus" class="size-4" />
          {{ t.chat.newChat }}
        </Button>
      </slot>
      <div class="relative">
        <Icon icon="lucide:search" class="absolute left-2.5 top-2.5 size-4 text-muted-foreground" />
        <Input v-model="searchQuery" :placeholder="t.common.search" class="pl-9 h-8" />
      </div>
    </div>

    <Separator />

    <!-- Thread list -->
    <ScrollArea class="flex-1">
      <div class="p-2 space-y-0.5">
        <div
          v-for="thread in filtered"
          :key="thread.id"
          :class="cn(
            'group flex items-center gap-2 rounded-md px-2 py-2 text-sm cursor-pointer transition-colors',
            thread.id === activeThreadId
              ? 'bg-accent text-accent-foreground'
              : 'hover:bg-accent/50',
          )"
          @click="emit('select-thread', thread.id)"
        >
          <slot name="item" :thread="thread">
            <div class="flex-1 min-w-0">
              <div class="truncate font-medium">{{ thread.title }}</div>
              <div v-if="thread.lastMessage" class="truncate text-xs text-muted-foreground">
                {{ thread.lastMessage }}
              </div>
            </div>
            <span class="text-[10px] text-muted-foreground shrink-0">
              {{ formatDate(thread.updatedAt) }}
            </span>
            <Button
              variant="ghost"
              size="icon-sm"
              class="size-6 shrink-0 opacity-0 group-hover:opacity-100"
              @click.stop="emit('delete-thread', thread.id)"
            >
              <Icon icon="lucide:trash-2" class="size-3" />
            </Button>
          </slot>
        </div>
      </div>
    </ScrollArea>

    <!-- Footer slot -->
    <slot name="footer" />
  </div>
</template>
