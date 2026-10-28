/**
 * `TUsageSettings` mounted under a Chinese catalogue.
 *
 * The source gate (`settingsPagesI18n.test.ts`) proves no literal is left in
 * the settings pages; this proves the other half on one of them: the catalogue
 * provided by an ancestor is the one that reaches the pane, and the shares,
 * window lengths and reset times are substituted into the translated sentences
 * rather than left as `{percent}`.
 */
import { afterEach, describe, expect, it } from 'vitest';
import { computed, createApp, defineComponent, h, nextTick, ref, type App } from 'vue';
import { UsageLimitWindow, type MyUsageLimitsDto, type MyUsageWindowDto } from '@tnzi/core/services/ai';
import TUsageSettings from '../../src/components/settings/TUsageSettings.vue';
import { createAiI18n } from '../../src/i18n';
import { zhCn } from '../../src/locales/zh-cn';
import type { UseAiUsageReturn } from '../../src/headless/useAiUsage';

function win(overrides: Partial<MyUsageWindowDto> = {}): MyUsageWindowDto {
  return {
    window: UsageLimitWindow.Session,
    limited: true,
    durationMinutes: 300,
    usedPercent: 42.4,
    startedAt: new Date(Date.now() - 60 * 60_000).toISOString(),
    // 2 h 13 min from now (plus a few seconds of slack, which rounds up to the minute).
    resetsAt: new Date(Date.now() + (2 * 60 + 12) * 60_000 + 30_000).toISOString(),
    exceeded: false,
    warningReached: false,
    ...overrides,
  };
}

function controller(value: MyUsageLimitsDto | null, opts: { failed?: boolean } = {}): UseAiUsageReturn {
  const status = ref<MyUsageLimitsDto | null>(value);
  const windows = computed(() => status.value?.windows ?? []);
  return {
    status,
    loading: ref(false),
    failed: ref(opts.failed ?? false),
    available: computed(() => true),
    enabled: computed(() => windows.value.some((w) => w.limited)),
    windows,
    exceeded: computed(() => null),
    warning: computed(() => null),
    checkedAt: computed(() => (status.value ? new Date(status.value.checkedAt) : null)),
    load: async () => {},
    currentLimit: () => null,
  };
}

let app: App | null = null;
let host: HTMLElement | null = null;

async function mountInChinese(ctrl: UseAiUsageReturn): Promise<string> {
  host = document.createElement('div');
  document.body.appendChild(host);
  const Root = defineComponent({
    setup() {
      createAiI18n(zhCn);
      return () => h(TUsageSettings, { controller: ctrl });
    },
  });
  app = createApp(Root);
  app.mount(host);
  await nextTick();
  return host.textContent ?? '';
}

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
});

describe('TUsageSettings under zh-cn', () => {
  it('one row per window: share used, reset countdown, window length', async () => {
    const text = await mountInChinese(controller({
      windows: [
        win(),
        win({ window: UsageLimitWindow.Weekly, durationMinutes: 7 * 24 * 60, usedPercent: 10, resetsAt: new Date(Date.now() + 3 * 86_400_000).toISOString() }),
      ],
      warningThreshold: 0.8,
      checkedAt: new Date().toISOString(),
    }));

    expect(text).toContain(zhCn.usageSettings.title);
    expect(text).toContain(zhCn.usageSettings.session);
    expect(text).toContain(zhCn.usageSettings.weekly);
    expect(text).toContain('已用 42%');
    expect(text).toContain('2 小时 13 分钟后重置');
    expect(text).toContain('持续 5 小时');
    expect(text).toContain('每 7 天 重置一次');
    expect(text).toContain('更新于');
    expect(text).not.toMatch(/\{\w+\}/);
    expect(text).not.toMatch(/used|Resets|Current session/);
  });

  it('a window not started yet says it starts with the next message', async () => {
    const text = await mountInChinese(controller({
      windows: [win({ usedPercent: 0, startedAt: null, resetsAt: null })],
      warningThreshold: 0.8,
      checkedAt: new Date().toISOString(),
    }));
    expect(text).toContain(zhCn.usageSettings.notStarted);
  });

  it('an unlimited window says so instead of drawing a bar', async () => {
    const text = await mountInChinese(controller({
      windows: [win(), win({ window: UsageLimitWindow.Weekly, limited: false, usedPercent: null, resetsAt: null })],
      warningThreshold: 0.8,
      checkedAt: new Date().toISOString(),
    }));
    expect(text).toContain(zhCn.usageSettings.noLimit);
  });

  it('says "no limits" in Chinese when no window is limited', async () => {
    const text = await mountInChinese(controller({
      windows: [win({ limited: false, usedPercent: null })],
      warningThreshold: 0.8,
      checkedAt: new Date().toISOString(),
    }));
    expect(text).toContain(zhCn.usageSettings.noLimits);
    expect(text).not.toMatch(/No usage limits/);
  });

  it('a failed first read says so', async () => {
    const text = await mountInChinese(controller(null, { failed: true }));
    expect(text).toContain(zhCn.usageSettings.loadFailed);
  });
});
