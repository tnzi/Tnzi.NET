/**
 * Asking before something that cannot be undone. `useConfirm()(options)` opens
 * the app's one confirmation dialog (`TConfirmHost`, mounted by `TChatApp`; an
 * app with its own shell mounts it once) and resolves with the person's answer.
 *
 * While `action` runs the dialog stays open and busy (no Escape, no backdrop,
 * buttons disabled), and a thrown error is shown in it rather than closing it,
 * so the person sees the failure next to what they tried and can try again or
 * cancel.
 *
 * `useConfirm()` works outside a component too: it talks to the package-wide
 * confirmer, which is what a host shows unless it is given another
 * (`createConfirmer()`).
 */
import { shallowRef, type ShallowRef } from 'vue'

export interface ConfirmOptions {
  title: string
  message: string
  /** The confirm button's words; the catalogue's "Confirm" when left out. */
  confirmText?: string
  /** The cancel button's words; the catalogue's "Cancel" when left out. */
  cancelText?: string
  /** Paints the confirm button as destructive. */
  danger?: boolean
  /** An extra choice, e.g. "Also delete its files"; its value reaches `action`. */
  checkbox?: string
  /**
   * The work to do once confirmed. A rejection keeps the dialog open with the
   * error's words (`describe`); resolving closes it.
   */
  action?: (checked: boolean) => Promise<void>
  /** Words for a failed `action`; the error's `message` when left out. */
  describe?: (error: unknown) => string
}

export interface ConfirmRequest extends ConfirmOptions {
  resolve: (confirmed: boolean) => void
}

export type ConfirmFn = (options: ConfirmOptions) => Promise<boolean>

export interface Confirmer {
  /** The request on screen, for the host to render; null when none. */
  readonly current: Readonly<ShallowRef<ConfirmRequest | null>>
  /** Resolves true when confirmed (and `action` succeeded), false when dismissed. */
  confirm: ConfirmFn
  /** The first host mounted draws this confirmer; the others draw nothing. */
  readonly owner: Readonly<ShallowRef<symbol | null>>
  claim(host: symbol): void
  release(host: symbol): void
}

export function createConfirmer(): Confirmer {
  const current = shallowRef<ConfirmRequest | null>(null)
  const owner = shallowRef<symbol | null>(null)
  const claims: symbol[] = []
  return {
    current,
    owner,
    confirm(options) {
      // A second request replaces an unanswered first one, which counts as dismissed.
      current.value?.resolve(false)
      return new Promise<boolean>((resolve) => {
        const request: ConfirmRequest = {
          ...options,
          resolve: (confirmed) => {
            if (current.value === request) current.value = null
            resolve(confirmed)
          },
        }
        current.value = request
      })
    },
    claim(host) {
      if (!claims.includes(host)) claims.push(host)
      owner.value = claims[0] ?? null
    },
    release(host) {
      const index = claims.indexOf(host)
      if (index >= 0) claims.splice(index, 1)
      owner.value = claims[0] ?? null
    },
  }
}

/** The package-wide confirmer: what `useConfirm()` asks and a host shows by default. */
export const confirmer: Confirmer = createConfirmer()

/** Asks the person; usable in and outside components. */
export function useConfirm(): ConfirmFn {
  return confirmer.confirm
}
