<script setup lang="ts">
/**
 * TTokenUsage - Token usage indicator
 *
 * SVG circular progress ring with a hover card showing
 * breakdown of input/output/cached/reasoning tokens and cost.
 */

import { NPopover, NDivider } from 'naive-ui';
import { computed } from 'vue';
import { useAiI18n } from '../../i18n/index';
import { formatCompactNumber } from '../../utils/format';
import type { TokenUsage } from '../../headless/useChat';
const props = withDefaults(defineProps<{
  usage: TokenUsage;
  /** Maximum token budget (for progress ring). */
  maxTokens?: number;
  estimatedCost?: number;
}>(), {
  maxTokens: 128000,
  estimatedCost: 0,
});

const t = useAiI18n();

// SVG circle math
const RADIUS = 18;
const CIRCUMFERENCE = 2 * Math.PI * RADIUS;

const progress = computed(() => {
  if (!props.maxTokens || props.maxTokens === 0) return 0;
  return Math.min(props.usage.totalTokens / props.maxTokens, 1);
});

const strokeDashoffset = computed(() => {
  return CIRCUMFERENCE * (1 - progress.value);
});

const progressColor = computed(() => {
  if (progress.value > 0.9) return 'text-ai-node-failed';
  if (progress.value > 0.7) return 'text-yellow-500';
  return 'text-ai-node-active';
});

function formatCost(cost: number): string {
  if (cost < 0.01) return `$${cost.toFixed(4)}`;
  return `$${cost.toFixed(2)}`;
}
</script>

<template>
  <NPopover trigger="hover">
    <template #trigger>
      <!-- Ring + label -->
      <div class="inline-flex items-center gap-1.5 cursor-default">
        <svg width="44" height="44" viewBox="0 0 44 44" class="-rotate-90">
          <!-- Background ring -->
          <circle
            cx="22"
            cy="22"
            :r="RADIUS"
            fill="none"
            stroke="currentColor"
            stroke-width="3"
            class="text-tnzi-border"
          />
          <!-- Progress ring -->
          <circle
            cx="22"
            cy="22"
            :r="RADIUS"
            fill="none"
            stroke="currentColor"
            stroke-width="3"
            stroke-linecap="round"
            :class="progressColor"
            :stroke-dasharray="CIRCUMFERENCE"
            :stroke-dashoffset="strokeDashoffset"
            class="transition-[stroke-dashoffset] duration-500 ease-out"
          />
        </svg>

        <span class="text-xs tabular-nums text-tnzi-muted">
          {{ formatCompactNumber(usage.totalTokens) }}
        </span>
      </div>
    </template>

    <div class="w-52 space-y-2 text-sm">
        <!-- Input tokens -->
        <div class="flex items-center justify-between">
          <span class="text-tnzi-muted">{{ t.token.input }}</span>
          <span class="tabular-nums font-medium">{{ formatCompactNumber(usage.inputTokens) }}</span>
        </div>

        <!-- Output tokens -->
        <div class="flex items-center justify-between">
          <span class="text-tnzi-muted">{{ t.token.output }}</span>
          <span class="tabular-nums font-medium">{{ formatCompactNumber(usage.outputTokens) }}</span>
        </div>

        <!-- Cached tokens (show only if > 0) -->
        <div
          v-if="usage.cachedInputTokens > 0"
          class="flex items-center justify-between"
        >
          <span class="text-tnzi-muted">{{ t.token.cached }}</span>
          <span class="tabular-nums font-medium">{{ formatCompactNumber(usage.cachedInputTokens) }}</span>
        </div>

        <!-- Cache creation tokens (show only if > 0) -->
        <div
          v-if="usage.cacheCreationTokens > 0"
          class="flex items-center justify-between"
        >
          <span class="text-tnzi-muted">{{ t.token.cacheWrite }}</span>
          <span class="tabular-nums font-medium">{{ formatCompactNumber(usage.cacheCreationTokens) }}</span>
        </div>

        <!-- Separator -->
        <NDivider style="margin: 8px 0" />

        <!-- Total -->
        <div class="flex items-center justify-between">
          <span class="text-tnzi-muted">{{ t.token.total }}</span>
          <span class="tabular-nums font-semibold">{{ formatCompactNumber(usage.totalTokens) }}</span>
        </div>

        <!-- Cost -->
        <div
          v-if="estimatedCost > 0"
          class="flex items-center justify-between"
        >
          <span class="text-tnzi-muted">{{ t.token.cost }}</span>
          <span class="tabular-nums font-semibold text-ai-node-active">
            {{ formatCost(estimatedCost) }}
          </span>
        </div>
    </div>
  </NPopover>
</template>
