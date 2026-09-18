import { describe, it, expect, afterEach, vi } from 'vitest';
import type { HttpClient } from '../../src/http/http';
import {
  isStepUpRequired,
  stepUpScopeOf,
  sendStepUpCode,
  stepUpWithCode,
  stepUpWithPasskey,
  withStepUp,
  STEP_UP_REQUIRED,
} from '../../src/services/identity/step-up';
import { TwoFactorType } from '../../src/services/identity/metadata';
import { normalizeApiResult, ensureOk } from '../../src/http/response';
import { HttpError } from '../../src/errors/api-error';

// ---------------------------------------------------------------------------
// Step-up sits on top of a valid session, so the failure modes are subtle:
// mistaking it for an expired session sends the user to the login page, and
// retrying forever re-prompts without ever saying what is wrong.
// ---------------------------------------------------------------------------

type Envelope<T> = { succeeded: boolean; data?: T | null; message?: string };

function ok<T>(data: T): Promise<Envelope<T>> {
  return Promise.resolve({ succeeded: true, data });
}

function createClient(overrides: Partial<Record<'get' | 'post', unknown>> = {}) {
  return {
    get: vi.fn(() => ok(null)),
    post: vi.fn(() => ok(null)),
    put: vi.fn(() => ok(null)),
    patch: vi.fn(() => ok(null)),
    delete: vi.fn(() => ok(null)),
    ...overrides,
  } as unknown as HttpClient;
}

/**
 * The challenge exactly as `[RequireStepUp]` puts it on the wire and
 * `HttpClient` normalises it: details travel under `errorDetails`, never
 * `errors`. Built from JSON on purpose - a hand-written object here is how the
 * previous fixture came to assert a field the backend never emits.
 */
function challenge(scope = 'tip.download') {
  return normalizeApiResult(
    JSON.parse(
      `{"succeeded":false,"success":false,"code":401,"message":"This action requires re-authentication","errorCode":"${STEP_UP_REQUIRED}","errorDetails":{"scope":"${scope}"}}`,
    ),
  );
}

/** The same challenge after a bridge has run it through `ensureOk` and thrown. */
function thrownChallenge(scope = 'tip.download'): unknown {
  try {
    ensureOk(challenge(scope));
  } catch (error) {
    return error;
  }
  throw new Error('ensureOk did not throw');
}

function grant(scope = 'tip.download') {
  return { scope, expiresAt: '2026-08-19T10:00:00Z', singleUse: false };
}

function givenBrowserSupportsPasskeys(result: unknown) {
  const parseRequestOptionsFromJSON = vi.fn((json: unknown) => json);
  const publicKeyCredential = {
    parseCreationOptionsFromJSON: vi.fn((json: unknown) => json),
    parseRequestOptionsFromJSON,
  };
  const get = vi.fn(() => Promise.resolve(result));
  const navigatorStub = { credentials: { create: vi.fn(), get } };

  vi.stubGlobal('PublicKeyCredential', publicKeyCredential);
  vi.stubGlobal('navigator', navigatorStub);
  vi.stubGlobal('window', { PublicKeyCredential: publicKeyCredential, navigator: navigatorStub });

  return { get };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('isStepUpRequired', () => {
  it('matches on the error code, not the status', () => {
    // Step-up and an expired session are both 401. Telling them apart by status
    // sends the user to the login page when all they needed was to touch a key.
    expect(isStepUpRequired(challenge())).toBe(true);
    expect(isStepUpRequired({ errorCode: 'UNAUTHORIZED', code: 401 })).toBe(false);
    expect(isStepUpRequired(null)).toBe(false);
    expect(isStepUpRequired(new Error('boom'))).toBe(false);
  });
});

describe('stepUpScopeOf', () => {
  it('reads the scope from the wire envelope (errorDetails.scope)', () => {
    expect(stepUpScopeOf(challenge('tip.destroy'))).toBe('tip.destroy');
    expect(stepUpScopeOf({ errorCode: STEP_UP_REQUIRED })).toBeUndefined();
  });

  it('reads the scope off the thrown shape the admin bridges produce', () => {
    // `ensureOk` must keep the code and the details on what it throws:
    // every ui-admin bridge goes through it, so a bare `Error(message)` would
    // make step-up unrecognisable on that whole path.
    const thrown = thrownChallenge('tip.destroy');
    expect(thrown).toBeInstanceOf(HttpError);
    expect(isStepUpRequired(thrown)).toBe(true);
    expect(stepUpScopeOf(thrown)).toBe('tip.destroy');
  });
});

describe('verify calls are auth-flow requests', () => {
  // A wrong code answers 401 (the backend deliberately says the same thing
  // for every failed verification). Without `skipAuthRefresh` the client would
  // refresh, automatically re-submit the SAME wrong code (counted as a second
  // failure server-side), and then log the user out.
  it('stepUpWithCode posts with skipAuthRefresh', async () => {
    const post = vi.fn(() => ok(grant()));
    const client = createClient({ post });

    await stepUpWithCode(client, 'tip.download', '123456', TwoFactorType.Totp);

    expect(post).toHaveBeenCalledWith(
      expect.stringContaining('/step-up/code'),
      expect.anything(),
      expect.objectContaining({ skipAuthRefresh: true }),
    );
  });

  it('sendStepUpCode posts with skipAuthRefresh', async () => {
    const post = vi.fn(() => ok('a***@example.com'));
    const client = createClient({ post });

    await sendStepUpCode(client, TwoFactorType.Email);

    expect(post).toHaveBeenCalledWith(
      expect.stringContaining('/step-up/send-code'),
      expect.anything(),
      expect.objectContaining({ skipAuthRefresh: true }),
    );
  });

  it('stepUpWithPasskey posts the grant request with skipAuthRefresh', async () => {
    givenBrowserSupportsPasskeys({ toJSON: () => ({ id: 'cred-1' }) });
    const post = vi.fn((url: string) =>
      url.includes('assert/begin')
        ? ok({ optionsJson: '{}', stateId: 'state-1' })
        : ok(grant()),
    );
    const client = createClient({ post });

    await stepUpWithPasskey(client, 'tip.download');

    const grantCall = (post.mock.calls as unknown as Array<[string, unknown, unknown]>).find(
      ([url]) => url.includes('/step-up/passkey'),
    );
    expect(grantCall?.[2]).toEqual(expect.objectContaining({ skipAuthRefresh: true }));
  });
});

describe('stepUpWithCode', () => {
  it('posts the code, type and scope together', async () => {
    const post = vi.fn(() => ok(grant()));
    const client = createClient({ post });

    const result = await stepUpWithCode(client, 'tip.download', '123456', TwoFactorType.Totp);

    expect(result.scope).toBe('tip.download');
    expect(post).toHaveBeenCalledWith(
      expect.stringContaining('/step-up/code'),
      { code: '123456', type: TwoFactorType.Totp, scope: 'tip.download' },
      expect.objectContaining({ skipAuthRefresh: true }),
    );
  });

  it('throws with the server message when verification fails', async () => {
    const post = vi.fn(() => Promise.resolve({ succeeded: false, message: 'Verification failed' }));
    const client = createClient({ post });

    await expect(stepUpWithCode(client, 'tip.download', '000000', TwoFactorType.Totp))
      .rejects.toThrow('Verification failed');
  });
});

describe('stepUpWithPasskey', () => {
  it('reuses the ordinary assertion challenge and carries the scope through', async () => {
    givenBrowserSupportsPasskeys({ toJSON: () => ({ id: 'cred-1' }) });
    const post = vi.fn((url: string) =>
      url.includes('assert/begin')
        ? ok({ optionsJson: '{}', stateId: 'state-1' })
        : ok(grant()),
    );
    const client = createClient({ post });

    const result = await stepUpWithPasskey(client, 'tip.download');

    expect(result?.scope).toBe('tip.download');
    expect(post).toHaveBeenCalledWith(
      expect.stringContaining('/step-up/passkey'),
      expect.objectContaining({ stateId: 'state-1', scope: 'tip.download' }),
      expect.objectContaining({ skipAuthRefresh: true }),
    );
  });

  it('returns null when the user dismisses the system dialog', async () => {
    // Dismissal is a normal outcome. Surfacing it as an error makes every UI
    // that calls this show a scary message for "I changed my mind".
    givenBrowserSupportsPasskeys(null);
    const post = vi.fn(() => ok({ optionsJson: '{}', stateId: 'state-1' }));
    const client = createClient({ post });

    expect(await stepUpWithPasskey(client, 'tip.download')).toBeNull();
    expect(post).toHaveBeenCalledTimes(1);
  });
});

describe('withStepUp', () => {
  // ★ HttpClient *resolves* failures as envelopes; only the admin bridges throw.
  // The first version of this helper handled the throwing path only, so every
  // raw-api caller got a silent no-op. Both shapes are pinned below.

  it('verifies and retries when the challenge is a returned envelope', async () => {
    const action = vi
      .fn()
      .mockResolvedValueOnce({ succeeded: false, ...challenge() })
      .mockResolvedValueOnce({ succeeded: true, data: 'done' });
    const verify = vi.fn(() => Promise.resolve(grant()));

    const result = await withStepUp(action, verify);

    expect(result).toEqual({ succeeded: true, data: 'done' });
    expect(verify).toHaveBeenCalledWith('tip.download');
    expect(action).toHaveBeenCalledTimes(2);
  });

  it('returns the challenge envelope untouched when verification is aborted', async () => {
    // Returned failures must come back *returned*, not thrown - the caller is
    // checking `succeeded`, and an exception would bypass their error handling.
    const envelope = { succeeded: false, ...challenge() };
    const action = vi.fn(() => Promise.resolve(envelope));
    const verify = vi.fn(() => Promise.resolve(null));

    expect(await withStepUp(action, verify)).toBe(envelope);
    expect(action).toHaveBeenCalledTimes(1);
  });

  it('passes a returned success through without consulting verify', async () => {
    const action = vi.fn(() => Promise.resolve({ succeeded: true, data: 1 }));
    const verify = vi.fn();

    expect(await withStepUp(action, verify)).toEqual({ succeeded: true, data: 1 });
    expect(verify).not.toHaveBeenCalled();
  });

  it('runs the action once when no challenge comes back', async () => {
    const action = vi.fn(() => Promise.resolve('done'));
    const verify = vi.fn();

    expect(await withStepUp(action, verify)).toBe('done');
    expect(verify).not.toHaveBeenCalled();
  });

  it('verifies once and retries the original action', async () => {
    const action = vi
      .fn()
      .mockRejectedValueOnce(thrownChallenge())
      .mockResolvedValueOnce('done');
    const verify = vi.fn(() => Promise.resolve(grant()));

    expect(await withStepUp(action, verify)).toBe('done');
    expect(verify).toHaveBeenCalledWith('tip.download');
    expect(action).toHaveBeenCalledTimes(2);
  });

  it('retries only once, so a grant that never sticks does not loop', async () => {
    // Looping would re-prompt forever without ever telling the user what is wrong.
    const action = vi.fn(() => Promise.reject(thrownChallenge()));
    const verify = vi.fn(() => Promise.resolve(grant()));

    await expect(withStepUp(action, verify)).rejects.toMatchObject({ errorCode: STEP_UP_REQUIRED });
    expect(action).toHaveBeenCalledTimes(2);
    expect(verify).toHaveBeenCalledTimes(1);
  });

  it('rethrows the challenge when the user aborts verification', async () => {
    // Returning null means "not done". Swallowing it would look like success
    // to the caller while nothing actually happened.
    const action = vi.fn(() => Promise.reject(thrownChallenge()));
    const verify = vi.fn(() => Promise.resolve(null));

    await expect(withStepUp(action, verify)).rejects.toMatchObject({ errorCode: STEP_UP_REQUIRED });
    expect(action).toHaveBeenCalledTimes(1);
  });

  it('passes other failures through untouched', async () => {
    const action = vi.fn(() => Promise.reject(new Error('server exploded')));
    const verify = vi.fn();

    await expect(withStepUp(action, verify)).rejects.toThrow('server exploded');
    expect(verify).not.toHaveBeenCalled();
  });
});
