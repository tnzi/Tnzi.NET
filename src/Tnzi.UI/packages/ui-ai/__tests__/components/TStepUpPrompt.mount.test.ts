/**
 * `TStepUpPrompt` mounted: the code field takes as many characters as the code
 * the user was given.
 *
 * An emailed / texted step-up code is `Identity:Otp:CodeLength` digits (4-8, a
 * runtime setting); the controller reads it from `GET /auth/config` and
 * reports it per method as `codeLength` (authenticator codes stay 6). A field
 * capped below the configured length truncates the code the user pastes.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, h, reactive, type App } from 'vue';
import TStepUpPrompt from '../../src/components/settings/TStepUpPrompt.vue';

function fakePrompt(over: Record<string, unknown> = {}) {
  return reactive({
    open: true,
    scope: 'identity.two-factor.manage',
    stage: 'code',
    methods: ['email', 'totp'],
    method: 'email' as string | null,
    sentTo: 'a***@example.com' as string | null,
    error: null as string | null,
    busy: false,
    canVerify: true,
    choose: vi.fn(async () => undefined),
    submitCode: vi.fn(async () => undefined),
    resendCode: vi.fn(async () => undefined),
    back: vi.fn(),
    cancel: vi.fn(),
    verify: vi.fn(),
    ...over,
  });
}

let app: App | null = null;
let host: HTMLElement | null = null;

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
});

/** The `maxlength` the rendered code field carries. */
function maxLengthOf(prompt: ReturnType<typeof fakePrompt>): string | null {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({ render: () => h(TStepUpPrompt, { prompt: prompt as never }) });
  app.mount(host);
  const input = host.querySelector('input');
  expect(input, 'code field rendered').not.toBeNull();
  return input!.getAttribute('maxlength');
}

describe('TStepUpPrompt code length', () => {
  it('accepts 8 characters for an emailed code on an 8-digit deployment', () => {
    expect(maxLengthOf(fakePrompt({ codeLength: 8 }))).toBe('8');
  });

  it('caps an authenticator code at 6 when the controller says 6', () => {
    expect(maxLengthOf(fakePrompt({ method: 'totp', sentTo: null, codeLength: 6 }))).toBe('6');
  });

  it('defaults to 6 when the controller does not report a length', () => {
    expect(maxLengthOf(fakePrompt())).toBe('6');
  });
});
