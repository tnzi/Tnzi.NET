<script setup lang="ts">
/**
 * TTaskItem — Subtask display with left vertical border and file badges
 */

import { ref } from 'vue';
import { Icon } from '@iconify/vue';
import { cn } from '@/lib/utils';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/primitives/ui/collapsible';
import { Badge } from '@/primitives/ui/badge';

const props = withDefaults(defineProps<{
  /** Task title. */
  title: string;
  /** Files associated with this task. */
  files?: string[];
  /** Whether the task is collapsible. */
  collapsible?: boolean;
  /** Default open state. */
  defaultOpen?: boolean;
}>(), {
  files: () => [],
  collapsible: true,
  defaultOpen: true,
});

const isOpen = ref(props.defaultOpen);
</script>

<template>
  <Collapsible v-if="collapsible" v-model:open="isOpen">
    <CollapsibleTrigger class="group flex w-full items-center gap-2 py-1.5 text-sm hover:bg-accent/50 rounded-md px-2 transition-colors">
      <Icon icon="lucide:search" class="size-3.5 shrink-0 text-muted-foreground" />
      <span class="flex-1 text-left font-medium truncate">{{ title }}</span>
      <Icon
        icon="lucide:chevron-down"
        :class="cn('size-3.5 shrink-0 text-muted-foreground transition-transform', isOpen && 'rotate-180')"
      />
    </CollapsibleTrigger>
    <CollapsibleContent class="data-[state=closed]:animate-out data-[state=open]:animate-in data-[state=open]:slide-in-from-top-2 data-[state=closed]:slide-out-to-top-2 data-[state=closed]:fade-out-0">
      <div class="ml-2 border-l-2 border-border pl-4 space-y-2 py-2">
        <slot />
        <div v-if="files.length > 0" class="flex flex-wrap gap-1">
          <Badge
            v-for="file in files"
            :key="file"
            variant="secondary"
            class="font-mono text-[11px]"
          >
            {{ file }}
          </Badge>
        </div>
      </div>
    </CollapsibleContent>
  </Collapsible>
  <div v-else class="flex items-start gap-2 py-1 text-sm text-muted-foreground">
    <slot />
    <div v-if="files.length > 0" class="flex flex-wrap gap-1">
      <Badge
        v-for="file in files"
        :key="file"
        variant="secondary"
        class="font-mono text-[11px]"
      >
        {{ file }}
      </Badge>
    </div>
  </div>
</template>
