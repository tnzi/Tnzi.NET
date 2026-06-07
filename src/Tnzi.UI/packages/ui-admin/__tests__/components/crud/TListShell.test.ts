import { describe, it, expect, vi, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref, computed } from 'vue'
import TListShell from '../../../src/components/crud/TListShell.vue'

vi.mock('../../../src/headless/useBreakpoint', () => ({
  useBreakpoint: () => ({
    width: ref(1280),
    isSm: ref((globalThis as any).__bpNarrow ?? false),
    isMd: ref(false),
    isLg: ref(false),
  }),
}))

const stubs = {
  Pagination: { name: 'Pagination', props: ['page', 'itemCount', 'pageSize'], template: '<div class="n-pagination-stub" />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show" class="n-modal-stub"><slot /><slot name="footer" /></div>' },
  Card: { name: 'Card', template: '<div class="n-card-stub"><slot name="header" /><slot name="header-extra" /><slot /></div>' },
  Popconfirm: { name: 'Popconfirm', template: '<div><slot name="trigger" /><slot /></div>' },
  Popover: { name: 'Popover', template: '<div><slot name="trigger" /><slot /></div>' },
  Alert: { name: 'Alert', props: ['title'], template: '<div class="n-alert-stub"><slot /></div>' },
  SvgIcon: true,
}

function makeState(overrides: Record<string, unknown> = {}) {
  const selected = ref<Set<number>>(new Set())
  return {
    query: ref({ pageIndex: 1, pageSize: 20, searchText: '', sortField: undefined, sortOrder: null, filters: {} }),
    items: ref([{ id: 1 }, { id: 2 }]),
    total: ref(2),
    loading: ref(false),
    error: ref<Error | null>(null),
    hasData: computed(() => true),
    columnSettings: { visibleColumns: ref([]), orderedKeys: ref([]), hiddenKeys: ref(new Set()), fixedOverrides: ref(new Map()), hide: vi.fn(), show: vi.fn(), toggle: vi.fn(), reorder: vi.fn(), cycleFixed: vi.fn(), getFixed: vi.fn(), reset: vi.fn() },
    batchActions: { selected, selectedIds: computed(() => [...selected.value]), selectedCount: computed(() => selected.value.size), hasSelection: computed(() => selected.value.size > 0), select: vi.fn(), unselect: vi.fn(), toggle: vi.fn(), selectAll: vi.fn(), clear: vi.fn(), isSelected: vi.fn() },
    formModal: { visible: ref(false), mode: ref<'create' | 'edit' | 'view' | null>(null), formData: ref(null), open: vi.fn(), close: vi.fn(), confirm: vi.fn() },
    rowKey: (r: { id: number }) => r.id,
    canCreate: true, canUpdate: true, canDelete: true,
    refresh: vi.fn(), setPage: vi.fn(), setPageSize: vi.fn(), setSearch: vi.fn(), setSort: vi.fn(), setFilters: vi.fn(), resetQuery: vi.fn(),
    openCreate: vi.fn(), openEdit: vi.fn(), openView: vi.fn(), submit: vi.fn(), handleDelete: vi.fn(), exportAll: vi.fn(), importFile: vi.fn(), dismissError: vi.fn(),
    ...overrides,
  }
}

describe('TListShell', () => {
  afterEach(() => { delete (globalThis as Record<string, unknown>).__bpNarrow })

  it('renders the #renderer slot body', () => {
    const wrapper = mount(TListShell, {
      props: { state: makeState() as any },
      slots: { renderer: '<div class="body-marker">B</div>' },
      global: { stubs },
    })
    expect(wrapper.find('.body-marker').exists()).toBe(true)
  })

  it('shows the create button when canCreate and showCreate, hides when canCreate=false', () => {
    const shown = mount(TListShell, { props: { state: makeState() as any }, slots: { renderer: '<div/>' }, global: { stubs } })
    expect(shown.find('.t-list-shell__create').exists()).toBe(true)
    const hidden = mount(TListShell, { props: { state: makeState({ canCreate: false }) as any }, slots: { renderer: '<div/>' }, global: { stubs } })
    expect(hidden.find('.t-list-shell__create').exists()).toBe(false)
  })

  it('does not mount the form modal when neither canCreate nor canUpdate', () => {
    const state = makeState({ canCreate: false, canUpdate: false })
    state.formModal.visible.value = true
    state.formModal.mode.value = 'view'
    const wrapper = mount(TListShell, { props: { state: state as any }, slots: { renderer: '<div/>', form: '<div class="form-body" />' }, global: { stubs } })
    expect(wrapper.find('.form-body').exists()).toBe(false)
  })

  it('hides pagination when showPagination=false', () => {
    const wrapper = mount(TListShell, { props: { state: makeState() as any, showPagination: false }, slots: { renderer: '<div/>' }, global: { stubs } })
    expect(wrapper.find('.n-pagination-stub').exists()).toBe(false)
  })

  it('applies container modifier class in container mode', () => {
    const wrapper = mount(TListShell, { props: { state: makeState() as any, mode: 'container' }, slots: { renderer: '<div/>' }, global: { stubs } })
    expect(wrapper.find('.t-list-shell--container').exists()).toBe(true)
  })

  it('refresh button calls state.refresh()', async () => {
    const state = makeState()
    const wrapper = mount(TListShell, { props: { state: state as any }, slots: { renderer: '<div/>' }, global: { stubs } })
    await wrapper.find('.t-list-shell__refresh').trigger('click')
    expect(state.refresh).toHaveBeenCalled()
  })

  it('renders the error banner with the error message when state.error is set', () => {
    const state = makeState({ error: ref(new Error('boom')) })
    const wrapper = mount(TListShell, { props: { state: state as any }, slots: { renderer: '<div/>' }, global: { stubs } })
    expect(wrapper.text()).toContain('boom')
  })

  it('renders the page title in a TPageHeader above the list', () => {
    const wrapper = mount(TListShell, { props: { state: makeState() as any, title: 'Roles' }, slots: { renderer: '<div/>' }, global: { stubs } })
    expect(wrapper.find('.t-page-header__title').text()).toBe('Roles')
  })

  it('still renders the create button (now inside the header actions)', () => {
    const wrapper = mount(TListShell, { props: { state: makeState() as any }, slots: { renderer: '<div/>' }, global: { stubs } })
    expect(wrapper.find('.t-list-shell__create').exists()).toBe(true)
  })

  it('shows the keyword search input at desktop width', () => {
    ;(globalThis as any).__bpNarrow = false
    const wrapper = mount(TListShell, { props: { state: makeState() as any }, slots: { renderer: '<div/>' }, global: { stubs } })
    expect(wrapper.find('.t-list-shell__search-input').exists()).toBe(true)
    expect(wrapper.find('.t-list-shell__search-icon').exists()).toBe(false)
  })

  it('collapses the keyword search to an icon below md', () => {
    ;(globalThis as any).__bpNarrow = true
    const wrapper = mount(TListShell, { props: { state: makeState() as any }, slots: { renderer: '<div/>' }, global: { stubs } })
    expect(wrapper.find('.t-list-shell__search-icon').exists()).toBe(true)
    expect(wrapper.find('.t-list-shell__search-input').exists()).toBe(false)
    ;(globalThis as any).__bpNarrow = false
  })

  it('opens the advanced drawer (no inline advanced panel) when searchFields are given', async () => {
    const drawerStubs = { ...stubs, Drawer: { name: 'Drawer', props: ['show'], template: '<div v-if="show" class="n-drawer-stub"><slot/></div>' }, DrawerContent: { name: 'DrawerContent', template: '<div><slot/><slot name="footer"/></div>' } }
    const wrapper = mount(TListShell, {
      props: { state: makeState() as any, searchFields: [{ key: 'name', label: 'Name', type: 'text' }] as any },
      slots: { renderer: '<div/>' },
      global: { stubs: drawerStubs },
    })
    expect(wrapper.find('.t-list-shell__advanced').exists()).toBe(false)
    await wrapper.find('.t-list-shell__adv-toggle').trigger('click')
    expect(wrapper.find('.n-drawer-stub').exists()).toBe(true)
  })

  // NOTE: the search toggles carry dynamic `:type`/`:tertiary` bindings (the
  // active-state highlight). @vue/test-utils' native `trigger('click')` does
  // NOT propagate a custom-stub's `$emit('click')` to the parent listener when
  // the stubbed component has dynamic props AND lives in a slot — so we drive
  // the wiring via the component's own `click` emit (what NButton fires on a
  // real tap). Browser-verified separately.
  it('phone: tapping the search toggle expands the keyword panel downward, tapping again collapses it', async () => {
    ;(globalThis as any).__bpNarrow = true
    const wrapper = mount(TListShell, { props: { state: makeState() as any }, slots: { renderer: '<div/>' }, global: { stubs } })
    // Collapsed by default — only the toggle icon, no panel.
    expect(wrapper.find('.t-list-shell__mobile-search').exists()).toBe(false)
    expect(wrapper.find('.t-list-shell__search-input').exists()).toBe(false)
    wrapper.findComponent('.t-list-shell__search-icon').vm.$emit('click')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.t-list-shell__mobile-search').exists()).toBe(true)
    expect(wrapper.find('.t-list-shell__mobile-field').exists()).toBe(true)
    wrapper.findComponent('.t-list-shell__search-icon').vm.$emit('click')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.t-list-shell__mobile-search').exists()).toBe(false)
    ;(globalThis as any).__bpNarrow = false
  })

  it('phone: Advanced expands the inline one-column form, not the right drawer', async () => {
    ;(globalThis as any).__bpNarrow = true
    const drawerStubs = { ...stubs, Drawer: { name: 'Drawer', props: ['show'], template: '<div v-if="show" class="n-drawer-stub"><slot/></div>' }, DrawerContent: { name: 'DrawerContent', template: '<div><slot/><slot name="footer"/></div>' } }
    const wrapper = mount(TListShell, {
      props: { state: makeState() as any, searchFields: [{ key: 'name', label: 'Name', type: 'text' }] as any },
      slots: { renderer: '<div/>' },
      global: { stubs: drawerStubs },
    })
    wrapper.findComponent('.t-list-shell__adv-toggle').vm.$emit('click')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.t-list-shell__mobile-search').exists()).toBe(true)
    expect(wrapper.find('.t-crud-search-advanced').exists()).toBe(true)
    // The right drawer is never mounted on phones.
    expect(wrapper.find('.n-drawer-stub').exists()).toBe(false)
    ;(globalThis as any).__bpNarrow = false
  })
})
