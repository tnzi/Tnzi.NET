import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { createThemeContext, mergeThemeSettings, type ThemeContext } from '@tnzi/ui'
import TThemeDrawer from '../../../src/components/layout/TThemeDrawer.vue'
import { useAdminThemeStore } from '../../../src/stores/useAdminThemeStore'

// vi.mock factory is hoisted before module init — keep stubs inline.
vi.mock('naive-ui', () => ({
  NDrawer: {
    name: 'NDrawer',
    props: ['show', 'width', 'placement'],
    emits: ['update:show'],
    template: '<div v-if="show" class="n-drawer-stub"><slot /></div>',
  },
  NDrawerContent: {
    name: 'NDrawerContent',
    props: ['title', 'closable'],
    template: '<div class="n-drawer-content-stub" :data-title="title"><slot /></div>',
  },
  NTabs: {
    name: 'NTabs',
    props: ['value', 'type', 'justifyContent', 'size'],
    emits: ['update:value'],
    template: '<div class="tabs-stub" :data-active="value"><slot /></div>',
  },
  NTab: {
    name: 'NTab',
    props: ['name'],
    template: '<div class="tab-stub" :data-name="name"><slot /></div>',
  },
  NTabPane: {
    name: 'NTabPane',
    props: ['name', 'tab'],
    template: '<div class="tab-pane-stub" :data-name="name" :data-tab="tab"><slot /></div>',
  },
  NColorPicker: {
    name: 'NColorPicker',
    props: ['value', 'modes', 'showAlpha', 'size'],
    emits: ['update:value'],
    template:
      '<div class="color-picker-stub" :data-value="value" @click="$emit(\'update:value\', \'#ff0000\')"></div>',
  },
  NRadioGroup: {
    name: 'NRadioGroup',
    props: ['value', 'size'],
    emits: ['update:value'],
    template: '<div class="radio-group" :data-value="value"><slot /></div>',
  },
  NRadioButton: {
    name: 'NRadioButton',
    props: ['value'],
    template: '<label class="radio-item" :data-value="value"><slot /></label>',
  },
  NSwitch: {
    name: 'NSwitch',
    props: ['value', 'disabled'],
    emits: ['update:value'],
    template:
      '<button class="switch" :data-value="value" :data-disabled="disabled" @click="$emit(\'update:value\', !value)"></button>',
  },
  NButton: {
    name: 'NButton',
    props: ['size', 'type', 'disabled'],
    template: '<button class="btn" :data-type="type" :disabled="disabled"><slot /></button>',
  },
  NSelect: {
    name: 'NSelect',
    props: ['value', 'options', 'size', 'disabled'],
    emits: ['update:value'],
    template:
      '<select class="select-stub" :data-value="value" :disabled="disabled" @change="$emit(\'update:value\', $event.target.value)"><option v-for="o in options" :key="o.value" :value="o.value">{{ o.label }}</option></select>',
  },
  NSlider: {
    name: 'NSlider',
    props: ['value', 'min', 'max', 'step', 'disabled'],
    emits: ['update:value'],
    template:
      '<input type="range" class="slider-stub" :value="value" :min="min" :max="max" :step="step" :disabled="disabled" @input="$emit(\'update:value\', Number($event.target.value))" />',
  },
  NInput: {
    name: 'NInput',
    props: ['value', 'type', 'rows', 'size', 'readonly', 'placeholder', 'disabled'],
    emits: ['update:value'],
    template:
      '<textarea class="input-stub" :value="value" :placeholder="placeholder" :readonly="readonly" :disabled="disabled" @input="$emit(\'update:value\', $event.target.value)"></textarea>',
  },
  NInputNumber: {
    name: 'NInputNumber',
    props: ['value', 'min', 'max', 'step', 'size', 'disabled'],
    emits: ['update:value'],
    template:
      '<input type="number" class="input-number-stub" :value="value" :min="min" :max="max" :disabled="disabled" @input="$emit(\'update:value\', Number($event.target.value))" />',
  },
  NPopconfirm: {
    name: 'NPopconfirm',
    props: ['positiveText'],
    emits: ['positiveClick'],
    template:
      '<div class="popconfirm-stub"><slot name="trigger" /><button class="popconfirm-positive" @click="$emit(\'positiveClick\')"></button><slot /></div>',
  },
  NWatermark: { name: 'NWatermark', template: '<div class="watermark-stub" />' },
  NMenu: { name: 'NMenu', template: '<div class="menu-stub" />' },
  NDivider: { name: 'NDivider', template: '<div class="divider-stub"><slot /></div>' },
  NTooltip: {
    name: 'NTooltip',
    props: ['placement', 'trigger'],
    template: '<div class="tooltip-stub"><slot name="trigger" /><slot /></div>',
  },
  useMessage: () => ({
    success: vi.fn(),
    warning: vi.fn(),
    error: vi.fn(),
    info: vi.fn(),
  }),
}))

// Stub @iconify/vue Icon — the drawer renders icons for color-mode tabs
// and the Preset palette active state, but tests don't care about visuals.
vi.mock('@iconify/vue', () => ({
  Icon: { name: 'Icon', props: ['icon'], template: '<i class="iconify-stub" :data-icon="icon" />' },
}))

// Stub TLayoutModeCard to a clickable button so we can drive selectLayoutMode in tests.
vi.mock('../../../src/components/layout/TLayoutModeCard.vue', () => ({
  default: defineComponent({
    name: 'TLayoutModeCard',
    props: ['mode', 'active', 'label'],
    emits: ['select'],
    setup(props, { emit }) {
      return () =>
        h(
          'button',
          {
            class: ['layout-mode-card', { active: props.active }],
            'data-mode': props.mode,
            onClick: () => emit('select', props.mode),
          },
          [props.label as string],
        )
    },
  }),
}))

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
    })
    expect(wrapper.find('.n-drawer-stub').exists()).toBe(false)
  })

  it('renders 4 tab panes when show=true (I.7.10: watermark merged into general)', () => {
    const wrapper = mount(TThemeDrawer, {
      props: { show: true, themeContext: createCtx() },
    })
    const panes = wrapper.findAll('.tab-pane-stub')
    expect(panes.length).toBe(4)
    const names = panes.map((p) => p.attributes('data-name'))
    expect(names).toEqual(['appearance', 'layout', 'general', 'preset'])
  })

  it('updates theme context primary color when palette card clicked', async () => {
    // Standalone swatches were folded into NColorPicker `:swatches` (soybean
    // parity) and the palette cards moved to the Preset tab as full-size
    // cards. We click an inner card directly via its class.
    const ctx = createCtx()
    const wrapper = mount(TThemeDrawer, {
      props: { show: true, themeContext: ctx },
    })
    const cards = wrapper.findAll('.t-theme-drawer__preset-card')
    expect(cards.length).toBe(12)
    await cards[2].trigger('click') // index 2 = '#7C3AED'
    expect(ctx.settings.value.colors.primary).toBe('#7C3AED')
  })

  it('updates themeStore.layoutMode when a layout card clicked', async () => {
    const store = useAdminThemeStore()
    expect(store.layoutMode).toBe('vertical')
    const wrapper = mount(TThemeDrawer, {
      props: { show: true, themeContext: createCtx() },
    })
    const horizontalCard = wrapper.find('[data-mode="horizontal"]')
    expect(horizontalCard.exists()).toBe(true)
    await horizontalCard.trigger('click')
    expect(store.layoutMode).toBe('horizontal')
  })

  it('renders all 6 layout mode cards', () => {
    const wrapper = mount(TThemeDrawer, {
      props: { show: true, themeContext: createCtx() },
    })
    const cards = wrapper.findAll('.layout-mode-card')
    expect(cards.length).toBe(6)
    const modes = cards.map((c) => c.attributes('data-mode'))
    expect(modes).toContain('vertical')
    expect(modes).toContain('horizontal')
    expect(modes).toContain('vertical-mix')
    expect(modes).toContain('vertical-hybrid-header-first')
    expect(modes).toContain('top-hybrid-sidebar-first')
    expect(modes).toContain('top-hybrid-header-first')
  })

  it('buildSnapshot exposes a v1 admin + ui shape', () => {
    const ctx = createCtx()
    const wrapper = mount(TThemeDrawer, {
      props: { show: true, themeContext: ctx },
    })
    const snapshot = (
      wrapper.vm as unknown as {
        buildSnapshot: () => { version: number; admin: Record<string, unknown>; ui: Record<string, unknown> }
      }
    ).buildSnapshot()
    expect(snapshot.version).toBe(1)
    expect(snapshot.admin.layoutMode).toBe('vertical')
    expect(snapshot.ui.mode).toBe('light')
    expect((snapshot.ui.colors as Record<string, string>).primary).toBe('#3b82f6')
  })

  it('resetAll calls both ctx.reset() and themeStore.reset()', () => {
    const ctx = createCtx()
    const store = useAdminThemeStore()
    const wrapper = mount(TThemeDrawer, {
      props: { show: true, themeContext: ctx },
    })
    const exposed = wrapper.vm as unknown as { resetAll: () => void }
    const ctxResetSpy = vi.spyOn(ctx, 'reset')
    const storeResetSpy = vi.spyOn(store, 'reset')
    exposed.resetAll()
    expect(ctxResetSpy).toHaveBeenCalled()
    expect(storeResetSpy).toHaveBeenCalled()
  })

  it('emits update:show(false) when close() invoked via exposed helper', () => {
    const wrapper = mount(TThemeDrawer, {
      props: { show: true, themeContext: createCtx() },
    })
    ;(wrapper.vm as unknown as { close: () => void }).close()
    expect(wrapper.emitted('update:show')).toBeTruthy()
    expect(wrapper.emitted('update:show')![0]).toEqual([false])
  })

  it('translate prop overrides default key passthrough', () => {
    const translate = vi.fn((k: string) => k.replace('admin.theme.', '#'))
    const wrapper = mount(TThemeDrawer, {
      props: { show: true, themeContext: createCtx(), translate },
    })
    const content = wrapper.find('.n-drawer-content-stub')
    expect(content.attributes('data-title')).toBe('#title')
  })
})
