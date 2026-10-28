import { computed, ref, shallowRef, toRaw, watchEffect, type ComputedRef, type Ref } from 'vue'

export type FormModalMode = 'create' | 'edit' | 'view'

/**
 * A switch at the left of the form modal's footer, opposite Cancel / Confirm: one line with the label, an optional
 * ⓘ hint and the switch; clicking the label toggles it too. For a record-level flag such as "Active" that is not a
 * field of the form but a state of the record.
 *
 * Shown on edit only by default: a new record starts in its default state, and an "off" switch on create would read
 * as "this will be created off".
 */
export interface FormFooterSwitch {
  /** The form model field the switch writes (`isActive`). */
  key: string
  /** Label: an i18n key resolved through the page translator, or literal text. */
  label: string
  /** Optional hint behind an ⓘ after the label (same resolution as `label`). */
  hint?: string
  /** The modes it appears in. Default `['edit']`. */
  modes?: ReadonlyArray<Exclude<FormModalMode, 'view'>>
}

export interface UseFormModalReturn<T> {
  visible: Ref<boolean>
  mode: Ref<FormModalMode | null>
  formData: Ref<T | null>
  /**
   * A save of this form is in flight. Set by the engine that owns the write
   * (`useCrudPage.submit` / `useDetail.submit`), which also refuses a second
   * call while it is true; the Confirm buttons of `TFormModal` / `TDetailHost`
   * show it as their loading state. A double-clicked Confirm on a create form
   * would otherwise post the same record twice.
   *
   * Deliberately NOT reset by `close()`: closing the overlay does not cancel
   * the request, and a form reopened meanwhile must not start a second write
   * before the first has answered.
   */
  submitting: Ref<boolean>
  open: (mode: FormModalMode, initial?: T | null) => void
  close: () => void
  confirm: () => Promise<T | null>
}

// `structuredClone` cannot serialise Vue reactive Proxy (DataCloneError, by spec).
// Strip Proxy via `toRaw` first, then prefer structuredClone (preserves Date/Map/Set);
// fall back to JSON round-trip for anything structuredClone still rejects (functions, etc.).
function cloneInitial<T>(initial: T | null | undefined): T | null {
  if (initial == null) return null
  const raw = toRaw(initial) as T
  if (typeof structuredClone === 'function') {
    try {
      return structuredClone(raw)
    } catch {
      // fall through to JSON
    }
  }
  return JSON.parse(JSON.stringify(raw)) as T
}

export function useFormModal<T = unknown>(): UseFormModalReturn<T> {
  const visible = ref(false)
  const mode = ref<FormModalMode | null>(null)
  const formData = ref<T | null>(null) as Ref<T | null>
  const submitting = ref(false)

  function open(nextMode: FormModalMode, initial: T | null = null): void {
    mode.value = nextMode
    formData.value = cloneInitial(initial)
    visible.value = true
  }

  function close(): void {
    visible.value = false
    mode.value = null
    formData.value = null
  }

  async function confirm(): Promise<T | null> {
    if (!visible.value) return null
    return formData.value
  }

  return {
    visible,
    mode,
    formData,
    submitting,
    open,
    close,
    confirm,
  }
}

/**
 * The `(mode, formData)` pair an overlay should RENDER - held stable across the
 * close animation.
 *
 * {@link useFormModal.close} hides the overlay and clears `mode` / `formData`
 * in the same tick, but the body is still on screen at that moment: NModal /
 * NDrawer keep it mounted until their leave transition ends. Handing that body
 * a null pair makes it repaint from nothing - selects lose their labels,
 * repeated rows collapse, and a view overlay's footer grows a Confirm button as
 * `mode !== 'view'` flips true - work whose result is thrown away microseconds
 * later when the body unmounts. Measured on a 424-node form in a consuming app:
 * roughly a third of the whole close cost, enough to drop a frame out of the
 * close animation.
 *
 * So the chrome renders the last pair the state held WHILE OPEN and keeps it
 * until the body is gone. Only the *rendering* is held: `visible` stays the
 * single authority for "is it open" and the state itself still clears on close,
 * so consumers watching `formData` for "nothing is being edited" (stopping a
 * poll, resetting a dependent field) keep the signal they are written against.
 *
 * Nothing leaks into the next open: `open()` writes both refs before it flips
 * `visible`, and the held pair is only consulted while `visible` is false.
 *
 * `state` is captured once, so pass the long-lived instance the page owns
 * (`crud.formModal` / `detail.form`) rather than something that can be swapped
 * for a different instance while the chrome stays mounted.
 */
export interface RetainedFormState<T> {
  mode: ComputedRef<FormModalMode | null>
  formData: ComputedRef<T | null>
}

export function useRetainedFormState<T>(state: UseFormModalReturn<T>): RetainedFormState<T> {
  const heldMode = shallowRef<FormModalMode | null>(null)
  const heldData = shallowRef<T | null>(null)

  // `flush: 'sync'` because `close()` clears the refs in the same tick it hides
  // the overlay - a deferred callback would run afterwards and capture exactly
  // the nulls this exists to avoid. Capturing only while `visible` is true also
  // makes it independent of the order of the assignments inside `close()`.
  // Tracking is on the refs themselves, not their contents, so editing a field
  // does not re-run this.
  watchEffect(
    () => {
      if (!state.visible.value) return
      heldMode.value = state.mode.value
      heldData.value = state.formData.value
    },
    { flush: 'sync' },
  )

  return {
    mode: computed(() => (state.visible.value ? state.mode.value : heldMode.value)),
    formData: computed(() => (state.visible.value ? state.formData.value : heldData.value)),
  }
}
