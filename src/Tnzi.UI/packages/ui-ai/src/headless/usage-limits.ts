/**
 * Usage limits as the user sees them: which window is at its limit, how far
 * through a window they are, and when it resets.
 *
 * Shared by the settings page (`TUsageSettings`), the composer notice and a
 * refused reply (`TUsageLimitNotice`), so the three never word the same reset
 * two different ways.
 */
import type { UsageLimitWindow } from '@tnzi/core/services/ai';
import { formatAiMessage, type AiLocaleMessages } from '../i18n/index';

/** Error code of a turn refused because the user reached a usage limit (`AI_USAGE_LIMIT_REACHED`). */
export const USAGE_LIMIT_REACHED_ERROR_CODE = 'AI_USAGE_LIMIT_REACHED';

/** Error code of a reply the external CLI could not give: its own account reached its limit. */
export const CLI_USAGE_LIMIT_ERROR_CODE = 'AI_CLI_USAGE_LIMIT';

/**
 * A usage limit the user ran into: the window at its limit and when it resets.
 * `window` is null when the server did not say (a run refused after it was queued);
 * `resetsAt` is null when the limit does not reset on its own (a limit of zero).
 */
export interface UsageLimitNotice {
  window: UsageLimitWindow | null;
  resetsAt: string | null;
}

type UsageLimitTexts = AiLocaleMessages['usageLimits'];

/** Resets closer than this read as a countdown ("in 2 hr 13 min"); later ones as a day and time. */
export const RELATIVE_RESET_HORIZON_MS = 24 * 60 * 60 * 1000;

const MINUTE_MS = 60_000;
const DAY_MS = 24 * 60 * MINUTE_MS;

/**
 * The `errorDetails` of a refused turn (`UsageLimitDenialDto`), read defensively:
 * an unknown window or an unreadable date becomes null rather than a wrong value.
 */
export function readUsageLimitDenial(details: unknown): UsageLimitNotice {
  const record = details && typeof details === 'object' ? (details as Record<string, unknown>) : {};
  return {
    window: asWindow(record.window),
    resetsAt: asDate(record.resetsAt),
  };
}

function asWindow(value: unknown): UsageLimitWindow | null {
  return value === 'Session' || value === 'Weekly' ? (value as UsageLimitWindow) : null;
}

function asDate(value: unknown): string | null {
  return typeof value === 'string' && value && !Number.isNaN(new Date(value).getTime()) ? value : null;
}

/**
 * A countdown in hours and minutes. Minutes round UP: a reset 30 seconds away
 * reads "less than a minute", one 61 seconds away "2 min" - saying "1 min" then
 * would send the user back a minute too early.
 */
export function formatCountdown(ms: number, texts: UsageLimitTexts): string {
  if (ms < MINUTE_MS) return texts.lessThanMinute;
  const totalMinutes = Math.ceil(ms / MINUTE_MS);
  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  if (hours === 0) return formatAiMessage(texts.minutes, { minutes });
  if (minutes === 0) return formatAiMessage(texts.hours, { hours });
  return formatAiMessage(texts.hoursMinutes, { hours, minutes });
}

/**
 * A day and time in the dictionary's language: "Thu 10:59 PM". More than six
 * days ahead the date is added, because the weekday alone would name today.
 */
export function formatResetDay(at: Date, texts: UsageLimitTexts, now: Date = new Date()): string {
  const farAhead = at.getTime() - now.getTime() > 6 * DAY_MS;
  const options: Intl.DateTimeFormatOptions = farAhead
    ? { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' }
    : { weekday: 'short', hour: 'numeric', minute: '2-digit' };
  try {
    return at.toLocaleString(texts.dateLocale || undefined, options);
  } catch {
    // A tag Intl cannot read: the browser's own language rather than nothing.
    return at.toLocaleString(undefined, options);
  }
}

/** When a reset happens, either as a countdown or as a day and time. Null for a missing or unreadable date. */
export function describeReset(
  iso: string | null | undefined,
  texts: UsageLimitTexts,
  now: Date = new Date(),
): { relative: boolean; time: string } | null {
  if (!iso) return null;
  const at = new Date(iso);
  if (Number.isNaN(at.getTime())) return null;
  const ms = at.getTime() - now.getTime();
  return ms < RELATIVE_RESET_HORIZON_MS
    ? { relative: true, time: formatCountdown(Math.max(0, ms), texts) }
    : { relative: false, time: formatResetDay(at, texts, now) };
}

/** "Resets in 2 hr 13 min" / "Resets Thu 10:59 PM", or null when the reset is not known. */
export function formatResetLabel(iso: string | null | undefined, texts: UsageLimitTexts, now: Date = new Date()): string | null {
  const reset = describeReset(iso, texts, now);
  if (!reset) return null;
  return formatAiMessage(reset.relative ? texts.resetsIn : texts.resetsAt, { time: reset.time });
}

/** The sentence after "You've reached ...": when it resets, or who can lift a limit that does not. */
export function formatResetSentence(iso: string | null | undefined, texts: UsageLimitTexts, now: Date = new Date()): string {
  const reset = describeReset(iso, texts, now);
  if (!reset) return texts.noReset;
  return formatAiMessage(reset.relative ? texts.resetSentenceIn : texts.resetSentenceAt, { time: reset.time });
}

/** "You've reached your session limit." for the window, or the general sentence when it is not known. */
export function formatLimitReached(window: UsageLimitWindow | null | undefined, texts: UsageLimitTexts): string {
  if (window === 'Session') return texts.reachedSession;
  if (window === 'Weekly') return texts.reachedWeekly;
  return texts.reached;
}

/** A window's length for a hint: "5 hours", "7 days", "90 minutes". */
export function formatWindowDuration(minutes: number, texts: UsageLimitTexts): string {
  if (!Number.isFinite(minutes) || minutes <= 0) return formatAiMessage(texts.durationMinutes, { count: 0 });
  if (minutes % (24 * 60) === 0) {
    const days = minutes / (24 * 60);
    return days === 1 ? texts.durationDay : formatAiMessage(texts.durationDays, { count: days });
  }
  if (minutes % 60 === 0) {
    const hours = minutes / 60;
    return hours === 1 ? texts.durationHour : formatAiMessage(texts.durationHours, { count: hours });
  }
  return formatAiMessage(texts.durationMinutes, { count: minutes });
}

/**
 * A share as a whole percentage for display, 0 to 100. The server can report
 * a little over 100 (the turn that crossed the limit was allowed to finish);
 * "104% used" says nothing "100% used" does not, and a bar cannot be wider
 * than its track.
 */
export function wholePercent(value: number | null | undefined): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) return 0;
  return Math.max(0, Math.min(100, Math.round(value)));
}

/** Whether a reset time is still ahead (an unreadable or missing date counts as ahead: it does not reset by itself). */
export function isResetAhead(iso: string | null | undefined, now: Date = new Date()): boolean {
  if (!iso) return true;
  const at = new Date(iso).getTime();
  return Number.isNaN(at) || at > now.getTime();
}
