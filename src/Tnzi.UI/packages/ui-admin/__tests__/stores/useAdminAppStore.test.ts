import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useAdminAppStore } from '../../src/stores/useAdminAppStore'

describe('useAdminAppStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('has default state', () => {
    const store = useAdminAppStore()
    expect(store.siderCollapse).toBe(false)
    expect(store.locale).toBe('en')
    expect(store.fullContent).toBe(false)
    expect(store.reloadFlag).toBe(true)
  })

  it('toggleSiderCollapse flips the flag', () => {
    const store = useAdminAppStore()
    expect(store.siderCollapse).toBe(false)
    store.toggleSiderCollapse()
    expect(store.siderCollapse).toBe(true)
    store.toggleSiderCollapse()
    expect(store.siderCollapse).toBe(false)
  })

  it('setLocale updates the locale', () => {
    const store = useAdminAppStore()
    store.setLocale('zh-cn')
    expect(store.locale).toBe('zh-cn')
  })

  it('toggleFullContent flips the flag', () => {
    const store = useAdminAppStore()
    store.toggleFullContent()
    expect(store.fullContent).toBe(true)
  })

  it('reloadPage sets reloadFlag to false then back to true', async () => {
    const store = useAdminAppStore()
    expect(store.reloadFlag).toBe(true)
    const promise = store.reloadPage()
    expect(store.reloadFlag).toBe(false)
    await promise
    expect(store.reloadFlag).toBe(true)
  })
})
