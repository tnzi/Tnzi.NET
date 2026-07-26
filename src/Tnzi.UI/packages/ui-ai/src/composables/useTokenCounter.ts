/**
 * useTokenCounter - accumulates token usage for a conversation.
 *
 * ## Where cost comes from
 *
 * The backend is authoritative on money: `ICostCalculator` stamps
 * `EstimatedCostUsd` on every AI usage log entry, driven by pricing that lives
 * in configuration. This composable therefore ships **no** pricing table of its
 * own - a hardcoded one in the UI would be a second source of truth that
 * silently drifts from the server every time a vendor changes a price.
 *
 * Prefer `setCost()` with the value the API returned. If an app genuinely needs
 * client-side estimation (offline preview, a "what would this cost" widget), it
 * passes its own `pricing` map and owns keeping it current.
 */

import { ref, readonly, computed, type Ref, type DeepReadonly, type ComputedRef } from 'vue';
import type { TokenUsage } from './useChat';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** USD per 1M tokens for one model. */
export interface ModelPricing {
  inputPer1M: number;
  outputPer1M: number;
}

export interface UseTokenCounterOptions {
  /** Initial model ID. */
  modelId?: string | null;
  /**
   * Consumer-owned pricing map, keyed by model id (an exact match wins, then
   * the longest key the model id starts with). Only consulted when no explicit
   * cost has been reported through `setCost`.
   */
  pricing?: Readonly<Record<string, ModelPricing>>;
}

export interface UseTokenCounterReturn {
  /** Current accumulated usage. */
  usage: DeepReadonly<Ref<TokenUsage>>;
  /** Current model ID. */
  modelId: DeepReadonly<Ref<string | null>>;
  /** Total tokens (computed). */
  totalTokens: ComputedRef<number>;
  /**
   * Cost in USD: the value reported through `setCost` when there is one,
   * otherwise derived from `options.pricing`, otherwise 0.
   */
  estimatedCost: ComputedRef<number>;
  /** Update usage (replaces current). Optionally change model. */
  update: (newUsage: TokenUsage, model?: string) => void;
  /** Accumulate a delta on top of current usage. */
  accumulate: (delta: Partial<TokenUsage>) => void;
  /** Record the server-calculated cost for this conversation. Pass null to
   *  fall back to client-side estimation. */
  setCost: (usd: number | null) => void;
  /** Reset usage and reported cost to zero. */
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

function findPricing(
  table: Readonly<Record<string, ModelPricing>>,
  model: string | null,
): ModelPricing | null {
  if (!model) return null;
  // Exact match first
  const exact = table[model];
  if (exact) return exact;
  // Longest prefix match, so "claude-3.5-sonnet-20241022" prefers the
  // "claude-3.5-sonnet" entry over a shorter "claude-3" one.
  const normalizedModel = model.toLowerCase();
  let best: ModelPricing | null = null;
  let bestLength = -1;
  for (const [key, pricing] of Object.entries(table)) {
    if (normalizedModel.startsWith(key.toLowerCase()) && key.length > bestLength) {
      best = pricing;
      bestLength = key.length;
    }
  }
  return best;
}

// ---------------------------------------------------------------------------
// Composable
// ---------------------------------------------------------------------------

export function useTokenCounter(options: UseTokenCounterOptions = {}): UseTokenCounterReturn {
  const usage = ref<TokenUsage>(emptyUsage());
  const modelId = ref<string | null>(options.modelId ?? null);
  const reportedCost = ref<number | null>(null);
  const pricingTable = options.pricing ?? {};

  const totalTokens = computed(() => usage.value.totalTokens);

  const estimatedCost = computed(() => {
    if (reportedCost.value != null) return reportedCost.value;
    const pricing = findPricing(pricingTable, modelId.value);
    if (!pricing) return 0;
    const inputCost = (usage.value.inputTokens / 1_000_000) * pricing.inputPer1M;
    const outputCost = (usage.value.outputTokens / 1_000_000) * pricing.outputPer1M;
    return inputCost + outputCost;
  });

  function setCost(usd: number | null): void {
    reportedCost.value = usd;
  }

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
    reportedCost.value = null;
  }

  return {
    usage: readonly(usage),
    modelId: readonly(modelId),
    totalTokens,
    estimatedCost,
    update,
    accumulate,
    setCost,
    reset,
  };
}
