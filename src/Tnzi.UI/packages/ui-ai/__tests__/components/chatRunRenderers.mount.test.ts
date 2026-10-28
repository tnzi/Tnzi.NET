/**
 * The conversation renderers for agents bound to an external CLI runtime, mounted for
 * real: a product's own tool renderer and the generic card, the usage-limit outcome,
 * editing a user message, history search and paging, file cards and stored images.
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

const { default: TToolCall } = await import('../../src/components/reasoning/TToolCall.vue');
const { default: TThreadMessage } = await import('../../src/components/chat/TThreadMessage.vue');
const { default: TThreadList } = await import('../../src/components/chat/TThreadList.vue');
const { default: TFileCard } = await import('../../src/components/chat/TFileCard.vue');
const { default: TMessageAttachments } = await import('../../src/components/chat/TMessageAttachments.vue');
const { provideChatRenderContext, TOOL_RENDERERS_KEY } = await import('../../src/components/chat/chat-render-context');
const { en } = await import('../../src/locales/en');

import type { ChatRenderContext } from '../../src/components/chat/chat-render-context';
import type { ChatMessage } from '../../src/headless/useChat';

let app: App | null = null;
let host: HTMLElement | null = null;

function mount(render: () => unknown, setup?: (app: App) => void): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({ render });
  setup?.(app);
  app.mount(host);
  return host;
}

/** Renders `child` under a render context. */
function withContext(context: ChatRenderContext, child: () => unknown): Component {
  return defineComponent({
    setup() {
      provideChatRenderContext(context);
      return () => child();
    },
  });
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
  vi.useRealTimers();
});

describe('tool renderers', () => {
  const Screenshot = defineComponent({
    props: { toolCall: { type: Object, required: true }, streaming: Boolean },
    setup: (props) => () => h('div', { class: 'custom-shot' }, `shot:${(props.toolCall as { id: string }).id}`),
  });

  it('a tool with a renderer registered for the app uses it, also behind an MCP prefix', () => {
    const el = mount(
      () => h(TToolCall, { toolCall: { id: 'c1', name: 'mcp__acme__take_screenshot', status: 'completed' } }),
      (a) => a.provide(TOOL_RENDERERS_KEY, { take_screenshot: Screenshot }),
    );
    expect(el.querySelector('.custom-shot')?.textContent).toBe('shot:c1');
  });

  it("a shell's own renderer wins over the app's on the same name", () => {
    const Other = defineComponent({ setup: () => () => h('div', { class: 'shell-shot' }) });
    const el = mount(
      () => h(withContext({ toolRenderers: { take_screenshot: Other } }, () =>
        h(TToolCall, { toolCall: { name: 'take_screenshot' } }))),
      (a) => a.provide(TOOL_RENDERERS_KEY, { take_screenshot: Screenshot }),
    );
    expect(el.querySelector('.shell-shot')).not.toBeNull();
    expect(el.querySelector('.custom-shot')).toBeNull();
  });

  it('every other tool gets the generic card: files always, details on demand', async () => {
    const el = mount(() => h(TToolCall, {
      toolCall: {
        id: 'c2',
        name: 'hand_over_file',
        status: 'completed',
        input: { path: '/reports/q3' },
        result: [
          { type: 'text', text: 'The report is ready.' },
          { type: 'file', name: 'report.pdf', uri: 'https://files.example.com/r.pdf' },
        ],
      },
    }));

    expect(el.querySelector('.t-tool-step__subject')?.textContent).toBe('/reports/q3');
    const card = el.querySelector('a.t-file-card') as HTMLAnchorElement;
    expect(card.getAttribute('href')).toBe('https://files.example.com/r.pdf');
    expect(el.querySelector('.t-tool-step__body')).toBeNull();

    (el.querySelector('.t-tool-step__line') as HTMLButtonElement).click();
    await nextTick();
    const body = el.querySelector('.t-tool-step__body')!;
    // The input as a named line, not as the JSON it arrived as.
    expect(body.querySelector('.t-tool-fields__name')?.textContent).toBe('Path');
    expect(body.querySelector('.t-tool-fields__value')?.textContent).toBe('/reports/q3');
    expect(body.textContent).not.toContain('"path"');
    expect(body.textContent).toContain('The report is ready.');
    // The file stays in view with the step open.
    expect(el.querySelector('a.t-file-card')).not.toBeNull();
  });

  it('a running call shows as running while its message streams', () => {
    const el = mount(() => h(TToolCall, { toolCall: { id: 'c3', name: 'Bash', status: 'running', isSuccess: true }, streaming: true }));
    expect(el.querySelector('[data-icon="lucide:check-circle-2"]')).toBeNull();
    expect(el.querySelector('[role="status"]')).not.toBeNull();
  });
});

describe('the usage limit outcome', () => {
  const limited = (resetsAt: string | null): ChatMessage => ({
    id: 'a1', role: 'assistant', content: '', createdAt: 'x', status: 'usage_limit', usageLimitResetsAt: resetsAt,
  });

  it('is said as its own notice, with when it resets', () => {
    const resetsAt = new Date(Date.now() + 60 * 60 * 1000).toISOString();
    const el = mount(() => h(TThreadMessage, { message: limited(resetsAt) }));
    const notice = el.querySelector('.t-usage-limit-notice')!;
    expect(notice.textContent).toContain(en.chat.usageLimit);
    expect(notice.textContent).toContain('It should be available again at about');
    expect(el.querySelector('.t-thread-message__error')).toBeNull();
  });

  it('without a reset time says the limit only', () => {
    const el = mount(() => h(TThreadMessage, { message: limited(null) }));
    expect(el.querySelector('.t-usage-limit-notice')!.textContent?.trim()).toBe(en.chat.usageLimit);
  });

  it("the user's own limit names the window and the reset, not the subscription", () => {
    const resetsAt = new Date(Date.now() + 90 * 60 * 1000 + 20_000).toISOString();
    const el = mount(() => h(TThreadMessage, {
      message: { ...limited(resetsAt), errorCode: 'AI_USAGE_LIMIT_REACHED', usageLimitWindow: 'Session' } as ChatMessage,
    }));
    const notice = el.querySelector('.t-usage-limit-notice')!;
    expect(notice.textContent).toContain(en.usageLimits.reachedSession);
    expect(notice.textContent).toContain('It resets in 1 hr 31 min.');
    expect(notice.textContent).not.toContain(en.chat.usageLimit);
  });
});

describe('editing a user message', () => {
  it('emits the new text, and nothing when unchanged', async () => {
    const onEdit = vi.fn();
    const el = mount(() => h(TThreadMessage, {
      message: { id: 'u1', role: 'user', content: 'first', createdAt: 'x' },
      enableEdit: true,
      onEdit,
    }));
    (el.querySelector('.t-thread-message__user-actions button') as HTMLButtonElement).click();
    await nextTick();
    const editor = el.querySelector('textarea') as HTMLTextAreaElement;
    expect(editor.value).toBe('first');

    editor.value = 'first, better';
    editor.dispatchEvent(new Event('input'));
    await nextTick();
    (el.querySelector('.t-thread-message__editor-btn--primary') as HTMLButtonElement).click();
    expect(onEdit).toHaveBeenCalledWith('u1', 'first, better');
  });

  it('is not offered unless enabled', () => {
    const el = mount(() => h(TThreadMessage, { message: { id: 'u1', role: 'user', content: 'first', createdAt: 'x' } }));
    expect(el.querySelector('.t-thread-message__user-actions')).toBeNull();
  });
});

describe('the thread list', () => {
  it('searches as the user types (debounced) and pages on request', async () => {
    vi.useFakeTimers();
    const onSearch = vi.fn();
    const onLoadMore = vi.fn();
    const el = mount(() => h(TThreadList, {
      threads: [{ id: 't1', title: 'One' }],
      searchable: true,
      hasMore: true,
      onSearch,
      'onLoad-more': onLoadMore,
    }));
    /* The box opens from the header's Search action. */
    expect(el.querySelector('input[type="search"]')).toBeNull();
    (el.querySelector('.t-thread-list__head-act[aria-pressed]') as HTMLButtonElement).click();
    await nextTick();
    const input = el.querySelector('input[type="search"]') as HTMLInputElement;
    input.value = 'quarter';
    input.dispatchEvent(new Event('input'));
    expect(onSearch).not.toHaveBeenCalled();
    vi.advanceTimersByTime(300);
    expect(onSearch).toHaveBeenCalledWith('quarter');

    (el.querySelector('.t-thread-list__more') as HTMLButtonElement).click();
    expect(onLoadMore).toHaveBeenCalledTimes(1);
  });
});

describe('file cards and stored images', () => {
  it("link where the product says, else to an address a browser can open, else nowhere", async () => {
    const custom = mount(() => h(withContext(
      { toolRenderers: {}, fileHref: (file) => `/app/files?uri=${encodeURIComponent(file.uri ?? '')}` },
      () => h(TFileCard, { file: { name: 'r.pdf', uri: 'storage://9' } }),
    )));
    await settle();
    expect(custom.querySelector('a')?.getAttribute('href')).toBe('/app/files?uri=storage%3A%2F%2F9');
    app?.unmount();
    host?.remove();

    const none = mount(() => h(TFileCard, { file: { name: 'r.pdf', uri: 'storage://9' } }));
    await settle();
    expect(none.querySelector('a')).toBeNull();
    expect(none.textContent).toContain('r.pdf');
  });

  it('a stored image is read through the resolver', async () => {
    const resolveAttachmentUrl = vi.fn(async () => 'blob:img-1');
    const el = mount(() => h(withContext(
      { toolRenderers: {}, resolveAttachmentUrl },
      () => h(TMessageAttachments, { attachments: [{ type: 'image', attachmentId: 'att-1', fileName: 'a.png' }] }),
    )));
    await settle();
    expect(resolveAttachmentUrl).toHaveBeenCalledWith('att-1');
    expect(el.querySelector('img')?.getAttribute('src')).toBe('blob:img-1');
  });

  it('a stored PDF is a card linking to its own content', async () => {
    const el = mount(() => h(withContext(
      { toolRenderers: {}, resolveAttachmentUrl: async () => 'blob:pdf-1' },
      () => h(TMessageAttachments, {
        attachments: [{ type: 'file', attachmentId: 'att-2', fileName: 'contract.pdf', mediaType: 'application/pdf', size: 2048 }],
      }),
    )));
    await settle();
    const card = el.querySelector('a.t-file-card') as HTMLAnchorElement;
    expect(card.getAttribute('href')).toBe('blob:pdf-1');
    expect(card.textContent).toContain('contract.pdf');
  });
});
