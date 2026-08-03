import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TReportTable from '../../src/components/data/TReportTable.vue'

/**
 * 合计行的正确性。
 *
 * ★ 这一条是**会产出错误数字**的那类缺陷：合计原本只对传进来的那一页求和，而在
 * 服务端分页的报表上，那是「当前页的合计」——它看起来和「报表的合计」一模一样，
 * 所以没有人会去质疑它。
 *
 * 缺陷由消费应用（Contoso）在拒绝采用本组件时指出：它的账龄与往来方分类账都从
 * 服务端取整个报表的合计，采纳当时的实现会安静地把数字换成页合计。
 */
describe('TReportTable totals', () => {
  const columns = [
    { key: 'name', title: 'Name' },
    { key: 'amount', title: 'Amount', total: true },
  ]
  const rows = [
    { name: 'A', amount: 10 },
    { name: 'B', amount: 20 },
  ]

  const stubs = {
    TResponsiveTable: {
      name: 'TResponsiveTable',
      props: ['columns', 'data', 'summary', 'rowProps'],
      template: '<div class="rt-stub" />',
    },
  }

  /** 取出 summary 函数并对给定页求值。 */
  function summaryFor(wrapper: ReturnType<typeof mount>, page: unknown[]) {
    const stub = wrapper.findComponent({ name: 'TResponsiveTable' })
    const fn = stub.props('summary') as
      | ((data: readonly unknown[]) => Record<string, { value: unknown }>)
      | undefined
    return fn ? fn(page) : undefined
  }

  /** 单元格的值是 `h('strong', text)` 的 VNode —— 取它的文本 children。 */
  function cellText(cell: { value: unknown } | undefined): string {
    const vnode = cell?.value as { children?: unknown } | undefined
    return String(vnode?.children ?? '')
  }

  it('sums the rows when the table is NOT paged', () => {
    // 全量表格里，页就是全部，本地求和是正确答案。
    const wrapper = mount(TReportTable, { props: { columns, rows }, global: { stubs } })

    const summary = summaryFor(wrapper, rows)
    expect(summary).toBeDefined()
    expect(cellText(summary!.amount)).toContain('30')
  })

  it('prefers authoritative totals over the local sum', () => {
    // 服务端说整个报表是 999，页上只有 30。必须印 999。
    const wrapper = mount(TReportTable, {
      props: { columns, rows, totals: { amount: 999 } },
      global: { stubs },
    })

    const summary = summaryFor(wrapper, rows)
    expect(cellText(summary!.amount)).toContain('999')
    expect(cellText(summary!.amount)).not.toContain('30')
  })

  it('suppresses the totals row on a paged table with no authoritative totals', () => {
    // ★ 核心断言：宁可不显示，也不显示一个读起来像报表合计的页合计。
    const wrapper = mount(TReportTable, {
      props: { columns, rows },
      attrs: { remote: true, pagination: { page: 1, pageCount: 5 } },
      global: { stubs },
    })

    const stub = wrapper.findComponent({ name: 'TResponsiveTable' })
    expect(stub.props('summary')).toBeUndefined()
  })

  it('shows the totals row on a paged table once totals are supplied', () => {
    const wrapper = mount(TReportTable, {
      props: { columns, rows, totals: { amount: 999 } },
      attrs: { remote: true, pagination: { page: 1, pageCount: 5 } },
      global: { stubs },
    })

    const summary = summaryFor(wrapper, rows)
    expect(summary).toBeDefined()
    expect(cellText(summary!.amount)).toContain('999')
  })

  it('forwards unknown attributes to the underlying table', () => {
    // 服务端分页的报表要 remote / pagination / loading / scroll-x / row-key，
    // 组件不透传就等于用不了。
    const wrapper = mount(TReportTable, {
      props: { columns, rows },
      attrs: { remote: true, loading: true, 'scroll-x': 1200 },
      global: { stubs },
    })

    const stub = wrapper.findComponent({ name: 'TResponsiveTable' })
    expect(stub.attributes('loading')).toBeDefined()
    expect(stub.attributes('scroll-x')).toBe('1200')
  })

  it('renders no totals row at all when no column asks for one', () => {
    const wrapper = mount(TReportTable, {
      props: { columns: [{ key: 'name', title: 'Name' }], rows },
      global: { stubs },
    })

    expect(wrapper.findComponent({ name: 'TResponsiveTable' }).props('summary')).toBeUndefined()
  })
})
