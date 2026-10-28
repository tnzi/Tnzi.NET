import { describe, it, expect, beforeEach, vi } from 'vitest'
import { createApp } from 'vue'
import { createTnziUi } from '../../src/plugin'

// Import the real module under test - other tests mock it, but this test
// deliberately exercises the actual implementation.
import {
  initStoreRuntime,
  getStoreHttpClient,
  getStoreStorage,
  setStoreHttpClient,
  setStoreStorageAdapter,
  resetStoreRuntime,
} from '../../src/stores/factory'

const fakeClient = { get: vi.fn(), post: vi.fn() } as any
const fakeStorage = { getItem: vi.fn(), setItem: vi.fn(), removeItem: vi.fn() } as any

describe('stores/factory', () => {
  beforeEach(() => {
    resetStoreRuntime()
  })

  describe('module-level runtime', () => {
    it('getStoreHttpClient throws before init', () => {
      expect(() => getStoreHttpClient()).toThrow(/not initialized/)
    })

    it('initStoreRuntime sets http client (and optional storage)', () => {
      initStoreRuntime(fakeClient)
      expect(getStoreHttpClient()).toBe(fakeClient)
      expect(getStoreStorage()).toBeNull()
      initStoreRuntime(fakeClient, fakeStorage)
      expect(getStoreStorage()).toBe(fakeStorage)
    })

    it('setStoreHttpClient / setStoreStorageAdapter act as standalone setters', () => {
      setStoreHttpClient(fakeClient)
      expect(getStoreHttpClient()).toBe(fakeClient)
      setStoreStorageAdapter(fakeStorage)
      expect(getStoreStorage()).toBe(fakeStorage)
    })

    it('resetStoreRuntime wipes both slots', () => {
      initStoreRuntime(fakeClient, fakeStorage)
      resetStoreRuntime()
      expect(getStoreStorage()).toBeNull()
      expect(() => getStoreHttpClient()).toThrow()
    })
  })

  // The plugin options are the documented way to wire the stores; they set the
  // same module-level runtime `initStoreRuntime` does.
  it('createTnziUi({ httpClient, storage }) points the store runtime at them', () => {
    const app = createApp({})
    app.use(createTnziUi({ httpClient: fakeClient, storage: fakeStorage, registerComponents: false, registerAdapters: false }))
    expect(getStoreHttpClient()).toBe(fakeClient)
    expect(getStoreStorage()).toBe(fakeStorage)
  })
})
