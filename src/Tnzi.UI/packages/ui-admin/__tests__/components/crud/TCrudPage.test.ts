import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref, computed } from 'vue'
import TCrudPage from '../../../src/components/crud/TCrudPage.vue'

const stubs = {
  DataTable: {
    name: 'DataTable',
    props: ['data', 'columns', 'loading'],
    template: '<div class="n-data-table-stub" :data-rows="data.length"></div>',
  },
  Pagination: {
    name: 'Pagination',
    props: ['page', 'itemCount', 'pageSize'],
    emits: ['update:page', 'update:pageSize'],
    template: '<div class="n-pagination-stub"></div>',
  },
  Input: {
    name: 'Input',
    props: ['value'],
    emits: ['update:value'],
    template:
      '<input class="n-input-stub" :value="value" @input="$emit(\'update:value\', $event.target.value)" />',
  },
  Button: {
    name: 'Button',
    template: '<button @click="$emit(\'click\')"><slot /></button>',
  },
  Modal: {
    name: 'Modal',
    props: ['show'],
    emits: ['update:show'],
    template:
      '<div v-if="show" class="n-modal-stub"><slot /><slot name="footer" /></div>',
  },
  Popover: {
    name: 'Popover',
    props: ['show'],
    template: '<div><slot name="trigger" /><slot /></div>',
  },
  Checkbox: { name: 'Checkbox', template: '<input type="checkbox" />' },
  VueDraggable: { name: 'VueDraggable', template: '<div><slot /></div>' },
}

interface Row {
  id: number
  name: string
}

function makeState() {
  const query = ref({
    pageIndex: 1,
    pageSize: 20,
    searchText: '',
    sortField: undefined as string | undefined,
    sortOrder: null as 'asc' | 'desc' | null,
    filters: {} as Record<string, unknown>,
  })
  const items = ref<Row[]>([
    { id: 1, name: 'Alice' },
    { id: 2, name: 'Bob' },
  ])
  const total = ref(2)
  const loading = ref(false)
  const error = ref(null)
  const hasData = computed(() => items.value.length > 0)

  const visibleColumns = ref([
    { key: 'id', title: 'ID' },
    { key: 'name', title: 'Name' },
  ])
  const orderedKeys = ref(['id', 'name'])
  const hiddenKeys = ref<Set<string>>(new Set())

  const columnSettings = {
    visibleColumns,
    orderedKeys,
    hiddenKeys,
    hide: vi.fn(),
    show: vi.fn(),
    toggle: vi.fn(),
    reorder: vi.fn(),
    reset: vi.fn(),
  }

  const selected = ref<Set<number>>(new Set())
  const selectedIds = computed(() => Array.from(selected.value))
  const selectedCount = computed(() => selected.value.size)
  const hasSelection = computed(() => selected.value.size > 0)
  const batchActions = {
    selected,
    selectedIds,
    selectedCount,
    hasSelection,
    select: vi.fn(),
    unselect: vi.fn(),
    toggle: vi.fn(),
    selectAll: vi.fn(),
    clear: vi.fn(),
    isSelected: vi.fn(),
  }

  const formModal = {
    visible: ref(false),
    mode: ref<'create' | 'edit' | 'view' | null>(null),
    formData: ref<Row | null>(null),
    open: vi.fn(),
    close: vi.fn(),
    confirm: vi.fn(),
  }

  return {
    query,
    items,
    total,
    loading,
    error,
    hasData,
    columnSettings,
    batchActions,
    formModal,
    refresh: vi.fn(),
    setPage: vi.fn(),
    setPageSize: vi.fn(),
    setSearch: vi.fn(),
    setSort: vi.fn(),
    setFilters: vi.fn(),
    resetQuery: vi.fn(),
    openCreate: vi.fn(),
    openEdit: vi.fn(),
    openView: vi.fn(),
    submit: vi.fn(),
    handleDelete: vi.fn(),
    exportAll: vi.fn(),
    importFile: vi.fn(),
  }
}

describe('TCrudPage', () => {
  it('renders NDataTable with items from state', () => {
    const state = makeState()
    const wrapper = mount(TCrudPage, {
      props: { state: state as any, allColumns: state.columnSettings.visibleColumns.value },
      global: { stubs },
    })
    const table = wrapper.find('.n-data-table-stub')
    expect(table.exists()).toBe(true)
    expect(table.attributes('data-rows')).toBe('2')
  })

  it('search input calls state.setSearch on input', async () => {
    const state = makeState()
    const wrapper = mount(TCrudPage, {
      props: { state: state as any, allColumns: state.columnSettings.visibleColumns.value },
      global: { stubs },
    })
    const input = wrapper.find('.t-crud-page__search .n-input-stub')
    await input.setValue('query')
    expect(state.setSearch).toHaveBeenCalledWith('query')
  })

  it('primary Create button opens create modal', async () => {
    const state = makeState()
    const wrapper = mount(TCrudPage, {
      props: { state: state as any, allColumns: state.columnSettings.visibleColumns.value },
      global: { stubs },
    })
    await wrapper.find('.t-crud-page__create').trigger('click')
    expect(state.openCreate).toHaveBeenCalled()
  })

  it('toolbar refresh calls state.refresh()', async () => {
    const state = makeState()
    const wrapper = mount(TCrudPage, {
      props: { state: state as any, allColumns: state.columnSettings.visibleColumns.value },
      global: { stubs },
    })
    await wrapper.find('.t-crud-toolbar__refresh button').trigger('click')
    expect(state.refresh).toHaveBeenCalled()
  })

  it('renders form slot inside modal', async () => {
    const state = makeState()
    state.formModal.visible.value = true
    state.formModal.mode.value = 'create'
    const wrapper = mount(TCrudPage, {
      props: { state: state as any, allColumns: state.columnSettings.visibleColumns.value },
      global: { stubs },
      slots: { form: '<div class="form-body">F</div>' },
    })
    expect(wrapper.find('.form-body').exists()).toBe(true)
  })

  it('form modal submit calls state.submit()', async () => {
    const state = makeState()
    state.formModal.visible.value = true
    state.formModal.mode.value = 'create'
    const wrapper = mount(TCrudPage, {
      props: { state: state as any, allColumns: state.columnSettings.visibleColumns.value },
      global: { stubs },
    })
    await wrapper.find('.t-form-modal__confirm').trigger('click')
    expect(state.submit).toHaveBeenCalled()
  })

  it('hides primary button when showCreate=false', () => {
    const state = makeState()
    const wrapper = mount(TCrudPage, {
      props: {
        state: state as any,
        allColumns: state.columnSettings.visibleColumns.value,
        showCreate: false,
      },
      global: { stubs },
    })
    expect(wrapper.find('.t-crud-page__create').exists()).toBe(false)
  })
})
