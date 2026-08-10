import { describe, it, expect, vi } from 'vitest';
import { useAiUsage, usageBarPercent, formatTokens, isUnlimited } from '../../src/headless/useAiUsage';

const QUOTA = {
  id: 'q1',
  userId: 'u1',
  dailyTokenLimit: 100000,
  monthlyTokenLimit: 2000000,
  currentDailyUsage: 25000,
  currentMonthlyUsage: 400000,
  remainingDailyQuota: 75000,
  remainingMonthlyQuota: 1600000,
  dailyUsagePercentage: 25,
  monthlyUsagePercentage: 20,
  isEnabled: true,
};

const client = (data: unknown) => ({ get: vi.fn(async () => ({ succeeded: true, data })) }) as never;

describe('useAiUsage', () => {
  it('is unavailable and inert without a client', async () => {
    const u = useAiUsage();
    expect(u.available.value).toBe(false);
    await u.load();
    expect(u.quota.value).toBeNull();
    expect(u.enabled.value).toBe(false);
  });

  it('loads the quota', async () => {
    const u = useAiUsage({ client: client(QUOTA) });
    await u.load();

    expect(u.quota.value?.remainingDailyQuota).toBe(75000);
    expect(u.enabled.value).toBe(true);
    expect(u.loading.value).toBe(false);
  });

  it('a deployment with quotas off is not an error', async () => {
    /* `enabled` false + no error: "this deployment does not meter usage" and
       "the request failed" are different facts, and the page says so. */
    const u = useAiUsage({ client: client({ ...QUOTA, isEnabled: false }) });
    await u.load();

    expect(u.quota.value).not.toBeNull();
    expect(u.enabled.value).toBe(false);
  });

  it('a failed read leaves the page in the no-limit state, not an error state', async () => {
    const u = useAiUsage({
      client: { get: vi.fn(async () => { throw new Error('offline'); }) } as never,
    });
    await u.load();

    expect(u.quota.value).toBeNull();
    expect(u.enabled.value).toBe(false);
    expect(u.loading.value).toBe(false);
  });
});

describe('usageBarPercent', () => {
  it('clamps over-100 so the bar cannot escape its track', () => {
    /* Usage really can exceed a limit: a turn that overshoots finishes rather
       than truncating mid-sentence. */
    expect(usageBarPercent(140)).toBe(100);
    expect(usageBarPercent(42)).toBe(42);
    expect(usageBarPercent(-5)).toBe(0);
  });

  it('treats missing or non-finite input as zero', () => {
    expect(usageBarPercent(undefined)).toBe(0);
    expect(usageBarPercent(null)).toBe(0);
    expect(usageBarPercent(Number.NaN)).toBe(0);
  });
});

describe('formatTokens', () => {
  it('groups thousands', () => {
    expect(formatTokens(1284730)).toBe((1284730).toLocaleString());
  });

  it('shows a dash rather than "0" for no value', () => {
    /* "no number" and "the number is zero" are different things to report. */
    expect(formatTokens(undefined)).toBe('-');
    expect(formatTokens(null)).toBe('-');
    expect(formatTokens(0)).toBe('0');
  });
});

describe('isUnlimited', () => {
  it('treats the backend sentinel as no limit', () => {
    /* `long.MaxValue / 2`. Rendered literally it reads
       "4,611,686,018,427,388,000 tokens", which nobody parses as unlimited. */
    expect(isUnlimited(4611686018427387903)).toBe(true);
  });

  it('treats anything past MAX_SAFE_INTEGER as no limit', () => {
    /* Not just cosmetic: past this point JSON parsing has already rounded the
       value, so the digits on screen are not the ones the server stored. */
    expect(isUnlimited(Number.MAX_SAFE_INTEGER)).toBe(true);
    expect(isUnlimited(Number.MAX_SAFE_INTEGER - 1)).toBe(false);
  });

  it('treats a real limit as a limit', () => {
    expect(isUnlimited(100000)).toBe(false);
    expect(isUnlimited(0)).toBe(false);
  });

  it('treats missing input as no limit rather than a zero budget', () => {
    /* A quota the page could not read must not render as "0 of 0 used", which
       says the user is out of tokens. */
    expect(isUnlimited(undefined)).toBe(true);
    expect(isUnlimited(null)).toBe(true);
    expect(isUnlimited(Number.NaN)).toBe(true);
  });
});
