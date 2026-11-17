import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h, nextTick } from 'vue'
import { THEME_CONTEXT_KEY, createThemeContext, mergeThemeSettings, type ThemeContext } from '@tnzi/ui'
import TThemeSchemaSwitch from '../../../src/components/utility/TThemeSchemaSwitch.vue'
import TLangSwitch from '../../../src/components/utility/TLangSwitch.vue'
import TFullScreen from '../../../src/components/utility/TFullScreen.vue'
import TReloadButton from '../../../src/components/utility/TReloadButton.vue'
import TPinToggler from '../../../src/components/utility/TPinToggler.vue'
import TMenuToggler from '../../../src/components/utility/TMenuToggler.vue'

function themeProvide() {
  const ctx = createThemeContext(mergeThemeSettings({}))
  return { [THEME_CONTEXT_KEY as unknown as symbol]: ctx }
}

describe('TThemeSchemaSwitch without a theme context', () => {
  beforeEach(() => {
    document.documentElement.classList.remove('dark')
  })

  it('cycles light → dark → auto → light and emits change', async () => {
    const wrapper = mount(TThemeSchemaSwitch, {
      props: { defaultValue: 'light', applyDocumentClass: false },
    })
    const btn = wrapper.find('button')
    await btn.trigger('click')
    expect(wrapper.emitted('change')?.[0]).toEqual(['dark'])
    await btn.trigger('click')
    expect(wrapper.emitted('change')?.[1]).toEqual(['auto'])
    await btn.trigger('click')
    expect(wrapper.emitted('change')?.[2]).toEqual(['light'])
  })

  it('starts at defaultValue and emits a different value after click', async () => {
    const wrapper = mount(TThemeSchemaSwitch, {
      props: { defaultValue: 'dark', applyDocumentClass: false },
    })
    await wrapper.find('button').trigger('click')
    // dark → auto on first click
    expect(wrapper.emitted('change')?.[0]).toEqual(['auto'])
  })

  it('toggles `dark` class on html when applyDocumentClass=true and value=dark', async () => {
    const wrapper = mount(TThemeSchemaSwitch, {
      props: { defaultValue: 'dark', applyDocumentClass: true },
    })
    expect(document.documentElement.classList.contains('dark')).toBe(true)
    wrapper.unmount()
  })

  // While the switch sits on 'auto' the document follows the OS, not only the
  // OS scheme at the moment 'auto' was picked; leaving 'auto' or unmounting
  // stops following it.
  it('follows OS colour-scheme changes while on auto', async () => {
    const originalMM = window.matchMedia
    const listeners = new Set<(e: { matches: boolean }) => void>()
    let osDark = false
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: vi.fn(() => ({
        get matches() {
          return osDark
        },
        addEventListener: (_: string, fn: (e: { matches: boolean }) => void) => listeners.add(fn),
        removeEventListener: (_: string, fn: (e: { matches: boolean }) => void) => listeners.delete(fn),
      })),
    })
    const switchOs = (dark: boolean) => {
      osDark = dark
      for (const fn of [...listeners]) fn({ matches: dark })
    }
    try {
      const wrapper = mount(TThemeSchemaSwitch, {
        props: { value: 'auto', applyDocumentClass: true },
        })
      expect(document.documentElement.classList.contains('dark')).toBe(false)

      switchOs(true)
      expect(document.documentElement.classList.contains('dark')).toBe(true)

      await wrapper.setProps({ value: 'light' })
      expect(listeners.size).toBe(0)
      expect(document.documentElement.classList.contains('dark')).toBe(false)

      await wrapper.setProps({ value: 'auto' })
      switchOs(false)
      expect(document.documentElement.classList.contains('dark')).toBe(false)
      switchOs(true)
      expect(document.documentElement.classList.contains('dark')).toBe(true)

      wrapper.unmount()
      expect(listeners.size).toBe(0)
    } finally {
      Object.defineProperty(window, 'matchMedia', { configurable: true, value: originalMM })
    }
  })
})

// The switch drives the theme context where one is installed, so the `dark`
// class, the context's mode and everything reading the context (the naive
// theme in TAdminAppRoot, the login page's dark styling) move together.
describe('TThemeSchemaSwitch with a theme context', () => {
  const originalMM = window.matchMedia
  let osDark = false
  const listeners = new Set<(e: { matches: boolean }) => void>()

  beforeEach(() => {
    document.documentElement.classList.remove('dark')
    osDark = false
    listeners.clear()
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: vi.fn(() => ({
        get matches() {
          return osDark
        },
        addEventListener: (_: string, fn: (e: { matches: boolean }) => void) => listeners.add(fn),
        removeEventListener: (_: string, fn: (e: { matches: boolean }) => void) => listeners.delete(fn),
      })),
    })
  })
  afterEach(() => {
    Object.defineProperty(window, 'matchMedia', { configurable: true, value: originalMM })
  })

  function mountWith(ctx: ThemeContext, props: Record<string, unknown> = {}) {
    return mount(TThemeSchemaSwitch, {
      props,
      global: { provide: { [THEME_CONTEXT_KEY as unknown as symbol]: ctx } },
    })
  }

  it('shows the context mode and cycles it through setMode', async () => {
    const ctx = createThemeContext(mergeThemeSettings({ mode: 'light' }))
    const wrapper = mountWith(ctx)
    expect(wrapper.find('[data-schema]').attributes('data-schema')).toBe('light')

    await wrapper.find('button').trigger('click')
    expect(ctx.settings.value.mode).toBe('dark')
    expect(ctx.isDark.value).toBe(true)
    expect(document.documentElement.classList.contains('dark')).toBe(true)
    expect(wrapper.emitted('change')?.[0]).toEqual(['dark'])

    await wrapper.find('button').trigger('click')
    expect(ctx.settings.value.mode).toBe('auto')
    expect(ctx.isDark.value).toBe(false)
    expect(document.documentElement.classList.contains('dark')).toBe(false)
  })

  it('does not write the class over the context on mount (default value auto, OS dark)', () => {
    osDark = true
    const ctx = createThemeContext(mergeThemeSettings({ mode: 'light' }))
    mountWith(ctx)
    expect(ctx.isDark.value).toBe(false)
    expect(document.documentElement.classList.contains('dark')).toBe(false)
  })

  it('on auto, the context and the class both follow the OS', async () => {
    const ctx = createThemeContext(mergeThemeSettings({ mode: 'auto' }))
    mountWith(ctx)
    expect(document.documentElement.classList.contains('dark')).toBe(false)

    osDark = true
    for (const fn of [...listeners]) fn({ matches: true })
    await Promise.resolve()
    expect(ctx.isDark.value).toBe(true)
    expect(document.documentElement.classList.contains('dark')).toBe(true)
  })

  it('sets a controlled value on the context', async () => {
    const ctx = createThemeContext(mergeThemeSettings({ mode: 'light' }))
    const wrapper = mountWith(ctx, { value: 'dark' })
    await nextTick()
    expect(ctx.settings.value.mode).toBe('dark')
    expect(document.documentElement.classList.contains('dark')).toBe(true)

    await wrapper.setProps({ value: 'light' })
    expect(ctx.settings.value.mode).toBe('light')
    expect(document.documentElement.classList.contains('dark')).toBe(false)
  })
})

describe('TThemeSchemaSwitch labels', () => {
  it('names the mode from the admin locale, Chinese on a zh-cn page', async () => {
    const { createPinia, setActivePinia } = await import('pinia')
    const { useAdminAppStore } = await import('../../../src/stores/useAdminAppStore')
    setActivePinia(createPinia())
    const ctx = createThemeContext(mergeThemeSettings({ mode: 'dark' }))
    const provide = { [THEME_CONTEXT_KEY as unknown as symbol]: ctx }
    useAdminAppStore().setLocale('zh-cn')
    const zh = mount(TThemeSchemaSwitch, { global: { provide } })
    expect(zh.find('button').attributes('aria-label')).toBe('深色')
    zh.unmount()
    useAdminAppStore().setLocale('en')
    ctx.setMode('auto')
    const en = mount(TThemeSchemaSwitch, { global: { provide } })
    expect(en.find('button').attributes('aria-label')).toBe('Follow system')
    en.unmount()
  })

  it("an app's translate wins where it knows the key", () => {
    const ctx = createThemeContext(mergeThemeSettings({ mode: 'dark' }))
    const w = mount(TThemeSchemaSwitch, {
      props: { translate: (k: string) => (k === 'admin.themeSchema.dark' ? 'Nuit' : k) },
      global: { provide: { [THEME_CONTEXT_KEY as unknown as symbol]: ctx } },
    })
    expect(w.find('button').attributes('aria-label')).toBe('Nuit')
  })
})

describe('TLangSwitch', () => {
  it('mounts without error using default options', () => {
    const wrapper = mount(TLangSwitch, {
      props: { defaultValue: 'en' },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('button').exists()).toBe(true)
  })

  it('mounts with custom options', () => {
    const wrapper = mount(TLangSwitch, {
      props: {
        defaultValue: 'fr',
        options: [
          { label: 'English', value: 'en' },
          { label: 'Français', value: 'fr' },
        ],
      },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('button').exists()).toBe(true)
  })
})

describe('TFullScreen', () => {
  it('mounts and exposes a clickable button', () => {
    const wrapper = mount(TFullScreen, {
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('button').exists()).toBe(true)
  })
})

describe('TReloadButton', () => {
  it('emits reload on click', async () => {
    const wrapper = mount(TReloadButton, {
      global: { provide: themeProvide() },
    })
    await wrapper.find('button').trigger('click')
    expect(wrapper.emitted('reload')).toBeTruthy()
  })

  it('awaits the onReload callback and spins until it settles', async () => {
    let resolved = false
    const wrapper = mount(TReloadButton, {
      props: {
        onReload: async () => {
          await new Promise<void>((r) => setTimeout(r, 10))
          resolved = true
        },
      },
      global: { provide: themeProvide() },
    })
    await wrapper.find('button').trigger('click')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.t-reload-btn--spinning').exists()).toBe(true)
    await new Promise<void>((r) => setTimeout(r, 20))
    await wrapper.vm.$nextTick()
    expect(resolved).toBe(true)
    expect(wrapper.find('.t-reload-btn--spinning').exists()).toBe(false)
  })

  // `@reload="fn"` and `:on-reload="fn"` are the same vnode prop, which Vue
  // hands both to the declared `onReload` prop and to `emit('reload')`, so a
  // click that emitted and then awaited the prop ran the handler twice.
  it('runs the reload handler once per click', async () => {
    const handler = vi.fn(async () => {})
    const wrapper = mount(TReloadButton, {
      props: { onReload: handler },
      global: { provide: themeProvide() },
    })
    await wrapper.find('button').trigger('click')
    await new Promise<void>((r) => setTimeout(r, 0))
    expect(handler).toHaveBeenCalledTimes(1)
  })
})

describe('TPinToggler', () => {
  it('emits toggle with negated state', async () => {
    const wrapper = mount(TPinToggler, {
      props: { pinned: false },
      global: { provide: themeProvide() },
    })
    await wrapper.find('button').trigger('click')
    expect(wrapper.emitted('toggle')?.[0]).toEqual([true])
    expect(wrapper.emitted('update:pinned')?.[0]).toEqual([true])
  })

  it('mounts both pinned states without error', () => {
    const wrap = mount(TPinToggler, {
      props: { pinned: true },
      global: { provide: themeProvide() },
    })
    expect(wrap.find('button').exists()).toBe(true)
  })
})

describe('TMenuToggler', () => {
  it('emits toggle with negated collapsed state', async () => {
    const wrapper = mount(TMenuToggler, {
      props: { collapsed: false },
      global: { provide: themeProvide() },
    })
    await wrapper.find('button').trigger('click')
    expect(wrapper.emitted('toggle')?.[0]).toEqual([true])
    expect(wrapper.emitted('update:collapsed')?.[0]).toEqual([true])
  })
})

// TSystemLogo stays in @tnzi/ui-admin (admin-specific brand surface).
// Its tests live in packages/ui-admin/__tests__/components/utility/TSystemLogo.test.ts.
