import { ref, type Ref } from 'vue'
import type { AdminDesktopWindow } from '../stores/useAdminDesktopStore'
import { MIN_WINDOW_SIZE, clampWindowPosition } from './window-sizing'

/** The eight resize grips, named by the edge/corner they live on. */
export type ResizeDirection = 'n' | 's' | 'e' | 'w' | 'ne' | 'nw' | 'se' | 'sw'

export const RESIZE_DIRECTIONS: ResizeDirection[] = ['n', 's', 'e', 'w', 'ne', 'nw', 'se', 'sw']

/** CSS cursor for each grip. */
export const RESIZE_CURSORS: Record<ResizeDirection, string> = {
  n: 'ns-resize',
  s: 'ns-resize',
  e: 'ew-resize',
  w: 'ew-resize',
  ne: 'nesw-resize',
  sw: 'nesw-resize',
  nw: 'nwse-resize',
  se: 'nwse-resize',
}

type Geometry = Pick<AdminDesktopWindow, 'x' | 'y' | 'width' | 'height'>

export interface UseWindowGeometryOptions {
  /** Current geometry of the window being manipulated. */
  current: () => Geometry
  /** Size of the desktop surface, used to keep the window reachable. */
  surface: () => { width: number; height: number }
  /** Called on every pointer move with the new geometry. */
  onChange: (geometry: Geometry) => void
  /** Called once when a gesture starts - the window should come to the front. */
  onStart?: () => void
  /** Called once when a gesture ends. */
  onEnd?: () => void
}

export interface UseWindowGeometryReturn {
  /** True for the duration of a drag or resize. Callers suppress CSS
   *  transitions while it is on, otherwise the window lags behind the cursor
   *  instead of tracking it 1:1. */
  active: Ref<boolean>
  startDrag: (e: MouseEvent) => void
  startResize: (e: MouseEvent, direction: ResizeDirection) => void
}

/**
 * Mouse-driven move and eight-way resize for a desktop window.
 *
 * The gesture shape follows `components/chat/TChatWindow.vue` (mousedown →
 * listeners on `window` → mouseup tears them down, with `user-select` pinned
 * off for the duration so dragging across the page does not select text). What
 * differs is the positioning model: that window is a translate offset from a
 * modal's centred position, this one owns absolute `x/y/w/h` on the desktop
 * surface - so a resize can move the origin, which a translate offset cannot
 * express.
 */
export function useWindowGeometry(options: UseWindowGeometryOptions): UseWindowGeometryReturn {
  const active = ref(false)

  let origin = { pointerX: 0, pointerY: 0, x: 0, y: 0, width: 0, height: 0 }

  function beginGesture(e: MouseEvent, move: (ev: MouseEvent) => void): void {
    const start = options.current()
    origin = {
      pointerX: e.clientX,
      pointerY: e.clientY,
      x: start.x,
      y: start.y,
      width: start.width,
      height: start.height,
    }
    active.value = true
    options.onStart?.()
    document.body.style.userSelect = 'none'

    const end = (): void => {
      active.value = false
      window.removeEventListener('mousemove', move)
      window.removeEventListener('mouseup', end)
      document.body.style.userSelect = ''
      options.onEnd?.()
    }
    window.addEventListener('mousemove', move)
    window.addEventListener('mouseup', end)
  }

  function onDragMove(e: MouseEvent): void {
    const dx = e.clientX - origin.pointerX
    const dy = e.clientY - origin.pointerY
    // Bounded by the SAME rule the surface-resize rescue uses, so a drag can
    // never leave a window in a state the rescue would have to undo. A window
    // dragged fully off-canvas has no affordance left to drag it back.
    const next = {
      x: origin.x + dx,
      y: origin.y + dy,
      width: origin.width,
      height: origin.height,
    }
    options.onChange({ ...next, ...clampWindowPosition(next, options.surface()) })
  }

  function startDrag(e: MouseEvent): void {
    beginGesture(e, onDragMove)
  }

  function startResize(e: MouseEvent, direction: ResizeDirection): void {
    // A grip must not also start a drag (the north grip sits on the title bar)
    // and must not bubble out to the surface's "click empty space to blur".
    e.stopPropagation()

    const move = (ev: MouseEvent): void => {
      const dx = ev.clientX - origin.pointerX
      const dy = ev.clientY - origin.pointerY
      let { x, y, width, height } = origin

      if (direction.includes('e')) {
        width = origin.width + dx
      }
      if (direction.includes('w')) {
        // Clamping width alone would let the origin keep sliding right after
        // the window bottoms out, so derive x from the clamped width instead.
        width = Math.max(origin.width - dx, MIN_WINDOW_SIZE.width)
        x = origin.x + (origin.width - width)
      }
      if (direction.includes('s')) {
        height = origin.height + dy
      }
      if (direction.includes('n')) {
        height = Math.max(origin.height - dy, MIN_WINDOW_SIZE.height)
        y = origin.y + (origin.height - height)
      }

      // Capped at the surface as well as the minimum: a window bigger than the
      // desktop it lives on cannot be shown, and its far edge - grips included
      // - would be unreachable.
      const surface = options.surface()
      const maxWidth = surface.width > 0 ? Math.max(surface.width, MIN_WINDOW_SIZE.width) : Infinity
      const maxHeight = surface.height > 0 ? Math.max(surface.height, MIN_WINDOW_SIZE.height) : Infinity
      options.onChange({
        x: Math.max(x, 0),
        y: Math.max(y, 0),
        width: Math.min(Math.max(width, MIN_WINDOW_SIZE.width), maxWidth),
        height: Math.min(Math.max(height, MIN_WINDOW_SIZE.height), maxHeight),
      })
    }

    beginGesture(e, move)
  }

  return { active, startDrag, startResize }
}
