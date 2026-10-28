/**
 * Tool calls as steps of an answer, mounted for real: the quiet line, what opens under
 * it, the CLI's WebSearch / WebFetch views in both dictionaries, the generic view
 * without call payloads, and a product's renderer in the same shell.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
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

const { default: TToolCall } = await import('../../src/components/reasoning/TToolCall.vue');
const { default: TToolStep } = await import('../../src/components/reasoning/TToolStep.vue');
const { default: TToolOutput } = await import('../../src/components/reasoning/TToolOutput.vue');
const { default: TToolCallCard } = await import('../../src/components/reasoning/TToolCallCard.vue');
const { TOOL_RENDERERS_KEY } = await import('../../src/components/chat/chat-render-context');
const { createAiI18n } = await import('../../src/i18n/index');
const { en } = await import('../../src/locales/en');
const { zhCn } = await import('../../src/locales/zh-cn');

import type { ToolCallInfo } from '../../src/headless/useChat';
import type { AiLocaleMessages } from '../../src/locales/en';

let app: App | null = null;
let host: HTMLElement | null = null;

function mount(render: () => unknown, options: { locale?: AiLocaleMessages; setup?: (app: App) => void } = {}): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp(defineComponent({
    setup() {
      if (options.locale) createAiI18n(options.locale);
      return () => render();
    },
  }));
  options.setup?.(app);
  app.mount(host);
  return host;
}

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
});

const line = (el: HTMLElement) => el.querySelector('.t-tool-step__line') as HTMLElement;
const lineText = (el: HTMLElement) =>
  `${el.querySelector('.t-tool-step__label')?.textContent ?? ''} ${el.querySelector('.t-tool-step__subject')?.textContent ?? ''}`.trim();

async function open(el: HTMLElement): Promise<HTMLElement> {
  (line(el) as HTMLButtonElement).click();
  await nextTick();
  return el.querySelector('.t-tool-step__body') as HTMLElement;
}

const SEARCH_RESULT = [
  'Web search results for query: "tauri deep link"',
  '',
  'Links: [{"title":"Deep Linking | Tauri","url":"https://v2.tauri.app/plugin/deep-linking/"},{"title":"Issue 1","url":"https://www.github.com/tauri-apps/x/issues/1"}]',
  '',
  'Notes for the agent.',
  '',
  'REMINDER: You MUST include the sources above in your response to the user using markdown hyperlinks.',
].join('\n');

const search = (patch: Partial<ToolCallInfo> = {}): ToolCallInfo => ({
  id: 's1',
  name: 'WebSearch',
  status: 'completed',
  input: { query: 'tauri deep link' },
  result: [{ type: 'text', text: SEARCH_RESULT }],
  ...patch,
});

describe('the CLI WebSearch', () => {
  it('reads as one quiet line: the action and the query', () => {
    const el = mount(() => h(TToolCall, { toolCall: search() }));
    expect(lineText(el)).toBe(`${en.tools.webSearchDone} tauri deep link`);
    expect(el.querySelector('.t-tool-step__body')).toBeNull();
    expect(line(el).getAttribute('aria-expanded')).toBe('false');
  });

  it('opens on the pages it found, each opening in a new tab, with no call payload', async () => {
    const el = mount(() => h(TToolCall, { toolCall: search() }));
    const body = await open(el);

    const rows = Array.from(body.querySelectorAll('a.t-tool-links__row')) as HTMLAnchorElement[];
    expect(rows.map((a) => a.querySelector('.t-tool-links__title')?.textContent)).toEqual(['Deep Linking | Tauri', 'Issue 1']);
    expect(rows.map((a) => a.querySelector('.t-tool-links__domain')?.textContent)).toEqual(['v2.tauri.app', 'github.com']);
    for (const a of rows) {
      expect(a.getAttribute('target')).toBe('_blank');
      expect(a.getAttribute('rel')).toBe('noopener noreferrer');
    }
    expect(body.textContent).toContain(en.tools.results);
    expect(body.textContent).not.toContain('Links:');
    expect(body.textContent).not.toContain('REMINDER');
    expect(body.textContent).not.toContain('Notes for the agent.');
    expect(body.textContent).not.toContain('"url"');
  });

  it('speaks the interface language', async () => {
    const el = mount(() => h(TToolCall, { toolCall: search() }), { locale: zhCn });
    expect(lineText(el)).toBe(`${zhCn.tools.webSearchDone} tauri deep link`);
    const body = await open(el);
    expect(body.textContent).toContain(zhCn.tools.results);
  });

  it('says it is searching while it runs, and that it could not when it failed', () => {
    const running = mount(() => h(TToolCall, { toolCall: search({ status: 'running', result: null }), streaming: true }));
    expect(lineText(running)).toBe(`${en.tools.webSearchRunning} tauri deep link`);
    expect(running.querySelector('[role="status"]')).not.toBeNull();
    app?.unmount();
    host?.remove();

    const failed = mount(() => h(TToolCall, { toolCall: search({ status: 'failed', result: null, error: 'quota' }) }), { locale: zhCn });
    expect(lineText(failed)).toBe(`${zhCn.tools.webSearchFailed} tauri deep link`);
    expect(failed.querySelector('[data-icon="lucide:x-circle"]')).not.toBeNull();
  });

  it('shows what the text says when it lists no pages, and survives text it cannot read', async () => {
    const el = mount(() => h(TToolCall, {
      toolCall: search({ input: {}, result: [{ type: 'text', text: 'Links: [{"broken": ' }] }),
    }));
    expect(el.querySelector('.t-tool-step__subject')).toBeNull();
    const body = await open(el);
    expect(body.textContent).toContain('Links: [{"broken":');
  });

  it("gives way to a product's renderer of the same name", () => {
    const Mine = defineComponent({ setup: () => () => h('div', { class: 'my-search' }) });
    const el = mount(() => h(TToolCall, { toolCall: search() }), {
      setup: (a) => a.provide(TOOL_RENDERERS_KEY, { WebSearch: Mine }),
    });
    expect(el.querySelector('.my-search')).not.toBeNull();
    expect(el.querySelector('.t-web-search')).toBeNull();
  });

  it("is not used for an MCP server's tool of the same bare name", () => {
    const el = mount(() => h(TToolCall, { toolCall: search({ name: 'mcp__acme__WebSearch' }) }));
    expect(el.querySelector('.t-web-search')).toBeNull();
    expect(el.querySelector('.t-tool-card')).not.toBeNull();
  });
});

describe('the CLI WebFetch', () => {
  const fetchCall = (patch: Partial<ToolCallInfo> = {}): ToolCallInfo => ({
    id: 'f1',
    name: 'WebFetch',
    status: 'completed',
    input: { url: 'https://www.tauri.app/start/', prompt: 'What does it say about deep links?' },
    result: [{ type: 'text', text: 'The page explains how to register a scheme.' }],
    ...patch,
  });

  it('says which site it read, and opens on the page address and what it found', async () => {
    const el = mount(() => h(TToolCall, { toolCall: fetchCall() }));
    expect(lineText(el)).toBe(`${en.tools.webFetchDone} tauri.app`);

    const body = await open(el);
    const link = body.querySelector('a.t-tool-links__row') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('https://www.tauri.app/start/');
    expect(link.getAttribute('target')).toBe('_blank');
    expect(link.getAttribute('rel')).toBe('noopener noreferrer');
    expect(link.textContent).toContain('www.tauri.app/start/');
    expect(body.textContent).toContain('The page explains how to register a scheme.');
    expect(body.textContent).not.toContain('What does it say about deep links?');
  });

  it('reads in Chinese, and keeps an address it cannot link as plain words', () => {
    const el = mount(() => h(TToolCall, { toolCall: fetchCall({ status: 'running', result: null, input: { url: 'ftp://files.example/a' } }), streaming: true }), { locale: zhCn });
    expect(lineText(el)).toBe(`${zhCn.tools.webFetchRunning} ftp://files.example/a`);
    expect(el.querySelector('a')).toBeNull();
  });
});

describe('the generic view', () => {
  it('reads the input as named lines and a JSON result as pages and lines, never as raw JSON', async () => {
    const el = mount(() => h(TToolCall, {
      toolCall: {
        name: 'web_search',
        status: 'completed',
        input: { query: 'tauri', max_results: 3 },
        output: JSON.stringify({ results: [{ title: 'Tauri', url: 'https://tauri.app/', snippet: 's' }], count: 1, query: 'tauri' }),
      },
    }));
    expect(lineText(el)).toBe('Web Search tauri');

    const body = await open(el);
    const names = Array.from(body.querySelectorAll('.t-tool-fields__name'), (n) => n.textContent);
    expect(names).toEqual(['Query', 'Max results', 'Count', 'Query']);
    expect(body.querySelector('a.t-tool-links__row')?.getAttribute('href')).toBe('https://tauri.app/');
    expect(body.textContent).not.toContain('{');
    expect(body.textContent).not.toContain('"results"');
  });

  it('folds long text output and says how much there is', async () => {
    const output = Array.from({ length: 30 }, (_, i) => `line ${i + 1}`).join('\n');
    const el = mount(() => h(TToolCall, { toolCall: { name: 'Bash', status: 'completed', input: { command: 'seq 30' }, output } }));
    const body = await open(el);
    const pre = body.querySelector('.t-tool-output pre') as HTMLElement;
    expect(pre.textContent?.split('\n')).toHaveLength(12);

    const toggle = body.querySelector('.t-tool-output__toggle') as HTMLButtonElement;
    expect(toggle.textContent?.trim()).toBe('Show all 30 lines');
    toggle.click();
    await nextTick();
    expect(pre.textContent?.split('\n')).toHaveLength(30);
    expect(toggle.textContent?.trim()).toBe(en.tools.showLess);
  });

  it('is a plain line, not a button, when there is nothing to open', () => {
    const el = mount(() => h(TToolCallCard, { toolCall: { name: 'ping', status: 'completed' } }));
    expect(el.querySelector('button')).toBeNull();
    expect(el.querySelector('.t-tool-step__chevron')).toBeNull();
    expect(el.querySelector('.sr-only')?.textContent).toBe(en.tools.completed);
  });

  it("does not take an inherited property for a renderer", () => {
    const el = mount(() => h(TToolCall, { toolCall: { name: 'constructor', status: 'completed' } }));
    expect(el.querySelector('.t-tool-card')).not.toBeNull();
  });
});

describe('a product renderer in the same shell', () => {
  it('draws its own line, body and the part that stays in view', async () => {
    const el = mount(() =>
      h(TToolStep, { state: 'completed', label: 'Ran', subject: 'npm test', mono: true, icon: 'lucide:terminal' }, {
        meta: () => 'on Office-PC',
        default: () => h(TToolOutput, { text: 'ok' }),
        after: () => h('a', { class: 'shot', href: '/files/1' }, 'screenshot.png'),
      }),
    );
    expect(el.querySelector('.t-tool-step__subject')?.classList.contains('is-mono')).toBe(true);
    expect(el.querySelector('.t-tool-step__meta')?.textContent).toBe('on Office-PC');
    expect(el.querySelector('[data-icon="lucide:terminal"]')).not.toBeNull();
    expect(el.querySelector('.shot')).not.toBeNull();
    expect(el.querySelector('.t-tool-step__body')).toBeNull();

    const body = await open(el);
    expect(body.textContent).toContain('ok');
    expect(el.querySelector('.shot')).not.toBeNull();
  });

  it('can start open and reports a toggle', async () => {
    const onToggle = vi.fn();
    const el = mount(() =>
      h(TToolStep, { state: 'failed', label: 'Ran', defaultOpen: true, onToggle }, { default: () => 'details' }),
    );
    expect(el.querySelector('.t-tool-step__body')?.textContent).toBe('details');
    (line(el) as HTMLButtonElement).click();
    await nextTick();
    expect(onToggle).toHaveBeenCalledWith(false);
    expect(el.querySelector('.t-tool-step__body')).toBeNull();
  });
});
