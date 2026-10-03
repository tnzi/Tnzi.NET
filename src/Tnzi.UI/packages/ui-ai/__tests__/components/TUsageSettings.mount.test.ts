/**
 * `TUsageSettings` mounted under a Chinese catalogue.
 *
 * The source gate (`settingsPagesI18n.test.ts`) proves no literal is left in
 * the settings pages; this proves the other half on one of them: the catalogue
 * provided by an ancestor is the one that reaches the pane, and the counts are
 * substituted into the translated sentences rather than left as `{used}`.
 */
import { afterEach, describe, expect, it } from 'vitest';
import { computed, createApp, defineComponent, h, nextTick, ref, type App } from 'vue';
import { QuotaWarningLevel, type UserQuotaDto } from '@tnzi/core/services/ai';
import TUsageSettings from '../../src/components/settings/TUsageSettings.vue';
import { createAiI18n } from '../../src/i18n';
import { zhCn } from '../../src/locales/zh-cn';
import { formatTokens, type UseAiUsageReturn } from '../../src/headless/useAiUsage';

function quota(overrides: Partial<UserQuotaDto> = {}): UserQuotaDto {
  return {
    id: 'q1',
    userId: 'u1',
    dailyTokenLimit: 1000,
    monthlyTokenLimit: 20000,
    currentDailyUsage: 250,
    currentMonthlyUsage: 5000,
    remainingDailyQuota: 750,
    remainingMonthlyQuota: 15000,
    dailyUsagePercentage: 25,
    monthlyUsagePercentage: 25,
    lastResetDate: '2026-01-01T00:00:00Z',
    isEnabled: true,
    warningThreshold: 80,
    criticalThreshold: 95,
    warningLevel: QuotaWarningLevel.None,
    creationTime: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

function controller(value: UserQuotaDto | null): UseAiUsageReturn {
  const q = ref<UserQuotaDto | null>(value);
  return {
    quota: q,
    loading: ref(false),
    available: computed(() => true),
    enabled: computed(() => q.value?.isEnabled === true),
    load: async () => {},
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
  it('renders the Chinese copy with the counts filled in', async () => {
    const text = await mountInChinese(controller(quota()));
    expect(text).toContain(zhCn.usageSettings.title);
    expect(text).toContain(zhCn.usageSettings.today);
    expect(text).toContain(zhCn.usageSettings.remainingToday);
    expect(text).toContain(`已用 ${formatTokens(250)} / ${formatTokens(1000)}`);
    expect(text).not.toMatch(/\{\w+\}/);
    expect(text).not.toMatch(/tokens used|Today|Remaining/);
  });

  it('says "no limit" in Chinese when quotas are off', async () => {
    const text = await mountInChinese(controller(null));
    expect(text).toContain(zhCn.usageSettings.noLimit);
    expect(text).not.toMatch(/No usage limit/);
  });
});
