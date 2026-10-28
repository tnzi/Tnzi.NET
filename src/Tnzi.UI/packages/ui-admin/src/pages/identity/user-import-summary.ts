import type { UserImportResult } from '@tnzi/core/services/identity'

/** How many refused rows the summary names; the rest are only counted. */
export const IMPORT_SUMMARY_MAX_ROWS = 5

export interface UserImportSummary {
  /** `success` when every row created an account, `warning` when any row was refused or skipped. */
  tone: 'success' | 'warning'
  text: string
}

type Translate = (key: string, params?: Record<string, unknown>) => string

/**
 * The line the admin sees after a CSV user import.
 *
 * ★ The import endpoint answers 200 with a per-row outcome even when every row
 * was refused. Read as "succeeded", an import of 200 rows where 60 failed the
 * password policy showed 140 new users and nothing else: nobody learned which
 * 60 were missing or why. The refused rows are named by their row in the file
 * (the header is row 1), lowest first, with the server's reason: the summary
 * line first, then one line per named row.
 */
export function summarizeUserImport(result: UserImportResult, t: Translate): UserImportSummary {
  const failed = result.failedCount ?? 0
  const skipped = result.skippedCount ?? 0
  const params = { success: result.successCount ?? 0, total: result.totalRows ?? 0, failed, skipped }
  if (failed === 0 && skipped === 0) {
    return { tone: 'success', text: t('import.done', params) }
  }

  // Row numbers are integer-like keys, which an object enumerates in ascending
  // numeric order, so the lowest rows are the ones named.
  const refused = Object.entries(result.errors ?? {}).map(([row, reason]) => ({ row: Number(row), reason }))
  const named = refused.slice(0, IMPORT_SUMMARY_MAX_ROWS).map((r) => t('import.row', r))
  const hidden = refused.length - named.length
  if (hidden > 0) named.push(t('import.moreRows', { count: hidden }))

  const text = [t('import.partial', params), ...named].join('\n')
  return { tone: 'warning', text }
}
