/**
 * Tooltip - the label of an icon-only control, shown beside it.
 *
 * What the browser's `title` cannot do, and the reason this exists: `title`
 * appears after a delay the page does not control (about a second and a half
 * in Chromium), never on keyboard focus, in the system's style, and not at all
 * on many touch browsers. This one:
 *
 * - opens `TOOLTIP_DELAY_MS` (350 ms) after the pointer rests on the control,
 *   and at once when another tooltip closed less than `TOOLTIP_SKIP_DELAY_MS`
 *   ago, so running the pointer down a rail reads every label without waiting
 *   again at each icon;
 * - opens at once on keyboard focus, not on the focus a click or a tap gives
 *   (`input-modality.ts` tells them apart);
 * - on a touch screen there is no hover, so it opens on a long press instead
 *   (`TOOLTIP_LONG_PRESS_MS`, 500 ms, held without moving), as a phone shows
 *   the label of a toolbar icon: it stays `TOOLTIP_TOUCH_SHOW_MS` (1.5 s)
 *   after the finger lifts, and that press does not also activate the
 *   control (its click and the system's long-press menu are swallowed). A
 *   short tap shows nothing and acts as before. `touch: 'none'` turns this
 *   off for one control (one whose long press means something else);
 * - closes on a press, on leave, on blur, on scroll and on Escape (WCAG
 *   1.4.13: dismissible without moving focus);
 * - is drawn `position: fixed` on `<body>`, on the tooltip layer
 *   (`--tnzi-ai-z-tooltip`), so an `overflow: hidden` rail or sidebar cannot
 *   clip it; it goes on the requested side, else the side the nearest
 *   ancestor names in `data-tooltip-side` (a whole sidebar whose tooltips
 *   should open to the left says it once), else `bottom`, centred under the
 *   control (a rail's buttons ask for `right`); it flips when there is no room.
 *
 * The bubble is decoration: it is `aria-hidden` and the control's accessible
 * name is left as it is. Give the control its own `aria-label`; only a control
 * with no name at all (no `aria-label` / `aria-labelledby`, no text) is given
 * the tooltip's text as its `aria-label`, so an icon button is never left
 * unnamed. One bubble is shared by the whole page, because only one tooltip is
 * ever showing. Its styles are the global `.t-tooltip` rules in
 * `components/overlay/tooltip.css`, part of the package stylesheet.
 */
import type { Directive } from 'vue'
import { placeBeside, type FloatingSide } from './place-beside'
import { inputModality, trackInputModality } from './input-modality'

/** Hover delay before a tooltip opens. */
export const TOOLTIP_DELAY_MS = 350
/** A tooltip opening this soon after another closed skips the delay. */
export const TOOLTIP_SKIP_DELAY_MS = 300
/** How long a finger rests on a control before its tooltip opens. */
export const TOOLTIP_LONG_PRESS_MS = 500
/** How long a tooltip opened by a long press stays after the finger lifts. */
export const TOOLTIP_TOUCH_SHOW_MS = 1500
/** How far (px) the finger may move during the press before it is a scroll, not a long press. */
const LONG_PRESS_SLOP = 10
/** A click this soon after a long press opened the tooltip is the end of that press, not an action. */
const SWALLOW_CLICK_MS = 600
/** Class of the shared bubble element. */
export const TOOLTIP_CLASS = 't-tooltip'
/**
 * The attribute that sets the side of every tooltip inside an element that
 * does not ask for one itself: `<aside data-tooltip-side="left">`. The nearest
 * one wins. A DOM attribute rather than a provided value, so it reaches the
 * directive on plain elements and on any component inside, with no setup.
 */
export const TOOLTIP_SIDE_ATTRIBUTE = 'data-tooltip-side'
const DEFAULT_SIDE: FloatingSide = 'bottom'
const SIDES: readonly FloatingSide[] = ['top', 'right', 'bottom', 'left']

/** The side a tooltip opens on: its own, else the nearest `data-tooltip-side`, else `bottom`. */
export function tooltipSide(anchor: Element, own?: FloatingSide): FloatingSide {
  if (own) return own
  const named = anchor.closest(`[${TOOLTIP_SIDE_ATTRIBUTE}]`)?.getAttribute(TOOLTIP_SIDE_ATTRIBUTE)
  return SIDES.find((side) => side === named) ?? DEFAULT_SIDE
}

export interface TooltipOptions {
  /** The label. An empty text shows nothing. */
  readonly text: string
  /** Side of the control it opens on. Unset: the nearest `data-tooltip-side`, else `bottom`; a rail's buttons pass `right`. */
  readonly side?: FloatingSide
  /** Hover delay in ms. Default `TOOLTIP_DELAY_MS`. */
  readonly delay?: number
  /** Show nothing (while the control's own menu is open, for one). */
  readonly disabled?: boolean
  /** On a touch screen: `long-press` (default) opens it on a long press; `none` never does. */
  readonly touch?: 'long-press' | 'none'
  /**
   * A selector for an element inside the control whose text may be cut off
   * (an ellipsis): the tooltip opens only while that text does not fit, to show
   * it whole. A control whose text fits shows none.
   */
  readonly whenTruncated?: string
}

/** Whether the one-line text of `element` is cut off: it is wider than its box. */
export function isTruncated(element: Element | null): boolean {
  return element instanceof HTMLElement && element.scrollWidth > element.clientWidth
}

/** What `v-tooltip` and `createTooltip` accept: the text alone, or options. Empty values show nothing. */
export type TooltipValue = string | TooltipOptions | null | undefined | false

export interface TooltipController {
  /** Takes new text or options; a tooltip showing follows them at once. */
  update(value: TooltipValue): void
  /** Shows it now, without the delay. */
  show(): void
  hide(): void
  /** Whether this control's tooltip is the one showing. */
  readonly shown: boolean
  /** Removes listeners, hides the tooltip and restores the control's attributes. */
  destroy(): void
}

export function normalizeTooltip(value: TooltipValue): TooltipOptions | null {
  if (!value) return null
  const options = typeof value === 'string' ? { text: value } : value
  return options.text.trim() === '' ? null : options
}

/* ── The shared bubble ─────────────────────────────────────────────────── */

let bubble: HTMLDivElement | null = null
let owner: object | null = null
let lastHiddenAt = Number.NEGATIVE_INFINITY

function ensureBubble(): HTMLDivElement {
  if (bubble?.isConnected) return bubble
  const element = document.createElement('div')
  element.className = TOOLTIP_CLASS
  element.setAttribute('aria-hidden', 'true')
  document.body.appendChild(element)
  bubble = element
  return element
}

function placeBubble(element: HTMLDivElement, anchor: HTMLElement, side: FloatingSide): void {
  element.style.top = '0px'
  element.style.left = '0px'
  const size = element.getBoundingClientRect()
  const box = anchor.getBoundingClientRect()
  const position = placeBeside(
    { top: box.top, left: box.left, width: box.width, height: box.height },
    { width: size.width, height: size.height },
    { width: window.innerWidth, height: window.innerHeight },
    { side, gap: 8, edge: 8 },
  )
  element.style.top = `${position.top}px`
  element.style.left = `${position.left}px`
  element.dataset.side = position.side
}

/* ── One control ───────────────────────────────────────────────────────── */

export function createTooltip(anchor: HTMLElement, value: TooltipValue): TooltipController {
  trackInputModality()
  const self = {}
  let options = normalizeTooltip(value)
  let timer: ReturnType<typeof setTimeout> | null = null
  /* Pressed since the pointer came in: the press dismissed the tooltip and it
     stays away until the pointer leaves, as a native tooltip does. */
  let pressed = false
  let labelled = false
  /* A long press on a touch screen: its timer, where the finger came down,
     whether it opened the tooltip, and the timer that closes it after the lift. */
  let pressTimer: ReturnType<typeof setTimeout> | null = null
  let pressStart: { x: number; y: number } | null = null
  let touchShown = false
  /* When the finger that opened the tooltip lifted: the click that follows is that press's. */
  let liftedAt = 0
  let touchHideTimer: ReturnType<typeof setTimeout> | null = null

  const isShown = (): boolean => owner === self

  function cancel(): void {
    if (timer !== null) clearTimeout(timer)
    timer = null
  }

  function syncLabel(): void {
    const unnamed = !anchor.hasAttribute('aria-labelledby')
      && (!anchor.hasAttribute('aria-label') || labelled)
      && (anchor.textContent ?? '').trim() === ''
    if (unnamed && options) {
      anchor.setAttribute('aria-label', options.text)
      labelled = true
    } else if (labelled && !options) {
      anchor.removeAttribute('aria-label')
      labelled = false
    }
  }

  function show(): void {
    cancel()
    if (!options || options.disabled || typeof document === 'undefined') return
    if (options.whenTruncated && !isTruncated(anchor.querySelector(options.whenTruncated))) return
    const element = ensureBubble()
    owner = self
    element.textContent = options.text
    placeBubble(element, anchor, tooltipSide(anchor, options.side))
    element.dataset.open = ''
    document.addEventListener('keydown', onKeydown, true)
    window.addEventListener('scroll', hide, true)
  }

  function hide(): void {
    cancel()
    cancelPress()
    if (touchHideTimer !== null) clearTimeout(touchHideTimer)
    touchHideTimer = null
    touchShown = false
    if (!isShown()) return
    owner = null
    lastHiddenAt = Date.now()
    if (bubble) delete bubble.dataset.open
    document.removeEventListener('keydown', onKeydown, true)
    window.removeEventListener('scroll', hide, true)
  }

  function schedule(): void {
    cancel()
    if (!options || options.disabled) return
    if (Date.now() - lastHiddenAt < TOOLTIP_SKIP_DELAY_MS || owner !== null) {
      show()
      return
    }
    timer = setTimeout(show, options.delay ?? TOOLTIP_DELAY_MS)
  }

  function onEnter(event: PointerEvent): void {
    if (event.pointerType === 'touch' || pressed) return
    schedule()
  }
  function onLeave(event: PointerEvent): void {
    pressed = false
    /* A finger "leaves" as it lifts: a tooltip it opened stays for its time. */
    if (event.pointerType === 'touch' && touchShown) return
    hide()
  }
  function onDown(event: PointerEvent): void {
    pressed = true
    liftedAt = 0
    hide()
    if (event.pointerType !== 'touch' || !options || options.disabled || options.touch === 'none') return
    pressStart = { x: event.clientX, y: event.clientY }
    pressTimer = setTimeout(() => {
      pressTimer = null
      show()
      if (!isShown()) return
      touchShown = true
    }, TOOLTIP_LONG_PRESS_MS)
  }
  function cancelPress(): void {
    if (pressTimer !== null) clearTimeout(pressTimer)
    pressTimer = null
    pressStart = null
  }
  function onMove(event: PointerEvent): void {
    if (!pressStart || event.pointerType !== 'touch') return
    if (Math.hypot(event.clientX - pressStart.x, event.clientY - pressStart.y) > LONG_PRESS_SLOP) cancelPress()
  }
  function onUp(event: PointerEvent): void {
    if (event.pointerType !== 'touch') return
    cancelPress()
    if (!touchShown) return
    liftedAt = Date.now()
    if (touchHideTimer !== null) clearTimeout(touchHideTimer)
    touchHideTimer = setTimeout(hide, TOOLTIP_TOUCH_SHOW_MS)
  }
  /* The click that ends a long press, and the system menu a long press opens,
     are not the control's: the press was for its label. */
  function onClick(event: MouseEvent): void {
    if (!liftedAt || Date.now() - liftedAt > SWALLOW_CLICK_MS) return
    liftedAt = 0
    event.preventDefault()
    event.stopImmediatePropagation()
  }
  function onContextMenu(event: Event): void {
    if (pressStart || touchShown) event.preventDefault()
  }
  function onFocus(): void {
    if (inputModality() === 'keyboard') show()
  }
  function onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') hide()
  }

  anchor.addEventListener('pointerenter', onEnter)
  anchor.addEventListener('pointerleave', onLeave)
  anchor.addEventListener('pointerdown', onDown)
  anchor.addEventListener('pointermove', onMove)
  anchor.addEventListener('pointerup', onUp)
  anchor.addEventListener('pointercancel', cancelPress)
  anchor.addEventListener('click', onClick, true)
  anchor.addEventListener('contextmenu', onContextMenu)
  anchor.addEventListener('focus', onFocus)
  anchor.addEventListener('blur', hide)
  syncLabel()

  return {
    update(next) {
      options = normalizeTooltip(next)
      syncLabel()
      if (!isShown()) return
      if (!options || options.disabled) hide()
      else show()
    },
    show,
    hide,
    get shown() {
      return isShown()
    },
    destroy() {
      hide()
      anchor.removeEventListener('pointerenter', onEnter)
      anchor.removeEventListener('pointerleave', onLeave)
      anchor.removeEventListener('pointerdown', onDown)
      anchor.removeEventListener('pointermove', onMove)
      anchor.removeEventListener('pointerup', onUp)
      anchor.removeEventListener('pointercancel', cancelPress)
      anchor.removeEventListener('click', onClick, true)
      anchor.removeEventListener('contextmenu', onContextMenu)
      anchor.removeEventListener('focus', onFocus)
      anchor.removeEventListener('blur', hide)
      if (labelled) anchor.removeAttribute('aria-label')
    },
  }
}

const controllers = new WeakMap<HTMLElement, TooltipController>()

/**
 * `v-tooltip` on an icon-only control: the text, or `{ text, side, delay, disabled, touch, whenTruncated }`.
 *
 * ```vue
 * <button v-tooltip="'Settings'" aria-label="Settings"><Icon icon="lucide:settings-2" /></button>
 * <button v-tooltip="{ text: 'Search', side: 'bottom' }" aria-label="Search">...</button>
 * ```
 */
export const vTooltip: Directive<HTMLElement, TooltipValue> = {
  mounted(el, binding) {
    controllers.set(el, createTooltip(el, binding.value))
  },
  updated(el, binding) {
    if (binding.value !== binding.oldValue) controllers.get(el)?.update(binding.value)
  },
  beforeUnmount(el) {
    controllers.get(el)?.destroy()
    controllers.delete(el)
  },
}
