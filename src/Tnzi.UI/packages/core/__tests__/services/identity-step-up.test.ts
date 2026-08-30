import { describe, it, expect, afterEach, vi } from 'vitest';
import type { HttpClient } from '../../src/http/http';
import {
  isStepUpRequired,
  stepUpScopeOf,
  stepUpWithCode,
  stepUpWithPasskey,
  withStepUp,
  STEP_UP_REQUIRED,
} from '../../src/services/identity/step-up';
import { TwoFactorType } from '../../src/services/identity/metadata';

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

function challenge(scope = 'tip.download') {
  return { errorCode: STEP_UP_REQUIRED, code: 401, errors: { scope } };
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
  it('reads the scope the server named', () => {
    expect(stepUpScopeOf(challenge('tip.destroy'))).toBe('tip.destroy');
    expect(stepUpScopeOf({ errorCode: STEP_UP_REQUIRED })).toBeUndefined();
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
      .mockRejectedValueOnce(challenge())
      .mockResolvedValueOnce('done');
    const verify = vi.fn(() => Promise.resolve(grant()));

    expect(await withStepUp(action, verify)).toBe('done');
    expect(verify).toHaveBeenCalledWith('tip.download');
    expect(action).toHaveBeenCalledTimes(2);
  });

  it('retries only once, so a grant that never sticks does not loop', async () => {
    // Looping would re-prompt forever without ever telling the user what is wrong.
    const action = vi.fn(() => Promise.reject(challenge()));
    const verify = vi.fn(() => Promise.resolve(grant()));

    await expect(withStepUp(action, verify)).rejects.toMatchObject({ errorCode: STEP_UP_REQUIRED });
    expect(action).toHaveBeenCalledTimes(2);
    expect(verify).toHaveBeenCalledTimes(1);
  });

  it('rethrows the challenge when the user aborts verification', async () => {
    // Returning null means "not done". Swallowing it would look like success
    // to the caller while nothing actually happened.
    const action = vi.fn(() => Promise.reject(challenge()));
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
