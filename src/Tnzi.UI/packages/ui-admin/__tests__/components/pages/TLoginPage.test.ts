import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'

// Stub heavy child components that pull in complex deps (waves animation,
// brand-panel characters, Naive UI NCard) so mount stays fast and isolated.
vi.mock('../../../src/components/pages/login/LoginWaves.vue', () => ({
  default: defineComponent({ name: 'LoginWaves', render: () => h('div', { class: 'stub-waves' }) }),
}))
vi.mock('../../../src/components/pages/login/LoginBrandPanel.vue', () => ({
  default: defineComponent({ name: 'LoginBrandPanel', render: () => h('div', { class: 'stub-brand-panel' }) }),
}))
vi.mock('naive-ui', () => ({
  NCard: defineComponent({
    name: 'NCard',
    setup(_, { slots }) {
      return () => h('div', { class: 'n-card' }, slots.default?.())
    },
  }),
}))

// Stub @tnzi/ui exports consumed by TLoginPage
vi.mock('@tnzi/ui', () => ({
  useTheme: () => ({
    resolvedMode: { value: 'light' },
    settings: { value: { colors: { primary: '#6366f1' } } },
  }),
  getPaletteColorByNumber: (_color: string, _n: number) => '#818cf8',
  mixColor: (_a: string, _b: string, _n: number) => '#e0e7ff',
  TSvgIcon: defineComponent({
    name: 'TSvgIcon',
    props: ['icon', 'size'],
    render: () => h('span', { class: 'stub-icon' }),
  }),
  TThemeSchemaSwitch: defineComponent({
    name: 'TThemeSchemaSwitch',
    render: () => h('span', { 'data-testid': 'theme-switch', class: 'stub-theme-switch' }),
  }),
  TLangSwitch: defineComponent({
    name: 'TLangSwitch',
    render: () => h('span', { 'data-testid': 'lang-switch', class: 'stub-lang-switch' }),
  }),
  // The login stack moved from `../../headless/*` into `@tnzi/ui` on
  // 2026-08-02, so these two now come from the same module as the components
  // above. They must live in THIS factory rather than a second
  // `vi.mock('@tnzi/ui', ...)`: a module gets one factory, and a second call
  // replaces the first - which silently drops every component stub above and
  // leaves the page with nothing to render.
  provideLoginContext: vi.fn(),
  DEFAULT_LOGIN_FEATURES: {
    passwordLogin: true,
    codeLogin: true,
    register: true,
    passwordRecovery: true,
    identifiers: { userName: true, email: true, phone: true },
    codeChannels: { sms: true, email: true },
    captchaOnLogin: false,
    captchaOnRegister: false,
  },
}))

import TLoginPage from '../../../src/components/pages/TLoginPage.vue'
import type { Component } from 'vue'

/** Minimal valid props for TLoginPage - all modules required. */
function makeModuleComponents(): Record<string, Component> {
  const stub = defineComponent({ render: () => h('div') })
  return {
    'pwd-login': stub,
    'code-login': stub,
    register: stub,
    'reset-pwd': stub,
    'bind-wechat': stub,
    'two-factor': stub,
  }
}

describe('TLoginPage toolbar visibility', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  // -----------------------------------------------------------------------
  // wave layout (default)
  // -----------------------------------------------------------------------
  describe('wave layout (default)', () => {
    it('(a) default config: renders TThemeSchemaSwitch, does NOT render TLangSwitch', () => {
      const wrapper = mount(TLoginPage, {
        props: { moduleComponents: makeModuleComponents() },
      })
      expect(wrapper.find('[data-testid="theme-switch"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="lang-switch"]').exists()).toBe(false)
    })

    it('(b) showLangSwitch=true: renders TLangSwitch', () => {
      const wrapper = mount(TLoginPage, {
        props: { moduleComponents: makeModuleComponents(), showLangSwitch: true },
      })
      expect(wrapper.find('[data-testid="lang-switch"]').exists()).toBe(true)
    })

    it('(c) showThemeSwitch=false: does NOT render TThemeSchemaSwitch', () => {
      const wrapper = mount(TLoginPage, {
        props: { moduleComponents: makeModuleComponents(), showThemeSwitch: false },
      })
      expect(wrapper.find('[data-testid="theme-switch"]').exists()).toBe(false)
    })

    it('(d) #toolbar slot overrides: default switches are NOT rendered', () => {
      const wrapper = mount(TLoginPage, {
        props: { moduleComponents: makeModuleComponents() },
        slots: { toolbar: '<span class="custom-toolbar">custom</span>' },
      })
      expect(wrapper.find('[data-testid="theme-switch"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="lang-switch"]').exists()).toBe(false)
      expect(wrapper.find('.custom-toolbar').exists()).toBe(true)
    })
  })

  // -----------------------------------------------------------------------
  // split layout
  // -----------------------------------------------------------------------
  describe('split layout', () => {
    it('(a) default config: renders TThemeSchemaSwitch, does NOT render TLangSwitch', () => {
      const wrapper = mount(TLoginPage, {
        props: { moduleComponents: makeModuleComponents(), layout: 'split' },
      })
      expect(wrapper.find('[data-testid="theme-switch"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="lang-switch"]').exists()).toBe(false)
    })

    it('(b) showLangSwitch=true: renders TLangSwitch', () => {
      const wrapper = mount(TLoginPage, {
        props: { moduleComponents: makeModuleComponents(), layout: 'split', showLangSwitch: true },
      })
      expect(wrapper.find('[data-testid="lang-switch"]').exists()).toBe(true)
    })

    it('(c) showThemeSwitch=false: does NOT render TThemeSchemaSwitch', () => {
      const wrapper = mount(TLoginPage, {
        props: { moduleComponents: makeModuleComponents(), layout: 'split', showThemeSwitch: false },
      })
      expect(wrapper.find('[data-testid="theme-switch"]').exists()).toBe(false)
    })

    it('(d) #toolbar slot overrides: default switches are NOT rendered', () => {
      const wrapper = mount(TLoginPage, {
        props: { moduleComponents: makeModuleComponents(), layout: 'split' },
        slots: { toolbar: '<span class="custom-toolbar">custom</span>' },
      })
      expect(wrapper.find('[data-testid="theme-switch"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="lang-switch"]').exists()).toBe(false)
      expect(wrapper.find('.custom-toolbar').exists()).toBe(true)
    })
  })
})
