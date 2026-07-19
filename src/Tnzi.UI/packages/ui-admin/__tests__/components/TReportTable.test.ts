import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import type { VNode } from 'vue'
import TReportTable from '../../src/components/data/TReportTable.vue'

const stub = {
  name: 'TResponsiveTable',
  props: {
    columns: {},
    data: {},
    summary: { type: Function },
    rowProps: { type: Function },
    size: {},
    bordered: {},
  },
  template: '<div class="rt" />',
}
const mountRT = (props: Record<string, unknown>) =>
  mount(TReportTable, { props, global: { stubs: { TResponsiveTable: stub } } })

describe('TReportTable', () => {
  it('builds naive columns: money columns right-aligned + formatted', () => {
    const w = mountRT({
      columns: [{ key: 'name', title: 'Name' }, { key: 'amount', title: 'Amount', money: true }],
      rows: [{ name: 'A', amount: 12.5 }],
    })
    const cols = w.findComponent({ name: 'TResponsiveTable' }).props('columns') as Array<Record<string, unknown>>
    expect(cols[0]!.align).toBe('left')
    expect(cols[1]!.align).toBe('right')
    expect((cols[1]!.render as (r: unknown) => string)({ name: 'A', amount: 12.5 })).toBe('12.50')
  })

  it('provides a summary that labels + sums the total columns', () => {
    const w = mountRT({
      columns: [{ key: 'name', title: 'Name' }, { key: 'amt', title: 'Amt', total: true }],
      rows: [{ name: 'A', amt: 10 }, { name: 'B', amt: 5 }],
    })
    const summary = w.findComponent({ name: 'TResponsiveTable' }).props('summary') as (
      d: readonly unknown[],
    ) => Record<string, { value: VNode }>
    const row = summary([{ name: 'A', amt: 10 }, { name: 'B', amt: 5 }])
    expect(row.name!.value.children).toBe('Total')
    expect(row.amt!.value.children).toBe('15.00')
  })

  it('no summary when no total columns and showTotals not set', () => {
    const w = mountRT({ columns: [{ key: 'name', title: 'N' }], rows: [{ name: 'A' }] })
    expect(w.findComponent({ name: 'TResponsiveTable' }).props('summary')).toBeUndefined()
  })

  it('emits row-click when clickable', () => {
    const w = mountRT({ columns: [{ key: 'name', title: 'N' }], rows: [{ name: 'A' }], clickable: true })
    const rp = w.findComponent({ name: 'TResponsiveTable' }).props('rowProps') as (r: unknown) => { onClick: () => void }
    rp({ name: 'A' }).onClick()
    expect(w.emitted('row-click')?.[0]?.[0]).toEqual({ name: 'A' })
  })
})
