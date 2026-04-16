<script setup lang="ts">
/**
 * Suggestions — Suggestion buttons
 *
 * Horizontal scroll of pill-shaped suggestion buttons with staggered fade-in.
 */

import { NScrollbar, NButton } from 'naive-ui';
import { Icon } from '@iconify/vue';
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
  <NScrollbar x-scrollable class="w-full">
    <div class="flex gap-2 pb-1">
      <NButton
        v-for="(item, index) in suggestions"
        :key="index"
        secondary
        size="small"
        round
        class="shrink-0 animate-fade-in"
        :style="{ animationDelay: `${index * 50}ms`, animationFillMode: 'both' }"
        @click="emit('select', item.text)"
      >
        <template v-if="item.icon" #icon>
          <Icon :icon="item.icon" />
        </template>
        {{ item.text }}
      </NButton>
    </div>
  </NScrollbar>
</template>
