import { watch, type Ref } from 'vue'
import { useOverlayLayer, type OverlayLayer } from './useOverlayLayer'

export interface UseFocusTrapOptions {
  /** Called when the user presses Escape while the trap is the topmost overlay. */
  onEscape?: () => void
  /**
   * Override the element that should be focused on activation.
   * Defaults to the first focusable element inside `rootRef`.
   */
  initialFocus?: () => HTMLElement | null
}

const FOCUSABLE_SELECTOR = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled]):not([type="hidden"])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(',')

function getFocusable(root: HTMLElement): HTMLElement[] {
  return Array.from(root.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR)).filter(
    (el) => !el.hasAttribute('aria-hidden'),
  )
}

/** Keeps a Tab key press inside `root`: past either end it wraps round. */
function trapTab(e: KeyboardEvent, root: HTMLElement | null): void {
  if (!root) return
  const focusable = getFocusable(root)
  if (focusable.length === 0) {
    e.preventDefault()
    return
  }
  const first = focusable[0]!
  const last = focusable[focusable.length - 1]!
  const current = document.activeElement as HTMLElement | null
  if (e.shiftKey) {
    if (current === first || !root.contains(current)) {
      e.preventDefault()
      last.focus()
    }
  } else {
    if (current === last || !root.contains(current)) {
      e.preventDefault()
      first.focus()
    }
  }
}

/**
 * Accessibility helper for modal-like components. Traps Tab navigation inside `rootRef`,
 * fires `onEscape` when Escape is pressed, and restores focus to the previously focused
 * element when `active` becomes false.
 *
 * The trap is a layer of the page's overlay stack (`useOverlayLayer`): Tab and Escape
 * reach it only while it is the topmost open overlay. A dialog opened above it with
 * `useFocusTrap` (or `useOverlayLayer`) gets them first, so one Escape closes only that
 * dialog and Tab stays inside it.
 *
 * Consumers are responsible for wiring the returned activation state themselves - the
 * composable only reacts to the `active` getter passed in.
 */
export function useFocusTrap(
  rootRef: Ref<HTMLElement | null>,
  active: () => boolean,
  options: UseFocusTrapOptions = {},
): OverlayLayer {
  let previouslyFocused: HTMLElement | null = null

  const layer = useOverlayLayer(active, {
    root: () => rootRef.value,
    onEscape: () => options.onEscape?.(),
    onKeydown: (e) => {
      if (e.key === 'Tab') trapTab(e, rootRef.value)
    },
  })

  watch(
    () => active(),
    (isActive) => {
      if (isActive) {
        previouslyFocused = (document.activeElement as HTMLElement) ?? null
        queueMicrotask(() => {
          const root = rootRef.value
          if (!root) return
          const target = options.initialFocus?.() ?? getFocusable(root)[0] ?? root
          target?.focus()
        })
      } else {
        previouslyFocused?.focus()
        previouslyFocused = null
      }
    },
    { immediate: true },
  )

  return layer
}
