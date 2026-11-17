/**
 * The dialog layer stack and the menus over it, mounted for real:
 *
 * - depths are as deep as the dialogs open at once, so they never climb into
 *   the popover layer however many dialogs a session opens;
 * - the settings dialog and the command palette join the same stack as
 *   `TDialog`, so whichever opened last is on top;
 * - `TPopoverMenu` is `position: fixed` on the popover layer and stays in the
 *   DOM where it was put, lined up with its anchor and flipped when there is no
 *   room; `teleport` moves it to `<body>`.
 */
import { afterEach, describe, expect, it } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue';

const { acquireDialogDepth, releaseDialogDepth, layerZIndex } = await import('../../src/components/overlay/layers');
const { default: TDialog } = await import('../../src/components/overlay/TDialog.vue');
const { default: TSettingsDialog } = await import('../../src/components/overlay/TSettingsDialog.vue');
const { default: TPopoverMenu } = await import('../../src/components/overlay/TPopoverMenu.vue');

let app: App | null = null;

function mount(render: () => unknown): HTMLElement {
  const host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp(defineComponent({ setup: () => render }));
  app.mount(host);
  return host;
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

const depthOf = (el: HTMLElement) => Number(/\+ (\d+)\)$/.exec(el.style.zIndex)?.[1]);

afterEach(() => {
  app?.unmount();
  app = null;
  document.body.innerHTML = '';
});

describe('dialog depths', () => {
  it('take one more than the deepest held and come back when released', () => {
    const a = acquireDialogDepth();
    const b = acquireDialogDepth();
    expect(b).toBe(a + 1);
    releaseDialogDepth(b);
    releaseDialogDepth(a);
    for (let i = 0; i < 500; i += 1) releaseDialogDepth(acquireDialogDepth());
    expect(acquireDialogDepth()).toBe(1);
    releaseDialogDepth(1);
  });

  it('names the layer token in the z-index', () => {
    expect(layerZIndex('popover')).toBe('var(--tnzi-ai-z-popover, 2000)');
    expect(layerZIndex('dialog', 2)).toBe('calc(var(--tnzi-ai-z-dialog, 1000) + 2)');
  });

  it('puts the settings dialog opened over a TDialog above it, and frees the depth on close', async () => {
    const plain = ref(true);
    const settings = ref(false);
    mount(() => [
      h(TDialog, { modelValue: plain.value, title: 'Plain' }),
      h(TSettingsDialog, { modelValue: settings.value, sections: [{ id: 'a', label: 'A' }] }),
    ]);
    await settle();
    settings.value = true;
    await settle();
    const [dialog, sheet] = [...document.querySelectorAll<HTMLElement>('.t-dialog-host')];
    expect(sheet!.querySelector('.t-settings-dialog')).not.toBeNull();
    expect(depthOf(sheet!)).toBeGreaterThan(depthOf(dialog!));
    settings.value = false;
    plain.value = false;
    await settle();
    const next = acquireDialogDepth();
    expect(next).toBe(1);
    releaseDialogDepth(next);
  });
});

describe('TPopoverMenu', () => {
  function rect(r: Partial<DOMRect>): DOMRect {
    const full = { x: 0, y: 0, top: 0, left: 0, right: 0, bottom: 0, width: 0, height: 0, ...r };
    return { ...full, toJSON: () => full } as DOMRect;
  }

  function mountMenu(props: Record<string, unknown>, anchorBox: Partial<DOMRect>) {
    const open = ref(true);
    const host = mount(() =>
      h('div', { class: 'anchor-box', style: 'position: relative' }, [
        h('button', { class: 'trigger' }, 'Open'),
        h(TPopoverMenu, { modelValue: open.value, 'onUpdate:modelValue': (v: boolean) => (open.value = v), ...props }, () =>
          h('button', { class: 't-popover-menu__item item' }, 'Item'),
        ),
      ]),
    );
    const box = host.querySelector<HTMLElement>('.anchor-box')!;
    box.getBoundingClientRect = () => rect(anchorBox);
    return { host, open, box };
  }

  it('stays in the DOM where it was put, fixed on the popover layer, lined up with its anchor', async () => {
    const { host, box } = mountMenu({ align: 'right' }, { top: 100, bottom: 130, left: 400, right: 600, width: 200, height: 30 });
    const anchor = host.querySelector<HTMLElement>('.t-popover-menu-anchor')!;
    Object.defineProperty(anchor, 'offsetParent', { value: box, configurable: true });
    await settle();
    const menu = host.querySelector<HTMLElement>('.t-popover-menu')!;
    expect(box.contains(menu)).toBe(true);
    // happy-dom has no layout: the menu measures 0x0, so its right edge is the anchor's.
    expect(menu.style.left).toBe('600px');
    expect(menu.style.top).toBe('136px');
    expect(menu.classList.contains('t-popover-menu--placing')).toBe(false);
  });

  it('opens above when there is no room below', async () => {
    const height = window.innerHeight;
    const { host, box } = mountMenu({ align: 'left' }, { top: height - 40, bottom: height - 10, left: 20, right: 120, width: 100, height: 30 });
    Object.defineProperty(host.querySelector('.t-popover-menu-anchor')!, 'offsetParent', { value: box, configurable: true });
    const menuSize = { offsetWidth: 180, offsetHeight: 120 };
    await nextTick();
    const menu = host.querySelector<HTMLElement>('.t-popover-menu')!;
    Object.defineProperty(menu, 'offsetWidth', { value: menuSize.offsetWidth });
    Object.defineProperty(menu, 'offsetHeight', { value: menuSize.offsetHeight });
    // Measured again on resize, now with a size to measure.
    window.dispatchEvent(new Event('resize'));
    await new Promise((resolve) => setTimeout(resolve, 40));
    expect(Number.parseFloat(menu.style.top)).toBe(height - 40 - 6 - 120);
    expect(menu.style.left).toBe('20px');
  });

  it('closes on a click outside, and moves to <body> with teleport', async () => {
    const { host, open } = mountMenu({ teleport: true }, { top: 10, bottom: 40, left: 10, right: 210 });
    await settle();
    const menu = document.querySelector<HTMLElement>('.t-popover-menu')!;
    expect(host.contains(menu)).toBe(false);
    expect(menu.parentElement).toBe(document.body);
    await new Promise((resolve) => setTimeout(resolve, 0));
    document.body.click();
    await settle();
    expect(open.value).toBe(false);
  });
});
