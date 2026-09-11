import { describe, it, expect, afterEach } from 'vitest';
import { createI18nContext, DEFAULT_LOCALE, LOCALES } from '../../src/adapters/i18n/create-i18n';
import {
  createI18nRuntime,
  setActiveI18nRuntime,
  getActiveI18nRuntime,
  provideI18n,
  useI18n,
  resetI18n,
  resetI18nRuntime,
} from '../../src/adapters/i18n/runtime';

afterEach(() => {
  resetI18nRuntime();
  resetI18n();
});

describe('createI18nContext', () => {
  it('translates a nested key in the requested locale and switches on setLocale', () => {
    const ctx = createI18nContext('en');
    expect(ctx.t('common.home')).toBe('Home');

    ctx.setLocale('zh-CN');
    expect(ctx.t('common.home')).toBe('首页');
  });

  it('maps region variants onto the base catalogue', () => {
    expect(createI18nContext('en-US').t('common.home')).toBe('Home');
    expect(createI18nContext('zh-TW').t('common.home')).toBe('首页');
  });

  it('interpolates {placeholders} and blanks a missing parameter rather than leaving the brace', () => {
    const ctx = createI18nContext('en');

    expect(ctx.t('common.minLength', { min: 8 })).toBe('Must be at least 8 characters');
    expect(ctx.t('common.minLength', {})).toBe('Must be at least  characters');
  });

  it('returns the key itself for a missing or non-leaf path', () => {
    const ctx = createI18nContext('en');

    expect(ctx.t('nothing.here')).toBe('nothing.here');
    expect(ctx.t('common')).toBe('common');
  });

  it('exposes the locale list and defaults to English', () => {
    expect(DEFAULT_LOCALE).toBe('en');
    expect(createI18nContext().locale).toBe('en');
    expect(createI18nContext().locales).toBe(LOCALES);
    expect(LOCALES.map(l => l.code)).toEqual(['en', 'zh-CN']);
  });
});

describe('i18n runtime', () => {
  it('provideI18n replaces the active context and useI18n reads it back', () => {
    const provided = provideI18n(undefined, 'zh-CN');

    expect(useI18n()).toBe(provided);
    expect(useI18n().t('common.home')).toBe('首页');
  });

  it('resetI18n returns to the default locale', () => {
    provideI18n(undefined, 'zh-CN');
    const reset = resetI18n();

    expect(useI18n()).toBe(reset);
    expect(useI18n().t('common.home')).toBe('Home');
  });

  it('setActiveI18nRuntime swaps the whole runtime and resetI18nRuntime restores the default one', () => {
    const before = getActiveI18nRuntime();
    const custom = createI18nRuntime({ locale: 'zh-CN' });

    setActiveI18nRuntime(custom);
    expect(getActiveI18nRuntime()).toBe(custom);
    expect(useI18n().t('common.home')).toBe('首页');

    resetI18nRuntime();
    expect(getActiveI18nRuntime()).toBe(before);
  });

  it('a custom runtime keeps its own context across provide / use / reset', () => {
    const runtime = createI18nRuntime();

    expect(runtime.use().t('common.home')).toBe('Home');
    const provided = runtime.provide(null, 'zh-CN');
    expect(runtime.use()).toBe(provided);
    expect(runtime.use().t('common.home')).toBe('首页');
    expect(runtime.reset().t('common.home')).toBe('Home');
  });
});
