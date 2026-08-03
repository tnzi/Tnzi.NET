import { describe, it, expect, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { HttpClient } from '@tnzi/core/http'
import { initStoreRuntime } from '../src/stores/factory'
import { useAppStore } from '../src/stores/app'

describe('useAppStore theme wiring', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    document.documentElement.className = ''
    // AppStateManager needs the dependency shape but never calls the client.
    initStoreRuntime({} as HttpClient)
  })

  it('applies the theme to the document, not only to state', () => {
    const store = useAppStore()

    store.setTheme('dark')

    expect(store.theme).toBe('dark')
    expect(document.documentElement.classList.contains('van-theme-dark')).toBe(true)

    store.setTheme('light')

    expect(store.theme).toBe('light')
    expect(document.documentElement.classList.contains('van-theme-dark')).toBe(false)
  })

  it('keeps toggleTheme and the document in sync', () => {
    const store = useAppStore()
    store.setTheme('light')

    store.toggleTheme()

    expect(store.theme).toBe('dark')
    expect(document.documentElement.classList.contains('van-theme-dark')).toBe(true)
  })
})
