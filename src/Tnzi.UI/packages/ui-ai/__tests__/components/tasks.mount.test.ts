/**
 * Tasks of an external CLI agent mounted for real: sub-agents and background commands counted in their run's line
 * and listed when it opens; a sub-agent's own view (in a panel, or in place where there is none); the side panel's
 * list with elapsed time, Stop and the finished ones folded; the run line's images (preview on hover, viewer on click,
 * arrow keys and Escape, a tap opening the viewer at once) and its line counts; and `TChatApp` opening its side panel
 * on a sub-agent and putting it back.
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
const { default: TSubAgentPanel } = await import('../../src/components/reasoning/TSubAgentPanel.vue');
const { default: TBackgroundTasks } = await import('../../src/components/reasoning/TBackgroundTasks.vue');
const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');
const { createSubAgentView, provideSubAgentView } = await import('../../src/components/reasoning/sub-agent-view');
const { provideChatRenderContext } = await import('../../src/components/chat/chat-render-context');
const { createAiI18n } = await import('../../src/i18n/index');
const { en } = await import('../../src/locales/en');
const { zhCn } = await import('../../src/locales/zh-cn');

import type { ChatMessage, ToolCallInfo } from '../../src/headless/useChat';
import type { SubAgentView } from '../../src/components/reasoning/sub-agent-view';
import type { AiLocaleMessages } from '../../src/locales/en';

let app: App | null = null;
let host: HTMLElement | null = null;

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
  vi.useRealTimers();
});

interface MountOptions {
  locale?: AiLocaleMessages;
  view?: SubAgentView;
  resolve?: (id: string) => Promise<string | null>;
  slots?: Record<string, (scope: never) => unknown>;
}

async function mount(component: unknown, props: () => Record<string, unknown>, options: MountOptions = {}): Promise<HTMLElement> {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp(defineComponent({
    setup() {
      if (options.locale) createAiI18n(options.locale);
      if (options.view) provideSubAgentView(options.view);
      if (options.resolve) provideChatRenderContext({ toolRenderers: {}, resolveAttachmentUrl: options.resolve });
      return () => h(component as never, props(), options.slots);
    },
  }));
  app.mount(host);
  await settle();
  return host;
}

async function settle(): Promise<void> {
  for (let i = 0; i < 6; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

const lead = 'Starting.';
const bg = (id: string, status: string, description: string, patch: Partial<NonNullable<ToolCallInfo['task']>> = {}): ToolCallInfo => ({
  id, name: 'Bash', status: 'completed', textOffset: lead.length,
  input: { command: `pnpm ${id}`, run_in_background: true },
  task: { taskId: `t-${id}`, kind: 'command', background: true, status, description, ...patch },
});
const agent = (id: string, patch: Partial<NonNullable<ToolCallInfo['task']>> = {}, call: Partial<ToolCallInfo> = {}): ToolCallInfo => ({
  id, name: 'Agent', status: 'completed', textOffset: lead.length, ...call,
  task: { taskId: `t-${id}`, kind: 'agent', status: 'running', description: `Agent ${id}`, steps: [], ...patch },
});
const read = (id: string, parentId?: string, image?: string): ToolCallInfo => ({
  id, name: 'Read', status: 'completed',
  ...(parentId ? { parentId } : { textOffset: lead.length }),
  input: { file_path: `/w/${id}.png` },
  result: image ? [{ type: 'image', mediaType: 'image/png', attachmentId: image }] : [{ type: 'text', text: 'ok' }],
});

const PROMPT = 'Find every reader. '.repeat(30).trim();
const outer = agent('a1', {
  agentType: 'Explore', model: 'claude-haiku-5-5', prompt: PROMPT, startedAt: '2026-11-09T15:00:00Z', toolUseCount: 2,
  activity: 'Reading the adapter', result: 'Found **two** readers.',
  steps: [{ type: 'text', text: 'Looking around.' }, { type: 'tool', toolCallId: 'r1' }, { type: 'tool', toolCallId: 'a2' }],
});
const nested = agent('a2', { description: 'Nested survey', status: 'completed', durationMs: 3_000, result: 'Nested report.' }, { parentId: 'a1', textOffset: null });

const answer = (toolCalls: ToolCallInfo[], patch: Partial<ChatMessage> = {}): ChatMessage => ({
  id: 'm1', role: 'assistant', createdAt: '2026-11-09T15:00:00Z', content: `${lead}\n\nDone.`, status: 'done', toolCalls, ...patch,
});

const runLine = (el: HTMLElement, i = 0) => el.querySelectorAll<HTMLButtonElement>('.t-tool-run__line')[i]!;
const lineText = (el: HTMLElement, i = 0) => (runLine(el, i).querySelector('.t-tool-run__label')?.textContent ?? '').trim();

describe('tasks in a run', () => {
  it('are counted in the run’s line, and listed in their own rows once it opens', async () => {
    const el = await mount(TThreadMessage, () => ({
      message: answer([bg('b1', 'completed', 'Unit tests'), bg('b2', 'completed', 'Lint'), bg('b3', 'running', 'Watch'), outer, read('r1', 'a1'), nested]),
    }));

    expect(lineText(el)).toBe('Finished 2 background commands, a background command running, an agent running');
    expect(el.querySelectorAll('.t-tool-run__line')).toHaveLength(1);

    runLine(el).click();
    await settle();
    expect(el.querySelectorAll('[data-sub-agent-step]')).toHaveLength(1);
    const commands = Array.from(el.querySelectorAll('.t-background-command'));
    expect(commands.map((c) => c.className)).toEqual([
      expect.stringContaining('is-completed'), expect.stringContaining('is-completed'), expect.stringContaining('is-running'),
    ]);
    expect(commands[2]!.querySelector('.t-background-command__title')?.textContent).toBe('Watch');
  });

  it('open a sub-agent in place where no panel shows it', async () => {
    const el = await mount(TThreadMessage, () => ({ message: answer([outer, read('r1', 'a1'), nested]) }));
    runLine(el).click();
    await settle();

    const row = el.querySelector<HTMLButtonElement>('[data-sub-agent-step]')!;
    expect(row.getAttribute('aria-expanded')).toBe('false');
    row.click();
    await settle();
    await vi.dynamicImportSettled();
    await settle();

    expect(row.getAttribute('aria-expanded')).toBe('true');
    const inline = el.querySelector('.t-sub-agent-panel.is-embedded')!;
    expect(inline.textContent).toContain('Looking around.');
    expect(inline.querySelector('.t-sub-agent-panel__header')).toBeNull();
  });

  it('open a sub-agent in the panel the view places', async () => {
    const view = createSubAgentView({ panel: () => true });
    const el = await mount(TThreadMessage, () => ({ message: answer([outer, read('r1', 'a1')]) }), { view });
    runLine(el).click();
    await settle();

    el.querySelector<HTMLButtonElement>('[data-sub-agent-step]')!.click();
    await settle();

    expect(view.current.value).toBe('a1');
    expect(el.querySelector('.t-sub-agent-panel')).toBeNull();
    expect(el.querySelector('[data-sub-agent-step]')?.getAttribute('aria-pressed')).toBe('true');
  });
});

describe('a sub-agent’s own panel', () => {
  const messages = [answer([outer, read('r1', 'a1'), nested])];

  it('shows its task, state, model, folded prompt, steps and report', async () => {
    const view = createSubAgentView({ stopTask: () => async () => true });
    view.open('a1');
    const el = await mount(TSubAgentPanel, () => ({ messages }), { view });

    expect(el.querySelector('.t-sub-agent-panel__title')?.textContent).toBe('Agent a1');
    expect(el.querySelector('.t-sub-agent-panel__status')?.textContent).toContain(en.tasks.running);
    expect(el.querySelector('.t-sub-agent-panel__chip')?.textContent).toBe('Explore');
    expect(el.querySelector('.t-sub-agent-panel__model')?.textContent).toBe('claude-haiku-5-5');
    expect(el.querySelector('.t-sub-agent-panel__activity')?.textContent?.trim()).toBe('Reading the adapter');

    const prompt = el.querySelector('.t-sub-agent-panel__prompt-text')!;
    expect(prompt.classList.contains('is-folded')).toBe(true);
    const more = el.querySelector<HTMLButtonElement>('.t-sub-agent-panel__more')!;
    expect(more.textContent?.trim()).toBe(en.tasks.showMore);
    more.click();
    await settle();
    expect(prompt.classList.contains('is-folded')).toBe(false);
    expect(more.getAttribute('aria-expanded')).toBe('true');

    const steps = el.querySelector('.t-sub-agent-panel__steps')!;
    expect(steps.textContent).toContain('Looking around.');
    expect(steps.querySelectorAll('.t-tool-run')).toHaveLength(1);
    expect(steps.querySelector('[data-sub-agent-step]')?.textContent).toContain('Nested survey');
    expect(el.querySelector('.t-sub-agent-panel__result strong')?.textContent).toBe('two');
  });

  it('stops the sub-agent, says it is stopping, and closes', async () => {
    let resolveStop: (ok: boolean) => void = () => {};
    const stopTask = vi.fn(() => new Promise<boolean>((r) => (resolveStop = r)));
    const view = createSubAgentView({ stopTask: () => stopTask });
    view.open('a1');
    const onClose = vi.fn();
    const el = await mount(TSubAgentPanel, () => ({ messages, onClose }), { view });

    const stop = el.querySelector<HTMLButtonElement>('.t-sub-agent-panel__stop')!;
    expect(stop.getAttribute('aria-label')).toBe('Stop Agent a1');
    stop.click();
    await settle();
    expect(stopTask).toHaveBeenCalledWith('t-a1');
    expect(stop.disabled).toBe(true);
    expect(stop.textContent).toContain(en.tasks.stopping);
    resolveStop(true);
    await settle();

    el.querySelector<HTMLButtonElement>(`.t-sub-agent-panel__header button[aria-label="${en.tasks.close}"]`)!.click();
    await settle();
    expect(view.current.value).toBeNull();
    expect(onClose).toHaveBeenCalledOnce();
  });

  it('shows each step by its own outcome while the sub-agent works on, and only a step still going as going', async () => {
    const working = agent('a3', { steps: [{ type: 'tool', toolCallId: 'r3' }, { type: 'tool', toolCallId: 'r4' }] });
    const failedRead: ToolCallInfo = { ...read('r4', 'a3'), status: 'failed', error: 'No such file' };
    const view = createSubAgentView({ panel: () => true });
    view.open('a3');
    const state = reactive({ calls: [working, read('r3', 'a3'), failedRead] as ToolCallInfo[] });
    const el = await mount(TSubAgentPanel, () => ({ messages: [answer(state.calls)] }), { view });

    const run = el.querySelector('.t-sub-agent-panel__steps .t-tool-run')!;
    expect(el.querySelector('.t-sub-agent-panel__status')?.textContent).toContain(en.tasks.running);
    expect(run.classList.contains('is-active')).toBe(false);
    expect(run.querySelector('.t-tool-run__label.is-live')).toBeNull();
    expect(lineText(el)).toContain(en.tools.readFile);
    expect(run.querySelector('.t-tool-run__mark--failed')).not.toBeNull();

    runLine(el).click();
    await settle();
    const rows = Array.from(run.querySelectorAll('.t-tool-run__row'));
    expect(rows).toHaveLength(2);
    const stateOf = (row: Element) => ['running', 'completed', 'failed', 'pending'].find((state) => row.querySelector(`.t-tool-step--${state}`));
    expect(rows.map(stateOf)).toEqual(['completed', 'failed']);
    expect(run.querySelector('.t-tool-step--running')).toBeNull();

    // A step that has not returned yet is the one going
    state.calls = [
      { ...working, task: { ...working.task!, steps: [...working.task!.steps!, { type: 'tool', toolCallId: 'r5' }] } },
      read('r3', 'a3'), failedRead, { ...read('r5', 'a3'), status: 'running', result: undefined },
    ];
    await settle();
    const live = el.querySelector('.t-sub-agent-panel__steps .t-tool-run')!;
    expect(live.classList.contains('is-active')).toBe(true);
    expect(live.querySelector('.t-tool-run__label.is-live')?.textContent).toBe(en.tools.readingFile);
  });

  it('opens a nested sub-agent with a way back, and says its steps are not streamed', async () => {
    const view = createSubAgentView({ panel: () => true });
    view.open('a1');
    const el = await mount(TSubAgentPanel, () => ({ messages }), { view });

    el.querySelector<HTMLButtonElement>('.t-sub-agent-panel__steps [data-sub-agent-step]')!.click();
    await settle();
    expect(view.current.value).toBe('a2');
    expect(el.querySelector('.t-sub-agent-panel__title')?.textContent).toBe('Nested survey');
    expect(el.querySelector('.t-sub-agent-panel__note')?.textContent).toBe(en.tasks.nestedSteps);
    expect(el.querySelector('.t-sub-agent-panel__stop')).toBeNull();

    el.querySelector<HTMLButtonElement>(`button[aria-label="${en.tasks.back}"]`)!.click();
    await settle();
    expect(view.current.value).toBe('a1');
  });
});

describe('the side panel’s list of background tasks', () => {
  const conversation = (): ChatMessage[] => [
    answer([
      bg('b1', 'completed', 'Unit tests', { durationMs: 52_000 }),
      bg('b2', 'failed', 'Lint', { durationMs: 9_000 }),
      { ...outer, task: { ...outer.task!, startedAt: '2026-11-09T15:00:00Z', durationMs: null } },
      bg('b3', 'running', 'Watch', { startedAt: '2026-11-09T15:04:00Z' }),
    ]),
  ];

  it('lists what runs with its time and a Stop square, and folds the finished ones', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] });
    vi.setSystemTime(new Date('2026-11-09T15:06:18Z'));
    const stopTask = vi.fn(async () => true);
    const view = createSubAgentView({ stopTask: () => stopTask });
    const el = await mount(TBackgroundTasks, () => ({ messages: conversation() }), { view });

    expect(el.querySelector('.t-background-tasks__title')?.textContent).toBe(en.tasks.listTitle);
    const rows = Array.from(el.querySelectorAll('.t-background-tasks__row.is-running'));
    expect(rows.map((r) => r.querySelector('.t-background-tasks__name')?.textContent)).toEqual(['Agent a1', 'Watch']);
    expect(rows.map((r) => r.querySelector('.t-background-tasks__time')?.textContent)).toEqual(['6m', '2m']);
    expect(rows[0]!.querySelector('[data-icon="lucide:bot"]')).not.toBeNull();
    expect(rows[1]!.querySelector('[data-icon="lucide:terminal"]')).not.toBeNull();

    const toggle = el.querySelector<HTMLButtonElement>('.t-background-tasks__toggle')!;
    expect(toggle.textContent?.trim()).toBe('2 finished');
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    toggle.click();
    await settle();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(Array.from(el.querySelectorAll('.t-background-tasks__row:not(.is-running) .t-background-tasks__time')).map((t) => t.textContent))
      .toEqual(['52s', '9s']);

    el.querySelectorAll<HTMLButtonElement>('.t-background-tasks__stop')[1]!.click();
    await settle();
    expect(stopTask).toHaveBeenCalledWith('t-b3');

    rows[0]!.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await settle();
    expect(view.current.value).toBe('a1');
  });

  it('speaks the interface language, and offers no Stop without a way to stop', async () => {
    const el = await mount(TBackgroundTasks, () => ({ messages: conversation() }), { locale: zhCn });

    expect(el.querySelector('.t-background-tasks__title')?.textContent).toBe('后台任务');
    expect(el.querySelector('.t-background-tasks__toggle')?.textContent?.trim()).toBe('2 个已结束');
    expect(el.querySelector('.t-background-tasks__stop')).toBeNull();
  });
});

describe('the run line’s images and line counts', () => {
  const images: Record<string, string> = { 'img-1': 'data:image/png;base64,AAAA', 'img-2': 'data:image/png;base64,BBBB' };
  const resolve = async (id: string) => images[id] ?? null;
  const editCall: ToolCallInfo = {
    id: 'e1', name: 'Edit', status: 'completed', textOffset: lead.length,
    input: { file_path: '/w/a.ts', old_string: 'a\nb', new_string: 'a\nc\nd' },
  };

  it('shows the images and counts without opening the run', async () => {
    const el = await mount(TThreadMessage, () => ({ message: answer([read('r1', undefined, 'img-1'), editCall, read('r2', undefined, 'img-2')]) }), { resolve });

    expect(el.querySelector('.t-tool-run__added')?.textContent).toBe('+2');
    expect(el.querySelector('.t-tool-run__removed')?.textContent).toBe('-1');
    expect(el.querySelector('.t-tool-run__added')?.getAttribute('aria-label')).toBe('2 lines added');
    const thumbs = Array.from(el.querySelectorAll<HTMLButtonElement>('[data-run-image]'));
    expect(thumbs.map((t) => t.getAttribute('aria-label'))).toEqual(['Read a file · 1 of 2', 'Read a file · 2 of 2']);
    expect(thumbs.map((t) => t.querySelector('img')?.getAttribute('src'))).toEqual([images['img-1'], images['img-2']]);
    expect(el.querySelector('.t-tool-run__box')?.getAttribute('style')).toContain('display: none');
  });

  it('previews on hover, opens the viewer on a click, steps with the arrow keys and closes on Escape', async () => {
    const el = await mount(TThreadMessage, () => ({ message: answer([read('r1', undefined, 'img-1'), read('r2', undefined, 'img-2')]) }), { resolve });
    const thumbs = Array.from(el.querySelectorAll<HTMLButtonElement>('[data-run-image]'));

    thumbs[1]!.dispatchEvent(new PointerEvent('pointerenter', { pointerType: 'mouse' }));
    await settle();
    const preview = document.querySelector('[data-run-image-preview]')!;
    expect(preview.querySelector('.t-run-images__source')?.textContent).toBe(en.tools.readFile);
    expect(preview.textContent).toContain('2 of 2');
    expect(preview.querySelector('img')?.getAttribute('src')).toBe(images['img-2']);

    thumbs[0]!.click();
    await settle();
    await vi.dynamicImportSettled();
    await settle();
    const viewer = document.querySelector<HTMLElement>('[data-image-viewer]')!;
    expect(viewer).not.toBeNull();
    expect(document.querySelector('[data-run-image-preview]')).toBeNull();
    expect(viewer.querySelector('img')?.getAttribute('src')).toBe(images['img-1']);

    viewer.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    await settle();
    expect(document.querySelector('[data-image-viewer] img')?.getAttribute('src')).toBe(images['img-2']);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    viewer.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();
    expect(document.querySelector('[data-image-viewer]')).toBeNull();
  });

  it('names the step in the interface’s language, as the run lists it', async () => {
    const el = await mount(TThreadMessage, () => ({ message: answer([read('r1', undefined, 'img-1'), read('r2', undefined, 'img-2')]) }), { resolve, locale: zhCn });
    const thumbs = Array.from(el.querySelectorAll<HTMLButtonElement>('[data-run-image]'));

    thumbs[0]!.dispatchEvent(new PointerEvent('pointerenter', { pointerType: 'mouse' }));
    await settle();

    const preview = document.querySelector('[data-run-image-preview]')!;
    expect(preview.querySelector('.t-run-images__source')?.textContent).toBe(zhCn.tools.readFile);
    expect(preview.textContent).not.toContain('Read');
    expect(thumbs[0]!.getAttribute('aria-label')).toBe(`${zhCn.tools.readFile} · 第 1 张，共 2 张`);
  });

  it('shows no preview for a touch, where a tap opens the viewer', async () => {
    const el = await mount(TThreadMessage, () => ({ message: answer([read('r1', undefined, 'img-1')]) }), { resolve });
    const thumb = el.querySelector<HTMLButtonElement>('[data-run-image]')!;

    thumb.dispatchEvent(new PointerEvent('pointerenter', { pointerType: 'touch' }));
    await settle();
    expect(document.querySelector('[data-run-image-preview]')).toBeNull();

    thumb.click();
    await settle();
    await vi.dynamicImportSettled();
    await settle();
    expect(document.querySelector('[data-image-viewer]')).not.toBeNull();
  });
});

describe('TChatApp and a sub-agent', () => {
  it('opens its side panel on the sub-agent’s tab, and puts it back when the view closes', async () => {
    const state = reactive({ tab: '' });
    const el = await mount(TChatApp, () => ({
      sidebarStorageKey: null,
      asideStorageKey: null,
      autoApplyTheme: false,
      activeThreadId: 't1',
      messages: [{ id: 'u1', role: 'user', content: 'go', createdAt: '2026-11-09T15:00:00Z' }, answer([outer, read('r1', 'a1')])],
      subAgentAside: 'sub-agent',
      asideDefaultTab: 'tasks',
      stopTask: null,
    }), {
      slots: {
        aside: ((scope: { tab: string }) => {
          state.tab = scope.tab;
          return scope.tab === 'sub-agent' ? h(TSubAgentPanel, { messages: [answer([outer, read('r1', 'a1')])] }) : h('div', { class: 'tasks-tab' });
        }) as never,
      },
    });

    expect(el.querySelector('.t-sub-agent-panel')).toBeNull();
    runLine(el).click();
    await settle();
    el.querySelector<HTMLButtonElement>('[data-sub-agent-step]')!.click();
    await settle();

    expect(state.tab).toBe('sub-agent');
    expect(el.querySelector('.t-sub-agent-panel__title')?.textContent).toBe('Agent a1');
    expect(el.querySelector('.t-sub-agent-panel__stop')).toBeNull();

    el.querySelector<HTMLButtonElement>(`.t-sub-agent-panel__header button[aria-label="${en.tasks.close}"]`)!.click();
    await settle();
    expect(el.querySelector('.t-sub-agent-panel')).toBeNull();
    expect(el.querySelector('.t-chat-app__aside, .t-aside-panel')).toBeNull();
  });
});
