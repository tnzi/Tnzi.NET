import { describe, it, expect } from 'vitest';
import { useTokenCounter, type ModelPricing } from '../../src/headless/useTokenCounter';
import type { TokenUsage } from '../../src/headless/useChat';

/* Pricing is consumer-owned on purpose (the backend's ICostCalculator is
   authoritative), so the tests bring their own table instead of asserting
   against a built-in one that would go stale. */
const PRICING: Record<string, ModelPricing> = {
  'gpt-4o': { inputPer1M: 2.5, outputPer1M: 10.0 },
  'claude-3.5-sonnet': { inputPer1M: 3.0, outputPer1M: 15.0 },
};

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

  it('should estimate cost from consumer-supplied pricing', () => {
    const counter = useTokenCounter({ modelId: 'gpt-4o', pricing: PRICING });
    // gpt-4o: input $2.5/1M, output $10/1M
    counter.update(makeUsage(1_000_000, 1_000_000));
    expect(counter.estimatedCost.value).toBeCloseTo(12.5, 2);
  });

  it('should estimate cost for a second model in the same table', () => {
    const counter = useTokenCounter({ modelId: 'claude-3.5-sonnet', pricing: PRICING });
    // claude-3.5-sonnet: input $3/1M, output $15/1M
    counter.update(makeUsage(500_000, 200_000));
    expect(counter.estimatedCost.value).toBeCloseTo(1.5 + 3.0, 2);
  });

  it('should return 0 cost for unknown model', () => {
    const counter = useTokenCounter({ modelId: 'unknown-model', pricing: PRICING });
    counter.update(makeUsage(1000, 500));
    expect(counter.estimatedCost.value).toBe(0);
  });

  it('should return 0 cost when no model is set', () => {
    const counter = useTokenCounter({ pricing: PRICING });
    counter.update(makeUsage(1000, 500));
    expect(counter.estimatedCost.value).toBe(0);
  });

  it('should ship no built-in pricing table', () => {
    const counter = useTokenCounter({ modelId: 'gpt-4o' });
    counter.update(makeUsage(1_000_000, 1_000_000));
    expect(counter.estimatedCost.value).toBe(0);
  });

  it('should match model by longest prefix (versioned model names)', () => {
    const counter = useTokenCounter({
      modelId: 'claude-3.5-sonnet-20241022',
      pricing: { ...PRICING, 'claude-3': { inputPer1M: 99, outputPer1M: 99 } },
    });
    counter.update(makeUsage(1_000_000, 0));
    // The longer "claude-3.5-sonnet" key wins over the shorter "claude-3" one.
    expect(counter.estimatedCost.value).toBeCloseTo(3.0, 2);
  });

  it('should handle zero tokens', () => {
    const counter = useTokenCounter({ modelId: 'gpt-4o', pricing: PRICING });
    counter.update(makeUsage(0, 0));
    expect(counter.estimatedCost.value).toBe(0);
  });

  it('should prefer a reported cost over client-side estimation', () => {
    const counter = useTokenCounter({ modelId: 'gpt-4o', pricing: PRICING });
    counter.update(makeUsage(1_000_000, 1_000_000));
    counter.setCost(9.99);
    expect(counter.estimatedCost.value).toBe(9.99);
  });

  it('should fall back to estimation when the reported cost is cleared', () => {
    const counter = useTokenCounter({ modelId: 'gpt-4o', pricing: PRICING });
    counter.update(makeUsage(1_000_000, 1_000_000));
    counter.setCost(9.99);
    counter.setCost(null);
    expect(counter.estimatedCost.value).toBeCloseTo(12.5, 2);
  });

  it('should clear the reported cost on reset', () => {
    const counter = useTokenCounter({ modelId: 'gpt-4o', pricing: PRICING });
    counter.setCost(9.99);
    counter.reset();
    expect(counter.estimatedCost.value).toBe(0);
  });
});
