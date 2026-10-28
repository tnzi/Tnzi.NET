/**
 * Tool calls made back to back, mounted for real: one line for the run, live and from
 * history alike; the steps one click away; failures said on the line; what the user came
 * for kept in view; one sign of work while the run goes on.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, reactive, type App } from 'vue';

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
const { default: TChatMessage } = await import('../../src/components/chat/TChatMessage.vue');
const { TOOL_RENDERERS_KEY } = await import('../../src/components/chat/chat-render-context');
const { createAiI18n } = await import('../../src/i18n/index');
const { en } = await import('../../src/locales/en');
const { zhCn } = await import('../../src/locales/zh-cn');

import type { ToolRendererMap } from '../../src/components/chat/chat-render-context';
import type { ChatMessage, ToolCallInfo } from '../../src/headless/useChat';
import type { AiLocaleMessages } from '../../src/locales/en';

let app: App | null = null;
let host: HTMLElement | null = null;

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
});

interface MountOptions {
  locale?: AiLocaleMessages;
  renderers?: ToolRendererMap;
  slots?: Record<string, (scope: never) => unknown>;
}

async function mount(component: unknown, props: () => Record<string, unknown>, options: MountOptions = {}): Promise<HTMLElement> {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp(defineComponent({
    setup() {
      if (options.locale) createAiI18n(options.locale);
      return () => h(component as never, props(), options.slots);
    },
  }));
  if (options.renderers) app.provide(TOOL_RENDERERS_KEY, options.renderers);
  app.mount(host);
  await settle();
  return host;
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

const SEARCH_TEXT = 'Web search results for query: "q"\n\nLinks: [{"title":"Electron Releases","url":"https://www.electronjs.org/releases"}]';
const lead = 'Let me check.';

const search = (id: string, patch: Partial<ToolCallInfo> = {}): ToolCallInfo => ({
  id,
  name: 'WebSearch',
  status: 'completed',
  input: { query: `query ${id}` },
  result: [{ type: 'text', text: SEARCH_TEXT }],
  textOffset: lead.length,
  ...patch,
});
const fetchPage = (id: string, patch: Partial<ToolCallInfo> = {}): ToolCallInfo => ({
  id,
  name: 'WebFetch',
  status: 'completed',
  input: { url: `https://github.com/wailsapp/wails/releases/${id}` },
  result: [{ type: 'text', text: 'Release notes.' }],
  textOffset: lead.length,
  ...patch,
});
const answer = (toolCalls: ToolCallInfo[], patch: Partial<ChatMessage> = {}): ChatMessage => ({
  id: 'a1',
  role: 'assistant',
  createdAt: '2026-10-21T00:00:00Z',
  content: `${lead}\n\nElectron 33 is current.`,
  status: 'done',
  toolCalls,
  ...patch,
});

const lines = (el: HTMLElement) => Array.from(el.querySelectorAll('.t-tool-run__line'));
const lineText = (el: HTMLElement, i = 0) => (lines(el)[i]?.querySelector('.t-tool-run__label')?.textContent ?? '').trim();
const box = (el: HTMLElement) => el.querySelector('.t-tool-run__box') as HTMLElement;

const SEVEN = [search('s1'), fetchPage('f1'), fetchPage('f2'), search('s2'), fetchPage('f3'), fetchPage('f4'), fetchPage('f5')];

describe('a finished run', () => {
  it('is one line that sums the run up by kind, with no mark', async () => {
    const el = await mount(TThreadMessage, () => ({ message: answer(SEVEN) }));
    expect(lines(el)).toHaveLength(1);
    expect(lineText(el)).toBe('Read 5 pages, searched the web');
    expect(el.querySelector('.t-tool-run__mark')).toBeNull();
    expect(box(el).style.display).toBe('none');
  });

  it('is summed up in the interface language', async () => {
    const el = await mount(TThreadMessage, () => ({ message: answer(SEVEN) }), { locale: zhCn });
    expect(lineText(el)).toBe('读取了 5 个网页，搜索了网页');
  });

  it('opens on one box with a row per step, a row opening its own detail in place', async () => {
    const el = await mount(TThreadMessage, () => ({ message: answer(SEVEN) }));
    (lines(el)[0] as HTMLButtonElement).click();
    await nextTick();
    expect(lines(el)[0]?.getAttribute('aria-expanded')).toBe('true');
    expect(box(el).style.display).toBe('');
    const rows = Array.from(box(el).querySelectorAll('.t-tool-run__row'));
    expect(rows).toHaveLength(7);
    expect(rows.every((row) => row.querySelector('.t-tool-step.is-row'))).toBe(true);
    expect(rows[0]?.querySelector('.t-tool-step__label')?.textContent).toBe(en.tools.webSearchDone);
    expect(rows[1]?.querySelector('.t-tool-step__subject')?.textContent).toBe('github.com');

    (rows[0]?.querySelector('.t-tool-step__line') as HTMLButtonElement).click();
    await nextTick();
    expect(rows[0]?.querySelector('a.t-tool-links__row')?.getAttribute('href')).toBe('https://www.electronjs.org/releases');
  });

  it('of one step opens straight onto that step: one click from the results', async () => {
    const el = await mount(TThreadMessage, () => ({ message: answer([search('s1')]) }));
    expect(lineText(el)).toBe(en.tools.webSearchDone);
    (lines(el)[0] as HTMLButtonElement).click();
    await settle();
    expect(box(el).querySelector('a.t-tool-links__row')).not.toBeNull();
  });

  it('is cut by reply text: text between calls makes two runs', async () => {
    const content = `${lead}\n\nFirst.\n\nDone.`;
    const message = answer([search('s1'), fetchPage('f1', { textOffset: `${lead}\n\nFirst.`.length })], { content });
    const el = await mount(TThreadMessage, () => ({ message }));
    expect(lines(el).map((_, i) => lineText(el, i))).toEqual([en.tools.webSearchDone, en.tools.readPage]);
  });

  it('says on the line that something failed, and which, once opened', async () => {
    const failed = search('s2', { status: 'failed', result: null, error: 'quota' });
    const el = await mount(TThreadMessage, () => ({ message: answer([fetchPage('f1'), failed]) }));
    expect(el.querySelector('.t-tool-run__mark--failed [data-icon="lucide:x-circle"]')).not.toBeNull();
    expect(el.querySelector('.t-tool-run__part--failed')?.textContent).toBe(`, ${en.tools.webSearchFailed.charAt(0).toLowerCase()}${en.tools.webSearchFailed.slice(1)}`);
    (lines(el)[0] as HTMLButtonElement).click();
    await nextTick();
    const rows = box(el).querySelectorAll('.t-tool-run__row');
    expect(rows[1]?.querySelector('[data-icon="lucide:x-circle"]')).not.toBeNull();
  });

  it('does not look failed when the run retried past the failure, which still shows once opened', async () => {
    const failed = search('s1', { status: 'failed', result: null, error: 'bad query' });
    const el = await mount(TThreadMessage, () => ({ message: answer([failed, search('s2'), fetchPage('f1')]) }));
    expect(el.querySelector('.t-tool-run__mark')).toBeNull();
    expect(el.querySelector('.t-tool-run__part--failed')).toBeNull();
    expect(el.querySelector('.t-tool-run__part--recovered')?.textContent).toBe(', a step retried after an error');
    (lines(el)[0] as HTMLButtonElement).click();
    await nextTick();
    const rows = box(el).querySelectorAll('.t-tool-run__row');
    expect(rows[0]?.querySelector('[data-icon="lucide:x-circle"]')).not.toBeNull();
  });
});

describe('live and from history', () => {
  it('reads the current step while it goes on, then the same line as history once done', async () => {
    const state = reactive({
      message: answer([search('s1'), fetchPage('f1', { status: 'running', result: null })], {
        content: lead,
        isStreaming: true,
        status: 'streaming',
      }),
    });
    const el = await mount(TThreadMessage, () => ({ message: state.message, showAgentByline: false }));
    expect(el.querySelector('.t-tool-run__label.is-live')?.textContent).toBe(en.tools.readingPage);

    state.message = answer([search('s1'), fetchPage('f1')]);
    await settle();
    const live = lineText(el);
    expect(el.querySelector('.t-tool-run__label.is-live')).toBeNull();
    app?.unmount();
    host?.remove();

    const history = await mount(TThreadMessage, () => ({ message: answer([search('s1'), fetchPage('f1')]) }));
    expect(lineText(history)).toBe(live);
    expect(live).toBe('Searched the web, read a page');
  });

  it("shows one sign of work: the product's mark moves onto the run's line", async () => {
    const seen: string[] = [];
    const el = await mount(
      TThreadMessage,
      () => ({
        message: answer([search('s1', { status: 'running', result: null })], { content: lead, isStreaming: true, status: 'streaming' }),
        showAgentByline: false,
      }),
      {
        slots: {
          'agent-mark': (scope: { placement: string; inRun?: boolean }) => {
            seen.push(`${scope.placement}:${scope.inRun ?? false}`);
            return h('b', { class: 'cat' });
          },
        } as Record<string, (scope: never) => unknown>,
      },
    );
    expect(el.querySelectorAll('.cat')).toHaveLength(1);
    expect(el.querySelector('.t-tool-run__line .cat')).not.toBeNull();
    expect(el.querySelector('.t-thread-message__pending, .t-thread-message__pending-mark')).toBeNull();
    expect(seen).toContain('pending:true');
  });

  it('without a product mark, the pulsing dot moves onto the line instead', async () => {
    const el = await mount(TThreadMessage, () => ({
      message: answer([search('s1', { status: 'running', result: null })], { content: '', isStreaming: true, status: 'streaming', toolCalls: [search('s1', { status: 'running', result: null, textOffset: 0 })] }),
      showAgentByline: false,
    }));
    expect(el.querySelectorAll('.t-thread-message__pending')).toHaveLength(1);
    expect(el.querySelector('.t-tool-run__line .t-thread-message__pending')).not.toBeNull();
  });

  it('with the byline on, the run keeps its own working mark and nothing is added under it', async () => {
    const el = await mount(TThreadMessage, () => ({
      message: answer([search('s1', { status: 'running', result: null })], { content: lead, isStreaming: true, status: 'streaming' }),
    }));
    expect(el.querySelector('.t-tool-run__line [role="status"]')).not.toBeNull();
    expect(el.querySelector('.t-thread-message__pending, .t-thread-message__pending-mark')).toBeNull();
  });
});

describe('what stays in view', () => {
  const FileStub = defineComponent({
    props: { toolCall: { type: Object, required: true }, streaming: Boolean },
    setup: (props) => () => h('div', { class: 'file-stub' }, (props.toolCall as ToolCallInfo).id ?? ''),
  });
  const sendFile = (id: string, patch: Partial<ToolCallInfo> = {}): ToolCallInfo => ({
    id,
    name: 'mcp__desk__send_file',
    status: 'completed',
    input: { path: `/out/${id}.pdf` },
    textOffset: lead.length,
    ...patch,
  });
  const renderers: ToolRendererMap = {
    send_file: {
      component: FileStub,
      alwaysVisible: (call) => !call.error,
      describe: (call) => ({
        kind: 'send',
        running: 'Sending a file',
        done: 'Sent a file',
        failed: "Couldn't send a file",
        subject: (call.input as { path: string }).path,
      }),
    },
  };

  it('a step registered as always visible is shown whole under the line, and still counted', async () => {
    const el = await mount(TThreadMessage, () => ({ message: answer([search('s1'), sendFile('f9')]) }), { renderers });
    const stub = el.querySelector('.file-stub') as HTMLElement;
    expect(stub.textContent).toBe('f9');
    expect(stub.closest('.t-tool-run__box')).toBeNull();
    expect(lineText(el)).toBe('Searched the web, sent a file');
    (lines(el)[0] as HTMLButtonElement).click();
    await nextTick();
    const row = box(el).querySelectorAll('.t-tool-run__row')[1];
    expect(row?.querySelector('.t-tool-step__label')?.textContent).toBe('Sent a file');
    expect(row?.querySelector('.t-tool-step__subject')?.textContent).toBe('/out/f9.pdf');
    expect(row?.querySelector('.file-stub')).toBeNull();
  });

  it('a run of nothing but such steps has no line at all', async () => {
    const el = await mount(TThreadMessage, () => ({ message: answer([sendFile('f1'), sendFile('f2')]) }), { renderers });
    expect(lines(el)).toHaveLength(0);
    expect(el.querySelectorAll('.file-stub')).toHaveLength(2);
  });

  it('the rule is per call: a failed hand-over folds into the run like any step', async () => {
    const el = await mount(TThreadMessage, () => ({ message: answer([search('s1'), sendFile('f1', { status: 'failed', error: 'too big' })]) }), { renderers });
    expect((el.querySelector('.file-stub') as HTMLElement).closest('.t-tool-run__box')).not.toBeNull();
    expect(el.querySelector('.t-tool-run__part--failed')?.textContent).toBe(", couldn't send a file");
  });

  it("what a folded step keeps in view (TToolStep's `after`) is shown under the line", async () => {
    const handOver: ToolCallInfo = {
      id: 'g1',
      name: 'export_report',
      status: 'completed',
      input: { quarter: 'Q3' },
      result: [{ type: 'file', name: 'q3.pdf', uri: 'https://files.example.com/q3.pdf' }],
      textOffset: lead.length,
    };
    const el = await mount(TThreadMessage, () => ({ message: answer([search('s1'), handOver]) }));
    const card = el.querySelector('a.t-file-card') as HTMLElement;
    expect(card.getAttribute('href')).toBe('https://files.example.com/q3.pdf');
    expect(card.closest('.t-tool-run__outlet')).not.toBeNull();
    expect(card.closest('.t-tool-run__box')).toBeNull();
  });
});

describe("a product's words for its tools", () => {
  const readFile = (id: string, path: string): ToolCallInfo => ({
    id, name: 'read_file', status: 'completed', input: { path }, textOffset: lead.length,
  });
  const renderers: ToolRendererMap = {
    read_file: {
      describe: (call) => ({
        kind: 'read',
        running: 'Reading a file',
        done: 'Read a file',
        failed: "Couldn't read a file",
        many: (count) => `Read ${count} files`,
        subject: (call.input as { path: string }).path,
      }),
    },
  };

  it('count its steps in the summary and name each row, without a component of its own', async () => {
    const el = await mount(TThreadMessage, () => ({ message: answer([readFile('r1', '/a'), readFile('r2', '/b'), readFile('r3', '/c')]) }), { renderers });
    expect(lineText(el)).toBe('Read 3 files');
    (lines(el)[0] as HTMLButtonElement).click();
    await nextTick();
    const first = box(el).querySelector('.t-tool-run__row');
    expect(first?.querySelector('.t-tool-step__label')?.textContent).toBe('Read a file');
    expect(first?.querySelector('.t-tool-step__subject')?.textContent).toBe('/a');
  });

  it('default to the tool name when there are none', async () => {
    const el = await mount(TThreadMessage, () => ({ message: answer([readFile('r1', '/a'), readFile('r2', '/b')]) }));
    expect(lineText(el)).toBe('Used Read File 2 times');
  });
});

describe('the classic layout', () => {
  it('shows its tool calls as a run too', async () => {
    const el = await mount(TChatMessage, () => ({ message: answer([search('s1'), fetchPage('f1')], { content: 'Ok' }) }));
    expect(lineText(el)).toBe('Searched the web, read a page');
    expect(el.querySelectorAll('.t-tool-run__row')).toHaveLength(2);
  });
});
