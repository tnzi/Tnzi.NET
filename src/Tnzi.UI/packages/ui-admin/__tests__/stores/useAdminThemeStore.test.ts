import { describe, it, expect, beforeEach } from 'vitest'
import { nextTick } from 'vue'
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
    // @ts-expect-error - testing runtime guard
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
    // @ts-expect-error - removed modes are no longer part of AdminLayoutMode
    store.setLayoutMode('vertical-hybrid-header-first')
    expect(store.layoutMode).toBe('vertical')
    // @ts-expect-error - removed modes are no longer part of AdminLayoutMode
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
    // @ts-expect-error - invalid style ignored
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
    // - long English labels now wrap or use icons-only mode).
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

// ── Per-surface background customization (chrome + content-area containers) ──
describe('useAdminThemeStore - background colors', () => {
  const SURFACE_VARS = [
    '--tnzi-admin-sider-bg',
    '--tnzi-admin-header-bg',
    '--tnzi-admin-tab-bg',
    '--tnzi-admin-footer-bg',
    '--tnzi-admin-content-bg',
    '--tnzi-admin-page-header-bg',
    '--tnzi-admin-card-bg',
    '--tnzi-layout-bg',
  ]
  beforeEach(() => {
    setActivePinia(createPinia())
    for (const v of SURFACE_VARS) document.documentElement.style.removeProperty(v)
  })

  it('all 7 surface background fields default to null', () => {
    const store = useAdminThemeStore()
    expect(store.siderBg).toBeNull()
    expect(store.headerBg).toBeNull()
    expect(store.tabBg).toBeNull()
    expect(store.footerBg).toBeNull()
    expect(store.contentBg).toBeNull()
    expect(store.pageHeaderBg).toBeNull()
    expect(store.cardBg).toBeNull()
  })

  it('each setter writes its own CSS var; reset clears it', () => {
    const store = useAdminThemeStore()
    const cases: Array<[(v: string | null) => void, () => void, () => string | null, string]> = [
      [store.setSiderBg, store.resetSiderBg, () => store.siderBg, '--tnzi-admin-sider-bg'],
      [store.setHeaderBg, store.resetHeaderBg, () => store.headerBg, '--tnzi-admin-header-bg'],
      [store.setTabBg, store.resetTabBg, () => store.tabBg, '--tnzi-admin-tab-bg'],
      [store.setFooterBg, store.resetFooterBg, () => store.footerBg, '--tnzi-admin-footer-bg'],
      [store.setContentBg, store.resetContentBg, () => store.contentBg, '--tnzi-admin-content-bg'],
      [store.setPageHeaderBg, store.resetPageHeaderBg, () => store.pageHeaderBg, '--tnzi-admin-page-header-bg'],
      [store.setCardBg, store.resetCardBg, () => store.cardBg, '--tnzi-admin-card-bg'],
    ]
    for (const [set, reset, get, cssVar] of cases) {
      set('#123456')
      expect(get()).toBe('#123456')
      expect(document.documentElement.style.getPropertyValue(cssVar)).toBe('#123456')
      reset()
      expect(get()).toBeNull()
      expect(document.documentElement.style.getPropertyValue(cssVar)).toBe('')
    }
  })

  it('setContentBg writes ONLY --tnzi-admin-content-bg (no longer hijacks --tnzi-layout-bg)', () => {
    const store = useAdminThemeStore()
    store.setContentBg('#f0f4f8')
    expect(document.documentElement.style.getPropertyValue('--tnzi-admin-content-bg')).toBe('#f0f4f8')
    expect(document.documentElement.style.getPropertyValue('--tnzi-layout-bg')).toBe('')
  })

  it('reset() clears all 7 bg fields and their CSS vars', () => {
    const store = useAdminThemeStore()
    store.setSiderBg('#111')
    store.setHeaderBg('#222')
    store.setTabBg('#333')
    store.setFooterBg('#444')
    store.setContentBg('#555')
    store.setPageHeaderBg('#666')
    store.setCardBg('#777')
    store.reset()
    expect(store.siderBg).toBeNull()
    expect(store.headerBg).toBeNull()
    expect(store.tabBg).toBeNull()
    expect(store.footerBg).toBeNull()
    expect(store.contentBg).toBeNull()
    expect(store.pageHeaderBg).toBeNull()
    expect(store.cardBg).toBeNull()
    expect(document.documentElement.style.getPropertyValue('--tnzi-admin-sider-bg')).toBe('')
    expect(document.documentElement.style.getPropertyValue('--tnzi-admin-tab-bg')).toBe('')
    expect(document.documentElement.style.getPropertyValue('--tnzi-admin-footer-bg')).toBe('')
    expect(document.documentElement.style.getPropertyValue('--tnzi-admin-page-header-bg')).toBe('')
    expect(document.documentElement.style.getPropertyValue('--tnzi-admin-card-bg')).toBe('')
  })

  it('page-header / card tones publish a root data-attribute; reset clears it', () => {
    const store = useAdminThemeStore()
    store.setCardBg('#0F172A') // dark card
    expect(store.cardTone).toBe('dark')
    expect(document.documentElement.getAttribute('data-tnzi-card-tone')).toBe('dark')
    store.setPageHeaderBg('#FFFFFF') // light bar
    expect(store.pageHeaderTone).toBe('light')
    expect(document.documentElement.getAttribute('data-tnzi-ph-tone')).toBe('light')
    store.reset()
    expect(document.documentElement.getAttribute('data-tnzi-card-tone')).toBeNull()
    expect(document.documentElement.getAttribute('data-tnzi-ph-tone')).toBeNull()
  })
})

// ── Surface tone (adaptive foreground) ──
describe('useAdminThemeStore - surface tone', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    document.documentElement.style.removeProperty('--tnzi-admin-sider-fg')
    document.documentElement.style.removeProperty('--tnzi-admin-header-fg')
  })

  it('tone is null when no override is set', () => {
    const store = useAdminThemeStore()
    expect(store.siderTone).toBeNull()
    expect(store.headerTone).toBeNull()
  })

  it('a dark color resolves to dark tone; a light color to light tone', () => {
    const store = useAdminThemeStore()
    store.setSiderBg('#0F172A')
    expect(store.siderTone).toBe('dark')
    store.setSiderBg('#FFFFFF')
    expect(store.siderTone).toBe('light')
    store.setHeaderBg('#1E293B')
    expect(store.headerTone).toBe('dark')
    store.setTabBg('#F5F6F8')
    expect(store.tabTone).toBe('light')
    store.setFooterBg('#334155')
    expect(store.footerTone).toBe('dark')
  })

  it('a custom text color forces the tone by its OWN luminance', () => {
    const store = useAdminThemeStore()
    store.setSiderBg('#FFFFFF') // light bg → auto = light surface (dark text)
    expect(store.siderTone).toBe('light')
    expect(store.siderTextColor).toBeNull()
    store.setSiderTextColor('#FFFFFF') // light text → dark-surface token family
    expect(store.siderTone).toBe('dark')
    store.setSiderTextColor('#000000') // dark text → light-surface token family
    expect(store.siderTone).toBe('light')
    store.setSiderTextColor(null) // back to auto (luminance-derived)
    expect(store.siderTone).toBe('light')
  })

  it('a custom text color writes --tnzi-admin-{surface}-fg; null clears it', () => {
    const store = useAdminThemeStore()
    store.setSiderTextColor('#ff0000')
    expect(store.siderTextColor).toBe('#ff0000')
    expect(document.documentElement.style.getPropertyValue('--tnzi-admin-sider-fg')).toBe('#ff0000')
    store.setSiderTextColor(null)
    expect(document.documentElement.style.getPropertyValue('--tnzi-admin-sider-fg')).toBe('')
  })

  it('a text color alone (no bg) drives the tone by its own luminance', () => {
    const store = useAdminThemeStore()
    expect(store.headerTone).toBeNull() // nothing set → follow global mode
    store.setHeaderTextColor('#ffffff') // light text, no bg → dark-surface family
    expect(store.headerTone).toBe('dark')
    store.setHeaderTextColor('#111111') // dark text, no bg → light-surface family
    expect(store.headerTone).toBe('light')
    store.setHeaderTextColor(null)
    expect(store.headerTone).toBeNull()
  })

  it('reset() clears all text-color overrides + their CSS vars', () => {
    const store = useAdminThemeStore()
    store.setSiderBg('#0F172A')
    store.setSiderTextColor('#ff0000')
    store.reset()
    expect(store.siderTextColor).toBeNull()
    expect(store.siderTone).toBeNull()
    expect(document.documentElement.style.getPropertyValue('--tnzi-admin-sider-fg')).toBe('')
  })
})

// ── Accessibility filters mutual exclusion ──
describe('useAdminThemeStore - grayscale/colourWeakness exclusivity', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    document.documentElement.style.filter = ''
  })

  it('enabling one accessibility lens turns the other off', () => {
    const store = useAdminThemeStore()
    store.setColourWeakness(true)
    expect(store.colourWeakness).toBe(true)
    store.setGrayscale(true)
    expect(store.grayscale).toBe(true)
    expect(store.colourWeakness).toBe(false)
    store.setColourWeakness(true)
    expect(store.colourWeakness).toBe(true)
    expect(store.grayscale).toBe(false)
  })

  it('re-applies the page filter when the flag changes WITHOUT the setter (persisted hydration path)', async () => {
    const store = useAdminThemeStore()
    // pinia-plugin-persistedstate writes refs directly, bypassing setGrayscale -
    // the hydration watcher must still paint the filter after a reload.
    store.grayscale = true
    await nextTick()
    expect(document.documentElement.style.filter).toContain('grayscale')
    store.grayscale = false
    await nextTick()
    expect(document.documentElement.style.filter).not.toContain('grayscale')
  })
})
