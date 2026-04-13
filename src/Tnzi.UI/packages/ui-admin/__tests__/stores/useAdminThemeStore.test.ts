import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useAdminThemeStore } from '../../src/stores/useAdminThemeStore'

describe('useAdminThemeStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('has default layout mode vertical', () => {
    const store = useAdminThemeStore()
    expect(store.layoutMode).toBe('vertical')
  })

  it('setLayoutMode updates the mode', () => {
    const store = useAdminThemeStore()
    store.setLayoutMode('horizontal')
    expect(store.layoutMode).toBe('horizontal')
  })

  it('rejects invalid layout modes', () => {
    const store = useAdminThemeStore()
    store.setLayoutMode('horizontal')
    // @ts-expect-error — testing runtime guard
    store.setLayoutMode('invalid')
    expect(store.layoutMode).toBe('horizontal')
  })

  it('header visibility toggles default to true', () => {
    const store = useAdminThemeStore()
    expect(store.headerVisible).toBe(true)
    expect(store.tabVisible).toBe(true)
    expect(store.footerVisible).toBe(true)
  })

  it('tabVisible can be toggled off', () => {
    const store = useAdminThemeStore()
    store.setTabVisible(false)
    expect(store.tabVisible).toBe(false)
  })

  it('pageTransition defaults to fade', () => {
    const store = useAdminThemeStore()
    expect(store.pageTransition).toBe('fade')
  })

  it('setPageTransition accepts known transitions', () => {
    const store = useAdminThemeStore()
    store.setPageTransition('slide-left')
    expect(store.pageTransition).toBe('slide-left')
  })
})
