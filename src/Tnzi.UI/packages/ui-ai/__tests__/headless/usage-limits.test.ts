import { describe, it, expect } from 'vitest';
import {
  describeReset,
  formatCountdown,
  formatLimitReached,
  formatResetDay,
  formatResetLabel,
  formatResetSentence,
  formatWindowDuration,
  isResetAhead,
  readUsageLimitDenial,
  wholePercent,
} from '../../src/headless/usage-limits';
import { en } from '../../src/locales/en';
import { zhCn } from '../../src/locales/zh-cn';

const EN = en.usageLimits;
const ZH = zhCn.usageLimits;
const NOW = new Date('2026-10-21T10:00:00Z');
const later = (ms: number) => new Date(NOW.getTime() + ms).toISOString();
const MIN = 60_000;
const HOUR = 60 * MIN;
const DAY = 24 * HOUR;

describe('readUsageLimitDenial', () => {
  it('reads the window and reset of a refused turn', () => {
    expect(readUsageLimitDenial({ window: 'Weekly', resetsAt: '2026-10-23T02:00:00Z' }))
      .toEqual({ window: 'Weekly', resetsAt: '2026-10-23T02:00:00Z' });
  });

  it('an unknown window or an unreadable date becomes null, never a wrong value', () => {
    expect(readUsageLimitDenial({ window: 'Monthly', resetsAt: 'soon' })).toEqual({ window: null, resetsAt: null });
    expect(readUsageLimitDenial(null)).toEqual({ window: null, resetsAt: null });
  });
});

describe('formatCountdown', () => {
  it('hours and minutes, minutes rounded up', () => {
    expect(formatCountdown(2 * HOUR + 12 * MIN + 1, EN)).toBe('2 hr 13 min');
    expect(formatCountdown(3 * HOUR, EN)).toBe('3 hr');
    expect(formatCountdown(61_000, EN)).toBe('2 min');
    expect(formatCountdown(30_000, EN)).toBe('less than a minute');
  });

  it('in Chinese', () => {
    expect(formatCountdown(2 * HOUR + 13 * MIN, ZH)).toBe('2 小时 13 分钟');
  });
});

describe('describeReset', () => {
  it('a reset within a day is a countdown', () => {
    expect(describeReset(later(90 * MIN), EN, NOW)).toEqual({ relative: true, time: '1 hr 30 min' });
  });

  it('a later reset is a day and time', () => {
    const reset = describeReset(later(3 * DAY), EN, NOW);
    expect(reset?.relative).toBe(false);
    expect(reset?.time).toBe(formatResetDay(new Date(later(3 * DAY)), EN, NOW));
  });

  it('a reset already passed reads as less than a minute, not a negative countdown', () => {
    expect(describeReset(later(-5 * MIN), EN, NOW)).toEqual({ relative: true, time: 'less than a minute' });
  });

  it('a missing or unreadable date is null', () => {
    expect(describeReset(null, EN, NOW)).toBeNull();
    expect(describeReset('nope', EN, NOW)).toBeNull();
  });
});

describe('formatResetDay', () => {
  it('the weekday names the day within the week', () => {
    const at = new Date(later(3 * DAY));
    expect(formatResetDay(at, EN, NOW)).toBe(at.toLocaleString('en-US', { weekday: 'short', hour: 'numeric', minute: '2-digit' }));
  });

  it('more than six days ahead the date is added (the weekday alone would name today)', () => {
    const at = new Date(later(6 * DAY + 20 * HOUR));
    expect(formatResetDay(at, EN, NOW)).toBe(at.toLocaleString('en-US', { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' }));
  });

  it('an unreadable language tag falls back instead of throwing', () => {
    expect(() => formatResetDay(new Date(later(DAY * 2)), { ...EN, dateLocale: 'zh_CN' }, NOW)).not.toThrow();
  });
});

describe('sentences', () => {
  it('label and sentence for a countdown and for a day', () => {
    expect(formatResetLabel(later(2 * HOUR), EN, NOW)).toBe('Resets in 2 hr');
    expect(formatResetSentence(later(2 * HOUR), EN, NOW)).toBe('It resets in 2 hr.');
    expect(formatResetLabel(later(2 * DAY), EN, NOW)).toMatch(/^Resets /);
    expect(formatResetSentence(later(2 * HOUR), ZH, NOW)).toBe('2 小时后重置。');
  });

  it('a limit that does not reset points at who can lift it', () => {
    expect(formatResetLabel(null, EN, NOW)).toBeNull();
    expect(formatResetSentence(null, EN, NOW)).toBe(EN.noReset);
  });

  it('names the window that is at its limit', () => {
    expect(formatLimitReached('Session' as never, EN)).toBe(EN.reachedSession);
    expect(formatLimitReached('Weekly' as never, EN)).toBe(EN.reachedWeekly);
    expect(formatLimitReached(null, EN)).toBe(EN.reached);
  });
});

describe('formatWindowDuration', () => {
  it('days, hours, minutes', () => {
    expect(formatWindowDuration(300, EN)).toBe('5 hours');
    expect(formatWindowDuration(60, EN)).toBe('1 hour');
    expect(formatWindowDuration(7 * 24 * 60, EN)).toBe('7 days');
    expect(formatWindowDuration(24 * 60, EN)).toBe('1 day');
    expect(formatWindowDuration(90, EN)).toBe('90 minutes');
    expect(formatWindowDuration(300, ZH)).toBe('5 小时');
  });
});

describe('wholePercent / isResetAhead', () => {
  it('rounds and clamps for display', () => {
    expect(wholePercent(42.4)).toBe(42);
    expect(wholePercent(104)).toBe(100);
    expect(wholePercent(null)).toBe(0);
  });

  it('a missing reset counts as ahead (it does not lift by itself)', () => {
    expect(isResetAhead(null, NOW)).toBe(true);
    expect(isResetAhead(later(MIN), NOW)).toBe(true);
    expect(isResetAhead(later(-MIN), NOW)).toBe(false);
  });
});
