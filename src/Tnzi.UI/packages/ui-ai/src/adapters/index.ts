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
import type { ChatMessage } from '../headless/useChat';
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
 * `ThreadMessageDto.toolCalls` / `.usage` are JSON **strings** on the wire while
 * `ChatMessage` wants objects. Parse defensively: a message that cannot be
 * fully understood should still render its text, not vanish or throw. Returns
 * `null` for anything unusable, which is also what "absent" looks like.
 */
function parseJsonField<T>(raw: string | null | undefined): T | null {
  if (!raw) return null;
  try {
    const parsed: unknown = JSON.parse(raw);
    return parsed && typeof parsed === 'object' ? (parsed as T) : null;
  } catch {
    return null;
  }
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
 * shorter".
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
    toolCalls: parseJsonField<ChatMessage['toolCalls']>(message.toolCalls) ?? null,
    usage: parseJsonField<ChatMessage['usage']>(message.usage) ?? null,
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
