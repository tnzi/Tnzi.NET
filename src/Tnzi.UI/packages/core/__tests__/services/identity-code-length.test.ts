import { describe, it, expect, vi } from 'vitest';
import type { HttpClient } from '../../src/http/http';
import {
  DEFAULT_OTP_CODE_LENGTH,
  TOTP_CODE_LENGTH,
  codeLengthForMethod,
  resolveOtpCodeLength,
} from '../../src/services/identity/code-length';
import { StepUpPromptController } from '../../src/services/identity/step-up-prompt';

// ---------------------------------------------------------------------------
// Email / SMS codes are generated with the runtime setting
// `Identity:Otp:CodeLength` (4-8). An input fixed at 6 cells cannot accept the
// 8-digit code a deployment configured for 8 sends, so every delivered-code
// entry sizes itself from `/auth/config`'s `otpCodeLength`. Authenticator
// codes stay 6 whatever the setting says.
// ---------------------------------------------------------------------------

function ok<T>(data: T) {
  return Promise.resolve({ succeeded: true, code: 200, data });
}

function createClient(get: (url: string) => unknown) {
  return {
    get: vi.fn(get),
    post: vi.fn(() => ok('j***@example.com')),
    put: vi.fn(() => ok(null)),
    patch: vi.fn(() => ok(null)),
    delete: vi.fn(() => ok(null)),
  } as unknown as HttpClient;
}

async function settle(): Promise<void> {
  for (let i = 0; i < 5; i += 1) await Promise.resolve();
}

describe('resolveOtpCodeLength', () => {
  it('honours the length the deployment reports', () => {
    expect(resolveOtpCodeLength({ otpCodeLength: 8 })).toBe(8);
    expect(resolveOtpCodeLength({ otpCodeLength: 4 })).toBe(4);
    expect(resolveOtpCodeLength(8)).toBe(8);
  });

  it('defaults to 6 when the backend is older and omits the field', () => {
    expect(DEFAULT_OTP_CODE_LENGTH).toBe(6);
    expect(resolveOtpCodeLength({})).toBe(6);
    expect(resolveOtpCodeLength(null)).toBe(6);
    expect(resolveOtpCodeLength(undefined)).toBe(6);
  });

  it('treats a nonsensical value as unknown rather than rendering it', () => {
    expect(resolveOtpCodeLength({ otpCodeLength: 0 })).toBe(6);
    expect(resolveOtpCodeLength({ otpCodeLength: -3 })).toBe(6);
    expect(resolveOtpCodeLength({ otpCodeLength: 6.5 })).toBe(6);
    expect(resolveOtpCodeLength({ otpCodeLength: 500 })).toBe(6);
    expect(resolveOtpCodeLength({ otpCodeLength: '8' as unknown as number })).toBe(6);
  });
});

describe('codeLengthForMethod', () => {
  it('delivered codes follow the setting; the authenticator app stays at 6', () => {
    expect(codeLengthForMethod('sms', 8)).toBe(8);
    expect(codeLengthForMethod('email', 8)).toBe(8);
    expect(codeLengthForMethod('totp', 8)).toBe(TOTP_CODE_LENGTH);
    expect(TOTP_CODE_LENGTH).toBe(6);
  });
});

describe('StepUpPromptController code length', () => {
  it('reads otpCodeLength from /auth/config: an emailed code takes 8 digits, the authenticator 6', async () => {
    const client = createClient((url) => (url.endsWith('/auth/config') ? ok({ otpCodeLength: 8 }) : ok(null)));
    const prompt = new StepUpPromptController({ client, discoverMethods: async () => ['email', 'totp'] });
    const pending = prompt.verify('s');
    await settle();

    await prompt.choose('email');
    expect(prompt.method).toBe('email');
    expect(prompt.codeLength).toBe(8);

    prompt.back();
    await prompt.choose('totp');
    expect(prompt.codeLength).toBe(6);

    prompt.cancel();
    await pending;
  });

  it('stays at 6 when the backend does not report a length', async () => {
    const client = createClient(() => ok({ enableCodeLogin: true }));
    const prompt = new StepUpPromptController({ client, discoverMethods: async () => ['sms'] });
    const pending = prompt.verify('s');
    await settle();

    await prompt.choose('sms');
    expect(prompt.codeLength).toBe(6);

    prompt.cancel();
    await pending;
  });

  it('a failed config probe still opens the prompt, at the last known length', async () => {
    const client = createClient(() => Promise.reject(new Error('offline')));
    const prompt = new StepUpPromptController({ client, discoverMethods: async () => ['email'] });
    const pending = prompt.verify('s');
    await settle();

    expect(prompt.stage).toBe('choose');
    expect(prompt.methods).toEqual(['email']);
    await prompt.choose('email');
    expect(prompt.codeLength).toBe(6);

    prompt.cancel();
    await pending;
  });

  it('re-reads the length on every open: it is a runtime setting', async () => {
    let configured = 6;
    const prompt = new StepUpPromptController({
      client: createClient(() => ok(null)),
      discoverMethods: async () => ['email'],
      loadOtpCodeLength: async () => configured,
    });

    const first = prompt.verify('s');
    await settle();
    await prompt.choose('email');
    expect(prompt.codeLength).toBe(6);
    prompt.cancel();
    await first;

    configured = 8;
    const second = prompt.verify('s');
    await settle();
    await prompt.choose('email');
    expect(prompt.codeLength).toBe(8);
    prompt.cancel();
    await second;
  });
});
