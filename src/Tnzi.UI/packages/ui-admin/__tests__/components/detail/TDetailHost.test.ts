import { describe, it, expect, afterEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref, h, nextTick } from 'vue'
import { useFormHostRegistration } from '@tnzi/ui/headless'
import TDetailHost from '../../../src/components/detail/TDetailHost.vue'

const stubs = {
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show" class="n-modal-stub"><slot /><slot name="footer"/></div>' },
  Drawer: { name: 'Drawer', props: ['show'], template: '<div v-if="show" class="n-drawer-stub"><slot /></div>' },
  DrawerContent: { name: 'DrawerContent', template: '<div class="n-drawer-content-stub"><slot /><slot name="footer"/></div>' },
  Tabs: { name: 'Tabs', template: '<div><slot /></div>' },
  TabPane: { name: 'TabPane', template: '<div><slot /></div>' },
  Menu: true,
  Popover: { name: 'Popover', template: '<div><slot name="trigger" /><slot /></div>' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  SvgIcon: true,
}

function makeState(mode: 'modal' | 'drawer' | 'page', visible = true) {
  // `useDetail` builds its open-state on ONE `useFormModal` instance and
  // publishes `visible` / `action` / `data` as views onto it, so the fake shares
  // the same refs instead of keeping a second, independent copy.
  const form = {
    visible: ref(visible),
    mode: ref('edit'),
    formData: ref({ id: 1, name: 'a' }),
    open: () => {},
    close: () => {},
    confirm: async () => null,
  }
  return {
    mode: ref(mode), action: form.mode, visible: form.visible,
    data: form.formData, loading: ref(false), error: ref(null),
    activeSection: ref('basic'),
    open: async () => {}, close: () => {}, submit: async () => {}, setSection: () => {},
    form,
  }
}

describe('TDetailHost', () => {
  /**
   * The host owns the Confirm button in every mode, so it provides the form
   * host: a slotted `TSchemaForm` registers and the button validates before
   * `state.submit` runs. The `#footer` slot's `submit` is the same gated call.
   */
  describe('form host', () => {
    function slottedForm(valid: boolean) {
      const validate = vi.fn(async () => valid)
      const component = {
        setup() {
          useFormHostRegistration({ validate })
          return () => h('div', { class: 'slotted-form' })
        },
      }
      return { component, validate }
    }

    for (const mode of ['modal', 'drawer'] as const) {
      it(`${mode} mode: Confirm does not submit while a slotted form is invalid`, async () => {
        const state = makeState(mode)
        state.submit = vi.fn(async () => {})
        const form = slottedForm(false)
        const wrapper = mount(TDetailHost, {
          props: { state: state as any, title: 'Edit', footer: true },
          slots: { default: () => h(form.component) },
          global: { stubs },
        })
        const confirm = wrapper.findAll('button').at(-1)!
        await confirm.trigger('click')
        await nextTick()
        expect(form.validate).toHaveBeenCalled()
        expect(state.submit).not.toHaveBeenCalled()
      })
    }

    it('modal mode: Confirm submits once the slotted form validates', async () => {
      const state = makeState('modal')
      state.submit = vi.fn(async () => {})
      const form = slottedForm(true)
      const wrapper = mount(TDetailHost, {
        props: { state: state as any, title: 'Edit', footer: true },
        slots: { default: () => h(form.component) },
        global: { stubs },
      })
      await wrapper.findAll('button').at(-1)!.trigger('click')
      await nextTick()
      expect(state.submit).toHaveBeenCalled()
    })

    // Page mode renders a footer only through the slot, so its Confirm is
    // whatever the page puts there: the slot's `submit` has to be the gated one.
    it('page mode: the #footer slot receives the gated submit', async () => {
      const state = makeState('page')
      state.submit = vi.fn(async () => {})
      const form = slottedForm(false)
      let slotSubmit: (() => Promise<void>) | undefined
      mount(TDetailHost, {
        props: { state: state as any, title: 'Edit', footer: true },
        slots: {
          default: () => h(form.component),
          footer: (p: { submit: () => Promise<void> }) => {
            slotSubmit = p.submit
            return h('span')
          },
        },
        global: { stubs },
      })
      await slotSubmit!()
      expect(form.validate).toHaveBeenCalledTimes(1)
      expect(state.submit).not.toHaveBeenCalled()
    })
  })

  it('renders NModal in modal mode', () => {
    const w = mount(TDetailHost, { props: { state: makeState('modal') as any, title: 'Edit' }, slots: { default: '<div class="body" />' }, global: { stubs } })
    expect(w.find('.n-modal-stub').exists()).toBe(true)
    expect(w.find('.body').exists()).toBe(true)
  })

  it('renders NDrawer in drawer mode', () => {
    const w = mount(TDetailHost, { props: { state: makeState('drawer') as any, title: 'Edit' }, slots: { default: '<div class="body" />' }, global: { stubs } })
    expect(w.find('.n-drawer-stub').exists()).toBe(true)
  })

  it('renders bare TDetailLayout (no overlay) in page mode', () => {
    const w = mount(TDetailHost, { props: { state: makeState('page') as any, title: 'Edit' }, slots: { default: '<div class="body" />' }, global: { stubs } })
    expect(w.find('.n-modal-stub').exists()).toBe(false)
    expect(w.find('.n-drawer-stub').exists()).toBe(false)
    expect(w.find('.t-detail-layout').exists()).toBe(true)
  })

  it('suppresses the in-layout header in modal mode (overlay owns chrome)', () => {
    const w = mount(TDetailHost, { props: { state: makeState('modal') as any, title: 'Edit' }, slots: { default: '<div class="body" />' }, global: { stubs } })
    expect(w.find('.t-page-header').exists()).toBe(false)
  })

  it('shows the in-layout header in page mode', () => {
    const w = mount(TDetailHost, { props: { state: makeState('page') as any, title: 'Edit' }, slots: { default: '<div class="body" />' }, global: { stubs } })
    expect(w.find('.t-page-header').exists()).toBe(true)
  })

  it('forwards #extra (with the record) into the page-mode identity column', () => {
    const w = mount(TDetailHost, {
      props: { state: makeState('page') as any, title: 'Edit' },
      slots: {
        default: '<div class="body" />',
        extra: '<span class="meta">{{ params.data.name }}</span>',
      },
      global: { stubs },
    })
    expect(w.find('.t-page-header__main > .t-page-header__extra .meta').text()).toBe('a')
    expect(w.find('.t-page-header__left .meta').exists()).toBe(false)
  })
  /**
   * The overlay identity + body-height cap, run against the REAL shells.
   *
   * `TModalShell` withholds naive's `title` prop the moment a `#header` slot
   * exists (the prop would otherwise win and drop the slot with no warning), so
   * the thing worth asserting is what naive ENDS UP painting - a stub would
   * happily report a `#header` the real card never rendered. NModal teleports,
   * hence `attachTo` + document queries.
   */
  describe('overlay identity and height (real shells)', () => {
    let w: ReturnType<typeof mount> | null = null

    afterEach(() => {
      w?.unmount()
      w = null
      document.body.innerHTML = ''
    })

    function open(mode: 'modal' | 'drawer', props: Record<string, unknown> = {}, slots: Record<string, unknown> = {}) {
      w = mount(TDetailHost, {
        props: { state: makeState(mode) as any, title: 'Edit', ...props },
        slots: { default: '<div class="body" />', ...(slots as any) },
        attachTo: document.body,
        global: { stubs: { TSvgIcon: true } },
      })
      return w
    }

    const headerMain = () =>
      document.querySelector('.n-card-header__main, .n-drawer-header__main') as HTMLElement | null
    const scrollStyle = () =>
      (document.querySelector('.t-modal-shell__scroll') as HTMLElement | null)?.style.maxHeight

    /* The guard on every OTHER overlay in the framework: none of them declares
       an icon or a #title, and their header must keep the byte-for-byte plain
       string naive rendered before. Supplying #header unconditionally would
       re-route all ~40 of them through our own markup for no gain. */
    it('leaves a plain-titled overlay exactly as naive rendered it', () => {
      open('modal')
      expect(headerMain()?.textContent).toBe('Edit')
      expect(document.querySelector('.t-detail-host__header')).toBeNull()
    })

    it('composes the icon into the modal header, ahead of the title', () => {
      open('modal', { icon: 'mdi:police-badge' })
      const icon = document.querySelector('.t-detail-host__header-icon')
      expect(icon?.getAttribute('icon')).toBe('mdi:police-badge')
      expect(document.querySelector('.t-detail-host__header-title')?.textContent).toBe('Edit')
    })

    /* Same field, same rule, the other overlay - a capability that stops at one
       of the two modes is the defect this whole change is about. */
    it('composes the icon into the drawer header too', () => {
      open('drawer', { icon: 'mdi:police-badge' })
      expect(document.querySelector('.t-detail-host__header-icon')?.getAttribute('icon')).toBe(
        'mdi:police-badge',
      )
    })

    /* Mirrors TPageHeader: its #title slot REPLACES icon + title rather than
       sitting beside them, so the rule must not change with the mode. */
    it('lets #title own the whole identity, icon included', () => {
      open('modal', { icon: 'mdi:police-badge' }, { title: () => h('span', { class: 'rich' }, 'Officer Reid') })
      expect(document.querySelector('.rich')?.textContent).toBe('Officer Reid')
      expect(document.querySelector('.t-detail-host__header-icon')).toBeNull()
      expect(document.querySelector('.t-detail-host__header-title')).toBeNull()
    })

    it('hands #title the retained record, so a closing overlay keeps its name', () => {
      open('modal', {}, { title: (p: any) => h('span', { class: 'rich' }, p.data?.name ?? '') })
      expect(document.querySelector('.rich')?.textContent).toBe('a')
    })

    it('caps the modal body at TModalShell default, and at the value given', () => {
      open('modal')
      expect(scrollStyle()).toBe('65vh')
      w?.unmount()
      document.body.innerHTML = ''
      open('modal', { contentMaxHeightVh: 76 })
      expect(scrollStyle()).toBe('76vh')
    })
  })

  /* Page mode routes the same two inputs down its own path (TPageHeader), and
     must keep doing so - the overlay branches are additive, not a takeover. */
  it('still sends icon and #title through the page-mode header', () => {
    const w = mount(TDetailHost, {
      props: { state: makeState('page') as any, title: 'Edit', icon: 'mdi:police-badge' },
      slots: { default: '<div class="body" />' },
      global: { stubs },
    })
    expect(w.find('.t-page-header__icon').exists()).toBe(true)
    expect(w.find('.t-detail-host__header').exists()).toBe(false)
  })
})
