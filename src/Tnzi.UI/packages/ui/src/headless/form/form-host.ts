/**
 * Form host: lets a container (a modal, a drawer, a detail panel) validate the
 * forms rendered inside its slots without holding a template ref to each one.
 *
 * A slotted form cannot be reached by ref from the component that owns the
 * submit button, so the host provides a registry and every form that wants to
 * take part registers its own `validate`. `TSchemaForm` does this when a host
 * is present; a form rendered outside any host is unaffected.
 *
 * ```ts
 * // in the modal that owns the Save button
 * const host = provideFormHost()
 * async function save() {
 *   if (!(await host.validate())) return
 *   emit('submit')
 * }
 * ```
 */
import { inject, onBeforeUnmount, provide, type InjectionKey } from 'vue'

export interface FormHostParticipant {
  /** Resolves true when the form is valid. Must never reject. */
  validate: () => Promise<boolean>
}

export interface FormHost {
  /** Registers a form; returns the function that removes it again. */
  register: (participant: FormHostParticipant) => () => void
  /**
   * Validates every registered form. All of them run, so each one shows its
   * own messages; the result is false when any of them fails. A host with no
   * registered form is valid.
   */
  validate: () => Promise<boolean>
}

export const FORM_HOST_KEY: InjectionKey<FormHost> = Symbol('tnzi-form-host')

export function createFormHost(): FormHost {
  const participants = new Set<FormHostParticipant>()
  return {
    register(participant) {
      participants.add(participant)
      return () => {
        participants.delete(participant)
      }
    },
    async validate() {
      const results = await Promise.all([...participants].map((p) => p.validate()))
      return results.every(Boolean)
    },
  }
}

/** Creates a host and provides it to the current component's descendants. */
export function provideFormHost(): FormHost {
  const host = createFormHost()
  provide(FORM_HOST_KEY, host)
  return host
}

/** The nearest host above the current component, or null outside any host. */
export function useFormHost(): FormHost | null {
  return inject(FORM_HOST_KEY, null)
}

/**
 * Registers the calling form with the nearest host for the component's
 * lifetime. A no-op when there is no host.
 */
export function useFormHostRegistration(participant: FormHostParticipant): void {
  const host = useFormHost()
  if (!host) return
  const unregister = host.register(participant)
  onBeforeUnmount(unregister)
}
