import { describe, it, expect } from 'vitest'
import { JournalEntryStatus } from '../../../src/services/bridges/finance-bridge'
import { entryTotal, type JournalRow } from '../../../src/pages/finance/journal-entry-config'

/**
 * The entry list shows one amount per side. Which field carries it depends on status, because
 * the backend fills two different fields for two different reasons - see JournalEntryDto.
 */
describe('journal entry list totals', () => {
  const draft: JournalRow = {
    status: JournalEntryStatus.Draft,
    currency: 'USD',
    // Base-currency totals are 0 on a draft BY DESIGN: the entity only denormalizes them at
    // posting, and a draft has no exchange rate yet, so the base amount does not exist.
    totalDebit: 0,
    totalCredit: 0,
    txnTotalDebit: 100,
    txnTotalCredit: 100,
  }

  const posted: JournalRow = {
    status: JournalEntryStatus.Posted,
    currency: 'USD',
    totalDebit: 100,
    totalCredit: 100,
    txnTotalDebit: 100,
    txnTotalCredit: 100,
  }

  it('shows a draft its transaction-currency total, not the base-currency zero', () => {
    // The defect this guards: reading totalDebit rendered $0.00 for every draft in the list -
    // hiding the one thing a reader opens a draft to check, which is whether it balances.
    expect(entryTotal(draft, 'debit')).toBe(100)
    expect(entryTotal(draft, 'credit')).toBe(100)
  })

  it('shows a posted entry its base-currency total', () => {
    expect(entryTotal(posted, 'debit')).toBe(100)
    expect(entryTotal(posted, 'credit')).toBe(100)
  })

  it('keeps a foreign-currency draft in its own currency rather than restating it', () => {
    // 1000 EUR at no rate yet. The txn total is the honest number; there is no base-currency
    // figure to show, and inventing one would be a fabricated amount on an accounting screen.
    const fx: JournalRow = { ...draft, currency: 'EUR', txnTotalDebit: 1000, txnTotalCredit: 1000 }
    expect(entryTotal(fx, 'debit')).toBe(1000)
  })

  it('does not fall back to the txn total when a posted entry genuinely totals zero', () => {
    // A `||` fallback would swap in the transaction-currency figure here and report a posted
    // entry as non-zero. The two fields are different currencies; only status may choose.
    const zeroPosted: JournalRow = { ...posted, totalDebit: 0, totalCredit: 0 }
    expect(entryTotal(zeroPosted, 'debit')).toBe(0)
    expect(entryTotal(zeroPosted, 'credit')).toBe(0)
  })

  it('reports a reversed entry from the base-currency total, like any posted entry', () => {
    const reversed: JournalRow = { ...posted, status: JournalEntryStatus.Reversed }
    expect(entryTotal(reversed, 'debit')).toBe(100)
  })
})
