import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { createThemeContext, mergeThemeSettings, type ThemeContext } from '@tnzi/ui'
import TThemeDrawer from '../../../src/components/layout/TThemeDrawer.vue'
import { useAdminThemeStore } from '../../../src/stores/useAdminThemeStore'

// vi.mock factory is hoisted before module init - keep stubs inline.
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
    template:
      '<div class="n-drawer-content-stub" :data-title="title"><slot /><slot name="footer" /></div>',
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
  NPopover: {
    name: 'NPopover',
    props: ['trigger', 'placement', 'showArrow'],
    template: '<div class="popover-stub"><slot name="trigger" /><slot /></div>',
  },
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

// Stub @iconify/vue Icon - the drawer renders icons for color-mode tabs
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

  it('applies a full appearance look when a Preset-tab look card is clicked', async () => {
    // The Preset tab offers full appearance presets (a complete look, not just
    // a primary swatch). Clicking the "aubergine" look applies its accent + the
    // unified aubergine chrome (sider + header).
    const ctx = createCtx()
    const store = useAdminThemeStore()
    store.setCardBg('#eeeeee') // a stale override the look must clear (aubergine sets no cardBg)
    const wrapper = mount(TThemeDrawer, {
      props: { show: true, themeContext: ctx },
    })
    const cards = wrapper.findAll('.t-theme-drawer__look-card')
    expect(cards.length).toBe(18) // built-in curated looks
    // Locate by aria-label so the test survives roster reordering.
    const aubergine = wrapper.find('[aria-label="admin.theme.preset.looks.aubergine"]')
    expect(aubergine.exists()).toBe(true)
    await aubergine.trigger('click')
    expect(ctx.settings.value.colors.primary).toBe('#611F69')
    expect(store.invertSider).toBe(true)
    expect(store.siderBg).toBe('#3F0E40') // Slack's aubergine sider
    expect(store.headerBg).toBe('#3F0E40') // unified chrome: header joins the sider
    // The look defines the whole surface set, so it clears the stale card override.
    expect(store.cardBg).toBeNull()
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

  it('renders all 4 layout mode cards', () => {
    const wrapper = mount(TThemeDrawer, {
      props: { show: true, themeContext: createCtx() },
    })
    const cards = wrapper.findAll('.layout-mode-card')
    expect(cards.length).toBe(4)
    const modes = cards.map((c) => c.attributes('data-mode'))
    expect(modes).toContain('vertical')
    expect(modes).toContain('horizontal')
    expect(modes).toContain('vertical-mix')
    expect(modes).toContain('top-hybrid-header-first')
    // The two buggy hybrid modes were removed (2026-06-26).
    expect(modes).not.toContain('vertical-hybrid-header-first')
    expect(modes).not.toContain('top-hybrid-sidebar-first')
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

// ─── Presets mode + global-theme controller (2026-07-07) ────────────────────

import { ref, computed } from 'vue'
import type { GlobalThemeController } from '../../../src/headless/useGlobalTheme'

function fakeController(overrides: Partial<GlobalThemeController> = {}): GlobalThemeController {
  return {
    enabled: true,
    remote: ref(null),
    loaded: ref(true),
    saving: ref(false),
    isDirty: computed(() => false),
    load: vi.fn(async () => undefined),
    applyRemote: vi.fn(),
    save: vi.fn(async () => true),
    reset: vi.fn(async () => true),
    ...overrides,
  }
}

describe('TThemeDrawer - presets mode', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('renders the whole-look grid (no tabs) with the appearance title', () => {
    const wrapper = mount(TThemeDrawer, {
      props: { show: true, themeContext: createCtx(), mode: 'presets' },
    })
    expect(wrapper.findAll('.tab-pane-stub').length).toBe(0)
    // Non-privileged users now pick a WHOLE look (same 18 built-in looks the
    // admin sees), not just a color swatch.
    expect(wrapper.findAll('.t-theme-drawer__look-card').length).toBe(18)
    expect(wrapper.find('.n-drawer-content-stub').attributes('data-title')).toBe(
      'admin.theme.userPreset.title',
    )
  })

  it('picking a look records userPresetLook and applies the whole preset', async () => {
    const ctx = createCtx()
    const store = useAdminThemeStore()
    const wrapper = mount(TThemeDrawer, {
      props: { show: true, themeContext: ctx, mode: 'presets' },
    })
    const aubergine = wrapper.find('[aria-label="admin.theme.preset.looks.aubergine"]')
    await aubergine.trigger('click')
    expect(store.userPresetLook).toBe('aubergine')
    expect(store.siderBg).toBe('#3F0E40') // the look's surface applied, not just a color
    expect(ctx.settings.value.colors.primary).toBe('#611F69')
  })

  it('the default look clears the personal choice and re-applies the global snapshot', async () => {
    const store = useAdminThemeStore()
    store.setUserPresetLook('nord')
    const controller = fakeController()
    controller.remote.value = { version: 1 } as never // a global snapshot exists
    const wrapper = mount(TThemeDrawer, {
      props: { show: true, themeContext: createCtx(), mode: 'presets', globalTheme: controller },
    })
    await wrapper.findAll('.t-theme-drawer__look-card')[0].trigger('click') // [0] = 'default'
    expect(store.userPresetLook).toBeNull()
    expect(controller.applyRemote).toHaveBeenCalled()
  })

  it('renders no footer (reset/save are privileged-only surfaces)', () => {
    const wrapper = mount(TThemeDrawer, {
      props: { show: true, themeContext: createCtx(), mode: 'presets', globalTheme: fakeController() },
    })
    expect(wrapper.find('.t-theme-drawer__footer').exists()).toBe(false)
  })
})

describe('TThemeDrawer - global theme (full mode)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('renders the "save for all users" footer action and delegates to the controller', async () => {
    const controller = fakeController()
    const wrapper = mount(TThemeDrawer, {
      props: { show: true, themeContext: createCtx(), globalTheme: controller },
    })
    const saveBtn = wrapper
      .findAll('.t-theme-drawer__footer .btn')
      .find((b) => b.text().includes('admin.theme.global.save'))
    expect(saveBtn).toBeTruthy()
    await saveBtn!.trigger('click')
    expect(controller.save).toHaveBeenCalled()
  })

  it('resetAll persists the factory snapshot globally (other clients only see saved snapshots)', async () => {
    const controller = fakeController()
    const store = useAdminThemeStore()
    store.setLayoutMode('horizontal')
    const wrapper = mount(TThemeDrawer, {
      props: { show: true, themeContext: createCtx(), globalTheme: controller },
    })
    await (wrapper.vm as unknown as { resetAll: () => Promise<void> }).resetAll()
    expect(store.layoutMode).toBe('vertical')
    expect(controller.save).toHaveBeenCalled()
    expect(controller.reset).not.toHaveBeenCalled()
  })

  it('general tab exposes the preset-picker visibility switch', async () => {
    const store = useAdminThemeStore()
    const wrapper = mount(TThemeDrawer, {
      props: { show: true, themeContext: createCtx() },
    })
    expect(store.presetPickerVisible).toBe(true)
    // The switch sits right after the reload-visibility toggle in the
    // General → Global group; drive the store setter through it.
    const generalPane = wrapper.find('.tab-pane-stub[data-name="general"]')
    const rows = generalPane.findAll('.t-theme-drawer__row')
    const row = rows.find((r) => r.text().includes('admin.theme.general.presetPickerVisible'))
    expect(row).toBeTruthy()
    await row!.find('.switch').trigger('click')
    expect(store.presetPickerVisible).toBe(false)
  })
})
