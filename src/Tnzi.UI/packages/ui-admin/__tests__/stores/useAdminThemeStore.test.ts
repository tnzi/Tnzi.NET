import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useAdminThemeStore } from '../../src/stores/useAdminThemeStore'
import type { AdminThemeSnapshot } from '../../src/theme/admin-config'

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

  it('accepts all 4 layout modes', () => {
    const store = useAdminThemeStore()
    const validModes = [
      'vertical',
      'horizontal',
      'vertical-mix',
      'top-hybrid-header-first',
    ] as const
    for (const m of validModes) {
      store.setLayoutMode(m)
      expect(store.layoutMode).toBe(m)
    }
  })

  it('ignores the two removed hybrid layout modes', () => {
    const store = useAdminThemeStore()
    store.setLayoutMode('vertical')
    // @ts-expect-error — removed modes are no longer part of AdminLayoutMode
    store.setLayoutMode('vertical-hybrid-header-first')
    expect(store.layoutMode).toBe('vertical')
    // @ts-expect-error — removed modes are no longer part of AdminLayoutMode
    store.setLayoutMode('top-hybrid-sidebar-first')
    expect(store.layoutMode).toBe('vertical')
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

// ── Background color customization (siderBg / headerBg / contentBg / containerBg) ──────
describe('useAdminThemeStore — background colors', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    // Reset any CSS vars written by previous tests
    document.documentElement.style.removeProperty('--tnzi-admin-sider-bg')
    document.documentElement.style.removeProperty('--tnzi-admin-header-bg')
    document.documentElement.style.removeProperty('--tnzi-admin-content-bg')
    document.documentElement.style.removeProperty('--tnzi-layout-bg')
    document.documentElement.style.removeProperty('--tnzi-container-bg')
  })

  it('all 4 background fields default to null', () => {
    const store = useAdminThemeStore()
    expect(store.siderBg).toBeNull()
    expect(store.headerBg).toBeNull()
    expect(store.contentBg).toBeNull()
    expect(store.containerBg).toBeNull()
  })

  it('setSiderBg sets the field and writes --tnzi-admin-sider-bg CSS var', () => {
    const store = useAdminThemeStore()
    store.setSiderBg('#123456')
    expect(store.siderBg).toBe('#123456')
    expect(document.documentElement.style.getPropertyValue('--tnzi-admin-sider-bg')).toBe('#123456')
  })

  it('setSiderBg(null) removes the CSS var (resetSiderBg)', () => {
    const store = useAdminThemeStore()
    store.setSiderBg('#123456')
    store.setSiderBg(null)
    expect(store.siderBg).toBeNull()
    expect(document.documentElement.style.getPropertyValue('--tnzi-admin-sider-bg')).toBe('')
  })

  it('resetSiderBg sets field to null and removes CSS var', () => {
    const store = useAdminThemeStore()
    store.setSiderBg('#abcdef')
    store.resetSiderBg()
    expect(store.siderBg).toBeNull()
    expect(document.documentElement.style.getPropertyValue('--tnzi-admin-sider-bg')).toBe('')
  })

  it('setHeaderBg sets the field and writes --tnzi-admin-header-bg CSS var', () => {
    const store = useAdminThemeStore()
    store.setHeaderBg('#ff0000')
    expect(store.headerBg).toBe('#ff0000')
    expect(document.documentElement.style.getPropertyValue('--tnzi-admin-header-bg')).toBe('#ff0000')
  })

  it('resetHeaderBg restores default (null, removes CSS var)', () => {
    const store = useAdminThemeStore()
    store.setHeaderBg('#ff0000')
    store.resetHeaderBg()
    expect(store.headerBg).toBeNull()
    expect(document.documentElement.style.getPropertyValue('--tnzi-admin-header-bg')).toBe('')
  })

  it('setContentBg writes BOTH --tnzi-admin-content-bg and --tnzi-layout-bg', () => {
    const store = useAdminThemeStore()
    store.setContentBg('#f0f4f8')
    expect(store.contentBg).toBe('#f0f4f8')
    expect(document.documentElement.style.getPropertyValue('--tnzi-admin-content-bg')).toBe('#f0f4f8')
    expect(document.documentElement.style.getPropertyValue('--tnzi-layout-bg')).toBe('#f0f4f8')
  })

  it('resetContentBg removes both CSS vars', () => {
    const store = useAdminThemeStore()
    store.setContentBg('#f0f4f8')
    store.resetContentBg()
    expect(store.contentBg).toBeNull()
    expect(document.documentElement.style.getPropertyValue('--tnzi-admin-content-bg')).toBe('')
    expect(document.documentElement.style.getPropertyValue('--tnzi-layout-bg')).toBe('')
  })

  it('setContainerBg writes --tnzi-container-bg', () => {
    const store = useAdminThemeStore()
    store.setContainerBg('#ffffff')
    expect(store.containerBg).toBe('#ffffff')
    expect(document.documentElement.style.getPropertyValue('--tnzi-container-bg')).toBe('#ffffff')
  })

  it('resetContainerBg removes CSS var', () => {
    const store = useAdminThemeStore()
    store.setContainerBg('#ffffff')
    store.resetContainerBg()
    expect(store.containerBg).toBeNull()
    expect(document.documentElement.style.getPropertyValue('--tnzi-container-bg')).toBe('')
  })

  it('reset() sets all 4 bg fields back to null and removes CSS vars', () => {
    const store = useAdminThemeStore()
    store.setSiderBg('#111')
    store.setHeaderBg('#222')
    store.setContentBg('#333')
    store.setContainerBg('#444')
    store.reset()
    expect(store.siderBg).toBeNull()
    expect(store.headerBg).toBeNull()
    expect(store.contentBg).toBeNull()
    expect(store.containerBg).toBeNull()
    expect(document.documentElement.style.getPropertyValue('--tnzi-admin-sider-bg')).toBe('')
    expect(document.documentElement.style.getPropertyValue('--tnzi-admin-header-bg')).toBe('')
    expect(document.documentElement.style.getPropertyValue('--tnzi-admin-content-bg')).toBe('')
    expect(document.documentElement.style.getPropertyValue('--tnzi-layout-bg')).toBe('')
    expect(document.documentElement.style.getPropertyValue('--tnzi-container-bg')).toBe('')
  })

  it('snapshot export includes all 4 bg fields', () => {
    const store = useAdminThemeStore()
    store.setSiderBg('#aabbcc')
    store.setHeaderBg('#ddeeff')
    // contentBg + containerBg stay null

    // Simulate what TThemeDrawer.buildSnapshot() does for the new fields
    const adminSnap = {
      siderBg: store.siderBg,
      headerBg: store.headerBg,
      contentBg: store.contentBg,
      containerBg: store.containerBg,
    }
    expect(adminSnap.siderBg).toBe('#aabbcc')
    expect(adminSnap.headerBg).toBe('#ddeeff')
    expect(adminSnap.contentBg).toBeNull()
    expect(adminSnap.containerBg).toBeNull()
  })

  it('snapshot import with null values calls reset on those fields', () => {
    const store = useAdminThemeStore()
    store.setSiderBg('#111')
    store.setHeaderBg('#222')
    store.setContentBg('#333')
    store.setContainerBg('#444')

    // Simulate applySnapshot with null bg values (older snapshot or cleared)
    const siderBg: string | null = null
    const headerBg: string | null = null
    const contentBg: string | null = null
    const containerBg: string | null = null

    if (siderBg !== undefined) store.setSiderBg(siderBg)
    if (headerBg !== undefined) store.setHeaderBg(headerBg)
    if (contentBg !== undefined) store.setContentBg(contentBg)
    if (containerBg !== undefined) store.setContainerBg(containerBg)

    expect(store.siderBg).toBeNull()
    expect(store.headerBg).toBeNull()
    expect(store.contentBg).toBeNull()
    expect(store.containerBg).toBeNull()
    expect(document.documentElement.style.getPropertyValue('--tnzi-admin-sider-bg')).toBe('')
    expect(document.documentElement.style.getPropertyValue('--tnzi-container-bg')).toBe('')
  })

  it('snapshot import restores specific colors from snapshot', () => {
    const store = useAdminThemeStore()

    // Simulate applySnapshot for bg fields (as TThemeDrawer would)
    const snapshot: Partial<AdminThemeSnapshot['admin']> = {
      siderBg: '#abc123',
      headerBg: '#def456',
      contentBg: '#112233',
      containerBg: '#aabbcc',
    }

    if (snapshot.siderBg !== undefined) store.setSiderBg(snapshot.siderBg)
    if (snapshot.headerBg !== undefined) store.setHeaderBg(snapshot.headerBg)
    if (snapshot.contentBg !== undefined) store.setContentBg(snapshot.contentBg)
    if (snapshot.containerBg !== undefined) store.setContainerBg(snapshot.containerBg)

    expect(store.siderBg).toBe('#abc123')
    expect(store.headerBg).toBe('#def456')
    expect(store.contentBg).toBe('#112233')
    expect(store.containerBg).toBe('#aabbcc')
    expect(document.documentElement.style.getPropertyValue('--tnzi-admin-sider-bg')).toBe('#abc123')
    expect(document.documentElement.style.getPropertyValue('--tnzi-admin-header-bg')).toBe('#def456')
    expect(document.documentElement.style.getPropertyValue('--tnzi-admin-content-bg')).toBe('#112233')
    expect(document.documentElement.style.getPropertyValue('--tnzi-layout-bg')).toBe('#112233')
    expect(document.documentElement.style.getPropertyValue('--tnzi-container-bg')).toBe('#aabbcc')
  })
})
