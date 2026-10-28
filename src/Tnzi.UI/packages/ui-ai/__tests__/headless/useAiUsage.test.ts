import { describe, it, expect, vi, afterEach } from 'vitest';
import { effectScope } from 'vue';
import { UsageLimitWindow, type MyUsageLimitsDto, type MyUsageWindowDto } from '@tnzi/core/services/ai';
import { useAiUsage, usageBarPercent } from '../../src/headless/useAiUsage';

const NOW = new Date('2026-10-21T10:00:00Z');

function win(overrides: Partial<MyUsageWindowDto> = {}): MyUsageWindowDto {
  return {
    window: UsageLimitWindow.Session,
    limited: true,
    durationMinutes: 300,
    usedPercent: 25,
    startedAt: '2026-10-21T09:00:00Z',
    resetsAt: '2026-10-21T14:00:00Z',
    exceeded: false,
    warningReached: false,
    ...overrides,
  };
}

function status(windows: MyUsageWindowDto[]): MyUsageLimitsDto {
  return { windows, warningThreshold: 0.8, checkedAt: NOW.toISOString() };
}

const client = (data: unknown) => ({ get: vi.fn(async () => ({ succeeded: true, data })) }) as never;

let scope: ReturnType<typeof effectScope> | null = null;
function inScope<T>(fn: () => T): T {
  scope = effectScope();
  return scope.run(fn)!;
}

afterEach(() => {
  scope?.stop();
  scope = null;
  vi.useRealTimers();
});

describe('useAiUsage', () => {
  it('is unavailable and inert without a client', async () => {
    const u = inScope(() => useAiUsage());
    expect(u.available.value).toBe(false);
    await u.load();
    expect(u.status.value).toBeNull();
    expect(u.enabled.value).toBe(false);
  });

  it('reads the windows from usage-limits/me', async () => {
    const http = client(status([win(), win({ window: UsageLimitWindow.Weekly, limited: false, usedPercent: null })]));
    const u = inScope(() => useAiUsage({ client: http, now: () => NOW }));
    await u.load();

    expect((http as { get: ReturnType<typeof vi.fn> }).get).toHaveBeenCalledWith('/usage-limits/me');
    expect(u.windows.value).toHaveLength(2);
    expect(u.enabled.value).toBe(true);
    expect(u.checkedAt.value?.toISOString()).toBe(NOW.toISOString());
    expect(u.loading.value).toBe(false);
    expect(u.failed.value).toBe(false);
  });

  it('no limited window reads "no limits", not an error', async () => {
    const u = inScope(() => useAiUsage({ client: client(status([win({ limited: false, usedPercent: null })])) }));
    await u.load();

    expect(u.status.value).not.toBeNull();
    expect(u.enabled.value).toBe(false);
    expect(u.failed.value).toBe(false);
  });

  it('a deployment without the endpoint is not a failure', async () => {
    const u = inScope(() => useAiUsage({ client: { get: vi.fn(async () => ({ succeeded: false, code: 404 })) } as never }));
    await u.load();

    expect(u.status.value).toBeNull();
    expect(u.failed.value).toBe(false);
  });

  it('a failed read is reported and keeps the previous figures', async () => {
    const get = vi.fn()
      .mockResolvedValueOnce({ succeeded: true, data: status([win()]) })
      .mockRejectedValueOnce(new Error('offline'));
    const u = inScope(() => useAiUsage({ client: { get } as never }));
    await u.load();
    await u.load();

    expect(u.failed.value).toBe(true);
    expect(u.windows.value).toHaveLength(1);
  });

  it('exceeded names the window at its limit while its reset is ahead', async () => {
    const u = inScope(() => useAiUsage({
      client: client(status([win({ exceeded: true, usedPercent: 101 })])),
      now: () => NOW,
    }));
    await u.load();

    expect(u.exceeded.value).toEqual({ window: UsageLimitWindow.Session, resetsAt: '2026-10-21T14:00:00Z' });
    expect(u.warning.value).toBeNull();
    expect(u.currentLimit()).toEqual(u.exceeded.value);
  });

  it('with both windows at their limit, the one that resets later is the one to wait for', async () => {
    const u = inScope(() => useAiUsage({
      client: client(status([
        win({ exceeded: true }),
        win({ window: UsageLimitWindow.Weekly, exceeded: true, resetsAt: '2026-10-24T08:00:00Z' }),
      ])),
      now: () => NOW,
    }));
    await u.load();

    expect(u.exceeded.value?.window).toBe(UsageLimitWindow.Weekly);
  });

  it('a limit whose reset has passed no longer blocks', async () => {
    let now = NOW;
    const u = inScope(() => useAiUsage({ client: client(status([win({ exceeded: true })])), now: () => now }));
    await u.load();
    expect(u.currentLimit()).not.toBeNull();

    now = new Date('2026-10-21T14:00:01Z');
    expect(u.currentLimit()).toBeNull();
  });

  it('the clock ticks while a window is at its limit, so a passing reset clears it', async () => {
    vi.useFakeTimers();
    let now = NOW;
    const u = inScope(() => useAiUsage({ client: client(status([win({ exceeded: true })])), now: () => now, tickMs: 1000 }));
    await u.load();
    expect(u.exceeded.value).not.toBeNull();

    now = new Date('2026-10-21T14:00:01Z');
    vi.advanceTimersByTime(1000);
    expect(u.exceeded.value).toBeNull();
  });

  it('warning picks the most-used window past the threshold', async () => {
    const u = inScope(() => useAiUsage({
      client: client(status([
        win({ warningReached: true, usedPercent: 82 }),
        win({ window: UsageLimitWindow.Weekly, warningReached: true, usedPercent: 91, resetsAt: '2026-10-24T08:00:00Z' }),
      ])),
      now: () => NOW,
    }));
    await u.load();

    expect(u.warning.value).toEqual({ window: UsageLimitWindow.Weekly, usedPercent: 91, resetsAt: '2026-10-24T08:00:00Z' });
  });

  it('a stale answer does not overwrite a newer one', async () => {
    let resolveFirst: (v: unknown) => void = () => {};
    const get = vi.fn()
      .mockImplementationOnce(() => new Promise((r) => { resolveFirst = r; }))
      .mockResolvedValueOnce({ succeeded: true, data: status([win({ usedPercent: 60 })]) });
    const u = inScope(() => useAiUsage({ client: { get } as never }));
    const first = u.load();
    await u.load();
    resolveFirst({ succeeded: true, data: status([win({ usedPercent: 10 })]) });
    await first;

    expect(u.windows.value[0]?.usedPercent).toBe(60);
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
