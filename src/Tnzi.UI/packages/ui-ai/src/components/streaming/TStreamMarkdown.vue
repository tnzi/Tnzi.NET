<script setup lang="ts">
/**
 * TStreamMarkdown - Streaming markdown renderer
 *
 * Watches `content` prop changes, feeds deltas into useStreamMarkdown composable,
 * and renders the resulting HTML. Handles full content replacement (e.g., branch
 * switch) by detecting when new content is shorter or doesn't start with old content.
 */

import { watch, onBeforeUnmount } from 'vue';
import { useStreamMarkdown } from '../../headless/useStreamMarkdown';
import { useAiI18n } from '../../i18n/index';

const props = withDefaults(defineProps<{
  /** Markdown content string (accumulated, not delta). */
  content: string;
  streaming?: boolean;
  class?: string;
}>(), {
  streaming: false,
});

const t = useAiI18n();
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
  if (copyLabelTimer != null) window.clearTimeout(copyLabelTimer);
});

let copyLabelTimer: number | null = null;

/** Event-delegated copy for fenced code blocks (buttons live in v-html output). */
function onMarkdownClick(e: MouseEvent): void {
  const target = e.target as HTMLElement;
  const btn = target.closest('.t-md-code__copy');
  if (!btn) return;
  const encoded = btn.closest('.t-md-code')?.getAttribute('data-code');
  if (!encoded) return;
  navigator.clipboard.writeText(decodeURIComponent(encoded)).catch(() => {
    /* clipboard unavailable */
  });
  const original = btn.textContent;
  btn.textContent = t.value.chat.copied;
  /* Tracked so unmounting before the label reverts does not leave a timer
     holding a detached DOM node. */
  if (copyLabelTimer != null) window.clearTimeout(copyLabelTimer);
  copyLabelTimer = window.setTimeout(() => {
    copyLabelTimer = null;
    btn.textContent = original;
  }, 2000);
}
</script>

<template>
  <div
    class="t-stream-markdown"
    :class="[streaming && 'ai-streaming', props.class]"
    v-html="html"
    @click="onMarkdownClick"
  />
</template>

<style scoped>
.t-stream-markdown {
  max-width: none;
  overflow-wrap: break-word;
  font-size: 0.875rem;
  line-height: 1.6;
}

/* Prose-like styles - replaces Tailwind prose + dark:prose-invert */
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
  font-size: 0.85em;
  font-weight: 600;
  font-family: var(--tnzi-ai-font-mono, monospace);
  background-color: var(--tnzi-ai-code-bg);
  border: 1px solid color-mix(in srgb, var(--tnzi-border) 60%, transparent);
  padding: 2px 6px;
  border-radius: 7px;
}
.t-stream-markdown :deep(pre) {
  background-color: var(--tnzi-ai-code-bg);
  padding: 0.95em 1em;
  border-radius: 12px;
  overflow-x: auto;
  margin-bottom: 0.75em;
  box-shadow: inset 0 1px 0 color-mix(in srgb, var(--tnzi-base-text) 6%, transparent);
}
.t-stream-markdown :deep(pre code) {
  background: none;
  border: none;
  padding: 0;
  font-weight: 400;
  font-size: 0.92em;
  line-height: 1.6;
}
.t-stream-markdown :deep(blockquote) {
  border-left: 3px solid var(--tnzi-ai-accent, var(--tnzi-primary));
  border-radius: 0 10px 10px 0;
  background: var(--tnzi-ai-accent-soft, rgba(13, 148, 136, 0.06));
  padding: 0.5em 1em;
  color: var(--tnzi-base-text-muted);
  font-style: normal;
  margin: 0.6em 0;
}
.t-stream-markdown :deep(a) {
  color: var(--tnzi-primary);
  text-decoration: none;
  border-bottom: 1px solid color-mix(in srgb, var(--tnzi-primary) 40%, transparent);
  transition: border-color 0.15s;
}
.t-stream-markdown :deep(a:hover) {
  border-bottom-color: var(--tnzi-primary);
}
.t-stream-markdown :deep(hr) {
  border: none;
  border-top: 1px solid var(--tnzi-border);
  margin: 1em 0;
}
.t-stream-markdown :deep(table) {
  width: 100%;
  border-collapse: separate;
  border-spacing: 0;
  border: 1px solid var(--tnzi-border);
  border-radius: 8px;
  overflow: hidden;
  font-size: 0.875em;
  margin-bottom: 0.75em;
}
.t-stream-markdown :deep(th),
.t-stream-markdown :deep(td) {
  padding: 8px 14px;
  border-bottom: 1px solid var(--tnzi-border);
  text-align: left;
}
.t-stream-markdown :deep(th) {
  background-color: var(--tnzi-ai-surface);
  font-weight: 600;
  font-size: 0.78em;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--tnzi-base-text-muted);
}
.t-stream-markdown :deep(td) {
  font-variant-numeric: tabular-nums;
}
.t-stream-markdown :deep(tr:last-child td) {
  border-bottom: none;
}
.t-stream-markdown :deep(tbody tr:hover td) {
  background-color: var(--tnzi-ai-hover);
}

/* Streaming cursor. Scoped to the LAST TOP-LEVEL block: `:deep(:last-child)`
   matched at any depth, so a trailing list or table sprouted a cursor on every
   nested last child at once. */
.ai-streaming > :deep(*:last-child)::after {
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

/* Fenced code block (shiki) wrapper - header bar + highlighted body */
.t-stream-markdown :deep(.t-md-code) {
  margin-bottom: 0.75em;
  border-radius: 12px;
  overflow: hidden;
  background-color: var(--tnzi-ai-code-bg);
  box-shadow: inset 0 1px 0 color-mix(in srgb, var(--tnzi-base-text) 6%, transparent);
}
.t-stream-markdown :deep(.t-md-code__bar) {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 4px 12px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  border-bottom: 1px solid color-mix(in srgb, var(--tnzi-border) 50%, transparent);
}
.t-stream-markdown :deep(.t-md-code__lang) {
  font-family: var(--tnzi-ai-font-mono, monospace);
  text-transform: lowercase;
}
.t-stream-markdown :deep(.t-md-code__copy) {
  border: none;
  background: none;
  color: var(--tnzi-base-text-muted);
  font: inherit;
  font-size: 12px;
  cursor: pointer;
  padding: 2px 6px;
  border-radius: 6px;
  transition: background 0.15s, color 0.15s;
}
.t-stream-markdown :deep(.t-md-code__copy:hover) {
  background: var(--tnzi-ai-hover, rgba(0, 0, 0, 0.04));
  color: var(--tnzi-ai-text);
}
.t-stream-markdown :deep(.t-md-code__body) {
  overflow-x: auto;
  padding: 0.85em 1em;
  font-size: 0.9em;
  line-height: 1.6;
}
.t-stream-markdown :deep(.t-md-code__body pre),
.t-stream-markdown :deep(.t-md-code__body .shiki),
.t-stream-markdown :deep(.t-md-code__fallback) {
  margin: 0;
  padding: 0;
  background: transparent !important;
  box-shadow: none;
}
/* shiki dual-theme: switch to dark token colors under .dark */
.dark .t-stream-markdown :deep(.shiki),
.dark .t-stream-markdown :deep(.shiki span) {
  color: var(--shiki-dark) !important;
  background-color: var(--shiki-dark-bg) !important;
  font-style: var(--shiki-dark-font-style) !important;
  font-weight: var(--shiki-dark-font-weight) !important;
}
</style>
