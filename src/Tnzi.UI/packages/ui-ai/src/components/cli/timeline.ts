/**
 * Grouping logic for an external CLI agent run's event stream.
 *
 * Kept out of the component because this is the part that can actually be
 * wrong: the wire format is a flat, append-only sequence, and turning it into
 * something readable means pairing, coalescing and ordering decisions that are
 * much easier to get right against a list of events than against a rendered DOM.
 */

import type { CliAgentEvent, CliRunMessageDto } from '@tnzi/core/services/ai';

/**
 * Either shape of a run event.
 *
 * Both are accepted because the real usage is "replay the persisted history,
 * then attach the live stream": a consumer holds `CliRunMessageDto[]` from the
 * REST call and `CliAgentEvent[]` from SSE, and should not have to normalise
 * them itself just to concatenate two halves of one conversation.
 */
export type CliTimelineEvent = CliAgentEvent | CliRunMessageDto;

/** A contiguous run of text or reasoning deltas, already concatenated. */
export interface CliTimelineTextRow {
  kind: 'text' | 'thinking';
  key: string;
  content: string;
}

/** One tool invocation, with its result folded in once it arrives. */
export interface CliTimelineToolRow {
  kind: 'tool';
  key: string;
  tool: string;
  callId: string | null;
  /** Parsed arguments, or null when absent/unparseable. */
  input: Record<string, unknown> | null;
  /** Null while the call is still in flight. */
  output: string | null;
  settled: boolean;
}

/** A status transition, error, or log line. */
export interface CliTimelineNoticeRow {
  kind: 'status' | 'error' | 'log';
  key: string;
  content: string;
  /** Log level, when the source event carried one. */
  level: string | null;
}

export type CliTimelineRow = CliTimelineTextRow | CliTimelineToolRow | CliTimelineNoticeRow;

/** Options for {@link groupCliEvents}. */
export interface GroupCliEventsOptions {
  /**
   * Include `Log` events. Default false.
   *
   * Logs are diagnostics for whoever operates the runtime, not for whoever is
   * reading what the agent did. Showing them by default buries the three lines
   * that matter under a hundred that do not.
   */
  includeLogs?: boolean;
}

/** Read the arguments off either event shape. */
function readInput(event: CliTimelineEvent): Record<string, unknown> | null {
  if ('input' in event && event.input) return event.input;
  if ('inputJson' in event && event.inputJson) {
    try {
      const parsed: unknown = JSON.parse(event.inputJson);
      // A JSON scalar or array is valid JSON but not an argument object; treating
      // it as one would put `0: 'a'` style rows in the UI.
      if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
        return parsed as Record<string, unknown>;
      }
    } catch {
      // Malformed arguments are shown as "no arguments" rather than crashing the
      // timeline: the tool name and its result are still worth reading, and the
      // raw text is one API call away in the admin run detail.
    }
  }
  return null;
}

/**
 * Fold a flat event sequence into rows suitable for rendering.
 *
 * Three things happen here, each because the raw sequence is unreadable
 * without it:
 *
 * - **Text and reasoning deltas are concatenated.** They arrive token by token;
 *   one row per delta would be thousands of rows for a single reply.
 * - **`ToolUse` and `ToolResult` are paired by `callId`.** The wire reports them
 *   as two independent events, so rendering them verbatim makes every tool
 *   appear twice and leaves the reader to correlate them by eye.
 * - **A `ToolResult` with no matching call still gets a row.** Dropping it would
 *   silently hide output whenever a run is replayed from a sequence number that
 *   lands between the two halves of one call.
 */
export function groupCliEvents(
  events: readonly CliTimelineEvent[],
  options: GroupCliEventsOptions = {},
): CliTimelineRow[] {
  const { includeLogs = false } = options;
  const rows: CliTimelineRow[] = [];
  const toolsByCallId = new Map<string, CliTimelineToolRow>();

  events.forEach((event, index) => {
    switch (event.type) {
      case 'Text':
      case 'Thinking': {
        const kind = event.type === 'Text' ? 'text' : 'thinking';
        const last = rows[rows.length - 1];
        if (last && last.kind === kind) {
          last.content += event.content ?? '';
        } else {
          rows.push({ kind, key: `${kind}-${index}`, content: event.content ?? '' });
        }
        break;
      }

      case 'ToolUse': {
        const row: CliTimelineToolRow = {
          kind: 'tool',
          key: `tool-${index}`,
          tool: event.tool ?? 'tool',
          callId: event.callId ?? null,
          input: readInput(event),
          output: null,
          settled: false,
        };
        rows.push(row);
        if (event.callId) toolsByCallId.set(event.callId, row);
        break;
      }

      case 'ToolResult': {
        const pending = event.callId ? toolsByCallId.get(event.callId) : undefined;
        if (pending) {
          pending.output = event.output ?? event.content ?? '';
          pending.settled = true;
          // Drop the pairing entry: a provider that reuses a call id across two
          // invocations would otherwise write the second result onto the first row.
          toolsByCallId.delete(event.callId as string);
        } else {
          rows.push({
            kind: 'tool',
            key: `tool-${index}`,
            tool: event.tool ?? 'tool',
            callId: event.callId ?? null,
            input: null,
            output: event.output ?? event.content ?? '',
            settled: true,
          });
        }
        break;
      }

      case 'Status':
        rows.push({
          kind: 'status',
          key: `status-${index}`,
          content: event.status ?? event.content ?? '',
          level: null,
        });
        break;

      case 'Error':
        rows.push({
          kind: 'error',
          key: `error-${index}`,
          content: event.content ?? '',
          level: null,
        });
        break;

      case 'Log':
        if (includeLogs) {
          rows.push({
            kind: 'log',
            key: `log-${index}`,
            content: event.content ?? '',
            level: event.level ?? null,
          });
        }
        break;

      default:
        // Unknown event types are dropped rather than rendered raw. The backend
        // already downgrades anything it does not recognise to `Log`, so a value
        // arriving here means the two sides are on different versions - and a
        // half-understood row is worse than a missing one.
        break;
    }
  });

  return rows;
}

/** Count of tool calls still awaiting a result. */
export function pendingToolCount(rows: readonly CliTimelineRow[]): number {
  return rows.filter((r): r is CliTimelineToolRow => r.kind === 'tool' && !r.settled).length;
}
