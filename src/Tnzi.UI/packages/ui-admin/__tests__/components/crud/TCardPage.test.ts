import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref, computed } from 'vue'
import TCardPage from '../../../src/components/crud/TCardPage.vue'

const stubs = {
  Pagination: { name: 'Pagination', props: ['page', 'itemCount', 'pageSize'], template: '<div class="n-pagination-stub" />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show" class="n-modal-stub"><slot /></div>' },
  Card: { name: 'Card', template: '<div class="n-card-stub"><slot name="header" /><slot name="header-extra" /><slot /></div>' },
  Popconfirm: { name: 'Popconfirm', template: '<div><slot name="trigger" /><slot /></div>' },
  Popover: { name: 'Popover', template: '<div><slot name="trigger" /><slot /></div>' },
  Alert: { name: 'Alert', template: '<div><slot /></div>' },
}

function makeState() {
  const selected = ref<Set<number>>(new Set())
  return {
    query: ref({ pageIndex: 1, pageSize: 20, searchText: '', sortField: undefined, sortOrder: null, filters: {} }),
    items: ref([{ id: 1, name: 'Alice' }, { id: 2, name: 'Bob' }]),
    total: ref(2),
    loading: ref(false),
    error: ref(null),
    hasData: computed(() => true),
    columnSettings: { visibleColumns: ref([]), orderedKeys: ref([]), hiddenKeys: ref(new Set()), fixedOverrides: ref(new Map()), hide: vi.fn(), show: vi.fn(), toggle: vi.fn(), reorder: vi.fn(), cycleFixed: vi.fn(), getFixed: vi.fn(), reset: vi.fn() },
    batchActions: { selected, selectedIds: computed(() => [...selected.value]), selectedCount: computed(() => selected.value.size), hasSelection: computed(() => selected.value.size > 0), select: vi.fn(), unselect: vi.fn(), toggle: vi.fn(), selectAll: vi.fn(), clear: vi.fn(), isSelected: vi.fn(() => false) },
    formModal: { visible: ref(false), mode: ref(null), formData: ref(null), open: vi.fn(), close: vi.fn(), confirm: vi.fn() },
    rowKey: (r: { id: number }) => r.id,
    canCreate: false, canUpdate: false, canDelete: false,
    refresh: vi.fn(), setPage: vi.fn(), setPageSize: vi.fn(), setSearch: vi.fn(), setSort: vi.fn(), setFilters: vi.fn(), resetQuery: vi.fn(),
    openCreate: vi.fn(), openEdit: vi.fn(), openView: vi.fn(), submit: vi.fn(), handleDelete: vi.fn(), exportAll: vi.fn(), importFile: vi.fn(), dismissError: vi.fn(),
  }
}

describe('TCardPage', () => {
  it('renders the #card slot for each item through the shell', () => {
    const wrapper = mount(TCardPage, {
      props: { state: makeState() as any, title: 'Cards' },
      slots: { card: '<div class="my-card">{{ params.item.name }}</div>' },
      global: { stubs },
    })
    expect(wrapper.findAll('.my-card')).toHaveLength(2)
    // slot scope actually receives the item through the shell
    expect(wrapper.text()).toContain('Alice')
    expect(wrapper.text()).toContain('Bob')
  })

  it('hides create button for a display-only state (canCreate=false)', () => {
    const wrapper = mount(TCardPage, {
      props: { state: makeState() as any },
      slots: { card: '<div />' },
      global: { stubs },
    })
    expect(wrapper.find('.t-list-shell__create').exists()).toBe(false)
  })

  it('forwards show-header=false to the shell (no white header card)', () => {
    const wrapper = mount(TCardPage, {
      props: { state: makeState() as any, title: 'Cards', showHeader: false },
      slots: { card: '<div />' },
      global: { stubs },
    })
    expect(wrapper.find('.t-list-shell__header-card').exists()).toBe(false)
    expect(wrapper.find('.t-page-header__title').exists()).toBe(false)
  })

  it('renders the shell header card by default', () => {
    const wrapper = mount(TCardPage, {
      props: { state: makeState() as any, title: 'Cards' },
      slots: { card: '<div />' },
      global: { stubs },
    })
    expect(wrapper.find('.t-list-shell__header-card').exists()).toBe(true)
  })
})
