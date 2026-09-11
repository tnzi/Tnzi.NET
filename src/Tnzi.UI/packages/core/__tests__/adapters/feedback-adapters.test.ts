import { describe, it, expect, vi, afterEach } from 'vitest';
import { setLoadingBarAdapter, useLoadingBar, resetLoadingBarAdapter } from '../../src/adapters/loading-bar';
import {
  setNotificationAdapter,
  useNotification,
  resetNotificationAdapter,
  type NotificationAdapter,
} from '../../src/adapters/notification';
import { setMessageAdapter, useMessage, resetMessageAdapter } from '../../src/adapters/message';
import { setDialogAdapter, useDialog, resetDialogAdapter } from '../../src/adapters/dialog';
import { setLoggerAdapter, useLogger, resetLoggerAdapter } from '../../src/adapters/logger';

// Every adapter below is a set/use/reset trio over one process-wide slot. The
// tests cover two things per adapter: the installed implementation wins, and
// the built-in fallback is safe to call before anything is installed.

afterEach(() => {
  resetLoadingBarAdapter();
  resetNotificationAdapter();
  resetMessageAdapter();
  resetDialogAdapter();
  resetLoggerAdapter();
  vi.restoreAllMocks();
});

describe('loading bar adapter', () => {
  it('falls back to a no-op that can be called freely', () => {
    const bar = useLoadingBar();

    expect(() => {
      bar.start();
      bar.finish();
      bar.error();
    }).not.toThrow();
  });

  it('returns the installed adapter until reset', () => {
    const installed = { start: vi.fn(), finish: vi.fn(), error: vi.fn() };
    setLoadingBarAdapter(installed);

    useLoadingBar().start();
    expect(installed.start).toHaveBeenCalledTimes(1);

    resetLoadingBarAdapter();
    expect(useLoadingBar()).not.toBe(installed);
  });
});

describe('notification adapter', () => {
  it('writes to the console with the level and an optional title before anything is installed', () => {
    const log = vi.spyOn(console, 'log').mockImplementation(() => {});
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const error = vi.spyOn(console, 'error').mockImplementation(() => {});

    const notification = useNotification();
    notification.info('Saved', { title: 'Profile' });
    notification.success('Done');
    notification.warning('Careful', { title: 'Quota' });
    notification.error('Failed');
    notification.destroyAll();

    expect(log).toHaveBeenCalledWith('[Notification:Info] Profile: Saved');
    expect(log).toHaveBeenCalledWith('[Notification:Success] Done');
    expect(warn).toHaveBeenCalledWith('[Notification:Warning] Quota: Careful');
    expect(error).toHaveBeenCalledWith('[Notification:Error] Failed');
  });

  it('routes through the installed adapter', () => {
    const installed: NotificationAdapter = {
      info: vi.fn(),
      success: vi.fn(),
      warning: vi.fn(),
      error: vi.fn(),
      destroyAll: vi.fn(),
    };
    setNotificationAdapter(installed);

    useNotification().success('Done', { duration: 1000 });

    expect(installed.success).toHaveBeenCalledWith('Done', { duration: 1000 });
  });
});

describe('message adapter fallback', () => {
  it('logs every level to the console and returns a closer from loading', () => {
    const log = vi.spyOn(console, 'log').mockImplementation(() => {});
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const error = vi.spyOn(console, 'error').mockImplementation(() => {});

    const message = useMessage();
    message.info('i');
    message.success('s');
    message.warning('w');
    message.error('e');
    const close = message.loading('l');
    close();

    expect(log).toHaveBeenCalledWith('[Info] i');
    expect(log).toHaveBeenCalledWith('[Success] s');
    expect(warn).toHaveBeenCalledWith('[Warning] w');
    expect(error).toHaveBeenCalledWith('[Error] e');
    expect(log).toHaveBeenCalledWith('[Loading] l');
    expect(log).toHaveBeenCalledWith('[Loading End] l');
  });

  it('prefers the installed adapter', () => {
    const installed = { info: vi.fn(), success: vi.fn(), warning: vi.fn(), error: vi.fn(), loading: vi.fn(() => () => {}) };
    setMessageAdapter(installed);

    useMessage().error('boom');

    expect(installed.error).toHaveBeenCalledWith('boom');
  });
});

describe('dialog adapter fallback', () => {
  it('resolves confirm to true and prompt to null so headless callers can proceed', async () => {
    vi.spyOn(console, 'log').mockImplementation(() => {});

    const dialog = useDialog();
    await expect(dialog.alert('hello')).resolves.toBeUndefined();
    await expect(dialog.confirm('sure?')).resolves.toBe(true);
    await expect(dialog.prompt('name?')).resolves.toBeNull();
  });

  it('prefers the installed adapter', async () => {
    const installed = {
      alert: vi.fn(async () => {}),
      confirm: vi.fn(async () => false),
      prompt: vi.fn(async () => 'typed'),
    };
    setDialogAdapter(installed);

    await expect(useDialog().confirm('sure?')).resolves.toBe(false);
    await expect(useDialog().prompt('name?')).resolves.toBe('typed');
  });
});

describe('logger adapter', () => {
  it('forwards each level to the matching console method by default', () => {
    const debug = vi.spyOn(console, 'debug').mockImplementation(() => {});
    const log = vi.spyOn(console, 'log').mockImplementation(() => {});
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const error = vi.spyOn(console, 'error').mockImplementation(() => {});

    const logger = useLogger();
    logger.debug('d', 1);
    logger.info('i', 2);
    logger.warn('w', 3);
    logger.error('e', 4);

    expect(debug).toHaveBeenCalledWith('d', 1);
    expect(log).toHaveBeenCalledWith('i', 2);
    expect(warn).toHaveBeenCalledWith('w', 3);
    expect(error).toHaveBeenCalledWith('e', 4);
  });

  it('prefers the installed adapter', () => {
    const installed = { debug: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn() };
    setLoggerAdapter(installed);

    useLogger().warn('careful');

    expect(installed.warn).toHaveBeenCalledWith('careful');
  });
});
