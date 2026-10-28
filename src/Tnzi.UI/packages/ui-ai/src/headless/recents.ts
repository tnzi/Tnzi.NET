/**
 * The sidebar's recent conversations: what the list shows (archive status, how
 * recently active), in which order, and how it is grouped.
 *
 * Status, activity window and order go to the server with every page, so paging
 * stays correct however the list is filtered: filtering a loaded page on the
 * client would leave short pages and a "Show more" that brings back rows the
 * filter then hides. Grouping only arranges the rows already loaded, so it
 * stays here.
 *
 * Pure functions plus the persisted preferences; `useChatThreads` holds the
 * state and `TThreadList` / `TRecentsPage` render it.
 */
import type { Ref } from 'vue';
import type { ThreadListQueryDto, ThreadListSort, ThreadListStatus } from '@tnzi/core/services/ai';
import type { ThreadItem } from '../components/chat/TThreadList.vue';

/** Which conversations the list shows by archive state. */
export type RecentsStatus = 'active' | 'archived' | 'all';
/** How recently a conversation must have been active to be listed. */
export type RecentsActivityWindow = '1d' | '3d' | '7d' | '30d' | 'all';
/** Order of the list: most recent activity, newest created, or by name. */
export type RecentsSort = 'activity' | 'created' | 'name';
/** How the sidebar arranges the loaded rows. */
export type RecentsGroupBy = 'date' | 'none';

/** The sidebar list's view: filter, order and grouping. Persisted per browser. */
export interface RecentsPreferences {
  readonly status: RecentsStatus;
  readonly lastActivity: RecentsActivityWindow;
  readonly sort: RecentsSort;
  readonly groupBy: RecentsGroupBy;
}

export const RECENTS_STATUSES: readonly RecentsStatus[] = ['active', 'archived', 'all'];
export const RECENTS_ACTIVITY_WINDOWS: readonly RecentsActivityWindow[] = ['1d', '3d', '7d', '30d', 'all'];
export const RECENTS_SORTS: readonly RecentsSort[] = ['name', 'created', 'activity'];
export const RECENTS_GROUP_BYS: readonly RecentsGroupBy[] = ['date', 'none'];

export const DEFAULT_RECENTS_PREFERENCES: RecentsPreferences = Object.freeze({
  status: 'active',
  lastActivity: 'all',
  sort: 'activity',
  groupBy: 'none',
});

/** Default localStorage key of the preferences (`useChatThreads({ recentsStorageKey })`). */
export const RECENTS_STORAGE_KEY = 'tnzi-ui-ai-recents';

/**
 * `TChatApp`'s `view` for the full Recents page: the sidebar's View all sets it
 * (through `update:view`), and with a `recents` controller the shell renders the
 * page itself (`#recents` slot to replace it). A product that routes its views
 * maps this id to a URL of its own.
 */
export const RECENTS_VIEW_ID = 'recents';

const pick = <T extends string>(value: unknown, allowed: readonly T[], fallback: T): T =>
  typeof value === 'string' && (allowed as readonly string[]).includes(value) ? (value as T) : fallback;

/** Preferences from anything (stored JSON, a partial object); unknown or missing values fall back to the defaults. */
export function normalizeRecentsPreferences(value: unknown): RecentsPreferences {
  const raw = (value && typeof value === 'object' ? value : {}) as Record<string, unknown>;
  const d = DEFAULT_RECENTS_PREFERENCES;
  return {
    status: pick(raw.status, RECENTS_STATUSES, d.status),
    lastActivity: pick(raw.lastActivity, RECENTS_ACTIVITY_WINDOWS, d.lastActivity),
    sort: pick(raw.sort, RECENTS_SORTS, d.sort),
    groupBy: pick(raw.groupBy, RECENTS_GROUP_BYS, d.groupBy),
  };
}

/** The stored preferences, or the defaults (no key, nothing stored, unreadable storage). */
export function readRecentsPreferences(key: string | null | undefined): RecentsPreferences {
  if (!key || typeof localStorage === 'undefined') return DEFAULT_RECENTS_PREFERENCES;
  try {
    const raw = localStorage.getItem(key);
    return raw ? normalizeRecentsPreferences(JSON.parse(raw)) : DEFAULT_RECENTS_PREFERENCES;
  } catch {
    return DEFAULT_RECENTS_PREFERENCES;
  }
}

export function writeRecentsPreferences(key: string | null | undefined, preferences: RecentsPreferences): void {
  if (!key || typeof localStorage === 'undefined') return;
  try {
    localStorage.setItem(key, JSON.stringify(preferences));
  } catch {
    // Storage full or blocked: the choice still applies for this page.
  }
}

/** Whether two preferences ask the server for different lists (grouping does not). */
export function changesRecentsQuery(a: RecentsPreferences, b: RecentsPreferences): boolean {
  return a.status !== b.status || a.lastActivity !== b.lastActivity || a.sort !== b.sort;
}

const WINDOW_DAYS: Record<Exclude<RecentsActivityWindow, 'all'>, number> = { '1d': 1, '3d': 3, '7d': 7, '30d': 30 };

/* The server's enum members, by name: the wire carries member names. Written
   as values rather than imported, so this module needs nothing at runtime from
   the service layer. */
const STATUS_TO_WIRE: Record<RecentsStatus, `${ThreadListStatus}`> = { active: 'Active', archived: 'Archived', all: 'All' };
const SORT_TO_WIRE: Record<RecentsSort, `${ThreadListSort}`> = { activity: 'LastActivity', created: 'CreationTime', name: 'Title' };

/** The list query fields for `status` / `lastActivity` / `sort` (`startTime` relative to `now`). */
export function recentsQuery(
  preferences: Pick<RecentsPreferences, 'status' | 'lastActivity' | 'sort'>,
  now: Date = new Date(),
): Pick<ThreadListQueryDto, 'status' | 'sort' | 'startTime'> {
  const query: Pick<ThreadListQueryDto, 'status' | 'sort' | 'startTime'> = {
    status: STATUS_TO_WIRE[preferences.status] as ThreadListStatus,
    sort: SORT_TO_WIRE[preferences.sort] as ThreadListSort,
  };
  if (preferences.lastActivity !== 'all') {
    const days = WINDOW_DAYS[preferences.lastActivity];
    return { ...query, startTime: new Date(now.getTime() - days * 24 * 60 * 60 * 1000).toISOString() };
  }
  return query;
}

/** Whether a conversation in the given archive state belongs in a list with `status`. */
export function matchesRecentsStatus(status: RecentsStatus, archived: boolean): boolean {
  return status === 'all' || (status === 'archived') === archived;
}

/** A run of rows under one date heading. `key` is `today`, `yesterday`, `previous7`, `previous30` or `month:YYYY-MM`. */
export interface RecentsDateGroup {
  readonly key: string;
  /** For `month:` groups: the first day of that month (local time), to name it. */
  readonly month?: Date;
  readonly threads: readonly ThreadItem[];
}

const startOfDay = (date: Date): number => new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime();
const DAY_MS = 24 * 60 * 60 * 1000;

/** The date heading a timestamp falls under, relative to `now` (local calendar days). */
export function recentsDateKey(at: Date, now: Date): string {
  const today = startOfDay(now);
  const day = startOfDay(at);
  /* Rounded: a day across a daylight-saving change is 23 or 25 hours long. */
  const daysAgo = Math.round((today - day) / DAY_MS);
  if (daysAgo <= 0) return 'today';
  if (daysAgo === 1) return 'yesterday';
  if (daysAgo < 7) return 'previous7';
  if (daysAgo < 30) return 'previous30';
  return `month:${at.getFullYear()}-${String(at.getMonth() + 1).padStart(2, '0')}`;
}

const DATE_KEY_RANK: Record<string, number> = { today: 0, yesterday: 1, previous7: 2, previous30: 3 };

/**
 * Group rows under date headings (Today, Yesterday, Previous 7 days, Previous 30
 * days, then one per month), newest heading first. Rows keep their list order
 * inside a heading, so the chosen sort still applies within each. `field` is
 * the date grouped on: last activity, or creation when the list is sorted by it.
 * A row without that date goes under Today (an optimistic row of a conversation
 * just started has no server dates yet).
 */
export function groupThreadsByDate(
  threads: readonly ThreadItem[],
  options: { now?: Date; field?: 'updatedAt' | 'createdAt' } = {},
): RecentsDateGroup[] {
  const now = options.now ?? new Date();
  const field = options.field ?? 'updatedAt';
  const groups = new Map<string, { month?: Date; threads: ThreadItem[] }>();
  for (const thread of threads) {
    const iso = thread[field] ?? thread.updatedAt;
    const at = iso ? new Date(iso) : now;
    const valid = !Number.isNaN(at.getTime());
    const key = valid ? recentsDateKey(at, now) : 'today';
    let group = groups.get(key);
    if (!group) {
      group = key.startsWith('month:') ? { month: new Date(at.getFullYear(), at.getMonth(), 1), threads: [] } : { threads: [] };
      groups.set(key, group);
    }
    group.threads.push(thread);
  }
  return [...groups.entries()]
    .map(([key, group]) => ({ key, ...group }))
    .sort((a, b) => {
      const ra = DATE_KEY_RANK[a.key];
      const rb = DATE_KEY_RANK[b.key];
      if (ra !== undefined || rb !== undefined) return (ra ?? 99) - (rb ?? 99);
      return b.key.localeCompare(a.key);
    });
}

/** The name of a `month:` heading: "October" this year, "October 2025" before. */
export function formatRecentsMonth(month: Date, now: Date = new Date(), locale?: string): string {
  const sameYear = month.getFullYear() === now.getFullYear();
  try {
    return month.toLocaleDateString(locale || undefined, sameYear ? { month: 'long' } : { month: 'long', year: 'numeric' });
  } catch {
    return month.toLocaleDateString(undefined, sameYear ? { month: 'long' } : { month: 'long', year: 'numeric' });
  }
}

/**
 * When a conversation was last active, for the full Recents page: relative
 * within the past week ("5 minutes ago", "5 hours ago", "4 days ago"), a date
 * after that ("Oct 13", with the year when it is not this year). Empty for a
 * missing or unreadable date.
 */
export function formatRecentActivity(iso: string | null | undefined, now: Date = new Date(), locale?: string): string {
  if (!iso) return '';
  const at = new Date(iso);
  if (Number.isNaN(at.getTime())) return '';
  const seconds = Math.max(0, Math.round((now.getTime() - at.getTime()) / 1000));
  const relative = (value: number, unit: Intl.RelativeTimeFormatUnit): string => {
    try {
      return new Intl.RelativeTimeFormat(locale || undefined, { numeric: 'auto' }).format(-value, unit);
    } catch {
      return new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' }).format(-value, unit);
    }
  };
  if (seconds < 60) return relative(0, 'second');
  if (seconds < 3600) return relative(Math.floor(seconds / 60), 'minute');
  if (seconds < DAY_MS / 1000) return relative(Math.floor(seconds / 3600), 'hour');
  const days = Math.floor(seconds / (DAY_MS / 1000));
  if (days < 7) return relative(days, 'day');
  const options: Intl.DateTimeFormatOptions =
    at.getFullYear() === now.getFullYear() ? { month: 'short', day: 'numeric' } : { month: 'short', day: 'numeric', year: 'numeric' };
  try {
    return at.toLocaleDateString(locale || undefined, options);
  } catch {
    return at.toLocaleDateString(undefined, options);
  }
}

/** The full Recents page's list: Active or Archived, a search, newest activity first, in pages. */
export interface RecentThreadsPage {
  readonly threads: Readonly<Ref<readonly ThreadItem[]>>;
  readonly status: Readonly<Ref<'active' | 'archived'>>;
  readonly keyword: Readonly<Ref<string>>;
  readonly hasMore: Readonly<Ref<boolean>>;
  /** The first page is loading. */
  readonly loading: Readonly<Ref<boolean>>;
  readonly loadingMore: Readonly<Ref<boolean>>;
  /** A first page has been asked for and answered (so an empty list means "none", not "not yet"). */
  readonly loaded: Readonly<Ref<boolean>>;
  /** (Re)load the first page with the current status and search. */
  load(): Promise<void>;
  loadMore(): Promise<void>;
  setStatus(status: 'active' | 'archived'): Promise<void>;
  search(keyword: string): Promise<void>;
}

/**
 * Recent conversations for `TChatApp` (`recents` prop): the sidebar list's
 * filter, order and grouping, conversation actions that keep every list in
 * step, and the full Recents page. `useChatThreads().recents` is one; a product
 * on its own transport can implement it.
 */
export interface RecentThreadsController {
  readonly preferences: Readonly<Ref<RecentsPreferences>>;
  /** Change the sidebar list's view; a change of filter or order reloads it. Persisted per browser. */
  setPreferences(patch: Partial<RecentsPreferences>): Promise<void>;
  /** Rename a conversation. Resolves false when it was refused (reported through the hook's `onError`). */
  rename(id: string, title: string): Promise<boolean>;
  archive(id: string): Promise<boolean>;
  unarchive(id: string): Promise<boolean>;
  readonly page: RecentThreadsPage;
}
