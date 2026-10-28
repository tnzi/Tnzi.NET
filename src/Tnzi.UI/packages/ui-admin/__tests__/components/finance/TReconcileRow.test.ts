import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TReconcileRow from '../../../src/components/finance/TReconcileRow.vue'
import { BankFeedDocType } from '../../../src/services/bridges/finance-bridge'

/**
 * An expense records money going out. The backend refuses to create one for a
 * deposit (it would post the deposit as money out), so the row must neither
 * preselect a rule's Expense suggestion for money in nor offer Expense at all.
 */
function mountRow(amount: number, suggestedDocType?: string) {
  return mount(TReconcileRow, {
    props: {
      txn: {
        id: `t${amount}`,
        accountId: 'gl-bank',
        txnDate: '2026-03-10',
        amount,
        currency: 'USD',
        description: 'AMAZON',
        status: 'Pending',
        suggestedDocType,
      } as never,
      candidates: [],
      expenseAccountOptions: [],
      fundsAccountOptions: [],
      customerOptions: [],
      vendorOptions: [],
      t: (key: string) => key,
    },
  })
}

interface RowVm {
  docType: string
  docTypeOptions: Array<{ value: string; disabled?: boolean }>
}

describe('TReconcileRow', () => {
  it('does not preselect an Expense suggestion for a deposit and disables Expense', () => {
    const vm = mountRow(50, BankFeedDocType.Expense).vm as unknown as RowVm

    expect(vm.docType).toBe(BankFeedDocType.PaymentEntry)
    expect(vm.docTypeOptions.find((o) => o.value === BankFeedDocType.Expense)?.disabled).toBe(true)
  })

  it('keeps an Expense suggestion for money going out', () => {
    const vm = mountRow(-50, BankFeedDocType.Expense).vm as unknown as RowVm

    expect(vm.docType).toBe(BankFeedDocType.Expense)
    expect(vm.docTypeOptions.find((o) => o.value === BankFeedDocType.Expense)?.disabled).toBe(false)
  })
})
