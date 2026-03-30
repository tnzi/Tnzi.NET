<script setup lang="ts">
/**
 * TSuggestions — Suggestion buttons
 *
 * Horizontal scroll of pill-shaped suggestion buttons with staggered fade-in.
 */

import { Icon } from '@iconify/vue';
import { ScrollArea, ScrollBar } from '@/primitives/ui/scroll-area';
import { Button } from '@/primitives/ui/button';

export interface SuggestionItem {
  text: string;
  icon?: string;
}

defineProps<{
  suggestions: SuggestionItem[];
}>();

const emit = defineEmits<{
  select: [text: string];
}>();
</script>

<template>
  <ScrollArea class="w-full">
    <div class="flex gap-2 pb-1">
      <Button
        v-for="(item, index) in suggestions"
        :key="index"
        variant="outline"
        size="sm"
        class="shrink-0 rounded-full animate-fade-in"
        :style="{ animationDelay: `${index * 50}ms`, animationFillMode: 'both' }"
        @click="emit('select', item.text)"
      >
        <Icon v-if="item.icon" :icon="item.icon" class="size-3.5" />
        <span>{{ item.text }}</span>
      </Button>
    </div>
    <ScrollBar orientation="horizontal" />
  </ScrollArea>
</template>
