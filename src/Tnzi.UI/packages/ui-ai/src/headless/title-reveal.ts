/**
 * Title reveal - a truncated row title scrolls sideways under the pointer so
 * its end can be read, then snaps back when the pointer leaves.
 *
 * The title element keeps `overflow: hidden` and its ellipsis at rest; this
 * module only drives `scrollLeft` (which works on an `overflow: hidden` box)
 * and two attributes that the stylesheet turns into edge fades:
 *
 * - `data-scrolled` while `scrollLeft > 0`: the start of the text has gone,
 *   so the left edge fades;
 * - `data-clipped` while `scrollLeft < range`: there is still text past the
 *   right edge, so the right edge fades.
 *
 * Either attribute also switches the title from ellipsis to clip, so the "..."
 * never sits on top of the moving text. Both are cleared on leave, which
 * restores the ellipsis in the same frame as the snap back to 0. Returning
 * instantly is deliberate: animating back would show the ellipsis colliding
 * with text that is still on its way home.
 *
 * Choices that shape how the motion starts and ends:
 *
 * - A short delay before anything moves, so a pointer sweeping down the list
 *   does not set every row it crosses in motion. Measurement happens when the
 *   delay ends, by which point hover-only row actions have appeared, so the
 *   range is the width actually left for the title.
 * - A constant speed rather than a fixed duration: a long title takes longer,
 *   instead of a short one jumping and a long one crawling.
 * - The scroll stops at the end. No loop, no ping-pong: once the end is read
 *   there is nothing more to show, and continued motion is noise.
 * - Touch pointers are ignored (there is no hover to end it), and reduced
 *   motion jumps straight to the end.
 * - A `hold` (the row's context menu is open) keeps the reveal after the
 *   pointer leaves; it ends when the hold is released.
 *
 * `createTitleReveal` is the plain-DOM controller; `vTitleReveal` binds it to
 * an element in a template. The hover target is the nearest ancestor marked
 * `data-title-reveal-host` (the whole row, so hovering its "..." button counts
 * too), or the title element itself when there is none.
 */
import type { Directive } from 'vue'

/** Wait before the scroll starts, so a passing pointer does nothing. */
export const TITLE_REVEAL_DELAY_MS = 300
/** Scroll speed: 30px per second. */
export const TITLE_REVEAL_SPEED_PX_PER_MS = 0.03
/** Overflow at or below this is not worth moving for. */
export const TITLE_REVEAL_MIN_RANGE_PX = 8
/** Marks the element whose hover drives the reveal. */
export const TITLE_REVEAL_HOST_ATTR = 'data-title-reveal-host'

export interface TitleRevealOptions {
  readonly delayMs?: number
  readonly speedPxPerMs?: number
  readonly minRangePx?: number
  /** Defaults to the `prefers-reduced-motion: reduce` media query. */
  readonly prefersReducedMotion?: () => boolean
}

export interface TitleRevealController {
  /** While held, leaving the host does not end the reveal (an open menu). */
  setHold(hold: boolean): void
  /** Removes listeners, cancels pending work and restores the title. */
  destroy(): void
}

function systemPrefersReducedMotion(): boolean {
  return typeof window !== 'undefined'
    && typeof window.matchMedia === 'function'
    && window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

export function createTitleReveal(title: HTMLElement, host: HTMLElement = title, options: TitleRevealOptions = {}): TitleRevealController {
  const delayMs = options.delayMs ?? TITLE_REVEAL_DELAY_MS
  const speed = options.speedPxPerMs ?? TITLE_REVEAL_SPEED_PX_PER_MS
  const minRange = options.minRangePx ?? TITLE_REVEAL_MIN_RANGE_PX
  const reducedMotion = options.prefersReducedMotion ?? systemPrefersReducedMotion

  let hovering = false
  let held = false
  let timer: ReturnType<typeof setTimeout> | null = null
  let frame: number | null = null

  function markPosition(position: number, range: number): void {
    title.toggleAttribute('data-scrolled', position > 0)
    title.toggleAttribute('data-clipped', position < range)
  }

  function cancelPending(): void {
    if (timer !== null) {
      clearTimeout(timer)
      timer = null
    }
    if (frame !== null) {
      cancelAnimationFrame(frame)
      frame = null
    }
  }

  function reset(): void {
    cancelPending()
    title.scrollLeft = 0
    title.removeAttribute('data-scrolled')
    title.removeAttribute('data-clipped')
  }

  function animate(range: number): void {
    let startTime: number | null = null
    const step = (now: number): void => {
      startTime ??= now
      const position = Math.min(range, (now - startTime) * speed)
      title.scrollLeft = position
      markPosition(position, range)
      frame = position < range ? requestAnimationFrame(step) : null
    }
    frame = requestAnimationFrame(step)
  }

  function start(): void {
    timer = null
    const range = title.scrollWidth - title.clientWidth
    if (range <= minRange) return
    if (reducedMotion()) {
      title.scrollLeft = range
      markPosition(range, range)
      return
    }
    markPosition(0, range)
    animate(range)
  }

  function onEnter(event: PointerEvent): void {
    if (event.pointerType === 'touch') return
    hovering = true
    // Already revealing (held through a menu, then re-entered): carry on.
    if (timer !== null || frame !== null || title.scrollLeft > 0) return
    timer = setTimeout(start, delayMs)
  }

  function onLeave(event: PointerEvent): void {
    if (event.pointerType === 'touch') return
    hovering = false
    if (!held) reset()
  }

  // The stylesheet's hook (`[data-title-reveal][data-scrolled]` and so on).
  title.setAttribute('data-title-reveal', '')
  host.addEventListener('pointerenter', onEnter)
  host.addEventListener('pointerleave', onLeave)

  return {
    setHold(hold: boolean): void {
      if (held === hold) return
      held = hold
      if (!hold && !hovering) reset()
    },
    destroy(): void {
      host.removeEventListener('pointerenter', onEnter)
      host.removeEventListener('pointerleave', onLeave)
      reset()
      title.removeAttribute('data-title-reveal')
    },
  }
}

const controllers = new WeakMap<HTMLElement, TitleRevealController>()

/**
 * `v-title-reveal` on the title element; the value is the hold flag.
 *
 * ```vue
 * <div class="row" data-title-reveal-host>
 *   <span v-title-reveal="menuOpen" class="row__title">{{ title }}</span>
 * </div>
 * ```
 */
export const vTitleReveal: Directive<HTMLElement, boolean | undefined> = {
  mounted(el, binding) {
    const host = el.closest<HTMLElement>(`[${TITLE_REVEAL_HOST_ATTR}]`) ?? el
    const controller = createTitleReveal(el, host)
    controller.setHold(Boolean(binding.value))
    controllers.set(el, controller)
  },
  updated(el, binding) {
    controllers.get(el)?.setHold(Boolean(binding.value))
  },
  beforeUnmount(el) {
    controllers.get(el)?.destroy()
    controllers.delete(el)
  },
}
