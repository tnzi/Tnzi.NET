import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { h } from 'vue'
import TDataCardList, {
  type CardColumn,
} from '../../../src/components/data/TDataCardList.vue'

const rows = [
  { id: 1, name: 'Alice', email: 'a@x.com', status: 'active' },
  { id: 2, name: 'Bob', email: 'b@x.com', status: 'locked' },
]

const columns: CardColumn[] = [
  { key: 'name', title: 'Name', primary: true },
  { key: 'email', title: 'Email' },
  { key: 'status', title: 'Status', render: (r) => h('em', { class: 'badge' }, String(r.status)) },
]

function mountList(extra: Record<string, unknown> = {}, slots: Record<string, unknown> = {}) {
  return mount(TDataCardList, {
    props: {
      items: rows,
      columns,
      rowKey: (r: Record<string, unknown>) => r.id as number,
      ...extra,
    },
    slots,
  })
}

describe('TDataCardList', () => {
  it('renders one card per row', () => {
    const w = mountList()
    expect(w.findAll('.t-data-cards__card')).toHaveLength(2)
  })

  it('promotes the primary column to the card title and lists the rest as label/value', () => {
    const w = mountList()
    const first = w.findAll('.t-data-cards__card')[0]
    expect(first.find('.t-data-cards__title').text()).toContain('Alice')
    const labels = first.findAll('.t-data-cards__label').map((n) => n.text())
    // Title column (name) is excluded from the body fields.
    expect(labels).toEqual(['Email', 'Status'])
  })

  it('falls back to the first column as title when none is flagged primary', () => {
    const w = mountList({
      columns: [
        { key: 'email', title: 'Email' },
        { key: 'name', title: 'Name' },
      ],
    })
    expect(w.findAll('.t-data-cards__card')[0].find('.t-data-cards__title').text()).toContain('a@x.com')
  })

  it('uses a column render fn for the cell value (badges/links survive)', () => {
    const w = mountList()
    expect(w.find('.t-data-cards__value .badge').text()).toBe('active')
  })

  it('omits columns flagged hidden', () => {
    const w = mountList({
      columns: [
        { key: 'name', title: 'Name', primary: true },
        { key: 'email', title: 'Email', hidden: true },
        { key: 'status', title: 'Status' },
      ],
    })
    const labels = w.findAll('.t-data-cards__card')[0].findAll('.t-data-cards__label').map((n) => n.text())
    expect(labels).toEqual(['Status'])
  })

  it('shows the selection control and emits toggle with the row key', async () => {
    const w = mountList({ showSelection: true, selectedKeys: [] })
    const btn = w.findAll('.t-data-cards__check')[1]
    await btn.trigger('click')
    expect(w.emitted('toggle')?.[0]).toEqual([2])
  })

  it('marks selected rows', () => {
    const w = mountList({ showSelection: true, selectedKeys: [1] })
    expect(w.findAll('.t-data-cards__card')[0].classes()).toContain('t-data-cards__card--selected')
    expect(w.findAll('.t-data-cards__card')[1].classes()).not.toContain('t-data-cards__card--selected')
  })

  it('renders the actions slot in the card footer', () => {
    const w = mountList({}, { actions: '<button class="act">Edit</button>' })
    expect(w.findAll('.t-data-cards__actions .act')).toHaveLength(2)
  })

  it('renders an empty state with no items', () => {
    const w = mountList({ items: [] })
    expect(w.find('.t-data-cards__empty').exists()).toBe(true)
    expect(w.findAll('.t-data-cards__card')).toHaveLength(0)
  })

  it('renders skeletons while loading with no items yet', () => {
    const w = mountList({ items: [], loading: true })
    expect(w.findAll('.t-data-cards__skeleton').length).toBeGreaterThan(0)
    expect(w.find('.t-data-cards__empty').exists()).toBe(false)
  })
})
