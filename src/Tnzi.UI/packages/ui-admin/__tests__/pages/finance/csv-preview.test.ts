import { describe, it, expect } from 'vitest'
import { splitCsvLine, peekCsv, guessColumns } from '../../../src/pages/finance/csv-preview'

describe('splitCsvLine', () => {
  it('keeps a quoted field containing the delimiter in one piece', () => {
    // The reason the preview cannot just `line.split(',')`: this would otherwise
    // show 5 columns and send the user to map every index one to the right.
    expect(splitCsvLine('2026-03-05,"Smith, John",500.00', ',')).toEqual([
      '2026-03-05',
      'Smith, John',
      '500.00',
    ])
  })

  it('unescapes a doubled quote inside a quoted field', () => {
    expect(splitCsvLine('a,"He said ""hi""",b', ',')).toEqual(['a', 'He said "hi"', 'b'])
  })

  it('honours a non-comma delimiter and preserves empty fields', () => {
    expect(splitCsvLine('a;;c', ';')).toEqual(['a', '', 'c'])
  })
})

describe('peekCsv', () => {
  const rbc = [
    'Account Type,Account Number,Transaction Date,Cheque Number,Description,Withdrawal Amount,Deposit Amount',
    'Chequing,00001,2026-03-05,,"PAYROLL, ACME INC",,2500.00',
    'Chequing,00001,2026-03-06,102,HYDRO BILL,145.20,',
  ].join('\n')

  it('uses the header row as column labels and returns the data rows', () => {
    const peek = peekCsv(rbc, ',', true)
    expect(peek.headers[2]).toBe('Transaction Date')
    expect(peek.rows).toHaveLength(2)
    // The quoted comma survived into the preview cell.
    expect(peek.rows[0][4]).toBe('PAYROLL, ACME INC')
  })

  it('synthesises column names when the file has no header row', () => {
    const peek = peekCsv('2026-03-05,Deposit,500.00', ',', false)
    expect(peek.headers).toEqual(['Column 1', 'Column 2', 'Column 3'])
    expect(peek.rows).toHaveLength(1)
  })

  it('skips leading rows after the header', () => {
    const peek = peekCsv(rbc, ',', true, 1)
    expect(peek.rows).toHaveLength(1)
    expect(peek.rows[0][4]).toBe('HYDRO BILL')
  })

  it('caps the preview at maxRows', () => {
    const many = ['h', ...Array.from({ length: 20 }, (_, i) => `r${i}`)].join('\n')
    expect(peekCsv(many, ',', true, 0, 5).rows).toHaveLength(5)
  })

  it('returns nothing for an empty file rather than throwing', () => {
    expect(peekCsv('', ',', true)).toEqual({ headers: [], rows: [] })
    expect(peekCsv('   \n\n', ',', true)).toEqual({ headers: [], rows: [] })
  })

  it('shows a wrong delimiter as one fat column instead of importing junk', () => {
    const peek = peekCsv('a,b,c\n1,2,3', ';', true)
    expect(peek.headers).toEqual(['a,b,c'])
  })
})

describe('guessColumns', () => {
  it('maps a signed single-amount export with no edits', () => {
    const guess = guessColumns(['Date', 'Description', 'Reference', 'Amount'])
    expect(guess).toMatchObject({ date: 0, description: 1, reference: 2, amount: 3, debit: null, credit: null })
  })

  it('picks the debit/credit pair shape when there is no signed amount column', () => {
    const guess = guessColumns(['Posted', 'Details', 'Withdrawal', 'Deposit'])
    expect(guess).toMatchObject({ date: 0, description: 1, debit: 2, credit: 3, amount: null })
  })

  it('does not mistake "Withdrawal Amount" for the signed amount column', () => {
    // The trap: a bare `includes('amount')` matches "Withdrawal Amount" first, so
    // the mapping would send every deposit in as a withdrawal. The pair must win.
    const guess = guessColumns([
      'Account Type', 'Account Number', 'Transaction Date', 'Cheque Number',
      'Description', 'Withdrawal Amount', 'Deposit Amount',
    ])
    expect(guess.amount).toBeNull()
    expect(guess.debit).toBe(5)
    expect(guess.credit).toBe(6)
    expect(guess.date).toBe(2)
    expect(guess.reference).toBe(3)
    expect(guess.description).toBe(4)
  })

  it('leaves a column it cannot find unmapped rather than guessing wrong', () => {
    const guess = guessColumns(['Foo', 'Bar'])
    expect(guess).toEqual({ date: null, description: null, reference: null, amount: null, debit: null, credit: null })
  })

  it('matches header names case-insensitively', () => {
    expect(guessColumns(['DATE', 'MEMO', 'AMOUNT'])).toMatchObject({ date: 0, description: 1, amount: 2 })
  })
})
