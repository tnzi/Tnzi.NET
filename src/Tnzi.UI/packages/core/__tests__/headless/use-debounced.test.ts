import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { effectScope } from 'vue';
import { useDebounced, DEFAULT_DEBOUNCE_MS } from '../../src/headless/useDebounced';

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe('useDebounced', () => {
  it('debounces like the plain helper: one run, last arguments', () => {
    const fn = vi.fn();
    const scope = effectScope();
    const debounced = scope.run(() => useDebounced(fn, 100))!;

    debounced('a');
    debounced('b');
    vi.advanceTimersByTime(100);

    expect(fn).toHaveBeenCalledTimes(1);
    expect(fn).toHaveBeenCalledWith('b');
    scope.stop();
  });

  it('defaults to the house interval', () => {
    const fn = vi.fn();
    const scope = effectScope();
    const debounced = scope.run(() => useDebounced(fn))!;

    debounced();
    vi.advanceTimersByTime(DEFAULT_DEBOUNCE_MS - 1);
    expect(fn).not.toHaveBeenCalled();

    vi.advanceTimersByTime(1);
    expect(fn).toHaveBeenCalledTimes(1);
    scope.stop();
  });

  // The reason this composable exists: a pending run must not outlive its owner.
  // Without the scope hook the callback still fires, against whatever it closed
  // over - a torn-down component, a disposed controller.
  it('drops a pending run when the scope is disposed', () => {
    const fn = vi.fn();
    const scope = effectScope();
    const debounced = scope.run(() => useDebounced(fn, 100))!;

    debounced('typed just before navigating away');
    scope.stop();

    vi.advanceTimersByTime(1000);
    expect(fn).not.toHaveBeenCalled();
  });

  it('still runs when the scope stays alive', () => {
    const fn = vi.fn();
    const scope = effectScope();
    const debounced = scope.run(() => useDebounced(fn, 100))!;

    debounced();
    vi.advanceTimersByTime(100);

    expect(fn).toHaveBeenCalledTimes(1);
    scope.stop();
  });

  // Outside a scope it is a silent no-op rather than a warning: being called from
  // plain code is a legitimate use, and the caller then owns cancel().
  it('works outside any scope, leaving cancellation to the caller', () => {
    const fn = vi.fn();
    const debounced = useDebounced(fn, 100);

    debounced();
    debounced.cancel();
    vi.advanceTimersByTime(1000);
    expect(fn).not.toHaveBeenCalled();

    debounced();
    vi.advanceTimersByTime(100);
    expect(fn).toHaveBeenCalledTimes(1);
  });

  it('cancel is safe to call with nothing pending', () => {
    const fn = vi.fn();
    const debounced = useDebounced(fn, 100);

    expect(() => {
      debounced.cancel();
      debounced.cancel();
    }).not.toThrow();
    expect(fn).not.toHaveBeenCalled();
  });
});
