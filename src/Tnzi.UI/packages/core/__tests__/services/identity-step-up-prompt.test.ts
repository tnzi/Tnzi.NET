import { describe, it, expect, vi, afterEach } from 'vitest';
import type { HttpClient } from '../../src/http/http';
import {
  StepUpPromptController,
  discoverStepUpMethods,
  type StepUpMethod,
} from '../../src/services/identity/step-up-prompt';
import { withStepUp } from '../../src/services/identity/step-up';
import { TwoFactorType } from '../../src/services/identity/metadata';
import { normalizeApiResult } from '../../src/http/response';

// ---------------------------------------------------------------------------
// The prompt is the missing half of `withStepUp`: the verify callback it needs
// has to ask a person to touch a key or type a code, wait, and only then
// resolve. Both UI packages render the same state machine, so it lives here
// once. Tests drive it exactly as a component would - by reading the reactive
// fields and calling the actions - with the HTTP layer faked.
// ---------------------------------------------------------------------------

const GRANT = { scope: 'identity.two-factor.manage', expiresAt: '2030-01-01T00:00:00Z', singleUse: false };

function ok<T>(data: T) {
  return Promise.resolve({ succeeded: true, code: 200, data });
}

function fail(message: string, code = 400) {
  return Promise.resolve({ succeeded: false, code, message, data: null });
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

function challenge(scope = 'identity.two-factor.manage') {
  return normalizeApiResult(
    JSON.parse(
      `{"succeeded":false,"success":false,"code":401,"message":"This action requires re-authentication","errorCode":"IDENTITY_STEP_UP_REQUIRED","errorDetails":{"scope":"${scope}"}}`,
    ),
  );
}

/** Let the controller's own awaits settle without faking timers. */
async function settle(): Promise<void> {
  for (let i = 0; i < 5; i += 1) await Promise.resolve();
}


function givenBrowserSupportsPasskeys(assertion: unknown) {
  const publicKeyCredential = {
    parseCreationOptionsFromJSON: vi.fn((json: unknown) => json),
    parseRequestOptionsFromJSON: vi.fn((json: unknown) => json),
  };
  const navigatorStub = { credentials: { create: vi.fn(), get: vi.fn(async () => assertion) } };
  vi.stubGlobal('PublicKeyCredential', publicKeyCredential);
  vi.stubGlobal('navigator', navigatorStub);
  vi.stubGlobal('window', { PublicKeyCredential: publicKeyCredential, navigator: navigatorStub });
}

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('discoverStepUpMethods', () => {
  it('offers the channels the account can actually complete', async () => {
    // SMS/email: address confirmed (`available`). TOTP: an authenticator is
    // enrolled (`enabled`) - `available` for TOTP only says the deployment
    // allows it, which is not something a code can be typed against.
    const client = createClient({
      get: vi.fn((url: string) => {
        if (url.endsWith('/two-factor/status')) {
          return ok({
            isEnabled: true,
            isTotpEnabled: true,
            supportedTypes: [],
            methods: [
              { type: 'Totp', available: true, enabled: true, isPreferred: true },
              { type: 'Sms', available: false, enabled: false, isPreferred: false, requiresAddress: true },
              { type: 'Email', available: true, enabled: false, isPreferred: false },
            ],
          });
        }
        return ok(null);
      }),
    });

    expect(await discoverStepUpMethods(client)).toEqual(['totp', 'email']);
  });

  it('does not offer TOTP that the deployment allows but the account never enrolled', async () => {
    const client = createClient({
      get: vi.fn((url: string) => {
        if (url.endsWith('/two-factor/status')) {
          return ok({
            methods: [
              { type: 'Totp', available: true, enabled: false, isPreferred: false },
              { type: 'Sms', available: true, enabled: false, isPreferred: false },
            ],
          });
        }
        return ok(null);
      }),
    });

    expect(await discoverStepUpMethods(client)).toEqual(['sms']);
  });

  it('offers a passkey only when the browser can run one AND the account has one', async () => {
    givenBrowserSupportsPasskeys(null);
    const client = createClient({
      get: vi.fn((url: string) => {
        if (url.endsWith('/passkey/credentials')) return ok([{ id: 'c1' }]);
        if (url.endsWith('/two-factor/status')) return ok({ methods: [] });
        return ok(null);
      }),
    });

    expect(await discoverStepUpMethods(client)).toEqual(['passkey']);
  });

  it('a failed probe removes that method instead of failing the prompt', async () => {
    const client = createClient({
      get: vi.fn((url: string) => {
        if (url.endsWith('/two-factor/status')) return Promise.reject(new Error('503'));
        return ok(null);
      }),
    });

    expect(await discoverStepUpMethods(client)).toEqual([]);
  });
});

describe('StepUpPromptController', () => {
  it('starts closed and idle', () => {
    const prompt = new StepUpPromptController({ client: createClient() });
    expect(prompt.open).toBe(false);
    expect(prompt.stage).toBe('idle');
    expect(prompt.scope).toBeNull();
  });

  it('verify() opens with the scope, discovers methods, and waits at "choose"', async () => {
    const prompt = new StepUpPromptController({
      client: createClient(),
      discoverMethods: async () => ['email', 'totp'],
    });

    const pending = prompt.verify('identity.two-factor.manage');
    expect(prompt.open).toBe(true);
    expect(prompt.scope).toBe('identity.two-factor.manage');
    await settle();

    expect(prompt.stage).toBe('choose');
    expect(prompt.methods).toEqual(['email', 'totp']);

    prompt.cancel();
    expect(await pending).toBeNull();
    expect(prompt.open).toBe(false);
    expect(prompt.stage).toBe('idle');
  });

  it('email: choose sends a step-up code, submit verifies it with the scope, and resolves the grant', async () => {
    const post = vi.fn((url: string) => {
      if (url.endsWith('/step-up/send-code')) return ok('a***@example.com');
      if (url.endsWith('/step-up/code')) return ok(GRANT);
      return ok(null);
    });
    const prompt = new StepUpPromptController({
      client: createClient({ post }),
      discoverMethods: async () => ['email'],
    });

    const pending = prompt.verify('identity.two-factor.manage');
    await settle();
    await prompt.choose('email');

    expect(post).toHaveBeenCalledWith(
      '/auth/step-up/send-code',
      { type: TwoFactorType.Email },
      expect.objectContaining({ skipAuthRefresh: true }),
    );
    expect(prompt.stage).toBe('code');
    expect(prompt.method).toBe('email');
    expect(prompt.sentTo).toBe('a***@example.com');

    await prompt.submitCode(' 123456 ');

    expect(post).toHaveBeenCalledWith(
      '/auth/step-up/code',
      { code: '123456', type: TwoFactorType.Email, scope: 'identity.two-factor.manage' },
      expect.objectContaining({ skipAuthRefresh: true }),
    );
    expect(await pending).toEqual(GRANT);
    expect(prompt.open).toBe(false);
  });

  it('totp: choose goes straight to the code stage without sending anything', async () => {
    const post = vi.fn(() => ok(GRANT));
    const prompt = new StepUpPromptController({
      client: createClient({ post }),
      discoverMethods: async () => ['totp'],
    });

    const pending = prompt.verify('s');
    await settle();
    await prompt.choose('totp');

    expect(post).not.toHaveBeenCalled();
    expect(prompt.stage).toBe('code');
    expect(prompt.sentTo).toBeNull();

    await prompt.submitCode('000000');
    expect(post).toHaveBeenCalledWith(
      '/auth/step-up/code',
      { code: '000000', type: TwoFactorType.Totp, scope: 's' },
      expect.anything(),
    );
    expect(await pending).toEqual(GRANT);
  });

  it('a wrong code keeps the prompt open with the server message, and a retry can still succeed', async () => {
    let attempts = 0;
    const post = vi.fn((url: string) => {
      if (url.endsWith('/step-up/code')) {
        attempts += 1;
        return attempts === 1 ? fail('Invalid or expired verification code') : ok(GRANT);
      }
      return ok(null);
    });
    const prompt = new StepUpPromptController({
      client: createClient({ post }),
      discoverMethods: async () => ['totp'],
    });

    const pending = prompt.verify('s');
    await settle();
    await prompt.choose('totp');
    await prompt.submitCode('111111');

    expect(prompt.open).toBe(true);
    expect(prompt.stage).toBe('code');
    expect(prompt.error).toBe('Invalid or expired verification code');

    await prompt.submitCode('222222');
    expect(await pending).toEqual(GRANT);
  });

  it('an empty code is refused locally', async () => {
    const post = vi.fn(() => ok(GRANT));
    const prompt = new StepUpPromptController({
      client: createClient({ post }),
      discoverMethods: async () => ['totp'],
    });
    const pending = prompt.verify('s');
    await settle();
    await prompt.choose('totp');

    await prompt.submitCode('   ');

    expect(post).not.toHaveBeenCalled();
    expect(prompt.error).toBeTruthy();
    prompt.cancel();
    expect(await pending).toBeNull();
  });

  it('a failed send keeps the chooser and shows why', async () => {
    const post = vi.fn(() => fail('Sms is not available for this account'));
    const prompt = new StepUpPromptController({
      client: createClient({ post }),
      discoverMethods: async () => ['sms', 'email'],
    });
    const pending = prompt.verify('s');
    await settle();

    await prompt.choose('sms');

    expect(prompt.stage).toBe('choose');
    expect(prompt.error).toBe('Sms is not available for this account');
    prompt.cancel();
    await pending;
  });

  it('back() returns from the code stage to the chooser and clears the sent address', async () => {
    const post = vi.fn(() => ok('+1***5'));
    const prompt = new StepUpPromptController({
      client: createClient({ post }),
      discoverMethods: async () => ['sms', 'totp'],
    });
    const pending = prompt.verify('s');
    await settle();
    await prompt.choose('sms');
    expect(prompt.stage).toBe('code');

    prompt.back();

    expect(prompt.stage).toBe('choose');
    expect(prompt.method).toBeNull();
    expect(prompt.sentTo).toBeNull();
    prompt.cancel();
    await pending;
  });

  it('passkey: choose runs the assertion ceremony and resolves the grant', async () => {
    givenBrowserSupportsPasskeys({ toJSON: () => ({ id: 'cred-1' }) });
    const post = vi.fn((url: string) => {
      if (url.endsWith('/passkey/assert/begin')) return ok({ stateId: 'st', optionsJson: '{}' });
      if (url.endsWith('/step-up/passkey')) return ok(GRANT);
      return ok(null);
    });
    const prompt = new StepUpPromptController({
      client: createClient({ post }),
      discoverMethods: async () => ['passkey'],
    });

    const pending = prompt.verify('identity.account.destroy');
    await settle();
    await prompt.choose('passkey');

    expect(post).toHaveBeenCalledWith(
      '/auth/step-up/passkey',
      expect.objectContaining({ stateId: 'st', scope: 'identity.account.destroy' }),
      expect.anything(),
    );
    expect(await pending).toEqual(GRANT);
    expect(prompt.open).toBe(false);
  });

  it('passkey: dismissing the browser dialog returns to the chooser, not an error', async () => {
    givenBrowserSupportsPasskeys(null);
    const post = vi.fn((url: string) =>
      url.endsWith('/passkey/assert/begin') ? ok({ stateId: 'st', optionsJson: '{}' }) : ok(null),
    );
    const prompt = new StepUpPromptController({
      client: createClient({ post }),
      discoverMethods: async () => ['passkey', 'totp'],
    });
    const pending = prompt.verify('s');
    await settle();

    await prompt.choose('passkey');

    expect(prompt.open).toBe(true);
    expect(prompt.stage).toBe('choose');
    expect(prompt.error).toBeNull();
    prompt.cancel();
    expect(await pending).toBeNull();
  });

  it('with nothing to offer, the prompt says so and can only be cancelled', async () => {
    const prompt = new StepUpPromptController({
      client: createClient(),
      discoverMethods: async () => [],
    });
    const pending = prompt.verify('s');
    await settle();

    expect(prompt.stage).toBe('choose');
    expect(prompt.methods).toEqual([]);
    expect(prompt.canVerify).toBe(false);
    prompt.cancel();
    expect(await pending).toBeNull();
  });

  it('★ closes the loop with withStepUp: challenge, prompt, verify, replay once', async () => {
    let calls = 0;
    const action = vi.fn(async () => {
      calls += 1;
      return calls === 1 ? challenge() : { succeeded: true, code: 200, data: 'done' };
    });
    const post = vi.fn((url: string) => (url.endsWith('/step-up/code') ? ok(GRANT) : ok(null)));
    const prompt = new StepUpPromptController({
      client: createClient({ post }),
      discoverMethods: async () => ['totp'],
    });

    const result = withStepUp(action, (scope) => prompt.verify(scope));
    await settle();
    expect(prompt.open).toBe(true);
    expect(prompt.scope).toBe('identity.two-factor.manage');

    await prompt.choose('totp');
    await prompt.submitCode('123456');

    expect(await result).toMatchObject({ succeeded: true, data: 'done' });
    expect(action).toHaveBeenCalledTimes(2);
  });

  it('★ cancelling hands the original challenge back to the caller (not a silent success)', async () => {
    const action = vi.fn(async () => challenge());
    const prompt = new StepUpPromptController({
      client: createClient(),
      discoverMethods: async () => ['totp'],
    });

    const result = withStepUp(action, (scope) => prompt.verify(scope));
    await settle();
    prompt.cancel();

    expect(await result).toMatchObject({ succeeded: false, errorCode: 'IDENTITY_STEP_UP_REQUIRED' });
    expect(action).toHaveBeenCalledTimes(1);
  });

  it('★ a request still in flight when the prompt was cancelled cannot touch the next prompt', async () => {
    // Cancel is disabled in the UI while busy, but the controller is public
    // API: a stale verify landing in a freshly opened prompt would resolve
    // the NEW prompt with a grant for the OLD scope.
    let releaseVerify: (value: unknown) => void = () => undefined;
    const post = vi.fn((url: string) =>
      url.endsWith('/step-up/code')
        ? new Promise((resolve) => {
            releaseVerify = resolve;
          })
        : ok(null),
    );
    const prompt = new StepUpPromptController({
      client: createClient({ post }),
      discoverMethods: async () => ['totp'],
    });

    const first = prompt.verify('first');
    await settle();
    await prompt.choose('totp');
    const submit = prompt.submitCode('123456');
    prompt.cancel();
    expect(await first).toBeNull();

    const second = prompt.verify('second');
    await settle();
    expect(prompt.scope).toBe('second');

    releaseVerify({ succeeded: true, code: 200, data: { ...GRANT, scope: 'first' } });
    await submit;
    await settle();

    expect(prompt.open).toBe(true);
    expect(prompt.scope).toBe('second');
    expect(prompt.stage).toBe('choose');
    prompt.cancel();
    expect(await second).toBeNull();
  });

  it('a discovery still in flight when the prompt was cancelled does not populate the next prompt', async () => {
    // Both discoveries are still pending when the stale one resolves, so the
    // second prompt is still `loading` - only the generation check can tell
    // the two results apart.
    const releases: Array<(methods: StepUpMethod[]) => void> = [];
    const prompt = new StepUpPromptController({
      client: createClient(),
      discoverMethods: () =>
        new Promise<StepUpMethod[]>((resolve) => {
          releases.push(resolve);
        }),
    });

    const first = prompt.verify('a');
    prompt.cancel();
    expect(await first).toBeNull();
    const second = prompt.verify('b');
    await settle();
    expect(prompt.stage).toBe('loading');

    releases[0]!(['passkey', 'sms', 'email']);
    await settle();
    expect(prompt.stage).toBe('loading');
    expect(prompt.methods).toEqual([]);

    releases[1]!(['totp']);
    await settle();
    expect(prompt.stage).toBe('choose');
    expect(prompt.methods).toEqual(['totp']);
    prompt.cancel();
    await second;
  });

  it('a second verify() while one is open waits its turn instead of clobbering the first', async () => {
    const post = vi.fn(() => ok(GRANT));
    const prompt = new StepUpPromptController({
      client: createClient({ post }),
      discoverMethods: async () => ['totp'],
    });

    const first = prompt.verify('a');
    const second = prompt.verify('b');
    await settle();
    expect(prompt.scope).toBe('a');

    await prompt.choose('totp');
    await prompt.submitCode('1');
    expect(await first).toEqual(GRANT);
    await settle();

    expect(prompt.open).toBe(true);
    expect(prompt.scope).toBe('b');
    prompt.cancel();
    expect(await second).toBeNull();
  });
});
