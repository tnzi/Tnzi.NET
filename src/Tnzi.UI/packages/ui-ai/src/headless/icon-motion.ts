/**
 * Icon motion - a short one-shot animation of a row's icon when the pointer
 * enters the row: an alarm clock rings, a launcher hops, a pen tilts.
 *
 * The motions move the whole icon (`transform` on the element), so they work
 * on any single-SVG icon - an Iconify / Lucide glyph cannot have its parts
 * animated separately. Transforms never take part in layout, so nothing
 * around the icon moves.
 *
 * The keyframes live in the stylesheet (`[data-icon-motion-play]
 * [data-icon-motion='ring']` and so on); this module only decides *when* one
 * plays, because CSS `:hover` cannot make the distinctions that matter:
 *
 * - once per pointer entry. Every motion starts and ends on the rest pose, so
 *   clearing the trigger when it ends is invisible, and the next entry plays it
 *   again. Re-entering while one is still playing does not restart it (a
 *   restart is a visible jump mid-motion);
 * - never for touch. A tap leaves `:hover` stuck on a touch screen, and a
 *   hybrid laptop reports `hover: hover` while its user is tapping;
 * - never under `prefers-reduced-motion: reduce` (the stylesheet drops the
 *   animations there as well).
 *
 * Keyboard focus is handled by the stylesheet alone (`:focus-visible` on the
 * host plays the motion once), since focus has none of the problems above.
 *
 * Markup: the host (the whole row) carries `data-icon-motion-host`, the icon
 * `data-icon-motion="<name>"`. `vIconMotion` on the host binds the controller.
 */
import type { Directive } from 'vue'

/** The built-in motions. Each one starts and ends on the icon's rest pose. */
export const ICON_MOTIONS = ['ring', 'hop', 'tilt', 'spin', 'pop'] as const
/**
 * - `ring`: lift and wiggle around the top centre, then settle (an alarm, a bell).
 * - `hop`: a springy hop up and to the right (a launcher, an agent, plugins).
 * - `tilt`: a rock around the bottom centre (a pen, a wand, a bulb).
 * - `spin`: one full turn that overshoots and settles (a gear, refresh).
 * - `pop`: a squash-and-bounce in place (anything else; a book, a library).
 */
export type IconMotion = (typeof ICON_MOTIONS)[number]

/** Upper bound of every motion's duration; the trigger is cleared by then even
 *  if `animationend` never arrives (the stylesheet was not loaded). */
export const ICON_MOTION_MAX_MS = 800
/** Marks the element whose hover plays the motion. */
export const ICON_MOTION_HOST_ATTR = 'data-icon-motion-host'
const PLAY_ATTR = 'data-icon-motion-play'
const ICON_SELECTOR = ICON_MOTIONS.map((m) => `[data-icon-motion='${m}']`).join(',')

export function isIconMotion(value: unknown): value is IconMotion {
  return typeof value === 'string' && (ICON_MOTIONS as readonly string[]).includes(value)
}

export interface IconMotionOptions {
  /** Defaults to the `prefers-reduced-motion: reduce` media query. */
  readonly prefersReducedMotion?: () => boolean
}

export interface IconMotionController {
  /** Removes listeners and any pending trigger. */
  destroy(): void
}

function systemPrefersReducedMotion(): boolean {
  return typeof window !== 'undefined'
    && typeof window.matchMedia === 'function'
    && window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

export function createIconMotion(host: HTMLElement, options: IconMotionOptions = {}): IconMotionController {
  const reducedMotion = options.prefersReducedMotion ?? systemPrefersReducedMotion
  let timer: ReturnType<typeof setTimeout> | null = null

  function stop(): void {
    if (timer !== null) {
      clearTimeout(timer)
      timer = null
    }
    host.removeAttribute(PLAY_ATTR)
  }

  function onEnter(event: PointerEvent): void {
    if (event.pointerType === 'touch') return
    if (host.hasAttribute(PLAY_ATTR)) return
    if (reducedMotion()) return
    if (!host.querySelector(ICON_SELECTOR)) return
    host.setAttribute(PLAY_ATTR, '')
    timer = setTimeout(stop, ICON_MOTION_MAX_MS)
  }

  function onEnd(event: AnimationEvent): void {
    if (event.animationName.startsWith('t-icon-motion-')) stop()
  }

  host.setAttribute(ICON_MOTION_HOST_ATTR, '')
  host.addEventListener('pointerenter', onEnter)
  host.addEventListener('animationend', onEnd)
  host.addEventListener('animationcancel', onEnd)

  return {
    destroy(): void {
      host.removeEventListener('pointerenter', onEnter)
      host.removeEventListener('animationend', onEnd)
      host.removeEventListener('animationcancel', onEnd)
      stop()
    },
  }
}

const controllers = new WeakMap<HTMLElement, IconMotionController>()

/**
 * `v-icon-motion` on the row; the icon inside names its motion.
 *
 * ```vue
 * <button v-icon-motion class="row">
 *   <Icon icon="lucide:clock" data-icon-motion="ring" />
 *   <span>Scheduled</span>
 * </button>
 * ```
 */
export const vIconMotion: Directive<HTMLElement> = {
  mounted(el) {
    controllers.set(el, createIconMotion(el))
  },
  beforeUnmount(el) {
    controllers.get(el)?.destroy()
    controllers.delete(el)
  },
}
