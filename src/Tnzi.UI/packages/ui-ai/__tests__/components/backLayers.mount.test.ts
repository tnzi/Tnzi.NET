/**
 * Which surfaces take the phone's back, when, and what back does to them.
 *
 * The history side (entries, the router, stacking) is `back-layers.test.ts`;
 * here the module is replaced by a recorder, so each test sees the layers a
 * component declares: whether one is active (open and enabled) and what its
 * `onBack` does when called.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, toValue, type App, type MaybeRefOrGetter } from 'vue';

interface Recorded {
  open: MaybeRefOrGetter<boolean>;
  onBack: () => unknown;
  enabled?: MaybeRefOrGetter<boolean>;
}
const recorded: Recorded[] = [];

vi.mock('../../src/headless/back-layers', () => ({
  useBackLayer: (open: MaybeRefOrGetter<boolean>, onBack: () => unknown, options: { enabled?: MaybeRefOrGetter<boolean> } = {}) => {
    recorded.push({ open, onBack, enabled: options.enabled });
    return { open() {}, close() {}, dispose() {}, isOpen: false };
  },
}));

const { default: TDialog } = await import('../../src/components/overlay/TDialog.vue');
const { default: TSettingsDialog } = await import('../../src/components/overlay/TSettingsDialog.vue');
const { default: TCollapsibleSidebar } = await import('../../src/components/layout/TCollapsibleSidebar.vue');
const { default: TAsidePanel } = await import('../../src/components/layout/TAsidePanel.vue');
const { default: TPopoverMenu } = await import('../../src/components/overlay/TPopoverMenu.vue');
const { default: TActionMenu } = await import('../../src/components/overlay/TActionMenu.vue');

let app: App | null = null;

function mount(render: () => unknown): void {
  const host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp(defineComponent({ setup: () => render }));
  app.mount(host);
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

const active = (layer: Recorded): boolean =>
  toValue(layer.open) && (layer.enabled === undefined || toValue(layer.enabled));

afterEach(() => {
  app?.unmount();
  app = null;
  recorded.length = 0;
  document.body.innerHTML = '';
});

describe('TDialog', () => {
  function mountDialog(props: Record<string, unknown>) {
    const open = ref(true);
    mount(() =>
      h(TDialog, { modelValue: open.value, title: 'T', 'onUpdate:modelValue': (v: boolean) => (open.value = v), ...props }),
    );
    return open;
  }

  it('takes back on the sheet and closes through its Escape path', async () => {
    const open = mountDialog({ narrow: true });
    await settle();
    expect(active(recorded[0]!)).toBe(true);
    await recorded[0]!.onBack();
    await settle();
    expect(open.value).toBe(false);
  });

  it('leaves back to the page as a centred dialog', async () => {
    mountDialog({ narrow: false });
    await settle();
    expect(active(recorded[0]!)).toBe(false);
  });

  it('stays open on back when it cannot be closed', async () => {
    for (const props of [{ closable: false }, { closeOnEscape: false }, { busy: true }, { beforeClose: () => false }]) {
      const open = mountDialog({ narrow: true, ...props });
      await settle();
      const layer = recorded[recorded.length - 1]!;
      expect(await layer.onBack()).toBe(false);
      await settle();
      expect(open.value).toBe(true);
      app?.unmount();
      app = null;
    }
  });

  it('can leave back to the page (`closeOnBack: false`)', async () => {
    mountDialog({ narrow: true, closeOnBack: false });
    await settle();
    expect(active(recorded[0]!)).toBe(false);
  });
});

describe('TSettingsDialog', () => {
  const sections = [
    { id: 'a', label: 'A' },
    { id: 'b', label: 'B' },
  ];

  it('takes one back on the sheet: its TDialog closes, whichever section is open', async () => {
    const open = ref(true);
    mount(() =>
      h(TSettingsDialog, { modelValue: open.value, sections, narrow: true, 'onUpdate:modelValue': (v: boolean) => (open.value = v) }),
    );
    await settle();
    // One layer (the TDialog's): the sections are a row on the sheet, not a screen of their own.
    expect(recorded.filter(active)).toHaveLength(1);
    document.querySelector<HTMLElement>('[data-section-id="b"]')!.click();
    await settle();
    expect(recorded.filter(active)).toHaveLength(1);
    await recorded.find(active)!.onBack();
    await settle();
    expect(open.value).toBe(false);
  });

  it('leaves back alone in the two-column dialog and with `closeOnBack: false`', async () => {
    mount(() => h('div', [
      h(TSettingsDialog, { modelValue: true, sections, narrow: false }),
      h(TSettingsDialog, { modelValue: true, sections, narrow: true, closeOnBack: false }),
    ]));
    await settle();
    expect(recorded.every((layer) => !active(layer))).toBe(true);
  });
});

describe('drawer, panel and menus', () => {
  it('the sidebar takes back only as an open drawer', async () => {
    const mode = ref<'expanded' | 'hidden'>('expanded');
    const mobile = ref(false);
    mount(() =>
      h(TCollapsibleSidebar, {
        modelValue: mode.value,
        isMobile: mobile.value,
        'onUpdate:modelValue': (v: 'expanded' | 'hidden') => (mode.value = v),
      }),
    );
    await settle();
    expect(active(recorded[0]!)).toBe(false);
    mobile.value = true;
    await settle();
    expect(active(recorded[0]!)).toBe(true);
    recorded[0]!.onBack();
    await settle();
    expect(mode.value).toBe('hidden');
    expect(active(recorded[0]!)).toBe(false);
  });

  it('the side panel takes back as the narrow layer and closes', async () => {
    const onClose = vi.fn();
    const narrow = ref(true);
    mount(() => h(TAsidePanel, { narrow: narrow.value, onClose }));
    await settle();
    expect(active(recorded[0]!)).toBe(true);
    recorded[0]!.onBack();
    expect(onClose).toHaveBeenCalledTimes(1);
    narrow.value = false;
    await settle();
    expect(active(recorded[0]!)).toBe(false);
  });

  it('the menus take back on a narrow viewport only', async () => {
    const popoverOpen = ref(true);
    const actionOpen = ref(true);
    mount(() => h('div', { style: 'position: relative' }, [
      h(TPopoverMenu, { modelValue: popoverOpen.value, 'onUpdate:modelValue': (v: boolean) => (popoverOpen.value = v) }),
      h(TActionMenu, {
        items: [{ id: 'x', label: 'X' }],
        open: actionOpen.value,
        'onUpdate:open': (v: boolean) => (actionOpen.value = v),
      }, { trigger: () => h('button', { type: 'button' }, '...') }),
    ]));
    await settle();
    const [popover, action] = recorded;
    // happy-dom's window is 1024px wide: a wide screen.
    expect(active(popover!)).toBe(false);
    expect(active(action!)).toBe(false);
    expect(toValue(popover!.open)).toBe(true);

    popover!.onBack();
    action!.onBack();
    await settle();
    expect(popoverOpen.value).toBe(false);
    expect(actionOpen.value).toBe(false);
  });
});
