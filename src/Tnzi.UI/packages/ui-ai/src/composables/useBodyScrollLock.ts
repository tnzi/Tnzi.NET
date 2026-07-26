import { watch, onScopeDispose, type MaybeRefOrGetter, toValue } from 'vue'

/*
 * Module-level refcount. Several ui-ai surfaces can be open at once (a settings
 * dialog on top of the mobile sidebar drawer, a command palette on top of
 * both). Save/restore per component would let whichever closes first hand the
 * page back its scrollbar while another overlay is still up, so the original
 * value is captured on the first lock and restored only when the last one goes
 * away.
 */
let lockCount = 0
let previousOverflow = ''

function acquire(): void {
  if (typeof document === 'undefined') return
  if (lockCount === 0) {
    previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
  }
  lockCount += 1
}

function release(): void {
  if (typeof document === 'undefined' || lockCount === 0) return
  lockCount -= 1
  if (lockCount === 0) {
    document.body.style.overflow = previousOverflow
    previousOverflow = ''
  }
}

/**
 * Prevent the page behind a modal surface from scrolling while `active` is
 * true. The lock is released when `active` goes false and when the owning
 * scope is disposed, so unmounting mid-overlay cannot leave the page stuck.
 */
export function useBodyScrollLock(active: MaybeRefOrGetter<boolean>): void {
  let held = false

  function sync(next: boolean): void {
    if (next === held) return
    held = next
    if (next) acquire()
    else release()
  }

  watch(() => toValue(active), sync, { immediate: true })

  onScopeDispose(() => sync(false), true)
}
