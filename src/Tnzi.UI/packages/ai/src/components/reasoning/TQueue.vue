<script setup lang="ts">
/**
 * TQueue — Task queue with collapsible sections
 */

import { Icon } from '@iconify/vue';
import { cn } from '@/lib/utils';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/primitives/ui/collapsible';
import { ScrollArea } from '@/primitives/ui/scroll-area';

export interface QueueSection {
  label: string;
  icon?: string;
  items: QueueItem[];
  defaultOpen?: boolean;
}

export interface QueueItem {
  id: string;
  title: string;
  description?: string;
  completed?: boolean;
  images?: string[];
  files?: string[];
}

defineProps<{
  sections: QueueSection[];
  maxHeight?: number;
}>();

const emit = defineEmits<{
  'item-click': [item: QueueItem];
}>();
</script>

<template>
  <div class="rounded-xl border shadow-xs overflow-hidden">
    <ScrollArea :style="{ maxHeight: `${maxHeight ?? 200}px` }">
      <div class="divide-y divide-border">
        <Collapsible
          v-for="section in sections"
          :key="section.label"
          :default-open="section.defaultOpen ?? true"
        >
          <CollapsibleTrigger class="group flex w-full items-center gap-2 px-3 py-2 text-xs font-medium text-muted-foreground hover:bg-accent/50 transition-colors">
            <Icon v-if="section.icon" :icon="section.icon" class="size-3.5 shrink-0" />
            <span class="flex-1 text-left">{{ section.label }}</span>
            <span class="text-[10px] tabular-nums">{{ section.items.length }}</span>
            <Icon icon="lucide:chevron-down" class="size-3 shrink-0 transition-transform group-data-[state=closed]:-rotate-90" />
          </CollapsibleTrigger>
          <CollapsibleContent>
            <ul class="divide-y divide-border/50">
              <li
                v-for="item in section.items"
                :key="item.id"
                class="group/item flex items-start gap-2 px-3 py-2 text-sm hover:bg-accent/30 cursor-pointer transition-colors"
                @click="emit('item-click', item)"
              >
                <span
                  :class="cn(
                    'mt-1 size-3.5 shrink-0 rounded-full border-2 flex items-center justify-center',
                    item.completed ? 'border-primary bg-primary text-primary-foreground' : 'border-muted-foreground/30',
                  )"
                >
                  <Icon v-if="item.completed" icon="lucide:check" class="size-2.5" />
                </span>
                <div class="flex-1 min-w-0">
                  <span :class="cn('block truncate', item.completed && 'line-through text-muted-foreground')">{{ item.title }}</span>
                  <span v-if="item.description" :class="cn('block text-xs text-muted-foreground mt-0.5 truncate', item.completed && 'line-through')">{{ item.description }}</span>
                  <div v-if="(item.images?.length ?? 0) > 0 || (item.files?.length ?? 0) > 0" class="flex flex-wrap gap-1 mt-1.5">
                    <img v-for="(img, i) in item.images" :key="`img-${i}`" :src="img" class="size-8 rounded object-cover" />
                    <span v-for="(file, i) in item.files" :key="`file-${i}`" class="inline-flex items-center gap-1 rounded-md bg-secondary border px-1.5 py-0.5 text-[11px] text-muted-foreground">
                      <Icon icon="lucide:paperclip" class="size-3" />
                      <span class="max-w-[80px] truncate">{{ file }}</span>
                    </span>
                  </div>
                </div>
              </li>
            </ul>
          </CollapsibleContent>
        </Collapsible>
      </div>
    </ScrollArea>
  </div>
</template>
