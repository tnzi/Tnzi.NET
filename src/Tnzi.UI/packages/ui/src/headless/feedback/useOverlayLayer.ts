import { computed, getCurrentScope, onScopeDispose, watch, type Ref } from 'vue'
import { isImeComposing } from '@tnzi/core/utils'
import { useOverlayLayerStack, type OverlayLayerEntry } from '@tnzi/core/headless'

/**
 * Keyboard handling for the page's stack of open overlays (dialogs, sheets,
 * palettes, drawers). Only the topmost layer hears Escape and the keys it asked
 * for: a confirmation opened over a settings dialog closes alone on Escape, and
 * the dialog under it does not pull Tab back into itself while it is covered.
 *
 * Before the stack every overlay listened on the document by itself, so one
 * Escape closed every open overlay at once, and each focus trap dragged Tab
 * back into its own element (two open traps fought over focus, and the one
 * opened on top never got past its first control).
 *
 * The stack itself (which layers are open, which is on top) is `@tnzi/core`'s
 * `useOverlayLayerStack()`: page-wide state, kept on core's globalThis registry.
 * This module is the DOM side: one document listener that picks the top layer
 * once per key press and hands the key to it. Per-layer listeners that each
 * asked "am I on top?" were not enough: the top layer closing in its own handler
 * leaves the stack before the next listener runs (Vue flushes between event
 * listeners), and the layer below then answered the same Escape as the new top.
 */

export interface OverlayLayerOptions {
  /**
   * Escape while this layer is on top. Not called for the Escape that cancels
   * an IME composition, nor for one a control already handled
   * (`event.preventDefault()`, e.g. a field that clears itself on Escape).
   */
  onEscape?: () => void
  /** Every other key press while this layer is on top (Tab included). */
  onKeydown?: (event: KeyboardEvent) => void
  /**
   * The layer's element. With it, a key press from inside another modal
   * (`[aria-modal="true"]`) that is not part of this layer - one opened above
   * it by code that does not use this stack, such as a naive-ui modal - is
   * left to that modal instead of being taken by this layer.
   */
  root?: () => HTMLElement | null
}

export interface OverlayLayer {
  /** Whether this layer is open and the topmost open layer. */
  readonly isTop: Readonly<Ref<boolean>>
}

/**
 * Stamped on a key press once a copy of this module has handed it to the top
 * layer. With two copies of the package on a page (a version mismatch between
 * apps), both copies' listeners see the event and share the stack; the stamp
 * makes the second one stand back, so the top layer gets the key once.
 */
const HANDLED = Symbol.for('tnzi.overlay-layer.keydown-handled')

/** A key press from inside a modal above `root` that did not join the stack. */
function fromForeignModal(event: KeyboardEvent, root: HTMLElement | null | undefined): boolean {
  const target = event.target
  if (!root || !(target instanceof Element) || root.contains(target)) return false
  const modal = target.closest('[aria-modal="true"]')
  return modal !== null && !modal.contains(root)
}

/* This copy's document listener: installed while a layer it opened is open,
   and dropped as soon as a key press finds the stack empty. */
let listening = false

function onDocumentKeydown(event: KeyboardEvent): void {
  const top = useOverlayLayerStack().top() as OverlayLayerEntry<OverlayLayerOptions> | undefined
  if (!top) {
    stopListening()
    return
  }
  const stamped = event as KeyboardEvent & { [HANDLED]?: true }
  if (stamped[HANDLED]) return
  stamped[HANDLED] = true

  const options = top.payload
  if (fromForeignModal(event, options.root?.())) return
  if (event.key === 'Escape') {
    if (isImeComposing(event) || event.defaultPrevented) return
    options.onEscape?.()
    return
  }
  options.onKeydown?.(event)
}

function startListening(): void {
  if (listening || typeof document === 'undefined') return
  listening = true
  document.addEventListener('keydown', onDocumentKeydown)
}

function stopListening(): void {
  if (!listening) return
  listening = false
  document.removeEventListener('keydown', onDocumentKeydown)
}

/**
 * Join the page's overlay stack while `active()` is true. The layer goes on
 * top when it becomes active and leaves the stack when it becomes inactive or
 * its scope ends (the component unmounts); keys reach it only while it is the
 * topmost layer.
 *
 * `useFocusTrap` joins the stack by itself, so a dialog that traps focus needs
 * nothing more. Use this directly for an overlay that takes Escape (or other
 * keys) without trapping focus, and handle those keys through `onEscape` /
 * `onKeydown` rather than a listener of your own: a listener of your own runs
 * outside the stack's one-layer-per-key-press guarantee.
 */
export function useOverlayLayer(active: () => boolean, options: OverlayLayerOptions = {}): OverlayLayer {
  const stack = useOverlayLayerStack()
  const entry: OverlayLayerEntry<OverlayLayerOptions> = { id: Symbol('overlay-layer'), payload: options }

  const isTop = computed(() => {
    const layers = stack.layers.value
    return layers.length > 0 && layers[layers.length - 1]!.id === entry.id
  })

  const close = (): void => {
    stack.close(entry.id)
    if (!stack.top()) stopListening()
  }

  const stop = watch(
    () => active(),
    (isActive) => {
      if (isActive) {
        stack.open(entry)
        startListening()
      } else {
        close()
      }
    },
    { immediate: true },
  )

  if (getCurrentScope()) {
    onScopeDispose(() => {
      stop()
      close()
    })
  }

  return { isTop }
}
