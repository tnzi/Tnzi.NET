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
import { TNZI_ADMIN_CLIENT_KEY } from '../../../src/plugin/client'
import { provideLoginContext } from '@tnzi/ui'
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

  // -----------------------------------------------------------------------
  // session-end notice (core's `auth.sessionEndReason`, read by LoginView)
  // -----------------------------------------------------------------------
  describe('session-end notice', () => {
    it.each(['wave', 'split'] as const)('%s: renders nothing when no session ended', (layout) => {
      const wrapper = mount(TLoginPage, {
        props: { moduleComponents: makeModuleComponents(), layout },
      })
      expect(wrapper.find('[data-test="t-login-page-notice"]').exists()).toBe(false)
    })

    it.each(['wave', 'split'] as const)(
      '%s: ★ "security" shows the warning the user has no other way to learn',
      (layout) => {
        const wrapper = mount(TLoginPage, {
          props: { moduleComponents: makeModuleComponents(), layout, sessionEndReason: 'security' },
        })
        const notice = wrapper.find('[data-test="t-login-page-notice"]')
        expect(notice.exists()).toBe(true)
        expect(notice.attributes('role')).toBe('status')
        expect(notice.text()).toMatch(/security reasons/i)
        expect(notice.classes()).toContain('t-login__notice--warning')
      },
    )

    it('"expired" shows the routine message in the softer style', () => {
      const wrapper = mount(TLoginPage, {
        props: { moduleComponents: makeModuleComponents(), sessionEndReason: 'expired' },
      })
      const notice = wrapper.find('[data-test="t-login-page-notice"]')
      expect(notice.text()).toMatch(/session expired/i)
      expect(notice.classes()).toContain('t-login__notice--info')
      expect(notice.classes()).not.toContain('t-login__notice--warning')
    })

    it('copy goes through translate() so the consumer can localise it', () => {
      const translate = (key: string, fallback?: string) =>
        key === 'admin.login.sessionEndedForSecurity' ? 'LOCALISED' : (fallback ?? key)
      const wrapper = mount(TLoginPage, {
        props: { moduleComponents: makeModuleComponents(), sessionEndReason: 'security', translate },
      })
      expect(wrapper.find('[data-test="t-login-page-notice"]').text()).toBe('LOCALISED')
    })
  })

  // -----------------------------------------------------------------------
  // captcha challenge URL resolver (Altcha fetches its own challenge; the
  // template is API-relative and must be resolved against the API base)
  // -----------------------------------------------------------------------
  describe('resolveUrl in the login context', () => {
    function providedContext(): { resolveUrl?: (url: string) => string } {
      const calls = vi.mocked(provideLoginContext).mock.calls
      return calls[calls.length - 1]![0] as { resolveUrl?: (url: string) => string }
    }

    it('★ defaults to the admin client createTnziUiAdmin({ client }) injected, so the built-in route cannot forget it', () => {
      const client = { resolveUrl: vi.fn((url: string) => `/api${url}`) }
      mount(TLoginPage, {
        props: { moduleComponents: makeModuleComponents() },
        global: { provide: { [TNZI_ADMIN_CLIENT_KEY as unknown as symbol]: client } },
      })
      const ctx = providedContext()
      expect(ctx.resolveUrl).toBeTypeOf('function')
      expect(ctx.resolveUrl!('/captcha/altcha/challenge?purpose=login')).toBe('/api/captcha/altcha/challenge?purpose=login')
      expect(client.resolveUrl).toHaveBeenCalledWith('/captcha/altcha/challenge?purpose=login')
    })

    it('an explicit resolveUrl prop wins over the injected client', () => {
      const client = { resolveUrl: vi.fn((url: string) => `/api${url}`) }
      const resolveUrl = (url: string) => `https://api.example${url}`
      mount(TLoginPage, {
        props: { moduleComponents: makeModuleComponents(), resolveUrl },
        global: { provide: { [TNZI_ADMIN_CLIENT_KEY as unknown as symbol]: client } },
      })
      expect(providedContext().resolveUrl).toBe(resolveUrl)
      expect(client.resolveUrl).not.toHaveBeenCalled()
    })

    it('is left undefined when neither is available (the field then reports the missing client)', () => {
      mount(TLoginPage, { props: { moduleComponents: makeModuleComponents() } })
      expect(providedContext().resolveUrl).toBeUndefined()
    })
  })
})
