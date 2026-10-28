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
/* The copy button on fenced code blocks speaks the interface language. */
const { html, append, finish, reset } = useStreamMarkdown({
  labels: () => ({ copy: t.value.chat.copy, copyCode: t.value.chat.copyCode }),
});

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
    } else {
      // Compute delta and append only new characters
      const delta = newContent.slice(previousContent.length);
      if (delta) {
        append(delta);
      }
    }
    previousContent = newContent;

    /* Content that is not streaming (a message from history, a finished
       answer) renders now rather than in the next frame. Waiting a frame is
       for coalescing a stream's deltas; for a whole message it only meant the
       first render of a thread had empty bodies that a second pass then
       filled, and the thread's scroller measured that empty height when it
       opened at the bottom. */
    if (!props.streaming) finish();
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
  const original = btn.getAttribute('aria-label') ?? '';
  btn.classList.add('is-copied');
  btn.setAttribute('aria-label', t.value.chat.copied);
  btn.setAttribute('title', t.value.chat.copied);
  /* Tracked so unmounting before the label reverts does not leave a timer
     holding a detached DOM node. */
  if (copyLabelTimer != null) window.clearTimeout(copyLabelTimer);
  copyLabelTimer = window.setTimeout(() => {
    copyLabelTimer = null;
    btn.classList.remove('is-copied');
    btn.setAttribute('aria-label', original);
    btn.setAttribute('title', original);
  }, 2000);
}
</script>

<template>
  <div
    class="t-stream-markdown t-ai-scroll-scope"
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

/* Message prose, proportioned after claude.ai's message rendering (measured
   at a 15px body; sizes here are in em, so they follow the body size). Every
   colour is a `--tnzi-ai-*` token or derived from the text token with alpha,
   so dark follows: LINE is the text at 10%, TINT the text at 5%. The package's
   reset (UnoCSS preflight) zeroes heading sizes and list markers, so both are
   set here explicitly.

   Rhythm: blocks are spaced by their top margin (the first has none).
   Paragraphs 0.5em, lists 0.75em (1.5em when a list follows a list), framed
   or ruled blocks (quote, rule, table, code) 1.5em. */
.t-stream-markdown {
  --t-md-line: color-mix(in srgb, var(--tnzi-ai-text) 10%, transparent);
  --t-md-tint: color-mix(in srgb, var(--tnzi-ai-text) 5%, transparent);
}
.t-stream-markdown :deep(p),
.t-stream-markdown :deep(ul),
.t-stream-markdown :deep(ol),
.t-stream-markdown :deep(blockquote),
.t-stream-markdown :deep(hr),
.t-stream-markdown :deep(pre),
.t-stream-markdown :deep(h1),
.t-stream-markdown :deep(h2),
.t-stream-markdown :deep(h3),
.t-stream-markdown :deep(h4),
.t-stream-markdown :deep(h5),
.t-stream-markdown :deep(h6) {
  margin: 0;
}
.t-stream-markdown :deep(p) { margin-top: 0.5em; }
.t-stream-markdown :deep(ul),
.t-stream-markdown :deep(ol) { margin-top: 0.75em; }
.t-stream-markdown :deep(ul + ul),
.t-stream-markdown :deep(ul + ol),
.t-stream-markdown :deep(ol + ul),
.t-stream-markdown :deep(ol + ol) { margin-top: 1.5em; }
.t-stream-markdown :deep(blockquote),
.t-stream-markdown :deep(hr),
.t-stream-markdown :deep(pre),
.t-stream-markdown :deep(.t-md-code),
.t-stream-markdown :deep(.t-md-table) { margin-top: 1.5em; }

/* Headings: semi-bold, close to the body size (a `#` is a section title, not a
   page title). Margins are 7.5px at a 15px body, written in each level's own em. */
.t-stream-markdown :deep(h1),
.t-stream-markdown :deep(h2),
.t-stream-markdown :deep(h3),
.t-stream-markdown :deep(h4),
.t-stream-markdown :deep(h5),
.t-stream-markdown :deep(h6) {
  font-weight: 580;
  color: inherit;
}
.t-stream-markdown :deep(h1) { font-size: 1.373em; line-height: 1.25; margin-top: 0.364em; }
.t-stream-markdown :deep(h2) { font-size: 1.127em; line-height: 1.3; margin-top: 0.444em; }
.t-stream-markdown :deep(h3) { font-size: 1em; line-height: 1.3; margin-top: 0.5em; }
.t-stream-markdown :deep(h4),
.t-stream-markdown :deep(h5),
.t-stream-markdown :deep(h6) { font-size: 0.873em; line-height: 1.3; margin-top: 0.573em; }
.t-stream-markdown :deep(h6) { color: var(--tnzi-ai-text-secondary); }

.t-stream-markdown :deep(strong) { font-weight: 600; }

/* Lists: markers outside, 2em indent, a little room between marker and text. */
.t-stream-markdown :deep(ul),
.t-stream-markdown :deep(ol) { padding-left: 2em; }
.t-stream-markdown :deep(ul) { list-style: disc; }
.t-stream-markdown :deep(ul ul) { list-style: circle; }
.t-stream-markdown :deep(ul ul ul) { list-style: square; }
.t-stream-markdown :deep(ol) { list-style: decimal; }
.t-stream-markdown :deep(ol ol) { list-style: lower-alpha; }
.t-stream-markdown :deep(ol ol ol) { list-style: lower-roman; }
.t-stream-markdown :deep(li) { padding-left: 0.5em; margin-top: 0.25em; }
.t-stream-markdown :deep(li:first-child) { margin-top: 0; }
.t-stream-markdown :deep(li > ul),
.t-stream-markdown :deep(li > ol) { margin-top: 0.25em; }
.t-stream-markdown :deep(li > p) { margin-top: 0; }

/* Task lists: the checkbox takes the marker's place; a done item is struck
   through in the secondary colour. */
.t-stream-markdown :deep(.t-md-tasks) { list-style: none; }
.t-stream-markdown :deep(.t-md-task) { position: relative; }
.t-stream-markdown :deep(.t-md-task__box) {
  position: absolute;
  left: -1.1em;
  top: 0.35em;
  width: 0.87em;
  height: 0.87em;
  margin: 0;
  accent-color: var(--tnzi-ai-text-secondary);
}
.t-stream-markdown :deep(.t-md-task.is-checked) {
  color: var(--tnzi-ai-text-secondary);
  text-decoration: line-through;
}

.t-stream-markdown :deep(blockquote) {
  padding-left: 1em;
  border-left: 4px solid var(--t-md-line);
  color: var(--tnzi-ai-text-secondary);
}

.t-stream-markdown :deep(hr) {
  border: none;
  border-top: 1px solid var(--t-md-line);
}

.t-stream-markdown :deep(a) {
  color: var(--tnzi-ai-accent-text);
  text-decoration: underline;
  text-decoration-thickness: 1px;
  text-underline-offset: 2px;
}

.t-stream-markdown :deep(img) {
  display: inline-block;
  max-width: 100%;
  height: auto;
  border-radius: 8px;
  vertical-align: middle;
}

/* Inline code: muted red on a faint tint, a hairline box. */
.t-stream-markdown :deep(code) {
  font-family: var(--tnzi-ai-font-mono, monospace);
  font-size: 0.9em;
  color: var(--tnzi-ai-code-inline-text);
  background-color: var(--t-md-tint);
  border: 1px solid var(--t-md-line);
  padding: 0.06em 0.25em;
  border-radius: 0.4em;
  -webkit-box-decoration-break: clone;
  box-decoration-break: clone;
}
/* An indented code block (no fence): the same frame as a fenced one. */
.t-stream-markdown :deep(pre) {
  position: relative;
  overflow-x: auto;
  padding: 14px;
  background-color: var(--tnzi-ai-code-frame-bg);
  border: 1px solid var(--t-md-line);
  border-radius: 12px;
}
.t-stream-markdown :deep(pre code) {
  color: var(--tnzi-ai-text);
  background: none;
  border: none;
  padding: 0;
  border-radius: 0;
  font-size: 0.933em;
  line-height: var(--tnzi-ai-code-line-height, 1.5);
}

/* Tables: the header is the one tinted row; the body sits on the message's
   background with hairlines between rows. The rounded outline is the table's
   own border (collapsed borders cannot be rounded). A wide table scrolls in
   its wrapper (`.t-md-table`, added by the renderer). Column alignment from
   the markdown is an inline style and wins over `text-align: left`. */
.t-stream-markdown :deep(.t-md-table) {
  position: relative;
  overflow-x: auto;
}
.t-stream-markdown :deep(table) {
  width: 100%;
  border-collapse: separate;
  border-spacing: 0;
  border: 1px solid var(--t-md-line);
  border-radius: 7px;
  font-variant-numeric: tabular-nums;
}
.t-stream-markdown :deep(th),
.t-stream-markdown :deep(td) {
  padding: 0.44em 0.75em;
  text-align: left;
  vertical-align: top;
}
.t-stream-markdown :deep(th) {
  font-weight: 500;
  white-space: nowrap;
  background-color: var(--t-md-tint);
  border-bottom: 1px solid var(--t-md-line);
}
.t-stream-markdown :deep(thead th:first-child) { border-top-left-radius: 6px; }
.t-stream-markdown :deep(thead th:last-child) { border-top-right-radius: 6px; }
.t-stream-markdown :deep(tbody tr + tr td) { border-top: 1px solid var(--t-md-line); }

/* The first block starts flush with the message. After the block margins: same
   specificity, so the later rule wins. */
.t-stream-markdown > :deep(:first-child) { margin-top: 0; }

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

/* Fenced code block: a frame a shade lighter than the canvas, a small language
   label at its top-left and an icon-only copy button at its top-right. No
   header bar. The button sits in a zero-height sticky row, so it stays in
   reach down a long block and stops at the block's end; the frame does not
   clip (`overflow` would stop the sticky row), and the body scrolls sideways
   on its own. */
.t-stream-markdown :deep(.t-md-code) {
  position: relative;
  border: 1px solid var(--t-md-line);
  border-radius: 12px;
  background-color: var(--tnzi-ai-code-frame-bg);
}
.t-stream-markdown :deep(.t-md-code__bar) {
  position: sticky;
  top: 0;
  z-index: 1;
  height: 0;
  display: flex;
  justify-content: flex-end;
}
.t-stream-markdown :deep(.t-md-code__copy) {
  width: 28px;
  height: 28px;
  margin: 8px 8px 0 0;
  border: none;
  border-radius: 6px;
  background: none;
  color: var(--tnzi-ai-text-secondary);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  transition: background 0.15s, color 0.15s;
}
.t-stream-markdown :deep(.t-md-code__copy:hover) {
  background: var(--t-md-tint);
  color: var(--tnzi-ai-text);
}
.t-stream-markdown :deep(.t-md-code__icon) { display: block; }
.t-stream-markdown :deep(.t-md-code__icon--copied) { display: none; }
.t-stream-markdown :deep(.t-md-code__copy.is-copied .t-md-code__icon--copy) { display: none; }
.t-stream-markdown :deep(.t-md-code__copy.is-copied .t-md-code__icon--copied) { display: block; }
.t-stream-markdown :deep(.t-md-code__lang) {
  padding: 14px 48px 0 14px;
  font-size: 12px;
  line-height: 16px;
  color: var(--tnzi-ai-text-secondary);
}
.t-stream-markdown :deep(.t-md-code__body) {
  overflow-x: auto;
  position: relative;
  padding: 14px;
  font-size: 0.933em;
  line-height: var(--tnzi-ai-code-line-height, 1.5);
}
.t-stream-markdown :deep(.t-md-code__body pre),
.t-stream-markdown :deep(.t-md-code__body .shiki),
.t-stream-markdown :deep(.t-md-code__fallback) {
  margin: 0;
  padding: 0;
  border: none;
  border-radius: 0;
  overflow: visible;
  background: transparent !important;
}
/* Inside a fenced block the body sets the size; the code inherits it. */
.t-stream-markdown :deep(.t-md-code__body code) { font-size: inherit; line-height: inherit; }
/* The shiki dual-theme dark swap is NOT here any more: it is a single unscoped
   rule in styles/index.css, so the other components that render highlighted
   code get it too. See the comment there before adding a copy back. */
</style>
