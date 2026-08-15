import { EMPTY_DASH } from '../../../src/utils/placeholders'
import { describe, it, expect } from 'vitest'
import { FINANCE_SOURCE_TYPES } from '../../../src/services/bridges/finance-bridge'
import {
  FINANCE_SOURCE_TYPE_LABEL_KEYS,
  financeSourceTypeLabel,
} from '../../../src/pages/finance/source-type'

describe('finance source-type vocabulary', () => {
  it('labels every source token the framework can write', () => {
    // The backend freezes these tokens as literals so an entity rename cannot
    // change the wire value. This is the other half of that contract: a token
    // the framework writes but nobody labelled would reach an accountant as a
    // raw `PaymentApplication` cell, which is the defect this module exists to fix.
    const unlabelled = FINANCE_SOURCE_TYPES.filter((token) => !(token in FINANCE_SOURCE_TYPE_LABEL_KEYS))
    expect(unlabelled).toEqual([])
  })

  it('resolves a framework token to its label, not the raw token', () => {
    expect(financeSourceTypeLabel('PaymentApplication')).toBe('Settlement')
    expect(financeSourceTypeLabel('Revaluation')).toBe('FX Revaluation')
    expect(financeSourceTypeLabel('CreditMemo')).toBe('Credit Memo')
  })

  it('falls back to the raw token for an unknown source', () => {
    // Consuming apps post their own tokens through ILedgerPostingService; a
    // source the framework does not recognise is still a source the accountant
    // must see, so it must never be blanked or hidden.
    expect(financeSourceTypeLabel('SomeUnmappedSource')).toBe('SomeUnmappedSource')
  })

  it('shows the placeholder for a posting with no source (manual journal entry)', () => {
    expect(financeSourceTypeLabel(null)).toBe(EMPTY_DASH)
    expect(financeSourceTypeLabel(undefined)).toBe(EMPTY_DASH)
    expect(financeSourceTypeLabel('')).toBe(EMPTY_DASH)
  })
})
