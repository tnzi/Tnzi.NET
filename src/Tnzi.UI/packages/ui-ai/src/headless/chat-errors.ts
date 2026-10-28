/**
 * One shape for every failure `useChatThreads` reports, whatever it came from:
 * a refused API call (an `ApiResult` envelope), a refused or failed stream (an
 * HTTP answer before the first event, or an error event in the stream), an
 * attachment that could not be sent, or a rule the hook applies itself.
 *
 * The point is the code. A product maps the server's error codes to its own
 * copy ("you have reached your plan's limit", "this message cannot be sent
 * yet"); with only the server's English sentence it can show that sentence or
 * nothing.
 */
import { AttachmentRefusedError } from './attachment-parts';

/** What the hook was doing when it failed. */
export type ChatThreadsOperation =
  | 'load-threads'
  | 'load-more-threads'
  | 'load-thread'
  | 'load-older-messages'
  | 'delete-thread'
  | 'rename-thread'
  | 'archive-thread'
  | 'unarchive-thread'
  | 'stop-run'
  | 'attachment'
  | 'edit'
  /** A turn (send, regenerate, edit, or a run picked up again) whose stream failed. Written on the reply row. */
  | 'turn';

export interface ChatThreadsError {
  readonly operation: ChatThreadsOperation;
  /** The message as the server (or the hook) worded it. */
  readonly message: string;
  /** The server's error code (`errorCode` of the response envelope or of the stream's error event); null when it gave none. */
  readonly code: string | null;
  /**
   * The envelope's `errorDetails`, when the server gave them. For an attachment the hook refused
   * itself: `{ reason, fileName }` (`reason` is `too-large`, `unsupported-type` or `upload-failed`).
   */
  readonly details: Readonly<Record<string, unknown>> | null;
  /** HTTP status of a refused request; null for stream events and refusals the hook made itself. */
  readonly status: number | null;
}

interface EnvelopeLike {
  message?: unknown;
  errorCode?: unknown;
  errorDetails?: unknown;
}

const asString = (value: unknown): string | null => (typeof value === 'string' && value ? value : null);
const asRecord = (value: unknown): Record<string, unknown> | null =>
  value && typeof value === 'object' && !Array.isArray(value) ? (value as Record<string, unknown>) : null;
const asNumber = (value: unknown): number | null => (typeof value === 'number' && Number.isFinite(value) ? value : null);

/**
 * A refused stream request (`StreamRequestError` / `ChatStreamRequestError` from
 * `@tnzi/core/services/ai`): the HTTP status and the body the server answered with.
 * Recognised by shape rather than `instanceof`, so a copy of the class from another
 * bundle entry (or a test double) is recognised too.
 */
function asRefusedRequest(source: unknown): { status: number; detail: string; message: string } | null {
  if (!(source instanceof Error)) return null;
  const candidate = source as Error & { status?: unknown; detail?: unknown };
  return typeof candidate.status === 'number' && typeof candidate.detail === 'string'
    ? { status: candidate.status, detail: candidate.detail, message: candidate.message }
    : null;
}

/** The error envelope a refused stream request answered with, when its body is one. */
function readEnvelope(body: string): EnvelopeLike | null {
  try {
    return asRecord(JSON.parse(body)) as EnvelopeLike | null;
  } catch {
    return null;
  }
}

/** Code, details and status off an error object (`HttpError` / `ApiError` carry them). */
function fromErrorObject(err: Record<string, unknown>): Pick<ChatThreadsError, 'code' | 'details' | 'status'> {
  return {
    code: asString(err.errorCode),
    details: asRecord(err.details),
    status: asNumber(err.statusCode) ?? asNumber(err.code) ?? asNumber(err.status),
  };
}

/**
 * Normalise a failure into a {@link ChatThreadsError}. `source` is whatever
 * failed (an `ApiResult`, an `Error`, a stream event); `fallback` is the
 * message to use when the source carries none.
 */
export function toChatThreadsError(
  operation: ChatThreadsOperation,
  source: unknown,
  fallback: string,
): ChatThreadsError {
  const base = { operation, message: fallback, code: null, details: null, status: null };
  if (source == null) return base;

  const refused = asRefusedRequest(source);
  if (refused) {
    const envelope = readEnvelope(refused.detail);
    return {
      operation,
      message: asString(envelope?.message) ?? (refused.message || fallback),
      code: asString(envelope?.errorCode),
      details: asRecord(envelope?.errorDetails),
      status: refused.status,
    };
  }

  if (source instanceof AttachmentRefusedError) {
    const cause = asRecord((source as Error & { cause?: unknown }).cause);
    const fromCause = cause ? fromErrorObject(cause) : { code: null, details: null, status: null };
    return {
      operation,
      message: source.message || fallback,
      code: fromCause.code,
      details: { ...(fromCause.details ?? {}), reason: source.reason, fileName: source.fileName },
      status: fromCause.status,
    };
  }

  if (source instanceof Error) {
    return { operation, message: source.message || fallback, ...fromErrorObject(source as unknown as Record<string, unknown>) };
  }

  const record = asRecord(source);
  if (!record) return base;

  // An `ApiResult` envelope: `code` is the HTTP status, `errorCode` the server's code.
  if (typeof record.succeeded === 'boolean') {
    return {
      operation,
      message: asString(record.message) ?? fallback,
      code: asString(record.errorCode),
      details: asRecord(record.errorDetails),
      status: asNumber(record.code),
    };
  }

  // A stream error event (`isError` with `errorMessage` / `errorCode`).
  return {
    operation,
    message: asString(record.errorMessage) ?? asString(record.message) ?? fallback,
    code: asString(record.errorCode),
    details: asRecord(record.errorDetails),
    status: null,
  };
}
