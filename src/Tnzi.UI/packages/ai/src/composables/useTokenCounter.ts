/**
 * useTokenCounter — Token counting and cost estimation
 *
 * Tracks token usage across a conversation and estimates cost based on
 * a built-in pricing table for common LLM models.
 */

import { ref, readonly, computed, type Ref, type DeepReadonly, type ComputedRef } from 'vue';
import type { TokenUsage } from './useChat';

// ---------------------------------------------------------------------------
// Pricing Table (USD per 1M tokens)
// ---------------------------------------------------------------------------

interface ModelPricing {
  inputPer1M: number;
  outputPer1M: number;
}

const PRICING_TABLE: Record<string, ModelPricing> = {
  // OpenAI
  'gpt-4': { inputPer1M: 30.0, outputPer1M: 60.0 },
  'gpt-4-turbo': { inputPer1M: 10.0, outputPer1M: 30.0 },
  'gpt-4o': { inputPer1M: 2.5, outputPer1M: 10.0 },
  'gpt-4o-mini': { inputPer1M: 0.15, outputPer1M: 0.60 },
  'gpt-4.1': { inputPer1M: 2.0, outputPer1M: 8.0 },
  'gpt-4.1-mini': { inputPer1M: 0.40, outputPer1M: 1.60 },
  'gpt-4.1-nano': { inputPer1M: 0.10, outputPer1M: 0.40 },
  'o1': { inputPer1M: 15.0, outputPer1M: 60.0 },
  'o1-mini': { inputPer1M: 1.10, outputPer1M: 4.40 },
  'o3': { inputPer1M: 2.0, outputPer1M: 8.0 },
  'o3-mini': { inputPer1M: 1.10, outputPer1M: 4.40 },
  'o4-mini': { inputPer1M: 1.10, outputPer1M: 4.40 },
  // Anthropic
  'claude-3-haiku': { inputPer1M: 0.25, outputPer1M: 1.25 },
  'claude-3-sonnet': { inputPer1M: 3.0, outputPer1M: 15.0 },
  'claude-3-opus': { inputPer1M: 15.0, outputPer1M: 75.0 },
  'claude-3.5-haiku': { inputPer1M: 0.80, outputPer1M: 4.0 },
  'claude-3.5-sonnet': { inputPer1M: 3.0, outputPer1M: 15.0 },
  'claude-3.7-sonnet': { inputPer1M: 3.0, outputPer1M: 15.0 },
  'claude-4-sonnet': { inputPer1M: 3.0, outputPer1M: 15.0 },
  'claude-4-opus': { inputPer1M: 15.0, outputPer1M: 75.0 },
  'claude-4.5-haiku': { inputPer1M: 0.80, outputPer1M: 4.0 },
  // Google
  'gemini-2.0-flash': { inputPer1M: 0.10, outputPer1M: 0.40 },
  'gemini-2.5-pro': { inputPer1M: 1.25, outputPer1M: 10.0 },
  'gemini-2.5-flash': { inputPer1M: 0.15, outputPer1M: 0.60 },
  // DeepSeek
  'deepseek-r1': { inputPer1M: 0.55, outputPer1M: 2.19 },
  'deepseek-v3': { inputPer1M: 0.27, outputPer1M: 1.10 },
};

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface UseTokenCounterOptions {
  /** Initial model ID for cost estimation. */
  modelId?: string | null;
}

export interface UseTokenCounterReturn {
  /** Current accumulated usage. */
  usage: DeepReadonly<Ref<TokenUsage>>;
  /** Current model ID. */
  modelId: DeepReadonly<Ref<string | null>>;
  /** Total tokens (computed). */
  totalTokens: ComputedRef<number>;
  /** Estimated cost in USD (computed). */
  estimatedCost: ComputedRef<number>;
  /** Update usage (replaces current). Optionally change model. */
  update: (newUsage: TokenUsage, model?: string) => void;
  /** Accumulate a delta on top of current usage. */
  accumulate: (delta: Partial<TokenUsage>) => void;
  /** Reset usage to zero. */
  reset: () => void;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function emptyUsage(): TokenUsage {
  return {
    inputTokens: 0,
    outputTokens: 0,
    totalTokens: 0,
    cachedInputTokens: 0,
    cacheCreationTokens: 0,
  };
}

function findPricing(model: string | null): ModelPricing | null {
  if (!model) return null;
  // Exact match first
  const exact = PRICING_TABLE[model];
  if (exact) return exact;
  // Prefix match (e.g., "claude-3.5-sonnet-20241022" → "claude-3.5-sonnet")
  const normalizedModel = model.toLowerCase();
  for (const [key, pricing] of Object.entries(PRICING_TABLE)) {
    if (normalizedModel.startsWith(key)) return pricing;
  }
  return null;
}

// ---------------------------------------------------------------------------
// Composable
// ---------------------------------------------------------------------------

export function useTokenCounter(options: UseTokenCounterOptions = {}): UseTokenCounterReturn {
  const usage = ref<TokenUsage>(emptyUsage());
  const modelId = ref<string | null>(options.modelId ?? null);

  const totalTokens = computed(() => usage.value.totalTokens);

  const estimatedCost = computed(() => {
    const pricing = findPricing(modelId.value);
    if (!pricing) return 0;
    const inputCost = (usage.value.inputTokens / 1_000_000) * pricing.inputPer1M;
    const outputCost = (usage.value.outputTokens / 1_000_000) * pricing.outputPer1M;
    return inputCost + outputCost;
  });

  function update(newUsage: TokenUsage, model?: string): void {
    usage.value = { ...newUsage };
    if (model !== undefined) {
      modelId.value = model;
    }
  }

  function accumulate(delta: Partial<TokenUsage>): void {
    const current = usage.value;
    usage.value = {
      inputTokens: current.inputTokens + (delta.inputTokens ?? 0),
      outputTokens: current.outputTokens + (delta.outputTokens ?? 0),
      totalTokens: current.totalTokens + (delta.totalTokens ?? 0),
      cachedInputTokens: current.cachedInputTokens + (delta.cachedInputTokens ?? 0),
      cacheCreationTokens: current.cacheCreationTokens + (delta.cacheCreationTokens ?? 0),
    };
  }

  function reset(): void {
    usage.value = emptyUsage();
  }

  return {
    usage: readonly(usage),
    modelId: readonly(modelId),
    totalTokens,
    estimatedCost,
    update,
    accumulate,
    reset,
  };
}
