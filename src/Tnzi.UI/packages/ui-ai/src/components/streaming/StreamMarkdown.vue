<script setup lang="ts">
/**
 * StreamMarkdown — Streaming markdown renderer
 *
 * Watches `content` prop changes, feeds deltas into useStreamMarkdown composable,
 * and renders the resulting HTML. Handles full content replacement (e.g., branch
 * switch) by detecting when new content is shorter or doesn't start with old content.
 */

import { watch, onBeforeUnmount } from 'vue';
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
    class="t-stream-markdown"
    :class="[streaming && 'ai-streaming', props.class]"
    v-html="html"
  />
</template>

<style scoped>
.t-stream-markdown {
  max-width: none;
  overflow-wrap: break-word;
  font-size: 0.875rem;
  line-height: 1.6;
}

/* Prose-like styles — replaces Tailwind prose + dark:prose-invert */
.t-stream-markdown :deep(h1),
.t-stream-markdown :deep(h2),
.t-stream-markdown :deep(h3),
.t-stream-markdown :deep(h4) {
  font-weight: 600;
  margin-top: 1em;
  margin-bottom: 0.5em;
  color: var(--tnzi-base-text);
}
.t-stream-markdown :deep(p) { margin-bottom: 0.75em; }
.t-stream-markdown :deep(p:last-child) { margin-bottom: 0; }
.t-stream-markdown :deep(ul),
.t-stream-markdown :deep(ol) {
  padding-left: 1.25em;
  margin-bottom: 0.75em;
}
.t-stream-markdown :deep(li) { margin-bottom: 0.25em; }
.t-stream-markdown :deep(code) {
  font-size: 0.8125em;
  font-family: monospace;
  background-color: var(--tnzi-ai-code-bg);
  padding: 1px 4px;
  border-radius: 3px;
}
.t-stream-markdown :deep(pre) {
  background-color: var(--tnzi-ai-code-bg);
  padding: 12px;
  border-radius: 6px;
  overflow-x: auto;
  margin-bottom: 0.75em;
}
.t-stream-markdown :deep(pre code) {
  background: none;
  padding: 0;
}
.t-stream-markdown :deep(blockquote) {
  border-left: 3px solid var(--tnzi-border);
  padding-left: 1em;
  color: var(--tnzi-base-text-muted);
  margin: 0.5em 0;
}
.t-stream-markdown :deep(a) {
  color: var(--tnzi-primary);
  text-decoration: underline;
}
.t-stream-markdown :deep(hr) {
  border: none;
  border-top: 1px solid var(--tnzi-border);
  margin: 1em 0;
}
.t-stream-markdown :deep(table) {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.875em;
}
.t-stream-markdown :deep(th),
.t-stream-markdown :deep(td) {
  border: 1px solid var(--tnzi-border);
  padding: 4px 8px;
}
.t-stream-markdown :deep(th) {
  background-color: var(--tnzi-container-bg);
  font-weight: 600;
}

/* Streaming cursor */
.ai-streaming :deep(:last-child)::after {
  content: '\25CF';
  display: inline-block;
  width: 6px;
  height: 6px;
  margin-left: 2px;
  color: var(--tnzi-ai-node-active);
  animation: t-md-pulse 1s ease-in-out infinite;
}

@keyframes t-md-pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.3; }
}
</style>
