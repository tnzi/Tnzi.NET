/**
 * The `show_widget` tool as the client sees it: its name and limits (the same as the server's
 * `WidgetToolDefinition`), reading a call's arguments, and its words in a run's summary.
 *
 * A call is drawn from its stored arguments, so a widget survives a reload and history paging:
 * the engine records them on the tool call, and so does the external CLI path.
 */
import { formatAiMessage } from '../../i18n/index';
import type { ToolCallInfo } from '../../headless/useChat';
import type { ToolStepDescriber } from '../reasoning/tool-steps';

/** The tool's name. A CLI agent reaches it as `mcp__<server>__show_widget`, which matches too. */
export const WIDGET_TOOL = 'show_widget';

/** The longest title accepted (the server's `WidgetToolDefinition.MaxTitleLength`). */
export const WIDGET_MAX_TITLE_LENGTH = 120;

/** The longest HTML document accepted (the server's `WidgetToolDefinition.MaxHtmlLength`). */
export const WIDGET_MAX_HTML_LENGTH = 200_000;

/** A widget call's arguments. */
export interface WidgetInput {
  readonly title: string;
  readonly html: string;
}

/**
 * The arguments of a widget call, or null when they are not a widget: missing, of the wrong type,
 * or over the limits (the server rejects such a call, but a stored message is shown as it is).
 * Accepts the arguments as an object or as the JSON text of one.
 */
export function parseWidgetInput(input: unknown): WidgetInput | null {
  let value = input;
  if (typeof value === 'string') {
    try {
      value = JSON.parse(value);
    } catch {
      return null;
    }
  }
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
  const { title, html } = value as Record<string, unknown>;
  if (typeof title !== 'string' || typeof html !== 'string') return null;
  const trimmedTitle = title.trim();
  if (!trimmedTitle || trimmedTitle.length > WIDGET_MAX_TITLE_LENGTH) return null;
  if (!html.trim() || html.length > WIDGET_MAX_HTML_LENGTH) return null;
  return { title: trimmedTitle, html };
}

/** The call failed, by any of the ways a backend reports it. */
function hasFailed(toolCall: ToolCallInfo): boolean {
  return toolCall.status === 'failed' || toolCall.isSuccess === false || Boolean(toolCall.error);
}

/**
 * Whether a widget call stays in view under its run's line instead of folding into the run:
 * while it is being made (its placeholder) and once it holds a widget. A call that failed, or
 * finished without a usable document, folds like any other step.
 */
export function isWidgetCallShown(toolCall: ToolCallInfo): boolean {
  if (hasFailed(toolCall)) return false;
  if (toolCall.status === 'running') return true;
  return parseWidgetInput(toolCall.input) !== null;
}

/** "Created a widget", with its title as the subject. */
export const describeWidget: ToolStepDescriber = (toolCall, t) => ({
  kind: WIDGET_TOOL,
  running: t.tools.widgetRunning,
  done: t.tools.widgetDone,
  failed: t.tools.widgetFailed,
  many: (count) => formatAiMessage(t.tools.widgetsDone, { count }),
  subject: parseWidgetInput(toolCall.input)?.title ?? null,
});

/** A file name for the widget's HTML: its title, made safe for a file system. */
export function widgetFileName(title: string): string {
  const base = Array.from(title.normalize('NFKC'), (ch) => (ch.charCodeAt(0) < 0x20 ? ' ' : ch))
    .join('')
    .replace(/[\\/:*?"<>|]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .slice(0, 80)
    .replace(/[. ]+$/, '');
  return `${base || 'widget'}.html`;
}
