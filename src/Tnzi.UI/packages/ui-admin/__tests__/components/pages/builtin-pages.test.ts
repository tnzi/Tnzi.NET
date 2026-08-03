import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createApp, defineComponent, h, ref, nextTick } from 'vue'
import { THEME_CONTEXT_KEY, createThemeContext, mergeThemeSettings } from '@tnzi/ui'
import TExceptionPage from '../../../src/components/pages/TExceptionPage.vue'
import TLoginPage from '../../../src/components/pages/TLoginPage.vue'
import TDashboardPage from '../../../src/components/pages/TDashboardPage.vue'
import PwdLogin from '../../../src/pages/login/modules/PwdLogin.vue'
import Register from '../../../src/pages/login/modules/Register.vue'
import { reactive } from 'vue'
import {
  LOGIN_CONTEXT_KEY,
  DEFAULT_LOGIN_FEATURES,
  type LoginContext,
} from '@tnzi/ui'

function themeProvide() {
  const ctx = createThemeContext(mergeThemeSettings({}))
  return { [THEME_CONTEXT_KEY as unknown as symbol]: ctx }
}

describe('TExceptionPage', () => {
  it('renders 404 preset by default', () => {
    const wrapper = mount(TExceptionPage)
    expect(wrapper.find('.t-exception-page__title').text()).toBe('404')
    expect(wrapper.find('.t-exception-page__subtitle').text()).toContain('doesn')
  })

  it('renders 403 preset when type=403', () => {
    const wrapper = mount(TExceptionPage, { props: { type: '403' } })
    expect(wrapper.find('.t-exception-page__title').text()).toBe('403')
  })

  it('renders 500 preset when type=500', () => {
    const wrapper = mount(TExceptionPage, { props: { type: '500' } })
    expect(wrapper.find('.t-exception-page__title').text()).toBe('500')
  })

  it('renders offline preset with custom title', () => {
    const wrapper = mount(TExceptionPage, { props: { type: 'offline' } })
    expect(wrapper.find('.t-exception-page__title').text()).toBe('Offline')
  })

  it('uses custom title + subtitle when provided', () => {
    const wrapper = mount(TExceptionPage, {
      props: {
        type: '404',
        title: 'Custom Title',
        subtitle: 'Custom Sub',
      },
    })
    expect(wrapper.find('.t-exception-page__title').text()).toBe('Custom Title')
    expect(wrapper.find('.t-exception-page__subtitle').text()).toBe('Custom Sub')
  })

  it('emits primary on CTA click', async () => {
    const wrapper = mount(TExceptionPage, {
      props: { primaryLabel: 'Go home' },
    })
    await wrapper.find('button').trigger('click')
    expect(wrapper.emitted('primary')).toBeTruthy()
  })

  it('renders secondary button when label provided', () => {
    const wrapper = mount(TExceptionPage, {
      props: { secondaryLabel: 'Retry' },
    })
    const buttons = wrapper.findAll('button')
    expect(buttons.length).toBe(2)
  })
})

describe('TLoginPage', () => {
  // Phase I.7.1 rewrite: shell is now a router-param driven `<component :is>`
  // with 5 module components. The legacy `centered` / `split` toggle is gone.
  const moduleStub = defineComponent({
    name: 'PwdLoginStub',
    render() {
      return h('p', { class: 'pwd-stub' }, 'pwd-stub')
    },
  })
  const codeStub = defineComponent({
    name: 'CodeLoginStub',
    render() {
      return h('p', { class: 'code-stub' }, 'code-stub')
    },
  })

  const fullModuleMap = {
    'pwd-login': moduleStub,
    'code-login': codeStub,
    register: moduleStub,
    'reset-pwd': moduleStub,
    'bind-wechat': moduleStub,
  }

  it('renders the shell with the active module by default (pwd-login)', () => {
    const wrapper = mount(TLoginPage, {
      props: { brand: 'Acme', moduleComponents: fullModuleMap },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('[data-test="t-login-page"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="t-login-page-brand"]').text()).toBe('Acme')
    expect(wrapper.find('.pwd-stub').exists()).toBe(true)
    expect(wrapper.find('.code-stub').exists()).toBe(false)
  })

  it('renders the requested module when `module` prop changes', async () => {
    const wrapper = mount(TLoginPage, {
      props: { module: 'pwd-login', moduleComponents: fullModuleMap },
      global: { provide: themeProvide() },
    })
    await wrapper.setProps({ module: 'code-login' })
    // Transition is async - flush via nextTick.
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.code-stub').exists()).toBe(true)
  })

  it('always renders the WaveBg behind the card', () => {
    const wrapper = mount(TLoginPage, {
      props: { moduleComponents: fullModuleMap },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('[data-test="t-login-page-waves"]').exists()).toBe(true)
  })

  it('renders the toolbar slot for theme + language switchers', () => {
    const wrapper = mount(TLoginPage, {
      props: { moduleComponents: fullModuleMap },
      slots: { toolbar: '<div class="my-tools">[theme] [lang]</div>' },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('[data-test="t-login-page-toolbar"]').exists()).toBe(true)
    expect(wrapper.find('.my-tools').exists()).toBe(true)
  })

  it('uses module label override when supplied', () => {
    const wrapper = mount(TLoginPage, {
      props: {
        moduleComponents: fullModuleMap,
        moduleLabels: { 'pwd-login': 'Custom Welcome' },
      },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('[data-test="t-login-page-module-label"]').text()).toBe(
      'Custom Welcome',
    )
  })

  // ---- 2026-06-11 redesign: split layout (方案 B) ----

  it('split layout renders the brand panel with tagline instead of waves', () => {
    const wrapper = mount(TLoginPage, {
      props: {
        layout: 'split',
        brand: 'Acme',
        tagline: 'Build admin apps fast',
        moduleComponents: fullModuleMap,
      },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('[data-test="t-login-page-brand-panel"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="t-login-page-brand"]').text()).toBe('Acme')
    expect(wrapper.text()).toContain('Build admin apps fast')
    expect(wrapper.find('[data-test="t-login-page-waves"]').exists()).toBe(false)
    expect(wrapper.find('.pwd-stub').exists()).toBe(true)
  })

  it('split layout shows the welcome heading on pwd-login and module label elsewhere', async () => {
    const wrapper = mount(TLoginPage, {
      props: { layout: 'split', moduleComponents: fullModuleMap },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('[data-test="t-login-page-module-label"]').text()).toBe('Welcome back!')
    await wrapper.setProps({ module: 'register' })
    await wrapper.vm.$nextTick()
    expect(wrapper.find('[data-test="t-login-page-module-label"]').text()).toBe('Register')
  })

  it('hides the QR corner without qrComponent and toggles the QR panel with one', async () => {
    const qrStub = defineComponent({
      name: 'QrStub',
      render() {
        return h('div', { class: 'qr-stub' }, 'qr')
      },
    })
    const plain = mount(TLoginPage, {
      props: { moduleComponents: fullModuleMap },
      global: { provide: themeProvide() },
    })
    expect(plain.find('[data-test="t-login-page-qr-toggle"]').exists()).toBe(false)

    const wrapper = mount(TLoginPage, {
      props: { moduleComponents: fullModuleMap, qrComponent: qrStub },
      global: { provide: themeProvide() },
    })
    const toggle = wrapper.find('[data-test="t-login-page-qr-toggle"]')
    expect(toggle.exists()).toBe(true)
    expect(wrapper.find('[data-test="t-login-page-qr-panel"]').exists()).toBe(false)
    await toggle.trigger('click')
    expect(wrapper.find('[data-test="t-login-page-qr-panel"]').exists()).toBe(true)
    expect(wrapper.find('.qr-stub').exists()).toBe(true)
    expect(wrapper.find('.pwd-stub').exists()).toBe(false)
    await toggle.trigger('click')
    expect(wrapper.find('.pwd-stub').exists()).toBe(true)
  })

  it('QR corner only renders on pwd-login / code-login modules', () => {
    const qrStub = defineComponent({ render: () => h('div') })
    const wrapper = mount(TLoginPage, {
      props: { module: 'register', moduleComponents: fullModuleMap, qrComponent: qrStub },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('[data-test="t-login-page-qr-toggle"]').exists()).toBe(false)
  })

  function makeLoginContext(overrides: Partial<LoginContext> = {}): LoginContext {
    return {
      translate: (key, fallback) => fallback ?? key,
      toggleLoginModule: () => undefined,
      callbacks: {},
      demoAccounts: [],
      ui: { labeled: false, pill: true },
      thirdParty: [],
      scene: reactive({ typing: false, passwordVisible: false, passwordLength: 0 }),
      pendingTwoFactor: ref(null),
      pendingCaptcha: ref(null),
      helpers: {
        setTwoFactorRequired: () => undefined,
        clearTwoFactor: () => undefined,
        setCaptchaRequired: () => undefined,
        clearCaptcha: () => undefined,
      },
      features: DEFAULT_LOGIN_FEATURES,
      ...overrides,
    }
  }

  it('PwdLogin renders third-party provider buttons from the context', async () => {
    const onClick = vi.fn()
    const ctx = makeLoginContext({
      thirdParty: [{ key: 'github', icon: 'mdi:github', label: 'GitHub', onClick }],
    })
    const wrapper = mount(PwdLogin, {
      global: {
        provide: {
          ...themeProvide(),
          [LOGIN_CONTEXT_KEY as unknown as symbol]: ctx,
        },
      },
    })
    const row = wrapper.find('[data-test="pwd-login-third-party"]')
    expect(row.exists()).toBe(true)
    await row.find('button').trigger('click')
    expect(onClick).toHaveBeenCalledTimes(1)
  })

  it('PwdLogin feeds typing / password signals into the scene state', async () => {
    const ctx = makeLoginContext()
    const wrapper = mount(PwdLogin, {
      global: {
        provide: {
          ...themeProvide(),
          [LOGIN_CONTEXT_KEY as unknown as symbol]: ctx,
        },
      },
    })
    const userInput = wrapper.findAll('input')[0]!
    await userInput.trigger('focus')
    expect(ctx.scene.typing).toBe(true)
    await userInput.trigger('blur')
    expect(ctx.scene.typing).toBe(false)

    const pwdInput = wrapper.find('input[type="password"]')
    await pwdInput.setValue('secret')
    expect(ctx.scene.passwordLength).toBe(6)
    expect((pwdInput.element as HTMLInputElement).type).toBe('password')

    await wrapper.find('.t-pwd-login__eye-toggle').trigger('click')
    expect(ctx.scene.passwordVisible).toBe(true)
    expect((pwdInput.element as HTMLInputElement).type).toBe('text')
  })

  it('PwdLogin shows enabled+wired entries, hides backend-disabled register', () => {
    const ctx = makeLoginContext({
      callbacks: { codeLogin: vi.fn(), register: vi.fn(), resetPwd: vi.fn() },
      features: { ...DEFAULT_LOGIN_FEATURES, register: false },
    })
    const wrapper = mount(PwdLogin, {
      global: { provide: { ...themeProvide(), [LOGIN_CONTEXT_KEY as unknown as symbol]: ctx } },
    })
    const labels = wrapper.findAll('button').map((b) => b.text())
    expect(labels).toContain('Code login')
    expect(labels).toContain('Forgot password?')
    expect(labels).not.toContain('Register') // disabled by backend features
  })

  it('PwdLogin hides secondary entries when the consumer did not wire the callback', () => {
    // All backend-enabled (default features) but no callbacks → double-gated off.
    const ctx = makeLoginContext({ callbacks: {}, features: DEFAULT_LOGIN_FEATURES })
    const wrapper = mount(PwdLogin, {
      global: { provide: { ...themeProvide(), [LOGIN_CONTEXT_KEY as unknown as symbol]: ctx } },
    })
    const labels = wrapper.findAll('button').map((b) => b.text())
    expect(labels).not.toContain('Code login')
    expect(labels).not.toContain('Register')
    expect(labels).not.toContain('Forgot password?')
  })

  it('PwdLogin reveals the captcha field only when the backend demands one', async () => {
    const ctx = makeLoginContext({
      callbacks: { pwdLogin: vi.fn(), getCaptcha: vi.fn(async () => ({ captchaId: 'c', imageBase64: 'IMG' })) },
    })
    const wrapper = mount(PwdLogin, {
      global: { provide: { ...themeProvide(), [LOGIN_CONTEXT_KEY as unknown as symbol]: ctx } },
    })
    // Adaptive: hidden until the backend pushes a captcha.
    expect(wrapper.find('.t-login-captcha').exists()).toBe(false)
    ctx.pendingCaptcha.value = { captchaId: 'c', imageBase64: 'IMG' }
    await nextTick()
    const field = wrapper.find('.t-login-captcha')
    expect(field.exists()).toBe(true)
    expect(field.find('img').attributes('src')).toContain('IMG')
  })

  it('Register shows the captcha up-front when enabled + fetchable', async () => {
    const getCaptcha = vi.fn(async () => ({ captchaId: 'c', imageBase64: 'REGIMG' }))
    const ctx = makeLoginContext({
      callbacks: { sendCode: vi.fn(), register: vi.fn(), getCaptcha },
      features: { ...DEFAULT_LOGIN_FEATURES, captchaOnRegister: true },
    })
    const wrapper = mount(Register, {
      global: { provide: { ...themeProvide(), [LOGIN_CONTEXT_KEY as unknown as symbol]: ctx } },
    })
    await flushPromises() // the immediate watch fetches on mount
    expect(getCaptcha).toHaveBeenCalledWith('register')
    expect(wrapper.find('.t-login-captcha').exists()).toBe(true)
  })

  it('Register hides the captcha when the register captcha is off', () => {
    const ctx = makeLoginContext({
      callbacks: { sendCode: vi.fn(), register: vi.fn(), getCaptcha: vi.fn() },
      features: { ...DEFAULT_LOGIN_FEATURES, captchaOnRegister: false },
    })
    const wrapper = mount(Register, {
      global: { provide: { ...themeProvide(), [LOGIN_CONTEXT_KEY as unknown as symbol]: ctx } },
    })
    expect(wrapper.find('.t-login-captcha').exists()).toBe(false)
  })
})

describe('TDashboardPage', () => {
  it('renders KPI cards with TCountTo values', () => {
    const wrapper = mount(TDashboardPage, {
      props: {
        kpis: [
          { key: 'a', title: 'Users', value: 1024, icon: 'mdi:account' },
          { key: 'b', title: 'Orders', value: 256, icon: 'mdi:cart' },
        ],
      },
      global: { provide: themeProvide() },
    })
    const titles = wrapper.findAll('.t-dashboard-page__kpi-title')
    expect(titles).toHaveLength(2)
    expect(titles[0]!.text()).toBe('Users')
    expect(titles[1]!.text()).toBe('Orders')
  })

  it('shows empty state when no chart data', () => {
    const wrapper = mount(TDashboardPage, {
      global: { provide: themeProvide() },
    })
    expect(wrapper.findAll('.t-dashboard-page__empty')).toHaveLength(2)
  })

  it('exposes KpiCard tone color via data-tone attribute', () => {
    const wrapper = mount(TDashboardPage, {
      props: {
        kpis: [
          { key: 'a', title: 'X', value: 1, icon: 'mdi:x', tone: 'success' },
        ],
      },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('.t-dashboard-page__kpi-icon').attributes('data-tone')).toBe('success')
  })

  it('renders KPI delta with trend attribute', () => {
    const wrapper = mount(TDashboardPage, {
      props: {
        kpis: [
          {
            key: 'a',
            title: 'X',
            value: 1,
            icon: 'mdi:x',
            delta: '+10%',
            deltaTrend: 'up',
          },
        ],
      },
      global: { provide: themeProvide() },
    })
    const delta = wrapper.find('.t-dashboard-page__kpi-delta')
    expect(delta.text()).toBe('+10%')
    expect(delta.attributes('data-trend')).toBe('up')
  })

  // Suppress unused vars warning for createApp/defineComponent/h
  it('imports stay tree-shakable', () => {
    void createApp
    void defineComponent
    void h
    expect(true).toBe(true)
  })
})
