/**
 * @tnzi/core/utils/timing
 *
 * Debounce and throttle utilities.
 */

/**
 * A debounced function, plus the handle needed to drop a pending run.
 */
export interface Debounced<A extends unknown[]> {
  (...args: A): void;
  /** Drop the pending run, if any. Safe to call repeatedly. */
  cancel(): void;
}

/**
 * Debounce function.
 *
 * The returned function carries `.cancel()`. Without it a caller that goes away
 * before the delay elapses has no way to stop the run, and the callback fires
 * against whatever it closed over - a torn-down component, a disposed scope, a
 * request for a screen nobody is looking at any more. See `useDebounced` in
 * `@tnzi/core/headless` for the variant that cancels itself on scope disposal.
 */
export function debounce<A extends unknown[]>(
  fn: (...args: A) => unknown,
  delay: number
): Debounced<A> {
  let timeoutId: ReturnType<typeof setTimeout> | null = null;

  const cancel = () => {
    if (timeoutId === null) return;
    clearTimeout(timeoutId);
    timeoutId = null;
  };

  const run = (...args: A) => {
    cancel();
    timeoutId = setTimeout(() => {
      timeoutId = null;
      fn(...args);
    }, delay);
  };

  return Object.assign(run, { cancel });
}

/**
 * Throttle function
 */
export function throttle<T extends (...args: unknown[]) => unknown>(
  fn: T,
  limit: number
): (...args: Parameters<T>) => void {
  let inThrottle = false;

  return function (...args: Parameters<T>) {
    if (!inThrottle) {
      fn(...args);
      inThrottle = true;
      setTimeout(() => {
        inThrottle = false;
      }, limit);
    }
  };
}
