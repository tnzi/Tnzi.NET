import { describe, it, expect, beforeEach } from 'vitest';
import { createAdapterSingleton } from '../../adapters/singleton';
import { setMessageAdapter, useMessage, resetMessageAdapter, type MessageAdapter } from '../../adapters/message';

const REGISTRY_KEY = '__TNZI_CORE_ADAPTERS__';

function clearRegistry(): void {
  delete (globalThis as Record<string, unknown>)[REGISTRY_KEY];
}

describe('createAdapterSingleton', () => {
  beforeEach(() => {
    clearRegistry();
  });

  it('set / use / reset round-trips through one slot', () => {
    const slot = createAdapterSingleton('probe', () => 'fallback');

    expect(slot.use()).toBe('fallback');
    expect(slot.peek()).toBeNull();

    slot.set('installed');
    expect(slot.use()).toBe('installed');
    expect(slot.peek()).toBe('installed');

    slot.reset();
    expect(slot.use()).toBe('fallback');
  });

  it('two handles for the same name share state (the cross-chunk case)', () => {
    // tsup builds this package with splitting:false across ~20 entries, so a
    // module can be inlined more than once. Two independent handles stand in
    // for those duplicated copies: the setter lives in one, the getter in the
    // other, and they MUST still see the same adapter.
    const writer = createAdapterSingleton('probe', () => 'fallback-a');
    const reader = createAdapterSingleton('probe', () => 'fallback-b');

    writer.set('installed-by-writer');

    expect(reader.use()).toBe('installed-by-writer');
    expect(reader.peek()).toBe('installed-by-writer');
  });

  it('shares the lazily created fallback between handles', () => {
    let built = 0;
    const build = () => ({ id: ++built });
    const a = createAdapterSingleton('probe', build);
    const b = createAdapterSingleton('probe', build);

    // A duplicated fallback would give the two copies different objects - fatal
    // for stateful fallbacks such as the in-process event bus.
    expect(a.use()).toBe(b.use());
    expect(built).toBe(1);
  });

  it('keeps different names isolated', () => {
    const one = createAdapterSingleton('probe-one', () => 'one');
    const two = createAdapterSingleton('probe-two', () => 'two');

    one.set('overridden');

    expect(one.use()).toBe('overridden');
    expect(two.use()).toBe('two');
  });

  it('parks its state on globalThis under a single key', () => {
    createAdapterSingleton('probe', () => 'fallback').set('x');
    expect((globalThis as Record<string, unknown>)[REGISTRY_KEY]).toBeDefined();
  });
});

describe('adapter singletons wired to the registry', () => {
  beforeEach(() => {
    resetMessageAdapter();
  });

  it('useMessage reads back what setMessageAdapter installed', () => {
    const calls: string[] = [];
    const custom = {
      info: (c: string) => calls.push(c),
      success: () => {},
      warning: () => {},
      error: () => {},
      loading: () => () => {},
    } satisfies MessageAdapter;

    setMessageAdapter(custom);
    useMessage().info('hello');

    expect(calls).toEqual(['hello']);
  });

  it('falls back to the console adapter after reset', () => {
    setMessageAdapter({
      info: () => {},
      success: () => {},
      warning: () => {},
      error: () => {},
      loading: () => () => {},
    });
    const installed = useMessage();
    resetMessageAdapter();

    expect(useMessage()).not.toBe(installed);
  });
});
