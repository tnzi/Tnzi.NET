/**
 * `useUserCenterProfileExtra()` - let the `userCenter.profile.extra` block join
 * the built-in Profile section's single Reset/Save pair.
 *
 * By default the extension block is self-contained (own data, own validation,
 * own save button) and the framework never touches it. An app that wants ONE
 * form with ONE Reset/Save pair on the Profile page - instead of two save
 * buttons stacked on the same screen - calls this hook from the block's
 * `setup()` and hands the framework a handler:
 *
 * ```ts
 * // ProfileContactBlock.vue - registered via
 * // defineAdminApp({ userCenter: { profile: { extra: () => import('./ProfileContactBlock.vue') } } })
 * import { useUserCenterProfileExtra } from '@tnzi/ui-admin'
 *
 * const form = reactive({ officePhone: '' })
 * const pristine = ref('')
 *
 * useUserCenterProfileExtra({
 *   save: async () => {
 *     await http.put('/admin/staff/me/details', { ...form })
 *     pristine.value = JSON.stringify(form)
 *   },
 *   reset: () => Object.assign(form, JSON.parse(pristine.value)),
 *   dirty: () => JSON.stringify(form) !== pristine.value,
 * })
 * ```
 *
 * Registration is provide/inject, NOT a template ref: the block is rendered
 * through `defineAsyncComponent`, so a ref on it resolves to the async wrapper
 * rather than to the block's own instance.
 *
 * ---
 * ## Contract
 *
 * **Once you register, do not render your own save button.** The framework's
 * Reset/Save now drives both halves; a second button on the same screen is
 * exactly what registering is meant to remove. Hiding it is the app's job -
 * the framework cannot reach into the block's template.
 *
 * **Execution order (Save):**
 * 1. The identity fields go to the framework's own endpoint (`/users/profile`).
 * 2. Only if that succeeded, the registered `save()` is awaited.
 *
 * **The two writes are NOT atomic.** The identity fields live in the framework's
 * backend and the block's fields almost always live in the app's own backend
 * (`/admin/staff/me/details`, …). There is no shared transaction, so one Save
 * can leave you half-committed. The framework does not pretend otherwise:
 *
 * - **Identity write fails** → the handler is never called. Nothing was
 *   committed on either side; the error names the *account profile* half.
 * - **Handler fails** → the identity half stays committed. It is **not** rolled
 *   back (it cannot be), and the error says so explicitly: the account profile
 *   was saved, the additional details were not. The UI reflects the true state -
 *   the identity fields are re-seeded from the server response and stop
 *   counting as unsaved, while your block keeps whatever the user typed so they
 *   can retry without re-entering it.
 *
 * Never surface a single generic "save failed": the user must be able to tell
 * which half survived, because one of them may need to be re-entered and the
 * other must not be re-submitted blindly.
 *
 * **Reset** calls the optional `reset()` alongside the framework's own form
 * reset, so one click restores both halves.
 *
 * **`dirty()`** is folded into the Profile section's combined unsaved-changes
 * state. It is evaluated inside a reactive effect, so read reactive state from
 * it. Omitting it is fine - the framework then simply never claims unsaved
 * changes on the block's behalf (it will not guess, and it never blocks Save).
 *
 * ---
 * Not registering keeps the old behaviour exactly: the block is self-contained,
 * the framework's Reset/Save governs the identity fields only and neither
 * triggers nor awaits the block, and the block may ship its own save button.
 */
import {
  getCurrentScope,
  inject,
  onScopeDispose,
  provide,
  shallowRef,
  type InjectionKey,
  type ShallowRef,
} from 'vue'

/**
 * What the extension block hands the framework so its fields are saved and
 * reset together with the identity fields.
 */
export interface UserCenterProfileExtraHandler {
  /**
   * Persist the block's own fields. Awaited by the framework's Save AFTER the
   * identity fields were written successfully. Throw (or reject) to report a
   * failure - the thrown message is shown to the user, attributed to the
   * extension half. Resolving means "saved"; the framework then reports one
   * combined success.
   */
  save: () => Promise<void>
  /** Restore the block's fields to their last-loaded / last-saved values. */
  reset?: () => void
  /**
   * Whether the block currently holds unsaved edits. Read reactive state - it
   * is evaluated inside a reactive effect. Omit when the block cannot answer
   * cheaply; the framework then never claims unsaved changes for it.
   */
  dirty?: () => boolean
}

/**
 * The slot the Profile section owns and the extension block registers into.
 * Created + provided by the built-in Profile section; consumers only ever call
 * {@link useUserCenterProfileExtra}.
 */
export interface UserCenterProfileExtraRegistry {
  /** The registered handler, or `null` while no block opted in. */
  readonly handler: ShallowRef<UserCenterProfileExtraHandler | null>
  /** Register a handler; returns the matching unregister function. */
  register: (handler: UserCenterProfileExtraHandler) => () => void
}

export const USER_CENTER_PROFILE_EXTRA_KEY: InjectionKey<UserCenterProfileExtraRegistry> = Symbol(
  'tnzi-user-center-profile-extra',
)

// Dev-only double-registration warning. Only ONE block can drive the single
// Save bar, so a second registration silently orphaning the first is a bug
// worth naming. Skipped under vitest (the suite registers on purpose).
// (The package tsconfig carries no vite/client types - probe untyped.)
const metaEnv = (import.meta as unknown as { env?: { DEV?: boolean; VITEST?: unknown } }).env
const IS_DEV_GUARD = !!metaEnv?.DEV && !metaEnv?.VITEST

/** Build the single-slot registry. Called once by the built-in Profile section. */
export function createUserCenterProfileExtraRegistry(): UserCenterProfileExtraRegistry {
  const handler = shallowRef<UserCenterProfileExtraHandler | null>(null)
  return {
    handler,
    register(next) {
      if (IS_DEV_GUARD && handler.value && handler.value !== next) {
        console.warn(
          '[tnzi-admin] useUserCenterProfileExtra(): a second handler replaced the one already ' +
            'registered for the Profile extension block. The Profile section drives a single ' +
            'Reset/Save pair, so only the last registration participates - register once, from ' +
            'the block component itself.',
        )
      }
      handler.value = next
      return () => {
        // Only clear our own slot: a later block may already own it.
        if (handler.value === next) handler.value = null
      }
    },
  }
}

/** Provide the registry to the extension-block subtree (Profile section only). */
export function provideUserCenterProfileExtra(registry: UserCenterProfileExtraRegistry): void {
  provide(USER_CENTER_PROFILE_EXTRA_KEY, registry)
}

/**
 * Register the extension block's save / reset / dirty handler with the built-in
 * Profile section. Call from `setup()`; the handler is unregistered
 * automatically when the block unmounts.
 *
 * See the module header for the full contract - in particular that the two
 * writes are not atomic and that a registered block must not render its own
 * save button.
 */
export function useUserCenterProfileExtra(handler: UserCenterProfileExtraHandler): void {
  const registry = inject(USER_CENTER_PROFILE_EXTRA_KEY, null)
  if (!registry) {
    // Fail loudly: a silent no-op would look like it worked while the user's
    // edits were never saved - the worst possible failure for a save hook.
    throw new Error(
      'useUserCenterProfileExtra() must be called from the User Center Profile extension block ' +
        '(the component passed as `userCenter.profile.extra`). No registry was found in the ' +
        'component tree.',
    )
  }
  const unregister = registry.register(handler)
  if (getCurrentScope()) onScopeDispose(unregister)
}
