/**
 * The keys of a small menu of `[role="menuitem"]` buttons, shared by
 * `TDropdownMenu` (opened from a trigger) and `TContextMenu` (opened where the
 * pointer is): Up and Down move between the enabled entries and wrap, Home and
 * End go to the ends, Escape closes it (and goes no further, so a dialog
 * around the menu stays open), Tab closes it and lets focus move on. Keys
 * typed while an input method is composing are not the menu's.
 */

export interface MenuKeysOptions {
  /** The menu's root (or an element that holds it). */
  readonly root: () => HTMLElement | null
  readonly isOpen: () => boolean
  /** Close it; `returnFocus` false for Tab, which moves focus on by itself. */
  readonly close: (returnFocus: boolean) => void
}

/** The enabled entries of a menu, in order. */
export function menuItems(root: HTMLElement | null): HTMLElement[] {
  return Array.from(root?.querySelectorAll<HTMLElement>('[role="menuitem"]:not(:disabled)') ?? [])
}

/** Focuses the entry at `index`, counted from either end and wrapping. */
export function focusMenuItem(root: HTMLElement | null, index: number): void {
  const list = menuItems(root)
  if (list.length === 0) return
  list[((index % list.length) + list.length) % list.length]?.focus()
}

export function createMenuKeydown(options: MenuKeysOptions): (event: KeyboardEvent) => void {
  return (event) => {
    if (!options.isOpen() || event.isComposing) return
    const root = options.root()
    const list = menuItems(root)
    const current = list.indexOf(document.activeElement as HTMLElement)
    switch (event.key) {
      case 'Escape':
        event.preventDefault()
        event.stopPropagation()
        options.close(true)
        return
      case 'Tab':
        options.close(false)
        return
      case 'ArrowDown':
        focusMenuItem(root, current + 1)
        break
      case 'ArrowUp':
        focusMenuItem(root, current < 0 ? -1 : current - 1)
        break
      case 'Home':
        focusMenuItem(root, 0)
        break
      case 'End':
        focusMenuItem(root, -1)
        break
      default:
        return
    }
    event.preventDefault()
  }
}

/** Whether the person is on a touch screen, where opening a menu moves no focus. */
export function isTouchScreen(): boolean {
  return typeof window !== 'undefined' && typeof window.matchMedia === 'function' && window.matchMedia('(hover: none)').matches
}

/**
 * Runs `run` on the next frame: by then `TPopoverMenu` has placed the menu (it
 * is `visibility: hidden` until placed, and a browser will not focus an element
 * that is not visible, so focusing right after the render does nothing).
 */
export function afterMenuPlaced(run: () => void): void {
  if (typeof window !== 'undefined' && typeof window.requestAnimationFrame === 'function') window.requestAnimationFrame(run)
  else setTimeout(run, 0)
}
