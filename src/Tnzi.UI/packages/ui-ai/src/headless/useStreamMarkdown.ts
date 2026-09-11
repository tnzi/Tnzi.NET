/**
 * useStreamMarkdown - Streaming markdown rendering composable
 *
 * Incrementally parses markdown chunks and produces reactive HTML output.
 * Uses a shared markdown-it instance with linkify + typographer. Raw HTML in the
 * source is OFF unless the caller passes `allowHtml: true` (assistant output is
 * untrusted; see the fence renderer for the one place author input reaches an
 * attribute).
 */

import { ref, readonly, type Ref, type DeepReadonly } from 'vue';
// markdown-it 15 ships its own declarations (`@types/markdown-it` is gone) and
// splits the two meanings of the old name: the default export is the callable
// factory/constructor, `MarkdownIt` is the instance type.
import MarkdownIt, { type MarkdownIt as MarkdownItInstance } from 'markdown-it';
import type { Highlighter } from 'shiki';
import { scheduleFrame } from '../utils/scheduleFrame';

// ---------------------------------------------------------------------------
// Shiki syntax highlighter - lazily loaded once, shared across all instances.
// markdown-it's fence renderer is synchronous, so we preload a highlighter
// (async) and re-render once it's ready. Until then code blocks fall back to
// escaped <pre>.
// ---------------------------------------------------------------------------

const SHIKI_LANGS = [
  'javascript', 'typescript', 'jsx', 'tsx', 'python', 'json', 'bash', 'shell',
  'html', 'css', 'vue', 'go', 'rust', 'java', 'csharp', 'cpp', 'sql', 'yaml',
  'markdown', 'diff',
] as const;
const langSet = new Set<string>(SHIKI_LANGS);

let highlighter: Highlighter | null = null;
let highlighterPromise: Promise<Highlighter> | null = null;

function ensureHighlighter(): Promise<Highlighter> {
  if (!highlighterPromise) {
    highlighterPromise = import('shiki')
      .then((shiki) =>
        shiki.createHighlighter({
          themes: ['github-light', 'github-dark'],
          langs: [...SHIKI_LANGS],
        }),
      )
      .then((h) => {
        highlighter = h;
        return h;
      })
      .catch((e) => {
        highlighterPromise = null;
        throw e;
      });
  }
  return highlighterPromise;
}

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface UseStreamMarkdownOptions {
  /** Custom markdown-it instance (overrides built-in shared singleton). */
  markdownIt?: MarkdownItInstance;
  /**
   * Allow raw HTML passthrough. Default false - raw `<tags>` in the markdown
   * source are escaped (`&lt;`), neutralising XSS from LLM/RAG output. Markdown
   * syntax (tables, code, lists, emphasis) still renders normally. Only set
   * true if you sanitize the input yourself.
   */
  allowHtml?: boolean;
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

function createMarkdownIt(allowHtml = false): MarkdownItInstance {
  const md = new MarkdownIt({
    html: allowHtml,
    linkify: true,
    typographer: true,
  });

  // Custom fence renderer - wraps each code block in a header bar (language +
  // copy button) and syntax-highlights via shiki once the shared highlighter is
  // ready. Copy is handled by event delegation in StreamMarkdown.vue.
  md.renderer.rules.fence = (tokens, idx) => {
    const token = tokens[idx];
    const info = (token?.info ?? '').trim();
    const lang = info.split(/\s+/)[0] ?? '';
    const code = token?.content ?? '';

    // `lang` is the first token of the fence info string, i.e. author input.
    // The shiki path only accepts it through the `langSet` allow-list, but the
    // fallback path used to interpolate it raw into a class attribute: an info
    // string of `js"onmouseover="alert(1)` (no whitespace, so the split keeps
    // it whole) closed the quote and planted a handler on the <code> element
    // of a page rendered from an assistant's or another user's message.
    const langAttr = lang ? ` class="language-${md.utils.escapeHtml(lang)}"` : '';
    const fallback = (): string =>
      `<pre class="t-md-code__fallback"><code${langAttr}>${md.utils.escapeHtml(code)}</code></pre>`;

    let bodyHtml: string;
    if (highlighter && lang && langSet.has(lang)) {
      try {
        bodyHtml = highlighter.codeToHtml(code, {
          lang,
          themes: { light: 'github-light', dark: 'github-dark' },
        });
      } catch {
        bodyHtml = fallback();
      }
    } else {
      bodyHtml = fallback();
    }

    const dataCode = encodeURIComponent(code);
    const langLabel = md.utils.escapeHtml(lang);
    return (
      `<div class="t-md-code" data-code="${dataCode}">` +
      `<div class="t-md-code__bar"><span class="t-md-code__lang">${langLabel}</span>` +
      `<button type="button" class="t-md-code__copy" aria-label="Copy code">Copy</button></div>` +
      `<div class="t-md-code__body">${bodyHtml}</div></div>`
    );
  };

  return md;
}

// ---------------------------------------------------------------------------
// Cached singleton markdown-it instance (shared across all composables)
// ---------------------------------------------------------------------------

let cachedMd: MarkdownItInstance | null = null;

function getSharedMarkdownIt(): MarkdownItInstance {
  if (!cachedMd) {
    cachedMd = createMarkdownIt();
  }
  return cachedMd;
}

// ---------------------------------------------------------------------------
// Composable
// ---------------------------------------------------------------------------

export function useStreamMarkdown(options: UseStreamMarkdownOptions = {}): UseStreamMarkdownReturn {
  const { markdownIt, allowHtml = false } = options;

  const md = markdownIt ?? (allowHtml ? createMarkdownIt(true) : getSharedMarkdownIt());

  const html = ref('');
  const rawText = ref('');
  const isStreaming = ref(false);

  let renderPending = false;

  function render(): void {
    html.value = md.render(rawText.value);
  }

  // Re-render once shiki is ready so already-rendered code blocks get highlighted.
  ensureHighlighter()
    .then(() => {
      if (rawText.value) render();
    })
    .catch(() => {
      /* shiki unavailable - fenced code stays as escaped <pre> */
    });

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
