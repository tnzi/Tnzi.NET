/**
 * The cards a conversation waits on its user for, mounted for real: the approval card (answers,
 * a code before the allowing answer, the countdown running out), the question card (submit only
 * once answered, the own answer), the plan card (revise opens the feedback box and focuses it),
 * the list (answers through the queue, a closed card and a refused answer each with a note, an
 * application's own kind by `describe` or the slot), and `TChatApp` (cards above the composer
 * without being handed them, and on a phone the composer keeps only its bar).
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, shallowRef, type App, type Component } from 'vue';
import TApprovalCard from '../../src/components/approvals/TApprovalCard.vue';
import TQuestionCard from '../../src/components/approvals/TQuestionCard.vue';
import TPlanCard from '../../src/components/approvals/TPlanCard.vue';
import TPendingApprovals from '../../src/components/approvals/TPendingApprovals.vue';
import TToastHost from '../../src/components/controls/TToastHost.vue';
import { toaster } from '../../src/components/controls/toast';
import { createApprovalQueue, type ApprovalSource, type PendingInteraction } from '../../src/headless/approval-queue';
import { provideChatThreads } from '../../src/headless/chat-approvals-key';
import type { ApprovalCardModel } from '../../src/headless/approval-card';
import type { ChatMessage } from '../../src/headless/useChat';

const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');

let app: App | null = null;
let host: HTMLElement | null = null;

function mount(component: Component, props: () => Record<string, unknown>, slots?: Record<string, (p?: any) => unknown>, setup?: () => void): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  const Root = defineComponent({
    setup: () => {
      setup?.();
      return () => [h(component, props(), slots), h(TToastHost)];
    },
  });
  app = createApp(Root);
  app.mount(host);
  return host;
}

async function settle(): Promise<void> {
  for (let i = 0; i < 6; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

const expires = (minutes = 5) => new Date(Date.now() + minutes * 60_000).toISOString();
const button = (el: HTMLElement, id: string) => el.querySelector<HTMLButtonElement>(`[data-choice="${id}"]`)!;

beforeEach(() => {
  toaster.toast.clear();
});

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
  vi.useRealTimers();
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1280 });
});

function card(over: Partial<ApprovalCardModel> = {}): ApprovalCardModel {
  return {
    key: 'k1',
    icon: 'lucide:terminal',
    title: 'The agent wants to run a command',
    summary: 'Clean the build',
    fields: [{ label: 'File', value: '/tmp/x', mono: true }],
    detail: { text: 'rm -rf build', mono: true },
    choices: [
      { id: 'yes', label: 'Allow once', tone: 'allow', primary: true },
      { id: 'no', label: 'Deny', tone: 'deny' },
    ],
    expiresAt: expires(),
    ...over,
  };
}

describe('TApprovalCard', () => {
  it('is a named group with its fields, its text, its answers and the time left', async () => {
    const choose = vi.fn();
    const el = mount(TApprovalCard, () => ({ card: card(), expiresText: (t: string) => `${t} left`, onChoose: choose }));
    await settle();
    const group = el.querySelector('[role="group"]')!;
    expect(group.getAttribute('aria-label')).toBe('The agent wants to run a command');
    expect(el.querySelector('dt')!.textContent).toBe('File');
    expect(el.querySelector('pre')!.textContent).toBe('rm -rf build');
    expect(el.querySelector('.t-approval-card__expires')!.textContent).toMatch(/^4:5\d left$|^5:00 left$/);
    expect(button(el, 'yes').className).toContain('t-button--primary');

    button(el, 'no').click();
    expect(choose).toHaveBeenCalledWith('no', undefined);
  });

  it('the allowing answer waits for the code and goes with its digits; the refusal does not', async () => {
    const choose = vi.fn();
    const el = mount(TApprovalCard, () => ({
      card: card({ code: { length: 6, label: 'Code from the mail' } }),
      expiresText: (t: string) => t,
      onChoose: choose,
    }));
    await settle();
    expect(button(el, 'yes').disabled).toBe(true);
    const input = el.querySelector<HTMLInputElement>('[data-approval-code]')!;
    input.value = '123 456';
    input.dispatchEvent(new Event('input'));
    await settle();
    expect(button(el, 'yes').disabled).toBe(false);
    button(el, 'yes').click();
    expect(choose).toHaveBeenCalledWith('yes', '123456');
    button(el, 'no').click();
    expect(choose).toHaveBeenLastCalledWith('no', undefined);
  });

  it('the code field is the 36px one-time-code field, not the 30px default', async () => {
    const el = mount(TApprovalCard, () => ({ card: card({ code: { length: 6, label: 'Code from the mail' } }), expiresText: (t: string) => t }));
    await settle();
    const input = el.querySelector<HTMLInputElement>('[data-approval-code]')!;
    expect(input.classList.contains('t-input--md')).toBe(true);
    expect(input.classList.contains('t-input--code')).toBe(true);
    expect(input.getAttribute('autocomplete')).toBe('one-time-code');
    expect(input.getAttribute('inputmode')).toBe('numeric');
  });

  it('says it expired once the time runs out, and the answers wait', async () => {
    vi.useFakeTimers();
    const expired = vi.fn();
    const el = mount(TApprovalCard, () => ({ card: card({ expiresAt: new Date(Date.now() + 2000).toISOString() }), expiresText: (t: string) => t, onExpired: expired }));
    await settle();
    expect(expired).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(3000);
    expect(expired).toHaveBeenCalledTimes(1);
    expect(button(el, 'yes').disabled).toBe(true);
  });

  it('busy holds every answer', async () => {
    const el = mount(TApprovalCard, () => ({ card: card(), busy: true, expiresText: (t: string) => t }));
    await settle();
    expect(button(el, 'yes').disabled).toBe(true);
    expect(button(el, 'no').disabled).toBe(true);
  });
});

const QUESTION_LABELS = { submit: 'Send answers', decline: 'Skip', own: 'Other', ownPlaceholder: 'Write', multiple: 'Pick any' };

describe('TQuestionCard', () => {
  const model = {
    key: 'q',
    title: 'The agent has 2 questions for you',
    questions: [
      { question: 'Which DB?', header: 'DB', options: [{ label: 'Postgres', description: 'Server' }, { label: 'SQLite' }], multiSelect: false },
      { question: 'Which parts?', options: [{ label: 'API' }, { label: 'UI' }], multiSelect: true },
    ],
    expiresAt: expires(),
  };

  it('submits once every question has an answer, with the own words where written', async () => {
    const submit = vi.fn();
    const el = mount(TQuestionCard, () => ({ card: model, labels: QUESTION_LABELS, expiresText: (t: string) => t, onSubmit: submit }));
    await settle();
    expect(el.querySelectorAll('fieldset legend')[0]!.textContent).toContain('Which DB?');
    expect(el.querySelectorAll('input[type="radio"]')).toHaveLength(3);
    expect(el.querySelectorAll('input[type="checkbox"]')).toHaveLength(3);
    expect(button(el, 'submit').disabled).toBe(true);

    el.querySelector<HTMLInputElement>('[data-question="0"] [data-option="SQLite"]')!.click();
    el.querySelector<HTMLInputElement>('[data-question="1"] [data-option="UI"]')!.click();
    el.querySelector<HTMLInputElement>('[data-question="1"] [data-option-own]')!.click();
    await settle();
    const own = el.querySelector<HTMLTextAreaElement>('[data-question="1"] [data-own-answer]')!;
    own.value = 'Docs';
    own.dispatchEvent(new Event('input'));
    await settle();

    expect(el.querySelector('[data-question="0"]')!.hasAttribute('data-answered')).toBe(true);
    expect(button(el, 'submit').disabled).toBe(false);
    button(el, 'submit').click();
    expect(submit).toHaveBeenCalledWith({ 'Which DB?': ['SQLite'], 'Which parts?': ['UI', 'Docs'] });
  });

  it('declines without answers', async () => {
    const decline = vi.fn();
    const el = mount(TQuestionCard, () => ({ card: model, labels: QUESTION_LABELS, expiresText: (t: string) => t, onDecline: decline }));
    await settle();
    button(el, 'decline').click();
    expect(decline).toHaveBeenCalled();
  });
});

const PLAN_LABELS = { send: 'Send back', cancel: 'Cancel', feedback: 'What to change', feedbackPlaceholder: 'e.g.', empty: 'No plan.' };

describe('TPlanCard', () => {
  const model = {
    key: 'p',
    title: 'Plan',
    plan: '1. **Read** the code\n2. Change it',
    note: 'A draft.',
    choices: [
      { id: 'approve', label: 'Approve', tone: 'allow' as const, primary: true },
      { id: 'reject', label: 'Revise', tone: 'deny' as const },
    ],
    expiresAt: expires(),
  };

  it('shows the plan as Markdown under its note, and approves at once', async () => {
    const choose = vi.fn();
    const el = mount(TPlanCard, () => ({ card: model, labels: PLAN_LABELS, expiresText: (t: string) => t, onChoose: choose }));
    await settle();
    expect(el.querySelector('[data-plan-note]')!.textContent).toBe('A draft.');
    expect(el.querySelector('[data-plan] strong')?.textContent).toBe('Read');
    button(el, 'approve').click();
    expect(choose).toHaveBeenCalledWith('approve', null);
  });

  it('revise opens the feedback box with focus, sends it back with what was written, and cancel returns', async () => {
    const choose = vi.fn();
    const el = mount(TPlanCard, () => ({ card: model, labels: PLAN_LABELS, expiresText: (t: string) => t, onChoose: choose }));
    await settle();
    button(el, 'reject').click();
    await settle();
    const box = el.querySelector<HTMLTextAreaElement>('[data-plan-feedback]')!;
    expect(document.activeElement).toBe(box);
    expect(button(el, 'approve')).toBeNull();

    button(el, 'cancel-feedback').click();
    await settle();
    expect(el.querySelector('[data-plan-feedback]')).toBeNull();

    button(el, 'reject').click();
    await settle();
    const again = el.querySelector<HTMLTextAreaElement>('[data-plan-feedback]')!;
    again.value = '  Only the page  ';
    again.dispatchEvent(new Event('input'));
    await settle();
    button(el, 'send-feedback').click();
    expect(choose).toHaveBeenCalledWith('reject', 'Only the page');
  });

  it('says so when the agent handed over no plan', async () => {
    const el = mount(TPlanCard, () => ({ card: { ...model, plan: '  ' }, labels: PLAN_LABELS, expiresText: (t: string) => t }));
    await settle();
    expect(el.querySelector('[data-plan-empty]')!.textContent).toBe('No plan.');
    expect(el.querySelector('[data-plan-note]')).toBeNull();
  });
});

function runRequest(requestId: string, over: Record<string, unknown> = {}): PendingInteraction {
  return {
    key: `run:${requestId}`,
    kind: (over.kind as string | undefined) ?? 'permission',
    expiresAt: expires(),
    createdAt: new Date().toISOString(),
    data: {
      runId: 'r1',
      requestId,
      kind: 'permission',
      status: 'Pending',
      tool: 'Bash',
      input: { command: 'ls' },
      options: [
        { id: 'allow_once', name: 'a', kind: 'allow_once' },
        { id: 'reject_once', name: 'r', kind: 'reject_once' },
      ],
      ...over,
    },
  };
}

function queueOf(items: PendingInteraction[], answer: ApprovalSource['answer'] = async () => 'answered', others: ApprovalSource[] = []) {
  const source: ApprovalSource = { id: 'run', load: async () => items, answer: vi.fn(answer) };
  const queue = createApprovalQueue({ sources: [source, ...others], intervalMs: 0 });
  queue.follow('t1', true);
  return { queue, source };
}

describe('TPendingApprovals', () => {
  it('draws each kind with its card, in a region that says what it is', async () => {
    const { queue } = queueOf([
      runRequest('a'),
      runRequest('b', { kind: 'question', questions: [{ question: 'Q?', options: [{ label: 'x' }], multiSelect: false }] }),
      runRequest('c', { kind: 'plan', plan: 'Do it', options: [{ id: 'approve', name: 'a', kind: 'allow_once' }] }),
    ]);
    const el = mount(TPendingApprovals, () => ({ queue }));
    await settle();
    const region = el.querySelector('[data-pending-approvals]')!;
    expect(region.getAttribute('role')).toBe('region');
    expect(region.getAttribute('aria-label')).toBe('Waiting for you');
    expect([...el.querySelectorAll('.t-approval-card__title')].map((t) => t.textContent)).toEqual([
      'The agent wants to run a command',
      'The agent has a question for you',
      'The agent has a plan for your approval',
    ]);
  });

  it('answers through the queue and the card leaves; a closed one leaves with a note', async () => {
    const answers: string[] = [];
    let outcome: 'answered' | 'closed' = 'answered';
    const { queue, source } = queueOf([runRequest('a'), runRequest('b')], async (_item, reply) => {
      answers.push(reply.optionId);
      return outcome;
    });
    const el = mount(TPendingApprovals, () => ({ queue }));
    await settle();
    el.querySelector<HTMLButtonElement>('[data-approval="run:a"] [data-choice="allow_once"]')!.click();
    await settle();
    expect(answers).toEqual(['allow_once']);
    expect(el.querySelector('[data-approval="run:a"]')).toBeNull();

    outcome = 'closed';
    el.querySelector<HTMLButtonElement>('[data-approval="run:b"] [data-choice="reject_once"]')!.click();
    await settle();
    expect(el.querySelector('[data-pending-approvals]')).toBeNull();
    expect(document.body.textContent).toContain('This card is no longer waiting for an answer');
    expect(source.answer).toHaveBeenCalledTimes(2);
  });

  it('a refused answer keeps the card and says why', async () => {
    const { queue } = queueOf([runRequest('a')], async () => {
      throw new Error('Answer every question.');
    });
    const el = mount(TPendingApprovals, () => ({ queue }));
    await settle();
    el.querySelector<HTMLButtonElement>('[data-choice="allow_once"]')!.click();
    await settle();
    expect(el.querySelector('[data-approval="run:a"]')).not.toBeNull();
    expect(document.body.textContent).toContain("Couldn't send your answer: Answer every question.");
  });

  it('an application kind: its own model by `describe` (the code goes with the answer), else the slot', async () => {
    const mine: ApprovalSource = {
      id: 'mine',
      load: async () => [
        { key: 'mine:1', kind: 'confirmation', expiresAt: expires(), data: { tool: 'send_email' } },
        { key: 'mine:2', kind: 'custom', expiresAt: expires(), data: {} },
      ],
      answer: vi.fn(async () => 'answered' as const),
    };
    const { queue } = queueOf([], undefined, [mine]);
    const describe = (item: PendingInteraction): ApprovalCardModel | null =>
      item.kind === 'confirmation'
        ? card({ key: item.key, title: 'Send the mail?', choices: [{ id: 'approve', label: 'Send', tone: 'allow', primary: true }], code: { length: 4, label: 'Code' } })
        : null;
    const el = mount(TPendingApprovals, () => ({ queue, describe }), {
      card: (p: { item: PendingInteraction }) => h('div', { class: 'own-card' }, p.item.key),
    });
    await settle();
    expect(el.querySelector('.t-approval-card__title')!.textContent).toBe('Send the mail?');
    expect(el.querySelector('.own-card')!.textContent).toBe('mine:2');

    const input = el.querySelector<HTMLInputElement>('[data-approval-code]')!;
    input.value = '1234';
    input.dispatchEvent(new Event('input'));
    await settle();
    el.querySelector<HTMLButtonElement>('[data-choice="approve"]')!.click();
    await settle();
    expect(mine.answer).toHaveBeenCalledWith(expect.objectContaining({ key: 'mine:1' }), { optionId: 'approve', code: '1234' });
  });
});

describe('TChatApp', () => {
  const messages: ChatMessage[] = [
    { id: 'u1', role: 'user', content: 'Clean up', createdAt: '' },
    { id: 'a1', role: 'assistant', content: 'Needs your approval.', createdAt: '' },
  ];
  /* A `useChatThreads` in the shell's parent, as far as the shell can tell: its messages and its queue. */
  const threads = (list: readonly ChatMessage[], approvals: ReturnType<typeof queueOf>['queue']) => () =>
    provideChatThreads({ messages: shallowRef(list), approvals });
  const shell = (extra: Record<string, unknown> = {}, setup?: () => void) =>
    mount(TChatApp, () => ({ sidebarStorageKey: null, autoApplyTheme: false, activeThreadId: 'A', messages, inputText: '', ...extra }), undefined, setup);

  it('shows the cards a `useChatThreads` above it provides, above the composer', async () => {
    const { queue } = queueOf([runRequest('a')]);
    const el = shell({}, threads(messages, queue));
    await settle();
    const top = el.querySelector('.t-thread-composer__top')!;
    expect(top.querySelector('[data-pending-approvals] [data-approval="run:a"]')).not.toBeNull();
    expect(top.nextElementSibling?.classList.contains('t-thread-composer')).toBe(true);
  });

  it('`approvals: null` turns them off, and a given queue wins over a provided one', async () => {
    const provided = queueOf([runRequest('p')]).queue;
    const off = shell({ approvals: null }, threads(messages, provided));
    await settle();
    expect(off.querySelector('[data-pending-approvals]')).toBeNull();
    app?.unmount();

    const given = queueOf([runRequest('g')]).queue;
    const el = shell({ approvals: given }, threads(messages, provided));
    await settle();
    expect(el.querySelector('[data-approval="run:g"]')).not.toBeNull();
    expect(el.querySelector('[data-approval="run:p"]')).toBeNull();
  });

  it('with two threads instances in one component, shows the queue of the one whose messages it shows', async () => {
    const other: ChatMessage[] = [{ id: 'c1', role: 'user', content: 'Build it', createdAt: '' }];
    const chatQueue = queueOf([runRequest('chat')]).queue;
    const codeQueue = queueOf([runRequest('code')]).queue;
    const el = shell({}, () => {
      threads(messages, chatQueue)();
      threads(other, codeQueue)();
    });
    await settle();
    expect(el.querySelector('[data-approval="run:chat"]')).not.toBeNull();
    expect(el.querySelector('[data-approval="run:code"]')).toBeNull();
    app?.unmount();

    const code = shell({ messages: other }, () => {
      threads(messages, chatQueue)();
      threads(other, codeQueue)();
    });
    await settle();
    expect(code.querySelector('[data-approval="run:code"]')).not.toBeNull();
    expect(code.querySelector('[data-approval="run:chat"]')).toBeNull();
  });

  it('with one instance it is that one even for a copy of its messages; with several and no match, none', async () => {
    const queue = queueOf([runRequest('only')]).queue;
    const single = shell({ messages: [...messages] }, threads(messages, queue));
    await settle();
    expect(single.querySelector('[data-approval="run:only"]')).not.toBeNull();
    app?.unmount();

    const ambiguous = shell({ messages: [...messages] }, () => {
      threads(messages, queue)();
      threads([], queueOf([runRequest('other')]).queue)();
    });
    await settle();
    expect(ambiguous.querySelector('[data-pending-approvals]')).toBeNull();
  });

  it('on a phone with a card waiting, the composer keeps only its bar', async () => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 390 });
    const { queue } = queueOf([runRequest('a')]);
    const el = shell({ approvals: queue });
    await settle();
    expect(el.querySelector('.t-chat-app')!.className).toContain('t-chat-app--awaiting');

    queue.remove('run:a');
    await settle();
    expect(el.querySelector('.t-chat-app')!.className).not.toContain('t-chat-app--awaiting');
  });

  it('on a wide screen the composer stays whole', async () => {
    const { queue } = queueOf([runRequest('a')]);
    const el = shell({ approvals: queue });
    await settle();
    expect(el.querySelector('.t-chat-app')!.className).not.toContain('t-chat-app--awaiting');
  });
});

/*
 * Tall cards keep their answers in view (the layout is read from the styles by
 * conventions/pending-card-heights.test.ts, as happy-dom does no layout). What that rule relies on
 * in the markup: each card is a direct child of the band, and the one long part of each card (the
 * text, the plan, the questions) and its answers are direct children of the card's column, so the
 * long part is what gives way. A wrapper slipped in between would turn the rule off silently.
 */
describe('tall cards', () => {
  const longText = Array.from({ length: 120 }, (_, i) => `+ line ${i}`).join('\n');
  const questions = Array.from({ length: 5 }, (_, i) => ({ question: `Q${i}?`, options: [{ label: 'a' }, { label: 'b' }], multiSelect: false }));
  const plan = Array.from({ length: 40 }, (_, i) => `${i + 1}. Step ${i + 1}`).join('\n');

  async function band() {
    const { queue } = queueOf([
      runRequest('diff', { kind: 'own' }),
      runRequest('plan', { kind: 'plan', plan, options: [{ id: 'approve', name: 'a', kind: 'allow_once' }, { id: 'no', name: 'n', kind: 'reject_once' }] }),
      runRequest('questions', { kind: 'question', questions }),
    ]);
    const describe = (item: PendingInteraction): ApprovalCardModel | null =>
      item.kind === 'own' ? card({ key: item.key, detail: { text: longText, mono: true }, choices: [{ id: 'yes', label: 'Change', tone: 'allow', primary: true }, { id: 'no', label: 'Decline', tone: 'deny' }] }) : null;
    const el = mount(TPendingApprovals, () => ({ queue, describe }));
    await settle();
    return el;
  }

  const column = (el: HTMLElement, key: string) => el.querySelector<HTMLElement>(`[data-approval="${key}"]`)!;
  const directChild = (parent: HTMLElement, selector: string) => [...parent.children].find((c) => c.matches(selector)) ?? null;

  it('every card sits directly in the band, so the band scrolls them and does not squeeze them', async () => {
    const el = await band();
    const region = el.querySelector<HTMLElement>('[data-pending-approvals]')!;
    expect([...region.children].map((c) => c.getAttribute('data-approval'))).toEqual(['run:diff', 'run:plan', 'run:questions']);
    expect([...region.children].every((c) => c.classList.contains('t-approval-card'))).toBe(true);
  });

  it.each([
    ['an approval card with a long diff', 'run:diff', '.t-approval-card__detail', ['yes', 'no']],
    ['a plan card', 'run:plan', '[data-plan]', ['approve', 'no']],
    ['a card of many questions', 'run:questions', '[data-questions]', ['submit', 'decline']],
  ])('%s: the long part and the answers are parts of the card\'s column', async (_kind, key, longPart, choices) => {
    const el = await band();
    const cardEl = column(el, key);
    expect(directChild(cardEl, '.t-approval-card__head')).not.toBeNull();
    expect(directChild(cardEl, longPart), `${longPart} must be a direct child of the card`).not.toBeNull();
    const row = directChild(cardEl, '.t-approval-card__choices')!;
    expect([...row.querySelectorAll('[data-choice]')].map((b) => b.getAttribute('data-choice'))).toEqual(choices);
    expect(directChild(cardEl, '.t-approval-card__expires')).not.toBeNull();
    // The long part comes before the answers: it is what gives way, not them.
    const parts = [...cardEl.children];
    expect(parts.indexOf(directChild(cardEl, longPart)!)).toBeLessThan(parts.indexOf(row));
  });
});
