import { describe, it, expect, afterEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { h, nextTick } from 'vue'
import TResponsiveTable from '../../../src/components/data/TResponsiveTable.vue'

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

afterEach(() => setViewport(1024))

const data = [
  { id: 1, name: 'Alice', role: 'admin' },
  { id: 2, name: 'Bob', role: 'user' },
]

const onRevoke = vi.fn()
const columns = [
  { key: 'name', title: 'Name', width: 160 },
  { key: 'role', title: 'Role', width: 120 },
  {
    key: 'actions',
    title: 'Actions',
    width: 100,
    render: (row: Record<string, unknown>) =>
      h('button', { class: 'revoke', onClick: () => onRevoke(row.id) }, 'Revoke'),
  },
]

const stubs = {
  DataTable: {
    name: 'DataTable',
    props: ['data', 'columns', 'loading', 'scrollX', 'rowProps', 'summary'],
    template: '<div class="n-data-table-stub" :data-cols="columns.length" :data-scrollx="String(scrollX)" :data-has-row-props="String(!!rowProps)" :data-has-summary="String(!!summary)" />',
  },
}

describe('TResponsiveTable', () => {
  it('renders NDataTable on desktop and forwards every column', () => {
    setViewport(1280)
    const w = mount(TResponsiveTable, {
      props: { columns, data, rowKey: (r: Record<string, unknown>) => r.id as number },
      global: { stubs },
    })
    expect(w.find('.n-data-table-stub').exists()).toBe(true)
    expect(w.find('.n-data-table-stub').attributes('data-cols')).toBe('3')
  })

  it('switches to cards on mobile and moves the action column into the footer', async () => {
    setViewport(375)
    const w = mount(TResponsiveTable, {
      props: { columns, data, rowKey: (r: Record<string, unknown>) => r.id as number },
      global: { stubs },
    })
    await nextTick()
    expect(w.find('.n-data-table-stub').exists()).toBe(false)
    expect(w.findAll('.t-data-cards__card')).toHaveLength(2)
    // action column is NOT a label/value row…
    const labels = w.findAll('.t-data-cards__card')[0].findAll('.t-data-cards__label').map((n) => n.text())
    expect(labels).toEqual(['Role'])
    // …it's in the card footer
    expect(w.findAll('.t-data-cards__actions .revoke')).toHaveLength(2)
  })

  it('fires the action handler from the mobile card footer', async () => {
    setViewport(375)
    const w = mount(TResponsiveTable, {
      props: { columns, data, rowKey: (r: Record<string, unknown>) => r.id as number },
      global: { stubs },
    })
    await nextTick()
    await w.findAll('.t-data-cards__actions .revoke')[1].trigger('click')
    expect(onRevoke).toHaveBeenCalledWith(2)
  })

  it('honours mobile="scroll" by keeping the table on phones', async () => {
    setViewport(375)
    const w = mount(TResponsiveTable, {
      props: { columns, data, mobile: 'scroll', rowKey: (r: Record<string, unknown>) => r.id as number },
      global: { stubs },
    })
    await nextTick()
    expect(w.find('.n-data-table-stub').exists()).toBe(true)
    expect(w.find('.t-data-cards').exists()).toBe(false)
  })

  it('forwards row-props and summary to NDataTable on desktop', () => {
    setViewport(1280)
    const w = mount(TResponsiveTable, {
      props: {
        columns, data,
        rowKey: (r: Record<string, unknown>) => r.id as number,
        rowProps: () => ({ onClick: () => undefined }),
        summary: () => ({ role: { value: 'total' } }),
      },
      global: { stubs },
    })
    const table = w.find('.n-data-table-stub')
    expect(table.attributes('data-has-row-props')).toBe('true')
    expect(table.attributes('data-has-summary')).toBe('true')
  })

  it('applies row-props to mobile cards (clickable affordance + handler fires)', async () => {
    setViewport(375)
    const onRowClick = vi.fn()
    const w = mount(TResponsiveTable, {
      props: {
        columns, data,
        rowKey: (r: Record<string, unknown>) => r.id as number,
        rowProps: (row: Record<string, unknown>) => ({ onClick: () => onRowClick(row.id) }),
      },
      global: { stubs },
    })
    await nextTick()
    const cards = w.findAll('.t-data-cards__card')
    expect(cards[0].classes()).toContain('t-data-cards__card--clickable')
    await cards[1].trigger('click')
    expect(onRowClick).toHaveBeenCalledWith(2)
  })

  it('keeps footer actions from also firing the row click on mobile', async () => {
    setViewport(375)
    const onRowClick = vi.fn()
    const w = mount(TResponsiveTable, {
      props: {
        columns, data,
        rowKey: (r: Record<string, unknown>) => r.id as number,
        rowProps: () => ({ onClick: onRowClick }),
      },
      global: { stubs },
    })
    await nextTick()
    await w.findAll('.t-data-cards__actions .revoke')[0].trigger('click')
    expect(onRevoke).toHaveBeenCalledWith(1)
    expect(onRowClick).not.toHaveBeenCalled()
  })

  it('renders the summary as a totals card at the bottom on mobile', async () => {
    setViewport(375)
    const w = mount(TResponsiveTable, {
      props: {
        columns, data,
        rowKey: (r: Record<string, unknown>) => r.id as number,
        summary: (pageData: Record<string, unknown>[]) => ({
          name: { value: `${pageData.length} rows` },
          role: { value: h('strong', { class: 'sum-role' }, '1 admin') },
        }),
      },
      global: { stubs },
    })
    await nextTick()
    const summary = w.find('.t-data-cards__card--summary')
    expect(summary.exists()).toBe(true)
    expect(summary.text()).toContain('2 rows')
    expect(summary.find('.sum-role').text()).toBe('1 admin')
  })

  it('renders a simple pager on mobile when pagination is an object', async () => {
    setViewport(375)
    const onUpdatePage = vi.fn()
    const w = mount(TResponsiveTable, {
      props: {
        columns, data,
        rowKey: (r: Record<string, unknown>) => r.id as number,
        pagination: { page: 1, pageSize: 10, itemCount: 2, onUpdatePage, onUpdatePageSize: vi.fn() },
      },
      global: { stubs },
    })
    await nextTick()
    expect(w.find('.t-responsive-table__pager').exists()).toBe(true)
  })
})
