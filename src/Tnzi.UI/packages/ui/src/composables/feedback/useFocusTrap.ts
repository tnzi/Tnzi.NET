import { watch, onBeforeUnmount, type Ref } from 'vue'

export interface UseFocusTrapOptions {
  /** Called when the user presses Escape while the trap is active. */
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

/**
 * Accessibility helper for modal-like components. Traps Tab navigation inside `rootRef`,
 * fires `onEscape` when Escape is pressed, and restores focus to the previously focused
 * element when `active` becomes false.
 *
 * Consumers are responsible for wiring the returned activation state themselves — the
 * composable only reacts to the `active` getter passed in.
 */
export function useFocusTrap(
  rootRef: Ref<HTMLElement | null>,
  active: () => boolean,
  options: UseFocusTrapOptions = {},
) {
  let previouslyFocused: HTMLElement | null = null

  function handleKeydown(e: KeyboardEvent) {
    if (!active()) return
    if (e.key === 'Escape') {
      options.onEscape?.()
      return
    }
    if (e.key !== 'Tab') return
    const root = rootRef.value
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

  watch(
    () => active(),
    (isActive) => {
      if (isActive) {
        previouslyFocused = (document.activeElement as HTMLElement) ?? null
        document.addEventListener('keydown', handleKeydown)
        queueMicrotask(() => {
          const root = rootRef.value
          if (!root) return
          const target = options.initialFocus?.() ?? getFocusable(root)[0] ?? root
          target?.focus()
        })
      } else {
        document.removeEventListener('keydown', handleKeydown)
        previouslyFocused?.focus()
        previouslyFocused = null
      }
    },
    { immediate: true },
  )

  onBeforeUnmount(() => {
    document.removeEventListener('keydown', handleKeydown)
  })
}
