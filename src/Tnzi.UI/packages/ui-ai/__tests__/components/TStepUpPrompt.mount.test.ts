/**
 * `TStepUpPrompt` mounted: the code field takes as many characters as the code
 * the user was given.
 *
 * An emailed / texted step-up code is `Identity:Otp:CodeLength` digits (4-8, a
 * runtime setting); the controller reads it from `GET /auth/config` and
 * reports it per method as `codeLength` (authenticator codes stay 6). A field
 * capped below the configured length truncates the code the user pastes.
 *
 * Also the interactions of the controls it is drawn with: the method buttons,
 * typing the code, Enter (never the IME-commit one), the disabled and loading
 * states, and the error state on the field.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, h, nextTick, reactive, type App } from 'vue';
import TStepUpPrompt from '../../src/components/settings/TStepUpPrompt.vue';
import { en } from '../../src/locales/en';

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

function mountPrompt(prompt: ReturnType<typeof fakePrompt>): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({ render: () => h(TStepUpPrompt, { prompt: prompt as never }) });
  app.mount(host);
  return host;
}

function buttonNamed(root: HTMLElement, label: string): HTMLButtonElement {
  const found = Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label);
  if (!found) throw new Error(`no button "${label}" in: ${root.textContent}`);
  return found;
}

describe('TStepUpPrompt interactions', () => {
  it('offers one button per method and chooses the clicked one', () => {
    const prompt = fakePrompt({ stage: 'choose', method: null, sentTo: null });
    const root = mountPrompt(prompt);
    expect(root.querySelector('input')).toBeNull();
    buttonNamed(root, en.settings.stepUpMethodTotp).click();
    expect(prompt.choose).toHaveBeenCalledWith('totp');
  });

  it('Verify stays disabled until a code is typed, then submits it', async () => {
    const prompt = fakePrompt();
    const root = mountPrompt(prompt);
    const verify = buttonNamed(root, en.settings.stepUpVerify);
    expect(verify.disabled).toBe(true);
    expect(verify.classList.contains('t-button--primary')).toBe(true);

    const input = root.querySelector('input')!;
    expect(input.classList.contains('t-input--code')).toBe(true);
    input.value = '424242';
    input.dispatchEvent(new Event('input'));
    await nextTick();
    expect(buttonNamed(root, en.settings.stepUpVerify).disabled).toBe(false);
    buttonNamed(root, en.settings.stepUpVerify).click();
    expect(prompt.submitCode).toHaveBeenCalledWith('424242');
  });

  it('Enter submits the code, but not the Enter that commits an IME composition', async () => {
    const prompt = fakePrompt();
    const root = mountPrompt(prompt);
    const input = root.querySelector('input')!;
    input.value = '424242';
    input.dispatchEvent(new Event('input'));
    await nextTick();
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', isComposing: true }));
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', keyCode: 229 }));
    expect(prompt.submitCode).not.toHaveBeenCalled();
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    expect(prompt.submitCode).toHaveBeenCalledTimes(1);
  });

  it('while verifying the field is locked and Verify shows it is busy', () => {
    const root = mountPrompt(fakePrompt({ stage: 'busy', busy: true }));
    expect(root.querySelector('input')!.disabled).toBe(true);
    expect(buttonNamed(root, en.settings.stepUpVerify).getAttribute('aria-busy')).toBe('true');
    expect(buttonNamed(root, en.settings.stepUpCancel).disabled).toBe(true);
  });

  it('a rejected code marks the field invalid and says why', () => {
    const root = mountPrompt(fakePrompt({ error: 'That code is not right.' }));
    expect(root.querySelector('input')!.getAttribute('aria-invalid')).toBe('true');
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('That code is not right.');
  });

  it('Back, Resend and Cancel reach the controller', () => {
    const prompt = fakePrompt();
    const root = mountPrompt(prompt);
    buttonNamed(root, en.settings.stepUpBack).click();
    expect(prompt.back).toHaveBeenCalled();
    buttonNamed(root, en.settings.stepUpResend).click();
    expect(prompt.resendCode).toHaveBeenCalled();
    buttonNamed(root, en.settings.stepUpCancel).click();
    expect(prompt.cancel).toHaveBeenCalled();
  });
});
