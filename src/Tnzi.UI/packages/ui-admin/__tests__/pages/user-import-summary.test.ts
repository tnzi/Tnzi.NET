import { describe, it, expect } from 'vitest'
import type { UserImportResult } from '@tnzi/core/services/identity'
import { makePageTranslator } from '../../src/i18n/translate'
import { IMPORT_SUMMARY_MAX_ROWS, summarizeUserImport } from '../../src/pages/identity/user-import-summary'

/**
 * The CSV user import answers 200 with a per-row outcome even when rows were
 * refused. These pin what the admin is told: a clean import is a success line,
 * anything refused or skipped is a warning that names the rows to fix.
 */
const t = makePageTranslator('identity.users')

function result(overrides: Partial<UserImportResult>): UserImportResult {
  return { totalRows: 0, successCount: 0, failedCount: 0, skippedCount: 0, errors: {}, ...overrides }
}

describe('summarizeUserImport', () => {
  it('reports a clean import as a success', () => {
    const summary = summarizeUserImport(result({ totalRows: 3, successCount: 3 }), t)

    expect(summary.tone).toBe('success')
    expect(summary.text).toBe('Imported 3 users.')
  })

  it('warns with the counts and names each refused row with its reason', () => {
    const summary = summarizeUserImport(
      result({
        totalRows: 4,
        successCount: 1,
        failedCount: 2,
        skippedCount: 1,
        errors: { 4: 'Email and Password are required', 2: 'Passwords must be at least 8 characters.' },
      }),
      t,
    )

    expect(summary.tone).toBe('warning')
    expect(summary.text).toBe(
      'Imported 1 of 4 rows: 2 failed, 1 skipped (email already registered).\n'
        + 'Row 2: Passwords must be at least 8 characters.\nRow 4: Email and Password are required',
    )
  })

  it('warns when rows were only skipped, so a re-run that created nothing is not read as done', () => {
    const summary = summarizeUserImport(result({ totalRows: 2, skippedCount: 2 }), t)

    expect(summary.tone).toBe('warning')
    expect(summary.text).toBe('Imported 0 of 2 rows: 0 failed, 2 skipped (email already registered).')
  })

  it('orders rows numerically and counts the ones beyond the cap', () => {
    const errors: Record<number, string> = {}
    for (const row of [12, 3, 10, 2, 5, 7, 9]) errors[row] = 'x'

    const summary = summarizeUserImport(result({ totalRows: 12, successCount: 5, failedCount: 7, errors }), t)

    const named = [2, 3, 5, 7, 9].map((row) => `Row ${row}: x`).join('\n')
    expect(IMPORT_SUMMARY_MAX_ROWS).toBe(5)
    expect(summary.text).toBe(
      `Imported 5 of 12 rows: 7 failed, 0 skipped (email already registered).\n${named}\n2 more failed rows not shown`,
    )
  })
})
