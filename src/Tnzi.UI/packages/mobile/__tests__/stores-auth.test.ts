import { describe, it, expect, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { HttpClient } from '@tnzi/core/http'
import { SESSION_ENDED_FOR_SECURITY_MESSAGE } from '@tnzi/core/state'
import { initStoreRuntime } from '../src/stores/factory'
import { useAuthStore, useAuth } from '../src/stores/auth'

/**
 * The store is a thin proxy over core's AuthStateManager. The manager learned
 * to classify why the previous session ended (`sessionEndReason`: a security
 * revocation versus a plain expiry); a mobile login page built on the store
 * could only read the raw English `error` string and had to import the
 * classifier from core itself to tell the two apart.
 */
describe('useAuthStore session end reason', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    initStoreRuntime({} as HttpClient)
  })

  it('proxies the manager classification on the store and the composable', () => {
    const store = useAuthStore()
    const auth = useAuth()
    expect(store.sessionEndReason).toBeNull()
    expect(auth.sessionEndReason.value).toBeNull()

    // The manager writes the security message when a refresh is refused
    // for a security reason; the store only proxies it, so set it through
    // the same `setError` the manager's own path uses.
    store.setError(SESSION_ENDED_FOR_SECURITY_MESSAGE)
    expect(store.sessionEndReason).toBe('security')
    expect(auth.sessionEndReason.value).toBe('security')
  })
})
