/**
 * `TSettingsDialog`, mounted for real:
 *
 * - it is a `TDialog` (900 wide, min(740px, 90vh) high, flush body, full screen
 *   on a phone) whose title is its accessible name; the open section's name is
 *   a heading over the pane, `heading` replaces it and `#lead` goes above it;
 * - the sections are listed in groups set apart, with no group headings; a
 *   badge shows only above 0 and carries its screen reader words;
 * - below `narrowBreakpoint` (or with `narrow`) the sections run along the top
 *   of a full-screen sheet and the open section is shown under them;
 * - focus opens on the panel, never on the filter box or a field, and goes
 *   back to the opener on close; Escape closes; `beforeClose` can refuse;
 * - the account and the filter box stay optional;
 * - a dialog a section opens on top of it gets Escape and Tab first;
 * - `TChatApp` opens it on Account from the account menu.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, Teleport, type App, type Component } from 'vue';
import { useFocusTrap } from '@tnzi/ui/headless';

const { default: TSettingsDialog } = await import('../../src/components/overlay/TSettingsDialog.vue');
const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');
const { createAiI18n } = await import('../../src/i18n/index');
const { en } = await import('../../src/locales/en');
const { zhCn } = await import('../../src/locales/zh-cn');

const sections = [
  { id: 'account', label: 'Account', icon: 'lucide:user', group: 1 },
  { id: 'passes', label: 'Passes', icon: 'lucide:key', group: 2, badge: 2, badgeLabel: '2 need your attention' },
  { id: 'about', label: 'About', group: 1 },
  { id: 'quiet', label: 'Quiet', group: 2, badge: 0 },
];

let app: App | null = null;
let host: HTMLElement | null = null;
let trigger: HTMLButtonElement | null = null;
const initialWidth = window.innerWidth;

function resizeTo(width: number): void {
  Object.defineProperty(window, 'innerWidth', { value: width, configurable: true, writable: true });
  window.dispatchEvent(new Event('resize'));
}

function mount(component: Component, props: () => Record<string, unknown>, slots: Record<string, (p: never) => unknown> = {}, messages?: typeof en): void {
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
const afterLeave = () => new Promise((resolve) => setTimeout(resolve, 50));

const q = <T extends Element = HTMLElement>(selector: string) => document.querySelector<T>(selector);
const items = () => [...document.querySelectorAll<HTMLButtonElement>('.t-settings__item')];
const item = (id: string) => q<HTMLButtonElement>(`[data-section-id="${id}"]`)!;
const escape = () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));

const slots = {
  account: () => h('p', { class: 'probe' }, 'account pane'),
  passes: () => h('p', { class: 'probe' }, 'passes pane'),
  about: () => h('p', { class: 'probe' }, 'about pane'),
};

afterEach(() => {
  app?.unmount();
  host?.remove();
  trigger?.remove();
  app = null;
  host = null;
  trigger = null;
  document.body.innerHTML = '';
  document.body.style.overflow = '';
  resizeTo(initialWidth);
});

describe('TSettingsDialog layout', () => {
  it('is a TDialog named by its title, 900 wide and min(740px, 90vh) high, with a flush body', async () => {
    resizeTo(1024);
    mount(TSettingsDialog, () => ({ modelValue: true, sections }), slots);
    await settle();
    const dialog = q('[role="dialog"]')!;
    expect(dialog.classList.contains('t-dialog')).toBe(true);
    expect(dialog.classList.contains('t-settings-dialog')).toBe(true);
    expect(document.getElementById(dialog.getAttribute('aria-labelledby')!)?.textContent).toBe(en.sidebar.settings);
    expect(dialog.style.getPropertyValue('--t-dialog-width')).toBe('900px');
    expect(dialog.style.getPropertyValue('--t-dialog-height')).toBe('min(740px, 90vh)');
    expect(q('.t-dialog__body')?.classList.contains('t-dialog__body--flush')).toBe(true);
    expect(q('.t-dialog__close')).not.toBeNull();
    expect(q('.t-settings--sheet')).toBeNull();
  });

  it('lists the sections in groups set apart, with no group headings', async () => {
    resizeTo(1024);
    mount(TSettingsDialog, () => ({ modelValue: true, sections }), slots);
    await settle();
    const groups = [...document.querySelectorAll('.t-settings__group')];
    expect(groups.map((g) => [...g.querySelectorAll('[data-section-id]')].map((b) => b.getAttribute('data-section-id')))).toEqual([
      ['account', 'about'],
      ['passes', 'quiet'],
    ]);
    expect(groups.every((g) => g.children.length === g.querySelectorAll('button').length)).toBe(true);
    expect(q('.t-settings__nav')?.getAttribute('aria-label')).toBe(en.settings.sections);
  });

  it('marks the open section, shows its pane under its name, and moves on a click', async () => {
    resizeTo(1024);
    mount(TSettingsDialog, () => ({ modelValue: true, sections }), slots);
    await settle();
    expect(item('account').getAttribute('aria-current')).toBe('page');
    expect(item('about').getAttribute('aria-current')).toBeNull();
    expect(q('[data-section-heading]')?.tagName).toBe('H3');
    expect(q('[data-section-heading]')?.textContent).toBe('Account');
    expect(q('.t-settings__content .probe')?.textContent).toBe('account pane');

    item('passes').click();
    await settle();
    expect(item('passes').getAttribute('aria-current')).toBe('page');
    expect(q('[data-section-heading]')?.textContent).toBe('Passes');
    expect(q('.t-settings__content .probe')?.textContent).toBe('passes pane');
  });

  it('emits update:activeSection and follows the prop', async () => {
    resizeTo(1024);
    const active = ref('account');
    const onActive = vi.fn((id: string) => (active.value = id));
    mount(TSettingsDialog, () => ({ modelValue: true, sections, activeSection: active.value, 'onUpdate:activeSection': onActive }), slots);
    await settle();
    item('about').click();
    await settle();
    expect(onActive).toHaveBeenCalledWith('about');
    active.value = 'passes';
    await settle();
    expect(q('.t-settings__content .probe')?.textContent).toBe('passes pane');
  });

  it('shows a badge above 0 only, with its screen reader words', async () => {
    resizeTo(1024);
    mount(TSettingsDialog, () => ({ modelValue: true, sections }), slots);
    await settle();
    const badge = item('passes').querySelector('[data-section-badge]')!;
    expect(badge.querySelector('[aria-hidden="true"]')?.textContent).toBe('2');
    expect(badge.querySelector('.sr-only')?.textContent).toBe('2 need your attention');
    expect(item('quiet').querySelector('[data-section-badge]')).toBeNull();
    expect(item('account').querySelector('[data-section-badge]')).toBeNull();
  });

  it('takes a heading of its own and something above it', async () => {
    resizeTo(1024);
    mount(TSettingsDialog, () => ({ modelValue: true, sections, heading: 'Your account' }), {
      ...slots,
      lead: () => h('div', { class: 'lead' }, 'Signed in as Ada'),
    });
    await settle();
    const content = q('.t-settings__content')!;
    expect(content.querySelector('[data-section-heading]')?.textContent).toBe('Your account');
    expect(content.firstElementChild?.classList.contains('lead')).toBe(true);
  });

  it('is a full-screen sheet with the sections along the top below the breakpoint', async () => {
    resizeTo(375);
    mount(TSettingsDialog, () => ({ modelValue: true, sections, activeSection: 'about' }), slots);
    await settle();
    expect(q('.t-settings--sheet')).not.toBeNull();
    expect(q('.t-dialog-host--fullscreen')).not.toBeNull();
    // Both at once: the row of sections and the open one under it.
    expect(q('.t-settings__nav')).not.toBeNull();
    expect(q('.t-settings__content .probe')?.textContent).toBe('about pane');
    expect(q('.t-dialog__close')).not.toBeNull();
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
    expect(q('.t-dialog-host--fullscreen')).not.toBeNull();
  });

  it('names the dialog and the section list in the current language', async () => {
    resizeTo(1024);
    mount(TSettingsDialog, () => ({ modelValue: true, sections }), slots, zhCn);
    await settle();
    const dialog = q('[role="dialog"]')!;
    expect(document.getElementById(dialog.getAttribute('aria-labelledby')!)?.textContent).toBe(zhCn.sidebar.settings);
    expect(q('.t-settings__nav')?.getAttribute('aria-label')).toBe(zhCn.settings.sections);
    expect(q('.t-dialog__close')?.getAttribute('aria-label')).toBe(zhCn.common.close);
  });
});

describe('TSettingsDialog content slots', () => {
  async function type(input: HTMLInputElement, value: string): Promise<void> {
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await settle();
  }

  it('keeps a default slot mounted across sections: unsaved input and its node survive a switch', async () => {
    resizeTo(1024);
    const seen: Array<{ activeSection: string; section: { id: string } | null }> = [];
    mount(TSettingsDialog, () => ({ modelValue: true, sections }), {
      default: (props: never) => {
        const { activeSection, section } = props as { activeSection: string; section: { id: string } | null };
        seen.push({ activeSection, section });
        return [
          h('div', { class: 'pane-account', style: activeSection === 'account' ? '' : 'display: none' }, [h('input', { class: 'draft' })]),
          h('div', { class: 'pane-about', style: activeSection === 'about' ? '' : 'display: none' }, 'about pane'),
        ];
      },
      // Not rendered while a default slot is given.
      account: () => h('p', { class: 'named' }, 'named account pane'),
    });
    await settle();
    expect(q('.named')).toBeNull();
    const input = q<HTMLInputElement>('.draft')!;
    await type(input, 'unsaved');

    item('about').click();
    await settle();
    expect(seen.at(-1)?.activeSection).toBe('about');
    expect(seen.at(-1)?.section?.id).toBe('about');
    expect(q<HTMLElement>('.pane-account')!.style.display).toBe('none');

    item('account').click();
    await settle();
    expect(q('.draft')).toBe(input);
    expect(q<HTMLInputElement>('.draft')!.value).toBe('unsaved');
  });

  it('with named slots, mounts only the open section and rebuilds it on return', async () => {
    resizeTo(1024);
    mount(TSettingsDialog, () => ({ modelValue: true, sections }), {
      ...slots,
      account: () => h('input', { class: 'draft' }),
    });
    await settle();
    const first = q<HTMLInputElement>('.draft')!;
    await type(first, 'unsaved');
    item('about').click();
    await settle();
    expect(q('.draft')).toBeNull();
    expect(q('.t-settings__content .probe')?.textContent).toBe('about pane');
    item('account').click();
    await settle();
    const again = q<HTMLInputElement>('.draft')!;
    expect(again).not.toBe(first);
    expect(again.value).toBe('');
  });
});

describe('TSettingsDialog flush', () => {
  it('is off by default', async () => {
    resizeTo(1024);
    mount(TSettingsDialog, () => ({ modelValue: true, sections }), slots);
    await settle();
    expect(q('.t-settings')?.classList.contains('t-settings--flush')).toBe(false);
  });

  it('puts the content in a body of its own under the lead and the section name, on a wide screen and the sheet', async () => {
    for (const width of [1024, 375]) {
      resizeTo(width);
      mount(TSettingsDialog, () => ({ modelValue: true, sections, flush: true }), {
        lead: () => h('div', { class: 'lead' }, 'lead'),
        account: () => h('div', { class: 'panel' }, [h('div', { class: 'panel-scroll' }), h('footer', { class: 'panel-actions' })]),
      });
      await settle();
      const root = q('.t-settings')!;
      expect(root.classList.contains('t-settings--flush')).toBe(true);
      expect(root.classList.contains('t-settings--sheet')).toBe(width < 768);
      const content = q('.t-settings__content')!;
      expect([...content.children].map((el) => el.className)).toEqual(['lead', 't-settings__heading', 't-settings__body']);
      expect(q('.t-settings__body > .panel > .panel-actions')).not.toBeNull();
      app?.unmount();
      host?.remove();
      document.body.innerHTML = '';
    }
  });
});

describe('TSettingsDialog keyboard and focus', () => {
  it('focuses the panel on open (not the filter box, a field or an entry) and gives focus back to the opener on Escape', async () => {
    resizeTo(1024);
    trigger = document.createElement('button');
    document.body.appendChild(trigger);
    trigger.focus();
    const open = ref(false);
    mount(TSettingsDialog, () => ({
      modelValue: open.value,
      sections,
      activeSection: 'about',
      searchable: true,
      'onUpdate:modelValue': (v: boolean) => (open.value = v),
    }), { ...slots, about: () => h('input', { class: 'field' }) });
    await settle();
    open.value = true;
    await settle();
    expect(document.activeElement).toBe(q('.t-settings-dialog'));

    escape();
    await settle();
    expect(open.value).toBe(false);
    await afterLeave();
    await settle();
    expect(document.activeElement).toBe(trigger);
  });

  it('asks beforeClose, and stays open when it says no', async () => {
    resizeTo(1024);
    const open = ref(true);
    const beforeClose = vi.fn(() => false);
    mount(TSettingsDialog, () => ({ modelValue: open.value, sections, beforeClose, 'onUpdate:modelValue': (v: boolean) => (open.value = v) }), slots);
    await settle();
    escape();
    await settle();
    expect(beforeClose).toHaveBeenCalledWith('escape');
    expect(open.value).toBe(true);
    q<HTMLButtonElement>('.t-dialog__close')!.click();
    await settle();
    expect(beforeClose).toHaveBeenCalledWith('close-button');
    expect(open.value).toBe(true);
  });
});

describe('TSettingsDialog account and filter', () => {
  it('shows neither by default', async () => {
    resizeTo(1024);
    mount(TSettingsDialog, () => ({ modelValue: true, sections }), slots);
    await settle();
    expect(q('.t-settings__account')).toBeNull();
    expect(q('.t-settings__search')).toBeNull();
  });

  it('shows the account with its switcher, and filters the sections', async () => {
    resizeTo(1024);
    const onSwitch = vi.fn();
    mount(TSettingsDialog, () => ({
      modelValue: true,
      sections,
      showAccount: true,
      accountName: 'Ada',
      accountSubtitle: 'ada@example.com',
      switchable: true,
      searchable: true,
      onSwitchAccount: onSwitch,
    }), slots);
    await settle();
    expect(q('.t-settings__account-name')?.textContent).toBe('Ada');
    expect(q('.t-settings__account-sub')?.textContent).toBe('ada@example.com');
    const switcher = q<HTMLButtonElement>('.t-settings__account-switch')!;
    expect(switcher.getAttribute('aria-label')).toBe(en.account.switchAccount);
    switcher.click();
    expect(onSwitch).toHaveBeenCalled();

    const filter = q<HTMLInputElement>('.t-settings__search-input')!;
    expect(filter.getAttribute('aria-label')).toBe(en.settings.searchAria);
    filter.value = 'pas';
    filter.dispatchEvent(new Event('input'));
    await settle();
    expect(items().map((b) => b.dataset.sectionId)).toEqual(['passes']);
    filter.value = 'zzz';
    filter.dispatchEvent(new Event('input'));
    await settle();
    expect(items()).toHaveLength(0);
    expect(q('.t-settings__nav-empty')?.textContent).toBe(en.settings.noResults);
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

    const last = q<HTMLButtonElement>('.consumer-last')!;
    last.focus();
    last.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(q('.consumer-first'));

    escape();
    await settle();
    expect(q('.consumer-dialog')).toBeNull();
    expect(q('.t-settings')).not.toBeNull();
    expect(onUpdate).not.toHaveBeenCalled();

    escape();
    await settle();
    expect(onUpdate).toHaveBeenCalledWith(false);
  });
});

describe('TChatApp settings', () => {
  async function pick(label: string): Promise<void> {
    q<HTMLButtonElement>('.t-user-menu__trigger')!.click();
    await settle();
    ([...document.querySelectorAll<HTMLButtonElement>('.t-popover-menu__item')].find(
      (b) => b.textContent?.trim() === label,
    ))!.click();
    await settle();
  }

  it('opens the phone sheet on Account from the account menu, with the sections along the top', async () => {
    mount(TChatApp, () => ({ sidebarStorageKey: null, autoApplyTheme: false, showAccountBar: true, isMobile: true }));
    await settle();
    await pick(en.account.accountLabel);
    expect(q('.t-settings--sheet')).not.toBeNull();
    expect(q('.t-settings__nav')).not.toBeNull();
    expect(q('[data-section-heading]')?.textContent).toBe(en.account.accountLabel);
    expect(q('.t-settings__item[aria-current="page"]')?.getAttribute('data-section-id')).toBe('account');
  });
});
