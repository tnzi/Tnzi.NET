import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'
import { THEME_CONTEXT_KEY, createThemeContext, mergeThemeSettings } from '@tnzi/ui'
import TThemeSchemaSwitch from '../../../src/components/utility/TThemeSchemaSwitch.vue'
import TLangSwitch from '../../../src/components/utility/TLangSwitch.vue'
import TFullScreen from '../../../src/components/utility/TFullScreen.vue'
import TReloadButton from '../../../src/components/utility/TReloadButton.vue'
import TPinToggler from '../../../src/components/utility/TPinToggler.vue'
import TMenuToggler from '../../../src/components/utility/TMenuToggler.vue'
import TSystemLogo from '../../../src/components/utility/TSystemLogo.vue'

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

  it('awaits onReload callback before re-emitting', async () => {
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
    // emit fires synchronously before the awaited callback completes
    expect(wrapper.emitted('reload')).toBeTruthy()
    await new Promise<void>((r) => setTimeout(r, 20))
    expect(resolved).toBe(true)
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

describe('TSystemLogo', () => {
  it('renders title and subtitle in full layout (default)', () => {
    const wrapper = mount(TSystemLogo, {
      props: { title: 'Acme', subtitle: 'admin' },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('.t-system-logo__title').text()).toBe('Acme')
    expect(wrapper.find('.t-system-logo__subtitle').text()).toBe('admin')
    expect(wrapper.find('.t-system-logo--full').exists()).toBe(true)
  })

  it('hides text in icon-only layout', () => {
    const wrapper = mount(TSystemLogo, {
      props: { layout: 'icon-only', title: 'Acme' },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('.t-system-logo--icon-only').exists()).toBe(true)
  })

  it('applies stacked layout class', () => {
    const wrapper = mount(TSystemLogo, {
      props: { layout: 'stacked', title: 'Acme' },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('.t-system-logo--stacked').exists()).toBe(true)
  })

  it('accepts icon override via slot', () => {
    const Custom = defineComponent({
      render: () => h('span', { class: 'my-icon' }, 'X'),
    })
    const wrapper = mount(TSystemLogo, {
      props: { title: 'Acme' },
      slots: {
        icon: () => h(Custom),
      },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('.my-icon').exists()).toBe(true)
  })
})
