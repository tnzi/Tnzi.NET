import { describe, it, expect } from 'vitest'
import { filterByKeyword } from '../../../src/utils/keyword-filter'

// Lists the page loads whole (chart of accounts, fiscal years, tax agencies /
// rates / codes) come from endpoints that take no keyword, so the shell's
// search box filters the loaded set here. Without it the box was shown and
// silently ignored.

const rows = [
  { code: '1000', name: 'Assets', description: null as string | null },
  { code: '1200', name: 'Accounts Receivable', description: 'Trade debtors' },
  { code: '2000', name: 'Liabilities', description: undefined },
]
const fields = (r: (typeof rows)[number]) => [r.code, r.name, r.description]

describe('filterByKeyword', () => {
  it('returns every row for a blank keyword', () => {
    expect(filterByKeyword(rows, '', fields)).toEqual(rows)
    expect(filterByKeyword(rows, '   ', fields)).toEqual(rows)
    expect(filterByKeyword(rows, undefined, fields)).toEqual(rows)
  })

  it('matches any field, case-insensitively, ignoring surrounding spaces', () => {
    expect(filterByKeyword(rows, ' receiv ', fields).map((r) => r.code)).toEqual(['1200'])
    expect(filterByKeyword(rows, '1200', fields).map((r) => r.code)).toEqual(['1200'])
    expect(filterByKeyword(rows, 'DEBTORS', fields).map((r) => r.code)).toEqual(['1200'])
  })

  it('skips empty fields and returns nothing when nothing matches', () => {
    expect(filterByKeyword(rows, 'zzz', fields)).toEqual([])
  })

  it('does not hand back the input array', () => {
    expect(filterByKeyword(rows, '', fields)).not.toBe(rows)
  })
})
