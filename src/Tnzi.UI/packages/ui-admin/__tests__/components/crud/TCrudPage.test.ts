import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref, computed } from 'vue'
import TCrudPage from '../../../src/components/crud/TCrudPage.vue'

const stubs = {
  DataTable: {
    name: 'DataTable',
    props: ['data', 'columns', 'loading', 'pagination'],
    emits: ['update:page', 'update:pageSize'],
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
  // Phase I.7.x — TCrudPage now wraps the page in NSpace + NCard +
  // NCollapse. Stub these passthrough-style so child slot content
  // (search input, toolbar buttons, etc) keeps rendering for tests.
  Card: {
    name: 'Card',
    template:
      '<div class="n-card-stub"><div class="n-card__header-stub"><slot name="header" /><slot name="header-extra" /></div><slot /></div>',
  },
  Collapse: {
    name: 'Collapse',
    template: '<div class="n-collapse-stub"><slot /></div>',
  },
  CollapseItem: {
    name: 'CollapseItem',
    template:
      '<div class="n-collapse-item-stub"><slot name="header" /><slot /></div>',
  },
  Space: {
    name: 'Space',
    template: '<div class="n-space-stub"><slot /></div>',
  },
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

  const fixedOverrides = ref<Map<string, 'left' | 'right' | null>>(new Map())
  const columnSettings = {
    visibleColumns,
    orderedKeys,
    hiddenKeys,
    fixedOverrides,
    hide: vi.fn(),
    show: vi.fn(),
    toggle: vi.fn(),
    reorder: vi.fn(),
    cycleFixed: vi.fn(),
    getFixed: vi.fn(() => undefined),
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

  it('simple search commits keyword on Search button click', async () => {
    // Simple search now commits on Enter / Search-button click instead
    // of firing on every keystroke (avoids spamming refetches while typing).
    const state = makeState()
    const wrapper = mount(TCrudPage, {
      props: { state: state as any, allColumns: state.columnSettings.visibleColumns.value },
      global: { stubs },
    })
    const input = wrapper.find('.t-crud-page__search .n-input-stub')
    await input.setValue('query')
    expect(state.setSearch).not.toHaveBeenCalled()
    // Find the primary Search button — it's the only n-button stub with
    // text "Search" inside the search panel.
    const searchBtn = wrapper
      .findAll('.t-crud-page__search button')
      .find((b) => b.text() === 'admin.crud.search' || b.text().toLowerCase().includes('search'))
    expect(searchBtn).toBeTruthy()
    await searchBtn!.trigger('click')
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
    // Phase I.7.x: refresh is now a NButton inside the list-card actions
    // strip, with stable class `.t-crud-toolbar__refresh` so existing
    // selectors keep working. The Button stub renders a real <button>
    // child, so we still trigger the same DOM node.
    await wrapper.find('.t-crud-toolbar__refresh').trigger('click')
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
