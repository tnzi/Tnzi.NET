import { describe, it, expect } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { defineComponent, h, nextTick } from 'vue'
import { NConfigProvider, darkTheme, type GlobalThemeOverrides } from 'naive-ui'
import {
  THEME_CONTEXT_KEY,
  createThemeContext,
  mergeThemeSettings,
  type ThemeContext,
} from '@tnzi/ui'
import TAdminAppRoot from '../../../src/components/layout/TAdminAppRoot.vue'
import TThemeSchemaSwitch from '../../../src/components/utility/TThemeSchemaSwitch.vue'
import { ADMIN_NAIVE_OVERRIDES_KEY, type NaiveOverridesSource } from '../../../src/theme/naive-bridge'

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

  // An application with a set of naive overrides for each mode configures it
  // once (createTnziUiAdmin({ naiveOverrides })) and passes nothing to the root.
  it('hands naive the overrides of the current mode and swaps them when the mode changes', async () => {
    const ctx: ThemeContext = createThemeContext(mergeThemeSettings({ mode: 'light' }))
    const source: NaiveOverridesSource = ({ isDark }) => ({ Card: { color: isDark ? '#1c1c1b' : '#fafafa' } })
    const wrapper = mount(TAdminAppRoot, {
      props: { routerView: false },
      slots: { default: 'x' },
      global: {
        provide: { [THEME_CONTEXT_KEY as symbol]: ctx, [ADMIN_NAIVE_OVERRIDES_KEY as symbol]: source },
      },
    })
    const provider = () => wrapper.findComponent(NConfigProvider)
    const card = () => (provider().props('themeOverrides') as GlobalThemeOverrides).Card?.color
    expect(card()).toBe('#fafafa')
    expect(provider().props('theme')).toBeNull()
    ctx.setMode('dark')
    await nextTick()
    expect(card()).toBe('#1c1c1b')
    expect(provider().props('theme')).toBe(darkTheme)
  })

  // The login page's switch (and `auto` following the OS) go through the same
  // context as the shell's header button, so the `dark` class, naive's theme
  // and the per-mode overrides agree in all three cases.
  it('the theme switch inside the root moves the class, naive theme and overrides together', async () => {
    document.documentElement.classList.remove('dark')
    const ctx: ThemeContext = createThemeContext(mergeThemeSettings({ mode: 'light' }))
    const source: NaiveOverridesSource = ({ isDark }) => ({ Input: { color: isDark ? '#222' : '#fff' } })
    const wrapper = mount(TAdminAppRoot, {
      props: { routerView: false },
      slots: { default: () => h(TThemeSchemaSwitch) },
      global: {
        provide: { [THEME_CONTEXT_KEY as symbol]: ctx, [ADMIN_NAIVE_OVERRIDES_KEY as symbol]: source },
      },
    })
    const provider = () => wrapper.findComponent(NConfigProvider)
    const input = () => (provider().props('themeOverrides') as GlobalThemeOverrides).Input?.color

    await wrapper.find('.t-theme-schema-switch').trigger('click')
    expect(ctx.settings.value.mode).toBe('dark')
    expect(document.documentElement.classList.contains('dark')).toBe(true)
    expect(provider().props('theme')).toBe(darkTheme)
    expect(input()).toBe('#222')

    // The shell's header sets the mode through the context: the switch shows it.
    ctx.setMode('light')
    await nextTick()
    expect(wrapper.find('.t-theme-schema-switch').attributes('data-schema')).toBe('light')
    expect(document.documentElement.classList.contains('dark')).toBe(false)
    expect(provider().props('theme')).toBeNull()
    ctx.setMode('dark')
    await nextTick()
    expect(wrapper.find('.t-theme-schema-switch').attributes('data-schema')).toBe('dark')

    await wrapper.find('.t-theme-schema-switch').trigger('click')
    await nextTick()
    expect(ctx.settings.value.mode).toBe('auto')
    const osDark = window.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false
    expect(document.documentElement.classList.contains('dark')).toBe(osDark)
    expect(provider().props('theme')).toBe(osDark ? darkTheme : null)
    wrapper.unmount()
    document.documentElement.classList.remove('dark')
  })

  it('an auto context on a dark OS gives naive the dark theme and the document the class', async () => {
    const originalMM = window.matchMedia
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: () => ({ matches: true, addEventListener: () => {}, removeEventListener: () => {} }),
    })
    try {
      const ctx: ThemeContext = createThemeContext(mergeThemeSettings({ mode: 'auto' }))
      const wrapper = mount(TAdminAppRoot, {
        props: { routerView: false },
        slots: { default: () => h(TThemeSchemaSwitch) },
        global: { provide: { [THEME_CONTEXT_KEY as symbol]: ctx } },
      })
      await nextTick()
      expect(document.documentElement.classList.contains('dark')).toBe(true)
      expect(wrapper.findComponent(NConfigProvider).props('theme')).toBe(darkTheme)
      wrapper.unmount()
    } finally {
      Object.defineProperty(window, 'matchMedia', { configurable: true, value: originalMM })
      document.documentElement.classList.remove('dark')
    }
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

  it('registers window.$message / $dialog / $notification / $loadingBar handles', () => {
    const w = window as unknown as {
      $message?: { error?: unknown }
      $dialog?: { error?: unknown }
      $notification?: { error?: unknown }
      $loadingBar?: { start?: unknown }
    }
    delete w.$message
    delete w.$dialog
    delete w.$notification
    delete w.$loadingBar
    mount(TAdminAppRoot, {
      props: { routerView: false },
      slots: { default: 'handles' },
    })
    expect(typeof w.$message?.error).toBe('function')
    expect(typeof w.$dialog?.error).toBe('function')
    expect(typeof w.$notification?.error).toBe('function')
    expect(typeof w.$loadingBar?.start).toBe('function')
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
