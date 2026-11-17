/**
 * The size of an element, kept current as it changes: a page that shows one,
 * two or three columns by the width of its container, not of the window.
 *
 * ```ts
 * const grid = ref<HTMLElement | null>(null)
 * const { width } = useElementSize(grid)
 * const columns = computed(() => (width.value >= 900 ? 3 : width.value >= 560 ? 2 : 1))
 * ```
 *
 * Backed by one `ResizeObserver` per call, observing whatever element the ref
 * (or getter) holds: when it changes the observer follows, when it is `null`
 * the size is 0 × 0. `box` picks what is measured: `content-box` (default,
 * the box inside the padding, what a layout of children cares about) or
 * `border-box`. Values are CSS pixels as the browser reports them (fractions
 * included). The observer is released when the calling scope ends (a
 * component's unmount). Without `ResizeObserver` (SSR, an old test
 * environment) the size is read once when the element appears and then stays.
 */
import { getCurrentScope, onScopeDispose, shallowRef, toValue, watch, type MaybeRefOrGetter, type ShallowRef } from 'vue'

export interface ElementSize {
  readonly width: Readonly<ShallowRef<number>>
  readonly height: Readonly<ShallowRef<number>>
  /** Stops observing; also done when the calling scope ends. */
  stop(): void
}

export interface UseElementSizeOptions {
  /** What is measured. Default `content-box`. */
  readonly box?: 'content-box' | 'border-box'
}

function measure(el: HTMLElement, box: 'content-box' | 'border-box'): { width: number; height: number } {
  if (box === 'border-box') return { width: el.offsetWidth, height: el.offsetHeight }
  const style = getComputedStyle(el)
  const padX = (parseFloat(style.paddingLeft) || 0) + (parseFloat(style.paddingRight) || 0)
  const padY = (parseFloat(style.paddingTop) || 0) + (parseFloat(style.paddingBottom) || 0)
  return { width: Math.max(0, el.clientWidth - padX), height: Math.max(0, el.clientHeight - padY) }
}

export function useElementSize(
  target: MaybeRefOrGetter<HTMLElement | null | undefined>,
  options: UseElementSizeOptions = {},
): ElementSize {
  const box = options.box ?? 'content-box'
  const width = shallowRef(0)
  const height = shallowRef(0)
  let observer: ResizeObserver | null = null

  function set(next: { width: number; height: number }): void {
    if (width.value !== next.width) width.value = next.width
    if (height.value !== next.height) height.value = next.height
  }

  function disconnect(): void {
    observer?.disconnect()
    observer = null
  }

  const stopWatch = watch(
    () => toValue(target),
    (el) => {
      disconnect()
      if (!el) {
        set({ width: 0, height: 0 })
        return
      }
      set(measure(el, box))
      if (typeof ResizeObserver === 'undefined') return
      observer = new ResizeObserver((entries) => {
        const entry = entries[entries.length - 1]
        const size = box === 'border-box' ? entry?.borderBoxSize?.[0] : entry?.contentBoxSize?.[0]
        set(size ? { width: size.inlineSize, height: size.blockSize } : measure(el, box))
      })
      observer.observe(el, { box })
    },
    { immediate: true, flush: 'post' },
  )

  function stop(): void {
    stopWatch()
    disconnect()
  }

  if (getCurrentScope()) onScopeDispose(stop)
  return { width, height, stop }
}
