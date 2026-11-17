import { describe, it, expect, beforeEach } from 'vitest';
import {
  DEFAULT_RECENTS_PREFERENCES,
  changesRecentsQuery,
  formatRecentActivity,
  formatRecentsMonth,
  groupThreadsByDate,
  matchesRecentsStatus,
  normalizeRecentsPreferences,
  readRecentsPreferences,
  recentsDateKey,
  compareThreads,
  recentsQuery,
  sortThreads,
  threadOrderTime,
  threadTier,
  writeRecentsPreferences,
} from '../../src/headless/recents';
import type { ThreadItem } from '../../src/components/chat/TThreadList.vue';

/* Local-time dates: grouping is by the user's calendar days. */
const NOW = new Date(2026, 9, 21, 15, 0, 0);
const at = (days: number, hours = 0) => new Date(NOW.getTime() - days * 86_400_000 - hours * 3_600_000);

describe('recents preferences', () => {
  beforeEach(() => localStorage.clear());

  it('defaults to active conversations, any time, the last message first, ungrouped', () => {
    expect(DEFAULT_RECENTS_PREFERENCES).toEqual({ status: 'active', lastActivity: 'all', sort: 'lastMessage', groupBy: 'none' });
  });

  it('normalises unknown and missing values to the defaults', () => {
    expect(normalizeRecentsPreferences({ status: 'archived', sort: 'bogus', groupBy: 3 })).toEqual({
      ...DEFAULT_RECENTS_PREFERENCES,
      status: 'archived',
    });
    expect(normalizeRecentsPreferences(null)).toEqual(DEFAULT_RECENTS_PREFERENCES);
  });

  it('round-trips through storage, and falls back on unreadable JSON or no key', () => {
    writeRecentsPreferences('k', { status: 'all', lastActivity: '7d', sort: 'name', groupBy: 'date' });
    expect(readRecentsPreferences('k')).toEqual({ status: 'all', lastActivity: '7d', sort: 'name', groupBy: 'date' });

    localStorage.setItem('broken', '{not json');
    expect(readRecentsPreferences('broken')).toEqual(DEFAULT_RECENTS_PREFERENCES);
    expect(readRecentsPreferences(null)).toEqual(DEFAULT_RECENTS_PREFERENCES);
    writeRecentsPreferences(null, DEFAULT_RECENTS_PREFERENCES);
    expect(localStorage.length).toBe(1 + 1);
  });

  it('grouping alone does not change the server query', () => {
    const base = DEFAULT_RECENTS_PREFERENCES;
    expect(changesRecentsQuery(base, { ...base, groupBy: 'date' })).toBe(false);
    expect(changesRecentsQuery(base, { ...base, sort: 'name' })).toBe(true);
    expect(changesRecentsQuery(base, { ...base, lastActivity: '1d' })).toBe(true);
    expect(changesRecentsQuery(base, { ...base, status: 'all' })).toBe(true);
  });
});

describe('recentsQuery', () => {
  it('sends the server enum member names', () => {
    expect(recentsQuery({ status: 'active', lastActivity: 'all', sort: 'lastMessage' }, NOW)).toEqual({
      status: 'Active',
      sort: 'LastUserMessage',
    });
    expect(recentsQuery({ status: 'archived', lastActivity: 'all', sort: 'created' }, NOW)).toMatchObject({
      status: 'Archived',
      sort: 'CreationTime',
    });
    expect(recentsQuery({ status: 'all', lastActivity: 'all', sort: 'name' }, NOW)).toMatchObject({ status: 'All', sort: 'Title' });
  });

  it('turns the activity window into a start time before now', () => {
    const query = recentsQuery({ status: 'active', lastActivity: '3d', sort: 'lastMessage' }, NOW);
    expect(new Date(query.startTime!).getTime()).toBe(NOW.getTime() - 3 * 86_400_000);
    expect(recentsQuery({ status: 'active', lastActivity: 'all', sort: 'lastMessage' }, NOW)).not.toHaveProperty('startTime');
  });
});

describe('matchesRecentsStatus', () => {
  it.each([
    ['active', false, true],
    ['active', true, false],
    ['archived', true, true],
    ['archived', false, false],
    ['all', true, true],
    ['all', false, true],
  ] as const)('%s list, archived=%s -> %s', (status, archived, expected) => {
    expect(matchesRecentsStatus(status, archived)).toBe(expected);
  });
});

describe('date grouping', () => {
  it('names calendar days, not 24-hour spans', () => {
    expect(recentsDateKey(new Date(2026, 9, 21, 0, 5), NOW)).toBe('today');
    expect(recentsDateKey(new Date(2026, 9, 20, 23, 55), NOW)).toBe('yesterday');
    expect(recentsDateKey(at(6), NOW)).toBe('previous7');
    expect(recentsDateKey(at(7), NOW)).toBe('previous30');
    expect(recentsDateKey(at(29), NOW)).toBe('previous30');
    expect(recentsDateKey(new Date(2026, 7, 2), NOW)).toBe('month:2026-08');
    expect(recentsDateKey(new Date(2025, 11, 31), NOW)).toBe('month:2025-12');
  });

  it('orders headings newest first and keeps the list order inside each', () => {
    const groups = groupThreadsByDate(
      [
        { id: 'a', title: 'A', userMessageAt: at(40).toISOString() },
        { id: 'b', title: 'B', userMessageAt: at(0, 1).toISOString(), updatedAt: at(500).toISOString() },
        { id: 'c', title: 'C', createdAt: at(400).toISOString() },
        { id: 'd', title: 'D', userMessageAt: at(1).toISOString(), updatedAt: at(0).toISOString() },
        { id: 'e', title: 'E', userMessageAt: at(0, 2).toISOString() },
      ],
      { now: NOW },
    );
    expect(groups.map((g) => g.key)).toEqual(['today', 'yesterday', 'month:2026-09', 'month:2025-09']);
    expect(groups[0]!.threads.map((t) => t.id)).toEqual(['b', 'e']);
    expect(groups[2]!.month).toEqual(new Date(2026, 8, 1));
  });

  it('groups on the creation date when asked, and puts a row without dates under Today', () => {
    const groups = groupThreadsByDate(
      [
        { id: 'a', title: 'A', updatedAt: at(0).toISOString(), createdAt: at(10).toISOString() },
        { id: 'p', title: 'Pending' },
      ],
      { now: NOW, field: 'createdAt' },
    );
    expect(groups.map((g) => [g.key, g.threads.map((t) => t.id)])).toEqual([
      ['today', ['p']],
      ['previous30', ['a']],
    ]);
  });

  it('puts the conversations awaiting the user, then the running ones, above the date headings', () => {
    const groups = groupThreadsByDate(
      [
        { id: 'w', title: 'W', awaitingOwner: true, running: true, userMessageAt: at(90).toISOString() },
        { id: 'r', title: 'R', running: true, userMessageAt: at(60).toISOString() },
        { id: 't', title: 'T', userMessageAt: at(0).toISOString() },
        { id: 'y', title: 'Y', userMessageAt: at(1).toISOString(), unread: true },
      ],
      { now: NOW },
    );
    expect(groups.map((g) => [g.key, g.threads.map((t) => t.id)])).toEqual([
      ['awaitingOwner', ['w']],
      ['running', ['r']],
      ['today', ['t']],
      ['yesterday', ['y']],
    ]);
  });

  it('names a month with its year only when it is not this year', () => {
    expect(formatRecentsMonth(new Date(2026, 8, 1), NOW, 'en-US')).toBe('September');
    expect(formatRecentsMonth(new Date(2025, 7, 1), NOW, 'en-US')).toBe('August 2025');
    expect(formatRecentsMonth(new Date(2026, 8, 1), NOW, 'not_a_locale')).toBeTruthy();
  });
});

describe('the list order', () => {
  const row = (id: string, extra: Partial<ThreadItem> = {}): ThreadItem => ({ id, title: id.toUpperCase(), ...extra });
  const ids = (rows: readonly ThreadItem[]) => rows.map((t) => t.id);

  it('awaiting the user first, then running, then the rest, each by the last message (creation when none)', () => {
    const rows = [
      row('fresh', { userMessageAt: at(0).toISOString(), createdAt: at(9).toISOString() }),
      row('silent', { createdAt: at(0, 1).toISOString() }),
      row('older', { userMessageAt: at(2).toISOString(), createdAt: at(9).toISOString() }),
      row('run-old', { running: true, userMessageAt: at(30).toISOString() }),
      row('run-new', { running: true, userMessageAt: at(20).toISOString() }),
      row('ask', { awaitingOwner: true, running: true, userMessageAt: at(50).toISOString() }),
      row('ask-app', { awaitingOwner: true, userMessageAt: at(40).toISOString() }),
    ];
    expect(ids(sortThreads(rows))).toEqual(['ask-app', 'ask', 'run-new', 'run-old', 'fresh', 'silent', 'older']);
    expect(threadTier(rows[5]!)).toBe(0);
    expect(threadTier(rows[3]!)).toBe(1);
    expect(threadTier(rows[0]!)).toBe(2);
  });

  it('breaks ties by the newer creation, and keeps rows still equal in the order given', () => {
    const same = at(1).toISOString();
    const rows = [
      row('b', { userMessageAt: same, createdAt: at(5).toISOString() }),
      row('a', { userMessageAt: same, createdAt: at(3).toISOString() }),
      row('z', { userMessageAt: same, createdAt: at(3).toISOString() }),
      row('y', { userMessageAt: same, createdAt: at(3).toISOString() }),
    ];
    expect(ids(sortThreads(rows))).toEqual(['a', 'z', 'y', 'b']);
  });

  it('only the time the user wrote moves a row, not the last activity, reading or a title', () => {
    const rows = [
      row('b', { userMessageAt: at(0, 1).toISOString(), updatedAt: at(3).toISOString() }),
      row('a', { userMessageAt: at(0, 2).toISOString(), updatedAt: at(0).toISOString(), unread: true }),
    ];
    expect(sortThreads(rows)).toBe(rows);
    const replied = rows.map((t) => (t.id === 'a' ? { ...t, updatedAt: new Date().toISOString(), title: 'Renamed', unread: false } : t));
    expect(ids(sortThreads(replied))).toEqual(['b', 'a']);
    const wrote = rows.map((t) => (t.id === 'a' ? { ...t, userMessageAt: at(0).toISOString() } : t));
    expect(ids(sortThreads(wrote))).toEqual(['a', 'b']);
  });

  it('a row that leaves the running tier goes back to its place by the last message', () => {
    const rows = [
      row('run', { running: true, userMessageAt: at(5).toISOString() }),
      row('new', { userMessageAt: at(0).toISOString() }),
      row('old', { userMessageAt: at(9).toISOString() }),
    ];
    expect(ids(sortThreads(rows))).toEqual(['run', 'new', 'old']);
    const ended = rows.map((t) => (t.id === 'run' ? { ...t, running: false } : t));
    expect(ids(sortThreads(ended))).toEqual(['new', 'run', 'old']);
  });

  it('every sort keeps the tiers; name and creation order inside them', () => {
    const rows = [
      row('banana', { title: 'banana', userMessageAt: at(0).toISOString(), createdAt: at(1).toISOString() }),
      row('apple', { title: 'Apple', userMessageAt: at(0, 1).toISOString(), createdAt: at(0, 1).toISOString() }),
      row('date', { title: 'date', userMessageAt: at(8).toISOString(), createdAt: at(2).toISOString() }),
      row('cherry', { title: 'cherry', awaitingOwner: true, userMessageAt: at(9).toISOString(), createdAt: at(9).toISOString() }),
    ];
    expect(ids(sortThreads(rows, 'name'))).toEqual(['cherry', 'apple', 'banana', 'date']);
    expect(ids(sortThreads(rows, 'created'))).toEqual(['cherry', 'apple', 'banana', 'date']);
    expect(ids(sortThreads(rows, 'lastMessage'))).toEqual(['cherry', 'banana', 'apple', 'date']);
    expect(compareThreads(rows[0]!, rows[1]!, 'name')).toBeGreaterThan(0);
  });

  it('a row with no times yet (one just started here) counts as newest', () => {
    const rows = [row('old', { userMessageAt: at(1).toISOString() }), row('pending', { pending: true })];
    expect(ids(sortThreads(rows))).toEqual(['pending', 'old']);
    expect(threadOrderTime({ createdAt: 'c' })).toBe('c');
    expect(threadOrderTime({ userMessageAt: 'u', createdAt: 'c' })).toBe('u');
  });
});

describe('formatRecentActivity', () => {
  it('is relative within a week, a date after', () => {
    expect(formatRecentActivity(at(0, 5).toISOString(), NOW, 'en-US')).toBe('5 hours ago');
    expect(formatRecentActivity(at(4).toISOString(), NOW, 'en-US')).toBe('4 days ago');
    expect(formatRecentActivity(new Date(NOW.getTime() - 20_000).toISOString(), NOW, 'en-US')).toBe('now');
    expect(formatRecentActivity(new Date(2026, 9, 13, 9).toISOString(), NOW, 'en-US')).toBe('Oct 13');
    expect(formatRecentActivity(new Date(2025, 9, 13, 9).toISOString(), NOW, 'en-US')).toBe('Oct 13, 2025');
  });

  it('is empty for no date or an unreadable one, and survives an unknown locale', () => {
    expect(formatRecentActivity(undefined, NOW)).toBe('');
    expect(formatRecentActivity('nope', NOW)).toBe('');
    expect(formatRecentActivity(at(1).toISOString(), NOW, 'not_a_locale')).toBeTruthy();
  });
});
