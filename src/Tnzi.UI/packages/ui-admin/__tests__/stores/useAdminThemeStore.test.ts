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

  it('pageTransition defaults to fade-slide (soybean signature)', () => {
    const store = useAdminThemeStore()
    expect(store.pageTransition).toBe('fade-slide')
  })

  it('setPageTransition accepts known transitions', () => {
    const store = useAdminThemeStore()
    store.setPageTransition('slide-left')
    expect(store.pageTransition).toBe('slide-left')
  })

  it('accepts all 6 layout modes including the 3 hybrid variants', () => {
    const store = useAdminThemeStore()
    const validModes = [
      'vertical',
      'horizontal',
      'vertical-mix',
      'vertical-hybrid-header-first',
      'top-hybrid-sidebar-first',
      'top-hybrid-header-first',
    ] as const
    for (const m of validModes) {
      store.setLayoutMode(m)
      expect(store.layoutMode).toBe(m)
    }
  })

  it('watermark defaults to disabled with sensible fallbacks', () => {
    const store = useAdminThemeStore()
    expect(store.watermark.enabled).toBe(false)
    expect(store.watermark.text).toBe('Tnzi Admin')
    expect(store.watermark.includeUserName).toBe(true)
    expect(store.watermark.includeDate).toBe(true)
    expect(store.watermark.opacity).toBeCloseTo(0.15)
    expect(store.watermark.fontSize).toBe(16)
  })

  it('setWatermark patches a single field without disturbing others', () => {
    const store = useAdminThemeStore()
    store.setWatermark({ enabled: true })
    expect(store.watermark.enabled).toBe(true)
    expect(store.watermark.text).toBe('Tnzi Admin')
    store.setWatermark({ text: 'Custom' })
    expect(store.watermark.enabled).toBe(true)
    expect(store.watermark.text).toBe('Custom')
  })

  it('resetWatermark restores defaults', () => {
    const store = useAdminThemeStore()
    store.setWatermark({ enabled: true, text: 'Custom' })
    store.resetWatermark()
    expect(store.watermark.enabled).toBe(false)
    expect(store.watermark.text).toBe('Tnzi Admin')
  })

  it('tabStyle defaults to button and validates input', () => {
    const store = useAdminThemeStore()
    expect(store.tabStyle).toBe('button')
    store.setTabStyle('chrome')
    expect(store.tabStyle).toBe('chrome')
    // @ts-expect-error — invalid style ignored
    store.setTabStyle('invalid')
    expect(store.tabStyle).toBe('chrome')
  })

  it('tabScrollAnimation defaults to off and toggles', () => {
    const store = useAdminThemeStore()
    expect(store.tabScrollAnimation).toBe(false)
    store.setTabScrollAnimation(true)
    expect(store.tabScrollAnimation).toBe(true)
  })

  it('fixed positioning flags have sensible defaults', () => {
    const store = useAdminThemeStore()
    expect(store.fixedHeader).toBe(true)
    expect(store.fixedTab).toBe(true)
    expect(store.fixedFooter).toBe(false)
    store.setFixedFooter(true)
    expect(store.fixedFooter).toBe(true)
  })

  it('pageAnimate defaults true and toggles', () => {
    const store = useAdminThemeStore()
    expect(store.pageAnimate).toBe(true)
    store.setPageAnimate(false)
    expect(store.pageAnimate).toBe(false)
  })

  it('invertSider defaults to true (shipped dark-sider theme) and toggles', () => {
    const store = useAdminThemeStore()
    expect(store.invertSider).toBe(true)
    store.toggleInvertSider()
    expect(store.invertSider).toBe(false)
    store.toggleInvertSider()
    expect(store.invertSider).toBe(true)
  })

  it('4-tier sider width system defaults match ui-admin spec', () => {
    // Defaults aligned to the user-supplied ui-admin default config
    // (2026-05-17): siderWidth 220, siderCollapsedWidth 60 (replacing
    // the earlier 240/64 that tried to fit "Organization Management"
    // — long English labels now wrap or use icons-only mode).
    const store = useAdminThemeStore()
    expect(store.siderWidth).toBe(220)
    expect(store.siderCollapsedWidth).toBe(60)
    expect(store.mixSiderWidth).toBe(90)
    expect(store.mixChildMenuWidth).toBe(200)
  })

  it('setMixChildMenuWidth updates the second-level rail width', () => {
    const store = useAdminThemeStore()
    store.setMixChildMenuWidth(240)
    expect(store.mixChildMenuWidth).toBe(240)
  })

  it('reset restores every Phase-A-added field too', () => {
    const store = useAdminThemeStore()
    store.setLayoutMode('top-hybrid-header-first')
    store.setTabStyle('button')
    store.setPageAnimate(false)
    store.setWatermark({ enabled: true, text: 'X' })
    store.setFixedFooter(true)
    store.toggleInvertSider() // true → false
    store.reset()
    expect(store.layoutMode).toBe('vertical')
    expect(store.tabStyle).toBe('button')
    expect(store.pageAnimate).toBe(true)
    expect(store.watermark.enabled).toBe(false)
    expect(store.fixedFooter).toBe(false)
    expect(store.invertSider).toBe(true)
  })
})
