/**
 * Submenu intent - which fly-out submenu of a menu is open while a mouse moves
 * over it, and when that changes.
 *
 * A submenu opens beside its item when the pointer rests on the item
 * (`openDelay`, so sweeping down the list does not flash every submenu on the
 * way). Only one is open at a time: resting on another item with a submenu
 * switches to it, resting on an item without one closes it.
 *
 * The hard part is the diagonal. The pointer leaves the item for the submenu
 * across the rows below (or above) it, and every row it crosses would switch
 * or close the submenu under it. So when the pointer leaves the open item, it
 * gets a grace area: the triangle from where it left to the submenu's near
 * edge. While it moves inside that triangle, rows it passes over are noted but
 * not acted on; the submenu stays. The grace ends when the pointer reaches the
 * submenu (nothing changes), when it moves out of the triangle or after
 * `graceTimeout` without arriving (the last row it rested on then takes
 * effect, as if it had just been entered).
 *
 * Leaving the menu and the submenu altogether closes the submenu after
 * `closeDelay`; coming back before that keeps it. A submenu opened by a click
 * or a key (`openNow`) is pinned: the pointer leaving does not close it, since
 * the pointer did not open it. Resting on another item still switches.
 *
 * Pure state and timers, no DOM: the component feeds it pointer events and
 * the submenu's rectangle.
 */
import type { FloatingRect } from './place-beside'

export interface MenuPoint {
  readonly x: number
  readonly y: number
}

/** Pointer rest on an item before its submenu opens, or the open one switches. */
export const SUBMENU_OPEN_DELAY_MS = 100
/** Grace after the pointer has left both the menu and the submenu. */
export const SUBMENU_CLOSE_DELAY_MS = 300
/** How long the pointer may take to cross the grace area into the submenu. */
export const SUBMENU_GRACE_TIMEOUT_MS = 400

/**
 * Pixels the triangle's apex is moved back from the exit point, away from the
 * submenu, so the first move after leaving the item is never already outside.
 */
const APEX_BACKOFF = 4

export interface SubmenuIntentOptions {
  readonly openDelay?: number
  readonly closeDelay?: number
  readonly graceTimeout?: number
  /** The open submenu's rectangle in viewport coordinates, when it is laid out. */
  readonly submenuRect: () => FloatingRect | null
  /** Called with the id of the submenu now open (`null`: none). */
  readonly onChange?: (openId: string | null) => void
}

export interface SubmenuIntent {
  /** The id of the item whose submenu is open, or `null`. */
  readonly openId: string | null
  /** Whether the open submenu was opened by a click or a key. */
  readonly pinned: boolean
  /**
   * The pointer entered a row of the menu: `id` of an item that has a submenu,
   * `null` for any other row.
   */
  enterItem(id: string | null): void
  /** The pointer left the row of item `id` at `point`. */
  leaveItem(id: string, point: MenuPoint): void
  /** The pointer moved, anywhere; only matters while a grace area is active. */
  move(point: MenuPoint): void
  /** The pointer entered the open submenu. */
  enterSubmenu(): void
  /** The pointer came back into the menu or the submenu. */
  enterMenus(): void
  /** The pointer left both the menu and the submenu. */
  leaveMenus(): void
  /** Opens `id`'s submenu now, pinned (a click, Enter, ArrowRight). */
  openNow(id: string): void
  /** Closes the open submenu now. */
  close(): void
  /** Clears every timer, without a change. */
  destroy(): void
}

function sign(p: MenuPoint, a: MenuPoint, b: MenuPoint): number {
  return (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y)
}

function inTriangle(p: MenuPoint, a: MenuPoint, b: MenuPoint, c: MenuPoint): boolean {
  const d1 = sign(p, a, b)
  const d2 = sign(p, b, c)
  const d3 = sign(p, c, a)
  const hasNegative = d1 < 0 || d2 < 0 || d3 < 0
  const hasPositive = d1 > 0 || d2 > 0 || d3 > 0
  return !(hasNegative && hasPositive)
}

/**
 * Whether `point` is on the way from `exit` (where the pointer left the item)
 * into the submenu at `rect`: inside the triangle from `exit` to the two
 * corners of the submenu's edge nearest to it, or inside the submenu itself.
 * The submenu is on whichever side of `exit` its centre is.
 */
export function inSubmenuGraceArea(point: MenuPoint, exit: MenuPoint, rect: FloatingRect): boolean {
  const right = rect.left + rect.width
  const bottom = rect.top + rect.height
  if (point.x >= rect.left && point.x <= right && point.y >= rect.top && point.y <= bottom) return true
  const towardsRight = rect.left + rect.width / 2 >= exit.x
  const edgeX = towardsRight ? rect.left : right
  const apex = { x: exit.x + (towardsRight ? -APEX_BACKOFF : APEX_BACKOFF), y: exit.y }
  return inTriangle(point, apex, { x: edgeX, y: rect.top }, { x: edgeX, y: bottom })
}

export function createSubmenuIntent(options: SubmenuIntentOptions): SubmenuIntent {
  const openDelay = options.openDelay ?? SUBMENU_OPEN_DELAY_MS
  const closeDelay = options.closeDelay ?? SUBMENU_CLOSE_DELAY_MS
  const graceTimeout = options.graceTimeout ?? SUBMENU_GRACE_TIMEOUT_MS

  let openId: string | null = null
  let pinned = false
  let openTimer: ReturnType<typeof setTimeout> | null = null
  let closeTimer: ReturnType<typeof setTimeout> | null = null
  let graceTimer: ReturnType<typeof setTimeout> | null = null
  let grace: { readonly exit: MenuPoint; readonly rect: FloatingRect } | null = null
  /* The row the pointer rests on while a grace area holds changes back;
     `undefined` when it has crossed none. */
  let pending: string | null | undefined

  function set(next: string | null, nextPinned: boolean): void {
    pinned = next !== null && nextPinned
    if (next === openId) return
    openId = next
    options.onChange?.(next)
  }

  function clearOpenTimer(): void {
    if (openTimer !== null) clearTimeout(openTimer)
    openTimer = null
  }

  function clearCloseTimer(): void {
    if (closeTimer !== null) clearTimeout(closeTimer)
    closeTimer = null
  }

  function clearGrace(): void {
    if (graceTimer !== null) clearTimeout(graceTimer)
    graceTimer = null
    grace = null
    pending = undefined
  }

  function schedule(id: string | null): void {
    clearOpenTimer()
    if (id === openId) return
    openTimer = setTimeout(() => {
      openTimer = null
      set(id, false)
    }, openDelay)
  }

  /* The grace is over without the pointer reaching the submenu: the row it
     rests on now takes effect. */
  function endGrace(): void {
    const target = pending
    clearGrace()
    if (target !== undefined) schedule(target)
  }

  function clearAll(): void {
    clearOpenTimer()
    clearCloseTimer()
    clearGrace()
  }

  return {
    get openId() {
      return openId
    },
    get pinned() {
      return pinned
    },
    enterItem(id) {
      clearCloseTimer()
      if (grace) {
        pending = id
        return
      }
      schedule(id)
    },
    leaveItem(id, point) {
      if (id !== openId) {
        clearOpenTimer()
        return
      }
      const rect = options.submenuRect()
      if (!rect) return
      clearGrace()
      grace = { exit: point, rect }
      graceTimer = setTimeout(endGrace, graceTimeout)
    },
    move(point) {
      if (grace && !inSubmenuGraceArea(point, grace.exit, grace.rect)) endGrace()
    },
    enterSubmenu() {
      clearAll()
    },
    enterMenus: clearCloseTimer,
    leaveMenus() {
      clearOpenTimer()
      if (openId === null || pinned) return
      clearCloseTimer()
      closeTimer = setTimeout(() => {
        closeTimer = null
        clearGrace()
        set(null, false)
      }, closeDelay)
    },
    openNow(id) {
      clearAll()
      set(id, true)
    },
    close() {
      clearAll()
      set(null, false)
    },
    destroy: clearAll,
  }
}
