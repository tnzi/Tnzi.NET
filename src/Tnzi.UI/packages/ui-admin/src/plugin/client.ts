import { inject, type InjectionKey } from 'vue'
import type { HttpClient } from '@tnzi/core/http/http'

/**
 * Injection key for the HttpClient that admin bridges consume.
 * Consumer apps pass a client via `createTnziUiAdmin({ client })`,
 * which `app.provide()`s it under this key. Pages pull it out via
 * `useAdminClient()` and forward it to their bridge factory.
 */
export const TNZI_ADMIN_CLIENT_KEY: InjectionKey<HttpClient> = Symbol('tnzi-admin-client')

/**
 * Retrieve the HttpClient injected by `createTnziUiAdmin({ client })`.
 *
 * @param required - When true (the default), throws if no client was
 *   provided. When false, returns `undefined` and lets the caller fall
 *   back to the bridge's stub path.
 *
 * Example:
 * ```ts
 * import { useAdminClient } from '@tnzi/ui-admin'
 * import { createSystemBridge } from '@tnzi/ui-admin/services/bridges/system-bridge'
 *
 * const bridge = createSystemBridge({ client: useAdminClient() })
 * ```
 */
export function useAdminClient(): HttpClient
export function useAdminClient(required: false): HttpClient | undefined
export function useAdminClient(required: true): HttpClient
export function useAdminClient(required = true): HttpClient | undefined {
  const client = inject(TNZI_ADMIN_CLIENT_KEY, undefined)
  if (!client && required) {
    throw new Error(
      'useAdminClient: no HttpClient was provided. Ensure the host app calls ' +
        'createTnziUiAdmin({ client }) with a valid HttpClient during setup.',
    )
  }
  return client
}
