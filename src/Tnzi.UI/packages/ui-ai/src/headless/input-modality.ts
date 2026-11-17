/**
 * Whether the user's latest input was the keyboard or a pointer.
 *
 * A surface that opens on focus (a tooltip, a rail flyout) must open for a
 * keyboard user tabbing onto its control but not for a tap or a click, which
 * focus the control too. `:focus-visible` says the same thing for styling but
 * is not reliable to query from script in every engine, so this follows the
 * input itself: a key press makes it `keyboard`, a pointer press `pointer`.
 * Before any input it reads `keyboard`, which is what focus moved by script on
 * load (a dialog's initial focus, a skip link) should be treated as.
 *
 * The listeners are installed once per document, on first use, in the capture
 * phase so a handler that stops propagation cannot hide an input from it.
 */

export type InputModality = 'keyboard' | 'pointer'

let modality: InputModality = 'keyboard'
let installed = false

function install(): void {
  if (installed || typeof document === 'undefined') return
  installed = true
  const onKey = (event: KeyboardEvent): void => {
    // Modifier-only presses (a shortcut held while clicking) are not navigation.
    if (event.metaKey || event.ctrlKey || event.altKey) return
    modality = 'keyboard'
  }
  const onPointer = (): void => {
    modality = 'pointer'
  }
  document.addEventListener('keydown', onKey, true)
  document.addEventListener('pointerdown', onPointer, true)
  document.addEventListener('mousedown', onPointer, true)
  document.addEventListener('touchstart', onPointer, { capture: true, passive: true })
}

/** The modality of the latest input (`keyboard` before any). */
export function inputModality(): InputModality {
  install()
  return modality
}

/** Starts tracking now, so the first input after page load is seen. */
export function trackInputModality(): void {
  install()
}
