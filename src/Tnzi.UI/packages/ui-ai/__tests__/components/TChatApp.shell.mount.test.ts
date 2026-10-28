/**
 * `TChatApp` behaviour that only shows once the shell is mounted:
 *
 * - its user-visible defaults come from the dictionary, so a product that
 *   provides `createAiI18n(zhCn)` does not get an English sidebar, landing
 *   page, composer and settings dialog;
 * - the built-in Account entry in the account menu opens the settings dialog
 *   on its Account section (it used to close the menu and do nothing);
 * - the landing composer refuses a second send while the first is still
 *   being read or uploaded.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, type App } from 'vue';

const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');
const { createAiI18n } = await import('../../src/i18n/index');
const { zhCn } = await import('../../src/locales/zh-cn');
const { en } = await import('../../src/locales/en');

let app: App | null = null;
let host: HTMLElement | null = null;

function mountShell(props: Record<string, unknown>, messages?: typeof en): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  const Root = defineComponent({
    setup() {
      if (messages) createAiI18n(messages);
      return () => h(TChatApp, { sidebarStorageKey: null, autoApplyTheme: false, ...props });
    },
  });
  app = createApp(Root);
  app.mount(host);
  return host;
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
});

describe('TChatApp defaults follow the dictionary', () => {
  it('renders the Chinese copy when the Chinese dictionary is provided', async () => {
    const el = mountShell({}, zhCn);
    await settle();

    expect(el.querySelector('.t-landing__headline')?.textContent?.trim()).toBe(zhCn.landing.greeting);
    expect(el.querySelector('.t-landing textarea')?.getAttribute('placeholder')).toBe(zhCn.landing.placeholder);
    expect(el.querySelector('.t-thread-list__head span')?.textContent?.trim()).toBe(zhCn.chat.threadsLabel);
  });

  it('keeps the English copy without a dictionary', async () => {
    const el = mountShell({});
    await settle();

    expect(el.querySelector('.t-landing__headline')?.textContent?.trim()).toBe('What can I do for you?');
    expect(el.querySelector('.t-thread-list__head span')?.textContent?.trim()).toBe('All tasks');
  });

  it('still lets a prop override the dictionary', async () => {
    const el = mountShell({ landingGreeting: 'Hi there', threadsLabel: 'History' }, zhCn);
    await settle();

    expect(el.querySelector('.t-landing__headline')?.textContent?.trim()).toBe('Hi there');
    expect(el.querySelector('.t-thread-list__head span')?.textContent?.trim()).toBe('History');
  });

  it('names the built-in settings sections in the current locale', async () => {
    const el = mountShell({ showAccountBar: true }, zhCn);
    await settle();
    (el.querySelector('.t-user-menu__trigger') as HTMLButtonElement).click();
    await settle();
    const settingsItem = [...el.querySelectorAll('.t-popover-menu__item')].find(
      (b) => b.textContent?.trim() === zhCn.sidebar.settings,
    ) as HTMLButtonElement;
    settingsItem.click();
    await settle();

    const navLabels = [...document.querySelectorAll('.t-settings__nav-item')].map((b) => b.textContent?.trim());
    expect(navLabels).toEqual([zhCn.account.accountLabel, zhCn.settings.appearance, zhCn.settings.about]);
  });
});

describe('TChatApp account menu', () => {
  async function pick(el: HTMLElement, label: string): Promise<void> {
    (el.querySelector('.t-user-menu__trigger') as HTMLButtonElement).click();
    await settle();
    const item = [...el.querySelectorAll('.t-popover-menu__item')].find(
      (b) => b.textContent?.trim() === label,
    ) as HTMLButtonElement;
    item.click();
    await settle();
  }

  it('opens the settings dialog on the Account section from the Account entry', async () => {
    const onUserMenu = vi.fn();
    const onOpenSettings = vi.fn();
    const el = mountShell({
      showAccountBar: true,
      accountName: 'Ada',
      'onUser-menu': onUserMenu,
      'onOpen-settings': onOpenSettings,
    });
    await settle();

    // Land on another section first, so the test sees the dialog move.
    await pick(el, en.sidebar.settings);
    const about = [...document.querySelectorAll('.t-settings__nav-item')].find(
      (b) => b.textContent?.trim() === en.settings.about,
    ) as HTMLButtonElement;
    about.click();
    await settle();
    (document.querySelector('.t-settings__close') as HTMLButtonElement).click();
    await settle();

    await pick(el, en.account.accountLabel);

    expect(document.querySelector('.t-settings')).not.toBeNull();
    expect(document.querySelector('.t-settings__nav-item--active')?.textContent?.trim()).toBe(
      en.account.accountLabel,
    );
    expect(onUserMenu).toHaveBeenLastCalledWith('account');
    expect(onOpenSettings).toHaveBeenCalledTimes(2);
  });

  it('leaves the Account entry to the product when the built-in dialog is off', async () => {
    const onUserMenu = vi.fn();
    const onOpenSettings = vi.fn();
    const el = mountShell({
      showAccountBar: true,
      enableSettings: false,
      'onUser-menu': onUserMenu,
      'onOpen-settings': onOpenSettings,
    });
    await settle();

    await pick(el, en.account.accountLabel);

    expect(onUserMenu).toHaveBeenCalledWith('account');
    expect(onOpenSettings).not.toHaveBeenCalled();
    expect(document.querySelector('.t-settings')).toBeNull();
  });
});

describe('TChatApp landing composer', () => {
  it('does not send again while the first send is still in flight', async () => {
    const onSend = vi.fn();
    const el = mountShell({ isStreaming: true, inputText: 'second try', onSend });
    await settle();

    const textarea = el.querySelector('.t-landing textarea') as HTMLTextAreaElement;
    textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    await settle();

    expect(onSend).not.toHaveBeenCalled();
  });
});
