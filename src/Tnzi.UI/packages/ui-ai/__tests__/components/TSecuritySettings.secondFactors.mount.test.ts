/**
 * `TSecuritySettings` second-factor rows, mounted against a fake controller.
 *
 * The rows must follow the status endpoint (a method the deployment switched
 * off is absent from `methods` and shows nothing) and the browser (no
 * WebAuthn, no passkey rows), and their buttons must reach the controller.
 * Enrolment draws the authenticator URI as an SVG QR code, black on white.
 */
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue';
import TSecuritySettings from '../../src/components/settings/TSecuritySettings.vue';
import { createAiI18n } from '../../src/i18n';
import { en } from '../../src/locales/en';
import { zhCn } from '../../src/locales/zh-cn';

const KEY = { credentialId: 'k1', name: 'YubiKey', createdAt: '2026-10-01T00:00:00Z', isBackedUp: false };

function fakeController(opts: {
  methods: unknown[];
  passkeys?: unknown[];
  passkeySupported?: boolean;
  error?: string;
  totpSetup?: { authenticatorUri: string; sharedKey: string } | null;
}) {
  return {
    twoFactor: ref({ isEnabled: true, supportedTypes: [], isTotpEnabled: false, methods: opts.methods }),
    totpSetup: ref(opts.totpSetup ?? null),
    beginTotp: vi.fn(async () => true),
    confirmTotp: vi.fn(async () => true),
    sessions: ref([]),
    otherSessions: ref([]),
    passkeys: ref(opts.passkeys ?? []),
    passkeySupported: opts.passkeySupported ?? true,
    busy: ref(false),
    error: ref(opts.error ?? null),
    stepUp: null,
    load: vi.fn(async () => {}),
    loadSessions: vi.fn(async () => {}),
    loadPasskeys: vi.fn(async () => {}),
    setEmailTwoFactor: vi.fn(async () => true),
    setPasskeyTwoFactor: vi.fn(async () => true),
    addPasskey: vi.fn(async () => true),
    removePasskey: vi.fn(async () => true),
    isCurrentSession: () => false,
  };
}

const EMAIL = { type: 'Email', available: true, enabled: false, isPreferred: false };
const PASSKEY = { type: 'Passkey', available: true, enabled: false, isPreferred: false };

let app: App | null = null;
let host: HTMLElement | null = null;

async function mount(ctrl: ReturnType<typeof fakeController>, catalogue = en): Promise<HTMLElement> {
  host = document.createElement('div');
  document.body.appendChild(host);
  const Root = defineComponent({
    setup() {
      createAiI18n(catalogue);
      return () => h(TSecuritySettings, { controller: ctrl as never });
    },
  });
  app = createApp(Root);
  app.mount(host);
  await nextTick();
  await nextTick();
  return host;
}

function button(root: HTMLElement, label: string): HTMLButtonElement {
  const found = Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label);
  if (!found) throw new Error(`no button "${label}" in: ${root.textContent}`);
  return found as HTMLButtonElement;
}

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
});

describe('TSecuritySettings second factors', () => {
  it('shows neither row when the deployment lists neither method', async () => {
    const root = await mount(fakeController({ methods: [] }));
    expect(root.textContent).not.toContain(en.securitySettings.emailCode);
    expect(root.textContent).not.toContain(en.securitySettings.passkey);
  });

  it('turns the email code on from its row', async () => {
    const ctrl = fakeController({ methods: [EMAIL] });
    const root = await mount(ctrl);

    expect(root.textContent).toContain(en.securitySettings.emailCode);
    button(root, en.securitySettings.turnOn).click();
    expect(ctrl.setEmailTwoFactor).toHaveBeenCalledWith(true);
  });

  it('offers turning the email code off when it is on, and blocks turning it on without an address', async () => {
    const on = fakeController({ methods: [{ ...EMAIL, enabled: true }] });
    button(await mount(on), en.securitySettings.turnOff).click();
    expect(on.setEmailTwoFactor).toHaveBeenCalledWith(false);
    app?.unmount();
    host?.remove();

    const blocked = fakeController({ methods: [{ ...EMAIL, available: false, requiresAddress: true }] });
    const root = await mount(blocked);
    expect(root.textContent).toContain(en.securitySettings.emailNeedsAddress);
    expect(button(root, en.securitySettings.turnOn).disabled).toBe(true);
  });

  it('hides the passkey rows where the browser cannot run the ceremony', async () => {
    const root = await mount(fakeController({ methods: [PASSKEY], passkeySupported: false }));
    expect(root.textContent).not.toContain(en.securitySettings.passkey);
  });

  it('lists keys with a name and a date, and removes one after a confirming click', async () => {
    const ctrl = fakeController({ methods: [PASSKEY], passkeys: [KEY] });
    const root = await mount(ctrl);

    expect(root.textContent).toContain('YubiKey');
    expect(ctrl.loadPasskeys).toHaveBeenCalled();
    button(root, en.securitySettings.removeKey).click();
    expect(ctrl.removePasskey).not.toHaveBeenCalled();
    await nextTick();
    button(root, en.securitySettings.removeKeyConfirm).click();
    expect(ctrl.removePasskey).toHaveBeenCalledWith('k1');
  });

  it('adds a passkey from the first-time button', async () => {
    const ctrl = fakeController({ methods: [PASSKEY] });
    const root = await mount(ctrl);

    button(root, en.securitySettings.passkeyAdd).click();
    expect(ctrl.addPasskey).toHaveBeenCalled();
  });

  it('shows the controller error once, in the page', async () => {
    const root = await mount(fakeController({ methods: [EMAIL], error: 'Code could not be sent' }));
    expect(root.querySelectorAll('[role="alert"]')).toHaveLength(1);
    expect(root.textContent).toContain('Code could not be sent');
  });

  it('renders the rows from the Chinese catalogue', async () => {
    const root = await mount(fakeController({ methods: [EMAIL, PASSKEY] }), zhCn);
    expect(root.textContent).toContain(zhCn.securitySettings.emailCode);
    expect(root.textContent).toContain(zhCn.securitySettings.passkey);
  });
});

const TOTP = {
  authenticatorUri: 'otpauth://totp/Tnzi:ada@example.com?secret=JBSWY3DPEHPK3PXP&issuer=Tnzi',
  sharedKey: 'jbsw y3dp ehpk 3pxp',
};

/** The QR once the encoder (loaded on demand) has drawn it. */
async function qrOf(root: Element): Promise<SVGElement> {
  await vi.waitFor(() => expect(root.querySelector('svg[data-totp-qr] path')).not.toBeNull());
  return root.querySelector('svg[data-totp-qr]') as SVGElement;
}

describe('TSecuritySettings authenticator enrolment', () => {
  it('draws the code with the shared TQrCode, not an encoder of its own', () => {
    const source = readFileSync(resolve(__dirname, '../../src/components/settings/TSecuritySettings.vue'), 'utf8');
    expect(source).toMatch(/<TQrCode\b/);
    expect(source).not.toMatch(/uqr/);
  });

  it('draws the authenticator URI as an SVG QR code on white, with a name', async () => {
    const root = await mount(fakeController({ methods: [], totpSetup: TOTP }));
    const svg = await qrOf(root);
    expect(svg.getAttribute('role')).toBe('img');
    expect(svg.getAttribute('aria-label')).toBe(en.securitySettings.qrLabel);
    const [, , w, h] = svg.getAttribute('viewBox')!.split(' ').map(Number);
    // Version >= 2 for this URI, plus a four-module quiet zone on each side.
    expect(w).toBe(h);
    expect(w).toBeGreaterThanOrEqual(25 + 8);
    expect(svg.querySelector('rect')?.getAttribute('fill')).toBe('#fff');
    const path = svg.querySelector('path')!;
    expect(path.getAttribute('fill')).toBe('#000');
    expect(path.getAttribute('d')!.length).toBeGreaterThan(100);
    // The quiet zone is empty: no module is drawn in the first four columns.
    expect(path.getAttribute('d')).not.toMatch(/M[0-3] /);
    expect(root.textContent).toContain(TOTP.sharedKey);
  });

  it('a different URI draws a different code', async () => {
    const first = (await qrOf(await mount(fakeController({ methods: [], totpSetup: TOTP })))).querySelector('path')!.getAttribute('d');
    app?.unmount();
    host?.remove();
    const other = { ...TOTP, authenticatorUri: TOTP.authenticatorUri.replace('JBSWY3DP', 'KRSXG5CT') };
    const second = (await qrOf(await mount(fakeController({ methods: [], totpSetup: other })))).querySelector('path')!.getAttribute('d');
    expect(second).not.toBe(first);
  });

  it('Set up starts enrolment; no QR before it', async () => {
    const ctrl = fakeController({ methods: [] });
    const root = await mount(ctrl);
    expect(root.querySelector('svg[data-totp-qr]')).toBeNull();
    button(root, en.securitySettings.setUp).click();
    expect(ctrl.beginTotp).toHaveBeenCalled();
  });

  it('typing the code enables Turn on; Enter confirms the trimmed code, not during IME composition', async () => {
    const ctrl = fakeController({ methods: [], totpSetup: TOTP });
    const root = await mount(ctrl);
    expect(button(root, en.securitySettings.turnOn).disabled).toBe(true);

    const code = root.querySelector<HTMLInputElement>(`input[aria-label="${en.securitySettings.codePlaceholder}"]`)!;
    expect(code.classList.contains('t-input--code')).toBe(true);
    code.value = ' 123456 ';
    code.dispatchEvent(new Event('input'));
    await nextTick();
    expect(button(root, en.securitySettings.turnOn).disabled).toBe(false);

    code.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', isComposing: true }));
    expect(ctrl.confirmTotp).not.toHaveBeenCalled();
    code.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    expect(ctrl.confirmTotp).toHaveBeenCalledWith('123456');
  });

  it('shows the busy state on the enrolment button while the controller works', async () => {
    const ctrl = fakeController({ methods: [] });
    ctrl.busy.value = true;
    const root = await mount(ctrl);
    const setUp = button(root, en.securitySettings.setUp);
    expect(setUp.disabled).toBe(true);
    expect(setUp.getAttribute('aria-busy')).toBe('true');
  });
});
