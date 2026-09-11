/**
 * @tnzi/core/headless/useDebounced
 *
 * Debounce a callback for the lifetime of the surrounding effect scope.
 *
 * @example
 * ```ts
 * const search = useDebounced((keyword: string) => void query.fetch({ keyword }));
 * // template: <input @input="search($event.target.value)" />
 * // nothing to clean up - the pending run is dropped when the scope goes away
 * ```
 */

import { onScopeDispose } from 'vue';
import { debounce, type Debounced } from '../utils/timing';

/**
 * The house interval for keyword boxes: long enough to swallow a burst of
 * keystrokes, short enough that a list still feels live.
 *
 * It is a default, not a rule. What it buys is that the interval stops being a
 * per-page decision - hand-rolled copies drift (300 here, 350 there) for no
 * reason anyone can reconstruct later.
 */
export const DEFAULT_DEBOUNCE_MS = 300;

/**
 * Debounce `fn`, and drop any pending run when the surrounding scope is disposed.
 *
 * @remarks
 * The plain {@link debounce} in `@tnzi/core/utils` is the right tool for a
 * standalone value. This one exists for the case that keeps going wrong: a
 * debounced handler owned by something that can go away. Type a keyword, navigate
 * before the delay elapses, and the timer still fires - against a torn-down
 * component, a disposed controller, a screen nobody is looking at. The symptom is
 * a stray request and, when the callback writes state, an "is not a function" or a
 * silent write into a discarded object.
 *
 * ★ Cleanup hangs off `onScopeDispose`, not `onBeforeUnmount`, so it works in
 * every place a composable legitimately runs: a component `setup`, a standalone
 * `effectScope`, another composable called from either. `onBeforeUnmount` would
 * require a component instance and warn when there is none, which would push
 * callers back to hand-rolling the timer in exactly the cases they most want this.
 * Outside any scope it is a silent no-op: the caller then owns `.cancel()`, which
 * is the same contract `debounce` already offers.
 *
 * @param fn    the work to defer; receives whatever arguments the returned function is called with
 * @param delay milliseconds to wait after the last call; defaults to {@link DEFAULT_DEBOUNCE_MS}
 * @returns the debounced function, carrying `.cancel()` for callers that need to drop a pending run early
 */
export function useDebounced<A extends unknown[]>(
  fn: (...args: A) => void,
  delay: number = DEFAULT_DEBOUNCE_MS
): Debounced<A> {
  const debounced = debounce<A>(fn, delay);

  // `true` = fail silently when there is no active scope. Being called outside one
  // is a legitimate use, not a mistake worth a console warning.
  onScopeDispose(debounced.cancel, true);

  return debounced;
}
