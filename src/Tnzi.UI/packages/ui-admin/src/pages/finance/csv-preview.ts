/**
 * Client-side peek at a CSV statement so the import form can offer real column
 * names instead of asking someone to count commas.
 *
 * The server does the authoritative parse (`CsvStatementParser`); this only has
 * to be right enough to label a dropdown — and honest about quoting, since a
 * preview that split `"Smith, John"` into two columns would send the user to map
 * the wrong index.
 */

/** Split one CSV line, honouring double-quoted fields and the `""` escape inside them. */
export function splitCsvLine(line: string, delimiter: string): string[] {
  const out: string[] = []
  let field = ''
  let quoted = false
  for (let i = 0; i < line.length; i++) {
    const ch = line[i]
    if (quoted) {
      if (ch !== '"') {
        field += ch
        continue
      }
      if (line[i + 1] === '"') {
        field += '"'
        i++
        continue
      }
      quoted = false
      continue
    }
    if (ch === '"') {
      quoted = true
      continue
    }
    if (ch === delimiter) {
      out.push(field)
      field = ''
      continue
    }
    field += ch
  }
  out.push(field)
  return out.map((f) => f.trim())
}

export interface CsvPeek {
  /** Column labels: the header row when there is one, otherwise `Column 1`, `Column 2`, … */
  headers: string[]
  /** Up to `maxRows` data rows, for the preview table. */
  rows: string[][]
}

/**
 * Parse the head of a CSV file.
 *
 * Rows are split on newlines, which is wrong for a quoted field containing one —
 * rare in bank exports, and it only ever costs a cosmetically odd preview row
 * (the server's parser is the one that has to get it right).
 */
export function peekCsv(
  text: string,
  delimiter: string,
  hasHeader: boolean,
  skipRows = 0,
  maxRows = 5,
): CsvPeek {
  const lines = text.split(/\r\n|\n|\r/).filter((l) => l.trim().length > 0)
  if (lines.length === 0) return { headers: [], rows: [] }

  const headerLine = hasHeader ? lines[0] : null
  const from = (hasHeader ? 1 : 0) + Math.max(0, skipRows)
  const rows = lines.slice(from, from + maxRows).map((l) => splitCsvLine(l, delimiter))

  const width = Math.max(
    headerLine ? splitCsvLine(headerLine, delimiter).length : 0,
    ...rows.map((r) => r.length),
    0,
  )
  const headers = headerLine
    ? splitCsvLine(headerLine, delimiter)
    : Array.from({ length: width }, (_, i) => `Column ${i + 1}`)

  return { headers, rows }
}

/** A guessed mapping. `null` = not found; the user picks it from the dropdown. */
export interface GuessedColumns {
  date: number | null
  description: number | null
  reference: number | null
  /** Signed single-column shape. Mutually exclusive with `debit`/`credit`. */
  amount: number | null
  debit: number | null
  credit: number | null
}

/**
 * Best-effort first guess at which column is which, by header name.
 *
 * Wrong guesses are free — the preview shows them and the user corrects them
 * from the dropdown; the point is that the common exports need no edits.
 *
 * Two shapes exist in the wild and they are mutually exclusive: one signed
 * `Amount` column, or a `Withdrawal`/`Deposit` pair. The pair is resolved first
 * and its columns are excluded from the `Amount` search on purpose — exports
 * label the pair `Withdrawal Amount` / `Deposit Amount` often enough that a bare
 * `includes('amount')` would otherwise map the withdrawal column as the signed
 * amount and import every deposit as a withdrawal.
 */
export function guessColumns(headers: string[]): GuessedColumns {
  const findFrom = (exclude: Array<number | null>, ...needles: string[]): number | null => {
    const i = headers.findIndex(
      (h, index) => !exclude.includes(index) && needles.some((n) => h.toLowerCase().includes(n)),
    )
    return i < 0 ? null : i
  }
  const find = (...needles: string[]) => findFrom([], ...needles)

  const debit = find('withdrawal', 'debit', 'paid out', 'money out')
  const credit = find('deposit', 'credit', 'paid in', 'money in')
  const amount = findFrom([debit, credit], 'amount')

  return {
    date: find('date', 'posted'),
    description: find('description', 'details', 'narrative', 'memo', 'payee'),
    reference: find('reference', 'cheque', 'check', 'ref no', 'serial'),
    // A signed Amount column wins when one exists; otherwise fall back to the pair.
    amount,
    debit: amount == null ? debit : null,
    credit: amount == null ? credit : null,
  }
}
