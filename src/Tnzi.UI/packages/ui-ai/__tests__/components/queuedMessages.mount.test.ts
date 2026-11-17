/**
 * The queued messages over the composer, mounted for real: each message says where it stands (held
 * ones say why, ones with the agent say so and have no actions) and lists its attachments, edits in
 * place with Enter / Escape and gives focus back, removes, and sends now: "Send now" when the turn
 * takes it without stopping, "Stop and send" when it does not; the list folds to its title line and
 * opens again. `TChatApp` lists a provided queue between the app's own content and the cards, and
 * queues through `send` (attachments included) while a server run goes on.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, shallowRef, type App, type Component } from 'vue';
import type { QueuedMessageDto } from '@tnzi/core/services/ai';
import TQueuedMessages from '../../src/components/chat/TQueuedMessages.vue';
import { createMessageQueue, type MessageQueue } from '../../src/headless/message-queue';
import { provideChatThreads } from '../../src/headless/chat-approvals-key';
import { createApprovalQueue } from '../../src/headless/approval-queue';
import type { ChatMessage } from '../../src/headless/useChat';
import { en } from '../../src/locales/en';

const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');

let app: App | null = null;
let host: HTMLElement | null = null;

function mount(component: Component, props: () => Record<string, unknown>, setup?: () => void): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp(defineComponent({
    setup: () => {
      setup?.();
      return () => h(component, props());
    },
  }));
  app.mount(host);
  return host;
}

async function settle(): Promise<void> {
  for (let i = 0; i < 6; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
});

const ok = <T>(data: T) => ({ succeeded: true, success: true, code: 200, data });

function message(id: string, patch: Partial<QueuedMessageDto> = {}): QueuedMessageDto {
  return { id, threadId: 't-1', text: `words of ${id}`, state: 'Queued', sendNow: false, afterRunId: 'r-1', queuedTime: '', ...patch };
}

/** A real queue over a fake server, following conversation t-1. */
async function queueOf(
  list: QueuedMessageDto[],
  running = true,
): Promise<{ queue: MessageQueue; api: Record<string, ReturnType<typeof vi.fn>>; setServer: (next: QueuedMessageDto[]) => void }> {
  let server = [...list];
  const api = {
    getQueue: vi.fn(async () => ok([...server])),
    queueMessage: vi.fn(),
    updateQueuedMessage: vi.fn(async (_t: string, id: string, text: string) => {
      server = server.map((m) => (m.id === id ? { ...m, text } : m));
      return ok(server.find((m) => m.id === id)!);
    }),
    removeQueuedMessage: vi.fn(async (_t: string, id: string) => {
      server = server.filter((m) => m.id !== id);
      return ok(undefined);
    }),
    sendQueuedMessageNow: vi.fn(async (_t: string, id: string) => ok({ ...server.find((m) => m.id === id)!, sendNow: true, state: 'Queued' as const })),
  };
  const queue = createMessageQueue({ api: api as never, intervalMs: 0 });
  queue.follow('t-1', running, running);
  await vi.waitFor(() => expect(api.getQueue).toHaveBeenCalled());
  await vi.waitFor(() => expect(queue.items.value).toHaveLength(list.filter((m) => m.state !== 'Delivered').length));
  return { queue, api, setServer: (next) => (server = [...next]) };
}

const items = (el: HTMLElement) => [...el.querySelectorAll<HTMLElement>('[data-queued-message]')];
const lineOf = (item: HTMLElement) => item.querySelector('[data-queued-line]')?.textContent?.trim() ?? null;

describe('TQueuedMessages', () => {
  it('lists the messages with where each stands, and says held ones wait for the user', async () => {
    const { queue } = await queueOf([
      message('next', { sendNow: true }),
      message('wait'),
      message('going', { state: 'Delivering' }),
      message('stopped', { state: 'Held' }),
      message('refused', { state: 'Held', failure: 'The model is not allowed.', failureCode: 'AI_MODEL_NOT_ALLOWED' }),
      message('odd', { state: 'Held', failure: 'Some reason from the server.' }),
    ]);
    const el = mount(TQueuedMessages, () => ({ queue }));
    await settle();

    const region = el.querySelector('[data-queued-messages]')!;
    expect(region.getAttribute('role')).toBe('region');
    expect(region.getAttribute('aria-label')).toBe(en.queue.region);
    expect(region.querySelector('strong')!.textContent).toBe('3 queued');
    expect(region.textContent).toContain(en.queue.hint);

    const lines = items(el).map(lineOf);
    expect(lines).toEqual([
      en.queue.next,
      null,
      en.queue.sending,
      en.queue.stopped,
      `Not sent: ${en.queue.reasonModel}`,
      'Not sent: Some reason from the server.',
    ]);
    expect(items(el)[4]!.classList.contains('is-failed')).toBe(true);
    expect(items(el)[2]!.querySelector('[data-queued-send-now]')).toBeNull();
  });

  it('edits in place: Enter saves, and focus goes back to the Edit button', async () => {
    const { queue, api } = await queueOf([message('a')]);
    const el = mount(TQueuedMessages, () => ({ queue }));
    await settle();

    el.querySelector<HTMLButtonElement>('[data-queued-edit]')!.click();
    await settle();
    const editor = el.querySelector<HTMLTextAreaElement>('[data-queued-editor]')!;
    expect(document.activeElement).toBe(editor);
    expect(editor.getAttribute('aria-label')).toBe(en.queue.editLabel);

    editor.value = '  better words ';
    editor.dispatchEvent(new Event('input'));
    editor.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    await settle();

    expect(api.updateQueuedMessage).toHaveBeenCalledWith('t-1', 'a', 'better words');
    expect(el.querySelector('[data-queued-editor]')).toBeNull();
    expect(el.querySelector('.t-queued-messages__text')!.textContent).toBe('better words');
    expect(document.activeElement).toBe(el.querySelector('[data-queued-edit]'));
  });

  it('Escape leaves the editor without saving; an input method commit does not save', async () => {
    const { queue, api } = await queueOf([message('a')]);
    const el = mount(TQueuedMessages, () => ({ queue }));
    await settle();
    el.querySelector<HTMLButtonElement>('[data-queued-edit]')!.click();
    await settle();
    const editor = el.querySelector<HTMLTextAreaElement>('[data-queued-editor]')!;
    editor.value = 'draft';
    editor.dispatchEvent(new Event('input'));
    editor.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', isComposing: true, bubbles: true, cancelable: true }));
    await settle();
    expect(api.updateQueuedMessage).not.toHaveBeenCalled();

    editor.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    await settle();
    expect(el.querySelector('[data-queued-editor]')).toBeNull();
    expect(api.updateQueuedMessage).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(el.querySelector('[data-queued-edit]'));
  });

  it('removes, and sends now: the button says whether that stops the turn', async () => {
    const { queue, api } = await queueOf([message('a', { sendNowStopsTurn: true }), message('b', { sendNowStopsTurn: false })]);
    const el = mount(TQueuedMessages, () => ({ queue }));
    await settle();

    const stopAndSend = items(el)[0]!.querySelector<HTMLButtonElement>('[data-queued-send-now]')!;
    expect(stopAndSend.textContent!.trim()).toBe(en.queue.stopAndSend);
    expect(stopAndSend.getAttribute('aria-description')).toBe(en.queue.stopAndSendHint);
    expect(stopAndSend.dataset.stopsTurn).toBe('true');
    const sendNow = items(el)[1]!.querySelector<HTMLButtonElement>('[data-queued-send-now]')!;
    expect(sendNow.textContent!.trim()).toBe(en.queue.sendNow);
    expect(sendNow.getAttribute('aria-description')).toBe(en.queue.sendNowHint);
    expect(sendNow.dataset.stopsTurn).toBeUndefined();
    sendNow.click();
    await settle();
    expect(api.sendQueuedMessageNow).toHaveBeenCalledWith('t-1', 'b');
    expect(items(el)[0]!.dataset.queuedId).toBe('b');

    items(el)[1]!.querySelector<HTMLButtonElement>('[data-queued-remove]')!.click();
    await settle();
    expect(api.removeQueuedMessage).toHaveBeenCalledWith('t-1', 'a');
    expect(items(el).map((i) => i.dataset.queuedId)).toEqual(['b']);
  });

  it('a message with the agent says so and has no actions; one it took in is not listed; attachments are listed by name', async () => {
    const { queue } = await queueOf([
      message('handed', { state: 'HandedOver', handedToRunId: 'r-1' }),
      message('taken', { state: 'Delivered', handedToRunId: 'r-1', threadMessageId: 'u-9' }),
      message('files', {
        text: '',
        sendNowStopsTurn: false,
        attachments: [
          { id: 'f-1', origin: 'Upload', mediaType: 'image/png', fileName: 'shot.png', size: 3 },
          { id: 'f-2', origin: 'Upload', mediaType: 'message/rfc822', fileName: 'invoice.eml', size: 9 },
        ],
      } as Partial<QueuedMessageDto>),
    ]);
    const el = mount(TQueuedMessages, () => ({ queue }));
    await settle();

    expect(items(el).map((i) => i.dataset.queuedId)).toEqual(['handed', 'files']);
    expect(items(el).map(lineOf)).toEqual([en.queue.handed, null]);
    expect(items(el)[0]!.querySelector('[data-queued-send-now], [data-queued-edit], [data-queued-remove]')).toBeNull();
    const files = items(el)[1]!.querySelector('[data-queued-attachments]')!;
    expect(files.getAttribute('aria-label')).toBe(en.queue.attachments);
    expect([...files.querySelectorAll('li')].map((li) => li.textContent!.trim())).toEqual(['shot.png', 'invoice.eml']);
    expect(el.querySelector('strong')!.textContent).toBe('2 queued');
    expect(el.textContent).toContain(en.queue.hintToAgent);
  });

  it('a message the agent took in leaves the card, and the card goes when nothing else is queued', async () => {
    const { queue, setServer } = await queueOf([message('a', { state: 'HandedOver', handedToRunId: 'r-1' })]);
    const el = mount(TQueuedMessages, () => ({ queue }));
    await settle();
    expect(items(el).map(lineOf)).toEqual([en.queue.handed]);

    // The agent took it in: it is a user message of the conversation now, while the server keeps the row until the turn ends.
    setServer([message('a', { state: 'Delivered', handedToRunId: 'r-1', threadMessageId: 'u-1' })]);
    await queue.refresh();
    await settle();
    expect(el.querySelector('[data-queued-messages]')).toBeNull();
  });

  it('a queue holding only messages the agent took in shows no card', async () => {
    const { queue } = await queueOf([message('a', { state: 'Delivered', handedToRunId: 'r-1', threadMessageId: 'u-1' })]);
    const el = mount(TQueuedMessages, () => ({ queue }));
    await settle();
    expect(el.querySelector('[data-queued-messages]')).toBeNull();
  });

  it('a message the agent did not take in says so', async () => {
    const { queue } = await queueOf([message('a', { state: 'Held', failure: 'declined', failureCode: 'AI_QUEUED_MESSAGE_NOT_TAKEN' })]);
    const el = mount(TQueuedMessages, () => ({ queue }));
    await settle();
    expect(lineOf(items(el)[0]!)).toBe(`Not sent: ${en.queue.reasonNotTaken}`);
  });

  it('Send now is the secondary button in a light and in a dark page alike', async () => {
    for (const dark of [false, true]) {
      document.documentElement.classList.toggle('dark', dark);
      const { queue } = await queueOf([message('a'), message('b', { state: 'Held' })], dark);
      const el = mount(TQueuedMessages, () => ({ queue }));
      await settle();
      for (const button of el.querySelectorAll<HTMLButtonElement>('[data-queued-send-now]')) {
        expect(button.classList.contains('t-button--secondary')).toBe(true);
        expect(button.classList.contains('t-button--primary')).toBe(false);
      }
      app?.unmount();
      host?.remove();
    }
    document.documentElement.classList.remove('dark');
  });

  it('with no turn running a held message offers plain Send', async () => {
    const { queue } = await queueOf([message('a', { state: 'Held' })], false);
    const el = mount(TQueuedMessages, () => ({ queue }));
    await settle();
    const send = el.querySelector<HTMLButtonElement>('[data-queued-send-now]')!;
    expect(send.textContent!.trim()).toBe(en.queue.send);
    expect(send.hasAttribute('aria-description')).toBe(false);
    expect(el.textContent).not.toContain(en.queue.hint);
  });

  it('folds to its title line, and opens again', async () => {
    const { queue } = await queueOf([message('a')]);
    const el = mount(TQueuedMessages, () => ({ queue, folded: true }));
    await settle();
    const toggle = el.querySelector<HTMLButtonElement>('[data-queued-toggle]')!;
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(el.querySelector<HTMLElement>('.t-queued-messages__list')!.style.display).toBe('none');

    toggle.click();
    await settle();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(el.querySelector<HTMLElement>('.t-queued-messages__list')!.style.display).toBe('');
  });

  it('is not drawn when nothing is queued', async () => {
    const { queue } = await queueOf([]);
    const el = mount(TQueuedMessages, () => ({ queue }));
    await settle();
    expect(el.querySelector('[data-queued-messages]')).toBeNull();
  });
});

describe('TChatApp with a message queue', () => {
  const messages: ChatMessage[] = [
    { id: 'u1', role: 'user', content: 'Refactor', createdAt: '' },
    { id: 'a1', role: 'assistant', content: 'Working on it', createdAt: '', isStreaming: true, status: 'streaming' },
  ];
  const shell = (extra: Record<string, unknown> = {}, setup?: () => void) =>
    mount(TChatApp, () => ({ sidebarStorageKey: null, autoApplyTheme: false, activeThreadId: 't-1', messages, isStreaming: true, ...extra }), setup);
  const primary = (el: HTMLElement) =>
    el.querySelector('.t-thread-composer__send:not(.t-thread-composer__side-stop)') as HTMLButtonElement;

  /** What a `useChatThreads` in the component above the shell registers: its messages, cards and queue. */
  const register = (queue: MessageQueue, shown: readonly ChatMessage[] = messages) => () =>
    provideChatThreads({ messages: shallowRef(shown), approvals: createApprovalQueue({ sources: [] }), messageQueue: queue });

  it('lists the registered queue above the composer, between the app\'s content and the cards', async () => {
    const { queue } = await queueOf([message('a')]);
    const approvals = createApprovalQueue({ sources: [] });
    const el = mount(
      TChatApp,
      () => ({ sidebarStorageKey: null, autoApplyTheme: false, activeThreadId: 't-1', messages, isStreaming: true, approvals }),
      register(queue),
    );
    await settle();
    const top = el.querySelector('.t-thread-composer__top')!;
    expect(top.querySelector('[data-queued-messages]')).not.toBeNull();
    expect(top.nextElementSibling?.classList.contains('t-thread-composer')).toBe(true);
  });

  it('with two conversations registered, lists the queue of the one on screen', async () => {
    const { queue: shownQueue } = await queueOf([message('mine')]);
    const { queue: otherQueue } = await queueOf([message('theirs'), message('also-theirs')]);
    const other: ChatMessage[] = [{ id: 'x1', role: 'user', content: 'elsewhere', createdAt: '' }];
    const el = shell({}, () => {
      register(otherQueue, other)();
      register(shownQueue)();
    });
    await settle();
    expect(items(el).map((i) => i.dataset.queuedId)).toEqual(['mine']);
  });

  it('while the server run goes on, the composer queues through `send`, not `queue`', async () => {
    const { queue } = await queueOf([]);
    const send = vi.fn();
    const queued = vi.fn();
    const el = shell({ inputText: 'and run the tests', onSend: send, onQueue: queued, messageQueue: queue });
    await settle();
    expect(primary(el).dataset.action).toBe('queue');

    primary(el).click();
    await settle();
    expect(send).toHaveBeenCalledWith('and run the tests', []);
    expect(queued).not.toHaveBeenCalled();
  });

  it('passes the composer a queue that takes attachments too, and only while the server run goes on', async () => {
    const { queue } = await queueOf([]);
    const el = shell({ inputText: 'look', messageQueue: queue });
    await settle();
    // The composer's own rule (`queueAttachments`) is pinned in composerQueue.mount.test.ts; the shell leaves it on.
    expect(el.querySelector('[data-queue-attachments="false"]')).toBeNull();
    expect(primary(el).dataset.action).toBe('queue');
    queue.follow('t-1', true, false);
    await settle();
    expect(primary(el).dataset.action).toBe('stop');
  });

  it('`messageQueue: null` turns it off, and a built-in turn is not queued behind', async () => {
    const { queue } = await queueOf([message('a')]);
    const off = shell({ inputText: 'x', messageQueue: null }, register(queue));
    await settle();
    expect(off.querySelector('[data-queued-messages]')).toBeNull();
    expect(primary(off).dataset.action).toBe('stop');
    app?.unmount();

    queue.follow('t-1', true, false);
    const builtIn = shell({ inputText: 'x', messageQueue: queue });
    await settle();
    expect(primary(builtIn).dataset.action).toBe('stop');
    expect(builtIn.querySelector('[data-queued-messages]')).not.toBeNull();
  });

  it('with a card waiting, the queue folds to its title line', async () => {
    const { queue } = await queueOf([message('a')]);
    const approvals = createApprovalQueue({ sources: [{ id: 'mine', load: async () => [], answer: async () => undefined }] });
    approvals.follow('t-1', true);
    approvals.push('mine', { key: 'mine:1', kind: 'confirmation', expiresAt: new Date(Date.now() + 60_000).toISOString(), data: {} });
    const el = shell({ messageQueue: queue, approvals });
    await settle();
    expect(el.querySelector('[data-queued-toggle]')!.getAttribute('aria-expanded')).toBe('false');

    approvals.remove('mine:1');
    await settle();
    expect(el.querySelector('[data-queued-toggle]')!.getAttribute('aria-expanded')).toBe('true');
  });
});
