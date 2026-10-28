import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import type { VNode } from 'vue'
import TReportTable from '../../src/components/data/TReportTable.vue'
import { EMPTY_DASH } from '../../src/utils/placeholders'

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

  it('formats money the way every other money cell does: parenthesised credits, a dash for blanks', () => {
    // A blank amount printed as 0.00 reads as "nothing owed"; a credit printed
    // as -500.00 disagrees with every TMoney cell, which prints (500.00).
    const w = mountRT({
      columns: [{ key: 'name', title: 'Name' }, { key: 'amount', title: 'Amount', money: true }],
      rows: [],
    })
    const render = (w.findComponent({ name: 'TResponsiveTable' }).props('columns') as Array<Record<string, unknown>>)[1]!
      .render as (r: unknown) => string
    expect(render({ amount: -500 })).toBe('(500.00)')
    expect(render({ amount: 1234.5 })).toBe('1,234.50')
    expect(render({ amount: null })).toBe(EMPTY_DASH)
    expect(render({})).toBe(EMPTY_DASH)
    expect(render({ amount: 0 })).toBe('0.00')
  })

  it('hands a custom formatMoney the number, as before', () => {
    const w = mountRT({
      columns: [{ key: 'amount', title: 'Amount', money: true }],
      rows: [],
      formatMoney: (v: number) => `$${v}`,
    })
    const render = (w.findComponent({ name: 'TResponsiveTable' }).props('columns') as Array<Record<string, unknown>>)[0]!
      .render as (r: unknown) => string
    expect(render({ amount: -5 })).toBe('$-5')
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
