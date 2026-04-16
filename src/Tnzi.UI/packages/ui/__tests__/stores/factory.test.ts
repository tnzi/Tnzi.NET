import { describe, it, expect, beforeEach, vi } from 'vitest'
import { createApp } from 'vue'

// Import the real module under test — other tests mock it, but this test
// deliberately exercises the actual implementation.
import {
  initStoreRuntime,
  getStoreHttpClient,
  getStoreStorage,
  setStoreHttpClient,
  setStoreStorageAdapter,
  resetStoreRuntime,
  provideStoreRuntime,
  useStoreHttpClient,
  useStoreStorage,
  STORE_HTTP_CLIENT,
  STORE_STORAGE,
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

  describe('provideStoreRuntime + useStoreHttpClient / useStoreStorage', () => {
    it('provides via Vue app AND sets module-level fallback', () => {
      const app = createApp({})
      const provideSpy = vi.spyOn(app, 'provide')
      provideStoreRuntime(app, fakeClient, fakeStorage)
      expect(provideSpy).toHaveBeenCalledWith(STORE_HTTP_CLIENT, fakeClient)
      expect(provideSpy).toHaveBeenCalledWith(STORE_STORAGE, fakeStorage)
      // Module-level fallback is also set
      expect(getStoreHttpClient()).toBe(fakeClient)
      expect(getStoreStorage()).toBe(fakeStorage)
    })

    it('skips storage provide when not passed', () => {
      const app = createApp({})
      const provideSpy = vi.spyOn(app, 'provide')
      provideStoreRuntime(app, fakeClient)
      expect(provideSpy).toHaveBeenCalledWith(STORE_HTTP_CLIENT, fakeClient)
      expect(provideSpy).not.toHaveBeenCalledWith(STORE_STORAGE, expect.anything())
      expect(getStoreStorage()).toBeNull()
    })

    it('useStoreHttpClient falls back to module singleton outside setup', () => {
      initStoreRuntime(fakeClient)
      expect(useStoreHttpClient()).toBe(fakeClient)
    })

    it('useStoreHttpClient throws when nothing available', () => {
      expect(() => useStoreHttpClient()).toThrow(/not provided/)
    })

    it('useStoreStorage returns module-level storage outside setup', () => {
      initStoreRuntime(fakeClient, fakeStorage)
      expect(useStoreStorage()).toBe(fakeStorage)
    })

    it('useStoreStorage returns null when nothing set', () => {
      expect(useStoreStorage()).toBeNull()
    })

    it('useStoreHttpClient prefers injected value when inside component setup', () => {
      initStoreRuntime(fakeClient)
      const scopedClient = { tag: 'scoped' } as any
      let seen: any
      const app = createApp({
        setup() {
          seen = useStoreHttpClient()
          return () => null
        },
      })
      app.provide(STORE_HTTP_CLIENT, scopedClient)
      app.mount(document.createElement('div'))
      expect(seen).toBe(scopedClient)
      app.unmount()
    })

    it('useStoreStorage prefers injected value when inside component setup', () => {
      initStoreRuntime(fakeClient, fakeStorage)
      const scopedStorage = { tag: 'scoped-storage' } as any
      let seen: any
      const app = createApp({
        setup() {
          seen = useStoreStorage()
          return () => null
        },
      })
      app.provide(STORE_STORAGE, scopedStorage)
      app.mount(document.createElement('div'))
      expect(seen).toBe(scopedStorage)
      app.unmount()
    })
  })
})
