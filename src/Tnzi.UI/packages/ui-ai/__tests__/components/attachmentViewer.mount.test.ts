/**
 * An image a message carries opens full size in a viewer over the page, never as a new
 * tab: a just-sent image is a data: URL, which browsers refuse to open as a page (the tab
 * stays blank), and an app embedding the UI hands new windows to the system browser, where
 * the page's blob: URLs do not exist. Mounted for real: a just-sent image, a stored one,
 * a tool's large screenshot, keyboard opening and closing.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, type App, type Component } from 'vue';

vi.mock('@iconify/vue', async (importOriginal) => {
  const original = await importOriginal<typeof import('@iconify/vue')>();
  const { defineComponent: define, h: render } = await import('vue');
  return {
    ...original,
    Icon: define({
      props: { icon: { type: String, default: '' } },
      setup: (props) => () => render('i', { 'data-icon': props.icon }),
    }),
  };
});

const { default: TMessageAttachments } = await import('../../src/components/chat/TMessageAttachments.vue');
const { default: TAttachmentImage } = await import('../../src/components/chat/TAttachmentImage.vue');
const { provideChatRenderContext } = await import('../../src/components/chat/chat-render-context');
const { en } = await import('../../src/locales/en');
/* Warm the module the viewer loads lazily: its first transform is slower than a test's timeout. */
await import('../../src/components/overlay/TImageViewer.vue');

import type { ChatRenderContext } from '../../src/components/chat/chat-render-context';

const PIXEL = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==';

let app: App | null = null;
let host: HTMLElement | null = null;

function mount(child: () => unknown, context: ChatRenderContext = { toolRenderers: {} }): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  const Root: Component = defineComponent({
    setup() {
      provideChatRenderContext(context);
      return () => child();
    },
  });
  app = createApp(Root);
  app.mount(host);
  return host;
}

/* The viewer is an async component: give its dynamic import a turn to land. */
async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
  await new Promise((resolve) => setTimeout(resolve, 0));
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

function viewer(): HTMLElement | null {
  return document.querySelector<HTMLElement>('[data-image-viewer]');
}

/** Clicks a thumbnail and waits for the viewer, whose code arrives by dynamic import. */
async function open(thumb: HTMLElement): Promise<HTMLElement> {
  thumb.click();
  await vi.waitFor(() => expect(viewer()).not.toBeNull(), { timeout: 5000 });
  await settle();
  return viewer()!;
}

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
});

describe('opening an image attachment', () => {
  it('a just-sent image opens in the viewer, not as a link to its data: URL', async () => {
    const el = mount(() => h(TMessageAttachments, {
      attachments: [{ type: 'image', mediaType: 'image/png', base64Data: PIXEL, fileName: 'cat.png' }],
    }));
    await settle();

    expect(el.querySelector('a')).toBeNull();
    const thumb = el.querySelector<HTMLButtonElement>('button.t-attachment-image')!;
    expect(thumb.getAttribute('aria-label')).toBe('Open cat.png');
    expect(thumb.querySelector('img')?.getAttribute('src')).toBe(`data:image/png;base64,${PIXEL}`);
    expect(viewer()).toBeNull();

    const panel = await open(thumb);
    expect(panel.getAttribute('role')).toBe('dialog');
    expect(panel.getAttribute('aria-modal')).toBe('true');
    expect(panel.getAttribute('aria-label')).toBe('cat.png');
    expect(panel.querySelector('[data-image-viewer-prev]')).toBeNull();
    expect(panel.querySelector('.t-image-viewer__count')).toBeNull();
    const full = panel.querySelector<HTMLImageElement>('.t-image-viewer__image')!;
    expect(full.getAttribute('src')).toBe(`data:image/png;base64,${PIXEL}`);
    expect(full.getAttribute('alt')).toBe('cat.png');
  });

  it('a stored image opens its resolved blob: URL in the viewer', async () => {
    const el = mount(
      () => h(TMessageAttachments, { attachments: [{ type: 'image', attachmentId: 'att-1', fileName: 'a.png' }] }),
      { toolRenderers: {}, resolveAttachmentUrl: async () => 'blob:img-1' },
    );
    await settle();

    const panel = await open(el.querySelector<HTMLButtonElement>('button.t-attachment-image')!);
    expect(panel.querySelector('.t-image-viewer__image')?.getAttribute('src')).toBe('blob:img-1');
  });

  it('an image without a name is labelled in the locale', async () => {
    const el = mount(() => h(TMessageAttachments, {
      attachments: [{ type: 'image', mediaType: 'image/png', base64Data: PIXEL }],
    }));
    await settle();
    expect(el.querySelector('button.t-attachment-image')?.getAttribute('aria-label')).toBe(`Open ${en.chat.image}`);
  });

  it('a tool\'s large screenshot opens in the viewer too', async () => {
    const el = mount(
      () => h(TAttachmentImage, { attachmentId: 'shot-1', alt: 'browser_screenshot', large: true }),
      { toolRenderers: {}, resolveAttachmentUrl: async () => 'blob:shot-1' },
    );
    await settle();

    const thumb = el.querySelector<HTMLButtonElement>('button.t-attachment-image--large')!;
    expect(thumb.querySelector('img')?.getAttribute('src')).toBe('blob:shot-1');
    const panel = await open(thumb);
    expect(panel.querySelector('.t-image-viewer__image')?.getAttribute('src')).toBe('blob:shot-1');
  });

  it('is a button for the keyboard, and Escape closes the viewer and returns focus to it', async () => {
    const el = mount(() => h(TMessageAttachments, {
      attachments: [{ type: 'image', mediaType: 'image/png', base64Data: PIXEL, fileName: 'cat.png' }],
    }));
    await settle();

    const thumb = el.querySelector<HTMLButtonElement>('button.t-attachment-image')!;
    expect(thumb.getAttribute('type')).toBe('button');
    expect(thumb.getAttribute('aria-haspopup')).toBe('dialog');
    thumb.focus();
    await open(thumb);

    expect(document.activeElement).toBe(viewer());
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();
    expect(viewer()).toBeNull();
    expect(document.activeElement).toBe(thumb);
  });

  it('a stored image that cannot be read stays a placeholder with nothing to open', async () => {
    const el = mount(
      () => h(TMessageAttachments, { attachments: [{ type: 'image', attachmentId: 'gone', fileName: 'x.png' }] }),
      { toolRenderers: {}, resolveAttachmentUrl: async () => null },
    );
    await settle();
    expect(el.querySelector('button')).toBeNull();
    expect(el.querySelector('.t-attachment-image--placeholder')?.getAttribute('title')).toBe(en.chat.attachmentUnavailable);
  });
});

describe('a set of images', () => {
  const two = () =>
    h(TMessageAttachments, {
      attachments: [
        { type: 'image', mediaType: 'image/png', base64Data: PIXEL, fileName: 'one.png' },
        { type: 'file', mediaType: 'application/pdf', fileName: 'notes.pdf' },
        { type: 'image', attachmentId: 'att-2', fileName: 'two.png' },
      ],
    });
  const context = { toolRenderers: {}, resolveAttachmentUrl: async () => 'blob:two' };
  const shownSrc = () => viewer()!.querySelector('.t-image-viewer__image')?.getAttribute('src');
  const press = (key: string) => viewer()!.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));

  it('opens on the picture clicked, says where it is, and the arrow keys step through the set, wrapping round', async () => {
    const el = mount(two, context);
    await settle();
    const thumbs = el.querySelectorAll<HTMLButtonElement>('button.t-attachment-image');
    expect(thumbs).toHaveLength(2);

    await open(thumbs[1]!);
    expect(shownSrc()).toBe('blob:two');
    expect(viewer()!.getAttribute('aria-label')).toBe('two.png');
    expect(viewer()!.querySelector('.t-image-viewer__count')!.textContent).toBe('2 / 2');

    press('ArrowRight');
    await settle();
    expect(shownSrc()).toBe(`data:image/png;base64,${PIXEL}`);
    expect(viewer()!.querySelector('.t-image-viewer__count')!.textContent).toBe('1 / 2');
    press('ArrowLeft');
    await settle();
    expect(shownSrc()).toBe('blob:two');
  });

  it('the buttons step too, and are named', async () => {
    const el = mount(two, context);
    await settle();
    await open(el.querySelector<HTMLButtonElement>('button.t-attachment-image')!);
    const prev = viewer()!.querySelector<HTMLButtonElement>('[data-image-viewer-prev]')!;
    const next = viewer()!.querySelector<HTMLButtonElement>('[data-image-viewer-next]')!;
    expect(prev.getAttribute('aria-label')).toBe('Previous image');
    expect(next.getAttribute('aria-label')).toBe('Next image');
    next.click();
    await settle();
    expect(shownSrc()).toBe('blob:two');
    prev.click();
    await settle();
    expect(viewer()!.getAttribute('aria-label')).toBe('one.png');
  });

  it('the close button and a click beside the picture close it', async () => {
    const el = mount(two, context);
    await settle();
    const thumb = el.querySelector<HTMLButtonElement>('button.t-attachment-image')!;
    await open(thumb);
    viewer()!.querySelector<HTMLButtonElement>('[data-image-viewer-close]')!.click();
    await settle();
    expect(viewer()).toBeNull();

    await open(thumb);
    viewer()!.querySelector<HTMLElement>('.t-image-viewer__image')!.click();
    await settle();
    expect(viewer()).not.toBeNull();
    viewer()!.click();
    await settle();
    expect(viewer()).toBeNull();
  });

  it('a picture of the set that cannot be shown says so', async () => {
    const el = mount(two, { toolRenderers: {}, resolveAttachmentUrl: async () => null });
    await settle();
    await open(el.querySelector<HTMLButtonElement>('button.t-attachment-image')!);
    press('ArrowRight');
    await settle();
    expect(viewer()!.querySelector('.t-image-viewer__image')).toBeNull();
    expect(viewer()!.querySelector('[role="status"]')!.textContent).toBe("This image couldn't be loaded");
  });

  it('two messages are two sets', async () => {
    const el = mount(() => [
      h(TMessageAttachments, { attachments: [{ type: 'image', mediaType: 'image/png', base64Data: PIXEL, fileName: 'a.png' }] }),
      h(TMessageAttachments, { attachments: [{ type: 'image', mediaType: 'image/png', base64Data: PIXEL, fileName: 'b.png' }] }),
    ]);
    await settle();
    await open(el.querySelectorAll<HTMLButtonElement>('button.t-attachment-image')[1]!);
    expect(viewer()!.getAttribute('aria-label')).toBe('b.png');
    expect(viewer()!.querySelector('[data-image-viewer-next]')).toBeNull();
  });
});
