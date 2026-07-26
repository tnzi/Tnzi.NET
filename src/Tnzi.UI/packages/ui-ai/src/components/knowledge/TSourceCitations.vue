<script setup lang="ts">
/**
 * TSourceCitations - Citation source badge list (collapsible)
 */

import { ref } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
const t = useAiI18n();

const isOpen = ref(false);

export interface Citation {
  title: string;
  url: string;
}

defineProps<{
  citations: Citation[];
}>();
</script>

<template>
  <div v-if="citations.length > 0" class="text-xs not-prose mb-4">
    <button
      type="button"
      class="inline-flex items-center gap-1 text-primary hover:underline cursor-pointer"
      @click="isOpen = !isOpen"
    >
      <Icon icon="lucide:book" class="size-3" />
      {{ t.knowledge.usedSources.replace('{count}', String(citations.length)) }}
      <Icon icon="lucide:chevron-down" class="size-3 transition-transform" :class="isOpen && 'rotate-180'" />
    </button>
    <div v-show="isOpen" class="mt-2 space-y-1">
      <a
        v-for="(citation, i) in citations"
        :key="i"
        :href="citation.url"
        target="_blank"
        rel="noopener noreferrer"
        class="flex items-center gap-1.5 rounded-md px-2 py-1 text-muted-foreground hover:bg-accent/50 hover:text-foreground transition-colors"
      >
        <Icon icon="lucide:book" class="size-3 shrink-0" />
        <span class="truncate">{{ citation.title }}</span>
      </a>
    </div>
  </div>
</template>
