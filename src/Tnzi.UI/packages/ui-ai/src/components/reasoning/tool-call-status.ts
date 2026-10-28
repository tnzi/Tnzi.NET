/**
 * How a tool call is shown: its state, a readable name, its duration. Shared by the
 * compact status line (`TToolCallDisplay`) and the generic card (`TToolCallCard`), so the
 * two never disagree about whether a call is still running.
 */
import type { ToolCallInfo } from '../../headless/useChat';

export type ToolCallState = 'pending' | 'running' | 'completed' | 'failed';

/**
 * A reported status comes first: a call that is still running already carries
 * `isSuccess: true` (the backend's default), so reading that first would show it as done
 * the moment it starts. Without a status, the outcome fields decide; a call with no result
 * yet is running while its message streams and was never answered once it has ended. A
 * duration on its own means the call ran.
 */
export function toolCallState(toolCall: ToolCallInfo, streaming: boolean): ToolCallState {
  if (toolCall.status === 'running') return streaming ? 'running' : 'pending';
  if (toolCall.status === 'failed') return 'failed';
  if (toolCall.status === 'completed') return 'completed';
  if (toolCall.isSuccess === false || toolCall.error) return 'failed';
  if (toolCall.isSuccess === true) return 'completed';
  if (toolCall.output === null || toolCall.output === undefined) {
    if (toolCall.durationMs != null) return 'completed';
    return streaming ? 'running' : 'pending';
  }
  if (typeof toolCall.output === 'string' && toolCall.output.startsWith('Error')) return 'failed';
  return 'completed';
}

/**
 * `take_screenshot` / `takeScreenshot` → "Take Screenshot". A CLI agent's MCP tool
 * (`mcp__server__tool`) shows the tool part only.
 */
export function readableToolName(name: string | null | undefined): string {
  const raw = name ?? '';
  const separator = raw.lastIndexOf('__');
  const bare = separator >= 0 ? raw.slice(separator + 2) : raw;
  return bare
    .replace(/_/g, ' ')
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/\b\w/g, (c) => c.toUpperCase());
}

/** "1.2s"; null for no duration. */
export function formatToolDuration(durationMs: number | null | undefined): string | null {
  if (!durationMs) return null;
  return `${(durationMs / 1000).toFixed(1)}s`;
}

/**
 * A one-line hint of what the call was asked to do, from its input: the first short
 * string argument (a path, a command, a query). Null when there is none.
 */
export function toolInputSummary(input: unknown, maxLength = 80): string | null {
  if (typeof input === 'string') return shorten(input, maxLength);
  if (!input || typeof input !== 'object' || Array.isArray(input)) return null;
  for (const value of Object.values(input as Record<string, unknown>)) {
    if (typeof value === 'string' && value.trim()) return shorten(value, maxLength);
  }
  return null;
}

/** The input as readable JSON (strings as they are). */
export function formatToolInput(input: unknown): string | null {
  if (input === undefined || input === null) return null;
  if (typeof input === 'string') return input;
  try {
    return JSON.stringify(input, null, 2);
  } catch {
    return String(input);
  }
}

function shorten(text: string, maxLength: number): string {
  const single = text.replace(/\s+/g, ' ').trim();
  return single.length > maxLength ? `${single.slice(0, maxLength - 1)}…` : single;
}
