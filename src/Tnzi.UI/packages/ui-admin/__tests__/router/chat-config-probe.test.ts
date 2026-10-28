import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { effectScope, ref, type EffectScope } from 'vue'
import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import type { HttpClient } from '@tnzi/core/http'
import { useChatConfigProbe } from '../../src/router/chat-config-probe'
import { useChatStore } from '../../src/stores/useChatStore'

vi.mock('pinia-plugin-persistedstate', () => ({ default: vi.fn() }))

/**
 * The shell probes `GET /chat/config` for whoever is signed in. A user switch
 * without a sign-out resets the chat store (config back to disabled) while the
 * shell stays mounted: the probe has to run again for the new user, or their
 * chat stays off until the shell happens to mount again.
 */

/** Who the backend sees as signed in, and whether each user holds `chat.use`. */
const backend = { user: 'alice', grants: { alice: true, bob: true, carol: false } as Record<string, boolean> }

const get = vi.fn(async (url: string) => {
  if (url !== '/chat/config') throw new Error(`unexpected GET ${url}`)
  return { succeeded: true, success: true, code: 200, data: { enabled: backend.grants[backend.user] ?? false } }
})
const client = { get } as unknown as HttpClient

let scope: EffectScope
const possible = ref(true)
const signedIn = ref(true)

function startProbe(): ReturnType<typeof useChatStore> {
  scope = effectScope()
  scope.run(() => useChatConfigProbe({ possible: () => possible.value, signedIn: () => signedIn.value, client }))
  return useChatStore()
}

describe('useChatConfigProbe', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    get.mockClear()
    backend.user = 'alice'
    possible.value = true
    signedIn.value = true
  })

  afterEach(() => {
    scope.stop()
  })

  it('probes once on mount; the first claim of the session does not probe again', async () => {
    const store = startProbe()
    await flushPromises()
    store.claimForUser('alice')
    await flushPromises()

    expect(get).toHaveBeenCalledTimes(1)
    expect(store.config.enabled).toBe(true)
  })

  it('★ re-probes after a user switch without a sign-out, so the new user gets chat', async () => {
    const store = startProbe()
    store.claimForUser('alice')
    await flushPromises()
    expect(store.config.enabled).toBe(true)

    backend.user = 'bob'
    store.claimForUser('bob')
    await flushPromises()

    expect(get).toHaveBeenCalledTimes(2)
    expect(store.config.enabled).toBe(true)
  })

  it('keeps chat hidden when the user switched to does not hold chat.use', async () => {
    const store = startProbe()
    store.claimForUser('alice')
    await flushPromises()
    expect(store.config.enabled).toBe(true)

    backend.user = 'carol'
    store.claimForUser('carol')
    await flushPromises()

    expect(get).toHaveBeenCalledTimes(2)
    expect(store.config.enabled).toBe(false)
  })

  it('does not probe for a signed-out session (the request could only answer 401)', async () => {
    const store = startProbe()
    store.claimForUser('alice')
    await flushPromises()

    // What a sign-out does: the session ends, then the chat store is reset.
    signedIn.value = false
    store.reset()
    await flushPromises()

    expect(get).toHaveBeenCalledTimes(1)
    expect(store.config.enabled).toBe(false)
  })

  it('does not probe while chat could not run here', async () => {
    possible.value = false
    const store = startProbe()
    store.claimForUser('alice')
    await flushPromises()
    expect(get).not.toHaveBeenCalled()

    possible.value = true
    await flushPromises()
    expect(get).toHaveBeenCalledTimes(1)
    expect(store.config.enabled).toBe(true)
  })

  // The probe only helps if the shell runs it, on the shell's own gates.
  it('the admin shell runs the probe on its chat gate and sign-in state', () => {
    const shellSource = readFileSync(resolve(__dirname, '../../src/router/AdminShellRoot.vue'), 'utf8')
    expect(shellSource).toMatch(/useChatConfigProbe\(\{\s*possible: \(\) => builtinChatPossible\.value,\s*signedIn: \(\) => authStore\.isLogin,/)
  })
})
