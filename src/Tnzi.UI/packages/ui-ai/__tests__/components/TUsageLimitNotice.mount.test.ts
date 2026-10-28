/**
 * `TUsageLimitNotice` and `TComposerModelPicker` mounted: the notice words the
 * window and the reset in the provided language (and keeps the external CLI's
 * own limit apart), and the picker emits the model the user chose.
 */
import { afterEach, describe, expect, it } from 'vitest';
import { createApp, defineComponent, h, nextTick, type App } from 'vue';
import TUsageLimitNotice from '../../src/components/chat/TUsageLimitNotice.vue';
import TComposerModelPicker from '../../src/components/chat/TComposerModelPicker.vue';
import { createAiI18n } from '../../src/i18n';
import { en } from '../../src/locales/en';
import { zhCn } from '../../src/locales/zh-cn';

let app: App | null = null;
let host: HTMLElement | null = null;

async function mount(component: unknown, props: Record<string, unknown>, messages = en): Promise<HTMLElement> {
  host = document.createElement('div');
  document.body.appendChild(host);
  const Root = defineComponent({
    setup() {
      createAiI18n(messages);
      return () => h(component as never, props);
    },
  });
  app = createApp(Root);
  app.mount(host);
  await nextTick();
  return host;
}

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
});

const inMinutes = (m: number) => new Date(Date.now() + m * 60_000 + 20_000).toISOString();

describe('TUsageLimitNotice', () => {
  it('names the window and counts down to the reset', async () => {
    const el = await mount(TUsageLimitNotice, { window: 'Session', resetsAt: inMinutes(133) });
    expect(el.textContent).toContain(en.usageLimits.reachedSession);
    expect(el.textContent).toContain('It resets in 2 hr 14 min.');
    expect(el.querySelector('[role="alert"]')).not.toBeNull();
  });

  it('in Chinese, with the draft kept', async () => {
    const el = await mount(TUsageLimitNotice, { window: 'Weekly', resetsAt: inMinutes(30), draftKept: true }, zhCn);
    expect(el.textContent).toContain(zhCn.usageLimits.reachedWeekly);
    expect(el.textContent).toContain('31 分钟后重置。');
    expect(el.textContent).toContain(zhCn.usageLimits.draftKept);
    expect(el.textContent).not.toMatch(/\{\w+\}/);
  });

  it('a warning gives the share used and the reset label', async () => {
    const el = await mount(TUsageLimitNotice, { tone: 'warning', window: 'Weekly', usedPercent: 86.6, resetsAt: inMinutes(60) });
    expect(el.textContent).toContain("You've used 87% of your weekly limit.");
    expect(el.textContent).toContain('Resets in 1 hr 1 min');
    expect(el.querySelector('[role="status"]')).not.toBeNull();
  });

  it("the external CLI's own limit keeps its own wording", async () => {
    const el = await mount(TUsageLimitNotice, { scope: 'account', variant: 'inline', resetsAt: null });
    expect(el.textContent).toContain(en.chat.usageLimit);
    expect(el.textContent).not.toContain(en.usageLimits.reached);
  });

  it('a zero limit (no reset) points at an administrator', async () => {
    const el = await mount(TUsageLimitNotice, { window: 'Session', resetsAt: null });
    expect(el.textContent).toContain(en.usageLimits.noReset);
  });

  it('dismiss emits', async () => {
    let dismissed = 0;
    const el = await mount(TUsageLimitNotice, { window: 'Session', resetsAt: inMinutes(5), dismissible: true, onDismiss: () => { dismissed += 1; } });
    (el.querySelector('button') as HTMLButtonElement).click();
    expect(dismissed).toBe(1);
  });
});

describe('TComposerModelPicker', () => {
  const models = [
    { id: 'claude-opus-5-5', displayName: 'Opus 5.5' },
    { id: 'claude-sonnet-5-5', displayName: 'Sonnet 5.5', description: 'Fast and capable' },
  ];

  it('shows the current model and emits the one picked', async () => {
    const picked: string[] = [];
    const el = await mount(TComposerModelPicker, {
      models,
      modelValue: 'claude-sonnet-5-5',
      'onUpdate:modelValue': (id: string) => picked.push(id),
    });
    const trigger = el.querySelector('button') as HTMLButtonElement;
    expect(trigger.textContent).toContain('Sonnet 5.5');

    trigger.click();
    await nextTick();
    const items = [...el.querySelectorAll('[role="menuitemradio"]')] as HTMLButtonElement[];
    expect(items.map((i) => i.getAttribute('aria-checked'))).toEqual(['false', 'true']);
    items[0].click();
    expect(picked).toEqual(['claude-opus-5-5']);
  });

  it('an unknown selection shows the first model', async () => {
    const el = await mount(TComposerModelPicker, { models, modelValue: 'gone' });
    expect(el.querySelector('button')?.textContent).toContain('Opus 5.5');
  });
});
