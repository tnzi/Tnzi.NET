import { describe, it, expect } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { defineComponent, h } from 'vue'
import { darkTheme } from 'naive-ui'
import {
  THEME_CONTEXT_KEY,
  createThemeContext,
  mergeThemeSettings,
  type ThemeContext,
} from '@tnzi/ui'
import TAdminAppRoot from '../../../src/components/layout/TAdminAppRoot.vue'

function makeRouter(): Router {
  const Home = defineComponent({
    render: () => h('div', { class: 'rv-home' }, 'home'),
  })
  return createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: Home }],
  })
}

describe('TAdminAppRoot', () => {
  it('renders <router-view /> inside the provider stack by default', async () => {
    const router = makeRouter()
    await router.push('/')
    await router.isReady()
    const wrapper = mount(TAdminAppRoot, {
      global: { plugins: [router] },
      attachTo: document.body,
    })
    await flushPromises()
    expect(wrapper.html()).toContain('rv-home')
  })

  it('renders default slot content when supplied', () => {
    const wrapper = mount(TAdminAppRoot, {
      props: { routerView: false },
      slots: { default: '<span class="custom-slot">custom</span>' },
    })
    expect(wrapper.find('.custom-slot').text()).toBe('custom')
  })

  it('falls back to null theme + empty overrides when no theme context provided', () => {
    const wrapper = mount(TAdminAppRoot, {
      props: { routerView: false },
      slots: { default: 'x' },
    })
    expect(wrapper.html()).toContain('x')
  })

  it('pulls theme + overrides from injected ThemeContext', () => {
    const ctx: ThemeContext = createThemeContext(
      mergeThemeSettings({ colors: { primary: '#06B6D4' }, mode: 'dark' }),
    )
    const wrapper = mount(TAdminAppRoot, {
      props: { routerView: false },
      slots: { default: 'themed' },
      global: {
        provide: { [THEME_CONTEXT_KEY as symbol]: ctx },
      },
    })
    expect(wrapper.html()).toContain('themed')
  })

  it('explicit `theme` prop overrides injected context', () => {
    const ctx: ThemeContext = createThemeContext(
      mergeThemeSettings({ mode: 'light' }),
    )
    const wrapper = mount(TAdminAppRoot, {
      props: { theme: darkTheme, routerView: false },
      slots: { default: 'forced-dark' },
      global: {
        provide: { [THEME_CONTEXT_KEY as symbol]: ctx },
      },
    })
    expect(wrapper.html()).toContain('forced-dark')
  })

  it('explicit `themeOverrides` prop overrides injected context overrides', () => {
    const overrides = { common: { primaryColor: '#ff0066' } }
    const wrapper = mount(TAdminAppRoot, {
      props: { themeOverrides: overrides, routerView: false },
      slots: { default: 'override' },
    })
    expect(wrapper.html()).toContain('override')
  })

  it('reactively follows isDark when no explicit theme prop is set', async () => {
    const ctx: ThemeContext = createThemeContext(
      mergeThemeSettings({ mode: 'light' }),
    )
    const wrapper = mount(TAdminAppRoot, {
      props: { routerView: false },
      slots: { default: 'r' },
      global: {
        provide: { [THEME_CONTEXT_KEY as symbol]: ctx },
      },
    })
    expect(wrapper.html()).toContain('r')
    ctx.setMode('dark')
    await wrapper.vm.$nextTick()
    expect(wrapper.html()).toContain('r')
  })
})
