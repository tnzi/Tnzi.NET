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
  recentsQuery,
  writeRecentsPreferences,
} from '../../src/headless/recents';

/* Local-time dates: grouping is by the user's calendar days. */
const NOW = new Date(2026, 9, 21, 15, 0, 0);
const at = (days: number, hours = 0) => new Date(NOW.getTime() - days * 86_400_000 - hours * 3_600_000);

describe('recents preferences', () => {
  beforeEach(() => localStorage.clear());

  it('defaults to active conversations, any activity, most recent first, ungrouped', () => {
    expect(DEFAULT_RECENTS_PREFERENCES).toEqual({ status: 'active', lastActivity: 'all', sort: 'activity', groupBy: 'none' });
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
    expect(recentsQuery({ status: 'active', lastActivity: 'all', sort: 'activity' }, NOW)).toEqual({
      status: 'Active',
      sort: 'LastActivity',
    });
    expect(recentsQuery({ status: 'archived', lastActivity: 'all', sort: 'created' }, NOW)).toMatchObject({
      status: 'Archived',
      sort: 'CreationTime',
    });
    expect(recentsQuery({ status: 'all', lastActivity: 'all', sort: 'name' }, NOW)).toMatchObject({ status: 'All', sort: 'Title' });
  });

  it('turns the activity window into a start time before now', () => {
    const query = recentsQuery({ status: 'active', lastActivity: '3d', sort: 'activity' }, NOW);
    expect(new Date(query.startTime!).getTime()).toBe(NOW.getTime() - 3 * 86_400_000);
    expect(recentsQuery({ status: 'active', lastActivity: 'all', sort: 'activity' }, NOW)).not.toHaveProperty('startTime');
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
        { id: 'a', title: 'A', updatedAt: at(40).toISOString() },
        { id: 'b', title: 'B', updatedAt: at(0, 1).toISOString() },
        { id: 'c', title: 'C', updatedAt: at(400).toISOString() },
        { id: 'd', title: 'D', updatedAt: at(1).toISOString() },
        { id: 'e', title: 'E', updatedAt: at(0, 2).toISOString() },
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

  it('names a month with its year only when it is not this year', () => {
    expect(formatRecentsMonth(new Date(2026, 8, 1), NOW, 'en-US')).toBe('September');
    expect(formatRecentsMonth(new Date(2025, 7, 1), NOW, 'en-US')).toBe('August 2025');
    expect(formatRecentsMonth(new Date(2026, 8, 1), NOW, 'not_a_locale')).toBeTruthy();
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
