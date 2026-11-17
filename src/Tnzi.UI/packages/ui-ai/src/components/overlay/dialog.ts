/**
 * What `TDialog` decides without rendering: its width, where focus lands when
 * it opens, and its place in the stack of open dialogs.
 */

export type DialogSize = 'sm' | 'md' | 'lg' | 'xl'

/** How a dialog is drawn below its narrow breakpoint. */
export type DialogNarrowLayout = 'sheet' | 'fullscreen'

/** A CSS length from a prop: a number is pixels, a string is used as written. */
export function cssLength(value: number | string | undefined): string | undefined {
  if (value === undefined || value === '') return undefined
  return typeof value === 'number' ? `${value}px` : value
}

/** The wide-screen width: an explicit `width`, else the size's token. */
export function dialogWidth(size: DialogSize, width?: number | string): string {
  return cssLength(width) ?? `var(--tnzi-ai-dialog-width-${size})`
}

/* Inputs that take typing. A file input is a button in disguise (and is
   usually hidden behind one), and checkboxes, radios, ranges and colours are
   choices, not fields: focusing one on open lights its ring for nothing. */
const NOT_TYPED = ['hidden', 'file', 'checkbox', 'radio', 'range', 'color', 'button', 'submit', 'reset', 'image']

const FIELD_SELECTOR = [
  `input${NOT_TYPED.map((type) => `:not([type="${type}"])`).join('')}:not([disabled]):not([readonly])`,
  'textarea:not([disabled]):not([readonly])',
  'select:not([disabled])',
  '[contenteditable="true"]',
].join(',')

/**
 * Whether `el` can take focus as it is drawn: not inert, not hidden by an
 * attribute, and neither it nor anything between it and `root` is
 * `display: none` / `visibility: hidden`. A field that cannot take focus
 * leaves focus where it was, on the page behind the dialog.
 */
export function isShownFocusable(el: HTMLElement, root: HTMLElement): boolean {
  if (el.closest('[inert], [hidden], [aria-hidden="true"]')) return false
  if (el.matches(':disabled')) return false
  const view = el.ownerDocument.defaultView
  for (let node: HTMLElement | null = el; node && node !== root.parentElement; node = node.parentElement) {
    const style = view?.getComputedStyle(node)
    if (!style) continue
    if (style.display === 'none') return false
    if (node === el && (style.visibility === 'hidden' || style.visibility === 'collapse')) return false
  }
  return true
}

/**
 * Where focus goes when a dialog opens: an element marked `autofocus` (or
 * `data-autofocus`, for components that do not pass `autofocus` through), else
 * the first field that takes typing, so typing can start at once, else the
 * panel itself. Only elements that can take focus as drawn count.
 *
 * Never the first button. Focusing the close button (the first focusable
 * element of most dialogs) lit its focus ring the moment a dialog opened from
 * the keyboard or from a menu, and Enter would then close the dialog.
 */
export function dialogInitialFocus(panel: HTMLElement): HTMLElement {
  const first = (selector: string) =>
    Array.from(panel.querySelectorAll<HTMLElement>(selector)).find((el) => isShownFocusable(el, panel))
  return first('[autofocus], [data-autofocus]') ?? first(FIELD_SELECTOR) ?? panel
}
