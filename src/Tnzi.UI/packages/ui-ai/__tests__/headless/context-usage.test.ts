import { describe, expect, it } from 'vitest';
import {
  CONTEXT_FULL_SHARE,
  CONTEXT_WARN_SHARE,
  compactTokens,
  contextLevel,
  contextShare,
  contextUsageOf,
  sharePercent,
} from '../../src/headless/context-usage';

describe('context usage', () => {
  it('writes token counts short', () => {
    expect(compactTokens(850)).toBe('850');
    expect(compactTokens(45_230)).toBe('45.2K');
    expect(compactTokens(1_000_000)).toBe('1M');
    expect(compactTokens(-3)).toBe('0');
  });

  it('is a share of the window only when the window is known', () => {
    expect(contextShare({ tokens: 50_000, window: 200_000 })).toBe(0.25);
    expect(contextShare({ tokens: 250_000, window: 200_000 })).toBe(1);
    expect(contextShare({ tokens: 10, window: null })).toBeNull();
    expect(contextShare({ tokens: 10, window: 0 })).toBeNull();
  });

  it('warns past 80% and says nearly full past 95%', () => {
    expect(contextLevel(null)).toBe('ok');
    expect(contextLevel(CONTEXT_WARN_SHARE - 0.01)).toBe('ok');
    expect(contextLevel(CONTEXT_WARN_SHARE)).toBe('warn');
    expect(contextLevel(CONTEXT_FULL_SHARE)).toBe('full');
  });

  it('never reads 0% for a context that holds something', () => {
    expect(sharePercent(0)).toBe('0%');
    expect(sharePercent(0.004)).toBe('<1%');
    expect(sharePercent(0.126)).toBe('13%');
  });

  it('reads a run, and a run the CLI reported nothing for has none', () => {
    expect(contextUsageOf({ contextTokens: 1200, contextWindow: 200_000 })).toEqual({ tokens: 1200, window: 200_000 });
    expect(contextUsageOf({ contextTokens: 1200 })).toEqual({ tokens: 1200, window: null });
    expect(contextUsageOf({ contextTokens: null, contextWindow: 200_000 })).toBeNull();
    expect(contextUsageOf(null)).toBeNull();
  });
});
