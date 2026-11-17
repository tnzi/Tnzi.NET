/**
 * How full a conversation's context window is: what its latest turn reported as it ended (an
 * external CLI run's `contextTokens` / `contextWindow` on `CliRunDto`). `TContextMeter` shows it.
 */
import type { CliRunDto } from '@tnzi/core/services/ai';

/** Above this share of the window the meter warns; above the second it says the window is nearly full. */
export const CONTEXT_WARN_SHARE = 0.8;
export const CONTEXT_FULL_SHARE = 0.95;

export type ContextLevel = 'ok' | 'warn' | 'full';

/** The context a turn ended with: tokens in it, and the window's size when known. */
export interface ContextUsage {
  tokens: number;
  window?: number | null;
}

const compact = new Intl.NumberFormat('en-US', { notation: 'compact', maximumFractionDigits: 1 });

/** A token count as the meter writes it: `850`, `45.2K`, `1M`. */
export function compactTokens(tokens: number): string {
  return compact.format(Math.max(0, tokens));
}

/** The share of the window used, from 0 to 1; null without a window. */
export function contextShare(usage: ContextUsage): number | null {
  if (!usage.window || usage.window <= 0) return null;
  return Math.min(1, Math.max(0, usage.tokens / usage.window));
}

export function contextLevel(share: number | null): ContextLevel {
  if (share === null || share < CONTEXT_WARN_SHARE) return 'ok';
  return share < CONTEXT_FULL_SHARE ? 'warn' : 'full';
}

/** A share as a whole percent, never 0% for a context that holds something. */
export function sharePercent(share: number): string {
  return `${share > 0 && share < 0.01 ? '<1' : Math.round(share * 100)}%`;
}

/** A run's context as the meter takes it; null when the CLI reported none. */
export function contextUsageOf(run: Pick<CliRunDto, 'contextTokens' | 'contextWindow'> | null | undefined): ContextUsage | null {
  if (!run || run.contextTokens == null) return null;
  return { tokens: run.contextTokens, window: run.contextWindow ?? null };
}
