<script setup lang="ts">
/**
 * TStreamMarkdown — Streaming markdown renderer
 *
 * Watches `content` prop changes, feeds deltas into useStreamMarkdown composable,
 * and renders the resulting HTML. Handles full content replacement (e.g., branch
 * switch) by detecting when new content is shorter or doesn't start with old content.
 */

import { watch, onBeforeUnmount } from 'vue';
import { cn } from '@/lib/utils';
import { useStreamMarkdown } from '@/composables/useStreamMarkdown';

const props = withDefaults(defineProps<{
  /** Markdown content string (accumulated, not delta). */
  content: string;
  streaming?: boolean;
  class?: string;
}>(), {
  streaming: false,
});

const { html, append, finish, reset } = useStreamMarkdown();

/** Track the previously seen content to compute deltas. */
let previousContent = '';

watch(
  () => props.content,
  (newContent) => {
    if (!newContent) {
      reset();
      previousContent = '';
      return;
    }

    // Detect full replacement (branch switch or content reset)
    if (!newContent.startsWith(previousContent)) {
      reset();
      append(newContent);
      previousContent = newContent;
      return;
    }

    // Compute delta and append only new characters
    const delta = newContent.slice(previousContent.length);
    if (delta) {
      append(delta);
    }
    previousContent = newContent;
  },
  { immediate: true },
);

watch(
  () => props.streaming,
  (isStreaming) => {
    if (!isStreaming) {
      finish();
    }
  },
);

onBeforeUnmount(() => {
  reset();
});
</script>

<template>
  <div
    :class="cn(
      'prose prose-sm dark:prose-invert max-w-none break-words',
      streaming && 'ai-streaming',
      props.class,
    )"
    v-html="html"
  />
</template>

<style scoped>
.ai-streaming :deep(:last-child)::after {
  content: '\25CF';
  display: inline-block;
  width: 6px;
  height: 6px;
  margin-left: 2px;
  color: hsl(var(--ai-streaming-cursor));
  animation: pulse 1s ease-in-out infinite;
}

@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.3; }
}
</style>
