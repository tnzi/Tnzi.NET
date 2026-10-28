/**
 * A `show_widget` call in a reply, mounted for real: a placeholder while it is made, the widget
 * (the runner in a scripts-only sandbox) once it holds a document, the ordinary tool card when it
 * failed; matched under a CLI agent's MCP name too, and kept in view instead of folded into the run.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, type App } from 'vue';

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

const { default: TThreadMessage } = await import('../../src/components/chat/TThreadMessage.vue');
const { TOOL_RENDERERS_KEY } = await import('../../src/components/chat/chat-render-context');
const { createWidgetToolRenderers } = await import('../../src/components/widget/widget-renderers');
const { en } = await import('../../src/locales/en');

import type { ChatMessage, ToolCallInfo } from '../../src/headless/useChat';

const RUNNER = 'https://widgets.example.com/ai/widget-runner';
const HTML = '<!DOCTYPE html><html><head></head><body><input type="range"></body></html>';

let app: App | null = null;
let host: HTMLElement | null = null;
let savedObserver: typeof IntersectionObserver | undefined;

beforeEach(() => {
  // Without an observer every frame is near; the lazy path is the browser's to show.
  savedObserver = globalThis.IntersectionObserver;
  (globalThis as { IntersectionObserver?: unknown }).IntersectionObserver = undefined;
});

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  (globalThis as { IntersectionObserver?: unknown }).IntersectionObserver = savedObserver;
  document.documentElement.classList.remove('dark');
});

async function mount(toolCalls: ToolCallInfo[], streaming = false): Promise<HTMLElement> {
  host = document.createElement('div');
  document.body.appendChild(host);
  const message: ChatMessage = {
    id: 'a1',
    role: 'assistant',
    createdAt: '2026-10-22T00:00:00Z',
    content: 'Here it is.',
    status: streaming ? 'streaming' : 'done',
    isStreaming: streaming,
    toolCalls,
  };
  app = createApp(defineComponent({ setup: () => () => h(TThreadMessage as never, { message }) }));
  app.provide(TOOL_RENDERERS_KEY, createWidgetToolRenderers({ runnerUrl: RUNNER }));
  app.mount(host);
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
  return host;
}

const widget = (patch: Partial<ToolCallInfo> = {}): ToolCallInfo => ({
  id: 'w1',
  name: 'show_widget',
  status: 'completed',
  input: { title: 'Usage left', html: HTML },
  textOffset: 0,
  ...patch,
});

describe('a widget call', () => {
  it('is the widget once complete: the runner, sandboxed to scripts only, in view under the run', async () => {
    const el = await mount([widget()]);
    const frame = el.querySelector('iframe.t-widget-frame__iframe') as HTMLIFrameElement;
    expect(frame).not.toBeNull();
    expect(frame.getAttribute('src')).toBe(RUNNER);
    expect(frame.getAttribute('sandbox')).toBe('allow-scripts');
    expect(frame.getAttribute('title')).toBe('Usage left');
    expect(el.querySelector('.t-widget__title')?.textContent).toBe('Usage left');
    expect(el.querySelector('.t-tool-run__kept .t-widget')).not.toBeNull();
    expect(el.querySelectorAll('.t-widget__action')).toHaveLength(3);
  });

  it('is matched under a CLI agent MCP name too', async () => {
    const el = await mount([widget({ name: 'mcp__acme__show_widget' })]);
    expect(el.querySelector('iframe.t-widget-frame__iframe')).not.toBeNull();
  });

  it('is a placeholder, never half a document, while it is made', async () => {
    const el = await mount([widget({ status: 'running', input: { title: 'Usage left', html: '<div' } })], true);
    expect(el.querySelector('iframe')).toBeNull();
    expect(el.querySelector('.t-widget--building')?.textContent).toContain(en.widget.building);
    expect(el.querySelector('.t-widget--building')?.textContent).toContain('Usage left');
  });

  it('is the ordinary tool card when it failed or holds no usable document', async () => {
    let el = await mount([widget({ status: 'failed', error: "'html' is required" })]);
    expect(el.querySelector('.t-widget')).toBeNull();
    expect(el.querySelector('iframe')).toBeNull();
    expect(el.querySelector('.t-tool-run__line')).not.toBeNull();

    app?.unmount();
    host?.remove();
    el = await mount([widget({ input: { title: 'Usage left' } })]);
    expect(el.querySelector('.t-widget')).toBeNull();
  });

  it('hands the frame the page colour scheme', async () => {
    document.documentElement.classList.add('dark');
    const el = await mount([widget()]);
    expect((el.querySelector('iframe') as HTMLIFrameElement).style.colorScheme).toBe('dark');
  });

  it('opens larger in a dialog of its own', async () => {
    const el = await mount([widget()]);
    (el.querySelector(`.t-widget__action[aria-label="${en.widget.expand}"]`) as HTMLButtonElement).click();
    await nextTick();
    const dialog = document.body.querySelector('.t-widget-overlay [role="dialog"]');
    expect(dialog?.getAttribute('aria-label')).toBe('Usage left');
    expect(dialog?.querySelector('iframe')?.getAttribute('sandbox')).toBe('allow-scripts');
    (dialog?.querySelector(`[aria-label="${en.widget.close}"]`) as HTMLButtonElement).click();
    await nextTick();
    expect(document.body.querySelector('.t-widget-overlay')).toBeNull();
  });
});

describe('createWidgetToolRenderers', () => {
  it('refuses to register without a runner', () => {
    expect(() => createWidgetToolRenderers({ runnerUrl: ' ' })).toThrow(/runnerUrl/);
  });
});
