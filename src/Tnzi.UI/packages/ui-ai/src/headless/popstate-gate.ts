/**
 * The first `popstate` listener of a window.
 *
 * Listeners at the window run in the order they were added (capture or not,
 * measured in Chromium), and only a listener that runs before a router's can
 * keep a `popstate` from reaching it. This adds one listener, as early as the
 * caller can (before the router is created), that hands each event to a
 * handler set later. Kept apart from the back layers themselves so the app
 * assembly can claim the place without carrying them.
 */

type PopStateHandler = (event: PopStateEvent) => void

interface Gate {
  handler: PopStateHandler | null
}

interface EventSource {
  addEventListener(type: string, listener: (event: Event) => void, options?: boolean | AddEventListenerOptions): void
}

const gates = new WeakMap<EventSource, Gate>()

/** Adds the window's gate listener once; later calls return the same gate. */
export function openPopStateGate(target: EventSource): { setHandler(handler: PopStateHandler): void } {
  let gate = gates.get(target)
  if (!gate) {
    const created: Gate = { handler: null }
    target.addEventListener('popstate', (event) => created.handler?.(event as PopStateEvent), { capture: true })
    gates.set(target, created)
    gate = created
  }
  const current = gate
  return {
    setHandler(handler) {
      current.handler = handler
    },
  }
}

/** Claims the place for the page's window, before a router is created. Nothing without `window`. */
export function claimPopState(): void {
  if (typeof window !== 'undefined') openPopStateGate(window)
}
