import { markRaw, shallowRef, type Component } from 'vue'

/**
 * Shell surfaces that can live inside a desktop window without being a route.
 *
 * The window manager normally resolves a window's content from the router: a
 * window IS a page. Chat is the case that does not fit - it is a companion
 * surface owned by the shell, deliberately not a route (it must not be
 * reachable by URL, and it exists identically in every layout).
 *
 * Before this, chat floated above the desktop in its own modal, which is what
 * made it read as "exclusive": always on top of every window, absent from the
 * taskbar, outside the z-order. Registering it as a panel makes it an ordinary
 * citizen of the window manager - focus, minimise, stacking and the taskbar
 * button all come from the same code paths every other window uses.
 *
 * A module-level registry rather than provide/inject: the consumer of a panel
 * (`TDesktopWindowHost`) sits several layers below whoever owns the surface,
 * and the registration is a fact about the running app, not about a subtree.
 * Same shape as `registerBrandIcon`.
 */
interface PanelEntry {
  component: Component
  /** Tile colour, resolved at RENDER time - see `registerDesktopPanel`. */
  color?: string
}

/**
 * REACTIVE, and that is load-bearing.
 *
 * Registration happens when the owning component mounts, which is not
 * guaranteed to be before a window that wants the panel renders - a desktop
 * restored from localStorage paints its windows immediately, while the chat
 * host is still coming up. With a plain Map the window's `computed` resolved
 * `null` once, never re-ran, and the restored chat window sat on "Page not
 * found" forever. Replacing the map (rather than mutating it) is what makes
 * dependent computeds re-evaluate; registration is rare enough that the copy
 * costs nothing.
 */
const panels = shallowRef(new Map<string, PanelEntry>())

/**
 * Make a component available as window content under `key`.
 *
 * Call it from the component that owns the surface (chat registers its own),
 * so the panel and the thing that opens it cannot drift apart. Registering the
 * same key twice replaces it, which is what a hot reload needs.
 */
export function registerDesktopPanel(
  key: string,
  component: Component,
  options?: { color?: string },
): void {
  // `markRaw` because this lands in a Pinia-adjacent lookup and then in a
  // `<component :is>`: making a component definition reactive is pure overhead
  // and Vue warns about it.
  const next = new Map(panels.value)
  next.set(key, { component: markRaw(component) as Component, color: options?.color })
  panels.value = next
}

/** The component for `key`, or null when nothing has registered it. */
export function getDesktopPanel(key: string): Component | null {
  return panels.value.get(key)?.component ?? null
}

/**
 * The panel's tile colour, or null.
 *
 * Read at render time, never stored on the window. Windows are persisted, so a
 * colour written into the record would freeze today's brand value into
 * localStorage: change it in a later version and every already-open window
 * keeps the old one, while everything else on screen has moved on. Same reason
 * the window icon is a declared value resolved late rather than a baked one.
 */
export function getDesktopPanelTint(key: string): string | null {
  return panels.value.get(key)?.color ?? null
}

/** Drop a registration. Only really needed by tests. */
export function unregisterDesktopPanel(key: string): void {
  const next = new Map(panels.value)
  next.delete(key)
  panels.value = next
}
