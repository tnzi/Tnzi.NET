/**
 * Flyout - when a popover that opens from a rail button opens and closes.
 *
 * A flyout opens on hover, on keyboard focus or on a click, and each way in
 * keeps it open by a different rule, which is the whole of this module:
 *
 * - `hover`: opened by the pointer resting on the button (`openDelay`, so
 *   sweeping across the rail does not flash it). It stays while the pointer is
 *   on the button or the panel, and closes `closeDelay` after it has left
 *   both: the grace period lets the pointer cross the gap between them, or cut
 *   a corner, without the panel vanishing under it.
 * - `focus`: opened by tabbing onto the button. It stays while focus is on the
 *   button or inside the panel, and closes when focus leaves both.
 * - `pinned`: opened (or kept) by a click. Leaving does not close it; a second
 *   click, a press outside, Escape or a choice does. Clicking the button of a
 *   flyout the hover opened pins it rather than closing it, since the click is
 *   what the user would have done to open it anyway.
 *
 * Touch never opens on "hover" (a tap is a click). Escape, a press outside and
 * a choice made in the panel close it from any state; the component returns
 * focus to the button where that is needed.
 *
 * Pure state and timers, no DOM: `TRailFlyout` feeds it events.
 */

export type FlyoutState = 'closed' | 'hover' | 'focus' | 'pinned'

/** Pointer rest before a hover opens the flyout. */
export const FLYOUT_OPEN_DELAY_MS = 150
/** Grace period after the pointer leaves the button and the panel. */
export const FLYOUT_CLOSE_DELAY_MS = 300

export interface FlyoutOptions {
  readonly openDelay?: number
  readonly closeDelay?: number
  /** Called with the new state whenever it changes. */
  readonly onChange?: (state: FlyoutState) => void
}

export interface FlyoutController {
  readonly state: FlyoutState
  readonly open: boolean
  /** The pointer entered the button or the panel. */
  pointerEnter(pointerType: string): void
  /** The pointer left the button and the panel. */
  pointerLeave(): void
  /** Focus entered the button or the panel; `keyboard` when a key moved it there. */
  focusIn(keyboard: boolean): void
  /** Focus left the button and the panel. */
  focusOut(): void
  /** The button was clicked. */
  click(): void
  /** Opens it now, pinned. */
  openPinned(): void
  /** Escape, a press outside, a choice: closes from any state. */
  close(): void
  destroy(): void
}

export function createFlyoutController(options: FlyoutOptions = {}): FlyoutController {
  const openDelay = options.openDelay ?? FLYOUT_OPEN_DELAY_MS
  const closeDelay = options.closeDelay ?? FLYOUT_CLOSE_DELAY_MS
  let state: FlyoutState = 'closed'
  let openTimer: ReturnType<typeof setTimeout> | null = null
  let closeTimer: ReturnType<typeof setTimeout> | null = null

  function clearTimers(): void {
    if (openTimer !== null) clearTimeout(openTimer)
    if (closeTimer !== null) clearTimeout(closeTimer)
    openTimer = null
    closeTimer = null
  }

  function set(next: FlyoutState): void {
    if (next === state) return
    state = next
    options.onChange?.(next)
  }

  function close(): void {
    clearTimers()
    set('closed')
  }

  return {
    get state() {
      return state
    },
    get open() {
      return state !== 'closed'
    },
    pointerEnter(pointerType) {
      if (closeTimer !== null) clearTimeout(closeTimer)
      closeTimer = null
      if (pointerType === 'touch' || state !== 'closed' || openTimer !== null) return
      openTimer = setTimeout(() => {
        openTimer = null
        if (state === 'closed') set('hover')
      }, openDelay)
    },
    pointerLeave() {
      if (openTimer !== null) clearTimeout(openTimer)
      openTimer = null
      if (state !== 'hover') return
      if (closeTimer !== null) clearTimeout(closeTimer)
      closeTimer = setTimeout(() => {
        closeTimer = null
        if (state === 'hover') set('closed')
      }, closeDelay)
    },
    focusIn(keyboard) {
      if (state === 'closed' && keyboard) {
        clearTimers()
        set('focus')
      }
    },
    focusOut() {
      if (state === 'focus' || state === 'pinned') close()
    },
    click() {
      if (state === 'pinned') {
        close()
        return
      }
      clearTimers()
      set('pinned')
    },
    openPinned() {
      clearTimers()
      set('pinned')
    },
    close,
    destroy: clearTimers,
  }
}
