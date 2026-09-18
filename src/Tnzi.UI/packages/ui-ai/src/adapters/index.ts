/**
 * `@tnzi/ui-ai/adapters` - backend DTO to view model.
 *
 * ## Why this layer exists
 *
 * `@tnzi/core` speaks the backend's shapes (`ThreadMessageDto`,
 * `AgentThreadDto`); this package's components take view models
 * (`ChatMessage`, `ThreadItem`). Something has to map between them, and until
 * now that something was **every consumer, separately** - the same
 * `toChatMessage` / `toThreadItem` / role-normalisation written out again in
 * each app, drifting apart as either side changed.
 *
 * This is the same job `@tnzi/ui-admin`'s `services/bridges/*` do for the admin
 * pages. The mapping belongs to whoever owns both contracts - the framework -
 * not to the app that happens to consume them.
 *
 * ## What it is not
 *
 * Not a transport layer. Nothing here fetches: the functions take a DTO and
 * return a view model. Fetching stays with the consumer (or with the opt-in
 * `useChatThreads`), per this package's transport rule.
 */
import type { AgentThreadDto, ThreadMessageDto } from '@tnzi/core/services/ai';
import type { ChatMessage, ToolCallInfo, TokenUsage } from '../headless/useChat';
import type { ThreadItem } from '../components/chat/TThreadList.vue';

/** Roles the message components know how to render. */
const KNOWN_ROLES: ReadonlyArray<ChatMessage['role']> = ['user', 'assistant', 'system', 'tool'];

/**
 * Normalise the backend's free-form role string into the rendered union.
 *
 * `AgentThreadMessage.Role` is a plain string server-side, so a value this
 * client does not know about is possible. Falling back to `'assistant'` renders
 * it as an ordinary reply; casting blindly would put an invalid member into the
 * union and break rendering somewhere far from here.
 */
export function toMessageRole(role: string | null | undefined): ChatMessage['role'] {
  const normalized = (role ?? '').toLowerCase() as ChatMessage['role'];
  return KNOWN_ROLES.includes(normalized) ? normalized : 'assistant';
}

/**
 * Lower-case the first letter of every key, recursively.
 *
 * ★ The persisted `toolCalls` / `usage` columns were written by the backend's
 * history middleware with System.Text.Json's DEFAULT options - PascalCase
 * (`{"InputTokens":…}`, `[{"Name":…,"DurationMs":…}]`) - while the SSE stream
 * and every frontend type are camelCase. Rows in that shape exist in every
 * database that has ever run the AI module, so the adapter reads both
 * spellings instead of asking for a data migration. camelCase input passes
 * through unchanged.
 */
function camelizeKeys(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(camelizeKeys);
  if (!value || typeof value !== 'object') return value;
  return Object.fromEntries(
    Object.entries(value as Record<string, unknown>).map(([key, v]) => [
      key.charAt(0).toLowerCase() + key.slice(1),
      camelizeKeys(v),
    ]),
  );
}

/**
 * `ThreadMessageDto.toolCalls` / `.usage` are JSON **strings** on the wire while
 * `ChatMessage` wants objects. Parse defensively: a message that cannot be
 * fully understood should still render its text, not vanish or throw. Returns
 * `null` for anything unusable, which is also what "absent" looks like.
 */
function parseJsonField(raw: string | null | undefined): unknown {
  if (!raw) return null;
  try {
    const parsed: unknown = JSON.parse(raw);
    return parsed && typeof parsed === 'object' ? camelizeKeys(parsed) : null;
  } catch {
    return null;
  }
}

/**
 * Only entries with a name survive: `TToolCallDisplay` derives its label from
 * `name`, and an entry without one would render a card with nothing on it (or
 * throw on `.replace`). Anything that is not an object is dropped the same way.
 */
function toToolCalls(raw: string | null | undefined): ToolCallInfo[] | null {
  const parsed = parseJsonField(raw);
  if (!Array.isArray(parsed)) return null;
  const calls = parsed.filter(
    (c): c is ToolCallInfo =>
      !!c && typeof c === 'object' && typeof (c as { name?: unknown }).name === 'string',
  );
  return calls;
}

function toUsage(raw: string | null | undefined): TokenUsage | null {
  const parsed = parseJsonField(raw);
  return parsed && !Array.isArray(parsed) ? (parsed as TokenUsage) : null;
}

/**
 * A stored thread message to a renderable one.
 *
 * Accepts the fields it needs rather than the whole DTO, so a caller holding a
 * projection (a list endpoint that omits `usage`, say) can still use it.
 *
 * ★ `toolCalls` and `usage` are carried through. They used to be dropped, which
 * meant a conversation looked complete while it was streaming and then lost its
 * tool-call blocks and token counts the moment the thread was reopened - the
 * kind of gap nobody reports as a bug because it reads as "the history is just
 * shorter". And carrying them through was not enough on its own: the stored
 * JSON is PascalCase (see `camelizeKeys`), so the first version of this
 * mapping parsed keys nothing downstream read.
 */
export function toChatMessage(
  message: Pick<ThreadMessageDto, 'id' | 'role' | 'content' | 'creationTime'> &
    Partial<Pick<ThreadMessageDto, 'feedbackRating' | 'toolCalls' | 'usage'>>,
): ChatMessage {
  return {
    id: message.id,
    role: toMessageRole(message.role),
    content: message.content,
    createdAt: message.creationTime,
    feedbackRating: message.feedbackRating ?? null,
    toolCalls: toToolCalls(message.toolCalls),
    usage: toUsage(message.usage),
  };
}

/**
 * A thread to a sidebar entry.
 *
 * An untitled thread gets a placeholder rather than an empty row: a blank line
 * in the history list is unclickable-looking and tells the user nothing. The
 * default is overridable because "New chat" is the framework's guess at what a
 * product calls a fresh conversation.
 */
export function toThreadItem(
  thread: Pick<AgentThreadDto, 'id' | 'lastActivityTime'> & Partial<Pick<AgentThreadDto, 'title'>>,
  untitledLabel = 'New chat',
): ThreadItem {
  return {
    id: thread.id,
    title: thread.title || untitledLabel,
    updatedAt: thread.lastActivityTime,
  };
}

/** Map a page of threads in one call. */
export function toThreadItems(
  threads: ReadonlyArray<Parameters<typeof toThreadItem>[0]>,
  untitledLabel?: string,
): ThreadItem[] {
  return threads.map((t) => toThreadItem(t, untitledLabel));
}

/** Map a list of stored messages in one call. */
export function toChatMessages(
  messages: ReadonlyArray<Parameters<typeof toChatMessage>[0]>,
): ChatMessage[] {
  return messages.map(toChatMessage);
}
