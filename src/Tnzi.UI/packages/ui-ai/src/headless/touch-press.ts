/**
 * Lets `:active` show on iOS.
 *
 * iOS Safari applies `:active` to a touched element only when the page listens
 * for `touchstart` somewhere; without a listener a press has no feedback at
 * all. One empty passive listener on the document is enough, so this adds it
 * once per page. Harmless elsewhere: the listener never blocks scrolling.
 */

let enabled = false

function noop(): void {}

/** Installs the listener once; does nothing without `document`. */
export function enableTouchPress(): void {
  if (enabled || typeof document === 'undefined') return
  enabled = true
  document.addEventListener('touchstart', noop, { passive: true })
}
