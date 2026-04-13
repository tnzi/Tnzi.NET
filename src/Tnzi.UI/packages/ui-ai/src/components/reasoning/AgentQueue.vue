<script setup lang="ts">
/**
 * AgentQueue — Task queue with collapsible sections
 */

import { ScrollArea } from '../../primitives';
import { reactive } from 'vue';
import { Icon } from '@iconify/vue';
import { cn } from '@/lib/utils';
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

const props = defineProps<{
  sections: QueueSection[];
  maxHeight?: number;
}>();

const emit = defineEmits<{
  'item-click': [item: QueueItem];
}>();

// 每个 section 独立的 open 状态（用 label 作 key）
const openSections = reactive(new Set<string>(
  props.sections.filter((s) => s.defaultOpen !== false).map((s) => s.label),
));

function toggleSection(label: string): void {
  if (openSections.has(label)) {
    openSections.delete(label);
  } else {
    openSections.add(label);
  }
}
</script>

<template>
  <div class="rounded-xl border shadow-xs overflow-hidden">
    <ScrollArea :style="{ maxHeight: `${maxHeight ?? 200}px` }">
      <div class="divide-y divide-border">
        <div
          v-for="section in sections"
          :key="section.label"
        >
          <button
            type="button"
            class="group flex w-full items-center gap-2 px-3 py-2 text-xs font-medium text-muted-foreground hover:bg-accent/50 transition-colors"
            @click="toggleSection(section.label)"
          >
            <Icon v-if="section.icon" :icon="section.icon" class="size-3.5 shrink-0" />
            <span class="flex-1 text-left">{{ section.label }}</span>
            <span class="text-[10px] tabular-nums">{{ section.items.length }}</span>
            <Icon icon="lucide:chevron-down" class="size-3 shrink-0 transition-transform" :class="!openSections.has(section.label) && '-rotate-90'" />
          </button>
          <ul v-show="openSections.has(section.label)" class="divide-y divide-border/50">
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
        </div>
      </div>
    </ScrollArea>
  </div>
</template>
