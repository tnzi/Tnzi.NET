/**
 * The artifact frame, code view and preview, drawn with the package's own
 * icon buttons (each named by its tooltip label) and a native `.t-input`
 * address bar: every button emits or acts, Copy confirms with a check, and
 * the address bar navigates the frame on Enter, not on every keystroke.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createApp, h, nextTick, type App } from 'vue';

vi.mock('shiki', () => ({ codeToHtml: async (code: string) => `<pre><code>${code}</code></pre>` }));

const { default: TArtifact } = await import('../../src/components/artifact/TArtifact.vue');
const { default: TArtifactCodeView } = await import('../../src/components/artifact/TArtifactCodeView.vue');
const { default: TArtifactPreview } = await import('../../src/components/artifact/TArtifactPreview.vue');

let app: App | null = null;
let host: HTMLElement | null = null;

function mount(render: () => unknown): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({ render });
  app.mount(host);
  return host;
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

function button(root: HTMLElement, label: string): HTMLButtonElement {
  const found = root.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`);
  if (!found) throw new Error(`no button "${label}"`);
  return found;
}

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
  vi.restoreAllMocks();
  vi.useRealTimers();
});

describe('TArtifact', () => {
  it('emits download and close from named icon buttons', async () => {
    const onDownload = vi.fn();
    const onClose = vi.fn();
    const root = mount(() => h(TArtifact, { title: 'Report', description: 'Q3', onDownload, onClose }, () => 'body'));

    expect(root.textContent).toContain('Report');
    expect(root.textContent).toContain('Q3');
    button(root, 'Download').click();
    button(root, 'Close').click();
    await settle();

    expect(onDownload).toHaveBeenCalledOnce();
    expect(onClose).toHaveBeenCalledOnce();
  });

  it('an #actions slot replaces Download but Close stays', () => {
    const root = mount(() =>
      h(TArtifact, { title: 'Report' }, { actions: () => h('button', { class: 'own' }, 'Mine') }),
    );
    expect(root.querySelector('button[aria-label="Download"]')).toBeNull();
    expect(root.querySelector('.own')).not.toBeNull();
    expect(root.querySelector('button[aria-label="Close"]')).not.toBeNull();
  });
});

describe('TArtifactCodeView', () => {
  let writeText: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
  });

  it('copies the code and shows Copied for a moment', async () => {
    vi.useFakeTimers();
    const root = mount(() => h(TArtifactCodeView, { code: 'const a = 1', language: 'ts' }));

    button(root, 'Copy').click();
    await vi.advanceTimersByTimeAsync(0);
    await nextTick();
    expect(writeText).toHaveBeenCalledExactlyOnceWith('const a = 1');
    expect(root.querySelector('button[aria-label="Copied"]')).not.toBeNull();

    await vi.advanceTimersByTimeAsync(1500);
    await nextTick();
    expect(root.querySelector('button[aria-label="Copy"]')).not.toBeNull();
  });

  it('emits download', async () => {
    const onDownload = vi.fn();
    const root = mount(() => h(TArtifactCodeView, { code: 'x', filename: 'a.ts', onDownload }));
    expect(root.textContent).toContain('a.ts');
    button(root, 'Download').click();
    await settle();
    expect(onDownload).toHaveBeenCalledOnce();
  });
});

describe('TArtifactPreview', () => {
  const frame = (root: HTMLElement) => root.querySelector('iframe');

  it('navigates the frame on Enter, not while typing', async () => {
    const root = mount(() => h(TArtifactPreview, { src: 'https://a.test/' }));
    const input = root.querySelector<HTMLInputElement>('input.t-input')!;
    expect(input.value).toBe('https://a.test/');
    expect(input.getAttribute('aria-label')).toBe('Preview address');

    input.value = 'https://b.test/';
    input.dispatchEvent(new Event('input'));
    await settle();
    expect(frame(root)!.getAttribute('src')).toBe('https://a.test/');

    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await settle();
    expect(frame(root)!.getAttribute('src')).toBe('https://b.test/');
  });

  it('Refresh goes back to src and reloads the frame', async () => {
    const root = mount(() => h(TArtifactPreview, { src: 'https://a.test/' }));
    const input = root.querySelector<HTMLInputElement>('input.t-input')!;
    input.value = 'https://b.test/';
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await settle();

    const before = frame(root);
    button(root, 'Refresh').click();
    await settle();

    expect(input.value).toBe('https://a.test/');
    expect(frame(root)!.getAttribute('src')).toBe('https://a.test/');
    expect(frame(root)).not.toBe(before);
  });

  it('opens the shown address in a new tab, without an opener', async () => {
    const open = vi.spyOn(window, 'open').mockReturnValue(null);
    const root = mount(() => h(TArtifactPreview, { src: 'https://a.test/' }));
    button(root, 'Open in new tab').click();
    await settle();
    expect(open).toHaveBeenCalledExactlyOnceWith('https://a.test/', '_blank', 'noopener,noreferrer');
  });

  it('keeps the address read-only for srcdoc', () => {
    const root = mount(() => h(TArtifactPreview, { srcdoc: '<p>hi</p>' }));
    expect(root.querySelector<HTMLInputElement>('input.t-input')!.readOnly).toBe(true);
  });
});
