import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { NDataTable, NPagination } from 'naive-ui'
import TTable from '../../../src/components/data/TTable.vue'

function rows(count: number, offset = 0) {
  return Array.from({ length: count }, (_, i) => ({
    id: String(offset + i + 1),
    name: `row-${offset + i + 1}`,
  }))
}

const columns = [{ key: 'name', title: 'Name', sortable: true }]

describe('TTable - server-side pagination contract', () => {
  it('shows the server page and total, not a local slice of the rows it was given', async () => {
    // Page 2 of a 500-row dataset: the consumer hands over only the 20 rows of that page.
    const wrapper = mount(TTable, {
      props: {
        data: rows(20, 20),
        columns,
        pagination: { pageIndex: 2, pageSize: 20, total: 500 },
      },
    })
    await wrapper.vm.$nextTick()

    const pagination = wrapper.findComponent(NPagination)
    expect(pagination.exists()).toBe(true)
    expect(pagination.props('page')).toBe(2)
    expect(pagination.props('itemCount')).toBe(500)
    expect(wrapper.findComponent(NDataTable).props('remote')).toBe(true)
  })

  it('emits pageChange for a page beyond the rows it holds', async () => {
    const wrapper = mount(TTable, {
      props: {
        data: rows(20),
        columns,
        pagination: { pageIndex: 1, pageSize: 20, total: 500 },
      },
    })
    await wrapper.vm.$nextTick()

    const pageThree = wrapper.findAll('.n-pagination-item').find((el) => el.text() === '3')
    expect(pageThree, 'pager must offer page 3 of 25').toBeDefined()
    await pageThree!.trigger('click')

    expect(wrapper.emitted('pageChange')).toEqual([[3, 20]])
  })

  it('emits sort and leaves the row order to the server', async () => {
    const data = [
      { id: '1', name: 'zeta' },
      { id: '2', name: 'alpha' },
    ]
    const wrapper = mount(TTable, {
      props: {
        data,
        columns,
        pagination: { pageIndex: 1, pageSize: 20, total: 2 },
      },
    })
    await wrapper.vm.$nextTick()

    await wrapper.find('.n-data-table-sorter').trigger('click')
    await wrapper.vm.$nextTick()

    // naive's first click on an unsorted column asks for descend; the direction is
    // forwarded, the rows are not reordered locally.
    expect(wrapper.emitted('sort')).toEqual([['name', 'desc']])
    const cells = wrapper.findAll('.n-data-table-td').map((td) => td.text())
    expect(cells).toEqual(['zeta', 'alpha'])
  })

  it('renders every row it is given when pagination is off', () => {
    const wrapper = mount(TTable, {
      props: { data: rows(3), columns, pagination: false },
    })
    expect(wrapper.findAll('.n-data-table-tbody .n-data-table-tr')).toHaveLength(3)
    expect(wrapper.findComponent(NPagination).exists()).toBe(false)
  })
})
