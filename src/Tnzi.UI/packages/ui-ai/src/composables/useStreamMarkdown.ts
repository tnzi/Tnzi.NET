/**
 * useStreamMarkdown — Streaming markdown rendering composable
 *
 * Incrementally parses markdown chunks and produces reactive HTML output.
 * Uses a shared markdown-it singleton with sensible defaults (html, linkify, typographer).
 */

import { ref, readonly, type Ref, type DeepReadonly } from 'vue';
import MarkdownIt from 'markdown-it';
import { scheduleFrame } from '@/lib/scheduleFrame';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface UseStreamMarkdownOptions {
  /** Custom markdown-it instance (overrides built-in shared singleton). */
  markdownIt?: MarkdownIt;
}

export interface UseStreamMarkdownReturn {
  /** Rendered HTML (reactive). */
  html: DeepReadonly<Ref<string>>;
  /** Raw accumulated markdown text (reactive). */
  rawText: DeepReadonly<Ref<string>>;
  /** Whether streaming is in progress (reactive). */
  isStreaming: DeepReadonly<Ref<boolean>>;
  /** Append a text chunk (incremental delta). */
  append: (chunk: string) => void;
  /** Re-render current rawText to HTML (useful after plugin changes). */
  flush: () => void;
  /** Mark streaming as finished and do a final render. */
  finish: () => void;
  /** Reset all state. */
  reset: () => void;
}

// ---------------------------------------------------------------------------
// Markdown-it factory
// ---------------------------------------------------------------------------

function createMarkdownIt(): MarkdownIt {
  const md = new MarkdownIt({
    html: true,
    linkify: true,
    typographer: true,
  });

  // Custom fence renderer — adds language class for code blocks
  const defaultFence = md.renderer.rules.fence;
  md.renderer.rules.fence = (tokens, idx, options, env, self) => {
    const token = tokens[idx];
    const lang = token?.info?.trim() ?? '';
    const langClass = lang ? ` class="language-${lang}"` : '';
    const content = md.utils.escapeHtml(token?.content ?? '');
    // If a default renderer exists, use it; otherwise provide a sensible fallback
    if (defaultFence) {
      return defaultFence(tokens, idx, options, env, self);
    }
    return `<pre><code${langClass}>${content}</code></pre>\n`;
  };

  return md;
}

// ---------------------------------------------------------------------------
// Cached singleton markdown-it instance (shared across all composables)
// ---------------------------------------------------------------------------

let cachedMd: MarkdownIt | null = null;

function getSharedMarkdownIt(): MarkdownIt {
  if (!cachedMd) {
    cachedMd = createMarkdownIt();
  }
  return cachedMd;
}

// ---------------------------------------------------------------------------
// Composable
// ---------------------------------------------------------------------------

export function useStreamMarkdown(options: UseStreamMarkdownOptions = {}): UseStreamMarkdownReturn {
  const { markdownIt } = options;

  const md = markdownIt ?? getSharedMarkdownIt();

  const html = ref('');
  const rawText = ref('');
  const isStreaming = ref(false);

  let renderPending = false;

  function render(): void {
    html.value = md.render(rawText.value);
  }

  function append(chunk: string): void {
    if (!isStreaming.value) {
      isStreaming.value = true;
    }
    rawText.value = rawText.value + chunk;
    if (!renderPending) {
      renderPending = true;
      scheduleFrame(() => {
        renderPending = false;
        render();
      });
    }
  }

  function flush(): void {
    if (renderPending) {
      renderPending = false;
    }
    render();
  }

  function finish(): void {
    flush();
    isStreaming.value = false;
  }

  function reset(): void {
    html.value = '';
    rawText.value = '';
    isStreaming.value = false;
  }

  return {
    html: readonly(html),
    rawText: readonly(rawText),
    isStreaming: readonly(isStreaming),
    append,
    flush,
    finish,
    reset,
  };
}
