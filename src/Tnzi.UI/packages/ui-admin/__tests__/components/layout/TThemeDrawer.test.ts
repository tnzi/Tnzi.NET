import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createThemeContext, mergeThemeSettings, type ThemeContext } from '@tnzi/ui'
import TThemeDrawer from '../../../src/components/layout/TThemeDrawer.vue'
import { useAdminThemeStore } from '../../../src/stores/useAdminThemeStore'

const stubs = {
  Drawer: {
    name: 'Drawer',
    props: ['show', 'width', 'placement'],
    emits: ['update:show'],
    template: '<div v-if="show" class="n-drawer-stub"><slot /></div>',
  },
  DrawerContent: {
    name: 'DrawerContent',
    props: ['title', 'closable'],
    template: '<div class="n-drawer-content-stub"><slot /></div>',
  },
  ColorPicker: {
    name: 'ColorPicker',
    props: ['value'],
    emits: ['update:value'],
    template:
      '<div class="color-picker-stub" :data-value="value" @click="$emit(\'update:value\', \'#ff0000\')"></div>',
  },
  RadioGroup: {
    name: 'RadioGroup',
    props: ['value'],
    emits: ['update:value'],
    template: '<div class="radio-group" :data-value="value"><slot /></div>',
  },
  Radio: {
    name: 'Radio',
    props: ['value'],
    template: '<label class="radio-item" :data-value="value"><slot /></label>',
  },
  Switch: {
    name: 'Switch',
    props: ['value'],
    emits: ['update:value'],
    template:
      '<button class="switch" :data-value="value" @click="$emit(\'update:value\', !value)"></button>',
  },
  Button: {
    name: 'Button',
    template: '<button class="btn"><slot /></button>',
  },
}

function createCtx(): ThemeContext {
  return createThemeContext(
    mergeThemeSettings({
      colors: { primary: '#3b82f6' },
      mode: 'light',
    }),
  )
}

describe('TThemeDrawer', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('does not render drawer content when show=false', () => {
    const wrapper = mount(TThemeDrawer, {
      props: { show: false, themeContext: createCtx() },
      global: { stubs },
    })
    expect(wrapper.find('.n-drawer-stub').exists()).toBe(false)
  })

  it('renders sections when show=true', () => {
    const wrapper = mount(TThemeDrawer, {
      props: { show: true, themeContext: createCtx() },
      global: { stubs },
    })
    expect(wrapper.find('.n-drawer-stub').exists()).toBe(true)
    expect(wrapper.find('.color-picker-stub').exists()).toBe(true)
    expect(wrapper.find('.radio-group').exists()).toBe(true)
  })

  it('updates theme context primary color when color picker changes', async () => {
    const ctx = createCtx()
    const wrapper = mount(TThemeDrawer, {
      props: { show: true, themeContext: ctx },
      global: { stubs },
    })
    await wrapper.find('.color-picker-stub').trigger('click')
    expect(ctx.settings.value.colors.primary).toBe('#ff0000')
  })

  it('updates themeStore.layoutMode via setLayoutMode exposed helper', () => {
    const store = useAdminThemeStore()
    expect(store.layoutMode).toBe('vertical')
    const wrapper = mount(TThemeDrawer, {
      props: { show: true, themeContext: createCtx() },
      global: { stubs },
    })
    ;(wrapper.vm as unknown as { setLayoutMode: (m: 'horizontal') => void }).setLayoutMode(
      'horizontal',
    )
    expect(store.layoutMode).toBe('horizontal')
  })

  it('toggles dark mode when switch clicked', async () => {
    const ctx = createCtx()
    expect(ctx.isDark.value).toBe(false)
    const wrapper = mount(TThemeDrawer, {
      props: { show: true, themeContext: ctx },
      global: { stubs },
    })
    await wrapper.find('.switch').trigger('click')
    expect(ctx.settings.value.mode).toBe('dark')
  })

  it('reset button calls both ctx.reset() and themeStore.reset()', async () => {
    const ctx = createCtx()
    const store = useAdminThemeStore()
    const ctxReset = vi.spyOn(ctx, 'reset')
    const storeReset = vi.spyOn(store, 'reset')
    const wrapper = mount(TThemeDrawer, {
      props: { show: true, themeContext: ctx },
      global: { stubs },
    })
    await wrapper.find('.t-theme-drawer__reset').trigger('click')
    expect(ctxReset).toHaveBeenCalled()
    expect(storeReset).toHaveBeenCalled()
  })

  it('emits update:show(false) when close() invoked via exposed helper', () => {
    const wrapper = mount(TThemeDrawer, {
      props: { show: true, themeContext: createCtx() },
      global: { stubs },
    })
    ;(wrapper.vm as unknown as { close: () => void }).close()
    expect(wrapper.emitted('update:show')).toBeTruthy()
    expect(wrapper.emitted('update:show')![0]).toEqual([false])
  })
})
