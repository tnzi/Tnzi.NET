/**
 * What a tool call's input and result look like to a person reading the answer.
 *
 * A tool's arguments and its result are machine payloads: an object of named
 * arguments, a JSON document, a block of text. Shown as they are they read like a
 * debugger. These helpers turn the common shapes into something readable, and the
 * tool step components (`TToolFields`, `TToolLinkList`, `TToolOutput`) draw them:
 *
 *   - named arguments become labelled lines (`file_path` reads "File path");
 *   - a result that is a list of web pages (`[{ title, url }]`, or an object holding
 *     one) becomes a list of links;
 *   - any other JSON result becomes labelled lines as well;
 *   - text stays text.
 *
 * Nothing here throws on unexpected input: a value it cannot read is shown as
 * short, plain text rather than failing the answer it sits in.
 */
import type { ToolCallInfo } from '../../headless/useChat';

/** A web page a tool read or found. */
export interface ToolLink {
  /** The address, an `http(s)` URL. */
  readonly url: string;
  /** The page's title, or its address when the tool gave none. */
  readonly title: string;
  /** The site, without `www.` (`tauri.app`). */
  readonly domain: string;
}

/** One labelled line of a tool's input or result. */
export interface ToolField {
  /** The argument's name as words ("File path"); empty for an input that is one value. */
  readonly label: string;
  /** The value as text. */
  readonly text: string;
  /** Multi-line or long text, shown as a block of its own (folded when long). */
  readonly block: boolean;
  /** The value is an `http(s)` address: shown as a link that opens in a new tab. */
  readonly href: string | null;
}

/** A tool's result, read: the pages it lists, its named values, and any plain text. */
export interface ToolResultView {
  readonly links: readonly ToolLink[];
  readonly fields: readonly ToolField[];
  readonly text: string | null;
}

/** Text longer than this is a block of its own rather than the end of a line. */
const BLOCK_LENGTH = 160;
/** A nested value deeper than the field and one level inside it is shown as short JSON. */
const MAX_DEPTH = 2;
const COMPACT_JSON_LENGTH = 200;

/** `file_path` / `filePath` / `file-path` → "File path". */
export function readableFieldName(key: string): string {
  const words = key
    .replace(/([a-z\d])([A-Z])/g, '$1 $2')
    .replace(/[_\-.]+/g, ' ')
    .trim()
    .toLowerCase();
  return words ? words.charAt(0).toUpperCase() + words.slice(1) : key;
}

/** The address when it is an absolute `http(s)` URL a browser can open, else null. */
export function webHref(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const text = value.trim();
  if (!/^https?:\/\//i.test(text)) return null;
  try {
    const url = new URL(text);
    return url.protocol === 'http:' || url.protocol === 'https:' ? url.href : null;
  } catch {
    return null;
  }
}

/** `https://www.tauri.app/start` → `tauri.app`; the text itself when it is not a URL. */
export function linkDomain(url: string): string {
  try {
    return new URL(url).hostname.replace(/^www\./i, '');
  } catch {
    return url;
  }
}

/**
 * A list of web pages: an array whose entries each carry an `http(s)` `url` (with a
 * `title`, `name` or nothing). Null when the value is not such a list, so an array
 * that only partly holds pages is shown as fields instead of losing the rest.
 */
export function toolLinks(value: unknown): ToolLink[] | null {
  if (!Array.isArray(value) || value.length === 0) return null;
  const links: ToolLink[] = [];
  const seen = new Set<string>();
  for (const entry of value) {
    if (!entry || typeof entry !== 'object' || Array.isArray(entry)) return null;
    const record = entry as Record<string, unknown>;
    const url = webHref(record.url);
    if (!url) return null;
    if (seen.has(url)) continue;
    seen.add(url);
    const title = [record.title, record.name].find((t): t is string => typeof t === 'string' && t.trim() !== '');
    links.push({ url, title: title?.trim() ?? url, domain: linkDomain(url) });
  }
  return links;
}

/**
 * A tool's input (or a JSON result) as labelled lines: an object gives one line per
 * argument, in its order; any other value is one line without a label. Empty values
 * are left out.
 */
export function toolFields(value: unknown): ToolField[] {
  if (value === null || value === undefined) return [];
  if (typeof value === 'object' && !Array.isArray(value)) {
    const fields: ToolField[] = [];
    for (const [key, entry] of Object.entries(value as Record<string, unknown>)) {
      const field = toField(readableFieldName(key), entry);
      if (field) fields.push(field);
    }
    return fields;
  }
  const field = toField('', value);
  return field ? [field] : [];
}

function toField(label: string, value: unknown): ToolField | null {
  const text = valueText(value, 1);
  if (text === null) return null;
  return {
    label,
    text,
    block: text.includes('\n') || text.length > BLOCK_LENGTH,
    href: webHref(value),
  };
}

/**
 * A value as text: strings as they are, numbers and flags as written, a list of plain
 * values joined with commas, a list of records one per line, a record as
 * "Name: value" pairs. Deeper than that it is short JSON.
 */
function valueText(value: unknown, depth: number): string | null {
  if (value === null || value === undefined) return null;
  if (typeof value === 'string') return value.trim() ? value.replace(/\s+$/, '') : null;
  if (typeof value === 'number' || typeof value === 'boolean' || typeof value === 'bigint') return String(value);
  if (typeof value !== 'object') return null;
  if (depth > MAX_DEPTH) return compactJson(value);
  if (Array.isArray(value)) {
    const parts = value.map((entry) => valueText(entry, depth + 1)).filter((part): part is string => part !== null);
    if (parts.length === 0) return null;
    const records = value.some((entry) => entry !== null && typeof entry === 'object');
    return parts.join(records ? '\n' : ', ');
  }
  const parts: string[] = [];
  for (const [key, entry] of Object.entries(value as Record<string, unknown>)) {
    const text = valueText(entry, depth + 1);
    if (text !== null) parts.push(`${readableFieldName(key)}: ${text}`);
  }
  return parts.length ? parts.join(' · ') : null;
}

function compactJson(value: unknown): string {
  let text: string;
  try {
    text = JSON.stringify(value) ?? String(value);
  } catch {
    text = String(value);
  }
  return text.length > COMPACT_JSON_LENGTH ? `${text.slice(0, COMPACT_JSON_LENGTH - 1)}…` : text;
}

/** JSON.parse that answers `undefined` instead of throwing. */
export function parseJsonSafely(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return undefined;
  }
}

/**
 * The text a call returned: the text blocks of its result (external CLI agents), or its
 * `output` when it reports no blocks (the built-in engine). A result of pictures or
 * files only has no text.
 */
export function toolResultText(toolCall: Pick<ToolCallInfo, 'result' | 'output'>): string {
  const blocks = toolCall.result ?? [];
  if (blocks.length) {
    return blocks
      .filter((block) => block.type === 'text' && block.text)
      .map((block) => block.text)
      .join('\n\n');
  }
  return typeof toolCall.output === 'string' ? toolCall.output : '';
}

/**
 * A result's text, read: a JSON list of pages gives links; a JSON object gives links
 * for each list of pages it holds (a search tool's `results`) and fields for the rest;
 * other JSON gives fields; anything else is text.
 */
export function toolResultView(text: string | null | undefined): ToolResultView {
  const value = (text ?? '').replace(/\s+$/, '');
  const trimmed = value.trim();
  if (!trimmed) return { links: [], fields: [], text: null };

  if (trimmed.startsWith('{') || trimmed.startsWith('[')) {
    const parsed = parseJsonSafely(trimmed);
    if (parsed !== undefined) {
      const listed = toolLinks(parsed);
      if (listed) return { links: listed, fields: [], text: null };
      if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
        const links: ToolLink[] = [];
        const rest: Record<string, unknown> = {};
        for (const [key, entry] of Object.entries(parsed as Record<string, unknown>)) {
          const pages = toolLinks(entry);
          if (pages) links.push(...pages.filter((page) => !links.some((known) => known.url === page.url)));
          else rest[key] = entry;
        }
        return { links, fields: toolFields(rest), text: null };
      }
      const fields = toolFields(parsed);
      if (fields.length) return { links: [], fields, text: null };
    }
  }
  return { links: [], fields: [], text: value };
}
