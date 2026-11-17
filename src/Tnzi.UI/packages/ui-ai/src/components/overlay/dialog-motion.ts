/**
 * How `TDialog` moves when it opens from an element and closes back into it:
 * a FLIP of the panel (laid out where it ends, drawn from the origin's box
 * back to its own), its contents fading in once it is nearly there and out
 * first on the way back so the scaling never shows on text, and the backdrop
 * dimming in step. With reduced motion the dialog only fades. Without an
 * origin to grow from (the card was deleted, scrolled away) it falls back to
 * the dialog's own fade and rise.
 *
 * Runs on the Web Animations API; where it is missing nothing animates and the
 * dialog simply appears and goes.
 */

/** A box on screen to open from: its untransformed layout box and its tilt. */
export interface DialogOriginBox {
  readonly left: number
  readonly top: number
  readonly width: number
  readonly height: number
  /** Degrees, clockwise, around the box's centre. */
  readonly rotate?: number
}

/** An element (its box and tilt are measured), an element with a tilt of its own, or a box. */
export type DialogOrigin = Element | { readonly element: Element; readonly rotate?: number } | DialogOriginBox

/** The origin, or a function that finds it (asked on open and again on close). */
export type DialogOriginSource = DialogOrigin | null | undefined | (() => DialogOrigin | null | undefined)

/** How a dialog was asked to close. */
/** What asked to close: `back` is the phone's back button or gesture on the sheet / full screen. */
export type DialogCloseReason = 'escape' | 'backdrop' | 'close-button' | 'back' | 'programmatic'

export const ORIGIN_OPEN_MS = 340
export const ORIGIN_CLOSE_MS = 280
export const FADE_MS = 150
export const FALLBACK_MS = 200
const OPEN_EASING = 'cubic-bezier(0.2, 0.85, 0.25, 1)'
const CLOSE_EASING = 'cubic-bezier(0.45, 0, 0.6, 1)'

/** The rotation in a computed `transform` (`matrix(a, b, ...)`), in degrees. */
export function rotationOf(transform: string | null | undefined): number {
  const match = /^matrix(3d)?\(([^)]+)\)$/.exec((transform ?? '').trim())
  if (!match) return 0
  const values = match[2]!.split(',').map(Number)
  const [a, b] = match[1] ? [values[0]!, values[1]!] : [values[0]!, values[1]!]
  if (!Number.isFinite(a) || !Number.isFinite(b)) return 0
  return Math.round((Math.atan2(b, a) * 180) / Math.PI * 100) / 100
}

function isBox(value: unknown): value is DialogOriginBox {
  return typeof value === 'object' && value !== null && 'width' in value && 'height' in value && 'left' in value && 'top' in value
}

/**
 * The origin as a box, or `null` when there is nothing on screen to grow from:
 * an element that is gone, has no size, or lies outside the viewport. A tilted
 * element's bounding box is larger than the element, so only its centre comes
 * from there; its size is its layout size and its tilt is read from its
 * computed transform unless one is given.
 */
export function resolveOrigin(source: DialogOriginSource, viewport: { width: number; height: number }): DialogOriginBox | null {
  const origin = typeof source === 'function' ? source() : source
  if (!origin) return null
  if (isBox(origin)) return origin.width > 0 && origin.height > 0 ? origin : null
  const element = 'element' in origin ? origin.element : origin
  const given = 'element' in origin ? origin.rotate : undefined
  if (!element.isConnected) return null
  const rect = element.getBoundingClientRect()
  if (rect.width === 0 || rect.height === 0) return null
  if (rect.bottom < 0 || rect.top > viewport.height || rect.right < 0 || rect.left > viewport.width) return null
  const html = element as HTMLElement
  const width = html.offsetWidth || rect.width
  const height = html.offsetHeight || rect.height
  const cx = rect.left + rect.width / 2
  const cy = rect.top + rect.height / 2
  const rotate = given ?? rotationOf(element.ownerDocument.defaultView?.getComputedStyle(element).transform)
  return { left: cx - width / 2, top: cy - height / 2, width, height, rotate }
}

function round(value: number, digits = 2): number {
  const factor = 10 ** digits
  return Math.round(value * factor) / factor
}

/** The transform that draws a panel laid out at `to` over the box `from` (transform-origin at its centre). */
export function originTransform(from: DialogOriginBox, to: { left: number; top: number; width: number; height: number }): string {
  const dx = from.left + from.width / 2 - (to.left + to.width / 2)
  const dy = from.top + from.height / 2 - (to.top + to.height / 2)
  const sx = to.width > 0 ? from.width / to.width : 1
  const sy = to.height > 0 ? from.height / to.height : 1
  return `translate(${round(dx)}px, ${round(dy)}px) rotate(${from.rotate ?? 0}deg) scale(${round(sx, 4)}, ${round(sy, 4)})`
}

export function prefersReducedMotion(): boolean {
  return typeof window !== 'undefined' && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches === true
}

function canAnimate(element: Element | null | undefined): element is HTMLElement {
  return !!element && typeof (element as HTMLElement).animate === 'function'
}

async function settled(animations: Animation[]): Promise<void> {
  await Promise.allSettled(animations.map((animation) => animation.finished))
}

/* The dimming behind the panel, from none to its own colour and blur. */
function backdropFrames(host: HTMLElement, showing: boolean): Keyframe[] {
  const style = getComputedStyle(host)
  const shown: Keyframe = { backgroundColor: style.backgroundColor, backdropFilter: style.backdropFilter }
  const hidden: Keyframe = { backgroundColor: 'rgba(0, 0, 0, 0)', backdropFilter: 'blur(0px)' }
  return showing ? [hidden, shown] : [shown, hidden]
}

/** What a dialog's enter or leave should look like. */
export type DialogMotion = { kind: 'origin'; box: DialogOriginBox } | { kind: 'fade' } | { kind: 'rise' }

/** Origin when there is one, a plain fade with reduced motion, else the dialog's own rise. */
export function chooseMotion(box: DialogOriginBox | null, reduced: boolean): DialogMotion {
  if (reduced) return { kind: 'fade' }
  return box ? { kind: 'origin', box } : { kind: 'rise' }
}

/** Plays the enter of `host` (the backdrop) and `panel` (laid out where it opens). */
export async function playEnter(host: HTMLElement, panel: HTMLElement, motion: DialogMotion): Promise<void> {
  if (!canAnimate(host) || !canAnimate(panel)) return
  if (motion.kind === 'fade') {
    await settled([host.animate([{ opacity: 0 }, { opacity: 1 }], { duration: FADE_MS, easing: 'linear' })])
    return
  }
  if (motion.kind === 'rise') {
    await settled([
      host.animate([{ opacity: 0 }, { opacity: 1 }], { duration: FALLBACK_MS, easing: 'ease' }),
      panel.animate([{ transform: 'translateY(12px) scale(0.98)' }, { transform: 'none' }], { duration: FALLBACK_MS, easing: 'ease' }),
    ])
    return
  }
  const start = originTransform(motion.box, panel.getBoundingClientRect())
  await settled([
    panel.animate([{ transform: start, boxShadow: 'none' }, { transform: 'none' }], { duration: ORIGIN_OPEN_MS, easing: OPEN_EASING }),
    ...Array.from(panel.children, (part) =>
      part.animate([{ opacity: 0 }, { opacity: 0, offset: 0.45 }, { opacity: 1 }], { duration: ORIGIN_OPEN_MS, easing: 'linear' }),
    ),
    host.animate(backdropFrames(host, true), { duration: ORIGIN_OPEN_MS, easing: 'ease-out' }),
  ])
}

/** Plays the leave; the dialog keeps its last frame until it is removed. */
export async function playLeave(host: HTMLElement, panel: HTMLElement, motion: DialogMotion): Promise<void> {
  if (!canAnimate(host) || !canAnimate(panel)) return
  const hold: KeyframeAnimationOptions = { fill: 'forwards' }
  if (motion.kind === 'fade') {
    await settled([host.animate([{ opacity: 1 }, { opacity: 0 }], { ...hold, duration: FADE_MS, easing: 'linear' })])
    return
  }
  if (motion.kind === 'rise') {
    await settled([
      host.animate([{ opacity: 1 }, { opacity: 0 }], { ...hold, duration: FALLBACK_MS, easing: 'ease' }),
      panel.animate([{ transform: 'none' }, { transform: 'translateY(12px) scale(0.98)' }], { ...hold, duration: FALLBACK_MS, easing: 'ease' }),
    ])
    return
  }
  const end = originTransform(motion.box, panel.getBoundingClientRect())
  const options: KeyframeAnimationOptions = { ...hold, duration: ORIGIN_CLOSE_MS, easing: CLOSE_EASING }
  await settled([
    panel.animate([{ transform: 'none' }, { transform: end, boxShadow: 'none' }], options),
    ...Array.from(panel.children, (part) =>
      part.animate([{ opacity: 1 }, { opacity: 0, offset: 0.35 }, { opacity: 0 }], { ...options, easing: 'linear' }),
    ),
    host.animate(backdropFrames(host, false), options),
  ])
}
