/**
 * useScrollMemory - each page keeps its scroll position.
 *
 * `save(key, el)` notes where `el` is scrolled for `key`; `restore(key, el)`
 * puts it back there (the top for a key never saved). A page whose content
 * arrives later (fetched on activation, rendered in pieces) is not yet tall
 * enough to be scrolled that far, so the position is held and applied again
 * whenever the scroller or its content changes size, until it is reached.
 * The user takes over at once: a wheel, a touch, a key or a press on the
 * scroller ends the restore, and so do a later `restore`, `cancel()` and
 * `timeout` ms passing without reaching the position.
 *
 * The element can stay the same between keys (a container whose content is
 * swapped) or be a new one each time.
 */

import { getCurrentScope, onScopeDispose } from 'vue'

export interface UseScrollMemoryOptions {
  /** Longest a position is waited for while the content grows, in ms. Default 10000. */
  timeout?: number
}

export interface UseScrollMemoryReturn {
  /** Note `el`'s scroll position under `key`. */
  save: (key: string, el: HTMLElement) => void
  /** Scroll `el` to the position saved under `key` (the top when none), now and as content arrives. */
  restore: (key: string, el: HTMLElement) => void
  /** The position saved under `key`, if any. */
  saved: (key: string) => number | undefined
  /** Drop what was saved under `key`. */
  forget: (key: string) => void
  /** Stop a restore in progress. */
  cancel: () => void
}

/** Inputs that mean the user is scrolling the page themselves. */
const USER_INPUTS = ['wheel', 'touchstart', 'keydown', 'pointerdown'] as const

export function useScrollMemory(options: UseScrollMemoryOptions = {}): UseScrollMemoryReturn {
  const timeout = options.timeout ?? 10000
  const positions = new Map<string, number>()
  let stopPending: (() => void) | null = null

  function cancel(): void {
    stopPending?.()
    stopPending = null
  }

  function save(key: string, el: HTMLElement): void {
    positions.set(key, el.scrollTop)
  }

  function restore(key: string, el: HTMLElement): void {
    cancel()
    const target = positions.get(key) ?? 0
    el.scrollTop = target
    if (target <= 0 || reached(el, target)) return

    let resizeObserver: ResizeObserver | null = null
    let mutationObserver: MutationObserver | null = null
    let timer: ReturnType<typeof setTimeout> | null = null

    const stop = (): void => {
      resizeObserver?.disconnect()
      mutationObserver?.disconnect()
      if (timer != null) clearTimeout(timer)
      for (const type of USER_INPUTS) el.removeEventListener(type, onInput, true)
      if (stopPending === stop) stopPending = null
    }
    function onInput(): void {
      stop()
    }
    const apply = (): void => {
      el.scrollTop = target
      if (reached(el, target)) stop()
    }

    for (const type of USER_INPUTS) el.addEventListener(type, onInput, { capture: true, passive: true })
    if (typeof ResizeObserver !== 'undefined') {
      resizeObserver = new ResizeObserver(apply)
      resizeObserver.observe(el)
      for (const child of Array.from(el.children)) resizeObserver.observe(child)
    }
    if (typeof MutationObserver !== 'undefined') {
      mutationObserver = new MutationObserver(() => {
        if (resizeObserver) for (const child of Array.from(el.children)) resizeObserver.observe(child)
        apply()
      })
      mutationObserver.observe(el, { childList: true, subtree: true, characterData: true })
    }
    timer = setTimeout(stop, timeout)
    stopPending = stop
  }

  if (getCurrentScope()) onScopeDispose(cancel)

  return {
    save,
    restore,
    saved: (key) => positions.get(key),
    forget: (key) => {
      positions.delete(key)
    },
    cancel,
  }
}

function reached(el: HTMLElement, target: number): boolean {
  return Math.abs(el.scrollTop - target) < 1
}
