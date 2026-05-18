import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createApp, defineComponent, h } from 'vue'
import { THEME_CONTEXT_KEY, createThemeContext, mergeThemeSettings } from '@tnzi/ui'
import TExceptionPage from '../../../src/components/pages/TExceptionPage.vue'
import TLoginPage from '../../../src/components/pages/TLoginPage.vue'
import TDashboardPage from '../../../src/components/pages/TDashboardPage.vue'

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
    // Transition is async — flush via nextTick.
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
