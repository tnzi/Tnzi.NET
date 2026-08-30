import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import 'pinia-plugin-persistedstate'
import { useAdminRouteStore } from './useAdminRouteStore'
import {
  MIN_WINDOW_SIZE,
  fitGeometryToSurface,
  isPositionReachable,
  isRememberedSizeUsable,
  placeNewWindow,
  rescaleWindowPosition,
  resolveWindowSize,
  type OccupiedSpot,
  type Surface,
  type WindowSizeHint,
} from '../headless/window-sizing'

/**
 * One location inside a window's own navigation stack.
 *
 * This is deliberately NOT a vue-router `RouteLocationNormalized` - a window
 * owns a stack of *destinations*, and the route object handed to the page is
 * synthesised from the active entry by `useWindowRoute`. Keeping this shape
 * flat and serialisable is what lets the whole desktop survive a reload.
 */
export interface WindowLocation {
  /** Route name. The window resolves its component from this. */
  name: string
  /** Resolved pathname (no query). Mirrors `route.path`. */
  path: string
  params: Record<string, string>
  query: Record<string, string>
  /**
   * Title for THIS entry - a raw `meta.title` i18n key, or a record name once
   * the page reports one through `useTabTitle`.
   *
   * Per-entry rather than per-window because a window's title belongs to the
   * page it is showing: drill into a user and the title becomes that user's
   * name; go back and it has to say "Users" again. With one title on the
   * window, the record's name stayed stranded on the list page after a back.
   */
  title: string
  icon?: string
  /**
   * A shell surface instead of a route (see `headless/desktop-panels`). Chat is
   * the one case: it is not a page, has no URL, and must still behave like an
   * ordinary window - taskbar button, z-order, minimise and all.
   */
  panel?: string
}

/**
 * A window on the desktop.
 *
 * NOT the same record as `AdminTab`, and deliberately a separate store: a tab
 * is "one route", a window is "one opened thing". Two windows can sit on the
 * same route (customer A and customer B side by side), and a window carries
 * geometry / z-order / minimise state that means nothing to the tab bar.
 * Folding these fields into `AdminTab` would push desktop-only concerns into
 * every other layout mode.
 */
export interface AdminDesktopWindow {
  /** Stable per-window id. Survives reload; participates in tab/breadcrumb
   *  keys so two windows on the same route don't overwrite each other. */
  id: string
  /** Icon shown in the title bar and on the taskbar button. */
  icon?: string
  /** Title shown in the title bar and on the taskbar button. */
  title: string
  /** Navigation stack. `stack[stackIndex]` is where the window is now. */
  stack: WindowLocation[]
  stackIndex: number
  /** Geometry in px, relative to the desktop surface (not the viewport). */
  x: number
  y: number
  width: number
  height: number
  minimized: boolean
  maximized: boolean
  /** Stacking order. Higher is nearer the front. Normalised on hydrate. */
  z: number
  /** Geometry to return to when un-maximising. Absent while not maximised. */
  restore?: { x: number; y: number; width: number; height: number }
  /**
   * What the page asked its window to be (`meta.window`), kept so that
   * "reset window sizes" restores THIS window's default rather than the
   * generic one - a node-graph editor asked for `full` and should get it back.
   */
  sizeHint?: WindowSizeHint
}

/**
 * Sizing and placement live in `headless/window-sizing.ts` - pure, and shared
 * with the drag gesture so "how much must stay on screen" has one definition.
 * Re-exported here because that is where consumers have always imported it.
 */
export { MIN_WINDOW_SIZE }

/** Geometry remembered for one module between sessions. */
export interface RememberedGeometry {
  x: number
  y: number
  width: number
  height: number
}

export interface OpenWindowInput {
  name: string
  path: string
  title: string
  icon?: string
  params?: Record<string, string>
  query?: Record<string, string>
  /** What this page wants its window to be, from `meta.window`. */
  size?: WindowSizeHint
  /** Registered panel key, for windows whose content is not a route. */
  panel?: string
}

/**
 * Everything a window needs, pulled off a resolved route in ONE place.
 *
 * Four call sites were hand-building this object - the icon grid, the User
 * Center entry, a global-search hit, the Settings gear - and three of them had
 * quietly forgotten `size`, so a route's `meta.window` only took effect when it
 * was opened from the desktop. Exactly the failure mode the route-record meta
 * whitelist keeps producing: a field list copied by hand drifts field by field.
 *
 * @param route         Resolved route (or a menu entry standing in for one).
 * @param fallbackTitle Used when the route has no `meta.title` - the menu
 *                      entry's already-translated label, for instance.
 */
export function routeToWindowInput(
  route: { name: string; path: string; meta?: Record<string, unknown> },
  fallbackTitle?: string,
): OpenWindowInput {
  const meta = route.meta ?? {}
  return {
    name: route.name,
    path: route.path,
    // Raw `meta.title` (an i18n key for built-in pages), resolved at render
    // time so switching language relabels windows that are already open.
    title: (meta.title as string | undefined) ?? fallbackTitle ?? route.name,
    // Only what the route DECLARED. The bundled table sets no `meta.icon` at
    // all, so this is usually undefined - and it should stay that way: windows
    // are persisted, and baking today's glyph into localStorage would leave a
    // restored taskbar showing an icon the sidebar no longer uses. The render
    // side (`resolveWindowIcon`) derives it from the shared map instead.
    icon: meta.icon as string | undefined,
    size: meta.window as WindowSizeHint | undefined,
  }
}

function toLocation(input: OpenWindowInput): WindowLocation {
  return {
    name: input.name,
    path: input.path,
    params: { ...(input.params ?? {}) },
    query: { ...(input.query ?? {}) },
    title: input.title,
    icon: input.icon,
    panel: input.panel,
  }
}

/**
 * Fit a window back into a surface of the given size.
 *
 * For HYDRATE, where both size and position may be stale: a window persisted at
 * x=1800 on a wide monitor is entirely off-canvas on a laptop, and one
 * persisted at 1900px wide cannot be shown at all. Resizing is acceptable here
 * because the geometry came from a different screen.
 *
 * The gentler `reflowToSurface` is what runs on a live resize - see there.
 */
export function clampToSurface(
  win: AdminDesktopWindow,
  surface: Surface,
): AdminDesktopWindow {
  return { ...win, ...fitGeometryToSurface(win, surface) }
}

export const useAdminDesktopStore = defineStore('admin-desktop', () => {
  const windows = ref<AdminDesktopWindow[]>([])
  /** Id of the focused window, or null when the desktop itself has focus. */
  const activeId = ref<string | null>(null)
  /** Monotonic counter behind window ids. Persisted so ids stay unique
   *  across reloads (a fresh counter would re-mint an id already in use). */
  const seq = ref(0)
  /** Highest z handed out so far. Normalised on hydrate so it can't run away. */
  const topZ = ref(0)
  /**
   * Measured size of the desktop surface. Written by the host on every resize.
   * NOT persisted - it is a measurement of this screen, and restoring last
   * session's is what puts windows off the edge of this one.
   */
  const surface = ref<Surface>({ width: 0, height: 0 })
  /**
   * Geometry the user last left a module at, keyed by MODULE rather than by
   * page: one window serves a whole module now, so a size set while looking at
   * Users is the size that window should come back at even if it was closed on
   * Roles. Keying by page would save under whichever page happened to be open
   * when it closed and read back a different one.
   */
  const geometryMemory = ref<Record<string, RememberedGeometry>>({})

  /** Windows in stacking order, back to front. Render order for the canvas. */
  /**
   * Windows sorted back-to-front.
   *
   * NOT what the desktop renders with: every window carries its own inline
   * `z-index`, so painting order is already settled and sorting the DOM adds
   * nothing - except that reordering MOVES the nodes, and moving a node
   * restarts its CSS animations. Measured: one focus change replayed the
   * open animation on BOTH windows, which is the flicker you see on the window
   * being sent to the back. Kept for callers that want the order as data.
   */
  const stacked = computed<AdminDesktopWindow[]>(() =>
    [...windows.value].sort((a, b) => a.z - b.z),
  )
  /** Windows in open order. Render order for the taskbar - buttons must not
   *  jump around when focus changes, which sorting by z would do. */
  const taskbarWindows = computed<AdminDesktopWindow[]>(() => windows.value)

  const activeWindow = computed<AdminDesktopWindow | null>(
    () => windows.value.find((w) => w.id === activeId.value) ?? null,
  )

  function find(id: string): AdminDesktopWindow | undefined {
    return windows.value.find((w) => w.id === id)
  }

  /** Where `stack[stackIndex]` points for a given window. */
  function locationOf(id: string): WindowLocation | null {
    const win = find(id)
    return win ? (win.stack[win.stackIndex] ?? null) : null
  }

  function patch(id: string, change: Partial<AdminDesktopWindow>): void {
    windows.value = windows.value.map((w) => (w.id === id ? { ...w, ...change } : w))
  }

  function setSurface(next: Surface): void {
    surface.value = { width: next.width, height: next.height }
  }

  /** The module a route's geometry is remembered under. */
  /**
   * The identity a window is grouped and remembered by - "one window per
   * module", and per panel.
   *
   * Panels are keyed apart from routes on purpose: the chat PANEL and the chat
   * admin MODULE are different things that happen to share a word. Keying both
   * off the route name would make opening the Chat module's pages focus the IM
   * window instead, or vice versa.
   */
  function groupKeyOf(loc: WindowLocation | OpenWindowInput | undefined): string {
    if (!loc) return ''
    if (loc.panel) return `panel:${loc.panel}`
    return useAdminRouteStore().moduleKeyOfRoute(loc.name)
  }

  function memoryKeyOf(loc: WindowLocation | OpenWindowInput | undefined): string {
    return groupKeyOf(loc)
  }

  /** Where the windows currently ON SCREEN have their top-left corner. */
  function occupiedSpots(): OccupiedSpot[] {
    return windows.value.filter((w) => !w.minimized).map((w) => ({ x: w.x, y: w.y }))
  }

  /**
   * Opening geometry: what the user last left this module at, or what the page
   * asks for - and in either case, somewhere nothing is already sitting.
   *
   * A remembered size is only reused while it still fits - see
   * `isRememberedSizeUsable`. When it is rejected the page's own hint decides,
   * so the fallback is "the right size for this page", not some global average.
   * Position is checked separately: a remembered size can be fine while the
   * position it was at is now off-screen.
   *
   * The remembered POSITION is a preference, not a guarantee: it is handed to
   * `placeNewWindow` as the starting point, which keeps it when it is free and
   * steps off it when another window is already there. Two modules the user
   * dragged to the same corner would otherwise open exactly on top of each
   * other, and the second would look like it never opened.
   */
  function openingGeometry(input: OpenWindowInput): RememberedGeometry {
    const remembered = geometryMemory.value[memoryKeyOf(input)]
    const size = isRememberedSizeUsable(remembered, surface.value)
      ? { width: remembered!.width, height: remembered!.height }
      : resolveWindowSize(input.size, surface.value)

    const preferred =
      remembered && isPositionReachable({ ...remembered, ...size }, surface.value)
        ? { x: remembered.x, y: remembered.y }
        : undefined

    return placeNewWindow(size, surface.value, occupiedSpots(), preferred)
  }

  /**
   * Open a window. Always mints a NEW window - "focus the existing one instead"
   * is a caller decision (the icon grid does it, the "open in new window"
   * command deliberately does not), because the desktop's whole point is that
   * the same feature can be open twice.
   */
  function open(input: OpenWindowInput, geometry?: Partial<AdminDesktopWindow>): string {
    seq.value += 1
    const id = `w${seq.value}`
    const spot = openingGeometry(input)
    const win: AdminDesktopWindow = {
      id,
      icon: input.icon,
      title: input.title,
      stack: [toLocation(input)],
      stackIndex: 0,
      sizeHint: input.size,
      ...spot,
      minimized: false,
      maximized: false,
      z: (topZ.value += 1),
      ...geometry,
    }
    windows.value = [...windows.value, win]
    activeId.value = id
    return id
  }

  function close(id: string): void {
    windows.value = windows.value.filter((w) => w.id !== id)
    if (activeId.value !== id) return
    // Focus falls to whatever is now nearest the front, not to "the previous
    // one opened" - that matches what the user sees after the window vanishes.
    const next = [...windows.value].sort((a, b) => b.z - a.z).find((w) => !w.minimized)
    activeId.value = next?.id ?? null
  }

  function closeAll(): void {
    windows.value = []
    activeId.value = null
  }

  /** Bring a window to the front and focus it. Un-minimises on the way. */
  function focus(id: string): void {
    const win = find(id)
    if (!win) return
    activeId.value = id
    const change: Partial<AdminDesktopWindow> = win.minimized ? { minimized: false } : {}
    // Already frontmost and not minimised - nothing to renumber.
    if (win.z === topZ.value && !win.minimized) return
    patch(id, { ...change, z: (topZ.value += 1) })
  }

  function minimize(id: string): void {
    patch(id, { minimized: true })
    if (activeId.value !== id) return
    const next = [...windows.value].sort((a, b) => b.z - a.z).find((w) => w.id !== id && !w.minimized)
    activeId.value = next?.id ?? null
  }

  /** Taskbar button behaviour: focused → minimise, otherwise → focus. */
  function toggleMinimize(id: string): void {
    const win = find(id)
    if (!win) return
    if (!win.minimized && activeId.value === id) minimize(id)
    else focus(id)
  }

  function toggleMaximize(id: string, surface?: { width: number; height: number }): void {
    const win = find(id)
    if (!win) return
    if (win.maximized) {
      const back = win.restore
      patch(id, {
        maximized: false,
        ...(back ? { x: back.x, y: back.y, width: back.width, height: back.height } : {}),
        restore: undefined,
      })
      return
    }
    patch(id, {
      maximized: true,
      restore: { x: win.x, y: win.y, width: win.width, height: win.height },
      ...(surface ? { x: 0, y: 0, width: surface.width, height: surface.height } : {}),
    })
    focus(id)
  }

  function setGeometry(id: string, geometry: Partial<Pick<AdminDesktopWindow, 'x' | 'y' | 'width' | 'height'>>): void {
    patch(id, geometry)
  }

  /**
   * Rename the window AND the stack entry it is showing.
   *
   * Stamping the entry is what makes the name survive navigation: go forward to
   * a record then back, and the list is called "Users" again while the record
   * keeps its own name for when you go forward.
   */
  function setTitle(id: string, title: string): void {
    const win = find(id)
    if (!win) return
    const current = win.stack[win.stackIndex]
    if (win.title === title && current?.title === title) return
    const stack = win.stack.map((l, i) => (i === win.stackIndex ? { ...l, title } : l))
    patch(id, { title, stack })
  }

  /**
   * Navigate inside a window - the window equivalent of `router.push`.
   *
   * Truncates any forward entries first, exactly like browser history: going
   * back twice and then somewhere new discards the branch you left.
   */
  function navigate(id: string, to: OpenWindowInput): void {
    const win = find(id)
    if (!win) return
    const stack = [...win.stack.slice(0, win.stackIndex + 1), toLocation(to)]
    patch(id, { stack, stackIndex: stack.length - 1, title: to.title, icon: to.icon ?? win.icon })
  }

  /** In-place navigation - the window equivalent of `router.replace`. */
  function replace(id: string, to: OpenWindowInput): void {
    const win = find(id)
    if (!win) return
    const stack = [...win.stack]
    stack[win.stackIndex] = toLocation(to)
    patch(id, { stack, title: to.title, icon: to.icon ?? win.icon })
  }

  /** Whether this window has anywhere to go back to. Drives the back
   *  affordance INSTEAD of the browser's global `history.state.back`, which
   *  says nothing about which window is asking. */
  function canGoBack(id: string): boolean {
    const win = find(id)
    return !!win && win.stackIndex > 0
  }

  /** Move to another entry, adopting its title and icon - the chrome has to
   *  describe the page the window is actually showing. */
  function goToIndex(id: string, index: number): void {
    const win = find(id)
    const target = win?.stack[index]
    if (!win || !target) return
    patch(id, { stackIndex: index, title: target.title, icon: target.icon ?? win.icon })
  }

  function back(id: string): void {
    const win = find(id)
    if (!win || win.stackIndex <= 0) return
    goToIndex(id, win.stackIndex - 1)
  }

  function forward(id: string): void {
    const win = find(id)
    if (!win || win.stackIndex >= win.stack.length - 1) return
    goToIndex(id, win.stackIndex + 1)
  }

  /**
   * Open a route as a window, reusing the one its module is already in.
   *
   * **One window per module**, the way an OS runs one instance of an app. Used
   * by the desktop's own icon grid AND by shell chrome that would otherwise
   * `router.push` - the User Center entry in the user menu, a global-search
   * result. Those pushes navigate a router whose outlet the desktop replaced,
   * so in this layout they do visibly nothing; measured on both.
   *
   * `focusOnly` is for "open the module" (a desktop tile): the user asked for
   * the module, and moving it to page one would throw away whichever page they
   * had left open. Everything else names a page, and that page IS the request.
   *
   * Returns the window and whether it had to be minted, so the caller can place
   * a new one without disturbing an existing one.
   */
  function openOrFocusRoute(
    input: OpenWindowInput,
    options?: { focusOnly?: boolean },
  ): { id: string; minted: boolean } {
    const moduleKey = groupKeyOf(input)
    const existing = moduleKey
      ? windows.value.find((w) => groupKeyOf(w.stack[w.stackIndex]) === moduleKey)
      : undefined

    if (existing) {
      const current = existing.stack[existing.stackIndex]?.name
      // Already showing it - focusing is the whole job, and pushing a duplicate
      // stack entry would make the back button a no-op that looks broken.
      if (!options?.focusOnly && current !== input.name) navigate(existing.id, input)
      focus(existing.id)
      return { id: existing.id, minted: false }
    }
    return { id: open(input), minted: true }
  }

  /**
   * Record where the user left a window, so its module opens there next time.
   *
   * Called at the END of a drag or resize, never from the programmatic paths
   * (maximise, the rescue below, hydrate clamping) - those are the framework
   * moving the window, and writing them down would teach it that a rescue
   * position is the user's preference.
   *
   * A maximised window is skipped: its geometry is the surface, not a choice.
   */
  function rememberGeometry(id: string): void {
    const win = find(id)
    if (!win || win.maximized) return
    const key = memoryKeyOf(win.stack[win.stackIndex])
    if (!key) return
    geometryMemory.value = {
      ...geometryMemory.value,
      [key]: { x: win.x, y: win.y, width: win.width, height: win.height },
    }
  }

  /** Fit every window to this surface - size included. For hydrate. */
  function clampAll(next: Surface): void {
    setSurface(next)
    windows.value = windows.value.map((w) =>
      w.maximized ? { ...w, x: 0, y: 0, width: next.width, height: next.height } : clampToSurface(w, next),
    )
  }

  /**
   * Re-lay the desktop for a resized browser.
   *
   * Every window keeps its place RELATIVE to the surface - centred stays
   * centred, parked against the right edge stays against the right edge -
   * rather than holding a pixel offset that means something different on a
   * different-sized screen.
   *
   * Runs on EVERY surface change, which `clampAll` deliberately does not: the
   * old code clamped once per session and then only tracked maximised windows,
   * so shrinking the browser left ordinary windows stranded off the edge with
   * no way to drag them back - measured, and the reason this exists.
   *
   * Position only; see `rescaleWindowPosition` for why size is left alone.
   */
  function reflowToSurface(next: Surface): void {
    const from = surface.value
    setSurface(next)
    windows.value = windows.value.map((w) => {
      if (w.maximized) return { ...w, x: 0, y: 0, width: next.width, height: next.height }
      return { ...w, ...rescaleWindowPosition(w, from, next) }
    })
  }

  /**
   * Put every window back to its module's default size and a fresh cascade.
   *
   * The escape hatch for a desktop that has got into a state the user cannot
   * undo by hand - and the honest answer to "I cannot find my window", which no
   * amount of automatic clamping fully prevents on a screen that keeps changing.
   */
  function resetLayout(): void {
    geometryMemory.value = {}
    const placed: OccupiedSpot[] = []
    windows.value = windows.value.map((w) => {
      const size = resolveWindowSize(w.sizeHint, surface.value)
      const spot = placeNewWindow(size, surface.value, placed)
      placed.push({ x: spot.x, y: spot.y })
      return { ...w, maximized: false, restore: undefined, ...spot }
    })
  }

  return {
    windows,
    activeId,
    seq,
    topZ,
    stacked,
    taskbarWindows,
    activeWindow,
    find,
    locationOf,
    open,
    openOrFocusRoute,
    close,
    closeAll,
    focus,
    minimize,
    toggleMinimize,
    toggleMaximize,
    setGeometry,
    setSurface,
    surface,
    geometryMemory,
    rememberGeometry,
    reflowToSurface,
    resetLayout,
    setTitle,
    navigate,
    replace,
    canGoBack,
    back,
    forward,
    clampAll,
  }
}, {
  persist: {
    key: 'tnzi-admin-desktop',
    // Only flat serialisable state. `stacked`/`activeWindow` are computed and
    // the actions are functions - persisting either would serialise garbage.
    pick: ['windows', 'activeId', 'seq', 'topZ', 'geometryMemory'],
    /**
     * Hydration bypasses every action, so anything a newer build no longer
     * accepts has to be repaired here or it renders as a ghost.
     *
     * Two repairs, both for failures that are invisible until they bite:
     *  - **z normalisation** - `topZ` grows monotonically for the life of a
     *    session and is persisted; without compaction a long-lived desktop
     *    hydrates with z in the thousands and every new window has to out-bid it.
     *  - **stack integrity** - a persisted `stackIndex` pointing past the end of
     *    a (possibly truncated) stack yields `undefined` as the current
     *    location, which renders an empty window with no way to fix it.
     *
     * Geometry clamping is NOT done here: the desktop surface size isn't known
     * at hydrate time. `TDesktopHost` calls `clampAll` once it has measured.
     */
    afterHydrate: (ctx) => {
      const store = ctx.store as unknown as {
        windows: AdminDesktopWindow[]
        activeId: string | null
        topZ: number
      }
      const list = Array.isArray(store.windows) ? store.windows : []
      const sound = list.filter(
        (w) => w && typeof w.id === 'string' && Array.isArray(w.stack) && w.stack.length > 0,
      )
      const ordered = [...sound].sort((a, b) => (a.z ?? 0) - (b.z ?? 0))
      store.windows = ordered.map((w, i) => ({
        ...w,
        z: i + 1,
        stackIndex: Math.min(Math.max(w.stackIndex ?? 0, 0), w.stack.length - 1),
      }))
      store.topZ = store.windows.length
      if (store.activeId && !store.windows.some((w) => w.id === store.activeId)) {
        store.activeId = null
      }
    },
  },
})
