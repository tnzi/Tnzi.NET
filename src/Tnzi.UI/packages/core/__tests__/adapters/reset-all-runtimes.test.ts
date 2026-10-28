import { describe, it, expect, vi, afterEach } from 'vitest';
import { resetAllRuntimes } from '../../src/adapters/index';
import { provideI18n, useI18n } from '../../src/adapters/i18n/runtime';
import { useEventBus } from '../../src/adapters/event-bus/index';
import { createAdapterSingleton } from '../../src/adapters/singleton';

/**
 * `resetAllRuntimes()` is the documented isolation step between SSR requests
 * and test cases. Nobody installs an i18n runtime or an event bus in the
 * common case, so the state lives in the built-in defaults; a reset that only
 * dropped installed adapters left that state behind.
 */
describe('resetAllRuntimes', () => {
  afterEach(() => {
    resetAllRuntimes();
  });

  it('forgets the locale provideI18n set on the default runtime', () => {
    provideI18n(null, 'zh-CN');
    expect(useI18n().t('common.home')).toBe('首页');

    resetAllRuntimes();

    expect(useI18n().t('common.home')).toBe('Home');
  });

  it('forgets the listeners on the default event bus', () => {
    const handler = vi.fn();
    useEventBus().on('order.created', handler);

    resetAllRuntimes();
    useEventBus().emit('order.created', { id: 1 });

    expect(handler).not.toHaveBeenCalled();
  });
});

describe('createAdapterSingleton reset', () => {
  it('drops the fallback so the next use builds a fresh one', () => {
    let built = 0;
    const slot = createAdapterSingleton(`probe-reset-${Math.random()}`, () => ({ id: ++built }));

    const first = slot.use();
    slot.reset();
    const second = slot.use();

    expect(second).not.toBe(first);
    expect(built).toBe(2);
  });
});
