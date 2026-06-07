import { describe, it, expect, vi, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref, computed, nextTick } from 'vue'
import TTableRenderer from '../../../src/components/crud/renderers/TTableRenderer.vue'

const stubs = {
  DataTable: {
    name: 'DataTable',
    props: ['data', 'columns', 'loading', 'flexHeight', 'scrollX'],
    template: '<div class="n-data-table-stub" :data-rows="data.length" :data-flex="String(flexHeight)" :data-cols="columns.length" :data-scrollx="String(scrollX)" :data-aligns="columns.map(c => c.align || \'\').join(\',\')" />',
  },
}

/** Install a matchMedia + innerWidth stub so `useBreakpoint` resolves to a
 *  given viewport width. Tailwind `md` = 768px → below that is the card band. */
function setViewport(width: number): void {
  Object.defineProperty(window, 'innerWidth', { configurable: true, writable: true, value: width })
  Object.defineProperty(window, 'innerHeight', { configurable: true, writable: true, value: 768 })
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    writable: true,
    value: (query: string): MediaQueryList => {
      const min = query.match(/min-width:\s*([\d.]+)px/)
      const max = query.match(/max-width:\s*([\d.]+)px/)
      const matches =
        min && max
          ? window.innerWidth >= Number(min[1]) && window.innerWidth <= Number(max[1])
          : min
            ? window.innerWidth >= Number(min[1])
            : max
              ? window.innerWidth <= Number(max[1])
              : false
      return {
        media: query, matches, onchange: null,
        addEventListener: () => undefined, removeEventListener: () => undefined,
        addListener: () => undefined, removeListener: () => undefined, dispatchEvent: () => true,
      } as unknown as MediaQueryList
    },
  })
}

afterEach(() => {
  setViewport(1024)
})

function makeState() {
  const selected = ref<Set<number>>(new Set())
  return {
    query: ref({ pageIndex: 1, pageSize: 20, searchText: '', sortField: undefined, sortOrder: null, filters: {} }),
    items: ref([{ id: 1, name: 'A' }, { id: 2, name: 'B' }]),
    total: ref(2),
    loading: ref(false),
    columnSettings: { visibleColumns: ref([{ key: 'id', title: 'ID' }, { key: 'name', title: 'Name' }]), getFixed: vi.fn(() => undefined) },
    batchActions: {
      selectedIds: computed(() => [...selected.value]),
      selectAll: vi.fn(),
      toggle: vi.fn((id: number) => { selected.value.has(id) ? selected.value.delete(id) : selected.value.add(id) }),
    },
    rowKey: (r: { id: number }) => r.id,
  }
}

describe('TTableRenderer', () => {
  it('passes items and visible columns to NDataTable', () => {
    const wrapper = mount(TTableRenderer, {
      props: { state: makeState() as any },
      global: { stubs },
    })
    const table = wrapper.find('.n-data-table-stub')
    expect(table.attributes('data-rows')).toBe('2')
    // 2 visible columns + 1 selection column (showSelection default true)
    expect(table.attributes('data-cols')).toBe('3')
  })

  it('enables flex-height in page mode and disables it in container mode', () => {
    const page = mount(TTableRenderer, { props: { state: makeState() as any, mode: 'page' }, global: { stubs } })
    expect(page.find('.n-data-table-stub').attributes('data-flex')).toBe('true')
    const container = mount(TTableRenderer, { props: { state: makeState() as any, mode: 'container' }, global: { stubs } })
    expect(container.find('.n-data-table-stub').attributes('data-flex')).toBe('false')
  })

  it('omits the selection column when showSelection=false', () => {
    const wrapper = mount(TTableRenderer, { props: { state: makeState() as any, showSelection: false }, global: { stubs } })
    expect(wrapper.find('.n-data-table-stub').attributes('data-cols')).toBe('2')
  })

  it('applies the page modifier class in page mode (drives flex-fill) and the container class in container mode', () => {
    // Regression guard: without `.t-table-renderer--page { flex:1 1 auto; min-height:0 }`
    // the table collapses to header height under TListShell's flex column and
    // the pagination rides up beneath the header.
    const page = mount(TTableRenderer, { props: { state: makeState() as any, mode: 'page' }, global: { stubs } })
    expect(page.find('.t-table-renderer--page').exists()).toBe(true)
    const container = mount(TTableRenderer, { props: { state: makeState() as any, mode: 'container' }, global: { stubs } })
    expect(container.find('.t-table-renderer--page').exists()).toBe(false)
    expect(container.find('.t-table-renderer--container').exists()).toBe(true)
  })

  it('forwards column align to NDataTable', () => {
    const state = makeState()
    state.columnSettings.visibleColumns.value = [
      { key: 'id', title: 'ID', align: 'right' },
      { key: 'name', title: 'Name' },
    ] as any
    const wrapper = mount(TTableRenderer, { props: { state: state as any }, global: { stubs } })
    // selection col (no align) + id(right) + name('') → data-aligns contains 'right'
    expect(wrapper.find('.n-data-table-stub').attributes('data-aligns')).toContain('right')
  })

  it('collapses to the stacked card list below the md breakpoint', async () => {
    setViewport(375)
    const wrapper = mount(TTableRenderer, { props: { state: makeState() as any }, global: { stubs } })
    await nextTick()
    expect(wrapper.find('.t-data-cards').exists()).toBe(true)
    expect(wrapper.find('.n-data-table-stub').exists()).toBe(false)
    expect(wrapper.findAll('.t-data-cards__card')).toHaveLength(2)
  })

  it('keeps the data table at desktop width and applies scroll-x for wide tables', () => {
    setViewport(1280)
    const state = makeState()
    state.columnSettings.visibleColumns.value = [
      { key: 'a', title: 'A', width: 200 }, { key: 'b', title: 'B', width: 200 },
      { key: 'c', title: 'C', width: 200 }, { key: 'd', title: 'D', width: 200 },
    ] as any
    const wrapper = mount(TTableRenderer, { props: { state: state as any }, global: { stubs } })
    expect(wrapper.find('.n-data-table-stub').exists()).toBe(true)
    // 40 (selection) + 800 (4×200) = 840 > 640 → scroll-x applied
    expect(wrapper.find('.n-data-table-stub').attributes('data-scrollx')).toBe('840')
  })

  it('honours disableMobileCards by keeping the table even on a phone width', async () => {
    setViewport(375)
    const wrapper = mount(TTableRenderer, {
      props: { state: makeState() as any, disableMobileCards: true },
      global: { stubs },
    })
    await nextTick()
    expect(wrapper.find('.n-data-table-stub').exists()).toBe(true)
    expect(wrapper.find('.t-data-cards').exists()).toBe(false)
  })
})
