/**
 * `TSettingsDialog` on a narrow screen and under other overlays, mounted for real:
 *
 * - below `narrowBreakpoint` (or with `narrow`) it is a full-screen sheet whose
 *   section list and selected section are two screens, with the close button in
 *   the sheet's own header; above it the two-column dialog is unchanged;
 * - the section list's accessible name comes from the dictionary;
 * - a dialog a section opens on top of it (a consumer's, through `useFocusTrap`)
 *   gets Escape first: one Escape closes that dialog, not both;
 * - `TChatApp` hands it the same narrow signal as its sidebar drawer, and a plain
 *   "Settings" opens the sheet on the list while "Account" opens on Account.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, Teleport, type App, type Component } from 'vue';
import { useFocusTrap } from '@tnzi/ui';

const { default: TSettingsDialog } = await import('../../src/components/overlay/TSettingsDialog.vue');
const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');
const { createAiI18n } = await import('../../src/i18n/index');
const { en } = await import('../../src/locales/en');
const { zhCn } = await import('../../src/locales/zh-cn');

const sections = [
  { id: 'account', label: 'Account' },
  { id: 'passes', label: 'Passes', group: 'Access' },
  { id: 'about', label: 'About' },
];

let app: App | null = null;
let host: HTMLElement | null = null;
const initialWidth = window.innerWidth;

function resizeTo(width: number): void {
  Object.defineProperty(window, 'innerWidth', { value: width, configurable: true, writable: true });
  window.dispatchEvent(new Event('resize'));
}

function mount(component: Component, props: () => Record<string, unknown>, slots: Record<string, () => unknown> = {}, messages?: typeof en): void {
  host = document.createElement('div');
  document.body.appendChild(host);
  const Root = defineComponent({
    setup() {
      if (messages) createAiI18n(messages);
      return () => h(component, props(), slots);
    },
  });
  app = createApp(Root);
  app.mount(host);
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

const q = <T extends Element = HTMLElement>(selector: string) => document.querySelector<T>(selector);
const navItem = (label: string) =>
  [...document.querySelectorAll<HTMLButtonElement>('.t-settings__nav-item')].find((b) => b.textContent?.trim() === label)!;

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
  resizeTo(initialWidth);
});

describe('TSettingsDialog layout', () => {
  const slots = {
    account: () => h('p', { class: 'probe' }, 'account pane'),
    passes: () => h('p', { class: 'probe' }, 'passes pane'),
    about: () => h('p', { class: 'probe' }, 'about pane'),
  };

  it('is the two-column dialog at or above the breakpoint', async () => {
    resizeTo(1024);
    mount(TSettingsDialog, () => ({ modelValue: true, sections }), slots);
    await settle();
    expect(q('.t-settings--sheet')).toBeNull();
    expect(q('.t-settings__nav')).not.toBeNull();
    expect(q('.t-settings__content .probe')?.textContent).toBe('account pane');
    expect(q('.t-settings__sheet-head')).toBeNull();
  });

  it('is a sheet that opens on the section list below the breakpoint', async () => {
    resizeTo(375);
    mount(TSettingsDialog, () => ({ modelValue: true, sections }), slots);
    await settle();
    expect(q('.t-settings--sheet')).not.toBeNull();
    expect(q('.t-settings-backdrop--sheet')).not.toBeNull();
    expect(q('.t-settings__nav')).not.toBeNull();
    expect(q('.t-settings__content')).toBeNull();
    // The close button is in the sheet's header, not floating over the list.
    expect(q('.t-settings__sheet-head .t-settings__close')).not.toBeNull();
    expect(q('.t-settings__sheet-title')?.textContent).toBe(en.sidebar.settings);
  });

  it('moves from the list to a section and back, with focus following', async () => {
    resizeTo(375);
    mount(TSettingsDialog, () => ({ modelValue: true, sections }), slots);
    await settle();

    navItem('Passes').click();
    await settle();
    expect(q('.t-settings__nav')).toBeNull();
    expect(q('.t-settings__content .probe')?.textContent).toBe('passes pane');
    expect(q('.t-settings__sheet-title')?.textContent).toBe('Passes');
    // The sheet's header names the section; the pane does not repeat it.
    expect(q('.t-settings__content-title')).toBeNull();
    const back = q<HTMLButtonElement>('.t-settings__back')!;
    expect(back.getAttribute('aria-label')).toBe(en.settings.backToSections);
    expect(document.activeElement).toBe(back);

    back.click();
    await settle();
    expect(q('.t-settings__content')).toBeNull();
    expect(document.activeElement).toBe(navItem('Passes'));
  });

  it('opens on the section it was asked for', async () => {
    resizeTo(375);
    mount(TSettingsDialog, () => ({ modelValue: true, activeSection: 'about', sections }), slots);
    await settle();
    expect(q('.t-settings__content .probe')?.textContent).toBe('about pane');
    expect(q('.t-settings__back')).not.toBeNull();
  });

  it('takes its breakpoint from `narrowBreakpoint`, and `narrow` overrides the viewport', async () => {
    resizeTo(720);
    const narrow = ref<boolean | undefined>(undefined);
    mount(TSettingsDialog, () => ({ modelValue: true, sections, narrowBreakpoint: 640, narrow: narrow.value }), slots);
    await settle();
    expect(q('.t-settings--sheet')).toBeNull();

    narrow.value = true;
    await settle();
    expect(q('.t-settings--sheet')).not.toBeNull();
  });

  it('switches layout when the viewport crosses the breakpoint while open', async () => {
    resizeTo(1024);
    mount(TSettingsDialog, () => ({ modelValue: true, sections }), slots);
    await settle();
    resizeTo(600);
    await settle();
    expect(q('.t-settings--sheet')).not.toBeNull();
    resizeTo(1024);
    await settle();
    expect(q('.t-settings--sheet')).toBeNull();
    expect(q('.t-settings__content .probe')).not.toBeNull();
  });

  it('names the section list in the current language', async () => {
    resizeTo(1024);
    mount(TSettingsDialog, () => ({ modelValue: true, sections }), slots, zhCn);
    await settle();
    expect(q('.t-settings__nav')?.getAttribute('aria-label')).toBe(zhCn.settings.sections);
  });
});

describe('TSettingsDialog under another overlay', () => {
  /** A consumer's dialog, teleported to body, opened from a settings section. */
  const ConsumerDialog = defineComponent({
    props: { open: { type: Boolean, required: true } },
    emits: ['close'],
    setup(props, { emit }) {
      const panel = ref<HTMLElement | null>(null);
      useFocusTrap(panel, () => props.open, { onEscape: () => emit('close') });
      return () =>
        props.open
          ? h(Teleport, { to: 'body' }, [
              h('div', { ref: panel, class: 'consumer-dialog', role: 'dialog', 'aria-modal': 'true' }, [
                h('button', { class: 'consumer-first' }, 'first'),
                h('button', { class: 'consumer-last' }, 'last'),
              ]),
            ])
          : null;
    },
  });

  it('lets a dialog opened above it take Escape and Tab first', async () => {
    resizeTo(1024);
    const settingsOpen = ref(true);
    const passOpen = ref(false);
    const onUpdate = vi.fn((v: boolean) => (settingsOpen.value = v));
    mount(
      TSettingsDialog,
      () => ({ modelValue: settingsOpen.value, sections, 'onUpdate:modelValue': onUpdate }),
      {
        account: () => [
          h('button', { class: 'open-pass', onClick: () => (passOpen.value = true) }, 'Create pass'),
          h(ConsumerDialog, { open: passOpen.value, onClose: () => (passOpen.value = false) }),
        ],
      },
    );
    await settle();
    q<HTMLButtonElement>('.open-pass')!.click();
    await settle();

    // Tab inside the consumer dialog wraps round inside it.
    const last = q<HTMLButtonElement>('.consumer-last')!;
    last.focus();
    last.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(q('.consumer-first'));

    // One Escape closes the consumer dialog only.
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();
    expect(q('.consumer-dialog')).toBeNull();
    expect(q('.t-settings')).not.toBeNull();
    expect(onUpdate).not.toHaveBeenCalled();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();
    expect(onUpdate).toHaveBeenCalledWith(false);
  });
});

describe('TChatApp settings on a narrow screen', () => {
  async function pick(label: string): Promise<void> {
    q<HTMLButtonElement>('.t-user-menu__trigger')!.click();
    await settle();
    ([...document.querySelectorAll<HTMLButtonElement>('.t-popover-menu__item')].find(
      (b) => b.textContent?.trim() === label,
    ))!.click();
    await settle();
  }

  it('opens the sheet on the list for Settings and on Account for Account', async () => {
    mount(TChatApp, () => ({ sidebarStorageKey: null, autoApplyTheme: false, showAccountBar: true, isMobile: true }));
    await settle();

    await pick(en.sidebar.settings);
    expect(q('.t-settings--sheet')).not.toBeNull();
    expect(q('.t-settings__nav')).not.toBeNull();
    expect(q('.t-settings__content')).toBeNull();

    q<HTMLButtonElement>('.t-settings__close')!.click();
    await settle();
    await pick(en.account.accountLabel);
    expect(q('.t-settings__sheet-title')?.textContent).toBe(en.account.accountLabel);
    expect(q('.t-settings__content')).not.toBeNull();
  });
});
