import { describe, expect, it } from 'vitest'
import { parseStatementCsv, extractTransactionsFromText } from '@/import/statementParser'

describe('statement CSV parsing, in the browser', () => {
  it('reads a simple bank export', async () => {
    const csv = [
      'Date,Description,Amount',
      '2026-01-05,METRO PLUS MARCHE,84.32',
      '2026-01-07,UBER EATS TORONTO,42.50',
    ].join('\n')

    const result = await parseStatementCsv(csv)

    expect(result.rows).toHaveLength(2)
    expect(result.rows[0].description).toBe('METRO PLUS MARCHE')
    expect(result.rows[0].amount).toBe(84.32)
  })

  it('detects the columns from the header', async () => {
    const csv = 'Transaction Date,Merchant,Debit\n2026-01-05,METRO,10.00'

    const result = await parseStatementCsv(csv)

    expect(result.mapping.date).toBe(0)
    expect(result.mapping.description).toBe(1)
    expect(result.mapping.amount).toBe(2)
  })

  it('handles a semicolon-delimited European export', async () => {
    const csv = 'Datum;Beschreibung;Betrag\n05.01.2026;REWE MARKT;84,32'

    const result = await parseStatementCsv(csv)

    expect(result.rows).toHaveLength(1)
    expect(result.rows[0].amount).toBe(84.32)
    expect(result.rows[0].date?.getUTCMonth()).toBe(0)
  })

  it('handles separate debit and credit columns', async () => {
    const csv = [
      'Date,Description,Debit,Credit',
      '2026-01-05,METRO,84.32,',
      '2026-01-06,REFUND,,20.00',
    ].join('\n')

    const result = await parseStatementCsv(csv)

    expect(result.rows[0].amount).toBe(84.32)
    // A credit is money coming back, so it reads as negative.
    expect(result.rows[1].amount).toBe(-20)
  })

  it('skips a header-only file', async () => {
    const result = await parseStatementCsv('Date,Description,Amount')

    expect(result.rows).toHaveLength(0)
  })

  it('flags a row it cannot read rather than dropping it silently', async () => {
    const csv = [
      'Date,Description,Amount',
      '2026-01-05,GOOD ROW,10.00',
      'not-a-date,BAD DATE,10.00',
      '2026-01-06,BAD AMOUNT,abc',
    ].join('\n')

    const result = await parseStatementCsv(csv)

    expect(result.rows).toHaveLength(3)
    expect(result.rows.filter((r) => r.problems.length > 0)).toHaveLength(2)
  })

  it('ignores a blank line', async () => {
    const csv = 'Date,Description,Amount\n2026-01-05,METRO,10.00\n\n'

    const result = await parseStatementCsv(csv)

    expect(result.rows).toHaveLength(1)
  })

  it('reports the currency when the export names one', async () => {
    const csv = 'Date,Description,Amount,Currency\n2026-01-05,METRO,10.00,USD'

    const result = await parseStatementCsv(csv)

    expect(result.rows[0].currency).toBe('USD')
  })

  it('rejects a file that is not a CSV at all', async () => {
    await expect(parseStatementCsv('')).rejects.toThrow()
  })
})

describe('statement text extraction', () => {
  it('pulls transactions out of a PDF text layer', () => {
    const text = [
      'CHEQUING ACCOUNT STATEMENT',
      'Jan 05  METRO PLUS MARCHE                        84.32',
      'Jan 07  UBER EATS TORONTO ON                     42.50',
      'Jan 12  PAYMENT RECEIVED                        500.00-',
      'Closing balance                                 1234.56',
    ].join('\n')

    const rows = extractTransactionsFromText(text, 2026)

    expect(rows.length).toBeGreaterThanOrEqual(3)
    expect(rows[0].description).toContain('METRO PLUS MARCHE')
    expect(rows[0].amount).toBe(84.32)
  })

  it('reads a trailing minus as a credit', () => {
    const rows = extractTransactionsFromText('Jan 12  PAYMENT RECEIVED  500.00-', 2026)

    expect(rows[0].amount).toBe(-500)
  })

  it('handles an ISO date layout', () => {
    const rows = extractTransactionsFromText('2026-01-05  METRO PLUS  84.32', 2026)

    expect(rows[0].date?.getUTCFullYear()).toBe(2026)
    expect(rows[0].amount).toBe(84.32)
  })

  it('handles a slash date layout', () => {
    const rows = extractTransactionsFromText('05/01/2026  METRO PLUS  84.32', 2026)

    expect(rows[0].date).not.toBeNull()
  })

  it('skips lines with no amount', () => {
    const rows = extractTransactionsFromText('CHEQUING ACCOUNT STATEMENT\nPage 1 of 3', 2026)

    expect(rows).toHaveLength(0)
  })

  it('skips a balance line, which is not a transaction', () => {
    const rows = extractTransactionsFromText(
      'Jan 05  METRO PLUS  84.32\nClosing balance  1234.56\nOpening balance  1000.00',
      2026,
    )

    expect(rows).toHaveLength(1)
  })

  it('skips a totals line', () => {
    const rows = extractTransactionsFromText(
      'Jan 05  METRO  84.32\nTotal purchases  84.32',
      2026,
    )

    expect(rows.map((r) => r.description)).toEqual(['METRO'])
  })

  it('carries a year over a December to January boundary', () => {
    const rows = extractTransactionsFromText(
      'Dec 28  METRO  10.00\nJan 03  LOBLAWS  20.00',
      2026,
    )

    // A statement spanning the new year prints no year; December belongs to the
    // earlier one or the transaction lands eleven months late.
    expect(rows[0].date?.getUTCFullYear()).toBe(2025)
    expect(rows[1].date?.getUTCFullYear()).toBe(2026)
  })

  it('returns nothing for empty text, so the caller can fall back to OCR', () => {
    expect(extractTransactionsFromText('', 2026)).toHaveLength(0)
    expect(extractTransactionsFromText('   \n  \n', 2026)).toHaveLength(0)
  })

  it('keeps the raw line for the review table', () => {
    const rows = extractTransactionsFromText('Jan 05  METRO PLUS  84.32', 2026)

    expect(rows[0].rawLine).toContain('METRO PLUS')
  })
})
