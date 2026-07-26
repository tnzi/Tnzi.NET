import { describe, it, expect, afterEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import TDocumentCard from '../../../src/components/finance/TDocumentCard.vue'
import { FinanceDocumentStatus } from '../../../src/services/bridges/finance-bridge'

/**
 * `TDocumentCard` is the shared row for every finance document list (invoices /
 * bills / expenses / credit memos). It shipped without tests; these lock the two
 * things it can get wrong in a way a reader would believe: the overdue calendar
 * boundary and the "no figure" placeholder.
 */
function localDateOnly(offsetDays: number): string {
  const d = new Date()
  d.setDate(d.getDate() + offsetDays)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

function mountCard(row: Record<string, unknown>) {
  return mount(TDocumentCard, {
    props: { row, t: (k: string) => k },
    global: { stubs: { TSvgIcon: true } },
  })
}

const openInvoice = {
  status: FinanceDocumentStatus.Posted,
  number: 'INV-1',
  partyName: 'Acme',
  total: 100,
  appliedTotal: 0,
  currency: 'CAD',
}

describe('TDocumentCard overdue boundary', () => {
  afterEach(() => vi.useRealTimers())

  // Regression: the check used `Date.parse(dueDate) < Date.now()`. A backend
  // date-only value parses as UTC midnight, which is already in the past when
  // the local day begins west of UTC, so a document due TODAY was painted red.
  it('is not overdue on its due date', () => {
    const wrapper = mountCard({ ...openInvoice, dueDate: localDateOnly(0) })
    expect(wrapper.find('.fdc-meta__item--late').exists()).toBe(false)
  })

  it('is overdue once the due date is behind today', () => {
    const wrapper = mountCard({ ...openInvoice, dueDate: localDateOnly(-1) })
    expect(wrapper.find('.fdc-meta__item--late').exists()).toBe(true)
  })

  it('is not overdue for a future due date', () => {
    const wrapper = mountCard({ ...openInvoice, dueDate: localDateOnly(5) })
    expect(wrapper.find('.fdc-meta__item--late').exists()).toBe(false)
  })

  it('never marks a settled document overdue, however old', () => {
    const wrapper = mountCard({
      ...openInvoice,
      status: FinanceDocumentStatus.Paid,
      appliedTotal: 100,
      dueDate: localDateOnly(-90),
    })
    expect(wrapper.find('.fdc-meta__item--late').exists()).toBe(false)
  })
})

describe('TDocumentCard money placeholder', () => {
  // Regression: `total` used `?? 0`, so a row with no figure rendered a
  // confident 0.00. On a ledger "no figure" and "zero" are different claims.
  it('shows the money placeholder rather than 0.00 when there is no figure', () => {
    const wrapper = mountCard({
      status: FinanceDocumentStatus.Draft,
      number: 'INV-2',
      partyName: 'Acme',
      currency: 'CAD',
    })
    expect(wrapper.text()).not.toContain('0.00')
  })

  it('still renders a genuine zero', () => {
    const wrapper = mountCard({ ...openInvoice, total: 0, appliedTotal: 0 })
    expect(wrapper.text()).toContain('0.00')
  })
})
