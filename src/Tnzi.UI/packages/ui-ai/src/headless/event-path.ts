/**
 * Where an event started, as it was when it started.
 *
 * In a browser, a click from a real pointer runs the page's microtasks after
 * every listener, so Vue re-renders between one listener and the next. A
 * button whose handler swaps its icon has, by the time an ancestor's listener
 * runs, a new `<svg>` in place of the one under the pointer: `event.target` is
 * then detached, `target.closest('button')` finds nothing and `contains`
 * answers false. A click made by script (`element.click()`) runs microtasks
 * only after the whole dispatch, which is why such a bug passes every test
 * that clicks by script.
 *
 * So the path is taken at the very start of the dispatch: `trackEventPaths()`
 * puts one capturing listener per event type on `window`, which runs before
 * any element's handler (nothing has re-rendered yet) and keeps the event's
 * path. A listener that asks "did this start on a button?" or "inside my
 * element?" after another listener has run asks it here instead of walking up
 * from `event.target`. Without a recorded path (tracking not started, an event
 * dispatched on a detached node) it falls back to `composedPath()` and then to
 * the target's ancestors.
 */

const TRACKED = ['click', 'pointerdown', 'pointerup', 'mousedown', 'mouseup', 'contextmenu'] as const
const recorded = new WeakMap<Event, Element[]>()
let installed = false

function record(event: Event): void {
  const path = typeof event.composedPath === 'function' ? event.composedPath() : []
  recorded.set(event, path.filter((node): node is Element => node instanceof Element))
}

/** Starts keeping the path of clicks and presses, once per page. Components that read paths call it in setup. */
export function trackEventPaths(): void {
  if (installed || typeof window === 'undefined') return
  installed = true
  for (const type of TRACKED) window.addEventListener(type, record, true)
}

/** The elements the event passed through, innermost first, as when it was dispatched. */
export function eventPath(event: Event): Element[] {
  const kept = recorded.get(event)
  if (kept && kept.length > 0) return kept
  const path = typeof event.composedPath === 'function' ? event.composedPath() : []
  if (path.length > 0) return path.filter((node): node is Element => node instanceof Element)
  const out: Element[] = []
  for (let node = event.target instanceof Element ? event.target : null; node; node = node.parentElement) out.push(node)
  return out
}

/** The nearest element on the event's path that matches `selector`, up to (not past) `within`. */
export function closestOnPath(event: Event, selector: string, within?: Element | null): Element | null {
  for (const element of eventPath(event)) {
    if (element.matches(selector)) return element
    if (within && element === within) return null
  }
  return null
}

/** Whether the event started inside `element` (or on it). */
export function startedWithin(event: Event, element: Element | null | undefined): boolean {
  return !!element && eventPath(event).includes(element)
}
