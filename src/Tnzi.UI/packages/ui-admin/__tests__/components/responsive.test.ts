/**
 * Responsive integration tests — assert each P0/P1 fix actually fires
 * its width / touch-driven branch when window size flips.
 *
 * Strategy:
 *   1. Install a stub `window.matchMedia` that resolves min/max-width
 *      queries against `window.innerWidth`, and `(pointer: coarse)`
 *      against a per-test flag.
 *   2. Set the initial width before the component mounts so `useBreakpoint`'s
 *      `useBreakpoints()` snapshots the correct band.
 *   3. Mount the SFC with deep stubs so naive-ui internals don't slow the
 *      test down or assert their own visual contracts.
 */
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { ref, nextTick, defineComponent } from 'vue'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { __resetTouchProbeForTests } from '../../src/headless/useBreakpoint'

// Mock @tnzi/ui's useTheme so TDashboardPage's useEcharts dependency
// doesn't require a real plugin install. Stub returns the minimal
// surface useEcharts touches (settings.value.mode + resolvedMode).
vi.mock('@tnzi/ui', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@tnzi/ui')
  return {
    ...actual,
    useTheme: () => ({
      settings: { value: { mode: 'light', colors: { primary: '#646cff' } } },
      isDark: { value: false },
      resolvedMode: { value: 'light' },
      setColor: vi.fn(),
      setMode: vi.fn(),
      reset: vi.fn(),
      toggleTheme: vi.fn(),
    }),
  }
})

// Mock useDialog so TAdminUserAvatar mounts without an NDialogProvider.
vi.mock('naive-ui', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('naive-ui')
  return {
    ...actual,
    useDialog: () => ({
      info: vi.fn(),
      success: vi.fn(),
      warning: vi.fn(),
      error: vi.fn(),
    }),
    useMessage: () => ({
      info: vi.fn(),
      success: vi.fn(),
      warning: vi.fn(),
      error: vi.fn(),
    }),
  }
})

import TFormModal from '../../src/components/crud/TFormModal.vue'
import TGlobalSearch from '../../src/components/layout/TGlobalSearch.vue'
import TAdminHeader from '../../src/components/layout/TAdminHeader.vue'
import TDashboardPage from '../../src/components/pages/TDashboardPage.vue'
import TAdminUserAvatar from '../../src/components/layout/TAdminUserAvatar.vue'

function installMatchMedia(width: number, coarse = false): void {
  Object.defineProperty(window, 'innerWidth', { configurable: true, writable: true, value: width })
  Object.defineProperty(window, 'innerHeight', { configurable: true, writable: true, value: 768 })
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    writable: true,
    value: (query: string): MediaQueryList => {
      const parseMin = (q: string) => q.match(/min-width:\s*([\d.]+)px/)
      const parseMax = (q: string) => q.match(/max-width:\s*([\d.]+)px/)
      const mqlEval = (): boolean => {
        if (query.includes('pointer: coarse')) return coarse
        const min = parseMin(query)
        const max = parseMax(query)
        if (min && max) {
          return window.innerWidth >= Number(min[1]) && window.innerWidth <= Number(max[1])
        }
        if (min) return window.innerWidth >= Number(min[1])
        if (max) return window.innerWidth <= Number(max[1])
        return false
      }
      return {
        media: query,
        matches: mqlEval(),
        onchange: null,
        addEventListener: () => undefined,
        removeEventListener: () => undefined,
        addListener: () => undefined,
        removeListener: () => undefined,
        dispatchEvent: () => true,
      } as unknown as MediaQueryList
    },
  })
}

// vue-test-utils matches stub keys against the underlying component
// `name` field, NOT the `N`-prefixed import. naive-ui exports its
// components with names like `Modal`, `Card`, `Drawer`, `Grid`, `Gi`
// etc. The stub map must therefore use those un-prefixed names.
const naiveStubs = {
  Modal: defineComponent({
    name: 'Modal',
    props: ['show', 'style'],
    inheritAttrs: true,
    template: '<div class="n-modal-stub"><slot /></div>',
  }),
  Card: defineComponent({ template: '<div class="n-card-stub"><slot /></div>' }),
  Input: defineComponent({
    name: 'Input',
    props: ['value'],
    template: '<input class="n-input-stub" :value="value" />',
  }),
  Empty: defineComponent({ template: '<div class="n-empty-stub" />' }),
  Button: defineComponent({
    template: '<button class="n-button-stub"><slot /></button>',
  }),
  Tooltip: defineComponent({
    template: '<div class="n-tooltip-stub"><slot name="trigger" /></div>',
  }),
  Dropdown: defineComponent({
    name: 'Dropdown',
    props: ['options'],
    template: '<div class="n-dropdown-stub" :data-options-count="options?.length"><slot /></div>',
  }),
  Drawer: defineComponent({
    name: 'Drawer',
    props: ['width'],
    template: '<div class="n-drawer-stub" :data-width="width"><slot /></div>',
  }),
  DrawerContent: defineComponent({ template: '<div class="n-drawer-content-stub"><slot /></div>' }),
  Tabs: defineComponent({ template: '<div class="n-tabs-stub"><slot /></div>' }),
  Tab: defineComponent({ template: '<div><slot /></div>' }),
  TabPane: defineComponent({ template: '<div><slot /></div>' }),
  ColorPicker: defineComponent({ template: '<div class="n-color-picker-stub" />' }),
  Switch: defineComponent({ template: '<div class="n-switch-stub" />' }),
  InputNumber: defineComponent({ template: '<div class="n-input-number-stub" />' }),
  Select: defineComponent({ template: '<div class="n-select-stub" />' }),
  Popconfirm: defineComponent({ template: '<div><slot name="trigger" /></div>' }),
  Divider: defineComponent({ template: '<div><slot /></div>' }),
  Grid: defineComponent({
    name: 'Grid',
    template: '<div class="n-grid-stub"><slot /></div>',
  }),
  // naive-ui exports `NGi` but the internal component name is
  // `GridItem` — match that so the stub actually takes over.
  GridItem: defineComponent({
    name: 'GridItem',
    props: ['span'],
    template: '<div class="n-gi-stub" :data-span="span"><slot /></div>',
  }),
  TCountTo: defineComponent({ template: '<span class="t-count-to-stub" />' }),
  TSvgIcon: defineComponent({ template: '<span class="t-svg-icon-stub" />' }),
  Icon: defineComponent({ template: '<span class="iconify-stub" />' }),
}

describe('responsive integration', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    __resetTouchProbeForTests()
  })
  afterEach(() => {
    vi.restoreAllMocks()
  })

  describe('TFormModal width auto-adapts', () => {
    function makeState() {
      return {
        visible: ref(true),
        mode: ref('edit' as const),
        formData: ref({}),
        open: vi.fn(),
        close: vi.fn(),
        confirm: vi.fn(async () => ({})),
      } as unknown as Parameters<typeof TFormModal>[0]['state']
    }

    it('uses fullscreen class on a 375px viewport', () => {
      installMatchMedia(375)
      const wrapper = mount(TFormModal, {
        props: { state: makeState(), title: 'Edit', width: 560 },
        global: { stubs: naiveStubs },
      })
      const modal = wrapper.find('.n-modal-stub')
      const classes = (modal.attributes('class') ?? '') + (modal.attributes('data-fullscreen') ?? '')
      expect(modal.exists()).toBe(true)
      // The class binding flows through naive's wrapper; assert the modal
      // received the fullscreen class via the wrapper's classnames. The
      // fullscreen chrome now lives in the shared TModalShell primitive.
      expect(wrapper.html()).toContain('t-modal-shell--fullscreen')
      expect(classes.length).toBeGreaterThan(0)
    })

    it('stays at the configured width on a 1440px desktop', () => {
      installMatchMedia(1440)
      const wrapper = mount(TFormModal, {
        props: { state: makeState(), title: 'Edit', width: 560 },
        global: { stubs: naiveStubs },
      })
      expect(wrapper.html()).not.toContain('t-modal-shell--fullscreen')
    })
  })

  describe('TGlobalSearch fullscreens below 660', () => {
    it('marks fullscreen at 480px', () => {
      installMatchMedia(480)
      const wrapper = mount(TGlobalSearch, {
        props: { show: true },
        global: { stubs: naiveStubs },
      })
      expect(wrapper.html()).toContain('t-global-search--fullscreen')
    })
    it('does not fullscreen at 1024px', () => {
      installMatchMedia(1024)
      const wrapper = mount(TGlobalSearch, {
        props: { show: true },
        global: { stubs: naiveStubs },
      })
      expect(wrapper.html()).not.toContain('t-global-search--fullscreen')
    })
  })

  describe('TAdminHeader overflow menu', () => {
    it('renders ··· dropdown trigger on a 375px viewport', () => {
      installMatchMedia(375)
      const wrapper = mount(TAdminHeader, {
        global: { stubs: naiveStubs },
      })
      // overflow path emits an aria-label="More actions" button
      expect(wrapper.html()).toContain('aria-label="More actions"')
    })

    it('keeps inline buttons on a 1440px desktop', () => {
      installMatchMedia(1440)
      const wrapper = mount(TAdminHeader, {
        global: { stubs: naiveStubs },
      })
      expect(wrapper.html()).not.toContain('aria-label="More actions"')
      expect(wrapper.html()).toContain('aria-label="Search"')
    })

    it('hides fullscreen button on touch devices', () => {
      installMatchMedia(1024, true)
      const wrapper = mount(TAdminHeader, {
        global: { stubs: naiveStubs },
      })
      const html = wrapper.html()
      expect(html).not.toContain('t-admin-header__fullscreen')
    })
  })

  describe('TDashboardPage KPI span scales with kpi count', () => {
    it('emits 24/12/6 ladder for 4 KPIs', () => {
      installMatchMedia(1440)
      const wrapper = mount(TDashboardPage, {
        props: {
          kpis: [
            { key: 'a', title: 'A', value: 1, icon: 'mdi:foo' },
            { key: 'b', title: 'B', value: 2, icon: 'mdi:foo' },
            { key: 'c', title: 'C', value: 3, icon: 'mdi:foo' },
            { key: 'd', title: 'D', value: 4, icon: 'mdi:foo' },
          ],
        },
        global: { stubs: naiveStubs },
      })
      const spans = wrapper.findAll('.n-gi-stub').map((g) => g.attributes('data-span'))
      // First 4 GI elements are KPI cards
      expect(spans.slice(0, 4).every((s) => s === '24 s:12 m:6')).toBe(true)
    })
    it('emits 24/8 ladder for 3 KPIs', () => {
      installMatchMedia(1440)
      const wrapper = mount(TDashboardPage, {
        props: {
          kpis: [
            { key: 'a', title: 'A', value: 1, icon: 'mdi:foo' },
            { key: 'b', title: 'B', value: 2, icon: 'mdi:foo' },
            { key: 'c', title: 'C', value: 3, icon: 'mdi:foo' },
          ],
        },
        global: { stubs: naiveStubs },
      })
      const spans = wrapper.findAll('.n-gi-stub').map((g) => g.attributes('data-span'))
      expect(spans.slice(0, 3).every((s) => s === '24 s:24 m:8')).toBe(true)
    })
  })

  describe('TAdminUserAvatar mobile name hide', () => {
    it('always renders the name in DOM (display:none handles mobile hiding via CSS)', () => {
      installMatchMedia(375)
      const wrapper = mount(TAdminUserAvatar, {
        props: { userName: 'Long Display Name' },
        global: { stubs: naiveStubs },
      })
      // The name span remains in the DOM with the `t-admin-user-avatar__name` class
      // so JS can still read it; the media query in the scoped style hides it.
      expect(wrapper.find('.t-admin-user-avatar__name').exists()).toBe(true)
      expect(wrapper.find('.t-admin-user-avatar').attributes('title')).toBe('Long Display Name')
    })
  })
})

// Pinia is required by stores that TAdminHeader / TAdminUserAvatar consume.
afterEach(async () => {
  await nextTick()
})
