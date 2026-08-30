/**
 * @tnzi/core/utils/date
 *
 * Date formatting utilities.
 */

import { createAdapterSingleton } from '../adapters/singleton';

/**
 * A named display form, mirroring `Intl.DateTimeFormat`'s `dateStyle` /
 * `timeStyle` values one for one - no translation layer, no second vocabulary
 * to learn.
 *
 * In `en-US`: `short` is `7/22/26`, `medium` is `Jul 22, 2026`, `long` is
 * `July 22, 2026`, `full` is `Tuesday, July 22, 2026`. Every one of them stays
 * locale-aware: `medium` renders `2026年7月22日` under `zh-CN`.
 *
 * `medium` is the usual answer for an app that wants unambiguous dates. A
 * numeric date reads as day/month in most of the world and month/day in the
 * US, and `toLocaleDateString(undefined)` picks whichever the viewer's browser
 * prefers - so the same record reads as two different days for two colleagues.
 * Naming the month removes that entirely, in every locale.
 */
export type DateDisplayStyle = 'short' | 'medium' | 'long' | 'full';

/** The display-form half of the options, shared by defaults and per-call. */
interface DateStyleOptions {
  /** Applies to {@link formatDateOnly} and the date half of {@link formatDateTime}. */
  dateStyle?: DateDisplayStyle;
  /** Applies to the time half of {@link formatDateTime}. Ignored by {@link formatDateOnly}. */
  timeStyle?: DateDisplayStyle;
}

/**
 * Which language to render in: a BCP-47 tag, or a getter returning one.
 *
 * The getter form is what makes a language switch visible without a reload -
 * see {@link setDateFormatDefaults}. Returning nothing means "follow the
 * browser", which is what these helpers did before a locale could be named.
 */
export type DateLocaleSource = string | (() => string | null | undefined);

/** Display forms and language applied when a call site does not name its own. */
export interface DateFormatDefaults extends DateStyleOptions {
  /**
   * The application's UI language. Tag casing does not matter - Intl
   * canonicalises, so `'zh-cn'` and `'zh-CN'` are the same locale.
   */
  locale?: DateLocaleSource;
}

/**
 * Process-wide default display forms. Empty by default, which is what keeps
 * every existing call rendering exactly what it renders today.
 *
 * Parked on the shared adapter registry rather than a module-level `let`: this
 * package is built unsplit across ~20 entries, so a plain module variable is
 * copied into each bundle and the setter would land in a different copy from
 * the reader. See `adapters/singleton`.
 */
const dateFormatDefaults = createAdapterSingleton<DateFormatDefaults>(
  'dateFormatDefaults',
  () => ({}),
);

/**
 * Set the display form every date helper falls back to.
 *
 * This exists because a host does not own all of its own call sites: the
 * built-in `@tnzi/ui-admin` pages format their own dates, so "one date style
 * across this UI" is not reachable by passing an option at each call. Set it
 * once at boot:
 *
 * ```ts
 * import { setDateFormatDefaults } from '@tnzi/core/utils'
 *
 * setDateFormatDefaults({ dateStyle: 'medium' })   // Jul 22, 2026
 * ```
 *
 * A per-call `dateStyle` / `timeStyle` / `locale` still wins, so a screen that
 * genuinely needs a compact numeric date, or a fixed language, can ask for one.
 *
 * ## Language
 *
 * Without a `locale`, Intl resolves the **browser's** language, which is not
 * the language the application is running its UI in. The framework cannot read
 * that language for you: `@tnzi/ui`'s `provideI18n` fires once at plugin
 * install, `@tnzi/ui-admin` drives its own store and never calls into it, and
 * an app may use neither. Only the application knows, so it tells us:
 *
 * ```ts
 * // Fixed for the life of the page.
 * setDateFormatDefaults({ locale: 'zh-CN', dateStyle: 'medium' })
 *
 * // Follows a language switch with no reload - see below.
 * setDateFormatDefaults({ locale: () => appStore.locale, dateStyle: 'medium' })
 * ```
 *
 * ## Whether a switch needs a reload
 *
 * The defaults are read at format time, never watched, so this is decided by
 * what you pass:
 *
 * - **A string** - dates keep rendering in it until the setter is called
 *   again. Calling it again does not repaint anything already on screen, so a
 *   switch shows up only as components happen to re-render. Treat it as
 *   needing a reload.
 * - **A getter over a reactive source** (a pinia store field, a `ref`) - the
 *   getter runs inside whatever effect is formatting, so a component that
 *   formatted a date while rendering tracks that source and re-renders when it
 *   changes. No reload. This is the same mechanism `@tnzi/ui-admin` relies on
 *   to repaint labels when a late dictionary chunk lands.
 *
 * A date formatted once outside a render (in `onMounted`, into a plain
 * variable) is a string from then on and will not update either way.
 */
export function setDateFormatDefaults(defaults: DateFormatDefaults): void {
  dateFormatDefaults.set({ ...defaults });
}

/** The defaults currently in effect. Empty object when none were set. */
export function getDateFormatDefaults(): DateFormatDefaults {
  return dateFormatDefaults.use();
}

/** Drop the defaults, restoring the locale's own forms. For tests / SSR isolation. */
export function resetDateFormatDefaults(): void {
  dateFormatDefaults.reset();
}

/**
 * Merge per-call styles over the app defaults for a date+time render.
 *
 * Returns `undefined` when neither half was named anywhere, so the untouched
 * case stays a bare `toLocaleString()` call rather than a reconstruction of it.
 * When only one half is named, the other keeps the form today's bare call gives
 * it - a numeric date and a time with seconds - so turning one knob changes
 * only its own half.
 */
function resolveDateTimeStyles(options?: DateStyleOptions): Intl.DateTimeFormatOptions | undefined {
  const defaults = getDateFormatDefaults();
  const dateStyle = options?.dateStyle ?? defaults.dateStyle;
  const timeStyle = options?.timeStyle ?? defaults.timeStyle;
  if (!dateStyle && !timeStyle) return undefined;
  return { dateStyle: dateStyle ?? 'short', timeStyle: timeStyle ?? 'medium' };
}

/**
 * The language to render in: what the call asked for, else the app default,
 * else nothing (Intl falls back to the browser, the behaviour these helpers
 * had before a locale could be named).
 *
 * The default is read - and a getter called - on every format, which is
 * precisely what lets a reactive getter make a language switch repaint.
 */
function resolveLocale(explicit?: string): string | undefined {
  if (explicit) return explicit;
  const source = getDateFormatDefaults().locale;
  const resolved = typeof source === 'function' ? source() : source;
  return resolved || undefined;
}

/** Options shared by {@link formatDateTime} and {@link formatDateOnly}. */
export interface FormatDateTimeOptions {
  /** Rendered for null / undefined / empty input. Default `''`. */
  fallback?: string;
  /** BCP-47 tag to render in. Falls back to {@link setDateFormatDefaults}, then the browser. */
  locale?: string;
  /** Display form for the date half. Falls back to {@link setDateFormatDefaults}. */
  dateStyle?: DateDisplayStyle;
  /** Display form for the time half. Falls back to {@link setDateFormatDefaults}. */
  timeStyle?: DateDisplayStyle;
}

/** Options for {@link formatDateOnly}. */
export interface FormatDateOnlyOptions {
  /** Rendered for null / undefined / empty input. Default `''`. */
  fallback?: string;
  /** BCP-47 tag to render in. Falls back to {@link setDateFormatDefaults}, then the browser. */
  locale?: string;
  /** Render the UTC calendar date rather than the viewer's. */
  utc?: boolean;
  /** Display form. Falls back to {@link setDateFormatDefaults}. */
  dateStyle?: DateDisplayStyle;
}

/**
 * Format date to ISO string
 */
export function formatDate(date: Date | string, format = 'yyyy-MM-dd'): string {
  const d = typeof date === 'string' ? new Date(date) : date;

  const year = d.getFullYear();
  const month = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  const hours = String(d.getHours()).padStart(2, '0');
  const minutes = String(d.getMinutes()).padStart(2, '0');
  const seconds = String(d.getSeconds()).padStart(2, '0');

  return format
    .replace('yyyy', String(year))
    .replace('MM', month)
    .replace('dd', day)
    .replace('HH', hours)
    .replace('mm', minutes)
    .replace('ss', seconds);
}

/**
 * Locale-aware date+time formatter (`toLocaleString`).
 *
 * Null/undefined/empty → `fallback` (default `''`). Invalid input falls back
 * to the original string when one was given. Use this for "human" timestamps
 * in tables/detail panes; use {@link formatDate} for fixed `yyyy-MM-dd` output.
 *
 * `dateStyle` / `timeStyle` name the display form and `locale` the language;
 * with none of them, and no app default set, the output is the browser
 * locale's own - unchanged from before those options existed.
 */
export function formatDateTime(
  value: string | number | Date | null | undefined,
  options?: FormatDateTimeOptions,
): string {
  if (value === null || value === undefined || value === '') return options?.fallback ?? '';
  // `new Date(invalid)` does not throw - it yields an Invalid Date whose
  // toLocaleString() is the literal "Invalid Date". Guard explicitly so
  // unparseable input falls back to the original string (or fallback).
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) {
    return typeof value === 'string' ? value : (options?.fallback ?? '');
  }
  const styles = resolveDateTimeStyles(options);
  const locale = resolveLocale(options?.locale);
  if (!locale && !styles) return d.toLocaleString();
  try {
    return d.toLocaleString(locale, styles);
  } catch {
    // A locale tag Intl cannot parse must not take the screen down with it -
    // a timestamp is furniture, not the point of the page. Fall back to the
    // browser's language, which is what an app that names none already gets.
    return styles ? d.toLocaleString(undefined, styles) : d.toLocaleString();
  }
}

/**
 * Locale-aware date-only formatter (`toLocaleDateString`). Same
 * null/fallback/`dateStyle` semantics as {@link formatDateTime}.
 *
 * `utc: true` renders the UTC calendar date instead of the local one. Use it
 * for date-only fields the backend stores as UTC midnight (posting dates,
 * rate dates, period start/end): without it, any viewer west of UTC sees the
 * previous day. Leave it off for real timestamps (creation/modification
 * times), where the local calendar date is the correct rendering.
 *
 * `dateStyle` and `utc` are orthogonal - which calendar to read and how to
 * spell it are two different questions, and a date-only field usually wants an
 * answer to both.
 */
export function formatDateOnly(
  value: string | number | Date | null | undefined,
  options?: FormatDateOnlyOptions,
): string {
  if (value === null || value === undefined || value === '') return options?.fallback ?? '';
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) {
    return typeof value === 'string' ? value : (options?.fallback ?? '');
  }
  const dateStyle = options?.dateStyle ?? getDateFormatDefaults().dateStyle;
  const timeZone = options?.utc ? 'UTC' : undefined;
  const locale = resolveLocale(options?.locale);
  if (!locale && !dateStyle && !timeZone) return d.toLocaleDateString();
  // Intl reads an explicitly-undefined property as absent, so this is the same
  // call as passing only the parts that were asked for.
  try {
    return d.toLocaleDateString(locale, { dateStyle, timeZone });
  } catch {
    return d.toLocaleDateString(undefined, { dateStyle, timeZone });
  }
}

/**
 * Picker timestamp (local midnight) → `yyyy-MM-dd` the backend DateTime binder
 * accepts.
 *
 * Companion to {@link formatDateOnly} (display) for editing date-only /
 * UTC-midnight fields: a date picker must NOT round-trip through
 * `new Date(iso)` or a viewer west of UTC lands on the previous calendar day.
 * Prefill the picker with {@link dateOnlyToPickerTs} and save with this.
 */
export function pickerTsToDateOnly(ts: number): string {
  const d = new Date(ts);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/**
 * Backend date-only value (`yyyy-MM-dd` or UTC-midnight ISO) → local-midnight
 * timestamp to prefill a date picker on the SAME calendar day. Reads the date
 * parts from the string directly (not `new Date`) to avoid the UTC→local shift.
 */
export function dateOnlyToPickerTs(dateOnly: string): number {
  const y = Number(dateOnly.slice(0, 4));
  const m = Number(dateOnly.slice(5, 7));
  const d = Number(dateOnly.slice(8, 10));
  return new Date(y, m - 1, d).getTime();
}

/**
 * Format relative time
 */
export function formatRelativeTime(date: Date | string): string {
  const d = typeof date === 'string' ? new Date(date) : date;
  const now = new Date();
  const diff = now.getTime() - d.getTime();

  const seconds = Math.floor(diff / 1000);
  const minutes = Math.floor(seconds / 60);
  const hours = Math.floor(minutes / 60);
  const days = Math.floor(hours / 24);

  if (seconds < 60) return 'just now';
  if (minutes < 60) return `${minutes}m ago`;
  if (hours < 24) return `${hours}h ago`;
  if (days < 7) return `${days}d ago`;
  return formatDate(d, 'yyyy-MM-dd');
}
