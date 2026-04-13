import { describe, it, expect } from 'vitest';
import { useTokenCounter } from '../../src/composables/useTokenCounter';
import type { TokenUsage } from '../../src/composables/useChat';

function makeUsage(input: number, output: number, total?: number): TokenUsage {
  return {
    inputTokens: input,
    outputTokens: output,
    totalTokens: total ?? input + output,
    cachedInputTokens: 0,
    cacheCreationTokens: 0,
  };
}

describe('useTokenCounter', () => {
  it('should initialize with zero usage', () => {
    const counter = useTokenCounter();
    expect(counter.usage.value.inputTokens).toBe(0);
    expect(counter.usage.value.outputTokens).toBe(0);
    expect(counter.totalTokens.value).toBe(0);
    expect(counter.estimatedCost.value).toBe(0);
    expect(counter.modelId.value).toBeNull();
  });

  it('should accept initial modelId', () => {
    const counter = useTokenCounter({ modelId: 'gpt-4o' });
    expect(counter.modelId.value).toBe('gpt-4o');
  });

  it('should update usage', () => {
    const counter = useTokenCounter();
    counter.update(makeUsage(1000, 500));
    expect(counter.usage.value.inputTokens).toBe(1000);
    expect(counter.usage.value.outputTokens).toBe(500);
    expect(counter.totalTokens.value).toBe(1500);
  });

  it('should update usage with model change', () => {
    const counter = useTokenCounter();
    counter.update(makeUsage(1000, 500), 'claude-3.5-sonnet');
    expect(counter.modelId.value).toBe('claude-3.5-sonnet');
  });

  it('should accumulate token deltas', () => {
    const counter = useTokenCounter();
    counter.update(makeUsage(100, 50));
    counter.accumulate({ inputTokens: 200, outputTokens: 100, totalTokens: 300 });
    expect(counter.usage.value.inputTokens).toBe(300);
    expect(counter.usage.value.outputTokens).toBe(150);
    expect(counter.totalTokens.value).toBe(450);
  });

  it('should accumulate with partial deltas', () => {
    const counter = useTokenCounter();
    counter.update(makeUsage(100, 50));
    counter.accumulate({ outputTokens: 25, totalTokens: 25 });
    expect(counter.usage.value.inputTokens).toBe(100);
    expect(counter.usage.value.outputTokens).toBe(75);
  });

  it('should reset to zero', () => {
    const counter = useTokenCounter({ modelId: 'gpt-4' });
    counter.update(makeUsage(1000, 500));
    counter.reset();
    expect(counter.usage.value.inputTokens).toBe(0);
    expect(counter.usage.value.outputTokens).toBe(0);
    expect(counter.totalTokens.value).toBe(0);
    // modelId is NOT reset
    expect(counter.modelId.value).toBe('gpt-4');
  });

  it('should estimate cost for gpt-4o', () => {
    const counter = useTokenCounter({ modelId: 'gpt-4o' });
    // gpt-4o: input $2.5/1M, output $10/1M
    counter.update(makeUsage(1_000_000, 1_000_000));
    expect(counter.estimatedCost.value).toBeCloseTo(12.5, 2);
  });

  it('should estimate cost for claude-3.5-sonnet', () => {
    const counter = useTokenCounter({ modelId: 'claude-3.5-sonnet' });
    // claude-3.5-sonnet: input $3/1M, output $15/1M
    counter.update(makeUsage(500_000, 200_000));
    expect(counter.estimatedCost.value).toBeCloseTo(1.5 + 3.0, 2);
  });

  it('should return 0 cost for unknown model', () => {
    const counter = useTokenCounter({ modelId: 'unknown-model' });
    counter.update(makeUsage(1000, 500));
    expect(counter.estimatedCost.value).toBe(0);
  });

  it('should return 0 cost when no model is set', () => {
    const counter = useTokenCounter();
    counter.update(makeUsage(1000, 500));
    expect(counter.estimatedCost.value).toBe(0);
  });

  it('should match model by prefix (versioned model names)', () => {
    const counter = useTokenCounter({ modelId: 'claude-3.5-sonnet-20241022' });
    counter.update(makeUsage(1_000_000, 0));
    // Should match claude-3.5-sonnet pricing ($3/1M input)
    expect(counter.estimatedCost.value).toBeCloseTo(3.0, 2);
  });

  it('should handle zero tokens', () => {
    const counter = useTokenCounter({ modelId: 'gpt-4o' });
    counter.update(makeUsage(0, 0));
    expect(counter.estimatedCost.value).toBe(0);
  });
});
