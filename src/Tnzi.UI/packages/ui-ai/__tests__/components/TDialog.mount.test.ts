/**
 * `TDialog`, mounted for real: its accessible structure, where focus goes on
 * open and close, the three ways to ask it to close and the switches that hold
 * them off, stacking of a dialog opened from a dialog, the page scroll lock, the
 * narrow layouts and the escape hatches (flush body, own header, own width,
 * full screen, no close button).
 *
 * happy-dom does no layout, so geometry is not checked here; the declared
 * styles are gated in `conventions/overlay-and-setting-row-layout.test.ts`.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App, type Ref } from 'vue';

const { default: TDialog } = await import('../../src/components/overlay/TDialog.vue');
const { en } = await import('../../src/locales/en');

let app: App | null = null;
let host: HTMLElement | null = null;
let trigger: HTMLButtonElement | null = null;

interface Mounted {
  open: Ref<boolean>;
  closed: ReturnType<typeof vi.fn>;
}

/** Mounts a TDialog bound to `open` (v-model), after focusing a trigger button. */
function mount(props: Record<string, unknown> = {}, slots: Record<string, (p: never) => unknown> = {}, initiallyOpen = false): Mounted {
  trigger = document.createElement('button');
  trigger.textContent = 'open';
  document.body.appendChild(trigger);
  trigger.focus();
  host = document.createElement('div');
  document.body.appendChild(host);
  const open = ref(initiallyOpen);
  const closed = vi.fn();
  const Root = defineComponent({
    setup() {
      return () =>
        h(
          TDialog,
          {
            modelValue: open.value,
            'onUpdate:modelValue': (value: boolean) => (open.value = value),
            onClosed: closed,
            title: 'Rename chat',
            ...props,
          },
          slots,
        );
    },
  });
  app = createApp(Root);
  app.mount(host);
  return { open, closed };
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

/** Long enough for a leave transition without CSS to finish in happy-dom. */
const afterLeave = () => new Promise((resolve) => setTimeout(resolve, 50));

const q = <T extends Element = HTMLElement>(selector: string) => document.querySelector<T>(selector);
const panel = () => q('.t-dialog')!;
const escape = () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));

function clickBackdrop(startOnPanel = false): void {
  const backdrop = q('.t-dialog-host')!;
  (startOnPanel ? panel() : backdrop).dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
  backdrop.dispatchEvent(new MouseEvent('click', { bubbles: true }));
}

afterEach(() => {
  app?.unmount();
  host?.remove();
  trigger?.remove();
  app = null;
  host = null;
  trigger = null;
  document.body.innerHTML = '';
  document.body.style.overflow = '';
});

describe('TDialog structure', () => {
  it('is a labelled modal dialog with a title, a description and a close button', async () => {
    const { open } = mount({ description: 'Pick a short name.' }, { default: () => h('p', 'body') });
    open.value = true;
    await settle();
    const dialog = q('[role="dialog"]')!;
    expect(dialog.getAttribute('aria-modal')).toBe('true');
    expect(document.getElementById(dialog.getAttribute('aria-labelledby')!)?.textContent).toBe('Rename chat');
    expect(document.getElementById(dialog.getAttribute('aria-describedby')!)?.textContent).toBe('Pick a short name.');
    expect(q('.t-dialog__close')?.getAttribute('aria-label')).toBe(en.common.close);
    expect(q('.t-dialog__body')?.classList.contains('t-ai-scroll')).toBe(true);
    expect(q('.t-dialog-host')?.classList.contains('t-dialog-backdrop')).toBe(true);
    expect(q('.t-dialog-host')?.classList.contains('t-ai-scroll-scope')).toBe(true);
    expect(q('.t-dialog__footer')).toBeNull();
  });

  it('renders nothing while closed', async () => {
    mount();
    await settle();
    expect(q('.t-dialog-host')).toBeNull();
  });

  it('takes role alertdialog and an aria-label when there is no title', async () => {
    mount({ title: undefined, ariaLabel: 'Confirm', role: 'alertdialog' }, {}, true);
    await settle();
    const dialog = q('[role="alertdialog"]')!;
    expect(dialog.getAttribute('aria-labelledby')).toBeNull();
    expect(dialog.getAttribute('aria-label')).toBe('Confirm');
  });

  it('puts the footer actions in the footer and hands every slot `close`', async () => {
    const { open } = mount({}, {
      default: ({ close }: { close: () => void }) => h('button', { class: 'in-body', onClick: close }, 'x'),
      footer: ({ close }: { close: () => void }) => [h('button', { class: 'cancel', onClick: close }, 'Cancel'), h('button', { class: 'save' }, 'Save')],
    } as never, true);
    await settle();
    const footer = q('.t-dialog__footer')!;
    expect([...footer.querySelectorAll('button')].map((b) => b.textContent)).toEqual(['Cancel', 'Save']);
    q<HTMLButtonElement>('.cancel')!.click();
    expect(open.value).toBe(false);
  });
});

describe('TDialog focus', () => {
  it('focuses the first field, not the close button', async () => {
    const { open } = mount({}, { default: () => [h('button', { class: 'b' }, 'b'), h('input', { class: 'field' })] });
    open.value = true;
    await settle();
    expect(document.activeElement).toBe(q('.field'));
  });

  it('skips fields that cannot take focus as drawn, and inputs that are not typed into', async () => {
    const { open } = mount({}, {
      default: () => [
        h('input', { type: 'file', class: 'file', style: 'display: none' }),
        h('input', { type: 'checkbox', class: 'check' }),
        h('div', { style: 'display: none' }, [h('input', { class: 'in-hidden-box' })]),
        h('input', { class: 'hidden-attr', hidden: true }),
        h('div', { inert: '' }, [h('input', { class: 'inert' })]),
        h('textarea', { class: 'notes' }),
      ],
    });
    open.value = true;
    await settle();
    expect(document.activeElement).toBe(q('.notes'));
  });

  it('falls back to the panel when the only field is a hidden file input', async () => {
    const { open } = mount({}, { default: () => [h('input', { type: 'file', style: 'display: none' }), h('button', 'Upload')] });
    open.value = true;
    await settle();
    expect(document.activeElement).toBe(panel());
  });

  it('focuses the panel itself when there is no field', async () => {
    const { open } = mount({}, { default: () => h('p', 'Nothing to type here.') });
    open.value = true;
    await settle();
    expect(document.activeElement).toBe(panel());
    expect(panel().getAttribute('tabindex')).toBe('-1');
  });

  it('prefers an autofocus element, and the initialFocus prop over both', async () => {
    const { open } = mount({}, { default: () => [h('input', { class: 'field' }), h('button', { class: 'go', 'data-autofocus': '' }, 'Go')] });
    open.value = true;
    await settle();
    expect(document.activeElement).toBe(q('.go'));
    app?.unmount();
    document.body.innerHTML = '';

    const target = ref<HTMLElement | null>(null);
    const second = mount(
      { initialFocus: () => target.value },
      { default: () => [h('input', { class: 'field' }), h('a', { href: '#', class: 'link', ref: target }, 'link')] },
    );
    second.open.value = true;
    await settle();
    expect(document.activeElement).toBe(q('.link'));
  });

  it('returns focus to what had it when it closes', async () => {
    const { open } = mount({}, { default: () => h('input', { class: 'field' }) });
    open.value = true;
    await settle();
    expect(document.activeElement).toBe(q('.field'));
    open.value = false;
    await settle();
    expect(document.activeElement).toBe(trigger);
  });

  it('keeps Shift+Tab from the panel inside the dialog', async () => {
    const { open } = mount({}, { default: () => h('p', 'text'), footer: () => h('button', { class: 'last' }, 'OK') });
    open.value = true;
    await settle();
    expect(document.activeElement).toBe(panel());
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true }));
    await nextTick();
    expect(document.activeElement).toBe(q('.last'));
  });
});

describe('TDialog closing', () => {
  it('closes on Escape, on the close button and on a click on the backdrop, then emits closed', async () => {
    const { open, closed } = mount({}, {}, true);
    await settle();
    escape();
    await settle();
    expect(open.value).toBe(false);
    await afterLeave();
    expect(q('.t-dialog-host')).toBeNull();
    expect(closed).toHaveBeenCalledTimes(1);

    open.value = true;
    await settle();
    q<HTMLButtonElement>('.t-dialog__close')!.click();
    expect(open.value).toBe(false);

    open.value = true;
    await settle();
    clickBackdrop();
    expect(open.value).toBe(false);
  });

  it('does not close when a press starts inside the panel and ends on the backdrop', async () => {
    const { open } = mount({}, {}, true);
    await settle();
    clickBackdrop(true);
    expect(open.value).toBe(true);
  });

  it('honours closeOnEscape and closeOnBackdrop', async () => {
    const { open } = mount({ closeOnEscape: false, closeOnBackdrop: false }, {}, true);
    await settle();
    escape();
    clickBackdrop();
    await settle();
    expect(open.value).toBe(true);
    q<HTMLButtonElement>('.t-dialog__close')!.click();
    expect(open.value).toBe(false);
  });

  it('holds open while busy, with the close button disabled, but follows modelValue', async () => {
    const { open } = mount({ busy: true }, {}, true);
    await settle();
    escape();
    clickBackdrop();
    await settle();
    expect(open.value).toBe(true);
    expect(q<HTMLButtonElement>('.t-dialog__close')!.disabled).toBe(true);
    expect(panel().getAttribute('aria-busy')).toBe('true');
    open.value = false;
    await settle();
    await afterLeave();
    expect(q('.t-dialog-host')).toBeNull();
  });

  it('locks the page scroll while open and gives it back after', async () => {
    const { open } = mount();
    open.value = true;
    await settle();
    expect(document.body.style.overflow).toBe('hidden');
    open.value = false;
    await settle();
    expect(document.body.style.overflow).toBe('');
  });
});

describe('TDialog stacking', () => {
  it('a dialog opened from a dialog is drawn above it and takes Escape alone', async () => {
    const inner = ref(false);
    const { open } = mount(
      {},
      {
        default: () =>
          h(TDialog, { modelValue: inner.value, 'onUpdate:modelValue': (v: boolean) => (inner.value = v), title: 'Delete?' }, () => h('p', 'sure?')),
      },
      true,
    );
    await settle();
    inner.value = true;
    await settle();
    const hosts = [...document.querySelectorAll<HTMLElement>('.t-dialog-host')];
    expect(hosts).toHaveLength(2);
    const index = (el: HTMLElement) => Number(/\+ (\d+)\)/.exec(el.style.zIndex)?.[1]);
    const outer = hosts.find((el) => el.textContent?.includes('Rename chat') && !el.querySelector('.t-dialog-host'))
      ?? hosts.find((el) => el.querySelector('.t-dialog__title')?.textContent === 'Rename chat')!;
    const top = hosts.find((el) => el.querySelector('.t-dialog__title')?.textContent === 'Delete?')!;
    expect(index(top)).toBeGreaterThan(index(outer));
    expect(top.style.zIndex).toMatch(/^calc\(var\(--tnzi-ai-z-dialog, 1000\) \+ \d+\)$/);

    escape();
    await settle();
    expect(inner.value).toBe(false);
    expect(open.value).toBe(true);
  });
});

describe('TDialog layouts and escape hatches', () => {
  it('sizes from the size token, or from width and height', async () => {
    mount({ size: 'lg' }, {}, true);
    await settle();
    expect(panel().style.getPropertyValue('--t-dialog-width')).toBe('var(--tnzi-ai-dialog-width-lg)');
    expect(panel().classList.contains('t-dialog-host__panel--sized')).toBe(false);
    app?.unmount();
    document.body.innerHTML = '';

    mount({ width: 640, height: '70vh' }, {}, true);
    await settle();
    expect(panel().style.getPropertyValue('--t-dialog-width')).toBe('640px');
    expect(panel().style.getPropertyValue('--t-dialog-height')).toBe('70vh');
    expect(panel().classList.contains('t-dialog-host__panel--sized')).toBe(true);
  });

  it('is a bottom sheet when narrow, or the full screen if asked', async () => {
    mount({ narrow: true }, {}, true);
    await settle();
    expect(q('.t-dialog-host')!.classList.contains('t-dialog-host--sheet')).toBe(true);
    expect(panel().classList.contains('t-dialog--narrow')).toBe(true);
    app?.unmount();
    document.body.innerHTML = '';

    mount({ narrow: true, narrowLayout: 'fullscreen' }, {}, true);
    await settle();
    expect(q('.t-dialog-host')!.classList.contains('t-dialog-host--fullscreen')).toBe(true);
  });

  it('follows the viewport below narrowBreakpoint', async () => {
    const width = window.innerWidth;
    Object.defineProperty(window, 'innerWidth', { value: 500, configurable: true, writable: true });
    try {
      mount({}, {}, true);
      await settle();
      expect(q('.t-dialog-host')!.classList.contains('t-dialog-host--sheet')).toBe(true);
    } finally {
      Object.defineProperty(window, 'innerWidth', { value: width, configurable: true, writable: true });
    }
  });

  it('fills the window with fullscreen on a wide screen', async () => {
    mount({ fullscreen: true, narrow: false }, {}, true);
    await settle();
    expect(q('.t-dialog-host')!.classList.contains('t-dialog-host--fullscreen')).toBe(true);
    expect(panel().classList.contains('t-dialog--narrow')).toBe(false);
  });

  it('puts class and data attributes on the panel', async () => {
    mount({ class: 'mine', 'data-test': 'x' }, {}, true);
    await settle();
    expect(panel().classList.contains('mine')).toBe(true);
    expect(panel().getAttribute('data-test')).toBe('x');
  });

  it('drops the body padding with flush', async () => {
    mount({ flush: true }, { default: () => h('img', { alt: '' }) }, true);
    await settle();
    expect(q('.t-dialog__body')!.classList.contains('t-dialog__body--flush')).toBe(true);
  });

  it('takes a header of its own and keeps the close button', async () => {
    mount({ title: undefined }, { header: ({ titleId }: { titleId: string }) => h('h2', { id: titleId, class: 'mine' }, 'Custom') } as never, true);
    await settle();
    const dialog = q('[role="dialog"]')!;
    expect(document.getElementById(dialog.getAttribute('aria-labelledby')!)?.textContent).toBe('Custom');
    expect(q('.t-dialog__title')).toBeNull();
    expect(q('.t-dialog__close')).not.toBeNull();
  });

  it('leaves out the close button with closable false, and the header with it when there is no title', async () => {
    mount({ closable: false }, {}, true);
    await settle();
    expect(q('.t-dialog__close')).toBeNull();
    expect(q('.t-dialog__header')!.classList.contains('t-dialog__header--plain')).toBe(true);
    app?.unmount();
    document.body.innerHTML = '';

    mount({ closable: false, title: undefined, ariaLabel: 'Preview' }, {}, true);
    await settle();
    expect(q('.t-dialog__header')).toBeNull();
    expect(q('.t-dialog__body')!.classList.contains('t-dialog__body--headless')).toBe(true);
  });
});

describe('TDialog above the on-screen keyboard', () => {
  class FakeViewport extends EventTarget {
    height = 844;
    offsetTop = 0;
  }
  const original = Object.getOwnPropertyDescriptor(window, 'visualViewport');
  const insetTop = (el: HTMLElement) => el.style.getPropertyValue('--tnzi-ai-viewport-inset-top');
  const insetBottom = (el: HTMLElement) => el.style.getPropertyValue('--tnzi-ai-viewport-inset-bottom');
  let viewport: FakeViewport;

  /* happy-dom does no layout: every host is a full 390x844 window, as a fixed `inset: 0` box is. */
  const rect = vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect');

  function stubViewport(): void {
    viewport = new FakeViewport();
    Object.defineProperty(window, 'visualViewport', { value: viewport, configurable: true });
    rect.mockImplementation(() => ({ top: 0, bottom: 844, left: 0, right: 390, width: 390, height: 844, x: 0, y: 0, toJSON: () => ({}) }) as DOMRect);
  }

  function openKeyboard(height = 508, offsetTop = 0): void {
    viewport.height = height;
    viewport.offsetTop = offsetTop;
    viewport.dispatchEvent(new Event('resize'));
  }

  afterEach(() => {
    rect.mockReset();
    if (original) Object.defineProperty(window, 'visualViewport', original);
    else delete (window as { visualViewport?: unknown }).visualViewport;
  });

  it.each([
    ['sheet', { narrow: true }],
    ['fullscreen', { narrow: true, narrowLayout: 'fullscreen' }],
  ])('a %s keeps to the visible part: it follows the keyboard and the page moving under it', async (_layout, props) => {
    stubViewport();
    mount(props, { default: () => h('textarea') }, true);
    await settle();
    const hostEl = q('.t-dialog-host')!;
    expect(insetBottom(hostEl)).toBe('0px');

    openKeyboard(508);
    expect(insetBottom(hostEl)).toBe('336px');
    viewport.offsetTop = 90;
    viewport.dispatchEvent(new Event('scroll'));
    expect(insetTop(hostEl)).toBe('90px');
    expect(insetBottom(hostEl)).toBe('246px');

    openKeyboard(844, 0);
    expect(insetTop(hostEl)).toBe('0px');
    expect(insetBottom(hostEl)).toBe('0px');
  });

  it('brings the focused field back into view when the keyboard takes room', async () => {
    stubViewport();
    mount({ narrow: true, narrowLayout: 'fullscreen' }, { default: () => [h('input', { class: 'name' }), h('textarea', { class: 'notes' })] }, true);
    await settle();
    const notes = q<HTMLTextAreaElement>('.notes')!;
    notes.focus();
    const reveal = vi.fn();
    notes.scrollIntoView = reveal;
    openKeyboard(508);
    expect(reveal).toHaveBeenCalledWith({ block: 'nearest', inline: 'nearest' });
  });

  it('leaves a centred dialog laid out against the whole window', async () => {
    stubViewport();
    mount({ narrow: false }, {}, true);
    await settle();
    openKeyboard(508);
    expect(insetBottom(q('.t-dialog-host')!)).toBe('');
  });

  it('writes nothing without a visual viewport', async () => {
    Object.defineProperty(window, 'visualViewport', { value: undefined, configurable: true });
    mount({ narrow: true }, {}, true);
    await settle();
    expect(insetBottom(q('.t-dialog-host')!)).toBe('');
  });

  it('leaves nothing on the document when stacked dialogs close, and stops following the viewport', async () => {
    stubViewport();
    const rootStyle = document.documentElement.getAttribute('style');
    const added = vi.spyOn(viewport, 'addEventListener');
    const removed = vi.spyOn(viewport, 'removeEventListener');
    const inner = ref(false);
    const { open } = mount(
      { narrow: true },
      {
        default: () =>
          h(TDialog, { modelValue: inner.value, 'onUpdate:modelValue': (v: boolean) => (inner.value = v), title: 'Delete?', narrow: true }, () => h('p', 'sure?')),
      },
      true,
    );
    await settle();
    inner.value = true;
    await settle();
    openKeyboard(508);
    const hosts = [...document.querySelectorAll<HTMLElement>('.t-dialog-host')];
    expect(hosts).toHaveLength(2);
    for (const el of hosts) expect(insetBottom(el)).toBe('336px');

    inner.value = false;
    await settle();
    await afterLeave();
    open.value = false;
    await settle();
    await afterLeave();
    expect(q('.t-dialog-host')).toBeNull();
    expect(removed.mock.calls.length).toBe(added.mock.calls.length);
    expect(document.documentElement.getAttribute('style')).toBe(rootStyle);
    expect(document.body.style.overflow).toBe('');
  });
});
