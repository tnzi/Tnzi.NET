/**
 * The Claude Code CLI's built-in web tools, read for a person: `WebSearch` (what was
 * searched for, and the pages it found) and `WebFetch` (which page was read).
 *
 * `WebSearch` returns text, not data. As the CLI writes it today:
 *
 *     Web search results for query: "tauri deep link"
 *
 *     Links: [{"title":"Deep Linking | Tauri","url":"https://v2.tauri.app/plugin/deep-linking/"}, ...]
 *
 *     <the search model's notes on what it found>
 *
 *     REMINDER: You MUST include the sources above in your response ...
 *
 * with one `Links:` list per round of searching (or "No links found."). That wording
 * belongs to the CLI and can change in any release, so the reading is forgiving: a
 * list that does not parse stays part of the text, and text that matches none of it
 * comes back as it is. Nothing here throws.
 */
import { linkDomain, parseJsonSafely, toolLinks, webHref, type ToolLink } from './tool-views';

/** The CLI's tool names these views are for. */
export const WEB_SEARCH_TOOL = 'WebSearch';
export const WEB_FETCH_TOOL = 'WebFetch';

/** A `WebSearch` result, read. */
export interface WebSearchResult {
  /** The query the result says it answers, when it says. */
  readonly query: string | null;
  /** The pages found, in order, each once. */
  readonly links: readonly ToolLink[];
  /** Whatever else the result says (the search model's notes), or null. */
  readonly text: string | null;
}

const QUERY_LINE = /^[ \t]*Web search results for query:[ \t]*"(.*)"[ \t]*$/m;
const LINKS_LABEL = /Links:[ \t]*\[/g;
const NO_LINKS_LINE = /^[ \t]*No links found\.?[ \t]*$/gim;
/** The CLI's instruction to the model, not meant for the reader. */
const REMINDER_LINE = /^[ \t]*REMINDER:[^\n]*$/gim;

/** Read a `WebSearch` result's text. Never throws; unreadable text comes back as `text`. */
export function parseWebSearchResult(raw: string | null | undefined): WebSearchResult {
  const source = typeof raw === 'string' ? raw : '';
  try {
    const links: ToolLink[] = [];
    const kept: string[] = [];
    let cursor = 0;
    LINKS_LABEL.lastIndex = 0;
    let match: RegExpExecArray | null;
    while ((match = LINKS_LABEL.exec(source)) !== null) {
      const open = match.index + match[0].length - 1;
      const close = closingBracket(source, open);
      if (close < 0) break;
      const pages = toolLinks(parseJsonSafely(source.slice(open, close + 1)));
      if (pages) {
        for (const page of pages) if (!links.some((known) => known.url === page.url)) links.push(page);
        kept.push(source.slice(cursor, match.index));
        cursor = close + 1;
      }
      LINKS_LABEL.lastIndex = close + 1;
    }
    kept.push(source.slice(cursor));

    let rest = kept.join('\n');
    const queryMatch = QUERY_LINE.exec(rest);
    const query = queryMatch?.[1]?.trim() || null;
    if (queryMatch) rest = rest.replace(QUERY_LINE, '');
    rest = rest.replace(NO_LINKS_LINE, '').replace(REMINDER_LINE, '').replace(/\n{3,}/g, '\n\n').trim();

    return { query, links, text: rest || null };
  } catch {
    return { query: null, links: [], text: source.trim() || null };
  }
}

/**
 * The index of the `]` that closes the `[` at `open`, skipping brackets inside JSON
 * strings; -1 when the list does not close.
 */
function closingBracket(text: string, open: number): number {
  let depth = 0;
  let inString = false;
  for (let i = open; i < text.length; i += 1) {
    const char = text[i];
    if (inString) {
      if (char === '\\') i += 1;
      else if (char === '"') inString = false;
      continue;
    }
    if (char === '"') inString = true;
    else if (char === '[' || char === '{') depth += 1;
    else if (char === ']' || char === '}') {
      depth -= 1;
      if (depth === 0) return char === ']' ? i : -1;
      if (depth < 0) return -1;
    }
  }
  return -1;
}

/** A string argument of a tool's input (`query`, `url`), trimmed; null when absent. */
export function inputString(input: unknown, key: string): string | null {
  if (!input || typeof input !== 'object' || Array.isArray(input)) return null;
  const value = (input as Record<string, unknown>)[key];
  return typeof value === 'string' && value.trim() ? value.trim() : null;
}

/** The page a `WebFetch` call reads: its `url` argument, when it is a web address. */
export function webFetchTarget(input: unknown): ToolLink | null {
  const url = webHref(inputString(input, 'url'));
  if (!url) return null;
  return { url, title: url.replace(/^https?:\/\//i, ''), domain: linkDomain(url) };
}
