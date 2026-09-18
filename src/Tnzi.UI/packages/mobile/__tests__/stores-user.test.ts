import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { HttpClient } from '@tnzi/core/http'
import { initStoreRuntime } from '../src/stores/factory'
import { useUserStore } from '../src/stores/user'

// `initStoreRuntime(httpClient)` with no storage adapter is the documented
// minimum; the user store has to work from it like its auth / app siblings.
describe('useUserStore wiring', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    document.documentElement.className = ''
    localStorage.clear()
    initStoreRuntime({} as HttpClient)
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('applies a theme preference to the document, not only to state', () => {
    const store = useUserStore()

    store.updatePreferences({ theme: 'dark' })

    expect(store.theme).toBe('dark')
    expect(document.documentElement.classList.contains('van-theme-dark')).toBe(true)
  })

  it('persists preferences and recent items to local storage when none is configured', () => {
    const store = useUserStore()

    store.addRecentItem({ id: 'r1', type: 'page', title: 'Orders', url: '/orders' })
    expect(() => vi.advanceTimersByTime(300)).not.toThrow()

    const recents = JSON.parse(localStorage.getItem('tnzi:user:recents') ?? 'null')
    expect(recents).toHaveLength(1)
    expect(recents[0].id).toBe('r1')
  })

  it('reads persisted data back through the same fallback adapter', () => {
    localStorage.setItem('tnzi:user:favorites', JSON.stringify([{ id: 'f1', type: 'page', title: 'Fav', url: '/fav' }]))
    const store = useUserStore()

    expect(() => store.loadPersistedData()).not.toThrow()
    expect(store.favoritesCount).toBe(1)
  })
})
