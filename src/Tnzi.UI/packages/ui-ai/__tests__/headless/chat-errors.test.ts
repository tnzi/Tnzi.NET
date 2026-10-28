import { describe, expect, it } from 'vitest';
import { toChatThreadsError } from '../../src/headless/chat-errors';
import { AttachmentRefusedError } from '../../src/headless/attachment-parts';

/** Same shape as `StreamRequestError` from `@tnzi/core/services/ai`. */
class RefusedRequest extends Error {
  constructor(readonly status: number, readonly detail: string) {
    super(`Stream request failed: ${status} ${detail}`);
  }
}

describe('toChatThreadsError', () => {
  it('reads a refused ApiResult: message, server code, details and HTTP status', () => {
    expect(
      toChatThreadsError('delete-thread', {
        succeeded: false, code: 409, message: 'Busy', errorCode: 'AI_CHAT_RUN_ACTIVE', errorDetails: { runId: 'r' },
      }, 'fallback'),
    ).toEqual({ operation: 'delete-thread', message: 'Busy', code: 'AI_CHAT_RUN_ACTIVE', details: { runId: 'r' }, status: 409 });
  });

  it('falls back to the given message when the envelope has none', () => {
    expect(toChatThreadsError('load-threads', { succeeded: false, code: 500 }, 'Could not load').message).toBe('Could not load');
  });

  it('reads the error envelope a refused stream request answered with', () => {
    const body = JSON.stringify({ message: 'Too large', errorCode: 'AI_INVALID_TURN_METADATA', errorDetails: { maxKeys: 16 } });
    expect(toChatThreadsError('turn', new RefusedRequest(400, body), 'Stream failed')).toEqual({
      operation: 'turn', message: 'Too large', code: 'AI_INVALID_TURN_METADATA', details: { maxKeys: 16 }, status: 400,
    });
  });

  it('keeps the status of a refused stream request whose body is not an envelope', () => {
    const error = toChatThreadsError('turn', new RefusedRequest(502, '<html>Bad gateway</html>'), 'Stream failed');
    expect(error).toMatchObject({ code: null, details: null, status: 502 });
    expect(error.message).toContain('502');
  });

  it('reads a stream error event', () => {
    expect(toChatThreadsError('turn', { isError: true, errorCode: 'AI_CLI_USAGE_LIMIT', errorMessage: 'Limit' }, 'Stream failed'))
      .toEqual({ operation: 'turn', message: 'Limit', code: 'AI_CLI_USAGE_LIMIT', details: null, status: null });
  });

  it('says which attachment was refused and why, and keeps the server code of a failed upload', () => {
    const cause = Object.assign(new Error('Storage is full'), { errorCode: 'STORAGE_QUOTA_EXCEEDED', statusCode: 413, details: { used: 10 } });
    const refused = new AttachmentRefusedError('Storage is full', { reason: 'upload-failed', fileName: 'a.zip', cause });
    expect(toChatThreadsError('attachment', refused, 'fallback')).toEqual({
      operation: 'attachment',
      message: 'Storage is full',
      code: 'STORAGE_QUOTA_EXCEEDED',
      details: { used: 10, reason: 'upload-failed', fileName: 'a.zip' },
      status: 413,
    });
  });

  it('reads code and details off an HttpError-shaped error', () => {
    const err = Object.assign(new Error('Denied'), { errorCode: 'FORBIDDEN', code: 403, details: { scope: 'x' } });
    expect(toChatThreadsError('load-thread', err, 'fallback')).toEqual({
      operation: 'load-thread', message: 'Denied', code: 'FORBIDDEN', details: { scope: 'x' }, status: 403,
    });
  });

  it('a failure with nothing to read is the fallback message alone', () => {
    expect(toChatThreadsError('edit', undefined, 'This message cannot be edited.')).toEqual({
      operation: 'edit', message: 'This message cannot be edited.', code: null, details: null, status: null,
    });
    expect(toChatThreadsError('edit', 'nonsense', 'Fallback').message).toBe('Fallback');
  });
});
