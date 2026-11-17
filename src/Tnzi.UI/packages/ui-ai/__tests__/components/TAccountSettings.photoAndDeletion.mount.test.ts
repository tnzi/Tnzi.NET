/**
 * `TAccountSettings` profile photo row and "Delete account" group, mounted
 * against a fake controller: the photo is a real button that opens the file
 * picker, a picked file reaches the controller, and deleting the account
 * cannot happen without typing the address - nor at all when the consumer
 * blocked it. The fields are plain `.t-input` boxes and the buttons
 * `TButton`s: typing, Enter (never the one that commits an IME composition),
 * disabled and loading states are pinned here too.
 */
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { computed, createApp, defineComponent, h, nextTick, ref, type App } from 'vue';
import TAccountSettings from '../../src/components/settings/TAccountSettings.vue';
import { createAiI18n } from '../../src/i18n';
import { en } from '../../src/locales/en';
import { zhCn } from '../../src/locales/zh-cn';

function fakeController(over: { avatarUrl?: string | null; target?: string; deleteError?: unknown } = {}) {
  return {
    profile: ref({ id: 'u1', userName: 'ada', nickname: 'Ada', email: 'ada@example.com' }),
    draft: ref({ nickname: 'Ada', email: 'ada@example.com', phoneNumber: '' }),
    busy: ref(false),
    error: ref(null),
    dirty: computed(() => false),
    stepUp: null,
    load: vi.fn(async () => {}),
    avatarUrl: computed(() => over.avatarUrl ?? null),
    avatarBusy: ref(false),
    avatarError: ref(null),
    avatarMaxBytes: 10 * 1024 * 1024,
    setAvatar: vi.fn(async () => true),
    removeAvatar: vi.fn(async () => true),
    deleteConfirmationTarget: computed(() => over.target ?? 'ada@example.com'),
    deleteBusy: ref(false),
    deleteError: ref(over.deleteError ?? null),
    deleteAccount: vi.fn(async () => true),
    saveProfile: vi.fn(async () => true),
    resetDraft: vi.fn(),
    sendEmailChangeCode: vi.fn(async () => true),
    sendPhoneChangeCode: vi.fn(async () => true),
    confirmEmailChange: vi.fn(async () => true),
    confirmPhoneChange: vi.fn(async () => true),
    changePassword: vi.fn(async () => true),
  };
}

let app: App | null = null;
let host: HTMLElement | null = null;

async function mount(
  ctrl: ReturnType<typeof fakeController>,
  props: Record<string, unknown> = {},
  catalogue = en,
): Promise<HTMLElement> {
  host = document.createElement('div');
  document.body.appendChild(host);
  const Root = defineComponent({
    setup() {
      createAiI18n(catalogue);
      return () => h(TAccountSettings, { controller: ctrl as never, ...props });
    },
  });
  app = createApp(Root);
  app.mount(host);
  await nextTick();
  await nextTick();
  return host;
}

function button(root: HTMLElement, label: string): HTMLButtonElement {
  const found = Array.from(root.querySelectorAll('button')).find(
    (b) => b.textContent?.trim() === label || b.getAttribute('aria-label') === label,
  );
  if (!found) throw new Error(`no button "${label}" in: ${root.textContent}`);
  return found as HTMLButtonElement;
}

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
});

describe('TAccountSettings - profile photo', () => {
  it('shows the photo row with the accepted formats and the size limit', async () => {
    const root = await mount(fakeController());
    expect(root.textContent).toContain('Profile photo');
    expect(root.textContent).toContain('PNG, JPEG, or WebP, up to 10 MB.');
    const input = root.querySelector('input[type="file"]') as HTMLInputElement;
    expect(input.accept).toBe('image/png,image/jpeg,image/webp');
  });

  it('the avatar is a labelled button that opens the picker; a picked file goes to the controller', async () => {
    const ctrl = fakeController();
    const root = await mount(ctrl);
    const input = root.querySelector('input[type="file"]') as HTMLInputElement;
    const click = vi.spyOn(input, 'click');

    button(root, 'Change profile photo').click();
    expect(click).toHaveBeenCalled();

    const file = new File(['x'], 'me.png', { type: 'image/png' });
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change'));
    await nextTick();
    expect(ctrl.setAvatar).toHaveBeenCalledWith(file);
  });

  it('offers the remove chip only when there is a photo', async () => {
    let root = await mount(fakeController({ avatarUrl: null }));
    expect(root.querySelector('[data-image-remove]')).toBeNull();
    app?.unmount();
    host?.remove();

    const ctrl = fakeController({ avatarUrl: '/api/files/f1/download' });
    root = await mount(ctrl);
    button(root, 'Remove profile photo').click();
    expect(ctrl.removeAvatar).toHaveBeenCalled();
    expect(root.querySelector('img')?.getAttribute('src')).toBe('/api/files/f1/download');
  });

  it('is a round TImagePicker that is busy while the photo is saved', async () => {
    const ctrl = fakeController({ avatarUrl: '/api/files/f1/download' });
    ctrl.avatarBusy.value = true;
    const root = await mount(ctrl);
    expect(root.querySelector('.t-image-picker')?.classList.contains('is-round')).toBe(true);
    const pick = root.querySelector<HTMLButtonElement>('[data-image-pick]')!;
    expect(pick.disabled).toBe(true);
    expect(pick.getAttribute('aria-busy')).toBe('true');
    expect(root.querySelector('[data-image-remove]')).toBeNull();
  });

  it('can be hidden by the consumer', async () => {
    const root = await mount(fakeController(), { allowAvatar: false });
    expect(root.textContent).not.toContain('Profile photo');
  });
});

describe('TAccountSettings - delete account', () => {
  it('requires typing the address before the destructive button enables', async () => {
    const ctrl = fakeController();
    const root = await mount(ctrl);

    button(root, 'Delete account').click();
    await nextTick();
    const confirm = button(root, 'Delete my account');
    expect(confirm.disabled).toBe(true);
    expect(root.textContent).toContain('Type ada@example.com to confirm.');

    const input = Array.from(root.querySelectorAll('input')).find((i) => i.placeholder === 'ada@example.com')!;
    input.value = 'ADA@example.com';
    input.dispatchEvent(new Event('input'));
    await nextTick();
    expect(button(root, 'Delete my account').disabled).toBe(false);

    button(root, 'Delete my account').click();
    await nextTick();
    expect(ctrl.deleteAccount).toHaveBeenCalledWith('ADA@example.com');
  });

  it('a blocking reason disables the button and says why', async () => {
    const root = await mount(fakeController(), { deletion: { blockedReason: 'Cancel your subscription first.' } });
    expect(button(root, 'Delete account').disabled).toBe(true);
    expect(root.textContent).toContain('Cancel your subscription first.');
  });

  it('can be hidden by the consumer', async () => {
    const root = await mount(fakeController(), { deletion: false });
    expect(Array.from(root.querySelectorAll('button')).some((b) => b.textContent?.trim() === 'Delete account')).toBe(false);
  });

  it('explains an account still in use, keeping what the server said holds it', async () => {
    const root = await mount(
      fakeController({ deleteError: { reason: 'server', errorCode: 'IDENTITY_USER_IN_USE', message: 'On the staff roster.' } }),
    );
    const alert = root.querySelector('[role="alert"]')!;
    expect(alert.textContent).toContain('still in use');
    expect(alert.textContent).toContain('On the staff roster.');
  });

  it('is translated', async () => {
    const root = await mount(fakeController(), {}, zhCn);
    expect(root.textContent).toContain('头像');
    expect(root.textContent).toContain('删除账号');
  });
});

function type(input: HTMLInputElement, value: string): void {
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

function enter(input: HTMLInputElement, composing = false): void {
  input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', isComposing: composing, bubbles: true }));
}

function field(root: HTMLElement, label: string): HTMLInputElement {
  const found = root.querySelector<HTMLInputElement>(`input[aria-label="${label}"]`);
  if (!found) throw new Error(`no field "${label}"`);
  return found;
}

describe('TAccountSettings - fields and buttons', () => {
  it('the display name is a .t-input that writes the draft', async () => {
    const ctrl = fakeController();
    const root = await mount(ctrl);
    const name = field(root, 'Display name');
    expect(name.classList.contains('t-input')).toBe(true);
    expect(name.maxLength).toBe(64);
    type(name, 'Ada L.');
    expect(ctrl.draft.value.nickname).toBe('Ada L.');
  });

  it('Save is the primary button, disabled while nothing changed, busy while saving', async () => {
    const ctrl = fakeController();
    const root = await mount(ctrl);
    const save = button(root, 'Save');
    expect(save.classList.contains('t-button--primary')).toBe(true);
    expect(save.disabled).toBe(true);
    ctrl.busy.value = true;
    await nextTick();
    expect(button(root, 'Save').getAttribute('aria-busy')).toBe('true');
  });

  it('changes the email in two steps: type the address, send, type the code, Enter confirms', async () => {
    const ctrl = fakeController();
    const root = await mount(ctrl);
    button(root, 'Change').click();
    await nextTick();

    expect(button(root, 'Send code').disabled).toBe(true);
    const target = field(root, 'New email');
    expect(target.type).toBe('email');
    type(target, 'new@example.com');
    await nextTick();
    enter(target);
    await nextTick();
    await nextTick();
    expect(ctrl.sendEmailChangeCode).toHaveBeenCalledWith('new@example.com');

    const code = field(root, 'Verification code');
    expect(code.classList.contains('t-input--code')).toBe(true);
    type(code, '123456');
    await nextTick();
    enter(code, true);
    expect(ctrl.confirmEmailChange).not.toHaveBeenCalled();
    enter(code);
    await nextTick();
    expect(ctrl.confirmEmailChange).toHaveBeenCalledWith('new@example.com', '123456');
  });

  it('each password box shows and hides on its own toggle', async () => {
    const root = await mount(fakeController());
    const current = field(root, 'Current password');
    expect(current.type).toBe('password');
    expect(current.autocomplete).toBe('current-password');
    const toggles = Array.from(root.querySelectorAll<HTMLButtonElement>('button[aria-label="Show password"]'));
    expect(toggles).toHaveLength(3);
    toggles[0].click();
    await nextTick();
    expect(field(root, 'Current password').type).toBe('text');
    expect(field(root, 'New password').type).toBe('password');
    expect(root.querySelector('button[aria-label="Hide password"]')?.getAttribute('aria-pressed')).toBe('true');
  });

  it('each password box says what to type, in the active language', async () => {
    const root = await mount(fakeController());
    expect(field(root, 'Current password').placeholder).toBe(en.accountSettings.currentPasswordPlaceholder);
    expect(field(root, 'New password').placeholder).toBe(en.accountSettings.newPasswordPlaceholder);
    expect(field(root, 'Confirm new password').placeholder).toBe(en.accountSettings.confirmPasswordPlaceholder);
    app?.unmount();
    host?.remove();
    const zh = await mount(fakeController(), {}, zhCn);
    expect(field(zh, zhCn.accountSettings.currentPassword).placeholder).toBe('输入当前密码');
  });

  it('the display name and the password boxes are sized by the same rule', async () => {
    const root = await mount(fakeController());
    expect(field(root, 'Display name').classList.contains('t-settings-field__control')).toBe(true);
    expect(field(root, 'New password').closest('.t-settings-field__control')).not.toBeNull();
    /* settings-fields.css loads before controls.css, so a single class would lose
       to `.t-input { width: 100% }` on source order for a field carrying both. */
    const css = readFileSync(resolve(__dirname, '../../src/components/settings/settings-fields.css'), 'utf8');
    expect(css).toMatch(/\.t-input\.t-settings-field__control\s*\{[^}]*width:\s*320px/);
  });

  it('a mismatched confirmation marks the box and does not call the controller', async () => {
    const ctrl = fakeController();
    const root = await mount(ctrl);
    type(field(root, 'Current password'), 'old');
    type(field(root, 'New password'), 'new-one');
    type(field(root, 'Confirm new password'), 'new-two');
    await nextTick();
    button(root, 'Update password').click();
    await nextTick();
    expect(ctrl.changePassword).not.toHaveBeenCalled();
    expect(field(root, 'Confirm new password').getAttribute('aria-invalid')).toBe('true');
    expect(root.textContent).toContain('The two new passwords do not match.');

    type(field(root, 'Confirm new password'), 'new-one');
    await nextTick();
    enter(field(root, 'Confirm new password'));
    await nextTick();
    expect(ctrl.changePassword).toHaveBeenCalledWith('old', 'new-one');
  });

  it('Enter in the delete confirmation deletes only once the address matches', async () => {
    const ctrl = fakeController();
    const root = await mount(ctrl);
    button(root, 'Delete account').click();
    await nextTick();
    const input = field(root, 'Type ada@example.com to confirm.');
    type(input, 'someone@else');
    await nextTick();
    enter(input);
    expect(ctrl.deleteAccount).not.toHaveBeenCalled();
    type(input, 'ada@example.com');
    await nextTick();
    expect(button(root, 'Delete my account').classList.contains('t-button--danger')).toBe(true);
    enter(input);
    await nextTick();
    expect(ctrl.deleteAccount).toHaveBeenCalledWith('ada@example.com');
  });
});
