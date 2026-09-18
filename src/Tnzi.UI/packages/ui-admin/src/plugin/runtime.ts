import { inject, type App, type InjectionKey } from 'vue'
import type { TnziClient } from '@tnzi/core/state'

/**
 * Injection key for the wired core runtime (`createTnziClient()` result) that
 * `defineAdminApp({ runtime })` was given.
 *
 * The login flow reaches the runtime through the callbacks `defineAdminApp`
 * synthesises, so ordinary pages never need it. The exception is a page that
 * receives a session OUTSIDE the login route: accepting an invitation issues
 * tokens on completion (`Identity:Invitation:SignInAfterAccept`), and the page
 * must hand them to the auth manager or the invitee lands on the login form
 * with an account they have just activated and no reason to type anything.
 */
export const TNZI_ADMIN_RUNTIME_KEY: InjectionKey<TnziClient> = Symbol('tnzi-admin-runtime')

export function provideAdminRuntime(app: App, runtime: TnziClient): void {
  app.provide(TNZI_ADMIN_RUNTIME_KEY, runtime)
}

/**
 * The wired runtime, or `undefined` when the host passed `client` instead of
 * `runtime` (it then owns the session itself and the caller must fall back to
 * a route the host's own auth handles, typically the login page).
 */
export function useAdminRuntime(): TnziClient | undefined {
  return inject(TNZI_ADMIN_RUNTIME_KEY, undefined)
}
