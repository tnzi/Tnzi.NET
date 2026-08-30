import { describe, it, expect, afterEach } from 'vitest';
import { ref, watchEffect, nextTick } from 'vue';
import {
  formatDateTime,
  formatDateOnly,
  formatDate,
  setDateFormatDefaults,
  getDateFormatDefaults,
  resetDateFormatDefaults,
} from '../../src/utils/date';

// The defaults live in a process-wide slot, so a test that sets them and does
// not clear them changes the meaning of every test after it.
afterEach(() => resetDateFormatDefaults());

describe('formatDateTime', () => {
  const iso = '2026-05-29T08:30:00.000Z';

  it('returns localized date-time string for a valid value', () => {
    expect(formatDateTime(iso)).toBe(new Date(iso).toLocaleString());
  });

  it('accepts Date and number inputs', () => {
    const d = new Date(iso);
    expect(formatDateTime(d)).toBe(d.toLocaleString());
    expect(formatDateTime(d.getTime())).toBe(d.toLocaleString());
  });

  it('returns empty string for null / undefined / empty', () => {
    expect(formatDateTime(null)).toBe('');
    expect(formatDateTime(undefined)).toBe('');
    expect(formatDateTime('')).toBe('');
  });

  it('honors a custom fallback for nullish input', () => {
    expect(formatDateTime(null, { fallback: '-' })).toBe('-');
  });

  it('returns the original string for an unparseable date (not "Invalid Date")', () => {
    expect(formatDateTime('not-a-date')).toBe('not-a-date');
    expect(formatDateTime('not-a-date', { fallback: '-' })).toBe('not-a-date');
  });

  it('names the date half without dropping the time', () => {
    const d = new Date(iso);
    expect(formatDateTime(iso, { dateStyle: 'medium' })).toBe(
      d.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'medium' }),
    );
    // The trap this guards: `toLocaleString(undefined, { dateStyle })` renders
    // the date ONLY, so naming the date form would silently drop the clock off
    // every timestamp in the app.
    expect(formatDateTime(iso, { dateStyle: 'medium' })).not.toBe(
      d.toLocaleDateString(undefined, { dateStyle: 'medium' }),
    );
  });

  it('names the time half alone, leaving the date form as it is today', () => {
    const d = new Date(iso);
    expect(formatDateTime(iso, { timeStyle: 'short' })).toBe(
      d.toLocaleString(undefined, { dateStyle: 'short', timeStyle: 'short' }),
    );
  });
});

describe('app-level date display defaults', () => {
  const iso = '2026-05-29T08:30:00.000Z';

  it('is empty until something sets it, so untouched calls render as before', () => {
    expect(getDateFormatDefaults()).toEqual({});
    expect(formatDateOnly(iso)).toBe(new Date(iso).toLocaleDateString());
    expect(formatDateTime(iso)).toBe(new Date(iso).toLocaleString());
  });

  it('applies one style to both helpers', () => {
    setDateFormatDefaults({ dateStyle: 'medium' });
    const d = new Date(iso);
    expect(formatDateOnly(iso)).toBe(d.toLocaleDateString(undefined, { dateStyle: 'medium' }));
    expect(formatDateTime(iso)).toBe(
      d.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'medium' }),
    );
  });

  it('survives alongside utc and fallback', () => {
    setDateFormatDefaults({ dateStyle: 'medium' });
    const utcMidnight = '2026-07-01T00:00:00Z';
    expect(formatDateOnly(utcMidnight, { utc: true })).toBe(
      new Date(utcMidnight).toLocaleDateString(undefined, {
        dateStyle: 'medium',
        timeZone: 'UTC',
      }),
    );
    expect(formatDateOnly(null, { fallback: '-' })).toBe('-');
  });

  it('yields to a per-call style', () => {
    setDateFormatDefaults({ dateStyle: 'full' });
    expect(formatDateOnly(iso, { dateStyle: 'short' })).toBe(
      new Date(iso).toLocaleDateString(undefined, { dateStyle: 'short' }),
    );
  });

  it('does not keep a live reference to the caller\'s object', () => {
    const config = { dateStyle: 'medium' as const };
    setDateFormatDefaults(config);
    (config as { dateStyle: string }).dateStyle = 'full';
    expect(getDateFormatDefaults().dateStyle).toBe('medium');
  });

  it('resets back to the locale default form', () => {
    setDateFormatDefaults({ dateStyle: 'full', timeStyle: 'full' });
    resetDateFormatDefaults();
    expect(formatDateOnly(iso)).toBe(new Date(iso).toLocaleDateString());
    expect(formatDateTime(iso)).toBe(new Date(iso).toLocaleString());
  });
});

describe('formatDateOnly', () => {
  const iso = '2026-05-29T08:30:00.000Z';

  it('returns localized date-only string for a valid value', () => {
    expect(formatDateOnly(iso)).toBe(new Date(iso).toLocaleDateString());
  });

  it('returns fallback for nullish input', () => {
    expect(formatDateOnly(null)).toBe('');
    expect(formatDateOnly(undefined, { fallback: 'n/a' })).toBe('n/a');
  });

  it('utc: true renders the UTC calendar date regardless of local timezone', () => {
    // A date-only value stored as UTC midnight must not shift a day for
    // viewers west of UTC.
    const utcMidnight = '2026-07-01T00:00:00Z';
    expect(formatDateOnly(utcMidnight, { utc: true })).toBe(
      new Date(utcMidnight).toLocaleDateString(undefined, { timeZone: 'UTC' }),
    );
    // Sanity: the rendered string contains the UTC day (1), whatever the locale.
    expect(formatDateOnly(utcMidnight, { utc: true })).toMatch(/1/);
  });

  it('utc option keeps null/invalid semantics', () => {
    expect(formatDateOnly(null, { utc: true, fallback: '-' })).toBe('-');
    expect(formatDateOnly('not-a-date', { utc: true })).toBe('not-a-date');
  });

  it('renders the named display form asked for', () => {
    const d = new Date(iso);
    expect(formatDateOnly(iso, { dateStyle: 'medium' })).toBe(
      d.toLocaleDateString(undefined, { dateStyle: 'medium' }),
    );
    // Sanity: the equality above would pass vacuously in a locale whose medium
    // form happens to equal its default numeric one. Asking for a form has to
    // actually produce a different string from asking for nothing.
    expect(formatDateOnly(iso, { dateStyle: 'medium' })).not.toBe(formatDateOnly(iso));
  });

  it('composes dateStyle with the UTC calendar reading', () => {
    const utcMidnight = '2026-07-01T00:00:00Z';
    expect(formatDateOnly(utcMidnight, { utc: true, dateStyle: 'medium' })).toBe(
      new Date(utcMidnight).toLocaleDateString(undefined, {
        dateStyle: 'medium',
        timeZone: 'UTC',
      }),
    );
  });

  it('keeps fallback and invalid-input semantics when a style is asked for', () => {
    expect(formatDateOnly(null, { dateStyle: 'full', fallback: '-' })).toBe('-');
    expect(formatDateOnly('not-a-date', { dateStyle: 'full' })).toBe('not-a-date');
  });
});


describe('rendering language', () => {
  const iso = '2026-10-02T08:30:00.000Z';
  const d = new Date(iso);

  it('renders in the language the call names', () => {
    expect(formatDateOnly(iso, { locale: 'zh-CN', dateStyle: 'medium' })).toBe(
      d.toLocaleDateString('zh-CN', { dateStyle: 'medium' }),
    );
    expect(formatDateOnly(iso, { locale: 'zh-CN', dateStyle: 'medium' })).toContain('年');
    expect(formatDateTime(iso, { locale: 'zh-CN', dateStyle: 'medium' })).toContain('年');
  });

  it('accepts the tag casing the admin store uses', () => {
    // `@tnzi/ui-admin` stores 'zh-cn'; core's own Locale union spells it
    // 'zh-CN'. Intl canonicalises, so an app must not have to normalise.
    expect(formatDateOnly(iso, { locale: 'zh-cn', dateStyle: 'medium' })).toBe(
      formatDateOnly(iso, { locale: 'zh-CN', dateStyle: 'medium' }),
    );
  });

  it('applies an app-level language to both helpers', () => {
    setDateFormatDefaults({ locale: 'zh-CN', dateStyle: 'medium' });
    expect(formatDateOnly(iso)).toBe(d.toLocaleDateString('zh-CN', { dateStyle: 'medium' }));
    expect(formatDateTime(iso)).toBe(
      d.toLocaleString('zh-CN', { dateStyle: 'medium', timeStyle: 'medium' }),
    );
  });

  it('applies an app-level language even with no style named', () => {
    setDateFormatDefaults({ locale: 'zh-CN' });
    expect(formatDateOnly(iso)).toBe(d.toLocaleDateString('zh-CN'));
    expect(formatDateTime(iso)).toBe(d.toLocaleString('zh-CN'));
  });

  it('composes with utc and fallback', () => {
    setDateFormatDefaults({ locale: 'zh-CN', dateStyle: 'medium' });
    const utcMidnight = '2026-07-01T00:00:00Z';
    expect(formatDateOnly(utcMidnight, { utc: true })).toBe(
      new Date(utcMidnight).toLocaleDateString('zh-CN', {
        dateStyle: 'medium',
        timeZone: 'UTC',
      }),
    );
    expect(formatDateOnly(null, { fallback: '-' })).toBe('-');
    expect(formatDateOnly('not-a-date')).toBe('not-a-date');
  });

  it('yields to a per-call language', () => {
    setDateFormatDefaults({ locale: 'zh-CN', dateStyle: 'medium' });
    expect(formatDateOnly(iso, { locale: 'en-US' })).toBe(
      d.toLocaleDateString('en-US', { dateStyle: 'medium' }),
    );
  });

  it('reads a getter on every format rather than once at set time', () => {
    let current = 'en-US';
    setDateFormatDefaults({ locale: () => current, dateStyle: 'medium' });
    expect(formatDateOnly(iso)).toBe(d.toLocaleDateString('en-US', { dateStyle: 'medium' }));
    current = 'zh-CN';
    // No second setDateFormatDefaults call: the getter is what makes a switch
    // reach dates that are formatted after it.
    expect(formatDateOnly(iso)).toBe(d.toLocaleDateString('zh-CN', { dateStyle: 'medium' }));
  });

  it('re-renders a reactive consumer on a language switch, with no reload', async () => {
    // The claim in setDateFormatDefaults' docs: a getter over a reactive source
    // is tracked by whatever effect is formatting, so a component that
    // formatted a date while rendering repaints when the language changes.
    const locale = ref('en-US');
    setDateFormatDefaults({ locale: () => locale.value, dateStyle: 'medium' });

    let rendered = '';
    watchEffect(() => {
      rendered = formatDateOnly(iso);
    });
    expect(rendered).toBe(d.toLocaleDateString('en-US', { dateStyle: 'medium' }));

    locale.value = 'zh-CN';
    await nextTick();
    expect(rendered).toBe(d.toLocaleDateString('zh-CN', { dateStyle: 'medium' }));
  });

  it('falls back to the browser language rather than throwing on an unusable tag', () => {
    // `zh_CN` (underscore) is the shape a POSIX / .NET style tag arrives in,
    // and Intl rejects it with a RangeError. A date is furniture: it must not
    // take the screen down with it.
    setDateFormatDefaults({ locale: 'zh_CN', dateStyle: 'medium' });
    expect(() => formatDateOnly(iso)).not.toThrow();
    expect(formatDateOnly(iso)).toBe(d.toLocaleDateString(undefined, { dateStyle: 'medium' }));
    expect(formatDateTime(iso)).toBe(
      d.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'medium' }),
    );
  });

  it('follows the browser when nothing names a language', () => {
    expect(formatDateOnly(iso)).toBe(d.toLocaleDateString());
    expect(formatDateTime(iso)).toBe(d.toLocaleString());
    expect(formatDateOnly(iso, { dateStyle: 'medium' })).toBe(
      d.toLocaleDateString(undefined, { dateStyle: 'medium' }),
    );
  });
});

describe('formatDate (fixed template)', () => {
  it('formats with the default yyyy-MM-dd pattern', () => {
    expect(formatDate('2026-05-29T08:30:00')).toBe('2026-05-29');
  });

  it('honors a custom format token set', () => {
    expect(formatDate('2026-05-29T08:30:05', 'yyyy/MM/dd HH:mm:ss')).toBe('2026/05/29 08:30:05');
  });
});
