/**
 * scheduleFrame -- requestAnimationFrame with SSR/test fallback
 *
 * Uses requestAnimationFrame when available (browser), otherwise
 * falls back to setTimeout(fn, 0) for Node.js / SSR / test environments.
 */
export const scheduleFrame: (callback: () => void) => void =
  typeof requestAnimationFrame === 'function'
    ? requestAnimationFrame
    : (cb) => setTimeout(cb, 0);
