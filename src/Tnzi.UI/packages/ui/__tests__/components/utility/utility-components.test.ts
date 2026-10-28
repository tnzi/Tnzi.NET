import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'
import { THEME_CONTEXT_KEY, createThemeContext, mergeThemeSettings } from '@tnzi/ui'
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

describe('TThemeSchemaSwitch', () => {
  beforeEach(() => {
    document.documentElement.classList.remove('dark')
  })

  it('cycles light → dark → auto → light and emits change', async () => {
    const wrapper = mount(TThemeSchemaSwitch, {
      props: { defaultValue: 'light', applyDocumentClass: false },
      global: { provide: themeProvide() },
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
      global: { provide: themeProvide() },
    })
    await wrapper.find('button').trigger('click')
    // dark → auto on first click
    expect(wrapper.emitted('change')?.[0]).toEqual(['auto'])
  })

  it('toggles `dark` class on html when applyDocumentClass=true and value=dark', async () => {
    const wrapper = mount(TThemeSchemaSwitch, {
      props: { defaultValue: 'dark', applyDocumentClass: true },
      global: { provide: themeProvide() },
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
        global: { provide: themeProvide() },
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
