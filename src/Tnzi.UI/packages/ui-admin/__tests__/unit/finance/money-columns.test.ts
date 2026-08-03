import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { h, defineComponent, type VNodeChild } from 'vue'

import { moneyPairColumns } from '../../../src/components/finance/money-columns'
import { translatePageKey } from '../../../src/i18n/translate'
import { en } from '../../../src/locales/en'
import { zhCn } from '../../../src/locales/zh-cn'

/**
 * The rule this primitive exists to hold: the two headings are chosen by what
 * the view shows, never by who is looking at it. See the module doc comment.
 */

interface Line {
  debit: number
  credit: number
}

const t = (key: string) => translatePageKey('finance.reports', key)

function render(node: VNodeChild) {
  const Host = defineComponent({ render: () => node })
  return mount(Host).text()
}

describe('money-direction vocabulary', () => {
  it('is a single dictionary entry per language, so the wording cannot fork', () => {
    expect(en.admin.shared.moneyFlow).toEqual({ in: 'Money in', out: 'Money out', any: 'Money in or out' })
    // 进账 / 出账, not 收入 / 支出 (collides with the P&L: a bank deposit is not
    // necessarily income) and not 钱进 / 钱出 (too colloquial to print).
    expect(zhCn.admin.shared.moneyFlow).toEqual({ in: '进账', out: '出账', any: '进出账不限' })
    expect(zhCn.admin.shared.ledger).toEqual({ debit: '借方', credit: '贷方' })
  })
})

describe('moneyPairColumns', () => {
  it('names the sides Debit / Credit for a ledger view', () => {
    const [debit, credit] = moneyPairColumns<Line>({
      presentation: 'ledger',
      translate: t,
      debit: (r) => r.debit,
      credit: (r) => r.credit,
    })
    expect(debit?.title).toBe('Debit')
    expect(credit?.title).toBe('Credit')
  })

  it('names the sides Money in / Money out for a single-account flow view', () => {
    const [debit, credit] = moneyPairColumns<Line>({
      presentation: 'flow',
      translate: t,
      debit: (r) => r.debit,
      credit: (r) => r.credit,
    })
    expect(debit?.title).toBe('Money in')
    expect(credit?.title).toBe('Money out')
  })

  it('resolves the headings from the shared dictionary, not a page namespace', () => {
    // Any page's translator must produce the same words - the whole point of
    // parking the vocabulary in `admin.shared.*` is that a page cannot fork it.
    const other = (key: string) => translatePageKey('finance.reconciliations', key)
    const fromReports = moneyPairColumns<Line>({ presentation: 'flow', translate: t, debit: (r) => r.debit, credit: (r) => r.credit })
    const fromRecon = moneyPairColumns<Line>({ presentation: 'flow', translate: other, debit: (r) => r.debit, credit: (r) => r.credit })
    expect(fromRecon[0]?.title).toBe(fromReports[0]?.title)
    expect(fromRecon[1]?.title).toBe(fromReports[1]?.title)
  })

  it('falls back to English when no translator is supplied', () => {
    const [debit, credit] = moneyPairColumns<Line>({
      presentation: 'flow',
      debit: (r) => r.debit,
      credit: (r) => r.credit,
    })
    expect(debit?.title).toBe('Money in')
    expect(credit?.title).toBe('Money out')
  })

  it('renders through TMoney so negatives keep an accessible minus sign', () => {
    const [debit] = moneyPairColumns<Line>({
      presentation: 'flow',
      translate: t,
      debit: (r) => r.debit,
      credit: (r) => r.credit,
    })
    const column = debit as { render: (row: Line) => VNodeChild }
    const wrapper = mount(defineComponent({ render: () => h('div', [column.render({ debit: -42, credit: 0 })]) }))
    // Visible glyph uses accounting parentheses…
    expect(wrapper.text()).toContain('(42.00)')
    // …while the accessible name carries a real sign, which parentheses do not.
    expect(wrapper.find('[aria-label]').attributes('aria-label')).toContain('-42.00')
  })

  it('renders zero as the empty placeholder rather than 0.00 by default', () => {
    const [, credit] = moneyPairColumns<Line>({
      presentation: 'flow',
      translate: t,
      debit: (r) => r.debit,
      credit: (r) => r.credit,
    })
    const column = credit as { render: (row: Line) => VNodeChild }
    expect(render(column.render({ debit: 0, credit: 0 }))).not.toContain('0.00')
  })
})
